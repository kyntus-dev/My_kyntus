# Échantillons — Fiche PRIME

## Fichier exemplaire (layout v2 — standard)

Référence **v2** : `EXEMPLAIRE PRIME1.xlsx` — voir [../prime-fiche-template-v2.md](../prime-fiche-template-v2.md).

**Téléchargement dans l’application SPA** : le même fichier est servi sous  
`projet_kyntus_service_planning-frontend/public/assets/prime/fiche-prime-modele-standard.xlsx`  
(bouton « Télécharger le modèle standard » dans Templates fiche PRIME, saisie et import).

Pour mettre à jour le modèle standard après modification de l’exemplaire :

```powershell
Copy-Item "docs\samples\EXEMPLAIRE PRIME1.xlsx" "projet_kyntus_service_planning-frontend\public\assets\prime\fiche-prime-modele-standard.xlsx" -Force
```

## Fichier exemplaire (layout v1 — legacy)

- `Exemplaire Prime.xlsx` (layout **v1**, voir [../prime-fiche-template-v1.md](../prime-fiche-template-v1.md))

Exemple (PowerShell, depuis la racine du dépôt) :

```powershell
Copy-Item "C:\chemin\vers\Exemplaire Prime.xlsx" "docs\samples\Exemplaire-Prime.xlsx"
```

Le dépôt peut fonctionner sans ces fichiers (tests unitaires génèrent un classeur minimal en mémoire). Les fichiers servent de **référence humaine** et de **modèle téléchargeable** (v2).

À l’import dans « Templates fiche PRIME », l’application extrait en plus une copie **multi-feuilles** bornée (`calcSheets`) pour recalculer les formules dans le navigateur (aperçu / résultat), y compris les références vers d’autres feuilles du classeur exemplaire. Les templates déjà enregistrés **avant** cette fonctionnalité n’ont pas ce bloc : **réimportez** le `.xlsx` puis enregistrez à nouveau le template pour bénéficier du recalcul complet.
