/**
 * v63.3 — Cause exacte d'une journée d'absence du planning.
 *
 * Le planning connaît le type d'absence (`absenceType`) ; les exports et les écrans affichent
 * le code du service paie (AM, ABS N, CP…) et son libellé, jamais « CP » par défaut.
 * Mêmes codes que le tableau des présences (PresenceCodes côté serveur).
 */
export const CODE_PAR_TYPE_ABSENCE: Record<string, { code: string; libelle: string }> = {
  AbsenceAutorisee: { code: 'ABS', libelle: 'Absence autorisée' },
  AbsenceNonAutorisee: { code: 'ABS N', libelle: 'Absence non autorisée' },
  ArretMaladie: { code: 'AM', libelle: 'Arrêt maladie' },
  NeTravaillePlus: { code: 'AR', libelle: 'Ne travaille plus' },
  AccidentDeTravail: { code: 'AT', libelle: 'Accident de travail' },
  CongesPayes: { code: 'CP', libelle: 'Congés payés' },
  CongesSansSolde: { code: 'CSS', libelle: 'Congés sans solde' },
  DeuilFamilial: { code: 'DF', libelle: 'Deuil familial' },
  EnfantMalade: { code: 'ENF', libelle: 'Enfant malade' },
  Maternite: { code: 'MAT', libelle: 'Maternité' },
  Paternite: { code: 'PAT', libelle: 'Paternité' },
  Recup: { code: 'REC', libelle: 'Récupération' },
};

/** Code paie d'une absence du planning ; « ABS » quand la cause n'est pas connue. */
export function codeAbsence(absenceType: string | null | undefined): string {
  const t = (absenceType ?? '').trim();
  return CODE_PAR_TYPE_ABSENCE[t]?.code ?? (t ? t : 'ABS');
}

/** Libellé d'une absence du planning ; « Absence » quand la cause n'est pas connue. */
export function libelleAbsence(absenceType: string | null | undefined): string {
  const t = (absenceType ?? '').trim();
  return CODE_PAR_TYPE_ABSENCE[t]?.libelle ?? (t ? t : 'Absence');
}

/** « AM — Arrêt maladie » (exports lisibles) ; « Absence » si la cause n'est pas connue. */
export function codeEtLibelleAbsence(absenceType: string | null | undefined): string {
  const t = (absenceType ?? '').trim();
  const connu = CODE_PAR_TYPE_ABSENCE[t];
  return connu ? `${connu.code} — ${connu.libelle}` : libelleAbsence(absenceType);
}
