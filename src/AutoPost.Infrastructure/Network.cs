using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace AutoPost.Infrastructure;

public static partial class Network
{
    public static bool Public(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address)) return false;
        if (address.AddressFamily == AddressFamily.InterNetworkV6) return address.GetAddressBytes()[0] is >= 0x20 and <= 0x3f;
        var b = address.GetAddressBytes();
        return !(b[0] is 0 or 10 or 127 or >= 224 || b[0] == 169 && b[1] == 254 || b[0] == 172 && b[1] is >= 16 and <= 31 || b[0] == 192 && b[1] == 168 || b[0] == 100 && b[1] is >= 64 and <= 127 || b[0] == 198 && b[1] is 18 or 19);
    }
    public static Uri Validate(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.HostNameType == UriHostNameType.Unknown) throw new InvalidOperationException("RSS 只允许公共 HTTPS 地址和默认端口");
        if (IPAddress.TryParse(uri.Host, out var ip) && !Public(ip)) throw new InvalidOperationException("禁止内网地址");
        return uri;
    }
    public static SocketsHttpHandler SafeHandler() => new()
    {
        AllowAutoRedirect = false,
        UseProxy = false,
        ConnectCallback = async (ctx, ct) =>
        {
            var addresses = await Dns.GetHostAddressesAsync(ctx.DnsEndPoint.Host, ct);
            if (addresses.Length == 0 || addresses.Any(a => !Public(a))) throw new HttpRequestException("RSS 非公共地址");
            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            try { await socket.ConnectAsync(new IPEndPoint(addresses[0], ctx.DnsEndPoint.Port), ct); return new NetworkStream(socket, true); }
            catch { socket.Dispose(); throw; }
        }
    };
    public static string Text(string value) => WebUtility.HtmlDecode(Tags().Replace(value, " ")).Trim();
    public static string Canonical(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return "";
        var parts = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries).Where(x => !x.StartsWith("utm_", StringComparison.OrdinalIgnoreCase) && !x.StartsWith("fbclid=", StringComparison.OrdinalIgnoreCase)).Order();
        return new UriBuilder(uri) { Fragment = "", Query = string.Join('&', parts) }.Uri.AbsoluteUri.TrimEnd('/');
    }
    public static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    [GeneratedRegex("<[^>]*>")]
    private static partial Regex Tags();
}
