import type { PlatformScope, PlatformScreen, PlatformVerb } from '../../../core/platform-access/platform-capability.catalog';
import type { IamPermissionDto, UpsertIamPermissionRequest } from '../../../core/platform-access/platform-iam-api.service';

/**
 * « Accès par rôle » (modèle B) : chaque rubrique a un niveau — Aucun, Consulter, Modifier,
 * Valider — plus un interrupteur « Configurer ». Les niveaux sont cumulatifs : « Modifier »
 * comprend « Consulter », « Valider » comprend « Modifier » quand la rubrique le propose.
 */
export type Niveau = 'Aucun' | 'View' | 'Act' | 'Validate';

export const LIBELLE_NIVEAU: Record<Niveau, string> = {
  Aucun: 'Aucun',
  View: 'Consulter',
  Act: 'Modifier',
  Validate: 'Valider',
};

export const LIBELLE_PERIMETRE: Record<PlatformScope, string> = {
  Global: 'Toute l’entreprise',
  Pole: 'Pôle',
  Cellule: 'Cellule',
  Service: 'Service',
  Self: 'Soi-même',
};

export interface LigneRubrique {
  capability: string;
  label: string;
  module: string;
  /** Niveaux proposés par la rubrique (Aucun et Consulter toujours). */
  niveaux: Niveau[];
  configurable: boolean;
  verbes: ReadonlySet<PlatformVerb>;
  perimetre: PlatformScope;
}

const memeRole = (a: string, b: string) => a.localeCompare(b, 'fr', { sensitivity: 'accent' }) === 0;

export function niveauxProposes(s: PlatformScreen): Niveau[] {
  return ['Aucun', 'View', ...(s.act?.length ? ['Act' as const] : []), ...(s.validate?.length ? ['Validate' as const] : [])];
}

/** Droits enregistrés d'un rôle, rubrique par rubrique (tous périmètres confondus). */
export function lignesDuRole(
  role: string,
  permissions: readonly IamPermissionDto[],
  ecrans: readonly PlatformScreen[],
  perimetreParDefaut: (ecran: PlatformScreen) => PlatformScope,
): LigneRubrique[] {
  const accordes = permissions.filter((p) => p.isAllowed && memeRole(p.role, role));
  return ecrans.map((s) => {
    const siens = accordes.filter((p) => p.action.startsWith(s.id + '.') && !p.action.slice(s.id.length + 1).includes('.'));
    const verbes = new Set(siens.map((p) => p.action.slice(s.id.length + 1) as PlatformVerb));
    const vue = siens.find((p) => p.action === `${s.id}.View`) ?? siens[0];
    return {
      capability: s.id,
      label: s.label,
      module: s.module,
      niveaux: niveauxProposes(s),
      configurable: !!s.configure?.length || verbes.has('Configure'),
      verbes,
      perimetre: (vue?.scope as PlatformScope | undefined) ?? perimetreParDefaut(s),
    };
  });
}

/** Niveau affiché : le plus haut droit accordé parmi ceux que propose la rubrique. */
export function niveauDe(l: LigneRubrique): Niveau {
  for (const n of ['Validate', 'Act', 'View'] as const) {
    if (l.verbes.has(n)) return n;
  }
  return 'Aucun';
}

/** Applique un niveau (cumulatif). « Aucun » retire aussi « Configurer ». */
export function avecNiveau(l: LigneRubrique, niveau: Niveau): LigneRubrique {
  const verbes = new Set<PlatformVerb>();
  if (niveau !== 'Aucun') {
    verbes.add('View');
    if ((niveau === 'Act' || niveau === 'Validate') && l.niveaux.includes('Act')) verbes.add('Act');
    if (niveau === 'Validate') verbes.add('Validate');
    if (l.verbes.has('Configure')) verbes.add('Configure');
  }
  return { ...l, verbes };
}

/** « Configurer » : l'activer ouvre aussi la consultation. */
export function avecConfiguration(l: LigneRubrique, actif: boolean): LigneRubrique {
  const verbes = new Set(l.verbes);
  if (actif) {
    verbes.add('Configure');
    verbes.add('View');
  } else {
    verbes.delete('Configure');
  }
  return { ...l, verbes };
}

