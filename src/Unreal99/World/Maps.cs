using System.Numerics;
using Silk.NET.OpenGL;
using Unreal99.Core;
using Unreal99.Game;
using Unreal99.Rendering;
using Unreal99.UI;

namespace Unreal99.World;

public enum MapId { AbyssDeck = 0, RustTower, LavaTemple, OrbitalArena, TwinForts, Count }

/// <summary>
/// The arenas. Every one is generated from code — no editor files, no imported assets —
/// using <see cref="LevelBuilder"/> to emit render geometry and collision brushes together.
/// </summary>
public static class Maps
{
    public static string Name(MapId id) => id switch
    {
        MapId.AbyssDeck => Loc.MapDeck,
        MapId.RustTower => Loc.MapTower,
        MapId.LavaTemple => Loc.MapTemple,
        MapId.OrbitalArena => Loc.MapArena,
        MapId.TwinForts => Loc.MapTwinForts,
        _ => Loc.MapDeck,
    };

    public static string Description(MapId id) => id switch
    {
        MapId.AbyssDeck => Loc.MapDeckDesc,
        MapId.RustTower => Loc.MapTowerDesc,
        MapId.LavaTemple => Loc.MapTempleDesc,
        MapId.OrbitalArena => Loc.MapArenaDesc,
        MapId.TwinForts => Loc.MapTwinFortsDesc,
        _ => Loc.MapDeckDesc,
    };

    public static bool SupportsCtf(MapId id) => id == MapId.TwinForts;

    public static Level Build(GL gl, MapId id) => id switch
    {
        MapId.AbyssDeck => BuildAbyssDeck(gl),
        MapId.RustTower => BuildRustTower(gl),
        MapId.LavaTemple => BuildLavaTemple(gl),
        MapId.OrbitalArena => BuildOrbitalArena(gl),
        MapId.TwinForts => BuildTwinForts(gl),
        _ => BuildAbyssDeck(gl),
    };

    // ================================================================ DM-深淵甲板

