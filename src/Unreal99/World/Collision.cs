using System.Numerics;
using Unreal99.Core;

namespace Unreal99.World;

public enum BrushKind
{
    Solid,
    Ramp,
    Lava,       // solid floor, applies burning damage on contact
    Water,      // non-solid, applies buoyancy and drag
    Void,       // non-solid, instantly kills anything that enters (the pit below the arena)
    Nonsolid,   // purely decorative geometry
}

/// <summary>
/// One convex piece of level geometry. Ramps are an AABB whose top face is sloped along one
/// horizontal axis, which covers every stair and incline the arenas need without a full BSP.
/// </summary>
public struct Brush
{
    public Vector3 Min;
    public Vector3 Max;
    public BrushKind Kind;
    /// <summary>0 = +X high, 1 = -X high, 2 = +Z high, 3 = -Z high. Only used by ramps.</summary>
    public int RampAxis;
    /// <summary>Index into <see cref="Level.Movers"/>, or -1 for static geometry.</summary>
    public int MoverIndex;

    public readonly Vector3 Center => (Min + Max) * 0.5f;
    public readonly Vector3 Size => Max - Min;
    public readonly bool Solid => Kind is BrushKind.Solid or BrushKind.Ramp or BrushKind.Lava;

    public static Brush Box(Vector3 min, Vector3 max, BrushKind kind = BrushKind.Solid) => new()
    {
        Min = Vector3.Min(min, max),
        Max = Vector3.Max(min, max),
        Kind = kind,
        RampAxis = -1,
        MoverIndex = -1,
    };

    public static Brush Ramp(Vector3 min, Vector3 max, int risingAxis) => new()
    {
        Min = Vector3.Min(min, max),
        Max = Vector3.Max(min, max),
        Kind = BrushKind.Ramp,
        RampAxis = risingAxis,
        MoverIndex = -1,
    };

    /// <summary>Top surface height at a horizontal position. For ramps this interpolates the slope.</summary>
    public readonly float TopAt(float x, float z)
    {
        if (Kind != BrushKind.Ramp) return Max.Y;
        float t = RampAxis switch
        {
            0 => (x - Min.X) / MathF.Max(Max.X - Min.X, 1e-4f),
            1 => 1f - (x - Min.X) / MathF.Max(Max.X - Min.X, 1e-4f),
            2 => (z - Min.Z) / MathF.Max(Max.Z - Min.Z, 1e-4f),
            _ => 1f - (z - Min.Z) / MathF.Max(Max.Z - Min.Z, 1e-4f),
        };
        return MathX.Lerp(Min.Y, Max.Y, MathX.Saturate(t));
    }

    /// <summary>Upward surface normal, accounting for the ramp slope.</summary>
    public readonly Vector3 TopNormal()
    {
        if (Kind != BrushKind.Ramp) return MathX.Up;
        float run = RampAxis <= 1 ? Max.X - Min.X : Max.Z - Min.Z;
        float rise = Max.Y - Min.Y;
        Vector3 n = RampAxis switch
        {
            0 => new Vector3(-rise, run, 0),
            1 => new Vector3(rise, run, 0),
            2 => new Vector3(0, run, -rise),
            _ => new Vector3(0, run, rise),
        };
        return Vector3.Normalize(n);
    }

    public readonly bool ContainsPoint(Vector3 p)
        => p.X >= Min.X && p.X <= Max.X && p.Y >= Min.Y && p.Y <= Max.Y && p.Z >= Min.Z && p.Z <= Max.Z;

    public readonly bool OverlapsXZ(Vector3 min, Vector3 max)
        => min.X < Max.X && max.X > Min.X && min.Z < Max.Z && max.Z > Min.Z;

    public readonly bool Overlaps(Vector3 min, Vector3 max)
        => min.X < Max.X && max.X > Min.X && min.Y < Max.Y && max.Y > Min.Y && min.Z < Max.Z && max.Z > Min.Z;
}

