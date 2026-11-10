/**
 * Numéro de semaine de l'année (norme ISO 8601, celle des calendriers français) : la semaine 1
 * est celle qui contient le premier jeudi de janvier ; une semaine commence le lundi.
 * Ex. 2 novembre 2026 → S45 (2026) ; 31 décembre 2026 → S53 (2026) ; 1er janvier 2027 → S53 (2026).
 */
export function semaineIso(jour: string | Date): { annee: number; numero: number } {
  const d = typeof jour === 'string' ? parse(jour) : new Date(jour.getFullYear(), jour.getMonth(), jour.getDate());
  // Jeudi de la même semaine : il donne l'année de la semaine.
  const jeudi = new Date(d.getFullYear(), d.getMonth(), d.getDate() + 3 - ((d.getDay() + 6) % 7));
  const premierJanvier = new Date(jeudi.getFullYear(), 0, 1);
  const jours = Math.round((jeudi.getTime() - premierJanvier.getTime()) / 86_400_000);
  return { annee: jeudi.getFullYear(), numero: Math.floor(jours / 7) + 1 };
}

/** « S45 » (numéro sur deux chiffres : « S05 »). */
export function libelleSemaine(jour: string | Date): string {
  return `S${String(semaineIso(jour).numero).padStart(2, '0')}`;
}

function parse(iso: string): Date {
  const [y, m, d] = iso.slice(0, 10).split('-').map(Number);
  return new Date(y, m - 1, d);
}
