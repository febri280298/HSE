import { HttpErrorResponse } from '@angular/common/http';

import { apiErrorMessage, apiFieldErrors } from './api-error';

describe('api-error', () => {
  const validation = new HttpErrorResponse({
    status: 400,
    error: { errors: { NewPassword: ['Password minimal 8 karakter.'], roleIds: ['Pilih minimal satu peran.'] } },
  });

  it('membaca kesalahan per field dari ValidationProblem .NET', () => {
    expect(apiFieldErrors(validation)).toEqual({
      newPassword: 'Password minimal 8 karakter.',
      roleIds: 'Pilih minimal satu peran.',
    });
  });

  it('pesan utama = kesalahan field pertama', () => {
    expect(apiErrorMessage(validation)).toBe('Password minimal 8 karakter.');
  });

  it('pesan jelas untuk 403 dan 429', () => {
    expect(apiErrorMessage(new HttpErrorResponse({ status: 403 }))).toContain('tidak punya akses');
    expect(apiErrorMessage(new HttpErrorResponse({ status: 429 }))).toContain('Terlalu banyak');
  });
});
