import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import {
  AmazonAsin,
  AttributionPlatform,
  Platform,
  ProductDetail,
  ProductListItem,
  ProductListing,
  ImageStorageRoot,
  StoreLink,
} from '../../core/models';
import { ProductApiService } from '../../core/product-api.service';

type ViewMode = 'products' | 'pricing' | 'marketing' | 'storage';

interface PricingStep {
  label: string;
  value: string;
  note: string;
}

interface PlatformChoice {
  platform: Platform;
  label: string;
  selected: boolean;
  url: string;
}

@Component({
  selector: 'app-product-manager',
  imports: [CommonModule, FormsModule, RouterLink],
  templateUrl: './product-manager.page.html',
  styleUrl: './product-manager.page.css',
})
export class ProductManagerPage {
  readonly api = inject(ProductApiService);

  readonly products = signal<ProductListItem[]>([]);
  readonly marketingProducts = signal<ProductListItem[]>([]);
  readonly selectedProduct = signal<ProductDetail | null>(null);
  readonly search = signal('');
  readonly marketingSearch = signal('');
  readonly platformFilter = signal('');
  readonly view = signal<ViewMode>('products');
  readonly loading = signal(false);
  readonly wizardOpen = signal(false);
  readonly importOpen = signal(false);
  readonly detailOpen = signal(false);
  readonly wizardStep = signal(1);
  readonly notice = signal('');
  readonly matchedImages = signal<string[]>([]);
  readonly detailMatchedImages = signal<string[]>([]);
  readonly importFile = signal<File | null>(null);
  readonly businessReportFile = signal<File | null>(null);
  readonly bulkAttributionFile = signal<File | null>(null);
  readonly attributionFolderFiles = signal<File[]>([]);
  readonly attributionFolderSummary = signal('');
  readonly unmatchedAttributionFiles = signal<string[]>([]);
  readonly expandedParents = signal<Set<number>>(new Set());
  readonly imageStorageRoots = signal<ImageStorageRoot[]>([]);
  readonly storeLinks = signal<StoreLink[]>([]);
  readonly newImageRoot = signal('');

  readonly platformOptions: { value: Platform | ''; label: string }[] = [
    { value: '', label: 'All platforms' },
    { value: 'amazon', label: 'Amazon' },
    { value: 'walmart', label: 'Walmart' },
    { value: 'ebay', label: 'eBay' },
    { value: 'etsy', label: 'Etsy' },
  ];
  readonly listingOptions: { value: Platform; label: string }[] = [
    { value: 'amazon', label: 'Amazon' },
    { value: 'walmart', label: 'Walmart' },
    { value: 'ebay', label: 'eBay' },
    { value: 'etsy', label: 'Etsy' },
  ];
  readonly attributionPlatforms: AttributionPlatform[] = ['pinterest', 'facebook', 'instagram', 'tiktok', 'youtube', 'shopify'];

  readonly createForm = {
    sku: '',
    productName: '',
    cost: null as number | null,
    weight: null as number | null,
    length: null as number | null,
    width: null as number | null,
    height: null as number | null,
    imagePath: '',
    imageFolder: '',
    imageMatchMode: 'folder' as 'folder' | 'sku',
  };
  readonly platformChoices: PlatformChoice[] = [
    { platform: 'amazon', label: 'Amazon', selected: false, url: '' },
    { platform: 'walmart', label: 'Walmart', selected: false, url: '' },
    { platform: 'ebay', label: 'eBay', selected: false, url: '' },
    { platform: 'etsy', label: 'Etsy', selected: false, url: '' },
  ];
  readonly createAsin = {
    asin: '',
    asinType: 'child' as 'parent' | 'child',
    parentAsin: '',
    amazonAttributeDone: false,
  };
  readonly editForm = {
    sku: '',
    productName: '',
    cost: null as number | null,
    weight: null as number | null,
    length: null as number | null,
    width: null as number | null,
    height: null as number | null,
    imagePath: ''
  };
  readonly storeLinkDrafts: Record<Platform, string> = { amazon: '', walmart: '', ebay: '', etsy: '' };
  readonly listingDrafts: Record<Platform, string> = { amazon: '', walmart: '', ebay: '', etsy: '' };
  readonly asinDraft = { asin: '', asinType: 'child' as 'parent' | 'child', parentAsin: '', amazonAttributeDone: false };
  readonly pricingForm = {
    platform: 'walmart' as Platform,
    logistics: 'usps',
    productCost: 0,
    weight: 1,
    length: 6,
    width: 4,
    height: 2,
    packagingCost: 0.65,
    targetMarginPercent: 20,
    bufferPercent: 5,
  };
  readonly pricingPlatformOptions = [
    { value: 'walmart' as Platform, label: 'Walmart', disabled: false },
    { value: 'amazon' as Platform, label: 'Amazon', disabled: true },
    { value: 'ebay' as Platform, label: 'eBay', disabled: true },
    { value: 'etsy' as Platform, label: 'Etsy', disabled: true },
  ];
  readonly pricingEstimate = computed(() => this.calculatePricingEstimate());

