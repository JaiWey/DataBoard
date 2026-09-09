import { Component, DestroyRef, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { RouterLink } from '@angular/router';
import { Subscription } from 'rxjs';
import { ChartViewComponent } from '../charts/chart-view.component';
import { CHART_TYPES, Chart, ChartsService } from '../charts/charts.service';
import { problemMessages } from '../organizations/data-sources.service';
import { ChartResult } from '../organizations/datasets.service';
import { DashboardSummary } from './dashboards.service';

/** A saved chart on the right of the dashboards page: how it looks, where it is used, and Edit and Delete. */
@Component({
  selector: 'app-chart-card',
  imports: [RouterLink, MatCardModule, MatButtonModule, ChartViewComponent],
  template: `
    <mat-card appearance="raised" class="detail">
      <mat-card-header>
        <mat-card-title>{{ chart().name }}</mat-card-title>
        <mat-card-subtitle>{{ typeLabel() }} · {{ datasetName() }}</mat-card-subtitle>
        @if (canEdit()) {
          <span class="actions">
            <a mat-stroked-button [routerLink]="['/organizations', orgId(), 'charts', chart().id]">Edit chart</a>
            <button mat-button type="button" (click)="remove()">Delete chart</button>
          </span>
        }
      </mat-card-header>
      <mat-card-content>
        @if (chart().description) { <p class="desc">{{ chart().description }}</p> }
        <p class="used">
          @if (usedIn().length > 0) { Used in: {{ usedIn().join(', ') }} } @else { Not on any dashboard yet. }
        </p>

        @for (message of errors(); track message) {
          <p class="fail" role="alert">{{ message }}</p>
        }
        @if (result(); as r) {
          <app-chart-view [type]="chart().type" [result]="r" [chartStyle]="chart().definition.style"
                          [dimensions]="chart().definition.query.dimensions?.length ?? 0" [label]="chart().name" />
          @if (r.truncated) { <p><small>Showing the first {{ r.rows.length }} rows.</small></p> }
        } @else if (errors().length === 0) {
          <p><em>Loading...</em></p>
        }
      </mat-card-content>
    </mat-card>
  `,
  styles: `
    :host { display: block; min-width: 0; }
    .detail { --mat-card-elevated-container-color: #fff; height: 100%; }
    mat-card-title { font: var(--mat-sys-title-medium); }
    mat-card-header { align-items: center; flex-wrap: wrap; gap: 0.5rem; }
    .actions { margin-left: auto; display: flex; gap: 0.4rem; }
    .detail > mat-card-content { flex: 1; min-height: 0; overflow: auto; padding-top: 0.75rem; }
    .desc { margin: 0.25rem 0; color: var(--mat-sys-on-surface-variant); }
    .used { margin: 0.25rem 0 1rem; color: var(--mat-sys-on-surface-variant); }
  `,
})
export class ChartCardComponent implements OnInit {
  private readonly api = inject(ChartsService);

  readonly chart = input.required<Chart>();
  readonly orgId = input.required<string>();
  readonly canEdit = input(false);
  readonly datasetNames = input<Record<string, string>>({});
  readonly dashboards = input<DashboardSummary[]>([]);

  readonly deleted = output<string>();

  protected readonly result = signal<ChartResult | null>(null);
  protected readonly errors = signal<string[]>([]);
  protected readonly typeLabel = computed(() => CHART_TYPES.find((t) => t.value === this.chart().type)?.label ?? this.chart().type);
  protected readonly datasetName = computed(() => this.datasetNames()[this.chart().datasetId] ?? 'unknown dataset');
  protected readonly usedIn = computed(() =>
    this.dashboards().filter((d) => d.chartIds?.includes(this.chart().id)).map((d) => d.name),
  );

  private request: Subscription | undefined;

  constructor() {
    inject(DestroyRef).onDestroy(() => this.request?.unsubscribe());
  }

  ngOnInit(): void {
    this.request = this.api.run(this.orgId(), this.chart().id).subscribe({
      next: (r) => this.result.set(r),
      error: (e) => this.errors.set(problemMessages(e, 'Could not run the chart.')),
    });
  }

  protected remove(): void {
    const chart = this.chart();
    const used = this.usedIn();
    const warning = used.length > 0 ? ` It will also be removed from: ${used.join(', ')}.` : '';
    if (!confirm(`Delete the chart "${chart.name}"?${warning}`)) return;
    this.api.remove(this.orgId(), chart.id).subscribe({
      next: () => this.deleted.emit(chart.id),
      error: (e) => this.errors.set(problemMessages(e, 'Could not delete the chart.')),
    });
  }
}
