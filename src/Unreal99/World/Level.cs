using System.Numerics;
using Silk.NET.OpenGL;
using Unreal99.Core;
using Unreal99.Game;
using Unreal99.Rendering;

namespace Unreal99.World;

public struct LevelLight
{
    public Vector3 Position;
    public Vector3 Color;
    public float Radius;
    public float Intensity;
    /// <summary>Non-zero makes the light flicker; used for lava glow and damaged fixtures.</summary>
    public float FlickerSpeed;
    public float FlickerAmount;
}

public struct SpawnPoint
{
    public Vector3 Position;
    public float Yaw;
    public Team Team;
}

public struct PickupPlacement
{
    public Vector3 Position;
    public PickupKind Kind;
    public WeaponKind Weapon;
    public AmmoKind Ammo;
    public float RespawnTime;
}

/// <summary>A brush that translates between two points: lifts and moving platforms.</summary>
public sealed class Mover
{
    public int BrushIndex;
    public Vector3 BaseMin, BaseMax;
    public Vector3 Offset;          // fully-extended displacement
    public float Period = 6f;       // seconds for a full there-and-back cycle
    public float Phase;
    public float DwellFraction = 0.3f;
    /// <summary>False keeps a decorative/manual lift out of bot pathfinding.</summary>
    public bool Navigable = true;
    public Vector3 CurrentOffset;
    public Vector3 Velocity;
    public int MeshInstance = -1;
}

public struct JumpPad
{
    public Vector3 Position;
    public Vector3 HalfExtents;
    public Vector3 LaunchVelocity;
    public Vector3 Color;
    /// <summary>Where the pad throws you; used to build the nav link.</summary>
    public Vector3 Destination;
}

public struct Teleporter
{
    public Vector3 Position;
    public Vector3 HalfExtents;
    public Vector3 Destination;
    public float DestinationYaw;
    public Vector3 Color;
}

public struct FlagBase
{
    public Vector3 Position;
    public Team Team;
    public float Yaw;
}

/// <summary>
/// A Domination control point. Captured by standing on it — there is no carry and no timer to
/// complete — and it then scores for whoever touched it last until somebody takes it back.
/// </summary>
public struct ControlPoint
{
    public Vector3 Position;
    /// <summary>Shown on the HUD, e.g. Tower / Bridge / Storage.</summary>
    public string Name;
    public float Radius;
}

/// <summary>Sky, sun and fog parameters for one arena.</summary>
public sealed class EnvironmentSettings
{
    public Vector3 SunDirection = Vector3.Normalize(new Vector3(-0.36f, -0.80f, -0.48f));
    public Vector3 SunColor = new(3.4f, 3.0f, 2.6f);
    public Vector3 AmbientSky = new(0.15f, 0.18f, 0.26f);
    public Vector3 AmbientGround = new(0.045f, 0.04f, 0.038f);
    public Vector3 SkyTop = new(0.04f, 0.09f, 0.22f);
    public Vector3 SkyHorizon = new(0.34f, 0.28f, 0.40f);
    public Vector3 SkyGround = new(0.03f, 0.03f, 0.035f);
    public float StarStrength = 1.0f;
    public float CloudStrength = 0.5f;
    public float EnvIntensity = 0.5f;
    public Vector3 FogColor = new(0.09f, 0.10f, 0.15f);
    public Vector3 FogSunColor = new(0.8f, 0.5f, 0.3f);
    public float FogDensity = 0.018f;
    public float FogHeightFalloff = 0.05f;
    public float FogStartHeight = -8f;

    public void ApplyTo(RenderScene scene)
    {
        scene.SunDirection = SunDirection;
        scene.SunColor = SunColor;
        scene.AmbientSky = AmbientSky;
        scene.AmbientGround = AmbientGround;
        scene.SkyTop = SkyTop;
        scene.SkyHorizon = SkyHorizon;
        scene.SkyGround = SkyGround;
        scene.StarStrength = StarStrength;
        scene.CloudStrength = CloudStrength;
        scene.EnvIntensity = EnvIntensity;
        scene.FogColor = FogColor;
        scene.FogSunColor = FogSunColor;
        scene.FogDensity = FogDensity;
        scene.FogHeightFalloff = FogHeightFalloff;
        scene.FogStartHeight = FogStartHeight;
    }
}

/// <summary>A fully built arena: geometry, collision, gameplay placements and navigation.</summary>
public sealed class Level : IDisposable
{
    public string Name = "";
    public string Description = "";
    public CollisionWorld Collision = new();
    public Mesh Geometry;
    public MeshSection[] Sections = [];
    public Mesh[] MoverMeshes = [];
    public MeshSection[][] MoverSections = [];

