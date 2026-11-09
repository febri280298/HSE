import { Injectable, inject } from '@angular/core';
import { MessageService } from 'primeng/api';

/** Notifikasi kecil di pojok kanan atas. */
@Injectable({ providedIn: 'root' })
export class NotifyService {
  private readonly messages = inject(MessageService);

  success(detail: string, summary = 'Berhasil'): void {
    this.messages.add({ severity: 'success', summary, detail, life: 4000 });
  }

  warning(detail: string, summary = 'Perhatian'): void {
    this.messages.add({ severity: 'warn', summary, detail, life: 5000 });
  }

  danger(detail: string, summary = 'Gagal'): void {
    this.messages.add({ severity: 'error', summary, detail, life: 6000 });
  }
}