  readonly stats = computed(() => {
    const products = this.products();
    const allProducts = products.flatMap((product) => [product, ...product.children]);
    return {
      total: allProducts.length,
      listed: allProducts.filter((item) => item.platforms.length > 0).length,
      withImages: allProducts.filter((item) => item.imagePath).length,
      channels: new Set(allProducts.flatMap((item) => item.platforms)).size,
    };
  });
  readonly amazonProducts = computed(() => {
    const keyword = this.marketingSearch().trim().toLowerCase();
    const products = this.marketingProducts()
      .flatMap((product) => [product, ...product.children])
      .filter((product) => this.hasAmazon(product));
    if (!keyword) return products;

    return products.filter((product) => this.matchesMarketingSearch(product, keyword));
  });

  async ngOnInit(): Promise<void> {
    await Promise.all([this.loadProducts(), this.loadImageStorageRoots(), this.loadStoreLinks()]);
  }

  async loadStoreLinks(): Promise<void> {
    try {
      const links = await this.api.storeLinks();
      this.storeLinks.set(links);
      for (const link of links) this.storeLinkDrafts[link.platform] = link.url ?? '';
    } catch {
      this.storeLinks.set([]);
    }
  }

  async saveStoreLink(platform: Platform): Promise<void> {
    await this.perform(async () => {
      await this.api.saveStoreLink(platform, this.storeLinkDrafts[platform] || null);
      await this.loadStoreLinks();
      this.showNotice(`${this.platformLabel(platform)} store link saved.`);
    });
  }

  storeLink(platform: Platform): StoreLink | undefined {
    return this.storeLinks().find(link => link.platform === platform);
  }

  async loadImageStorageRoots(): Promise<void> {
    try {
      this.imageStorageRoots.set(await this.api.imageStorageRoots());
    } catch {
      this.imageStorageRoots.set([]);
    }
  }

  selectImageRoot(path: string): void {
    if (path) this.createForm.imageFolder = path;
  }

  async loadProducts(): Promise<void> {
    await this.perform(async () => {
      this.products.set(await this.api.search(this.search(), this.platformFilter()));
    }, false);
  }

  async loadMarketingProducts(): Promise<void> {
    await this.perform(async () => {
      this.marketingProducts.set(await this.api.search('', '', true));
    }, false);
  }

  changeView(view: ViewMode): void {
    this.view.set(view);
    this.detailOpen.set(false);
    if (view === 'marketing') void this.loadMarketingProducts();
  }

  pageEyebrow(): string {
    const labels: Record<ViewMode, string> = {
      products: 'Catalog',
      pricing: 'Cost planning',
      marketing: 'Amazon Attribution',
      storage: 'Server settings',
    };
    return labels[this.view()];
  }

  pageTitle(): string {
    const labels: Record<ViewMode, string> = {
      products: 'Product management',
      pricing: 'Pricing estimator',
      marketing: 'Marketing links',
      storage: 'Image storage',
    };
    return labels[this.view()];
  }

