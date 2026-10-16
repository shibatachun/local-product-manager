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

  async prepareSelectedImages(files: File[]): Promise<File[]> {
    // Some mobile file pickers revoke their File handles after the input is
    // cleared. Copy bytes while the selection is still live, then decode or
    // upload only the stable in-memory copies.
    const copies = await Promise.all(files.map(async (file, index) => {
      const bytes = await this.readFileBytes(file);
      return new File([bytes], this.uploadFileName(file, index), {
        type: file.type,
        lastModified: file.lastModified || Date.now(),
      });
    }));
    return this.prepareCameraImages(copies);
  }

  async uploadMultipart<T>(url: string, files: File[], fieldName = 'files', fields: Record<string, string> = {}): Promise<T> {
    const data = new FormData();
    for (const [name, value] of Object.entries(fields)) data.append(name, value);
    for (const [index, file] of files.entries()) {
      const bytes = await this.readFileBytes(file);
      data.append(fieldName, new Blob([bytes], { type: file.type || 'application/octet-stream' }), this.uploadFileName(file, index));
    }
    return firstValueFrom(this.http.post<T>(url, data));
  }

  async uploadCapturedVideo<T>(url: string, file: File): Promise<T> {
    const bytes = await this.readFileBytes(file);
    const contentType = file.type || 'video/mp4';
    return firstValueFrom(this.http.post<T>(url, new Blob([bytes], { type: contentType }), {
      headers: new HttpHeaders({
        'Content-Type': contentType,
        'X-Upload-File-Name': this.uploadFileName(file, 0),
      }),
    }));
  }

  async readFileBytes(file: File): Promise<ArrayBuffer> {
    const bytes = await file.arrayBuffer();
    if (bytes.byteLength === 0 || bytes.byteLength !== file.size)
      throw new Error(`The browser could not read “${file.name || 'selected file'}” (${file.size} B selected, ${bytes.byteLength} B read). Choose the file again.`);
    return bytes;
  }

  private async prepareCameraImage(file: File): Promise<File> {
    const sourceUrl = URL.createObjectURL(file);
    try {
      const image = new Image();
      image.decoding = 'async';
      await new Promise<void>((resolve, reject) => {
        const timeout = setTimeout(() => reject(new Error('Camera image decoding timed out.')), 15000);
        image.onload = () => { clearTimeout(timeout); resolve(); };
        image.onerror = () => { clearTimeout(timeout); reject(new Error('The captured image could not be decoded.')); };
        image.src = sourceUrl;
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

      const blob = await new Promise<Blob | null>(resolve => {
        const timeout = setTimeout(() => resolve(null), 15000);
        canvas.toBlob(result => { clearTimeout(timeout); resolve(result); }, 'image/jpeg', .86);
      });
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
