import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { apiBaseUrl } from './api-base';
import { MediaUploadService } from './media-upload.service';

export interface LiveStreamBidProduct {
  id: number;
  sourceProductId: number | null;
  batchNumber: string | null;
  batchProductNumber: number | null;
  actualSku: string | null;
  requiresLocalLabel: boolean;
  whatnotAuctionSafePrice: number | null;
  itemCode: string | null;
  uuid: string;
  imagePath: string | null;
  title: string;
  quantity: number;
  inventory: number;
  actualInventory: number;
  unitPrice: number;
  weightGrams: number | null;
  weightOunces: number | null;
  material: string | null;
  totalCost: number | null;
  importedFileName: string | null;
  importedFileSha256: string | null;
  isSold: boolean;
  soldAt: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface LiveStreamBidDraftRow {
  sourceProductId: number | null; externalUuid: string | null; actualSku: string | null; title: string; quantity: number; expectedInventory: number; actualInventory: number;
  unitPrice: number; weightGrams: number | null; weightOunces: number | null;
  material: string | null; totalCost: number | null; whatnotAuctionSafePrice: number | null;
  priceType: 'per-unit' | 'per-package';
}

export interface LiveStreamBidImportResult {
  created: number;
  updated: number;
  skipped: number;
  totalRows: number;
  conflicts: LiveStreamBidImportConflict[];
  regeneratedUuids: LiveStreamBidRegeneratedUuid[];
}

export interface LiveStreamBidRegeneratedUuid {
  rowNumber: number;
  productId: number;
  originalUuid: string;
  newUuid: string;
  title: string;
}

export interface LiveStreamBidImportConflict {
  rowNumber: number;
  uuid: string | null;
  actualSku: string | null;
  title: string;
  reasons: string[];
}

export interface LiveStreamBidSale {
  id: number;
  showSessionId: number | null;
  bidProductId: number;
  productUuid: string;
  productTitle: string;
  quantity: number;
  cost: number | null;
  actualSalePriceUsd: number | null;
  salePriceRmb: number | null;
  exchangeRateRmbToUsd: number | null;
  exchangeRateDate: string | null;
  profitRmb: number | null;
  profitUsd: number | null;
  saleDate: string;
  soldAt: string;
  soldBy: string;
}
export interface CloudCsvFile { remotePath: string; fileName: string; alreadyImported: boolean; }

export interface LiveStreamBidShowSummary {
  id: number | null;
  date: string;
  showNumber: number;
  isActive: boolean;
  startedAt: string | null;
  endedAt: string | null;
  startedBy: string | null;
  endedBy: string | null;
  soldCount: number;
  inventorySold: number;
  totalCostRmb: number;
  totalCostUsd: number;
  actualRevenueUsd: number;
  actualRevenueRmb: number;
  realizedProfitRmb: number;
  realizedProfitUsd: number;
  sales: LiveStreamBidSale[];
}

export interface LiveStreamBidDailySummary {
  date: string;
  soldCount: number;
  inventorySold: number;
  totalCostRmb: number;
  totalCostUsd: number;
  actualRevenueUsd: number;
  actualRevenueRmb: number;
  realizedProfitRmb: number;
  realizedProfitUsd: number;
  shows: LiveStreamBidShowSummary[];
  sales: LiveStreamBidSale[];
}

@Injectable({ providedIn: 'root' })
export class LiveStreamBidProductApiService {
  private readonly http = inject(HttpClient);
  private readonly mediaUpload = inject(MediaUploadService);
  private readonly baseUrl = apiBaseUrl();

  list(): Promise<LiveStreamBidProduct[]> {
    return firstValueFrom(this.http.get<LiveStreamBidProduct[]>(`${this.baseUrl}/live-stream-bid-products`));
  }

