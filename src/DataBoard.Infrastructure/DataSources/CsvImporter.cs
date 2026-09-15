using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CsvHelper;
using CsvHelper.Configuration;
using DataBoard.Application.DataSources;
using Microsoft.Data.Sqlite;
using ValidationException = DataBoard.Application.Common.ValidationException;

namespace DataBoard.Infrastructure.DataSources;

/// <summary>
/// Reads a CSV file into a database file of its own. The file is read completely into memory first (it is at most
/// <see cref="CsvLimits.MaxBytes"/>), so its columns can be given the type that fits everything in them: a column is only
/// a number if every value in it is, which keeps one stray word from breaking a sum later. Nothing of the uploaded file
/// (its name, its path) is used for anything except to suggest the name of the table, and that is cleaned first.
/// </summary>
public sealed partial class CsvImporter : ICsvImporter
{
    private static readonly char[] Delimiters = [',', ';', '\t', '|'];

    // What the importer calls a column's type; the connector maps each to the name the rest of the application uses.
    private const string Integer = "INTEGER";
    private const string Real = "REAL";
    private const string Text = "TEXT";
    private const string Boolean = "BOOLEAN";
    private const string Date = "DATE";
    private const string DateTime = "DATETIME";

    [GeneratedRegex(@"^[+-]?(0|[1-9][0-9]{0,17})$")]
    private static partial Regex IntegerPattern();

    [GeneratedRegex(@"^[+-]?([0-9]+\.?[0-9]*|\.[0-9]+)([eE][+-]?[0-9]+)?$")]
    private static partial Regex RealPattern();

    [GeneratedRegex(@"^[0-9]{4}-[0-9]{2}-[0-9]{2}$")]
    private static partial Regex DatePattern();

    [GeneratedRegex(@"^[0-9]{4}-[0-9]{2}-[0-9]{2}[T ][0-9]{2}:[0-9]{2}(:[0-9]{2}(\.[0-9]{1,7})?)?(Z|[+-][0-9]{2}:?[0-9]{2})?$")]
    private static partial Regex DateTimePattern();

    [GeneratedRegex(@"^[+-]?\d+$")]
    private static partial Regex WholeNumberPattern();

