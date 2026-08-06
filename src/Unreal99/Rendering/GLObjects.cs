using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Silk.NET.OpenGL;

namespace Unreal99.Rendering;

/// <summary>Standard static-geometry vertex. 52 bytes.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct Vertex
{
    public Vector3 Position;
    public Vector3 Normal;
    public Vector4 Tangent;   // xyz = tangent, w = bitangent handedness
    public Vector2 Uv;
    public uint Color;        // RGBA8, used as a per-vertex tint / AO bake

    public Vertex(Vector3 p, Vector3 n, Vector2 uv, uint color = 0xFFFFFFFF)
    {
        Position = p; Normal = n; Uv = uv; Color = color;
        Tangent = new Vector4(1, 0, 0, 1);
    }
}

/// <summary>Skinned vertex: adds four bone influences. 60 bytes.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct SkinnedVertex
{
    public Vector3 Position;
    public Vector3 Normal;
    public Vector4 Tangent;
    public Vector2 Uv;
    public uint Color;
    public uint BoneIndices;  // 4 x u8
    public uint BoneWeights;  // 4 x u8, normalized
}

public readonly record struct VertexAttrib(
    uint Index, int Size, VertexAttribPointerType Type, bool Normalized, int Offset, bool AsInteger = false);

public static class VertexLayouts
{
    public static readonly VertexAttrib[] Static =
    [
        new(0, 3, VertexAttribPointerType.Float, false, 0),
        new(1, 3, VertexAttribPointerType.Float, false, 12),
        new(2, 4, VertexAttribPointerType.Float, false, 24),
        new(3, 2, VertexAttribPointerType.Float, false, 40),
        new(4, 4, VertexAttribPointerType.UnsignedByte, true, 48),
    ];

    public static readonly VertexAttrib[] Skinned =
    [
        new(0, 3, VertexAttribPointerType.Float, false, 0),
        new(1, 3, VertexAttribPointerType.Float, false, 12),
        new(2, 4, VertexAttribPointerType.Float, false, 24),
        new(3, 2, VertexAttribPointerType.Float, false, 40),
        new(4, 4, VertexAttribPointerType.UnsignedByte, true, 48),
        new(5, 4, VertexAttribPointerType.UnsignedByte, false, 52, AsInteger: true),
        new(6, 4, VertexAttribPointerType.UnsignedByte, true, 56),
    ];
}

/// <summary>Compiled GLSL program with cached uniform locations.</summary>
public sealed class Shader : IDisposable
{
    private readonly GL _gl;
    private readonly Dictionary<string, int> _locations = new(StringComparer.Ordinal);
    public uint Handle { get; }
    public string Name { get; }

    public Shader(GL gl, string name, string vertexSrc, string fragmentSrc, string geometrySrc = null)
    {
        _gl = gl;
        Name = name;
        uint vs = Compile(ShaderType.VertexShader, vertexSrc, name + ".vert");
        uint fs = Compile(ShaderType.FragmentShader, fragmentSrc, name + ".frag");
        uint gs = geometrySrc != null ? Compile(ShaderType.GeometryShader, geometrySrc, name + ".geom") : 0;

        Handle = _gl.CreateProgram();
        _gl.AttachShader(Handle, vs);
        _gl.AttachShader(Handle, fs);
        if (gs != 0) _gl.AttachShader(Handle, gs);
        _gl.LinkProgram(Handle);
        _gl.GetProgram(Handle, GLEnum.LinkStatus, out int linked);
        if (linked == 0)
            throw new InvalidOperationException($"連結著色器失敗 '{name}':\n{_gl.GetProgramInfoLog(Handle)}");

        _gl.DetachShader(Handle, vs);
        _gl.DetachShader(Handle, fs);
        _gl.DeleteShader(vs);
        _gl.DeleteShader(fs);
        if (gs != 0) { _gl.DetachShader(Handle, gs); _gl.DeleteShader(gs); }
    }

