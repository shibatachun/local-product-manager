import { CommonModule } from '@angular/common';
import { AfterViewInit, Component, ElementRef, EventEmitter, HostListener, Input, OnDestroy, Output, ViewChild, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { BarcodeFormat, BrowserMultiFormatReader, IScannerControls } from '@zxing/browser';
import { DecodeHintType } from '@zxing/library';
import { ProductApiService } from '../../core/product-api.service';
import { InventoryApiService } from '../../core/inventory-api.service';
import { ProductListItem } from '../../core/models';
import { LiveStreamBidDailySummary, LiveStreamBidProduct, LiveStreamBidProductApiService, LiveStreamBidSale } from '../../core/live-stream-bid-product-api.service';
import { MediaUploadService } from '../../core/media-upload.service';
import { MediaPickerComponent } from '../../shared/media-picker.component';

type ScannerLocation = {
  levelId: number;
  binNumber: number;
  key: string;
  code: string;
  description: string;
  qty: number;
};

// Some keyboard-wedge scanners pause while sending long UUIDs. Enter remains immediate;
// scanners without an Enter suffix submit after the whole input has been idle.
const SCANNER_INPUT_IDLE_MS = 2000;

@Component({
  selector: 'app-barcode-scanner',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, MediaPickerComponent],
  host: { '[class.embedded-host]': 'embedded' },
  templateUrl: './barcode-scanner.page.html',
  styleUrl: './barcode-scanner.page.css',
})
export class BarcodeScannerPage implements AfterViewInit, OnDestroy {
  @Input() embedded = false;
  @Input() lockedScanMode: 'inventory' | 'bid' | null = null;
  @Input() bidShowSessionId: number | null = null;
  @Input() bidShowOrderId: number | null = null;
  @Input() bidOrderRemainingQuantity: number | null = null;
  @Input() bidSalesOnly = false;
  @Input() cameraOnly = false;
  @Input() compactBidSale = false;
  @Output() readonly openProductDetail = new EventEmitter<number>();
  @Output() readonly bidSaleRecorded = new EventEmitter<LiveStreamBidSale>();
  @ViewChild('video') video?: ElementRef<HTMLVideoElement>;
  @ViewChild('barcodeInput') barcodeInput?: ElementRef<HTMLInputElement>;
  readonly api = inject(ProductApiService);
  private readonly inventoryApi = inject(InventoryApiService);
  private readonly bidApi = inject(LiveStreamBidProductApiService);
  private readonly mediaUpload = inject(MediaUploadService);
  private readonly reader = new BrowserMultiFormatReader(new Map<DecodeHintType, unknown>([
    [DecodeHintType.POSSIBLE_FORMATS, [BarcodeFormat.QR_CODE]],
    [DecodeHintType.TRY_HARDER, true],
  ]), { delayBetweenScanAttempts: 80, delayBetweenScanSuccess: 800 });
  private controls?: IScannerControls;
  private cameraSession = 0;
  private cameraResultLocked = false;
  private destroyed = false;
  private hardwareBuffer = '';
  private hardwareLastKeyAt = 0;
  private scannerInputTimer?: ReturnType<typeof setTimeout>;

  readonly scanning = signal(false);
  readonly loading = signal(false);
  readonly error = signal('');
  readonly scannedCode = signal('');
  readonly product = signal<ProductListItem | null>(null);
  readonly bidProduct = signal<LiveStreamBidProduct | null>(null);
  readonly bidToday = signal<LiveStreamBidDailySummary | null>(null);
  readonly bidCurrency = signal<'usd' | 'rmb'>('usd');
  readonly uploadingBidPhotos = signal(false);
  readonly uploadingBidVideos = signal(false);
  readonly bidVideos = signal<{ fileName: string; updatedAt: string }[]>([]);
  readonly locations = signal<ScannerLocation[]>([]);
  readonly warehouseLevels = signal<ScannerLocation[]>([]);
  readonly notFound = signal(false);
  readonly showAdd = signal(false);
  readonly success = signal('');
  readonly mobileMenuOpen = signal(false);
  readonly torchAvailable = signal(false);
  readonly torchOn = signal(false);
  readonly operation = signal<'in' | 'out' | null>(null);
  readonly scannedType = signal<'lpm' | 'bid' | null>(null);
  readonly inputMode = signal<'camera' | 'scanner'>('scanner');
  readonly scanMode = signal<'inventory' | 'bid'>('inventory');
  operationQty = 1;
  bidReceiveQty = 1;
  actualSalePrice: number | null = null;
  bidSaleQuantity = 1;
  operationLocationKey: string | null = null;
  manualCode = '';
  addForm = { localInventoryId: '', sku: '', productName: '', qty: 0 };

