import { DashboardDeptRow, DashboardRow } from '../../core/models/temuan.model';
import { byDepartment, byPlant, monthlyOf, niceMax, plantPanels, statusCounts, totalsOf } from './dashboard-data';

const row = (plantId: number, month: number, total: number, closed: number, overdue = 0, closedOnTime = 0): DashboardRow => ({
  plantId,
  plantCode: `P${plantId}`,
  plantName: `Plant ${plantId}`,
  month,
  type: 'Safety',
  total,
  closed,
  overdue,
  closedOnTime,
});

describe('dashboard-data', () => {
  it('menjumlahkan total, persen close, dan persen tepat waktu', () => {
    expect(totalsOf([row(1, 1, 8, 6, 1, 3), row(2, 2, 2, 2, 0, 2)])).toEqual({
      total: 10,
      closed: 8,
      open: 2,
      overdue: 1,
      closedOnTime: 5,
      percentClosed: 80,
      percentOnTime: 62.5,
    });
  });

  it('persen kosong kalau belum ada data, bukan 0 atau NaN', () => {
    const totals = totalsOf([]);
    expect(totals.percentClosed).toBeNull();
    expect(totals.percentOnTime).toBeNull();
  });

  it('selalu 12 bulan dan menggabungkan plant di bulan yang sama', () => {
    const months = monthlyOf([row(1, 3, 4, 1), row(2, 3, 6, 5)]);
    expect(months).toHaveLength(12);
    expect(months[2]).toEqual({ month: 3, total: 10, closed: 6 });
    expect(months[0]).toEqual({ month: 1, total: 0, closed: 0 });
  });

  it('rekap per plant', () => {
    expect(byPlant([row(1, 1, 4, 2), row(1, 2, 4, 2), row(2, 1, 1, 1)]).map((p) => [p.plantName, p.total, p.percentClosed])).toEqual([
      ['Plant 1', 8, 50],
      ['Plant 2', 1, 100],
    ]);
  });

  it('panel plant memakai skala sumbu yang sama per tipe', () => {
    const fiveR = (plantId: number, month: number, total: number): DashboardRow => ({ ...row(plantId, month, total, 0), type: '5R' });
    const panels = plantPanels([row(1, 1, 3, 1), fiveR(1, 1, 18), row(2, 2, 7, 7), fiveR(2, 2, 2)], ['Safety', '5R']);

    expect(panels.map((p) => p.plantName)).toEqual(['Plant 1', 'Plant 2']);
    expect(panels[0].charts.map((c) => [c.type, c.scaleMax])).toEqual([['Safety', 8], ['5R', 20]]);
    expect(panels[1].charts.map((c) => c.scaleMax)).toEqual([8, 20]);
    expect(panels[1].charts[0].totals.percentClosed).toBe(100);
    expect(panels[0].totals.total).toBe(21);
  });

  it('panel hanya berisi tipe yang dipilih', () => {
    expect(plantPanels([row(1, 1, 3, 1)], ['Safety'])[0].charts.map((c) => c.type)).toEqual(['Safety']);
  });

  it('rekap departemen: terbanyak di atas, belum ditugaskan paling bawah', () => {
    const dept = (deptCode: string | null, total: number, closed = 0, overdue = 0): DashboardDeptRow => ({
      plantId: 1,
      type: 'Safety',
      deptCode,
      deptName: deptCode,
      total,
      closed,
      overdue,
    });

    const result = byDepartment([dept('MKT', 2, 1), dept(null, 9), dept('PRD', 3, 3, 1), dept('PRD', 5, 1)]);

    expect(result.map((d) => [d.deptName, d.total, d.closed, d.overdue])).toEqual([
      ['PRD', 8, 4, 1],
      ['MKT', 2, 1, 0],
      ['Belum ditugaskan', 9, 0, 0],
    ]);
    expect(result[0].percentClosed).toBe(50);
  });

  it('status urut sesuai alur dan yang kosong tetap ada', () => {
    const result = statusCounts([
      { plantId: 1, type: 'Safety', status: 'Close', total: 8 },
      { plantId: 2, type: '5R', status: 'Close', total: 2 },
      { plantId: 1, type: '5R', status: 'Open', total: 5 },
    ]);

    expect(result.map((s) => s.status)).toEqual(['Open', 'Ditugaskan', 'OnProgress', 'Verifikasi', 'NotClose', 'Close']);
    expect(result.find((s) => s.status === 'Close')).toEqual({ status: 'Close', total: 10, share: 100 });
    expect(result.find((s) => s.status === 'Open')?.share).toBe(50);
    expect(result.find((s) => s.status === 'NotClose')?.total).toBe(0);
  });

  it('batas sumbu selalu tidak kurang dari nilai terbesar', () => {
    expect(niceMax(0)).toBe(4);
    expect(niceMax(3)).toBe(4);
    expect(niceMax(17)).toBe(20);
    expect(niceMax(86)).toBe(200);
    for (const value of [5, 9, 41, 99, 101, 543]) {
      expect(niceMax(value)).toBeGreaterThanOrEqual(value);
    }
  });
});
