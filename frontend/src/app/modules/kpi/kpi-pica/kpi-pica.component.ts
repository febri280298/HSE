import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { SelectModule } from 'primeng/select';
import { ColDef } from 'ag-grid-community';
import { firstValueFrom } from 'rxjs';

import { KPI_PICA_TYPES, KpiOptions, KpiPica } from '../../../core/models/kpi.model';
import { KpiService } from '../../../core/services/kpi.service';
import { ModalService } from '../../../core/services/modal.service';
import { NotifyService } from '../../../core/services/notify.service';
import { apiErrorMessage } from '../../../core/utils/api-error';
import { MONTH_NAMES } from '../../../core/utils/date';
import { ConfirmDialogComponent } from '../../../shared/components/confirm-dialog/confirm-dialog.component';
import { DataGridComponent } from '../../../shared/components/data-grid/data-grid.component';
import { GridAction, detailAction } from '../../../shared/components/data-grid/grid-actions-cell.component';
import { chipCell, dateColumn, formatDay } from '../../../shared/components/data-grid/grid-cells';
import { openDetail } from '../../../shared/components/detail-modal/detail-modal.component';
import { kpiYears } from '../kpi-format';
import { PicaFormDialogComponent } from './pica-form-dialog.component';

const typeLabel = (value: string) => KPI_PICA_TYPES.find((t) => t.value === value)?.label ?? value;

/** PICA (Problem Identification and Corrective Action): wajib kalau ada kasus penyakit atau kecelakaan. */
@Component({
  selector: 'app-kpi-pica',
  imports: [FormsModule, ButtonModule, SelectModule, DataGridComponent],
  templateUrl: './kpi-pica.component.html',
  styles: `
    .filters { display: flex; flex-wrap: wrap; align-items: center; gap: 0.5rem 0.75rem; margin-bottom: 1rem; }
  `,
})
export class KpiPicaComponent {
  private readonly kpi = inject(KpiService);
  private readonly toastr = inject(NotifyService);
  private readonly modal = inject(ModalService);

  readonly years = kpiYears();
  readonly options = signal<KpiOptions | null>(null);
  readonly year = signal(this.years[0]);
  readonly plantId = signal(0);
  readonly items = signal<KpiPica[]>([]);
  readonly loading = signal(true);

  readonly plantChoices = computed(() => [{ plantId: 0, name: 'Semua plant' }, ...(this.options()?.plants ?? [])]);
  /** Plant dan departemen yang boleh diisi pengguna ini. */
  readonly editable = computed(() => {
    const options = this.options();
    if (!options) {
      return [];
    }
    return options.canManage
      ? options.plants
      : options.plants
          .map((p) => ({ ...p, departments: p.departments.filter((d) => d.deptId === options.deptId) }))
          .filter((p) => p.departments.length);
  });

  readonly rowId = (row: KpiPica) => String(row.picaId);
  readonly actions: GridAction<KpiPica>[] = [
    detailAction((row) => this.showDetail(row)),
    { icon: 'pi pi-pencil', label: 'Ubah', visible: (row) => row.canEdit, run: (row) => this.openForm(row) },
    { icon: 'pi pi-trash', label: 'Hapus', status: 'danger', visible: (row) => row.canEdit, run: (row) => this.confirmDelete(row) },
  ];

  readonly columns: ColDef<KpiPica>[] = [
    { headerName: 'Bulan', minWidth: 90, flex: 0, width: 100, valueGetter: (p) => p.data?.month, valueFormatter: (p) => MONTH_NAMES[(p.value as number) - 1] },
    { headerName: 'Plant', field: 'plantName', minWidth: 120 },
    { headerName: 'Dept', field: 'deptCode', minWidth: 85, flex: 0, width: 95 },
    { headerName: 'KPI', minWidth: 170, valueGetter: (p) => (p.data ? typeLabel(p.data.kpiType) : '') },
    { headerName: 'Problem Issue', field: 'problemIssue', minWidth: 200, flex: 2, tooltipField: 'problemIssue' },
    { headerName: 'Corrective Action', field: 'correctiveAction', minWidth: 200, flex: 2, tooltipField: 'correctiveAction' },
    dateColumn('Due Date', (r) => r.dueDate),
    { headerName: 'PIC', field: 'picName', minWidth: 130 },
    {
      headerName: 'EVA',
      field: 'status',
      minWidth: 100,
      flex: 0,
      width: 105,
      cellRenderer: (p: { value: string }) => chipCell(p.value, p.value === 'Closed' ? 'success' : 'warning'),
    },
  ];

  constructor() {
    void this.init();
  }

  private async init(): Promise<void> {
    try {
      this.options.set(await firstValueFrom(this.kpi.options()));
    } catch (err) {
      this.toastr.danger(apiErrorMessage(err), 'PICA');
    }
    await this.load();
  }

  changeYear(year: number): void {
    this.year.set(year);
    void this.load();
  }

  changePlant(plantId: number): void {
    this.plantId.set(plantId);
    void this.load();
  }

  openForm(pica?: KpiPica): void {
    this.modal
      .open(PicaFormDialogComponent, { pica, plants: this.editable(), year: this.year() }, { closeOnBackdrop: false })
      ?.onClose.subscribe((saved?: boolean) => {
        if (saved) {
          this.toastr.success(pica ? 'PICA diperbarui.' : 'PICA ditambahkan.', 'PICA');
          void this.load();
        }
      });
  }

  private confirmDelete(pica: KpiPica): void {
    this.modal
      .open(
        ConfirmDialogComponent,
        { title: 'Hapus PICA', message: `Hapus PICA ${pica.deptCode} bulan ${MONTH_NAMES[pica.month - 1]}? Tindakan ini tidak bisa dibatalkan.`, confirmText: 'Hapus', status: 'danger' },
        { width: 'min(92vw, 28rem)' },
      )
      ?.onClose.subscribe(async (ok?: boolean) => {
        if (!ok) {
          return;
        }
        try {
          await firstValueFrom(this.kpi.deletePica(pica.picaId));
          this.toastr.success('PICA dihapus.', 'PICA');
          await this.load();
        } catch (err) {
          this.toastr.danger(apiErrorMessage(err), 'PICA');
        }
      });
  }

  private showDetail(pica: KpiPica): void {
    openDetail(this.modal, `PICA ${pica.deptName} · ${MONTH_NAMES[pica.month - 1]} ${pica.year}`, [
      { label: 'Plant', value: pica.plantName },
      { label: 'KPI', value: typeLabel(pica.kpiType) },
      { label: 'Due date', value: formatDay(pica.dueDate) },
      { label: 'PIC', value: pica.picName },
      { label: 'EVA', value: pica.status },
      { label: 'Problem issue', value: pica.problemIssue, wide: true },
      { label: 'Corrective action', value: pica.correctiveAction, wide: true },
    ]);
  }

  private async load(): Promise<void> {
    this.loading.set(true);
    try {
      this.items.set(await firstValueFrom(this.kpi.picas(this.year(), this.plantId())));
    } catch (err) {
      this.toastr.danger(apiErrorMessage(err), 'PICA');
    } finally {
      this.loading.set(false);
    }
  }
}
