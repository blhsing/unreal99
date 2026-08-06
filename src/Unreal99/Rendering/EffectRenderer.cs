using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.OpenGL;
using Unreal99.Core;

namespace Unreal99.Rendering;

/// <summary>A view-facing quad strip between two world points: tracers, shock beams, lightning.</summary>
public struct Beam
{
    public Vector3 Start;
    public Vector3 End;
    public float Width;
    public Vector4 ColorStart;
    public Vector4 ColorEnd;
    public Spr Sprite;
    public float Life;
    public float MaxLife;
    public int Segments;
    public float Jitter;
    public float ScrollSpeed;
}

/// <summary>A projected quad stuck to a surface: bullet holes, scorch marks, blood splats.</summary>
public struct Decal
{
    public Vector3 Position;
    public Vector3 Normal;
    public float Size;
    public float Rotation;
    public Vector4 Color;
    public Spr Sprite;
    public float Life;
    public float MaxLife;
}

/// <summary>
/// Draws the non-billboard transient effects: beams (rebuilt per view because they face the
/// camera) and decals (built once per frame and shared across views).
/// </summary>
public sealed class EffectRenderer : IDisposable
{
    private const int MaxBeams = 256;
    private const int MaxDecals = 512;

    private readonly GL _gl;
    private readonly Shader _shader;
    private readonly Mesh _beamMesh;
    private readonly Mesh _decalMesh;
    private readonly Texture2D _atlas;
    private readonly Rng _rng = new(0x5EED1234);

    private readonly Beam[] _beams = new Beam[MaxBeams];
    private readonly Decal[] _decals = new Decal[MaxDecals];
    private int _beamCount, _decalCount;

    private Vertex[] _vscratch = new Vertex[8192];
    private uint[] _iscratch = new uint[12288];
    private int _vcount, _icount;
    private bool _decalsDirty = true;

    public int BeamCount => _beamCount;
    public int DecalCount => _decalCount;

    public EffectRenderer(GL gl, Texture2D atlas)
    {
        _gl = gl;
        _atlas = atlas;
        _shader = new Shader(gl, "unlit", Shaders.UnlitVert, Shaders.UnlitFrag);
        _beamMesh = new Mesh(gl, VertexLayouts.Static, Marshal.SizeOf<Vertex>(), dynamic: true);
        _decalMesh = new Mesh(gl, VertexLayouts.Static, Marshal.SizeOf<Vertex>(), dynamic: true);
    }

    // ---------------------------------------------------------------- spawning

    public void AddBeam(Vector3 start, Vector3 end, float width, Vector4 colorStart, Vector4 colorEnd,
        float life, Spr sprite = Spr.Puff, int segments = 1, float jitter = 0f, float scrollSpeed = 0f)
    {
        if (_beamCount >= MaxBeams) return;
        _beams[_beamCount++] = new Beam
        {
            Start = start, End = end, Width = width,
            ColorStart = colorStart, ColorEnd = colorEnd,
            Sprite = sprite, Life = life, MaxLife = life,
            Segments = Math.Max(1, segments), Jitter = jitter, ScrollSpeed = scrollSpeed,
        };
    }

    /// <summary>Thin fading streak left by hitscan weapons.</summary>
    public void AddTracer(Vector3 start, Vector3 end, Vector3 color, float width = 0.035f, float life = 0.09f)
        => AddBeam(start, end, width, new Vector4(color * 3.2f, 1f), new Vector4(color * 0.5f, 0f), life, Spr.Puff);

    /// <summary>Crackling forked bolt, used by the shock combo and the pulse gun's beam.</summary>
    public void AddLightning(Vector3 start, Vector3 end, Vector3 color, float width = 0.12f, float life = 0.12f)
        => AddBeam(start, end, width, new Vector4(color * 4.5f, 1f), new Vector4(color * 0.8f, 0f),
            life, Spr.Bolt, segments: 10, jitter: 0.22f, scrollSpeed: 12f);

    public void AddDecal(Vector3 pos, Vector3 normal, float size, Vector4 color, Spr sprite, float life = 22f)
    {
        if (_decalCount >= MaxDecals)
        {
            // Recycle the oldest slot rather than dropping the newest hit.
            int oldest = 0;
            float best = float.MaxValue;
            for (int i = 0; i < _decalCount; i++) if (_decals[i].Life < best) { best = _decals[i].Life; oldest = i; }
            _decalCount = oldest;
        }
        _decals[_decalCount++] = new Decal
        {
            Position = pos + normal * 0.012f,
            Normal = MathX.SafeNormalize(normal, MathX.Up),
            Size = size,
            Rotation = _rng.Range(0f, MathX.TwoPi),
            Color = color,
            Sprite = sprite,
            Life = life,
            MaxLife = life,
        };
        _decalsDirty = true;
    }

