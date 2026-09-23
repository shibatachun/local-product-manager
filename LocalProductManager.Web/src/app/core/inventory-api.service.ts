import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ProductListItem, ShelfDetail, ShelfLevel, Warehouse, WarehouseShelf } from './models';

export interface ProductScannerLocation {
  levelId: number;
  binNumber: number;
  key: string;
  code: string;
  description: string;
  qty: number;
}

export interface InventoryHistoryEntry {
  id: number;
  productId: number;
  localInventoryId: string | null;
  sku: string;
  productName: string;
  shelfLevelId: number;
  binNumber: number;
  locationCode: string;
  locationDescription: string;
  operation: 'stock_in' | 'stock_out';
  beforeQty: number;
  afterQty: number;
  delta: number;
  userName: string;
  userRole: string;
  remoteIp: string | null;
  userAgent: string | null;
  source: 'set' | 'adjust' | 'bid_transfer' | 'place_unassigned' | 'move_to_unassigned' | `move_out:${number}:${number}` | `move_in:${number}:${number}`;
  createdAt: string;
}
import { apiBaseUrl } from './api-base';

@Injectable({ providedIn: 'root' })
export class InventoryApiService {
  private readonly http = inject(HttpClient);
  readonly baseUrl = apiBaseUrl();

  async getWarehouse(): Promise<Warehouse | null> {
    try {
      return await firstValueFrom(this.http.get<Warehouse>(`${this.baseUrl}/inventory/warehouse`));
    } catch (error: any) {
      if (error?.status === 204) return null;
      throw error;
    }
  }

  createWarehouse(name: string): Promise<Warehouse> {
    return firstValueFrom(this.http.post<Warehouse>(`${this.baseUrl}/inventory/warehouses`, { name }));
  }

  createShelf(warehouseId: number, body: {
    zoneCode: string; rowNumber: number; levelCount: number; x: number; y: number;
  }): Promise<WarehouseShelf> {
    return firstValueFrom(this.http.post<WarehouseShelf>(
      `${this.baseUrl}/inventory/warehouses/${warehouseId}/shelves`, body));
  }

  getShelf(shelfId: number): Promise<ShelfDetail> {
    return firstValueFrom(this.http.get<ShelfDetail>(`${this.baseUrl}/inventory/shelves/${shelfId}`));
  }

  productLocations(productId: number): Promise<ProductScannerLocation[]> {
    return firstValueFrom(this.http.get<ProductScannerLocation[]>(
      `${this.baseUrl}/inventory/products/${productId}/locations`,
    ));
  }

  moveProduct(productId: number, body: {
    sourceLevelId: number | null; sourceBinNumber: number | null; targetLevelId: number | null; targetBinNumber: number | null; quantity: number;
  }): Promise<ProductScannerLocation[]> {
    return firstValueFrom(this.http.post<ProductScannerLocation[]>(
      `${this.baseUrl}/inventory/products/${productId}/move`, body));
  }

  addShelfLevel(shelfId: number): Promise<ShelfDetail> {
    return firstValueFrom(this.http.post<ShelfDetail>(`${this.baseUrl}/inventory/shelves/${shelfId}/levels`, {}));
  }

  deleteShelfLevel(levelId: number): Promise<ShelfDetail> {
    return firstValueFrom(this.http.delete<ShelfDetail>(`${this.baseUrl}/inventory/levels/${levelId}`));
  }

  deleteShelf(shelfId: number): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`${this.baseUrl}/inventory/shelves/${shelfId}`));
  }

  updateShelfLayout(shelfId: number, x: number, y: number, rotation: number): Promise<WarehouseShelf> {
    return firstValueFrom(this.http.put<WarehouseShelf>(
      `${this.baseUrl}/inventory/shelves/${shelfId}/layout`, { x, y, rotation }));
  }

  setInventory(levelId: number, productId: number, qty: number, binNumber = 1): Promise<ShelfLevel> {
    return firstValueFrom(this.http.put<ShelfLevel>(
      `${this.baseUrl}/inventory/levels/${levelId}/inventory`, { productId, qty, binNumber }));
  }

  adjustInventory(levelId: number, productId: number, delta: number, binNumber = 1): Promise<ShelfLevel> {
    return firstValueFrom(this.http.patch<ShelfLevel>(
      `${this.baseUrl}/inventory/levels/${levelId}/inventory`, { productId, delta, binNumber }));
  }

  history(search = '', operation = '', limit = 250): Promise<InventoryHistoryEntry[]> {
    return firstValueFrom(this.http.get<InventoryHistoryEntry[]>(`${this.baseUrl}/inventory/history`, {
      params: { search, operation, limit },
    }));
  }

  products(): Promise<ProductListItem[]> {
    return firstValueFrom(this.http.get<ProductListItem[]>(`${this.baseUrl}/products`));
  }
}
