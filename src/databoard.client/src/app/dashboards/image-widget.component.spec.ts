import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { ImageWidgetComponent } from './image-widget.component';

describe('ImageWidgetComponent', () => {
  let fixture: ComponentFixture<ImageWidgetComponent>;
  const el = () => fixture.nativeElement as HTMLElement;
  const img = () => el().querySelector('img') as HTMLImageElement | null;

  beforeEach(() => {
    let n = 0;
    vi.spyOn(URL, 'createObjectURL').mockImplementation(() => `blob:picture-${++n}`);
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
  });

  afterEach(() => vi.restoreAllMocks());

  const create = (inputs: Partial<{ load: () => any; version: string; fit: string; alt: string | null; linkUrl: string | null }> = {}) => {
    fixture = TestBed.createComponent(ImageWidgetComponent);
    const all = { load: () => of(new Blob(['x'])), version: 'v1', ...inputs };
    for (const [name, value] of Object.entries(all)) fixture.componentRef.setInput(name, value);
    fixture.detectChanges();
  };

  it('shows the picture with its description', () => {
    create({ alt: 'Company logo' });

    expect(img()!.getAttribute('src')).toBe('blob:picture-1');
    expect(img()!.getAttribute('alt')).toBe('Company logo');
    expect(el().querySelector('a')).toBeNull();
  });

  it.each(['contain', 'cover', 'fill'])('fits the picture as %s', (fit) => {
    create({ fit });

    expect(img()!.style.objectFit).toBe(fit);
  });

  it('has an empty description, which hides the picture from screen readers, when none was given', () => {
    create();

    expect(img()!.getAttribute('alt')).toBe('');
  });

  it('makes the picture a link that opens in a new tab without giving the page away', () => {
    create({ linkUrl: ' https://example.com/about ', alt: 'About us' });

    const a = el().querySelector('a')!;
    expect(a.getAttribute('href')).toBe('https://example.com/about');
    expect(a.getAttribute('target')).toBe('_blank');
    expect(a.getAttribute('rel')).toBe('noopener noreferrer');
    expect(a.getAttribute('aria-label')).toBe('About us');
    expect(a.querySelector('img')).not.toBeNull();
  });

  it.each(['javascript:alert(1)', 'data:text/html,<script>1</script>', 'ftp://example.com', 'not a link', '   ', ''])(
    'never follows %j',
    (linkUrl) => {
      create({ linkUrl });

      expect(el().querySelector('a')).toBeNull();
      expect(img()).not.toBeNull();
    },
  );

  it('fetches again for a new version only, and lets go of the old picture', () => {
    const load = vi.fn(() => of(new Blob(['x'])));
    create({ load });

    fixture.componentRef.setInput('load', () => load()); // a template makes a new function every pass
    fixture.detectChanges();
    expect(load).toHaveBeenCalledTimes(1);

    fixture.componentRef.setInput('version', 'v2');
    fixture.detectChanges();
    expect(load).toHaveBeenCalledTimes(2);
    expect(img()!.getAttribute('src')).toBe('blob:picture-2');
    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:picture-1');
  });

  it('says so when the picture cannot be fetched, instead of showing a broken image', () => {
    create({ load: () => throwError(() => new Error('gone')) });

    expect(img()).toBeNull();
    expect(el().textContent).toContain('could not be shown');
  });
});
