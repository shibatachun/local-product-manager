import { CommonModule } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { SocialAccount, SocialMetricKey, SocialPlatform, YouTubeAuthStatus, YouTubeDebugSnapshot } from '../../core/models';
import { SocialApiService } from '../../core/social-api.service';

@Component({
  selector: 'app-social-accounts',
  imports: [CommonModule, FormsModule, RouterLink],
  templateUrl: './social-accounts.page.html',
  styleUrl: './social-accounts.page.css',
})
export class SocialAccountsPage {
  readonly api = inject(SocialApiService);
  readonly accounts = signal<SocialAccount[]>([]);
  readonly selectedMetric = signal<SocialMetricKey>('clicks');
  readonly selectedAccountId = signal<number | null>(null);
  readonly notice = signal('');
  readonly loading = signal(false);
  readonly youtubeStatus = signal<YouTubeAuthStatus | null>(null);
  readonly youtubeSnapshot = signal<YouTubeDebugSnapshot | null>(null);
  readonly selectedYouTubeVideoId = signal<string | null>(null);
  readonly selectedContentPlatform = signal<SocialPlatform>('youtube');
  readonly hoveredChartIndex = signal<number | null>(null);
  readonly appVersion = 'social-platform-content-v2';
  readonly today = new Date().toISOString().slice(0, 10);
  readonly runningOnLanHost = !['localhost', '127.0.0.1', ''].includes(globalThis.location?.hostname || '');
  readonly chartWidth = 640;
  readonly chartHeight = 330;
  readonly chartLeft = 58;
  readonly chartRight = 610;
  readonly chartTop = 26;
  readonly chartBottom = 272;
  private youtubePollTimer: number | null = null;

  readonly range = {
    from: new Date(Date.now() - 1000 * 60 * 60 * 24 * 30).toISOString().slice(0, 10),
    to: this.today,
  };
  readonly accountForm = {
    platform: 'pinterest' as SocialPlatform,
    accountName: '',
    accountHandle: '',
    accountUrl: '',
    color: '#176b87',
  };
  readonly metricForm = {
    snapshotDate: this.today,
    clicks: 0,
    likes: 0,
    views: 0,
    comments: 0,
    shares: 0,
    followers: 0,
  };

  readonly platforms: { value: SocialPlatform; label: string }[] = [
    { value: 'pinterest', label: 'Pinterest' },
    { value: 'facebook', label: 'Facebook' },
    { value: 'instagram', label: 'Instagram' },
    { value: 'tiktok', label: 'TikTok' },
    { value: 'youtube', label: 'YouTube' },
    { value: 'shopify', label: 'Shopify' },
  ];
  readonly metrics: { value: SocialMetricKey; label: string; icon: string }[] = [
    { value: 'clicks', label: 'Clicks', icon: 'C' },
    { value: 'likes', label: 'Likes', icon: 'L' },
    { value: 'views', label: 'Views', icon: 'V' },
    { value: 'comments', label: 'Comments', icon: 'M' },
    { value: 'shares', label: 'Shares', icon: 'S' },
    { value: 'followers', label: 'Followers', icon: 'F' },
  ];
  readonly chartPlatforms: { value: SocialPlatform; label: string; color: string }[] = [
    { value: 'youtube', label: 'YouTube', color: '#d92727' },
    { value: 'facebook', label: 'Facebook', color: '#1877f2' },
    { value: 'instagram', label: 'Instagram', color: '#c13584' },
    { value: 'pinterest', label: 'Pinterest', color: '#bd081c' },
    { value: 'tiktok', label: 'TikTok', color: '#111827' },
  ];
  readonly contentPlatforms: { value: SocialPlatform; label: string; itemLabel: string; pluralLabel: string }[] = [
    { value: 'youtube', label: 'YouTube', itemLabel: 'video', pluralLabel: 'videos' },
    { value: 'pinterest', label: 'Pinterest', itemLabel: 'pin', pluralLabel: 'pins' },
    { value: 'instagram', label: 'Instagram', itemLabel: 'post', pluralLabel: 'posts / reels' },
    { value: 'facebook', label: 'Facebook', itemLabel: 'post', pluralLabel: 'posts' },
    { value: 'tiktok', label: 'TikTok', itemLabel: 'video', pluralLabel: 'videos' },
  ];

