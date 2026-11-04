import type { EtatException, UserAccessScreenDto, VerbeAcces } from '../../../core/platform-access/user-access-api.service';

export const LIBELLES_VERBES: Record<VerbeAcces, string> = {
  View: 'Consulter',
  Act: 'Modifier',
  Validate: 'Valider',
  Configure: 'Configurer',
};

export const ORDRE_VERBES: readonly VerbeAcces[] = ['View', 'Act', 'Validate', 'Configure'];

export const action = (ecran: string, verbe: VerbeAcces): string => `${ecran}.${verbe}`;

export function etatInitial(exception: boolean | null): EtatException {
  return exception === null ? 'herite' : exception ? 'accorde' : 'retire';
}

export function effectif(role: boolean, etat: EtatException): boolean {
  return etat === 'herite' ? role : etat === 'accorde';
}

/** Valeur demandée pour un droit : revenir à celle du rôle supprime l'exception. */
export function etatPour(role: boolean, voulu: boolean): EtatException {
  return voulu === role ? 'herite' : voulu ? 'accorde' : 'retire';
}

/**
 * Bascule un droit d'une rubrique et applique les dépendances : sans « Consulter », aucun autre
 * droit ; « Modifier », « Valider » ou « Configurer » impliquent « Consulter ».
 * `etats` : état courant de chaque action de la rubrique. Retourne les nouveaux états.
 */
export function basculer(
  ecran: UserAccessScreenDto,
  etats: ReadonlyMap<string, EtatException>,
  verbe: VerbeAcces,
): Map<string, EtatException> {
  const out = new Map(etats);
  const roleDe = new Map(ecran.verbs.map((v) => [v.verb, v.role]));
  const valeur = (v: VerbeAcces) => effectif(roleDe.get(v) ?? false, out.get(action(ecran.id, v)) ?? 'herite');
  const poser = (v: VerbeAcces, voulu: boolean) => {
    if (!roleDe.has(v)) return;
    out.set(action(ecran.id, v), etatPour(roleDe.get(v)!, voulu));
  };

  const voulu = !valeur(verbe);
  poser(verbe, voulu);
  if (verbe === 'View' && !voulu) {
    for (const v of ecran.verbs) if (v.verb !== 'View') poser(v.verb, false);
  }
  if (verbe !== 'View' && voulu) poser('View', true);
  return out;
}

/** Changements à envoyer : actions dont l'état diffère de l'état enregistré. */
export function changements(
  initial: ReadonlyMap<string, EtatException>,
  courant: ReadonlyMap<string, EtatException>,
): { action: string; etat: EtatException }[] {
  const out: { action: string; etat: EtatException }[] = [];
  for (const [a, etat] of courant) {
    if ((initial.get(a) ?? 'herite') !== etat) out.push({ action: a, etat });
  }
  return out.sort((x, y) => x.action.localeCompare(y.action));
}

/** États enregistrés de toutes les actions (hérité par défaut). */
export function etatsDepuis(ecrans: readonly UserAccessScreenDto[]): Map<string, EtatException> {
  const m = new Map<string, EtatException>();
  for (const e of ecrans) for (const v of e.verbs) m.set(action(e.id, v.verb), etatInitial(v.exception));
  return m;
}

export interface ModuleAcces {
  module: string;
  ecrans: UserAccessScreenDto[];
  ouverts: number;
}

/** Rubriques groupées par module, filtrées (recherche, accessibles, exceptions). */
export function grouperModules(
  ecrans: readonly UserAccessScreenDto[],
  etats: ReadonlyMap<string, EtatException>,
  recherche: string,
  filtre: 'toutes' | 'accessibles' | 'exceptions',
): ModuleAcces[] {
  const q = recherche.trim().normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase();
  const ouvert = (e: UserAccessScreenDto) =>
    e.verbs.some((v) => v.verb === 'View' && effectif(v.role, etats.get(action(e.id, v.verb)) ?? 'herite'));
  const aException = (e: UserAccessScreenDto) => e.verbs.some((v) => (etats.get(action(e.id, v.verb)) ?? 'herite') !== 'herite');
  const groupes = new Map<string, UserAccessScreenDto[]>();
  for (const e of ecrans) {
    const texte = `${e.label} ${e.module} ${e.id}`.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase();
    if (q && !texte.includes(q)) continue;
    if (filtre === 'accessibles' && !ouvert(e)) continue;
    if (filtre === 'exceptions' && !aException(e)) continue;
    groupes.set(e.module, [...(groupes.get(e.module) ?? []), e]);
  }
  return [...groupes.entries()].map(([module, liste]) => ({ module, ecrans: liste, ouverts: liste.filter(ouvert).length }));
}

/** Nombre de rubriques ouvertes par verbe (synthèse en tête de page). */
export function synthese(
  ecrans: readonly UserAccessScreenDto[],
  etats: ReadonlyMap<string, EtatException>,
): Record<VerbeAcces, number> {
  const out: Record<VerbeAcces, number> = { View: 0, Act: 0, Validate: 0, Configure: 0 };
  for (const e of ecrans) {
    for (const v of e.verbs) if (effectif(v.role, etats.get(action(e.id, v.verb)) ?? 'herite')) out[v.verb]++;
  }
  return out;
}
