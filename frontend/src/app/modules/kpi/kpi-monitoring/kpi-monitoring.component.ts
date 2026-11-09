import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { SelectModule } from 'primeng/select';
import { ColDef } from 'ag-grid-community';
import { firstValueFrom } from 'rxjs';

import { KpiMonitoring, KpiMonitoringPlant } from '../../../core/models/kpi.model';
import { KpiService } from '../../../core/services/kpi.service';
import { ModalService } from '../../../core/services/modal.service';
import { NotifyService } from '../../../core/services/notify.service';
import { apiErrorMessage } from '../../../core/utils/api-error';
import { MONTH_NAMES } from '../../../core/utils/date';
import { DataGridComponent } from '../../../shared/components/data-grid/data-grid.component';
import { GridAction, detailAction } from '../../../shared/components/data-grid/grid-actions-cell.component';
import { chipCell } from '../../../shared/components/data-grid/grid-cells';
import { openDetail } from '../../../shared/components/detail-modal/detail-modal.component';
import { kpiPercent, kpiYears } from '../kpi-format';

type StatusKey = 'hasInput' | 'hasKalender' | 'hasPpt';

/** Ringkasan KPI K3 tiap plant dan kelengkapan laporan departemen per bulan. */
@Component({
  selector: 'app-kpi-monitoring',
  imports: [FormsModule, SelectModule, DataGridComponent],
  templateUrl: './kpi-monitoring.component.html',
  styles: `
    .filters { display: flex; flex-wrap: wrap; align-items: center; gap: 0.5rem 0.75rem; margin-bottom: 1.5rem; }
    .tiles { display: grid; grid-template-columns: repeat(auto-fit, minmax(11rem, 1fr)); gap: 1.5rem; margin-bottom: 1.5rem; }
    .tile { margin: 0; padding: 1.25rem 1.5rem; }
    .tile-label { display: block; margin-bottom: 0.75rem; font-weight: 500; color: var(--text-color-secondary); }
    .tile-value { font-size: 1.75rem; font-weight: 700; letter-spacing: -0.02em; line-height: 1.2; }
    .tile-note { display: block; margin-top: 0.375rem; font-size: 0.875rem; color: var(--text-color-secondary); }
  `,
})
export class KpiMonitoringComponent {
  private readonly kpi = inject(KpiService);
  private readonly toastr = inject(NotifyService);
  private readonly modal = inject(ModalService);

  readonly years = kpiYears();
  readonly months = MONTH_NAMES.map((label, i) => ({ label, value: i + 1 }));
  readonly year = signal(this.years[0]);
  readonly month = signal<number | undefined>(undefined);
  readonly data = signal<KpiMonitoring | null>(null);
  readonly loading = signal(true);

  readonly plants = computed(() => this.data()?.plants ?? []);
  readonly monthName = computed(() => MONTH_NAMES[(this.data()?.month ?? 1) - 1]);
  readonly totals = computed(() => {
    const plants = this.plants();
    const findings = plants.reduce((n, p) => n + p.findings, 0);
    const closed = plants.reduce((n, p) => n + p.closed, 0);
    const departments = plants.flatMap((p) => p.departments);
    return {
      accidents: plants.reduce((n, p) => n + p.accidents, 0),
      diseaseCases: plants.reduce((n, p) => n + p.diseaseCases, 0),
      findings,
      closed,
      percentClosed: findings ? closed / findings : null,
      departments: departments.length,
      complete: departments.filter((d) => d.hasInput && d.hasKalender && d.hasPpt).length,
    };
  });
  readonly percent = kpiPercent;

  readonly rowId = (row: KpiMonitoringPlant) => String(row.plantId);
  readonly actions: GridAction<KpiMonitoringPlant>[] = [detailAction((row) => this.showDetail(row))];
  readonly columns: ColDef<KpiMonitoringPlant>[] = [
    { headerName: 'Plant', field: 'plantName', minWidth: 140, flex: 2, filter: false },
    { headerName: 'Accident', field: 'accidents', minWidth: 95, filter: false },
    { headerName: 'Kasus Penyakit', field: 'diseaseCases', minWidth: 125, filter: false },
    { headerName: 'Rata-rata', field: 'diseaseAverage', minWidth: 95, filter: false },
    { headerName: 'Temuan', field: 'findings', minWidth: 90, filter: false },
    { headerName: 'Close', field: 'closed', minWidth: 85, filter: false },
    { headerName: '% Close', field: 'percentClosed', minWidth: 90, filter: false, valueFormatter: (p) => kpiPercent(p.value) },
    this.progressColumn('Summary', 'hasInput'),
    this.progressColumn('Kalender', 'hasKalender'),
    this.progressColumn('PPT', 'hasPpt'),
  ];

  constructor() {
    void this.load();
  }

  private progressColumn(headerName: string, key: StatusKey): ColDef<KpiMonitoringPlant> {
    return {
      headerName,
      colId: key,
      minWidth: 105,
      filter: false,
      sortable: false,
      valueGetter: (p) => (p.data ? p.data.departments.filter((d) => d[key]).length : 0),
      cellRenderer: (p: { data?: KpiMonitoringPlant; value: number }) => {
        const total = p.data?.departments.length ?? 0;
        return chipCell(`${p.value}/${total}`, total > 0 && p.value === total ? 'success' : p.value > 0 ? 'warning' : 'danger');
      },
    };
  }

  changeYear(year: number): void {
    this.year.set(year);
    void this.load();
  }

  changeMonth(month: number): void {
    this.month.set(month);
    void this.load();
  }

  private showDetail(plant: KpiMonitoringPlant): void {
    const missing = (key: StatusKey) => plant.departments.filter((d) => !d[key]).map((d) => d.deptName);
    openDetail(this.modal, `${plant.plantName} · ${this.monthName()} ${this.data()?.year}`, [
      { label: 'Accident (setahun)', value: plant.accidents },
      { label: 'Kasus penyakit (setahun)', value: plant.diseaseCases },
      { label: 'Temuan close', value: `${plant.closed} dari ${plant.findings}` },
      { label: 'Belum isi Summary KPI', items: missing('hasInput'), wide: true },
      { label: 'Belum unggah Kalender Safety', items: missing('hasKalender'), wide: true },
      { label: 'Belum unggah PPT', items: missing('hasPpt'), wide: true },
    ]);
  }

  private async load(): Promise<void> {
    this.loading.set(true);
    try {
      const data = await firstValueFrom(this.kpi.monitoring(this.year(), this.month()));
      this.data.set(data);
      this.month.set(data.month);
    } catch (err) {
      this.toastr.danger(apiErrorMessage(err), 'KPI');
    } finally {
      this.loading.set(false);
    }
  }
}