  ngAfterViewInit(): void {
    if (this.lockedScanMode) this.scanMode.set(this.lockedScanMode);
    if (this.cameraOnly || matchMedia('(max-width: 820px), (pointer: coarse)').matches) this.setInputMode('camera');
    else this.setInputMode('scanner');
  }

  ngOnDestroy(): void {
    this.destroyed = true;
    this.stopCamera();
    if (this.scannerInputTimer) clearTimeout(this.scannerInputTimer);
  }

  setInputMode(mode: 'camera' | 'scanner'): void {
    this.inputMode.set(mode);
    this.reset();
    if (mode === 'camera') setTimeout(() => void this.startCamera());
    else {
      this.stopCamera();
      setTimeout(() => this.barcodeInput?.nativeElement.focus());
    }
  }

  setScanMode(mode: 'inventory' | 'bid'): void {
    if (this.lockedScanMode) return;
    this.scanMode.set(mode);
    this.reset();
    if (this.inputMode() === 'camera') void this.startCamera();
  }

  async startCamera(): Promise<void> {
    if (!this.video || this.scanning() || this.destroyed || this.cameraResultLocked || this.uploadingBidPhotos()) return;
    this.error.set('');
    if (!globalThis.isSecureContext && location.hostname !== 'localhost') {
      this.error.set('Camera access requires HTTPS. You can still enter the barcode manually.');
      return;
    }
    const session = ++this.cameraSession;
    try {
      this.scanning.set(true);
      const controls = await this.reader.decodeFromConstraints(
        {
          video: {
            facingMode: { ideal: 'environment' },
            width: { ideal: 1920 },
            height: { ideal: 1080 },
            advanced: [{ focusMode: 'continuous' } as MediaTrackConstraintSet],
          },
          audio: false,
        },
        this.video.nativeElement,
        result => {
          if (!result || session !== this.cameraSession || this.cameraResultLocked || !this.scanning()) return;
          const value = result.getText().trim();
          if (!value) return;
          // The decoder can report the same QR repeatedly, including while its
          // controls are still being initialized. Lock synchronously so a late
          // callback cannot replace the product card or interrupt a photo upload.
          this.cameraResultLocked = true;
          this.stopCamera();
          navigator.vibrate?.(80);
          void this.lookupCode(value);
        },
      );
      if (session !== this.cameraSession || this.destroyed || this.cameraResultLocked) {
        controls.stop();
        return;
      }
      this.controls = controls;
      this.torchAvailable.set(!!controls.switchTorch);
    } catch (err) {
      if (session === this.cameraSession && !this.destroyed) {
        this.scanning.set(false);
        this.error.set(this.cameraError(err));
      }
    }
  }

  stopCamera(): void {
    this.cameraSession++;
    this.controls?.stop();
    this.controls = undefined;
    this.scanning.set(false);
    this.torchAvailable.set(false);
    this.torchOn.set(false);
  }

  async toggleTorch(): Promise<void> {
    if (!this.controls?.switchTorch) return;
    const next = !this.torchOn();
    try {
      await this.controls.switchTorch(next);
      this.torchOn.set(next);
    } catch {
      this.error.set('This camera does not support flashlight control.');
    }
  }

  submitManual(): void { void this.lookupCode(this.manualCode); }

  scannerInputChanged(value: string): void {
    this.manualCode = value;
    if (this.inputMode() !== 'scanner') return;
    if (this.scannerInputTimer) clearTimeout(this.scannerInputTimer);
    const code = value.trim();
    if (!code) return;
    this.scannerInputTimer = setTimeout(() => {
      const completedCode = this.manualCode.trim();
      if (!completedCode || this.loading()) return;
      this.manualCode = '';
      if (this.barcodeInput?.nativeElement) this.barcodeInput.nativeElement.value = '';
      void this.lookupCode(completedCode);
    }, SCANNER_INPUT_IDLE_MS);
  }

  scannerInputSubmitted(event: Event): void {
    event.preventDefault();
    if (this.scannerInputTimer) clearTimeout(this.scannerInputTimer);
    const completedCode = this.manualCode.trim();
    if (!completedCode || this.loading()) return;
    this.manualCode = '';
    if (this.barcodeInput?.nativeElement) this.barcodeInput.nativeElement.value = '';
    void this.lookupCode(completedCode);
  }