  async selectMarketingProduct(productId: number): Promise<void> {
    await this.perform(async () => {
      this.selectedProduct.set(await this.api.getProduct(productId));
      this.detailOpen.set(false);
    }, false);
  }

  async addImageRoot(): Promise<void> {
    const path = this.newImageRoot().trim();
    if (!path) return this.showNotice('Enter a local or UNC folder path.');

    await this.perform(async () => {
      const root = await this.api.addImageStorageRoot(path);
      this.newImageRoot.set('');
      await this.loadImageStorageRoots();
      this.showNotice(root.available
        ? 'Image folder added and is accessible.'
        : 'Image folder added, but the server cannot currently access it.');
    });
  }

  async removeImageRoot(root: ImageStorageRoot): Promise<void> {
    if (!confirm(`Remove this image folder from the whitelist?\n${root.path}`)) return;
    await this.perform(async () => {
      await this.api.deleteImageStorageRoot(root.path);
      await this.loadImageStorageRoots();
      if (this.createForm.imageFolder === root.path) this.createForm.imageFolder = '';
      this.showNotice('Image folder removed from the whitelist.');
    });
  }

  openWizard(): void {
    this.resetWizard();
    this.wizardOpen.set(true);
  }

  openImport(): void {
    this.businessReportFile.set(null);
    this.bulkAttributionFile.set(null);
    this.importOpen.set(true);
  }

  closeImport(): void {
    this.importOpen.set(false);
  }

  onBusinessReportSelected(event: Event): void {
    this.businessReportFile.set((event.target as HTMLInputElement).files?.[0] ?? null);
  }

  onBulkAttributionSelected(event: Event): void {
    this.bulkAttributionFile.set((event.target as HTMLInputElement).files?.[0] ?? null);
  }

  async importBusinessReport(): Promise<void> {
    const file = this.businessReportFile();
    if (!file) return this.showNotice('Select an Amazon Business Report CSV first.');

    await this.perform(async () => {
      const result = await this.api.importBusinessReport(file);
      this.importOpen.set(false);
      this.expandedParents.set(new Set());
      await this.loadProducts();
      await this.loadMarketingProducts();
      this.showNotice(
        `Imported ${result.rowsRead} rows: ${result.variationGroups} variations, ` +
        `${result.childProductsCreated + result.standaloneProductsCreated} products created.`,
      );
    });
  }

  async importBulkAttributions(): Promise<void> {
    const file = this.bulkAttributionFile();
    if (!file) return this.showNotice('Select an Amazon Attribution CSV first.');

    await this.perform(async () => {
      const result = await this.api.importBulkAttributions(file);
      this.importOpen.set(false);
      await this.loadProducts();
      await this.loadMarketingProducts();
      const unmatched = result.unmatchedSkus.length
        ? ` ${result.unmatchedSkus.length} SKU(s) were not found.`
        : '';
      this.showNotice(`Imported ${result.linksImported} attribution links.${unmatched}`);
    });
  }

  onAttributionFolderSelected(event: Event): void {
    const files = Array.from((event.target as HTMLInputElement).files ?? [])
      .filter((file) => file.name.toLowerCase().endsWith('.csv'));
    this.attributionFolderFiles.set(files);
    this.attributionFolderSummary.set('');
    this.unmatchedAttributionFiles.set([]);
  }

  async importAttributionFolder(): Promise<void> {
    const files = this.attributionFolderFiles();
    if (!files.length) return this.showNotice('Select a folder containing attribution CSV files.');

    await this.perform(async () => {
      const result = await this.api.importAttributionFolder(files);
      const selectedId = this.selectedProduct()?.id;
      if (selectedId) await this.selectMarketingProduct(selectedId);
      this.attributionFolderFiles.set([]);
      this.attributionFolderSummary.set(
        `${result.filesMatched}/${result.filesRead} files matched, ` +
        `${result.productsMatched} products updated, ${result.linksImported} links imported.`,
      );
      this.unmatchedAttributionFiles.set(result.unmatchedFiles);
      const unmatched = result.unmatchedFiles.length
        ? ` ${result.unmatchedFiles.length} file(s) could not be matched.`
        : '';
      this.showNotice(
        `Matched ${result.filesMatched}/${result.filesRead} files and imported ` +
        `${result.linksImported} links.${unmatched}`,
      );
    });
  }

