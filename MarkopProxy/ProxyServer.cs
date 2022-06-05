using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MarkopProxy.Enums;
using MarkopProxy.Exceptions;
using MarkopProxy.Models;

namespace MarkopProxy
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
                    var tcpClient = await listener.AcceptTcpClientAsync(cancellationToken);

                    void CallBack(object? param)
                    {
                        var array = param as object[];
                        var client = array?[0] as TcpClient;
                        var token = array?[1] as CancellationToken? ?? default;
                        if (client != null)
                            HandleRequest(client, token);
                    }

                    ThreadPool.QueueUserWorkItem(CallBack, new object[] {tcpClient, cancellationToken});

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
                    if (_proxyConfig.Logging ?? false)
                        Console.WriteLine(ex.ToString());
                }
            }
        }

        private async void HandleRequest(TcpClient e, CancellationToken cancellationToken)
        {
            var networkStream = e.GetStream();

            SupportedProtocol? requestProtocol = null;

            // Read 1024 byte of request to confirm the request is http
            var request = new byte[1024];
            int readBytes;
            try
            {
                readBytes = await networkStream.ReadAsync(request, cancellationToken);
            }
            catch
            {
                e.Close();
                return;
            }

            request = request.Take(readBytes).ToArray();

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
                var bytes = await networkStream.ReadAsync(data, 0, data.Length, cancellationToken);
                memoryStream.Write(data, 0, bytes);
            }

            // Collect request data
            var buffer = request.Concat(memoryStream.ToArray()).ToArray();

            try
            {
                switch (requestProtocol)
                {
                    case SupportedProtocol.Http:
                        await HandleHttpRequest(e, networkStream, buffer, cancellationToken);
                        break;
                    case SupportedProtocol.Https:
                        await HandleHttpsRequest(e, buffer, cancellationToken);
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
                if (_proxyConfig.Logging ?? false)
                    Console.WriteLine(ex);
            }
        }

        private async Task HandleHttpsRequest(TcpClient e, byte[] buffer,
            CancellationToken cancellationToken)
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

            for (var i = 0; i < extensionsLength && buffer.Length > offset;)
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
            var hostEntry = await Dns.GetHostEntryAsync(host, cancellationToken);
            var ipAddress = hostEntry.AddressList.First(address => address.AddressFamily == AddressFamily.InterNetwork);

            var clientLocalEndPoint = e.Client.LocalEndPoint ?? throw new Exception("LocalEndpoint is not available");

            // Connect to target server and pass the request data
            var tcpClient = new TcpClient(ipAddress.ToString(), ((IPEndPoint) clientLocalEndPoint).Port);

            var networkStream = e.GetStream();
            var clientReceiveStream = tcpClient.GetStream();

            // Write request data to target connection
            await clientReceiveStream.WriteAsync(buffer, cancellationToken);

            var lastConnectionDate = DateTime.UtcNow;

            void CloseConnections()
            {
                e.Close();
                tcpClient.Close();
                if (_proxyConfig.Logging ?? false)
                    Console.WriteLine("Connection Closed");
            }

            async void TargetConnection(object? _)
            {
                while (tcpClient.Connected)
                {
                    try
                    {
                        // Read response data from target connection
                        await using var memoryStreamReceive = new MemoryStream();
                        do
                        {
                            var data = new byte[2048];
                            var bytes = await clientReceiveStream.ReadAsync(data, cancellationToken);
                            if (bytes == 0)
                            {
                                CloseConnections();
                                return;
                            }

                            await memoryStreamReceive.WriteAsync(data.AsMemory(0, bytes), cancellationToken);
                        } while (clientReceiveStream.DataAvailable);

                        // Write response to client
                        var responseBuffer = memoryStreamReceive.ToArray();
                        await networkStream.WriteAsync(responseBuffer, cancellationToken);
                    }
                    catch
                    {
                        CloseConnections();
                    }

                    if (_proxyConfig.Logging ?? false)
                        Console.WriteLine("Server -> Client");
                    lastConnectionDate = DateTime.UtcNow;
                }
            }

            async void ClientConnection(object? _)
            {
                while (e.Connected)
                {
                    try
                    {
                        // Read response data from client connection
                        await using var memoryStreamReceive = new MemoryStream();
                        do
                        {
                            var data = new byte[2048];
                            var bytes = await networkStream.ReadAsync(data, cancellationToken);
                            if (bytes == 0)
                            {
                                CloseConnections();
                                return;
                            }

                            await memoryStreamReceive.WriteAsync(data.AsMemory(0, bytes), cancellationToken);
                        } while (networkStream.DataAvailable);

                        // Write response to target
                        var responseBuffer = memoryStreamReceive.ToArray();
                        await clientReceiveStream.WriteAsync(responseBuffer, cancellationToken);
                    }
                    catch
                    {
                        CloseConnections();
                    }

                    if (_proxyConfig.Logging ?? false)
                        Console.WriteLine("Client -> Server");
                    lastConnectionDate = DateTime.UtcNow;
                }
            }

            async void CheckConnection(object? _)
            {
                while (e.Connected || tcpClient.Connected)
                {
                    try
                    {
                        await Task.Delay(1000, cancellationToken);

                        await e.Client.SendAsync(Array.Empty<byte>(), SocketFlags.None);
                        await tcpClient.Client.SendAsync(Array.Empty<byte>(), SocketFlags.None);
                    }
                    catch
                    {
                        // ignored
                    }

                    if (e.Connected && tcpClient.Connected && DateTime.UtcNow.Ticks - lastConnectionDate.Ticks <
                        _proxyConfig.TimeToAlive * 10_000_000)
                        continue;

                    CloseConnections();
                }
            }

            ThreadPool.QueueUserWorkItem(CheckConnection);
            ThreadPool.QueueUserWorkItem(TargetConnection);
            ThreadPool.QueueUserWorkItem(ClientConnection);
        }

        private async Task HandleHttpRequest(TcpClient e, Stream networkStream, byte[] buffer,
            CancellationToken cancellationToken)
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
            var hostEntry = await Dns.GetHostEntryAsync(host, cancellationToken);
            var ipAddress = hostEntry.AddressList.First();

            var clientLocalEndPoint = e.Client.LocalEndPoint ?? throw new Exception("LocalEndpoint is not available");

            // Connect to target server and pass the request data
            using var tcpClient = new TcpClient(ipAddress.ToString(), ((IPEndPoint) clientLocalEndPoint).Port);

            // Write request data to target connection
            var clientReceiveStream = tcpClient.GetStream();
            await clientReceiveStream.WriteAsync(buffer, cancellationToken);

            await clientReceiveStream.WaitUtilDataAvailable();

            // Read response data from target connection
            await using var memoryStreamReceive = new MemoryStream();
            while (clientReceiveStream.DataAvailable)
            {
                var data = new byte[2048];
                var bytes = await clientReceiveStream.ReadAsync(data, cancellationToken);
                memoryStreamReceive.Write(data, 0, bytes);
            }

            // Close target connection
            tcpClient.Close();

            // Write response to client
            var responseBuffer = memoryStreamReceive.ToArray();
            await networkStream.WriteAsync(responseBuffer, cancellationToken);

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