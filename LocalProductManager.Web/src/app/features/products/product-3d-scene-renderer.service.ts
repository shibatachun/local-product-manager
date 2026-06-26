import { Injectable } from '@angular/core';
import * as THREE from 'three';
import { OrbitControls } from 'three/examples/jsm/controls/OrbitControls.js';

export interface ProductSceneRenderOptions {
  gridVisible: boolean;
  shadowsEnabled: boolean;
  turntableEnabled: boolean;
  productScale: number;
}

export type ProductSceneSelectionHandler = (object: THREE.Object3D | null) => void;

@Injectable()
export class Product3dSceneRendererService {
  private host: HTMLElement | null = null;
  private scene: THREE.Scene | null = null;
  private camera: THREE.PerspectiveCamera | null = null;
  private renderer: THREE.WebGLRenderer | null = null;
  private controls: OrbitControls | null = null;
  private resizeObserver: ResizeObserver | null = null;
  private frameId = 0;
  private demoMesh: THREE.Object3D | null = null;
  private productModel: THREE.Object3D | null = null;
  private selectedObject: THREE.Object3D | null = null;
  private selectionBox: THREE.BoxHelper | null = null;
  private grid: THREE.GridHelper | null = null;
  private floor: THREE.Mesh | null = null;
  private raycaster = new THREE.Raycaster();
  private pointer = new THREE.Vector2();
  private draggingObject: THREE.Object3D | null = null;
  private dragPlane = new THREE.Plane();
  private dragOffset = new THREE.Vector3();
  private keyboardFocused = false;
  private pressedKeys = new Set<string>();
  private lastFrameTime = 0;
  private selectionHandler: ProductSceneSelectionHandler | null = null;
  private options: ProductSceneRenderOptions = {
    gridVisible: true,
    shadowsEnabled: true,
    turntableEnabled: false,
    productScale: 1,
  };

  mount(host: HTMLElement, options: ProductSceneRenderOptions): void {
    this.dispose();
    this.host = host;
    this.options = { ...options };
    this.scene = new THREE.Scene();
    this.scene.background = new THREE.Color(0x17212b);

    this.camera = new THREE.PerspectiveCamera(45, 1, 0.1, 100);
    this.camera.position.set(4, 3, 5);

    this.renderer = new THREE.WebGLRenderer({ antialias: true, alpha: false, preserveDrawingBuffer: true });
    this.renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
    this.renderer.shadowMap.enabled = this.options.shadowsEnabled;
    this.renderer.shadowMap.type = THREE.PCFSoftShadowMap;
    this.renderer.domElement.tabIndex = 0;
    host.appendChild(this.renderer.domElement);

    this.controls = new OrbitControls(this.camera, this.renderer.domElement);
    this.controls.enableDamping = true;
    this.controls.target.set(0, 0.8, 0);

    this.addLights();
    this.addFloor();
    this.addDemoModel();
    this.addPointerEvents();
    this.addKeyboardEvents();
    this.applyOptions(options);
    this.resize();
    this.resizeObserver = new ResizeObserver(() => this.resize());
    this.resizeObserver.observe(host);
    this.renderLoop();
  }

  applyOptions(options: ProductSceneRenderOptions): void {
    this.options = { ...options };
    if (this.renderer) {
      this.renderer.shadowMap.enabled = this.options.shadowsEnabled;
    }
    if (this.grid) {
      this.grid.visible = this.options.gridVisible;
    }
    if (this.floor) {
      this.floor.receiveShadow = this.options.shadowsEnabled;
    }
    this.scene?.traverse((object) => {
      if (object instanceof THREE.Mesh) {
        object.castShadow = this.options.shadowsEnabled;
        object.receiveShadow = this.options.shadowsEnabled;
      }
    });
    this.applyModelScale();
  }

  setProductModel(model: THREE.Object3D): void {
    if (!this.scene) return;
    this.clearObject(this.productModel);
    this.productModel = model;
    this.selectObject(null);
    this.productModel.name = model.name || 'Imported product model';
    this.normalizeModel(this.productModel);
    this.scene.add(this.productModel);
    if (this.demoMesh) {
      this.demoMesh.visible = false;
    }
    this.applyOptions(this.options);
  }

