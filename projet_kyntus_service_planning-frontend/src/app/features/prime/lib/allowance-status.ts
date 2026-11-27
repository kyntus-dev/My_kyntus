import type { Role } from '../models';
import { kyntusThemeCourant } from '../../../core/theme/kyntus-theme.service';

export const ALLOWANCE_STATUSES = {
  Draft: 'Draft',
  Submitted: 'Submitted',
  ManagerApproved: 'ManagerApproved',
  RhApproved: 'RhApproved',
  ComptaApproved: 'ComptaApproved',
  Paid: 'Paid',
  Rejected: 'Rejected',
} as const;

export type AllowanceStatus = (typeof ALLOWANCE_STATUSES)[keyof typeof ALLOWANCE_STATUSES];

export type AllowanceStatusViewer = 'manager' | 'stakeholder';

const STATUS_META: Record<string, { label: string; managerLabel?: string; badgeClass: string }> = {
  Draft: { label: 'Brouillon', badgeClass: 'bg-[var(--surface-3)] text-[var(--text-muted)]' },
  Submitted: { label: 'Soumis', badgeClass: 'bg-[var(--warning-bg)] text-[var(--warning-text)]' },
  ManagerApproved: {
    label: 'En attente validation RH',
    managerLabel: 'Soumis au RH',
    badgeClass: 'bg-[var(--info-bg)] text-[var(--info-text)]',
  },
  RhApproved: { label: 'Validé RH', badgeClass: 'bg-[var(--info-bg)] text-[var(--info-text)]' },
  ComptaApproved: {
    label: 'Validé compta',
    badgeClass: 'bg-[color-mix(in_srgb,var(--electric-blue)_14%,var(--bg-card))] text-[var(--electric-blue)]',
  },
  Paid: { label: 'Payé', badgeClass: 'bg-[var(--success-bg)] text-[var(--success-text)]' },
  Rejected: { label: 'Rejeté', badgeClass: 'bg-[var(--danger-bg)] text-[var(--danger-text)]' },
};

export function allowanceStatusLabel(status: string, viewer: AllowanceStatusViewer = 'stakeholder'): string {
  const meta = STATUS_META[status];
  if (!meta) return status;
  if (viewer === 'manager' && meta.managerLabel) return meta.managerLabel;
  return meta.label;
}

export function allowanceStatusBadgeClass(status: string): string {
  return STATUS_META[status]?.badgeClass ?? 'bg-[var(--surface-3)] text-[var(--text-muted)]';
}

/** Resolve a theme CSS custom property from document.body (ECharts canvas styling). */
export function primeCssToken(name: string, fallback = ''): string {
  if (typeof document === 'undefined') return fallback;
  const value = getComputedStyle(document.body).getPropertyValue(name).trim();
  return value || fallback;
}

export function primeChartRgba(rgbVar: string, alpha: number): string {
  const rgb = primeCssToken(rgbVar);
  if (!rgb) return '';
  // Theme tokens use space-separated RGB (`30 58 138`); CanvasGradient needs commas.
  const channels = rgb.includes(',') ? rgb : rgb.trim().replace(/\s+/g, ', ');
  return `rgba(${channels}, ${alpha})`;
}

export function primeChartTheme() {
  // v80 — Lecture du thème courant : les options de graphique (computed) se recalculent à la bascule.
  kyntusThemeCourant();
  const t = primeCssToken;
  const accent = t('--electric-blue');
  const info = t('--soft-blue') || t('--info');
  const success = t('--success');
  return {
    tooltipBg: t('--bg-card'),
    tooltipBorder: t('--border-color'),
    tooltipText: t('--text-primary'),
    axisLabel: t('--text-muted'),
    splitLine: t('--border-color'),
    axisLine: t('--border-color'),
    accent,
    info,
    success,
    radiusMd: 8,
    barRadiusEnd: [0, 4, 4, 0] as [number, number, number, number],
    barRadiusTop: [4, 4, 0, 0] as [number, number, number, number],
    areaGradient: (rgbVar: string, topAlpha = 0.3) => ({
      type: 'linear' as const,
      x: 0,
      y: 0,
      x2: 0,
      y2: 1,
      colorStops: [
        { offset: 0.05, color: primeChartRgba(rgbVar, topAlpha) },
        { offset: 0.95, color: primeChartRgba(rgbVar, 0) },
      ],
    }),
  };
}

export function allowanceSourceLabel(source: string): string {
  const s = source.trim();
  if (s === 'Auto') return 'Automatique';
  if (s === 'Manual') return 'Manuelle';
  return s || '—';
}

export function inboxStepLabel(role: Role | string): string {
  const r = String(role).trim();
  if (r === 'RH') return 'Validation RH — étape 1';
  if (r === 'Comptabilité' || r === 'Comptable') return 'Validation comptabilité — étape 2';
  return 'File de validation — Primes Support';
}

const ROLE_EXPECTED_STATUS: Record<string, string> = {
  RH: ALLOWANCE_STATUSES.ManagerApproved,
  Comptabilité: ALLOWANCE_STATUSES.RhApproved,
  Comptable: ALLOWANCE_STATUSES.RhApproved,
};

export function canValidateAtStep(role: Role | string, status: string): boolean {
  const expected = ROLE_EXPECTED_STATUS[String(role).trim()];
  return !!expected && status === expected;
}

export function currentAllowancePeriod(): string {
  return new Date().toISOString().slice(0, 7);
}

export function countByStatus(rows: { status: string }[]): Record<string, number> {
  const counts: Record<string, number> = {};
  for (const r of rows) {
    counts[r.status] = (counts[r.status] ?? 0) + 1;
  }
  return counts;
}

export function isPendingRhValidation(status: string): boolean {
  return status === ALLOWANCE_STATUSES.ManagerApproved;
}

export function isPendingForManager(status: string): boolean {
  return !['Paid', 'Rejected'].includes(status);
}

export function validateAllowanceAmount(
  amount: number,
  type: { minAmount?: number; maxAmount?: number; requiresJustification?: boolean },
  reason: string,
): string | null {
  if (type.minAmount != null && amount < type.minAmount) {
    return `Montant inférieur au minimum (${type.minAmount}).`;
  }
  if (type.maxAmount != null && amount > type.maxAmount) {
    return `Montant supérieur au maximum (${type.maxAmount}).`;
  }
  if (type.requiresJustification && !reason.trim()) {
    return 'Motif obligatoire pour ce type de prime.';
  }
  return null;
}
