using System.Net;

namespace Pos.Infrastructure;

public static class LanNetworkPolicy
{
    public const int ProtocolVersion = 2;

    public static bool IsLocalOrPrivate(IPAddress? address)
    {
        if (address is null) return false;
        if (IPAddress.IsLoopback(address)) return true;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] == 10 ||
                (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
                (bytes[0] == 192 && bytes[1] == 168);
        }

        if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6) return false;
        var ipv6 = address.GetAddressBytes();
        return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || (ipv6[0] & 0xfe) == 0xfc;
    }

    public static bool IsLoopback(IPAddress? address) => address is not null && (IPAddress.IsLoopback(address) || (address.IsIPv4MappedToIPv6 && IPAddress.IsLoopback(address.MapToIPv4())));
}
