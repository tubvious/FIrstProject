using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Chess.Web.Configuration;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Options;

namespace Chess.Web.Infrastructure;

/// <summary>The address a friend should use to open a game, and whether it only works on this computer.</summary>
public sealed record ShareBase(string BaseUrl, bool IsLocalOnly);

/// <summary>
/// Works out the base URL for shareable game links. A link built from "localhost" is useless to a
/// friend on another computer, so when the host is browsing via localhost we substitute this machine's
/// network address, provided the server is listening on the network.
/// </summary>
public sealed class ShareLinkProvider(IServer server, IOptions<GameOptions> options)
{
    public ShareBase GetShareBase(HttpRequest request)
    {
        if (!string.IsNullOrWhiteSpace(options.Value.PublicBaseUrl))
        {
            return new ShareBase(options.Value.PublicBaseUrl.TrimEnd('/'), IsLocalOnly: false);
        }

        var requestOrigin = $"{request.Scheme}://{request.Host}";
        if (!IsLoopback(request.Host.Host))
        {
            // Already browsing through a real address (LAN IP, dev tunnel, deployed site).
            return new ShareBase(requestOrigin, IsLocalOnly: false);
        }

        if (FindNetworkListener() is { } listener && FindLanAddress() is { } lanAddress)
        {
            return new ShareBase($"{listener.Scheme}://{lanAddress}:{listener.Port}", IsLocalOnly: false);
        }

        return new ShareBase(requestOrigin, IsLocalOnly: true);
    }

    /// <summary>A server address bound to all interfaces (e.g. http://*:5101), preferring plain HTTP.</summary>
    private Uri? FindNetworkListener()
    {
        var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses ?? [];
        return addresses
            .Select(address => address.Replace("://*", "://0.0.0.0", StringComparison.Ordinal)
                .Replace("://+", "://0.0.0.0", StringComparison.Ordinal))
            .Select(address => Uri.TryCreate(address, UriKind.Absolute, out var uri) ? uri : null)
            .OfType<Uri>()
            .Where(uri => uri.Host is "0.0.0.0" or "[::]" || (IPAddress.TryParse(uri.Host, out var ip) && !IPAddress.IsLoopback(ip)))
            .OrderBy(uri => uri.Scheme == Uri.UriSchemeHttp ? 0 : 1)
            .FirstOrDefault();
    }

    /// <summary>This machine's IPv4 address on the local network (private ranges preferred).</summary>
    private static string? FindLanAddress()
    {
        var candidates = NetworkInterface.GetAllNetworkInterfaces()
            .Where(nic => nic.OperationalStatus == OperationalStatus.Up &&
                          nic.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
            .SelectMany(nic => nic.GetIPProperties().UnicastAddresses)
            .Select(unicast => unicast.Address)
            .Where(ip => ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip) && !IsLinkLocal(ip))
            .ToList();

        return (candidates.FirstOrDefault(IsPrivate) ?? candidates.FirstOrDefault())?.ToString();
    }

    private static bool IsLoopback(string host) =>
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
        (IPAddress.TryParse(host.Trim('[', ']'), out var ip) && IPAddress.IsLoopback(ip));

    private static bool IsLinkLocal(IPAddress ip) => ip.GetAddressBytes() is [169, 254, ..];

    private static bool IsPrivate(IPAddress ip) => ip.GetAddressBytes() switch
    {
        [10, ..] => true,
        [172, >= 16 and <= 31, ..] => true,
        [192, 168, ..] => true,
        _ => false,
    };
}