  readonly selectedAccount = computed(() =>
    this.accounts().find(account => account.id === this.selectedAccountId()) ?? this.accounts()[0] ?? null,
  );
  readonly maxMetricValue = computed(() =>
    Math.max(1, ...this.chartPlatforms.flatMap(platform =>
      this.chartDates().map(date => this.platformMetricValue(platform.value, date)),
    )),
  );
  readonly chartDates = computed(() => {
    const dates = new Set<string>();
    for (const account of this.accounts()) {
      for (const metric of account.metrics) dates.add(metric.snapshotDate);
    }
    return Array.from(dates).sort();
  });
  readonly selectedYouTubeVideo = computed(() => {
    const snapshot = this.youtubeSnapshot();
    if (!snapshot?.videos.length) return null;
    return snapshot.videos.find(video => video.id === this.selectedYouTubeVideoId()) ?? snapshot.videos[0];
  });
  readonly youtubeStats = computed(() => {
    const snapshot = this.youtubeSnapshot();
    const video = this.selectedYouTubeVideo();
    const previous = video?.metrics.length && video.metrics.length > 1 ? video.metrics[video.metrics.length - 2] : null;
    return [
      { label: 'Clicks', value: snapshot?.channel.clicks ?? null, delta: null, detail: snapshot?.channel.clicksNote ?? 'Not available from YouTube Data API' },
      { label: 'Views', value: video?.views ?? snapshot?.channel.views ?? 0, delta: video && previous ? video.views - previous.views : null, detail: video ? 'Selected video views' : 'Channel total views' },
      { label: 'Likes', value: video?.likes ?? 0, delta: video && previous ? video.likes - previous.likes : null, detail: video ? 'Selected video likes' : 'Select a video' },
      { label: 'Comments', value: video?.comments ?? 0, delta: video && previous ? video.comments - previous.comments : null, detail: video ? 'Selected video comments' : 'Select a video' },
      { label: 'Followers', value: snapshot?.channel.followers ?? 0, delta: null, detail: 'Channel subscribers' },
      { label: 'Videos', value: snapshot?.channel.videoCount ?? snapshot?.videos.length ?? 0, delta: null, detail: 'Channel video count' },
    ];
  });
  readonly selectedContentMeta = computed(() =>
    this.contentPlatforms.find(platform => platform.value === this.selectedContentPlatform()) ?? this.contentPlatforms[0],
  );
  readonly selectedPlatformAccounts = computed(() =>
    this.accounts().filter(account => account.platform === this.selectedContentPlatform()),
  );
  readonly selectedPlatformTotals = computed(() => {
    const accounts = this.selectedPlatformAccounts();
    const metrics = accounts.flatMap(account => account.metrics);
    return [
      { label: 'Clicks', value: metrics.reduce((sum, metric) => sum + metric.clicks, 0) },
      { label: 'Views', value: metrics.reduce((sum, metric) => sum + metric.views, 0) },
      { label: 'Likes', value: metrics.reduce((sum, metric) => sum + metric.likes, 0) },
      { label: 'Comments', value: metrics.reduce((sum, metric) => sum + metric.comments, 0) },
      { label: 'Shares', value: metrics.reduce((sum, metric) => sum + metric.shares, 0) },
      { label: 'Followers', value: metrics.reduce((sum, metric) => sum + metric.followers, 0) },
    ];
  });
  readonly yAxisTicks = computed(() => {
    const max = this.maxMetricValue();
    return [1, 0.75, 0.5, 0.25, 0].map(ratio => {
      const value = Math.round(max * ratio);
      return { value, y: this.chartY(value) };
    });
  });
  readonly chartHover = computed(() => {
    const index = this.hoveredChartIndex();
    const dates = this.chartDates();
    if (index === null || !dates[index]) return null;
    const date = dates[index];
    const rows = this.chartPlatforms.map(platform => {
      const value = this.platformMetricValue(platform.value, date);
      return {
        ...platform,
        value,
        x: this.chartX(index),
        y: this.chartY(value),
      };
    });
    return {
      date,
      x: this.chartX(index),
      rows,
      total: rows.reduce((sum, row) => sum + row.value, 0),
      tooltipX: Math.min(this.chartRight - 176, Math.max(this.chartLeft + 8, this.chartX(index) + 12)),
    };
  });

  async ngOnInit(): Promise<void> {
    await Promise.all([this.load(), this.loadYouTubeStatus()]);
    await this.loadStoredYouTubeVideos();
  }

  async load(): Promise<void> {
    await this.perform(async () => {
      const accounts = await this.api.accounts(this.range.from, this.range.to);
      this.accounts.set(accounts);
      if (!this.selectedAccountId() && accounts[0]) this.selectedAccountId.set(accounts[0].id);
    }, false);
  }