  resetDemo(): void {
    this.clearObject(this.productModel);
    this.productModel = null;
    this.selectObject(null);
    if (this.demoMesh) {
      this.demoMesh.visible = true;
    }
  }

  dispose(): void {
    cancelAnimationFrame(this.frameId);
    this.resizeObserver?.disconnect();
    this.resizeObserver = null;
    this.controls?.dispose();
    this.controls = null;
    this.pressedKeys.clear();
    this.draggingObject = null;
    this.selectionHandler = null;
    this.clearObject(this.productModel);
    this.clearObject(this.demoMesh);
    this.clearObject(this.selectionBox);
    this.scene?.traverse((object) => {
      if (object instanceof THREE.Mesh) {
        object.geometry?.dispose();
        const materials = Array.isArray(object.material) ? object.material : [object.material];
        materials.forEach((material) => material.dispose());
      }
    });
    this.renderer?.dispose();
    this.renderer?.domElement.remove();
    this.host = null;
    this.scene = null;
    this.camera = null;
    this.renderer = null;
    this.demoMesh = null;
    this.productModel = null;
    this.grid = null;
    this.floor = null;
    this.selectionBox = null;
  }

  onSelectionChanged(handler: ProductSceneSelectionHandler | null): void {
    this.selectionHandler = handler;
  }

  selectObjectByUuid(uuid: string | null): void {
    if (!uuid) {
      this.selectObject(null);
      return;
    }
    const object = this.scene?.getObjectByProperty('uuid', uuid) ?? null;
    this.selectObject(object);
  }

  private addLights(): void {
    if (!this.scene) return;
    const hemi = new THREE.HemisphereLight(0xffffff, 0x4b5d66, 1.8);
    this.scene.add(hemi);

    const key = new THREE.DirectionalLight(0xffffff, 2.4);
    key.position.set(4, 6, 5);
    key.castShadow = true;
    key.shadow.mapSize.set(1024, 1024);
    this.scene.add(key);

    const rim = new THREE.DirectionalLight(0x9bdcf0, 1.1);
    rim.position.set(-4, 3, -4);
    this.scene.add(rim);
  }

  private addFloor(): void {
    if (!this.scene) return;
    this.grid = new THREE.GridHelper(8, 16, 0x8bc7d4, 0x35515c);
    this.scene.add(this.grid);

    const geometry = new THREE.PlaneGeometry(8, 8);
    const material = new THREE.ShadowMaterial({ color: 0x000000, opacity: 0.28 });
    this.floor = new THREE.Mesh(geometry, material);
    this.floor.rotation.x = -Math.PI / 2;
    this.floor.position.y = -0.02;
    this.floor.receiveShadow = true;
    this.scene.add(this.floor);
  }

  private addDemoModel(): void {
    if (!this.scene) return;
    const group = new THREE.Group();
    group.name = 'Three.js demo product';

    const box = new THREE.Mesh(
      new THREE.BoxGeometry(1.25, 1.25, 1.25),
      new THREE.MeshStandardMaterial({ color: 0x38a8c2, roughness: 0.45, metalness: 0.12 }),
    );
    box.position.y = 0.72;
    group.add(box);

    const torus = new THREE.Mesh(
      new THREE.TorusKnotGeometry(0.42, 0.12, 96, 16),
      new THREE.MeshStandardMaterial({ color: 0xf3c969, roughness: 0.28, metalness: 0.35 }),
    );
    torus.position.set(0.05, 0.72, 0.05);
    group.add(torus);

    group.traverse((object) => {
      if (object instanceof THREE.Mesh) {
        object.castShadow = true;
        object.receiveShadow = true;
      }
    });
    this.demoMesh = group;
    this.scene.add(group);
  }

  private normalizeModel(model: THREE.Object3D): void {
    model.updateMatrixWorld(true);
    const box = new THREE.Box3().setFromObject(model);
    const size = box.getSize(new THREE.Vector3());
    const center = box.getCenter(new THREE.Vector3());
    const maxAxis = Math.max(size.x, size.y, size.z);
    const baseScale = maxAxis > 0 ? 2.2 / maxAxis : 1;
    model.position.sub(center);
    model.scale.setScalar(baseScale);
    model.position.y += (size.y * baseScale) / 2;
    this.applyModelScale();
  }

