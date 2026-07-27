import { CommonModule } from '@angular/common';
import { Component, computed, ElementRef, HostListener, inject, signal, ViewChild } from '@angular/core';
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
  @ViewChild('warehouseStage') warehouseStage?: ElementRef<HTMLElement>;
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
  drag: { shelfId: number; pointerX: number; pointerY: number; startX: number; startY: number; moved: boolean } | null = null;
  private suppressShelfClick = false;

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
    if (this.suppressShelfClick) {
      this.suppressShelfClick = false;
      return;
    }
    const detail = await this.run(async () => this.api.getShelf(shelf.id));
    if (!detail) return;
    this.selectedShelf.set(detail);
    this.selectedLevel.set(null);
    this.shelfOpen.set(true);
  }

  startDrag(event: PointerEvent, shelf: WarehouseShelf): void {
    if (event.button !== 0) return;
    event.preventDefault();
    this.drag = {
      shelfId: shelf.id,
      pointerX: event.clientX,
      pointerY: event.clientY,
      startX: shelf.x,
      startY: shelf.y,
      moved: false,
    };
    (event.currentTarget as HTMLElement).setPointerCapture?.(event.pointerId);
  }

  @HostListener('document:pointermove', ['$event'])
  moveShelf(event: PointerEvent): void {
    if (!this.drag || !this.warehouseStage) return;
    const rect = this.warehouseStage.nativeElement.getBoundingClientRect();
    const warehouse = this.warehouse();
    const shelf = warehouse?.shelves.find(item => item.id === this.drag!.shelfId);
    if (!warehouse || !shelf || !rect.width || !rect.height) return;

    const dx = ((event.clientX - this.drag.pointerX) / rect.width) * 100;
    const dy = ((event.clientY - this.drag.pointerY) / rect.height) * 100;
    if (Math.abs(dx) + Math.abs(dy) > .35) this.drag.moved = true;
    const width = shelf.rotation === 90 ? shelf.height : shelf.width;
    const height = shelf.rotation === 90 ? shelf.width : shelf.height;
    let x = this.snap(this.drag.startX + dx, shelf, 'x');
    let y = this.snap(this.drag.startY + dy, shelf, 'y');
    x = Math.max(0, Math.min(100 - width, x));
    y = Math.max(0, Math.min(100 - height, y));
    this.replaceShelf({ ...shelf, x, y });
  }

  @HostListener('document:pointerup')
  async finishDrag(): Promise<void> {
    if (!this.drag) return;
    const drag = this.drag;
    this.drag = null;
    if (!drag.moved) return;
    this.suppressShelfClick = true;
    const shelf = this.warehouse()?.shelves.find(item => item.id === drag.shelfId);
    if (shelf) await this.persistLayout(shelf, false);
  }

  async rotateShelf(event: MouseEvent, shelf: WarehouseShelf): Promise<void> {
    event.stopPropagation();
    const rotation = shelf.rotation === 90 ? 0 : 90;
    const width = rotation === 90 ? shelf.height : shelf.width;
    const height = rotation === 90 ? shelf.width : shelf.height;
    const rotated = {
      ...shelf,
      rotation,
      x: Math.min(shelf.x, 100 - width),
      y: Math.min(shelf.y, 100 - height),
    };
    this.replaceShelf(rotated);
    await this.persistLayout(rotated, true);
  }

  effectiveWidth(shelf: WarehouseShelf): number {
    return shelf.rotation === 90 ? shelf.height : shelf.width;
  }

  effectiveHeight(shelf: WarehouseShelf): number {
    return shelf.rotation === 90 ? shelf.width : shelf.height;
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

  private snap(value: number, shelf: WarehouseShelf, axis: 'x' | 'y'): number {
    const gridValue = Math.round(value / 2) * 2;
    const ownSize = axis === 'x' ? this.effectiveWidth(shelf) : this.effectiveHeight(shelf);
    const candidates = this.warehouse()?.shelves
      .filter(item => item.id !== shelf.id)
      .flatMap(item => {
        const size = axis === 'x' ? this.effectiveWidth(item) : this.effectiveHeight(item);
        const start = axis === 'x' ? item.x : item.y;
        const end = start + size;
        return [start, end, start + size / 2 - ownSize / 2, start - ownSize, end - ownSize];
      }) ?? [];
    const aligned = candidates.find(candidate => Math.abs(candidate - value) <= 1.2);
    return aligned ?? gridValue;
  }

  private replaceShelf(updated: WarehouseShelf): void {
    const warehouse = this.warehouse();
    if (!warehouse) return;
    this.warehouse.set({
      ...warehouse,
      shelves: warehouse.shelves.map(item => item.id === updated.id ? updated : item),
    });
  }

  private async persistLayout(shelf: WarehouseShelf, showMessage: boolean): Promise<void> {
    const updated = await this.run(() => this.api.updateShelfLayout(shelf.id, shelf.x, shelf.y, shelf.rotation));
    if (updated) this.replaceShelf(updated);
    if (updated && showMessage) this.showNotice(`${updated.code} rotated ${updated.rotation}°.`);
  }

  private showNotice(message: string): void {
    this.notice.set(message);
    window.setTimeout(() => this.notice.set(''), 2600);
  }
}
