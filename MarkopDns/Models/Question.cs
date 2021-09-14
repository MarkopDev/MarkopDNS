namespace MarkopDns.Models
{
    public class Question
    {
        public string Name { get; init; }
        public ushort Type { get; init; }
        public byte[] Data { get; init; }
        public ushort Class { get; init; }
    }
}