    private uint Compile(ShaderType type, string src, string label)
    {
        uint h = _gl.CreateShader(type);
        _gl.ShaderSource(h, src);
        _gl.CompileShader(h);
        _gl.GetShader(h, GLEnum.CompileStatus, out int ok);
        if (ok == 0)
        {
            string log = _gl.GetShaderInfoLog(h);
            _gl.DeleteShader(h);
            throw new InvalidOperationException($"編譯著色器失敗 '{label}':\n{log}\n{NumberSource(src)}");
        }
        return h;
    }

    private static string NumberSource(string src)
    {
        var sb = new System.Text.StringBuilder();
        string[] lines = src.Replace("\r\n", "\n").Split('\n');
        for (int i = 0; i < lines.Length; i++) sb.Append(i + 1).Append(": ").Append(lines[i]).Append('\n');
        return sb.ToString();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Use() => _gl.UseProgram(Handle);

    public int Loc(string name)
    {
        if (_locations.TryGetValue(name, out int l)) return l;
        l = _gl.GetUniformLocation(Handle, name);
        _locations[name] = l;
        return l;
    }

    public void Set(string n, int v) { int l = Loc(n); if (l >= 0) _gl.Uniform1(l, v); }
    public void Set(string n, float v) { int l = Loc(n); if (l >= 0) _gl.Uniform1(l, v); }

    /// <summary>
    /// Flags are declared as <c>uniform float</c> in the shaders, so this must upload a float.
    /// Uploading an int into a float uniform is a GL error and silently leaves the old value.
    /// </summary>
    public void Set(string n, bool v) { int l = Loc(n); if (l >= 0) _gl.Uniform1(l, v ? 1f : 0f); }
    public void Set(string n, Vector2 v) { int l = Loc(n); if (l >= 0) _gl.Uniform2(l, v.X, v.Y); }
    public void Set(string n, Vector3 v) { int l = Loc(n); if (l >= 0) _gl.Uniform3(l, v.X, v.Y, v.Z); }
    public void Set(string n, Vector4 v) { int l = Loc(n); if (l >= 0) _gl.Uniform4(l, v.X, v.Y, v.Z, v.W); }

    public unsafe void Set(string n, in Matrix4x4 m)
    {
        int l = Loc(n);
        if (l < 0) return;
        fixed (Matrix4x4* p = &m) _gl.UniformMatrix4(l, 1, false, (float*)p);
    }

    public unsafe void SetArray(string n, ReadOnlySpan<Matrix4x4> m)
    {
        int l = Loc(n);
        if (l < 0 || m.Length == 0) return;
        fixed (Matrix4x4* p = m) _gl.UniformMatrix4(l, (uint)m.Length, false, (float*)p);
    }

    public unsafe void SetArray(string n, ReadOnlySpan<Vector4> v)
    {
        int l = Loc(n);
        if (l < 0 || v.Length == 0) return;
        fixed (Vector4* p = v) _gl.Uniform4(l, (uint)v.Length, (float*)p);
    }

    public unsafe void SetArray(string n, ReadOnlySpan<float> v)
    {
        int l = Loc(n);
        if (l < 0 || v.Length == 0) return;
        fixed (float* p = v) _gl.Uniform1(l, (uint)v.Length, p);
    }

    public void Dispose() => _gl.DeleteProgram(Handle);
}

/// <summary>2D texture; also serves array-free shadow maps and post-process targets.</summary>
public sealed class Texture2D : IDisposable
{
    private readonly GL _gl;
    public uint Handle { get; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public InternalFormat Format { get; }

    public unsafe Texture2D(GL gl, int width, int height, InternalFormat internalFormat,
        PixelFormat format, PixelType type, void* pixels = null, bool mipmaps = false,
        bool linear = true, bool repeat = true, int anisotropy = 0)
    {
        _gl = gl;
        Width = width; Height = height; Format = internalFormat;
        Handle = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, Handle);
        gl.TexImage2D(TextureTarget.Texture2D, 0, internalFormat, (uint)width, (uint)height, 0, format, type, pixels);

        int min = linear ? (mipmaps ? (int)GLEnum.LinearMipmapLinear : (int)GLEnum.Linear) : (int)GLEnum.Nearest;
        int mag = linear ? (int)GLEnum.Linear : (int)GLEnum.Nearest;
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, min);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, mag);
        int wrap = repeat ? (int)GLEnum.Repeat : (int)GLEnum.ClampToEdge;
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, wrap);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, wrap);
        if (mipmaps) gl.GenerateMipmap(TextureTarget.Texture2D);
        if (anisotropy > 1)
            gl.TexParameter(TextureTarget.Texture2D, (TextureParameterName)GLEnum.TextureMaxAnisotropy, (float)anisotropy);
        gl.BindTexture(TextureTarget.Texture2D, 0);
    }

    public static unsafe Texture2D FromRgba(GL gl, int w, int h, ReadOnlySpan<byte> rgba, bool mipmaps = true,
        bool srgb = false, int anisotropy = 8)
    {
        fixed (byte* p = rgba)
            return new Texture2D(gl, w, h, srgb ? InternalFormat.Srgb8Alpha8 : InternalFormat.Rgba8,
                PixelFormat.Rgba, PixelType.UnsignedByte, p, mipmaps, true, true, anisotropy);
    }

    /// <summary>Reallocates storage (post-process targets on window resize).</summary>
    public unsafe void Resize(int w, int h, PixelFormat format, PixelType type)
    {
        if (w == Width && h == Height) return;
        Width = w; Height = h;
        _gl.BindTexture(TextureTarget.Texture2D, Handle);
        _gl.TexImage2D(TextureTarget.Texture2D, 0, Format, (uint)w, (uint)h, 0, format, type, null);
        _gl.BindTexture(TextureTarget.Texture2D, 0);
    }

    public unsafe void SubImage(int x, int y, int w, int h, ReadOnlySpan<byte> data, PixelFormat format)
    {
        _gl.BindTexture(TextureTarget.Texture2D, Handle);
        _gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        fixed (byte* p = data)
            _gl.TexSubImage2D(TextureTarget.Texture2D, 0, x, y, (uint)w, (uint)h, format, PixelType.UnsignedByte, p);
        _gl.BindTexture(TextureTarget.Texture2D, 0);
    }

    public void SetBorderClamp(Vector4 border)
    {
        _gl.BindTexture(TextureTarget.Texture2D, Handle);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToBorder);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToBorder);
        Span<float> b = [border.X, border.Y, border.Z, border.W];
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureBorderColor, b);
        _gl.BindTexture(TextureTarget.Texture2D, 0);
    }

    public void Bind(int unit)
    {
        _gl.ActiveTexture(TextureUnit.Texture0 + unit);
        _gl.BindTexture(TextureTarget.Texture2D, Handle);
    }

    public void Dispose() => _gl.DeleteTexture(Handle);
}

