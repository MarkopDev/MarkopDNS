using System.Collections.Generic;

namespace MarkopDns.Models
{
    public class ProxyServerConfig
    {
        public List<int>? Port { get; init; }
        public string? Host { get; init; }
        public int? TimeToAlive { get; init; }
    }
}