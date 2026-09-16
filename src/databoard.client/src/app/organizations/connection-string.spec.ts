import { buildMySqlConnectionString, buildPostgresConnectionString } from './connection-string';

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

describe('buildMySqlConnectionString', () => {
  const mysql = { ...base, port: 3306, sslMode: 'Prefer' as const };

  it('builds the plain form with the names MySQL uses', () => {
    expect(buildMySqlConnectionString(mysql)).toBe(
      'Server=db.example.com;Port=3306;Database=sales;User ID=reader;Password=secret;SSL Mode=Preferred',
    );
  });

  it.each([
    ['Prefer', 'Preferred'],
    ['Require', 'Required'],
    ['Disable', 'None'],
  ] as const)('writes the SSL choice %s as %s', (choice, written) => {
    expect(buildMySqlConnectionString({ ...mysql, sslMode: choice })).toContain(`SSL Mode=${written}`);
  });

  it('quotes values that contain separators so they cannot add options', () => {
    const cs = buildMySqlConnectionString({ ...mysql, password: 'a;AllowLoadLocalInfile=true', username: 'u;SslCert=/x' });

    expect(cs).toContain('Password="a;AllowLoadLocalInfile=true";');
    expect(cs).toContain('User ID="u;SslCert=/x";');
  });
});
