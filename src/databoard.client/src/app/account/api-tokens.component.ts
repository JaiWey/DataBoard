import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { problemMessages } from '../organizations/data-sources.service';
import { ApiToken, ApiTokenCreated, ApiTokensService, TokenLifetime, TokenScope } from './api-tokens.service';
import { AppSelectComponent, SelectOption } from '../shared/app-select.component';
import { ConfirmService } from '../shared/confirm.service';

/** How long a new token can last. The default is the one in the middle: three months, then it stops by itself. */
export const TOKEN_LIFETIMES: { label: string; value: TokenLifetime }[] = [
  { label: '30 days', value: { expiresInDays: 30 } },
  { label: '90 days', value: { expiresInDays: 90 } },
  { label: '1 year', value: { expiresInDays: 365 } },
  { label: 'Never (until I revoke it)', value: { neverExpires: true } },
];

export const TOKEN_SCOPES: { value: TokenScope; label: string }[] = [
  { value: 'Write', label: 'Read and create' },
  { value: 'Read', label: 'Read only' },
];

/** The address an assistant connects to. */
export const mcpUrl = (): string => `${window.location.origin}/mcp`;

/**
 * Personal access tokens, for connecting an AI assistant to DataBoard over MCP. A token acts as its owner and never
 * has more rights than they do; it is shown once, when it is made.
 */
@Component({
  selector: 'app-api-tokens',
  imports: [ReactiveFormsModule, DatePipe, MatCardModule, MatFormFieldModule, MatInputModule, MatButtonModule, AppSelectComponent],
  template: `
    <h2>Access tokens</h2>
    <p class="intro">
      An access token lets an AI assistant such as Claude work in DataBoard for you: it can look at your datasets and, with
      the right scope, build charts and dashboards from a prompt. It acts as you and never has more rights than you do.
      It can also create and change datasets (read-only SQL). It cannot delete anything or change data sources.
    </p>

    <mat-card appearance="outlined" class="make">
      <mat-card-content>
        <h3>New token</h3>
        <form [formGroup]="form" (ngSubmit)="create()" class="form-row">
          <mat-form-field>
            <mat-label>Name</mat-label>
            <input matInput formControlName="name" aria-label="Token name" placeholder="Claude Desktop" />
          </mat-form-field>
          <app-select label="Can" ariaLabel="Token scope" formControlName="scope" [options]="scopes" />
          <app-select label="Works for" ariaLabel="Token lifetime" [options]="lifetimeOptions" [value]="'' + lifetime()" (valueChange)="lifetime.set(+$event)" />
          <button mat-flat-button type="submit" class="create" [disabled]="form.invalid || busy()">Create token</button>
        </form>
      </mat-card-content>
    </mat-card>

    @if (created(); as c) {
      <mat-card appearance="outlined" class="created" role="status">
        <mat-card-content>
          <strong>Copy the token now. It cannot be shown again.</strong>
          <div class="copy">
            <input readonly aria-label="Access token" [value]="c.token" (focus)="$any($event.target).select()" />
            <button mat-stroked-button type="button" class="copy-token" (click)="copy(c.token)">{{ copied() ? 'Copied' : 'Copy' }}</button>
          </div>

          <h4>Connect Claude Code</h4>
          <pre class="snippet" aria-label="Claude Code command">{{ claudeCode(c.token) }}</pre>
          <h4>Any other MCP client</h4>
          <p class="hint">Add a server of type <code>http</code> with this address, and send the token as a bearer token:</p>
          <pre class="snippet" aria-label="MCP client settings">{{ clientSettings(c.token) }}</pre>
        </mat-card-content>
      </mat-card>
    }

    @for (message of errors(); track message) {
      <p class="fail" role="alert">{{ message }}</p>
    }

    <h3>Your tokens</h3>
    @if (tokens().length === 0) {
      <p><em>You have no tokens yet.</em></p>
    }
    <ul class="tokens">
      @for (token of tokens(); track token.id) {
        <li>
          <span class="info">
            <strong>{{ token.name }}</strong>
            <span class="chip" [class.measure]="token.scope === 'Write'">{{ token.scope === 'Write' ? 'Read and create' : 'Read only' }}</span>
            <small>
              Made {{ token.createdUtc | date: 'medium' }} ·
              @if (token.expired) { <strong class="fail">expired {{ token.expiresUtc | date: 'medium' }}</strong> }
              @else if (token.expiresUtc) { works until {{ token.expiresUtc | date: 'medium' }} }
              @else { never expires }
              ·
              @if (token.lastUsedUtc) { last used {{ token.lastUsedUtc | date: 'medium' }} } @else { never used }
            </small>
          </span>
          <button mat-button type="button" class="revoke" [disabled]="busy()" (click)="revoke(token)">Revoke</button>
        </li>
      }
    </ul>
  `,
  styles: `
    .intro { max-width: 52rem; color: var(--mat-sys-on-surface-variant); }
    .make, .created { margin-bottom: 1rem; --mat-card-outlined-container-color: #fff; }
    .created { --mat-card-outlined-container-color: var(--mat-sys-secondary-container); }
    h3 { margin-top: 0; }
    .create { margin-top: 0.4rem; }
    .make app-select { min-width: 12rem; }
    .copy { display: flex; gap: 0.5rem; margin: 0.5rem 0 1rem; }
    .copy input { flex: 1; min-width: 0; padding: 0.4rem; font-family: monospace; }
    h4 { margin: 1rem 0 0.25rem; }
    .hint { margin: 0 0 0.4rem; }
    .snippet { margin: 0; padding: 0.6rem; overflow-x: auto; background: var(--mat-sys-surface-container); border-radius: 8px; font-size: 0.85rem; white-space: pre-wrap; word-break: break-all; }
    .tokens { list-style: none; margin: 0; padding: 0; max-width: 60rem; }
    .tokens li { display: flex; align-items: center; justify-content: space-between; gap: 0.75rem; padding: 0.5rem 0; border-bottom: 1px solid var(--mat-sys-outline-variant); }
    .info { display: flex; flex-wrap: wrap; align-items: center; gap: 0.2rem 0.75rem; }
  `,
})
export class ApiTokensComponent implements OnInit {
  private readonly api = inject(ApiTokensService);
  private readonly confirms = inject(ConfirmService);
  private readonly fb = inject(FormBuilder);

