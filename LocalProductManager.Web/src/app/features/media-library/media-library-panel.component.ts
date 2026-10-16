import { CommonModule } from '@angular/common';
import { Component, ElementRef, OnDestroy, ViewChild, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MediaLibraryApiService, MediaLibraryFile, MediaLibraryFolder } from '../../core/media-library-api.service';
import { MediaUploadService } from '../../core/media-upload.service';
import { CurrentUser } from '../../core/models';
import { MediaPickerComponent } from '../../shared/media-picker.component';

@Component({
  selector: 'app-media-library-panel',
  imports: [CommonModule, FormsModule, MediaPickerComponent],
  templateUrl: './media-library-panel.component.html',
  styleUrl: './media-library-panel.component.css',
})
export class MediaLibraryPanelComponent implements OnDestroy {
  @ViewChild('recordingPreview') recordingPreview?: ElementRef<HTMLVideoElement>;
  readonly currentUser = input<CurrentUser | null>(null);
  private readonly api = inject(MediaLibraryApiService);
  private readonly uploads = inject(MediaUploadService);
  readonly folders = signal<MediaLibraryFolder[]>([]);
  readonly selectedFolder = signal('');
  readonly photos = signal<MediaLibraryFile[]>([]);
  readonly videos = signal<MediaLibraryFile[]>([]);
  readonly newFolderName = signal('');
  readonly busy = signal(false);
  readonly message = signal('');
  readonly recordingState = signal<'idle' | 'starting' | 'recording' | 'saving'>('idle');
  readonly recordingResolution = signal('');
  private recordingStream?: MediaStream;
  private recorder?: MediaRecorder;
  private recordingChunks: Blob[] = [];
  private recordingBytes = 0;
  private recordingFolder = '';
  private discardRecording = false;
  private destroyed = false;

  constructor() { void this.loadFolders(); }

  ngOnDestroy(): void {
    this.destroyed = true;
    this.discardRecording = true;
    if (this.recorder?.state === 'recording') this.recorder.stop();
    this.releaseRecordingCamera();
  }

  async startRecording(): Promise<void> {
    const folder = this.selectedFolder();
    if (!folder || this.busy() || this.recordingState() !== 'idle') return;
    if (!navigator.mediaDevices?.getUserMedia || typeof MediaRecorder === 'undefined') {
      this.message.set('HD recording is unavailable in this browser. Use Device camera or Upload videos.');
      return;
    }
    this.recordingState.set('starting');
    this.recordingFolder = folder;
    this.recordingResolution.set('');
    this.discardRecording = false;
    try {
      const stream = await navigator.mediaDevices.getUserMedia({
        audio: true,
        video: { facingMode: { ideal: 'environment' }, width: { ideal: 1920 }, height: { ideal: 1080 }, frameRate: { ideal: 30 } },
      });
      if (this.destroyed || this.recordingState() !== 'starting') {
        stream.getTracks().forEach(track => track.stop());
        return;
      }
      this.recordingStream = stream;
      const settings = stream.getVideoTracks()[0]?.getSettings();
      this.recordingResolution.set(settings?.width && settings?.height ? `${settings.width} × ${settings.height}` : 'Resolution unavailable');
      if (this.recordingPreview) this.recordingPreview.nativeElement.srcObject = stream;
      const mimeType = ['video/mp4', 'video/webm;codecs=vp9,opus', 'video/webm;codecs=vp8,opus', 'video/webm']
        .find(type => MediaRecorder.isTypeSupported(type));
      const recorder = new MediaRecorder(stream, {
        ...(mimeType ? { mimeType } : {}),
        videoBitsPerSecond: 8_000_000,
        audioBitsPerSecond: 128_000,
      });
      this.recorder = recorder;
      this.recordingChunks = [];
      this.recordingBytes = 0;
      recorder.ondataavailable = event => {
        if (!event.data.size || this.discardRecording) return;
        this.recordingChunks.push(event.data);
        this.recordingBytes += event.data.size;
        // Leave headroom for the last encoder chunk and the API's 200 MB limit.
        if (this.recordingBytes >= 190_000_000 && recorder.state === 'recording') recorder.stop();
      };
      recorder.onstop = () => void this.finishRecording(recorder.mimeType);
      recorder.start(1000);
      this.recordingState.set('recording');
      this.message.set('Recording in HD when supported by this camera. Stop to save the video.');
    } catch {
      this.releaseRecordingCamera();
      this.recordingState.set('idle');
      this.message.set('Could not start HD recording. Allow camera/microphone access, use HTTPS, or choose Device camera.');
    }
  }

  stopRecording(): void {
    if (this.recordingState() === 'recording' && this.recorder?.state === 'recording') this.recorder.stop();
  }

  cancelRecording(): void {
    if (this.recordingState() !== 'starting' && this.recordingState() !== 'recording') return;
    this.discardRecording = true;
    if (this.recorder?.state === 'recording') this.recorder.stop();
    else { this.releaseRecordingCamera(); this.recordingState.set('idle'); }
  }

