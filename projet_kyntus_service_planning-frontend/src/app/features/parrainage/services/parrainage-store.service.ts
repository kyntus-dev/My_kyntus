import { Injectable, inject, signal } from '@angular/core';
import type {
  NotificationPreferences,
  Referral,
  ReferralHistoryEntry,
  ReferralNotification,
  ReferralRule,
  RoleFilter,
} from '../models/referral.model';
import type { AuditLogEntry, SystemConfig } from '../models/system-config.model';
import { DEFAULT_SYSTEM_CONFIG } from '../models/system-config.model';
import { ParrainageApiService } from './parrainage-api.service';
import { mapApiOrgNodes, setOrgNodes } from '../lib/org-hierarchy';
import { normalizeReferralProgramRules, syncLegacyBonusFields } from '../lib/referral-program';

@Injectable({ providedIn: 'root' })
export class ParrainageStoreService {
  private readonly api = inject(ParrainageApiService);

  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly referrals = signal<Referral[]>([]);
  readonly rules = signal<ReferralRule[]>([]);
  readonly history = signal<ReferralHistoryEntry[]>([]);
  readonly notifications = signal<ReferralNotification[]>([]);
  readonly notificationPrefs = signal<NotificationPreferences>({
    email: true,
    inApp: true,
    systemAlerts: true,
    referrals: true,
    approvals: true,
    payments: true,
  });
  readonly systemConfig = signal<SystemConfig>({ ...DEFAULT_SYSTEM_CONFIG });
  readonly auditLog = signal<AuditLogEntry[]>([]);

  private canLoadAudit(role?: RoleFilter): boolean {
    return role === 'ADMIN' || role === 'AUDIT';
  }

  async bootstrap(role: RoleFilter, userId: string, projectId?: string): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      const [referrals, rules, history, prefs, config, audit, orgRows] = await Promise.all([
        this.api.getReferrals(),
        this.api.getRules(),
        this.api.getHistory(),
        this.api.getNotificationPreferences(),
        this.api.getConfig(),
        this.canLoadAudit(role) ? this.api.getAudit() : Promise.resolve([] as AuditLogEntry[]),
        this.api.getOrgNodes(),
      ]);
      setOrgNodes(mapApiOrgNodes(orgRows));
      const notifications = await this.api.getNotifications(role, userId, projectId);
      this.referrals.set(referrals);
      this.rules.set(rules);
      this.history.set(history);
      this.notifications.set(notifications);
      this.notificationPrefs.set(prefs);
      this.systemConfig.set(this.normalizeConfig(config));
      this.auditLog.set(audit);
    } catch (e) {
      this.error.set(e instanceof Error ? e.message : 'Erreur de chargement');
    } finally {
      this.loading.set(false);
    }
  }

  async refreshCore(role?: RoleFilter, userId?: string, projectId?: string): Promise<void> {
    const [referrals, history, notifications, audit] = await Promise.all([
      this.api.getReferrals(),
      this.api.getHistory(),
      this.api.getNotifications(role, userId, projectId),
      this.canLoadAudit(role) ? this.api.getAudit() : Promise.resolve([] as AuditLogEntry[]),
    ]);
    this.referrals.set(referrals);
    this.history.set(history);
    this.notifications.set(notifications);
    this.auditLog.set(audit);
  }

  async refreshRules(): Promise<void> {
    this.rules.set(await this.api.getRules());
  }

  async refreshConfig(): Promise<void> {
    this.systemConfig.set(this.normalizeConfig(await this.api.getConfig()));
  }

  async refreshNotifications(role: RoleFilter, userId: string, projectId?: string): Promise<void> {
    this.notifications.set(await this.api.getNotifications(role, userId, projectId));
  }

  patchReferral(updated: Referral): void {
    this.referrals.update((list) => {
      const idx = list.findIndex((r) => r.id === updated.id);
      if (idx === -1) return [updated, ...list];
      const next = [...list];
      next[idx] = updated;
      return next;
    });
  }

  private normalizeConfig(cfg: SystemConfig): SystemConfig {
    const rules = normalizeReferralProgramRules(cfg);
    const legacy = syncLegacyBonusFields(rules);
    return { ...cfg, referralProgramRules: rules, ...legacy };
  }
}
