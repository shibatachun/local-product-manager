import { CommonModule } from '@angular/common';
import { Component, Input, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { CloudCsvFile, LiveStreamBidDailySummary, LiveStreamBidDraftRow, LiveStreamBidImportConflict, LiveStreamBidManualCost, LiveStreamBidProduct, LiveStreamBidProductApiService, LiveStreamBidRegeneratedUuid, LiveStreamBidSale, LiveStreamBidShowOrder, LiveStreamBidShowSummary } from '../../core/live-stream-bid-product-api.service';
import { InventoryApiService, ProductScannerLocation } from '../../core/inventory-api.service';
import { ProductListItem } from '../../core/models';
import { LabelPrintingApiService, LabelPrintingSettings } from '../../core/label-printing-api.service';
import { BarcodeScannerPage } from '../barcode-scanner/barcode-scanner.page';
import { BrowserSecretVaultService } from '../../core/browser-secret-vault.service';
import { MediaPickerComponent } from '../../shared/media-picker.component';

type BulkCsvImportResult = { fileName: string; batchNumber: string | null; products: number; error: string | null };
type UuidImportPreview = { fileName: string; rowNumber: number; title: string; originalUuid: string };

@Component({
  selector: 'app-live-stream-bid-products-panel',
  standalone: true,
  imports: [CommonModule, FormsModule, BarcodeScannerPage, MediaPickerComponent],
  templateUrl: './live-stream-bid-products-panel.component.html',
  styleUrls: ['./live-stream-bid-products-panel.component.css', './live-stream-bid-products-create.component.css', './live-stream-bid-products-sku.component.css', './live-stream-bid-products-hierarchy.component.css'],
})
export class LiveStreamBidProductsPanelComponent implements OnInit {
  @Input() canImport = false;
  @Input() isAdmin = false;
  @Input() isHost = false;
  @Input() canMove = false;
  private readonly api = inject(LiveStreamBidProductApiService);
  private readonly inventoryApi = inject(InventoryApiService);
  private readonly labelPrinting = inject(LabelPrintingApiService);
  private readonly secretVault = inject(BrowserSecretVaultService);
  readonly products = signal<LiveStreamBidProduct[]>([]);
  readonly search = signal('');
  readonly loading = signal(false);
  readonly message = signal('');
  readonly error = signal('');
  readonly history = signal<LiveStreamBidDailySummary[]>([]);
  readonly historyOpen = signal(true);
  readonly selectedDay = signal<LiveStreamBidDailySummary | null>(null);
  readonly activeShow = signal<LiveStreamBidShowSummary | null>(null);
  readonly selectedShowOrderId = signal<number | null>(null);
  readonly editingOrderQuantityId = signal<number | null>(null);
  readonly orderQuantityError = signal('');
  orderQuantityDraft = 1;
  readonly orderCostMode = signal<'manual' | 'scan'>('manual');
  readonly orderCostError = signal('');
  readonly orderCostMessage = signal('');
  readonly editingCost = signal<{ kind: 'manual' | 'scan'; id: number } | null>(null);
  costDraft: number | null = null;
  readonly costEditError = signal('');
  readonly priceEvidenceFeedback = signal<{ orderId: number; message: string; error: boolean } | null>(null);
  readonly expandedPriceEvidenceOrders = signal<ReadonlySet<number>>(new Set());
  readonly expandedNodes = signal<ReadonlySet<string>>(new Set());
  private readonly initializedActiveShows = new Set<string>();
  showReportFile: File | null = null;
  manualCostQuantity = 1;
  manualUnitCost: number | null = null;
  readonly displayCurrency = signal<'usd' | 'rmb'>('usd');
  readonly moveOpen = signal(false);
  readonly importToLocalProduct = signal<LiveStreamBidProduct | null>(null);
  importTransferInventory = true;
  readonly localInventoryProducts = signal<ProductListItem[]>([]);
  readonly moveLocations = signal<ProductScannerLocation[]>([]);
  moveProductId: number | null = null;
  moveLocationKey = '';
  moveQuantity = 1;
  selectedFile: File | null = null;
  readonly bulkCsvFiles = signal<File[]>([]);
  readonly bulkCsvResults = signal<BulkCsvImportResult[]>([]);
  readonly cloudCsvFiles = signal<CloudCsvFile[]>([]);
  readonly cloudImportError = signal<{ path: string; message: string } | null>(null);
  readonly cloudTokenRequired = signal(true);
  cloudToken = '';
  private draftExternalFile: { fileName: string; sha256: string } | null = null;
  readonly createOpen = signal(false);
  readonly draftRows = signal<LiveStreamBidDraftRow[]>([]);
  readonly editingProduct = signal<LiveStreamBidProduct | null>(null);
  readonly targetBatch = signal<string | null>(null);
  readonly expandedBatch = signal<string | null>(null);
  readonly labelPrintingSettings = signal<LabelPrintingSettings | null>(null);
  readonly safePriceHelpOpen = signal(false);
  readonly saleProduct = signal<LiveStreamBidProduct | null>(null);
  manualSalePrice: number | null = null;
  readonly importConflicts = signal<LiveStreamBidImportConflict[]>([]);
  readonly regeneratedUuids = signal<LiveStreamBidRegeneratedUuid[]>([]);
  readonly uuidImportConfirmation = signal<UuidImportPreview[]>([]);
  private pendingUuidImportAction: (() => Promise<void>) | null = null;
  draftProductId: number | null = null;

  readonly filteredProducts = computed(() => {
    const keywords = this.search().trim().toLowerCase().split(/\s+/).filter(Boolean);
    if (!keywords.length) return this.products();
    return this.products().filter(item => {
      const searchable = [
        item.uuid, `BID|${item.uuid}`, item.batchNumber, item.batchProductNumber,
        item.itemCode, item.actualSku, item.title, item.material,
        item.unitPrice, item.totalCost, item.whatnotAuctionSafePrice,
      ].filter(value => value !== null && value !== undefined).join(' ').toLowerCase();
      return keywords.every(keyword => searchable.includes(keyword));
    });
  });
  readonly totalQuantity = computed(() => this.products().reduce((sum, item) => sum + item.inventory, 0));
  readonly totalCost = computed(() => this.products().reduce((sum, item) => sum + (item.totalCost ?? 0) * item.inventory, 0));
  readonly averageUnitPrice = computed(() => {
    const inventory = this.totalQuantity();
    return inventory === 0
      ? null
      : this.products().reduce((sum, item) => sum + item.unitPrice * item.inventory, 0) / inventory;
  });
  readonly averageWeightGrams = computed(() => {
    const productsWithWeight = this.products().filter(item => item.weightGrams !== null && item.inventory > 0);
    const inventory = productsWithWeight.reduce((sum, item) => sum + item.inventory, 0);
    return inventory === 0
      ? null
      : productsWithWeight.reduce((sum, item) => sum + item.weightGrams! * item.inventory, 0) / inventory;
  });
  readonly batchGroups = computed(() => {
    const importedAtByBatch = new Map<string, string>();
    for (const item of this.products()) {
      const key = item.batchNumber ?? `Legacy-${item.id}`;
      const current = importedAtByBatch.get(key);
      if (!current || Date.parse(item.createdAt) < Date.parse(current)) {
        importedAtByBatch.set(key, item.createdAt);
      }
    }

    const groups = new Map<string, LiveStreamBidProduct[]>();
    for (const item of this.filteredProducts()) {
      const key = item.batchNumber ?? `Legacy-${item.id}`;
      groups.set(key, [...(groups.get(key) ?? []), item]);
    }
    return [...groups.entries()]
      .map(([key, products]) => ({
        key,
        batchNumber: products[0].batchNumber,
        products,
        inventory: products.reduce((sum, item) => sum + item.inventory, 0),
        cost: products.reduce((sum, item) => sum + (item.totalCost ?? 0) * item.inventory, 0),
        importedAt: importedAtByBatch.get(key) ?? products[0].createdAt,
      }))
      .sort((left, right) => Date.parse(right.importedAt) - Date.parse(left.importedAt));
  });
  readonly selectedShowOrder = computed(() => {
    const id = this.selectedShowOrderId();
    return this.activeShow()?.customers.flatMap(customer => customer.orders).find(order => order.id === id) ?? null;
  });

  private showKey(show: LiveStreamBidShowSummary): string {
    return `${show.id ?? `${show.date}-${show.showNumber}`}`;
  }

  private nodeKey(level: 'show' | 'buyer', show: LiveStreamBidShowSummary, customerName?: string): string {
    return level === 'show'
      ? `show:${this.showKey(show)}`
      : `buyer:${this.showKey(show)}:${customerName!.toLowerCase()}`;
  }

  isExpanded(level: 'show' | 'buyer', show: LiveStreamBidShowSummary, customerName?: string): boolean {
    return this.expandedNodes().has(this.nodeKey(level, show, customerName));
  }

  toggleExpanded(level: 'show' | 'buyer', show: LiveStreamBidShowSummary, customerName?: string): void {
    const key = this.nodeKey(level, show, customerName);
    this.expandedNodes.update(current => {
      const next = new Set(current);
      if (next.has(key)) next.delete(key); else next.add(key);
      return next;
    });
  }

  currencySymbol(): string { return this.displayCurrency() === 'usd' ? '$' : '¥'; }
  summaryCost(summary: LiveStreamBidDailySummary | LiveStreamBidShowSummary): number { return this.displayCurrency() === 'usd' ? summary.totalCostUsd : summary.totalCostRmb; }
  summaryRevenue(summary: LiveStreamBidDailySummary | LiveStreamBidShowSummary): number { return this.displayCurrency() === 'usd' ? summary.actualRevenueUsd : summary.actualRevenueRmb; }
  summaryProfit(summary: LiveStreamBidDailySummary | LiveStreamBidShowSummary): number { return this.displayCurrency() === 'usd' ? summary.realizedProfitUsd : summary.realizedProfitRmb; }
  bidImageUrl(product: LiveStreamBidProduct): string { return this.api.imageUrl(product.id, 96, product.updatedAt); }
  downloadBidPicture(product: LiveStreamBidProduct): void {
    const anchor = document.createElement('a');
    anchor.href = this.api.imageDownloadUrl(product.id);
    document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();
  }

  async openProductFolder(product: LiveStreamBidProduct): Promise<void> {
    if (!this.isAdmin || !this.isHost) return;
    this.error.set('');
    this.message.set('');
    try {
      await this.api.openProductFolder(product.id);
      this.message.set('BID product folder opened on the host computer.');
    } catch (error: any) {
      this.error.set(typeof error?.error === 'string'
        ? error.error
        : 'Could not open the BID product folder on this computer.');
    }
  }

  ngOnInit(): void { void this.load(); void this.loadLabelPrintingSettings(); void this.restoreCloudToken(); }

  private async restoreCloudToken(): Promise<void> {
    this.cloudToken = await this.secretVault.load('cloud-csv-token') ?? '';
    this.cloudTokenRequired.set(!this.cloudToken);
  }

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
      const [products, history, activeShow] = await Promise.all([
        this.api.list(), this.api.salesHistory(), this.api.activeShowSession()
      ]);
      this.products.set(products);
      this.history.set(history);
      this.activeShow.set(activeShow);
      if (activeShow) {
        const key = this.nodeKey('show', activeShow);
        if (!this.initializedActiveShows.has(key)) {
          this.initializedActiveShows.add(key);
          this.expandedNodes.update(current => new Set([...current, key]));
        }
      }
      if (!activeShow?.customers.some(customer => customer.orders.some(order => order.id === this.selectedShowOrderId()))) {
        this.selectedShowOrderId.set(null);
        this.orderCostMode.set('manual');
      }
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

  async toggleDay(date: string): Promise<void> {
    if (this.selectedDay()?.date === date) {
      this.closeDay();
      return;
    }
    await this.openDay(date);
  }

  selectedDayFor(date: string): LiveStreamBidDailySummary | null {
    const summary = this.selectedDay();
    return summary?.date === date ? summary : null;
  }

  closeDay(): void { this.selectedDay.set(null); }

  async startShow(): Promise<void> {
    if (this.loading() || this.activeShow()) return;
    this.loading.set(true);
    this.error.set('');
    this.message.set('');
    try {
      const show = await this.api.startShowSession();
      this.activeShow.set(show);
      await this.load();
      this.message.set(`Show ${show.showNumber} started. Scanned BID sales will stay in this session until settlement.`);
    } catch (error: any) {
      this.error.set(error?.status === 409
        ? (typeof error?.error === 'string' ? error.error : 'Another show session is already active.')
        : 'The show cost session could not be started.');
    } finally { this.loading.set(false); }
  }

  selectShowReport(event: Event): void {
    this.showReportFile = (event.target as HTMLInputElement).files?.[0] ?? null;
    this.error.set('');
    this.message.set('');
  }

  async importShowReport(): Promise<void> {
    if (!this.showReportFile || this.loading()) return;
    this.loading.set(true);
    this.error.set('');
    this.message.set('');
    try {
      const result = await this.api.importShowReport(this.showReportFile);
      this.showReportFile = null;
      await this.load();
      this.message.set(`Show imported: ${result.importedCustomers} customer(s), ${result.importedOrders} order(s), ${result.ignoredRows} cancelled/failed row(s) ignored.`);
    } catch (error: any) {
      this.error.set(typeof error?.error === 'string' ? error.error : 'The Whatnot show report could not be imported.');
    } finally { this.loading.set(false); }
  }

  selectShowOrder(order: LiveStreamBidShowOrder): void {
    if (this.selectedShowOrderId() === order.id) {
      this.selectedShowOrderId.set(null);
      this.orderCostMode.set('manual');
      this.orderCostError.set('');
      this.orderCostMessage.set('');
      return;
    }
    this.selectedShowOrderId.set(order.id);
    this.orderCostMode.set('manual');
    this.orderCostError.set('');
    this.orderCostMessage.set('');
    this.manualCostQuantity = 1;
    this.manualUnitCost = null;
  }

  editOrderQuantity(order: LiveStreamBidShowOrder): void {
    this.editingOrderQuantityId.set(order.id);
    this.orderQuantityDraft = order.quantity;
    this.orderQuantityError.set('');
  }

  cancelOrderQuantityEdit(): void {
    this.editingOrderQuantityId.set(null);
    this.orderQuantityError.set('');
  }

  async saveOrderQuantity(order: LiveStreamBidShowOrder): Promise<void> {
    if (this.loading()) return;
    const quantity = Number(this.orderQuantityDraft);
    if (!Number.isInteger(quantity) || quantity < 1 || quantity < order.recordedQuantity) {
      this.orderQuantityError.set(`Enter a whole number of at least ${Math.max(1, order.recordedQuantity)}; recorded items cannot be removed.`);
      return;
    }
    this.orderQuantityError.set('');
    if (quantity === order.quantity) { this.cancelOrderQuantityEdit(); return; }
    this.loading.set(true);
    try {
      await this.api.updateShowOrderQuantity(order.id, quantity);
      await this.load();
      if (this.error()) {
        this.orderQuantityError.set('Quantity was saved, but the page could not refresh. Reload to see the updated remaining count.');
        return;
      }
      this.cancelOrderQuantityEdit();
      this.orderCostMessage.set(`Order quantity updated to ${quantity} inventory item(s).`);
    } catch (error: any) {
      this.orderQuantityError.set(typeof error?.error === 'string' ? error.error : 'Could not update the order quantity.');
    } finally { this.loading.set(false); }
  }

  showAttachmentUrl(id: number): string { return this.api.showAttachmentUrl(id); }

  priceEvidenceDetailOpen(orderId: number): boolean {
    return this.expandedPriceEvidenceOrders().has(orderId);
  }

  togglePriceEvidenceDetail(orderId: number): void {
    this.expandedPriceEvidenceOrders.update(current => {
      const next = new Set(current);
      if (next.has(orderId)) next.delete(orderId);
      else next.add(orderId);
      return next;
    });
  }

  async uploadOrderPriceEvidence(orderId: number, event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;
    if (this.loading()) { input.value = ''; return; }
    this.priceEvidenceFeedback.set(null);
    if (file.size <= 0 || file.size > 20 * 1024 * 1024) {
      this.priceEvidenceFeedback.set({ orderId, message: 'Choose a photo between 1 byte and 20 MB.', error: true });
      input.value = '';
      return;
    }
    this.loading.set(true);
    this.error.set('');
    this.message.set('');
    try {
      await this.api.uploadOrderPriceEvidence(orderId, file);
      await this.load();
      if (this.error()) {
        this.priceEvidenceFeedback.set({ orderId, message: 'Photo saved, but the list could not refresh. Reload to view it.', error: true });
      } else {
        this.priceEvidenceFeedback.set({ orderId, message: 'Order price photo saved.', error: false });
      }
    } catch (error: any) {
      this.priceEvidenceFeedback.set({ orderId, message: this.priceEvidenceUploadError(error), error: true });
    } finally { input.value = ''; this.loading.set(false); }
  }

  private priceEvidenceUploadError(error: any): string {
    if (error instanceof Error && error.message.trim()) return error.message;
    if (typeof error?.error === 'string' && error.error.trim()) return error.error;
    const validationErrors = error?.error?.errors;
    if (validationErrors && typeof validationErrors === 'object') {
      const first = Object.values(validationErrors).flat().find(value => typeof value === 'string' && value.trim());
      if (typeof first === 'string') return first;
    }
    if (typeof error?.error?.detail === 'string' && error.error.detail.trim()) return error.error.detail;
    if (error?.status === 0) return 'Could not reach the server. Check the connection and try again.';
    if (error?.status === 413) return 'Photo is too large. Choose one under 20 MB.';
    if (error?.status === 401 || error?.status === 403) return 'Sign in with an account allowed to upload price photos.';
    return `Could not save the order price photo (HTTP ${error?.status || 'error'}).`;
  }

  async uploadShipmentDocument(showId: number | null, customerName: string,
    kind: 'shipping-slip' | 'shipping-label', event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (!file || showId === null || this.loading()) return;
    this.loading.set(true);
    this.error.set('');
    this.message.set('');
    try {
      await this.api.uploadShipmentDocument(showId, customerName, kind, file);
      await this.load();
      if (!this.error()) this.message.set(`${kind === 'shipping-slip' ? 'Shipping slip' : 'Shipping label'} saved for ${customerName}.`);
    } catch (error: any) {
      this.error.set(typeof error?.error === 'string' ? error.error : 'Could not save the shipment document.');
    } finally { this.loading.set(false); }
  }

  showManualCost(order: LiveStreamBidShowOrder): void {
    if (this.selectedShowOrderId() !== order.id) this.selectShowOrder(order);
    this.orderCostMode.set('manual');
    this.orderCostError.set('');
  }

  showCameraScanner(order: LiveStreamBidShowOrder): void {
    if (this.selectedShowOrderId() !== order.id) this.selectShowOrder(order);
    this.orderCostMode.set('scan');
    this.orderCostError.set('');
    this.orderCostMessage.set('');
  }

  async addManualCost(): Promise<void> {
    const order = this.selectedShowOrder();
    const quantity = Number(this.manualCostQuantity);
    const unitCost = Number(this.manualUnitCost);
    if (!order || !Number.isInteger(quantity) || quantity < 1 || quantity > order.remainingQuantity) {
      this.orderCostError.set(`Enter a whole-number quantity from 1 to ${order?.remainingQuantity ?? 0}.`);
      return;
    }
    if (this.manualUnitCost === null || !Number.isFinite(unitCost) || unitCost < 0) {
      this.orderCostError.set('Enter a valid unit cost of zero or greater.');
      return;
    }
    this.loading.set(true);
    this.error.set('');
    this.orderCostError.set('');
    this.orderCostMessage.set('');
    try {
      const saved = await this.api.addManualCost(order.id, quantity, unitCost);
      this.applyManualCostLocally(order.id, saved);
      this.manualCostQuantity = 1;
      this.manualUnitCost = null;
      this.orderCostMessage.set(`${quantity} item(s) recorded. Total manual cost: ¥${saved.totalCost.toFixed(2)}.`);
    } catch (error: any) {
      this.orderCostError.set(typeof error?.error === 'string' ? error.error : 'Could not save the manual cost.');
    } finally { this.loading.set(false); }
  }

  private applyManualCostLocally(orderId: number, saved: LiveStreamBidManualCost): void {
    const show = this.activeShow();
    if (!show) return;
    const quantity = saved.quantity;
    const cost = saved.totalCost;
    this.activeShow.set({
      ...show,
      inventorySold: show.inventorySold + quantity,
      totalCostRmb: show.totalCostRmb + cost,
      totalCostUsd: show.totalCostUsd + cost * 0.15,
      realizedProfitRmb: show.realizedProfitRmb - cost,
      realizedProfitUsd: show.realizedProfitUsd - cost * 0.15,
      recordedCostQuantity: show.recordedCostQuantity + quantity,
      remainingCostQuantity: Math.max(0, show.remainingCostQuantity - quantity),
      customers: show.customers.map(customer => {
        if (!customer.orders.some(item => item.id === orderId)) return customer;
        return {
          ...customer,
          recordedQuantity: customer.recordedQuantity + quantity,
          remainingQuantity: Math.max(0, customer.remainingQuantity - quantity),
          totalCostRmb: customer.totalCostRmb + cost,
          orders: customer.orders.map(item => item.id !== orderId ? item : {
            ...item,
            manualQuantity: item.manualQuantity + quantity,
            recordedQuantity: item.recordedQuantity + quantity,
            remainingQuantity: Math.max(0, item.remainingQuantity - quantity),
            totalCostRmb: item.totalCostRmb + cost,
            manualCosts: [...item.manualCosts, saved],
          }),
        };
      }),
    });
  }

  async deleteManualCost(id: number): Promise<void> {
    if (!confirm('Delete this manual cost entry?')) return;
    this.loading.set(true);
    this.error.set('');
    try { await this.api.deleteManualCost(id); await this.load(); }
    catch (error: any) { this.error.set(typeof error?.error === 'string' ? error.error : 'Could not delete the manual cost.'); }
    finally { this.loading.set(false); }
  }

  startCostEdit(kind: 'manual' | 'scan', id: number, currentCost: number | null): void {
    this.editingCost.set({ kind, id });
    this.costDraft = currentCost;
    this.costEditError.set('');
  }

  isEditingCost(kind: 'manual' | 'scan', id: number): boolean {
    const edit = this.editingCost();
    return edit?.kind === kind && edit.id === id;
  }

  orderHasEditedCost(order: LiveStreamBidShowOrder): boolean {
    const edit = this.editingCost();
    return !!edit && (edit.kind === 'manual'
      ? order.manualCosts.some(item => item.id === edit.id)
      : order.scannedCosts.some(item => item.id === edit.id));
  }

  cancelCostEdit(): void {
    this.editingCost.set(null);
    this.costDraft = null;
    this.costEditError.set('');
  }

  async saveCostEdit(): Promise<void> {
    const edit = this.editingCost();
    const amount = Number(this.costDraft);
    if (!edit || this.costDraft === null || !Number.isFinite(amount) || amount < 0) {
      this.costEditError.set('Enter a cost of zero or greater.');
      return;
    }
    this.loading.set(true);
    this.costEditError.set('');
    try {
      if (edit.kind === 'manual') await this.api.updateManualCost(edit.id, amount);
      else await this.api.updateScannedCost(edit.id, amount);
      await this.load();
      if (this.error()) {
        this.costEditError.set('Cost was saved, but the page could not refresh. Reload to see the new totals.');
        return;
      }
      this.cancelCostEdit();
      this.message.set('Cost updated. Order and show totals have been recalculated.');
    } catch (error: any) {
      this.costEditError.set(typeof error?.error === 'string' ? error.error : 'Could not update this cost.');
    } finally { this.loading.set(false); }
  }

  async stopShow(): Promise<void> {
    const show = this.activeShow();
    if (!show?.id || this.loading()) return;
    if (!confirm(`Stop and settle Show ${show.showNumber}? This session cannot receive more scans after settlement.`)) return;
    this.loading.set(true);
    this.error.set('');
    this.message.set('');
    try {
      const settled = await this.api.stopShowSession(show.id);
      this.activeShow.set(null);
      const key = this.nodeKey('show', show);
      this.initializedActiveShows.delete(key);
      this.expandedNodes.update(current => {
        const next = new Set(current);
        next.delete(key);
        return next;
      });
      await this.load();
      await this.openDay(settled.date);
      this.message.set(settled.soldCount === 0 && settled.customers.length === 0
        ? `Empty Show ${settled.showNumber} was removed.`
        : `Show ${settled.showNumber} settled with ${settled.soldCount} sale(s) and ${this.currencySymbol()}${this.summaryCost(settled).toFixed(2)} cost.`);
    } catch (error: any) {
      this.error.set(typeof error?.error === 'string' ? error.error : 'The show cost session could not be settled.');
    } finally { this.loading.set(false); }
  }

  async bidSaleRecorded(sale: LiveStreamBidSale): Promise<void> {
    const order = this.selectedShowOrder();
    if (!order || sale.showOrderId !== order.id) {
      await this.load();
      this.message.set('Sale added to the active show cost session.');
      return;
    }
    this.applyScannedCostLocally(order.id, sale);
    this.orderCostMessage.set(`Scanned cost recorded: ${sale.productTitle}.`);
    this.products.update(products => products.map(product => product.id !== sale.bidProductId ? product : {
      ...product,
      inventory: Math.max(0, product.inventory - sale.soldQuantity),
      actualInventory: Math.max(0, product.actualInventory - sale.soldQuantity),
    }));
  }

  private applyScannedCostLocally(orderId: number, sale: LiveStreamBidSale): void {
    const show = this.activeShow();
    if (!show) return;
    const costRmb = sale.cost ?? 0;
    const costUsd = costRmb * (sale.exchangeRateRmbToUsd ?? 0.15);
    const quantity = sale.soldQuantity;
    this.activeShow.set({
      ...show,
      soldCount: show.soldCount + 1,
      inventorySold: show.inventorySold + quantity,
      totalCostRmb: show.totalCostRmb + costRmb,
      totalCostUsd: show.totalCostUsd + costUsd,
      actualRevenueUsd: show.actualRevenueUsd + (sale.actualSalePriceUsd ?? 0),
      actualRevenueRmb: show.actualRevenueRmb + (sale.salePriceRmb ?? 0),
      realizedProfitRmb: show.realizedProfitRmb + (sale.profitRmb ?? 0),
      realizedProfitUsd: show.realizedProfitUsd + (sale.profitUsd ?? 0),
      recordedCostQuantity: show.recordedCostQuantity + quantity,
      remainingCostQuantity: Math.max(0, show.remainingCostQuantity - quantity),
      sales: [sale, ...show.sales],
      customers: show.customers.map(customer => {
        if (!customer.orders.some(item => item.id === orderId)) return customer;
        return {
          ...customer,
          recordedQuantity: customer.recordedQuantity + quantity,
          remainingQuantity: Math.max(0, customer.remainingQuantity - quantity),
          totalCostRmb: customer.totalCostRmb + costRmb,
          orders: customer.orders.map(item => item.id !== orderId ? item : {
            ...item,
            scannedQuantity: item.scannedQuantity + quantity,
            recordedQuantity: item.recordedQuantity + quantity,
            remainingQuantity: Math.max(0, item.remainingQuantity - quantity),
            totalCostRmb: item.totalCostRmb + costRmb,
            scannedCosts: [sale, ...item.scannedCosts],
          }),
        };
      }),
    });
  }

  openManualSale(item: LiveStreamBidProduct): void {
    if (!this.activeShow()) {
      this.error.set('Start a show cost session before recording a BID sale.');
      return;
    }
    if (this.activeShow()!.customers.length && !this.selectedShowOrder()) {
      this.error.set('Choose a customer order before recording a BID cost.');
      return;
    }
    this.error.set('');
    this.message.set('');
    this.manualSalePrice = null;
    this.saleProduct.set(item);
  }

  closeManualSale(): void {
    this.saleProduct.set(null);
    this.manualSalePrice = null;
  }

  async confirmManualSale(): Promise<void> {
    const product = this.saleProduct();
    const show = this.activeShow();
    if (!product || !show?.id || this.loading()) return;
    const rawPrice = this.manualSalePrice;
    const price = rawPrice === null || rawPrice === undefined || String(rawPrice).trim() === ''
      ? null : Number(rawPrice);
    if (price !== null && (!Number.isFinite(price) || price < 0)) {
      this.error.set('Actual sale price must be zero or greater.');
      return;
    }

    this.loading.set(true);
    this.error.set('');
    try {
      const sale = await this.api.confirmSale(product.id, price, show.id, this.selectedShowOrderId());
      this.closeManualSale();
      await this.load();
      this.message.set(`Sale recorded for ${product.title}${sale.actualSalePriceUsd === null ? '' : ` at $${sale.actualSalePriceUsd.toFixed(2)}`}.`);
    } catch (error: any) {
      this.error.set(error?.status === 409
        ? (typeof error?.error === 'string' ? error.error : 'This BID product has no available inventory.')
        : 'The BID sale could not be recorded.');
    } finally {
      this.loading.set(false);
    }
  }

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

  openImportToLocal(item: LiveStreamBidProduct): void {
    if (!this.canMove || item.sourceProductId !== null) return;
    this.importTransferInventory = true;
    this.importToLocalProduct.set(item);
  }

  closeImportToLocal(): void { this.importToLocalProduct.set(null); }

  bidWeightOunces(item: LiveStreamBidProduct): number | null {
    if (item.weightOunces !== null) return item.weightOunces;
    return item.weightGrams === null ? null : +(item.weightGrams / 28.349523125).toFixed(2);
  }

  async confirmImportToLocal(): Promise<void> {
    const item = this.importToLocalProduct();
    if (!item || !this.canMove || this.loading()) return;
    this.loading.set(true);
    this.error.set('');
    try {
      const created = await this.api.importToLocal(item.id, this.importTransferInventory);
      this.importToLocalProduct.set(null);
      this.message.set(`${created.localInventoryId} (${created.sku}) created from BID|${item.uuid} with ${created.qty} in Local inventory.`);
      await this.load();
    } catch (error: any) {
      this.error.set(error?.status === 409
        ? 'This BID product is already linked to a Local product. Reload the list.'
        : typeof error?.error === 'string' ? error.error : 'Could not import this BID product to Local inventory.');
    } finally { this.loading.set(false); }
  }

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
      this.selectedDay.set(detail.shows.length ? detail : null);
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
    const file = this.selectedFile;
    const duplicates = this.findDuplicateImportedUuids([{ fileName: file.name, rows: await this.draftRowsFromCsv(file) }]);
    if (duplicates.length) {
      this.requestUuidImportConfirmation(duplicates, () => this.executeCsvImport(file));
      return;
    }
    await this.executeCsvImport(file);
  }

  private async executeCsvImport(file: File): Promise<void> {
    this.loading.set(true);
    this.message.set('');
    this.error.set('');
    this.importConflicts.set([]);
    this.regeneratedUuids.set([]);
    try {
      const result = await this.api.importCsv(file);
      this.message.set(`Import complete: ${result.created} created, ${result.regeneratedUuids.length} duplicate UUID(s) replaced, ${result.skipped} invalid rows skipped, ${result.conflicts.length} SKU conflict(s).`);
      this.importConflicts.set(result.conflicts);
      this.regeneratedUuids.set(result.regeneratedUuids);
      this.selectedFile = null;
      await this.load();
    } catch (error: any) {
      this.error.set(typeof error?.error === 'string' ? error.error : 'Could not import this CSV file.');
    } finally { this.loading.set(false); }
  }

  async printRegeneratedLabel(item: LiveStreamBidRegeneratedUuid): Promise<void> {
    this.loading.set(true); this.error.set('');
    try { this.message.set((await this.labelPrinting.printBidProduct(item.productId, 1)).message); }
    catch (error: any) { this.error.set(error?.error?.message || error?.error || 'Could not print the replacement label.'); }
    finally { this.loading.set(false); }
  }

  async openCreateList(): Promise<void> {
    this.loading.set(true); this.error.set('');
    try {
      const products = await this.inventoryApi.products();
      this.localInventoryProducts.set(products.flatMap(product => [product, ...product.children]));
      this.draftRows.set([this.blankRow()]); this.draftProductId = null; this.bulkCsvFiles.set([]); this.bulkCsvResults.set([]); this.draftExternalFile = null; this.createOpen.set(true);
      this.editingProduct.set(null); this.targetBatch.set(null);
    } catch { this.error.set('Could not load local products.'); }
    finally { this.loading.set(false); }
  }
  closeCreateList(): void { this.createOpen.set(false); this.editingProduct.set(null); this.targetBatch.set(null); this.draftExternalFile = null; }
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
      sourceProductId: item.sourceProductId, externalUuid: item.uuid, actualSku: item.actualSku, title: item.title, quantity,
      expectedInventory: item.inventory, actualInventory: item.actualInventory,
      unitPrice: price, weightGrams: item.weightGrams, weightOunces: item.weightOunces,
      material: item.material, totalCost: item.totalCost, whatnotAuctionSafePrice: item.whatnotAuctionSafePrice, priceType: this.inferPriceType(price, quantity, grams, total)
    }]); this.createOpen.set(true);
  }
  addBlankRow(): void { this.draftRows.update(rows => [...rows, this.blankRow()]); }
  addWarehouseProduct(): void {
    const product = this.localInventoryProducts().find(item => item.id === this.draftProductId); if (!product) return;
    this.draftRows.update(rows => [...rows.filter(row => row.title.trim()), {
      sourceProductId: product.id, externalUuid: null, actualSku: product.sku, title: product.productName, quantity: 1,
      expectedInventory: Math.max(1, product.qty), actualInventory: Math.max(1, product.qty),
      unitPrice: product.cost ?? 0, weightGrams: product.weight, weightOunces: product.weight == null ? null : +(product.weight / 28.349523125).toFixed(4),
      material: null, totalCost: product.cost == null ? null : +(product.cost + (product.weight ?? 0) * .05).toFixed(2), whatnotAuctionSafePrice: null, priceType: 'per-package'
    }]); this.draftProductId = null;
  }
  removeDraftRow(index: number): void { this.draftRows.update(rows => rows.filter((_, i) => i !== index)); }
  updateOunces(row: LiveStreamBidDraftRow): void { row.weightOunces = row.weightGrams == null ? null : +(Number(row.weightGrams) / 28.349523125).toFixed(4); }
  async loadDraftCsv(event: Event): Promise<void> {
    const file = (event.target as HTMLInputElement).files?.[0]; if (!file) return;
    this.draftRows.set(await this.draftRowsFromCsv(file));
    this.draftExternalFile = { fileName: file.name, sha256: await this.sha256(file) };
  }
  selectBulkCsvFiles(event: Event): void {
    this.setBulkCsvFiles(Array.from((event.target as HTMLInputElement).files ?? []));
  }
  dropBulkCsvFiles(event: DragEvent): void {
    event.preventDefault();
    this.setBulkCsvFiles(Array.from(event.dataTransfer?.files ?? []));
  }
  removeBulkCsvFile(index: number): void {
    this.bulkCsvFiles.update(files => files.filter((_, fileIndex) => fileIndex !== index));
  }
  private setBulkCsvFiles(files: File[]): void {
    const csvFiles = files.filter(file => file.name.toLowerCase().endsWith('.csv'));
    const unique = new Map(this.bulkCsvFiles().map(file => [`${file.name}|${file.size}|${file.lastModified}`, file]));
    for (const file of csvFiles) unique.set(`${file.name}|${file.size}|${file.lastModified}`, file);
    this.bulkCsvFiles.set([...unique.values()]);
    this.bulkCsvResults.set([]);
    this.error.set(csvFiles.length === files.length ? '' : 'Only CSV files were added to the batch import queue.');
  }
  async importBulkConvertedCsvs(): Promise<void> {
    const files = this.bulkCsvFiles();
    if (!files.length || this.loading()) return;
    const prepared = await Promise.all(files.map(async file => ({ file, rows: (await this.draftRowsFromCsv(file)).filter(row => row.title.trim()) })));
    const duplicates = this.findDuplicateImportedUuids(prepared.map(item => ({ fileName: item.file.name, rows: item.rows })));
    if (duplicates.length) {
      this.requestUuidImportConfirmation(duplicates, () => this.executeBulkConvertedCsvs(prepared));
      return;
    }
    await this.executeBulkConvertedCsvs(prepared);
  }

  private async executeBulkConvertedCsvs(prepared: { file: File; rows: LiveStreamBidDraftRow[] }[]): Promise<void> {
    const files = prepared.map(item => item.file);
    this.loading.set(true); this.error.set(''); this.message.set(''); this.bulkCsvResults.set([]);
    const results: BulkCsvImportResult[] = [];
    const regenerated: LiveStreamBidRegeneratedUuid[] = [];
    for (const { file, rows } of prepared) {
      try {
        if (!rows.length) throw new Error('No valid product rows were found.');
        const result = await this.api.createBatch(rows, { fileName: file.name, sha256: await this.sha256(file) });
        regenerated.push(...result.regeneratedUuids);
        results.push({ fileName: file.name, batchNumber: result.batchNumber, products: result.products.length, error: null });
      } catch (error: any) {
        const detail = typeof error?.error === 'string' ? error.error : error?.message || 'Import failed.';
        results.push({ fileName: file.name, batchNumber: null, products: 0, error: detail });
      }
      this.bulkCsvResults.set([...results]);
    }
    this.regeneratedUuids.set(regenerated);
    const succeeded = results.filter(result => result.batchNumber).length;
    this.message.set(`Bulk import complete: ${succeeded}/${files.length} CSV files created as separate batches.`);
    this.bulkCsvFiles.set([]);
    this.createOpen.set(false);
    await this.load();
    this.loading.set(false);
  }
  private async draftRowsFromCsv(file: File): Promise<LiveStreamBidDraftRow[]> {
    const records = this.parseCsv(await file.text()); if (records.length < 2) return [];
    const headings = records[0].map(value => value.trim().toLowerCase());
    const at = (row: string[], name: string) => row[headings.indexOf(name)] ?? '';
    return records.slice(1).filter(row => row.some(Boolean)).map(row => ({
      sourceProductId: null, externalUuid: this.normalizeImportedUuid(at(row, 'uuid')), actualSku: at(row, 'actual sku') || null, title: at(row, 'title'), quantity: +(at(row, 'quantity') || 1),
      expectedInventory: +(at(row, 'expected inventory') || 0), actualInventory: +(at(row, 'inventory') || 0),
      unitPrice: +(at(row, 'unit price') || 0), weightGrams: at(row, 'weight (g)') === '' ? null : +at(row, 'weight (g)'),
      weightOunces: at(row, 'weight (oz)') === '' ? null : +at(row, 'weight (oz)'), material: at(row, 'material') || null,
      totalCost: at(row, 'total cost') === '' ? null : +at(row, 'total cost'),
      whatnotAuctionSafePrice: at(row, 'whatnot auction safe price') === '' ? null : +at(row, 'whatnot auction safe price')
      , priceType: this.inferPriceType(+(at(row, 'unit price') || 0), +(at(row, 'quantity') || 1), +(at(row, 'weight (g)') || 0), +(at(row, 'total cost') || 0))
    }));
  }
  async loadCloudCsvFiles(): Promise<void> {
    if (!this.cloudToken.trim() || this.loading()) return;
    this.loading.set(true); this.error.set('');
    try {
      this.cloudCsvFiles.set(await this.api.cloudFiles(this.cloudToken.trim()));
      await this.secretVault.save('cloud-csv-token', this.cloudToken.trim());
      this.cloudTokenRequired.set(false);
    }
    catch (error: any) {
      if (error?.status === 401) { await this.secretVault.clear('cloud-csv-token'); this.cloudToken = ''; this.cloudTokenRequired.set(true); }
      this.error.set(error?.status === 401 ? 'The cloud token expired or is invalid. Enter it again.' : 'Could not read the cloud CSV inbox. Check the server connection.');
    }
    finally { this.loading.set(false); }
  }
  async importCloudCsv(item: CloudCsvFile): Promise<void> {
    if (item.alreadyImported || this.loading()) return;
    this.loading.set(true); this.error.set(''); this.message.set('');
    this.cloudImportError.set(null);
    let stage = 'download';
    try {
      const blob = await this.api.downloadCloudFile(this.cloudToken.trim(), item.remotePath);
      stage = 'read';
      const file = new File([blob], item.fileName, { type: 'text/csv' });
      const rows = (await this.draftRowsFromCsv(file)).filter(row => row.title.trim());
      if (!rows.length) throw new Error('No valid product rows were found.');
      stage = 'create batch';
      const result = await this.api.createBatch(rows, { fileName: file.name, sha256: await this.sha256(file) });
      this.message.set(`Cloud batch ${result.batchNumber} imported with ${result.products.length} products.`);
      this.cloudCsvFiles.update(files => files.map(file => file.remotePath === item.remotePath ? { ...file, alreadyImported: true } : file));
      stage = 'refresh';
      await this.load();
      this.cloudCsvFiles.set(await this.api.cloudFiles(this.cloudToken.trim()));
    } catch (error: any) {
      if (error?.status === 401) { await this.secretVault.clear('cloud-csv-token'); this.cloudToken = ''; this.cloudTokenRequired.set(true); }
      const detail = error?.status === 409 ? 'This cloud CSV name or SHA-256 was already imported.' : (typeof error?.error === 'string' ? error.error : error?.message || 'Cloud CSV import failed.');
      const message = stage === 'refresh' ? `Batch created, but list refresh failed: ${detail}` : `Could not ${stage}: ${detail}`;
      this.error.set(message);
      this.cloudImportError.set({ path: item.remotePath, message });
    } finally { this.loading.set(false); }
  }
  private async sha256(file: File): Promise<string> {
    const digest = await crypto.subtle.digest('SHA-256', await file.arrayBuffer());
    return [...new Uint8Array(digest)].map(value => value.toString(16).padStart(2, '0')).join('');
  }
  private findDuplicateImportedUuids(groups: { fileName: string; rows: LiveStreamBidDraftRow[] }[]): UuidImportPreview[] {
    const used = new Set(this.products().map(product => this.normalizeImportedUuid(product.uuid)?.toLowerCase()).filter((uuid): uuid is string => !!uuid));
    const duplicates: UuidImportPreview[] = [];
    for (const group of groups) {
      group.rows.forEach((row, index) => {
        const normalized = this.normalizeImportedUuid(row.externalUuid ?? '');
        const uuid = normalized?.toLowerCase();
        if (!uuid) return;
        if (used.has(uuid)) duplicates.push({ fileName: group.fileName, rowNumber: index + 2, title: row.title, originalUuid: normalized! });
        else used.add(uuid);
      });
    }
    return duplicates;
  }

  private requestUuidImportConfirmation(preview: UuidImportPreview[], action: () => Promise<void>): void {
    this.pendingUuidImportAction = action;
    this.uuidImportConfirmation.set(preview);
    this.error.set('');
  }

  cancelUuidImport(): void {
    this.pendingUuidImportAction = null;
    this.uuidImportConfirmation.set([]);
    this.message.set('Import abandoned. No products or batches were created.');
  }

  async confirmUuidImport(): Promise<void> {
    const action = this.pendingUuidImportAction;
    if (!action || this.loading()) return;
    try { await action(); }
    finally {
      this.pendingUuidImportAction = null;
      this.uuidImportConfirmation.set([]);
    }
  }
  async saveBatch(): Promise<void> {
    const rows = this.draftRows().filter(row => row.title.trim()); if (!rows.length) { this.error.set('Add at least one product.'); return; }
    const duplicates = this.editingProduct() ? [] : this.findDuplicateImportedUuids([{ fileName: 'Create BID List', rows }]);
    if (duplicates.length) {
      this.requestUuidImportConfirmation(duplicates, () => this.executeSaveBatch(rows));
      return;
    }
    await this.executeSaveBatch(rows);
  }

  private async executeSaveBatch(rows: LiveStreamBidDraftRow[]): Promise<void> {
    this.loading.set(true); this.error.set('');
    try {
      const editing = this.editingProduct();
      if (editing) { await this.api.updateProduct(editing.id, rows[0]); this.message.set(`BID|${editing.uuid} updated.`); }
      else if (this.targetBatch()) { const result = await this.api.addBatchProducts(this.targetBatch()!, rows); this.regeneratedUuids.set(result.regeneratedUuids); this.message.set(`${result.products.length} products added to ${result.batchNumber}; ${result.regeneratedUuids.length} duplicate UUID(s) replaced.`); }
      else { const result = await this.api.createBatch(rows, this.draftExternalFile ?? undefined); this.regeneratedUuids.set(result.regeneratedUuids); this.message.set(`Batch ${result.batchNumber} created with ${result.products.length} products; ${result.regeneratedUuids.length} duplicate UUID(s) replaced.`); }
      this.createOpen.set(false); this.editingProduct.set(null); this.targetBatch.set(null); this.draftExternalFile = null; await this.load();
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
  async deleteBatch(batchNumber: string, productCount: number): Promise<void> {
    if (!confirm(`Delete batch ${batchNumber} and all ${productCount} product records in it?\n\nThis cannot be undone. Batches with sales history cannot be deleted.`)) return;
    this.loading.set(true); this.error.set(''); this.message.set('');
    try {
      await this.api.deleteBatch(batchNumber);
      if (this.expandedBatch() === batchNumber) this.expandedBatch.set(null);
      this.message.set(`Batch ${batchNumber} and its ${productCount} products were deleted.`);
      await this.load();
    } catch (error: any) {
      this.error.set(error?.status === 409 ? 'This batch has sales history and cannot be deleted.' : 'Could not delete this batch.');
    } finally { this.loading.set(false); }
  }
  recalculate(row: LiveStreamBidDraftRow): void {
    const price = Number(row.unitPrice), quantity = Number(row.quantity), grams = Number(row.weightGrams ?? 0);
    const merchandise = row.priceType === 'per-package' ? price : price * quantity;
    row.totalCost = Number.isFinite(merchandise) ? +(merchandise + grams * .05).toFixed(2) : null;
    this.updateOunces(row);
  }
  itemCodeForPrice(price: number | null): string {
    let value = Math.round(Number(price));
    if (!Number.isFinite(value) || value < 1) return '—';
    let code = '';
    while (value > 0) { value--; code = String.fromCharCode(65 + value % 26) + code; value = Math.floor(value / 26); }
    return code;
  }
  private blankRow(): LiveStreamBidDraftRow { return { sourceProductId: null, externalUuid: null, actualSku: null, title: '', quantity: 1, expectedInventory: 1, actualInventory: 1, unitPrice: 0, weightGrams: null, weightOunces: null, material: null, totalCost: 0, whatnotAuctionSafePrice: null, priceType: 'per-package' }; }
  private normalizeImportedUuid(value: string): string | null { const normalized = value.trim().replace(/^BID\s*\|\s*/i, '').trim(); return normalized || null; }
  private inferPriceType(price: number, quantity: number, grams: number, total: number): 'per-unit' | 'per-package' { return Math.abs(total - (price * quantity + grams * .05)) < Math.abs(total - (price + grams * .05)) ? 'per-unit' : 'per-package'; }
  private parseCsv(text: string): string[][] {
    const rows: string[][] = []; let row: string[] = [], value = '', quoted = false;
    for (let i = 0; i < text.length; i++) { const char = text[i]; if (char === '"') { if (quoted && text[i + 1] === '"') { value += '"'; i++; } else quoted = !quoted; } else if (char === ',' && !quoted) { row.push(value); value = ''; } else if ((char === '\n' || char === '\r') && !quoted) { if (char === '\r' && text[i + 1] === '\n') i++; row.push(value); rows.push(row); row = []; value = ''; } else value += char; }
    if (value || row.length) { row.push(value); rows.push(row); } return rows;
  }
}
