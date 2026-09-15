# Création et rapprochement RH depuis HTEL

HTEL est la référence pour l’identifiant technicien, le nom complet, le code HTEL et le statut externe. Une fiche MyKyntus complète et son compte de connexion sont créés seulement lorsque RH termine le formulaire existant.

## Utilisation par RH / Admin

1. Ouvrir **Employés** : l’encart **Collaborateurs HTEL** compte les nouveaux actifs à compléter, les correspondances à vérifier, les fiches absentes ou sans correspondance et les statuts inactifs HTEL à examiner.
2. Cliquer **Ouvrir le suivi HTEL** (ou le menu existant **Liaisons HTEL**, `/users/htel-liaisons`). La comparaison est effectuée à l’ouverture, puis toutes les cinq minutes tant que la page reste visible. **Actualiser HTEL** permet une vérification immédiate. Il ne s’agit pas d’un abonnement webhook ni d’une notification envoyée lorsque le navigateur est fermé : les nouveaux collaborateurs seront détectés à la prochaine ouverture.
3. Pour un nouveau technicien actif, cliquer **Préremplir la fiche RH**. Son identifiant est transmis au formulaire, puis les données sont relues depuis le serveur. On peut également choisir le technicien dans **Nouvel employé → Collaborateur à créer depuis HTEL**.
4. Vérifier le nom complet HTEL affiché, ajuster la séparation **Nom / Prénom** et cocher la confirmation. Les noms composés et prénoms multiples restent modifiables. HTEL ne fournit pas deux champs distincts permettant de garantir cette séparation automatiquement.
5. Compléter l’email de connexion, l’email personnel, les dates, l’organisation, le rôle et les autres champs exigés par le parcours RH actuel, puis terminer la création. Le code HTEL aide à identifier le service, mais n’affecte pas automatiquement l’organisation MyKyntus.
6. Pour une fiche existante sans identifiant HTEL, examiner les **Correspondances à vérifier**, y compris lorsqu’il n’y a qu’un seul candidat. Confirmer la liaison de la bonne personne. Une homonymie ou une ressemblance n’est jamais une preuve suffisante pour fusionner des comptes automatiquement.
7. Pour une fiche déjà liée, **Actualiser les champs HTEL** actualise le code renseigné et le libellé HTEL. **Vérifier l’identité** propose séparément le nom et le prénom à confirmer. La confirmation publie les changements dans les projections existantes via l’outbox Directory.
8. Examiner les fiches **Absentes de HTEL ou sans correspondance fiable**. Un identifiant auparavant lié mais absent est distingué d’un compte sans lien ni correspondance probable. **Examiner la fiche** ouvre les actions RH existantes. La désactivation et la suppression restent des décisions explicites sur la fiche. Un compte actif dans MyKyntus et inactif dans HTEL est signalé dans la liste des liaisons.

Les comptes désactivés participent au rapprochement et continuent de réserver leur identifiant HTEL. Si un technicien est déjà lié, ouvrir sa fiche au lieu d’en recréer une. Si plusieurs personnes partagent un nom, RH peut confirmer **Il s’agit d’une autre personne** après examen ; l’unicité de l’identifiant reste contrôlée côté serveur et en base.

## Origine des champs

| Donnée HTEL | Traitement MyKyntus |
| --- | --- |
| `id_technicien` | Clé de liaison stable, unique pour les fiches actives et désactivées. |
| `technicien` | Libellé source conservé ; proposition nom/prénom à confirmer par RH. |
| `code` | Code HTEL affiché et conservé ; un code source vide n’efface pas un code déjà présent. |
| `actif` | Affiché pour la comparaison. Seuls les nouveaux techniciens actifs peuvent démarrer la création depuis HTEL. Aucun compte existant n’est activé, désactivé ou supprimé automatiquement. |
| Email, téléphone, CIN, contrat, dates, rôle, organisation | Complétés et validés par le parcours RH existant ; ces données ne sont pas inventées à partir du nom HTEL. |

## Prérequis et mise à jour locale

