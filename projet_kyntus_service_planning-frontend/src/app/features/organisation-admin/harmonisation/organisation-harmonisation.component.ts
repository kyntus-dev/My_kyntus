import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom, type Observable } from 'rxjs';
import { OrgAdminApiService } from '../../../core/org/org-admin-api.service';
import { PrimeOrgApiService } from '../../prime/services/prime-org-api.service';
import { KyntusToastService } from '../../../shared/components/ui/kyntus-toast.service';
import { KyntusConfirmService } from '../../../shared/components/kyntus-confirm/kyntus-confirm.service';
import {
  ARCHIVER,
  type Decisions,
  type EtatOrganisation,
  type Etape,
  type Operation,
  type Phase,
  PHASES,
  LIBELLES_PHASE,
  cheminsCellules,
  cheminsPoles,
  contratsTraites,
  estDifferee,
  estProtege,
  planifier,
  proposerDecisions,
  resumePhases,
} from './harmonisation.util';
import { REFERENTIEL_CONTRATS, REFERENTIEL_SUPPORT } from './referentiel-organisation';

interface Ligne { niveau: 'contrat' | 'pole' | 'cellule'; id: string; nom: string; parent: string; agents: number; protege: boolean }
interface Journal { ok: boolean; texte: string }

const MOTIF = 'Harmonisation (fichier de référence)';
const PASSAGES_MAX = 4;

/**
 * v32 : harmonisation de l'organisation sur le fichier de référence (Admin).
 * 1. Correspondances proposées d'office (modifiables) entre l'existant et le fichier.
 * 2. Aperçu des changements, exportable, avant toute écriture.
 * 3. Application étape par étape, avec un journal ; relancer ne refait que ce qui manque.
 */
