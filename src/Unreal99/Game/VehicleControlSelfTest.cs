using System.Numerics;
using Unreal99.Core;
using Unreal99.World;

namespace Unreal99.Game;

/// <summary>
/// The contract between a player and the vehicle they are sitting in. Every check here started
/// life as something that was actually wrong: the crew's view did not follow the hull, so
/// steering a Hellbender rotated the truck underneath a camera that never moved and read as
/// "steering does nothing"; mouse look did not reach a rider at all; and a multi-seat vehicle
/// gave whoever boarded whichever seat happened to be free, with no way to move afterwards.
/// </summary>
public static class VehicleControlSelfTest
{
    /// <summary>
    /// Floor for a first-person interior's triangle count. The exteriors run from about 1,700
    /// triangles for a Manta up to 8,500 for a Leviathan, with most of the roster between 2,500
    /// and 7,000. The interior is what a driver stares at for an entire match, from much closer
    /// than anyone ever sees the outside, so a blocked-out set of crates in front of a hull built
    /// to that standard reads as unfinished. This sits in the same band as the mid-weight hulls.
    /// </summary>
    public const int MinimumCockpitTriangles = 2400;

    /// <summary>
    /// Floor for an exterior. The Axon vehicles carry their density in wheels, suspension and
    /// turret hardware and land between 2,500 and 8,500; the smooth Necris shells had none of
    /// that and sat at 728 to 888, which looked unfinished parked next to a Scorpion. The
    /// hoverboard is exempt — it is a plank, not a vehicle silhouette.
    /// </summary>
    public const int MinimumHullTriangles = 1700;

