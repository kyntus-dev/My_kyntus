# My Kyntus — connexion et recette locales

Préparé le 8 septembre 2026 pour `kyntus-dev/My_kyntus`, commit
`59b2cc9b5afdb650955607db35d8e24e6f674b9f`.

**La redirection vient du profil `deploy` chargé dans les deux interfaces.**
Le callback de connexion est configuré vers `http://10.10.10.25:8200/auth-callback`.
Ce dossier prépare un profil Docker local qui reste sur `localhost` et restreint les
connexions sortantes des conteneurs. Il s'applique à une stack Docker Desktop
déjà construite sur votre PC Windows.

Les contrôles réalisés couvrent la syntaxe PowerShell, la lecture du YAML, les
cibles des montages et des routes, le profil local et les valeurs HTTPS par
défaut après surcharge. Ils ne constituent pas une exécution du pack. Docker et
PowerShell ne sont pas disponibles dans l'environnement qui a servi à le préparer :
son exécution Windows/Docker et votre connexion réelle restent à vérifier sur
votre PC. Le mode `-VerifierSeulement` vérifie la configuration avant toute
recréation de conteneur. La recette de l'application reste ensuite à exécuter.

## 1. Extraire et vérifier sur votre PC

Extraire le dossier `My_Kyntus_Mode_Local` contenu dans le ZIP à la racine du
projet. Vous devez obtenir notamment :

`D:\My-Kyntus\projets_mykyntus-\projets_mykyntus-\My_Kyntus_Mode_Local\Activer-Local.ps1`

Dans **PowerShell sur votre PC**, avec Docker Desktop démarré en mode Linux :

```powershell
Set-Location 'D:\My-Kyntus\projets_mykyntus-\projets_mykyntus-'
powershell -NoProfile -ExecutionPolicy Bypass -File .\My_Kyntus_Mode_Local\Activer-Local.ps1 -VerifierSeulement
```

Ce premier passage écrit les fichiers de configuration dans le dossier extrait
et vérifie le moteur Docker, les connexions et les volumes. Il ne démarre et ne
recrée aucun conteneur. Le contenu du `.env` et les secrets ne sont pas affichés.

Si votre Docker Desktop utilise le contexte `default` au lieu de `desktop-linux`,
consulter `docker context ls`, puis ajouter `-DockerContext default` aux commandes.
Le script vérifie quand même que ce contexte correspond à un pipe local Docker
Desktop. Un contexte SSH/TCP distant est refusé.

Résultat attendu :

```text
Contexte local confirme : desktop-linux (Docker Desktop Linux).
Configuration verifiee : SQL/Mongo/Redis/RabbitMQ locaux ; reseau interne ; URLs localhost.
Verification terminee. Aucun conteneur demarre ou recree.
```

En cas d'arrêt, corriger le point indiqué avant l'activation. Vous pouvez partager
le message d'erreur, mais pas le `.env`, les jetons ni `secrets-locaux.json`.

## 2. Activer la configuration

Après une vérification réussie :

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\My_Kyntus_Mode_Local\Activer-Local.ps1
```

Cette commande recrée les conteneurs de la stack **locale** avec la configuration
de recette. Les volumes Docker existants du PC sont conservés : vos tests vont
modifier ces données locales. Elle n'efface pas les volumes, ne crée pas un
nouveau jeu de données et n'active pas automatiquement les comptes de démo.
Les éventuels seeds déjà activés dans votre configuration locale restent actifs.

Une nouvelle clé JWT et une nouvelle clé interne sont générées pour cette recette.
Il faut donc se reconnecter. Les anciens jetons locaux cessent d'être utilisables.
Le fichier contenant ces clés reste dans ce dossier ; il est réutilisé à chaque
activation. Gardez-le local et ne le transmettez pas.

Le pack conserve aussi, avant recréation, les modèles documentaires déjà présents
dans `/app/local-template-storage`, puis monte leur dossier local pour les rendre
persistants. Il s'arrête si cette récupération échoue ou si une destination
existante rendrait la copie ambiguë.

Il ne lance ni compilation, ni téléchargement d'image. Si une image manque,
l'activation peut rester partielle. Il faut construire les images sur Docker
Desktop avec la configuration locale, puis relancer le script ; ne pas lancer
un script de déploiement. Une erreur de démarrage ne signifie pas que les tests
fonctionnels sont réussis.

## 3. Vérifier la connexion dans le navigateur

1. Fermer les anciens onglets My Kyntus. Ouvrir une fenêtre privée dédiée à la recette.
2. Ouvrir `http://localhost:8201/login`.
3. Se connecter avec un compte **présent dans la base locale**.
4. Vérifier que le callback passe par `http://localhost:8200/auth-callback`, puis
   que l'adresse reste sur `http://localhost:8200/...`.
