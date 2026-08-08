using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.OpenGL;
using Unreal99.Core;

namespace Unreal99.Rendering;

[StructLayout(LayoutKind.Sequential)]
public struct UiVertex
{
    public Vector2 Pos;
    public Vector2 Uv;
    public uint Color;
}

public enum TextAlign { Left, Center, Right }

/// <summary>
/// Immediate-mode batched 2D renderer for the HUD, scoreboard and menus.
/// Batches break only when the bound texture or the text/solid mode changes, so a whole
/// HUD is typically two or three draw calls.
/// </summary>
public sealed class UiRenderer : IDisposable
{
    /// <summary>Accessibility floor for every piece of in-game and menu text, in screen pixels.</summary>
    // Match the 22 px flag-carrier message at the 1600x900 reference resolution. Keeping the
    // floor in the shared renderer guarantees menus, notifications, scoreboards, and debug
    // overlays cannot quietly opt into smaller text after viewport scaling.
    public const float MinimumTextSize = 22f;

    private static readonly VertexAttrib[] Layout =
    [
        new(0, 2, VertexAttribPointerType.Float, false, 0),
        new(1, 2, VertexAttribPointerType.Float, false, 8),
        new(2, 4, VertexAttribPointerType.UnsignedByte, true, 16),
    ];

    private readonly GL _gl;
    private readonly Shader _shader;
    private readonly Mesh _mesh;
    private readonly FontSystem _fonts;
    private readonly Texture2D _white;

    private UiVertex[] _verts = new UiVertex[8192];
    private uint[] _indices = new uint[12288];
    private int _vcount, _icount;

    private uint _boundTexture;
    private bool _textMode;
    private bool _useTexture;
    private Matrix4x4 _proj;

    public int ScreenWidth { get; private set; }
    public int ScreenHeight { get; private set; }
    public FontSystem Fonts => _fonts;
    public int DrawCalls { get; private set; }

    public UiRenderer(GL gl, FontSystem fonts)
    {
        _gl = gl;
        _fonts = fonts;
        _shader = new Shader(gl, "ui", Shaders.UiVert, Shaders.UiFrag);
        _mesh = new Mesh(gl, Layout, Marshal.SizeOf<UiVertex>(), dynamic: true);
        Span<byte> px = [255, 255, 255, 255];
        _white = Texture2D.FromRgba(gl, 1, 1, px, false, false, 0);
    }

    public static uint Rgba(float r, float g, float b, float a = 1f)
        => (uint)MathX.Clamp((int)(r * 255f), 0, 255)
         | ((uint)MathX.Clamp((int)(g * 255f), 0, 255) << 8)
         | ((uint)MathX.Clamp((int)(b * 255f), 0, 255) << 16)
         | ((uint)MathX.Clamp((int)(a * 255f), 0, 255) << 24);

    public static uint Rgba(Vector4 c) => Rgba(c.X, c.Y, c.Z, c.W);
    public static uint Rgba(Vector3 c, float a = 1f) => Rgba(c.X, c.Y, c.Z, a);

    /// <summary>Multiplies a packed colour's alpha, preserving RGB.</summary>
    public static uint WithAlpha(uint c, float a)
    {
        uint old = (c >> 24) & 0xFF;
        uint na = (uint)MathX.Clamp((int)(old * a), 0, 255);
        return (c & 0x00FFFFFF) | (na << 24);
    }

    public void Begin(int width, int height)
    {
        ScreenWidth = width; ScreenHeight = height;
        _proj = MathX.Ortho(0, width, height, 0, -1, 1);
        _vcount = _icount = 0;
        _boundTexture = _white.Handle;
        _textMode = false;
        _useTexture = false;
        DrawCalls = 0;

        _gl.Disable(EnableCap.DepthTest);
        _gl.Disable(EnableCap.CullFace);
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
    }

    public void End()
    {
        Flush();
        _gl.Disable(EnableCap.Blend);
    }

