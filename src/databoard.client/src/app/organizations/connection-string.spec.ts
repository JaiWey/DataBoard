import { buildPostgresConnectionString } from './connection-string';

const base = {
  host: 'db.example.com',
  port: 5432,
  database: 'sales',
  username: 'reader',
  password: 'secret',
  sslMode: 'Require' as const,
};

describe('buildPostgresConnectionString', () => {
  it('builds the plain form', () => {
    expect(buildPostgresConnectionString(base)).toBe(
      'Host=db.example.com;Port=5432;Database=sales;Username=reader;Password=secret;SSL Mode=Require',
    );
  });

  it('trims host, database and username but not the password', () => {
    const cs = buildPostgresConnectionString({ ...base, host: ' h ', database: ' d ', username: ' u ', password: ' p ' });

    expect(cs).toContain('Host=h;');
    expect(cs).toContain('Database=d;');
    expect(cs).toContain('Username=u;');
    expect(cs).toContain('Password=" p ";');
  });

  it('quotes values that contain separators so they cannot add options', () => {
    const cs = buildPostgresConnectionString({ ...base, password: 'a;Passfile=/etc/passwd' });

    expect(cs).toContain('Password="a;Passfile=/etc/passwd";');
  });

  it('doubles embedded double quotes', () => {
    const cs = buildPostgresConnectionString({ ...base, password: 'pa"ss' });

    expect(cs).toContain('Password="pa""ss";');
  });

  it('quotes an empty password', () => {
    expect(buildPostgresConnectionString({ ...base, password: '' })).toContain('Password="";');
  });
});