  toggleParent(productId: number, event?: Event): void {
    event?.stopPropagation();
    this.expandedParents.update((current) => {
      const next = new Set(current);
      if (next.has(productId)) next.delete(productId);
      else next.add(productId);
      return next;
    });
  }

  isExpanded(productId: number): boolean {
    return this.expandedParents().has(productId);
  }

  closeWizard(): void {
    this.wizardOpen.set(false);
  }

  nextWizardStep(): void {
    if (this.wizardStep() === 1 && (!this.createForm.sku.trim() || !this.createForm.productName.trim())) {
      return this.showNotice('SKU and product name are required.');
    }

    if (this.wizardStep() === 2 && this.platformChoices.some((item) => item.platform === 'amazon' && item.selected)) {
      this.wizardStep.set(3);
      return;
    }

    if (this.wizardStep() === 2) {
      void this.finishCreate();
      return;
    }

    this.wizardStep.update((step) => Math.min(3, step + 1));
  }

  previousWizardStep(): void {
    this.wizardStep.update((step) => Math.max(1, step - 1));
  }

  async autoMatchImages(): Promise<void> {
    const folder = this.createForm.imageFolder.trim() || this.folderFromPath(this.createForm.imagePath);
    if (!folder) return this.showNotice('Enter a product image folder first.');
    if (this.createForm.imageMatchMode === 'sku' && !this.createForm.sku.trim()) {
      return this.showNotice('Enter a SKU before searching images by SKU.');
    }

    await this.perform(async () => {
      const result = await this.api.matchImages(
        this.createForm.sku,
        folder,
        this.createForm.imageMatchMode,
      );
      this.matchedImages.set(result.matchedImages);
      if (result.matchedImages[0]) this.createForm.imagePath = result.matchedImages[0];
      this.showNotice(
        this.createForm.imageMatchMode === 'folder'
          ? `${result.matchedImages.length} image(s) loaded from the product folder.`
          : `${result.matchedImages.length} image(s) containing SKU "${this.createForm.sku}" found.`,
      );
    });
  }

  chooseMatchedImage(path: string): void {
    this.createForm.imagePath = path;
  }

  async finishCreate(): Promise<void> {
    await this.perform(async () => {
      const product = await this.api.createProduct({
        sku: this.createForm.sku,
        productName: this.createForm.productName,
        cost: this.createForm.cost,
        weight: this.createForm.weight,
        length: this.createForm.length,
        width: this.createForm.width,
        height: this.createForm.height,
        imagePath: this.createForm.imagePath || null,
        imageSource: this.imageSourceFor(this.createForm.imagePath),
      });

      for (const choice of this.platformChoices.filter((item) => item.selected)) {
        await this.api.saveListing(product.id, { platform: choice.platform, listingUrl: choice.url || null });
      }

      if (this.platformChoices.find((item) => item.platform === 'amazon')?.selected && this.createAsin.asin.trim()) {
        await this.api.createAsin(product.id, {
          asin: this.createAsin.asin,
          asinType: this.createAsin.asinType,
          parentAsin: this.createAsin.asinType === 'child' ? this.createAsin.parentAsin || null : null,
          amazonAttributeDone: this.createAsin.amazonAttributeDone,
        });
      }

      this.wizardOpen.set(false);
      await this.loadProducts();
      await this.openDetail(product.id);
      this.showNotice('Product created.');
    });
  }

