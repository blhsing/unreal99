using System.Numerics;
using Unreal99.Core;

namespace Unreal99.Rendering;

public struct PointLight
{
    public Vector3 Position;
    public float Radius;
    public Vector3 Color;
    public float Intensity;
    /// <summary>Higher priority lights survive the per-view cull to the shader's light budget.</summary>
    public float Priority;
}

public struct DrawCall
{
    public Mesh Mesh;
    public int IndexOffset;
    public int IndexCount;      // 0 = draw the whole mesh
    public Material Material;
    public Matrix4x4 Transform;
    public int BoneBase;        // index into RenderScene.Bones, or -1 for static geometry
    public int BoneCount;
    public Vector4 Tint;
    public Vector3 Emissive;
    public bool OverrideEmissive;
    public float Alpha;
    public Vector3 Center;
    public float Radius;
    public bool CastShadow;
    public float RimStrength;
    public Vector3 RimColor;
    public Vector2 UvScale;
    public Vector2 UvOffset;
    /// <summary>Views whose index differs are skipped; used for first-person weapon models.</summary>
    public int OwnerView;
    public bool FirstPerson;
}

/// <summary>
/// Per-frame draw list. Gameplay fills this once and every split-screen view consumes it,
/// so animation and transform work happens a single time regardless of player count.
/// </summary>
public sealed class RenderScene
{
    public readonly List<DrawCall> Opaque = new(512);
    public readonly List<DrawCall> Transparent = new(128);
    public readonly List<Matrix4x4> Bones = new(1024);
    public readonly List<PointLight> Lights = new(128);

    // Environment
    public Vector3 SunDirection = Vector3.Normalize(new Vector3(-0.35f, -0.82f, -0.45f));
    public Vector3 SunColor = new(3.6f, 3.2f, 2.75f);
    public Vector3 AmbientSky = new(0.16f, 0.19f, 0.26f);
    public Vector3 AmbientGround = new(0.05f, 0.045f, 0.04f);
    public Vector3 SkyTop = new(0.05f, 0.10f, 0.24f);
    public Vector3 SkyHorizon = new(0.35f, 0.30f, 0.42f);
    public Vector3 SkyGround = new(0.04f, 0.035f, 0.04f);
    public float StarStrength = 1.0f;
    public float CloudStrength = 0.55f;
    public float EnvIntensity = 0.55f;

    public Vector3 FogColor = new(0.10f, 0.11f, 0.16f);
    public Vector3 FogSunColor = new(0.85f, 0.55f, 0.32f);
    public float FogDensity = 0.020f;
    public float FogHeightFalloff = 0.055f;
    public float FogStartHeight = -6f;

    public float Time;

    public void Clear()
    {
        Opaque.Clear();
        Transparent.Clear();
        Bones.Clear();
        Lights.Clear();
    }

    public int AddBones(ReadOnlySpan<Matrix4x4> bones)
    {
        int baseIndex = Bones.Count;
        for (int i = 0; i < bones.Length; i++) Bones.Add(bones[i]);
        return baseIndex;
    }

    public void AddLight(Vector3 pos, float radius, Vector3 color, float intensity, float priority = 1f)
    {
        if (intensity <= 0f || radius <= 0f) return;
        Lights.Add(new PointLight
        {
            Position = pos,
            Radius = radius,
            Color = color,
            Intensity = intensity,
            Priority = priority,
        });
    }

    /// <summary>Adds every section of a multi-material mesh.</summary>
    public void AddMesh(Mesh mesh, MeshSection[] sections, MaterialLibrary materials, in Matrix4x4 transform,
        Vector3 center, float radius, bool castShadow = true)
    {
        if (mesh == null || sections == null) return;
        foreach (var s in sections)
        {
            Material mat = materials.Get(s.Material);
            var dc = MakeDefault(mesh, mat, transform, center, radius, castShadow);
            dc.IndexOffset = s.IndexOffset;
            dc.IndexCount = s.IndexCount;
            if (mat.Transparent) Transparent.Add(dc); else Opaque.Add(dc);
        }
    }

    public void AddMesh(Mesh mesh, Material material, in Matrix4x4 transform, Vector3 center, float radius,
        bool castShadow = true, Vector4? tint = null, Vector3? emissive = null, float alpha = 1f,
        int boneBase = -1, int boneCount = 0, float rim = 0f, Vector3 rimColor = default,
        int ownerView = -1, bool firstPerson = false)
    {
        if (mesh == null || material == null) return;
        var dc = MakeDefault(mesh, material, transform, center, radius, castShadow);
        if (tint.HasValue) dc.Tint = tint.Value;
        if (emissive.HasValue) { dc.Emissive = emissive.Value; dc.OverrideEmissive = true; }
        dc.Alpha = alpha * material.Alpha;
        dc.BoneBase = boneBase;
        dc.BoneCount = boneCount;
        dc.RimStrength = rim;
        dc.RimColor = rimColor;
        dc.OwnerView = ownerView;
        dc.FirstPerson = firstPerson;
        if (material.Transparent || dc.Alpha < 0.999f) Transparent.Add(dc);
        else Opaque.Add(dc);
    }

    private static DrawCall MakeDefault(Mesh mesh, Material material, in Matrix4x4 transform,
        Vector3 center, float radius, bool castShadow) => new()
        {
            Mesh = mesh,
            IndexOffset = 0,
            IndexCount = 0,
            Material = material,
            Transform = transform,
            BoneBase = -1,
            BoneCount = 0,
            Tint = material.BaseColor,
            Emissive = material.Emissive,
            OverrideEmissive = false,
            Alpha = material.Alpha,
            Center = center,
            Radius = radius,
            CastShadow = castShadow,
            RimStrength = 0f,
            RimColor = Vector3.Zero,
            UvScale = material.UvScale,
            UvOffset = Vector2.Zero,
            OwnerView = -1,
            FirstPerson = false,
        };

    /// <summary>
    /// Picks the strongest <paramref name="budget"/> lights for a camera position: score falls off
    /// with distance and rises with intensity, so a nearby rocket beats a distant lamp.
    /// </summary>
    public int SelectLights(Vector3 cameraPos, in Frustum frustum, Span<Vector4> posRadius,
        Span<Vector4> colorIntensity, int budget)
    {
        Span<(float score, int index)> scored = Lights.Count <= 128
            ? stackalloc (float, int)[Lights.Count]
            : new (float, int)[Lights.Count];

        int n = 0;
        for (int i = 0; i < Lights.Count; i++)
        {
            var l = Lights[i];
            if (!frustum.SphereVisible(l.Position, l.Radius)) continue;
            float d = Vector3.Distance(cameraPos, l.Position);
            // Lights we are standing inside always win; otherwise fall off with distance.
            float score = l.Priority * l.Intensity * l.Radius / MathF.Max(d * d, 1f);
            if (d < l.Radius) score *= 4f;
            scored[n++] = (score, i);
        }

        var slice = scored[..n];
        // Partial selection sort: budget is small (24), so this beats a full sort.
        int count = Math.Min(budget, n);
        for (int i = 0; i < count; i++)
        {
            int best = i;
            for (int j = i + 1; j < n; j++) if (slice[j].Item1 > slice[best].Item1) best = j;
            (slice[i], slice[best]) = (slice[best], slice[i]);

            var l = Lights[slice[i].Item2];
            posRadius[i] = new Vector4(l.Position, l.Radius);
            colorIntensity[i] = new Vector4(l.Color, l.Intensity);
        }
        return count;
    }
}
