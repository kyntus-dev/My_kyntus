const FR_MONTHS_SHORT = [
  'Janv.',
  'Févr.',
  'Mars',
  'Avr.',
  'Mai',
  'Juin',
  'Juil.',
  'Août',
  'Sept.',
  'Oct.',
  'Nov.',
  'Déc.',
] as const;

/** « Mars 2026 » */
export function formatMonthYearFr(date: Date = new Date()): string {
  const raw = new Intl.DateTimeFormat('fr-FR', { month: 'long', year: 'numeric' }).format(date);
  return raw.charAt(0).toUpperCase() + raw.slice(1);
}

/** Clé API `03` / `2026-03` → `Mars`. */
export function monthKeyToFr(key: string | null | undefined): string {
  const raw = (key ?? '').trim();
  if (!raw) return '';
  const mm = raw.length >= 7 && raw.includes('-') ? raw.slice(5, 7) : raw;
  const n = Number.parseInt(mm, 10);
  if (n >= 1 && n <= 12) return FR_MONTHS_SHORT[n - 1];
  return raw;
}
