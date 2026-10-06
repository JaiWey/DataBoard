import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { LoadingBarComponent } from './loading-bar.component';
import { LoadingService, QUIET_REQUEST, SHOW_AFTER_MS, SHOW_AT_LEAST_MS, loadingInterceptor, quietly } from './loading.service';

describe('LoadingService', () => {
  let loading: LoadingService;

  beforeEach(() => {
    vi.useFakeTimers();
    loading = TestBed.inject(LoadingService);
  });
  afterEach(() => vi.useRealTimers());

  it('shows nothing for a request that is quick', () => {
    loading.start();
    vi.advanceTimersByTime(SHOW_AFTER_MS - 1);
    loading.stop();
    vi.advanceTimersByTime(5_000);

    expect(loading.visible()).toBe(false);
    expect(loading.pending()).toBe(0);
  });

  it('shows the bar once a request has waited a little, and counts what is waiting', () => {
    loading.start();
    expect(loading.pending()).toBe(1);
    expect(loading.visible()).toBe(false);

    vi.advanceTimersByTime(SHOW_AFTER_MS);

    expect(loading.visible()).toBe(true);
  });

  it('keeps the bar for a minimum time after it was shown, so it does not blink', () => {
    loading.start();
    vi.advanceTimersByTime(SHOW_AFTER_MS);
    vi.advanceTimersByTime(40); // the answer arrives a moment after the bar appeared
    loading.stop();

    expect(loading.visible()).toBe(true);
    vi.advanceTimersByTime(SHOW_AT_LEAST_MS - 41);
    expect(loading.visible()).toBe(true);
    vi.advanceTimersByTime(2);
    expect(loading.visible()).toBe(false);
  });

  it('goes away at once when the request took longer than the minimum', () => {
    loading.start();
    vi.advanceTimersByTime(SHOW_AFTER_MS + SHOW_AT_LEAST_MS + 100);
    expect(loading.visible()).toBe(true);

    loading.stop();
    vi.advanceTimersByTime(0);

    expect(loading.visible()).toBe(false);
  });

  it('stays while any of several requests is still waiting', () => {
    loading.start();
    loading.start();
    vi.advanceTimersByTime(SHOW_AFTER_MS + SHOW_AT_LEAST_MS);
    loading.stop();
    vi.advanceTimersByTime(1_000);
    expect(loading.visible()).toBe(true);
    expect(loading.pending()).toBe(1);

    loading.stop();
    vi.advanceTimersByTime(0);
    expect(loading.visible()).toBe(false);
  });

  it('keeps a bar that was about to go away when something new starts waiting', () => {
    loading.start();
    vi.advanceTimersByTime(SHOW_AFTER_MS);
    loading.stop(); // would go away after the minimum time
    loading.start();
    vi.advanceTimersByTime(SHOW_AT_LEAST_MS + 500);

    expect(loading.visible()).toBe(true);
    loading.stop();
    vi.advanceTimersByTime(SHOW_AT_LEAST_MS);
    expect(loading.visible()).toBe(false);
  });

  it('never counts below zero, and shows nothing for a stop that had no start', () => {
    loading.stop();
    loading.stop();
    vi.advanceTimersByTime(5_000);

    expect(loading.pending()).toBe(0);
    expect(loading.visible()).toBe(false);
    loading.start();
    expect(loading.pending()).toBe(1);
  });

  it('shows the bar again for the next slow request', () => {
    loading.start();
    vi.advanceTimersByTime(SHOW_AFTER_MS + SHOW_AT_LEAST_MS);
    loading.stop();
    vi.advanceTimersByTime(0);
    expect(loading.visible()).toBe(false);

    loading.start();
    vi.advanceTimersByTime(SHOW_AFTER_MS);
    expect(loading.visible()).toBe(true);
  });
});

describe('loadingInterceptor', () => {
  let http: HttpClient;
  let backend: HttpTestingController;
  let loading: LoadingService;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(withInterceptors([loadingInterceptor])), provideHttpClientTesting()] });
    http = TestBed.inject(HttpClient);
    backend = TestBed.inject(HttpTestingController);
    loading = TestBed.inject(LoadingService);
  });
  afterEach(() => backend.verify());

  it('counts a request while it waits and stops counting when it is answered', () => {
    http.get('/api/a').subscribe();
    expect(loading.pending()).toBe(1);

    backend.expectOne('/api/a').flush([]);

    expect(loading.pending()).toBe(0);
  });

  it('stops counting when the request fails', () => {
    http.get('/api/a').subscribe({ error: () => undefined });

    backend.expectOne('/api/a').flush({}, { status: 500, statusText: 'Server Error' });

    expect(loading.pending()).toBe(0);
  });

  it('stops counting when the request is cancelled', () => {
    const subscription = http.get('/api/a').subscribe();
    expect(loading.pending()).toBe(1);

    subscription.unsubscribe();

    expect(loading.pending()).toBe(0);
    expect(backend.expectOne('/api/a').cancelled).toBe(true);
  });

  it('counts several requests at once', () => {
    http.get('/api/a').subscribe();
    http.post('/api/b', {}).subscribe();
    expect(loading.pending()).toBe(2);

    backend.expectOne('/api/a').flush([]);
    expect(loading.pending()).toBe(1);
    backend.expectOne('/api/b').flush({});
    expect(loading.pending()).toBe(0);
  });

  it('does not count a quiet request', () => {
    http.get('/api/a', { context: quietly() }).subscribe();

    expect(loading.pending()).toBe(0);
    const request = backend.expectOne('/api/a');
    expect(request.request.context.get(QUIET_REQUEST)).toBe(true);
    request.flush([]);
  });

  it('counts a request that does not say it is quiet', () => {
    http.get('/api/a').subscribe();

    expect(backend.expectOne('/api/a').request.context.get(QUIET_REQUEST)).toBe(false);
    loading.stop();
  });
});

describe('LoadingBarComponent', () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it('is a progress bar only while the loading service says so', () => {
    const fixture = TestBed.createComponent(LoadingBarComponent);
    const loading = TestBed.inject(LoadingService);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('mat-progress-bar')).toBeNull();

    loading.start();
    vi.advanceTimersByTime(SHOW_AFTER_MS);
    fixture.detectChanges();
    const bar = fixture.nativeElement.querySelector('mat-progress-bar') as HTMLElement;
    expect(bar).not.toBeNull();
    expect(bar.getAttribute('aria-label')).toBe('Loading');
    expect(bar.getAttribute('role')).toBe('progressbar');
    expect(bar.classList).toContain('no-print');

    loading.stop();
    vi.advanceTimersByTime(SHOW_AT_LEAST_MS);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('mat-progress-bar')).toBeNull();
  });
});
