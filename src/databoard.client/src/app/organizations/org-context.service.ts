import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';

export interface Organization {
  id: string;
  name: string;
  role: 'Viewer' | 'Editor' | 'Owner';
  /** The organization everyone belongs to; it cannot be left. */
  isDefault?: boolean;
}

const STORAGE_KEY = 'databoard.organization';

/** Storage can be blocked (private windows, strict settings); the app must work without it. */
const readStored = (): string | null => {
  try {
    return localStorage.getItem(STORAGE_KEY);
  } catch {
    return null;
  }
};

const writeStored = (id: string): void => {
  try {
    localStorage.setItem(STORAGE_KEY, id);
  } catch {
    // remembering the choice is a convenience only
  }
};

/** The organization the signed-in user is working in. The navigation bar and the home page both follow it. */
@Injectable({ providedIn: 'root' })
export class OrgContext {
  private readonly http = inject(HttpClient);

  readonly organizations = signal<Organization[]>([]);
  readonly loaded = signal(false);
  private readonly selectedId = signal<string | null>(readStored());

  /** The chosen organization if the user still belongs to it, otherwise the default one, otherwise their first. */
  readonly current = computed(() => {
    const all = this.organizations();
    return all.find((o) => o.id === this.selectedId()) ?? all.find((o) => o.isDefault) ?? all[0] ?? null;
  });

  load(): void {
    this.http.get<Organization[]>('/api/organizations').subscribe({
      next: (items) => {
        this.organizations.set(items);
        this.loaded.set(true);
      },
      error: () => this.loaded.set(true),
    });
  }

  select(id: string): void {
    this.selectedId.set(id);
    writeStored(id);
  }

  clear(): void {
    this.organizations.set([]);
    this.loaded.set(false);
  }
}
