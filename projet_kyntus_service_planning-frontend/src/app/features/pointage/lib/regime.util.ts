import type {
  RegimeCible,
  RegimeCibleDto,
  RegimeHoraireDto,
  RegimeHoraireRequest,
  RegimeJourDto,
} from '../../../core/models/regime-horaire.models';

export const JOURS_SEMAINE: { iso: number; court: string; long: string }[] = [
  { iso: 1, court: 'Lun', long: 'Lundi' },
  { iso: 2, court: 'Mar', long: 'Mardi' },
  { iso: 3, court: 'Mer', long: 'Mercredi' },
  { iso: 4, court: 'Jeu', long: 'Jeudi' },
  { iso: 5, court: 'Ven', long: 'Vendredi' },
  { iso: 6, court: 'Sam', long: 'Samedi' },
  { iso: 7, court: 'Dim', long: 'Dimanche' },
];

export const CIBLE_LABELS: Record<RegimeCible, string> = {
  Pole: 'Pôle',
  Cellule: 'Cellule',
  Service: 'Service',
  Utilisateur: 'Collaborateur',
};

/** Ligne du formulaire : un jour de la semaine, travaillé ou non. */
export interface JourForm {
  iso: number;
  travaille: boolean;
  arrivee: string;
  /** Production « HH:mm » (durée). */
  production: string;
}

export interface RegimeForm {
  id: number | null;
  nom: string;
  /** Pause autorisée « HH:mm » (durée). */
  pause: string;
  accesCalculSortie: boolean;
  jours: JourForm[];
}

/** « 08:40 » → 520 ; null si invalide. */
export function dureeVersMinutes(value: string): number | null {
  const m = /^\s*(\d{1,2}):(\d{2})\s*$/.exec(value ?? '');
  if (!m || Number(m[2]) > 59) return null;
  return Number(m[1]) * 60 + Number(m[2]);
}

/** 520 → « 08:40 » */
export function minutesVersDuree(minutes: number): string {
  const m = Math.max(0, Math.round(minutes));
  return `${String(Math.floor(m / 60)).padStart(2, '0')}:${String(m % 60).padStart(2, '0')}`;
}

/** Modèle proposé : l'Excel (08:40 du lundi au jeudi, 07:40 le vendredi, pause 01:20, samedi non travaillé). */
export function nouveauRegime(): RegimeForm {
  return {
    id: null,
    nom: '',
    pause: '01:20',
    accesCalculSortie: true,
    jours: JOURS_SEMAINE.map((j) => ({
      iso: j.iso,
      travaille: j.iso <= 5,
      arrivee: '08:00',
      production: j.iso === 5 ? '07:40' : '08:40',
    })),
  };
}

export function regimeVersForm(r: RegimeHoraireDto): RegimeForm {
  return {
    id: r.id,
    nom: r.nom,
    pause: minutesVersDuree(r.pauseAutoriseeMinutes),
    accesCalculSortie: r.accesCalculSortie,
    jours: JOURS_SEMAINE.map((j) => {
      const d = r.jours.find((x) => x.jourSemaine === j.iso);
      return {
        iso: j.iso,
        travaille: !!d,
        arrivee: d?.arrivee ?? '08:00',
        production: d ? minutesVersDuree(d.productionMinutes) : '08:40',
      };
    }),
  };
}

/** Formulaire → requête API ; message d'erreur si la saisie est incomplète. */
export function formVersRequest(f: RegimeForm): { request: RegimeHoraireRequest } | { erreur: string } {
  if (!f.nom.trim()) return { erreur: 'Donnez un nom au régime.' };
  const pause = dureeVersMinutes(f.pause);
  if (pause === null || pause < 1) return { erreur: 'Pause autorisée invalide (format hh:mm, ex. 01:20).' };
  const jours: RegimeJourDto[] = [];
  for (const j of f.jours.filter((x) => x.travaille)) {
    const label = JOURS_SEMAINE.find((x) => x.iso === j.iso)!.long;
    const production = dureeVersMinutes(j.production);
    if (!/^\d{2}:\d{2}$/.test(j.arrivee ?? '')) return { erreur: `${label} : heure d’arrivée invalide.` };
    if (production === null || production < 1) return { erreur: `${label} : production invalide (format hh:mm, ex. 08:40).` };
    jours.push({ jourSemaine: j.iso, arrivee: j.arrivee, productionMinutes: production });
  }
  if (!jours.length) return { erreur: 'Cochez au moins un jour travaillé.' };
  return { request: { nom: f.nom.trim(), pauseAutoriseeMinutes: pause, accesCalculSortie: f.accesCalculSortie, jours } };
}

/** Production hebdomadaire prévue (minutes). */
export function productionHebdo(jours: readonly RegimeJourDto[]): number {
  return jours.reduce((s, j) => s + j.productionMinutes, 0);
}

/** Résumé lisible : « Lun–Jeu 08:40 · Ven 07:40 ». Les jours consécutifs identiques sont regroupés. */
export function resumeSemaine(jours: readonly RegimeJourDto[]): string {
  const tries = [...jours].sort((a, b) => a.jourSemaine - b.jourSemaine);
  const groupes: { debut: number; fin: number; production: number; arrivee: string }[] = [];
  for (const j of tries) {
    const g = groupes[groupes.length - 1];
    if (g && g.fin === j.jourSemaine - 1 && g.production === j.productionMinutes && g.arrivee === j.arrivee) g.fin = j.jourSemaine;
    else groupes.push({ debut: j.jourSemaine, fin: j.jourSemaine, production: j.productionMinutes, arrivee: j.arrivee });
  }
  const court = (iso: number) => JOURS_SEMAINE[iso - 1].court;
  return groupes
    .map((g) => `${g.debut === g.fin ? court(g.debut) : `${court(g.debut)}–${court(g.fin)}`} ${minutesVersDuree(g.production)}`)
    .join(' · ');
}

/** Recherche dans les éléments affectables (libellé ou détail), sans accents. */
export function filtrerCibles(cibles: readonly RegimeCibleDto[], recherche: string, limite = 60): RegimeCibleDto[] {
  const norm = (s: string) => s.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase();
  const q = norm(recherche.trim());
  const res = q ? cibles.filter((c) => norm(`${c.libelle} ${c.detail ?? ''}`).includes(q)) : [...cibles];
  return res.slice(0, limite);
}
