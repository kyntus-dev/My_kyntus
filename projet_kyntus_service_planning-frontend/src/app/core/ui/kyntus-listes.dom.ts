import {
  colonneStatut,
  filtresStatut,
  libelleLignes,
  ligneVisible,
  tableauEligible,
  tonStatut,
  type FiltreStatut,
} from './kyntus-listes.util';

/**
 * v84 — Barre de traitement posée sur les tableaux de données de toutes les pages (voir
 * kyntus-listes.util.ts). Fonctionne sur le DOM déjà rendu par Angular, sans toucher aux composants :
 * - ajoute une barre AVANT le tableau (filtres par statut avec compteurs, recherche, nombre de lignes) ;
 * - colore les cellules de la colonne « Statut » en pastilles (classe ajoutée, texte inchangé) ;
 * - masque à l'affichage les lignes qui ne correspondent pas au filtre (attribut `hidden`).
 * Rien n'est supprimé ni modifié : effacer la recherche ou choisir « Tous » réaffiche tout.
 * Un tableau qui a des cases à cocher garde toutes ses lignes visibles (pas de sélection cachée).
 */

interface EtatTableau {
  barre: HTMLElement;
  recherche: string;
  statut: string;
  colonne: number;
  filtrable: boolean;
}

const ATTR = 'data-ky-liste';

export class KyntusListes {
  private readonly etats = new Map<HTMLTableElement, EtatTableau>();
  private observateur: MutationObserver | null = null;
  private minuteur: ReturnType<typeof setTimeout> | null = null;

  constructor(private readonly racine: ParentNode = document) {}

  demarrer(): void {
    if (this.observateur || typeof MutationObserver === 'undefined') return;
    this.observateur = new MutationObserver((mutations) => {
      // Nos propres changements (contenu de la barre, pose ou retrait de la barre) ne relancent pas
      // d'analyse ; l'attribut hidden et les data-ky-* ne sont pas observés (seuls nœuds et textes le sont).
      if (mutations.every((m) => this.estNotreMutation(m))) return;
      this.planifier();
    });
    this.observateur.observe(document.body, { childList: true, subtree: true, characterData: true });
    this.planifier();
  }

  arreter(): void {
    this.observateur?.disconnect();
    this.observateur = null;
    if (this.minuteur) clearTimeout(this.minuteur);
    for (const [table, e] of this.etats) this.nettoyer(table, e);
    this.etats.clear();
  }

  private planifier(): void {
    if (this.minuteur) clearTimeout(this.minuteur);
    this.minuteur = setTimeout(() => this.analyser(), 180);
  }

  private estNotreMutation(m: MutationRecord): boolean {
    const cible = m.target instanceof Element ? m.target : m.target.parentElement;
    if (cible?.closest?.('.ky-tt')) return true;
    const noeuds = [...Array.from(m.addedNodes), ...Array.from(m.removedNodes)];
    return noeuds.length > 0 && noeuds.every((n) => n instanceof Element && n.classList.contains('ky-tt'));
  }

  /** Parcourt les tableaux de la zone de contenu, pose ou met à jour les barres. */
  analyser(): void {
    // Barres dont le tableau a disparu (changement de page, @if…) : retirées.
    for (const [table, e] of this.etats) {
      if (!table.isConnected) {
        e.barre.remove();
        this.etats.delete(table);
      }
    }
    const tables = Array.from(this.racine.querySelectorAll<HTMLTableElement>('.ks-content table'));
    for (const table of tables) {
      const corps = table.tBodies[0];
      const lignes = corps ? Array.from(corps.rows) : [];
      const desc = {
        classes: table.className || '',
        lignes: lignes.length,
        aEntete: !!table.tHead && table.tHead.rows.length > 0,
        aCasesACocher: !!corps?.querySelector('input[type="checkbox"]'),
        aFusions: !!corps?.querySelector('td[rowspan]:not([rowspan="1"]), th[rowspan]:not([rowspan="1"])'),
        dansFenetre: !!table.closest('[role="dialog"], .modal, .ky-viewport-overlay, dialog, [data-ky-no-liste]'),
      };
      const existant = this.etats.get(table);
      if (!tableauEligible(desc)) {
        if (existant) this.nettoyer(table, existant);
        continue;
      }
      const etat = existant ?? this.poser(table, desc.aCasesACocher);
      this.mettreAJour(table, etat);
    }
  }

  private entetes(table: HTMLTableElement): string[] {
    const ligne = table.tHead?.rows[table.tHead.rows.length - 1];
    return ligne ? Array.from(ligne.cells).map((c) => (c.textContent ?? '').trim()) : [];
  }

  private poser(table: HTMLTableElement, aCases: boolean): EtatTableau {
    const barre = document.createElement('div');
    barre.className = 'ky-tt';
    barre.setAttribute('role', 'region');
    barre.setAttribute('aria-label', 'Filtres de la liste');
    // La barre va AU-DESSUS du bloc du tableau : on remonte les enveloppes qui ne contiennent que lui
    // (cadre défilant, carte du tableau), pour la poser hors de la carte, comme dans la maquette.
    let cadre: HTMLElement = table;
    while (
      cadre.parentElement &&
      cadre.parentElement.children.length === 1 &&
      !cadre.parentElement.classList.contains('ks-content') &&
      /(wrap|scroll|overflow|table|card|responsive|panel)/i.test(cadre.parentElement.className)
    ) {
      cadre = cadre.parentElement;
    }
    cadre.parentElement?.insertBefore(barre, cadre);
    table.setAttribute(ATTR, '');
    // Recherche déjà présente dans la page : on n'en ajoute pas une deuxième.
    const conteneur = cadre.parentElement?.closest('.ky-page-shell, .prime-page-shell, .ku-page, section, main') ?? cadre.parentElement;
    const rechercheExistante = !!conteneur?.querySelector('input[type="search"], input[placeholder*="echerch" i], input[placeholder*="Filtrer" i]');
    const etat: EtatTableau = { barre, recherche: '', statut: '', colonne: -1, filtrable: !aCases };
    this.construireBarre(table, etat, !rechercheExistante && !aCases);
    this.etats.set(table, etat);
    return etat;
  }

