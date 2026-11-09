import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule, NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { CheckboxModule } from 'primeng/checkbox';
import { DynamicDialogConfig, DynamicDialogRef } from 'primeng/dynamicdialog';
import { InputGroupModule } from 'primeng/inputgroup';
import { InputTextModule } from 'primeng/inputtext';
import { MessageModule } from 'primeng/message';
import { SelectModule } from 'primeng/select';
import { ToggleSwitchModule } from 'primeng/toggleswitch';
import { firstValueFrom } from 'rxjs';

import { UserAccount, UserOptions } from '../../../core/models/admin.model';
import { AdminService, generatePassword } from '../../../core/services/admin.service';
import { apiErrorMessage, apiFieldErrors } from '../../../core/utils/api-error';

/** Tambah/ubah pengguna. Hasil `onClose`: `true` kalau tersimpan. */
@Component({
  selector: 'app-user-form-dialog',
  imports: [
    FormsModule,
    ReactiveFormsModule,
    ButtonModule,
    CheckboxModule,
    InputGroupModule,
    InputTextModule,
    MessageModule,
    SelectModule,
    ToggleSwitchModule,
  ],
  templateUrl: './user-form-dialog.component.html',
  styles: `
    .choices { display: grid; grid-template-columns: repeat(auto-fill, minmax(11rem, 1fr)); gap: 0.5rem 1rem; }
    .choice { display: inline-flex; align-items: center; gap: 0.5rem; }
    .switch { display: inline-flex; align-items: center; gap: 0.625rem; }
  `,
})
export class UserFormDialogComponent implements OnInit {
  private readonly ref = inject(DynamicDialogRef);
  private readonly config = inject(DynamicDialogConfig);
  private readonly admin = inject(AdminService);
  private readonly fb = inject(NonNullableFormBuilder);

  /** Pengguna yang diubah (kosong = tambah baru). */
  readonly user: UserAccount | undefined = this.config.data?.user;
  readonly options: UserOptions = this.config.data?.options ?? { roles: [], plants: [], departments: [] };
  /** Pengguna yang diubah adalah diri sendiri. */
  readonly isSelf: boolean = this.config.data?.isSelf ?? false;
  readonly deptChoices = [
    { label: 'Tanpa departemen', value: 0 },
    ...this.options.departments.map((d) => ({ label: `${d.code} · ${d.name}`, value: d.deptId })),
  ];

  readonly form = this.fb.group({
    username: ['', [Validators.required, Validators.pattern(/^[A-Za-z0-9._-]{3,50}$/)]],
    displayName: ['', [Validators.required, Validators.maxLength(100)]],
    email: ['', [Validators.email, Validators.maxLength(200)]],
    password: ['', [Validators.required, Validators.minLength(8)]],
    // 0 = tanpa departemen; dikirim ke server sebagai null.
    deptId: [0],
    isActive: [true],
  });
  readonly roleIds = signal(new Set<number>());
  readonly plantIds = signal(new Set<number>());
  readonly busy = signal(false);
  readonly error = signal('');
  readonly fieldErrors = signal<Record<string, string>>({});

  ngOnInit(): void {
    if (this.user) {
      this.form.patchValue({
        username: this.user.username,
        displayName: this.user.displayName,
        email: this.user.email ?? '',
        deptId: this.user.deptId ?? 0,
        isActive: this.user.isActive,
      });
      this.form.controls.username.disable();
      this.form.controls.password.disable();
      this.roleIds.set(new Set(this.user.roleIds));
      this.plantIds.set(new Set(this.user.plantIds));
    } else {
      this.form.controls.password.setValue(generatePassword());
    }
    if (this.isSelf) {
      this.form.controls.isActive.disable();
    }
  }

  toggleRole(roleId: number, checked: boolean): void {
    this.roleIds.update((set) => toggled(set, roleId, checked));
  }

  togglePlant(plantId: number, checked: boolean): void {
    this.plantIds.update((set) => toggled(set, plantId, checked));
  }

  newPassword(): void {
    this.form.controls.password.setValue(generatePassword());
  }

  async save(): Promise<void> {
    if (this.form.invalid || this.roleIds().size === 0) {
      this.form.markAllAsTouched();
      if (this.roleIds().size === 0) {
        this.fieldErrors.set({ roleIds: 'Pilih minimal satu peran.' });
      }
      return;
    }

    const value = this.form.getRawValue();
    const data = {
      displayName: value.displayName.trim(),
      email: value.email.trim() || null,
      deptId: value.deptId || null,
      isActive: value.isActive,
      roleIds: [...this.roleIds()],
      plantIds: [...this.plantIds()],
    };

    this.busy.set(true);
    this.error.set('');
    this.fieldErrors.set({});
    try {
      await firstValueFrom(
        this.user
          ? this.admin.updateUser(this.user.userId, data)
          : this.admin.createUser({ ...data, username: value.username.trim(), password: value.password }),
      );
      this.ref.close(true);
    } catch (err) {
      const fields = apiFieldErrors(err);
      this.fieldErrors.set(fields);
      if (!Object.keys(fields).length) {
        this.error.set(apiErrorMessage(err));
      }
    } finally {
      this.busy.set(false);
    }
  }

  cancel(): void {
    this.ref.close(false);
  }
}

function toggled(set: Set<number>, id: number, checked: boolean): Set<number> {
  const next = new Set(set);
  if (checked) {
    next.add(id);
  } else {
    next.delete(id);
  }
  return next;
}
