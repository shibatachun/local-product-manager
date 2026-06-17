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
  ProductListItem,
  ProductListing,
  StoreLink,
} from './models';

@Injectable({ providedIn: 'root' })
export class ProductApiService {
  private readonly http = inject(HttpClient);
  readonly baseUrl = `http://${globalThis.location?.hostname || 'localhost'}:5077/api`;

  search(keyword = '', platform = '', hasAmazon?: boolean): Promise<ProductListItem[]> {
    const query = new URLSearchParams();
    if (keyword.trim()) query.set('keyword', keyword.trim());
    if (platform) query.set('platform', platform);
    if (hasAmazon !== undefined) query.set('hasAmazon', String(hasAmazon));
    const suffix = query.size ? `?${query}` : '';
    return firstValueFrom(this.http.get<ProductListItem[]>(`${this.baseUrl}/products${suffix}`));
  }

  getProduct(id: number): Promise<ProductDetail> {
    return firstValueFrom(this.http.get<ProductDetail>(`${this.baseUrl}/products/${id}`));
  }

  createProduct(body: {
    sku: string;
    productName: string;
    category?: string | null;
    cost: number | null;
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
    sku: string;
    productName: string;
    category?: string | null;
    cost: number | null;
    weight?: number | null;
    length?: number | null;
    width?: number | null;
    height?: number | null;
    imagePath: string | null;
    imageSource?: 'local' | 'remote';
  }): Promise<ProductDetail> {
    return firstValueFrom(this.http.put<ProductDetail>(`${this.baseUrl}/products/${id}`, body));
  }

  deleteProduct(id: number): Promise<unknown> {
    return firstValueFrom(this.http.delete(`${this.baseUrl}/products/${id}`));
  }

  saveListing(productId: number, body: { platform: Platform; listingUrl: string | null }): Promise<ProductListing> {
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
      `${this.baseUrl}/products/${productId}/amazon-attributions/${platform}`,
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

  productImageUrl(productId: number, download = false): string {
    return `${this.baseUrl}/products/${productId}/image${download ? '?download=true' : ''}`;
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
}
