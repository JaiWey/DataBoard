import {
  DatasetParameter,
  parameterText,
  parametersMissingDefaults,
  parseParameterValue,
  placeholdersIn,
  sameParameters,
  syncParameters,
} from './dataset-parameters';

describe('placeholdersIn', () => {
  it('finds each name once, in the order it first appears, ignoring case and spaces', () => {
    expect(placeholdersIn('SELECT * FROM t WHERE a = {{region}} AND b > {{ min_price }} OR c = {{Region}}')).toEqual(['region', 'min_price']);
  });

  it('finds nothing in sql without placeholders', () => {
    expect(placeholdersIn("SELECT 1, '{{x}}', \"{{y}}\" FROM t -- {{z}}")).toEqual([]);
    expect(placeholdersIn('SELECT $1, $$ {{a}} $$, /* {{b}} */ ARRAY[1,2]')).toEqual([]);
  });

  it('does not stop at a quote that is closed, or at a doubled quote inside one', () => {
    expect(placeholdersIn("SELECT 'it''s', \"a\"\"b\" FROM t WHERE x = {{after}}")).toEqual(['after']);
  });

  it('copes with unfinished input while someone is still typing', () => {
    expect(placeholdersIn('SELECT {{')).toEqual([]);
    expect(placeholdersIn('SELECT {{reg')).toEqual([]);
    expect(placeholdersIn("SELECT 'unfinished {{x}}")).toEqual([]);
    expect(placeholdersIn('SELECT {{region}} /* never closed {{b}}')).toEqual(['region']);
  });

  it('skips a placeholder that is not written correctly', () => {
    expect(placeholdersIn('SELECT {{1x}}, {{two words}}, {{}}, {{ok}}')).toEqual(['ok']);
  });
});

describe('syncParameters', () => {
  const region: DatasetParameter = { name: 'region', type: 'Text', default: 'Auckland', label: 'Region' };
  const min: DatasetParameter = { name: 'min', type: 'Number', default: 10 };

  it('starts a new placeholder as text with no default', () => {
    expect(syncParameters('SELECT {{region}}', [])).toEqual([{ name: 'region', type: 'Text', default: null }]);
  });

  it('keeps what was already set for a placeholder that is still there, whatever the case', () => {
    expect(syncParameters('SELECT {{REGION}}, {{min}}', [min, region])).toEqual([region, min]); // in the SQL order
  });

  it('drops a parameter whose placeholder was deleted', () => {
    expect(syncParameters('SELECT {{min}}', [region, min])).toEqual([min]);
  });

  it('has none when the sql has no placeholders', () => {
    expect(syncParameters('SELECT 1', [region])).toEqual([]);
  });

  it('can tell whether anything changed', () => {
    expect(sameParameters([region], [{ ...region }])).toBe(true);
    expect(sameParameters([region], [{ ...region, default: 'x' }])).toBe(false);
  });
});

describe('values', () => {
  it('turns what is typed into the value for its type', () => {
    expect(parseParameterValue('Text', 'Auckland')).toBe('Auckland');
    expect(parseParameterValue('Text', ' keeps spaces ')).toBe(' keeps spaces ');
    expect(parseParameterValue('Number', '12.5')).toBe(12.5);
    expect(parseParameterValue('Number', ' -3 ')).toBe(-3);
    expect(parseParameterValue('Date', '2026-03-05')).toBe('2026-03-05');
    expect(parseParameterValue('Boolean', 'true')).toBe(true);
    expect(parseParameterValue('Boolean', 'false')).toBe(false);
  });

  it('turns nothing, or something that does not fit, into no value rather than guessing', () => {
    expect(parseParameterValue('Text', '')).toBeNull();
    expect(parseParameterValue('Number', '')).toBeNull();
    expect(parseParameterValue('Number', 'lots')).toBeNull();
    expect(parseParameterValue('Number', 'Infinity')).toBeNull();
    expect(parseParameterValue('Date', '  ')).toBeNull();
    expect(parseParameterValue('Boolean', 'maybe')).toBeNull();
  });

  it('shows a value, or nothing, in an input', () => {
    expect(parameterText(null)).toBe('');
    expect(parameterText(undefined)).toBe('');
    expect(parameterText(0)).toBe('0');
    expect(parameterText(false)).toBe('false');
    expect(parameterText('x')).toBe('x');
  });

  it('names the parameters that still need a default (zero and false are values)', () => {
    expect(parametersMissingDefaults([
      { name: 'a', type: 'Number', default: 0 },
      { name: 'b', type: 'Boolean', default: false },
      { name: 'c', type: 'Text', default: null },
      { name: 'd', type: 'Date' },
    ])).toEqual(['c', 'd']);
  });
});
