import { Routes } from '@angular/router';

import { authGuard, guestGuard, signedInGuard } from './core/guards/auth.guard';
import { LayoutComponent } from './layout/layout.component';

export const routes: Routes = [
  {
    path: 'login',
    title: 'Masuk · HSE System',
    canActivate: [guestGuard],
    loadComponent: () => import('./modules/auth/login/login.component').then((m) => m.LoginComponent),
  },
  {
    path: 'ganti-password',
    title: 'Ganti Password · HSE System',
    canActivate: [signedInGuard],
    loadComponent: () =>
      import('./modules/auth/change-password/change-password.component').then((m) => m.ChangePasswordComponent),
  },
  {
    path: '',
    component: LayoutComponent,
    canActivate: [authGuard],
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
      {
        path: 'dashboard',
        title: 'Dashboard · HSE System',
        loadComponent: () =>
          import('./modules/dashboard/dashboard.component').then((m) => m.DashboardComponent),
      },
      {
        path: 'temuan',
        loadChildren: () => import('./modules/temuan/temuan.routes').then((m) => m.TEMUAN_ROUTES),
      },
      {
        path: 'kpi',
        loadChildren: () => import('./modules/kpi/kpi.routes').then((m) => m.KPI_ROUTES),
      },
      {
        path: 'admin',
        loadChildren: () => import('./modules/admin/admin.routes').then((m) => m.ADMIN_ROUTES),
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
