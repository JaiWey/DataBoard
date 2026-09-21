import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ChartStylePanelComponent } from './chart-style-panel.component';
import { ChartStyle, ChartType, defaultChartStyle } from './charts.service';

describe('ChartStylePanelComponent', () => {
  let fixture: ComponentFixture<ChartStylePanelComponent>;
  let changes: Partial<ChartStyle>[];
  const el = () => fixture.nativeElement as HTMLElement;
  const field = <T extends HTMLElement>(label: string) => el().querySelector(`[aria-label="${label}"]`) as T;
  const summaries = () => Array.from(el().querySelectorAll('summary')).map((s) => s.textContent?.trim());
  const type = (input: HTMLInputElement, value: string) => {
    input.value = value;
    input.dispatchEvent(new Event('input'));
  };

  const create = (chartType: ChartType, style: Partial<ChartStyle> = {}, seriesNames: string[] = []) => {
    fixture = TestBed.createComponent(ChartStylePanelComponent);
    changes = [];
    fixture.componentRef.setInput('style', { ...defaultChartStyle(), ...style });
    fixture.componentRef.setInput('type', chartType);
    fixture.componentRef.setInput('seriesNames', seriesNames);
    fixture.componentInstance.changed.subscribe((c) => changes.push(c));
    fixture.detectChanges();
  };

  describe('which sections it offers', () => {
    it.each([
      ['Bar', ['Title and axes', 'Colours', 'Numbers', 'Look']],
      ['Line', ['Title and axes', 'Colours', 'Numbers', 'Look']],
      ['Pie', ['Title', 'Colours', 'Numbers', 'Look', 'Pie']],
      ['Heatmap', ['Title and axes', 'Colours', 'Numbers']],
      ['Scatter', ['Title and axes', 'Colours', 'Numbers', 'Look']],
      ['Number', ['Numbers']],
      ['Table', []],
    ] as [ChartType, string[]][])('for a %s chart', (chartType, expected) => {
      create(chartType);

      expect(summaries()).toEqual(expected);
    });

    it('offers bar choices only to bars, line choices to lines and areas, and area opacity only to areas', () => {
      create('Bar');
      expect([field('Bar width'), field('Line width'), field('Area opacity')].map((f) => !!f)).toEqual([true, false, false]);
      create('Line');
      expect([field('Bar width'), field('Line width'), field('Area opacity')].map((f) => !!f)).toEqual([false, true, false]);
      create('Area');
      expect([field('Bar width'), field('Line width'), field('Area opacity')].map((f) => !!f)).toEqual([false, true, true]);
    });

    it('offers rotating labels only where there is a category axis', () => {
      create('Bar');
      expect(field('Label rotation')).not.toBeNull();
      create('Scatter');
      expect(field('Label rotation')).toBeNull();
    });
  });

  describe('title and axes', () => {
    beforeEach(() => create('Bar'));

    it('reports a title, trimmed, and null when it is emptied', () => {
      type(field('Chart title'), '  Revenue ');
      type(field('Chart title'), '');

      expect(changes).toEqual([{ title: 'Revenue' }, { title: null }]);
    });

    it('reports axis titles, grid lines, limits and the rotation', () => {
      type(field('Horizontal axis title'), 'Region');
      type(field('Vertical axis title'), 'NZD');
      field<HTMLInputElement>('Axis minimum').value = '0';
      field<HTMLInputElement>('Axis minimum').dispatchEvent(new Event('input'));
      type(field('Axis maximum'), '');
      type(field('Label rotation'), '200');
      type(field('Label rotation'), '');
      el().querySelector<HTMLInputElement>('mat-checkbox input')!.click();

      expect(changes).toEqual([
        { xAxisTitle: 'Region' }, { yAxisTitle: 'NZD' }, { yMin: 0 }, { yMax: null }, { labelRotate: 90 }, { labelRotate: 0 }, { grid: false },
      ]);
    });

    it('switches the limits off for a 100% chart, whose axis is fixed', () => {
      create('Bar', { percent: true, stack: true });

      expect(field<HTMLInputElement>('Axis minimum').disabled).toBe(true);
      expect(field<HTMLInputElement>('Axis maximum').disabled).toBe(true);
    });
  });

  describe('colours', () => {
    it('adds colours that differ, changes one, and removes one', () => {
      create('Bar', { colors: ['#111111'] });

      el().querySelector<HTMLButtonElement>('button.add')!.click();
      const second = field<HTMLInputElement>('Colour 1');
      second.value = '#222222';
      second.dispatchEvent(new Event('input'));
      field<HTMLButtonElement>('Remove colour 1').click();

      expect(changes).toEqual([{ colors: ['#111111', '#e8590c'] }, { colors: ['#222222'] }, { colors: null }]);
    });

    it('stops at twelve colours', () => {
      create('Bar', { colors: Array.from({ length: 12 }, () => '#111111') });

      expect(el().querySelector('button.add')).toBeNull();
    });

    it('shows a short colour as a full one, which is what a colour input takes', () => {
      create('Bar', { colors: ['#19c'] });

      expect(field<HTMLInputElement>('Colour 1').value).toBe('#1199cc');
    });

    it('gives each series a colour: the one it has, else the one it would get', () => {
      create('Bar', { colors: ['#111111', '#222222'], seriesColors: { Auckland: '#2b8a3e' } }, ['Auckland', 'Wellington', 'Dunedin']);

      expect(field<HTMLInputElement>('Colour of Auckland').value).toBe('#2b8a3e');
      expect(field<HTMLInputElement>('Colour of Wellington').value).toBe('#222222');
      expect(field<HTMLInputElement>('Colour of Dunedin').value).toBe('#111111'); // the list starts over
      expect(field('Reset colour of Auckland')).not.toBeNull();
      expect(field('Reset colour of Wellington')).toBeNull();
    });

    it('reports a series colour and keeps the others, and resets one back to null when it was the last', () => {
      create('Bar', { seriesColors: { Auckland: '#2b8a3e' } }, ['Auckland', 'Wellington']);

      const wellington = field<HTMLInputElement>('Colour of Wellington');
      wellington.value = '#c92a2a';
      wellington.dispatchEvent(new Event('input'));
      field<HTMLButtonElement>('Reset colour of Auckland').click();

      expect(changes).toEqual([{ seriesColors: { Auckland: '#2b8a3e', Wellington: '#c92a2a' } }, { seriesColors: null }]);
    });

    it('offers no series colours when nothing is drawn', () => {
      create('Bar', {}, []);

      expect(el().querySelector('[aria-label="Colour of each series"]')).toBeNull();
    });
  });

  describe('numbers', () => {
    it('reports each choice together with the others already made', () => {
      create('Bar', { numberFormat: { decimals: 2, thousands: false } });

      const decimals = field<HTMLSelectElement>('Decimals');
      expect(decimals.value).toBe('2');
      decimals.value = '';
      decimals.dispatchEvent(new Event('change'));
      type(field('Number prefix'), '$');
      type(field('Number suffix'), '');

      expect(changes).toEqual([
        { numberFormat: { decimals: null, thousands: false } },
        { numberFormat: { decimals: 2, thousands: false, prefix: '$' } },
        { numberFormat: { decimals: 2, thousands: false, suffix: null } },
      ]);
    });

    it('shows an example of what the choices write', () => {
      create('Bar', { numberFormat: { decimals: 1, prefix: '$', compact: true } });

      expect(el().textContent).toContain('Example: $1.2M');
    });

    it('is offered for a Number chart too', () => {
      create('Number');

      expect(field('Number prefix')).not.toBeNull();
    });
  });

  describe('look and pie', () => {
    it('reports the legend position, bar width and corners, clamped to what is allowed', () => {
      create('Bar');

      const position = field<HTMLSelectElement>('Legend position');
      position.value = 'right';
      position.dispatchEvent(new Event('change'));
      type(field('Bar width'), '500');
      type(field('Bar width'), '');
      type(field('Bar corner radius'), '8');

      expect(changes).toEqual([{ legendPosition: 'right' }, { barWidth: 100 }, { barWidth: null }, { barRadius: 8 }]);
    });

    it('reports the line choices', () => {
      create('Line');

      type(field('Line width'), '4');
      const boxes = Array.from(el().querySelectorAll<HTMLInputElement>('mat-checkbox input'));
      boxes[boxes.length - 2].click(); // smooth
      boxes[boxes.length - 1].click(); // points

      expect(changes).toEqual([{ lineWidth: 4 }, { smooth: true }, { symbols: false }]);
    });

    it('reports the pie choices and shows the type\'s own hole size as a hint', () => {
      create('Donut');
      expect(field<HTMLInputElement>('Hole size').placeholder).toBe('45');

      type(field('Hole size'), '60');
      Array.from(el().querySelectorAll('mat-checkbox')).find((c) => c.textContent?.includes('Slice radius'))!.querySelector('input')!.click();
      const label = field<HTMLSelectElement>('Slice label');
      label.value = 'name-percent';
      label.dispatchEvent(new Event('change'));

      expect(changes).toEqual([{ innerRadius: 60 }, { rose: true }, { pieLabel: 'name-percent' }]);
    });
  });
});
