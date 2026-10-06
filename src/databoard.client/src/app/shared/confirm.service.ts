import { Component, Injectable, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule } from '@angular/material/dialog';
import { Observable, defer, map } from 'rxjs';

export interface ConfirmOptions {
  /** What is being decided, as a short heading. */
  title?: string;
  /** The question, and what follows from saying yes. Line breaks are kept. */
  message: string;
  /** The button that agrees. It says what it does ("Delete", "Revoke"), not "OK". */
  confirmLabel?: string;
  cancelLabel?: string;
  /** Something that cannot be undone or takes access away: the agreeing button is drawn as a warning and Cancel has the focus. */
  danger?: boolean;
}

/** The look of a question that needs a yes. Closes with `true` for yes and `false` (or nothing, on Escape or a click outside) for no. */
@Component({
  selector: 'app-confirm-dialog',
  imports: [MatDialogModule, MatButtonModule],
  template: `
    <h2 mat-dialog-title>{{ data.title ?? 'Are you sure?' }}</h2>
    <mat-dialog-content><p class="message">{{ data.message }}</p></mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button type="button" class="cancel" [mat-dialog-close]="false" [attr.cdkFocusInitial]="data.danger === true ? '' : null">{{ data.cancelLabel ?? 'Cancel' }}</button>
      <button mat-flat-button type="button" class="confirm" [class.danger]="data.danger === true" [mat-dialog-close]="true" [attr.cdkFocusInitial]="data.danger === true ? null : ''">{{ data.confirmLabel ?? 'Confirm' }}</button>
    </mat-dialog-actions>
  `,
  styles: `
    .message { margin: 0; white-space: pre-line; overflow-wrap: anywhere; }
    .confirm.danger { --mat-button-filled-container-color: var(--mat-sys-error); --mat-button-filled-label-text-color: var(--mat-sys-on-error); }
  `,
})
export class ConfirmDialogComponent {
  protected readonly data = inject<ConfirmOptions>(MAT_DIALOG_DATA);
}

/**
 * Asks the person to confirm, in a Material dialog instead of the browser's own box. Subscribe and act on `true`:
 *
 *     this.confirms.ask({ message: 'Delete it?', confirmLabel: 'Delete', danger: true })
 *       .subscribe((yes) => { if (yes) this.delete(); });
 *
 * It completes after one answer. Closing the dialog any other way counts as no.
 */
@Injectable({ providedIn: 'root' })
export class ConfirmService {
  private readonly dialog = inject(MatDialog);

  ask(options: ConfirmOptions | string): Observable<boolean> {
    const data: ConfirmOptions = typeof options === 'string' ? { message: options } : options;
    // Deferred: the dialog opens when somebody subscribes (once per subscription), not when the question is only built.
    return defer(() =>
      this.dialog
        .open<ConfirmDialogComponent, ConfirmOptions, boolean>(ConfirmDialogComponent, {
          data,
          width: '26rem',
          maxWidth: '95vw',
          autoFocus: 'first-tabbable',
        })
        .afterClosed(),
    ).pipe(map((answer) => answer === true));
  }
}
