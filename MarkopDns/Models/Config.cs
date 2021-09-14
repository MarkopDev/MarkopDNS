using System.Collections.Generic;

namespace MarkopDns.Models
{
    public class Config
    {
        public string? DefaultDns { get; init; }
        public DnsServerConfig? DnsServer { get; init; }
        public ProxyServerConfig? ProxyServer { get; init; }
        public Dictionary<string, Record>? Records { get; init; }
    }
}