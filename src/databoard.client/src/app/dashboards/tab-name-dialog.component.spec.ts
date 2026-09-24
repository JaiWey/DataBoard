import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { TabNameDialogComponent, TabNameDialogData } from './tab-name-dialog.component';

describe('TabNameDialogComponent', () => {
  let fixture: ComponentFixture<TabNameDialogComponent>;
  let close: ReturnType<typeof vi.fn>;
  const el = () => fixture.nativeElement as HTMLElement;
  const input = () => el().querySelector('[aria-label="Tab name"]') as HTMLInputElement;
  const submit = () => el().querySelector('button[type=submit]') as HTMLButtonElement;
  const type = (value: string) => {
    input().value = value;
    input().dispatchEvent(new Event('input'));
    fixture.detectChanges();
  };

  const create = (data: TabNameDialogData) => {
    close = vi.fn();
    TestBed.configureTestingModule({
      providers: [{ provide: MAT_DIALOG_DATA, useValue: data }, { provide: MatDialogRef, useValue: { close } }],
    });
    fixture = TestBed.createComponent(TabNameDialogComponent);
    fixture.detectChanges();
  };

  it('asks for a new name and cannot be saved until one is typed, spaces not counting', () => {
    create({ taken: [] });
    expect(el().querySelector('h2')?.textContent).toContain('New tab');
    expect(submit().disabled).toBe(true);

    type('   ');
    expect(submit().disabled).toBe(true);
    type('Costs');
    expect(submit().disabled).toBe(false);
  });

  it('closes with the trimmed name', () => {
    create({ taken: [] });
    type('  Costs ');

    submit().click();
    el().querySelector('form')!.dispatchEvent(new Event('submit'));

    expect(close).toHaveBeenCalledWith('Costs');
  });

  it('refuses a name another tab has, ignoring case and spaces, and says so', () => {
    create({ taken: ['Overview', 'Costs'] });

    type(' costs ');

    expect(submit().disabled).toBe(true);
    expect(el().querySelector('[role=alert]')?.textContent).toContain('already has that name');
  });

  it('starts from the current name when renaming, which may be kept as it is', () => {
    create({ name: 'Costs', taken: ['Overview'] });

    expect(el().querySelector('h2')?.textContent).toContain('Rename tab');
    expect(input().value).toBe('Costs');
    expect(submit().disabled).toBe(false);
  });

  it('limits how long a name can be', () => {
    create({ taken: [] });

    expect(input().maxLength).toBe(60);
  });
});
