import { Component, inject, input, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { MessageModule } from 'primeng/message';
import { PasswordModule } from 'primeng/password';

import { AuthService } from '../../../core/services/auth.service';
import { apiErrorMessage } from '../../../core/utils/api-error';
import { BrandMarkComponent } from '../../../layout/brand-mark.component';

@Component({
  selector: 'app-login',
  imports: [ReactiveFormsModule, ButtonModule, InputTextModule, MessageModule, PasswordModule, BrandMarkComponent],
  templateUrl: './login.component.html',
  styleUrl: '../auth-page.scss',
})
export class LoginComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly fb = inject(NonNullableFormBuilder);

  /** Dari query string (withComponentInputBinding): halaman tujuan setelah login. */
  readonly returnUrl = input<string>();
  /** `habis` = dikeluarkan karena sesi berakhir. */
  readonly sesi = input<string>();

  readonly form = this.fb.group({
    username: ['', Validators.required],
    password: ['', Validators.required],
  });
  readonly busy = signal(false);
  readonly error = signal('');

  async submit(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.busy.set(true);
    this.error.set('');
    try {
      const { username, password } = this.form.getRawValue();
      const user = await this.auth.login(username, password);
      const target = user.mustChangePassword ? '/ganti-password' : safeReturnUrl(this.returnUrl());
      await this.router.navigateByUrl(target);
    } catch (err) {
      this.error.set(apiErrorMessage(err, 'Login gagal.'));
      this.form.controls.password.reset();
    } finally {
      this.busy.set(false);
    }
  }
}

/** Hanya alamat di dalam aplikasi ini (cegah redirect ke situs lain lewat ?returnUrl=). */
function safeReturnUrl(url: string | undefined): string {
  return url && url.startsWith('/') && !url.startsWith('//') && !url.startsWith('/login') ? url : '/dashboard';
}
