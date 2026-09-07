/**
 * Matching flou noms HTEL — aligné sur referral-candidate-match.util.ts
 * et HtelNameNormalizer (backend).
 */

export const HTEL_MATCH_ALERT_THRESHOLD = 0.7;
export const HTEL_MATCH_AUTO_LINK_THRESHOLD = 0.85;
export const HTEL_MATCH_AMBIGUITY_GAP = 0.05;
export const HTEL_MATCH_MIN_NAME_LENGTH = 2;
const HTEL_TOKEN_PAIR_MIN = 0.65;

export interface HtelNameMatchTechnicien {
  idTechnicien: number;
  technicien: string;
}

export function normalizeHtelName(value: string | null | undefined): string {
  if (!value?.trim()) return '';
  return value
    .normalize('NFD')
    .replace(/\p{M}/gu, '')
    .toLowerCase()
    .replace(/[^a-z0-9\s]/g, ' ')
    .replace(/\s+/g, ' ')
    .trim();
}

export function employeeHtelNameKeys(firstName: string, lastName: string): string[] {
  const first = normalizeHtelName(firstName);
  const last = normalizeHtelName(lastName);
  if (!first && !last) return [];
  if (first && last) return [`${last} ${first}`, `${first} ${last}`];
  return [last || first];
}

function levenshtein(a: string, b: string): number {
  if (a === b) return 0;
  if (!a.length) return b.length;
  if (!b.length) return a.length;
  const row = Array.from({ length: b.length + 1 }, (_, i) => i);
  for (let i = 1; i <= a.length; i++) {
    let prev = i;
    for (let j = 1; j <= b.length; j++) {
      const cost = a[i - 1] === b[j - 1] ? 0 : 1;
      const next = Math.min(row[j] + 1, prev + 1, row[j - 1] + cost);
      row[j - 1] = prev;
      prev = next;
    }
    row[b.length] = prev;
  }
  return row[b.length];
}

/** Similarité chaîne → [0,1] (même logique que scoreStrings parrainage). */
export function scoreHtelStrings(left: string, right: string): number {
  const a = normalizeHtelName(left);
  const b = normalizeHtelName(right);
  if (!a || !b) return 0;
  if (a === b) return 1;

  const short = a.length <= b.length ? a : b;
  const long = a.length <= b.length ? b : a;
  const ratio = short.length / long.length;

  if (ratio >= 0.72) {
    if (long.includes(short)) return Math.min(1, 0.9 + 0.1 * ratio);
    if (long.startsWith(short) || short.startsWith(long)) return Math.min(1, 0.85 + 0.12 * ratio);
  } else if (ratio >= 0.5 && short.length >= 4 && long.includes(short)) {
    return 0.55 + 0.35 * ratio;
  }

  const maxLen = Math.max(a.length, b.length);
  return Math.max(0, 1 - levenshtein(a, b) / maxLen);
}

function scoreHtelFullName(firstName: string, lastName: string, candidateName: string): number {
  const first = firstName.trim();
  const last = lastName.trim();
  if (!first || !last) return 0;
  const variants = [`${last} ${first}`, `${first} ${last}`];
  const candidate = candidateName.trim();
  let best = 0;
  for (const v of variants) best = Math.max(best, scoreHtelStrings(v, candidate));
  return best;
}

function scoreHtelAlignedTokens(firstName: string, lastName: string, candidateName: string): number {
  const first = normalizeHtelName(firstName);
  const last = normalizeHtelName(lastName);
  if (!first || !last) return 0;

  const tokens = normalizeHtelName(candidateName).split(/\s+/).filter(Boolean);
  if (tokens.length === 0) return 0;

  let best = 0;
  for (let i = 0; i < tokens.length; i++) {
    for (let j = 0; j < tokens.length; j++) {
      if (tokens.length > 1 && i === j) continue;
      const lastScore = scoreHtelStrings(last, tokens[i]);
      const firstScore = scoreHtelStrings(first, tokens[j]);
      if (lastScore < HTEL_TOKEN_PAIR_MIN || firstScore < HTEL_TOKEN_PAIR_MIN) continue;
      best = Math.max(best, (lastScore + firstScore) / 2);
    }
  }
  return best;
}

/** Score employé ↔ libellé technicien HTEL. */
export function scoreEmployeeToHtel(
  firstName: string,
  lastName: string,
  technicien: string,
): number {
  const first = firstName.trim();
  const last = lastName.trim();
  if (first.length < HTEL_MATCH_MIN_NAME_LENGTH || last.length < HTEL_MATCH_MIN_NAME_LENGTH) {
    return 0;
  }
  return Math.max(
    scoreHtelFullName(first, last, technicien),
    scoreHtelAlignedTokens(first, last, technicien),
  );
}

/** Liaison auto : match unique au-dessus du seuil fort. */
export function findUniqueHtelMatch<T extends HtelNameMatchTechnicien>(
  firstName: string,
  lastName: string,
  techniciens: T[],
): T | null {
  const scored = techniciens
    .map((t) => ({ t, score: scoreEmployeeToHtel(firstName, lastName, t.technicien) }))
    .filter((x) => x.score >= HTEL_MATCH_AUTO_LINK_THRESHOLD)
    .sort((a, b) => b.score - a.score);

  if (scored.length === 0) return null;
  if (scored.length === 1) return scored[0].t;
  if (scored[0].score - scored[1].score >= HTEL_MATCH_AMBIGUITY_GAP) return scored[0].t;
  return null;
}
