import { HttpClient, HttpHeaders } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';

@Injectable({ providedIn: 'root' })
export class MediaUploadService {
  private readonly http = inject(HttpClient);

  filesFrom(event: Event): { input: HTMLInputElement; files: File[] } {
    const input = event.target as HTMLInputElement;
    return { input, files: Array.from(input.files ?? []) };
  }

  async prepareCameraImages(files: File[]): Promise<File[]> {
    return Promise.all(files.map(file => file.type.startsWith('image/')
      ? this.prepareCameraImage(file)
      : file));
  }

  uploadMultipart<T>(url: string, files: File[]): Promise<T> {
    const data = new FormData();
    files.forEach((file, index) => data.append('files', file, this.uploadFileName(file, index)));
    return firstValueFrom(this.http.post<T>(url, data));
  }

  async uploadCapturedVideo<T>(url: string, file: File): Promise<T> {
    const bytes = await file.arrayBuffer();
    if (bytes.byteLength === 0) {
      throw new Error(`The camera returned an empty video (${file.name || 'unnamed file'}, ${file.type || 'unknown type'}).`);
    }
    const contentType = file.type || 'video/mp4';
    return firstValueFrom(this.http.post<T>(url, new Blob([bytes], { type: contentType }), {
      headers: new HttpHeaders({
        'Content-Type': contentType,
        'X-Upload-File-Name': this.uploadFileName(file, 0),
      }),
    }));
  }

  private async prepareCameraImage(file: File): Promise<File> {
    const sourceUrl = URL.createObjectURL(file);
    try {
      const image = new Image();
      image.decoding = 'async';
      image.src = sourceUrl;
      await new Promise<void>((resolve, reject) => {
        image.onload = () => resolve();
        image.onerror = () => reject(new Error('The captured image could not be decoded.'));
      });

      const maximumDimension = 2048;
      const scale = Math.min(1, maximumDimension / Math.max(image.naturalWidth, image.naturalHeight));
      const width = Math.max(1, Math.round(image.naturalWidth * scale));
      const height = Math.max(1, Math.round(image.naturalHeight * scale));
      const canvas = document.createElement('canvas');
      canvas.width = width;
      canvas.height = height;
      const context = canvas.getContext('2d');
      if (!context) return file;
      context.drawImage(image, 0, 0, width, height);

      const blob = await new Promise<Blob | null>(resolve => canvas.toBlob(resolve, 'image/jpeg', .86));
      if (!blob) return file;
      const baseName = file.name.replace(/\.[^.]+$/, '') || `camera-${Date.now()}`;
      return new File([blob], `${baseName}.jpg`, { type: 'image/jpeg', lastModified: Date.now() });
    } catch {
      return file;
    } finally {
      URL.revokeObjectURL(sourceUrl);
    }
  }

  private uploadFileName(file: File, index: number): string {
    const existingName = file.name?.trim();
    if (existingName) return existingName;

    const extensionByMime: Record<string, string> = {
      'image/jpeg': '.jpg',
      'image/png': '.png',
      'image/webp': '.webp',
      'image/gif': '.gif',
      'image/bmp': '.bmp',
      'video/mp4': '.mp4',
      'video/quicktime': '.mov',
      'video/webm': '.webm',
    };
    const extension = extensionByMime[file.type.toLowerCase()] ??
      (file.type.startsWith('video/') ? '.mp4' : '.jpg');
    return `mobile-capture-${Date.now()}-${index + 1}${extension}`;
  }
}
