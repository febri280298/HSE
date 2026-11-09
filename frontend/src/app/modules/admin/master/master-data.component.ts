import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { CheckboxModule } from 'primeng/checkbox';
import { InputTextModule } from 'primeng/inputtext';
import { SelectModule } from 'primeng/select';
import { CellValueChangedEvent, ColDef } from 'ag-grid-community';
import { firstValueFrom } from 'rxjs';

import { PlantInfo } from '../../../core/models/auth.model';
import { Area, Department } from '../../../core/models/temuan.model';
import { AdminService } from '../../../core/services/admin.service';
import { KpiService } from '../../../core/services/kpi.service';
import { ModalService } from '../../../core/services/modal.service';
import { NotifyService } from '../../../core/services/notify.service';
import { TemuanService } from '../../../core/services/temuan.service';
import { apiErrorMessage } from '../../../core/utils/api-error';
import { DataGridComponent } from '../../../shared/components/data-grid/data-grid.component';
import { GridAction, detailAction } from '../../../shared/components/data-grid/grid-actions-cell.component';
import { openDetail } from '../../../shared/components/detail-modal/detail-modal.component';

/** Daftar area per plant dan departemen. Klik sel untuk mengubah; data yang sudah dipakai dinonaktifkan, bukan dihapus. */
@Component({
  selector: 'app-master-data',
  imports: [FormsModule, ButtonModule, CheckboxModule, InputTextModule, SelectModule, DataGridComponent],
  templateUrl: './master-data.component.html',
  styles: `
    .add { display: flex; flex-wrap: wrap; gap: 0.5rem; margin-bottom: 1rem; }
    .add input { flex: 1 1 8rem; min-width: 0; }
    .add .code { flex: 0 1 6rem; }
    .plant { margin-bottom: 1rem; }
    .kpi-plant { max-width: 20rem; margin-bottom: 1rem; }
    .kpi-depts { display: grid; grid-template-columns: repeat(auto-fill, minmax(13rem, 1fr)); gap: 0.75rem 1rem; }
    .kpi-dept { display: flex; align-items: center; gap: 0.5rem; }
  `,
})
export class MasterDataComponent {
  private readonly admin = inject(AdminService);
  private readonly kpi = inject(KpiService);
  private readonly temuan = inject(TemuanService);
  private readonly toastr = inject(NotifyService);
  private readonly dialog = inject(ModalService);

  readonly plants = signal<PlantInfo[]>([]);
  readonly plantId = signal(0);
  readonly areas = signal<Area[]>([]);
  readonly departments = signal<Department[]>([]);
  readonly loading = signal(true);

  readonly newArea = signal('');
  readonly newDeptCode = signal('');
  readonly newDeptName = signal('');

  /** Departemen yang wajib melapor KPI K3 di plant terpilih. */
  readonly kpiPlantId = signal(0);
  readonly kpiDepts = signal<number[]>([]);

  readonly areaId = (area: Area) => String(area.areaId);
  readonly deptId = (dept: Department) => String(dept.deptId);

  readonly areaColumns: ColDef<Area>[] = [
    { headerName: 'Nama Area', field: 'name', editable: true, minWidth: 160 },
    { headerName: 'Aktif', field: 'isActive', editable: true, flex: 0, width: 90, minWidth: 90 },
  ];
  readonly deptColumns: ColDef<Department>[] = [
    { headerName: 'Kode', field: 'code', editable: true, flex: 0, width: 110, minWidth: 100 },
    { headerName: 'Nama Departemen', field: 'name', editable: true, minWidth: 160 },
    { headerName: 'Aktif', field: 'isActive', editable: true, flex: 0, width: 90, minWidth: 90 },
  ];

  readonly areaActions: GridAction<Area>[] = [detailAction((area) => this.showArea(area))];
  readonly deptActions: GridAction<Department>[] = [detailAction((dept) => this.showDepartment(dept))];

  constructor() {
    void this.init();
  }

  private async init(): Promise<void> {
    await this.attempt(async () => {
      const [plants, departments] = await Promise.all([
        firstValueFrom(this.temuan.plants()),
        firstValueFrom(this.admin.departments()),
      ]);
      this.plants.set(plants);
      this.departments.set(departments);
      if (plants.length) {
        await Promise.all([this.selectPlant(plants[0].plantId), this.selectKpiPlant(plants[0].plantId)]);
      }
    });
    this.loading.set(false);
  }

  async selectPlant(plantId: number): Promise<void> {
    this.plantId.set(plantId);
    await this.attempt(async () => this.areas.set(await firstValueFrom(this.admin.areas(plantId))));
  }

  async addArea(): Promise<void> {
    const name = this.newArea().trim();
    if (!name) {
      return;
    }
    await this.attempt(async () => {
      await firstValueFrom(this.admin.createArea(this.plantId(), name));
      this.newArea.set('');
      this.areas.set(await firstValueFrom(this.admin.areas(this.plantId())));
    });
  }

  /** Nilai sel sudah berubah di grid; muat ulang supaya kalau server menolak, nilai lama kembali. */
  async saveArea(event: CellValueChangedEvent<Area>): Promise<void> {
    await this.attempt(() => firstValueFrom(this.admin.updateArea(event.data)));
    await this.attempt(async () => this.areas.set(await firstValueFrom(this.admin.areas(this.plantId()))));
  }

  async addDepartment(): Promise<void> {
    const code = this.newDeptCode().trim();
    const name = this.newDeptName().trim();
    if (!code || !name) {
      return;
    }
    await this.attempt(async () => {
      await firstValueFrom(this.admin.createDepartment(code, name));
      this.newDeptCode.set('');
      this.newDeptName.set('');
      this.departments.set(await firstValueFrom(this.admin.departments()));
    });
  }

  async saveDepartment(event: CellValueChangedEvent<Department>): Promise<void> {
    await this.attempt(() => firstValueFrom(this.admin.updateDepartment(event.data)));
    await this.attempt(async () => this.departments.set(await firstValueFrom(this.admin.departments())));
  }

  async selectKpiPlant(plantId: number): Promise<void> {
    this.kpiPlantId.set(plantId);
    await this.attempt(async () => this.kpiDepts.set(await firstValueFrom(this.kpi.participants(plantId))));
  }

  /** Tersimpan begitu dicentang; kalau server menolak, centang kembali ke kondisi di server. */
  async toggleKpiDept(deptId: number, checked: boolean): Promise<void> {
    const plantId = this.kpiPlantId();
    const deptIds = checked ? [...this.kpiDepts(), deptId] : this.kpiDepts().filter((id) => id !== deptId);
    this.kpiDepts.set(deptIds);
    try {
      await firstValueFrom(this.kpi.saveParticipants(plantId, deptIds));
    } catch (err) {
      this.toastr.danger(apiErrorMessage(err), 'Master Data');
      await this.selectKpiPlant(plantId);
    }
  }

  private showArea(area: Area): void {
    openDetail(this.dialog, area.name, [
      { label: 'Plant', value: this.plants().find((p) => p.plantId === area.plantId)?.name },
      { label: 'Status', value: area.isActive ? 'Aktif' : 'Nonaktif' },
    ]);
  }

  private showDepartment(dept: Department): void {
    openDetail(this.dialog, dept.name, [
      { label: 'Kode', value: dept.code },
      { label: 'Status', value: dept.isActive ? 'Aktif' : 'Nonaktif' },
    ]);
  }

  private async attempt(action: () => Promise<unknown>): Promise<void> {
    try {
      await action();
    } catch (err) {
      this.toastr.danger(apiErrorMessage(err), 'Master Data');
    }
  }
}
