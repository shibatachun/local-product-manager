import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, ElementRef, HostListener, inject, signal, ViewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { BrowserQRCodeSvgWriter } from '@zxing/browser';
import { EncodeHintType } from '@zxing/library';
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
  InventoryCategory,
  StoreLink,
} from '../../core/models';
import { AuthApiService } from '../../core/auth-api.service';
import { ProductApiService, ProductImageFile } from '../../core/product-api.service';
import { InventoryApiService, ProductScannerLocation } from '../../core/inventory-api.service';
import { ProgressiveImageDirective } from '../../core/progressive-image.directive';
import { defaultProductLabelPresets, LabelSettings, loadSavedLabelPresets, SavedLabelPreset } from '../../core/label-preset-storage';
import { FileStoragePanelComponent } from '../file-storage/file-storage-panel.component';
import { VideoTrendPanelComponent } from '../video-trend/video-trend-panel.component';
import { UnitConverterPanelComponent } from '../unit-converter/unit-converter-panel.component';
import { ApiDocsPanelComponent } from '../api-documentation/api-docs-panel.component';
import { Product3dSceneEditorPanelComponent } from '../product-3d-scene-editor/product-3d-scene-editor-panel.component';
import { InventoryManagerPanelComponent } from '../inventory-manager/inventory-manager-panel.component';
import { BarcodeScannerPage } from '../barcode-scanner/barcode-scanner.page';
import { LabelMakerPage } from '../label-maker/label-maker.page';
import { LiveStreamBidProductsPanelComponent } from '../live-stream-bid-products/live-stream-bid-products-panel.component';
import {
  AsinRelationshipRow,
  LogisticsOption,
  MarketingProductFilter,
  PlatformChoice,
  PricingCategory,
  PricingEstimate,
  PricingInput,
  PricingPlatform,
  PricingPlatformConfig,
  PricingWeightUnit,
  ProductCostCurrency,
  TauriInternals,
  ViewMode,
} from './product-management.models';

type ProductSortKey = 'inventoryId' | 'categoryId' | 'sku';
type ProductSortDirection = 'asc' | 'desc';

interface ProductTableColumn {
  label: string;
  width: number;
  minWidth: number;
  sortKey?: ProductSortKey;
}

@Component({
  selector: 'app-product-manager',
  imports: [CommonModule, FormsModule, RouterLink, ProgressiveImageDirective, FileStoragePanelComponent, VideoTrendPanelComponent, UnitConverterPanelComponent, ApiDocsPanelComponent, Product3dSceneEditorPanelComponent, InventoryManagerPanelComponent, BarcodeScannerPage, LabelMakerPage, LiveStreamBidProductsPanelComponent],
  templateUrl: './product-manager.page.html',
  styleUrls: [
    './product-manager.page.css',
    './product-manager.pricing.css',
    './product-manager.modals.css',
    './product-manager.detail.css',
  ],
})
export class ProductManagerPage {
  @ViewChild('inventoryPanel') inventoryPanel?: InventoryManagerPanelComponent;
  @ViewChild('createdBarcode') createdBarcode?: ElementRef<SVGSVGElement>;
  @ViewChild('detailBarcode') detailBarcode?: ElementRef<SVGSVGElement>;
  private readonly route = inject(ActivatedRoute);
  readonly api = inject(ProductApiService);
  readonly authApi = inject(AuthApiService);
  private readonly inventoryApi = inject(InventoryApiService);
  private readonly productColumnStorageKey = 'lpm-product-table-column-widths-v1';
  private readonly productDraftStorageKey = 'lpm-add-product-draft-v1';
  private readonly productDraftDbName = 'lpm-product-drafts';

  productTableColumns: ProductTableColumn[] = [
    { label: 'Product', width: 430, minWidth: 220 },
    { label: 'Inventory ID', width: 145, minWidth: 105, sortKey: 'inventoryId' },
    { label: 'Category ID', width: 155, minWidth: 110, sortKey: 'categoryId' },
    { label: 'SKU', width: 190, minWidth: 120, sortKey: 'sku' },
    { label: 'Qty', width: 80, minWidth: 60 },
    { label: 'Cost (RMB)', width: 120, minWidth: 90 },
    { label: 'Sale price', width: 120, minWidth: 90 },
    { label: 'Weight (oz)', width: 115, minWidth: 90 },
    { label: 'Platforms', width: 130, minWidth: 100 },
    { label: 'Updated', width: 120, minWidth: 95 },
    { label: '', width: 54, minWidth: 48 },
  ];
  private columnResize: { index: number; startX: number; startWidth: number } | null = null;

