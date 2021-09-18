using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Linq;
using System.Text;
using System.Threading;
using System.Net.Sockets;
using System.Threading.Tasks;
using MarkopDns.Enums;
using MarkopDns.Exceptions;
using MarkopDns.Models;

namespace MarkopDns
{
    public class ProxyServer
    {
        private readonly ProxyServerConfig _proxyConfig;
        private readonly IEnumerable<TcpListener> _tcpListeners;
        private CancellationTokenSource? _cancellationTokenSource;

        private ProxyServer(Config config)
        {
            _proxyConfig = config.Proxy ?? throw new Exception("Provide proxy server config inside config.yml");

            var ports = _proxyConfig.Port ?? throw new Exception("Provide server port for proxy inside config.yml");
            var host = _proxyConfig.Host ?? throw new Exception("Provide server host for proxy inside config.yml");

            _tcpListeners = ports.Select(port =>
                new TcpListener(new IPEndPoint(IPAddress.Parse(host), port)));
        }

        public static ProxyServer GetInstance(Config config)
        {
            return new ProxyServer(config);
        }

        public void Start(CancellationToken? cat = null)
        {
            _cancellationTokenSource = new CancellationTokenSource();
            var cancellationToken = cat ?? _cancellationTokenSource.Token;
            foreach (var listener in _tcpListeners)
                Start(listener, cancellationToken);
        }

