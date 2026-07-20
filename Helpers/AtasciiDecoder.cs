using System.Text;

namespace AtariHackerMCP.Helpers;

internal static class AtasciiDecoder
{
    /// <summary>Decode a byte as an ATASCII screen code.</summary>
    public static char DecodeByte(byte b)
    {
        var inverse = (b & 0x80) != 0;
        var plain = (byte)(b & 0x7F);
        if (plain is >= 0x20 and <= 0x5F)
        {
            return inverse ? (char)('A' + 128) : (char)plain; // use high-bit marker internally
        }

        if (plain <= 0x1F)
        {
            var mapped = plain switch
            {
                <= 25 => (char)('A' + plain),
                <= 35 => (char)('0' + plain - 26),
                _ => '\0'
            };
            if (mapped != '\0')
            {
                return inverse ? (char)('A' + 128) : mapped; // not perfect but works with our ~ prefix approach
            }
        }

        return '.';
    }

    /// <summary>Decode a span of bytes as ATASCII text, using ~ prefix for inverse characters.</summary>
    public static string Decode(ReadOnlySpan<byte> bytes)
    {
        var sb = new StringBuilder(bytes.Length);
        foreach (var b in bytes)
        {
            var inverse = (b & 0x80) != 0;
            var plain = (byte)(b & 0x7F);
            if (plain is >= 0x20 and <= 0x5F)
            {
                if (inverse) sb.Append('~');
                sb.Append((char)plain);
            }
            else if (plain <= 0x1F)
            {
                var mapped = plain switch
                {
                    <= 25 => (char)('A' + plain),
                    <= 35 => (char)('0' + plain - 26),
                    _ => '\0'
                };
                if (mapped != '\0')
                {
                    if (inverse) sb.Append('~');
                    sb.Append(mapped);
                }
                else
                {
                    sb.Append('.');
                }
            }
            else
            {
                sb.Append('.');
            }
        }

        return sb.ToString();
    }
}
