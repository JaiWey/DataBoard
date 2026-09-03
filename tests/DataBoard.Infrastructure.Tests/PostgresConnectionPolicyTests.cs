using DataBoard.Application.Common;
using DataBoard.Infrastructure.DataSources;
using Npgsql;

namespace DataBoard.Infrastructure.Tests;

public class PostgresConnectionPolicyTests
{
    private const string Valid = "Host=db.example.com;Port=5432;Database=sales;Username=reader;Password=p@ss";

    [Fact]
    public void Accepts_the_plain_options()
    {
        var builder = PostgresConnectionPolicy.Parse(Valid + ";SSL Mode=Require;Trust Server Certificate=true");

        Assert.Equal("db.example.com", builder.Host);
        Assert.Equal("sales", builder.Database);
    }

    [Theory]
    [InlineData("Server=db;Database=d;User Id=u;Password=p")]
    [InlineData("Host=db;Database=d;Username=u")]
    public void Accepts_common_aliases(string connectionString) =>
        Assert.Equal("db", PostgresConnectionPolicy.Parse(connectionString).Host);

    [Theory]
    [InlineData(Valid + ";Passfile=/etc/passwd")]
    [InlineData(Valid + ";SSL Certificate=/etc/ssl/private/server.key")]
    [InlineData(Valid + ";SSL Key=/etc/ssl/private/server.key")]
    [InlineData(Valid + ";Root Certificate=/etc/ssl/ca.pem")]
    [InlineData(Valid + ";Options=-c default_transaction_read_only=off")]
    [InlineData(Valid + ";Application Name=x")]
    [InlineData(Valid + ";Pooling=true")]
    [InlineData(Valid + ";Command Timeout=0")]
    [InlineData(Valid + ";Include Error Detail=true")]
    [InlineData(Valid + ";Load Balance Hosts=true")]
    public void Rejects_options_that_read_files_or_weaken_limits(string connectionString)
    {
        var error = Assert.Throws<ValidationException>(() => PostgresConnectionPolicy.Parse(connectionString));
        Assert.Contains("ConnectionString", error.Errors.Keys);
    }

    [Theory]
    [InlineData("Host=a,b;Database=d;Username=u")]
    [InlineData("Host=/var/run/postgresql;Database=d;Username=u")]
    [InlineData("Host=a b;Database=d;Username=u")]
    [InlineData("Database=d;Username=u")]
    [InlineData("Host=db;Username=u")]
    [InlineData("Host=db;Database=d")]
    [InlineData("Host=db;Database=d;Username=u;Port=0")]
    [InlineData("Host=db;Database=d;Username=u;Port=70000")]
    [InlineData("this is not a connection string")]
    [InlineData("")]
    public void Rejects_incomplete_or_unsafe_hosts(string connectionString) =>
        Assert.Throws<ValidationException>(() => PostgresConnectionPolicy.Parse(connectionString));

    [Fact]
    public void Harden_forces_read_only_timeouts_and_no_pooling()
    {
        var hardened = new NpgsqlConnectionStringBuilder(PostgresConnectionPolicy.Harden(PostgresConnectionPolicy.Parse(Valid)));

        Assert.False(hardened.Pooling);
        Assert.False(hardened.Multiplexing);
        Assert.Equal(5, hardened.Timeout);
        Assert.Equal(15, hardened.CommandTimeout);
        Assert.Contains("default_transaction_read_only=on", hardened.Options);
        Assert.Contains("statement_timeout=10000", hardened.Options);
        Assert.Equal("DataBoard", hardened.ApplicationName);
        Assert.Equal("db.example.com", hardened.Host);
        Assert.Equal("reader", hardened.Username);
    }
}