  async createAccount(): Promise<void> {
    if (!this.youtubeStatus()?.isAdminHost) return this.showNotice('Account editing is only available on the host computer.');
    if (!this.accountForm.accountName.trim()) return this.showNotice('Account name is required.');
    await this.perform(async () => {
      const account = await this.api.createAccount({
        platform: this.accountForm.platform,
        accountName: this.accountForm.accountName,
        accountHandle: this.accountForm.accountHandle || null,
        accountUrl: this.accountForm.accountUrl || null,
        color: this.accountForm.color || null,
        isActive: true,
      });
      this.accountForm.accountName = '';
      this.accountForm.accountHandle = '';
      this.accountForm.accountUrl = '';
      await this.load();
      this.selectedAccountId.set(account.id);
      this.showNotice('Social account added.');
    });
  }

  async deleteAccount(account: SocialAccount): Promise<void> {
    if (!this.youtubeStatus()?.isAdminHost) return this.showNotice('Account editing is only available on the host computer.');
    const action = account.platform === 'youtube' ? 'Disconnect' : 'Delete';
    if (!confirm(`${action} ${account.accountName}? Metrics for this account will be removed.`)) return;
    await this.perform(async () => {
      await this.api.deleteAccount(account.id);
      if (this.selectedAccountId() === account.id) this.selectedAccountId.set(null);
      await this.load();
      this.showNotice(`${this.platformLabel(account.platform)} account removed.`);
    });
  }

  async saveMetric(): Promise<void> {
    if (!this.youtubeStatus()?.isAdminHost) return this.showNotice('Manual metrics are only available on the host computer.');
    const account = this.selectedAccount();
    if (!account) return this.showNotice('Select or create an account first.');
    await this.perform(async () => {
      await this.api.saveMetric(account.id, { ...this.metricForm });
      await this.load();
      this.showNotice('Metric snapshot saved.');
    });
  }

  async sync(): Promise<void> {
    if (!this.youtubeStatus()?.isAdminHost) return this.showNotice('API sync is only available on the host computer.');
    await this.perform(async () => {
      const result = await this.api.sync();
      this.showNotice(result.status);
    });
  }

  async loadYouTubeStatus(): Promise<void> {
    try {
      this.youtubeStatus.set(await this.api.youtubeStatus());
    } catch {
      this.youtubeStatus.set(null);
    }
  }

  async connectYouTube(): Promise<void> {
    const status = this.youtubeStatus();
    if (status?.isAdminHost === false) {
      return this.showNotice('This page is read-only from other computers. Open localhost on the host computer to connect accounts.');
    }
    if (status?.isConfigured === false) {
      return this.showNotice(status.message || 'Set YouTube OAuth ClientId and ClientSecret first.');
    }
    if (this.runningOnLanHost) {
      return this.showNotice('Open http://localhost:5077/social-accounts on the server computer to connect YouTube.');
    }

    await this.perform(async () => {
      const result = await this.api.youtubeAuthStart();
      const popup = window.open(result.authorizationUrl, 'youtubeOAuth', 'width=720,height=760');
      if (!popup) {
        location.href = result.authorizationUrl;
        return;
      }
      this.startYouTubeStatusPolling(popup);
      this.showNotice('Complete YouTube authorization in the popup, then click Sync YouTube.');
    });
  }

  async syncYouTube(): Promise<void> {
    if (!this.youtubeStatus()?.isAdminHost) return this.showNotice('Sync is only available on the host computer.');
    await this.perform(async () => {
      const result = await this.api.youtubeSync();
      await Promise.all([this.load(), this.loadYouTubeStatus()]);
      this.showNotice(result.status);
    });
  }

  async disconnectYouTube(): Promise<void> {
    if (!this.youtubeStatus()?.isAdminHost) return this.showNotice('Disconnect is only available on the host computer.');
    if (!confirm('Disconnect YouTube authorization on this server?')) return;
    await this.perform(async () => {
      await this.api.youtubeDisconnect();
      this.youtubeSnapshot.set(null);
      this.selectedYouTubeVideoId.set(null);
      await this.loadYouTubeStatus();
      await this.load();
      this.showNotice('YouTube authorization disconnected. Reconnect to request the updated scopes.');
    });
  }

  async fetchYouTubeVideos(): Promise<void> {
    if (!this.youtubeStatus()?.isAdminHost) return this.showNotice('Fetching is only available on the host computer. Showing saved data.');
    await this.perform(async () => {
      const snapshot = await this.api.youtubeVideosDebug();
      this.youtubeSnapshot.set(snapshot);
      this.selectedYouTubeVideoId.set(snapshot.videos[0]?.id ?? null);
      console.log('YouTube channel metrics', snapshot.channel);
      console.table(snapshot.videos.map(video => ({
        title: video.title,
        views: video.views,
        likes: video.likes,
        comments: video.comments,
        publishedAt: video.publishedAt,
        id: video.id,
      })));
      console.log('YouTube raw debug snapshot', snapshot);
      await this.load();
      this.showNotice(`Fetched ${snapshot.videos.length} YouTube videos. Open browser console to inspect.`);
    });
  }

