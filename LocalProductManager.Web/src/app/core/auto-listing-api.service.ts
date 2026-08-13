import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { apiBaseUrl } from './api-base';

export interface AutoListingPlatform {
  platform: 'ebay' | 'walmart' | 'etsy';
  label: string;
  status: string;
  enabled: boolean;
  message: string;
}

export interface EbayListingDraft {
  productId: number;
  sku: string;
  title: string;
  description: string;
  price: number | null;
  quantity: number;
  condition: string;
  marketplaceId: string;
  imagePath: string | null;
  missingFields: string[];
}

@Injectable({ providedIn: 'root' })
export class AutoListingApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = apiBaseUrl();
  platforms(): Promise<AutoListingPlatform[]> {
    return firstValueFrom(this.http.get<AutoListingPlatform[]>(`${this.baseUrl}/auto-listing/platforms`));
  }
  ebayDraft(productId: number): Promise<EbayListingDraft> {
    return firstValueFrom(this.http.post<EbayListingDraft>(`${this.baseUrl}/auto-listing/ebay/drafts/${productId}`, {}));
  }
}
