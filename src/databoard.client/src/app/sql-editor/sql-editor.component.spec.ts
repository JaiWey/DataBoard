import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { fakeMonaco } from './fake-monaco.testing';
import { MONACO_LOADER, SqlEditorComponent } from './sql-editor.component';

@Component({
  imports: [SqlEditorComponent],
  template: `<app-sql-editor [value]="value()" [readOnly]="readOnly()" (valueChange)="changes.push($event)" (save)="saves = saves + 1" />`,
})
class Host {
  readonly value = signal('SELECT 1');
  readonly readOnly = signal(false);
  changes: string[] = [];
  saves = 0;
}

describe('SqlEditorComponent', () => {
  const setup = async () => {
    const monaco = fakeMonaco();
    TestBed.configureTestingModule({ providers: [{ provide: MONACO_LOADER, useValue: monaco.loader }] });
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    await fixture.whenStable();
    return { fixture, monaco, host: fixture.componentInstance };
  };

  it('creates an editor holding the given SQL', async () => {
    const { monaco } = await setup();

    expect(monaco.editors.length).toBe(1);
    expect(monaco.editors[0].value).toBe('SELECT 1');
    expect(monaco.editors[0].readOnly).toBe(false);
  });

  it('reports what the user types and Ctrl+S', async () => {
    const { monaco, host } = await setup();

    monaco.editors[0].type('SELECT 2');
    monaco.editors[0].pressSave();

    expect(host.changes).toEqual(['SELECT 2']);
    expect(host.saves).toBe(1);
  });

  it('follows the inputs without recreating the editor', async () => {
    const { fixture, monaco, host } = await setup();

    host.value.set('SELECT 3');
    host.readOnly.set(true);
    fixture.detectChanges();

    expect(monaco.editors.length).toBe(1);
    expect(monaco.editors[0].value).toBe('SELECT 3');
    expect(monaco.editors[0].readOnly).toBe(true);
  });

  it('disposes the editor with the component', async () => {
    const { fixture, monaco } = await setup();

    fixture.destroy();

    expect(monaco.editors[0].disposed).toBe(true);
  });

  it('does not create an editor if it was destroyed while Monaco was loading', async () => {
    const monaco = fakeMonaco();
    TestBed.configureTestingModule({ providers: [{ provide: MONACO_LOADER, useValue: monaco.loader }] });
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    fixture.destroy();
    await Promise.resolve();

    expect(monaco.editors.length).toBe(0);
  });
});
