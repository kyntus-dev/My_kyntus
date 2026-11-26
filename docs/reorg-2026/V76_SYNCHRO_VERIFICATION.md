# v76 — Synchro organisation : la vérification fonctionne enfin

## Le constat après la v73

La v73 a rendu la vérification honnête : au lieu d'afficher « alignée » à tort, l'écran a affiché
« non vérifiée — Planning et Prime injoignable(s) ». Les départs, eux, sont bien passés
(`departuresRepublished: 38`, et Prime compte 318 fiches dont 38 sorties).

## La vraie cause : les appels partaient sans jeton

Les services Planning et Prime tournaient normalement (même adresse, même port 8080). Mais l'annuaire
les interrogeait **sans jeton de connexion**. Or :

- côté Planning, `/api/admin/org-reconciliation/verify` et `/api/users` sont réservés aux
  utilisateurs connectés ;
- côté Prime, toute l'API exige un utilisateur connecté (seul `/health` est ouvert).

Les deux répondaient donc **401**, l'erreur était avalée et notée « injoignable ». Cette vérification
n'a en réalité **jamais pu aboutir**, avant comme après la v73 — la v73 l'a seulement rendu visible.

## Correction

- L'annuaire interroge Planning et Prime **au nom de l'administrateur qui lance la réconciliation** :
  le jeton de sa requête est transmis tel quel, jamais stocké. Sans utilisateur connecté (tâche
  automatique), rien n'est transmis et l'écran reste honnêtement « non vérifiée ».
- Les agents sortis que Prime garde depuis la v73 ne sont plus pris pour des orphelins : seuls les
  agents présents sont comparés à l'annuaire, et « Employés Prime » compte les présents.
- Le journal dit maintenant **pourquoi** un service n'a pas répondu (HTTP 401, connexion impossible,
  délai dépassé) au lieu d'un simple « unreachable ».

## Après l'installation

Administration → Synchro organisation → « Lancer la réconciliation ». L'écran doit afficher les
compteurs « Utilisateurs Planning actifs » et « Employés Prime » (≈ 280), les orphelins, et le statut
« alignée » ou « écarts détectés » — plus « non vérifiée ».
