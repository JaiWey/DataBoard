import { Component, DestroyRef, computed, effect, inject, input, model, signal, untracked } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { Subscription } from 'rxjs';
import { AppSelectComponent, SelectOption } from '../shared/app-select.component';
import { SqlResultsComponent } from '../sql-editor/sql-results.component';
import { DataSourcesService, MAX_PREVIEW_ROWS, QueryPreview, TableSchema, problemMessages } from './data-sources.service';
import { DatasetsService, JoinType, TableViewDefinition, TableViewTable } from './datasets.service';
import { FieldsDialogComponent, FieldsDialogData } from './fields-dialog.component';

interface Step {
  /** 0 is the main table; k is the k-th join. */
  index: number;
  table: TableViewTable;
}

interface SchemaTable extends TableSchema {
  schema: string;
}

const MAX_JOINS = 10;
const MAX_CONDITIONS = 5;

/**
 * The guided way to build a dataset without writing SQL: pick a main table and its fields, then add table joins one
 * step at a time (join type, related table, fields, and how the tables relate). Any step can be run to see its result.
 */
@Component({
  selector: 'app-table-view-builder',
  imports: [MatCardModule, MatButtonModule, MatFormFieldModule, MatInputModule, AppSelectComponent, SqlResultsComponent],
  template: `
    @if (loadError(); as error) {
      <p class="fail" role="alert">{{ error }}</p>
    }

    @for (step of steps(); track step.index) {
      <mat-card appearance="outlined" class="step">
        <mat-card-header>
          <mat-card-title>Step {{ step.index + 1 }} · {{ step.index === 0 ? 'Main table' : 'Join table' }}</mat-card-title>
          @if (step.index > 0 && step.index === joins().length) {
            <button mat-button type="button" class="remove" [attr.aria-label]="'Remove step ' + (step.index + 1)" (click)="removeLastJoin()">Remove</button>
          }
        </mat-card-header>
        <mat-card-content>
          <div class="form-row">
            @if (step.index > 0) {
              <app-select class="type" label="Join" [ariaLabel]="'Join type of step ' + (step.index + 1)" [options]="joinTypes"
                          [value]="joins()[step.index - 1].type" (valueChange)="setJoinType(step.index, $event)" />
            }
            <app-select class="table" label="Table" [ariaLabel]="'Table of step ' + (step.index + 1)" [options]="tableOptions()"
                        [value]="tableValue(step.table)" (valueChange)="setTable(step.index, $event)" />
            <button mat-stroked-button type="button" class="fields" [disabled]="selectedIndex(step.table) < 0"
                    [attr.aria-label]="'Choose fields of step ' + (step.index + 1)" (click)="chooseFields(step.index)">
              Fields ({{ step.table.fields.length }})
            </button>
          </div>

          @if (step.table.fields.length > 0) {
            <p class="chips">
              @for (field of step.table.fields; track field) { <span class="chip">{{ field }}</span> }
            </p>
          }

          @if (step.index > 0) {
            <div class="relations">
              <p class="label">How does this table relate to the earlier ones?</p>
              @for (condition of joins()[step.index - 1].on; track $index) {
                <div class="form-row condition">
                  <app-select label="Earlier field" [ariaLabel]="'Earlier field of condition ' + ($index + 1) + ' in step ' + (step.index + 1)"
                              [options]="leftChoices(step.index)" [value]="condition.leftField ? condition.leftStep + ':' + condition.leftField : ''"
                              (valueChange)="setLeft(step.index, $index, $event)" />
                  <span class="equals" aria-hidden="true">=</span>
                  <app-select label="This table's field" [ariaLabel]="'Field of this table in condition ' + ($index + 1) + ' in step ' + (step.index + 1)"
                              [options]="rightChoices(step.table)" [value]="condition.rightField" (valueChange)="setRight(step.index, $index, $event)" />
                  @if (joins()[step.index - 1].on.length > 1) {
                    <button mat-button type="button" [attr.aria-label]="'Remove condition ' + ($index + 1) + ' of step ' + (step.index + 1)"
                            (click)="removeCondition(step.index, $index)">Remove</button>
                  }
                </div>
              }
              <button mat-button type="button" [disabled]="joins()[step.index - 1].on.length >= maxConditions"
                      [attr.aria-label]="'Add condition to step ' + (step.index + 1)" (click)="addCondition(step.index)">Add condition</button>
            </div>
          }

          <p class="step-actions">
            <button mat-stroked-button type="button" [disabled]="!canRun(step.index) || running()"
                    [attr.aria-label]="'Run step ' + (step.index + 1)" (click)="run(step.index)">Run this step</button>
          </p>
        </mat-card-content>
      </mat-card>
    }

    <p class="bottom-actions">
      <button mat-stroked-button type="button" class="add-join" [disabled]="!canRun(joins().length) || joins().length >= maxJoins" (click)="addJoin()">Add table join</button>
      <button mat-flat-button type="button" class="run" [disabled]="!canRun(joins().length) || running()" (click)="run(joins().length)">Run</button>
    </p>

    @if (ran() !== null) {
      <h3 class="result-title">Result{{ joins().length > 0 ? ' of step ' + (ran()! + 1) : '' }}</h3>
    }
    <app-sql-results [result]="result()" [errors]="errors()" />
    @if (sql()) {
      <details class="generated"><summary>Generated SQL</summary><pre class="sql">{{ sql() }}</pre></details>
    }
  `,
  styles: `
    :host { display: block; }
    .step { margin-bottom: 0.75rem; }
    mat-card-title { font: var(--mat-sys-title-small); }
    .remove { margin-left: auto; }
    mat-card-header { align-items: center; padding-bottom: 0.5rem; }
    .step mat-card-content { padding-top: 0.75rem; }
    .type { width: 9rem; }
    .table { min-width: 16rem; flex: 1; }
    .fields { margin-top: 0.4rem; }
    .chips { display: flex; flex-wrap: wrap; gap: 0.35rem; margin: 0.25rem 0; }
    .relations { margin-top: 0.5rem; }
    .label { margin: 0.25rem 0; color: var(--mat-sys-on-surface-variant); font: var(--mat-sys-body-small); }
    .condition { align-items: center; }
    .equals { padding: 0 0.25rem 1.2rem; }
    .step-actions { margin: 0.25rem 0 0; }
    .bottom-actions { display: flex; gap: 0.5rem; margin: 0.5rem 0; }
    .result-title { font: var(--mat-sys-title-small); margin: 0.75rem 0 0.25rem; }
    .generated { margin: 0.5rem 0; }
  `,
})
export class TableViewBuilderComponent {
  private readonly sourcesApi = inject(DataSourcesService);
  private readonly datasetsApi = inject(DatasetsService);
  private readonly dialog = inject(MatDialog);

