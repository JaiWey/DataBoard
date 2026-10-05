import { Folder } from './folders.service';

/** Anything that can sit in a folder: a dataset, a chart or a dashboard, as far as the tree is concerned. */
export interface TreeEntry {
  id: string;
  name: string;
  /** A short second line under the name. */
  subline?: string;
  /** The folder it is in; null or missing is the top level. */
  folderId?: string | null;
  /** Who may see it is limited: shown with a lock. */
  restricted?: boolean;
}

/** Folders can be nested this many levels deep (a top level folder is level 1). Mirrors the server's limit. */
export const MAX_FOLDER_DEPTH = 8;

export type TreeRow =
  | { kind: 'folder'; id: string; name: string; depth: number; expanded: boolean; childCount: number; folder: Folder }
  | { kind: 'item'; id: string; name: string; depth: number; entry: TreeEntry };

/** What is being dragged or moved. */
export type TreeItem = { kind: 'folder' | 'item'; id: string };

const byName = (a: { name: string }, b: { name: string }) => a.name.localeCompare(b.name, undefined, { sensitivity: 'base' });

/** The folders that have this folder as parent (null for the top level), A to Z. */
export const childFolders = (folders: Folder[], parentId: string | null): Folder[] =>
  folders.filter((f) => (f.parentId ?? null) === parentId).sort(byName);

export const itemsIn = (entries: TreeEntry[], folderId: string | null): TreeEntry[] =>
  entries.filter((d) => (d.folderId ?? null) === folderId).sort(byName);

/** 1 for a top level folder, 2 for a folder inside it, and so on. 0 for the top level itself. */
export function depthOf(folders: Folder[], folderId: string | null): number {
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
export function heightOf(folders: Folder[], folderId: string): number {
  const below = childFolders(folders, folderId).map((c) => heightOf(folders, c.id));
  return 1 + Math.max(0, ...below);
}

/** The ids of the folder and all folders below it. */
export function subtreeIds(folders: Folder[], folderId: string): Set<string> {
  const ids = new Set<string>([folderId]);
  for (const child of childFolders(folders, folderId)) subtreeIds(folders, child.id).forEach((id) => ids.add(id));
  return ids;
}

/** The ids of the folders from the top down to (and including) this one. */
export function ancestorIds(folders: Folder[], folderId: string | null): string[] {
  const byId = new Map(folders.map((f) => [f.id, f]));
  const path: string[] = [];
  for (let id = folderId; id !== null && byId.has(id) && !path.includes(id); id = byId.get(id)!.parentId ?? null) path.unshift(id);
  return path;
}

/** "Sales / Europe" for the folder, or "" for the top level. */
export const folderPath = (folders: Folder[], folderId: string | null): string =>
  ancestorIds(folders, folderId).map((id) => folders.find((f) => f.id === id)!.name).join(' / ');

/**
 * Can the item go into this folder (null is the top level)? The same rules the server applies: a folder cannot go
 * into itself or below itself, nesting stops at {@link MAX_FOLDER_DEPTH} levels, and moving to where it already is
 * changes nothing.
 */
export function canMoveTo(folders: Folder[], entries: TreeEntry[], item: TreeItem, targetId: string | null): boolean {
  if (targetId !== null && !folders.some((f) => f.id === targetId)) return false;
  if (item.kind === 'item') {
    const entry = entries.find((d) => d.id === item.id);
    return !!entry && (entry.folderId ?? null) !== targetId;
  }
  const folder = folders.find((f) => f.id === item.id);
  if (!folder || (folder.parentId ?? null) === targetId) return false;
  if (targetId !== null && subtreeIds(folders, item.id).has(targetId)) return false;
  return depthOf(folders, targetId) + heightOf(folders, item.id) <= MAX_FOLDER_DEPTH;
}

/**
 * The rows of the tree, top to bottom: in each folder the subfolders first, then the items, each A to Z.
 * A folder's contents show only while it is expanded. A filter shows the items whose name matches (and the folders
 * on the way to them, open), plus everything inside a folder whose own name matches.
 */
export function buildRows(folders: Folder[], entries: TreeEntry[], expanded: ReadonlySet<string>, filter = ''): TreeRow[] {
  const needle = filter.trim().toLowerCase();
  const matches = (name: string) => name.toLowerCase().includes(needle);

  // With a filter, work out which folders and items are shown.
  let shownFolders: Set<string> | null = null;
  let shownItems: Set<string> | null = null;
  if (needle) {
    shownFolders = new Set();
    shownItems = new Set();
    for (const folder of folders) {
      if (!matches(folder.name)) continue;
      const inside = subtreeIds(folders, folder.id);
      inside.forEach((id) => shownFolders!.add(id));
      ancestorIds(folders, folder.id).forEach((id) => shownFolders!.add(id));
      entries.filter((d) => d.folderId && inside.has(d.folderId)).forEach((d) => shownItems!.add(d.id));
    }
    for (const entry of entries) {
      if (!matches(entry.name)) continue;
      shownItems.add(entry.id);
      ancestorIds(folders, entry.folderId ?? null).forEach((id) => shownFolders!.add(id));
    }
  }

  const rows: TreeRow[] = [];
  const visit = (parentId: string | null, depth: number): void => {
    for (const folder of childFolders(folders, parentId)) {
      if (shownFolders && !shownFolders.has(folder.id)) continue;
      const open = shownFolders ? true : expanded.has(folder.id);
      const count = childFolders(folders, folder.id).length + itemsIn(entries, folder.id).length;
      rows.push({ kind: 'folder', id: folder.id, name: folder.name, depth, expanded: open, childCount: count, folder });
      if (open) visit(folder.id, depth + 1);
    }
    for (const entry of itemsIn(entries, parentId)) {
      if (shownItems && !shownItems.has(entry.id)) continue;
      rows.push({ kind: 'item', id: entry.id, name: entry.name, depth, entry });
    }
  };
  visit(null, 0);
  return rows;
}