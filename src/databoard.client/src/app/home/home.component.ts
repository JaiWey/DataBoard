import { DatePipe } from '@angular/common';
import { Component, computed, effect, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDividerModule } from '@angular/material/divider';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSortModule, Sort } from '@angular/material/sort';
import { MatTableModule } from '@angular/material/table';
import { MatTabsModule } from '@angular/material/tabs';
import { RouterLink } from '@angular/router';
import { forkJoin } from 'rxjs';
import { AuthService } from '../auth/auth.service';
import { DashboardsService } from '../dashboards/dashboards.service';
import { DataSourcesService } from '../organizations/data-sources.service';
import { DatasetsService } from '../organizations/datasets.service';
import { OrgContext } from '../organizations/org-context.service';
import { AppSelectComponent, SelectOption } from '../shared/app-select.component';

type Kind = 'Dashboard' | 'Dataset' | 'Data source';
type Tab = 'recent' | Kind;

interface Row {
  key: string;
  name: string;
  kind: Kind;
  detail: string;
  updatedUtc: string;
  link: string[];
}

const TABS: { id: Tab; label: string }[] = [
  { id: 'recent', label: 'Recent' },
  { id: 'Dashboard', label: 'Dashboards' },
  { id: 'Dataset', label: 'Datasets' },
  { id: 'Data source', label: 'Data sources' },
];

