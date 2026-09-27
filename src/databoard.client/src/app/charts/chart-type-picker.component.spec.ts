import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ChartTypePickerComponent } from './chart-type-picker.component';
import { ChartType } from './charts.service';

describe('ChartTypePickerComponent', () => {
  let fixture: ComponentFixture<ChartTypePickerComponent>;
  let picked: ChartType[];
  const el = () => fixture.nativeElement as HTMLElement;
  const tabs = () => Array.from(el().querySelectorAll('[role=tab]')).map((t) => t.textContent?.trim());
  const types = () => Array.from(el().querySelectorAll('button.type')).map((b) => b.getAttribute('aria-label'));
  const tab = (name: string) => Array.from(el().querySelectorAll<HTMLButtonElement>('[role=tab]')).find((t) => t.textContent?.trim() === name)!;
  const type = (label: string) => el().querySelector<HTMLButtonElement>(`button.type[aria-label="${label}"]`)!;

  const create = (value: ChartType = 'Bar') => {
    fixture = TestBed.createComponent(ChartTypePickerComponent);
    picked = [];
    fixture.componentRef.setInput('value', value);
    fixture.componentInstance.pick.subscribe((t) => picked.push(t));
    fixture.detectChanges();
  };

  beforeEach(() => localStorage.clear());
  afterEach(() => vi.restoreAllMocks());

  it('has the tabs of the kinds of chart and shows every type under All to begin with', () => {
    create();

    expect(tabs()).toEqual(['All', 'Recent', 'Table', 'Bar Chart', 'Line Chart', 'Pie Chart', 'Scatter Chart', 'Heatmap', 'Score Card']);
    expect(types()).toEqual(['Table', 'Bar', 'Horizontal bar', 'Line', 'Area', 'Pie', 'Donut', 'Number', 'Heatmap', 'Scatter / bubble']);
    expect(tab('All').getAttribute('aria-selected')).toBe('true');
  });

  it('shows only the types of the kind chosen', () => {
    create();

    tab('Bar Chart').click();
    fixture.detectChanges();
    expect(types()).toEqual(['Bar', 'Horizontal bar']);
    expect(tab('Bar Chart').getAttribute('aria-selected')).toBe('true');
    expect(tab('All').getAttribute('aria-selected')).toBe('false');

    tab('Pie Chart').click();
    fixture.detectChanges();
    expect(types()).toEqual(['Pie', 'Donut']);
    tab('Line Chart').click();
    fixture.detectChanges();
    expect(types()).toEqual(['Line', 'Area']);
  });

  it('marks the type the chart has, and reports the one picked', () => {
    create('Donut');
    tab('Pie Chart').click();
    fixture.detectChanges();

    expect(type('Donut').getAttribute('aria-pressed')).toBe('true');
    expect(type('Pie').getAttribute('aria-pressed')).toBe('false');
    expect(type('Donut').classList).toContain('selected');

    type('Pie').click();

    expect(picked).toEqual(['Pie']);
  });

  it('shows a hint under Recent until something has been picked, then the types picked, newest first', () => {
    create();

    tab('Recent').click();
    fixture.detectChanges();
    expect(types()).toEqual([]);
    expect(el().textContent).toContain('The types you pick show up here.');

    tab('All').click();
    fixture.detectChanges();
    type('Pie').click();
    type('Heatmap').click();
    type('Pie').click();
    tab('Recent').click();
    fixture.detectChanges();

    expect(types()).toEqual(['Pie', 'Heatmap']);
  });

  it('remembers the tab and what was picked for the next chart', () => {
    create();
    tab('Pie Chart').click();
    fixture.detectChanges();
    type('Donut').click();
    TestBed.resetTestingModule();

    create();

    expect(tab('Pie Chart').getAttribute('aria-selected')).toBe('true');
    expect(types()).toEqual(['Pie', 'Donut']);
    tab('Recent').click();
    fixture.detectChanges();
    expect(types()).toEqual(['Donut']);
  });

  it('draws an icon for every type', () => {
    create();

    expect(el().querySelectorAll('button.type svg').length).toBe(10);
    expect(Array.from(el().querySelectorAll('button.type svg')).every((s) => s.children.length > 0)).toBe(true);
  });

  it('is still a working picker when the browser will not remember anything', () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => { throw new Error('blocked'); });
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('blocked'); });
    create();

    tab('Bar Chart').click();
    fixture.detectChanges();
    type('Bar').click();

    expect(types()).toEqual(['Bar', 'Horizontal bar']);
    expect(picked).toEqual(['Bar']);
  });
});
