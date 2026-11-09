import { Component, computed, effect, input, output } from '@angular/core';
import { AgGridAngular } from 'ag-grid-angular';
import {
  AllCommunityModule,
  CellValueChangedEvent,
  ColDef,
  GetRowIdFunc,
  GridApi,
  GridReadyEvent,
  ModuleRegistry,
  RowClassRules,
  themeQuartz,
} from 'ag-grid-community';

import { GridAction, GridActionsCellComponent } from './grid-actions-cell.component';
import { GRID_LOCALE } from './grid-locale';

ModuleRegistry.registerModules([AllCommunityModule]);

/** Warna diambil dari variabel tema Sakai, jadi grid ikut berganti saat mode terang/gelap diganti. */
const GRID_THEME = themeQuartz.withParams({
  accentColor: 'var(--primary-color)',
  backgroundColor: 'var(--surface-card)',
  foregroundColor: 'var(--text-color)',
  headerBackgroundColor: 'var(--surface-hover)',
  headerTextColor: 'var(--text-color-secondary)',
  borderColor: 'var(--surface-border)',
  rowHoverColor: 'var(--surface-hover)',
  fontFamily: 'inherit',
  fontSize: 13,
  headerFontSize: 12,
  wrapperBorderRadius: 8,
});

const ACTION_WIDTH = 36;

/**
 * Tabel standar aplikasi (AG Grid Community): sort, filter per kolom, paging, dan kolom aksi di kanan.
 * Pakai `actions` untuk tombol per baris; tombol lihat detail memakai ikon `pi pi-eye`.
 *
 * ```html
 * <app-data-grid [rows]="users()" [columns]="columns" [actions]="actions" [loading]="loading()" />
 * ```
 */
@Component({
  selector: 'app-data-grid',
  imports: [AgGridAngular],
  template: `
    <ag-grid-angular
      [theme]="theme()"
      [rowData]="rows()"
      [columnDefs]="columnDefs()"
      [defaultColDef]="defaultColDef"
      domLayout="autoHeight"
      [loading]="loading()"
      [localeText]="localeText"
      [overlayNoRowsTemplate]="emptyTemplate()"
      [pagination]="pageSize() > 0"
      [paginationPageSize]="pageSize() || 20"
      [paginationPageSizeSelector]="pageSizes()"
      [quickFilterText]="quickFilter()"
      [includeHiddenColumnsInQuickFilter]="true"
      [getRowId]="getRowId()"
      [rowClassRules]="rowClassRules()"
      [singleClickEdit]="true"
      [stopEditingWhenCellsLoseFocus]="true"
      [tooltipShowDelay]="300"
      [popupParent]="popupParent"
      (gridReady)="onGridReady($event)"
      (cellValueChanged)="cellChanged.emit($event)" />
  `,
  styles: `
    :host { display: block; container-type: inline-size; }
  `,
})
export class DataGridComponent<T> {
  readonly rows = input.required<T[]>();
  readonly columns = input.required<ColDef<T>[]>();
  readonly actions = input<GridAction<T>[]>([]);
  readonly loading = input(false);
  /** Jumlah baris per halaman; 0 = tanpa paging. */
  readonly pageSize = input(20);
  /** Teks pencarian ke semua kolom. */
  readonly quickFilter = input('');
  /** Kalau nilainya berubah (mis. pindah tab), tabel kembali ke halaman pertama. */
  readonly resetPageKey = input<unknown>();
  readonly rowId = input<(row: T) => string>();
  readonly rowHeight = input<number>();
  readonly rowClassRules = input<RowClassRules<T>>();
  readonly emptyMessage = input('Belum ada data.');

  /** Nilai sel yang boleh diedit berubah. */
  readonly cellChanged = output<CellValueChangedEvent<T>>();

  /** Tinggi baris lewat tema, supaya teks sel tetap sejajar tengah. */
  readonly theme = computed(() => {
    const rowHeight = this.rowHeight();
    return rowHeight ? GRID_THEME.withParams({ rowHeight }) : GRID_THEME;
  });
  readonly localeText = GRID_LOCALE;
  /** Popup filter ditaruh di body supaya tidak terpotong pada tabel yang barisnya sedikit. */
  readonly popupParent = document.body;
  readonly defaultColDef: ColDef<T> = { sortable: true, filter: true, resizable: true, flex: 1, minWidth: 90 };

  readonly columnDefs = computed<ColDef<T>[]>(() => {
    const actions = this.actions();
    if (!actions.length) {
      return this.columns();
    }
    const width = 16 + actions.length * ACTION_WIDTH;
    return [
      ...this.columns(),
      {
        colId: 'aksi',
        headerName: '',
        pinned: 'right',
        width,
        minWidth: width,
        flex: 0,
        sortable: false,
        filter: false,
        resizable: false,
        suppressMovable: true,
        cellRenderer: GridActionsCellComponent,
        cellRendererParams: { actions },
      },
    ];
  });

  readonly pageSizes = computed(() => (this.pageSize() > 0 ? [...new Set([this.pageSize(), 10, 20, 50, 100])].sort((a, b) => a - b) : false));
  readonly emptyTemplate = computed(() => `<span>${this.emptyMessage()}</span>`);
  readonly getRowId = computed<GetRowIdFunc<T> | undefined>(() => {
    const rowId = this.rowId();
    return rowId ? (params) => rowId(params.data) : undefined;
  });

  private gridApi?: GridApi<T>;

  constructor() {
    effect(() => {
      this.resetPageKey();
      this.gridApi?.paginationGoToPage(0);
    });
  }

  onGridReady(event: GridReadyEvent<T>): void {
    this.gridApi = event.api;
  }
}