public struct RayHit
{
    public bool Hit;
    public Vector3 Point;
    public Vector3 Normal;
    public float Distance;
    public int BrushIndex;
    public BrushKind Kind;
}

public struct MoveResult
{
    public Vector3 Position;
    public Vector3 Velocity;
    public bool OnGround;
    public Vector3 GroundNormal;
    public int GroundBrush;
    public bool HitWall;
    public Vector3 WallNormal;
    public bool HitCeiling;
    /// <summary>Impact speed along the ground normal at the moment of landing; drives fall damage.</summary>
    public float LandingSpeed;
}

/// <summary>
/// Broadphase + swept-box collision over a brush soup. A uniform XZ grid keeps queries cheap
/// enough that dozens of pawns, projectiles and particles can all collide every frame.
/// </summary>
public sealed class CollisionWorld
{
    private const float CellSize = 8f;
    private const float SkinWidth = 0.002f;

    private readonly List<Brush> _brushes = new();
    private readonly Dictionary<long, List<int>> _grid = new();
    private Vector3 _worldMin = new(float.MaxValue), _worldMax = new(float.MinValue);
    // Stamped visited set: lets Query dedupe brushes spanning several cells without an O(n^2) scan.
    private int[] _visited = [];
    private int _visitStamp;

    /// <summary>Cosine of the steepest slope a pawn can stand on (45 degrees).</summary>
    public float MaxWalkableY = 0.707f;
    public float StepHeight = 0.55f;

    public IReadOnlyList<Brush> Brushes => _brushes;
    public Vector3 WorldMin => _worldMin;
    public Vector3 WorldMax => _worldMax;

    public int Add(Brush b)
    {
        _brushes.Add(b);
        return _brushes.Count - 1;
    }

    public void UpdateBrush(int index, Brush b)
    {
        _brushes[index] = b;
        // Movers are rare and small; a full rebuild is simpler than incremental grid edits.
        Rebuild();
    }

    public void SetBrushOffset(int index, Vector3 offset, Vector3 baseMin, Vector3 baseMax)
    {
        var b = _brushes[index];
        b.Min = baseMin + offset;
        b.Max = baseMax + offset;
        _brushes[index] = b;
    }

    private static long CellKey(int cx, int cz) => ((long)cx << 32) ^ (uint)cz;

    public void Rebuild()
    {
        _grid.Clear();
        if (_visited.Length < _brushes.Count) _visited = new int[Math.Max(_brushes.Count, 64)];
        _worldMin = new Vector3(float.MaxValue);
        _worldMax = new Vector3(float.MinValue);
        for (int i = 0; i < _brushes.Count; i++)
        {
            var b = _brushes[i];
            _worldMin = Vector3.Min(_worldMin, b.Min);
            _worldMax = Vector3.Max(_worldMax, b.Max);
            int x0 = (int)MathF.Floor(b.Min.X / CellSize), x1 = (int)MathF.Floor(b.Max.X / CellSize);
            int z0 = (int)MathF.Floor(b.Min.Z / CellSize), z1 = (int)MathF.Floor(b.Max.Z / CellSize);
            for (int cx = x0; cx <= x1; cx++)
                for (int cz = z0; cz <= z1; cz++)
                {
                    long k = CellKey(cx, cz);
                    if (!_grid.TryGetValue(k, out var list)) _grid[k] = list = new List<int>(8);
                    list.Add(i);
                }
        }
    }

    /// <summary>Collects brush indices whose cells overlap the query box into <paramref name="output"/>.</summary>
    public void Query(Vector3 min, Vector3 max, List<int> output)
    {
        output.Clear();
        if (_visited.Length < _brushes.Count) _visited = new int[Math.Max(_brushes.Count, 64)];
        _visitStamp++;
        int x0 = (int)MathF.Floor(min.X / CellSize), x1 = (int)MathF.Floor(max.X / CellSize);
        int z0 = (int)MathF.Floor(min.Z / CellSize), z1 = (int)MathF.Floor(max.Z / CellSize);
        for (int cx = x0; cx <= x1; cx++)
            for (int cz = z0; cz <= z1; cz++)
            {
                if (!_grid.TryGetValue(CellKey(cx, cz), out var list)) continue;
                foreach (int i in list)
                {
                    if (_visited[i] == _visitStamp) continue;
                    _visited[i] = _visitStamp;
                    output.Add(i);
                }
            }
    }

