import { Component, inject } from '@angular/core';
import { ButtonModule } from 'primeng/button';
import { DynamicDialogConfig, DynamicDialogRef } from 'primeng/dynamicdialog';

import { ModalService } from '../../../core/services/modal.service';

/** Satu baris pada modal detail: nilai tunggal atau daftar label (`items`). */
export interface DetailField {
  label: string;
  value?: string | number | null;
  items?: string[];
  /** Selebar modal, untuk teks panjang atau daftar. */
  wide?: boolean;
}

/** Modal detail untuk baris tabel. Buka lewat `openDetail(modal, judul, field)`. */
@Component({
  selector: 'app-detail-modal',
  imports: [ButtonModule],
  template: `
    <div class="dialog-frame">
      <div class="dialog-head">
        <span>{{ title }}</span>
        <p-button icon="pi pi-times" [text]="true" [rounded]="true" severity="secondary" ariaLabel="Tutup" (onClick)="ref.close()" />
      </div>
      <div class="dialog-body">
        <dl class="detail-grid">
          @for (field of fields; track field.label) {
            <div [class.wide]="field.wide">
              <dt>{{ field.label }}</dt>
              <dd>
                @if (field.items) {
                  @for (item of field.items; track item) {
                    <span class="chip">{{ item }}</span>
                  } @empty {
                    —
                  }
                } @else {
                  {{ field.value === null || field.value === undefined || field.value === '' ? '—' : field.value }}
                }
              </dd>
            </div>
          }
        </dl>
      </div>
    </div>
  `,
})
export class DetailModalComponent {
  readonly ref = inject(DynamicDialogRef);
  private readonly config = inject(DynamicDialogConfig);

  readonly title: string = this.config.data?.title ?? '';
  readonly fields: DetailField[] = this.config.data?.fields ?? [];
}

export function openDetail(modal: ModalService, title: string, fields: DetailField[]): void {
  modal.open(DetailModalComponent, { title, fields });
}