    public static int Run()
    {
        var failures = new List<string>();

        // ---------------------------------------------------------------- steering turns the hull
        using (Level level = Maps.Build(null, MapId.Torlan))
        {
            foreach (VehicleKind kind in Enum.GetValues<VehicleKind>())
            {
                if (kind == VehicleKind.Count) continue;
                var v = new Vehicle();
                v.Configure(kind, new Vector3(0f, 40f, 0f), 0f);
                v.Reset();
                float before = v.Yaw;
                // Full left stick for a quarter second, which every class should answer.
                for (int i = 0; i < 15; i++) v.Move(level, new Vector2(-1f, 0f), false, false, 1f / 60f);
                float turned = MathF.Abs(MathX.WrapAngle(v.Yaw - before));
                if (v.Immobile) continue;   // a deployed Leviathan is meant to be rooted
                if (turned < 0.05f)
                    failures.Add($"{VehicleDef.Get(kind).Name} 轉向無效（{turned:F3} rad）");
                // The delta the crew's view rides on has to be reported, or the camera cannot
                // follow: this is the exact link that was missing.
                if (MathF.Abs(v.YawDelta) < 1e-5f)
                    failures.Add($"{VehicleDef.Get(kind).Name} 未回報 YawDelta");
                if (MathF.Sign(v.YawDelta) != MathF.Sign(MathX.WrapAngle(v.Yaw - before)))
                    failures.Add($"{VehicleDef.Get(kind).Name} 的 YawDelta 方向與實際轉向相反");
            }
        }

        // ---------------------------------------------------------------- the view rides the hull
        // This is the relationship the camera fix is built on, written out so it cannot be
        // dropped again: a rider holding a fixed offset from the hull keeps that offset while the
        // hull turns. Get it wrong and the vehicle rotates under a stationary camera, which is
        // exactly what "I can't steer the Hellbender" looked like.
        using (Level level = Maps.Build(null, MapId.Torlan))
        {
            var v = new Vehicle();
            v.Configure(VehicleKind.Hellbender, new Vector3(0f, 40f, 0f), 0f);
            v.Reset();
            const float lookOffset = 0.4f;          // rider glancing to one side
            float viewYaw = v.Yaw + lookOffset;
            for (int i = 0; i < 40; i++)
            {
                v.Move(level, new Vector2(-1f, 0.6f), false, false, 1f / 60f);
                viewYaw = MathX.WrapAngle(viewYaw + v.YawDelta);
            }
            float drift = MathF.Abs(MathX.WrapAngle(viewYaw - v.Yaw - lookOffset));
            Check(drift < 1e-3f, $"轉向時視角未跟著車體（偏移 {drift:F4} rad）", failures);
            Check(MathF.Abs(MathX.WrapAngle(v.Yaw)) > 0.2f,
                "測試本身無效：車體根本沒轉", failures);
        }

        // ---------------------------------------------------------------- seats
        var hellbender = new Vehicle();
        hellbender.Configure(VehicleKind.Hellbender, Vector3.Zero, 0f);
        hellbender.Reset();
        Check(hellbender.Occupants.Length >= 3, "Hellbender 應有三個座位", failures);
        Check(hellbender.FreeSeat() == 0, "空車第一個上車的人應該是駕駛", failures);

        hellbender.Occupants[0] = 7;
        Check(hellbender.FreeSeat() == 1, "駕駛座有人時應改坐下一個空位", failures);
        Check(hellbender.NextFreeSeatAfter(0) == 1, "駕駛的下一個空位是 1 號", failures);
        hellbender.Occupants[1] = 8;
        Check(hellbender.NextFreeSeatAfter(0) == 2, "1 號有人時應跳到 2 號", failures);
        Check(hellbender.NextFreeSeatAfter(2) == -1,
            "只剩自己的座位可坐時不應回報任何空位", failures);
        hellbender.Occupants[1] = -1;
        Check(hellbender.NextFreeSeatAfter(2) == 1, "換座位應能繞回前面的空位", failures);

        var manta = new Vehicle();
        manta.Configure(VehicleKind.Manta, Vector3.Zero, 0f);
        manta.Reset();
        Check(manta.Occupants.Length == 1, "曼塔只有一個座位", failures);
        Check(manta.NextFreeSeatAfter(0) == -1, "單座載具沒有可換的座位", failures);

        // ---------------------------------------------------------------- armed seats can shoot
        foreach (VehicleKind kind in Enum.GetValues<VehicleKind>())
        {
            if (kind == VehicleKind.Count) continue;
            var def = VehicleDef.Get(kind);
            for (int s = 0; s < def.Seats.Length; s++)
            {
                var seat = def.Seats[s];
                if (!seat.Armed) continue;
                // A zero interval never clears its cooldown check, so the seat would look armed
                // on the HUD and refuse to shoot.
                if (seat.Primary.Interval <= 0f)
                    failures.Add($"{def.Name} 的{seat.Role}座標示為武裝卻沒有可用的主要射擊");
                // GameWorld.HandleVehicleFire dispatches exactly these three. A seat authored with
                // any other mode compiles, shows an armed reticle, and silently does nothing when
                // the player pulls the trigger — so the roster is checked against the dispatcher
                // rather than trusted.
                foreach (var (fire, which) in new[] { (seat.Primary, "主要"), (seat.Alt, "次要") })
                {
                    if (fire.Interval <= 0f) continue;   // that mode is simply not offered
                    if (fire.Mode is FireMode.Hitscan or FireMode.Projectile or FireMode.Melee) continue;
                    failures.Add($"{def.Name} 的{seat.Role}座{which}射擊使用了載具開火不支援的 {fire.Mode}");
                }
            }
        }

        // ---------------------------------------------------------------- reachable placements
        // A vehicle you cannot walk up to is scenery. Boarding needs the pawn within
        // GameWorld.VehicleToBoard's reach of the hull, so every spawn has to sit close enough
        // above a surface somebody can actually stand on. Torlan parked its Raptors and Cicadas
        // twelve to thirty-four metres up with nothing underneath them.
        const float boardReach = 3.6f;
        for (var id = MapId.Morbias; id < MapId.Count; id++)
        {
            using Level level = Maps.Build(null, id);
            foreach (var spawn in level.VehicleSpawns)
            {
                var def = VehicleDef.Get(spawn.Kind);
                float? surface = LevelBuilder.SurfaceUnderVehicle(level.Collision,
                    spawn.Position, def.HalfExtents.Y);
                if (surface is not { } ground)
                {
                    failures.Add($"{Maps.Name(id)} 的{def.Name}下方沒有任何地面");
                    continue;
                }
                // A pawn standing on that surface, measured the way boarding measures it.
                float gap = spawn.Position.Y - ground - def.HalfExtents.Y;
                if (gap > boardReach)
                    failures.Add($"{Maps.Name(id)} 的{def.Name}離地 {gap:F1} m，"
                        + $"超過可上車的 {boardReach:F1} m");
                else if (gap < -0.05f)
                    failures.Add($"{Maps.Name(id)} 的{def.Name}埋在地面下 {-gap:F1} m");
            }
        }

        // ---------------------------------------------------------------- water that exists
        // A water volume authored inside a solid is invisible and does nothing: you cannot see it,
        // swim in it, or be slowed by it, and anything the map parks "in the river" spawns inside
        // rock. WAR-Torlan laid its arena floor as one slab up to ground level and then authored
        // the riverbed, the water and both banks within it, so the delta the map is named for was
        // flat ground. Sampling the top face catches the whole class.
        for (var id = MapId.Morbias; id < MapId.Count; id++)
        {
            using Level level = Maps.Build(null, id);
            var scratch = new List<int>(8);
            int index = 0;
            foreach (var brush in level.Collision.Brushes)
            {
                index++;
                if (brush.Kind != BrushKind.Water) continue;
                // Just above the surface, across the middle of the volume: if that is inside rock
                // then the water has no exposed face there at all.
                int buried = 0, samples = 0;
                for (int sx = 1; sx <= 3; sx++)
                    for (int sz = 1; sz <= 3; sz++)
                    {
                        Vector3 p = new(
                            MathX.Lerp(brush.Min.X, brush.Max.X, sx / 4f),
                            brush.Max.Y + 0.12f,
                            MathX.Lerp(brush.Min.Z, brush.Max.Z, sz / 4f));
                        samples++;
                        var probe = new Vector3(0.08f, 0.08f, 0.08f);
                        if (level.Collision.BoxOverlapsSolid(p - probe, p + probe, scratch)) buried++;
                    }
                if (buried == samples)
                    failures.Add($"{Maps.Name(id)} 的水體完全埋在實心地形裡"
                        + $"（y={brush.Max.Y:F1}，{samples} 個取樣點全被擋住）");
            }
        }

        // ---------------------------------------------------------------- interior density
        // The interior is the model a driver looks at for the whole match, so it has to be built
        // to the same standard as the hull seen from outside rather than a few blocked-out
        // crates. The floor is read from the roster itself: the leanest exterior in the game.
        int leanestHull = int.MaxValue;
        foreach (VehicleKind kind in Enum.GetValues<VehicleKind>())
        {
            if (kind == VehicleKind.Count) continue;
            int hull = VehicleModels.TriangleCountFor(kind);
            Console.WriteLine($"HULL_DENSITY {VehicleDef.Get(kind).Name,-12} {hull,6} tris");
            // The hoverboard is a plank a metre long and is not a comparison for anything else.
            if (kind == VehicleKind.Hoverboard) continue;
            // The Necris craft are smooth shells with no wheels or turrets to carry detail, so
            // they landed at a fraction of the Axon vehicles' density and read as blanks parked
            // beside them. They get their own vocabulary of ribs, vents and claws instead.
            if (hull < MinimumHullTriangles)
                failures.Add($"{VehicleDef.Get(kind).Name}的車體只有 {hull} 個三角形，"
                    + $"低於下限 {MinimumHullTriangles}");
            leanestHull = Math.Min(leanestHull, hull);
        }
        foreach (CockpitKind kind in Enum.GetValues<CockpitKind>())
        {
            if (kind == CockpitKind.Count) continue;
            int tris = CockpitModels.TriangleCountFor(kind);
            Console.WriteLine($"COCKPIT_DENSITY {kind,-6} {tris,6} tris");
            if (tris < MinimumCockpitTriangles)
                failures.Add($"{kind} 座艙只有 {tris} 個三角形，低於下限 {MinimumCockpitTriangles}");
        }
        Console.WriteLine($"COCKPIT_DENSITY 最精簡的車體外觀 {leanestHull} tris");

        foreach (string f in failures) Console.WriteLine($"VEHICLE_CONTROL FAIL {f}");
        Console.WriteLine(failures.Count == 0
            ? "VEHICLE_CONTROL PASS"
            : $"VEHICLE_CONTROL FAIL failures={failures.Count}");
        return failures.Count == 0 ? 0 : 1;
    }

    private static void Check(bool condition, string what, List<string> failures)
    {
        if (!condition) failures.Add(what);
    }
}