  readonly currentUser = signal<CurrentUser | null>(null);
  readonly products = signal<ProductListItem[]>([]);
  readonly productSortKey = signal<ProductSortKey | null>(null);
  readonly productSortDirection = signal<ProductSortDirection>('asc');
  readonly marketingProducts = signal<ProductListItem[]>([]);
  readonly selectedProduct = signal<ProductDetail | null>(null);
  readonly detailLocations = signal<ProductScannerLocation[]>([]);
  readonly search = signal('');
  readonly marketingSearch = signal('');
  readonly marketingProductFilter = signal<MarketingProductFilter>('family-one');
  readonly platformFilter = signal('');
  readonly view = signal<ViewMode>('products');
  readonly mobileMenuOpen = signal(false);
  readonly loading = signal(false);
  readonly wizardOpen = signal(false);
  readonly pendingBinTarget = signal<{ levelId: number; binNumber: number; locationCode: string } | null>(null);
  readonly createdBinProduct = signal<ProductListItem | null>(null);
  readonly createdBinLocation = signal('');
  readonly createdBinQuantity = signal(0);
  readonly printPresetTarget = signal<'created' | 'detail' | null>(null);
  readonly savedPrintPresets = signal<SavedLabelPreset[]>([]);
  readonly importOpen = signal(false);
  readonly unitConverterOpen = signal(false);
  readonly detailOpen = signal(false);
  private detailReturnsToInventory = false;
  readonly wizardStep = signal(1);
  readonly notice = signal('');
  readonly checkingForUpdates = signal(false);
  readonly desktopApp = Boolean(
    (globalThis as unknown as { __TAURI_INTERNALS__?: TauriInternals }).__TAURI_INTERNALS__,
  );
  readonly matchedImages = signal<string[]>([]);
  readonly detailMatchedImages = signal<string[]>([]);
  readonly detailProductImages = signal<ProductImageFile[]>([]);
  pendingCreateImage: File | null = null;
  createImagePreviewUrl = '';
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
  readonly inventoryCategoryOptions: { value: InventoryCategory; label: string }[] = [
    { value: 'KEYCHAIN', label: 'KEYCHAIN' },
    { value: 'POM', label: 'POM' },
    { value: 'CHARM', label: 'CHARM' },
    { value: 'EYE', label: 'EYE' },
    { value: 'KIT', label: 'KIT' },
    { value: 'BEAD', label: 'BEAD' },
    { value: 'BROOCH', label: 'BROOCH' },
    { value: 'UNKNOWN', label: 'UNKNOWN' },
    { value: 'BAG', label: 'BAG' },
    { value: 'PACKAGE', label: 'PACKAGE' },
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
    inventoryCategory: 'UNKNOWN' as InventoryCategory,
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
    inventoryCategory: 'UNKNOWN' as InventoryCategory,
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
    weightUnit: 'oz' as PricingWeightUnit,
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
  private productsLoaded = false;
  private marketingProductsLoaded = false;
  private imageStorageRootsLoaded = false;
  private storeLinksLoaded = false;
  private exchangeRateLoaded = false;

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
  readonly sortedProducts = computed(() => {
    const sortKey = this.productSortKey();
    if (!sortKey) return this.products();

    const direction = this.productSortDirection() === 'asc' ? 1 : -1;
    const sortItems = (items: ProductListItem[]): ProductListItem[] => [...items]
      .sort((left, right) => direction * this.compareProducts(left, right, sortKey))
      .map((product) => ({
        ...product,
        children: product.children.length ? sortItems(product.children) : product.children,
      }));

    return sortItems(this.products());
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
    this.loadProductColumnWidths();
    const requestedView = this.route.snapshot.queryParamMap.get('view') as ViewMode | null;
    const validViews: ViewMode[] = ['products', 'bidProducts', 'inventory', 'barcodeScanner', 'labelMaker', 'pricing', 'unitConverter', 'marketing', 'storage', 'videoTrend', 'sceneEditor', 'apiDocs'];
    if (requestedView && validViews.includes(requestedView)) this.view.set(requestedView);
    await Promise.all([this.loadCurrentUser(), this.ensureViewData(this.view())]);
  }

  productTableWidth(): number {
    return this.productTableColumns.reduce((total, column) => total + column.width, 0);
  }

  toggleProductSort(sortKey: ProductSortKey): void {
    if (this.productSortKey() === sortKey) {
      this.productSortDirection.update((direction) => direction === 'asc' ? 'desc' : 'asc');
      return;
    }

    this.productSortKey.set(sortKey);
    this.productSortDirection.set('asc');
  }

  productSortAriaLabel(column: ProductTableColumn): string {
    if (!column.sortKey) return column.label;
    if (this.productSortKey() !== column.sortKey) return `Sort ${column.label} ascending`;
    const nextDirection = this.productSortDirection() === 'asc' ? 'descending' : 'ascending';
    return `Sort ${column.label} ${nextDirection}`;
  }

  productSortIndicator(sortKey: ProductSortKey): string {
    if (this.productSortKey() !== sortKey) return '↕';
    return this.productSortDirection() === 'asc' ? '↑' : '↓';
  }

  private compareProducts(left: ProductListItem, right: ProductListItem, sortKey: ProductSortKey): number {
    const value = (product: ProductListItem): string => {
      switch (sortKey) {
        case 'inventoryId': return product.localInventoryId || `INV-${product.id}`;
        case 'categoryId': return product.categoryInventoryId || 'UNKNOWN';
        case 'sku': return product.sku || '';
      }
    };

    return value(left).localeCompare(value(right), undefined, { numeric: true, sensitivity: 'base' });
  }

  startColumnResize(event: PointerEvent, index: number): void {
    const column = this.productTableColumns[index];
    if (!column) return;
    event.preventDefault();
    event.stopPropagation();
    this.columnResize = { index, startX: event.clientX, startWidth: column.width };
    document.body.style.cursor = 'col-resize';
    document.body.style.userSelect = 'none';
    (event.currentTarget as HTMLElement).setPointerCapture?.(event.pointerId);
  }

  @HostListener('document:pointermove', ['$event'])
  resizeProductColumn(event: PointerEvent): void {
    if (!this.columnResize) return;
    const column = this.productTableColumns[this.columnResize.index];
    if (!column) return;
    column.width = Math.max(column.minWidth, Math.round(this.columnResize.startWidth + event.clientX - this.columnResize.startX));
  }

  @HostListener('document:pointerup')
  finishColumnResize(): void {
    if (!this.columnResize) return;
    this.columnResize = null;
    document.body.style.cursor = '';
    document.body.style.userSelect = '';
    this.persistProductColumnWidths();
  }

  resetProductColumnWidth(event: MouseEvent, index: number): void {
    event.preventDefault();
    event.stopPropagation();
    const defaults = [430, 145, 155, 190, 80, 120, 120, 115, 130, 120, 54];
    if (this.productTableColumns[index]) this.productTableColumns[index].width = defaults[index];
    this.persistProductColumnWidths();
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
      this.storeLinksLoaded = true;
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
      this.imageStorageRootsLoaded = true;
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
      this.productsLoaded = true;
      if (this.pricingProductId() !== null && !this.pricingProduct()) {
        this.pricingProductId.set(null);
      }
    }, false);
  }

