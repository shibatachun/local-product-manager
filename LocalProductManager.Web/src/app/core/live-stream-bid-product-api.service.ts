import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { apiBaseUrl } from './api-base';

export interface LiveStreamBidProduct {
  id: number;
  sourceProductId: number | null;
  uuid: string;
  title: string;
  quantity: number;
  inventory: number;
  unitPrice: number;
  weightGrams: number | null;
  weightOunces: number | null;
  material: string | null;
  totalCost: number | null;
  importedFileName: string | null;
  isSold: boolean;
  soldAt: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface LiveStreamBidImportResult {
  created: number;
  updated: number;
  skipped: number;
  totalRows: number;
}

export interface LiveStreamBidSale {
  id: number;
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

export interface LiveStreamBidDailySummary {
  date: string;
  soldCount: number;
  totalUnits: number;
  totalCostRmb: number;
  totalCostUsd: number;
  actualRevenueUsd: number;
  actualRevenueRmb: number;
  realizedProfitRmb: number;
  realizedProfitUsd: number;
  sales: LiveStreamBidSale[];
}

@Injectable({ providedIn: 'root' })
export class LiveStreamBidProductApiService {
  private readonly http = inject(HttpClient);
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

  confirmSale(id: number, actualSalePrice: number | null): Promise<LiveStreamBidSale> {
    return firstValueFrom(this.http.post<LiveStreamBidSale>(`${this.baseUrl}/live-stream-bid-products/${id}/confirm-sale`, { actualSalePrice }));
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
}
