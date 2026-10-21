using System;
using System.IO;
using System.IO.Compression;

namespace LakskongenSim.Environment.Geo
{
    /// <summary>
    /// A decoded data PNG: 8-bit greyscale or RGB, non-interlaced, exact byte
    /// values (the suitability grids are data, not pictures; GEODATA §7.1).
    /// A port of <c>tests/test_geodata.py::decode_png</c> on the BCL's
    /// <see cref="ZLibStream"/>: signature and chunk parsing, the IDATs
    /// concatenated and inflated, and filters 0-4 undone. Anything else
    /// (another bit depth, colour type, interlace or filter, a truncated
    /// chunk, a short image) throws <see cref="InvalidDataException"/> naming
    /// the field. Immutable; rows run north to south.
    /// </summary>
    public sealed class PngGrid
    {
        private static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        /// <summary>The largest decoded image accepted (bytes): far above any geo grid, far below a memory problem.</summary>
        private const long MaxDecodedBytes = 64L * 1024 * 1024;

        private readonly byte[] _pixels;

        private PngGrid(int width, int height, byte colourType, byte[] pixels)
        {
            Width = width;
            Height = height;
            ColourType = colourType;
            Channels = colourType == 2 ? 3 : 1;
            _pixels = pixels;
        }

        /// <summary>Columns.</summary>
        public int Width { get; }

        /// <summary>Rows.</summary>
        public int Height { get; }

        /// <summary>PNG colour type: 0 greyscale or 2 RGB.</summary>
        public byte ColourType { get; }

        /// <summary>Bytes per pixel: 1 (greyscale) or 3 (RGB).</summary>
        public int Channels { get; }

        /// <summary>One channel of one pixel (column, row from the top, channel 0..Channels-1).</summary>
        public byte Sample(int column, int row, int channel)
        {
            if ((uint)column >= (uint)Width) throw new ArgumentOutOfRangeException(nameof(column));
            if ((uint)row >= (uint)Height) throw new ArgumentOutOfRangeException(nameof(row));
            if ((uint)channel >= (uint)Channels) throw new ArgumentOutOfRangeException(nameof(channel));
            return _pixels[((long)row * Width + column) * Channels + channel];
        }

        /// <summary>One row's bytes (Width × Channels), read-only.</summary>
        public ReadOnlySpan<byte> Row(int row)
        {
            if ((uint)row >= (uint)Height) throw new ArgumentOutOfRangeException(nameof(row));
            int stride = Width * Channels;
            return new ReadOnlySpan<byte>(_pixels, row * stride, stride);
        }

        /// <summary>Decode an 8-bit greyscale or RGB, non-interlaced PNG.</summary>
        public static PngGrid Decode(byte[] png)
        {
            if (png == null) throw new ArgumentNullException(nameof(png));
            if (png.Length < Signature.Length || !png.AsSpan(0, Signature.Length).SequenceEqual(Signature))
                throw new InvalidDataException("PNG signature is missing");

            Header? header = null;
            using var idat = new MemoryStream();
            int pos = Signature.Length;
            bool ended = false;
            while (pos < png.Length && !ended)
            {
                if (pos + 8 > png.Length) throw new InvalidDataException($"PNG chunk at byte {pos} is truncated");
                uint length = ReadUInt32(png, pos);
                string type = System.Text.Encoding.ASCII.GetString(png, pos + 4, 4);
                if (length > int.MaxValue || pos + 12L + length > png.Length)
                    throw new InvalidDataException($"PNG chunk '{type}' length {length} runs past the end");
                var data = new ReadOnlySpan<byte>(png, pos + 8, (int)length);
                if (header == null && type != "IHDR")
                    throw new InvalidDataException($"PNG chunk '{type}' comes before IHDR");
                switch (type)
                {
                    case "IHDR":
                        if (header != null) throw new InvalidDataException("PNG has a second IHDR");
                        header = ReadHeader(data);
                        break;
                    case "IDAT":
                        idat.Write(data);
                        break;
                    case "IEND":
                        ended = true;
                        break;
                }
                pos += 12 + (int)length;
            }
            if (header == null) throw new InvalidDataException("PNG has no IHDR");
            if (idat.Length == 0) throw new InvalidDataException("PNG has no IDAT data");

            var h = header.Value;
            int stride = h.Width * h.Channels;
            byte[] filtered = Inflate(idat, (long)h.Height * (stride + 1));
            return new PngGrid(h.Width, h.Height, h.ColourType, Unfilter(filtered, h.Width, h.Height, h.Channels));
        }

