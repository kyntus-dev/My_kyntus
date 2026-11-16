import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';

const routes: Routes = [
  { path: '', loadComponent: () => import('./pages/user-list/user-list.component').then(m => m.UserListComponent) },
  { path: 'create', loadComponent: () => import('./pages/user-form/user-form.component').then(m => m.UserFormComponent) },
  { path: 'fields', loadComponent: () => import('./pages/employee-fields/employee-fields-page.component').then(m => m.EmployeeFieldsPageComponent) },
  {
    path: 'htel-liaisons',
    loadComponent: () =>
      import('./pages/htel-liaisons/htel-liaisons-page.component').then((m) => m.HtelLiaisonsPageComponent),
  },
  {
    path: 'mails-internes',
    loadComponent: () =>
      import('./pages/mails-internes/mails-internes-page.component').then((m) => m.MailsInternesPageComponent),
  },
  { path: 'edit/:id', loadComponent: () => import('./pages/user-form/user-form.component').then(m => m.UserFormComponent) },
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