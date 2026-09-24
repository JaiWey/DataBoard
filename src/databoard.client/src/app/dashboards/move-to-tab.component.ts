import { Component, input, output } from '@angular/core';
import { MatMenuModule } from '@angular/material/menu';
import { DashboardTab } from './dashboards.service';

/** "Move to tab": a button with a menu of the other tabs. Shows nothing when there is no other tab to go to. */
@Component({
  selector: 'app-move-to-tab',
  imports: [MatMenuModule],
  template: `
    @if (others().length > 0) {
      <button type="button" class="move" aria-label="Move to another tab" [matMenuTriggerFor]="menu">Move to tab</button>
      <mat-menu #menu="matMenu">
        @for (tab of others(); track tab.id) {
          <button mat-menu-item type="button" (click)="pick.emit(tab.id)">{{ tab.name }}</button>
        }
      </mat-menu>
    }
  `,
  styles: `
    .move { border: 1px solid var(--mat-sys-outline); background: transparent; color: inherit; border-radius: 1rem; padding: 0.3rem 0.8rem; font: inherit; cursor: pointer; }
    .move:hover { background: var(--mat-sys-surface-container); }
  `,
})
export class MoveToTabComponent {
  readonly tabs = input.required<DashboardTab[]>();
  /** The tab the widget is on now, which is left out of the menu. */
  readonly current = input<string | null>(null);
  readonly pick = output<string>();

  protected others = () => this.tabs().filter((t) => t.id !== this.current());
}