export function avecPerimetre(l: LigneRubrique, perimetre: PlatformScope): LigneRubrique {
  return { ...l, perimetre };
}

export function estModifiee(avant: LigneRubrique | undefined, apres: LigneRubrique): boolean {
  if (!avant) return apres.verbes.size > 0;
  if (avant.verbes.size !== apres.verbes.size || [...apres.verbes].some((v) => !avant.verbes.has(v))) return true;
  return apres.verbes.size > 0 && avant.perimetre !== apres.perimetre;
}

/**
 * Écritures à envoyer pour passer de l'état enregistré à l'état voulu : un droit retiré l'est sur
 * tous les périmètres où il était accordé ; un droit accordé (ou déplacé) l'est sur le périmètre
 * de la rubrique.
 */
export function ecritures(
  role: string,
  permissions: readonly IamPermissionDto[],
  avant: readonly LigneRubrique[],
  apres: readonly LigneRubrique[],
): UpsertIamPermissionRequest[] {
  const out: UpsertIamPermissionRequest[] = [];
  const parCap = new Map(avant.map((l) => [l.capability, l]));
  for (const l of apres) {
    const a = parCap.get(l.capability);
    if (!estModifiee(a, l)) continue;
    for (const verbe of ['View', 'Act', 'Validate', 'Configure'] as const) {
      const action = `${l.capability}.${verbe}`;
      const existants = permissions.filter((p) => p.isAllowed && memeRole(p.role, role) && p.action === action);
      const voulu = l.verbes.has(verbe);
      for (const p of existants) {
        if (!voulu || p.scope !== l.perimetre) out.push({ role, action, scope: p.scope, isAllowed: false });
      }
      if (voulu && !existants.some((p) => p.scope === l.perimetre)) {
        out.push({ role, action, scope: l.perimetre, isAllowed: true });
      }
    }
  }
  return out;
}

/** Nombre de rubriques ouvertes (Consulter) par rôle, pour la liste de gauche. */
export function rubriquesOuvertes(permissions: readonly IamPermissionDto[], role: string): number {
  const caps = new Set(
    permissions
      .filter((p) => p.isAllowed && memeRole(p.role, role) && p.action.endsWith('.View'))
      .map((p) => p.action),
  );
  return caps.size;
}

export function aJoker(permissions: readonly IamPermissionDto[], role: string): boolean {
  return memeRole(role, 'Admin') || permissions.some((p) => p.isAllowed && memeRole(p.role, role) && p.action === '*');
}

/**
 * Droits par défaut d'un rôle (ceux du catalogue, ajoutés au démarrage de l'Annuaire) : sert à
 * « Rétablir les droits par défaut » et à repérer les rubriques réglées autrement.
 */
export function lignesParDefaut(
  role: string,
  ecrans: readonly PlatformScreen[],
  perimetreParDefaut: (ecran: PlatformScreen) => PlatformScope,
): LigneRubrique[] {
  const a = (liste?: readonly string[]) => !!liste?.some((r) => memeRole(r, role));
  return ecrans.map((s) => {
    const verbes = new Set<PlatformVerb>();
    if (a(s.view)) verbes.add('View');
    if (a(s.act)) verbes.add('Act');
    if (a(s.validate)) verbes.add('Validate');
    if (a(s.configure)) verbes.add('Configure');
    if (verbes.size) verbes.add('View');
    return {
      capability: s.id,
      label: s.label,
      module: s.module,
      niveaux: niveauxProposes(s),
      configurable: !!s.configure?.length,
      verbes,
      perimetre: perimetreParDefaut(s),
    };
  });
}

/** Niveau (ou « Configurer ») différent du réglage par défaut ; le périmètre n'est pas comparé. */
export function differeDuDefaut(defaut: LigneRubrique | undefined, l: LigneRubrique): boolean {
  const d = defaut?.verbes ?? new Set<PlatformVerb>();
  return d.size !== l.verbes.size || [...l.verbes].some((v) => !d.has(v));
}

/** Clés « écran.Verbe:Périmètre » des lignes (brouillon compris), pour l'aperçu du menu. */
export function clesDesLignes(lignes: readonly LigneRubrique[]): string[] {
  return lignes.flatMap((l) => [...l.verbes].map((v) => `${l.capability}.${v}:${l.perimetre}`));
}
