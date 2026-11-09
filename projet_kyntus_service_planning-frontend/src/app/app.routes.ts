import { Routes } from '@angular/router';
import { AuthCallbackComponent } from './component/pages/auth-callback.component';
import { AuthGuard } from './guard/guards/auth';
import { NewsletterAdminComponent } from './features/Newsletter-admin/newsletter-admin.component';
import { ShellLayoutComponent } from './features/shell/shell-layout.component';
import { KyntusRoleNames, ROLE_SETS } from './core/org/kyntus-role-names';
import { heureSortieGuard } from './core/services/heure-sortie-access.service';

export const routes: Routes = [

  // ─── HORS SHELL (auth / erreurs) ──────────────────
  {
    path: 'auth-callback',
    component: AuthCallbackComponent,
  },
  {
    path: 'unauthorized',
    loadComponent: () =>
      import('./features/dashboard/pages/unauthorized.component')
        .then(m => m.UnauthorizedComponent),
  },

  // ─── ANCIENS DASHBOARDS (redirigés vers le dashboard unifié) ──
  { path: 'dashboard', redirectTo: 'home', pathMatch: 'full' },
  { path: 'dashboard-employee', redirectTo: 'home', pathMatch: 'full' },

  // ─── SHELL UNIFIÉ (menu latéral global persistant) ──
  {
    path: '',
    component: ShellLayoutComponent,
    canActivate: [AuthGuard],
    children: [

      // Accueil — lanceur de microservices
      {
        path: 'home',
        loadComponent: () =>
          import('./features/dashboard/pages/unified-dashboard/unified-dashboard.component')
            .then(m => m.UnifiedDashboardComponent),
      },

      // Planning IT partagé (cellule IT : Ingénieur, Support IT) — droits dans « Accès par rôle ».
      {
        path: 'planning-it',
        canActivate: [AuthGuard],
        data: { capability: 'planning-it' },
        loadComponent: () =>
          import('./features/planning-it/planning-it.component')
            .then(m => m.PlanningItComponent),
      },

      // Notifications et paramètres — centre unifié plateforme
      {
        path: 'notifications',
        loadComponent: () =>
          import('./features/shell/pages/notifications-center/notifications-center.component')
            .then(m => m.NotificationsCenterComponent),
      },
      {
        path: 'settings',
        loadComponent: () =>
          import('./features/shell/pages/global-settings/global-settings.component')
            .then(m => m.GlobalSettingsComponent),
      },
      {
        path: 'admin',
        canActivate: [AuthGuard],
        data: {
          roles: ROLE_SETS.adminRh,
          capabilities: ['admin.overview', 'admin.access', 'admin.users', 'admin.organisation', 'admin.sync', 'admin.modules', 'admin.anomalies'],
        },
        loadComponent: () =>
          import('./features/platform/pages/admin-root.component')
            .then(m => m.AdminRootComponent),
      },
      {
        path: 'audit',
        canActivate: [AuthGuard],
        data: {
          roles: [KyntusRoleNames.Admin, KyntusRoleNames.Audit],
          capabilities: ['audit.dashboard', 'audit.journal', 'audit.access-history', 'audit.anomalies', 'audit.reporting'],
        },
        loadComponent: () =>
          import('./features/platform/pages/audit-root.component')
            .then(m => m.AuditRootComponent),
      },

      {
        path: 'assistance',
        loadComponent: () =>
          import('./features/assistance/assistance.component')
            .then(m => m.AssistanceComponent),
      },

      // ─── AIDE : guide de l'application (parcours par rôle) ──
      { path: 'aide', redirectTo: 'aide/guide', pathMatch: 'full' },
      {
        path: 'aide/guide',
        loadComponent: () =>
          import('./features/aide/app-guide-page.component')
            .then(m => m.AppGuidePageComponent),
      },

      // ─── QUALITÉ & AMÉLIORATION ──────────────────
      {
        path: 'reclamations',
        canActivate: [AuthGuard],
        data: { roles: ROLE_SETS.reclamationsEmployee, ouvertureIam: 'qualite.self' },
        loadComponent: () =>
          import('./features/reclamation/employee/reclamation-employee.component')
            .then(m => m.ReclamationEmployeeComponent),
      },
      {
        path: 'reclamations-admin',
        canActivate: [AuthGuard],
        data: { roles: ROLE_SETS.reclamationsAdmin, ouvertureIam: 'qualite.admin' },
        loadComponent: () =>
          import('./features/reclamation/admin/reclamation-admin.component')
            .then(m => m.ReclamationAdminComponent),
      },
      {
        path: 'qualite/cq',
        canActivate: [AuthGuard],
        data: { roles: [...ROLE_SETS.qualiteCq, ...ROLE_SETS.qualiteCqPilot], ouvertureIam: ['cq.evaluations', 'cq.coaching', 'cq.self', 'cq.parametres', 'cq.admin'] },
        loadComponent: () =>
          import('./features/qualite/qualite-cq-host.component')
            .then(m => m.QualiteCqHostComponent),
      },

      // ─── FORMATION ───────────────────────────────
      // Routes spécifiques AVANT `formations` (évite collision de préfixe).
      {
        path: 'formations/planifier',
        canActivate: [AuthGuard],
        data: { roles: ROLE_SETS.formationPlanner, ouvertureIam: 'formation.planification' },
        loadComponent: () =>
          import('./features/formation/rh/formation-rh-plan.component')
            .then(m => m.FormationRhPlanComponent),
      },
      {
        path: 'formations/dashboard',
        canActivate: [AuthGuard],
        data: { roles: ROLE_SETS.formationPlanner, ouvertureIam: 'formation.planification' },
        loadComponent: () =>
          import('./features/formation/dashboard/formation-dashboard.component')
            .then(m => m.FormationDashboardComponent),
      },
      {
        path: 'formations/initiales',
        canActivate: [AuthGuard],
        data: { roles: ROLE_SETS.formationFormateur, ouvertureIam: 'formation.suivi-initiale' },
        loadComponent: () =>
          import('./features/formation/formateur/formation-formateur-initial.component')
            .then(m => m.FormationFormateurInitialComponent),
      },
      {
        path: 'formations/passage-production',
        canActivate: [AuthGuard],
        data: { roles: ROLE_SETS.adminRh },
        loadComponent: () =>
          import('./features/formation/rh/formation-rh-prod-queue.component')
            .then(m => m.FormationRhProdQueueComponent),
      },
      {
        path: 'formations/bibliotheque',
        canActivate: [AuthGuard],
        data: { roles: ROLE_SETS.formationPlanner, ouvertureIam: 'formation.planification' },
        loadComponent: () =>
          import('./features/formation/catalog/formation-catalog-admin.component')
            .then(m => m.FormationCatalogAdminComponent),
      },
      {
        path: 'formations/catalogue',
        redirectTo: '/formations/bibliotheque',
        pathMatch: 'full',
      },
      {
        path: 'formations/documents-checklist-config',
        canActivate: [AuthGuard],
        data: { roles: ROLE_SETS.adminRh },
        loadComponent: () =>
          import('./features/formation/rh/formation-documents-config.component')
            .then(m => m.FormationDocumentsConfigComponent),
      },
      {
        path: 'formations/initiales/:pathId/checklist',
        canActivate: [AuthGuard],
        data: { roles: ROLE_SETS.formationChecklist },
        loadComponent: () =>
          import('./features/formation/rh/formation-path-checklist.component')
            .then(m => m.FormationPathChecklistComponent),
      },
      {
        path: 'formations',
        pathMatch: 'full',
        canActivate: [AuthGuard],
        data: { roles: ROLE_SETS.adminRh },
        loadComponent: () =>
          import('./features/formation/admin/formation-admin.component')
            .then(m => m.FormationAdminComponent),
      },
      {
        path: 'mes-sessions',
        canActivate: [AuthGuard],
        data: {
          roles: ROLE_SETS.mesSessions,
          ouvertureIam: 'formation.self',
        },
        loadComponent: () =>
          import('./features/formation/sessions/formation-mes-sessions.component')
            .then(m => m.FormationMesSessionsComponent),
      },
      {
        path: 'mes-sessions/:sessionId/quiz',
        canActivate: [AuthGuard],
        data: {
          roles: ROLE_SETS.mesSessions,
          ouvertureIam: 'formation.self',
        },
        loadComponent: () =>
          import('./features/formation/sessions/formation-session-quiz.component')
            .then(m => m.FormationSessionQuizComponent),
      },
      {
        path: 'mes-formations',
        canActivate: [AuthGuard],
        data: {
          roles: ROLE_SETS.mesFormations,
          ouvertureIam: 'formation.self',
        },
        loadComponent: () =>
          import('./features/formation/employee/formation-employee.component')
            .then(m => m.FormationEmployeeComponent),
      },
      {
        path: 'mes-formations/historique',
        canActivate: [AuthGuard],
        data: {
          roles: ROLE_SETS.mesFormations,
          ouvertureIam: 'formation.self',
        },
        loadComponent: () =>
          import('./features/formation/employee/formation-quiz-history.component')
            .then(m => m.FormationQuizHistoryComponent),
      },
      {
        path: 'mes-formations/contenu/:catalogItemId/quiz',
        canActivate: [AuthGuard],
        data: {
          roles: ROLE_SETS.mesFormations,
          ouvertureIam: 'formation.self',
        },
        loadComponent: () =>
          import('./features/formation/sessions/formation-take-quiz.component')
            .then(m => m.FormationTakeQuizComponent),
      },
      {
        path: 'mes-formations/contenu/:catalogItemId',
        canActivate: [AuthGuard],
        data: {
          roles: ROLE_SETS.mesFormations,
          ouvertureIam: 'formation.self',
        },
        loadComponent: () =>
          import('./features/formation/catalog/formation-catalog-player.component')
            .then(m => m.FormationCatalogPlayerComponent),
      },
      {
        path: 'mes-formations/:sessionId/contenu',
        canActivate: [AuthGuard],
        data: {
          roles: ROLE_SETS.mesFormations,
          ouvertureIam: 'formation.self',
        },
        loadComponent: () =>
          import('./features/formation/catalog/formation-catalog-player.component')
            .then(m => m.FormationCatalogPlayerComponent),
      },
      {
        path: 'mes-formations/:sessionId/quiz',
        canActivate: [AuthGuard],
        data: {
          roles: ROLE_SETS.mesFormations,
          ouvertureIam: 'formation.self',
        },
        loadComponent: () =>
          import('./features/formation/sessions/formation-take-quiz.component')
            .then(m => m.FormationTakeQuizComponent),
      },

      // ─── NEWSLETTER ──────────────────────────────
      {
        path: 'newsletter',
        canActivate: [AuthGuard],
        data: { roles: ROLE_SETS.adminRh, ouvertureIam: 'communication.newsletter' },
        component: NewsletterAdminComponent,
      },
      {
        path: 'mes-newsletters',
        canActivate: [AuthGuard],
        data: { roles: ROLE_SETS.allAuthenticated, ouvertureIam: 'communication.fil' },
        loadComponent: () =>
          import('./features/newsletter-inbox/my-newsletters-page.component')
            .then(m => m.MyNewslettersPageComponent),
      },

      // ─── ORGANISATION ────────────────────────────
      {
        // Organisation administrative (Admin / RH) : contrats, pôles, cellules, affectation
        // des agents et archives. « Supprimer » archive : rien n'est effacé.
        path: 'organisation/administrative',
        canActivate: [AuthGuard],
        data: { roles: ROLE_SETS.adminRh },
        loadComponent: () =>
          import('./features/organisation-admin/organisation-admin.component')
            .then((m) => m.OrganisationAdminComponent),
      },
      {
        // v32 : harmonisation sur le fichier de référence (Admin uniquement).
        path: 'organisation/harmonisation',
        canActivate: [AuthGuard],
        data: { roles: ['Admin'] },
        loadComponent: () =>
          import('./features/organisation-admin/harmonisation/organisation-harmonisation.component')
            .then((m) => m.OrganisationHarmonisationComponent),
      },
      {
        path: 'organisation',
        canActivate: [AuthGuard],
        data: { ouvertureIam: 'admin.organisation', roles: ROLE_SETS.adminRh },
        loadComponent: () =>
          import('./features/prime/pages/organisation-management.component')
            .then((m) => m.OrganisationManagementComponent),
      },
      {
        path: 'departements-metier',
        canActivate: [AuthGuard],
        data: { roles: ROLE_SETS.adminRh, ouvertureIam: 'admin.organisation' },
        loadComponent: () =>
          import('./features/prime/pages/allowances/business-departments-page.component')
            .then((m) => m.BusinessDepartmentsPageComponent),
      },
      {
        path: 'floors',
        redirectTo: '/organisation?tab=departments',
        pathMatch: 'prefix',
      },
      {
        path: 'services',
        redirectTo: '/organisation?tab=poles',
        pathMatch: 'prefix',
      },
      {
        path: 'sub-services',
        redirectTo: '/organisation?tab=cellules',
        pathMatch: 'prefix',
      },

      // ─── RH — Employés & Imports ─────────────────
      {
        path: 'pilotage-rh',
        canActivate: [AuthGuard],
        data: { roles: ROLE_SETS.adminRh, ouvertureIam: 'admin.users' },
        loadComponent: () =>
          import('./features/rh/pilotage-rh/pilotage-rh.component')
            .then(m => m.PilotageRhComponent),
      },
      {
        // Réaffectations (v21) et comptes (v26) : droits « rh.affectations » et « rh.activation ».
        path: 'reaffectations',
        canActivate: [AuthGuard],
        data: { roles: ROLE_SETS.congesEquipe, capabilities: ['rh.affectations', 'rh.activation'] },
        loadComponent: () =>
          import('./features/rh/reaffectations/reaffectations.component')
            .then(m => m.ReaffectationsComponent),
      },
      {
        path: 'users',
        canActivate: [AuthGuard],
        data: { ouvertureIam: 'admin.users', roles: ROLE_SETS.adminRh },
        loadChildren: () =>
          import('./features/users/users-routing-module')
            .then(m => m.UsersRoutingModule),
      },
      {
        path: 'import',
        canActivate: [AuthGuard],
        data: { roles: ROLE_SETS.adminRh, ouvertureIam: 'admin.users' },
        loadComponent: () =>
          import('./features/users/pages/employee-import-guided/employee-import-guided.component')
            .then(m => m.EmployeeImportGuidedComponent),
      },

      // ─── CONTRATS & CONGÉS ───────────────────────
      {
        path: 'contracts',
        canActivate: [AuthGuard],
        data: { roles: ROLE_SETS.congesManager, ouvertureIam: 'rh.contrats' },
        loadChildren: () =>
          import('./features/contract/contract-routing-module')
            .then(m => m.ContractRoutingModule),
      },
      {
        path: 'new-employees',
        redirectTo: 'users',
        pathMatch: 'full',
      },
      // Compat: anciennes URLs Congés / absences
      { path: 'conge', redirectTo: 'absences-planning', pathMatch: 'full' },
      { path: 'conge-gestion', redirectTo: '/conges/validation', pathMatch: 'full' },
      { path: 'conge-historique', redirectTo: '/conges/historique', pathMatch: 'full' },
      {
        path: 'absences-planning',
        canActivate: [AuthGuard],
        data: { roles: ROLE_SETS.congesManager, capability: 'conges.validation' },
        loadComponent: () =>
          import('./features/planning/pages/conge-manager/conge-manager.component')
            .then(m => m.CongeManagerComponent),
      },
      {
        // Présences et paie : tableau des présences (pointage + saisies), retards et autorisations.
        // Droits dans « Accès par rôle » ; chaque responsable ne voit que son périmètre (serveur).
        path: 'presences',
        children: [
          {
            path: '',
            pathMatch: 'full',
            canActivate: [AuthGuard],
            data: { roles: [...ROLE_SETS.congesEquipe, 'Formateur', 'Equipe_Formation'], capability: 'presences' },
            loadComponent: () =>
              import('./features/presences/pages/presences-tableau.component')
                .then(m => m.PresencesTableauComponent),
          },
          {
            path: 'retards',
            canActivate: [AuthGuard],
            data: { roles: ROLE_SETS.congesEquipe, capability: 'presences.retards' },
            loadComponent: () =>
              import('./features/presences/pages/presences-retards.component')
                .then(m => m.PresencesRetardsComponent),
          },
          {
            path: 'avertissements',
            canActivate: [AuthGuard],
            data: { roles: ROLE_SETS.congesEquipe, capability: 'presences.avertissements' },
            loadComponent: () =>
              import('./features/presences/pages/presences-avertissements.component')
                .then(m => m.PresencesAvertissementsComponent),
          },
          {
            path: 'evp',
            canActivate: [AuthGuard],
            data: { roles: ROLE_SETS.congesEquipe, capability: 'presences.evp' },
            loadComponent: () =>
              import('./features/presences/pages/presences-evp.component')
                .then(m => m.PresencesEvpComponent),
          },
          {
            path: 'dossiers',
            canActivate: [AuthGuard],
            data: { roles: ROLE_SETS.congesEquipe, capability: 'presences.dossier' },
            loadComponent: () =>
              import('./features/presences/pages/presences-dossiers.component')
                .then(m => m.PresencesDossiersComponent),
          },
          {
            path: 'export',
            canActivate: [AuthGuard],
            data: { roles: ROLE_SETS.congesEquipe, capability: 'presences.export' },
            loadComponent: () =>
              import('./features/presences/pages/presences-export.component')
                .then(m => m.PresencesExportComponent),
          },
        ],
      },
      {
        // Pointages ZKBio Time (lecture seule) : suivi personnel, équipe, correspondances RH.
        path: 'pointage',
        children: [
          {
            path: 'mon-suivi',
            canActivate: [AuthGuard],
            data: { ouvertureIam: 'pointage.self', roles: ROLE_SETS.planningSelfService },
            loadComponent: () =>
              import('./features/pointage/pages/pointage-suivi/pointage-suivi.component')
                .then(m => m.PointageSuiviComponent),
          },
          {
            path: 'collaborateurs/:id',
            canActivate: [AuthGuard],
            data: { ouvertureIam: 'pointage.equipe', roles: ROLE_SETS.congesEquipe },
            loadComponent: () =>
              import('./features/pointage/pages/pointage-suivi/pointage-suivi.component')
                .then(m => m.PointageSuiviComponent),
          },
          {
            path: 'equipe',
            canActivate: [AuthGuard],
            data: { ouvertureIam: 'pointage.equipe', roles: ROLE_SETS.congesEquipe },
            loadComponent: () =>
              import('./features/pointage/pages/pointage-equipe/pointage-equipe.component')
                .then(m => m.PointageEquipeComponent),
          },
          {
            path: 'correspondances',
            canActivate: [AuthGuard],
            data: { ouvertureIam: 'pointage.correspondances', roles: ROLE_SETS.adminRh },
            loadComponent: () =>
              import('./features/pointage/pages/pointage-correspondances/pointage-correspondances.component')
                .then(m => m.PointageCorrespondancesComponent),
          },
          {
            // Heure de sortie (Excel « Combien il me reste ») : accès accordé par l'Admin via le régime horaire.
            path: 'heure-de-sortie',
            canActivate: [AuthGuard, heureSortieGuard],
            data: { ouvertureIam: 'pointage.self', roles: ROLE_SETS.planningSelfService },
            loadComponent: () =>
              import('./features/pointage/pages/pointage-heure-sortie/pointage-heure-sortie.component')
                .then(m => m.PointageHeureSortieComponent),
          },
          {
            path: 'prevu-realise',
            canActivate: [AuthGuard],
            data: { ouvertureIam: 'pointage.equipe', roles: ROLE_SETS.congesEquipe },
            loadComponent: () =>
              import('./features/pointage/pages/pointage-prevu-realise/pointage-prevu-realise.component')
                .then(m => m.PointagePrevuRealiseComponent),
          },
          {
            // Accès « Heure de sortie » compte par compte (Admin).
            path: 'acces-heure-sortie',
            canActivate: [AuthGuard],
            data: { ouvertureIam: 'pointage.acces-sortie', roles: [KyntusRoleNames.Admin] },
            loadComponent: () =>
              import('./features/pointage/pages/heure-sortie-acces/heure-sortie-acces.component')
                .then(m => m.HeureSortieAccesComponent),
          },
          {
            path: 'regimes',
            canActivate: [AuthGuard],
            data: { ouvertureIam: 'pointage.regimes', roles: ROLE_SETS.adminRh },
            loadComponent: () =>
              import('./features/pointage/pages/regimes-horaires/regimes-horaires.component')
                .then(m => m.RegimesHorairesComponent),
          },
        ],
      },
      {
        path: 'conges',
        children: [
          {
            path: 'validation',
            canActivate: [AuthGuard],
            data: { roles: ROLE_SETS.congesManager, capability: 'conges.validation' },
            loadComponent: () =>
              import('./features/conge/pages/conge-manager/conge-manager.component')
                .then(m => m.CongeManagerComponent),
          },
          {
            // Congés de l'équipe : chaque responsable ne voit que son périmètre (contrôlé côté serveur).
            path: 'equipe',
            canActivate: [AuthGuard],
            data: { roles: ROLE_SETS.congesEquipe, ouvertureIam: 'conges.equipe' },
            loadComponent: () =>
              import('./features/conge/pages/conge-equipe/conge-equipe.component')
                .then(m => m.CongeEquipeComponent),
          },
          {
            // Congés de sa propre cellule : visible par tout collaborateur (cellule déduite côté serveur).
            path: 'ma-cellule',
            canActivate: [AuthGuard],
            data: { roles: ROLE_SETS.mesConges, capability: 'conges.self' },
            loadComponent: () =>
              import('./features/conge/pages/conge-cellule/conge-cellule.component')
                .then(m => m.CongeCelluleComponent),
          },
          {
            path: 'historique',
            canActivate: [AuthGuard],
            data: { roles: ROLE_SETS.congesRhConfig, capability: 'conges.history' },
            loadComponent: () =>
              import('./features/conge/pages/conge-historique/conge-historique.component')
                .then(m => m.CongeHistoriqueComponent),
          },
          {
            path: 'periodes-interdites',
            canActivate: [AuthGuard],
            data: { roles: ROLE_SETS.congesRhConfig, capability: 'conges.periodes' },
            loadComponent: () =>
              import('./features/conge/pages/conge-periodes-interdites/conge-periodes-interdites.component')
                .then(m => m.CongePeriodesInterditesComponent),
          },
          {
            path: 'quotas-service',
            canActivate: [AuthGuard],
            data: { roles: ROLE_SETS.congesSuperviseurConfig, capability: 'conges.quotas' },
            loadComponent: () =>
              import('./features/conge/pages/conge-quotas-service/conge-quotas-service.component')
                .then(m => m.CongeQuotasServiceComponent),
          },
        ],
      },
      {
        // Membres de l'équipe d'un responsable (périmètre du fichier Périmètre, contrôlé côté serveur).
        path: 'mon-equipe',
        canActivate: [AuthGuard],
        data: { ouvertureIam: 'planning.equipe', roles: ROLE_SETS.congesEquipe },
        loadComponent: () =>
          import('./features/users/pages/my-team/my-team.component')
            .then(m => m.MyTeamComponent),
      },
      {
        path: 'mes-conges',
        canActivate: [AuthGuard],
        data: { roles: ROLE_SETS.mesConges, capability: 'conges.self' },
        loadComponent: () =>
          import('./features/conge/pages/conge-employe/conge-employe.component')
            .then(m => m.CongeEmployeComponent),
      },

      // ─── PLANNING ────────────────────────────────
      {
        path: 'mes-plannings',
        canActivate: [AuthGuard],
        data: { ouvertureIam: 'planning.self', roles: ROLE_SETS.planningSelfService },
        loadComponent: () =>
          import('./features/planning/pages/mes-plannings/mes-plannings.component')
            .then(m => m.MesPlanningsComponent),
      },
      {
        path: 'mes-demandes-changement',
        canActivate: [AuthGuard],
        data: { ouvertureIam: 'planning.self', roles: ROLE_SETS.planningSelfService },
        loadComponent: () =>
          import('./features/planning/pages/mes-demandes-changement/mes-demandes-changement.component')
            .then(m => m.MesDemandesChangementComponent),
      },
      {
        path: 'mes-demandes-exceptionnelles',
        canActivate: [AuthGuard],
        data: { ouvertureIam: 'planning.self', roles: ROLE_SETS.planningSelfService },
        loadComponent: () =>
          import('./features/planning/pages/mes-demandes-exceptionnelles/mes-demandes-exceptionnelles.component')
            .then(m => m.MesDemandesExceptionnellesComponent),
      },
      {
        path: 'mes-renforts',
        canActivate: [AuthGuard],
        data: { ouvertureIam: 'planning.renforts-self', roles: ROLE_SETS.planningSelfService },
        loadComponent: () =>
          import('./features/planning/pages/mes-renforts/mes-renforts.component')
            .then(m => m.MesRenfortsComponent),
      },
      {
        path: 'planning',
        canActivate: [AuthGuard],
        data: { ouvertureIam: ['planning.self', 'planning.equipe', 'planning.change-requests', 'planning.exceptional-requests', 'planning.reinforcement', 'planning.generate', 'planning.validation', 'planning.shift-config', 'planning.publish'], roles: ROLE_SETS.planningManagers },
        loadChildren: () =>
          import('./features/planning/planning-routing-module')
            .then(m => m.PlanningRoutingModule),
      },

      // ─── DOCUMENTATION (microservice intégré, lazy) ──
      {
        path: 'documentation',
        canActivate: [AuthGuard],
        data: { roles: ROLE_SETS.documentation, moduleIam: 'documentation.module' },
        loadChildren: () =>
          import('./features/documentation/documentation.module')
            .then(m => m.DocumentationModule),
      },

      // ─── PRIME ───────────────────────────────────
      {
        path: 'prime',
        canActivate: [AuthGuard],
        data: { roles: ROLE_SETS.prime, moduleIam: 'prime.module' },
        loadChildren: () =>
          import('./features/prime/prime.routes').then((m) => m.PRIME_ROUTES),
      },

      // ─── PARRAINAGE ──────────────────────────────
      {
        path: 'parrainage',
        canActivate: [AuthGuard],
        data: { roles: ROLE_SETS.parrainage, moduleIam: 'parrainage.module' },
        loadChildren: () =>
          import('./features/parrainage/parrainage.routes').then((m) => m.PARRAINAGE_ROUTES),
      },

      { path: '', redirectTo: 'home', pathMatch: 'full' },
    ],
  },

  { path: '**', redirectTo: 'home' },
];
