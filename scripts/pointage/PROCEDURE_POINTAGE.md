# Pointage ZKBio Time → My-Kyntus

## Principe

Les 6 pointeuses (une par étage) envoient déjà leurs pointages à ZKBio Time (https://10.10.10.7). My-Kyntus ne parle pas aux pointeuses : il **lit** ZKBio Time par son API REST, depuis le serveur 10.10.10.25, qui est sur le même réseau.

```
Pointeuses (étages 1 à 6) ──► ZKBio Time 10.10.10.7 ──API (lecture seule)──► My-Kyntus 10.10.10.25
                                                                           ├─ pointages bruts (sans doublon)
                                                                           ├─ calcul : retards, pauses, pointages manquants
                                                                           └─ écrans : Mon suivi, équipe, RH
```

- **Lecture seule** : My-Kyntus n'écrit jamais dans ZKBio Time.
- Le compte de l'API et son mot de passe restent sur le serveur (fichier `.env`), jamais dans le navigateur.

## Étape 1 — compte dédié dans ZKBio Time (5 minutes, dans l'interface)

Dans ZKBio Time, ouvrez **Système › Utilisateur** et créez le compte `kyntus-api` :

- donnez-lui un mot de passe fort ;
- accordez-lui uniquement la **consultation** des modules Personnel, Appareil et Présence, sans droit de modification si le choix est proposé ;
- n'utilisez pas le compte administrateur.

Vérifiez ensuite la version : menu du profil (en haut à droite) › **À propos**. L'API REST existe à partir de BioTime 8.

## Étape 2 — diagnostic depuis le serveur (lecture seule)

```bash
cd /home/o/projets_mykyntus-
tar -xzf /tmp/pointage-scripts.tar.gz            # scripts/pointage + scripts/reorg-2026 (client Kyntus)
cd scripts/pointage
PY="docker run --rm -it --network host -e BIOTIME_USER -e KYNTUS_GATEWAY -e KYNTUS_EMAIL -v $PWD/..:/s -w /s/pointage python:3.12-slim python3"

# a) Empreinte du certificat (certificat auto-signé : on l'épingle au lieu de désactiver le contrôle)
$PY 01_diagnostic_biotime.py --empreinte
```

Comparez l'empreinte affichée avec celle du navigateur. Ouvrez https://10.10.10.7, puis cliquez sur le cadenas « Non sécurisé » › Certificat › Empreinte SHA-256. Si les deux correspondent :

```bash
export BIOTIME_USER=kyntus-api KYNTUS_GATEWAY=http://localhost:8500
read -p "Email Admin My-Kyntus : " KYNTUS_EMAIL; export KYNTUS_EMAIL
$PY 01_diagnostic_biotime.py --empreinte-attendue 'AB:CD:…'   # le script demande les mots de passe
```

Le script affiche :

- le mode d'authentification accepté ;
- les pointeuses et leur dernière activité ;
- les pointages des 2 derniers jours (entrées, sorties, pauses, par étage) et le délai entre pointeuse et ZKBio Time ;
- la **correspondance** entre l'ID pointeuse (210, 102, 359…) et les collaborateurs My-Kyntus : par ID technicien HTEL, sinon par nom.

Le dossier `diagnostic_AAAAMMJJ_HHMM/` contient :

- `correspondance_pointeuse_kyntus.csv` ;
- `echantillon_pointages.json` (5 pointages).

**Renvoyez ces deux fichiers et la sortie du script** : ils fixent la clé de correspondance et le format réel des données avant le développement.

En cas de blocage :

- **`Authentification refusée`** : ouvrez https://10.10.10.7/api/docs/ dans le navigateur. Si la page n'existe pas, l'API n'est pas disponible dans cette version ou cette licence ; voir le plan B.
- **Pas de réponse depuis le serveur** : vérifiez que 10.10.10.25 joint 10.10.10.7 sur le port 443 (`curl -k -o /dev/null -w '%{http_code}' https://10.10.10.7/login/`).

## Étape 3 — intégration dans My-Kyntus (développement, après le diagnostic)

1. **Synchronisation** : un service en tâche de fond, toutes les 60 secondes.
   - Il lit `/iclock/api/transactions/` depuis le dernier pointage reçu, avec un chevauchement de 10 minutes pour rattraper les pointages arrivés en retard.
   - Il enregistre chaque pointage une seule fois (identifiant ZKBio Time unique) et reprend seul après une coupure.
   - Il complète l'historique sur la période choisie lors de la mise en service.
2. **Correspondance** : un champ « ID pointeuse » sur la fiche collaborateur, rempli automatiquement d'après le diagnostic et modifiable par RH. Les pointages sans correspondance sont listés pour RH.
3. **Calcul**, comparé au planning publié dans My-Kyntus :
   - arrivée, départ et pauses (états 0 entrée, 1 sortie, 2 début de pause, 3 fin de pause) ;
   - retard d'entrée avec une tolérance de 5 minutes : au-delà, tout le retard compte (même règle que HTEL) ;
   - dépassement de pause et pointage manquant.
4. **Écrans** :
   - « Mon suivi » pour chaque agent : retards, pointages manquants, détail par jour, congés My-Kyntus ;
   - vue équipe pour le superviseur (son périmètre) ;
   - vue globale et export Excel pour RH.

## Plan B — si l'API n'est pas disponible

ZKBio Time enregistre les pointages dans sa propre base de données (table des transactions). Un **utilisateur de base en lecture seule** sur cette seule table permet la même synchronisation, sans toucher au logiciel.
