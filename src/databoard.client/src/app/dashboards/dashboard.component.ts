import { Component, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { FormBuilder, FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { ChartViewComponent } from '../charts/chart-view.component';
import { ExportMenuComponent } from '../charts/export-menu.component';
import { Chart, ChartsService } from '../charts/charts.service';
import { problemMessages } from '../organizations/data-sources.service';
import { AddChartsDialogComponent, AddChartsDialogData } from './add-charts-dialog.component';
import { DashboardGridComponent } from './dashboard-grid.component';
import { ShareDialogComponent, ShareDialogData } from './share-dialog.component';
import { LayoutItem, arrange, sameLayout } from './dashboard-layout';
import { Dataset, DatasetsService } from '../organizations/datasets.service';
import {
  Dashboard,
  DashboardDefinition,
  DashboardFilter,
  DashboardRun,
  DashboardWidget,
  DashboardsService,
  FilterTarget,
  FilterValues,
  NEW_ID,
  SHARED_FILTER_OPERATORS,
  SharedFilterOperator,
  WidgetResult,
} from './dashboards.service';

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
  imports: [ReactiveFormsModule, RouterLink, MatCardModule, MatFormFieldModule, MatInputModule, MatButtonModule, ChartViewComponent, DashboardGridComponent, ExportMenuComponent],
  template: `
    <mat-card appearance="raised" class="detail">
    @if (dashboard(); as d) {
      <mat-card-header>
        <mat-card-title>{{ d.name }}</mat-card-title>
        <span class="actions no-print">
          @if (d.definition.widgets.length > 0 && !editingLayout()) {
            <button mat-button type="button" class="print" (click)="print()">Print / PDF</button>
          }
          @if (canEdit()) {
            @if (editingLayout()) {
              <button mat-flat-button type="button" class="save-layout" [disabled]="busy() || !layoutChanged()" (click)="saveLayout(d)">Save layout</button>
              <button mat-button type="button" class="cancel-layout" [disabled]="busy()" (click)="cancelLayout()">Cancel</button>
            } @else {
              @if (d.definition.widgets.length > 0) {
                <button mat-stroked-button type="button" class="edit-layout" (click)="editLayout()">Edit layout</button>
              }
              <button mat-stroked-button type="button" class="share" (click)="share(d)">Share</button>
              <button mat-stroked-button type="button" (click)="addCharts(d)">Add chart</button>
              <button mat-button type="button" (click)="removeDashboard(d)">Delete dashboard</button>
            }
          }
        </span>
      </mat-card-header>
      <mat-card-content>

      @if (d.definition.filters.length > 0) {
        <form class="filters form-row no-print" [formGroup]="filterForm" (ngSubmit)="apply()">
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
          @if (canEdit()) { Use <strong>Add chart</strong> to put charts on it, or <a [routerLink]="['/organizations', orgId(), 'charts', 'new']">create a new chart</a>. }
        </p>
      }

      <app-dashboard-grid [items]="items()" [editing]="editingLayout()" [labels]="labels()" (layoutChange)="draft.set($event)">
        <ng-template let-id>
          @if (widgetById(id); as widget) {
          <mat-card appearance="outlined" class="widget" data-export-scope>
            @if (chartFor(widget.chartId); as chart) {
              <mat-card-header>
                <mat-card-title>{{ chart.name }}</mat-card-title>
                <mat-card-subtitle>{{ datasetName(chart.datasetId) }}</mat-card-subtitle>
                @if (!editingLayout()) {
                  <span class="widget-actions no-print">
                    @if (resultFor(widget.id)?.result; as exported) {
                      <app-export-menu [name]="chart.name" [result]="exported" [type]="chart.type" />
                    }
                    @if (canEdit()) {
                      <a mat-button [routerLink]="['/organizations', orgId(), 'charts', chart.id]">Edit chart</a>
                      <button mat-button type="button" class="remove-widget" (click)="removeWidget(d, widget.id)">Remove</button>
                    }
                  </span>
                }
              </mat-card-header>
              <mat-card-content>
                @if (resultFor(widget.id); as r) {
                  @if (r.error) {
                    <p class="fail" role="alert">{{ r.error }}</p>
                  } @else if (r.result; as result) {
                    <app-chart-view [fill]="true" [type]="chart.type" [result]="result" [chartStyle]="chart.definition.style"
                                    [dimensions]="chart.definition.query.dimensions?.length ?? 0" [label]="chart.name" />
                    @if (result.truncated) { <p class="note"><small>Showing the first {{ result.rows.length }} rows.</small></p> }
                  }
                } @else {
                  <p><em>Loading...</em></p>
                }
              </mat-card-content>
            } @else {
              <mat-card-header>
                <mat-card-title>Chart not found</mat-card-title>
                @if (canEdit() && !editingLayout()) {
                  <span class="widget-actions no-print">
                    <button mat-button type="button" class="remove-widget" (click)="removeWidget(d, widget.id)">Remove</button>
                  </span>
                }
              </mat-card-header>
              <mat-card-content><p class="fail" role="alert">This chart was deleted.</p></mat-card-content>
            }
          </mat-card>
          }
        </ng-template>
      </app-dashboard-grid>

      @if (canEdit()) {
        <h3 class="no-print">Add a shared filter</h3>
        <form class="form-row no-print" [formGroup]="newFilterForm" (ngSubmit)="addFilter(d)">
          <mat-form-field>
            <mat-label>Label</mat-label>
            <input matInput formControlName="label" aria-label="Filter label" />
          </mat-form-field>
          <mat-form-field>
            <mat-label>Applies to</mat-label>
            <select matNativeControl formControlName="target" aria-label="Filter applies to" (change)="setTarget($any($event.target).value)">
              <option value="Column">A column</option>
              <option value="Parameter" [disabled]="parameterNames().length === 0">A dataset parameter</option>
            </select>
          </mat-form-field>
          <mat-form-field>
            <mat-label>{{ target() === 'Parameter' ? 'Parameter' : 'Column' }}</mat-label>
            <select matNativeControl formControlName="column" aria-label="Filter column">
              @for (name of targetNames(); track name) { <option [value]="name">{{ name }}</option> }
            </select>
            <mat-hint>
              @if (target() === 'Parameter') { Sets the value for every chart whose dataset has this parameter }
              @else { Applies to every chart whose dataset has this column }
            </mat-hint>
          </mat-form-field>
          @if (target() === 'Column') {
            <mat-form-field>
              <mat-label>Condition</mat-label>
              <select matNativeControl formControlName="operator" aria-label="Filter condition">
                @for (op of operators; track op.value) { <option [value]="op.value">{{ op.label }}</option> }
              </select>
            </mat-form-field>
          }
          <button mat-flat-button type="submit" class="apply" [disabled]="newFilterForm.invalid || busy()">Add filter</button>
        </form>
      }
      </mat-card-content>
    } @else if (errors().length === 0) {
      <mat-card-content><p><em>Loading...</em></p></mat-card-content>
    }

    @if (errors().length > 0) {
      <mat-card-content>
        @for (message of errors(); track message) {
          <p class="fail" role="alert">{{ message }}</p>
        }
      </mat-card-content>
    }
    </mat-card>
  `,
  styles: `
    :host { display: block; min-width: 0; }
    /* The card fills the pane; only its content scrolls, so a long dashboard never grows the page. */
    .detail { --mat-card-elevated-container-color: #fff; height: 100%; }
    mat-card-title { font: var(--mat-sys-title-medium); }
    mat-card-header { align-items: center; flex-wrap: wrap; gap: 0.5rem; }
    .actions { margin-left: auto; display: flex; gap: 0.4rem; }
    .widget-actions { margin-left: auto; display: flex; }
    .detail > mat-card-content { flex: 1; min-height: 0; overflow: auto; padding-top: 0.75rem; }
    .filters { margin: 0.5rem 0 1rem; }
    .apply, .remove-filter { margin-top: 0.4rem; }
    /* A widget fills its cell on the grid; its chart takes what the header leaves, and a table scrolls inside. */
    .widget { height: 100%; }
    .widget mat-card-header { align-items: center; }
    .widget mat-card-content { flex: 1; min-height: 0; overflow: hidden; display: flex; flex-direction: column; padding-bottom: 0.5rem; }
    .widget app-chart-view { flex: 1; min-height: 0; }
    .note { margin: 0.25rem 0 0; }
    /* The grid is one column on a narrow screen, so there is nothing to arrange. */
    @media (max-width: 640px) { .edit-layout { display: none; } }
  `,
})
export class DashboardComponent implements OnInit {
  private readonly api = inject(DashboardsService);
  private readonly datasetsApi = inject(DatasetsService);
  private readonly chartsApi = inject(ChartsService);
  private readonly dialog = inject(MatDialog);
  private readonly fb = inject(FormBuilder);

