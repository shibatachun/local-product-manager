import { CommonModule } from '@angular/common';
import { Component } from '@angular/core';
import { FormsModule } from '@angular/forms';

@Component({
  selector: 'app-product-3d-scene-editor-panel',
  imports: [CommonModule, FormsModule],
  templateUrl: './product-3d-scene-editor-panel.component.html',
  styleUrl: './product-3d-scene-editor-panel.component.css',
})
export class Product3dSceneEditorPanelComponent {
  readonly rendererOptions = ['WebGPU', 'WebGL2 fallback'];
  renderer = 'WebGPU';
  sceneName = 'Product scene draft';
  productScale = 1;
  environment = 'studio-softbox';
  gridVisible = true;
  shadowsEnabled = true;
  turntableEnabled = false;
}