  readonly orgId = input.required<string>();
  readonly dataSourceId = input.required<string>();
  readonly definition = model.required<TableViewDefinition>();

  protected readonly maxJoins = MAX_JOINS;
  protected readonly maxConditions = MAX_CONDITIONS;
  protected readonly joinTypes: { value: JoinType; label: string }[] = [
    { value: 'Inner', label: 'Inner join' },
    { value: 'Left', label: 'Left join' },
    { value: 'Right', label: 'Right join' },
    { value: 'Full', label: 'Full join' },
  ];

  protected readonly tables = signal<SchemaTable[]>([]);
  protected readonly loadError = signal<string | null>(null);
  protected readonly joins = computed(() => this.definition().joins);
  protected readonly steps = computed<Step[]>(() => [
    { index: 0, table: this.definition().main },
    ...this.joins().map((j, i) => ({ index: i + 1, table: j.table })),
  ]);

  protected readonly result = signal<QueryPreview | null>(null);
  protected readonly errors = signal<string[]>([]);
  protected readonly sql = signal<string | null>(null);
  /** Which step the shown result belongs to, or null before anything ran. */
  protected readonly ran = signal<number | null>(null);
  protected readonly running = signal(false);

  private schemaRequest: Subscription | undefined;
  private runRequest: Subscription | undefined;

  constructor() {
    inject(DestroyRef).onDestroy(() => {
      this.schemaRequest?.unsubscribe();
      this.runRequest?.unsubscribe();
    });

    effect(() => {
      const orgId = this.orgId();
      const dataSourceId = this.dataSourceId();
      untracked(() => this.loadSchema(orgId, dataSourceId));
    });
  }

  private loadSchema(orgId: string, dataSourceId: string): void {
    this.schemaRequest?.unsubscribe();
    this.tables.set([]);
    this.loadError.set(null);
    if (!dataSourceId) return;

    this.schemaRequest = this.sourcesApi.schema(orgId, dataSourceId).subscribe({
      next: (schema) =>
        this.tables.set(schema.schemas.flatMap((s) => s.tables.map((t) => ({ ...t, schema: s.name })))),
      error: (e) => this.loadError.set(problemMessages(e, 'Could not load the tables of this data source.')[0]),
    });
  }

  // ---- looking things up --------------------------------------------------------------------------------

  protected tableLabel(table: SchemaTable): string {
    return `${table.schema}.${table.name}${table.kind === 'View' ? ' (view)' : ''}`;
  }

  protected selectedIndex(table: TableViewTable): number {
    return this.tables().findIndex((t) => t.schema === table.schema && t.name === table.name);
  }

  protected columnsOf(table: TableViewTable) {
    return this.tables()[this.selectedIndex(table)]?.columns ?? [];
  }

  protected readonly tableOptions = computed<SelectOption[]>(() => [
    { value: '', label: 'Choose a table…' },
    ...this.tables().map((t, i) => ({ value: String(i), label: this.tableLabel(t) })),
  ]);

  /** The value of the table select for a step: the position of its table in the list, or empty. */
  protected tableValue(table: TableViewTable): string {
    const index = this.selectedIndex(table);
    return index < 0 ? '' : String(index);
  }

  protected leftChoices(step: number): SelectOption[] {
    return [{ value: '', label: 'Choose…' }, ...this.leftOptions(step)];
  }

