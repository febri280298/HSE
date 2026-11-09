import { Component, WritableSignal, computed, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { CheckboxModule } from 'primeng/checkbox';
import { IconFieldModule } from 'primeng/iconfield';
import { InputIconModule } from 'primeng/inputicon';
import { InputTextModule } from 'primeng/inputtext';
import { SelectModule } from 'primeng/select';
import { TabsModule } from 'primeng/tabs';
import { ColDef } from 'ag-grid-community';
import { firstValueFrom } from 'rxjs';

import { PERMISSIONS, PlantInfo } from '../../../core/models/auth.model';
import { FindingListItem, STATUS_INFO, STATUS_LIST } from '../../../core/models/temuan.model';
import { AuthService } from '../../../core/services/auth.service';
import { ModalService } from '../../../core/services/modal.service';
import { NotifyService } from '../../../core/services/notify.service';
import { TemuanService } from '../../../core/services/temuan.service';
import { apiErrorMessage } from '../../../core/utils/api-error';
import { MONTH_NAMES } from '../../../core/utils/date';
import { DataGridComponent } from '../../../shared/components/data-grid/data-grid.component';
import { GridAction, detailAction } from '../../../shared/components/data-grid/grid-actions-cell.component';
import { cellOf, chipCell, dateColumn, isNarrowScreen, thumbCell } from '../../../shared/components/data-grid/grid-cells';
import { openFindingDetail } from '../temuan-detail-modal/temuan-detail-modal.component';

const ALL = 'semua';
/** Semua temuan sesuai filter dimuat sekali; tab status, pencarian, sort, dan paging dikerjakan di browser. */
const FETCH_LIMIT = 5000;

@Component({
  selector: 'app-temuan-list',
  imports: [
    FormsModule,
    RouterLink,
    ButtonModule,
    CheckboxModule,
    IconFieldModule,
    InputIconModule,
    InputTextModule,
    SelectModule,
    TabsModule,
    DataGridComponent,
  ],
  templateUrl: './temuan-list.component.html',
  styles: `
    .count { margin-left: 0.25rem; color: var(--text-color-secondary); font-weight: 400; }
    .tabs { display: block; margin-bottom: 1rem; }
    .tab-count {
      display: inline-block; min-width: 1.5rem; margin-left: 0.375rem; padding: 0 0.375rem; border-radius: 999px;
      background: var(--surface-hover); color: var(--text-color-secondary);
      font-size: 0.75rem; font-weight: 600; line-height: 1.25rem; text-align: center; font-variant-numeric: tabular-nums;
    }
    .tab-count.active { background: color-mix(in srgb, var(--primary-color) 16%, transparent); color: var(--primary-color); }
    .filters { display: flex; flex-wrap: wrap; align-items: center; gap: 0.5rem 0.75rem; margin-bottom: 1rem; }
    .check { display: inline-flex; align-items: center; gap: 0.5rem; }
    .search { flex: 1 1 12rem; max-width: 20rem; margin-left: auto; }
    @media (max-width: 767.98px) {
      .search { flex: 1 1 100%; max-width: none; order: -1; margin-left: 0; }
    }
  `,
})
export class TemuanListComponent {
  private readonly temuan = inject(TemuanService);
  private readonly auth = inject(AuthService);
  private readonly toastr = inject(NotifyService);
  private readonly dialog = inject(ModalService);
  private readonly router = inject(Router);

  readonly months = MONTH_NAMES.map((name, i) => ({ value: i + 1, name }));
  readonly years = Array.from({ length: 5 }, (_, i) => new Date().getFullYear() - i);

  readonly plants = signal<PlantInfo[]>([]);
  readonly plantChoices = computed(() => [{ plantId: 0, name: 'Semua plant' }, ...this.plants()]);
  readonly yearChoices = this.years.map((y) => ({ label: String(y), value: y }));
  readonly monthChoices = [{ label: 'Semua bulan', value: 0 }, ...this.months.map((m) => ({ label: m.name, value: m.value }))];
  readonly typeChoices = [
    { label: 'Safety & 5R', value: ALL },
    { label: 'Safety', value: 'Safety' },
    { label: '5R', value: '5R' },
  ];
  readonly plantId = signal(0);
  readonly year = signal(new Date().getFullYear());
  readonly month = signal(0);
  readonly tab = signal<string>(ALL);
  readonly search = signal('');
  readonly type = signal<string>(ALL);
  readonly overdue = signal(false);
  readonly mine = signal(false);
  readonly picOnly = signal(false);

  readonly items = signal<FindingListItem[]>([]);
  readonly total = signal(0);
  readonly loading = signal(true);

  readonly tabs = computed(() => {
    const items = this.items();
    return [
      { value: ALL, label: 'Semua', count: items.length },
      ...STATUS_LIST.map((status) => ({
        value: status as string,
        label: STATUS_INFO[status].label,
        count: items.filter((item) => item.status === status).length,
      })),
    ];
  });
  readonly visibleItems = computed(() => {
    const tab = this.tab();
    return tab === ALL ? this.items() : this.items().filter((item) => item.status === tab);
  });

  readonly hasDept = computed(() => this.auth.user()?.deptId != null);
  readonly canInput = computed(() => {
    this.auth.user();
    return this.auth.can(PERMISSIONS.temuanInput);
  });

  readonly rowId = (row: FindingListItem) => String(row.findingId);
  readonly actions: GridAction<FindingListItem>[] = [
    detailAction((row) => openFindingDetail(this.dialog, row.findingId)),
    // Di HP cukup ikon mata: modalnya punya tombol ke halaman lengkap.
    ...(isNarrowScreen()
      ? []
      : [{ icon: 'pi pi-arrow-right', label: 'Buka halaman', run: (row: FindingListItem) => void this.router.navigate(['/temuan', row.findingId]) }]),
  ];

  readonly columns: ColDef<FindingListItem>[] = this.buildColumns();

  constructor() {
    void this.init();
  }

  private buildColumns(): ColDef<FindingListItem>[] {
    const narrow = isNarrowScreen();
    const status: ColDef<FindingListItem> = {
      headerName: 'Status',
      colId: 'status',
      minWidth: 210,
      flex: 0,
      width: 215,
      valueGetter: (p) => this.statusText(p.data),
      cellRenderer: (p: { data?: FindingListItem }) => this.statusCell(p.data),
    };
    // Status di depan supaya terlihat tanpa menggeser tabel; di HP detail lebih dulu karena No disembunyikan.
    return [
      {
        headerName: 'Foto',
        colId: 'foto',
        width: 90,
        minWidth: 90,
        flex: 0,
        sortable: false,
        filter: false,
        hide: narrow,
        valueGetter: (p) => p.data?.thumbnailUrl ?? null,
        // Alamat foto memuat kata "temuan"; jangan ikut pencarian.
        getQuickFilterText: () => '',
        cellRenderer: (p: { value: string | null }) => thumbCell(p.value),
      },
      { headerName: 'No', field: 'number', minWidth: 175, flex: 0, width: 175, hide: narrow },
      ...(narrow ? [] : [status]),
      { headerName: 'Detail Temuan', field: 'detail', minWidth: 170, flex: 2, tooltipField: 'detail' },
      ...(narrow ? [status] : []),
      dateColumn('Tanggal', (r) => r.patrolDate, { hide: narrow }),
      { headerName: 'Plant', field: 'plantCode', minWidth: 85, hide: narrow },
      { headerName: 'Area', field: 'areaName', minWidth: 110, hide: narrow },
      { headerName: 'Tipe', field: 'type', minWidth: 80, hide: narrow },
      { headerName: 'Dept', minWidth: 85, valueGetter: (p) => p.data?.deptCode ?? 'Belum', hide: narrow },
      { headerName: 'PIC', minWidth: 125, valueGetter: (p) => p.data?.picName ?? '', hide: narrow },
      dateColumn('Due', (r) => r.dueDate, { hide: narrow }),
    ];
  }

  private statusText(row?: FindingListItem): string {
    return row ? `${STATUS_INFO[row.status].label}${row.isOverdue ? ' Overdue' : ''}` : '';
  }

  private statusCell(row?: FindingListItem): HTMLElement | string {
    if (!row) {
      return '';
    }
    const info = STATUS_INFO[row.status];
    return cellOf(chipCell(info.label, info.tone), row.isOverdue ? chipCell('Overdue', 'danger') : null);
  }

  private async init(): Promise<void> {
    try {
      const plants = await firstValueFrom(this.temuan.plants());
      const own = new Set(this.auth.user()?.plants.map((p) => p.plantId));
      this.plants.set(this.auth.can(PERMISSIONS.temuanSemuaPlant) ? plants : plants.filter((p) => own.has(p.plantId)));
    } catch (err) {
      this.toastr.danger(apiErrorMessage(err), 'Temuan');
    }
    await this.load();
  }

  selectTab(value: string | number | undefined): void {
    this.tab.set(String(value ?? ALL));
  }

  onSearch(event: Event): void {
    this.search.set((event.target as HTMLInputElement).value);
  }

  setter<T>(target: WritableSignal<T>, value: T): () => void {
    return () => target.set(value);
  }

  apply(change: () => void): void {
    change();
    void this.load();
  }

  private async load(): Promise<void> {
    this.loading.set(true);
    try {
      const result = await firstValueFrom(
        this.temuan.list({
          plantId: this.plantId(),
          year: this.year(),
          month: this.month(),
          type: this.type() === ALL ? undefined : this.type(),
          overdue: this.overdue(),
          mine: this.mine(),
          pic: this.picOnly(),
          page: 1,
          pageSize: FETCH_LIMIT,
        }),
      );
      this.items.set(result.items);
      this.total.set(result.total);
    } catch (err) {
      this.toastr.danger(apiErrorMessage(err), 'Temuan');
    } finally {
      this.loading.set(false);
    }
  }
}
