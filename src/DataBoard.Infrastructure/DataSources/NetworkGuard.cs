using System.Net;
using System.Net.Sockets;
using DataBoard.Application.Common;
using Microsoft.Extensions.Options;

namespace DataBoard.Infrastructure.DataSources;

/// <summary>
/// Stops users from pointing a data source at the server's own network. The check runs right before each
/// connection. A host name that changes its answer between this check and the connection (DNS rebinding) is a
/// known residual risk; production deployments should also restrict outbound traffic at the network level.
/// </summary>
internal sealed class NetworkGuard(IOptions<DataSourceSecurityOptions> options)
{
    private static readonly TimeSpan ResolveTimeout = TimeSpan.FromSeconds(5);

    public async Task EnsureAllowedAsync(string host, CancellationToken ct)
    {
        if (options.Value.AllowPrivateNetworks) return;

        IPAddress[] addresses;
        if (IPAddress.TryParse(host, out var literal))
        {
            addresses = [literal];
        }
        else
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(ResolveTimeout);
            try
            {
                addresses = await Dns.GetHostAddressesAsync(host, timeout.Token);
            }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException && !ct.IsCancellationRequested)
            {
                throw new ValidationException("ConnectionString", "The host name could not be resolved.");
            }
        }

        // Every resolved address must be public, otherwise a name with one public and one internal record would pass.
        if (addresses.Length == 0 || addresses.Any(IsBlocked))
            throw new ValidationException("ConnectionString", "That host is not allowed.");
    }

    public static bool IsBlocked(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip)) return true;

        var b = ip.GetAddressBytes();

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            return b[0] == 0                                        // "this network"
                || b[0] == 10                                       // private
                || (b[0] == 100 && b[1] is >= 64 and <= 127)        // carrier-grade NAT
                || b[0] == 127                                      // loopback
                || (b[0] == 169 && b[1] == 254)                     // link-local, includes cloud metadata 169.254.169.254
                || (b[0] == 172 && b[1] is >= 16 and <= 31)         // private
                || (b[0] == 192 && b[1] == 0 && b[2] == 0)          // IETF protocol assignments
                || (b[0] == 192 && b[1] == 168)                     // private
                || (b[0] == 198 && b[1] is 18 or 19)                // benchmarking
                || b[0] >= 224;                                     // multicast, reserved, broadcast
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return ip.Equals(IPAddress.IPv6Any)
                || ip.IsIPv6LinkLocal
                || ip.IsIPv6SiteLocal
                || ip.IsIPv6Multicast
                || (b[0] & 0xFE) == 0xFC                            // unique local fc00::/7
                || (b[0] == 0x20 && b[1] == 0x02)                   // 6to4, can embed an IPv4 address
                || (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0 && b[3] == 0) // Teredo
                || (b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xFF && b[3] == 0x9B); // NAT64
        }

        return true; // unknown address family: refuse
    }
}
