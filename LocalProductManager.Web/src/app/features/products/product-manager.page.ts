import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import {
  AmazonAsin,
  AttributionPlatform,
  CurrentUser,
  Platform,
  ProductDetail,
  ProductListItem,
  ProductListing,
  ImageHostBrowseResult,
  ImageHostEntry,
  ImageStorageRoot,
  StoreLink,
} from '../../core/models';
import { AuthApiService } from '../../core/auth-api.service';
import { ProductApiService } from '../../core/product-api.service';
import { FileStoragePanelComponent } from './file-storage-panel.component';
import { VideoTrendPanelComponent } from './video-trend-panel.component';
import { UnitConverterPanelComponent } from './unit-converter-panel.component';
import { ApiDocsPanelComponent } from './api-docs-panel.component';
import { Product3dSceneEditorPanelComponent } from './product-3d-scene-editor-panel.component';
import { InventoryManagerPanelComponent } from './inventory-manager-panel.component';
import { BarcodeScannerPage } from '../barcode-scanner/barcode-scanner.page';
import { LabelMakerPage } from '../label-maker/label-maker.page';

type ViewMode = 'products' | 'inventory' | 'barcodeScanner' | 'labelMaker' | 'pricing' | 'unitConverter' | 'marketing' | 'storage' | 'videoTrend' | 'sceneEditor' | 'apiDocs';
type MarketingProductFilter = 'all' | 'standalone' | 'family-one';
type PricingPlatform = Exclude<Platform, 'shopify'>;
type ProductCostCurrency = 'rmb' | 'usd';
type PricingWeightUnit = 'lb' | 'oz' | 'g' | 'kg';
type TauriInternals = {
  invoke<T>(command: string, args?: Record<string, unknown>): Promise<T>;
};
type AsinRelationshipRow = {
  parentAsin: string;
  childAsin: string;
  sku: string;
  amazonAttributeDone: boolean;
  source: 'current' | 'child';
};

interface PricingStep {
  label: string;
  value: string;
  note: string;
}

interface PricingEstimate {
  platform: PricingPlatform;
  logistics: string;
  category: string;
  categoryRate: number;
  quantity: number;
  unitCost: number;
  productCostCurrency: ProductCostCurrency;
  unitProductCostRmb: number;
  unitProductCostUsd: number;
  unitFirstMileRmb: number;
  unitFirstMileUsd: number;
  unitLandedCostRmb: number;
  totalLandedCostRmb: number;
  firstMileRate: number;
  unitWeightInput: number;
  unitWeightUnit: PricingWeightUnit;
  packagingWeightInput: number;
  packagingWeightUnit: PricingWeightUnit;
  unitWeightGrams: number;
  unitProductWeight: number;
  packagingWeight: number;
  totalProductWeight: number;
  totalPackageWeight: number;
  rmbToUsdRate: number;
  exchangeRateDate: string;
  exchangeRateSource: string;
  totalCost: number;
  cubicInches: number;
  dimensionalWeight: number;
  billableWeight: number;
  shippingCost: number;
  breakEvenPrice: number;
  suggestedPrice: number;
  estimatedProfit: number;
  targetProfit: number;
  targetProfitEnabled: boolean;
  steps: PricingStep[];
}

interface LogisticsOption {
  value: string;
  label: string;
  note: string;
  dimDivisor: number;
  tiers: { max: number; cost: number; baseAbove?: number; perLb?: number }[];
  overflowPerLb: number;
  weightPaddingLb?: number;
  includePackagingReserve?: boolean;
}

interface PricingPlatformConfig {
  label: string;
  defaultLogistics: string;
  defaultCategory: string;
  processingRate: number;
  fixedFee: number;
  listingFee: number;
  marginPercent: number;
  bufferPercent: number;
  note: string;
  categories: PricingCategory[];
  logistics: LogisticsOption[];
}

interface PricingCategory {
  value: string;
  label: string;
  feeRate: number;
  note: string;
}

interface PricingInput {
  platform: PricingPlatform;
  category: string;
  logistics: string;
  quantity: number;
  unitCost: number | null;
  productCostCurrency: ProductCostCurrency;
  unitWeight: number | null;
  unitWeightUnit: PricingWeightUnit;
  packagingWeight: number | null;
  packagingWeightUnit: PricingWeightUnit;
  firstMileRate: number | null;
  length: number | null;
  width: number | null;
  height: number | null;
  packagingCost: number | null;
  targetProfit: number | null;
  targetMarginPercent: number | null;
  bufferPercent: number | null;
}

interface PlatformChoice {
  platform: Platform;
  label: string;
  selected: boolean;
  url: string;
}

@Component({
  selector: 'app-product-manager',
  imports: [CommonModule, FormsModule, RouterLink, FileStoragePanelComponent, VideoTrendPanelComponent, UnitConverterPanelComponent, ApiDocsPanelComponent, Product3dSceneEditorPanelComponent, InventoryManagerPanelComponent, BarcodeScannerPage, LabelMakerPage],
  templateUrl: './product-manager.page.html',
  styleUrls: [
    './product-manager.page.css',
    './product-manager.pricing.css',
    './product-manager.modals.css',
    './product-manager.detail.css',
  ],
})
export class ProductManagerPage {
  private readonly route = inject(ActivatedRoute);
  readonly api = inject(ProductApiService);
  readonly authApi = inject(AuthApiService);

  readonly currentUser = signal<CurrentUser | null>(null);
  readonly products = signal<ProductListItem[]>([]);
  readonly marketingProducts = signal<ProductListItem[]>([]);
  readonly selectedProduct = signal<ProductDetail | null>(null);
  readonly search = signal('');
  readonly marketingSearch = signal('');
  readonly marketingProductFilter = signal<MarketingProductFilter>('family-one');
  readonly platformFilter = signal('');
  readonly view = signal<ViewMode>('products');
  readonly mobileMenuOpen = signal(false);
  readonly loading = signal(false);
  readonly wizardOpen = signal(false);
  readonly importOpen = signal(false);
  readonly unitConverterOpen = signal(false);
  readonly detailOpen = signal(false);
  readonly wizardStep = signal(1);
  readonly notice = signal('');
  readonly checkingForUpdates = signal(false);
  readonly desktopApp = Boolean(
    (globalThis as unknown as { __TAURI_INTERNALS__?: TauriInternals }).__TAURI_INTERNALS__,
  );
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
  readonly imageHost = signal<ImageHostBrowseResult | null>(null);
  readonly storeLinks = signal<StoreLink[]>([]);
  readonly newImageRoot = signal('');
  readonly imageHostPath = signal('');
  readonly imageVersion = signal<Record<number, number>>({});
  readonly pricingProductId = signal<number | null>(null);