  async openDetail(productId: number): Promise<void> {
    await this.perform(async () => {
      const product = await this.api.getProduct(productId);
      this.selectedProduct.set(product);
      this.editForm.sku = product.sku;
      this.editForm.productName = product.productName;
      this.editForm.cost = product.cost;
      this.editForm.weight = product.weight;
      this.editForm.length = product.length;
      this.editForm.width = product.width;
      this.editForm.height = product.height;
      this.editForm.imagePath = product.imagePath ?? '';
      this.syncListingDrafts(product);
      this.detailOpen.set(true);
      this.detailMatchedImages.set(product.imagePath ? [product.imagePath] : []);
    }, false);
  }

  closeDetail(): void {
    this.detailOpen.set(false);
  }

  async updateProduct(): Promise<void> {
    const product = this.selectedProduct();
    if (!product) return;
    await this.perform(async () => {
      await this.api.updateProduct(product.id, {
        sku: this.editForm.sku,
        productName: this.editForm.productName,
        cost: this.editForm.cost,
        weight: this.editForm.weight,
        length: this.editForm.length,
        width: this.editForm.width,
        height: this.editForm.height,
        imagePath: this.editForm.imagePath || null,
        imageSource: this.imageSourceFor(this.editForm.imagePath),
      });
      await this.loadProducts();
      await this.openDetail(product.id);
      this.showNotice('Product details saved.');
    });
  }

  async deleteProduct(): Promise<void> {
    const product = this.selectedProduct();
    if (!product || !confirm(`Delete ${product.sku}?`)) return;
    await this.perform(async () => {
      await this.api.deleteProduct(product.id);
      this.detailOpen.set(false);
      this.selectedProduct.set(null);
      await this.loadProducts();
      this.showNotice('Product deleted.');
    });
  }

  async saveListing(platform: Platform): Promise<void> {
    const product = this.selectedProduct();
    if (!product) return;
    await this.perform(async () => {
      await this.api.saveListing(product.id, { platform, listingUrl: this.listingDrafts[platform] || null });
      await this.refreshSelected(product.id);
      this.showNotice(`${this.platformLabel(platform)} listing saved.`);
    });
  }

  async useAmazonImage(): Promise<void> {
    const product = this.selectedProduct();
    if (!product) return;
    await this.perform(async () => {
      await this.api.useAmazonImage(product.id, this.listingDrafts.amazon || null);
      await this.refreshSelected(product.id);
      this.showNotice('Amazon image connected.');
    });
  }

  async loadDetailImageFolder(): Promise<void> {
    const product = this.selectedProduct();
    const folder = this.folderFromPath(this.editForm.imagePath || product?.imagePath || '');
    if (!product || !folder) return this.showNotice('Enter an image path first.');

    await this.perform(async () => {
      const result = await this.api.matchImages(product.sku, folder, 'folder');
      this.detailMatchedImages.set(result.matchedImages);
      if (!this.editForm.imagePath && result.matchedImages[0]) this.editForm.imagePath = result.matchedImages[0];
      this.showNotice(`${result.matchedImages.length} image(s) loaded from the product folder.`);
    });
  }

  chooseDetailImage(path: string): void {
    this.editForm.imagePath = path;
  }

  async removeListing(listing: ProductListing): Promise<void> {
    await this.perform(async () => {
      await this.api.deleteListing(listing.productId, listing.id);
      await this.refreshSelected(listing.productId);
      this.showNotice(`${this.platformLabel(listing.platform)} listing removed.`);
    });
  }

  async addAsin(): Promise<void> {
    const product = this.selectedProduct();
    if (!product || !this.asinDraft.asin.trim()) return;
    await this.perform(async () => {
      await this.api.createAsin(product.id, {
        asin: this.asinDraft.asin,
        asinType: this.asinDraft.asinType,
        parentAsin: this.asinDraft.asinType === 'child' ? this.asinDraft.parentAsin || null : null,
        amazonAttributeDone: this.asinDraft.amazonAttributeDone,
      });
      this.asinDraft.asin = '';
      this.asinDraft.parentAsin = '';
      await this.refreshSelected(product.id);
      this.showNotice('ASIN added.');
    });
  }

