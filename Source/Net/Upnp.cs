using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Xml.Linq;

namespace CallOfSlop;

/// <summary>
/// Minimal UPnP Internet Gateway Device client: finds the router with SSDP, then uses SOAP to
/// open (and later close) a UDP port mapping so other people can reach the host without manual
/// port forwarding. Only uses System.Net, no extra packages.
/// </summary>
public sealed class UpnpMapping : IDisposable
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(3) };

    private readonly string _controlUrl;
    private readonly string _serviceType;
    private bool _disposed;

    public ushort Port { get; }
    public string LocalIp { get; }
    public string? ExternalIp { get; }
    public string RouterName { get; }

    private UpnpMapping(string controlUrl, string serviceType, ushort port, string localIp, string? externalIp, string routerName)
    {
        _controlUrl = controlUrl;
        _serviceType = serviceType;
        Port = port;
        LocalIp = localIp;
        ExternalIp = externalIp;
        RouterName = routerName;
    }

    /// <summary>True when the "external" address is itself private, which means a second router or carrier-grade NAT is in the way.</summary>
    public bool BehindSecondNat => ExternalIp is { } ip && IPAddress.TryParse(ip, out var a) && IsPrivate(a);

    /// <summary>Tries to map UDP <paramref name="port"/> on the router to this PC. Returns null (and logs why) on failure.</summary>
    public static UpnpMapping? TryCreate(ushort port, Action<string> log)
    {
        try
        {
            return TryCreateAsync(port, log).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            log($"UPnP: failed ({ex.Message})");
            return null;
        }
    }

    private static async Task<UpnpMapping?> TryCreateAsync(ushort port, Action<string> log)
    {
        var localIp = GetLocalIPv4();
        if (localIp == null)
        {
            log("UPnP: could not work out this PC's LAN address.");
            return null;
        }

        log("UPnP: looking for your router...");
        var locations = await DiscoverAsync(localIp, TimeSpan.FromSeconds(2.5));
        if (locations.Count == 0)
        {
            log("UPnP: no router answered. UPnP is probably turned off in your router's settings.");
            return null;
        }

        foreach (var location in locations)
        {
            var gateway = await FindWanServiceAsync(location);
            if (gateway is not { } g)
                continue;

            // Some routers refuse permanent (lease 0) mappings, others refuse timed ones. Try both.
            string? error = null;
            foreach (var lease in new[] { 0, 7200 })
            {
                error = await AddMappingAsync(g.ControlUrl, g.ServiceType, port, localIp.ToString(), lease);
                if (error == null)
                    break;
            }
            if (error != null)
            {
                log($"UPnP: {g.Name} refused the port mapping ({error}).");
                continue;
            }

            var external = await GetExternalIpAsync(g.ControlUrl, g.ServiceType);
            return new UpnpMapping(g.ControlUrl, g.ServiceType, port, localIp.ToString(), external, g.Name);
        }

        log("UPnP: found a device but it isn't an internet gateway that accepts port mappings.");
        return null;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        try
        {
            SoapAsync(
                    _controlUrl,
                    _serviceType,
                    "DeletePortMapping",
                    $"<NewRemoteHost></NewRemoteHost><NewExternalPort>{Port}</NewExternalPort><NewProtocol>UDP</NewProtocol>"
                )
                .GetAwaiter()
                .GetResult();
            Console.WriteLine($"UPnP: closed UDP port {Port} on {RouterName}.");
        }
        catch
        {
            // Router gone or mapping already expired; nothing useful to do.
        }
    }

    // ---------------------------------------------------------------------
    // SSDP discovery
    // ---------------------------------------------------------------------

    private static async Task<List<string>> DiscoverAsync(IPAddress localIp, TimeSpan window)
    {
        string[] targets =
        [
            "urn:schemas-upnp-org:device:InternetGatewayDevice:1",
            "urn:schemas-upnp-org:device:InternetGatewayDevice:2",
            "urn:schemas-upnp-org:service:WANIPConnection:1",
            "urn:schemas-upnp-org:service:WANIPConnection:2",
            "urn:schemas-upnp-org:service:WANPPPConnection:1",
        ];

        var found = new List<string>();
        using var udp = new UdpClient(new IPEndPoint(localIp, 0));
        var multicast = new IPEndPoint(new IPAddress([239, 255, 255, 250]), 1900);

        foreach (var st in targets)
        {
            var msg =
                "M-SEARCH * HTTP/1.1\r\n"
                + "HOST: 239.255.255.250:1900\r\n"
                + "MAN: \"ssdp:discover\"\r\n"
                + "MX: 2\r\n"
                + $"ST: {st}\r\n\r\n";
            var bytes = Encoding.ASCII.GetBytes(msg);
            await udp.SendAsync(bytes, bytes.Length, multicast);
        }

        using var cts = new CancellationTokenSource(window);
        try
        {
            while (true)
            {
                var result = await udp.ReceiveAsync(cts.Token);
                var text = Encoding.ASCII.GetString(result.Buffer);
                foreach (var line in text.Split("\r\n"))
                {
                    if (!line.StartsWith("LOCATION:", StringComparison.OrdinalIgnoreCase))
                        continue;
                    var url = line["LOCATION:".Length..].Trim();
                    if (!found.Contains(url))
                        found.Add(url);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Discovery window over.
        }

        return found;
    }

    private readonly record struct Gateway(string ControlUrl, string ServiceType, string Name);

    private static async Task<Gateway?> FindWanServiceAsync(string location)
    {
        try
        {
            var xml = XDocument.Parse(await Http.GetStringAsync(location));
            XNamespace ns = "urn:schemas-upnp-org:device-1-0";
            var baseUrl = xml.Root?.Element(ns + "URLBase")?.Value;
            var root = new Uri(string.IsNullOrWhiteSpace(baseUrl) ? location : baseUrl);
            var name = xml.Descendants(ns + "friendlyName").FirstOrDefault()?.Value ?? "your router";

            foreach (var service in xml.Descendants(ns + "service"))
            {
                var type = service.Element(ns + "serviceType")?.Value ?? "";
                if (!type.Contains("WANIPConnection") && !type.Contains("WANPPPConnection"))
                    continue;
                var control = service.Element(ns + "controlURL")?.Value;
                if (string.IsNullOrWhiteSpace(control))
                    continue;
                return new Gateway(new Uri(root, control).ToString(), type, name);
            }
        }
        catch
        {
            // Not a usable device description; try the next one.
        }
        return null;
    }

    // ---------------------------------------------------------------------
    // SOAP actions
    // ---------------------------------------------------------------------

    private static async Task<string?> AddMappingAsync(string controlUrl, string serviceType, ushort port, string localIp, int lease)
    {
        var (ok, body) = await SoapAsync(
            controlUrl,
            serviceType,
            "AddPortMapping",
            "<NewRemoteHost></NewRemoteHost>"
                + $"<NewExternalPort>{port}</NewExternalPort>"
                + "<NewProtocol>UDP</NewProtocol>"
                + $"<NewInternalPort>{port}</NewInternalPort>"
                + $"<NewInternalClient>{localIp}</NewInternalClient>"
                + "<NewEnabled>1</NewEnabled>"
                + "<NewPortMappingDescription>Call of Slop</NewPortMappingDescription>"
                + $"<NewLeaseDuration>{lease}</NewLeaseDuration>"
        );
        if (ok)
            return null;

        var code = TryGetTag(body, "errorCode");
        var desc = TryGetTag(body, "errorDescription");
        return code == "718"
            ? "the port is already mapped to another computer"
            : $"error {code ?? "?"} {desc}".Trim();
    }

    private static async Task<string?> GetExternalIpAsync(string controlUrl, string serviceType)
    {
        try
        {
            var (ok, body) = await SoapAsync(controlUrl, serviceType, "GetExternalIPAddress", "");
            return ok ? TryGetTag(body, "NewExternalIPAddress") : null;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<(bool Ok, string Body)> SoapAsync(string controlUrl, string serviceType, string action, string args)
    {
        var envelope =
            "<?xml version=\"1.0\"?>"
            + "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">"
            + $"<s:Body><u:{action} xmlns:u=\"{serviceType}\">{args}</u:{action}></s:Body></s:Envelope>";

        using var request = new HttpRequestMessage(HttpMethod.Post, controlUrl);
        request.Headers.TryAddWithoutValidation("SOAPAction", $"\"{serviceType}#{action}\"");
        request.Content = new StringContent(envelope, Encoding.UTF8);
        request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("text/xml; charset=\"utf-8\"");

        using var response = await Http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        return (response.IsSuccessStatusCode, body);
    }

    private static string? TryGetTag(string xml, string localName)
    {
        try
        {
            return XDocument.Parse(xml).Descendants().FirstOrDefault(e => e.Name.LocalName == localName)?.Value;
        }
        catch
        {
            return null;
        }
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    /// <summary>The LAN address Windows would use to reach the internet (no packet is actually sent).</summary>
    private static (string? Ip, long At) _localCache = (null, -1_000_000);

    /// <summary><see cref="GetLocalIPv4"/> as a string, looked up at most every 10 s (cheap enough to call every frame).</summary>
    public static string? GetLocalIPv4Cached()
    {
        var now = Environment.TickCount64;
        if (now - _localCache.At > 10_000)
            _localCache = (GetLocalIPv4()?.ToString(), now);
        return _localCache.Ip;
    }

    public static IPAddress? GetLocalIPv4()
    {
        try
        {
            using var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            s.Connect("8.8.8.8", 65530);
            return (s.LocalEndPoint as IPEndPoint)?.Address;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsPrivate(IPAddress ip)
    {
        var b = ip.GetAddressBytes();
        return b.Length == 4
            && (
                b[0] == 10
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                || (b[0] == 192 && b[1] == 168)
                || (b[0] == 100 && b[1] >= 64 && b[1] <= 127) // carrier-grade NAT
            );
    }
}
