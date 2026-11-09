import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { firstValueFrom, forkJoin, type Observable } from 'rxjs';
import { KyntusPageHeaderComponent } from '../../shared/components/ui/kyntus-page-header.component';
import { KyntusToastService } from '../../shared/components/ui/kyntus-toast.service';
import { KyntusConfirmService } from '../../shared/components/kyntus-confirm/kyntus-confirm.service';
import { KyntusPromptService } from '../../shared/components/kyntus-prompt/kyntus-prompt.service';
import { AuthService } from '../../core/services/auth.service';
import { PrimeOrgApiService } from '../prime/services/prime-org-api.service';
import type { OperationalDepartmentNode, OrgCelluleNode, OrgPoleNode, OrgServiceNode } from '../prime/models/org-tree.types';
import {
  OrgAdminApiService,
  type ArchivedOrgNode,
  type BusinessDepartmentRow,
  type OrgNodeLevelKey,
} from '../../core/org/org-admin-api.service';
import {
  SANS_CELLULE,
  buildCelluleIndex,
  celluleLabel,
  cellulesDe,
  contenuDuPole,
  effectifsParCellule,
  isMovableAgent,
  membresDeCellule,
  resolveServiceCible,
  type OrgAgent,
} from '../../core/org/org-admin.util';
import {
  agentsDuNoeud,
  destinationParDefaut,
  destinationsArchivage,
  repartirAgents,
  type CibleArchivage,
} from './archivage.util';

type Onglet = 'structure' | 'agents' | 'archives';

/**
 * Organisation administrative (Admin / RH).
 *
 * - Structure : contrats (activités), pôles, cellules et services — créer, renommer, archiver.
 * - Agents : voir les membres d'une cellule et les déplacer vers une autre cellule.
 * - Archives : restaurer, ou supprimer définitivement un nœud qui n'a jamais servi.
 *
 * « Archiver » ne supprime rien : le nœud reste en base avec son historique
 * (plannings, primes, congés, affectations clôturées) et disparaît des listes et des choix.
 */
