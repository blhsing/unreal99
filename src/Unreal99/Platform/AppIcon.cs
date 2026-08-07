using System.Buffers.Binary;
using System.Numerics;
using StbImageSharp;
using Unreal99.Core;

namespace Unreal99.Platform;

/// <summary>
/// Builds a multi-resolution Windows icon from the branded game emblem, with the original
/// procedural badge retained as a safe fallback if the packaged artwork is unavailable.
/// </summary>
public static class AppIcon
{
    private static readonly int[] Sizes = [16, 32, 48, 64, 128, 256];

    /// <summary>Writes a Windows .ico containing every size in <see cref="Sizes"/>.</summary>
    public static void WriteIco(string path)
    {
        ImageResult logo = TryLoadLogo();
        var images = new List<byte[]>(Sizes.Length);
        foreach (int size in Sizes)
        {
            byte[] rgba = logo != null ? RenderLogo(logo, size) : Render(size);
            using var ms = new MemoryStream();
            Png.WriteToStream(ms, size, size, rgba, 4, flipVertically: false);
            images.Add(ms.ToArray());
        }

        using var file = File.Create(path);
        Span<byte> header = stackalloc byte[6];
        BinaryPrimitives.WriteUInt16LittleEndian(header[..2], 0);        // reserved
        BinaryPrimitives.WriteUInt16LittleEndian(header.Slice(2, 2), 1); // 1 = icon
        BinaryPrimitives.WriteUInt16LittleEndian(header.Slice(4, 2), (ushort)images.Count);
        file.Write(header);

        int offset = 6 + 16 * images.Count;
        Span<byte> entry = stackalloc byte[16];
        for (int i = 0; i < images.Count; i++)
        {
            // 256 is encoded as 0 in the directory entry.
            entry[0] = (byte)(Sizes[i] >= 256 ? 0 : Sizes[i]);
            entry[1] = (byte)(Sizes[i] >= 256 ? 0 : Sizes[i]);
            entry[2] = 0;   // palette size
            entry[3] = 0;   // reserved
            BinaryPrimitives.WriteUInt16LittleEndian(entry.Slice(4, 2), 1);   // colour planes
            BinaryPrimitives.WriteUInt16LittleEndian(entry.Slice(6, 2), 32);  // bits per pixel
            BinaryPrimitives.WriteInt32LittleEndian(entry.Slice(8, 4), images[i].Length);
            BinaryPrimitives.WriteInt32LittleEndian(entry.Slice(12, 4), offset);
            file.Write(entry);
            offset += images[i].Length;
        }

        foreach (byte[] image in images) file.Write(image);
    }

    private static ImageResult TryLoadLogo()
    {
        string[] candidates =
        [
            Path.Combine(AppContext.BaseDirectory, "Assets", "Unreal99Logo.png"),
            Path.Combine(AppContext.BaseDirectory, "Unreal99Logo.png"),
            Path.Combine(Environment.CurrentDirectory, "src", "Unreal99", "Assets", "Unreal99Logo.png"),
        ];
        foreach (string candidate in candidates)
        {
            if (!File.Exists(candidate)) continue;
            try
            {
                using var stream = File.OpenRead(candidate);
                return ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
            }
            catch (Exception) { }
        }
        return null;
    }

    private static byte[] RenderLogo(ImageResult source, int size)
    {
        byte[] output = new byte[size * size * 4];
        float scale = MathF.Min(source.Width, source.Height) / (size * 0.94f);
        float content = size * 0.94f;
        float inset = (size - content) * 0.5f;

        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float sx = (x - inset + 0.5f) * scale - 0.5f + (source.Width - source.Height) * 0.5f;
            float sy = (y - inset + 0.5f) * scale - 0.5f;
            if (sx < 0f || sy < 0f || sx >= source.Width - 1 || sy >= source.Height - 1) continue;

            int x0 = (int)MathF.Floor(sx), y0 = (int)MathF.Floor(sy);
            float tx = sx - x0, ty = sy - y0;
            Vector4 c00 = Pixel(x0, y0), c10 = Pixel(x0 + 1, y0);
            Vector4 c01 = Pixel(x0, y0 + 1), c11 = Pixel(x0 + 1, y0 + 1);
            Vector4 c = Vector4.Lerp(Vector4.Lerp(c00, c10, tx), Vector4.Lerp(c01, c11, tx), ty);
            int d = (y * size + x) * 4;
            output[d] = ToByte(c.X);
            output[d + 1] = ToByte(c.Y);
            output[d + 2] = ToByte(c.Z);
            output[d + 3] = ToByte(c.W);
        }
        return output;