    public readonly List<LevelLight> Lights = new();
    public readonly List<SpawnPoint> Spawns = new();
    public readonly List<PickupPlacement> Pickups = new();
    public readonly List<Mover> Movers = new();
    public readonly List<JumpPad> JumpPads = new();
    public readonly List<Teleporter> Teleporters = new();
    public readonly List<FlagBase> FlagBases = new();
    public readonly List<ControlPoint> ControlPoints = new();
    public NavGraph Nav = new();
    public EnvironmentSettings Environment = new();

    public Vector3 Min, Max;
    public Vector3 Center => (Min + Max) * 0.5f;
    public float Radius => (Max - Min).Length() * 0.5f;
    /// <summary>Anything below this is out of bounds and dies.</summary>
    public float KillPlaneY = -60f;

    /// <summary>
    /// Multiplies gravity for this arena. Rooftop maps run light so a running jump can clear a
    /// gap that would be impossible at 1.0 — the floaty feel is the whole point of those layouts.
    /// Jump pads solve their ballistic arc against this too.
    /// </summary>
    public float GravityScale = 1f;

    public void Update(float dt, float time)
    {
        foreach (var m in Movers)
        {
            // Triangle wave with dwell at both ends, so lifts pause before reversing.
            float t = (time / m.Period + m.Phase) % 1f;
            float dwell = MathX.Clamp(m.DwellFraction, 0f, 0.45f);
            float travel = (1f - dwell * 2f) * 0.5f;
            float f;
            if (t < travel) f = t / travel;
            else if (t < travel + dwell) f = 1f;
            else if (t < travel * 2f + dwell) f = 1f - (t - travel - dwell) / travel;
            else f = 0f;
            f = MathX.SmoothStep(0f, 1f, f);

            Vector3 prev = m.CurrentOffset;
            m.CurrentOffset = m.Offset * f;
            m.Velocity = dt > 1e-5f ? (m.CurrentOffset - prev) / dt : Vector3.Zero;
            Collision.SetBrushOffset(m.BrushIndex, m.CurrentOffset, m.BaseMin, m.BaseMax);
        }
        if (Movers.Count > 0) Collision.Rebuild();
    }

    /// <summary>Adds the static geometry and every mover to this frame's draw list.</summary>
    public void Submit(RenderScene scene, MaterialLibrary materials, float time)
    {
        if (Geometry != null)
            scene.AddMesh(Geometry, Sections, materials, Matrix4x4.Identity, Center, Radius, castShadow: true);

        for (int i = 0; i < Movers.Count; i++)
        {
            var m = Movers[i];
            if (m.MeshInstance < 0 || m.MeshInstance >= MoverMeshes.Length) continue;
            Matrix4x4 xf = Matrix4x4.CreateTranslation(m.CurrentOffset);
            Vector3 c = (m.BaseMin + m.BaseMax) * 0.5f + m.CurrentOffset;
            float r = (m.BaseMax - m.BaseMin).Length() * 0.5f;
            scene.AddMesh(MoverMeshes[m.MeshInstance], MoverSections[m.MeshInstance], materials, xf, c, r);
        }

        foreach (var l in Lights)
        {
            float intensity = l.Intensity;
            if (l.FlickerAmount > 0f)
            {
                // Two out-of-phase sines make a believable unsteady flame without a noise lookup.
                float f = MathF.Sin(time * l.FlickerSpeed + l.Position.X * 3.1f) * 0.6f
                        + MathF.Sin(time * l.FlickerSpeed * 1.73f + l.Position.Z * 2.3f) * 0.4f;
                intensity *= 1f + f * l.FlickerAmount;
            }
            scene.AddLight(l.Position, l.Radius, l.Color, intensity, 0.6f);
        }
    }

    public SpawnPoint PickSpawn(Rng rng, Team team, IReadOnlyList<Vector3> avoid, float minDistance = 9f)
    {
        var candidates = new List<int>(Spawns.Count);
        for (int i = 0; i < Spawns.Count; i++)
            if (team == Team.None || Spawns[i].Team == Team.None || Spawns[i].Team == team) candidates.Add(i);
        if (candidates.Count == 0)
        {
            for (int i = 0; i < Spawns.Count; i++) candidates.Add(i);
            if (candidates.Count == 0)
                return new SpawnPoint { Position = Center + MathX.Up * 3f, Yaw = 0f, Team = Team.None };
        }

        // Prefer the spawn furthest from every living pawn; fall back to random if all are crowded.
        int best = -1;
        float bestScore = float.MinValue;
        for (int attempt = 0; attempt < candidates.Count; attempt++)
        {
            int idx = candidates[rng.Range(0, candidates.Count)];
            float nearest = float.MaxValue;
            foreach (var p in avoid) nearest = MathF.Min(nearest, Vector3.Distance(Spawns[idx].Position, p));
            if (avoid.Count == 0) nearest = 1000f;
            if (nearest >= minDistance) return Spawns[idx];
            if (nearest > bestScore) { bestScore = nearest; best = idx; }
        }
        return Spawns[best >= 0 ? best : candidates[0]];
    }

