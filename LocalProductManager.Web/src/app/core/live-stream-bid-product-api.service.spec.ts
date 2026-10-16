import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { LiveStreamBidProductApiService } from './live-stream-bid-product-api.service';

describe('LiveStreamBidProductApiService', () => {
  beforeEach(() => TestBed.configureTestingModule({
    providers: [LiveStreamBidProductApiService, provideHttpClient(), provideHttpClientTesting()],
  }));

  it('sends the selected price photo as a materialized image body', async () => {
    const service = TestBed.inject(LiveStreamBidProductApiService);
    const http = TestBed.inject(HttpTestingController);
    const photo = new File([new Uint8Array([0xff, 0xd8, 0xff, 0xd9])], 'price.jpg', { type: 'image/jpeg' });

    const response = service.uploadOrderPriceEvidence(12, photo);
    await new Promise(resolve => setTimeout(resolve, 0));
    const request = http.expectOne(req => req.url.endsWith('/show-attachments/orders/12/price-evidence/raw'));
    expect(request.request.headers.get('Content-Type')).toBe('image/jpeg');
    expect(request.request.body).toBeInstanceOf(Blob);
    expect(await (request.request.body as Blob).arrayBuffer()).toEqual(await photo.arrayBuffer());
    request.flush({ id: 1 });
    await expect(response).resolves.toMatchObject({ id: 1 });
    http.verify();
  });
});
