import { NgTemplateOutlet } from '@angular/common';
import { Component, DestroyRef, ElementRef, TemplateRef, computed, contentChild, inject, input, output, signal, viewChild } from '@angular/core';
import { GRID_COLUMNS, LayoutItem, move, readingOrder, resize } from './dashboard-layout';

/** Row height and the space between cells, in pixels. The CSS below uses the same numbers. */
export const ROW_HEIGHT = 80;
export const GAP = 16;

/** Below this width the grid becomes one column and cannot be edited. */
const NARROW = 640;

/**
 * Lays widgets out on the 12-column grid. The page supplies each widget's content as an `ng-template` (it gets the
 * widget's id); in edit mode every widget gets a handle to drag it and a corner to resize it, and both can also be
 * done with the arrow keys (Shift resizes).
 */
@Component({
  selector: 'app-dashboard-grid',
  imports: [NgTemplateOutlet],
  template: `
    <div #grid class="grid" [class.editing]="editing()">
      @for (item of items(); track item.id) {
        <div class="cell" [class.active]="activeId() === item.id"
             [style.--x]="item.x" [style.--y]="item.y" [style.--w]="item.w" [style.--h]="item.h" [style.--order]="order().get(item.id)">
          @if (editing()) {
            <div class="handle" role="button" tabindex="0" aria-roledescription="draggable widget"
                 [attr.aria-label]="'Move or resize ' + labelOf(item.id) + '. Arrow keys move it, Shift with the arrow keys resizes it.'"
                 (pointerdown)="start($event, item, 'move')" (keydown)="key($event, item)">
              <span aria-hidden="true">⠿</span> Drag to move
            </div>
          }
          <div class="content">
            <ng-container [ngTemplateOutlet]="template()" [ngTemplateOutletContext]="{ $implicit: item.id }" />
          </div>
          @if (editing()) {
            <div class="resize" aria-hidden="true" title="Drag to resize" (pointerdown)="start($event, item, 'resize')"></div>
          }
        </div>
      }
    </div>
  `,
  styles: `
    :host { display: block; }
    .grid {
      display: grid;
      grid-template-columns: repeat(12, minmax(0, 1fr));
      grid-auto-rows: 80px;
      gap: 16px;
    }
    .cell {
      position: relative;
      display: flex;
      flex-direction: column;
      min-width: 0;
      min-height: 0;
      grid-column: calc(var(--x) + 1) / span var(--w);
      grid-row: calc(var(--y) + 1) / span var(--h);
    }
    .content { flex: 1; min-height: 0; display: flex; flex-direction: column; }
    .content > * { flex: 1; min-height: 0; }
    .editing .cell { outline: 2px dashed var(--mat-sys-outline-variant); outline-offset: 4px; border-radius: 12px; }
    .editing .cell.active { outline: 2px solid var(--mat-sys-primary); z-index: 2; }
    .handle {
      flex: none; padding: 0.15rem 0.6rem; cursor: grab; user-select: none; touch-action: none;
      font: var(--mat-sys-label-medium); color: var(--mat-sys-on-primary-container);
      background: var(--mat-sys-primary-container); border-radius: 8px 8px 0 0;
    }
    .handle:active { cursor: grabbing; }
    .handle:focus-visible { outline: 2px solid var(--mat-sys-primary); outline-offset: 2px; }
    .resize {
      position: absolute; right: 2px; bottom: 2px; width: 1.25rem; height: 1.25rem; cursor: nwse-resize; touch-action: none;
      border-right: 4px solid var(--mat-sys-primary); border-bottom: 4px solid var(--mat-sys-primary); border-radius: 0 0 8px 0;
    }
    /* One column on a narrow screen, in reading order; the layout cannot be edited there. */
    @media (max-width: 640px) {
      .grid { grid-template-columns: minmax(0, 1fr); }
      .cell { grid-column: 1; grid-row: auto / span var(--h); order: var(--order); }
      .handle, .resize { display: none; }
      .editing .cell { outline: none; }
    }
  `,
})
export class DashboardGridComponent {
  readonly items = input.required<LayoutItem[]>();
  readonly editing = input(false);
  /** A name for each widget, used to describe its handle to screen readers. */
  readonly labels = input<Record<string, string>>({});

  /** The layout after a move or a resize. The page decides what to do with it (keep it as a draft, save it). */
  readonly layoutChange = output<LayoutItem[]>();
  /**
   * A change is about to begin: once per drag or resize (when the pointer first moves, so a plain click says nothing) and once
   * per arrow key. The page keeps what was there before it, so one gesture is one step to undo.
   */
  readonly layoutBegin = output<void>();

  protected readonly template = contentChild.required(TemplateRef);
  protected readonly order = computed(() => readingOrder(this.items()));
  protected readonly activeId = signal<string | null>(null);

  private readonly grid = viewChild.required<ElementRef<HTMLElement>>('grid');
  private stopDragging: (() => void) | undefined;

  constructor() {
    inject(DestroyRef).onDestroy(() => this.stopDragging?.());
  }

  protected labelOf(id: string): string {
    return this.labels()[id] || 'widget';
  }

  /** Pixels per column and per row, gaps included: how far the pointer has to travel to change cell. */
  private steps(): { column: number; row: number } | null {
    const width = this.grid().nativeElement.getBoundingClientRect().width;
    if (!(width >= NARROW)) return null;
    return { column: (width - GAP * (GRID_COLUMNS - 1)) / GRID_COLUMNS + GAP, row: ROW_HEIGHT + GAP };
  }

  protected start(event: PointerEvent, item: LayoutItem, mode: 'move' | 'resize'): void {
    const steps = this.steps();
    if (!this.editing() || !steps || event.button !== 0) return;
    event.preventDefault();

    const base = this.items().map((i) => ({ ...i }));
    const from = { x: event.clientX, y: event.clientY };
    this.activeId.set(item.id);

    let begun = false;
    const onMove = (e: PointerEvent): void => {
      if (!begun) {
        begun = true;
        this.layoutBegin.emit();
      }
      const dx = Math.round((e.clientX - from.x) / steps.column);
      const dy = Math.round((e.clientY - from.y) / steps.row);
      this.layoutChange.emit(
        mode === 'move' ? move(base, item.id, item.x + dx, item.y + dy) : resize(base, item.id, item.w + dx, item.h + dy),
      );
    };
    const stop = (): void => {
      document.removeEventListener('pointermove', onMove);
      document.removeEventListener('pointerup', stop);
      document.removeEventListener('pointercancel', stop);
      this.activeId.set(null);
      this.stopDragging = undefined;
    };

    this.stopDragging?.();
    document.addEventListener('pointermove', onMove);
    document.addEventListener('pointerup', stop);
    document.addEventListener('pointercancel', stop);
    this.stopDragging = stop;
  }

  protected key(event: KeyboardEvent, item: LayoutItem): void {
    const step = { ArrowLeft: [-1, 0], ArrowRight: [1, 0], ArrowUp: [0, -1], ArrowDown: [0, 1] }[event.key];
    if (!this.editing() || !step) return;
    event.preventDefault();

    const current = this.items();
    this.layoutBegin.emit();
    this.layoutChange.emit(
      event.shiftKey
        ? resize(current, item.id, item.w + step[0], item.h + step[1])
        : move(current, item.id, item.x + step[0], item.y + step[1]),
    );
  }
}
