using System.Net;
using DataBoard.Application.Common;
using DataBoard.Infrastructure.DataSources;
using Microsoft.Extensions.Options;

namespace DataBoard.Infrastructure.Tests;

public class NetworkGuardTests
{
    private static NetworkGuard Guard(bool allowPrivate) =>
        new(Options.Create(new DataSourceSecurityOptions { AllowPrivateNetworks = allowPrivate }));

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("127.1.2.3")]
    [InlineData("10.0.0.1")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.169.254")]   // cloud metadata service
    [InlineData("100.64.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("224.0.0.1")]
    [InlineData("255.255.255.255")]
    [InlineData("::1")]
    [InlineData("::")]
    [InlineData("fe80::1")]
    [InlineData("fc00::1")]
    [InlineData("fd12:3456::1")]
    [InlineData("ff02::1")]
    [InlineData("::ffff:127.0.0.1")]  // IPv4 loopback hidden inside an IPv6 address
    [InlineData("::ffff:10.0.0.1")]
    [InlineData("::ffff:169.254.169.254")]
    [InlineData("2002:7f00:1::1")]    // 6to4
    public void Blocks_internal_addresses(string address) =>
        Assert.True(NetworkGuard.IsBlocked(IPAddress.Parse(address)));

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("172.32.0.1")]
    [InlineData("172.15.255.255")]
    [InlineData("100.63.255.255")]
    [InlineData("192.169.0.1")]
    [InlineData("2606:4700:4700::1111")]
    public void Allows_public_addresses(string address) =>
        Assert.False(NetworkGuard.IsBlocked(IPAddress.Parse(address)));

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("169.254.169.254")]
    [InlineData("::1")]
    [InlineData("localhost")]
    public async Task Rejects_internal_hosts_by_default(string host)
    {
        var error = await Assert.ThrowsAsync<ValidationException>(() => Guard(false).EnsureAllowedAsync(host, CancellationToken.None));
        Assert.Contains("not allowed", error.Errors["ConnectionString"][0]);
    }

    [Fact]
    public async Task Public_literal_addresses_pass_without_any_dns_lookup() =>
        await Guard(false).EnsureAllowedAsync("8.8.8.8", CancellationToken.None);

    [Fact]
    public async Task Everything_is_allowed_when_private_networks_are_enabled()
    {
        await Guard(true).EnsureAllowedAsync("127.0.0.1", CancellationToken.None);
        await Guard(true).EnsureAllowedAsync("localhost", CancellationToken.None);
    }
}
