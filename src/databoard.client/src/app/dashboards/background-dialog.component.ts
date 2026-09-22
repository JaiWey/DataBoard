import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { problemMessages } from '../organizations/data-sources.service';
import {
  BACKGROUND_FITS, BACKGROUND_MAX_BYTES, BACKGROUND_TYPES, BackgroundFit, Dashboard, DashboardBackground, DashboardsService,
} from './dashboards.service';

export interface BackgroundDialogData {
  orgId: string;
  dashboardId: string;
  dashboardName: string;
  /** What the dashboard has now; null when it has no picture. */
  background: DashboardBackground | null;
}

/**
 * Chooses the picture behind a dashboard and how it is shown. Closes with the changed dashboard, or with nothing if
 * cancelled. The picture is kept on the server; only editors and owners get here.
 */
@Component({
  selector: 'app-background-dialog',
  imports: [MatDialogModule, MatButtonModule],
  template: `
    <h2 mat-dialog-title>Background of "{{ data.dashboardName }}"</h2>
    <mat-dialog-content>
      <div class="preview" [class.empty]="!preview()">
        @if (preview(); as url) {
          <div class="picture" [style.background-image]="'url(' + url + ')'" [style.background-size]="size()" [style.background-repeat]="repeat()"
               [style.opacity]="opacity() / 100" role="img" aria-label="Preview of the background"></div>
        } @else {
          <span>No picture</span>
        }
      </div>

      <div class="pick">
        <input #file type="file" class="hidden" [accept]="accept" aria-label="Background picture" (change)="choose($any($event.target))" />
        <button mat-stroked-button type="button" (click)="file.click()">{{ hasPicture() ? 'Choose another picture' : 'Choose a picture' }}</button>
        <small>PNG, JPEG, GIF or WebP, up to 5 MB.</small>
      </div>

      <label class="row">
        <span>Show it</span>
        <select aria-label="How the picture is shown" [disabled]="!hasPicture()" (change)="fit.set($any($event.target).value)">
          @for (f of fits; track f.value) { <option [value]="f.value" [selected]="f.value === fit()">{{ f.label }}</option> }
        </select>
      </label>
      <label class="row">
        <span>Strength ({{ opacity() }}%)</span>
        <input type="range" min="10" max="100" step="5" aria-label="Picture strength" [disabled]="!hasPicture()"
               [value]="opacity()" (input)="opacity.set(+$any($event.target).value)" />
      </label>
      <p class="hint">A fainter picture keeps the charts easy to read. Charts sit on top of it; the picture is not printed.</p>

      @for (message of errors(); track message) {
        <p class="fail" role="alert">{{ message }}</p>
      }
    </mat-dialog-content>

    <mat-dialog-actions align="end">
      @if (data.background && !chosen()) {
        <button mat-button type="button" class="remove" [disabled]="busy()" (click)="remove()">Remove picture</button>
      }
      <span class="spacer"></span>
      <button mat-button type="button" mat-dialog-close>Cancel</button>
      <button mat-flat-button type="button" class="save" [disabled]="!canSave() || busy()" (click)="save()">Save</button>
    </mat-dialog-actions>
  `,
  styles: `
    .hidden { display: none; }
    .preview { height: 9rem; border: 1px solid var(--mat-sys-outline-variant); border-radius: 0.5rem; overflow: hidden; position: relative; background: #fff; display: grid; place-items: center; color: var(--mat-sys-on-surface-variant); }
    .picture { position: absolute; inset: 0; background-position: center; }
    .pick { display: flex; align-items: center; gap: 0.75rem; margin: 0.75rem 0; flex-wrap: wrap; }
    .row { display: grid; grid-template-columns: 9rem 1fr; align-items: center; gap: 0.5rem; margin: 0.4rem 0; }
    .row select { padding: 0.3rem 0.4rem; font: inherit; }
    .hint { margin: 0.5rem 0 0; font-size: 0.8rem; color: var(--mat-sys-on-surface-variant); }
    .spacer { flex: 1; }
    mat-dialog-actions { display: flex; }
  `,
})
export class BackgroundDialogComponent implements OnInit {
  protected readonly data = inject<BackgroundDialogData>(MAT_DIALOG_DATA);
  private readonly ref = inject<MatDialogRef<BackgroundDialogComponent, Dashboard>>(MatDialogRef);
  private readonly api = inject(DashboardsService);

  protected readonly fits = BACKGROUND_FITS;
  protected readonly accept = BACKGROUND_TYPES.join(',');

  /** The picture picked in this dialog, not saved yet. */
  protected readonly chosen = signal<File | null>(null);
  protected readonly fit = signal<BackgroundFit>(this.data.background?.fit ?? 'cover');
  protected readonly opacity = signal(this.data.background?.opacity ?? 100);
  protected readonly preview = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly errors = signal<string[]>([]);

  protected readonly hasPicture = computed(() => !!this.chosen() || !!this.data.background);
  protected readonly canSave = computed(() => {
    if (this.chosen()) return true;
    const current = this.data.background;
    return !!current && (current.fit !== this.fit() || current.opacity !== this.opacity());
  });
  protected readonly size = computed(() => (this.fit() === 'contain' ? 'contain' : this.fit() === 'cover' ? 'cover' : 'auto'));
  protected readonly repeat = computed(() => (this.fit() === 'tile' ? 'repeat' : 'no-repeat'));

  constructor() {
    inject(DestroyRef).onDestroy(() => this.setPreview(null));
  }

  ngOnInit(): void {
    const current = this.data.background;
    if (!current) return;
    this.api.backgroundImage(this.data.orgId, this.data.dashboardId, current.version).subscribe({
      next: (blob) => {
        if (!this.chosen()) this.setPreview(URL.createObjectURL(blob));
      },
      // The picture is only a preview here; the dashboard itself shows what is wrong.
      error: () => undefined,
    });
  }

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
    this.setPreview(URL.createObjectURL(file));
  }

  protected save(): void {
    if (!this.canSave()) return;
    this.busy.set(true);
    this.errors.set([]);
    this.api
      .setBackground(this.data.orgId, this.data.dashboardId, { file: this.chosen(), fit: this.fit(), opacity: this.opacity() })
      .subscribe({
        next: (saved) => this.ref.close(saved),
        error: (e) => this.failed(e, 'Could not save the background.'),
      });
  }

  protected remove(): void {
    this.busy.set(true);
    this.errors.set([]);
    this.api.removeBackground(this.data.orgId, this.data.dashboardId).subscribe({
      next: (saved) => this.ref.close(saved),
      error: (e) => this.failed(e, 'Could not remove the background.'),
    });
  }

  private failed(e: unknown, fallback: string): void {
    this.busy.set(false);
    this.errors.set(problemMessages(e, fallback));
  }

  private setPreview(url: string | null): void {
    const old = this.preview();
    this.preview.set(url);
    if (old) URL.revokeObjectURL(old);
  }
}
