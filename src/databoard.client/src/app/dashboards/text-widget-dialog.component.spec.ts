import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { Dashboard, DashboardWidget } from './dashboards.service';
import { TextWidgetDialogComponent, TextWidgetDialogData } from './text-widget-dialog.component';

const ORG = '11111111-1111-1111-1111-111111111111';
const DASH = '22222222-2222-2222-2222-222222222222';
const EMPTY = '00000000-0000-0000-0000-000000000000';
const URL_ = `/api/organizations/${ORG}/dashboards/${DASH}`;

const chart = (id: string, y: number, h: number): DashboardWidget => ({ id, chartId: `c-${id}`, layout: { x: 0, y, w: 6, h } });
const words = (over: Partial<DashboardWidget> = {}): DashboardWidget => ({
  id: 'w-text', chartId: EMPTY, kind: 'Text', text: 'Hello', textAlign: 'left', textSize: 'normal', layout: { x: 0, y: 5, w: 12, h: 2 }, ...over,
});
const dashboard = (...widgets: DashboardWidget[]): Dashboard => ({
  id: DASH, name: 'Sales', description: 'About', createdUtc: '', updatedUtc: '', definition: { widgets, filters: [] },
});

describe('TextWidgetDialogComponent', () => {
  let fixture: ComponentFixture<TextWidgetDialogComponent>;
  let http: HttpTestingController;
  let close: ReturnType<typeof vi.fn>;
  const el = () => fixture.nativeElement as HTMLElement;
  const field = <T extends HTMLElement>(label: string) => el().querySelector(`[aria-label="${label}"]`) as T;
  const button = (text: string) => Array.from(el().querySelectorAll('button')).find((b) => b.textContent?.trim() === text) as HTMLButtonElement;
  const write = (value: string) => {
    field<HTMLTextAreaElement>('Text').value = value;
    field<HTMLTextAreaElement>('Text').dispatchEvent(new Event('input'));
    fixture.detectChanges();
  };
  const choose = (label: string, value: string) => {
    field<HTMLSelectElement>(label).value = value;
    field<HTMLSelectElement>(label).dispatchEvent(new Event('change'));
    fixture.detectChanges();
  };

  const create = (data: TextWidgetDialogData) => {
    close = vi.fn();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(), provideHttpClientTesting(),
        { provide: MAT_DIALOG_DATA, useValue: data },
        { provide: MatDialogRef, useValue: { close } },
      ],
    });
    fixture = TestBed.createComponent(TextWidgetDialogComponent);
    http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
  };

  afterEach(() => http.verify());

  describe('adding text', () => {
    beforeEach(() => create({ orgId: ORG, dashboard: dashboard(chart('a', 0, 5), chart('b', 2, 6)) }));

    it('needs some text, and shows a preview of it as it is written', () => {
      expect(el().querySelector('h2')?.textContent).toContain('Add text');
      expect(button('Add text').disabled).toBe(true);
      expect(el().querySelector('.preview')?.textContent).toContain('Nothing to show yet');

      write('# Hello\nSome **words**');

      expect(button('Add text').disabled).toBe(false);
      expect(el().querySelector('.preview h3')?.textContent).toBe('Hello');
      expect(el().querySelector('.preview strong')?.textContent).toBe('words');
      expect(el().querySelector('.count')?.textContent).toContain('22 / 4000');
    });

    it('will not take only spaces', () => {
      write('   \n  ');

      expect(button('Add text').disabled).toBe(true);
    });

    it('adds the widget below everything else across the whole width, by saving the dashboard', () => {
      write(' ## Section ');
      choose('Text alignment', 'center');
      choose('Text size', 'large');
      button('Add text').click();

      const request = http.expectOne({ method: 'PUT', url: URL_ });
      expect(request.request.body.name).toBe('Sales');
      expect(request.request.body.description).toBe('About');
      const widgets = request.request.body.definition.widgets;
      expect(widgets.length).toBe(3);
      expect(widgets[2]).toEqual({
        id: '00000000-0000-0000-0000-000000000000', chartId: EMPTY, kind: 'Text', text: '## Section', textAlign: 'center', textSize: 'large',
        layout: { x: 0, y: 11, w: 12, h: 2 }, // the second chart overlaps the first, so the page pushes it down to row 5; it ends at row 11
      });
      const saved = dashboard(chart('a', 0, 5), words({ text: '## Section' }));
      request.flush(saved);
      expect(close).toHaveBeenCalledWith(saved);
    });

    it('stays open and shows the reason when the server refuses', () => {
      write('x');
      button('Add text').click();

      http.expectOne({ method: 'PUT', url: URL_ }).flush(
        { title: 'Bad request', errors: { Widgets: ['A dashboard can have at most 24 widgets.'] } }, { status: 400, statusText: 'Bad Request' },
      );
      fixture.detectChanges();

      expect(close).not.toHaveBeenCalled();
      expect(el().querySelector('[role=alert]')?.textContent).toContain('at most 24');
      expect(button('Add text').disabled).toBe(false);
    });
  });

  describe('adding text to an empty dashboard', () => {
    it('puts it at the top', () => {
      create({ orgId: ORG, dashboard: dashboard() });

      write('Notes');
      button('Add text').click();

      const request = http.expectOne({ method: 'PUT', url: URL_ });
      expect(request.request.body.definition.widgets[0].layout).toEqual({ x: 0, y: 0, w: 12, h: 2 });
      request.flush(dashboard());
    });
  });

  describe('changing text', () => {
    const widget = words({ text: 'Hello', textAlign: 'right', textSize: 'small' });
    beforeEach(() => create({ orgId: ORG, dashboard: dashboard(chart('a', 0, 5), widget), widget }));

    it('shows what the widget has and has nothing to save until something changes', () => {
      expect(el().querySelector('h2')?.textContent).toContain('Text');
      expect(field<HTMLTextAreaElement>('Text').value).toBe('Hello');
      expect(field<HTMLSelectElement>('Text alignment').value).toBe('right');
      expect(field<HTMLSelectElement>('Text size').value).toBe('small');
      expect(button('Save').disabled).toBe(true);
    });

    it('saves the change in place, leaving the other widgets and the place of this one alone', () => {
      write('Goodbye');
      choose('Text alignment', 'left');
      button('Save').click();

      const request = http.expectOne({ method: 'PUT', url: URL_ });
      const widgets = request.request.body.definition.widgets;
      expect(widgets[0]).toEqual(chart('a', 0, 5));
      expect(widgets[1]).toEqual({ ...widget, text: 'Goodbye', textAlign: 'left' });
      request.flush(dashboard());
      expect(close).toHaveBeenCalled();
    });

    it('cannot be emptied', () => {
      write('');

      expect(button('Save').disabled).toBe(true);
    });
  });
});