        private async void Start(TcpListener listener, CancellationToken cancellationToken)
        {
            listener.Start();
            var listenerLocalEndpoint = (IPEndPoint) listener.LocalEndpoint;
            Console.WriteLine("[+] Proxy Server listen to " +
                              $"{listenerLocalEndpoint.Address}:{listenerLocalEndpoint.Port}");
            while (true)
            {
                try
                {
                    HandleRequest(await listener.AcceptTcpClientAsync(cancellationToken));

                    if (cancellationToken is not {IsCancellationRequested: true})
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

        private async void HandleRequest(TcpClient e)
        {
            var networkStream = e.GetStream();

            SupportedProtocol? requestProtocol = null;

            // Read 1024 byte of request to confirm the request is http
            var request = new byte[1024];
            request = request.Take(await networkStream.ReadAsync(request)).ToArray();

            // handshake content type
            if (request[0] == 0x16
                // lower than major of last protocol version
                && request[1] < 4
                // lower than minor of last protocol version
                && request[2] < 4
                // Client hello handshake
                && request[5] == 1)
                requestProtocol = SupportedProtocol.Https;

            if (requestProtocol == null)
            {
                // Get http version from request
                var httpVersion = request.SkipWhile(b => b != 32).Skip(1)
                    .SkipWhile(b => b != 32).Skip(1)
                    .TakeWhile(b => b != 13).ToArray();

                // Check http version is correct
                if (Encoding.ASCII.GetString(httpVersion).StartsWith("HTTP/"))
                    requestProtocol = SupportedProtocol.Http;
            }

            if (requestProtocol == null)
            {
                e.Close();
                return;
            }

            // Read reminded request data
            await using var memoryStream = new MemoryStream();
            while (networkStream.DataAvailable)
            {
                var data = new byte[2048];
                var bytes = networkStream.Read(data, 0, data.Length);
                memoryStream.Write(data, 0, bytes);
            }

            // Collect request data
            var buffer = request.Concat(memoryStream.ToArray()).ToArray();

            try
            {
                switch (requestProtocol)
                {
                    case SupportedProtocol.Http:
                        await HandleHttpRequest(e, networkStream, buffer);
                        break;
                    case SupportedProtocol.Https:
                        await HandleHttpsRequest(e, networkStream, buffer);
                        break;
                    default:
                        e.Close();
                        return;
                }
            }
            catch (UnsupportedProtocol)
            {
                e.Close();
            }
            catch (Exception ex)
            {
                e.Close();
                Console.WriteLine(ex);
            }
        }

        private async Task HandleHttpsRequest(TcpClient e, NetworkStream networkStream, byte[] buffer)
        {
            // https://datatracker.ietf.org/doc/html/rfc5246
            var offset = /*Handshake type offset*/6 + /*Handshake length*/3 + /*Protocol Version*/2 + /*Random*/32;

            var sessionIdLength = buffer[offset];

            offset += /*SessionID Length*/1 + sessionIdLength;

            var cipherSuitesLength = BinaryPrimitives.ReadUInt16BigEndian(buffer[offset..(offset + 2)]);

            offset += /*Cipher Suites Length*/2 + cipherSuitesLength;

            var compressionMethodLength = buffer[offset];

            offset += /*Compression Method Length*/1 + compressionMethodLength;

            var extensionsLength = BinaryPrimitives.ReadUInt16BigEndian(buffer[offset..(offset + 2)]);

            offset += /*Extensions Length*/2;

            string? host = null;

            for (var i = 0; i < extensionsLength && buffer.Length > offset; i++)
            {
                var extensionType = BinaryPrimitives.ReadUInt16BigEndian(buffer[(offset + i)..(offset + i + 2)]);
                var extensionDataLength =
                    BinaryPrimitives.ReadUInt16BigEndian(buffer[(offset + i + 2)..(offset + i + 4)]);

                // Server name extension type
                if (extensionType == 0)
                {
                    const int serverNameOffsetSection = 2
                                                        + /*Server Name Type*/1
                                                        + /*Server Name Length*/2;
                    var serverNameLength = extensionDataLength - serverNameOffsetSection;
                    var serverNameOffset = offset + i + /*Extension Type*/2 + /*Extension Data Length*/2 +
                                           serverNameOffsetSection;
                    host = Encoding.ASCII.GetString(buffer[serverNameOffset..(serverNameOffset + serverNameLength)]);
                    break;
                }

                i += /*Extension Type*/2 + /*Extension Data Length*/2 + extensionDataLength;
            }

            if (host == null)
                throw new UnsupportedProtocol();

            // Resolve hostname
            var hostEntry = await Dns.GetHostEntryAsync(host);
            var ipAddress = hostEntry.AddressList.First();

            var clientLocalEndPoint = e.Client.LocalEndPoint ?? throw new Exception("LocalEndpoint is not available");

            // Connect to target server and pass the request data
            using var tcpClient = new TcpClient(ipAddress.ToString(), ((IPEndPoint) clientLocalEndPoint).Port);

            // Write request data to target connection
            var clientReceiveStream = tcpClient.GetStream();
            clientReceiveStream.Write(buffer, 0, buffer.Length);

            await clientReceiveStream.WaitUtilDataAvailable();

            var now = DateTime.UtcNow.Ticks;

            while (tcpClient.Connected)
            {
                if (clientReceiveStream.DataAvailable)
                {
                    // Read response data from target connection
                    await using var memoryStreamReceive = new MemoryStream();
                    while (clientReceiveStream.DataAvailable)
                    {
                        var data = new byte[2048];
                        var bytes = clientReceiveStream.Read(data, 0, data.Length);
                        memoryStreamReceive.Write(data, 0, bytes);
                    }

                    // Write response to client
                    var responseBuffer = memoryStreamReceive.ToArray();
                    await networkStream.WriteAsync(responseBuffer);

                    now = DateTime.UtcNow.Ticks;

                    Console.WriteLine("Server -> Client");

                    continue;
                }

                if (networkStream.DataAvailable)
                {
                    // Read response data from client connection
                    await using var memoryStreamReceive = new MemoryStream();
                    while (networkStream.DataAvailable)
                    {
                        var data = new byte[2048];
                        var bytes = networkStream.Read(data, 0, data.Length);
                        memoryStreamReceive.Write(data, 0, bytes);
                    }

                    // Write response to target
                    var responseBuffer = memoryStreamReceive.ToArray();
                    await clientReceiveStream.WriteAsync(responseBuffer);

                    now = DateTime.UtcNow.Ticks;

                    Console.WriteLine("Client -> Server");

                    continue;
                }

                if (DateTime.UtcNow.Ticks - now > _proxyConfig.TimeToAlive * 10_000_000)
                    try
                    {
                        await networkStream.WriteAsync(new byte[] {0});
                        await clientReceiveStream.WriteAsync(new byte[] {0});
                    }
                    catch
                    {
                        e.Close();
                        tcpClient.Close();

                        Console.WriteLine("Connection Closed");
                    }
            }
        }

        private async Task HandleHttpRequest(TcpClient e, Stream networkStream, byte[] buffer)
        {
            // Find host header
            var bufferLength = buffer.Length;
            var hostHeader = buffer.SkipWhile((b, i) => !(b == 72 // H
                                                          && bufferLength > i + 4
                                                          && buffer[i + 1] == 111 // o
                                                          && buffer[i + 2] == 115 // s
                                                          && buffer[i + 3] == 116 // t
                                                          && buffer[i + 4] == 58 /* : */))
                .TakeWhile(b => b != 13);

            // Get Host value
            var host = Encoding.ASCII.GetString(hostHeader.Skip(6).ToArray());

            // Resolve hostname
            var hostEntry = await Dns.GetHostEntryAsync(host);
            var ipAddress = hostEntry.AddressList.First();

            var clientLocalEndPoint = e.Client.LocalEndPoint ?? throw new Exception("LocalEndpoint is not available");

            // Connect to target server and pass the request data
            using var tcpClient = new TcpClient(ipAddress.ToString(), ((IPEndPoint) clientLocalEndPoint).Port);

            // Write request data to target connection
            var clientReceiveStream = tcpClient.GetStream();
            clientReceiveStream.Write(buffer, 0, buffer.Length);

            await clientReceiveStream.WaitUtilDataAvailable();

            // Read response data from target connection
            await using var memoryStreamReceive = new MemoryStream();
            while (clientReceiveStream.DataAvailable)
            {
                var data = new byte[2048];
                var bytes = clientReceiveStream.Read(data, 0, data.Length);
                memoryStreamReceive.Write(data, 0, bytes);
            }

            // Close target connection
            tcpClient.Close();

            // Write response to client
            var responseBuffer = memoryStreamReceive.ToArray();
            await networkStream.WriteAsync(responseBuffer);

            // Close client connection
            e.Close();
        }

        public void Stop()
        {
            _cancellationTokenSource?.Cancel();

            foreach (var listener in _tcpListeners)
                listener.Stop();
        }
    }
}