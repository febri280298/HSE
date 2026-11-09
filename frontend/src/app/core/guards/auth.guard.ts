import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';

import { AuthService } from '../services/auth.service';

/** Halaman aplikasi: wajib login dan sudah mengganti password awal. */
export const authGuard: CanActivateFn = async (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  const user = await auth.ensureLoaded();
  if (!user) {
    return router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
  }
  return user.mustChangePassword ? router.createUrlTree(['/ganti-password']) : true;
};

/** Halaman ganti password: cukup sudah login. */
export const signedInGuard: CanActivateFn = async () => {
  const router = inject(Router);
  return (await inject(AuthService).ensureLoaded()) ? true : router.createUrlTree(['/login']);
};

/** Halaman login: kalau sudah login langsung ke aplikasi. */
export const guestGuard: CanActivateFn = async () => {
  const router = inject(Router);
  return (await inject(AuthService).ensureLoaded()) ? router.createUrlTree(['/']) : true;
};

/**
 * Halaman yang butuh hak akses tertentu. Server tetap memeriksa ulang setiap permintaan.
 * Tanpa akses → dashboard dengan pesan (bukan toast: saat halaman dibuka langsung lewat alamat,
 * layout Nebular belum ada sehingga toast gagal dan navigasi ikut batal).
 */
export function permissionGuard(...permissions: string[]): CanActivateFn {
  return async () => {
    const auth = inject(AuthService);
    const router = inject(Router);
    await auth.ensureLoaded();
    return (
      permissions.some((p) => auth.can(p)) ||
      router.createUrlTree(['/dashboard'], { queryParams: { akses: 'ditolak' } })
    );
  };
}
