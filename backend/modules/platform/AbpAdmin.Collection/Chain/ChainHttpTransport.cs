using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.Http;

namespace AbpAdmin.Collection.Chain;

/// <summary>Restricts chain requests to direct HTTPS connections to public addresses.</summary>
public static class ChainHttpTransport
{
    /// <summary>Checks endpoint syntax and IP literals without requiring the provider to be online.</summary>
    internal static bool IsAllowedEndpoint(string endpoint) =>
        Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) && IsAllowedEndpoint(uri);

    private static bool IsAllowedEndpoint(Uri uri)
    {
        if (uri.Scheme != Uri.UriSchemeHttps || uri.HostNameType == UriHostNameType.Unknown
            || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }

        var host = uri.IdnHost.TrimEnd('.');
        return !host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            && !host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
            && (!IPAddress.TryParse(host, out var address) || !IsBlockedAddress(address));
    }

    /// <summary>Pins a checked address at connect time, with the original host retained for TLS.</summary>
    public static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false,
        UseProxy = false,
        UseCookies = false,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        ConnectCallback = (context, cancellationToken) =>
        {
            if (context.InitialRequestMessage.RequestUri is not { IsAbsoluteUri: true } uri || !IsAllowedEndpoint(uri))
            {
                throw new HttpRequestException("The chain endpoint must use HTTPS and a public address.");
            }

            return ConnectToPublicAddressAsync(context.DnsEndPoint, Dns.GetHostAddressesAsync, ConnectSocketAsync, cancellationToken);
        },
    };

    internal static async ValueTask<Stream> ConnectToPublicAddressAsync(DnsEndPoint endpoint,
        Func<string, CancellationToken, Task<IPAddress[]>> resolve,
        Func<IPEndPoint, CancellationToken, ValueTask<Stream>> connect,
        CancellationToken cancellationToken)
    {
        var addresses = IPAddress.TryParse(endpoint.Host, out var literal)
            ? new[] { literal }
            : await resolve(endpoint.Host, cancellationToken);
        if (addresses.Length == 0 || addresses.Any(IsBlockedAddress))
        {
            throw new HttpRequestException("The chain endpoint did not resolve exclusively to public addresses.");
        }

        SocketException? failure = null;
        foreach (var address in addresses)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await connect(new IPEndPoint(address, endpoint.Port), cancellationToken);
            }
            catch (SocketException exception)
            {
                failure = exception;
            }
        }

        throw new HttpRequestException("Unable to connect to the public chain endpoint.", failure);
    }

    private static bool IsBlockedAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        // IPv6 outside the global unicast range and IPv4 transition tunnels can carry private routes.
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var bytes = address.GetAddressBytes();
            if ((bytes[0] & 0xe0) != 0x20 || (bytes[0] == 0x20 && bytes[1] == 0x02)
                || (bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0 && bytes[3] == 0))
            {
                return true;
            }
        }

        return SafeHttpUrl.IsBlockedAddress(address);
    }

    private static async ValueTask<Stream> ConnectSocketAsync(IPEndPoint endpoint, CancellationToken cancellationToken)
    {
        var socket = new Socket(endpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(endpoint, cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
