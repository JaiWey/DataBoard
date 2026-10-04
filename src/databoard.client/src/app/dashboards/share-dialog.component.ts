import { DatePipe } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { Observable } from 'rxjs';
import { problemMessages } from '../organizations/data-sources.service';
import { DashboardShare, DashboardsService, OrgShare, ShareLifetime } from './dashboards.service';

export interface ShareDialogData {
  orgId: string;
  dashboardId: string;
  dashboardName: string;
}

interface MyOrganization {
  id: string;
  name: string;
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
      @if (restricted()) {
        <p class="warning">
          Only people who are signed in and belong to one of the organizations below can open this link. They see the
          dashboard's data, cannot change anything, and never see the SQL.
        </p>
      } @else {
        <p class="warning">
          Anyone with a link can see this dashboard's data without signing in. They cannot change anything, and they never
          see the SQL.
        </p>
      }

      <fieldset class="who">
        <legend>Who can open the link</legend>
        <label><input type="radio" name="who" aria-label="Anyone with the link" [checked]="!restricted()" (change)="restricted.set(false)" /> Anyone with the link</label>
        <label><input type="radio" name="who" aria-label="Only members of chosen organizations" [checked]="restricted()" (change)="restricted.set(true)" /> Only signed-in members of chosen organizations</label>
        @if (restricted()) {
          <div class="orgs">
            @for (org of organizations(); track org.id) {
              <label><input type="checkbox" [attr.aria-label]="'Members of ' + org.name" [checked]="chosen().has(org.id)" (change)="toggle(org.id, $any($event.target).checked)" /> {{ org.name }}</label>
            }
            @if (organizations().length === 0) { <small>Loading your organizations…</small> }
          </div>
        }
      </fieldset>

      <div class="make">
        <label>
          <span>Link works for</span>
          <select aria-label="Link lifetime" (change)="lifetime.set(+$any($event.target).value)">
            @for (option of lifetimes; track option.label; let i = $index) {
              <option [value]="i" [selected]="i === lifetime()">{{ option.label }}</option>
            }
          </select>
        </label>
        <button mat-flat-button type="button" class="create" [disabled]="busy() || (restricted() && chosen().size === 0)" (click)="create()">Create link</button>
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

      <h3>Other organizations</h3>
      <p class="hint">
        Show this dashboard, read-only and live, to every member of an organization you belong to. They find it under
        "Shared with us" on their Dashboards page and never see its queries.
      </p>
      <div class="orgs-shared">
        @for (org of otherOrganizations(); track org.id) {
          <label>
            <input type="checkbox" class="org-share" [attr.aria-label]="'Show to ' + org.name" [checked]="isSharedWith(org.id)"
                   [disabled]="busy()" (change)="setSharedWith(org, $any($event.target))" />
            {{ org.name }}
          </label>
        }
        @for (share of othersShares(); track share.organizationId) {
          <span class="unknown">
            Another organization
            <button mat-button type="button" class="stop-sharing" [disabled]="busy()" (click)="stopSharing(share.organizationId)">Stop</button>
          </span>
        }
        @if (otherOrganizations().length === 0 && othersShares().length === 0) {
          <small class="none"><em>You are not in any other organization.</em></small>
        }
      </div>

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
              · <span class="who-can">{{ whoFor(share) }}</span>
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
    .who { border: 1px solid var(--mat-sys-outline-variant); border-radius: 8px; margin: 0 0 1rem; padding: 0.5rem 0.75rem; display: grid; gap: 0.3rem; }
    .who legend { padding: 0 0.3rem; color: var(--mat-sys-on-surface-variant); font-size: 0.85rem; }
    .orgs { display: grid; gap: 0.2rem; margin: 0.2rem 0 0.2rem 1.5rem; }
    .hint { margin: 0 0 0.5rem; color: var(--mat-sys-on-surface-variant); font-size: 0.9rem; }
    .orgs-shared { display: grid; gap: 0.2rem; }
    .unknown { display: flex; align-items: center; gap: 0.5rem; color: var(--mat-sys-on-surface-variant); }
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
  private readonly http = inject(HttpClient);

