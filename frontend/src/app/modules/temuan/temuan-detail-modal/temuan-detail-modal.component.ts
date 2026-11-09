import { DatePipe } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { DynamicDialogConfig, DynamicDialogRef } from 'primeng/dynamicdialog';
import { MessageModule } from 'primeng/message';
import { firstValueFrom } from 'rxjs';

import { FindingDetail, FindingPhoto, STATUS_INFO } from '../../../core/models/temuan.model';
import { ModalService } from '../../../core/services/modal.service';
import { TemuanService } from '../../../core/services/temuan.service';
import { apiErrorMessage } from '../../../core/utils/api-error';
import { ImagePreviewComponent } from '../../../shared/components/image-preview/image-preview.component';

/** Ringkasan satu temuan dalam modal: data, foto, dan riwayat. Aksi (tugaskan, perbaikan, verifikasi) ada di halaman detail. */
@Component({
  selector: 'app-temuan-detail-modal',
  imports: [DatePipe, ButtonModule, MessageModule],
  templateUrl: './temuan-detail-modal.component.html',
  styles: `
    h6 { margin: 1.25rem 0 0.5rem; font-size: 1rem; font-weight: 600; }
    .photo-compare { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 1rem; align-items: start; }
    .photos { display: grid; grid-template-columns: repeat(auto-fit, minmax(8rem, 1fr)); gap: 0.5rem; }
    .photo { padding: 0; cursor: zoom-in; border-radius: var(--content-border-radius); overflow: hidden; }
    .photo img { display: block; width: 100%; aspect-ratio: 4 / 3; object-fit: cover; }
    .history { margin: 0; padding: 0; list-style: none; }
    .history li { display: flex; flex-direction: column; padding: 0 0 0.75rem 0.75rem; border-left: 2px solid var(--surface-border); }
    .history span { color: var(--text-color-secondary); font-size: 0.875rem; }
    .history p { margin: 0.125rem 0 0; overflow-wrap: anywhere; }
    .loading { display: flex; align-items: center; justify-content: center; height: 14rem; color: var(--primary-color); }
    .title { display: inline-flex; flex-wrap: wrap; align-items: center; gap: 0.5rem; }
    .title .chip { margin: 0; }
  `,
})
export class TemuanDetailModalComponent implements OnInit {
  readonly ref = inject(DynamicDialogRef);
  private readonly config = inject(DynamicDialogConfig);
  private readonly temuan = inject(TemuanService);
  private readonly modal = inject(ModalService);
  private readonly router = inject(Router);

  private readonly findingId: number = this.config.data?.findingId ?? 0;

  readonly detail = signal<FindingDetail | null>(null);
  readonly error = signal('');
  readonly status = computed(() => {
    const d = this.detail();
    return d ? STATUS_INFO[d.status] : null;
  });
  readonly before = computed(() => this.detail()?.photos.filter((p) => p.kind === 'Temuan') ?? []);
  readonly after = computed(() => this.detail()?.photos.filter((p) => p.kind === 'Perbaikan') ?? []);
  readonly needsAction = computed(() => {
    const d = this.detail();
    return !!d && (d.canAssign || d.canRepair || d.canVerify);
  });

  async ngOnInit(): Promise<void> {
    try {
      this.detail.set(await firstValueFrom(this.temuan.get(this.findingId)));
    } catch (err) {
      this.error.set(apiErrorMessage(err));
    }
  }

  preview(photos: FindingPhoto[], startIndex: number): void {
    this.modal.open(ImagePreviewComponent, { images: photos, startIndex }, { width: 'min(94vw, 1100px)' });
  }

  openPage(): void {
    this.ref.close();
    void this.router.navigate(['/temuan', this.findingId]);
  }
}

export function openFindingDetail(modal: ModalService, findingId: number): void {
  modal.open(TemuanDetailModalComponent, { findingId });
}
