using System;
using System.IO;
using System.Threading;
using MarkopDns.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace MarkopDns
{
    class Program
    {
        private static CancellationTokenSource? _tokenSource;

        static void Main(string[] args)
        {
            var config = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .Build().Deserialize<Config>(new StreamReader("config.yml"));

            var dnsServer = DnsServer.GetInstance(config);
            var proxyServer = ProxyServer.GetInstance(config);

            Console.CancelKeyPress += ConsoleOnCancelKeyPress;

            _tokenSource = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken.None);

            dnsServer.Start(_tokenSource.Token);
            Console.WriteLine("[+] Dns Server Started");

            proxyServer.Start(_tokenSource.Token);
            Console.WriteLine("[+] Proxy Server Started");

            _tokenSource.Token.WaitHandle.WaitOne();
        }

        private static void ConsoleOnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
        {
            _tokenSource?.Cancel();
        }
    }
}