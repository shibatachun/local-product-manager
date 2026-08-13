import { CommonModule } from '@angular/common';
import { AfterViewInit, Component, ElementRef, EventEmitter, HostListener, Input, OnDestroy, Output, ViewChild, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { BarcodeFormat, BrowserMultiFormatReader, IScannerControls } from '@zxing/browser';
import { DecodeHintType } from '@zxing/library';
import { ProductApiService } from '../../core/product-api.service';
import { InventoryApiService } from '../../core/inventory-api.service';
import { ProductListItem } from '../../core/models';
import { LiveStreamBidDailySummary, LiveStreamBidProduct, LiveStreamBidProductApiService } from '../../core/live-stream-bid-product-api.service';

type ScannerLocation = {
  levelId: number;
  binNumber: number;
  key: string;
  code: string;
  description: string;
  qty: number;
};

@Component({
  selector: 'app-barcode-scanner',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  templateUrl: './barcode-scanner.page.html',
  styleUrl: './barcode-scanner.page.css',
})
export class BarcodeScannerPage implements AfterViewInit, OnDestroy {
  @Input() embedded = false;
  @Output() readonly openProductDetail = new EventEmitter<number>();
  @ViewChild('video') video?: ElementRef<HTMLVideoElement>;
  @ViewChild('barcodeInput') barcodeInput?: ElementRef<HTMLInputElement>;
  readonly api = inject(ProductApiService);
  private readonly inventoryApi = inject(InventoryApiService);
  private readonly bidApi = inject(LiveStreamBidProductApiService);
  private readonly reader = new BrowserMultiFormatReader(new Map<DecodeHintType, unknown>([
    [DecodeHintType.POSSIBLE_FORMATS, [BarcodeFormat.QR_CODE]],
    [DecodeHintType.TRY_HARDER, true],
  ]), { delayBetweenScanAttempts: 80, delayBetweenScanSuccess: 800 });
  private controls?: IScannerControls;
  private lastValue = '';
  private lastScanAt = 0;
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
  actualSalePrice: number | null = null;
  operationLocationKey: string | null = null;
  manualCode = '';
  addForm = { localInventoryId: '', sku: '', productName: '', qty: 0 };

  ngAfterViewInit(): void {
    if (matchMedia('(max-width: 820px), (pointer: coarse)').matches) this.setInputMode('camera');
    else this.setInputMode('scanner');
  }

  ngOnDestroy(): void {
    this.stopCamera();
    if (this.scannerInputTimer) clearTimeout(this.scannerInputTimer);
  }

  setInputMode(mode: 'camera' | 'scanner'): void {
    this.inputMode.set(mode);
    this.reset();
    if (mode === 'camera') void this.startCamera();
    else {
      this.stopCamera();
      setTimeout(() => this.barcodeInput?.nativeElement.focus());
    }
  }

  setScanMode(mode: 'inventory' | 'bid'): void {
    this.scanMode.set(mode);
    this.reset();
    if (this.inputMode() === 'camera') void this.startCamera();
  }

  async startCamera(): Promise<void> {
    if (!this.video || this.scanning()) return;
    this.error.set('');
    if (!globalThis.isSecureContext && location.hostname !== 'localhost') {
      this.error.set('Camera access requires HTTPS. You can still enter the barcode manually.');
      return;
    }
    try {
      this.scanning.set(true);
      this.controls = await this.reader.decodeFromConstraints(
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
          if (!result) return;
          const value = result.getText().trim();
          const now = Date.now();
          if (!value || (value === this.lastValue && now - this.lastScanAt < 2500)) return;
          this.lastValue = value;
          this.lastScanAt = now;
          navigator.vibrate?.(80);
          void this.lookupCode(value);
        },
      );
      this.torchAvailable.set(!!this.controls.switchTorch);
    } catch (err) {
      this.scanning.set(false);
      this.error.set(this.cameraError(err));
    }
  }

  stopCamera(): void {
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
    }, 180);
  }

  @HostListener('document:keydown', ['$event'])
  captureHardwareScanner(event: KeyboardEvent): void {
    const target = event.target as HTMLElement | null;
    if (target?.matches('input, textarea, select, [contenteditable="true"]') || event.ctrlKey || event.altKey || event.metaKey) return;
    const now = Date.now();
    if (now - this.hardwareLastKeyAt > 140) this.hardwareBuffer = '';
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
    try {
      const [product, today] = await Promise.all([this.bidApi.lookup(uuid), this.bidApi.today()]);
      this.bidProduct.set(product);
      this.bidToday.set(today);
      this.notFound.set(!product);
    } catch {
      this.error.set('Unable to reach bid inventory. Check the API connection and try again.');
    } finally { this.loading.set(false); }
  }

  async confirmBidSale(): Promise<void> {
    const product = this.bidProduct();
    if (!product || this.loading()) return;
    const price = this.actualSalePrice === null || this.actualSalePrice === undefined || String(this.actualSalePrice).trim() === ''
      ? null : Number(this.actualSalePrice);
    if (price !== null && (!Number.isFinite(price) || price < 0)) {
      this.error.set('Actual sale price must be zero or greater.');
      return;
    }
    this.loading.set(true);
    this.error.set('');
    try {
      const sale = await this.bidApi.confirmSale(product.id, price);
      const confirmation = `Bid sale recorded${sale.actualSalePriceUsd === null ? '' : ` at $${sale.actualSalePriceUsd.toFixed(2)}`}. Ready for the next scan.`;
      this.reset();
      this.success.set(confirmation);
    } catch (error: any) {
      this.error.set(error?.status === 409 ? 'This bid product has already been sold.' : 'The bid sale could not be recorded.');
    } finally { this.loading.set(false); }
  }

  async adjustBidActualInventory(delta: 1 | -1): Promise<void> {
    const product = this.bidProduct();
    if (!product || this.loading()) return;
    this.loading.set(true); this.error.set(''); this.success.set('');
    try {
      const updated = await this.bidApi.adjustActualInventory(product.id, delta);
      this.bidProduct.set(updated);
      this.success.set(`Actual inventory ${delta > 0 ? 'increased' : 'decreased'} to ${updated.actualInventory}.`);
    } catch (error: any) {
      this.error.set(error?.error || (delta > 0 ? 'Actual inventory cannot exceed expected inventory.' : 'Actual inventory cannot be negative.'));
    } finally { this.loading.set(false); }
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
    this.product.set(null);
    this.bidProduct.set(null);
    this.bidToday.set(null);
    this.notFound.set(false);
    this.scannedCode.set('');
    this.locations.set([]);
    this.warehouseLevels.set([]);
    this.operation.set(null);
    this.scannedType.set(null);
    this.operationLocationKey = null;
    this.operationQty = 1;
    this.showAdd.set(false);
    this.success.set('');
    this.manualCode = '';
    this.actualSalePrice = null;
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
