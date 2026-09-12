import { Component, OnInit, computed, effect, inject, input, output, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { SqlEditorComponent } from '../sql-editor/sql-editor.component';
import { SqlResultsComponent } from '../sql-editor/sql-results.component';
import { DataSource, DataSourcesService, MAX_PREVIEW_ROWS, QueryPreview, problemMessages } from './data-sources.service';
import { DatasetParameter, parametersMissingDefaults } from './dataset-parameters';
import { ParametersPanelComponent } from './parameters-panel.component';
import { Dataset } from './datasets.service';
import { SaveDatasetDialogComponent, SaveDatasetDialogData } from './save-dataset-dialog.component';

/** An unsaved dataset: pick the data source, write the SQL, and Save asks for a name. */
@Component({
  selector: 'app-new-dataset-tab',
  imports: [MatCardModule, MatButtonModule, MatFormFieldModule, MatInputModule, SqlEditorComponent, SqlResultsComponent, ParametersPanelComponent],
  template: `
    <mat-card appearance="raised" class="detail">
      <mat-card-header>
        <mat-card-title>New dataset</mat-card-title>
        <span class="actions">
          <button mat-stroked-button type="button" class="test" [disabled]="!canSave() || testing()" (click)="test()">Run query</button>
          <button mat-flat-button type="button" class="save" [disabled]="!canSave()" (click)="openSave()">Save</button>
        </span>
      </mat-card-header>
      <mat-card-content>
        @if (loaded() && sources().length === 0) {
          <p class="fail" role="alert">Add a data source first; a dataset is a saved query on one.</p>
        }
        <mat-form-field class="source">
          <mat-label>Data source</mat-label>
          <select matNativeControl aria-label="Data source" [value]="sourceId()" (change)="sourceId.set($any($event.target).value)">
            @for (s of sources(); track s.id) { <option [value]="s.id">{{ s.name }}</option> }
          </select>
        </mat-form-field>
        <app-sql-editor [value]="sql()" (valueChange)="sql.set($event)" (save)="openSave()" (run)="test()" />
        <app-parameters-panel [sql]="sql()" [(parameters)]="parameters" />
        <p><small>One read-only SELECT. Give every column a unique name. {{ parameterHint }}</small></p>
        @for (message of errors(); track message) {
          <p class="fail" role="alert">{{ message }}</p>
        }
        <app-sql-results [result]="testResult()" [errors]="testErrors()" />
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
    .source { width: 18rem; max-width: 100%; }
  `,
  host: { '[class.inactive]': '!active()' },
})
export class NewDatasetTabComponent implements OnInit {
  private readonly sourcesApi = inject(DataSourcesService);
  private readonly dialog = inject(MatDialog);

  readonly orgId = input.required<string>();
  readonly active = input(true);
  /** The dataset that was saved; the page turns this tab into that dataset's tab. */
  readonly created = output<Dataset>();
  /** True once there is SQL that would be lost by closing the tab. */
  readonly dirtyChange = output<boolean>();

  protected readonly sources = signal<DataSource[]>([]);
  protected readonly loaded = signal(false);
  protected readonly sourceId = signal('');
  protected readonly sql = signal('');
  protected readonly errors = signal<string[]>([]);
  protected readonly testResult = signal<QueryPreview | null>(null);
  protected readonly testErrors = signal<string[]>([]);
  protected readonly testing = signal(false);
  /** The SQL's parameters, kept in step with its {{placeholders}} by the panel. */
  protected readonly parameters = signal<DatasetParameter[]>([]);
  protected readonly parameterHint = 'Write {{name}} to make a value a parameter that charts and dashboards can choose.';
  protected readonly canSave = computed(
    () => !!this.sourceId() && this.sql().trim().length > 0 && parametersMissingDefaults(this.parameters()).length === 0,
  );

  constructor() {
    effect(() => this.dirtyChange.emit(this.sql().trim().length > 0));
  }

  ngOnInit(): void {
    this.sourcesApi.list(this.orgId()).subscribe({
      next: (items) => {
        this.sources.set(items);
        this.loaded.set(true);
        if (items.length > 0) this.sourceId.set(items[0].id);
      },
      error: (e) => {
        this.loaded.set(true);
        this.errors.set(problemMessages(e, 'Could not load the data sources.'));
      },
    });
  }

  /** Runs the SQL on the chosen data source and shows the rows below the editor. */
  protected test(): void {
    if (!this.canSave() || this.testing()) return;
    this.testing.set(true);
    this.testResult.set(null);
    this.testErrors.set([]);
    this.sourcesApi.preview(this.orgId(), this.sourceId(), this.sql().trim(), MAX_PREVIEW_ROWS, this.parameters()).subscribe({
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

  protected openSave(): void {
    if (!this.canSave()) return;
    this.dialog
      .open<SaveDatasetDialogComponent, SaveDatasetDialogData, Dataset>(SaveDatasetDialogComponent, {
        data: { orgId: this.orgId(), dataSourceId: this.sourceId(), sql: this.sql(), parameters: this.parameters() },
        width: '28rem',
        maxWidth: '95vw',
      })
      .afterClosed()
      .subscribe((created) => {
        if (created) this.created.emit(created);
      });
  }
}
