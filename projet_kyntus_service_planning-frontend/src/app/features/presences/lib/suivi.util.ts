import type { PresenceAgentRefDto } from '../../../core/models/presence.models';
import { periodePaie, ymd } from './presence.util';

/** Plage affichée par les listes de suivi : période de paie ou 12 derniers mois. */
export type Plage = 'paie' | 'annee';

export function plageDates(plage: Plage, decalage: number, aujourdhui = new Date()): { du: string; au: string; libelle: string } {
  const p = periodePaie(aujourdhui, decalage);
  if (plage === 'paie') return { du: p.du, au: p.au, libelle: `Paie de ${p.libelle}` };
  const fin = new Date(aujourdhui.getFullYear(), aujourdhui.getMonth(), aujourdhui.getDate());
  const debut = new Date(fin.getFullYear() - 1, fin.getMonth(), fin.getDate() + 1);
  return { du: ymd(debut), au: ymd(fin), libelle: '12 derniers mois' };
}

/** Agents groupés par cellule (listes de choix), avec filtre texte. */
export function agentsParCellule(agents: readonly PresenceAgentRefDto[], texte = ''): { cellule: string; agents: PresenceAgentRefDto[] }[] {
  const q = sansAccents(texte.trim());
  const groupes = new Map<string, PresenceAgentRefDto[]>();
  for (const a of agents) {
    if (q && !sansAccents(`${a.nom} ${a.cellule ?? ''}`).includes(q)) continue;
    const c = a.cellule ?? 'Sans cellule';
    groupes.set(c, [...(groupes.get(c) ?? []), a]);
  }
  return [...groupes.entries()]
    .sort(([x], [y]) => x.localeCompare(y, 'fr', { sensitivity: 'base' }))
    .map(([cellule, liste]) => ({ cellule, agents: liste }));
}

export function sansAccents(s: string): string {
  return s.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase();
}

export function dateFr(iso: string | null | undefined): string {
  return iso ? iso.slice(0, 10).split('-').reverse().join('/') : '—';
}

export function montantFr(n: number): string {
  return n.toLocaleString('fr-FR', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}

export function tailleFr(octets: number): string {
  if (octets < 1024) return `${octets} o`;
  if (octets < 1024 * 1024) return `${Math.round(octets / 1024)} Ko`;
  return `${(octets / 1024 / 1024).toLocaleString('fr-FR', { maximumFractionDigits: 1 })} Mo`;
}

/** Message d'erreur lisible pour une réponse HTTP du Planning. */
export function messageErreur(err: { status?: number; error?: { message?: string } } | null | undefined, defaut: string): string {
  if (err?.status === 403) return err.error?.message ?? 'Accès réservé aux rôles autorisés dans « Accès par rôle ».';
  return err?.error?.message ?? defaut;
}
