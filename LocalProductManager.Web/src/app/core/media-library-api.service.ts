import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { apiBaseUrl } from './api-base';
import { MediaUploadService } from './media-upload.service';

export interface MediaLibraryFolder {
  name: string;
  photoCount: number;
  videoCount: number;
}

export interface MediaLibraryFile {
  fileName: string;
  isVideo: boolean;
  size: number;
  updatedAt: string;
}

export interface MediaLibraryUploadResult {
  imageCount: number;
  videoCount: number;
}

@Injectable({ providedIn: 'root' })
export class MediaLibraryApiService {
  private readonly http = inject(HttpClient);
  private readonly mediaUpload = inject(MediaUploadService);
  private readonly baseUrl = `${apiBaseUrl()}/media-library`;

  folders(): Promise<MediaLibraryFolder[]> {
    return firstValueFrom(this.http.get<MediaLibraryFolder[]>(`${this.baseUrl}/folders`));
  }

  createFolder(name: string): Promise<MediaLibraryFolder> {
    return firstValueFrom(this.http.post<MediaLibraryFolder>(`${this.baseUrl}/folders`, { name }));
  }

  files(folder: string): Promise<MediaLibraryFile[]> {
    return firstValueFrom(this.http.get<MediaLibraryFile[]>(`${this.baseUrl}/folders/${encodeURIComponent(folder)}/files`));
  }

  openFolder(folder: string): Promise<{ folder: string }> {
    return firstValueFrom(this.http.post<{ folder: string }>(
      `${this.baseUrl}/folders/${encodeURIComponent(folder)}/open`, {}));
  }

  fileUrl(folder: string, fileName: string): string {
    return `${this.baseUrl}/folders/${encodeURIComponent(folder)}/files/${encodeURIComponent(fileName)}`;
  }

  upload(folder: string, files: File[]): Promise<MediaLibraryUploadResult> {
    return this.mediaUpload.uploadMultipart<MediaLibraryUploadResult>(
      `${this.baseUrl}/folders/${encodeURIComponent(folder)}/upload`, files);
  }

  async uploadFile(folder: string, file: File): Promise<MediaLibraryUploadResult> {
    // Camera videos already use this byte-read + Blob path successfully. Read
    // picked files the same way so an unreadable/empty picker result is caught
    // before the request instead of silently sending an empty File body.
    const bytes = await this.mediaUpload.readFileBytes(file);
    const contentType = file.type || 'application/octet-stream';
    const url = `${this.baseUrl}/folders/${encodeURIComponent(folder)}/upload-file?fileName=${encodeURIComponent(file.name)}`;
    return firstValueFrom(this.http.post<MediaLibraryUploadResult>(url, new Blob([bytes], { type: contentType }), {
      headers: { 'Content-Type': contentType, 'X-Upload-Expected-Bytes': String(bytes.byteLength) },
    }));
  }

  uploadCapturedVideo(folder: string, file: File): Promise<MediaLibraryUploadResult> {
    return this.mediaUpload.uploadCapturedVideo<MediaLibraryUploadResult>(
      `${this.baseUrl}/folders/${encodeURIComponent(folder)}/upload-capture`, file);
  }
}
