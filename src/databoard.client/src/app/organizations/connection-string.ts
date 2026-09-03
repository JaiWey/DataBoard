export interface PostgresConnectionDetails {
  host: string;
  port: number;
  database: string;
  username: string;
  password: string;
  sslMode: 'Prefer' | 'Require' | 'Disable';
}

/** Quotes a value only when needed, the way ADO.NET-style connection strings expect (double quotes doubled). */
function quote(value: string): string {
  return /[;"'\s=]/.test(value) || value === '' ? `"${value.replace(/"/g, '""')}"` : value;
}

/**
 * Builds the connection string sent to the API. Only options the server accepts are produced; the server
 * validates them again and rejects anything else.
 */
export function buildPostgresConnectionString(d: PostgresConnectionDetails): string {
  return [
    `Host=${quote(d.host.trim())}`,
    `Port=${d.port}`,
    `Database=${quote(d.database.trim())}`,
    `Username=${quote(d.username.trim())}`,
    `Password=${quote(d.password)}`,
    `SSL Mode=${d.sslMode}`,
  ].join(';');
}