    public void Dispose()
    {
        Geometry?.Dispose();
        foreach (var m in MoverMeshes) m?.Dispose();
    }
}

/// <summary>
/// Authoring helper that writes geometry and collision together, so every call to
/// <see cref="Solid"/> produces both a visible brush and something you can stand on.
/// </summary>
public sealed class LevelBuilder
{
    private readonly MeshBuilder _mesh = new();
    private readonly List<MeshBuilder> _moverMeshes = new();
    private readonly List<(Vector3 Min, Vector3 Max)> _occluders = new();
    private readonly Level _level = new();
    public Rng Rng = new(0xBEEF01);

    public Level Level => _level;
    public MeshBuilder Mesh => _mesh;

    public LevelBuilder(string name, string description)
    {
        _level.Name = name;
        _level.Description = description;
    }

    // ---------------------------------------------------------------- geometry primitives

    public void Solid(Vector3 min, Vector3 max, MatId material, bool collide = true, float uvScale = 1f)
    {
        // Symmetric arenas frequently author the opposite side with signed coordinates. Always
        // canonicalise the brush first; reversed bounds otherwise create inward-facing geometry
        // and invalid collision boxes that look like paper-thin or missing walls.
        Vector3 low = Vector3.Min(min, max);
        Vector3 high = Vector3.Max(min, max);

        // A structural wall needs enough depth for its exposed edges to read as masonry or metal,
        // rather than as a texture painted onto a plane. This only affects tall, colliding sheets;
        // floors, catwalk decks, trim, railings and compact pillars retain their authored size.
        if (collide && high.Y - low.Y >= 1.5f)
        {
            const float MinimumWallDepth = 0.45f;
            const float MinimumWallLength = 1.0f;
            float width = high.X - low.X;
            float depth = high.Z - low.Z;
            if (width < MinimumWallDepth && depth >= MinimumWallLength)
            {
                float center = (low.X + high.X) * 0.5f;
                low.X = center - MinimumWallDepth * 0.5f;
                high.X = center + MinimumWallDepth * 0.5f;
            }
            if (depth < MinimumWallDepth && width >= MinimumWallLength)
            {
                float center = (low.Z + high.Z) * 0.5f;
                low.Z = center - MinimumWallDepth * 0.5f;
                high.Z = center + MinimumWallDepth * 0.5f;
            }
        }

        min = low;
        max = high;
        _mesh.Material = (int)material;
        _mesh.WorldUv = true;
        _mesh.WorldUvScale = uvScale;
        _mesh.AddBoxMinMax(min, max);
        if (collide)
        {
            _level.Collision.Add(Brush.Box(min, max));
            _occluders.Add((min, max));
        }
    }

    public void Ramp(Vector3 min, Vector3 max, int risingAxis, MatId material, bool collide = true, float uvScale = 1f)
    {
        // Brush.Ramp already canonicalises collision bounds. Do the same for the visible mesh so
        // mirrored ramps do not become inverted, zero-looking sheets while collision stays solid.
        Vector3 low = Vector3.Min(min, max);
        Vector3 high = Vector3.Max(min, max);
        min = low;
        max = high;
        _mesh.Material = (int)material;
        _mesh.WorldUv = true;
        _mesh.WorldUvScale = uvScale;

        // The old wedge tapered to exactly zero at its low end. A constant-thickness sloped slab
        // gives both side fascias and both ends an unmistakably structural profile.
        // Long exposed ramps (notably November's gallery access) need enough fascia depth to
        // read as load-bearing structures at gameplay distance, not floating sheets.
        const float RampThickness = 1.25f;
        Vector3 visualMin = new(min.X, min.Y - RampThickness, min.Z);
        _mesh.AddRampSlab(min, max, risingAxis, RampThickness);
        if (collide)
        {
            _level.Collision.Add(Brush.Ramp(min, max, risingAxis));
            _occluders.Add((visualMin, max));
        }
    }

