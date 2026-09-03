import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { App } from './app';

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

  it('should show an error when the API is unreachable', () => {
    fixture.detectChanges();

    httpMock.expectOne('/api/health').error(new ProgressEvent('error'));
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).querySelector('#api-status')?.textContent;
    expect(text).toContain('not reachable');
  });
});