  readonly orgId = input.required<string>();
  readonly dashboardId = input.required<string>();
  readonly canEdit = input(false);

  /** The dashboard after it was saved (a chart or filter was added or removed), so the page can update its list. */
  readonly changed = output<Dashboard>();
  readonly deleted = output<string>();

  protected readonly operators = SHARED_FILTER_OPERATORS;

  protected readonly dashboard = signal<Dashboard | null>(null);
  protected readonly datasets = signal<Dataset[]>([]);
  protected readonly charts = signal<Chart[]>([]);
  protected readonly run = signal<DashboardRun | null>(null);
  protected readonly errors = signal<string[]>([]);
  protected readonly busy = signal(false);

  /** True while the editor is arranging widgets; the changes live in `draft` until they are saved. */
  protected readonly editingLayout = signal(false);
  protected readonly draft = signal<LayoutItem[] | null>(null);
  /** Where each widget sits as saved, with places made up for widgets that have none yet. */
  private readonly savedLayout = computed(() => arrange(this.dashboard()?.definition.widgets ?? []));
  protected readonly items = computed(() => this.draft() ?? this.savedLayout());
  protected readonly layoutChanged = computed(() => !sameLayout(this.items(), this.savedLayout()));
  protected readonly labels = computed(() => {
    const names: Record<string, string> = {};
    for (const w of this.dashboard()?.definition.widgets ?? []) names[w.id] = this.chartFor(w.chartId)?.name ?? 'missing chart';
    return names;
  });

