export type Platform = 'amazon' | 'walmart' | 'ebay' | 'etsy' | 'shopify';
export type AttributionPlatform = 'pinterest' | 'facebook' | 'instagram' | 'tiktok' | 'youtube' | 'shopify';

export interface CurrentUser {
  sessionId: string;
  userName: string;
  role: string;
  isHost: boolean;
  isAdmin: boolean;
  canManageStorage: boolean;
  canEditAttribution: boolean;
  canManageSocial: boolean;
  canImportData: boolean;
  canEditProductDetails: boolean;
}

export interface AuthAdminSnapshot {
  users: AppUserAdmin[];
  sessions: UserSessionAdmin[];
}

export interface SocialDataMaintenanceResult {
  status: string;
  accounts: number;
  accountSnapshots: number;
  contentSnapshots: number;
  dailyDeltas: number;
  completedAt: string;
}

export interface AppUserAdmin {
  id: number;
  userName: string;
  role: string;
  isEnabled: boolean;
  canManageStorage: boolean;
  canEditAttribution: boolean;
  canManageSocial: boolean;
  canImportData: boolean;
  canEditProductDetails: boolean;
  updatedAt: string;
}

export interface UserSessionAdmin {
  id: string;
  appUserId: number | null;
  userName: string;
  role: string;
  remoteIp: string | null;
  isHost: boolean;
  createdAt: string;
  lastSeenAt: string;
}

export interface ProductListItem {
  id: number;
  localInventoryId: string | null;
  sku: string;
  productName: string;
  category: string | null;
  inventoryCategory: InventoryCategory;
  categoryInventoryId: string | null;
  cost: number | null;
  originalCost: number | null;
  salePrice: number | null;
  qty: number;
  readyToList: boolean;
  note: string | null;
  weight: number | null;
  length: number | null;
  width: number | null;
  height: number | null;
  imagePath: string | null;
  imageSource: 'local' | 'remote';
  platforms: string[];
  amazonAsinValues: string[];
  asinCount: number;
  productType: 'standalone' | 'parent' | 'child';
  skuBase: string | null;
  amazonParentAsin: string | null;
  parentProductId: number | null;
  children: ProductListItem[];
  createdAt: string;
  updatedAt: string;
}

export interface ProductDetail extends ProductListItem {
  listings: ProductListing[];
  amazonAsins: AmazonAsin[];
  amazonAttributionLinks: AmazonAttributionLink[];
}

export interface Warehouse {
  id: number;
  code: string;
  name: string;
  shelves: WarehouseShelf[];
}

export interface WarehouseShelf {
  id: number;
  code: string;
  zoneCode: string;
  rowNumber: number;
  rackNumber: number;
  levelCount: number;
  x: number;
  y: number;
  width: number;
  height: number;
  rotation: number;
  totalQty: number;
}

export interface ShelfDetail {
  id: number;
  code: string;
  levelCount: number;
  levels: ShelfLevel[];
}

export interface ShelfLevel {
  id: number;
  code: string;
  levelNumber: number;
  totalQty: number;
  items: ShelfInventoryItem[];
}

export interface ShelfInventoryItem {
  productId: number;
  sku: string;
  productName: string;
  qty: number;
  binNumber: number;
}

export interface ProductExtendedData {
  details: { externalProductCode: string | null; variantName: string | null; material: string | null; specification: string | null; unit: string | null; style: string | null; description: string | null; developmentDate: string | null };
  identifiers: { identifierType: string; value: string; isPrimary: boolean; notes: string | null }[];
  suppliers: { name: string; contactName: string | null; contactDetails: string | null; procurementChannel: string | null; buyer: string | null; minimumPurchasePrice: number | null; minimumOrderQuantity: number | null; currency: string; isPreferred: boolean; provenance: string | null; procurementLinks: { url: string; sortOrder: number; source: string | null }[] }[];
  compliance: { declarationName: string | null; countryOfOrigin: string | null; hsCode: string | null; declaredValue: number | null; declaredValueCurrency: string | null; containsBattery: boolean | null; containsPowder: boolean | null; containsLiquid: boolean | null; containsMagnet: boolean | null; shippingMethod: string | null; expirationDate: string | null };
  packaging: { packQuantity: number | null; packageLength: number | null; packageWidth: number | null; packageHeight: number | null; dimensionUnit: string; packageWeight: number | null; cartonWeight: number | null; billableWeight: number | null; weightUnit: string; packagingCost: number | null; packagingCostCurrency: string | null; packagingDifficulty: string | null; labelRequirements: string | null };
  prices: { priceType: string; currency: string; amount: number; quantityThreshold: number | null; effectiveFrom: string | null; effectiveTo: string | null; source: string | null }[];
}

export type InventoryCategory =
  | 'KEYCHAIN'
  | 'POM'
  | 'CHARM'
  | 'EYE'
  | 'KIT'
  | 'BEAD'
  | 'BROOCH'
  | 'CABOCHON'
  | 'UNKNOWN'
  | 'BAG'
  | 'PACKAGE';

