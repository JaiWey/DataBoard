import { HttpContext, HttpContextToken, HttpInterceptorFn } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { finalize } from 'rxjs';

/** A request that is not worth a loading bar (a background reload the person did not ask for, a status check). */
export const QUIET_REQUEST = new HttpContextToken<boolean>(() => false);

/** The context to give such a request: `http.get(url, { context: quietly() })`. */
export const quietly = (): HttpContext => new HttpContext().set(QUIET_REQUEST, true);

/** A request faster than this never shows the bar, so quick pages do not flicker. */
export const SHOW_AFTER_MS = 150;
/** Once shown, the bar stays at least this long, so it does not blink on and off. */
export const SHOW_AT_LEAST_MS = 300;

/**
 * Whether the app is waiting on the server, for the bar at the top of the page. Every request that is not quiet counts
 * (see `loadingInterceptor`). The bar appears only if something is still waiting after a short moment, and then stays a
 * little while, so what the person sees is calm: nothing for quick answers, one steady bar for slow ones.
 */
@Injectable({ providedIn: 'root' })
export class LoadingService {
  private readonly _pending = signal(0);
  private readonly _visible = signal(false);
  private showTimer: ReturnType<typeof setTimeout> | undefined;
  private hideTimer: ReturnType<typeof setTimeout> | undefined;
  private shownAt = 0;

  /** How many requests are waiting right now. */
  readonly pending = this._pending.asReadonly();
  /** Whether the bar is showing. */
  readonly visible = this._visible.asReadonly();

  start(): void {
    this._pending.update((n) => n + 1);

    // Something new is waiting, so a bar that was about to go away stays.
    if (this.hideTimer !== undefined) {
      clearTimeout(this.hideTimer);
      this.hideTimer = undefined;
    }
    if (!this._visible() && this.showTimer === undefined) {
      this.showTimer = setTimeout(() => {
        this.showTimer = undefined;
        if (this._pending() > 0) {
          this.shownAt = Date.now();
          this._visible.set(true);
        }
      }, SHOW_AFTER_MS);
    }
  }

  stop(): void {
    const left = Math.max(0, this._pending() - 1);
    this._pending.set(left);
    if (left > 0) return;

    // Nothing is waiting: a bar that never showed never will, and one that did goes after its minimum time.
    if (this.showTimer !== undefined) {
      clearTimeout(this.showTimer);
      this.showTimer = undefined;
    }
    if (this._visible() && this.hideTimer === undefined) {
      const remaining = Math.max(0, SHOW_AT_LEAST_MS - (Date.now() - this.shownAt));
      this.hideTimer = setTimeout(() => {
        this.hideTimer = undefined;
        if (this._pending() === 0) this._visible.set(false);
      }, remaining);
    }
  }
}

/** Counts every API request that is not quiet while it waits: done, failed or cancelled, it stops counting. */
export const loadingInterceptor: HttpInterceptorFn = (req, next) => {
  if (req.context.get(QUIET_REQUEST)) return next(req);

  const loading = inject(LoadingService);
  loading.start();
  return next(req).pipe(finalize(() => loading.stop()));
};
