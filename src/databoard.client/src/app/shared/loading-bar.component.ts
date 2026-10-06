import { Component, inject } from '@angular/core';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { LoadingService } from './loading.service';

/** A thin bar across the top of the page while the app waits on the server (see `LoadingService` for when it shows). */
@Component({
  selector: 'app-loading-bar',
  imports: [MatProgressBarModule],
  template: `
    @if (loading.visible()) {
      <mat-progress-bar class="bar no-print" mode="indeterminate" aria-label="Loading" />
    }
  `,
  styles: `
    .bar { position: fixed; top: 0; left: 0; right: 0; z-index: 2000; --mat-progress-bar-track-height: 3px; --mat-progress-bar-active-indicator-height: 3px; }
  `,
})
export class LoadingBarComponent {
  protected readonly loading = inject(LoadingService);
}
