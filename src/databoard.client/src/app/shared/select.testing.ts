import { ComponentFixture } from '@angular/core/testing';

/**
 * Helpers for tests that use an `app-select` (Angular Material's select). The list of a Material select is drawn in an
 * overlay outside the component, so choosing means opening the select and clicking an option, which a test cannot do by
 * setting `value` on a native `<select>`. Every select and option carries its value in `data-value`.
 */

/** The select (the mat-select element) with this accessible name, looked for in the fixture first and then in the open dialogs. */
export function selectHost(fixture: ComponentFixture<unknown>, label: string): HTMLElement | null {
  const selector = `mat-select[aria-label="${label}"]`;
  return (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>(selector) ?? document.querySelector<HTMLElement>(selector);
}

/** The value now chosen in the select. */
export function selectedValue(fixture: ComponentFixture<unknown>, label: string): string | null {
  return selectHost(fixture, label)?.getAttribute('data-value') ?? null;
}

/** The text of the choice now made, found from the list of choices (a select shows its first text a moment late). */
export function selectedLabel(fixture: ComponentFixture<unknown>, label: string): string | undefined {
  const value = selectedValue(fixture, label);
  return selectOptions(fixture, label).find(([v]) => v === value)?.[1];
}

/** Whether the select can be used. */
export function selectDisabled(fixture: ComponentFixture<unknown>, label: string): boolean {
  return selectHost(fixture, label)?.getAttribute('aria-disabled') === 'true';
}

/** What the select shows as its chosen choice. */
export function selectedText(fixture: ComponentFixture<unknown>, label: string): string {
  return selectHost(fixture, label)?.querySelector('.mat-mdc-select-value')?.textContent?.trim() ?? '';
}

/** The list of the select that was opened last. The lists of selects that were closed before stay in the page in a test. */
let openPanel: HTMLElement | null = null;

function open(fixture: ComponentFixture<unknown>, label: string): void {
  const host = selectHost(fixture, label);
  if (!host) throw new Error(`There is no select named "${label}".`);
  fixture.detectChanges(); // what the page has just been told (a request that finished) may enable the select
  host.querySelector<HTMLElement>('.mat-mdc-select-trigger')!.click();
  fixture.detectChanges();
  const id = host.getAttribute('aria-controls');
  openPanel = (id ? document.getElementById(id) : null) ?? Array.from(document.querySelectorAll<HTMLElement>('.mat-mdc-select-panel')).at(-1) ?? null;
}

const optionElements = () => Array.from(openPanel?.querySelectorAll<HTMLElement>('.mat-mdc-option') ?? []);

function close(fixture: ComponentFixture<unknown>): void {
  openPanel?.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
  openPanel = null;
  fixture.detectChanges();
}

/** The choices of the select, as `[value, text]`. */
export function selectOptions(fixture: ComponentFixture<unknown>, label: string): [string, string][] {
  open(fixture, label);
  const options = optionElements().map((o) => [o.getAttribute('data-value') ?? '', o.textContent?.trim() ?? ''] as [string, string]);
  close(fixture);
  return options;
}

/** Which choices of the select cannot be picked. */
export function disabledOptions(fixture: ComponentFixture<unknown>, label: string): string[] {
  open(fixture, label);
  const disabled = optionElements().filter((o) => o.getAttribute('aria-disabled') === 'true').map((o) => o.getAttribute('data-value') ?? '');
  close(fixture);
  return disabled;
}

/** Opens the select and picks the choice with this value. */
export function chooseOption(fixture: ComponentFixture<unknown>, label: string, value: string): void {
  open(fixture, label);
  const option = optionElements().find((o) => o.getAttribute('data-value') === value);
  if (!option) {
    close(fixture);
    throw new Error(`The select "${label}" has no choice with the value "${value}".`);
  }
  option.click();
  fixture.detectChanges();
}