@Component({
  selector: 'app-organisation-harmonisation',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './organisation-harmonisation.component.html',
  styleUrls: ['./organisation-harmonisation.component.css'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class OrganisationHarmonisationComponent implements OnInit {
  private readonly prime = inject(PrimeOrgApiService);
  private readonly api = inject(OrgAdminApiService);
  private readonly toast = inject(KyntusToastService);
  private readonly confirm = inject(KyntusConfirmService);

  readonly ARCHIVER = ARCHIVER;
  readonly referentiel = REFERENTIEL_CONTRATS;
  readonly support = REFERENTIEL_SUPPORT;
  readonly libellesPhase = LIBELLES_PHASE;
  readonly optionsContrat = REFERENTIEL_CONTRATS.map((c) => c.nom);
  readonly optionsPole = cheminsPoles();
  readonly optionsCellule = cheminsCellules();

  readonly etat = signal<EtatOrganisation | null>(null);
  /** Choix de l'Admin (prioritaires sur les propositions). */
  readonly choix = signal<Decisions>({ contrats: {}, poles: {}, cellules: {} });
  readonly avecResponsables = signal(true);
  readonly loading = signal(false);
  readonly erreur = signal<string | null>(null);
  readonly enCours = signal(false);
  readonly journal = signal<Journal[]>([]);
  readonly phasesOuvertes = signal<Set<Phase>>(new Set());
  readonly filtre = signal<'tout' | 'archiver' | 'changer'>('tout');

  readonly decisions = computed<Decisions>(() => {
    const e = this.etat();
    if (!e) return { contrats: {}, poles: {}, cellules: {} };
    const p = proposerDecisions(e);
    const c = this.choix();
    return { contrats: { ...p.contrats, ...c.contrats }, poles: { ...p.poles, ...c.poles }, cellules: { ...p.cellules, ...c.cellules } };
  });

  readonly plan = computed(() => {
    const e = this.etat();
    return e ? planifier(e, this.decisions(), { avecResponsables: this.avecResponsables() }) : null;
  });
  readonly phases = computed(() => (this.plan() ? resumePhases(this.plan()!) : []));

  /** Lignes de correspondance : contrats, pôles et cellules existants, avec le nombre d'agents. */
  readonly lignes = computed<Ligne[]>(() => {
    const e = this.etat();
    if (!e) return [];
    const agents = (pred: (x: EtatOrganisation['employes'][number]) => boolean) => e.employes.filter(pred).length;
    const out: Ligne[] = [];
    for (const c of e.contrats) {
      const protege = estProtege(c.nom);
      out.push({ niveau: 'contrat', id: c.id, nom: c.nom, parent: c.code, agents: 0, protege });
      if (protege) continue;
      for (const p of c.poles) {
        out.push({ niveau: 'pole', id: p.id, nom: p.nom, parent: c.nom, protege, agents: agents((x) => x.poleId === p.id) });
        for (const ce of p.cellules)
          out.push({ niveau: 'cellule', id: ce.id, nom: ce.nom, parent: `${c.nom} › ${p.nom}`, protege,
            agents: agents((x) => x.celluleId === ce.id || ce.services.some((s) => s.id === x.serviceId)) });
      }
    }
    for (const p of e.polesSansContrat) {
      out.push({ niveau: 'pole', id: p.id, nom: p.nom, parent: '(sans contrat)', protege: false, agents: agents((x) => x.poleId === p.id) });
      for (const ce of p.cellules)
        out.push({ niveau: 'cellule', id: ce.id, nom: ce.nom, parent: p.nom, protege: false,
          agents: agents((x) => x.celluleId === ce.id || ce.services.some((s) => s.id === x.serviceId)) });
    }
    return out;
  });

  readonly lignesFiltrees = computed(() => {
    const f = this.filtre();
    if (f === 'tout') return this.lignes();
    return this.lignes().filter((l) => {
      if (l.protege) return false;
      const d = this.decisionDe(l);
      if (f === 'archiver') return d === ARCHIVER;
      return d !== ARCHIVER && d.split(' › ').pop() !== l.nom;
    });
  });

  readonly synthese = computed(() => {
    const l = this.lignes().filter((x) => !x.protege);
    return {
      gardes: l.filter((x) => this.decisionDe(x) !== ARCHIVER).length,
      archives: l.filter((x) => this.decisionDe(x) === ARCHIVER).length,
      etapes: this.plan()?.etapes.length ?? 0,
      differees: this.plan()?.etapes.filter((e) => estDifferee(e.operation)).length ?? 0,
      alertes: this.plan()?.alertes.length ?? 0,
      introuvables: this.plan()?.introuvables.length ?? 0,
    };
  });

  ngOnInit(): void {
    void this.charger();
  }

  async charger(): Promise<void> {
    this.loading.set(true);
    this.erreur.set(null);
    try {
      this.etat.set(await this.lireEtat());
    } catch (err) {
      this.erreur.set((err as { error?: { error?: string; message?: string } })?.error?.error ?? 'Impossible de lire l’organisation.');
    } finally {
      this.loading.set(false);
    }
  }

  decisionDe(l: Ligne): string {
    const d = this.decisions();
    const m = l.niveau === 'contrat' ? d.contrats : l.niveau === 'pole' ? d.poles : d.cellules;
    return m[l.id] ?? ARCHIVER;
  }

  options(l: Ligne): string[] {
    return l.niveau === 'contrat' ? this.optionsContrat : l.niveau === 'pole' ? this.optionsPole : this.optionsCellule;
  }

  choisir(l: Ligne, valeur: string): void {
    const c = this.choix();
    const cle = l.niveau === 'contrat' ? 'contrats' : l.niveau === 'pole' ? 'poles' : 'cellules';
    this.choix.set({ ...c, [cle]: { ...c[cle], [l.id]: valeur } });
  }

  basculerPhase(p: Phase): void {
    const s = new Set(this.phasesOuvertes());
    if (s.has(p)) s.delete(p); else s.add(p);
    this.phasesOuvertes.set(s);
  }

  /** Aperçu en Excel, pour validation avant d'appliquer. */
  async exporterApercu(): Promise<void> {
    const plan = this.plan();
    if (!plan) return;
    const mod = await import('exceljs');
    const ExcelJS = ((mod as unknown) as { default?: typeof import('exceljs') }).default ?? mod;
    const wb = new ExcelJS.Workbook();
    const corr = wb.addWorksheet('Correspondances');
    corr.addRow(['Niveau', 'Nom actuel', 'Rattachement actuel', 'Collaborateurs', 'Décision']).font = { bold: true };
    for (const l of this.lignes())
      corr.addRow([l.niveau, l.nom, l.parent, l.agents, l.protege ? 'Non modifié (Facturation / IT)' : this.decisionDe(l) === ARCHIVER ? 'Archiver' : this.decisionDe(l)]);
    const ops = wb.addWorksheet('Changements');
    ops.addRow(['Étape', 'Changement']).font = { bold: true };
    for (const e of plan.etapes) ops.addRow([LIBELLES_PHASE[e.phase], e.libelle]);
    const al = wb.addWorksheet('À vérifier');
    al.addRow(['Point à vérifier']).font = { bold: true };
    for (const a of plan.alertes) al.addRow([a]);
    for (const i of plan.introuvables) al.addRow([`Introuvable dans l'application : ${i}`]);
    for (const ws of [corr, ops, al]) ws.columns.forEach((c, i) => (c.width = i === 0 && ws !== al ? 18 : 60));
    const buffer = await wb.xlsx.writeBuffer();
    const a = document.createElement('a');
    a.href = URL.createObjectURL(new Blob([buffer], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' }));
    a.download = `harmonisation_organisation_apercu.xlsx`;
    a.click();
    setTimeout(() => URL.revokeObjectURL(a.href), 1000);
  }

  async appliquer(): Promise<void> {
    const plan = this.plan();
    if (!plan || !plan.etapes.length || this.enCours()) return;
    const ok = await this.confirm.confirm({
      title: 'Appliquer l’harmonisation',
      message: `${plan.etapes.length} changement(s) vont être appliqués dans Organisation RH, puis repris par tous les modules ` +
        `(plannings, congés, présences, primes). Les anciens noms sont archivés (historique conservé, restaurables). ` +
        `Facturation et IT ne sont pas touchés. Continuer ?`,
      confirmLabel: 'Appliquer',
      variant: 'warning',
    });
    if (!ok) return;
    this.enCours.set(true);
    this.journal.set([]);
    const choix = this.choix();
    try {
      for (let passage = 1; passage <= PASSAGES_MAX; passage++) {
        let faites = 0;
        for (const phase of PHASES) {
          const etat = await this.lireEtat();
          this.etat.set(etat);
          const p = planifier(etat, this.fusion(etat, choix), { avecResponsables: this.avecResponsables() });
          // Les étapes « après création » attendent l'identifiant réel : elles passent au tour suivant.
          for (const e of p.etapes.filter((x) => x.phase === phase && !estDifferee(x.operation))) {
            faites++;
            await this.executer(e);
          }
        }
        if (faites === 0) break;
      }
      this.etat.set(await this.lireEtat());
      const erreurs = this.journal().filter((j) => !j.ok).length;
      if (erreurs) this.toast.error(`Harmonisation terminée avec ${erreurs} erreur(s) : voir le journal.`);
      else this.toast.success('Harmonisation appliquée.');
    } catch (err) {
      this.toast.error('Harmonisation interrompue : ' + this.message(err));
    } finally {
      this.enCours.set(false);
    }
  }

  private fusion(etat: EtatOrganisation, choix: Decisions): Decisions {
    const p = proposerDecisions(etat);
    return { contrats: { ...p.contrats, ...choix.contrats }, poles: { ...p.poles, ...choix.poles }, cellules: { ...p.cellules, ...choix.cellules } };
  }

  private async executer(e: Etape): Promise<void> {
    try {
      await firstValueFrom(this.appel(e.operation));
      this.journal.update((j) => [...j, { ok: true, texte: e.libelle }]);
    } catch (err) {
      this.journal.update((j) => [...j, { ok: false, texte: `${e.libelle} — ${this.message(err)}` }]);
    }
  }

  private appel(o: Operation): Observable<unknown> {
    switch (o.type) {
      case 'creerContrat': return this.api.createBusinessDepartment(o.nom);
      case 'renommerContrat':
        return this.api.updateBusinessDepartment(
          { id: o.id, code: '', name: o.nom, kind: 'Operational', managerEmployeeId: null, isActive: true, poleIds: [] }, { name: o.nom });
      case 'archiverContrat': return this.api.archiveBusinessDepartment(o.id);
      case 'creerPole': return this.api.createPole(o.contratId, o.nom);
      case 'rattacherPole': return this.api.attachPoleToBusinessDepartment(o.poleId, o.contratId);
      case 'renommerPole': return this.api.renameNode('pole', o.poleId, '', o.nom);
      case 'archiverPole': return this.api.archiveNode('pole', o.poleId, '');
      case 'creerCellule': return this.api.createCellule(o.poleId, o.nom);
      case 'renommerCellule': return this.api.renameNode('cellule', o.celluleId, o.poleId, o.nom);
      case 'regrouperCellules':
        return this.api.regrouperCellules({ sourceIds: o.sourceIds, serviceIds: [], cibleId: o.cibleId, nomCible: o.nomCible, poleId: o.poleId, motif: MOTIF });
      case 'archiverCellule': return this.api.archiveNode('cellule', o.celluleId, o.poleId);
      case 'creerService': return this.api.createService(o.celluleId, o.nom);
      case 'renommerService': return this.api.renameNode('service', o.serviceId, o.celluleId, o.nom);
      case 'placerAgent': return this.api.moveAgent(o.employeId, o.serviceId, { force: true, reason: MOTIF });
      case 'affecterResponsable': return this.api.assignResponsable(o.kind, o.noeudId, o.employeId, MOTIF);
    }
  }

  private message(err: unknown): string {
    const e = err as { error?: { error?: string; message?: string; title?: string }; message?: string; status?: number };
    return e?.error?.error ?? e?.error?.message ?? e?.error?.title ?? e?.message ?? `erreur ${e?.status ?? ''}`;
  }

  /** État actuel d'Organisation RH (lecture fraîche, sans cache). */
  private async lireEtat(): Promise<EtatOrganisation> {
    this.prime.invalidateOverviewCache();
    const o = await firstValueFrom(this.prime.loadOverview());
    const poles = (ps: typeof o.unassignedPoles) => ps.map((p) => ({
      id: p.id,
      nom: p.name,
      cellules: p.cellules.map((c) => ({ id: c.id, nom: c.name, services: c.services.map((s) => ({ id: s.id, nom: s.name })) })),
    }));
    return {
      contrats: o.operationalDepartments.map((d) => ({ id: d.id, code: d.code, nom: d.name, poles: poles(d.poles) })),
      polesSansContrat: poles(o.unassignedPoles),
      employes: o.employees.map((e) => ({
        id: e.id,
        prenom: e.firstName ?? '',
        nom: e.lastName ?? '',
        role: String(e.role ?? ''),
        poleId: e.poleId || null,
        celluleId: e.celluleId || null,
        serviceId: e.serviceId || null,
      })),
      affectations: [
        ...o.managerEtage.map((a) => ({ type: 'ChefDeProjet' as const, employeId: a.userId, noeudId: a.etageId })),
        ...o.supervisorService.map((a) => ({ type: 'Superviseur' as const, employeId: a.userId, noeudId: a.celluleId ?? a.serviceId })),
        ...o.coachSousService.map((a) => ({ type: 'ReferentTechnique' as const, employeId: a.userId, noeudId: a.sousServiceId })),
      ],
    };
  }

  contratsTraites(): number {
    const e = this.etat();
    return e ? contratsTraites(e).length : 0;
  }
}
