using System.Collections.Generic;

namespace MarkopProxy.Models
{
    public class ProxyServerConfig
    {
        public List<int>? Port { get; init; }
        public string? Host { get; init; }
        public int? TimeToAlive { get; init; }
        public bool? Logging { get; init; }
    }
}