using System.Numerics;
using Unreal99.Core;

namespace Unreal99.Rendering;

/// <summary>A contiguous run of indices that share one material.</summary>
public readonly record struct MeshSection(int Material, int IndexOffset, int IndexCount);

/// <summary>
/// Builds procedural geometry. Everything in the game — arenas, weapons, characters, pickups —
/// is generated through here, so there are no external art assets to ship.
/// Indices are grouped per material so a whole level draws as one VBO with a handful of draw calls.
/// </summary>
public sealed class MeshBuilder
{
    private readonly List<Vertex> _vertices = new(4096);
    private readonly Dictionary<int, List<uint>> _indicesByMaterial = new();
    private readonly Stack<Matrix4x4> _stack = new();

    public Matrix4x4 Transform = Matrix4x4.Identity;
    public int Material;
    public uint Color = 0xFFFFFFFF;
    public Vector2 UvScale = Vector2.One;
    public Vector2 UvOffset = Vector2.Zero;
    /// <summary>When true, UVs are derived from world-space position so tiling stays consistent across brushes.</summary>
    public bool WorldUv = true;
    public float WorldUvScale = 1f;

    public int VertexCount => _vertices.Count;

    public void PushTransform(Matrix4x4 m)
    {
        _stack.Push(Transform);
        Transform = m * Transform;
    }

    public void PopTransform() => Transform = _stack.Pop();

    private List<uint> Indices(int material)
    {
        if (!_indicesByMaterial.TryGetValue(material, out var list))
        {
            list = new List<uint>(4096);
            _indicesByMaterial[material] = list;
        }
        return list;
    }

    public static uint PackColor(float r, float g, float b, float a)
        => (uint)(MathX.Clamp((int)(r * 255f + 0.5f), 0, 255))
         | ((uint)(MathX.Clamp((int)(g * 255f + 0.5f), 0, 255)) << 8)
         | ((uint)(MathX.Clamp((int)(b * 255f + 0.5f), 0, 255)) << 16)
         | ((uint)(MathX.Clamp((int)(a * 255f + 0.5f), 0, 255)) << 24);

    public static uint PackColor(Vector3 rgb, float ao = 1f) => PackColor(rgb.X, rgb.Y, rgb.Z, ao);

    /// <summary>White tint with a baked ambient-occlusion value in alpha.</summary>
    public static uint Ao(float ao) => PackColor(1f, 1f, 1f, ao);

    private uint AddVertex(Vector3 localPos, Vector3 localNormal, Vector3 localTangent, Vector2 uv, uint color)
    {
        Vector3 p = Vector3.Transform(localPos, Transform);
        Vector3 n = Vector3.Normalize(Vector3.TransformNormal(localNormal, Transform));
        Vector3 t = Vector3.TransformNormal(localTangent, Transform);
        t = MathX.SafeNormalize(t - n * Vector3.Dot(n, t), MathX.Right);

        _vertices.Add(new Vertex
        {
            Position = p,
            Normal = n,
            Tangent = new Vector4(t, 1f),
            Uv = uv * UvScale + UvOffset,
            Color = color,
        });
        return (uint)(_vertices.Count - 1);
    }

    private void Tri(uint a, uint b, uint c)
    {
        var list = Indices(Material);
        list.Add(a); list.Add(b); list.Add(c);
    }

    private void Quad(uint a, uint b, uint c, uint d)
    {
        Tri(a, b, c);
        Tri(a, c, d);
    }

    /// <summary>Projects a world position to UV on the plane whose dominant axis is <paramref name="normal"/>.</summary>
    private Vector2 PlanarUv(Vector3 localPos, Vector3 normal, Vector2 fallback)
    {
        if (!WorldUv) return fallback;
        Vector3 w = Vector3.Transform(localPos, Transform) * WorldUvScale;
        Vector3 n = Vector3.TransformNormal(normal, Transform);
        float ax = MathF.Abs(n.X), ay = MathF.Abs(n.Y), az = MathF.Abs(n.Z);
        if (ay >= ax && ay >= az) return new Vector2(w.X, w.Z);
        if (ax >= az) return new Vector2(w.Z, -w.Y);
        return new Vector2(w.X, -w.Y);
    }