  private construireBarre(table: HTMLTableElement, etat: EtatTableau, avecRecherche: boolean): void {
    const b = etat.barre;
    b.innerHTML = '';
    const puces = document.createElement('div');
    puces.className = 'ky-tt-puces';
    puces.setAttribute('role', 'group');
    puces.setAttribute('aria-label', 'Filtrer par statut');
    b.appendChild(puces);
    const droite = document.createElement('div');
    droite.className = 'ky-tt-droite';
    if (avecRecherche) {
      const label = document.createElement('label');
      label.className = 'ky-tt-recherche';
      label.innerHTML = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" aria-hidden="true"><circle cx="11" cy="11" r="7"/><path d="m20 20-4-4"/></svg>';
      const input = document.createElement('input');
      input.type = 'search';
      input.placeholder = 'Rechercher dans la liste';
      input.setAttribute('aria-label', 'Rechercher dans la liste');
      input.addEventListener('input', () => {
        etat.recherche = input.value;
        this.appliquerFiltre(table, etat);
      });
      label.appendChild(input);
      droite.appendChild(label);
    }
    const compte = document.createElement('span');
    compte.className = 'ky-tt-compte';
    compte.setAttribute('aria-live', 'polite');
    droite.appendChild(compte);
    b.appendChild(droite);
  }

  private mettreAJour(table: HTMLTableElement, etat: EtatTableau): void {
    etat.colonne = colonneStatut(this.entetes(table));
    const lignes = Array.from(table.tBodies[0]?.rows ?? []);
    // Pastilles de statut : classe ajoutée à la cellule, son texte et son contenu ne changent pas.
    if (etat.colonne >= 0) {
      for (const tr of lignes) {
        const td = tr.cells[etat.colonne];
        if (!td || td.querySelector('button, select, input, a')) continue;
        const ton = tonStatut(td.textContent ?? '');
        if (td.getAttribute('data-ky-ton') !== ton) td.setAttribute('data-ky-ton', ton);
      }
    }
    const valeurs = etat.colonne >= 0 ? lignes.map((tr) => tr.cells[etat.colonne]?.textContent ?? '') : [];
    const filtres = etat.filtrable ? filtresStatut(valeurs) : [];
    if (etat.statut && !filtres.some((f) => f.valeur === etat.statut)) etat.statut = '';
    this.dessinerPuces(table, etat, filtres, lignes.length);
    this.appliquerFiltre(table, etat);
  }

  private dessinerPuces(table: HTMLTableElement, etat: EtatTableau, filtres: FiltreStatut[], total: number): void {
    const puces = etat.barre.querySelector<HTMLElement>('.ky-tt-puces');
    if (!puces) return;
    const cle = JSON.stringify([filtres.map((f) => [f.valeur, f.n]), etat.statut, total]);
    if (puces.getAttribute('data-cle') === cle) return;
    puces.setAttribute('data-cle', cle);
    puces.innerHTML = '';
    if (!filtres.length) return;
    const ajouter = (valeur: string, libelle: string, n: number, ton: string) => {
      const btn = document.createElement('button');
      btn.type = 'button';
      btn.className = 'ky-tt-puce';
      btn.setAttribute('data-ton', ton);
      btn.setAttribute('aria-pressed', String(etat.statut === valeur));
      if (etat.statut === valeur) btn.classList.add('is-on');
      btn.textContent = libelle + ' ';
      const nb = document.createElement('span');
      nb.textContent = String(n);
      btn.appendChild(nb);
      btn.addEventListener('click', () => {
        etat.statut = etat.statut === valeur ? '' : valeur;
        puces.removeAttribute('data-cle');
        this.dessinerPuces(table, etat, filtres, total);
        this.appliquerFiltre(table, etat);
      });
      puces.appendChild(btn);
    };
    ajouter('', 'Tous', total, 'tous');
    for (const f of filtres) ajouter(f.valeur, f.valeur, f.n, f.ton);
  }

  private appliquerFiltre(table: HTMLTableElement, etat: EtatTableau): void {
    const lignes = Array.from(table.tBodies[0]?.rows ?? []);
    let visibles = 0;
    for (const tr of lignes) {
      const ok = !etat.filtrable || ligneVisible(tr.textContent ?? '', etat.recherche, etat.colonne >= 0 ? tr.cells[etat.colonne]?.textContent ?? '' : '', etat.statut);
      // Seules les lignes que NOUS avons masquées sont réaffichées (on ne touche pas aux autres).
      if (!ok && !tr.hidden) { tr.hidden = true; tr.setAttribute('data-ky-masquee', ''); }
      else if (ok && tr.hasAttribute('data-ky-masquee')) { tr.hidden = false; tr.removeAttribute('data-ky-masquee'); }
      if (!tr.hidden) visibles += 1;
    }
    const compte = etat.barre.querySelector<HTMLElement>('.ky-tt-compte');
    if (compte) compte.textContent = libelleLignes(visibles, lignes.length);
  }

  private nettoyer(table: HTMLTableElement, e: EtatTableau): void {
    e.barre.remove();
    table.removeAttribute(ATTR);
    for (const tr of Array.from(table.querySelectorAll('tr[data-ky-masquee]')) as HTMLTableRowElement[]) {
      tr.hidden = false;
      tr.removeAttribute('data-ky-masquee');
    }
    this.etats.delete(table);
  }
}
