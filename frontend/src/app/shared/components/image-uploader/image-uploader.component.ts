import {
  Component,
  ElementRef,
  OnDestroy,
  computed,
  forwardRef,
  inject,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import {
  ControlValueAccessor,
  NG_VALIDATORS,
  NG_VALUE_ACCESSOR,
  ValidationErrors,
  Validator,
} from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { Subscription } from 'rxjs';

import { ImageResponse } from '../../../core/models/image-response.model';
import { ImageUploadService, cameraFileName } from '../../../core/services/image-upload.service';
import { ModalService } from '../../../core/services/modal.service';
import { NotifyService } from '../../../core/services/notify.service';
import {
  CameraCaptureComponent,
  CameraResult,
  canUseInPageCamera,
} from '../camera-capture/camera-capture.component';
import { ImagePreviewComponent, PreviewImage } from '../image-preview/image-preview.component';

/**
 * Kapan tombol "Pilih File" dan kotak dropzone ditampilkan:
 * - `always`: selalu (kamera + file).
 * - `desktop`: hanya di PC/laptop. Di HP/tablet wajib kamera langsung, jadi foto lama dari galeri tidak bisa dipakai.
 * - `never`: hanya kamera.
 */
export type GalleryMode = 'always' | 'desktop' | 'never';

interface UploadItem {
  key: number;
  fileName: string;
  /** Sumber gambar thumbnail: blob lokal saat baru diambil, atau URL server untuk foto yang sudah ada. */
  previewUrl: string;
  /** Object URL lokal yang harus dilepas saat item dihapus. */
  objectUrl: string | null;
  status: 'processing' | 'uploading' | 'done' | 'error';
  percent: number;
  image?: ImageResponse;
  error?: string;
  blob?: Blob;
}

const IS_TOUCH_DEVICE = typeof window !== 'undefined' && window.matchMedia?.('(pointer: coarse)').matches;

/**
 * REUSABLE COMPONENT upload foto: tombol kamera, tombol/dropzone file, thumbnail, progres, dan pratinjau.
 *
 * Pakai seperti input biasa di reactive form; nilainya `ImageResponse[]` (foto yang sudah tersimpan di server):
 *
 * ```html
 * <app-image-uploader formControlName="fotoTemuan" category="temuan" galleryMode="desktop" stamp="Temuan Patrol" />
 * ```
 *
 * Selama masih ada foto yang diupload, control bernilai invalid (`imageUploading`), jadi form tidak bisa disimpan.
 */
@Component({
  selector: 'app-image-uploader',
  imports: [ButtonModule],
  templateUrl: './image-uploader.component.html',
  styleUrl: './image-uploader.component.scss',
  providers: [
    { provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(() => ImageUploaderComponent), multi: true },
    { provide: NG_VALIDATORS, useExisting: forwardRef(() => ImageUploaderComponent), multi: true },
  ],
})
export class ImageUploaderComponent implements ControlValueAccessor, Validator, OnDestroy {
  /** Folder di server: `temuan`, `perbaikan`, `improvement`, ... (huruf kecil, angka, minus). */
  readonly category = input('umum');
  readonly maxFiles = input(5);
  readonly galleryMode = input<GalleryMode>('always');
  /** Teks cap di pojok bawah foto; tanggal & jam ditambahkan otomatis. `null` = tanpa cap. */
  readonly stamp = input<string | null>(null);
  /** Sisi terpanjang foto setelah diperkecil (px). */
  readonly maxSize = input(1600);

  /** Satu foto selesai tersimpan di server. */
  readonly uploaded = output<ImageResponse>();
  /** `true` selama masih ada foto yang diproses/diupload. */
  readonly uploadingChange = output<boolean>();

  private readonly uploadService = inject(ImageUploadService);
  private readonly dialog = inject(ModalService);
  private readonly toastr = inject(NotifyService);
  private readonly fileInput = viewChild.required<ElementRef<HTMLInputElement>>('fileInput');
  private readonly nativeCameraInput = viewChild.required<ElementRef<HTMLInputElement>>('nativeCameraInput');

  readonly items = signal<UploadItem[]>([]);
  readonly disabled = signal(false);
  readonly dragging = signal(false);
  readonly isFull = computed(() => this.items().length >= this.maxFiles());
  readonly busy = computed(() => this.items().some((i) => i.status === 'processing' || i.status === 'uploading'));
  readonly showGallery = computed(() => {
    const mode = this.galleryMode();
    return mode === 'always' || (mode === 'desktop' && !IS_TOUCH_DEVICE);
  });

  private nextKey = 1;
  private readonly uploads = new Map<number, Subscription>();
  private onChange: (value: ImageResponse[]) => void = () => {};
  private onTouched: () => void = () => {};
  private onValidatorChange: () => void = () => {};

  // ---------- Aksi pengguna ----------

  openCamera(): void {
    if (this.disabled() || this.isFull()) {
      return;
    }
    this.onTouched();

    if (!canUseInPageCamera()) {
      // Mis. dibuka lewat http://IP-server di HP: kamera di dalam web diblokir browser, pakai kamera bawaan.
      this.nativeCameraInput().nativeElement.click();
      return;
    }

    this.dialog
      .open(CameraCaptureComponent, {}, { width: 'min(94vw, 45rem)', closeOnBackdrop: false })
      ?.onClose.subscribe((result?: CameraResult) => {
        if (!result) {
          return;
        }
        if ('fallback' in result) {
          this.nativeCameraInput().nativeElement.click();
          return;
        }
        void this.addFiles([new File([result.blob], cameraFileName(), { type: 'image/jpeg' })]);
      });
  }

  openFilePicker(): void {
    if (this.disabled() || this.isFull() || !this.showGallery()) {
      return;
    }
    this.onTouched();
    this.fileInput().nativeElement.click();
  }

  onFilesSelected(event: Event): void {
    const inputEl = event.target as HTMLInputElement;
    const files = Array.from(inputEl.files ?? []);
    inputEl.value = ''; // supaya file yang sama bisa dipilih lagi
    void this.addFiles(files);
  }

  onDragOver(event: DragEvent): void {
    if (!this.showGallery() || this.disabled()) {
      return;
    }
    event.preventDefault();
    this.dragging.set(true);
  }

  onDragLeave(): void {
    this.dragging.set(false);
  }

  onDrop(event: DragEvent): void {
    event.preventDefault();
    this.dragging.set(false);
    if (!this.showGallery() || this.disabled()) {
      return;
    }
    this.onTouched();
    void this.addFiles(Array.from(event.dataTransfer?.files ?? []));
  }

  retry(item: UploadItem): void {
    this.startUpload(item.key);
  }

  remove(item: UploadItem): void {
    this.uploads.get(item.key)?.unsubscribe();
    this.uploads.delete(item.key);
    if (item.objectUrl) {
      URL.revokeObjectURL(item.objectUrl);
    }
    this.items.update((list) => list.filter((i) => i.key !== item.key));
    this.onTouched();
    this.emitValue();
  }

  preview(item: UploadItem): void {
    const withImage = this.items().filter((i) => i.previewUrl);
    const images: PreviewImage[] = withImage.map((i) => ({
      url: i.image?.url ?? i.previewUrl,
      fileName: i.fileName,
    }));
    this.dialog.open(
      ImagePreviewComponent,
      { images, startIndex: withImage.findIndex((i) => i.key === item.key) },
      { width: 'min(94vw, 1100px)' },
    );
  }

  // ---------- Proses foto ----------

  private async addFiles(files: File[]): Promise<void> {
    const room = this.maxFiles() - this.items().length;
    if (files.length > room) {
      this.toastr.warning(`Maksimal ${this.maxFiles()} foto.`, 'Foto');
    }

    for (const file of files.slice(0, Math.max(room, 0))) {
      if (!file.type.startsWith('image/')) {
        this.toastr.danger(`${file.name} bukan file foto.`, 'Foto');
        continue;
      }

      const key = this.nextKey++;
      this.items.update((list) => [
        ...list,
        { key, fileName: file.name, previewUrl: '', objectUrl: null, status: 'processing', percent: 0 },
      ]);
      this.emitBusy();

      try {
        const blob = await this.uploadService.compress(file, { maxSize: this.maxSize(), stamp: this.stamp() });
        if (!this.find(key)) {
          continue; // sudah dihapus pengguna selagi diproses
        }
        const objectUrl = URL.createObjectURL(blob);
        this.patch(key, { blob, objectUrl, previewUrl: objectUrl, fileName: toJpegName(file.name) });
        this.startUpload(key);
      } catch (err) {
        this.patch(key, { status: 'error', error: this.uploadService.errorMessage(err) });
        this.emitBusy();
      }
    }
  }

  private startUpload(key: number): void {
    const item = this.find(key);
    if (!item?.blob) {
      return;
    }

    this.patch(key, { status: 'uploading', percent: 0, error: undefined });
    this.emitBusy();

    const subscription = this.uploadService.upload(item.blob, item.fileName, this.category()).subscribe({
      next: (event) => {
        if (event.type === 'progress') {
          this.patch(key, { percent: event.percent });
        } else {
          this.patch(key, { status: 'done', percent: 100, image: event.image });
          this.uploaded.emit(event.image);
          this.emitValue();
        }
      },
      error: (err) => {
        this.uploads.delete(key);
        this.patch(key, { status: 'error', error: this.uploadService.errorMessage(err) });
        this.emitBusy();
      },
      complete: () => this.uploads.delete(key),
    });
    this.uploads.set(key, subscription);
  }

  private find(key: number): UploadItem | undefined {
    return this.items().find((i) => i.key === key);
  }

  private patch(key: number, changes: Partial<UploadItem>): void {
    this.items.update((list) => list.map((i) => (i.key === key ? { ...i, ...changes } : i)));
  }

  private value(): ImageResponse[] {
    return this.items().flatMap((i) => (i.status === 'done' && i.image ? [i.image] : []));
  }

  private emitValue(): void {
    this.onChange(this.value());
    this.emitBusy();
  }

  private emitBusy(): void {
    this.onValidatorChange();
    this.uploadingChange.emit(this.busy());
  }

  private clear(): void {
    this.uploads.forEach((s) => s.unsubscribe());
    this.uploads.clear();
    this.items().forEach((i) => i.objectUrl && URL.revokeObjectURL(i.objectUrl));
  }

  // ---------- ControlValueAccessor + Validator ----------

  writeValue(value: ImageResponse[] | null): void {
    this.clear();
    this.items.set(
      (value ?? []).map((image) => ({
        key: this.nextKey++,
        fileName: image.fileName,
        previewUrl: image.url,
        objectUrl: null,
        status: 'done',
        percent: 100,
        image,
      })),
    );
  }

  registerOnChange(fn: (value: ImageResponse[]) => void): void {
    this.onChange = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  setDisabledState(isDisabled: boolean): void {
    this.disabled.set(isDisabled);
  }

  validate(): ValidationErrors | null {
    if (this.busy()) {
      return { imageUploading: true };
    }
    if (this.items().some((i) => i.status === 'error')) {
      return { imageUploadFailed: true };
    }
    return null;
  }

  registerOnValidatorChange(fn: () => void): void {
    this.onValidatorChange = fn;
  }

  ngOnDestroy(): void {
    this.clear();
  }
}

/** Foto selalu dikirim sebagai JPEG setelah diperkecil, jadi ekstensinya disamakan. */
function toJpegName(name: string): string {
  const stem = name.replace(/\.[^.]+$/, '') || 'foto';
  return `${stem}.jpg`;
}