    public void Lava(Vector3 min, Vector3 max)
    {
        _mesh.Material = (int)MatId.Lava;
        _mesh.WorldUv = true;
        _mesh.WorldUvScale = 1f;
        _mesh.AddBoxMinMax(min, max);
        _level.Collision.Add(Brush.Box(min, max, BrushKind.Lava));
        _occluders.Add((min, max));

        // Light the pool from a grid of points so the glow follows its shape.
        Vector3 size = max - min;
        int nx = MathX.Clamp((int)(size.X / 7f), 1, 4);
        int nz = MathX.Clamp((int)(size.Z / 7f), 1, 4);
        for (int x = 0; x < nx; x++)
            for (int z = 0; z < nz; z++)
            {
                Vector3 p = new(
                    MathX.Lerp(min.X, max.X, (x + 0.5f) / nx),
                    max.Y + 1.2f,
                    MathX.Lerp(min.Z, max.Z, (z + 0.5f) / nz));
                AddLight(p, new Vector3(1.0f, 0.38f, 0.10f), 13f, 3.2f, 4.2f, 0.26f);
            }
    }

    public void Water(Vector3 min, Vector3 max)
    {
        _mesh.Material = (int)MatId.Water;
        _mesh.AddBoxMinMax(min, max);
        _level.Collision.Add(Brush.Box(min, max, BrushKind.Water));
    }

    /// <summary>Non-solid decorative geometry: trim, cables, panels behind grates.</summary>
    public void Decor(Vector3 min, Vector3 max, MatId material, float uvScale = 1f)
        => Solid(min, max, material, collide: false, uvScale);

    public void Prism(Vector3 center, float radius, float height, int sides, MatId material,
        bool collide = true, float rotation = 0f)
    {
        _mesh.Material = (int)material;
        _mesh.WorldUv = true;
        _mesh.WorldUvScale = 1f;
        _mesh.AddPrism(center, radius, height, sides, rotation);
        if (collide)
        {
            Vector3 half = new(radius * 0.86f, height * 0.5f, radius * 0.86f);
            _level.Collision.Add(Brush.Box(center - half, center + half));
            _occluders.Add((center - half, center + half));
        }
    }

    public void Cylinder(Vector3 center, float radiusBottom, float radiusTop, float height, int segments,
        MatId material, bool collide = false)
    {
        _mesh.Material = (int)material;
        _mesh.WorldUv = false;
        _mesh.AddCylinder(center, radiusBottom, radiusTop, height, segments);
        _mesh.WorldUv = true;
        if (collide)
        {
            float r = MathF.Max(radiusBottom, radiusTop) * 0.8f;
            Vector3 half = new(r, height * 0.5f, r);
            _level.Collision.Add(Brush.Box(center - half, center + half));
        }
    }

    public void Sphere(Vector3 center, float radius, MatId material, int rings = 10, int segs = 14)
    {
        _mesh.Material = (int)material;
        _mesh.WorldUv = false;
        _mesh.AddSphere(center, radius, rings, segs);
        _mesh.WorldUv = true;
    }

    public void Torus(Vector3 center, float major, float minor, MatId material, int a = 20, int b = 8)
    {
        _mesh.Material = (int)material;
        _mesh.WorldUv = false;
        _mesh.AddTorus(center, major, minor, a, b);
        _mesh.WorldUv = true;
    }

    // ---------------------------------------------------------------- composite structures

    /// <summary>
    /// Emits a disc or annulus as a run of axis-aligned boxes.
    /// Collision only understands axis-aligned brushes, so a rotated slab ring would give every
    /// segment a wildly inflated bounding box that juts into the arena. Rasterising the shape
    /// into axis-aligned slabs instead keeps geometry and collision identical, and the faceted
    /// silhouette suits the era.
    /// </summary>
    public void Annulus(Vector3 center, float bottomY, float topY, float innerRadius, float outerRadius,
        MatId material, int slabs = 20, bool collide = true, float uvScale = 1f)
    {
        if (outerRadius <= 0f || topY <= bottomY) return;
        slabs = Math.Max(4, slabs);
        float step = outerRadius * 2f / slabs;

        for (int i = 0; i < slabs; i++)
        {
            float x0 = -outerRadius + i * step;
            float x1 = x0 + step;
            float xFar = MathF.Max(MathF.Abs(x0), MathF.Abs(x1));
            // A slab straddling the axis reaches all the way to x = 0.
            float xNear = (x0 < 0f && x1 > 0f) ? 0f : MathF.Min(MathF.Abs(x0), MathF.Abs(x1));
            if (xFar >= outerRadius) continue;

            float zOuter = MathF.Sqrt(outerRadius * outerRadius - xFar * xFar);
            if (zOuter <= 0.01f) continue;

            void Emit(float za, float zb)
            {
                if (zb - za < 0.02f) return;
                Solid(new Vector3(center.X + x0, bottomY, center.Z + za),
                      new Vector3(center.X + x1, topY, center.Z + zb), material, collide, uvScale);
            }

            if (innerRadius <= 0.01f || xNear >= innerRadius)
            {
                Emit(-zOuter, zOuter);
            }
            else
            {
                float zInner = MathF.Sqrt(innerRadius * innerRadius - xNear * xNear);
                if (zInner >= zOuter) continue;
                Emit(-zOuter, -zInner);
                Emit(zInner, zOuter);
            }
        }
    }

