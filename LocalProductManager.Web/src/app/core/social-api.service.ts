import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { SocialAccount, SocialMetricSnapshot, SocialPlatform, SocialSyncResult, YouTubeAuthStatus } from './models';

@Injectable({ providedIn: 'root' })
export class SocialApiService {
  private readonly http = inject(HttpClient);
  readonly baseUrl = `http://${globalThis.location?.hostname || 'localhost'}:5077/api`;

  accounts(from?: string, to?: string): Promise<SocialAccount[]> {
    const query = new URLSearchParams();
    if (from) query.set('from', from);
    if (to) query.set('to', to);
    return firstValueFrom(this.http.get<SocialAccount[]>(
      `${this.baseUrl}/social-accounts${query.size ? `?${query}` : ''}`,
    ));
  }

  createAccount(body: {
    platform: SocialPlatform;
    accountName: string;
    accountHandle: string | null;
    accountUrl: string | null;
    color: string | null;
    isActive: boolean;
  }): Promise<SocialAccount> {
    return firstValueFrom(this.http.post<SocialAccount>(`${this.baseUrl}/social-accounts`, body));
  }

  saveMetric(accountId: number, body: {
    snapshotDate: string;
    clicks: number;
    likes: number;
    views: number;
    comments: number;
    shares: number;
    followers: number;
  }): Promise<SocialMetricSnapshot> {
    return firstValueFrom(this.http.post<SocialMetricSnapshot>(
      `${this.baseUrl}/social-accounts/${accountId}/metrics`,
      body,
    ));
  }

  sync(): Promise<SocialSyncResult> {
    return firstValueFrom(this.http.post<SocialSyncResult>(`${this.baseUrl}/social-accounts/sync`, {}));
  }

  youtubeStatus(): Promise<YouTubeAuthStatus> {
    return firstValueFrom(this.http.get<YouTubeAuthStatus>(`${this.baseUrl}/youtube/auth/status`));
  }

  youtubeAuthStart(): Promise<{ authorizationUrl: string; redirectUri: string }> {
    return firstValueFrom(this.http.get<{ authorizationUrl: string; redirectUri: string }>(`${this.baseUrl}/youtube/auth/start`));
  }

  youtubeSync(): Promise<SocialSyncResult> {
    return firstValueFrom(this.http.post<SocialSyncResult>(`${this.baseUrl}/youtube/sync`, {}));
  }
}
