import { Dataset, DatasetFolder } from './datasets.service';

/** Folders can be nested this many levels deep (a top level folder is level 1). Mirrors the server's limit. */
export const MAX_FOLDER_DEPTH = 8;

export type TreeRow =
  | { kind: 'folder'; id: string; name: string; depth: number; expanded: boolean; childCount: number; folder: DatasetFolder }
  | { kind: 'dataset'; id: string; name: string; depth: number; dataset: Dataset };

/** What is being dragged or moved. */
export type TreeItem = { kind: 'folder' | 'dataset'; id: string };

const byName = (a: { name: string }, b: { name: string }) => a.name.localeCompare(b.name, undefined, { sensitivity: 'base' });

/** The folders that have this folder as parent (null for the top level), A to Z. */
export const childFolders = (folders: DatasetFolder[], parentId: string | null): DatasetFolder[] =>
  folders.filter((f) => (f.parentId ?? null) === parentId).sort(byName);

export const datasetsIn = (datasets: Dataset[], folderId: string | null): Dataset[] =>
  datasets.filter((d) => (d.folderId ?? null) === folderId).sort(byName);

/** 1 for a top level folder, 2 for a folder inside it, and so on. 0 for the top level itself. */
export function depthOf(folders: DatasetFolder[], folderId: string | null): number {
  const byId = new Map(folders.map((f) => [f.id, f]));
  let depth = 0;
  const seen = new Set<string>();
  for (let id = folderId; id !== null && byId.has(id) && !seen.has(id); id = byId.get(id)!.parentId ?? null) {
    seen.add(id);
    depth++;
  }
  return depth;
}

/** How many levels the folder and everything below it take; a folder without subfolders is 1. */
export function heightOf(folders: DatasetFolder[], folderId: string): number {
  const below = childFolders(folders, folderId).map((c) => heightOf(folders, c.id));
  return 1 + Math.max(0, ...below);
}

/** The ids of the folder and all folders below it. */
export function subtreeIds(folders: DatasetFolder[], folderId: string): Set<string> {
  const ids = new Set<string>([folderId]);
  for (const child of childFolders(folders, folderId)) subtreeIds(folders, child.id).forEach((id) => ids.add(id));
  return ids;
}

/** The ids of the folders from the top down to (and including) this one. */
export function ancestorIds(folders: DatasetFolder[], folderId: string | null): string[] {
  const byId = new Map(folders.map((f) => [f.id, f]));
  const path: string[] = [];
  for (let id = folderId; id !== null && byId.has(id) && !path.includes(id); id = byId.get(id)!.parentId ?? null) path.unshift(id);
  return path;
}

/** "Sales / Europe" for the folder, or "" for the top level. */
export const folderPath = (folders: DatasetFolder[], folderId: string | null): string =>
  ancestorIds(folders, folderId).map((id) => folders.find((f) => f.id === id)!.name).join(' / ');

/**
 * Can the item go into this folder (null is the top level)? The same rules the server applies: a folder cannot go
 * into itself or below itself, nesting stops at {@link MAX_FOLDER_DEPTH} levels, and moving to where it already is
 * changes nothing.
 */
export function canMoveTo(folders: DatasetFolder[], datasets: Dataset[], item: TreeItem, targetId: string | null): boolean {
  if (targetId !== null && !folders.some((f) => f.id === targetId)) return false;
  if (item.kind === 'dataset') {
    const dataset = datasets.find((d) => d.id === item.id);
    return !!dataset && (dataset.folderId ?? null) !== targetId;
  }
  const folder = folders.find((f) => f.id === item.id);
  if (!folder || (folder.parentId ?? null) === targetId) return false;
  if (targetId !== null && subtreeIds(folders, item.id).has(targetId)) return false;
  return depthOf(folders, targetId) + heightOf(folders, item.id) <= MAX_FOLDER_DEPTH;
}

/**
 * The rows of the tree, top to bottom: in each folder the subfolders first, then the datasets, each A to Z.
 * A folder's contents show only while it is expanded. A filter shows the datasets whose name matches (and the folders
 * on the way to them, open), plus everything inside a folder whose own name matches.
 */
export function buildRows(folders: DatasetFolder[], datasets: Dataset[], expanded: ReadonlySet<string>, filter = ''): TreeRow[] {
  const needle = filter.trim().toLowerCase();
  const matches = (name: string) => name.toLowerCase().includes(needle);

  // With a filter, work out which folders and datasets are shown.
  let shownFolders: Set<string> | null = null;
  let shownDatasets: Set<string> | null = null;
  if (needle) {
    shownFolders = new Set();
    shownDatasets = new Set();
    for (const folder of folders) {
      if (!matches(folder.name)) continue;
      const inside = subtreeIds(folders, folder.id);
      inside.forEach((id) => shownFolders!.add(id));
      ancestorIds(folders, folder.id).forEach((id) => shownFolders!.add(id));
      datasets.filter((d) => d.folderId && inside.has(d.folderId)).forEach((d) => shownDatasets!.add(d.id));
    }
    for (const dataset of datasets) {
      if (!matches(dataset.name)) continue;
      shownDatasets.add(dataset.id);
      ancestorIds(folders, dataset.folderId ?? null).forEach((id) => shownFolders!.add(id));
    }
  }

  const rows: TreeRow[] = [];
  const visit = (parentId: string | null, depth: number): void => {
    for (const folder of childFolders(folders, parentId)) {
      if (shownFolders && !shownFolders.has(folder.id)) continue;
      const open = shownFolders ? true : expanded.has(folder.id);
      const count = childFolders(folders, folder.id).length + datasetsIn(datasets, folder.id).length;
      rows.push({ kind: 'folder', id: folder.id, name: folder.name, depth, expanded: open, childCount: count, folder });
      if (open) visit(folder.id, depth + 1);
    }
    for (const dataset of datasetsIn(datasets, parentId)) {
      if (shownDatasets && !shownDatasets.has(dataset.id)) continue;
      rows.push({ kind: 'dataset', id: dataset.id, name: dataset.name, depth, dataset });
    }
  };
  visit(null, 0);
  return rows;
}
