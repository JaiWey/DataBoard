import { MonacoApi, SqlEditorHandle } from './sql-editor.component';

export interface FakeEditor extends SqlEditorHandle {
  value: string;
  readOnly: boolean;
  disposed: boolean;
  /** Pretends the user typed, as Monaco would on a keystroke. */
  type(text: string): void;
  /** Pretends the user pressed Ctrl+S. */
  pressSave(): void;
  pressRun(): void;
}

/** Stands in for Monaco in specs: records the editors that were created and lets a test type into them. */
export function fakeMonaco() {
  const editors: FakeEditor[] = [];
  const api: MonacoApi = {
    create(_element, options) {
      let listener: () => void = () => {};
      const editor: FakeEditor = {
        value: options.value,
        readOnly: options.readOnly,
        disposed: false,
        getValue: () => editor.value,
        setValue: (value) => (editor.value = value),
        setReadOnly: (readOnly) => (editor.readOnly = readOnly),
        onChange: (callback) => (listener = callback),
        dispose: () => (editor.disposed = true),
        type: (text) => {
          editor.value = text;
          listener();
        },
        pressSave: () => options.onSave(),
        pressRun: () => options.onRun(),
      };
      editors.push(editor);
      return editor;
    },
  };
  return { api, loader: async () => api, editors };
}
