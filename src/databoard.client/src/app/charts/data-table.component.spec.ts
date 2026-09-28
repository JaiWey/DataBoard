import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TableOptions } from './chart-option';
import { DataTableComponent } from './data-table.component';
import { NumberFormatOptions } from './number-format';

const rows = (n: number): unknown[][] => Array.from({ length: n }, (_, i) => [`name ${i + 1}`, (i * 7) % 11]);
const result = (data: unknown[][] = [['Auckland', 2480], ['Wellington', 5], [null, null]]) => ({ columns: [{ name: 'region' }, { name: 'revenue' }], rows: data });

describe('DataTableComponent', () => {
  let fixture: ComponentFixture<DataTableComponent>;
  const el = () => fixture.nativeElement as HTMLElement;
  const heads = () => Array.from(el().querySelectorAll('th')).map((h) => h.textContent?.trim());
  const cells = () => Array.from(el().querySelectorAll('tbody tr')).map((r) => Array.from(r.querySelectorAll('td')).map((c) => c.textContent?.trim()));
  const column = (index: number) => cells().map((r) => r[index]);
  const sortButton = (name: string) => el().querySelector<HTMLButtonElement>(`button.sort[aria-label="Sort by ${name}"]`)!;
  const pageButton = (n: number) => el().querySelector<HTMLButtonElement>(`button.page[aria-label="Page ${n}"]`)!;

  const create = (inputs: { data?: unknown[][]; style?: TableOptions | null; format?: NumberFormatOptions | null; fill?: boolean } = {}) => {
    fixture = TestBed.createComponent(DataTableComponent);
    fixture.componentRef.setInput('result', result(inputs.data));
    fixture.componentRef.setInput('tableStyle', inputs.style ?? null);
    fixture.componentRef.setInput('numberFormat', inputs.format ?? null);
    fixture.componentRef.setInput('fill', inputs.fill ?? false);
    fixture.detectChanges();
  };

  describe('the usual look', () => {
    it('shows a header and a row per result row, with a hollow circle for a missing value', () => {
      create();

      expect(heads()).toEqual(['region', 'revenue']);
      expect(cells()).toEqual([['Auckland', '2480'], ['Wellington', '5'], ['∅', '∅']]);
    });

    it('has a header that stays in view and borders, no row numbers and no pages', () => {
      create();
      const th = el().querySelector('th') as HTMLElement;

      expect(th.style.position).toBe('');
      expect(th.style.border).toBe('');
      expect(el().querySelector('.number')).toBeNull();
      expect(el().querySelector('.pager')).toBeNull();
    });

    it('is inside the box that scrolls', () => {
      create({ fill: true });

      expect(el().querySelector('.results.fill')).not.toBeNull();
      expect(el().classList).toContain('fill');
    });

    it('formats numbers only when the chart has a number format', () => {
      create({ data: [['a', 1234567.5]] });
      expect(column(1)).toEqual(['1234567.5']);

      create({ data: [['a', 1234567.5]], format: { thousands: true, decimals: 1, prefix: '$' } });
      expect(column(1)).toEqual(['$1,234,567.5']);
    });
  });

  describe('style', () => {
    it('can leave the header to scroll away and have no borders', () => {
      create({ style: { fixedHeader: false, border: false } });
      const th = el().querySelector('th') as HTMLElement;
      const td = el().querySelector('td') as HTMLElement;

      expect(th.style.position).toBe('static');
      // jsdom writes the "none" it was given as the initial value of border; what matters is that it is set at all.
      expect(th.style.border).not.toBe('');
      expect(td.style.border).not.toBe('');
    });

    it('counts the rows in a first column', () => {
      create({ style: { rowNumber: true } });

      expect(heads()).toEqual(['#', 'region', 'revenue']);
      expect(column(0)).toEqual(['1', '2', '3']);
    });

    it.each([['small', '0.3rem 0.6rem'], ['medium', '0.5rem 0.8rem'], ['large', '0.75rem 1rem']] as const)('is %s: roomy as that says', (size, padding) => {
      create({ style: { size } });

      expect((el().querySelector('td') as HTMLElement).style.padding).toBe(padding);
    });

    it('colours the header, sets its font and aligns it', () => {
      create({ style: { headerBackground: '#f1f3f5', headerAlign: 'center', headerFont: { family: 'serif', size: 13, weight: 'bold', italic: true, color: '#343a40' } } });
      const th = el().querySelector('th') as HTMLElement;

      expect(th.style.backgroundColor).toBe('rgb(241, 243, 245)');
      expect(th.style.textAlign).toBe('center');
      expect(th.style.fontFamily).toBe('serif');
      expect(th.style.fontSize).toBe('13px');
      expect(th.style.fontWeight).toBe('bold');
      expect(th.style.fontStyle).toBe('italic');
      expect(th.style.color).toBe('rgb(52, 58, 64)');
    });

    it('colours odd and even rows, the first row being odd', () => {
      create({ style: { oddRowBackground: '#ffffff', oddRowColor: '#111111', evenRowBackground: '#f8f9fa', evenRowColor: '#222222' } });
      const tr = Array.from(el().querySelectorAll<HTMLElement>('tbody tr'));

      expect(tr.map((r) => r.style.backgroundColor)).toEqual(['rgb(255, 255, 255)', 'rgb(248, 249, 250)', 'rgb(255, 255, 255)']);
      expect(tr.map((r) => r.style.color)).toEqual(['rgb(17, 17, 17)', 'rgb(34, 34, 34)', 'rgb(17, 17, 17)']);
    });

    it('puts the header text and its arrow where the alignment says', () => {
      create();
      const button = () => el().querySelector('button.sort') as HTMLElement;
      const arrow = () => el().querySelector('button.sort svg') as unknown as HTMLElement;
      expect(button().style.justifyContent).toBe('');
      expect(arrow().style.marginLeft).toBe(''); // the arrow is pushed to the far end

      create({ style: { headerAlign: 'center' } });
      expect(button().style.justifyContent).toBe('center');
      expect(arrow().style.marginLeft).toBe('0.4rem');

      create({ style: { headerAlign: 'right' } });
      expect(button().style.justifyContent).toBe('flex-end');
    });
    it('leaves rows alone that have no colour chosen', () => {
      create({ style: { evenRowBackground: '#f8f9fa' } });
      const tr = Array.from(el().querySelectorAll<HTMLElement>('tbody tr'));

      expect(tr.map((r) => r.style.backgroundColor)).toEqual(['', 'rgb(248, 249, 250)', '']);
    });

    it('sets the font of the body', () => {
      create({ style: { bodyFont: { family: 'monospace', size: 11, weight: 'bold' } } });
      const body = el().querySelector('tbody') as HTMLElement;

      expect([body.style.fontFamily, body.style.fontSize, body.style.fontWeight]).toEqual(['monospace', '11px', 'bold']);
    });
  });

  describe('sorting', () => {
    it('sorts by a header: ascending, descending, and back to the order the rows came in', () => {
      create({ data: [['b', 2], ['a', 30], ['c', 10]] });

      sortButton('revenue').click();
      fixture.detectChanges();
      expect(column(1)).toEqual(['2', '10', '30']);
      sortButton('revenue').click();
      fixture.detectChanges();
      expect(column(1)).toEqual(['30', '10', '2']);
      sortButton('revenue').click();
      fixture.detectChanges();
      expect(column(1)).toEqual(['2', '30', '10']);
    });

    it('says which way it is sorted, to screen readers and with the arrow', () => {
      create({ data: [['b', 2], ['a', 30]] });
      const th = (name: string) => Array.from(el().querySelectorAll('th')).find((h) => h.textContent?.trim() === name)!;

      expect(th('region').getAttribute('aria-sort')).toBeNull();
      sortButton('region').click();
      fixture.detectChanges();
      expect(th('region').getAttribute('aria-sort')).toBe('ascending');
      sortButton('region').click();
      fixture.detectChanges();
      expect(th('region').getAttribute('aria-sort')).toBe('descending');
      sortButton('revenue').click();
      fixture.detectChanges();
      expect(th('region').getAttribute('aria-sort')).toBeNull();
      expect(th('revenue').getAttribute('aria-sort')).toBe('ascending');
    });

    it('puts empty values last whichever way it is sorted', () => {
      create({ data: [[null, null], ['a', 5], ['b', 1]] });

      sortButton('revenue').click();
      fixture.detectChanges();
      expect(column(1)).toEqual(['1', '5', '∅']);
      sortButton('revenue').click();
      fixture.detectChanges();
      expect(column(1)).toEqual(['5', '1', '∅']);
    });

    it('has plain headers, with no buttons, when sorting is turned off', () => {
      create({ style: { sortable: false } });

      expect(el().querySelector('button.sort')).toBeNull();
      expect(heads()).toEqual(['region', 'revenue']);
    });

    it('starts again when the rows are other rows', () => {
      create({ data: [['b', 2], ['a', 30]] });
      sortButton('region').click();
      fixture.detectChanges();

      fixture.componentRef.setInput('result', result([['z', 1], ['y', 2]]));
      fixture.detectChanges();

      expect(column(0)).toEqual(['z', 'y']);
      expect(el().querySelector('th[aria-sort]')).toBeNull();
    });

    it('keeps the header text exactly the name of the column', () => {
      create();

      expect(Array.from(el().querySelectorAll('th')).map((h) => h.textContent)).toEqual(['region', 'revenue']);
    });
  });

  describe('pages', () => {
    it('shows the rows of one page, and a pager that goes from page to page', () => {
      create({ data: rows(45), style: { pageSize: 20 } });

      expect(cells().length).toBe(20);
      expect(column(0)[0]).toBe('name 1');
      expect(Array.from(el().querySelectorAll('.pager .page')).map((b) => b.textContent?.trim())).toEqual(['1', '2', '3']);
      expect(pageButton(1).getAttribute('aria-current')).toBe('page');

      pageButton(3).click();
      fixture.detectChanges();
      expect(cells().length).toBe(5);
      expect(column(0)[0]).toBe('name 41');
      expect(pageButton(3).getAttribute('aria-current')).toBe('page');
    });

    it('steps with the arrows, which stop at the ends', () => {
      create({ data: rows(45), style: { pageSize: 20 } });
      const prev = () => el().querySelector<HTMLButtonElement>('button[aria-label="Previous page"]')!;
      const next = () => el().querySelector<HTMLButtonElement>('button[aria-label="Next page"]')!;

      expect(prev().disabled).toBe(true);
      next().click();
      fixture.detectChanges();
      expect(column(0)[0]).toBe('name 21');
      next().click();
      fixture.detectChanges();
      expect(next().disabled).toBe(true);
      prev().click();
      fixture.detectChanges();
      expect(column(0)[0]).toBe('name 21');
    });

    it('shows no pager when everything fits on one page, or there are no pages', () => {
      create({ data: rows(20), style: { pageSize: 20 } });
      expect(el().querySelector('.pager')).toBeNull();

      create({ data: rows(200) });
      expect(el().querySelector('.pager')).toBeNull();
      expect(cells().length).toBe(200);
    });

    it('shows a gap in a long list of pages', () => {
      create({ data: rows(500), style: { pageSize: 5 } });

      expect(Array.from(el().querySelectorAll('.pager .page')).map((b) => b.textContent?.trim())).toEqual(['1', '2', '3', '4', '5', '100']);
      expect(el().querySelector('.pager .gap')).not.toBeNull();
    });

    it('goes on counting rows across pages when they are numbered', () => {
      create({ data: rows(45), style: { pageSize: 20, rowNumber: true } });

      pageButton(2).click();
      fixture.detectChanges();

      expect(column(0)[0]).toBe('21');
      expect(column(0)[19]).toBe('40');
    });

    it('sorts all the rows, not just the page, and goes back to the first page', () => {
      create({ data: rows(45), style: { pageSize: 20 } });
      pageButton(2).click();
      fixture.detectChanges();

      sortButton('region').click();
      sortButton('region').click(); // descending
      fixture.detectChanges();

      expect(pageButton(1).getAttribute('aria-current')).toBe('page');
      expect(column(0)[0]).toBe('name 45');
    });

    it('goes back to the first page for other rows, and stays on a page that exists when the page grows', () => {
      create({ data: rows(45), style: { pageSize: 20 } });
      pageButton(3).click();
      fixture.detectChanges();

      fixture.componentRef.setInput('tableStyle', { pageSize: 50 });
      fixture.detectChanges();
      expect(column(0)[0]).toBe('name 1');
      expect(el().querySelector('.pager')).toBeNull();
    });

    it('colours rows by their place in the whole table, not in the page', () => {
      create({ data: rows(45), style: { pageSize: 3, evenRowBackground: '#f8f9fa' } });

      pageButton(2).click();
      fixture.detectChanges();

      const tr = Array.from(el().querySelectorAll<HTMLElement>('tbody tr'));
      expect(tr.map((r) => r.style.backgroundColor)).toEqual(['rgb(248, 249, 250)', '', 'rgb(248, 249, 250)']); // rows 4, 5 and 6 of the table: the 4th and 6th are even
    });
  });
});
