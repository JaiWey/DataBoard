import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { QueryPreview } from '../organizations/data-sources.service';
import { SqlResultsComponent } from './sql-results.component';

@Component({
  imports: [SqlResultsComponent],
  template: `<app-sql-results [result]="result()" />`,
})
class Host {
  readonly result = signal<QueryPreview | null>(null);
}

const rowsOf = (n: number, offset = 0): QueryPreview => ({
  columns: [{ name: 'id', dataType: 'int4' }],
  rows: Array.from({ length: n }, (_, i) => [i + offset]),
  truncated: false,
});

describe('SqlResultsComponent', () => {
  const setup = () => {
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const ids = () => Array.from(el.querySelectorAll('tbody td')).map((c) => c.textContent);
    const click = (label: string) => {
      (el.querySelector(`button[aria-label="${label}"]`) as HTMLButtonElement).click();
      fixture.detectChanges();
    };
    return { fixture, el, ids, click, host: fixture.componentInstance };
  };

  it('shows only the first page of a long result', () => {
    const { fixture, host, ids, el } = setup();

    host.result.set(rowsOf(25));
    fixture.detectChanges();

    expect(ids()).toEqual(Array.from({ length: 10 }, (_, i) => String(i)));
    expect(el.querySelector('.mat-mdc-paginator-range-label')?.textContent).toContain('1 – 10 of 25');
  });

  it('moves to the next and last page', () => {
    const { fixture, host, ids, click } = setup();
    host.result.set(rowsOf(25));
    fixture.detectChanges();

    click('Next page');
    expect(ids()[0]).toBe('10');

    click('Last page');
    expect(ids()).toEqual(['20', '21', '22', '23', '24']);
  });

  it('starts at the first page again when a new result arrives', () => {
    const { fixture, host, ids, click } = setup();
    host.result.set(rowsOf(25));
    fixture.detectChanges();
    click('Next page');

    host.result.set(rowsOf(30, 100));
    fixture.detectChanges();

    expect(ids()[0]).toBe('100');
  });

  it('says how many rows came back and when more exist', () => {
    const { fixture, host, el } = setup();

    host.result.set({ ...rowsOf(1), truncated: true });
    fixture.detectChanges();

    expect(el.textContent).toContain('1 row (more rows exist');
  });
});
