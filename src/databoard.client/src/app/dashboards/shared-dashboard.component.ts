import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { FormBuilder, FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { Observable, Subscription } from 'rxjs';
import { ChartViewComponent } from '../charts/chart-view.component';
import { ExportMenuComponent } from '../charts/export-menu.component';
import { problemMessages } from '../organizations/data-sources.service';
import { BackgroundLayerComponent } from './background-layer.component';
import { MarkdownComponent } from '../shared/markdown.component';
import { ImageWidgetComponent } from './image-widget.component';
import { DashboardGridComponent } from './dashboard-grid.component';
import { ago } from '../shared/relative-time';
import { arrange } from './dashboard-layout';
import { parseFilterInput } from './dashboard.component';
import { DashboardRun, DashboardWidget, FilterValues, SHARED_FILTER_OPERATORS, WidgetResult } from './dashboards.service';
import { SharedChart, SharedDashboard, SharedDashboardsService } from './shared-dashboards.service';

/**
 * A dashboard opened from a public link: no account, read-only. The token in the address is the only credential.
 * Visitors can type values into the shared filters, export a chart's rows or picture, and print the page to PDF.
 */
@Component({
  selector: 'app-shared-dashboard',
  imports: [ReactiveFormsModule, MatCardModule, MatButtonModule, MatFormFieldModule, MatInputModule, ChartViewComponent, DashboardGridComponent, ExportMenuComponent, BackgroundLayerComponent, ImageWidgetComponent, MarkdownComponent],
  template: `
    <header class="bar">
      <svg viewBox="0 0 32 32" width="24" height="24" aria-hidden="true">
        <rect x="2" y="2" width="28" height="28" rx="7" fill="#3370ff" />
        <rect x="8" y="16" width="4" height="8" rx="1" fill="#fff" />
        <rect x="14" y="10" width="4" height="14" rx="1" fill="#fff" />
        <rect x="20" y="13" width="4" height="11" rx="1" fill="#fff" />
      </svg>
      <span class="brand">DataBoard</span>
      <span class="note no-print">Shared view</span>
    </header>

    <main class="page">
      @if (dashboard(); as d) {
        @if (d.background; as background) {
          <app-background-layer [load]="loadBackground(background.version)" [version]="background.version" [fit]="background.fit" [opacity]="background.opacity" />
        }
        <div class="title">
          <div>
            <h1>{{ d.name }}</h1>
            @if (d.description) { <p class="desc">{{ d.description }}</p> }
          </div>
          @if (d.widgets.length > 0) {
            <button mat-stroked-button type="button" class="print no-print" (click)="print()">Print / PDF</button>
          }
        </div>

        @if (d.filters.length > 0) {
          <form class="filters no-print" [formGroup]="filterForm" (ngSubmit)="execute()">
            @for (filter of d.filters; track filter.id) {
              <mat-form-field>
                <mat-label>{{ filter.label }} ({{ operatorLabel(filter.operator) }})</mat-label>
                <input matInput [formControlName]="filter.id" [attr.aria-label]="filter.label" />
                @if (filter.operator === 'In' || filter.operator === 'Between') {
                  <mat-hint>Separate values with commas</mat-hint>
                }
              </mat-form-field>
            }
            <button mat-flat-button type="submit" class="apply" [disabled]="busy()">Apply filters</button>
            <button mat-button type="button" class="apply" (click)="clear()">Clear</button>
          </form>
        }

        @if (d.widgets.length === 0) {
          <p><em>This dashboard has no charts.</em></p>
        }

        <app-dashboard-grid [items]="items()" [labels]="labels()">
          <ng-template let-id>
            @if (imageOf(id); as picture) {
              <mat-card appearance="outlined" class="widget picture">
                <app-image-widget [load]="loadWidgetImage(picture)" [version]="picture.imageKey ?? ''" [fit]="picture.imageFit ?? 'contain'"
                                  [alt]="picture.alt" [linkUrl]="picture.linkUrl" />
              </mat-card>
            } @else if (textOf(id); as words) {
              <mat-card appearance="outlined" class="widget words">
                <app-markdown class="words-text" [text]="words.text ?? ''" [align]="words.textAlign ?? 'left'" [size]="words.textSize ?? 'normal'" />
              </mat-card>
            } @else if (chartOf(id); as chart) {
              <mat-card appearance="outlined" class="widget" data-export-scope>
                <mat-card-header>
                  <mat-card-title>{{ chart.name }}</mat-card-title>
                  @if (resultOf(id)?.result; as exported) {
                    <span class="widget-actions no-print">
                      <app-export-menu [name]="chart.name" [result]="exported" [type]="chart.type" />
                    </span>
                  }
                </mat-card-header>
                <mat-card-content>
                  @if (resultOf(id); as r) {
                    @if (r.error) {
                      <p class="fail" role="alert">{{ r.error }}</p>
                    } @else if (r.result; as result) {
                      <app-chart-view [fill]="true" [type]="chart.type" [result]="result" [chartStyle]="chart.style"
                                      [dimensions]="chart.dimensionCount" [label]="chart.name" />
                      @if (result.truncated || result.cachedUtc) {
                        <p class="truncated">
                          <small>
                            @if (result.truncated) { Showing the first {{ result.rows.length }} rows. }
                            @if (result.cachedUtc; as stored) { <span class="cached">Data from {{ age(stored) }}.</span> }
                          </small>
                        </p>
                      }
                    }
                  } @else {
                    <p><em>Loading...</em></p>
                  }
                </mat-card-content>
              </mat-card>
            }
          </ng-template>
        </app-dashboard-grid>

        @if (d.expiresUtc) {
          <p class="expires no-print"><small>This link works until {{ d.expiresUtc }}.</small></p>
        }
      } @else if (errors().length === 0) {
        <p><em>Loading...</em></p>
      }

      @for (message of errors(); track message) {
        <p class="fail" role="alert">{{ message }}</p>
      }
    </main>
  `,
  styles: `
    /* The background picture is a layer behind the whole page, so the page makes its own stacking context. */
    :host { display: block; min-height: 100vh; position: relative; isolation: isolate; background: var(--mat-sys-surface-container-lowest, #fff); }
    .bar { display: flex; align-items: center; gap: 0.5rem; padding: 0.6rem 1.5rem; background: #05111f; color: #fff; }
    .brand { font: var(--mat-sys-title-medium); }
    .bar .note { margin-left: auto; opacity: 0.7; font-size: 0.85rem; }
    .page { padding: 1.25rem 1.5rem 2rem; max-width: 96rem; margin: 0 auto; }
    .title { display: flex; align-items: flex-start; justify-content: space-between; gap: 1rem; }
    h1 { font: var(--mat-sys-headline-small); margin: 0; }
    .desc { margin: 0.25rem 0 0; color: var(--mat-sys-on-surface-variant); }
    .filters { display: flex; flex-wrap: wrap; gap: 0.75rem; align-items: flex-start; margin: 1rem 0; }
    .apply { margin-top: 0.4rem; }
    .widget { height: 100%; }
    .widget mat-card-title { font: var(--mat-sys-title-medium); }
    .widget mat-card-header { align-items: center; }
    .widget.picture, .widget.words { overflow: hidden; padding: 0; }
    .words-text { display: block; height: 100%; overflow: auto; padding: 0.75rem 1rem; box-sizing: border-box; }
    .widget-actions { margin-left: auto; display: flex; }
    .widget mat-card-content { flex: 1; min-height: 0; overflow: hidden; display: flex; flex-direction: column; padding-bottom: 0.5rem; }
    .widget app-chart-view { flex: 1; min-height: 0; }
    .truncated { margin: 0.25rem 0 0; }
    .expires { margin-top: 1rem; }
    app-dashboard-grid { display: block; margin-top: 1rem; }
  `,
})
export class SharedDashboardComponent implements OnInit {
  private readonly api = inject(SharedDashboardsService);
  private readonly route = inject(ActivatedRoute);
  private readonly fb = inject(FormBuilder);

  protected readonly dashboard = signal<SharedDashboard | null>(null);
  protected readonly run = signal<DashboardRun | null>(null);
  protected readonly errors = signal<string[]>([]);
  protected readonly busy = signal(false);

  protected filterForm: FormGroup<Record<string, FormControl<string>>> = this.fb.nonNullable.group({});

  protected readonly items = computed(() => arrange(this.dashboard()?.widgets ?? []));
  private readonly charts = computed(() => new Map((this.dashboard()?.charts ?? []).map((c) => [c.id, c])));
  protected readonly labels = computed(() => {
    const names: Record<string, string> = {};
    for (const w of this.dashboard()?.widgets ?? []) names[w.id] = w.kind === 'Image' ? w.alt || 'picture' : w.kind === 'Text' ? 'text' : (this.charts().get(w.chartId)?.name ?? 'chart');
    return names;
  });

  private token = '';

  /** Gets the picture behind the dashboard; the layer calls it again whenever the version changes. */
  protected loadBackground(version: string): () => Observable<Blob> {
    return () => this.api.backgroundImage(this.token, version);
  }
  private request: Subscription | undefined;

  constructor() {
    inject(DestroyRef).onDestroy(() => this.request?.unsubscribe());
  }

  ngOnInit(): void {
    this.token = this.route.snapshot.paramMap.get('token') ?? '';
    this.api.get(this.token).subscribe({
      next: (d) => {
        this.filterForm = this.fb.nonNullable.group(Object.fromEntries(d.filters.map((f) => [f.id, this.fb.nonNullable.control('')])));
        this.dashboard.set(d);
        this.execute();
      },
      error: (e) => this.errors.set(problemMessages(e, 'Could not open this dashboard.')),
    });
  }

  protected operatorLabel(operator: string): string {
    return SHARED_FILTER_OPERATORS.find((o) => o.value === operator)?.label ?? operator;
  }

  /** The widget, if it is some text. */
  protected textOf(widgetId: string): DashboardWidget | undefined {
    const widget = this.dashboard()?.widgets.find((w) => w.id === widgetId);
    return widget?.kind === 'Text' ? widget : undefined;
  }

  /** The widget, if it is a picture. */
  protected imageOf(widgetId: string): DashboardWidget | undefined {
    const widget = this.dashboard()?.widgets.find((w) => w.id === widgetId);
    return widget?.kind === 'Image' ? widget : undefined;
  }

  /** Gets the picture of a picture widget; the widget calls it again whenever the picture's version changes. */
  protected loadWidgetImage(widget: DashboardWidget): () => Observable<Blob> {
    return () => this.api.widgetImage(this.token, widget.id, widget.imageKey ?? '');
  }

  protected chartOf(widgetId: string): SharedChart | undefined {
    const widget = this.dashboard()?.widgets.find((w) => w.id === widgetId);
    return widget ? this.charts().get(widget.chartId) : undefined;
  }

  protected resultOf(widgetId: string): WidgetResult | undefined {
    return this.run()?.widgets.find((w) => w.widgetId === widgetId);
  }

  protected age(utc: string): string {
    return ago(utc);
  }

  protected clear(): void {
    this.filterForm.reset();
    this.execute();
  }

  protected print(): void {
    window.print();
  }

  protected execute(): void {
    const d = this.dashboard();
    if (!d) return;

    const values: FilterValues = {};
    for (const f of d.filters) {
      const parsed = parseFilterInput(f.operator, this.filterForm.controls[f.id]?.value ?? '');
      if (parsed.length > 0) values[f.id] = parsed;
    }

    this.busy.set(true);
    this.errors.set([]);
    this.request?.unsubscribe(); // a newer answer is the one that counts
    this.request = this.api.run(this.token, values).subscribe({
      next: (r) => {
        this.run.set(r);
        this.busy.set(false);
      },
      error: (e) => {
        this.busy.set(false);
        this.errors.set(problemMessages(e, 'Could not load the data.'));
      },
    });
  }
}
