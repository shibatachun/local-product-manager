import { CommonModule } from '@angular/common';
import { AfterViewChecked, Component, ElementRef, Input, QueryList, ViewChildren, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { BrowserQRCodeSvgWriter } from '@zxing/browser';
import { EncodeHintType } from '@zxing/library';
import { ProductApiService } from '../../core/product-api.service';
import { InventoryApiService } from '../../core/inventory-api.service';
import { ProductListItem } from '../../core/models';
import { productCategoryNumber } from '../../core/product-inventory-category';
import { emptyLabelPresetSlots, LabelSettings, loadSavedLabelPresets, SavedLabelPreset, saveLabelPresets } from '../../core/label-preset-storage';
import { LabelPrintingApiService, LabelPrintingSettings } from '../../core/label-printing-api.service';

type LabelPreset = { name: string; width: number; height: number; columns: 1 | 2 };
type BinLabel = { code: string; description: string };

@Component({
  selector: 'app-label-maker',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  templateUrl: './label-maker.page.html',
  styleUrl: './label-maker.page.css',
})
export class LabelMakerPage implements AfterViewChecked {
  @Input() embedded = false;
  @ViewChildren('barcode') barcodeElements?: QueryList<ElementRef<HTMLElement>>;
  private readonly api = inject(ProductApiService);
  private readonly inventoryApi = inject(InventoryApiService);
  private readonly thirdPartyPrinting = inject(LabelPrintingApiService);

  readonly products = signal<ProductListItem[]>([]);
  readonly loading = signal(true);
  readonly error = signal('');
  readonly printMessage = signal('');
  readonly thirdPartySettings = signal<LabelPrintingSettings | null>(null);
  readonly mobileMenuOpen = signal(false);
  readonly selectedIds = signal(new Set<number>());
  readonly bins = signal<BinLabel[]>([]);
  readonly selectedBinCodes = signal(new Set<string>());
  labelMode: 'product' | 'bin' = 'product';
  binSearch = '';
  binDescription = '';

  readonly presets: LabelPreset[] = [
    { name: '70 × 20 mm · Single column', width: 70, height: 20, columns: 1 },
    { name: '50 × 30 mm · Double column', width: 50, height: 30, columns: 2 },
  ];
  search = '';
  preset = this.presets[0];
  widthMm = this.preset.width;
  heightMm = this.preset.height;
  labelGapMm = 3;
  columns: 1 | 2 = this.preset.columns;
  columnGapMm = 0;
  rowGapMm = 0;
  sideMarginMm = 0;
  fontSize = 11;
  barcodeHeight = 48;
  showInv = true;
  showCategoryId = true;
  showSku = true;
  showName = true;
  showQuantity = false;
  customPresets: SavedLabelPreset[] = emptyLabelPresetSlots();

  constructor() {
    this.loadCustomPresets();
    void this.loadProducts();
    void this.loadBins();
    void this.loadThirdPartySettings();
  }

  ngAfterViewChecked(): void {
    this.barcodeElements?.forEach(reference => {
      const svg = reference.nativeElement;
      const value = svg.dataset['value'] || '';
      if (!value || svg.dataset['rendered'] === value) return;
      try {
        svg.replaceChildren();
        const hints = new Map<EncodeHintType, unknown>([
          [EncodeHintType.ERROR_CORRECTION, 'M'],
          [EncodeHintType.MARGIN, 0],
        ]);
        // Render at the QR matrix's intrinsic size, then let CSS scale the SVG.
        // Passing an arbitrary pixel size makes ZXing center whole-pixel modules
        // inside a larger canvas, which looks like an excessive quiet zone.
        const qr = new BrowserQRCodeSvgWriter().write(value, 1, 1, hints);
        const intrinsicWidth = qr.getAttribute('width') || '1';
        const intrinsicHeight = qr.getAttribute('height') || intrinsicWidth;
        if (!qr.getAttribute('viewBox')) qr.setAttribute('viewBox', `0 0 ${intrinsicWidth} ${intrinsicHeight}`);
        qr.setAttribute('width', '100%');
        qr.setAttribute('height', '100%');
        qr.setAttribute('preserveAspectRatio', 'xMidYMid meet');
        qr.style.display = 'block';
        svg.appendChild(qr);
        svg.dataset['rendered'] = value;
      } catch {
        svg.replaceChildren();
      }
    });
  }

  get filteredProducts(): ProductListItem[] {
    const keyword = this.search.trim().toLocaleLowerCase();
    return this.products().filter(product => !keyword ||
      product.productName.toLocaleLowerCase().includes(keyword) ||
      product.sku.toLocaleLowerCase().includes(keyword) ||
      this.categoryInventoryNumber(product).toLocaleLowerCase().includes(keyword) ||
      this.inventoryNumber(product).toLocaleLowerCase().includes(keyword));
  }

  get selectedProducts(): ProductListItem[] {
    const ids = this.selectedIds();
    return this.products().filter(product => ids.has(product.id));
  }

  get labelRows(): ProductListItem[][] {
    const rows: ProductListItem[][] = [];
    const products = this.selectedProducts;
    for (let index = 0; index < products.length; index += this.columns) {
      rows.push(products.slice(index, index + this.columns));
    }
    return rows;
  }

  get filteredBins(): BinLabel[] {
    const keyword = this.binSearch.trim().toLocaleLowerCase();
    return this.bins().filter(bin => !keyword ||
      bin.code.toLocaleLowerCase().includes(keyword) ||
      bin.description.toLocaleLowerCase().includes(keyword));
  }

  get selectedBins(): BinLabel[] {
    const codes = this.selectedBinCodes();
    return this.bins().filter(bin => codes.has(bin.code));
  }

  get binLabelRows(): BinLabel[][] {
    const rows: BinLabel[][] = [];
    for (let index = 0; index < this.selectedBins.length; index += this.columns) {
      rows.push(this.selectedBins.slice(index, index + this.columns));
    }
    return rows;
  }

  get rollWidthMm(): number {
    return (this.widthMm * this.columns) + (this.columnGapMm * (this.columns - 1)) + (this.sideMarginMm * 2);
  }

  get rollRowHeightMm(): number {
    return this.heightMm + this.rowGapMm;
  }

  selectPreset(preset: LabelPreset): void {
    this.preset = preset;
    this.widthMm = preset.width;
    this.heightMm = preset.height;
    this.columns = preset.columns;
    this.columnGapMm = 0;
    this.rowGapMm = 0;
    this.sideMarginMm = 0;
  }

  saveCustomPreset(index: number): void {
    const slot = this.customPresets[index];
    if (!slot) return;
    slot.settings = {
      labelMode: this.labelMode,
      widthMm: this.widthMm,
      heightMm: this.heightMm,
      columns: this.columns,
      columnGapMm: this.columnGapMm,
      rowGapMm: this.rowGapMm,
      sideMarginMm: this.sideMarginMm,
      fontSize: this.fontSize,
      barcodeHeight: this.barcodeHeight,
      showInv: this.showInv,
      showCategoryId: this.showCategoryId,
      showSku: this.showSku,
      showName: this.showName,
      showQuantity: this.showQuantity,
      binDescription: this.binDescription,
    };
    this.persistCustomPresets();
  }

  applyCustomPreset(index: number): void {
    const settings = this.customPresets[index]?.settings;
    if (!settings) return;
    this.labelMode = settings.labelMode;
    this.widthMm = settings.widthMm;
    this.heightMm = settings.heightMm;
    this.columns = settings.columns;
    this.columnGapMm = settings.columnGapMm;
    this.rowGapMm = settings.rowGapMm;
    this.sideMarginMm = settings.sideMarginMm;
    this.fontSize = settings.fontSize;
    this.barcodeHeight = settings.barcodeHeight;
    this.showInv = settings.showInv;
    this.showCategoryId = settings.showCategoryId;
    this.showSku = settings.showSku;
    this.showName = settings.showName;
    this.showQuantity = settings.showQuantity;
    this.binDescription = settings.binDescription;
    this.rerenderBarcodes();
  }

  clearCustomPreset(index: number): void {
    const slot = this.customPresets[index];
    if (!slot) return;
    slot.settings = null;
    this.persistCustomPresets();
  }

  toggleProduct(id: number): void {
    const next = new Set(this.selectedIds());
    next.has(id) ? next.delete(id) : next.add(id);
    this.selectedIds.set(next);
  }

  selectVisible(): void {
    const next = new Set(this.selectedIds());
    this.filteredProducts.forEach(product => next.add(product.id));
    this.selectedIds.set(next);
  }

  clearSelection(): void { this.selectedIds.set(new Set()); }

  toggleBin(code: string): void {
    const next = new Set(this.selectedBinCodes());
    next.has(code) ? next.delete(code) : next.add(code);
    this.selectedBinCodes.set(next);
  }

  selectVisibleBins(): void {
    const next = new Set(this.selectedBinCodes());
    this.filteredBins.forEach(bin => next.add(bin.code));
    this.selectedBinCodes.set(next);
  }

  clearBinSelection(): void { this.selectedBinCodes.set(new Set()); }

  inventoryNumber(product: ProductListItem): string {
    return product.localInventoryId || `INV-${product.id}`;
  }

  categoryInventoryNumber(product: ProductListItem): string {
    return productCategoryNumber(product);
  }

  barcodeValue(product: ProductListItem): string {
    return `LPM|${this.inventoryNumber(product)}`;
  }

  rerenderBarcodes(): void {
    this.barcodeElements?.forEach(item => delete item.nativeElement.dataset['rendered']);
  }

  private loadCustomPresets(): void {
    this.customPresets = loadSavedLabelPresets();
  }

  persistCustomPresets(): void {
    saveLabelPresets(this.customPresets);
  }

  printLabels(): void {
    if (!this.selectedProducts.length) return;
    const rows = Array.from(document.querySelectorAll<HTMLElement>('.label-sheet .label-row'));
    if (!rows.length) return;

    const frame = document.createElement('iframe');
    frame.style.cssText = 'position:fixed;left:-10000px;top:0;width:1px;height:1px;border:0';
    frame.setAttribute('aria-hidden', 'true');
    document.body.appendChild(frame);
    const frameDocument = frame.contentDocument;
    if (!frameDocument) {
      frame.remove();
      return;
    }

    const labelWidth = this.widthMm;
    const labelHeight = this.heightMm;
    const pageWidth = this.rollWidthMm;
    const pageHeight = this.rollRowHeightMm;
    frameDocument.open();
    frameDocument.write(`<!doctype html><html><head><meta charset="utf-8"><title>LPM labels</title>
      <style>
        @page { size: ${pageWidth}mm ${pageHeight}mm; margin: 0; }
        * { box-sizing: border-box; }
        html, body { width: ${pageWidth}mm; margin: 0; padding: 0; background: #fff; }
        body { color: #000; font-family: Arial, sans-serif; }
        .label-row {
          display: flex; width: ${pageWidth}mm; height: ${pageHeight}mm;
          gap: ${this.columnGapMm}mm; margin: 0;
          padding: 0 ${this.sideMarginMm}mm ${this.rowGapMm}mm;
          overflow: hidden; break-after: page; page-break-after: always;
        }
        .label-row:last-child { break-after: auto; page-break-after: auto; }
        .product-label {
          display: flex; flex: 0 0 ${labelWidth}mm; flex-direction: column;
          align-items: center; justify-content: center;
          width: ${labelWidth}mm; height: ${labelHeight}mm;
          margin: 0; padding: 2.5mm; overflow: hidden;
          color: #000; background: #fff; border: 0;
          font-size: ${this.fontSize}px; text-align: center;
        }
        .blank-label { visibility: hidden; }
        .product-name { display: block; width: 100%; margin-bottom: 1mm; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
        .barcode { display: grid; place-items: center; flex: 0 0 auto; width: ${this.barcodeHeight}px; height: ${this.barcodeHeight}px; max-width: 100%; max-height: 60%; padding: 1px; background: #fff; }
        .barcode svg { display: block; width: 100%; height: 100%; }
        .inv { margin-top: .3mm; font-family: Consolas, monospace; letter-spacing: .04em; }
        .category-id { font-family: Consolas, monospace; font-size: .95em; letter-spacing: .04em; }
        .meta { display: flex; justify-content: center; gap: 3mm; width: 100%; margin-top: .3mm; font-size: .92em; }
      </style></head><body>${rows.map(row => row.outerHTML).join('')}</body></html>`);
    frameDocument.close();

    setTimeout(() => {
      frame.contentWindow?.focus();
      frame.contentWindow?.print();
      setTimeout(() => frame.remove(), 1500);
    }, 300);
  }

  async printLabelsWithThirdParty(): Promise<void> {
    if (!this.selectedProducts.length) return;
    this.error.set(''); this.printMessage.set('');
    try {
      const result = await this.thirdPartyPrinting.printInventory(this.selectedProducts.map(product => ({
        recordType: 'inventory-product', recordId: this.barcodeValue(product), title: product.productName, sku: product.sku, copies: 1,
      })));
      this.printMessage.set(result.message);
    } catch (error: any) {
      this.error.set(error?.error?.message || 'Third-party label printing failed. Check General Settings and the printing service.');
    }
  }

  private async loadThirdPartySettings(): Promise<void> {
    try { this.thirdPartySettings.set(await this.thirdPartyPrinting.inventorySettings()); } catch { this.thirdPartySettings.set(null); }
  }

  printBinLabels(): void {
    if (!this.selectedBins.length) return;
    const rows = Array.from(document.querySelectorAll<HTMLElement>('.bin-label-sheet .label-row'));
    if (!rows.length) return;

    const frame = document.createElement('iframe');
    frame.style.cssText = 'position:fixed;left:-10000px;top:0;width:1px;height:1px;border:0';
    frame.setAttribute('aria-hidden', 'true');
    document.body.appendChild(frame);
    const doc = frame.contentDocument;
    if (!doc) {
      frame.remove();
      return;
    }
    doc.open();
    doc.write(`<!doctype html><html><head><meta charset="utf-8"><title>Bin labels</title><style>
      @page{size:${this.rollWidthMm}mm ${this.rollRowHeightMm}mm;margin:0}*{box-sizing:border-box}
      html,body{width:${this.rollWidthMm}mm;margin:0;padding:0;background:#fff}
      body{color:#000;font-family:Consolas,"Cascadia Mono",monospace}
      .label-row{display:flex;width:${this.rollWidthMm}mm;height:${this.rollRowHeightMm}mm;gap:${this.columnGapMm}mm;margin:0;padding:0 ${this.sideMarginMm}mm ${this.rowGapMm}mm;overflow:hidden;break-after:page;page-break-after:always}
      .label-row:last-child{break-after:auto;page-break-after:auto}
      .bin-label{display:flex;flex:0 0 ${this.widthMm}mm;flex-direction:column;align-items:center;justify-content:center;gap:1.2mm;width:${this.widthMm}mm;height:${this.heightMm}mm;padding:2.5mm;overflow:hidden;text-align:center}
      .bin-description{display:block;max-width:100%;overflow:hidden;font-family:Arial,sans-serif;font-size:${Math.max(9, this.fontSize)}px;font-weight:700;line-height:1.15;overflow-wrap:anywhere}
      .bin-code{display:block;max-width:100%;font-size:${Math.max(14, this.fontSize + 5)}px;font-weight:900;line-height:1.05;word-break:break-all}
      .blank-label{visibility:hidden}
    </style></head><body>${rows.map(row => row.outerHTML).join('')}</body></html>`);
    doc.close();
    setTimeout(() => {
      frame.contentWindow?.focus();
      frame.contentWindow?.print();
      setTimeout(() => frame.remove(), 1500);
    }, 300);
  }

  private async loadProducts(): Promise<void> {
    try {
      const parents = await this.api.search();
      this.products.set(parents.flatMap(product => [product, ...product.children]));
      if (this.products().length) this.selectedIds.set(new Set([this.products()[0].id]));
    } catch {
      this.error.set('Unable to load inventory products.');
    } finally {
      this.loading.set(false);
    }
  }

  private async loadBins(): Promise<void> {
    try {
      const warehouse = await this.inventoryApi.getWarehouse();
      if (!warehouse) return;
      const shelves = await Promise.all(
        warehouse.shelves.map(async shelf => ({
          rowNumber: shelf.rowNumber,
          rackNumber: shelf.rackNumber,
          detail: await this.inventoryApi.getShelf(shelf.id),
        })),
      );
      const bins = shelves
        .flatMap(shelf => shelf.detail.levels.flatMap(level => [1, 2].map(binNumber => ({
          code: `${level.code}-B${binNumber.toString().padStart(2, '0')}`,
          description: `Row ${shelf.rowNumber} · Rack ${shelf.rackNumber} · Level ${level.levelNumber} · Bin ${binNumber}`,
        }))))
        .sort((left, right) => left.code.localeCompare(right.code));
      this.bins.set(bins);
    } catch {
      this.error.set('Unable to load warehouse bin labels.');
    }
  }
}