    [GeneratedRegex(@"^[+-]?0\d")]
    private static partial Regex LeadingZeroPattern();

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]{0,63}$")]
    private static partial Regex TableNamePattern();

    public async Task<CsvImportResult> ImportAsync(
        Stream csv, string fileName, CsvImportOptions options, string destinationPath, CancellationToken ct = default)
    {
        var bytes = await ReadLimitedAsync(csv, ct);
        if (bytes.Length == 0)
            throw new ValidationException("File", "The file is empty.");
        if (LooksBinary(bytes))
            throw new ValidationException("File", "This does not look like a CSV file: it is not text. Export it as CSV (comma-separated values) and try again.");

        var text = Decode(bytes);
        var delimiter = ChooseDelimiter(text, options.Delimiter);
        var tableName = ChooseTableName(options.TableName, fileName);

        var (header, rows) = ReadRows(text, delimiter, options.HasHeader);
        if (rows.Count == 0)
            throw new ValidationException("File", options.HasHeader ? "The file has column names but no rows of data." : "The file has no rows of data.");

        var width = header?.Length ?? rows.Max(r => Array.FindLastIndex(r, v => v is not null) + 1); // a trailing delimiter adds no column
        if (width > CsvLimits.MaxColumns)
            throw new ValidationException("File", $"The file has {width} columns; at most {CsvLimits.MaxColumns} are allowed.");

        var names = ColumnNames(header, width);
        var types = Enumerable.Range(0, width).Select(c => InferType(rows, c)).ToArray();

        try
        {
            await WriteAsync(destinationPath, tableName, names, types, rows, ct);
        }
        catch
        {
            TryDelete(destinationPath);
            throw;
        }

        return new CsvImportResult(
            tableName, rows.Count,
            names.Select((n, i) => new CsvColumn(n, SqliteConnector.CanonicalType(types[i], null))).ToList(),
            delimiter == '\t' ? "\t" : delimiter.ToString());
    }

    // ---- reading ---------------------------------------------------------------------------------------------

    private static async Task<byte[]> ReadLimitedAsync(Stream stream, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81_920];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > CsvLimits.MaxBytes)
                throw new ValidationException("File", $"The file is larger than {CsvLimits.MaxBytes / (1024 * 1024)} MB. Split it, or remove columns and rows that are not needed.");
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    /// <summary>Text has no zero bytes (apart from UTF-16, which says so with a byte order mark); a file with one is something else.</summary>
    private static bool LooksBinary(byte[] bytes)
    {
        if (bytes.Length >= 2 && ((bytes[0] == 0xFF && bytes[1] == 0xFE) || (bytes[0] == 0xFE && bytes[1] == 0xFF))) return false;
        return Array.IndexOf(bytes, (byte)0, 0, Math.Min(bytes.Length, 8_192)) >= 0;
    }

    /// <summary>UTF-8 unless a byte order mark says otherwise (Excel writes one); bytes that are not valid text become "�" rather than failing the file.</summary>
    private static string Decode(byte[] bytes)
    {
        using var reader = new StreamReader(new MemoryStream(bytes), new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static char ChooseDelimiter(string text, string? requested)
    {
        if (!string.IsNullOrEmpty(requested))
        {
            var wanted = requested.Equals("tab", StringComparison.OrdinalIgnoreCase) || requested == "\\t" ? "\t" : requested;
            if (wanted.Length == 1 && Array.IndexOf(Delimiters, wanted[0]) >= 0) return wanted[0];
            throw new ValidationException("Delimiter", "The delimiter is a comma, a semicolon, a tab or a vertical bar.");
        }

        // Look at the first lines: the delimiter is the character that appears the same number of times in most of them.
        var lines = FirstLines(text, 20);
        char best = ',';
        var bestScore = 0;
        foreach (var candidate in Delimiters)
        {
            var counts = lines.Select(l => CountOutsideQuotes(l, candidate)).Where(c => c > 0).ToList();
            if (counts.Count == 0) continue;
            var score = counts.GroupBy(c => c).Max(g => g.Count()) * 1000 + counts.Max(); // consistent first, then more columns
            if (score > bestScore)
            {
                best = candidate;
                bestScore = score;
            }
        }
        return best;
    }

    private static List<string> FirstLines(string text, int count)
    {
        var lines = new List<string>();
        using var reader = new StringReader(text);
        while (lines.Count < count && reader.ReadLine() is { } line)
        {
            if (line.Length > 0) lines.Add(line);
        }
        return lines;
    }

    private static int CountOutsideQuotes(string line, char delimiter)
    {
        var count = 0;
        var inQuotes = false;
        foreach (var c in line)
        {
            if (c == '"') inQuotes = !inQuotes;
            else if (c == delimiter && !inQuotes) count++;
        }
        return count;
    }

    private static (string?[]? Header, List<string?[]> Rows) ReadRows(string text, char delimiter, bool hasHeader)
    {
        var configuration = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = delimiter.ToString(),
            HasHeaderRecord = false,
            MissingFieldFound = null,
            Mode = CsvMode.RFC4180,
        };

        string?[]? header = null;
        var rows = new List<string?[]>();
        using var parser = new CsvParser(new StringReader(text), configuration);

        try
        {
            while (parser.Read())
            {
                var record = parser.Record!;
                var values = new string?[record.Length];
                for (var i = 0; i < record.Length; i++) values[i] = string.IsNullOrWhiteSpace(record[i]) ? null : record[i];

                if (values.All(v => v is null)) continue; // a blank line

                if (hasHeader && header is null)
                {
                    header = values;
                    continue;
                }

                if (rows.Count >= CsvLimits.MaxRows)
                    throw new ValidationException("File", $"The file has more than {CsvLimits.MaxRows:N0} rows. Split it, or remove rows that are not needed.");
                if (header is not null && values.Length > header.Length && values[header.Length..].Any(v => v is not null))
                    throw new ValidationException("File",
                        $"Row {parser.Row} has {values.Length} values but there are {header.Length} columns. Check for a delimiter inside a value that is not in quotes.");

                rows.Add(values);
            }
        }
        catch (CsvHelperException ex)
        {
            throw new ValidationException("File", $"The file could not be read near row {parser.Row}: {ex.Message.Split('\n')[0].Trim()}");
        }

        return (header, rows);
    }

    // ---- names ----------------------------------------------------------------------------------------------

    private static string ChooseTableName(string? requested, string fileName)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            if (!TableNamePattern().IsMatch(requested) || requested.StartsWith("sqlite_", StringComparison.OrdinalIgnoreCase))
                throw new ValidationException("TableName", "A table name starts with a letter or underscore and has only letters, digits and underscores (at most 64).");
            return requested;
        }

        // The file name is the person's, not to be trusted: only the part after any folder, only plain characters.
        var stem = Path.GetFileNameWithoutExtension(fileName.Replace('\\', '/').Split('/').Last());
        var name = Regex.Replace(stem.ToLowerInvariant(), "[^a-z0-9_]+", "_").Trim('_');
        if (name.Length == 0) name = "data";
        if (char.IsAsciiDigit(name[0]) || name.StartsWith("sqlite_", StringComparison.Ordinal)) name = "t_" + name;
        return name.Length > 64 ? name[..64] : name;
    }

    /// <summary>The header's names, tidied: no control characters, none empty, none the same as another.</summary>
    private static string[] ColumnNames(string?[]? header, int width)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var names = new string[width];
        for (var i = 0; i < width; i++)
        {
            var raw = header is not null && i < header.Length ? header[i] : null;
            var name = raw is null ? string.Empty : new string(raw.Where(c => !char.IsControl(c)).ToArray()).Trim();
            if (name.Length > CsvLimits.MaxNameLength) name = name[..CsvLimits.MaxNameLength];
            if (name.Length == 0) name = $"column_{i + 1}";

            var unique = name;
            for (var n = 2; !used.Add(unique); n++) unique = $"{name}_{n}";
            names[i] = unique;
        }
        return names;
    }

    // ---- types ----------------------------------------------------------------------------------------------

    /// <summary>The narrowest type that every value in the column fits; empty values do not count.</summary>
    private static string InferType(List<string?[]> rows, int column)
    {
        bool integer = true, real = true, boolean = true, date = true, dateTime = true;
        var any = false;

        foreach (var row in rows)
        {
            if (column >= row.Length || row[column] is not { } raw) continue;
            var value = raw.Trim();
            any = true;

            integer &= IntegerPattern().IsMatch(value);
            // Codes with leading zeros and whole numbers too big for 64 bits stay text, so nothing is changed or rounded.
            var keepsAsText = LeadingZeroPattern().IsMatch(value)
                || (WholeNumberPattern().IsMatch(value) && !IntegerPattern().IsMatch(value));
            real &= !keepsAsText && RealPattern().IsMatch(value) && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && double.IsFinite(d);
            if (keepsAsText) integer = false;
            boolean &= value.Equals("true", StringComparison.OrdinalIgnoreCase) || value.Equals("false", StringComparison.OrdinalIgnoreCase);
            var isDate = DatePattern().IsMatch(value) && DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
            date &= isDate;
            dateTime &= isDate || (DateTimePattern().IsMatch(value) && TryParseDateTime(value, out _)); // a date is a time at midnight

            if (!integer && !real && !boolean && !date && !dateTime) break; // text, whatever follows
        }

        if (!any) return Text;
        if (boolean) return Boolean;
        if (integer) return Integer;
        if (real) return Real;
        if (date) return Date;
        if (dateTime) return DateTime;
        return Text;
    }

    private static bool TryParseDateTime(string value, out System.DateTime utc)
    {
        // A time with an offset is converted to UTC; one without is taken as it is (the database has no time zones either).
        if (System.DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out utc))
            return true;
        utc = default;
        return false;
    }

    // ---- writing --------------------------------------------------------------------------------------------

    private static async Task WriteAsync(
        string path, string table, string[] names, string[] types, List<string?[]> rows, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path)) File.Delete(path);

        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false, Cache = SqliteCacheMode.Private,
        }.ConnectionString);
        await connection.OpenAsync(ct);

        // A file that is made once and then only read needs no journal, and is much faster to fill without one.
        await using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA journal_mode = OFF; PRAGMA synchronous = OFF;";
            await pragma.ExecuteNonQueryAsync(ct);
        }

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct);

        await using (var create = connection.CreateCommand())
        {
            create.Transaction = transaction;
            create.CommandText = $"CREATE TABLE {Quote(table)} ({string.Join(", ", names.Select((n, i) => $"{Quote(n)} {types[i]}"))})";
            await create.ExecuteNonQueryAsync(ct);
        }

        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = $"INSERT INTO {Quote(table)} VALUES ({string.Join(", ", names.Select((_, i) => "$v" + i))})";
        var parameters = names.Select((_, i) => insert.Parameters.Add("$v" + i, SqliteType.Text)).ToArray();
        insert.Prepare();

        for (var r = 0; r < rows.Count; r++)
        {
            if (r % 5_000 == 0) ct.ThrowIfCancellationRequested();
            for (var c = 0; c < names.Length; c++)
                parameters[c].Value = ToValue(c < rows[r].Length ? rows[r][c] : null, types[c]);
            await insert.ExecuteNonQueryAsync(ct);
        }

        await transaction.CommitAsync(ct);
    }

    private static object ToValue(string? raw, string type)
    {
        if (raw is null) return DBNull.Value;
        var value = raw.Trim();

        return type switch
        {
            Integer => long.Parse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture),
            Real => double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture),
            Boolean => value.Equals("true", StringComparison.OrdinalIgnoreCase) ? 1L : 0L,
            Date => value,
            DateTime => TryParseDateTime(value, out var utc) ? Timestamp(utc) : value,
            _ => raw,
        };
    }

    /// <summary>"2026-03-05 14:30:00", with the fraction of a second only when there is one: the form SQLite's date functions read.</summary>
    private static string Timestamp(System.DateTime value)
    {
        var text = value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        var fraction = value.Ticks % TimeSpan.TicksPerSecond;
        return fraction == 0 ? text : text + "." + fraction.ToString("D7", CultureInfo.InvariantCulture).TrimEnd('0');
    }

    private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Nothing more to do for a half-written file that cannot be removed right now.
        }
    }
}
