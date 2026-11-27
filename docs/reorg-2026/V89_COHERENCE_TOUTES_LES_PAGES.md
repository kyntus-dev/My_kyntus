# V89 — Cohérence de toute l'application (pages hors maquette)

Base : `main` + v80 → v88. **Interface seule, styles uniquement** (`kyntus-maquette.css`, section 10) :
aucun fichier TypeScript ou HTML, aucune API, table, règle, droit ni comportement modifiés.

## Pourquoi

Les modules ont été écrits à des moments différents : on trouve plusieurs familles de classes pour la même
chose (boutons `ky-btn-*`, `ku-btn`, `btn-primary`, `mdc-btn`… ; cartes `ky-card`, `card-navy`, `gen-card`,
`mdc-card`… ; champs `form-group`, `ku-field`, `doc-field`, `filter-ctl`…). La v89 leur donne **le même langage
visuel** que l'accueil et les écrans de traitement, en clair comme en sombre.

## Ce qui est harmonisé

| Élément | Règle commune |
|---|---|
| Cartes et sections | même arrondi (18 px), même bordure, même ombre |
| Titres de section, aides | même police et même graisse ; aides en gris lisible |
| Formulaires | libellés au-dessus (12,5 px, gras, gris) ; champs de 38 px, même fond, même bordure ; focus bleu identique ; astérisque obligatoire en rouge ; boutons d'action alignés à droite |
| Boutons (toutes familles) | même arrondi, même graisse, même contour clavier, même état désactivé |
| Tableaux hors écran de traitement | même cadre, en-têtes gris |
| Fenêtres (modales) | fond voilé, en-tête et pied séparés par un trait, titre dans la police des titres |
| États vides | encadré pointillé centré |
| Onglets | même arrondi, graisse et contour clavier |
| Calendriers | jour courant cerclé de bleu (classe `is-today` déjà posée par les écrans) |
| Fiches de détail | libellés gris, valeurs en couleur principale (`dt` / `dd`, priorité minimale) |

## Garanties

- Les styles propres à un écran restent **prioritaires** : les nouvelles règles ont la même spécificité que les
  styles d'écran et sont chargées avant eux (ou ont une priorité nulle avec `:where`). Elles comblent les manques
  sans écraser un choix d'écran.
- Aucune couleur de décision n'est changée (les boutons « Valider » verts, « Refuser » rouges… restent tels quels).
- Rien n'est masqué, ajouté ni déplacé ; aucun texte modifié.
- Mouvement réduit respecté.

## Tests sur l'environnement de test (Ctrl+F5)

Clair puis sombre, ordinateur puis téléphone :
1. Formulaires : création d'un employé, contrat, demande de congé, demande de document, réclamation, paramètres.
2. Fenêtres : refus d'un congé, régime horaire, confirmation de suppression.
3. Fiches : détail d'un employé, d'un parrainage, d'une fiche Prime.
4. Plannings et calendriers : planning de la semaine, calendrier d'équipe des congés (jour courant).
5. États vides : une liste filtrée sans résultat.
