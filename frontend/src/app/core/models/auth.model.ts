export interface PlantInfo {
  plantId: number;
  code: string;
  name: string;
}

/** Pengguna yang sedang login (balasan /api/auth/me dan /api/auth/login). */
export interface CurrentUser {
  userId: number;
  username: string;
  displayName: string;
  /** Departemen pengguna; temuan departemen ini muncul untuknya. */
  deptId: number | null;
  deptName: string | null;
  /** `true` = wajib ganti password dulu; selama itu `permissions` kosong. */
  mustChangePassword: boolean;
  isAdministrator: boolean;
  roles: string[];
  permissions: string[];
  plants: PlantInfo[];
}

/** Kode hak akses, sama dengan backend `Auth/Permissions.cs`. */
export const PERMISSIONS = {
  temuanLihat: 'temuan.lihat',
  temuanInput: 'temuan.input',
  perbaikanInput: 'perbaikan.input',
  temuanVerifikasi: 'temuan.verifikasi',
  temuanSemuaPlant: 'temuan.semua-plant',
  temuanTugaskan: 'temuan.tugaskan',
  jadwalKelola: 'jadwal.kelola',
  kpiLihat: 'kpi.lihat',
  kpiInput: 'kpi.input',
  kpiKelola: 'kpi.kelola',
  penggunaKelola: 'pengguna.kelola',
  peranKelola: 'peran.kelola',
  masterKelola: 'master.kelola',
} as const;

/** Boleh membuka daftar temuan dan dashboard (sama dengan kebijakan `Permissions.TemuanAkses` di backend). */
export const TEMUAN_AKSES = [
  PERMISSIONS.temuanLihat,
  PERMISSIONS.perbaikanInput,
  PERMISSIONS.temuanVerifikasi,
  PERMISSIONS.temuanTugaskan,
];

/** Boleh membuka menu KPI K3 (sama dengan kebijakan `Permissions.KpiAkses` di backend). */
export const KPI_AKSES = [PERMISSIONS.kpiLihat, PERMISSIONS.kpiInput, PERMISSIONS.kpiKelola];

export type Permission = (typeof PERMISSIONS)[keyof typeof PERMISSIONS];
