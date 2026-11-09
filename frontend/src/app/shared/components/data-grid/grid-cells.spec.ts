import { chipCell, formatDay, formatPercent, meterCell } from './grid-cells';

describe('grid-cells', () => {
  it('format tanggal dd/MM/yyyy, kosong tetap kosong', () => {
    expect(formatDay('2026-11-08')).toBe('08/11/2026');
    expect(formatDay(null)).toBe('');
  });

  it('format persen memakai koma, kosong jadi tanda strip', () => {
    expect(formatPercent(80.5)).toBe('80,5%');
    expect(formatPercent(null)).toBe('—');
  });

  it('teks dari data tidak dibaca sebagai HTML', () => {
    const chip = chipCell('<img src=x onerror=alert(1)>', 'danger');
    expect(chip.querySelector('img')).toBeNull();
    expect(chip.textContent).toBe('<img src=x onerror=alert(1)>');
    expect(chip.className).toBe('chip danger');
  });

  it('bilah meter mengikuti persen, kosong dianggap 0', () => {
    expect(meterCell(75).querySelector('i')?.style.width).toBe('75%');
    expect(meterCell(null).querySelector('i')?.style.width).toBe('0%');
  });
});
