namespace MarkopDns.Models
{
    public class DnsServerConfig
    {
        public int Ttl { get; init; }
        public int Port { get; init; }
        public string Host { get; init; }
    }
}