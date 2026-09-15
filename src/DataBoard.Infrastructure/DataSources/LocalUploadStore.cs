using System.Text.RegularExpressions;
using DataBoard.Application.DataSources;
using Microsoft.Extensions.Options;

namespace DataBoard.Infrastructure.DataSources;

public sealed class UploadOptions
{
    public const string SectionName = "Uploads";

    /// <summary>
    /// The folder the database files of uploaded data are kept in. It has to survive a restart, and, if several servers
    /// run, be a folder they share. Left out, a folder under the system's temporary files is used (fine for trying it out).
    /// </summary>
    public string? Path { get; set; }
}

/// <summary>
/// Keeps the database files of uploaded data in a folder on disk. A file is named by a random key that this class made;
/// anything else is refused, so no key, however it reached here, can name a file outside the folder.
/// </summary>
public sealed partial class LocalUploadStore(IOptions<UploadOptions> options) : IUploadStore
{
    [GeneratedRegex("^[0-9a-f]{32}\\.db$")]
    private static partial Regex KeyPattern();

    private string Folder => options.Value.Path is { Length: > 0 } path
        ? Path.GetFullPath(path)
        : Path.Combine(Path.GetTempPath(), "databoard-uploads");

    public string NewKey() => Guid.NewGuid().ToString("N") + ".db";

    public string PathOf(string key)
    {
        if (!KeyPattern().IsMatch(key ?? string.Empty))
            throw new ArgumentException("That is not the key of an uploaded file.", nameof(key));

        Directory.CreateDirectory(Folder);
        return Path.Combine(Folder, key);
    }

    public bool Exists(string key) => File.Exists(PathOf(key));

    public void Delete(string key)
    {
        var path = PathOf(key);
        foreach (var file in new[] { path, path + "-journal", path + "-wal", path + "-shm" })
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
                // Still open somewhere for the moment; it is only disk space, and the key is never used again.
            }
        }
    }
}
