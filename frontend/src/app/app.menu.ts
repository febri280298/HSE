import { KPI_AKSES, PERMISSIONS, TEMUAN_AKSES } from './core/models/auth.model';

export interface MenuItem {
  title: string;
  /** Judul kelompok menu, tanpa tautan. */
  group?: boolean;
  icon?: string;
  link?: string;
}

/** Item menu + hak akses yang dibutuhkan, salah satu cukup (kosong = semua pengguna yang login). */
interface MenuEntry extends MenuItem {
  permissions?: string[];
}

/** Menu sidebar. Ikon dari PrimeIcons: https://primeng.org/icons */
const MENU: MenuEntry[] = [
  { title: 'Dashboard', icon: 'pi pi-home', link: '/dashboard' },

  { title: 'SAFETY & 5R PATROL', group: true },
  { title: 'Daftar Temuan', icon: 'pi pi-list', link: '/temuan', permissions: TEMUAN_AKSES },
  { title: 'Input Temuan', icon: 'pi pi-camera', link: '/temuan/baru', permissions: [PERMISSIONS.temuanInput] },
  {
    title: 'Jadwal Genba',
    icon: 'pi pi-calendar',
    link: '/temuan/jadwal',
    permissions: [...TEMUAN_AKSES, PERMISSIONS.temuanInput, PERMISSIONS.jadwalKelola],
  },

  { title: 'KPI K3', group: true },
  { title: 'Monitoring KPI', icon: 'pi pi-chart-bar', link: '/kpi', permissions: KPI_AKSES },
  { title: 'Summary KPI', icon: 'pi pi-table', link: '/kpi/summary', permissions: KPI_AKSES },
  { title: 'Kalender Safety', icon: 'pi pi-calendar-clock', link: '/kpi/kalender', permissions: KPI_AKSES },
  { title: 'PPT P2K3L', icon: 'pi pi-file', link: '/kpi/ppt', permissions: KPI_AKSES },
  { title: 'PICA', icon: 'pi pi-exclamation-circle', link: '/kpi/pica', permissions: KPI_AKSES },

  { title: 'ADMINISTRASI', group: true },
  { title: 'Pengguna', icon: 'pi pi-users', link: '/admin/pengguna', permissions: [PERMISSIONS.penggunaKelola] },
  { title: 'Peran & Hak Akses', icon: 'pi pi-shield', link: '/admin/peran', permissions: [PERMISSIONS.peranKelola] },
  { title: 'Master Data', icon: 'pi pi-database', link: '/admin/master', permissions: [PERMISSIONS.masterKelola] },
];

/** Menu sesuai hak akses pengguna; judul grup yang isinya kosong ikut disembunyikan. */
export function buildMenu(can: (permission: string) => boolean): MenuItem[] {
  const visible = MENU.filter((item) => !item.permissions || item.permissions.some(can));
  return visible
    .filter((item, i) => !item.group || (visible[i + 1] !== undefined && !visible[i + 1].group))
    .map(({ permissions: _permissions, ...item }) => item);
}