5. Se déconnecter : la page de connexion doit rester sur `http://localhost:8201/login`.
6. Refaire le cycle après actualisation et après expiration de la session.

Le script vérifie les fichiers servis par les deux frontends. Vous pouvez aussi
les ouvrir directement :

- `http://localhost:8200/kyntus-public-urls.js`
- `http://localhost:8201/kyntus-public-urls.js`

Ils doivent contenir `localhost` pour les URLs de l'application et aucun
`10.10.10.25`. En cas d'ancien affichage, faire `Ctrl+Maj+R` sur les deux interfaces.
La mention `deploy` dans le paramètre `?v=...` d'un ancien HTML peut subsister :
c'est le **contenu effectivement servi** du script qui détermine les URLs.

Dans F12 → Réseau, conserver le journal, puis filtrer `10.10.10.25` : aucune
requête de l'application ne doit partir vers ce serveur pendant le parcours.
Vérifier aussi les appels `/api`, `/hubs`, `/socket.io` et le module Qualité.
Ils doivent utiliser les origines locales. Le navigateur lui-même n'est pas
enfermé dans le réseau Docker : un lien externe cliqué, une vidéo distante ou
une URL de pièce jointe importée peut encore quitter votre PC. Utiliser des
fichiers et des liens de test locaux. Les polices Google présentes dans les
interfaces peuvent également provoquer des lectures externes.

## 4. Périmètre de l'isolement

| Élément | Configuration du pack | Contrôle à faire |
|---|---|---|
| Connexion / retour au portail | URLs et callback sur `localhost:8201` / `localhost:8200` | Connexion, déconnexion, actualisation, expiration |
| Moteur Docker | Pipe local Docker Desktop Windows ; contexte explicitement fourni à chaque commande | Message « Contexte local confirme » |
| PostgreSQL | Chaînes de connexion vers `Host=postgres` | Chaque module retrouve sa propre base locale |
| MongoDB Qualité | `mongodb://kcq-mongo:27017/kcq` | Données Qualité locales et persistance |
| Redis et RabbitMQ | Noms de services Docker locaux | Synchronisation entre modules, notifications internes |
| Volumes | Stockage Docker local ; stockage externe ou options particulières refusés | Préparer ou sauvegarder les données du PC avant une campagne destructrice |
| Réseau des conteneurs | Réseau bridge `internal: true`, unique pour les services de cette stack | Le script contrôle le réseau réellement attaché après démarrage |
| Ports publiés | Liaison par défaut sur `127.0.0.1` | Accès depuis le navigateur du PC |
| Emails | Configuration SMTP d'environnement vidée ; réseau sortant bloqué | Un paramètre SMTP/API stocké en base peut encore déclencher une tentative qui doit échouer |
| HTEL | Cible neutralisée sur le loopback du conteneur | Les synchronisations HTEL réelles ne sont pas validables dans ce mode |
| IA documentaire | Cible neutralisée ; clé d'environnement vide | Les clés éventuellement stockées en base ne doivent pas permettre une connexion externe |
| Stockage documentaire S3 | Accès S3 désactivé ; stockage fichier local | Création, téléchargement et persistance des modèles locaux |
| Audio Qualité | Mode `local`, MySQL et picking distant désactivés | Fournir des enregistrements de test dans le dossier local monté |
| Restauration Qualité | Import automatique du dump désactivé | Préparer explicitement les données de recette |
| Web Push distant | Clés VAPID vidées | Tester les notifications internes ; push navigateur à traiter séparément |

