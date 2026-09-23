import { TestBed } from '@angular/core/testing';
import { InventoryApiService } from '../../core/inventory-api.service';
import { LiveStreamBidProductApiService } from '../../core/live-stream-bid-product-api.service';
import { MediaUploadService } from '../../core/media-upload.service';
import { ProductApiService } from '../../core/product-api.service';
import { BarcodeScannerPage } from './barcode-scanner.page';

describe('BarcodeScannerPage scanner input', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    TestBed.configureTestingModule({
      providers: [
        { provide: ProductApiService, useValue: {} },
        { provide: InventoryApiService, useValue: {} },
        { provide: LiveStreamBidProductApiService, useValue: {} },
        { provide: MediaUploadService, useValue: {} },
      ],
    });
  });

  afterEach(() => vi.useRealTimers());

  it('waits for the complete code when a scanner pauses between characters', () => {
    const page = TestBed.runInInjectionContext(() => new BarcodeScannerPage());
    const lookup = vi.spyOn(page, 'lookupCode').mockResolvedValue();

    page.scannerInputChanged('BID|1234');
    vi.advanceTimersByTime(800);
    expect(lookup).not.toHaveBeenCalled();

    page.scannerInputChanged('BID|12345678');
    vi.advanceTimersByTime(1999);
    expect(lookup).not.toHaveBeenCalled();
    vi.advanceTimersByTime(1);
    expect(lookup).toHaveBeenCalledExactlyOnceWith('BID|12345678');
  });

  it('submits immediately when the scanner sends Enter', () => {
    const page = TestBed.runInInjectionContext(() => new BarcodeScannerPage());
    const lookup = vi.spyOn(page, 'lookupCode').mockResolvedValue();

    page.scannerInputChanged('BID|12345678');
    page.scannerInputSubmitted({ preventDefault: vi.fn() } as unknown as Event);
    expect(lookup).toHaveBeenCalledExactlyOnceWith('BID|12345678');
    vi.advanceTimersByTime(2000);
    expect(lookup).toHaveBeenCalledTimes(1);
  });

  it('keeps the document-level scanner buffer across a short pause', () => {
    const page = TestBed.runInInjectionContext(() => new BarcodeScannerPage());
    const lookup = vi.spyOn(page, 'lookupCode').mockResolvedValue();

    for (const key of 'BID|12') page.captureHardwareScanner(new KeyboardEvent('keydown', { key }));
    vi.advanceTimersByTime(800);
    for (const key of '345678') page.captureHardwareScanner(new KeyboardEvent('keydown', { key }));
    page.captureHardwareScanner(new KeyboardEvent('keydown', { key: 'Enter' }));

    expect(lookup).toHaveBeenCalledExactlyOnceWith('BID|12345678');
  });
});
