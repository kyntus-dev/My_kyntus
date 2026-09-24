# Validation — première connexion obligatoire

## Commandes reproductibles

```bash
dotnet test AuthService/Auth.Application.UnitTests/Auth.Application.UnitTests.csproj -c Release
dotnet test AuthService/Auth.ArchitectureTests/Auth.ArchitectureTests.csproj -c Release
dotnet test AuthService/Auth.CharacterizationTests/AuthService.CharacterizationTests.csproj -c Release
npm ci --prefix auth-frontend
npm test --prefix auth-frontend -- --watch=false
npm run build --prefix auth-frontend
npm ci --prefix QualiteService/backend --ignore-scripts
node --test QualiteService/backend/tests/sessionValidation.test.js
node --check QualiteService/backend/server.js
```

Le workflow `Mandatory first login` reproduit ces contrôles, fournit PostgreSQL 16 et compile les sept backends métier consommateurs de la bibliothèque JWT. Le test PostgreSQL crée puis supprime son propre schéma aléatoire ; il vérifie la migration sur une table Users préexistante, la conservation des anciens hashes, une deuxième migration sans effet et deux changements simultanés dont un seul peut réussir.

Pour l'exécuter ailleurs, définir `FIRST_LOGIN_TEST_POSTGRES` avec une connexion à une instance PostgreSQL **de test**. Sans cette variable, ce seul test est explicitement ignoré. Les tests HTTP utilisent SQLite et de vrais hashes, pas des mocks du service de changement.

## Résultats locaux du 24 septembre 2026

- Compilation Auth API et bibliothèque JWT : réussie.
- Tests unitaires Auth : 26 réussis.
- Tests HTTP/validation de session : résultats finaux consignés dans la PR ; PostgreSQL exécuté séparément dans GitHub si le workflow est disponible.
- Angular : 9 tests réussis ; build production réussi. Avertissement CSS préexistant sur home.component.css (13,98 kB pour un budget d'avertissement de 10 kB).
- KCQ : test du contrôle de session réussi (204 accepté ; 200, redirections, 401, 403, 500 et serveur indisponible refusés). Syntaxe server.js valide.
- Les anciens tests Angular de composants avaient des imports incorrects, un ancien titre de page et un fichier guard.spec.ts contenant l'implémentation au lieu de tests : corrigés pour exécuter la suite.

Scénarios couverts : pilote/qualiticien, aucune émission de JWT avant changement, remplacement du hash, refus du temporaire après changement, autorisation consommée/expirée/remplacée, compte inactif/verrouillé, confirmation/politique du mot de passe, activation Admin/RH uniquement et prévisualisation par défaut, révocation d'un JWT existant, refus de réactivation après personnalisation, non-écrasement du mot de passe à l'import, création de nouveaux comptes et refus du refresh pendant l'obligation.

## Limites connues de la référence main

Le workflow général `verify-unification` échouait déjà sur le commit de référence a0c0f0b : tests Planning (FloorEndpointGoldenTests) et FakeImportUserService incomplet dans ImportExecutionJournalTests (CS0535). La réussite des tests ciblés ne signifie donc pas que la suite globale du dépôt est intégralement verte. Aucune garantie « zéro régression » ne remplace la recette de préproduction détaillée dans FIRST_LOGIN_PRODUCTION.md.

Les tests ne simulent pas la charge de production ni toutes les connexions SignalR/Socket.IO persistantes. Vérifier ces points sur l'infrastructure cible, ainsi que la connectivité de chaque service vers Auth et la configuration HTTPS/gateway. Aucun secret ni compte de production n'a été utilisé.
