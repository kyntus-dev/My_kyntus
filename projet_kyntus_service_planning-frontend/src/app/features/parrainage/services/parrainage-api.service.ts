import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import type {
  NotificationPreferences,
  Referral,
  ReferralHistoryEntry,
  ReferralNotification,
  ReferralRewardPreview,
  ReferralRule,
  ReferralRuleCatalogItem,
  ReferralStatus,
  RoleFilter,
} from '../models/referral.model';
import type { AuditLogEntry, SystemConfig } from '../models/system-config.model';

const PREFIX = '/api/parrainage';

type Actor = { id: string; label: string; role?: string };

interface RawReferral extends Omit<Referral, 'createdAt' | 'approvedAt' | 'eligibleForPaymentAt' | 'paidAt' | 'trainingEndNotifiedAt'> {
  createdAt: string;
  approvedAt?: string;
  eligibleForPaymentAt?: string;
  paidAt?: string;
  trainingEndNotifiedAt?: string;
}
interface RawHistory extends Omit<ReferralHistoryEntry, 'createdAt'> {
  createdAt: string;
}
interface RawRule extends Omit<ReferralRule, 'createdAt' | 'minDurationMonths'> {
  createdAt: string;
  minDurationMonths?: number;
}
interface RawNotification extends Omit<ReferralNotification, 'createdAt'> {
  createdAt: string;
}
interface RawAudit extends Omit<AuditLogEntry, 'timestamp'> {
  timestamp: string;
}

export interface AnomaliesResult {
  duplicateCandidates: { email: string; referrals: Referral[] }[];
  suspiciousEmails: { email: string; count: number; referralIds: string[] }[];
}

export interface ParrainageOrgNodeRow {
  id: string;
  parentId?: string | null;
  email: string;
  role: string;
  name: string;
}

export interface ExportSnapshot {
  exportedAt: string;
  referrals: Referral[];
  rules: ReferralRule[];
  history: ReferralHistoryEntry[];
  notifications: ReferralNotification[];
  notificationPreferences?: NotificationPreferences;
  systemConfig?: SystemConfig;
  auditLog: AuditLogEntry[];
}

@Injectable({ providedIn: 'root' })
export class ParrainageApiService {
  private readonly http = inject(HttpClient);

  async getOrgNodes(): Promise<ParrainageOrgNodeRow[]> {
    return firstValueFrom(this.http.get<ParrainageOrgNodeRow[]>(`${PREFIX}/org/nodes`));
  }

  async getReferrals(): Promise<Referral[]> {
    const rows = await firstValueFrom(this.http.get<RawReferral[]>(`${PREFIX}/referrals`));
    return rows.map(reviveReferral);
  }

  async getReferral(id: string): Promise<Referral> {
    return reviveReferral(await firstValueFrom(this.http.get<RawReferral>(`${PREFIX}/referrals/${id}`)));
  }

  async createReferral(body: {
    referrerId: string;
    referrerName: string;
    candidateName: string;
    candidateEmail: string;
    candidatePhone: string;
    ruleId?: string;
    position?: string;
    project?: string;
    notes?: string;
  }): Promise<Referral> {
    return reviveReferral(await firstValueFrom(this.http.post<RawReferral>(`${PREFIX}/referrals`, body)));
  }

  async uploadReferralCv(id: string, file: File): Promise<Referral> {
    const form = new FormData();
    form.append('file', file, file.name);
    return reviveReferral(
      await firstValueFrom(this.http.post<RawReferral>(`${PREFIX}/referrals/${id}/cv`, form)),
    );
  }

  async patchReferral(
    id: string,
    body: Partial<Referral> & { actor?: Actor },
  ): Promise<Referral> {
    return reviveReferral(await firstValueFrom(this.http.patch<RawReferral>(`${PREFIX}/referrals/${id}`, body)));
  }