/** The workbench: who you are, what you can create, and everything in the current organization at a glance. */
@Component({
  selector: 'app-home',
  imports: [
    RouterLink,
    DatePipe,
    MatButtonModule,
    MatCardModule,
    MatDividerModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatSortModule,
    MatTableModule,
    MatTabsModule,
    AppSelectComponent,
  ],
  template: `
    @if (ctx.loaded() && !org()) {
      <mat-card class="welcome" appearance="raised">
        <mat-card-header><mat-card-title>Welcome to DataBoard</mat-card-title></mat-card-header>
        <mat-card-content>
          <p>You are not in an organization yet. Create one to start connecting data.</p>
        </mat-card-content>
        <mat-card-actions>
          <a mat-flat-button class="primary" routerLink="/organizations">Create an organization</a>
        </mat-card-actions>
      </mat-card>
    } @else if (org(); as o) {
      <div class="layout">
        <aside>
          <mat-card appearance="raised" class="profile">
            <mat-card-content>
              <div class="who">
                <span class="avatar" aria-hidden="true">{{ initials() }}</span>
                <div>
                  <strong>{{ auth.user()?.displayName }}</strong>
                  <div class="muted">{{ auth.user()?.email }}</div>
                </div>
              </div>

              <app-select class="org" label="Organization" ariaLabel="Organization" [options]="organizationOptions()" [value]="o.id" (valueChange)="choose($event)" />
              <div class="muted role">Your role: {{ o.role }}</div>

              <mat-divider />
              <dl class="counts">
                <div><dt>Dashboards</dt><dd>{{ dashboards().length }}</dd></div>
                <div><dt>Datasets</dt><dd>{{ datasets().length }}</dd></div>
                <div><dt>Data sources</dt><dd>{{ sources().length }}</dd></div>
              </dl>
            </mat-card-content>
          </mat-card>

          <mat-card appearance="raised">
            <mat-card-header><mat-card-title>Quick create</mat-card-title></mat-card-header>
            <mat-card-content class="quick">
              <a mat-stroked-button class="tile" [routerLink]="['/organizations', o.id, 'dashboards']">
                <span class="icon blue" matButtonIcon aria-hidden="true">
                  <svg viewBox="0 0 24 24" width="20" height="20"><rect x="4" y="12" width="4" height="8" fill="currentColor"/><rect x="10" y="6" width="4" height="14" fill="currentColor"/><rect x="16" y="9" width="4" height="11" fill="currentColor"/></svg>
                </span>
                Dashboard
              </a>
              <a mat-stroked-button class="tile" [routerLink]="['/organizations', o.id]">
                <span class="icon purple" matButtonIcon aria-hidden="true">
                  <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" stroke-width="2"><ellipse cx="12" cy="6" rx="7" ry="3"/><path d="M5 6v12c0 1.7 3.1 3 7 3s7-1.3 7-3V6"/><path d="M5 12c0 1.7 3.1 3 7 3s7-1.3 7-3"/></svg>
                </span>
                Data source
              </a>
              <a mat-stroked-button class="tile" [routerLink]="['/organizations', o.id, 'datasets']">
                <span class="icon cyan" matButtonIcon aria-hidden="true">
                  <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" stroke-width="2"><path d="M12 3l8 4.5v9L12 21l-8-4.5v-9z"/><path d="M12 12l8-4.5M12 12L4 7.5M12 12v9"/></svg>
                </span>
                Dataset
              </a>
              <a mat-stroked-button class="tile" routerLink="/organizations">
                <span class="icon orange" matButtonIcon aria-hidden="true">
                  <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" stroke-width="2"><rect x="4" y="4" width="16" height="16" rx="2"/><path d="M4 10h16M10 10v10"/></svg>
                </span>
                Organizations
              </a>
            </mat-card-content>
          </mat-card>
        </aside>

        <mat-card appearance="raised" class="main">
          <mat-card-content>
            <mat-tab-group
              class="tabs"
              [mat-stretch-tabs]="false"
              [selectedIndex]="tabIndex()"
              (selectedIndexChange)="tab.set(tabs[$event].id)"
              animationDuration="0ms"
              aria-label="Show"
            >
              @for (t of tabs; track t.id) {
                <mat-tab [label]="t.label" />
              }
            </mat-tab-group>

            <div class="toolbar">
              <mat-form-field subscriptSizing="dynamic" class="search">
                <mat-label>Search by name</mat-label>
                <input matInput type="search" aria-label="Search" [value]="query()" (input)="query.set($any($event.target).value)" />
              </mat-form-field>
            </div>

            <table mat-table [dataSource]="rows()" [trackBy]="trackRow" matSort matSortActive="updated" matSortDirection="desc" matSortDisableClear (matSortChange)="sorted($event)">
              <ng-container matColumnDef="name">
                <th mat-header-cell *matHeaderCellDef>Name</th>
                <td mat-cell *matCellDef="let row"><a [routerLink]="row.link">{{ row.name }}</a></td>
              </ng-container>
              <ng-container matColumnDef="kind">
                <th mat-header-cell *matHeaderCellDef>Type</th>
                <td mat-cell *matCellDef="let row"><span class="kind" [attr.data-kind]="row.kind">{{ row.kind }}</span></td>
              </ng-container>
              <ng-container matColumnDef="detail">
                <th mat-header-cell *matHeaderCellDef>Details</th>
                <td mat-cell *matCellDef="let row" class="muted">{{ row.detail }}</td>
              </ng-container>
              <ng-container matColumnDef="updated">
                <th mat-header-cell *matHeaderCellDef mat-sort-header>Last updated</th>
                <td mat-cell *matCellDef="let row" class="muted">{{ row.updatedUtc | date: 'medium' }}</td>
              </ng-container>
              <ng-container matColumnDef="actions">
                <th mat-header-cell *matHeaderCellDef>Actions</th>
                <td mat-cell *matCellDef="let row"><a mat-button [routerLink]="row.link">Open</a></td>
              </ng-container>

              <tr mat-header-row *matHeaderRowDef="columns"></tr>
              <tr mat-row *matRowDef="let row; columns: columns"></tr>
            </table>

            @if (!busy() && rows().length === 0) {
              <div class="empty">
                <svg viewBox="0 0 120 90" width="120" height="90" aria-hidden="true">
                  <path d="M20 40l40-14 40 14v32l-40 14-40-14z" fill="#e8ebf1" stroke="#c3c9d6"/>
                  <path d="M20 40l40 14 40-14M60 54v32" fill="none" stroke="#c3c9d6"/>
                  <path d="M44 14l4 8M60 8v10M76 14l-4 8" stroke="#8d96a8" stroke-width="2" stroke-linecap="round"/>
                </svg>
                <p>{{ query() ? 'Nothing matches your search.' : 'Nothing here yet.' }}</p>
              </div>
            }
            @if (error()) { <p class="fail" role="alert">{{ error() }}</p> }
          </mat-card-content>
        </mat-card>
      </div>
    } @else {
      <p><em>Loading...</em></p>
    }
  `,
  styles: `
    :host {
      --mat-card-elevated-container-color: #fff;
      --mat-table-background-color: #fff;
      --mat-button-outlined-container-shape: 10px;
    }
    .layout { display: grid; grid-template-columns: 22rem 1fr; gap: 1.25rem; align-items: start; }
    @media (max-width: 60rem) { .layout { grid-template-columns: 1fr; } }
    aside { display: grid; gap: 1.25rem; }
    mat-card-content { padding-top: 0.75rem; }
    mat-card-title { font: var(--mat-sys-title-medium); }
    .muted { color: var(--mat-sys-on-surface-variant); font-size: 0.85rem; }
    .welcome { max-width: 32rem; margin: 2rem auto; text-align: center; }
    .welcome mat-card-actions { justify-content: center; padding-bottom: 1rem; }

    .who { display: flex; gap: 0.75rem; align-items: center; margin-bottom: 1rem; }
    .avatar { width: 3rem; height: 3rem; border-radius: 50%; background: var(--mat-sys-primary); color: var(--mat-sys-on-primary); display: grid; place-items: center; font-weight: 600; }
    .org { width: 100%; }
    .role { margin: 0.4rem 0 1rem; }
    .counts { display: flex; justify-content: space-between; margin: 1rem 0 0; }
    .counts dt { color: var(--mat-sys-on-surface-variant); font-size: 0.85rem; }
    .counts dd { margin: 0.2rem 0 0; font-size: 1.5rem; font-weight: 500; }

    .quick { display: grid; grid-template-columns: 1fr 1fr; gap: 0.75rem; }
    .tile { justify-content: flex-start; height: 3.25rem; padding: 0 0.75rem; white-space: nowrap; }
    .icon { width: 2rem; height: 2rem; border-radius: 8px; display: inline-grid; place-items: center; color: #fff; margin-right: 0.5rem; }
    .icon.blue { background: #3370ff; } .icon.purple { background: #7f3bf5; } .icon.cyan { background: #14c0ff; } .icon.orange { background: #ff8800; }

    .toolbar { display: flex; justify-content: flex-end; margin: 1rem 0; }
    .search { width: 16rem; max-width: 100%; }
    table { width: 100%; }
    td a:not([mat-button]) { text-decoration: none; }
    td a:not([mat-button]):hover { text-decoration: underline; }
    .kind { padding: 0.1rem 0.5rem; border-radius: 4px; font-size: 0.8rem; background: var(--mat-sys-secondary-container); color: var(--mat-sys-on-secondary-container); }
    .empty { text-align: center; padding: 3rem 0; color: var(--mat-sys-on-surface-variant); }
    .fail { color: var(--mat-sys-error); }
    :host ::ng-deep .tabs .mat-mdc-tab-body-wrapper { display: none; }
  `,
})
export class HomeComponent {
  protected readonly auth = inject(AuthService);
  protected readonly ctx = inject(OrgContext);
  private readonly dashboardsApi = inject(DashboardsService);
  private readonly datasetsApi = inject(DatasetsService);
  private readonly sourcesApi = inject(DataSourcesService);

