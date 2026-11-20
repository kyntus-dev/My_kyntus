import type {
  AnalyseFichesReferenceDto,
  ApplicationFichesReferenceDto,
  LigneFicheReferenceDto,
  StatutLigneFiche,
} from '../services/fiches-reference-api.service';

/** v63 — Fichier RH de référence : règles d'écran (choix par ligne, filtres, rapport). */

export const LIBELLE_STATUT_FICHE: Record<StatutLigneFiche, string> = {
  identifie: 'Identifié',
  'a-verifier': 'À vérifier',
  ambigu: 'À choisir',
  nouveau: 'Nouveau',
  'sorti-inconnu': 'Sorti, sans fiche',
  historique: 'Historique',
  ignore: 'Ignoré',
};

export const LIBELLE_RESULTAT_FICHE: Record<string, string> = {
  'mis-a-jour': 'Mise à jour',
  'sans-changement': 'Déjà à jour',
  cree: 'Créée',
  desactive: 'Mise à jour + désactivée',
  ignore: 'Non appliquée',
  erreur: 'Erreur',
};

export type FiltreFiches = 'tous' | 'modifs' | 'verifier' | 'nouveaux' | 'anomalies' | 'incompletes' | 'ecartes';

export const FILTRES_FICHES: readonly { valeur: FiltreFiches; libelle: string }[] = [
  { valeur: 'tous', libelle: 'Toutes les lignes' },
  { valeur: 'modifs', libelle: 'Fiches modifiées' },
  { valeur: 'verifier', libelle: 'À vérifier / à choisir' },
  { valeur: 'nouveaux', libelle: 'Nouveaux employés' },
  { valeur: 'anomalies', libelle: 'Valeurs refusées' },
  { valeur: 'incompletes', libelle: 'Fiches incomplètes' },
  { valeur: 'ecartes', libelle: 'Ignorées / historique' },
];

/** Choix proposé par le serveur : identifiés appliqués, nouveaux créés, le reste attend une décision. */
export function choixInitiaux(analyse: AnalyseFichesReferenceDto): Record<number, string> {
  const choix: Record<number, string> = {};
  for (const l of analyse.lignes) choix[l.numero] = l.choixParDefaut || 'ignorer';
  return choix;
}

/** Une ligne peut-elle être modifiée à l'écran ? (les lignes sans employé et l'historique ne le sont pas) */
export function ligneChoisissable(l: LigneFicheReferenceDto): boolean {
  return l.statut !== 'ignore' && l.statut !== 'historique';
}

/** Options du sélecteur de chaque ligne. */
export function optionsChoix(l: LigneFicheReferenceDto): { valeur: string; libelle: string }[] {
  const options: { valeur: string; libelle: string }[] = [{ valeur: 'ignorer', libelle: 'Ne pas appliquer' }];
  const vus = new Set<number>();
  if (l.userId && l.ficheApp) {
    options.push({ valeur: `user:${l.userId}`, libelle: `Mettre à jour : ${l.ficheApp}` });
    vus.add(l.userId);
  }
  for (const c of l.candidats) {
    if (vus.has(c.userId)) continue;
    vus.add(c.userId);
    const matricule = c.matricule ? ` · N° ${c.matricule}` : '';
    const inactif = c.actif ? '' : ' · inactive';
    options.push({ valeur: `user:${c.userId}`, libelle: `Mettre à jour : ${c.nom} ${c.prenom}${matricule}${inactif} (${c.raison})` });
  }
  if (!l.sorti && (l.statut === 'nouveau' || l.statut === 'ambigu' || l.statut === 'a-verifier')) {
    options.push({ valeur: 'creer', libelle: `Créer une nouvelle fiche${l.roleSiCreation ? ` (${l.roleSiCreation})` : ''}` });
  }
  return options;
}

export function nombreAAppliquer(choix: Record<number, string>): { majs: number; creations: number } {
  let majs = 0;
  let creations = 0;
  for (const c of Object.values(choix)) {
    if (c.startsWith('user:')) majs++;
    else if (c === 'creer') creations++;
  }
  return { majs, creations };
}

function sansAccents(texte: string): string {
  return texte.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase();
}

export function filtrerLignes(
  lignes: readonly LigneFicheReferenceDto[],
  filtre: FiltreFiches,
  recherche: string,
): LigneFicheReferenceDto[] {
  const q = sansAccents(recherche.trim());
  return lignes.filter((l) => {
    const garde = (() => {
      switch (filtre) {
        case 'modifs': return l.statut === 'identifie' && l.modifications.length > 0;
        case 'verifier': return l.statut === 'a-verifier' || l.statut === 'ambigu';
        case 'nouveaux': return l.statut === 'nouveau';
        case 'anomalies': return l.anomalies.length > 0;
        case 'incompletes': return l.champsManquants.length > 0 && !l.sorti;
        case 'ecartes': return l.statut === 'ignore' || l.statut === 'historique' || l.statut === 'sorti-inconnu';
        default: return true;
      }
    })();
    if (!garde) return false;
    if (!q) return true;
    return sansAccents(`${l.agent ?? ''} ${l.numeroOrdre ?? ''} ${l.ficheApp ?? ''}`).includes(q);
  });
}

/** Valeur lisible : les dates « aaaa-mm-jj » passent en « jj/mm/aaaa ». */
export function valeurLisible(valeur: string | null | undefined): string {
  if (valeur === null || valeur === undefined || valeur === '') return '—';
  const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(valeur);
  return m ? `${m[3]}/${m[2]}/${m[1]}` : valeur;
}

function csv(valeur: string | number | null | undefined): string {
  let t = valeur === null || valeur === undefined ? '' : String(valeur);
  if (/^[=+\-@]/.test(t)) t = `'${t}`;
  return /[;"\n]/.test(t) ? `"${t.replace(/"/g, '""')}"` : t;
}

/**
 * Rapport détaillé de l'application (CSV « ; », une ligne par champ modifié, plus une ligne par
 * fiche sans modification, en erreur ou non appliquée) — à garder avec le fichier importé.
 */
export function rapportApplicationCsv(rapport: ApplicationFichesReferenceDto): string {
  const lignes = [
    ['Ligne du fichier', 'Employé', 'Résultat', 'Message', 'Champ', 'Ancienne valeur', 'Nouvelle valeur',
      'Valeurs refusées', 'Champs encore manquants', 'Annuaire synchronisé'].join(';'),
  ];
  for (const l of rapport.lignes) {
    const commun = [csv(l.numero), csv(l.agent), csv(LIBELLE_RESULTAT_FICHE[l.resultat] ?? l.resultat), csv(l.message)];
    const fin = [csv(l.anomalies.join(' | ')), csv(l.champsManquants.join(', ')), l.annuaireSynchronise ? 'Oui' : 'Non'];
    if (l.modifications.length === 0) {
      lignes.push([...commun, '', '', '', ...fin].join(';'));
      continue;
    }
    for (const m of l.modifications)
      lignes.push([...commun, csv(m.libelle), csv(valeurLisible(m.ancienne)), csv(valeurLisible(m.nouvelle)), ...fin].join(';'));
  }
  return '﻿' + lignes.join('\n');
}
