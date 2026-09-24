# Première connexion obligatoire — déploiement

Cette modification vise les pilotes (`Employee` dans Auth, alias Planning `Pilote`) et les `Qualiticien`. Elle est désactivée par défaut. Aucun compte existant n'est réinitialisé par la migration ou au redémarrage.

## Comportement

- Compte explicitement activé : son hash devient celui du mot de passe temporaire commun, avec expiration (72 h par défaut, réglable de 1 à 168 h).
- Connexion correcte : HTTP 403 `PASSWORD_CHANGE_REQUIRED`, sans JWT ni refresh token. Le formulaire exige le changement. L'autorisation opaque dure au plus 10 minutes, reste en mémoire navigateur et n'est conservée en base que sous forme SHA-256.
- Enregistrement : nouveau mot de passe de 12 à 64 caractères conforme à la politique existante, confirmation identique, différent du temporaire. Une mise à jour SQL atomique remplace le hash, consomme l'autorisation, retire l'obligation et révoque les anciens jetons. Reconnexion avec le mot de passe personnel.
- Le mot de passe commun n'est jamais une alternative globale au mot de passe personnel. Après changement il est refusé pour ce compte. Un réimport Planning ne remplace plus les mots de passe existants. Le seed démo ne remplace pas les mots de passe personnalisés ou en attente.
- Les comptes déjà personnalisés avec ce dispositif ne peuvent pas être réactivés par `first-login/enroll`.
- Les anciens comptes n'ont pas d'historique fiable de personnalisation : sélectionner uniquement les personnes réellement concernées. L'activation explicite remplace leur ancien mot de passe et révoque leurs sessions.

## 1. Préparer et sauvegarder

Faire d'abord toute la procédure sur une copie de préproduction. Consulter `FIRST_LOGIN_FILES.md` pour la liste exhaustive des fichiers. Intégrer le commit complet, avec la bibliothèque partagée : copier seulement le contrôleur Auth ne suffit pas.

La base de référence est `a0c0f0b988b50e02ac9c485b306b717c19400932`. Vérifier que les migrations Auth jusqu'à `20260726180000_AddUserLockoutFields` sont présentes. Si votre production est plus ancienne, traiter les migrations intermédiaires en préproduction avant cette mise à jour. Ne pas exécuter de SQL de suppression/recréation des utilisateurs.

Depuis la racine du dépôt, sauvegarder Auth avec votre utilisateur PostgreSQL autorisé (exemple installation par défaut) :

```bash
umask 077
docker compose exec -T postgres pg_dump -U postgres -Fc auth_db > auth-before-first-login.dump
docker compose exec -T postgres psql -U postgres -d auth_db -c 'SELECT * FROM "__EFMigrationsHistory" ORDER BY "MigrationId";'
```

Vérifier la sauvegarde et sa restauration sur une base distincte. Conserver également les images précédentes et la configuration. Utiliser HTTPS pour les utilisateurs et l'outil d'activation. Ne pas publier le mot de passe commun dans Git, les tickets ou les logs : quiconque connaît ce secret et l'email peut tenter d'activer un compte encore en attente.

## 2. Déployer d'abord avec activation désactivée

Ajouter dans le `.env` existant, sans remplacer ses autres variables :

```dotenv
KYNTUS_FIRST_LOGIN_ENABLED=false
KYNTUS_FIRST_LOGIN_COMMON_PASSWORD=
KYNTUS_FIRST_LOGIN_TEMPORARY_HOURS=72
```

Désactiver le seed démo en production (`KYNTUS_DEMO_SEED_ENABLED=false`). Conserver les clés JWT actuelles. Reconstruire les services qui utilisent la bibliothèque JWT :

```bash
docker compose config --quiet
docker compose build auth-backend auth-frontend planning-backend formation-backend conge-backend documentation-backend prime-backend employee-directory-backend parrainage-backend kcq-backend
```

Prévoir une fenêtre de maintenance, puis démarrer Auth avant les autres services :

```bash
docker compose up -d --no-deps auth-backend
docker compose logs --tail=100 auth-backend
```

Auth applique automatiquement `20260924090000_AddMandatoryFirstLogin` avant d'accepter les connexions. La migration ajoute six colonnes et un index ; elle ne modifie aucun hash existant. Si la migration échoue, le service refuse de démarrer. Contrôler `/health`, l'absence d'erreurs et l'historique de migration :

```bash
docker compose exec -T postgres psql -U postgres -d auth_db -c 'SELECT * FROM "__EFMigrationsHistory" ORDER BY "MigrationId";'
docker compose up -d --no-deps auth-frontend planning-backend formation-backend conge-backend documentation-backend prime-backend employee-directory-backend parrainage-backend kcq-backend
```

Les backends métier doivent **tous** avoir `SessionValidation__BaseUrl=http://auth-backend:8080/` ; KCQ doit avoir `SESSION_VALIDATION_BASE_URL=http://auth-backend:8080`. Le Compose fourni les configure. Pour un déploiement hors Compose, les configurer explicitement avant toute activation de compte. Cette vérification ajoute une requête interne Auth par requête métier authentifiée ; surveiller charge/latence et disponibilité Auth. Une indisponibilité Auth bloque ces requêtes (échec fermé).

