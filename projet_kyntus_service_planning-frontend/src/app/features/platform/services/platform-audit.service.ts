import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { forkJoin, of } from 'rxjs';
import { catchError, map } from 'rxjs/operators';
import type { AuditLogDto as DocAuditLogDto } from '../../../core/models/documentation.models';
import type { PagedResponse } from '../../../core/models/documentation.models';
import type { AnomalyDto, AuditLogDto as PrimeAuditLogDto } from '../../prime/services/prime-admin.service';

export type AuditModuleId = 'prime' | 'documentation' | 'parrainage' | 'qualite' | 'auth';

export interface UnifiedAuditRow {
  id: string;
  at: string;
  module: AuditModuleId;
  actor: string;
  role: string;
  action: string;
  entity: string;
  success: boolean | null;
  ip: string;
  correlationId: string;
  detail: string;
}

export interface UnifiedAnomalyRow {
  id: string;
  module: AuditModuleId;
  detectedAt: string;
  severity: string;
  status: string;
  type: string;
  description: string;
  actionable: boolean;
}

export interface JournalModuleTotals {
  prime: number;
  documentation: number;
  parrainage: number;
  qualite: number;
}

export interface JournalLoadResult {
  rows: UnifiedAuditRow[];
  sources: Record<string, 'ok' | 'error'>;
  totals: JournalModuleTotals;
}

export interface AnomalyLoadResult {
  rows: UnifiedAnomalyRow[];
  sources: Record<string, 'ok' | 'error'>;
}

export interface AccessAuditEntry {
  id: string;
  at: string;
  eventType: string;
  email: string;
  userId?: number | null;
  ipAddress?: string | null;
  userAgent?: string | null;
  message?: string | null;
}

export interface ServiceHealthRow {
  id: string;
  label: string;
  ok: boolean;
  detail: string;
}

interface ParrainageAuditDto {
  id: string;
  action: string;
  userId: string;
  userLabel: string;
  timestamp: string;
  details?: string | null;
}

interface QualiteAuditPage {
  items?: Array<{
    _id?: string;
    action?: string;
    actorRole?: string;
    actor?: { name?: string; email?: string; role?: string };
    targetType?: string;
    targetId?: string;
    createdAt?: string;
    ip?: string;
    metadata?: unknown;
  }>;
  total?: number;
}

interface ParrainageAnomaliesDto {
  duplicateCandidates?: Array<{ email: string; referrals?: unknown[] }>;
  suspiciousEmails?: Array<{ email: string; count: number; referralIds?: string[] }>;
}

@Injectable({ providedIn: 'root' })
export class PlatformAuditService {
  private readonly http = inject(HttpClient);

  loadJournal(take = 200) {
    return forkJoin({
      prime: this.http
        .get<PrimeAuditLogDto[]>(`/api/prime/admin/audit-logs`, {
          params: new HttpParams().set('take', String(take)),
        })
        .pipe(
          map((rows) => ({ ok: true as const, rows })),
          catchError(() => of({ ok: false as const, rows: [] as PrimeAuditLogDto[] })),
        ),
      documentation: this.http
        .get<PagedResponse<DocAuditLogDto>>(`/api/documentation/data/audit-logs`, {
          params: new HttpParams().set('page', '1').set('pageSize', String(Math.min(take, 100))),
        })
        .pipe(
          map((page) => ({ ok: true as const, page })),
          catchError(() => of({ ok: false as const, page: { items: [], totalCount: 0, page: 1, pageSize: take } })),
        ),
      parrainage: this.http
        .get<ParrainageAuditDto[]>(`/api/parrainage/audit`, {
          params: new HttpParams().set('take', String(take)),
        })
        .pipe(
          map((rows) => ({ ok: true as const, rows })),
          catchError(() => of({ ok: false as const, rows: [] as ParrainageAuditDto[] })),
        ),
      qualite: this.http
        .get<QualiteAuditPage>(`/api/qualite/admin/audit`, {
          params: new HttpParams().set('page', '1').set('limit', String(Math.min(take, 100))),
        })
        .pipe(
          map((page) => ({ ok: true as const, page })),
          catchError(() => of({ ok: false as const, page: { items: [], total: 0 } as QualiteAuditPage })),
        ),
    }).pipe(
      map(({ prime, documentation, parrainage, qualite }) => {
        const rows: UnifiedAuditRow[] = [];
        for (const r of prime.rows) {
          rows.push({
            id: `prime:${r.id}`,
            at: r.at,
            module: 'prime',
            actor: r.userDisplayName || r.userId || '—',
            role: r.role || '—',
            action: r.action,
            entity: [r.entityType, r.entityId].filter(Boolean).join(' '),
            success: null,
            ip: r.ipAddress || '—',
            correlationId: '—',
            detail: r.detailJson || '',
          });
        }
        for (const r of documentation.page.items ?? []) {
          rows.push({
            id: `documentation:${r.id}`,
            at: r.occurredAt,
            module: 'documentation',
            actor: r.actorName || r.actorUserId || '—',
            role: '—',
            action: r.action,
            entity: [r.entityType, r.entityId].filter(Boolean).join(' '),
            success: r.success,
            ip: '—',
            correlationId: r.correlationId || '—',
            detail: r.errorMessage || '',
          });
        }
        for (const r of parrainage.rows) {
          rows.push({
            id: `parrainage:${r.id}`,
            at: r.timestamp,
            module: 'parrainage',
            actor: r.userLabel || r.userId || '—',
            role: '—',
            action: r.action,
            entity: '',
            success: null,
            ip: '—',
            correlationId: '—',
            detail: r.details || '',
          });
        }
        for (const r of qualite.page.items ?? []) {
          rows.push({
            id: `qualite:${r._id ?? r.createdAt ?? r.action}`,
            at: r.createdAt || '',
            module: 'qualite',
            actor: r.actor?.name || r.actor?.email || '—',
            role: r.actorRole || r.actor?.role || '—',
            action: r.action || '—',
            entity: [r.targetType, r.targetId].filter(Boolean).join(' '),
            success: null,
            ip: r.ip || '—',
            correlationId: '—',
            detail: r.metadata ? JSON.stringify(r.metadata) : '',
          });
        }
        rows.sort((a, b) => +new Date(b.at) - +new Date(a.at));
        const result: JournalLoadResult = {
          rows,
          sources: {
            prime: prime.ok ? 'ok' : 'error',
            documentation: documentation.ok ? 'ok' : 'error',
            parrainage: parrainage.ok ? 'ok' : 'error',
            qualite: qualite.ok ? 'ok' : 'error',
          },
          totals: {
            prime: prime.rows.length,
            documentation: documentation.page.totalCount ?? documentation.page.items?.length ?? 0,
            parrainage: parrainage.rows.length,
            qualite: qualite.page.total ?? qualite.page.items?.length ?? 0,
          },
        };
        return result;
      }),
    );
  }

