# Dashboard legacy — retiré

Les anciennes pages RH/employé (`pages/dashboard-home/`, `pages/dashboard-employee/`) ont été **supprimées**.
Le tableau de bord unique est `UnifiedDashboardComponent` + `GlobalDashboardService` (`/home`).

Les URLs `/dashboard` et `/dashboard-employee` redirigent vers `/home`.

**Ne pas réintroduire** de grille « microservices » ni de liens rapides copiant le menu latéral sur `/home`.
