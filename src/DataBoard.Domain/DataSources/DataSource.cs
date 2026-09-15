using DataBoard.Domain.Common;

namespace DataBoard.Domain.DataSources;

/// <summary>A connection to an external data system that datasets are built on.</summary>
public sealed class DataSource : BaseEntity
{
    public const int MaxNameLength = 200;

    private DataSource() { }

    public Guid OrganizationId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public DataSourceType Type { get; private set; }

    /// <summary>Encrypted before it reaches the domain; the domain never sees the plain connection string.</summary>
    public string EncryptedConnectionString { get; private set; } = string.Empty;

    public DateTime CreatedUtc { get; private set; }

    /// <summary>
    /// Points the data source at new data, for a file that is uploaded again. It stays the same data source, so the datasets
    /// and charts built on it carry on; results stored for the old file are no longer asked for, because the connection is
    /// part of what identifies a stored result.
    /// </summary>
    public void ReplaceConnection(string encryptedConnectionString)
    {
        if (string.IsNullOrWhiteSpace(encryptedConnectionString))
            throw new ArgumentException("Connection string is required.", nameof(encryptedConnectionString));
        EncryptedConnectionString = encryptedConnectionString;
    }

    public static DataSource Create(
        Guid organizationId,
        string name,
        DataSourceType type,
        string encryptedConnectionString,
        DateTime createdUtc)
    {
        if (organizationId == Guid.Empty)
            throw new ArgumentException("Organization is required.", nameof(organizationId));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required.", nameof(name));
        if (name.Length > MaxNameLength)
            throw new ArgumentException($"Name must be at most {MaxNameLength} characters.", nameof(name));
        if (!Enum.IsDefined(type))
            throw new ArgumentException("Unknown data source type.", nameof(type));
        if (string.IsNullOrWhiteSpace(encryptedConnectionString))
            throw new ArgumentException("Connection string is required.", nameof(encryptedConnectionString));

        return new DataSource
        {
            OrganizationId = organizationId,
            Name = name.Trim(),
            Type = type,
            EncryptedConnectionString = encryptedConnectionString,
            CreatedUtc = createdUtc,
        };
    }
}
