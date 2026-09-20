import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TreeEntry } from './folder-tree';
import { FolderTreeComponent } from './folder-tree.component';
import { Folder } from './folders.service';

const folder = (id: string, name: string, parentId: string | null = null): Folder => ({ id, name, parentId });
const dataset = (id: string, name: string, folderId: string | null = null): TreeEntry => ({ id, name, folderId, subline: '1 columns · SQL' });

const folders = [folder('sales', 'Sales'), folder('europe', 'Europe', 'sales'), folder('archive', 'Archive')];
const datasets = [dataset('orders', 'Orders', 'europe'), dataset('returns', 'Returns', 'sales'), dataset('zebra', 'Zebra')];

describe('FolderTreeComponent', () => {
  let fixture: ComponentFixture<FolderTreeComponent>;
  const el = () => fixture.nativeElement as HTMLElement;
  const names = () => Array.from(el().querySelectorAll('.row .name')).map((n) => n.textContent?.trim());
  const row = (name: string) => Array.from(el().querySelectorAll<HTMLElement>('.row')).find((r) => r.querySelector('.name')?.textContent?.trim() === name)!;
  const main = (name: string) => row(name).querySelector<HTMLButtonElement>('.main')!;

  const create = (canEdit = true, data: { folders?: Folder[]; datasets?: TreeEntry[] } = {}) => {
    fixture = TestBed.createComponent(FolderTreeComponent);
    fixture.componentRef.setInput('folders', data.folders ?? folders);
    fixture.componentRef.setInput('entries', data.datasets ?? datasets);
    fixture.componentRef.setInput('filterLabel', 'Filter datasets');
    fixture.componentRef.setInput('emptyText', 'No datasets yet.');
    fixture.componentRef.setInput('addActions', [{ id: 'sql', label: 'New SQL dataset here' }, { id: 'view', label: 'New table view here' }]);
    fixture.componentRef.setInput('canEdit', canEdit);
    fixture.componentRef.setInput('storageKey', 'test.tree');
    fixture.detectChanges();
  };

  /** A drag event whose payload jsdom cannot build; the component only touches dataTransfer and preventDefault. */
  const dragEvent = (type: string) => {
    const event = new Event(type, { bubbles: true, cancelable: true }) as DragEvent;
    Object.defineProperty(event, 'dataTransfer', { value: { setData: vi.fn(), effectAllowed: '', dropEffect: '' } });
    return event;
  };
  const drag = (from: HTMLElement, to: HTMLElement) => {
    from.dispatchEvent(dragEvent('dragstart'));
    to.dispatchEvent(dragEvent('dragover'));
    fixture.detectChanges();
    const drop = dragEvent('drop');
    to.dispatchEvent(drop);
    fixture.detectChanges();
    return drop;
  };

  beforeEach(() => localStorage.clear());

  it('starts collapsed, folders first, with how much is in each folder', () => {
    create();

    expect(names()).toEqual(['Archive', 'Sales', 'Zebra']);
    expect(row('Sales').querySelector('.count')?.textContent).toBe('2');
    expect(row('Sales').getAttribute('aria-expanded')).toBe('false');
    expect(el().querySelector('[role=tree]')).not.toBeNull();
  });

  it('opens and closes a folder on click, and tells the page which folder is selected', () => {
    create();
    const selected: (string | null)[] = [];
    fixture.componentInstance.selectFolder.subscribe((id) => selected.push(id));

    main('Sales').click();
    fixture.detectChanges();

    expect(names()).toEqual(['Archive', 'Sales', 'Europe', 'Returns', 'Zebra']);
    expect(row('Sales').getAttribute('aria-expanded')).toBe('true');
    expect(row('Europe').getAttribute('aria-level')).toBe('2');
    main('Sales').click();
    fixture.detectChanges();
    expect(names()).toEqual(['Archive', 'Sales', 'Zebra']);
    expect(selected).toEqual(['sales', 'sales']);
  });

  it('remembers which folders are open', () => {
    create();
    main('Sales').click();
    fixture.detectChanges();

    TestBed.resetTestingModule();
    create();

    expect(names()).toContain('Europe');
  });

  it('opens a dataset on click and shows what kind it is', () => {
    create();
    const opened: TreeEntry[] = [];
    fixture.componentInstance.open.subscribe((d) => opened.push(d));

    main('Zebra').click();

    expect(opened.map((d) => d.id)).toEqual(['zebra']);
    expect(row('Zebra').querySelector('.sub')?.textContent).toContain('1 columns · SQL');
  });

  it('opens the folders on the way to the active dataset', () => {
    create();

    fixture.componentRef.setInput('activeId', 'orders');
    fixture.detectChanges();

    expect(names()).toEqual(['Archive', 'Sales', 'Europe', 'Orders', 'Returns', 'Zebra']);
    expect(row('Orders').classList).toContain('active');
  });

  it('filters by name and shows the folders on the way, and says when nothing matches', () => {
    create();
    const input = el().querySelector<HTMLInputElement>('input[aria-label="Filter datasets"]')!;

    input.value = 'ord';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(names()).toEqual(['Sales', 'Europe', 'Orders']);

    input.value = 'zzz';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(el().querySelector('.empty')?.textContent).toContain('Nothing matches.');
  });

  it('tells an empty organization how it stands', () => {
    create(true, { folders: [], datasets: [] });

    expect(el().querySelector('.empty')?.textContent).toContain('No datasets yet.');
  });

  it('moves between rows with the arrow keys and opens or closes folders with right and left', () => {
    create();
    const key = (name: string, k: string) => main(name).dispatchEvent(new KeyboardEvent('keydown', { key: k, bubbles: true, cancelable: true }));

    key('Archive', 'ArrowDown');
    expect(document.activeElement).toBe(main('Sales'));
    key('Sales', 'ArrowRight');
    fixture.detectChanges();
    expect(names()).toContain('Europe');
    key('Sales', 'ArrowRight'); // already open: goes into it
    expect(document.activeElement).toBe(main('Europe'));
    key('Europe', 'ArrowUp');
    key('Sales', 'ArrowLeft');
    fixture.detectChanges();
    expect(names()).not.toContain('Europe');
  });

  describe('for editors', () => {
    it('asks for a new folder at the top level, and inside a folder from its menu', async () => {
      create();
      const asked: (string | null)[] = [];
      fixture.componentInstance.createFolder.subscribe((p) => asked.push(p));

      el().querySelector<HTMLButtonElement>('button[aria-label="New folder"]')!.click();
      row('Sales').querySelector<HTMLButtonElement>('.more')!.click();
      fixture.detectChanges();
      Array.from(document.querySelectorAll<HTMLButtonElement>('.mat-mdc-menu-item')).find((b) => b.textContent?.trim() === 'New folder here')!.click();

      expect(asked).toEqual([null, 'sales']);
    });

    it('offers rename, move and delete on a folder, but only Move to… on a dataset', () => {
      create();
      const labels = () => Array.from(document.querySelectorAll('.mat-mdc-menu-item')).map((b) => b.textContent?.trim());

      row('Sales').querySelector<HTMLButtonElement>('.more')!.click();
      fixture.detectChanges();
      expect(labels()).toEqual(['New folder here', 'New SQL dataset here', 'New table view here', 'Rename', 'Move to…', 'Delete']);
    });

    it('asks to add a dataset in a folder and to move a dataset', () => {
      create();
      const added: unknown[] = [];
      const moved: unknown[] = [];
      fixture.componentInstance.addItem.subscribe((r) => added.push(r));
      fixture.componentInstance.moveTo.subscribe((r) => moved.push(r));

      row('Sales').querySelector<HTMLButtonElement>('.more')!.click();
      fixture.detectChanges();
      Array.from(document.querySelectorAll<HTMLButtonElement>('.mat-mdc-menu-item')).find((b) => b.textContent?.trim() === 'New table view here')!.click();
      fixture.detectChanges();
      row('Zebra').querySelector<HTMLButtonElement>('.more')!.click();
      fixture.detectChanges();
      Array.from(document.querySelectorAll<HTMLButtonElement>('.mat-mdc-menu-item')).find((b) => b.textContent?.trim() === 'Move to…')!.click();

      expect(added).toEqual([{ action: 'view', folderId: 'sales' }]);
      expect(moved).toEqual([{ kind: 'item', id: 'zebra' }]);
    });

    it('moves a dataset by dropping it on a folder, and opens that folder', () => {
      create();
      const moves: unknown[] = [];
      fixture.componentInstance.move.subscribe((m) => moves.push(m));

      const drop = drag(row('Zebra'), row('Archive'));

      expect(drop.defaultPrevented).toBe(true);
      expect(moves).toEqual([{ item: { kind: 'item', id: 'zebra' }, targetId: 'archive' }]);
    });

    it('moves a folder by dropping it on another, and to the top level by dropping on the empty space', () => {
      create();
      const moves: unknown[] = [];
      fixture.componentInstance.move.subscribe((m) => moves.push(m));

      drag(row('Archive'), row('Sales')); // the drop also opens Sales
      drag(row('Europe'), el().querySelector<HTMLElement>('.tree')!);

      expect(moves).toEqual([
        { item: { kind: 'folder', id: 'archive' }, targetId: 'sales' },
        { item: { kind: 'folder', id: 'europe' }, targetId: null },
      ]);
    });

    it('a dataset dropped on another dataset goes where that one is', () => {
      create();
      const moves: unknown[] = [];
      fixture.componentInstance.move.subscribe((m) => moves.push(m));
      main('Sales').click();
      fixture.detectChanges();

      drag(row('Zebra'), row('Returns'));

      expect(moves).toEqual([{ item: { kind: 'item', id: 'zebra' }, targetId: 'sales' }]);
    });

    it('does not take a drop that is not allowed: into itself, below itself, or where it already is', () => {
      create();
      const moves: unknown[] = [];
      fixture.componentInstance.move.subscribe((m) => moves.push(m));
      main('Sales').click();
      fixture.detectChanges();

      const intoItself = drag(row('Sales'), row('Sales'));
      const belowItself = drag(row('Sales'), row('Europe'));
      const sameFolder = drag(row('Returns'), row('Sales'));

      expect([intoItself, belowItself, sameFolder].map((e) => e.defaultPrevented)).toEqual([false, false, false]);
      expect(moves).toEqual([]);
    });

    it('lights up the folder it would land in while dragging', () => {
      create();

      row('Zebra').dispatchEvent(dragEvent('dragstart'));
      row('Archive').dispatchEvent(dragEvent('dragover'));
      fixture.detectChanges();
      expect(row('Archive').classList).toContain('drop');

      row('Zebra').dispatchEvent(dragEvent('dragend'));
      fixture.detectChanges();
      expect(row('Archive').classList).not.toContain('drop');
    });

    it('cannot drag while a filter is on, because the rows are not where they live', () => {
      create();
      const input = el().querySelector<HTMLInputElement>('input[aria-label="Filter datasets"]')!;
      input.value = 'zeb';
      input.dispatchEvent(new Event('input'));
      fixture.detectChanges();

      expect(row('Zebra').getAttribute('draggable')).toBeNull();
    });
  });

  describe('for viewers', () => {
    it('shows the tree but no menus, no new-folder button and nothing to drag', () => {
      create(false);

      expect(el().querySelector('.more')).toBeNull();
      expect(el().querySelector('button[aria-label="New folder"]')).toBeNull();
      expect(row('Zebra').getAttribute('draggable')).toBeNull();
      const moves: unknown[] = [];
      fixture.componentInstance.move.subscribe((m) => moves.push(m));

      drag(row('Zebra'), row('Archive'));

      expect(moves).toEqual([]);
    });
  });
});