  protected filterForm: FormGroup<Record<string, FormControl<string>>> = this.fb.nonNullable.group({});
  protected readonly newFilterForm = this.fb.nonNullable.group({
    label: ['', [Validators.required, Validators.maxLength(200)]],
    column: ['', Validators.required],
    operator: ['In' as SharedFilterOperator],
    target: ['Column' as FilterTarget],
  });
  /** What the filter being made acts on. A parameter sets one value, so its condition is always "is". */
  protected readonly target = signal<FilterTarget>('Column');

  protected readonly columnNames = computed(() =>
    [...new Set(this.datasets().flatMap((d) => d.columns.map((c) => c.name)))].sort((a, b) => a.localeCompare(b)),
  );
  protected readonly parameterNames = computed(() =>
    [...new Set(this.datasets().flatMap((d) => (d.parameters ?? []).map((p) => p.name)))].sort((a, b) => a.localeCompare(b)),
  );
  protected readonly targetNames = computed(() => (this.target() === 'Parameter' ? this.parameterNames() : this.columnNames()));

  protected setTarget(target: FilterTarget): void {
    this.target.set(target);
    const controls = this.newFilterForm.controls;
    controls.column.setValue(this.targetNames()[0] ?? '');
    controls.operator.setValue(target === 'Parameter' ? 'Eq' : 'In');
  }

