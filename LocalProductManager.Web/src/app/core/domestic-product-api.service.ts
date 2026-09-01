import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { apiBaseUrl } from './api-base';

export interface DomesticProduct {
  id: number; sku: string; productCode: string | null; productName: string | null;
  material: string | null; specification: string | null; costRmb: number | null;
  minimumSalePriceUsd: number | null; warehouseInventory: number | null;
  supplierName: string | null; chineseDeclarationName: string | null;
  englishDeclarationName: string | null; weightGrams: number | null;
  imagePath: string | null; details: Record<string, string>; sourceFileName: string | null;
  importedAt: string; updatedAt: string;
}

export interface DomesticProductSearchResult {
  items: DomesticProduct[]; total: number; page: number; pageSize: number; skuSortDirection: 'asc' | 'desc';
}

export interface DomesticProductImportResult {
  processed: number; created: number; updated: number; skipped: number; sourceFileName: string;
}

@Injectable({ providedIn: 'root' })
export class DomesticProductApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${apiBaseUrl()}/domestic-products`;

  search(query: string, skuSort: 'asc' | 'desc', page: number, pageSize = 100): Promise<DomesticProductSearchResult> {
    return firstValueFrom(this.http.get<DomesticProductSearchResult>(this.baseUrl, {
      params: { query, skuSort, page, pageSize },
    }));
  }

  import(file: File): Promise<DomesticProductImportResult> {
    const form = new FormData(); form.append('file', file);
    return firstValueFrom(this.http.post<DomesticProductImportResult>(`${this.baseUrl}/import`, form));
  }
}
