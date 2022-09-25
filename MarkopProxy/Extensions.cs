using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace MarkopProxy;

public static class Extensions
{
    public static unsafe void SetReUsePort(this Socket socket)
    {
        socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, 1);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // set SO_REUSEADDR (https://github.com/dotnet/corefx/issues/32027)
            var value = 1;
            setsockopt(socket.Handle.ToInt32(), 1, 2, &value, sizeof(int));
        }
        else
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseUnicastPort, 1);

    }

    [DllImport("libc", SetLastError = true)]
    private static extern unsafe int setsockopt(int socket, int level, int option_name, void* option_value, uint option_len);
}