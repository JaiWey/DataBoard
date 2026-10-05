import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

/** What a folder organizes. Each kind has its own tree. */
export type FolderKind = 'Dataset' | 'Chart' | 'Dashboard';

/** A folder that organizes datasets, charts or dashboards. Folders nest; a null parent is the top level. */
export interface Folder {
  id: string;
  parentId: string | null;
  name: string;
  kind?: FolderKind;
  /** Who may see it is limited (dashboard folders). */
  restricted?: boolean;
}

@Injectable({ providedIn: 'root' })
export class FoldersService {
  private readonly http = inject(HttpClient);

  private url(orgId: string, suffix = ''): string {
    return `/api/organizations/${orgId}/folders${suffix}`;
  }

  list(orgId: string, kind: FolderKind): Observable<Folder[]> {
    return this.http.get<Folder[]>(this.url(orgId), { params: { kind } });
  }

  create(orgId: string, kind: FolderKind, name: string, parentId: string | null): Observable<Folder> {
    return this.http.post<Folder>(this.url(orgId), { name, parentId, kind });
  }

  /** Renames the folder and/or moves it (the parent is always sent; null is the top level). */
  update(orgId: string, id: string, name: string, parentId: string | null): Observable<Folder> {
    return this.http.put<Folder>(this.url(orgId, `/${id}`), { name, parentId });
  }

  /** Only an empty folder can be deleted. */
  remove(orgId: string, id: string): Observable<void> {
    return this.http.delete<void>(this.url(orgId, `/${id}`));
  }
}
