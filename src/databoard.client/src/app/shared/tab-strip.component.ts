import { Component, input, output } from '@angular/core';

export interface TabItem {
  id: string;
  name: string;
  /** Shows a dot: the tab has changes that closing it would lose. */
  dirty?: boolean;
}

/** A VS Code style row of tabs: the active one is highlighted, each has a close button, and middle-click closes. */
@Component({
  selector: 'app-tab-strip',
  template: `
    <div class="tabstrip" role="tablist" [attr.aria-label]="label()">
      @for (tab of tabs(); track tab.id) {
        <div class="tab" role="presentation" [class.active]="tab.id === activeId()">
          <button type="button" role="tab" class="tab-label" [attr.aria-selected]="tab.id === activeId()"
                  (click)="select.emit(tab.id)" (auxclick)="$event.button === 1 && close.emit(tab.id)">
            {{ tab.name }}@if (tab.dirty) { <span class="dot" title="Unsaved changes" aria-label="Unsaved changes"> ●</span> }
          </button>
          <button type="button" class="tab-close" [attr.aria-label]="'Close ' + tab.name" (click)="close.emit(tab.id)">×</button>
        </div>
      }
    </div>
  `,
  styles: `
    :host { display: block; flex: none; }
    /* A scrolling row; the active tab merges into the pane below. */
    .tabstrip { display: flex; overflow-x: auto; margin-bottom: 0.5rem; border-bottom: 1px solid var(--mat-sys-outline-variant); }
    .tab { display: flex; align-items: center; flex: none; border: 1px solid transparent; border-bottom: 0; border-radius: 6px 6px 0 0;
           color: var(--mat-sys-on-surface-variant); }
    .tab.active { background: #fff; border-color: var(--mat-sys-outline-variant); color: var(--mat-sys-on-surface);
                  box-shadow: inset 0 2px 0 var(--mat-sys-primary); }
    .tab button { border: 0; background: none; color: inherit; font: var(--mat-sys-label-large); cursor: pointer; }
    .tab-label { padding: 0.55rem 0.5rem 0.55rem 0.9rem; max-width: 14rem; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .tab-close { padding: 0 0.6rem; font-size: 1.1rem; line-height: 1; border-radius: 4px; }
    .tab-close:hover { background: var(--mat-sys-surface-container-high); }
    .dot { color: var(--mat-sys-primary); }
  `,
})
export class TabStripComponent {
  readonly tabs = input.required<TabItem[]>();
  readonly activeId = input<string | null>(null);
  readonly label = input('Open tabs');

  readonly select = output<string>();
  readonly close = output<string>();
}
