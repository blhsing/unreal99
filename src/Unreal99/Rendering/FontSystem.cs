using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.OpenGL;
using StbTrueTypeSharp;
using Unreal99.Core;

namespace Unreal99.Rendering;

public struct Glyph
{
    public float U0, V0, U1, V1;
    public float XOffset, YOffset;   // pixel offset from the pen position to the bitmap's top-left
    public float Advance;
    public float Width, Height;
    public bool HasBitmap;
}

/// <summary>
/// Rasterises glyphs on demand into a single shared R8 atlas using a shelf packer.
/// Traditional Chinese needs thousands of distinct glyphs, so pre-baking a fixed range
/// is wasteful — instead every codepoint is added the first time it is drawn and cached.
/// </summary>
public sealed class FontSystem : IDisposable
{
    private sealed class Face
    {
        public StbTrueType.stbtt_fontinfo Info;
        public GCHandle Pin;
        public int Ascent, Descent, LineGap;
        public string Name = "";
    }

    public const int AtlasSize = 2048;

    private readonly GL _gl;
    private readonly List<Face> _faces = new();
    private readonly Dictionary<long, Glyph> _glyphs = new();
    private readonly byte[] _scratch = new byte[256 * 256];

    private int _penX = 1, _penY = 1, _shelfHeight;
    private bool _atlasFull;

    public Texture2D Atlas { get; }
    public int FaceCount => _faces.Count;

    public unsafe FontSystem(GL gl)
    {
        _gl = gl;
        Atlas = new Texture2D(gl, AtlasSize, AtlasSize, InternalFormat.R8,
            PixelFormat.Red, PixelType.UnsignedByte, null, false, true, false);

        // Clear to zero so unwritten regions never bleed into glyph edges.
        var zero = new byte[AtlasSize * 64];
        for (int y = 0; y < AtlasSize; y += 64)
            Atlas.SubImage(0, y, AtlasSize, 64, zero, PixelFormat.Red);
    }

    /// <summary>Loads a TTF/TTC. Returns the face id, or -1 if the file is missing or unreadable.</summary>
    public unsafe int AddFont(string path, int collectionIndex = 0, string name = null)
    {
        try
        {
            if (!File.Exists(path)) return -1;
            byte[] data = File.ReadAllBytes(path);
            int offset = 0;
            fixed (byte* p = data)
            {
                int o = StbTrueType.stbtt_GetFontOffsetForIndex(p, collectionIndex);
                if (o < 0) o = StbTrueType.stbtt_GetFontOffsetForIndex(p, 0);
                if (o < 0) return -1;
                offset = o;
            }

            var pin = GCHandle.Alloc(data, GCHandleType.Pinned);
            var info = new StbTrueType.stbtt_fontinfo();
            byte* ptr = (byte*)pin.AddrOfPinnedObject();
            if (StbTrueType.stbtt_InitFont(info, ptr, offset) == 0)
            {
                pin.Free();
                return -1;
            }

            var face = new Face { Info = info, Pin = pin, Name = name ?? Path.GetFileName(path) };
            int a, d, l;
            StbTrueType.stbtt_GetFontVMetrics(info, &a, &d, &l);
            face.Ascent = a; face.Descent = d; face.LineGap = l;
            _faces.Add(face);
            return _faces.Count - 1;
        }
        catch (Exception)
        {
            return -1;
        }
    }

    public string FaceName(int face) => Valid(face) ? _faces[face].Name : "";

    private bool Valid(int face) => face >= 0 && face < _faces.Count;

    private float ScaleFor(int face, float pixelHeight)
        => Valid(face) ? StbTrueType.stbtt_ScaleForPixelHeight(_faces[face].Info, pixelHeight) : 0f;

    public float Ascent(int face, float pixelHeight)
        => Valid(face) ? _faces[face].Ascent * ScaleFor(face, pixelHeight) : pixelHeight * 0.8f;

    public float Descent(int face, float pixelHeight)
        => Valid(face) ? _faces[face].Descent * ScaleFor(face, pixelHeight) : -pixelHeight * 0.2f;

    public float LineHeight(int face, float pixelHeight)
    {
        if (!Valid(face)) return pixelHeight * 1.25f;
        var f = _faces[face];
        return (f.Ascent - f.Descent + f.LineGap) * ScaleFor(face, pixelHeight);
    }

    private static long Key(int face, float pixelHeight, int codepoint)
        => ((long)face << 48) | ((long)(int)(pixelHeight * 4f) << 24) | (uint)codepoint;

