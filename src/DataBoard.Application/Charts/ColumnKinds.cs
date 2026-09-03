namespace DataBoard.Application.Charts;

/// <summary>What a column can be used for, decided from the database type name.</summary>
public enum ColumnKind
{
    Numeric,
    Boolean,
    Text,
    Uuid,
    Date,
    Timestamp,
    TimestampTz,
    /// <summary>json, arrays, bytea, intervals and so on: can be selected but not filtered or aggregated.</summary>
    Other,
}

public static class ColumnKinds
{
    private static readonly HashSet<string> Numeric = new(StringComparer.OrdinalIgnoreCase)
    {
        "smallint", "integer", "bigint", "numeric", "decimal", "real", "double precision",
        "int2", "int4", "int8", "float4", "float8",
    };

    private static readonly HashSet<string> Text = new(StringComparer.OrdinalIgnoreCase)
    {
        "text", "character varying", "character", "varchar", "char", "bpchar", "citext", "name",
    };

    /// <param name="dataType">The database type name, for example "numeric(10, 2)" or "timestamp with time zone".</param>
    public static ColumnKind Of(string dataType)
    {
        var type = dataType.Split('(', 2)[0].Trim();

        if (Numeric.Contains(type)) return ColumnKind.Numeric;
        if (Text.Contains(type)) return ColumnKind.Text;

        return type.ToLowerInvariant() switch
        {
            "boolean" or "bool" => ColumnKind.Boolean,
            "uuid" => ColumnKind.Uuid,
            "date" => ColumnKind.Date,
            "timestamp without time zone" or "timestamp" => ColumnKind.Timestamp,
            "timestamp with time zone" or "timestamptz" => ColumnKind.TimestampTz,
            _ => ColumnKind.Other,
        };
    }

    public static bool IsTemporal(ColumnKind kind) => kind is ColumnKind.Date or ColumnKind.Timestamp or ColumnKind.TimestampTz;

    /// <summary>Kinds that have a natural order, so &gt;, &lt; and BETWEEN make sense.</summary>
    public static bool IsOrdered(ColumnKind kind) => kind is ColumnKind.Numeric or ColumnKind.Text || IsTemporal(kind);

    /// <summary>Kinds that can be compared for equality and listed in IN (...).</summary>
    public static bool SupportsEquality(ColumnKind kind) => kind != ColumnKind.Other;
}
