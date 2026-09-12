import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { DatasetParameter } from './dataset-parameters';
import { ParametersPanelComponent } from './parameters-panel.component';

@Component({
  imports: [ParametersPanelComponent],
  template: `<app-parameters-panel [sql]="sql()" [readOnly]="readOnly()" [(parameters)]="parameters" />`,
})
class HostComponent {
  readonly sql = signal('SELECT 1');
  readonly readOnly = signal(false);
  readonly parameters = signal<DatasetParameter[]>([]);
}

describe('ParametersPanelComponent', () => {
  let fixture: ComponentFixture<HostComponent>;
  let host: HostComponent;

  const el = () => fixture.nativeElement as HTMLElement;
  const settle = () => {
    fixture.detectChanges();
    fixture.detectChanges(); // the rows follow the SQL one change later
  };
  const control = (label: string) => el().querySelector(`[aria-label="${label}"]`) as HTMLInputElement | HTMLSelectElement;
  const set = (label: string, value: string, event = 'input') => {
    const c = control(label);
    c.value = value;
    c.dispatchEvent(new Event(event));
    settle();
  };

  beforeEach(() => {
    fixture = TestBed.createComponent(HostComponent);
    host = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('shows nothing for SQL without placeholders', () => {
    expect(el().querySelector('.params')).toBeNull();
  });

  it('adds a row for each placeholder as it is written, starting as text with no default', () => {
    host.sql.set('SELECT * FROM t WHERE a = {{region}} AND b > {{min_price}}');
    settle();

    expect(Array.from(el().querySelectorAll('.name')).map((n) => n.textContent)).toEqual(['{{region}}', '{{min_price}}']);
    expect(host.parameters()).toEqual([
      { name: 'region', type: 'Text', default: null },
      { name: 'min_price', type: 'Text', default: null },
    ]);
    expect(el().textContent).toContain('Give region, min_price a default');
  });

  it('removes the row when its placeholder is deleted from the SQL, and keeps the others as they were', () => {
    host.sql.set('SELECT {{a}}, {{b}}');
    settle();
    set('Default of a', 'x');

    host.sql.set('SELECT {{a}}');
    settle();

    expect(host.parameters()).toEqual([{ name: 'a', type: 'Text', default: 'x' }]);
  });

  it('keeps what was set for a parameter when the SQL changes around it', () => {
    host.sql.set('SELECT {{a}}');
    settle();
    set('Type of a', 'Number', 'change');
    set('Default of a', '12.5');

    host.sql.set('SELECT 1 + {{a}} AS total');
    settle();

    expect(host.parameters()).toEqual([{ name: 'a', type: 'Number', default: 12.5 }]);
  });

  it('turns what is typed into a value of the parameter type', () => {
    host.sql.set('SELECT {{n}}, {{d}}, {{f}}, {{t}}');
    settle();

    set('Type of n', 'Number', 'change');
    set('Default of n', '7');
    set('Type of d', 'Date', 'change');
    set('Default of d', '2026-03-05');
    set('Type of f', 'Boolean', 'change');
    set('Default of f', 'false', 'change');
    set('Default of t', 'Auckland');

    expect(host.parameters()).toEqual([
      { name: 'n', type: 'Number', default: 7 },
      { name: 'd', type: 'Date', default: '2026-03-05' },
      { name: 'f', type: 'Boolean', default: false },
      { name: 't', type: 'Text', default: 'Auckland' },
    ]);
    expect(el().textContent).not.toContain('a default'); // everything has one
  });

  it('starts the default again when the type changes, because the old one rarely fits the new type', () => {
    host.sql.set('SELECT {{a}}');
    settle();
    set('Default of a', 'Auckland');

    set('Type of a', 'Number', 'change');

    expect(host.parameters()).toEqual([{ name: 'a', type: 'Number', default: null }]);
    expect(el().textContent).toContain('Give a a default');
  });

  it('does not take text that is not a number as a default', () => {
    host.sql.set('SELECT {{a}}');
    settle();
    set('Type of a', 'Number', 'change');

    set('Default of a', 'lots');

    expect(host.parameters()[0].default).toBeNull();
  });

  it('sets an optional label, and clears it again', () => {
    host.sql.set('SELECT {{a}}');
    settle();

    set('Label of a', 'Region');
    expect(host.parameters()[0].label).toBe('Region');
    set('Label of a', '');
    expect(host.parameters()[0].label).toBeNull();
  });

  it('shows what is there but changes nothing when read only', () => {
    host.parameters.set([{ name: 'a', type: 'Text', default: 'x' }]);
    host.readOnly.set(true);
    host.sql.set('SELECT {{a}}, {{b}}');
    settle();

    expect(host.parameters()).toEqual([{ name: 'a', type: 'Text', default: 'x' }]); // not kept in step with the SQL
    expect((control('Default of a') as HTMLInputElement).disabled).toBe(true);
    expect((control('Type of a') as HTMLSelectElement).disabled).toBe(true);
  });
});
