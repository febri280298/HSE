import { ColDef } from 'ag-grid-community';

/** Layar HP: kolom pendukung disembunyikan, isi lengkapnya tetap ada di modal detail. */
export const isNarrowScreen = (): boolean => window.matchMedia('(max-width: 575.98px)').matches;

/** `2026-11-08` menjadi `08/11/2026`. */
export function formatDay(value: string | null | undefined): string {
  if (!value) {
    return '';
  }
  const [year, month, day] = value.slice(0, 10).split('-');
  return `${day}/${month}/${year}`;
}

/** Waktu ISO menjadi `08/11/2026 14:05` di zona waktu browser. */
export function formatDateTime(value: string | null | undefined): string {
  if (!value) {
    return '';
  }
  const date = new Date(value);
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${pad(date.getDate())}/${pad(date.getMonth() + 1)}/${date.getFullYear()} ${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

/** Kolom tanggal: urut memakai nilai asli, tampil dan disaring dalam format dd/MM/yyyy. */
export function dateColumn<T>(
  headerName: string,
  value: (row: T) => string | null | undefined,
  extra: Partial<ColDef<T>> = {},
): ColDef<T> {
  return {
    headerName,
    minWidth: 125,
    valueGetter: (p) => (p.data ? (value(p.data) ?? null) : null),
    valueFormatter: (p) => formatDay(p.value),
    filterValueGetter: (p) => (p.data ? formatDay(value(p.data)) : ''),
    getQuickFilterText: (p) => formatDay(p.value),
    ...extra,
  };
}

/** Isi sel berupa label berwarna. Teks diisi lewat `textContent`, jadi aman dari HTML di data. */
export function chipCell(text: string, tone = ''): HTMLElement {
  const chip = document.createElement('span');
  chip.className = `chip ${tone}`.trim();
  chip.textContent = text;
  return chip;
}

/** Teks diikuti label, atau beberapa label berjajar. */
export function cellOf(...parts: (string | HTMLElement | null | undefined)[]): HTMLElement {
  const cell = document.createElement('span');
  for (const part of parts) {
    if (part) {
      cell.append(part);
    }
  }
  return cell;
}

export function thumbCell(url: string | null | undefined): HTMLElement | string {
  if (!url) {
    return '';
  }
  const image = document.createElement('img');
  image.src = url;
  image.alt = '';
  image.loading = 'lazy';
  image.className = 'grid-thumb';
  return image;
}

/** Bilah persen + angka. */
export function meterCell(percent: number | null): HTMLElement {
  const cell = document.createElement('span');
  cell.className = 'grid-meter';
  const track = document.createElement('span');
  const bar = document.createElement('i');
  bar.style.width = `${percent ?? 0}%`;
  track.append(bar);
  const label = document.createElement('b');
  label.textContent = formatPercent(percent);
  cell.append(track, label);
  return cell;
}

export function formatPercent(value: number | null | undefined): string {
  return value === null || value === undefined ? '—' : `${value.toLocaleString('id-ID')}%`;
}
