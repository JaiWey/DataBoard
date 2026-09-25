import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FontOptions } from './chart-option';
import { ColorFieldComponent, FontFieldsComponent } from './style-fields.component';

describe('ColorFieldComponent', () => {
  let fixture: ComponentFixture<ColorFieldComponent>;
  const el = () => fixture.nativeElement as HTMLElement;
  const input = () => el().querySelector('input') as HTMLInputElement;

  const create = (value: string | null, label = 'Border colour') => {
    fixture = TestBed.createComponent(ColorFieldComponent);
    fixture.componentRef.setInput('label', label);
    fixture.componentRef.setInput('value', value);
    fixture.detectChanges();
  };

  it('says it is the default while no colour is chosen, with nothing to reset', () => {
    create(null);

    expect(el().textContent).toContain('default');
    expect(el().querySelector('button')).toBeNull();
    expect(input().getAttribute('aria-label')).toBe('Border colour');
  });

  it('shows the colour chosen and a Default button that goes back to none', () => {
    create('#c92a2a');
    const seen: (string | null)[] = [];
    fixture.componentInstance.changed.subscribe((c) => seen.push(c));

    expect(input().value).toBe('#c92a2a');
    (el().querySelector('button') as HTMLButtonElement).click();

    expect(seen).toEqual([null]);
    expect(el().querySelector('button')!.getAttribute('aria-label')).toBe('Use the default border colour');
  });

  it('reports a colour picked', () => {
    create(null);
    const seen: (string | null)[] = [];
    fixture.componentInstance.changed.subscribe((c) => seen.push(c));

    input().value = '#1971c2';
    input().dispatchEvent(new Event('input'));

    expect(seen).toEqual(['#1971c2']);
  });

  it('shows a short colour in full, which is what a colour input takes', () => {
    create('#19c');

    expect(input().value).toBe('#1199cc');
  });
});

describe('FontFieldsComponent', () => {
  let fixture: ComponentFixture<FontFieldsComponent>;
  let seen: (FontOptions | null)[];
  const el = () => fixture.nativeElement as HTMLElement;
  const field = <T extends HTMLElement>(label: string) => el().querySelector(`[aria-label="${label}"]`) as T;

  /** What the page does: it keeps the font that was reported and gives it back. */
  const settle = () => {
    fixture.componentRef.setInput('font', seen.at(-1) ?? null);
    fixture.detectChanges();
  };

  const create = (font: FontOptions | null = null) => {
    fixture = TestBed.createComponent(FontFieldsComponent);
    seen = [];
    fixture.componentRef.setInput('label', 'Label font');
    fixture.componentRef.setInput('font', font);
    fixture.componentInstance.changed.subscribe((f) => seen.push(f));
    fixture.detectChanges();
  };

  it('starts with the default font and nothing else chosen', () => {
    create();

    expect(field<HTMLSelectElement>('Label font family').value).toBe('');
    expect(field<HTMLInputElement>('Label font size').value).toBe('');
    expect(field<HTMLInputElement>('Label font bold').checked).toBe(false);
    expect(el().querySelector('app-color-field')?.textContent).toContain('default');
  });

  it('shows the font it was given', () => {
    create({ family: 'serif', size: 14, weight: 'bold', italic: true, color: '#123456' });

    expect(field<HTMLSelectElement>('Label font family').value).toBe('serif');
    expect(field<HTMLInputElement>('Label font size').value).toBe('14');
    expect(field<HTMLInputElement>('Label font bold').checked).toBe(true);
    expect(field<HTMLInputElement>('Label font italic').checked).toBe(true);
    expect(el().querySelector<HTMLInputElement>('input[type=color]')!.value).toBe('#123456');
  });

  it('reports the whole font each time something changes, keeping what was chosen before', () => {
    create({ size: 12 });

    const family = field<HTMLSelectElement>('Label font family');
    family.value = 'monospace';
    family.dispatchEvent(new Event('change'));
    settle();
    field<HTMLInputElement>('Label font bold').click();
    settle();
    field<HTMLInputElement>('Label font italic').click();
    settle();
    const color = el().querySelector<HTMLInputElement>('input[type=color]')!;
    color.value = '#e8590c';
    color.dispatchEvent(new Event('input'));

    expect(seen).toEqual([
      { size: 12, family: 'monospace' },
      { size: 12, family: 'monospace', weight: 'bold' },
      { size: 12, family: 'monospace', weight: 'bold', italic: true },
      { size: 12, family: 'monospace', weight: 'bold', italic: true, color: '#e8590c' },
    ]);
  });

  it('keeps a size inside 8 to 48, and clears it when the box is emptied', () => {
    create();
    const size = field<HTMLInputElement>('Label font size');

    size.value = '200';
    size.dispatchEvent(new Event('input'));
    size.value = '2';
    size.dispatchEvent(new Event('input'));
    size.value = '';
    size.dispatchEvent(new Event('input'));

    expect(seen).toEqual([{ size: 48 }, { size: 8 }, null]);
  });

  it('reports nothing at all (null) once every choice has been taken back, so no empty font is stored', () => {
    create({ family: 'serif', weight: 'bold' });

    const family = field<HTMLSelectElement>('Label font family');
    family.value = '';
    family.dispatchEvent(new Event('change'));
    settle();
    field<HTMLInputElement>('Label font bold').click();

    expect(seen).toEqual([{ family: null, weight: 'bold' }, null]);
  });

  it('can go back to the default colour', () => {
    create({ color: '#123456', size: 10 });

    (el().querySelector('app-color-field button') as HTMLButtonElement).click();

    expect(seen).toEqual([{ color: null, size: 10 }]);
  });
});
