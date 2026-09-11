import { downloadCsv, downloadImage, fileName, toCsv } from './export';

const result = (rows: unknown[][], names = ['region', 'revenue']) => ({ columns: names.map((name) => ({ name })), rows });

describe('toCsv', () => {
  it('writes a header and one line per row, separated by CRLF', () => {
    expect(toCsv(result([['Auckland', 2480], ['Dunedin', 100]]))).toBe('region,revenue\r\nAuckland,2480\r\nDunedin,100\r\n');
  });

  it('writes nothing for an empty or missing value', () => {
    expect(toCsv(result([[null, undefined]]))).toBe('region,revenue\r\n,\r\n');
  });

  it('quotes values with a comma, a quote or a line break, and doubles the quotes', () => {
    expect(toCsv(result([['Auckland, North', 'say "hi"'], ['two\nlines', 'a\rb']]))).toBe(
      'region,revenue\r\n"Auckland, North","say ""hi"""\r\n"two\nlines","a\rb"\r\n',
    );
  });

  it('quotes the column names too', () => {
    expect(toCsv(result([], ['a,b', 'c']))).toBe('"a,b",c\r\n');
  });

  it('defuses text that a spreadsheet would run as a formula, but leaves numbers alone', () => {
    const csv = toCsv(result([['=HYPERLINK("http://evil")', -5], ['+1', '-2'], ['@SUM(A1)', 0], ['fine=ok', 1]]));

    expect(csv).toBe(
      'region,revenue\r\n' +
        `"'=HYPERLINK(""http://evil"")",-5\r\n` +
        `'+1,'-2\r\n` +
        `'@SUM(A1),0\r\n` +
        'fine=ok,1\r\n',
    );
  });

  it('keeps booleans and writes objects as JSON', () => {
    expect(toCsv(result([[true, { a: 1 }]]))).toBe('region,revenue\r\ntrue,"{""a"":1}"\r\n');
  });
});

describe('fileName', () => {
  it('keeps a normal title and adds the extension', () => {
    expect(fileName('Revenue by region', 'csv')).toBe('Revenue by region.csv');
  });

  it('removes characters a file name cannot hold and tidies spaces', () => {
    expect(fileName('  Q1 / Q2: "sales"?  ', 'png')).toBe('Q1 Q2 sales.png');
  });

  it('never gives an empty or very long name', () => {
    expect(fileName('???', 'csv')).toBe('export.csv');
    expect(fileName('x'.repeat(300), 'csv').length).toBe(84);
  });
});

describe('downloads', () => {
  let clicked: HTMLAnchorElement[];

  beforeEach(() => {
    clicked = [];
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
      clicked.push(this);
    });
    // jsdom has no object URLs.
    (URL as unknown as Record<string, unknown>)['createObjectURL'] = vi.fn(() => 'blob:fake');
    (URL as unknown as Record<string, unknown>)['revokeObjectURL'] = vi.fn();
  });

  afterEach(() => vi.restoreAllMocks());

  it('saves a CSV under the chart name, with a byte order mark so Excel reads UTF-8', async () => {
    downloadCsv('Orders', result([['Zürich', 1]]));

    expect(clicked.map((a) => [a.download, a.href])).toEqual([['Orders.csv', 'blob:fake']]);
    const blob = (URL.createObjectURL as unknown as ReturnType<typeof vi.fn>).mock.calls[0][0] as Blob;
    const bytes = new Uint8Array(await blob.arrayBuffer());
    expect(Array.from(bytes.slice(0, 3))).toEqual([0xef, 0xbb, 0xbf]); // the UTF-8 byte order mark
    expect(new TextDecoder().decode(bytes.slice(3))).toBe('region,revenue\r\nZürich,1\r\n');
    expect(document.querySelector('a[download]')).toBeNull(); // the temporary link is removed again
  });

  it('saves an image under the chart name', () => {
    downloadImage('Orders', 'data:image/png;base64,AAAA');

    expect(clicked.map((a) => [a.download, a.href])).toEqual([['Orders.png', 'data:image/png;base64,AAAA']]);
  });
});
