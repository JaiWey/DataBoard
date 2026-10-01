import { HttpClient } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';

/**
 * "Continue with GitHub", shown only when the server has GitHub set up. It is a plain link: the whole page goes to GitHub
 * and comes back signed in, so there is nothing for scripts to hold on the way.
 */
@Component({
  selector: 'app-github-sign-in',
  imports: [MatButtonModule],
  template: `
    @if (enabled()) {
      <div class="or" aria-hidden="true"><span>or</span></div>
      <a mat-stroked-button class="github" href="/api/auth/github">
        <svg viewBox="0 0 16 16" width="18" height="18" fill="currentColor" aria-hidden="true">
          <path d="M8 0C3.58 0 0 3.58 0 8c0 3.54 2.29 6.53 5.47 7.59.4.07.55-.17.55-.38 0-.19-.01-.82-.01-1.49-2.01.37-2.53-.49-2.69-.94-.09-.23-.48-.94-.82-1.13-.28-.15-.68-.52-.01-.53.63-.01 1.08.58 1.23.82.72 1.21 1.87.87 2.33.66.07-.52.28-.87.51-1.07-1.78-.2-3.64-.89-3.64-3.95 0-.87.31-1.59.82-2.15-.08-.2-.36-1.02.08-2.12 0 0 .67-.21 2.2.82a7.6 7.6 0 0 1 4 0c1.53-1.04 2.2-.82 2.2-.82.44 1.1.16 1.92.08 2.12.51.56.82 1.27.82 2.15 0 3.07-1.87 3.75-3.65 3.95.29.25.54.73.54 1.48 0 1.07-.01 1.93-.01 2.2 0 .21.15.46.55.38A8.01 8.01 0 0 0 16 8c0-4.42-3.58-8-8-8z" />
        </svg>
        Continue with GitHub
      </a>
    }
  `,
  styles: `
    :host { display: block; }
    .or { display: flex; align-items: center; gap: 0.75rem; margin: 0.75rem 0; color: var(--mat-sys-on-surface-variant); font-size: 0.85rem; }
    .or::before, .or::after { content: ''; flex: 1; border-top: 1px solid var(--mat-sys-outline-variant); }
    .github { width: 100%; }
    .github svg { margin-right: 0.5rem; }
  `,
})
export class GitHubSignInComponent {
  private readonly http = inject(HttpClient);

  protected readonly enabled = signal(false);

  constructor() {
    // Not knowing is the same as not offering: a failed call just leaves the password form.
    this.http.get<{ gitHub: boolean }>('/api/auth/providers').subscribe({
      next: (p) => this.enabled.set(p.gitHub === true),
      error: () => this.enabled.set(false),
    });
  }
}
