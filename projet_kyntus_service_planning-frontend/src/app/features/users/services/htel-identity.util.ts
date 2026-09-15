import { normalizeHtelName } from './htel-name-match.util';

/** Suggestion modifiable : le libellé HTEL n'est pas un couple nom/prénom structuré. */
export function suggestHtelIdentity(fullName: string, firstName = '', lastName = ''): { firstName: string; lastName: string } {
  const parts = fullName.trim().split(/\s+/).filter(Boolean);
  const full = normalizeHtelName(fullName);
  let lastCount = 0;
  if (firstName.trim() && lastName.trim()) {
    if (normalizeHtelName(`${lastName} ${firstName}`) === full) {
      lastCount = lastName.trim().split(/\s+/).length;
    } else if (normalizeHtelName(`${firstName} ${lastName}`) === full) {
      lastCount = firstName.trim().split(/\s+/).length;
    }
  }
  if (!lastCount) {
    // Préserve les noms composés en capitales : « EL AARAB Fatima Zahra ».
    while (lastCount < parts.length && parts[lastCount] === parts[lastCount].toUpperCase()
      && parts[lastCount] !== parts[lastCount].toLowerCase()) lastCount++;
    // Tout en capitales ou casse libre : simple proposition, à confirmer par RH.
    if (!lastCount || lastCount === parts.length) lastCount = Math.min(1, parts.length);
  }
  return { lastName: parts.slice(0, lastCount).join(' '), firstName: parts.slice(lastCount).join(' ') };
}

export function htelIdentityMatches(fullName: string, firstName: string, lastName: string): boolean {
  return !!firstName.trim() && !!lastName.trim()
    && normalizeHtelName(`${lastName} ${firstName}`) === normalizeHtelName(fullName);
}
