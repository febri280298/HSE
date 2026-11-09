import { Component, computed, inject, signal } from '@angular/core';
import { ButtonModule } from 'primeng/button';
import { ColDef } from 'ag-grid-community';
import { firstValueFrom } from 'rxjs';

import { PermissionInfo, Role } from '../../../core/models/admin.model';
import { AdminService } from '../../../core/services/admin.service';
import { AuthService } from '../../../core/services/auth.service';
import { ModalService } from '../../../core/services/modal.service';
import { NotifyService } from '../../../core/services/notify.service';
import { apiErrorMessage } from '../../../core/utils/api-error';
import { ConfirmDialogComponent } from '../../../shared/components/confirm-dialog/confirm-dialog.component';
import { DataGridComponent } from '../../../shared/components/data-grid/data-grid.component';
import { GridAction, detailAction } from '../../../shared/components/data-grid/grid-actions-cell.component';
import { cellOf, chipCell } from '../../../shared/components/data-grid/grid-cells';
import { openDetail } from '../../../shared/components/detail-modal/detail-modal.component';
import { RoleFormDialogComponent } from './role-form-dialog.component';

/** Daftar peran. Admin membuat peran sendiri dan mencentang hak aksesnya. */
@Component({
  selector: 'app-role-list',
  imports: [ButtonModule, DataGridComponent],
  templateUrl: './role-list.component.html',
})
export class RoleListComponent {
  private readonly admin = inject(AdminService);
  private readonly auth = inject(AuthService);
  private readonly dialog = inject(ModalService);
  private readonly toastr = inject(NotifyService);

  readonly roles = signal<Role[]>([]);
  readonly permissions = signal<PermissionInfo[]>([]);
  readonly loading = signal(true);
  private readonly permissionNames = computed(() => new Map(this.permissions().map((p) => [p.code, p.name])));

  readonly rowId = (role: Role) => String(role.roleId);
  readonly actions: GridAction<Role>[] = [
    detailAction((role) => this.showDetail(role)),
    { icon: 'pi pi-pencil', label: 'Ubah', visible: (role) => !role.isSystem, run: (role) => this.openForm(role) },
    { icon: 'pi pi-trash', label: 'Hapus', status: 'danger', visible: (role) => !role.isSystem, run: (role) => this.confirmDelete(role) },
  ];

  readonly columns = computed<ColDef<Role>[]>(() => [
    {
      headerName: 'Peran',
      field: 'name',
      minWidth: 180,
      cellRenderer: (p: { data?: Role }) =>
        p.data ? cellOf(p.data.name + ' ', p.data.isSystem ? chipCell('Sistem', 'primary') : null) : '',
    },
    { headerName: 'Keterangan', field: 'description', minWidth: 220, flex: 2, tooltipField: 'description' },
    { headerName: 'Hak akses', minWidth: 220, flex: 2, valueGetter: (p) => this.permissionsOf(p.data).join(', ') },
    { headerName: 'Pengguna', field: 'userCount', minWidth: 100, flex: 0, width: 110 },
  ]);

  constructor() {
    void this.load();
  }

  async load(): Promise<void> {
    this.loading.set(true);
    try {
      const [roles, permissions] = await Promise.all([
        firstValueFrom(this.admin.roles()),
        firstValueFrom(this.admin.permissions()),
      ]);
      this.permissions.set(permissions);
      this.roles.set(roles);
    } catch (err) {
      this.toastr.danger(apiErrorMessage(err), 'Peran');
    } finally {
      this.loading.set(false);
    }
  }

  openForm(role?: Role): void {
    this.dialog
      .open(RoleFormDialogComponent, { role, permissions: this.permissions() }, { closeOnBackdrop: false })
      ?.onClose.subscribe((saved?: boolean) => {
        if (saved) {
          this.toastr.success(role ? 'Peran diperbarui. Berlaku langsung.' : 'Peran ditambahkan.', 'Peran');
          void this.load();
          void this.auth.refresh(); // hak akses sendiri mungkin ikut berubah
        }
      });
  }

  private showDetail(role: Role): void {
    openDetail(this.dialog, role.name, [
      { label: 'Jenis', value: role.isSystem ? 'Peran sistem' : 'Peran buatan admin' },
      { label: 'Jumlah pengguna', value: role.userCount },
      { label: 'Keterangan', value: role.description, wide: true },
      { label: 'Hak akses', items: this.permissionsOf(role), wide: true },
    ]);
  }

  private permissionsOf(role?: Role): string[] {
    return (role?.permissions ?? []).map((code) => this.permissionNames().get(code) ?? code);
  }

  private confirmDelete(role: Role): void {
    this.dialog
      .open(
        ConfirmDialogComponent,
        {
          title: 'Hapus peran',
          message: `Hapus peran "${role.name}"? Tindakan ini tidak bisa dibatalkan.`,
          confirmText: 'Hapus',
          status: 'danger',
        },
        { width: 'min(92vw, 28rem)' },
      )
      ?.onClose.subscribe(async (ok?: boolean) => {
        if (!ok) {
          return;
        }
        try {
          await firstValueFrom(this.admin.deleteRole(role.roleId));
          this.toastr.success(`Peran "${role.name}" dihapus.`, 'Peran');
          await this.load();
        } catch (err) {
          this.toastr.danger(apiErrorMessage(err), 'Peran');
        }
      });
  }
}
