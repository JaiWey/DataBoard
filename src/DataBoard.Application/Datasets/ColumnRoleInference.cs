using DataBoard.Domain.Datasets;

namespace DataBoard.Application.Datasets;

/// <summary>Numbers are measures (summed, averaged); everything else is a dimension (grouped by).</summary>
public static class ColumnRoleInference
{
    private static readonly HashSet<string> Numeric = new(StringComparer.OrdinalIgnoreCase)
    {
        "smallint", "integer", "bigint", "numeric", "decimal", "real", "double precision",
        "int2", "int4", "int8", "float4", "float8",
    };

    /// <param name="dataType">The database type name, for example "numeric(10,2)" or "integer[]".</param>
    public static ColumnRole Infer(string dataType)
    {
        var baseType = dataType.Split('(', 2)[0].Trim();
        return Numeric.Contains(baseType) ? ColumnRole.Measure : ColumnRole.Dimension;
    }
}
