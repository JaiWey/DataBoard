import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

export type MemberRole = 'Viewer' | 'Editor' | 'Owner';

/** The roles, lowest first, with what each can do (shown next to the choice). */
export const MEMBER_ROLES: { value: MemberRole; label: string; hint: string }[] = [
  { value: 'Viewer', label: 'Viewer', hint: 'Looks at dashboards and charts.' },
  { value: 'Editor', label: 'Editor', hint: 'Also builds charts, dashboards and datasets, and adds data sources.' },
  { value: 'Owner', label: 'Owner', hint: 'Also manages the people in the organization.' },
];

export interface Member {
  userId: string;
  email: string;
  displayName: string;
  role: MemberRole;
  joinedUtc: string;
}

/** The people of an organization. Seeing them is open to every member; the rest is for Owners (and leaving, for anyone). */
@Injectable({ providedIn: 'root' })
export class MembersService {
  private readonly http = inject(HttpClient);

  private url(orgId: string, userId?: string): string {
    return `/api/organizations/${orgId}/members` + (userId ? `/${userId}` : '');
  }

  list(orgId: string): Observable<Member[]> {
    return this.http.get<Member[]>(this.url(orgId));
  }

  /** The person must already have an account. */
  add(orgId: string, email: string, role: MemberRole): Observable<void> {
    return this.http.post<void>(this.url(orgId), { email, role });
  }

  changeRole(orgId: string, userId: string, role: MemberRole): Observable<void> {
    return this.http.put<void>(this.url(orgId, userId), { role });
  }

  /** Removes someone, or, with your own id, leaves. */
  remove(orgId: string, userId: string): Observable<void> {
    return this.http.delete<void>(this.url(orgId, userId));
  }
}
