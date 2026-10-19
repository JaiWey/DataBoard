import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, of, shareReplay } from 'rxjs';

export interface AuthProviders {
  gitHub?: boolean;
  guest?: boolean;
}

/** What the server offers besides a password. Asked once, however many parts of the sign-in page want to know. */
@Injectable({ providedIn: 'root' })
export class AuthProvidersService {
  private readonly http = inject(HttpClient);
  private providers$: Observable<AuthProviders> | null = null;

  /** Not knowing is the same as not offering: a failed call gives an empty answer. */
  load(): Observable<AuthProviders> {
    return (this.providers$ ??= this.http.get<AuthProviders>('/api/auth/providers').pipe(
      catchError(() => of<AuthProviders>({})),
      shareReplay(1),
    ));
  }
}
