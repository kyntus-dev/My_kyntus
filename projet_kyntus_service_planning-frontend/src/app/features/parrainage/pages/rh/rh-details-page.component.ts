import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { AlertTriangle, Loader2, X as XIcon } from 'lucide';
import { LucideIconComponent } from '@/shared/lucide-icon.component';
import { CvPreviewPanelComponent } from '../../components/cv-preview-panel.component';
import { ReferralService } from '../../services/referral.service';
import { ParrainageStoreService } from '../../services/parrainage-store.service';
import { ParrainageNavService } from '../../state/parrainage-nav.service';
import { ParrainageRoleService } from '../../state/parrainage-role.service';
import { KyntusToastService } from '../../../../shared/components/ui/kyntus-toast.service';
import type { ReferralStatus } from '../../models/referral.model';
import {
  REFERRAL_STATUS_LABELS,
  REFERRAL_STATUS_STYLES,
  referralHistoryActionLabel,
} from '../../utils/referral-status.util';

const HISTORY_ACTION_LABELS: Record<string, string> = {
  PRODUCTION_CONFIRMED: 'Passage en production',
  TRAINING_EXTENDED: 'Formation prolongée',
  TRAINING_END_DUE: 'Fin de formation atteinte',
  EARLY_DEPARTURE: 'Départ avant période minimale',
};

type ToastType = 'success' | 'error' | 'info';

