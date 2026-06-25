import { Injectable } from '@angular/core';
import * as THREE from 'three';
import { GLTFLoader } from 'three/examples/jsm/loaders/GLTFLoader.js';

export interface ProductModelLoadResult {
  name: string;
  scene: THREE.Object3D;
  animationCount: number;
}

@Injectable()
export class Product3dModelLoaderService {
  private readonly loader = new GLTFLoader();

  async load(file: File): Promise<ProductModelLoadResult> {
    const extension = file.name.split('.').pop()?.toLowerCase();
    if (extension !== 'glb' && extension !== 'gltf') {
      throw new Error('Only GLB and GLTF model files are supported.');
    }

    const data = extension === 'glb'
      ? await this.readArrayBuffer(file)
      : await this.readText(file);
    const gltf = await this.withTimeout(
      this.loader.parseAsync(data, ''),
      15000,
      'Model parsing timed out. Try a binary .glb file or an embedded .gltf without external texture/bin dependencies.',
    );
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
}
