import { Component, computed, input, output } from '@angular/core';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { AppSelectComponent, SelectOption } from '../shared/app-select.component';
import { FONT_FAMILIES, FontOptions } from './chart-option';

/** A colour with a way back to "not chosen": the colour input cannot be empty, so a Default button clears it. */
@Component({
  selector: 'app-color-field',
  template: `
    <span class="color">
      <input type="color" [attr.aria-label]="label()" [value]="shown()" (input)="changed.emit($any($event.target).value)" />
      @if (value()) {
        <button type="button" class="reset" [attr.aria-label]="'Use the default ' + label().toLowerCase()" (click)="changed.emit(null)">Default</button>
      } @else {
        <span class="hint">default</span>
      }
    </span>
  `,
  styles: `
    .color { display: inline-flex; align-items: center; gap: 0.4rem; }
    input[type=color] { width: 2rem; height: 1.8rem; padding: 0; border: 1px solid var(--mat-sys-outline-variant); border-radius: 0.3rem; background: none; }
    .reset { border: 0; background: transparent; color: var(--mat-sys-primary); cursor: pointer; font: inherit; font-size: 0.8rem; }
    .hint { font-size: 0.8rem; color: var(--mat-sys-on-surface-variant); }
  `,
})
export class ColorFieldComponent {
  readonly label = input.required<string>();
  /** The colour chosen (#rrggbb), or null for the default. */
  readonly value = input<string | null | undefined>(null);
  /** What the colour input shows while nothing is chosen. */
  readonly fallback = input('#495057');
  readonly changed = output<string | null>();

  protected readonly shown = computed(() => this.expand(this.value() || this.fallback()));

  private expand(color: string): string {
    return /^#[0-9a-f]{3}$/i.test(color) ? `#${color[1]}${color[1]}${color[2]}${color[2]}${color[3]}${color[3]}` : color;
  }
}

/**
 * The choices of a font: family, size, bold, italic and colour. It reports the whole font each time, or null when nothing
 * is chosen any more, so a font with no choices is not stored.
 */
@Component({
  selector: 'app-font-fields',
  imports: [MatCheckboxModule, ColorFieldComponent, AppSelectComponent],
  template: `
    <div class="font" role="group" [attr.aria-label]="label()">
      <app-select [ariaLabel]="label() + ' family'" [options]="familyOptions" [value]="font()?.family ?? ''" (valueChange)="change({ family: $event || null })" />
      <input type="number" min="8" max="48" placeholder="size" [attr.aria-label]="label() + ' size'" [value]="font()?.size ?? ''"
             (input)="change({ size: whole($event) })" />
      <mat-checkbox [checked]="font()?.weight === 'bold'" [aria-label]="label() + ' bold'" (change)="change({ weight: $event.checked ? 'bold' : null })">Bold</mat-checkbox>
      <mat-checkbox [checked]="font()?.italic === true" [aria-label]="label() + ' italic'" (change)="change({ italic: $event.checked })">Italic</mat-checkbox>
      <app-color-field [label]="label() + ' colour'" [value]="font()?.color" (changed)="change({ color: $event })" />
    </div>
  `,
  styles: `
    .font { display: flex; flex-wrap: wrap; align-items: center; gap: 0.4rem 0.6rem; }
    input[type=number] { padding: 0.3rem 0.4rem; font: inherit; min-width: 0; }
    input[type=number] { width: 4.5rem; }
  `,
})
export class FontFieldsComponent {
  /** What the font is for ("Label font"), which also names its fields for screen readers. */
  readonly label = input.required<string>();
  readonly font = input<FontOptions | null | undefined>(null);
  readonly changed = output<FontOptions | null>();

  protected readonly familyOptions: SelectOption[] = [{ value: '', label: 'Default font' }, ...FONT_FAMILIES.map((f) => ({ value: f, label: f }))];

  protected change(changes: Partial<FontOptions>): void {
    const next: FontOptions = { ...(this.font() ?? {}), ...changes };
    const empty = !next.family && !next.size && !next.weight && !next.italic && !next.color;
    this.changed.emit(empty ? null : next);
  }

  protected whole(event: Event): number | null {
    const raw = (event.target as HTMLInputElement).value;
    if (raw === '') return null;
    const n = Number(raw);
    return Number.isFinite(n) ? Math.min(48, Math.max(8, Math.round(n))) : null;
  }
}
