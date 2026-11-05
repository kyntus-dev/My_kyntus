import { roleNamesMatch } from '../org/org-role-assignment';
import type { MenuItem, Microservice } from './microservices.config';

/** Écrans IAM d'une entrée de menu (capability, puis ouvertureIam simple ou multiple). */
export function ecransDe(item: Pick<MenuItem, 'capability' | 'ouvertureIam'>): readonly string[] {
  if (item.capability) return [item.capability];
  const o = item.ouvertureIam;
  return !o ? [] : typeof o === 'string' ? [o] : o;
}

/**
 * Droit accordé dans une liste de clés « écran.Verbe:Périmètre » (tous périmètres confondus).
 * « Consulter » est aussi accordé par Modifier, Valider ou Configurer ; « Modifier » par Valider
 * ou Configurer — comme dans PlatformAccessService.
 */
export function droitAccorde(cles: readonly string[], ecran: string, verbe: 'View' | 'Act'): boolean {
  if (cles.includes('*:Global')) return true;
  const verbes = verbe === 'View' ? ['View', 'Act', 'Validate', 'Configure'] : ['Act', 'Validate', 'Configure'];
  return cles.some((k) => verbes.some((v) => k.startsWith(`${ecran}.${v}:`)));
}

export interface GroupeApercu {
  id: string;
  label: string;
  entrees: string[];
  /** Module dont les entrées suivent ses propres réglages (Prime, Parrainage, Documentation). */
  module?: boolean;
}

/**
 * Menu qu'un rôle verra avec ces droits (aperçu dans « Accès par rôle ») : même règle que le menu
 * réel une fois les droits chargés — une entrée liée à une rubrique s'affiche seulement si elle est
 * accordée ; les autres suivent la liste de rôles. Les deux casquettes sont réunies.
 */
export function apercuMenu(role: string, cles: readonly string[], groupes: readonly Microservice[]): GroupeApercu[] {
  const roleOk = (roles?: readonly string[]) => !roles?.length || roles.some((r) => roleNamesMatch(r, role));
  const out: GroupeApercu[] = [];
  for (const g of groupes) {
    if (g.ouvertureIam && !droitAccorde(cles, g.ouvertureIam, 'View')) continue;
    if (g.dynamicChildren) {
      if (roleOk(g.roles)) out.push({ id: g.id, label: g.label, entrees: [], module: true });
      continue;
    }
    let lieAuxDroits = false;
    const entrees: string[] = [];
    for (const c of g.children) {
      if (c.isSectionHeader) continue;
      const ecrans = ecransDe(c);
      if (ecrans.length) lieAuxDroits = true;
      const ok = ecrans.length
        ? ecrans.some((e) => droitAccorde(cles, e, c.capabilityVerb === 'Act' ? 'Act' : 'View'))
        : roleOk(c.roles);
      // « Heure de sortie » : en plus du droit, l'accès est donné compte par compte (Pointage).
      const libelle = c.route === '/pointage/heure-de-sortie' ? `${c.label} (comptes autorisés)` : c.label;
      if (ok && !entrees.includes(libelle)) entrees.push(libelle);
    }
    if (entrees.length && (roleOk(g.roles) || lieAuxDroits)) out.push({ id: g.id, label: g.label, entrees });
  }
  return out;
}
