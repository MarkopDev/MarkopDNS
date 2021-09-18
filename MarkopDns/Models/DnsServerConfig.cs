using System.Collections.Generic;

namespace MarkopDns.Models
{
    public class DnsServerConfig
    {
        public int? Ttl { get; init; }
        public int? Port { get; init; }
        public string? Host { get; init; }
        public string? Default { get; init; }
        public Dictionary<string, Record>? Records { get; init; }
    }
}