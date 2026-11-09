import { buildMenu } from './app.menu';
import { PERMISSIONS } from './core/models/auth.model';

describe('buildMenu', () => {
  const titles = (granted: string[]) => buildMenu((p) => granted.includes(p)).map((i) => i.title);

  it('tanpa hak akses hanya Dashboard (judul grup kosong ikut hilang)', () => {
    expect(titles([])).toEqual(['Dashboard']);
  });

  it('patroller melihat Input Temuan dan Jadwal Genba', () => {
    expect(titles([PERMISSIONS.temuanInput])).toEqual(['Dashboard', 'SAFETY & 5R PATROL', 'Input Temuan', 'Jadwal Genba']);
  });

  it('pengelola peran tidak melihat menu Pengguna', () => {
    expect(titles([PERMISSIONS.peranKelola])).toEqual(['Dashboard', 'ADMINISTRASI', 'Peran & Hak Akses']);
  });

  it('user departemen melihat Daftar Temuan', () => {
    expect(titles([PERMISSIONS.perbaikanInput])).toEqual(['Dashboard', 'SAFETY & 5R PATROL', 'Daftar Temuan', 'Jadwal Genba']);
  });

  it('tim HSE dengan hak tugaskan melihat Daftar Temuan', () => {
    expect(titles([PERMISSIONS.temuanTugaskan])).toContain('Daftar Temuan');
  });

  it('item menu tidak membawa kode hak akses ke tampilan', () => {
    expect(buildMenu(() => true).some((item) => 'permissions' in item)).toBe(false);
  });
});
