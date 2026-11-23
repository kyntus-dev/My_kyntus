import type { JournalLigne } from '../../../core/services/journal-actions.service';

/** v64 — Mise en forme des lignes du journal d'actions (pur, testable). */
export interface JournalChangement {
  champ: string;
  avant: string | null;
  apres: string | null;
}

/** Champs avant / après, dans l'ordre d'apparition, sans doublon. */
export function journalChangements(l: Pick<JournalLigne, 'anciennesValeurs' | 'nouvellesValeurs'>): JournalChangement[] {
  const avant = l.anciennesValeurs ?? {};
  const apres = l.nouvellesValeurs ?? {};
  const champs = [...new Set([...Object.keys(avant), ...Object.keys(apres)])];
  return champs.map((champ) => ({ champ, avant: avant[champ] ?? null, apres: apres[champ] ?? null }));
}

/** Résumé d'une ligne pour le tableau : « Champ : avant → après » (3 champs au plus). */
export function journalResume(l: Pick<JournalLigne, 'anciennesValeurs' | 'nouvellesValeurs' | 'details'>): string {
  const c = journalChangements(l);
  if (!c.length) return l.details ?? '—';
  const premiers = c.slice(0, 3).map((x) =>
    x.avant === null ? `${x.champ} : ${x.apres ?? '—'}` : `${x.champ} : ${x.avant} → ${x.apres ?? '—'}`,
  );
  return premiers.join(' · ') + (c.length > 3 ? ` · +${c.length - 3}` : '');
}

/** Couleur du badge d'opération. */
export function operationTon(operation: string): 'creation' | 'suppression' | 'validation' | 'alerte' | 'neutre' {
  const o = operation.toLowerCase();
  if (o.startsWith('création') || o === 'saisie' || o === 'import' || o === 'réactivation') return 'creation';
  if (o.startsWith('suppression') || o === 'désactivation' || o.startsWith('retour au calcul')) return 'suppression';
  if (o === 'validation' || o === 'génération' || o === 'génération automatique' || o === 'régénération') return 'validation';
  if (o === 'dévalidation' || o.startsWith('correction') || o.includes('droits')) return 'alerte';
  return 'neutre';
}
