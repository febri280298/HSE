import { DatePipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { FormControl, NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { MessageModule } from 'primeng/message';
import { SelectModule } from 'primeng/select';
import { SelectButtonModule } from 'primeng/selectbutton';
import { TextareaModule } from 'primeng/textarea';
import { firstValueFrom } from 'rxjs';

import { PERMISSIONS } from '../../../core/models/auth.model';
import { ImageResponse } from '../../../core/models/image-response.model';
import { FindingOptions, FindingType, GenbaSchedule } from '../../../core/models/temuan.model';
import { AuthService } from '../../../core/services/auth.service';
import { NotifyService } from '../../../core/services/notify.service';
import { TemuanService } from '../../../core/services/temuan.service';
import { apiErrorMessage, apiFieldErrors } from '../../../core/utils/api-error';
import { toIsoDate } from '../../../core/utils/date';
import { ImageUploaderComponent } from '../../../shared/components/image-uploader/image-uploader.component';

/**
 * Form temuan genba. Patroller hanya mencatat apa yang ditemukan; departemen dan due date ditentukan tim HSE.
 * Setelah tersimpan, form tetap terbuka untuk temuan berikutnya dalam genba yang sama.
 */
@Component({
  selector: 'app-temuan-form',
  imports: [
    DatePipe,
    ReactiveFormsModule,
    RouterLink,
    ButtonModule,
    InputTextModule,
    MessageModule,
    SelectModule,
    SelectButtonModule,
    TextareaModule,
    ImageUploaderComponent,
  ],
  templateUrl: './temuan-form.component.html',
  styleUrl: './temuan-form.component.scss',
})
export class TemuanFormComponent {
  private readonly fb = inject(NonNullableFormBuilder);
  private readonly toastr = inject(NotifyService);
  private readonly temuan = inject(TemuanService);
  private readonly auth = inject(AuthService);

  readonly tipeOptions: FindingType[] = ['Safety', '5R'];
  readonly maxDate = toIsoDate(new Date());

  readonly form = this.fb.group({
    plantId: new FormControl<number | null>(null, Validators.required),
    patrolDate: [toIsoDate(new Date()), Validators.required],
    fotoTemuan: this.fb.control<ImageResponse[]>([], Validators.required),
    detail: ['', [Validators.required, Validators.maxLength(500)]],
    areaId: new FormControl<number | null>(null, Validators.required),
    tipe: this.fb.control<FindingType>('Safety', Validators.required),
  });

  readonly options = signal<FindingOptions>({ plants: [], areas: [], departments: [], pics: [] });
  readonly schedules = signal<GenbaSchedule[]>([]);
  readonly loading = signal(true);
  readonly uploading = signal(false);
  readonly saving = signal(false);
  private readonly submitted = signal(false);
  readonly error = signal('');
  readonly fieldErrors = signal<Record<string, string>>({});
  readonly canManageMaster = computed(() => {
    this.auth.user();
    return this.auth.can(PERMISSIONS.masterKelola);
  });

  constructor() {
    void this.init();
  }

  private async init(): Promise<void> {
    try {
      const [options, schedules] = await Promise.all([
        firstValueFrom(this.temuan.options()),
        firstValueFrom(this.temuan.genba(new Date().getFullYear())),
      ]);
      this.options.set(options);
      this.schedules.set(schedules);
      if (options.plants.length) {
        // Plant pengguna lebih dulu; kalau hanya satu, langsung terpilih.
        const own = this.auth.user()?.plants[0]?.plantId;
        await this.selectPlant(own ?? options.plants[0].plantId);
      }
    } catch (err) {
      this.error.set(apiErrorMessage(err));
    } finally {
      this.loading.set(false);
    }
  }

  async selectPlant(plantId: number): Promise<void> {
    this.form.patchValue({ plantId, areaId: null });
    try {
      this.options.set(await firstValueFrom(this.temuan.options(plantId)));
    } catch (err) {
      this.error.set(apiErrorMessage(err));
    }
  }

  /** Jadwal genba plant terpilih di bulan tanggal patrol; hanya informasi, tidak membatasi input. */
  plannedGenba(): string | null {
    const { plantId, patrolDate } = this.form.getRawValue();
    const month = Number(patrolDate.slice(5, 7));
    return this.schedules().find((s) => s.plantId === plantId && s.month === month)?.plannedDate ?? null;
  }

  /** Tanda merah baru muncul setelah tombol simpan ditekan, bukan saat pengguna masih mengisi. */
  showError(name: keyof typeof this.form.controls): boolean {
    return !!this.fieldErrors()[name] || (this.submitted() && this.form.controls[name].invalid);
  }

  async submit(): Promise<void> {
    if (this.form.invalid) {
      this.submitted.set(true);
      this.form.markAllAsTouched();
      // Di HP tombol menempel di bawah, jadi kolom yang salah bisa berada di luar layar.
      setTimeout(() => document.querySelector('app-temuan-form .status-danger')?.scrollIntoView({ behavior: 'smooth', block: 'center' }));
      return;
    }

    const value = this.form.getRawValue();
    this.saving.set(true);
    this.error.set('');
    this.fieldErrors.set({});
    try {
      const saved = await firstValueFrom(
        this.temuan.create({
          plantId: value.plantId!,
          patrolDate: value.patrolDate,
          detail: value.detail.trim(),
          areaId: value.areaId!,
          type: value.tipe,
          photos: value.fotoTemuan.map((f) => ({ path: f.path, fileName: f.fileName })),
        }),
      );
      this.toastr.success(`Temuan ${saved.number ?? ''} tersimpan dan menunggu penugasan tim HSE.`, 'Temuan');
      // Plant dan tanggal biasanya sama untuk satu kali genba: dibiarkan.
      this.form.patchValue({ fotoTemuan: [], detail: '', areaId: null });
      // Kolom yang baru dikosongkan bukan kesalahan pengguna; tanpa ini langsung ditandai merah.
      this.form.markAsPristine();
      this.form.markAsUntouched();
      this.submitted.set(false);
      window.scrollTo({ top: 0, behavior: 'smooth' });
    } catch (err) {
      const fields = apiFieldErrors(err);
      this.fieldErrors.set(fields);
      this.error.set(Object.keys(fields).length ? 'Periksa kembali isian yang ditandai.' : apiErrorMessage(err));
    } finally {
      this.saving.set(false);
    }
  }
}