    /// <summary>
    /// Hollow room: floor, ceiling and four walls of the given thickness, built inward from
    /// the supplied outer bounds. Doorways are punched afterwards with <see cref="Doorway"/>.
    /// </summary>
    public void Room(Vector3 min, Vector3 max, float wall, MatId floorMat, MatId wallMat, MatId ceilMat,
        bool withCeiling = true, bool withFloor = true)
    {
        if (withFloor) Solid(new Vector3(min.X, min.Y, min.Z), new Vector3(max.X, min.Y + wall, max.Z), floorMat);
        if (withCeiling) Solid(new Vector3(min.X, max.Y - wall, min.Z), new Vector3(max.X, max.Y, max.Z), ceilMat);
        Solid(new Vector3(min.X, min.Y, min.Z), new Vector3(min.X + wall, max.Y, max.Z), wallMat);
        Solid(new Vector3(max.X - wall, min.Y, min.Z), new Vector3(max.X, max.Y, max.Z), wallMat);
        Solid(new Vector3(min.X + wall, min.Y, min.Z), new Vector3(max.X - wall, max.Y, min.Z + wall), wallMat);
        Solid(new Vector3(min.X + wall, min.Y, max.Z - wall), new Vector3(max.X - wall, max.Y, max.Z), wallMat);
    }

    /// <summary>Builds a wall with a rectangular opening, as four surrounding slabs.</summary>
    public void WallWithDoor(Vector3 min, Vector3 max, float doorCenter, float doorWidth, float doorHeight,
        MatId material, bool alongX = true)
    {
        if (alongX)
        {
            float d0 = doorCenter - doorWidth * 0.5f, d1 = doorCenter + doorWidth * 0.5f;
            Solid(min, new Vector3(d0, max.Y, max.Z), material);
            Solid(new Vector3(d1, min.Y, min.Z), max, material);
            Solid(new Vector3(d0, min.Y + doorHeight, min.Z), new Vector3(d1, max.Y, max.Z), material);
        }
        else
        {
            float d0 = doorCenter - doorWidth * 0.5f, d1 = doorCenter + doorWidth * 0.5f;
            Solid(min, new Vector3(max.X, max.Y, d0), material);
            Solid(new Vector3(min.X, min.Y, d1), max, material);
            Solid(new Vector3(min.X, min.Y + doorHeight, d0), new Vector3(max.X, max.Y, d1), material);
        }
    }

    /// <summary>A run of steps between two heights. Cheaper on collision than a long ramp chain.</summary>
    public void Stairs(Vector3 start, Vector3 end, float width, int steps, MatId material, bool alongX = true)
    {
        for (int i = 0; i < steps; i++)
        {
            float t0 = i / (float)steps, t1 = (i + 1) / (float)steps;
            Vector3 a = Vector3.Lerp(start, end, t0);
            Vector3 b = Vector3.Lerp(start, end, t1);
            float y = MathX.Lerp(start.Y, end.Y, t1);
            Vector3 min, max;
            if (alongX)
            {
                min = new Vector3(MathF.Min(a.X, b.X), start.Y - 0.6f, a.Z - width * 0.5f);
                max = new Vector3(MathF.Max(a.X, b.X), y, a.Z + width * 0.5f);
            }
            else
            {
                min = new Vector3(a.X - width * 0.5f, start.Y - 0.6f, MathF.Min(a.Z, b.Z));
                max = new Vector3(a.X + width * 0.5f, y, MathF.Max(a.Z, b.Z));
            }
            Solid(min, max, material);
        }
    }