@Component({
  selector: 'app-rh-details-page',
  standalone: true,
  imports: [FormsModule, LucideIconComponent, CvPreviewPanelComponent],
  template: `
    <section class="flex-1 min-w-0 space-y-6">
      <div class="space-y-2">
        <div class="ky-entete flex flex-wrap items-center justify-between gap-3">
          <div>
            <h1 class="prime-page-title">Décision RH</h1>
            <p class="text-sm text-muted mt-1">Validation uniquement depuis l'écran de détail.</p>
          </div>
          <div class="flex items-center gap-3">
            <span [class]="'inline-flex items-center rounded-full border px-2.5 py-0.5 text-xs font-medium ' + statusStyles[referral()?.status ?? 'SUBMITTED']">{{ statusLabels[referral()?.status ?? 'SUBMITTED'] }}</span>
            <button type="button" (click)="nav.setView('rh-management')" class="text-sm text-soft-blue hover:underline font-medium">
              ← Retour
            </button>
          </div>
        </div>
      </div>

      @if (loading()) {
        <div class="card-navy p-10 text-center text-muted text-sm">Chargement du dossier…</div>
      } @else if (!referral()) {
        <div class="card-navy p-10 text-center text-muted text-sm">
          Parrainage introuvable.
        </div>
      } @else {
        @if (referral(); as ref) {
          <div class="space-y-6">
            @if (unauthorized) {
              <div class="card-navy p-5 border-[var(--danger-border)] text-[var(--danger-text)]">
                Accès refusé. Seule la RH peut valider des dossiers.
              </div>
            }

            @if (ruleLabel()) {
              <div class="card-navy p-4 border border-[var(--info-border)] bg-[var(--info-bg)] text-sm text-[var(--info-text)]">
                Règle appliquée : {{ ruleLabel() }}
              </div>
            }

            <div class="grid grid-cols-1 lg:grid-cols-2 gap-6">
              <div class="card-navy p-5 md:p-6 space-y-4">
                <div class="flex items-start justify-between gap-3">
                  <h2 class="text-sm font-semibold text-primary">Candidat</h2>
                  <span class="text-xs text-muted font-mono">{{ ref.id }}</span>
                </div>
                <div class="space-y-2 text-sm">
                  <div class="flex justify-between gap-4">
                    <span class="text-muted">Nom</span>
                    <span class="text-primary text-right font-medium">{{ ref.candidateName }}</span>
                  </div>
                  <div class="flex justify-between gap-4">
                    <span class="text-muted">E-mail</span>
                    <span class="text-primary text-right break-all">{{ ref.candidateEmail }}</span>
                  </div>
                  <div class="flex justify-between gap-4">
                    <span class="text-muted">Téléphone</span>
                    <span class="text-primary text-right break-all">{{ ref.candidatePhone }}</span>
                  </div>
                  <div class="flex justify-between gap-4">
                    <span class="text-muted">Poste</span>
                    <span class="text-primary text-right font-medium">{{ ref.position }}</span>
                  </div>
                </div>
                @if (ref.notes) {
                  <div class="pt-2 border-t border-default">
                    <p class="text-xs text-muted mb-1">Notes du parrain</p>
                    <p class="text-sm text-primary whitespace-pre-wrap">{{ ref.notes }}</p>
                  </div>
                }
              </div>

              <div class="card-navy p-5 md:p-6 space-y-4">
                <h2 class="text-sm font-semibold text-primary">Parrain</h2>
                <div class="space-y-2 text-sm">
                  <div class="flex justify-between gap-4">
                    <span class="text-muted">Nom</span>
                    <span class="text-primary text-right font-medium">{{ ref.referrerName }}</span>
                  </div>
                  <div class="flex justify-between gap-4">
                    <span class="text-muted">Équipe</span>
                    <span class="text-primary text-right">{{ ref.teamId }}</span>
                  </div>
                  <div class="flex justify-between gap-4">
                    <span class="text-muted">Projet</span>
                    <span class="text-primary text-right">{{ ref.projectName }}</span>
                  </div>
                </div>
              </div>
            </div>

            <div class="grid grid-cols-1 xl:grid-cols-3 gap-6">
              <div class="card-navy p-5 md:p-6 xl:col-span-2">
                <app-cv-preview-panel [cvUrl]="ref.cvUrl" />
              </div>

              <div class="card-navy p-5 md:p-6 space-y-4">
                <h2 class="text-sm font-semibold text-primary">Chronologie</h2>
                <div class="space-y-3">
                  <div class="text-sm">
                    <div class="text-xs uppercase tracking-wide text-muted">Date de soumission</div>
                    <div class="text-primary mt-1 font-medium">
                      {{ fr(ref.createdAt) }}
                    </div>
                  </div>
                  <div class="space-y-2">
                    @if (statusHistory().length === 0) {
                      <p class="text-xs text-muted">Pas encore d'historique RH.</p>
                    } @else {
                      @for (h of statusHistory(); track h.id) {
                        <div class="flex items-start justify-between gap-4">
                          <div class="min-w-0">
                            <div class="flex items-center gap-2">
                              <span [class]="'inline-flex items-center rounded-full border px-2.5 py-0.5 text-xs font-medium ' + statusStyles[historyStatus(h.action)]">{{ statusLabels[historyStatus(h.action)] }}</span>
                              <span class="text-sm font-medium text-primary">
                                {{ historyActionLabel(h.action) }}
                              </span>
                            </div>
                            @if (h.comment) {
                              <p class="text-xs text-muted mt-1">Commentaire : {{ h.comment }}</p>
                            }
                            @if (h.rewardAmount !== undefined && h.rewardAmount !== null) {
                              <p class="text-xs text-purple-200 mt-1">Montant engagé : {{ h.rewardAmount }} DH</p>
                            }
                          </div>
                          <div class="text-right text-xs text-muted whitespace-nowrap">
                            {{ fr(h.createdAt) }}
                          </div>
                        </div>
                      }
                    }
                  </div>
                </div>
              </div>
            </div>

            @if (ref.status === 'PROCESSED') {
              <div class="card-navy p-4 border border-[var(--success-border)] bg-[var(--success-bg)] text-sm text-[var(--success-text)] space-y-3">
                <p>
                  Candidature consultée par la RH — validez l'entrée en créant le compte employé.
                  L'enregistrement du formulaire validera automatiquement le dossier parrainage.
                </p>
                @if (!ref.candidateEmployeeId) {
                  <button
                    type="button"
                    (click)="validateAndCreateEmployee(ref.id)"
                    class="rounded-lg border border-[var(--success-border)] bg-[var(--success-bg)] px-4 py-2 text-sm font-medium text-[var(--success-text)] hover:border-[var(--success)]"
                  >
                    Valider et créer l'employé
                  </button>
                } @else {
                  <p class="text-xs text-[var(--success-text)]">Employé lié : {{ ref.candidateEmployeeId }}</p>
                  <button
                    type="button"
                    (click)="validateAndCreateEmployee(ref.id)"
                    class="rounded-lg border border-[var(--success-border)] px-4 py-2 text-sm font-medium text-[var(--success-text)] hover:bg-[var(--success-bg)]"
                  >
                    Reprendre / finaliser
                  </button>
                }
              </div>
            }

            @if (ref.status === 'IN_TRAINING') {
              <div class="card-navy p-4 border border-[var(--warning-border)] bg-[var(--warning-bg)] text-sm text-[var(--warning-text)]">
                En cours de formation — la période minimum de prime n'est pas encore comptée.
                @if (ref.candidateStartDate) {
                  <span class="block mt-1">Début formation : {{ ref.candidateStartDate }}</span>
                }
                @if (ref.trainingEndDate) {
                  <span class="block mt-1">Fin prévue : {{ ref.trainingEndDate }}</span>
                }
                @if (ref.trainingEndNotifiedAt) {
                  <span class="block mt-1 text-[var(--warning-text)]">Fin de formation atteinte — confirmez le passage en production ou prolongez la formation.</span>
                }
                Montant engagé : {{ ref.rewardAmount }} DH.
              </div>
            }

            @if (ref.status === 'APPROVED' || ref.status === 'REWARDED') {
              <div class="card-navy p-4 border border-[var(--success-border)] bg-[var(--success-bg)] text-sm text-[var(--success-text)]">
                @if (ref.status === 'APPROVED') {
                  Période d'ancienneté jusqu'au {{ formatDate(ref.eligibleForPaymentAt) }} —
                  montant engagé {{ ref.rewardAmount }} DH.
                  @if (ref.productionStartDate) {
                    <span class="block mt-1 text-primary">Production depuis le {{ ref.productionStartDate }}.</span>
                  }
                  @if (ref.paymentStatus === 'NOT_ELIGIBLE') {
                    <span class="block mt-1 text-primary">En attente de la fin de la période minimum.</span>
                  }
                  @if (ref.paymentStatus === 'AWAITING_RH') {
                    <span class="block mt-1 text-[var(--warning-text)]">
                      Période écoulée — confirmez que le candidat est toujours en poste avant transmission à la comptabilité.
                    </span>
                    @if (ref.employmentCheckSummary) {
                      <span class="block mt-1 text-primary">
                        Contrat : {{ ref.employmentCheckSummary.contractStatus || '—' }}
                        @if (ref.employmentCheckSummary.blockReason) {
                          — {{ ref.employmentCheckSummary.blockReason }}
                        }
                      </span>
                    }
                  }
                  @if (ref.paymentStatus === 'READY') {
                    <span class="block mt-1 text-[var(--warning-text)]">Éligibilité confirmée — en attente du service comptabilité.</span>
                  }
                } @else {
                  Prime versée le {{ formatDate(ref.paidAt) }} ({{ ref.rewardAmount }} DH).
                }
              </div>
            }

            <div class="card-navy p-5 md:p-6 space-y-4">
              <div class="flex items-center justify-between gap-3">
                <h2 class="text-sm font-semibold text-primary">Décision</h2>
                @if (ref.status === 'REJECTED') {
                  <span class="text-xs text-[var(--danger-text)] inline-flex items-center gap-2">
                    <app-lucide-icon [icon]="alertIcon" className="h-3.5 w-3.5" />
                    Rejeté
                  </span>
                }
              </div>

              <div class="flex flex-wrap gap-3">
                @if (referral()?.status === 'SUBMITTED' && !hasCv()) {
                  <p class="w-full text-xs text-[var(--warning-text)]">
                    CV candidat manquant — le pilote doit joindre un CV avant le traitement RH.
                  </p>
                }
                @if (canProcess()) {
                  <button
                    type="button"
                    (click)="handleProcessClick()"
                    [disabled]="busy() || unauthorized"
                    class="rounded-lg border border-[var(--info-border)] bg-[var(--info-bg)] px-4 py-2 text-sm font-medium text-[var(--info-text)] hover:border-[var(--soft-blue)] disabled:opacity-50"
                  >
                    Marquer comme consulté
                  </button>
                }
                @if (canConfirmProduction()) {
                  <button
                    type="button"
                    (click)="handleConfirmProductionClick()"
                    [disabled]="busy() || unauthorized"
                    class="rounded-lg border border-[var(--success-border)] bg-[var(--success-bg)] px-4 py-2 text-sm font-medium text-[var(--success-text)] hover:border-[var(--success)] disabled:opacity-50"
                  >
                    Confirmer passage en production
                  </button>
                }
                @if (productionAutoConfirmed()) {
                  <span class="rounded-lg border border-[var(--success-border)] bg-[var(--success-bg)] px-3 py-2 text-sm text-[var(--success-text)]">
                    Passage en production confirmé automatiquement (Formation)
                  </span>
                }
                @if (canExtendTraining()) {
                  <button
                    type="button"
                    (click)="handleExtendTrainingClick()"
                    [disabled]="busy() || unauthorized"
                    class="rounded-lg border border-[var(--warning-border)] bg-[var(--warning-bg)] px-4 py-2 text-sm font-medium text-[var(--warning-text)] hover:border-[var(--warning)] disabled:opacity-50"
                  >
                    Prolonger la formation
                  </button>
                }
                @if (canConfirmEligibility()) {
                  <button
                    type="button"
                    (click)="handleConfirmEligibilityClick()"
                    [disabled]="busy() || unauthorized"
                    class="rounded-lg border border-[var(--warning-border)] bg-[var(--warning-bg)] px-4 py-2 text-sm font-medium text-[var(--warning-text)] hover:border-[var(--warning)] disabled:opacity-50"
                  >
                    Confirmer l'éligibilité
                  </button>
                }
                @if (canRejectEarlyDeparture()) {
                  <button
                    type="button"
                    (click)="handleEarlyDepartureClick()"
                    [disabled]="busy() || unauthorized"
                    class="rounded-lg border border-[var(--warning-border)] px-4 py-2 text-sm font-medium text-[var(--warning-text)] hover:bg-[var(--warning-bg)] disabled:opacity-50"
                  >
                    Recrue partie (avant période min.)
                  </button>
                }
                <button
                  type="button"
                  (click)="handleRejectClick()"
                  [disabled]="!canReject() || busy() || unauthorized"
                  class="rounded-lg border border-[var(--danger-border)] px-4 py-2 text-sm font-medium text-[var(--danger-text)] hover:bg-[var(--danger-bg)] disabled:opacity-50 disabled:hover:bg-transparent"
                >
                  Rejeter
                </button>
              </div>

              @if (mode() === 'process' && canProcess()) {
                <div class="space-y-4">
                  <p class="text-xs text-muted">
                    Confirmez que la candidature a été examinée (CV, entretiens). Le dossier passera en statut « Consulté ».
                  </p>
                  <div>
                    <label class="block text-xs font-medium uppercase tracking-wide text-muted mb-1.5">
                      Commentaire (facultatif)
                    </label>
                    <textarea
                      class="w-full min-h-[70px] rounded-lg border border-default bg-input/40 px-3 py-2 text-sm text-primary"
                      [(ngModel)]="processComment"
                      placeholder="Notes internes RH…"
                    ></textarea>
                  </div>
                  <button
                    type="button"
                    (click)="confirmOpen.set('process')"
                    [disabled]="busy() || unauthorized"
                    class="rounded-lg border border-[var(--info-border)] bg-[var(--info-bg)] px-4 py-2 text-sm font-medium text-[var(--info-text)] hover:border-[var(--soft-blue)] disabled:opacity-50"
                  >
                    Confirmer le traitement
                  </button>
                </div>
              }

              @if (mode() === 'confirm-production' && canConfirmProduction()) {
                <div class="space-y-4">
                  <p class="text-xs text-muted">
                    Confirmez la date d'entrée en production. La période minimum de {{ suggestedMinDuration() }} mois démarrera à cette date.
                  </p>
                  <div>
                    <label class="block text-xs font-medium uppercase tracking-wide text-muted mb-1.5">
                      Date de début production
                    </label>
                    <input
                      type="date"
                      class="w-full rounded-lg border border-default bg-input/40 px-3 py-2 text-sm text-primary focus:outline-none focus:ring-1 focus:ring-soft-blue"
                      [(ngModel)]="productionStartDate"
                    />
                  </div>
                  <div>
                    <label class="block text-xs font-medium uppercase tracking-wide text-muted mb-1.5">
                      Commentaire (facultatif)
                    </label>
                    <textarea
                      class="w-full min-h-[70px] rounded-lg border border-default bg-input/40 px-3 py-2 text-sm text-primary"
                      [(ngModel)]="productionComment"
                      placeholder="Notes internes RH…"
                    ></textarea>
                  </div>
                  <button
                    type="button"
                    (click)="confirmOpen.set('confirm-production')"
                    [disabled]="busy() || unauthorized"
                    class="rounded-lg border border-[var(--success-border)] bg-[var(--success-bg)] px-4 py-2 text-sm font-medium text-[var(--success-text)] hover:border-[var(--success)] disabled:opacity-50"
                  >
                    Confirmer le passage en production
                  </button>
                </div>
              }

              @if (mode() === 'extend-training' && canExtendTraining()) {
                <div class="space-y-4">
                  <p class="text-xs text-muted">
                    Saisissez une nouvelle date de fin de formation (postérieure à la date actuelle).
                  </p>
                  <div>
                    <label class="block text-xs font-medium uppercase tracking-wide text-muted mb-1.5">
                      Nouvelle date de fin de formation
                    </label>
                    <input
                      type="date"
                      class="w-full rounded-lg border border-default bg-input/40 px-3 py-2 text-sm text-primary focus:outline-none focus:ring-1 focus:ring-soft-blue"
                      [(ngModel)]="extendTrainingEndDate"
                    />
                  </div>
                  <div>
                    <label class="block text-xs font-medium uppercase tracking-wide text-muted mb-1.5">
                      Commentaire (facultatif)
                    </label>
                    <textarea
                      class="w-full min-h-[70px] rounded-lg border border-default bg-input/40 px-3 py-2 text-sm text-primary"
                      [(ngModel)]="extendTrainingComment"
                      placeholder="Motif de la prolongation…"
                    ></textarea>
                  </div>
                  <button
                    type="button"
                    (click)="confirmOpen.set('extend-training')"
                    [disabled]="busy() || unauthorized"
                    class="rounded-lg border border-[var(--warning-border)] bg-[var(--warning-bg)] px-4 py-2 text-sm font-medium text-[var(--warning-text)] hover:border-[var(--warning)] disabled:opacity-50"
                  >
                    Prolonger la formation
                  </button>
                </div>
              }

              @if (mode() === 'confirm-eligibility' && canConfirmEligibility()) {
                <div class="space-y-4">
                  <p class="text-xs text-muted">
                    Vérifiez que le candidat recruté est toujours en poste avant de transmettre le dossier à la comptabilité.
                  </p>
                  <div>
                    <label class="block text-xs font-medium uppercase tracking-wide text-muted mb-1.5">
                      Commentaire (facultatif)
                    </label>
                    <textarea
                      class="w-full min-h-[70px] rounded-lg border border-default bg-input/40 px-3 py-2 text-sm text-primary"
                      [(ngModel)]="eligibilityComment"
                      placeholder="Ex. : candidat toujours en poste au {{ formatDate(referral()?.eligibleForPaymentAt) }}…"
                    ></textarea>
                  </div>
                  <button
                    type="button"
                    (click)="confirmOpen.set('confirm-eligibility')"
                    [disabled]="busy() || unauthorized"
                    class="rounded-lg border border-[var(--warning-border)] bg-[var(--warning-bg)] px-4 py-2 text-sm font-medium text-[var(--warning-text)] hover:border-[var(--warning)] disabled:opacity-50"
                  >
                    Confirmer et transmettre à la compta
                  </button>
                </div>
              }

              @if (mode() === 'early-departure' && canRejectEarlyDeparture()) {
                <div class="space-y-4">
                  <p class="text-xs text-muted">
                    La recrue a quitté l'entreprise avant la fin de la période minimale ({{ suggestedMinDuration() }} mois).
                    Le dossier sera rejeté et la prime de parrainage annulée automatiquement.
                  </p>
                  <div>
                    <label class="block text-xs font-medium uppercase tracking-wide text-muted mb-1.5">
                      Date de départ
                    </label>
                    <input
                      type="date"
                      class="w-full rounded-lg border border-default bg-input/40 px-3 py-2 text-sm text-primary focus:outline-none focus:ring-1 focus:ring-soft-blue"
                      [(ngModel)]="earlyDepartureDate"
                    />
                  </div>
                  <div>
                    <label class="block text-xs font-medium uppercase tracking-wide text-muted mb-1.5">
                      Commentaire (facultatif)
                    </label>
                    <textarea
                      class="w-full min-h-[70px] rounded-lg border border-default bg-input/40 px-3 py-2 text-sm text-primary"
                      [(ngModel)]="earlyDepartureComment"
                      placeholder="Précisions sur le départ…"
                    ></textarea>
                  </div>
                  <button
                    type="button"
                    (click)="confirmOpen.set('early-departure')"
                    [disabled]="busy() || unauthorized || !earlyDepartureDate"
                    class="rounded-lg border border-[var(--warning-border)] bg-[var(--warning-bg)] px-4 py-2 text-sm font-medium text-[var(--warning-text)] hover:border-[var(--warning)] disabled:opacity-50"
                  >
                    Confirmer le départ anticipé
                  </button>
                </div>
              }

              @if (mode() === 'reject' && canReject()) {
                <div class="space-y-4">
                  <div>
                    <label class="block text-xs font-medium uppercase tracking-wide text-muted mb-1.5">
                      Commentaire (facultatif)
                    </label>
                    <textarea
                      class="w-full min-h-[90px] rounded-lg border border-default bg-input/40 px-3 py-2 text-sm text-primary focus:outline-none focus:ring-1 focus:ring-soft-blue"
                      [(ngModel)]="rejectComment"
                      placeholder="Motif du refus…"
                    ></textarea>
                  </div>
                  <button
                    type="button"
                    (click)="confirmOpen.set('reject')"
                    [disabled]="busy() || unauthorized"
                    class="rounded-lg border border-[var(--danger-border)] px-4 py-2 text-sm font-medium text-[var(--danger-text)] hover:bg-[var(--danger-bg)] disabled:opacity-50"
                  >
                    Rejeter
                  </button>
                </div>
              }

              @if (ref.status === 'REWARDED' || ref.status === 'REJECTED' || ref.status === 'APPROVED') {
                <p class="text-xs text-muted">
                  Décision finale enregistrée. Les actions sont désactivées.
                </p>
              }
            </div>
          </div>
        }
      }
    </section>

    @if (confirmOpen() === 'process') {
      <div class="fixed inset-0 z-50 flex items-center justify-center p-4">
        <button type="button" class="absolute inset-0 bg-app/80 backdrop-blur-sm" aria-label="Fermer" (click)="confirmOpen.set(null)"></button>
        <div class="relative card-navy max-w-md w-full p-6 shadow-2xl border border-default">
          <div class="flex items-start justify-between gap-4">
            <h3 class="text-lg font-semibold text-primary">Marquer comme consulté</h3>
            <button type="button" class="rounded-lg p-1 text-muted hover:text-primary hover:bg-input" (click)="confirmOpen.set(null)" aria-label="Fermer">
              <app-lucide-icon [icon]="xIcon" className="h-5 w-5" />
            </button>
          </div>
          <p class="mt-3 text-sm text-muted leading-relaxed">
            Candidat : {{ referral()?.candidateName ?? '' }}.
            Le dossier passera en « Consulté » en attente de la création du compte employé.
          </p>
          <div class="mt-6 flex flex-wrap justify-end gap-2">
            <button type="button" (click)="confirmOpen.set(null)" class="rounded-lg border border-default px-4 py-2 text-sm text-primary hover:bg-input/80" [disabled]="busy()">
              Annuler
            </button>
            <button type="button" (click)="handleConfirm()" class="rounded-lg px-4 py-2 text-sm font-medium border border-[var(--info-border)] bg-[var(--info-bg)] text-[var(--info-text)] hover:border-[var(--soft-blue)]" [disabled]="busy()">
              @if (busy()) {
                <span class="inline-flex items-center gap-2">
                  <app-lucide-icon [icon]="loaderIcon" className="h-4 w-4 animate-spin" />
                  Traitement…
                </span>
              } @else {
                Confirmer
              }
            </button>
          </div>
        </div>
      </div>
    }

    @if (confirmOpen() === 'confirm-eligibility') {
      <div class="fixed inset-0 z-50 flex items-center justify-center p-4">
        <button type="button" class="absolute inset-0 bg-app/80 backdrop-blur-sm" aria-label="Fermer" (click)="confirmOpen.set(null)"></button>
        <div class="relative card-navy max-w-md w-full p-6 shadow-2xl border border-default">
          <div class="flex items-start justify-between gap-4">
            <h3 class="text-lg font-semibold text-primary">Confirmer l'éligibilité</h3>
            <button type="button" class="rounded-lg p-1 text-muted hover:text-primary hover:bg-input" (click)="confirmOpen.set(null)" aria-label="Fermer">
              <app-lucide-icon [icon]="xIcon" className="h-5 w-5" />
            </button>
          </div>
          <p class="mt-3 text-sm text-muted leading-relaxed">
            Candidat : {{ referral()?.candidateName ?? '' }}.
            Vous confirmez que le candidat est toujours en poste et que la prime peut être transmise à la comptabilité ({{ referral()?.rewardAmount ?? 0 }} DH).
          </p>
          <div class="mt-6 flex flex-wrap justify-end gap-2">
            <button type="button" (click)="confirmOpen.set(null)" class="rounded-lg border border-default px-4 py-2 text-sm text-primary hover:bg-input/80" [disabled]="busy()">
              Annuler
            </button>
            <button type="button" (click)="handleConfirm()" class="rounded-lg px-4 py-2 text-sm font-medium border border-[var(--warning-border)] bg-[var(--warning-bg)] text-[var(--warning-text)] hover:border-[var(--warning)]" [disabled]="busy()">
              @if (busy()) {
                <span class="inline-flex items-center gap-2">
                  <app-lucide-icon [icon]="loaderIcon" className="h-4 w-4 animate-spin" />
                  Traitement…
                </span>
              } @else {
                Confirmer
              }
            </button>
          </div>
        </div>
      </div>
    }

    @if (confirmOpen() === 'confirm-production') {
      <div class="fixed inset-0 z-50 flex items-center justify-center p-4">
        <button type="button" class="absolute inset-0 bg-app/80 backdrop-blur-sm" aria-label="Fermer" (click)="confirmOpen.set(null)"></button>
        <div class="relative card-navy max-w-md w-full p-6 shadow-2xl border border-default">
          <div class="flex items-start justify-between gap-4">
            <h3 class="text-lg font-semibold text-primary">Confirmer le passage en production</h3>
            <button type="button" class="rounded-lg p-1 text-muted hover:text-primary hover:bg-input" (click)="confirmOpen.set(null)" aria-label="Fermer">
              <app-lucide-icon [icon]="xIcon" className="h-5 w-5" />
            </button>
          </div>
          <p class="mt-3 text-sm text-muted leading-relaxed">
            Candidat : {{ referral()?.candidateName ?? '' }}.
            Production à partir du {{ productionStartDate || '—' }} — décompte de {{ suggestedMinDuration() }} mois.
          </p>
          <div class="mt-6 flex flex-wrap justify-end gap-2">
            <button type="button" (click)="confirmOpen.set(null)" class="rounded-lg border border-default px-4 py-2 text-sm text-primary hover:bg-input/80" [disabled]="busy()">
              Annuler
            </button>
            <button type="button" (click)="handleConfirm()" class="rounded-lg px-4 py-2 text-sm font-medium border border-[var(--success-border)] bg-[var(--success-bg)] text-[var(--success-text)] hover:border-[var(--success)]" [disabled]="busy()">
              @if (busy()) {
                <span class="inline-flex items-center gap-2">
                  <app-lucide-icon [icon]="loaderIcon" className="h-4 w-4 animate-spin" />
                  Traitement…
                </span>
              } @else {
                Confirmer
              }
            </button>
          </div>
        </div>
      </div>
    }

    @if (confirmOpen() === 'extend-training') {
      <div class="fixed inset-0 z-50 flex items-center justify-center p-4">
        <button type="button" class="absolute inset-0 bg-app/80 backdrop-blur-sm" aria-label="Fermer" (click)="confirmOpen.set(null)"></button>
        <div class="relative card-navy max-w-md w-full p-6 shadow-2xl border border-default">
          <div class="flex items-start justify-between gap-4">
            <h3 class="text-lg font-semibold text-primary">Prolonger la formation</h3>
            <button type="button" class="rounded-lg p-1 text-muted hover:text-primary hover:bg-input" (click)="confirmOpen.set(null)" aria-label="Fermer">
              <app-lucide-icon [icon]="xIcon" className="h-5 w-5" />
            </button>
          </div>
          <p class="mt-3 text-sm text-muted leading-relaxed">
            Candidat : {{ referral()?.candidateName ?? '' }}.
            Nouvelle fin de formation : {{ extendTrainingEndDate || '—' }}.
          </p>
          <div class="mt-6 flex flex-wrap justify-end gap-2">
            <button type="button" (click)="confirmOpen.set(null)" class="rounded-lg border border-default px-4 py-2 text-sm text-primary hover:bg-input/80" [disabled]="busy()">
              Annuler
            </button>
            <button type="button" (click)="handleConfirm()" class="rounded-lg px-4 py-2 text-sm font-medium border border-[var(--warning-border)] bg-[var(--warning-bg)] text-[var(--warning-text)] hover:border-[var(--warning)]" [disabled]="busy()">
              @if (busy()) {
                <span class="inline-flex items-center gap-2">
                  <app-lucide-icon [icon]="loaderIcon" className="h-4 w-4 animate-spin" />
                  Traitement…
                </span>
              } @else {
                Confirmer
              }
            </button>
          </div>
        </div>
      </div>
    }

    @if (confirmOpen() === 'reject') {
      <div class="fixed inset-0 z-50 flex items-center justify-center p-4">
        <button type="button" class="absolute inset-0 bg-app/80 backdrop-blur-sm" aria-label="Fermer" (click)="confirmOpen.set(null)"></button>
        <div class="relative card-navy max-w-md w-full p-6 shadow-2xl border border-default">
          <div class="flex items-start justify-between gap-4">
            <h3 class="text-lg font-semibold text-primary">Confirmer le refus</h3>
            <button type="button" class="rounded-lg p-1 text-muted hover:text-primary hover:bg-input" (click)="confirmOpen.set(null)" aria-label="Fermer">
              <app-lucide-icon [icon]="xIcon" className="h-5 w-5" />
            </button>
          </div>
          <p class="mt-3 text-sm text-muted leading-relaxed">Candidat : {{ referral()?.candidateName ?? '' }}. Le statut passera à « Rejeté ».</p>
          <div class="mt-6 flex flex-wrap justify-end gap-2">
            <button type="button" (click)="confirmOpen.set(null)" class="rounded-lg border border-default px-4 py-2 text-sm text-primary hover:bg-input/80" [disabled]="busy()">
              Annuler
            </button>
            <button type="button" (click)="handleConfirm()" class="rounded-lg px-4 py-2 text-sm font-medium ky-btn-danger" [disabled]="busy()">
              @if (busy()) {
                <span class="inline-flex items-center gap-2">
                  <app-lucide-icon [icon]="loaderIcon" className="h-4 w-4 animate-spin" />
                  Traitement…
                </span>
              } @else {
                Confirmer
              }
            </button>
          </div>
        </div>
      </div>
    }

    @if (confirmOpen() === 'early-departure') {
      <div class="fixed inset-0 z-50 flex items-center justify-center p-4">
        <button type="button" class="absolute inset-0 bg-app/80 backdrop-blur-sm" aria-label="Fermer" (click)="confirmOpen.set(null)"></button>
        <div class="relative card-navy max-w-md w-full p-6 shadow-2xl border border-default">
          <div class="flex items-start justify-between gap-4">
            <h3 class="text-lg font-semibold text-primary">Départ avant période minimale</h3>
            <button type="button" class="rounded-lg p-1 text-muted hover:text-primary hover:bg-input" (click)="confirmOpen.set(null)" aria-label="Fermer">
              <app-lucide-icon [icon]="xIcon" className="h-5 w-5" />
            </button>
          </div>
          <p class="mt-3 text-sm text-muted leading-relaxed">
            Candidat : {{ referral()?.candidateName ?? '' }}.
            Départ le {{ earlyDepartureDate || '—' }} — le dossier sera rejeté et la prime annulée.
          </p>
          <div class="mt-6 flex flex-wrap justify-end gap-2">
            <button type="button" (click)="confirmOpen.set(null)" class="rounded-lg border border-default px-4 py-2 text-sm text-primary hover:bg-input/80" [disabled]="busy()">
              Annuler
            </button>
            <button type="button" (click)="handleConfirm()" class="rounded-lg px-4 py-2 text-sm font-medium border border-[var(--warning-border)] bg-[var(--warning-bg)] text-[var(--warning-text)] hover:border-[var(--warning)]" [disabled]="busy()">
              @if (busy()) {
                <span class="inline-flex items-center gap-2">
                  <app-lucide-icon [icon]="loaderIcon" className="h-4 w-4 animate-spin" />
                  Traitement…
                </span>
              } @else {
                Confirmer le rejet
              }
            </button>
          </div>
        </div>
      </div>
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RhDetailsPageComponent {
  private readonly referralService = inject(ReferralService);
  private readonly store = inject(ParrainageStoreService);
  readonly nav = inject(ParrainageNavService);
  private readonly role = inject(ParrainageRoleService);
  private readonly router = inject(Router);
  private readonly toastSvc = inject(KyntusToastService);

  readonly String = String;

  readonly statusStyles = REFERRAL_STATUS_STYLES;
  readonly statusLabels = REFERRAL_STATUS_LABELS;
  readonly alertIcon = AlertTriangle;
  readonly loaderIcon = Loader2;
  readonly xIcon = XIcon;

  readonly loading = computed(() => this.store.loading());
  readonly referral = computed(() => {
    const rid = this.nav.selectedReferralId();
    if (!rid) return null;
    return this.store.referrals().find((r) => r.id === rid) ?? null;
  });
  readonly history = computed(() => {
    const rid = this.nav.selectedReferralId();
    if (!rid) return [];
    return this.store.history().filter((h) => h.referralId === rid);
  });
  readonly mode = signal<'none' | 'process' | 'confirm-production' | 'extend-training' | 'confirm-eligibility' | 'early-departure' | 'reject'>('none');
  productionStartDate = '';
  extendTrainingEndDate = '';
  earlyDepartureDate = '';
  earlyDepartureComment = '';
  processComment = '';
  rejectComment = '';
  eligibilityComment = '';
  productionComment = '';
  extendTrainingComment = '';
  readonly busy = signal(false);
  readonly confirmOpen = signal<null | 'process' | 'confirm-production' | 'extend-training' | 'confirm-eligibility' | 'early-departure' | 'reject'>(null);

  get id(): string {
    return this.nav.selectedReferralId() ?? '';
  }

  get unauthorized(): boolean {
    return this.role.user().role !== 'RH';
  }

  readonly hasCv = computed(() => !!this.referral()?.cvUrl?.trim());
  readonly canProcess = computed(() => this.referral()?.status === 'SUBMITTED' && this.hasCv());
  readonly canConfirmEligibility = computed(
    () => this.referral()?.status === 'APPROVED' && this.referral()?.paymentStatus === 'AWAITING_RH',
  );
  readonly canConfirmProduction = computed(() => this.referral()?.status === 'IN_TRAINING');
  readonly productionAutoConfirmed = computed(() => {
    const r = this.referral();
    if (!r || r.status === 'IN_TRAINING') return false;
    return this.history().some(
      (h) =>
        h.action === 'PRODUCTION_CONFIRMED' &&
        (h.performedById === 'formation-system' ||
          (h.comment ?? '').toLowerCase().includes('formation') ||
          (h.performedByLabel ?? '').toLowerCase() === 'formation'),
    );
  });
  readonly canExtendTraining = computed(() => this.referral()?.status === 'IN_TRAINING');
  readonly canRejectEarlyDeparture = computed(() => {
    const r = this.referral();
    if (!r) return false;
    return (r.status === 'APPROVED' || r.status === 'IN_TRAINING') && r.paymentStatus !== 'PAID';
  });
  readonly canReject = computed(() => {
    const r = this.referral();
    return r ? r.status === 'SUBMITTED' || r.status === 'PROCESSED' : false;
  });

  readonly statusHistory = computed(() =>
    this.history()
      .slice()
      .sort((a, b) => a.createdAt.getTime() - b.createdAt.getTime())
      .filter((h) => h.action !== 'SUBMITTED'),
  );

  readonly suggestedReward = computed(() => {
    if (!this.id) return 500;
    return this.referralService.getSuggestedReward(this.id);
  });

  readonly suggestedMinDuration = computed(() => {
    if (!this.id) return 6;
    return this.referralService.getSuggestedMinDuration(this.id);
  });

  readonly ruleLabel = computed(() => {
    if (!this.id) return '';
    return this.referralService.getRuleLabelForReferral(this.id);
  });

  historyStatus(action: string): ReferralStatus {
    if (action === 'PROCESSED') return 'PROCESSED';
    if (action === 'IN_TRAINING') return 'IN_TRAINING';
    if (action === 'PRODUCTION_CONFIRMED') return 'APPROVED';
    if (action === 'EARLY_DEPARTURE') return 'REJECTED';
    return action === 'APPROVED' ? 'APPROVED' : action === 'REJECTED' ? 'REJECTED' : 'REWARDED';
  }

  historyActionLabel(action: string): string {
    if (HISTORY_ACTION_LABELS[action]) return HISTORY_ACTION_LABELS[action];
    return referralHistoryActionLabel(action);
  }

  private actor() {
    const u = this.role.user();
    return { id: u.id ?? 'rh-1', label: u.name ?? 'RH' };
  }

  private showToast(type: ToastType, message: string): void {
    if (type === 'error') this.toastSvc.error(message);
    else if (type === 'info') this.toastSvc.info(message);
    else this.toastSvc.success(message);
  }

  handleProcessClick(): void {
    this.processComment = '';
    this.mode.set('process');
  }

  handleConfirmProductionClick(): void {
    const ref = this.referral();
    this.productionStartDate = ref?.trainingEndDate ?? new Date().toISOString().slice(0, 10);
    this.productionComment = '';
    this.mode.set('confirm-production');
  }

  handleExtendTrainingClick(): void {
    this.extendTrainingEndDate = '';
    this.extendTrainingComment = '';
    this.mode.set('extend-training');
  }

  handleEarlyDepartureClick(): void {
    this.earlyDepartureDate = new Date().toISOString().slice(0, 10);
    this.earlyDepartureComment = '';
    this.mode.set('early-departure');
  }

  handleRejectClick(): void {
    this.rejectComment = '';
    this.mode.set('reject');
  }

  handleConfirmEligibilityClick(): void {
    this.eligibilityComment = '';
    this.mode.set('confirm-eligibility');
  }

  async handleConfirm(): Promise<void> {
    const id = this.id;
    if (!id || !this.referral() || this.busy()) return;
    this.busy.set(true);
    try {
      if (this.confirmOpen() === 'process') {
        const updated = await this.referralService.processReferral(
          id,
          this.processComment || undefined,
          this.actor(),
        );
        if (!updated) throw new Error('Échec du marquage.');
        this.showToast('success', 'Dossier marqué comme consulté.');
        this.mode.set('none');
        this.confirmOpen.set(null);
        return;
      }
      if (this.confirmOpen() === 'confirm-production') {
        if (!this.productionStartDate) {
          this.showToast('error', 'Date de début production requise.');
          this.busy.set(false);
          this.confirmOpen.set(null);
          return;
        }
        const updated = await this.referralService.confirmProductionStart(
          id,
          {
            productionStartDate: this.productionStartDate,
            comment: this.productionComment || undefined,
          },
          this.actor(),
        );
        if (!updated) throw new Error('Échec de la confirmation production.');
        this.showToast('success', 'Passage en production confirmé — période d\'ancienneté démarrée.');
        this.mode.set('none');
        this.confirmOpen.set(null);
        return;
      }
      if (this.confirmOpen() === 'extend-training') {
        if (!this.extendTrainingEndDate) {
          this.showToast('error', 'Nouvelle date de fin requise.');
          this.busy.set(false);
          this.confirmOpen.set(null);
          return;
        }
        const updated = await this.referralService.extendTraining(
          id,
          {
            trainingEndDate: this.extendTrainingEndDate,
            comment: this.extendTrainingComment || undefined,
          },
          this.actor(),
        );
        if (!updated) throw new Error('Échec de la prolongation.');
        this.showToast('success', 'Formation prolongée.');
        this.mode.set('none');
        this.confirmOpen.set(null);
        return;
      }
      if (this.confirmOpen() === 'confirm-eligibility') {
        const updated = await this.referralService.confirmPaymentEligibility(
          id,
          this.eligibilityComment || undefined,
          this.actor(),
        );
        if (!updated) throw new Error('Échec de la confirmation.');
        this.showToast('success', 'Éligibilité confirmée — dossier transmis à la comptabilité.');
        this.mode.set('none');
        this.confirmOpen.set(null);
        return;
      }
      if (this.confirmOpen() === 'early-departure') {
        if (!this.earlyDepartureDate) {
          this.showToast('error', 'Date de départ requise.');
          this.busy.set(false);
          this.confirmOpen.set(null);
          return;
        }
        const updated = await this.referralService.rejectEarlyDeparture(
          id,
          {
            departureDate: this.earlyDepartureDate,
            comment: this.earlyDepartureComment || undefined,
          },
          this.actor(),
        );
        if (!updated) throw new Error('Échec de l’enregistrement du départ.');
        this.showToast('success', 'Départ anticipé enregistré — dossier rejeté, prime annulée.');
        this.mode.set('none');
        this.confirmOpen.set(null);
        return;
      }
      if (this.confirmOpen() === 'reject') {
        await this.referralService.updateStatus(id, 'REJECTED', this.actor(), this.rejectComment || undefined);
        this.showToast('success', 'Décision enregistrée (rejet).');
        this.mode.set('none');
        this.confirmOpen.set(null);
      }
    } catch (error) {
      const msg = error instanceof Error ? error.message : 'Action impossible. Réessayez.';
      this.showToast('error', msg);
      this.confirmOpen.set(null);
    } finally {
      this.busy.set(false);
    }
  }

  fr(d: Date): string {
    return new Date(d).toLocaleDateString('fr-FR');
  }

  formatDate(d?: Date): string {
    if (!d) return '—';
    return this.fr(d);
  }

  validateAndCreateEmployee(referralId: string): void {
    void this.router.navigate(['/users/create'], {
      queryParams: { referralId, fromParrainage: '1' },
    });
  }
}
