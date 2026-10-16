import { Component, EventEmitter, Input, Output } from '@angular/core';

/** Reusable camera/library picker; the owning page keeps its upload destination. */
@Component({
  selector: 'app-media-picker',
  standalone: true,
  template: `
    <label class="media-picker" [class.outline]="variant === 'outline'" [class.dashed]="variant === 'dashed'" [class.attachment]="variant === 'attachment'" [class.disabled]="disabled">
      {{ label }}
      <input type="file" [attr.accept]="accept" [multiple]="multiple" [attr.capture]="capture ? 'environment' : null"
        [disabled]="disabled" (change)="selected.emit($event)" />
    </label>
  `,
  styles: [`
    :host { display: contents; }
    .media-picker { position: relative; display: inline-flex; align-items: center; justify-content: center; min-height: 34px; padding: 9px 13px; border: 1px solid #bdd0d6; border-radius: 6px; color: #15576c; background: #eff8fa; font: inherit; font-weight: 700; cursor: pointer; }
    .media-picker.outline { border-color: #b9ccd5; border-radius: 8px; color: #126b85; background: #fff; }
    .media-picker.dashed { min-height: 46px; padding: 0 14px; border: 1px dashed #8fb8c4; border-radius: 8px; color: #176b87; background: #f7fbfc; font-size: 12px; font-weight: 800; }
    .media-picker.attachment { min-height: 34px; padding: 6px 10px; border-color: #b7a4d1; color: #5e3d89; background: #fff; font-size: 11px; }
    .media-picker.disabled { opacity: .55; cursor: wait; }
    .media-picker:focus-within { outline: 2px solid #176b87; outline-offset: 2px; }
    input { position: absolute; width: 1px; height: 1px; padding: 0; border: 0; opacity: 0; overflow: hidden; clip-path: inset(50%); }
  `],
})
export class MediaPickerComponent {
  @Input({ required: true }) label = '';
  @Input({ required: true }) accept = '';
  @Input() multiple = false;
  @Input() capture = false;
  @Input() disabled = false;
  @Input() variant: 'default' | 'outline' | 'dashed' | 'attachment' = 'default';
  @Output() readonly selected = new EventEmitter<Event>();
}
