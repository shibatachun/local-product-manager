import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ArchiveLogResult, FacebookContentSnapshot, FacebookReportImportResult, InstagramContentSnapshot, InstagramReportImportResult, MetaAuthStatus, PinterestAuthStatus, PinterestContentSnapshot, PinterestReportImportResult, SocialAccount, SocialAiAnalysisReport, SocialAiAnalysisReportContent, SocialAiAnalysisResult, SocialAiAnalysisStatus, SocialDebugSnapshot, SocialFetchLogEntry, SocialMetricSnapshot, SocialPlatform, SocialSyncResult, YouTubeAuthStatus, YouTubeDebugSnapshot } from './models';

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

  deleteAccount(accountId: number): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`${this.baseUrl}/social-accounts/${accountId}`));
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

  socialDebug(): Promise<SocialDebugSnapshot> {
    return firstValueFrom(this.http.get<SocialDebugSnapshot>(`${this.baseUrl}/social-accounts/debug`));
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

  youtubeDisconnect(): Promise<void> {
    return firstValueFrom(this.http.post<void>(`${this.baseUrl}/youtube/auth/disconnect`, {}));
  }

  youtubeVideosDebug(): Promise<YouTubeDebugSnapshot> {
    return firstValueFrom(this.http.get<YouTubeDebugSnapshot>(`${this.baseUrl}/youtube/videos/debug`));
  }

  youtubeStoredVideos(): Promise<YouTubeDebugSnapshot> {
    return firstValueFrom(this.http.get<YouTubeDebugSnapshot>(`${this.baseUrl}/youtube/videos`));
  }

  pinterestStatus(): Promise<PinterestAuthStatus> {
    return firstValueFrom(this.http.get<PinterestAuthStatus>(`${this.baseUrl}/pinterest/auth/status`));
  }

  pinterestAuthStart(): Promise<{ authorizationUrl: string; redirectUri: string }> {
    return firstValueFrom(this.http.get<{ authorizationUrl: string; redirectUri: string }>(`${this.baseUrl}/pinterest/auth/start`));
  }

  pinterestSync(): Promise<SocialSyncResult> {
    return firstValueFrom(this.http.post<SocialSyncResult>(`${this.baseUrl}/pinterest/sync`, {}));
  }

  pinterestDisconnect(): Promise<void> {
    return firstValueFrom(this.http.post<void>(`${this.baseUrl}/pinterest/auth/disconnect`, {}));
  }

  pinterestImportReport(file: File): Promise<PinterestReportImportResult> {
    const data = new FormData();
    data.append('file', file);
    return firstValueFrom(this.http.post<PinterestReportImportResult>(`${this.baseUrl}/pinterest/report/import`, data));
  }

  pinterestPins(accountId?: number | null): Promise<PinterestContentSnapshot> {
    const query = accountId ? `?accountId=${accountId}` : '';
    return firstValueFrom(this.http.get<PinterestContentSnapshot>(`${this.baseUrl}/pinterest/pins${query}`));
  }

  metaStatus(): Promise<MetaAuthStatus> {
    return firstValueFrom(this.http.get<MetaAuthStatus>(`${this.baseUrl}/meta/auth/status`));
  }

  metaAuthStart(): Promise<{ authorizationUrl: string; redirectUri: string }> {
    return firstValueFrom(this.http.get<{ authorizationUrl: string; redirectUri: string }>(`${this.baseUrl}/meta/auth/start`));
  }

  metaSync(): Promise<SocialSyncResult> {
    return firstValueFrom(this.http.post<SocialSyncResult>(`${this.baseUrl}/meta/sync`, {}));
  }

  metaDisconnect(): Promise<void> {
    return firstValueFrom(this.http.post<void>(`${this.baseUrl}/meta/auth/disconnect`, {}));
  }

  instagramImportReport(file: File): Promise<InstagramReportImportResult> {
    const data = new FormData();
    data.append('file', file);
    return firstValueFrom(this.http.post<InstagramReportImportResult>(`${this.baseUrl}/instagram/report/import`, data));
  }

  instagramPosts(accountId?: number | null): Promise<InstagramContentSnapshot> {
    const query = accountId ? `?accountId=${accountId}` : '';
    return firstValueFrom(this.http.get<InstagramContentSnapshot>(`${this.baseUrl}/instagram/posts${query}`));
  }

  facebookImportReport(file: File): Promise<FacebookReportImportResult> {
    const data = new FormData();
    data.append('file', file);
    return firstValueFrom(this.http.post<FacebookReportImportResult>(`${this.baseUrl}/facebook/report/import`, data));
  }

  facebookPosts(accountId?: number | null): Promise<FacebookContentSnapshot> {
    const query = accountId ? `?accountId=${accountId}` : '';
    return firstValueFrom(this.http.get<FacebookContentSnapshot>(`${this.baseUrl}/facebook/posts${query}`));
  }

  fetchLogs(): Promise<SocialFetchLogEntry[]> {
    return firstValueFrom(this.http.get<SocialFetchLogEntry[]>(`${this.baseUrl}/social-fetch-logs`));
  }

  archiveLogs(): Promise<ArchiveLogResult> {
    return firstValueFrom(this.http.post<ArchiveLogResult>(`${this.baseUrl}/social-fetch-logs/archive`, {}));
  }

  aiAnalysisStatus(): Promise<SocialAiAnalysisStatus> {
    return firstValueFrom(this.http.get<SocialAiAnalysisStatus>(`${this.baseUrl}/social-ai-analysis/status`));
  }

  aiAnalysisReports(limit = 20): Promise<SocialAiAnalysisReport[]> {
    return firstValueFrom(this.http.get<SocialAiAnalysisReport[]>(`${this.baseUrl}/social-ai-analysis/reports?limit=${limit}`));
  }

  aiAnalysisReportContent(fileName: string): Promise<SocialAiAnalysisReportContent> {
    return firstValueFrom(this.http.get<SocialAiAnalysisReportContent>(`${this.baseUrl}/social-ai-analysis/reports/${encodeURIComponent(fileName)}`));
  }

  aiAnalysisReportDownloadUrl(fileName: string): string {
    return `${this.baseUrl}/social-ai-analysis/reports/${encodeURIComponent(fileName)}/download`;
  }

  runAiAnalysis(period: 'daily' | 'weekly', date?: string): Promise<SocialAiAnalysisResult> {
    return firstValueFrom(this.http.post<SocialAiAnalysisResult>(`${this.baseUrl}/social-ai-analysis/run`, {
      period,
      date: date || null,
    }));
  }
}
