import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, OnDestroy, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { BroochFinderApiService, BroochFindMatch, BroochFindRecord } from '../../core/brooch-finder-api.service';
import { MediaUploadService } from '../../core/media-upload.service';
import { MediaPickerComponent } from '../../shared/media-picker.component';

@Component({
  selector: 'app-brooch-finder-panel',
  imports: [CommonModule, FormsModule, MediaPickerComponent],
  templateUrl: './brooch-finder-panel.component.html',
  styleUrl: './brooch-finder-panel.component.css',
})
export class BroochFinderPanelComponent implements OnDestroy {
  private readonly api = inject(BroochFinderApiService);
  private readonly media = inject(MediaUploadService);
  readonly records = signal<BroochFindRecord[]>([]);
  readonly matches = signal<BroochFindMatch[]>([]);
  readonly busy = signal(false);
  readonly message = signal('');
  readonly searched = signal(false);
  readonly selectedFile = signal<File | null>(null);
  readonly queryPreview = signal('');
  newLabel = '';
  newLocation = '';
  editId = '';
  editLabel = '';
  editLocation = '';

  constructor() { void this.refresh(); }

  ngOnDestroy(): void {
    if (this.queryPreview()) URL.revokeObjectURL(this.queryPreview());
  }

  async refresh(): Promise<void> {
    try { this.records.set(await this.api.list()); }
    catch { this.message.set('Could not load the brooch finder. Check the API connection.'); }
  }

  async chooseReference(event: Event): Promise<void> {
    const { input, files } = this.media.filesFrom(event);
    try {
      this.selectedFile.set((await this.media.prepareSelectedImages(files))[0] ?? null);
      if (this.selectedFile()) this.message.set(`Photo ready: ${this.selectedFile()!.name}`);
    } catch (error) { this.selectedFile.set(null); this.message.set(this.errorMessage(error)); }
    finally { input.value = ''; }
  }

  async create(): Promise<void> {
    if (!this.newLocation.trim() || !this.selectedFile() || this.busy()) {
      this.message.set('Enter a location and choose one real brooch photo.');
      return;
    }
    this.busy.set(true);
    this.message.set('');
    try {
      const file = this.selectedFile()!;
      await this.api.createWithPhoto(this.newLabel, this.newLocation, file);
      this.newLabel = '';
      this.newLocation = '';
      this.selectedFile.set(null);
      await this.refresh();
      this.message.set('Brooch saved. It is separate from inventory.');
    } catch (error) {
      await this.refresh();
      this.message.set(this.errorMessage(error));
    } finally { this.busy.set(false); }
  }

  async addPhoto(record: BroochFindRecord, event: Event): Promise<void> {
    const { input, files } = this.media.filesFrom(event);
    if (!files.length || this.busy()) { input.value = ''; return; }
    this.busy.set(true);
    try {
      for (const file of await this.media.prepareSelectedImages(files.slice(0, 6 - record.photos.length)))
        await this.api.addPhoto(record.id, file);
      await this.refresh();
      this.message.set('Reference photo added.');
    } catch (error) { this.message.set(this.errorMessage(error)); }
    finally { this.busy.set(false); input.value = ''; }
  }

  async find(event: Event): Promise<void> {
    const { input, files } = this.media.filesFrom(event);
    if (!files.length || this.busy()) { input.value = ''; return; }
    this.busy.set(true);
    this.matches.set([]);
    this.searched.set(false);
    const previous = this.queryPreview();
    if (previous) URL.revokeObjectURL(previous);
    try {
      const file = (await this.media.prepareSelectedImages([files[0]]))[0];
      this.queryPreview.set(URL.createObjectURL(file));
      this.matches.set(await this.api.search(file));
      this.searched.set(true);
      this.message.set(this.matches().length ? 'Check the photos before taking a brooch. Scores are experimental, not proof of identity.' : 'No brooches with reference photos yet.');
    } catch (error) { this.message.set(this.errorMessage(error)); }
    finally { this.busy.set(false); input.value = ''; }
  }

  startEdit(record: BroochFindRecord): void {
    this.editId = record.id;
    this.editLabel = record.label;
    this.editLocation = record.location;
  }

  async saveEdit(): Promise<void> {
    if (!this.editId || !this.editLocation.trim() || this.busy()) return;
    this.busy.set(true);
    try {
      await this.api.update(this.editId, this.editLabel, this.editLocation);
      this.editId = '';
      await this.refresh();
      this.message.set('Location updated.');
    } catch (error) { this.message.set(this.errorMessage(error)); }
    finally { this.busy.set(false); }
  }

  photoUrl(record: BroochFindRecord, photoId: string): string { return this.api.photoUrl(record.id, photoId); }
  shortId(record: BroochFindRecord): string { return `B-${record.id.slice(0, 6).toUpperCase()}`; }

  private errorMessage(error: unknown): string {
    if (error instanceof HttpErrorResponse && typeof error.error === 'string' && error.error.trim())
      return error.error;
    if (error instanceof Error) return error.message;
    return 'The action failed. Please try again.';
  }
}
