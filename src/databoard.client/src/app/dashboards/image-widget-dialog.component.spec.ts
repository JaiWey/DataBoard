import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { Dashboard, DashboardWidget } from './dashboards.service';
import { ImageWidgetDialogComponent, ImageWidgetDialogData } from './image-widget-dialog.component';

const ORG = '11111111-1111-1111-1111-111111111111';
const DASH = '22222222-2222-2222-2222-222222222222';
const WIDGET = '33333333-3333-3333-3333-333333333333';
const BASE = `/api/organizations/${ORG}/dashboards/${DASH}`;

const picture = (over: Partial<DashboardWidget> = {}): DashboardWidget => ({
  id: WIDGET, chartId: '00000000-0000-0000-0000-000000000000', kind: 'Image', imageKey: 'k1', imageFit: 'contain', alt: 'Logo', linkUrl: null, ...over,
});
const dashboard = (...widgets: DashboardWidget[]): Dashboard => ({
  id: DASH, name: 'Sales', description: null, createdUtc: '', updatedUtc: '', definition: { widgets, filters: [] },
});

describe('ImageWidgetDialogComponent', () => {
  let fixture: ComponentFixture<ImageWidgetDialogComponent>;
  let http: HttpTestingController;
  let close: ReturnType<typeof vi.fn>;
  const el = () => fixture.nativeElement as HTMLElement;
  const field = <T extends HTMLElement>(label: string) => el().querySelector(`[aria-label="${label}"]`) as T;
  const button = (text: string) => Array.from(el().querySelectorAll('button')).find((b) => b.textContent?.trim() === text) as HTMLButtonElement;
  const type = (label: string, value: string) => {
    field<HTMLInputElement>(label).value = value;
    field<HTMLInputElement>(label).dispatchEvent(new Event('input'));
    fixture.detectChanges();
  };
  const png = (size = 10, name = 'logo.png', type = 'image/png') => new File([new Uint8Array(size)], name, { type });
  const pick = (file: File) => {
    const input = field<HTMLInputElement>('Picture file');
    Object.defineProperty(input, 'files', { value: [file], configurable: true });
    input.dispatchEvent(new Event('change'));
    fixture.detectChanges();
  };

  const create = (data: ImageWidgetDialogData) => {
    close = vi.fn();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(), provideHttpClientTesting(),
        { provide: MAT_DIALOG_DATA, useValue: data },
        { provide: MatDialogRef, useValue: { close } },
      ],
    });
    fixture = TestBed.createComponent(ImageWidgetDialogComponent);
    http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
  };

  afterEach(() => http.verify());

  describe('adding a picture', () => {
    beforeEach(() => create({ orgId: ORG, dashboard: dashboard() }));

    it('needs a picture before it can be saved', () => {
      expect(el().querySelector('h2')?.textContent).toContain('Add a picture');
      expect(button('Add picture').disabled).toBe(true);

      pick(png());

      expect(button('Add picture').disabled).toBe(false);
      expect(el().textContent).toContain('logo.png');
    });

    it('sends the picture with its fit, description and link', () => {
      pick(png());
      field<HTMLSelectElement>('How the picture fits').value = 'cover';
      field<HTMLSelectElement>('How the picture fits').dispatchEvent(new Event('change'));
      type('Picture description', ' Our logo ');
      type('Picture link', 'https://example.com');
      button('Add picture').click();

      const request = http.expectOne({ method: 'POST', url: `${BASE}/image-widgets` });
      const form = request.request.body as FormData;
      expect((form.get('File') as File).name).toBe('logo.png');
      expect(form.get('Fit')).toBe('cover');
      expect(form.get('Alt')).toBe('Our logo');
      expect(form.get('LinkUrl')).toBe('https://example.com');
      const saved = dashboard(picture());
      request.flush(saved);
      expect(close).toHaveBeenCalledWith(saved);
    });

    it.each([
      [png(10, 'logo.svg', 'image/svg+xml'), 'PNG, JPEG, GIF or WebP'],
      [png(5 * 1024 * 1024 + 1), 'at most 5 MB'],
    ])('refuses a picture of the wrong kind or too big, before sending anything', (file, message) => {
      pick(file);

      expect(el().querySelector('[role=alert]')?.textContent).toContain(message);
      expect(button('Add picture').disabled).toBe(true);
    });

    it.each(['javascript:alert(1)', 'ftp://example.com', 'example.com'])('will not take the link %j', (link) => {
      pick(png());
      type('Picture link', link);

      expect(el().textContent).toContain('must start with http');
      expect(button('Add picture').disabled).toBe(true);
    });

    it('stays open and shows the reason when the server refuses', () => {
      pick(png());
      button('Add picture').click();

      http.expectOne({ method: 'POST', url: `${BASE}/image-widgets` }).flush(
        { title: 'Bad request', errors: { Widgets: ['A dashboard can have at most 24 widgets.'] } }, { status: 400, statusText: 'Bad Request' },
      );
      fixture.detectChanges();

      expect(close).not.toHaveBeenCalled();
      expect(el().querySelector('[role=alert]')?.textContent).toContain('at most 24');
      expect(button('Add picture').disabled).toBe(false);
    });
  });

  describe('changing a picture', () => {
    const widget = picture({ linkUrl: 'https://example.com' });
    beforeEach(() => create({ orgId: ORG, dashboard: dashboard(widget), widget }));

    it('shows what the widget has and has nothing to save until something changes', () => {
      expect(el().querySelector('h2')?.textContent).toContain('Picture');
      expect(field<HTMLInputElement>('Picture description').value).toBe('Logo');
      expect(field<HTMLInputElement>('Picture link').value).toBe('https://example.com');
      expect(field<HTMLSelectElement>('How the picture fits').value).toBe('contain');
      expect(el().textContent).toContain('Keeping the current picture');
      expect(button('Save').disabled).toBe(true);
    });

    it('saves new settings with the dashboard, widget by widget, without touching the picture', () => {
      type('Picture description', 'New logo');
      type('Picture link', '');
      field<HTMLSelectElement>('How the picture fits').value = 'fill';
      field<HTMLSelectElement>('How the picture fits').dispatchEvent(new Event('change'));
      button('Save').click();

      const request = http.expectOne({ method: 'PUT', url: BASE });
      expect(request.request.body.definition.widgets).toEqual([{ ...widget, imageFit: 'fill', alt: 'New logo', linkUrl: null }]);
      expect(request.request.body.name).toBe('Sales');
      const saved = dashboard({ ...widget, imageFit: 'fill', alt: 'New logo', linkUrl: null });
      request.flush(saved);
      expect(close).toHaveBeenCalledWith(saved);
    });

    it('replaces the picture on its own when only the file changed', () => {
      pick(png(10, 'new.png'));
      button('Save').click();

      const request = http.expectOne({ method: 'PUT', url: `${BASE}/image-widgets/${WIDGET}/image` });
      expect(((request.request.body as FormData).get('File') as File).name).toBe('new.png');
      const saved = dashboard({ ...widget, imageKey: 'k2' });
      request.flush(saved);
      expect(close).toHaveBeenCalledWith(saved);
    });

    it('replaces the picture first and then saves the settings on the dashboard that came back', () => {
      pick(png(10, 'new.png'));
      type('Picture description', 'Brand new');
      button('Save').click();

      http.expectOne({ method: 'PUT', url: `${BASE}/image-widgets/${WIDGET}/image` }).flush(dashboard({ ...widget, imageKey: 'k2' }));
      const update = http.expectOne({ method: 'PUT', url: BASE });
      expect(update.request.body.definition.widgets[0]).toMatchObject({ imageKey: 'k2', alt: 'Brand new' });
      update.flush(dashboard({ ...widget, imageKey: 'k2', alt: 'Brand new' }));

      expect(close).toHaveBeenCalled();
    });

    it('stays open and shows the reason when the settings are refused', () => {
      type('Picture description', 'x');
      button('Save').click();

      http.expectOne({ method: 'PUT', url: BASE }).flush(
        { title: 'Bad request', errors: { 'Widgets[0]': ['A link must be an address starting with http:// or https://.'] } },
        { status: 400, statusText: 'Bad Request' },
      );
      fixture.detectChanges();

      expect(close).not.toHaveBeenCalled();
      expect(el().querySelector('[role=alert]')?.textContent).toContain('A link must be');
    });
  });
});
