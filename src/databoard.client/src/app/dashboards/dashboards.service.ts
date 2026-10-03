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

/** What a widget shows: a saved chart (the default), a picture that belongs to the dashboard, or some text. */
export type WidgetKind = 'Chart' | 'Image' | 'Text';

export type TextAlign = 'left' | 'center' | 'right';
export type TextSize = 'small' | 'normal' | 'large';

export const TEXT_ALIGNS: { value: TextAlign; label: string }[] = [
  { value: 'left', label: 'Left' }, { value: 'center', label: 'Centered' }, { value: 'right', label: 'Right' },
];
export const TEXT_SIZES: { value: TextSize; label: string }[] = [
  { value: 'small', label: 'Small' }, { value: 'normal', label: 'Normal' }, { value: 'large', label: 'Large' },
];
export const MAX_TEXT_LENGTH = 4000;

export type ImageFit = 'contain' | 'cover' | 'fill';

export const IMAGE_FITS: { value: ImageFit; label: string }[] = [
  { value: 'contain', label: 'Show all of it' },
  { value: 'cover', label: 'Fill the widget (may crop)' },
  { value: 'fill', label: 'Stretch to the widget' },
];

/**
 * A widget placed on a dashboard. A chart widget shows a saved chart (dataset, type, query, style), shared by every
 * dashboard that shows it. An image widget shows a picture kept with this dashboard; it is made by uploading, and only
 * its fit, description and link can be changed afterwards.
 */