  @HostListener('document:keydown', ['$event'])
  captureHardwareScanner(event: KeyboardEvent): void {
    const target = event.target as HTMLElement | null;
    if (target?.matches('input, textarea, select, [contenteditable="true"]') || event.ctrlKey || event.altKey || event.metaKey) return;
    const now = Date.now();
    if (now - this.hardwareLastKeyAt > SCANNER_INPUT_IDLE_MS) this.hardwareBuffer = '';
    this.hardwareLastKeyAt = now;
    if (event.key === 'Enter') {
      const value = this.hardwareBuffer.trim();
      this.hardwareBuffer = '';
      if (value) { event.preventDefault(); void this.lookupCode(value); }
      return;
    }
    if (event.key.length === 1) this.hardwareBuffer += event.key;
  }

  async lookupCode(raw: string): Promise<void> {
    if (this.uploadingBidPhotos() || this.uploadingBidVideos()) return;
    const parsed = this.parseCode(raw);
    if (!parsed.value) return;
    if (this.scanMode() === 'bid') await this.lookupBid(parsed.value, raw.trim());
    else await this.lookupLocal(parsed.value, raw.trim());
  }

  private async lookupLocal(code: string, raw: string): Promise<void> {
    if (!code || this.loading()) return;
    this.stopCamera();
    this.loading.set(true);
    this.error.set('');
    this.success.set('');
    this.showAdd.set(false);
    this.scannedCode.set(raw);
    this.scannedType.set('lpm');
    this.bidProduct.set(null);
    this.bidVideos.set([]);
    try {
      const found = await this.api.lookupExact(code);
      this.product.set(found);
      this.notFound.set(!found);
      if (found) await this.loadLocations(found.id);
      else {
        this.locations.set([]);
        this.warehouseLevels.set([]);
      }
      if (!found) this.addForm = { localInventoryId: '', sku: code, productName: '', qty: 0 };
    } catch {
      this.error.set('Unable to reach inventory. Check the local API connection and try again.');
    } finally {
      this.loading.set(false);
    }
  }

  private async lookupBid(uuid: string, raw: string): Promise<void> {
    if (!uuid || this.loading()) return;
    this.stopCamera();
    this.loading.set(true);
    this.error.set('');
    this.success.set('');
    this.scannedCode.set(raw);
    this.scannedType.set('bid');
    this.product.set(null);
    this.locations.set([]);
    this.warehouseLevels.set([]);
    this.bidVideos.set([]);
    try {
      const product = await this.bidApi.lookup(uuid);
      this.bidProduct.set(product);
      this.bidReceiveQty = product ? Math.max(0, product.inventory - product.actualInventory) : 1;
      this.notFound.set(!product);
      if (product && !this.bidSalesOnly && !this.compactBidSale) {
        try { this.bidVideos.set(await this.bidApi.videoFiles(product.id)); }
        catch { this.error.set('BID product loaded, but its videos could not be loaded.'); }
      }
    } catch {
      this.error.set('Unable to reach bid inventory. Check the API connection and try again.');
    } finally { this.loading.set(false); }
  }

  async confirmBidSale(): Promise<void> {
    const product = this.bidProduct();
    if (!product || this.loading()) return;
    if (!this.bidShowSessionId) {
      this.error.set('Start a show cost session on the BID Product page before recording a sale.');
      return;
    }
    const quantity = Number(this.bidSaleQuantity);
    const available = Math.min(product.inventory, this.bidOrderRemainingQuantity ?? Number.MAX_SAFE_INTEGER);
    if (!Number.isInteger(quantity) || quantity < 1 || quantity > available) {
      this.error.set(`Enter a whole-number sold quantity from 1 to ${Math.max(0, available)}.`);
      return;
    }
    const price = this.actualSalePrice === null || this.actualSalePrice === undefined || String(this.actualSalePrice).trim() === ''
      ? null : Number(this.actualSalePrice);
    if (price !== null && (!Number.isFinite(price) || price < 0)) {
      this.error.set('Actual sale price must be zero or greater.');
      return;
    }
    this.loading.set(true);
    this.error.set('');
    try {
      const sale = await this.bidApi.confirmSale(product.id, price, this.bidShowSessionId, this.bidShowOrderId, quantity);
      const confirmation = `${sale.soldQuantity} item(s) sold${sale.actualSalePriceUsd === null ? '' : ` at $${sale.actualSalePriceUsd.toFixed(2)}`}. Ready for the next scan.`;
      this.reset();
      this.success.set(confirmation);
      this.bidSaleRecorded.emit(sale);
    } catch (error: any) {
      this.error.set(error?.status === 409
        ? (typeof error?.error === 'string' ? error.error : 'This BID product or customer order cannot receive another cost item.')
        : 'The bid sale could not be recorded.');
    } finally { this.loading.set(false); }
  }