  async updateStatus(id: string, status: ReferralStatus, actor?: Actor, comment?: string): Promise<Referral> {
    return reviveReferral(
      await firstValueFrom(
        this.http.post<RawReferral>(`${PREFIX}/referrals/${id}/status`, { status, comment, actor }),
      ),
    );
  }

  async assignReward(id: string, amount: number, actor?: Actor): Promise<Referral> {
    return reviveReferral(
      await firstValueFrom(this.http.post<RawReferral>(`${PREFIX}/referrals/${id}/reward`, { amount, actor })),
    );
  }

  async approveReferral(
    id: string,
    body: {
      candidateStartDate: string;
      rewardAmount: number;
      requiresTraining?: boolean;
      trainingEndDate?: string;
      comment?: string;
      actor?: Actor;
    },
  ): Promise<Referral> {
    return reviveReferral(
      await firstValueFrom(this.http.post<RawReferral>(`${PREFIX}/referrals/${id}/approve`, body)),
    );
  }

  async confirmProductionStart(
    id: string,
    body: { productionStartDate: string; comment?: string; actor?: Actor },
  ): Promise<Referral> {
    return reviveReferral(
      await firstValueFrom(
        this.http.post<RawReferral>(`${PREFIX}/referrals/${id}/confirm-production`, body),
      ),
    );
  }

  async extendTraining(
    id: string,
    body: { trainingEndDate: string; comment?: string; actor?: Actor },
  ): Promise<Referral> {
    return reviveReferral(
      await firstValueFrom(
        this.http.post<RawReferral>(`${PREFIX}/referrals/${id}/extend-training`, body),
      ),
    );
  }

  async rejectEarlyDeparture(
    id: string,
    body: { departureDate?: string; comment?: string; actor?: Actor },
  ): Promise<Referral> {
    return reviveReferral(
      await firstValueFrom(
        this.http.post<RawReferral>(`${PREFIX}/referrals/${id}/reject-early-departure`, body),
      ),
    );
  }

  async processReferral(id: string, body: { comment?: string; actor?: Actor }): Promise<Referral> {
    return reviveReferral(
      await firstValueFrom(this.http.post<RawReferral>(`${PREFIX}/referrals/${id}/process`, body)),
    );
  }

  async confirmPaymentEligibility(
    id: string,
    body: { comment?: string; actor?: Actor },
  ): Promise<Referral> {
    return reviveReferral(
      await firstValueFrom(
        this.http.post<RawReferral>(`${PREFIX}/referrals/${id}/confirm-eligibility`, body),
      ),
    );
  }

  async getOnboardingReferrals(): Promise<Referral[]> {
    const rows = await firstValueFrom(this.http.get<RawReferral[]>(`${PREFIX}/referrals/onboarding`));
    return rows.map(reviveReferral);
  }

  async getLinkableReferrals(search?: string): Promise<Referral[]> {
    const qs = search?.trim() ? `?q=${encodeURIComponent(search.trim())}` : '';
    const rows = await firstValueFrom(this.http.get<RawReferral[]>(`${PREFIX}/referrals/linkable${qs}`));
    return rows.map(reviveReferral);
  }

  async linkEmployee(id: string, body: { employeeId: string; actor?: Actor }): Promise<Referral> {
    return reviveReferral(
      await firstValueFrom(this.http.post<RawReferral>(`${PREFIX}/referrals/${id}/link-employee`, body)),
    );
  }

  async completeOnboarding(
    id: string,
    body: {
      employeeId: string;
      candidateStartDate: string;
      rewardAmount: number;
      requiresTraining?: boolean;
      trainingEndDate?: string;
      comment?: string;
      actor?: Actor;
    },
  ): Promise<Referral> {
    return reviveReferral(
      await firstValueFrom(this.http.post<RawReferral>(`${PREFIX}/referrals/${id}/complete-onboarding`, body)),
    );
  }