  async loadStoredYouTubeVideos(): Promise<void> {
    try {
      const snapshot = await this.api.youtubeStoredVideos();
      this.youtubeSnapshot.set(snapshot);
      this.selectedYouTubeVideoId.set(snapshot.videos[0]?.id ?? null);
    } catch {
      this.youtubeSnapshot.set(null);
      this.selectedYouTubeVideoId.set(null);
    }
  }

  selectYouTubeVideo(videoId: string): void {
    this.selectedYouTubeVideoId.set(videoId);
  }

  selectContentPlatform(platform: SocialPlatform): void {
    this.selectedContentPlatform.set(platform);
  }

  platformPath(platform: SocialPlatform): string {
    const dates = this.chartDates();
    if (dates.length === 0) return '';
    const points = dates.map((date, index) => {
      const value = this.platformMetricValue(platform, date);
      const x = this.chartX(index);
      const y = this.chartY(value);
      return `${x},${y}`;
    });
    return points.join(' ');
  }

  chartX(index: number): number {
    const dates = this.chartDates();
    if (dates.length <= 1) return (this.chartLeft + this.chartRight) / 2;
    return this.chartLeft + (index * (this.chartRight - this.chartLeft)) / (dates.length - 1);
  }

  chartY(value: number): number {
    return this.chartBottom - (Math.max(0, value) * (this.chartBottom - this.chartTop)) / this.maxMetricValue();
  }

  updateChartHover(event: MouseEvent): void {
    const dates = this.chartDates();
    if (!dates.length) {
      this.hoveredChartIndex.set(null);
      return;
    }

    const rect = (event.currentTarget as SVGSVGElement).getBoundingClientRect();
    const svgX = ((event.clientX - rect.left) / rect.width) * this.chartWidth;
    const clampedX = Math.min(this.chartRight, Math.max(this.chartLeft, svgX));
    const ratio = dates.length <= 1 ? 0 : (clampedX - this.chartLeft) / (this.chartRight - this.chartLeft);
    this.hoveredChartIndex.set(Math.min(dates.length - 1, Math.max(0, Math.round(ratio * (dates.length - 1)))));
  }

  clearChartHover(): void {
    this.hoveredChartIndex.set(null);
  }

  platformMetricTotal(platform: SocialPlatform): number {
    return this.accounts()
      .filter(account => account.platform === platform)
      .flatMap(account => account.metrics)
      .reduce((sum, metric) => sum + (Number(metric[this.selectedMetric()]) || 0), 0);
  }

  private platformMetricValue(platform: SocialPlatform, date: string): number {
    return this.accounts()
      .filter(account => account.platform === platform)
      .flatMap(account => account.metrics)
      .filter(metric => metric.snapshotDate === date)
      .reduce((sum, metric) => sum + (Number(metric[this.selectedMetric()]) || 0), 0);
  }

  totalFor(account: SocialAccount, key: SocialMetricKey): number {
    return account.metrics.reduce((sum, metric) => sum + (Number(metric[key]) || 0), 0);
  }

  platformLabel(platform: string): string {
    return this.platforms.find(item => item.value === platform)?.label ?? platform;
  }

  connectLabel(): string {
    return `Connect ${this.platformLabel(this.accountForm.platform)}`;
  }

  youtubeHelperText(): string {
    const status = this.youtubeStatus();
    if (!status) return 'Checking YouTube connection...';
    if (!status.isAdminHost) return status.isConnected ? 'Read-only view from this computer' : 'Waiting for host computer setup';
    if (status.isConnected) return status.channelTitle || 'YouTube Channel';
    if (this.runningOnLanHost) return `Connect on the server computer: ${status.redirectUri}`;
    return status.message || 'Open Google authorization';
  }

  private startYouTubeStatusPolling(popup: Window): void {
    if (this.youtubePollTimer) window.clearInterval(this.youtubePollTimer);
    this.youtubePollTimer = window.setInterval(async () => {
      await this.loadYouTubeStatus();
      if (this.youtubeStatus()?.isConnected || popup.closed) {
        if (this.youtubePollTimer) window.clearInterval(this.youtubePollTimer);
        this.youtubePollTimer = null;
        await this.load();
      }
    }, 1200);
  }

  private async perform(action: () => Promise<void>, showErrors = true): Promise<void> {
    this.loading.set(true);
    try {
      await action();
    } catch (error) {
      if (showErrors) this.showNotice(error instanceof Error ? error.message : String(error));
    } finally {
      this.loading.set(false);
    }
  }

  private showNotice(message: string): void {
    this.notice.set(message);
    setTimeout(() => {
      if (this.notice() === message) this.notice.set('');
    }, 3200);
  }
}
