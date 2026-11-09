import { Routes } from '@angular/router';

import { permissionGuard } from '../../core/guards/auth.guard';
import { KPI_AKSES } from '../../core/models/auth.model';

const canOpen = [permissionGuard(...KPI_AKSES)];
const documents = () => import('./kpi-dokumen/kpi-dokumen.component').then((m) => m.KpiDokumenComponent);

export const KPI_ROUTES: Routes = [
  {
    path: '',
    pathMatch: 'full',
    title: 'Monitoring KPI · HSE System',
    canActivate: canOpen,
    loadComponent: () => import('./kpi-monitoring/kpi-monitoring.component').then((m) => m.KpiMonitoringComponent),
  },
  {
    path: 'summary',
    title: 'Summary KPI · HSE System',
    canActivate: canOpen,
    loadComponent: () => import('./kpi-summary/kpi-summary.component').then((m) => m.KpiSummaryComponent),
  },
  { path: 'kalender', title: 'Kalender Safety · HSE System', canActivate: canOpen, data: { kind: 'Kalender' }, loadComponent: documents },
  { path: 'ppt', title: 'PPT P2K3L · HSE System', canActivate: canOpen, data: { kind: 'PPT' }, loadComponent: documents },
  {
    path: 'pica',
    title: 'PICA · HSE System',
    canActivate: canOpen,
    loadComponent: () => import('./kpi-pica/kpi-pica.component').then((m) => m.KpiPicaComponent),
  },
];
