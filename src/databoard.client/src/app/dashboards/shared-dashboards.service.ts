import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { quietly } from '../shared/loading.service';
import { ChartStyle, ChartType } from '../charts/charts.service';
import { DashboardBackground, DashboardRun, DashboardTab, DashboardWidget, FilterValues, SharedFilterOperator } from './dashboards.service';

/** What a visitor with a link needs to draw one chart: how it looks, not where its data comes from. */
export interface SharedChart {
  id: string;
  name: string;
  type: ChartType;
  style: ChartStyle;
  dimensionCount: number;
}

export interface SharedFilter {
  id: string;
  label: string;
  operator: SharedFilterOperator;
}

export interface SharedDashboard {
  name: string;
  description: string | null;
  widgets: DashboardWidget[];
  filters: SharedFilter[];
  charts: SharedChart[];
  expiresUtc: string | null;
  background?: DashboardBackground | null;
  /** Missing or empty: one page. */
  tabs?: DashboardTab[] | null;
  reloadSeconds?: number | null;
  /** The organization that owns it, when another organization shares it (not for a link). */
  sharedBy?: string | null;
}

/**
 * The read-only view of a dashboard: through a public link (the token in the address is the credential), or because
 * another organization shares it with yours (you are signed in as one of its members). Both are the same page, so every
 * call takes where the dashboard is: a token, or the address of a shared dashboard (see `orgSharedAddress`).
 */
@Injectable({ providedIn: 'root' })
export class SharedDashboardsService {
  private readonly http = inject(HttpClient);

  private url(where: string, suffix = ''): string {
    return where.startsWith('/api/') ? where + suffix : `/api/shared/dashboards/${encodeURIComponent(where)}${suffix}`;
  }

  get(token: string): Observable<SharedDashboard> {
    return this.http.get<SharedDashboard>(this.url(token));
  }

  /** The picture behind the dashboard, as a file. */
  backgroundImage(token: string, version: string): Observable<Blob> {
    return this.http.get(this.url(token, '/background'), { params: { v: version }, responseType: 'blob' });
  }

  /** The picture of a picture widget, as a file. */
  widgetImage(token: string, widgetId: string, version: string): Observable<Blob> {
    return this.http.get(this.url(token, `/images/${widgetId}`), { params: { v: version }, responseType: 'blob' });
  }

  /** `quiet`: a reload the page does by itself, which shows no loading bar. */
  run(token: string, filters: FilterValues, quiet = false): Observable<DashboardRun> {
    return this.http.post<DashboardRun>(this.url(token, '/run'), { filters }, quiet ? { context: quietly() } : {});
  }
}

/** Where a dashboard that another organization shares with `viewingOrg` can be read from. */
export const orgSharedAddress = (viewingOrg: string, sourceOrg: string, dashboardId: string): string =>
  `/api/organizations/${viewingOrg}/shared-dashboards/${sourceOrg}/${dashboardId}`;