Le réseau Docker interne est la protection principale contre les accès sortants
des conteneurs. Ce n'est pas une isolation du navigateur ou de tout le PC.
Ne pas ajouter de réseau externe ni de proxy sortant à ces conteneurs pendant la
recette. Un proxy ou service de relais déjà installé sur le PC peut nécessiter
une vérification complémentaire de votre configuration Docker/hôte.

## 5. Utiliser le classeur des tests par rôle

Renseigner l'environnement de la campagne :

```text
Environnement : LOCAL - Docker Desktop
Connexion : http://localhost:8201/login
Application : http://localhost:8200
Version / commit du code local : à relever avec git rev-parse HEAD
Date, navigateur, testeur : à compléter
Données : locales, jeux de recette identifiés
```

Préparer les comptes et les données dans les onglets `Préparation`, `Comptes`
et `Jeux`. Utiliser des enregistrements nommés `TEST_LOCAL_...` et effectuer les
tests dans les 15 onglets de rôles. Créer un compte local ne prouve pas à lui seul
que son salarié, ses affectations et ses permissions existent dans tous les modules.

Les cas dépendant d'HTEL, d'un email reçu, du push navigateur, de l'IA distante,
de S3 distant ou de l'audio externe doivent rester **Bloqués — service de test à
préparer**, tant qu'aucun double local ou environnement de test n'est raccordé.
Ne pas les marquer OK sur la base du blocage réseau. Pour les exercer sans toucher
au déploiement, il faut ensuite un SMTP de capture local, des fixtures HTEL/audio,
et selon le cas un fournisseur IA de test ou un stockage de test.

Un résultat OK sur les interfaces locales ne valide pas automatiquement TLS,
les sauvegardes/restaurations, la supervision, les capacités, les migrations ou
le fonctionnement des intégrations dans l'environnement de production. Conserver
les contrôles techniques et les critères de décision du classeur.

## 6. Démarrer les fois suivantes

Relancer la commande d'activation de ce pack, qui réutilise les clés et les données
locales. Un simple `docker compose up` depuis la racine, sans le complément de ce
pack, réappliquerait la configuration habituelle et pourrait rétablir le profil
déployé ou les intégrations externes.

Pour un arrêt temporaire, utiliser **Stop** sur ce projet dans Docker Desktop.
Ne pas supprimer les volumes. Pour revenir à une autre configuration, vérifier
d'abord ses connexions : ce pack ne propose pas de retour automatique qui
réactiverait des intégrations externes.

Les fichiers de configuration du dépôt et du serveur restent inchangés avec ce
pack Docker. Les fichiers générés sont dans `My_Kyntus_Mode_Local`. Ne pas
committer/publier ce dossier : il contient les clés locales et peut contenir les
documents de recette. Les changements normaux de code de l'application restent
à gérer séparément par votre procédure Git habituelle.

## 7. Si vous utilisez `npm start` / `ng serve`

Le montage Docker du pack n'agit pas sur les fichiers servis par `ng serve`.
Le dépôt possède déjà une commande de changement de profil pour ce cas :

```powershell
Set-Location 'D:\My-Kyntus\projets_mykyntus-\projets_mykyntus-'
.\scripts\switch-kyntus-urls.cmd -Profile local
```

Cette commande modifie des fichiers de votre copie du dépôt : les trois scripts
d'URLs, les deux HTML d'entrée, le profil actif et les références publiques de
la gateway/du compose. Elle ne change pas les connexions aux bases et ne suffit
donc pas, seule, à isoler la recette. Ne pas pousser ces changements de profil
local sur le déploiement.

Dans deux terminaux, après avoir arrêté les frontends Docker qui occupent les
mêmes ports :

```powershell
# Terminal du portail
Set-Location 'D:\My-Kyntus\projets_mykyntus-\projets_mykyntus-\projet_kyntus_service_planning-frontend'
npm start -- --host localhost --port 8200 --proxy-config proxy.conf.json
```

