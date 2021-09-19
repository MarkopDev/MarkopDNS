using System;
using System.Net;
using System.Linq;
using System.Text;
using System.Threading;
using System.Net.Sockets;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Threading.Tasks;
using MarkopDns.Enums;
using MarkopDns.Models;

namespace MarkopDns
{
    public class DnsServer
    {
        private readonly UdpClient _udpClient;
        private readonly DnsServerConfig _dnsConfig;
        private CancellationTokenSource? _cancellationTokenSource;

        private DnsServer(Config config)
        {
            _dnsConfig = config.Dns ?? throw new Exception("Provide dns server config inside config.yml");

            var port = _dnsConfig.Port ?? throw new Exception("Provide server port for dns inside config.yml");
            var host = _dnsConfig.Host ?? throw new Exception("Provide server host for dns inside config.yml");

            _udpClient = new UdpClient(new IPEndPoint(IPAddress.Parse(host), port));
        }

        public static DnsServer GetInstance(Config config)
        {
            return new DnsServer(config);
        }

        public async void Start(CancellationToken? cat = null)
        {
            _cancellationTokenSource = new CancellationTokenSource();
            var cancellationToken = cat ?? _cancellationTokenSource.Token;

            var localEndPoint =
                _udpClient.Client.LocalEndPoint ?? throw new Exception("LocalEndpoint is not available");

            Console.WriteLine("[+] Dns Server listen to " +
                              $"{((IPEndPoint) localEndPoint).Address}:{((IPEndPoint) localEndPoint).Port}");
            while (true)
            {
                try
                {
                    HandleRequest(await _udpClient.ReceiveAsync(cancellationToken));

                    if (cancellationToken is {IsCancellationRequested: true})
                        throw new TaskCanceledException();
                }
                catch (TaskCanceledException)
                {
                    Stop();
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }
            }
        }

        private async void HandleRequest(UdpReceiveResult e)
        {
            // https://routley.io/posts/hand-writing-dns-messages/
            var questionCount = BinaryPrimitives.ReadUInt16BigEndian(e.Buffer[4..6]);

            var offset = 12;
            var questions = new List<Question>();
            for (var i = 0; i < questionCount; i++)
            {
                var sOffset = offset;
                var name = "";
                while (true)
                {
                    var segmentLength = e.Buffer[offset];
                    name += Encoding.ASCII.GetString(e.Buffer[offset..].Skip(1).Take(segmentLength).ToArray());
                    offset += 1 + segmentLength;

                    if (e.Buffer[offset] == 0)
                    {
                        offset += 1;
                        break;
                    }

                    name += ".";
                }

                var type = BinaryPrimitives.ReadUInt16BigEndian(e.Buffer[offset..(offset + 2)]);
                offset += 2;

                var queryClass = BinaryPrimitives.ReadUInt16BigEndian(e.Buffer[offset..(offset + 2)]);
                offset += 2;

                questions.Add(new Question(
                    name,
                    type,
                    queryClass,
                    e.Buffer[sOffset..offset]
                ));
            }

            var buffer = e.Buffer.ToArray();

            // Answer Count
            BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan()[6..8], (ushort) questions.Count);

            // Response flag
            buffer[2] = (byte) (buffer[2] | 0b1000_0000);
            // RD flag
            buffer[2] = (byte) (buffer[2] | 0b0000_0001);
            // RA flag
            buffer[3] = (byte) (buffer[3] | 0b1000_0000);

            foreach (var question in questions)
            {
                var records = _dnsConfig.Records ?? throw new Exception("Provide dns record inside config.yml");

                var matchedQuestion = records.FirstOrDefault(record =>
                    record.Key == question.Name && record.Value.Type == Enum.GetName((DnsType) question.Type));

                if (matchedQuestion.Equals(default(KeyValuePair<string, Record>)))
                {
                    var dnsServerProxy = new IPEndPoint(IPAddress.Parse(_dnsConfig.Default ?? "8.8.8.8"), 53);
                    using var proxyClient = new UdpClient();
                    proxyClient.Connect(dnsServerProxy);
                    await proxyClient.SendAsync(e.Buffer, e.Buffer.Length);
                    var response = await proxyClient.ReceiveAsync();

                    await _udpClient.SendAsync(response.Buffer, response.Buffer.Length, e.RemoteEndPoint);
                    return;
                }

                var typeBytes = BitConverter.GetBytes(question.Type).Reverse().ToArray();
                var classBytes = BitConverter.GetBytes(question.Class).Reverse().ToArray();

                var ttlBytes = BitConverter.GetBytes(_dnsConfig.Ttl ?? 150).Reverse().ToArray();

                var address = matchedQuestion.Value.Address ??
                              throw new Exception($"Provide address for {matchedQuestion.Key}");
                var ipBytes = IPAddress.Parse(address).GetAddressBytes();
                var dataLengthBytes = BitConverter.GetBytes((ushort) ipBytes.Length).Reverse().ToArray();

                var answersBytes = new byte[]
                {
                    // Point to name
                    0xC0, 0x0C
                };
                answersBytes = answersBytes
                    // Type
                    .Concat(typeBytes)
                    // Class
                    .Concat(classBytes)
                    // TTL
                    .Concat(ttlBytes)
                    // Data Length
                    .Concat(dataLengthBytes)
                    // IP
                    .Concat(ipBytes)
                    .ToArray();

                buffer = buffer.Concat(answersBytes).ToArray();
            }

            await _udpClient.SendAsync(buffer, buffer.Length, e.RemoteEndPoint);
        }

        public void Stop()
        {
            _cancellationTokenSource?.Cancel();
            _udpClient.Close();
        }
    }
}