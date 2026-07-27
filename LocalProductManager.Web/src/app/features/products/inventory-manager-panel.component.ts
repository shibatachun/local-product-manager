import { CommonModule } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { InventoryApiService } from '../../core/inventory-api.service';
import { ProductListItem, ShelfDetail, ShelfLevel, Warehouse, WarehouseShelf } from '../../core/models';

@Component({
  selector: 'app-inventory-manager-panel',
  imports: [CommonModule, FormsModule],
  templateUrl: './inventory-manager-panel.component.html',
  styleUrl: './inventory-manager-panel.component.css',
})
export class InventoryManagerPanelComponent {
  private readonly api = inject(InventoryApiService);
  readonly warehouse = signal<Warehouse | null>(null);
  readonly products = signal<ProductListItem[]>([]);
  readonly selectedShelf = signal<ShelfDetail | null>(null);
  readonly selectedLevel = signal<ShelfLevel | null>(null);
  readonly addShelfOpen = signal(false);
  readonly shelfOpen = signal(false);
  readonly loading = signal(false);
  readonly notice = signal('');
  readonly warehouseName = signal('Main Warehouse');
  readonly shelfForm = { zoneCode: 'A', aisleNumber: 1, levelCount: 5 };
  readonly inventoryForm = { productId: null as number | null, qty: 0 };
  readonly totalQty = computed(() => this.warehouse()?.shelves.reduce((sum, shelf) => sum + shelf.totalQty, 0) ?? 0);

  constructor() {
    void this.load();
  }

  async load(): Promise<void> {
    await this.run(async () => {
      this.warehouse.set(await this.api.getWarehouse());
      this.products.set(await this.api.products());
    });
  }

  async createWarehouse(): Promise<void> {
    await this.run(async () => {
      this.warehouse.set(await this.api.createWarehouse(this.warehouseName()));
      this.showNotice('Warehouse created.');
    });
  }

  openAddShelf(): void {
    this.addShelfOpen.set(true);
  }

  async createShelf(): Promise<void> {
    const warehouse = this.warehouse();
    if (!warehouse) return;
    const index = warehouse.shelves.length;
    const shelf = await this.run(async () => this.api.createShelf(warehouse.id, {
      zoneCode: this.shelfForm.zoneCode,
      aisleNumber: this.shelfForm.aisleNumber,
      levelCount: this.shelfForm.levelCount,
      x: 5 + (index % 4) * 23,
      y: 8 + Math.floor(index / 4) * 22,
    }));
    if (!shelf) return;
    this.warehouse.set({ ...warehouse, shelves: [...warehouse.shelves, shelf] });
    this.addShelfOpen.set(false);
    this.showNotice(`${shelf.code} created with ${shelf.levelCount} levels.`);
  }

  async openShelf(shelf: WarehouseShelf): Promise<void> {
    const detail = await this.run(async () => this.api.getShelf(shelf.id));
    if (!detail) return;
    this.selectedShelf.set(detail);
    this.selectedLevel.set(null);
    this.shelfOpen.set(true);
  }

  selectLevel(level: ShelfLevel): void {
    this.selectedLevel.set(level);
    this.inventoryForm.productId = level.items[0]?.productId ?? this.products()[0]?.id ?? null;
    this.inventoryForm.qty = level.items[0]?.qty ?? 0;
  }

  onProductChanged(productId: number | null): void {
    const row = this.selectedLevel()?.items.find(item => item.productId === Number(productId));
    this.inventoryForm.qty = row?.qty ?? 0;
  }

  async saveInventory(): Promise<void> {
    const level = this.selectedLevel();
    const productId = Number(this.inventoryForm.productId);
    if (!level || !productId) return;
    const updated = await this.run(async () => this.api.setInventory(level.id, productId, this.inventoryForm.qty));
    if (!updated) return;
    this.selectedLevel.set(updated);
    const shelf = this.selectedShelf();
    if (shelf) {
      this.selectedShelf.set({
        ...shelf,
        levels: shelf.levels.map(item => item.id === updated.id ? updated : item),
      });
    }
    await this.load();
    this.showNotice('Level inventory updated.');
  }

  private async run<T>(action: () => Promise<T>): Promise<T | undefined> {
    this.loading.set(true);
    try {
      return await action();
    } catch (error: any) {
      this.showNotice(error?.error || error?.message || 'Inventory request failed.');
      return undefined;
    } finally {
      this.loading.set(false);
    }
  }

  private showNotice(message: string): void {
    this.notice.set(message);
    window.setTimeout(() => this.notice.set(''), 2600);
  }
}