    // ---------------------------------------------------------------- raycast

    private readonly List<int> _rayScratch = new(64);

    /// <summary>Ray versus solid brushes. Returns the nearest hit between from and to.</summary>
    public RayHit Raycast(Vector3 from, Vector3 to, bool includeNonSolid = false)
    {
        RayHit best = default;
        best.Distance = float.MaxValue;

        Vector3 delta = to - from;
        float length = delta.Length();
        if (length < 1e-6f) return default;
        Vector3 dir = delta / length;

        Vector3 qmin = Vector3.Min(from, to) - new Vector3(0.05f);
        Vector3 qmax = Vector3.Max(from, to) + new Vector3(0.05f);
        var candidates = _rayScratch;
        Query(qmin, qmax, candidates);

        foreach (int i in candidates)
        {
            var b = _brushes[i];
            if (!includeNonSolid && !b.Solid) continue;
            if (b.Kind == BrushKind.Ramp)
            {
                if (RayVsRamp(b, from, dir, length, out float t, out Vector3 n) && t < best.Distance)
                {
                    best.Hit = true; best.Distance = t; best.Normal = n;
                    best.Point = from + dir * t; best.BrushIndex = i; best.Kind = b.Kind;
                }
            }
            else if (RayVsBox(b.Min, b.Max, from, dir, length, out float t2, out Vector3 n2) && t2 < best.Distance)
            {
                best.Hit = true; best.Distance = t2; best.Normal = n2;
                best.Point = from + dir * t2; best.BrushIndex = i; best.Kind = b.Kind;
            }
        }

        if (!best.Hit) best.Distance = length;
        return best;
    }

    public static bool RayVsBox(Vector3 min, Vector3 max, Vector3 origin, Vector3 dir, float maxDist,
        out float t, out Vector3 normal)
    {
        t = 0f; normal = Vector3.Zero;
        float tmin = 0f, tmax = maxDist;
        int axis = -1;
        float sign = 1f;

        for (int a = 0; a < 3; a++)
        {
            float o = a == 0 ? origin.X : a == 1 ? origin.Y : origin.Z;
            float d = a == 0 ? dir.X : a == 1 ? dir.Y : dir.Z;
            float lo = a == 0 ? min.X : a == 1 ? min.Y : min.Z;
            float hi = a == 0 ? max.X : a == 1 ? max.Y : max.Z;

            if (MathF.Abs(d) < 1e-8f)
            {
                if (o < lo || o > hi) return false;
                continue;
            }
            float inv = 1f / d;
            float t1 = (lo - o) * inv;
            float t2 = (hi - o) * inv;
            float s = -1f;
            if (t1 > t2) { (t1, t2) = (t2, t1); s = 1f; }
            if (t1 > tmin) { tmin = t1; axis = a; sign = s; }
            if (t2 < tmax) tmax = t2;
            if (tmin > tmax) return false;
        }

        if (axis < 0) return false;
        t = tmin;
        normal = axis switch
        {
            0 => new Vector3(sign, 0, 0),
            1 => new Vector3(0, sign, 0),
            _ => new Vector3(0, 0, sign),
        };
        return t >= 0f && t <= maxDist;
    }

