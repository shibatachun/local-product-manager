import { CommonModule } from '@angular/common';
import { Component, Input, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DomesticProduct, DomesticProductApiService } from '../../core/domestic-product-api.service';

@Component({
  selector: 'app-domestic-products-panel',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './domestic-products-panel.component.html',
  styleUrl: './domestic-products-panel.component.css',
})
export class DomesticProductsPanelComponent implements OnInit {
  @Input() canImport = false;
  private readonly api = inject(DomesticProductApiService);
  readonly products = signal<DomesticProduct[]>([]);
  readonly total = signal(0);
  readonly page = signal(1);
  readonly pageSize = 100;
  readonly query = signal('');
  readonly skuSort = signal<'asc' | 'desc'>('asc');
  readonly loading = signal(false);
  readonly message = signal('');
  readonly error = signal('');
  readonly expandedId = signal<number | null>(null);
  selectedFile: File | null = null;
  private searchTimer: ReturnType<typeof setTimeout> | null = null;
  readonly pageCount = computed(() => Math.max(1, Math.ceil(this.total() / this.pageSize)));

  ngOnInit(): void { void this.load(); }

  onSearch(value: string): void {
    this.query.set(value); this.page.set(1);
    if (this.searchTimer) clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => void this.load(), 250);
  }

  toggleSkuSort(): void {
    this.skuSort.update(value => value === 'asc' ? 'desc' : 'asc');
    this.page.set(1); void this.load();
  }

  async changePage(delta: number): Promise<void> {
    const next = Math.min(this.pageCount(), Math.max(1, this.page() + delta));
    if (next === this.page()) return;
    this.page.set(next); await this.load();
  }

  selectFile(event: Event): void {
    this.selectedFile = (event.target as HTMLInputElement).files?.[0] ?? null;
    this.message.set(''); this.error.set('');
  }

  async importXlsx(): Promise<void> {
    if (!this.selectedFile || this.loading()) return;
    this.loading.set(true); this.error.set(''); this.message.set('');
    try {
      const result = await this.api.import(this.selectedFile);
      this.message.set(`Import complete: ${result.created} created, ${result.updated} updated, ${result.skipped} skipped.`);
      this.selectedFile = null; this.page.set(1); await this.load(false);
    } catch (error: any) {
      this.error.set(typeof error?.error === 'string' ? error.error : 'Could not import this XLSX file.');
    } finally { this.loading.set(false); }
  }

  toggleDetails(item: DomesticProduct): void { this.expandedId.update(id => id === item.id ? null : item.id); }
  detailEntries(item: DomesticProduct): { key: string; value: string }[] {
    return Object.entries(item.details).map(([key, value]) => ({ key, value }));
  }

  private async load(showSpinner = true): Promise<void> {
    if (showSpinner) this.loading.set(true);
    this.error.set('');
    try {
      const result = await this.api.search(this.query().trim(), this.skuSort(), this.page(), this.pageSize);
      this.products.set(result.items); this.total.set(result.total);
      if (this.page() > this.pageCount()) { this.page.set(this.pageCount()); await this.load(false); }
    } catch { this.error.set('Could not load domestic product information.'); }
    finally { if (showSpinner) this.loading.set(false); }
  }
}
