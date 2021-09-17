using System.Net;
using System.Net.Http;
using System.Threading;
using MarkopDns;
using MarkopDns.Models;
using Xunit;
using Xunit.Abstractions;

namespace UnitTest
{
    public class ProxyServerTest
    {
        private ITestOutputHelper OutputHelper { get; }

        public ProxyServerTest(ITestOutputHelper outputHelper)
        {
            OutputHelper = outputHelper;
        }

        [Fact]
        async public void TestCase1_HttpV1()
        {
            var config = new Config
            {
                Dns = new DnsServerConfig
                {
                    Default = "8.8.8.8",
                },
                Proxy = new ProxyServerConfig
                {
                    Host = "127.0.0.1",
                    Port = 80,
                    TimeToAlive = 2
                }
            };
            var cancellationToken = new CancellationTokenSource();
            ProxyServer.GetInstance(config).Start(cancellationToken.Token);

            var httpClient = new HttpClient();

            httpClient.DefaultRequestHeaders.Host = "example.com";

            var httpResponseMessage = await httpClient.GetAsync("http://127.0.0.1/", cancellationToken.Token);

            Assert.True(httpResponseMessage.IsSuccessStatusCode);

            var response = await httpResponseMessage.Content.ReadAsStringAsync(cancellationToken.Token);

            OutputHelper.WriteLine("Response:");
            OutputHelper.WriteLine(response);

            cancellationToken.Cancel();
        }

        [Fact]
        async public void TestCase2_Https()
        {
            var config = new Config
            {
                Dns = new DnsServerConfig
                {
                    Default = "8.8.8.8",
                },
                Proxy = new ProxyServerConfig
                {
                    Host = "127.0.0.1",
                    Port = 443,
                    TimeToAlive = 2
                }
            };
            var cancellationToken = new CancellationTokenSource();
            ProxyServer.GetInstance(config).Start(cancellationToken.Token);

            var httpClient = new HttpClient();

            httpClient.DefaultRequestHeaders.Host = "example.com";

            var httpResponseMessage = await httpClient.GetAsync("https://127.0.0.1/", cancellationToken.Token);

            Assert.True(httpResponseMessage.IsSuccessStatusCode);

            var response = await httpResponseMessage.Content.ReadAsStringAsync(cancellationToken.Token);

            OutputHelper.WriteLine("Response:");
            OutputHelper.WriteLine(response);

            cancellationToken.Cancel();
        }

        [Fact]
        async public void TestCase3_HttpV2()
        {
            var config = new Config
            {
                Dns = new DnsServerConfig
                {
                    Default = "8.8.8.8",
                },
                Proxy = new ProxyServerConfig
                {
                    Host = "127.0.0.1",
                    Port = 80,
                    TimeToAlive = 2
                }
            };
            var cancellationToken = new CancellationTokenSource();
            ProxyServer.GetInstance(config).Start(cancellationToken.Token);

            var httpClient = new HttpClient(new SocketsHttpHandler())
            {
                DefaultRequestVersion = HttpVersion.Version20,
                DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrHigher
            };

            httpClient.DefaultRequestHeaders.Host = "ce.markop.ir";

            var httpResponseMessage = await httpClient.GetAsync("http://127.0.0.1:80", cancellationToken.Token);

            Assert.True(httpResponseMessage.IsSuccessStatusCode);

            var response = await httpResponseMessage.Content.ReadAsStringAsync(cancellationToken.Token);

            OutputHelper.WriteLine("Response:");
            OutputHelper.WriteLine(response);

            cancellationToken.Cancel();
        }
    }
}