  private applyModelScale(): void {
    if (!this.productModel) return;
    const scale = Math.max(0.1, this.options.productScale || 1);
    this.productModel.scale.multiplyScalar(scale / this.currentModelUserScale());
    this.productModel.userData['productScale'] = scale;
  }

  private currentModelUserScale(): number {
    return Number(this.productModel?.userData['productScale']) || 1;
  }

  private renderLoop = (time = performance.now()): void => {
    if (!this.renderer || !this.scene || !this.camera) return;
    this.frameId = requestAnimationFrame(this.renderLoop);
    const deltaSeconds = Math.min(0.05, Math.max(0, (time - this.lastFrameTime) / 1000 || 0));
    this.lastFrameTime = time;
    this.moveCameraFromKeyboard(deltaSeconds);
    if (this.options.turntableEnabled) {
      const object = this.productModel ?? this.demoMesh;
      if (object) object.rotation.y += 0.008;
    }
    this.controls?.update();
    this.selectionBox?.update();
    this.renderer.render(this.scene, this.camera);
  };

  private resize(): void {
    if (!this.host || !this.renderer || !this.camera) return;
    const width = Math.max(1, this.host.clientWidth);
    const height = Math.max(1, this.host.clientHeight);
    this.camera.aspect = width / height;
    this.camera.updateProjectionMatrix();
    this.renderer.setSize(width, height, false);
  }

  private clearObject(object: THREE.Object3D | null): void {
    if (!object) return;
    object.parent?.remove(object);
    object.traverse((child) => {
      if (child instanceof THREE.Mesh) {
        child.geometry?.dispose();
        const materials = Array.isArray(child.material) ? child.material : [child.material];
        materials.forEach((material) => material.dispose());
      }
    });
  }

  private addPointerEvents(): void {
    const canvas = this.renderer?.domElement;
    if (!canvas) return;
    canvas.addEventListener('pointerdown', this.handlePointerDown);
    canvas.addEventListener('pointermove', this.handlePointerMove);
    canvas.addEventListener('pointerup', this.handlePointerUp);
    canvas.addEventListener('pointercancel', this.handlePointerUp);
    canvas.addEventListener('pointerleave', this.handlePointerUp);
  }

  private addKeyboardEvents(): void {
    const canvas = this.renderer?.domElement;
    if (!canvas) return;
    canvas.addEventListener('focus', () => { this.keyboardFocused = true; });
    canvas.addEventListener('blur', () => {
      this.keyboardFocused = false;
      this.pressedKeys.clear();
    });
    canvas.addEventListener('keydown', (event) => {
      const key = event.key.toLowerCase();
      if (['w', 'a', 's', 'd'].includes(key)) {
        event.preventDefault();
        this.pressedKeys.add(key);
      }
    });
    canvas.addEventListener('keyup', (event) => {
      this.pressedKeys.delete(event.key.toLowerCase());
    });
  }

  private handlePointerDown = (event: PointerEvent): void => {
    const canvas = this.renderer?.domElement;
    if (!canvas || !this.camera) return;
    canvas.focus();
    this.keyboardFocused = true;
    this.updatePointer(event);

    const clickedObject = this.findIntersectedObject();
    if (clickedObject) {
      this.selectObject(clickedObject);
    }

    const dragTarget = this.selectedObject && this.isDraggable(this.selectedObject)
      ? this.selectedObject
      : null;
    if (!dragTarget || !this.pointerHitsObject(dragTarget)) return;

    const worldPosition = new THREE.Vector3();
    dragTarget.getWorldPosition(worldPosition);
    const cameraDirection = new THREE.Vector3();
    this.camera.getWorldDirection(cameraDirection);
    this.dragPlane.setFromNormalAndCoplanarPoint(cameraDirection, worldPosition);

    const intersection = this.intersectDragPlane();
    if (!intersection) return;
    this.dragOffset.copy(intersection).sub(worldPosition);
    this.draggingObject = dragTarget;
    this.controls!.enabled = false;
    canvas.setPointerCapture(event.pointerId);
  };

