import { Component, HostListener, OnInit, computed, inject, signal } from '@angular/core';
import { ButtonModule } from 'primeng/button';
import { DynamicDialogConfig, DynamicDialogRef } from 'primeng/dynamicdialog';

export interface PreviewImage {
  url: string;
  fileName: string;
}

/**
 * Pop-up pratinjau foto besar. Buka lewat ModalService:
 * `modal.open(ImagePreviewComponent, { images, startIndex }, { width: 'min(94vw, 1100px)' })`.
 * Panah kiri/kanan untuk pindah foto, Esc untuk menutup.
 */
@Component({
  selector: 'app-image-preview',
  imports: [ButtonModule],
  templateUrl: './image-preview.component.html',
  styleUrl: './image-preview.component.scss',
})
export class ImagePreviewComponent implements OnInit {
  private readonly dialogRef = inject(DynamicDialogRef);
  private readonly config = inject(DynamicDialogConfig);

  readonly images: PreviewImage[] = this.config.data?.images ?? [];
  private readonly startIndex: number = this.config.data?.startIndex ?? 0;

  readonly index = signal(0);
  readonly current = computed<PreviewImage | undefined>(() => this.images[this.index()]);

  ngOnInit(): void {
    this.index.set(Math.min(Math.max(this.startIndex, 0), Math.max(this.images.length - 1, 0)));
  }

  prev(): void {
    this.index.update((i) => Math.max(i - 1, 0));
  }

  next(): void {
    this.index.update((i) => Math.min(i + 1, this.images.length - 1));
  }

  close(): void {
    this.dialogRef.close();
  }

  @HostListener('document:keydown', ['$event'])
  onKeydown(event: KeyboardEvent): void {
    if (event.key === 'ArrowLeft') {
      this.prev();
    } else if (event.key === 'ArrowRight') {
      this.next();
    }
  }
}