    /// <summary>Returns the cached glyph, rasterising and packing it on first use.</summary>
    public unsafe Glyph GetGlyph(int face, float pixelHeight, int codepoint)
    {
        long key = Key(face, pixelHeight, codepoint);
        if (_glyphs.TryGetValue(key, out Glyph g)) return g;

        g = default;
        if (!Valid(face)) { _glyphs[key] = g; return g; }

        var f = _faces[face];
        float scale = ScaleFor(face, pixelHeight);
        int glyphIndex = StbTrueType.stbtt_FindGlyphIndex(f.Info, codepoint);
        if (glyphIndex == 0 && codepoint != ' ')
        {
            // Missing glyph: cache an empty entry so we do not retry every frame.
            _glyphs[key] = g;
            return g;
        }

        int adv, lsb;
        StbTrueType.stbtt_GetGlyphHMetrics(f.Info, glyphIndex, &adv, &lsb);
        g.Advance = adv * scale;

        int x0, y0, x1, y1;
        StbTrueType.stbtt_GetGlyphBitmapBox(f.Info, glyphIndex, scale, scale, &x0, &y0, &x1, &y1);
        int w = x1 - x0, h = y1 - y0;

        if (w > 0 && h > 0 && !_atlasFull)
        {
            const int pad = 1;
            if (_penX + w + pad >= AtlasSize)
            {
                _penX = 1;
                _penY += _shelfHeight + pad;
                _shelfHeight = 0;
            }
            if (_penY + h + pad >= AtlasSize)
            {
                _atlasFull = true;
            }
            else
            {
                byte[] buffer = w * h <= _scratch.Length ? _scratch : new byte[w * h];
                Array.Clear(buffer, 0, w * h);
                fixed (byte* dst = buffer)
                    StbTrueType.stbtt_MakeGlyphBitmap(f.Info, dst, w, h, w, scale, scale, glyphIndex);

                Atlas.SubImage(_penX, _penY, w, h, buffer.AsSpan(0, w * h), PixelFormat.Red);

                const float inv = 1f / AtlasSize;
                g.U0 = _penX * inv;
                g.V0 = _penY * inv;
                g.U1 = (_penX + w) * inv;
                g.V1 = (_penY + h) * inv;
                g.Width = w;
                g.Height = h;
                g.XOffset = x0;
                g.YOffset = y0;
                g.HasBitmap = true;

                _penX += w + pad;
                _shelfHeight = Math.Max(_shelfHeight, h);
            }
        }

        _glyphs[key] = g;
        return g;
    }

    public float KernAdvance(int face, float pixelHeight, int a, int b)
    {
        if (!Valid(face)) return 0f;
        return StbTrueType.stbtt_GetCodepointKernAdvance(_faces[face].Info, a, b) * ScaleFor(face, pixelHeight);
    }

    /// <summary>Measures a string's advance width in pixels.</summary>
    public float Measure(int face, float pixelHeight, ReadOnlySpan<char> text, float letterSpacing = 0f)
    {
        float x = 0f;
        int prev = 0;
        for (int i = 0; i < text.Length; i++)
        {
            int cp = DecodeAt(text, ref i);
            if (cp == '\n') { prev = 0; continue; }
            if (prev != 0) x += KernAdvance(face, pixelHeight, prev, cp);
            x += GetGlyph(face, pixelHeight, cp).Advance + letterSpacing;
            prev = cp;
        }
        return x;
    }

    /// <summary>Reads one code point at <paramref name="i"/>, advancing past a surrogate pair if present.</summary>
    public static int DecodeAt(ReadOnlySpan<char> text, ref int i)
    {
        char c = text[i];
        if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
        {
            int cp = char.ConvertToUtf32(c, text[i + 1]);
            i++;
            return cp;
        }
        return c;
    }

    /// <summary>Word-wraps text to a pixel width. CJK breaks anywhere; Latin breaks on spaces.</summary>
    public List<string> Wrap(int face, float pixelHeight, string text, float maxWidth)
    {
        var lines = new List<string>();
        var sb = new System.Text.StringBuilder();
        float x = 0f;
        int lastBreak = -1;
        float widthAtBreak = 0f;

        foreach (string paragraph in text.Split('\n'))
        {
            sb.Clear(); x = 0f; lastBreak = -1;
            var span = paragraph.AsSpan();
            for (int i = 0; i < span.Length; i++)
            {
                int start = i;
                int cp = DecodeAt(span, ref i);
                float adv = GetGlyph(face, pixelHeight, cp).Advance;

                bool cjk = cp >= 0x2E80 && cp <= 0x9FFF || cp >= 0xF900 && cp <= 0xFAFF
                        || cp >= 0xFF00 && cp <= 0xFF60 || cp >= 0x3000 && cp <= 0x303F;
                if (cp == ' ' || cjk) { lastBreak = sb.Length; widthAtBreak = x; }

                if (x + adv > maxWidth && sb.Length > 0)
                {
                    if (lastBreak > 0 && lastBreak < sb.Length)
                    {
                        string head = sb.ToString(0, lastBreak).TrimEnd();
                        string tail = sb.ToString(lastBreak, sb.Length - lastBreak);
                        lines.Add(head);
                        sb.Clear();
                        sb.Append(tail.TrimStart());
                        x = Measure(face, pixelHeight, sb.ToString());
                    }
                    else
                    {
                        lines.Add(sb.ToString());
                        sb.Clear();
                        x = 0f;
                    }
                    lastBreak = -1;
                    _ = widthAtBreak;
                }

                sb.Append(span.Slice(start, i - start + 1));
                x += adv;
            }
            lines.Add(sb.ToString());
        }
        return lines;
    }

    public void Dispose()
    {
        Atlas.Dispose();
        foreach (var f in _faces) if (f.Pin.IsAllocated) f.Pin.Free();
        _faces.Clear();
    }
}
