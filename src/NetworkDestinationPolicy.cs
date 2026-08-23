using System.Net;
using System.Net.Sockets;

namespace DotnetInspector.Networking;

internal static class NetworkDestinationPolicy
{
    private static readonly IPAddress PcpAnycastV6 =
        IPAddress.Parse("2001:1::1");
    private static readonly IPAddress TurnAnycastV6 =
        IPAddress.Parse("2001:1::2");
    private static readonly IPAddress DnssdAnycastV6 =
        IPAddress.Parse("2001:1::3");

    internal static async ValueTask<Stream> ConnectAsync(
        SocketsHttpConnectionContext context,
        string? trustedHost,
        int? trustedPort,
        CancellationToken cancellationToken)
    {
        DnsEndPoint endpoint = context.DnsEndPoint;
        IPAddress[] addresses = await Dns.GetHostAddressesAsync(
            endpoint.Host,
            cancellationToken).ConfigureAwait(false);
        if (addresses.Length == 0)
        {
            throw new HttpRequestException(
                $"Could not resolve host: {endpoint.Host}");
        }

        bool isTrustedOrigin = trustedHost is not null
            && endpoint.Port == trustedPort
            && endpoint.Host.Equals(
                trustedHost,
                StringComparison.OrdinalIgnoreCase);
        if (!isTrustedOrigin)
        {
            foreach (IPAddress address in addresses)
            {
                if (IsNonPublic(address))
                {
                    throw new HttpRequestException(
                        $"Blocked request to non-public address: {endpoint.Host} resolves to {address}");
                }
            }
        }

        var socket = new Socket(
            SocketType.Stream,
            ProtocolType.Tcp)
        {
            NoDelay = true,
        };
        try
        {
            await socket.ConnectAsync(
                addresses,
                endpoint.Port,
                cancellationToken).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Returns true for destinations that are not globally reachable, including
    /// embedded non-public IPv4 destinations in IPv6 translation and transition
    /// addresses.
    /// </summary>
    /// <remarks>
    /// <c>HttpClientFactoryTests.UntrustedFetchAddressClassification_MatchesNonPublicContract</c>
    /// gates the shared classification, while
    /// <c>PackageSourceClientTests.DefaultV3TransportBlocksPrivateCrossOriginSearchEndpoint</c>
    /// gates the NuGetFetch transport wiring.
    /// </remarks>
    internal static bool IsNonPublic(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip)
            || ip.Equals(IPAddress.Any)
            || ip.Equals(IPAddress.IPv6Any))
        {
            return true;
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv4MappedToIPv6)
                return IsNonPublic(ip.MapToIPv4());

            if (ip.IsIPv6LinkLocal
                || ip.IsIPv6SiteLocal
                || ip.IsIPv6Multicast
                || ip.IsIPv6UniqueLocal)
            {
                return true;
            }

            byte[] address = ip.GetAddressBytes();

            if (HasPrefix(
                    address,
                    [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
                    96))
            {
                return true;
            }

            if ((address[8] is 0x00 or 0x02)
                && address[9] == 0x00
                && address[10] == 0x5e
                && address[11] == 0xfe
                && IsNonPublic(new IPAddress(address.AsSpan(12, 4))))
            {
                return true;
            }

            if (HasPrefix(
                    address,
                    [0x00, 0x64, 0xff, 0x9b, 0, 0, 0, 0, 0, 0, 0, 0],
                    96))
            {
                return IsNonPublic(
                    new IPAddress(address.AsSpan(12, 4)));
            }

            if (HasPrefix(
                    address,
                    [0x00, 0x64, 0xff, 0x9b, 0x00, 0x01],
                    48)
                || HasPrefix(
                    address,
                    [0x01, 0x00, 0, 0, 0, 0, 0, 0],
                    64)
                || HasPrefix(
                    address,
                    [0x01, 0x00, 0, 0, 0, 0, 0, 0x01],
                    64))
            {
                return true;
            }

            if (HasPrefix(address, [0x20, 0x01, 0x00], 23))
            {
                bool globallyReachable =
                    ip.Equals(PcpAnycastV6)
                    || ip.Equals(TurnAnycastV6)
                    || ip.Equals(DnssdAnycastV6)
                    || HasPrefix(
                        address,
                        [0x20, 0x01, 0x00, 0x03],
                        32)
                    || HasPrefix(
                        address,
                        [0x20, 0x01, 0x00, 0x04, 0x01, 0x12],
                        48)
                    || HasPrefix(
                        address,
                        [0x20, 0x01, 0x00, 0x20],
                        28)
                    || HasPrefix(
                        address,
                        [0x20, 0x01, 0x00, 0x30],
                        28);
                return !globallyReachable;
            }

            if (HasPrefix(
                    address,
                    [0x20, 0x01, 0x0d, 0xb8],
                    32)
                || HasPrefix(
                    address,
                    [0x3f, 0xff, 0x00],
                    20)
                || HasPrefix(
                    address,
                    [0x5f, 0x00],
                    16))
            {
                return true;
            }

            if (HasPrefix(address, [0x20, 0x02], 16))
            {
                return IsNonPublic(
                    new IPAddress(address.AsSpan(2, 4)));
            }

            return !HasPrefix(address, [0x20], 3);
        }

        byte[] bytes = ip.GetAddressBytes();
        return bytes[0] switch
        {
            0 => true,
            10 => true,
            127 => true,
            169 when bytes[1] == 254 => true,
            172 when bytes[1] >= 16 && bytes[1] <= 31 => true,
            192 when bytes[1] == 0
                && bytes[2] == 0
                && bytes[3] is not (9 or 10) => true,
            192 when bytes[1] == 0 && bytes[2] == 2 => true,
            192 when bytes[1] == 88 && bytes[2] == 99 => true,
            192 when bytes[1] == 168 => true,
            100 when bytes[1] >= 64 && bytes[1] <= 127 => true,
            198 when bytes[1] is 18 or 19 => true,
            198 when bytes[1] == 51 && bytes[2] == 100 => true,
            203 when bytes[1] == 0 && bytes[2] == 113 => true,
            >= 224 => true,
            _ => false,
        };
    }

    private static bool HasPrefix(
        ReadOnlySpan<byte> address,
        ReadOnlySpan<byte> prefix,
        int prefixLength)
    {
        int wholeBytes = prefixLength / 8;
        if (!address[..wholeBytes].SequenceEqual(
                prefix[..wholeBytes]))
        {
            return false;
        }

        int remainingBits = prefixLength % 8;
        if (remainingBits == 0)
            return true;

        byte mask = (byte)(0xff << (8 - remainingBits));
        return (address[wholeBytes] & mask)
            == (prefix[wholeBytes] & mask);
    }
}
