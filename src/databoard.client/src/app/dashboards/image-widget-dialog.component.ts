import { Component, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { problemMessages } from '../organizations/data-sources.service';
import { AppSelectComponent } from '../shared/app-select.component';
import {
  BACKGROUND_MAX_BYTES, BACKGROUND_TYPES, Dashboard, DashboardWidget, DashboardsService, IMAGE_FITS, ImageFit,
} from './dashboards.service';

export interface ImageWidgetDialogData {
  orgId: string;
  /** The dashboard as it is now (an existing picture is changed by saving the dashboard with the widget changed). */
  dashboard: Dashboard;
  /** The picture widget being changed; missing means a new picture is being added. */
  widget?: DashboardWidget;
  /** The tab a new picture goes on (the first, if missing or unknown). */
  tabId?: string | null;
}

/**
 * Adds a picture to a dashboard as a widget, or changes one: another picture, how it fits, what it shows (for people who
 * cannot see it) and where a click on it goes. Closes with the changed dashboard, or with nothing if cancelled.
 */
@Component({
  selector: 'app-image-widget-dialog',
  imports: [MatDialogModule, MatButtonModule, AppSelectComponent],
  template: `
    <h2 mat-dialog-title>{{ data.widget ? 'Picture' : 'Add a picture' }}</h2>
    <mat-dialog-content>
      <div class="pick">
        <input #file type="file" class="hidden" [accept]="accept" aria-label="Picture file" (change)="choose($any($event.target))" />
        <button mat-stroked-button type="button" (click)="file.click()">{{ data.widget ? 'Choose another picture' : 'Choose a picture' }}</button>
        <span class="chosen">{{ chosen()?.name ?? (data.widget ? 'Keeping the current picture' : 'No picture chosen') }}</span>
      </div>
      <small>PNG, JPEG, GIF or WebP, up to 5 MB.</small>

      <div class="row">
        <span>Fit</span>
        <app-select ariaLabel="How the picture fits" [options]="fits" [value]="fit()" (valueChange)="fit.set($any($event))" />
      </div>
      <label class="row">
        <span>Description</span>
        <input type="text" maxlength="200" aria-label="Picture description" placeholder="What the picture shows"
               [value]="alt()" (input)="alt.set($any($event.target).value)" />
      </label>
      <label class="row">
        <span>Link (optional)</span>
        <input type="url" maxlength="500" aria-label="Picture link" placeholder="https://example.com"
               [value]="linkUrl()" (input)="linkUrl.set($any($event.target).value)" />
      </label>
      @if (linkProblem()) {
        <p class="fail" role="alert">{{ linkProblem() }}</p>
      }

      @for (message of errors(); track message) {
        <p class="fail" role="alert">{{ message }}</p>
      }
    </mat-dialog-content>

    <mat-dialog-actions align="end">
      <button mat-button type="button" mat-dialog-close>Cancel</button>
      <button mat-flat-button type="button" class="save" [disabled]="!canSave() || busy()" (click)="save()">{{ data.widget ? 'Save' : 'Add picture' }}</button>
    </mat-dialog-actions>
  `,
  styles: `
    .hidden { display: none; }
    .pick { display: flex; align-items: center; gap: 0.75rem; flex-wrap: wrap; margin-bottom: 0.25rem; }
    .chosen { color: var(--mat-sys-on-surface-variant); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; max-width: 14rem; }
    .row { display: grid; grid-template-columns: 8.5rem 1fr; align-items: center; gap: 0.5rem; margin: 0.6rem 0; }
    .row input { min-width: 0; padding: 0.35rem 0.45rem; font: inherit; }
  `,
})
export class ImageWidgetDialogComponent {
  protected readonly data = inject<ImageWidgetDialogData>(MAT_DIALOG_DATA);
  private readonly ref = inject<MatDialogRef<ImageWidgetDialogComponent, Dashboard>>(MatDialogRef);
  private readonly api = inject(DashboardsService);

  protected readonly fits = IMAGE_FITS;
  protected readonly accept = BACKGROUND_TYPES.join(',');

  protected readonly chosen = signal<File | null>(null);
  protected readonly fit = signal<ImageFit>(this.data.widget?.imageFit ?? 'contain');
  protected readonly alt = signal(this.data.widget?.alt ?? '');
  protected readonly linkUrl = signal(this.data.widget?.linkUrl ?? '');
  protected readonly busy = signal(false);
  protected readonly errors = signal<string[]>([]);

  /** A link has to be a web address; saying so before sending saves a round trip, and the server checks again. */
  protected readonly linkProblem = computed(() => {
    const value = this.linkUrl().trim();
    if (!value) return '';
    try {
      const url = new URL(value);
      return url.protocol === 'http:' || url.protocol === 'https:' ? '' : 'A link must start with http:// or https://.';
    } catch {
      return 'A link must start with http:// or https://.';
    }
  });

  protected readonly canSave = computed(() => {
    if (this.linkProblem()) return false;
    const widget = this.data.widget;
    if (!widget) return !!this.chosen(); // a new picture needs a file
    return (
      !!this.chosen() ||
      this.fit() !== (widget.imageFit ?? 'contain') ||
      this.alt().trim() !== (widget.alt ?? '') ||
      this.linkUrl().trim() !== (widget.linkUrl ?? '')
    );
  });

  protected choose(input: HTMLInputElement): void {
    const file = input.files?.[0];
    input.value = '';
    this.errors.set([]);
    if (!file) return;
    if (!BACKGROUND_TYPES.includes(file.type)) {
      this.errors.set(['Choose a PNG, JPEG, GIF or WebP picture.']);
      return;
    }
    if (file.size > BACKGROUND_MAX_BYTES) {
      this.errors.set(['The picture can be at most 5 MB.']);
      return;
    }
    this.chosen.set(file);
  }

  protected save(): void {
    if (!this.canSave()) return;
    this.busy.set(true);
    this.errors.set([]);

    const { orgId, dashboard, widget } = this.data;
    const file = this.chosen();
    const settings = { fit: this.fit(), alt: this.alt().trim() || null, linkUrl: this.linkUrl().trim() || null };

    if (!widget) {
      this.api.addImageWidget(orgId, dashboard.id, { file: file!, ...settings, tabId: this.data.tabId }).subscribe({
        next: (saved) => this.ref.close(saved),
        error: (e) => this.failed(e, 'Could not add the picture.'),
      });
      return;
    }

    // Another picture first (the server keeps the widget's place), then the settings with the dashboard.
    const replaced = file ? this.api.replaceImageWidget(orgId, dashboard.id, widget.id, file) : null;
    const settle = (current: Dashboard) => {
      const changed = this.settingsDiffer(current.definition.widgets.find((w) => w.id === widget.id), settings);
      if (!changed) {
        this.ref.close(current);
        return;
      }
      const widgets = current.definition.widgets.map((w) =>
        w.id === widget.id ? { ...w, imageFit: settings.fit, alt: settings.alt, linkUrl: settings.linkUrl } : w,
      );
      this.api.update(orgId, current, { ...current.definition, widgets }).subscribe({
        next: (saved) => this.ref.close(saved),
        error: (e) => this.failed(e, 'Could not save the picture.'),
      });
    };

    if (replaced) {
      replaced.subscribe({ next: settle, error: (e) => this.failed(e, 'Could not replace the picture.') });
    } else {
      settle(dashboard);
    }
  }

  private settingsDiffer(w: DashboardWidget | undefined, s: { fit: ImageFit; alt: string | null; linkUrl: string | null }): boolean {
    return !w || (w.imageFit ?? 'contain') !== s.fit || (w.alt ?? null) !== s.alt || (w.linkUrl ?? null) !== s.linkUrl;
  }

  private failed(e: unknown, fallback: string): void {
    this.busy.set(false);
    this.errors.set(problemMessages(e, fallback));
  }
}