    // ------------------------------------------------------------------ primitives

    /// <summary>Axis-aligned (in local space) box centred on <paramref name="center"/>.</summary>
    public void AddBox(Vector3 center, Vector3 halfExtents, uint? color = null)
    {
        uint col = color ?? Color;
        Vector3 h = halfExtents;

        // +X, -X, +Y, -Y, +Z, -Z
        Span<Vector3> normals =
        [
            new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0), new(0, 0, 1), new(0, 0, -1)
        ];
        Span<Vector3> tangents =
        [
            new(0, 0, -1), new(0, 0, 1), new(1, 0, 0), new(1, 0, 0), new(1, 0, 0), new(-1, 0, 0)
        ];

        for (int f = 0; f < 6; f++)
        {
            Vector3 n = normals[f];
            Vector3 t = tangents[f];
            Vector3 b = Vector3.Cross(n, t);
            Vector3 faceCenter = center + n * Vector3.Dot(h, new Vector3(MathF.Abs(n.X), MathF.Abs(n.Y), MathF.Abs(n.Z)));
            float su = MathF.Abs(Vector3.Dot(h, new Vector3(MathF.Abs(t.X), MathF.Abs(t.Y), MathF.Abs(t.Z))));
            float sv = MathF.Abs(Vector3.Dot(h, new Vector3(MathF.Abs(b.X), MathF.Abs(b.Y), MathF.Abs(b.Z))));

            Vector3 p0 = faceCenter - t * su - b * sv;
            Vector3 p1 = faceCenter + t * su - b * sv;
            Vector3 p2 = faceCenter + t * su + b * sv;
            Vector3 p3 = faceCenter - t * su + b * sv;

            uint i0 = AddVertex(p0, n, t, PlanarUv(p0, n, new Vector2(0, 0)), col);
            uint i1 = AddVertex(p1, n, t, PlanarUv(p1, n, new Vector2(su * 2, 0)), col);
            uint i2 = AddVertex(p2, n, t, PlanarUv(p2, n, new Vector2(su * 2, sv * 2)), col);
            uint i3 = AddVertex(p3, n, t, PlanarUv(p3, n, new Vector2(0, sv * 2)), col);
            Quad(i0, i1, i2, i3);
        }
    }

    /// <summary>Box given by min/max corners.</summary>
    public void AddBoxMinMax(Vector3 min, Vector3 max, uint? color = null)
        => AddBox((min + max) * 0.5f, (max - min) * 0.5f, color);

    /// <summary>
    /// A ramp: a box with the +Y face sloped down along one horizontal axis.
    /// <paramref name="risingAxis"/> 0 = +X is high, 1 = -X, 2 = +Z, 3 = -Z.
    /// </summary>
    public void AddRamp(Vector3 min, Vector3 max, int risingAxis, uint? color = null)
    {
        uint col = color ?? Color;
        float lowY = min.Y, highY = max.Y;

        // Corner heights, indexed by (x,z) corner: 00, 10, 11, 01
        float h00, h10, h11, h01;
        switch (risingAxis)
        {
            case 0: h00 = lowY; h01 = lowY; h10 = highY; h11 = highY; break;   // +X high
            case 1: h00 = highY; h01 = highY; h10 = lowY; h11 = lowY; break;   // -X high
            case 2: h00 = lowY; h10 = lowY; h01 = highY; h11 = highY; break;   // +Z high
            default: h00 = highY; h10 = highY; h01 = lowY; h11 = lowY; break;  // -Z high
        }

        Vector3 a = new(min.X, h00, min.Z);
        Vector3 b = new(max.X, h10, min.Z);
        Vector3 c = new(max.X, h11, max.Z);
        Vector3 d = new(min.X, h01, max.Z);
        Vector3 a0 = new(min.X, min.Y, min.Z);
        Vector3 b0 = new(max.X, min.Y, min.Z);
        Vector3 c0 = new(max.X, min.Y, max.Z);
        Vector3 d0 = new(min.X, min.Y, max.Z);

        AddPolygon([a, b, c, d], col);          // sloped top
        AddPolygon([d0, c0, b0, a0], col);      // bottom
        AddPolygon([a0, b0, b, a], col);        // -Z side
        AddPolygon([c0, d0, d, c], col);        // +Z side
        AddPolygon([b0, c0, c, b], col);        // +X side
        AddPolygon([d0, a0, a, d], col);        // -X side
    }

    /// <summary>Convex polygon, wound counter-clockwise when viewed from the front.</summary>
    public void AddPolygon(ReadOnlySpan<Vector3> pts, uint? color = null)
    {
        if (pts.Length < 3) return;
        uint col = color ?? Color;
        Vector3 n = Vector3.Zero;
        for (int i = 0; i < pts.Length; i++)
        {
            Vector3 cur = pts[i], next = pts[(i + 1) % pts.Length];
            n.X += (cur.Y - next.Y) * (cur.Z + next.Z);
            n.Y += (cur.Z - next.Z) * (cur.X + next.X);
            n.Z += (cur.X - next.X) * (cur.Y + next.Y);
        }
        n = MathX.SafeNormalize(n, MathX.Up);
        MathX.OrthoBasis(n, out Vector3 t, out _);

        Span<uint> idx = pts.Length <= 16 ? stackalloc uint[pts.Length] : new uint[pts.Length];
        for (int i = 0; i < pts.Length; i++)
            idx[i] = AddVertex(pts[i], n, t, PlanarUv(pts[i], n, new Vector2(pts[i].X, pts[i].Z)), col);
        for (int i = 1; i < pts.Length - 1; i++)
            Tri(idx[0], idx[i], idx[i + 1]);
    }

    /// <summary>Cylinder along +Y, centred at <paramref name="center"/>.</summary>
    public void AddCylinder(Vector3 center, float radiusBottom, float radiusTop, float height,
        int segments = 16, bool caps = true, uint? color = null)
    {
        uint col = color ?? Color;
        float hy = height * 0.5f;
        segments = Math.Max(3, segments);

        for (int i = 0; i < segments; i++)
        {
            float a0 = i / (float)segments * MathX.TwoPi;
            float a1 = (i + 1) / (float)segments * MathX.TwoPi;
            Vector3 d0 = new(MathF.Cos(a0), 0, MathF.Sin(a0));
            Vector3 d1 = new(MathF.Cos(a1), 0, MathF.Sin(a1));

            Vector3 p0 = center + d0 * radiusBottom + new Vector3(0, -hy, 0);
            Vector3 p1 = center + d1 * radiusBottom + new Vector3(0, -hy, 0);
            Vector3 p2 = center + d1 * radiusTop + new Vector3(0, hy, 0);
            Vector3 p3 = center + d0 * radiusTop + new Vector3(0, hy, 0);

            // Slanted side normal accounts for a conical profile.
            float slope = (radiusBottom - radiusTop) / MathF.Max(height, 1e-4f);
            Vector3 n0 = Vector3.Normalize(new Vector3(d0.X, slope, d0.Z));
            Vector3 n1 = Vector3.Normalize(new Vector3(d1.X, slope, d1.Z));
            Vector3 t0 = Vector3.Cross(MathX.Up, n0);
            Vector3 t1 = Vector3.Cross(MathX.Up, n1);

            float u0 = i / (float)segments * MathX.TwoPi * MathF.Max(radiusBottom, radiusTop);
            float u1 = (i + 1) / (float)segments * MathX.TwoPi * MathF.Max(radiusBottom, radiusTop);

            uint v0 = AddVertex(p0, n0, t0, new Vector2(u0, -hy), col);
            uint v1 = AddVertex(p1, n1, t1, new Vector2(u1, -hy), col);
            uint v2 = AddVertex(p2, n1, t1, new Vector2(u1, hy), col);
            uint v3 = AddVertex(p3, n0, t0, new Vector2(u0, hy), col);
            Quad(v0, v1, v2, v3);

            if (caps)
            {
                if (radiusTop > 1e-4f)
                {
                    uint c0 = AddVertex(center + new Vector3(0, hy, 0), MathX.Up, MathX.Right, new Vector2(0, 0), col);
                    uint c1 = AddVertex(p3, MathX.Up, MathX.Right, new Vector2(p3.X, p3.Z), col);
                    uint c2 = AddVertex(p2, MathX.Up, MathX.Right, new Vector2(p2.X, p2.Z), col);
                    Tri(c0, c1, c2);
                }
                if (radiusBottom > 1e-4f)
                {
                    uint b0 = AddVertex(center + new Vector3(0, -hy, 0), MathX.Down, MathX.Right, new Vector2(0, 0), col);
                    uint b1 = AddVertex(p1, MathX.Down, MathX.Right, new Vector2(p1.X, p1.Z), col);
                    uint b2 = AddVertex(p0, MathX.Down, MathX.Right, new Vector2(p0.X, p0.Z), col);
                    Tri(b0, b1, b2);
                }
            }
        }
    }

    /// <summary>UV sphere.</summary>
    public void AddSphere(Vector3 center, float radius, int rings = 12, int segments = 18, uint? color = null)
    {
        uint col = color ?? Color;
        rings = Math.Max(3, rings); segments = Math.Max(3, segments);
        int baseIdx = _vertices.Count;

        for (int r = 0; r <= rings; r++)
        {
            float v = r / (float)rings;
            float phi = v * MathX.Pi;
            float sy = MathF.Cos(phi), sr = MathF.Sin(phi);
            for (int s = 0; s <= segments; s++)
            {
                float u = s / (float)segments;
                float theta = u * MathX.TwoPi;
                Vector3 n = new(sr * MathF.Cos(theta), sy, sr * MathF.Sin(theta));
                Vector3 t = new(-MathF.Sin(theta), 0, MathF.Cos(theta));
                AddVertex(center + n * radius, n, t, new Vector2(u * MathX.TwoPi * radius, v * MathX.Pi * radius), col);
            }
        }
        int stride = segments + 1;
        for (int r = 0; r < rings; r++)
        {
            for (int s = 0; s < segments; s++)
            {
                uint i0 = (uint)(baseIdx + r * stride + s);
                uint i1 = (uint)(baseIdx + r * stride + s + 1);
                uint i2 = (uint)(baseIdx + (r + 1) * stride + s + 1);
                uint i3 = (uint)(baseIdx + (r + 1) * stride + s);
                Quad(i0, i1, i2, i3);
            }
        }
    }

    /// <summary>Capsule along +Y (two hemispheres plus a cylindrical body).</summary>
    public void AddCapsule(Vector3 center, float radius, float cylinderHeight, int rings = 8, int segments = 14, uint? color = null)
    {
        uint col = color ?? Color;
        float hy = cylinderHeight * 0.5f;
        AddCylinder(center, radius, radius, cylinderHeight, segments, false, col);

        for (int hemi = 0; hemi < 2; hemi++)
        {
            float sign = hemi == 0 ? 1f : -1f;
            Vector3 origin = center + new Vector3(0, sign * hy, 0);
            int baseIdx = _vertices.Count;
            for (int r = 0; r <= rings; r++)
            {
                float v = r / (float)rings * MathX.HalfPi;
                float sy = MathF.Sin(v) * sign, sr = MathF.Cos(v);
                for (int s = 0; s <= segments; s++)
                {
                    float u = s / (float)segments;
                    float theta = u * MathX.TwoPi;
                    Vector3 n = new(sr * MathF.Cos(theta), sy, sr * MathF.Sin(theta));
                    Vector3 t = new(-MathF.Sin(theta), 0, MathF.Cos(theta));
                    AddVertex(origin + n * radius, n, t, new Vector2(u * 6.283f * radius, sy * radius), col);
                }
            }
            int stride = segments + 1;
            for (int r = 0; r < rings; r++)
            {
                for (int s = 0; s < segments; s++)
                {
                    uint i0 = (uint)(baseIdx + r * stride + s);
                    uint i1 = (uint)(baseIdx + r * stride + s + 1);
                    uint i2 = (uint)(baseIdx + (r + 1) * stride + s + 1);
                    uint i3 = (uint)(baseIdx + (r + 1) * stride + s);
                    if (hemi == 0) Quad(i0, i1, i2, i3);
                    else Quad(i3, i2, i1, i0);
                }
            }
        }
    }

    /// <summary>Torus in the XZ plane.</summary>
    public void AddTorus(Vector3 center, float majorRadius, float minorRadius, int major = 24, int minor = 10, uint? color = null)
    {
        uint col = color ?? Color;
        int baseIdx = _vertices.Count;
        for (int i = 0; i <= major; i++)
        {
            float u = i / (float)major * MathX.TwoPi;
            Vector3 dir = new(MathF.Cos(u), 0, MathF.Sin(u));
            Vector3 ringCenter = center + dir * majorRadius;
            for (int j = 0; j <= minor; j++)
            {
                float v = j / (float)minor * MathX.TwoPi;
                Vector3 n = dir * MathF.Cos(v) + MathX.Up * MathF.Sin(v);
                Vector3 t = new(-MathF.Sin(u), 0, MathF.Cos(u));
                AddVertex(ringCenter + n * minorRadius, n, t,
                    new Vector2(i / (float)major * 4f, j / (float)minor * 1.5f), col);
            }
        }
        int stride = minor + 1;
        for (int i = 0; i < major; i++)
            for (int j = 0; j < minor; j++)
            {
                uint i0 = (uint)(baseIdx + i * stride + j);
                uint i1 = (uint)(baseIdx + (i + 1) * stride + j);
                uint i2 = (uint)(baseIdx + (i + 1) * stride + j + 1);
                uint i3 = (uint)(baseIdx + i * stride + j + 1);
                Quad(i0, i1, i2, i3);
            }
    }

    /// <summary>Regular n-gon prism along +Y; the workhorse for pillars and weapon barrels.</summary>
    public void AddPrism(Vector3 center, float radius, float height, int sides, float rotation = 0f, uint? color = null)
    {
        uint col = color ?? Color;
        float hy = height * 0.5f;
        sides = Math.Max(3, sides);
        for (int i = 0; i < sides; i++)
        {
            float a0 = rotation + i / (float)sides * MathX.TwoPi;
            float a1 = rotation + (i + 1) / (float)sides * MathX.TwoPi;
            Vector3 d0 = new(MathF.Cos(a0), 0, MathF.Sin(a0));
            Vector3 d1 = new(MathF.Cos(a1), 0, MathF.Sin(a1));
            Vector3 p0 = center + d0 * radius - new Vector3(0, hy, 0);
            Vector3 p1 = center + d1 * radius - new Vector3(0, hy, 0);
            Vector3 p2 = center + d1 * radius + new Vector3(0, hy, 0);
            Vector3 p3 = center + d0 * radius + new Vector3(0, hy, 0);
            AddPolygon([p0, p1, p2, p3], col);
        }
        // Caps
        Span<Vector3> top = new Vector3[sides];
        Span<Vector3> bottom = new Vector3[sides];
        for (int i = 0; i < sides; i++)
        {
            float a = rotation + i / (float)sides * MathX.TwoPi;
            Vector3 d = new(MathF.Cos(a), 0, MathF.Sin(a));
            top[i] = center + d * radius + new Vector3(0, hy, 0);
            bottom[sides - 1 - i] = center + d * radius - new Vector3(0, hy, 0);
        }
        AddPolygon(top, col);
        AddPolygon(bottom, col);
    }

    /// <summary>Screen-facing quad in local XY, used for decals and flat panels.</summary>
    public void AddQuad(Vector3 center, Vector3 right, Vector3 up, uint? color = null)
    {
        uint col = color ?? Color;
        Vector3 n = Vector3.Normalize(Vector3.Cross(right, up));
        Vector3 t = Vector3.Normalize(right);
        uint i0 = AddVertex(center - right - up, n, t, new Vector2(0, 0), col);
        uint i1 = AddVertex(center + right - up, n, t, new Vector2(1, 0), col);
        uint i2 = AddVertex(center + right + up, n, t, new Vector2(1, 1), col);
        uint i3 = AddVertex(center - right + up, n, t, new Vector2(0, 1), col);
        Quad(i0, i1, i2, i3);
    }

    /// <summary>Lofts a tube along a polyline; used for cables, rails and energy conduits.</summary>
    public void AddTube(ReadOnlySpan<Vector3> path, float radius, int segments = 8, uint? color = null)
    {
        if (path.Length < 2) return;
        uint col = color ?? Color;
        int baseIdx = _vertices.Count;
        for (int i = 0; i < path.Length; i++)
        {
            Vector3 dir = i == 0 ? path[1] - path[0]
                        : i == path.Length - 1 ? path[i] - path[i - 1]
                        : path[i + 1] - path[i - 1];
            dir = MathX.SafeNormalize(dir, MathX.Up);
            MathX.OrthoBasis(dir, out Vector3 t, out Vector3 b);
            for (int s = 0; s <= segments; s++)
            {
                float a = s / (float)segments * MathX.TwoPi;
                Vector3 n = t * MathF.Cos(a) + b * MathF.Sin(a);
                AddVertex(path[i] + n * radius, n, dir, new Vector2(s / (float)segments * 2f, i * 0.5f), col);
            }
        }
        int stride = segments + 1;
        for (int i = 0; i < path.Length - 1; i++)
            for (int s = 0; s < segments; s++)
            {
                uint i0 = (uint)(baseIdx + i * stride + s);
                uint i1 = (uint)(baseIdx + i * stride + s + 1);
                uint i2 = (uint)(baseIdx + (i + 1) * stride + s + 1);
                uint i3 = (uint)(baseIdx + (i + 1) * stride + s);
                Quad(i0, i1, i2, i3);
            }
    }

    // ------------------------------------------------------------------ post-processing

    /// <summary>Recomputes tangents from UV derivatives; call after building with generic polygons.</summary>
    public void RecalculateTangents()
    {
        var tan = new Vector3[_vertices.Count];
        var bitan = new Vector3[_vertices.Count];

        foreach (var list in _indicesByMaterial.Values)
        {
            for (int i = 0; i + 2 < list.Count; i += 3)
            {
                int a = (int)list[i], b = (int)list[i + 1], c = (int)list[i + 2];
                Vector3 p0 = _vertices[a].Position, p1 = _vertices[b].Position, p2 = _vertices[c].Position;
                Vector2 u0 = _vertices[a].Uv, u1 = _vertices[b].Uv, u2 = _vertices[c].Uv;
                Vector3 e1 = p1 - p0, e2 = p2 - p0;
                Vector2 d1 = u1 - u0, d2 = u2 - u0;
                float det = d1.X * d2.Y - d2.X * d1.Y;
                if (MathF.Abs(det) < 1e-8f) continue;
                float r = 1f / det;
                Vector3 t = (e1 * d2.Y - e2 * d1.Y) * r;
                Vector3 bt = (e2 * d1.X - e1 * d2.X) * r;
                tan[a] += t; tan[b] += t; tan[c] += t;
                bitan[a] += bt; bitan[b] += bt; bitan[c] += bt;
            }
        }

        for (int i = 0; i < _vertices.Count; i++)
        {
            Vector3 n = _vertices[i].Normal;
            Vector3 t = tan[i];
            if (t.LengthSquared() < 1e-10f) continue;
            t = MathX.SafeNormalize(t - n * Vector3.Dot(n, t), MathX.Right);
            float w = Vector3.Dot(Vector3.Cross(n, t), bitan[i]) < 0f ? -1f : 1f;
            var v = _vertices[i];
            v.Tangent = new Vector4(t, w);
            _vertices[i] = v;
        }
    }

    /// <summary>
    /// Bakes cheap ambient occlusion into vertex alpha by ray-marching a set of hemisphere
    /// directions against the supplied occluder boxes. Runs once at level build time.
    /// </summary>
    public void BakeAmbientOcclusion(IReadOnlyList<(Vector3 Min, Vector3 Max)> occluders, float maxDist = 3.0f, int rays = 12)
    {
        if (occluders.Count == 0) return;
        var rng = new Rng(0xA0A0BEEF);
        Span<Vector3> dirs = new Vector3[rays];
        for (int i = 0; i < rays; i++) dirs[i] = rng.OnUnitSphere();

        for (int i = 0; i < _vertices.Count; i++)
        {
            var v = _vertices[i];
            Vector3 origin = v.Position + v.Normal * 0.02f;
            int hits = 0, total = 0;
            for (int d = 0; d < rays; d++)
            {
                Vector3 dir = dirs[d];
                if (Vector3.Dot(dir, v.Normal) < 0f) dir = -dir;
                total++;
                if (RayHitsAnyBox(origin, dir, maxDist, occluders)) hits++;
            }
            float ao = total > 0 ? 1f - hits / (float)total * 0.72f : 1f;
            ao = MathX.Clamp(ao, 0.24f, 1f);
            uint existing = v.Color;
            v.Color = (existing & 0x00FFFFFFu) | ((uint)(MathX.Clamp((int)(ao * 255f), 0, 255)) << 24);
            _vertices[i] = v;
        }
    }

    private static bool RayHitsAnyBox(Vector3 o, Vector3 d, float maxDist, IReadOnlyList<(Vector3 Min, Vector3 Max)> boxes)
    {
        Vector3 inv = new(
            MathF.Abs(d.X) < 1e-6f ? 1e6f : 1f / d.X,
            MathF.Abs(d.Y) < 1e-6f ? 1e6f : 1f / d.Y,
            MathF.Abs(d.Z) < 1e-6f ? 1e6f : 1f / d.Z);

        for (int i = 0; i < boxes.Count; i++)
        {
            var (min, max) = boxes[i];
            float t1 = (min.X - o.X) * inv.X, t2 = (max.X - o.X) * inv.X;
            float tmin = MathF.Min(t1, t2), tmax = MathF.Max(t1, t2);
            t1 = (min.Y - o.Y) * inv.Y; t2 = (max.Y - o.Y) * inv.Y;
            tmin = MathF.Max(tmin, MathF.Min(t1, t2)); tmax = MathF.Min(tmax, MathF.Max(t1, t2));
            t1 = (min.Z - o.Z) * inv.Z; t2 = (max.Z - o.Z) * inv.Z;
            tmin = MathF.Max(tmin, MathF.Min(t1, t2)); tmax = MathF.Min(tmax, MathF.Max(t1, t2));
            if (tmax >= MathF.Max(tmin, 0.01f) && tmin < maxDist) return true;
        }
        return false;
    }

    // ------------------------------------------------------------------ output

    public (Vertex[] Vertices, uint[] Indices, MeshSection[] Sections) Build()
    {
        var sections = new List<MeshSection>();
        var indices = new List<uint>(_indicesByMaterial.Values.Sum(v => v.Count));
        foreach (var kv in _indicesByMaterial.OrderBy(k => k.Key))
        {
            if (kv.Value.Count == 0) continue;
            sections.Add(new MeshSection(kv.Key, indices.Count, kv.Value.Count));
            indices.AddRange(kv.Value);
        }
        return (_vertices.ToArray(), indices.ToArray(), sections.ToArray());
    }

    public void Clear()
    {
        _vertices.Clear();
        _indicesByMaterial.Clear();
        _stack.Clear();
        Transform = Matrix4x4.Identity;
    }
}