- L’API configurée dans Employee Directory (`Htel:TechniciensUrl` ou `Htel:BaseUrl` + `Htel:TechniciensPath`) doit renvoyer **le tableau complet**, avec les actifs et les inactifs. Une URL filtrée ou une seule page de résultats ne permet pas de conclure qu’une personne est absente. Le format fourni ne contient pas de total permettant de détecter une réponse partielle pourtant valide.
- Une réponse vide, mal formée, contenant des identifiants en double, des identifiants invalides ou des statuts manquants suspend la comparaison. Une panne HTTP ou un timeout est affiché comme une erreur ; il ne produit pas une liste de comptes à désactiver.
- `Directory:WriteMaster` doit être activé pour créer depuis HTEL (valeur par défaut actuelle : `true`). Le lien HTEL est vérifié et enregistré dans la création Directory initiale, avant la création du compte Planning/Auth. Il n’est plus tenté comme une opération tardive après création.
- La contrainte existante `IX_employees_IdTechnicien` doit être présente. Aucune nouvelle table ou migration destructive n’est ajoutée.
- Déployer ensemble **employee-directory-backend**, **planning-backend** et **planning-frontend**. RabbitMQ et les consommateurs de projections existants doivent fonctionner pour propager les changements d’identité.
- Conserver vos modifications locales et intégrer ce changement dans votre branche de travail (fusion ou cherry-pick du commit). Éviter de remplacer le projet complet si vous avez des correctifs locaux non publiés.

Depuis la racine du projet, après intégration :

```powershell
docker compose up -d --build employee-directory-backend planning-backend planning-frontend
docker compose logs --since 10m --tail 200 employee-directory-backend
```

Le profil de recette `My_Kyntus_Mode_Local` neutralise volontairement HTEL et le réseau sortant. Utiliser un double d’API dans cette recette, ou une configuration de développement autorisée pouvant joindre HTEL. Ne pas supprimer les protections du profil pour masquer une erreur de connexion.

## Recette à exécuter avec vos données de test

| Cas | Résultat attendu |
| --- | --- |
| Nouveau technicien actif, sans fiche ni nom approchant | Apparaît comme nouveau ; préremplissage ID, nom complet et code ; RH complète puis enregistre. |
| Même technicien déjà lié, même sur une fiche désactivée | N’apparaît pas comme nouveau ; deuxième liaison/création refusée. |
| Nom/prénom inversés ou nom composé | Proposition vérifiable ; modification seulement après confirmation RH. |
| Une ou plusieurs correspondances par nom | Candidats visibles ; aucune liaison automatique ; cas d’homonymie examinable. |
| Fiche sans lien ressemblant à une fiche déjà liée | Signalée pour examen d’un éventuel doublon, sans nouvelle liaison. |
| Code ou libellé HTEL modifié | Actualisation des champs HTEL de la fiche liée ; deuxième actualisation sans changement supplémentaire. |
| Compte MyKyntus actif, `actif=0` dans HTEL | Écart visible ; compte conservé actif jusqu’à la décision RH. |
| Identifiant lié absent du catalogue complet | Signalé « identifiant HTEL absent » ; compte conservé. |
| API indisponible, vide, invalide ou ID externe dupliqué | Comparaison suspendue ; aucune absence déduite et aucune synchronisation partielle. |
| HTEL devient indisponible ou le technicien est lié ailleurs entre le préremplissage et l’enregistrement | Création refusée avec erreur ; pas de nouveau compte créé pour cette sélection. |
| Deux créations concurrentes avec le même identifiant | La contrainte unique empêche la seconde fiche. |
| Changement de collaborateur dans le formulaire | Brouillons séparés par identifiant HTEL ; les autres champs sont réinitialisés ou restaurés depuis le brouillon de cette personne. |

## Vérifications techniques

```powershell
dotnet build EmployeeDirectory/EmployeeDirectory.API/EmployeeDirectory.API.csproj
dotnet build PlanningService/Planning.API/Planning.API.csproj
dotnet test EmployeeDirectory/EmployeeDirectory.UnitTests/EmployeeDirectory.UnitTests.csproj --filter "FullyQualifiedName~Htel"
dotnet test PlanningService/Planning.UnitTests/Planning.UnitTests.csproj --filter "FullyQualifiedName~DirectoryEmployeeWriteClientHtel"
cd projet_kyntus_service_planning-frontend
npm ci
npx ngc -p tsconfig.app.json
npx ng test --watch=false --ts-config=tsconfig.htel-spec.json --include="**/htel-*.spec.ts"
```

Le fichier de configuration de tests HTEL isole cette vérification des erreurs de typage déjà présentes dans d’autres tests du projet ; il inclut aussi les tests d’export planning HTEL existants.
