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

  ngOnInit(): void {
    this.load();
  }

  load(): void {
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
    const { cellules, services } = contenuDuPole(p);
    const effectif = this.effectifPole(p);
    const ok = await this.confirm.confirm({
      title: 'Archiver le pôle',
      message: `« ${p.name} » et ses ${cellules} cellule(s) / ${services} service(s) seront archivés. `
        + (effectif ? `${effectif} collaborateur(s) y sont rattachés : déplacez-les avant si besoin. ` : '')
        + 'Rien n’est supprimé : l’historique est conservé et le pôle peut être restauré.',
      confirmLabel: 'Archiver',
    });
    if (!ok) return;
    await this.run(this.api.archiveNode('pole', p.id, ''), 'Pôle archivé.');
  }

  async archiverCellule(p: OrgPoleNode, c: OrgCelluleNode): Promise<void> {
    const effectif = this.effectifCellule(c);
    const ok = await this.confirm.confirm({
      title: 'Archiver la cellule',
      message: `« ${c.name} » sera archivée avec ses services. `
        + (effectif ? `${effectif} collaborateur(s) y sont rattachés : déplacez-les avant si besoin. ` : '')
        + 'Rien n’est supprimé : l’historique est conservé et la cellule peut être restaurée.',
      confirmLabel: 'Archiver',
    });
    if (!ok) return;
    await this.run(this.api.archiveNode('cellule', c.id, p.id), 'Cellule archivée.');
  }

  async archiverService(c: OrgCelluleNode, s: OrgServiceNode): Promise<void> {
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