  async removeAsin(asin: AmazonAsin): Promise<void> {
    await this.perform(async () => {
      await this.api.deleteAsin(asin.productId, asin.id);
      await this.refreshSelected(asin.productId);
      this.showNotice('ASIN removed.');
    });
  }

  async copyAttribution(platform: AttributionPlatform): Promise<void> {
    const link = this.selectedProduct()?.amazonAttributionLinks.find((item) => item.platform === platform);
    if (!link) return this.showNotice(`No ${this.platformLabel(platform)} link saved.`);
    await navigator.clipboard.writeText(link.attributionUrl);
    this.showNotice(`${this.platformLabel(platform)} link copied.`);
  }

  async saveAttribution(platform: AttributionPlatform, url: string): Promise<void> {
    const product = this.selectedProduct();
    if (!product || !url.trim()) return;
    await this.perform(async () => {
      await this.api.saveAttribution(product.id, platform, url);
      await this.refreshSelected(product.id);
      this.showNotice(`${this.platformLabel(platform)} link saved.`);
    });
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.importFile.set(input.files?.[0] ?? null);
  }

  async importAttribution(): Promise<void> {
    const product = this.selectedProduct();
    const file = this.importFile();
    if (!product || !file) return this.showNotice('Select an Amazon product and CSV file first.');
    await this.perform(async () => {
      await this.api.importAttribution(product.id, file);
      await this.refreshSelected(product.id);
      this.importFile.set(null);
      this.showNotice('Attribution CSV imported.');
    });
  }

  attributionUrl(platform: AttributionPlatform): string {
    return this.selectedProduct()?.amazonAttributionLinks.find((item) => item.platform === platform)?.attributionUrl ?? '';
  }

  private calculatePricingEstimate(): {
    cubicInches: number;
    dimensionalWeight: number;
    billableWeight: number;
    shippingCost: number;
    breakEvenPrice: number;
    suggestedPrice: number;
    estimatedProfit: number;
    steps: PricingStep[];
  } {
    const cost = this.safeNumber(this.pricingForm.productCost);
    const weight = Math.max(0, this.safeNumber(this.pricingForm.weight));
    const length = Math.max(0, this.safeNumber(this.pricingForm.length));
    const width = Math.max(0, this.safeNumber(this.pricingForm.width));
    const height = Math.max(0, this.safeNumber(this.pricingForm.height));
    const packaging = this.safeNumber(this.pricingForm.packagingCost);
    const marginRate = this.safePercent(this.pricingForm.targetMarginPercent);
    const bufferRate = this.safePercent(this.pricingForm.bufferPercent);
    const walmartReferralRate = 0.15;
    const processingRate = 0.029;
    const processingFixed = 0.3;
    const cubicInches = length * width * height;
    const dimensionalWeight = cubicInches > 0 ? cubicInches / 166 : 0;
    const billableWeight = Math.max(1, Math.ceil(Math.max(weight, dimensionalWeight)));
    const shippingCost = this.estimateUspsCost(billableWeight);
    const fixedCost = cost + shippingCost + packaging + processingFixed;
    const breakEvenVariableRate = walmartReferralRate + processingRate + bufferRate;
    const suggestedVariableRate = breakEvenVariableRate + marginRate;
    const breakEvenPrice = fixedCost / Math.max(0.01, 1 - breakEvenVariableRate);
    const suggestedPrice = fixedCost / Math.max(0.01, 1 - suggestedVariableRate);
    const walmartFee = suggestedPrice * walmartReferralRate;
    const processingFee = suggestedPrice * processingRate + processingFixed;
    const buffer = suggestedPrice * bufferRate;
    const estimatedProfit = suggestedPrice - cost - shippingCost - packaging - walmartFee - processingFee - buffer;

    return {
      cubicInches,
      dimensionalWeight,
      billableWeight,
      shippingCost,
      breakEvenPrice,
      suggestedPrice,
      estimatedProfit,
      steps: [
        { label: 'Product cost', value: this.currency(cost), note: 'Your item cost before shipping and marketplace fees.' },
        { label: 'Package size', value: `${this.number(cubicInches)} in³`, note: `Dimensional weight uses ${this.number(cubicInches)} / 166 = ${this.number(dimensionalWeight)} lb.` },
        { label: 'USPS billable weight', value: `${billableWeight} lb`, note: `The estimate uses the higher of actual weight and dimensional weight, rounded up.` },
        { label: 'Estimated USPS shipping', value: this.currency(shippingCost), note: 'Local planning estimate only; real label prices can vary by zone, service, and account rate.' },
        { label: 'Packaging reserve', value: this.currency(packaging), note: 'Box, mailer, label, tape, and small handling material reserve.' },
        { label: 'Walmart referral fee', value: `${this.number(walmartReferralRate * 100)}%`, note: `${this.currency(walmartFee)} at the suggested price. Default assumes a general 15% category fee.` },
        { label: 'Processing reserve', value: `2.9% + ${this.currency(processingFixed)}`, note: `${this.currency(processingFee)} at the suggested price.` },
        { label: 'Risk buffer', value: `${this.number(bufferRate * 100)}%`, note: `${this.currency(buffer)} reserve for returns, price changes, and small fee drift.` },
        { label: 'Target margin', value: `${this.number(marginRate * 100)}%`, note: `${this.currency(estimatedProfit)} estimated profit at the suggested price.` },
      ],
    };
  }

