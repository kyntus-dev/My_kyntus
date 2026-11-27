# V91 — Accueil : 5 indicateurs au plus

Base : `main` + v80 → v90. Interface seule (`core/dashboard/global-dashboard.config.ts`) : aucune API, règle,
droit ni donnée modifiés. Les pages concernées restent accessibles par le menu et les raccourcis.

| Rôle | Retiré de l'accueil | Indicateurs restants |
|---|---|---|
| Admin, RH | Validations PRIME | Actions en attente · Employés actifs · Congés à valider · Réclamations ouvertes · Contrats à échéance |
| RP | Validations PRIME pôle | Actions en attente · Congés équipe · Réclamations ouvertes · Docs en attente · Passage production |
| Pilote (et collaborateur, ingénieur, support IT : même profil d'accueil) | Formations actives | Semaine active · Jours planifiés · Congés en attente · Solde congés · Notifications |
| Autres rôles | — | 5 au plus, dans l'ordre de priorité du profil (Manager, Coach, CDP… : « Passage production » passe hors de l'accueil) |

Les indicateurs retirés ne sont plus calculés sur l'accueil (moins d'appels au chargement). « Voir comme » (v88)
suit les mêmes règles. Test : `accueil-profil.util.spec.ts` (v91).
