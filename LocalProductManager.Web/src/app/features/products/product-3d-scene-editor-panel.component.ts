import { CommonModule } from '@angular/common';
import { AfterViewInit, Component, ElementRef, OnDestroy, ViewChild, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Product3dModelLoaderService } from './product-3d-model-loader.service';
import { Product3dSceneRendererService } from './product-3d-scene-renderer.service';

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
  readonly webGpuSupported = 'gpu' in navigator;

  ngAfterViewInit(): void {
    queueMicrotask(() => {
      if (this.viewportHost?.nativeElement) {
        this.rendererService.mount(this.viewportHost.nativeElement, this.renderOptions());
      }
    });
  }

  ngOnDestroy(): void {
    this.rendererService.dispose();
  }

  async importModel(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;

    this.loadingModel = true;
    this.modelStatus = 'Loading model...';
    try {
      const result = await this.modelLoader.load(file);
      this.rendererService.setProductModel(result.scene);
      this.modelName = result.name;
      this.animationCount = result.animationCount;
      this.modelStatus = result.animationCount
        ? `Loaded with ${result.animationCount} animation(s)`
        : 'Loaded static GLTF/GLB model';
    } catch (error) {
      this.modelStatus = error instanceof Error ? error.message : 'Model import failed.';
    } finally {
      this.loadingModel = false;
      input.value = '';
    }
  }

  resetDemo(): void {
    this.rendererService.resetDemo();
    this.modelName = 'Three.js demo product';
    this.animationCount = 0;
    this.modelStatus = 'Demo scene running';
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
}
