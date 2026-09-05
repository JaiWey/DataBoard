// Monaco's package maps deep imports ("monaco-editor/editor/...") to esm files that ship no type declarations.
declare module 'monaco-editor/*';
declare module '*.css';
