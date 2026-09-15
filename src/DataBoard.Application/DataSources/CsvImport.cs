namespace DataBoard.Application.DataSources;

/// <summary>What an uploaded file may be: a data source is a small file, not a data lake.</summary>
public static class CsvLimits
{
    public const long MaxBytes = 20L * 1024 * 1024;
    public const int MaxRows = 200_000;
    public const int MaxColumns = 200;
    public const int MaxNameLength = 128;
}

/// <param name="TableName">What the data is called in SQL. Left out, it is made from the file's name.</param>
/// <param name="HasHeader">Whether the first row holds the column names (the usual case).</param>
/// <param name="Delimiter">The character between values. Left out, it is found by looking at the file.</param>
public sealed record CsvImportOptions(string? TableName = null, bool HasHeader = true, string? Delimiter = null);

/// <param name="DataType">The type named the way the rest of the application names types, so charts treat it correctly.</param>
public sealed record CsvColumn(string Name, string DataType);

public sealed record CsvImportResult(string TableName, int RowCount, IReadOnlyList<CsvColumn> Columns, string Delimiter);

/// <summary>
/// Reads a CSV file into a small database file of its own, working out each column's type from what is in it. The
/// result is then an ordinary data source: SQL, table views and charts work on it like on any other.
/// </summary>
public interface ICsvImporter
{
    /// <summary>
    /// Throws a <see cref="Common.ValidationException"/> that says what is wrong with the file (empty, not text, too big,
    /// too many columns, a row with more values than there are columns) in terms a person can act on.
    /// </summary>
    Task<CsvImportResult> ImportAsync(
        Stream csv, string fileName, CsvImportOptions options, string destinationPath, CancellationToken ct = default);
}

/// <summary>Where the database files of uploaded data live. Files are named by a key, never by anything a user typed.</summary>
public interface IUploadStore
{
    /// <summary>A new, unused key.</summary>
    string NewKey();

    /// <summary>The path of the file for a key. Throws if the key is not one this store made, so a key can never point elsewhere.</summary>
    string PathOf(string key);

    bool Exists(string key);

    /// <summary>Removes the file; nothing happens if it is not there.</summary>
    void Delete(string key);
}