    /// <summary>Catwalk with railings; the classic connective tissue of a UT arena.</summary>
    public void Catwalk(Vector3 from, Vector3 to, float width, MatId deckMat, MatId railMat,
        bool railings = true, float railHeight = 0.85f)
    {
        Vector3 dir = MathX.SafeNormalize((to - from).FlatXZ(), MathX.Right);
        Vector3 side = new(-dir.Z, 0, dir.X);
        Vector3 min = Vector3.Min(from, to) - side * (width * 0.5f) - new Vector3(0.001f);
        Vector3 max = Vector3.Max(from, to) + side * (width * 0.5f) + new Vector3(0.001f);
        min = Vector3.Min(min, max - new Vector3(0.2f, 0.001f, 0.2f));
        Solid(new Vector3(min.X, from.Y - 0.28f, min.Z), new Vector3(max.X, from.Y, max.Z), deckMat);

        if (!railings) return;
        // Rails are thin, non-colliding visual guards; the deck edge does the blocking.
        Vector3 a = from + side * (width * 0.5f - 0.06f);
        Vector3 b = to + side * (width * 0.5f - 0.06f);
        Vector3 c = from - side * (width * 0.5f - 0.06f);
        Vector3 d = to - side * (width * 0.5f - 0.06f);
        RailSegment(a, b, railHeight, railMat);
        RailSegment(c, d, railHeight, railMat);
    }

    private void RailSegment(Vector3 a, Vector3 b, float height, MatId mat)
    {
        Vector3 min = Vector3.Min(a, b), max = Vector3.Max(a, b);
        Decor(new Vector3(min.X - 0.05f, min.Y + height - 0.09f, min.Z - 0.05f),
              new Vector3(max.X + 0.05f, max.Y + height, max.Z + 0.05f), mat, 1.4f);
        float len = Vector3.Distance(a, b);
        int posts = Math.Max(2, (int)(len / 2.4f));
        for (int i = 0; i <= posts; i++)
        {
            Vector3 p = Vector3.Lerp(a, b, i / (float)posts);
            Decor(p - new Vector3(0.05f, 0f, 0.05f), p + new Vector3(0.05f, height, 0.05f), mat, 1.4f);
        }
    }

    /// <summary>Recessed ceiling light fixture plus the matching dynamic light.</summary>
    public void CeilingLamp(Vector3 position, Vector3 color, float radius = 13f, float intensity = 5.5f,
        float size = 0.9f)
    {
        Decor(position - new Vector3(size, 0.16f, size), position + new Vector3(size, 0.05f, size), MatId.EnergyPanel, 0.9f);
        Decor(position - new Vector3(size * 1.25f, 0.28f, size * 1.25f),
              position + new Vector3(size * 1.25f, 0.02f, size * 1.25f), MatId.Trim, 1.2f);
        AddLight(position - new Vector3(0, 0.5f, 0), color, radius, intensity);
    }

    public void AddLight(Vector3 position, Vector3 color, float radius, float intensity,
        float flickerSpeed = 0f, float flickerAmount = 0f)
        => _level.Lights.Add(new LevelLight
        {
            Position = position,
            Color = color,
            Radius = radius,
            Intensity = intensity,
            FlickerSpeed = flickerSpeed,
            FlickerAmount = flickerAmount,
        });

    // ---------------------------------------------------------------- gameplay placements

    public void Spawn(Vector3 position, float yawDegrees = 0f, Team team = Team.None)
        => _level.Spawns.Add(new SpawnPoint
        {
            Position = position,
            Yaw = yawDegrees * MathX.Deg2Rad,
            Team = team,
        });

    public void Weapon(Vector3 position, WeaponKind weapon, float respawn = 22f)
        => _level.Pickups.Add(new PickupPlacement
        {
            Position = position,
            Kind = PickupKind.WeaponPickup,
            Weapon = weapon,
            Ammo = AmmoKind.None,
            RespawnTime = respawn,
        });

    public void Ammo(Vector3 position, AmmoKind ammo, float respawn = 18f)
        => _level.Pickups.Add(new PickupPlacement
        {
            Position = position,
            Kind = PickupKind.AmmoPickup,
            Weapon = WeaponKind.Count,
            Ammo = ammo,
            RespawnTime = respawn,
        });

    public void Item(Vector3 position, PickupKind kind, float respawn = 0f)
        => _level.Pickups.Add(new PickupPlacement
        {
            Position = position,
            Kind = kind,
            Weapon = WeaponKind.Count,
            Ammo = AmmoKind.None,
            RespawnTime = respawn > 0f ? respawn : DefaultRespawn(kind),
        });

