import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { PlantInfo } from '../models/auth.model';
import {
  CreateFindingRequest,
  CreatedFinding,
  DashboardResponse,
  FindingDetail,
  FindingListItem,
  FindingOptions,
  FindingQuery,
  GenbaSchedule,
  PagedResult,
  PhotoRef,
} from '../models/temuan.model';

/** API temuan Safety & 5R Patrol: catat, perbaiki, verifikasi, rekap. */
@Injectable({ providedIn: 'root' })
export class TemuanService {
  private readonly http = inject(HttpClient);

  plants(): Observable<PlantInfo[]> {
    return this.http.get<PlantInfo[]>('/api/master/plants');
  }

  options(plantId?: number): Observable<FindingOptions> {
    return this.http.get<FindingOptions>('/api/temuan/options', { params: toParams({ plantId }) });
  }

  list(query: FindingQuery): Observable<PagedResult<FindingListItem>> {
    return this.http.get<PagedResult<FindingListItem>>('/api/temuan', { params: toParams({ ...query }) });
  }

  get(findingId: number): Observable<FindingDetail> {
    return this.http.get<FindingDetail>(`/api/temuan/${findingId}`);
  }

  create(request: CreateFindingRequest): Observable<CreatedFinding> {
    return this.http.post<CreatedFinding>('/api/temuan', request);
  }

  /** Tim HSE menentukan departemen, PIC, dan due date. */
  assign(findingId: number, deptId: number, picUserId: number, dueDate: string): Observable<FindingDetail> {
    return this.http.post<FindingDetail>(`/api/temuan/${findingId}/tugaskan`, { deptId, picUserId, dueDate });
  }

  genba(year: number): Observable<GenbaSchedule[]> {
    return this.http.get<GenbaSchedule[]>('/api/genba', { params: toParams({ year }) });
  }

  /** Satu tanggal per plant per bulan; `null` menghapus jadwal bulan itu. */
  saveGenba(plantId: number, year: number, month: number, plannedDate: string | null): Observable<void> {
    return this.http.put<void>('/api/genba', { plantId, year, month, plannedDate });
  }

  saveRepair(findingId: number, countermeasure: string, photos: PhotoRef[], selesai: boolean): Observable<FindingDetail> {
    return this.http.post<FindingDetail>(`/api/temuan/${findingId}/perbaikan`, { countermeasure, photos, selesai });
  }

  verify(findingId: number, setuju: boolean, catatan: string | null): Observable<FindingDetail> {
    return this.http.post<FindingDetail>(`/api/temuan/${findingId}/verifikasi`, { setuju, catatan });
  }

  dashboard(year: number): Observable<DashboardResponse> {
    return this.http.get<DashboardResponse>('/api/dashboard', { params: toParams({ year }) });
  }
}

/** Isian kosong tidak dikirim, supaya server memakai "semua". */
function toParams(values: Record<string, string | number | boolean | undefined>): HttpParams {
  let params = new HttpParams();
  for (const [key, value] of Object.entries(values)) {
    if (value !== undefined && value !== '' && value !== false && value !== 0) {
      params = params.set(key, String(value));
    }
  }
  return params;
}