  async adjustBidActualInventory(delta: number): Promise<void> {
    const product = this.bidProduct();
    const amount = Math.floor(Number(delta));
    if (!product || this.loading()) return;
    if (!Number.isFinite(amount) || amount === 0) {
      this.error.set('Enter a whole-number received quantity of at least 1.');
      return;
    }
    const next = product.actualInventory + amount;
    if (next < 0 || next > product.inventory) {
      this.error.set(amount > 0
        ? `Only ${Math.max(0, product.inventory - product.actualInventory)} item(s) remain to be received.`
        : 'Actual inventory cannot be negative.');
      return;
    }
    this.loading.set(true); this.error.set(''); this.success.set('');
    try {
      const updated = await this.bidApi.adjustActualInventory(product.id, amount);
      this.bidProduct.set(updated);
      this.bidReceiveQty = Math.max(0, updated.inventory - updated.actualInventory);
      this.success.set(`Actual inventory ${amount > 0 ? 'increased' : 'decreased'} by ${Math.abs(amount)} to ${updated.actualInventory}.`);
    } catch (error: any) {
      this.error.set(error?.error || (amount > 0 ? 'Actual inventory cannot exceed expected inventory.' : 'Actual inventory cannot be negative.'));
    } finally { this.loading.set(false); }
  }

  receiveBidInventory(): void {
    void this.adjustBidActualInventory(this.bidReceiveQty);
  }

  bidImageUrl(product: LiveStreamBidProduct, thumbnail = 640): string {
    return this.bidApi.imageUrl(product.id, thumbnail, product.updatedAt);
  }

  downloadBidPicture(product: LiveStreamBidProduct): void {
    const anchor = document.createElement('a');
    anchor.href = this.bidApi.imageDownloadUrl(product.id);
    document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();
  }

  async uploadBidPhotos(event: Event, cameraPhoto = false): Promise<void> {
    const { input, files: selectedFiles } = this.mediaUpload.filesFrom(event);
    const product = this.bidProduct();
    if (!product || this.uploadingBidPhotos()) { input.value = ''; return; }
    if (selectedFiles.length === 0) {
      this.error.set('The camera or photo library did not return a photo. Please try again.');
      input.value = '';
      return;
    }
    this.uploadingBidPhotos.set(true);
    this.cameraResultLocked = true;
    this.stopCamera();
    this.error.set('');
    this.success.set('');
    try {
      const files = cameraPhoto
        ? await this.mediaUpload.prepareCameraImages(selectedFiles)
        : selectedFiles;
      const result = await this.bidApi.uploadImages(product.id, files);
      if (this.bidProduct()?.id === product.id)
        this.bidProduct.set({ ...this.bidProduct()!, imagePath: result.imagePath, updatedAt: result.updatedAt });
      this.success.set(`${result.uploadedCount} photo${result.uploadedCount === 1 ? '' : 's'} uploaded.`);
    } catch (error: any) {
      this.error.set(typeof error?.error === 'string' ? error.error : 'Could not upload the BID product photos. Check the file type, size, and connection.');
    } finally {
      input.value = '';
      this.uploadingBidPhotos.set(false);
    }
  }

  bidVideoUrl(product: LiveStreamBidProduct, fileName: string): string {
    return this.bidApi.videoUrl(product.id, fileName);
  }

  async uploadBidVideos(event: Event, cameraVideo = false): Promise<void> {
    const { input, files } = this.mediaUpload.filesFrom(event);
    const product = this.bidProduct();
    if (!product || this.uploadingBidVideos()) { input.value = ''; return; }
    if (!files.length) {
      this.error.set('The camera or video library did not return a video. Please try again.');
      input.value = '';
      return;
    }
    this.uploadingBidVideos.set(true);
    this.error.set('');
    this.success.set('');
    try {
      const result = cameraVideo && files.length === 1
        ? await this.bidApi.uploadCapturedVideo(product.id, files[0])
        : await this.bidApi.uploadVideos(product.id, files);
      this.bidVideos.set(await this.bidApi.videoFiles(product.id));
      this.success.set(`${result.videoCount} video${result.videoCount === 1 ? '' : 's'} uploaded.`);
    } catch (error: any) {
      this.error.set(typeof error?.error === 'string' ? error.error : 'Could not upload the BID product video. Check the file type and size.');
    } finally {
      input.value = '';
      this.uploadingBidVideos.set(false);
    }
  }

  cancelBidSale(): void { this.reset(); setTimeout(() => this.barcodeInput?.nativeElement.focus()); }

