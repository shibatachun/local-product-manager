import { CommonModule } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { SocialAccount, SocialMetricKey, SocialPlatform, YouTubeAuthStatus } from '../../core/models';
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
  readonly today = new Date().toISOString().slice(0, 10);
  readonly runningOnLanHost = !['localhost', '127.0.0.1', ''].includes(globalThis.location?.hostname || '');

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

  readonly selectedAccount = computed(() =>
    this.accounts().find(account => account.id === this.selectedAccountId()) ?? this.accounts()[0] ?? null,
  );
  readonly maxMetricValue = computed(() =>
    Math.max(1, ...this.accounts().flatMap(account =>
      account.metrics.map(metric => Number(metric[this.selectedMetric()]) || 0),
    )),
  );
  readonly chartDates = computed(() => {
    const dates = new Set<string>();
    for (const account of this.accounts()) {
      for (const metric of account.metrics) dates.add(metric.snapshotDate);
    }
    return Array.from(dates).sort();
  });

  async ngOnInit(): Promise<void> {
    await Promise.all([this.load(), this.loadYouTubeStatus()]);
  }

  async load(): Promise<void> {
    await this.perform(async () => {
      const accounts = await this.api.accounts(this.range.from, this.range.to);
      this.accounts.set(accounts);
      if (!this.selectedAccountId() && accounts[0]) this.selectedAccountId.set(accounts[0].id);
    }, false);
  }

  async createAccount(): Promise<void> {
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

  async saveMetric(): Promise<void> {
    const account = this.selectedAccount();
    if (!account) return this.showNotice('Select or create an account first.');
    await this.perform(async () => {
      await this.api.saveMetric(account.id, { ...this.metricForm });
      await this.load();
      this.showNotice('Metric snapshot saved.');
    });
  }

  async sync(): Promise<void> {
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
      this.showNotice('Complete YouTube authorization in the popup, then click Sync YouTube.');
    });
  }

  async syncYouTube(): Promise<void> {
    await this.perform(async () => {
      const result = await this.api.youtubeSync();
      await Promise.all([this.load(), this.loadYouTubeStatus()]);
      this.showNotice(result.status);
    });
  }

  accountPath(account: SocialAccount): string {
    const dates = this.chartDates();
    if (dates.length === 0) return '';
    const byDate = new Map(account.metrics.map(metric => [metric.snapshotDate, metric]));
    const points = dates.map((date, index) => {
      const metric = byDate.get(date);
      const value = metric ? Number(metric[this.selectedMetric()]) || 0 : 0;
      const x = dates.length === 1 ? 50 : 40 + (index * 520) / (dates.length - 1);
      const y = 250 - (value * 210) / this.maxMetricValue();
      return `${x},${y}`;
    });
    return points.join(' ');
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
    if (status.isConnected) return status.channelTitle || 'YouTube Channel';
    if (this.runningOnLanHost) return `Connect on the server computer: ${status.redirectUri}`;
    return status.message || 'Open Google authorization';
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