    private static float DefaultRespawn(PickupKind k) => k switch
    {
        PickupKind.HealthVial => 20f,
        PickupKind.HealthPack => 26f,
        PickupKind.SuperHealth => 55f,
        PickupKind.ThighPads => 26f,
        PickupKind.BodyArmor => 36f,
        PickupKind.ShieldBelt => 55f,
        PickupKind.DamageAmp => 70f,
        PickupKind.Invisibility => 65f,
        PickupKind.JumpBoots => 45f,
        _ => 20f,
    };

    /// <summary>Adds a lift: a platform brush that oscillates between base and base+offset.</summary>
    public void Lift(Vector3 min, Vector3 max, Vector3 offset, MatId material, float period = 7f,
        float phase = 0f, float dwell = 0.3f, bool navigable = true)
    {
        var mb = new MeshBuilder { Material = (int)material, WorldUv = true, WorldUvScale = 1f };
        mb.AddBoxMinMax(min, max);
        mb.Material = (int)MatId.Trim;
        mb.AddBoxMinMax(new Vector3(min.X, max.Y - 0.04f, min.Z), new Vector3(max.X, max.Y + 0.03f, min.Z + 0.16f));
        mb.AddBoxMinMax(new Vector3(min.X, max.Y - 0.04f, max.Z - 0.16f), new Vector3(max.X, max.Y + 0.03f, max.Z));
        _moverMeshes.Add(mb);

        int brush = _level.Collision.Add(Brush.Box(min, max));
        _level.Movers.Add(new Mover
        {
            BrushIndex = brush,
            BaseMin = min,
            BaseMax = max,
            Offset = offset,
            Period = period,
            Phase = phase,
            DwellFraction = dwell,
            Navigable = navigable,
            MeshInstance = _moverMeshes.Count - 1,
        });
        AddLight((min + max) * 0.5f + offset * 0.5f + MathX.Up, new Vector3(0.3f, 0.7f, 1f), 8f, 1.6f);
    }

    public void AddJumpPad(Vector3 position, Vector3 destination, Vector3 color,
        float peakClearance = 3.2f)
    {
        // Ballistic solve: pick a flight time from the height difference and derive the launch
        // velocity. Maps may request extra clearance when the route must pass over a wall before
        // descending to a lower landing; the default preserves the compact ordinary arc.
        Vector3 delta = destination - position;
        float gravity = Physics.Gravity * _level.GravityScale;
        float peak = MathF.Max(delta.Y + peakClearance, peakClearance);
        float vy = MathF.Sqrt(2f * gravity * peak);
        float tUp = vy / gravity;
        float tDown = MathF.Sqrt(2f * MathF.Max(peak - delta.Y, 0.1f) / gravity);
        float total = tUp + tDown;
        Vector3 horizontal = delta.FlatXZ() / MathF.Max(total, 0.15f);

        _level.JumpPads.Add(new JumpPad
        {
            Position = position,
            HalfExtents = new Vector3(1.1f, 0.35f, 1.1f),
            LaunchVelocity = new Vector3(horizontal.X, vy, horizontal.Z),
            Color = color,
            Destination = destination,
        });

        Decor(position - new Vector3(1.15f, 0.14f, 1.15f), position + new Vector3(1.15f, 0.06f, 1.15f),
            MatId.Trim, 1.2f);
        _mesh.Material = (int)MatId.EnergyPanel;
        _mesh.WorldUv = false;
        _mesh.AddCylinder(position + new Vector3(0, 0.08f, 0), 0.95f, 0.95f, 0.1f, 20);
        _mesh.WorldUv = true;
        AddLight(position + new Vector3(0, 1.1f, 0), color, 9f, 3.4f, 6f, 0.22f);
    }

    public void AddTeleporter(Vector3 position, Vector3 destination, float destinationYawDegrees, Vector3 color)
    {
        _level.Teleporters.Add(new Teleporter
        {
            Position = position,
            HalfExtents = new Vector3(0.9f, 1.3f, 0.9f),
            Destination = destination,
            DestinationYaw = destinationYawDegrees * MathX.Deg2Rad,
            Color = color,
        });
        _mesh.Material = (int)MatId.Trim;
        _mesh.WorldUv = false;
        _mesh.AddTorus(position + new Vector3(0, 1.3f, 0), 1.15f, 0.14f, 18, 8);
        _mesh.WorldUv = true;
        AddLight(position + new Vector3(0, 1.3f, 0), color, 8f, 3.2f, 5f, 0.18f);
    }

