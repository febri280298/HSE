import { Component, computed, inject, input, signal } from '@angular/core';
import { ColDef } from 'ag-grid-community';

import { ModalService } from '../../core/services/modal.service';
import { MONTH_NAMES } from '../../core/utils/date';
import { DataGridComponent } from '../../shared/components/data-grid/data-grid.component';
import { GridAction, detailAction } from '../../shared/components/data-grid/grid-actions-cell.component';
import { openDetail } from '../../shared/components/detail-modal/detail-modal.component';
import { MonthPoint, niceMax } from './dashboard-data';

/**
 * Kolom berkelompok per bulan: Temuan vs Close. Warna seri ada di `--viz-series-*` (theme/styles/_app.scss),
 * sudah divalidasi untuk buta warna di keempat tema. Label angka hanya pada kolom Temuan di bulan tertinggi
 * (dua label berdampingan menempel di layar HP); sisanya lewat tooltip (hover/fokus) dan tabel di bawah grafik.
 */
@Component({
  selector: 'app-monthly-chart',
  imports: [DataGridComponent],
  templateUrl: './monthly-chart.component.html',
  styleUrl: './monthly-chart.component.scss',
  host: { '[class.compact]': 'compact()' },
})
export class MonthlyChartComponent {
  readonly data = input.required<MonthPoint[]>();
  readonly year = input.required<number>();
  /** Versi kecil untuk banyak grafik berdampingan: tanpa legenda dan tabel (legenda dipasang sekali oleh induk). */
  readonly compact = input(false);
  /** Samakan sumbu Y antar grafik; kosong = dihitung dari data grafik ini. */
  readonly scaleMax = input<number | null>(null);

  private readonly dialog = inject(ModalService);

  readonly monthNames = MONTH_NAMES;
  readonly active = signal<number | null>(null);
  /** Tabel baru dirender saat dibuka, supaya grid mengukur lebar yang benar. */
  readonly tableOpen = signal(false);

  readonly tableColumns: ColDef<MonthPoint>[] = [
    { headerName: 'Bulan', minWidth: 90, filter: false, valueGetter: (p) => p.data?.month, valueFormatter: (p) => MONTH_NAMES[(p.value as number) - 1] },
    { headerName: 'Temuan', field: 'total', filter: false },
    { headerName: 'Close', field: 'closed', filter: false },
    { headerName: '% Close', filter: false, valueGetter: (p) => (p.data ? this.percent(p.data) : '') },
  ];
  readonly tableActions: GridAction<MonthPoint>[] = [
    detailAction((point) =>
      openDetail(this.dialog, `${MONTH_NAMES[point.month - 1]} ${this.year()}`, [
        { label: 'Temuan', value: point.total },
        { label: 'Close', value: point.closed },
        { label: 'Belum close', value: point.total - point.closed },
        { label: '% Close', value: this.percent(point) },
      ]),
    ),
  ];
  readonly tableRowId = (point: MonthPoint) => String(point.month);

  onToggle(event: Event): void {
    this.tableOpen.set((event.target as HTMLDetailsElement).open);
  }

  readonly max = computed(() => this.scaleMax() ?? niceMax(Math.max(...this.data().map((d) => d.total), 0)));
  readonly ticks = computed(() => [4, 3, 2, 1, 0].map((i) => (this.max() / 4) * i));
  readonly peakMonth = computed(() => {
    const peak = this.data().reduce((best, d) => (d.total > best.total ? d : best), this.data()[0]);
    return peak.total > 0 ? peak.month : null;
  });
  readonly activePoint = computed(() => this.data().find((d) => d.month === this.active()) ?? null);

  height(value: number): string {
    return `${(value / this.max()) * 100}%`;
  }

  /** Posisi tooltip mengikuti kolom; di bulan-bulan akhir dibalik supaya tidak keluar kartu. */
  tooltipStyle(month: number): Record<string, string> {
    const center = ((month - 0.5) / 12) * 100;
    return month > 8 ? { right: `${100 - center}%` } : { left: `${center}%` };
  }

  percent(point: MonthPoint): string {
    return point.total ? `${Math.round((100 * point.closed) / point.total)}%` : '—';
  }

  label(point: MonthPoint): string {
    return `${MONTH_NAMES[point.month - 1]} ${this.year()}: ${point.total} temuan, ${point.closed} close`;
  }
}
