import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Subject, of, throwError } from 'rxjs';
import { BackgroundLayerComponent } from './background-layer.component';
import { BackgroundFit } from './dashboards.service';

describe('BackgroundLayerComponent', () => {
  let fixture: ComponentFixture<BackgroundLayerComponent>;
  let created: string[];
  let revoked: string[];
  const layer = () => (fixture.nativeElement as HTMLElement).querySelector('.layer') as HTMLElement;

  beforeEach(() => {
    created = [];
    revoked = [];
    let n = 0;
    vi.spyOn(URL, 'createObjectURL').mockImplementation(() => {
      const url = `blob:picture-${++n}`;
      created.push(url);
      return url;
    });
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation((url) => void revoked.push(url));
  });

  afterEach(() => vi.restoreAllMocks());

  const create = (load: () => any, version = 'v1', fit: BackgroundFit = 'cover', opacity = 100) => {
    fixture = TestBed.createComponent(BackgroundLayerComponent);
    fixture.componentRef.setInput('load', load);
    fixture.componentRef.setInput('version', version);
    fixture.componentRef.setInput('fit', fit);
    fixture.componentRef.setInput('opacity', opacity);
    fixture.detectChanges();
  };

  it('shows the picture it fetched, behind everything and out of the way of clicks', () => {
    create(() => of(new Blob(['x'])));

    expect(layer().style.backgroundImage).toContain('blob:picture-1');
    expect(layer().getAttribute('aria-hidden')).toBe('true');
  });

  it.each([
    ['cover', 'cover', 'no-repeat'],
    ['contain', 'contain', 'no-repeat'],
    ['center', 'auto', 'no-repeat'],
    ['tile', 'auto', 'repeat'],
  ] as [BackgroundFit, string, string][])('shows a picture set to %s', (fit, size, repeat) => {
    create(() => of(new Blob(['x'])), 'v1', fit, 40);

    expect(layer().style.backgroundSize).toBe(size);
    expect(layer().style.backgroundRepeat).toBe(repeat);
    expect(layer().style.opacity).toBe('0.4');
  });

  it('fetches again when the version changes, and lets go of the old picture', () => {
    const load = vi.fn(() => of(new Blob(['x'])));
    create(load, 'v1');

    fixture.componentRef.setInput('version', 'v2');
    fixture.detectChanges();

    expect(load).toHaveBeenCalledTimes(2);
    expect(created).toEqual(['blob:picture-1', 'blob:picture-2']);
    expect(revoked).toEqual(['blob:picture-1']);
    expect(layer().style.backgroundImage).toContain('blob:picture-2');
  });

  it('does not fetch again when it is given a new function to fetch with, as a template does on every pass', () => {
    const load = vi.fn(() => of(new Blob(['x'])));
    create(load);

    fixture.componentRef.setInput('load', () => load());
    fixture.detectChanges();
    fixture.componentRef.setInput('load', () => load());
    fixture.detectChanges();

    expect(load).toHaveBeenCalledTimes(1);
  });

  it('does not fetch again when only the look changes', () => {
    const load = vi.fn(() => of(new Blob(['x'])));
    create(load);

    fixture.componentRef.setInput('fit', 'tile');
    fixture.componentRef.setInput('opacity', 50);
    fixture.detectChanges();

    expect(load).toHaveBeenCalledTimes(1);
  });

  it('shows nothing when the picture cannot be fetched', () => {
    create(() => throwError(() => new Error('gone')));

    expect(layer().style.backgroundImage).toBe('');
  });

  it('ignores a picture that arrives after it was replaced', () => {
    const slow = new Subject<Blob>();
    const load = vi.fn().mockReturnValueOnce(slow).mockReturnValueOnce(of(new Blob(['new'])));
    create(load, 'v1');

    fixture.componentRef.setInput('version', 'v2');
    fixture.detectChanges();
    slow.next(new Blob(['old']));
    fixture.detectChanges();

    expect(created).toEqual(['blob:picture-1']);
    expect(layer().style.backgroundImage).toContain('blob:picture-1');
  });

  it('lets go of the picture when it goes away', () => {
    create(() => of(new Blob(['x'])));

    fixture.destroy();

    expect(revoked).toEqual(['blob:picture-1']);
  });
});
