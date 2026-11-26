import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';
import { AuthGuard } from '../../guard/guards/auth';

/**
 * v66 — Pages d'action des Utilisateurs : « Modifier » (admin.users) exigé, comme pour leurs entrées de
 * menu. La liste et la fiche restent ouvertes avec « Consulter » (garde du module parent).
 */
const ACTION = { ouvertureIam: 'admin.users', capabilityVerb: 'Act' as const };

const routes: Routes = [
  { path: '', loadComponent: () => import('./pages/user-list/user-list.component').then(m => m.UserListComponent) },
  { path: 'create', canActivate: [AuthGuard], data: ACTION, loadComponent: () => import('./pages/user-form/user-form.component').then(m => m.UserFormComponent) },
  { path: 'fields', canActivate: [AuthGuard], data: ACTION, loadComponent: () => import('./pages/employee-fields/employee-fields-page.component').then(m => m.EmployeeFieldsPageComponent) },
  {
    path: 'htel-liaisons',
    canActivate: [AuthGuard],
    data: ACTION,
    loadComponent: () =>
      import('./pages/htel-liaisons/htel-liaisons-page.component').then((m) => m.HtelLiaisonsPageComponent),
  },
  {
    // v63 — Fiches employés : fichier RH de référence, complétude, historique.
    path: 'fiches-reference',
    canActivate: [AuthGuard],
    data: ACTION,
    loadComponent: () =>
      import('./pages/fiches-reference/fiches-reference-page.component').then((m) => m.FichesReferencePageComponent),
  },
  {
    path: 'mails-internes',
    canActivate: [AuthGuard],
    data: ACTION,
    loadComponent: () =>
      import('./pages/mails-internes/mails-internes-page.component').then((m) => m.MailsInternesPageComponent),
  },
  { path: 'edit/:id', canActivate: [AuthGuard], data: ACTION, loadComponent: () => import('./pages/user-form/user-form.component').then(m => m.UserFormComponent) },
  {
    path: 'import',
    redirectTo: '/import',
    pathMatch: 'full',
  },
  { path: ':id', loadComponent: () => import('./pages/user-detail/user-detail.component').then(m => m.UserDetailComponent) },
];

@NgModule({
  imports: [RouterModule.forChild(routes)],
  exports: [RouterModule]
})
export class UsersRoutingModule {}