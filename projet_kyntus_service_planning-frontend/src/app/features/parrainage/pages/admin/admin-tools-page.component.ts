import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { Search, AlertTriangle, Bug, Wrench, Check, X, Pencil, Copy } from 'lucide';
import { LucideIconComponent } from '@/shared/lucide-icon.component';
import { StatusBadgeComponent } from '../../components/status-badge.component';
import { AccessDeniedComponent } from '../../components/access-denied.component';
import { ReferralService } from '../../services/referral.service';
import { ParrainageStoreService } from '../../services/parrainage-store.service';
import { AdminService } from '../../services/admin.service';
import { ParrainageRoleService } from '../../state/parrainage-role.service';
import type { Referral, ReferralStatus } from '../../models/referral.model';
import { REFERRAL_STATUS_LABELS } from '../../utils/referral-status.util';

type DebugTab = 'referral' | 'config' | 'logs';

const EDIT_FIELDS: ReadonlyArray<[keyof Referral, string]> = [
  ['candidateName', 'Nom du candidat'],
  ['candidateEmail', 'E-mail du candidat'],
  ['candidatePhone', 'Téléphone'],
  ['position', 'Poste'],
  ['projectName', 'Projet'],
];

const STATUS_OPTIONS: ReadonlyArray<[ReferralStatus, string]> = (
  Object.entries(REFERRAL_STATUS_LABELS) as [ReferralStatus, string][]
);

