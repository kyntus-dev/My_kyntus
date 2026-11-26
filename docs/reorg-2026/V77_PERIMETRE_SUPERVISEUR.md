# v77 — Périmètre superviseur : chacun ne voit que ses cellules

## Pourquoi une v77

Cette correction faisait partie d'une seconde version de la v73, envoyée après coup. En production,
c'est la **première** v73 qui a été posée : l'installateur a ensuite vu « v73 déjà là » et n'a rien
ajouté. Résultat : la liste « Cellule active » proposait toujours toutes les cellules, et la rubrique
« Voir les périmètres des autres encadrants » n'existait pas (aucune ligne en base). La v77 apporte
uniquement ce morceau manquant.

## La cause

Pour décider d'ouvrir toute l'entreprise, la page demandait « Valider les plannings » ou
« Configuration des horaires » **au périmètre Global**. Mais la recherche d'un droit ne tient pas
compte du périmètre demandé : elle répond oui dès que le droit existe. Un superviseur avec son droit
normal, limité à sa cellule, obtenait donc la liste de toutes les cellules.

## Correction

- Nouvelle rubrique dans « Accès par rôle » (module Administration) : **« Voir les périmètres des
  autres encadrants »**, accordée par défaut à l'Admin et aux RH seulement.
- Par défaut, un superviseur ou un référent technique ne voit que ses propres cellules.
- Pour qu'un encadrant voie celles des autres : accorder la rubrique à son rôle dans « Accès par
  rôle », ou à une seule personne dans « Accès par compte ».
- La rubrique est inscrite parmi les rubriques ajoutées : sa ligne RH est créée automatiquement au
  démarrage de l'annuaire, sans rien réensemencer d'autre.

Note : l'Admin et les RH voient toujours toutes les cellules. Pour vérifier la correction, il faut
se connecter avec un compte **Superviseur**.

## Vérification

- Construction de production OK ; test du catalogue (rubrique réservée à l'Admin et aux RH) ;
  test serveur `Le_perimetre_global_est_reserve_a_l_admin_et_aux_rh` rejoué : OK.
