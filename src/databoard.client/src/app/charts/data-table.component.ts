import { Component, computed, effect, input, signal, untracked } from '@angular/core';
import { FontOptions, TableOptions, fontProps } from './chart-option';
import { NumberFormatOptions, formatNumber } from './number-format';
import { Sort, nextSort, pageCount, pageList, sortRows } from './table-rows';

/** The size of the text and the room around it for each table size. */
const SIZES = {
  small: { padding: '0.3rem 0.6rem', fontSize: '0.9rem' },
  medium: { padding: '0.5rem 0.8rem', fontSize: '0.95rem' },
  large: { padding: '0.75rem 1rem', fontSize: '1.05rem' },
};

/**
 * A Table chart: the rows of a result, styled as the chart says (fixed header, borders, row numbers, size, header and
 * row colours and fonts), with a click on a header to sort the rows that are loaded and, if the chart asks for it, pages.
 */
@Component({
  selector: 'app-data-table',
  template: `
    <div class="results" [class.fill]="fill()">
      <table [style.font-size]="size().fontSize" [style.border-collapse]="'collapse'">
        <thead>
          <tr>
            @if (options().rowNumber) {
              <th class="number" [style]="headerStyle()" [style.padding]="size().padding" [style.position]="fixed() ? null : 'static'" [style.border]="border() ? null : 'none'">#</th>
            }
            @for (column of result().columns; track $index) {
              <th [style]="headerStyle()" [style.padding]="size().padding" [style.position]="fixed() ? null : 'static'" [style.border]="border() ? null : 'none'"
                  [attr.aria-sort]="ariaSort($index)">@if (sortable()) {<button type="button" class="sort" [style.justify-content]="justify()" [attr.aria-label]="'Sort by ' + column.name" (click)="sortBy($index)">{{ column.name }}<svg viewBox="0 0 10 14" width="8" height="12" aria-hidden="true" [style.margin-left]="justify() ? '0.4rem' : null"><path d="M5 1L1.5 5.5h7z" [attr.opacity]="arrow($index, 'asc')" fill="currentColor" /><path d="M5 13L1.5 8.5h7z" [attr.opacity]="arrow($index, 'desc')" fill="currentColor" /></svg></button>} @else {<ng-container>{{ column.name }}</ng-container>}</th>
            }
          </tr>
        </thead>
        <tbody [style]="bodyFont()">
          @for (row of pageRows(); track $index; let i = $index) {
            <tr [style.background-color]="rowBackground(offset() + i)" [style.color]="rowColor(offset() + i)">
              @if (options().rowNumber) {
                <td class="number" [style.padding]="size().padding" [style.border]="border() ? null : 'none'">{{ offset() + i + 1 }}</td>
              }
              @for (cell of row; track $index) {
                <td [style.padding]="size().padding" [style.border]="border() ? null : 'none'">{{ show(cell) }}</td>
              }
            </tr>
          }
        </tbody>
      </table>
    </div>

    @if (pages() > 1) {
      <nav class="pager" aria-label="Pages">
        <button type="button" class="step" aria-label="Previous page" [disabled]="page() === 0" (click)="go(page() - 1)">‹</button>
        @for (p of pageNumbers(); track $index) {
          @if (p === null) {
            <span class="gap" aria-hidden="true">…</span>
          } @else {
            <button type="button" class="page" [class.current]="p === page() + 1" [attr.aria-label]="'Page ' + p"
                    [attr.aria-current]="p === page() + 1 ? 'page' : null" (click)="go(p - 1)">{{ p }}</button>
          }
        }
        <button type="button" class="step" aria-label="Next page" [disabled]="page() === pages() - 1" (click)="go(page() + 1)">›</button>
      </nav>
    }
  `,
  styles: `
    :host { display: block; }
    :host(.fill) { height: 100%; display: flex; flex-direction: column; min-height: 0; }
    .results.fill { flex: 1; min-height: 0; height: auto; max-height: none; }
    .number { text-align: right; color: var(--mat-sys-on-surface-variant); }
    .sort { all: unset; cursor: pointer; display: inline-flex; align-items: center; gap: 0.4rem; width: 100%; box-sizing: border-box; }
    .sort svg { margin-left: auto; flex: none; }
    .sort:focus-visible { outline: 2px solid var(--mat-sys-primary); outline-offset: 2px; }
    .pager { display: flex; justify-content: flex-end; align-items: center; gap: 0.25rem; padding: 0.5rem 0.25rem 0; flex: none; }
    .pager button { min-width: 1.8rem; height: 1.8rem; border: 1px solid transparent; border-radius: 0.3rem; background: transparent; color: inherit; font: inherit; cursor: pointer; }
    .pager button:hover:not(:disabled) { background: var(--mat-sys-surface-container); }
    .pager button:disabled { opacity: 0.4; cursor: default; }
    .pager .current { border-color: var(--mat-sys-primary); color: var(--mat-sys-primary); }
    .pager .gap { padding: 0 0.25rem; color: var(--mat-sys-on-surface-variant); }
  `,
  host: { '[class.fill]': 'fill()' },
})
export class DataTableComponent {
  readonly result = input.required<{ columns: { name: string }[]; rows: unknown[][] }>();
  readonly tableStyle = input<TableOptions | null | undefined>(null);
  readonly numberFormat = input<NumberFormatOptions | null | undefined>(null);
  /** Take the height of the parent: the rows scroll inside it. */
  readonly fill = input(false);

