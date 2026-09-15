using System.Text;
using DataBoard.Application.Common;
using DataBoard.Application.DataSources;
using DataBoard.Infrastructure.DataSources;
using Microsoft.Data.Sqlite;

namespace DataBoard.Infrastructure.Tests;

/// <summary>
/// Reading an uploaded CSV into a database file: what type each column gets, how odd files are read, what is refused, and
/// that nothing the file says (its name, its column names, its values) can reach beyond the table that is made from it.
/// </summary>
public sealed class CsvImporterTests : IDisposable
{
    private readonly CsvImporter _importer = new();
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "databoard-tests-" + Guid.NewGuid().ToString("N"));

    public CsvImporterTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    private string NewPath() => Path.Combine(_folder, Guid.NewGuid().ToString("N") + ".db");

    private static MemoryStream Csv(string text, Encoding? encoding = null)
    {
        encoding ??= new UTF8Encoding(false);
        return new MemoryStream([.. encoding.GetPreamble(), .. encoding.GetBytes(text)]);
    }

    private async Task<(CsvImportResult Result, string Path)> ImportAsync(
        string text, string fileName = "data.csv", CsvImportOptions? options = null, Encoding? encoding = null)
    {
        var path = NewPath();
        var result = await _importer.ImportAsync(Csv(text, encoding), fileName, options ?? new CsvImportOptions(), path);
        return (result, path);
    }

    private static List<object?[]> Query(string path, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var rows = new List<object?[]>();
        while (reader.Read())
        {
            var row = new object?[reader.FieldCount];
            for (var i = 0; i < row.Length; i++) row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            rows.Add(row);
        }
        return rows;
    }

    private async Task<ValidationException> RejectedAsync(string text, string fileName = "data.csv", CsvImportOptions? options = null) =>
        await Assert.ThrowsAsync<ValidationException>(async () => await ImportAsync(text, fileName, options));

    private static string Types(CsvImportResult result) => string.Join(",", result.Columns.Select(c => c.DataType));

    // ---- the types of the columns ---------------------------------------------------------------------------

    [Fact]
    public async Task Gives_each_column_the_type_that_fits_everything_in_it()
    {
        var (result, path) = await ImportAsync("""
            id,price,ratio,paid,sold_on,sold_at,note
            1,10,0.5,true,2026-03-05,2026-03-05 14:30:00,hello
            2,20,1.5,false,2026-03-06,2026-03-06T09:00:00Z,world
            3,30.50,2,TRUE,2026-03-07,2026-03-07 10:15,
            """, "orders.csv");

        Assert.Equal("orders", result.TableName);
        Assert.Equal(3, result.RowCount);
        Assert.Equal(["id", "price", "ratio", "paid", "sold_on", "sold_at", "note"], result.Columns.Select(c => c.Name));
        Assert.Equal("bigint,double precision,double precision,boolean,date,timestamp without time zone,text", Types(result));
        var row = Query(path, "SELECT * FROM orders ORDER BY id")[2];
        Assert.Equal(3L, row[0]);
        Assert.Equal(30.5, row[1]);
        Assert.Equal(1L, row[3]); // true is stored as 1
        Assert.Equal("2026-03-07", row[4]);
        Assert.Equal("2026-03-07 10:15:00", row[5]); // a time without seconds gets them, so dates sort and compare the same way
        Assert.Null(row[6]); // an empty value is no value
    }

    [Fact]
    public async Task Makes_a_column_text_as_soon_as_one_value_is_not_what_the_rest_are()
    {
        var (result, path) = await ImportAsync("a,b,c,d\n1,1,2026-01-01,true\n2,two,2026-01-02,false\n3,3,tomorrow,maybe\n");

        Assert.Equal("bigint,text,text,text", Types(result));
        Assert.Equal("two", Query(path, "SELECT b FROM data WHERE a = 2")[0][0]); // nothing is lost
    }

    [Fact]
    public async Task Keeps_codes_that_look_like_numbers_as_text_so_leading_zeros_survive()
    {
        var (result, path) = await ImportAsync("zip,count\n00123,5\n02134,6\n90210,7\n");

        Assert.Equal("text,bigint", Types(result));
        Assert.Equal("00123", Query(path, "SELECT zip FROM data ORDER BY count")[0][0]);
    }

    [Theory]
    [InlineData("1\n2\n3", "bigint")]
    [InlineData("1\n2.5\n3", "double precision")]
    [InlineData("-1\n+2\n3", "bigint")]
    [InlineData("1e3\n2.5E-2", "double precision")]
    [InlineData(".5\n5.", "double precision")]
    [InlineData("1,000\n2,000", "text")]            // a thousands separator: not read as a number (a comma here is quoted below)
    [InlineData("NaN\n1", "text")]
    [InlineData("Infinity\n1", "text")]
    [InlineData("1.2.3\n4", "text")]
    [InlineData("12345678901234567890\n1", "text")] // too big for a 64-bit integer: kept whole as text
    [InlineData("2026-03-05\n2026-03-06", "date")]
    [InlineData("2026-02-30\n2026-03-06", "text")]  // not a real date
    [InlineData("2026-03-05\n2026-03-06 10:00:00", "timestamp without time zone")] // dates among times: all are times
    [InlineData("2026-03-05T10:00:00+02:00\n2026-03-05T08:00:00Z", "timestamp without time zone")]
    [InlineData("05/03/2026\n06/03/2026", "text")]  // day or month first? not guessed
    [InlineData("true\nfalse\nTrue", "boolean")]
    [InlineData("yes\nno", "text")]
    [InlineData("1\n0\n1", "bigint")]               // zeros and ones are numbers, not truth values
    [InlineData("", "text")]
    public async Task Works_out_the_type_of_a_column_from_its_values(string values, string expected)
    {
        var quoted = string.Join("\n", values.Split('\n').Select(v => v.Contains(',') ? $"\"{v}\"" : v));
        var (result, _) = await ImportAsync("x,id\n" + string.Join("\n", quoted.Split('\n').Select((v, i) => $"{v},{i}")));

        Assert.Equal(expected, result.Columns[0].DataType);
    }

    [Fact]
    public async Task A_column_with_no_values_at_all_is_text()
    {
        var (result, _) = await ImportAsync("a,b\n1,\n2,\n");

        Assert.Equal("bigint,text", Types(result));
    }

    [Fact]
    public async Task Turns_times_with_an_offset_into_utc_and_keeps_fractions_of_a_second()
    {
        var (_, path) = await ImportAsync("t\n2026-03-05T10:00:00+02:00\n2026-03-05 10:00:00.25\n");

        var times = Query(path, "SELECT t FROM data ORDER BY t").Select(r => (string)r[0]!).ToList();

        Assert.Equal(["2026-03-05 08:00:00", "2026-03-05 10:00:00.25"], times);
    }

    // ---- how a file is read ---------------------------------------------------------------------------------

    [Theory]
    [InlineData("a,b,c\n1,2,3\n4,5,6\n", ",")]
    [InlineData("a;b;c\n1;2;3\n4;5;6\n", ";")]
    [InlineData("a\tb\tc\n1\t2\t3\n4\t5\t6\n", "\t")]
    [InlineData("a|b|c\n1|2|3\n4|5|6\n", "|")]
    [InlineData("\"a;1\",b,c\n\"x;y\",2,3\n", ",")]       // a semicolon inside quotes is only text
    [InlineData("a;b;c\n1,5;2;3\n4,5;5;6\n", ";")]        // decimal commas, semicolons between values
    public async Task Finds_the_delimiter_by_looking_at_the_file(string text, string delimiter)
    {
        var (result, _) = await ImportAsync(text);

        Assert.Equal(delimiter, result.Delimiter);
        Assert.Equal(3, result.Columns.Count);
    }

    [Fact]
    public async Task Takes_the_delimiter_it_is_told_to_over_the_one_it_would_have_found()
    {
        var (result, _) = await ImportAsync("a;b,c\n1;2,3\n", options: new CsvImportOptions(Delimiter: ";"));

        Assert.Equal(["a", "b,c"], result.Columns.Select(c => c.Name));
    }

    [Theory]
    [InlineData("tab", "\t")]
    [InlineData("\\t", "\t")]
    [InlineData("|", "|")]
    public async Task Accepts_the_delimiter_as_a_word_or_a_character(string given, string expected)
    {
        var text = "a" + expected + "b\n1" + expected + "2\n";

        var (result, _) = await ImportAsync(text, options: new CsvImportOptions(Delimiter: given));

        Assert.Equal(expected, result.Delimiter);
    }

    [Theory]
    [InlineData("x")]
    [InlineData("ab")]
    [InlineData("\"")]
    public async Task Refuses_a_delimiter_that_is_none_of_the_four(string given)
    {
        var error = await RejectedAsync("a,b\n1,2\n", options: new CsvImportOptions(Delimiter: given));

        Assert.Contains("Delimiter", error.Errors.Keys);
    }

    [Fact]
    public async Task Reads_quoted_values_with_delimiters_quotes_and_line_breaks_in_them()
    {
        var (result, path) = await ImportAsync("name,note\n\"Smith, Jo\",\"said \"\"hi\"\"\"\n\"Lee\",\"line one\nline two\"\n");

        Assert.Equal(2, result.RowCount);
        Assert.Equal("said \"hi\"", Query(path, "SELECT note FROM data WHERE name = 'Smith, Jo'")[0][0]);
        Assert.Equal("line one\nline two", Query(path, "SELECT note FROM data WHERE name = 'Lee'")[0][0]);
    }

    [Fact]
    public async Task Reads_windows_and_unix_line_endings_and_ignores_blank_lines()
    {
        var (result, _) = await ImportAsync("a,b\r\n1,2\r\n\r\n3,4\n\n5,6\n");

        Assert.Equal(3, result.RowCount);
    }

    [Fact]
    public async Task Reads_text_in_any_language_and_ignores_the_byte_order_mark_excel_writes()
    {
        var text = "名前,都市\n太郎,東京\nZoë,Zürich\n";
        var (result, path) = await ImportAsync(text, encoding: new UTF8Encoding(true));

        Assert.Equal(["名前", "都市"], result.Columns.Select(c => c.Name)); // not "\uFEFF名前"
        Assert.Equal("Zürich", Query(path, "SELECT 都市 FROM data WHERE 名前 = 'Zoë'".Replace("都市", "\"都市\"").Replace("名前", "\"名前\""))[0][0]);
    }

    [Fact]
    public async Task Reads_utf16_files_that_say_so_with_a_byte_order_mark()
    {
        var (result, _) = await ImportAsync("a,b\n1,2\n", encoding: new UnicodeEncoding(false, true));

        Assert.Equal(["a", "b"], result.Columns.Select(c => c.Name));
    }

    [Fact]
    public async Task Pads_a_short_row_and_refuses_a_row_with_more_values_than_columns()
    {
        var (_, path) = await ImportAsync("a,b,c\n1,2\n4,5,6\n");
        Assert.Null(Query(path, "SELECT c FROM data WHERE a = 1")[0][0]);

        var error = await RejectedAsync("a,b\n1,2\n3,4,5\n");
        Assert.Contains("Row 3 has 3 values but there are 2 columns", error.Errors["File"][0]);
    }

    [Fact]
    public async Task Allows_a_trailing_delimiter_that_adds_an_empty_value()
    {
        var (result, _) = await ImportAsync("a,b\n1,2,\n3,4,\n");

        Assert.Equal(2, result.Columns.Count);
        Assert.Equal(2, result.RowCount);
    }

    [Fact]
    public async Task Without_a_header_the_columns_are_numbered()
    {
        var (result, path) = await ImportAsync("1,x\n2,y\n", options: new CsvImportOptions(HasHeader: false));

        Assert.Equal(["column_1", "column_2"], result.Columns.Select(c => c.Name));
        Assert.Equal(2, result.RowCount);
        Assert.Equal("x", Query(path, "SELECT column_2 FROM data WHERE column_1 = 1")[0][0]);
    }

    // ---- names ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Names_empty_columns_and_separates_columns_with_the_same_name_ignoring_case()
    {
        var (result, _) = await ImportAsync("a,,A,a,\n1,2,3,4,5\n");

        Assert.Equal(["a", "column_2", "A_2", "a_3", "column_5"], result.Columns.Select(c => c.Name));
    }

    [Fact]
    public async Task Keeps_column_names_as_they_are_written_with_spaces_and_symbols_but_not_control_characters()
    {
        var (result, path) = await ImportAsync("Order ID,Total ($),\"Cust\u0001omer\"\n1,2,3\n");

        Assert.Equal(["Order ID", "Total ($)", "Customer"], result.Columns.Select(c => c.Name));
        Assert.Equal(1L, Query(path, "SELECT \"Order ID\" FROM data")[0][0]);
    }

    [Fact]
    public async Task A_column_name_with_quotes_or_sql_in_it_is_only_a_name()
    {
        var hostile = "a\"; DROP TABLE data; --";

        var (result, path) = await ImportAsync($"\"a\"\"; DROP TABLE data; --\",b\n1,2\n");

        Assert.Equal(hostile, result.Columns[0].Name);
        Assert.Equal(1L, Query(path, "SELECT count(*) FROM data")[0][0]); // the table is still there
        Assert.Equal(1L, Query(path, $"SELECT \"{hostile.Replace("\"", "\"\"")}\" FROM data")[0][0]);
    }

    [Fact]
    public async Task Cuts_a_very_long_column_name()
    {
        var (result, _) = await ImportAsync(new string('x', 500) + ",b\n1,2\n");

        Assert.Equal(CsvLimits.MaxNameLength, result.Columns[0].Name.Length);
    }

    [Theory]
    [InlineData("Sales 2026.csv", "sales_2026")]
    [InlineData("orders.CSV", "orders")]
    [InlineData("2026 sales.csv", "t_2026_sales")]
    [InlineData("a--b  c.csv", "a_b_c")]
    [InlineData("???.csv", "data")]
    [InlineData("", "data")]
    [InlineData("sqlite_master.csv", "t_sqlite_master")]
    [InlineData("Ünïcödé.csv", "n_c_d")]
    public async Task Makes_the_table_name_from_the_file_name(string fileName, string expected)
    {
        var (result, path) = await ImportAsync("a\n1\n", fileName);

        Assert.Equal(expected, result.TableName);
        Assert.Equal(1L, Query(path, $"SELECT count(*) FROM \"{expected}\"")[0][0]);
    }

    [Theory]
    [InlineData("../../etc/passwd", "passwd")]
    [InlineData("..\\..\\windows\\system32\\config.csv", "config")]
    [InlineData("/var/data/report.csv", "report")]
    [InlineData("C:\\Users\\me\\report.csv", "report")]
    [InlineData("x\"; DROP TABLE y; --.csv", "x_drop_table_y")]
    public async Task Uses_nothing_of_a_file_name_but_plain_letters_and_never_a_folder(string fileName, string expected)
    {
        var (result, path) = await ImportAsync("a\n1\n", fileName);

        Assert.Equal(expected, result.TableName);
        Assert.Equal(_folder, Path.GetDirectoryName(path)); // the file went where it was told to, whatever the name said
    }

    [Fact]
    public async Task Takes_a_table_name_that_was_asked_for_and_refuses_a_bad_one()
    {
        var (result, _) = await ImportAsync("a\n1\n", "x.csv", new CsvImportOptions(TableName: "My_Table1"));
        Assert.Equal("My_Table1", result.TableName);

        foreach (var bad in new[] { "1abc", "has space", "a-b", "x\"y", "sqlite_hidden", new string('a', 65) })
        {
            var error = await RejectedAsync("a\n1\n", options: new CsvImportOptions(TableName: bad));
            Assert.Contains("TableName", error.Errors.Keys);
        }
    }

    // ---- what is refused -----------------------------------------------------------------------------------

    [Theory]
    [InlineData("")]
    public async Task Refuses_an_empty_file(string text) =>
        Assert.Contains("empty", (await RejectedAsync(text)).Errors["File"][0]);

    [Theory]
    [InlineData("\n\n\n")]
    [InlineData("a,b\n")]
    [InlineData("a,b\n,\n\n")]
    public async Task Refuses_a_file_without_any_rows_of_data(string text) =>
        Assert.Contains("no rows", (await RejectedAsync(text)).Errors["File"][0]);

    [Fact]
    public async Task Refuses_a_file_that_is_not_text()
    {
        var bytes = new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x00, 0x00, 0x08, 0x00, 0x00, 0x00 }; // the start of a zip, an xlsx or a docx
        var error = await Assert.ThrowsAsync<ValidationException>(() =>
            _importer.ImportAsync(new MemoryStream(bytes), "book.xlsx", new CsvImportOptions(), NewPath()));

        Assert.Contains("not text", error.Errors["File"][0]);
    }

    [Fact]
    public async Task Refuses_a_file_that_is_too_big_and_does_not_read_all_of_it_first()
    {
        var oversized = new NeverEndingStream();

        var error = await Assert.ThrowsAsync<ValidationException>(() =>
            _importer.ImportAsync(oversized, "big.csv", new CsvImportOptions(), NewPath()));

        Assert.Contains("larger than 20 MB", error.Errors["File"][0]);
        Assert.InRange(oversized.BytesRead, CsvLimits.MaxBytes, CsvLimits.MaxBytes + 200_000); // it stopped soon after the limit
    }

    [Fact]
    public async Task Refuses_too_many_columns_and_too_many_rows()
    {
        var wide = string.Join(",", Enumerable.Range(0, CsvLimits.MaxColumns + 1).Select(i => "c" + i));
        Assert.Contains("columns", (await RejectedAsync(wide + "\n" + string.Join(",", Enumerable.Repeat("1", CsvLimits.MaxColumns + 1)))).Errors["File"][0]);

        var tall = "a\n" + string.Concat(Enumerable.Repeat("1\n", CsvLimits.MaxRows + 1));
        Assert.Contains("rows", (await RejectedAsync(tall)).Errors["File"][0]);
    }

    [Fact]
    public async Task Imports_a_file_right_at_the_limits()
    {
        var (result, _) = await ImportAsync("a\n" + string.Concat(Enumerable.Repeat("1\n", CsvLimits.MaxRows)));

        Assert.Equal(CsvLimits.MaxRows, result.RowCount);
    }

    [Fact]
    public async Task Says_where_a_file_that_cannot_be_read_goes_wrong_in_words_the_person_can_use()
    {
        var error = await RejectedAsync("a,b\n1,\"never closed\n2,3\n");

        Assert.StartsWith("The file could not be read", error.Errors["File"][0]);
        Assert.DoesNotContain("CsvHelper", error.Errors["File"][0]);
    }

    [Fact]
    public async Task Leaves_no_file_behind_when_the_import_fails()
    {
        var path = NewPath();

        await Assert.ThrowsAsync<ValidationException>(() =>
            _importer.ImportAsync(Csv("a,b\n1,2\n3,4,5\n"), "x.csv", new CsvImportOptions(), path));

        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task Writes_over_a_file_that_is_already_there_rather_than_adding_to_it()
    {
        var path = NewPath();
        await _importer.ImportAsync(Csv("a\n1\n2\n"), "x.csv", new CsvImportOptions(), path);

        await _importer.ImportAsync(Csv("a\n9\n"), "x.csv", new CsvImportOptions(), path);

        Assert.Equal(1L, Query(path, "SELECT count(*) FROM x")[0][0]);
    }

    [Fact]
    public async Task Stops_when_asked_to()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _importer.ImportAsync(Csv("a\n1\n"), "x.csv", new CsvImportOptions(), NewPath(), cancelled.Token));
    }

    // ---- values are only values ----------------------------------------------------------------------------

    [Theory]
    [InlineData("=HYPERLINK(\"http://evil\")")]
    [InlineData("'; DROP TABLE data; --")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("{{region}}")]
    [InlineData("@databoard_p0")]
    public async Task Keeps_a_value_exactly_as_it_is_whatever_it_looks_like(string value)
    {
        var (_, path) = await ImportAsync($"id,note\n1,\"{value.Replace("\"", "\"\"")}\"\n");

        Assert.Equal(value, Query(path, "SELECT note FROM data")[0][0]);
        Assert.Equal(1L, Query(path, "SELECT count(*) FROM data")[0][0]);
    }

    private sealed class NeverEndingStream : Stream
    {
        public long BytesRead { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => Fill(buffer.AsSpan(offset, count));
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => ValueTask.FromResult(Fill(buffer.Span));
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        private int Fill(Span<byte> span)
        {
            span.Fill((byte)'a');
            BytesRead += span.Length;
            return span.Length;
        }
    }
}
