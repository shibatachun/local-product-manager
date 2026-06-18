import { CommonModule } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { PinterestAuthStatus, PinterestContentSnapshot, SocialAccount, SocialFetchLogEntry, SocialMetricKey, SocialPlatform, YouTubeAuthStatus, YouTubeDebugSnapshot } from '../../core/models';
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
  readonly pinterestStatus = signal<PinterestAuthStatus | null>(null);
  readonly youtubeSnapshot = signal<YouTubeDebugSnapshot | null>(null);
  readonly pinterestSnapshot = signal<PinterestContentSnapshot | null>(null);
  readonly selectedYouTubeVideoId = signal<string | null>(null);
  readonly selectedPinterestItemId = signal<string | null>(null);
  readonly selectedContentPlatform = signal<SocialPlatform>('youtube');
  readonly hoveredChartIndex = signal<number | null>(null);
  readonly selectedRangePreset = signal<RangePreset>('month');
  readonly selectedChartPlatforms = signal<Set<SocialPlatform>>(new Set(['youtube']));
  readonly fetchLogs = signal<SocialFetchLogEntry[]>([]);
  readonly logsOpen = signal(false);
  readonly appVersion = 'cumulative-social-chart-v1';
  readonly today = this.toDateInput(new Date());
  readonly runningOnLanHost = !['localhost', '127.0.0.1', ''].includes(globalThis.location?.hostname || '');
  readonly chartWidth = 640;
  readonly chartHeight = 330;
  readonly chartLeft = 58;
  readonly chartRight = 610;
  readonly chartTop = 26;
  readonly chartBottom = 272;
  private youtubePollTimer: number | null = null;
  private pinterestPollTimer: number | null = null;

  readonly range = {
    from: this.toDateInput(this.addDays(new Date(), -30)),
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
  readonly rangePresets: { value: RangePreset; label: string }[] = [
    { value: 'day', label: '1D' },
    { value: 'week', label: '1W' },
    { value: 'month', label: '1M' },
    { value: 'ytd', label: 'YTD' },
    { value: '5y', label: '5Y' },
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
  readonly visibleChartPlatforms = computed(() =>
    this.chartPlatforms.filter(platform => this.selectedChartPlatforms().has(platform.value)),
  );
  readonly maxMetricValue = computed(() =>
    Math.max(1, ...this.visibleChartPlatforms().flatMap(platform =>
      this.chartDates().map(date => this.platformMetricValue(platform.value, date)),
    )),
  );
  readonly dailyMaxMetricValue = computed(() =>
    Math.max(1, ...this.visibleChartPlatforms().flatMap(platform =>
      this.chartDates().map(date => this.dailyPlatformMetricValue(platform.value, date)),
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
  readonly selectedPinterestItem = computed(() => {
    const snapshot = this.pinterestSnapshot();
    if (!snapshot?.pins.length) return null;
    return snapshot.pins.find(item => item.id === this.selectedPinterestItemId()) ?? snapshot.pins[0];
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
  readonly pinterestStats = computed(() => {
    const snapshot = this.pinterestSnapshot();
    const item = this.selectedPinterestItem();
    const previous = item?.metrics.length && item.metrics.length > 1 ? item.metrics[item.metrics.length - 2] : null;
    return [
      { label: 'Clicks', value: item?.clicks ?? snapshot?.channel.clicks ?? 0, delta: item && previous ? item.clicks - previous.clicks : null, detail: item ? 'Selected Pinterest item clicks' : 'Report click total' },
      { label: 'Views', value: item?.views ?? snapshot?.channel.views ?? 0, delta: item && previous ? item.views - previous.views : null, detail: item ? 'Selected item impressions' : 'Report impressions' },
      { label: 'Likes', value: item?.likes ?? 0, delta: item && previous ? item.likes - previous.likes : null, detail: 'Pinterest engagement mapped to likes' },
      { label: 'Comments', value: item?.comments ?? 0, delta: item && previous ? item.comments - previous.comments : null, detail: 'Not included in this report' },
      { label: 'Followers', value: snapshot?.channel.followers ?? 0, delta: null, detail: 'Not included in this report' },
      { label: 'Pins', value: snapshot?.channel.pinCount ?? snapshot?.pins.length ?? 0, delta: null, detail: 'Imported pins' },
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
  readonly dailyYAxisTicks = computed(() => {
    const max = this.dailyMaxMetricValue();
    return [1, 0.5, 0].map(ratio => {
      const value = Math.round(max * ratio);
      return { value, y: this.dailyChartY(value) };
    });
  });
  readonly xAxisTicks = computed(() => {
    const dates = this.chartDates();
    if (!dates.length) return [];
    if (dates.length === 1) {
      return [{ label: this.formatChartDate(dates[0]), x: this.chartXForDate(dates[0]) }];
    }

    const tickCount = Math.min(6, dates.length);
    const indexes = new Set<number>();
    for (var i = 0; i < tickCount; i++) {
      indexes.add(Math.round((i * (dates.length - 1)) / (tickCount - 1)));
    }

    return Array.from(indexes)
      .sort((left, right) => left - right)
      .map(index => ({
        label: this.formatChartDate(dates[index]),
        x: this.chartXForDate(dates[index]),
      }));
  });
  readonly chartHover = computed(() => {
    const index = this.hoveredChartIndex();
    const dates = this.chartDates();
    if (index === null || !dates[index]) return null;
    const date = dates[index];
    const rows = this.visibleChartPlatforms().map(platform => {
      const value = this.platformMetricValue(platform.value, date);
      const startValue = this.platformMetricValue(platform.value, dates[0]);
      const change = value - startValue;
      return {
        ...platform,
        value,
        change,
        percent: startValue > 0 ? (change / startValue) * 100 : null,
        x: this.chartXForDate(date),
        y: this.chartY(value),
        dailyValue: this.dailyPlatformMetricValue(platform.value, date),
        dailyY: this.dailyChartY(this.dailyPlatformMetricValue(platform.value, date)),
      };
    });
    return {
      date,
      dateLabel: this.formatTooltipDate(date),
      x: this.chartXForDate(date),
      rows,
      total: rows.reduce((sum, row) => sum + row.value, 0),
      tooltipX: Math.min(this.chartRight - 224, Math.max(this.chartLeft + 8, this.chartXForDate(date) + 12)),
      tooltipHeight: 48 + rows.length * 26,
    };
  });

  async ngOnInit(): Promise<void> {
    await Promise.all([this.load(), this.loadYouTubeStatus(), this.loadPinterestStatus()]);
    await this.loadSelectedAccountContent();
  }

  async load(): Promise<void> {
    await this.perform(async () => {
      const accounts = await this.api.accounts(this.range.from, this.range.to);
      this.accounts.set(accounts);
      const selected = accounts.find(account => account.id === this.selectedAccountId()) ?? accounts[0] ?? null;
      this.selectedAccountId.set(selected?.id ?? null);
      if (selected) this.setContentPlatformForAccount(selected);
    }, false);
  }

  async applyRangePreset(preset: RangePreset): Promise<void> {
    const today = new Date();
    let from = today;
    if (preset === 'week') from = this.addDays(today, -6);
    if (preset === 'month') from = this.addMonths(today, -1);
    if (preset === 'ytd') from = new Date(today.getFullYear(), 0, 1);
    if (preset === '5y') from = new Date(today.getFullYear() - 5, today.getMonth(), today.getDate());

    this.selectedRangePreset.set(preset);
    this.range.from = this.toDateInput(from);
    this.range.to = this.toDateInput(today);
    this.clearChartHover();
    await this.load();
  }

  async rangeChanged(): Promise<void> {
    this.selectedRangePreset.set('custom');
    this.clearChartHover();
    await this.load();
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
      this.setContentPlatformForAccount(account);
      this.showNotice('Social account added.');
    });
  }

  async deleteAccount(account: SocialAccount): Promise<void> {
    if (!this.youtubeStatus()?.isAdminHost) return this.showNotice('Account editing is only available on the host computer.');
    const action = account.platform === 'youtube' || account.platform === 'pinterest' ? 'Disconnect' : 'Delete';
    if (!confirm(`${action} ${account.accountName}? Metrics for this account will be removed.`)) return;
    if (account.platform === 'youtube') {
      await this.perform(async () => {
        await this.api.youtubeDisconnect();
        this.youtubeSnapshot.set(null);
        this.selectedYouTubeVideoId.set(null);
        this.selectedAccountId.set(null);
        await this.loadYouTubeStatus();
        await this.load();
        this.showNotice('YouTube authorization disconnected.');
      });
      return;
    }
    if (account.platform === 'pinterest') {
      await this.perform(async () => {
        await this.api.pinterestDisconnect();
        this.selectedAccountId.set(null);
        await this.loadPinterestStatus();
        await this.load();
        this.showNotice('Pinterest authorization disconnected.');
      });
      return;
    }
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
      await this.loadFetchLogs(false);
      this.showNotice(result.status);
    });
  }

  async loadFetchLogs(showNotice = true): Promise<void> {
    if (!this.youtubeStatus()?.isAdminHost) return;
    await this.perform(async () => {
      this.fetchLogs.set(await this.api.fetchLogs());
      this.logsOpen.set(true);
      if (showNotice) this.showNotice('Fetch logs loaded.');
    });
  }

  async archiveFetchLogs(): Promise<void> {
    if (!this.youtubeStatus()?.isAdminHost) return;
    await this.perform(async () => {
      const result = await this.api.archiveLogs();
      await this.loadFetchLogs(false);
      this.showNotice(`Logs archived to ${result.path}`);
    });
  }

  async loadYouTubeStatus(): Promise<void> {
    try {
      this.youtubeStatus.set(await this.api.youtubeStatus());
    } catch {
      this.youtubeStatus.set(null);
    }
  }

  async loadPinterestStatus(): Promise<void> {
    try {
      this.pinterestStatus.set(await this.api.pinterestStatus());
    } catch {
      this.pinterestStatus.set(null);
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
      await this.selectConnectedPlatformAccount('youtube');
      await this.loadFetchLogs(false);
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
      await this.selectConnectedPlatformAccount('youtube');
      await this.loadFetchLogs(false);
      this.showNotice(`Fetched ${snapshot.videos.length} YouTube videos. Open browser console to inspect.`);
    });
  }

  async connectPinterest(): Promise<void> {
    const status = this.pinterestStatus();
    if (status?.isAdminHost === false) {
      return this.showNotice('This page is read-only from other computers. Open localhost on the host computer to connect accounts.');
    }
    if (status?.isConfigured === false) {
      return this.showNotice(status.message || 'Set Pinterest OAuth ClientId and ClientSecret first.');
    }
    if (this.runningOnLanHost) {
      return this.showNotice('Open http://localhost:5077/social-accounts on the server computer to connect Pinterest.');
    }

    await this.perform(async () => {
      const result = await this.api.pinterestAuthStart();
      const popup = window.open(result.authorizationUrl, 'pinterestOAuth', 'width=720,height=760');
      if (!popup) {
        location.href = result.authorizationUrl;
        return;
      }
      this.startPinterestStatusPolling(popup);
      this.showNotice('Complete Pinterest authorization in the popup, then click Sync Pinterest.');
    });
  }

  async syncPinterest(): Promise<void> {
    if (!this.youtubeStatus()?.isAdminHost) return this.showNotice('Sync is only available on the host computer.');
    await this.perform(async () => {
      const result = await this.api.pinterestSync();
      await Promise.all([this.load(), this.loadPinterestStatus()]);
      await this.selectConnectedPlatformAccount('pinterest');
      await this.loadFetchLogs(false);
      this.showNotice(result.status);
    });
  }

  async disconnectPinterest(): Promise<void> {
    if (!this.youtubeStatus()?.isAdminHost) return this.showNotice('Disconnect is only available on the host computer.');
    if (!confirm('Disconnect Pinterest authorization on this server?')) return;
    await this.perform(async () => {
      await this.api.pinterestDisconnect();
      await this.loadPinterestStatus();
      await this.load();
      this.showNotice('Pinterest authorization disconnected.');
    });
  }

  async onPinterestReportSelected(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0] ?? null;
    input.value = '';
    if (!file) return;
    await this.importPinterestReport(file);
  }

  async importPinterestReport(file: File): Promise<void> {
    if (!this.youtubeStatus()?.isAdminHost) return this.showNotice('Pinterest report import is only available on the host computer.');
    await this.perform(async () => {
      const result = await this.api.pinterestImportReport(file);
      await this.load();
      this.selectedAccountId.set(result.accountId);
      this.setContentPlatformForAccount(this.selectedAccount());
      await Promise.all([this.loadStoredPinterestPins(result.accountId), this.loadFetchLogs(false)]);
      this.selectedChartPlatforms.update(current => new Set([...Array.from(current), 'pinterest']));
      this.showNotice(`Imported ${result.pinsImported} pins, ${result.boardsImported} boards, and ${result.dailyRowsImported} daily rows.`);
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

  async loadStoredPinterestPins(accountId: number | null = this.selectedAccount()?.id ?? null): Promise<void> {
    try {
      const snapshot = await this.api.pinterestPins(accountId);
      this.pinterestSnapshot.set(snapshot);
      this.selectedPinterestItemId.set(snapshot.pins[0]?.id ?? null);
    } catch {
      this.pinterestSnapshot.set(null);
      this.selectedPinterestItemId.set(null);
    }
  }

  selectYouTubeVideo(videoId: string): void {
    this.selectedYouTubeVideoId.set(videoId);
  }

  selectPinterestItem(itemId: string): void {
    this.selectedPinterestItemId.set(itemId);
  }

  async selectAccount(account: SocialAccount): Promise<void> {
    this.selectedAccountId.set(account.id);
    this.setContentPlatformForAccount(account);
    this.selectedChartPlatforms.update(current => new Set([...Array.from(current), account.platform]));
    await this.loadSelectedAccountContent();
  }

  selectContentPlatform(platform: SocialPlatform): void {
    this.selectedContentPlatform.set(platform);
  }

  platformPath(platform: SocialPlatform): string {
    const dates = this.chartDates();
    if (dates.length === 0) return '';
    const points = dates.map((date, index) => {
      const value = this.platformMetricValue(platform, date);
      const x = this.chartXForDate(date);
      const y = this.chartY(value);
      return `${x},${y}`;
    });
    return points.join(' ');
  }

  dailyPlatformPath(platform: SocialPlatform): string {
    const dates = this.chartDates();
    if (dates.length === 0) return '';
    return dates.map(date => {
      const value = this.dailyPlatformMetricValue(platform, date);
      return `${this.chartXForDate(date)},${this.dailyChartY(value)}`;
    }).join(' ');
  }

  chartXForDate(date: string): number {
    const dates = this.chartDates();
    if (dates.length <= 1) return (this.chartLeft + this.chartRight) / 2;
    const min = this.dateTime(dates[0]);
    const max = this.dateTime(dates[dates.length - 1]);
    if (max <= min) return (this.chartLeft + this.chartRight) / 2;
    return this.chartLeft + ((this.dateTime(date) - min) * (this.chartRight - this.chartLeft)) / (max - min);
  }

  chartY(value: number): number {
    return this.chartBottom - (Math.max(0, value) * (this.chartBottom - this.chartTop)) / this.maxMetricValue();
  }

  dailyChartY(value: number): number {
    return this.chartBottom - (Math.max(0, value) * (this.chartBottom - this.chartTop)) / this.dailyMaxMetricValue();
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
    const nearest = dates
      .map((date, index) => ({ index, distance: Math.abs(this.chartXForDate(date) - clampedX) }))
      .sort((left, right) => left.distance - right.distance)[0];
    this.hoveredChartIndex.set(nearest?.index ?? null);
  }

  clearChartHover(): void {
    this.hoveredChartIndex.set(null);
  }

  toggleChartPlatform(platform: SocialPlatform): void {
    this.selectedChartPlatforms.update(current => {
      const next = new Set(current);
      if (next.has(platform) && next.size > 1) next.delete(platform);
      else next.add(platform);
      return next;
    });
    this.clearChartHover();
  }

  platformMetricTotal(platform: SocialPlatform): number {
    const dates = this.chartDates();
    return dates.length ? this.platformMetricValue(platform, dates[dates.length - 1]) : 0;
  }

  private platformMetricValue(platform: SocialPlatform, date: string): number {
    const key = this.selectedMetric();
    const target = this.dateTime(date);
    const accounts = this.accounts().filter(account => account.platform === platform);
    if (key === 'followers') {
      return accounts.reduce((sum, account) => {
        const latest = account.metrics
          .filter(metric => this.dateTime(metric.snapshotDate) <= target)
          .sort((left, right) => this.dateTime(right.snapshotDate) - this.dateTime(left.snapshotDate))[0];
        return sum + (latest ? Number(latest.followers) || 0 : 0);
      }, 0);
    }

    return accounts
      .flatMap(account => account.metrics)
      .filter(metric => this.dateTime(metric.snapshotDate) <= target)
      .reduce((sum, metric) => sum + (Number(metric[key]) || 0), 0);
  }

  private dailyPlatformMetricValue(platform: SocialPlatform, date: string): number {
    return this.accounts()
      .filter(account => account.platform === platform)
      .flatMap(account => account.metrics)
      .filter(metric => metric.snapshotDate === date)
      .reduce((sum, metric) => sum + (Number(metric[this.selectedMetric()]) || 0), 0);
  }

  private async loadSelectedAccountContent(): Promise<void> {
    const account = this.selectedAccount();
    if (!account) return;
    this.setContentPlatformForAccount(account);
    if (account.platform === 'youtube') {
      await this.loadStoredYouTubeVideos();
      return;
    }
    if (account.platform === 'pinterest') {
      await this.loadStoredPinterestPins(account.id);
      return;
    }
  }

  private async selectConnectedPlatformAccount(platform: SocialPlatform): Promise<void> {
    const account = this.accounts().find(item => item.platform === platform);
    if (!account) return;
    this.selectedAccountId.set(account.id);
    this.setContentPlatformForAccount(account);
    await this.loadSelectedAccountContent();
  }

  private setContentPlatformForAccount(account: SocialAccount | null): void {
    if (!account) return;
    if (this.contentPlatforms.some(platform => platform.value === account.platform)) {
      this.selectedContentPlatform.set(account.platform);
    }
  }

  totalFor(account: SocialAccount, key: SocialMetricKey): number {
    if (key === 'followers') {
      const latest = [...account.metrics]
        .sort((left, right) => this.dateTime(right.snapshotDate) - this.dateTime(left.snapshotDate))[0];
      return latest?.followers ?? 0;
    }
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

  pinterestHelperText(): string {
    const status = this.pinterestStatus();
    if (!status) return 'Checking Pinterest connection...';
    if (!status.isAdminHost) return status.isConnected ? 'Read-only view from this computer' : 'Waiting for host computer setup';
    if (status.isConnected) return status.accountName || 'Pinterest Account';
    if (this.runningOnLanHost) return `Connect on the server computer: ${status.redirectUri}`;
    return status.message || 'Open Pinterest authorization';
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

  private startPinterestStatusPolling(popup: Window): void {
    if (this.pinterestPollTimer) window.clearInterval(this.pinterestPollTimer);
    this.pinterestPollTimer = window.setInterval(async () => {
      await this.loadPinterestStatus();
      if (this.pinterestStatus()?.isConnected || popup.closed) {
        if (this.pinterestPollTimer) window.clearInterval(this.pinterestPollTimer);
        this.pinterestPollTimer = null;
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

  private addDays(date: Date, days: number): Date {
    const copy = new Date(date);
    copy.setDate(copy.getDate() + days);
    return copy;
  }

  private addMonths(date: Date, months: number): Date {
    const copy = new Date(date);
    copy.setMonth(copy.getMonth() + months);
    return copy;
  }

  private toDateInput(date: Date): string {
    const year = date.getFullYear();
    const month = String(date.getMonth() + 1).padStart(2, '0');
    const day = String(date.getDate()).padStart(2, '0');
    return `${year}-${month}-${day}`;
  }

  private parseDateInput(value: string): Date {
    const [year, month, day] = value.split('-').map(Number);
    return new Date(year, month - 1, day);
  }

  private dateTime(value: string): number {
    return this.parseDateInput(value).getTime();
  }

  private formatChartDate(value: string): string {
    const date = this.parseDateInput(value);
    const preset = this.selectedRangePreset();
    if (preset === 'day' || preset === 'week') {
      return date.toLocaleDateString(undefined, { weekday: 'short' });
    }
    if (preset === 'month') {
      return date.toLocaleDateString(undefined, { month: 'short', day: 'numeric' });
    }
    if (preset === 'ytd') {
      return date.toLocaleDateString(undefined, { month: 'short' });
    }
    if (preset === '5y') {
      return String(date.getFullYear());
    }

    return date.toLocaleDateString(undefined, { month: 'short', day: 'numeric' });
  }

  private formatTooltipDate(value: string): string {
    return this.parseDateInput(value).toLocaleDateString(undefined, {
      year: 'numeric',
      month: 'short',
      day: 'numeric',
    });
  }
}

type RangePreset = 'day' | 'week' | 'month' | 'ytd' | '5y' | 'custom';
