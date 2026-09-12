import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

export type TokenScope = 'Read' | 'Write';

export interface ApiToken {
  id: string;
  name: string;
  scope: TokenScope;
  createdUtc: string;
  expiresUtc: string | null;
  lastUsedUtc: string | null;
  expired: boolean;
}

export interface ApiTokenCreated {
  id: string;
  name: string;
  scope: TokenScope;
  /** The secret. Shown once: the server keeps only a hash. */
  token: string;
  createdUtc: string;
  expiresUtc: string | null;
}

/** How long a new token lasts: a number of days, or explicitly forever. */
export type TokenLifetime = { expiresInDays: number } | { neverExpires: true };

/** Personal access tokens: how an AI assistant (or any MCP client) acts as the signed-in person, with no password. */
@Injectable({ providedIn: 'root' })
export class ApiTokensService {
  private readonly http = inject(HttpClient);

  list(): Observable<ApiToken[]> {
    return this.http.get<ApiToken[]>('/api/me/tokens');
  }

  create(name: string, scope: TokenScope, lifetime: TokenLifetime): Observable<ApiTokenCreated> {
    return this.http.post<ApiTokenCreated>('/api/me/tokens', { name, scope, ...lifetime });
  }

  revoke(id: string): Observable<void> {
    return this.http.delete<void>(`/api/me/tokens/${id}`);
  }
}
