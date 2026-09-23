import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import {
  AmazonAsin,
  AmazonAttributionLink,
  AttributionFolderImportResult,
  AttributionPlatform,
  BulkAttributionImportResult,
  BusinessReportImportResult,
  ImageMatchResult,
  ImageHostBrowseResult,
  ImageStorageRoot,
  Platform,
  ProductDetail,
  ProductDuplicateCheck,
  ProductListItem,
  ProductExtendedData,
  ProductListing,
  RmbToUsdRate,
  StoreLink,
  InventoryCategory,
} from './models';
import { apiBaseUrl } from './api-base';
import { MediaUploadService } from './media-upload.service';

@Injectable({ providedIn: 'root' })
export class ProductApiService {
  private readonly http = inject(HttpClient);
  private readonly mediaUpload = inject(MediaUploadService);
  readonly baseUrl = apiBaseUrl();

  search(keyword = '', platform = '', hasAmazon?: boolean): Promise<ProductListItem[]> {
    const query = new URLSearchParams();
    if (keyword.trim()) query.set('keyword', keyword.trim());
    if (platform) query.set('platform', platform);
    if (hasAmazon !== undefined) query.set('hasAmazon', String(hasAmazon));
    const suffix = query.size ? `?${query}` : '';
    return firstValueFrom(this.http.get<ProductListItem[]>(`${this.baseUrl}/products${suffix}`));
  }

  async lookupExact(code: string): Promise<ProductListItem | null> {
    try {
      return await firstValueFrom(this.http.get<ProductListItem>(
        `${this.baseUrl}/products/lookup`,
        { params: { code } },
      ));
    } catch (error: any) {
      if (error?.status === 404) return null;
      throw error;
    }
  }

  getProduct(id: number): Promise<ProductDetail> {
    return firstValueFrom(this.http.get<ProductDetail>(`${this.baseUrl}/products/${id}`));
  }

  getExtendedData(id: number): Promise<ProductExtendedData> {
    return firstValueFrom(this.http.get<ProductExtendedData>(`${this.baseUrl}/products/${id}/extended-data`));
  }

  saveExtendedData(id: number, data: ProductExtendedData): Promise<ProductExtendedData> {
    return firstValueFrom(this.http.put<ProductExtendedData>(`${this.baseUrl}/products/${id}/extended-data`, data));
  }

  amazonMarketingProducts(): Promise<ProductListItem[]> {
    return firstValueFrom(this.http.get<ProductListItem[]>(`${this.baseUrl}/amazon-marketing/products`));
  }

  amazonMarketingProduct(id: number): Promise<ProductDetail> {
    return firstValueFrom(this.http.get<ProductDetail>(`${this.baseUrl}/amazon-marketing/products/${id}`));
  }

  exportProducts(): Promise<Blob> {
    return firstValueFrom(this.http.get(`${this.baseUrl}/products/export`, { responseType: 'blob' }));
  }

  checkDuplicateSku(sku: string, excludeProductId?: number): Promise<ProductDuplicateCheck> {
    const params: Record<string, string> = { sku };
    if (excludeProductId !== undefined) params['excludeProductId'] = String(excludeProductId);
    return firstValueFrom(this.http.get<ProductDuplicateCheck>(`${this.baseUrl}/products/duplicate-check`, { params }));
  }

  createProduct(body: {
    localInventoryId?: string | null;
    sku: string;
    productName: string;
    category?: string | null;
    inventoryCategory?: InventoryCategory | null;
    cost: number | null;
    originalCost?: number | null;
    salePrice?: number | null;
    qty: number;
    readyToList?: boolean;
    note?: string | null;
    weight?: number | null;
    length?: number | null;
    width?: number | null;
    height?: number | null;
    imagePath: string | null;
    imageSource?: 'local' | 'remote';
  }): Promise<ProductDetail> {
    return firstValueFrom(this.http.post<ProductDetail>(`${this.baseUrl}/products`, body));
  }

  updateProduct(id: number, body: {
    localInventoryId?: string | null;
    sku: string;
    productName: string;
    category?: string | null;
    inventoryCategory?: InventoryCategory | null;
    cost: number | null;
    originalCost?: number | null;
    salePrice?: number | null;
    qty: number;
    readyToList?: boolean;
    note?: string | null;
    weight?: number | null;
    length?: number | null;
    width?: number | null;
    height?: number | null;
    imagePath: string | null;
    imageSource?: 'local' | 'remote';
  }): Promise<ProductDetail> {
    return firstValueFrom(this.http.put<ProductDetail>(`${this.baseUrl}/products/${id}`, body));
  }

