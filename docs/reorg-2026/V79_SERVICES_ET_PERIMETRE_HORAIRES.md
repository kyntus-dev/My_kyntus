# v79 — Réaffectation par service, et « Configuration des horaires » limitée au périmètre

## 1. Les nouveaux services n'apparaissaient pas dans la réaffectation

Dans Organisation → Affectation des agents, la cellule BtB a été découpée en trois services
(Back office, Pré-appel, Support technicien). Mais l'écran :

- ne montrait que les cellules, jamais leurs services ni le service de chaque agent ;
- ne proposait le choix du service d'arrivée qu'après avoir choisi une cellule, et seulement si
  elle en avait plusieurs ;
- **refusait tout déplacement vers la cellule actuelle** (« La cellule d'arrivée est la cellule
  actuelle »). Il était donc impossible de répartir les 16 agents de BtB entre ses services.

**Corrigé :**

- sous la cellule choisie, ses services s'affichent avec leurs effectifs (et « Sans service » pour
  les agents pas encore répartis) ; un clic filtre la liste ;
- colonne « Service » dans le tableau des membres ;
- liste « Service d'arrivée » toujours visible (choisie d'office si la cellule n'en a qu'un) ;
- le déplacement vers un autre service de la même cellule est permis ; seuls les agents déjà dans le
  service d'arrivée sont écartés, et la confirmation le précise.

## 2. « Configuration des horaires » montrait toute l'organisation

Pour décider qui voit tout, la page demandait le droit « Configuration des horaires » au périmètre
Global. Mais côté interface, ce test répond oui dès que le droit existe, quel que soit son périmètre :
un référent technique ou un chef de projet limité à sa cellule voyait toutes les cellules.

**Corrigé :** voient toute l'organisation l'Admin, les RH, ceux à qui « Voir les périmètres des
autres encadrants » est accordé (Accès par rôle ou par compte), et ceux qui ont « Configuration des
horaires » accordé explicitement au niveau « Toute l'entreprise ». Tous les autres ne voient que leur
périmètre. Le serveur applique la même règle, et la page attend désormais d'avoir lu les droits avant
de choisir ce qu'elle affiche. « Périmètre superviseur » utilise la même règle.

## Vérification

Construction de production OK ; 8 nouveaux tests (services de la réaffectation, périmètre global) ;
les 5 échecs déjà présents avant sont inchangés.
