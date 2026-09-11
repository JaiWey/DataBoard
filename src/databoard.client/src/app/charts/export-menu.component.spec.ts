import { CUSTOM_ELEMENTS_SCHEMA, Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ECHARTS_LOADER } from './chart.component';
import { ChartType } from './charts.service';
import { ExportMenuComponent } from './export-menu.component';

const result = { columns: [{ name: 'region', dataType: 'text' }, { name: 'n', dataType: 'bigint' }], rows: [['Auckland', 3]], truncated: false, sql: null };

/** The menu sits inside a widget, next to the chart it takes the picture of. */
@Component({
  imports: [ExportMenuComponent],
  schemas: [CUSTOM_ELEMENTS_SCHEMA], // a stand-in <app-chart>: the real one needs a canvas
  template: `
    <div data-export-scope>
      <app-export-menu name="Revenue / region" [result]="result()" [type]="type()" />
      <app-chart><div class="chart"></div></app-chart>
    </div>
  `,
})
class HostComponent {
  readonly result = signal(result);
  readonly type = signal<ChartType>('Bar');
}

describe('ExportMenuComponent', () => {
  let fixture: ComponentFixture<HostComponent>;
  let saved: HTMLAnchorElement[];
  const getDataURL = vi.fn(() => 'data:image/png;base64,AAAA');
  const getInstanceByDom = vi.fn(() => ({ setOption() {}, resize() {}, dispose() {}, getDataURL }));

  const menuItems = () => Array.from(document.querySelectorAll('.mat-mdc-menu-item')) as HTMLButtonElement[];
  const openMenu = () => {
    (fixture.nativeElement.querySelector('button.export') as HTMLButtonElement).click();
    fixture.detectChanges();
  };

  beforeEach(() => {
    saved = [];
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
      saved.push(this);
    });
    (URL as unknown as Record<string, unknown>)['createObjectURL'] = vi.fn(() => 'blob:fake');
    (URL as unknown as Record<string, unknown>)['revokeObjectURL'] = vi.fn();
    getDataURL.mockClear();
    getInstanceByDom.mockClear();

    TestBed.configureTestingModule({
      providers: [{ provide: ECHARTS_LOADER, useValue: async () => ({ init: () => null, getInstanceByDom }) }],
    });
    fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
  });

  afterEach(() => {
    vi.restoreAllMocks();
    document.querySelectorAll('.cdk-overlay-container').forEach((c) => (c.innerHTML = ''));
  });

  it('offers the rows as CSV and, for a drawn chart, a PNG', () => {
    openMenu();

    expect(menuItems().map((i) => i.textContent?.trim())).toEqual(['Download CSV', 'Download PNG']);
  });

  for (const type of ['Table', 'Number'] as const) {
    it(`offers only the CSV for a ${type.toLowerCase()}, which is not drawn on a canvas`, () => {
      fixture.componentInstance.type.set(type);
      fixture.detectChanges();
      openMenu();

      expect(menuItems().map((i) => i.textContent?.trim())).toEqual(['Download CSV']);
    });
  }

  it('saves the rows under the chart name, with characters a file cannot hold removed', () => {
    openMenu();
    menuItems()[0].click();

    expect(saved.map((a) => a.download)).toEqual(['Revenue region.csv']);
  });

  it('saves a picture of the chart drawn next to it', async () => {
    openMenu();
    menuItems()[1].click();
    await vi.waitFor(() => expect(saved.length).toBe(1));

    expect(getInstanceByDom).toHaveBeenCalledWith(fixture.nativeElement.querySelector('app-chart .chart'));
    expect(getDataURL).toHaveBeenCalledWith({ type: 'png', pixelRatio: 2, backgroundColor: '#ffffff' });
    expect([saved[0].download, saved[0].href]).toEqual(['Revenue region.png', 'data:image/png;base64,AAAA']);
  });
});