    private static bool RayVsRamp(in Brush b, Vector3 origin, Vector3 dir, float maxDist,
        out float t, out Vector3 normal)
    {
        // Slab-clip against the bounding box, then clip against the slope plane.
        if (!RayVsBox(b.Min, b.Max, origin, dir, maxDist, out float tBox, out Vector3 nBox))
        {
            // The origin may already be inside the box.
            if (!b.ContainsPoint(origin)) { t = 0; normal = Vector3.Zero; return false; }
            tBox = 0f; nBox = MathX.Up;
        }

        Vector3 planeN = b.TopNormal();
        Vector3 planePoint = new(b.Center.X, b.TopAt(b.Center.X, b.Center.Z), b.Center.Z);
        float denom = Vector3.Dot(dir, planeN);
        float dist = Vector3.Dot(planePoint - origin, planeN);

        if (MathF.Abs(denom) > 1e-6f)
        {
            float tp = dist / denom;
            if (tp >= 0f && tp <= maxDist)
            {
                Vector3 p = origin + dir * tp;
                if (p.X >= b.Min.X - 0.01f && p.X <= b.Max.X + 0.01f &&
                    p.Z >= b.Min.Z - 0.01f && p.Z <= b.Max.Z + 0.01f &&
                    p.Y >= b.Min.Y - 0.01f && p.Y <= b.Max.Y + 0.01f && denom < 0f)
                {
                    t = tp; normal = planeN; return true;
                }
            }
        }

        // Fall back to the box hit but only where it is below the slope surface.
        Vector3 hitPoint = origin + dir * tBox;
        if (hitPoint.Y <= b.TopAt(hitPoint.X, hitPoint.Z) + 0.01f && nBox.Y < 0.9f)
        {
            t = tBox; normal = nBox; return true;
        }
        t = 0; normal = Vector3.Zero;
        return false;
    }

    // ---------------------------------------------------------------- box queries

    /// <summary>True if the axis-aligned box overlaps any solid brush.</summary>
    public bool BoxOverlapsSolid(Vector3 min, Vector3 max, List<int> scratch = null)
    {
        scratch ??= new List<int>(32);
        Query(min, max, scratch);
        foreach (int i in scratch)
        {
            var b = _brushes[i];
            if (!b.Solid) continue;
            if (!b.Overlaps(min, max)) continue;
            if (b.Kind == BrushKind.Ramp)
            {
                // The box only counts as blocked if it dips under the slope.
                float cx = MathX.Clamp((min.X + max.X) * 0.5f, b.Min.X, b.Max.X);
                float cz = MathX.Clamp((min.Z + max.Z) * 0.5f, b.Min.Z, b.Max.Z);
                if (min.Y < b.TopAt(cx, cz) - 0.001f) return true;
            }
            else return true;
        }
        return false;
    }

    /// <summary>Kinds of non-solid volume the box currently sits inside.</summary>
    public BrushKind VolumeAt(Vector3 min, Vector3 max, List<int> scratch = null)
    {
        scratch ??= new List<int>(16);
        Query(min, max, scratch);
        BrushKind result = BrushKind.Nonsolid;
        foreach (int i in scratch)
        {
            var b = _brushes[i];
            if (b.Kind is BrushKind.Water or BrushKind.Void && b.Overlaps(min, max))
            {
                if (b.Kind == BrushKind.Void) return BrushKind.Void;
                result = b.Kind;
            }
        }
        return result;
    }

    /// <summary>True if the box is touching lava (either standing on it or inside it).</summary>
    public bool TouchingLava(Vector3 min, Vector3 max, List<int> scratch = null)
    {
        scratch ??= new List<int>(16);
        Query(min - new Vector3(0, 0.08f, 0), max, scratch);
        foreach (int i in scratch)
        {
            var b = _brushes[i];
            if (b.Kind != BrushKind.Lava) continue;
            if (b.Overlaps(min - new Vector3(0, 0.08f, 0), max)) return true;
        }
        return false;
    }

    // ---------------------------------------------------------------- movement

    private readonly List<int> _moveScratch = new(64);

