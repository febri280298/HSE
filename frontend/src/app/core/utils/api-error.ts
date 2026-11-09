import { HttpErrorResponse } from '@angular/common/http';

/** Pesan kesalahan dari balasan API yang bisa langsung ditampilkan ke pengguna. */
export function apiErrorMessage(error: unknown, fallback = 'Terjadi kesalahan. Coba lagi.'): string {
  if (error instanceof HttpErrorResponse) {
    const first = Object.values(apiFieldErrors(error))[0];
    if (first) {
      return first;
    }
    switch (error.status) {
      case 0:
        return 'Tidak terhubung ke server. Periksa jaringan lalu coba lagi.';
      case 401:
        return 'Sesi login berakhir. Silakan masuk lagi.';
      case 403:
        return 'Anda tidak punya akses untuk tindakan ini.';
      case 404:
        return 'Data tidak ditemukan. Mungkin sudah dihapus.';
      case 413:
        return 'Ukuran file terlalu besar.';
      case 429:
        return 'Terlalu banyak percobaan. Tunggu sebentar lalu coba lagi.';
      default:
        return `${fallback} (kode ${error.status})`;
    }
  }
  return error instanceof Error ? error.message : fallback;
}

/** Kesalahan per field dari ValidationProblem .NET: `{ errors: { field: [pesan] } }` → `{ field: pesan }`. */
export function apiFieldErrors(error: unknown): Record<string, string> {
  if (!(error instanceof HttpErrorResponse) || error.status !== 400) {
    return {};
  }
  const errors = (error.error?.errors ?? {}) as Record<string, string[]>;
  return Object.fromEntries(
    Object.entries(errors)
      .filter(([, messages]) => messages?.length)
      .map(([field, messages]) => [field.charAt(0).toLowerCase() + field.slice(1), messages[0]]),
  );
}
