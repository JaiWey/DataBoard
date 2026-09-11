import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { problemMessages } from '../organizations/data-sources.service';
import { DashboardShare, DashboardsService, ShareLifetime } from './dashboards.service';

export interface ShareDialogData {
  orgId: string;
  dashboardId: string;
  dashboardName: string;
}

/** How long a new link can last. The default is the one in the middle: public for a month, then it stops by itself. */
export const SHARE_LIFETIMES: { label: string; value: ShareLifetime }[] = [
  { label: '7 days', value: { expiresInDays: 7 } },
  { label: '30 days', value: { expiresInDays: 30 } },
  { label: '90 days', value: { expiresInDays: 90 } },
  { label: '1 year', value: { expiresInDays: 365 } },
  { label: 'Never (until I revoke it)', value: { neverExpires: true } },
];
const DEFAULT_LIFETIME = 1;

/** The address a visitor opens for a token. */
export const shareUrl = (token: string): string => `${window.location.origin}/shared/${token}`;

/**
 * Makes and revokes public links to a dashboard. A link's secret is shown once, when it is made (the server keeps only
 * a hash), so this dialog is the only place to copy it from.
 */
@Component({
  selector: 'app-share-dialog',
  imports: [MatDialogModule, MatButtonModule, DatePipe],
  template: `
    <h2 mat-dialog-title>Share "{{ data.dashboardName }}"</h2>
    <mat-dialog-content>
      <p class="warning">
        Anyone with a link can see this dashboard's data without signing in. They cannot change anything, and they never
        see the SQL.
      </p>

      <div class="make">
        <label>
          <span>Link works for</span>
          <select aria-label="Link lifetime" (change)="lifetime.set(+$any($event.target).value)">
            @for (option of lifetimes; track option.label; let i = $index) {
              <option [value]="i" [selected]="i === lifetime()">{{ option.label }}</option>
            }
          </select>
        </label>
        <button mat-flat-button type="button" class="create" [disabled]="busy()" (click)="create()">Create link</button>
      </div>

      @if (created(); as link) {
        <div class="created" role="status">
          <strong>Copy the link now. It cannot be shown again.</strong>
          <div class="copy">
            <input readonly aria-label="Share link" [value]="link" (focus)="$any($event.target).select()" />
            <button mat-stroked-button type="button" class="copy-button" (click)="copy(link)">{{ copied() ? 'Copied' : 'Copy' }}</button>
          </div>
        </div>
      }

      @for (message of errors(); track message) {
        <p class="fail" role="alert">{{ message }}</p>
      }

      <h3>Links</h3>
      @if (shares().length === 0) {
        <p class="none"><em>No links yet.</em></p>
      }
      <ul class="shares">
        @for (share of shares(); track share.id) {
          <li>
            <span class="info">
              Made {{ share.createdUtc | date: 'medium' }} ·
              @if (share.expired) { <strong class="fail">expired {{ share.expiresUtc | date: 'medium' }}</strong> }
              @else if (share.expiresUtc) { works until {{ share.expiresUtc | date: 'medium' }} }
              @else { never expires }
            </span>
            <button mat-button type="button" class="revoke" [disabled]="busy()" (click)="revoke(share)">Revoke</button>
          </li>
        }
      </ul>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button type="button" mat-dialog-close>Close</button>
    </mat-dialog-actions>
  `,
  styles: `
    .warning { margin: 0 0 1rem; color: var(--mat-sys-on-surface-variant); }
    .make { display: flex; flex-wrap: wrap; align-items: flex-end; gap: 0.75rem; }
    .make label { display: flex; flex-direction: column; gap: 0.2rem; }
    .created { margin: 1rem 0; padding: 0.75rem; border-radius: 8px; background: var(--mat-sys-secondary-container); }
    .copy { display: flex; gap: 0.5rem; margin-top: 0.5rem; }
    .copy input { flex: 1; min-width: 0; padding: 0.4rem; font-family: monospace; }
    h3 { margin-top: 1.25rem; }
    .shares { list-style: none; margin: 0; padding: 0; }
    .shares li { display: flex; align-items: center; justify-content: space-between; gap: 0.5rem; padding: 0.2rem 0; border-bottom: 1px solid var(--mat-sys-outline-variant); }
  `,
})
export class ShareDialogComponent implements OnInit {
  protected readonly data = inject<ShareDialogData>(MAT_DIALOG_DATA);
  private readonly api = inject(DashboardsService);

  protected readonly lifetimes = SHARE_LIFETIMES;
  protected readonly lifetime = signal(DEFAULT_LIFETIME);
  protected readonly shares = signal<DashboardShare[]>([]);
  protected readonly created = signal<string | null>(null);
  protected readonly copied = signal(false);
  protected readonly busy = signal(false);
  protected readonly errors = signal<string[]>([]);
  private createdId: string | null = null;

  ngOnInit(): void {
    this.load();
  }

  private load(): void {
    this.api.listShares(this.data.orgId, this.data.dashboardId).subscribe({
      next: (items) => this.shares.set(items),
      error: (e) => this.errors.set(problemMessages(e, 'Could not load the links.')),
    });
  }

  protected create(): void {
    this.busy.set(true);
    this.errors.set([]);
    this.copied.set(false);
    this.api.createShare(this.data.orgId, this.data.dashboardId, SHARE_LIFETIMES[this.lifetime()].value).subscribe({
      next: (created) => {
        this.busy.set(false);
        this.createdId = created.id;
        this.created.set(shareUrl(created.token));
        this.load();
      },
      error: (e) => {
        this.busy.set(false);
        this.errors.set(problemMessages(e, 'Could not create the link.'));
      },
    });
  }

  protected copy(link: string): void {
    // Needs a secure page; the field is selected on focus, so copying by hand still works where this is refused.
    navigator.clipboard?.writeText(link).then(
      () => this.copied.set(true),
      () => this.copied.set(false),
    );
  }

  protected revoke(share: DashboardShare): void {
    if (!confirm('Revoke this link? Anyone who has it will lose access straight away.')) return;
    this.busy.set(true);
    this.errors.set([]);
    this.api.revokeShare(this.data.orgId, this.data.dashboardId, share.id).subscribe({
      next: () => {
        this.busy.set(false);
        if (share.id === this.createdId) this.created.set(null); // no point offering to copy a link that no longer works
        this.load();
      },
      error: (e) => {
        this.busy.set(false);
        this.errors.set(problemMessages(e, 'Could not revoke the link.'));
      },
    });
  }
}
