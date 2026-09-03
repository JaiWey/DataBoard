using DataBoard.Domain.DataSources;

namespace DataBoard.Application.DataSources;

public sealed record CreateDataSourceRequest(
    Guid OrganizationId,
    string Name,
    DataSourceType Type,
    string ConnectionString);

/// <summary>Never includes the connection string.</summary>
public sealed record DataSourceDto(
    Guid Id,
    Guid OrganizationId,
    string Name,
    DataSourceType Type,
    DateTime CreatedUtc);
