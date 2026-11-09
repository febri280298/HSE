import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MessageModule } from 'primeng/message';
import { SelectModule } from 'primeng/select';
import { TabsModule } from 'primeng/tabs';
import { CellValueChangedEvent, ColDef, RowClassRules } from 'ag-grid-community';
import { firstValueFrom } from 'rxjs';

import { KPI_INFO, KpiOptions, KpiReviewRow, KpiRow, KpiRowFormat, KpiSummary } from '../../../core/models/kpi.model';
import { KpiService } from '../../../core/services/kpi.service';
import { ModalService } from '../../../core/services/modal.service';
import { NotifyService } from '../../../core/services/notify.service';
import { apiErrorMessage } from '../../../core/utils/api-error';
import { MONTH_NAMES } from '../../../core/utils/date';
import { DataGridComponent } from '../../../shared/components/data-grid/data-grid.component';
import { GridAction, detailAction } from '../../../shared/components/data-grid/grid-actions-cell.component';
import { chipCell, isNarrowScreen } from '../../../shared/components/data-grid/grid-cells';
import { openDetail } from '../../../shared/components/detail-modal/detail-modal.component';
import { kpiPercent, kpiValue, kpiYears } from '../kpi-format';

const ALL = 'all';

/** Baris grid: judul KPI (`group`) atau satu baris data dengan kolom `m1`-`m12`. */
type GridRow = Omit<KpiRow, 'format' | 'values' | 'texts'> & { format: KpiRowFormat | 'group' } & Record<string, unknown>;

/** Summary KPI seperti file master: tab per departemen, ALL menjumlahkan semuanya. Kolom kuning diisi departemen. */
@Component({
  selector: 'app-kpi-summary',
  imports: [FormsModule, MessageModule, SelectModule, TabsModule, DataGridComponent],
  templateUrl: './kpi-summary.component.html',
  styles: `
    .filters { display: flex; flex-wrap: wrap; align-items: center; gap: 0.5rem 0.75rem; margin-bottom: 1rem; }
    .tabs { display: block; margin-bottom: 1rem; }
    .review { margin-top: 1.5rem; }
  `,
})
export class KpiSummaryComponent {
  private readonly kpi = inject(KpiService);
  private readonly toastr = inject(NotifyService);
  private readonly modal = inject(ModalService);

  readonly years = kpiYears();
  readonly reviewMonths = MONTH_NAMES.map((label, i) => ({ label, value: i + 1 }));
  readonly options = signal<KpiOptions | null>(null);
  readonly year = signal(this.years[0]);
  readonly plantId = signal(0);
  readonly tab = signal<string>(ALL);
  readonly summary = signal<KpiSummary | null>(null);
  readonly loading = signal(true);

  readonly plants = computed(() => this.options()?.plants ?? []);
  readonly tabs = computed(() => [
    { value: ALL, label: 'ALL' },
    ...(this.plants().find((p) => p.plantId === this.plantId())?.departments ?? []).map((d) => ({ value: String(d.deptId), label: d.code })),
  ]);
  readonly isAll = computed(() => this.tab() === ALL);

  /** Id memuat plant, departemen, dan tahun: pindah tab membuat baris baru, jadi warna kolom kuning ikut dihitung ulang. */
  readonly rowId = (row: GridRow) => {
    const summary = this.summary();
    return `${summary?.plantId}/${summary?.deptId ?? ALL}/${summary?.year}/${row.key}`;
  };
  readonly reviewRowId = (row: KpiReviewRow) => String(row.no);
  readonly rowClasses: RowClassRules<GridRow> = { 'kpi-group': (p) => p.data?.format === 'group' };

  readonly rows = computed<GridRow[]>(() => {
    const out: GridRow[] = [];
    let group = 0;
    for (const row of this.summary()?.rows ?? []) {
      if (row.kpiNo !== group) {
        group = row.kpiNo;
        out.push({ key: `group-${group}`, kpiNo: group, kpi: row.kpi, label: `${group}. ${row.kpi}`, format: 'group', field: null });
      }
      const months = Object.fromEntries(
        Array.from({ length: 12 }, (_, i) => [`m${i + 1}`, row.format === 'eval' || row.format === 'text' ? row.texts[i] : row.values[i]]),
      );
      out.push({ key: row.key, kpiNo: row.kpiNo, kpi: row.kpi, label: row.label, format: row.format, field: row.field, ...months });
    }
    return out;
  });

  readonly actions: GridAction<GridRow>[] = [{ ...detailAction((row) => this.showKpi(row)), visible: (row) => row.format === 'group' }];
  readonly columns: ColDef<GridRow>[] = this.buildColumns();

