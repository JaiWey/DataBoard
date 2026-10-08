import { Component, computed, effect, inject, input, linkedSignal, output, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { SqlEditorComponent } from '../sql-editor/sql-editor.component';
import { SqlResultsComponent } from '../sql-editor/sql-results.component';
import { DataSourcesService, MAX_PREVIEW_ROWS, QueryPreview, problemMessages } from './data-sources.service';
import { TableViewBuilderComponent } from './table-view-builder.component';
import { CACHE_CHOICES, cacheLabel } from '../shared/relative-time';
import { DatasetParameter, parametersMissingDefaults, sameParameters } from './dataset-parameters';
import { ParametersPanelComponent } from './parameters-panel.component';
import { Dataset, DatasetsService, TableViewDefinition, emptyTableView } from './datasets.service';
import { AppSelectComponent, SelectOption } from '../shared/app-select.component';
import { ConfirmService } from '../shared/confirm.service';

/** One open dataset: its SQL editor and the preview/explore panels. Each tab keeps its own state while hidden. */
@Component({
  selector: 'app-dataset-tab',
  imports: [MatCardModule, MatButtonModule, AppSelectComponent, SqlEditorComponent, SqlResultsComponent, TableViewBuilderComponent, ParametersPanelComponent],
  template: `
    <mat-card appearance="raised" class="detail">
      <mat-card-header>
        <mat-card-title>{{ dataset().name }}</mat-card-title>
        <span class="actions">
          <button mat-stroked-button type="button" (click)="show()">Preview</button>
          @if (canEdit()) {
            <button mat-button type="button" (click)="remove()">Delete</button>
          }
        </span>
      </mat-card-header>
      <mat-card-content>
        @if (dataset().description) { <p class="desc">{{ dataset().description }}</p> }

        <p class="chips">
          @for (column of dataset().columns; track column.name) {
            <span class="chip" [class.measure]="column.role === 'Measure'" [title]="column.dataType">
              {{ column.name }} <small>{{ column.role === 'Measure' ? 'measure' : 'dimension' }}</small>
            </span>
          }
        </p>

        @if (canEdit()) {
          <p class="cache">
            <span class="field">
              <span>Keep results for</span>
              <app-select ariaLabel="Cache results for" [options]="cacheOptions()" [value]="'' + cacheSeconds()" [disabled]="cacheBusy()" (valueChange)="setCache($event)" />
            </span>
            <small>Charts on this dataset reuse a result for this long instead of asking the database again.</small>
          </p>
        } @else if (cacheSeconds() > 0) {
          <p class="cache"><small>Results are reused for {{ cacheText() }}, so the data can be a little behind.</small></p>
        }

        @if (isView()) {
          <app-table-view-builder [orgId]="orgId()" [dataSourceId]="dataset().dataSourceId" [(definition)]="viewDefinition" />
          <p class="save-row">
            <button mat-flat-button type="button" class="save" [disabled]="!dirty() || busy()" (click)="saveSql()">Save</button>
            @if (dirty()) { <small>Unsaved changes</small> }
          </p>
          <details class="generated"><summary>Generated SQL</summary><app-sql-editor [value]="dataset().sql ?? ''" [readOnly]="true" label="Generated SQL" /></details>
        } @else if (dataset().sql !== null) {
          <app-sql-editor [value]="sqlText()" [readOnly]="!canEdit()" (valueChange)="sqlText.set($event)" (save)="saveSql()" (run)="testSql()" />
          <app-parameters-panel [sql]="sqlText()" [readOnly]="!canEdit()" [(parameters)]="parameters" />
          @if (canEdit()) {
            <p class="hint"><small>{{ parameterHint }}</small></p>
            <p class="save-row">
              <button mat-flat-button type="button" class="save" [disabled]="!dirty() || busy() || missingDefaults()" (click)="saveSql()">Save</button>
              <button mat-stroked-button type="button" class="test" [disabled]="testing() || !sqlText().trim() || missingDefaults()" (click)="testSql()">Run query</button>
              @if (dirty()) { <small>Unsaved changes</small> }
            </p>
          }
        }
        @for (message of sqlErrors(); track message) {
          <p class="fail" role="alert">{{ message }}</p>
        }
        <app-sql-results [result]="testResult()" [errors]="testErrors()" />

        @if (panel() === 'preview') {
          @if (preview(); as p) {
            <div class="results">
              <table>
                <thead><tr>@for (c of p.columns; track $index) { <th>{{ c.name }}</th> }</tr></thead>
                <tbody>
                  @for (row of p.rows; track $index) {
                    <tr>@for (cell of row; track $index) { <td>{{ cell === null ? '∅' : cell }}</td> }</tr>
                  }
                </tbody>
              </table>
            </div>
            <p><small>{{ p.rows.length }} rows@if (p.truncated) { (more rows exist) }</small></p>
          }
          @for (message of errors(); track message) {
            <p class="fail" role="alert">{{ message }}</p>
          }
        }
      </mat-card-content>
    </mat-card>
  `,
  styles: `
    :host { display: block; min-width: 0; }
    :host(.inactive) { display: none; }
    /* The card fills the tab's pane; only its content scrolls, so a long result never grows the page. */
    .detail { --mat-card-elevated-container-color: #fff; height: 100%; }
    mat-card-content { flex: 1; min-height: 0; overflow: auto; }
    mat-card-title { font: var(--mat-sys-title-medium); }
    mat-card-header { align-items: center; flex-wrap: wrap; gap: 0.5rem; }
    .actions { margin-left: auto; display: flex; gap: 0.4rem; }
    mat-card-content { padding-top: 0.75rem; }
    .desc { margin: 0.25rem 0; color: var(--mat-sys-on-surface-variant); }
    .chips { display: flex; flex-wrap: wrap; gap: 0.35rem; }
    .generated { margin: 0.5rem 0; }
    .save-row { display: flex; align-items: center; gap: 0.75rem; margin: 0.5rem 0; }
    .hint { margin: 0.25rem 0 0; }
    .cache { display: flex; flex-wrap: wrap; align-items: center; gap: 0.5rem 0.75rem; margin: 0.25rem 0 0.75rem; }
    .cache .field { display: flex; align-items: center; gap: 0.5rem; }
  `,
  host: { '[class.inactive]': '!active()' },
})
export class DatasetTabComponent {
  private readonly api = inject(DatasetsService);
  private readonly confirms = inject(ConfirmService);
  private readonly sourcesApi = inject(DataSourcesService);

  readonly dataset = input.required<Dataset>();
  readonly orgId = input.required<string>();
  readonly canEdit = input(false);
  /** Only the visible tab is shown; the others stay alive so their editor text and results survive. */
  readonly active = input(true);

  /** The saved dataset, after the SQL was saved. */
  readonly updated = output<Dataset>();
  readonly deleted = output<string>();
  readonly dirtyChange = output<boolean>();

  // The three below start from, and reset to, what is saved. They follow the saved content itself (not the dataset object),
  // so changing something else about the dataset, such as how long its results are cached, does not wipe an unsaved edit.
  // Each source is a computed: it only changes when the content does, while a bare function would re-run for any change.
  private readonly savedSql = computed(() => this.dataset().sql ?? '');
  private readonly savedDefinition = computed(() => JSON.stringify(this.dataset().definition ?? emptyTableView()));
  private readonly savedParameters = computed(() => JSON.stringify(this.dataset().parameters ?? []));

  /** The text in the editor. */
  protected readonly sqlText = linkedSignal({ source: this.savedSql, computation: (sql) => sql });
  /** A table view that this user may edit: built step by step instead of written as SQL. */
  protected readonly isView = computed(() => this.canEdit() && this.dataset().kind === 'TableView' && this.dataset().definition !== null);
  /** The table view being edited; starts from, and resets to, the saved definition. */
  protected readonly viewDefinition = linkedSignal<string, TableViewDefinition>({
    source: this.savedDefinition,
    computation: (json) => JSON.parse(json) as TableViewDefinition,
  });
  /** The SQL's parameters; they start from, and reset to, the saved ones. */
  protected readonly parameters = linkedSignal<string, DatasetParameter[]>({
    source: this.savedParameters,
    computation: (json) => JSON.parse(json) as DatasetParameter[],
  });
  protected readonly parameterHint = 'Write {{name}} in the SQL to make a value a parameter. Charts and dashboards can choose its value.';
  protected readonly missingDefaults = computed(() => parametersMissingDefaults(this.parameters()).length > 0);
  protected readonly dirty = computed(() =>
    this.isView()
      ? JSON.stringify(this.viewDefinition()) !== JSON.stringify(this.dataset().definition)
      : this.dataset().sql !== null
        && (this.sqlText() !== this.dataset().sql || !sameParameters(this.parameters(), this.dataset().parameters ?? [])));
  protected readonly cacheSeconds = computed(() => this.dataset().cacheSeconds ?? 0);
  protected readonly cacheText = computed(() => cacheLabel(this.cacheSeconds()).toLowerCase());
  /** The usual choices, plus the current setting if it was set to something else through the API. */
  protected readonly cacheChoices = computed(() =>
    CACHE_CHOICES.some((c) => c.seconds === this.cacheSeconds())
      ? CACHE_CHOICES
      : [...CACHE_CHOICES, { seconds: this.cacheSeconds(), label: cacheLabel(this.cacheSeconds()) }],
  );
  protected readonly cacheOptions = computed<SelectOption[]>(() => this.cacheChoices().map((c) => ({ value: String(c.seconds), label: c.label })));
  protected readonly cacheBusy = signal(false);
  protected readonly sqlErrors = signal<string[]>([]);
  /** Rows from running the editor's current SQL (saved or not) against the dataset's data source. */
  protected readonly testResult = signal<QueryPreview | null>(null);
  protected readonly testErrors = signal<string[]>([]);
  protected readonly testing = signal(false);

  protected readonly panel = signal<'preview' | null>(null);
  protected readonly preview = signal<QueryPreview | null>(null);
  protected readonly errors = signal<string[]>([]);
  protected readonly busy = signal(false);
  constructor() {
    effect(() => this.dirtyChange.emit(this.dirty()));
  }

  /** Runs the SQL as it is in the editor, so a change can be tried before it is saved. */
  protected testSql(): void {
    const sql = this.sqlText().trim();
    if (!this.canEdit() || !sql || this.testing()) return;
    this.testing.set(true);
    this.testResult.set(null);
    this.testErrors.set([]);
    this.sourcesApi.preview(this.orgId(), this.dataset().dataSourceId, sql, MAX_PREVIEW_ROWS, this.parameters()).subscribe({
      next: (r) => {
        this.testing.set(false);
        this.testResult.set(r);
      },
      error: (e) => {
        this.testing.set(false);
        this.testErrors.set(problemMessages(e, 'The query failed.'));
      },
    });
  }

  protected saveSql(): void {
    const dataset = this.dataset();
    if (!this.canEdit() || !this.dirty() || this.busy()) return;
    this.busy.set(true);
    this.sqlErrors.set([]);
    const request = this.isView()
      ? this.api.updateTableView(this.orgId(), dataset.id, dataset.name, this.viewDefinition(), dataset.description)
      : this.api.update(this.orgId(), dataset.id, dataset.name, this.sqlText(), dataset.description, this.parameters());
    request.subscribe({
      next: (saved) => {
        this.busy.set(false);
        // The columns may have changed with the query, so any open results no longer describe the dataset.
        this.preview.set(null);
        this.panel.set(null);
        this.updated.emit(saved);
      },
      error: (e) => {
        this.busy.set(false);
        this.sqlErrors.set(problemMessages(e, 'Could not save the query.'));
      },
    });
  }

  /** Takes effect at once and needs no Save: it is how the dataset is served, not what it is. */
  protected setCache(value: string): void {
    const dataset = this.dataset();
    const seconds = Number(value);
    if (!this.canEdit() || this.cacheBusy() || seconds === this.cacheSeconds()) return;
    this.cacheBusy.set(true);
    this.sqlErrors.set([]);
    this.api.setCache(this.orgId(), dataset.id, seconds).subscribe({
      next: (saved) => {
        this.cacheBusy.set(false);
        // Only the setting changed; the list takes the new copy, and unsaved edits in this tab are left alone.
        this.updated.emit({ ...dataset, cacheSeconds: saved.cacheSeconds ?? seconds });
      },
      error: (e) => {
        this.cacheBusy.set(false);
        this.sqlErrors.set(problemMessages(e, 'Could not change how long results are kept.')); // the select still shows the old setting
      },
    });
  }

  protected show(): void {
    this.panel.set('preview');
    this.preview.set(null);
    this.errors.set([]);
    this.api.preview(this.orgId(), this.dataset().id).subscribe({
      next: (p) => this.preview.set(p),
      error: (e) => this.errors.set(problemMessages(e, 'Could not load the data.')),
    });
  }

  protected remove(): void {
    const dataset = this.dataset();
    this.confirms
      .ask({ title: 'Delete dataset', message: `Delete the dataset "${dataset.name}"?`, confirmLabel: 'Delete', danger: true })
      .subscribe((yes) => {
        if (yes) this.removeConfirmed(dataset);
      });
  }

  private removeConfirmed(dataset: Dataset): void {
    this.api.remove(this.orgId(), dataset.id).subscribe({
      next: () => this.deleted.emit(dataset.id),
      error: (e) => this.errors.set(problemMessages(e, 'Could not delete the dataset.')),
    });
  }
}
