import { ChangeDetectionStrategy, Component, ElementRef, OnInit, ViewChild, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { PresenceService } from '../../../core/services/presence.service';
import type {
  PresenceAgentDto,
  PresenceCodeDto,
  PresenceGrilleDto,
  PresenceJourDto,
} from '../../../core/models/presence.models';
import { PresencesNavComponent } from '../components/presences-nav.component';
import { KyntusToastService } from '../../../shared/components/ui/kyntus-toast.service';
import {
  FILTRES_VIDES,
  type FiltresPresence,
  enteteJour,
  familleCode,
  filtrerAgents,
  formatMinutes,
  joursDeLaPeriode,
  periodePaie,
  ymd,
} from '../lib/presence.util';

interface Activite { id: string; name: string; kind: string; isActive: boolean; poleIds: string[] }

/** Formulaire de la fenêtre « Journée ». */
interface Edition {
  agent: PresenceAgentDto;
  jour: PresenceJourDto;
  mode: 'auto' | 'present' | 'absent' | 'formation';
  motif: string;
  periode: boolean;
  au: string;
  retardAutorise: boolean;
  retardPauseAutorise: boolean;
  motifAutorisation: string;
  commentaire: string;
  absencePartielle: boolean;
}

/**
 * Tableau des présences : une ligne par agent du périmètre, une colonne par jour de la période de
 * paie (24 → 23). Chaque case vient du pointage ; le responsable qualifie les absences, autorise
 * les retards et commente.
 */
@Component({
  selector: 'app-presences-tableau',
  standalone: true,
  imports: [CommonModule, FormsModule, PresencesNavComponent],
  templateUrl: './presences-tableau.component.html',
  styleUrls: ['../presences-commun.css', './presences-tableau.component.css'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PresencesTableauComponent implements OnInit {
  private readonly svc = inject(PresenceService);
  private readonly http = inject(HttpClient);
  private readonly toast = inject(KyntusToastService);

  readonly enteteJour = enteteJour;
  readonly familleCode = familleCode;
  readonly formatMinutes = formatMinutes;
  readonly aujourdhui = ymd(new Date());

  readonly decalage = signal(0);
  readonly periode = computed(() => periodePaie(new Date(), this.decalage()));
  readonly data = signal<PresenceGrilleDto | null>(null);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly filtres = signal<FiltresPresence>({ ...FILTRES_VIDES });
  readonly activites = signal<Activite[]>([]);
  readonly edition = signal<Edition | null>(null);
  readonly enregistrement = signal(false);
  @ViewChild('panneau') private panneau?: ElementRef<HTMLDialogElement>;

  readonly jours = computed(() => {
    const d = this.data();
    return d ? joursDeLaPeriode(d.du.slice(0, 10), d.au.slice(0, 10)) : [];
  });
  readonly codesAbsence = computed(() =>
    (this.data()?.codes ?? []).filter((c) => c.famille === 'absence' || c.famille === 'conge'));
  readonly libelles = computed(() => new Map((this.data()?.codes ?? []).map((c) => [c.code, c.libelle])));

  private readonly activiteParPole = computed(() => {
    const m = new Map<string, string>();
    for (const a of this.activites()) {
      if (!a.isActive || (a.kind ?? '').toLowerCase() !== 'operational') continue;
      for (const p of a.poleIds ?? []) m.set(p, a.name);
    }
    return m;
  });
  readonly activiteDuPole = (poleId: string | null): string | null =>
    poleId ? this.activiteParPole().get(poleId) ?? null : null;

  readonly options = computed(() => {
    const agents = this.data()?.agents ?? [];
    const uniq = (xs: (string | null)[]) =>
      [...new Set(xs.filter((x): x is string => !!x))].sort((a, b) => a.localeCompare(b, 'fr', { sensitivity: 'base' }));
    const f = this.filtres();
    const dansActivite = agents.filter((a) => !f.activite || this.activiteDuPole(a.poleId) === f.activite);
    const dansPole = dansActivite.filter((a) => !f.pole || a.pole === f.pole);
    return {
      activites: uniq(agents.map((a) => this.activiteDuPole(a.poleId))),
      poles: uniq(dansActivite.map((a) => a.pole)),
      cellules: uniq(dansPole.map((a) => a.cellule)),
      types: uniq(agents.map((a) => a.type)),
    };
  });

  readonly agents = computed(() => filtrerAgents(this.data()?.agents ?? [], this.filtres(), this.activiteDuPole));

  readonly synthese = computed(() => {
    const l = this.agents();
    return {
      agents: l.length,
      presents: l.reduce((n, a) => n + a.totaux.presents, 0),
      aQualifier: l.reduce((n, a) => n + a.totaux.absencesNonQualifiees, 0),
      absences: l.reduce((n, a) => n + Object.values(a.totaux.absences).reduce((s, v) => s + v, 0), 0),
      retards: l.reduce((n, a) => n + a.totaux.retardRetenuMinutes, 0),
      autorises: l.reduce((n, a) => n + a.totaux.retardAutoriseMinutes, 0),
      manques: l.reduce((n, a) => n + a.totaux.manquesPointage, 0),
    };
  });

  ngOnInit(): void {
    this.load();
    this.http.get<Activite[]>('/api/directory/business-departments').subscribe({
      next: (a) => this.activites.set(a ?? []),
      error: () => this.activites.set([]),
    });
  }

  changerPeriode(delta: number): void {
    this.decalage.set(delta === 0 ? 0 : this.decalage() + delta);
    this.load();
  }

  load(): void {
    const { du, au } = this.periode();
    this.loading.set(true);
    this.error.set(null);
    this.svc.grille(du, au).subscribe({
      next: (d) => {
        this.data.set(d);
        this.loading.set(false);
      },
      error: (err) => {
        this.loading.set(false);
        this.error.set(err?.status === 403
          ? 'Le tableau des présences est réservé aux rôles autorisés dans « Accès par rôle ».'
          : err?.error?.message ?? 'Impossible de charger le tableau des présences.');
      },
    });
  }

  filtrer(patch: Partial<FiltresPresence>): void {
    const next = { ...this.filtres(), ...patch };
    // Cascade : changer d'activité ou de pôle remet à zéro les niveaux en dessous.
    if ('activite' in patch) { next.pole = ''; next.cellule = ''; }
    if ('pole' in patch) next.cellule = '';
    this.filtres.set(next);
  }

  reinitialiser(): void {
    this.filtres.set({ ...FILTRES_VIDES });
  }

  jourDe(a: PresenceAgentDto, jour: string): PresenceJourDto | undefined {
    return a.jours.find((j) => j.jour.slice(0, 10) === jour);
  }

  infobulle(a: PresenceAgentDto, j: PresenceJourDto): string {
    const date = j.jour.slice(0, 10).split('-').reverse().join('/');
    const parts = [
      `${a.nom} — ${date}`,
      j.code ? `${j.code} : ${this.libelles().get(j.code) ?? ''}${j.auto ? ' (calculé)' : ' (saisi)'}` : 'Non planifié',
      j.planning ? `Prévu ${j.planning}` : '',
      j.pointages.length ? `Pointages : ${j.pointages.map((p) => p.slice(0, 5)).join(' · ')}` : '',
      j.retardEntreeMinutes + j.retardPauseMinutes > 0
        ? `Retard ${formatMinutes(j.retardEntreeMinutes + j.retardPauseMinutes)}${j.retardRetenuMinutes === 0 ? ' — autorisé' : ''}`
        : '',
      j.manquePointage ? 'Pointage manquant' : '',
      j.commentaire ? `« ${j.commentaire} »${j.modifiePar ? ` (${j.modifiePar})` : ''}` : '',
    ];
    return parts.filter(Boolean).join('\n');
  }

  ouvrir(a: PresenceAgentDto, j: PresenceJourDto): void {
    const code = j.auto ? null : j.code;
    const mode: Edition['mode'] = code === null ? 'auto' : code === 'P' ? 'present' : code === 'FOR' ? 'formation' : 'absent';
    this.edition.set({
      agent: a,
      jour: j,
      mode,
      motif: mode === 'absent' ? code! : 'ABS N',
      periode: false,
      au: j.jour.slice(0, 10),
      retardAutorise: j.retardAutorise,
      retardPauseAutorise: j.retardPauseAutorise,
      motifAutorisation: j.motifAutorisation ?? '',
      commentaire: j.commentaire ?? '',
      absencePartielle: !!j.absencePartielle,
    });
    const d = this.panneau?.nativeElement;
    if (d && !d.open) d.showModal();
  }

  fermer(): void {
    this.edition.set(null);
    const d = this.panneau?.nativeElement;
    if (d?.open) d.close();
  }

  /** Clic sur le fond assombri (hors du panneau) : fermeture. */
  clicFond(event: MouseEvent): void {
    if (event.target === this.panneau?.nativeElement) this.fermer();
  }

  patch(p: Partial<Edition>): void {
    const e = this.edition();
    if (e) this.edition.set({ ...e, ...p });
  }

  enregistrer(): void {
    const e = this.edition();
    const d = this.data();
    if (!e || !d || this.enregistrement()) return;
    const code = e.mode === 'auto' ? null : e.mode === 'present' ? 'P' : e.mode === 'formation' ? 'FOR' : e.motif;
    this.enregistrement.set(true);
    this.svc.saisir(
      {
        userId: e.agent.userId,
        jour: e.jour.jour.slice(0, 10),
        au: e.periode && e.au > e.jour.jour.slice(0, 10) ? e.au : null,
        code,
        retardAutorise: e.retardAutorise,
        retardPauseAutorise: e.retardPauseAutorise,
        motifAutorisation: e.motifAutorisation.trim() || null,
        commentaire: e.commentaire.trim() || null,
        absencePartielle: e.absencePartielle,
      },
      d.du.slice(0, 10),
      d.au.slice(0, 10),
    ).subscribe({
      next: (ligne) => {
        this.data.set({ ...d, agents: d.agents.map((a) => (a.userId === ligne.userId ? ligne : a)) });
        this.enregistrement.set(false);
        this.fermer();
        this.toast.success('Journée enregistrée.');
      },
      error: (err) => {
        this.enregistrement.set(false);
        this.toast.error(err?.error?.message ?? 'Enregistrement impossible.');
      },
    });
  }

  libelleCode(c: PresenceCodeDto): string {
    return `${c.code} — ${c.libelle}`;
  }

  dateCourte(jour: string): string {
    const [y, m, d] = jour.slice(0, 10).split('-').map(Number);
    return new Date(y, m - 1, d).toLocaleDateString('fr-FR', { weekday: 'long', day: 'numeric', month: 'long' });
  }

  absences(a: PresenceAgentDto): number {
    return Object.values(a.totaux.absences).reduce((s, v) => s + v, 0);
  }
}
