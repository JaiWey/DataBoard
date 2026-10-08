import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { AppSelectComponent, SelectOption } from './app-select.component';
import { chooseOption, disabledOptions, selectDisabled, selectOptions, selectedText, selectedValue } from './select.testing';

const OPTIONS: SelectOption[] = [
  { value: '', label: 'Nothing' },
  { value: 'a', label: 'Alpha' },
  { value: 'b', label: 'Beta', disabled: true },
  { value: 'c', label: 'Gamma' },
];

@Component({
  imports: [AppSelectComponent, ReactiveFormsModule],
  template: `
    <app-select ariaLabel="Bound" label="Letter" hint="Pick one" [options]="options" [value]="value()" [disabled]="off()" (valueChange)="take($event)" />
    <app-select ariaLabel="Controlled" [options]="options" [formControl]="control" />
  `,
})
class Host {
  options = OPTIONS;
  value = signal('a');
  off = signal(false);
  accepting = signal(true);
  chosen: string[] = [];
  control = new FormControl('c', { nonNullable: true });

  take(chosen: string): void {
    this.chosen.push(chosen);
    if (this.accepting()) this.value.set(chosen);
  }
}

describe('AppSelectComponent', () => {
  const setup = () => {
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    return fixture;
  };

  afterEach(() => document.querySelectorAll('.cdk-overlay-container').forEach((c) => (c.innerHTML = '')));

  it('shows the value it is given and the text of that choice', async () => {
    const fixture = setup();
    await fixture.whenStable(); // a select works out the text of its first value a moment after it is drawn
    fixture.detectChanges();

    expect(selectedValue(fixture, 'Bound')).toBe('a');
    expect(selectedText(fixture, 'Bound')).toBe('Alpha');
  });

  it('lists its choices and says which cannot be picked', () => {
    const fixture = setup();

    expect(selectOptions(fixture, 'Bound')).toEqual([['', 'Nothing'], ['a', 'Alpha'], ['b', 'Beta'], ['c', 'Gamma']]);
    expect(disabledOptions(fixture, 'Bound')).toEqual(['b']);
  });

  it('tells what was chosen, and an empty choice is a choice', () => {
    const fixture = setup();

    chooseOption(fixture, 'Bound', 'c');
    chooseOption(fixture, 'Bound', '');

    expect(fixture.componentInstance.chosen).toEqual(['c', '']);
    expect(selectedValue(fixture, 'Bound')).toBe('');
    expect(selectedText(fixture, 'Bound')).toBe('Nothing');
  });

  it('keeps showing the bound value when the parent does not take the new choice', async () => {
    const fixture = setup();
    fixture.componentInstance.accepting.set(false);

    chooseOption(fixture, 'Bound', 'c');
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.componentInstance.chosen).toEqual(['c']);
    expect(selectedValue(fixture, 'Bound')).toBe('a');
    expect(selectedText(fixture, 'Bound')).toBe('Alpha');
  });

  it('follows a value that is changed from outside', () => {
    const fixture = setup();

    fixture.componentInstance.value.set('c');
    fixture.detectChanges();

    expect(selectedText(fixture, 'Bound')).toBe('Gamma');
  });

  it('cannot be used while it is disabled', () => {
    const fixture = setup();
    fixture.componentInstance.off.set(true);
    fixture.detectChanges();

    expect(selectDisabled(fixture, 'Bound')).toBe(true);
  });

  it('works with a form control, both ways', () => {
    const fixture = setup();
    expect(selectedValue(fixture, 'Controlled')).toBe('c');

    chooseOption(fixture, 'Controlled', 'a');
    expect(fixture.componentInstance.control.value).toBe('a');

    fixture.componentInstance.control.setValue('c');
    fixture.detectChanges();
    expect(selectedText(fixture, 'Controlled')).toBe('Gamma');

    fixture.componentInstance.control.disable();
    fixture.detectChanges();
    expect(selectDisabled(fixture, 'Controlled')).toBe(true);
  });

  it('shows its caption and its hint', () => {
    const fixture = setup();

    const text = (fixture.nativeElement as HTMLElement).textContent;
    expect(text).toContain('Letter');
    expect(text).toContain('Pick one');
  });
});
