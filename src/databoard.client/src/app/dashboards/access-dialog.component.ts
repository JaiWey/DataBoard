import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { problemMessages } from '../organizations/data-sources.service';
import { Member, MemberRole, MembersService } from '../organizations/members.service';
import { AccessRule, DashboardsService } from './dashboards.service';

export interface AccessDialogData {
  orgId: string;
  /** What is being limited: one dashboard, or a dashboard folder (and so everything in it). */
  kind: 'dashboard' | 'folder';
  id: string;
  name: string;
}

/** The roles a rule can ask for. Viewer would be everybody, so it is only listed if a rule already has it. */
const ROLE_CHOICES: { value: MemberRole; label: string }[] = [
  { value: 'Editor', label: 'Editors and Owners' },
  { value: 'Owner', label: 'Owners only' },
];

/**
 * Who may see a dashboard or a dashboard folder: everybody in the organization, or only people with at least a role
 * and/or particular people. Owners always see everything, so they are shown ticked and cannot be taken off. Closes with
 * `true` when something was saved.
 */
@Component({
  selector: 'app-access-dialog',
  imports: [MatDialogModule, MatButtonModule],
  template: `
    <h2 mat-dialog-title>Who can see "{{ data.name }}"</h2>
    <mat-dialog-content>
      @if (data.kind === 'folder') {
        <p class="hint">A limit on a folder covers everything in it, including the folders inside.</p>
      }
      @for (up of inherited(); track up.folderId) {
        <p class="inherited" role="note">
          Also limited by the folder "{{ up.folderName }}"
          ({{ describe(up.minRole, up.people) }}): somebody has to be let in by that one too.
        </p>
      }

      <fieldset class="scope">
        <legend>Visible to</legend>
        <label><input type="radio" name="scope" aria-label="Everyone in the organization" [checked]="!limited()" (change)="limited.set(false)" /> Everyone in the organization</label>
        <label><input type="radio" name="scope" aria-label="Only some people" [checked]="limited()" (change)="limited.set(true)" /> Only some people</label>
      </fieldset>

      @if (limited()) {
        <label class="role">
          <span>People with the role</span>
          <select aria-label="Minimum role" (change)="setRole($any($event.target).value)">
            <option value="" [selected]="minRole() === null">Nobody by role</option>
            @for (r of roleChoices(); track r.value) {
              <option [value]="r.value" [selected]="minRole() === r.value">{{ r.label }}</option>
            }
          </select>
        </label>

        <p class="people-title">and these people:</p>
        <ul class="people" aria-label="People">
          @for (m of members(); track m.userId) {
            <li>
              <label>
                <input type="checkbox" [attr.aria-label]="'Allow ' + m.displayName" [checked]="isIn(m)" [disabled]="m.role === 'Owner' || busy()"
                       (change)="toggle(m, $any($event.target).checked)" />
                {{ m.displayName }}
                <small>{{ m.email }} · {{ m.role }}@if (m.role === 'Owner') { · always can } @else if (byRole(m)) { · by role }</small>
              </label>
            </li>
          }
        </ul>
        @if (!valid()) {
          <p class="hint">Choose a role or at least one person. Owners can always see it.</p>
        }
      }

      @for (message of errors(); track message) {
        <p class="fail" role="alert">{{ message }}</p>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button type="button" mat-dialog-close>Cancel</button>
      <button mat-flat-button type="button" class="save" [disabled]="busy() || !loaded() || !valid()" (click)="save()">Save</button>
    </mat-dialog-actions>
  `,
  styles: `
    .hint, .inherited { margin: 0 0 0.75rem; color: var(--mat-sys-on-surface-variant); font-size: 0.9rem; }
    .inherited { padding: 0.5rem 0.75rem; border-radius: 8px; background: var(--mat-sys-secondary-container); color: var(--mat-sys-on-secondary-container); }
    .scope { border: 0; margin: 0 0 0.75rem; padding: 0; display: grid; gap: 0.3rem; }
    .scope legend { padding: 0; margin-bottom: 0.3rem; font-weight: 500; }
    .role { display: flex; align-items: center; gap: 0.6rem; margin-bottom: 0.5rem; }
    .role select { padding: 0.25rem; }
    .people-title { margin: 0.5rem 0 0.25rem; }
    .people { list-style: none; margin: 0; padding: 0; max-height: 15rem; overflow: auto; border: 1px solid var(--mat-sys-outline-variant); border-radius: 8px; }
    .people li { padding: 0.25rem 0.6rem; border-bottom: 1px solid var(--mat-sys-outline-variant); }
    .people li:last-child { border-bottom: 0; }
    .people small { color: var(--mat-sys-on-surface-variant); margin-left: 0.3rem; }
    .fail { color: var(--mat-sys-error); }
  `,
})
export class AccessDialogComponent implements OnInit {
  protected readonly data = inject<AccessDialogData>(MAT_DIALOG_DATA);
  private readonly ref = inject<MatDialogRef<AccessDialogComponent, boolean>>(MatDialogRef);
  private readonly api = inject(DashboardsService);
  private readonly membersApi = inject(MembersService);

