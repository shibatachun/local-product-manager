import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
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
    const request = http.expectOne('/upload');
    const uploaded = (request.request.body as FormData).get('files') as File;

    expect(uploaded.name).toMatch(/^mobile-capture-\d+-1\.jpg$/);
    request.flush({ ok: true });
    await expect(response).resolves.toEqual({ ok: true });
    http.verify();
  });
});
