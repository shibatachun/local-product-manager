import { CommonModule } from '@angular/common';
import { AfterViewInit, Component, ElementRef, Input, OnDestroy, ViewChild, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { BarcodeFormat, BrowserMultiFormatReader, IScannerControls } from '@zxing/browser';
import { DecodeHintType } from '@zxing/library';
import { ProductApiService } from '../../core/product-api.service';
import { InventoryApiService } from '../../core/inventory-api.service';
import { ProductListItem } from '../../core/models';

type ScannerLocation = {
  levelId: number;
  binNumber: number;
  key: string;
  code: string;
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
  @ViewChild('video') video?: ElementRef<HTMLVideoElement>;
  readonly api = inject(ProductApiService);
  private readonly inventoryApi = inject(InventoryApiService);
  private readonly reader = new BrowserMultiFormatReader(new Map<DecodeHintType, unknown>([
    [DecodeHintType.POSSIBLE_FORMATS, [BarcodeFormat.QR_CODE]],
    [DecodeHintType.TRY_HARDER, true],
  ]), { delayBetweenScanAttempts: 80, delayBetweenScanSuccess: 800 });
  private controls?: IScannerControls;
  private lastValue = '';
  private lastScanAt = 0;

  readonly scanning = signal(false);
  readonly loading = signal(false);
  readonly error = signal('');
  readonly scannedCode = signal('');
  readonly product = signal<ProductListItem | null>(null);
  readonly locations = signal<ScannerLocation[]>([]);
  readonly warehouseLevels = signal<ScannerLocation[]>([]);
  readonly notFound = signal(false);
  readonly showAdd = signal(false);
  readonly success = signal('');
  readonly mobileMenuOpen = signal(false);
  readonly torchAvailable = signal(false);
  readonly torchOn = signal(false);
  readonly operation = signal<'in' | 'out' | null>(null);
  operationQty = 1;
  operationLocationKey: string | null = null;
  manualCode = '';
  addForm = { localInventoryId: '', sku: '', productName: '', qty: 0 };

  ngAfterViewInit(): void {
    if (matchMedia('(max-width: 820px), (pointer: coarse)').matches) void this.startCamera();
  }

  ngOnDestroy(): void { this.stopCamera(); }

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
          if (!value.startsWith('LPM|')) return;
          const now = Date.now();
          if (!value || (value === this.lastValue && now - this.lastScanAt < 2500)) return;
          this.lastValue = value;
          this.lastScanAt = now;
          navigator.vibrate?.(80);
          void this.lookup(value.slice(4));
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

  submitManual(): void { void this.lookup(this.manualCode); }

  async lookup(raw: string): Promise<void> {
    const code = raw.trim();
    if (!code || this.loading()) return;
    this.stopCamera();
    this.loading.set(true);
    this.error.set('');
    this.success.set('');
    this.showAdd.set(false);
    this.scannedCode.set(code);
    try {
      const matches = await this.api.search(code);
      const normalized = code.toLocaleLowerCase();
      const found = this.flatten(matches).find(item =>
        item.sku.trim().toLocaleLowerCase() === normalized ||
        item.localInventoryId?.trim().toLocaleLowerCase() === normalized,
      ) ?? null;
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
    this.notFound.set(false);
    this.scannedCode.set('');
    this.locations.set([]);
    this.warehouseLevels.set([]);
    this.operation.set(null);
    this.operationLocationKey = null;
    this.operationQty = 1;
    this.showAdd.set(false);
    this.success.set('');
    this.manualCode = '';
  }

  private flatten(products: ProductListItem[]): ProductListItem[] {
    return products.flatMap(product => [product, ...product.children]);
  }

  startOperation(type: 'in' | 'out'): void {
    this.operation.set(type);
    this.operationQty = 1;
    const options = type === 'in' ? this.warehouseLevels() : this.locations();
    this.operationLocationKey = options[0]?.key ?? null;
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
      this.error.set(`Only ${location.qty} item(s) are available at ${location.code}.`);
      return;
    }

    this.loading.set(true);
    this.error.set('');
    try {
      const newQty = type === 'in' ? location.qty + amount : location.qty - amount;
      await this.inventoryApi.setInventory(location.levelId, product.id, newQty, location.binNumber);
      this.product.set(await this.api.getProduct(product.id));
      await this.loadLocations(product.id);
      this.success.set(`${type === 'in' ? 'Stock in' : 'Stock out'} completed: ${amount} item(s) at ${location.code}.`);
      this.cancelOperation();
    } catch {
      this.error.set('The inventory operation could not be completed.');
    } finally {
      this.loading.set(false);
    }
  }

  private async loadLocations(productId: number): Promise<void> {
    const warehouse = await this.inventoryApi.getWarehouse();
    if (!warehouse) {
      this.locations.set([]);
      this.warehouseLevels.set([]);
      return;
    }
    const shelves = await Promise.all(warehouse.shelves.map(shelf => this.inventoryApi.getShelf(shelf.id)));
    const levels = shelves.flatMap(shelf => shelf.levels.flatMap(level =>
      [1, 2].map(binNumber => {
        const item = level.items.find(row => row.productId === productId && row.binNumber === binNumber);
        return {
          levelId: level.id,
          binNumber,
          key: `${level.id}:${binNumber}`,
          code: `${level.code}-B${binNumber.toString().padStart(2, '0')}`,
          qty: item?.qty ?? 0,
        };
      })));
    this.warehouseLevels.set(levels);
    this.locations.set(levels.filter(level => level.qty > 0));
  }

  private cameraError(err: unknown): string {
    const name = (err as DOMException)?.name;
    if (name === 'NotAllowedError') return 'Camera permission was denied. Allow camera access or enter the barcode manually.';
    if (name === 'NotFoundError') return 'No camera was found on this device.';
    return 'The camera could not be started. You can enter the barcode manually.';
  }
}
