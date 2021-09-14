using System.Linq;
using System.Text;

namespace MarkopDns
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
    }
}