import { Component, DestroyRef, ElementRef, InjectionToken, effect, inject, input, output, viewChild } from '@angular/core';

/** The slice of a Monaco editor this component uses, so tests can stand in for it. */
export interface SqlEditorHandle {
  getValue(): string;
  setValue(value: string): void;
  setReadOnly(readOnly: boolean): void;
  onChange(callback: () => void): void;
  dispose(): void;
}
export interface MonacoApi {
  create(element: HTMLElement, options: { value: string; readOnly: boolean; ariaLabel: string; onSave: () => void; onRun: () => void }): SqlEditorHandle;
}

let loaded: Promise<MonacoApi> | undefined;

/**
 * Loads Monaco on first need so it stays out of the main bundle. Only the editor core and the features a SQL
 * editor needs are imported (not editor.main, which would pull in every language).
 */
async function loadMonaco(): Promise<MonacoApi> {
  loaded ??= (async () => {
    const [monaco] = await Promise.all([
      import('monaco-editor/editor/editor.api'),
      import('monaco-editor/editor/browser/coreCommands'),
      import('monaco-editor/editor/contrib/find/browser/findController'),
      import('monaco-editor/editor/contrib/suggest/browser/suggestController'),
      import('monaco-editor/editor/contrib/hover/browser/hoverContribution'),
      import('monaco-editor/editor/contrib/comment/browser/comment'),
      import('monaco-editor/editor/contrib/bracketMatching/browser/bracketMatching'),
      import('monaco-editor/editor/contrib/clipboard/browser/clipboard'),
      import('monaco-editor/editor/contrib/contextmenu/browser/contextmenu'),
      import('monaco-editor/editor/contrib/linesOperations/browser/linesOperations'),
      import('monaco-editor/editor/contrib/multicursor/browser/multicursor'),
      import('monaco-editor/editor/contrib/folding/browser/folding'),
      import('monaco-editor/editor/contrib/wordOperations/browser/wordOperations'),
      import('monaco-editor/languages/definitions/sql/register'),
    ]);

    // Monaco's styles (and the icon font) are built as a separate monaco.css (see angular.json) and only fetched here,
    // because a stylesheet imported from a lazily loaded script would never be attached to the page.
    const link = document.createElement('link');
    link.rel = 'stylesheet';
    link.href = 'monaco.css';
    document.head.appendChild(link);

    (self as unknown as { MonacoEnvironment: unknown }).MonacoEnvironment = {
      getWorker: () => new Worker(new URL('./editor.worker', import.meta.url), { type: 'module' }),
    };

    const dark = () => typeof matchMedia === 'function' && matchMedia('(prefers-color-scheme: dark)').matches;
    return {
      create(element, options) {
        const editor = monaco.editor.create(element, {
          value: options.value,
          language: 'sql',
          readOnly: options.readOnly,
          ariaLabel: options.ariaLabel,
          theme: dark() ? 'vs-dark' : 'vs',
          automaticLayout: true,
          minimap: { enabled: false },
          scrollBeyondLastLine: false,
          fontSize: 14,
          tabSize: 2,
          wordWrap: 'on',
        });
        editor.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.KeyS, options.onSave);
        editor.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.Enter, options.onRun);
        return {
          getValue: () => editor.getValue(),
          setValue: (value) => editor.setValue(value),
          setReadOnly: (readOnly) => editor.updateOptions({ readOnly }),
          onChange: (callback) => void editor.onDidChangeModelContent(callback),
          dispose: () => editor.dispose(),
        };
      },
    };
  })();
  return loaded;
}

export const MONACO_LOADER = new InjectionToken<() => Promise<MonacoApi>>('MONACO_LOADER', {
  providedIn: 'root',
  factory: () => loadMonaco,
});

/** A SQL editor (Monaco). `value` is the text to show; `valueChange` fires on every edit, `save` on Ctrl/Cmd+S, `run` on Ctrl/Cmd+Enter. */
@Component({
  selector: 'app-sql-editor',
  template: `<div #host class="editor" [attr.data-label]="label()"></div>`,
  styles: `
    :host { display: block; }
    .editor { height: 14rem; border: 1px solid var(--mat-sys-outline-variant); border-radius: 4px; overflow: hidden; }
  `,
})
export class SqlEditorComponent {
  readonly value = input('');
  readonly readOnly = input(false);
  readonly label = input('SQL query');
  readonly valueChange = output<string>();
  readonly save = output<void>();
  readonly run = output<void>();

  private readonly host = viewChild.required<ElementRef<HTMLElement>>('host');
  private readonly loader = inject(MONACO_LOADER);
  private editor: SqlEditorHandle | undefined;
  private destroyed = false;
  private starting = false;

  constructor() {
    inject(DestroyRef).onDestroy(() => {
      this.destroyed = true;
      this.editor?.dispose();
    });

    effect(() => {
      const value = this.value();
      const readOnly = this.readOnly();
      if (this.editor) {
        // Only write back what differs, so typing never fights the text coming back in through `value`.
        if (this.editor.getValue() !== value) this.editor.setValue(value);
        this.editor.setReadOnly(readOnly);
      } else {
        void this.start();
      }
    });
  }

  private async start(): Promise<void> {
    if (this.starting) return;
    this.starting = true;
    const monaco = await this.loader();
    if (this.destroyed) return;

    this.editor = monaco.create(this.host().nativeElement, {
      value: this.value(),
      readOnly: this.readOnly(),
      ariaLabel: this.label(),
      onSave: () => this.save.emit(),
      onRun: () => this.run.emit(),
    });
    this.editor.onChange(() => this.valueChange.emit(this.editor!.getValue()));
  }
}
