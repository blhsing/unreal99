using System.Numerics;
using Silk.NET.OpenGL;
using Unreal99.Core;
using Unreal99.Game;
using Unreal99.Rendering;
using Unreal99.UI;

namespace Unreal99.World;

/// <summary>
/// The Domination arenas, modelled on the stock DOM maps. Every one of the originals carries
/// exactly three control points, and the layouts here are built around that: three positions far
/// enough apart that no team can watch them all, joined by routes short enough that losing one is
/// worth answering immediately.
///
/// Control points are placed with named identifiers because the HUD shows them by name, and the
/// originals name theirs — Leadworks' are Tower, Bridge and Storage.
/// </summary>
public static partial class Maps
{
    // ================================================================ DOM-熔鉛廠

    /// <summary>
    /// A molten-lead works. The three points are the ones the original documents: a Tower ringed
    /// by molten lead and reached by two bridges, a Bridge point in the middle of a span across a
    /// second pool, and Storage on an island in molten iron. The lead is the map's real defence —
    /// every point is approached over something that will kill you.
    /// </summary>
    private static Level BuildLeadworks(GL gl)
    {
        var b = new LevelBuilder(Loc.MapLeadworks, Loc.MapLeadworksDesc);
        var env = b.Level.Environment;
        env.SunDirection = Vector3.Normalize(new Vector3(-0.24f, -0.92f, -0.30f));
        env.SunColor = new Vector3(1.5f, 1.35f, 1.1f);
        env.AmbientSky = new Vector3(0.26f, 0.24f, 0.26f);
        env.AmbientGround = new Vector3(0.24f, 0.12f, 0.05f);
        env.EnvIntensity = 0.36f;
        env.SkyTop = new Vector3(0.015f, 0.013f, 0.015f);
        env.SkyHorizon = new Vector3(0.10f, 0.07f, 0.05f);
        env.StarStrength = 0f;
        env.CloudStrength = 0.2f;
        env.FogColor = new Vector3(0.13f, 0.09f, 0.07f);
        env.FogSunColor = new Vector3(1f, 0.5f, 0.15f);
        env.FogDensity = 0.020f;

        const float HX = 46f, HZ = 34f, CeilY = 22f;
        const float Walk = 0f;

        b.Room(new Vector3(-HX - 2f, -9f, -HZ - 2f), new Vector3(HX + 2f, CeilY, HZ + 2f), 2f,
            MatId.Concrete, MatId.RustMetal, MatId.TechPanelDark, withCeiling: true, withFloor: false);
        b.Solid(new Vector3(-HX, -9f, -HZ), new Vector3(HX, -8f, HZ), MatId.Concrete, true, 0.8f);

        // --- three separate pools, not a lava sea ---
        // The first pass flooded the whole floor and left only walkways above it, which made the
        // map one continuous death plane: bots crossing between points simply fell in and died.
        // The original has three distinct pools with solid ground between them, which is also
        // what makes each control point its own approach problem rather than one shared hazard.
        const float PoolZ = 14f;
        foreach (var (px0, px1) in new[] { (-38f, -14f), (-8f, 8f), (16f, 40f) })
            b.Lava(new Vector3(px0, -9f, -PoolZ), new Vector3(px1, -6.4f, PoolZ));

        // Solid floor everywhere the pools are not.
        b.Solid(new Vector3(-HX, -1.6f, -HZ), new Vector3(HX, Walk, -PoolZ), MatId.Concrete, true, 0.8f);
        b.Solid(new Vector3(-HX, -1.6f, PoolZ), new Vector3(HX, Walk, HZ), MatId.Concrete, true, 0.8f);
        foreach (var (fx0, fx1) in new[] { (-HX, -38f), (-14f, -8f), (8f, 16f), (40f, HX) })
            b.Solid(new Vector3(fx0, -1.6f, -PoolZ), new Vector3(fx1, Walk, PoolZ), MatId.Concrete, true, 0.8f);

        // Rails along the pool edges so nobody strafes in by accident.
        foreach (var edge in new[] { -38f, -14f, -8f, 8f, 16f, 40f })
            RailRun(b, new Vector3(edge, Walk, -PoolZ), new Vector3(edge, Walk, PoolZ), 0.9f);

        // --- Tower: a lead-ringed island reached by two bridges ---
        // The deck sits flush with the floor and the tower rises above it. Two earlier versions
        // raised the deck instead — 5.5m then 2.5m — and in both the point stayed neutral for a
        // whole match because nothing could path onto it. Flush removes the question entirely:
        // the bridges are floor-level walkways, so the route is as reachable as plain ground.
        Vector3 tower = new(-26f, Walk, 0f);
        b.Solid(new Vector3(tower.X - 9f, -8f, tower.Z - 9f), new Vector3(tower.X + 9f, Walk, tower.Z + 9f),
            MatId.Concrete, true, 0.8f);
        // The structure that gives the point its name, standing on the island rather than being it.
        b.Solid(new Vector3(tower.X - 4f, Walk, tower.Z - 4f), new Vector3(tower.X + 4f, 13f, tower.Z + 4f),
            MatId.RustMetal, true, 0.8f);
        for (int i = 0; i < 8; i++)
        {
            float a = i / 8f * MathX.TwoPi;
            Vector3 d = new(MathF.Cos(a), 0f, MathF.Sin(a));
            b.Decor(tower + d * 4.3f + new Vector3(-0.4f, 3f, -0.4f),
                    tower + d * 4.3f + new Vector3(0.4f, 11f, 0.4f), MatId.EnergyPanel, 0.5f);
        }
        b.AddLight(tower + new Vector3(0f, 9f, 0f), new Vector3(1f, 0.55f, 0.2f), 24f, 5f);
        foreach (int s in new[] { -1, 1 })
        {
            b.Solid(new Vector3(tower.X - 3f, -1.2f, MathF.Min(s * 9f, s * PoolZ)),
                    new Vector3(tower.X + 3f, Walk, MathF.Max(s * 9f, s * PoolZ)), MatId.MetalGrate, true, 1f);
            RailRun(b, new Vector3(tower.X - 3f, Walk, s * 9f), new Vector3(tower.X - 3f, Walk, s * PoolZ));
            RailRun(b, new Vector3(tower.X + 3f, Walk, s * 9f), new Vector3(tower.X + 3f, Walk, s * PoolZ));
        }
        b.AddControlPoint(tower + new Vector3(6.5f, 0f, 0f), "Tower");

        // --- Bridge: a span across the middle pool, point dead centre ---
        Vector3 bridge = new(0f, 2.5f, 0f);
        b.Solid(new Vector3(-9f, 1.9f, -5f), new Vector3(9f, 2.5f, 5f), MatId.MetalGrate, true, 1f);
        // The span stops short of the wall so a ramp can climb to it. Running it all the way to
        // the wall left a 2.5m step at the end and no way up: the point sat unreachable and the
        // score never moved.
        foreach (int s in new[] { -1, 1 })
        {
            b.Solid(new Vector3(-3.5f, 1.9f, MathF.Min(s * 5f, s * 22f)),
                    new Vector3(3.5f, 2.5f, MathF.Max(s * 5f, s * 22f)), MatId.MetalGrate, true, 1f);
            RailRun(b, new Vector3(-3.5f, 2.5f, s * 5f), new Vector3(-3.5f, 2.5f, s * 22f));
            RailRun(b, new Vector3(3.5f, 2.5f, s * 5f), new Vector3(3.5f, 2.5f, s * 22f));
        }
        RailRun(b, new Vector3(-9f, 2.5f, -5f), new Vector3(-9f, 2.5f, 5f));
        RailRun(b, new Vector3(9f, 2.5f, -5f), new Vector3(9f, 2.5f, 5f));
        // Ramps up from the floor at 1-in-4, shallow enough for the nav graph to route over.
        // Rising axis points at the span, not away from it.
        foreach (int s in new[] { -1, 1 })
            b.Ramp(new Vector3(-3.5f, 0f, MathF.Min(s * 22f, s * 32f)),
                   new Vector3(3.5f, 2.5f, MathF.Max(s * 22f, s * 32f)), s < 0 ? 2 : 3, MatId.Concrete);
        b.AddControlPoint(bridge, "Bridge");

        // --- Storage: an island in molten iron, behind a door ---
        Vector3 storage = new(28f, 1.2f, 0f);
        b.Solid(new Vector3(storage.X - 10f, -8f, storage.Z - 10f), new Vector3(storage.X + 10f, 1.2f, storage.Z + 10f),
            MatId.Concrete, true, 0.8f);
        b.Solid(new Vector3(storage.X - 10f, 1.2f, storage.Z - 10f), new Vector3(storage.X - 8.6f, 6f, storage.Z + 10f),
            MatId.RustMetal, true, 0.9f);
        var rng = new Rng(0x1EAD);
        for (int i = 0; i < 8; i++)
        {
            float cx = storage.X + rng.Range(-7f, 7f), cz = storage.Z + rng.Range(-7f, 7f);
            if (MathF.Abs(cx - storage.X) < 3f && MathF.Abs(cz - storage.Z) < 3f) continue;
            float sz = rng.Range(1.2f, 2.1f);
            b.Solid(new Vector3(cx - sz, 1.2f, cz - sz), new Vector3(cx + sz, 1.2f + sz * 1.5f, cz + sz),
                rng.Chance(0.5f) ? MatId.RustMetal : MatId.TechPanelDark, true, 1.2f);
        }
        foreach (int s in new[] { -1, 1 })
        {
            b.Solid(new Vector3(storage.X - 3f, 0.6f, MathF.Min(s * 10f, s * 15f)),
                    new Vector3(storage.X + 3f, 1.2f, MathF.Max(s * 10f, s * 15f)), MatId.MetalGrate, true, 1f);
            b.Ramp(new Vector3(storage.X - 3f, 0f, MathF.Min(s * 15f, s * 20f)),
                   new Vector3(storage.X + 3f, 1.2f, MathF.Max(s * 15f, s * 20f)), s < 0 ? 2 : 3, MatId.Concrete);
        }
        b.AddControlPoint(storage, "Storage");

        for (int i = -1; i <= 1; i++)
            for (int s = -1; s <= 1; s += 2)
                b.CeilingLamp(new Vector3(i * 24f, CeilY - 1.6f, s * 20f), new Vector3(0.95f, 0.86f, 0.7f), 30f, 8f, 1.6f);
        for (int i = -2; i <= 2; i++)
            b.AddLight(new Vector3(i * 16f, -5f, 0f), new Vector3(1f, 0.42f, 0.10f), 26f, 6f, 1.8f, 0.24f);

        // --- loadout, per the original's list ---
        b.Weapon(new Vector3(-38f, Walk + 0.9f, 0f), WeaponKind.ShockRifle);
        b.Weapon(new Vector3(0f, Walk + 0.9f, -HZ + 3f), WeaponKind.PulseGun);
        b.Weapon(new Vector3(storage.X, 2.1f, 6f), WeaponKind.Ripper);
        b.Weapon(new Vector3(storage.X, 2.1f, -6f), WeaponKind.Ripper);
        b.Weapon(new Vector3(0f, Walk + 0.9f, HZ - 3f), WeaponKind.Minigun);
        b.Weapon(tower + new Vector3(5f, 0.9f, 0f), WeaponKind.Minigun);
        b.Weapon(new Vector3(-20f, Walk + 0.9f, -HZ + 3f), WeaponKind.FlakCannon);
        b.Weapon(new Vector3(storage.X + 6f, 2.1f, 0f), WeaponKind.FlakCannon);
        b.Weapon(new Vector3(-20f, Walk + 0.9f, HZ - 3f), WeaponKind.RocketLauncher);
        b.Weapon(new Vector3(20f, Walk + 0.9f, HZ - 3f), WeaponKind.RocketLauncher);
        b.Ammo(new Vector3(-36f, Walk + 0.7f, 4f), AmmoKind.ShockCore);
        b.Ammo(new Vector3(-18f, Walk + 0.7f, HZ - 3f), AmmoKind.Rockets);
        b.Ammo(new Vector3(18f, Walk + 0.7f, HZ - 3f), AmmoKind.Rockets);
        b.Ammo(new Vector3(2f, Walk + 0.7f, HZ - 3f), AmmoKind.MinigunBullets);
        b.Ammo(new Vector3(storage.X + 4f, 2.0f, 3f), AmmoKind.FlakShells);

        b.Item(new Vector3(0f, Walk + 0.8f, -HZ + 6f), PickupKind.SuperHealth);
        b.Item(new Vector3(storage.X + 8f, 2.0f, 0f), PickupKind.BodyArmor);
        b.Item(new Vector3(0f, Walk + 0.8f, HZ - 6f), PickupKind.ShieldBelt);
        b.Item(new Vector3(-20f, Walk + 0.8f, 0f), PickupKind.DamageAmp);
        b.Item(new Vector3(20f, Walk + 0.8f, 0f), PickupKind.Invisibility);
        for (int i = 0; i < 18; i++)
        {
            float a = i / 18f * MathX.TwoPi;
            b.Item(new Vector3(MathF.Cos(a) * 40f, Walk + 0.7f, MathF.Sin(a) * 28f), PickupKind.HealthPack);
        }

        for (int i = 0; i < 4; i++)
        {
            b.Spawn(new Vector3(-40f, Walk + 0.2f, -12f + i * 8f), 90f, Team.Red);
            b.Spawn(new Vector3(40f, Walk + 0.2f, -12f + i * 8f), -90f, Team.Blue);
        }
        return b.Build(gl);
    }

