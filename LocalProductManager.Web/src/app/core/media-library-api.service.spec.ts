import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { MediaLibraryApiService } from './media-library-api.service';

describe('MediaLibraryApiService', () => {
  beforeEach(() => TestBed.configureTestingModule({
    providers: [MediaLibraryApiService, provideHttpClient(), provideHttpClientTesting()],
  }));

  it('reads the selected bytes and sends a nonempty Blob', async () => {
    const service = TestBed.inject(MediaLibraryApiService);
    const http = TestBed.inject(HttpTestingController);
    const file = new File(['photo'], 'test photo.jpg', { type: 'image/jpeg' });

    const response = service.uploadFile('My folder', file);
    const request = await vi.waitFor(() => http.expectOne(req => req.url.includes('/media-library/folders/My%20folder/upload-file')));
    expect(request.request.body).toBeInstanceOf(Blob);
    expect((request.request.body as Blob).size).toBe(5);
    expect(request.request.url).toContain('fileName=test%20photo.jpg');
    expect(request.request.headers.get('Content-Type')).toBe('image/jpeg');
    expect(request.request.headers.get('X-Upload-Expected-Bytes')).toBe('5');
    request.flush({ imageCount: 1, videoCount: 0 });

    await expect(response).resolves.toEqual({ imageCount: 1, videoCount: 0 });
    http.verify();
  });

  it('does not send a file picker result with zero readable bytes', async () => {
    const service = TestBed.inject(MediaLibraryApiService);
    const http = TestBed.inject(HttpTestingController);
    const file = new File([], 'empty.jpg', { type: 'image/jpeg' });

    await expect(service.uploadFile('My folder', file)).rejects.toThrow('0 B selected, 0 B read');
    http.expectNone(req => req.url.includes('/media-library/'));
    http.verify();
  });
});
