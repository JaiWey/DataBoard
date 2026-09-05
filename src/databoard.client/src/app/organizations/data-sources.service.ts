import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

export interface DataSource {
  id: string;
  organizationId: string;
  name: string;
  type: string;
  createdUtc: string;
}

export interface ConnectionTestResult {
  success: boolean;
  message: string;
  serverVersion: string | null;
  latencyMs: number;
}

export interface ColumnSchema { name: string; dataType: string; nullable: boolean }
export interface TableSchema { name: string; kind: string; columns: ColumnSchema[] }
export interface SchemaNode { name: string; tables: TableSchema[] }
export interface DatabaseSchema { schemas: SchemaNode[]; truncated: boolean }

/** The most rows the API returns for one query. */
export const MAX_PREVIEW_ROWS = 100;

export interface QueryPreview {
  columns: { name: string; dataType: string }[];
  rows: unknown[][];
  truncated: boolean;
}

/** Reads the API's problem details into plain messages: validation errors, or a single detail line. */
export function problemMessages(e: unknown, fallback: string): string[] {
  if (!(e instanceof HttpErrorResponse)) return [fallback];
  if (e.status === 429) return ['Too many requests. Wait a moment and try again.'];
  if (e.status === 403) return ['You do not have permission to do this.'];

  const fields = e.error?.errors as Record<string, string[]> | undefined;
  const messages = fields ? Object.values(fields).flat() : [];
  if (messages.length) return messages;
  return typeof e.error?.detail === 'string' ? [e.error.detail] : [fallback];
}

@Injectable({ providedIn: 'root' })
export class DataSourcesService {
  private readonly http = inject(HttpClient);

  private url(orgId: string, suffix = ''): string {
    return `/api/organizations/${orgId}/data-sources${suffix}`;
  }

  list(orgId: string): Observable<DataSource[]> {
    return this.http.get<DataSource[]>(this.url(orgId));
  }

  create(orgId: string, name: string, connectionString: string): Observable<DataSource> {
    return this.http.post<DataSource>(this.url(orgId), { name, type: 'PostgreSql', connectionString });
  }

  testNew(orgId: string, connectionString: string): Observable<ConnectionTestResult> {
    return this.http.post<ConnectionTestResult>(this.url(orgId, '/test'), { type: 'PostgreSql', connectionString });
  }

  testSaved(orgId: string, id: string): Observable<ConnectionTestResult> {
    return this.http.post<ConnectionTestResult>(this.url(orgId, `/${id}/test`), {});
  }

  schema(orgId: string, id: string): Observable<DatabaseSchema> {
    return this.http.get<DatabaseSchema>(this.url(orgId, `/${id}/schema`));
  }

  preview(orgId: string, id: string, sql: string, maxRows?: number): Observable<QueryPreview> {
    return this.http.post<QueryPreview>(this.url(orgId, `/${id}/preview`), maxRows ? { sql, maxRows } : { sql });
  }
}
