import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';
import { authGuard } from './auth/auth.guard';
import { LoginComponent } from './auth/login.component';
import { RegisterComponent } from './auth/register.component';
import { DashboardComponent } from './dashboards/dashboard.component';
import { DashboardsComponent } from './dashboards/dashboards.component';
import { DataSourcesComponent } from './organizations/data-sources.component';
import { DatasetsComponent } from './organizations/datasets.component';
import { OrganizationsComponent } from './organizations/organizations.component';

const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'organizations' },
  { path: 'login', component: LoginComponent },
  { path: 'register', component: RegisterComponent },
  { path: 'organizations', component: OrganizationsComponent, canActivate: [authGuard] },
  { path: 'organizations/:id', component: DataSourcesComponent, canActivate: [authGuard] },
  { path: 'organizations/:id/datasets', component: DatasetsComponent, canActivate: [authGuard] },
  { path: 'organizations/:id/dashboards', component: DashboardsComponent, canActivate: [authGuard] },
  { path: 'organizations/:id/dashboards/:dashboardId', component: DashboardComponent, canActivate: [authGuard] },
  { path: '**', redirectTo: 'organizations' },
];

@NgModule({
  imports: [RouterModule.forRoot(routes)],
  exports: [RouterModule]
})
export class AppRoutingModule { }