  readonly reviewColumns: ColDef<KpiReviewRow>[] = [
    { headerName: 'No', field: 'no', flex: 0, width: 70, minWidth: 70, sortable: false, filter: false },
    { headerName: 'KPI', field: 'kpi', minWidth: 210, flex: 2, sortable: false, filter: false },
    { headerName: 'Target', field: 'target', minWidth: 170, sortable: false, filter: false },
    { headerName: 'Pencapaian bulan ini', field: 'thisMonth', minWidth: 230, flex: 2, sortable: false, filter: false },
    { headerName: 'Year to End (ACC)', field: 'yearToEndAcc', minWidth: 170, sortable: false, filter: false },
    {
      headerName: 'Year to End (%)',
      field: 'yearToEndPercent',
      minWidth: 130,
      sortable: false,
      filter: false,
      valueFormatter: (p) => kpiPercent(p.value),
    },
    {
      headerName: 'Status',
      field: 'achieved',
      minWidth: 110,
      flex: 0,
      width: 120,
      sortable: false,
      filter: false,
      cellRenderer: (p: { value: boolean | null }) => (p.value === null ? '—' : p.value ? chipCell('Tercapai', 'success') : chipCell('Belum', 'danger')),
    },
  ];
  readonly reviewActions: GridAction<KpiReviewRow>[] = [
    detailAction((row) =>
      openDetail(this.modal, row.kpi, [
        { label: 'Target', value: row.target },
        { label: 'Pencapaian bulan ini', value: row.thisMonth },
        { label: 'Year to End (ACC)', value: row.yearToEndAcc },
        { label: 'Year to End (%)', value: kpiPercent(row.yearToEndPercent) },
        { label: 'Status', value: row.achieved === null ? '—' : row.achieved ? 'Tercapai' : 'Belum tercapai' },
        { label: 'Definisi', value: KPI_INFO[row.no].definisi, wide: true },
      ]),
    ),
  ];

  constructor() {
    void this.init();
  }

  private buildColumns(): ColDef<GridRow>[] {
    const narrow = isNarrowScreen();
    return [
      {
        headerName: 'Item',
        field: 'label',
        tooltipField: 'label',
        pinned: narrow ? undefined : 'left',
        flex: 0,
        width: narrow ? 170 : 230,
        minWidth: narrow ? 170 : 230,
        sortable: false,
        filter: false,
        suppressMovable: true,
      },
      ...MONTH_NAMES.map((name, i): ColDef<GridRow> => {
        const month = i + 1;
        return {
          headerName: name,
          colId: `m${month}`,
          field: `m${month}`,
          cellDataType: false,
          minWidth: 72,
          sortable: false,
          filter: false,
          suppressMovable: true,
          editable: (p) => this.isEditable(p.data, month),
          cellClass: (p) => (this.isEditable(p.data, month) ? 'kpi-input' : undefined),
          valueFormatter: (p) => (!p.data || p.data.format === 'group' ? '' : kpiValue(p.data.format, p.value as number | string | null)),
          tooltipValueGetter: (p) => (p.data?.format === 'text' ? (p.value as string) : undefined),
          cellRendererSelector: (p) =>
            p.data?.format === 'eval'
              ? { component: (c: { value: string | null }) => (c.value ? chipCell(c.value, c.value === 'OK' ? 'success' : 'danger') : '') }
              : undefined,
          cellEditorSelector: (p) =>
            p.data?.format === 'text'
              ? { component: 'agTextCellEditor', params: { maxLength: 200 } }
              : { component: 'agNumberCellEditor', params: { min: 0, max: 999, precision: 0 } },
        };
      }),
    ];
  }

  private isEditable(row: GridRow | undefined, month: number): boolean {
    const summary = this.summary();
    return !!row?.field && !!summary?.canEdit && month <= summary.lastMonth;
  }

  private async init(): Promise<void> {
    try {
      const options = await firstValueFrom(this.kpi.options());
      this.options.set(options);
      const own = options.plants.find((p) => p.departments.some((d) => d.deptId === options.deptId));
      const plant = own ?? options.plants[0];
      if (!plant) {
        this.loading.set(false);
        return;
      }
      this.plantId.set(plant.plantId);
      this.tab.set(own && options.deptId ? String(options.deptId) : ALL);
      await this.load();
    } catch (err) {
      this.toastr.danger(apiErrorMessage(err), 'KPI');
      this.loading.set(false);
    }
  }

  changeYear(year: number): void {
    this.year.set(year);
    void this.load();
  }

  changePlant(plantId: number): void {
    this.plantId.set(plantId);
    if (!this.tabs().some((t) => t.value === this.tab())) {
      this.tab.set(ALL);
    }
    void this.load();
  }

  selectTab(value: string | number | undefined): void {
    this.tab.set(String(value ?? ALL));
    void this.load();
  }

  changeReviewMonth(month: number): void {
    void this.load(month);
  }

  private deptId(): number | null {
    return this.tab() === ALL ? null : Number(this.tab());
  }

  async save(event: CellValueChangedEvent<GridRow>): Promise<void> {
    const field = event.data.field;
    const deptId = this.deptId();
    if (!field || deptId === null) {
      return;
    }
    const month = Number(String(event.colDef.field).slice(1));
    try {
      await firstValueFrom(this.kpi.saveInput(this.plantId(), deptId, this.year(), month, field, event.newValue == null ? '' : String(event.newValue)));
    } catch (err) {
      this.toastr.danger(apiErrorMessage(err), 'KPI');
    }
    await this.load(this.summary()?.reviewMonth);
  }

  private async load(reviewMonth?: number): Promise<void> {
    if (!this.plantId()) {
      return;
    }
    this.loading.set(true);
    try {
      this.summary.set(await firstValueFrom(this.kpi.summary(this.year(), this.plantId(), this.deptId(), reviewMonth)));
    } catch (err) {
      this.toastr.danger(apiErrorMessage(err), 'KPI');
    } finally {
      this.loading.set(false);
    }
  }

  private showKpi(row: GridRow): void {
    const info = KPI_INFO[row.kpiNo];
    openDetail(this.modal, row.kpi, [
      { label: 'Perspective', value: 'Internal Process' },
      { label: 'Target', value: info.target },
      { label: 'Spesifikasi', value: info.spesifikasi },
      { label: 'Definisi', value: info.definisi, wide: true },
    ]);
  }
}