```powershell
# Terminal de connexion
Set-Location 'D:\My-Kyntus\projets_mykyntus-\projets_mykyntus-\auth-frontend'
npm start -- --host localhost --proxy-config ..\projet_kyntus_service_planning-frontend\proxy.conf.json
```

La commande `npm start` du projet auth fixe déjà le port 8201. Les proxies API
ci-dessus visent `http://localhost:8500` : il faut donc une gateway et des backends
locaux réellement actifs et isolés. `production: false` dans un environnement
Angular ne suffit pas à corriger le callback.

**Pour la recette complète des rôles, privilégier la stack Docker complète.**
Le proxy Angular actuel ne configure que `/api/*` et `/hubs/*` : il ne reproduit
pas tout le routage nginx du module Qualité (`/qualite-app/`, `/socket.io/`).
Les chemins imbriqués des API peuvent aussi nécessiter des motifs `/**` avec le
serveur de développement utilisé. Ne pas interpréter ces écarts de proxy comme
une validation des modules concernés.

## 8. En cas de blocage

| Message ou symptôme | Action locale |
|---|---|
| `.env` absent | Configurer un `.env` local à partir de `.env.example`, avec des mots de passe locaux cohérents. Ne pas copier aveuglément les secrets du serveur. |
| Connexion SQL refusée au contrôle | Vérifier que l'hôte est `postgres` et que la base est celle du module local. |
| Congés : authentification PostgreSQL échouée | L'exemple du dépôt utilise `Username=postgres` avec le mot de passe du rôle congé. Le rôle initialisé est `conge_user` : vérifier `KYNTUS_CONN_CONGE` et la cohérence avec `KYNTUS_DB_PASSWORD_CONGE`. |
| Montage hors projet | Remplacer le chemin par un dossier de fixtures locales à l'intérieur de la copie du projet. |
| Autre projet Compose utilise le nom du conteneur | Vérifier le dossier de lancement et le nom de projet utilisé auparavant ; ne pas supprimer le conteneur de l'autre projet. |
| MongoDB ne démarre pas | Conserver la version d'image compatible avec le volume local. Le script inclut votre `docker-compose.override.yml` s'il existe ; ne pas supprimer le volume pour contourner l'erreur. |
| Service unhealthy | Lire ses logs dans Docker Desktop et corriger sa configuration locale. Garder la recette en attente. |
| Toujours redirigé | Lire les deux scripts d'URLs servis ; vérifier que le navigateur ouvre bien les ports 8200/8201 du PC ; actualiser les deux origines. |
| HTEL / mail / IA ne répondent plus | C'est attendu sans service de test. Marquer les scénarios concernés Bloqués ; ne pas réactiver les endpoints réels. |

## Sources vérifiées

- [Configuration d'URLs active du dépôt](https://github.com/kyntus-dev/My_kyntus/blob/59b2cc9b5afdb650955607db35d8e24e6f674b9f/config/kyntus-public-urls.runtime.js) : profil `deploy` et callback serveur.
- [Connexion Angular](https://github.com/kyntus-dev/My_kyntus/blob/59b2cc9b5afdb650955607db35d8e24e6f674b9f/auth-frontend/src/app/components/login/login.component.ts) : redirection après authentification via `planningAuthCallback`.
- [Script local/deploy existant](https://github.com/kyntus-dev/My_kyntus/blob/59b2cc9b5afdb650955607db35d8e24e6f674b9f/scripts/switch-kyntus-urls.ps1) : génération des profils.
- [Composition des services et montages](https://github.com/kyntus-dev/My_kyntus/blob/59b2cc9b5afdb650955607db35d8e24e6f674b9f/docker-compose.yml) et [exemple de variables](https://github.com/kyntus-dev/My_kyntus/blob/59b2cc9b5afdb650955607db35d8e24e6f674b9f/.env.example).
- [Docker — réseaux Compose](https://docs.docker.com/reference/compose-file/networks/#internal) : `internal: true` crée un réseau isolé des réseaux externes.
- [Docker — contextes](https://docs.docker.com/engine/manage-resources/contexts/) : un client Docker peut cibler un moteur local ou distant ; ce pack fournit le contexte local explicitement.