  protected readonly options = computed<TableOptions>(() => this.tableStyle() ?? {});
  protected readonly fixed = computed(() => this.options().fixedHeader !== false);
  protected readonly border = computed(() => this.options().border !== false);
  protected readonly sortable = computed(() => this.options().sortable !== false);
  protected readonly size = computed(() => SIZES[this.options().size ?? 'small'] ?? SIZES.small);

  protected readonly sort = signal<Sort | null>(null);
  protected readonly page = signal(0);

  private readonly sorted = computed(() => sortRows(this.result().rows, this.sort()));
  protected readonly pages = computed(() => pageCount(this.result().rows.length, this.options().pageSize));
  protected readonly offset = computed(() => (this.options().pageSize ? this.page() * this.options().pageSize! : 0));
  protected readonly pageRows = computed(() => {
    const size = this.options().pageSize;
    return size ? this.sorted().slice(this.offset(), this.offset() + size) : this.sorted();
  });
  protected readonly pageNumbers = computed(() => pageList(this.page() + 1, this.pages()));

  constructor() {
    // Other rows are another table: back to the first page, in the order they came.
    effect(() => {
      this.result();
      untracked(() => {
        this.sort.set(null);
        this.page.set(0);
      });
    });
    // Fewer pages than before (the page size grew): stay on a page that exists.
    effect(() => {
      const last = this.pages() - 1;
      untracked(() => {
        if (this.page() > last) this.page.set(Math.max(0, last));
      });
    });
  }

  protected readonly headerStyle = computed(() => {
    const o = this.options();
    const font = fontProps(o.headerFont);
    return {
      ...(o.headerBackground ? { 'background-color': o.headerBackground } : {}),
      ...(font['fontFamily'] ? { 'font-family': font['fontFamily'] as string } : {}),
      ...(font['fontSize'] ? { 'font-size': `${font['fontSize']}px` } : {}),
      ...(font['fontWeight'] ? { 'font-weight': font['fontWeight'] as string } : {}),
      ...(font['fontStyle'] ? { 'font-style': font['fontStyle'] as string } : {}),
      ...(font['color'] ? { color: font['color'] as string } : {}),
      'text-align': o.headerAlign ?? 'left',
    };
  });

  protected readonly bodyFont = computed(() => cssFont(this.options().bodyFont));

  /** Where a header's text and arrow sit in its button: at the start (the arrow at the far end), or together in the middle or at the end. */
  protected readonly justify = computed(() => ({ left: null, center: 'center', right: 'flex-end' })[this.options().headerAlign ?? 'left']);

  protected rowBackground(index: number): string | null {
    const o = this.options();
    return (index % 2 === 0 ? o.oddRowBackground : o.evenRowBackground) || null; // the 1st row (index 0) is odd
  }

  protected rowColor(index: number): string | null {
    const o = this.options();
    return (index % 2 === 0 ? o.oddRowColor : o.evenRowColor) || null;
  }

  protected show(cell: unknown): string {
    if (cell === null || cell === undefined) return '∅';
    const format = this.numberFormat();
    return typeof cell === 'number' && format ? formatNumber(cell, format) : String(cell);
  }

  protected sortBy(column: number): void {
    this.sort.set(nextSort(this.sort(), column));
    this.page.set(0);
  }

  protected ariaSort(column: number): 'ascending' | 'descending' | null {
    const s = this.sort();
    return s?.column === column ? (s.direction === 'asc' ? 'ascending' : 'descending') : null;
  }

  /** How strong an arrow is: the one that is in force, or both faint. */
  protected arrow(column: number, direction: 'asc' | 'desc'): number {
    const s = this.sort();
    if (s?.column !== column) return 0.3;
    return s.direction === direction ? 1 : 0.15;
  }

  protected go(page: number): void {
    this.page.set(Math.min(Math.max(0, page), this.pages() - 1));
  }
}

/** A font as CSS properties for a style binding. */
function cssFont(font: FontOptions | null | undefined): Record<string, string> {
  const p = fontProps(font);
  return {
    ...(p['fontFamily'] ? { 'font-family': p['fontFamily'] as string } : {}),
    ...(p['fontSize'] ? { 'font-size': `${p['fontSize']}px` } : {}),
    ...(p['fontWeight'] ? { 'font-weight': p['fontWeight'] as string } : {}),
    ...(p['fontStyle'] ? { 'font-style': p['fontStyle'] as string } : {}),
    ...(p['color'] ? { color: p['color'] as string } : {}),
  };
}
