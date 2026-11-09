import {
  DashboardDeptRow,
  DashboardRow,
  DashboardStatusRow,
  FindingStatus,
  FindingType,
  STATUS_LIST,
} from '../../core/models/temuan.model';

export interface Totals {
  total: number;
  closed: number;
  open: number;
  overdue: number;
  closedOnTime: number;
  /** Persen close; `null` kalau belum ada temuan. */
  percentClosed: number | null;
  /** Persen tepat waktu dari temuan yang sudah close; `null` kalau belum ada yang close. */
  percentOnTime: number | null;
}

export interface MonthPoint {
  month: number;
  total: number;
  closed: number;
}

export interface PlantTotals extends Totals {
  plantId: number;
  plantName: string;
}

export interface TypeChart {
  type: FindingType;
  totals: Totals;
  monthly: MonthPoint[];
  /** Batas sumbu Y yang sama untuk semua plant pada tipe ini, supaya tinggi batang bisa dibandingkan. */
  scaleMax: number;
}

export interface PlantPanel {
  plantId: number;
  plantName: string;
  totals: Totals;
  charts: TypeChart[];
}

export interface DeptTotals {
  deptCode: string | null;
  deptName: string;
  total: number;
  closed: number;
  overdue: number;
  percentClosed: number | null;
}

export interface StatusCount {
  status: FindingStatus;
  total: number;
  /** Lebar batang: persen terhadap status terbanyak. */
  share: number;
}

const percentOf = (part: number, whole: number) => (whole ? Math.round((1000 * part) / whole) / 10 : null);

export function totalsOf(rows: DashboardRow[]): Totals {
  const total = sum(rows, (r) => r.total);
  const closed = sum(rows, (r) => r.closed);
  const closedOnTime = sum(rows, (r) => r.closedOnTime);
  return {
    total,
    closed,
    open: total - closed,
    overdue: sum(rows, (r) => r.overdue),
    closedOnTime,
    percentClosed: percentOf(closed, total),
    percentOnTime: percentOf(closedOnTime, closed),
  };
}

/** Selalu 12 bulan, supaya bulan tanpa temuan tetap terlihat kosong. */
export function monthlyOf(rows: DashboardRow[]): MonthPoint[] {
  return Array.from({ length: 12 }, (_, i) => {
    const inMonth = rows.filter((r) => r.month === i + 1);
    return { month: i + 1, total: sum(inMonth, (r) => r.total), closed: sum(inMonth, (r) => r.closed) };
  });
}

export function byPlant(rows: DashboardRow[]): PlantTotals[] {
  const plants = new Map<number, string>();
  rows.forEach((r) => plants.set(r.plantId, r.plantName));
  return [...plants].map(([plantId, plantName]) => ({
    plantId,
    plantName,
    ...totalsOf(rows.filter((r) => r.plantId === plantId)),
  }));
}

/** Satu panel per plant berisi grafik per tipe, seperti blok Safety dan 5R di sheet PROGRESS & STATISTIC. */
export function plantPanels(rows: DashboardRow[], types: FindingType[]): PlantPanel[] {
  const charts = (plantRows: DashboardRow[], type: FindingType) => plantRows.filter((r) => r.type === type);
  const scales = new Map(
    types.map((type) => {
      const peak = Math.max(0, ...byPlant(rows).map((p) => Math.max(...monthlyOf(charts(rows.filter((r) => r.plantId === p.plantId), type)).map((m) => m.total))));
      return [type, niceMax(peak)] as const;
    }),
  );

  return byPlant(rows).map(({ plantId, plantName }) => {
    const plantRows = rows.filter((r) => r.plantId === plantId);
    return {
      plantId,
      plantName,
      totals: totalsOf(plantRows.filter((r) => types.includes(r.type))),
      charts: types.map((type) => ({
        type,
        totals: totalsOf(charts(plantRows, type)),
        monthly: monthlyOf(charts(plantRows, type)),
        scaleMax: scales.get(type) as number,
      })),
    };
  });
}

/** Terbanyak di atas; temuan yang belum ditugaskan selalu paling bawah. */
export function byDepartment(rows: DashboardDeptRow[]): DeptTotals[] {
  const groups = new Map<string, DashboardDeptRow[]>();
  rows.forEach((r) => {
    const key = r.deptCode ?? '';
    groups.set(key, [...(groups.get(key) ?? []), r]);
  });
  return [...groups.values()]
    .map((group) => {
      const total = group.reduce((n, r) => n + r.total, 0);
      const closed = group.reduce((n, r) => n + r.closed, 0);
      return {
        deptCode: group[0].deptCode,
        deptName: group[0].deptName ?? 'Belum ditugaskan',
        total,
        closed,
        overdue: group.reduce((n, r) => n + r.overdue, 0),
        percentClosed: percentOf(closed, total),
      };
    })
    .sort((a, b) => Number(a.deptCode === null) - Number(b.deptCode === null) || b.total - a.total);
}

/** Urut sesuai alur kerja; status tanpa temuan tetap ditampilkan dengan angka 0. */
export function statusCounts(rows: DashboardStatusRow[]): StatusCount[] {
  const counts = STATUS_LIST.map((status) => ({
    status,
    total: rows.filter((r) => r.status === status).reduce((n, r) => n + r.total, 0),
  }));
  const peak = Math.max(1, ...counts.map((c) => c.total));
  return counts.map((c) => ({ ...c, share: (100 * c.total) / peak }));
}

/** Batas atas sumbu Y yang rapi (kelipatan 1, 2, 5 × 10ⁿ), dibagi 4 garis. */
export function niceMax(value: number): number {
  if (value <= 4) {
    return 4;
  }
  const step = value / 4;
  const magnitude = 10 ** Math.floor(Math.log10(step));
  const niceStep = [1, 2, 5, 10].map((m) => m * magnitude).find((s) => s >= step) as number;
  return niceStep * 4;
}

function sum(rows: DashboardRow[], pick: (row: DashboardRow) => number): number {
  return rows.reduce((total, row) => total + pick(row), 0);
}
