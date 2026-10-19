import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, input, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { Router } from '@angular/router';
import { AuthProvidersService } from './auth-providers.service';
import { AuthService } from './auth.service';

/**
 * "Continue as guest", shown only when the server has guests turned on. It signs in a temporary account with a sandbox of
 * its own, so the whole app can be tried without registering.
 */
@Component({
  selector: 'app-guest-sign-in',
  imports: [MatButtonModule],
  template: `
    @if (enabled()) {
      <div class="or" aria-hidden="true"><span>or</span></div>
      <button mat-stroked-button type="button" class="guest" [disabled]="busy()" (click)="start()">Continue as guest</button>
      <p class="hint">Try DataBoard in a sandbox of your own. It is deleted after a day.</p>
      @if (error(); as e) {
        <p class="fail" role="alert">{{ e }}</p>
      }
    }
  `,
  styles: `
    :host { display: block; }
    .or { display: flex; align-items: center; gap: 0.75rem; margin: 0.75rem 0; color: var(--mat-sys-on-surface-variant); font-size: 0.85rem; }
    .or::before, .or::after { content: ''; flex: 1; border-top: 1px solid var(--mat-sys-outline-variant); }
    .guest { width: 100%; }
    .hint { margin: 0.5rem 0 0; font-size: 0.85rem; color: var(--mat-sys-on-surface-variant); }
  `,
})
export class GuestSignInComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  /** A page of this app to go to once in. */
  readonly returnUrl = input<string | null>(null);

  protected readonly enabled = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  constructor() {
    inject(AuthProvidersService).load().subscribe((p) => this.enabled.set(p.guest === true));
  }

  protected start(): void {
    this.busy.set(true);
    this.error.set(null);
    this.auth.guest().subscribe({
      next: () => this.router.navigateByUrl(this.returnUrl() ?? '/'),
      error: (e: HttpErrorResponse) => {
        this.busy.set(false);
        this.error.set(e.status === 429 ? 'Too many guests from your address. Try again later.' : 'Could not start a guest session. Try again.');
      },
    });
  }
}