    public void AddFlagBase(Vector3 position, Team team, float yawDegrees = 0f)
    {
        _level.FlagBases.Add(new FlagBase { Position = position, Team = team, Yaw = yawDegrees * MathX.Deg2Rad });
        Vector3 col = GameTypes.TeamColor(team);
        Decor(position - new Vector3(1.3f, 0.18f, 1.3f), position + new Vector3(1.3f, 0.12f, 1.3f), MatId.Trim, 1.1f);
        _mesh.Material = (int)MatId.EnergyPanel;
        _mesh.WorldUv = false;
        _mesh.AddCylinder(position + new Vector3(0, 0.16f, 0), 1.05f, 1.05f, 0.08f, 20);
        _mesh.WorldUv = true;
        AddLight(position + new Vector3(0, 1.6f, 0), col, 11f, 3.6f);
    }

    /// <summary>
    /// A Domination control point: a low dais with a pillar, so it reads from across a room and
    /// from above. The team colour is applied at runtime by the renderer, not baked here — the
    /// whole point of the thing is that it changes hands.
    /// </summary>
    public void AddControlPoint(Vector3 position, string name, float radius = 2.2f)
    {
        _level.ControlPoints.Add(new ControlPoint { Position = position, Name = name, Radius = radius });
        Decor(position - new Vector3(radius, 0.22f, radius), position + new Vector3(radius, 0.10f, radius),
            MatId.Trim, 1.1f);
        Decor(position - new Vector3(0.34f, 0f, 0.34f), position + new Vector3(0.34f, 2.6f, 0.34f),
            MatId.TechPanelDark, 0.8f);
        AddLight(position + new Vector3(0, 2.2f, 0), new Vector3(0.85f, 0.85f, 0.9f), 13f, 3.2f);
    }

    // ---------------------------------------------------------------- finalise

    public Level Build(GL gl, bool bakeAo = true)
    {
        _level.Collision.Rebuild();
        _level.Min = _level.Collision.WorldMin;
        _level.Max = _level.Collision.WorldMax;
        _level.KillPlaneY = _level.Min.Y - 25f;

        // An out-of-bounds volume under the arena so falls resolve as a death, not a freeze.
        Vector3 pad = new(60f, 0f, 60f);
        _level.Collision.Add(Brush.Box(
            new Vector3(_level.Min.X - pad.X, _level.KillPlaneY - 8f, _level.Min.Z - pad.Z),
            new Vector3(_level.Max.X + pad.X, _level.KillPlaneY, _level.Max.Z + pad.Z),
            BrushKind.Void));
        _level.Collision.Rebuild();

        _mesh.RecalculateTangents();
        if (bakeAo) _mesh.BakeAmbientOcclusion(_occluders, 2.6f, 10);

        var (verts, inds, sections) = _mesh.Build();
        _level.Geometry = Rendering.Mesh.CreateStatic<Vertex>(gl, verts, inds, VertexLayouts.Static);
        _level.Sections = sections;

        _level.MoverMeshes = new Mesh[_moverMeshes.Count];
        _level.MoverSections = new MeshSection[_moverMeshes.Count][];
        for (int i = 0; i < _moverMeshes.Count; i++)
        {
            _moverMeshes[i].RecalculateTangents();
            var (mv, mi, ms) = _moverMeshes[i].Build();
            _level.MoverMeshes[i] = Rendering.Mesh.CreateStatic<Vertex>(gl, mv, mi, VertexLayouts.Static);
            _level.MoverSections[i] = ms;
        }

        _level.Nav.Generate(_level.Collision);
        foreach (var pad2 in _level.JumpPads)
            _level.Nav.AddSpecialLink(pad2.Position, pad2.Destination, NavFlags.JumpPad);
        foreach (var tp in _level.Teleporters)
            _level.Nav.AddSpecialLink(tp.Position, tp.Destination, NavFlags.Teleporter);
        foreach (var p in _level.Pickups)
            _level.Nav.MarkFlag(p.Position, NavFlags.NearPickup, 2.2f);
        foreach (var m in _level.Movers)
        {
            if (!m.Navigable) continue;
            Vector3 top = (m.BaseMin + m.BaseMax) * 0.5f + m.Offset + new Vector3(0, (m.BaseMax.Y - m.BaseMin.Y) * 0.5f + 0.4f, 0);
            Vector3 bottom = (m.BaseMin + m.BaseMax) * 0.5f + new Vector3(0, (m.BaseMax.Y - m.BaseMin.Y) * 0.5f + 0.4f, 0);
            _level.Nav.AddSpecialLink(bottom, top, NavFlags.JumpPad);
            _level.Nav.AddSpecialLink(top, bottom, NavFlags.JumpPad);
        }

        return _level;
    }
}