  private handlePointerMove = (event: PointerEvent): void => {
    if (!this.draggingObject) return;
    this.updatePointer(event);
    const intersection = this.intersectDragPlane();
    if (!intersection) return;

    const targetWorldPosition = intersection.sub(this.dragOffset);
    const parent = this.draggingObject.parent;
    const targetLocalPosition = parent ? parent.worldToLocal(targetWorldPosition.clone()) : targetWorldPosition;
    this.draggingObject.position.copy(targetLocalPosition);
  };

  private handlePointerUp = (event: PointerEvent): void => {
    const canvas = this.renderer?.domElement;
    if (canvas?.hasPointerCapture(event.pointerId)) {
      canvas.releasePointerCapture(event.pointerId);
    }
    this.draggingObject = null;
    if (this.controls) this.controls.enabled = true;
  };

  private updatePointer(event: PointerEvent): void {
    const canvas = this.renderer?.domElement;
    if (!canvas) return;
    const rect = canvas.getBoundingClientRect();
    this.pointer.x = ((event.clientX - rect.left) / rect.width) * 2 - 1;
    this.pointer.y = -((event.clientY - rect.top) / rect.height) * 2 + 1;
  }

  private findIntersectedObject(): THREE.Object3D | null {
    if (!this.camera) return null;
    const root = this.productModel ?? this.demoMesh;
    if (!root) return null;
    this.raycaster.setFromCamera(this.pointer, this.camera);
    const hit = this.raycaster.intersectObject(root, true)[0]?.object ?? null;
    return hit ? this.draggableAncestor(hit) ?? hit : null;
  }

  private pointerHitsObject(object: THREE.Object3D): boolean {
    if (!this.camera) return false;
    this.raycaster.setFromCamera(this.pointer, this.camera);
    return this.raycaster.intersectObject(object, true).length > 0;
  }

  private intersectDragPlane(): THREE.Vector3 | null {
    if (!this.camera) return null;
    this.raycaster.setFromCamera(this.pointer, this.camera);
    return this.raycaster.ray.intersectPlane(this.dragPlane, new THREE.Vector3());
  }

  private selectObject(object: THREE.Object3D | null): void {
    this.selectedObject = object;
    this.clearObject(this.selectionBox);
    this.selectionBox = null;
    if (object && this.scene) {
      this.selectionBox = new THREE.BoxHelper(object, 0xf3c969);
      this.selectionBox.name = 'Selection bounding box';
      this.scene.add(this.selectionBox);
    }
    this.selectionHandler?.(object);
  }

  private isDraggable(object: THREE.Object3D): boolean {
    return object instanceof THREE.Mesh || object instanceof THREE.Group || object.type === 'Object3D';
  }

  private draggableAncestor(object: THREE.Object3D): THREE.Object3D | null {
    let current: THREE.Object3D | null = object;
    while (current && current !== this.productModel && current !== this.demoMesh) {
      if (this.isDraggable(current)) return current;
      current = current.parent;
    }
    return object instanceof THREE.Mesh ? object : null;
  }

  private moveCameraFromKeyboard(deltaSeconds: number): void {
    if (!this.keyboardFocused || !this.camera || !this.controls || !this.pressedKeys.size) return;

    const speed = 3.2 * deltaSeconds;
    const forward = new THREE.Vector3();
    this.camera.getWorldDirection(forward);
    forward.y = 0;
    forward.normalize();

    const right = new THREE.Vector3().crossVectors(forward, this.camera.up).normalize();
    const movement = new THREE.Vector3();
    if (this.pressedKeys.has('w')) movement.add(forward);
    if (this.pressedKeys.has('s')) movement.sub(forward);
    if (this.pressedKeys.has('d')) movement.add(right);
    if (this.pressedKeys.has('a')) movement.sub(right);
    if (!movement.lengthSq()) return;

    movement.normalize().multiplyScalar(speed);
    this.camera.position.add(movement);
    this.controls.target.add(movement);
  }
}
