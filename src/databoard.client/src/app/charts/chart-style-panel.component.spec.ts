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
      ['Bar', ['Title and axes', 'Colours', 'Numbers', 'X axis', 'Y axis', 'Split lines', 'Margin', 'Bars', 'Labels', 'Legend']],
      ['Line', ['Title and axes', 'Colours', 'Numbers', 'X axis', 'Y axis', 'Split lines', 'Margin', 'Labels', 'Legend', 'Look']],
      ['Pie', ['Title', 'Colours', 'Numbers', 'Labels', 'Legend', 'Pie']],
      ['Heatmap', ['Title and axes', 'Colours', 'Numbers', 'X axis', 'Y axis', 'Split lines', 'Margin', 'Labels']],
      ['Scatter', ['Title and axes', 'Colours', 'Numbers', 'X axis', 'Y axis', 'Split lines', 'Margin', 'Labels', 'Legend']],
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

  describe('the finer groups', () => {
    const box = (text: string) => Array.from(el().querySelectorAll('mat-checkbox')).find((c) => c.textContent?.trim() === text)!.querySelector('input') as HTMLInputElement;
    const select = (label: string, value: string) => {
      const s = field<HTMLSelectElement>(label);
      s.value = value;
      s.dispatchEvent(new Event('change'));
    };

    describe('axes', () => {
      it('reports what is turned off on an axis, keeping the rest of what was chosen for it', () => {
        create('Bar', { xAxis: { lineColor: '#111111' } });

        const first = Array.from(el().querySelectorAll('details')).find((d) => d.querySelector('summary')?.textContent?.trim() === 'X axis')!;
        const boxes = Array.from(first.querySelectorAll('mat-checkbox input')) as HTMLInputElement[];
        boxes[1].click(); // axis line
        boxes[3].click(); // labels

        expect(changes).toEqual([
          { xAxis: { lineColor: '#111111', line: false } },
          { xAxis: { lineColor: '#111111', labels: false } }, // the page gives back what it kept; this panel has not
        ]);
      });

      it('switches the other parts off while the axis is hidden', () => {
        create('Bar', { yAxis: { show: false } });
        fixture.detectChanges();

        const section = Array.from(el().querySelectorAll('details')).find((d) => d.querySelector('summary')?.textContent?.trim() === 'Y axis')!;
        const boxes = Array.from(section.querySelectorAll('mat-checkbox input')) as HTMLInputElement[];

        expect(boxes[0].checked).toBe(false);
        expect(boxes.slice(1, 4).map((b) => b.disabled)).toEqual([true, true, true]); // line, ticks and labels (the fonts have boxes of their own after)
      });

      it('has a line colour and fonts for the labels and the title of each axis', () => {
        create('Line');

        expect(field('X axis line colour')).not.toBeNull();
        expect(field('X axis label font')).not.toBeNull();
        expect(field('Y axis title font')).not.toBeNull();
        const colour = field<HTMLInputElement>('Y axis line colour');
        colour.value = '#e8590c';
        colour.dispatchEvent(new Event('input'));
        expect(changes).toEqual([{ yAxis: { lineColor: '#e8590c' } }]);
      });

      it('reports a font chosen for a label as part of its axis', () => {
        create('Bar');

        const size = field<HTMLInputElement>('X axis label font size');
        size.value = '10';
        size.dispatchEvent(new Event('input'));

        expect(changes).toEqual([{ xAxis: { labelFont: { size: 10 } } }]);
      });
    });

    describe('split lines', () => {
      it('shows what an axis has by default: the value axis, not the category axis', () => {
        create('Bar');
        const x = el().querySelector('details:has(summary)') as HTMLElement;
        void x;

        expect(box('X axis lines').checked).toBe(false);
        expect(box('Y axis lines').checked).toBe(true);
      });

      it('has them the other way round on a horizontal bar chart, on both axes of a scatter chart, and on neither of a heatmap', () => {
        create('BarHorizontal');
        expect([box('X axis lines').checked, box('Y axis lines').checked]).toEqual([true, false]);
        create('Scatter');
        expect([box('X axis lines').checked, box('Y axis lines').checked]).toEqual([true, true]);
        create('Heatmap');
        expect([box('X axis lines').checked, box('Y axis lines').checked]).toEqual([false, false]);
      });

      it('has the value axis lines off when Grid lines is off', () => {
        create('Bar', { grid: false });

        expect(box('Y axis lines').checked).toBe(false);
      });

      it('reports each choice, with the colour, width and style', () => {
        create('Bar');

        box('X axis lines').click();
        const width = field<HTMLInputElement>('Y split line width');
        width.value = '3';
        width.dispatchEvent(new Event('input'));
        select('Y split line style', 'dashed');
        const colour = field<HTMLInputElement>('X split line colour');
        colour.value = '#dee2e6';
        colour.dispatchEvent(new Event('input'));

        expect(changes).toEqual([
          { xSplitLine: { show: true } },
          { ySplitLine: { width: 3 } },
          { ySplitLine: { lineType: 'dashed' } },
          { xSplitLine: { color: '#dee2e6' } },
        ]);
      });

      it('takes the style back to the default', () => {
        create('Bar', { ySplitLine: { lineType: 'dotted' } });

        select('Y split line style', '');

        expect(changes).toEqual([{ ySplitLine: { lineType: null } }]);
      });
    });

    describe('margin', () => {
      it('reports a side, keeping the others, and clears a side that is emptied', () => {
        create('Bar', { margin: { left: 30 } });

        const top = field<HTMLInputElement>('top margin');
        top.value = '12';
        top.dispatchEvent(new Event('input'));
        const left = field<HTMLInputElement>('left margin');
        left.value = '';
        left.dispatchEvent(new Event('input'));

        expect(changes).toEqual([{ margin: { left: 30, top: 12 } }, { margin: { left: null } }]);
        expect(field<HTMLInputElement>('left margin').placeholder).toBe('auto');
      });

      it('keeps a side inside 0 to 300', () => {
        create('Bar');

        const right = field<HTMLInputElement>('right margin');
        right.value = '900';
        right.dispatchEvent(new Event('input'));

        expect(changes).toEqual([{ margin: { right: 300 } }]);
      });
    });

    describe('bars', () => {
      it('reports the border and the gaps', () => {
        create('Bar');

        select('Bar border style', 'dotted');
        const width = field<HTMLInputElement>('Bar border width');
        width.value = '2';
        width.dispatchEvent(new Event('input'));
        const gap = field<HTMLInputElement>('Bar gap');
        gap.value = '10';
        gap.dispatchEvent(new Event('input'));
        const group = field<HTMLInputElement>('Group gap');
        group.value = '';
        group.dispatchEvent(new Event('input'));
        const colour = field<HTMLInputElement>('Bar border colour');
        colour.value = '#c92a2a';
        colour.dispatchEvent(new Event('input'));

        expect(changes).toEqual([
          { barBorderStyle: 'dotted' }, { barBorderWidth: 2 }, { barGap: 10 }, { barCategoryGap: null }, { barBorderColor: '#c92a2a' },
        ]);
      });

      it('shows what a bar has now', () => {
        create('Bar', { barBorderStyle: 'dashed', barBorderWidth: 3, barBorderColor: '#c92a2a', barGap: 20, barCategoryGap: 40 });

        expect(field<HTMLSelectElement>('Bar border style').value).toBe('dashed');
        expect(field<HTMLInputElement>('Bar border width').value).toBe('3');
        expect(field<HTMLInputElement>('Bar border colour').value).toBe('#c92a2a');
        expect(field<HTMLInputElement>('Bar gap').value).toBe('20');
        expect(field<HTMLInputElement>('Group gap').value).toBe('40');
      });

      it('is for bars only', () => {
        create('Line');

        expect(field('Bar border style')).toBeNull();
        expect(field('Bar width')).toBeNull();
      });
    });

    describe('labels', () => {
      it('has a position for axis charts and a font for all, but no position for a pie', () => {
        create('Bar');
        expect(field('Label position')).not.toBeNull();
        expect(field('Label font')).not.toBeNull();
        create('Pie');
        expect(field('Label position')).toBeNull();
        expect(field('Label font')).not.toBeNull();
      });

      it('reports a position and a font', () => {
        create('Bar');

        select('Label position', 'insideTop');
        const size = field<HTMLInputElement>('Label font size');
        size.value = '14';
        size.dispatchEvent(new Event('input'));

        expect(changes).toEqual([{ labelPosition: 'insideTop' }, { labelFont: { size: 14 } }]);
      });
    });

    describe('legend', () => {
      it('reports its type, buttons, height and font', () => {
        create('Bar');

        select('Legend type', 'scroll');
        box('Select all and invert buttons').click();
        const height = field<HTMLInputElement>('Legend height');
        height.value = '120';
        height.dispatchEvent(new Event('input'));
        const size = field<HTMLInputElement>('Legend font size');
        size.value = '11';
        size.dispatchEvent(new Event('input'));

        expect(changes).toEqual([{ legendType: 'scroll' }, { legendSelector: true }, { legendHeight: 120 }, { legendFont: { size: 11 } }]);
      });

      it('has its position here and keeps a height inside 20 to 1000, emptied for automatic', () => {
        create('Bar');

        select('Legend position', 'left');
        const height = field<HTMLInputElement>('Legend height');
        height.value = '5';
        height.dispatchEvent(new Event('input'));
        height.value = '';
        height.dispatchEvent(new Event('input'));

        expect(changes).toEqual([{ legendPosition: 'left' }, { legendHeight: 20 }, { legendHeight: null }]);
      });

      it('is not offered for a heatmap, which has none', () => {
        create('Heatmap');

        expect(field('Legend type')).toBeNull();
      });
    });
  });});
