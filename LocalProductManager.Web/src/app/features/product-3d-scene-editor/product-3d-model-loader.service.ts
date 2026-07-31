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

    if (file.size > 75 * 1024 * 1024) {
      log('warning', 'Large model file', 'Network-drive reads can take a while. If this hangs, try copying the model locally or exporting a smaller GLB.');
    }

    log('info', extension === 'glb' ? 'Reading binary GLB data' : 'Reading text GLTF data');
    await this.yieldToBrowser();
    const data = extension === 'glb'
      ? await this.readArrayBuffer(file, log)
      : await this.readText(file, log);
    log(
      'success',
      'File read complete',
      data instanceof ArrayBuffer ? `${this.formatBytes(data.byteLength)} loaded into memory` : `${data.length.toLocaleString()} characters loaded`,
    );

    if (typeof data === 'string') {
      this.inspectGltfJson(data, log);
    }

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

  private async readArrayBuffer(file: File, log: ProductModelLoadLogger): Promise<ArrayBuffer> {
    log('info', 'Browser file read started', 'Using File.arrayBuffer()');
    try {
      const buffer = await this.withTimeout(
        file.arrayBuffer(),
        20000,
        'Reading model file timed out. The file may be on a slow network share or blocked by the browser.',
      );
      log('success', 'Browser file read finished', this.formatBytes(buffer.byteLength));
      return buffer;
    } catch (error) {
      log('error', 'Browser file read failed', error instanceof Error ? error.message : 'Unknown binary read error.');
      throw error;
    }
  }

  private async readText(file: File, log: ProductModelLoadLogger): Promise<string> {
    log('info', 'Browser file read started', 'Using File.text()');
    try {
      const text = await this.withTimeout(
        file.text(),
        20000,
        'Reading GLTF text timed out. The file may be on a slow network share or blocked by the browser.',
      );
      log('success', 'Browser text read finished', `${text.length.toLocaleString()} characters`);
      return text;
    } catch (error) {
      log('error', 'Browser text read failed', error instanceof Error ? error.message : 'Unknown text read error.');
      throw error;
    }
  }

  private inspectGltfJson(text: string, log: ProductModelLoadLogger): void {
    try {
      const parsed = JSON.parse(text) as {
        asset?: { version?: string; generator?: string };
        scenes?: unknown[];
        buffers?: Array<{ uri?: string }>;
        images?: Array<{ uri?: string }>;
      };
      const externalBuffers = (parsed.buffers ?? []).filter((buffer) => buffer.uri && !buffer.uri.startsWith('data:'));
      const externalImages = (parsed.images ?? []).filter((image) => image.uri && !image.uri.startsWith('data:'));
      log(
        'info',
        'GLTF JSON inspected',
        `asset ${parsed.asset?.version ?? 'unknown'} | ${parsed.scenes?.length ?? 0} scene(s) | generator ${parsed.asset?.generator ?? 'unknown'}`,
      );
      if (externalBuffers.length || externalImages.length) {
        log(
          'warning',
          'External GLTF dependencies detected',
          `${externalBuffers.length} buffer file(s), ${externalImages.length} image file(s). Single-file import cannot read sibling files yet; export as .glb if parsing fails.`,
        );
      }
    } catch (error) {
      log('error', 'GLTF JSON validation failed', error instanceof Error ? error.message : 'The text file is not valid JSON.');
      throw new Error('The selected .gltf file is not valid JSON.');
    }
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
