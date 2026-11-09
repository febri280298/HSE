import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { DynamicDialogConfig, DynamicDialogRef } from 'primeng/dynamicdialog';
import { InputGroupModule } from 'primeng/inputgroup';
import { InputTextModule } from 'primeng/inputtext';
import { MessageModule } from 'primeng/message';
import { firstValueFrom } from 'rxjs';

import { UserAccount } from '../../../core/models/admin.model';
import { AdminService, generatePassword } from '../../../core/services/admin.service';
import { apiErrorMessage } from '../../../core/utils/api-error';

/**
 * Reset password oleh admin: pengguna dikeluarkan dari semua perangkat, kunci akun dibuka,
 * dan wajib mengganti password sementara ini saat login berikutnya.
 */
@Component({
  selector: 'app-reset-password-dialog',
  imports: [FormsModule, ButtonModule, InputGroupModule, InputTextModule, MessageModule],
  template: `
    <div class="dialog-frame">
      <div class="dialog-head">Reset Password · {{ user.displayName }}</div>
      <div class="dialog-body">
        @if (done()) {
          <p-message severity="success">
            Password {{ user.displayName }} sudah di-reset. Berikan password sementara di bawah; pengguna wajib
            menggantinya saat login.
          </p-message>
        } @else {
          <p>Pengguna akan keluar dari semua perangkat. Kalau akun terkunci karena salah password, kuncinya ikut dibuka.</p>
        }
        @if (error()) {
          <p-message severity="error" role="alert">{{ error() }}</p-message>
        }
        <label class="label" for="reset-password">Password sementara</label>
        <p-inputgroup>
          <input pInputText id="reset-password" autocomplete="off" [readonly]="done()"
                 [ngModel]="password()" (ngModelChange)="password.set($event)" />
          @if (!done()) {
            <p-button icon="pi pi-refresh" severity="secondary" ariaLabel="Buat password acak" (onClick)="password.set(generate())" />
          }
        </p-inputgroup>
      </div>
      <div class="dialog-foot">
        @if (done()) {
          <p-button label="Selesai" (onClick)="ref.close(true)" />
        } @else {
          <p-button label="Batal" [text]="true" severity="secondary" (onClick)="ref.close(false)" />
          <p-button [label]="busy() ? 'Memproses…' : 'Reset Password'" severity="warn" [disabled]="busy()" (onClick)="reset()" />
        }
      </div>
    </div>
  `,
})
export class ResetPasswordDialogComponent {
  readonly ref = inject(DynamicDialogRef);
  private readonly admin = inject(AdminService);
  private readonly config = inject(DynamicDialogConfig);

  readonly user: UserAccount = this.config.data.user;

  readonly password = signal(generatePassword());
  readonly busy = signal(false);
  readonly done = signal(false);
  readonly error = signal('');
  readonly generate = generatePassword;

  async reset(): Promise<void> {
    this.busy.set(true);
    this.error.set('');
    try {
      await firstValueFrom(this.admin.resetPassword(this.user.userId, this.password()));
      this.done.set(true);
    } catch (err) {
      this.error.set(apiErrorMessage(err));
    } finally {
      this.busy.set(false);
    }
  }
}
