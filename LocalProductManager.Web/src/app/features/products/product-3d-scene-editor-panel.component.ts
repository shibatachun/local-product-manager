import { CommonModule } from '@angular/common';
import { AfterViewInit, ChangeDetectorRef, Component, ElementRef, HostListener, OnDestroy, ViewChild, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import * as THREE from 'three';
import { Product3dModelLoaderService, ProductModelLoadLogLevel } from './product-3d-model-loader.service';
import { Product3dSceneRendererService } from './product-3d-scene-renderer.service';

interface ProductModelLoadLogEntry {
  id: number;
  level: ProductModelLoadLogLevel;
  time: string;
  message: string;
  detail?: string;
}

interface ProductModelNodeEntry {
  uuid: string;
  name: string;
  type: string;
  depth: number;
  childCount: number;
  isMesh: boolean;
  isBone: boolean;
}

@Component({
  selector: 'app-product-3d-scene-editor-panel',
  imports: [CommonModule, FormsModule],
  templateUrl: './product-3d-scene-editor-panel.component.html',
  styleUrl: './product-3d-scene-editor-panel.component.css',
  providers: [Product3dSceneRendererService, Product3dModelLoaderService],
})
export class Product3dSceneEditorPanelComponent implements AfterViewInit, OnDestroy {
  @ViewChild('viewportHost') private viewportHost?: ElementRef<HTMLElement>;

  private readonly rendererService = inject(Product3dSceneRendererService);
  private readonly modelLoader = inject(Product3dModelLoaderService);
  private readonly changeDetector = inject(ChangeDetectorRef);

  readonly rendererOptions = ['WebGPU', 'WebGL2 fallback'];
  renderer = 'WebGPU';
  sceneName = 'Product scene draft';
  productScale = 1;
  environment = 'studio-softbox';
  gridVisible = true;
  shadowsEnabled = true;
  turntableEnabled = false;
  modelName = 'Three.js demo product';
  modelStatus = 'Demo scene running';
  animationCount = 0;
  loadingModel = false;
  loaderLogs: ProductModelLoadLogEntry[] = [];
  modelNodes: ProductModelNodeEntry[] = [];
  selectedNodeUuid = '';
  readonly webGpuSupported = 'gpu' in navigator;
  private logEntryId = 0;

  ngAfterViewInit(): void {
    queueMicrotask(() => {
      if (this.viewportHost?.nativeElement) {
        this.rendererService.mount(this.viewportHost.nativeElement, this.renderOptions());
        this.rendererService.onSelectionChanged((object) => this.syncSelectedNode(object));
        this.addLoaderLog('success', 'Renderer mounted', 'Three.js scene is ready for model imports.');
      }
    });
  }

  ngOnDestroy(): void {
    this.rendererService.dispose();
  }

  @HostListener('window:keydown', ['$event'])
  handleGlobalKeydown(event: KeyboardEvent): void {
    if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'z' && !event.shiftKey) {
      const undone = this.rendererService.undoLastAction();
      if (!undone) return;
      event.preventDefault();
      this.addLoaderLog('info', 'Undo applied', undone);
    }
  }

  async importModel(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;

    this.loadingModel = true;
    this.modelStatus = 'Loading model...';
    this.loaderLogs = [];
    this.addLoaderLog('info', 'Import started', file.name);
    try {
      const result = await this.modelLoader.load(file, (level, message, detail) => {
        this.addLoaderLog(level, message, detail);
      });
      this.modelNodes = this.flattenModelNodes(result.scene);
      this.addLoaderLog('info', 'Replacing product mesh in scene', result.name);
      this.rendererService.setProductModel(result.scene);
      this.selectedNodeUuid = result.scene.uuid;
      this.rendererService.selectObjectByUuid(this.selectedNodeUuid);
      this.modelName = result.name;
      this.animationCount = result.animationCount;
      this.modelStatus = result.animationCount
        ? `Loaded with ${result.animationCount} animation(s)`
        : 'Loaded static GLTF/GLB model';
      this.addLoaderLog('success', 'Model visible in viewport', this.modelStatus);
    } catch (error) {
      this.modelStatus = error instanceof Error ? error.message : 'Model import failed.';
      this.addLoaderLog('error', 'Model import failed', this.modelStatus);
    } finally {
      this.loadingModel = false;
      input.value = '';
      this.addLoaderLog('info', 'Import process finished');
    }
  }

  resetDemo(): void {
    this.rendererService.resetDemo();
    this.modelName = 'Three.js demo product';
    this.animationCount = 0;
    this.modelStatus = 'Demo scene running';
    this.modelNodes = [];
    this.selectedNodeUuid = '';
    this.addLoaderLog('info', 'Demo scene restored');
  }

  selectNode(node: ProductModelNodeEntry): void {
    this.selectedNodeUuid = node.uuid;
    this.rendererService.selectObjectByUuid(node.uuid);
  }

  applySceneOptions(): void {
    this.rendererService.applyOptions(this.renderOptions());
  }

  private renderOptions() {
    return {
      gridVisible: this.gridVisible,
      shadowsEnabled: this.shadowsEnabled,
      turntableEnabled: this.turntableEnabled,
      productScale: this.productScale,
    };
  }

  clearLoaderLogs(): void {
    this.loaderLogs = [];
  }

  private addLoaderLog(level: ProductModelLoadLogLevel, message: string, detail?: string): void {
    const now = new Date();
    this.loaderLogs = [
      ...this.loaderLogs,
      {
        id: ++this.logEntryId,
        level,
        time: now.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit' }),
        message,
        detail,
      },
    ].slice(-60);
  }

  private flattenModelNodes(root: THREE.Object3D): ProductModelNodeEntry[] {
    const nodes: ProductModelNodeEntry[] = [];
    const visit = (object: THREE.Object3D, depth: number): void => {
      nodes.push({
        uuid: object.uuid,
        name: object.name || object.type || 'Unnamed node',
        type: object.type,
        depth,
        childCount: object.children.length,
        isMesh: object instanceof THREE.Mesh,
        isBone: object instanceof THREE.Bone,
      });
      object.children.forEach((child) => visit(child, depth + 1));
    };
    visit(root, 0);
    return nodes;
  }

  private syncSelectedNode(object: THREE.Object3D | null): void {
    this.selectedNodeUuid = object?.uuid ?? '';
    this.changeDetector.detectChanges();
  }
}
