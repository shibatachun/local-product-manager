import { CommonModule } from '@angular/common';
import { Component, Input, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';
import { CurrentUser, VideoTrendItem } from '../../core/models';
import { VideoTrendApiService } from '../../core/video-trend-api.service';

@Component({
  selector: 'app-video-trend-panel',
  imports: [CommonModule, FormsModule],
  templateUrl: './video-trend-panel.component.html',
  styleUrl: './video-trend-panel.component.css',
})
export class VideoTrendPanelComponent implements OnInit {
  @Input() currentUser: CurrentUser | null = null;

  private readonly api = inject(VideoTrendApiService);
  private readonly sanitizer = inject(DomSanitizer);

  readonly items = signal<VideoTrendItem[]>([]);
  readonly selectedPlatform = signal('all');
  readonly loading = signal(false);
  readonly notice = signal('');
  readonly form = {
    platform: 'auto',
    url: '',
    title: '',
    description: '',
    author: '',
    tags: '',
  };

  readonly platforms = [
    { value: 'all', label: 'All', short: 'A' },
    { value: 'youtube', label: 'YouTube', short: 'Y' },
    { value: 'instagram', label: 'Instagram', short: 'I' },
    { value: 'tiktok', label: 'TikTok', short: 'T' },
    { value: 'pinterest', label: 'Pinterest', short: 'P' },
    { value: 'facebook', label: 'Facebook', short: 'F' },
    { value: 'other', label: 'Other', short: 'O' },
  ];

  readonly visibleItems = computed(() => {
    const platform = this.selectedPlatform();
    return platform === 'all'
      ? this.items()
      : this.items().filter(item => item.platform === platform);
  });

  readonly counts = computed(() => {
    const counts = new Map<string, number>();
    for (const item of this.items()) counts.set(item.platform, (counts.get(item.platform) ?? 0) + 1);
    return counts;
  });

  async ngOnInit(): Promise<void> {
    await this.load();
  }

  canManage(): boolean {
    return !!this.currentUser?.canImportData;
  }

  async load(): Promise<void> {
    await this.perform(async () => {
      this.items.set(await this.api.items());
    }, false);
  }

  async addVideo(): Promise<void> {
    if (!this.canManage()) return this.showNotice('Only users with import permission can add videos.');
    if (!this.form.url.trim()) return this.showNotice('Paste a video link first.');
    await this.perform(async () => {
      const item = await this.api.create({
        platform: this.form.platform === 'auto' ? null : this.form.platform,
        url: this.form.url,
        title: this.form.title || null,
        description: this.form.description || null,
        author: this.form.author || null,
        tags: this.form.tags || null,
      });
      this.resetForm();
      await this.load();
      this.selectedPlatform.set(item.platform);
      this.showNotice(`Saved ${this.platformLabel(item.platform)} video.`);
    });
  }

  async deleteVideo(item: VideoTrendItem): Promise<void> {
    if (!this.canManage()) return this.showNotice('Only users with import permission can delete videos.');
    if (!confirm(`Delete "${item.title}"?`)) return;
    await this.perform(async () => {
      await this.api.delete(item.id);
      await this.load();
      this.showNotice('Video removed.');
    });
  }

  platformLabel(platform: string): string {
    return this.platforms.find(item => item.value === platform)?.label ?? platform;
  }

  safeEmbedUrl(item: VideoTrendItem): SafeResourceUrl | null {
    return item.embedUrl ? this.sanitizer.bypassSecurityTrustResourceUrl(item.embedUrl) : null;
  }

  openUrl(item: VideoTrendItem): void {
    globalThis.open(item.url, '_blank', 'noopener,noreferrer');
  }

  private resetForm(): void {
    Object.assign(this.form, {
      platform: 'auto',
      url: '',
      title: '',
      description: '',
      author: '',
      tags: '',
    });
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