/// <summary>Cube map, used for the environment/skybox and cheap specular IBL.</summary>
public sealed class TextureCube : IDisposable
{
    private readonly GL _gl;
    public uint Handle { get; }
    public int Size { get; }

    public unsafe TextureCube(GL gl, int size, byte[][] faces, bool mipmaps = true)
    {
        _gl = gl; Size = size;
        Handle = gl.GenTexture();
        gl.BindTexture(TextureTarget.TextureCubeMap, Handle);
        for (int i = 0; i < 6; i++)
        {
            fixed (byte* p = faces[i])
                gl.TexImage2D(TextureTarget.TextureCubeMapPositiveX + i, 0, InternalFormat.Rgba8,
                    (uint)size, (uint)size, 0, PixelFormat.Rgba, PixelType.UnsignedByte, p);
        }
        gl.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureMinFilter,
            mipmaps ? (int)GLEnum.LinearMipmapLinear : (int)GLEnum.Linear);
        gl.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        gl.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        gl.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
        gl.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapR, (int)GLEnum.ClampToEdge);
        if (mipmaps) gl.GenerateMipmap(TextureTarget.TextureCubeMap);
        gl.BindTexture(TextureTarget.TextureCubeMap, 0);
    }

    public void Bind(int unit)
    {
        _gl.ActiveTexture(TextureUnit.Texture0 + unit);
        _gl.BindTexture(TextureTarget.TextureCubeMap, Handle);
    }

    public void Dispose() => _gl.DeleteTexture(Handle);
}

