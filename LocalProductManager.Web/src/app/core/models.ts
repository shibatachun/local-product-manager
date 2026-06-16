export type Platform = 'amazon' | 'walmart' | 'ebay' | 'etsy';
export type AttributionPlatform = 'pinterest' | 'facebook' | 'instagram' | 'tiktok' | 'youtube' | 'shopify';

export interface ProductListItem {
  id: number;
  sku: string;
  productName: string;
  cost: number | null;
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

export interface YouTubeAuthStatus {
  isConfigured: boolean;
  isConnected: boolean;
  channelId: string | null;
  channelTitle: string | null;
  channelThumbnailUrl: string | null;
  message: string | null;
  redirectUri: string;
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