    private static Level BuildAbyssDeck(GL gl)
    {
        var b = new LevelBuilder(Loc.MapDeck, Loc.MapDeckDesc);
        var env = b.Level.Environment;
        env.SunDirection = Vector3.Normalize(new Vector3(-0.30f, -0.86f, -0.42f));
        env.SunColor = new Vector3(3.2f, 2.8f, 2.3f);
        // The arena is roofless but walled, so bounced sky light carries most of the exposure.
        env.AmbientSky = new Vector3(0.22f, 0.25f, 0.34f);
        env.AmbientGround = new Vector3(0.11f, 0.07f, 0.05f);
        env.EnvIntensity = 0.45f;
        env.SkyTop = new Vector3(0.02f, 0.045f, 0.11f);
        env.SkyHorizon = new Vector3(0.30f, 0.16f, 0.10f);
        env.SkyGround = new Vector3(0.05f, 0.025f, 0.02f);
        env.StarStrength = 1.1f;
        env.CloudStrength = 0.75f;
        env.FogColor = new Vector3(0.11f, 0.085f, 0.09f);
        env.FogSunColor = new Vector3(0.85f, 0.42f, 0.20f);
        env.FogDensity = 0.020f;

        const float H = 34f;       // half-size of the arena
        const float WallTop = 22f;
        const float Wall = 1.6f;

        // --- ground deck with a lava pit punched through the middle ---
        // Four floor slabs surround the pit so the pit itself stays open.
        b.Solid(new Vector3(-H, -1.2f, -H), new Vector3(H, 0f, -10f), MatId.TechFloor);
        b.Solid(new Vector3(-H, -1.2f, 10f), new Vector3(H, 0f, H), MatId.TechFloor);
        b.Solid(new Vector3(-H, -1.2f, -10f), new Vector3(-10f, 0f, 10f), MatId.TechFloor);
        b.Solid(new Vector3(10f, -1.2f, -10f), new Vector3(H, 0f, 10f), MatId.TechFloor);
        b.Lava(new Vector3(-10f, -5.5f, -10f), new Vector3(10f, -3.4f, 10f));
        // Pit walls
        b.Solid(new Vector3(-10.6f, -5.6f, -10.6f), new Vector3(-10f, 0.1f, 10.6f), MatId.RustMetal);
        b.Solid(new Vector3(10f, -5.6f, -10.6f), new Vector3(10.6f, 0.1f, 10.6f), MatId.RustMetal);
        b.Solid(new Vector3(-10.6f, -5.6f, -10.6f), new Vector3(10.6f, 0.1f, -10f), MatId.RustMetal);
        b.Solid(new Vector3(-10.6f, -5.6f, 10f), new Vector3(10.6f, 0.1f, 10.6f), MatId.RustMetal);

        // --- outer walls ---
        b.Solid(new Vector3(-H - Wall, -6f, -H - Wall), new Vector3(-H, WallTop, H + Wall), MatId.TechWall);
        b.Solid(new Vector3(H, -6f, -H - Wall), new Vector3(H + Wall, WallTop, H + Wall), MatId.TechWall);
        b.Solid(new Vector3(-H, -6f, -H - Wall), new Vector3(H, WallTop, -H), MatId.TechWall);
        b.Solid(new Vector3(-H, -6f, H), new Vector3(H, WallTop, H + Wall), MatId.TechWall);

        // --- upper ring walkway at Y = 7 ---
        const float RingY = 7f;
        const float RingW = 5.5f;
        b.Solid(new Vector3(-H, RingY - 0.4f, -H), new Vector3(H, RingY, -H + RingW), MatId.MetalGrate);
        b.Solid(new Vector3(-H, RingY - 0.4f, H - RingW), new Vector3(H, RingY, H), MatId.MetalGrate);
        b.Solid(new Vector3(-H, RingY - 0.4f, -H + RingW), new Vector3(-H + RingW, RingY, H - RingW), MatId.MetalGrate);
        b.Solid(new Vector3(H - RingW, RingY - 0.4f, -H + RingW), new Vector3(H, RingY, H - RingW), MatId.MetalGrate);

        // Railings along the inner edge of the ring.
        RailRun(b, new Vector3(-H + RingW, RingY, -H + RingW), new Vector3(H - RingW, RingY, -H + RingW));
        RailRun(b, new Vector3(-H + RingW, RingY, H - RingW), new Vector3(H - RingW, RingY, H - RingW));
        RailRun(b, new Vector3(-H + RingW, RingY, -H + RingW), new Vector3(-H + RingW, RingY, H - RingW));
        RailRun(b, new Vector3(H - RingW, RingY, -H + RingW), new Vector3(H - RingW, RingY, H - RingW));

        // --- ramps up to the ring at two opposite corners ---
        b.Ramp(new Vector3(-H + RingW, 0f, -26f), new Vector3(-H + RingW + 12f, RingY, -20f), 1, MatId.TechFloor);
        b.Ramp(new Vector3(H - RingW - 12f, 0f, 20f), new Vector3(H - RingW, RingY, 26f), 0, MatId.TechFloor);

        // --- stairs on the other two corners ---
        b.Stairs(new Vector3(-24f, 0f, H - RingW), new Vector3(-24f, RingY, H - RingW - 11f), 5.5f, 12,
            MatId.TechFloor, alongX: false);
        b.Stairs(new Vector3(24f, 0f, -H + RingW), new Vector3(24f, RingY, -H + RingW + 11f), 5.5f, 12,
            MatId.TechFloor, alongX: false);

        // --- central island above the lava, reached by jump pads ---
        const float IslandY = 11.5f;
        b.Solid(new Vector3(-5f, IslandY - 0.6f, -5f), new Vector3(5f, IslandY, 5f), MatId.TechPanelDark);
        b.Decor(new Vector3(-5.4f, IslandY, -5.4f), new Vector3(5.4f, IslandY + 0.22f, -4.8f), MatId.Trim);
        b.Decor(new Vector3(-5.4f, IslandY, 4.8f), new Vector3(5.4f, IslandY + 0.22f, 5.4f), MatId.Trim);
        b.Decor(new Vector3(-5.4f, IslandY, -5.4f), new Vector3(-4.8f, IslandY + 0.22f, 5.4f), MatId.Trim);
        b.Decor(new Vector3(4.8f, IslandY, -5.4f), new Vector3(5.4f, IslandY + 0.22f, 5.4f), MatId.Trim);
        // Support column dropping into the lava — reads as structure, not a floating slab.
        b.Prism(new Vector3(0, 3f, 0), 1.6f, 17f, 8, MatId.RustMetal, collide: true);

        b.AddJumpPad(new Vector3(-16f, 0.1f, 0f), new Vector3(-3.5f, IslandY + 0.4f, 0f), new Vector3(0.3f, 0.85f, 1f));
        b.AddJumpPad(new Vector3(16f, 0.1f, 0f), new Vector3(3.5f, IslandY + 0.4f, 0f), new Vector3(0.3f, 0.85f, 1f));
        b.AddJumpPad(new Vector3(0f, 0.1f, -16f), new Vector3(0f, IslandY + 0.4f, -3.5f), new Vector3(0.3f, 0.85f, 1f));
        b.AddJumpPad(new Vector3(0f, 0.1f, 16f), new Vector3(0f, IslandY + 0.4f, 3.5f), new Vector3(0.3f, 0.85f, 1f));

        // --- catwalks crossing the pit at ring height ---
        b.Solid(new Vector3(-H + RingW, RingY - 0.35f, -2.2f), new Vector3(-5.2f, RingY, 2.2f), MatId.MetalGrate);
        b.Solid(new Vector3(5.2f, RingY - 0.35f, -2.2f), new Vector3(H - RingW, RingY, 2.2f), MatId.MetalGrate);
        b.Solid(new Vector3(-2.2f, RingY - 0.35f, -H + RingW), new Vector3(2.2f, RingY, -5.2f), MatId.MetalGrate);
        b.Solid(new Vector3(-2.2f, RingY - 0.35f, 5.2f), new Vector3(2.2f, RingY, H - RingW), MatId.MetalGrate);

        // --- sniper balconies at Y = 14, corner-mounted ---
        foreach (var (sx, sz) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
        {
            Vector3 c = new(sx * (H - 7f), 14f, sz * (H - 7f));
            b.Solid(c - new Vector3(5f, 0.4f, 5f), c + new Vector3(5f, 0f, 5f), MatId.TechPanelDark);
            b.Decor(c - new Vector3(5.3f, 0f, 5.3f), c + new Vector3(5.3f, 1.0f, -4.6f), MatId.Trim);
            b.Lift(
                new Vector3(sx * (H - 4.6f) - 1.6f, RingY, sz * (H - 4.6f) - 1.6f),
                new Vector3(sx * (H - 4.6f) + 1.6f, RingY + 0.4f, sz * (H - 4.6f) + 1.6f),
                new Vector3(0, 14f - RingY, 0), MatId.TechPanelDark, period: 8f, phase: (sx + sz) * 0.17f);
            b.CeilingLamp(c + new Vector3(0, 6f, 0), new Vector3(0.6f, 0.8f, 1f), 16f, 5f);
        }

        // --- structural detail: pipes, vents, wall panels ---
        for (int i = 0; i < 10; i++)
        {
            float t = -H + 4f + i * (2f * H - 8f) / 9f;
            b.Decor(new Vector3(t - 0.4f, 8f, -H + 0.1f), new Vector3(t + 0.4f, WallTop - 2f, -H + 0.7f), MatId.RustMetal, 1.5f);
            b.Decor(new Vector3(t - 0.4f, 8f, H - 0.7f), new Vector3(t + 0.4f, WallTop - 2f, H - 0.1f), MatId.RustMetal, 1.5f);
            b.Decor(new Vector3(-H + 0.1f, 8f, t - 0.4f), new Vector3(-H + 0.7f, WallTop - 2f, t + 0.4f), MatId.RustMetal, 1.5f);
            b.Decor(new Vector3(H - 0.7f, 8f, t - 0.4f), new Vector3(H - 0.1f, WallTop - 2f, t + 0.4f), MatId.RustMetal, 1.5f);
        }
        for (int i = 0; i < 6; i++)
        {
            float t = -H + 8f + i * (2f * H - 16f) / 5f;
            b.Decor(new Vector3(t - 1.6f, 2.2f, -H + 0.1f), new Vector3(t + 1.6f, 4.6f, -H + 0.5f), MatId.EnergyPanel, 0.7f);
            b.AddLight(new Vector3(t, 3.4f, -H + 1.4f), new Vector3(0.3f, 0.75f, 1f), 9f, 2.0f);
            b.Decor(new Vector3(t - 1.6f, 2.2f, H - 0.5f), new Vector3(t + 1.6f, 4.6f, H - 0.1f), MatId.EnergyPanel, 0.7f);
            b.AddLight(new Vector3(t, 3.4f, H - 1.4f), new Vector3(0.3f, 0.75f, 1f), 9f, 2.0f);
        }

        // --- overhead lighting: floodlights slung below the wall tops so they actually reach the deck ---
        for (int x = -1; x <= 1; x++)
            for (int z = -1; z <= 1; z++)
            {
                if (x == 0 && z == 0) continue;
                b.CeilingLamp(new Vector3(x * 21f, 15.5f, z * 21f), new Vector3(0.88f, 0.93f, 1f), 32f, 8f, 1.7f);
            }
        // Lower fixtures along the ring keep the walkways and floor readable.
        for (int i = 0; i < 4; i++)
        {
            float a = i / 4f * MathX.TwoPi + MathX.Pi / 4f;
            Vector3 p = new(MathF.Cos(a) * 24f, 9.5f, MathF.Sin(a) * 24f);
            b.CeilingLamp(p, new Vector3(0.80f, 0.86f, 1f), 22f, 5f, 1.1f);
        }

        // --- weapons ---
        b.Weapon(new Vector3(0f, IslandY + 0.9f, 0f), WeaponKind.RocketLauncher);
        b.Weapon(new Vector3(-28f, 0.9f, -28f), WeaponKind.FlakCannon);
        b.Weapon(new Vector3(28f, 0.9f, 28f), WeaponKind.Minigun);
        b.Weapon(new Vector3(-28f, 0.9f, 28f), WeaponKind.PulseGun);
        b.Weapon(new Vector3(28f, 0.9f, -28f), WeaponKind.BioRifle);
        b.Weapon(new Vector3(0f, RingY + 0.9f, -H + 2.6f), WeaponKind.ShockRifle);
        b.Weapon(new Vector3(0f, RingY + 0.9f, H - 2.6f), WeaponKind.Ripper);
        b.Weapon(new Vector3(-(H - 7f), 14.9f, -(H - 7f)), WeaponKind.SniperRifle);
        b.Weapon(new Vector3(H - 7f, 14.9f, H - 7f), WeaponKind.SniperRifle);
        b.Weapon(new Vector3(-H + 2.6f, RingY + 0.9f, 0f), WeaponKind.Enforcer);
        b.Weapon(new Vector3(H - 2.6f, RingY + 0.9f, 0f), WeaponKind.Enforcer);

        // --- ammo ---
        b.Ammo(new Vector3(-24f, 0.7f, -28f), AmmoKind.FlakShells);
        b.Ammo(new Vector3(24f, 0.7f, 28f), AmmoKind.MinigunBullets);
        b.Ammo(new Vector3(-24f, 0.7f, 28f), AmmoKind.PulseCells);
        b.Ammo(new Vector3(24f, 0.7f, -28f), AmmoKind.BioSludge);
        b.Ammo(new Vector3(3f, IslandY + 0.7f, 3f), AmmoKind.Rockets);
        b.Ammo(new Vector3(-3f, IslandY + 0.7f, -3f), AmmoKind.Rockets);
        b.Ammo(new Vector3(4f, RingY + 0.7f, -H + 2.6f), AmmoKind.ShockCore);
        b.Ammo(new Vector3(-4f, RingY + 0.7f, H - 2.6f), AmmoKind.Blades);
        b.Ammo(new Vector3(-(H - 10f), 14.7f, -(H - 7f)), AmmoKind.SniperRounds);
        b.Ammo(new Vector3(H - 10f, 14.7f, H - 7f), AmmoKind.SniperRounds);

        // --- power-ups ---
        b.Item(new Vector3(0f, RingY + 0.9f, 0f), PickupKind.ShieldBelt);   // exposed catwalk junction
        b.Item(new Vector3(-(H - 7f), 14.9f, H - 7f), PickupKind.DamageAmp);
        b.Item(new Vector3(H - 7f, 14.9f, -(H - 7f)), PickupKind.Invisibility);
        b.Item(new Vector3(-14f, 0.8f, -14f), PickupKind.BodyArmor);
        b.Item(new Vector3(14f, 0.8f, 14f), PickupKind.BodyArmor);
        b.Item(new Vector3(14f, 0.8f, -14f), PickupKind.ThighPads);
        b.Item(new Vector3(-14f, 0.8f, 14f), PickupKind.ThighPads);
        b.Item(new Vector3(0f, 0.7f, -22f), PickupKind.HealthPack);
        b.Item(new Vector3(0f, 0.7f, 22f), PickupKind.HealthPack);
        b.Item(new Vector3(-22f, 0.7f, 0f), PickupKind.HealthPack);
        b.Item(new Vector3(22f, 0.7f, 0f), PickupKind.HealthPack);
        b.Item(new Vector3(0f, IslandY + 0.7f, -3.5f), PickupKind.SuperHealth);
        for (int i = 0; i < 8; i++)
        {
            float a = i / 8f * MathX.TwoPi;
            b.Item(new Vector3(MathF.Cos(a) * 19f, 0.6f, MathF.Sin(a) * 19f), PickupKind.HealthVial);
        }
        b.Item(new Vector3(-30f, RingY + 0.8f, -30f), PickupKind.JumpBoots);

        // --- spawns ---
        for (int i = 0; i < 8; i++)
        {
            float a = i / 8f * MathX.TwoPi + 0.4f;
            Vector3 p = new(MathF.Cos(a) * 27f, 0.2f, MathF.Sin(a) * 27f);
            b.Spawn(p, -a * MathX.Rad2Deg + 90f);
        }
        // Ring spawns face inward: yaw 0 looks along -Z, +90 looks along -X.
        b.Spawn(new Vector3(-H + 3f, RingY + 0.2f, 0f), -90f);
        b.Spawn(new Vector3(H - 3f, RingY + 0.2f, 0f), 90f);
        b.Spawn(new Vector3(0f, RingY + 0.2f, -H + 3f), 180f);
        b.Spawn(new Vector3(0f, RingY + 0.2f, H - 3f), 0f);

        return b.Build(gl);
    }

    // ================================================================ DM-鏽蝕高塔

    private static Level BuildRustTower(GL gl)
    {
        var b = new LevelBuilder(Loc.MapTower, Loc.MapTowerDesc);
        var env = b.Level.Environment;
        env.SunDirection = Vector3.Normalize(new Vector3(0.42f, -0.55f, 0.30f));
        env.SunColor = new Vector3(3.6f, 2.6f, 1.7f);
        // A deep shaft blocks nearly all direct sun, so the ambient term has to carry the arena.
        env.AmbientSky = new Vector3(0.34f, 0.30f, 0.34f);
        env.AmbientGround = new Vector3(0.14f, 0.10f, 0.08f);
        env.SkyTop = new Vector3(0.06f, 0.08f, 0.18f);
        env.SkyHorizon = new Vector3(0.55f, 0.28f, 0.14f);
        env.SkyGround = new Vector3(0.07f, 0.05f, 0.04f);
        env.StarStrength = 0.35f;
        env.CloudStrength = 0.95f;
        env.FogColor = new Vector3(0.16f, 0.12f, 0.11f);
        env.FogSunColor = new Vector3(1.0f, 0.55f, 0.24f);
        env.FogDensity = 0.026f;
        env.EnvIntensity = 0.7f;

        const float R = 21f;          // inner radius of the shaft
        const float TowerTop = 46f;

        // --- base floor and shaft wall, rasterised into axis-aligned slabs ---
        b.Annulus(Vector3.Zero, -1.4f, 0f, 0f, R + 0.6f, MatId.Concrete, 22);
        b.Annulus(Vector3.Zero, -2f, TowerTop, R, R + 3.4f, MatId.RustMetal, 22, true, 0.8f);

        // --- spiral of landings climbing the shaft ---
        const int Levels = 7;
        var landings = new Vector3[Levels];
        for (int i = 0; i < Levels; i++)
        {
            float y = 6f + i * 5.6f;
            float a = i * 2.1f;
            Vector3 dir = new(MathF.Cos(a), 0, MathF.Sin(a));
            Vector3 center = dir * (R - 6.5f) + new Vector3(0, y, 0);
            landings[i] = center;

            Vector3 half = new(6.2f, 0.35f, 6.2f);
            b.Solid(center - half, center + new Vector3(half.X, 0f, half.Z), MatId.MetalGrate, true, 1.1f);

            // Guard rail around the outer arc of each landing.
            RailRun(b, center + new Vector3(-6.2f, 0, -6.2f), center + new Vector3(6.2f, 0, -6.2f));
            RailRun(b, center + new Vector3(-6.2f, 0, 6.2f), center + new Vector3(6.2f, 0, 6.2f));

            b.AddLight(center + new Vector3(0, 3.2f, 0), new Vector3(1.0f, 0.82f, 0.58f), 22f, 7f, 3f, 0.10f);
            b.Decor(center + new Vector3(-6.6f, -0.5f, -6.6f), center + new Vector3(-6.0f, 2.4f, -6.0f), MatId.Trim);
            b.Decor(center + new Vector3(6.0f, -0.5f, 6.0f), center + new Vector3(6.6f, 2.4f, 6.6f), MatId.Trim);

            // Connect to the previous landing with a ramp when they are close enough.
            if (i > 0)
            {
                Vector3 prev = landings[i - 1];
                Vector3 mid = (prev + center) * 0.5f;
                Vector3 d = center - prev;
                if (MathF.Abs(d.X) > MathF.Abs(d.Z))
                {
                    Vector3 min = new(MathF.Min(prev.X, center.X), prev.Y - 0.35f, mid.Z - 2.6f);
                    Vector3 max = new(MathF.Max(prev.X, center.X), center.Y, mid.Z + 2.6f);
                    b.Ramp(min, max, d.X > 0 ? 0 : 1, MatId.TechFloor);
                }
                else
                {
                    Vector3 min = new(mid.X - 2.6f, prev.Y - 0.35f, MathF.Min(prev.Z, center.Z));
                    Vector3 max = new(mid.X + 2.6f, center.Y, MathF.Max(prev.Z, center.Z));
                    b.Ramp(min, max, d.Z > 0 ? 2 : 3, MatId.TechFloor);
                }
            }
        }

        // --- central column with lift ---
        b.Prism(new Vector3(0, TowerTop * 0.5f, 0), 3.2f, TowerTop, 8, MatId.TechPanelDark);
        b.Lift(new Vector3(-2.4f, 0.2f, 3.4f), new Vector3(2.4f, 0.6f, 6.2f),
            new Vector3(0, 23f, 0), MatId.TechPanelDark, period: 11f, dwell: 0.22f);
        b.Lift(new Vector3(-2.4f, 23.2f, -6.2f), new Vector3(2.4f, 23.6f, -3.4f),
            new Vector3(0, 17f, 0), MatId.TechPanelDark, period: 10f, phase: 0.4f, dwell: 0.22f);

        // --- jump pads at the base flinging you to the mid landings ---
        b.AddJumpPad(new Vector3(-13f, 0.1f, -13f), landings[2] + new Vector3(0, 1.5f, 0), new Vector3(1f, 0.55f, 0.15f));
        b.AddJumpPad(new Vector3(13f, 0.1f, 13f), landings[3] + new Vector3(0, 1.5f, 0), new Vector3(1f, 0.55f, 0.15f));
        b.AddJumpPad(new Vector3(13f, 0.1f, -13f), landings[1] + new Vector3(0, 1.5f, 0), new Vector3(1f, 0.55f, 0.15f));

        // --- crown platform ---
        Vector3 crown = new(0, TowerTop - 2f, 0);
        b.Solid(crown - new Vector3(7.5f, 0.5f, 7.5f), crown + new Vector3(7.5f, 0f, 7.5f), MatId.Trim, true, 0.7f);
        b.Torus(crown + new Vector3(0, 1.4f, 0), 6.2f, 0.28f, MatId.EnergyPanel, 24, 8);
        b.AddLight(crown + new Vector3(0, 3f, 0), new Vector3(0.4f, 0.8f, 1f), 22f, 7f);
        // Ramp from the top landing to the crown.
        {
            Vector3 last = landings[Levels - 1];
            Vector3 mid = (last + crown) * 0.5f;
            Vector3 d = crown - last;
            if (MathF.Abs(d.X) > MathF.Abs(d.Z))
                b.Ramp(new Vector3(MathF.Min(last.X, crown.X), last.Y - 0.35f, mid.Z - 2.8f),
                       new Vector3(MathF.Max(last.X, crown.X), crown.Y, mid.Z + 2.8f), d.X > 0 ? 0 : 1, MatId.TechFloor);
            else
                b.Ramp(new Vector3(mid.X - 2.8f, last.Y - 0.35f, MathF.Min(last.Z, crown.Z)),
                       new Vector3(mid.X + 2.8f, crown.Y, MathF.Max(last.Z, crown.Z)), d.Z > 0 ? 2 : 3, MatId.TechFloor);
        }

        // --- lava at the foot of the tower to punish falls ---
        b.Lava(new Vector3(-7f, -1.5f, -7f), new Vector3(7f, -0.55f, 7f));

        // --- weapons and items climb in value with height ---
        b.Weapon(new Vector3(-16f, 0.9f, 0f), WeaponKind.Enforcer);
        b.Weapon(new Vector3(16f, 0.9f, 0f), WeaponKind.BioRifle);
        b.Weapon(new Vector3(0f, 0.9f, -16f), WeaponKind.PulseGun);
        b.Weapon(landings[0] + new Vector3(0, 0.9f, 0), WeaponKind.Ripper);
        b.Weapon(landings[1] + new Vector3(0, 0.9f, 0), WeaponKind.ShockRifle);
        b.Weapon(landings[2] + new Vector3(0, 0.9f, 0), WeaponKind.FlakCannon);
        b.Weapon(landings[3] + new Vector3(0, 0.9f, 0), WeaponKind.Minigun);
        b.Weapon(landings[4] + new Vector3(0, 0.9f, 0), WeaponKind.SniperRifle);
        b.Weapon(landings[5] + new Vector3(0, 0.9f, 0), WeaponKind.RocketLauncher);
        b.Weapon(crown + new Vector3(0, 0.9f, 0), WeaponKind.Redeemer, 95f);

        b.Item(landings[6] + new Vector3(0, 0.8f, 0), PickupKind.ShieldBelt);
        b.Item(landings[4] + new Vector3(2.5f, 0.8f, 0), PickupKind.DamageAmp);
        b.Item(landings[2] + new Vector3(-2.5f, 0.8f, 0), PickupKind.BodyArmor);
        b.Item(new Vector3(-16f, 0.7f, 12f), PickupKind.JumpBoots);
        b.Item(new Vector3(16f, 0.7f, -12f), PickupKind.ThighPads);
        for (int i = 0; i < Levels; i++)
        {
            b.Item(landings[i] + new Vector3(-3f, 0.7f, 3f), i % 2 == 0 ? PickupKind.HealthPack : PickupKind.HealthVial);
            b.Ammo(landings[i] + new Vector3(3f, 0.7f, -3f), (AmmoKind)(i % (int)AmmoKind.Count));
        }
        for (int i = 0; i < 6; i++)
        {
            float a = i / 6f * MathX.TwoPi;
            b.Item(new Vector3(MathF.Cos(a) * 17f, 0.6f, MathF.Sin(a) * 17f), PickupKind.HealthVial);
            // yaw = 90° - a points from the ring back toward the centre.
            b.Spawn(new Vector3(MathF.Cos(a) * 15f, 0.2f, MathF.Sin(a) * 15f), 90f - a * MathX.Rad2Deg);
        }
        for (int i = 0; i < Levels; i++)
            b.Spawn(landings[i] + new Vector3(0, 0.2f, 3.5f), i * 47f);

        return b.Build(gl);
    }

    // ================================================================ DM-熔岩神殿

    private static Level BuildLavaTemple(GL gl)
    {
        var b = new LevelBuilder(Loc.MapTemple, Loc.MapTempleDesc);
        var env = b.Level.Environment;
        env.SunDirection = Vector3.Normalize(new Vector3(-0.18f, -0.94f, -0.28f));
        env.SunColor = new Vector3(2.6f, 2.35f, 2.1f);
        env.AmbientSky = new Vector3(0.22f, 0.21f, 0.27f);
        env.AmbientGround = new Vector3(0.10f, 0.055f, 0.03f);
        env.SkyTop = new Vector3(0.015f, 0.02f, 0.05f);
        env.SkyHorizon = new Vector3(0.22f, 0.09f, 0.05f);
        env.SkyGround = new Vector3(0.05f, 0.02f, 0.012f);
        env.StarStrength = 1.4f;
        env.CloudStrength = 0.30f;
        env.FogColor = new Vector3(0.13f, 0.07f, 0.05f);
        env.FogSunColor = new Vector3(1.0f, 0.42f, 0.14f);
        env.FogDensity = 0.030f;
        env.EnvIntensity = 0.32f;

        const float HX = 38f, HZ = 30f;
        const float CeilY = 19f;

        b.Solid(new Vector3(-HX, -1.5f, -HZ), new Vector3(HX, 0f, HZ), MatId.Rock);
        b.Solid(new Vector3(-HX - 2f, -2f, -HZ - 2f), new Vector3(-HX, CeilY, HZ + 2f), MatId.Rock, true, 0.7f);
        b.Solid(new Vector3(HX, -2f, -HZ - 2f), new Vector3(HX + 2f, CeilY, HZ + 2f), MatId.Rock, true, 0.7f);
        b.Solid(new Vector3(-HX, -2f, -HZ - 2f), new Vector3(HX, CeilY, -HZ), MatId.Rock, true, 0.7f);
        b.Solid(new Vector3(-HX, -2f, HZ), new Vector3(HX, CeilY, HZ + 2f), MatId.Rock, true, 0.7f);
        // Ceiling with a wide oculus so the sky and a shaft of light come through.
        b.Solid(new Vector3(-HX, CeilY, -HZ), new Vector3(-9f, CeilY + 2f, HZ), MatId.Concrete, true, 0.6f);
        b.Solid(new Vector3(9f, CeilY, -HZ), new Vector3(HX, CeilY + 2f, HZ), MatId.Concrete, true, 0.6f);
        b.Solid(new Vector3(-9f, CeilY, -HZ), new Vector3(9f, CeilY + 2f, -9f), MatId.Concrete, true, 0.6f);
        b.Solid(new Vector3(-9f, CeilY, 9f), new Vector3(9f, CeilY + 2f, HZ), MatId.Concrete, true, 0.6f);

        // --- lava channels forming a cross ---
        b.Lava(new Vector3(-HX + 2f, -1.6f, -3.2f), new Vector3(-14f, -0.35f, 3.2f));
        b.Lava(new Vector3(14f, -1.6f, -3.2f), new Vector3(HX - 2f, -0.35f, 3.2f));
        b.Lava(new Vector3(-3.2f, -1.6f, -HZ + 2f), new Vector3(3.2f, -0.35f, -13f));
        b.Lava(new Vector3(-3.2f, -1.6f, 13f), new Vector3(3.2f, -0.35f, HZ - 2f));

        // Bridges over the channels.
        b.Solid(new Vector3(-24f, -0.1f, -2.4f), new Vector3(-19f, 0.25f, 2.4f), MatId.Concrete);
        b.Solid(new Vector3(19f, -0.1f, -2.4f), new Vector3(24f, 0.25f, 2.4f), MatId.Concrete);
        b.Solid(new Vector3(-2.4f, -0.1f, -21f), new Vector3(2.4f, 0.25f, -16f), MatId.Concrete);
        b.Solid(new Vector3(-2.4f, -0.1f, 16f), new Vector3(2.4f, 0.25f, 21f), MatId.Concrete);

        // --- central altar ---
        b.Solid(new Vector3(-11f, 0f, -11f), new Vector3(11f, 1.6f, 11f), MatId.Concrete, true, 0.55f);
        b.Solid(new Vector3(-7.5f, 1.6f, -7.5f), new Vector3(7.5f, 3.0f, 7.5f), MatId.Concrete, true, 0.55f);
        b.Ramp(new Vector3(-13.6f, 0f, -3f), new Vector3(-11f, 1.6f, 3f), 0, MatId.Concrete);
        b.Ramp(new Vector3(11f, 0f, -3f), new Vector3(13.6f, 1.6f, 3f), 1, MatId.Concrete);
        b.Ramp(new Vector3(-3f, 0f, -13.6f), new Vector3(3f, 1.6f, -11f), 2, MatId.Concrete);
        b.Ramp(new Vector3(-3f, 0f, 11f), new Vector3(3f, 1.6f, 13.6f), 3, MatId.Concrete);
        b.Ramp(new Vector3(-10.4f, 1.6f, -2.4f), new Vector3(-7.5f, 3.0f, 2.4f), 0, MatId.Concrete);
        b.Ramp(new Vector3(7.5f, 1.6f, -2.4f), new Vector3(10.4f, 3.0f, 2.4f), 1, MatId.Concrete);

        b.Prism(new Vector3(0, 5.2f, 0), 1.5f, 4.4f, 6, MatId.Trim);
        b.Sphere(new Vector3(0, 8.2f, 0), 1.15f, MatId.EnergyPanel, 12, 18);
        b.AddLight(new Vector3(0, 8.2f, 0), new Vector3(0.45f, 0.85f, 1f), 22f, 8f, 2.2f, 0.08f);

        // --- columns around the hall ---
        for (int i = 0; i < 5; i++)
        {
            float x = -26f + i * 13f;
            foreach (float z in new[] { -19f, 19f })
            {
                b.Prism(new Vector3(x, CeilY * 0.5f, z), 1.7f, CeilY, 8, MatId.Concrete);
                b.Prism(new Vector3(x, 1.0f, z), 2.3f, 2.0f, 8, MatId.Rock);
                b.Prism(new Vector3(x, CeilY - 1.0f, z), 2.3f, 2.0f, 8, MatId.Rock);
                b.Decor(new Vector3(x - 0.6f, 3.4f, z - 0.6f), new Vector3(x + 0.6f, 4.4f, z + 0.6f), MatId.Lava, 0.9f);
                b.AddLight(new Vector3(x, 4.0f, z), new Vector3(1f, 0.46f, 0.14f), 10f, 2.4f, 5.5f, 0.28f);
            }
        }

        // --- upper galleries along the long walls ---
        const float GalY = 8.5f;
        b.Solid(new Vector3(-HX, GalY - 0.5f, -HZ), new Vector3(HX, GalY, -HZ + 6.5f), MatId.Concrete, true, 0.7f);
        b.Solid(new Vector3(-HX, GalY - 0.5f, HZ - 6.5f), new Vector3(HX, GalY, HZ), MatId.Concrete, true, 0.7f);
        RailRun(b, new Vector3(-HX, GalY, -HZ + 6.5f), new Vector3(HX, GalY, -HZ + 6.5f));
        RailRun(b, new Vector3(-HX, GalY, HZ - 6.5f), new Vector3(HX, GalY, HZ - 6.5f));
        b.Stairs(new Vector3(-HX + 3f, 0f, -HZ + 8f), new Vector3(-HX + 3f, GalY, -HZ + 2.5f), 5f, 14, MatId.Concrete, false);
        b.Stairs(new Vector3(HX - 3f, 0f, HZ - 8f), new Vector3(HX - 3f, GalY, HZ - 2.5f), 5f, 14, MatId.Concrete, false);
        b.AddJumpPad(new Vector3(-30f, 0.1f, 12f), new Vector3(-30f, GalY + 1.5f, HZ - 3.5f), new Vector3(1f, 0.4f, 0.1f));
        b.AddJumpPad(new Vector3(30f, 0.1f, -12f), new Vector3(30f, GalY + 1.5f, -HZ + 3.5f), new Vector3(1f, 0.4f, 0.1f));

        // --- teleporter pair linking the far corners ---
        b.AddTeleporter(new Vector3(-HX + 5f, 0.2f, HZ - 5f), new Vector3(HX - 5f, 0.4f, -HZ + 5f), -135f,
            new Vector3(0.6f, 0.3f, 1f));
        b.AddTeleporter(new Vector3(HX - 5f, 0.2f, -HZ + 5f), new Vector3(-HX + 5f, 0.4f, HZ - 5f), 45f,
            new Vector3(0.6f, 0.3f, 1f));

        // --- placements ---
        b.Weapon(new Vector3(0f, 3.9f, 0f), WeaponKind.RocketLauncher);
        b.Weapon(new Vector3(-30f, GalY + 0.9f, -HZ + 3.5f), WeaponKind.SniperRifle);
        b.Weapon(new Vector3(30f, GalY + 0.9f, HZ - 3.5f), WeaponKind.SniperRifle);
        b.Weapon(new Vector3(-20f, 0.9f, -24f), WeaponKind.FlakCannon);
        b.Weapon(new Vector3(20f, 0.9f, 24f), WeaponKind.Minigun);
        b.Weapon(new Vector3(20f, 0.9f, -24f), WeaponKind.ShockRifle);
        b.Weapon(new Vector3(-20f, 0.9f, 24f), WeaponKind.PulseGun);
        b.Weapon(new Vector3(-8f, 2.5f, -8f), WeaponKind.Ripper);
        b.Weapon(new Vector3(8f, 2.5f, 8f), WeaponKind.BioRifle);
        b.Weapon(new Vector3(-33f, 0.9f, 0f), WeaponKind.Enforcer);
        b.Weapon(new Vector3(33f, 0.9f, 0f), WeaponKind.Enforcer);

        b.Item(new Vector3(0f, 3.9f, -5f), PickupKind.ShieldBelt);
        b.Item(new Vector3(0f, GalY + 0.8f, -HZ + 3.5f), PickupKind.DamageAmp);
        b.Item(new Vector3(0f, GalY + 0.8f, HZ - 3.5f), PickupKind.Invisibility);
        b.Item(new Vector3(-26f, 0.8f, 0f), PickupKind.BodyArmor);
        b.Item(new Vector3(26f, 0.8f, 0f), PickupKind.BodyArmor);
        b.Item(new Vector3(0f, 1.9f, -9f), PickupKind.HealthPack);
        b.Item(new Vector3(0f, 1.9f, 9f), PickupKind.HealthPack);
        b.Item(new Vector3(-13f, 0.7f, -13f), PickupKind.ThighPads);
        b.Item(new Vector3(13f, 0.7f, 13f), PickupKind.ThighPads);
        b.Item(new Vector3(0f, 4.5f, 0f), PickupKind.SuperHealth);

        b.Ammo(new Vector3(-18f, 0.7f, -24f), AmmoKind.FlakShells);
        b.Ammo(new Vector3(18f, 0.7f, 24f), AmmoKind.MinigunBullets);
        b.Ammo(new Vector3(18f, 0.7f, -24f), AmmoKind.ShockCore);
        b.Ammo(new Vector3(-18f, 0.7f, 24f), AmmoKind.PulseCells);
        b.Ammo(new Vector3(2.5f, 3.9f, 2.5f), AmmoKind.Rockets);
        b.Ammo(new Vector3(-2.5f, 3.9f, -2.5f), AmmoKind.Rockets);
        b.Ammo(new Vector3(-28f, GalY + 0.7f, -HZ + 3.5f), AmmoKind.SniperRounds);
        b.Ammo(new Vector3(28f, GalY + 0.7f, HZ - 3.5f), AmmoKind.SniperRounds);

        for (int i = 0; i < 5; i++)
        {
            b.Item(new Vector3(-24f + i * 12f, 0.6f, -8f), PickupKind.HealthVial);
            b.Item(new Vector3(-24f + i * 12f, 0.6f, 8f), PickupKind.HealthVial);
        }

        b.Spawn(new Vector3(-32f, 0.2f, -24f), 45f);
        b.Spawn(new Vector3(32f, 0.2f, 24f), -135f);
        b.Spawn(new Vector3(32f, 0.2f, -24f), 135f);
        b.Spawn(new Vector3(-32f, 0.2f, 24f), -45f);
        b.Spawn(new Vector3(0f, 0.2f, -26f), 180f);
        b.Spawn(new Vector3(0f, 0.2f, 26f), 0f);
        b.Spawn(new Vector3(-34f, 0.2f, 0f), 90f);
        b.Spawn(new Vector3(34f, 0.2f, 0f), -90f);
        b.Spawn(new Vector3(-18f, GalY + 0.2f, -HZ + 3.5f), 0f);
        b.Spawn(new Vector3(18f, GalY + 0.2f, HZ - 3.5f), 180f);
        b.Spawn(new Vector3(0f, 3.2f, 5f), 0f);

        return b.Build(gl);
    }

    // ================================================================ DM-軌道競技場

    private static Level BuildOrbitalArena(GL gl)
    {
        var b = new LevelBuilder(Loc.MapArena, Loc.MapArenaDesc);
        var env = b.Level.Environment;
        env.SunDirection = Vector3.Normalize(new Vector3(0.25f, -0.66f, -0.71f));
        env.SunColor = new Vector3(3.0f, 3.1f, 3.4f);
        env.AmbientSky = new Vector3(0.36f, 0.40f, 0.52f);
        env.AmbientGround = new Vector3(0.10f, 0.11f, 0.14f);
        env.SkyTop = new Vector3(0.008f, 0.010f, 0.028f);
        env.SkyHorizon = new Vector3(0.06f, 0.10f, 0.26f);
        env.SkyGround = new Vector3(0.01f, 0.012f, 0.03f);
        env.StarStrength = 2.4f;
        env.CloudStrength = 0.0f;
        env.FogColor = new Vector3(0.05f, 0.06f, 0.10f);
        env.FogSunColor = new Vector3(0.5f, 0.6f, 0.9f);
        env.FogDensity = 0.010f;
        env.EnvIntensity = 0.85f;

        const float R = 30f;
        const int Sides = 16;
        const float WallTop = 17f;

        // --- circular floor and perimeter wall, rasterised into axis-aligned slabs ---
        b.Annulus(Vector3.Zero, -1.6f, 0f, 0f, R + 1.5f, MatId.TechFloor, 26);
        b.Annulus(Vector3.Zero, -2f, WallTop, R + 1.5f, R + 4.5f, MatId.SkyMetal, 26, true, 0.9f);
        b.Cylinder(new Vector3(0, 0.06f, 0), 9f, 9f, 0.10f, 32, MatId.EnergyPanel);
        b.AddLight(new Vector3(0, 1.2f, 0), new Vector3(0.35f, 0.75f, 1f), 22f, 3.5f);

        // --- wall lamps ---
        for (int i = 0; i < Sides; i += 2)
        {
            float a = i / (float)Sides * MathX.TwoPi;
            Vector3 dir = new(MathF.Cos(a), 0, MathF.Sin(a));
            Vector3 lampPos = dir * (R + 0.6f) + new Vector3(0, 8.5f, 0);
            b.Decor(lampPos - new Vector3(1.1f, 1.6f, 1.1f), lampPos + new Vector3(1.1f, 1.6f, 1.1f),
                MatId.EnergyPanel, 0.8f);
            b.AddLight(lampPos - dir * 2.0f, new Vector3(0.60f, 0.78f, 1f), 26f, 9f);
        }

        // --- tiered ledges: an inner ring at 4.5 and an outer catwalk at 9.5 ---
        const float Tier1 = 4.5f, Tier2 = 9.5f;
        b.Annulus(Vector3.Zero, Tier1 - 0.5f, Tier1, R - 12f, R - 5.5f, MatId.TechPanelDark, 26);
        b.Annulus(Vector3.Zero, Tier2 - 0.5f, Tier2, R - 6.5f, R - 1.6f, MatId.MetalGrate, 26);
        RingPosts(b, Tier1, R - 12.2f, 18);
        RingPosts(b, Tier2, R - 6.7f, 20);

        for (int i = 0; i < 4; i++)
        {
            float a = i / 4f * MathX.TwoPi + MathX.Pi / 4f;
            Vector3 dir = new(MathF.Cos(a), 0, MathF.Sin(a));
            Vector3 inner = dir * (R - 13.5f);
            Vector3 outer = dir * (R - 4.5f);
            // Ramp from the floor to tier 1, aligned to the dominant axis.
            Vector3 mid = (inner + outer) * 0.5f;
            if (MathF.Abs(dir.X) > MathF.Abs(dir.Z))
                b.Ramp(new Vector3(MathF.Min(inner.X, outer.X), -0.1f, mid.Z - 3f),
                       new Vector3(MathF.Max(inner.X, outer.X), Tier1, mid.Z + 3f), dir.X > 0 ? 0 : 1, MatId.TechFloor);
            else
                b.Ramp(new Vector3(mid.X - 3f, -0.1f, MathF.Min(inner.Z, outer.Z)),
                       new Vector3(mid.X + 3f, Tier1, MathF.Max(inner.Z, outer.Z)), dir.Z > 0 ? 2 : 3, MatId.TechFloor);

            b.AddJumpPad(dir * (R - 20f) + new Vector3(0, 0.1f, 0), dir * (R - 4f) + new Vector3(0, Tier2 + 1.5f, 0),
                new Vector3(0.35f, 0.8f, 1f));
        }

        // --- centre column with the shield belt on top ---
        b.Prism(new Vector3(0, 3.2f, 0), 3.4f, 6.4f, 6, MatId.TechPanelDark);
        b.Prism(new Vector3(0, 6.6f, 0), 4.3f, 0.6f, 6, MatId.Trim);
        b.Torus(new Vector3(0, 7.4f, 0), 3.2f, 0.2f, MatId.EnergyPanel, 20, 8);
        b.AddLight(new Vector3(0, 8.4f, 0), new Vector3(0.4f, 0.85f, 1f), 18f, 5f);
        b.AddJumpPad(new Vector3(-6.5f, 0.1f, 0f), new Vector3(-1.8f, 7.6f, 0f), new Vector3(0.35f, 0.8f, 1f));
        b.AddJumpPad(new Vector3(6.5f, 0.1f, 0f), new Vector3(1.8f, 7.6f, 0f), new Vector3(0.35f, 0.8f, 1f));

        // --- scattered low cover ---
        for (int i = 0; i < 8; i++)
        {
            float a = i / 8f * MathX.TwoPi + 0.2f;
            Vector3 p = new(MathF.Cos(a) * 15f, 0f, MathF.Sin(a) * 15f);
            b.Solid(p - new Vector3(1.9f, 0f, 1.9f), p + new Vector3(1.9f, 1.7f, 1.9f), MatId.TechPanelDark);
            b.Decor(p - new Vector3(2.1f, 0f, 2.1f), p + new Vector3(2.1f, 0.18f, 2.1f), MatId.Trim);
        }

        // --- placements ---
        b.Weapon(new Vector3(0f, 7.7f, 0f), WeaponKind.RocketLauncher);
        b.Item(new Vector3(0f, 7.7f, 2.2f), PickupKind.ShieldBelt);
        for (int i = 0; i < 8; i++)
        {
            float a = i / 8f * MathX.TwoPi;
            Vector3 dir = new(MathF.Cos(a), 0, MathF.Sin(a));
            var w = (WeaponKind)(1 + i % 8);
            b.Weapon(dir * 22f + new Vector3(0, 0.9f, 0), w);
            b.Ammo(dir * 19f + new Vector3(0, 0.7f, 0), (AmmoKind)(i % (int)AmmoKind.Count));
            b.Item(dir * 11f + new Vector3(0, 0.6f, 0), PickupKind.HealthVial);
            b.Spawn(dir * 25f + new Vector3(0, 0.2f, 0), 90f - a * MathX.Rad2Deg);
        }
        for (int i = 0; i < 4; i++)
        {
            float a = i / 4f * MathX.TwoPi + MathX.Pi / 4f;
            Vector3 dir = new(MathF.Cos(a), 0, MathF.Sin(a));
            b.Item(dir * (R - 9f) + new Vector3(0, Tier1 + 0.8f, 0), i % 2 == 0 ? PickupKind.BodyArmor : PickupKind.ThighPads);
            b.Item(dir * (R - 4f) + new Vector3(0, Tier2 + 0.8f, 0), PickupKind.HealthPack);
            b.Weapon(dir * (R - 4f) + new Vector3(0, Tier2 + 0.9f, 0), WeaponKind.SniperRifle);
            b.Spawn(dir * (R - 9f) + new Vector3(0, Tier1 + 0.2f, 0), 90f - a * MathX.Rad2Deg);
        }
        b.Item(new Vector3(0f, 0.7f, 20f), PickupKind.DamageAmp);
        b.Item(new Vector3(0f, 0.7f, -20f), PickupKind.Invisibility);
        b.Item(new Vector3(20f, 0.7f, 0f), PickupKind.SuperHealth);
        b.Item(new Vector3(-20f, 0.7f, 0f), PickupKind.JumpBoots);

        return b.Build(gl);
    }

    // ================================================================ CTF-雙子要塞

    private static Level BuildTwinForts(GL gl)
    {
        var b = new LevelBuilder(Loc.MapTwinForts, Loc.MapTwinFortsDesc);
        var env = b.Level.Environment;
        env.SunDirection = Vector3.Normalize(new Vector3(-0.42f, -0.72f, 0.30f));
        env.SunColor = new Vector3(3.9f, 3.5f, 2.9f);
        // Both forts are roofed, so ambient carries the interiors while the sun lights midfield.
        env.AmbientSky = new Vector3(0.36f, 0.40f, 0.50f);
        env.AmbientGround = new Vector3(0.13f, 0.13f, 0.12f);
        env.SkyTop = new Vector3(0.05f, 0.13f, 0.30f);
        env.SkyHorizon = new Vector3(0.48f, 0.42f, 0.44f);
        env.StarStrength = 0.2f;
        env.CloudStrength = 0.75f;
        env.FogColor = new Vector3(0.14f, 0.15f, 0.19f);
        env.FogDensity = 0.014f;

        const float HX = 30f;
        const float BaseZ = 46f;      // centre of each fort
        const float WallTop = 20f;

        b.Solid(new Vector3(-HX, -1.4f, -BaseZ - 16f), new Vector3(HX, 0f, BaseZ + 16f), MatId.TechFloor);
        b.Solid(new Vector3(-HX - 2f, -2f, -BaseZ - 18f), new Vector3(-HX, WallTop, BaseZ + 18f), MatId.TechWall);
        b.Solid(new Vector3(HX, -2f, -BaseZ - 18f), new Vector3(HX + 2f, WallTop, BaseZ + 18f), MatId.TechWall);
        b.Solid(new Vector3(-HX, -2f, -BaseZ - 18f), new Vector3(HX, WallTop, -BaseZ - 16f), MatId.TechWall);
        b.Solid(new Vector3(-HX, -2f, BaseZ + 16f), new Vector3(HX, WallTop, BaseZ + 18f), MatId.TechWall);

        for (int side = 0; side < 2; side++)
        {
            float sign = side == 0 ? -1f : 1f;
            Team team = side == 0 ? Team.Red : Team.Blue;
            Vector3 col = GameTypes.TeamColor(team);
            MatId teamMat = team == Team.Red ? MatId.TeamRed : MatId.TeamBlue;
            float z = BaseZ * sign;

            // --- fort shell: a room open toward the centre ---
            b.Solid(new Vector3(-22f, 0f, z - 14f * sign), new Vector3(-19f, 13f, z + 14f * sign), MatId.TechWall);
            b.Solid(new Vector3(19f, 0f, z - 14f * sign), new Vector3(22f, 13f, z + 14f * sign), MatId.TechWall);
            // Back wall of the fort.
            Vector3 backMin = new(-22f, 0f, sign > 0 ? z + 11f : z - 14f);
            Vector3 backMax = new(22f, 13f, sign > 0 ? z + 14f : z - 11f);
            b.Solid(backMin, backMax, teamMat, true, 0.6f);
            // Roof.
            b.Solid(new Vector3(-22f, 13f, z - 14f * sign), new Vector3(22f, 14.4f, z + 14f * sign), MatId.TechPanelDark);

            // Front face with a wide entrance plus two side doors.
            float frontZ = z - 12f * sign;
            b.Solid(new Vector3(-22f, 0f, MathF.Min(frontZ, frontZ - 2f * sign)),
                    new Vector3(-11f, 13f, MathF.Max(frontZ, frontZ - 2f * sign)), teamMat, true, 0.6f);
            b.Solid(new Vector3(11f, 0f, MathF.Min(frontZ, frontZ - 2f * sign)),
                    new Vector3(22f, 13f, MathF.Max(frontZ, frontZ - 2f * sign)), teamMat, true, 0.6f);
            b.Solid(new Vector3(-11f, 6.5f, MathF.Min(frontZ, frontZ - 2f * sign)),
                    new Vector3(11f, 13f, MathF.Max(frontZ, frontZ - 2f * sign)), teamMat, true, 0.6f);

            // --- flag room: raised dais at the back ---
            Vector3 flagPos = new(0f, 1.2f, z + 6f * sign);
            b.Solid(new Vector3(-6f, 0f, flagPos.Z - 5f), new Vector3(6f, 1.2f, flagPos.Z + 5f), MatId.TechPanelDark);
            b.Ramp(new Vector3(-3.5f, 0f, flagPos.Z - 8f), new Vector3(3.5f, 1.2f, flagPos.Z - 5f),
                sign > 0 ? 2 : 3, MatId.TechFloor);
            b.AddFlagBase(flagPos, team, sign > 0 ? 180f : 0f);
            b.AddLight(new Vector3(0, 6f, flagPos.Z), col, 20f, 6f);

            // --- upper gallery inside the fort ---
            float galY = 7.0f;
            b.Solid(new Vector3(-22f, galY - 0.4f, z - 11f * sign), new Vector3(-13f, galY, z + 11f * sign), MatId.MetalGrate);
            b.Solid(new Vector3(13f, galY - 0.4f, z - 11f * sign), new Vector3(22f, galY, z + 11f * sign), MatId.MetalGrate);
            b.Solid(new Vector3(-13f, galY - 0.4f, z + 8f * sign), new Vector3(13f, galY, z + 11f * sign), MatId.MetalGrate);
            b.Stairs(new Vector3(-17.5f, 0f, z - 9f * sign), new Vector3(-17.5f, galY, z - 1f * sign), 6f, 12,
                MatId.TechFloor, alongX: false);
            b.Stairs(new Vector3(17.5f, 0f, z - 9f * sign), new Vector3(17.5f, galY, z - 1f * sign), 6f, 12,
                MatId.TechFloor, alongX: false);

            // --- forward battlements flanking the entrance ---
            b.Solid(new Vector3(-19f, 0f, z - 22f * sign), new Vector3(-12f, 5.5f, z - 15f * sign), MatId.TechPanelDark);
            b.Solid(new Vector3(12f, 0f, z - 22f * sign), new Vector3(19f, 5.5f, z - 15f * sign), MatId.TechPanelDark);
            b.Ramp(new Vector3(-19f, 0f, z - 26f * sign < z - 22f * sign ? z - 26f * sign : z - 22f * sign),
                   new Vector3(-12f, 5.5f, z - 26f * sign < z - 22f * sign ? z - 22f * sign : z - 26f * sign),
                   sign > 0 ? 3 : 2, MatId.TechFloor);
            b.Ramp(new Vector3(12f, 0f, z - 26f * sign < z - 22f * sign ? z - 26f * sign : z - 22f * sign),
                   new Vector3(19f, 5.5f, z - 26f * sign < z - 22f * sign ? z - 22f * sign : z - 26f * sign),
                   sign > 0 ? 3 : 2, MatId.TechFloor);

            b.CeilingLamp(new Vector3(0, 12.5f, z), col * 0.5f + new Vector3(0.55f), 30f, 10f, 1.5f);
            b.CeilingLamp(new Vector3(-15f, 12.5f, z - 5f * sign), new Vector3(0.85f, 0.90f, 1f), 26f, 8f);
            b.CeilingLamp(new Vector3(15f, 12.5f, z - 5f * sign), new Vector3(0.85f, 0.90f, 1f), 26f, 8f);
            b.CeilingLamp(new Vector3(0, 12.5f, z - 18f * sign), new Vector3(0.85f, 0.90f, 1f), 26f, 8f);

            // --- team spawns ---
            for (int i = 0; i < 6; i++)
            {
                float sx = -14f + i * 5.6f;
                b.Spawn(new Vector3(sx, 0.2f, z + 3f * sign), sign > 0 ? 180f : 0f, team);
            }
            b.Spawn(new Vector3(-17.5f, galY + 0.2f, z + 5f * sign), sign > 0 ? 180f : 0f, team);
            b.Spawn(new Vector3(17.5f, galY + 0.2f, z + 5f * sign), sign > 0 ? 180f : 0f, team);

            // --- fort loadout ---
            b.Weapon(new Vector3(-16f, 0.9f, z - 4f * sign), WeaponKind.FlakCannon);
            b.Weapon(new Vector3(16f, 0.9f, z - 4f * sign), WeaponKind.Minigun);
            b.Weapon(new Vector3(-17.5f, galY + 0.9f, z + 2f * sign), WeaponKind.SniperRifle);
            b.Weapon(new Vector3(17.5f, galY + 0.9f, z + 2f * sign), WeaponKind.ShockRifle);
            b.Weapon(new Vector3(0f, 0.9f, z - 9f * sign), WeaponKind.PulseGun);
            b.Weapon(new Vector3(-15.5f, 5.9f, z - 18.5f * sign), WeaponKind.RocketLauncher);
            b.Weapon(new Vector3(15.5f, 5.9f, z - 18.5f * sign), WeaponKind.Ripper);
            b.Item(new Vector3(0f, 2.0f, z + 9f * sign), PickupKind.BodyArmor);
            b.Item(new Vector3(-8f, 0.7f, z - 6f * sign), PickupKind.HealthPack);
            b.Item(new Vector3(8f, 0.7f, z - 6f * sign), PickupKind.HealthPack);
            b.Item(new Vector3(0f, galY + 0.8f, z + 9.5f * sign), PickupKind.ThighPads);
            b.Ammo(new Vector3(-13f, 0.7f, z - 4f * sign), AmmoKind.FlakShells);
            b.Ammo(new Vector3(13f, 0.7f, z - 4f * sign), AmmoKind.MinigunBullets);
            b.Ammo(new Vector3(-15.5f, 5.7f, z - 20.5f * sign), AmmoKind.Rockets);
            b.Ammo(new Vector3(15.5f, 5.7f, z - 20.5f * sign), AmmoKind.Blades);
            b.Ammo(new Vector3(-17.5f, galY + 0.7f, z + 4f * sign), AmmoKind.SniperRounds);
            b.Ammo(new Vector3(17.5f, galY + 0.7f, z + 4f * sign), AmmoKind.ShockCore);
        }

        // --- midfield: a raised central bridge with cover on either flank ---
        b.Solid(new Vector3(-9f, 0f, -13f), new Vector3(9f, 4.5f, 13f), MatId.TechPanelDark);
        b.Ramp(new Vector3(-6f, 0f, -20f), new Vector3(6f, 4.5f, -13f), 2, MatId.TechFloor);
        b.Ramp(new Vector3(-6f, 0f, 13f), new Vector3(6f, 4.5f, 20f), 3, MatId.TechFloor);
        b.Solid(new Vector3(-9f, 4.5f, -13f), new Vector3(-7f, 6.2f, 13f), MatId.Trim, true, 1.1f);
        b.Solid(new Vector3(7f, 4.5f, -13f), new Vector3(9f, 6.2f, 13f), MatId.Trim, true, 1.1f);
        b.CeilingLamp(new Vector3(0, 14f, 0), new Vector3(0.9f, 0.92f, 1f), 32f, 9f, 2f);

        for (int i = -1; i <= 1; i += 2)
        {
            b.Solid(new Vector3(i * 20f - 4f, 0f, -8f), new Vector3(i * 20f + 4f, 3.4f, 8f), MatId.Concrete);
            b.Ramp(new Vector3(i * 20f - 4f, 0f, -12f), new Vector3(i * 20f + 4f, 3.4f, -8f), 2, MatId.Concrete);
            b.Ramp(new Vector3(i * 20f - 4f, 0f, 8f), new Vector3(i * 20f + 4f, 3.4f, 12f), 3, MatId.Concrete);
            b.Item(new Vector3(i * 20f, 4.1f, 0f), PickupKind.HealthPack);
            b.AddJumpPad(new Vector3(i * 25f, 0.1f, 0f), new Vector3(i * 12f, 5.5f, 0f), new Vector3(0.4f, 0.9f, 1f));
        }

        b.Weapon(new Vector3(0f, 5.2f, 0f), WeaponKind.RocketLauncher);
        b.Item(new Vector3(0f, 5.2f, -5f), PickupKind.ShieldBelt);
        b.Item(new Vector3(0f, 5.2f, 5f), PickupKind.DamageAmp);
        b.Weapon(new Vector3(-24f, 0.9f, -24f), WeaponKind.BioRifle);
        b.Weapon(new Vector3(24f, 0.9f, 24f), WeaponKind.BioRifle);
        b.Weapon(new Vector3(24f, 0.9f, -24f), WeaponKind.Enforcer);
        b.Weapon(new Vector3(-24f, 0.9f, 24f), WeaponKind.Enforcer);
        b.Item(new Vector3(-26f, 0.7f, 0f), PickupKind.Invisibility);
        b.Item(new Vector3(26f, 0.7f, 0f), PickupKind.SuperHealth);
        b.Item(new Vector3(0f, 0.7f, -24f), PickupKind.JumpBoots);
        b.Item(new Vector3(0f, 0.7f, 24f), PickupKind.JumpBoots);
        for (int i = 0; i < 6; i++)
        {
            float t = -25f + i * 10f;
            b.Item(new Vector3(t, 0.6f, -18f), PickupKind.HealthVial);
            b.Item(new Vector3(t, 0.6f, 18f), PickupKind.HealthVial);
        }
        b.Spawn(new Vector3(0f, 5.2f, 0f), 0f);

        return b.Build(gl);
    }

    // ================================================================ shared helpers

    /// <summary>Non-colliding guard rail run: a top bar plus evenly spaced posts.</summary>
    private static void RailRun(LevelBuilder b, Vector3 a, Vector3 c, float height = 0.95f,
        MatId mat = MatId.Trim)
    {
        Vector3 min = Vector3.Min(a, c), max = Vector3.Max(a, c);
        b.Decor(new Vector3(min.X - 0.07f, min.Y + height - 0.10f, min.Z - 0.07f),
                new Vector3(max.X + 0.07f, max.Y + height, max.Z + 0.07f), mat, 1.4f);
        float len = Vector3.Distance(a, c);
        int posts = Math.Max(2, (int)(len / 2.6f));
        for (int i = 0; i <= posts; i++)
        {
            Vector3 p = Vector3.Lerp(a, c, i / (float)posts);
            b.Decor(p - new Vector3(0.06f, 0f, 0.06f), p + new Vector3(0.06f, height, 0.06f), mat, 1.4f);
        }
    }

    /// <summary>
    /// Guard posts spaced around a circle. Only the posts are emitted (no connecting bar),
    /// because a bar between two off-axis points would need a rotated box.
    /// </summary>
    private static void RingPosts(LevelBuilder b, float y, float radius, int count, float height = 0.95f,
        MatId mat = MatId.Trim)
    {
        for (int i = 0; i < count; i++)
        {
            float a = i / (float)count * MathX.TwoPi;
            Vector3 p = new(MathF.Cos(a) * radius, y, MathF.Sin(a) * radius);
            b.Decor(p - new Vector3(0.07f, 0f, 0.07f), p + new Vector3(0.07f, height, 0.07f), mat, 1.4f);
            b.Decor(p - new Vector3(0.14f, -height + 0.12f, 0.14f),
                    p + new Vector3(0.14f, height, 0.14f), mat, 1.4f);
        }
    }
}