  uploadProductImage(productId: number, file: File): Promise<{ imagePath: string }> {
    const data = new FormData();
    data.append('file', file);
    return firstValueFrom(this.http.post<{ imagePath: string }>(
      `${this.baseUrl}/products/${productId}/image/upload`,
      data,
    ));
  }

  uploadProductImages(productId: number, files: File[]): Promise<ProductMediaUploadResult> {
    return this.mediaUpload.uploadMultipart<ProductMediaUploadResult>(
      `${this.baseUrl}/products/${productId}/image/upload-many`, files);
  }

  async uploadCapturedProductVideo(productId: number, file: File): Promise<ProductMediaUploadResult> {
    return this.mediaUpload.uploadCapturedVideo<ProductMediaUploadResult>(
      `${this.baseUrl}/products/${productId}/image/upload-capture`, file);
  }

  productImageFiles(productId: number): Promise<ProductImageFile[]> {
    return firstValueFrom(this.http.get<ProductImageFile[]>(`${this.baseUrl}/products/${productId}/image/files`));
  }

  productImageFileUrl(productId: number, fileName: string): string {
    return `${this.baseUrl}/products/${productId}/image/files/${encodeURIComponent(fileName)}`;
  }

  deleteProductMedia(productId: number, fileName: string): Promise<{ deleted: string; primaryCleared: boolean }> {
    return firstValueFrom(this.http.delete<{ deleted: string; primaryCleared: boolean }>(
      `${this.baseUrl}/products/${productId}/image/files/${encodeURIComponent(fileName)}`,
    ));
  }

  productMediaDownloadUrl(productId: number): string {
    return `${this.baseUrl}/products/${productId}/image/files/download-all-media`;
  }

  setPrimaryProductImage(productId: number, fileName: string): Promise<{ imagePath: string }> {
    return firstValueFrom(this.http.post<{ imagePath: string }>(
      `${this.baseUrl}/products/${productId}/image/primary`,
      { fileName },
    ));
  }

  openProductImageFolder(productId: number): Promise<{ folder: string }> {
    return firstValueFrom(this.http.post<{ folder: string }>(
      `${this.baseUrl}/products/${productId}/image/open-folder`,
      {},
    ));
  }

  deleteProduct(id: number): Promise<unknown> {
    return firstValueFrom(this.http.delete(`${this.baseUrl}/products/${id}`));
  }

  saveListing(productId: number, body: { platform: Platform; listingUrl: string | null; retailPrice?: number | null }): Promise<ProductListing> {
    return firstValueFrom(this.http.post<ProductListing>(`${this.baseUrl}/products/${productId}/listings`, body));
  }

  deleteListing(productId: number, listingId: number): Promise<unknown> {
    return firstValueFrom(this.http.delete(`${this.baseUrl}/products/${productId}/listings/${listingId}`));
  }

  createAsin(productId: number, body: {
    asin: string;
    asinType: 'parent' | 'child';
    parentAsin: string | null;
    amazonAttributeDone: boolean;
  }): Promise<AmazonAsin> {
    return firstValueFrom(this.http.post<AmazonAsin>(`${this.baseUrl}/products/${productId}/amazon-asins`, body));
  }

  deleteAsin(productId: number, asinId: number): Promise<unknown> {
    return firstValueFrom(this.http.delete(`${this.baseUrl}/products/${productId}/amazon-asins/${asinId}`));
  }

  saveAttribution(productId: number, platform: AttributionPlatform, attributionUrl: string, amazonAsinId: number | null = null): Promise<AmazonAttributionLink> {
    return firstValueFrom(this.http.put<AmazonAttributionLink>(
      `${this.baseUrl}/amazon-marketing/products/${productId}/attributions/${platform}`,
      { attributionUrl, amazonAsinId },
    ));
  }

  importAttribution(productId: number, file: File, amazonAsinId?: number): Promise<unknown> {
    const data = new FormData();
    data.append('file', file);
    if (amazonAsinId) data.append('amazonAsinId', String(amazonAsinId));
    return firstValueFrom(this.http.post(`${this.baseUrl}/products/${productId}/amazon-attributions/import`, data));
  }

  importBusinessReport(file: File): Promise<BusinessReportImportResult> {
    const data = new FormData();
    data.append('file', file);
    return firstValueFrom(this.http.post<BusinessReportImportResult>(
      `${this.baseUrl}/imports/amazon-business-report`,
      data,
    ));
  }

  importBulkAttributions(file: File): Promise<BulkAttributionImportResult> {
    const data = new FormData();
    data.append('file', file);
    return firstValueFrom(this.http.post<BulkAttributionImportResult>(
      `${this.baseUrl}/imports/amazon-attributions`,
      data,
    ));
  }

