import { Block, Inline, parseInline, parseMarkdown, webAddress } from './markdown';

const text = (v: string): Inline => ({ t: 'text', v });

describe('webAddress', () => {
  it('accepts http and https addresses, written the way the browser reads them', () => {
    expect(webAddress('https://example.com')).toBe('https://example.com/');
    expect(webAddress(' http://example.com/a?b=1 ')).toBe('http://example.com/a?b=1');
  });

  it.each(['javascript:alert(1)', 'data:text/html,x', 'ftp://example.com', 'mailto:a@b.c', 'example.com', '', '   ', '//example.com'])(
    'refuses %j',
    (value) => expect(webAddress(value)).toBeNull(),
  );
});

describe('parseInline', () => {
  it('leaves plain words alone', () => {
    expect(parseInline('hello world')).toEqual([text('hello world')]);
    expect(parseInline('')).toEqual([]);
  });

  it('reads bold, italic and code', () => {
    expect(parseInline('a **bold** and *slanted* and _also_ `x < 1`')).toEqual([
      text('a '), { t: 'strong', c: [text('bold')] }, text(' and '), { t: 'em', c: [text('slanted')] },
      text(' and '), { t: 'em', c: [text('also')] }, text(' '), { t: 'code', v: 'x < 1' },
    ]);
  });

  it('nests italic in bold and bold in a link', () => {
    expect(parseInline('**very *big* deal**')).toEqual([{ t: 'strong', c: [text('very '), { t: 'em', c: [text('big')] }, text(' deal')] }]);
    expect(parseInline('[**go**](https://example.com)')).toEqual([{ t: 'link', href: 'https://example.com/', c: [{ t: 'strong', c: [text('go')] }] }]);
  });

  it('reads a link to a web address, and leaves any other link as the words it was written with', () => {
    expect(parseInline('see [the docs](https://example.com/docs) now')).toEqual([
      text('see '), { t: 'link', href: 'https://example.com/docs', c: [text('the docs')] }, text(' now'),
    ]);
    expect(parseInline('[click](javascript:alert(1))')).toEqual([text('[click](javascript:alert(1))')]);
    expect(parseInline('[a](data:text/html,x)')).toEqual([text('[a](data:text/html,x)')]);
  });

  it('keeps raw HTML as plain text, so it can never become markup', () => {
    expect(parseInline('<script>alert(1)</script> <b>x</b>')).toEqual([text('<script>alert(1)</script> <b>x</b>')]);
    expect(parseInline('<img src=x onerror=alert(1)>')).toEqual([text('<img src=x onerror=alert(1)>')]);
  });

  it('does not italicise underscores inside words, a lone star, or a star before a space', () => {
    expect(parseInline('snake_case_name')).toEqual([text('snake_case_name')]);
    expect(parseInline('2 * 3 * 4')).toEqual([text('2 * 3 * 4')]);
    expect(parseInline('a * b')).toEqual([text('a * b')]);
  });

  it('leaves unclosed marks as the words they were', () => {
    expect(parseInline('**oops')).toEqual([text('**oops')]);
    expect(parseInline('`code')).toEqual([text('`code')]);
    expect(parseInline('[half](')).toEqual([text('[half](')]);
  });

  it('lets a backslash keep a mark as a plain character', () => {
    expect(parseInline('\\*not italic\\*')).toEqual([text('*not italic*')]);
    expect(parseInline('a \\\\ b')).toEqual([text('a \\ b')]);
    expect(parseInline('\\q')).toEqual([text('\\q')]); // only marks can be escaped
  });
});

describe('parseMarkdown', () => {
  it('reads headings, mapping deeper ones to the third level, and leaves # without a space alone', () => {
    expect(parseMarkdown('# One\n## Two\n### Three\n#### Four\n#nope')).toEqual<Block[]>([
      { t: 'heading', level: 1, c: [text('One')] },
      { t: 'heading', level: 2, c: [text('Two')] },
      { t: 'heading', level: 3, c: [text('Three')] },
      { t: 'heading', level: 3, c: [text('Four')] },
      { t: 'p', c: [text('#nope')] },
    ]);
  });

  it('drops closing hashes from a heading', () => {
    expect(parseMarkdown('## Title ##')).toEqual([{ t: 'heading', level: 2, c: [text('Title')] }]);
  });

  it('makes a paragraph of lines together, with a line break between them, and a new one after a blank line', () => {
    expect(parseMarkdown('first\nsecond\n\nthird')).toEqual<Block[]>([
      { t: 'p', c: [text('first'), { t: 'br' }, text('second')] },
      { t: 'p', c: [text('third')] },
    ]);
  });

  it('reads bullet and numbered lists, and a change of kind starts a new list', () => {
    expect(parseMarkdown('- a\n* b\n+ c\n1. one\n2) two')).toEqual<Block[]>([
      { t: 'ul', items: [[text('a')], [text('b')], [text('c')]] },
      { t: 'ol', items: [[text('one')], [text('two')]] },
    ]);
  });

  it('ends a list at a blank line or a paragraph, and reads marks inside items', () => {
    expect(parseMarkdown('- **a**\n\ntext\n- b')).toEqual<Block[]>([
      { t: 'ul', items: [[{ t: 'strong', c: [text('a')] }]] },
      { t: 'p', c: [text('text')] },
      { t: 'ul', items: [[text('b')]] },
    ]);
  });

  it('reads a horizontal rule, but a list item that starts with a dash is not one', () => {
    expect(parseMarkdown('above\n---\nbelow\n***\n- item')).toEqual<Block[]>([
      { t: 'p', c: [text('above')] }, { t: 'hr' }, { t: 'p', c: [text('below')] }, { t: 'hr' }, { t: 'ul', items: [[text('item')]] },
    ]);
  });

  it('reads Windows line endings and ignores blank input', () => {
    expect(parseMarkdown('# A\r\n\r\ntext')).toEqual<Block[]>([{ t: 'heading', level: 1, c: [text('A')] }, { t: 'p', c: [text('text')] }]);
    expect(parseMarkdown('')).toEqual([]);
    expect(parseMarkdown('   \n \n')).toEqual([]);
  });

  it('copes with a very long line of marks without hanging', () => {
    const started = Date.now();

    parseMarkdown('*'.repeat(4000));
    parseMarkdown('['.repeat(2000));
    parseMarkdown('**a'.repeat(1300));

    expect(Date.now() - started).toBeLessThan(2000);
  });
});
