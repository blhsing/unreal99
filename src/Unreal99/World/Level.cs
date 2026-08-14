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
    /// <summary>
    /// Assault only. Group 0 is always live; higher groups open as the attackers complete
    /// objectives, and the attackers then use the highest group they have unlocked. Defenders
    /// never advance, so their spawns stay on group 0. Every other mode ignores this.
    /// </summary>
    public int AssaultGroup;
}

public struct PickupPlacement
{
    public Vector3 Position;
    public PickupKind Kind;
    public WeaponKind Weapon;
    public AmmoKind Ammo;
    public float RespawnTime;
    /// <summary>
    /// For <see cref="PickupKind.WeaponLocker"/>: everything the rack hands out at once. UT2004
    /// and UT3 distribute most of their arsenal through these rather than through single pickups,
    /// so a map rebuilt without them ends up with a completely different weapon economy.
    /// </summary>
    public WeaponKind[] LockerWeapons;
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
/// A Bombing Run hoop. <see cref="Team"/> is the side that defends it, so the other side is the
/// one trying to put the ball through. The position is the centre of the ring, not its base —
/// scoring is a proximity test against that point.
/// </summary>
public struct GoalHoop
{
    public Vector3 Position;
    public Team Team;
    public float Yaw;
}

/// <summary>
/// What a node does beyond sitting in the chain. Onslaught only has <see cref="Link"/>; the rest
/// are Warfare's auxiliary nodes, which sit outside the link network and pay out in other ways.
/// </summary>
public enum NodeRole
{
    /// <summary>Part of the chain: must be linked to be captured, and links onward when built.</summary>
    Link,
    /// <summary>Capturable with no link at all. Pays out in vehicles, weapons and a spawn point.</summary>
    Support,
    /// <summary>Support node that runs a clock; finishing it damages the enemy core.</summary>
    Countdown,
    /// <summary>Countdown node that delivers a vehicle instead of hurting the core.</summary>
    Vehicle,
}

/// <summary>
/// One node in an Onslaught or Warfare power network. Links are indices into the level's own node
/// list, which is what encodes the chain a team has to advance along.
/// </summary>
public struct PowerNodeDef
{
    public Vector3 Position;
    public string Name;
    public bool IsCore;
    /// <summary>Set only for cores; nodes start neutral.</summary>
    public Team Team;
    public int[] Links;
    public NodeRole Role;
    /// <summary>Seconds on the clock for <see cref="NodeRole.Countdown"/> and <see cref="NodeRole.Vehicle"/>.</summary>
    public float CountdownSeconds;
    /// <summary>Fraction of the enemy core a finished <see cref="NodeRole.Countdown"/> removes.</summary>
    public float CoreDamageFraction;
    /// <summary>What a finished <see cref="NodeRole.Vehicle"/> node parks, and where.</summary>
    public VehicleKind RewardVehicle;
    public Vector3 RewardPosition;
    public float RewardYaw;
}

/// <summary>
/// Where a team's Warfare orb appears. A spawn tied to a node only works while that node is
/// friendly, which is what lets a team that has pushed forward restart its orb runs from there.
/// </summary>
public struct OrbSpawn
{
    public Vector3 Position;
    public Team Team;
    /// <summary>Node that must be held for this spawn to be live; -1 for an always-on base spawn.</summary>
    public int NodeIndex;
}

/// <summary>A vehicle spawn pad: what parks here, facing which way, and how fast it comes back.</summary>
public struct VehicleSpawn
{
    public Vector3 Position;
    public float Yaw;
    public VehicleKind Kind;
    /// <summary>Which team may drive it. None means anyone.</summary>
    public Team Team;
    public float RespawnSeconds;
}

/// <summary>How an Assault objective is completed.</summary>
public enum ObjectiveKind
{
    /// <summary>Shoot it until it breaks: a generator, a panel, a door mechanism.</summary>
    Destroy,
    /// <summary>Remain in the radius for a while: planting a charge, holding a switch.</summary>
    Hold,
    /// <summary>Reach it. Grabbing the missile at the end of Convoy is this.</summary>
    Touch,
}

/// <summary>
/// One step in an Assault map's fixed sequence. Objectives are completed strictly in the order
/// they are declared — that ordering is the mode, and it is what lets the defenders concentrate.
/// </summary>
public struct AssaultObjectiveDef
{
    public Vector3 Position;
    public string Name;
    public ObjectiveKind Kind;
    public float Radius;
    /// <summary>Hit points for <see cref="ObjectiveKind.Destroy"/>.</summary>
    public float Health;
    /// <summary>Seconds of occupation for <see cref="ObjectiveKind.Hold"/>.</summary>
    public float HoldSeconds;
    /// <summary>
    /// Attacker spawns unlocked by finishing this one. The original pushes the attacking team's
    /// spawn forward after every objective; without that the last objective is unwinnable.
    /// </summary>
    public int UnlocksSpawnGroup;
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
    /// <summary>Static geometry triangle count, recorded even when built without a GL context.</summary>
    public int GeometryTriangles;
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
    public readonly List<VehicleSpawn> VehicleSpawns = new();
    public readonly List<PowerNodeDef> PowerNodes = new();
    public readonly List<OrbSpawn> OrbSpawns = new();
    public readonly List<AssaultObjectiveDef> Objectives = new();
    public readonly List<GoalHoop> GoalHoops = new();
    /// <summary>Bombing Run only: where the ball starts and where it returns to.</summary>
    public Vector3 BallSpawn;
    /// <summary>Assault only: which side attacks in round one. Defenders get the other colour.</summary>
    public Team AssaultAttackers = Team.Red;
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

