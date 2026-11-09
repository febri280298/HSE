import { Routes } from '@angular/router';

import { permissionGuard } from '../../core/guards/auth.guard';
import { PERMISSIONS, TEMUAN_AKSES } from '../../core/models/auth.model';

export const TEMUAN_ROUTES: Routes = [
  {
    path: '',
    pathMatch: 'full',
    title: 'Daftar Temuan · HSE System',
    canActivate: [permissionGuard(...TEMUAN_AKSES)],
    loadComponent: () => import('./temuan-list/temuan-list.component').then((m) => m.TemuanListComponent),
  },
  {
    path: 'baru',
    title: 'Input Temuan · HSE System',
    canActivate: [permissionGuard(PERMISSIONS.temuanInput)],
    loadComponent: () => import('./temuan-form/temuan-form.component').then((m) => m.TemuanFormComponent),
  },
  {
    path: 'jadwal',
    title: 'Jadwal Genba · HSE System',
    loadComponent: () => import('./genba-jadwal/genba-jadwal.component').then((m) => m.GenbaJadwalComponent),
  },
  {
    path: ':id',
    title: 'Detail Temuan · HSE System',
    canActivate: [permissionGuard(...TEMUAN_AKSES)],
    loadComponent: () => import('./temuan-detail/temuan-detail.component').then((m) => m.TemuanDetailComponent),
  },
];