    public void AddBulletHole(Vector3 pos, Vector3 normal, float size = 0.13f)
        => AddDecal(pos, normal, size, new Vector4(1f, 1f, 1f, 0.92f), Spr.BulletHole);

    public void AddScorch(Vector3 pos, Vector3 normal, float size = 1.0f)
        => AddDecal(pos, normal, size, new Vector4(1f, 1f, 1f, 0.85f), Spr.Scorch);

    public void AddBloodSplat(Vector3 pos, Vector3 normal, float size = 0.5f)
        => AddDecal(pos, normal, size, new Vector4(0.115f, 0.010f, 0.010f, 0.72f), Spr.Blood, 16f);

    // ---------------------------------------------------------------- simulation

    public void Update(float dt)
    {
        int w = 0;
        for (int i = 0; i < _beamCount; i++)
        {
            _beams[i].Life -= dt;
            if (_beams[i].Life <= 0f) continue;
            if (w != i) _beams[w] = _beams[i];
            w++;
        }
        _beamCount = w;

        w = 0;
        for (int i = 0; i < _decalCount; i++)
        {
            _decals[i].Life -= dt;
            if (_decals[i].Life <= 0f) { _decalsDirty = true; continue; }
            if (w != i) { _decals[w] = _decals[i]; _decalsDirty = true; }
            w++;
        }
        if (w != _decalCount) _decalsDirty = true;
        _decalCount = w;
    }

    public void Clear()
    {
        _beamCount = 0;
        _decalCount = 0;
        _decalsDirty = true;
    }

    // ---------------------------------------------------------------- geometry building

    private void ResetScratch()
    {
        _vcount = 0;
        _icount = 0;
    }

    private void EnsureScratch(int verts, int inds)
    {
        if (_vcount + verts > _vscratch.Length) Array.Resize(ref _vscratch, Math.Max(_vscratch.Length * 2, _vcount + verts));
        if (_icount + inds > _iscratch.Length) Array.Resize(ref _iscratch, Math.Max(_iscratch.Length * 2, _icount + inds));
    }

    private static void AtlasUv(Spr sprite, out Vector2 uv0, out Vector2 uv1)
    {
        const float cols = 4f;
        int idx = (int)sprite;
        float cx = idx % 4, cy = idx / 4;
        uv0 = new Vector2(cx / cols, cy / cols);
        uv1 = new Vector2((cx + 1f) / cols, (cy + 1f) / cols);
    }

