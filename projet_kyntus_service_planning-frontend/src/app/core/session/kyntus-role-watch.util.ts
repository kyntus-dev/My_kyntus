/** Compare deux noms de rôle sans tenir compte de la casse, des accents ni des espaces. */
export function memeRole(a: string | null | undefined, b: string | null | undefined): boolean {
  const n = (v: string | null | undefined) =>
    (v ?? '').normalize('NFD').replace(/[̀-ͯ]/g, '').replace(/[\s_-]/g, '').toLowerCase();
  return n(a) === n(b);
}

/**
 * Faut-il rafraîchir la session ? Oui si le rôle en base diffère de celui du jeton, et qu'on
 * n'a pas déjà tenté de passer à ce même rôle (évite une boucle si le rafraîchissement échoue).
 */
export function doitRafraichir(roleJeton: string, roleServeur: string | null | undefined, dejaTente: string | null): boolean {
  if (!roleServeur?.trim()) return false;
  if (memeRole(roleJeton, roleServeur)) return false;
  return !memeRole(dejaTente, roleServeur);
}
