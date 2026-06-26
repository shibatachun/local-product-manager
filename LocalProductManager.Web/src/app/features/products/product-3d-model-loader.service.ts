import { Injectable } from '@angular/core';
import * as THREE from 'three';
import { GLTFLoader } from 'three/examples/jsm/loaders/GLTFLoader.js';

export interface ProductModelLoadResult {
  name: string;
  scene: THREE.Object3D;
  animationCount: number;
}

export type ProductModelLoadLogLevel = 'info' | 'success' | 'warning' | 'error';
export type ProductModelLoadLogger = (level: ProductModelLoadLogLevel, message: string, detail?: string) => void;

@Injectable()
export class Product3dModelLoaderService {
  private readonly loader = new GLTFLoader();

  async load(file: File, log: ProductModelLoadLogger = () => undefined): Promise<ProductModelLoadResult> {
    log('info', 'Selected model file', `${file.name} | ${this.formatBytes(file.size)} | ${file.type || 'unknown type'}`);

    const extension = file.name.split('.').pop()?.toLowerCase();
    log('info', 'Detected file extension', extension ?? 'none');

    if (extension !== 'glb' && extension !== 'gltf') {
      log('error', 'Unsupported model file', 'Only .glb and .gltf files can be parsed by the current loader.');
      throw new Error('Only GLB and GLTF model files are supported.');
    }

    log('info', extension === 'glb' ? 'Reading binary GLB data' : 'Reading text GLTF data');
    const data = extension === 'glb'
      ? await this.readArrayBuffer(file)
      : await this.readText(file);
    log(
      'success',
      'File read complete',
      data instanceof ArrayBuffer ? `${this.formatBytes(data.byteLength)} loaded into memory` : `${data.length.toLocaleString()} characters loaded`,
    );

    log('info', 'Starting GLTF parser', 'If loading stops here, the model may reference missing external .bin or texture files, or the parser is blocked.');
    await this.yieldToBrowser();

    const gltf = await this.withTimeout(
      this.loader.parseAsync(data, ''),
      15000,
      'Model parsing timed out. Try a binary .glb file or an embedded .gltf without external texture/bin dependencies.',
    );
    log('success', 'GLTF parser finished', `${gltf.scene.children.length} root object(s), ${gltf.animations.length} animation(s)`);

    gltf.scene.name = file.name;
    return {
      name: file.name,
      scene: gltf.scene,
      animationCount: gltf.animations.length,
    };
  }

  private async withTimeout<T>(promise: Promise<T>, timeoutMs: number, message: string): Promise<T> {
    let timeoutId: number | undefined;
    const timeout = new Promise<never>((_, reject) => {
      timeoutId = window.setTimeout(() => reject(new Error(message)), timeoutMs);
    });
    try {
      return await Promise.race([promise, timeout]);
    } finally {
      if (timeoutId) window.clearTimeout(timeoutId);
    }
  }

  private readArrayBuffer(file: File): Promise<ArrayBuffer> {
    return this.withTimeout(new Promise<ArrayBuffer>((resolve, reject) => {
      const reader = new FileReader();
      reader.onerror = () => reject(reader.error ?? new Error('Unable to read model file.'));
      reader.onload = () => {
        if (reader.result instanceof ArrayBuffer) resolve(reader.result);
        else reject(new Error('Model file did not read as binary data.'));
      };
      reader.readAsArrayBuffer(file);
    }), 10000, 'Reading model file timed out.');
  }

  private readText(file: File): Promise<string> {
    return this.withTimeout(new Promise<string>((resolve, reject) => {
      const reader = new FileReader();
      reader.onerror = () => reject(reader.error ?? new Error('Unable to read model file.'));
      reader.onload = () => resolve(String(reader.result ?? ''));
      reader.readAsText(file);
    }), 10000, 'Reading model file timed out.');
  }

  private formatBytes(bytes: number): string {
    if (!Number.isFinite(bytes) || bytes <= 0) return '0 B';
    const units = ['B', 'KB', 'MB', 'GB'];
    const unitIndex = Math.min(Math.floor(Math.log(bytes) / Math.log(1024)), units.length - 1);
    const value = bytes / Math.pow(1024, unitIndex);
    return `${value.toFixed(value >= 10 || unitIndex === 0 ? 0 : 1)} ${units[unitIndex]}`;
  }

  private yieldToBrowser(): Promise<void> {
    return new Promise((resolve) => window.requestAnimationFrame(() => resolve()));
  }
}
