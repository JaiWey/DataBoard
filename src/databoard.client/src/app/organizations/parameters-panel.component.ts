import { Component, effect, input, model } from '@angular/core';
import { AppSelectComponent, SelectOption } from '../shared/app-select.component';
import {
  DatasetParameter,
  PARAMETER_TYPES,
  ParameterType,
  parameterText,
  parametersMissingDefaults,
  parseParameterValue,
  sameParameters,
  syncParameters,
} from './dataset-parameters';

/**
 * The parameters of the SQL being edited, one row for each `{{placeholder}}` in it. The rows follow the SQL: write
 * `{{region}}` and a row appears for it; delete it and the row goes. Each has a type and a default, and the SQL is
 * checked and run with the defaults. Charts and dashboards can choose other values.
 */
@Component({
  selector: 'app-parameters-panel',
  imports: [AppSelectComponent],
  template: `
    @if (parameters().length > 0) {
      <section class="params" aria-label="Parameters">
        <h4>Parameters</h4>
        @for (p of parameters(); track p.name; let i = $index) {
          <div class="row">
            <code class="name">{{ placeholder(p.name) }}</code>
            <app-select [ariaLabel]="'Type of ' + p.name" [options]="typeOptions" [value]="p.type" [disabled]="readOnly()" (valueChange)="setType(i, $any($event))" />
            @switch (p.type) {
              @case ('Boolean') {
                <app-select [ariaLabel]="'Default of ' + p.name" [options]="booleanOptions" [value]="booleanValue(p.default)" [disabled]="readOnly()"
                            (valueChange)="setDefault(i, $event)" />
              }
              @case ('Number') {
                <input type="number" step="any" placeholder="default" [attr.aria-label]="'Default of ' + p.name" [disabled]="readOnly()"
                       [value]="text(p.default)" (input)="setDefault(i, $any($event.target).value)" />
              }
              @case ('Date') {
                <input type="date" [attr.aria-label]="'Default of ' + p.name" [disabled]="readOnly()"
                       [value]="text(p.default)" (input)="setDefault(i, $any($event.target).value)" />
              }
              @default {
                <input type="text" placeholder="default" [attr.aria-label]="'Default of ' + p.name" [disabled]="readOnly()"
                       [value]="text(p.default)" (input)="setDefault(i, $any($event.target).value)" />
              }
            }
            <input type="text" class="label" placeholder="label (optional)" [attr.aria-label]="'Label of ' + p.name" [disabled]="readOnly()"
                   [value]="p.label ?? ''" (input)="setLabel(i, $any($event.target).value)" />
          </div>
        }
        @if (missing().length > 0) {
          <p class="warn" role="status">
            Give {{ missing().join(', ') }} a default. The SQL is checked and run with the defaults.
          </p>
        }
      </section>
    }
  `,
  styles: `
    :host { display: block; }
    .params { margin: 0.5rem 0; padding: 0.5rem 0.75rem; border: 1px solid var(--mat-sys-outline-variant); border-radius: 8px; }
    h4 { margin: 0 0 0.4rem; font: var(--mat-sys-title-small); }
    .row { display: flex; flex-wrap: wrap; align-items: center; gap: 0.5rem; padding: 0.2rem 0; }
    .name { min-width: 8rem; padding: 0.1rem 0.4rem; background: var(--mat-sys-surface-container); border-radius: 4px; }
    .row input[type='text'], .row input[type='number'], .row input[type='date'] { padding: 0.25rem; }
    .label { flex: 1; min-width: 8rem; }
    .warn { margin: 0.4rem 0 0; color: var(--mat-sys-error); }
  `,
})
export class ParametersPanelComponent {
  /** The SQL the parameters belong to. */
  readonly sql = input.required<string>();
  /** The parameters, kept in step with the SQL's placeholders. */
  readonly parameters = model<DatasetParameter[]>([]);
  readonly readOnly = input(false);

  protected readonly types = PARAMETER_TYPES;
  protected readonly typeOptions: SelectOption[] = PARAMETER_TYPES.map((t) => ({ value: t, label: t }));
  protected readonly booleanOptions: SelectOption[] = [
    { value: '', label: 'no default' },
    { value: 'true', label: 'true' },
    { value: 'false', label: 'false' },
  ];

  protected booleanValue(value: unknown): string {
    return value === true ? 'true' : value === false ? 'false' : '';
  }
  protected readonly text = parameterText;

  constructor() {
    effect(() => {
      if (this.readOnly()) return;
      const current = this.parameters();
      const next = syncParameters(this.sql(), current);
      if (!sameParameters(next, current)) this.parameters.set(next);
    });
  }

  /** How the parameter is written in the SQL. */
  protected placeholder(name: string): string {
    return `{{${name}}}`;
  }

  protected missing(): string[] {
    return parametersMissingDefaults(this.parameters());
  }

  private change(index: number, changes: Partial<DatasetParameter>): void {
    this.parameters.update((list) => list.map((p, i) => (i === index ? { ...p, ...changes } : p)));
  }

  /** A default that suited the old type usually does not suit the new one, so it starts again. */
  protected setType(index: number, type: ParameterType): void {
    this.change(index, { type, default: null });
  }

  protected setDefault(index: number, text: string): void {
    this.change(index, { default: parseParameterValue(this.parameters()[index].type, text) });
  }

  protected setLabel(index: number, label: string): void {
    this.change(index, { label: label.trim() === '' ? null : label });
  }
}
