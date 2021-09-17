using System.Collections.Generic;
using System.Threading;
using MarkopDns;
using MarkopDns.Models;
using Xunit;
using Record = MarkopDns.Models.Record;

namespace UnitTest
{
    public class DnsServerTest
    {
        [Fact]
        async public void TestCase1()
        {
            var config = new Config
            {
                Dns = new DnsServerConfig
                {
                    Default = "8.8.8.8",
                    Host = "127.0.0.1",
                    Port = 8080,
                    Records = new Dictionary<string, Record>
                    {
                        {
                            "example.com", new Record
                            {
                                Address = "127.0.0.1",
                                Type = "A"
                            }
                        }
                    }
                }
            };
            var cancellationToken = new CancellationTokenSource();
            DnsServer.GetInstance(config).Start(cancellationToken.Token);


            // TODO


            cancellationToken.Cancel();
        }
    }
}