  readonly platformOptions: { value: Platform | ''; label: string }[] = [
    { value: '', label: 'All platforms' },
    { value: 'amazon', label: 'Amazon' },
    { value: 'walmart', label: 'Walmart' },
    { value: 'ebay', label: 'eBay' },
    { value: 'etsy', label: 'Etsy' },
    { value: 'shopify', label: 'Shopify' },
  ];
  readonly listingOptions: { value: Platform; label: string }[] = [
    { value: 'amazon', label: 'Amazon' },
    { value: 'walmart', label: 'Walmart' },
    { value: 'ebay', label: 'eBay' },
    { value: 'etsy', label: 'Etsy' },
    { value: 'shopify', label: 'Shopify' },
  ];
  readonly attributionPlatforms: AttributionPlatform[] = ['pinterest', 'facebook', 'instagram', 'tiktok', 'youtube', 'shopify'];
  readonly marketingProductFilters: { value: MarketingProductFilter; label: string }[] = [
    { value: 'all', label: 'All' },
    { value: 'standalone', label: 'Singles' },
    { value: 'family-one', label: 'One / family' },
  ];
  readonly createForm = {
    localInventoryId: '',
    sku: '',
    productName: '',
    category: '',
    cost: null as number | null,
    costCurrency: 'rmb' as ProductCostCurrency,
    originalCost: null as number | null,
    salePrice: null as number | null,
    qty: 0,
    weight: null as number | null,
    length: null as number | null,
    width: null as number | null,
    height: null as number | null,
    imagePath: '',
    imageFolder: '',
    listingImageUrl: '',
  };
  readonly platformChoices: PlatformChoice[] = [
    { platform: 'amazon', label: 'Amazon', selected: false, url: '' },
    { platform: 'walmart', label: 'Walmart', selected: false, url: '' },
    { platform: 'ebay', label: 'eBay', selected: false, url: '' },
    { platform: 'etsy', label: 'Etsy', selected: false, url: '' },
    { platform: 'shopify', label: 'Shopify', selected: false, url: '' },
  ];
  readonly createAsin = {
    asin: '',
    asinType: 'child' as 'parent' | 'child',
    parentAsin: '',
    amazonAttributeDone: false,
  };
  readonly editForm = {
    localInventoryId: '',
    sku: '',
    productName: '',
    category: '',
    cost: null as number | null,
    costCurrency: 'rmb' as ProductCostCurrency,
    originalCost: null as number | null,
    salePrice: null as number | null,
    qty: 0,
    weight: null as number | null,
    length: null as number | null,
    width: null as number | null,
    height: null as number | null,
    imagePath: ''
  };
  readonly detailImageForm = {
    onlineUrl: '',
    folder: ''
  };
  readonly storeLinkDrafts: Record<Platform, string> = { amazon: '', walmart: '', ebay: '', etsy: '', shopify: '' };
  readonly listingDrafts: Record<Platform, string> = { amazon: '', walmart: '', ebay: '', etsy: '', shopify: '' };
  readonly asinDraft = { asin: '', asinType: 'child' as 'parent' | 'child', parentAsin: '', amazonAttributeDone: false };
  readonly pricingForm = {
    platform: 'walmart' as PricingPlatform,
    category: 'general',
    logistics: 'usps-ground',
    quantity: 1,
    productCost: 0,
    productCostCurrency: 'rmb' as ProductCostCurrency,
    weight: 1,
    weightUnit: 'lb' as PricingWeightUnit,
    packagingWeight: 0.25,
    packagingWeightUnit: 'lb' as PricingWeightUnit,
    firstMileRate: 0.05,
    length: 6,
    width: 4,
    height: 2,
    packagingCost: 0.65,
    targetProfit: 0,
    targetMarginPercent: 20,
    bufferPercent: 5,
  };
  readonly detailPricingForm = {
    platform: 'walmart' as PricingPlatform,
    category: 'general',
    logistics: 'usps-ground',
    quantity: 1,
    productCostCurrency: 'rmb' as ProductCostCurrency,
    packagingWeight: 0.25,
    packagingWeightUnit: 'lb' as PricingWeightUnit,
    firstMileRate: 0.05,
    packagingCost: 0.65,
    targetProfit: 0,
    targetMarginPercent: 20,
    bufferPercent: 5,
  };
  readonly pricingConfigs: Record<PricingPlatform, PricingPlatformConfig> = {
    walmart: {
      label: 'Walmart',
      defaultLogistics: 'walmart-wfs',
      defaultCategory: 'general',
      processingRate: 0.029,
      fixedFee: 0.3,
      listingFee: 0,
      marginPercent: 20,
      bufferPercent: 5,
      note: 'Walmart estimate defaults to WFS fulfillment plus a general referral fee assumption.',
      categories: [
        this.category('general', 'General merchandise', 0.15, 'Common Walmart Marketplace planning rate for many categories.'),
        this.category('apparel', 'Apparel and accessories', 0.15, 'Typical soft goods planning rate.'),
        this.category('electronics', 'Consumer electronics', 0.08, 'Lower electronics-style referral estimate.'),
        this.category('home', 'Home, kitchen, garden', 0.15, 'General home and kitchen planning rate.'),
        this.category('jewelry', 'Jewelry and watches', 0.20, 'Higher-risk accessory category estimate.'),
        this.category('media', 'Media, books, games', 0.15, 'General media planning rate.'),
      ],
      logistics: [
        this.logistics(
          'walmart-wfs',
          'Walmart WFS',
          'WFS fulfillment estimate. Uses the explicit packaging weight field, rounds up, and includes pick, pack, ship, support, and standard packaging. Storage, returns, peak, hazmat, apparel, oversize, and low-price surcharges are not included.',
          139,
          [
            [1, 3.45],
            [2, 4.95],
            [3, 5.45],
            { max: 20, cost: 5.75, baseAbove: 4, perLb: 0.4 },
            { max: 30, cost: 15.55, baseAbove: 21, perLb: 0.4 },
            { max: 50, cost: 14.55, baseAbove: 31, perLb: 0.4 },
            { max: 500, cost: 17.55, baseAbove: 51, perLb: 0.4 },
          ],
          0.8,
          { includePackagingReserve: false },
        ),
        this.logistics('usps-ground', 'USPS Ground Advantage', 'Common light parcel option for seller-fulfilled orders.', 139, [[1, 5.7], [2, 8.35], [3, 10.5], [5, 13.35], [10, 19.95], [20, 34.95]], 1.45),
        this.logistics('ups-ground', 'UPS Ground', 'Useful for heavier boxes and commercial pickups.', 139, [[1, 8.9], [5, 13.8], [10, 18.9], [20, 31.5], [40, 55]], 1.25),
        this.logistics('fedex-ground', 'FedEx Ground', 'Similar ground parcel planning tier.', 139, [[1, 9.25], [5, 14.25], [10, 19.5], [20, 32.5], [40, 57]], 1.3),
      ],
    },
    amazon: {
      label: 'Amazon',
      defaultLogistics: 'amazon-fba',
      defaultCategory: 'home-kitchen',
      processingRate: 0,
      fixedFee: 0.3,
      listingFee: 0,
      marginPercent: 20,
      bufferPercent: 6,
      note: 'Amazon estimate defaults to FBA-style fulfillment plus the common 15% referral assumption.',
      categories: [
        this.category('home-kitchen', 'Home and Kitchen', 0.15, 'Amazon commonly lists Home and Kitchen at 15%.'),
        this.category('clothing-low', 'Clothing <= $15', 0.05, 'Amazon clothing tier for lower-priced items.'),
        this.category('clothing-mid', 'Clothing $15.01-$20', 0.10, 'Amazon clothing mid-price tier.'),
        this.category('clothing-high', 'Clothing > $20', 0.17, 'Amazon clothing higher-price tier.'),
        this.category('electronics', 'Consumer Electronics', 0.08, 'Amazon electronics planning rate.'),
        this.category('beauty-low', 'Beauty/Health <= $10', 0.08, 'Amazon low-price beauty and health tier.'),
        this.category('beauty-high', 'Beauty/Health > $10', 0.15, 'Amazon beauty and health higher-price tier.'),
        this.category('jewelry', 'Jewelry <= $250', 0.20, 'Amazon jewelry first-tier estimate.'),
        this.category('everything-else', 'Everything Else', 0.15, 'Fallback Amazon category rate.'),
      ],
      logistics: [
        this.logistics('amazon-fba', 'Amazon FBA estimate', 'Simplified FBA fulfillment tier for small parcel planning.', 139, [[1, 4.2], [2, 5.35], [3, 6.25], [5, 7.85], [10, 10.95], [20, 17.5], [40, 34]], 1.1),
        this.logistics('usps-ground', 'FBM USPS Ground Advantage', 'Merchant-fulfilled Amazon order shipped by USPS.', 139, [[1, 5.7], [2, 8.35], [3, 10.5], [5, 13.35], [10, 19.95], [20, 34.95]], 1.45),
        this.logistics('ups-ground', 'FBM UPS Ground', 'Merchant-fulfilled Amazon order shipped by UPS.', 139, [[1, 8.9], [5, 13.8], [10, 18.9], [20, 31.5], [40, 55]], 1.25),
      ],
    },
    ebay: {
      label: 'eBay',
      defaultLogistics: 'ebay-usps',
      defaultCategory: 'general',
      processingRate: 0,
      fixedFee: 0.4,
      listingFee: 0,
      marginPercent: 18,
      bufferPercent: 5,
      note: 'General eBay final value fee estimate. Category fees can differ.',
      categories: [
        this.category('general', 'General goods', 0.1325, 'Common eBay final value planning rate.'),
        this.category('fashion', 'Fashion', 0.1325, 'Common fashion estimate; exact seller/account rules can vary.'),
        this.category('electronics', 'Consumer electronics', 0.10, 'Lower electronics-style final value estimate.'),
        this.category('media', 'Books, movies, music', 0.1455, 'Media categories can run higher than general goods.'),
        this.category('jewelry', 'Jewelry and watches', 0.15, 'Accessory category planning estimate.'),
        this.category('collectibles', 'Collectibles', 0.1325, 'General collectibles planning rate.'),
      ],
      logistics: [
        this.logistics('ebay-usps', 'eBay USPS label', 'Common eBay label flow for light parcels.', 139, [[1, 5.55], [2, 8.15], [3, 10.25], [5, 12.95], [10, 19.25], [20, 33.5]], 1.4),
        this.logistics('ebay-ups', 'eBay UPS Ground label', 'Often better for heavier packages.', 139, [[1, 8.6], [5, 13.35], [10, 18.35], [20, 30.95], [40, 54]], 1.2),
      ],
    },
    etsy: {
      label: 'Etsy',
      defaultLogistics: 'etsy-usps',
      defaultCategory: 'standard',
      processingRate: 0.03,
      fixedFee: 0.25,
      listingFee: 0.2,
      marginPercent: 22,
      bufferPercent: 5,
      note: 'Etsy estimate includes listing, transaction, and US payment processing assumptions.',
      categories: [
        this.category('standard', 'Standard Etsy sale', 0.065, 'Etsy transaction fee estimate before payment processing.'),
        this.category('handmade', 'Handmade goods', 0.065, 'Same Etsy transaction fee model; useful for tracking category.'),
        this.category('vintage', 'Vintage goods', 0.065, 'Same Etsy transaction fee model; category kept for planning.'),
        this.category('craft-supplies', 'Craft supplies', 0.065, 'Same Etsy transaction fee model; category kept for planning.'),
        this.category('offsite-ad', 'Offsite ad sale', 0.185, 'Adds a 12% offsite ads reserve on top of the 6.5% transaction fee.'),
      ],
      logistics: [
        this.logistics('etsy-usps', 'Etsy USPS label', 'Common Etsy seller label for handmade and small parcels.', 139, [[1, 5.55], [2, 8.15], [3, 10.25], [5, 12.95], [10, 19.25], [20, 33.5]], 1.4),
        this.logistics('etsy-ups', 'Etsy UPS Ground label', 'Planning option for larger boxes.', 139, [[1, 8.6], [5, 13.35], [10, 18.35], [20, 30.95], [40, 54]], 1.2),
      ],
    },
  };
  readonly pricingPlatformOptions: { value: PricingPlatform; label: string }[] = [
    { value: 'amazon', label: 'Amazon' },
    { value: 'walmart', label: 'Walmart' },
    { value: 'ebay', label: 'eBay' },
    { value: 'etsy', label: 'Etsy' },
  ];
  readonly productCostCurrencyOptions: { value: ProductCostCurrency; label: string }[] = [
    { value: 'rmb', label: 'RMB' },
    { value: 'usd', label: 'USD' },
  ];
  readonly pricingWeightUnitOptions: { value: PricingWeightUnit; label: string }[] = [
    { value: 'lb', label: 'lb' },
    { value: 'oz', label: 'oz' },
    { value: 'g', label: 'g' },
    { value: 'kg', label: 'kg' },
  ];
  readonly firstMileRateOptions = [0, 0.03, 0.05, 0.08, 0.1];
  readonly pricingResult = signal<PricingEstimate | null>(null);
  readonly rmbToUsdRate = signal({ rate: 0.14, date: 'fallback', source: 'Fallback estimate' });
  readonly detailPricingResult = signal<PricingEstimate | null>(null);

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
  readonly inventoryProducts = computed(() =>
    this.flattenProducts(this.products()).sort((left, right) =>
      (left.localInventoryId ?? '').localeCompare(right.localInventoryId ?? '') ||
      left.sku.localeCompare(right.sku),
    ),
  );
  readonly pricingProduct = computed(() => {
    const productId = this.pricingProductId();
    return productId === null ? null : this.inventoryProducts().find((product) => product.id === productId) ?? null;
  });
  readonly amazonProducts = computed(() => {
    const keyword = this.marketingSearch().trim().toLowerCase();
    const products = this.marketingBaseProducts();
    if (!keyword) return products;

    return products.filter((product) => this.matchesMarketingSearch(product, keyword));
  });
  readonly marketingBaseProducts = computed(() =>
    this.marketingListProducts(this.marketingProducts(), this.marketingProductFilter()),
  );

