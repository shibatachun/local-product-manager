import { Injectable } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class BrowserSecretVaultService {
  private readonly databaseName = 'local-product-manager-secret-vault';
  private readonly storeName = 'keys';

  async load(name: string): Promise<string | null> {
    const stored = localStorage.getItem(`vault:${name}`);
    if (!stored) return null;
    try {
      const payload = JSON.parse(stored) as { iv: string; data: string };
      const key = await this.key(name, false);
      if (!key) return null;
      const clear = await crypto.subtle.decrypt(
        { name: 'AES-GCM', iv: this.fromBase64(payload.iv) }, key, this.fromBase64(payload.data));
      return new TextDecoder().decode(clear);
    } catch {
      await this.clear(name);
      return null;
    }
  }

  async save(name: string, value: string): Promise<void> {
    const key = await this.key(name, true);
    if (!key) throw new Error('Could not create a local encryption key.');
    const iv = crypto.getRandomValues(new Uint8Array(12));
    const encrypted = await crypto.subtle.encrypt({ name: 'AES-GCM', iv }, key, new TextEncoder().encode(value));
    localStorage.setItem(`vault:${name}`, JSON.stringify({ iv: this.toBase64(iv), data: this.toBase64(new Uint8Array(encrypted)) }));
  }

  async clear(name: string): Promise<void> {
    localStorage.removeItem(`vault:${name}`);
  }

  private async key(name: string, create: boolean): Promise<CryptoKey | null> {
    const database = await this.openDatabase();
    const existing = await new Promise<CryptoKey | undefined>((resolve, reject) => {
      const request = database.transaction(this.storeName).objectStore(this.storeName).get(name);
      request.onsuccess = () => resolve(request.result);
      request.onerror = () => reject(request.error);
    });
    if (existing || !create) { database.close(); return existing ?? null; }
    const generated = await crypto.subtle.generateKey({ name: 'AES-GCM', length: 256 }, false, ['encrypt', 'decrypt']);
    await new Promise<void>((resolve, reject) => {
      const request = database.transaction(this.storeName, 'readwrite').objectStore(this.storeName).put(generated, name);
      request.onsuccess = () => resolve();
      request.onerror = () => reject(request.error);
    });
    database.close();
    return generated;
  }

  private openDatabase(): Promise<IDBDatabase> {
    return new Promise((resolve, reject) => {
      const request = indexedDB.open(this.databaseName, 1);
      request.onupgradeneeded = () => request.result.createObjectStore(this.storeName);
      request.onsuccess = () => resolve(request.result);
      request.onerror = () => reject(request.error);
    });
  }

  private toBase64(value: Uint8Array): string {
    let binary = ''; value.forEach(byte => binary += String.fromCharCode(byte)); return btoa(binary);
  }
  private fromBase64(value: string): ArrayBuffer {
    const bytes = Uint8Array.from(atob(value), char => char.charCodeAt(0));
    return bytes.buffer as ArrayBuffer;
  }
}
