import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { FieldsDialogComponent } from './fields-dialog.component';

const columns = [
  { name: 'id', dataType: 'integer' },
  { name: 'customer_name', dataType: 'text' },
  { name: 'customer_email', dataType: 'text' },
  { name: 'total', dataType: 'numeric' },
];

describe('FieldsDialogComponent', () => {
  const close = vi.fn();

  const setup = (selected: string[] = []) => {
    close.mockReset();
    TestBed.configureTestingModule({
      providers: [
        { provide: MAT_DIALOG_DATA, useValue: { table: 'orders', columns, selected } },
        { provide: MatDialogRef, useValue: { close } },
      ],
    });
    const fixture = TestBed.createComponent(FieldsDialogComponent);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const button = (text: string) => Array.from(el.querySelectorAll('button')).find((b) => b.textContent?.trim() === text) as HTMLButtonElement;
    const names = () => Array.from(el.querySelectorAll('mat-checkbox')).map((c) => c.textContent?.replace(/\s+/g, ' ').trim().split(' ')[0]);
    const search = (text: string) => {
      const input = el.querySelector('input[aria-label="Search fields"]') as HTMLInputElement;
      input.value = text;
      input.dispatchEvent(new Event('input'));
      fixture.detectChanges();
    };
    const tick = (name: string) => {
      const box = Array.from(el.querySelectorAll('mat-checkbox')).find((c) => c.textContent?.includes(name))!.querySelector('input') as HTMLInputElement;
      box.click();
      fixture.detectChanges();
    };
    return { fixture, el, button, names, search, tick };
  };

  it('lists the table fields with the ones already chosen ticked', () => {
    const { el, names } = setup(['id']);

    expect(el.querySelector('h2')?.textContent).toContain('orders');
    expect(names()).toEqual(['id', 'customer_name', 'customer_email', 'total']);
    expect(el.textContent).toContain('1 of 4 selected');
  });

  it('closes with the ticked fields in the table order', () => {
    const { button, tick, fixture } = setup();

    tick('total');
    tick('id');
    button('Done').click();
    fixture.detectChanges();

    expect(close).toHaveBeenCalledWith(['id', 'total']);
  });

  it('searches, and Select all then only picks what the search shows', () => {
    const { button, names, search, fixture } = setup();

    search('customer');
    expect(names()).toEqual(['customer_name', 'customer_email']);
    button('Select all').click();
    search('');
    button('Done').click();
    fixture.detectChanges();

    expect(close).toHaveBeenCalledWith(['customer_name', 'customer_email']);
  });

  it('clears the selection and says when nothing matches', () => {
    const { button, el, search, fixture } = setup(['id', 'total']);

    button('Clear').click();
    fixture.detectChanges();
    expect(el.textContent).toContain('0 of 4 selected');

    search('zzz');
    expect(el.textContent).toContain('No field matches.');
  });
});
