import { Injectable, Type, inject } from '@angular/core';
import { DialogService, DynamicDialogRef } from 'primeng/dynamicdialog';

export interface ModalOptions {
  width?: string;
  /** false: klik di luar modal tidak menutupnya (form yang sedang diisi). */
  closeOnBackdrop?: boolean;
}

/** Membuka komponen sebagai modal. Isi `data` dibaca komponen lewat `DynamicDialogConfig`. */
@Injectable({ providedIn: 'root' })
export class ModalService {
  private readonly dialog = inject(DialogService);

  open<T>(component: Type<T>, data: object, options: ModalOptions = {}): DynamicDialogRef | null {
    return this.dialog.open(component, {
      data,
      modal: true,
      showHeader: false,
      closable: true,
      closeOnEscape: true,
      dismissableMask: options.closeOnBackdrop ?? true,
      width: options.width ?? 'min(94vw, 42rem)',
      styleClass: 'app-dialog',
    });
  }
}
