import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { App } from './app';
import { LoadingService, QUIET_REQUEST, SHOW_AFTER_MS } from './shared/loading.service';

describe('App', () => {
  let fixture: ComponentFixture<App>;
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App, HttpClientTestingModule],
      providers: [provideRouter([])],
    }).compileComponents();

    fixture = TestBed.createComponent(App);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should show API status when the health check succeeds', () => {
    fixture.detectChanges();

    const req = httpMock.expectOne('/api/health');
    expect(req.request.method).toEqual('GET');
    req.flush({ status: 'Healthy', timeUtc: '2026-10-07T00:00:00Z' });
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).querySelector('#api-status')?.textContent;
    expect(text).toContain('Healthy');
  });

  it('has the loading bar, which shows only when something has been waiting a while', () => {
    vi.useFakeTimers();
    try {
      fixture.detectChanges();
      httpMock.expectOne('/api/health').flush({ status: 'Healthy', timeUtc: '' });
      const loading = TestBed.inject(LoadingService);
      const root = fixture.nativeElement as HTMLElement;
      expect(root.querySelector('app-loading-bar')).not.toBeNull();
      expect(root.querySelector('mat-progress-bar')).toBeNull();

      loading.start();
      vi.advanceTimersByTime(SHOW_AFTER_MS);
      fixture.detectChanges();
      expect(root.querySelector('mat-progress-bar')).not.toBeNull();
      loading.stop();
    } finally {
      vi.useRealTimers();
    }
  });

  it('does not show a loading bar for its own status check', () => {
    fixture.detectChanges();

    const request = httpMock.expectOne('/api/health');

    expect(request.request.context.get(QUIET_REQUEST)).toBe(true);
    request.flush({ status: 'Healthy', timeUtc: '' });
  });

  it('should show an error when the API is unreachable', () => {
    fixture.detectChanges();

    httpMock.expectOne('/api/health').error(new ProgressEvent('error'));
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).querySelector('#api-status')?.textContent;
    expect(text).toContain('not reachable');
  });
});
