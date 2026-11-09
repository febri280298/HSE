import { Component } from '@angular/core';
import { ICellRendererAngularComp } from 'ag-grid-angular';
import { ICellRendererParams } from 'ag-grid-community';

/** Tombol pada kolom aksi. `icon` memakai class PrimeIcons; lihat detail memakai `pi pi-eye`. */
export interface GridAction<T> {
  icon: string;
  label: string;
  status?: 'basic' | 'primary' | 'warning' | 'danger';
  visible?: (row: T) => boolean;
  run: (row: T) => void;
}

@Component({
  selector: 'app-grid-actions-cell',
  template: `
    @for (action of visible; track action.label) {
      <button type="button" class="action" [class]="action.status ?? 'basic'"
              [title]="action.label" [attr.aria-label]="action.label" (click)="action.run(row)">
        <i [class]="action.icon"></i>
      </button>
    }
  `,
  styles: `
    :host { display: flex; align-items: center; justify-content: flex-end; height: 100%; }
    .action {
      display: inline-flex; align-items: center; justify-content: center;
      width: 2.25rem; height: 2.25rem; border-radius: 50%; cursor: pointer;
      color: var(--text-color-secondary);
      transition: background-color var(--element-transition-duration);
    }
    .action:hover { background: var(--surface-hover); }
    .action:focus-visible { outline: var(--focus-ring-width) var(--focus-ring-style) var(--focus-ring-color); outline-offset: -2px; }
    .action.primary { color: var(--primary-color); }
    .action.warning { color: var(--p-amber-600); }
    .action.danger { color: var(--p-red-500); }
  `,
})
export class GridActionsCellComponent<T> implements ICellRendererAngularComp {
  row!: T;
  visible: GridAction<T>[] = [];

  agInit(params: ICellRendererParams<T> & { actions: GridAction<T>[] }): void {
    this.row = params.data as T;
    this.visible = params.actions.filter((a) => !a.visible || a.visible(this.row));
  }

  refresh(): boolean {
    return false;
  }
}

/** Tombol lihat detail (ikon mata) yang dipakai semua tabel. */
export function detailAction<T>(run: (row: T) => void): GridAction<T> {
  return { icon: 'pi pi-eye', label: 'Lihat detail', status: 'primary', run };
}
