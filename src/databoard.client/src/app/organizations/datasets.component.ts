import { HttpClient } from '@angular/common/http';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { QueryPreview, problemMessages } from './data-sources.service';
import { ChartComponent } from '../charts/chart.component';
import { Dashboard, DashboardSummary, DashboardsService, NEW_ID, WidgetChartType } from '../dashboards/dashboards.service';
import { Aggregation, ChartQuery, ChartResult, Dataset, DatasetsService, TimeGrain } from './datasets.service';

interface Organization {
  id: string;
  name: string;
  role: 'Viewer' | 'Editor' | 'Owner';
}

@Component({
  selector: 'app-datasets',
  imports: [ReactiveFormsModule, RouterLink, MatCardModule, MatFormFieldModule, MatInputModule, MatButtonModule, ChartComponent],
  template: `
    <p>
      <a mat-button [routerLink]="['/organizations', orgId]">&larr; Data sources</a>
      <a mat-button [routerLink]="['/organizations', orgId, 'dashboards']">Dashboards</a>
    </p>
    <h2>{{ organization()?.name }} <small>datasets</small></h2>

    @if (loaded() && datasets().length === 0) {
      <p><em>No datasets yet.</em>
        @if (canEdit()) { Run a query on a data source and choose <strong>Save as dataset</strong>. }
      </p>
    }

    @for (dataset of datasets(); track dataset.id) {
      <mat-card class="dataset" appearance="outlined">
        <mat-card-header>
          <mat-card-title>{{ dataset.name }}</mat-card-title>
          <span class="actions">
            <button mat-stroked-button type="button" (click)="show(dataset)">Preview</button>
            <button mat-stroked-button type="button" (click)="explore(dataset)">Explore</button>
            @if (canEdit()) {
              <button mat-button type="button" (click)="remove(dataset)">Delete</button>
            }
          </span>
        </mat-card-header>
        <mat-card-content>
        @if (dataset.description) { <p class="desc">{{ dataset.description }}</p> }

        <p class="chips">
          @for (column of dataset.columns; track column.name) {
            <span class="chip" [class.measure]="column.role === 'Measure'" [title]="column.dataType">
              {{ column.name }} <small>{{ column.role === 'Measure' ? 'measure' : 'dimension' }}</small>
            </span>
          }
        </p>

        @if (dataset.sql) { <pre class="sql">{{ dataset.sql }}</pre> }

        @if (exploringId() === dataset.id) {
          <form [formGroup]="exploreForm" (ngSubmit)="run(dataset)" class="explore form-row">
            <mat-form-field>
              <mat-label>Group by</mat-label>
              <select matNativeControl formControlName="dimension" aria-label="Group by">
                <option value="">(nothing)</option>
                @for (column of dataset.columns; track column.name) { <option [value]="column.name">{{ column.name }}</option> }
              </select>
            </mat-form-field>
            @if (grainAvailable(dataset)) {
              <mat-form-field>
              <mat-label>Per</mat-label>
              <select matNativeControl formControlName="grain" aria-label="Time grain">
                  <option value="">(exact value)</option>
                  @for (g of grains; track g) { <option [value]="g">{{ g }}</option> }
                </select>
            </mat-form-field>
            }
            <mat-form-field>
              <mat-label>Measure</mat-label>
              <select matNativeControl formControlName="aggregation" aria-label="Aggregation">
                @for (a of aggregations; track a) { <option [value]="a">{{ a }}</option> }
              </select>
            </mat-form-field>
            @if (exploreForm.controls.aggregation.value !== 'Count') {
              <mat-form-field>
              <mat-label>of</mat-label>
              <select matNativeControl formControlName="measureColumn" aria-label="Measure column">
                  @for (column of measureColumns(dataset); track column.name) { <option [value]="column.name">{{ column.name }}</option> }
                </select>
            </mat-form-field>
            }
            <mat-form-field>
              <mat-label>Show as</mat-label>
              <select matNativeControl formControlName="chartType" aria-label="Chart type">
                @for (t of chartTypes; track t) { <option [value]="t">{{ t }}</option> }
              </select>
            </mat-form-field>
            <button mat-flat-button type="submit" class="run" [disabled]="busy()">Run</button>
          </form>

          @if (result(); as r) {
            @if (exploreForm.controls.chartType.value !== 'Table' && r.rows.length > 0) {
              <app-chart [kind]="chartKind()" [result]="r" label="Chart of the query result" />
            }
            <div class="results">
              <table>
                <thead><tr>@for (c of r.columns; track $index) { <th>{{ c.name }}</th> }</tr></thead>
                <tbody>
                  @for (row of r.rows; track $index) {
                    <tr>
                      @for (cell of row; track $index) { <td>{{ cell === null ? '∅' : cell }}</td> }
                      @if (r.columns.length === 2 && exploreForm.controls.chartType.value === 'Table') {
                        <td class="bar-cell"><span class="bar" [style.width.%]="barWidth(r, $index)"></span></td>
                      }
                    </tr>
                  }
                </tbody>
              </table>
            </div>
            <p><small>{{ r.rows.length }} rows@if (r.truncated) { (more exist; raise the limit) }</small></p>
            @if (r.sql) { <details><summary>Generated SQL</summary><pre class="sql">{{ r.sql }}</pre></details> }
            @if (canEdit()) {
              @if (!addingToDashboard()) {
                <button mat-stroked-button type="button" (click)="startAdd()">Add to dashboard</button>
              } @else {
                <form [formGroup]="addForm" (ngSubmit)="addToDashboard(dataset)" class="add-widget form-row">
                  <mat-form-field>
                    <mat-label>Dashboard</mat-label>
                    <select matNativeControl formControlName="dashboardId" aria-label="Dashboard">
                      @for (d of dashboards(); track d.id) { <option [value]="d.id">{{ d.name }}</option> }
                    </select>
                  </mat-form-field>
                  <mat-form-field>
                    <mat-label>Chart title</mat-label>
                    <input matInput formControlName="title" aria-label="Chart title" />
                  </mat-form-field>
                  <button mat-flat-button type="submit" class="run" [disabled]="addForm.invalid || busy()">Add</button>
                </form>
                @if (dashboards().length === 0) {
                  <p><small>There are no dashboards yet. <a [routerLink]="['/organizations', orgId, 'dashboards']">Create one</a> first.</small></p>
                }
              }
              @if (addedTo(); as added) {
                <p class="ok" role="status">Added to "{{ added.name }}". <a [routerLink]="['/organizations', orgId, 'dashboards', added.id]">Open dashboard</a></p>
              }
            }
          }
          @for (message of errors(); track message) {
            <p class="fail" role="alert">{{ message }}</p>
          }
        }

        @if (selectedId() === dataset.id) {
          @if (preview(); as p) {
            <div class="results">
              <table>
                <thead><tr>@for (c of p.columns; track $index) { <th>{{ c.name }}</th> }</tr></thead>
                <tbody>
                  @for (row of p.rows; track $index) {
                    <tr>@for (cell of row; track $index) { <td>{{ cell === null ? '∅' : cell }}</td> }</tr>
                  }
                </tbody>
              </table>
            </div>
            <p><small>{{ p.rows.length }} rows@if (p.truncated) { (more rows exist) }</small></p>
          }
          @for (message of errors(); track message) {
            <p class="fail" role="alert">{{ message }}</p>
          }
        }
        </mat-card-content>
      </mat-card>
    }
  `,
  styles: `
    .dataset { margin: 1rem 0; }
    .desc { margin: 0.25rem 0; color: var(--mat-sys-on-surface-variant); }
    .chips { display: flex; flex-wrap: wrap; gap: 0.35rem; }
    mat-card-header { align-items: center; }
    .explore { margin: 1rem 0 0; }
    .run { margin-top: 0.4rem; }
    .bar-cell { width: 12rem; }
    .bar { display: block; height: 0.8rem; background: var(--mat-sys-primary); border-radius: 2px; }
  `,
})
export class DatasetsComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly http = inject(HttpClient);
  private readonly api = inject(DatasetsService);

  protected orgId = '';
  protected readonly organization = signal<Organization | null>(null);
  protected readonly canEdit = computed(() => {
    const role = this.organization()?.role;
    return role === 'Editor' || role === 'Owner';
  });
  protected readonly datasets = signal<Dataset[]>([]);
  protected readonly loaded = signal(false);
  protected readonly selectedId = signal<string | null>(null);
  protected readonly preview = signal<QueryPreview | null>(null);
  protected readonly errors = signal<string[]>([]);

  protected readonly aggregations: Aggregation[] = ['Count', 'Sum', 'Avg', 'Min', 'Max', 'CountDistinct'];
  protected readonly grains: TimeGrain[] = ['Day', 'Week', 'Month', 'Quarter', 'Year'];
  protected readonly chartTypes: WidgetChartType[] = ['Table', 'Bar', 'Line', 'Pie'];
  protected readonly exploringId = signal<string | null>(null);
  private readonly dashboardsApi = inject(DashboardsService);
  private lastQuery: ChartQuery | null = null;
  protected readonly addingToDashboard = signal(false);
  protected readonly dashboards = signal<DashboardSummary[]>([]);
  protected readonly addedTo = signal<{ id: string; name: string } | null>(null);
  protected readonly result = signal<ChartResult | null>(null);
  protected readonly busy = signal(false);
  private readonly fb = inject(FormBuilder);
  protected readonly exploreForm = this.fb.nonNullable.group({
    dimension: [''],
    grain: [''],
    aggregation: ['Count' as Aggregation],
    measureColumn: [''],
    chartType: ['Table' as WidgetChartType],
  });
  protected readonly addForm = this.fb.nonNullable.group({
    dashboardId: ['', Validators.required],
    title: ['', [Validators.required, Validators.maxLength(200)]],
  });

  protected chartKind(): 'Bar' | 'Line' | 'Pie' {
    const type = this.exploreForm.controls.chartType.value;
    return type === 'Table' ? 'Bar' : type;
  }

  ngOnInit(): void {
    this.orgId = this.route.snapshot.paramMap.get('id') ?? '';
    this.http.get<Organization[]>('/api/organizations').subscribe({
      next: (orgs) => this.organization.set(orgs.find((o) => o.id === this.orgId) ?? null),
    });
    this.reload();
  }

  protected show(dataset: Dataset): void {
    this.selectedId.set(dataset.id);
    this.preview.set(null);
    this.errors.set([]);
    this.api.preview(this.orgId, dataset.id).subscribe({
      next: (p) => this.preview.set(p),
      error: (e) => this.errors.set(problemMessages(e, 'Could not load the data.')),
    });
  }

  protected explore(dataset: Dataset): void {
    this.exploringId.set(this.exploringId() === dataset.id ? null : dataset.id);
    this.result.set(null);
    this.errors.set([]);
    const firstMeasure = dataset.columns.find((c) => c.role === 'Measure');
    this.exploreForm.reset({ dimension: '', grain: '', aggregation: 'Count', measureColumn: firstMeasure?.name ?? '', chartType: 'Table' });
    this.addingToDashboard.set(false);
    this.addedTo.set(null);
  }

  protected grainAvailable(dataset: Dataset): boolean {
    const column = dataset.columns.find((c) => c.name === this.exploreForm.controls.dimension.value);
    return !!column && /^(date|timestamp)/i.test(column.dataType);
  }

  /** Sum and average only make sense on numbers; the others work on any column. */
  protected measureColumns(dataset: Dataset) {
    const numbersOnly = ['Sum', 'Avg'].includes(this.exploreForm.controls.aggregation.value);
    return numbersOnly ? dataset.columns.filter((c) => c.role === 'Measure') : dataset.columns;
  }

  protected run(dataset: Dataset): void {
    const v = this.exploreForm.getRawValue();
    const query: ChartQuery = {
      dimensions: v.dimension
        ? [{ column: v.dimension, ...(v.grain && this.grainAvailable(dataset) ? { grain: v.grain as TimeGrain } : {}) }]
        : [],
      measures: [
        v.aggregation === 'Count'
          ? { aggregation: 'Count', alias: 'count' }
          : { aggregation: v.aggregation, column: v.measureColumn, alias: 'value' },
      ],
      limit: 100,
    };

    this.lastQuery = query;
    this.addedTo.set(null);
    this.busy.set(true);
    this.errors.set([]);
    this.api.query(this.orgId, dataset.id, query).subscribe({
      next: (r) => {
        this.result.set(r);
        this.busy.set(false);
      },
      error: (e) => {
        this.result.set(null);
        this.busy.set(false);
        this.errors.set(problemMessages(e, 'The query failed.'));
      },
    });
  }

  protected startAdd(): void {
    this.addingToDashboard.set(true);
    this.dashboardsApi.list(this.orgId).subscribe({
      next: (items) => {
        this.dashboards.set(items);
        if (items.length > 0 && !this.addForm.controls.dashboardId.value) this.addForm.controls.dashboardId.setValue(items[0].id);
      },
      error: (e) => this.errors.set(problemMessages(e, 'Could not load dashboards.')),
    });
  }

  /** Appends the chart just explored (its query and chart type) as a new widget of the chosen dashboard. */
  protected addToDashboard(dataset: Dataset): void {
    if (this.addForm.invalid || !this.lastQuery) return;
    const { dashboardId, title } = this.addForm.getRawValue();
    const query = this.lastQuery;
    const chartType = this.exploreForm.controls.chartType.value;

    this.busy.set(true);
    this.errors.set([]);
    this.dashboardsApi.get(this.orgId, dashboardId).subscribe({
      next: (dashboard: Dashboard) => {
        const widget = { id: NEW_ID, title, datasetId: dataset.id, chartType, query };
        this.dashboardsApi
          .update(this.orgId, dashboard, { ...dashboard.definition, widgets: [...dashboard.definition.widgets, widget] })
          .subscribe({
            next: (saved) => {
              this.busy.set(false);
              this.addedTo.set({ id: saved.id, name: saved.name });
              this.addForm.controls.title.reset();
            },
            error: (e) => {
              this.busy.set(false);
              this.errors.set(problemMessages(e, 'Could not add the chart.'));
            },
          });
      },
      error: (e) => {
        this.busy.set(false);
        this.errors.set(problemMessages(e, 'Could not load the dashboard.'));
      },
    });
  }

  /** Width of a bar as a percentage of the largest value in the second column. */
  protected barWidth(result: ChartResult, rowIndex: number): number {
    const values = result.rows.map((r) => Number(r[1]) || 0);
    const max = Math.max(...values, 0);
    return max > 0 ? Math.max(0, (values[rowIndex] / max) * 100) : 0;
  }

  protected remove(dataset: Dataset): void {
    if (!confirm(`Delete the dataset "${dataset.name}"?`)) return;
    this.api.remove(this.orgId, dataset.id).subscribe({
      next: () => this.reload(),
      error: (e) => {
        this.selectedId.set(dataset.id);
        this.errors.set(problemMessages(e, 'Could not delete the dataset.'));
      },
    });
  }

  private reload(): void {
    this.api.list(this.orgId).subscribe({
      next: (items) => {
        this.datasets.set(items);
        this.loaded.set(true);
      },
    });
  }
}