  async loadMarketingProducts(): Promise<void> {
    await this.perform(async () => {
      this.marketingProducts.set(await this.api.amazonMarketingProducts());
      this.marketingProductsLoaded = true;
    }, false);
  }

  changeView(view: ViewMode): void {
    this.view.set(view);
    this.mobileMenuOpen.set(false);
    this.detailOpen.set(false);
    void this.ensureViewData(view);
  }

  private async ensureViewData(view: ViewMode): Promise<void> {
    if (view === 'products') {
      await Promise.all([
        this.productsLoaded ? Promise.resolve() : this.loadProducts(),
        this.storeLinksLoaded ? Promise.resolve() : this.loadStoreLinks(),
      ]);
      return;
    }

    if (view === 'pricing') {
      await Promise.all([
        this.productsLoaded ? Promise.resolve() : this.loadProducts(),
        this.exchangeRateLoaded ? Promise.resolve() : this.loadRmbToUsdRate(false),
      ]);
      return;
    }

    if (view === 'marketing' && !this.marketingProductsLoaded) {
      await this.loadMarketingProducts();
    }
  }

  pageEyebrow(): string {
    const labels: Record<ViewMode, string> = {
      products: 'Catalog',
      bidProducts: 'Live commerce',
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
      bidProducts: 'Live Stream Bid Products',
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
      this.exchangeRateLoaded = true;
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
    this.pricingForm.weightUnit = 'oz';
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
        weight: estimate.unitProductWeight * 16,
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
      this.selectedProduct.set(await this.api.amazonMarketingProduct(productId));
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

  async openWizard(): Promise<void> {
    this.pendingBinTarget.set(null);
    await this.restoreWizardDraft();
    this.wizardOpen.set(true);
  }

  async openWizardForBin(target: { levelId: number; binNumber: number; locationCode: string }): Promise<void> {
    const restored = await this.restoreWizardDraft();
    if (!restored) this.createForm.qty = 1;
    this.pendingBinTarget.set(target);
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
    this.saveWizardDraft();
    this.wizardOpen.set(false);
    this.pendingBinTarget.set(null);
  }

  saveWizardDraft(): void {
    const draft = {
      createForm: { ...this.createForm },
      platformChoices: this.platformChoices.map(item => ({ platform: item.platform, selected: item.selected, url: item.url })),
      createAsin: { ...this.createAsin },
      wizardStep: this.wizardStep(),
    };
    try {
      localStorage.setItem(this.productDraftStorageKey, JSON.stringify(draft));
    } catch {
      // The in-memory draft remains available when browser storage is unavailable.
    }
  }

  async nextWizardStep(): Promise<void> {
    if (this.wizardStep() === 1 && !this.createForm.productName.trim()) {
      return this.showNotice('Product name is required.');
    }

    if (this.wizardStep() === 1 && !await this.checkCreateSkuDuplicate()) {
      return;
    }

    if (this.wizardStep() === 2 && this.platformChoices.some((item) => item.platform === 'amazon' && item.selected)) {
      this.wizardStep.set(3);
      this.saveWizardDraft();
      return;
    }

    if (this.wizardStep() === 2) {
      void this.finishCreate();
      return;
    }

    this.wizardStep.update((step) => Math.min(3, step + 1));
    this.saveWizardDraft();
  }

  previousWizardStep(): void {
    this.wizardStep.update((step) => Math.max(1, step - 1));
    this.saveWizardDraft();
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

  async selectCreateImage(event: Event, cameraPhoto = false): Promise<void> {
    const input = event.target as HTMLInputElement;
    let file = input.files?.[0] ?? null;
    if (!file) return;
    if (cameraPhoto) file = await this.prepareCameraImage(file);
    if (this.createImagePreviewUrl.startsWith('blob:')) URL.revokeObjectURL(this.createImagePreviewUrl);
    this.pendingCreateImage = file;
    this.createImagePreviewUrl = URL.createObjectURL(file);
    this.createForm.imagePath = '';
    await this.writeDraftImage(file);
    this.saveWizardDraft();
    input.value = '';
  }

  useCreateRemoteImage(url: string): void {
    this.createForm.imagePath = url.trim();
    this.pendingCreateImage = null;
    if (this.createImagePreviewUrl.startsWith('blob:')) URL.revokeObjectURL(this.createImagePreviewUrl);
    this.createImagePreviewUrl = this.createForm.imagePath;
    void this.writeDraftImage(null);
    this.saveWizardDraft();
  }

  async selectDetailImage(event: Event, cameraPhoto = false): Promise<void> {
    const input = event.target as HTMLInputElement;
    let file = input.files?.[0];
    const product = this.selectedProduct();
    if (!file || !product) return;
    if (cameraPhoto) file = await this.prepareCameraImage(file);
    await this.perform(async () => {
      const result = await this.api.uploadProductImage(product.id, file);
      this.editForm.imagePath = result.imagePath;
      this.detailMatchedImages.set([result.imagePath]);
      this.bumpProductImageVersion(product.id);
      this.showNotice('Product image uploaded.');
    });
    input.value = '';
  }

  async selectDetailImages(event: Event, cameraPhoto = false): Promise<void> {
    const input = event.target as HTMLInputElement;
    const product = this.selectedProduct();
    let files = Array.from(input.files ?? []);
    if (!product || files.length === 0) return;
    if (cameraPhoto) {
      files = await Promise.all(files.map(file => this.prepareCameraImage(file)));
    }

    await this.perform(async () => {
      const result = await this.api.uploadProductImages(product.id, files);
      this.editForm.imagePath = result.imagePath;
      this.detailMatchedImages.set(result.imagePaths);
      await this.loadDetailProductImages(product.id);
      this.bumpProductImageVersion(product.id);
      this.showNotice(`${result.count} product photo${result.count === 1 ? '' : 's'} uploaded.`);
    });
    input.value = '';
  }

  detailProductImageUrl(productId: number, fileName: string): string {
    return this.api.productImageFileUrl(productId, fileName);
  }

  async makeDetailImagePrimary(image: ProductImageFile): Promise<void> {
    const product = this.selectedProduct();
    if (!product || image.isPrimary) return;
    await this.perform(async () => {
      const result = await this.api.setPrimaryProductImage(product.id, image.fileName);
      this.editForm.imagePath = result.imagePath;
      product.imagePath = result.imagePath;
      product.imageSource = 'local';
      await this.loadDetailProductImages(product.id);
      this.bumpProductImageVersion(product.id);
      this.showNotice('Main product image updated.');
    });
  }

  private async loadDetailProductImages(productId: number): Promise<void> {
    this.detailProductImages.set(await this.api.productImageFiles(productId));
  }

  async openDetailImageFolder(): Promise<void> {
    const product = this.selectedProduct();
    if (!product) return;
    await this.perform(async () => {
      await this.api.openProductImageFolder(product.id);
      this.showNotice('Product image folder opened on the host computer.');
    });
  }

  async finishCreate(): Promise<void> {
    await this.perform(async () => {
      if (!await this.checkCreateSkuDuplicate()) return;
      const product = await this.api.createProduct({
        localInventoryId: this.createForm.localInventoryId || null,
        sku: this.createForm.sku,
        productName: this.createForm.productName,
        category: this.createForm.category || null,
        inventoryCategory: this.createForm.inventoryCategory,
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
      let imageUploadWarning = '';
      if (this.pendingCreateImage) {
        try {
          await this.api.uploadProductImage(product.id, this.pendingCreateImage);
        } catch (error) {
          imageUploadWarning = this.formatError(error);
        }
      }

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

      const binTarget = this.pendingBinTarget();
      if (binTarget) {
        const quantity = Math.max(1, Math.floor(Number(this.createForm.qty) || 0));
        await this.inventoryApi.setInventory(
          binTarget.levelId,
          product.id,
          quantity,
          binTarget.binNumber,
        );
        this.pendingBinTarget.set(null);
        this.wizardOpen.set(false);
        await this.loadProducts();
        await this.inventoryPanel?.refreshAfterProductAdded(binTarget.levelId, product.id);
        this.createdBinProduct.set(product);
        this.createdBinLocation.set(binTarget.locationCode);
        this.createdBinQuantity.set(quantity);
        await this.clearWizardDraft();
        setTimeout(() => this.renderCreatedBarcode());
        if (imageUploadWarning) this.showNotice(`Product created, but image upload failed: ${imageUploadWarning}`);
        return;
      }

      this.wizardOpen.set(false);
      await this.clearWizardDraft();
      await this.loadProducts();
      await this.openDetail(product.id);
      this.showNotice(imageUploadWarning
        ? `Product created, but image upload failed: ${imageUploadWarning}`
        : 'Product created.');
    });
  }

  createdBarcodeValue(product: ProductListItem): string {
    return `LPM|${product.localInventoryId || `INV-${product.id}`}`;
  }

  async closeCreatedBinProduct(): Promise<void> {
    this.createdBinProduct.set(null);
    await this.loadProducts();
  }

  printCreatedProductLabel(): void { this.openPrintPresetSelector('created'); }

  printDetailProductLabel(): void { this.openPrintPresetSelector('detail'); }

  openPrintPresetSelector(target: 'created' | 'detail'): void {
    this.savedPrintPresets.set([
      ...defaultProductLabelPresets(),
      ...loadSavedLabelPresets().filter(item => !!item.settings),
    ]);
    this.printPresetTarget.set(target);
  }

  closePrintPresetSelector(): void { this.printPresetTarget.set(null); }

  selectPrintPreset(preset: SavedLabelPreset): void {
    const settings = preset.settings;
    const target = this.printPresetTarget();
    if (!settings || !target) return;
    this.printPresetTarget.set(null);
    setTimeout(() => this.printProductLabelWithSettings(target, settings));
  }

  private printProductLabelWithSettings(target: 'created' | 'detail', settings: LabelSettings): void {
    const product = target === 'created' ? this.createdBinProduct() : this.selectedProduct();
    const svg = target === 'created' ? this.createdBarcode?.nativeElement : this.detailBarcode?.nativeElement;
    if (!product || !svg) return;
    const qrMarkup = svg.firstElementChild?.outerHTML || svg.outerHTML;
    // Quick print always emits one physical label. Roll columns and feed gaps
    // belong to Label Maker batch printing and must not change this page size.
    const pageWidth = settings.widthMm;
    const pageHeight = settings.heightMm;
    const inventoryId = product.localInventoryId || `INV-${product.id}`;
    const categoryId = product.categoryInventoryId || 'UNKNOWN';
    const content = [
      settings.showName ? `<strong class="product-name">${this.escapeHtml(product.productName)}</strong>` : '',
      `<div class="barcode">${qrMarkup}</div>`,
      settings.showInv ? `<strong class="inv">${this.escapeHtml(inventoryId)}</strong>` : '',
      settings.showCategoryId ? `<strong class="category-id">${this.escapeHtml(categoryId)}</strong>` : '',
      settings.showSku || settings.showQuantity
        ? `<div class="meta">${settings.showSku ? `<span>SKU: ${this.escapeHtml(product.sku)}</span>` : ''}${settings.showQuantity ? `<span>QTY: ${product.qty}</span>` : ''}</div>`
        : '',
    ].join('');
    const frame = document.createElement('iframe');
    frame.style.cssText = 'position:fixed;left:-10000px;top:0;width:1px;height:1px;border:0';
    frame.setAttribute('aria-hidden', 'true');
    document.body.appendChild(frame);
    const doc = frame.contentDocument;
    if (!doc) { frame.remove(); return; }
    doc.open();
    doc.write(`<!doctype html><html><head><meta charset="utf-8"><title>LPM label</title><style>
      @page{size:${pageWidth}mm ${pageHeight}mm;margin:0}*{box-sizing:border-box}html,body{width:${pageWidth}mm;height:${pageHeight}mm;margin:0;padding:0;background:#fff}
      body{font-family:Arial,sans-serif}.label{display:flex;width:${settings.widthMm}mm;height:${settings.heightMm}mm;margin:0;flex-direction:column;align-items:center;justify-content:center;padding:2.5mm;overflow:hidden;text-align:center;font-size:${settings.fontSize}px}
      .product-name{display:block;width:100%;margin-bottom:1mm;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}.barcode{display:grid;place-items:center;flex:0 0 auto;width:${settings.barcodeHeight}px;height:${settings.barcodeHeight}px;max-width:100%;max-height:60%;padding:1px;background:#fff}.barcode svg{display:block;width:100%;height:100%}
      .inv{margin-top:.3mm;font-family:Consolas,monospace;letter-spacing:.04em}.category-id{font-family:Consolas,monospace;font-size:.95em;letter-spacing:.04em}.meta{display:flex;justify-content:center;gap:3mm;width:100%;margin-top:.3mm;font-size:.92em}
    </style></head><body><div class="label">${content}</div></body></html>`);
    doc.close();
    setTimeout(() => { frame.contentWindow?.focus(); frame.contentWindow?.print(); setTimeout(() => frame.remove(), 1500); }, 300);
  }

  private printCreatedProductLabelLegacy(): void {
    const product = this.createdBinProduct();
    const svg = this.createdBarcode?.nativeElement;
    if (!product || !svg) return;
    const qrMarkup = svg.firstElementChild?.outerHTML || svg.outerHTML;

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
    doc.write(`<!doctype html><html><head><meta charset="utf-8"><title>LPM label</title><style>
      @page{size:50mm 30mm;margin:0}*{box-sizing:border-box}html,body{margin:0;padding:0}
      body{font-family:Arial,sans-serif}.label{position:absolute;top:0;left:0;display:flex;width:50mm;height:30mm;flex-direction:column;align-items:center;justify-content:center;padding:2mm;overflow:hidden;text-align:center}
      strong{display:block;max-width:46mm;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;font-size:10px}
      svg{width:15mm;height:15mm}code{font-size:9px;font-weight:700}small{font-size:7px}
    </style></head><body><div class="label"><strong>${this.escapeHtml(product.productName)}</strong>${qrMarkup}
      <code>${this.escapeHtml(product.localInventoryId || `INV-${product.id}`)} · ${this.escapeHtml(product.categoryInventoryId || 'UNKNOWN')}</code>
      <small>${this.escapeHtml(product.sku)} · ${this.escapeHtml(this.createdBinLocation())}</small></div></body></html>`);
    doc.close();
    setTimeout(() => {
      frame.contentWindow?.focus();
      frame.contentWindow?.print();
      setTimeout(() => frame.remove(), 1500);
    }, 300);
  }

  private renderCreatedBarcode(): void {
    const product = this.createdBinProduct();
    const svg = this.createdBarcode?.nativeElement;
    if (!product || !svg) return;
    const hints = new Map<EncodeHintType, unknown>([
      [EncodeHintType.ERROR_CORRECTION, 'M'],
      [EncodeHintType.MARGIN, 2],
    ]);
    svg.replaceChildren();
    const qr = new BrowserQRCodeSvgWriter().write(this.createdBarcodeValue(product), 160, 160, hints);
    qr.setAttribute('width', '100%');
    qr.setAttribute('height', '100%');
    qr.setAttribute('preserveAspectRatio', 'xMidYMid meet');
    svg.appendChild(qr);
  }

  private printDetailProductLabelLegacy(): void {
    const product = this.selectedProduct();
    const svg = this.detailBarcode?.nativeElement;
    if (!product || !svg) return;
    const qrMarkup = svg.firstElementChild?.outerHTML || svg.outerHTML;

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
    doc.write(`<!doctype html><html><head><meta charset="utf-8"><title>LPM label</title><style>
      @page{size:50mm 30mm;margin:0}*{box-sizing:border-box}html,body{margin:0;padding:0}
      body{font-family:Arial,sans-serif}.label{position:absolute;top:0;left:0;display:flex;width:50mm;height:30mm;flex-direction:column;align-items:center;justify-content:center;padding:2mm;overflow:hidden;text-align:center}
      strong{display:block;max-width:46mm;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;font-size:10px}
      svg{width:15mm;height:15mm}code{font-size:9px;font-weight:700}small{font-size:7px}
    </style></head><body><div class="label"><strong>${this.escapeHtml(product.productName)}</strong>${qrMarkup}
      <code>${this.escapeHtml(product.localInventoryId || `INV-${product.id}`)} · ${this.escapeHtml(product.categoryInventoryId || 'UNKNOWN')}</code>
      <small>${this.escapeHtml(product.sku)}</small></div></body></html>`);
    doc.close();
    setTimeout(() => {
      frame.contentWindow?.focus();
      frame.contentWindow?.print();
      setTimeout(() => frame.remove(), 1500);
    }, 300);
  }

  private renderDetailBarcode(): void {
    const product = this.selectedProduct();
    const svg = this.detailBarcode?.nativeElement;
    if (!product || !svg) return;
    const hints = new Map<EncodeHintType, unknown>([
      [EncodeHintType.ERROR_CORRECTION, 'M'],
      [EncodeHintType.MARGIN, 2],
    ]);
    svg.replaceChildren();
    const qr = new BrowserQRCodeSvgWriter().write(this.createdBarcodeValue(product), 160, 160, hints);
    qr.setAttribute('width', '100%');
    qr.setAttribute('height', '100%');
    qr.setAttribute('preserveAspectRatio', 'xMidYMid meet');
    svg.appendChild(qr);
  }

  private escapeHtml(value: string): string {
    return value.replace(/[&<>"']/g, character => ({
      '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;',
    })[character]!);
  }

  async openDetail(productId: number): Promise<void> {
    await this.perform(async () => {
      const [product, locations] = await Promise.all([
        this.api.getProduct(productId),
        this.inventoryApi.productLocations(productId),
      ]);
      this.selectedProduct.set(product);
      this.detailLocations.set(locations.filter(location => location.qty > 0));
      this.editForm.localInventoryId = product.localInventoryId ?? '';
      this.editForm.sku = product.sku;
      this.editForm.productName = product.productName;
      this.editForm.category = product.category ?? '';
      this.editForm.inventoryCategory = product.inventoryCategory || 'UNKNOWN';
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
      await this.loadDetailProductImages(product.id);
      this.syncDetailPricing(product);
      setTimeout(() => this.renderDetailBarcode());
    }, false);
  }

  async openInventoryProductDetail(productId: number): Promise<void> {
    this.detailReturnsToInventory = true;
    await this.openDetail(productId);
  }

  async closeDetail(): Promise<void> {
    this.detailOpen.set(false);
    this.detailLocations.set([]);
    this.detailProductImages.set([]);
    if (this.detailReturnsToInventory) {
      this.detailReturnsToInventory = false;
      await this.inventoryPanel?.refreshOpenShelf();
    }
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
        inventoryCategory: this.editForm.inventoryCategory,
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

  private async checkCreateSkuDuplicate(): Promise<boolean> {
    const sku = this.createForm.sku.trim();
    if (!sku) return true;
    const duplicate = await this.api.checkDuplicateSku(sku);
    if (duplicate.exactSkuExists) {
      this.showNotice(`SKU "${duplicate.sku}" already exists on ${duplicate.exactProductName || 'another product'}.`);
      if (duplicate.exactProductId) {
        await this.openDetail(duplicate.exactProductId);
      }
      return false;
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
      unitWeightUnit: 'oz',
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

  productImageUrl(productId: number, download = false, thumbnailSize?: number): string {
    const url = this.api.productImageUrl(productId, download, thumbnailSize);
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

  showBrokenDetailImage(event: Event): void {
    const image = event.target as HTMLImageElement;
    image.style.display = 'none';
    image.parentElement?.classList.add('image-load-failed');
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
    if (this.createImagePreviewUrl.startsWith('blob:')) URL.revokeObjectURL(this.createImagePreviewUrl);
    this.pendingCreateImage = null;
    this.createImagePreviewUrl = '';
    Object.assign(this.createForm, {
      localInventoryId: '',
      sku: '',
      productName: '',
      category: '',
      inventoryCategory: 'UNKNOWN',
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

  private async restoreWizardDraft(): Promise<boolean> {
    let restored = false;
    try {
      const raw = localStorage.getItem(this.productDraftStorageKey);
      if (raw) {
        const draft = JSON.parse(raw) as {
          createForm?: Record<string, unknown>;
          platformChoices?: Array<{ platform: Platform; selected: boolean; url: string }>;
          createAsin?: Record<string, unknown>;
          wizardStep?: number;
        };
        if (draft.createForm) Object.assign(this.createForm, draft.createForm);
        for (const saved of draft.platformChoices ?? []) {
          const choice = this.platformChoices.find(item => item.platform === saved.platform);
          if (choice) Object.assign(choice, { selected: Boolean(saved.selected), url: saved.url ?? '' });
        }
        if (draft.createAsin) Object.assign(this.createAsin, draft.createAsin);
        this.wizardStep.set(Math.max(1, Math.min(3, Number(draft.wizardStep) || 1)));
        restored = true;
      } else {
        this.resetWizard();
      }
    } catch {
      this.resetWizard();
    }

    const image = await this.readDraftImage();
    if (image) {
      if (this.createImagePreviewUrl.startsWith('blob:')) URL.revokeObjectURL(this.createImagePreviewUrl);
      this.pendingCreateImage = image;
      this.createImagePreviewUrl = URL.createObjectURL(image);
      restored = true;
    } else if (this.createForm.imagePath) {
      this.createImagePreviewUrl = this.createForm.imagePath;
    }
    return restored;
  }

  private async clearWizardDraft(): Promise<void> {
    try {
      localStorage.removeItem(this.productDraftStorageKey);
    } catch {
      // Nothing else is required when browser storage is unavailable.
    }
    await this.writeDraftImage(null);
    this.resetWizard();
  }

  private openDraftDatabase(): Promise<IDBDatabase> {
    return new Promise((resolve, reject) => {
      const request = indexedDB.open(this.productDraftDbName, 1);
      request.onupgradeneeded = () => {
        if (!request.result.objectStoreNames.contains('drafts')) request.result.createObjectStore('drafts');
      };
      request.onsuccess = () => resolve(request.result);
      request.onerror = () => reject(request.error);
    });
  }

  private async writeDraftImage(file: File | null): Promise<void> {
    try {
      const database = await this.openDraftDatabase();
      await new Promise<void>((resolve, reject) => {
        const transaction = database.transaction('drafts', 'readwrite');
        const request = file
          ? transaction.objectStore('drafts').put(file, 'create-image')
          : transaction.objectStore('drafts').delete('create-image');
        request.onsuccess = () => resolve();
        request.onerror = () => reject(request.error);
      });
      database.close();
    } catch {
      // File persistence is best-effort; the current in-memory photo is retained.
    }
  }

  private async readDraftImage(): Promise<File | null> {
    try {
      const database = await this.openDraftDatabase();
      const file = await new Promise<File | null>((resolve, reject) => {
        const request = database.transaction('drafts', 'readonly').objectStore('drafts').get('create-image');
        request.onsuccess = () => resolve(request.result instanceof File ? request.result : null);
        request.onerror = () => reject(request.error);
      });
      database.close();
      return file;
    } catch {
      return null;
    }
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

  private loadProductColumnWidths(): void {
    try {
      const saved = JSON.parse(localStorage.getItem(this.productColumnStorageKey) || 'null');
      if (!Array.isArray(saved)) return;
      this.productTableColumns.forEach((column, index) => {
        const width = Number(saved[index]);
        if (Number.isFinite(width)) column.width = Math.max(column.minWidth, Math.round(width));
      });
    } catch {
      // Keep the default widths when browser storage is unavailable or damaged.
    }
  }

  private persistProductColumnWidths(): void {
    try {
      localStorage.setItem(
        this.productColumnStorageKey,
        JSON.stringify(this.productTableColumns.map(column => column.width)),
      );
    } catch {
      // Column resizing still works for the current session without storage.
    }
  }

  private async prepareCameraImage(file: File): Promise<File> {
    const sourceUrl = URL.createObjectURL(file);
    try {
      const image = new Image();
      image.decoding = 'async';
      image.src = sourceUrl;
      await new Promise<void>((resolve, reject) => {
        image.onload = () => resolve();
        image.onerror = () => reject(new Error('The captured image could not be decoded.'));
      });

      const maximumDimension = 2048;
      const scale = Math.min(1, maximumDimension / Math.max(image.naturalWidth, image.naturalHeight));
      const width = Math.max(1, Math.round(image.naturalWidth * scale));
      const height = Math.max(1, Math.round(image.naturalHeight * scale));
      const canvas = document.createElement('canvas');
      canvas.width = width;
      canvas.height = height;
      const context = canvas.getContext('2d');
      if (!context) throw new Error('Image conversion is unavailable on this device.');
      context.drawImage(image, 0, 0, width, height);

      const blob = await new Promise<Blob | null>(resolve => canvas.toBlob(resolve, 'image/jpeg', .86));
      if (!blob) throw new Error('The captured image could not be converted.');
      const baseName = file.name.replace(/\.[^.]+$/, '') || `camera-${Date.now()}`;
      return new File([blob], `${baseName}.jpg`, {
        type: 'image/jpeg',
        lastModified: Date.now(),
      });
    } catch {
      this.showNotice('Photo conversion was unavailable; uploading the original image.');
      return file;
    } finally {
      URL.revokeObjectURL(sourceUrl);
    }
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
