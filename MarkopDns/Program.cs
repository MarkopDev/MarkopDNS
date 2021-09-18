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
            proxyServer.Start(_tokenSource.Token);

            _tokenSource.Token.WaitHandle.WaitOne();
        }

        private static void ConsoleOnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
        {
            _tokenSource?.Cancel();
        }
    }
}