import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { CHART_TYPES, ChartsService, SaveChart, defaultChartStyle } from './charts.service';

const ORG = '11111111-1111-1111-1111-111111111111';

const payload = (): SaveChart => ({
  name: 'Revenue',
  description: null,
  datasetId: 'ds-1',
  type: 'Bar',
  definition: { query: { measures: [{ aggregation: 'Count' }] }, style: defaultChartStyle() },
});

describe('ChartsService', () => {
  let service: ChartsService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(ChartsService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('lists and gets charts of an organization', () => {
    service.list(ORG).subscribe();
    http.expectOne({ method: 'GET', url: `/api/organizations/${ORG}/charts` }).flush([]);

    service.get(ORG, 'c1').subscribe();
    http.expectOne({ method: 'GET', url: `/api/organizations/${ORG}/charts/c1` }).flush({});
  });

  it('creates with the whole chart as the body', () => {
    service.create(ORG, payload()).subscribe();

    const req = http.expectOne({ method: 'POST', url: `/api/organizations/${ORG}/charts` });
    expect(req.request.body).toEqual(payload());
    req.flush({});
  });

  it('updates, deletes and runs by id', () => {
    service.update(ORG, 'c1', payload()).subscribe();
    http.expectOne({ method: 'PUT', url: `/api/organizations/${ORG}/charts/c1` }).flush({});

    service.remove(ORG, 'c1').subscribe();
    http.expectOne({ method: 'DELETE', url: `/api/organizations/${ORG}/charts/c1` }).flush(null);

    service.run(ORG, 'c1').subscribe();
    http.expectOne({ method: 'POST', url: `/api/organizations/${ORG}/charts/c1/run` }).flush({});
  });

  it('knows the eleven chart types, each with a readable name', () => {
    expect(CHART_TYPES.map((t) => t.value)).toEqual(['Table', 'Bar', 'BarHorizontal', 'Line', 'Area', 'Pie', 'Donut', 'Number', 'Heatmap', 'Scatter', 'WordCloud']);
    expect(CHART_TYPES.find((t) => t.value === 'BarHorizontal')?.label).toBe('Horizontal bar');
  });
});
