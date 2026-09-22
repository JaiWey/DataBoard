using System.Text.RegularExpressions;
using DataBoard.Application.Dashboards;
using Microsoft.Extensions.Options;

namespace DataBoard.Infrastructure.DataSources;

/// <summary>
/// Keeps pictures in an "images" folder next to the uploaded data. A picture is named by a random key this class made;
/// anything else is refused, so no key, however it reached here, can name a file outside the folder.
/// </summary>
public sealed partial class LocalImageStore(IOptions<UploadOptions> options) : IImageStore
{
    [GeneratedRegex("^[0-9a-f]{32}\\.(png|jpg|gif|webp)$")]
    private static partial Regex KeyPattern();

    private string Folder => Path.Combine(
        options.Value.Path is { Length: > 0 } path ? Path.GetFullPath(path) : Path.Combine(Path.GetTempPath(), "databoard-uploads"),
        "images");

    public async Task<string> SaveAsync(byte[] content, string extension, CancellationToken ct = default)
    {
        var key = $"{Guid.NewGuid():N}.{extension}";
        if (!KeyPattern().IsMatch(key)) throw new ArgumentException("That is not a kind of picture that is kept.", nameof(extension));

        Directory.CreateDirectory(Folder);
        await File.WriteAllBytesAsync(Path.Combine(Folder, key), content, ct);
        return key;
    }

    public Stream? Open(string key)
    {
        if (!KeyPattern().IsMatch(key ?? string.Empty)) return null;
        var path = Path.Combine(Folder, key!);
        return File.Exists(path) ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read) : null;
    }

    public void Delete(string key)
    {
        if (!KeyPattern().IsMatch(key ?? string.Empty)) return;
        try
        {
            File.Delete(Path.Combine(Folder, key!));
        }
        catch (IOException)
        {
            // Still open for the moment; it is only disk space, and the key is never used again.
        }
    }
}
