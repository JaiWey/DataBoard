import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ChartInstance, ChartComponent, ECHARTS_LOADER } from './chart.component';
import { ChartKind } from './chart-option';

@Component({
  imports: [ChartComponent],
  template: `<app-chart [kind]="kind()" [result]="result()" label="Revenue by region" />`,
})
class Host {
  kind = signal<ChartKind>('Bar');
  result = signal({ columns: [{ name: 'region' }, { name: 'revenue' }], rows: [['Auckland', 5]] as unknown[][] });
}

describe('ChartComponent', () => {
  const calls: string[] = [];
  const options: any[] = [];
  let initialised = 0;

  const instance: ChartInstance = {
    setOption: (option) => {
      calls.push('setOption');
      options.push(option);
    },
    resize: () => calls.push('resize'),
    dispose: () => calls.push('dispose'),
  };

  const settle = async (fixture: { detectChanges(): void }) => {
    fixture.detectChanges();
    await Promise.resolve();
    await Promise.resolve();
    fixture.detectChanges();
  };

  beforeEach(() => {
    calls.length = 0;
    options.length = 0;
    initialised = 0;
    TestBed.configureTestingModule({
      providers: [
        {
          provide: ECHARTS_LOADER,
          useValue: async () => ({
            init: () => {
              initialised++;
              return instance;
            },
          }),
        },
      ],
    });
  });

  it('creates one chart and describes it for screen readers', async () => {
    const fixture = TestBed.createComponent(Host);
    await settle(fixture);

    const el = fixture.nativeElement.querySelector('.chart') as HTMLElement;
    expect(el.getAttribute('role')).toBe('img');
    expect(el.getAttribute('aria-label')).toBe('Revenue by region');
    expect(initialised).toBe(1);
    expect(options[0].series[0].type).toBe('bar');
  });

  it('redraws on the same chart when the kind or the data change, replacing the old series', async () => {
    const fixture = TestBed.createComponent(Host);
    await settle(fixture);

    fixture.componentInstance.kind.set('Pie');
    await settle(fixture);

    expect(initialised).toBe(1);
    expect(options.at(-1).series[0].type).toBe('pie');
  });

  it('disposes the chart when the component is destroyed', async () => {
    const fixture = TestBed.createComponent(Host);
    await settle(fixture);

    fixture.destroy();

    expect(calls).toContain('dispose');
  });

  it('does not create a chart if it is destroyed before ECharts finished loading', async () => {
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    fixture.destroy();
    await Promise.resolve();
    await Promise.resolve();

    expect(initialised).toBe(0);
  });
});
