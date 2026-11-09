import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule, NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { CheckboxModule } from 'primeng/checkbox';
import { DynamicDialogConfig, DynamicDialogRef } from 'primeng/dynamicdialog';
import { InputTextModule } from 'primeng/inputtext';
import { MessageModule } from 'primeng/message';
import { firstValueFrom } from 'rxjs';

import { PermissionInfo, Role, RoleRequest } from '../../../core/models/admin.model';
import { AdminService } from '../../../core/services/admin.service';
import { apiErrorMessage, apiFieldErrors } from '../../../core/utils/api-error';

interface PermissionGroup {
  name: string;
  items: PermissionInfo[];
}

/** Tambah/ubah peran. Hasil `onClose`: `true` kalau tersimpan. */
@Component({
  selector: 'app-role-form-dialog',
  imports: [FormsModule, ReactiveFormsModule, ButtonModule, CheckboxModule, InputTextModule, MessageModule],
  templateUrl: './role-form-dialog.component.html',
  styles: `
    .group { margin-bottom: 1rem; }
    .group-title { display: flex; align-items: center; gap: 0.5rem; margin-bottom: 0.25rem; }
    .permission { display: grid; grid-template-columns: auto 1fr; column-gap: 0.5rem; align-items: center; padding: 0.375rem 0; }
    .permission .hint { grid-column: 2; color: var(--text-color-secondary); font-size: 0.875rem; }
  `,
})
export class RoleFormDialogComponent implements OnInit {
  private readonly ref = inject(DynamicDialogRef);
  private readonly config = inject(DynamicDialogConfig);
  private readonly admin = inject(AdminService);
  private readonly fb = inject(NonNullableFormBuilder);

  /** Peran yang diubah (kosong = tambah baru). */
  readonly role: Role | undefined = this.config.data?.role;
  /** Daftar semua hak akses. */
  readonly permissions: PermissionInfo[] = this.config.data?.permissions ?? [];

  readonly form = this.fb.group({
    name: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(50)]],
    description: ['', Validators.maxLength(200)],
  });
  readonly selected = signal(new Set<string>());
  readonly busy = signal(false);
  readonly error = signal('');
  readonly fieldErrors = signal<Record<string, string>>({});
  groups: PermissionGroup[] = [];

  ngOnInit(): void {
    if (this.role) {
      this.form.patchValue({ name: this.role.name, description: this.role.description ?? '' });
      this.selected.set(new Set(this.role.permissions));
    }
    const groups = new Map<string, PermissionInfo[]>();
    for (const permission of this.permissions) {
      groups.set(permission.group, [...(groups.get(permission.group) ?? []), permission]);
    }
    this.groups = [...groups].map(([name, items]) => ({ name, items }));
  }

  isChecked(code: string): boolean {
    return this.selected().has(code);
  }

  groupState(group: PermissionGroup): 'all' | 'some' | 'none' {
    const count = group.items.filter((p) => this.isChecked(p.code)).length;
    return count === 0 ? 'none' : count === group.items.length ? 'all' : 'some';
  }

  toggle(codes: string[], checked: boolean): void {
    this.selected.update((current) => {
      const next = new Set(current);
      codes.forEach((code) => (checked ? next.add(code) : next.delete(code)));
      return next;
    });
  }

  toggleGroup(group: PermissionGroup, checked: boolean): void {
    this.toggle(group.items.map((p) => p.code), checked);
  }

  async save(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const value = this.form.getRawValue();
    const request: RoleRequest = {
      name: value.name.trim(),
      description: value.description.trim() || null,
      permissions: this.permissions.map((p) => p.code).filter((code) => this.isChecked(code)),
    };

    this.busy.set(true);
    this.error.set('');
    this.fieldErrors.set({});
    try {
      await firstValueFrom(
        this.role ? this.admin.updateRole(this.role.roleId, request) : this.admin.createRole(request),
      );
      this.ref.close(true);
    } catch (err) {
      const fields = apiFieldErrors(err);
      this.fieldErrors.set(fields);
      if (!fields['name'] && !fields['description']) {
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
