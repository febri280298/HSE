export interface KpiDepartment {
  deptId: number;
  code: string;
  name: string;
}

export interface KpiPlantOption {
  plantId: number;
  code: string;
  name: string;
  departments: KpiDepartment[];
}

export interface KpiOptions {
  plants: KpiPlantOption[];
  /** Boleh mengisi untuk semua departemen (tim HSE). */
  canManage: boolean;
  deptId: number | null;
}

export type KpiRowFormat = 'number' | 'percent' | 'decimal' | 'eval' | 'text';
export type KpiInputField = 'accident' | 'disease' | 'diseaseTypes' | 'accidentNote';

export interface KpiRow {
  key: string;
  kpiNo: number;
  kpi: string;
  label: string;
  format: KpiRowFormat;
  /** Terisi kalau baris ini diinput departemen. */
  field: KpiInputField | null;
  values: (number | null)[];
  texts: (string | null)[];
}

export interface KpiReviewRow {
  no: number;
  kpi: string;
  target: string;
  thisMonth: string;
  yearToEndAcc: string;
  yearToEndPercent: number | null;
  achieved: boolean | null;
}

export interface KpiSummary {
  year: number;
  plantId: number;
  plantName: string;
  deptId: number | null;
  title: string;
  canEdit: boolean;
  lastMonth: number;
  reviewMonth: number;
  rows: KpiRow[];
  review: KpiReviewRow[];
}

export interface KpiDepartmentStatus {
  deptId: number;
  deptName: string;
  hasInput: boolean;
  hasKalender: boolean;
  hasPpt: boolean;
}

export interface KpiMonitoringPlant {
  plantId: number;
  plantName: string;
  accidents: number;
  diseaseCases: number;
  diseaseAverage: number;
  findings: number;
  closed: number;
  percentClosed: number | null;
  departments: KpiDepartmentStatus[];
}

export interface KpiMonitoring {
  year: number;
  month: number;
  plants: KpiMonitoringPlant[];
}

export type KpiDocumentKind = 'Kalender' | 'PPT';

export interface KpiDocumentInfo {
  documentId: number;
  month: number;
  fileName: string;
  sizeBytes: number;
  uploadedByName: string;
  uploadedAt: string;
}

export interface KpiDocumentRow {
  deptId: number;
  deptCode: string;
  deptName: string;
  canUpload: boolean;
  files: KpiDocumentInfo[];
}

export interface KpiDocuments {
  kind: KpiDocumentKind;
  year: number;
  plantId: number;
  lastMonth: number;
  departments: KpiDocumentRow[];
}

export type KpiPicaType = 'Accident' | 'Penyakit';
export type KpiPicaStatus = 'Open' | 'Closed';

export interface KpiPicaRequest {
  plantId: number;
  deptId: number;
  year: number;
  month: number;
  kpiType: KpiPicaType;
  problemIssue: string;
  correctiveAction: string;
  dueDate: string;
  picName: string;
  status: KpiPicaStatus;
}

export interface KpiPica extends KpiPicaRequest {
  picaId: number;
  plantName: string;
  deptCode: string;
  deptName: string;
  canEdit: boolean;
}

export const KPI_PICA_TYPES: { label: string; value: KpiPicaType }[] = [
  { label: 'Zero Accident', value: 'Accident' },
  { label: 'Jumlah kasus penyakit', value: 'Penyakit' },
];

/** Keterangan tiap KPI, dari file master KPI. */
export const KPI_INFO: Record<number, { target: string; definisi: string; spesifikasi: string }> = {
  1: {
    target: '0 Accident',
    definisi: 'Rank A jumlah accident, fatality, Rank B accident LWD, Rank C jumlah accident, dan NLWD yang terjadi.',
    spesifikasi: 'Sesuai KPI Perusahaan',
  },
  2: { target: '1 Kasus penyakit', definisi: 'Total temuan kasus penyakit. Yang diisi jumlah kasus, bukan jumlah karyawan yang sakit.', spesifikasi: 'Attend. ratio' },
  3: {
    target: '100% countermeasured',
    definisi: 'Total temuan penyimpangan terhadap 5R & Safety di area kerja saat genba safety patrol yang telah selesai diperbaiki.',
    spesifikasi: 'Sesuai KPI Perusahaan',
  },
};
