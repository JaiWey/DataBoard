import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TabItem, TabStripComponent } from './tab-strip.component';

@Component({
  imports: [TabStripComponent],
  template: `<app-tab-strip [tabs]="tabs()" [activeId]="active()" label="Open things" (select)="selected.push($event)" (close)="closed.push($event)" />`,
})
class Host {
  readonly tabs = signal<TabItem[]>([{ id: 'a', name: 'Alpha' }, { id: 'b', name: 'Beta', dirty: true }]);
  readonly active = signal<string | null>('a');
  selected: string[] = [];
  closed: string[] = [];
}

describe('TabStripComponent', () => {
  const setup = () => {
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    return { fixture, host: fixture.componentInstance, el: fixture.nativeElement as HTMLElement };
  };

  it('shows the tabs, highlights the active one and marks unsaved ones', () => {
    const { el } = setup();

    expect(el.querySelector('[role=tablist]')?.getAttribute('aria-label')).toBe('Open things');
    expect(Array.from(el.querySelectorAll('[role=tab]')).map((t) => t.textContent?.replace('●', '').trim())).toEqual(['Alpha', 'Beta']);
    expect(el.querySelector('.tab.active [role=tab]')?.textContent).toContain('Alpha');
    expect(el.querySelectorAll('.dot').length).toBe(1);
    expect(el.querySelector('.tab:nth-child(2) .dot')).not.toBeNull();
  });

  it('reports which tab was selected or closed', () => {
    const { host, el, fixture } = setup();

    (el.querySelectorAll('[role=tab]')[1] as HTMLButtonElement).click();
    (el.querySelector('button[aria-label="Close Alpha"]') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(host.selected).toEqual(['b']);
    expect(host.closed).toEqual(['a']);
  });

  it('closes a tab with a middle click but not with other mouse buttons', () => {
    const { host, el } = setup();
    const tab = el.querySelector('[role=tab]') as HTMLButtonElement;

    tab.dispatchEvent(new MouseEvent('auxclick', { button: 2, bubbles: true }));
    expect(host.closed).toEqual([]);

    tab.dispatchEvent(new MouseEvent('auxclick', { button: 1, bubbles: true }));
    expect(host.closed).toEqual(['a']);
  });

  it('marks the selected tab for assistive technology', () => {
    const { el } = setup();

    expect(Array.from(el.querySelectorAll('[role=tab]')).map((t) => t.getAttribute('aria-selected'))).toEqual(['true', 'false']);
  });
});
