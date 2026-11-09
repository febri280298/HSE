import { Component, computed, inject, signal } from '@angular/core';
import {
  AbstractControl,
  NonNullableFormBuilder,
  ReactiveFormsModule,
  ValidationErrors,
  Validators,
} from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { MessageModule } from 'primeng/message';

import { AuthService } from '../../../core/services/auth.service';
import { NotifyService } from '../../../core/services/notify.service';
import { apiErrorMessage, apiFieldErrors } from '../../../core/utils/api-error';
import { BrandMarkComponent } from '../../../layout/brand-mark.component';

const sameAsNew = (group: AbstractControl): ValidationErrors | null =>
  group.get('newPassword')?.value === group.get('confirmPassword')?.value ? null : { mismatch: true };

/** Ganti password sendiri. Wajib dilakukan pada login pertama / setelah password di-reset admin. */
@Component({
  selector: 'app-change-password',
  imports: [ReactiveFormsModule, RouterLink, ButtonModule, InputTextModule, MessageModule, BrandMarkComponent],
  templateUrl: './change-password.component.html',
  styleUrl: '../auth-page.scss',
})
export class ChangePasswordComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly toastr = inject(NotifyService);
  private readonly fb = inject(NonNullableFormBuilder);

  readonly forced = computed(() => this.auth.user()?.mustChangePassword ?? false);
  readonly displayName = computed(() => this.auth.user()?.displayName ?? '');

  readonly form = this.fb.group(
    {
      currentPassword: ['', Validators.required],
      newPassword: ['', [Validators.required, Validators.minLength(8)]],
      confirmPassword: ['', Validators.required],
    },
    { validators: sameAsNew },
  );
  readonly busy = signal(false);
  readonly error = signal('');
  readonly fieldErrors = signal<Record<string, string>>({});

  async submit(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.busy.set(true);
    this.error.set('');
    this.fieldErrors.set({});
    try {
      const { currentPassword, newPassword } = this.form.getRawValue();
      await this.auth.changePassword(currentPassword, newPassword);
      this.toastr.success('Password berhasil diganti.', 'Password');
      await this.router.navigateByUrl('/dashboard');
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

  logout(): void {
    void this.auth.logout();
  }
}
