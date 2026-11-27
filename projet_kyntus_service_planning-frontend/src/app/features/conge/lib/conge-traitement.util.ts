import { StatutDemande, normalizeStatutDemande, type CongeDisponibiliteMoisDto, type DemandeCongeDto } from '../../../core/models/conge.models';

/**
 * v85 — « Valider les congés » comme la maquette : effectif de la cellule sur la période demandée,
 * conflits, compteurs de l'en-tête. Calculs purs (testés), à partir de l'occupation du mois déjà
 * fournie par le serveur (GET /api/conges/disponibilite/mois, même contrôle d'accès que la décision).
 */

export type TonEffectif = 'ok' | 'limite' | 'conflit' | 'inconnu';

export interface JourEffectif {
  /** yyyy-MM-dd */
  jour: string;
  /** Présents = effectif de la cellule − absents ce jour-là. */
  presents: number;
  absents: number;
  sature: boolean;
}

export interface EffectifPeriode {
  effectif: number;
  quota: number | null;
  /** Le moins de présents sur la période. */
  presentsMin: number;
  ton: TonEffectif;
  jours: JourEffectif[];
}

const pad = (n: number) => String(n).padStart(2, '0');

/** Date locale « yyyy-MM-dd » d'une date ISO (sans décalage de fuseau). */
export function jourIso(d: string): string {
  return d.slice(0, 10);
}

/** Tous les jours de la période, bornes comprises (au plus 120 jours). */
export function joursPeriode(debut: string, fin: string): string[] {
  const [a1, m1, j1] = jourIso(debut).split('-').map(Number);
  const [a2, m2, j2] = jourIso(fin || debut).split('-').map(Number);
  const d = new Date(Date.UTC(a1, m1 - 1, j1));
  const f = new Date(Date.UTC(a2, m2 - 1, j2));
  const out: string[] = [];
  while (d <= f && out.length < 120) {
    out.push(`${d.getUTCFullYear()}-${pad(d.getUTCMonth() + 1)}-${pad(d.getUTCDate())}`);
    d.setUTCDate(d.getUTCDate() + 1);
  }
  return out;
}

/** Mois couverts par la période (pour demander l'occupation de chaque mois). */
export function moisCouverts(debut: string, fin: string): { annee: number; mois: number }[] {
  const vus = new Set<string>();
  const out: { annee: number; mois: number }[] = [];
  for (const j of joursPeriode(debut, fin)) {
    const cle = j.slice(0, 7);
    if (vus.has(cle)) continue;
    vus.add(cle);
    out.push({ annee: Number(cle.slice(0, 4)), mois: Number(cle.slice(5, 7)) });
  }
  return out;
}

export function cleOccupation(employeId: string, annee: number, mois: number): string {
  return `${employeId.toLowerCase()}|${annee}-${pad(mois)}`;
}

/**
 * Effectif de la cellule sur la période : présents le jour le plus chargé, et ton.
 * - conflit : un jour atteint le quota d'absences de la cellule (jour « saturé ») ;
 * - limite  : il ne reste qu'une place avant le quota ;
 * - inconnu : occupation non disponible (pas de cellule, pas d'effectif, ou erreur).
 * Les samedis et dimanches sans aucun absent sont ignorés (journées non travaillées).
 */
export function effectifPeriode(
  debut: string,
  fin: string,
  occupations: readonly (CongeDisponibiliteMoisDto | null | undefined)[],
): EffectifPeriode | null {
  const parJour = new Map<string, { absents: number; sature: boolean }>();
  let effectif = 0;
  let quota: number | null = null;
  for (const o of occupations) {
    if (!o) continue;
    effectif = Math.max(effectif, o.effectif || 0);
    if (o.quota != null) quota = quota == null ? o.quota : Math.min(quota, o.quota);
    for (const j of o.jours ?? []) parJour.set(jourIso(j.jour), { absents: j.absents || 0, sature: !!j.sature });
  }
  if (!effectif) return null;
  const jours: JourEffectif[] = [];
  for (const j of joursPeriode(debut, fin)) {
    const occ = parJour.get(j);
    if (!occ) continue;
    const dow = new Date(`${j}T12:00:00Z`).getUTCDay();
    if ((dow === 0 || dow === 6) && occ.absents === 0) continue;
    jours.push({ jour: j, absents: occ.absents, presents: Math.max(0, effectif - occ.absents), sature: occ.sature });
  }
  if (!jours.length) return { effectif, quota, presentsMin: effectif, ton: 'inconnu', jours };
  const presentsMin = Math.min(...jours.map((x) => x.presents));
  const maxAbsents = Math.max(...jours.map((x) => x.absents));
  const ton: TonEffectif = jours.some((x) => x.sature) || (quota != null && maxAbsents >= quota)
    ? 'conflit'
    : quota != null && maxAbsents >= quota - 1
      ? 'limite'
      : 'ok';
  return { effectif, quota, presentsMin, ton, jours };
}

/** Demandes validées pendant le mois en cours (date de décision). */
export function valideesCeMois(demandes: readonly DemandeCongeDto[], maintenant: Date = new Date()): number {
  const prefixe = `${maintenant.getFullYear()}-${pad(maintenant.getMonth() + 1)}`;
  return demandes.filter((d) => normalizeStatutDemande(d.statut) === StatutDemande.Validee && (d.dateDecision ?? '').startsWith(prefixe)).length;
}

/** Initiales d'un collaborateur (avatar). */
export function initiales(prenom: string | null | undefined, nom: string | null | undefined, repli = ''): string {
  const p = (prenom ?? '').trim();
  const n = (nom ?? '').trim();
  const i = `${p.charAt(0)}${n.charAt(0)}`.toUpperCase();
  return i || repli.slice(0, 2).toUpperCase();
}

const COULEURS_AVATAR = ['#7c3aed', '#0e7490', '#b45309', '#2563eb', '#db2777', '#16a34a', '#475569'];

/** Couleur stable d'avatar d'après l'identifiant. */
export function couleurAvatar(id: string): string {
  let h = 0;
  for (const c of id) h = (h * 31 + c.charCodeAt(0)) >>> 0;
  return COULEURS_AVATAR[h % COULEURS_AVATAR.length];
}

/** Libellé de la période : « 22 → 24 déc. », « 26 déc. ». */
export function libellePeriode(debut: string, fin: string): string {
  const fmt = (s: string) => {
    const [a, m, j] = jourIso(s).split('-').map(Number);
    return new Date(Date.UTC(a, m - 1, j)).toLocaleDateString('fr-FR', { day: 'numeric', month: 'short', timeZone: 'UTC' });
  };
  const d = fmt(debut);
  const f = fmt(fin || debut);
  if (d === f) return d;
  const [jd, ...md] = d.split(' ');
  const [, ...mf] = f.split(' ');
  return md.join(' ') === mf.join(' ') ? `${jd} → ${f}` : `${d} → ${f}`;
}
