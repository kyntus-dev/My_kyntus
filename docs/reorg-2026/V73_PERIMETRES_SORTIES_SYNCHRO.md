# v73 — Périmètres : agents sortis grisés, réaffectations suivies, synchro honnête

Trois corrections liées, toutes sur la même cause de fond : les modules Prime et Planning gardent une
copie de l'annuaire, et cette copie n'apprenait jamais qu'un agent était parti ou avait changé de
service. Aucune donnée n'est supprimée.

## 1. Un agent désactivé ou sorti restait dans le périmètre

Quand l'annuaire signalait un départ, le message arrivait bien dans le module Prime… et était jeté :

```csharp
if (msg.IsDeleted) return;   // avant la v73 : rien n'était fait
```

Pire, la fiche employé de Prime n'avait **aucun champ actif/inactif** : ni l'API ni l'interface ne
pouvaient distinguer un agent présent d'un agent sorti. Le filtre du périmètre ne regardait que la
cellule et le service. L'autre moitié du même écran, elle, filtrait déjà les comptes inactifs — les
deux ne se comportaient donc pas pareil.

**Corrigé :**

- colonne `IsActive` sur `prime_employee` (ajoutée au démarrage, `DEFAULT TRUE` : tous les agents déjà
  enregistrés restent présents, aucun n'est effacé ni masqué rétroactivement) ;
- le départ est désormais traité : la fiche est **conservée** pour l'historique des primes et marquée
  sortie ; le retour d'un agent le réactive ;
- `/api/prime/employees` renvoie l'état ;
- périmètres superviseur et référent technique : la ligne s'affiche **grisée avec une pastille
  « Sorti »**, sans bouton d'action, et le compteur indique « dont N sorti(s), hors effectif ».

## 2. Un agent réaffecté restait dans son ancien périmètre

Deux raisons distinctes :

- **Encadrement.** Une règle gelait le rattachement : dès qu'un Superviseur, Référent technique ou
  Chef de projet avait un pôle, une cellule ou un service, la synchro gardait l'ancien et ignorait
  l'annuaire. Réaffecté, il fallait le déplacer **une seconde fois à la main** dans Administration →
  Organisation, sans quoi il restait affiché dans son ancien périmètre. Le gel est retiré : le
  rattachement suit l'annuaire pour tout le monde. La protection contre l'effacement reste en place
  (si l'annuaire n'envoie aucun rattachement, l'actuel est conservé), et **les cellules qu'un
  superviseur encadre ne sont pas touchées** : elles sont stockées séparément.
- **Copie du Planning en retard.** L'écran complète sa liste avec les comptes du Planning rattachés au
  sous-service. Tant que la synchro n'était pas passée, un agent réaffecté y gardait son ancien
  sous-service et se faisait **réinjecter** dans l'ancien périmètre. Désormais, cette complétion
  n'ajoute que les personnes que l'Organisation ne connaît pas encore ; celles qu'elle place ailleurs
  sont ignorées.

## 3. « Réconciliation : alignée » pouvait être faux

La synchro ne republiait **que les agents actifs** : les sortants n'étaient jamais renvoyés, donc
Prime et Planning n'apprenaient jamais un départ. C'est la cause directe du point 1 en production.
Elle republie maintenant aussi les départs.

Par ailleurs, la vérification interroge Planning et Prime en HTTP. Si un service ne répondait pas, les
compteurs revenaient vides et le test les comptait comme « pas d'anomalie » : l'écran annonçait
**« Réconciliation : alignée » en vert alors que rien n'avait pu être vérifié**. Il affiche désormais
« non vérifiée — Planning et/ou Prime injoignable(s) » en orange, et nomme le service en cause.

## Vérification

- `npx ng build --configuration production` : OK.
- `vitest` : 4 nouveaux tests de périmètre (sortis conservés et marqués, réaffectation visible dans le
  seul nouveau service, réponse serveur antérieure à la v73 traitée comme présente). Les 5 échecs déjà
  présents avant la v73 sont inchangés.
- `SortiesEtReaffectationsV73Tests` (6 cas) couvre côté serveur le marquage de sortie, la réactivation,
  le superviseur réaffecté et la conservation du rattachement quand l'annuaire n'envoie rien. Ces tests
  n'ont pas pu être exécutés dans l'environnement de préparation (accès NuGet fermé) ; ils ne sont pas
  compilés par la construction de l'image, qui ne construit que `Prime.API`.

## Après l'installation

1. Administration → **Synchro organisation** → « Lancer la réconciliation ». Les départs sont alors
   poussés vers Prime et Planning pour la première fois : les agents sortis passent en grisé.
2. Si le bandeau affiche « non vérifiée », c'est que l'annuaire ne joint pas Planning ou Prime —
   vérifiez que les conteneurs tournent, puis relancez.
3. Les quotas et l'écart d'effectif (« Organisation : 43 · Planning : 40 ») se règlent à part, dans
   Configuration des horaires → Répartir.