  private async finishRecording(mimeType: string): Promise<void> {
    this.releaseRecordingCamera();
    if (this.discardRecording || this.destroyed) {
      this.recordingChunks = [];
      this.recordingState.set('idle');
      return;
    }
    this.recordingState.set('saving');
    const folder = this.recordingFolder;
    if (!mimeType.startsWith('video/mp4') && !mimeType.startsWith('video/webm')) {
      this.recordingChunks = [];
      this.message.set('This browser recorded an unsupported video format. Use Device camera or Upload videos.');
      this.recordingState.set('idle');
      return;
    }
    const type = mimeType.startsWith('video/mp4') ? 'video/mp4' : 'video/webm';
    const file = new File(this.recordingChunks, `recording-${Date.now()}.${type === 'video/mp4' ? 'mp4' : 'webm'}`, { type });
    this.recordingChunks = [];
    if (!file.size || file.size > 200_000_000) {
      this.message.set('Recording is empty or over 200 MB. Please record a shorter video.');
      this.recordingState.set('idle');
      return;
    }
    try {
      const result = await this.api.uploadCapturedVideo(folder, file);
      await this.selectFolder(folder);
      await this.loadFolders();
      this.message.set(`${result.videoCount} video saved in “${folder}” (${this.recordingResolution()}).`);
    } catch (error) { this.showError(error, 'Could not save the recorded video.'); }
    finally { this.recordingState.set('idle'); }
  }

  private releaseRecordingCamera(): void {
    if (this.recordingPreview) this.recordingPreview.nativeElement.srcObject = null;
    this.recordingStream?.getTracks().forEach(track => track.stop());
    this.recordingStream = undefined;
    this.recorder = undefined;
  }

  async loadFolders(): Promise<void> {
    try {
      const folders = await this.api.folders();
      this.folders.set(folders);
      if (!folders.some(folder => folder.name === this.selectedFolder())) {
        this.selectedFolder.set(folders[0]?.name ?? '');
      }
      if (this.selectedFolder()) await this.selectFolder(this.selectedFolder());
      else { this.photos.set([]); this.videos.set([]); }
    } catch (error) { this.showError(error, 'Could not load media folders.'); }
  }

  async createFolder(): Promise<void> {
    const name = this.newFolderName().trim();
    if (!name || this.busy()) return;
    this.busy.set(true);
    try {
      const folder = await this.api.createFolder(name);
      this.newFolderName.set('');
      this.selectedFolder.set(folder.name);
      await this.loadFolders();
      this.message.set(`Folder “${folder.name}” created on the local server.`);
    } catch (error) { this.showError(error, 'Could not create folder.'); }
    finally { this.busy.set(false); }
  }

  async selectFolder(name: string): Promise<void> {
    this.selectedFolder.set(name);
    if (!name) { this.photos.set([]); this.videos.set([]); return; }
    try {
      const files = await this.api.files(name);
      if (this.selectedFolder() !== name) return;
      this.photos.set(files.filter(file => !file.isVideo));
      this.videos.set(files.filter(file => file.isVideo));
    } catch (error) { this.showError(error, 'Could not load this folder.'); }
  }

  async openFolder(): Promise<void> {
    const folder = this.selectedFolder();
    if (!folder || !this.currentUser()?.isAdmin) return;
    try {
      await this.api.openFolder(folder);
      this.message.set(`Opened “${folder}” on the API computer.`);
    } catch (error) { this.showError(error, 'Could not open this folder on the API computer.'); }
  }

  async upload(event: Event, kind: 'photo' | 'video', camera = false): Promise<void> {
    const { input, files: selected } = this.uploads.filesFrom(event);
    const folder = this.selectedFolder();
    if (!folder || !selected.length) {
      if (folder) this.message.set('The camera or file picker did not return a file. Please try again.');
      input.value = '';
      return;
    }
    this.busy.set(true);
    try {
      const files = kind === 'photo' && camera ? await this.uploads.prepareCameraImages(selected) : selected;
      let imageCount = 0;
      let videoCount = 0;
      for (const file of files) {
        try {
          const result = kind === 'video' && camera
            ? await this.api.uploadCapturedVideo(folder, file)
            : await this.api.uploadFile(folder, file);
          imageCount += result.imageCount;
          videoCount += result.videoCount;
        } catch (error) {
          await this.selectFolder(folder);
          await this.loadFolders();
          this.showError(error, `Upload failed for “${file.name}”.`, `${imageCount + videoCount} saved; “${file.name}”: `);
          return;
        }
      }
      await this.selectFolder(folder);
      await this.loadFolders();
      this.message.set(`${imageCount} photo(s) and ${videoCount} video(s) saved in “${folder}”.`);
    } catch (error) { this.showError(error, 'Upload failed. Check the file type and size.'); }
    finally { this.busy.set(false); input.value = ''; }
  }

  fileUrl(file: MediaLibraryFile): string { return this.api.fileUrl(this.selectedFolder(), file.fileName); }
  sizeLabel(size: number): string { return size < 1024 * 1024 ? `${Math.round(size / 1024)} KB` : `${(size / 1024 / 1024).toFixed(1)} MB`; }

  private showError(error: unknown, fallback: string, prefix = ''): void {
    const response = error as { error?: string | { title?: string; detail?: string }; message?: string; status?: number };
    const detail = response?.error;
    const reason = typeof detail === 'string' ? detail : detail?.detail || detail?.title;
    const statusHint = response?.status === 413 ? 'File exceeds the server request limit.' : undefined;
    this.message.set(prefix + (reason || statusHint || (error instanceof Error ? error.message : '') || fallback));
  }
}