    // ================================================================ DOM-賽斯瑪之墓

    /// <summary>
    /// An Egyptian tomb: three burial chambers off a cross of short corridors, one control point
    /// in each. The original's armoury is famously lopsided — four miniguns and six rocket
    /// launchers — and that is what makes its corridors feel the way they do, so it is kept.
    /// </summary>
    private static Level BuildSesmar(GL gl)
    {
        var b = new LevelBuilder(Loc.MapSesmar, Loc.MapSesmarDesc);
        var env = b.Level.Environment;
        env.SunDirection = Vector3.Normalize(new Vector3(-0.30f, -0.90f, -0.32f));
        env.SunColor = new Vector3(1.7f, 1.5f, 1.1f);
        env.AmbientSky = new Vector3(0.28f, 0.25f, 0.20f);
        env.AmbientGround = new Vector3(0.16f, 0.12f, 0.08f);
        env.EnvIntensity = 0.34f;
        env.SkyTop = new Vector3(0.02f, 0.017f, 0.012f);
        env.SkyHorizon = new Vector3(0.10f, 0.08f, 0.05f);
        env.StarStrength = 0f;
        env.CloudStrength = 0f;
        env.FogColor = new Vector3(0.12f, 0.10f, 0.07f);
        env.FogDensity = 0.024f;

        const float CeilY = 15f;
        var chambers = new[]
        {
            (pos: new Vector3(0f, 0f, -30f), name: "North Tomb"),
            (pos: new Vector3(-28f, 0f, 20f), name: "West Tomb"),
            (pos: new Vector3(28f, 0f, 20f), name: "East Tomb"),
        };

        b.Solid(new Vector3(-48f, -1.6f, -48f), new Vector3(48f, 0f, 48f), MatId.Concrete, true, 0.7f);
        b.Room(new Vector3(-48f, -1.6f, -48f), new Vector3(48f, CeilY, 48f), 2f,
            MatId.Concrete, MatId.Rock, MatId.TechPanelDark, withCeiling: true, withFloor: false);

        // --- central hall the three corridors meet in ---
        b.Prism(new Vector3(0f, 0f, 0f), 5f, 4.2f, 8, MatId.Rock);
        b.Decor(new Vector3(-5.4f, 4.2f, -5.4f), new Vector3(5.4f, 4.9f, 5.4f), MatId.Trim, 0.9f);
        b.AddLight(new Vector3(0f, 7f, 0f), new Vector3(1f, 0.78f, 0.42f), 26f, 6f, 1.6f, 0.10f);

        var rng = new Rng(0x5E5A);
        foreach (var (pos, name) in chambers)
        {
            // Chamber: a pillared room with the point on a low dais at its centre.
            b.Room(pos + new Vector3(-13f, -1.6f, -13f), pos + new Vector3(13f, CeilY, 13f), 1.8f,
                MatId.Concrete, MatId.Rock, MatId.TechPanelDark, withCeiling: true, withFloor: false);
            foreach (var (px, pz) in new[] { (-8f, -8f), (8f, -8f), (-8f, 8f), (8f, 8f) })
                b.Prism(pos + new Vector3(px, 0f, pz), 1.5f, 9f, 8, MatId.Rock);
            b.Solid(pos + new Vector3(-4f, 0f, -4f), pos + new Vector3(4f, 0.8f, 4f), MatId.Trim, true, 0.9f);
            b.AddControlPoint(pos + new Vector3(0f, 0.8f, 0f), name);
            b.CeilingLamp(pos + new Vector3(0f, CeilY - 1.6f, 0f), new Vector3(1f, 0.82f, 0.5f), 26f, 7f, 1.4f);

            // Corridor back to the middle: short, so losing a point is answerable at once.
            Vector3 dir = MathX.SafeNormalize(-pos.FlatXZ(), MathX.Forward);
            Vector3 mid = pos * 0.5f;
            bool alongZ = MathF.Abs(dir.Z) > MathF.Abs(dir.X);
            Vector3 half = alongZ ? new Vector3(3.5f, 0f, pos.Length() * 0.5f) : new Vector3(pos.Length() * 0.5f, 0f, 3.5f);
            b.Solid(mid - half - new Vector3(0f, 1.6f, 0f), mid + half + new Vector3(0f, 0f, 0f), MatId.Concrete, true, 0.8f);

            for (int i = 0; i < 4; i++)
            {
                float a = i / 4f * MathX.TwoPi + 0.7f;
                b.Spawn(pos + new Vector3(MathF.Cos(a) * 9f, 0.2f, MathF.Sin(a) * 9f),
                    -a * MathX.Rad2Deg + 90f, i % 2 == 0 ? Team.Red : Team.Blue);
            }
            _ = rng;
        }

        // --- the original's lopsided armoury: 6 rockets, 4 miniguns, 3 pulse, 2 shock ---
        for (int i = 0; i < 6; i++)
        {
            float a = i / 6f * MathX.TwoPi;
            b.Weapon(new Vector3(MathF.Cos(a) * 17f, 0.9f, MathF.Sin(a) * 17f), WeaponKind.RocketLauncher);
            b.Ammo(new Vector3(MathF.Cos(a) * 19f, 0.7f, MathF.Sin(a) * 19f), AmmoKind.Rockets);
        }
        for (int i = 0; i < 4; i++)
        {
            float a = i / 4f * MathX.TwoPi + 0.5f;
            b.Weapon(new Vector3(MathF.Cos(a) * 26f, 0.9f, MathF.Sin(a) * 26f), WeaponKind.Minigun);
            b.Ammo(new Vector3(MathF.Cos(a) * 28f, 0.7f, MathF.Sin(a) * 28f), AmmoKind.MinigunBullets);
        }
        for (int i = 0; i < 3; i++)
        {
            float a = i / 3f * MathX.TwoPi + 1.1f;
            b.Weapon(new Vector3(MathF.Cos(a) * 11f, 0.9f, MathF.Sin(a) * 11f), WeaponKind.PulseGun);
        }
        b.Weapon(new Vector3(-9f, 0.9f, 0f), WeaponKind.ShockRifle);
        b.Weapon(new Vector3(9f, 0.9f, 0f), WeaponKind.ShockRifle);

        b.Item(new Vector3(0f, 5.1f, 0f), PickupKind.SuperHealth);
        b.Item(new Vector3(0f, 0.8f, 12f), PickupKind.ShieldBelt);
        for (int i = 0; i < 12; i++)
        {
            float a = i / 12f * MathX.TwoPi;
            b.Item(new Vector3(MathF.Cos(a) * 22f, 0.7f, MathF.Sin(a) * 22f), PickupKind.HealthPack);
        }
        for (int i = 0; i < 34; i++)
        {
            float a = i / 34f * MathX.TwoPi * 3f;
            float r = 8f + (i % 5) * 6.5f;
            b.Item(new Vector3(MathF.Cos(a) * r, 0.6f, MathF.Sin(a) * r), PickupKind.HealthVial);
        }
        return b.Build(gl);
    }

