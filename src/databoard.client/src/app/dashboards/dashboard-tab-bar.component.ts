import { Component, input, output } from '@angular/core';
import { MatMenuModule } from '@angular/material/menu';
import { MAX_TABS } from './dashboard-tabs';
import { DashboardTab } from './dashboards.service';

/**
 * The tabs of a dashboard, as a row of buttons. Everyone can switch tabs; editors can also add, rename and remove them
 * (never while the layout is being arranged). A dashboard with no tabs shows nothing but an "Add tab" button to editors.
 * It only reports what was chosen; the dashboard does the work.
 */
@Component({
  selector: 'app-dashboard-tab-bar',
  imports: [MatMenuModule],
  template: `
    @if (tabs().length > 0 || canEdit()) {
      <div class="bar" role="tablist" aria-label="Dashboard tabs">
        @for (tab of tabs(); track tab.id) {
          <span class="tab" [class.active]="tab.id === activeId()">
            <button type="button" role="tab" class="name" [attr.aria-selected]="tab.id === activeId()" (click)="select.emit(tab.id)">{{ tab.name }}</button>
            @if (canEdit() && !editing() && tab.id === activeId()) {
              <button type="button" class="more" [attr.aria-label]="'Actions for the tab ' + tab.name" [matMenuTriggerFor]="menu" [matMenuTriggerData]="{ tab }">⋮</button>
            }
          </span>
        }
        @if (canEdit() && !editing() && tabs().length < max) {
          <button type="button" class="add" (click)="add.emit()">{{ tabs().length === 0 ? 'Add tab' : '+' }}</button>
        }
      </div>
    }

    <mat-menu #menu="matMenu">
      <ng-template matMenuContent let-tab="tab">
        <button mat-menu-item type="button" (click)="rename.emit(tab)">Rename</button>
        <button mat-menu-item type="button" [disabled]="tabs().length < 2" (click)="remove.emit(tab)">Remove</button>
        <button mat-menu-item type="button" [disabled]="tabs()[0]?.id === tab.id" (click)="moveEarlier.emit(tab)">Move left</button>
        <button mat-menu-item type="button" [disabled]="tabs()[tabs().length - 1]?.id === tab.id" (click)="moveLater.emit(tab)">Move right</button>
      </ng-template>
    </mat-menu>
  `,
  styles: `
    :host { display: block; }
    .bar { display: flex; flex-wrap: wrap; align-items: center; gap: 0.25rem; border-bottom: 1px solid var(--mat-sys-outline-variant); margin: 0 0 0.75rem; }
    .tab { display: inline-flex; align-items: center; border-radius: 0.5rem 0.5rem 0 0; border-bottom: 2px solid transparent; }
    .tab.active { border-bottom-color: var(--mat-sys-primary); }
    button { border: 0; background: transparent; color: inherit; font: inherit; cursor: pointer; }
    .name { padding: 0.5rem 0.9rem; }
    .tab.active .name { font-weight: 600; color: var(--mat-sys-primary); }
    .more { padding: 0.25rem 0.4rem; border-radius: 0.4rem; line-height: 1; }
    .name:hover, .more:hover, .add:hover { background: var(--mat-sys-surface-container); }
    .add { padding: 0.4rem 0.8rem; color: var(--mat-sys-primary); }
    button:focus-visible { outline: 2px solid var(--mat-sys-primary); outline-offset: -2px; }
    @media print { :host { display: none; } }
  `,
})
export class DashboardTabBarComponent {
  readonly tabs = input.required<DashboardTab[]>();
  readonly activeId = input<string | null>(null);
  readonly canEdit = input(false);
  /** While the layout is being arranged the tabs cannot be changed (only switched). */
  readonly editing = input(false);

  readonly select = output<string>();
  readonly add = output<void>();
  readonly rename = output<DashboardTab>();
  readonly remove = output<DashboardTab>();
  readonly moveEarlier = output<DashboardTab>();
  readonly moveLater = output<DashboardTab>();

  protected readonly max = MAX_TABS;
}
