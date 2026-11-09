import { Component, inject } from '@angular/core';
import { ButtonModule } from 'primeng/button';
import { DynamicDialogConfig, DynamicDialogRef } from 'primeng/dynamicdialog';

/**
 * Dialog konfirmasi (pengganti `window.confirm`). Hasil `onClose`: `true` kalau disetujui.
 * `modal.open(ConfirmDialogComponent, { title, message, confirmText, status: 'danger' }, { width: 'min(92vw, 28rem)' })`
 */
@Component({
  selector: 'app-confirm-dialog',
  imports: [ButtonModule],
  template: `
    <div class="dialog-frame">
      <div class="dialog-head">{{ title }}</div>
      <div class="dialog-body">{{ message }}</div>
      <div class="dialog-foot">
        <p-button label="Batal" [text]="true" severity="secondary" (onClick)="ref.close(false)" />
        <p-button [label]="confirmText" [severity]="severity" (onClick)="ref.close(true)" />
      </div>
    </div>
  `,
})
export class ConfirmDialogComponent {
  readonly ref = inject(DynamicDialogRef);
  private readonly config = inject(DynamicDialogConfig);

  readonly title: string = this.config.data?.title ?? 'Konfirmasi';
  readonly message: string = this.config.data?.message ?? '';
  readonly confirmText: string = this.config.data?.confirmText ?? 'Ya';
  private readonly status: 'primary' | 'danger' | 'warning' = this.config.data?.status ?? 'primary';
  readonly severity = this.status === 'warning' ? 'warn' : this.status;
}
