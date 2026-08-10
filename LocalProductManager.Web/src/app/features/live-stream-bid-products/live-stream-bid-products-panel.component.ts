import { CommonModule } from '@angular/common';
import { Component, Input, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { LiveStreamBidDailySummary, LiveStreamBidProduct, LiveStreamBidProductApiService } from '../../core/live-stream-bid-product-api.service';
import { InventoryApiService, ProductScannerLocation } from '../../core/inventory-api.service';
import { ProductListItem } from '../../core/models';

@Component({
  selector: 'app-live-stream-bid-products-panel',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './live-stream-bid-products-panel.component.html',
  styleUrl: './live-stream-bid-products-panel.component.css',
})
export class LiveStreamBidProductsPanelComponent implements OnInit {
  @Input() canImport = false;
  @Input() isAdmin = false;
  @Input() canMove = false;
  private readonly api = inject(LiveStreamBidProductApiService);
  private readonly inventoryApi = inject(InventoryApiService);
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

  readonly filteredProducts = computed(() => {
    const keyword = this.search().trim().toLowerCase();
    if (!keyword) return this.products();
    return this.products().filter(item =>
      item.uuid.toLowerCase().includes(keyword) ||
      item.title.toLowerCase().includes(keyword) ||
      (item.material ?? '').toLowerCase().includes(keyword));
  });
  readonly totalQuantity = computed(() => this.products().reduce((sum, item) => sum + item.inventory, 0));
  readonly totalCost = computed(() => this.products().reduce((sum, item) => sum + (item.totalCost ?? 0), 0));

  currencySymbol(): string { return this.displayCurrency() === 'usd' ? '$' : '¥'; }
  summaryCost(summary: LiveStreamBidDailySummary): number { return this.displayCurrency() === 'usd' ? summary.totalCostUsd : summary.totalCostRmb; }
  summaryRevenue(summary: LiveStreamBidDailySummary): number { return this.displayCurrency() === 'usd' ? summary.actualRevenueUsd : summary.actualRevenueRmb; }
  summaryProfit(summary: LiveStreamBidDailySummary): number { return this.displayCurrency() === 'usd' ? summary.realizedProfitUsd : summary.realizedProfitRmb; }

  ngOnInit(): void { void this.load(); }

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
}
