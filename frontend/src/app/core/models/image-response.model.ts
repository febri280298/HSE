/**
 * Balasan POST /api/images dari .NET (backend: Images/ImageResponse.cs).
 * Simpan `path` ke data (temuan, perbaikan, ...); pakai `url` untuk menampilkan foto.
 */
export interface ImageResponse {
  id: string;
  /** Lokasi relatif di penyimpanan, mis. `temuan/2026/11/0193....jpg`. Ini yang disimpan ke database. */
  path: string;
  /** Alamat untuk `<img src>`, mis. `/uploads/temuan/2026/11/0193....jpg`. */
  url: string;
  fileName: string;
  contentType: string;
  /** Ukuran file dalam byte. */
  size: number;
  category: string;
  /** Waktu upload (ISO 8601, UTC). */
  uploadedAt: string;
}

/** Tahapan satu upload: progres 0-100 berkali-kali, lalu satu kali `done` berisi hasil dari server. */
export type ImageUploadEvent =
  | { type: 'progress'; percent: number }
  | { type: 'done'; image: ImageResponse };
