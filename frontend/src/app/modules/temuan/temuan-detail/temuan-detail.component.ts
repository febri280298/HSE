import { DatePipe } from '@angular/common';
import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { MessageModule } from 'primeng/message';
import { SelectModule } from 'primeng/select';
import { TextareaModule } from 'primeng/textarea';
import { firstValueFrom } from 'rxjs';

import { ImageResponse } from '../../../core/models/image-response.model';
import { FindingDetail, FindingOptions, FindingPhoto, STATUS_INFO } from '../../../core/models/temuan.model';
import { ModalService } from '../../../core/services/modal.service';
import { NotifyService } from '../../../core/services/notify.service';
import { TemuanService } from '../../../core/services/temuan.service';
import { apiErrorMessage } from '../../../core/utils/api-error';
import { addDays, toIsoDate } from '../../../core/utils/date';
import { ImagePreviewComponent } from '../../../shared/components/image-preview/image-preview.component';
import { ImageUploaderComponent } from '../../../shared/components/image-uploader/image-uploader.component';

const EVENT_LABEL: Record<string, string> = {
  Dibuat: 'Temuan dicatat',
  Ditugaskan: 'Ditugaskan ke departemen',
  Progres: 'Progres perbaikan',
  Diajukan: 'Perbaikan diajukan',
  Ditolak: 'Perbaikan ditolak',
  Ditutup: 'Temuan ditutup (Close)',
};

/**
 * Detail temuan + aksi sesuai peran: tim HSE menentukan departemen, user departemen mengisi perbaikan,
 * pemeriksa menutup atau menolak.
 */
@Component({
  selector: 'app-temuan-detail',
  imports: [
    DatePipe,
    FormsModule,
    RouterLink,
    ButtonModule,
    InputTextModule,
    MessageModule,
    SelectModule,
    TextareaModule,
    ImageUploaderComponent,
  ],
  templateUrl: './temuan-detail.component.html',
  styleUrl: './temuan-detail.component.scss',
})
export class TemuanDetailComponent {
  private readonly temuan = inject(TemuanService);
  private readonly dialog = inject(ModalService);
  private readonly toastr = inject(NotifyService);

  /** Dari alamat `/temuan/:id` (withComponentInputBinding). */
  readonly id = input.required<string>();

  readonly detail = signal<FindingDetail | null>(null);
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly uploading = signal(false);
  readonly error = signal('');

  readonly departments = signal<FindingOptions['departments']>([]);
  private readonly pics = signal<FindingOptions['pics']>([]);
  readonly assignDept = signal<number | null>(null);
  readonly assignPic = signal<number | null>(null);
  readonly assignDue = signal('');
  /** Calon PIC hanya dari departemen yang dipilih. */
  readonly picOptions = computed(() => this.pics().filter((p) => p.deptId === this.assignDept()));
  readonly deptChoices = computed(() => this.departments().map((d) => ({ label: `${d.code} · ${d.name}`, value: d.deptId })));

  readonly countermeasure = signal('');
  readonly repairPhotos = signal<ImageResponse[]>([]);
  readonly verifyNote = signal('');

  readonly status = computed(() => {
    const d = this.detail();
    return d ? STATUS_INFO[d.status] : null;
  });
  readonly beforePhotos = computed(() => this.detail()?.photos.filter((p) => p.kind === 'Temuan') ?? []);
  readonly afterPhotos = computed(() => this.detail()?.photos.filter((p) => p.kind === 'Perbaikan') ?? []);

  constructor() {
    effect(() => void this.load(Number(this.id())));
  }

  eventLabel(action: string): string {
    return EVENT_LABEL[action] ?? action;
  }

  preview(photos: FindingPhoto[], startIndex: number): void {
    this.dialog.open(ImagePreviewComponent, { images: photos, startIndex }, { width: 'min(94vw, 1100px)' });
  }

  /** Ganti departemen: PIC lama bukan anggota departemen baru, jadi harus dipilih ulang. */
  changeDept(deptId: number): void {
    if (deptId !== this.assignDept()) {
      this.assignDept.set(deptId);
      this.assignPic.set(null);
    }
  }

  async assign(): Promise<void> {
    const deptId = this.assignDept();
    const picUserId = this.assignPic();
    if (!deptId || !picUserId || !this.assignDue()) {
      return;
    }
    await this.run('Temuan ditugaskan ke departemen.', (d) => this.temuan.assign(d.findingId, deptId, picUserId, this.assignDue()));
  }

  async saveRepair(selesai: boolean): Promise<void> {
    await this.run(selesai ? 'Perbaikan diajukan untuk diperiksa.' : 'Progres tersimpan.', (d) =>
      this.temuan.saveRepair(
        d.findingId,
        this.countermeasure().trim(),
        this.repairPhotos().map((p) => ({ path: p.path, fileName: p.fileName })),
        selesai,
      ),
    );
  }

  async verify(setuju: boolean): Promise<void> {
    await this.run(setuju ? 'Temuan ditutup (Close).' : 'Perbaikan dikembalikan ke departemen.', (d) =>
      this.temuan.verify(d.findingId, setuju, this.verifyNote().trim() || null),
    );
  }

  private async run(success: string, action: (d: FindingDetail) => ReturnType<TemuanService['get']>): Promise<void> {
    const current = this.detail();
    if (!current) {
      return;
    }
    this.busy.set(true);
    this.error.set('');
    try {
      await this.show(await firstValueFrom(action(current)));
      this.toastr.success(success, current.number);
    } catch (err) {
      this.error.set(apiErrorMessage(err));
    } finally {
      this.busy.set(false);
    }
  }

  private async load(findingId: number): Promise<void> {
    this.loading.set(true);
    this.error.set('');
    try {
      await this.show(await firstValueFrom(this.temuan.get(findingId)));
    } catch (err) {
      this.detail.set(null);
      this.error.set(apiErrorMessage(err));
    } finally {
      this.loading.set(false);
    }
  }

  private async show(detail: FindingDetail): Promise<void> {
    this.detail.set(detail);
    this.assignDept.set(detail.deptId);
    this.assignPic.set(detail.picUserId);
    this.assignDue.set(detail.dueDate ?? toIsoDate(addDays(new Date(), 7)));
    if (detail.canAssign) {
      const options = await firstValueFrom(this.temuan.options(detail.plantId));
      this.departments.set(options.departments);
      this.pics.set(options.pics);
    }
    this.countermeasure.set(detail.countermeasure ?? '');
    this.verifyNote.set('');
    // Foto perbaikan yang sudah tersimpan ikut dimuat ke uploader supaya bisa dilanjutkan.
    this.repairPhotos.set(
      detail.photos
        .filter((p) => p.kind === 'Perbaikan')
        .map((p, i) => ({
          id: String(i),
          path: p.path,
          url: p.url,
          fileName: p.fileName,
          contentType: 'image/jpeg',
          size: 0,
          category: 'perbaikan',
          uploadedAt: '',
        })),
    );
  }
}
