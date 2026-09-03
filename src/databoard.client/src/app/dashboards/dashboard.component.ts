import { HttpClient } from '@angular/common/http';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormBuilder, FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ChartComponent } from '../charts/chart.component';
import { problemMessages } from '../organizations/data-sources.service';
import { Dataset, DatasetsService } from '../organizations/datasets.service';
import {
  Dashboard,
  DashboardDefinition,
  DashboardFilter,
  DashboardRun,
  DashboardsService,
  FilterValues,
  NEW_ID,
  SHARED_FILTER_OPERATORS,
  SharedFilterOperator,
  WidgetResult,
} from './dashboards.service';

interface Organization {
  id: string;
  name: string;
  role: 'Viewer' | 'Editor' | 'Owner';
}

/** In and Between take several values, typed as a comma-separated list; the rest take one. */
export function parseFilterInput(operator: SharedFilterOperator, text: string): string[] {
  const trimmed = text.trim();
  if (!trimmed) return [];
  return operator === 'In' || operator === 'Between'
    ? trimmed.split(',').map((v) => v.trim()).filter((v) => v.length > 0)
    : [trimmed];
}

@Component({
  selector: 'app-dashboard',
  imports: [ReactiveFormsModule, RouterLink, MatCardModule, MatFormFieldModule, MatInputModule, MatButtonModule, ChartComponent],
  template: `
    <p><a mat-button [routerLink]="['/organizations', orgId, 'dashboards']">&larr; Dashboards</a></p>

    @if (dashboard(); as d) {
      <header class="title">
        <h2>{{ d.name }}</h2>
        @if (canEdit()) {
          <button mat-button type="button" (click)="removeDashboard(d)">Delete dashboard</button>
        }
      </header>

      @if (d.definition.filters.length > 0) {
        <form class="filters form-row" [formGroup]="filterForm" (ngSubmit)="apply()">
          @for (filter of d.definition.filters; track filter.id) {
            <mat-form-field>
              <mat-label>{{ filter.label }} ({{ operatorLabel(filter.operator) }})</mat-label>
              <input matInput [formControlName]="filter.id" [attr.aria-label]="filter.label" />
              @if (filter.operator === 'In' || filter.operator === 'Between') {
                <mat-hint>Separate values with commas</mat-hint>
              }
            </mat-form-field>
            @if (canEdit()) {
              <button mat-button type="button" class="remove-filter" (click)="removeFilter(d, filter)">Remove {{ filter.label }}</button>
            }
          }
          <button mat-flat-button type="submit" class="apply" [disabled]="busy()">Apply filters</button>
          <button mat-button type="button" class="apply" (click)="clearFilters()">Clear</button>
        </form>
      }

      @if (d.definition.widgets.length === 0) {
        <p><em>This dashboard has no charts yet.</em>
          @if (canEdit()) { Open a dataset, choose <strong>Explore</strong>, then <strong>Add to dashboard</strong>. }
        </p>
      }

      <div class="widgets">
        @for (widget of d.definition.widgets; track widget.id) {
          <mat-card appearance="outlined" class="widget">
            <mat-card-header>
              <mat-card-title>{{ widget.title }}</mat-card-title>
              <mat-card-subtitle>{{ datasetName(widget.datasetId) }}</mat-card-subtitle>
              @if (canEdit()) {
                <button mat-button type="button" class="actions" (click)="removeWidget(d, widget.id)">Remove</button>
              }
            </mat-card-header>
            <mat-card-content>
              @if (resultFor(widget.id); as r) {
                @if (r.error) {
                  <p class="fail" role="alert">{{ r.error }}</p>
                } @else if (r.result; as result) {
                  @if (widget.chartType === 'Table') {
                    <div class="results">
                      <table>
                        <thead><tr>@for (c of result.columns; track $index) { <th>{{ c.name }}</th> }</tr></thead>
                        <tbody>
                          @for (row of result.rows; track $index) {
                            <tr>@for (cell of row; track $index) { <td>{{ cell === null ? '∅' : cell }}</td> }</tr>
                          }
                        </tbody>
                      </table>
                    </div>
                  } @else {
                    <app-chart [kind]="widget.chartType" [result]="result" [label]="widget.title" />
                  }
                  @if (result.truncated) { <p><small>Showing the first {{ result.rows.length }} rows.</small></p> }
                }
              } @else {
                <p><em>Loading...</em></p>
              }
            </mat-card-content>
          </mat-card>
        }
      </div>

      @if (canEdit()) {
        <h3>Add a shared filter</h3>
        <form class="form-row" [formGroup]="newFilterForm" (ngSubmit)="addFilter(d)">
          <mat-form-field>
            <mat-label>Label</mat-label>
            <input matInput formControlName="label" aria-label="Filter label" />
          </mat-form-field>
          <mat-form-field>
            <mat-label>Column</mat-label>
            <select matNativeControl formControlName="column" aria-label="Filter column">
              @for (name of columnNames(); track name) { <option [value]="name">{{ name }}</option> }
            </select>
            <mat-hint>Applies to every chart whose dataset has this column</mat-hint>
          </mat-form-field>
          <mat-form-field>
            <mat-label>Condition</mat-label>
            <select matNativeControl formControlName="operator" aria-label="Filter condition">
              @for (op of operators; track op.value) { <option [value]="op.value">{{ op.label }}</option> }
            </select>
          </mat-form-field>
          <button mat-flat-button type="submit" class="apply" [disabled]="newFilterForm.invalid || busy()">Add filter</button>
        </form>
      }
    } @else if (errors().length === 0) {
      <p><em>Loading...</em></p>
    }

    @for (message of errors(); track message) {
      <p class="fail" role="alert">{{ message }}</p>
    }
  `,
  styles: `
    .title { display: flex; align-items: baseline; gap: 1rem; }
    .title h2 { margin: 0 0 1rem; }
    .filters { margin: 0.5rem 0 1rem; }
    .apply, .remove-filter { margin-top: 0.4rem; }
    .widgets { display: grid; grid-template-columns: repeat(auto-fill, minmax(24rem, 1fr)); gap: 1rem; }
    mat-card-header { align-items: center; }
  `,
})
export class DashboardComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly http = inject(HttpClient);
  private readonly api = inject(DashboardsService);
  private readonly datasetsApi = inject(DatasetsService);
  private readonly fb = inject(FormBuilder);

  protected orgId = '';
  protected readonly operators = SHARED_FILTER_OPERATORS;

  protected readonly organization = signal<Organization | null>(null);
  protected readonly canEdit = computed(() => {
    const role = this.organization()?.role;
    return role === 'Editor' || role === 'Owner';
  });
  protected readonly dashboard = signal<Dashboard | null>(null);
  protected readonly datasets = signal<Dataset[]>([]);
  protected readonly run = signal<DashboardRun | null>(null);
  protected readonly errors = signal<string[]>([]);
  protected readonly busy = signal(false);

  protected filterForm: FormGroup<Record<string, FormControl<string>>> = this.fb.nonNullable.group({});
  protected readonly newFilterForm = this.fb.nonNullable.group({
    label: ['', [Validators.required, Validators.maxLength(200)]],
    column: ['', Validators.required],
    operator: ['In' as SharedFilterOperator],
  });

  protected readonly columnNames = computed(() =>
    [...new Set(this.datasets().flatMap((d) => d.columns.map((c) => c.name)))].sort((a, b) => a.localeCompare(b)),
  );

  private dashboardId = '';

  ngOnInit(): void {
    this.orgId = this.route.snapshot.paramMap.get('id') ?? '';
    this.dashboardId = this.route.snapshot.paramMap.get('dashboardId') ?? '';

    this.http.get<Organization[]>('/api/organizations').subscribe({
      next: (orgs) => this.organization.set(orgs.find((o) => o.id === this.orgId) ?? null),
    });
    this.datasetsApi.list(this.orgId).subscribe({
      next: (items) => {
        this.datasets.set(items);
        if (!this.newFilterForm.controls.column.value && this.columnNames().length > 0) {
          this.newFilterForm.controls.column.setValue(this.columnNames()[0]);
        }
      },
    });
    this.api.get(this.orgId, this.dashboardId).subscribe({
      next: (d) => {
        this.show(d);
        this.execute();
      },
      error: (e) => this.errors.set(problemMessages(e, 'Could not load the dashboard.')),
    });
  }

  protected operatorLabel(operator: SharedFilterOperator): string {
    return this.operators.find((o) => o.value === operator)?.label ?? operator;
  }

  protected datasetName(id: string): string {
    return this.datasets().find((d) => d.id === id)?.name ?? '';
  }

  protected resultFor(widgetId: string): WidgetResult | undefined {
    return this.run()?.widgets.find((w) => w.widgetId === widgetId);
  }

  protected apply(): void {
    this.execute();
  }

  protected clearFilters(): void {
    this.filterForm.reset();
    this.execute();
  }

  protected addFilter(d: Dashboard): void {
    if (this.newFilterForm.invalid) return;
    const v = this.newFilterForm.getRawValue();
    this.save(d, {
      ...d.definition,
      filters: [...d.definition.filters, { id: NEW_ID, label: v.label, column: v.column, operator: v.operator }],
    }, () => this.newFilterForm.controls.label.reset());
  }

  protected removeFilter(d: Dashboard, filter: DashboardFilter): void {
    this.save(d, { ...d.definition, filters: d.definition.filters.filter((f) => f.id !== filter.id) });
  }

  protected removeWidget(d: Dashboard, widgetId: string): void {
    this.save(d, { ...d.definition, widgets: d.definition.widgets.filter((w) => w.id !== widgetId) });
  }

  protected removeDashboard(d: Dashboard): void {
    if (!confirm(`Delete the dashboard "${d.name}"?`)) return;
    this.api.remove(this.orgId, d.id).subscribe({
      next: () => this.router.navigate(['/organizations', this.orgId, 'dashboards']),
      error: (e) => this.errors.set(problemMessages(e, 'Could not delete the dashboard.')),
    });
  }

  private save(d: Dashboard, definition: DashboardDefinition, done?: () => void): void {
    this.busy.set(true);
    this.errors.set([]);
    this.api.update(this.orgId, d, definition).subscribe({
      next: (saved) => {
        this.busy.set(false);
        this.show(saved);
        done?.();
        this.execute();
      },
      error: (e) => {
        this.busy.set(false);
        this.errors.set(problemMessages(e, 'Could not save the dashboard.'));
      },
    });
  }

  /** Shows a dashboard and keeps the values already typed into filters that still exist. */
  private show(d: Dashboard): void {
    const previous = this.filterForm?.getRawValue() ?? {};
    const controls: Record<string, FormControl<string>> = {};
    for (const f of d.definition.filters) {
      controls[f.id] = this.fb.nonNullable.control(previous[f.id] ?? '');
    }
    this.filterForm = this.fb.nonNullable.group(controls);
    this.dashboard.set(d);
  }

  private execute(): void {
    const d = this.dashboard();
    if (!d) return;

    const values: FilterValues = {};
    for (const f of d.definition.filters) {
      const parsed = parseFilterInput(f.operator, this.filterForm.controls[f.id]?.value ?? '');
      if (parsed.length > 0) values[f.id] = parsed;
    }

    this.busy.set(true);
    this.errors.set([]);
    this.api.run(this.orgId, d.id, values).subscribe({
      next: (r) => {
        this.run.set(r);
        this.busy.set(false);
      },
      error: (e) => {
        this.busy.set(false);
        this.errors.set(problemMessages(e, 'Could not run the dashboard.'));
      },
    });
  }
}