  protected readonly lifetimes = SHARE_LIFETIMES;
  protected readonly lifetime = signal(DEFAULT_LIFETIME);
  protected readonly shares = signal<DashboardShare[]>([]);
  /** The organizations the dashboard is shown to. */
  protected readonly orgShares = signal<OrgShare[]>([]);
  /** Limited to members of chosen organizations, instead of anyone with the link. */
  protected readonly restricted = signal(false);
  /** The organizations the person belongs to: a link can only be limited to those. */
  protected readonly organizations = signal<MyOrganization[]>([]);
  protected readonly chosen = signal<ReadonlySet<string>>(new Set([this.data.orgId]));
  /** The organizations a dashboard could be shown to: the ones the person is in, apart from the dashboard's own. */
  protected readonly otherOrganizations = computed(() => this.organizations().filter((o) => o.id !== this.data.orgId));
  /** Shares with organizations the person is not in (someone else made them): they can be stopped, not named. */
  protected readonly othersShares = computed(() => this.orgShares().filter((s) => !this.organizations().some((o) => o.id === s.organizationId)));
  private readonly names = computed(() => new Map(this.organizations().map((o) => [o.id, o.name])));
  protected readonly created = signal<string | null>(null);
  protected readonly copied = signal(false);
  protected readonly busy = signal(false);
  protected readonly errors = signal<string[]>([]);
  private createdId: string | null = null;

  ngOnInit(): void {
    this.load();
    this.loadOrgShares();
    this.http.get<MyOrganization[]>('/api/organizations').subscribe({
      next: (items) => this.organizations.set(items),
      error: () => this.organizations.set([]),
    });
  }

  private loadOrgShares(): void {
    this.api.listOrgShares(this.data.orgId, this.data.dashboardId).subscribe({
      next: (items) => this.orgShares.set(items),
      error: (e) => this.errors.set(problemMessages(e, 'Could not load who it is shared with.')),
    });
  }

  protected isSharedWith(organizationId: string): boolean {
    return this.orgShares().some((s) => s.organizationId === organizationId);
  }

  /** Ticking shows the dashboard to that organization at once; unticking takes it away at once. */
  protected setSharedWith(org: MyOrganization, box: HTMLInputElement): void {
    const on = box.checked;
    this.busy.set(true);
    this.errors.set([]);
    const call: Observable<unknown> = on
      ? this.api.shareWithOrg(this.data.orgId, this.data.dashboardId, org.id)
      : this.api.unshareWithOrg(this.data.orgId, this.data.dashboardId, org.id);
    call.subscribe({
      next: () => {
        this.busy.set(false);
        this.loadOrgShares();
      },
      error: (e) => {
        this.busy.set(false);
        box.checked = !on; // nothing changed, so put the box back by hand
        this.errors.set(problemMessages(e, on ? 'Could not share the dashboard.' : 'Could not stop sharing.'));
      },
    });
  }

  protected stopSharing(organizationId: string): void {
    this.busy.set(true);
    this.errors.set([]);
    this.api.unshareWithOrg(this.data.orgId, this.data.dashboardId, organizationId).subscribe({
      next: () => {
        this.busy.set(false);
        this.loadOrgShares();
      },
      error: (e) => {
        this.busy.set(false);
        this.errors.set(problemMessages(e, 'Could not stop sharing.'));
      },
    });
  }

  protected toggle(id: string, on: boolean): void {
    const next = new Set(this.chosen());
    if (on) next.add(id);
    else next.delete(id);
    this.chosen.set(next);
  }

  /** "Anyone with the link", or the organizations by name (an organization you are not in is not named). */
  protected whoFor(share: DashboardShare): string {
    const ids = share.organizationIds ?? [];
    if (ids.length === 0) return 'anyone with the link';
    return 'signed-in members of ' + ids.map((id) => this.names().get(id) ?? 'another organization').join(', ');
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
    const organizationIds = this.restricted() ? [...this.chosen()] : [];
    this.api.createShare(this.data.orgId, this.data.dashboardId, SHARE_LIFETIMES[this.lifetime()].value, organizationIds).subscribe({
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
