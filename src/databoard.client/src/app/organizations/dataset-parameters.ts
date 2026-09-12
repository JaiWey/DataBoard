/**
 * Dataset parameters in the editor. A parameter is a value a dataset's SQL takes, written `{{name}}` in the SQL; the server
 * binds it, so it can only ever be data. These helpers find the placeholders as the person types, keep the list of
 * parameters in step with them, and turn what is typed into the JSON values the API expects.
 */
export type ParameterType = 'Text' | 'Number' | 'Date' | 'Boolean';

export const PARAMETER_TYPES: ParameterType[] = ['Text', 'Number', 'Date', 'Boolean'];

export interface DatasetParameter {
  name: string;
  type: ParameterType;
  /** A string, number or boolean to suit the type; null when nobody has chosen one yet. */
  default?: string | number | boolean | null;
  label?: string | null;
}

const PLACEHOLDER = /^\{\{\s*([A-Za-z_][A-Za-z0-9_]{0,62})\s*\}\}/;

/**
 * The names written as `{{name}}` in some SQL, once each, in the order they first appear. Like the server, it skips
 * strings, quoted names, comments and dollar-quoted text, where a placeholder would not stand for a value (the server
 * explains that when the SQL is saved).
 */
export function placeholdersIn(sql: string): string[] {
  const names: string[] = [];
  let i = 0;
  while (i < sql.length) {
    const c = sql[i];
    if (c === "'" || c === '"') {
      i = skipQuoted(sql, i, c);
    } else if (c === '-' && sql[i + 1] === '-') {
      const end = sql.indexOf('\n', i);
      i = end < 0 ? sql.length : end;
    } else if (c === '/' && sql[i + 1] === '*') {
      i = skipBlockComment(sql, i);
    } else if (c === '$' && dollarTagAt(sql, i)) {
      const tag = dollarTagAt(sql, i) as string;
      const close = sql.indexOf(tag, i + tag.length);
      i = close < 0 ? sql.length : close + tag.length;
    } else if (c === '{' && sql[i + 1] === '{') {
      const match = PLACEHOLDER.exec(sql.slice(i));
      if (match) {
        if (!names.some((n) => n.toLowerCase() === match[1].toLowerCase())) names.push(match[1]);
        i += match[0].length;
      } else {
        i += 2;
      }
    } else {
      i++;
    }
  }
  return names;
}

function skipQuoted(sql: string, start: number, quote: string): number {
  let i = start + 1;
  while (i < sql.length) {
    if (sql[i] === quote) {
      if (sql[i + 1] === quote) {
        i += 2;
        continue;
      }
      return i + 1;
    }
    i++;
  }
  return sql.length;
}

function skipBlockComment(sql: string, start: number): number {
  let depth = 0;
  let i = start;
  while (i < sql.length - 1) {
    if (sql[i] === '/' && sql[i + 1] === '*') {
      depth++;
      i += 2;
    } else if (sql[i] === '*' && sql[i + 1] === '/') {
      depth--;
      i += 2;
      if (depth === 0) return i;
    } else {
      i++;
    }
  }
  return sql.length;
}

/** "$tag$" or "$$" at this position, if it opens a dollar-quoted string ($1 is a positional parameter, not a quote). */
function dollarTagAt(sql: string, start: number): string | null {
  let i = start + 1;
  while (i < sql.length && /[A-Za-z0-9_]/.test(sql[i])) i++;
  if (i >= sql.length || sql[i] !== '$') return null;
  if (i > start + 1 && /[0-9]/.test(sql[start + 1])) return null;
  return sql.slice(start, i + 1);
}

/**
 * The parameters for some SQL: one for each placeholder, in the order they appear. A placeholder that already had a
 * declaration keeps it (type, default, label); a new one starts as text with no default. A declaration whose
 * placeholder was deleted from the SQL goes away with it.
 */
export function syncParameters(sql: string, current: DatasetParameter[]): DatasetParameter[] {
  return placeholdersIn(sql).map(
    (name) => current.find((p) => p.name.toLowerCase() === name.toLowerCase()) ?? { name, type: 'Text', default: null },
  );
}

export const sameParameters = (a: DatasetParameter[], b: DatasetParameter[]): boolean => JSON.stringify(a) === JSON.stringify(b);

/** A default or a value as the person sees it in an input. */
export const parameterText = (value: string | number | boolean | null | undefined): string => (value === null || value === undefined ? '' : String(value));

/**
 * What was typed, as the JSON value for a parameter of this type. Nothing typed is null (no value). Text that is not
 * a valid number stays null too, so the person sees it is still missing rather than something being guessed.
 */
export function parseParameterValue(type: ParameterType, text: string): string | number | boolean | null {
  const trimmed = type === 'Text' ? text : text.trim();
  if (trimmed === '') return null;
  switch (type) {
    case 'Number': {
      const n = Number(trimmed);
      return Number.isFinite(n) ? n : null;
    }
    case 'Boolean':
      return trimmed === 'true' ? true : trimmed === 'false' ? false : null;
    default:
      return trimmed;
  }
}

/** The names of the parameters that cannot be saved yet because they have no default (the SQL is checked with the defaults). */
export const parametersMissingDefaults = (parameters: DatasetParameter[]): string[] =>
  parameters.filter((p) => p.default === null || p.default === undefined).map((p) => p.name);
