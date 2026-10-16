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

  it('accepts one camera result and stops controls that arrive after the result', async () => {
    const page = TestBed.runInInjectionContext(() => new BarcodeScannerPage());
    page.video = { nativeElement: document.createElement('video') } as BarcodeScannerPage['video'];
    const lookup = vi.spyOn(page, 'lookupCode').mockResolvedValue();
    const stop = vi.fn();
    let emit: ((result: { getText(): string }) => void) | undefined;
    let finishStart: ((controls: { stop(): void }) => void) | undefined;
    vi.spyOn((page as any).reader, 'decodeFromConstraints').mockImplementation(
      (...args: unknown[]) => {
        emit = args[2] as (result: { getText(): string }) => void;
        return new Promise(resolve => { finishStart = resolve as typeof finishStart; });
      },
    );

    const starting = page.startCamera();
    emit?.({ getText: () => 'BID|same-product' });
    emit?.({ getText: () => 'BID|same-product' });
    expect(lookup).toHaveBeenCalledExactlyOnceWith('BID|same-product');
    expect(page.scanning()).toBe(false);

    finishStart?.({ stop });
    await starting;
    expect(stop).toHaveBeenCalledTimes(1);
    await page.startCamera();
    expect(lookup).toHaveBeenCalledTimes(1);
  });

  it('keeps the BID product card stable while a mobile photo upload is in progress', async () => {
    let completeUpload: ((result: { imagePath: string; updatedAt: string; uploadedCount: number }) => void) | undefined;
    const uploadImages = vi.fn().mockImplementation(() => new Promise(resolve => { completeUpload = resolve; }));
    const lookup = vi.fn();
    TestBed.overrideProvider(LiveStreamBidProductApiService, { useValue: { uploadImages, lookup } });
    TestBed.overrideProvider(MediaUploadService, { useValue: {
      filesFrom: () => ({ input: { value: 'photo.jpg' }, files: [new File(['photo'], 'photo.jpg', { type: 'image/jpeg' })] }),
    } });
    const page = TestBed.runInInjectionContext(() => new BarcodeScannerPage());
    page.scanMode.set('bid');
    page.bidProduct.set({ id: 17, imagePath: null, updatedAt: 'old' } as any);

    const uploading = page.uploadBidPhotos({} as Event);
    await page.lookupCode('BID|another-product');
    expect(lookup).not.toHaveBeenCalled();
    expect(page.bidProduct()?.id).toBe(17);

    completeUpload?.({ imagePath: 'new.jpg', updatedAt: 'new', uploadedCount: 1 });
    await uploading;
    expect(page.bidProduct()?.imagePath).toBe('new.jpg');
    expect(page.uploadingBidPhotos()).toBe(false);
  });

  it('confirms a BID sale when actual inventory is zero but expected inventory remains', async () => {
    const sale = { soldQuantity: 1, actualSalePriceUsd: null };
    const confirmSale = vi.fn().mockResolvedValue(sale);
    TestBed.overrideProvider(LiveStreamBidProductApiService, { useValue: { confirmSale } });
    const page = TestBed.runInInjectionContext(() => new BarcodeScannerPage());
    page.bidShowSessionId = 7;
    page.bidProduct.set({ id: 83, inventory: 1, actualInventory: 0 } as any);

    await page.confirmBidSale();

    expect(confirmSale).toHaveBeenCalledExactlyOnceWith(83, null, 7, null, 1);
    expect(page.error()).toBe('');
    expect(page.success()).toContain('1 item(s) sold');
  });
});
