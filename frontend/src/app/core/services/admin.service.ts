import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import {
  CreateUserRequest,
  PermissionInfo,
  Role,
  RoleRequest,
  UpdateUserRequest,
  UserAccount,
  UserOptions,
} from '../models/admin.model';
import { Area, Department } from '../models/temuan.model';

/** API halaman Administrasi: peran (hak akses) dan pengguna. */
@Injectable({ providedIn: 'root' })
export class AdminService {
  private readonly http = inject(HttpClient);

  permissions(): Observable<PermissionInfo[]> {
    return this.http.get<PermissionInfo[]>('/api/admin/roles/permissions');
  }

  roles(): Observable<Role[]> {
    return this.http.get<Role[]>('/api/admin/roles');
  }

  createRole(request: RoleRequest): Observable<Role> {
    return this.http.post<Role>('/api/admin/roles', request);
  }

  updateRole(roleId: number, request: RoleRequest): Observable<Role> {
    return this.http.put<Role>(`/api/admin/roles/${roleId}`, request);
  }

  deleteRole(roleId: number): Observable<void> {
    return this.http.delete<void>(`/api/admin/roles/${roleId}`);
  }

  users(): Observable<UserAccount[]> {
    return this.http.get<UserAccount[]>('/api/admin/users');
  }

  userOptions(): Observable<UserOptions> {
    return this.http.get<UserOptions>('/api/admin/users/options');
  }

  createUser(request: CreateUserRequest): Observable<UserAccount> {
    return this.http.post<UserAccount>('/api/admin/users', request);
  }

  updateUser(userId: number, request: UpdateUserRequest): Observable<UserAccount> {
    return this.http.put<UserAccount>(`/api/admin/users/${userId}`, request);
  }

  resetPassword(userId: number, newPassword: string): Observable<void> {
    return this.http.post<void>(`/api/admin/users/${userId}/reset-password`, { newPassword });
  }

  areas(plantId: number): Observable<Area[]> {
    return this.http.get<Area[]>('/api/master/areas', { params: { plantId } });
  }

  createArea(plantId: number, name: string): Observable<Area> {
    return this.http.post<Area>('/api/master/areas', { plantId, name, isActive: true });
  }

  updateArea(area: Area): Observable<Area> {
    return this.http.put<Area>(`/api/master/areas/${area.areaId}`, area);
  }

  departments(): Observable<Department[]> {
    return this.http.get<Department[]>('/api/master/departments');
  }

  createDepartment(code: string, name: string): Observable<Department> {
    return this.http.post<Department>('/api/master/departments', { code, name, isActive: true });
  }

  updateDepartment(department: Department): Observable<Department> {
    return this.http.put<Department>(`/api/master/departments/${department.deptId}`, department);
  }
}

/** Password sementara acak yang memenuhi aturan server (huruf + angka, 12 karakter). */
export function generatePassword(length = 12): string {
  const letters = 'abcdefghjkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ';
  const digits = '23456789';
  const all = letters + digits;
  const pick = (chars: string) => chars[crypto.getRandomValues(new Uint32Array(1))[0] % chars.length];
  const chars = [pick(letters), pick(digits), ...Array.from({ length: length - 2 }, () => pick(all))];
  for (let i = chars.length - 1; i > 0; i--) {
    const j = crypto.getRandomValues(new Uint32Array(1))[0] % (i + 1);
    [chars[i], chars[j]] = [chars[j], chars[i]];
  }
  return chars.join('');
}
