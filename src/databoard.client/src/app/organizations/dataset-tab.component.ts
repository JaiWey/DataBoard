import { Component, computed, effect, inject, input, linkedSignal, output, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { RouterLink } from '@angular/router';
import { ChartComponent } from '../charts/chart.component';
import { Dashboard, DashboardSummary, DashboardsService, NEW_ID, WidgetChartType } from '../dashboards/dashboards.service';
import { SqlEditorComponent } from '../sql-editor/sql-editor.component';
import { SqlResultsComponent } from '../sql-editor/sql-results.component';
import { DataSourcesService, MAX_PREVIEW_ROWS, QueryPreview, problemMessages } from './data-sources.service';
import { TableViewBuilderComponent } from './table-view-builder.component';
import { Aggregation, ChartQuery, ChartResult, Dataset, DatasetsService, TableViewDefinition, TimeGrain, emptyTableView } from './datasets.service';

/** One open dataset: its SQL editor and the preview/explore panels. Each tab keeps its own state while hidden. */
@Component({
  selector: 'app-dataset-tab',
  imports: [ReactiveFormsModule, RouterLink, MatCardModule, MatFormFieldModule, MatInputModule, MatButtonModule, ChartComponent, SqlEditorComponent, SqlResultsComponent, TableViewBuilderComponent],
  template: `
    <mat-card appearance="raised" class="detail">
      <mat-card-header>
        <mat-card-title>{{ dataset().name }}</mat-card-title>
        <span class="actions">
          <button mat-stroked-button type="button" (click)="show()">Preview</button>
          <button mat-stroked-button type="button" (click)="explore()">Explore</button>
          @if (canEdit()) {
            <button mat-button type="button" (click)="remove()">Delete</button>
          }
        </span>
      </mat-card-header>
      <mat-card-content>
        @if (dataset().description) { <p class="desc">{{ dataset().description }}</p> }

        <p class="chips">
          @for (column of dataset().columns; track column.name) {
            <span class="chip" [class.measure]="column.role === 'Measure'" [title]="column.dataType">
              {{ column.name }} <small>{{ column.role === 'Measure' ? 'measure' : 'dimension' }}</small>
            </span>
          }
        </p>

        @if (isView()) {
          <app-table-view-builder [orgId]="orgId()" [dataSourceId]="dataset().dataSourceId" [(definition)]="viewDefinition" />
          <p class="save-row">
            <button mat-flat-button type="button" class="save" [disabled]="!dirty() || busy()" (click)="saveSql()">Save</button>
            @if (dirty()) { <small>Unsaved changes</small> }
          </p>
          <details class="generated"><summary>Generated SQL</summary><app-sql-editor [value]="dataset().sql ?? ''" [readOnly]="true" label="Generated SQL" /></details>
        } @else if (dataset().sql !== null) {
          <app-sql-editor [value]="sqlText()" [readOnly]="!canEdit()" (valueChange)="sqlText.set($event)" (save)="saveSql()" (run)="testSql()" />
          @if (canEdit()) {
            <p class="save-row">
              <button mat-flat-button type="button" class="save" [disabled]="!dirty() || busy()" (click)="saveSql()">Save</button>
              <button mat-stroked-button type="button" class="test" [disabled]="testing() || !sqlText().trim()" (click)="testSql()">Run query</button>
              @if (dirty()) { <small>Unsaved changes</small> }
            </p>
          }
        }
        @for (message of sqlErrors(); track message) {
          <p class="fail" role="alert">{{ message }}</p>
        }
        <app-sql-results [result]="testResult()" [errors]="testErrors()" />

        @if (panel() === 'explore') {
          <form [formGroup]="exploreForm" (ngSubmit)="run()" class="explore form-row">
            <mat-form-field>
              <mat-label>Group by</mat-label>
              <select matNativeControl formControlName="dimension" aria-label="Group by">
                <option value="">(nothing)</option>
                @for (column of dataset().columns; track column.name) { <option [value]="column.name">{{ column.name }}</option> }
              </select>
            </mat-form-field>
            @if (grainAvailable()) {
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
                  @for (column of measureColumns(); track column.name) { <option [value]="column.name">{{ column.name }}</option> }
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
                <form [formGroup]="addForm" (ngSubmit)="addToDashboard()" class="add-widget form-row">
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
                  <p><small>There are no dashboards yet. <a [routerLink]="['/organizations', orgId(), 'dashboards']">Create one</a> first.</small></p>
                }
              }
              @if (addedTo(); as added) {
                <p class="ok" role="status">Added to "{{ added.name }}". <a [routerLink]="['/organizations', orgId(), 'dashboards', added.id]">Open dashboard</a></p>
              }
            }
          }
          @for (message of errors(); track message) {
            <p class="fail" role="alert">{{ message }}</p>
          }
        }

        @if (panel() === 'preview') {
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
  `,
  styles: `
    :host { display: block; min-width: 0; }
    :host(.inactive) { display: none; }
    /* The card fills the tab's pane; only its content scrolls, so a long result never grows the page. */
    .detail { --mat-card-elevated-container-color: #fff; height: 100%; }
    mat-card-content { flex: 1; min-height: 0; overflow: auto; }
    mat-card-title { font: var(--mat-sys-title-medium); }
    mat-card-header { align-items: center; flex-wrap: wrap; gap: 0.5rem; }
    .actions { margin-left: auto; display: flex; gap: 0.4rem; }
    mat-card-content { padding-top: 0.75rem; }
    .desc { margin: 0.25rem 0; color: var(--mat-sys-on-surface-variant); }
    .chips { display: flex; flex-wrap: wrap; gap: 0.35rem; }
    .generated { margin: 0.5rem 0; }
    .save-row { display: flex; align-items: center; gap: 0.75rem; margin: 0.5rem 0; }
    .explore { margin: 1rem 0 0; }
    .run { margin-top: 0.4rem; }
    .bar-cell { width: 12rem; }
    .bar { display: block; height: 0.8rem; background: var(--mat-sys-primary); border-radius: 2px; }
  `,
  host: { '[class.inactive]': '!active()' },
})
export class DatasetTabComponent {
  private readonly api = inject(DatasetsService);
  private readonly dashboardsApi = inject(DashboardsService);
  private readonly sourcesApi = inject(DataSourcesService);
  private readonly fb = inject(FormBuilder);

  readonly dataset = input.required<Dataset>();
  readonly orgId = input.required<string>();
  readonly canEdit = input(false);
  /** Only the visible tab is shown; the others stay alive so their editor text and results survive. */
  readonly active = input(true);

  /** The saved dataset, after the SQL was saved. */
  readonly updated = output<Dataset>();
  readonly deleted = output<string>();
  readonly dirtyChange = output<boolean>();

  /** The text in the editor; starts from, and resets to, the saved SQL whenever the dataset changes. */
  protected readonly sqlText = linkedSignal(() => this.dataset().sql ?? '');
  /** A table view that this user may edit: built step by step instead of written as SQL. */
  protected readonly isView = computed(() => this.canEdit() && this.dataset().kind === 'TableView' && this.dataset().definition !== null);
  /** The table view being edited; starts from, and resets to, the saved definition. */
  protected readonly viewDefinition = linkedSignal<TableViewDefinition>(() => this.dataset().definition ?? emptyTableView());
  protected readonly dirty = computed(() =>
    this.isView()
      ? JSON.stringify(this.viewDefinition()) !== JSON.stringify(this.dataset().definition)
      : this.dataset().sql !== null && this.sqlText() !== this.dataset().sql);
  protected readonly sqlErrors = signal<string[]>([]);
  /** Rows from running the editor's current SQL (saved or not) against the dataset's data source. */
  protected readonly testResult = signal<QueryPreview | null>(null);
  protected readonly testErrors = signal<string[]>([]);
  protected readonly testing = signal(false);

  protected readonly panel = signal<'preview' | 'explore' | null>(null);
  protected readonly preview = signal<QueryPreview | null>(null);
  protected readonly errors = signal<string[]>([]);
  protected readonly result = signal<ChartResult | null>(null);
  protected readonly busy = signal(false);
  protected readonly addingToDashboard = signal(false);
  protected readonly dashboards = signal<DashboardSummary[]>([]);
  protected readonly addedTo = signal<{ id: string; name: string } | null>(null);
  private lastQuery: ChartQuery | null = null;

  protected readonly aggregations: Aggregation[] = ['Count', 'Sum', 'Avg', 'Min', 'Max', 'CountDistinct'];
  protected readonly grains: TimeGrain[] = ['Day', 'Week', 'Month', 'Quarter', 'Year'];
  protected readonly chartTypes: WidgetChartType[] = ['Table', 'Bar', 'Line', 'Pie'];

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

  constructor() {
    effect(() => this.dirtyChange.emit(this.dirty()));
  }

  protected chartKind(): 'Bar' | 'Line' | 'Pie' {
    const type = this.exploreForm.controls.chartType.value;
    return type === 'Table' ? 'Bar' : type;
  }

  /** Runs the SQL as it is in the editor, so a change can be tried before it is saved. */
  protected testSql(): void {
    const sql = this.sqlText().trim();
    if (!this.canEdit() || !sql || this.testing()) return;
    this.testing.set(true);
    this.testResult.set(null);
    this.testErrors.set([]);
    this.sourcesApi.preview(this.orgId(), this.dataset().dataSourceId, sql, MAX_PREVIEW_ROWS).subscribe({
      next: (r) => {
        this.testing.set(false);
        this.testResult.set(r);
      },
      error: (e) => {
        this.testing.set(false);
        this.testErrors.set(problemMessages(e, 'The query failed.'));
      },
    });
  }

  protected saveSql(): void {
    const dataset = this.dataset();
    if (!this.canEdit() || !this.dirty() || this.busy()) return;
    this.busy.set(true);
    this.sqlErrors.set([]);
    const request = this.isView()
      ? this.api.updateTableView(this.orgId(), dataset.id, dataset.name, this.viewDefinition(), dataset.description)
      : this.api.update(this.orgId(), dataset.id, dataset.name, this.sqlText(), dataset.description);
    request.subscribe({
      next: (saved) => {
        this.busy.set(false);
        // The columns may have changed with the query, so any open results no longer describe the dataset.
        this.result.set(null);
        this.preview.set(null);
        this.panel.set(null);
        this.updated.emit(saved);
      },
      error: (e) => {
        this.busy.set(false);
        this.sqlErrors.set(problemMessages(e, 'Could not save the query.'));
      },
    });
  }

  protected show(): void {
    this.panel.set('preview');
    this.preview.set(null);
    this.errors.set([]);
    this.api.preview(this.orgId(), this.dataset().id).subscribe({
      next: (p) => this.preview.set(p),
      error: (e) => this.errors.set(problemMessages(e, 'Could not load the data.')),
    });
  }

  protected explore(): void {
    this.panel.set(this.panel() === 'explore' ? null : 'explore');
    this.result.set(null);
    this.errors.set([]);
    const firstMeasure = this.dataset().columns.find((c) => c.role === 'Measure');
    this.exploreForm.reset({ dimension: '', grain: '', aggregation: 'Count', measureColumn: firstMeasure?.name ?? '', chartType: 'Table' });
    this.addingToDashboard.set(false);
    this.addedTo.set(null);
  }

  protected grainAvailable(): boolean {
    const column = this.dataset().columns.find((c) => c.name === this.exploreForm.controls.dimension.value);
    return !!column && /^(date|timestamp)/i.test(column.dataType);
  }

  /** Sum and average only make sense on numbers; the others work on any column. */
  protected measureColumns() {
    const numbersOnly = ['Sum', 'Avg'].includes(this.exploreForm.controls.aggregation.value);
    return numbersOnly ? this.dataset().columns.filter((c) => c.role === 'Measure') : this.dataset().columns;
  }

  protected run(): void {
    const v = this.exploreForm.getRawValue();
    const query: ChartQuery = {
      dimensions: v.dimension
        ? [{ column: v.dimension, ...(v.grain && this.grainAvailable() ? { grain: v.grain as TimeGrain } : {}) }]
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
    this.api.query(this.orgId(), this.dataset().id, query).subscribe({
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
    this.dashboardsApi.list(this.orgId()).subscribe({
      next: (items) => {
        this.dashboards.set(items);
        if (items.length > 0 && !this.addForm.controls.dashboardId.value) this.addForm.controls.dashboardId.setValue(items[0].id);
      },
      error: (e) => this.errors.set(problemMessages(e, 'Could not load dashboards.')),
    });
  }

  /** Appends the chart just explored (its query and chart type) as a new widget of the chosen dashboard. */
  protected addToDashboard(): void {
    if (this.addForm.invalid || !this.lastQuery) return;
    const { dashboardId, title } = this.addForm.getRawValue();
    const query = this.lastQuery;
    const chartType = this.exploreForm.controls.chartType.value;
    const orgId = this.orgId();

    this.busy.set(true);
    this.errors.set([]);
    this.dashboardsApi.get(orgId, dashboardId).subscribe({
      next: (dashboard: Dashboard) => {
        const widget = { id: NEW_ID, title, datasetId: this.dataset().id, chartType, query };
        this.dashboardsApi
          .update(orgId, dashboard, { ...dashboard.definition, widgets: [...dashboard.definition.widgets, widget] })
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

  protected remove(): void {
    const dataset = this.dataset();
    if (!confirm(`Delete the dataset "${dataset.name}"?`)) return;
    this.api.remove(this.orgId(), dataset.id).subscribe({
      next: () => this.deleted.emit(dataset.id),
      error: (e) => this.errors.set(problemMessages(e, 'Could not delete the dataset.')),
    });
  }
}