export interface ProductDuplicateCheck {
  sku: string;
  skuBase: string | null;
  exactSkuExists: boolean;
  exactProductId: number | null;
  exactProductName: string | null;
  sameBaseProducts: {
    id: number;
    sku: string;
    productName: string;
    skuBase: string | null;
  }[];
}

export interface ProductListing {
  id: number;
  productId: number;
  platform: Platform;
  listingUrl: string | null;
  retailPrice: number | null;
}

export interface AmazonAsin {
  id: number;
  productId: number;
  asin: string;
  asinType: 'parent' | 'child';
  parentAsin: string | null;
  amazonAttributeDone: boolean;
}

export interface AmazonAttributionLink {
  id: number;
  productId: number;
  amazonAsinId: number | null;
  platform: AttributionPlatform;
  attributionUrl: string;
}

export interface ImageMatchResult {
  sku: string;
  folderPath: string;
  mode: 'folder' | 'sku';
  matchedImages: string[];
}

export interface ImageStorageRoot {
  path: string;
  available: boolean;
}

export interface ImageHostEntry {
  name: string;
  path: string;
  type: 'folder' | 'image' | 'file';
  size: number | null;
  modifiedAt: string | null;
  extension: string | null;
}

export interface ImageHostBrowseResult {
  path: string;
  parentPath: string | null;
  folders: ImageHostEntry[];
  files: ImageHostEntry[];
  images: ImageHostEntry[];
}

export interface StoreLink {
  platform: Platform;
  label: string;
  url: string | null;
}

export interface RmbToUsdRate {
  rate: number;
  date: string;
  source: string;
}

export type VideoTrendPlatform = 'youtube' | 'instagram' | 'tiktok' | 'pinterest' | 'facebook' | 'other';

