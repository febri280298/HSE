import { kpiPercent, kpiValue } from './kpi-format';

describe('kpi-format', () => {
  it('persen dibulatkan seperti file master', () => {
    expect(kpiPercent(0.9167)).toBe('92%');
    expect(kpiPercent(0.0833)).toBe('8%');
    expect(kpiPercent(null)).toBe('—');
  });

  it('nilai mengikuti format baris', () => {
    expect(kpiValue('percent', 1)).toBe('100%');
    expect(kpiValue('decimal', 1.7)).toBe('1,7');
    expect(kpiValue('number', 0)).toBe('0');
    expect(kpiValue('number', null)).toBe('');
    expect(kpiValue('text', 'Demam')).toBe('Demam');
  });
});
