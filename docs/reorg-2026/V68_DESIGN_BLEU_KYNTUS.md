# v68 — Design « Combinaison A + E » et bleu du logo Kyntus

À poser après la v67 (base déployée : v64.2 + v65 + v66 + v67). Frontend uniquement : aucune API, aucune
migration, aucune donnée modifiée.

## 1. Bleu Kyntus (couleur du logo)

- Couleur de référence extraite du logo (`public/assets/brand/logo-mode-claire.png`) : **#0D2E5B**.
- Gamme complète déclarée une seule fois dans `src/styles.css` (`--ky-brand-50` … `--ky-brand-950`) et
  dans Tailwind (`brand-*` et `blue-*`), pour que les anciens écrans en classes `blue-*` suivent aussi :

  | 50 | 100 | 200 | 300 | 400 | 500 | 600 | 700 | **800** | 900 | 950 |
  |----|-----|-----|-----|-----|-----|-----|-----|---------|-----|-----|
  | #eef3fa | #dce6f4 | #b9cde8 | #9db9e2 | #6f95cf | #3a68ab | #22508f | #163f76 | **#0d2e5b** | #0a2347 | #061630 |

- Jetons sémantiques utilisés par les écrans : `--ky-blue` (fonds, boutons), `--ky-blue-strong` (survol),
  `--ky-blue-text` (texte bleu lisible), `--ky-blue-tint` (fonds légers), `--info`.
- Thème sombre : `--ky-blue` passe à #3a68ab et le texte bleu à #9db9e2, pour rester lisible sur fond
  foncé (contraste au moins égal à l'ancien bleu #1e4fd8).
- Boutons principaux (`.btn-primary`, `.ky-btn-primary`, `.ku-btn--primary`) : bleu plein du logo, sans
  dégradé. Bandeaux de page (`.ku-hero`) : bleu du logo plein, en thème clair comme en sombre.
- Couleurs codées en dur remplacées par les jetons dans 19 fichiers (composants partagés, planning,
  congés, mails internes, audit, export des présences, menu). L'export PDF du planning utilise #0D2E5B.

## 2. Menu latéral

- Une icône dédiée par module (19 icônes, toutes différentes), dans une tuile arrondie.
- Modules regroupés en rubriques repliables : **Pilotage**, **Collaborateurs**, **Temps et paie**,
  **Qualité et ressources**, **Aide**. Un module inconnu (ajouté plus tard) va dans « Autres » : rien ne
  disparaît du menu. L'état replié/déplié est mémorisé par navigateur.
- Module de la page ouverte : tuile bleu plein. Module déplié : tuile bleu clair. Compteurs en pastille.
- Menu réduit : tuiles de 36 px, compteurs en point rouge, séparateurs entre rubriques.
- Les droits ne changent pas : le menu affiche exactement les mêmes entrées qu'avant (même service
  `NavigationMenuService`), seule la présentation change.

## 3. Accueil

- Bandeau : date du jour, rôle, « Bonjour, … », résumé des actions en attente.
- **Recherche instantanée** des pages du menu (Entrée ouvre le premier résultat, Échap efface), avec
  suggestions (congés, planning, paie, employé…). Elle ne cherche que dans les pages auxquelles la
  personne a accès.
- Raccourcis, indicateurs (cliquables quand ils mènent à une page), file d'actions filtrable par module,
  widget « pointage du jour », alertes, santé des modules, « Ma semaine », activité récente.
- Les chiffres viennent des mêmes services qu'avant (`GlobalDashboardService`) : aucun nouvel appel API.

## 4. Points de vigilance traités

- Les garde-fous de contraste existants (`kyntus-theme-contrast.css`) forçaient un fond blanc sur les
  `<header>` : le bandeau bleu est posé en `background-image` pour y résister (titre lisible sur
  Planning IT, Présences, Pointage, Congés…).
- La règle globale `:root nav button svg` colorait les icônes du menu : elles suivent maintenant la
  couleur de leur tuile (icône blanche sur la tuile du module courant).
- Champ de recherche de l'accueil protégé contre les styles forcés des champs.

## 5. Vérifications

- `npx ng build --configuration production` : OK.
- `npx vitest run` : 14 nouveaux tests v68 (`src/app/core/v68-design.spec.ts`) verts ; aucun nouvel échec
  (les 5 échecs connus de `kyntus-return-url.util.spec` et `prime-view-loaders.spec` sont antérieurs).
- Aperçus contrôlés en thème clair et sombre : accueil, recherche, menu, Planning IT, Présences, Suivi des
  congés, Utilisateurs.

## 6. Après installation

1. Vider le cache du navigateur (Ctrl+F5) : les feuilles de style changent.
2. Vérifier : menu (rubriques, icônes, module courant en bleu), accueil (recherche, raccourcis), un
   bandeau de page (Planning IT), un bouton principal, puis le thème sombre.
3. Retour arrière : l'installateur affiche à la fin les trois commandes exactes (retrait du correctif,
   retour aux images sauvegardées `reorg-avant-v68-…`, redémarrage de planning-frontend).

Installation : copier `reorg-2026-code-v68.patch` (et ceux des versions non posées) dans `/tmp`, puis
`bash installer_v68.sh test`, contrôler, puis `bash installer_v68.sh prod`. Si seule la v68 manque,
seul `planning-frontend` est reconstruit.
