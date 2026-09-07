/** Libellés métier PRIME. Les valeurs API/DB restent inchangées. */
const VALIDATION_STATUS_LABELS: Readonly<Record<string, string>> = {
  AwaitingData: 'Saisie à compléter',
  NotStarted: 'Non démarrée',
  InProgress: 'En cours de saisie',
  Complete: 'Saisie terminée',
  Pending: 'En attente du référent technique',
  'Référent technique Approved': 'Approuvée par le référent technique',
  'Coach Approved': 'Approuvée par le référent technique',
  'Superviseur Approved': 'Approuvée par le superviseur',
  'Manager Approved': 'Approuvée par le superviseur',
  'Chef de projet Approved': 'Approuvée par le chef de projet',
  'RP Approved': 'Approuvée par le chef de projet',
  'RH Approved': 'Approuvée par la RH',
  Rejected: 'Rejetée',
  'Historical Import': 'Archivée (import historique)',
};

const VALIDATION_STATUS_SHORT_LABELS: Readonly<Record<string, string>> = {
  AwaitingData: 'À compléter',
  NotStarted: 'Non démarrée',
  InProgress: 'En cours',
  Complete: 'Terminée',
  Pending: 'En attente RT',
  'Référent technique Approved': 'RT OK',
  'Coach Approved': 'RT OK',
  'Superviseur Approved': 'Superviseur OK',
  'Manager Approved': 'Superviseur OK',
  'Chef de projet Approved': 'Chef de projet OK',
  'RP Approved': 'Chef de projet OK',
  'RH Approved': 'RH OK',
  Rejected: 'Rejetée',
  'Historical Import': 'Archivée',
};

const ROLE_LABELS: Readonly<Record<string, string>> = {
  Coach: 'Référent technique',
  Employee: 'Pilote',
  RP: 'Chef de projet',
  Comptable: 'Comptabilité',
};

export function primeRoleLabel(role: string | null | undefined): string {
  const value = (role ?? '').trim();
  return ROLE_LABELS[value] ?? (value || '—');
}

export function primeValidationStatusLabel(status: string | null | undefined): string {
  const value = (status ?? '').trim();
  return VALIDATION_STATUS_LABELS[value] ?? (value || '—');
}

export function primeValidationStatusShortLabel(status: string | null | undefined): string {
  const value = (status ?? '').trim();
  return VALIDATION_STATUS_SHORT_LABELS[value] ?? primeValidationStatusLabel(value);
}

export function primeFillingStatusLabel(status: string | null | undefined): string {
  const value = (status ?? '').trim();
  if (value === 'NotStarted' || !value) return 'Non démarrée';
  if (value === 'InProgress') return 'En cours de saisie';
  if (value === 'Complete') return 'Saisie terminée';
  return primeValidationStatusLabel(value);
}

export function primeValidationActionLabel(action: string | null | undefined): string {
  const value = (action ?? '').trim();
  if (value === 'Submitted') return 'Soumission au circuit';
  if (value === 'Approved') return 'Approbation';
  if (value === 'Rejected') return 'Rejet';
  if (value === 'LineRejected') return 'Rejet de la ligne de synthèse';
  if (value === 'Paid') return 'Marquée comme payée';
  if (value === 'Unpaid') return 'Paiement annulé';
  return value || 'Action';
}

export function primeWorkflowTransitionLabel(
  fromStatus: string | null | undefined,
  toStatus: string | null | undefined,
): string {
  return `${primeValidationStatusLabel(fromStatus)} → ${primeValidationStatusLabel(toStatus)}`;
}