/// <summary>VAO + VBO (+ optional EBO). Supports static upload and per-frame dynamic streaming.</summary>
public sealed class Mesh : IDisposable
{
    private readonly GL _gl;
    private uint _vao, _vbo, _ebo;
    private readonly int _stride;
    private int _vboCapacityBytes;
    private int _eboCapacityBytes;

    public int IndexCount { get; private set; }
    public int VertexCount { get; private set; }
    public bool HasIndices => _ebo != 0;
    public PrimitiveType Primitive { get; set; } = PrimitiveType.Triangles;

    public unsafe Mesh(GL gl, VertexAttrib[] layout, int stride, bool dynamic = false)
    {
        _gl = gl;
        _stride = stride;
        _vao = gl.GenVertexArray();
        _vbo = gl.GenBuffer();
        gl.BindVertexArray(_vao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        foreach (var a in layout)
        {
            gl.EnableVertexAttribArray(a.Index);
            if (a.AsInteger)
                gl.VertexAttribIPointer(a.Index, a.Size, (VertexAttribIType)a.Type, (uint)stride, (void*)a.Offset);
            else
                gl.VertexAttribPointer(a.Index, a.Size, a.Type, a.Normalized, (uint)stride, (void*)a.Offset);
        }
        gl.BindVertexArray(0);
        _ = dynamic;
    }

    public static Mesh CreateStatic<T>(GL gl, ReadOnlySpan<T> vertices, ReadOnlySpan<uint> indices,
        VertexAttrib[] layout) where T : unmanaged
    {
        var m = new Mesh(gl, layout, Unsafe.SizeOf<T>());
        m.Upload(vertices, indices, BufferUsageARB.StaticDraw);
        return m;
    }

    public unsafe void Upload<T>(ReadOnlySpan<T> vertices, ReadOnlySpan<uint> indices,
        BufferUsageARB usage = BufferUsageARB.DynamicDraw) where T : unmanaged
    {
        VertexCount = vertices.Length;
        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        int vbytes = vertices.Length * Unsafe.SizeOf<T>();
        if (vbytes > _vboCapacityBytes)
        {
            _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)vbytes, vertices, usage);
            _vboCapacityBytes = vbytes;
        }
        else if (vbytes > 0)
        {
            // Orphan the old store so the driver never stalls waiting on in-flight draws.
            _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)_vboCapacityBytes, (void*)null, usage);
            _gl.BufferSubData(BufferTargetARB.ArrayBuffer, 0, (nuint)vbytes, vertices);
        }

        IndexCount = indices.Length;
        if (indices.Length > 0)
        {
            if (_ebo == 0) _ebo = _gl.GenBuffer();
            _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ebo);
            int ibytes = indices.Length * sizeof(uint);
            if (ibytes > _eboCapacityBytes)
            {
                _gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)ibytes, indices, usage);
                _eboCapacityBytes = ibytes;
            }
            else
            {
                _gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)_eboCapacityBytes, (void*)null, usage);
                _gl.BufferSubData(BufferTargetARB.ElementArrayBuffer, 0, (nuint)ibytes, indices);
            }
        }
        _gl.BindVertexArray(0);
    }

    public unsafe void Draw()
    {
        if (VertexCount == 0) return;
        _gl.BindVertexArray(_vao);
        if (IndexCount > 0)
            _gl.DrawElements(Primitive, (uint)IndexCount, DrawElementsType.UnsignedInt, (void*)0);
        else
            _gl.DrawArrays(Primitive, 0, (uint)VertexCount);
    }

    public unsafe void DrawRange(int indexOffset, int indexCount)
    {
        if (indexCount <= 0) return;
        _gl.BindVertexArray(_vao);
        _gl.DrawElements(Primitive, (uint)indexCount, DrawElementsType.UnsignedInt, (void*)(indexOffset * sizeof(uint)));
    }

    public void Dispose()
    {
        _gl.DeleteVertexArray(_vao);
        _gl.DeleteBuffer(_vbo);
        if (_ebo != 0) _gl.DeleteBuffer(_ebo);
        _vao = _vbo = _ebo = 0;
    }
}

