namespace DataBoard.Infrastructure.DataSources;

public sealed class DataSourceSecurityOptions
{
    public const string SectionName = "DataSourceSecurity";

    /// <summary>
    /// When false (the default), data sources may not point at loopback, private, link-local or otherwise internal
    /// addresses, so users cannot use the server to probe its own network (SSRF). Enable only for local development.
    /// </summary>
    public bool AllowPrivateNetworks { get; set; }
}
