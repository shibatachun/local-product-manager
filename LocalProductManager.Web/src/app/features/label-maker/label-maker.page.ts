import { CommonModule } from '@angular/common';
import { AfterViewChecked, Component, ElementRef, Input, QueryList, ViewChildren, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { BrowserQRCodeSvgWriter } from '@zxing/browser';
import { EncodeHintType } from '@zxing/library';
import { ProductApiService } from '../../core/product-api.service';
import { ProductListItem } from '../../core/models';

type LabelPreset = { name: string; width: number; height: number };

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

  readonly products = signal<ProductListItem[]>([]);
  readonly loading = signal(true);
  readonly error = signal('');
  readonly mobileMenuOpen = signal(false);
  readonly selectedIds = signal(new Set<number>());

  readonly presets: LabelPreset[] = [
    { name: '50 × 30 mm', width: 50, height: 30 },
    { name: '2 × 1 in', width: 50.8, height: 25.4 },
    { name: '3 × 1 in', width: 76.2, height: 25.4 },
    { name: '4 × 2 in', width: 101.6, height: 50.8 },
  ];
  search = '';
  preset = this.presets[0];
  widthMm = this.preset.width;
  heightMm = this.preset.height;
  labelGapMm = 3;
  columns: 1 | 2 = 2;
  columnGapMm = 0;
  rowGapMm = 0;
  sideMarginMm = 0;
  fontSize = 11;
  barcodeHeight = 48;
  showInv = true;
  showSku = true;
  showName = true;
  showQuantity = false;

  constructor() { void this.loadProducts(); }

  ngAfterViewChecked(): void {
    this.barcodeElements?.forEach(reference => {
      const svg = reference.nativeElement;
      const value = svg.dataset['value'] || '';
      if (!value || svg.dataset['rendered'] === value) return;
      try {
        svg.replaceChildren();
        const hints = new Map<EncodeHintType, unknown>([
          [EncodeHintType.ERROR_CORRECTION, 'M'],
          [EncodeHintType.MARGIN, 2],
        ]);
        svg.appendChild(new BrowserQRCodeSvgWriter().write(value, this.barcodeHeight, this.barcodeHeight, hints));
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

  get rollWidthMm(): number {
    return (this.widthMm * this.columns) + (this.columnGapMm * (this.columns - 1)) + (this.sideMarginMm * 2);
  }

  get rollRowHeightMm(): number {
    return this.heightMm + this.rowGapMm;
  }

  useTwoColumnRoll(): void {
    this.columns = 2;
    this.widthMm = 50;
    this.heightMm = 30;
    this.columnGapMm = 0;
    this.rowGapMm = 0;
    this.sideMarginMm = 0;
  }

  selectPreset(preset: LabelPreset): void {
    this.preset = preset;
    this.widthMm = preset.width;
    this.heightMm = preset.height;
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

  inventoryNumber(product: ProductListItem): string {
    return product.localInventoryId || `INV-${product.id}`;
  }

  barcodeValue(product: ProductListItem): string {
    return `LPM|${this.inventoryNumber(product)}`;
  }

  rerenderBarcodes(): void {
    this.barcodeElements?.forEach(item => delete item.nativeElement.dataset['rendered']);
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
        .barcode { display: grid; place-items: center; flex: 0 0 auto; width: 15mm; height: 15mm; max-width: 100%; max-height: 60%; }
        .barcode svg { display: block; width: 100%; height: 100%; }
        .inv { margin-top: .8mm; font-family: Consolas, monospace; letter-spacing: .04em; }
        .meta { display: flex; justify-content: center; gap: 3mm; width: 100%; margin-top: .7mm; font-size: .82em; }
      </style></head><body>${rows.map(row => row.outerHTML).join('')}</body></html>`);
    frameDocument.close();

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
}
