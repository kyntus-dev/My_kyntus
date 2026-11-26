# v66 — Liaison HTEL, import du fichier RH, droits d'accès

À installer **avec la v65** (`installer_v66.sh` pose les deux, dans l'ordre). Aucune migration de
schéma ; historiques de pointage, de paie et fiches conservés.

## 1. Liaison HTEL

**Cause.** Le rapport ne comparait les techniciens HTEL qu'aux fiches **actives et non liées**. Une
fiche inactive, ou déjà liée à un autre identifiant HTEL (doublon côté HTEL), faisait passer son
technicien pour « Non défini sur MyKyntus ». Aggravant : « Synchroniser » **désactivait** toute fiche
dont le technicien était inactif dans HTEL — la fiche sortait alors des listes, puis son technicien
apparaissait « non défini ». Enfin, une fiche présente dans l'application mais absente de l'annuaire
n'était pas vue du tout.

**Correction.**
- Rapport sur **toutes** les fiches (actives, inactives, liées, libres). « Non définis » = aucune fiche
  ne ressemble au technicien. « À rapprocher » = une ou plusieurs fiches existent (avec leur état :
  inactive, déjà liée à l'ID X, % de ressemblance).
- Plus aucune désactivation automatique : les écarts (ID absent d'HTEL, technicien inactif / fiche
  active) sont listés dans **Divergences** ; la sortie se décide depuis la fiche.
- L'écran recoupe aussi avec les fiches de l'application : si une fiche ressemble à un technicien
  « non défini », on propose **Lier** au lieu de « Créer employé » (plus de doublon).
- Rapprochement des noms indexé (particules collées « ELAMRANI » / « EL AMRANI », ordre inversé).
- Correctif : accolades manquantes dans `ApplyLinkOnEmployeeAsync` (NullReferenceException quand l'ID
  lié n'existe plus dans HTEL : l'enregistrement de la fiche échouait).
- Un ID présent deux fois dans HTEL ne fait plus échouer la synchronisation.

## 2. Import du fichier RH

**Cause.** L'import de l'écran « Liaisons HTEL » écartait toute ligne dont le nom existait déjà
(« Déjà présent ») : la fiche n'était ni vérifiée ni mise à jour. Les valeurs illisibles (date,
situation familiale, e-mail) étaient ignorées sans le dire ; une date impossible (« 31/31/1990 »)
faisait échouer l'import entier ; un intitulé « Nom » pouvait être lu comme « Nombre d'enfants ».

**Correction.**
- Chaque ligne est rapprochée d'une fiche : **CIN** d'abord (nouvel identifiant), puis nom.
- Fiche identifiée → comparée champ par champ (CIN, naissance, situation, enfants, adresse, e-mail
  personnel, téléphone) : valeurs différentes remplacées, champs vides complétés, jamais vidés.
  Chaque changement est journalisé (avant / après), poussé vers l'annuaire et **annulable** dans
  Utilisateurs › Fiches employés › Historique.
- Doute (homonymes, même nom mais CIN différent, CIN porté par deux fiches, même fiche visée par deux
  lignes) → « À vérifier », rien n'est écrit, fiches candidates proposées.
- Lignes sans fiche → créées comme avant.
- Valeurs non reconnues signalées ligne par ligne, jamais appliquées.
- Le fichier RH de référence (v63) bénéficie du même rapprochement par CIN et de la colonne
  « E-mail personnel ».

## 3. Droits d'accès

**Cause.** Le contrôle des API à l'entrée des services ne s'appliquait **qu'aux administrateurs**
restreints par le Super Admin. Un droit retiré à un rôle (« Accès par rôle ») ou à un compte
(« Accès par compte ») disparaissait du menu, mais l'API répondait toujours. Retirer « Consulter » à
un compte laissait « Modifier » actif (qui rouvrait l'écran). Plusieurs pages d'action
(création, import, liaisons, fiches, mails internes, planification de formation) s'ouvraient par
leur URL avec le seul droit « Consulter ». Et la rubrique « Heure de sortie » créée en v65 n'avait
aucune ligne de droits sur une base en service.

**Correction.**
- Le contrôle serveur couvre **tous les rôles** : un droit **explicitement retiré** (décoché sur tous
  ses périmètres, ou retiré au compte) renvoie 403 `ACCESS_REVOKED` sur ses API. Hiérarchie :
  compte > rôle ; un retrait de « Consulter » retire l'écran entier. Les droits jamais accordés ne
  sont pas concernés (le contrôle reste celui du service : aucune régression sur les écrans non
  configurés).
- Mêmes règles dans les droits renvoyés à l'application (menu, boutons) et dans l'évaluateur de droits.
- Prise en compte en quelques secondes (5 s annuaire, 10 s autres services), sans reconnexion.
- Pages d'action : « Modifier » exigé à l'URL comme dans le menu.
- `/api/users/fiches-reference` et l'écriture des mails internes rattachés à « Utilisateurs ».
- « Heure de sortie » reprend, une fois, les réglages de « Mon suivi de pointage ».
- Test permanent : chaque entrée de menu liée à une rubrique ouvre une route qui contrôle cette
  rubrique (et « Modifier » quand l'entrée l'exige).
