import { AfterViewInit, Directive, ElementRef, HostListener, Input, OnChanges, OnDestroy } from '@angular/core';

@Directive({ selector: 'img[lpmProgressiveSrc]', standalone: true })
export class ProgressiveImageDirective implements AfterViewInit, OnChanges, OnDestroy {
  private static readonly maxConcurrent = 4;
  private static active = 0;
  private static queue: ProgressiveImageDirective[] = [];

  @Input() lpmProgressiveSrc = '';
  @Input() lpmImagePriority = false;

  private observer?: IntersectionObserver;
  private ready = false;
  private destroyed = false;
  private state: 'idle' | 'queued' | 'loading' | 'done' = 'idle';
  private loadingUrl = '';

  constructor(private readonly element: ElementRef<HTMLImageElement>) {}

  ngAfterViewInit(): void {
    this.ready = true;
    this.schedule();
  }

  ngOnChanges(): void {
    if (!this.ready || this.state === 'loading') return;
    this.state = 'idle';
    this.schedule();
  }

  ngOnDestroy(): void {
    this.destroyed = true;
    this.observer?.disconnect();
    if (this.state === 'loading') {
      this.state = 'done';
      ProgressiveImageDirective.active = Math.max(0, ProgressiveImageDirective.active - 1);
      ProgressiveImageDirective.drain();
    }
  }

  @HostListener('load')
  @HostListener('error')
  imageFinished(): void {
    if (this.state !== 'loading') return;
    this.state = 'done';
    ProgressiveImageDirective.active = Math.max(0, ProgressiveImageDirective.active - 1);
    ProgressiveImageDirective.drain();
    if (this.loadingUrl !== this.lpmProgressiveSrc) {
      this.state = 'idle';
      this.schedule();
    }
  }

  private schedule(): void {
    if (this.destroyed || !this.lpmProgressiveSrc || this.state !== 'idle') return;
    if (this.lpmImagePriority || typeof IntersectionObserver === 'undefined') {
      this.enqueue();
      return;
    }
    this.observer?.disconnect();
    this.observer = new IntersectionObserver(entries => {
      if (!entries.some(entry => entry.isIntersecting)) return;
      this.observer?.disconnect();
      this.enqueue();
    }, { rootMargin: '320px 0px' });
    this.observer.observe(this.element.nativeElement);
  }

  private enqueue(): void {
    if (this.destroyed || this.state !== 'idle') return;
    this.state = 'queued';
    ProgressiveImageDirective.queue.push(this);
    ProgressiveImageDirective.drain();
  }

  private start(): void {
    if (this.destroyed || this.state !== 'queued') return;
    this.state = 'loading';
    this.loadingUrl = this.lpmProgressiveSrc;
    this.element.nativeElement.src = this.loadingUrl;
  }

  private static drain(): void {
    while (this.active < this.maxConcurrent && this.queue.length) {
      const next = this.queue.shift()!;
      if (next.destroyed || next.state !== 'queued') continue;
      this.active += 1;
      next.start();
    }
  }
}
