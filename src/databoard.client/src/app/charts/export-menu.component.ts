import { Component, ElementRef, computed, inject, input } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatMenuModule } from '@angular/material/menu';
import { ResultShape } from './chart-option';
import { ECHARTS_LOADER } from './chart.component';
import { ChartType } from './charts.service';
import { downloadCsv, downloadImage } from './export';

/**
 * "Export" button for a chart: the rows as CSV, and, for charts that are drawn, a PNG picture. It lives inside the
 * element marked `data-export-scope` (the widget or card), and takes the picture from the chart drawn in there.
 */
@Component({
  selector: 'app-export-menu',
  imports: [MatButtonModule, MatMenuModule],
  template: `
    <button mat-button type="button" class="export" [matMenuTriggerFor]="menu" [attr.aria-label]="'Export ' + name()">Export</button>
    <mat-menu #menu="matMenu">
      <button mat-menu-item type="button" class="export-csv" (click)="csv()">Download CSV</button>
      @if (drawn()) {
        <button mat-menu-item type="button" class="export-png" (click)="png()">Download PNG</button>
      }
    </mat-menu>
  `,
})
export class ExportMenuComponent {
  /** The chart's name: the title of the saved files. */
  readonly name = input.required<string>();
  readonly result = input.required<ResultShape>();
  readonly type = input<ChartType>('Table');

  private readonly element = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly loader = inject(ECHARTS_LOADER);

  /** A table, a number and a word cloud are not drawn on a canvas, so there is no picture to take; they have the CSV. */
  protected readonly drawn = computed(() => !['Table', 'Number', 'WordCloud'].includes(this.type()) && this.result().rows.length > 0);

  protected csv(): void {
    downloadCsv(this.name(), this.result());
  }

  protected async png(): Promise<void> {
    const chartElement = this.element.nativeElement.closest('[data-export-scope]')?.querySelector<HTMLElement>('app-chart .chart');
    if (!chartElement) return;

    const instance = (await this.loader()).getInstanceByDom?.(chartElement);
    const picture = instance?.getDataURL?.({ type: 'png', pixelRatio: 2, backgroundColor: '#ffffff' });
    if (picture) downloadImage(this.name(), picture);
  }
}
