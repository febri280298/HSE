import { Component, computed, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { MessageModule } from 'primeng/message';
import { SelectModule } from 'primeng/select';
import { ColDef } from 'ag-grid-community';
import { firstValueFrom } from 'rxjs';

import { PERMISSIONS, PlantInfo, TEMUAN_AKSES } from '../../core/models/auth.model';
import {
  DashboardOverdueItem,
  DashboardResponse,
  FindingStatus,
  FindingType,
  GenbaSchedule,
  STATUS_INFO,
} from '../../core/models/temuan.model';
import { AuthService } from '../../core/services/auth.service';
import { ModalService } from '../../core/services/modal.service';
import { NotifyService } from '../../core/services/notify.service';
import { TemuanService } from '../../core/services/temuan.service';
import { apiErrorMessage } from '../../core/utils/api-error';
import { MONTH_NAMES } from '../../core/utils/date';
import { DataGridComponent } from '../../shared/components/data-grid/data-grid.component';
import { GridAction, detailAction } from '../../shared/components/data-grid/grid-actions-cell.component';
import { dateColumn, formatDay, formatPercent, meterCell } from '../../shared/components/data-grid/grid-cells';
import { openDetail } from '../../shared/components/detail-modal/detail-modal.component';
import { openFindingDetail } from '../temuan/temuan-detail-modal/temuan-detail-modal.component';
import { DeptTotals, byDepartment, monthlyOf, plantPanels, statusCounts, totalsOf } from './dashboard-data';
import { MonthlyChartComponent } from './monthly-chart.component';

interface GenbaNowRow {
  plantId: number;
  plantName: string;
  plannedDate: string | null;
  findings: number;
}

const ALL = 'semua';
const EMPTY: Omit<DashboardResponse, 'year'> = { rows: [], departments: [], statuses: [], overdue: [] };

/** Pengganti sheet PROGRESS & STATISTIC: angka selalu dari data temuan, tanpa rekap manual. */
@Component({
  selector: 'app-dashboard',
  imports: [
    FormsModule,
    RouterLink,
    ButtonModule,
    MessageModule,
    SelectModule,
    MonthlyChartComponent,
    DataGridComponent,
  ],
  templateUrl: './dashboard.component.html',
  styleUrl: './dashboard.component.scss',
})
export class DashboardComponent {
  private readonly auth = inject(AuthService);
  private readonly temuan = inject(TemuanService);
  private readonly toastr = inject(NotifyService);
  private readonly dialog = inject(ModalService);

  readonly deptColumns: ColDef<DeptTotals>[] = [
    { headerName: 'Departemen', field: 'deptName', minWidth: 115, flex: 2, filter: false },
    { headerName: 'Temuan', field: 'total', minWidth: 80, filter: false },
    {
      headerName: '% Close',
      field: 'percentClosed',
      minWidth: 90,
      flex: 2,
      filter: false,
      cellRenderer: (p: { value: number | null }) => meterCell(p.value),
    },
    { headerName: 'Overdue', field: 'overdue', minWidth: 88, filter: false },
  ];
  readonly deptActions: GridAction<DeptTotals>[] = [
    detailAction((dept) =>
      openDetail(this.dialog, dept.deptName, [
        { label: 'Kode', value: dept.deptCode },
        { label: 'Temuan', value: dept.total },
        { label: 'Close', value: dept.closed },
        { label: 'Belum close', value: dept.total - dept.closed },
        { label: 'Overdue', value: dept.overdue },
        { label: '% Close', value: formatPercent(dept.percentClosed) },
      ]),
    ),
  ];
  readonly deptRowId = (dept: DeptTotals) => dept.deptCode ?? 'belum';

  readonly genbaColumns: ColDef<GenbaNowRow>[] = [
    { headerName: 'Plant', field: 'plantName', minWidth: 100, flex: 2, filter: false },
    dateColumn('Tanggal', (r) => r.plannedDate, { filter: false, valueFormatter: (p) => formatDay(p.value) || '—' }),
    { headerName: 'Temuan', field: 'findings', minWidth: 80, filter: false },
  ];
  readonly genbaActions: GridAction<GenbaNowRow>[] = [
    detailAction((row) =>
      openDetail(this.dialog, `Genba ${this.currentMonthName} ${this.currentYear}`, [
        { label: 'Plant', value: row.plantName },
        { label: 'Tanggal', value: formatDay(row.plannedDate) || 'Belum dijadwalkan' },
        { label: 'Temuan bulan ini', value: row.findings },
      ]),
    ),
  ];
  readonly genbaRowId = (row: GenbaNowRow) => String(row.plantId);

  readonly overdueColumns: ColDef<DashboardOverdueItem>[] = [
    { headerName: 'No', field: 'number', minWidth: 175, flex: 0, width: 175 },
    {
      headerName: 'Telat',
      field: 'daysLate',
      minWidth: 100,
      flex: 0,
      width: 100,
      sort: 'desc',
      filter: 'agNumberColumnFilter',
      valueFormatter: (p) => `${p.value} hari`,
    },
    { headerName: 'Detail', field: 'detail', minWidth: 170, flex: 2, tooltipField: 'detail' },
    { headerName: 'Dept', minWidth: 85, valueGetter: (p) => p.data?.deptCode ?? '' },
    { headerName: 'PIC', minWidth: 130, valueGetter: (p) => p.data?.picName ?? '' },
    dateColumn('Due', (r) => r.dueDate),
  ];
  readonly overdueActions: GridAction<DashboardOverdueItem>[] = [detailAction((item) => openFindingDetail(this.dialog, item.findingId))];
  readonly overdueRowId = (item: DashboardOverdueItem) => String(item.findingId);

  /** `?akses=ditolak` dari permissionGuard. */
  readonly akses = input<string>();

  readonly all = ALL;
  readonly user = this.auth.user;
  readonly currentYear = new Date().getFullYear();
  readonly currentMonth = new Date().getMonth() + 1;
  readonly currentMonthName = MONTH_NAMES[this.currentMonth - 1];
  readonly years = Array.from({ length: 5 }, (_, i) => this.currentYear - i);
  readonly typeOptions = [
    { label: 'Safety & 5R', value: ALL },
    { label: 'Safety', value: 'Safety' },
    { label: '5R', value: '5R' },
  ];
  readonly year = signal(this.currentYear);
  readonly plantFilter = signal(0);
  readonly typeFilter = signal<string>(ALL);
  readonly data = signal(EMPTY);
  readonly plants = signal<PlantInfo[]>([]);
  readonly schedules = signal<GenbaSchedule[]>([]);
  readonly loading = signal(false);

  readonly canInput = computed(() => {
    this.user();
    return this.auth.can(PERMISSIONS.temuanInput);
  });
  readonly canSeeStats = computed(() => {
    this.user();
    return TEMUAN_AKSES.some((p) => this.auth.can(p));
  });

  private readonly matches = (row: { plantId: number; type: FindingType }) =>
    (!this.plantFilter() || row.plantId === this.plantFilter()) && (this.typeFilter() === ALL || row.type === this.typeFilter());

  /** Pilihan plant mengikuti data yang boleh dilihat pengguna. */
  readonly plantOptions = computed(() => {
    const seen = new Map(this.data().rows.map((r) => [r.plantId, r.plantName]));
    return [...seen].map(([plantId, plantName]) => ({ plantId, plantName }));
  });
  readonly plantChoices = computed(() => [
    { plantId: 0, plantName: 'Semua plant' },
    ...this.plantOptions(),
  ]);
  private readonly filtered = computed(() => this.data().rows.filter(this.matches));
  readonly totals = computed(() => totalsOf(this.filtered()));
  readonly monthly = computed(() => monthlyOf(this.filtered()));
  readonly statuses = computed(() => statusCounts(this.data().statuses.filter(this.matches)));
  readonly departments = computed(() => byDepartment(this.data().departments.filter(this.matches)));
  readonly panels = computed(() => {
    const types: FindingType[] = this.typeFilter() === ALL ? ['Safety', '5R'] : [this.typeFilter() as FindingType];
    return plantPanels(this.filtered(), types);
  });
  readonly overdue = computed(() => this.data().overdue.filter(this.matches));
  /** Server mengirim 100 temuan overdue terlama; sisanya ada di Daftar Temuan. */
  readonly overdueHidden = computed(() => Math.max(this.totals().overdue - this.overdue().length, 0));

  /** Jadwal genba bulan berjalan per plant beserta jumlah temuannya. */
  readonly genbaNow = computed(() =>
    this.plants()
      .filter((p) => !this.plantFilter() || p.plantId === this.plantFilter())
      .map((plant) => ({
        plantId: plant.plantId,
        plantName: plant.name,
        plannedDate: this.schedules().find((s) => s.plantId === plant.plantId && s.month === this.currentMonth)?.plannedDate ?? null,
        findings: this.data()
          .rows.filter((r) => r.plantId === plant.plantId && r.month === this.currentMonth)
          .reduce((n, r) => n + r.total, 0),
      })),
  );
  readonly showGenba = computed(() => this.year() === this.currentYear);

  constructor() {
    if (this.canSeeStats()) {
      void this.init();
    }
  }

  private async init(): Promise<void> {
    await this.load();
    try {
      const [plants, schedules] = await Promise.all([
        firstValueFrom(this.temuan.plants()),
        firstValueFrom(this.temuan.genba(this.currentYear)),
      ]);
      this.plants.set(plants);
      this.schedules.set(schedules);
    } catch (err) {
      this.toastr.danger(apiErrorMessage(err), 'Dashboard');
    }
  }

  changeYear(year: number): void {
    this.year.set(year);
    this.plantFilter.set(0);
    void this.load();
  }

  percent(value: number | null): string {
    return formatPercent(value);
  }

  statusLabel(status: FindingStatus): string {
    return STATUS_INFO[status].label;
  }

  private async load(): Promise<void> {
    this.loading.set(true);
    try {
      this.data.set(await firstValueFrom(this.temuan.dashboard(this.year())));
    } catch (err) {
      this.toastr.danger(apiErrorMessage(err), 'Dashboard');
    } finally {
      this.loading.set(false);
    }
  }
}
