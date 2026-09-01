import { ProductListItem } from './models';

export function productCategoryNumber(product: ProductListItem): string {
  return product.categoryInventoryId || product.inventoryCategory || 'UNKNOWN';
}
