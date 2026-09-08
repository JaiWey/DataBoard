import { TestBed } from '@angular/core/testing';
import { ECHARTS_LOADER } from './chart.component';
import { ChartViewComponent } from './chart-view.component';
import { ChartType } from './charts.service';

const result = (columns: string[], rows: unknown[][]) => ({ columns: columns.map((name) => ({ name })), rows });

describe('ChartViewComponent', () => {
  const setup = (type: ChartType, r = result(['region', 'revenue'], [['Auckland', 2480], [null, 5]])) => {
    TestBed.configureTestingModule({
      providers: [{ provide: ECHARTS_LOADER, useValue: async () => ({ init: () => ({ setOption() {}, resize() {}, dispose() {} }) }) }],
    });
    const fixture = TestBed.createComponent(ChartViewComponent);
    fixture.componentRef.setInput('type', type);
    fixture.componentRef.setInput('result', r);
    fixture.componentRef.setInput('label', 'Revenue');
    fixture.detectChanges();
    return { fixture, el: fixture.nativeElement as HTMLElement };
  };

  it('shows a table with a dash for missing values', () => {
    const { el } = setup('Table');

    expect(Array.from(el.querySelectorAll('th')).map((c) => c.textContent)).toEqual(['region', 'revenue']);
    expect(Array.from(el.querySelectorAll('td')).map((c) => c.textContent)).toEqual(['Auckland', '2480', '∅', '5']);
    expect(el.querySelector('app-chart')).toBeNull();
  });

  it.each(['Bar', 'BarHorizontal', 'Line', 'Area', 'Pie', 'Donut'] as ChartType[])('draws %s with ECharts', (type) => {
    const { el } = setup(type);

    expect(el.querySelector('app-chart')).not.toBeNull();
    expect(el.querySelector('table')).toBeNull();
  });

  it('shows a number as one big, readable value with the name of the metric', () => {
    const { el } = setup('Number', result(['sum_price'], [[1234567]]));

    expect(el.querySelector('.value')?.textContent).toBe((1234567).toLocaleString());
    expect(el.querySelector('.caption')?.textContent).toBe('sum_price');
    expect(el.querySelector('[role=img]')?.getAttribute('aria-label')).toContain('Revenue');
    expect(el.querySelector('app-chart')).toBeNull();
  });

  it('takes the last column of the first row as the number, shows text as it is and a missing value as a dash', () => {
    expect(setup('Number', result(['a', 'b'], [['x', 7]])).el.querySelector('.value')?.textContent).toBe('7');
    TestBed.resetTestingModule();
    expect(setup('Number', result(['a'], [['n/a']])).el.querySelector('.value')?.textContent).toBe('n/a');
    TestBed.resetTestingModule();
    expect(setup('Number', result(['a'], [[null]])).el.querySelector('.value')?.textContent).toBe('∅');
  });

  it('says there is no data instead of drawing an empty chart', () => {
    for (const type of ['Table', 'Bar', 'Number'] as ChartType[]) {
      TestBed.resetTestingModule();
      const { el } = setup(type, result(['a'], []));

      expect(el.textContent).toContain('No data.');
      expect(el.querySelector('app-chart, table, .number')).toBeNull();
    }
  });
});
