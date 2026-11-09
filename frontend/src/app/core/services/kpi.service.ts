import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import {
  KpiDocumentKind,
  KpiDocuments,
  KpiInputField,
  KpiMonitoring,
  KpiOptions,
  KpiPica,
  KpiPicaRequest,
  KpiSummary,
} from '../models/kpi.model';

function toParams(values: Record<string, string | number | undefined | null>): HttpParams {
  let params = new HttpParams();
  for (const [key, value] of Object.entries(values)) {
    if (value !== undefined && value !== null) {
      params = params.set(key, value);
    }
  }
  return params;
}

/** API KPI K3: Summary KPI, Kalender Safety, PPT P2K3L, PICA. */
@Injectable({ providedIn: 'root' })
export class KpiService {
  private readonly http = inject(HttpClient);

  options(): Observable<KpiOptions> {
    return this.http.get<KpiOptions>('/api/kpi/options');
  }

  monitoring(year: number, month?: number): Observable<KpiMonitoring> {
    return this.http.get<KpiMonitoring>('/api/kpi/monitoring', { params: toParams({ year, month }) });
  }

  /** `deptId` null = gabungan semua departemen (ALL). */
  summary(year: number, plantId: number, deptId: number | null, reviewMonth?: number): Observable<KpiSummary> {
    return this.http.get<KpiSummary>('/api/kpi/summary', { params: toParams({ year, plantId, deptId, reviewMonth }) });
  }

  saveInput(plantId: number, deptId: number, year: number, month: number, field: KpiInputField, value: string): Observable<void> {
    return this.http.put<void>('/api/kpi/input', { plantId, deptId, year, month, field, value });
  }

  documents(kind: KpiDocumentKind, year: number, plantId: number): Observable<KpiDocuments> {
    return this.http.get<KpiDocuments>('/api/kpi/dokumen', { params: toParams({ kind, year, plantId }) });
  }

  upload(kind: KpiDocumentKind, plantId: number, deptId: number, year: number, month: number, file: File): Observable<void> {
    const form = new FormData();
    form.set('file', file);
    form.set('kind', kind);
    form.set('plantId', String(plantId));
    form.set('deptId', String(deptId));
    form.set('year', String(year));
    form.set('month', String(month));
    return this.http.post<void>('/api/kpi/dokumen', form);
  }

  downloadUrl(documentId: number): string {
    return `/api/kpi/dokumen/${documentId}/unduh`;
  }

  picas(year: number, plantId?: number): Observable<KpiPica[]> {
    return this.http.get<KpiPica[]>('/api/kpi/pica', { params: toParams({ year, plantId: plantId || undefined }) });
  }

  createPica(request: KpiPicaRequest): Observable<{ picaId: number }> {
    return this.http.post<{ picaId: number }>('/api/kpi/pica', request);
  }

  updatePica(picaId: number, request: KpiPicaRequest): Observable<void> {
    return this.http.put<void>(`/api/kpi/pica/${picaId}`, request);
  }

  deletePica(picaId: number): Observable<void> {
    return this.http.delete<void>(`/api/kpi/pica/${picaId}`);
  }

  participants(plantId: number): Observable<number[]> {
    return this.http.get<number[]>('/api/kpi/peserta', { params: toParams({ plantId }) });
  }

  saveParticipants(plantId: number, deptIds: number[]): Observable<void> {
    return this.http.put<void>('/api/kpi/peserta', { plantId, deptIds });
  }
}