  async ngOnInit(): Promise<void> {
    const requestedView = this.route.snapshot.queryParamMap.get('view') as ViewMode | null;
    const validViews: ViewMode[] = ['products', 'inventory', 'barcodeScanner', 'labelMaker', 'pricing', 'unitConverter', 'marketing', 'storage', 'videoTrend', 'sceneEditor', 'apiDocs'];
    if (requestedView && validViews.includes(requestedView)) this.changeView(requestedView);
    await Promise.all([this.loadCurrentUser(), this.loadProducts(), this.loadImageStorageRoots(), this.loadStoreLinks(), this.loadRmbToUsdRate(false)]);
  }

  async loadCurrentUser(): Promise<void> {
    try {
      this.currentUser.set(await this.authApi.me());
    } catch {
      this.currentUser.set(null);
    }
  }

  canEditAttribution(): boolean {
    return !!this.currentUser()?.canEditAttribution;
  }

  canImportData(): boolean {
    return !!this.currentUser()?.canImportData;
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

  async exportProducts(): Promise<void> {
    await this.perform(async () => {
      const blob = await this.api.exportProducts();
      const url = URL.createObjectURL(blob);
      const anchor = document.createElement('a');
      anchor.href = url;
      anchor.download = `local-products-${this.exportTimestamp()}.xlsx`;
      document.body.appendChild(anchor);
      anchor.click();
      anchor.remove();
      URL.revokeObjectURL(url);
      this.showNotice('Product Excel exported.');
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

  selectImageHostRoot(path: string): void {
    if (path) this.imageHostPath.set(path);
  }

  async browseImageHost(path = this.imageHostPath()): Promise<void> {
    const trimmed = path.trim();
    if (!trimmed) return this.showNotice('Enter or choose a file host folder first.');

    await this.perform(async () => {
      const result = await this.api.browseImageHost(trimmed);
      this.imageHost.set(result);
      this.imageHostPath.set(result.path);
      this.showNotice(`${result.folders.length} folder(s), ${this.hostFiles(result).length} file(s) loaded.`);
    });
  }

  async openImageHostFolder(path: string): Promise<void> {
    await this.browseImageHost(path);
  }

  imageDownloadUrl(path: string): string {
    return this.api.imageDownloadUrl(path);
  }

  hostFiles(host: ImageHostBrowseResult): ImageHostEntry[] {
    return host.files?.length ? host.files : host.images;
  }

  isImageFile(file: ImageHostEntry): boolean {
    const extension = (file.extension || file.name.split('.').pop() || '').toLowerCase().replace(/^\./, '');
    return ['jpg', 'jpeg', 'png', 'webp', 'bmp', 'gif'].includes(extension);
  }

  fileExtensionLabel(file: ImageHostEntry): string {
    const extension = (file.extension || file.name.split('.').pop() || 'file').replace(/^\./, '');
    return extension ? extension.toUpperCase() : 'FILE';
  }

  fileSize(size: number | null): string {
    if (size === null) return '';
    if (size < 1024) return `${size} B`;
    if (size < 1024 * 1024) return `${(size / 1024).toFixed(1)} KB`;
    return `${(size / 1024 / 1024).toFixed(1)} MB`;
  }

  async loadProducts(): Promise<void> {
    await this.perform(async () => {
      this.products.set(await this.api.search(this.search(), this.platformFilter()));
      if (this.pricingProductId() !== null && !this.pricingProduct()) {
        this.pricingProductId.set(null);
      }
    }, false);
  }

  async loadMarketingProducts(): Promise<void> {
    await this.perform(async () => {
      this.marketingProducts.set(await this.api.search('', '', true));
    }, false);
  }

  changeView(view: ViewMode): void {
    this.view.set(view);
    this.mobileMenuOpen.set(false);
    this.detailOpen.set(false);
    if (view === 'marketing') void this.loadMarketingProducts();
  }

  pageEyebrow(): string {
    const labels: Record<ViewMode, string> = {
      products: 'Catalog',
      inventory: 'Warehouse operations',
      barcodeScanner: 'Inventory tools',
      labelMaker: 'Inventory tools',
      pricing: 'Cost planning',
      unitConverter: 'Measurement tools',
      marketing: 'Amazon Attribution',
      storage: 'Server settings',
      videoTrend: 'Creative research',
      sceneEditor: '3D workspace',
      apiDocs: 'Developer reference',
    };
    return labels[this.view()];
  }

  pageTitle(): string {
    const labels: Record<ViewMode, string> = {
      products: 'Product management',
      inventory: 'Inventory Manager',
      barcodeScanner: 'Barcode Scanner',
      labelMaker: 'Label Maker',
      pricing: 'Pricing estimator',
      unitConverter: 'Converter',
      marketing: 'Marketing links',
      storage: 'File storage',
      videoTrend: 'Video trend',
      sceneEditor: 'Product 3D Scene Editor',
      apiDocs: 'API documentation',
    };
    return labels[this.view()];
  }

  onPricingPlatformChanged(platform: PricingPlatform): void {
    const config = this.pricingConfigs[platform];
    this.pricingForm.platform = platform;
    this.pricingForm.category = config.defaultCategory;
    this.pricingForm.logistics = config.defaultLogistics;
    this.pricingForm.targetMarginPercent = config.marginPercent;
    this.pricingForm.targetProfit = 0;
    this.pricingForm.bufferPercent = config.bufferPercent;
    this.pricingResult.set(null);
  }

  adjustPricingQuantity(delta: number): void {
    this.pricingForm.quantity = Math.max(1, Math.round(this.safeNumber(this.pricingForm.quantity) + delta));
    this.pricingResult.set(null);
  }

  async loadRmbToUsdRate(showErrors = true): Promise<void> {
    try {
      this.rmbToUsdRate.set(await this.api.rmbToUsdRate());
      this.pricingResult.set(null);
      this.detailPricingResult.set(null);
    } catch {
      if (showErrors) this.showNotice('Could not fetch RMB to USD rate. Using fallback estimate.');
    }
  }

  async calculatePricing(): Promise<void> {
    if (this.pricingNeedsExchangeRate(this.pricingForm.productCostCurrency, this.pricingForm.firstMileRate)) {
      await this.loadRmbToUsdRate(false);
    }
    this.pricingResult.set(this.calculatePricingEstimate());
  }

  onPricingProductChanged(productId: number | null): void {
    this.pricingProductId.set(productId);
    const product = this.pricingProduct();
    if (!product) return;

    this.pricingForm.productCost = product.cost ?? 0;
    this.pricingForm.productCostCurrency = 'rmb';
    this.pricingForm.weight = product.weight ?? 1;
    this.pricingForm.weightUnit = 'lb';
    this.pricingForm.length = product.length ?? this.pricingForm.length;
    this.pricingForm.width = product.width ?? this.pricingForm.width;
    this.pricingForm.height = product.height ?? this.pricingForm.height;
    if (product.category && this.pricingCategoryOptions().some(category => category.value === product.category)) {
      this.pricingForm.category = product.category;
    }
    this.pricingResult.set(null);
  }

  onPricingCostCurrencyChanged(currency: ProductCostCurrency): void {
    this.pricingForm.productCost = this.convertCostAmount(
      this.pricingForm.productCost,
      this.pricingForm.productCostCurrency,
      currency,
    ) ?? 0;
    this.pricingForm.productCostCurrency = currency;
    this.pricingResult.set(null);
  }

  onCreateCostCurrencyChanged(currency: ProductCostCurrency): void {
    this.createForm.cost = this.convertCostAmount(this.createForm.cost, this.createForm.costCurrency, currency);
    this.createForm.costCurrency = currency;
  }

  onEditCostCurrencyChanged(currency: ProductCostCurrency): void {
    this.editForm.cost = this.convertCostAmount(this.editForm.cost, this.editForm.costCurrency, currency);
    this.editForm.costCurrency = currency;
  }

  async savePricingToInventory(): Promise<void> {
    const product = this.pricingProduct();
    const estimate = this.pricingResult();
    if (!product || !estimate) return this.showNotice('Choose an inventory product and calculate a price first.');

    await this.perform(async () => {
      await this.api.updateProduct(product.id, {
        localInventoryId: product.localInventoryId,
        sku: product.sku,
        productName: product.productName,
        category: estimate.category,
        cost: estimate.unitProductCostRmb,
        originalCost: estimate.unitFirstMileRmb,
        salePrice: estimate.suggestedPrice,
        qty: product.qty,
        weight: estimate.unitProductWeight,
        length: product.length ?? this.pricingForm.length,
        width: product.width ?? this.pricingForm.width,
        height: product.height ?? this.pricingForm.height,
        imagePath: product.imagePath,
        imageSource: product.imageSource,
      });
      await this.loadProducts();
      this.showNotice(`Pricing saved to ${product.localInventoryId ?? product.sku}.`);
    });
  }

  async calculateDetailPricing(): Promise<void> {
    const product = this.selectedProduct();
    if (!product) return;
    if (this.pricingNeedsExchangeRate(this.detailPricingForm.productCostCurrency, this.detailPricingForm.firstMileRate)) {
      await this.loadRmbToUsdRate(false);
    }
    this.detailPricingResult.set(this.calculatePricingEstimateForProduct(product));
  }

  pricingLogisticsOptions(): LogisticsOption[] {
    return this.pricingConfigs[this.pricingForm.platform].logistics;
  }

  pricingCategoryOptions(): PricingCategory[] {
    return this.pricingConfigs[this.pricingForm.platform].categories;
  }

  detailPricingLogisticsOptions(): LogisticsOption[] {
    return this.pricingConfigs[this.detailPricingForm.platform].logistics;
  }

  detailPricingCategoryOptions(): PricingCategory[] {
    return this.pricingConfigs[this.detailPricingForm.platform].categories;
  }

  onDetailPricingPlatformChanged(platform: PricingPlatform): void {
    const config = this.pricingConfigs[platform];
    this.detailPricingForm.platform = platform;
    this.detailPricingForm.category = this.selectedProduct()?.category || config.defaultCategory;
    if (!config.categories.some(category => category.value === this.detailPricingForm.category)) {
      this.detailPricingForm.category = config.defaultCategory;
    }
    this.detailPricingForm.logistics = config.defaultLogistics;
    this.detailPricingForm.targetMarginPercent = config.marginPercent;
    this.detailPricingForm.bufferPercent = config.bufferPercent;
    this.detailPricingResult.set(null);
  }

  adjustDetailPricingQuantity(delta: number): void {
    this.detailPricingForm.quantity = Math.max(1, Math.round(this.safeNumber(this.detailPricingForm.quantity) + delta));
    this.detailPricingResult.set(null);
  }

  detailPricingDerived(product: ProductDetail): PricingEstimate {
    return this.calculatePricingEstimateForProduct(product);
  }

  pricingDerived(): PricingEstimate {
    return this.calculatePricingEstimate();
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
      this.imageHostPath.set(root.path);
      if (root.available) await this.browseImageHost(root.path);
      this.showNotice(root.available
        ? 'File folder added and is accessible.'
        : 'File folder added, but the server cannot currently access it.');
    });
  }

  async removeImageRoot(root: ImageStorageRoot): Promise<void> {
    if (!confirm(`Remove this file folder from the whitelist?\n${root.path}`)) return;
    await this.perform(async () => {
      await this.api.deleteImageStorageRoot(root.path);
      await this.loadImageStorageRoots();
      if (this.createForm.imageFolder === root.path) this.createForm.imageFolder = '';
      this.showNotice('File folder removed from the whitelist.');
    });
  }

  openWizard(): void {
    this.resetWizard();
    this.wizardOpen.set(true);
  }

  openImport(): void {
    if (!this.canImportData()) return this.showNotice('Only admin can import Amazon data.');
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
    if (!this.canImportData()) return this.showNotice('Only admin can import Amazon data.');
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
    if (!this.canImportData()) return this.showNotice('Only admin can import attribution data.');
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
    if (!this.canImportData()) return;
    const files = Array.from((event.target as HTMLInputElement).files ?? [])
      .filter((file) => file.name.toLowerCase().endsWith('.csv'));
    this.attributionFolderFiles.set(files);
    this.attributionFolderSummary.set('');
    this.unmatchedAttributionFiles.set([]);
  }

  async importAttributionFolder(): Promise<void> {
    if (!this.canImportData()) return this.showNotice('Only admin can import attribution data.');
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

  async nextWizardStep(): Promise<void> {
    if (this.wizardStep() === 1 && (!this.createForm.sku.trim() || !this.createForm.productName.trim())) {
      return this.showNotice('SKU and product name are required.');
    }

    if (this.wizardStep() === 1 && !await this.checkCreateSkuDuplicate()) {
      return;
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

  async loadCreateLocalImage(): Promise<void> {
    const path = this.createForm.imageFolder.trim() || this.createForm.imagePath.trim();
    if (!path) return this.showNotice('Enter a local image folder or image path first.');
    if (this.isImagePath(path)) {
      this.createForm.imagePath = path;
      this.matchedImages.set([path]);
      this.showNotice('Local image selected.');
      return;
    }

    await this.perform(async () => {
      const result = await this.api.matchImages(this.createForm.sku, path, 'folder');
      this.matchedImages.set(result.matchedImages);
      if (result.matchedImages[0]) this.createForm.imagePath = result.matchedImages[0];
      this.showNotice(`${result.matchedImages.length} image(s) loaded from the local folder.`);
    });
  }

  async fetchCreateListingImage(): Promise<void> {
    const listingUrl = this.createForm.listingImageUrl.trim();
    if (!listingUrl) return this.showNotice('Enter a product page URL first.');

    await this.perform(async () => {
      const result = await this.api.findListingImage(listingUrl);
      this.createForm.imagePath = result.imageUrl;
      this.matchedImages.set([result.imageUrl]);
      this.showNotice('Listing image found.');
    });
  }

  chooseMatchedImage(path: string): void {
    this.createForm.imagePath = path;
  }

  async finishCreate(): Promise<void> {
    await this.perform(async () => {
      if (!await this.checkCreateSkuDuplicate(false)) return;
      const product = await this.api.createProduct({
        localInventoryId: this.createForm.localInventoryId || null,
        sku: this.createForm.sku,
        productName: this.createForm.productName,
        category: this.createForm.category || null,
        cost: this.costInputToRmb(this.createForm.cost, this.createForm.costCurrency),
        originalCost: this.createForm.originalCost,
        salePrice: this.createForm.salePrice,
        qty: this.createForm.qty,
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
      this.editForm.localInventoryId = product.localInventoryId ?? '';
      this.editForm.sku = product.sku;
      this.editForm.productName = product.productName;
      this.editForm.category = product.category ?? '';
      this.editForm.cost = product.cost;
      this.editForm.costCurrency = 'rmb';
      this.editForm.originalCost = product.originalCost;
      this.editForm.salePrice = product.salePrice;
      this.editForm.qty = product.qty;
      this.editForm.weight = product.weight;
      this.editForm.length = product.length;
      this.editForm.width = product.width;
      this.editForm.height = product.height;
      this.editForm.imagePath = product.imagePath ?? '';
      this.syncListingDrafts(product);
      this.syncDetailImageForm(product);
      this.detailOpen.set(true);
      this.detailMatchedImages.set(product.imagePath ? [product.imagePath] : []);
      this.syncDetailPricing(product);
    }, false);
  }

  closeDetail(): void {
    this.detailOpen.set(false);
  }

  async updateProduct(): Promise<void> {
    const product = this.selectedProduct();
    if (!product) return;
    await this.perform(async () => {
      const duplicate = await this.api.checkDuplicateSku(this.editForm.sku, product.id);
      if (duplicate.exactSkuExists) {
        this.showNotice(`SKU "${duplicate.sku}" already exists on ${duplicate.exactProductName || 'another product'}.`);
        return;
      }

      await this.api.updateProduct(product.id, {
        localInventoryId: this.editForm.localInventoryId || product.localInventoryId,
        sku: this.editForm.sku,
        productName: this.editForm.productName,
        category: this.editForm.category || null,
        cost: this.costInputToRmb(this.editForm.cost, this.editForm.costCurrency),
        originalCost: this.editForm.originalCost,
        salePrice: this.editForm.salePrice,
        qty: this.editForm.qty,
        weight: this.editForm.weight,
        length: this.editForm.length,
        width: this.editForm.width,
        height: this.editForm.height,
        imagePath: this.editForm.imagePath || null,
        imageSource: this.imageSourceFor(this.editForm.imagePath),
      });
      this.bumpProductImageVersion(product.id);
      await this.loadProducts();
      await this.openDetail(product.id);
      this.showNotice('Product details saved.');
    });
  }

  private async checkCreateSkuDuplicate(showSameBaseNotice = true): Promise<boolean> {
    const sku = this.createForm.sku.trim();
    if (!sku) return false;
    const duplicate = await this.api.checkDuplicateSku(sku);
    if (duplicate.exactSkuExists) {
      this.showNotice(`SKU "${duplicate.sku}" already exists on ${duplicate.exactProductName || 'another product'}.`);
      if (duplicate.exactProductId) {
        await this.openDetail(duplicate.exactProductId);
      }
      return false;
    }

    if (showSameBaseNotice && duplicate.sameBaseProducts.length) {
      const related = duplicate.sameBaseProducts
        .slice(0, 3)
        .map((product) => product.sku)
        .join(', ');
      this.showNotice(`No exact duplicate. Same SKU number prefix found: ${related}.`);
    }

    return true;
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

  async loadDetailOnlineImage(): Promise<void> {
    const product = this.selectedProduct();
    if (!product) return;
    const listingUrl = this.detailImageForm.onlineUrl.trim() || this.firstListingUrlWithValue();
    if (!listingUrl) return this.showNotice('Enter a product page URL first.');
    const platform = this.platformFromUrl(listingUrl) ?? this.firstListingPlatformWithUrl();

    await this.perform(async () => {
      const result = await this.api.useListingImage(product.id, listingUrl, platform);
      this.editForm.imagePath = result.imageUrl;
      this.detailMatchedImages.set([result.imageUrl]);
      this.bumpProductImageVersion(product.id);
      await this.refreshSelected(product.id);
      this.showNotice('Online image loaded.');
    });
  }

  async loadDetailImageFolder(): Promise<void> {
    const product = this.selectedProduct();
    const fallbackFolder = this.isRemotePath(this.editForm.imagePath || product?.imagePath || '')
      ? ''
      : this.folderFromPath(this.editForm.imagePath || product?.imagePath || '');
    const folder = this.detailImageForm.folder.trim() || fallbackFolder;
    if (!product || !folder) return this.showNotice('Enter a local or network image folder first.');

    await this.perform(async () => {
      const result = await this.api.matchImages(product.sku, folder, 'folder');
      this.detailMatchedImages.set(result.matchedImages);
      this.detailImageForm.folder = folder;
      if (!this.editForm.imagePath && result.matchedImages[0]) this.editForm.imagePath = result.matchedImages[0];
      this.showNotice(`${result.matchedImages.length} image(s) loaded from the product folder.`);
    });
  }

  chooseDetailImage(path: string): void {
    this.editForm.imagePath = path;
    const product = this.selectedProduct();
    if (product) this.bumpProductImageVersion(product.id);
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
    await this.copyText(link.attributionUrl);
    this.showNotice(`${this.platformLabel(platform)} link copied.`);
  }

  async saveAttribution(platform: AttributionPlatform, url: string): Promise<void> {
    if (!this.canEditAttribution()) return this.showNotice('Only admin can edit attribution links.');
    const product = this.selectedProduct();
    
    if (!product || !url.trim()) return;
    await this.perform(async () => {
      await this.api.saveAttribution(product.id, platform, url);
      await this.refreshSelected(product.id);
      this.showNotice(`${this.platformLabel(platform)} link saved.`);
    });
  }

  async onFileSelected(event: Event): Promise<void> {
    if (!this.canImportData()) return this.showNotice('Only admin can import attribution CSV files.');
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0] ?? null;
    this.importFile.set(file);
    input.value = '';
    if (file) await this.importAttribution();
  }

  async importAttribution(): Promise<void> {
    if (!this.canImportData()) return this.showNotice('Only admin can import attribution CSV files.');
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

  private calculatePricingEstimate(): PricingEstimate {
    return this.buildPricingEstimate({
      platform: this.pricingForm.platform,
      category: this.pricingForm.category,
      logistics: this.pricingForm.logistics,
      quantity: this.pricingForm.quantity,
      unitCost: this.pricingForm.productCost,
      productCostCurrency: this.pricingForm.productCostCurrency,
      unitWeight: this.pricingForm.weight,
      unitWeightUnit: this.pricingForm.weightUnit,
      packagingWeight: this.pricingForm.packagingWeight,
      packagingWeightUnit: this.pricingForm.packagingWeightUnit,
      firstMileRate: this.pricingForm.firstMileRate,
      length: this.pricingForm.length,
      width: this.pricingForm.width,
      height: this.pricingForm.height,
      packagingCost: this.pricingForm.packagingCost,
      targetProfit: this.pricingForm.targetProfit,
      targetMarginPercent: this.pricingForm.targetMarginPercent,
      bufferPercent: this.pricingForm.bufferPercent,
    });
  }

  private calculatePricingEstimateForProduct(product: ProductDetail): PricingEstimate {
    return this.buildPricingEstimate({
      platform: this.detailPricingForm.platform,
      category: this.detailPricingForm.category,
      logistics: this.detailPricingForm.logistics,
      quantity: this.detailPricingForm.quantity,
      unitCost: product.cost,
      productCostCurrency: this.detailPricingForm.productCostCurrency,
      unitWeight: product.weight,
      unitWeightUnit: 'lb',
      packagingWeight: this.detailPricingForm.packagingWeight,
      packagingWeightUnit: this.detailPricingForm.packagingWeightUnit,
      firstMileRate: this.detailPricingForm.firstMileRate,
      length: product.length,
      width: product.width,
      height: product.height,
      packagingCost: this.detailPricingForm.packagingCost,
      targetProfit: this.detailPricingForm.targetProfit,
      targetMarginPercent: this.detailPricingForm.targetMarginPercent,
      bufferPercent: this.detailPricingForm.bufferPercent,
    });
  }

  private buildPricingEstimate(input: PricingInput): PricingEstimate {
    const config = this.pricingConfigs[input.platform];
    const logistics = config.logistics.find((item) => item.value === input.logistics) ?? config.logistics[0];
    const category = config.categories.find((item) => item.value === input.category) ?? config.categories[0];
    const quantity = Math.max(1, Math.round(this.safeNumber(input.quantity)));
    const productCostCurrency = input.productCostCurrency;
    const unitProductCostInput = this.safeNumber(input.unitCost);
    const unitWeightInput = Math.max(0, this.safeNumber(input.unitWeight));
    const packagingWeightInput = Math.max(0, this.safeNumber(input.packagingWeight));
    const unitWeightUnit = input.unitWeightUnit;
    const packagingWeightUnit = input.packagingWeightUnit;
    const unitProductWeight = this.weightToLb(unitWeightInput, unitWeightUnit);
    const packagingWeight = this.weightToLb(packagingWeightInput, packagingWeightUnit);
    const unitWeightGrams = this.weightToGrams(unitWeightInput, unitWeightUnit);
    const firstMileRate = Math.max(0, this.safeNumber(input.firstMileRate));
    const unitFirstMileRmb = unitWeightGrams * firstMileRate;
    const exchangeRate = this.rmbToUsdRate();
    const rmbToUsdRate = this.safeNumber(exchangeRate.rate) || 0.14;
    const unitProductCostRmb = productCostCurrency === 'rmb' ? unitProductCostInput : unitProductCostInput / rmbToUsdRate;
    const unitProductCostUsd = productCostCurrency === 'usd' ? unitProductCostInput : unitProductCostInput * rmbToUsdRate;
    const unitFirstMileUsd = unitFirstMileRmb * rmbToUsdRate;
    const unitCost = unitProductCostUsd + unitFirstMileUsd;
    const totalCost = unitCost * quantity;
    const unitLandedCostRmb = unitProductCostRmb + unitFirstMileRmb;
    const totalLandedCostRmb = unitLandedCostRmb * quantity;
    const totalProductWeight = unitProductWeight * quantity;
    const totalPackageWeight = totalProductWeight + packagingWeight;
    const length = Math.max(0, this.safeNumber(input.length));
    const width = Math.max(0, this.safeNumber(input.width));
    const height = Math.max(0, this.safeNumber(input.height));
    const packaging = logistics.includePackagingReserve === false ? 0 : this.safeNumber(input.packagingCost);
    const targetProfit = Math.max(0, this.safeNumber(input.targetProfit));
    const targetProfitEnabled = targetProfit > 0;
    const marginRate = this.safePercent(input.targetMarginPercent);
    const bufferRate = this.safePercent(input.bufferPercent);
    const cubicInches = length * width * height;
    const dimensionalWeight = cubicInches > 0 ? cubicInches / logistics.dimDivisor : 0;
    const carrierPaddingWeight = logistics.weightPaddingLb ?? 0;
    const billableWeight = Math.max(1, Math.ceil(Math.max(totalPackageWeight, dimensionalWeight) + carrierPaddingWeight));
    const shippingCost = this.estimateLogisticsCost(billableWeight, logistics);
    const fixedCost = totalCost + shippingCost + packaging + config.fixedFee + config.listingFee;
    const breakEvenVariableRate = category.feeRate + config.processingRate + bufferRate;
    const suggestedVariableRate = breakEvenVariableRate + marginRate;
    const breakEvenPrice = fixedCost / Math.max(0.01, 1 - breakEvenVariableRate);
    const suggestedPrice = targetProfitEnabled
      ? (fixedCost + targetProfit) / Math.max(0.01, 1 - breakEvenVariableRate)
      : fixedCost / Math.max(0.01, 1 - suggestedVariableRate);
    const platformFee = suggestedPrice * category.feeRate;
    const processingFee = suggestedPrice * config.processingRate + config.fixedFee;
    const buffer = suggestedPrice * bufferRate;
    const estimatedProfit = suggestedPrice - totalCost - shippingCost - packaging - platformFee - processingFee - config.listingFee - buffer;

    return {
      platform: input.platform,
      logistics: logistics.label,
      category: category.label,
      categoryRate: category.feeRate,
      quantity,
      unitCost,
      productCostCurrency,
      unitProductCostRmb,
      unitProductCostUsd,
      unitFirstMileRmb,
      unitFirstMileUsd,
      unitLandedCostRmb,
      totalLandedCostRmb,
      firstMileRate,
      unitWeightInput,
      unitWeightUnit,
      packagingWeightInput,
      packagingWeightUnit,
      unitWeightGrams,
      rmbToUsdRate,
      exchangeRateDate: exchangeRate.date,
      exchangeRateSource: exchangeRate.source,
      unitProductWeight,
      packagingWeight,
      totalProductWeight,
      totalPackageWeight,
      totalCost,
      cubicInches,
      dimensionalWeight,
      billableWeight,
      shippingCost,
      breakEvenPrice,
      suggestedPrice,
      estimatedProfit,
      targetProfit,
      targetProfitEnabled,
      steps: [
        productCostCurrency === 'rmb'
          ? { label: 'Product cost (RMB)', value: this.rmb(unitProductCostRmb), note: `${this.rmb(unitProductCostRmb)} x ${this.unit(rmbToUsdRate)} = ${this.currency(unitProductCostUsd)} per unit.` }
          : { label: 'Product cost (USD)', value: this.currency(unitProductCostUsd), note: 'USD product cost is used directly without RMB conversion.' },
        { label: 'First-mile freight (RMB)', value: this.rmb(unitFirstMileRmb), note: `${this.unit(firstMileRate)} RMB/g x ${this.number(unitWeightGrams)} g = ${this.rmb(unitFirstMileRmb)} per unit.` },
        { label: 'Landed unit cost (USD)', value: this.currency(unitCost), note: `${this.currency(unitProductCostUsd)} product + ${this.currency(unitFirstMileUsd)} first-mile = ${this.currency(unitCost)} per unit.` },
        { label: 'RMB to USD', value: `${this.unit(rmbToUsdRate)} USD/RMB`, note: `${exchangeRate.source}, rate date ${exchangeRate.date}. RMB inputs are converted with this rate.` },
        { label: 'Quantity', value: `${quantity}`, note: `${this.currency(unitCost)} landed unit cost x ${quantity} = ${this.currency(totalCost)} product cost basis.` },
        { label: 'Product weight', value: `${this.number(totalProductWeight)} lb`, note: `${this.number(unitWeightInput)} ${unitWeightUnit} per unit = ${this.number(unitProductWeight)} lb / ${this.number(unitWeightGrams)} g. Grams are used for first-mile freight.` },
        { label: 'Package actual weight', value: `${this.number(totalPackageWeight)} lb`, note: `${this.number(totalProductWeight)} lb product weight + ${this.number(packagingWeight)} lb packaging weight (${this.number(packagingWeightInput)} ${packagingWeightUnit}).` },
        { label: 'Package size', value: `${this.number(cubicInches)} in³`, note: `Dimensional weight uses ${this.number(cubicInches)} / ${logistics.dimDivisor} = ${this.number(dimensionalWeight)} lb.` },
        { label: 'Billable weight', value: `${billableWeight} lb`, note: `Uses the higher of package actual weight and dimensional weight${carrierPaddingWeight ? `, adds ${this.number(carrierPaddingWeight)} lb carrier/program padding` : ''}, then rounds up.` },
        { label: logistics.label, value: this.currency(shippingCost), note: logistics.note },
        { label: 'Packaging cost reserve', value: this.currency(packaging), note: 'Box, mailer, label, tape, and small handling material reserve.' },
        { label: `${config.label} category fee`, value: `${this.number(category.feeRate * 100)}%`, note: `${this.currency(platformFee)} at the suggested price. ${category.note}` },
        { label: 'Processing/fixed fees', value: `${this.number(config.processingRate * 100)}% + ${this.currency(config.fixedFee + config.listingFee)}`, note: `${this.currency(processingFee + config.listingFee)} at the suggested price.` },
        { label: 'Risk buffer', value: `${this.number(bufferRate * 100)}%`, note: `${this.currency(buffer)} reserve for returns, price changes, and small fee drift.` },
        targetProfitEnabled
          ? { label: 'Target profit', value: this.currency(targetProfit), note: `Suggested price is calculated to leave about ${this.currency(targetProfit)} after product cost, shipping, platform fees, processing fees, and buffer.` }
          : { label: 'Target margin', value: `${this.number(marginRate * 100)}%`, note: `${this.currency(estimatedProfit)} estimated profit at the suggested price.` },
      ],
    };
  }

  private estimateLogisticsCost(weightLb: number, logistics: LogisticsOption): number {
    const tier = logistics.tiers.find((item) => weightLb <= item.max);
    if (tier) return tier.cost + Math.max(0, weightLb - (tier.baseAbove ?? tier.max)) * (tier.perLb ?? 0);
    const last = logistics.tiers[logistics.tiers.length - 1];
    return last.cost + (weightLb - last.max) * logistics.overflowPerLb;
  }

  private logistics(
    value: string,
    label: string,
    note: string,
    dimDivisor: number,
    tiers: ([number, number] | { max: number; cost: number; baseAbove?: number; perLb?: number })[],
    overflowPerLb: number,
    options: Pick<LogisticsOption, 'weightPaddingLb' | 'includePackagingReserve'> = {},
  ): LogisticsOption {
    return {
      value,
      label,
      note,
      dimDivisor,
      tiers: tiers.map((tier) => Array.isArray(tier) ? { max: tier[0], cost: tier[1] } : tier),
      overflowPerLb,
      ...options,
    };
  }

  private category(value: string, label: string, feeRate: number, note: string): PricingCategory {
    return { value, label, feeRate, note };
  }

  private safeNumber(value: number | null | undefined): number {
    return Number.isFinite(Number(value)) ? Number(value) : 0;
  }

  private safePercent(value: number | null | undefined): number {
    return Math.max(0, Math.min(95, this.safeNumber(value))) / 100;
  }

  private costInputToRmb(value: number | null | undefined, currency: ProductCostCurrency): number | null {
    if (value === null || value === undefined) return null;
    const amount = this.safeNumber(value);
    return currency === 'usd' ? amount / (this.safeNumber(this.rmbToUsdRate().rate) || 0.14) : amount;
  }

  private convertCostAmount(value: number | null | undefined, from: ProductCostCurrency, to: ProductCostCurrency): number | null {
    if (value === null || value === undefined || from === to) return value ?? null;
    const rate = this.safeNumber(this.rmbToUsdRate().rate) || 0.14;
    const amount = this.safeNumber(value);
    return to === 'usd' ? amount * rate : amount / rate;
  }

  private pricingNeedsExchangeRate(currency: ProductCostCurrency, firstMileRate: number | null | undefined): boolean {
    return currency === 'rmb' || this.safeNumber(firstMileRate) > 0;
  }

  private weightToGrams(value: number, unit: PricingWeightUnit): number {
    const safeValue = Math.max(0, this.safeNumber(value));
    switch (unit) {
      case 'oz': return safeValue * 28.349523125;
      case 'lb': return safeValue * 453.59237;
      case 'kg': return safeValue * 1000;
      case 'g':
      default: return safeValue;
    }
  }

  private weightToLb(value: number, unit: PricingWeightUnit): number {
    return this.weightToGrams(value, unit) / 453.59237;
  }

  private exportTimestamp(): string {
    const now = new Date();
    const pad = (value: number) => String(value).padStart(2, '0');
    return `${now.getFullYear()}${pad(now.getMonth() + 1)}${pad(now.getDate())}-${pad(now.getHours())}${pad(now.getMinutes())}`;
  }

  currency(value: number): string {
    return `$${this.safeNumber(value).toFixed(2)}`;
  }

  rmb(value: number): string {
    return `¥${this.safeNumber(value).toFixed(2)}`;
  }

  number(value: number): string {
    return this.safeNumber(value).toLocaleString(undefined, { maximumFractionDigits: 2 });
  }

  unit(value: number): string {
    return this.safeNumber(value).toLocaleString(undefined, { maximumFractionDigits: 4 });
  }

  asinRelationshipRows(product: ProductDetail): AsinRelationshipRow[] {
    const rows = new Map<string, AsinRelationshipRow>();
    const addRow = (
      parentAsin: string,
      childAsin: string,
      sku: string,
      status: boolean,
      source: 'current' | 'child',
    ) => {
      const parent = parentAsin.trim().toUpperCase();
      const child = childAsin.trim().toUpperCase();
      if (!parent || !child) return;
      rows.set(`${parent}:${child}:${sku}`, { parentAsin: parent, childAsin: child, sku, amazonAttributeDone: status, source });
    };

    for (const asin of product.amazonAsins) {
      const parentAsin = asin.parentAsin || product.amazonParentAsin || asin.asin;
      addRow(parentAsin, asin.asin, product.sku, asin.amazonAttributeDone, 'current');
    }

    for (const child of product.children) {
      for (const childAsin of child.amazonAsinValues) {
        const parentAsin = child.amazonParentAsin || product.amazonParentAsin || this.firstParentAsin(product) || childAsin;
        addRow(parentAsin, childAsin, child.sku, false, 'child');
      }
    }

    if (!rows.size && product.amazonParentAsin) {
      addRow(product.amazonParentAsin, product.amazonParentAsin, product.sku, false, 'current');
    }

    return Array.from(rows.values()).sort((left, right) =>
      left.parentAsin.localeCompare(right.parentAsin) ||
      left.childAsin.localeCompare(right.childAsin) ||
      left.sku.localeCompare(right.sku),
    );
  }

  listingFor(product: ProductDetail, platform: Platform): ProductListing | undefined {
    return product.listings.find((item) => item.platform === platform);
  }

  marketplaceListingUrl(platform: Platform): string | null {
    const value = this.listingDrafts[platform]?.trim();
    if (!value) return null;
    return /^https?:\/\//i.test(value) ? value : `https://${value}`;
  }

  hasAmazon(product: ProductListItem): boolean {
    return product.platforms.includes('amazon') ||
      product.asinCount > 0 ||
      product.amazonAsinValues.length > 0 ||
      product.children.some((child) => this.hasAmazon(child));
  }

  matchesMarketingSearch(product: ProductListItem, keyword: string): boolean {
    return [
      product.sku,
      product.productName,
      product.amazonParentAsin ?? '',
      ...product.amazonAsinValues,
      ...product.children.flatMap((child) => [
        child.sku,
        child.productName,
        child.amazonParentAsin ?? '',
        ...child.amazonAsinValues,
      ]),
    ].some((value) => value.toLowerCase().includes(keyword));
  }

  private marketingListProducts(products: ProductListItem[], filter: MarketingProductFilter): ProductListItem[] {
    const flattened = this.flattenMarketingProducts(products).filter((product) => this.hasAmazon(product));
    if (filter === 'all') {
      return flattened.sort((left, right) => left.sku.localeCompare(right.sku));
    }

    if (filter === 'standalone') {
      return flattened
        .filter((product) => product.parentProductId === null && product.productType !== 'parent')
        .sort((left, right) => left.sku.localeCompare(right.sku));
    }

    const result = new Map<string, ProductListItem>();
    for (const product of flattened) {
      if (product.productType === 'parent') continue;
      const key = product.parentProductId !== null
        ? `parent:${product.parentProductId}`
        : `product:${product.id}`;
      const current = result.get(key);
      if (!current || this.marketingRepresentativeScore(product) > this.marketingRepresentativeScore(current)) {
        result.set(key, product);
      }
    }

    return Array.from(result.values()).sort((left, right) => left.sku.localeCompare(right.sku));
  }

  private flattenMarketingProducts(products: ProductListItem[]): ProductListItem[] {
    return this.flattenProducts(products);
  }

  private flattenProducts(products: ProductListItem[]): ProductListItem[] {
    const result = new Map<number, ProductListItem>();
    for (const product of products) {
      result.set(product.id, product);
      for (const child of product.children) result.set(child.id, child);
    }

    return Array.from(result.values());
  }

  private marketingRepresentativeScore(product: ProductListItem): number {
    let score = 0;
    if (product.imagePath) score += 100;
    if (product.asinCount > 0 || product.amazonAsinValues.length > 0) score += 50;
    if (product.platforms.includes('amazon')) score += 20;
    score -= Math.min(product.sku.length, 120) / 100;
    return score;
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

  private firstParentAsin(product: ProductDetail): string | null {
    return product.amazonAsins.find((asin) => asin.asinType === 'parent')?.asin ?? null;
  }

  productImageUrl(productId: number, download = false): string {
    const url = this.api.productImageUrl(productId, download);
    const version = this.imageVersion()[productId];
    if (!version || download) return url;
    return `${url}${url.includes('?') ? '&' : '?'}v=${version}`;
  }

  imagePreviewUrl(path: string): string {
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
      try {
        await navigator.clipboard.writeText(value);
        return;
      } catch {
        // Fall back for LAN HTTP pages where the async Clipboard API is blocked.
      }
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
    this.editForm.localInventoryId = product.localInventoryId ?? '';
    this.editForm.cost = product.cost;
    this.editForm.originalCost = product.originalCost;
    this.editForm.salePrice = product.salePrice;
    this.editForm.weight = product.weight;
    this.editForm.imagePath = product.imagePath ?? '';
    this.syncListingDrafts(product);
    this.syncDetailImageForm(product, false);
    if (product.imagePath && !this.detailMatchedImages().includes(product.imagePath)) {
      this.detailMatchedImages.update(images => [product.imagePath!, ...images]);
    }
    await this.loadProducts();
    if (this.view() === 'marketing') {
      await this.loadMarketingProducts();
    }
  }

  private bumpProductImageVersion(productId: number): void {
    this.imageVersion.update((versions) => ({ ...versions, [productId]: Date.now() }));
  }

  private syncListingDrafts(product: ProductDetail): void {
    for (const platform of this.listingOptions.map((option) => option.value)) {
      this.listingDrafts[platform] = product.listings.find((item) => item.platform === platform)?.listingUrl ?? '';
    }
  }

  private firstListingPlatformWithUrl(): Platform | null {
    return this.listingOptions.find(option => !!this.listingDrafts[option.value]?.trim())?.value ?? null;
  }

  private firstListingUrlWithValue(): string {
    const platform = this.firstListingPlatformWithUrl();
    return platform ? this.listingDrafts[platform].trim() : '';
  }

  private syncDetailImageForm(product: ProductDetail, overwrite = true): void {
    const firstListingUrl = this.firstListingUrlWithValue();
    if (overwrite || !this.detailImageForm.onlineUrl) {
      this.detailImageForm.onlineUrl = firstListingUrl || (product.imageSource === 'remote' ? product.imagePath ?? '' : '');
    }
    if (overwrite || !this.detailImageForm.folder) {
      this.detailImageForm.folder = product.imagePath && product.imageSource !== 'remote'
        ? this.folderFromPath(product.imagePath)
        : '';
    }
  }

  private platformFromUrl(url: string): Platform | null {
    const value = url.toLowerCase();
    if (value.includes('amazon.')) return 'amazon';
    if (value.includes('walmart.')) return 'walmart';
    if (value.includes('ebay.')) return 'ebay';
    if (value.includes('etsy.')) return 'etsy';
    if (value.includes('shopify.') || value.includes('myshopify.')) return 'shopify';
    return null;
  }

  private syncDetailPricing(product: ProductDetail): void {
    const listingPlatform = product.listings.map((item) => item.platform).find((platform) => this.isPricingPlatform(platform));
    const platform = listingPlatform ?? this.detailPricingForm.platform;
    const config = this.pricingConfigs[platform];
    this.detailPricingForm.platform = platform;
    this.detailPricingForm.category = product.category || config.defaultCategory;
    if (!config.categories.some(category => category.value === this.detailPricingForm.category)) {
      this.detailPricingForm.category = config.defaultCategory;
    }
    this.detailPricingForm.logistics = config.defaultLogistics;
    this.detailPricingForm.quantity = 1;
    this.detailPricingForm.productCostCurrency = 'rmb';
    this.detailPricingForm.firstMileRate = 0.05;
    this.detailPricingForm.packagingWeight = 0.25;
    this.detailPricingForm.packagingWeightUnit = 'lb';
    this.detailPricingForm.targetProfit = 0;
    this.detailPricingForm.targetMarginPercent = config.marginPercent;
    this.detailPricingForm.bufferPercent = config.bufferPercent;
    this.detailPricingResult.set(null);
  }

  private isPricingPlatform(platform: Platform): platform is PricingPlatform {
    return platform === 'amazon' || platform === 'walmart' || platform === 'ebay' || platform === 'etsy';
  }

  private resetWizard(): void {
    Object.assign(this.createForm, {
      localInventoryId: '',
      sku: '',
      productName: '',
      category: '',
      cost: null,
      costCurrency: 'rmb',
      originalCost: null,
      salePrice: null,
      qty: 0,
      weight: null,
      length: null,
      width: null,
      height: null,
      imagePath: '',
      imageFolder: '',
      listingImageUrl: '',
    });
    for (const item of this.platformChoices) {
      item.selected = false;
      item.url = '';
    }
    Object.assign(this.createAsin, { asin: '', asinType: 'child', parentAsin: '', amazonAttributeDone: false });
    this.matchedImages.set([]);
    this.wizardStep.set(1);
  }

  async checkForUpdates(): Promise<void> {
    const tauri = (globalThis as unknown as { __TAURI_INTERNALS__?: TauriInternals }).__TAURI_INTERNALS__;
    if (!tauri || this.checkingForUpdates()) return;

    this.checkingForUpdates.set(true);
    try {
      const result = await tauri.invoke<string>('check_for_updates');
      this.showNotice(result);
    } catch (error) {
      this.showNotice(`Unable to check for updates: ${this.formatError(error)}`);
    } finally {
      this.checkingForUpdates.set(false);
    }
  }

  private folderFromPath(path: string): string {
    const normalized = path.trim().replaceAll('/', '\\');
    const lastSlash = normalized.lastIndexOf('\\');
    return lastSlash > 2 ? normalized.slice(0, lastSlash) : normalized;
  }

  private isRemotePath(path: string): boolean {
    return /^https?:\/\//i.test(path.trim());
  }

  private isImagePath(path: string): boolean {
    return /\.(avif|bmp|gif|jpe?g|png|webp)$/i.test(path.trim().split(/[?#]/)[0]);
  }

  private imageSourceFor(path: string): 'local' | 'remote' {
    return this.isRemotePath(path) ? 'remote' : 'local';
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
