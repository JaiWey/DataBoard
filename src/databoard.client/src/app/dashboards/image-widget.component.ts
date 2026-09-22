import { Component, DestroyRef, effect, inject, input, untracked } from '@angular/core';
import { Observable } from 'rxjs';
import { BlobUrl } from '../shared/blob-url';
import { ImageFit } from './dashboards.service';

/**
 * A picture widget: the picture fills the widget the way its fit says, and, if it has a link, a click on it opens the
 * link in a new tab. The picture is fetched through `load` (so it carries the sign-in or the link's token); a different
 * `version` fetches it again. If it cannot be fetched, a quiet note says so instead of a broken image.
 */
@Component({
  selector: 'app-image-widget',
  template: `
    @if (picture.url(); as src) {
      @if (link(); as href) {
        <a class="link" [href]="href" target="_blank" rel="noopener noreferrer" [attr.aria-label]="alt() || 'Open the link of this picture'">
          <img [src]="src" [alt]="alt() ?? ''" [style.object-fit]="fit()" />
        </a>
      } @else {
        <img [src]="src" [alt]="alt() ?? ''" [style.object-fit]="fit()" />
      }
    } @else if (picture.failed()) {
      <p class="missing"><em>The picture could not be shown.</em></p>
    }
  `,
  styles: `
    :host { display: block; width: 100%; height: 100%; min-height: 0; }
    img { display: block; width: 100%; height: 100%; }
    .link { display: block; width: 100%; height: 100%; }
    .missing { margin: 0; padding: 0.75rem; color: var(--mat-sys-on-surface-variant); }
  `,
})
export class ImageWidgetComponent {
  /** Gets the picture. Called again whenever `version` changes. */
  readonly load = input.required<() => Observable<Blob>>();
  readonly version = input.required<string>();
  readonly fit = input<ImageFit>('contain');
  readonly alt = input<string | null | undefined>(null);
  /** Where a click goes. Only an http or https address is ever followed. */
  readonly linkUrl = input<string | null | undefined>(null);

  protected readonly picture = new BlobUrl();

  constructor() {
    effect(() => {
      this.version(); // only a new version fetches the picture again, not a new function to fetch it with
      untracked(() => {
        this.picture.fetch(this.load()());
      });
    });
    inject(DestroyRef).onDestroy(() => this.picture.dispose());
  }

  /** The link, but only if it is a web address: a picture must never be a way to run script from a saved dashboard. */
  protected link(): string | null {
    const value = this.linkUrl()?.trim();
    if (!value) return null;
    try {
      const url = new URL(value);
      return url.protocol === 'http:' || url.protocol === 'https:' ? url.href : null;
    } catch {
      return null;
    }
  }
}
