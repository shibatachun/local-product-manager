import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { MediaUploadService } from './media-upload.service';

describe('MediaUploadService', () => {
  beforeEach(() => TestBed.configureTestingModule({
    providers: [MediaUploadService, provideHttpClient(), provideHttpClientTesting()],
  }));

  it('gives an unnamed mobile camera file a multipart filename', async () => {
    const service = TestBed.inject(MediaUploadService);
    const http = TestBed.inject(HttpTestingController);
    const unnamedPhoto = new File(['photo'], '', { type: 'image/jpeg' });

    const response = service.uploadMultipart<{ ok: boolean }>('/upload', [unnamedPhoto]);
    const request = await vi.waitFor(() => http.expectOne('/upload'));
    const uploaded = (request.request.body as FormData).get('files') as File;

    expect(uploaded.name).toMatch(/^mobile-capture-\d+-1\.jpg$/);
    expect(uploaded.size).toBe(5);
    request.flush({ ok: true });
    await expect(response).resolves.toEqual({ ok: true });
    http.verify();
  });

  it('rejects unreadable files before sending a multipart request', async () => {
    const service = TestBed.inject(MediaUploadService);
    const http = TestBed.inject(HttpTestingController);
    await expect(service.uploadMultipart('/upload', [new File([], 'empty.jpg', { type: 'image/jpeg' })]))
      .rejects.toThrow('0 B selected, 0 B read');
    http.expectNone('/upload');
    http.verify();
  });

  it('copies selected camera bytes before the picker can be cleared', async () => {
    const service = TestBed.inject(MediaUploadService);
    vi.spyOn(service, 'prepareCameraImages').mockImplementation(async files => files);
    const selected = new File(['camera bytes'], 'front.jpg', { type: 'image/jpeg' });
    const [copy] = await service.prepareSelectedImages([selected]);
    expect(copy).not.toBe(selected);
    expect(copy.size).toBeGreaterThan(0);
    expect(await copy.arrayBuffer()).toEqual(await selected.arrayBuffer());
  });

  it('keeps a custom field name and metadata for shipment uploads', async () => {
    const service = TestBed.inject(MediaUploadService);
    const http = TestBed.inject(HttpTestingController);
    const file = new File(['proof'], 'slip.jpg', { type: 'image/jpeg' });

    const response = service.uploadMultipart('/shipment', [file], 'file', { customerName: 'Buyer', kind: 'shipping-slip' });
    const request = await vi.waitFor(() => http.expectOne('/shipment'));
    const form = request.request.body as FormData;
    expect((form.get('file') as File).size).toBe(5);
    expect((form.get('file') as File).name).toBe('slip.jpg');
    expect(form.get('customerName')).toBe('Buyer');
    expect(form.get('kind')).toBe('shipping-slip');
    request.flush({ ok: true });

    await expect(response).resolves.toEqual({ ok: true });
    http.verify();
  });
});
