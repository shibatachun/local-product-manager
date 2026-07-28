import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { apiBaseUrl } from './api-base';
import { CreateVideoTrendItemRequest, VideoTrendItem } from './models';

@Injectable({ providedIn: 'root' })
export class VideoTrendApiService {
  private readonly http = inject(HttpClient);
  readonly baseUrl = apiBaseUrl();

  items(platform?: string): Promise<VideoTrendItem[]> {
    const suffix = platform && platform !== 'all' ? `?platform=${encodeURIComponent(platform)}` : '';
    return firstValueFrom(this.http.get<VideoTrendItem[]>(`${this.baseUrl}/video-trends${suffix}`));
  }

  create(body: CreateVideoTrendItemRequest): Promise<VideoTrendItem> {
    return firstValueFrom(this.http.post<VideoTrendItem>(`${this.baseUrl}/video-trends`, body));
  }

  delete(id: number): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`${this.baseUrl}/video-trends/${id}`));
  }
}