export interface DashboardWidget {
  id: string;
  /** The chart of a chart widget; empty for a picture. */
  chartId: string;
  /** Missing means a chart. */
  kind?: WidgetKind;
  /** For a picture: names the file, and changes whenever the picture does. Set by the server. */
  imageKey?: string | null;
  imageFit?: ImageFit | null;
  /** What the picture shows, for people who cannot see it. */
  alt?: string | null;
  /** Where a click on the picture goes (http or https). */
  linkUrl?: string | null;
  /** For a text widget: simple Markdown (headings, bold, italic, lists, links). */
  text?: string | null;
  textAlign?: TextAlign | null;
  textSize?: TextSize | null;
  /** The tab the widget is on. Missing or null: the first tab, or the one page of a dashboard without tabs. */
  tabId?: string | null;
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

/** A page of a dashboard. Each tab has its own grid; the shared filters apply to every tab. */
export interface DashboardTab {
  id: string;
  name: string;
}

export interface DashboardDefinition {
  widgets: DashboardWidget[];
  filters: DashboardFilter[];
  /** Missing or empty: the dashboard is one page. */
  tabs?: DashboardTab[] | null;
  /** How often an open dashboard asks for its charts again, 10 to 3600 seconds. Missing or null: only when asked. */
  reloadSeconds?: number | null;
}

export interface DashboardSummary {
  id: string;
  name: string;
  description: string | null;
  widgetCount: number;
  updatedUtc: string;
  /** The charts on the dashboard, so a chart can show which dashboards use it. */
  chartIds?: string[];
  /** The folder the dashboard is filed in; null or missing means the top level. */
  folderId?: string | null;
}

/** How the server keeps a dashboard's data fresh, and how the last time went. */
export interface DashboardRefresh {
  /** How often the server refreshes the charts, in minutes; null when it does not. */
  intervalMinutes: number | null;
  nextUtc: string | null;
  lastUtc: string | null;
  /** How many charts could not be refreshed the last time; 0 when it went well. */
  lastFailures: number;
  lastMessage: string | null;
}

/** The picture behind a dashboard and how it is shown. `version` changes with the picture, so a cached copy is never reused for a new one. */
export interface DashboardBackground {
  version: string;
  fit: BackgroundFit;
  /** 10 to 100: how strong the picture is. */
  opacity: number;
}

export type BackgroundFit = 'cover' | 'contain' | 'tile' | 'center';

export const BACKGROUND_FITS: { value: BackgroundFit; label: string }[] = [
  { value: 'cover', label: 'Fill the page' },
  { value: 'contain', label: 'Fit inside' },
  { value: 'tile', label: 'Repeat' },
  { value: 'center', label: 'Centered, as it is' },
];

/** What a picture may be, checked before it is sent (the server checks again, by looking at the file). */
export const BACKGROUND_TYPES = ['image/png', 'image/jpeg', 'image/gif', 'image/webp'];
export const BACKGROUND_MAX_BYTES = 5 * 1024 * 1024;

export interface Dashboard {
  id: string;
  name: string;
  description: string | null;
  definition: DashboardDefinition;
  createdUtc: string;
  updatedUtc: string;
  refresh?: DashboardRefresh;
  folderId?: string | null;
  /** Null or missing when there is no picture. */
  background?: DashboardBackground | null;
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
  /** Who may open it: empty is anyone with the link, otherwise signed-in members of these organizations. */
  organizationIds?: string[];
}

export interface DashboardShareCreated {
  id: string;
  /** The secret part of the link. Shown once: the server keeps only a hash. */
  token: string;
  createdUtc: string;
  expiresUtc: string | null;
  organizationIds?: string[];
}

/** How long a new link lasts: a number of days, or explicitly forever. */
export type ShareLifetime = { expiresInDays: number } | { neverExpires: true };

/**
 * A dashboard as a file (see the export): charts are named, tabs have keys local to the file, pictures are inside it. Kept loose on
 * purpose: the server checks it, and a file from a newer version is shown its message, not refused here.
 */
export interface DashboardPackage {
  format: string;
  version: number;
  name: string;
  description?: string | null;
  [more: string]: unknown;
}

export const DASHBOARD_PACKAGE_FORMAT = 'databoard-dashboard';
/** The biggest file the server takes. */
export const MAX_PACKAGE_BYTES = 64 * 1024 * 1024;

/** What an import made, and the charts the file named that this organization does not have (their widgets were left out). */
export interface ImportResult {
  dashboard: Dashboard;
  missingCharts: string[];
}

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
    orgId: string, name: string, definition: DashboardDefinition = { widgets: [], filters: [] }, description?: string, folderId?: string | null,
  ): Observable<Dashboard> {
    return this.http.post<Dashboard>(this.url(orgId), { name, ...(description ? { description } : {}), definition, ...(folderId ? { folderId } : {}) });
  }

  /** Files the dashboard in a folder (null for the top level). */
  move(orgId: string, id: string, folderId: string | null): Observable<Dashboard> {
    return this.http.put<Dashboard>(this.url(orgId, `/${id}/folder`), { folderId });
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

  /** `refresh` asks the databases again instead of reusing stored results (editors and owners only). */
  run(orgId: string, id: string, filters: FilterValues, refresh = false): Observable<DashboardRun> {
    return this.http.post<DashboardRun>(this.url(orgId, `/${id}/run`), refresh ? { filters, refresh } : { filters });
  }

  /** Puts a picture behind the dashboard, or (with no file) changes how the current one is shown. Editors and owners. */
  setBackground(orgId: string, id: string, change: { file?: File | null; fit?: BackgroundFit; opacity?: number }): Observable<Dashboard> {
    const form = new FormData();
    if (change.file) form.append('File', change.file);
    if (change.fit) form.append('Fit', change.fit);
    if (change.opacity !== undefined) form.append('Opacity', String(change.opacity));
    return this.http.put<Dashboard>(this.url(orgId, `/${id}/background`), form);
  }

  removeBackground(orgId: string, id: string): Observable<Dashboard> {
    return this.http.delete<Dashboard>(this.url(orgId, `/${id}/background`));
  }

  /** The picture itself, as a file. (An image tag cannot send the sign-in, so it is fetched like any request and shown from memory.) */
  backgroundImage(orgId: string, id: string, version: string): Observable<Blob> {
    return this.http.get(this.url(orgId, `/${id}/background`), { params: { v: version }, responseType: 'blob' });
  }

  /** The dashboard as a file to keep or to bring into another organization. Editors and owners. */
  exportPackage(orgId: string, id: string): Observable<DashboardPackage> {
    return this.http.get<DashboardPackage>(this.url(orgId, `/${id}/export`));
  }

  /** Makes a dashboard from an exported file; charts are found by name. Editors and owners. */
  importPackage(orgId: string, pkg: DashboardPackage, options: { name?: string | null; folderId?: string | null } = {}): Observable<ImportResult> {
    return this.http.post<ImportResult>(this.url(orgId, '/import'), { package: pkg, name: options.name ?? null, folderId: options.folderId ?? null });
  }

  /** A copy in the same organization. */
  duplicate(orgId: string, id: string, name?: string | null): Observable<Dashboard> {
    return this.http.post<Dashboard>(this.url(orgId, `/${id}/duplicate`), name ? { name } : {});
  }

  /** Puts a picture on the dashboard as a widget below the others. Editors and owners. */
  addImageWidget(
    orgId: string, id: string, add: { file: File; fit?: ImageFit; alt?: string | null; linkUrl?: string | null; tabId?: string | null },
  ): Observable<Dashboard> {
    const form = new FormData();
    form.append('File', add.file);
    if (add.tabId) form.append('TabId', add.tabId);
    if (add.fit) form.append('Fit', add.fit);
    if (add.alt) form.append('Alt', add.alt);
    if (add.linkUrl) form.append('LinkUrl', add.linkUrl);
    return this.http.post<Dashboard>(this.url(orgId, `/${id}/image-widgets`), form);
  }

  /** Puts another picture in a picture widget; everything else about the widget stays. */
  replaceImageWidget(orgId: string, id: string, widgetId: string, file: File): Observable<Dashboard> {
    const form = new FormData();
    form.append('File', file);
    return this.http.put<Dashboard>(this.url(orgId, `/${id}/image-widgets/${widgetId}/image`), form);
  }

  /** The picture of a picture widget, as a file (see `backgroundImage` for why it is fetched like this). */
  widgetImage(orgId: string, id: string, widgetId: string, version: string): Observable<Blob> {
    return this.http.get(this.url(orgId, `/${id}/image-widgets/${widgetId}/image`), { params: { v: version }, responseType: 'blob' });
  }

  /** Puts the dashboard on a refresh schedule (5 to 1440 minutes), or takes it off with null. Editors and owners. */
  setRefresh(orgId: string, id: string, intervalMinutes: number | null): Observable<Dashboard> {
    return this.http.put<Dashboard>(this.url(orgId, `/${id}/refresh`), { intervalMinutes });
  }

  listShares(orgId: string, id: string): Observable<DashboardShare[]> {
    return this.http.get<DashboardShare[]>(this.url(orgId, `/${id}/shares`));
  }

  /** `organizationIds` empty: anyone with the link. Otherwise only signed-in members of those organizations (ones you belong to). */
  createShare(orgId: string, id: string, lifetime: ShareLifetime, organizationIds: string[] = []): Observable<DashboardShareCreated> {
    return this.http.post<DashboardShareCreated>(this.url(orgId, `/${id}/shares`), { ...lifetime, organizationIds });
  }

  revokeShare(orgId: string, id: string, shareId: string): Observable<void> {
    return this.http.delete<void>(this.url(orgId, `/${id}/shares/${shareId}`));
  }
}
