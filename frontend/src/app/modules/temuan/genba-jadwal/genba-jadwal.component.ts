import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { SelectModule } from 'primeng/select';
import { CellValueChangedEvent, ColDef, RowClassRules } from 'ag-grid-community';
import { firstValueFrom } from 'rxjs';

import { PERMISSIONS, PlantInfo } from '../../../core/models/auth.model';
import { AuthService } from '../../../core/services/auth.service';
import { ModalService } from '../../../core/services/modal.service';
import { NotifyService } from '../../../core/services/notify.service';
import { TemuanService } from '../../../core/services/temuan.service';
import { apiErrorMessage } from '../../../core/utils/api-error';
import { MONTH_NAMES, toIsoDate } from '../../../core/utils/date';
import { DataGridComponent } from '../../../shared/components/data-grid/data-grid.component';
import { GridAction, detailAction } from '../../../shared/components/data-grid/grid-actions-cell.component';
import { cellOf, chipCell, formatDay, isNarrowScreen } from '../../../shared/components/data-grid/grid-cells';
import { openDetail } from '../../../shared/components/detail-modal/detail-modal.component';

/** Satu baris = satu bulan; kolom `p<plantId>` berisi tanggal genba plant itu. */
type GenbaRow = { month: number } & Record<string, string | number | null>;

/** Jadwal genba: satu tanggal per plant per bulan. */
@Component({
  selector: 'app-genba-jadwal',
  imports: [FormsModule, SelectModule, DataGridComponent],
  templateUrl: './genba-jadwal.component.html',
})
export class GenbaJadwalComponent {
  private readonly temuan = inject(TemuanService);
  private readonly auth = inject(AuthService);
  private readonly toastr = inject(NotifyService);
  private readonly dialog = inject(ModalService);

  readonly years = Array.from({ length: 4 }, (_, i) => new Date().getFullYear() + 1 - i);
  private readonly currentMonth = new Date().getMonth() + 1;
  private readonly currentYear = new Date().getFullYear();
  private readonly narrow = isNarrowScreen();

  readonly year = signal(this.currentYear);
  readonly plants = signal<PlantInfo[]>([]);
  private readonly dates = signal(new Map<string, string>());
  readonly loading = signal(true);
  readonly canEdit = computed(() => {
    this.auth.user();
    return this.auth.can(PERMISSIONS.jadwalKelola);
  });

  readonly rowId = (row: GenbaRow) => String(row.month);
  readonly rowClasses: RowClassRules<GenbaRow> = {
    'grid-row-current': (p) => p.data?.month === this.currentMonth && this.year() === this.currentYear,
  };
  readonly actions: GridAction<GenbaRow>[] = [detailAction((row) => this.showDetail(row))];

  readonly rows = computed<GenbaRow[]>(() =>
    MONTH_NAMES.map((_, i) => {
      const month = i + 1;
      const row: GenbaRow = { month } as GenbaRow;
      for (const plant of this.plants()) {
        row[`p${plant.plantId}`] = this.dates().get(`${plant.plantId}-${month}`) ?? null;
      }
      return row;
    }),
  );

  readonly columns = computed<ColDef<GenbaRow>[]>(() => [
    {
      headerName: 'Bulan',
      colId: 'bulan',
      flex: 0,
      width: this.narrow ? 76 : 150,
      minWidth: this.narrow ? 76 : 130,
      pinned: 'left',
      filter: false,
      valueGetter: (p) => p.data?.month,
      cellRenderer: (p: { data?: GenbaRow }) =>
        p.data
          ? cellOf(
              MONTH_NAMES[p.data.month - 1] + ' ',
              // Di HP kolom ini sempit; barisnya tetap diberi warna.
              !this.narrow && p.data.month === this.currentMonth && this.year() === this.currentYear ? chipCell('Bulan ini', 'primary') : null,
            )
          : '',
    },
    ...this.plants().map(
      (plant): ColDef<GenbaRow> => ({
        headerName: plant.name,
        field: `p${plant.plantId}`,
        cellDataType: 'dateString',
        minWidth: 130,
        filter: false,
        editable: this.canEdit(),
        valueFormatter: (p) => formatDay(p.value as string | null) || '—',
        cellEditorParams: (p: { data?: GenbaRow }) => ({ min: this.minOf(p.data?.month ?? 1), max: this.maxOf(p.data?.month ?? 1) }),
      }),
    ),
  ]);

  constructor() {
    void this.init();
  }

  private async init(): Promise<void> {
    try {
      this.plants.set(await firstValueFrom(this.temuan.plants()));
      await this.load();
    } catch (err) {
      this.toastr.danger(apiErrorMessage(err), 'Jadwal Genba');
      this.loading.set(false);
    }
  }

  async changeYear(year: number): Promise<void> {
    this.year.set(year);
    await this.load();
  }

  async save(event: CellValueChangedEvent<GenbaRow>): Promise<void> {
    const plantId = Number(String(event.colDef.field).slice(1));
    const value = (event.newValue as string | null) || null;
    try {
      await firstValueFrom(this.temuan.saveGenba(plantId, this.year(), event.data.month, value));
      this.toastr.success(value ? 'Jadwal genba tersimpan.' : 'Jadwal genba dihapus.', 'Jadwal Genba');
    } catch (err) {
      this.toastr.danger(apiErrorMessage(err), 'Jadwal Genba');
    }
    await this.load();
  }

  private showDetail(row: GenbaRow): void {
    openDetail(
      this.dialog,
      `Jadwal Genba ${MONTH_NAMES[row.month - 1]} ${this.year()}`,
      this.plants().map((plant) => ({
        label: plant.name,
        value: formatDay(row[`p${plant.plantId}`] as string | null) || 'Belum dijadwalkan',
      })),
    );
  }

  private minOf(month: number): string {
    return toIsoDate(new Date(this.year(), month - 1, 1));
  }

  private maxOf(month: number): string {
    return toIsoDate(new Date(this.year(), month, 0));
  }

  private async load(): Promise<void> {
    this.loading.set(true);
    try {
      const rows = await firstValueFrom(this.temuan.genba(this.year()));
      this.dates.set(new Map(rows.map((r) => [`${r.plantId}-${r.month}`, r.plannedDate])));
    } catch (err) {
      this.toastr.danger(apiErrorMessage(err), 'Jadwal Genba');
    } finally {
      this.loading.set(false);
    }
  }
}
