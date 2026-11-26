import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { JournalActionsService } from '../../../core/services/journal-actions.service';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import { forkJoin, of } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { PresenceService } from '../../../core/services/presence.service';
import { PresenceSuiviService } from '../../../core/services/presence-suivi.service';
import type { AvertissementDto, EvpDto, PresenceAgentDto, PresenceGrilleDto } from '../../../core/models/presence.models';
import { KyntusToastService } from '../../../shared/components/ui/kyntus-toast.service';
import { PresencesNavComponent } from '../components/presences-nav.component';
import {
  CODES_DEDUITS,
  CODES_NON_DEDUITS,
  COULEURS_FORMATION,
  type LignePaie,
  joursAvantMois,
  joursOuvres,
  lignePaie,
  moisCivilDePeriode,
  nomsExport,
  telechargerExportPaie,
} from '../lib/paie.util';
import { type ContextePenalites, type PeriodeAgents, penalitesAgent } from '../lib/manquements.util';
import { ActiviteStructure, enteteJour, estEncadrementNonPlace, familleCode, optionsPerimetre, periodePaie, ymd } from '../lib/presence.util';
import { DirectoryFacadeService } from '../../../core/directory/directory-facade.service';
import { dateFr, messageErreur, montantFr, sansAccents } from '../lib/suivi.util';
import { normaliserTypeFormation } from '../../../core/hr/type-formation.util';
import { KyntusSessionService } from '../../../core/session/kyntus-session.service';
import type { PaieValidationDto } from '../../../core/models/presence.models';

interface Activite { id: string; name: string; kind: string; isActive: boolean; poleIds: string[] }
type Mode = 'paie' | 'mois';
type Tri = 'affectation' | 'pilote' | 'absences' | 'retards' | 'evp';

/**
 * Export paie : aperçu vérifiable puis fichier Excel identique au « recap_absences » actuel
 * (mêmes colonnes, même ordre, mêmes règles de calcul).
 */
