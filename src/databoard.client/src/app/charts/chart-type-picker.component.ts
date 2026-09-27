import { Component, computed, input, output, signal } from '@angular/core';
import { CHART_CATEGORIES, RECENT_CATEGORY, readRecent, readTab, typesIn, withRecent, writeRecent, writeTab } from './chart-type-categories';
import { CHART_TYPES, ChartType } from './charts.service';

/**
 * Picks the type of a chart. Tabs group the types (All, Recent, Table, Bar Chart, Line Chart...), and the icons of the
 * tab on show are the choices. The tab and the types picked lately are remembered in the browser.
 */
@Component({
  selector: 'app-chart-type-picker',
  template: `
    <div class="tabs" role="tablist" aria-label="Kinds of chart">
      @for (c of categories; track c.id) {
        <button type="button" role="tab" class="tab" [class.active]="tab() === c.id" [attr.aria-selected]="tab() === c.id" (click)="show(c.id)">{{ c.label }}</button>
      }
    </div>

    <div class="types" role="group" aria-label="Chart type">
      @for (t of shown(); track t.value) {
        <button type="button" class="type" [class.selected]="value() === t.value" [attr.aria-pressed]="value() === t.value"
                [attr.aria-label]="t.label" [title]="t.label" (click)="choose(t.value)">
          <svg viewBox="0 0 24 24" width="26" height="26" fill="none" stroke="currentColor" stroke-width="1.8" aria-hidden="true">
            @switch (t.value) {
              @case ('Table') { <rect x="3" y="4" width="18" height="16" rx="1.5" /><path d="M3 10h18M3 15h18M9 4v16" /> }
              @case ('Bar') { <path d="M5 20V11M12 20V5M19 20v-8" stroke-width="3.2" /> }
              @case ('BarHorizontal') { <path d="M4 5h11M4 12h16M4 19h8" stroke-width="3.2" /> }
              @case ('Line') { <path d="M3 17l5-6 4 3 8-9" /><path d="M3 21h18" /> }
              @case ('Area') { <path d="M3 18l5-7 4 3 8-8v12z" fill="currentColor" fill-opacity="0.25" /><path d="M3 18l5-7 4 3 8-8" /> }
              @case ('Pie') { <circle cx="12" cy="12" r="8" /><path d="M12 12V4M12 12l6.5 4.5" /> }
              @case ('Donut') { <circle cx="12" cy="12" r="8" /><circle cx="12" cy="12" r="3.5" /> }
              @case ('Number') { <text x="2.5" y="16" font-size="11" font-weight="700" fill="currentColor" stroke="none">123</text> }
              @case ('Heatmap') {
                <rect x="3" y="3" width="5" height="5" fill="currentColor" fill-opacity="0.85" /><rect x="9.5" y="3" width="5" height="5" fill="currentColor" fill-opacity="0.3" /><rect x="16" y="3" width="5" height="5" fill="currentColor" fill-opacity="0.55" />
                <rect x="3" y="9.5" width="5" height="5" fill="currentColor" fill-opacity="0.4" /><rect x="9.5" y="9.5" width="5" height="5" fill="currentColor" fill-opacity="0.9" /><rect x="16" y="9.5" width="5" height="5" fill="currentColor" fill-opacity="0.2" />
                <rect x="3" y="16" width="5" height="5" fill="currentColor" fill-opacity="0.6" /><rect x="9.5" y="16" width="5" height="5" fill="currentColor" fill-opacity="0.25" /><rect x="16" y="16" width="5" height="5" fill="currentColor" fill-opacity="0.75" />
              }
              @case ('Scatter') { <path d="M3 3v18h18" /><circle cx="9" cy="14" r="2" /><circle cx="13" cy="9" r="3.2" /><circle cx="18" cy="15" r="1.6" /> }
            }
          </svg>
        </button>
      } @empty {
        <p class="none"><em>{{ tab() === recentId ? 'The types you pick show up here.' : 'No types here.' }}</em></p>
      }
    </div>
  `,
  styles: `
    :host { display: block; border-bottom: 1px solid var(--mat-sys-outline-variant); padding-bottom: 0.75rem; }
    .tabs { display: flex; flex-wrap: wrap; gap: 0.1rem 0.25rem; border-bottom: 1px solid var(--mat-sys-outline-variant); margin-bottom: 0.6rem; }
    .tab { border: 0; border-bottom: 2px solid transparent; background: transparent; color: inherit; font: inherit; font-size: 0.9rem; padding: 0.4rem 0.6rem; cursor: pointer; }
    .tab:hover { background: var(--mat-sys-surface-container); }
    .tab.active { color: var(--mat-sys-primary); border-bottom-color: var(--mat-sys-primary); }
    .tab:focus-visible, .type:focus-visible { outline: 2px solid var(--mat-sys-primary); outline-offset: -2px; }
    .types { display: flex; flex-wrap: wrap; gap: 0.35rem; min-height: 2.7rem; align-items: center; }
    .type { width: 2.7rem; height: 2.7rem; display: grid; place-items: center; border: 0; border-radius: 6px; background: var(--mat-sys-surface-container); color: var(--mat-sys-on-surface-variant); cursor: pointer; }
    .type.selected { background: var(--mat-sys-primary); color: var(--mat-sys-on-primary); }
    .none { margin: 0; color: var(--mat-sys-on-surface-variant); font-size: 0.9rem; }
  `,
})
export class ChartTypePickerComponent {
  /** The type the chart has now. */
  readonly value = input.required<ChartType>();
  readonly pick = output<ChartType>();

  protected readonly categories = CHART_CATEGORIES;
  protected readonly recentId = RECENT_CATEGORY;

  protected readonly tab = signal(readTab());
  private readonly recent = signal(readRecent());

  protected readonly shown = computed(() => {
    const wanted = typesIn(this.tab(), this.recent());
    return wanted.map((type) => CHART_TYPES.find((t) => t.value === type)!);
  });

  protected show(id: string): void {
    this.tab.set(id);
    writeTab(id);
  }

  protected choose(type: ChartType): void {
    const recent = withRecent(this.recent(), type);
    this.recent.set(recent);
    writeRecent(recent);
    this.pick.emit(type);
  }
}
