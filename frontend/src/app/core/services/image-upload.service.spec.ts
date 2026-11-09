import { HttpErrorResponse, HttpEventType, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { ImageResponse, ImageUploadEvent } from '../models/image-response.model';
import { ImageUploadService, cameraFileName } from './image-upload.service';

describe('ImageUploadService', () => {
  let service: ImageUploadService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(ImageUploadService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('mengirim foto sebagai multipart ke /api/images lalu melaporkan progres dan hasil', () => {
    const events: ImageUploadEvent[] = [];
    service.upload(new Blob(['x'], { type: 'image/jpeg' }), 'temuan.jpg', 'temuan').subscribe((e) => events.push(e));

    const req = http.expectOne('/api/images');
    expect(req.request.method).toBe('POST');
    const body = req.request.body as FormData;
    expect((body.get('file') as File).name).toBe('temuan.jpg');
    expect(body.get('category')).toBe('temuan');

    req.event({ type: HttpEventType.UploadProgress, loaded: 50, total: 200 });
    const image: ImageResponse = {
      id: 'abc',
      path: 'temuan/2026/11/abc.jpg',
      url: '/uploads/temuan/2026/11/abc.jpg',
      fileName: 'temuan.jpg',
      contentType: 'image/jpeg',
      size: 1,
      category: 'temuan',
      uploadedAt: '2026-11-08T03:00:00Z',
    };
    req.flush(image);

    expect(events).toEqual([
      { type: 'progress', percent: 25 },
      { type: 'done', image },
    ]);
  });

  it('memakai pesan validasi dari server', () => {
    const error = new HttpErrorResponse({
      status: 400,
      error: { errors: { file: ['Ukuran foto maksimal 10 MB.'] } },
    });
    expect(service.errorMessage(error)).toBe('Ukuran foto maksimal 10 MB.');
  });

  it('memberi pesan jelas saat server tidak terjangkau', () => {
    expect(service.errorMessage(new HttpErrorResponse({ status: 0 }))).toContain('Tidak terhubung');
  });

  it('memberi nama file foto kamera dari tanggal dan jam', () => {
    expect(cameraFileName(new Date(2026, 10, 8, 14, 5, 9))).toBe('foto-20261108-140509.jpg');
  });
});
