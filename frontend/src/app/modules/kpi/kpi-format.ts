import { KpiRowFormat } from '../../core/models/kpi.model';

/** Tahun yang bisa dipilih di semua halaman KPI. */
export function kpiYears(): number[] {
  const now = new Date().getFullYear();
  return Array.from({ length: 4 }, (_, i) => now - i);
}

/** Persen dibulatkan seperti tampilan di file master (92%, 8%). */
export function kpiPercent(value: number | null | undefined): string {
  return value === null || value === undefined ? '—' : `${Math.round(value * 100)}%`;
}

export function kpiValue(format: KpiRowFormat, value: number | string | null | undefined): string {
  if (value === null || value === undefined || value === '') {
    return '';
  }
  if (typeof value === 'string') {
    return value;
  }
  switch (format) {
    case 'percent':
      return kpiPercent(value);
    case 'decimal':
      return value.toLocaleString('id-ID', { maximumFractionDigits: 1 });
    default:
      return String(value);
  }
}