  private estimateUspsCost(weightLb: number): number {
    if (weightLb <= 1) return 5.25;
    if (weightLb <= 2) return 7.85;
    if (weightLb <= 3) return 9.95;
    if (weightLb <= 5) return 12.75;
    if (weightLb <= 10) return 18.95;
    if (weightLb <= 20) return 32.95;
    return 32.95 + (weightLb - 20) * 1.35;
  }

  private safeNumber(value: number | null | undefined): number {
    return Number.isFinite(Number(value)) ? Number(value) : 0;
  }

  private safePercent(value: number | null | undefined): number {
    return Math.max(0, Math.min(95, this.safeNumber(value))) / 100;
  }

  currency(value: number): string {
    return `$${this.safeNumber(value).toFixed(2)}`;
  }

  number(value: number): string {
    return this.safeNumber(value).toLocaleString(undefined, { maximumFractionDigits: 2 });
  }

  listingFor(product: ProductDetail, platform: Platform): ProductListing | undefined {
    return product.listings.find((item) => item.platform === platform);
  }

  hasAmazon(product: ProductListItem): boolean {
    return product.platforms.includes('amazon') || product.asinCount > 0 || product.amazonAsinValues.length > 0;
  }

  matchesMarketingSearch(product: ProductListItem, keyword: string): boolean {
    return [
      product.sku,
      product.productName,
      product.amazonParentAsin ?? '',
      ...product.amazonAsinValues,
    ].some((value) => value.toLowerCase().includes(keyword));
  }

  platformLabel(platform: string): string {
    const names: Record<string, string> = {
      amazon: 'Amazon', walmart: 'Walmart', ebay: 'eBay', etsy: 'Etsy',
      pinterest: 'Pinterest', facebook: 'Facebook', instagram: 'Instagram',
      tiktok: 'TikTok', youtube: 'YouTube', shopify: 'Shopify',
    };
    return names[platform] ?? platform;
  }

  productInitial(product: ProductListItem | ProductDetail): string {
    return (product.productName || product.sku).trim().slice(0, 2).toUpperCase();
  }

  productImageUrl(productId: number, download = false): string {
    return this.api.productImageUrl(productId, download);
  }