  loadAnomalies() {
    return forkJoin({
      prime: this.http.get<AnomalyDto[]>('/api/prime/admin/anomalies').pipe(
        map((rows) => ({ ok: true as const, rows })),
        catchError(() => of({ ok: false as const, rows: [] as AnomalyDto[] })),
      ),
      parrainage: this.http.get<ParrainageAnomaliesDto>('/api/parrainage/anomalies').pipe(
        map((dto) => ({ ok: true as const, dto })),
        catchError(() => of({ ok: false as const, dto: {} as ParrainageAnomaliesDto })),
      ),
    }).pipe(
      map(({ prime, parrainage }) => {
        const rows: UnifiedAnomalyRow[] = prime.rows.map((a) => ({
          id: a.id,
          module: 'prime' as const,
          detectedAt: a.detectedAt,
          severity: a.severity,
          status: a.status,
          type: a.type,
          description: a.description,
          actionable: true,
        }));
        for (const d of parrainage.dto.duplicateCandidates ?? []) {
          rows.push({
            id: `parrainage-dup:${d.email}`,
            module: 'parrainage',
            detectedAt: '',
            severity: 'Medium',
            status: 'Open',
            type: 'DuplicateEmail',
            description: `Doublon email ${d.email} (${d.referrals?.length ?? 0} dossiers)`,
            actionable: false,
          });
        }
        for (const d of parrainage.dto.suspiciousEmails ?? []) {
          rows.push({
            id: `parrainage-sus:${d.email}`,
            module: 'parrainage',
            detectedAt: '',
            severity: 'Low',
            status: 'Open',
            type: 'SuspiciousEmail',
            description: `Email suspect ${d.email} (${d.count} occurrences)`,
            actionable: false,
          });
        }
        const result: AnomalyLoadResult = {
          rows,
          sources: {
            prime: prime.ok ? 'ok' : 'error',
            parrainage: parrainage.ok ? 'ok' : 'error',
          },
        };
        return result;
      }),
    );
  }

  resolvePrimeAnomaly(id: string, status: string, note?: string) {
    return this.http.put<AnomalyDto>(`/api/prime/admin/anomalies/${id}`, {
      status,
      resolutionNote: note ?? null,
    });
  }

  recomputePrimeAnomalies() {
    return this.http.post<{ upsertedCount: number }>('/api/prime/admin/anomalies/recompute-all', {});
  }

  loadAccessHistory(take = 200) {
    return this.http.get<AccessAuditEntry[]>('/api/Auth/audit/access', {
      params: new HttpParams().set('take', String(take)),
    }).pipe(catchError(() => of([] as AccessAuditEntry[])));
  }

  loadServiceHealth() {
    const probe = (id: string, label: string, url: string) =>
      this.http.get(url, { observe: 'response' }).pipe(
        map((resp): ServiceHealthRow => ({
          id,
          label,
          ok: resp.status >= 200 && resp.status < 400,
          detail: `${resp.status}`,
        })),
        catchError(() => of({ id, label, ok: false, detail: 'indisponible' } as ServiceHealthRow)),
      );

    return forkJoin([
      probe('directory', 'Employee Directory', '/api/directory/health'),
      probe('auth', 'Authentification', '/api/Auth/health'),
      probe('planning', 'Planning', '/api/planning/health'),
      probe('prime', 'Prime', '/api/prime/health'),
      probe('documentation', 'Documentation', '/api/documentation/health'),
      probe('parrainage', 'Parrainage', '/api/parrainage/health'),
      probe('qualite', 'Contrôle qualité', '/api/qualite/health'),
    ]);
  }
}
