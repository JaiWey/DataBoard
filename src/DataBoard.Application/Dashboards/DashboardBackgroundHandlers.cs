using DataBoard.Application.Abstractions;
using DataBoard.Application.Common;
using DataBoard.Application.Datasets;
using DataBoard.Application.Organizations;
using DataBoard.Domain.Dashboards;
using DataBoard.Domain.Organizations;

namespace DataBoard.Application.Dashboards;

/// <summary>What a picture may be: a background is a small image, not a photo album.</summary>
public static class ImageLimits
{
    public const long MaxBytes = 5L * 1024 * 1024;
}

/// <summary>Where pictures are kept. A picture is named by a key the store made, never by anything a user typed.</summary>
public interface IImageStore
{
    /// <summary>Keeps the picture and returns its key. The extension (png, jpg, gif or webp) becomes part of the key.</summary>
    Task<string> SaveAsync(byte[] content, string extension, CancellationToken ct = default);

    /// <summary>The picture, or null if there is none for the key (or the key is not one this store made).</summary>
    Stream? Open(string key);

    /// <summary>Removes the picture; nothing happens if it is not there.</summary>
    void Delete(string key);
}

/// <summary>A picture's real type, found by looking at its first bytes: its name and its claimed type are not trusted.</summary>
public static class ImageSniffer
{
    public static (string ContentType, string Extension)? Detect(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 8 && bytes[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
            return ("image/png", "png");
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return ("image/jpeg", "jpg");
        if (bytes.Length >= 6 && (bytes[..6].SequenceEqual("GIF87a"u8) || bytes[..6].SequenceEqual("GIF89a"u8)))
            return ("image/gif", "gif");
        if (bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8))
            return ("image/webp", "webp");
        return null;
    }

    public static string ContentTypeOfKey(string key) => Path.GetExtension(key).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        _ => "application/octet-stream",
    };
}

/// <param name="Version">Changes whenever the picture does, so a cached copy is never shown for a new picture.</param>
/// <param name="Fit">cover, contain, tile or center.</param>
/// <param name="Opacity">10 to 100.</param>
public sealed record DashboardBackgroundDto(string Version, string Fit, int Opacity);

/// <summary>The picture and what to tell the browser about it.</summary>
public sealed record BackgroundImage(Stream Content, string ContentType);

internal static class BackgroundMapping
{
    public static DashboardBackgroundDto? ToBackgroundDto(this Dashboard d) =>
        d.BackgroundImageKey is { } key ? new DashboardBackgroundDto(Path.GetFileNameWithoutExtension(key), d.BackgroundFit, d.BackgroundOpacity) : null;
}

/// <summary>
/// Sets the picture behind a dashboard, or (with no new picture) how the current one is shown. Editors and Owners.
/// </summary>
public sealed class SetDashboardBackgroundHandler(
    IDashboardRepository dashboards, IImageStore images, IUnitOfWork unitOfWork, OrganizationAccess access)
{
    /// <param name="image">The new picture, or null to keep the current one and only change how it is shown.</param>
    public async Task<DashboardDto> HandleAsync(
        Guid organizationId, Guid dashboardId, Stream? image, string? fit, int? opacity, CancellationToken ct = default)
    {
        await access.RequireRoleAsync(organizationId, OrganizationRole.Editor, ct);
        var dashboard = await dashboards.GetAsync(organizationId, dashboardId, ct) ?? throw new NotFoundException("Dashboard not found.");

        var previous = dashboard.BackgroundImageKey;
        string key;
        string? replaced = null;
        if (image is not null)
        {
            var bytes = await ReadAsync(image, ct);
            var kind = ImageUpload.Kind(bytes);
            key = await images.SaveAsync(bytes, kind.Extension, ct);
            replaced = previous;
        }
        else
        {
            key = previous ?? throw new ValidationException("File", "Choose a picture to use as the background.");
        }

        try
        {
            DatasetQuery.Guard(() =>
            {
                dashboard.SetBackground(key, fit ?? dashboard.BackgroundFit, opacity ?? dashboard.BackgroundOpacity);
                return true;
            });
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch
        {
            // Nothing was saved, so a picture that was just stored is not wanted.
            if (replaced is not null || previous is null) images.Delete(key);
            throw;
        }

        if (replaced is not null) images.Delete(replaced);
        return dashboard.ToDto();
    }

    private static Task<byte[]> ReadAsync(Stream stream, CancellationToken ct) => ImageUpload.ReadAsync(stream, "Choose a picture to use as the background.", ct);
}

/// <summary>Reading an uploaded picture safely: bounded in size, and its real type found by looking at it.</summary>
internal static class ImageUpload
{
    public static async Task<byte[]> ReadAsync(Stream stream, string emptyMessage, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > ImageLimits.MaxBytes)
                throw new ValidationException("File", $"The picture can be at most {ImageLimits.MaxBytes / (1024 * 1024)} MB.");
            buffer.Write(chunk, 0, read);
        }
        if (buffer.Length == 0) throw new ValidationException("File", emptyMessage);
        return buffer.ToArray();
    }

    public static (string ContentType, string Extension) Kind(byte[] bytes) =>
        ImageSniffer.Detect(bytes) ?? throw new ValidationException("File", "That is not a PNG, JPEG, GIF or WebP picture.");
}

public sealed class RemoveDashboardBackgroundHandler(
    IDashboardRepository dashboards, IImageStore images, IUnitOfWork unitOfWork, OrganizationAccess access)
{
    public async Task<DashboardDto> HandleAsync(Guid organizationId, Guid dashboardId, CancellationToken ct = default)
    {
        await access.RequireRoleAsync(organizationId, OrganizationRole.Editor, ct);
        var dashboard = await dashboards.GetAsync(organizationId, dashboardId, ct) ?? throw new NotFoundException("Dashboard not found.");

        var key = dashboard.BackgroundImageKey;
        dashboard.ClearBackground();
        await unitOfWork.SaveChangesAsync(ct);
        if (key is not null) images.Delete(key);
        return dashboard.ToDto();
    }
}

/// <summary>The picture behind a dashboard, for any member of its organization.</summary>
public sealed class GetDashboardBackgroundHandler(IDashboardRepository dashboards, IImageStore images, OrganizationAccess access)
{
    public async Task<BackgroundImage> HandleAsync(Guid organizationId, Guid dashboardId, CancellationToken ct = default)
    {
        await access.RequireRoleAsync(organizationId, OrganizationRole.Viewer, ct);
        var dashboard = await dashboards.GetAsync(organizationId, dashboardId, ct) ?? throw new NotFoundException("Dashboard not found.");
        return Open(dashboard, images);
    }

    internal static BackgroundImage Open(Dashboard dashboard, IImageStore images)
    {
        if (dashboard.BackgroundImageKey is not { } key) throw new NotFoundException("This dashboard has no background.");
        var stream = images.Open(key) ?? throw new NotFoundException("This dashboard has no background.");
        return new BackgroundImage(stream, ImageSniffer.ContentTypeOfKey(key));
    }
}

/// <summary>The picture behind a dashboard, for a visitor with a valid link (and only for that link's dashboard).</summary>
public sealed class GetSharedDashboardBackgroundHandler(
    IDashboardShareRepository shares, IDashboardRepository dashboards, IImageStore images, IClock clock)
{
    public async Task<BackgroundImage> HandleAsync(string? token, CancellationToken ct = default)
    {
        var (_, dashboard) = await SharedLinks.ResolveAsync(token, shares, dashboards, clock, ct);
        return GetDashboardBackgroundHandler.Open(dashboard, images);
    }
}
