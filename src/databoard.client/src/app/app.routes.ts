import { Routes } from '@angular/router';
import { authGuard } from './auth/auth.guard';

// Every page is loaded the first time it is visited, so the first load only carries the shell.
export const routes: Routes = [
  {
    path: '',
    pathMatch: 'full',
    canActivate: [authGuard],
    loadComponent: () => import('./home/home.component').then((m) => m.HomeComponent),
  },
  { path: 'login', loadComponent: () => import('./auth/login.component').then((m) => m.LoginComponent) },
  { path: 'register', loadComponent: () => import('./auth/register.component').then((m) => m.RegisterComponent) },
  {
    path: 'organizations',
    canActivate: [authGuard],
    loadComponent: () => import('./organizations/organizations.component').then((m) => m.OrganizationsComponent),
  },
  {
    path: 'organizations/:id',
    canActivate: [authGuard],
    loadComponent: () => import('./organizations/data-sources.component').then((m) => m.DataSourcesComponent),
  },
  {
    path: 'organizations/:id/datasets',
    canActivate: [authGuard],
    loadComponent: () => import('./organizations/datasets.component').then((m) => m.DatasetsComponent),
  },
  {
    path: 'organizations/:id/dashboards',
    canActivate: [authGuard],
    loadComponent: () => import('./dashboards/dashboards.component').then((m) => m.DashboardsComponent),
  },
  {
    path: 'organizations/:id/dashboards/:dashboardId',
    canActivate: [authGuard],
    // The same page; it opens that dashboard in a tab (this is the address "Open dashboard" links to).
    loadComponent: () => import('./dashboards/dashboards.component').then((m) => m.DashboardsComponent),
  },
  {
    // "new" starts an empty chart; otherwise the chart with that id is opened.
    path: 'organizations/:id/charts/:chartId',
    canActivate: [authGuard],
    loadComponent: () => import('./charts/chart-editor.component').then((m) => m.ChartEditorComponent),
  },
  { path: '**', redirectTo: '' },
];