export interface VideoTrendItem {
  id: number;
  platform: VideoTrendPlatform | string;
  url: string;
  title: string;
  description: string | null;
  author: string | null;
  platformVideoId: string | null;
  embedUrl: string | null;
  thumbnailUrl: string | null;
  tags: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface CreateVideoTrendItemRequest {
  platform: string | null;
  url: string;
  title: string | null;
  description: string | null;
  author: string | null;
  tags: string | null;
}

export interface BusinessReportImportResult {
  rowsRead: number;
  parentProductsCreated: number;
  childProductsCreated: number;
  standaloneProductsCreated: number;
  productsUpdated: number;
  variationGroups: number;
}

export interface BulkAttributionImportResult {
  rowsRead: number;
  productsMatched: number;
  linksImported: number;
  unmatchedSkus: string[];
}

export interface AttributionFolderImportResult {
  filesRead: number;
  filesMatched: number;
  productsMatched: number;
  linksImported: number;
  unmatchedFiles: string[];
}

export type SocialPlatform = 'pinterest' | 'facebook' | 'instagram' | 'tiktok' | 'youtube' | 'shopify';
export type SocialMetricKey = 'clicks' | 'likes' | 'views' | 'comments' | 'shares' | 'followers';

export interface SocialAccount {
  id: number;
  platform: SocialPlatform;
  accountName: string;
  accountHandle: string | null;
  accountUrl: string | null;
  color: string | null;
  isActive: boolean;
  metrics: SocialMetricSnapshot[];
  createdAt: string;
  updatedAt: string;
}

export interface SocialMetricSnapshot {
  id: number;
  socialAccountId: number;
  snapshotDate: string;
  clicks: number;
  likes: number;
  views: number;
  comments: number;
  shares: number;
  followers: number;
  createdAt: string;
  updatedAt: string;
}

export interface SocialSyncResult {
  accountsChecked: number;
  metricsImported: number;
  status: string;
}

export interface SocialFetchLogEntry {
  timestamp: string;
  source: string;
  status: string;
  metricsImported: number;
  message: string;
}

export interface SocialDebugSnapshot {
  generatedAt: string;
  accounts: {
    id: number;
    platform: string;
    accountName: string;
    accountHandle: string | null;
    isActive: boolean;
  }[];
  metricSnapshots: SocialDebugMetric[];
  videoMetricSnapshots: SocialDebugVideoMetric[];
  platformTotals: {
    platform: string;
    accounts: number;
    metricRows: number;
    latestViews: number;
    latestLikes: number;
    latestComments: number;
    latestShares: number;
    latestFollowers: number;
    sumViews: number;
    sumLikes: number;
    sumComments: number;
    sumClicks: number;
  }[];
  latestVideoTotals: {
    platform: string;
    socialAccountId: number;
    accountName: string;
    videos: number;
    latestViews: number;
    latestLikes: number;
    latestComments: number;
    latestSnapshotDate: string | null;
  }[];
}

export interface SocialDebugMetric {
  id: number;
  socialAccountId: number;
  platform: string;
  accountName: string;
  snapshotDate: string;
  clicks: number;
  likes: number;
  views: number;
  comments: number;
  shares: number;
  followers: number;
  updatedAt: string;
}

export interface SocialDebugVideoMetric {
  id: number;
  socialAccountId: number;
  platform: string;
  accountName: string;
  platformVideoId: string;
  title: string;
  snapshotDate: string;
  views: number;
  likes: number;
  comments: number;
  clicks: number;
  updatedAt: string;
}

export interface ArchiveLogResult {
  path: string;
}

export interface SocialAiAnalysisStatus {
  isConfigured: boolean;
  model: string;
  reportDirectory: string;
  message: string | null;
}

export interface SocialAiAnalysisResult {
  period: string;
  from: string;
  to: string;
  model: string;
  path: string;
  summary: string;
  generatedAt: string;
}

export interface SocialAiAnalysisReport {
  fileName: string;
  path: string;
  period: string;
  from: string;
  to: string;
  generatedAt: string;
  preview: string;
}

export interface SocialAiAnalysisReportContent {
  fileName: string;
  content: string;
}

export interface YouTubeAuthStatus {
  isConfigured: boolean;
  isConnected: boolean;
  channelId: string | null;
  channelTitle: string | null;
  channelThumbnailUrl: string | null;
  message: string | null;
  redirectUri: string;
  isAdminHost: boolean;
  isTokenExpired: boolean;
  canRefresh: boolean;
  needsReauthorization: boolean;
}

export interface PinterestAuthStatus {
  isConfigured: boolean;
  isConnected: boolean;
  accountId: string | null;
  accountName: string | null;
  accountThumbnailUrl: string | null;
  message: string | null;
  redirectUri: string;
  isAdminHost: boolean;
}

export interface MetaAuthStatus {
  isConfigured: boolean;
  isConnected: boolean;
  userId: string | null;
  userName: string | null;
  facebookPages: number;
  instagramAccounts: number;
  message: string | null;
  redirectUri: string;
  isAdminHost: boolean;
}

export interface PinterestReportImportResult {
  accountId: number;
  dailyRowsImported: number;
  boardsImported: number;
  pinsImported: number;
  accountName: string;
  snapshotDate: string;
}

export interface PinterestContentSnapshot {
  channel: {
    id: string;
    title: string;
    thumbnailUrl: string | null;
    followers: number;
    views: number;
    pinCount: number;
    clicks: number;
    clicksNote: string;
  };
  pins: {
    id: string;
    title: string;
    url: string;
    kind: 'pin' | 'board' | string;
    publishedAt: string | null;
    views: number;
    likes: number;
    clicks: number;
    comments: number;
    metrics: {
      snapshotDate: string;
      views: number;
      likes: number;
      comments: number;
      clicks: number;
    }[];
  }[];
  boards: {
    id: string;
    title: string;
    url: string;
    kind: 'board' | string;
    publishedAt: string | null;
    views: number;
    likes: number;
    clicks: number;
    comments: number;
    metrics: {
      snapshotDate: string;
      views: number;
      likes: number;
      comments: number;
      clicks: number;
    }[];
  }[];
}

export interface InstagramReportImportResult {
  accountId: number;
  postsImported: number;
  viewsImported: number;
  likesImported: number;
  commentsImported: number;
  sharesImported: number;
  followsImported: number;
  accountName: string;
  snapshotDate: string;
}

export interface InstagramContentSnapshot {
  channel: {
    id: string;
    title: string;
    thumbnailUrl: string | null;
    followers: number;
    views: number;
    postCount: number;
    shares: number;
    clicksNote: string;
  };
  posts: {
    id: string;
    title: string;
    url: string;
    kind: 'post' | 'reel' | string;
    publishedAt: string | null;
    views: number;
    likes: number;
    saves: number;
    comments: number;
    metrics: {
      snapshotDate: string;
      views: number;
      likes: number;
      comments: number;
      clicks: number;
    }[];
  }[];
}

export interface FacebookReportImportResult {
  accountId: number;
  postsImported: number;
  viewsImported: number;
  likesImported: number;
  commentsImported: number;
  sharesImported: number;
  clicksImported: number;
  accountName: string;
  snapshotDate: string;
}

export interface TikTokReportImportResult {
  accountId: number;
  rowsImported: number;
  viewsImported: number;
  profileViewsImported: number;
  likesImported: number;
  commentsImported: number;
  sharesImported: number;
  accountName: string;
  from: string;
  to: string;
}

export interface FacebookContentSnapshot {
  channel: {
    id: string;
    title: string;
    thumbnailUrl: string | null;
    followers: number;
    views: number;
    postCount: number;
    clicks: number;
    clicksNote: string;
  };
  posts: {
    id: string;
    title: string;
    url: string;
    kind: 'post' | 'video' | 'reel' | string;
    publishedAt: string | null;
    views: number;
    likes: number;
    clicks: number;
    comments: number;
    metrics: {
      snapshotDate: string;
      views: number;
      likes: number;
      comments: number;
      clicks: number;
    }[];
  }[];
}

export interface YouTubeDebugSnapshot {
  channel: {
    id: string;
    title: string;
    thumbnailUrl: string | null;
    followers: number;
    views: number;
    videoCount: number;
    clicks: number | null;
    clicksNote: string;
  };
  videos: {
    id: string;
    title: string;
    thumbnailUrl: string | null;
    publishedAt: string | null;
    views: number;
    likes: number;
    comments: number;
    metrics: {
      snapshotDate: string;
      views: number;
      likes: number;
      comments: number;
      clicks: number;
    }[];
  }[];
}
