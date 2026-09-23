/**
 * A small, safe Markdown: headings (#, ##, ###), paragraphs (a single new line is a line break), bullet and numbered
 * lists, bold (**), italic (* or _), code (`), links to http and https addresses, and a horizontal rule (---).
 *
 * It does not make HTML. It makes a tree of plain pieces, which the page draws with its own templates, so nothing in the
 * text can ever become markup or script. Anything it does not understand stays as text.
 */
export type Inline =
  | { t: 'text'; v: string }
  | { t: 'br' }
  | { t: 'code'; v: string }
  | { t: 'strong' | 'em'; c: Inline[] }
  | { t: 'link'; href: string; c: Inline[] };

export type Block =
  | { t: 'heading'; level: 1 | 2 | 3; c: Inline[] }
  | { t: 'p'; c: Inline[] }
  | { t: 'ul' | 'ol'; items: Inline[][] }
  | { t: 'hr' };

/** The address of a web link, written the way the browser will read it, or null if it is not an http(s) address. */
export function webAddress(value: string): string | null {
  try {
    const url = new URL(value.trim());
    return url.protocol === 'http:' || url.protocol === 'https:' ? url.href : null;
  } catch {
    return null;
  }
}

const ESCAPABLE = '\\`*_[]()#-+.!';
const isWord = (c: string | undefined): boolean => c !== undefined && /[\p{L}\p{N}]/u.test(c);

export function parseInline(s: string): Inline[] {
  const out: Inline[] = [];
  let text = '';
  const flush = (): void => {
    if (text) out.push({ t: 'text', v: text });
    text = '';
  };

  let i = 0;
  while (i < s.length) {
    const ch = s[i];

    if (ch === '\\' && i + 1 < s.length && ESCAPABLE.includes(s[i + 1])) {
      text += s[i + 1];
      i += 2;
      continue;
    }

    if (ch === '`') {
      const end = s.indexOf('`', i + 1);
      if (end > i + 1) {
        flush();
        out.push({ t: 'code', v: s.slice(i + 1, end) });
        i = end + 1;
        continue;
      }
    }

    if (s.startsWith('**', i)) {
      const end = s.indexOf('**', i + 2);
      if (end > i + 2) {
        flush();
        out.push({ t: 'strong', c: parseInline(s.slice(i + 2, end)) });
        i = end + 2;
        continue;
      }
    }

    if ((ch === '*' || ch === '_') && !/\s/.test(s[i + 1] ?? ' ')) {
      // An underscore inside a word (snake_case) is just an underscore.
      const opens = ch === '*' || !isWord(s[i - 1]);
      const end = s.indexOf(ch, i + 1);
      if (opens && end > i + 1 && (ch === '*' || !isWord(s[end + 1]))) {
        flush();
        out.push({ t: 'em', c: parseInline(s.slice(i + 1, end)) });
        i = end + 1;
        continue;
      }
    }

    if (ch === '[') {
      const close = s.indexOf('](', i + 1);
      const end = close > i ? s.indexOf(')', close + 2) : -1;
      const href = end > close ? webAddress(s.slice(close + 2, end)) : null;
      if (href) {
        flush();
        out.push({ t: 'link', href, c: parseInline(s.slice(i + 1, close)) });
        i = end + 1;
        continue;
      }
    }

    text += ch;
    i++;
  }
  flush();
  return out;
}

/** Lines of one paragraph: a new line inside it is a line break. */
const paragraph = (lines: string[]): Inline[] =>
  lines.flatMap((line, n) => (n === 0 ? parseInline(line.trim()) : [{ t: 'br' } as Inline, ...parseInline(line.trim())]));

export function parseMarkdown(source: string): Block[] {
  const blocks: Block[] = [];
  let para: string[] = [];
  let list: { t: 'ul' | 'ol'; items: Inline[][] } | null = null;

  const endPara = (): void => {
    if (para.length > 0) blocks.push({ t: 'p', c: paragraph(para) });
    para = [];
  };
  const endList = (): void => {
    if (list) blocks.push(list);
    list = null;
  };

  for (const raw of source.replace(/\r\n?/g, '\n').split('\n')) {
    const line = raw.trimEnd();

    if (line.trim() === '') {
      endPara();
      endList();
      continue;
    }

    const heading = /^\s{0,3}(#{1,6})\s+(.*)$/.exec(line);
    if (heading) {
      endPara();
      endList();
      blocks.push({ t: 'heading', level: Math.min(heading[1].length, 3) as 1 | 2 | 3, c: parseInline(heading[2].replace(/\s+#+\s*$/, '').trim()) });
      continue;
    }

    if (/^\s{0,3}([-*_])(\s*\1){2,}\s*$/.test(line)) {
      endPara();
      endList();
      blocks.push({ t: 'hr' });
      continue;
    }

    const bullet = /^\s*[-*+]\s+(.*)$/.exec(line);
    const numbered = bullet ? null : /^\s*\d+[.)]\s+(.*)$/.exec(line);
    if (bullet || numbered) {
      endPara();
      const kind = bullet ? 'ul' : 'ol';
      if (list && list.t !== kind) endList();
      list ??= { t: kind, items: [] };
      list.items.push(parseInline((bullet ?? numbered)![1].trim()));
      continue;
    }

    endList();
    para.push(line);
  }
  endPara();
  endList();
  return blocks;
}
