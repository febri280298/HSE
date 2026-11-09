import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';

import { CurrentUser } from '../models/auth.model';

/**
 * Pengguna yang sedang login. Login memakai cookie HttpOnly dari .NET, jadi tidak ada token yang disimpan di browser;
 * service ini hanya menyimpan data pengguna untuk menu, guard, dan header.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);

  private readonly current = signal<CurrentUser | null>(null);
  private loaded = false;
  private loading: Promise<CurrentUser | null> | null = null;

  readonly user = this.current.asReadonly();
  readonly isLoggedIn = computed(() => this.current() !== null);

  /** Ambil data pengguna dari server sekali saat aplikasi dibuka (cookie bisa masih berlaku). */
  ensureLoaded(): Promise<CurrentUser | null> {
    if (this.loaded) {
      return Promise.resolve(this.current());
    }
    this.loading ??= firstValueFrom(this.http.get<CurrentUser>('/api/auth/me'))
      .catch(() => null)
      .then((user) => {
        this.setUser(user);
        this.loading = null;
        return user;
      });
    return this.loading;
  }

  async login(username: string, password: string): Promise<CurrentUser> {
    const user = await firstValueFrom(this.http.post<CurrentUser>('/api/auth/login', { username, password }));
    this.setUser(user);
    return user;
  }

  async changePassword(currentPassword: string, newPassword: string): Promise<CurrentUser> {
    const user = await firstValueFrom(
      this.http.post<CurrentUser>('/api/auth/password', { currentPassword, newPassword }),
    );
    this.setUser(user);
    return user;
  }

  /** Muat ulang data pengguna (mis. setelah admin mengubah peran sendiri). */
  async refresh(): Promise<CurrentUser | null> {
    this.loaded = false;
    return this.ensureLoaded();
  }

  async logout(): Promise<void> {
    try {
      await firstValueFrom(this.http.post('/api/auth/logout', {}));
    } finally {
      this.setUser(null);
      await this.router.navigateByUrl('/login');
    }
  }

  /** Server menjawab 401 di tengah pemakaian: sesi berakhir (dinonaktifkan, password di-reset, kedaluwarsa). */
  sessionExpired(): void {
    if (!this.current()) {
      return;
    }
    this.setUser(null);
    void this.router.navigate(['/login'], { queryParams: { returnUrl: this.router.url, sesi: 'habis' } });
  }

  can(permission: string): boolean {
    return this.current()?.permissions.includes(permission) ?? false;
  }

  private setUser(user: CurrentUser | null): void {
    this.current.set(user);
    this.loaded = true;
  }
}
