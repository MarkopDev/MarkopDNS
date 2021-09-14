namespace MarkopDns.Models
{
    public class ProxyServerConfig
    {
        public int Port { get; init; }
        public string Host { get; init; }
        public int TimeToAlive { get; init; }
    }
}