  async markReferralPaid(
    id: string,
    body: { paid: boolean; paidAt?: string; reference?: string; actor?: Actor },
  ): Promise<Referral> {
    return reviveReferral(
      await firstValueFrom(this.http.post<RawReferral>(`${PREFIX}/referrals/${id}/payment`, body)),
    );
  }

  async getPaymentsInbox(): Promise<{
    readyCount: number;
    paidCount: number;
    totalApprovedCount: number;
    items: Array<{ referral: Referral; amount: number; canMarkPaid: boolean; canUndoPayment: boolean }>;
  }> {
    const raw = await firstValueFrom(
      this.http.get<{
        readyCount: number;
        paidCount: number;
        totalApprovedCount: number;
        items: Array<{
          referral: RawReferral;
          amount: number;
          canMarkPaid: boolean;
          canUndoPayment: boolean;
        }>;
      }>(`${PREFIX}/payments/inbox`),
    );
    return {
      ...raw,
      items: raw.items.map((i) => ({ ...i, referral: reviveReferral(i.referral) })),
    };
  }

  async payAllReferrals(body: { reference?: string; actor?: Actor }): Promise<{ paid: number; total: number }> {
    return firstValueFrom(
      this.http.post<{ paid: number; total: number }>(`${PREFIX}/payments/pay-all`, {
        paid: true,
        reference: body.reference,
        actor: body.actor,
      }),
    );
  }

  async getHistory(): Promise<ReferralHistoryEntry[]> {
    const rows = await firstValueFrom(this.http.get<RawHistory[]>(`${PREFIX}/referrals/history`));
    return rows.map((h) => ({ ...h, createdAt: new Date(h.createdAt) }));
  }

  async getRules(): Promise<ReferralRule[]> {
    const rows = await firstValueFrom(this.http.get<RawRule[]>(`${PREFIX}/rules`));
    return rows.map((r) => ({ ...r, minDurationMonths: r.minDurationMonths ?? 6, createdAt: new Date(r.createdAt) }));
  }

  async getRulesCatalog(): Promise<ReferralRuleCatalogItem[]> {
    return firstValueFrom(this.http.get<ReferralRuleCatalogItem[]>(`${PREFIX}/rules/catalog`));
  }

  async getRewardPreview(id: string): Promise<ReferralRewardPreview> {
    return firstValueFrom(this.http.get<ReferralRewardPreview>(`${PREFIX}/referrals/${id}/reward-preview`));
  }

  async upsertRule(id: string, body: Partial<ReferralRule>): Promise<ReferralRule> {
    const r = await firstValueFrom(this.http.put<RawRule>(`${PREFIX}/rules/${id}`, body));
    return { ...r, minDurationMonths: r.minDurationMonths ?? 6, createdAt: new Date(r.createdAt) };
  }

  async deleteRule(id: string): Promise<void> {
    await firstValueFrom(this.http.delete<void>(`${PREFIX}/rules/${id}`));
  }

  async getNotifications(role?: RoleFilter, userId?: string, projectId?: string): Promise<ReferralNotification[]> {
    const qs = new URLSearchParams();
    if (role) qs.set('role', role);
    if (userId) qs.set('userId', userId);
    if (projectId) qs.set('projectId', projectId);
    const suffix = qs.toString() ? `?${qs.toString()}` : '';
    const rows = await firstValueFrom(this.http.get<RawNotification[]>(`${PREFIX}/notifications${suffix}`));
    return rows.map((n) => ({ ...n, createdAt: new Date(n.createdAt) }));
  }

  async getNotificationPreferences(): Promise<NotificationPreferences> {
    return firstValueFrom(this.http.get<NotificationPreferences>(`${PREFIX}/notifications/preferences`));
  }

  async updateNotificationPreferences(prefs: NotificationPreferences): Promise<NotificationPreferences> {
    return firstValueFrom(this.http.patch<NotificationPreferences>(`${PREFIX}/notifications/preferences`, prefs));
  }

  async markNotificationRead(id: string): Promise<void> {
    await firstValueFrom(this.http.post<void>(`${PREFIX}/notifications/read`, { id }));
  }

