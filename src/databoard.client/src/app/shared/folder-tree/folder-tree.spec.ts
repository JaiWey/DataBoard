import { MAX_FOLDER_DEPTH, TreeEntry, ancestorIds, buildRows, canMoveTo, depthOf, folderPath, heightOf, subtreeIds } from './folder-tree';
import { Folder } from './folders.service';

const folder = (id: string, name: string, parentId: string | null = null): Folder => ({ id, name, parentId });
const dataset = (id: string, name: string, folderId: string | null = null): TreeEntry => ({ id, name, folderId });

// Sales > Europe > Q1,  Archive,  datasets: Orders (Europe), Returns (Sales), Zebra (top), alpha (top)
const folders = [folder('sales', 'Sales'), folder('europe', 'Europe', 'sales'), folder('q1', 'Q1', 'europe'), folder('archive', 'Archive')];
const datasets = [dataset('orders', 'Orders', 'europe'), dataset('returns', 'Returns', 'sales'), dataset('zebra', 'Zebra'), dataset('alpha', 'alpha')];

const rows = (expanded: string[] = [], filter = '') =>
  buildRows(folders, datasets, new Set(expanded), filter).map((r) => `${'  '.repeat(r.depth)}${r.kind === 'folder' ? '[' + r.name + ']' : r.name}`);

describe('buildRows', () => {
  it('shows folders first, then datasets, each A to Z ignoring case, with everything collapsed', () => {
    expect(rows()).toEqual(['[Archive]', '[Sales]', 'alpha', 'Zebra']);
  });

  it('shows the contents of an expanded folder one level in, subfolders before datasets', () => {
    expect(rows(['sales'])).toEqual(['[Archive]', '[Sales]', '  [Europe]', '  Returns', 'alpha', 'Zebra']);
    expect(rows(['sales', 'europe', 'q1'])).toEqual(['[Archive]', '[Sales]', '  [Europe]', '    [Q1]', '    Orders', '  Returns', 'alpha', 'Zebra']);
  });

  it('does not show what is inside a collapsed folder even if its parent is open', () => {
    expect(rows(['europe'])).toEqual(['[Archive]', '[Sales]', 'alpha', 'Zebra']);
  });

  it('counts what is in each folder and knows whether it is open', () => {
    const sales = buildRows(folders, datasets, new Set(['sales'])).find((r) => r.id === 'sales');

    expect(sales).toMatchObject({ kind: 'folder', childCount: 2, expanded: true });
    expect(buildRows(folders, datasets, new Set()).find((r) => r.id === 'archive')).toMatchObject({ childCount: 0, expanded: false });
  });

  it('a filter shows matching datasets with the folders on the way, open, and hides the rest', () => {
    expect(rows([], 'ord')).toEqual(['[Sales]', '  [Europe]', '    Orders']);
    expect(rows([], 'ZEB')).toEqual(['Zebra']);
    expect(rows([], 'nothing')).toEqual([]);
  });

  it('a filter that matches a folder shows everything inside it', () => {
    expect(rows([], 'europe')).toEqual(['[Sales]', '  [Europe]', '    [Q1]', '    Orders']);
  });

  it('ignores a blank filter', () => {
    expect(rows([], '   ')).toEqual(rows());
  });
});

describe('folder helpers', () => {
  it('measures depth from the top and height down', () => {
    expect(depthOf(folders, null)).toBe(0);
    expect(depthOf(folders, 'sales')).toBe(1);
    expect(depthOf(folders, 'q1')).toBe(3);
    expect(heightOf(folders, 'sales')).toBe(3);
    expect(heightOf(folders, 'archive')).toBe(1);
  });

  it('finds the subtree, the ancestors and the path', () => {
    expect([...subtreeIds(folders, 'sales')].sort()).toEqual(['europe', 'q1', 'sales']);
    expect(ancestorIds(folders, 'q1')).toEqual(['sales', 'europe', 'q1']);
    expect(ancestorIds(folders, null)).toEqual([]);
    expect(folderPath(folders, 'q1')).toBe('Sales / Europe / Q1');
    expect(folderPath(folders, null)).toBe('');
  });

  it('does not loop on a broken tree', () => {
    const loop = [folder('a', 'A', 'b'), folder('b', 'B', 'a')];

    expect(depthOf(loop, 'a')).toBe(2);
    expect(ancestorIds(loop, 'a').length).toBe(2);
  });
});

describe('canMoveTo', () => {
  const can = (kind: 'folder' | 'item', id: string, target: string | null) => canMoveTo(folders, datasets, { kind, id }, target);

  it('lets a dataset go into another folder or the top, but not where it already is', () => {
    expect(can('item', 'orders', 'archive')).toBe(true);
    expect(can('item', 'orders', null)).toBe(true);
    expect(can('item', 'orders', 'europe')).toBe(false);
    expect(can('item', 'zebra', null)).toBe(false);
  });

  it('lets a folder go anywhere that is not itself, below itself or where it already is', () => {
    expect(can('folder', 'archive', 'europe')).toBe(true);
    expect(can('folder', 'europe', null)).toBe(true);
    expect(can('folder', 'sales', 'sales')).toBe(false);
    expect(can('folder', 'sales', 'q1')).toBe(false);
    expect(can('folder', 'europe', 'sales')).toBe(false);
  });

  it('refuses unknown targets and unknown items', () => {
    expect(can('item', 'orders', 'nope')).toBe(false);
    expect(can('item', 'nope', null)).toBe(false);
    expect(can('folder', 'nope', null)).toBe(false);
  });

  it('counts the whole subtree against the depth limit', () => {
    const chain = Array.from({ length: MAX_FOLDER_DEPTH - 1 }, (_, i) => folder(`c${i}`, `C${i}`, i === 0 ? null : `c${i - 1}`));
    const all = [...chain, folder('one', 'One'), folder('two', 'Two'), folder('twoChild', 'Two child', 'two')];
    const last = `c${MAX_FOLDER_DEPTH - 2}`;

    expect(canMoveTo(all, [], { kind: 'folder', id: 'one' }, last)).toBe(true); // level 8
    expect(canMoveTo(all, [], { kind: 'folder', id: 'two' }, last)).toBe(false); // its child would be level 9
  });
});
