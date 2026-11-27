# V90 — Raccourcis juste sous « Vos indicateurs »

Base : `main` + v80 → v89. Interface seule (accueil) : aucune API, règle, droit ni donnée modifiés.

## Ce qui change (tous les rôles)

- **Raccourcis** remontent **juste sous « Vos indicateurs »**, présentés de la même façon (titre à gauche,
  « Personnaliser » discret à droite, pas de cadre en plus) : une rangée compacte de tuiles horizontales
  (icône, page, module), 58 px de haut. Libellé complet au survol.
- Téléphone : une seule rangée qui défile horizontalement au lieu d'une pile de tuiles.
- En bas : **Activité récente** et **Ma semaine** côte à côte (Ma semaine seulement pour les comptes avec un planning).
- Personnalisation inchangée (ajouter, retirer, « Par défaut »), mêmes raccourcis par rôle.

## Ordre de l'accueil

1. Bandeau (salutation, focus du jour)  2. Vos indicateurs  3. **Raccourcis**  4. Alertes (s'il y en a)
5. Actions en attente | Ma journée + Santé des modules  6. Activité récente | Ma semaine

## Tests

Chaque rôle, clair / sombre, ordinateur / téléphone : raccourcis sous les indicateurs ; « Personnaliser » → ajout
et retrait fonctionnent ; un clic ouvre la page.