  async lookup(uuid: string): Promise<LiveStreamBidProduct | null> {
    try {
      return await firstValueFrom(this.http.get<LiveStreamBidProduct>(`${this.baseUrl}/live-stream-bid-products/lookup`, { params: { uuid } }));
    } catch (error: any) {
      if (error?.status === 404) return null;
      throw error;
    }
  }

  confirmSale(id: number, actualSalePrice: number | null, showSessionId: number): Promise<LiveStreamBidSale> {
    return firstValueFrom(this.http.post<LiveStreamBidSale>(`${this.baseUrl}/live-stream-bid-products/${id}/confirm-sale`, { actualSalePrice, showSessionId }));
  }

  activeShowSession(): Promise<LiveStreamBidShowSummary | null> {
    return firstValueFrom(this.http.get<LiveStreamBidShowSummary | null>(`${this.baseUrl}/live-stream-bid-products/show-sessions/active`));
  }

  startShowSession(): Promise<LiveStreamBidShowSummary> {
    return firstValueFrom(this.http.post<LiveStreamBidShowSummary>(`${this.baseUrl}/live-stream-bid-products/show-sessions/start`, {}));
  }

  stopShowSession(id: number): Promise<LiveStreamBidShowSummary> {
    return firstValueFrom(this.http.post<LiveStreamBidShowSummary>(`${this.baseUrl}/live-stream-bid-products/show-sessions/${id}/stop`, {}));
  }

  adjustActualInventory(id: number, delta: number): Promise<LiveStreamBidProduct> {
    return firstValueFrom(this.http.post<LiveStreamBidProduct>(`${this.baseUrl}/live-stream-bid-products/${id}/actual-inventory`, { delta }));
  }

  today(): Promise<LiveStreamBidDailySummary> {
    return firstValueFrom(this.http.get<LiveStreamBidDailySummary>(`${this.baseUrl}/live-stream-bid-products/sales/today`));
  }

  salesHistory(): Promise<LiveStreamBidDailySummary[]> {
    return firstValueFrom(this.http.get<LiveStreamBidDailySummary[]>(`${this.baseUrl}/live-stream-bid-products/sales/history`));
  }

  salesByDate(date: string): Promise<LiveStreamBidDailySummary> {
    return firstValueFrom(this.http.get<LiveStreamBidDailySummary>(`${this.baseUrl}/live-stream-bid-products/sales/by-date`, { params: { date } }));
  }