@Component({
  selector: 'app-organisation-admin',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, KyntusPageHeaderComponent],
  templateUrl: './organisation-admin.component.html',
  styleUrls: ['../conge/pages/conge-historique/conge-historique.component.css', './organisation-admin.component.css'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class OrganisationAdminComponent implements OnInit {
  private readonly api = inject(OrgAdminApiService);
  private readonly orgApi = inject(PrimeOrgApiService);
  private readonly toast = inject(KyntusToastService);
  private readonly confirm = inject(KyntusConfirmService);
  private readonly prompt = inject(KyntusPromptService);
  private readonly auth = inject(AuthService);

  readonly SANS_CELLULE = SANS_CELLULE;
  readonly celluleLabel = celluleLabel;
  readonly isMovableAgent = isMovableAgent;

  readonly onglet = signal<Onglet>('structure');
  readonly loading = signal(false);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);

  readonly depts = signal<OperationalDepartmentNode[]>([]);
  readonly unassigned = signal<OrgPoleNode[]>([]);
  readonly agents = signal<OrgAgent[]>([]);
  readonly contrats = signal<BusinessDepartmentRow[]>([]);
  readonly archives = signal<ArchivedOrgNode[]>([]);

  /** Pôles dépliés dans l'onglet Structure. */
  readonly ouverts = signal<Set<string>>(new Set());

  // ── Onglet Agents ───────────────────────────────────────────────────────────
  readonly filtreActivite = signal('');
  readonly filtrePole = signal('');
  readonly celluleSel = signal('');
  readonly recherche = signal('');
  readonly selection = signal<Set<string>>(new Set());
  readonly cibleActivite = signal('');
  readonly ciblePole = signal('');
  readonly cibleCellule = signal('');
  readonly cibleService = signal('');

  readonly isAdmin = computed(() => (this.auth.getRole() || '').toLowerCase() === 'admin');
  readonly index = computed(() => buildCelluleIndex(this.depts(), this.unassigned()));
  readonly effectifs = computed(() => effectifsParCellule(this.agents()));
  readonly codeParContrat = computed(() => new Map(this.contrats().map((c) => [c.id, c.code])));
  readonly contratsArchives = computed(() =>
    this.contrats().filter((c) => !c.isActive && (c.kind ?? '').toLowerCase() === 'operational'));

  readonly polesFiltre = computed(() => {
    const a = this.filtreActivite();
    return a ? this.depts().find((d) => d.id === a)?.poles ?? [] : [];
  });
  readonly cellulesFiltre = computed(() =>
    cellulesDe(this.depts(), this.unassigned(), this.filtreActivite(), this.filtrePole()));
  readonly membres = computed(() =>
    this.celluleSel() ? membresDeCellule(this.agents(), this.celluleSel(), this.recherche()) : []);
  readonly membresDeplacables = computed(() => this.membres().filter((m) => isMovableAgent(m)));

  readonly polesCible = computed(() => {
    const a = this.cibleActivite();
    return a ? this.depts().find((d) => d.id === a)?.poles ?? [] : [];
  });
  readonly cellulesCible = computed(() =>
    this.ciblePole() ? cellulesDe(this.depts(), this.unassigned(), this.cibleActivite(), this.ciblePole()) : []);
  readonly servicesCible = computed<OrgServiceNode[]>(() =>
    this.index().get(this.cibleCellule())?.services ?? []);
  readonly serviceCible = computed(() =>
    resolveServiceCible(this.index().get(this.cibleCellule()), this.cibleService() || null));

  // ── Regroupement de cellules (v29) ──────────────────────────────────────────
  readonly rgOuvert = signal(false);
  readonly rgCellules = signal<ReadonlySet<string>>(new Set());
  readonly rgServices = signal<ReadonlySet<string>>(new Set());
  readonly rgMode = signal<'nouvelle' | 'existante'>('nouvelle');
  readonly rgNom = signal('');
  readonly rgPole = signal('');
  readonly rgCible = signal('');
  readonly rgMotif = signal('');
  readonly rgRecherche = signal('');
  /** Pôles actifs avec leur contrat, pour choisir sources et destination. */
  readonly rgPoles = computed(() => [
    ...this.depts().flatMap((d) => d.poles.map((p) => ({ pole: p, contrat: d.name }))),
    ...this.unassigned().map((p) => ({ pole: p, contrat: 'Sans contrat' })),
  ]);
  readonly rgPolesFiltres = computed(() => {
    const q = this.rgRecherche().trim().toLowerCase().normalize('NFD').replace(/[\u0300-\u036f]/g, '');
    if (!q) return this.rgPoles();
    const ok = (t: string) => t.toLowerCase().normalize('NFD').replace(/[\u0300-\u036f]/g, '').includes(q);
    return this.rgPoles()
      .map((x) => ({ ...x, pole: { ...x.pole, cellules: x.pole.cellules.filter((c) => ok(c.name) || c.services.some((s) => ok(s.name))) } }))
      .filter((x) => x.pole.cellules.length > 0);
  });
  readonly rgPret = computed(() =>
    (this.rgCellules().size > 0 || this.rgServices().size > 0)
    && (this.rgMode() === 'existante' ? !!this.rgCible() : !!this.rgNom().trim()));

  // ── Archivage avec réaffectation des collaborateurs (v36) ──────────────────
  readonly arCible = signal<CibleArchivage | null>(null);
  readonly arDest = signal('');
  readonly arRecherche = signal('');
  readonly arDestinations = computed(() => {
    const c = this.arCible();
    return c ? destinationsArchivage(this.depts(), this.unassigned(), c) : [];
  });
  readonly arDestFiltrees = computed(() => {
    const norm = (t: string) => t.toLowerCase().normalize('NFD').replace(/[\u0300-\u036f]/g, '');
    const q = norm(this.arRecherche().trim());
    const liste = this.arDestinations();
    const filtrees = q ? liste.filter((d) => norm(`${d.groupe} ${d.nom}`).includes(q)) : liste;
    // La destination choisie reste visible même si elle ne correspond plus à la recherche.
    const choisie = liste.find((d) => d.id === this.arDest());
    return choisie && !filtrees.includes(choisie) ? [choisie, ...filtrees] : filtrees;
  });
  readonly arGroupes = computed(() => {
    const m = new Map<string, { groupe: string; services: { id: string; nom: string }[] }>();
    for (const d of this.arDestFiltrees()) {
      const g = m.get(d.groupe) ?? { groupe: d.groupe, services: [] };
      g.services.push({ id: d.id, nom: d.nom });
      m.set(d.groupe, g);
    }
    return [...m.values()];
  });
  readonly arAgents = computed(() => {
    const c = this.arCible();
    return c ? repartirAgents(agentsDuNoeud(this.agents(), c)) : { pilotes: [], encadrement: [] };
  });
  readonly arPret = computed(() => !!this.arCible() && (this.arAgents().pilotes.length === 0 || !!this.arDest()));

  ngOnInit(): void {
    this.load();
  }

  private ouvrirArchivage(cible: CibleArchivage, parentCelluleId: string | null, parentPoleId: string | null): void {
    this.rgOuvert.set(false);
    this.arRecherche.set('');
    this.arCible.set(cible);
    this.arDest.set(destinationParDefaut(this.arDestinations(), cible, parentCelluleId, parentPoleId));
    setTimeout(() => document.querySelector('.oa-ar')?.scrollIntoView({ behavior: 'smooth', block: 'start' }));
  }

  fermerArchivage(): void {
    this.arCible.set(null);
  }

  /** Onglet Archives : un nœud archivé qui garde des collaborateurs. */
  reaffecterArchive(n: ArchivedOrgNode): void {
    this.ouvrirArchivage({
      niveau: this.levelKey(n), id: n.id, nom: n.name, parentId: n.parentId ?? '', cellules: [], dejaArchive: true,
    }, null, null);
  }

  async confirmerArchivage(): Promise<void> {
    const cible = this.arCible();
    if (!cible || !this.arPret() || this.busy()) return;
    const { pilotes, encadrement } = this.arAgents();
    const dest = this.arDestinations().find((d) => d.id === this.arDest());
    const libelle = cible.niveau === 'pole' ? 'le pôle' : cible.niveau === 'cellule' ? 'la cellule' : 'le service';
    const ok = await this.confirm.confirm({
      title: cible.dejaArchive ? `Réaffecter les collaborateurs de « ${cible.nom} »` : `Archiver ${libelle} « ${cible.nom} »`,
      message: [
        pilotes.length && dest ? `${pilotes.length} collaborateur(s) passent dans ${dest.groupe} › ${dest.nom} (affectation actuelle clôturée, historique conservé).` : '',
        encadrement.length ? `${encadrement.length} responsable(s) (${encadrement.map((a) => `${a.lastName} ${a.firstName}`.trim()).join(', ')}) ne sont pas déplacés comme pilotes : corrigez leur fiche.` : '',
        cible.dejaArchive ? '' : `Puis ${libelle} est archivé(e) et disparaît des listes, des choix de périmètre et des quotas. Restaurable depuis Archives.`,
      ].filter(Boolean).join(' '),
      confirmLabel: cible.dejaArchive ? 'Réaffecter' : 'Réaffecter et archiver',
      variant: 'warning',
    });
    if (!ok) return;

    this.busy.set(true);
    const echecs: string[] = [];
    let deplaces = 0;
    for (const a of pilotes) {
      try {
        await firstValueFrom(this.api.moveAgent(a.id, dest!.id, {
          force: this.isAdmin(),
          reason: `Archivage de « ${cible.nom} »`,
        }));
        deplaces++;
      } catch (err) {
        const motif = (err as { status?: number })?.status === 422
          ? 'durée minimale de rotation non atteinte (un Admin peut forcer)'
          : this.message(err, 'échec');
        echecs.push(`${a.lastName} ${a.firstName} : ${motif}`);
      }
    }

    if (echecs.length) {
      this.busy.set(false);
      this.toast.error(`${echecs.length} collaborateur(s) non déplacé(s), rien n’est archivé : ${echecs.slice(0, 3).join(' — ')}`);
      this.load();
      return;
    }

    try {
      if (!cible.dejaArchive) await firstValueFrom(this.api.archiveNode(cible.niveau, cible.id, cible.parentId));
      this.toast.success((deplaces ? `${deplaces} collaborateur(s) réaffecté(s). ` : '')
        + (cible.dejaArchive ? '' : `« ${cible.nom} » archivé(e).`));
      this.arCible.set(null);
    } catch (err) {
      this.toast.error(this.message(err, 'Archivage impossible.'));
    } finally {
      this.busy.set(false);
      this.load();
    }
  }

  ouvrirRegroupement(): void {
    this.rgCellules.set(new Set());
    this.rgServices.set(new Set());
    this.rgMode.set('nouvelle');
    this.rgNom.set('');
    this.rgPole.set('');
    this.rgCible.set('');
    this.rgMotif.set('');
    this.rgRecherche.set('');
    this.rgOuvert.set(true);
  }

  basculerRg(liste: 'cellules' | 'services', id: string): void {
    const sig = liste === 'cellules' ? this.rgCellules : this.rgServices;
    const s = new Set(sig());
    if (s.has(id)) s.delete(id); else s.add(id);
    sig.set(s);
    // Pôle de la nouvelle cellule : par défaut celui de la première cellule choisie.
    if (liste === 'cellules' && !this.rgPole() && s.size) {
      const p = this.rgPoles().find((x) => x.pole.cellules.some((c) => s.has(c.id)));
      if (p) this.rgPole.set(p.pole.id);
    }
  }

  async regrouper(): Promise<void> {
    if (!this.rgPret() || this.busy()) return;
    const cellules = this.rgPoles().flatMap((x) => x.pole.cellules).filter((c) => this.rgCellules().has(c.id));
    const services = this.rgPoles().flatMap((x) => x.pole.cellules.flatMap((c) => c.services)).filter((s) => this.rgServices().has(s.id));
    const cible = this.rgMode() === 'existante'
      ? this.rgPoles().flatMap((x) => x.pole.cellules).find((c) => c.id === this.rgCible())?.name ?? ''
      : this.rgNom().trim();
    const effectif = cellules.reduce((n, c) => n + this.effectifCellule(c), 0);
    const ok = await this.confirm.confirm({
      title: `Regrouper dans « ${cible} »`,
      message: [
        cellules.length ? `Cellules regroupées : ${cellules.map((c) => c.name).join(', ')} (${effectif} personne(s)).` : '',
        services.length ? `Services déplacés : ${services.map((s) => s.name).join(', ')}.` : '',
        'Leurs services, agents et superviseurs passent dans la cellule de destination ; plannings, congés, présences et primes suivent.',
        'Les cellules vidées sont archivées (restaurables, historique conservé).',
      ].filter(Boolean).join(' '),
      confirmLabel: 'Regrouper',
      variant: 'warning',
    });
    if (!ok) return;
    this.busy.set(true);
    try {
      const r = await firstValueFrom(this.api.regrouperCellules({
        sourceIds: [...this.rgCellules()],
        serviceIds: [...this.rgServices()],
        cibleId: this.rgMode() === 'existante' ? this.rgCible() : null,
        nomCible: this.rgMode() === 'nouvelle' ? this.rgNom().trim() : null,
        poleId: this.rgMode() === 'nouvelle' ? this.rgPole() || null : null,
        motif: this.rgMotif().trim() || null,
      }));
      this.toast.success(`« ${r.cibleNom} » : ${r.servicesDeplaces} service(s), ${r.agents} agent(s), ${r.superviseurs} superviseur(s) regroupés`
        + (r.cellulesArchivees.length ? ` ; archivées : ${r.cellulesArchivees.join(', ')}.` : '.'));
      this.rgOuvert.set(false);
      this.load();
    } catch (err) {
      this.toast.error(this.message(err, 'Regroupement impossible.'));
    } finally {
      this.busy.set(false);
    }
  }

  load(): void {
    // Données fraîches après chaque modification (le cache de l'annuaire garde 30 s).
    this.orgApi.invalidateOverviewCache();
    this.loading.set(true);
    this.error.set(null);
    forkJoin({
      overview: this.orgApi.loadOverview(),
      contrats: this.api.listBusinessDepartments(),
      archives: this.api.listArchivedNodes(),
    }).subscribe({
      next: ({ overview, contrats, archives }) => {
        this.depts.set(overview.operationalDepartments ?? []);
        this.unassigned.set(overview.unassignedPoles ?? []);
        this.agents.set((overview.employees ?? []).map((e) => ({
          id: e.id,
          firstName: e.firstName ?? '',
          lastName: e.lastName ?? '',
          role: String(e.role ?? ''),
          email: e.email ?? '',
          poleId: e.poleId || null,
          celluleId: e.celluleId || null,
          serviceId: e.serviceId || null,
        })));
        this.contrats.set(contrats ?? []);
        this.archives.set(archives ?? []);
        this.nettoyerSelection();
        this.loading.set(false);
      },
      error: (err) => {
        this.loading.set(false);
        this.error.set(err?.error?.error ?? err?.error?.message ?? 'Impossible de charger l’organisation.');
      },
    });
  }

  // ── Structure ───────────────────────────────────────────────────────────────

  togglePole(id: string): void {
    const next = new Set(this.ouverts());
    if (next.has(id)) next.delete(id);
    else next.add(id);
    this.ouverts.set(next);
  }

  estOuvert(id: string): boolean {
    return this.ouverts().has(id);
  }

  effectifCellule(c: OrgCelluleNode): number {
    return this.effectifs().get(c.id) ?? 0;
  }

  effectifPole(p: OrgPoleNode): number {
    return (p.cellules ?? []).reduce((n, c) => n + this.effectifCellule(c), 0);
  }

  async nouveauContrat(): Promise<void> {
    const name = await this.prompt.prompt({
      title: 'Nouveau contrat',
      message: 'Nom du contrat (activité), par exemple « Bytel Kyntus ».',
      confirmLabel: 'Créer',
    });
    if (!name?.trim()) return;
    await this.run(this.api.createBusinessDepartment(name), `Contrat « ${name.trim()} » créé.`);
  }

  async renommerContrat(d: OperationalDepartmentNode): Promise<void> {
    const row = this.contrats().find((c) => c.id === d.id);
    if (!row) return;
    const name = await this.prompt.prompt({ title: 'Renommer le contrat', defaultValue: d.name, confirmLabel: 'Renommer' });
    if (!name?.trim() || name.trim() === d.name) return;
    await this.run(this.api.updateBusinessDepartment(row, { name }), 'Contrat renommé.');
  }

  async archiverContrat(d: OperationalDepartmentNode): Promise<void> {
    if ((d.poles ?? []).length > 0) {
      this.toast.error(`« ${d.name} » contient encore ${d.poles.length} pôle(s) : archivez-les d’abord.`);
      return;
    }
    const ok = await this.confirm.confirm({
      title: 'Archiver le contrat',
      message: `« ${d.name} » ne sera plus proposé. Son historique est conservé et il pourra être réactivé depuis l’onglet Archives.`,
      confirmLabel: 'Archiver',
    });
    if (!ok) return;
    await this.run(this.api.archiveBusinessDepartment(d.id), 'Contrat archivé.');
  }

  async nouveauPole(d: OperationalDepartmentNode): Promise<void> {
    const name = await this.prompt.prompt({ title: `Nouveau pôle — ${d.name}`, confirmLabel: 'Créer' });
    if (!name?.trim()) return;
    await this.run(this.api.createPole(d.id, name), `Pôle « ${name.trim()} » créé.`);
  }

  async nouvelleCellule(p: OrgPoleNode): Promise<void> {
    const name = await this.prompt.prompt({
      title: `Nouvelle cellule — ${p.name}`,
      message: 'Un service du même nom sera créé dans la cellule pour pouvoir y affecter des agents.',
      confirmLabel: 'Créer',
    });
    if (!name?.trim()) return;
    this.busy.set(true);
    try {
      const created = await firstValueFrom(this.api.createCellule(p.id, name));
      await firstValueFrom(this.api.createService(created.id, name));
      this.ouverts.set(new Set(this.ouverts()).add(p.id));
      this.toast.success(`Cellule « ${name.trim()} » créée.`);
      this.load();
    } catch (err) {
      this.toast.error(this.message(err, 'Impossible de créer la cellule.'));
    } finally {
      this.busy.set(false);
    }
  }

  async nouveauService(c: OrgCelluleNode): Promise<void> {
    const name = await this.prompt.prompt({ title: `Nouveau service — ${c.name}`, confirmLabel: 'Créer' });
    if (!name?.trim()) return;
    await this.run(this.api.createService(c.id, name), `Service « ${name.trim()} » créé.`);
  }

  async renommer(level: OrgNodeLevelKey, id: string, parentId: string, current: string): Promise<void> {
    const name = await this.prompt.prompt({ title: 'Renommer', defaultValue: current, confirmLabel: 'Renommer' });
    if (!name?.trim() || name.trim() === current) return;
    await this.run(this.api.renameNode(level, id, parentId, name), 'Nom mis à jour.');
  }

  async archiverPole(p: OrgPoleNode): Promise<void> {
    const cible: CibleArchivage = {
      niveau: 'pole', id: p.id, nom: p.name, parentId: '', cellules: (p.cellules ?? []).map((c) => c.id), dejaArchive: false,
    };
    if (agentsDuNoeud(this.agents(), cible).length) {
      this.ouvrirArchivage(cible, null, null);
      return;
    }
    const { cellules, services } = contenuDuPole(p);
    const ok = await this.confirm.confirm({
      title: 'Archiver le pôle',
      message: `« ${p.name} » et ses ${cellules} cellule(s) / ${services} service(s) seront archivés. `
        + 'Rien n’est supprimé : l’historique est conservé et le pôle peut être restauré.',
      confirmLabel: 'Archiver',
    });
    if (!ok) return;
    await this.run(this.api.archiveNode('pole', p.id, ''), 'Pôle archivé.');
  }

  async archiverCellule(p: OrgPoleNode, c: OrgCelluleNode): Promise<void> {
    const cible: CibleArchivage = { niveau: 'cellule', id: c.id, nom: c.name, parentId: p.id, cellules: [], dejaArchive: false };
    if (agentsDuNoeud(this.agents(), cible).length) {
      this.ouvrirArchivage(cible, null, p.id);
      return;
    }
    const ok = await this.confirm.confirm({
      title: 'Archiver la cellule',
      message: `« ${c.name} » sera archivée avec ses services. `
        + 'Rien n’est supprimé : l’historique est conservé et la cellule peut être restaurée.',
      confirmLabel: 'Archiver',
    });
    if (!ok) return;
    await this.run(this.api.archiveNode('cellule', c.id, p.id), 'Cellule archivée.');
  }

  async archiverService(c: OrgCelluleNode, s: OrgServiceNode): Promise<void> {
    const cible: CibleArchivage = { niveau: 'service', id: s.id, nom: s.name, parentId: c.id, cellules: [], dejaArchive: false };
    if (agentsDuNoeud(this.agents(), cible).length) {
      this.ouvrirArchivage(cible, c.id, null);
      return;
    }
    const ok = await this.confirm.confirm({
      title: 'Archiver le service',
      message: `« ${s.name} » ne sera plus proposé. L’historique est conservé et le service peut être restauré.`,
      confirmLabel: 'Archiver',
    });
    if (!ok) return;
    await this.run(this.api.archiveNode('service', s.id, c.id), 'Service archivé.');
  }

  // ── Agents ──────────────────────────────────────────────────────────────────

  choisirActivite(id: string): void {
    this.filtreActivite.set(id);
    this.filtrePole.set('');
  }

  choisirCellule(id: string): void {
    this.celluleSel.set(id);
    this.selection.set(new Set());
  }

  choisirCibleActivite(id: string): void {
    this.cibleActivite.set(id);
    this.ciblePole.set('');
    this.cibleCellule.set('');
    this.cibleService.set('');
  }

  choisirCiblePole(id: string): void {
    this.ciblePole.set(id);
    this.cibleCellule.set('');
    this.cibleService.set('');
  }

  choisirCibleCellule(id: string): void {
    this.cibleCellule.set(id);
    this.cibleService.set('');
  }

  libelleCelluleSel(): string {
    return this.celluleSel() === SANS_CELLULE ? 'Agents sans cellule' : celluleLabel(this.index().get(this.celluleSel()));
  }

  estSelectionne(id: string): boolean {
    return this.selection().has(id);
  }

  toggleAgent(id: string): void {
    const next = new Set(this.selection());
    if (next.has(id)) next.delete(id);
    else next.add(id);
    this.selection.set(next);
  }

  toutSelectionner(): void {
    const ids = this.membresDeplacables().map((m) => m.id);
    const all = ids.length > 0 && ids.every((id) => this.selection().has(id));
    this.selection.set(all ? new Set() : new Set(ids));
  }

  async deplacer(): Promise<void> {
    const ids = [...this.selection()];
    const loc = this.index().get(this.cibleCellule());
    const service = this.serviceCible();
    if (!ids.length || !loc) return;
    if (!service) {
      this.toast.error(loc.services.length
        ? 'Choisissez le service d’arrivée dans la cellule.'
        : 'Cette cellule n’a pas encore de service : ajoutez-en un dans l’onglet Structure.');
      return;
    }
    if (loc.celluleId === this.celluleSel()) {
      this.toast.error('La cellule d’arrivée est la cellule actuelle.');
      return;
    }
    const ok = await this.confirm.confirm({
      title: 'Déplacer les agents',
      message: `${ids.length} agent(s) vers ${celluleLabel(loc)}. Leur affectation actuelle est clôturée (historique conservé).`,
      confirmLabel: 'Déplacer',
    });
    if (!ok) return;

    this.busy.set(true);
    const bloques: string[] = [];
    const erreurs: string[] = [];
    let deplaces = 0;
    for (const id of ids) {
      try {
        await firstValueFrom(this.api.moveAgent(id, service.id));
        deplaces++;
      } catch (err) {
        if ((err as { status?: number })?.status === 422) bloques.push(id);
        else erreurs.push(`${this.nomAgent(id)} : ${this.message(err, 'échec')}`);
      }
    }

    if (bloques.length && this.isAdmin()) {
      const forcer = await this.confirm.confirm({
        title: 'Rotation non échue',
        message: `${bloques.length} agent(s) n’ont pas atteint la durée minimale dans leur service actuel. Forcer le déplacement (motif tracé) ?`,
        confirmLabel: 'Forcer',
      });
      if (forcer) {
        for (const id of [...bloques]) {
          try {
            await firstValueFrom(this.api.moveAgent(id, service.id, {
              force: true,
              reason: 'Réaffectation forcée depuis Organisation administrative',
            }));
            deplaces++;
            bloques.splice(bloques.indexOf(id), 1);
          } catch (err) {
            erreurs.push(`${this.nomAgent(id)} : ${this.message(err, 'échec')}`);
          }
        }
      }
    }

    this.busy.set(false);
    if (deplaces) this.toast.success(`${deplaces} agent(s) déplacé(s) vers ${loc.celluleNom}.`);
    if (bloques.length) {
      this.toast.error(`${bloques.length} agent(s) non déplacé(s) : durée minimale de rotation non atteinte`
        + (this.isAdmin() ? '.' : ' (un Admin peut forcer).'));
    }
    if (erreurs.length) this.toast.error(erreurs.slice(0, 3).join(' — '));
    this.selection.set(new Set());
    this.load();
  }

  async retirer(): Promise<void> {
    const ids = [...this.selection()];
    if (!ids.length) return;
    const ok = await this.confirm.confirm({
      title: 'Retirer de la cellule',
      message: `${ids.length} agent(s) n’auront plus de pôle ni de cellule (ils apparaîtront dans « Agents sans cellule »). L’historique est conservé.`,
      confirmLabel: 'Retirer',
    });
    if (!ok) return;
    this.busy.set(true);
    let n = 0;
    for (const id of ids) {
      try {
        await firstValueFrom(this.api.clearPlacement(id));
        n++;
      } catch (err) {
        this.toast.error(`${this.nomAgent(id)} : ${this.message(err, 'échec')}`);
      }
    }
    this.busy.set(false);
    if (n) this.toast.success(`${n} agent(s) retiré(s) de la cellule.`);
    this.selection.set(new Set());
    this.load();
  }

  // ── Archives ────────────────────────────────────────────────────────────────

  levelKey(n: ArchivedOrgNode): OrgNodeLevelKey {
    return n.level === 'Pole' ? 'pole' : n.level === 'Cellule' ? 'cellule' : 'service';
  }

  levelLabel(n: ArchivedOrgNode): string {
    return n.level === 'Pole' ? 'Pôle' : n.level === 'Cellule' ? 'Cellule' : 'Service';
  }

  purgeTitle(n: ArchivedOrgNode): string {
    if (n.suppressionDefinitivePossible) return 'Supprimer définitivement (jamais utilisé)';
    if (n.collaborateurs > 0) return `${n.collaborateurs} collaborateur(s) encore rattaché(s)`;
    return 'Déjà utilisé (affectations ou historique) : reste archivé';
  }

  async restaurer(n: ArchivedOrgNode): Promise<void> {
    await this.run(this.api.restoreNode(this.levelKey(n), n.id), `${this.levelLabel(n)} « ${n.name} » restauré(e).`);
  }

  async supprimerDefinitivement(n: ArchivedOrgNode): Promise<void> {
    if (!n.suppressionDefinitivePossible) return;
    const ok = await this.confirm.confirm({
      title: 'Suppression définitive',
      message: `« ${n.name} » n’a jamais servi. Il sera supprimé définitivement : cette action est irréversible.`,
      confirmLabel: 'Supprimer',
      variant: 'danger',
    });
    if (!ok) return;
    await this.run(this.api.purgeNode(this.levelKey(n), n.id, n.parentId ?? ''), 'Supprimé définitivement.');
  }

  async reactiverContrat(c: BusinessDepartmentRow): Promise<void> {
    await this.run(this.api.updateBusinessDepartment(c, { isActive: true }), `Contrat « ${c.name} » réactivé.`);
  }

  formatDate(value: string | null): string {
    if (!value) return '—';
    const d = new Date(value);
    return Number.isNaN(d.getTime()) ? '—' : d.toLocaleDateString('fr-FR');
  }

  // ── Utilitaires ─────────────────────────────────────────────────────────────

  private async run(call: Observable<unknown>, success: string): Promise<void> {
    this.busy.set(true);
    try {
      await firstValueFrom(call);
      this.toast.success(success);
      this.load();
    } catch (err) {
      this.toast.error(this.message(err, 'Opération impossible.'));
    } finally {
      this.busy.set(false);
    }
  }

  private message(err: unknown, fallback: string): string {
    const e = err as { error?: { error?: string; message?: string } | string };
    if (typeof e?.error === 'string' && e.error.trim()) return e.error;
    if (e?.error && typeof e.error === 'object') return e.error.error || e.error.message || fallback;
    return fallback;
  }

  private nomAgent(id: string): string {
    const a = this.agents().find((x) => x.id === id);
    return a ? `${a.lastName} ${a.firstName}`.trim() : id;
  }

  /** Après rechargement, oublie la sélection d'agents qui ne sont plus dans la cellule. */
  private nettoyerSelection(): void {
    const presents = new Set(this.membres().map((m) => m.id));
    this.selection.set(new Set([...this.selection()].filter((id) => presents.has(id))));
  }
}
