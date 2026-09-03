import { HttpErrorResponse, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, switchMap, throwError } from 'rxjs';
import { AuthService } from './auth.service';

const isAuthEndpoint = (url: string) => url.startsWith('/api/auth/');
const isApi = (url: string) => url.startsWith('/api/');

const withToken = (req: HttpRequest<unknown>, token: string | null) =>
  token ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : req;

/** Adds the bearer token to API calls and, on a 401, refreshes once and retries before giving up. */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  if (!isApi(req.url) || isAuthEndpoint(req.url)) {
    return next(req);
  }

  return next(withToken(req, auth.accessToken)).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse) || error.status !== 401) {
        return throwError(() => error);
      }

      return auth.refresh().pipe(
        switchMap((token) => {
          if (!token) {
            router.navigate(['/login']);
            return throwError(() => error);
          }
          return next(withToken(req, token));
        }),
      );
    }),
  );
};