@Component({
  selector: 'app-admin-tools-page',
  standalone: true,
  imports: [LucideIconComponent, StatusBadgeComponent, AccessDeniedComponent],
  template: `
    @if (role !== 'ADMIN') {
      <app-access-denied
        message="Cette page est réservée au rôle Admin."
        [backLabel]="role === 'RH' ? 'Retour au tableau de bord RH' : 'Retour'"
      />
    } @else {
      <section class="space-y-6">
        <div class="ky-entete">
          <h1 class="prime-page-title flex items-center gap-2">
            <app-lucide-icon [icon]="wrenchIcon" className="w-7 h-7 text-blue-500 shrink-0" />
            Outils administrateur
          </h1>
          <p class="text-sm text-muted mt-1">
            Recherche de parrainages, actions rapides, anomalies et débogage (données locales).
          </p>
        </div>

        @if (anomalies().duplicateCandidates.length > 0 || anomalies().suspiciousEmails.length > 0) {
          <div class="card-navy border-amber-500/40 bg-amber-500/5 p-4">
            <div class="flex items-center gap-2 text-[var(--warning-text)] font-semibold text-sm mb-2">
              <app-lucide-icon [icon]="alertIcon" className="w-4 h-4" />
              Anomalies détectées
            </div>
            <ul class="text-sm text-primary space-y-1 list-disc list-inside">
              @for (d of anomalies().duplicateCandidates; track d.email) {
                <li>
                  Candidat en doublon (même e-mail) : <span class="font-mono text-[var(--warning-text)]">{{ d.email }}</span> —
                  {{ d.referrals.length }} dossiers
                </li>
              }
            </ul>
          </div>
        }

        <div class="card-navy p-4 space-y-4">
          <div class="relative">
            <app-lucide-icon [icon]="searchIcon" className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-muted" />
            <input
              [value]="query()"
              (input)="query.set($any($event.target).value)"
              placeholder="Rechercher par nom, e-mail, ID parrainage…"
              class="w-full bg-input border border-default rounded-lg pl-10 pr-4 py-2.5 text-sm text-primary placeholder-slate-500 focus:outline-none focus:border-blue-500 focus:ring-1 focus:ring-blue-500/50"
            />
          </div>

          <div class="overflow-x-auto">
            <table class="w-full text-left border-collapse">
              <thead>
                <tr class="bg-card/50 border-b border-default">
                  <th class="px-4 py-3 text-[11px] font-bold text-muted uppercase tracking-wider">ID</th>
                  <th class="px-4 py-3 text-[11px] font-bold text-muted uppercase tracking-wider">Candidat</th>
                  <th class="px-4 py-3 text-[11px] font-bold text-muted uppercase tracking-wider">E-mail</th>
                  <th class="px-4 py-3 text-[11px] font-bold text-muted uppercase tracking-wider">Statut</th>
                  <th class="px-4 py-3 text-[11px] font-bold text-muted uppercase tracking-wider text-right">Actions</th>
                </tr>
              </thead>
              <tbody class="divide-y divide-default">
                @for (r of filtered(); track r.id) {
                  <tr class="hover:bg-input/30 transition-colors">
                    <td class="px-4 py-3 text-xs font-mono text-muted">{{ r.id }}</td>
                    <td class="px-4 py-3 text-sm text-primary">{{ r.candidateName }}</td>
                    <td class="px-4 py-3 text-sm text-muted">{{ r.candidateEmail }}</td>
                    <td class="px-4 py-3"><app-status-badge [status]="r.status" /></td>
                    <td class="px-4 py-3 text-right">
                      <div class="inline-flex flex-wrap gap-1 justify-end">
                        <button type="button" title="Afficher le JSON" class="p-2 rounded-lg text-muted hover:bg-input hover:text-blue-400" (click)="selectedJson.set(r)">
                          <app-lucide-icon [icon]="bugIcon" className="w-4 h-4" />
                        </button>
                        <button type="button" title="Forcer la validation" class="p-2 rounded-lg text-muted hover:bg-[var(--success-bg)] hover:text-[var(--success-text)]" (click)="forceApprove(r.id)">
                          <app-lucide-icon [icon]="checkIcon" className="w-4 h-4" />
                        </button>
                        <button type="button" title="Forcer le refus" class="p-2 rounded-lg text-muted hover:bg-[var(--danger-bg)] hover:text-[var(--danger-text)]" (click)="forceReject(r.id)">
                          <app-lucide-icon [icon]="xIcon" className="w-4 h-4" />
                        </button>
                        <button type="button" title="Éditer" class="p-2 rounded-lg text-muted hover:bg-blue-500/10 hover:text-blue-400" (click)="openEdit(r)">
                          <app-lucide-icon [icon]="pencilIcon" className="w-4 h-4" />
                        </button>
                      </div>
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        </div>

        <div class="card-navy overflow-hidden">
          <div class="flex border-b border-default">
            @for (t of debugTabs; track t) {
              <button
                type="button"
                (click)="debugTab.set(t)"
                [class]="'px-4 py-3 text-sm font-medium transition-colors ' + (debugTab() === t ? 'bg-card/50 text-blue-400 border-b-2 border-blue-500' : 'text-muted hover:text-primary')"
              >
                {{ tabLabel(t) }}
              </button>
            }
            <button type="button" (click)="copyExport()" class="ml-auto px-4 py-3 text-sm text-muted hover:text-primary flex items-center gap-1">
              <app-lucide-icon [icon]="copyIcon" className="w-4 h-4" />
              Copier export complet
            </button>
          </div>
          <div class="p-4 max-h-[420px] overflow-auto">
            <pre class="text-xs font-mono text-muted whitespace-pre-wrap break-all">{{ debugContent() }}</pre>
          </div>
        </div>

        @if (editOpen()) {
          <div class="fixed inset-0 z-[60] flex items-center justify-center p-4">
            <button type="button" class="absolute inset-0 bg-app/80 backdrop-blur-sm" aria-label="Fermer" (click)="editOpen.set(null)"></button>
            <div class="relative card-navy max-w-lg w-full p-6 border border-default shadow-2xl z-[61]">
              <h3 class="text-lg font-semibold text-primary mb-4">Édition manuelle — {{ editOpen()!.id }}</h3>
              <div class="grid gap-3">
                @for (f of editFields; track f[0]) {
                  <div>
                    <label class="text-xs font-bold text-muted uppercase">{{ f[1] }}</label>
                    <input
                      class="w-full mt-1 bg-input border border-default rounded-lg px-3 py-2 text-sm text-primary focus:border-[var(--soft-blue)] focus:ring-1 focus:ring-[var(--info-border)]"
                      [value]="draftValue(f[0])"
                      (input)="setDraft(f[0], $any($event.target).value)"
                    />
                  </div>
                }
                <div>
                  <label class="text-xs font-bold text-muted uppercase">Statut</label>
                  <select
                    class="w-full mt-1 bg-input border border-default rounded-lg px-3 py-2 text-sm text-primary"
                    [value]="editDraft().status ?? editOpen()!.status"
                    (change)="setDraft('status', $any($event.target).value)"
                  >
                    @for (s of statusOptions; track s[0]) {
                      <option [value]="s[0]">{{ s[1] }}</option>
                    }
                  </select>
                </div>
                <div>
                  <label class="text-xs font-bold text-muted uppercase">Montant récompense (€)</label>
                  <input
                    type="number"
                    class="w-full mt-1 bg-input border border-default rounded-lg px-3 py-2 text-sm text-primary"
                    [value]="editDraft().rewardAmount ?? 0"
                    (input)="setDraft('rewardAmount', $any($event.target).value)"
                  />
                </div>
              </div>
              <div class="flex justify-end gap-2 mt-6">
                <button type="button" class="px-4 py-2 rounded-lg border border-default text-primary hover:bg-input" (click)="editOpen.set(null)">Annuler</button>
                <button type="button" class="ky-btn-primary px-4 py-2" (click)="saveEdit()">Enregistrer</button>
              </div>
            </div>
          </div>
        }
      </section>
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminToolsPageComponent {
  private readonly referrals = inject(ReferralService);
  private readonly store = inject(ParrainageStoreService);
  private readonly admin = inject(AdminService);
  private readonly roleSvc = inject(ParrainageRoleService);

  readonly searchIcon = Search;
  readonly alertIcon = AlertTriangle;
  readonly bugIcon = Bug;
  readonly wrenchIcon = Wrench;
  readonly checkIcon = Check;
  readonly xIcon = X;
  readonly pencilIcon = Pencil;
  readonly copyIcon = Copy;

  readonly debugTabs: DebugTab[] = ['referral', 'config', 'logs'];
  readonly editFields = EDIT_FIELDS;
  readonly statusOptions = STATUS_OPTIONS;

  readonly query = signal('');
  readonly debugTab = signal<DebugTab>('referral');
  readonly selectedJson = signal<Referral | null>(null);
  readonly editOpen = signal<Referral | null>(null);
  readonly editDraft = signal<Partial<Referral>>({});
  get role() {
    return this.roleSvc.user().role;
  }

  private get actor() {
    const u = this.roleSvc.user();
    return { id: u.id, label: u.name || 'Support technique' };
  }

  readonly anomalies = computed(() => {
    this.store.referrals();
    return this.referrals.detectAnomalies();
  });

  readonly filtered = computed(() => {
    const all = this.store.referrals();
    const q = this.query().trim().toLowerCase();
    if (!q) return all;
    return all.filter(
      (r) =>
        r.id.toLowerCase().includes(q) ||
        r.candidateName.toLowerCase().includes(q) ||
        r.candidateEmail.toLowerCase().includes(q) ||
        r.referrerName.toLowerCase().includes(q),
    );
  });

  readonly debugContent = computed(() => {
    const tab = this.debugTab();
    if (tab === 'referral') {
      const sel = this.selectedJson();
      return sel ? JSON.stringify(sel, null, 2) : 'Sélectionnez une ligne (icône bug) ou choisissez un ID dans le tableau.';
    }
    if (tab === 'config') {
      return JSON.stringify(this.admin.getSystemConfig(), null, 2);
    }
    return JSON.stringify(
      {
        historySample: this.store.history().slice(0, 40),
        audit: this.store.auditLog().slice(0, 40),
      },
      null,
      2,
    );
  });

  tabLabel(t: DebugTab): string {
    if (t === 'referral') return 'JSON parrainage (sélection)';
    if (t === 'config') return 'Config système';
    return 'Journaux et audit';
  }

  forceApprove(id: string): void {
    void this.referrals.forceApprove(id, this.actor);
  }

  forceReject(id: string): void {
    void this.referrals.forceReject(id, this.actor);
  }

  openEdit(r: Referral): void {
    this.editDraft.set({
      candidateName: r.candidateName,
      candidateEmail: r.candidateEmail,
      candidatePhone: r.candidatePhone,
      position: r.position,
      projectName: r.projectName,
      status: r.status,
      rewardAmount: r.rewardAmount,
    });
    this.editOpen.set(r);
  }

  draftValue(field: keyof Referral): string {
    return (this.editDraft()[field] as string | undefined) ?? '';
  }

  setDraft(field: keyof Referral, value: string): void {
    const v = field === 'rewardAmount' ? Number(value) : value;
    this.editDraft.update((d) => ({ ...d, [field]: v }));
  }

  async saveEdit(): Promise<void> {
    const target = this.editOpen();
    if (!target) return;
    const d = this.editDraft();
    await this.referrals.updateReferralManual(
      target.id,
      {
        candidateName: d.candidateName,
        candidateEmail: d.candidateEmail,
        candidatePhone: d.candidatePhone,
        position: d.position,
        projectName: d.projectName,
        status: d.status,
        rewardAmount: d.rewardAmount,
      },
      this.actor,
    );
    this.editOpen.set(null);
  }

  async copyExport(): Promise<void> {
    const text = await this.referrals.exportDataSnapshot();
    void navigator.clipboard.writeText(text);
  }
}
