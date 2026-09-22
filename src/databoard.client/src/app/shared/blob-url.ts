import { signal } from '@angular/core';
import { Observable, Subscription } from 'rxjs';

/**
 * Shows a file fetched by a normal request (so it carries the sign-in or the link's token, which an image tag cannot
 * send) as an address the page can use. Every picture it makes is let go of when the next one comes or when it is
 * disposed of. If the file cannot be fetched there is no address.
 */
export class BlobUrl {
  /** The address of the file fetched last, or null while there is none (not yet fetched, or it failed). */
  readonly url = signal<string | null>(null);
  /** True when the last fetch gave an error, until the next one starts. */
  readonly failed = signal(false);
  private request: Subscription | null = null;

  /** Fetches a file; a fetch still on its way for an earlier one is dropped. */
  fetch(source: Observable<Blob>): void {
    this.request?.unsubscribe();
    this.failed.set(false);
    this.request = source.subscribe({
      next: (blob) => this.show(URL.createObjectURL(blob)),
      error: () => {
        this.show(null);
        this.failed.set(true);
      },
    });
  }

  dispose(): void {
    this.request?.unsubscribe();
    this.request = null;
    this.show(null);
  }

  private show(url: string | null): void {
    const old = this.url();
    this.url.set(url);
    if (old) URL.revokeObjectURL(old);
  }
}
