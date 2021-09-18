namespace MarkopDns.Models
{
    public record Question(
        string Name,
        ushort Type,
        ushort Class,
        byte[] Data
    );
}