    // ================================================================ DOM-奧登含水層

    /// <summary>
    /// A small temple aquifer built for four to six, so the three points sit close together and
    /// the map is fought vertically: a flooded floor, a colonnade above it, and a shrine on top.
    /// </summary>
    private static Level BuildOlden(GL gl)
    {
        var b = new LevelBuilder(Loc.MapOlden, Loc.MapOldenDesc);
        var env = b.Level.Environment;
        env.SunDirection = Vector3.Normalize(new Vector3(-0.34f, -0.86f, -0.38f));
        env.SunColor = new Vector3(2.1f, 1.95f, 1.6f);
        env.AmbientSky = new Vector3(0.28f, 0.30f, 0.34f);
        env.AmbientGround = new Vector3(0.12f, 0.13f, 0.13f);
        env.EnvIntensity = 0.42f;
        env.SkyTop = new Vector3(0.02f, 0.03f, 0.05f);
        env.SkyHorizon = new Vector3(0.10f, 0.11f, 0.12f);
        env.StarStrength = 0.3f;
        env.CloudStrength = 0.3f;
        env.FogColor = new Vector3(0.10f, 0.12f, 0.13f);
        env.FogDensity = 0.020f;

        const float H = 26f, CeilY = 24f;
        const float Colonnade = 7.5f, Shrine = 15f;

        b.Room(new Vector3(-H - 2f, -7f, -H - 2f), new Vector3(H + 2f, CeilY, H + 2f), 2f,
            MatId.Concrete, MatId.Rock, MatId.TechPanelDark, withCeiling: true, withFloor: false);
        b.Solid(new Vector3(-H, -7f, -H), new Vector3(H, -5.4f, H), MatId.Rock, true, 0.7f);

        // --- island, moat, outer ring ---
        // Only the moat is flooded. The first version submerged the entire floor under two
        // metres of water, so everyone who spawned simply drowned where they stood — an
        // aquifer, not a swimming pool.
        const float Floor = -3.0f;
        b.Annulus(Vector3.Zero, -7f, Floor, 0f, 9f, MatId.Concrete, slabs: 20, collide: true, uvScale: 0.8f);
        b.Annulus(Vector3.Zero, -7f, Floor, 14f, H * 1.5f, MatId.Concrete, slabs: 26, collide: true, uvScale: 0.8f);
        b.Water(new Vector3(-14f, -5.4f, -14f), new Vector3(14f, -3.9f, 14f));

        // Four causeways so the spring is reachable on foot from every side.
        foreach (var (dx, dz) in new[] { (1f, 0f), (-1f, 0f), (0f, 1f), (0f, -1f) })
        {
            Vector3 a = new(dx * 8f, 0f, dz * 8f), c = new(dx * 15f, 0f, dz * 15f);
            Vector3 lo = Vector3.Min(a, c) - new Vector3(MathF.Abs(dz) * 2.5f, 0f, MathF.Abs(dx) * 2.5f);
            Vector3 hi = Vector3.Max(a, c) + new Vector3(MathF.Abs(dz) * 2.5f, 0f, MathF.Abs(dx) * 2.5f);
            b.Solid(new Vector3(lo.X, Floor - 0.6f, lo.Z), new Vector3(hi.X, Floor, hi.Z), MatId.Concrete, true, 0.8f);
        }
        b.AddControlPoint(new Vector3(0f, Floor, 0f), "Spring");

        // --- colonnade ring, second point on its north side ---
        b.Annulus(Vector3.Zero, Colonnade - 0.6f, Colonnade, 12f, H, MatId.Concrete, slabs: 24, collide: true, uvScale: 0.8f);
        RingPosts(b, Colonnade, 12.4f, 18);
        for (int i = 0; i < 10; i++)
        {
            float a = i / 10f * MathX.TwoPi;
            b.Prism(new Vector3(MathF.Cos(a) * 15f, Colonnade, MathF.Sin(a) * 15f), 1.1f, 7f, 8, MatId.Concrete);
        }
        // The point sits on the outer floor below the colonnade, not up on the ring. Elevated
        // points reachable only by jump pad stayed neutral for whole matches here and on
        // Leadworks — the graph will not commit to a pad-only approach, and bots that did make
        // the jump took fall damage coming back down. The colonnade above is still worth holding
        // as a firing position; it just is not the thing you have to stand on.
        b.AddControlPoint(new Vector3(0f, Floor, -20f), "Colonnade");
        foreach (int s in new[] { -1, 1 })
            b.AddJumpPad(new Vector3(s * 16f, -3.0f, 0f), new Vector3(s * 19f, Colonnade + 2f, 0f),
                new Vector3(0.5f, 0.85f, 1f));

        // --- shrine on top, third point, reachable only by pad ---
        b.Solid(new Vector3(-8f, Shrine - 0.8f, -8f), new Vector3(8f, Shrine, 8f), MatId.Concrete, true, 0.8f);
        foreach (var (px, pz) in new[] { (-6.4f, -6.4f), (6.4f, -6.4f), (-6.4f, 6.4f), (6.4f, 6.4f) })
            b.Prism(new Vector3(px, Shrine, pz), 1.0f, 6f, 8, MatId.Concrete);
        b.AddControlPoint(new Vector3(0f, Floor, 20f), "Shrine");
        foreach (int s in new[] { -1, 1 })
            b.AddJumpPad(new Vector3(0f, Colonnade + 0.1f, s * 19f), new Vector3(0f, Shrine + 2.4f, s * 5f),
                new Vector3(0.5f, 0.85f, 1f));
        b.AddLight(new Vector3(0f, Shrine + 5f, 0f), new Vector3(1f, 0.85f, 0.55f), 24f, 6f);

        for (int i = 0; i < 4; i++)
        {
            float a = i / 4f * MathX.TwoPi + 0.4f;
            b.CeilingLamp(new Vector3(MathF.Cos(a) * 18f, CeilY - 1.6f, MathF.Sin(a) * 18f),
                new Vector3(0.9f, 0.9f, 1f), 26f, 7f, 1.4f);
        }

        // --- loadout per the original: no minigun body, only its ammo ---
        b.Weapon(new Vector3(-16f, -3.0f, 0f), WeaponKind.BioRifle);
        b.Weapon(new Vector3(16f, -3.0f, 0f), WeaponKind.Ripper);
        b.Weapon(new Vector3(0f, Shrine + 0.9f, 5f), WeaponKind.SniperRifle);
        b.Weapon(new Vector3(0f, Colonnade + 0.9f, 19f), WeaponKind.FlakCannon);
        b.Weapon(new Vector3(-19f, Colonnade + 0.9f, 0f), WeaponKind.FlakCannon);
        b.Weapon(new Vector3(0f, -3.0f, -16f), WeaponKind.RocketLauncher);
        b.Weapon(new Vector3(19f, Colonnade + 0.9f, 0f), WeaponKind.ShockRifle);
        b.Ammo(new Vector3(3f, Colonnade + 0.7f, 19f), AmmoKind.MinigunBullets);
        b.Ammo(new Vector3(-3f, Colonnade + 0.7f, 19f), AmmoKind.MinigunBullets);
        b.Ammo(new Vector3(0f, -3.0f, -13f), AmmoKind.Rockets);
        b.Ammo(new Vector3(0f, Shrine + 0.7f, 2f), AmmoKind.SniperRounds);

        b.Item(new Vector3(0f, Shrine + 0.8f, -5f), PickupKind.ShieldBelt);
        b.Item(new Vector3(0f, -3.0f, 16f), PickupKind.BodyArmor);
        b.Item(new Vector3(0f, -2.9f, 0f), PickupKind.SuperHealth);
        for (int i = 0; i < 4; i++)
        {
            float a = i / 4f * MathX.TwoPi;
            b.Item(new Vector3(MathF.Cos(a) * 20f, Colonnade + 0.7f, MathF.Sin(a) * 20f), PickupKind.HealthPack);
        }
        for (int i = 0; i < 10; i++)
        {
            float a = i / 10f * MathX.TwoPi;
            b.Item(new Vector3(MathF.Cos(a) * 11f, -3.0f, MathF.Sin(a) * 11f), PickupKind.HealthVial);
        }

        for (int i = 0; i < 3; i++)
        {
            b.Spawn(new Vector3(-19f, -3.0f, -8f + i * 8f), 90f, Team.Red);
            b.Spawn(new Vector3(19f, -3.0f, -8f + i * 8f), -90f, Team.Blue);
        }
        return b.Build(gl);
    }