        private readonly record struct Header(int Width, int Height, byte ColourType, int Channels);

        private static Header ReadHeader(ReadOnlySpan<byte> d)
        {
            if (d.Length != 13) throw new InvalidDataException($"PNG IHDR length is {d.Length}, expected 13");
            uint width = ReadUInt32(d, 0), height = ReadUInt32(d, 4);
            byte bitDepth = d[8], colourType = d[9], compression = d[10], filterMethod = d[11], interlace = d[12];
            if (width == 0 || height == 0 || width > int.MaxValue || height > int.MaxValue)
                throw new InvalidDataException($"PNG width {width} or height {height} is out of range");
            if (bitDepth != 8)
                throw new InvalidDataException($"PNG bit depth is {bitDepth}; only bit depth 8 is supported");
            if (colourType != 0 && colourType != 2)
                throw new InvalidDataException($"PNG colour type is {colourType}; only colour type 0 (grey) or 2 (RGB) is supported");
            if (compression != 0)
                throw new InvalidDataException($"PNG compression method is {compression}; only 0 is defined");
            if (filterMethod != 0)
                throw new InvalidDataException($"PNG filter method is {filterMethod}; only 0 is defined");
            if (interlace != 0)
                throw new InvalidDataException($"PNG interlace method is {interlace}; only 0 (no interlace) is supported");
            int channels = colourType == 2 ? 3 : 1;
            if ((long)width * height * channels > MaxDecodedBytes)
                throw new InvalidDataException($"PNG image {width}x{height} is too large for a data grid");
            return new Header((int)width, (int)height, colourType, channels);
        }

        private static byte[] Inflate(MemoryStream idat, long expected)
        {
            idat.Position = 0;
            var output = new byte[expected];
            int read = 0;
            bool longer;
            try
            {
                using var zlib = new ZLibStream(idat, CompressionMode.Decompress, leaveOpen: true);
                while (read < output.Length)
                {
                    int n = zlib.Read(output, read, output.Length - read);
                    if (n == 0) break;
                    read += n;
                }
                longer = read == output.Length && zlib.Read(new byte[1], 0, 1) != 0;
            }
            catch (InvalidDataException e)
            {
                throw new InvalidDataException($"PNG IDAT data does not inflate: {e.Message}", e);
            }
            if (read < output.Length)
                throw new InvalidDataException($"PNG IDAT data inflates to {read} bytes, expected {expected}");
            if (longer)
                throw new InvalidDataException($"PNG IDAT data inflates to more than the expected {expected} bytes");
            return output;
        }

        /// <summary>Undo the per-row filters (PNG spec §9; test_geodata.py's loop, row by row).</summary>
        private static byte[] Unfilter(byte[] data, int width, int height, int channels)
        {
            int stride = width * channels;
            var pixels = new byte[(long)stride * height];
            for (int r = 0; r < height; r++)
            {
                int src = r * (stride + 1);
                byte filter = data[src];
                if (filter > 4)
                    throw new InvalidDataException($"PNG filter type is {filter} on row {r}; only filter types 0-4 exist");
                int dst = r * stride;
                int up = dst - stride;
                for (int i = 0; i < stride; i++)
                {
                    int x = data[src + 1 + i];
                    int left = i >= channels ? pixels[dst + i - channels] : 0;
                    int above = r > 0 ? pixels[up + i] : 0;
                    int upperLeft = r > 0 && i >= channels ? pixels[up + i - channels] : 0;
                    pixels[dst + i] = (byte)(filter switch
                    {
                        0 => x,
                        1 => x + left,
                        2 => x + above,
                        3 => x + ((left + above) >> 1),
                        _ => x + Paeth(left, above, upperLeft),
                    });
                }
            }
            return pixels;
        }

        private static int Paeth(int a, int b, int c)
        {
            int p = a + b - c;
            int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
            return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
        }

        private static uint ReadUInt32(ReadOnlySpan<byte> d, int offset) =>
            (uint)(d[offset] << 24 | d[offset + 1] << 16 | d[offset + 2] << 8 | d[offset + 3]);
    }
}
