import { CommonModule } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AmazonAsin, AttributionPlatform, Platform, ProductDetail, ProductListing } from '../../core/models';
import { ProductApiService } from '../../core/product-api.service';

@Component({
  selector: 'app-test-page',
  imports: [CommonModule, FormsModule, RouterLink],
  templateUrl: './test.page.html',
  styleUrl: './test.page.css',
})
export class TestPage {
  private readonly api = inject(ProductApiService);
  readonly products = signal<ProductDetail[]>([]);
  readonly selectedProduct = signal<ProductDetail | null>(null);
  readonly loading = signal(false);
  readonly logs = signal<string[]>([]);
  readonly matchedImages = signal<string[]>([]);
  readonly apiBase = this.api.baseUrl;

  readonly productForm = { sku: `SKU-${Date.now()}`, productName: 'API Test Product', cost: 12.5, imagePath: 'D:\\Products\\123456-main.jpg' };
  readonly listingForm = { platform: 'amazon' as Platform, listingUrl: 'https://www.amazon.com/dp/B0TEST1234' };
  readonly asinForm = { asin: 'B0TEST1234', asinType: 'child' as 'parent' | 'child', parentAsin: 'B0PARENT123', amazonAttributeDone: false };
  readonly attributionForm = { platform: 'pinterest' as AttributionPlatform, attributionUrl: 'https://www.amazon.com/attribution/test' };
  readonly imageForm = { sku: '123456-BLACK', folderPath: 'D:\\Products' };
  readonly selectedTitle = computed(() => this.selectedProduct()?.sku ?? 'No product selected');

  async ngOnInit(): Promise<void> { await this.refresh(); }

  async refresh(): Promise<void> {
    await this.run('GET products', async () => {
      const summaries = await this.api.search();
      this.products.set(await Promise.all(summaries.map((item) => this.api.getProduct(item.id))));
    });
  }

  async create(): Promise<void> {
    await this.run('POST product', async () => {
      this.selectedProduct.set(await this.api.createProduct(this.productForm));
      await this.refresh();
    });
  }

  async select(id: number): Promise<void> {
    await this.run(`GET product ${id}`, async () => this.selectedProduct.set(await this.api.getProduct(id)));
  }

  async removeProduct(): Promise<void> {
    const product = this.selectedProduct();
    if (!product) return;
    await this.run('DELETE product', async () => {
      await this.api.deleteProduct(product.id);
      this.selectedProduct.set(null);
      await this.refresh();
    });
  }

  async saveListing(): Promise<void> {
    const product = this.selectedProduct();
    if (!product) return this.log('Select a product first.');
    await this.run('POST listing', async () => {
      await this.api.saveListing(product.id, this.listingForm);
      await this.select(product.id);
    });
  }

  async removeListing(item: ProductListing): Promise<void> {
    await this.run('DELETE listing', async () => {
      await this.api.deleteListing(item.productId, item.id);
      await this.select(item.productId);
    });
  }

  async saveAsin(): Promise<void> {
    const product = this.selectedProduct();
    if (!product) return this.log('Select a product first.');
    await this.run('POST ASIN', async () => {
      await this.api.createAsin(product.id, this.asinForm);
      await this.select(product.id);
    });
  }

  async removeAsin(item: AmazonAsin): Promise<void> {
    await this.run('DELETE ASIN', async () => {
      await this.api.deleteAsin(item.productId, item.id);
      await this.select(item.productId);
    });
  }

  async saveAttribution(): Promise<void> {
    const product = this.selectedProduct();
    if (!product) return this.log('Select a product first.');
    await this.run('PUT attribution', async () => {
      await this.api.saveAttribution(product.id, this.attributionForm.platform, this.attributionForm.attributionUrl);
      await this.select(product.id);
    });
  }

  async matchImages(): Promise<void> {
    await this.run('POST image match', async () => this.matchedImages.set((await this.api.matchImages(this.imageForm.sku, this.imageForm.folderPath)).matchedImages));
  }

  private async run(label: string, action: () => Promise<void>): Promise<void> {
    this.loading.set(true);
    try {
      await action();
      this.log(`${label} OK`);
    } catch (error) {
      this.log(`${label} FAILED: ${error instanceof Error ? error.message : String(error)}`);
    } finally {
      this.loading.set(false);
    }
  }

  private log(message: string): void {
    this.logs.update((items) => [`${new Date().toLocaleTimeString()} ${message}`, ...items].slice(0, 12));
  }
}