        Vector4 Pixel(int px, int py)
        {
            int i = (py * source.Width + px) * 4;
            return new Vector4(source.Data[i] / 255f, source.Data[i + 1] / 255f,
                source.Data[i + 2] / 255f, source.Data[i + 3] / 255f);
        }
    }

    /// <summary>
    /// A dark metal badge with a glowing chevron, matching the game's orange-on-gunmetal palette.
    /// Rendered with 3x3 supersampling so the small sizes stay clean.
    /// </summary>
    private static byte[] Render(int size)
    {
        var pixels = new byte[size * size * 4];
        const int Samples = 3;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector4 accum = Vector4.Zero;
                for (int sy = 0; sy < Samples; sy++)
                {
                    for (int sx = 0; sx < Samples; sx++)
                    {
                        float u = (x + (sx + 0.5f) / Samples) / size * 2f - 1f;
                        float v = (y + (sy + 0.5f) / Samples) / size * 2f - 1f;
                        accum += Sample(u, v);
                    }
                }
                accum /= Samples * Samples;

                int i = (y * size + x) * 4;
                pixels[i + 0] = ToByte(accum.X);
                pixels[i + 1] = ToByte(accum.Y);
                pixels[i + 2] = ToByte(accum.Z);
                pixels[i + 3] = ToByte(accum.W);
            }
        }
        return pixels;
    }

    private static Vector4 Sample(float u, float v)
    {
        float r = MathF.Sqrt(u * u + v * v);
        if (r > 0.99f) return Vector4.Zero;

        // Gunmetal body with a soft top-left highlight and a darker rim.
        float lighting = MathX.Saturate(0.55f - (u * 0.35f + v * 0.55f));
        Vector3 body = Vector3.Lerp(new Vector3(0.055f, 0.060f, 0.080f),
                                    new Vector3(0.230f, 0.245f, 0.290f), lighting);
        body *= MathX.Lerp(1f, 0.55f, MathX.SmoothStep(0.70f, 0.99f, r));

        Vector3 color = body;

        // Outer ring.
        float ring = 1f - MathF.Abs(r - 0.88f) / 0.07f;
        color = Vector3.Lerp(color, new Vector3(1.0f, 0.62f, 0.16f), MathX.Saturate(ring) * 0.95f);

        // Inner hairline.
        float inner = 1f - MathF.Abs(r - 0.70f) / 0.018f;
        color = Vector3.Lerp(color, new Vector3(0.45f, 0.72f, 1.0f), MathX.Saturate(inner) * 0.55f);

        // Chevron: distance to a V, thick and slightly rounded at the tips.
        float chevron = ChevronMask(u, v, 0.02f, 0.155f);
        float chevronGlow = ChevronMask(u, v, 0.02f, 0.30f);
        color += new Vector3(1.0f, 0.45f, 0.10f) * chevronGlow * 0.35f;
        color = Vector3.Lerp(color, new Vector3(1.0f, 0.80f, 0.35f), chevron);

        float alpha = MathX.Saturate((0.99f - r) / 0.03f);
        return new Vector4(color, alpha);
    }

    /// <summary>Coverage of an upward chevron centred in the badge.</summary>
    private static float ChevronMask(float u, float v, float yOffset, float thickness)
    {
        // The V is y = |x| * slope - height; distance to it gives an even stroke width.
        const float Slope = 1.05f;
        const float Height = 0.26f;
        float d = MathF.Abs((v + yOffset) - (MathF.Abs(u) * Slope - Height));
        // Normalise by the slope so the diagonal arms are not thinner than a vertical stroke.
        d /= MathF.Sqrt(1f + Slope * Slope) * 0.72f;

        float stroke = 1f - MathX.SmoothStep(thickness * 0.65f, thickness, d);
        // Clip to the arms so the V does not extend into a full X.
        float span = 1f - MathX.SmoothStep(0.46f, 0.54f, MathF.Abs(u));
        float bottom = 1f - MathX.SmoothStep(0.40f, 0.48f, v);
        return stroke * span * bottom;
    }

    private static byte ToByte(float v) => (byte)MathX.Clamp((int)(MathX.Saturate(v) * 255f + 0.5f), 0, 255);
}
