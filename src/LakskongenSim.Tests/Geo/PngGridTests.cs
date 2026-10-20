#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using LakskongenSim.Environment.Geo;
using Xunit;

namespace LakskongenSim.Tests.Geo;

/// <summary>
/// The data-PNG decoder (STAGE23_PLAN track A, A1): synthetic grey and RGB
/// images round-trip under each of the five filters (one filter for the whole
/// image, and all five mixed row by row, over several IDAT chunks), and every
/// unsupported or broken file throws an error naming the field.
/// </summary>
public class PngGridTests
{
    // ── A minimal PNG encoder (test only) ─────────────────────────────────

    private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(n =>
    {
        uint c = (uint)n;
        for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    private static uint Crc(byte[] bytes)
    {
        uint c = 0xFFFFFFFFu;
        foreach (byte b in bytes) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }

    private static void BigEndian(Stream s, uint v)
    {
        s.WriteByte((byte)(v >> 24));
        s.WriteByte((byte)(v >> 16));
        s.WriteByte((byte)(v >> 8));
        s.WriteByte((byte)v);
    }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        BigEndian(s, (uint)data.Length);
        byte[] typed = Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
        s.Write(typed);
        BigEndian(s, Crc(typed));
    }

    private static byte[] Header(int width, int height, byte bitDepth = 8, byte colourType = 2, byte interlace = 0)
    {
        using var ms = new MemoryStream();
        BigEndian(ms, (uint)width);
        BigEndian(ms, (uint)height);
        ms.Write(new byte[] { bitDepth, colourType, 0, 0, interlace });
        return ms.ToArray();
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    /// <summary>Filter each row with its filter type (PNG spec §9), forward.</summary>
    private static byte[] Filtered(byte[][] rows, int channels, Func<int, byte> filterOfRow)
    {
        using var ms = new MemoryStream();
        byte[] prev = new byte[rows[0].Length];
        for (int r = 0; r < rows.Length; r++)
        {
            byte f = filterOfRow(r);
            ms.WriteByte(f);
            var row = rows[r];
            for (int i = 0; i < row.Length; i++)
            {
                int left = i >= channels ? row[i - channels] : 0;
                int up = prev[i];
                int ul = i >= channels ? prev[i - channels] : 0;
                int predictor = f switch { 0 => 0, 1 => left, 2 => up, 3 => (left + up) / 2, 4 => Paeth(left, up, ul), _ => 0 };
                ms.WriteByte((byte)(row[i] - predictor));
            }
            prev = row;
        }
        return ms.ToArray();
    }

    private static byte[] Encode(byte[][] rows, int width, byte colourType, Func<int, byte> filterOfRow,
        int idatChunks = 1, byte bitDepth = 8, byte interlace = 0, byte[]? rawOverride = null)
    {
        int channels = colourType == 2 ? 3 : 1;
        byte[] raw = rawOverride ?? Filtered(rows, channels, filterOfRow);
        using var deflated = new MemoryStream();
        using (var z = new ZLibStream(deflated, CompressionLevel.Optimal, leaveOpen: true))
            z.Write(raw);
        byte[] zdata = deflated.ToArray();

        using var png = new MemoryStream();
        png.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        Chunk(png, "IHDR", Header(width, rows.Length, bitDepth, colourType, interlace));
        Chunk(png, "tEXt", Encoding.ASCII.GetBytes("Comment\0synthetic"));
        int size = (zdata.Length + idatChunks - 1) / idatChunks;
        for (int i = 0; i < zdata.Length; i += size)
            Chunk(png, "IDAT", zdata.Skip(i).Take(size).ToArray());
        Chunk(png, "IEND", Array.Empty<byte>());
        return png.ToArray();
    }

    /// <summary>Deterministic pixels with gradients, edges and wrap-around values.</summary>
    private static byte[][] Pixels(int width, int height, int channels) =>
        Enumerable.Range(0, height).Select(r => Enumerable.Range(0, width * channels)
            .Select(i => (byte)((r * 37 + i * 11 + (i % channels) * 101 + (r * i) % 7 * 40) & 0xFF)).ToArray()).ToArray();

    private static void AssertDecodes(byte[][] rows, int width, int channels, byte[] png)
    {
        var grid = PngGrid.Decode(png);
        Assert.Equal((width, rows.Length, channels), (grid.Width, grid.Height, grid.Channels));
        for (int r = 0; r < rows.Length; r++)
        {
            Assert.Equal(rows[r], grid.Row(r).ToArray());
            for (int c = 0; c < width; c++)
                for (int ch = 0; ch < channels; ch++)
                    Assert.Equal(rows[r][c * channels + ch], grid.Sample(c, r, ch));
        }
    }

    // ── Round trips ───────────────────────────────────────────────────────

    public static IEnumerable<object[]> FiltersAndColourTypes() =>
        from colourType in new byte[] { 0, 2 }
        from filter in new byte[] { 0, 1, 2, 3, 4 }
        select new object[] { colourType, filter };

    [Theory]
    [MemberData(nameof(FiltersAndColourTypes))]
    public void ASyntheticImage_RoundTrips_UnderEachFilter(byte colourType, byte filter)
    {
        int channels = colourType == 2 ? 3 : 1;
        const int width = 23, height = 17;
        var rows = Pixels(width, height, channels);
        AssertDecodes(rows, width, channels, Encode(rows, width, colourType, _ => filter));
    }

    [Theory]
    [InlineData((byte)0)]
    [InlineData((byte)2)]
    public void AllFiveFiltersMixedRowByRow_OverSeveralIdatChunks_RoundTrip(byte colourType)
    {
        int channels = colourType == 2 ? 3 : 1;
        const int width = 31, height = 25;
        var rows = Pixels(width, height, channels);
        AssertDecodes(rows, width, channels, Encode(rows, width, colourType, r => (byte)(r % 5), idatChunks: 4));
    }

    /// <summary>
    /// Paeth's tie-breaks (PNG spec §9.4: a, then b, then c). Pixel (1, 1) has
    /// left 110, above 80, upper-left 100: p = 90 is 10 from both b and c, so b
    /// wins. Pixel (3, 1) has left 80, above 110, upper-left 100: p = 90 is 10
    /// from both a and c, so a wins. Smooth test images rarely hit either tie.
    /// </summary>
    [Fact]
    public void PaethBreaksTies_ToTheLeftThenTheAbove()
    {
        var rows = new[]
        {
            new byte[] { 100, 80, 100, 110 },
            new byte[] { 110, 37, 80, 201 },
        };
        AssertDecodes(rows, 4, 1, Encode(rows, 4, 0, r => (byte)(r == 0 ? 0 : 4)));
        Assert.Equal(80, Paeth(110, 80, 100));
        Assert.Equal(80, Paeth(80, 110, 100));
    }

    [Fact]
    public void AOnePixelImage_RoundTrips()
    {
        var rows = new[] { new byte[] { 200, 100, 50 } };
        AssertDecodes(rows, 1, 3, Encode(rows, 1, 2, _ => 4));
    }

    // ── Refusals that name the field ──────────────────────────────────────

    private static string Refusal(byte[] png) => Assert.Throws<InvalidDataException>(() => PngGrid.Decode(png)).Message;

    private static readonly byte[][] Small = Pixels(4, 3, 3);

    [Fact]
    public void A16BitImage_IsRefused_NamingTheBitDepth()
    {
        Assert.Contains("bit depth is 16", Refusal(Encode(Small, 4, 2, _ => 0, bitDepth: 16)));
    }

    [Fact]
    public void AnInterlacedImage_IsRefused_NamingTheInterlaceMethod()
    {
        Assert.Contains("interlace method is 1", Refusal(Encode(Small, 4, 2, _ => 0, interlace: 1)));
    }

    [Theory]
    [InlineData((byte)3)]
    [InlineData((byte)4)]
    [InlineData((byte)6)]
    public void AnotherColourType_IsRefused_NamingTheColourType(byte colourType)
    {
        Assert.Contains($"colour type is {colourType}", Refusal(Encode(Small, 4, colourType, _ => 0)));
    }

    [Fact]
    public void AnUnknownFilterType_IsRefused_NamingTheFilterAndRow()
    {
        var raw = Filtered(Small, 3, _ => 0);
        raw[2 * (4 * 3 + 1)] = 5;   // row 2's filter byte
        string message = Refusal(Encode(Small, 4, 2, _ => 0, rawOverride: raw));
        Assert.Contains("filter type is 5 on row 2", message);
    }

    [Fact]
    public void ABadSignature_IsRefused()
    {
        var png = Encode(Small, 4, 2, _ => 0);
        png[1] = (byte)'Q';
        Assert.Contains("signature", Refusal(png));
        Assert.Contains("signature", Refusal(new byte[] { 1, 2, 3 }));
    }

    [Fact]
    public void ATruncatedFile_IsRefused_NamingTheChunk()
    {
        var png = Encode(Small, 4, 2, _ => 0);
        int idat = FindChunk(png, "IDAT");
        Assert.Contains("'IDAT'", Refusal(png.Take(idat + 10).ToArray()));
    }

    [Fact]
    public void ShortImageData_IsRefused_NamingTheExpectedSize()
    {
        var raw = Filtered(Small, 3, _ => 0).Take(20).ToArray();
        string message = Refusal(Encode(Small, 4, 2, _ => 0, rawOverride: raw));
        Assert.Contains("IDAT data inflates to 20 bytes", message);
    }

    [Fact]
    public void ExtraImageData_IsRefused()
    {
        var raw = Filtered(Small, 3, _ => 0).Concat(new byte[] { 0, 0, 0 }).ToArray();
        Assert.Contains("more than the expected", Refusal(Encode(Small, 4, 2, _ => 0, rawOverride: raw)));
    }

    [Fact]
    public void CorruptCompressedData_IsRefused()
    {
        var png = Encode(Small, 4, 2, _ => 0);
        int idat = FindChunk(png, "IDAT");
        for (int i = idat + 8 + 2; i < idat + 8 + 8; i++) png[i] ^= 0xFF;
        Assert.Contains("IDAT", Refusal(png));
    }

    [Fact]
    public void AChunkBeforeIhdr_AndAMissingIdat_AreRefused()
    {
        using var noIdat = new MemoryStream();
        noIdat.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        Chunk(noIdat, "IHDR", Header(4, 3));
        Chunk(noIdat, "IEND", Array.Empty<byte>());
        Assert.Contains("no IDAT", Refusal(noIdat.ToArray()));

        using var early = new MemoryStream();
        early.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        Chunk(early, "IDAT", new byte[] { 1 });
        Assert.Contains("before IHDR", Refusal(early.ToArray()));
    }

    [Fact]
    public void SampleAndRow_RefuseOutOfRangeIndices()
    {
        var grid = PngGrid.Decode(Encode(Small, 4, 2, _ => 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => grid.Sample(4, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => grid.Sample(0, 3, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => grid.Sample(0, 0, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => grid.Sample(-1, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => grid.Row(3).ToArray());
    }

    private static int FindChunk(byte[] png, string type)
    {
        var tag = Encoding.ASCII.GetBytes(type);
        for (int i = 8; i + 8 <= png.Length; i++)
            if (png.AsSpan(i + 4, 4).SequenceEqual(tag))
                return i;
        throw new InvalidOperationException(type + " not found");
    }
}
