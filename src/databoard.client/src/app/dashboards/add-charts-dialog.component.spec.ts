import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { provideRouter } from '@angular/router';
import { AddChartsDialogComponent } from './add-charts-dialog.component';

const ORG = '11111111-1111-1111-1111-111111111111';
const chart = (id: string, name: string, type: string) => ({
  id, name, description: null, datasetId: 'ds-1', type, createdUtc: '', updatedUtc: '',
  definition: { query: {}, style: { legend: true, labels: false, palette: 'default' } },
});

describe('AddChartsDialogComponent', () => {
  const close = vi.fn();

  const setup = (charts = [chart('c1', 'Revenue', 'Bar'), chart('c2', 'Share', 'BarHorizontal'), chart('c3', 'Orders', 'Table')]) => {
    close.mockReset();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: MAT_DIALOG_DATA, useValue: { orgId: ORG, charts, datasetNames: { 'ds-1': 'Orders data' } } },
        { provide: MatDialogRef, useValue: { close } },
      ],
    });
    const fixture = TestBed.createComponent(AddChartsDialogComponent);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const button = (text: string) => Array.from(el.querySelectorAll('button')).find((b) => b.textContent?.trim().startsWith(text)) as HTMLButtonElement;
    const tick = (name: string) => {
      const box = Array.from(el.querySelectorAll('mat-checkbox')).find((c) => c.textContent?.trim().startsWith(name))!.querySelector('input') as HTMLInputElement;
      box.click();
      fixture.detectChanges();
    };
    return { fixture, el, button, tick };
  };

  it('lists each chart with its type and dataset', () => {
    const { el } = setup();

    const rows = Array.from(el.querySelectorAll('mat-checkbox')).map((c) => c.textContent?.replace(/\s+/g, ' ').trim());
    expect(rows).toEqual(['Revenue Bar · Orders data', 'Share Horizontal bar · Orders data', 'Orders Table · Orders data']);
  });

  it('cannot add until something is ticked, and says how many', () => {
    const { button, tick } = setup();
    expect(button('Add').disabled).toBe(true);

    tick('Revenue');
    tick('Orders');

    expect(button('Add').disabled).toBe(false);
    expect(button('Add').textContent?.trim()).toBe('Add 2');
  });

  it('closes with the chosen charts in the order they were listed', () => {
    const { button, tick, fixture } = setup();

    tick('Orders');
    tick('Revenue');
    button('Add').click();
    fixture.detectChanges();

    expect(close).toHaveBeenCalledWith(['c1', 'c3']);
  });

  it('unticking takes a chart out again', () => {
    const { button, tick, fixture } = setup();

    tick('Revenue');
    tick('Revenue');
    tick('Share');
    button('Add').click();
    fixture.detectChanges();

    expect(close).toHaveBeenCalledWith(['c2']);
  });

  it('says so when there is nothing left to add, and still offers to create a chart', () => {
    const { el } = setup([]);

    expect(el.textContent).toContain('Every chart is already on this dashboard, or there are no charts yet.');
    const link = Array.from(el.querySelectorAll('a')).find((a) => a.textContent?.includes('Create a new chart'));
    expect(link?.getAttribute('href')).toBe(`/organizations/${ORG}/charts/new`);
  });
});
