/** Affichage Nom Prénom. Jamais l’email brut. */

export function looksLikeEmail(value: string | null | undefined): boolean {
  return (value ?? '').includes('@');
}

function titleCaseWords(value: string): string {
  return value
    .split(/\s+/)
    .filter(Boolean)
    .map((part) => part.charAt(0).toUpperCase() + part.slice(1).toLowerCase())
    .join(' ');
}

function fromEmailLocalPart(email: string): string {
  const local = email.split('@')[0]?.trim() ?? '';
  if (!local) return '';
  return titleCaseWords(local.replace(/[._-]+/g, ' '));
}

export function formatPersonDisplayName(source: {
  firstName?: string | null;
  lastName?: string | null;
  username?: string | null;
  email?: string | null;
}): string {
  const first = (source.firstName ?? '').trim();
  const last = (source.lastName ?? '').trim();
  const full = `${last} ${first}`.trim();
  if (full && !looksLikeEmail(full)) return full;

  const email = (source.email ?? '').trim();
  if (looksLikeEmail(email)) {
    const derived = fromEmailLocalPart(email);
    if (derived) return derived;
  }

  const username = (source.username ?? '').trim();
  if (username && !looksLikeEmail(username)) return username;

  if (looksLikeEmail(username)) {
    const derived = fromEmailLocalPart(username);
    if (derived) return derived;
  }

  return 'Utilisateur';
}

export function personInitials(source: {
  firstName?: string | null;
  lastName?: string | null;
  username?: string | null;
  email?: string | null;
}): string {
  const first = (source.firstName ?? '').trim();
  const last = (source.lastName ?? '').trim();
  if (first && last) return `${first.charAt(0)}${last.charAt(0)}`.toUpperCase();
  const display = formatPersonDisplayName(source);
  const parts = display.split(/\s+/).filter(Boolean);
  if (parts.length >= 2) return `${parts[0].charAt(0)}${parts[1].charAt(0)}`.toUpperCase();
  return display.slice(0, 2).toUpperCase() || 'KY';
}