  protected rightChoices(table: TableViewTable): SelectOption[] {
    return [{ value: '', label: 'Choose…' }, ...this.columnsOf(table).map((c) => ({ value: c.name, label: c.name }))];
  }

  /** Every field of the tables before this step, to relate the new table to. */
  protected leftOptions(step: number): { value: string; label: string }[] {
    const earlier = this.steps().slice(0, step);
    return earlier.flatMap((s) => {
      const repeated = earlier.filter((o) => o.table.schema === s.table.schema && o.table.name === s.table.name).length > 1;
      const name = repeated ? `${s.table.name} (step ${s.index + 1})` : s.table.name;
      return this.columnsOf(s.table).map((c) => ({ value: `${s.index}:${c.name}`, label: `${name}.${c.name}` }));
    });
  }

  /** A step can be run once it and the steps before it are set up and at least one field is chosen. */
  protected canRun(step: number): boolean {
    const steps = this.steps().slice(0, step + 1);
    const tablesChosen = steps.every((s) => this.selectedIndex(s.table) >= 0);
    const related = this.joins()
      .slice(0, step)
      .every((j) => j.on.length > 0 && j.on.every((c) => !!c.leftField && !!c.rightField));
    return tablesChosen && related && steps.some((s) => s.table.fields.length > 0);
  }

  // ---- editing the definition ----------------------------------------------------------------------------

  private change(edit: (draft: TableViewDefinition) => void): void {
    const draft = JSON.parse(JSON.stringify(this.definition())) as TableViewDefinition;
    edit(draft);
    this.definition.set(draft);
  }

  protected setTable(step: number, value: string): void {
    const table = value === '' ? undefined : this.tables()[Number(value)];
    this.change((d) => {
      const target = step === 0 ? d.main : d.joins[step - 1].table;
      target.schema = table?.schema ?? '';
      target.name = table?.name ?? '';
      target.fields = [];
      // Conditions that used the old table's fields no longer mean anything.
      d.joins.forEach((join, i) => {
        if (i + 1 === step) join.on.forEach((c) => (c.rightField = ''));
        join.on.forEach((c) => {
          if (c.leftStep === step) c.leftField = '';
        });
      });
    });
  }

  protected setJoinType(step: number, value: string): void {
    this.change((d) => (d.joins[step - 1].type = value as JoinType));
  }

  protected chooseFields(step: number): void {
    const table = this.steps()[step].table;
    const columns = this.columnsOf(table);
    this.dialog
      .open<FieldsDialogComponent, FieldsDialogData, string[]>(FieldsDialogComponent, {
        data: { table: table.name, columns: columns.map((c) => ({ name: c.name, dataType: c.dataType })), selected: table.fields },
        width: '28rem',
        maxWidth: '95vw',
      })
      .afterClosed()
      .subscribe((fields) => {
        if (fields) this.change((d) => ((step === 0 ? d.main : d.joins[step - 1].table).fields = fields));
      });
  }

  protected addJoin(): void {
    this.change((d) =>
      d.joins.push({
        type: 'Inner',
        table: { schema: '', name: '', fields: [] },
        on: [{ leftStep: 0, leftField: '', rightField: '' }],
      }),
    );
  }

  protected removeLastJoin(): void {
    this.change((d) => d.joins.pop());
    this.clearResult();
  }

  protected addCondition(step: number): void {
    this.change((d) => d.joins[step - 1].on.push({ leftStep: 0, leftField: '', rightField: '' }));
  }

  protected removeCondition(step: number, index: number): void {
    this.change((d) => d.joins[step - 1].on.splice(index, 1));
  }

  protected setLeft(step: number, index: number, value: string): void {
    const colon = value.indexOf(':');
    this.change((d) => {
      const condition = d.joins[step - 1].on[index];
      condition.leftStep = colon < 0 ? 0 : Number(value.slice(0, colon));
      condition.leftField = colon < 0 ? '' : value.slice(colon + 1);
    });
  }

  protected setRight(step: number, index: number, value: string): void {
    this.change((d) => (d.joins[step - 1].on[index].rightField = value));
  }

  // ---- running --------------------------------------------------------------------------------------------

  private clearResult(): void {
    this.result.set(null);
    this.errors.set([]);
    this.sql.set(null);
    this.ran.set(null);
  }

  /** Runs the definition up to a step (0 is the main table alone) and shows its rows below. */
  protected run(step: number): void {
    const full = this.definition();
    const definition: TableViewDefinition = { main: full.main, joins: full.joins.slice(0, step) };

    this.runRequest?.unsubscribe();
    this.clearResult();
    this.running.set(true);
    this.runRequest = this.datasetsApi.previewTableView(this.orgId(), this.dataSourceId(), definition, MAX_PREVIEW_ROWS).subscribe({
      next: (r) => {
        this.running.set(false);
        this.ran.set(step);
        this.result.set(r);
        this.sql.set(r.sql);
      },
      error: (e) => {
        this.running.set(false);
        this.ran.set(step);
        this.errors.set(problemMessages(e, 'The query failed.'));
      },
    });
  }
}
