import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { QueryPreview } from './data-sources.service';

export type ColumnRole = 'Dimension' | 'Measure';
export type Aggregation = 'Sum' | 'Avg' | 'Min' | 'Max' | 'Count' | 'CountDistinct';
export type TimeGrain = 'Day' | 'Week' | 'Month' | 'Quarter' | 'Year';

/** The typed query the API understands. It is a description, never SQL. */
export interface ChartQuery {
  dimensions?: { column: string; grain?: TimeGrain; alias?: string }[];
  measures?: { aggregation: Aggregation; column?: string; alias?: string }[];
  limit?: number;
}

export interface ChartResult extends QueryPreview {
  /** Only editors and owners receive the generated SQL. */
  sql: string | null;
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

  create(orgId: string, dataSourceId: string, name: string, sql: string, description?: string): Observable<Dataset> {
    return this.http.post<Dataset>(this.url(orgId), { dataSourceId, name, description: description ?? null, sql });
  }

  /** A table view sends its definition; the server generates the SQL. */
  createTableView(orgId: string, dataSourceId: string, name: string, definition: TableViewDefinition, description?: string): Observable<Dataset> {
    return this.http.post<Dataset>(this.url(orgId), { dataSourceId, name, description: description ?? null, kind: 'TableView', definition });
  }

  update(orgId: string, id: string, name: string, sql: string, description: string | null): Observable<Dataset> {
    return this.http.put<Dataset>(this.url(orgId, `/${id}`), { name, description, sql });
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

  preview(orgId: string, id: string): Observable<QueryPreview> {
    return this.http.get<QueryPreview>(this.url(orgId, `/${id}/preview`));
  }

  query(orgId: string, id: string, query: ChartQuery): Observable<ChartResult> {
    return this.http.post<ChartResult>(this.url(orgId, `/${id}/query`), query);
  }
}