    private void Flush()
    {
        if (_icount == 0) return;
        _shader.Use();
        _shader.Set("uProj", _proj);
        _shader.Set("uIsText", _textMode);
        _shader.Set("uUseTexture", _useTexture);
        _shader.Set("uTex", 0);
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, _boundTexture);
        _mesh.Upload<UiVertex>(_verts.AsSpan(0, _vcount), _indices.AsSpan(0, _icount), BufferUsageARB.StreamDraw);
        _mesh.Draw();
        DrawCalls++;
        _vcount = _icount = 0;
    }

    private void SetState(uint texture, bool textMode, bool useTexture)
    {
        if (texture != _boundTexture || textMode != _textMode || useTexture != _useTexture)
        {
            Flush();
            _boundTexture = texture;
            _textMode = textMode;
            _useTexture = useTexture;
        }
    }

    private void Reserve(int verts, int inds)
    {
        if (_vcount + verts > _verts.Length)
        {
            if (verts > _verts.Length) Array.Resize(ref _verts, Math.Max(_verts.Length * 2, verts * 2));
            else Flush();
        }
        if (_icount + inds > _indices.Length)
        {
            if (inds > _indices.Length) Array.Resize(ref _indices, Math.Max(_indices.Length * 2, inds * 2));
            else Flush();
        }
    }

    private void PushQuad(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3,
        Vector2 uv0, Vector2 uv1, uint c0, uint c1, uint c2, uint c3)
    {
        Reserve(4, 6);
        uint b = (uint)_vcount;
        _verts[_vcount++] = new UiVertex { Pos = p0, Uv = new Vector2(uv0.X, uv0.Y), Color = c0 };
        _verts[_vcount++] = new UiVertex { Pos = p1, Uv = new Vector2(uv1.X, uv0.Y), Color = c1 };
        _verts[_vcount++] = new UiVertex { Pos = p2, Uv = new Vector2(uv1.X, uv1.Y), Color = c2 };
        _verts[_vcount++] = new UiVertex { Pos = p3, Uv = new Vector2(uv0.X, uv1.Y), Color = c3 };
        _indices[_icount++] = b; _indices[_icount++] = b + 1; _indices[_icount++] = b + 2;
        _indices[_icount++] = b; _indices[_icount++] = b + 2; _indices[_icount++] = b + 3;
    }

    // ---------------------------------------------------------------- shapes

    public void Rect(float x, float y, float w, float h, uint color)
    {
        SetState(_white.Handle, false, false);
        PushQuad(new Vector2(x, y), new Vector2(x + w, y), new Vector2(x + w, y + h), new Vector2(x, y + h),
            Vector2.Zero, Vector2.One, color, color, color, color);
    }

    /// <summary>Vertical gradient rectangle.</summary>
    public void GradientRect(float x, float y, float w, float h, uint top, uint bottom)
    {
        SetState(_white.Handle, false, false);
        PushQuad(new Vector2(x, y), new Vector2(x + w, y), new Vector2(x + w, y + h), new Vector2(x, y + h),
            Vector2.Zero, Vector2.One, top, top, bottom, bottom);
    }

    public void HGradientRect(float x, float y, float w, float h, uint left, uint right)
    {
        SetState(_white.Handle, false, false);
        PushQuad(new Vector2(x, y), new Vector2(x + w, y), new Vector2(x + w, y + h), new Vector2(x, y + h),
            Vector2.Zero, Vector2.One, left, right, right, left);
    }

    public void RectOutline(float x, float y, float w, float h, float thickness, uint color)
    {
        Rect(x, y, w, thickness, color);
        Rect(x, y + h - thickness, w, thickness, color);
        Rect(x, y + thickness, thickness, h - thickness * 2, color);
        Rect(x + w - thickness, y + thickness, thickness, h - thickness * 2, color);
    }

    /// <summary>Rectangle with the corners cut off — the angular look UT99 uses for panels.</summary>
    public void ChamferRect(float x, float y, float w, float h, float cut, uint color)
    {
        SetState(_white.Handle, false, false);
        Reserve(8, 18);
        uint b = (uint)_vcount;
        Span<Vector2> pts =
        [
            new(x + cut, y), new(x + w - cut, y), new(x + w, y + cut), new(x + w, y + h - cut),
            new(x + w - cut, y + h), new(x + cut, y + h), new(x, y + h - cut), new(x, y + cut)
        ];
        for (int i = 0; i < 8; i++)
            _verts[_vcount++] = new UiVertex { Pos = pts[i], Uv = Vector2.Zero, Color = color };
        for (int i = 1; i < 7; i++)
        {
            _indices[_icount++] = b;
            _indices[_icount++] = b + (uint)i;
            _indices[_icount++] = b + (uint)i + 1;
        }
    }

    public void Line(Vector2 a, Vector2 b, float thickness, uint color)
    {
        Vector2 d = b - a;
        float len = d.Length();
        if (len < 1e-4f) return;
        Vector2 n = new Vector2(-d.Y, d.X) / len * (thickness * 0.5f);
        SetState(_white.Handle, false, false);
        PushQuad(a - n, b - n, b + n, a + n, Vector2.Zero, Vector2.One, color, color, color, color);
    }

    public void Circle(Vector2 center, float radius, uint color, int segments = 24)
    {
        SetState(_white.Handle, false, false);
        Reserve(segments + 1, segments * 3);
        uint b = (uint)_vcount;
        _verts[_vcount++] = new UiVertex { Pos = center, Uv = Vector2.Zero, Color = color };
        for (int i = 0; i < segments; i++)
        {
            float a = i / (float)segments * MathX.TwoPi;
            _verts[_vcount++] = new UiVertex
            {
                Pos = center + new Vector2(MathF.Cos(a), MathF.Sin(a)) * radius,
                Uv = Vector2.Zero,
                Color = color
            };
        }
        for (int i = 0; i < segments; i++)
        {
            _indices[_icount++] = b;
            _indices[_icount++] = b + (uint)i + 1;
            _indices[_icount++] = b + (uint)((i + 1) % segments) + 1;
        }
    }

    public void Ring(Vector2 center, float radius, float thickness, uint color, int segments = 32,
        float startAngle = 0f, float sweep = MathX.TwoPi)
    {
        SetState(_white.Handle, false, false);
        float inner = MathF.Max(0f, radius - thickness);
        Reserve((segments + 1) * 2, segments * 6);
        uint b = (uint)_vcount;
        for (int i = 0; i <= segments; i++)
        {
            float a = startAngle + i / (float)segments * sweep;
            Vector2 d = new(MathF.Cos(a), MathF.Sin(a));
            _verts[_vcount++] = new UiVertex { Pos = center + d * inner, Uv = Vector2.Zero, Color = color };
            _verts[_vcount++] = new UiVertex { Pos = center + d * radius, Uv = Vector2.Zero, Color = color };
        }
        for (int i = 0; i < segments; i++)
        {
            uint i0 = b + (uint)(i * 2);
            _indices[_icount++] = i0; _indices[_icount++] = i0 + 1; _indices[_icount++] = i0 + 3;
            _indices[_icount++] = i0; _indices[_icount++] = i0 + 3; _indices[_icount++] = i0 + 2;
        }
    }

    /// <summary>Filled triangle; used for arrows, hit markers and team indicators.</summary>
    public void Triangle(Vector2 a, Vector2 b, Vector2 c, uint color)
    {
        SetState(_white.Handle, false, false);
        Reserve(3, 3);
        uint bi = (uint)_vcount;
        _verts[_vcount++] = new UiVertex { Pos = a, Uv = Vector2.Zero, Color = color };
        _verts[_vcount++] = new UiVertex { Pos = b, Uv = Vector2.Zero, Color = color };
        _verts[_vcount++] = new UiVertex { Pos = c, Uv = Vector2.Zero, Color = color };
        _indices[_icount++] = bi; _indices[_icount++] = bi + 1; _indices[_icount++] = bi + 2;
    }

    public void Texture(Texture2D tex, float x, float y, float w, float h, uint color,
        Vector2 uv0 = default, Vector2 uv1 = default)
    {
        if (uv1 == default) uv1 = Vector2.One;
        SetState(tex.Handle, false, true);
        PushQuad(new Vector2(x, y), new Vector2(x + w, y), new Vector2(x + w, y + h), new Vector2(x, y + h),
            uv0, uv1, color, color, color, color);
    }

    // ---------------------------------------------------------------- text

    /// <summary>Draws text with the pen at the baseline unless <paramref name="fromTop"/> is set.</summary>
    public float Text(int face, float size, float x, float y, string text, uint color,
        TextAlign align = TextAlign.Left, bool fromTop = true, float letterSpacing = 0f)
    {
        if (string.IsNullOrEmpty(text)) return x;
        size = MathF.Max(MinimumTextSize, size);

        float baseline = fromTop ? y + _fonts.Ascent(face, size) : y;
        float width = align == TextAlign.Left ? 0f : _fonts.Measure(face, size, text, letterSpacing);
        float penX = align switch
        {
            TextAlign.Center => x - width * 0.5f,
            TextAlign.Right => x - width,
            _ => x,
        };

        SetState(_fonts.Atlas.Handle, true, false);
        var span = text.AsSpan();
        int prev = 0;
        for (int i = 0; i < span.Length; i++)
        {
            int cp = FontSystem.DecodeAt(span, ref i);
            if (cp == '\n')
            {
                baseline += _fonts.LineHeight(face, size);
                penX = align switch
                {
                    TextAlign.Center => x - width * 0.5f,
                    TextAlign.Right => x - width,
                    _ => x,
                };
                prev = 0;
                continue;
            }
            if (prev != 0) penX += _fonts.KernAdvance(face, size, prev, cp);
            Glyph g = _fonts.GetGlyph(face, size, cp);
            if (g.HasBitmap)
            {
                float gx = penX + g.XOffset;
                float gy = baseline + g.YOffset;
                SetState(_fonts.Atlas.Handle, true, false);
                PushQuad(new Vector2(gx, gy), new Vector2(gx + g.Width, gy),
                    new Vector2(gx + g.Width, gy + g.Height), new Vector2(gx, gy + g.Height),
                    new Vector2(g.U0, g.V0), new Vector2(g.U1, g.V1), color, color, color, color);
            }
            penX += g.Advance + letterSpacing;
            prev = cp;
        }
        return penX;
    }

    /// <summary>Text with a drop shadow. Essential over bright, busy 3D scenes.</summary>
    public void TextShadow(int face, float size, float x, float y, string text, uint color,
        TextAlign align = TextAlign.Left, float shadowOffset = 2f, float shadowAlpha = 0.85f,
        bool fromTop = true, float letterSpacing = 0f)
    {
        uint shadow = Rgba(0f, 0f, 0f, shadowAlpha);
        Text(face, size, x + shadowOffset, y + shadowOffset, text, shadow, align, fromTop, letterSpacing);
        Text(face, size, x, y, text, color, align, fromTop, letterSpacing);
    }

    /// <summary>Text with a full outline; used for the big HUD numbers and announcements.</summary>
    public void TextOutline(int face, float size, float x, float y, string text, uint color,
        uint outline, float thickness = 2f, TextAlign align = TextAlign.Left, bool fromTop = true,
        float letterSpacing = 0f)
    {
        for (int i = 0; i < 8; i++)
        {
            float a = i / 8f * MathX.TwoPi;
            Text(face, size, x + MathF.Cos(a) * thickness, y + MathF.Sin(a) * thickness,
                text, outline, align, fromTop, letterSpacing);
        }
        Text(face, size, x, y, text, color, align, fromTop, letterSpacing);
    }

    public float MeasureText(int face, float size, string text, float letterSpacing = 0f)
        => _fonts.Measure(face, MathF.Max(MinimumTextSize, size), text, letterSpacing);

    public void Dispose()
    {
        _shader.Dispose();
        _mesh.Dispose();
        _white.Dispose();
    }
}
