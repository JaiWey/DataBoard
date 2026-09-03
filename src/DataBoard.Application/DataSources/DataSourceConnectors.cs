using DataBoard.Application.Common;
using DataBoard.Domain.DataSources;

namespace DataBoard.Application.DataSources;

/// <summary>Finds the connector for a data source type.</summary>
public sealed class DataSourceConnectors(IEnumerable<IDataSourceConnector> connectors)
{
    private readonly Dictionary<DataSourceType, IDataSourceConnector> _byType = connectors.ToDictionary(c => c.Type);

    public bool IsSupported(DataSourceType type) => _byType.ContainsKey(type);

    public IDataSourceConnector Get(DataSourceType type) =>
        _byType.TryGetValue(type, out var connector)
            ? connector
            : throw new ValidationException("Type", $"{type} data sources are not supported yet.");
}
