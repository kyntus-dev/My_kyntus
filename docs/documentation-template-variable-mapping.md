# Spec — Mapping des variables de modèles documentaires

Contrat entre l’écran **Modèles** (`/documentation/templates`), le catalogue **Champs employés** (`/users/fields`) et la génération RH (`/documentation/doc-gen`).

Le catalogue `/users/fields` (champs système **et** personnalisés) est la **seule** source de vérité. Le service Documentation ne duplique pas ce catalogue.

## Pipeline RH / Admin

1. Import d’un modèle dynamique (DOCX).
2. Détection des placeholders (`{{ nom }}`, `(Nom employé)`, etc.).
3. Comparaison automatique avec les champs employés (même moteur que l’import : normalisation + similarité).
4. Revue RH/Admin : badges de confiance, mapping manuel, ou « À saisir par RH ».
5. Enregistrement du mapping sur la version courante.
6. À la génération pour un employé : remplissage DB, sinon saisie RH de secours.

## Seuils de confiance

Identiques à l’import employés (`EmployeeImportColumnMatcher`) :

| Confiance | Score | Comportement à la détection |
|-----------|-------|-----------------------------|
| `high` | ≥ 0,92 | Mapping prévalidé vers le champ employé (`formScope = db`). |
| `medium` | ≥ 0,75 | Suggestion affichée ; confirmation RH recommandée. |
| `low` | < 0,75 | Aucun mapping ; variable classée « À saisir par RH ». |

Candidats comparés, dans l’ordre du meilleur score : libellé affiché, nom technique, nom normalisé, placeholder brut.

## Catalogue `/users/fields`

- Relu à **chaque** ouverture du détail modèle et à chaque génération.
- Champs **activés** : proposés dans la liste de mapping.
- Champ **désactivé** : reste valide uniquement pour un mapping **déjà enregistré** ; signalé « indisponible ». Si aucune valeur exploitable n’est retournée pour l’employé, la saisie RH de secours s’ouvre.
- Champ **supprimé** : mapping conservé en base modèle, signalé indisponible, même repli RH.
- Un nouveau champ personnalisé créé sur `/users/fields` devient immédiatement mappable (pas de resynchronisation manuelle).

## Règle anti-blocage

Toute variable aboutit à l’un des deux états :

- mappée à un `fieldKey` employé, ou
- classée **À saisir par RH** (`formScope = hr`, `sourceFieldKey` vide).

À la génération, priorité des valeurs :

1. Valeur employé non vide (y compris un mapping existant vers un champ désactivé s’il a encore une valeur).
2. Saisie RH **uniquement** si la valeur employé est nulle, vide, placeholder (`x`, `(x)`, `—`, etc.) ou si le mapping est indisponible sans valeur.
3. Valeur par défaut du modèle.

Les variables obligatoires ne bloquent **qu’après** cette résolution. Un champ DB vide pour l’employé X n’empêche jamais l’aperçu : le champ RH de secours s’affiche.

La saisie RH **ne remplace pas** une valeur DB déjà renseignée.

## Métadonnées persistées par variable

| Champ | Rôle |
|-------|------|
| `sourceFieldKey` | Clé stable du catalogue employés. |
| `mappingConfidence` | `high` / `medium` / `low`. |
| `mappingConfirmed` | `true` si prévalidé (`high`) ou confirmé / choisi manuellement. |
| `formScope` | `db` si mappé, `hr` si saisie RH. |

Les indicateurs live (`mappingUnavailable`, `suggestedFieldKey`, `mappingScore`, `filledFromDb`, `needsRhInput`) sont recalculés, pas stockés comme catalogue.

## Rôles

RH et Admin peuvent définir les modèles, mapper les variables et générer les documents.
