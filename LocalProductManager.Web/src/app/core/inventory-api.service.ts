import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ProductListItem, ShelfDetail, ShelfLevel, Warehouse, WarehouseShelf } from './models';

@Injectable({ providedIn: 'root' })
export class InventoryApiService {
  private readonly http = inject(HttpClient);
  readonly baseUrl = `${window.location.protocol}//${window.location.hostname}:5077/api`;

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
    zoneCode: string; aisleNumber: number; levelCount: number; x: number; y: number;
  }): Promise<WarehouseShelf> {
    return firstValueFrom(this.http.post<WarehouseShelf>(
      `${this.baseUrl}/inventory/warehouses/${warehouseId}/shelves`, body));
  }

  getShelf(shelfId: number): Promise<ShelfDetail> {
    return firstValueFrom(this.http.get<ShelfDetail>(`${this.baseUrl}/inventory/shelves/${shelfId}`));
  }

  setInventory(levelId: number, productId: number, qty: number): Promise<ShelfLevel> {
    return firstValueFrom(this.http.put<ShelfLevel>(
      `${this.baseUrl}/inventory/levels/${levelId}/inventory`, { productId, qty }));
  }

  products(): Promise<ProductListItem[]> {
    return firstValueFrom(this.http.get<ProductListItem[]>(`${this.baseUrl}/products`));
  }
}
