import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { BackgroundDialogComponent, BackgroundDialogData } from './background-dialog.component';
import { Dashboard } from './dashboards.service';

const ORG = '11111111-1111-1111-1111-111111111111';
const DASHBOARD = '22222222-2222-2222-2222-222222222222';
const BASE = `/api/organizations/${ORG}/dashboards/${DASHBOARD}/background`;

describe('BackgroundDialogComponent', () => {
  let fixture: ComponentFixture<BackgroundDialogComponent>;
  let http: HttpTestingController;
  let close: ReturnType<typeof vi.fn>;
  const el = () => fixture.nativeElement as HTMLElement;
  const field = <T extends HTMLElement>(label: string) => el().querySelector(`[aria-label="${label}"]`) as T;
  const button = (text: string) => Array.from(el().querySelectorAll('button')).find((b) => b.textContent?.trim() === text) as HTMLButtonElement | undefined;
  const dashboard = (background: Dashboard['background']): Dashboard => ({
    id: DASHBOARD, name: 'Sales', description: null, definition: { widgets: [], filters: [] }, createdUtc: '', updatedUtc: '', background,
  });

  const create = (background: BackgroundDialogData['background']) => {
    close = vi.fn();
    let n = 0;
    vi.spyOn(URL, 'createObjectURL').mockImplementation(() => `blob:preview-${++n}`);
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(), provideHttpClientTesting(),
        { provide: MAT_DIALOG_DATA, useValue: { orgId: ORG, dashboardId: DASHBOARD, dashboardName: 'Sales', background } satisfies BackgroundDialogData },
        { provide: MatDialogRef, useValue: { close } },
      ],
    });
    fixture = TestBed.createComponent(BackgroundDialogComponent);
    http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
  };

  /** Picks a file the way the browser's file dialog would. */
  const pick = (file: File) => {
    const input = field<HTMLInputElement>('Background picture');
    Object.defineProperty(input, 'files', { value: [file], configurable: true });
    input.dispatchEvent(new Event('change'));
    fixture.detectChanges();
  };
  const png = (size = 10) => new File([new Uint8Array(size)], 'logo.png', { type: 'image/png' });

  afterEach(() => {
    http.verify();
    vi.restoreAllMocks();
  });

  describe('a dashboard without a picture', () => {
    beforeEach(() => create(null));

    it('says there is no picture and cannot save yet', () => {
      expect(el().textContent).toContain('No picture');
      expect(button('Choose a picture')).toBeDefined();
      expect(button('Save')!.disabled).toBe(true);
      expect(button('Remove picture')).toBeUndefined();
      expect(field<HTMLSelectElement>('How the picture is shown').disabled).toBe(true);
    });

    it('previews the chosen picture and sends it with the way to show it', () => {
      pick(png());

      expect(el().querySelector('[role=img]')).not.toBeNull();
      expect(button('Choose another picture')).toBeDefined();
      field<HTMLSelectElement>('How the picture is shown').value = 'tile';
      field<HTMLSelectElement>('How the picture is shown').dispatchEvent(new Event('change'));
      field<HTMLInputElement>('Picture strength').value = '60';
      field<HTMLInputElement>('Picture strength').dispatchEvent(new Event('input'));
      fixture.detectChanges();
      button('Save')!.click();

      const request = http.expectOne({ method: 'PUT', url: BASE });
      const form = request.request.body as FormData;
      expect((form.get('File') as File).name).toBe('logo.png');
      expect(form.get('Fit')).toBe('tile');
      expect(form.get('Opacity')).toBe('60');
      const saved = dashboard({ version: 'abc', fit: 'tile', opacity: 60 });
      request.flush(saved);
      expect(close).toHaveBeenCalledWith(saved);
    });

    it.each([
      [new File(['<svg/>'], 'logo.svg', { type: 'image/svg+xml' }), 'PNG, JPEG, GIF or WebP'],
      [new File(['x'], 'notes.txt', { type: 'text/plain' }), 'PNG, JPEG, GIF or WebP'],
      [new File([new Uint8Array(5 * 1024 * 1024 + 1)], 'big.png', { type: 'image/png' }), 'at most 5 MB'],
    ])('refuses a picture that is the wrong kind or too big, before sending anything', (file, message) => {
      pick(file);

      expect(el().querySelector('[role=alert]')?.textContent).toContain(message);
      expect(button('Save')!.disabled).toBe(true);
    });

    it('stays open and shows the reason when the server refuses', () => {
      pick(png());
      button('Save')!.click();

      http.expectOne({ method: 'PUT', url: BASE }).flush(
        { title: 'Bad request', errors: { File: ['That is not a PNG, JPEG, GIF or WebP picture.'] } }, { status: 400, statusText: 'Bad Request' },
      );
      fixture.detectChanges();

      expect(close).not.toHaveBeenCalled();
      expect(el().querySelector('[role=alert]')?.textContent).toContain('not a PNG');
      expect(button('Save')!.disabled).toBe(false);
    });
  });

  describe('a dashboard with a picture', () => {
    beforeEach(() => {
      create({ version: 'v1', fit: 'contain', opacity: 70 });
      http.expectOne(`${BASE}?v=v1`).flush(new Blob(['x']));
      fixture.detectChanges();
    });

    it('shows the current picture and how it is shown', () => {
      expect(el().querySelector('[role=img]')).not.toBeNull();
      expect(field<HTMLSelectElement>('How the picture is shown').value).toBe('contain');
      expect(field<HTMLInputElement>('Picture strength').value).toBe('70');
      expect(button('Remove picture')).toBeDefined();
      expect(button('Save')!.disabled).toBe(true); // nothing changed
    });

    it('saves a change to how it is shown without sending the picture again', () => {
      field<HTMLSelectElement>('How the picture is shown').value = 'center';
      field<HTMLSelectElement>('How the picture is shown').dispatchEvent(new Event('change'));
      fixture.detectChanges();
      expect(button('Save')!.disabled).toBe(false);

      button('Save')!.click();

      const form = http.expectOne({ method: 'PUT', url: BASE }).request.body as FormData;
      expect(form.get('File')).toBeNull();
      expect(form.get('Fit')).toBe('center');
      expect(form.get('Opacity')).toBe('70');
    });

    it('removes the picture', () => {
      button('Remove picture')!.click();

      const saved = dashboard(null);
      http.expectOne({ method: 'DELETE', url: BASE }).flush(saved);

      expect(close).toHaveBeenCalledWith(saved);
    });

    it('cannot remove the picture while another one is chosen, only replace it', () => {
      pick(png());

      expect(button('Remove picture')).toBeUndefined();
      expect(button('Save')!.disabled).toBe(false);
    });
  });
});
