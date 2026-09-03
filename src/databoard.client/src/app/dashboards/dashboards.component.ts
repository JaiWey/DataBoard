import { HttpClient } from '@angular/common/http';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { problemMessages } from '../organizations/data-sources.service';
import { DashboardSummary, DashboardsService } from './dashboards.service';

interface Organization {
  id: string;
  name: string;
  role: 'Viewer' | 'Editor' | 'Owner';
}

@Component({
  selector: 'app-dashboards',
  imports: [ReactiveFormsModule, RouterLink, MatCardModule, MatFormFieldModule, MatInputModule, MatButtonModule],
  template: `
    <p><a mat-button routerLink="/organizations">&larr; Organizations</a></p>
    <h2>{{ organization()?.name }} <small>dashboards</small></h2>

    @if (loaded() && dashboards().length === 0) {
      <p><em>No dashboards yet.</em></p>
    }

    <div class="grid">
      @for (d of dashboards(); track d.id) {
        <mat-card appearance="outlined">
          <mat-card-header>
            <mat-card-title>{{ d.name }}</mat-card-title>
            <mat-card-subtitle>{{ d.widgetCount }} {{ d.widgetCount === 1 ? 'chart' : 'charts' }}</mat-card-subtitle>
          </mat-card-header>
          <mat-card-actions>
            <a mat-button [routerLink]="['/organizations', orgId, 'dashboards', d.id]">Open</a>
          </mat-card-actions>
        </mat-card>
      }
    </div>

    @if (canEdit()) {
      <h3>New dashboard</h3>
      <form [formGroup]="form" (ngSubmit)="create()" class="form-row">
        <mat-form-field>
          <mat-label>Dashboard name</mat-label>
          <input matInput formControlName="name" aria-label="Dashboard name" />
        </mat-form-field>
        <button mat-flat-button type="submit" class="create" [disabled]="form.invalid || busy()">Create</button>
      </form>
    }

    @for (message of errors(); track message) {
      <p class="fail" role="alert">{{ message }}</p>
    }
  `,
  styles: `
    .grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(16rem, 1fr)); gap: 1rem; }
    .create { margin-top: 0.4rem; }
  `,
})
export class DashboardsComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly http = inject(HttpClient);
  private readonly api = inject(DashboardsService);
  private readonly fb = inject(FormBuilder);

  protected orgId = '';
  protected readonly organization = signal<Organization | null>(null);
  protected readonly canEdit = computed(() => {
    const role = this.organization()?.role;
    return role === 'Editor' || role === 'Owner';
  });
  protected readonly dashboards = signal<DashboardSummary[]>([]);
  protected readonly loaded = signal(false);
  protected readonly errors = signal<string[]>([]);
  protected readonly busy = signal(false);
  protected readonly form = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(200)]],
  });

  ngOnInit(): void {
    this.orgId = this.route.snapshot.paramMap.get('id') ?? '';
    this.http.get<Organization[]>('/api/organizations').subscribe({
      next: (orgs) => this.organization.set(orgs.find((o) => o.id === this.orgId) ?? null),
    });
    this.reload();
  }

  protected create(): void {
    if (this.form.invalid) return;
    this.busy.set(true);
    this.errors.set([]);
    this.api.create(this.orgId, this.form.getRawValue().name).subscribe({
      next: () => {
        this.busy.set(false);
        this.form.reset();
        this.reload();
      },
      error: (e) => {
        this.busy.set(false);
        this.errors.set(problemMessages(e, 'Could not create the dashboard.'));
      },
    });
  }

  private reload(): void {
    this.api.list(this.orgId).subscribe({
      next: (items) => {
        this.dashboards.set(items);
        this.loaded.set(true);
      },
    });
  }
}
