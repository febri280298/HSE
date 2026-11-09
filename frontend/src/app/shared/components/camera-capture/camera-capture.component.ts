import { AfterViewInit, Component, ElementRef, OnDestroy, inject, signal, viewChild } from '@angular/core';
import { ButtonModule } from 'primeng/button';
import { DynamicDialogRef } from 'primeng/dynamicdialog';

/** Hasil dialog kamera: foto yang dipilih, atau permintaan memakai kamera bawaan HP. Tutup tanpa hasil = batal. */
export type CameraResult = { blob: Blob } | { fallback: true };

/** Kamera bisa dibuka di dalam halaman hanya lewat HTTPS atau localhost. */
export function canUseInPageCamera(): boolean {
  return window.isSecureContext && !!navigator.mediaDevices?.getUserMedia;
}

/**
 * Webcam dan kamera depan dibidik seperti bercermin; kamera belakang tampil apa adanya.
 * Webcam laptop sering tidak melaporkan arah, jadi selain kamera belakang dianggap menghadap pengguna.
 */
export function shouldMirrorPreview(facingMode: string | undefined): boolean {
  return facingMode !== 'environment';
}

/**
 * Kamera DI DALAM halaman web (getUserMedia). Foto hanya ada di memori browser lalu langsung dikirim ke server,
 * sehingga tidak pernah masuk galeri HP. Dibuka lewat ModalService oleh ImageUploaderComponent.
 */
@Component({
  selector: 'app-camera-capture',
  imports: [ButtonModule],
  templateUrl: './camera-capture.component.html',
  styleUrl: './camera-capture.component.scss',
})
export class CameraCaptureComponent implements AfterViewInit, OnDestroy {
  private readonly dialogRef = inject(DynamicDialogRef);
  private readonly video = viewChild.required<ElementRef<HTMLVideoElement>>('video');

  readonly state = signal<'starting' | 'live' | 'captured' | 'error'>('starting');
  readonly error = signal('');
  readonly photoUrl = signal<string | null>(null);
  readonly canSwitch = signal(false);
  /** Hanya tampilan di layar. Foto yang disimpan tetap asli supaya tulisan pada rambu terbaca. */
  readonly mirrored = signal(false);

  private stream: MediaStream | null = null;
  private photo: Blob | null = null;
  private facingMode: 'environment' | 'user' = 'environment';

  ngAfterViewInit(): void {
    void this.start();
  }

  async start(): Promise<void> {
    this.stopStream();
    this.state.set('starting');

    if (!canUseInPageCamera()) {
      this.fail('Kamera di dalam web hanya bisa dipakai lewat HTTPS atau localhost.');
      return;
    }

    try {
      this.stream = await navigator.mediaDevices.getUserMedia({
        video: {
          facingMode: { ideal: this.facingMode },
          width: { ideal: 1920 },
          height: { ideal: 1080 },
        },
        audio: false,
      });
      this.mirrored.set(shouldMirrorPreview(this.stream.getVideoTracks()[0]?.getSettings().facingMode));
      const video = this.video().nativeElement;
      video.srcObject = this.stream;
      await video.play();
      this.state.set('live');

      const devices = await navigator.mediaDevices.enumerateDevices();
      this.canSwitch.set(devices.filter((d) => d.kind === 'videoinput').length > 1);
    } catch (err) {
      this.fail(cameraErrorMessage(err));
    }
  }

  capture(): void {
    const video = this.video().nativeElement;
    const canvas = document.createElement('canvas');
    canvas.width = video.videoWidth;
    canvas.height = video.videoHeight;
    canvas.getContext('2d')?.drawImage(video, 0, 0);

    canvas.toBlob(
      (blob) => {
        if (!blob) {
          this.fail('Gagal mengambil foto. Coba lagi.');
          return;
        }
        this.photo = blob;
        this.setPhotoUrl(URL.createObjectURL(blob));
        video.pause();
        this.state.set('captured');
      },
      'image/jpeg',
      0.92,
    );
  }

  retake(): void {
    this.photo = null;
    this.setPhotoUrl(null);
    this.state.set('live');
    void this.video().nativeElement.play();
  }

  usePhoto(): void {
    if (this.photo) {
      this.dialogRef.close({ blob: this.photo } satisfies CameraResult);
    }
  }

  useNativeCamera(): void {
    this.dialogRef.close({ fallback: true } satisfies CameraResult);
  }

  switchCamera(): void {
    this.facingMode = this.facingMode === 'environment' ? 'user' : 'environment';
    void this.start();
  }

  cancel(): void {
    this.dialogRef.close();
  }

  ngOnDestroy(): void {
    this.stopStream();
    this.setPhotoUrl(null);
  }

  private fail(message: string): void {
    this.stopStream();
    this.error.set(message);
    this.state.set('error');
  }

  private stopStream(): void {
    this.stream?.getTracks().forEach((track) => track.stop());
    this.stream = null;
  }

  private setPhotoUrl(url: string | null): void {
    const old = this.photoUrl();
    if (old) {
      URL.revokeObjectURL(old);
    }
    this.photoUrl.set(url);
  }
}

function cameraErrorMessage(err: unknown): string {
  switch (err instanceof DOMException ? err.name : '') {
    case 'NotAllowedError':
    case 'SecurityError':
      return 'Izin kamera ditolak. Izinkan akses kamera untuk situs ini di pengaturan browser, lalu coba lagi.';
    case 'NotFoundError':
    case 'OverconstrainedError':
      return 'Kamera tidak ditemukan di perangkat ini.';
    case 'NotReadableError':
      return 'Kamera sedang dipakai aplikasi lain. Tutup aplikasi itu lalu coba lagi.';
    default:
      return 'Kamera tidak bisa dibuka.';
  }
}
