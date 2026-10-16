import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { apiBaseUrl } from './api-base';
import { MediaUploadService } from './media-upload.service';

export interface BroochFindRecord {
  id: string;
  label: string;
  location: string;
  updatedAt: string;
  photos: { id: string }[];
}

export interface BroochFindMatch {
  record: BroochFindRecord;
  bestPhotoId: string;
  visualScore: number;
}

@Injectable({ providedIn: 'root' })
export class BroochFinderApiService {
  private readonly http = inject(HttpClient);
  private readonly upload = inject(MediaUploadService);
  private readonly root = `${apiBaseUrl()}/brooch-finder`;

  list(): Promise<BroochFindRecord[]> {
    return firstValueFrom(this.http.get<BroochFindRecord[]>(this.root));
  }

  create(label: string, location: string): Promise<BroochFindRecord> {
    return firstValueFrom(this.http.post<BroochFindRecord>(this.root, { label, location }));
  }

  createWithPhoto(label: string, location: string, file: File): Promise<BroochFindRecord> {
    const params = new URLSearchParams({ label, location });
    return this.postImage<BroochFindRecord>(`${this.root}/create-with-photo?${params}`, file);
  }

  update(id: string, label: string, location: string): Promise<BroochFindRecord> {
    return firstValueFrom(this.http.put<BroochFindRecord>(`${this.root}/${id}`, { label, location }));
  }

  async addPhoto(id: string, file: File): Promise<BroochFindRecord> {
    return this.postImage<BroochFindRecord>(`${this.root}/${id}/photos`, file);
  }

  async search(file: File): Promise<BroochFindMatch[]> {
    return this.postImage<BroochFindMatch[]>(`${this.root}/search`, file);
  }

  photoUrl(recordId: string, photoId: string): string {
    return `${this.root}/${recordId}/photos/${photoId}`;
  }

  private async postImage<T>(url: string, file: File): Promise<T> {
    const bytes = await this.upload.readFileBytes(file);
    const type = file.type.toLowerCase();
    if (!['image/jpeg', 'image/png', 'image/webp', 'image/bmp', 'image/gif'].includes(type))
      throw new Error('Choose a JPG, PNG, WebP, BMP, or GIF image.');
    return firstValueFrom(this.http.post<T>(url, new Blob([bytes], { type }), {
      headers: { 'Content-Type': type, 'X-Upload-Expected-Bytes': String(bytes.byteLength) },
    }));
  }
}