Les connexions SignalR/Socket.IO déjà ouvertes sont revérifiées toutes les 15 secondes, avec délai réseau maximal de 5 secondes. Une connexion existante peut donc rester ouverte jusqu'à environ 20 secondes après révocation. Les nouvelles requêtes HTTP sont vérifiées immédiatement. Tester les hubs en préproduction et, pour un basculement sans session persistante antérieure, redémarrer les services concernés pendant la maintenance.

Vérifier qu'un compte RH/Admin et un compte existant non activé se connectent normalement et accèdent aux services métier. Vérifier que la gateway transmet les nouveaux chemins `/api/Auth/complete-first-login`, `/api/Auth/first-login/enroll` et `/api/Auth/session/validate` sans réécriture inattendue.

## 3. Activer un premier groupe

Choisir un secret temporaire robuste de 12 à 64 caractères, le placer uniquement dans le `.env` du serveur (utiliser les quotes simples dotenv si besoin), puis définir `KYNTUS_FIRST_LOGIN_ENABLED=true`. Recréer Auth :

```bash
docker compose up -d --no-deps auth-backend
```

L'activation rend automatique le parcours pour les **nouveaux** comptes éligibles créés via Planning. Elle ne touche pas les comptes existants. Pour ceux-ci, relever leurs IDs **Auth entiers**, pas leurs GUID Planning/Directory :

```sql
SELECT u."Id", u."Email", r."Name", u."IsActive", u."MustChangePassword", u."PasswordChangedAt"
FROM "Users" u JOIN "Roles" r ON r."Id" = u."RoleId"
WHERE r."Name" IN ('Employee', 'Pilote', 'Qualiticien')
ORDER BY u."Email";
```

Créer localement `pilotes-ids.txt`, un ID sélectionné par ligne (500 maximum). Se connecter avec un compte Admin/RH et utiliser son `accessToken` dans la saisie masquée de l'outil. Prévisualiser :

```bash
python3 deploy/first-login-enroll.py --base-url https://VOTRE-GATEWAY --ids-file pilotes-ids.txt
```

Vérifier les emails et statuts `would_enroll`, puis appliquer aux mêmes IDs :

```bash
python3 deploy/first-login-enroll.py --base-url https://VOTRE-GATEWAY --ids-file pilotes-ids.txt --apply
```

L'API équivalente est `POST /api/Auth/first-login/enroll`, Bearer Admin/RH, corps `{"userIds":[123,456],"dryRun":true,"renewExpired":false}`. `dryRun` vaut true si omis ; passer explicitement false pour appliquer. Les comptes inactifs, non éligibles, déjà activés ou déjà personnalisés sont ignorés avec un statut explicite.

## 4. Recette avant généralisation

Sur un pilote et un qualiticien sélectionnés :

1. Email + temporaire : formulaire obligatoire, aucune ouverture de l'application ni de session JWT.
2. Mauvaise confirmation, mot de passe court ou égal au temporaire : refus.
3. Nouveau mot de passe accepté : retour à la connexion ; le nouveau fonctionne, le temporaire ne fonctionne plus.
4. Ancien JWT/refresh token refusés. Un compte non sélectionné et les rôles RH/Admin fonctionnent comme avant.
5. Redémarrer Auth et réimporter le même utilisateur depuis Planning : son nouveau mot de passe doit continuer à fonctionner.
6. Contrôler en base `MustChangePassword=false`, `PasswordChangedAt` renseigné, `FirstLoginTokenHash=NULL` et l'événement `FirstLoginCompleted`. Ne pas exporter les hashes.

Élargir ensuite par groupes. Les tests automatisés sont décrits dans `FIRST_LOGIN_TESTS.md`.

## Assistance et arrêt des nouvelles activations

- Autorisation du formulaire expirée : revenir à la connexion, utiliser le temporaire encore valide et recommencer.
- Temporaire expiré : prévisualiser puis appliquer avec `--renew-expired`. Cela ne réactive jamais un compte déjà personnalisé. La nouvelle expiration repart de la date d'application.
- Changement de configuration du secret commun : ne remplace pas les hashes des comptes déjà activés. Renouveler explicitement les activations expirées, ou utiliser le reset administrateur existant pour un compte précis.
- Reset administrateur d'un compte encore en attente : invalide son ancienne autorisation, conserve l'obligation de changement pendant 24 h tant que son rôle reste éligible. S'il a été promu vers un rôle non éligible, le reset administrateur explicite retire cette obligation devenue inapplicable.
- Mettre `KYNTUS_FIRST_LOGIN_ENABLED=false` et recréer Auth arrête les nouvelles activations. Les comptes déjà en attente restent obligés de personnaliser leur mot de passe, et les mots de passe personnels restent valides.
- Après activation de comptes, **ne pas redéployer l'ancien Auth ni retirer les contrôles métier** : cela supprimerait l'obligation de changement pour les comptes en attente. Corriger en conservant les contrôles, ou suspendre l'accès des comptes concernés. Ne pas supprimer les nouvelles colonnes ; le retour arrière EF est volontairement bloqué.

Aucune mise en production, activation réelle ou modification de la base de production n'est réalisée par cette livraison.
