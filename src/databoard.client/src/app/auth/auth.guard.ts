import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';
import { returnQuery } from './return-url';

export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  // After signing in, come back to the page that was asked for.
  return auth.isAuthenticated() ? true : inject(Router).createUrlTree(['/login'], { queryParams: returnQuery(state?.url) });
};
