using AtariHacker.Helpers;

namespace AtariHacker.Test;

public sealed class AtasciiDecoderTests
{
    [Fact]
    public void DecodeByte_DecodesStandardAsciiRangeCorrectly()
    {
        // 0x20 = space, 0x41 = 'A', 0x5F = '_'
        Assert.Equal(' ', AtasciiDecoder.DecodeByte(0x20));
        Assert.Equal('A', AtasciiDecoder.DecodeByte(0x41));
        Assert.Equal('_', AtasciiDecoder.DecodeByte(0x5F));
    }

    [Fact]
    public void DecodeByte_DecodesInverseVideoCorrectly()
    {
        // 0x80 + 0x41 = 0xC1 = inverse 'A'
        var result = AtasciiDecoder.DecodeByte(0xC1);
        // Inverse characters use high-bit marker
        Assert.True(result > 127);
    }

    [Fact]
    public void DecodeByte_DecodesControlCodesToLetters()
    {
        // 0x00 -> 'A' (control-A), 0x01 -> 'B', etc.
        Assert.Equal('A', AtasciiDecoder.DecodeByte(0x00));
        Assert.Equal('B', AtasciiDecoder.DecodeByte(0x01));
        Assert.Equal('Z', AtasciiDecoder.DecodeByte(0x19));
    }

    [Fact]
    public void DecodeByte_ReturnsDotForUnmappableBytes()
    {
        // 0x60-0x7F are unmappable in ATASCII
        Assert.Equal('.', AtasciiDecoder.DecodeByte(0x60));
        Assert.Equal('.', AtasciiDecoder.DecodeByte(0x7F));
    }

    [Fact]
    public void Decode_DecodesSpanOfAtasciiBytesToString()
    {
        var bytes = new byte[] { 0x48, 0x45, 0x4C, 0x4C, 0x4F }; // "HELLO"
        var result = AtasciiDecoder.Decode(bytes);
        Assert.Equal("HELLO", result);
    }

    [Fact]
    public void Decode_PrefixesInverseCharactersWithTilde()
    {
        var bytes = new byte[] { 0xC1, 0xC2 }; // inverse 'A', inverse 'B'
        var result = AtasciiDecoder.Decode(bytes);
        Assert.Equal("~A~B", result);
    }

    [Fact]
    public void Decode_HandlesEmptySpan()
    {
        var result = AtasciiDecoder.Decode(ReadOnlySpan<byte>.Empty);
        Assert.Equal("", result);
    }
}
