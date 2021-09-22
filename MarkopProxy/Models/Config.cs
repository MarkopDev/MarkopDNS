namespace MarkopProxy.Models
{
    public class Config
    {
        public DnsServerConfig? Dns { get; init; }
        public ProxyServerConfig? Proxy { get; init; }
    }
}