import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { QueryPreview } from './data-sources.service';
import { DatasetParameter } from './dataset-parameters';

export type ColumnRole = 'Dimension' | 'Measure';
export type Aggregation = 'Sum' | 'Avg' | 'Min' | 'Max' | 'Count' | 'CountDistinct';
export type TimeGrain = 'Day' | 'Week' | 'Month' | 'Quarter' | 'Year';

/** The typed query the API understands. It is a description, never SQL. */
export interface ChartQuery {
  dimensions?: { column: string; grain?: TimeGrain | null; alias?: string | null }[] | null;
  measures?: { aggregation: Aggregation; column?: string | null; alias?: string | null }[] | null;
  /** Conditions on dataset columns. In, NotIn and Between take `values`; the others take `value`; IsNull and IsNotNull take neither. */
  filters?: { column: string; operator: string; value?: unknown; values?: unknown[] | null }[] | null;
  /** Sorts by a name in the result (a dimension or metric alias). */
  orderBy?: { field: string; direction: 'Asc' | 'Desc' }[] | null;
  limit?: number | null;
  /** Keeps only this many series (the second dimension), ranked by the first metric's total. */
  seriesLimit?: number | null;
  /** Values for the dataset's own parameters, by name. A parameter that is left out uses its default. */
  parameters?: Record<string, unknown> | null;
}

export type { DatasetParameter, ParameterType } from './dataset-parameters';

export interface ChartResult extends QueryPreview {
  /** Only editors and owners receive the generated SQL. */
  sql: string | null;
  /** When the data was fetched from the database, if this result was stored and reused; null if it was fetched just now. */
  cachedUtc?: string | null;
}

export interface DatasetColumn {
  name: string;
  dataType: string;
  role: ColumnRole;
}

/** A dataset is written as SQL, or put together in the guided table view (main table, fields, joins). */
export type DatasetKind = 'Sql' | 'TableView';
export type JoinType = 'Inner' | 'Left' | 'Right' | 'Full';

export interface TableViewTable { schema: string; name: string; fields: string[] }
/** Step 0 is the main table, step k the k-th join; a join may only refer to the steps before it. */
export interface JoinCondition { leftStep: number; leftField: string; rightField: string }
export interface TableViewJoin { type: JoinType; table: TableViewTable; on: JoinCondition[] }
export interface TableViewDefinition { main: TableViewTable; joins: TableViewJoin[] }

export interface TableViewPreview extends QueryPreview {
  /** The SQL the definition stands for. */
  sql: string;
}

export const emptyTableView = (): TableViewDefinition => ({ main: { schema: '', name: '', fields: [] }, joins: [] });

export interface Dataset {
  id: string;
  dataSourceId: string;
  name: string;
  description: string | null;
  /** Null for viewers: only editors and owners may see how a dataset is built. */
  sql: string | null;
  columns: DatasetColumn[];
  kind: DatasetKind;
  /** Only for table views, and only for editors and owners. */
  definition: TableViewDefinition | null;
  /** The values the SQL takes. Empty for a table view and for SQL without {{placeholders}}. */
  parameters?: DatasetParameter[];
  /** How long results of queries on this dataset are reused, in seconds. 0 (or missing) means every run is live. */
  cacheSeconds?: number;
  /** The folder the dataset is filed in; null or missing means the top level. */
  folderId?: string | null;
  createdUtc: string;
  updatedUtc: string;
}

@Injectable({ providedIn: 'root' })
export class DatasetsService {
  private readonly http = inject(HttpClient);

  private url(orgId: string, suffix = ''): string {
    return `/api/organizations/${orgId}/datasets${suffix}`;
  }

  list(orgId: string): Observable<Dataset[]> {
    return this.http.get<Dataset[]>(this.url(orgId));
  }

  create(
    orgId: string, dataSourceId: string, name: string, sql: string, description?: string, parameters: DatasetParameter[] = [], folderId?: string | null,
  ): Observable<Dataset> {
    return this.http.post<Dataset>(this.url(orgId), {
      dataSourceId, name, description: description ?? null, sql, ...(parameters.length > 0 ? { parameters } : {}), ...(folderId ? { folderId } : {}),
    });
  }

  /** A table view sends its definition; the server generates the SQL. */
  createTableView(
    orgId: string, dataSourceId: string, name: string, definition: TableViewDefinition, description?: string, folderId?: string | null,
  ): Observable<Dataset> {
    return this.http.post<Dataset>(this.url(orgId), {
      dataSourceId, name, description: description ?? null, kind: 'TableView', definition, ...(folderId ? { folderId } : {}),
    });
  }

  /** Files the dataset in a folder (null for the top level). */
  move(orgId: string, id: string, folderId: string | null): Observable<Dataset> {
    return this.http.put<Dataset>(this.url(orgId, `/${id}/folder`), { folderId });
  }

  /** An update replaces everything, so leaving the parameters out removes them. */
  update(orgId: string, id: string, name: string, sql: string, description: string | null, parameters: DatasetParameter[] = []): Observable<Dataset> {
    return this.http.put<Dataset>(this.url(orgId, `/${id}`), { name, description, sql, ...(parameters.length > 0 ? { parameters } : {}) });
  }

  updateTableView(orgId: string, id: string, name: string, definition: TableViewDefinition, description: string | null): Observable<Dataset> {
    return this.http.put<Dataset>(this.url(orgId, `/${id}`), { name, description, definition });
  }

  /** Runs a table view definition without saving it (editors and owners). */
  previewTableView(orgId: string, dataSourceId: string, definition: TableViewDefinition, maxRows: number): Observable<TableViewPreview> {
    return this.http.post<TableViewPreview>(`/api/organizations/${orgId}/data-sources/${dataSourceId}/table-view/preview`, { definition, maxRows });
  }

  remove(orgId: string, id: string): Observable<void> {
    return this.http.delete<void>(this.url(orgId, `/${id}`));
  }

  /** Chooses how long results are reused (0 for never). Takes effect at once; it does not change the dataset itself. */
  setCache(orgId: string, id: string, cacheSeconds: number): Observable<Dataset> {
    return this.http.put<Dataset>(this.url(orgId, `/${id}/cache`), { cacheSeconds });
  }

  preview(orgId: string, id: string): Observable<QueryPreview> {
    return this.http.get<QueryPreview>(this.url(orgId, `/${id}/preview`));
  }

  query(orgId: string, id: string, query: ChartQuery): Observable<ChartResult> {
    return this.http.post<ChartResult>(this.url(orgId, `/${id}/query`), query);
  }
}
