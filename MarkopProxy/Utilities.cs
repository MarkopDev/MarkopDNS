using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace MarkopProxy
{
    public static class Utilities
    {
        public static byte[] GetDomainBytes(this string domain)
        {
            var segments = domain.Split(".");
            return segments.SelectMany(segment =>
                    new[] {(byte) segment.Length}.Concat(Encoding.ASCII.GetBytes(segment))).ToArray()
                .Concat(new byte[] {0x00}).ToArray();
        }
        public static Task WaitUtilDataAvailable(this NetworkStream networkStream)
        {
            return Task.Run(async () =>
            {
                while (!networkStream.DataAvailable && networkStream.CanRead && networkStream.Socket.Connected)
                    await Task.Delay(5);
            });
        }
    }
}