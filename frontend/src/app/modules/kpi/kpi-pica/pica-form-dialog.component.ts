import { Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormControl, NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { DynamicDialogConfig, DynamicDialogRef } from 'primeng/dynamicdialog';
import { InputTextModule } from 'primeng/inputtext';
import { MessageModule } from 'primeng/message';
import { SelectModule } from 'primeng/select';
import { TextareaModule } from 'primeng/textarea';
import { firstValueFrom } from 'rxjs';

import { KPI_PICA_TYPES, KpiPica, KpiPicaStatus, KpiPicaType, KpiPlantOption } from '../../../core/models/kpi.model';
import { KpiService } from '../../../core/services/kpi.service';
import { apiErrorMessage, apiFieldErrors } from '../../../core/utils/api-error';
import { MONTH_NAMES } from '../../../core/utils/date';

/** Tambah/ubah PICA. Hasil `onClose`: `true` kalau tersimpan. */
@Component({
  selector: 'app-pica-form-dialog',
  imports: [ReactiveFormsModule, ButtonModule, InputTextModule, MessageModule, SelectModule, TextareaModule],
  templateUrl: './pica-form-dialog.component.html',
})
export class PicaFormDialogComponent {
  private readonly ref = inject(DynamicDialogRef);
  private readonly config = inject(DynamicDialogConfig);
  private readonly kpi = inject(KpiService);
  private readonly fb = inject(NonNullableFormBuilder);

  readonly pica: KpiPica | undefined = this.config.data?.pica;
  readonly plants: KpiPlantOption[] = this.config.data?.plants ?? [];
  private readonly year: number = this.pica?.year ?? this.config.data?.year;

  readonly months = MONTH_NAMES.map((label, i) => ({ label, value: i + 1 }));
  readonly types = KPI_PICA_TYPES;
  readonly statuses: KpiPicaStatus[] = ['Open', 'Closed'];

  readonly form = this.fb.group({
    plantId: new FormControl<number | null>(this.pica?.plantId ?? this.plants[0]?.plantId ?? null, Validators.required),
    deptId: new FormControl<number | null>(this.pica?.deptId ?? null, Validators.required),
    month: new FormControl<number | null>(this.pica?.month ?? new Date().getMonth() + 1, Validators.required),
    kpiType: new FormControl<KpiPicaType | null>(this.pica?.kpiType ?? null, Validators.required),
    problemIssue: [this.pica?.problemIssue ?? '', [Validators.required, Validators.maxLength(1000)]],
    correctiveAction: [this.pica?.correctiveAction ?? '', [Validators.required, Validators.maxLength(1000)]],
    dueDate: [this.pica?.dueDate ?? '', Validators.required],
    picName: [this.pica?.picName ?? '', [Validators.required, Validators.maxLength(100)]],
    status: this.fb.control<KpiPicaStatus>(this.pica?.status ?? 'Open', Validators.required),
  });

  private readonly selectedPlant = toSignal(this.form.controls.plantId.valueChanges, { initialValue: this.form.controls.plantId.value });
  readonly departments = computed(() => this.plants.find((p) => p.plantId === this.selectedPlant())?.departments ?? []);

  readonly busy = signal(false);
  readonly error = signal('');
  readonly submitted = signal(false);

  constructor() {
    this.form.controls.plantId.valueChanges.subscribe(() => {
      if (!this.departments().some((d) => d.deptId === this.form.controls.deptId.value)) {
        this.form.controls.deptId.setValue(this.departments().length === 1 ? this.departments()[0].deptId : null);
      }
    });
    if (!this.pica && this.departments().length === 1) {
      this.form.controls.deptId.setValue(this.departments()[0].deptId);
    }
  }

  invalid(name: keyof typeof this.form.controls): boolean {
    return this.submitted() && this.form.controls[name].invalid;
  }

  async save(): Promise<void> {
    this.submitted.set(true);
    if (this.form.invalid) {
      return;
    }
    const v = this.form.getRawValue();
    const request = {
      plantId: v.plantId!,
      deptId: v.deptId!,
      year: this.year,
      month: v.month!,
      kpiType: v.kpiType!,
      problemIssue: v.problemIssue.trim(),
      correctiveAction: v.correctiveAction.trim(),
      dueDate: v.dueDate,
      picName: v.picName.trim(),
      status: v.status,
    };
    this.busy.set(true);
    this.error.set('');
    try {
      await firstValueFrom<unknown>(this.pica ?this.kpi.updatePica(this.pica.picaId, request) : this.kpi.createPica(request));
      this.ref.close(true);
    } catch (err) {
      const fields = Object.values(apiFieldErrors(err));
      this.error.set(fields[0] ?? apiErrorMessage(err));
    } finally {
      this.busy.set(false);
    }
  }

  cancel(): void {
    this.ref.close(false);
  }
}