    /// <summary>
    /// Picks a spawn for a team. <paramref name="assaultGroup"/> is the highest forward spawn the
    /// attackers have unlocked; pass -1 outside Assault to ignore grouping entirely. Attackers
    /// always come in at their furthest unlocked group, which is what stops the last objective on
    /// a long map from being a hopeless run back down the level.
    /// </summary>
    public SpawnPoint PickSpawn(Rng rng, Team team, IReadOnlyList<Vector3> avoid, float minDistance = 9f,
        int assaultGroup = -1)
    {
        var candidates = new List<int>(Spawns.Count);
        for (int i = 0; i < Spawns.Count; i++)
        {
            if (!(team == Team.None || Spawns[i].Team == Team.None || Spawns[i].Team == team)) continue;
            if (assaultGroup >= 0 && Spawns[i].AssaultGroup > assaultGroup) continue;
            candidates.Add(i);
        }

        // Attackers use the furthest unlocked group they have; the ones behind it are dead weight.
        if (assaultGroup > 0 && candidates.Count > 0)
        {
            int furthest = 0;
            foreach (int i in candidates) furthest = Math.Max(furthest, Spawns[i].AssaultGroup);
            if (furthest > 0) candidates.RemoveAll(i => Spawns[i].AssaultGroup != furthest);
        }

        if (candidates.Count == 0)
        {
            for (int i = 0; i < Spawns.Count; i++) candidates.Add(i);
            if (candidates.Count == 0)
                return new SpawnPoint { Position = Center + MathX.Up * 3f, Yaw = 0f, Team = Team.None };
        }

        // Prefer a genuinely clear spawn. A randomized scan used to sample the same candidate
        // repeatedly, miss the one free point, then telefrag a pawn during the initial countdown.
        // Shuffle once and visit every candidate exactly once instead.
        for (int i = candidates.Count - 1; i > 0; i--)
        {
            int j = rng.Range(0, i + 1);
            (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
        }
        int best = -1;
        float bestScore = float.MinValue;
        foreach (int idx in candidates)
        {
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

        // The old wedge tapered to exactly zero at its low end. A parallel underside gives both
        // side fascias and both ends a structural profile. A single 1.25 m depth was still too
        // slight on broad exposed approaches such as Peak and November, so scale the slab by its
        // smaller plan dimension while enforcing a substantial two-metre minimum everywhere.
        float run = risingAxis is 0 or 1 ? max.X - min.X : max.Z - min.Z;
        float width = risingAxis is 0 or 1 ? max.Z - min.Z : max.X - min.X;
        float rampThickness = MathX.Clamp(MathF.Min(run, width) * 0.22f, 2.0f, 3.0f);
        Vector3 visualMin = new(min.X, min.Y - rampThickness, min.Z);
        _mesh.AddRampSlab(min, max, risingAxis, rampThickness);
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

    /// <summary>
    /// Non-solid beam running between two arbitrary points. <see cref="Decor"/> is a min/max box
    /// and therefore axis-aligned, which is why the arenas had no diagonals in them at all: no
    /// truss bracing, no arch voussoirs, no buttresses, no raked roof edges. Those are most of what
    /// makes the originals' architecture read as built rather than stacked, so they get a primitive.
    /// Never collides — detail passes must not be able to disturb navigation.
    /// </summary>
    public void DecorBeam(Vector3 from, Vector3 to, float halfWidth, float halfHeight,
        MatId material, float uvScale = 1f)
    {
        Vector3 delta = to - from;
        float length = delta.Length();
        if (length < 1e-4f) return;

        Vector3 forward = delta / length;
        // Any reference up-vector works except one parallel to the run itself.
        Vector3 reference = MathF.Abs(forward.Y) > 0.98f ? MathX.Right : MathX.Up;
        Vector3 side = Vector3.Normalize(Vector3.Cross(reference, forward));
        Vector3 up = Vector3.Cross(forward, side);

        // Row-major basis: System.Numerics transforms row vectors, so the rows are the axes.
        var basis = new Matrix4x4(
            side.X, side.Y, side.Z, 0f,
            up.X, up.Y, up.Z, 0f,
            forward.X, forward.Y, forward.Z, 0f,
            0f, 0f, 0f, 1f);
        basis.Translation = (from + to) * 0.5f;

        _mesh.Material = (int)material;
        _mesh.WorldUv = true;
        _mesh.WorldUvScale = uvScale;
        _mesh.PushTransform(basis);
        _mesh.AddBox(Vector3.Zero, new Vector3(halfWidth, halfHeight, length * 0.5f));
        _mesh.PopTransform();
    }

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

    /// <summary>
    /// A wall running along Z with any number of doorways cut through it. <see cref="WallWithDoor"/>
    /// only handles one, and calling it twice over the same span seals both openings because each
    /// call fills the other's doorway — build the segments once instead.
    /// </summary>
    public void WallWithDoors(Vector3 min, Vector3 max, float doorHeight, MatId material,
        params (float Centre, float Width)[] doors)
    {
        var gaps = doors.Select(d => (Lo: d.Centre - d.Width * 0.5f, Hi: d.Centre + d.Width * 0.5f))
            .OrderBy(g => g.Lo).ToArray();
        float cursor = min.Z;
        foreach (var gap in gaps)
        {
            if (gap.Lo > cursor)
                Solid(new Vector3(min.X, min.Y, cursor), new Vector3(max.X, max.Y, gap.Lo), material);
            // Lintel over the opening.
            Solid(new Vector3(min.X, min.Y + doorHeight, gap.Lo),
                new Vector3(max.X, max.Y, gap.Hi), material);
            cursor = MathF.Max(cursor, gap.Hi);
        }
        if (cursor < max.Z)
            Solid(new Vector3(min.X, min.Y, cursor), max, material);
    }

    /// <summary>A run of steps between two heights. Cheaper on collision than a long ramp chain.</summary>
    public void Stairs(Vector3 start, Vector3 end, float width, int steps, MatId material, bool alongX = true)
    {
        // Authored counts are a visual preference, but no riser may exceed the collision world's
        // step height. Otherwise an ordinary staircase becomes a wall unless the player jumps.
        const float MaxRiser = 0.50f;
        steps = Math.Max(steps, (int)MathF.Ceiling(MathF.Abs(end.Y - start.Y) / MaxRiser));
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
        // A lit panel and a plate read as a texture swap on the ceiling. Real fixtures have a
        // housing, a reflector that steps out past the pane, and a cage over the glass; every
        // arena in the game places these, so the detail lands everywhere at once.
        Decor(position - new Vector3(size, 0.16f, size), position + new Vector3(size, 0.05f, size), MatId.EnergyPanel, 0.9f);
        Decor(position - new Vector3(size * 1.25f, 0.28f, size * 1.25f),
              position + new Vector3(size * 1.25f, 0.02f, size * 1.25f), MatId.Trim, 1.2f);
        Decor(position - new Vector3(size * 1.5f, 0.40f, size * 1.5f),
              position + new Vector3(size * 1.5f, -0.24f, size * 1.5f), MatId.Trim, 1.2f);
        // Cage bars across the pane, and corner mounts back up into the ceiling.
        for (int i = 0; i < 3; i++)
        {
            float o = MathX.Lerp(-size * 0.62f, size * 0.62f, i * 0.5f);
            Decor(position + new Vector3(o - 0.05f, -0.21f, -size),
                  position + new Vector3(o + 0.05f, -0.13f, size), MatId.Trim, 1.2f);
        }
        foreach (float sx in new[] { -1f, 1f })
            foreach (float sz in new[] { -1f, 1f })
                Decor(position + new Vector3(sx * size * 1.28f - 0.07f, -0.28f, sz * size * 1.28f - 0.07f),
                      position + new Vector3(sx * size * 1.28f + 0.07f, 0.34f, sz * size * 1.28f + 0.07f),
                      MatId.Trim, 1.2f);
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

    public void Spawn(Vector3 position, float yawDegrees = 0f, Team team = Team.None,
        int assaultGroup = 0)
        => _level.Spawns.Add(new SpawnPoint
        {
            Position = position,
            Yaw = yawDegrees * MathX.Deg2Rad,
            Team = team,
            AssaultGroup = assaultGroup,
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

    /// <summary>
    /// A weapon locker: touching it hands over every weapon on the rack with a full load. This is
    /// how UT2004 and UT3 arm their maps — placing the same weapons as separate pickups would give
    /// a completely different pace, because a locker arms you instantly and then goes on cooldown.
    /// </summary>
    public void Locker(Vector3 position, params WeaponKind[] weapons)
        => _level.Pickups.Add(new PickupPlacement
        {
            Position = position,
            Kind = PickupKind.WeaponLocker,
            Weapon = weapons is { Length: > 0 } ? weapons[0] : WeaponKind.Count,
            Ammo = AmmoKind.None,
            RespawnTime = 30f,
            LockerWeapons = weapons ?? [],
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
    /// A Bombing Run hoop: a standing ring on two posts, lit in the defending team's colour.
    /// <paramref name="team"/> is whoever defends it. The ring itself carries no collision —
    /// the ball and the players both have to be able to pass through the middle of it.
    /// </summary>
    public void AddGoalHoop(Vector3 position, Team team, float yawDegrees = 0f)
    {
        _level.GoalHoops.Add(new GoalHoop
        {
            Position = position, Team = team, Yaw = yawDegrees * MathX.Deg2Rad,
        });
        Vector3 col = GameTypes.TeamColor(team);
        _mesh.Material = (int)MatId.Trim;
        _mesh.WorldUv = false;
        _mesh.AddTorus(position, 2.3f, 0.22f, 28, 10);
        _mesh.WorldUv = true;
        // Two posts down to the deck so the ring reads as a structure rather than a floating
        // decal. They are decor: a post you could snag on would change how the mode plays.
        foreach (float s in new[] { -1f, 1f })
            Decor(position + new Vector3(s * 2.3f - 0.16f, -position.Y, -0.16f),
                  position + new Vector3(s * 2.3f + 0.16f, -0.2f, 0.16f), MatId.TechPanelDark, 0.9f);
        AddLight(position, col, 14f, 4.2f);
    }

    /// <summary>Where the Bombing Run ball starts. One per arena, at midfield.</summary>
    public void AddBallSpawn(Vector3 position)
    {
        _level.BallSpawn = position;
        Decor(position - new Vector3(1.6f, 0.20f, 1.6f), position + new Vector3(1.6f, -0.06f, 1.6f),
            MatId.EnergyPanel, 1.2f);
        AddLight(position + new Vector3(0f, 1.4f, 0f), new Vector3(0.95f, 0.85f, 0.45f), 12f, 3.4f);
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

    /// <summary>
    /// Adds one power node or core. Links are indices into the order these are added, so a map
    /// declares its chain by construction order — Torlan's five nodes go 1-2-3-4-5 with the
    /// centre wired to both cores.
    /// </summary>
    public int AddPowerNode(Vector3 position, string name, int[] links, bool isCore = false,
        Team team = Team.None, NodeRole role = NodeRole.Link, float countdownSeconds = 0f,
        float coreDamageFraction = 0f, VehicleKind rewardVehicle = VehicleKind.Count,
        Vector3 rewardPosition = default, float rewardYawDegrees = 0f)
    {
        _level.PowerNodes.Add(new PowerNodeDef
        {
            Position = position, Name = name, IsCore = isCore, Team = team, Links = links ?? [],
            Role = role, CountdownSeconds = countdownSeconds, CoreDamageFraction = coreDamageFraction,
            RewardVehicle = rewardVehicle,
            RewardPosition = rewardPosition == default ? position + new Vector3(0f, 0f, 8f) : rewardPosition,
            RewardYaw = rewardYawDegrees * MathX.Deg2Rad,
        });
        // A node reads as a pylon: a base ring, a column and a cap. Colour is applied at runtime.
        float r = isCore ? 3.4f : 2.4f;
        float h = isCore ? 6.5f : 4.6f;
        Decor(position - new Vector3(r, 0.24f, r), position + new Vector3(r, 0.10f, r), MatId.Trim, 1.1f);
        Decor(position - new Vector3(0.55f, 0f, 0.55f), position + new Vector3(0.55f, h, 0.55f),
            MatId.TechPanelDark, 0.8f);
        Decor(position + new Vector3(-1.1f, h, -1.1f), position + new Vector3(1.1f, h + 0.7f, 1.1f),
            MatId.EnergyPanel, 0.7f);
        // Auxiliary nodes wear a second collar so a player can tell from across the valley that
        // this one is grabbable without a link — which is the whole reason to detour to it.
        if (!isCore && role != NodeRole.Link)
            Decor(position + new Vector3(-1.5f, h * 0.55f, -1.5f),
                position + new Vector3(1.5f, h * 0.55f + 0.4f, 1.5f), MatId.Trim, 0.9f);
        AddLight(position + new Vector3(0, h + 1.6f, 0),
            isCore && team != Team.None ? GameTypes.TeamColor(team) : new Vector3(0.85f, 0.85f, 0.9f),
            isCore ? 26f : 18f, isCore ? 6f : 4f);
        return _level.PowerNodes.Count - 1;
    }

    /// <summary>
    /// Adds an orb spawn. Pass the index of a node to make it live only while that node is held;
    /// the default -1 is a base spawn that is always available.
    /// </summary>
    public void AddOrbSpawn(Vector3 position, Team team, int nodeIndex = -1)
        => _level.OrbSpawns.Add(new OrbSpawn { Position = position, Team = team, NodeIndex = nodeIndex });

    /// <summary>
    /// Wires a node's links after the fact. Links are indices, so a chain that refers forwards
    /// cannot be declared at construction time — every node has to exist first.
    /// </summary>
    public void LinkPowerNodes(int index, int[] links)
    {
        if (index < 0 || index >= _level.PowerNodes.Count) return;
        var def = _level.PowerNodes[index];
        def.Links = links ?? [];
        _level.PowerNodes[index] = def;
    }

    /// <summary>
    /// Adds the next Assault objective. Declaration order is completion order, so a map reads as
    /// its own walkthrough. The marker geometry differs by kind so a player can tell at a glance
    /// whether to shoot the thing or stand on it.
    /// </summary>
    public int AddObjective(Vector3 position, string name, ObjectiveKind kind, float radius = 3.4f,
        float health = 900f, float holdSeconds = 8f, int unlocksSpawnGroup = 0)
    {
        _level.Objectives.Add(new AssaultObjectiveDef
        {
            Position = position, Name = name, Kind = kind, Radius = radius,
            Health = health, HoldSeconds = holdSeconds, UnlocksSpawnGroup = unlocksSpawnGroup,
        });

        switch (kind)
        {
            case ObjectiveKind.Destroy:
                // A housing with an exposed panel: obviously something to shoot.
                Decor(position - new Vector3(1.5f, 0f, 1.5f), position + new Vector3(1.5f, 3.0f, 1.5f),
                    MatId.TechPanelDark, 0.75f);
                Decor(position + new Vector3(-1.1f, 0.7f, 1.5f), position + new Vector3(1.1f, 2.4f, 1.62f),
                    MatId.EnergyPanel, 0.6f);
                break;
            case ObjectiveKind.Hold:
                // A marked floor plate, so its footprint is the thing you read.
                Decor(position - new Vector3(radius, 0.24f, radius), position + new Vector3(radius, 0.08f, radius),
                    MatId.Trim, 1.1f);
                Decor(position - new Vector3(radius * 0.7f, 0f, radius * 0.7f),
                    position + new Vector3(radius * 0.7f, 0.14f, radius * 0.7f), MatId.EnergyPanel, 0.6f);
                break;
            default:
                Decor(position - new Vector3(0.5f, 0f, 0.5f), position + new Vector3(0.5f, 1.8f, 0.5f),
                    MatId.EnergyPanel, 0.7f);
                break;
        }
        AddLight(position + new Vector3(0, 2.6f, 0), new Vector3(1f, 0.75f, 0.35f), 15f, 3.6f);
        return _level.Objectives.Count - 1;
    }

    /// <summary>
    /// Parks a vehicle. The pad is drawn but not collided — a solid slab under a wheeled
    /// vehicle would leave it permanently resting on a lip it has to climb off.
    /// </summary>
    public void AddVehicle(VehicleKind kind, Vector3 position, float yawDegrees = 0f,
        Team team = Team.None, float respawnSeconds = 30f)
    {
        _level.VehicleSpawns.Add(new VehicleSpawn
        {
            Position = position, Yaw = yawDegrees, Kind = kind, Team = team, RespawnSeconds = respawnSeconds,
        });
        var def = VehicleDef.Get(kind);
        float r = MathF.Max(def.HalfExtents.X, def.HalfExtents.Z) + 0.8f;
        Decor(position - new Vector3(r, 0.18f, r), position + new Vector3(r, -0.02f, r), MatId.Trim, 1.1f);
        Vector3 col = team == Team.None ? new Vector3(0.7f, 0.72f, 0.8f) : GameTypes.TeamColor(team);
        AddLight(position + new Vector3(0, 1.4f, 0), col, 9f, 2.4f);
    }

    // ---------------------------------------------------------------- finalise

    /// <summary>
    /// Drops any vehicle parked out of a player's reach onto the surface underneath it.
    ///
    /// Aircraft in particular were authored at the height they look right hovering at — Torlan's
    /// Raptors and Cicadas sat ten to twelve metres up, and one Raptor thirty-four metres above
    /// the tower deck — which reads well from a distance and is completely unboardable, because
    /// boarding needs a pawn within a few metres of the hull. Run after the final collision
    /// rebuild so every pad, roof and deck the map authored is already there to land on.
    /// </summary>
    /// <summary>
    /// The surface a vehicle parked at <paramref name="position"/> should be resting on, or null
    /// when there is nothing below it at all.
    ///
    /// A single downward ray is not enough. One that starts inside a brush reports no hit, and
    /// vehicles authored at water level in a filled riverbed start exactly there — so the probe
    /// climbs its start point until it is in open space, then takes the first surface below.
    /// </summary>
    public static float? SurfaceUnderVehicle(CollisionWorld world, Vector3 position, float halfHeight)
    {
        var scratch = new List<int>(8);
        Vector3 probeHalf = new(0.05f, 0.05f, 0.05f);
        for (float lift = halfHeight + 0.05f; lift <= halfHeight + 40f; lift += 1.5f)
        {
            Vector3 from = position + new Vector3(0f, lift, 0f);
            // The start has to be in open space, or the ray reports nothing and a buried vehicle
            // looks like one floating over a bottomless hole.
            if (world.BoxOverlapsSolid(from - probeHalf, from + probeHalf, scratch)) continue;
            var hit = world.Raycast(from, from - new Vector3(0f, 500f, 0f));
            if (hit.Hit) return hit.Point.Y;
            return null;                                  // open space above, nothing below
        }
        return null;
    }

    private void SettleVehicleSpawns()
    {
        // Matches GameWorld.VehicleToBoard's reach, less a margin so a spawn is comfortably
        // inside it rather than exactly on the limit.
        const float reach = 2.6f;
        for (int i = 0; i < _level.VehicleSpawns.Count; i++)
        {
            VehicleSpawn spawn = _level.VehicleSpawns[i];
            float halfHeight = Game.VehicleDef.Get(spawn.Kind).HalfExtents.Y;
            float? surface = SurfaceUnderVehicle(_level.Collision, spawn.Position, halfHeight);
            if (surface is not { } ground) continue;      // nothing below: a map bug the test names
            float gap = spawn.Position.Y - ground - halfHeight;
            if (gap > 0f && gap <= reach) continue;
            spawn.Position.Y = ground + halfHeight + 0.35f;
            _level.VehicleSpawns[i] = spawn;
        }
    }

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
        SettleVehicleSpawns();

        // Recorded before the headless early-out so map density can be reported without a GPU.
        _level.GeometryTriangles = _mesh.TriangleCount;

        // Headless callers pass a null GL. They want the gameplay placements — vehicle pads, node
        // graph, objectives — not the geometry, so the upload and the nav bake are both skipped.
        // Nothing else in Build touches the GPU, so this stays a cheap data query.
        if (gl == null) return _level;

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
            _level.Nav.AddSpecialLink(pad2.Position, pad2.Destination, NavFlags.JumpPad,
                discourageOrdinaryTraversal: true);
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