    // ================================================================ DOM-灰燼鑄造廠

    /// <summary>
    /// A shut-down foundry. The sources give no inventory for this one, so the layout follows the
    /// theme the description does give — furnace, crane and casting floor — and the loadout is
    /// built to suit those three positions rather than invented detail passed off as original.
    /// </summary>
    private static Level BuildCinder(GL gl)
    {
        var b = new LevelBuilder(Loc.MapCinder, Loc.MapCinderDesc);
        var env = b.Level.Environment;
        env.SunDirection = Vector3.Normalize(new Vector3(-0.18f, -0.94f, -0.28f));
        env.SunColor = new Vector3(1.3f, 1.2f, 1.1f);
        env.AmbientSky = new Vector3(0.24f, 0.23f, 0.25f);
        env.AmbientGround = new Vector3(0.20f, 0.11f, 0.06f);
        env.EnvIntensity = 0.34f;
        env.SkyTop = new Vector3(0.014f, 0.013f, 0.016f);
        env.SkyHorizon = new Vector3(0.09f, 0.07f, 0.06f);
        env.StarStrength = 0f;
        env.CloudStrength = 0.15f;
        env.FogColor = new Vector3(0.12f, 0.09f, 0.08f);
        env.FogSunColor = new Vector3(1f, 0.48f, 0.16f);
        env.FogDensity = 0.022f;

        const float HX = 38f, HZ = 30f, CeilY = 24f;
        const float Gantry = 9f;

        b.Solid(new Vector3(-HX, -1.6f, -HZ), new Vector3(HX, 0f, HZ), MatId.Concrete, true, 0.8f);
        b.Room(new Vector3(-HX - 2f, -1.6f, -HZ - 2f), new Vector3(HX + 2f, CeilY, HZ + 2f), 2f,
            MatId.Concrete, MatId.RustMetal, MatId.TechPanelDark, withCeiling: true, withFloor: false);

        // --- Furnace: a glowing drum with the point on its apron ---
        Vector3 furnace = new(-25f, 0f, 0f);
        b.Prism(furnace + new Vector3(0f, 0f, 0f), 6.5f, 12f, 10, MatId.RustMetal);
        for (int i = 0; i < 10; i++)
        {
            float a = i / 10f * MathX.TwoPi;
            Vector3 d = new(MathF.Cos(a), 0f, MathF.Sin(a));
            b.Decor(furnace + d * 6.7f + new Vector3(-0.5f, 2.2f, -0.5f),
                    furnace + d * 6.7f + new Vector3(0.5f, 7.5f, 0.5f), MatId.EnergyPanel, 0.5f);
        }
        b.AddLight(furnace + new Vector3(0f, 6f, 0f), new Vector3(1f, 0.42f, 0.10f), 30f, 8f, 2.2f, 0.26f);
        b.AddControlPoint(furnace + new Vector3(9.5f, 0f, 0f), "Furnace");

        // --- Casting floor: a lava channel with the point on the island between the moulds ---
        b.Solid(new Vector3(-6f, -6f, -HZ), new Vector3(6f, -1.6f, HZ), MatId.Concrete, true, 0.8f);
        b.Lava(new Vector3(-6f, -6f, -HZ), new Vector3(6f, -3.6f, HZ));
        foreach (int s in new[] { -1, 1 })
            b.Solid(new Vector3(-6f, -1.6f, s * 7f), new Vector3(6f, 0f, s * 13f), MatId.MetalGrate, true, 0.9f);
        b.Solid(new Vector3(-6f, -1.6f, -4f), new Vector3(6f, 0f, 4f), MatId.MetalGrate, true, 0.9f);
        b.AddControlPoint(new Vector3(0f, 0f, 0f), "Casting");

        // --- Crane: a gantry over the far end, point on its deck ---
        b.Solid(new Vector3(14f, Gantry - 0.6f, -HZ + 4f), new Vector3(HX - 2f, Gantry, HZ - 4f),
            MatId.MetalGrate, true, 0.9f);
        RailRun(b, new Vector3(14f, Gantry, -HZ + 4f), new Vector3(14f, Gantry, HZ - 4f));
        b.Solid(new Vector3(20f, Gantry, -3f), new Vector3(32f, Gantry + 5f, 3f), MatId.RustMetal, true, 0.9f);
        // On the floor under the gantry, for the same reason as Olden's upper points: a pad-only
        // approach leaves the point uncontested all match.
        b.AddControlPoint(new Vector3(26f, 0f, 0f), "Crane");
        foreach (int s in new[] { -1, 1 })
            b.AddJumpPad(new Vector3(20f, 0.1f, s * (HZ - 6f)), new Vector3(20f, Gantry + 2.4f, s * (HZ - 8f)),
                new Vector3(0.45f, 0.85f, 1f));
        b.Ramp(new Vector3(8f, 0f, -HZ + 4f), new Vector3(14f, Gantry, -HZ + 14f), 0, MatId.Concrete);

        var rng = new Rng(0xC1D3);
        for (int i = 0; i < 12; i++)
        {
            float cx = rng.Range(-HX + 6f, HX - 6f), cz = rng.Range(-HZ + 6f, HZ - 6f);
            if (MathF.Abs(cx) < 9f) continue;
            if (cx > 12f && MathF.Abs(cz) < HZ - 4f && rng.Chance(0.5f)) continue;
            float sz = rng.Range(1.3f, 2.3f);
            b.Solid(new Vector3(cx - sz, 0f, cz - sz), new Vector3(cx + sz, sz * 1.6f, cz + sz),
                rng.Chance(0.5f) ? MatId.RustMetal : MatId.TechPanelDark, true, 1.2f);
        }
        for (int i = -1; i <= 1; i++)
            for (int s = -1; s <= 1; s += 2)
                b.CeilingLamp(new Vector3(i * 20f, CeilY - 1.6f, s * 18f), new Vector3(0.92f, 0.86f, 0.74f), 28f, 8f, 1.5f);

        b.Weapon(furnace + new Vector3(0f, 0.9f, 10f), WeaponKind.FlakCannon);
        b.Weapon(furnace + new Vector3(0f, 0.9f, -10f), WeaponKind.Minigun);
        b.Weapon(new Vector3(0f, 0.9f, -16f), WeaponKind.RocketLauncher);
        b.Weapon(new Vector3(0f, 0.9f, 16f), WeaponKind.ShockRifle);
        b.Weapon(new Vector3(24f, Gantry + 0.9f, -8f), WeaponKind.SniperRifle);
        b.Weapon(new Vector3(30f, 0.9f, 0f), WeaponKind.PulseGun);
        b.Weapon(new Vector3(-14f, 0.9f, 20f), WeaponKind.Ripper);
        b.Weapon(new Vector3(-14f, 0.9f, -20f), WeaponKind.BioRifle);
        b.Ammo(new Vector3(0f, 0.7f, -13f), AmmoKind.Rockets);
        b.Ammo(new Vector3(24f, Gantry + 0.7f, -11f), AmmoKind.SniperRounds);
        b.Ammo(furnace + new Vector3(3f, 0.7f, 10f), AmmoKind.FlakShells);

        b.Item(new Vector3(24f, Gantry + 0.8f, 0f), PickupKind.ShieldBelt);
        b.Item(furnace + new Vector3(-9.5f, 0.8f, 0f), PickupKind.BodyArmor);
        b.Item(new Vector3(0f, 0.8f, 22f), PickupKind.DamageAmp);
        b.Item(new Vector3(0f, 0.8f, -22f), PickupKind.SuperHealth);
        for (int i = 0; i < 12; i++)
        {
            float a = i / 12f * MathX.TwoPi;
            b.Item(new Vector3(MathF.Cos(a) * 30f, 0.7f, MathF.Sin(a) * 22f), PickupKind.HealthPack);
        }

        for (int i = 0; i < 4; i++)
        {
            b.Spawn(new Vector3(-HX + 5f, 0.2f, -12f + i * 8f), 90f, Team.Red);
            b.Spawn(new Vector3(HX - 5f, 0.2f, -12f + i * 8f), -90f, Team.Blue);
        }
        return b.Build(gl);
    }
}
