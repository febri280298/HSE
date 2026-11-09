import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { vi } from 'vitest';

import { ImageResponse } from '../../../core/models/image-response.model';
import { AuthService } from '../../../core/services/auth.service';
import { NotifyService } from '../../../core/services/notify.service';
import { TemuanService } from '../../../core/services/temuan.service';
import { TemuanFormComponent } from './temuan-form.component';

describe('TemuanFormComponent', () => {
  async function create(): Promise<TemuanFormComponent> {
    TestBed.configureTestingModule({
      providers: [
        {
          provide: TemuanService,
          useValue: {
            options: () =>
              of({
                plants: [{ plantId: 1, name: 'A' }, { plantId: 2, name: 'B' }],
                areas: [{ areaId: 9, name: 'Area' }],
                departments: [],
                pics: [],
              }),
            genba: () => of([]),
            create: () => of({ number: 'X-1' }),
          },
        },
        { provide: AuthService, useValue: { user: signal({ plants: [{ plantId: 2 }] }), can: () => false } },
        { provide: NotifyService, useValue: { success: vi.fn(), danger: vi.fn() } },
      ],
    });
    const component = TestBed.runInInjectionContext(() => new TemuanFormComponent());
    await vi.waitFor(() => expect(component.loading()).toBe(false));
    return component;
  }

  beforeEach(() => {
    vi.spyOn(window, 'scrollTo').mockImplementation(() => undefined);
  });

  it('tanda merah baru muncul setelah tombol simpan ditekan', async () => {
    const form = await create();
    form.form.controls.detail.markAsTouched();
    form.form.controls.detail.markAsDirty();
    expect(form.showError('detail')).toBe(false);

    await form.submit();

    expect(form.showError('detail')).toBe(true);
    expect(form.showError('areaId')).toBe(true);
    expect(form.showError('fotoTemuan')).toBe(true);
  });

  it('setelah tersimpan tidak ada tanda merah, plant dan tanggal tetap', async () => {
    const form = await create();
    const date = '2026-11-05';
    form.form.patchValue({
      patrolDate: date,
      detail: 'Lantai licin',
      areaId: 9,
      fotoTemuan: [{ path: 'a', fileName: 'a.jpg' } as ImageResponse],
    });
    form.form.controls.detail.markAsDirty();
    form.form.controls.areaId.markAsDirty();

    await form.submit();

    expect(form.form.controls.plantId.value).toBe(2);
    expect(form.form.controls.patrolDate.value).toBe(date);
    expect(form.form.controls.detail.value).toBe('');
    expect(form.form.controls.detail.dirty).toBe(false);
    expect(form.form.controls.areaId.dirty).toBe(false);
    expect(form.showError('detail')).toBe(false);
    expect(form.showError('areaId')).toBe(false);
    expect(form.showError('fotoTemuan')).toBe(false);
  });
});