/// <summary>Render target with up to 4 color attachments plus optional depth texture or renderbuffer.</summary>
public sealed class Framebuffer : IDisposable
{
    private readonly GL _gl;
    public uint Handle { get; private set; }
    public Texture2D[] Color { get; private set; }
    public Texture2D DepthTexture { get; private set; }
    private uint _depthRb;
    public int Width { get; private set; }
    public int Height { get; private set; }

    private readonly (InternalFormat i, PixelFormat f, PixelType t)[] _colorSpec;
    private readonly bool _depthAsTexture;
    private readonly bool _hasDepth;
    private readonly bool _linear;

    public Framebuffer(GL gl, int width, int height,
        (InternalFormat, PixelFormat, PixelType)[] colorSpec,
        bool depth = true, bool depthAsTexture = false, bool linear = true)
    {
        _gl = gl;
        _colorSpec = colorSpec;
        _hasDepth = depth;
        _depthAsTexture = depthAsTexture;
        _linear = linear;
        Width = Math.Max(1, width); Height = Math.Max(1, height);
        Build();
    }

    private unsafe void Build()
    {
        Handle = _gl.GenFramebuffer();
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, Handle);

        Color = new Texture2D[_colorSpec.Length];
        Span<GLEnum> drawBufs = stackalloc GLEnum[Math.Max(1, _colorSpec.Length)];
        for (int i = 0; i < _colorSpec.Length; i++)
        {
            var (inf, fmt, typ) = _colorSpec[i];
            Color[i] = new Texture2D(_gl, Width, Height, inf, fmt, typ, null, false, _linear, false);
            _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer,
                FramebufferAttachment.ColorAttachment0 + i, TextureTarget.Texture2D, Color[i].Handle, 0);
            drawBufs[i] = GLEnum.ColorAttachment0 + i;
        }

        if (_colorSpec.Length == 0)
        {
            _gl.DrawBuffer(DrawBufferMode.None);
            _gl.ReadBuffer(ReadBufferMode.None);
        }
        else
        {
            fixed (GLEnum* p = drawBufs) _gl.DrawBuffers((uint)_colorSpec.Length, p);
        }

        if (_hasDepth)
        {
            if (_depthAsTexture)
            {
                DepthTexture = new Texture2D(_gl, Width, Height, InternalFormat.DepthComponent24,
                    PixelFormat.DepthComponent, PixelType.Float, null, false, _linear, false);
                _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment,
                    TextureTarget.Texture2D, DepthTexture.Handle, 0);
            }
            else
            {
                _depthRb = _gl.GenRenderbuffer();
                _gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _depthRb);
                _gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.DepthComponent24,
                    (uint)Width, (uint)Height);
                _gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment,
                    RenderbufferTarget.Renderbuffer, _depthRb);
            }
        }

        var status = (GLEnum)_gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (status != GLEnum.FramebufferComplete)
            throw new InvalidOperationException($"畫格緩衝區不完整: {status}");
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    public void Resize(int width, int height)
    {
        width = Math.Max(1, width); height = Math.Max(1, height);
        if (width == Width && height == Height) return;
        DisposeResources();
        Width = width; Height = height;
        Build();
    }

    public void Bind(bool setViewport = true)
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, Handle);
        if (setViewport) _gl.Viewport(0, 0, (uint)Width, (uint)Height);
    }

    public static void BindDefault(GL gl) => gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);

    private void DisposeResources()
    {
        if (Color != null) foreach (var c in Color) c?.Dispose();
        DepthTexture?.Dispose();
        DepthTexture = null;
        if (_depthRb != 0) { _gl.DeleteRenderbuffer(_depthRb); _depthRb = 0; }
        if (Handle != 0) { _gl.DeleteFramebuffer(Handle); Handle = 0; }
    }

    public void Dispose() => DisposeResources();
}
