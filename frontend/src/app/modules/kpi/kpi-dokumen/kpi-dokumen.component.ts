import { Component, ElementRef, computed, inject, input, signal, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { MessageModule } from 'primeng/message';
import { SelectModule } from 'primeng/select';
import { ColDef } from 'ag-grid-community';
import { firstValueFrom } from 'rxjs';

import { KpiDocumentKind, KpiDocumentRow, KpiDocuments, KpiPlantOption } from '../../../core/models/kpi.model';
import { KpiService } from '../../../core/services/kpi.service';
import { ModalService } from '../../../core/services/modal.service';
import { NotifyService } from '../../../core/services/notify.service';
import { apiErrorMessage } from '../../../core/utils/api-error';
import { MONTH_NAMES } from '../../../core/utils/date';
import { DataGridComponent } from '../../../shared/components/data-grid/data-grid.component';
import { GridAction, detailAction } from '../../../shared/components/data-grid/grid-actions-cell.component';
import { formatDateTime, isNarrowScreen } from '../../../shared/components/data-grid/grid-cells';
import { openDetail } from '../../../shared/components/detail-modal/detail-modal.component';
import { kpiYears } from '../kpi-format';

const INFO: Record<KpiDocumentKind, { title: string; accept: string; hint: string; button: string }> = {
  Kalender: {
    title: 'Kalender Safety',
    accept: 'application/pdf,.pdf',
    hint: 'Wajib PDF, sudah disetujui sampai atasan, dan dikumpulkan setiap bulan.',
    button: 'Pilih PDF',
  },
  PPT: {
    title: 'PPT P2K3L',
    accept: '.pptx,.ppt',
    hint: 'Wajib memakai format PPT dari tim HSE. Buat PICA kalau ada kasus penyakit atau kecelakaan.',
    button: 'Pilih PPT',
  },
};

/** Satu halaman untuk Kalender Safety dan PPT P2K3L: tabel kelengkapan per departemen dan form unggah. */
@Component({
  selector: 'app-kpi-dokumen',
  imports: [FormsModule, ButtonModule, MessageModule, SelectModule, DataGridComponent],
  templateUrl: './kpi-dokumen.component.html',
  styles: `
    .filters, .upload { display: flex; flex-wrap: wrap; align-items: center; gap: 0.5rem 0.75rem; }
    .filters { margin-bottom: 1rem; }
    .upload { margin-bottom: 1rem; padding: 0.875rem 1rem; border: 1px dashed var(--surface-border); border-radius: 0.5rem; }
    .file-name { max-width: 18rem; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; color: var(--text-color-secondary); }
  `,
})
export class KpiDokumenComponent {
  private readonly kpi = inject(KpiService);
  private readonly toastr = inject(NotifyService);
  private readonly modal = inject(ModalService);
  private readonly fileInput = viewChild<ElementRef<HTMLInputElement>>('picker');

  /** Dari `data` rute. */
  readonly kind = input.required<KpiDocumentKind>();

  readonly info = computed(() => INFO[this.kind()]);
  readonly years = kpiYears();
  readonly plants = signal<KpiPlantOption[]>([]);
  readonly year = signal(this.years[0]);
  readonly plantId = signal(0);
  readonly data = signal<KpiDocuments | null>(null);
  readonly loading = signal(true);
  readonly busy = signal(false);

  readonly uploadDept = signal<number | null>(null);
  readonly uploadMonth = signal<number | null>(null);
  readonly file = signal<File | null>(null);

  readonly uploadable = computed(() => this.data()?.departments.filter((d) => d.canUpload) ?? []);
  readonly monthChoices = computed(() =>
    MONTH_NAMES.slice(0, this.data()?.lastMonth ?? 0).map((label, i) => ({ label, value: i + 1 })),
  );
  readonly canSubmit = computed(() => !!this.file() && !!this.uploadDept() && !!this.uploadMonth() && !this.busy());

  readonly rowId = (row: KpiDocumentRow) => String(row.deptId);
  readonly actions: GridAction<KpiDocumentRow>[] = [detailAction((row) => this.showDetail(row))];
  readonly columns: ColDef<KpiDocumentRow>[] = this.buildColumns();

  constructor() {
    void this.init();
  }

  private buildColumns(): ColDef<KpiDocumentRow>[] {
    return [
      {
        headerName: 'Departemen',
        field: 'deptName',
        pinned: isNarrowScreen() ? undefined : 'left',
        flex: 0,
        width: 160,
        minWidth: 140,
        filter: false,
      },
      ...MONTH_NAMES.map((name, i): ColDef<KpiDocumentRow> => {
        const month = i + 1;
        return {
          headerName: name,
          colId: `m${month}`,
          minWidth: 66,
          sortable: false,
          filter: false,
          valueGetter: (p) => p.data?.files.find((f) => f.month === month)?.fileName ?? '',
          tooltipValueGetter: (p) => (p.value as string) || undefined,
          cellRenderer: (p: { data?: KpiDocumentRow }) => this.fileCell(p.data, month),
        };
      }),
    ];
  }

  /** Ikon centang yang langsung mengunduh file; kosong kalau belum ada. */
  private fileCell(row: KpiDocumentRow | undefined, month: number): HTMLElement | string {
    const file = row?.files.find((f) => f.month === month);
    if (!file) {
      const empty = document.createElement('span');
      empty.className = 'kpi-doc-empty';
      empty.textContent = '—';
      return empty;
    }
    const link = document.createElement('a');
    link.href = this.kpi.downloadUrl(file.documentId);
    link.className = 'kpi-doc-link';
    link.title = `Unduh ${file.fileName}`;
    link.setAttribute('aria-label', `Unduh ${file.fileName}`);
    const icon = document.createElement('i');
    icon.className = 'pi pi-check-circle';
    link.append(icon);
    return link;
  }

  private async init(): Promise<void> {
    try {
      const options = await firstValueFrom(this.kpi.options());
      this.plants.set(options.plants);
      const own = options.plants.find((p) => p.departments.some((d) => d.deptId === options.deptId));
      this.plantId.set((own ?? options.plants[0])?.plantId ?? 0);
      await this.load();
    } catch (err) {
      this.toastr.danger(apiErrorMessage(err), this.info().title);
      this.loading.set(false);
    }
  }

  changeYear(year: number): void {
    this.year.set(year);
    this.uploadMonth.set(null);
    void this.load();
  }

  changePlant(plantId: number): void {
    this.plantId.set(plantId);
    this.uploadDept.set(null);
    void this.load();
  }

  pickFile(): void {
    this.fileInput()?.nativeElement.click();
  }

  onFile(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.file.set(input.files?.[0] ?? null);
    input.value = ''; // supaya file yang sama bisa dipilih lagi
  }

  async upload(): Promise<void> {
    const file = this.file();
    const deptId = this.uploadDept();
    const month = this.uploadMonth();
    if (!file || !deptId || !month) {
      return;
    }
    this.busy.set(true);
    try {
      await firstValueFrom(this.kpi.upload(this.kind(), this.plantId(), deptId, this.year(), month, file));
      this.toastr.success(`${this.info().title} ${MONTH_NAMES[month - 1]} tersimpan.`, this.info().title);
      this.file.set(null);
      await this.load();
    } catch (err) {
      this.toastr.danger(apiErrorMessage(err), this.info().title);
    } finally {
      this.busy.set(false);
    }
  }

  private async load(): Promise<void> {
    if (!this.plantId()) {
      return;
    }
    this.loading.set(true);
    try {
      const data = await firstValueFrom(this.kpi.documents(this.kind(), this.year(), this.plantId()));
      this.data.set(data);
      const editable = data.departments.filter((d) => d.canUpload);
      if (!editable.some((d) => d.deptId === this.uploadDept())) {
        this.uploadDept.set(editable.length === 1 ? editable[0].deptId : null);
      }
      if (!this.uploadMonth() && data.lastMonth) {
        this.uploadMonth.set(data.lastMonth);
      }
    } catch (err) {
      this.toastr.danger(apiErrorMessage(err), this.info().title);
    } finally {
      this.loading.set(false);
    }
  }

  private showDetail(row: KpiDocumentRow): void {
    openDetail(
      this.modal,
      `${this.info().title} · ${row.deptName}`,
      MONTH_NAMES.map((name, i) => {
        const file = row.files.find((f) => f.month === i + 1);
        return {
          label: name,
          value: file ? `${file.fileName} (${file.uploadedByName}, ${formatDateTime(file.uploadedAt)})` : 'Belum diunggah',
          wide: true,
        };
      }),
    );
  }
}
