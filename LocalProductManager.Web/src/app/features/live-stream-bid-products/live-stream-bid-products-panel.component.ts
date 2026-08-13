import { CommonModule } from '@angular/common';
import { Component, Input, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { LiveStreamBidDailySummary, LiveStreamBidDraftRow, LiveStreamBidProduct, LiveStreamBidProductApiService } from '../../core/live-stream-bid-product-api.service';
import { InventoryApiService, ProductScannerLocation } from '../../core/inventory-api.service';
import { ProductListItem } from '../../core/models';
import { LabelPrintingApiService, LabelPrintingSettings } from '../../core/label-printing-api.service';

@Component({
  selector: 'app-live-stream-bid-products-panel',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './live-stream-bid-products-panel.component.html',
  styleUrls: ['./live-stream-bid-products-panel.component.css', './live-stream-bid-products-create.component.css', './live-stream-bid-products-sku.component.css'],
})
export class LiveStreamBidProductsPanelComponent implements OnInit {
  @Input() canImport = false;
  @Input() isAdmin = false;
  @Input() canMove = false;
  private readonly api = inject(LiveStreamBidProductApiService);
  private readonly inventoryApi = inject(InventoryApiService);
  private readonly labelPrinting = inject(LabelPrintingApiService);
  readonly products = signal<LiveStreamBidProduct[]>([]);
  readonly search = signal('');
  readonly loading = signal(false);
  readonly message = signal('');
  readonly error = signal('');
  readonly history = signal<LiveStreamBidDailySummary[]>([]);
  readonly selectedDay = signal<LiveStreamBidDailySummary | null>(null);
  readonly displayCurrency = signal<'usd' | 'rmb'>('usd');
  readonly moveOpen = signal(false);
  readonly localInventoryProducts = signal<ProductListItem[]>([]);
  readonly moveLocations = signal<ProductScannerLocation[]>([]);
  moveProductId: number | null = null;
  moveLocationKey = '';
  moveQuantity = 1;
  selectedFile: File | null = null;
  readonly createOpen = signal(false);
  readonly draftRows = signal<LiveStreamBidDraftRow[]>([]);
  readonly editingProduct = signal<LiveStreamBidProduct | null>(null);
  readonly targetBatch = signal<string | null>(null);
  readonly expandedBatch = signal<string | null>(null);
  readonly labelPrintingSettings = signal<LabelPrintingSettings | null>(null);
  readonly safePriceHelpOpen = signal(false);
  draftProductId: number | null = null;

  readonly filteredProducts = computed(() => {
    const keyword = this.search().trim().toLowerCase();
    if (!keyword) return this.products();
    return this.products().filter(item =>
      item.uuid.toLowerCase().includes(keyword) ||
      (item.actualSku ?? '').toLowerCase().includes(keyword) ||
      item.title.toLowerCase().includes(keyword) ||
      (item.material ?? '').toLowerCase().includes(keyword));
  });
  readonly totalQuantity = computed(() => this.products().reduce((sum, item) => sum + item.inventory, 0));
  readonly totalCost = computed(() => this.products().reduce((sum, item) => sum + (item.totalCost ?? 0) * item.inventory, 0));
  readonly batchGroups = computed(() => {
    const groups = new Map<string, LiveStreamBidProduct[]>();
    for (const item of this.filteredProducts()) {
      const key = item.batchNumber ?? `Legacy-${item.id}`;
      groups.set(key, [...(groups.get(key) ?? []), item]);
    }
    return [...groups.entries()].map(([key, products]) => ({
      key, batchNumber: products[0].batchNumber, products,
      inventory: products.reduce((sum, item) => sum + item.inventory, 0),
      cost: products.reduce((sum, item) => sum + (item.totalCost ?? 0) * item.inventory, 0),
      updatedAt: products.reduce((latest, item) => item.updatedAt > latest ? item.updatedAt : latest, products[0].updatedAt)
    }));
  });

  currencySymbol(): string { return this.displayCurrency() === 'usd' ? '$' : '¥'; }
  summaryCost(summary: LiveStreamBidDailySummary): number { return this.displayCurrency() === 'usd' ? summary.totalCostUsd : summary.totalCostRmb; }
  summaryRevenue(summary: LiveStreamBidDailySummary): number { return this.displayCurrency() === 'usd' ? summary.actualRevenueUsd : summary.actualRevenueRmb; }
  summaryProfit(summary: LiveStreamBidDailySummary): number { return this.displayCurrency() === 'usd' ? summary.realizedProfitUsd : summary.realizedProfitRmb; }

  ngOnInit(): void { void this.load(); void this.loadLabelPrintingSettings(); }

  private async loadLabelPrintingSettings(): Promise<void> {
    try { this.labelPrintingSettings.set(await this.labelPrinting.settings()); } catch { this.labelPrintingSettings.set(null); }
  }

  async printBidLabel(item: LiveStreamBidProduct): Promise<void> {
    const entered = window.prompt(`How many labels for ${item.title}?`, String(Math.max(1, item.inventory)));
    if (entered === null) return;
    const copies = Number(entered);
    if (!Number.isInteger(copies) || copies < 1 || copies > 999) {
      this.error.set('Enter a whole-number label quantity between 1 and 999.');
      return;
    }
    this.loading.set(true); this.error.set(''); this.message.set('');
    try { this.message.set(`${(await this.labelPrinting.printBidProduct(item.id, copies)).message} ${copies} label(s) requested.`); }
    catch (error: any) { this.error.set(error?.error?.message || error?.error || 'Could not send this label to the third-party printer.'); }
    finally { this.loading.set(false); }
  }

  selectFile(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.selectedFile = input.files?.[0] ?? null;
    this.message.set('');
    this.error.set('');
  }

  async load(): Promise<void> {
    this.loading.set(true);
    this.error.set('');
    try {
      const [products, history] = await Promise.all([this.api.list(), this.api.salesHistory()]);
      this.products.set(products);
      this.history.set(history);
      const selectedDate = this.selectedDay()?.date;
      if (selectedDate && history.some(day => day.date === selectedDate)) await this.openDay(selectedDate);
      else this.selectedDay.set(null);
    }
    catch (error: any) { this.error.set(error?.error || 'Could not load bid products.'); }
    finally { this.loading.set(false); }
  }

  async openDay(date: string): Promise<void> {
    this.loading.set(true);
    this.error.set('');
    try { this.selectedDay.set(await this.api.salesByDate(date)); }
    catch (error: any) { this.error.set(error?.error || 'Could not load sales for this date.'); }
    finally { this.loading.set(false); }
  }

  closeDay(): void { this.selectedDay.set(null); }

  async openMoveProduct(): Promise<void> {
    if (!this.canMove) return;
    this.loading.set(true);
    this.error.set('');
    try {
      const products = await this.inventoryApi.products();
      this.localInventoryProducts.set(products.flatMap(product => [product, ...product.children]).filter(product => product.qty > 0));
      this.moveProductId = null;
      this.moveLocationKey = '';
      this.moveQuantity = 1;
      this.moveLocations.set([]);
      this.moveOpen.set(true);
    } catch { this.error.set('Could not load local inventory products.'); }
    finally { this.loading.set(false); }
  }

  closeMoveProduct(): void { this.moveOpen.set(false); }

  async moveProductChanged(): Promise<void> {
    this.moveLocationKey = '';
    this.moveQuantity = 1;
    if (!this.moveProductId) { this.moveLocations.set([]); return; }
    this.loading.set(true);
    try { this.moveLocations.set((await this.inventoryApi.productLocations(this.moveProductId)).filter(location => location.qty > 0)); }
    catch { this.error.set('Could not load this product\'s warehouse locations.'); }
    finally { this.loading.set(false); }
  }

  async moveSelectedProduct(): Promise<void> {
    const location = this.moveLocations().find(item => item.key === this.moveLocationKey);
    const quantity = Math.floor(Number(this.moveQuantity));
    if (!this.moveProductId || !location || quantity < 1 || quantity > location.qty) {
      this.error.set('Choose a location and enter an available quantity.');
      return;
    }
    this.loading.set(true);
    this.error.set('');
    try {
      const created = await this.api.moveFromInventory({
        productId: this.moveProductId, levelId: location.levelId,
        binNumber: location.binNumber, quantity,
      });
      this.moveOpen.set(false);
      this.message.set(`${created.title} moved to BID inventory with code BID | ${created.uuid}.`);
      await this.load();
    } catch (error: any) {
      this.error.set(error?.status === 409 ? 'That location no longer has enough inventory.' : 'Could not move this product to BID inventory.');
    } finally { this.loading.set(false); }
  }

  async deleteSale(id: number): Promise<void> {
    if (!this.isAdmin || !confirm('Delete this sales history record? This cannot be undone.')) return;
    const date = this.selectedDay()?.date;
    if (!date) return;
    this.loading.set(true);
    this.error.set('');
    try {
      await this.api.deleteSale(id);
      const [history, detail] = await Promise.all([this.api.salesHistory(), this.api.salesByDate(date)]);
      this.history.set(history);
      this.selectedDay.set(detail.sales.length ? detail : null);
      this.message.set('Sales history record deleted.');
    } catch (error: any) {
      this.error.set(error?.status === 403 ? 'Administrator access is required.' : 'Could not delete this sales record.');
    } finally { this.loading.set(false); }
  }

  async deleteDay(date: string, event: Event): Promise<void> {
    event.stopPropagation();
    if (!this.isAdmin || !confirm(`Delete every sales record for ${date}? This cannot be undone.`)) return;
    this.loading.set(true);
    this.error.set('');
    try {
      await this.api.deleteSalesByDate(date);
      this.history.set(await this.api.salesHistory());
      if (this.selectedDay()?.date === date) this.selectedDay.set(null);
      this.message.set(`All sales records for ${date} were deleted.`);
    } catch (error: any) {
      this.error.set(error?.status === 403 ? 'Administrator access is required.' : 'Could not delete this daily sales record.');
    } finally { this.loading.set(false); }
  }

  async importCsv(): Promise<void> {
    if (!this.selectedFile || this.loading()) return;
    this.loading.set(true);
    this.message.set('');
    this.error.set('');
    try {
      const result = await this.api.importCsv(this.selectedFile);
      this.message.set(`Import complete: ${result.created} created, ${result.updated} updated, ${result.skipped} skipped.`);
      this.selectedFile = null;
      await this.load();
    } catch (error: any) {
      this.error.set(typeof error?.error === 'string' ? error.error : 'Could not import this CSV file.');
    } finally { this.loading.set(false); }
  }

  async openCreateList(): Promise<void> {
    this.loading.set(true); this.error.set('');
    try {
      const products = await this.inventoryApi.products();
      this.localInventoryProducts.set(products.flatMap(product => [product, ...product.children]));
      this.draftRows.set([this.blankRow()]); this.draftProductId = null; this.createOpen.set(true);
      this.editingProduct.set(null); this.targetBatch.set(null);
    } catch { this.error.set('Could not load local products.'); }
    finally { this.loading.set(false); }
  }
  closeCreateList(): void { this.createOpen.set(false); this.editingProduct.set(null); this.targetBatch.set(null); }
  toggleBatch(key: string): void { this.expandedBatch.set(this.expandedBatch() === key ? null : key); }
  async openAddToBatch(batchNumber: string): Promise<void> {
    this.loading.set(true); this.error.set('');
    try {
      const products = await this.inventoryApi.products();
      this.localInventoryProducts.set(products.flatMap(product => [product, ...product.children]));
      this.draftRows.set([this.blankRow()]); this.draftProductId = null; this.editingProduct.set(null);
      this.targetBatch.set(batchNumber); this.createOpen.set(true);
    } catch { this.error.set('Could not load local products.'); }
    finally { this.loading.set(false); }
  }
  openEditProduct(item: LiveStreamBidProduct): void {
    const price = item.unitPrice, quantity = item.quantity, grams = item.weightGrams ?? 0, total = item.totalCost ?? 0;
    this.editingProduct.set(item); this.draftRows.set([{
      sourceProductId: item.sourceProductId, externalUuid: item.uuid, actualSku: item.actualSku, title: item.title, quantity, inventory: item.inventory,
      unitPrice: price, weightGrams: item.weightGrams, weightOunces: item.weightOunces,
      material: item.material, totalCost: item.totalCost, whatnotAuctionSafePrice: item.whatnotAuctionSafePrice, priceType: this.inferPriceType(price, quantity, grams, total)
    }]); this.createOpen.set(true);
  }
  addBlankRow(): void { this.draftRows.update(rows => [...rows, this.blankRow()]); }
  addWarehouseProduct(): void {
    const product = this.localInventoryProducts().find(item => item.id === this.draftProductId); if (!product) return;
    this.draftRows.update(rows => [...rows.filter(row => row.title.trim()), {
      sourceProductId: product.id, externalUuid: null, actualSku: product.sku, title: product.productName, quantity: 1, inventory: Math.max(1, product.qty),
      unitPrice: product.cost ?? 0, weightGrams: product.weight, weightOunces: product.weight == null ? null : +(product.weight / 28.349523125).toFixed(4),
      material: null, totalCost: product.cost == null ? null : +(product.cost + (product.weight ?? 0) * .05).toFixed(2), whatnotAuctionSafePrice: null, priceType: 'per-package'
    }]); this.draftProductId = null;
  }
  removeDraftRow(index: number): void { this.draftRows.update(rows => rows.filter((_, i) => i !== index)); }
  updateOunces(row: LiveStreamBidDraftRow): void { row.weightOunces = row.weightGrams == null ? null : +(Number(row.weightGrams) / 28.349523125).toFixed(4); }
  async loadDraftCsv(event: Event): Promise<void> {
    const file = (event.target as HTMLInputElement).files?.[0]; if (!file) return;
    const records = this.parseCsv(await file.text()); if (records.length < 2) return;
    const headings = records[0].map(value => value.trim().toLowerCase());
    const at = (row: string[], name: string) => row[headings.indexOf(name)] ?? '';
    this.draftRows.set(records.slice(1).filter(row => row.some(Boolean)).map(row => ({
      sourceProductId: null, externalUuid: this.normalizeImportedUuid(at(row, 'uuid')), actualSku: at(row, 'actual sku') || null, title: at(row, 'title'), quantity: +(at(row, 'quantity') || 1), inventory: +(at(row, 'inventory') || 1),
      unitPrice: +(at(row, 'unit price') || 0), weightGrams: at(row, 'weight (g)') === '' ? null : +at(row, 'weight (g)'),
      weightOunces: at(row, 'weight (oz)') === '' ? null : +at(row, 'weight (oz)'), material: at(row, 'material') || null,
      totalCost: at(row, 'total cost') === '' ? null : +at(row, 'total cost'),
      whatnotAuctionSafePrice: at(row, 'whatnot auction safe price') === '' ? null : +at(row, 'whatnot auction safe price')
      , priceType: this.inferPriceType(+(at(row, 'unit price') || 0), +(at(row, 'quantity') || 1), +(at(row, 'weight (g)') || 0), +(at(row, 'total cost') || 0))
    })));
  }
  async saveBatch(): Promise<void> {
    const rows = this.draftRows().filter(row => row.title.trim()); if (!rows.length) { this.error.set('Add at least one product.'); return; }
    this.loading.set(true); this.error.set('');
    try {
      const editing = this.editingProduct();
      if (editing) { await this.api.updateProduct(editing.id, rows[0]); this.message.set(`BID|${editing.uuid} updated.`); }
      else if (this.targetBatch()) { const result = await this.api.addBatchProducts(this.targetBatch()!, rows); this.message.set(`${result.products.length} products added to ${result.batchNumber}.`); }
      else { const result = await this.api.createBatch(rows); this.message.set(`Batch ${result.batchNumber} created with ${result.products.length} products.`); }
      this.createOpen.set(false); this.editingProduct.set(null); this.targetBatch.set(null); await this.load();
    }
    catch (error: any) { this.error.set(typeof error?.error === 'string' ? error.error : 'Could not create BID list.'); }
    finally { this.loading.set(false); }
  }
  async exportBatch(batch: string | null): Promise<void> { if (batch) await this.api.exportBatch(batch); }
  async deleteProduct(item: LiveStreamBidProduct): Promise<void> {
    if (!confirm(`Delete BID|${item.uuid} (${item.title})?`)) return;
    this.loading.set(true); this.error.set('');
    try { await this.api.deleteProduct(item.id); this.message.set(`BID|${item.uuid} deleted.`); await this.load(); }
    catch (error: any) { this.error.set(error?.status === 409 ? 'This product has sales history and cannot be deleted.' : 'Could not delete this BID product.'); }
    finally { this.loading.set(false); }
  }
  recalculate(row: LiveStreamBidDraftRow): void {
    const price = Number(row.unitPrice), quantity = Number(row.quantity), grams = Number(row.weightGrams ?? 0);
    const merchandise = row.priceType === 'per-package' ? price : price * quantity;
    row.totalCost = Number.isFinite(merchandise) ? +(merchandise + grams * .05).toFixed(2) : null;
    this.updateOunces(row);
  }
  private blankRow(): LiveStreamBidDraftRow { return { sourceProductId: null, externalUuid: null, actualSku: null, title: '', quantity: 1, inventory: 1, unitPrice: 0, weightGrams: null, weightOunces: null, material: null, totalCost: 0, whatnotAuctionSafePrice: null, priceType: 'per-package' }; }
  private normalizeImportedUuid(value: string): string | null { const normalized = value.trim().replace(/^BID\s*\|\s*/i, '').trim(); return normalized || null; }
  private inferPriceType(price: number, quantity: number, grams: number, total: number): 'per-unit' | 'per-package' { return Math.abs(total - (price * quantity + grams * .05)) < Math.abs(total - (price + grams * .05)) ? 'per-unit' : 'per-package'; }
  private parseCsv(text: string): string[][] {
    const rows: string[][] = []; let row: string[] = [], value = '', quoted = false;
    for (let i = 0; i < text.length; i++) { const char = text[i]; if (char === '"') { if (quoted && text[i + 1] === '"') { value += '"'; i++; } else quoted = !quoted; } else if (char === ',' && !quoted) { row.push(value); value = ''; } else if ((char === '\n' || char === '\r') && !quoted) { if (char === '\r' && text[i + 1] === '\n') i++; row.push(value); rows.push(row); row = []; value = ''; } else value += char; }
    if (value || row.length) { row.push(value); rows.push(row); } return rows;
  }
}
