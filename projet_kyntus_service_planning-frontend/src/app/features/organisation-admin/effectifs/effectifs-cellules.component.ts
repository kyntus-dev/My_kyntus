import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { PrimeOrgApiService } from '../../prime/services/prime-org-api.service';
import type { OperationalDepartmentNode, OrgPoleNode } from '../../prime/models/org-tree.types';
import { OrgAdminApiService } from '../../../core/org/org-admin-api.service';
import type { OrgAgent } from '../../../core/org/org-admin.util';
import { AuthService } from '../../../core/services/auth.service';
import { PlatformAccessService } from '../../../core/platform-access/platform-access.service';
import { KyntusToastService } from '../../../shared/components/ui/kyntus-toast.service';
import { KyntusConfirmService } from '../../../shared/components/kyntus-confirm/kyntus-confirm.service';
import { agentsDuNoeud, destinationsArchivage, repartirAgents, type CibleArchivage } from '../archivage.util';
import { filtrerEffectifs, lignesEffectifs, totauxEffectifs, type LigneEffectif } from './effectifs.util';

type Mode = 'reaffecter' | 'desactiver';

/**
 * v39 — Structure de production › Effectifs : effectif de chaque cellule, réaffectation de ses agents
 * vers un autre service, ou désactivation (archivage) après réaffectation. Rien n'est supprimé :
 * l'historique est conservé et la cellule reste restaurable depuis Contrats, pôles et cellules › Archives.
 */
