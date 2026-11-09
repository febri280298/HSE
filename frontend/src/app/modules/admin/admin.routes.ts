import { Routes } from '@angular/router';

import { permissionGuard } from '../../core/guards/auth.guard';
import { PERMISSIONS } from '../../core/models/auth.model';

export const ADMIN_ROUTES: Routes = [
  {
    path: 'pengguna',
    title: 'Pengguna · HSE System',
    canActivate: [permissionGuard(PERMISSIONS.penggunaKelola)],
    loadComponent: () => import('./users/user-list.component').then((m) => m.UserListComponent),
  },
  {
    path: 'peran',
    title: 'Peran & Hak Akses · HSE System',
    canActivate: [permissionGuard(PERMISSIONS.peranKelola)],
    loadComponent: () => import('./roles/role-list.component').then((m) => m.RoleListComponent),
  },
  {
    path: 'master',
    title: 'Master Data · HSE System',
    canActivate: [permissionGuard(PERMISSIONS.masterKelola)],
    loadComponent: () => import('./master/master-data.component').then((m) => m.MasterDataComponent),
  },
];
