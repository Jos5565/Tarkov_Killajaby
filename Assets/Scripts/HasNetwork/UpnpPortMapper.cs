using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

// 공유기(UPnP IGD)에 UDP 포트 열기/닫기를 요청한다. 외부 라이브러리 없이 표준 절차만 사용.
//   1) SSDP: 같은 네트워크에 "인터넷 공유기 있나요?"를 방송(239.255.255.250:1900) → 공유기 설명 주소(LOCATION)
//   2) 설명 XML에서 WANIPConnection / WANPPPConnection 제어 주소를 찾는다
//   3) SOAP: GetExternalIPAddress, AddPortMapping(유지 시간 지정), DeletePortMapping
// 공유기 설정은 바꾸지 않는다 (UPnP가 꺼져 있으면 찾지 못하고 끝).
// 모든 작업은 백그라운드(Task)에서 돌고, 실패하면 null/false를 돌려준다 (예외를 밖으로 던지지 않음).
public static class UpnpPortMapper
{
    public sealed class Gateway
    {
        public string controlUrl;
        public string serviceType;
        public IPAddress localAddress;   // 이 PC의 내부 IP (공유기 쪽으로 나가는 주소)
    }

    static readonly HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };

    static readonly string[] SearchTargets =
    {
        "urn:schemas-upnp-org:device:InternetGatewayDevice:1",
        "urn:schemas-upnp-org:device:InternetGatewayDevice:2",
        "urn:schemas-upnp-org:service:WANIPConnection:1",
        "urn:schemas-upnp-org:service:WANPPPConnection:1",
    };

    public static async Task<Gateway> DiscoverAsync(int timeoutMs = 2500)
    {
        try
        {
            using (var udp = new UdpClient(new IPEndPoint(IPAddress.Any, 0)))
            {
                var target = new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900);
                foreach (string st in SearchTargets)
                {
                    byte[] req = Encoding.ASCII.GetBytes(
                        "M-SEARCH * HTTP/1.1\r\nHOST: 239.255.255.250:1900\r\nMAN: \"ssdp:discover\"\r\nMX: 2\r\nST: " + st + "\r\n\r\n");
                    await udp.SendAsync(req, req.Length, target);
                }

                DateTime end = DateTime.UtcNow.AddMilliseconds(timeoutMs);
                while (true)
                {
                    TimeSpan remaining = end - DateTime.UtcNow;
                    if (remaining <= TimeSpan.Zero) break;
                    Task<UdpReceiveResult> receive = udp.ReceiveAsync();
                    Task done = await Task.WhenAny(receive, Task.Delay(remaining));
                    if (done != receive) break;
                    string response = Encoding.ASCII.GetString(receive.Result.Buffer);
                    string location = Header(response, "LOCATION");
                    if (location == null) continue;
                    Gateway g = await ReadDescriptionAsync(location);
                    if (g != null) return g;
                }
            }
        }
        catch (Exception)
        {
        }
        return null;
    }

    static string Header(string response, string name)
    {
        foreach (string line in response.Split('\n'))
        {
            int colon = line.IndexOf(':');
            if (colon > 0 && line.Substring(0, colon).Trim().Equals(name, StringComparison.OrdinalIgnoreCase))
                return line.Substring(colon + 1).Trim();
        }
        return null;
    }

    static async Task<Gateway> ReadDescriptionAsync(string location)
    {
        try
        {
            var locationUri = new Uri(location);
            // 공유기는 내부 네트워크에만 있어야 한다 (엉뚱한 응답으로 외부 주소에 요청하지 않게)
            if (!IPAddress.TryParse(locationUri.Host, out IPAddress host) || !IsPrivate(host)) return null;

            string xml = await http.GetStringAsync(locationUri);
            var doc = new XmlDocument { XmlResolver = null };
            doc.LoadXml(xml);
            var ns = new XmlNamespaceManager(doc.NameTable);
            ns.AddNamespace("d", "urn:schemas-upnp-org:device-1-0");

            string urlBase = doc.SelectSingleNode("//d:URLBase", ns)?.InnerText;
            foreach (XmlNode service in doc.SelectNodes("//d:service", ns))
            {
                string type = service.SelectSingleNode("d:serviceType", ns)?.InnerText ?? "";
                if (!type.Contains("WANIPConnection") && !type.Contains("WANPPPConnection")) continue;
                string control = service.SelectSingleNode("d:controlURL", ns)?.InnerText;
                if (string.IsNullOrEmpty(control)) continue;

                var baseUri = new Uri(string.IsNullOrEmpty(urlBase) ? locationUri.GetLeftPart(UriPartial.Authority) : urlBase);
                var controlUri = new Uri(baseUri, control);
                if (controlUri.Host != locationUri.Host) continue;

                return new Gateway
                {
                    controlUrl = controlUri.ToString(),
                    serviceType = type,
                    localAddress = LocalAddressToward(host),
                };
            }
        }
        catch (Exception)
        {
        }
        return null;
    }

    // 공유기 쪽으로 나갈 때 쓰는 이 PC의 주소 (UDP Connect는 패킷을 보내지 않는다)
    static IPAddress LocalAddressToward(IPAddress host)
    {
        using (var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
        {
            s.Connect(host, 1900);
            return ((IPEndPoint)s.LocalEndPoint).Address;
        }
    }

    public static async Task<IPAddress> GetExternalAddressAsync(Gateway g)
    {
        string xml = await SoapAsync(g, "GetExternalIPAddress", "");
        string value = Tag(xml, "NewExternalIPAddress");
        return IPAddress.TryParse(value, out IPAddress ip) ? ip : null;
    }

    // leaseSeconds 동안 유지 (0이면 영구 - 유지 시간을 지원하지 않는 공유기용). 성공하면 실제로 쓴 유지 시간, 실패하면 -1
    public static async Task<int> AddPortMappingAsync(Gateway g, int port, int leaseSeconds, string description)
    {
        foreach (int lease in new[] { leaseSeconds, 0 })
        {
            string args =
                "<NewRemoteHost></NewRemoteHost>" +
                $"<NewExternalPort>{port}</NewExternalPort>" +
                "<NewProtocol>UDP</NewProtocol>" +
                $"<NewInternalPort>{port}</NewInternalPort>" +
                $"<NewInternalClient>{g.localAddress}</NewInternalClient>" +
                "<NewEnabled>1</NewEnabled>" +
                $"<NewPortMappingDescription>{SecurityElement(description)}</NewPortMappingDescription>" +
                $"<NewLeaseDuration>{lease}</NewLeaseDuration>";
            if (await SoapAsync(g, "AddPortMapping", args) != null) return lease;
            if (lease == 0) break;
        }
        return -1;
    }

    public static async Task<bool> DeletePortMappingAsync(Gateway g, int port)
    {
        string args = $"<NewRemoteHost></NewRemoteHost><NewExternalPort>{port}</NewExternalPort><NewProtocol>UDP</NewProtocol>";
        return await SoapAsync(g, "DeletePortMapping", args) != null;
    }

    // 성공(200)이면 응답 XML, 실패하면 null
    static async Task<string> SoapAsync(Gateway g, string action, string args)
    {
        if (g == null) return null;
        try
        {
            string body =
                "<?xml version=\"1.0\"?>" +
                "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">" +
                $"<s:Body><u:{action} xmlns:u=\"{g.serviceType}\">{args}</u:{action}></s:Body></s:Envelope>";
            var request = new HttpRequestMessage(HttpMethod.Post, g.controlUrl)
            {
                Content = new StringContent(body, Encoding.UTF8, "text/xml"),
            };
            request.Headers.TryAddWithoutValidation("SOAPAction", $"\"{g.serviceType}#{action}\"");
            using (HttpResponseMessage response = await http.SendAsync(request))
                return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync() : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    static string Tag(string xml, string name)
    {
        if (xml == null) return null;
        int start = xml.IndexOf("<" + name + ">", StringComparison.Ordinal);
        if (start < 0) return null;
        start += name.Length + 2;
        int end = xml.IndexOf("</" + name + ">", start, StringComparison.Ordinal);
        return end > start ? xml.Substring(start, end - start).Trim() : null;
    }

    static string SecurityElement(string s) => System.Security.SecurityElement.Escape(s ?? "");

    // 사설 IP (10/8, 172.16/12, 192.168/16) 또는 통신사 공유 주소(100.64/10, 이중 NAT)
    public static bool IsPrivate(IPAddress ip)
    {
        byte[] b = ip.GetAddressBytes();
        if (b.Length != 4) return false;
        return b[0] == 10 ||
               (b[0] == 172 && b[1] >= 16 && b[1] <= 31) ||
               (b[0] == 192 && b[1] == 168) ||
               (b[0] == 100 && b[1] >= 64 && b[1] <= 127) ||
               b[0] == 127;
    }

    public static bool IsUsablePublic(IPAddress ip) =>
        ip != null && ip.AddressFamily == AddressFamily.InterNetwork && !IsPrivate(ip) && !ip.Equals(IPAddress.Any) &&
        ip.GetAddressBytes().Any(x => x != 0);
}