  ngOnInit(): void {
    this.chartsApi.list(this.orgId()).subscribe({ next: (items) => this.charts.set(items) });
    this.datasetsApi.list(this.orgId()).subscribe({
      next: (items) => {
        this.datasets.set(items);
        if (!this.newFilterForm.controls.column.value && this.columnNames().length > 0) {
          this.newFilterForm.controls.column.setValue(this.columnNames()[0]);
        }
      },
    });
    this.api.get(this.orgId(), this.dashboardId()).subscribe({
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

  protected chartFor(id: string): Chart | undefined {
    return this.charts().find((c) => c.id === id);
  }

  protected widgetById(id: string): DashboardWidget | undefined {
    return this.dashboard()?.definition.widgets.find((w) => w.id === id);
  }

  /** Public links: make one (and copy it) or revoke the ones that exist. */
  protected share(d: Dashboard): void {
    this.dialog.open<ShareDialogComponent, ShareDialogData>(ShareDialogComponent, {
      data: { orgId: this.orgId(), dashboardId: d.id, dashboardName: d.name },
      width: '36rem',
      maxWidth: '95vw',
    });
  }

  /** Opens the browser's print dialog, whose "Save as PDF" makes the file. The print styles leave only the dashboard on the page. */
  protected print(): void {
    window.print();
  }

  protected editLayout(): void {
    this.draft.set(this.savedLayout());
    this.editingLayout.set(true);
  }

  protected cancelLayout(): void {
    this.editingLayout.set(false);
    this.draft.set(null);
  }

  protected saveLayout(d: Dashboard): void {
    const layout = this.items();
    const widgets = d.definition.widgets.map((w) => {
      const place = layout.find((i) => i.id === w.id);
      return place ? { ...w, layout: { x: place.x, y: place.y, w: place.w, h: place.h } } : w;
    });
    // The charts are not run again: moving a widget does not change what it shows.
    this.save(d, { ...d.definition, widgets }, () => this.cancelLayout(), false);
  }

  /** Writes the places the page worked out into the widgets, so they stay where they are when others come and go. */
  private placed(widgets: DashboardWidget[]): DashboardWidget[] {
    const layout = arrange(widgets);
    return widgets.map((w) => {
      const place = layout.find((i) => i.id === w.id)!;
      return { ...w, layout: { x: place.x, y: place.y, w: place.w, h: place.h } };
    });
  }

  /** Lets the editor pick saved charts to put on this dashboard (or go and make a new one). */
  protected addCharts(d: Dashboard): void {
    const placed = new Set(d.definition.widgets.map((w) => w.chartId));
    const names: Record<string, string> = {};
    for (const dataset of this.datasets()) names[dataset.id] = dataset.name;

    // The list may have grown since the dashboard opened (a chart was made in the editor and we came back).
    this.chartsApi.list(this.orgId()).subscribe({
      next: (items) => {
        this.charts.set(items);
        this.dialog
          .open<AddChartsDialogComponent, AddChartsDialogData, string[]>(AddChartsDialogComponent, {
            data: { orgId: this.orgId(), charts: items.filter((c) => !placed.has(c.id)), datasetNames: names },
            width: '30rem',
            maxWidth: '95vw',
          })
          .afterClosed()
          .subscribe((ids) => {
            if (!ids || ids.length === 0) return;
            // New widgets have no id yet, so they are placed under their position in the list and the server gives ids.
            const widgets = this.placed([...d.definition.widgets, ...ids.map((chartId, n) => ({ id: `${NEW_ID}-${n}`, chartId }))])
              .map((w) => (w.id.startsWith(NEW_ID) ? { ...w, id: NEW_ID } : w));
            this.save(d, { ...d.definition, widgets });
          });
      },
      error: (e) => this.errors.set(problemMessages(e, 'Could not load the charts.')),
    });
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
      filters: [...d.definition.filters, { id: NEW_ID, label: v.label, column: v.column, operator: v.operator, target: v.target }],
    }, () => this.newFilterForm.controls.label.reset());
  }

  protected removeFilter(d: Dashboard, filter: DashboardFilter): void {
    this.save(d, { ...d.definition, filters: d.definition.filters.filter((f) => f.id !== filter.id) });
  }

  protected removeWidget(d: Dashboard, widgetId: string): void {
    this.save(d, { ...d.definition, widgets: this.placed(d.definition.widgets.filter((w) => w.id !== widgetId)) });
  }

  protected removeDashboard(d: Dashboard): void {
    if (!confirm(`Delete the dashboard "${d.name}"?`)) return;
    this.api.remove(this.orgId(), d.id).subscribe({
      next: () => this.deleted.emit(d.id),
      error: (e) => this.errors.set(problemMessages(e, 'Could not delete the dashboard.')),
    });
  }

  private save(d: Dashboard, definition: DashboardDefinition, done?: () => void, rerun = true): void {
    this.busy.set(true);
    this.errors.set([]);
    this.api.update(this.orgId(), d, definition).subscribe({
      next: (saved) => {
        this.busy.set(false);
        this.show(saved);
        this.changed.emit(saved);
        done?.();
        if (rerun) this.execute();
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
    this.api.run(this.orgId(), d.id, values).subscribe({
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
