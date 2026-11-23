/**
 * v64.2 — Modification des dates d'une demande de congé (historique, Admin / RH).
 * Aperçu et contrôles avant l'envoi : mêmes règles que DemandeConge.Modifier côté serveur,
 * qui reste l'arbitre (mois interdits, quota, chevauchement et solde n'y sont vérifiés que là).
 */
import { StatutDemande, TypeConge, TypeCongeExceptionnel } from '../../../core/models/conge.models';

/** Durées fixes des congés exceptionnels (PolitiqueConge côté serveur). */
export const DUREES_EXCEPTIONNELLES: Record<TypeCongeExceptionnel, number> = {
  [TypeCongeExceptionnel.Mariage]: 4,
  [TypeCongeExceptionnel.DecesConjoint]: 3,
  [TypeCongeExceptionnel.DecesParent]: 2,
  [TypeCongeExceptionnel.Naissance]: 3,
  [TypeCongeExceptionnel.Maternite]: 98,
};

export interface DemandeModifiable {
  typeConge: TypeConge;
  typeExceptionnel: TypeCongeExceptionnel | null;
  statut: StatutDemande;
  dateDebut: string;
  dateFin: string;
}

const iso = (v: string | null | undefined) => (v ?? '').slice(0, 10);
const fr = (v: string) => v.split('-').reverse().join('/');

function utc(isoDate: string): number {
  const [y, m, d] = isoDate.split('-').map(Number);
  return Date.UTC(y, m - 1, d);
}

function versIso(ms: number): string {
  return new Date(ms).toISOString().slice(0, 10);
}

/** Statuts dont les dates se modifient : en attente (superviseur ou RH) et validée. */
export function estModifiable(d: Pick<DemandeModifiable, 'statut' | 'typeConge' | 'typeExceptionnel'>): boolean {
  const statutOk = d.statut === StatutDemande.EnAttente
    || d.statut === StatutDemande.EnAttenteRh
    || d.statut === StatutDemande.Validee;
  const typeOk = d.typeConge === TypeConge.Annuel
    || (d.typeConge === TypeConge.Exceptionnel && d.typeExceptionnel != null);
  return statutOk && typeOk;
}

/** Jours ouvrables (samedi et dimanche exclus), bornes comprises. */
export function joursOuvrables(debut: string, fin: string): number {
  const a = iso(debut);
  const b = iso(fin);
  if (!a || !b || a > b) return 0;
  let n = 0;
  for (let t = utc(a); t <= utc(b); t += 86_400_000) {
    const j = new Date(t).getUTCDay();
    if (j !== 0 && j !== 6) n++;
  }
  return n;
}

/** Fin d'un congé exceptionnel : début + durée − 1 (jours calendaires). */
export function finExceptionnelle(debut: string, type: TypeCongeExceptionnel): string {
  const duree = DUREES_EXCEPTIONNELLES[type] ?? 1;
  return versIso(utc(iso(debut)) + (duree - 1) * 86_400_000);
}

/** Nouvelle période et durée telles que le serveur les calculera. */
export function apercuModification(
  d: DemandeModifiable,
  debut: string,
  fin: string,
): { debut: string; fin: string; jours: number } {
  const a = iso(debut);
  if (d.typeConge === TypeConge.Exceptionnel && d.typeExceptionnel != null && a) {
    return { debut: a, fin: finExceptionnelle(a, d.typeExceptionnel), jours: DUREES_EXCEPTIONNELLES[d.typeExceptionnel] ?? 0 };
  }
  const b = iso(fin);
  return { debut: a, fin: b, jours: joursOuvrables(a, b) };
}

/** Message d'erreur, ou null si la modification peut être envoyée. */
export function controlerModification(
  d: DemandeModifiable,
  debut: string,
  fin: string,
  aujourdhui: string,
): string | null {
  if (!estModifiable(d)) return 'Cette demande ne peut plus être modifiée.';
  const p = apercuModification(d, debut, fin);
  if (!p.debut) return 'La date de début est obligatoire.';
  if (!p.fin) return 'La date de fin est obligatoire.';
  if (p.fin < p.debut) return 'La date de fin doit être postérieure ou égale à la date de début.';
  if (p.jours <= 0) return 'La période ne contient aucun jour ouvrable.';
  if (p.debut !== iso(d.dateDebut) && p.debut < iso(aujourdhui)) {
    return 'La nouvelle date de début ne peut pas être dans le passé.';
  }
  if (p.debut === iso(d.dateDebut) && p.fin === iso(d.dateFin)) {
    return `Les dates sont identiques (du ${fr(p.debut)} au ${fr(p.fin)}) : rien à modifier.`;
  }
  return null;
}