@Component({
  selector: 'app-presences-export',
  standalone: true,
  imports: [FormsModule, RouterLink, PresencesNavComponent],
  templateUrl: './presences-export.component.html',
  styleUrls: ['../presences-commun.css', './presences-tableau.component.css', './presences-export.component.css'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PresencesExportComponent implements OnInit {
  private readonly presences = inject(PresenceService);
  private readonly suivi = inject(PresenceSuiviService);
  private readonly http = inject(HttpClient);
  private readonly annuaire = inject(DirectoryFacadeService);
  private readonly toast = inject(KyntusToastService);
  private readonly session = inject(KyntusSessionService);
  private readonly journalActions = inject(JournalActionsService);

  readonly codesDeduits = CODES_DEDUITS;
  readonly codesNonDeduits = CODES_NON_DEDUITS;
  readonly familleCode = familleCode;
  readonly enteteJour = enteteJour;
  readonly dateFr = dateFr;
  readonly montantFr = montantFr;

  readonly mode = signal<Mode>('paie');
  readonly decalage = signal(0);
  readonly periode = computed(() => this.periodeDe(this.decalage()));
  readonly grille = signal<PresenceGrilleDto | null>(null);
  /**
   * v61 — Mois civil + décalage (mode paie) : colonnes du 1er à la fin du mois ; absences, EVP,
   * retards et heures supplémentaires sur la période 24 → 23 (grille ci-dessus, inchangée).
   */
  readonly mois = computed(() => (this.mode() === 'paie' ? moisCivilDePeriode(this.periode().au) : null));
  readonly grilleMois = signal<PresenceGrilleDto | null>(null);
  /** v30 : manquements saisis (période et deux précédentes) ; null si illisibles. */
  readonly saisies = signal<AvertissementDto[] | null>(null);
  /** v30 : grilles des deux périodes précédentes (rang « mois consécutif » des retards). */
  readonly precedents = signal<[PeriodeAgents | null, PeriodeAgents | null]>([null, null]);
  readonly evp = signal<EvpDto[]>([]);
  readonly evpIndisponible = signal(false);
  readonly activites = signal<Activite[]>([]);
  /** v48.1 : pôles et cellules déclarés dans l'organisation (filtres même sans agent). */
  readonly structure = signal<ActiviteStructure[]>([]);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly exportEnCours = signal(false);

  // v58 — Validation du tableau de paie : réservée au RH, visible de tous.
  readonly validation = signal<PaieValidationDto | null>(null);
  readonly validationEnCours = signal(false);
  readonly estRh = computed(() => {
    const role = (this.session.getRole() ?? '').trim().toLowerCase();
    return role === 'rh' || role === 'admin';
  });
  readonly verrouille = computed(() => this.validation()?.validee === true);

  readonly recherche = signal('');
  readonly activite = signal('');
  readonly pole = signal('');
  readonly cellule = signal('');
  readonly contrat = signal('');
  readonly alertesSeulement = signal(false);
  /** v37 : '' tous ; 'formation' = en formation ou intégrés ; sinon un état précis. */
  // v48 : « contractuel » / « non-contractuel » = type de formation choisi sur la fiche.
  readonly formation = signal<'' | 'formation' | 'en-formation' | 'integre' | 'production' | 'contractuel' | 'non-contractuel'>('');
  readonly couleursFormation = COULEURS_FORMATION;
  readonly voirJours = signal(true);
  readonly tri = signal<Tri>('affectation');

  readonly jours = computed(() => {
    const g = this.grille();
    const m = this.mois();
    if (!g) return [];
    return m ? joursOuvres(m.du, m.au) : joursOuvres(g.du.slice(0, 10), g.au.slice(0, 10));
  });

  /**
   * v69 — Mode paie : jours du 24 à la fin du mois précédent, affichés avant les jours du mois pour
   * vérifier les absences déduites de la période 24 → 23 (aucun autre calcul).
   */
  readonly joursAvant = computed(() => {
    const g = this.grille();
    const m = this.mois();
    return g && m ? joursAvantMois(g.du.slice(0, 10), m) : [];
  });

  private readonly activiteParPole = computed(() => {
    const m = new Map<string, string>();
    for (const a of this.activites()) {
      if (!a.isActive || (a.kind ?? '').toLowerCase() !== 'operational') continue;
      for (const p of a.poleIds ?? []) m.set(p, a.name);
    }
    return m;
  });
  /** v46 : « Formation & Qualité » (nom du contrat s'il existe). */
  private readonly activiteFormation = computed(() =>
    this.activites().find((a) => a.isActive && /formation/i.test(a.name))?.name ?? 'Formation & Qualité');
  private activiteDe(l: LignePaie): string {
    // v46 : un agent en formation sans pôle relève de Formation & Qualité.
    return (l.poleId && this.activiteParPole().get(l.poleId))
      || (l.etatFormation === 'en-formation' ? this.activiteFormation() : '');
  }

  readonly toutes = computed(() => {
    const g = this.grille();
    if (!g) return [];
    const du = g.du.slice(0, 10);
    const au = g.au.slice(0, 10);
    const ctx: ContextePenalites = { du, au, saisies: this.saisies(), precedents: this.precedents() };
    const mois = this.mois();
    // v62 : toute l'entreprise : les superviseurs / référents sans cellule (ajoutés pour leurs tableaux
    // dédiés du tableau des présences) ne changent pas l'export paie.
    const agentsPaie = g.toutesActivites ? g.agents.filter((a) => !estEncadrementNonPlace(a)) : g.agents;
    if (!mois) return agentsPaie.map((a) => lignePaie(a, this.evp(), du, au, penalitesAgent(a, ctx)));
    // v61 : un agent entré après le 23 n'apparaît que sur la grille du mois : il est ajouté (sans doublon).
    const parMois = new Map<number, PresenceAgentDto>((this.grilleMois()?.agents ?? []).map((a) => [a.userId, a]));
    const agents = [...agentsPaie, ...[...parMois.values()]
      .filter((a) => !(g.toutesActivites && estEncadrementNonPlace(a)))
      .filter((a) => !g.agents.some((x) => x.userId === a.userId))];
    return agents.map((a) => lignePaie(a, this.evp(), du, au, penalitesAgent(a, ctx), { mois, agentMois: parMois.get(a.userId) ?? null }));
  });

  readonly options = computed(() => {
    const uniq = (xs: string[]) => [...new Set(xs.filter(Boolean))].sort((a, b) => a.localeCompare(b, 'fr', { sensitivity: 'base' }));
    const l = this.toutes();
    const dansActivite = l.filter((x) => !this.activite() || this.activiteDe(x) === this.activite());
    const dansPole = dansActivite.filter((x) => !this.pole() || x.pole === this.pole());
    // v48.1 : complétées par l'organisation (activité sans agent : Formation & Qualité).
    // v58 : mêmes listes que le tableau des présences — sans doublon et conformes à l'organisation.
    // v61 : un RP (périmètre limité) ne voit que les contrats, pôles et cellules de son périmètre.
    const toutes = this.grille()?.toutesActivites ?? true;
    const listes = optionsPerimetre(toutes, this.structure(), this.activite(), this.pole(), {
      activites: [...l.map((x) => this.activiteDe(x)), ...(toutes ? [this.activiteFormation()] : [])],
      poles: dansActivite.map((x) => x.pole),
      cellules: dansPole.map((x) => x.cellule),
    });
    return {
      activites: listes.activites,
      poles: listes.poles,
      cellules: listes.cellules,
    };
  });

  readonly lignes = computed(() => {
    const q = sansAccents(this.recherche().trim());
    const filtrees = this.toutes().filter((l) =>
      (!q || sansAccents(`${l.pilote} ${l.affectation}`).includes(q))
      && (!this.activite() || this.activiteDe(l) === this.activite())
      && (!this.pole() || l.pole === this.pole())
      && (!this.cellule() || l.cellule === this.cellule())
      && (!this.contrat() || l.type === this.contrat())
      && (!this.alertesSeulement() || l.alertes.length > 0)
      && this.correspondFormation(l));
    const cmp: Record<Tri, (a: LignePaie, b: LignePaie) => number> = {
      affectation: (a, b) => a.affectation.localeCompare(b.affectation, 'fr') || a.pilote.localeCompare(b.pilote, 'fr'),
      pilote: (a, b) => a.pilote.localeCompare(b.pilote, 'fr'),
      absences: (a, b) => b.totalAbsences - a.totalAbsences,
      retards: (a, b) => b.retardMinutes - a.retardMinutes,
      evp: (a, b) => Math.abs(b.totalEvp) - Math.abs(a.totalEvp),
    };
    return filtrees.sort(cmp[this.tri()]);
  });

  private correspondFormation(l: LignePaie): boolean {
    const f = this.formation();
    if (!f) return true;
    if (f === 'formation') return l.etatFormation !== 'production';
    if (f === 'contractuel' || f === 'non-contractuel') {
      return normaliserTypeFormation(l.typeFormation) === f;
    }
    return l.etatFormation === f;
  }

  readonly synthese = computed(() => {
    const l = this.lignes();
    const somme = (f: (x: LignePaie) => number) => l.reduce((s, x) => s + f(x), 0);
    return {
      agents: l.length,
      jrsTravailles: somme((x) => x.jrsTravailles),
      absences: somme((x) => x.totalAbsences),
      penalites: somme((x) => x.penalite),
      agentsPenalises: l.filter((x) => x.penalite < 0).length,
      horsPaie: l.filter((x) => x.joursProduction === 0).length,
      enFormation: l.filter((x) => x.formation > 0).length,
      horsPaieFormation: l.filter((x) => x.etatFormation === 'en-formation').length,
      integres: l.filter((x) => x.etatFormation === 'integre').length,
      evp: somme((x) => x.totalEvp),
      hs: Math.round(somme((x) => x.hsTotal) * 100) / 100,
      alertes: l.filter((x) => x.alertes.length > 0).length,
    };
  });

  /** Vérifications avant export, sur les lignes filtrées. */
  readonly verifications = computed(() => {
    const l = this.lignes();
    const compte = (f: (x: LignePaie) => boolean) => l.filter(f).length;
    return [
      { libelle: 'ABS N à confirmer (jour planifié sans pointage)', n: compte((x) => x.deduits['ABS N'] > 0), lien: '/presences' },
      { libelle: 'Pointages manquants', n: compte((x) => x.manques > 0), lien: '/presences' },
      { libelle: 'EVP « à valoriser » (montant fixé par la paie)', n: compte((x) => x.alertes.includes('EVP à valoriser')), lien: '/presences/evp' },
      { libelle: 'Sorties pendant la période', n: compte((x) => !!x.sortie), lien: null },
      { libelle: 'Agents en formation (hors paie ou au prorata)', n: compte((x) => x.formation > 0), lien: null },
      { libelle: 'Manquements disciplinaires (G02 / G03, sans montant)', n: compte((x) => x.alertes.some((a) => a.includes('disciplinaire'))), lien: '/presences/avertissements' },
      { libelle: 'Agents sans affectation', n: compte((x) => !x.affectation), lien: null },
    ];
  });

  readonly nomFichier = computed(() => {
    const g = this.grille();
    return g ? nomsExport(g.du.slice(0, 10), g.au.slice(0, 10), this.mois() ?? undefined).fichier : '';
  });

  ngOnInit(): void {
    this.load();
    this.http.get<Activite[]>('/api/directory/business-departments').subscribe({
      next: (a) => this.activites.set(a ?? []),
      error: () => this.activites.set([]),
    });
    this.annuaire.getOrgOverview<{ operationalDepartments?: ActiviteStructure[] }>().subscribe({
      next: (o) => this.structure.set(o?.operationalDepartments ?? []),
      error: () => this.structure.set([]),
    });
  }

  choisirMode(m: Mode): void {
    this.mode.set(m);
    this.decalage.set(0);
    this.load();
  }

  changerPeriode(delta: number): void {
    this.decalage.set(delta === 0 ? 0 : this.decalage() + delta);
    this.load();
  }

  /** Période affichée, ou une période précédente (decalage − 1, − 2) pour le rang des manquements. */
  private periodeDe(decalage: number): { du: string; au: string; libelle: string } {
    if (this.mode() === 'paie') return periodePaie(new Date(), decalage);
    const now = new Date();
    const debut = new Date(now.getFullYear(), now.getMonth() + decalage, 1);
    const fin = new Date(debut.getFullYear(), debut.getMonth() + 1, 0);
    return { du: ymd(debut), au: ymd(fin), libelle: debut.toLocaleDateString('fr-FR', { month: 'long', year: 'numeric' }) };
  }

  load(): void {
    const { du, au } = this.periode();
    const mois = this.mois();
    const p1 = this.periodeDe(this.decalage() - 1);
    const p2 = this.periodeDe(this.decalage() - 2);
    const versPeriode = (g: PresenceGrilleDto | null, p: { du: string; au: string }): PeriodeAgents | null =>
      g ? { du: p.du, au: p.au, agents: new Map<number, PresenceAgentDto>(g.agents.map((a) => [a.userId, a])) } : null;
    this.loading.set(true);
    this.error.set(null);
    forkJoin({
      grille: this.presences.grille(du, au),
      evp: this.suivi.evp(du, au).pipe(catchError(() => of(null))),
      saisies: this.suivi.avertissements(p2.du, au).pipe(catchError(() => of(null))),
      prec1: this.presences.grille(p1.du, p1.au).pipe(catchError(() => of(null))),
      prec2: this.presences.grille(p2.du, p2.au).pipe(catchError(() => of(null))),
      validation: this.presences.validationPaie(du, au).pipe(catchError(() => of(null))),
      grilleMois: mois ? this.presences.grille(mois.du, mois.au).pipe(catchError(() => of(null))) : of(null),
    }).subscribe({
      next: ({ grille, evp, saisies, prec1, prec2, validation, grilleMois }) => {
        this.validation.set(validation);
        this.grille.set(grille);
        this.grilleMois.set(grilleMois);
        this.evp.set(evp ?? []);
        this.evpIndisponible.set(evp === null);
        this.saisies.set(saisies);
        this.precedents.set([versPeriode(prec1, p1), versPeriode(prec2, p2)]);
        this.loading.set(false);
      },
      error: (err) => {
        this.loading.set(false);
        this.error.set(messageErreur(err, 'Impossible de préparer l’export paie.'));
      },
    });
  }

  /** v58 — Valide le tableau : RH uniquement, et la période devient non modifiable. */
  validerPaie(): void {
    if (!this.estRh() || this.validationEnCours()) return;
    const { du, au, libelle } = this.periode();
    if (!confirm(
      `Valider le tableau de paie de ${libelle} ?\n\n`
      + 'La période sera verrouillée : plus aucune présence ni aucun montant ne pourra y être '
      + 'modifié tant que les RH ne la rouvrent pas. Votre nom et la date seront affichés.',
    )) return;

    this.validationEnCours.set(true);
    this.presences.validerPaie(du, au).subscribe({
      next: (v) => {
        this.validation.set(v);
        this.validationEnCours.set(false);
        this.toast.success('Tableau de paie validé.');
      },
      error: (err) => {
        this.validationEnCours.set(false);
        this.toast.error(messageErreur(err, 'Validation impossible.'));
      },
    });
  }

  /** v58 — Rouvre la période pour correction : RH uniquement, action tracée. */
  rouvrirPaie(): void {
    if (!this.estRh() || this.validationEnCours()) return;
    const { du, au, libelle } = this.periode();
    if (!confirm(`Rouvrir le tableau de paie de ${libelle} ? Les corrections redeviendront possibles.`)) return;

    this.validationEnCours.set(true);
    this.presences.rouvrirPaie(du, au).subscribe({
      next: (v) => {
        this.validation.set(v);
        this.validationEnCours.set(false);
        this.toast.success('Période rouverte.');
      },
      error: (err) => {
        this.validationEnCours.set(false);
        this.toast.error(messageErreur(err, 'Réouverture impossible.'));
      },
    });
  }

  choisirActivite(v: string): void { this.activite.set(v); this.pole.set(''); this.cellule.set(''); }
  choisirPole(v: string): void { this.pole.set(v); this.cellule.set(''); }

  reinitialiser(): void {
    this.recherche.set('');
    this.activite.set('');
    this.pole.set('');
    this.cellule.set('');
    this.contrat.set('');
    this.alertesSeulement.set(false);
    this.formation.set('');
  }

  async exporter(): Promise<void> {
    const g = this.grille();
    const lignes = this.lignes();
    if (!g || !lignes.length || this.exportEnCours()) return;
    this.exportEnCours.set(true);
    try {
      await telechargerExportPaie(lignes, g.du.slice(0, 10), g.au.slice(0, 10), this.mois() ?? undefined);
      this.toast.success(`Export paie : ${lignes.length} ligne(s).`);
      // v64 : journal d'actions.
      this.journalActions.declarerExport(
        `Export paie du ${g.du.slice(0, 10).split('-').reverse().join('/')} au ${g.au.slice(0, 10).split('-').reverse().join('/')}`,
        `${lignes.length} ligne(s)`,
      );
    } catch {
      this.toast.error('Génération du fichier impossible.');
    } finally {
      this.exportEnCours.set(false);
    }
  }
}
