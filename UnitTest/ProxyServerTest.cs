using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using MarkopProxy;
using MarkopProxy.Models;
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
        public async void TestCase1_HttpV1()
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
                    Port = new List<int> {80}
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
        public async void TestCase2_Https()
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
                    Port = new List<int> {443},
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
        public async void TestCase3_HttpV2()
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
                    Port = new List<int> {1080}
                }
            };
            var cancellationToken = new CancellationTokenSource();
            ProxyServer.GetInstance(config).Start(cancellationToken.Token);

            var httpClient = new HttpClient(new SocketsHttpHandler())
            {
                DefaultRequestVersion = HttpVersion.Version20,
                DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrHigher
            };

            httpClient.DefaultRequestHeaders.Host = "http2.markop.ir";

            var httpResponseMessage = await httpClient.GetAsync("http://127.0.0.1:1080", cancellationToken.Token);

            Assert.True(httpResponseMessage.IsSuccessStatusCode);

            var response = await httpResponseMessage.Content.ReadAsStringAsync(cancellationToken.Token);

            OutputHelper.WriteLine("Response:");
            OutputHelper.WriteLine(response);

            cancellationToken.Cancel();
        }
    }
}