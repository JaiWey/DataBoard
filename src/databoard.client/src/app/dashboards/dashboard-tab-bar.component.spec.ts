import { ComponentFixture, TestBed } from '@angular/core/testing';
import { DashboardTabBarComponent } from './dashboard-tab-bar.component';
import { MoveToTabComponent } from './move-to-tab.component';
import { DashboardTab } from './dashboards.service';

const tabs: DashboardTab[] = [{ id: 't1', name: 'Overview' }, { id: 't2', name: 'Costs' }, { id: 't3', name: 'People' }];

describe('DashboardTabBarComponent', () => {
  let fixture: ComponentFixture<DashboardTabBarComponent>;
  const el = () => fixture.nativeElement as HTMLElement;
  const names = () => Array.from(el().querySelectorAll('[role=tab]')).map((t) => t.textContent?.trim());
  const emitted = <T>(output: { subscribe(fn: (v: T) => void): unknown }) => {
    const seen: T[] = [];
    output.subscribe((v) => seen.push(v));
    return seen;
  };

  const create = (inputs: { tabs?: DashboardTab[]; activeId?: string | null; canEdit?: boolean; editing?: boolean } = {}) => {
    fixture = TestBed.createComponent(DashboardTabBarComponent);
    fixture.componentRef.setInput('tabs', inputs.tabs ?? tabs);
    fixture.componentRef.setInput('activeId', inputs.activeId ?? 't1');
    fixture.componentRef.setInput('canEdit', inputs.canEdit ?? false);
    fixture.componentRef.setInput('editing', inputs.editing ?? false);
    fixture.detectChanges();
  };
  const openMenu = () => {
    (el().querySelector('.more') as HTMLButtonElement).click();
    fixture.detectChanges();
  };
  const menuItem = (text: string) =>
    Array.from(document.querySelectorAll<HTMLButtonElement>('.mat-mdc-menu-item')).find((b) => b.textContent?.trim() === text)!;

  it('lists the tabs and marks the one on show', () => {
    create({ activeId: 't2' });

    expect(names()).toEqual(['Overview', 'Costs', 'People']);
    expect(Array.from(el().querySelectorAll('[role=tab]')).map((t) => t.getAttribute('aria-selected'))).toEqual(['false', 'true', 'false']);
    expect(el().querySelector('.tab.active .name')?.textContent).toBe('Costs');
  });

  it('reports the tab that was clicked, for everyone', () => {
    create();
    const picked = emitted<string>(fixture.componentInstance.select);

    (el().querySelectorAll('[role=tab]')[2] as HTMLButtonElement).click();

    expect(picked).toEqual(['t3']);
  });

  it('shows nothing at all to a viewer when there are no tabs', () => {
    create({ tabs: [], canEdit: false });

    expect(el().querySelector('.bar')).toBeNull();
  });

  it('shows viewers the tabs but nothing to change them with', () => {
    create({ canEdit: false });

    expect(el().querySelector('.more')).toBeNull();
    expect(el().querySelector('.add')).toBeNull();
  });

  it('offers an editor of a dashboard without tabs an Add tab button, and a + once there are some', () => {
    create({ tabs: [], canEdit: true });
    expect(el().querySelector('.add')?.textContent?.trim()).toBe('Add tab');

    create({ canEdit: true });
    expect(el().querySelector('.add')?.textContent?.trim()).toBe('+');
  });

  it('stops offering more tabs at the limit', () => {
    create({ tabs: Array.from({ length: 10 }, (_, i) => ({ id: `t${i}`, name: `Tab ${i}` })), canEdit: true });

    expect(el().querySelector('.add')).toBeNull();
  });

  it('reports adding a tab', () => {
    create({ canEdit: true });
    const added = emitted<void>(fixture.componentInstance.add);

    (el().querySelector('.add') as HTMLButtonElement).click();

    expect(added.length).toBe(1);
  });

  it('offers the menu of the tab on show only, and reports rename, remove and moves', () => {
    create({ canEdit: true, activeId: 't2' });
    const renamed = emitted<DashboardTab>(fixture.componentInstance.rename);
    const removed = emitted<DashboardTab>(fixture.componentInstance.remove);
    const earlier = emitted<DashboardTab>(fixture.componentInstance.moveEarlier);
    const later = emitted<DashboardTab>(fixture.componentInstance.moveLater);
    expect(el().querySelectorAll('.more').length).toBe(1);

    openMenu();
    menuItem('Rename').click();
    fixture.detectChanges();
    openMenu();
    menuItem('Remove').click();
    fixture.detectChanges();
    openMenu();
    menuItem('Move left').click();
    fixture.detectChanges();
    openMenu();
    menuItem('Move right').click();

    expect(renamed).toEqual([tabs[1]]);
    expect(removed).toEqual([tabs[1]]);
    expect(earlier).toEqual([tabs[1]]);
    expect(later).toEqual([tabs[1]]);
  });

  it('cannot move the first tab left or the last right', () => {
    create({ canEdit: true, activeId: 't1' });
    openMenu();
    expect(menuItem('Move left').disabled).toBe(true);
    expect(menuItem('Move right').disabled).toBe(false);
  });

  it('cannot change tabs while the layout is being arranged, but can still switch', () => {
    create({ canEdit: true, editing: true });

    expect(el().querySelector('.more')).toBeNull();
    expect(el().querySelector('.add')).toBeNull();
    expect(el().querySelectorAll('[role=tab]').length).toBe(3);
  });
});

describe('MoveToTabComponent', () => {
  let fixture: ComponentFixture<MoveToTabComponent>;

  const create = (current: string | null, list: DashboardTab[] = tabs) => {
    fixture = TestBed.createComponent(MoveToTabComponent);
    fixture.componentRef.setInput('tabs', list);
    fixture.componentRef.setInput('current', current);
    fixture.detectChanges();
  };

  it('lists the other tabs and reports the one chosen', () => {
    create('t1');
    const picked: string[] = [];
    fixture.componentInstance.pick.subscribe((id) => picked.push(id));

    (fixture.nativeElement.querySelector('.move') as HTMLButtonElement).click();
    fixture.detectChanges();
    const items = Array.from(document.querySelectorAll<HTMLButtonElement>('.mat-mdc-menu-item'));

    expect(items.map((i) => i.textContent?.trim())).toEqual(['Costs', 'People']);
    items[1].click();
    expect(picked).toEqual(['t3']);
  });

  it('shows nothing when there is no other tab, such as on a dashboard without tabs', () => {
    create(null, []);
    expect(fixture.nativeElement.querySelector('.move')).toBeNull();

    create('t1', [tabs[0]]);
    expect(fixture.nativeElement.querySelector('.move')).toBeNull();
  });
});
