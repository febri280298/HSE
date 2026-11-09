import { PlantInfo } from './auth.model';

export type FindingStatus = 'Open' | 'Ditugaskan' | 'OnProgress' | 'Verifikasi' | 'NotClose' | 'Close';
export type FindingType = 'Safety' | '5R';

export const STATUS_INFO: Record<FindingStatus, { label: string; tone: 'primary' | 'warning' | 'danger' | 'success' | '' }> = {
  Open: { label: 'Open', tone: 'primary' },
  Ditugaskan: { label: 'Ditugaskan', tone: '' },
  OnProgress: { label: 'On Progress', tone: 'warning' },
  Verifikasi: { label: 'Menunggu Verifikasi', tone: '' },
  NotClose: { label: 'Not Close', tone: 'danger' },
  Close: { label: 'Close', tone: 'success' },
};

export const STATUS_LIST = Object.keys(STATUS_INFO) as FindingStatus[];

export interface PhotoRef {
  path: string;
  fileName: string;
}

export interface FindingOptions {
  plants: PlantInfo[];
  areas: { areaId: number; name: string }[];
  departments: { deptId: number; code: string; name: string }[];
  /** Calon PIC; hanya terisi untuk tim HSE dan kalau plant diminta. */
  pics: { userId: number; displayName: string; deptId: number }[];
}

export interface CreateFindingRequest {
  plantId: number;
  patrolDate: string;
  detail: string;
  areaId: number;
  type: FindingType;
  photos: PhotoRef[];
}

export interface CreatedFinding {
  findingId: number;
  number: string | null;
}

export interface FindingQuery {
  plantId?: number;
  year?: number;
  month?: number;
  status?: string;
  type?: string;
  overdue?: boolean;
  mine?: boolean;
  pic?: boolean;
  page?: number;
  pageSize?: number;
}

export interface PagedResult<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}

export interface FindingListItem {
  findingId: number;
  number: string;
  plantCode: string;
  plantName: string;
  patrolDate: string;
  type: FindingType;
  detail: string;
  areaName: string;
  deptCode: string | null;
  picName: string | null;
  dueDate: string | null;
  status: FindingStatus;
  isOverdue: boolean;
  thumbnailUrl: string | null;
}

export interface FindingPhoto extends PhotoRef {
  kind: 'Temuan' | 'Perbaikan';
  url: string;
}

export interface FindingEvent {
  action: string;
  note: string | null;
  userName: string;
  createdAt: string;
}

export interface FindingDetail {
  findingId: number;
  number: string;
  plantId: number;
  plantCode: string;
  plantName: string;
  patrolDate: string;
  type: FindingType;
  detail: string;
  areaName: string;
  deptId: number | null;
  deptCode: string | null;
  deptName: string | null;
  picUserId: number | null;
  picName: string | null;
  dueDate: string | null;
  status: FindingStatus;
  isOverdue: boolean;
  countermeasure: string | null;
  repairedByName: string | null;
  repairedAt: string | null;
  verifiedByName: string | null;
  verifiedAt: string | null;
  verifyNote: string | null;
  createdByName: string;
  createdAt: string;
  canAssign: boolean;
  canRepair: boolean;
  canVerify: boolean;
  photos: FindingPhoto[];
  events: FindingEvent[];
}

export interface DashboardRow {
  plantId: number;
  plantCode: string;
  plantName: string;
  month: number;
  type: FindingType;
  total: number;
  closed: number;
  overdue: number;
  closedOnTime: number;
}

export interface DashboardDeptRow {
  plantId: number;
  type: FindingType;
  /** Kosong = temuan yang belum ditugaskan. */
  deptCode: string | null;
  deptName: string | null;
  total: number;
  closed: number;
  overdue: number;
}

export interface DashboardStatusRow {
  plantId: number;
  type: FindingType;
  status: FindingStatus;
  total: number;
}

export interface DashboardOverdueItem {
  findingId: number;
  number: string;
  plantId: number;
  type: FindingType;
  detail: string;
  deptCode: string | null;
  picName: string | null;
  dueDate: string;
  daysLate: number;
}

export interface DashboardResponse {
  year: number;
  rows: DashboardRow[];
  departments: DashboardDeptRow[];
  statuses: DashboardStatusRow[];
  overdue: DashboardOverdueItem[];
}

export interface GenbaSchedule {
  plantId: number;
  month: number;
  plannedDate: string;
}

export interface Area {
  areaId: number;
  plantId: number;
  name: string;
  isActive: boolean;
}

export interface Department {
  deptId: number;
  code: string;
  name: string;
  isActive: boolean;
}
