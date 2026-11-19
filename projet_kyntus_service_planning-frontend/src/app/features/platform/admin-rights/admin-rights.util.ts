import {
  type AdminRestrictionScreenDto,
  type EtatRestriction,
  VERBES_ADMIN,
  type VerbeAdmin,
} from '../../../core/platform-access/admin-restrictions.model';

/** v60 — Libellés des verbes administrateur. */
export const LIBELLES_VERBES_ADMIN: Record<VerbeAdmin, string> = {
  View: 'Consulter',
  Act: 'Modifier',
  Validate: 'Valider',
  Configure: 'Configurer',
  Delete: 'Supprimer',
  Export: 'Exporter',
};

export const action = (ecran: string, verbe: VerbeAdmin): string => `${ecran}.${verbe}`;

/** États enregistrés : réglage du compte, ou réglage « tous les administrateurs » pour la ligne globale. */
export function etatsDepuis(screens: readonly AdminRestrictionScreenDto[], global: boolean): Map<string, EtatRestriction> {
  const m = new Map<string, EtatRestriction>();
  for (const s of screens) {
    for (const v of s.verbs) {
      const valeur = global ? v.tousLesAdmins : v.compte;
      m.set(action(s.id, v.verb), valeur === true ? 'accorde' : valeur === false ? 'retire' : 'herite');
    }
  }
  return m;
}

/** Réglage « tous les administrateurs » (pour un compte) : permis sauf retrait global. */
export function valeursGlobales(screens: readonly AdminRestrictionScreenDto[]): Map<string, boolean> {
  const m = new Map<string, boolean>();
  for (const s of screens) for (const v of s.verbs) m.set(action(s.id, v.verb), v.tousLesAdmins !== false);
  return m;
}

/** Verbe permis en tenant compte du brouillon ; sans « Consulter », plus rien n'est permis sur l'écran. */
export function permis(
  ecran: string,
  verbe: VerbeAdmin,
  etats: ReadonlyMap<string, EtatRestriction>,
  globales: ReadonlyMap<string, boolean> | null,
): boolean {
  const brut = (v: VerbeAdmin): boolean => {
    const a = action(ecran, v);
    const e = etats.get(a) ?? 'herite';
    if (e === 'accorde') return true;
    if (e === 'retire') return false;
    return globales ? globales.get(a) ?? true : true;
  };
  return brut('View') && brut(verbe);
}

/**
 * Nouvel état pour obtenir <paramref>voulu</paramref> : « herite » quand le niveau au-dessus donne déjà
 * ce résultat (aucune ligne inutile), sinon « accorde » / « retire ».
 */
export function etatPour(
  a: string,
  voulu: boolean,
  globales: ReadonlyMap<string, boolean> | null,
): EtatRestriction {
  const parDefaut = globales ? globales.get(a) ?? true : true;
  if (voulu === parDefaut) return 'herite';
  return voulu ? 'accorde' : 'retire';
}

/** Bascule un verbe ; retirer « Consulter » retire tout l'écran, rendre un verbe rend aussi « Consulter ». */
export function basculer(
  ecran: string,
  verbe: VerbeAdmin,
  etats: ReadonlyMap<string, EtatRestriction>,
  globales: ReadonlyMap<string, boolean> | null,
): Map<string, EtatRestriction> {
  const m = new Map(etats);
  const voulu = !permis(ecran, verbe, etats, globales);
  m.set(action(ecran, verbe), etatPour(action(ecran, verbe), voulu, globales));
  if (voulu && verbe !== 'View' && !permis(ecran, 'View', m, globales)) {
    m.set(action(ecran, 'View'), etatPour(action(ecran, 'View'), true, globales));
  }
  return m;
}

/** Tout l'écran (ou tout un module) permis ou retiré. */
export function regler(
  ecrans: readonly string[],
  voulu: boolean,
  etats: ReadonlyMap<string, EtatRestriction>,
  globales: ReadonlyMap<string, boolean> | null,
): Map<string, EtatRestriction> {
  const m = new Map(etats);
  for (const e of ecrans) {
    for (const v of VERBES_ADMIN) m.set(action(e, v), etatPour(action(e, v), voulu, globales));
  }
  return m;
}

/** Changements à enregistrer (seulement ce qui diffère de l'état chargé). */
export function changements(
  initial: ReadonlyMap<string, EtatRestriction>,
  courant: ReadonlyMap<string, EtatRestriction>,
): { action: string; etat: EtatRestriction }[] {
  const out: { action: string; etat: EtatRestriction }[] = [];
  for (const [a, e] of courant) if ((initial.get(a) ?? 'herite') !== e) out.push({ action: a, etat: e });
  return out;
}

/** Clés « écran.Verbe:Global » permises : sert à l'aperçu du menu de l'administrateur. */
export function clesPermises(
  screens: readonly AdminRestrictionScreenDto[],
  etats: ReadonlyMap<string, EtatRestriction>,
  globales: ReadonlyMap<string, boolean> | null,
): string[] {
  const out: string[] = [];
  for (const s of screens) for (const v of VERBES_ADMIN) if (permis(s.id, v, etats, globales)) out.push(`${s.id}.${v}:Global`);
  return out;
}

/** Nombre de fonctionnalités retirées (brouillon compris). */
export function nombreRetraits(
  screens: readonly AdminRestrictionScreenDto[],
  etats: ReadonlyMap<string, EtatRestriction>,
  globales: ReadonlyMap<string, boolean> | null,
): number {
  let n = 0;
  for (const s of screens) for (const v of VERBES_ADMIN) if (!permis(s.id, v, etats, globales)) n++;
  return n;
}