    /// <summary>
    /// Moves an axis-aligned box through the world with collide-and-slide.
    /// Each axis is resolved separately, which produces clean wall sliding, and the whole
    /// move is sub-stepped so fast projectiles and dodges never tunnel through thin brushes.
    /// </summary>
    public MoveResult MoveBox(Vector3 position, Vector3 halfExtents, Vector3 velocity, float dt,
        bool stepUp = true, float gravityDir = -1f)
    {
        MoveResult r = default;
        r.Position = position;
        r.Velocity = velocity;
        r.GroundBrush = -1;
        r.GroundNormal = MathX.Up;

        Vector3 delta = velocity * dt;
        float maxStep = 0.12f;
        int steps = Math.Clamp((int)MathF.Ceiling(delta.Length() / maxStep), 1, 64);
        Vector3 stepDelta = delta / steps;

        for (int s = 0; s < steps; s++)
        {
            // --- X ---
            r.Position.X += stepDelta.X;
            if (Blocked(r.Position, halfExtents, out float pushX, 0))
            {
                if (stepUp && TryStepUp(ref r, halfExtents, 0)) { }
                else
                {
                    r.Position.X += pushX;
                    r.Velocity.X = 0f;
                    r.HitWall = true;
                    r.WallNormal = new Vector3(MathF.Sign(pushX), 0, 0);
                }
            }

            // --- Z ---
            r.Position.Z += stepDelta.Z;
            if (Blocked(r.Position, halfExtents, out float pushZ, 2))
            {
                if (stepUp && TryStepUp(ref r, halfExtents, 2)) { }
                else
                {
                    r.Position.Z += pushZ;
                    r.Velocity.Z = 0f;
                    r.HitWall = true;
                    r.WallNormal = new Vector3(0, 0, MathF.Sign(pushZ));
                }
            }

            // --- Y ---
            float beforeY = r.Velocity.Y;
            r.Position.Y += stepDelta.Y;
            if (Blocked(r.Position, halfExtents, out float pushY, 1))
            {
                r.Position.Y += pushY;
                if (pushY > 0f)
                {
                    if (!r.OnGround) r.LandingSpeed = MathF.Max(r.LandingSpeed, -beforeY);
                    r.OnGround = true;
                    r.GroundNormal = FindGroundNormal(r.Position, halfExtents, out int gb);
                    r.GroundBrush = gb;
                }
                else r.HitCeiling = true;
                r.Velocity.Y = 0f;
                stepDelta.Y = 0f;
            }
        }

        // Final ground probe: a short downward test keeps the pawn glued when walking down slopes.
        if (!r.OnGround && r.Velocity.Y <= 0.01f)
        {
            Vector3 probe = r.Position - new Vector3(0, 0.09f, 0);
            if (Blocked(probe, halfExtents, out float pushProbe, 1) && pushProbe > 0f)
            {
                r.Position = probe;
                r.Position.Y += pushProbe;
                r.OnGround = true;
                r.GroundNormal = FindGroundNormal(r.Position, halfExtents, out int gb);
                r.GroundBrush = gb;
                if (r.Velocity.Y < 0f) { r.LandingSpeed = -r.Velocity.Y; r.Velocity.Y = 0f; }
            }
        }
        _ = gravityDir;
        return r;
    }

