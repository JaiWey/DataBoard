import { Component, DestroyRef, ElementRef, InjectionToken, effect, inject, input, viewChild } from '@angular/core';
import { ChartKind, ChartStyleOptions, ResultShape, buildChartOption } from './chart-option';

/** The slice of ECharts this component uses, so tests can stand in for it. */
export interface ChartInstance {
  setOption(option: unknown, notMerge?: boolean): void;
  resize(): void;
  dispose(): void;
}
export interface EchartsApi {
  init(element: HTMLElement): ChartInstance;
}

let loaded: Promise<EchartsApi> | undefined;

/** Loads only the chart types and components in use, on first need, so they stay out of the main bundle. */
async function loadEcharts(): Promise<EchartsApi> {
  loaded ??= (async () => {
    const [core, charts, components, renderers] = await Promise.all([
      import('echarts/core'),
      import('echarts/charts'),
      import('echarts/components'),
      import('echarts/renderers'),
    ]);
    core.use([
      charts.BarChart,
      charts.LineChart,
      charts.PieChart,
      components.GridComponent,
      components.TooltipComponent,
      components.LegendComponent,
      components.AriaComponent,
      renderers.CanvasRenderer,
    ]);
    return core as unknown as EchartsApi;
  })();
  return loaded;
}

export const ECHARTS_LOADER = new InjectionToken<() => Promise<EchartsApi>>('ECHARTS_LOADER', {
  providedIn: 'root',
  factory: () => loadEcharts,
});

@Component({
  selector: 'app-chart',
  template: `<div #host class="chart" role="img" [attr.aria-label]="label()"></div>`,
  styles: `
    :host { display: block; }
    .chart { width: 100%; height: 320px; }
  `,
})
export class ChartComponent {
  readonly kind = input.required<ChartKind>();
  readonly result = input.required<ResultShape>();
  readonly chartStyle = input<ChartStyleOptions>({});
  /** How many leading result columns are dimensions; see `buildChartOption`. Left out, it is worked out from the columns. */
  readonly dimensions = input<number | undefined>(undefined);
  /** Text alternative announced by screen readers. */
  readonly label = input('Chart');

  private readonly host = viewChild.required<ElementRef<HTMLElement>>('host');
  private readonly loader = inject(ECHARTS_LOADER);
  private chart: ChartInstance | undefined;
  private destroyed = false;
  private observer: ResizeObserver | undefined;

  constructor() {
    inject(DestroyRef).onDestroy(() => {
      this.destroyed = true;
      this.observer?.disconnect();
      this.chart?.dispose();
    });

    effect(() => {
      const option = buildChartOption(this.kind(), this.result(), this.chartStyle(), this.dimensions());
      void this.draw(option);
    });
  }

  private async draw(option: unknown): Promise<void> {
    const echarts = await this.loader();
    if (this.destroyed) return;

    if (!this.chart) {
      this.chart = echarts.init(this.host().nativeElement);
      if (typeof ResizeObserver !== 'undefined') {
        this.observer = new ResizeObserver(() => this.chart?.resize());
        this.observer.observe(this.host().nativeElement);
      }
    }
    // notMerge: a new kind of chart must replace the old series, not blend with them.
    this.chart.setOption(option, true);
  }
}
