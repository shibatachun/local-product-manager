import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { apiBaseUrl } from './api-base';

export interface LabelPrintingSettings {
  enabled: boolean;
  provider: string;
  endpointUrl: string;
  templateName: string;
  printerName: string;
  timeoutSeconds: number;
}

export interface LabelPrintItem {
  recordType: string;
  recordId: string;
  title?: string | null;
  sku?: string | null;
  copies?: number;
}

export interface LabelPrintResult { accepted: boolean; message: string; }

@Injectable({ providedIn: 'root' })
export class LabelPrintingApiService {
  private readonly http = inject(HttpClient);
  private readonly base = `${apiBaseUrl()}/label-printing`;

  settings(): Promise<LabelPrintingSettings> {
    return firstValueFrom(this.http.get<LabelPrintingSettings>(`${this.base}/settings`));
  }
  saveSettings(settings: LabelPrintingSettings): Promise<LabelPrintingSettings> {
    return firstValueFrom(this.http.put<LabelPrintingSettings>(`${this.base}/settings`, settings));
  }
  test(): Promise<LabelPrintResult> {
    return firstValueFrom(this.http.post<LabelPrintResult>(`${this.base}/test`, {}));
  }
  print(items: LabelPrintItem[]): Promise<LabelPrintResult> {
    return firstValueFrom(this.http.post<LabelPrintResult>(`${this.base}/print`, { items }));
  }
  printBidProduct(id: number, copies: number): Promise<LabelPrintResult> {
    return firstValueFrom(this.http.post<LabelPrintResult>(`${this.base}/bid-products/${id}`, { copies }));
  }
}
