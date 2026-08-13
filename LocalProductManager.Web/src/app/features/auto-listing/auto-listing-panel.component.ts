import { CommonModule } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AutoListingApiService, AutoListingPlatform, EbayListingDraft } from '../../core/auto-listing-api.service';
import { ProductApiService } from '../../core/product-api.service';
import { ProductListItem } from '../../core/models';

@Component({
  selector: 'app-auto-listing-panel',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './auto-listing-panel.component.html',
  styleUrl: './auto-listing-panel.component.css',
})
export class AutoListingPanelComponent {
  private readonly api = inject(AutoListingApiService);
  readonly productApi = inject(ProductApiService);
  readonly platforms = signal<AutoListingPlatform[]>([]);
  readonly products = signal<ProductListItem[]>([]);
  readonly draft = signal<EbayListingDraft | null>(null);
  readonly loading = signal(false);
  readonly message = signal('');
  selectedProductId: number | null = null;

  constructor() { void this.load(); }

  async load(): Promise<void> {
    this.loading.set(true);
    try {
      const [platforms, products] = await Promise.all([this.api.platforms(), this.productApi.search()]);
      this.platforms.set(platforms);
      this.products.set(products);
    } catch { this.message.set('Unable to load Auto Listing.'); }
    finally { this.loading.set(false); }
  }

  platform(id: AutoListingPlatform['platform']): AutoListingPlatform | undefined {
    return this.platforms().find(item => item.platform === id);
  }

  async generateDraft(): Promise<void> {
    if (!this.selectedProductId) return;
    this.loading.set(true); this.message.set('');
    try { this.draft.set(await this.api.ebayDraft(this.selectedProductId)); }
    catch { this.message.set('Unable to generate the eBay draft.'); }
    finally { this.loading.set(false); }
  }
}
