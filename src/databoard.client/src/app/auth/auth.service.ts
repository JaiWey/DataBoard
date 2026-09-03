import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Observable, catchError, finalize, map, of, shareReplay, switchMap, tap } from 'rxjs';

export interface User {
  id: string;
  email: string;
  displayName: string;
}

interface SessionResponse {
  accessToken: string;
  accessTokenExpiresUtc: string;
  user: User;
}

/**
 * Keeps the short-lived access token in memory only. The refresh token lives in an HttpOnly cookie
 * that scripts cannot read, so a page reload restores the session through /api/auth/refresh.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);

  private readonly _accessToken = signal<string | null>(null);
  private readonly _user = signal<User | null>(null);
  private refreshInFlight$: Observable<string | null> | null = null;

  readonly user = this._user.asReadonly();
  readonly isAuthenticated = computed(() => this._accessToken() !== null);

  get accessToken(): string | null {
    return this._accessToken();
  }

  login(email: string, password: string): Observable<User> {
    return this.http
      .post<SessionResponse>('/api/auth/login', { email, password })
      .pipe(
        tap((s) => this.setSession(s)),
        map((s) => s.user),
      );
  }

  register(email: string, password: string, displayName: string): Observable<User> {
    return this.http
      .post('/api/auth/register', { email, password, displayName })
      .pipe(switchMap(() => this.login(email, password)));
  }

  logout(): Observable<void> {
    return this.http.post<void>('/api/auth/logout', {}).pipe(
      catchError(() => of(undefined)),
      finalize(() => this.clear()),
    );
  }

  /** Returns the new access token, or null when there is no valid session. Concurrent calls share one request. */
  refresh(): Observable<string | null> {
    if (!this.refreshInFlight$) {
      this.refreshInFlight$ = this.http.post<SessionResponse>('/api/auth/refresh', {}).pipe(
        tap((s) => this.setSession(s)),
        map((s) => s.accessToken as string | null),
        catchError(() => {
          this.clear();
          return of(null);
        }),
        finalize(() => (this.refreshInFlight$ = null)),
        shareReplay(1),
      );
    }
    return this.refreshInFlight$;
  }

  clear(): void {
    this._accessToken.set(null);
    this._user.set(null);
  }

  private setSession(s: SessionResponse): void {
    this._accessToken.set(s.accessToken);
    this._user.set(s.user);
  }
}
