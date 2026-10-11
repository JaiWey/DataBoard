import { DatePipe } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthService } from '../auth/auth.service';
import { problemMessages } from './data-sources.service';
import { MEMBER_ROLES, Member, MemberRole, MembersService } from './members.service';
import { AppSelectComponent } from '../shared/app-select.component';
import { ConfirmService } from '../shared/confirm.service';

interface Organization {
  id: string;
  name: string;
  role: MemberRole;
  isDefault?: boolean;
}

/**
 * The people of an organization: who they are and what they may do. Everyone sees the list. Owners add people, change
 * their roles and remove them; anyone can leave. The last Owner cannot be demoted, removed or leave (the server refuses),
 * so those controls are not offered for them.
 */
@Component({
  selector: 'app-members',
  imports: [ReactiveFormsModule, RouterLink, DatePipe, MatButtonModule, MatCardModule, MatFormFieldModule, MatInputModule, AppSelectComponent],
  template: `
    <h2>{{ organization()?.name }} <small>members</small></h2>
    <p><a [routerLink]="['/organizations']">Back to organizations</a></p>
    @if (organization()?.isDefault) {
      <p class="hint">This is the default organization: everyone who signs up joins it, so nobody can leave or be removed.</p>
    }

    <mat-card appearance="raised">
      <mat-card-content>
        @if (loaded() && members().length === 0) {
          <p><em>Nobody to show.</em></p>
        }
        @if (members().length > 0) {
          <table class="members">
            <thead>
              <tr><th>Name</th><th>Email</th><th>Role</th><th>Joined</th><th><span class="sr-only">Actions</span></th></tr>
            </thead>
            <tbody>
              @for (m of members(); track m.userId) {
                <tr [class.me]="isMe(m)">
                  <td>{{ m.displayName }} @if (isMe(m)) { <small class="you">(you)</small> }</td>
                  <td>{{ m.email }}</td>
                  <td>
                    @if (isOwner()) {
                      <app-select [ariaLabel]="'Role of ' + m.displayName" [options]="roles" [value]="m.role" [disabled]="busy() || lastOwner(m)"
                                  [title]="lastOwner(m) ? 'Make someone else an Owner first.' : ''" (valueChange)="changeRole(m, $event)" />
                    } @else {
                      {{ m.role }}
                    }
                  </td>
                  <td>{{ m.joinedUtc | date: 'mediumDate' }}</td>
                  <td class="actions">
                    @if (organization()?.isDefault) {
                      <!-- everyone belongs to the default organization: nobody leaves it or is removed from it -->
                    } @else if (isMe(m)) {
                      <button mat-button type="button" class="leave" [disabled]="busy() || lastOwner(m)"
                              [title]="lastOwner(m) ? 'Make someone else an Owner first.' : ''" (click)="remove(m)">Leave</button>
                    } @else if (isOwner()) {
                      <button mat-button type="button" class="remove danger" [attr.aria-label]="'Remove ' + m.displayName"
                              [disabled]="busy()" (click)="remove(m)">Remove</button>
                    }
                  </td>
                </tr>
              }
            </tbody>
          </table>
        }

        @for (message of errors(); track message) {
          <p class="fail" role="alert">{{ message }}</p>
        }
      </mat-card-content>
    </mat-card>

    @if (isOwner()) {
      <h3>Add a member</h3>
      <p class="hint">They need an account already. Roles: @for (r of roles; track r.value) { <strong>{{ r.label }}</strong> {{ r.hint }} }</p>
      <form [formGroup]="form" (ngSubmit)="add()" class="form-row">
        <mat-form-field>
          <mat-label>Email</mat-label>
          <input matInput type="email" formControlName="email" aria-label="Member email" autocomplete="off" />
        </mat-form-field>
        <app-select label="Role" ariaLabel="New member role" formControlName="role" [options]="roles" />
        <button mat-flat-button type="submit" class="add" [disabled]="form.invalid || busy()">Add</button>
      </form>
    }
  `,
  styles: `
    .members { width: 100%; border-collapse: collapse; }
    .members th { text-align: left; font-weight: 500; color: var(--mat-sys-on-surface-variant); }
    .members th, .members td { padding: 0.5rem 0.75rem; border-bottom: 1px solid var(--mat-sys-outline-variant); }
    .members tr.me td { background: var(--mat-sys-surface-container-low, transparent); }
    .actions { text-align: right; white-space: nowrap; }
    .danger { color: var(--mat-sys-error); }
    .you { color: var(--mat-sys-on-surface-variant); }
    .hint { color: var(--mat-sys-on-surface-variant); font-size: 0.9rem; max-width: 50rem; }
    .add { margin-top: 0.4rem; }
    .sr-only { position: absolute; width: 1px; height: 1px; overflow: hidden; clip: rect(0 0 0 0); white-space: nowrap; }
    @media (max-width: 40rem) { .members th:nth-child(4), .members td:nth-child(4) { display: none; } }
  `,
})
export class MembersComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly confirms = inject(ConfirmService);
  private readonly router = inject(Router);
  private readonly http = inject(HttpClient);
  private readonly api = inject(MembersService);
  private readonly auth = inject(AuthService);
  private readonly fb = inject(FormBuilder);

  protected readonly roles = MEMBER_ROLES;
  protected orgId = '';

  protected readonly organization = signal<Organization | null>(null);
  protected readonly members = signal<Member[]>([]);
  protected readonly loaded = signal(false);
  protected readonly busy = signal(false);
  protected readonly errors = signal<string[]>([]);

  protected readonly isOwner = computed(() => this.organization()?.role === 'Owner');
  private readonly ownerCount = computed(() => this.members().filter((m) => m.role === 'Owner').length);

  protected readonly form = this.fb.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    role: ['Viewer' as MemberRole, Validators.required],
  });

  ngOnInit(): void {
    this.orgId = this.route.snapshot.paramMap.get('id') ?? '';
    this.reload();
  }

  protected isMe(m: Member): boolean {
    return m.userId === this.auth.user()?.id;
  }

  /** The only Owner: the server will not let them go, so there is nothing to offer. */
  protected lastOwner(m: Member): boolean {
    return m.role === 'Owner' && this.ownerCount() <= 1;
  }

  protected add(): void {
    if (this.form.invalid) return;
    const { email, role } = this.form.getRawValue();
    this.run(this.api.add(this.orgId, email.trim(), role), 'Could not add the member.', () => this.form.controls.email.reset());
  }

  /** The select keeps showing the old role until the change is made and the list is read again, so a refusal needs no undoing. */
  protected changeRole(m: Member, value: string): void {
    const role = value as MemberRole;
    if (role === m.role) return;
    const change = () => this.run(this.api.changeRole(this.orgId, m.userId, role), 'Could not change the role.');

    // Giving up your own Owner role takes the controls away, so ask first.
    if (this.isMe(m) && m.role === 'Owner') {
      this.confirms
        .ask({
          title: 'Stop being an Owner?',
          message: 'Stop being an Owner? You will no longer be able to manage the members.',
          confirmLabel: 'Change my role',
          danger: true,
        })
        .subscribe((yes) => {
          if (yes) change();
        });
      return;
    }
    change();
  }

  protected remove(m: Member): void {
    const leaving = this.isMe(m);
    const question = leaving
      ? `Leave "${this.organization()?.name}"? You will lose access to everything in it.`
      : `Remove ${m.displayName} (${m.email})? They lose access to the organization at once; what they made stays.`;
    this.confirms
      .ask({ title: leaving ? 'Leave organization' : 'Remove member', message: question, confirmLabel: leaving ? 'Leave' : 'Remove', danger: true })
      .subscribe((yes) => {
        if (!yes) return;
        this.run(this.api.remove(this.orgId, m.userId), leaving ? 'Could not leave.' : 'Could not remove the member.', () => {
          if (leaving) this.router.navigate(['/organizations']);
        });
      });
  }

  private run(call: ReturnType<MembersService['add']>, failure: string, done?: () => void, failed?: () => void): void {
    this.busy.set(true);
    this.errors.set([]);
    call.subscribe({
      next: () => {
        this.busy.set(false);
        done?.();
        this.reload();
      },
      error: (e) => {
        this.busy.set(false);
        this.errors.set(problemMessages(e, failure));
        failed?.();
      },
    });
  }

  private reload(): void {
    this.http.get<Organization[]>('/api/organizations').subscribe({
      next: (orgs) => this.organization.set(orgs.find((o) => o.id === this.orgId) ?? null),
    });
    this.api.list(this.orgId).subscribe({
      next: (items) => {
        this.members.set(items);
        this.loaded.set(true);
      },
      error: (e) => {
        this.loaded.set(true);
        this.errors.set(problemMessages(e, 'Could not load the members.'));
      },
    });
  }
}
