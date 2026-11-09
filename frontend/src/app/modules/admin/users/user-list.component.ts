import { Component, computed, inject, signal } from '@angular/core';
import { ButtonModule } from 'primeng/button';
import { IconFieldModule } from 'primeng/iconfield';
import { InputIconModule } from 'primeng/inputicon';
import { InputTextModule } from 'primeng/inputtext';
import { ColDef } from 'ag-grid-community';
import { firstValueFrom } from 'rxjs';

import { UserAccount, UserOptions } from '../../../core/models/admin.model';
import { AdminService } from '../../../core/services/admin.service';
import { AuthService } from '../../../core/services/auth.service';
import { ModalService } from '../../../core/services/modal.service';
import { NotifyService } from '../../../core/services/notify.service';
import { apiErrorMessage } from '../../../core/utils/api-error';
import { DataGridComponent } from '../../../shared/components/data-grid/data-grid.component';
import { GridAction, detailAction } from '../../../shared/components/data-grid/grid-actions-cell.component';
import { cellOf, chipCell, formatDateTime } from '../../../shared/components/data-grid/grid-cells';
import { openDetail } from '../../../shared/components/detail-modal/detail-modal.component';
import { ResetPasswordDialogComponent } from './reset-password-dialog.component';
import { UserFormDialogComponent } from './user-form-dialog.component';

interface UserStatus {
  text: string;
  tone: 'success' | 'warning' | 'danger';
}

@Component({
  selector: 'app-user-list',
  imports: [ButtonModule, IconFieldModule, InputIconModule, InputTextModule, DataGridComponent],
  templateUrl: './user-list.component.html',
  styles: `
    .toolbar { margin-bottom: 1rem; max-width: 22rem; }
  `,
})
export class UserListComponent {
  private readonly admin = inject(AdminService);
  private readonly auth = inject(AuthService);
  private readonly dialog = inject(ModalService);
  private readonly toastr = inject(NotifyService);

  readonly users = signal<UserAccount[]>([]);
  readonly options = signal<UserOptions>({ roles: [], plants: [], departments: [] });
  readonly loading = signal(true);
  readonly search = signal('');

  private readonly deptNames = computed(() => new Map(this.options().departments.map((d) => [d.deptId, d.name])));
  private readonly roleNames = computed(() => new Map(this.options().roles.map((r) => [r.roleId, r.name])));
  private readonly plantCodes = computed(() => new Map(this.options().plants.map((p) => [p.plantId, p.code])));

  readonly rowId = (user: UserAccount) => String(user.userId);
  readonly actions: GridAction<UserAccount>[] = [
    detailAction((user) => this.showDetail(user)),
    { icon: 'pi pi-pencil', label: 'Ubah', run: (user) => this.openForm(user) },
    { icon: 'pi pi-lock', label: 'Reset password', status: 'warning', run: (user) => this.openResetPassword(user) },
  ];

  readonly columns = computed<ColDef<UserAccount>[]>(() => [
    {
      headerName: 'Nama',
      field: 'displayName',
      minWidth: 180,
      flex: 2,
      cellRenderer: (p: { data?: UserAccount }) =>
        p.data ? cellOf(p.data.displayName + ' ', this.isSelf(p.data) ? chipCell('Anda', 'primary') : null) : '',
    },
    { headerName: 'Username', field: 'username', minWidth: 130 },
    { headerName: 'Departemen', minWidth: 130, valueGetter: (p) => this.deptOf(p.data) },
    { headerName: 'Peran', minWidth: 140, valueGetter: (p) => this.rolesOf(p.data).join(', ') },
    { headerName: 'Plant', minWidth: 120, valueGetter: (p) => this.plantsOf(p.data).join(', ') },
    {
      headerName: 'Status',
      minWidth: 170,
      valueGetter: (p) => (p.data ? this.status(p.data).text : ''),
      cellRenderer: (p: { data?: UserAccount }) => {
        if (!p.data) {
          return '';
        }
        const status = this.status(p.data);
        return chipCell(status.text, status.tone);
      },
    },
    {
      headerName: 'Login terakhir',
      minWidth: 140,
      valueGetter: (p) => p.data?.lastLoginAt ?? null,
      valueFormatter: (p) => (p.value ? formatDateTime(p.value) : 'Belum pernah'),
    },
  ]);

  constructor() {
    void this.load();
  }

  canAdd(): boolean {
    return !this.loading() && this.options().roles.length > 0;
  }

  async load(): Promise<void> {
    this.loading.set(true);
    try {
      const [users, options] = await Promise.all([
        firstValueFrom(this.admin.users()),
        firstValueFrom(this.admin.userOptions()),
      ]);
      this.options.set(options);
      this.users.set(users);
    } catch (err) {
      this.toastr.danger(apiErrorMessage(err), 'Pengguna');
    } finally {
      this.loading.set(false);
    }
  }

  onSearch(event: Event): void {
    this.search.set((event.target as HTMLInputElement).value);
  }

  openForm(user?: UserAccount): void {
    this.dialog
      .open(UserFormDialogComponent, { user, options: this.options(), isSelf: user ? this.isSelf(user) : false }, { closeOnBackdrop: false })
      ?.onClose.subscribe((saved?: boolean) => {
        if (saved) {
          this.toastr.success(user ? 'Data pengguna diperbarui.' : 'Pengguna ditambahkan.', 'Pengguna');
          void this.load();
          if (user && this.isSelf(user)) {
            void this.auth.refresh();
          }
        }
      });
  }

  private showDetail(user: UserAccount): void {
    openDetail(this.dialog, user.displayName, [
      { label: 'Username', value: user.username },
      { label: 'Email', value: user.email },
      { label: 'Departemen', value: this.deptOf(user) },
      { label: 'Status', value: this.status(user).text },
      { label: 'Login terakhir', value: user.lastLoginAt ? formatDateTime(user.lastLoginAt) : 'Belum pernah' },
      { label: 'Peran', items: this.rolesOf(user), wide: true },
      { label: 'Plant', items: this.plantsOf(user), wide: true },
    ]);
  }

  private openResetPassword(user: UserAccount): void {
    this.dialog
      .open(ResetPasswordDialogComponent, { user }, { width: 'min(94vw, 32rem)', closeOnBackdrop: false })
      ?.onClose.subscribe((done?: boolean) => {
        if (done) {
          void this.load();
        }
      });
  }

  private status(user: UserAccount): UserStatus {
    if (!user.isActive) {
      return { text: 'Nonaktif', tone: 'danger' };
    }
    if (user.isLocked) {
      return { text: 'Terkunci', tone: 'warning' };
    }
    if (user.mustChangePassword) {
      return { text: 'Belum ganti password', tone: 'warning' };
    }
    return { text: 'Aktif', tone: 'success' };
  }

  private isSelf(user: UserAccount): boolean {
    return user.userId === this.auth.user()?.userId;
  }

  private deptOf(user?: UserAccount): string {
    return user?.deptId ? (this.deptNames().get(user.deptId) ?? '') : '';
  }

  private rolesOf(user?: UserAccount): string[] {
    return (user?.roleIds ?? []).map((id) => this.roleNames().get(id) ?? '');
  }

  private plantsOf(user?: UserAccount): string[] {
    return (user?.plantIds ?? []).map((id) => this.plantCodes().get(id) ?? '');
  }
}
