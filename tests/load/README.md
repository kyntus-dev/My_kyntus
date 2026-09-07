# Load tests Kyntus — capacité ≥300 utilisateurs simultanés

## Prérequis

- [k6](https://k6.io/docs/get-started/installation/) installé
- Stack locale : `docker compose up -d` (gateway `:8500`, Redis, Postgres)
- Variable `KYNTUS_LOAD_TOKEN` = JWT valide (login Auth)

## Scénarios

| Script | VUs | Durée | Objectif |
|--------|-----|-------|---------|
| `kyntus-capacity.js` | ramp 50→150→**300** | ~15 min | Preuve capacité |

## Lancer

```bash
# Smoke 50 VUs (rapide)
k6 run -e BASE_URL=http://localhost:8500 -e KYNTUS_LOAD_TOKEN=$TOKEN --vus 50 --duration 2m tests/load/kyntus-capacity.js

# Cible 300 (scénario intégré)
k6 run -e BASE_URL=http://localhost:8500 -e KYNTUS_LOAD_TOKEN=$TOKEN tests/load/kyntus-capacity.js
```

## SLO

- p95 lectures &lt; 1 s
- taux d'erreur HTTP &lt; 1 %
- Pas de crash Redis / Postgres (surveiller `docker stats`)

## Notes

- Exécution **locale** d'abord. Sur le serveur uniquement en mode déploiement.
- Les endpoints lourds (génération planning, synthèse prime) sont exclus du mix écriture pour éviter la corruption de données.
