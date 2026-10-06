import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialog, MatDialogRef } from '@angular/material/dialog';
import { of } from 'rxjs';
import { answerConfirm } from './confirm.testing';
import { ConfirmDialogComponent, ConfirmOptions, ConfirmService } from './confirm.service';

describe('ConfirmDialogComponent', () => {
  const close = vi.fn();

  const open = (data: ConfirmOptions) => {
    close.mockReset();
    TestBed.configureTestingModule({
      providers: [{ provide: MAT_DIALOG_DATA, useValue: data }, { provide: MatDialogRef, useValue: { close } }],
    });
    const fixture = TestBed.createComponent(ConfirmDialogComponent);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  };
  const button = (el: HTMLElement, cls: string) => el.querySelector(`button.${cls}`) as HTMLButtonElement;

  it('shows the heading and the question, and offers Cancel and Confirm when nothing else is said', () => {
    const el = open({ message: 'Delete it?' });

    expect(el.querySelector('h2')?.textContent).toBe('Are you sure?');
    expect(el.querySelector('.message')?.textContent).toBe('Delete it?');
    expect(button(el, 'cancel').textContent?.trim()).toBe('Cancel');
    expect(button(el, 'confirm').textContent?.trim()).toBe('Confirm');
  });

  it('uses the words it is given, so the button says what it does', () => {
    const el = open({ title: 'Delete dashboard', message: 'Delete "Sales"?', confirmLabel: 'Delete', cancelLabel: 'Keep it' });

    expect(el.querySelector('h2')?.textContent).toBe('Delete dashboard');
    expect(button(el, 'confirm').textContent?.trim()).toBe('Delete');
    expect(button(el, 'cancel').textContent?.trim()).toBe('Keep it');
  });

  it('closes with true for the agreeing button and false for Cancel', () => {
    const el = open({ message: 'Sure?' });

    button(el, 'confirm').click();
    expect(close).toHaveBeenLastCalledWith(true);
    button(el, 'cancel').click();
    expect(close).toHaveBeenLastCalledWith(false);
  });

  it('draws a dangerous question as a warning and gives Cancel the focus, a harmless one the other way round', () => {
    const danger = open({ message: 'Delete?', danger: true });
    expect(button(danger, 'confirm').classList).toContain('danger');
    expect(button(danger, 'cancel').hasAttribute('cdkFocusInitial')).toBe(true);
    expect(button(danger, 'confirm').hasAttribute('cdkFocusInitial')).toBe(false);

    TestBed.resetTestingModule();
    const harmless = open({ message: 'Leave?' });
    expect(button(harmless, 'confirm').classList).not.toContain('danger');
    expect(button(harmless, 'confirm').hasAttribute('cdkFocusInitial')).toBe(true);
    expect(button(harmless, 'cancel').hasAttribute('cdkFocusInitial')).toBe(false);
  });

  it('keeps line breaks in the question and shows it as text, never as markup', () => {
    const el = open({ message: 'First line\nSecond <b>line</b>' });

    expect(el.querySelector('.message')?.textContent).toBe('First line\nSecond <b>line</b>');
    expect(el.querySelector('.message b')).toBeNull();
    expect(getComputedStyle(el.querySelector('.message')!).whiteSpace).toBe('pre-line');
  });
});

describe('ConfirmService', () => {
  const answered = (answer: unknown) => {
    const open = vi.fn().mockReturnValue({ afterClosed: () => of(answer) });
    TestBed.configureTestingModule({ providers: [{ provide: MatDialog, useValue: { open } }] });
    return { service: TestBed.inject(ConfirmService), open };
  };
  const result = (observable: ReturnType<ConfirmService['ask']>) => {
    let value: boolean | undefined;
    observable.subscribe((v) => (value = v));
    return value;
  };

  it('says yes only when the dialog closed with true', () => {
    expect(result(answered(true).service.ask('Sure?'))).toBe(true);
  });

  it.each([false, undefined, null, 'true', 1])('says no for anything else, such as %j (Escape or a click outside gives undefined)', (answer) => {
    TestBed.resetTestingModule();
    expect(result(answered(answer).service.ask('Sure?'))).toBe(false);
  });

  it('opens one dialog with the options, and takes a plain string as the question', () => {
    const { service, open } = answered(true);

    service.ask({ title: 'T', message: 'M', danger: true }).subscribe();
    service.ask('Just a question').subscribe();

    expect(open).toHaveBeenCalledTimes(2);
    expect(open.mock.calls[0][0]).toBe(ConfirmDialogComponent);
    expect(open.mock.calls[0][1].data).toEqual({ title: 'T', message: 'M', danger: true });
    expect(open.mock.calls[1][1].data).toEqual({ message: 'Just a question' });
    expect(open.mock.calls[0][1].maxWidth).toBe('95vw');
  });

  it('opens nothing until somebody subscribes, and then one dialog per subscription', () => {
    const { service, open } = answered(true);

    const question = service.ask('Sure?');
    expect(open).not.toHaveBeenCalled();

    question.subscribe();
    expect(open).toHaveBeenCalledTimes(1);
    question.subscribe();
    expect(open).toHaveBeenCalledTimes(2);
  });
});

describe('answerConfirm (the helper the other specs use)', () => {
  afterEach(() => vi.restoreAllMocks());

  it('answers without a dialog, keeps what was asked, and can change its mind', () => {
    const stub = answerConfirm(false);
    const service = TestBed.inject(ConfirmService);
    const got: boolean[] = [];

    service.ask({ title: 'First', message: 'One?' }).subscribe((v) => got.push(v));
    stub.answer(true);
    service.ask('Two?').subscribe((v) => got.push(v));
    stub.answers(false);
    service.ask('Three?').subscribe((v) => got.push(v));
    service.ask('Four?').subscribe((v) => got.push(v));

    expect(got).toEqual([false, true, false, true]);
    expect(stub.messages).toEqual(['One?', 'Two?', 'Three?', 'Four?']);
    expect(stub.questions[0].title).toBe('First');
    expect(stub.ask).toHaveBeenCalledTimes(4);
  });
});