  protected readonly scopes = TOKEN_SCOPES;
  protected readonly lifetimes = TOKEN_LIFETIMES;
  protected readonly lifetimeOptions: SelectOption[] = TOKEN_LIFETIMES.map((o, i) => ({ value: String(i), label: o.label }));
  protected readonly lifetime = signal(1); // 90 days
  protected readonly tokens = signal<ApiToken[]>([]);
  protected readonly created = signal<ApiTokenCreated | null>(null);
  protected readonly copied = signal(false);
  protected readonly busy = signal(false);
  protected readonly errors = signal<string[]>([]);

  protected readonly form = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(100)]],
    scope: ['Write' as TokenScope],
  });

  ngOnInit(): void {
    this.load();
  }

  private load(): void {
    this.api.list().subscribe({
      next: (items) => this.tokens.set(items),
      error: (e) => this.errors.set(problemMessages(e, 'Could not load your tokens.')),
    });
  }

  protected claudeCode(token: string): string {
    return `claude mcp add --transport http databoard ${mcpUrl()} --header "Authorization: Bearer ${token}"`;
  }

  protected clientSettings(token: string): string {
    return JSON.stringify({ mcpServers: { databoard: { type: 'http', url: mcpUrl(), headers: { Authorization: `Bearer ${token}` } } } }, null, 2);
  }

  protected create(): void {
    if (this.form.invalid) return;
    const { name, scope } = this.form.getRawValue();
    this.busy.set(true);
    this.errors.set([]);
    this.copied.set(false);
    this.api.create(name.trim(), scope, TOKEN_LIFETIMES[this.lifetime()].value).subscribe({
      next: (created) => {
        this.busy.set(false);
        this.created.set(created);
        this.form.controls.name.reset();
        this.load();
      },
      error: (e) => {
        this.busy.set(false);
        this.errors.set(problemMessages(e, 'Could not create the token.'));
      },
    });
  }

  protected copy(token: string): void {
    // Needs a secure page; the field is selected on focus, so copying by hand still works where this is refused.
    navigator.clipboard?.writeText(token).then(
      () => this.copied.set(true),
      () => this.copied.set(false),
    );
  }

  protected revoke(token: ApiToken): void {
    this.confirms
      .ask({
        title: 'Revoke token',
        message: `Revoke "${token.name}"? Anything using it will lose access straight away.`,
        confirmLabel: 'Revoke',
        danger: true,
      })
      .subscribe((yes) => {
        if (yes) this.revokeConfirmed(token);
      });
  }

  private revokeConfirmed(token: ApiToken): void {
    this.busy.set(true);
    this.errors.set([]);
    this.api.revoke(token.id).subscribe({
      next: () => {
        this.busy.set(false);
        if (this.created()?.id === token.id) this.created.set(null); // no point offering to copy a token that no longer works
        this.load();
      },
      error: (e) => {
        this.busy.set(false);
        this.errors.set(problemMessages(e, 'Could not revoke the token.'));
      },
    });
  }
}
