import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ChartResult } from '../organizations/datasets.service';

export type SharedFilterOperator = 'Eq' | 'NotEq' | 'In' | 'Contains' | 'StartsWith' | 'Gte' | 'Lte' | 'Between';

export const SHARED_FILTER_OPERATORS: { value: SharedFilterOperator; label: string }[] = [
  { value: 'Eq', label: 'is' },
  { value: 'NotEq', label: 'is not' },
  { value: 'In', label: 'is one of' },
  { value: 'Contains', label: 'contains' },
  { value: 'StartsWith', label: 'starts with' },
  { value: 'Gte', label: 'at least' },
  { value: 'Lte', label: 'at most' },
  { value: 'Between', label: 'between' },
];

/** A saved chart placed on a dashboard. The chart itself (dataset, type, query, style) is shared by every dashboard that shows it. */
export interface DashboardWidget {
  id: string;
  chartId: string;
  /** Where the widget sits on the 12-column grid. Dashboards saved before layouts existed have none; the page places those. */
  layout?: { x: number; y: number; w: number; h: number } | null;
}

/** What a shared filter acts on: a dataset column (it filters rows), or a dataset parameter (it sets a value the SQL takes). */
export type FilterTarget = 'Column' | 'Parameter';

export interface DashboardFilter {
  id: string;
  label: string;
  /** The column's name, or the parameter's name when the target is a parameter. */
  column: string;
  operator: SharedFilterOperator;
  /** Left out (or Column) for a column filter. */
  target?: FilterTarget;
}

export interface DashboardDefinition {
  widgets: DashboardWidget[];
  filters: DashboardFilter[];
}

export interface DashboardSummary {
  id: string;
  name: string;
  description: string | null;
  widgetCount: number;
  updatedUtc: string;
  /** The charts on the dashboard, so a chart can show which dashboards use it. */
  chartIds?: string[];
}

export interface Dashboard {
  id: string;
  name: string;
  description: string | null;
  definition: DashboardDefinition;
  createdUtc: string;
  updatedUtc: string;
}

export interface WidgetResult {
  widgetId: string;
  result: ChartResult | null;
  error: string | null;
  appliedFilters: string[];
}

export interface DashboardRun {
  widgets: WidgetResult[];
}

/** Filter id -> the values the viewer picked. A filter with no values is not applied. */
export type FilterValues = Record<string, string[]>;

/** A public, read-only link to a dashboard. The secret itself is never listed: it is only shown when the link is made. */
export interface DashboardShare {
  id: string;
  createdUtc: string;
  expiresUtc: string | null;
  expired: boolean;
}

export interface DashboardShareCreated {
  id: string;
  /** The secret part of the link. Shown once: the server keeps only a hash. */
  token: string;
  createdUtc: string;
  expiresUtc: string | null;
}

/** How long a new link lasts: a number of days, or explicitly forever. */
export type ShareLifetime = { expiresInDays: number } | { neverExpires: true };

/** Ids are assigned by the server; a new widget or filter is sent with the empty id. */
export const NEW_ID = '00000000-0000-0000-0000-000000000000';

@Injectable({ providedIn: 'root' })
export class DashboardsService {
  private readonly http = inject(HttpClient);

  private url(orgId: string, suffix = ''): string {
    return `/api/organizations/${orgId}/dashboards${suffix}`;
  }

  list(orgId: string): Observable<DashboardSummary[]> {
    return this.http.get<DashboardSummary[]>(this.url(orgId));
  }

  get(orgId: string, id: string): Observable<Dashboard> {
    return this.http.get<Dashboard>(this.url(orgId, `/${id}`));
  }

  create(
    orgId: string, name: string, definition: DashboardDefinition = { widgets: [], filters: [] }, description?: string,
  ): Observable<Dashboard> {
    return this.http.post<Dashboard>(this.url(orgId), { name, ...(description ? { description } : {}), definition });
  }

  update(orgId: string, dashboard: Dashboard, definition: DashboardDefinition): Observable<Dashboard> {
    return this.http.put<Dashboard>(this.url(orgId, `/${dashboard.id}`), {
      name: dashboard.name,
      description: dashboard.description,
      definition,
    });
  }

  remove(orgId: string, id: string): Observable<void> {
    return this.http.delete<void>(this.url(orgId, `/${id}`));
  }

  run(orgId: string, id: string, filters: FilterValues): Observable<DashboardRun> {
    return this.http.post<DashboardRun>(this.url(orgId, `/${id}/run`), { filters });
  }

  listShares(orgId: string, id: string): Observable<DashboardShare[]> {
    return this.http.get<DashboardShare[]>(this.url(orgId, `/${id}/shares`));
  }

  createShare(orgId: string, id: string, lifetime: ShareLifetime): Observable<DashboardShareCreated> {
    return this.http.post<DashboardShareCreated>(this.url(orgId, `/${id}/shares`), lifetime);
  }

  revokeShare(orgId: string, id: string, shareId: string): Observable<void> {
    return this.http.delete<void>(this.url(orgId, `/${id}/shares/${shareId}`));
  }
}