    /// <summary>
    /// Tests the box for overlap and, if blocked, returns the smallest push along
    /// <paramref name="axis"/> that separates it.
    /// </summary>
    private bool Blocked(Vector3 center, Vector3 half, out float push, int axis)
    {
        push = 0f;
        Vector3 min = center - half, max = center + half;
        Query(min, max, _moveScratch);
        bool any = false;
        float bestPush = 0f;

        foreach (int i in _moveScratch)
        {
            var b = _brushes[i];
            if (!b.Solid) continue;
            if (!b.Overlaps(min, max)) continue;

            float p;
            if (b.Kind == BrushKind.Ramp)
            {
                // Sample the slope under the box's footprint corners; the highest wins.
                float top = MathF.Max(
                    MathF.Max(b.TopAt(MathX.Clamp(min.X, b.Min.X, b.Max.X), MathX.Clamp(min.Z, b.Min.Z, b.Max.Z)),
                              b.TopAt(MathX.Clamp(max.X, b.Min.X, b.Max.X), MathX.Clamp(min.Z, b.Min.Z, b.Max.Z))),
                    MathF.Max(b.TopAt(MathX.Clamp(min.X, b.Min.X, b.Max.X), MathX.Clamp(max.Z, b.Min.Z, b.Max.Z)),
                              b.TopAt(MathX.Clamp(max.X, b.Min.X, b.Max.X), MathX.Clamp(max.Z, b.Min.Z, b.Max.Z))));
                if (min.Y >= top - 0.001f) continue;
                if (axis != 1)
                {
                    // Slopes never block horizontally; the Y resolve lifts the pawn instead.
                    continue;
                }
                p = top - min.Y + SkinWidth;
            }
            else
            {
                p = axis switch
                {
                    0 => ShortestPush(min.X, max.X, b.Min.X, b.Max.X),
                    1 => ShortestPush(min.Y, max.Y, b.Min.Y, b.Max.Y),
                    _ => ShortestPush(min.Z, max.Z, b.Min.Z, b.Max.Z),
                };
            }

            if (MathF.Abs(p) > MathF.Abs(bestPush)) bestPush = p;
            any = true;
        }

        push = bestPush;
        return any && MathF.Abs(bestPush) > 1e-5f;
    }

    private static float ShortestPush(float aMin, float aMax, float bMin, float bMax)
    {
        float pushPos = bMax - aMin + SkinWidth;   // push A in +axis
        float pushNeg = bMin - aMax - SkinWidth;   // push A in -axis
        return MathF.Abs(pushPos) < MathF.Abs(pushNeg) ? pushPos : pushNeg;
    }

    /// <summary>Lets the pawn walk up small ledges without jumping.</summary>
    private bool TryStepUp(ref MoveResult r, Vector3 half, int axis)
    {
        if (!r.OnGround) return false;
        Vector3 raised = r.Position + new Vector3(0, StepHeight, 0);
        if (Blocked(raised, half, out _, axis)) return false;
        // Make sure there is floor under the raised position before committing to the step.
        Vector3 settle = raised - new Vector3(0, StepHeight * 0.9f, 0);
        if (!Blocked(settle, half, out float down, 1) || down <= 0f) return false;
        r.Position = settle;
        r.Position.Y += down;
        return true;
    }

    private Vector3 FindGroundNormal(Vector3 center, Vector3 half, out int brushIndex)
    {
        brushIndex = -1;
        Vector3 min = center - half - new Vector3(0, 0.08f, 0);
        Vector3 max = center + half;
        Query(min, max, _moveScratch);
        float bestTop = float.MinValue;
        Vector3 normal = MathX.Up;
        foreach (int i in _moveScratch)
        {
            var b = _brushes[i];
            if (!b.Solid) continue;
            if (!b.OverlapsXZ(min, max)) continue;
            float top = b.TopAt(MathX.Clamp(center.X, b.Min.X, b.Max.X), MathX.Clamp(center.Z, b.Min.Z, b.Max.Z));
            if (top > center.Y - half.Y + 0.2f || top < center.Y - half.Y - 0.3f) continue;
            if (top > bestTop)
            {
                bestTop = top;
                normal = b.TopNormal();
                brushIndex = i;
            }
        }
        return normal;
    }

    /// <summary>Highest walkable surface below <paramref name="from"/>, or NaN if there is none.</summary>
    public float FloorHeight(Vector3 from, float maxDrop = 60f)
    {
        RayHit h = Raycast(from, from - new Vector3(0, maxDrop, 0));
        return h.Hit ? h.Point.Y : float.NaN;
    }

    /// <summary>Unobstructed straight line between two points (bot line of sight).</summary>
    public bool LineOfSight(Vector3 a, Vector3 b)
    {
        RayHit h = Raycast(a, b);
        return !h.Hit;
    }
}
