export type Platform = 'amazon' | 'walmart' | 'ebay' | 'etsy';
export type AttributionPlatform = 'pinterest' | 'facebook' | 'instagram' | 'tiktok' | 'youtube' | 'shopify';

export interface ProductListItem {
  id: number;
  sku: string;
  productName: string;
  category: string | null;
  cost: number | null;
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

export interface ProductListing {
  id: number;
  productId: number;
  platform: Platform;
  listingUrl: string | null;
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
  type: 'folder' | 'image';
  size: number | null;
  modifiedAt: string | null;
}

export interface ImageHostBrowseResult {
  path: string;
  parentPath: string | null;
  folders: ImageHostEntry[];
  images: ImageHostEntry[];
}

export interface StoreLink {
  platform: Platform;
  label: string;
  url: string | null;
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

export interface YouTubeAuthStatus {
  isConfigured: boolean;
  isConnected: boolean;
  channelId: string | null;
  channelTitle: string | null;
  channelThumbnailUrl: string | null;
  message: string | null;
  redirectUri: string;
  isAdminHost: boolean;
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