    private void PushQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 uv0, Vector2 uv1,
        uint ca, uint cb, uint cc, uint cd)
    {
        EnsureScratch(4, 6);
        uint bi = (uint)_vcount;
        Vector3 n = MathX.SafeNormalize(Vector3.Cross(b - a, d - a), MathX.Up);
        _vscratch[_vcount++] = new Vertex { Position = a, Normal = n, Tangent = new Vector4(1, 0, 0, 1), Uv = new Vector2(uv0.X, uv0.Y), Color = ca };
        _vscratch[_vcount++] = new Vertex { Position = b, Normal = n, Tangent = new Vector4(1, 0, 0, 1), Uv = new Vector2(uv1.X, uv0.Y), Color = cb };
        _vscratch[_vcount++] = new Vertex { Position = c, Normal = n, Tangent = new Vector4(1, 0, 0, 1), Uv = new Vector2(uv1.X, uv1.Y), Color = cc };
        _vscratch[_vcount++] = new Vertex { Position = d, Normal = n, Tangent = new Vector4(1, 0, 0, 1), Uv = new Vector2(uv0.X, uv1.Y), Color = cd };
        _iscratch[_icount++] = bi; _iscratch[_icount++] = bi + 1; _iscratch[_icount++] = bi + 2;
        _iscratch[_icount++] = bi; _iscratch[_icount++] = bi + 2; _iscratch[_icount++] = bi + 3;
    }

    private static uint Pack(Vector4 c) => MeshBuilder.PackColor(c.X, c.Y, c.Z, c.W);

    /// <summary>Builds beam geometry facing <paramref name="camera"/>. Must run per view.</summary>
    public void RenderBeams(in Camera camera, float time)
    {
        if (_beamCount == 0) return;
        ResetScratch();

        for (int i = 0; i < _beamCount; i++)
        {
            ref Beam bm = ref _beams[i];
            float t = 1f - MathX.Saturate(bm.Life / MathF.Max(bm.MaxLife, 1e-4f));
            Vector4 col = Vector4.Lerp(bm.ColorStart, bm.ColorEnd, t);
            if (col.W <= 0.003f) continue;

            AtlasUv(bm.Sprite, out Vector2 uv0, out Vector2 uv1);
            Vector3 axis = bm.End - bm.Start;
            float len = axis.Length();
            if (len < 1e-4f) continue;
            axis /= len;

            uint packed = Pack(col);
            var rng = new Rng((uint)(i * 2654435761u + 17u));

            Vector3 prev = bm.Start;
            for (int s = 0; s < bm.Segments; s++)
            {
                float f0 = s / (float)bm.Segments;
                float f1 = (s + 1) / (float)bm.Segments;
                Vector3 p0 = prev;
                Vector3 p1 = Vector3.Lerp(bm.Start, bm.End, f1);

                if (bm.Jitter > 0f && s < bm.Segments - 1)
                {
                    MathX.OrthoBasis(axis, out Vector3 jt, out Vector3 jb);
                    // Phase the jitter on time so the bolt visibly crackles.
                    float ph = time * bm.ScrollSpeed + s * 1.7f;
                    float amp = bm.Jitter * MathF.Sin(f1 * MathX.Pi);
                    p1 += jt * (MathF.Sin(ph) * amp + rng.Symmetric(amp * 0.4f))
                        + jb * (MathF.Cos(ph * 1.3f) * amp + rng.Symmetric(amp * 0.4f));
                }

                Vector3 segDir = MathX.SafeNormalize(p1 - p0, axis);
                Vector3 toCam = MathX.SafeNormalize(camera.Position - (p0 + p1) * 0.5f, camera.Forward);
                Vector3 side = MathX.SafeNormalize(Vector3.Cross(segDir, toCam), camera.Right) * (bm.Width * 0.5f);

                PushQuad(p0 - side, p1 - side, p1 + side, p0 + side,
                    new Vector2(uv0.X, MathX.Lerp(uv0.Y, uv1.Y, f0)),
                    new Vector2(uv1.X, MathX.Lerp(uv0.Y, uv1.Y, f1)),
                    packed, packed, packed, packed);
                prev = p1;
            }
        }

        if (_icount == 0) return;
        _beamMesh.Upload<Vertex>(_vscratch.AsSpan(0, _vcount), _iscratch.AsSpan(0, _icount), BufferUsageARB.StreamDraw);

        _shader.Use();
        _shader.Set("uModel", Matrix4x4.Identity);
        _shader.Set("uViewProj", camera.ViewProj);
        _shader.Set("uTint", Vector4.One);
        _shader.Set("uUseTexture", 1f);
        _shader.Set("uTex", 0);
        _shader.Set("uCamPos", camera.Position);
        _shader.Set("uFadeDistance", 0f);
        _atlas.Bind(0);

        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.One);
        _gl.DepthMask(false);
        _gl.Disable(EnableCap.CullFace);
        _beamMesh.Draw();
        _gl.DepthMask(true);
        _gl.Disable(EnableCap.Blend);
    }

    /// <summary>Builds and draws decals. Geometry is cached until a decal is added or expires.</summary>
    public void RenderDecals(in Camera camera)
    {
        if (_decalCount == 0) return;

        if (_decalsDirty)
        {
            ResetScratch();
            for (int i = 0; i < _decalCount; i++)
            {
                ref Decal d = ref _decals[i];
                AtlasUv(d.Sprite, out Vector2 uv0, out Vector2 uv1);
                MathX.OrthoBasis(d.Normal, out Vector3 t, out Vector3 b);
                float c = MathF.Cos(d.Rotation), s = MathF.Sin(d.Rotation);
                Vector3 r = (t * c + b * s) * (d.Size * 0.5f);
                Vector3 u = (b * c - t * s) * (d.Size * 0.5f);
                uint packed = Pack(d.Color);
                PushQuad(d.Position - r - u, d.Position + r - u, d.Position + r + u, d.Position - r + u,
                    uv0, uv1, packed, packed, packed, packed);
            }
            if (_icount > 0)
                _decalMesh.Upload<Vertex>(_vscratch.AsSpan(0, _vcount), _iscratch.AsSpan(0, _icount), BufferUsageARB.DynamicDraw);
            _decalsDirty = false;
        }

        if (_decalMesh.IndexCount == 0) return;

        _shader.Use();
        _shader.Set("uModel", Matrix4x4.Identity);
        _shader.Set("uViewProj", camera.ViewProj);
        _shader.Set("uTint", Vector4.One);
        _shader.Set("uUseTexture", 1f);
        _shader.Set("uTex", 0);
        _shader.Set("uCamPos", camera.Position);
        _shader.Set("uFadeDistance", 0f);
        _atlas.Bind(0);

        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        _gl.DepthMask(false);
        _gl.Enable(EnableCap.PolygonOffsetFill);
        // Pull decals toward the camera so they never z-fight with the surface they sit on.
        _gl.PolygonOffset(-2.5f, -3.0f);
        _gl.Disable(EnableCap.CullFace);
        _decalMesh.Draw();
        _gl.Disable(EnableCap.PolygonOffsetFill);
        _gl.PolygonOffset(0f, 0f);
        _gl.DepthMask(true);
        _gl.Disable(EnableCap.Blend);
    }

    public void Dispose()
    {
        _shader.Dispose();
        _beamMesh.Dispose();
        _decalMesh.Dispose();
    }
}
