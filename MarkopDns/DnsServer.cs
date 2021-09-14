using System;
using System.Net;
using System.Linq;
using System.Text;
using System.Threading;
using System.Net.Sockets;
using System.Buffers.Binary;
using System.Collections.Generic;
using MarkopDns.Enums;
using MarkopDns.Models;

namespace MarkopDns
{
    public class DnsServer
    {
        private readonly Config _config;
        private readonly UdpClient _udpClient;

        private DnsServer(Config config)
        {
            _config = config;

            _udpClient = new UdpClient(new IPEndPoint(IPAddress.Parse(config.DnsServer!.Host), config.DnsServer.Port));
        }

        public static DnsServer GetInstance(Config config)
        {
            return new DnsServer(config);
        }

        public async void Start(CancellationToken? cancellationToken = null)
        {
            while (true)
            {
                try
                {
                    HandleRequest(await _udpClient.ReceiveAsync());

                    if (cancellationToken is not {IsCancellationRequested: true})
                        continue;

                    _udpClient.Close();
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

                questions.Add(new Question
                {
                    Name = name,
                    Type = type,
                    Class = queryClass,
                    Data = e.Buffer[sOffset..offset]
                });
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
                var matchedQuestion = _config.Records!.FirstOrDefault(record => record.Key == question.Name
                    && record.Value.Type == Enum.GetName((DnsType) question.Type));

                if (matchedQuestion.Equals(default(KeyValuePair<string, Record>)))
                {
                    var dnsServerProxy = new IPEndPoint(IPAddress.Parse(_config.DefaultDns!), 53);
                    using var proxyClient = new UdpClient();
                    proxyClient.Connect(dnsServerProxy);
                    await proxyClient.SendAsync(e.Buffer, e.Buffer.Length);
                    var response = await proxyClient.ReceiveAsync();

                    await _udpClient.SendAsync(response.Buffer, response.Buffer.Length, e.RemoteEndPoint);
                    return;
                }

                var typeBytes = BitConverter.GetBytes(question.Type).Reverse().ToArray();
                var classBytes = BitConverter.GetBytes(question.Class).Reverse().ToArray();

                var ttlBytes = BitConverter.GetBytes(_config.DnsServer!.Ttl).Reverse().ToArray();

                var ipBytes = IPAddress.Parse("127.0.0.1").GetAddressBytes();
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
            _udpClient.Close();
        }
    }
}