  imagePreviewUrl(path: string): string {
    if (/^https?:\/\//i.test(path.trim())) return path;
    return this.api.imagePreviewUrl(path);
  }

  fileLocationUrl(path: string): string {
    if (/^https?:\/\//i.test(path.trim())) return path;
    const normalized = path.trim().replaceAll('\\', '/');
    if (normalized.startsWith('//')) return `file:${normalized}`;
    if (/^[a-zA-Z]:\//.test(normalized)) return `file:///${normalized}`;
    return path;
  }

  hideBrokenImage(event: Event): void {
    (event.target as HTMLImageElement).style.display = 'none';
  }

  async copyImageLink(productId: number): Promise<void> {
    await this.copyText(this.productImageUrl(productId));
    this.showNotice('Server image link copied.');
  }

  async copyImage(productId: number): Promise<void> {
    const imageUrl = this.productImageUrl(productId);

    try {
      const response = await fetch(imageUrl);
      if (!response.ok) throw new Error('Image unavailable');
      const blob = await response.blob();
      const ClipboardItemType = globalThis.ClipboardItem;
      if (!ClipboardItemType || !navigator.clipboard?.write) throw new Error('Binary clipboard unavailable');
      await navigator.clipboard.write([new ClipboardItemType({ [blob.type]: blob })]);
      this.showNotice('Image copied to clipboard.');
    } catch {
      await this.copyText(imageUrl);
      this.showNotice('The browser blocked image copying, so the image link was copied instead.');
    }
  }

  private async copyText(value: string): Promise<void> {
    if (navigator.clipboard?.writeText) {
      await navigator.clipboard.writeText(value);
      return;
    }

    const textarea = document.createElement('textarea');
    textarea.value = value;
    textarea.style.position = 'fixed';
    textarea.style.opacity = '0';
    document.body.appendChild(textarea);
    textarea.select();
    document.execCommand('copy');
    textarea.remove();
  }

  private async refreshSelected(id: number): Promise<void> {
    const product = await this.api.getProduct(id);
    this.selectedProduct.set(product);
    this.syncListingDrafts(product);
    await this.loadProducts();
  }

  private syncListingDrafts(product: ProductDetail): void {
    for (const platform of ['amazon', 'walmart', 'ebay', 'etsy'] as Platform[]) {
      this.listingDrafts[platform] = product.listings.find((item) => item.platform === platform)?.listingUrl ?? '';
    }
  }

  private resetWizard(): void {
    Object.assign(this.createForm, {
      sku: '',
      productName: '',
      cost: null,
      weight: null,
      length: null,
      width: null,
      height: null,
      imagePath: '',
      imageFolder: '',
      imageMatchMode: 'folder',
    });
    for (const item of this.platformChoices) {
      item.selected = false;
      item.url = '';
    }
    Object.assign(this.createAsin, { asin: '', asinType: 'child', parentAsin: '', amazonAttributeDone: false });
    this.matchedImages.set([]);
    this.wizardStep.set(1);
  }

  private folderFromPath(path: string): string {
    const normalized = path.trim().replaceAll('/', '\\');
    const lastSlash = normalized.lastIndexOf('\\');
    return lastSlash > 2 ? normalized.slice(0, lastSlash) : normalized;
  }

  private imageSourceFor(path: string): 'local' | 'remote' {
    return /^https?:\/\//i.test(path.trim()) ? 'remote' : 'local';
  }

  private async perform(action: () => Promise<void>, showErrors = true): Promise<void> {
    this.loading.set(true);
    try {
      await action();
    } catch (error) {
      if (showErrors) this.showNotice(this.formatError(error));
    } finally {
      this.loading.set(false);
    }
  }

  private formatError(error: unknown): string {
    if (error instanceof HttpErrorResponse) {
      const body = error.error;
      if (typeof body === 'string') return body;
      if (body && typeof body === 'object') {
        const message = 'message' in body ? String(body.message) : error.message;
        const roots = 'allowedRoots' in body && Array.isArray(body.allowedRoots)
          ? ` Allowed roots: ${body.allowedRoots.join(', ')}`
          : '';
        return message + roots;
      }
      return error.message;
    }

    if (error instanceof Error) return error.message;
    return typeof error === 'string' ? error : JSON.stringify(error);
  }

  private showNotice(message: string): void {
    this.notice.set(message);
    setTimeout(() => {
      if (this.notice() === message) this.notice.set('');
    }, 3200);
  }
}