  protected readonly loaded = signal(false);
  protected readonly busy = signal(false);
  protected readonly errors = signal<string[]>([]);
  protected readonly members = signal<Member[]>([]);
  protected readonly inherited = signal<AccessRule['inherited']>([]);

  protected readonly limited = signal(false);
  protected readonly minRole = signal<MemberRole | null>(null);
  protected readonly chosen = signal<ReadonlySet<string>>(new Set());

  protected readonly roleChoices = computed(() =>
    this.minRole() === 'Viewer' ? [{ value: 'Viewer' as MemberRole, label: 'Everyone (any role)' }, ...ROLE_CHOICES] : ROLE_CHOICES);

  /** A limited rule has to let somebody in by role or by name; otherwise it would be no limit at all. */
  protected readonly valid = computed(() => !this.limited() || this.minRole() !== null || this.chosen().size > 0);

  ngOnInit(): void {
    this.membersApi.list(this.data.orgId).subscribe({
      next: (items) => this.members.set(items),
      error: (e) => this.errors.set(problemMessages(e, 'Could not load the people.')),
    });
    const load = this.data.kind === 'dashboard'
      ? this.api.getAccess(this.data.orgId, this.data.id)
      : this.api.getFolderAccess(this.data.orgId, this.data.id);
    load.subscribe({
      next: (rule) => {
        this.limited.set(rule.minRole !== null || rule.memberIds.length > 0);
        this.minRole.set(rule.minRole);
        this.chosen.set(new Set(rule.memberIds));
        this.inherited.set(rule.inherited);
        this.loaded.set(true);
      },
      error: (e) => this.errors.set(problemMessages(e, 'Could not load who can see it.')),
    });
  }

  protected setRole(value: string): void {
    this.minRole.set(value === '' ? null : (value as MemberRole));
  }

  /** Does the chosen role already let this person in? */
  protected byRole(m: Member): boolean {
    const order: Record<MemberRole, number> = { Viewer: 1, Editor: 2, Owner: 3 };
    const minimum = this.minRole();
    return minimum !== null && order[m.role] >= order[minimum];
  }

  protected isIn(m: Member): boolean {
    return m.role === 'Owner' || this.chosen().has(m.userId);
  }

  protected toggle(m: Member, on: boolean): void {
    const next = new Set(this.chosen());
    if (on) next.add(m.userId);
    else next.delete(m.userId);
    this.chosen.set(next);
  }

  /** "Editors and Owners and 2 people", for the note about a folder above. */
  protected describe(role: MemberRole | null, people: number): string {
    const parts: string[] = [];
    if (role) parts.push(ROLE_CHOICES.find((r) => r.value === role)?.label ?? 'Everyone');
    if (people > 0) parts.push(`${people} ${people === 1 ? 'person' : 'people'}`);
    return parts.join(' and ') || 'Owners only';
  }

  protected save(): void {
    if (!this.valid()) return;
    this.busy.set(true);
    this.errors.set([]);
    // Owners always get in, so they are not stored as listed people.
    const owners = new Set(this.members().filter((m) => m.role === 'Owner').map((m) => m.userId));
    const rule = {
      minRole: this.limited() ? this.minRole() : null,
      memberIds: this.limited() ? [...this.chosen()].filter((id) => !owners.has(id)) : [],
    };
    const call = this.data.kind === 'dashboard'
      ? this.api.setAccess(this.data.orgId, this.data.id, rule)
      : this.api.setFolderAccess(this.data.orgId, this.data.id, rule);
    call.subscribe({
      next: () => this.ref.close(true),
      error: (e) => {
        this.busy.set(false);
        this.errors.set(problemMessages(e, 'Could not save who can see it.'));
      },
    });
  }
}
