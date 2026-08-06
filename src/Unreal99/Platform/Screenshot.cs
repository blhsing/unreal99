using System.Buffers.Binary;
using System.IO.Compression;

namespace Unreal99.Platform;

/// <summary>
/// Minimal PNG writer. Avoids pulling in an imaging library just to save screenshots:
/// PNG is a handful of chunks around a zlib stream, and .NET already ships the zlib codec.
/// </summary>
public static class Png
{
    /// <summary>Writes 8-bit RGB(A) pixel data as a PNG. Set <paramref name="flipVertically"/> for GL buffers.</summary>
    public static void Write(string path, int width, int height, ReadOnlySpan<byte> pixels, int channels = 4,
        bool flipVertically = true)
    {
        using var file = File.Create(path);
        WriteToStream(file, width, height, pixels, channels, flipVertically);
    }

    /// <summary>Same encoder, writing into any stream. Used to embed PNGs inside an .ico.</summary>
    public static void WriteToStream(Stream stream, int width, int height, ReadOnlySpan<byte> pixels,
        int channels = 4, bool flipVertically = true)
    {
        if (width <= 0 || height <= 0) return;
        byte colorType = channels == 4 ? (byte)6 : (byte)2;
        int stride = width * channels;

        // Each scanline is prefixed with a filter byte; filter 0 (None) keeps this simple.
        var raw = new byte[(stride + 1) * height];
        for (int y = 0; y < height; y++)
        {
            int src = (flipVertically ? height - 1 - y : y) * stride;
            int dst = y * (stride + 1);
            raw[dst] = 0;
            pixels.Slice(src, stride).CopyTo(raw.AsSpan(dst + 1, stride));
        }

        byte[] compressed;
        using (var ms = new MemoryStream())
        {
            using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
                z.Write(raw, 0, raw.Length);
            compressed = ms.ToArray();
        }

        Span<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        stream.Write(signature);

        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr[..4], width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.Slice(4, 4), height);
        ihdr[8] = 8;              // bit depth
        ihdr[9] = colorType;
        ihdr[10] = 0;             // deflate
        ihdr[11] = 0;             // adaptive filtering
        ihdr[12] = 0;             // no interlace
        WriteChunk(stream, "IHDR", ihdr);
        WriteChunk(stream, "IDAT", compressed);
        WriteChunk(stream, "IEND", []);
    }

    private static void WriteChunk(Stream stream, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        stream.Write(length);

        Span<byte> typeBytes = stackalloc byte[4];
        for (int i = 0; i < 4; i++) typeBytes[i] = (byte)type[i];
        stream.Write(typeBytes);
        stream.Write(data);

        // The CRC covers the chunk type followed by its data.
        uint crc = 0xFFFFFFFFu;
        crc = Crc32Update(crc, typeBytes);
        crc = Crc32Update(crc, data);
        crc ^= 0xFFFFFFFFu;

        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        stream.Write(crcBytes);
    }

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }

    private static uint Crc32Update(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (byte b in data) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc;
    }
}
