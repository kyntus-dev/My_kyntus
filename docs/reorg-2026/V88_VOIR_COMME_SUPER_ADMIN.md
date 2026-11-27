# V88 — « Voir comme » (Super Admin)

Base : `main` + v80 → v87. **Interface seule** (`planning-frontend`) : aucune API, table, règle, droit ni session modifiés.

## Ce qui change

Sur l'accueil du **Super Admin uniquement**, un bouton **« Voir comme… »** sous la recherche ouvre un panneau :

- choix du rôle (rôles du catalogue + rôles personnalisés, comme dans « Accès par rôle ») ;
- **Accueil** du rôle : titre et sous-titre de son bandeau ;
- **Indicateurs** qu'il voit (mêmes règles que l'accueil réel : indicateurs du profil, limités aux modules de son menu) ;
- **Raccourcis par défaut** (première page de chaque module) ;
- **Menu** : modules et pages, dépliables (même calcul que l'aperçu de « Accès par rôle »).

## Garanties

- Lecture seule : mêmes appels de lecture que « Accès par rôle » (liste des droits par rôle, liste des rôles),
  déjà ouverts au Super Admin. Aucune écriture.
- La session reste celle du Super Admin : on ne se connecte pas « en tant que », aucune donnée d'un autre compte
  n'est affichée (aperçu sans chiffres).
- Le bouton n'existe pas pour les autres rôles.
- Les droits accordés compte par compte (« Accès par compte ») ne sont pas inclus : le panneau le signale.

## Fichiers

- `features/dashboard/pages/unified-dashboard/voir-comme.util.ts` (+ spec, 2 tests), `voir-comme.component.ts`,
  accueil (`unified-dashboard.component.{ts,html,css}`).

## Tests sur l'environnement de test

1. Super Admin : « Voir comme… » → Superviseur, RH, Pilote, Comptabilité… : titre, indicateurs, menu cohérents avec
   « Accès par rôle » (même rôle, même liste de pages).
2. Admin, RH, Superviseur, Pilote : **pas** de bouton « Voir comme ».
3. Clair / sombre ; téléphone (panneau plein écran).
