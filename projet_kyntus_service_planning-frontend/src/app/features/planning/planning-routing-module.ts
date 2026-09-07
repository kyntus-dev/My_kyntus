// features/planning/planning-routing-module.ts

import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';
import { AuthGuard } from '../../guard/guards/auth';

const routes: Routes = [
  {
    path: '',
    pathMatch: 'full',
    redirectTo: 'validation',
  },
  {
    path: 'generate',
    canActivate: [AuthGuard],
    data: { roles: ['Admin', 'RH'], capability: 'planning.generate' },
    loadComponent: () =>
      import('./pages/planning-generate/planning-generate.component')
        .then(m => m.PlanningGenerateComponent)
  },
  {
    path: 'validation',
    canActivate: [AuthGuard],
    data: { roles: ['Admin', 'RH', 'Superviseur'], capability: 'planning.validation' },
    loadComponent: () =>
      import('./pages/planning-validation/planning-validation.component')
        .then(m => m.PlanningValidationComponent),
  },
  {
    path: 'shift-config',
    canActivate: [AuthGuard],
    data: { roles: ['Admin', 'RH', 'Superviseur'], capability: 'planning.shift-config' },
    loadComponent: () =>
      import('./pages/shift-config/shift-config.component')
        .then(m => m.ShiftConfigComponent)
  },
  {
    path: 'view/:id',
    loadComponent: () =>
      import('./pages/planning-view/planning-view.component')
        .then(m => m.PlanningViewComponent)
  },
  {
    path: 'conges',
    redirectTo: '/absences-planning',
    pathMatch: 'full',
  },
  {
    path: 'equipe',
    canActivate: [AuthGuard],
    data: { roles: ['Manager', 'Coach', 'Référent technique', 'Superviseur', 'Admin', 'RH'] },
    loadComponent: () =>
      import('./pages/planning-equipe/planning-equipe.component')
        .then(m => m.PlanningEquipeComponent),
  },
  {
    path: 'change-requests',
    canActivate: [AuthGuard],
    data: {
      roles: [
        'Admin',
        'RH',
        'Superviseur',
        'Manager',
        'Référent technique',
        'Coach',
        'Chef de projet',
        'RP',
      ],
    },
    loadComponent: () =>
      import('./pages/planning-change-requests/planning-change-requests.component')
        .then(m => m.PlanningChangeRequestsComponent),
  },
  {
    path: 'exceptional-requests',
    canActivate: [AuthGuard],
    data: {
      roles: [
        'Admin',
        'RH',
        'Superviseur',
        'Manager',
        'Référent technique',
        'Coach',
        'Chef de projet',
        'RP',
      ],
    },
    loadComponent: () =>
      import('./pages/planning-exceptional-requests/planning-exceptional-requests.component')
        .then(m => m.PlanningExceptionalRequestsComponent),
  },
  {
    path: 'demandes-renfort',
    canActivate: [AuthGuard],
    data: {
      roles: [
        'Admin',
        'RH',
        'Superviseur',
        'Manager',
        'Référent technique',
        'Coach',
        'Chef de projet',
        'RP',
      ],
    },
    loadComponent: () =>
      import('./pages/planning-reinforcement-requests/planning-reinforcement-requests.component')
        .then(m => m.PlanningReinforcementRequestsComponent),
  },
];

@NgModule({
  imports: [RouterModule.forChild(routes)],
  exports: [RouterModule]
})
export class PlanningRoutingModule {}
