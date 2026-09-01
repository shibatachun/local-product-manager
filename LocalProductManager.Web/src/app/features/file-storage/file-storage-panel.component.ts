import { CommonModule } from '@angular/common';
import { Component, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { CurrentUser, ImageHostBrowseResult, ImageHostEntry, ImageStorageRoot } from '../../core/models';
import { ProductApiService } from '../../core/product-api.service';

type FileTreeNode = {
  entry: ImageHostEntry;
  kind: 'folder' | 'file';
  expanded: boolean;
  loading: boolean;
  children: FileTreeNode[] | null;
};

@Component({
  selector: 'app-file-storage-panel',
  imports: [CommonModule, FormsModule],
  templateUrl: './file-storage-panel.component.html',
  styleUrl: './file-storage-panel.component.css',
})
export class FileStoragePanelComponent {
  readonly currentUser = input<CurrentUser | null>(null);
  readonly roots = signal<ImageStorageRoot[]>([]);
  readonly host = signal<ImageHostBrowseResult | null>(null);
  readonly treeNodes = signal<FileTreeNode[]>([]);
  readonly newRoot = signal('');
  readonly hostPath = signal('');
  readonly notice = signal('');
  readonly loading = signal(false);

  constructor(private readonly api: ProductApiService) {}

  async ngOnInit(): Promise<void> {
    await this.loadRoots();
  }

  canManage(): boolean {
    return !!this.currentUser()?.canManageStorage;
  }

  async loadRoots(): Promise<void> {
    try {
      this.roots.set(await this.api.imageStorageRoots());
    } catch {
      this.roots.set([]);
    }
  }

  selectRoot(path: string): void {
    if (path) this.hostPath.set(path);
  }

  async addRoot(): Promise<void> {
    if (!this.canManage()) return this.show('Only admin can add file roots.');
    const path = this.newRoot().trim();
    if (!path) return this.show('Enter a local or UNC folder path.');

    await this.run(async () => {
      const root = await this.api.addImageStorageRoot(path);
      this.newRoot.set('');
      await this.loadRoots();
      this.hostPath.set(root.path);
      if (root.available) await this.browse(root.path);
      this.show(root.available ? 'File folder added and is accessible.' : 'File folder added, but the server cannot currently access it.');
    });
  }

  async removeRoot(root: ImageStorageRoot): Promise<void> {
    if (!this.canManage()) return this.show('Only admin can remove file roots.');
    if (!confirm(`Remove this file folder from the whitelist?\n${root.path}`)) return;
    await this.run(async () => {
      await this.api.deleteImageStorageRoot(root.path);
      await this.loadRoots();
      this.show('File folder removed from the whitelist.');
    });
  }

  async browse(path = this.hostPath()): Promise<void> {
    const trimmed = path.trim();
    if (!trimmed) return this.show('Enter or choose a file host folder first.');
    await this.run(async () => {
      const result = await this.api.browseImageHost(trimmed);
      this.host.set(result);
      this.treeNodes.set(this.toTreeNodes(result));
      this.hostPath.set(result.path);
      this.show(`${result.folders.length} folder(s), ${this.files(result).length} file(s) loaded.`);
    });
  }

  async openFolder(path: string): Promise<void> {
    await this.browse(path);
  }

  async toggleFolder(node: FileTreeNode): Promise<void> {
    if (node.loading) return;
    if (node.expanded) {
      node.expanded = false;
      this.refreshTree();
      return;
    }
    if (node.children !== null) {
      node.expanded = true;
      this.refreshTree();
      return;
    }
    node.loading = true;
    this.refreshTree();
    try {
      const result = await this.api.browseImageHost(node.entry.path);
      node.children = this.toTreeNodes(result);
      node.expanded = true;
    } catch (error) {
      this.show(error instanceof Error ? error.message : 'Could not open this folder.');
    } finally {
      node.loading = false;
      this.refreshTree();
    }
  }

  files(host: ImageHostBrowseResult): ImageHostEntry[] {
    return host.files?.length ? host.files : host.images;
  }

  isImage(file: ImageHostEntry): boolean {
    const extension = (file.extension || file.name.split('.').pop() || '').toLowerCase().replace(/^\./, '');
    return ['jpg', 'jpeg', 'png', 'webp', 'bmp', 'gif'].includes(extension);
  }

  fileLabel(file: ImageHostEntry): string {
    return (file.extension || file.name.split('.').pop() || 'file').replace(/^\./, '').toUpperCase();
  }

  fileSize(size: number | null): string {
    if (size === null) return '';
    if (size < 1024) return `${size} B`;
    if (size < 1024 * 1024) return `${(size / 1024).toFixed(1)} KB`;
    return `${(size / 1024 / 1024).toFixed(1)} MB`;
  }

  previewUrl(path: string): string {
    return this.api.imagePreviewUrl(path);
  }

  downloadUrl(path: string): string {
    return this.api.imageDownloadUrl(path);
  }

  hideBrokenImage(event: Event): void {
    (event.target as HTMLImageElement).style.display = 'none';
  }

  private toTreeNodes(host: ImageHostBrowseResult): FileTreeNode[] {
    return [
      ...host.folders.map(entry => ({ entry, kind: 'folder' as const, expanded: false, loading: false, children: null })),
      ...this.files(host).map(entry => ({ entry, kind: 'file' as const, expanded: false, loading: false, children: null })),
    ];
  }

  private refreshTree(): void {
    this.treeNodes.update(nodes => [...nodes]);
  }

  private async run(action: () => Promise<void>): Promise<void> {
    this.loading.set(true);
    try {
      await action();
    } catch (error) {
      this.show(error instanceof Error ? error.message : 'Action failed.');
    } finally {
      this.loading.set(false);
    }
  }

  private show(message: string): void {
    this.notice.set(message);
    window.setTimeout(() => {
      if (this.notice() === message) this.notice.set('');
    }, 3600);
  }
}