@Component({
  selector: 'app-effectifs-cellules',
  standalone: true,
  imports: [FormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="ky-card ef">
      <header class="ef-tete">
        <div>
          <h2>Effectifs par cellule</h2>
          <p class="ef-sous">Nombre de collaborateurs de chaque cellule active. Réaffectez ses agents vers un autre service, ou désactivez-la.</p>
        </div>
        <button type="button" class="ky-btn-secondary" (click)="charger()" [disabled]="chargement() || occupe()">Actualiser</button>
      </header>

      @if (erreur()) {
        <p class="ef-erreur">{{ erreur() }}</p>
      } @else {
        <div class="ef-kpis">
          <span><b>{{ totaux().cellules }}</b> cellules</span>
          <span><b>{{ totaux().agents }}</b> collaborateurs</span>
          <span><b>{{ totaux().pilotes }}</b> pilotes</span>
          <span [class.ef-alerte]="totaux().vides > 0"><b>{{ totaux().vides }}</b> cellule(s) vide(s)</span>
        </div>

        <div class="ef-filtres">
          <select class="ky-input" [ngModel]="contrat()" (ngModelChange)="contrat.set($event); pole.set('')" aria-label="Contrat">
            <option value="">Tous les contrats</option>
            @for (c of contrats(); track c.id) { <option [value]="c.id">{{ c.nom }}</option> }
          </select>
          <select class="ky-input" [ngModel]="pole()" (ngModelChange)="pole.set($event)" aria-label="Pôle">
            <option value="">Tous les pôles</option>
            @for (p of poles(); track p.id) { <option [value]="p.id">{{ p.nom }}</option> }
          </select>
          <input class="ky-input ef-recherche" type="search" placeholder="Rechercher une cellule…" [ngModel]="recherche()" (ngModelChange)="recherche.set($event)" />
          <label class="ef-check"><input type="checkbox" [ngModel]="videsSeulement()" (ngModelChange)="videsSeulement.set($event)" /> Cellules vides</label>
          <span class="ef-compte">{{ visibles().length }} / {{ lignes().length }}</span>
        </div>

        @if (selection(); as sel) {
          <section class="ef-panneau" [class.ef-panneau--warn]="mode() === 'desactiver'">
            <header class="ef-tete">
              <div>
                <h3>{{ mode() === 'desactiver' ? 'Désactiver' : 'Réaffecter les agents de' }} « {{ sel.cellule }} »</h3>
                <p class="ef-sous">{{ sel.contrat }} › {{ sel.pole }} — {{ sel.total }} collaborateur(s)</p>
              </div>
              <button type="button" class="ef-lien" (click)="fermer()">Annuler</button>
            </header>
            <div class="ef-panneau-grille">
              <div>
                @if (membres().pilotes.length) {
                  <label class="ef-check ef-tout">
                    <input type="checkbox" [checked]="toutCoche()" (change)="toutCocher()" [disabled]="mode() === 'desactiver'" />
                    Pilotes ({{ coches().size }} / {{ membres().pilotes.length }})
                  </label>
                  <ul class="ef-liste">
                    @for (a of membres().pilotes; track a.id) {
                      <li><label class="ef-check">
                        <input type="checkbox" [checked]="coches().has(a.id)" (change)="cocher(a.id)" [disabled]="mode() === 'desactiver'" />
                        {{ a.lastName }} {{ a.firstName }}
                      </label></li>
                    }
                  </ul>
                } @else {
                  <p class="ef-sous">Aucun pilote dans cette cellule.</p>
                }
                @if (membres().encadrement.length) {
                  <p class="ef-note">Responsables non déplacés (à corriger dans leur fiche) :
                    @for (a of membres().encadrement; track a.id; let last = $last) { {{ a.lastName }} {{ a.firstName }} ({{ a.role }}){{ last ? '' : ', ' }} }
                  </p>
                }
              </div>
              <div class="ef-cible">
                @if (membres().pilotes.length) {
                  <input class="ky-input" type="search" placeholder="Filtrer : contrat, pôle, cellule, service…" [ngModel]="rechercheDest()" (ngModelChange)="rechercheDest.set($event)" />
                  <label class="ef-champ"><span>Service de destination</span>
                    <select class="ky-input" [ngModel]="destination()" (ngModelChange)="destination.set($event)">
                      <option value="">Choisir…</option>
                      @for (g of groupesDest(); track g.groupe) {
                        <optgroup [label]="g.groupe">
                          @for (s of g.services; track s.id) { <option [value]="s.id">{{ s.nom }}</option> }
                        </optgroup>
                      }
                    </select></label>
                }
                @if (mode() === 'desactiver') {
                  <p class="ef-note">Les pilotes sont d'abord affectés au service choisi, puis la cellule et ses services sont archivés : ils disparaissent des listes, des périmètres et des quotas. Restaurable depuis Contrats, pôles et cellules › Archives.</p>
                }
                <button type="button" class="ky-btn-primary" [disabled]="!pret() || occupe()" (click)="valider()">
                  {{ libelleAction() }}
                </button>
              </div>
            </div>
          </section>
        }

        <div class="ef-table-wrap">
          <table class="ef-table">
            <thead>
              <tr>
                <th>Contrat › Pôle</th>
                <th>Cellule</th>
                <th class="ef-n">Services</th>
                <th class="ef-n">Pilotes</th>
                <th class="ef-n">Encadrement</th>
                <th class="ef-n">Total</th>
                @if (peutModifier()) { <th class="ef-actions">Actions</th> }
              </tr>
            </thead>
            <tbody>
              @for (l of visibles(); track l.celluleId) {
                <tr [class.ef-vide]="l.total === 0" [class.ef-active]="selection()?.celluleId === l.celluleId">
                  <td class="ef-chemin">{{ l.contrat }} › {{ l.pole }}</td>
                  <td><b>{{ l.cellule }}</b></td>
                  <td class="ef-n">{{ l.services }}</td>
                  <td class="ef-n">{{ l.pilotes }}</td>
                  <td class="ef-n">{{ l.encadrement || '' }}</td>
                  <td class="ef-n"><b>{{ l.total }}</b></td>
                  @if (peutModifier()) {
                    <td class="ef-actions">
                      <button type="button" class="ef-lien" (click)="ouvrir(l, 'reaffecter')" [disabled]="occupe() || l.pilotes === 0">Réaffecter</button>
                      <button type="button" class="ef-lien ef-lien--warn" (click)="ouvrir(l, 'desactiver')" [disabled]="occupe()">Désactiver</button>
                    </td>
                  }
                </tr>
              } @empty {
                <tr><td [attr.colspan]="peutModifier() ? 7 : 6" class="ef-sous">{{ chargement() ? 'Chargement…' : 'Aucune cellule ne correspond.' }}</td></tr>
              }
            </tbody>
          </table>
        </div>
      }
    </section>
  `,
  styles: [`
    .ef { display: flex; flex-direction: column; gap: 12px; }
    .ef-tete { display: flex; justify-content: space-between; align-items: flex-start; gap: 12px; }
    .ef h2 { margin: 0; font-size: 1.05rem; } .ef h3 { margin: 0; font-size: 0.95rem; }
    .ef-sous { margin: 2px 0 0; font-size: 0.8rem; color: var(--text-muted); }
    .ef-erreur { color: var(--danger, #b91c1c); }
    .ef-kpis { display: flex; flex-wrap: wrap; gap: 8px; }
    .ef-kpis span { padding: 6px 12px; border: 1px solid var(--border-color); border-radius: 8px; font-size: 0.8rem; color: var(--text-muted); }
    .ef-kpis b { color: var(--text-primary); font-size: 0.95rem; margin-right: 2px; }
    .ef-kpis .ef-alerte { border-color: var(--warning-border, #f59e0b); background: var(--warning-bg, #fffbeb); }
    .ef-filtres { display: flex; flex-wrap: wrap; gap: 8px; align-items: center; }
    .ef-filtres select.ky-input { max-width: 220px; } .ef-recherche { max-width: 240px; }
    .ef-check { display: inline-flex; align-items: center; gap: 6px; font-size: 0.82rem; }
    .ef-compte { margin-left: auto; font-size: 0.78rem; color: var(--text-muted); }
    .ef-table-wrap { overflow-x: auto; border: 1px solid var(--border-color); border-radius: 10px; }
    .ef-table { width: 100%; border-collapse: collapse; font-size: 0.84rem; }
    .ef-table th { text-align: left; font-size: 0.7rem; text-transform: uppercase; letter-spacing: 0.04em; color: var(--text-muted); padding: 8px 10px; border-bottom: 1px solid var(--border-color); background: var(--bg-input); }
    .ef-table td { padding: 7px 10px; border-bottom: 1px solid var(--border-color); }
    .ef-table tr:last-child td { border-bottom: 0; }
    .ef-n { text-align: right !important; font-variant-numeric: tabular-nums; width: 90px; }
    .ef-chemin { color: var(--text-muted); font-size: 0.78rem; }
    .ef-vide td { color: var(--text-muted); }
    .ef-active td { background: color-mix(in srgb, var(--ky-blue, #2563eb) 8%, transparent); }
    .ef-actions { text-align: right !important; white-space: nowrap; width: 170px; }
    .ef-lien { background: none; border: 0; padding: 2px 6px; color: var(--ky-blue, #2563eb); font-weight: 600; cursor: pointer; font-size: 0.8rem; }
    .ef-lien:disabled { opacity: 0.4; cursor: default; }
    .ef-lien--warn { color: var(--warning, #b45309); }
    .ef-panneau { border: 1px solid var(--ky-blue, #2563eb); border-radius: 10px; padding: 12px 14px; display: flex; flex-direction: column; gap: 10px; }
    .ef-panneau--warn { border-color: var(--warning, #d97706); }
    .ef-panneau-grille { display: grid; grid-template-columns: minmax(0, 1.4fr) minmax(240px, 1fr); gap: 16px; }
    @media (max-width: 760px) { .ef-panneau-grille { grid-template-columns: 1fr; } }
    .ef-liste { list-style: none; margin: 6px 0 0; padding: 0; columns: 2; max-height: 260px; overflow: auto; }
    .ef-tout { font-weight: 600; }
    .ef-note { font-size: 0.78rem; color: var(--text-muted); margin: 8px 0 0; }
    .ef-cible { display: flex; flex-direction: column; gap: 8px; }
    .ef-champ { display: flex; flex-direction: column; gap: 4px; font-size: 0.75rem; color: var(--text-muted); }
  `],
})
export class EffectifsCellulesComponent implements OnInit {
  private readonly org = inject(PrimeOrgApiService);
  private readonly api = inject(OrgAdminApiService);
  private readonly auth = inject(AuthService);
  private readonly access = inject(PlatformAccessService);
  private readonly toast = inject(KyntusToastService);
  private readonly confirm = inject(KyntusConfirmService);

  readonly chargement = signal(true);
  readonly occupe = signal(false);
  readonly erreur = signal<string | null>(null);
  private readonly depts = signal<OperationalDepartmentNode[]>([]);
  private readonly sansContrat = signal<OrgPoleNode[]>([]);
  private readonly agents = signal<OrgAgent[]>([]);

  readonly contrat = signal('');
  readonly pole = signal('');
  readonly recherche = signal('');
  readonly videsSeulement = signal(false);

  readonly selection = signal<LigneEffectif | null>(null);
  readonly mode = signal<Mode>('reaffecter');
  readonly coches = signal<ReadonlySet<string>>(new Set());
  readonly destination = signal('');
  readonly rechercheDest = signal('');

  private readonly role = (this.auth.getRole() || '').toLowerCase();
  readonly estAdmin = this.role === 'admin';
  /** Admin / RH, ou « Organisation » au niveau Modifier dans Accès par rôle (le serveur applique la même règle). */
  readonly peutModifier = computed(() =>
    this.role === 'admin' || this.role === 'rh' || (this.access.hasCatalog() && this.access.can('admin.organisation', 'Act')));

  readonly lignes = computed(() => lignesEffectifs(this.depts(), this.sansContrat(), this.agents()));
  readonly visibles = computed(() => filtrerEffectifs(this.lignes(), {
    contrat: this.contrat(), pole: this.pole(), recherche: this.recherche(), videsSeulement: this.videsSeulement(),
  }));
  readonly totaux = computed(() => totauxEffectifs(this.visibles()));
  readonly contrats = computed(() => uniques(this.lignes().map((l) => ({ id: l.contratId, nom: l.contrat }))));
  readonly poles = computed(() => uniques(this.lignes()
    .filter((l) => !this.contrat() || l.contratId === this.contrat())
    .map((l) => ({ id: l.poleId, nom: this.contrat() ? l.pole : `${l.contrat} › ${l.pole}` }))));

  private readonly cible = computed<CibleArchivage | null>(() => {
    const s = this.selection();
    return s ? { niveau: 'cellule', id: s.celluleId, nom: s.cellule, parentId: s.poleId, cellules: [], dejaArchive: false } : null;
  });
  readonly membres = computed(() => {
    const c = this.cible();
    return c ? repartirAgents(agentsDuNoeud(this.agents(), c)) : { pilotes: [], encadrement: [] };
  });
  readonly toutCoche = computed(() => this.membres().pilotes.length > 0 && this.membres().pilotes.every((a) => this.coches().has(a.id)));
  private readonly destinations = computed(() => {
    const c = this.cible();
    return c ? destinationsArchivage(this.depts(), this.sansContrat(), c) : [];
  });
  readonly groupesDest = computed(() => {
    const q = norm(this.rechercheDest().trim());
    const m = new Map<string, { groupe: string; services: { id: string; nom: string }[] }>();
    for (const d of this.destinations()) {
      if (q && d.id !== this.destination() && !norm(`${d.groupe} ${d.nom}`).includes(q)) continue;
      const g = m.get(d.groupe) ?? { groupe: d.groupe, services: [] };
      g.services.push({ id: d.id, nom: d.nom });
      m.set(d.groupe, g);
    }
    return [...m.values()];
  });
  readonly pret = computed(() => {
    if (!this.selection()) return false;
    if (this.mode() === 'desactiver') return this.membres().pilotes.length === 0 || !!this.destination();
    return this.coches().size > 0 && !!this.destination();
  });
  readonly libelleAction = computed(() => {
    if (this.mode() === 'reaffecter') return `Réaffecter ${this.coches().size} agent(s)`;
    return this.membres().pilotes.length ? 'Réaffecter et désactiver' : 'Désactiver la cellule';
  });

  ngOnInit(): void {
    this.charger();
  }

  charger(): void {
    this.chargement.set(true);
    this.erreur.set(null);
    this.org.invalidateOverviewCache();
    this.org.loadOverview().subscribe({
      next: (o) => {
        this.depts.set(o.operationalDepartments ?? []);
        this.sansContrat.set(o.unassignedPoles ?? []);
        this.agents.set((o.employees ?? []).map((e) => ({
          id: e.id, firstName: e.firstName ?? '', lastName: e.lastName ?? '', role: String(e.role ?? ''), email: e.email ?? '',
          poleId: e.poleId || null, celluleId: e.celluleId || null, serviceId: e.serviceId || null,
        })));
        // La cellule ouverte a pu être archivée entre-temps.
        const sel = this.selection();
        if (sel) this.selection.set(this.lignes().find((l) => l.celluleId === sel.celluleId) ?? null);
        this.chargement.set(false);
      },
      error: () => {
        this.chargement.set(false);
        this.erreur.set('Impossible de charger les effectifs. Actualisez ; si l’erreur persiste, vérifiez votre accès « Organisation ».');
      },
    });
  }

  ouvrir(l: LigneEffectif, mode: Mode): void {
    this.selection.set(l);
    this.mode.set(mode);
    this.destination.set('');
    this.rechercheDest.set('');
    this.coches.set(new Set(this.membres().pilotes.map((a) => a.id)));
  }

  fermer(): void {
    this.selection.set(null);
  }

  cocher(id: string): void {
    const s = new Set(this.coches());
    if (s.has(id)) s.delete(id); else s.add(id);
    this.coches.set(s);
  }

  toutCocher(): void {
    this.coches.set(this.toutCoche() ? new Set() : new Set(this.membres().pilotes.map((a) => a.id)));
  }

  async valider(): Promise<void> {
    const sel = this.selection();
    if (!sel || !this.pret() || this.occupe()) return;
    const desactiver = this.mode() === 'desactiver';
    const pilotes = desactiver ? this.membres().pilotes : this.membres().pilotes.filter((a) => this.coches().has(a.id));
    const dest = this.destinations().find((d) => d.id === this.destination());
    const ok = await this.confirm.confirm({
      title: desactiver ? `Désactiver « ${sel.cellule} »` : `Réaffecter vers ${dest?.nom ?? ''}`,
      message: [
        pilotes.length && dest ? `${pilotes.length} pilote(s) passent dans ${dest.groupe} › ${dest.nom} (affectation actuelle clôturée, historique conservé).` : '',
        desactiver ? `Puis « ${sel.cellule} » et ses services sont archivés (restaurables).` : '',
      ].filter(Boolean).join(' '),
      confirmLabel: desactiver ? 'Désactiver' : 'Réaffecter',
      variant: desactiver ? 'warning' : undefined,
    });
    if (!ok) return;

    this.occupe.set(true);
    const echecs: string[] = [];
    let deplaces = 0;
    for (const a of pilotes) {
      try {
        await firstValueFrom(this.api.moveAgent(a.id, dest!.id, {
          force: this.estAdmin,
          reason: desactiver ? `Désactivation de la cellule « ${sel.cellule} »` : `Réaffectation depuis « ${sel.cellule} »`,
        }));
        deplaces++;
      } catch (err) {
        const status = (err as { status?: number })?.status;
        echecs.push(`${a.lastName} ${a.firstName} : ${status === 422 ? 'durée minimale de rotation non atteinte (un Admin peut forcer)' : message(err)}`);
      }
    }
    try {
      if (echecs.length) {
        this.toast.error(`${echecs.length} agent(s) non déplacé(s)${desactiver ? ', la cellule n’est pas désactivée' : ''} : ${echecs.slice(0, 3).join(' — ')}`, 9000);
      } else if (desactiver) {
        await firstValueFrom(this.api.archiveNode('cellule', sel.celluleId, sel.poleId));
        this.toast.success(`${deplaces ? `${deplaces} pilote(s) réaffecté(s). ` : ''}« ${sel.cellule} » désactivée.`);
        this.selection.set(null);
      } else {
        this.toast.success(`${deplaces} agent(s) réaffecté(s) vers ${dest?.nom}.`);
      }
    } catch (err) {
      this.toast.error(`Désactivation impossible : ${message(err)}`);
    } finally {
      this.occupe.set(false);
      this.charger();
    }
  }
}

function uniques(xs: { id: string; nom: string }[]): { id: string; nom: string }[] {
  const m = new Map(xs.map((x) => [x.id, x]));
  return [...m.values()].sort((a, b) => a.nom.localeCompare(b.nom, 'fr', { sensitivity: 'base' }));
}

const norm = (t: string) => t.toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, '');

function message(err: unknown): string {
  const e = err as { status?: number; error?: { error?: string; message?: string } | string };
  if (e?.status === 403) return 'droit « Organisation » (Modifier) requis';
  if (typeof e?.error === 'string' && e.error.trim()) return e.error;
  if (e?.error && typeof e.error === 'object') return e.error.error || e.error.message || 'échec';
  return 'échec';
}
