# v81 — La charte de la maquette sur toutes les pages

Interface seulement (planning-frontend). **Aucune API, donnée, règle métier, droit, rôle ni table
modifiés ; aucun élément ajouté, retiré ou masqué.** Seul planning-frontend est reconstruit.
S'installe après la v80.

## Principe

L'accueil v80 sert de modèle. Ses éléments sont appliqués à toutes les pages, en clair et en sombre :

| Élément de la maquette | Où il s'applique |
|---|---|
| **Bandeau d'en-tête** (carte claire / sombre, reflets violet et bleu, titre en police d'affichage, sous-titre, actions à droite) | les 68 pages à en-tête commun, les 16 pages à ancien bloc bleu (pointage, présences, réaffectations, planning IT, harmonisation, congés de ma cellule) et 57 pages à en-tête écrit à la main (Prime, Parrainage, cellules, pôles, services, employés, paramètres, notifications, contrats…) |
| **Cartes** : rayon 18 px, ombre douce, bordure du thème | cartes communes (`ky-card`, `card-navy`, `ku-card`, cartes Prime) et cartes propres des pages (demandes de planning, droits des administrateurs, notifications, semaines d'agent…) |
| **Indicateurs** : libellé lisible (plus de majuscules), grand chiffre, teinte rouge / ambre selon l'état, tuile d'icône | indicateurs communs (`ku-kpi`, `ky-kpi-card`, `kpi-card`, `kc-*`), cartes du parrainage, et indicateurs propres de 9 pages (historique et validation des congés, contrats, réclamations, actualités, formation, HTEL, mails internes, import guidé) |
| **Tableaux** : en-tête discret, survol, cadre arrondi | `ky-table`, `prime-table`, `ku-table` |
| **Boutons et champs** : coins à 10 px | boutons et champs communs |

La feuille `kyntus-maquette.css` porte ces règles ; elle est chargée après les feuilles existantes
et avant les garde-fous de contraste (`angular.json`).

**Gardés volontairement** : l'en-tête du planning (statut, effectif, semaine), les barres d'outils
collantes des grilles de saisie Prime, les pages 403 / accès refusé, l'aperçu « papier » de l'export.

## Page par page

### Accueil

| Page | Ce qui change |
|---|---|
| unauthorized | page d’erreur 403 (inchangée) |
| unified-dashboard | accueil v80 (déjà sur la maquette) |

### Administration et audit

| Page | Ce qui change |
|---|---|
| admin-root | bandeau (en-tête commun) |
| audit-root | bandeau (en-tête commun) |

### Aide

| Page | Ce qui change |
|---|---|
| app-guide-page | bandeau (en-tête commun) |

### Assistance

| Page | Ce qui change |
|---|---|
| assistance | bandeau (en-tête commun) |

### Communication

| Page | Ce qui change |
|---|---|
| my-newsletters-page | bandeau (en-tête commun) · cartes / indicateurs |
| newsletter-admin | bandeau (en-tête commun) |

### Congés et absences

| Page | Ce qui change |
|---|---|
| conge-cellule | bandeau (ancien bloc bleu) |
| conge-employe | bandeau (en-tête commun) |
| conge-equipe | bandeau (en-tête commun) |
| conge-historique | bandeau (en-tête commun) · cartes / indicateurs |
| conge-manager | bandeau (en-tête commun) · cartes / indicateurs |
| conge-periodes-interdites | bandeau (en-tête commun) |
| conge-quotas-service | bandeau (en-tête commun) |

### Contrats

| Page | Ce qui change |
|---|---|
| Notification-list | bandeau (en-tête posé à la main) |
| contract-detail | bandeau (en-tête commun) |
| contract-form | bandeau (en-tête commun) |
| contract-list | bandeau (en-tête commun) · cartes / indicateurs |

### Contrôle qualité

| Page | Ce qui change |
|---|---|
| qualite-cq-host | bandeau (en-tête commun) |

### Documentation

| Page | Ce qui change |
|---|---|
| doc-gen-page | bandeau (en-tête commun) |
| hr-generated-history-page | bandeau (en-tête commun) |
| hr-management-page | bandeau (en-tête commun) |
| my-documents-page | bandeau (en-tête commun) |
| settings-page | bandeau (en-tête posé à la main) |
| templates-page | bandeau (en-tête commun) |

### Formation

| Page | Ce qui change |
|---|---|
| formation-admin | bandeau (en-tête commun) · cartes / indicateurs |
| formation-catalog-admin | bandeau (en-tête commun) · cartes / indicateurs |
| formation-catalog-player | bandeau (en-tête commun) |
| formation-dashboard | bandeau (en-tête commun) |
| formation-documents-config | bandeau (en-tête commun) |
| formation-employee | bandeau (en-tête commun) |
| formation-formateur-initial | bandeau (en-tête commun) |
| formation-mes-sessions | bandeau (en-tête commun) |
| formation-path-checklist | bandeau (en-tête commun) |
| formation-quiz-history | bandeau (en-tête commun) |
| formation-rh-plan | bandeau (en-tête commun) |
| formation-rh-prod-queue | bandeau (en-tête commun) |
| formation-session-quiz | bandeau (en-tête commun) |
| formation-take-quiz | bandeau (en-tête commun) |

### Mon compte

| Page | Ce qui change |
|---|---|
| mon-mot-de-passe | formulaire déjà dans une carte |

### Organisation

| Page | Ce qui change |
|---|---|
| organisation-admin | bandeau (en-tête commun) |
| organisation-harmonisation | bandeau (ancien bloc bleu) |

### Organisation (cellules)

| Page | Ce qui change |
|---|---|
| service-detail | bandeau (en-tête posé à la main) |
| service-list | bandeau (en-tête posé à la main) |

### Organisation (pôles)

| Page | Ce qui change |
|---|---|
| floor-list | bandeau (en-tête posé à la main) |

### Organisation (services)

| Page | Ce qui change |
|---|---|
| sub-service-list | bandeau (en-tête posé à la main) |

### Paramètres et notifications

| Page | Ce qui change |
|---|---|
| global-settings | bandeau (en-tête posé à la main) · cartes / indicateurs |
| notifications-center | bandeau (en-tête posé à la main) · cartes / indicateurs |

### Parrainage

| Page | Ce qui change |
|---|---|
| admin-config-page | bandeau (en-tête posé à la main) |
| admin-dashboard-page | bandeau (en-tête posé à la main) |
| admin-tools-page | bandeau (en-tête posé à la main) |
| admin-workflow-page | bandeau (en-tête posé à la main) |
| compta-payments-page | bandeau (en-tête posé à la main) |
| pilote-bonus-page | bandeau (en-tête posé à la main) |
| pilote-dashboard-page | bandeau (en-tête posé à la main) |
| pilote-referrals-page | bandeau (en-tête posé à la main) |
| pilote-submit-page | bandeau (en-tête posé à la main) |
| pm-dashboard-page | bandeau (en-tête posé à la main) |
| pm-performance-page | bandeau (en-tête posé à la main) |
| pm-referrals-page | bandeau (en-tête posé à la main) |
| pm-team-page | bandeau (en-tête posé à la main) |
| rh-dashboard-page | bandeau (en-tête posé à la main) |
| rh-details-page | bandeau (en-tête posé à la main) |
| rh-history-page | bandeau (en-tête posé à la main) |
| rh-management-page | bandeau (en-tête posé à la main) |
| rh-rules-page | bandeau (en-tête posé à la main) |

### Planification

| Page | Ce qui change |
|---|---|
| conge-manager | bandeau (en-tête commun) |
| mes-demandes-changement | bandeau (en-tête commun) · cartes / indicateurs |
| mes-demandes-exceptionnelles | bandeau (en-tête commun) · cartes / indicateurs |
| mes-plannings | bandeau (en-tête commun) · cartes / indicateurs |
| mes-renforts | bandeau (en-tête commun) · cartes / indicateurs |
| new-employee-manager | bandeau (en-tête commun) |
| planning-change-requests | bandeau (en-tête commun) · cartes / indicateurs |
| planning-equipe | bandeau (en-tête commun) · cartes / indicateurs |
| planning-exceptional-requests | bandeau (en-tête commun) · cartes / indicateurs |
| planning-generate | bandeau (en-tête commun) |
| planning-reinforcement-requests | bandeau (en-tête commun) · cartes / indicateurs |
| planning-validation | bandeau (en-tête commun) |
| planning-view | en-tête du planning propre (statut, effectif), gardé |
| shift-config | bandeau (en-tête commun) |

### Planning IT

| Page | Ce qui change |
|---|---|
| planning-it | bandeau (ancien bloc bleu) |

### Pointage

| Page | Ce qui change |
|---|---|
| heure-sortie-acces | bandeau (ancien bloc bleu) |
| pointage-correspondances | bandeau (en-tête commun) |
| pointage-equipe | bandeau (ancien bloc bleu) |
| pointage-heure-sortie | bandeau (ancien bloc bleu) · cartes / indicateurs |
| pointage-prevu-realise | bandeau (ancien bloc bleu) |
| pointage-suivi | bandeau (ancien bloc bleu) |
| regimes-horaires | bandeau (ancien bloc bleu) |

### Prime

| Page | Ce qui change |
|---|---|
| access-denied | page d’accès refusé (inchangée) |
| admin-dashboard | bandeau (en-tête posé à la main) |
| allowances-allocation-page | bandeau (en-tête posé à la main) |
| allowances-history-page | bandeau (en-tête posé à la main) |
| allowances-manager-dashboard-page | bandeau (en-tête posé à la main) |
| allowances-page-shell | bandeau (en-tête posé à la main) |
| allowances-progress-page | bandeau (en-tête posé à la main) |
| business-departments-page | bandeau (en-tête commun) |
| chef-projet-scope-page | bandeau (en-tête posé à la main) |
| employee-dashboard-page | bandeau (en-tête posé à la main) |
| my-performance-page | bandeau (en-tête posé à la main) |
| my-primes-page | bandeau (en-tête posé à la main) |
| organisation-management | bandeau (en-tête commun) |
| prime-cellule-indicators-page | barre d’outils compacte (grille de saisie), gardée |
| prime-configuration-page | bandeau (en-tête posé à la main) |
| prime-dashboard-standard | bandeau (en-tête posé à la main) |
| prime-fiche-import | bandeau (en-tête posé à la main) |
| prime-fiches-communes-list | en-tête collant de saisie, gardé |
| prime-fiches-pilotes-page | bandeau (en-tête posé à la main) |
| prime-global-pool-page | bandeau (en-tête posé à la main) |
| prime-history-page | bandeau (en-tête posé à la main) |
| prime-results-page | bandeau (en-tête posé à la main) |
| prime-rules-page | bandeau (en-tête posé à la main) |
| prime-saisie | en-tête collant de saisie, gardé |
| prime-saisie-cellule-page | bandeau (en-tête posé à la main) |
| prime-supervisor-dashboard | bandeau (en-tête posé à la main) |
| prime-synthesis-tracking-page | bandeau (en-tête posé à la main) |
| prime-types-page | bandeau (en-tête posé à la main) |
| prime-validation-history-page | bandeau (en-tête posé à la main) |
| prime-validation-page | bandeau (en-tête posé à la main) |
| referent-technique-scope-page | bandeau (en-tête posé à la main) |
| settings-module | bandeau (en-tête posé à la main) |
| superviseur-scope-page | bandeau (en-tête posé à la main) |
| team-performance-page | bandeau (en-tête posé à la main) |
| template-manager | bandeau (en-tête posé à la main) |

### Process Bytel

| Page | Ce qui change |
|---|---|
| process-bytel-page | bandeau (en-tête commun) |

### Présences et paie

| Page | Ce qui change |
|---|---|
| presences-avertissements | bandeau (ancien bloc bleu) |
| presences-dossiers | bandeau (ancien bloc bleu) |
| presences-evp | bandeau (ancien bloc bleu) |
| presences-export | bandeau (ancien bloc bleu) |
| presences-retards | bandeau (ancien bloc bleu) |
| presences-tableau | bandeau (ancien bloc bleu) |

### Qualité (réclamations)

| Page | Ce qui change |
|---|---|
| reclamation-admin | bandeau (en-tête commun) |
| reclamation-employee | bandeau (en-tête commun) |

### RH

| Page | Ce qui change |
|---|---|
| pilotage-rh | bandeau (en-tête commun) |
| reaffectations | bandeau (ancien bloc bleu) |

### Ressources humaines (employés)

| Page | Ce qui change |
|---|---|
| employee-fields-page | bandeau (en-tête commun) |
| employee-import-guided | bandeau (en-tête commun) · cartes / indicateurs |
| fiches-reference-page | bandeau (en-tête commun) |
| htel-liaisons-page | bandeau (en-tête commun) · cartes / indicateurs |
| mails-internes-page | bandeau (en-tête commun) · cartes / indicateurs |
| my-team | bandeau (en-tête commun) |
| user-detail | bandeau (en-tête commun) |
| user-form | bandeau (en-tête commun) |
| user-import | bandeau (en-tête posé à la main) |
| user-list | bandeau (en-tête posé à la main) · cartes / indicateurs |

## Vérification

- Rendu des briques communes (bandeaux, indicateurs, cartes, tableaux, puces) contrôlé en clair et
  en sombre avec les vraies feuilles de style.
- Les 57 en-têtes posés à la main : seule la classe `ky-entete` est ajoutée (vérifié ligne à ligne).
- Toutes les feuilles modifiées sont bien formées ; le correctif s'applique sans décalage sur main + v80.
- **À faire sur l'environnement de test** : construction (`installer_v81.sh test`), puis le parcours
  affiché en fin d'installation (points 19 à 21), en clair et en sombre, ordinateur et mobile.

## Retour arrière

Correctif retiré (`patch -R`) et image précédente de planning-frontend : voir la fin du script.