  async addProduct(): Promise<void> {
    const sku = this.addForm.sku.trim();
    const productName = this.addForm.productName.trim();
    if (!sku || !productName || this.loading()) {
      this.error.set('SKU and product name are required.');
      return;
    }
    this.loading.set(true);
    this.error.set('');
    try {
      const created = await this.api.createProduct({
        localInventoryId: this.addForm.localInventoryId.trim() || null,
        sku, productName,
        qty: Math.max(0, Number(this.addForm.qty) || 0),
        cost: null,
        imagePath: null,
      });
      this.product.set(created);
      this.notFound.set(false);
      this.showAdd.set(false);
      this.locations.set([]);
      this.warehouseLevels.set([]);
      this.success.set('Product added to inventory.');
    } catch (err: any) {
      this.error.set(err?.status === 409 ? 'This SKU already exists. Scan again to view it.' : 'Could not add this product.');
    } finally {
      this.loading.set(false);
    }
  }

  reset(): void {
    this.cameraResultLocked = false;
    this.stopCamera();
    this.product.set(null);
    this.bidProduct.set(null);
    this.bidVideos.set([]);
    this.bidToday.set(null);
    this.notFound.set(false);
    this.scannedCode.set('');
    this.locations.set([]);
    this.warehouseLevels.set([]);
    this.operation.set(null);
    this.scannedType.set(null);
    this.operationLocationKey = null;
    this.operationQty = 1;
    this.bidReceiveQty = 1;
    this.showAdd.set(false);
    this.success.set('');
    this.manualCode = '';
    this.actualSalePrice = null;
    this.bidSaleQuantity = 1;
    setTimeout(() => this.barcodeInput?.nativeElement.focus());
  }

  startOperation(type: 'in' | 'out'): void {
    this.operation.set(type);
    this.operationQty = 1;
    const currentLocation = this.locations()
      .reduce<ScannerLocation | null>((best, location) =>
        !best || location.qty > best.qty ? location : best, null);
    const fallbackLocation = type === 'in' ? this.warehouseLevels()[0] : null;
    this.operationLocationKey = currentLocation?.key ?? fallbackLocation?.key ?? null;
    this.error.set('');
    this.success.set('');
  }

  cancelOperation(): void {
    this.operation.set(null);
    this.operationLocationKey = null;
    this.operationQty = 1;
  }

  operationLocations(): ScannerLocation[] {
    return this.operation() === 'out' ? this.locations() : this.warehouseLevels();
  }

  async applyOperation(): Promise<void> {
    const product = this.product();
    const location = this.warehouseLevels().find(item => item.key === this.operationLocationKey);
    const amount = Math.floor(Number(this.operationQty));
    const type = this.operation();
    if (!product || !type || !location || amount < 1 || this.loading()) {
      this.error.set('Choose a location and enter a quantity of at least 1.');
      return;
    }
    if (type === 'out' && amount > location.qty) {
      this.error.set(`Only ${location.qty} item(s) are available at ${location.description}.`);
      return;
    }

    this.loading.set(true);
    this.error.set('');
    try {
      const delta = type === 'in' ? amount : -amount;
      await this.inventoryApi.adjustInventory(location.levelId, product.id, delta, location.binNumber);
      this.product.set(await this.api.getProduct(product.id));
      await this.loadLocations(product.id);
      this.success.set(`${type === 'in' ? 'Stock in' : 'Stock out'} completed: ${amount} item(s) at ${location.description}.`);
      this.cancelOperation();
    } catch {
      this.error.set('The inventory operation could not be completed.');
    } finally {
      this.loading.set(false);
    }
  }

  private async loadLocations(productId: number): Promise<void> {
    const levels = await this.inventoryApi.productLocations(productId);
    this.warehouseLevels.set(levels);
    this.locations.set(levels.filter(level => level.qty > 0));
  }

  private cameraError(err: unknown): string {
    const name = (err as DOMException)?.name;
    if (name === 'NotAllowedError') return 'Camera permission was denied. Allow camera access or enter the barcode manually.';
    if (name === 'NotFoundError') return 'No camera was found on this device.';
    return 'The camera could not be started. You can enter the barcode manually.';
  }

  private parseCode(raw: string): { type: 'lpm' | 'bid'; value: string } {
    const normalized = raw.trim();
    const match = /^(LPM|BID)\s*(?:\||:|-|\s)\s*(.+)$/i.exec(normalized);
    if (!match) {
      const bareUuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(normalized);
      return { type: bareUuid ? 'bid' : 'lpm', value: normalized };
    }
    return { type: match[1].toUpperCase() === 'BID' ? 'bid' : 'lpm', value: match[2].trim() };
  }
}
