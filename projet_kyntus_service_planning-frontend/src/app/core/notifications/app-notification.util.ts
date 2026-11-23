import type { KyntusNotificationSource } from './kyntus-notification-hub.service';

/**
 * v62 — Notifications « d'application » envoyées par le serveur après une action (présence saisie,
 * congé validé, document traité, réaffectation…). Elles arrivent par le même canal que les plannings
 * publiés, avec une clé « NOTIF-… », un titre, une catégorie et le lien de la page concernée.
 */
export const PREFIXE_NOTIFICATION_APPLICATION = 'NOTIF-';

export function estNotificationApplication(weekCode: string | null | undefined): boolean {
  return (weekCode ?? '').toUpperCase().startsWith(PREFIXE_NOTIFICATION_APPLICATION);
}

/** Rubrique de la cloche (et préférence utilisateur) d'une catégorie serveur. */
export function sourceDeCategorie(categorie: string | null | undefined): KyntusNotificationSource {
  switch ((categorie ?? '').trim().toLowerCase()) {
    case 'conges':
    case 'conge':
      return 'conge';
    case 'documentation':
      return 'documentation';
    case 'formation':
      return 'formation';
    default:
      return 'planning';
  }
}

/** Libellé de rubrique plus précis pour les notifications d'application. */
export function libelleCategorie(categorie: string | null | undefined): string | null {
  switch ((categorie ?? '').trim().toLowerCase()) {
    case 'presences':
      return 'Présences';
    case 'affectation':
      return 'Affectation';
    case 'conges':
    case 'conge':
      return 'Congés';
    case 'documentation':
      return 'Documents';
    case 'planning':
      return 'Planning';
    default:
      return null;
  }
}

/** Ton de la notification d'après son titre (refus, annulation, absence → attention). */
export function severiteNotification(titre: string | null | undefined): 'info' | 'success' | 'warning' {
  const t = (titre ?? '').toLowerCase();
  if (/(refus|annul|retir|absence enregistr)/.test(t)) return 'warning';
  if (/(valid|disponible|autoris|nouvelle affectation)/.test(t)) return 'success';
  return 'info';
}

/** Lien interne sûr : uniquement une route de l'application. */
export function lienInterne(lien: string | null | undefined, repli = '/notifications'): string {
  const l = (lien ?? '').trim();
  return l.startsWith('/') && !l.startsWith('//') ? l : repli;
}

/** « à l'instant », « il y a 5 min », « il y a 3 h », « hier », sinon la date. */
export function depuis(date: Date, maintenant = new Date()): string {
  const s = Math.max(0, Math.round((maintenant.getTime() - date.getTime()) / 1000));
  if (s < 60) return 'à l’instant';
  const m = Math.round(s / 60);
  if (m < 60) return `il y a ${m} min`;
  const h = Math.round(m / 60);
  if (h < 24) return `il y a ${h} h`;
  if (h < 48) return 'hier';
  return date.toLocaleDateString('fr-FR', { day: '2-digit', month: '2-digit' });
}

/** Identifiants lus conservés entre deux rafraîchissements (notifications calculées côté application). */
export function lireIdsLus(cle: string): Set<string> {
  if (typeof localStorage === 'undefined') return new Set();
  try {
    const arr = JSON.parse(localStorage.getItem(cle) ?? '[]') as unknown;
    return new Set(Array.isArray(arr) ? arr.filter((x): x is string => typeof x === 'string') : []);
  } catch {
    return new Set();
  }
}

export function ecrireIdsLus(cle: string, ids: Set<string>, max = 300): void {
  if (typeof localStorage === 'undefined') return;
  try {
    localStorage.setItem(cle, JSON.stringify([...ids].slice(-max)));
  } catch {
    /* stockage plein ou interdit : sans conséquence */
  }
}