  importAttributionFolder(files: File[]): Promise<AttributionFolderImportResult> {
    const data = new FormData();
    for (const file of files) data.append('files', file, file.name);
    return firstValueFrom(this.http.post<AttributionFolderImportResult>(
      `${this.baseUrl}/imports/amazon-attribution-files`,
      data,
    ));
  }

  matchImages(sku: string, folderPath: string, mode: 'folder' | 'sku' = 'sku'): Promise<ImageMatchResult> {
    return firstValueFrom(this.http.post<ImageMatchResult>(`${this.baseUrl}/images/match`, { sku, folderPath, mode }));
  }

  findListingImage(listingUrl: string): Promise<{ imageUrl: string }> {
    return firstValueFrom(this.http.post<{ imageUrl: string }>(
      `${this.baseUrl}/images/from-listing`,
      { listingUrl },
    ));
  }

  imagePreviewUrl(path: string): string {
    const trimmed = path.trim();
    if (/^https?:\/\//i.test(trimmed)) {
      return `${this.baseUrl}/images/remote-preview?url=${encodeURIComponent(trimmed)}`;
    }

    return `${this.baseUrl}/images/preview?path=${encodeURIComponent(trimmed)}`;
  }

  imageDownloadUrl(path: string): string {
    return `${this.baseUrl}/images/preview?path=${encodeURIComponent(path.trim())}&download=true`;
  }

  browseImageHost(path: string): Promise<ImageHostBrowseResult> {
    return firstValueFrom(this.http.get<ImageHostBrowseResult>(`${this.baseUrl}/images/host/browse`, {
      params: { path },
    }));
  }

  imageStorageRoots(): Promise<ImageStorageRoot[]> {
    return firstValueFrom(this.http.get<ImageStorageRoot[]>(`${this.baseUrl}/images/roots`));
  }

  addImageStorageRoot(path: string): Promise<ImageStorageRoot> {
    return firstValueFrom(this.http.post<ImageStorageRoot>(`${this.baseUrl}/images/roots`, { path }));
  }

  deleteImageStorageRoot(path: string): Promise<unknown> {
    return firstValueFrom(this.http.delete(`${this.baseUrl}/images/roots`, {
      params: { path },
    }));
  }

  productImageUrl(productId: number, download = false, thumbnailSize?: number): string {
    const params = new URLSearchParams();
    // Product images are API responses with their own HTTP cache headers. Sending
    // them through Angular's service worker adds an unnecessary fetch proxy and
    // can leave a group of first-load images queued in installed PWA mode.
    params.set('ngsw-bypass', 'true');
    if (download) params.set('download', 'true');
    if (thumbnailSize) {
      params.set('thumbnail', String(thumbnailSize));
      params.set('thumbnailVersion', '1');
    }
    const query = params.toString();
    return `${this.baseUrl}/products/${productId}/image${query ? `?${query}` : ''}`;
  }

  useAmazonImage(productId: number, listingUrl: string | null = null): Promise<{ imageUrl: string }> {
    return firstValueFrom(this.http.post<{ imageUrl: string }>(
      `${this.baseUrl}/products/${productId}/image/from-amazon`,
      { listingUrl },
    ));
  }

  useListingImage(productId: number, listingUrl: string | null = null, platform: Platform | null = null): Promise<{ imageUrl: string }> {
    return firstValueFrom(this.http.post<{ imageUrl: string }>(
      `${this.baseUrl}/products/${productId}/image/from-listing`,
      { listingUrl, platform },
    ));
  }

  storeLinks(): Promise<StoreLink[]> {
    return firstValueFrom(this.http.get<StoreLink[]>(`${this.baseUrl}/store-links`));
  }

  saveStoreLink(platform: Platform, url: string | null): Promise<StoreLink> {
    return firstValueFrom(this.http.put<StoreLink>(`${this.baseUrl}/store-links/${platform}`, { url }));
  }

  rmbToUsdRate(): Promise<RmbToUsdRate> {
    return firstValueFrom(this.http.get<RmbToUsdRate>(`${this.baseUrl}/exchange-rates/rmb-to-usd`));
  }
}

export interface ProductImageFile {
  fileName: string;
  mediaType: 'image' | 'video';
  isVideo: boolean;
  isPrimary: boolean;
  updatedAt: string;
}

export interface ProductMediaUploadResult {
  imagePath: string | null;
  imagePaths: string[];
  mediaPaths: string[];
  imageCount: number;
  videoCount: number;
  count: number;
}