  deleteSale(id: number): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`${this.baseUrl}/live-stream-bid-products/sales/${id}`));
  }

  deleteSalesByDate(date: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`${this.baseUrl}/live-stream-bid-products/sales/by-date`, { params: { date } }));
  }

  importCsv(file: File): Promise<LiveStreamBidImportResult> {
    const data = new FormData();
    data.append('file', file);
    return firstValueFrom(this.http.post<LiveStreamBidImportResult>(`${this.baseUrl}/live-stream-bid-products/import`, data));
  }

  moveFromInventory(body: { productId: number; levelId: number; binNumber: number; quantity: number }): Promise<LiveStreamBidProduct> {
    return firstValueFrom(this.http.post<LiveStreamBidProduct>(`${this.baseUrl}/live-stream-bid-products/move-from-inventory`, body));
  }

  createBatch(rows: LiveStreamBidDraftRow[], external?: { fileName: string; sha256: string }): Promise<{ batchNumber: string; products: LiveStreamBidProduct[]; regeneratedUuids: LiveStreamBidRegeneratedUuid[] }> {
    return firstValueFrom(this.http.post<{ batchNumber: string; products: LiveStreamBidProduct[]; regeneratedUuids: LiveStreamBidRegeneratedUuid[] }>(`${this.baseUrl}/live-stream-bid-products/batches`, {
      rows,
      externalFileName: external?.fileName ?? null,
      externalFileSha256: external?.sha256 ?? null,
    }));
  }

  imageUrl(id: number, thumbnail = 160, version?: string): string {
    const suffix = version ? `&v=${encodeURIComponent(version)}` : '';
    return `${this.baseUrl}/live-stream-bid-products/${id}/image?thumbnail=${thumbnail}${suffix}`;
  }

  imageDownloadUrl(id: number): string {
    return `${this.baseUrl}/live-stream-bid-products/${id}/image?download=true`;
  }

  uploadImages(id: number, files: File[]): Promise<{ imagePath: string; updatedAt: string; uploadedCount: number }> {
    return this.mediaUpload.uploadMultipart<{ imagePath: string; updatedAt: string; uploadedCount: number }>(
      `${this.baseUrl}/live-stream-bid-products/${id}/image/upload-many`, files);
  }

  videoFiles(id: number): Promise<{ fileName: string; updatedAt: string }[]> {
    return firstValueFrom(this.http.get<{ fileName: string; updatedAt: string }[]>(
      `${this.baseUrl}/live-stream-bid-products/${id}/image/videos`));
  }

  videoUrl(id: number, fileName: string): string {
    return `${this.baseUrl}/live-stream-bid-products/${id}/image/videos/${encodeURIComponent(fileName)}`;
  }

  uploadVideos(id: number, files: File[]): Promise<{ videoCount: number }> {
    return this.mediaUpload.uploadMultipart<{ videoCount: number }>(
      `${this.baseUrl}/live-stream-bid-products/${id}/image/videos/upload-many`, files);
  }

  uploadCapturedVideo(id: number, file: File): Promise<{ videoCount: number }> {
    return this.mediaUpload.uploadCapturedVideo<{ videoCount: number }>(
      `${this.baseUrl}/live-stream-bid-products/${id}/image/videos/upload-capture`, file);
  }

  openProductFolder(id: number): Promise<{ folder: string }> {
    return firstValueFrom(this.http.post<{ folder: string }>(
      `${this.baseUrl}/live-stream-bid-products/${id}/image/open-folder`, {}));
  }

  cloudFiles(token: string): Promise<CloudCsvFile[]> {
    return firstValueFrom(this.http.post<CloudCsvFile[]>(`${this.baseUrl}/live-stream-bid-products/cloud-files`, { token }));
  }

  downloadCloudFile(token: string, remotePath: string): Promise<Blob> {
    return firstValueFrom(this.http.post(`${this.baseUrl}/live-stream-bid-products/cloud-files/download`, { token, remotePath }, { responseType: 'blob' }));
  }

  addBatchProducts(batchNumber: string, rows: LiveStreamBidDraftRow[]): Promise<{ batchNumber: string; products: LiveStreamBidProduct[]; regeneratedUuids: LiveStreamBidRegeneratedUuid[] }> {
    return firstValueFrom(this.http.post<{ batchNumber: string; products: LiveStreamBidProduct[]; regeneratedUuids: LiveStreamBidRegeneratedUuid[] }>(`${this.baseUrl}/live-stream-bid-products/batches/${encodeURIComponent(batchNumber)}/products`, { rows }));
  }

  async exportBatch(batchNumber: string): Promise<void> {
    const blob = await firstValueFrom(this.http.get(`${this.baseUrl}/live-stream-bid-products/batches/${encodeURIComponent(batchNumber)}/bartender.csv`, { responseType: 'blob' }));
    const url = URL.createObjectURL(blob); const anchor = document.createElement('a');
    anchor.href = url; anchor.download = `${batchNumber}-bartender.csv`; anchor.click(); URL.revokeObjectURL(url);
  }

  deleteProduct(id: number): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`${this.baseUrl}/live-stream-bid-products/${id}`));
  }

  deleteBatch(batchNumber: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`${this.baseUrl}/live-stream-bid-products/batches/${encodeURIComponent(batchNumber)}`));
  }

  updateProduct(id: number, row: LiveStreamBidDraftRow): Promise<LiveStreamBidProduct> {
    return firstValueFrom(this.http.put<LiveStreamBidProduct>(`${this.baseUrl}/live-stream-bid-products/${id}`, row));
  }
}