  protected readonly tabs = TABS;
  protected readonly columns = ['name', 'kind', 'detail', 'updated', 'actions'];
  protected readonly tab = signal<Tab>('recent');
  protected readonly tabIndex = computed(() => TABS.findIndex((t) => t.id === this.tab()));
  protected readonly query = signal('');
  protected readonly newestFirst = signal(true);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly org = this.ctx.current;
  protected readonly organizationOptions = computed<SelectOption[]>(() => this.ctx.organizations().map((o) => ({ value: o.id, label: o.name })));
  protected readonly dashboards = signal<Row[]>([]);
  protected readonly datasets = signal<Row[]>([]);
  protected readonly sources = signal<Row[]>([]);

  protected readonly initials = computed(() => {
    const name = this.auth.user()?.displayName?.trim() ?? '';
    const letters = name.split(/\s+/).filter(Boolean).slice(0, 2).map((w) => w[0].toUpperCase());
    return letters.join('') || '?';
  });

  /** The rows of the chosen tab, filtered by the search text and sorted by last update. */
  protected readonly rows = computed(() => {
    const tab = this.tab();
    const needle = this.query().trim().toLowerCase();
    const all = [...this.dashboards(), ...this.datasets(), ...this.sources()];
    const shown = all
      .filter((r) => tab === 'recent' || r.kind === tab)
      .filter((r) => !needle || r.name.toLowerCase().includes(needle))
      .sort((a, b) => (a.updatedUtc < b.updatedUtc ? 1 : a.updatedUtc > b.updatedUtc ? -1 : 0));
    if (!this.newestFirst()) shown.reverse();
    return tab === 'recent' ? shown.slice(0, 20) : shown;
  });

  private requests = 0;

  constructor() {
    effect(() => {
      const id = this.org()?.id;
      if (id) this.loadFor(id);
    });
  }

  protected choose(id: string): void {
    this.ctx.select(id);
  }

  protected sorted(sort: Sort): void {
    this.newestFirst.set(sort.direction !== 'asc');
  }

  protected trackRow = (_: number, row: Row) => row.key;

  private loadFor(id: string): void {
    const request = ++this.requests;
    this.busy.set(true);
    this.error.set(null);

    forkJoin({
      dashboards: this.dashboardsApi.list(id),
      datasets: this.datasetsApi.list(id),
      sources: this.sourcesApi.list(id),
    }).subscribe({
      next: (r) => {
        if (request !== this.requests) return; // the user switched organization while this was loading
        this.dashboards.set(
          r.dashboards.map((d) => ({
            key: `dashboard:${d.id}`,
            name: d.name,
            kind: 'Dashboard',
            detail: `${d.widgetCount} ${d.widgetCount === 1 ? 'chart' : 'charts'}`,
            updatedUtc: d.updatedUtc,
            link: ['/organizations', id, 'dashboards', d.id],
          })),
        );
        this.datasets.set(
          r.datasets.map((d) => ({
            key: `dataset:${d.id}`,
            name: d.name,
            kind: 'Dataset',
            detail: `${d.columns.length} columns`,
            updatedUtc: d.updatedUtc,
            link: ['/organizations', id, 'datasets'],
          })),
        );
        this.sources.set(
          r.sources.map((s) => ({
            key: `source:${s.id}`,
            name: s.name,
            kind: 'Data source',
            detail: s.type,
            updatedUtc: s.createdUtc,
            link: ['/organizations', id],
          })),
        );
        this.busy.set(false);
      },
      error: () => {
        if (request !== this.requests) return;
        this.busy.set(false);
        this.error.set('Could not load this organization.');
      },
    });
  }
}
