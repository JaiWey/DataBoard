import { Component, DestroyRef, computed, effect, inject, input, untracked } from '@angular/core';
import { Observable } from 'rxjs';
import { BlobUrl } from '../shared/blob-url';
import { BackgroundFit } from './dashboards.service';

/**
 * A picture behind the content of its parent (which has to be positioned): it fills the parent, sits underneath
 * everything and never takes a click. The picture is fetched through `load` (so it carries the sign-in or the link's
 * token) and shown from memory; a different `version` fetches it again. If it cannot be fetched, nothing is shown.
 */
@Component({
  selector: 'app-background-layer',
  template: `<div class="layer" [style.background-image]="image()" [style.background-size]="size()" [style.background-repeat]="repeat()"
                  [style.background-position]="'center'" [style.opacity]="opacity() / 100" aria-hidden="true"></div>`,
  styles: `
    :host { position: absolute; inset: 0; z-index: -1; pointer-events: none; overflow: hidden; border-radius: inherit; }
    .layer { position: absolute; inset: 0; }
    @media print { :host { display: none; } }
  `,
})
export class BackgroundLayerComponent {
  /** Gets the picture. Called again whenever `version` changes. */
  readonly load = input.required<() => Observable<Blob>>();
  readonly version = input.required<string>();
  readonly fit = input<BackgroundFit>('cover');
  readonly opacity = input(100);

  private readonly picture = new BlobUrl();

  protected readonly image = computed(() => (this.picture.url() ? `url("${this.picture.url()}")` : null));
  protected readonly size = computed(() => {
    switch (this.fit()) {
      case 'contain': return 'contain';
      case 'center':
      case 'tile': return 'auto';
      default: return 'cover';
    }
  });
  protected readonly repeat = computed(() => (this.fit() === 'tile' ? 'repeat' : 'no-repeat'));

  constructor() {
    effect(() => {
      this.version(); // only a new version fetches the picture again, not a new function to fetch it with
      // Showing the picture reads and writes a signal of its own, which must not make this effect run again.
      untracked(() => this.picture.fetch(this.load()()));
    });
    inject(DestroyRef).onDestroy(() => this.picture.dispose());
  }
}
