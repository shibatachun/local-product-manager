import { CommonModule, Location } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { ProductApiService, ProductImageFile } from '../../core/product-api.service';
import { InventoryApiService, ProductScannerLocation } from '../../core/inventory-api.service';
import { ProductDetail, ProductExtendedData } from '../../core/models';
import { AuthApiService } from '../../core/auth-api.service';
import { MediaUploadService } from '../../core/media-upload.service';

@Component({
  selector: 'app-product-details-page',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './product-details.page.html',
  styleUrl: './product-details.page.css',
})
export class ProductDetailsPage {
  private readonly route = inject(ActivatedRoute);
  private readonly api = inject(ProductApiService);
  private readonly location = inject(Location);
  private readonly inventoryApi = inject(InventoryApiService);
  private readonly authApi = inject(AuthApiService);
  private readonly mediaUpload = inject(MediaUploadService);
  readonly product = signal<ProductDetail | null>(null);
  readonly record = signal<ProductExtendedData | null>(null);
  readonly locations = signal<ProductScannerLocation[]>([]);
  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly message = signal('');
  readonly canEdit = signal(false);
  readonly canOpenLocalFolder = signal(false);
  readonly images = signal<ProductImageFile[]>([]);

  constructor() { void this.load(); }

  async load(): Promise<void> {
    const id = Number(this.route.snapshot.paramMap.get('productId'));
    if (!Number.isFinite(id)) { this.loading.set(false); return; }
    try {
      try {
        const user = await this.authApi.me();
        this.canEdit.set(user.canEditProductDetails);
        this.canOpenLocalFolder.set(user.isAdmin);
      } catch {
        this.canEdit.set(false);
        this.canOpenLocalFolder.set(false);
      }
      this.product.set(await this.api.getProduct(id));
      const [recordResult, locationResult, imageResult] = await Promise.allSettled([
        this.api.getExtendedData(id),
        this.inventoryApi.productLocations(id),
        this.api.productImageFiles(id),
      ]);
      if (recordResult.status === 'fulfilled') this.record.set(recordResult.value);
      else {
        this.record.set(this.emptyRecord());
        this.message.set('The extended product fields could not be loaded. Restart the API service to apply the new database tables.');
      }
      if (locationResult.status === 'fulfilled') this.locations.set(locationResult.value.filter(item => item.qty > 0));
      if (imageResult.status === 'fulfilled') this.images.set(imageResult.value);
    } catch { this.message.set('Unable to load this product. The API service may be unavailable.'); }
    finally { this.loading.set(false); }
  }

  private emptyRecord(): ProductExtendedData {
    return {
      details: { externalProductCode: null, variantName: null, material: null, specification: null, unit: null, style: null, description: null, developmentDate: null },
      identifiers: [], suppliers: [], prices: [],
      compliance: { declarationName: null, countryOfOrigin: null, hsCode: null, declaredValue: null, declaredValueCurrency: null, containsBattery: null, containsPowder: null, containsLiquid: null, containsMagnet: null, shippingMethod: null, expirationDate: null },
      packaging: { packQuantity: null, packageLength: null, packageWidth: null, packageHeight: null, dimensionUnit: 'in', packageWeight: null, cartonWeight: null, billableWeight: null, weightUnit: 'oz', packagingCost: null, packagingCostCurrency: null, packagingDifficulty: null, labelRequirements: null },
    };
  }

  back(): void { this.location.back(); }
  imageUrl(product: ProductDetail): string { return this.api.productImageUrl(product.id, false, 512); }
  folderImageUrl(productId: number, fileName: string): string { return this.api.productImageFileUrl(productId, fileName); }
  mediaDownloadUrl(productId: number): string { return this.api.productMediaDownloadUrl(productId); }
  async openLocalImageFolder(): Promise<void> {
    const product = this.product();
    if (!product || !this.canOpenLocalFolder()) return;
    try {
      await this.api.openProductImageFolder(product.id);
      this.message.set('Product image folder opened on this computer.');
    } catch {
      this.message.set('Unable to open the product image folder. This action is only available to an administrator.');
    }
  }
  async uploadPhotos(event: Event, cameraPhoto = false, cameraVideo = false): Promise<void> {
    const { input, files: selectedFiles } = this.mediaUpload.filesFrom(event);
    const product = this.product();
    if (!product) return;
    if (!selectedFiles.length) {
      this.message.set('The camera or media library did not return a photo or video. Please try again.');
      input.value = '';
      return;
    }
    this.saving.set(true);
    try {
      const files = cameraPhoto
        ? await this.mediaUpload.prepareCameraImages(selectedFiles)
        : selectedFiles;
      const result = cameraVideo && files.length === 1
        ? await this.api.uploadCapturedProductVideo(product.id, files[0])
        : await this.api.uploadProductImages(product.id, files);
      this.images.set(await this.api.productImageFiles(product.id));
      this.product.set(await this.api.getProduct(product.id));
      const parts = [];
      if (result.imageCount) parts.push(`${result.imageCount} photo${result.imageCount === 1 ? '' : 's'}`);
      if (result.videoCount) parts.push(`${result.videoCount} video${result.videoCount === 1 ? '' : 's'}`);
      this.message.set(`${parts.join(' and ')} saved.`);
    } catch { this.message.set('Media upload failed. Check the file type and size.'); }
    finally { this.saving.set(false); input.value = ''; }
  }
  async makePrimary(image: ProductImageFile): Promise<void> {
    const product = this.product();
    if (!product || image.isVideo || image.isPrimary) return;
    this.saving.set(true);
    try {
      await this.api.setPrimaryProductImage(product.id, image.fileName);
      this.images.set(await this.api.productImageFiles(product.id));
      this.product.set(await this.api.getProduct(product.id));
      this.message.set('Main product image updated.');
    } catch { this.message.set('Unable to update the main product image.'); }
    finally { this.saving.set(false); }
  }
  addIdentifier(): void { this.record()!.identifiers.push({ identifierType: 'UPC', value: '', isPrimary: false, notes: null }); }
  removeIdentifier(index: number): void { this.record()!.identifiers.splice(index, 1); }
  addSupplier(): void { this.record()!.suppliers.push({ name: '', contactName: null, contactDetails: null, procurementChannel: null, buyer: null, minimumPurchasePrice: null, minimumOrderQuantity: null, currency: 'CNY', isPreferred: false, provenance: null, procurementLinks: [] }); }
  removeSupplier(index: number): void { this.record()!.suppliers.splice(index, 1); }
  addLink(supplierIndex: number): void { this.record()!.suppliers[supplierIndex].procurementLinks.push({ url: '', sortOrder: this.record()!.suppliers[supplierIndex].procurementLinks.length, source: null }); }
  removeLink(supplierIndex: number, linkIndex: number): void { this.record()!.suppliers[supplierIndex].procurementLinks.splice(linkIndex, 1); }
  addPrice(): void { this.record()!.prices.push({ priceType: 'purchase', currency: 'USD', amount: 0, quantityThreshold: null, effectiveFrom: null, effectiveTo: null, source: null }); }
  removePrice(index: number): void { this.record()!.prices.splice(index, 1); }

  async save(): Promise<void> {
    if (!this.canEdit()) { this.message.set('You do not have permission to edit detailed product information.'); return; }
    const product = this.product(), record = this.record();
    if (!product || !record) return;
    this.saving.set(true); this.message.set('');
    try { this.record.set(await this.api.saveExtendedData(product.id, record)); this.message.set('Product record saved.'); }
    catch { this.message.set('Save failed. Please check the fields and try again.'); }
    finally { this.saving.set(false); }
  }
}
