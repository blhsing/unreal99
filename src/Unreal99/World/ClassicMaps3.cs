using System.Numerics;
using Silk.NET.OpenGL;
using Unreal99.Core;
using Unreal99.Game;
using Unreal99.Rendering;
using Unreal99.UI;

namespace Unreal99.World;

/// <summary>
/// More homages to the 1999 roster: the industrial deck everyone remembers, the grinder,
/// the corporate core shaft, a mountaintop, the one-room arena, and two flag maps.
/// Layouts and routes are rebuilt from memory; all geometry is written here from scratch.
/// </summary>
public static partial class Maps
{
    // ================================================================ DM-十六號甲板

    /// <summary>
    /// The industrial deck: a lava channel splits the map end to end, the shock rifle sits on
    /// the bridge across the middle, and everything else is a loop of catwalks and pump rooms
    /// feeding back to that one contested crossing.
    /// </summary>
    private static Level BuildDeck16(GL gl)
    {
        var b = new LevelBuilder(Loc.MapDeck16, Loc.MapDeck16Desc);
        var env = b.Level.Environment;
        env.SunDirection = Vector3.Normalize(new Vector3(-0.24f, -0.93f, -0.28f));
        env.SunColor = new Vector3(1.5f, 1.5f, 1.6f);
        env.AmbientSky = new Vector3(0.30f, 0.32f, 0.38f);
        env.AmbientGround = new Vector3(0.16f, 0.10f, 0.07f);
        env.EnvIntensity = 0.42f;
        env.SkyTop = new Vector3(0.02f, 0.025f, 0.04f);
        env.SkyHorizon = new Vector3(0.10f, 0.09f, 0.10f);
        env.StarStrength = 0.25f;
        env.CloudStrength = 0.35f;
        env.FogColor = new Vector3(0.12f, 0.10f, 0.10f);
        env.FogSunColor = new Vector3(0.9f, 0.45f, 0.16f);
        env.FogDensity = 0.018f;

        const float HX = 34f, HZ = 30f;
        const float CeilY = 21f;
        const float Chan = 9f;        // half-width of the lava channel
        const float Upper = 8.5f;     // catwalk ring height
        const float LavaY = -8.6f;

        // --- shell ---
        b.Room(new Vector3(-HX - 2f, -11f, -HZ - 2f), new Vector3(HX + 2f, CeilY, HZ + 2f), 2f,
            MatId.TechFloor, MatId.TechWall, MatId.TechPanelDark, withCeiling: true, withFloor: false);

        // --- the channel: lava at the bottom, a service ledge either side, deck level above ---
        b.Solid(new Vector3(-HX, -11f, -HZ), new Vector3(HX, -10f, HZ), MatId.Concrete, true, 0.8f);
        b.Lava(new Vector3(-Chan, -11f, -HZ), new Vector3(Chan, LavaY, HZ));
        for (int s = -1; s <= 1; s += 2)
        {
            // Deck floor on both sides of the channel.
            float x0 = s < 0 ? -HX : Chan;
            float x1 = s < 0 ? -Chan : HX;
            b.Solid(new Vector3(x0, -1.6f, -HZ), new Vector3(x1, 0f, HZ), MatId.TechFloor, true, 0.9f);
            // Channel wall, with a narrow maintenance ledge partway down.
            b.Solid(new Vector3(s * Chan - s * 0.8f, -1.6f, -HZ), new Vector3(s * Chan, 0f, HZ), MatId.Trim, true, 0.7f);
            b.Solid(new Vector3(s * (Chan - 2.6f), -6.4f, -HZ + 6f), new Vector3(s * Chan, -5.6f, HZ - 6f),
                MatId.MetalGrate, true, 0.8f);
        }

        // --- the crossing everyone fights over ---
        b.Solid(new Vector3(-Chan, -0.5f, -3.2f), new Vector3(Chan, 0f, 3.2f), MatId.MetalGrate, true, 1.0f);
        RailRun(b, new Vector3(-Chan, 0f, -3.2f), new Vector3(Chan, 0f, -3.2f));
        RailRun(b, new Vector3(-Chan, 0f, 3.2f), new Vector3(Chan, 0f, 3.2f));
        // Two more crossings near the ends so the map is a loop, not a barbell.
        foreach (float bz in new[] { -20f, 20f })
        {
            b.Solid(new Vector3(-Chan, -0.5f, bz - 2f), new Vector3(Chan, 0f, bz + 2f), MatId.MetalGrate, true, 1.0f);
            RailRun(b, new Vector3(-Chan, 0f, bz - 2f), new Vector3(Chan, 0f, bz - 2f));
            RailRun(b, new Vector3(-Chan, 0f, bz + 2f), new Vector3(Chan, 0f, bz + 2f));
        }

        // --- upper catwalk ring hugging the outer walls ---
        b.Solid(new Vector3(-HX, Upper - 0.5f, -HZ), new Vector3(HX, Upper, -HZ + 6f), MatId.MetalGrate, true, 0.9f);
        b.Solid(new Vector3(-HX, Upper - 0.5f, HZ - 6f), new Vector3(HX, Upper, HZ), MatId.MetalGrate, true, 0.9f);
        b.Solid(new Vector3(-HX, Upper - 0.5f, -HZ + 6f), new Vector3(-HX + 7f, Upper, HZ - 6f), MatId.MetalGrate, true, 0.9f);
        b.Solid(new Vector3(HX - 7f, Upper - 0.5f, -HZ + 6f), new Vector3(HX, Upper, HZ - 6f), MatId.MetalGrate, true, 0.9f);
        RailRun(b, new Vector3(-HX + 7f, Upper, -HZ + 6f), new Vector3(HX - 7f, Upper, -HZ + 6f));
        RailRun(b, new Vector3(-HX + 7f, Upper, HZ - 6f), new Vector3(HX - 7f, Upper, HZ - 6f));
        RailRun(b, new Vector3(-HX + 7f, Upper, -HZ + 6f), new Vector3(-HX + 7f, Upper, HZ - 6f));
        RailRun(b, new Vector3(HX - 7f, Upper, -HZ + 6f), new Vector3(HX - 7f, Upper, HZ - 6f));

        // A high catwalk over the channel: the sniper perch, and the only way across up top.
        b.Solid(new Vector3(-Chan - 1f, Upper - 0.5f, -2.4f), new Vector3(Chan + 1f, Upper, 2.4f), MatId.MetalGrate, true, 1.0f);
        RailRun(b, new Vector3(-Chan, Upper, -2.4f), new Vector3(Chan, Upper, -2.4f));
        RailRun(b, new Vector3(-Chan, Upper, 2.4f), new Vector3(Chan, Upper, 2.4f));

        // --- routes between the levels ---
        b.Ramp(new Vector3(-HX + 7f, 0f, -HZ + 6f), new Vector3(-HX + 20f, Upper, -HZ + 11f), 1, MatId.TechFloor);
        b.Ramp(new Vector3(HX - 20f, 0f, HZ - 11f), new Vector3(HX - 7f, Upper, HZ - 6f), 0, MatId.TechFloor);
        b.Lift(new Vector3(HX - 6f, -1.4f, -HZ + 7f), new Vector3(HX - 1.5f, -0.9f, -HZ + 11.5f),
            new Vector3(0f, Upper + 0.9f, 0f), MatId.TechPanelDark, period: 7.5f);
        b.Lift(new Vector3(-HX + 1.5f, -1.4f, HZ - 11.5f), new Vector3(-HX + 6f, -0.9f, HZ - 7f),
            new Vector3(0f, Upper + 0.9f, 0f), MatId.TechPanelDark, period: 7.5f, phase: 3.5f);
        // Out of the channel: a pad on each maintenance ledge throws you back onto its own deck.
        // They have to sit on the ledge, not over the middle — the middle is lava.
        foreach (float pz in new[] { -22f, 22f })
            for (int s = -1; s <= 1; s += 2)
                b.AddJumpPad(new Vector3(s * (Chan - 1.3f), -5.4f, pz), new Vector3(s * 15f, 1.4f, pz),
                    new Vector3(0.45f, 0.9f, 1f));

        // --- four pump rooms, one per corner, off the deck ---
        foreach (var (sx, sz) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
        {
            float rx = sx * (HX - 8f);
            float rz = sz * (HZ - 8f);
            b.Solid(new Vector3(rx - 6f, 0f, rz - 6f), new Vector3(rx + 6f, 0.9f, rz + 6f), MatId.TechPanelDark, true, 0.9f);
            b.Prism(new Vector3(rx, 0.9f, rz), 2.6f, 4.4f, 8, MatId.RustMetal);
            b.Decor(new Vector3(rx - 2.9f, 2.2f, rz - 2.9f), new Vector3(rx + 2.9f, 3.4f, rz + 2.9f), MatId.EnergyPanel, 0.7f);
            b.AddLight(new Vector3(rx, 4f, rz), new Vector3(0.35f, 0.95f, 0.55f), 15f, 3.6f, 3.2f, 0.12f);
            b.Spawn(new Vector3(rx + sx * -4f, 1.1f, rz + sz * -4f), sx < 0 ? 90f : -90f);
        }

        // --- lighting ---
        for (int i = -2; i <= 2; i++)
            b.AddLight(new Vector3(0f, -6.2f, i * 12f), new Vector3(1f, 0.42f, 0.10f), 22f, 5.5f, 1.8f, 0.22f);
        for (int x = -1; x <= 1; x += 2)
            for (int i = -1; i <= 1; i++)
                b.CeilingLamp(new Vector3(x * 22f, CeilY - 1.4f, i * 18f), new Vector3(0.88f, 0.92f, 1f), 30f, 9f, 1.6f);

        // --- placements: shock on the bridge, the belt down where the lava is ---
        b.Weapon(new Vector3(0f, 0.9f, 0f), WeaponKind.ShockRifle);
        b.Ammo(new Vector3(0f, 0.7f, 2.2f), AmmoKind.ShockCore);
        b.Weapon(new Vector3(0f, Upper + 0.9f, 0f), WeaponKind.SniperRifle);
        b.Ammo(new Vector3(2.4f, Upper + 0.7f, 0f), AmmoKind.SniperRounds);
        b.Weapon(new Vector3(-HX + 4f, Upper + 0.9f, 0f), WeaponKind.RocketLauncher);
        b.Ammo(new Vector3(-HX + 4f, Upper + 0.7f, 3f), AmmoKind.Rockets);
        b.Weapon(new Vector3(HX - 4f, Upper + 0.9f, 0f), WeaponKind.Minigun);
        b.Ammo(new Vector3(HX - 4f, Upper + 0.7f, 3f), AmmoKind.MinigunBullets);
        b.Weapon(new Vector3(-20f, 1.0f, -HZ + 4f), WeaponKind.FlakCannon);
        b.Weapon(new Vector3(20f, 1.0f, HZ - 4f), WeaponKind.PulseGun);
        b.Weapon(new Vector3(-20f, 1.0f, HZ - 4f), WeaponKind.BioRifle);
        b.Weapon(new Vector3(20f, 1.0f, -HZ + 4f), WeaponKind.Ripper);
        // Second of each doubled weapon, per the original's list: shock x2, flak x2, rocket x2,
        // sniper x2, and the Redeemer it has always had.
        b.Weapon(new Vector3(0f, 0.9f, -20f), WeaponKind.ShockRifle);
        b.Weapon(new Vector3(20f, 1.0f, 4f), WeaponKind.FlakCannon);
        b.Weapon(new Vector3(-20f, 1.0f, -4f), WeaponKind.RocketLauncher);
        b.Weapon(new Vector3(0f, Upper + 0.9f, 20f), WeaponKind.SniperRifle);
        // Down on the channel ledge, where taking it means standing next to the slime.
        b.Weapon(new Vector3(0f, -5.4f, 20f), WeaponKind.Redeemer, respawn: 100f);

        b.Item(new Vector3(-(Chan - 1.3f), -5.4f, 0f), PickupKind.ShieldBelt);
        b.Item(new Vector3(Chan - 1.3f, -5.4f, 0f), PickupKind.JumpBoots);
        b.Item(new Vector3(0f, Upper + 0.8f, -HZ + 3f), PickupKind.DamageAmp);
        b.Item(new Vector3(0f, Upper + 0.8f, HZ - 3f), PickupKind.ThighPads);
        b.Item(new Vector3(-26f, 0.8f, 0f), PickupKind.BodyArmor);
        for (int i = 0; i < 4; i++)
            b.Item(new Vector3(26f, 0.8f, -12f + i * 8f), PickupKind.HealthPack);
        for (int i = 0; i < 6; i++)
        {
            float t = -25f + i * 10f;
            b.Item(new Vector3(-14f, 0.6f, t), PickupKind.HealthVial);
            b.Item(new Vector3(14f, 0.6f, t), PickupKind.HealthVial);
        }
        b.Spawn(new Vector3(-26f, 0.2f, -12f), 90f);
        b.Spawn(new Vector3(26f, 0.2f, 12f), -90f);
        b.Spawn(new Vector3(-26f, 0.2f, 12f), 90f);
        b.Spawn(new Vector3(26f, 0.2f, -12f), -90f);
        b.Spawn(new Vector3(-HX + 4f, Upper + 0.2f, -16f), 0f);
        b.Spawn(new Vector3(HX - 4f, Upper + 0.2f, 16f), 180f);

        return b.Build(gl);
    }

    // ================================================================ DM-絞碎機

    /// <summary>
    /// A dark machine hall built around a circular pit. The ring floor is the whole map;
    /// the only shortcut across is a narrow beam over the grinder, and the belt sits on it.
    /// </summary>
    private static Level BuildGrinder(GL gl)
    {
        var b = new LevelBuilder(Loc.MapGrinder, Loc.MapGrinderDesc);
        var env = b.Level.Environment;
        env.SunDirection = Vector3.Normalize(new Vector3(-0.10f, -0.98f, -0.15f));
        env.SunColor = new Vector3(0.9f, 0.85f, 0.85f);
        env.AmbientSky = new Vector3(0.26f, 0.25f, 0.28f);
        env.AmbientGround = new Vector3(0.16f, 0.08f, 0.05f);
        env.EnvIntensity = 0.30f;
        env.SkyTop = new Vector3(0.012f, 0.012f, 0.016f);
        env.SkyHorizon = new Vector3(0.06f, 0.05f, 0.05f);
        env.StarStrength = 0f;
        env.CloudStrength = 0.2f;
        env.FogColor = new Vector3(0.09f, 0.07f, 0.07f);
        env.FogSunColor = new Vector3(0.85f, 0.35f, 0.12f);
        env.FogDensity = 0.030f;

        const float H = 24f;
        const float CeilY = 17f;
        const float PitR = 8.5f;
        const float Gallery = 7f;

        b.Room(new Vector3(-H - 2f, -14f, -H - 2f), new Vector3(H + 2f, CeilY, H + 2f), 2f,
            MatId.Concrete, MatId.RustMetal, MatId.TechPanelDark, withCeiling: true, withFloor: false);

        // --- ring floor with the pit punched out of the middle ---
        b.Annulus(Vector3.Zero, -1.4f, 0f, PitR, H * 1.45f, MatId.TechFloor, slabs: 28, collide: true, uvScale: 0.9f);
        b.Annulus(Vector3.Zero, 0f, 0.35f, PitR, PitR + 0.9f, MatId.Trim, slabs: 28, collide: true, uvScale: 0.7f);

        // --- the grinder itself: a drum of teeth turning in lava at the bottom of the shaft ---
        // The bottom is floored wall to wall, not just under the pit mouth. Annulus() rasterises
        // its hole to slab granularity, so the opening runs a little wider than its nominal
        // radius — a floor sized to PitR leaves a slot at the extremes that drops to the void.
        b.Solid(new Vector3(-H - 2f, -14f, -H - 2f), new Vector3(H + 2f, -13f, H + 2f), MatId.Concrete, true, 0.8f);
        b.Lava(new Vector3(-PitR - 2f, -14f, -PitR - 2f), new Vector3(PitR + 2f, -11.4f, PitR + 2f));
        b.Prism(new Vector3(0f, -11.4f, 0f), 3.4f, 4.2f, 10, MatId.RustMetal);
        for (int i = 0; i < 10; i++)
        {
            float a = i / 10f * MathX.TwoPi;
            Vector3 d = new(MathF.Cos(a), 0f, MathF.Sin(a));
            b.Decor(d * 4.4f + new Vector3(-0.7f, -11.2f, -0.7f), d * 4.4f + new Vector3(0.7f, -7.4f, 0.7f),
                MatId.Trim, 0.8f);
        }
        b.AddLight(new Vector3(0f, -9.5f, 0f), new Vector3(1f, 0.38f, 0.08f), 26f, 8f, 2.4f, 0.30f);

        // --- the beam across the pit: a shortcut with no margin ---
        b.Solid(new Vector3(-PitR - 1f, -0.5f, -1.3f), new Vector3(PitR + 1f, 0f, 1.3f), MatId.MetalGrate, true, 1f);
        b.Item(new Vector3(0f, 0.8f, 0f), PickupKind.ShieldBelt);

        // --- upper gallery around the outside, reached by two ramps and a lift ---
        for (int s = -1; s <= 1; s += 2)
        {
            b.Solid(new Vector3(-H, Gallery - 0.5f, s * (H - 5f)), new Vector3(H, Gallery, s * H), MatId.MetalGrate, true, 0.9f);
            b.Solid(new Vector3(s * (H - 5f), Gallery - 0.5f, -H + 5f), new Vector3(s * H, Gallery, H - 5f), MatId.MetalGrate, true, 0.9f);
            RailRun(b, new Vector3(-H + 5f, Gallery, s * (H - 5f)), new Vector3(H - 5f, Gallery, s * (H - 5f)));
            RailRun(b, new Vector3(s * (H - 5f), Gallery, -H + 5f), new Vector3(s * (H - 5f), Gallery, H - 5f));
        }
        b.Ramp(new Vector3(-H + 5f, 0f, -H + 5f), new Vector3(-H + 16f, Gallery, -H + 9f), 1, MatId.Concrete);
        b.Ramp(new Vector3(H - 16f, 0f, H - 9f), new Vector3(H - 5f, Gallery, H - 5f), 0, MatId.Concrete);
        b.Lift(new Vector3(H - 9f, -1.2f, -H + 5.5f), new Vector3(H - 5.4f, -0.7f, -H + 9f),
            new Vector3(0f, Gallery + 0.7f, 0f), MatId.TechPanelDark, period: 6.5f);
        b.Lift(new Vector3(-H + 5.4f, -1.2f, H - 9f), new Vector3(-H + 9f, -0.7f, H - 5.5f),
            new Vector3(0f, Gallery + 0.7f, 0f), MatId.TechPanelDark, period: 6.5f, phase: 3f);

        // --- machinery lining the walls, for cover and for something to look at ---
        var rng = new Rng(0x3C0D);
        for (int i = 0; i < 12; i++)
        {
            float a = i / 12f * MathX.TwoPi + 0.26f;
            float r = H - 3.5f;
            Vector3 p = new(MathF.Cos(a) * r, 0f, MathF.Sin(a) * r);
            float sz = rng.Range(1.5f, 2.4f);
            b.Solid(p - new Vector3(sz, 0f, sz), p + new Vector3(sz, rng.Range(2f, 3.6f), sz),
                rng.Chance(0.5f) ? MatId.RustMetal : MatId.TechPanelDark, true, 1.2f);
            if (rng.Chance(0.5f))
                b.AddLight(p + new Vector3(0f, 3.4f, 0f), new Vector3(0.9f, 0.5f, 0.18f), 12f, 2.4f, 5f, 0.25f);
        }
        for (int i = 0; i < 4; i++)
        {
            float a = i / 4f * MathX.TwoPi + MathX.Pi * 0.25f;
            b.CeilingLamp(new Vector3(MathF.Cos(a) * 15f, CeilY - 1.4f, MathF.Sin(a) * 15f),
                new Vector3(0.85f, 0.8f, 0.72f), 24f, 7f, 1.4f);
        }

        // --- placements ---
        b.Weapon(new Vector3(-16f, 0.9f, 0f), WeaponKind.RocketLauncher);
        b.Ammo(new Vector3(-16f, 0.7f, 2.6f), AmmoKind.Rockets);
        b.Weapon(new Vector3(16f, 0.9f, 0f), WeaponKind.FlakCannon);
        b.Ammo(new Vector3(16f, 0.7f, 2.6f), AmmoKind.FlakShells);
        b.Weapon(new Vector3(0f, 0.9f, -16f), WeaponKind.PulseGun);
        b.Weapon(new Vector3(0f, 0.9f, 16f), WeaponKind.BioRifle);
        b.Weapon(new Vector3(0f, Gallery + 0.9f, -H + 2.5f), WeaponKind.ShockRifle);
        b.Weapon(new Vector3(0f, Gallery + 0.9f, H - 2.5f), WeaponKind.Minigun);
        b.Ammo(new Vector3(3f, Gallery + 0.7f, -H + 2.5f), AmmoKind.ShockCore);
        b.Ammo(new Vector3(3f, Gallery + 0.7f, H - 2.5f), AmmoKind.MinigunBullets);
        // The original's list for this map is short and has no power-ups at all: seven weapons
        // (no sniper rifle — there is nowhere on it worth a long shot), body armour, thigh pads,
        // ten health packs and four vials. An earlier pass had a sniper rifle, an amplifier and
        // a keg, which is a different and much swingier map.
        b.Weapon(new Vector3(12f, 0.9f, -12f), WeaponKind.Ripper);
        b.Item(new Vector3(-H + 2.5f, Gallery + 0.8f, 0f), PickupKind.BodyArmor);
        b.Item(new Vector3(H - 2.5f, Gallery + 0.8f, 0f), PickupKind.ThighPads);
        for (int i = 0; i < 10; i++)
        {
            float a = i / 10f * MathX.TwoPi;
            b.Item(new Vector3(MathF.Cos(a) * 13f, 0.7f, MathF.Sin(a) * 13f), PickupKind.HealthPack);
        }
        for (int i = 0; i < 4; i++)
        {
            float a = i / 4f * MathX.TwoPi + 0.4f;
            b.Item(new Vector3(MathF.Cos(a) * 19f, 0.6f, MathF.Sin(a) * 19f), PickupKind.HealthVial);
        }
        for (int i = 0; i < 6; i++)
        {
            float a = i / 6f * MathX.TwoPi + 0.4f;
            b.Spawn(new Vector3(MathF.Cos(a) * 18f, 0.2f, MathF.Sin(a) * 18f), -a * MathX.Rad2Deg + 90f);
        }
        b.Spawn(new Vector3(-H + 2.5f, Gallery + 0.2f, -10f), 90f);
        b.Spawn(new Vector3(H - 2.5f, Gallery + 0.2f, 10f), -90f);

        return b.Build(gl);
    }

    // ================================================================ DM-利安德里核心

    /// <summary>
    /// The corporate core: a square shaft with a glowing column up the middle and four
    /// staggered galleries climbing it. Each level is missing one side, so the shaft stays
    /// open all the way down — the fastest route up is the jump pads, the fastest route down
    /// is stepping off.
    /// </summary>
    private static Level BuildLiandri(GL gl)
    {
        var b = new LevelBuilder(Loc.MapLiandri, Loc.MapLiandriDesc);
        var env = b.Level.Environment;
        env.SunDirection = Vector3.Normalize(new Vector3(-0.18f, -0.95f, -0.26f));
        env.SunColor = new Vector3(1.7f, 1.8f, 2.1f);
        env.AmbientSky = new Vector3(0.28f, 0.32f, 0.44f);
        env.AmbientGround = new Vector3(0.10f, 0.12f, 0.17f);
        env.EnvIntensity = 0.50f;
        env.SkyTop = new Vector3(0.010f, 0.018f, 0.045f);
        env.SkyHorizon = new Vector3(0.06f, 0.10f, 0.20f);
        env.StarStrength = 0.9f;
        env.CloudStrength = 0.35f;
        env.FogColor = new Vector3(0.07f, 0.09f, 0.14f);
        env.FogSunColor = new Vector3(0.35f, 0.7f, 1f);
        env.FogDensity = 0.014f;

        const float H = 23f;          // half-width of the shaft
        const float Top = 46f;
        const float CoreR = 5f;
        const float Ledge = 6.5f;     // gallery depth
        float[] levels = { 0f, 11f, 21f, 30f, 38f };

        // --- shaft walls, open to the sky at the top ---
        b.Room(new Vector3(-H - 2f, -7f, -H - 2f), new Vector3(H + 2f, Top, H + 2f), 2f,
            MatId.TechFloor, MatId.TechWall, MatId.TechPanelDark, withCeiling: false, withFloor: false);

        // --- the core: a lit column you can never quite hide behind ---
        b.Prism(new Vector3(0f, -7f, 0f), CoreR, Top + 7f, 8, MatId.TechPanelDark);
        for (int i = 0; i < 8; i++)
        {
            float a = i / 8f * MathX.TwoPi;
            Vector3 d = new(MathF.Cos(a), 0f, MathF.Sin(a));
            // Broken into a strip per storey rather than one full-height bar: a continuous
            // emissive column this tall reads as a light fixture, not as a building.
            for (int lv = 0; lv < 5; lv++)
                b.Decor(d * (CoreR + 0.10f) + new Vector3(-0.24f, -3f + lv * 9.5f, -0.24f),
                        d * (CoreR + 0.10f) + new Vector3(0.24f, 3.2f + lv * 9.5f, 0.24f), MatId.EnergyPanel, 0.35f);
        }
        for (int i = 0; i < 5; i++)
            b.AddLight(new Vector3(0f, 0f + i * 10f, 0f), new Vector3(0.35f, 0.72f, 1f), 22f, 3.2f, 2.2f, 0.10f);

        // --- ground floor: solid, wall to wall ---
        // An earlier pass ringed the core with a lava moat and four bridges. The drop from the
        // upper galleries is already the hazard this map is about; a moat on top of it just
        // meant every bot that clipped a bridge corner died to the floor.
        b.Solid(new Vector3(-H, -7f, -H), new Vector3(H, 0f, H), MatId.TechFloor, true, 0.9f);
        b.Annulus(Vector3.Zero, 0f, 0.4f, CoreR, CoreR + 3.2f, MatId.Trim, slabs: 20, collide: true, uvScale: 0.8f);
        for (int i = 0; i < 4; i++)
        {
            float a = i / 4f * MathX.TwoPi + MathX.Pi * 0.25f;
            b.AddLight(new Vector3(MathF.Cos(a) * (H - 4f), 3.5f, MathF.Sin(a) * (H - 4f)),
                new Vector3(0.9f, 0.85f, 0.78f), 20f, 3.6f);
        }

        // --- galleries: each level drops one side, rotating around as you climb ---
        var sides = new (float dx, float dz)[] { (0f, -1f), (1f, 0f), (0f, 1f), (-1f, 0f) };
        for (int lv = 1; lv < levels.Length; lv++)
        {
            float y = levels[lv];
            int missing = (lv - 1) % 4;
            for (int s = 0; s < 4; s++)
            {
                if (s == missing) continue;
                var (dx, dz) = sides[s];
                Vector3 mid = new(dx * (H - Ledge * 0.5f), y, dz * (H - Ledge * 0.5f));
                Vector3 half = new(dx != 0f ? Ledge * 0.5f : H, 0.5f, dz != 0f ? Ledge * 0.5f : H);
                b.Solid(mid - half, mid + new Vector3(half.X, 0f, half.Z), MatId.MetalGrate, true, 0.9f);
                // Rail on the shaft-facing edge only.
                Vector3 inner = new(dx * (H - Ledge), y, dz * (H - Ledge));
                Vector3 ra = inner - new Vector3(dx != 0f ? 0f : H, 0f, dz != 0f ? 0f : H);
                Vector3 rc = inner + new Vector3(dx != 0f ? 0f : H, 0f, dz != 0f ? 0f : H);
                RailRun(b, ra, rc);
            }
            for (int i = 0; i < 4; i++)
            {
                float a = i / 4f * MathX.TwoPi + MathX.Pi * 0.25f;
                b.AddLight(new Vector3(MathF.Cos(a) * (H - 3f), y + 3.5f, MathF.Sin(a) * (H - 3f)),
                    new Vector3(0.9f, 0.85f, 0.75f), 18f, 4f);
            }
        }

        // --- jump pads climbing the tower, each landing on the next gallery up ---
        for (int lv = 0; lv < levels.Length - 1; lv++)
        {
            int missing = lv % 4;                       // the side that is open on level lv+1
            int from = (missing + 2) % 4;               // launch from the far side
            int to = (missing + 1) % 4;                 // land on a side that exists
            var (fx, fz) = sides[from];
            var (tx, tz) = sides[to];
            b.AddJumpPad(new Vector3(fx * (H - Ledge * 0.5f), levels[lv] + 0.1f, fz * (H - Ledge * 0.5f)),
                new Vector3(tx * (H - Ledge * 0.5f), levels[lv + 1] + 1.4f, tz * (H - Ledge * 0.5f)),
                new Vector3(0.4f, 0.85f, 1f));
        }

        // --- placements: the prize is at the top, and the climb is the cost ---
        b.Weapon(new Vector3(0f, 0.9f, -H + 3f), WeaponKind.FlakCannon);
        b.Weapon(new Vector3(0f, 0.9f, H - 3f), WeaponKind.BioRifle);
        b.Weapon(new Vector3(-H + 3f, 0.9f, 0f), WeaponKind.PulseGun);
        // No Ripper on this one — the original's list has eight entries and that is not among
        // them. It does carry a second rocket launcher instead.
        b.Weapon(new Vector3(H - 3f, 0.9f, 0f), WeaponKind.RocketLauncher);
        b.Ammo(new Vector3(3f, 0.7f, -H + 3f), AmmoKind.FlakShells);
        b.Weapon(new Vector3(H - Ledge * 0.5f, levels[1] + 0.9f, 0f), WeaponKind.RocketLauncher);
        b.Ammo(new Vector3(H - Ledge * 0.5f, levels[1] + 0.7f, 3f), AmmoKind.Rockets);
        b.Weapon(new Vector3(0f, levels[2] + 0.9f, H - Ledge * 0.5f), WeaponKind.Minigun);
        b.Weapon(new Vector3(-H + Ledge * 0.5f, levels[3] + 0.9f, 0f), WeaponKind.ShockRifle);
        b.Weapon(new Vector3(0f, levels[3] + 0.9f, -H + Ledge * 0.5f), WeaponKind.SniperRifle);
        b.Ammo(new Vector3(3f, levels[3] + 0.7f, -H + Ledge * 0.5f), AmmoKind.SniperRounds);
        b.Weapon(new Vector3(H - Ledge * 0.5f, levels[4] + 0.9f, 0f), WeaponKind.Redeemer, respawn: 55f);
        // Belt, body armour and one amplifier; twelve vials and six packs. No keg, no boots.
        b.Item(new Vector3(0f, levels[4] + 0.8f, -H + Ledge * 0.5f), PickupKind.ShieldBelt);
        b.Item(new Vector3(0f, levels[2] + 0.8f, -H + Ledge * 0.5f), PickupKind.DamageAmp);
        for (int i = 0; i < 6; i++)
        {
            float a = i / 6f * MathX.TwoPi + 0.3f;
            b.Item(new Vector3(MathF.Cos(a) * 16f, 0.7f, MathF.Sin(a) * 16f), PickupKind.HealthPack);
        }
        b.Item(new Vector3(-H + Ledge * 0.5f, levels[1] + 0.8f, 0f), PickupKind.BodyArmor);
        b.Item(new Vector3(0f, 0.8f, 0f), PickupKind.SuperHealth);
        b.Item(new Vector3(-H + Ledge * 0.5f, levels[2] + 0.8f, 0f), PickupKind.JumpBoots);
        for (int i = 0; i < 4; i++)
        {
            float a = i / 4f * MathX.TwoPi + MathX.Pi * 0.25f;
            b.Item(new Vector3(MathF.Cos(a) * 12f, 0.6f, MathF.Sin(a) * 12f), PickupKind.HealthVial);
        }
        for (int i = 0; i < 4; i++)
        {
            var (dx, dz) = sides[i];
            b.Spawn(new Vector3(dx * (H - 3f), 0.2f, dz * (H - 3f)), MathF.Atan2(-dx, -dz) * MathX.Rad2Deg);
        }
        b.Spawn(new Vector3(H - Ledge * 0.5f, levels[1] + 0.2f, -8f), 180f);
        b.Spawn(new Vector3(0f, levels[2] + 0.2f, H - Ledge * 0.5f), 180f);
        b.Spawn(new Vector3(-H + Ledge * 0.5f, levels[3] + 0.2f, 8f), 0f);

        return b.Build(gl);
    }

    // ================================================================ DM-孤峰

    /// <summary>
    /// Daylight and thin air: a walled summit courtyard with a ruined shrine on a raised
    /// platform and four corner terraces looking down on it.
    ///
    /// This started as an exposed mountaintop over open void — stacked rock discs, stone
    /// bridges to outlying spurs, the lot. It was unplayable. The nav graph only links
    /// neighbours within a step height, so the 40° ramps that made it read as a mountain
    /// carried no bot routes at all, and everything that did move eventually walked off the
    /// edge; a full match ended on a negative score with the whole field dead in the void.
    /// The summit is enclosed now and every climb is a shallow ramp the graph can follow.
    /// The height still reads — it comes from the sky, the cloud and the fog, not from a drop
    /// nobody survives.
    /// </summary>
    private static Level BuildPeak(GL gl)
    {
        var b = new LevelBuilder(Loc.MapPeak, Loc.MapPeakDesc);
        var env = b.Level.Environment;
        env.SunDirection = Vector3.Normalize(new Vector3(-0.42f, -0.72f, -0.55f));
        // Strong sun against a modest ambient. Lifting the ambient to match the sun just turns
        // pale rock into a flat white card with no shape in it.
        env.SunColor = new Vector3(3.6f, 3.35f, 2.95f);
        env.AmbientSky = new Vector3(0.24f, 0.29f, 0.42f);
        env.AmbientGround = new Vector3(0.13f, 0.13f, 0.12f);
        env.EnvIntensity = 0.55f;
        env.SkyTop = new Vector3(0.10f, 0.26f, 0.60f);
        env.SkyHorizon = new Vector3(0.62f, 0.74f, 0.88f);
        env.SkyGround = new Vector3(0.50f, 0.55f, 0.62f);
        env.StarStrength = 0f;
        env.CloudStrength = 1.15f;
        env.FogColor = new Vector3(0.62f, 0.71f, 0.85f);
        env.FogDensity = 0.005f;

        const float HX = 36f, HZ = 30f;
        const float RimTop = 17f;
        const float Shrine = 4f;      // raised shrine platform
        const float ShrineHalf = 11f;
        const float Corner = 2.5f;    // corner terrace height

        // --- courtyard floor and the rock rim that encloses it, open to the sky ---
        b.Solid(new Vector3(-HX, -2f, -HZ), new Vector3(HX, 0f, HZ), MatId.Rock, true, 0.65f);
        b.Room(new Vector3(-HX - 3f, -2f, -HZ - 3f), new Vector3(HX + 3f, RimTop, HZ + 3f), 3f,
            MatId.Rock, MatId.Rock, MatId.Rock, withCeiling: false, withFloor: false);
        // Broken crenellations along the rim, so the wall reads as weathered rock.
        var rng = new Rng(0x5E4A);
        for (int i = 0; i < 26; i++)
        {
            float t = -1f + i / 12.5f;
            float w = rng.Range(1.4f, 3f);
            b.Decor(new Vector3(t * HX - w, RimTop, -HZ - 3f),
                    new Vector3(t * HX + w, RimTop + rng.Range(1.2f, 3.4f), -HZ), MatId.Rock, 0.65f);
            b.Decor(new Vector3(t * HX - w, RimTop, HZ),
                    new Vector3(t * HX + w, RimTop + rng.Range(1.2f, 3.4f), HZ + 3f), MatId.Rock, 0.65f);
        }

        // --- the shrine platform, with a shallow ramp up each face ---
        // Slope matters more than looks here: the nav graph links neighbours 2m apart only if
        // they differ by less than a step, so anything above about 1-in-3 carries no bot route.
        // Each of these runs 15m for 4m of rise.
        b.Solid(new Vector3(-ShrineHalf, 0f, -ShrineHalf), new Vector3(ShrineHalf, Shrine, ShrineHalf),
            MatId.Concrete, true, 0.75f);
        b.Ramp(new Vector3(-5f, 0f, -ShrineHalf - 15f), new Vector3(5f, Shrine, -ShrineHalf), 2, MatId.Rock, true, 0.7f);
        b.Ramp(new Vector3(-5f, 0f, ShrineHalf), new Vector3(5f, Shrine, ShrineHalf + 15f), 3, MatId.Rock, true, 0.7f);
        b.Ramp(new Vector3(-ShrineHalf - 15f, 0f, -5f), new Vector3(-ShrineHalf, Shrine, 5f), 0, MatId.Rock, true, 0.7f);
        b.Ramp(new Vector3(ShrineHalf, 0f, -5f), new Vector3(ShrineHalf + 15f, Shrine, 5f), 1, MatId.Rock, true, 0.7f);

        // --- the ruin standing on it ---
        foreach (var (px, pz) in new[] { (-8.4f, -8.4f), (8.4f, -8.4f), (-8.4f, 8.4f), (8.4f, 8.4f) })
        {
            b.Prism(new Vector3(px, Shrine, pz), 1.2f, 7.5f, 8, MatId.Concrete);
            b.Decor(new Vector3(px - 1.6f, Shrine + 7.5f, pz - 1.6f), new Vector3(px + 1.6f, Shrine + 8.3f, pz + 1.6f),
                MatId.Trim, 0.9f);
        }
        foreach (var (dx, dz) in new[] { (1f, 0f), (0f, 1f) })
        {
            b.Decor(new Vector3(dx != 0f ? -10f : -9.6f, Shrine + 8.3f, dz != 0f ? -10f : -9.6f),
                    new Vector3(dx != 0f ? 10f : -8.2f, Shrine + 9.1f, dz != 0f ? 10f : -8.2f), MatId.Concrete, 0.7f);
            b.Decor(new Vector3(dx != 0f ? -10f : 8.2f, Shrine + 8.3f, dz != 0f ? -10f : 8.2f),
                    new Vector3(dx != 0f ? 10f : 9.6f, Shrine + 9.1f, dz != 0f ? 10f : 9.6f), MatId.Concrete, 0.7f);
        }
        // Altar: one shallow step, so it is cover you can actually get on top of.
        b.Solid(new Vector3(-3f, Shrine, -3f), new Vector3(3f, Shrine + 0.5f, 3f), MatId.Concrete, true, 0.8f);
        b.Decor(new Vector3(-2.6f, Shrine + 0.5f, -2.6f), new Vector3(2.6f, Shrine + 0.72f, 2.6f), MatId.EnergyPanel, 0.6f);
        b.AddLight(new Vector3(0f, Shrine + 3f, 0f), new Vector3(1f, 0.82f, 0.45f), 22f, 4.5f, 1.6f, 0.10f);

        // --- four corner terraces, each with its own shallow ramp ---
        foreach (var (sx, sz) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
        {
            float x0 = sx < 0 ? -HX : HX - 15f, x1 = sx < 0 ? -HX + 15f : HX;
            float z0 = sz < 0 ? -HZ : HZ - 12f, z1 = sz < 0 ? -HZ + 12f : HZ;
            b.Solid(new Vector3(x0, 0f, z0), new Vector3(x1, Corner, z1), MatId.Rock, true, 0.7f);
            RailRun(b, new Vector3(x0, Corner, sz < 0 ? z1 : z0), new Vector3(x1, Corner, sz < 0 ? z1 : z0), 0.85f);
            // 10m of run for 2.5m of rise.
            float rx0 = sx < 0 ? x1 : x1 - 25f, rx1 = sx < 0 ? x1 + 10f : x1 - 15f;
            b.Ramp(new Vector3(rx0, 0f, sz < 0 ? z1 - 6f : z0), new Vector3(rx1, Corner, sz < 0 ? z1 : z0 + 6f),
                sx < 0 ? 1 : 0, MatId.Rock, true, 0.7f);
            b.AddLight(new Vector3((x0 + x1) * 0.5f, Corner + 4f, (z0 + z1) * 0.5f), new Vector3(0.85f, 0.88f, 1f), 20f, 2.6f);
        }

        // --- boulders for cover on the open floor ---
        for (int i = 0; i < 16; i++)
        {
            float px = rng.Range(-HX + 4f, HX - 4f);
            float pz = rng.Range(-HZ + 4f, HZ - 4f);
            if (MathF.Abs(px) < ShrineHalf + 4f && MathF.Abs(pz) < ShrineHalf + 4f) continue;
            if (MathF.Abs(px) > HX - 16f && MathF.Abs(pz) > HZ - 13f) continue;
            float sz2 = rng.Range(1.2f, 2.2f);
            b.Solid(new Vector3(px - sz2, 0f, pz - sz2), new Vector3(px + sz2, rng.Range(1.4f, 2.8f), pz + sz2),
                MatId.Rock, true, 0.8f);
        }

        // --- placements ---
        b.Weapon(new Vector3(0f, Shrine + 1.4f, 0f), WeaponKind.FlakCannon);
        b.Ammo(new Vector3(0f, Shrine + 0.7f, 4.5f), AmmoKind.FlakShells);
        b.Item(new Vector3(0f, Shrine + 1.3f, -4.5f), PickupKind.ShieldBelt);
        // Four flak cannons and no sniper rifle: this map is all short sightlines and drops,
        // and the original arms it accordingly.
        b.Weapon(new Vector3(-HX + 7f, Corner + 0.9f, -HZ + 6f), WeaponKind.FlakCannon);
        b.Weapon(new Vector3(0f, 0.9f, -18f), WeaponKind.FlakCannon);
        b.Weapon(new Vector3(0f, 0.9f, 18f), WeaponKind.FlakCannon);
        b.Ammo(new Vector3(-HX + 10f, Corner + 0.7f, -HZ + 6f), AmmoKind.FlakShells);
        b.Weapon(new Vector3(HX - 7f, Corner + 0.9f, HZ - 6f), WeaponKind.RocketLauncher);
        b.Ammo(new Vector3(HX - 10f, Corner + 0.7f, HZ - 6f), AmmoKind.Rockets);
        b.Weapon(new Vector3(HX - 7f, Corner + 0.9f, -HZ + 6f), WeaponKind.Minigun);
        b.Weapon(new Vector3(-HX + 7f, Corner + 0.9f, HZ - 6f), WeaponKind.PulseGun);
        b.Item(new Vector3(-HX + 7f, Corner + 0.8f, -HZ + 9f), PickupKind.DamageAmp);
        b.Item(new Vector3(HX - 7f, Corner + 0.8f, HZ - 9f), PickupKind.BodyArmor);
        b.Weapon(new Vector3(0f, 0.9f, -HZ + 5f), WeaponKind.ShockRifle);
        b.Weapon(new Vector3(0f, 0.9f, HZ - 5f), WeaponKind.BioRifle);
        b.Weapon(new Vector3(-HX + 5f, 0.9f, 0f), WeaponKind.Ripper);
        b.Item(new Vector3(HX - 5f, 0.8f, 0f), PickupKind.SuperHealth);
        b.Item(new Vector3(-18f, 0.8f, 0f), PickupKind.JumpBoots);
        b.Item(new Vector3(18f, 0.8f, 0f), PickupKind.ThighPads);
        for (int i = 0; i < 8; i++)
        {
            float a = i / 8f * MathX.TwoPi;
            b.Item(new Vector3(MathF.Cos(a) * 19f, 0.6f, MathF.Sin(a) * 16f), PickupKind.HealthVial);
        }

        b.Spawn(new Vector3(0f, Shrine + 0.2f, 6f), 180f);
        b.Spawn(new Vector3(0f, Shrine + 0.2f, -6f), 0f);
        foreach (var (sx, sz) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
            b.Spawn(new Vector3(sx * (HX - 9f), Corner + 0.2f, sz * (HZ - 6f)), sx < 0 ? 90f : -90f);
        b.Spawn(new Vector3(0f, 0.2f, -HZ + 6f), 0f);
        b.Spawn(new Vector3(0f, 0.2f, HZ - 6f), 180f);
        b.Spawn(new Vector3(-HX + 6f, 0.2f, 0f), 90f);
        b.Spawn(new Vector3(HX - 6f, 0.2f, 0f), -90f);

        return b.Build(gl);
    }

    // ================================================================ DM-莫比亞斯

    /// <summary>
    /// An octagonal dome of two floors, pillars on both for cover, and a corridor at the north
    /// and south ends. The lifts in those corridors are the only way between the floors.
    ///
    /// The armoury is four rocket launchers and a Redeemer, and that is the entire pickup list —
    /// no armour, no health, nothing. An earlier version was one flat room with a rocket
    /// launcher, a flak cannon and a scattering of health, which is a different map wearing the
    /// name: half the geometry and none of the reason it is remembered.
    /// </summary>
    private static Level BuildMorbias(GL gl)
    {
        var b = new LevelBuilder(Loc.MapMorbias, Loc.MapMorbiasDesc);
        var env = b.Level.Environment;
        env.SunDirection = Vector3.Normalize(new Vector3(-0.05f, -0.99f, -0.10f));
        env.SunColor = new Vector3(2.6f, 2.4f, 2.2f);
        env.AmbientSky = new Vector3(0.34f, 0.32f, 0.38f);
        env.AmbientGround = new Vector3(0.14f, 0.12f, 0.13f);
        env.EnvIntensity = 0.42f;
        env.SkyTop = new Vector3(0.03f, 0.035f, 0.06f);
        env.SkyHorizon = new Vector3(0.14f, 0.11f, 0.13f);
        env.StarStrength = 0.7f;
        env.CloudStrength = 0.3f;
        env.FogColor = new Vector3(0.10f, 0.09f, 0.11f);
        env.FogDensity = 0.020f;

        const float OuterR = 18f;
        const float Upper = 9f;          // second floor
        const float DomeTop = 20f;
        const float CorridorHalfX = 5f;
        const float CorridorEnd = 30f;   // north/south corridors run out to here

        // --- shell: floor, octagonal wall, dome ---
        b.Annulus(Vector3.Zero, -2.4f, 0f, 0f, OuterR + 3f, MatId.TechFloor, slabs: 30, collide: true, uvScale: 0.8f);
        b.Annulus(Vector3.Zero, 0f, DomeTop, OuterR, OuterR + 3f, MatId.Concrete, slabs: 30, collide: true, uvScale: 0.9f);
        b.Annulus(Vector3.Zero, DomeTop, DomeTop + 1.6f, 0f, OuterR + 3f, MatId.TechPanelDark,
            slabs: 30, collide: true, uvScale: 0.9f);

        // --- second floor: a gallery ring, open over the middle so both levels fight each other ---
        b.Annulus(Vector3.Zero, Upper - 0.6f, Upper, 8.5f, OuterR, MatId.MetalGrate,
            slabs: 28, collide: true, uvScale: 0.9f);
        RingPosts(b, Upper, 8.9f, 16, 0.95f);

        // --- pillars on both floors, as the original has ---
        for (int i = 0; i < 4; i++)
        {
            float a = i / 4f * MathX.TwoPi + MathX.Pi * 0.25f;
            Vector3 d = new(MathF.Cos(a), 0f, MathF.Sin(a));
            b.Prism(d * 12.5f, 1.5f, Upper - 0.6f, 8, MatId.TechPanelDark);
            b.Prism(d * 13.5f + new Vector3(0f, Upper, 0f), 1.4f, DomeTop - Upper, 8, MatId.TechPanelDark);
            b.Decor(d * 12.5f + new Vector3(-0.28f, 2.2f, -0.28f),
                    d * 12.5f + new Vector3(0.28f, 6.6f, 0.28f), MatId.EnergyPanel, 0.45f);
            b.AddLight(d * 12.5f + new Vector3(0f, 7.2f, 0f), new Vector3(0.9f, 0.62f, 0.35f), 17f, 3.0f);
        }

        // --- north and south corridors, each with the lift that is the only way upstairs ---
        foreach (int s in new[] { -1, 1 })
        {
            float zi = s * (OuterR - 1f), zo = s * CorridorEnd;
            float z0 = MathF.Min(zi, zo), z1 = MathF.Max(zi, zo);
            b.Solid(new Vector3(-CorridorHalfX, -2.4f, z0), new Vector3(CorridorHalfX, 0f, z1), MatId.TechFloor, true, 0.8f);
            b.Solid(new Vector3(-CorridorHalfX - 1.6f, 0f, z0), new Vector3(-CorridorHalfX, DomeTop, z1), MatId.Concrete, true, 0.9f);
            b.Solid(new Vector3(CorridorHalfX, 0f, z0), new Vector3(CorridorHalfX + 1.6f, DomeTop, z1), MatId.Concrete, true, 0.9f);
            b.Solid(new Vector3(-CorridorHalfX - 1.6f, 0f, s * CorridorEnd), new Vector3(CorridorHalfX + 1.6f, DomeTop, s * (CorridorEnd + 1.6f)),
                MatId.Concrete, true, 0.9f);
            b.Solid(new Vector3(-CorridorHalfX - 1.6f, DomeTop, z0), new Vector3(CorridorHalfX + 1.6f, DomeTop + 1.6f, z1),
                MatId.TechPanelDark, true, 0.9f);
            // Upper landing joining the corridor to the gallery ring.
            b.Solid(new Vector3(-CorridorHalfX, Upper - 0.6f, s * (OuterR - 4f)), new Vector3(CorridorHalfX, Upper, s * (CorridorEnd - 4f)),
                MatId.MetalGrate, true, 0.9f);
            b.Lift(new Vector3(-3f, -0.2f, s * (CorridorEnd - 6f) - 3f), new Vector3(3f, 0.3f, s * (CorridorEnd - 6f) + 3f),
                new Vector3(0f, Upper + 0.3f, 0f), MatId.TechPanelDark, period: 6f, phase: s < 0 ? 0f : 3f);
            b.CeilingLamp(new Vector3(0f, DomeTop - 1.4f, s * (CorridorEnd - 8f)), new Vector3(0.9f, 0.85f, 0.75f), 22f, 6f, 1.2f);
        }

        for (int i = 0; i < 8; i++)
        {
            float a = i / 8f * MathX.TwoPi + MathX.Pi / 8f;
            Vector3 d = new(MathF.Cos(a), 0f, MathF.Sin(a));
            b.Decor(d * (OuterR - 0.2f) + new Vector3(-0.3f, 12f, -0.3f),
                    d * (OuterR - 0.2f) + new Vector3(0.3f, 14.4f, 0.3f), MatId.EnergyPanel, 0.5f);
            b.AddLight(d * (OuterR - 2f) + new Vector3(0f, 13f, 0f), new Vector3(0.8f, 0.85f, 1f), 16f, 2.6f);
        }
        b.AddLight(new Vector3(0f, 6f, 0f), new Vector3(0.95f, 0.7f, 0.4f), 30f, 5f);

        // --- the whole armoury: four rocket launchers and a Redeemer. Nothing else, by design. ---
        b.Weapon(new Vector3(0f, 0.9f, -12f), WeaponKind.RocketLauncher);
        b.Weapon(new Vector3(0f, 0.9f, 12f), WeaponKind.RocketLauncher);
        b.Weapon(new Vector3(0f, Upper + 0.9f, -13f), WeaponKind.RocketLauncher);
        b.Weapon(new Vector3(0f, Upper + 0.9f, 13f), WeaponKind.RocketLauncher);
        b.Weapon(new Vector3(0f, Upper + 0.9f, 0f), WeaponKind.Redeemer, respawn: 90f);
        // The Redeemer platform: without it the middle of the gallery ring is open air.
        b.Annulus(Vector3.Zero, Upper - 0.6f, Upper, 0f, 3.4f, MatId.Trim, slabs: 14, collide: true, uvScale: 0.8f);
        foreach (int s in new[] { -1, 1 })
            b.AddJumpPad(new Vector3(0f, Upper + 0.1f, s * 6.5f), new Vector3(0f, Upper + 2.2f, 0f),
                new Vector3(0.45f, 0.85f, 1f));
        for (int i = 0; i < 11; i++)
            b.Ammo(new Vector3(MathF.Cos(i / 11f * MathX.TwoPi) * 15f, 0.7f,
                               MathF.Sin(i / 11f * MathX.TwoPi) * 15f), AmmoKind.Rockets);

        for (int i = 0; i < 6; i++)
        {
            float a = i / 6f * MathX.TwoPi;
            b.Spawn(new Vector3(MathF.Cos(a) * 15f, 0.2f, MathF.Sin(a) * 15f), -a * MathX.Rad2Deg + 90f);
        }
        for (int i = 0; i < 4; i++)
        {
            float a = i / 4f * MathX.TwoPi + 0.6f;
            b.Spawn(new Vector3(MathF.Cos(a) * 14f, Upper + 0.2f, MathF.Sin(a) * 14f), -a * MathX.Rad2Deg + 90f);
        }

        return b.Build(gl);
    }

    // ================================================================ CTF-科瑞特設施

    /// <summary>
    /// Tight indoor flag map. Each base feeds a high route and a low route into one central
    /// hall, so a runner always has a choice and a defender always has to guess.
    /// </summary>
    private static Level BuildCoret(GL gl)
    {
        var b = new LevelBuilder(Loc.MapCoret, Loc.MapCoretDesc);
        var env = b.Level.Environment;
        env.SunDirection = Vector3.Normalize(new Vector3(-0.22f, -0.94f, -0.26f));
        env.SunColor = new Vector3(1.6f, 1.7f, 1.8f);
        env.AmbientSky = new Vector3(0.30f, 0.34f, 0.40f);
        env.AmbientGround = new Vector3(0.12f, 0.14f, 0.16f);
        env.EnvIntensity = 0.45f;
        env.SkyTop = new Vector3(0.015f, 0.030f, 0.045f);
        env.SkyHorizon = new Vector3(0.07f, 0.12f, 0.14f);
        env.StarStrength = 0.3f;
        env.CloudStrength = 0.3f;
        env.FogColor = new Vector3(0.08f, 0.11f, 0.13f);
        env.FogDensity = 0.020f;

        const float HX = 22f;
        const float BaseZ = 44f;      // centre of each flag room
        const float EndZ = 56f;
        const float CeilY = 17f;
        const float Upper = 7.5f;
        const float LaneX = 14f;      // outer lane centre

        b.Room(new Vector3(-HX - 2f, -2f, -EndZ - 2f), new Vector3(HX + 2f, CeilY, EndZ + 2f), 2f,
            MatId.TechFloor, MatId.TechWall, MatId.TechPanelDark, withCeiling: true, withFloor: true);

        // --- central hall: open, two storeys, the place both routes converge ---
        b.Solid(new Vector3(-HX, -0.6f, -14f), new Vector3(HX, 0f, 14f), MatId.TechFloor, true, 0.9f);
        b.Prism(new Vector3(0f, 0f, 0f), 4.2f, 5.5f, 8, MatId.TechPanelDark);
        // A band around the pillar rather than a lid over it — a slab this wide reads as a lamp
        // the size of the room.
        for (int i = 0; i < 8; i++)
        {
            float a = i / 8f * MathX.TwoPi;
            Vector3 d = new(MathF.Cos(a), 0f, MathF.Sin(a));
            b.Decor(d * 4.3f + new Vector3(-0.5f, 3.9f, -0.5f), d * 4.3f + new Vector3(0.5f, 5.2f, 0.5f),
                MatId.EnergyPanel, 0.6f);
        }
        b.AddLight(new Vector3(0f, 7f, 0f), new Vector3(0.5f, 0.85f, 1f), 24f, 4.5f, 1.6f, 0.10f);
        // Upper balcony ring in the hall, reached from the high routes.
        for (int s = -1; s <= 1; s += 2)
        {
            b.Solid(new Vector3(-HX, Upper - 0.5f, s * 9f), new Vector3(HX, Upper, s * 14f), MatId.MetalGrate, true, 0.9f);
            RailRun(b, new Vector3(-HX, Upper, s * 9f), new Vector3(HX, Upper, s * 9f));
        }
        b.Solid(new Vector3(-HX, Upper - 0.5f, -9f), new Vector3(-HX + 5f, Upper, 9f), MatId.MetalGrate, true, 0.9f);
        b.Solid(new Vector3(HX - 5f, Upper - 0.5f, -9f), new Vector3(HX, Upper, 9f), MatId.MetalGrate, true, 0.9f);
        RailRun(b, new Vector3(-HX + 5f, Upper, -9f), new Vector3(-HX + 5f, Upper, 9f));
        RailRun(b, new Vector3(HX - 5f, Upper, -9f), new Vector3(HX - 5f, Upper, 9f));
        b.Ramp(new Vector3(-8f, 0f, -14f), new Vector3(-2f, Upper, -6f), 3, MatId.TechFloor);
        b.Ramp(new Vector3(2f, 0f, 6f), new Vector3(8f, Upper, 14f), 2, MatId.TechFloor);

        for (int t = -1; t <= 1; t += 2)
        {
            Team team = t < 0 ? Team.Red : Team.Blue;
            MatId trim = t < 0 ? MatId.TeamRed : MatId.TeamBlue;
            Vector3 tint = GameTypes.TeamColor(team);
            float sz = t;                        // +1 pushes toward +Z
            float baseZ = sz * BaseZ;

            // --- flag room ---
            b.Solid(new Vector3(-HX, -0.6f, sz * 32f), new Vector3(HX, 0f, sz * EndZ), MatId.TechFloor, true, 0.9f);
            b.Solid(new Vector3(-7f, 0f, baseZ - 6f), new Vector3(7f, 1.4f, baseZ + 6f), trim, true, 0.8f);
            b.Decor(new Vector3(-7.4f, 1.4f, baseZ - 6.4f), new Vector3(7.4f, 1.7f, baseZ + 6.4f), MatId.EnergyPanel, 0.6f);
            b.AddFlagBase(new Vector3(0f, 1.5f, baseZ), team, sz < 0f ? 0f : 180f);
            b.AddLight(new Vector3(0f, 5.5f, baseZ), tint * 1.4f, 24f, 6f);
            // The flag rooms had no fixtures of their own and read as black boxes.
            b.CeilingLamp(new Vector3(-11f, CeilY - 1.4f, baseZ), new Vector3(0.9f, 0.92f, 1f), 24f, 6.5f, 1.4f);
            b.CeilingLamp(new Vector3(11f, CeilY - 1.4f, baseZ), new Vector3(0.9f, 0.92f, 1f), 24f, 6.5f, 1.4f);
            b.CeilingLamp(new Vector3(0f, CeilY - 1.4f, sz * (EndZ - 5f)), new Vector3(0.9f, 0.92f, 1f), 22f, 6f, 1.2f);
            // Ramps up onto the flag dais from both sides. Rising axis points at the dais.
            b.Ramp(new Vector3(-11f, 0f, baseZ - 3f), new Vector3(-7f, 1.4f, baseZ + 3f), 0, MatId.TechFloor);
            b.Ramp(new Vector3(7f, 0f, baseZ - 3f), new Vector3(11f, 1.4f, baseZ + 3f), 1, MatId.TechFloor);
            // Back wall alcove with the base armour.
            // Both bases carry the same six weapons, so the map's sixteen entries are two of
            // each plus the four Enforcers. Nothing is exclusive to one side.
            b.Item(new Vector3(0f, 0.8f, sz * (EndZ - 4f)), PickupKind.BodyArmor);
            b.Item(new Vector3(-4f, 0.8f, sz * (EndZ - 4f)), PickupKind.ThighPads);
            b.Weapon(new Vector3(-14f, 0.9f, sz * (EndZ - 5f)), WeaponKind.FlakCannon);
            b.Weapon(new Vector3(14f, 0.9f, sz * (EndZ - 5f)), WeaponKind.Minigun);
            b.Weapon(new Vector3(-18f, 0.9f, sz * (EndZ - 10f)), WeaponKind.Ripper);
            b.Weapon(new Vector3(18f, 0.9f, sz * (EndZ - 10f)), WeaponKind.ShockRifle);
            b.Weapon(new Vector3(-10f, 0.9f, sz * 20f), WeaponKind.RocketLauncher);
            b.Weapon(new Vector3(10f, 0.9f, sz * 20f), WeaponKind.Enforcer);
            b.Weapon(new Vector3(-16f, 0.9f, sz * 26f), WeaponKind.Enforcer);
            for (int h = 0; h < 7; h++)
                b.Item(new Vector3(-12f + h * 4f, 0.7f, sz * (EndZ - 14f)), PickupKind.HealthPack);
            b.Ammo(new Vector3(-14f, 0.7f, sz * (EndZ - 8f)), AmmoKind.FlakShells);
            b.Ammo(new Vector3(14f, 0.7f, sz * (EndZ - 8f)), AmmoKind.MinigunBullets);

            // --- low routes: two side corridors from the base to the hall ---
            for (int lane = -1; lane <= 1; lane += 2)
            {
                float lx = lane * LaneX;
                b.Solid(new Vector3(lx - 5f, -0.6f, sz * 14f), new Vector3(lx + 5f, 0f, sz * 32f), MatId.TechFloor, true, 0.9f);
                // Corridor walls, leaving the ends open.
                b.Solid(new Vector3(lx - lane * 5f, 0f, sz * 14f), new Vector3(lx - lane * 6.4f, CeilY, sz * 32f),
                    MatId.TechWall, true, 0.9f);
                b.CeilingLamp(new Vector3(lx, CeilY - 1.4f, sz * 23f), new Vector3(0.85f, 0.9f, 1f), 22f, 6f, 1.2f);
                b.Item(new Vector3(lx, 0.6f, sz * 23f), PickupKind.HealthVial);
            }

            // --- high route: a catwalk over the middle of the connector ---
            b.Solid(new Vector3(-4.5f, Upper - 0.5f, sz * 9f), new Vector3(4.5f, Upper, sz * 34f), MatId.MetalGrate, true, 0.9f);
            RailRun(b, new Vector3(-4.5f, Upper, sz * 9f), new Vector3(-4.5f, Upper, sz * 34f));
            RailRun(b, new Vector3(4.5f, Upper, sz * 9f), new Vector3(4.5f, Upper, sz * 34f));
            // Rising axis has to point back toward the hall, where the catwalk is.
            b.Ramp(new Vector3(-4.5f, 0f, sz * 34f), new Vector3(4.5f, Upper, sz * 40f), sz < 0f ? 2 : 3, MatId.TechFloor);
            b.Weapon(new Vector3(0f, Upper + 0.9f, sz * 20f), WeaponKind.SniperRifle);
            b.Ammo(new Vector3(0f, Upper + 0.7f, sz * 24f), AmmoKind.SniperRounds);

            // Pad from the flag room onto the high route, for a fast exit. Keep it behind the
            // flag dais on exposed floor: the old ±37 position was buried halfway up the ramp,
            // so bots followed its nav link into solid geometry and stalled there indefinitely.
            b.AddJumpPad(new Vector3(0f, 0.1f, sz * (EndZ - 3f)), new Vector3(0f, Upper + 1.6f, sz * 28f),
                new Vector3(0.4f, 0.85f, 1f));

            for (int i = 0; i < 4; i++)
                b.Spawn(new Vector3(-12f + i * 8f, 0.2f, sz * (EndZ - 9f)), sz < 0f ? 0f : 180f, team);
            b.Spawn(new Vector3(0f, Upper + 0.2f, sz * 30f), sz < 0f ? 0f : 180f, team);
        }

        // --- neutral middle: the contested pickups ---
        b.Weapon(new Vector3(0f, 0.9f, -7f), WeaponKind.SniperRifle);
        b.Weapon(new Vector3(0f, 0.9f, 7f), WeaponKind.SniperRifle);
        b.Ammo(new Vector3(3f, 0.7f, -7f), AmmoKind.Rockets);
        b.Weapon(new Vector3(-18f, 0.9f, 0f), WeaponKind.Enforcer);
        b.Weapon(new Vector3(18f, 0.9f, 0f), WeaponKind.Enforcer);
        // One amplifier, up in the middle's top area, and nothing else out here.
        b.Item(new Vector3(0f, Upper + 0.8f, 0f), PickupKind.DamageAmp);
        for (int i = 0; i < 6; i++)
        {
            b.Item(new Vector3(-16f, 0.6f, -10f + i * 4f), PickupKind.HealthVial);
            b.Item(new Vector3(16f, 0.6f, -10f + i * 4f), PickupKind.HealthVial);
        }
        for (int s = -1; s <= 1; s += 2)
            for (int i = -1; i <= 1; i++)
                b.CeilingLamp(new Vector3(i * 12f, CeilY - 1.4f, s * 11f), new Vector3(0.9f, 0.92f, 1f), 26f, 7f, 1.4f);

        return b.Build(gl);
    }

    // ================================================================ CTF-十一月號

    /// <summary>
    /// A submarine pen. The flag rooms sit at either end of a flooded dock; the sub moored
    /// down the middle is the high road between them, and the water either side is the slow,
    /// safe one. Taking the sub is faster and gets you shot at from both galleries.
    /// </summary>
    private static Level BuildNovember(GL gl)
    {
        var b = new LevelBuilder(Loc.MapNovember, Loc.MapNovemberDesc);
        var env = b.Level.Environment;
        env.SunDirection = Vector3.Normalize(new Vector3(-0.18f, -0.95f, -0.24f));
        env.SunColor = new Vector3(1.3f, 1.45f, 1.5f);
        env.AmbientSky = new Vector3(0.26f, 0.32f, 0.36f);
        env.AmbientGround = new Vector3(0.10f, 0.14f, 0.15f);
        env.EnvIntensity = 0.40f;
        env.SkyTop = new Vector3(0.012f, 0.025f, 0.030f);
        env.SkyHorizon = new Vector3(0.06f, 0.11f, 0.12f);
        env.StarStrength = 0.2f;
        env.CloudStrength = 0.25f;
        env.FogColor = new Vector3(0.07f, 0.11f, 0.12f);
        env.FogDensity = 0.024f;

        const float HX = 30f;
        const float EndZ = 52f;
        const float CeilY = 20f;
        const float Dock = 2.4f;      // dock walkway height
        const float WaterHalf = 10f;  // half-width of the channel
        const float Gallery = 9f;

        b.Room(new Vector3(-HX - 2f, -10f, -EndZ - 2f), new Vector3(HX + 2f, CeilY, EndZ + 2f), 2f,
            MatId.Concrete, MatId.Concrete, MatId.TechPanelDark, withCeiling: true, withFloor: false);

        // --- the flooded channel down the middle ---
        b.Solid(new Vector3(-HX, -10f, -EndZ), new Vector3(HX, -9f, EndZ), MatId.Concrete, true, 0.8f);
        b.Water(new Vector3(-WaterHalf, -9f, -EndZ), new Vector3(WaterHalf, 0f, EndZ));

        // --- dock aprons either side, raised above the waterline ---
        for (int s = -1; s <= 1; s += 2)
        {
            b.Solid(new Vector3(s * WaterHalf, -10f, -EndZ), new Vector3(s * HX, Dock, EndZ), MatId.Concrete, true, 0.8f);
            // Bollards, and a lip at the water's edge so you step off deliberately.
            b.Decor(new Vector3(s * WaterHalf - s * 0.5f, Dock, -EndZ), new Vector3(s * WaterHalf, Dock + 0.4f, EndZ),
                MatId.Trim, 0.8f);
            for (int i = -4; i <= 4; i++)
                b.Decor(new Vector3(s * (WaterHalf + 1.6f) - 0.4f, Dock, i * 11f - 0.4f),
                        new Vector3(s * (WaterHalf + 1.6f) + 0.4f, Dock + 1.1f, i * 11f + 0.4f), MatId.RustMetal, 0.9f);
        }
        // Ladders out of the water: pads at both ends of each side.
        for (int s = -1; s <= 1; s += 2)
            foreach (float pz in new[] { -30f, 0f, 30f })
                b.AddJumpPad(new Vector3(s * (WaterHalf - 2f), -1.4f, pz),
                    new Vector3(s * (WaterHalf + 4f), Dock + 1.5f, pz), new Vector3(0.4f, 0.9f, 0.95f));

        // --- the submarine: a long hull with a conning tower, moored amidships ---
        const float SubHalfZ = 22f, SubHalfX = 6f, Deck = 3.6f;
        b.Solid(new Vector3(-SubHalfX, -6f, -SubHalfZ), new Vector3(SubHalfX, Deck, SubHalfZ), MatId.RustMetal, true, 0.9f);
        // Tapered bow and stern, so it reads as a boat rather than a crate.
        for (int i = 0; i < 4; i++)
        {
            float inset = SubHalfX * (0.78f - i * 0.18f);
            float extra = 2.2f + i * 2.0f;
            b.Solid(new Vector3(-inset, -4f, -SubHalfZ - extra), new Vector3(inset, Deck - 0.5f - i * 0.35f, -SubHalfZ),
                MatId.RustMetal, true, 0.9f);
            b.Solid(new Vector3(-inset, -4f, SubHalfZ), new Vector3(inset, Deck - 0.5f - i * 0.35f, SubHalfZ + extra),
                MatId.RustMetal, true, 0.9f);
        }
        b.Solid(new Vector3(-2.6f, Deck, -5f), new Vector3(2.6f, Deck + 4.2f, 5f), MatId.TechPanelDark, true, 0.9f);
        b.Decor(new Vector3(-3f, Deck + 4.2f, -5.4f), new Vector3(3f, Deck + 4.8f, 5.4f), MatId.Trim, 0.8f);
        b.Decor(new Vector3(-0.25f, Deck + 4.8f, -0.25f), new Vector3(0.25f, Deck + 8f, 0.25f), MatId.Trim, 0.8f);
        b.AddLight(new Vector3(0f, Deck + 8.4f, 0f), new Vector3(1f, 0.45f, 0.35f), 22f, 4.5f, 1.2f, 0.35f);
        // Onto the deck from the dock: two gangways.
        foreach (int s in new[] { -1, 1 })
            b.Solid(new Vector3(MathF.Min(s * SubHalfX, s * (WaterHalf + 0.5f)), Deck - 0.5f, s * 9f - 1.8f),
                    new Vector3(MathF.Max(s * SubHalfX, s * (WaterHalf + 0.5f)), Deck, s * 9f + 1.8f),
                    MatId.MetalGrate, true, 1f);
        // Dock apron up to gangway height. The rise has to be on the gangway side, not the far
        // side, or the ramp climbs away from the thing it is meant to reach.
        b.Ramp(new Vector3(WaterHalf + 0.5f, Dock, 7.2f), new Vector3(WaterHalf + 5.5f, Deck, 10.8f), 1, MatId.Concrete);
        b.Ramp(new Vector3(-WaterHalf - 5.5f, Dock, -10.8f), new Vector3(-WaterHalf - 0.5f, Deck, -7.2f), 0, MatId.Concrete);

        // --- wall galleries running the length of the pen ---
        for (int s = -1; s <= 1; s += 2)
        {
            b.Solid(new Vector3(s * (HX - 6f), Gallery - 0.5f, -EndZ), new Vector3(s * HX, Gallery, EndZ),
                MatId.MetalGrate, true, 0.9f);
            RailRun(b, new Vector3(s * (HX - 6f), Gallery, -EndZ), new Vector3(s * (HX - 6f), Gallery, EndZ));
            b.Ramp(new Vector3(MathF.Min(s * (HX - 6f), s * (HX - 16f)), Dock, -6f),
                   new Vector3(MathF.Max(s * (HX - 6f), s * (HX - 16f)), Gallery, 6f), s < 0 ? 1 : 0, MatId.Concrete);
            for (int i = -2; i <= 2; i++)
                b.CeilingLamp(new Vector3(s * (HX - 3f), CeilY - 1.4f, i * 20f), new Vector3(0.85f, 0.92f, 1f), 26f, 7f, 1.4f);
        }

        // --- bases at either end ---
        for (int t = -1; t <= 1; t += 2)
        {
            Team team = t < 0 ? Team.Red : Team.Blue;
            MatId trim = t < 0 ? MatId.TeamRed : MatId.TeamBlue;
            Vector3 tint = GameTypes.TeamColor(team);
            float sz = t;
            float baseZ = sz * (EndZ - 8f);

            // The end of the pen is decked over: a solid platform bridging both aprons.
            b.Solid(new Vector3(-WaterHalf, -10f, sz * (EndZ - 16f)), new Vector3(WaterHalf, Dock, sz * EndZ),
                MatId.Concrete, true, 0.8f);
            b.Solid(new Vector3(-6f, Dock, baseZ - 4f), new Vector3(6f, Dock + 1.3f, baseZ + 4f), trim, true, 0.8f);
            b.Decor(new Vector3(-6.4f, Dock + 1.3f, baseZ - 4.4f), new Vector3(6.4f, Dock + 1.6f, baseZ + 4.4f),
                MatId.EnergyPanel, 0.6f);
            b.AddFlagBase(new Vector3(0f, Dock + 1.4f, baseZ), team, sz < 0f ? 0f : 180f);
            b.AddLight(new Vector3(0f, Dock + 6f, baseZ), tint * 1.4f, 26f, 6.5f);
            b.Ramp(new Vector3(-10f, Dock, baseZ - 3f), new Vector3(-6f, Dock + 1.3f, baseZ + 3f), 0, MatId.Concrete);
            b.Ramp(new Vector3(6f, Dock, baseZ - 3f), new Vector3(10f, Dock + 1.3f, baseZ + 3f), 1, MatId.Concrete);

            b.Weapon(new Vector3(-16f, Dock + 0.9f, sz * (EndZ - 4f)), WeaponKind.FlakCannon);
            b.Weapon(new Vector3(16f, Dock + 0.9f, sz * (EndZ - 4f)), WeaponKind.Minigun);
            b.Ammo(new Vector3(-16f, Dock + 0.7f, sz * (EndZ - 8f)), AmmoKind.FlakShells);
            b.Ammo(new Vector3(16f, Dock + 0.7f, sz * (EndZ - 8f)), AmmoKind.MinigunBullets);
            b.Item(new Vector3(0f, Dock + 0.8f, sz * (EndZ - 14f)), PickupKind.BodyArmor);
            b.Weapon(new Vector3(0f, Gallery + 0.9f, sz * (EndZ - 6f)), WeaponKind.SniperRifle);

            for (int i = 0; i < 4; i++)
                b.Spawn(new Vector3(-13f + i * 8.7f, Dock + 0.2f, sz * (EndZ - 12f)), sz < 0f ? 0f : 180f, team);
            b.Spawn(new Vector3(-(HX - 3f), Gallery + 0.2f, sz * (EndZ - 12f)), sz < 0f ? 0f : 180f, team);
            b.Spawn(new Vector3(HX - 3f, Gallery + 0.2f, sz * (EndZ - 12f)), sz < 0f ? 0f : 180f, team);
        }

        // --- neutral middle ---
        b.Weapon(new Vector3(0f, Deck + 0.9f, -14f), WeaponKind.RocketLauncher);
        b.Weapon(new Vector3(0f, Deck + 0.9f, 14f), WeaponKind.PulseGun);
        b.Ammo(new Vector3(2.6f, Deck + 0.7f, -14f), AmmoKind.Rockets);
        b.Item(new Vector3(0f, Deck + 4.9f, 0f), PickupKind.ShieldBelt);
        b.Weapon(new Vector3(-(HX - 3f), Gallery + 0.9f, 0f), WeaponKind.ShockRifle);
        b.Weapon(new Vector3(HX - 3f, Gallery + 0.9f, 0f), WeaponKind.Ripper);
        b.Item(new Vector3(-(HX - 3f), Gallery + 0.8f, -18f), PickupKind.DamageAmp);
        b.Item(new Vector3(HX - 3f, Gallery + 0.8f, 18f), PickupKind.SuperHealth);
        b.Weapon(new Vector3(-(WaterHalf + 5f), Dock + 0.9f, 22f), WeaponKind.BioRifle);
        b.Weapon(new Vector3(WaterHalf + 5f, Dock + 0.9f, -22f), WeaponKind.BioRifle);
        for (int i = -3; i <= 3; i++)
        {
            b.Item(new Vector3(-(WaterHalf + 5f), Dock + 0.6f, i * 9f), PickupKind.HealthVial);
            b.Item(new Vector3(WaterHalf + 5f, Dock + 0.6f, i * 9f), PickupKind.HealthVial);
        }

        return b.Build(gl);
    }
}