  async markAllNotificationsRead(): Promise<void> {
    await firstValueFrom(this.http.post<void>(`${PREFIX}/notifications/read-all`, {}));
  }

  async getConfig(): Promise<SystemConfig> {
    return firstValueFrom(this.http.get<SystemConfig>(`${PREFIX}/config`));
  }

  async updateConfig(body: Partial<SystemConfig> & { actor?: Actor }): Promise<SystemConfig> {
    return firstValueFrom(this.http.patch<SystemConfig>(`${PREFIX}/config`, body));
  }

  async getAudit(): Promise<AuditLogEntry[]> {
    const rows = await firstValueFrom(this.http.get<RawAudit[]>(`${PREFIX}/audit`));
    return rows.map((e) => ({ ...e, timestamp: new Date(e.timestamp) }));
  }

  async addAudit(entry: { action: string; userId?: string; userLabel?: string; details?: string }): Promise<void> {
    try {
      await firstValueFrom(this.http.post<void>(`${PREFIX}/audit`, entry));
    } catch {
      /* écriture réservée Admin — le métier continue d’auditer côté serveur */
    }
  }

  async getAnomalies(): Promise<AnomaliesResult> {
    const raw = await firstValueFrom(
      this.http.get<{
        duplicateCandidates: { email: string; referrals: RawReferral[] }[];
        suspiciousEmails: { email: string; count: number; referralIds: string[] }[];
      }>(`${PREFIX}/anomalies`),
    );
    return {
      duplicateCandidates: raw.duplicateCandidates.map((d) => ({
        email: d.email,
        referrals: d.referrals.map(reviveReferral),
      })),
      suspiciousEmails: raw.suspiciousEmails,
    };
  }

  async exportSnapshot(): Promise<ExportSnapshot> {
    const raw = await firstValueFrom(
      this.http.get<Omit<ExportSnapshot, 'referrals' | 'history' | 'rules' | 'notifications' | 'auditLog'> & {
        referrals: RawReferral[];
        history: RawHistory[];
        rules: RawRule[];
        notifications: RawNotification[];
        auditLog: RawAudit[];
      }>(`${PREFIX}/admin/export`),
    );
    return {
      ...raw,
      referrals: raw.referrals.map(reviveReferral),
      history: raw.history.map((h) => ({ ...h, createdAt: new Date(h.createdAt) })),
      rules: raw.rules.map((r) => ({ ...r, minDurationMonths: r.minDurationMonths ?? 6, createdAt: new Date(r.createdAt) })),
      notifications: raw.notifications.map((n) => ({ ...n, createdAt: new Date(n.createdAt) })),
      auditLog: raw.auditLog.map((e) => ({ ...e, timestamp: new Date(e.timestamp) })),
    };
  }
}

function reviveReferral(r: RawReferral): Referral {
  return {
    ...r,
    positionMode: r.positionMode ?? 'CUSTOM',
    paymentStatus: r.paymentStatus ?? 'NOT_ELIGIBLE',
    createdAt: new Date(r.createdAt),
    approvedAt: r.approvedAt ? new Date(r.approvedAt) : undefined,
    eligibleForPaymentAt: r.eligibleForPaymentAt ? new Date(r.eligibleForPaymentAt) : undefined,
    paidAt: r.paidAt ? new Date(r.paidAt) : undefined,
    trainingEndNotifiedAt: r.trainingEndNotifiedAt ? new Date(r.trainingEndNotifiedAt) : undefined,
  };
}

export function parseParrainageApiError(error: unknown, fallback: string): string {
  if (error instanceof HttpErrorResponse) {
    const body = error.error as { error?: string } | string | null;
    if (body && typeof body === 'object' && typeof body.error === 'string') return body.error;
    if (typeof body === 'string' && body.trim()) return body;
    if (error.message) return error.message;
  }
  if (error instanceof Error) return error.message || fallback;
  return fallback;
}
