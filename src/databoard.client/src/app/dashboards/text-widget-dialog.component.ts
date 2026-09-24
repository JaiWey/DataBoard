import { Component, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { problemMessages } from '../organizations/data-sources.service';
import { MarkdownComponent } from '../shared/markdown.component';
import { arrangeTab, bottomOf, tabOf } from './dashboard-tabs';
import {
  Dashboard, DashboardWidget, DashboardsService, MAX_TEXT_LENGTH, NEW_ID, TEXT_ALIGNS, TEXT_SIZES, TextAlign, TextSize,
} from './dashboards.service';

export interface TextWidgetDialogData {
  orgId: string;
  /** The dashboard as it is now; the text is added to it, or changed in it, by saving it. */
  dashboard: Dashboard;
  /** The text widget being changed; missing means a new one is being added. */
  widget?: DashboardWidget;
  /** The tab a new text goes on (the first, if missing or unknown). */
  tabId?: string | null;
}

/** How tall and wide a new text widget is: the whole width, two rows. */
const NEW_WIDTH = 12;
const NEW_HEIGHT = 2;

/** Writes the text of a text widget, in simple Markdown, with a preview. Closes with the saved dashboard, or with nothing if cancelled. */
@Component({
  selector: 'app-text-widget-dialog',
  imports: [MatDialogModule, MatButtonModule, MarkdownComponent],
  template: `
    <h2 mat-dialog-title>{{ data.widget ? 'Text' : 'Add text' }}</h2>
    <mat-dialog-content>
      <label class="field">
        <span>Text</span>
        <textarea rows="7" aria-label="Text" [maxLength]="max" placeholder="# A title&#10;Some words, **bold**, *italic*, [a link](https://example.com)"
                  [value]="text()" (input)="text.set($any($event.target).value)"></textarea>
      </label>
      <small class="count">{{ text().length }} / {{ max }}. Headings start with #, lists with - or 1., links are [words](https://address).</small>

      <div class="options">
        <label class="row"><span>Align</span>
          <select aria-label="Text alignment" (change)="align.set($any($event.target).value)">
            @for (a of aligns; track a.value) { <option [value]="a.value" [selected]="a.value === align()">{{ a.label }}</option> }
          </select></label>
        <label class="row"><span>Size</span>
          <select aria-label="Text size" (change)="size.set($any($event.target).value)">
            @for (s of sizes; track s.value) { <option [value]="s.value" [selected]="s.value === size()">{{ s.label }}</option> }
          </select></label>
      </div>

      <div class="preview" aria-label="Preview">
        @if (text().trim()) {
          <app-markdown [text]="text()" [align]="align()" [size]="size()" />
        } @else {
          <em>Nothing to show yet.</em>
        }
      </div>

      @for (message of errors(); track message) {
        <p class="fail" role="alert">{{ message }}</p>
      }
    </mat-dialog-content>

    <mat-dialog-actions align="end">
      <button mat-button type="button" mat-dialog-close>Cancel</button>
      <button mat-flat-button type="button" class="save" [disabled]="!canSave() || busy()" (click)="save()">{{ data.widget ? 'Save' : 'Add text' }}</button>
    </mat-dialog-actions>
  `,
  styles: `
    .field { display: grid; gap: 0.25rem; }
    textarea { width: 100%; box-sizing: border-box; padding: 0.5rem; font: inherit; resize: vertical; }
    .count { display: block; margin: 0.25rem 0 0.5rem; color: var(--mat-sys-on-surface-variant); }
    .options { display: flex; gap: 1rem; flex-wrap: wrap; }
    .row { display: flex; align-items: center; gap: 0.5rem; }
    .row select { padding: 0.3rem 0.4rem; font: inherit; }
    .preview { margin-top: 0.75rem; padding: 0.75rem; min-height: 3rem; border: 1px dashed var(--mat-sys-outline-variant); border-radius: 0.5rem; }
  `,
})
export class TextWidgetDialogComponent {
  protected readonly data = inject<TextWidgetDialogData>(MAT_DIALOG_DATA);
  private readonly ref = inject<MatDialogRef<TextWidgetDialogComponent, Dashboard>>(MatDialogRef);
  private readonly api = inject(DashboardsService);

  protected readonly max = MAX_TEXT_LENGTH;
  protected readonly aligns = TEXT_ALIGNS;
  protected readonly sizes = TEXT_SIZES;

  protected readonly text = signal(this.data.widget?.text ?? '');
  protected readonly align = signal<TextAlign>(this.data.widget?.textAlign ?? 'left');
  protected readonly size = signal<TextSize>(this.data.widget?.textSize ?? 'normal');
  protected readonly busy = signal(false);
  protected readonly errors = signal<string[]>([]);

  protected readonly canSave = computed(() => {
    const text = this.text().trim();
    if (!text || text.length > MAX_TEXT_LENGTH) return false;
    const widget = this.data.widget;
    return !widget || text !== (widget.text ?? '') || this.align() !== (widget.textAlign ?? 'left') || this.size() !== (widget.textSize ?? 'normal');
  });

  protected save(): void {
    if (!this.canSave()) return;
    this.busy.set(true);
    this.errors.set([]);

    const { orgId, dashboard, widget } = this.data;
    const words = { text: this.text().trim(), textAlign: this.align(), textSize: this.size() };
    const widgets: DashboardWidget[] = widget
      ? dashboard.definition.widgets.map((w) => (w.id === widget.id ? { ...w, ...words } : w))
      : [...dashboard.definition.widgets, this.newWidget(dashboard, words)];

    this.api.update(orgId, dashboard, { ...dashboard.definition, widgets }).subscribe({
      next: (saved) => this.ref.close(saved),
      error: (e) => {
        this.busy.set(false);
        this.errors.set(problemMessages(e, 'Could not save the text.'));
      },
    });
  }

  /** A new text widget goes below everything else on its tab, across the whole width. */
  private newWidget(dashboard: Dashboard, words: { text: string; textAlign: TextAlign; textSize: TextSize }): DashboardWidget {
    const tabs = dashboard.definition.tabs ?? [];
    const tab = tabOf({ tabId: this.data.tabId }, tabs);
    const bottom = bottomOf(arrangeTab(dashboard.definition.widgets, tabs, tab));
    return {
      id: NEW_ID, chartId: NEW_ID, kind: 'Text', ...words, ...(tab ? { tabId: tab } : {}),
      layout: { x: 0, y: bottom, w: NEW_WIDTH, h: NEW_HEIGHT },
    };
  }
}
