import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { authInterceptor } from './auth.interceptor';
import { AuthService } from './auth.service';

const session = (token: string) => ({
  accessToken: token,
  accessTokenExpiresUtc: '2099-01-01T00:00:00Z',
  user: { id: '1', email: 'a@b.c', displayName: 'A' },
});

describe('authInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;
  let auth: AuthService;
  let router: Router;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        provideRouter([{ path: '**', children: [] }]),
      ],
    });
    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
    router = TestBed.inject(Router);
  });

  afterEach(() => httpMock.verify());

  const signIn = () => {
    auth.login('a@b.c', 'password-123456').subscribe();
    httpMock.expectOne('/api/auth/login').flush(session('token-1'));
  };

  it('adds the bearer token to API calls', () => {
    signIn();

    http.get('/api/organizations').subscribe();

    const req = httpMock.expectOne('/api/organizations');
    expect(req.request.headers.get('Authorization')).toBe('Bearer token-1');
    req.flush([]);
  });

  it('does not add a token to auth endpoints', () => {
    signIn();

    http.post('/api/auth/logout', {}).subscribe();

    const req = httpMock.expectOne('/api/auth/logout');
    expect(req.request.headers.has('Authorization')).toBe(false);
    req.flush(null);
  });

  it('refreshes once on 401 and retries the request with the new token', () => {
    signIn();
    let result: unknown;

    http.get('/api/organizations').subscribe((r) => (result = r));
    httpMock.expectOne('/api/organizations').flush(null, { status: 401, statusText: 'Unauthorized' });

    httpMock.expectOne('/api/auth/refresh').flush(session('token-2'));

    const retry = httpMock.expectOne('/api/organizations');
    expect(retry.request.headers.get('Authorization')).toBe('Bearer token-2');
    retry.flush(['ok']);
    expect(result).toEqual(['ok']);
  });

  it('shares a single refresh between simultaneous 401s', () => {
    signIn();

    http.get('/api/a').subscribe();
    http.get('/api/b').subscribe();
    httpMock.expectOne('/api/a').flush(null, { status: 401, statusText: 'Unauthorized' });
    httpMock.expectOne('/api/b').flush(null, { status: 401, statusText: 'Unauthorized' });

    httpMock.expectOne('/api/auth/refresh').flush(session('token-2'));

    httpMock.expectOne('/api/a').flush([]);
    httpMock.expectOne('/api/b').flush([]);
  });

  it('sends the user to /login and surfaces the error when refresh fails', () => {
    signIn();
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    let status: number | undefined;

    http.get('/api/organizations').subscribe({ error: (e) => (status = e.status) });
    httpMock.expectOne('/api/organizations').flush(null, { status: 401, statusText: 'Unauthorized' });
    httpMock.expectOne('/api/auth/refresh').flush(null, { status: 401, statusText: 'Unauthorized' });

    expect(status).toBe(401);
    expect(navigate).toHaveBeenCalledWith(['/login']);
    expect(auth.isAuthenticated()).toBe(false);
  });

  it('remembers the page that needed the sign-in, so signing in comes back to it', async () => {
    signIn();
    await router.navigateByUrl('/shared/T0KEN');
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);

    http.get('/api/shared/dashboards/T0KEN').subscribe({ error: () => undefined });
    httpMock.expectOne('/api/shared/dashboards/T0KEN').flush(null, { status: 401, statusText: 'Unauthorized' });
    httpMock.expectOne('/api/auth/refresh').flush(null, { status: 401, statusText: 'Unauthorized' });

    expect(navigate).toHaveBeenCalledWith(['/login'], { queryParams: { returnUrl: '/shared/T0KEN' } });
  });

  it('does not remember the sign-in pages themselves', async () => {
    signIn();
    await router.navigateByUrl('/register');
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);

    http.get('/api/organizations').subscribe({ error: () => undefined });
    httpMock.expectOne('/api/organizations').flush(null, { status: 401, statusText: 'Unauthorized' });
    httpMock.expectOne('/api/auth/refresh').flush(null, { status: 401, statusText: 'Unauthorized' });

    expect(navigate).toHaveBeenCalledWith(['/login']);
  });

  it('passes non-401 errors straight through without refreshing', () => {
    signIn();
    let status: number | undefined;

    http.get('/api/organizations').subscribe({ error: (e) => (status = e.status) });
    httpMock.expectOne('/api/organizations').flush(null, { status: 500, statusText: 'Server Error' });

    expect(status).toBe(500);
    httpMock.expectNone('/api/auth/refresh');
  });
});
