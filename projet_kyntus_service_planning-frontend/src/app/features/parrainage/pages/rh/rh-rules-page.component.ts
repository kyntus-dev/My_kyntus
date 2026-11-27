import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ReferralService } from '../../services/referral.service';
import { ParrainageRoleService } from '../../state/parrainage-role.service';
import type { ReferralRule, ReferralRuleStatus, ReferralRuleType } from '../../models/referral.model';

@Component({
  selector: 'app-rh-rules-page',
  standalone: true,
  imports: [FormsModule],
  template: `
    <section class="flex-1 min-w-0 space-y-6">
      @if (unauthorized) {
        <div class="card-navy p-10 text-center text-[var(--danger-text)] text-sm">
          Accès refusé. Réservé à la RH.
        </div>
      }
      <div class="ky-entete">
        <h1 class="prime-page-title">Règles de parrainage</h1>
        <p class="text-sm text-muted mt-1">Gérez les règles métier (hors configuration système).</p>
      </div>

      <div class="grid grid-cols-1 xl:grid-cols-3 gap-6">
        <div class="card-navy p-5 md:p-6 xl:col-span-1 space-y-4">
          <h2 class="text-sm font-semibold text-primary">
            {{ editingId() ? 'Modifier la règle' : 'Créer une règle' }}
          </h2>

          <div class="space-y-3">
            <div>
              <label class="block text-xs uppercase tracking-wide text-muted mb-1.5">
                Intitulé de la règle
              </label>
              <input
                class="w-full rounded-lg border border-default bg-input/40 px-3 py-2 text-sm text-primary focus:outline-none focus:border-blue-500 focus:ring-1 focus:ring-blue-500/50"
                [(ngModel)]="name"
                placeholder="Ex. : Prime par poste — Développeur"
              />
            </div>

            <div class="grid grid-cols-1 sm:grid-cols-2 gap-3">
              <div>
                <label class="block text-xs uppercase tracking-wide text-muted mb-1.5">
                  Type
                </label>
                <select
                  class="w-full rounded-lg border border-default bg-input/40 px-3 py-2 text-sm text-primary focus:outline-none focus:border-blue-500 focus:ring-1 focus:ring-blue-500/50"
                  [(ngModel)]="type"
                >
                  <option value="REWARD_PER_POSITION">Prime selon le poste</option>
                  <option value="REWARD_AFTER_PROBATION">Prime après période d'essai</option>
                </select>
              </div>
              <div>
                <label class="block text-xs uppercase tracking-wide text-muted mb-1.5">
                  Statut
                </label>
                <select
                  class="w-full rounded-lg border border-default bg-input/40 px-3 py-2 text-sm text-primary focus:outline-none focus:border-blue-500 focus:ring-1 focus:ring-blue-500/50"
                  [(ngModel)]="status"
                >
                  <option value="ACTIVE">Actif</option>
                  <option value="PAUSED">En pause</option>
                </select>
              </div>
            </div>

            @if (type === 'REWARD_PER_POSITION') {
              <div>
                <label class="block text-xs uppercase tracking-wide text-muted mb-1.5">
                  Poste cible
                </label>
                <input
                  class="w-full rounded-lg border border-default bg-input/40 px-3 py-2 text-sm text-primary focus:outline-none focus:border-blue-500 focus:ring-1 focus:ring-blue-500/50"
                  [(ngModel)]="target"
                  placeholder="Ex. : Développeur"
                />
              </div>
              <div>
                <label class="block text-xs uppercase tracking-wide text-muted mb-1.5">
                  Durée minimum (mois)
                </label>
                <select
                  class="w-full rounded-lg border border-default bg-input/40 px-3 py-2 text-sm text-primary focus:outline-none focus:border-blue-500 focus:ring-1 focus:ring-blue-500/50"
                  [(ngModel)]="minDurationMonths"
                >
                  @for (m of durationOptions; track m) {
                    <option [ngValue]="m">{{ m }} mois</option>
                  }
                </select>
              </div>
            }

            <div>
              <label class="block text-xs uppercase tracking-wide text-muted mb-1.5">
                Montant (DH)
              </label>
              <input
                class="w-full rounded-lg border border-default bg-input/40 px-3 py-2 text-sm text-primary focus:outline-none focus:border-blue-500 focus:ring-1 focus:ring-blue-500/50"
                [(ngModel)]="value"
                placeholder="Ex. : 600"
              />
            </div>
          </div>

          <div class="flex flex-wrap gap-2">
            <button
              type="button"
              (click)="submit()"
              [disabled]="!canSubmit()"
              class="flex-1 min-w-[160px] ky-btn-primary disabled:opacity-50"
            >
              {{ editingId() ? 'Enregistrer les modifications' : 'Créer la règle' }}
            </button>
            <button
              type="button"
              (click)="resetForm()"
              class="flex-1 min-w-[160px] rounded-lg border border-default px-4 py-2 text-sm text-primary hover:bg-input/80"
            >
              Réinitialiser
            </button>
          </div>
        </div>

        <div class="card-navy p-5 md:p-6 xl:col-span-2 space-y-4">
          <h2 class="text-sm font-semibold text-primary">Règles</h2>

          @if (loading()) {
            <div class="text-sm text-muted py-10 text-center">Chargement…</div>
          } @else if (rules().length === 0) {
            <div class="text-sm text-muted py-10 text-center">Aucune règle.</div>
          } @else {
            <div class="overflow-x-auto">
              <table class="w-full text-left border-collapse">
                <thead>
                  <tr class="bg-card/50 border-b border-default">
                    <th class="px-6 py-4 text-[11px] font-bold text-muted uppercase tracking-wider">Intitulé</th>
                    <th class="px-6 py-4 text-[11px] font-bold text-muted uppercase tracking-wider">Type</th>
                    <th class="px-6 py-4 text-[11px] font-bold text-muted uppercase tracking-wider">Montant</th>
                    <th class="px-6 py-4 text-[11px] font-bold text-muted uppercase tracking-wider">Durée min.</th>
                    <th class="px-6 py-4 text-[11px] font-bold text-muted uppercase tracking-wider">Statut</th>
                    <th class="px-6 py-4 text-[11px] font-bold text-muted uppercase tracking-wider text-right">Actions</th>
                  </tr>
                </thead>
                <tbody class="divide-y divide-default">
                  @for (r of rules(); track r.id) {
                    <tr class="hover:bg-input/30 transition-colors">
                      <td class="px-6 py-4 text-sm font-medium text-primary whitespace-nowrap">{{ r.name }}</td>
                      <td class="px-6 py-4 text-sm text-primary whitespace-nowrap">
                        {{ typeLabel(r) }}
                      </td>
                      <td class="px-6 py-4 text-sm text-primary whitespace-nowrap">
                        {{ amountLabel(r) }}
                      </td>
                      <td class="px-6 py-4 text-sm text-primary whitespace-nowrap">
                        {{ durationLabel(r) }}
                      </td>
                      <td class="px-6 py-4">
                        <span [class]="'ky-badge ' + (r.status === 'ACTIVE' ? 'ky-badge--success' : 'ky-badge--warning')">{{ r.status === 'ACTIVE' ? 'Actif' : 'En pause' }}</span>
                      </td>
                      <td class="px-6 py-4 text-right">
                        <div class="inline-flex items-center gap-2">
                          <button
                            type="button"
                            (click)="startEdit(r)"
                            class="text-xs text-blue-500 hover:underline font-medium"
                          >
                            Modifier
                          </button>
                          <button
                            type="button"
                            (click)="deleteTargetId.set(r.id)"
                            class="text-xs text-[var(--danger-text)] hover:underline font-medium"
                          >
                            Supprimer
                          </button>
                        </div>
                      </td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          }
        </div>
      </div>

      <div></div>
    </section>

    @if (deleteTargetId()) {
      <div class="fixed inset-0 z-50 flex items-center justify-center p-4">
        <button type="button" class="absolute inset-0 bg-app/80 backdrop-blur-sm" aria-label="Fermer" (click)="deleteTargetId.set(null)"></button>
        <div class="relative card-navy max-w-md w-full p-6 shadow-2xl border border-default">
          <div class="flex items-start justify-between gap-4">
            <h3 class="text-lg font-semibold text-primary">Supprimer cette règle ?</h3>
          </div>
          <p class="mt-3 text-sm text-muted leading-relaxed">Cette action est définitive. La règle ne sera plus utilisée pour suggérer les montants de prime.</p>
          <div class="mt-6 flex flex-wrap justify-end gap-2">
            <button type="button" (click)="deleteTargetId.set(null)" class="rounded-lg border border-default px-4 py-2 text-sm text-primary hover:bg-input/80">
              Annuler
            </button>
            <button type="button" (click)="confirmDelete()" class="ky-btn-danger px-4 py-2 text-sm">
              Supprimer
            </button>
          </div>
        </div>
      </div>
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RhRulesPageComponent {
  private readonly referrals = inject(ReferralService);
  private readonly role = inject(ParrainageRoleService);

  readonly loading = signal(true);
  readonly rules = signal<ReferralRule[]>([]);

  readonly editingId = signal<string | null>(null);
  readonly durationOptions = [3, 6, 9, 12];
  name = '';
  type: ReferralRuleType = 'REWARD_PER_POSITION';
  target = '';
  value = '';
  minDurationMonths = 6;
  status: ReferralRuleStatus = 'ACTIVE';

  readonly deleteTargetId = signal<string | null>(null);

  get unauthorized(): boolean {
    return this.role.user().role !== 'RH';
  }

  typeLabel(r: ReferralRule): string {
    return r.type === 'REWARD_PER_POSITION' ? 'Prime selon le poste' : 'Prime après période d\'essai';
  }

  amountLabel(r: ReferralRule): string {
    return r.type === 'REWARD_PER_POSITION' && r.target ? `${r.value} DH (${r.target})` : `${r.value} DH`;
  }

  durationLabel(r: ReferralRule): string {
    if (r.type !== 'REWARD_PER_POSITION') return '—';
    return `${r.minDurationMonths ?? 6} mois`;
  }

  canSubmit(): boolean {
    const v = Number(this.value.replace(',', '.'));
    if (!this.name.trim()) return false;
    if (!Number.isFinite(v) || v <= 0) return false;
    if (this.type === 'REWARD_PER_POSITION' && !this.target.trim()) return false;
    if (this.type === 'REWARD_PER_POSITION' && !this.durationOptions.includes(this.minDurationMonths)) return false;
    return true;
  }

  constructor() {
    this.refresh();
    this.loading.set(false);
  }

  private refresh(): void {
    this.rules.set(this.referrals.getRules());
  }

  async submit(): Promise<void> {
    const v = Number(this.value.replace(',', '.'));
    const target = this.type === 'REWARD_PER_POSITION' ? this.target.trim() : undefined;
    const saved = await this.referrals.upsertRule({
      id: this.editingId() ?? undefined,
      name: this.name.trim(),
      type: this.type,
      value: v,
      target,
      minDurationMonths: this.type === 'REWARD_PER_POSITION' ? this.minDurationMonths : 6,
      status: this.status,
    });
    this.editingId.set(saved.id);
    this.status = saved.status;
    this.name = saved.name;
    this.type = saved.type;
    this.target = saved.target ?? '';
    this.minDurationMonths = saved.minDurationMonths ?? 6;
    this.value = String(saved.value);
    this.refresh();
  }

  resetForm(): void {
    this.editingId.set(null);
    this.name = '';
    this.type = 'REWARD_PER_POSITION';
    this.target = '';
    this.value = '';
    this.minDurationMonths = 6;
    this.status = 'ACTIVE';
  }

  startEdit(r: ReferralRule): void {
    this.editingId.set(r.id);
    this.name = r.name;
    this.type = r.type;
    this.target = r.target ?? '';
    this.minDurationMonths = r.minDurationMonths ?? 6;
    this.value = String(r.value);
    this.status = r.status;
    this.deleteTargetId.set(null);
  }

  confirmDelete(): void {
    const id = this.deleteTargetId();
    if (!id) return;
    void this.referrals.deleteRule(id).then(() => this.refresh());
    this.deleteTargetId.set(null);
    this.refresh();
    if (this.editingId() === id) this.resetForm();
  }
}
