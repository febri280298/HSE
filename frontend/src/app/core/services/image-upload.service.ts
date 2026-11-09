import { HttpClient, HttpEventType } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, filter, map } from 'rxjs';

import { ImageResponse, ImageUploadEvent } from '../models/image-response.model';
import { apiErrorMessage } from '../utils/api-error';

export interface CompressOptions {
  /** Sisi terpanjang hasil (px). Default 1600: cukup tajam untuk laporan, kecil untuk jaringan pabrik. */
  maxSize?: number;
  /** Kualitas JPEG 0-1. */
  quality?: number;
  /** Teks cap di pojok bawah foto (tanggal & jam ditambahkan otomatis). Kosong = tanpa cap. */
  stamp?: string | null;
}

/**
 * SERVICE UTAMA foto: memperkecil foto di browser lalu mengirimnya (FormData) ke .NET `POST /api/images`.
 */
@Injectable({ providedIn: 'root' })
export class ImageUploadService {
  private readonly http = inject(HttpClient);
  private readonly endpoint = '/api/images';

  /** Upload satu foto ke folder `category` (temuan, perbaikan, improvement, ...). */
  upload(file: Blob, fileName: string, category: string): Observable<ImageUploadEvent> {
    const form = new FormData();
    form.append('file', file, fileName);
    form.append('category', category);

    return this.http
      .post<ImageResponse>(this.endpoint, form, { reportProgress: true, observe: 'events' })
      .pipe(
        filter(
          (event) =>
            event.type === HttpEventType.UploadProgress || event.type === HttpEventType.Response,
        ),
        map((event): ImageUploadEvent => {
          if (event.type === HttpEventType.Response) {
            return { type: 'done', image: event.body as ImageResponse };
          }
          const total = event.total ?? 0;
          return { type: 'progress', percent: total ? Math.round((100 * event.loaded) / total) : 0 };
        }),
      );
  }

  /**
   * Perkecil foto menjadi JPEG sebelum dikirim. Gambar digambar ulang lewat canvas, sehingga data EXIF
   * (termasuk lokasi GPS HP) ikut terbuang. Foto 4-8 MB dari kamera HP biasanya menjadi 200-400 KB.
   */
  async compress(source: Blob, options: CompressOptions = {}): Promise<Blob> {
    const { maxSize = 1600, quality = 0.82, stamp = null } = options;

    let bitmap: ImageBitmap;
    try {
      bitmap = await createImageBitmap(source, { imageOrientation: 'from-image' });
    } catch {
      throw new Error('Format foto tidak dikenali. Gunakan foto JPG atau PNG.');
    }

    const scale = Math.min(1, maxSize / Math.max(bitmap.width, bitmap.height));
    const width = Math.round(bitmap.width * scale);
    const height = Math.round(bitmap.height * scale);

    const canvas = document.createElement('canvas');
    canvas.width = width;
    canvas.height = height;
    const ctx = canvas.getContext('2d');
    if (!ctx) {
      bitmap.close();
      throw new Error('Browser tidak bisa memproses foto.');
    }
    ctx.drawImage(bitmap, 0, 0, width, height);
    bitmap.close();

    if (stamp) {
      drawStamp(ctx, width, height, `${formatStampTime(new Date())} · ${stamp}`);
    }

    return new Promise((resolve, reject) =>
      canvas.toBlob(
        (blob) => (blob ? resolve(blob) : reject(new Error('Gagal memproses foto.'))),
        'image/jpeg',
        quality,
      ),
    );
  }

  /** Pesan kesalahan yang bisa langsung ditampilkan ke pengguna. */
  errorMessage(error: unknown): string {
    return apiErrorMessage(error, 'Upload foto gagal.');
  }
}

/** Nama file untuk foto hasil jepretan kamera, mis. `foto-20261108-143205.jpg`. */
export function cameraFileName(date = new Date()): string {
  const pad = (n: number) => String(n).padStart(2, '0');
  return (
    `foto-${date.getFullYear()}${pad(date.getMonth() + 1)}${pad(date.getDate())}` +
    `-${pad(date.getHours())}${pad(date.getMinutes())}${pad(date.getSeconds())}.jpg`
  );
}

function formatStampTime(date: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0');
  return (
    `${pad(date.getDate())}/${pad(date.getMonth() + 1)}/${date.getFullYear()} ` +
    `${pad(date.getHours())}:${pad(date.getMinutes())}`
  );
}

/** Cap teks putih di atas pita gelap transparan di bagian bawah foto. */
function drawStamp(ctx: CanvasRenderingContext2D, width: number, height: number, text: string): void {
  const fontSize = Math.max(14, Math.round(width / 45));
  const padding = Math.round(fontSize * 0.6);
  ctx.font = `600 ${fontSize}px "Open Sans", sans-serif`;
  ctx.textBaseline = 'bottom';

  const barHeight = fontSize + padding * 2;
  ctx.fillStyle = 'rgba(0, 0, 0, 0.55)';
  ctx.fillRect(0, height - barHeight, width, barHeight);

  ctx.fillStyle = '#ffffff';
  ctx.fillText(text, padding, height - padding, width - padding * 2);
}
