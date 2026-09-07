import { triggerBlobDownload } from '@/features/documentation/lib/documentation-download.util';

/** Asset statique servi par le SPA (voir public/assets/prime/). */
export const PRIME_FICHE_STANDARD_TEMPLATE_ASSET_PATH = '/assets/prime/fiche-prime-modele-standard.xlsx';

/** Nom proposé au téléchargement (layout v2 — docs/prime-fiche-template-v2.md). */
export const PRIME_FICHE_STANDARD_TEMPLATE_DOWNLOAD_NAME = 'Fiche-PRIME-modele-standard.xlsx';

/** Télécharge le classeur exemplaire officiel (format standard fiche PRIME v2). */
export async function downloadPrimeFicheStandardTemplate(): Promise<void> {
  const resp = await fetch(PRIME_FICHE_STANDARD_TEMPLATE_ASSET_PATH, { cache: 'no-cache' });
  if (!resp.ok) {
    throw new Error('Modèle standard introuvable sur le serveur.');
  }
  const blob = await resp.blob();
  triggerBlobDownload(blob, PRIME_FICHE_STANDARD_TEMPLATE_DOWNLOAD_NAME);
}
