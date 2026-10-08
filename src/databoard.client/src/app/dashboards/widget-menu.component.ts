import { Component, ElementRef, computed, inject, input, output } from '@angular/core';
import { MatMenuModule } from '@angular/material/menu';
import { RouterLink } from '@angular/router';
import { ResultShape } from '../charts/chart-option';
import { ECHARTS_LOADER } from '../charts/chart.component';
import { ChartType } from '../charts/charts.service';
import { downloadChartPicture, downloadCsv, hasPicture } from '../charts/export';
import { DashboardTab } from './dashboards.service';

/**
 * The three-dot menu of a chart on a dashboard: Download CSV and PNG for anyone, and for editors Move to tab, Edit chart
 * and Remove. It lives inside the element marked `data-export-scope` (the widget), where the picture is taken from.
 */
@Component({
  selector: 'app-widget-menu',
  imports: [MatMenuModule, RouterLink],
  template: `
    @if (hasItems()) {
      <button type="button" class="more" [matMenuTriggerFor]="menu" [attr.aria-label]="'Actions for ' + name()">⋮</button>
      <mat-menu #menu="matMenu">
        @if (result(); as r) {
          <button mat-menu-item type="button" class="export-csv" (click)="csv(r)">Download CSV</button>
          @if (drawn()) {
            <button mat-menu-item type="button" class="export-png" (click)="png()">Download PNG</button>
          }
        }
        @if (canEdit()) {
          @if (others().length > 0) {
            <button mat-menu-item type="button" class="move" [matMenuTriggerFor]="tabsMenu">Move to tab</button>
          }
          <a mat-menu-item class="edit-chart" [routerLink]="['/organizations', orgId(), 'charts', chartId()]">Edit chart</a>
          <button mat-menu-item type="button" class="remove-widget" (click)="remove.emit()">Remove</button>
        }
      </mat-menu>
      <mat-menu #tabsMenu="matMenu">
        @for (tab of others(); track tab.id) {
          <button mat-menu-item type="button" (click)="pick.emit(tab.id)">{{ tab.name }}</button>
        }
      </mat-menu>
    }
  `,
  styles: `
    .more { border: 0; background: none; cursor: pointer; font-size: 1.4rem; line-height: 1; padding: 0.2rem 0.6rem; border-radius: 50%; color: inherit; }
    .more:hover, .more:focus-visible { background: var(--mat-sys-surface-container); }
  `,
})
export class WidgetMenuComponent {
  /** The chart's name: the title of the saved files and the name the button gives to screen readers. */
  readonly name = input.required<string>();
  /** What the chart shows now; without it there is nothing to download. */
  readonly result = input<ResultShape | null>(null);
  readonly type = input<ChartType>('Table');
  readonly canEdit = input(false);
  /** Only for editors, who get Edit chart: the organization and chart it opens. */
  readonly orgId = input('');
  readonly chartId = input('');
  readonly tabs = input<DashboardTab[]>([]);
  /** The tab the widget is on now, which is left out of Move to tab. */
  readonly current = input<string | null>(null);

  readonly pick = output<string>();
  readonly remove = output<void>();

  private readonly element = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly loader = inject(ECHARTS_LOADER);

  protected readonly others = computed(() => this.tabs().filter((t) => t.id !== this.current()));
  protected readonly drawn = computed(() => hasPicture(this.type(), this.result()?.rows.length ?? 0));
  protected readonly hasItems = computed(() => this.canEdit() || this.result() !== null);

  protected csv(result: ResultShape): void {
    downloadCsv(this.name(), result);
  }

  protected png(): Promise<void> {
    return downloadChartPicture(this.element.nativeElement, this.name(), this.loader);
  }
}
