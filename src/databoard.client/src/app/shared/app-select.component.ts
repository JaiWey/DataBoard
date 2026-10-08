import { Component, effect, forwardRef, input, output, signal, viewChild } from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelect, MatSelectModule } from '@angular/material/select';

/** One choice of a select. The value is text, as it was for a native select, so the handlers did not have to change. */
export interface SelectOption {
  value: string;
  label: string;
  disabled?: boolean;
}

/**
 * A drop-down list: Angular Material's select in a compact outlined field. It works both ways a native select did:
 * bound to a value (`[value]` and `(valueChange)`), or to a form control (`formControlName`).
 *
 * `ariaLabel` names it for screen readers (and for tests); `label` is the visible caption that floats on the field, for
 * the ones that have one; `hint` is the line of help under it. Without a `label` the field is as high as a button.
 */
@Component({
  selector: 'app-select',
  imports: [MatFormFieldModule, MatSelectModule],
  providers: [{ provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(() => AppSelectComponent), multi: true }],
  template: `
    <mat-form-field appearance="outline" subscriptSizing="dynamic" [class.compact]="!label()" [class.with-hint]="!!hint()">
      @if (label()) { <mat-label>{{ label() }}</mat-label> }
      <mat-select [value]="current()" [disabled]="isDisabled()" [aria-label]="ariaLabel()" [attr.data-value]="current()"
                  (selectionChange)="choose($event.value)" (closed)="touched()">
        @for (option of options(); track option.value) {
          <mat-option [value]="option.value" [disabled]="option.disabled ?? false" [attr.data-value]="option.value">{{ option.label }}</mat-option>
        }
      </mat-select>
      @if (hint()) { <mat-hint>{{ hint() }}</mat-hint> }
    </mat-form-field>
  `,
  styles: `
    :host { display: inline-block; min-width: 0; max-width: 100%; vertical-align: middle; }
    mat-form-field { width: 100%; }
    /* No caption: as high as a button, so it sits in a row of them or in a chip. */
    .compact { --mat-form-field-container-height: 36px; --mat-form-field-container-vertical-padding: 6px; font-size: 0.875rem; }
  `,
})
export class AppSelectComponent implements ControlValueAccessor {
  readonly options = input.required<SelectOption[]>();
  /** The value shown, when it is bound; with a form control the control's value is shown. */
  readonly value = input<string | null | undefined>(undefined);
  readonly ariaLabel = input<string>('');
  readonly label = input<string>('');
  readonly hint = input<string>('');
  readonly disabled = input(false);

  /** The text of the choice that was made. */
  readonly valueChange = output<string>();

  private readonly select = viewChild.required(MatSelect);
  protected readonly current = signal('');
  private readonly formDisabled = signal(false);
  protected readonly isDisabled = () => this.disabled() || this.formDisabled();
  private onChange: (value: string) => void = () => undefined;
  private onTouched: () => void = () => undefined;

  constructor() {
    effect(() => {
      const value = this.value();
      if (value !== undefined) this.current.set(value ?? '');
    });
  }

  protected choose(value: string): void {
    this.onChange(value);
    const bound = this.value();
    if (bound === undefined) {
      this.current.set(value); // with a form control, what was chosen is what is shown
    } else {
      // A bound value belongs to the parent: it keeps showing the old one unless the parent accepts the new one (it may ask
      // first, or fail), and the parent's answer arrives with the next render.
      this.select().value = bound ?? '';
    }
    this.valueChange.emit(value);
  }

  protected touched(): void {
    this.onTouched();
  }

  writeValue(value: string | null): void {
    this.current.set(value ?? '');
  }

  registerOnChange(fn: (value: string) => void): void {
    this.onChange = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  setDisabledState(disabled: boolean): void {
    this.formDisabled.set(disabled);
  }
}
