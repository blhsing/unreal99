using System.Numerics;
using Silk.NET.OpenGL;
using Unreal99.Core;
using Unreal99.Game;
using Unreal99.Rendering;
using Unreal99.UI;

namespace Unreal99.World;

public static partial class Maps
{
    // ================================================================ CTF-熔岩巨人

    /// <summary>
    /// A rock island adrift in a lava sea, cut in half by a central ridge. Each team holds a
    /// fort at one end; the ridge can be crossed high over the top or low around either flank.
    /// </summary>
    private static Level BuildLavaGiant(GL gl)
    {
        var b = new LevelBuilder(Loc.MapLavaGiant, Loc.MapLavaGiantDesc);
        var env = b.Level.Environment;
        env.SunDirection = Vector3.Normalize(new Vector3(0.30f, -0.72f, -0.62f));
        env.SunColor = new Vector3(4.2f, 3.2f, 2.2f);
        env.AmbientSky = new Vector3(0.30f, 0.24f, 0.24f);
        env.AmbientGround = new Vector3(0.22f, 0.10f, 0.05f);
        env.SkyTop = new Vector3(0.10f, 0.06f, 0.09f);
        env.SkyHorizon = new Vector3(0.62f, 0.24f, 0.08f);
        env.SkyGround = new Vector3(0.30f, 0.09f, 0.02f);
        env.StarStrength = 0f;
        env.CloudStrength = 0.85f;
        env.EnvIntensity = 0.5f;
        env.FogColor = new Vector3(0.30f, 0.13f, 0.06f);
        env.FogSunColor = new Vector3(1.0f, 0.5f, 0.16f);
        env.FogDensity = 0.020f;

        const float HX = 40f;
        const float BaseZ = 52f;
        const float RidgeTop = 13f;

        // --- the lava sea, then the island sitting in it ---
        b.Lava(new Vector3(-HX - 44f, -9f, -BaseZ - 46f), new Vector3(HX + 44f, -5.5f, BaseZ + 46f));
        b.Solid(new Vector3(-HX, -6f, -BaseZ - 20f), new Vector3(HX, 0f, BaseZ + 20f), MatId.Rock, true, 0.45f);

        // --- the central ridge: a wall of rock with a high pass over the top ---
        b.Solid(new Vector3(-HX, 0f, -9f), new Vector3(-24f, RidgeTop, 9f), MatId.Rock, true, 0.5f);
        b.Solid(new Vector3(24f, 0f, -9f), new Vector3(HX, RidgeTop, 9f), MatId.Rock, true, 0.5f);
        b.Solid(new Vector3(-24f, 0f, -9f), new Vector3(24f, RidgeTop - 4f, 9f), MatId.Rock, true, 0.5f);
        // The saddle in the middle of the ridge — the high road between the bases.
        b.Solid(new Vector3(-10f, RidgeTop - 4f, -7f), new Vector3(10f, RidgeTop - 3.4f, 7f),
            MatId.Concrete, true, 0.8f);
        b.Ramp(new Vector3(-10f, 0f, -22f), new Vector3(10f, RidgeTop - 3.4f, -7f), 2, MatId.Rock, true, 0.6f);
        b.Ramp(new Vector3(-10f, 0f, 7f), new Vector3(10f, RidgeTop - 3.4f, 22f), 3, MatId.Rock, true, 0.6f);
        // Flank passes hugging the outer walls.
        b.Solid(new Vector3(-HX, -0.1f, -9f), new Vector3(-30f, 0f, 9f), MatId.Rock, true, 0.5f);
        b.Solid(new Vector3(30f, -0.1f, -9f), new Vector3(HX, 0f, 9f), MatId.Rock, true, 0.5f);
        b.Solid(new Vector3(-30f, 0f, -9f), new Vector3(-24f, RidgeTop, 9f), MatId.Rock, true, 0.5f);
        b.Solid(new Vector3(24f, 0f, -9f), new Vector3(30f, RidgeTop, 9f), MatId.Rock, true, 0.5f);

        for (int side = 0; side < 2; side++)
        {
            float sign = side == 0 ? -1f : 1f;
            Team team = side == 0 ? Team.Red : Team.Blue;
            Vector3 col = GameTypes.TeamColor(team);
            MatId teamMat = team == Team.Red ? MatId.TeamRed : MatId.TeamBlue;
            float z = BaseZ * sign;

            // --- the fort: a walled compound open toward midfield ---
            b.Solid(new Vector3(-22f, 0f, z - 16f * sign), new Vector3(-19f, 12f, z + 16f * sign), teamMat, true, 0.6f);
            b.Solid(new Vector3(19f, 0f, z - 16f * sign), new Vector3(22f, 12f, z + 16f * sign), teamMat, true, 0.6f);
            float back = z + 16f * sign;
            b.Solid(new Vector3(-22f, 0f, MathF.Min(back, back + 3f * sign)),
                    new Vector3(22f, 12f, MathF.Max(back, back + 3f * sign)), teamMat, true, 0.6f);
            b.Solid(new Vector3(-22f, 12f, z - 16f * sign), new Vector3(22f, 13.4f, z + 16f * sign),
                MatId.TechPanelDark, true, 0.7f);

            // Front face with a central gate and two side doors.
            float front = z - 16f * sign;
            float f0 = MathF.Min(front, front - 3f * sign), f1 = MathF.Max(front, front - 3f * sign);
            b.Solid(new Vector3(-22f, 0f, f0), new Vector3(-11f, 12f, f1), teamMat, true, 0.6f);
            b.Solid(new Vector3(11f, 0f, f0), new Vector3(22f, 12f, f1), teamMat, true, 0.6f);
            b.Solid(new Vector3(-11f, 6.5f, f0), new Vector3(11f, 12f, f1), teamMat, true, 0.6f);

            // --- flag dais and the gallery above it ---
            Vector3 flag = new(0f, 1.2f, z + 7f * sign);
            b.Solid(new Vector3(-6f, 0f, flag.Z - 5f), new Vector3(6f, 1.2f, flag.Z + 5f), MatId.TechPanelDark);
            b.Ramp(new Vector3(-4f, 0f, MathF.Min(flag.Z - 5f, flag.Z - 9f)),
                   new Vector3(4f, 1.2f, MathF.Max(flag.Z - 5f, flag.Z - 9f)), sign > 0 ? 2 : 3, MatId.Concrete);
            b.AddFlagBase(flag, team, sign > 0 ? 180f : 0f);
            b.AddLight(new Vector3(0f, 6f, flag.Z), col, 20f, 6f);

            b.Solid(new Vector3(-22f, 6.5f, z - 13f * sign), new Vector3(-13f, 7f, z + 13f * sign),
                MatId.MetalGrate, true, 0.9f);
            b.Solid(new Vector3(13f, 6.5f, z - 13f * sign), new Vector3(22f, 7f, z + 13f * sign),
                MatId.MetalGrate, true, 0.9f);
            b.Stairs(new Vector3(-17.5f, 0f, z - 11f * sign), new Vector3(-17.5f, 7f, z - 2f * sign), 6f, 12,
                MatId.Concrete, alongX: false);
            b.Stairs(new Vector3(17.5f, 0f, z - 11f * sign), new Vector3(17.5f, 7f, z - 2f * sign), 6f, 12,
                MatId.Concrete, alongX: false);

            b.CeilingLamp(new Vector3(0f, 11.5f, z), col * 0.5f + new Vector3(0.5f), 26f, 8f, 1.5f);
            b.CeilingLamp(new Vector3(-14f, 11.5f, z - 6f * sign), new Vector3(0.9f, 0.85f, 0.8f), 22f, 6f);
            b.CeilingLamp(new Vector3(14f, 11.5f, z - 6f * sign), new Vector3(0.9f, 0.85f, 0.8f), 22f, 6f);

            b.Weapon(new Vector3(-17.5f, 7.9f, z), WeaponKind.SniperRifle);
            b.Weapon(new Vector3(17.5f, 7.9f, z), WeaponKind.ShockRifle);
            b.Weapon(new Vector3(-14f, 0.9f, z - 8f * sign), WeaponKind.FlakCannon);
            b.Weapon(new Vector3(14f, 0.9f, z - 8f * sign), WeaponKind.Minigun);
            b.Weapon(new Vector3(0f, 0.9f, z - 12f * sign), WeaponKind.PulseGun);
            b.Item(new Vector3(0f, 2.0f, z + 11f * sign), PickupKind.BodyArmor);
            b.Item(new Vector3(-8f, 0.7f, z - 4f * sign), PickupKind.HealthPack);
            b.Item(new Vector3(8f, 0.7f, z - 4f * sign), PickupKind.HealthPack);
            b.Ammo(new Vector3(-17.5f, 7.7f, z + 4f), AmmoKind.SniperRounds);
            b.Ammo(new Vector3(17.5f, 7.7f, z + 4f), AmmoKind.ShockCore);
            b.Ammo(new Vector3(-12f, 0.7f, z - 8f * sign), AmmoKind.FlakShells);
            b.Ammo(new Vector3(12f, 0.7f, z - 8f * sign), AmmoKind.MinigunBullets);

            for (int i = 0; i < 6; i++)
                b.Spawn(new Vector3(-14f + i * 5.6f, 0.2f, z + 3f * sign), sign > 0 ? 180f : 0f, team);
            b.Spawn(new Vector3(-17.5f, 7.2f, z + 6f * sign), sign > 0 ? 180f : 0f, team);
            b.Spawn(new Vector3(17.5f, 7.2f, z + 6f * sign), sign > 0 ? 180f : 0f, team);

            // Pad from the fort roof line up onto the ridge saddle, for a fast flag run.
            b.AddJumpPad(new Vector3(0f, 0.1f, z - 26f * sign),
                new Vector3(0f, RidgeTop - 1.6f, 0f), new Vector3(1f, 0.5f, 0.15f));
        }

        // --- midfield: scattered rock cover on the no-man's land either side of the ridge ---
        var rng = new Rng(0x1A7A);
        for (int i = 0; i < 12; i++)
        {
            float x = rng.Range(-34f, 34f);
            float z = rng.Range(14f, 34f) * (rng.Chance(0.5f) ? 1f : -1f);
            float r = rng.Range(2f, 4f);
            b.Solid(new Vector3(x - r, 0f, z - r), new Vector3(x + r, r * 1.3f, z + r), MatId.Rock, true, 0.6f);
        }
        b.Weapon(new Vector3(0f, RidgeTop - 2.5f, 0f), WeaponKind.RocketLauncher);
        b.Item(new Vector3(-6f, RidgeTop - 2.6f, 0f), PickupKind.ShieldBelt);
        b.Item(new Vector3(6f, RidgeTop - 2.6f, 0f), PickupKind.DamageAmp);
        b.Weapon(new Vector3(-34f, 0.9f, 0f), WeaponKind.BioRifle);
        b.Weapon(new Vector3(34f, 0.9f, 0f), WeaponKind.Ripper);
        b.Item(new Vector3(-34f, 0.7f, 16f), PickupKind.SuperHealth);
        b.Item(new Vector3(34f, 0.7f, -16f), PickupKind.SuperHealth);
        for (int i = 0; i < 5; i++)
        {
            float x = -28f + i * 14f;
            b.Item(new Vector3(x, 0.6f, -24f), PickupKind.HealthVial);
            b.Item(new Vector3(x, 0.6f, 24f), PickupKind.HealthVial);
        }
        b.Spawn(new Vector3(0f, RidgeTop - 2.9f, 0f), 0f);

        return b.Build(gl);
    }

    // ================================================================ DM-詛咒之庭

    /// <summary>
    /// An upper courtyard crossed by a bridge, a long hall running the length of the level
    /// beneath it, ledges around the edges, and a chamber hidden behind a false wall.
    /// </summary>
    private static Level BuildCurse(GL gl)
    {
        var b = new LevelBuilder(Loc.MapCurse, Loc.MapCurseDesc);
        var env = b.Level.Environment;
        env.SunDirection = Vector3.Normalize(new Vector3(-0.42f, -0.78f, 0.46f));
        env.SunColor = new Vector3(2.4f, 2.1f, 1.8f);
        env.AmbientSky = new Vector3(0.24f, 0.24f, 0.30f);
        env.AmbientGround = new Vector3(0.11f, 0.09f, 0.08f);
        env.SkyTop = new Vector3(0.03f, 0.05f, 0.13f);
        env.SkyHorizon = new Vector3(0.24f, 0.18f, 0.22f);
        env.StarStrength = 1.0f;
        env.CloudStrength = 0.6f;
        env.EnvIntensity = 0.42f;
        env.FogColor = new Vector3(0.12f, 0.11f, 0.13f);
        env.FogDensity = 0.020f;

        const float HX = 34f, HZ = 24f;
        const float Upper = 9f;
        const float CeilY = 21f;

        // --- lower level: one long hall spanning the whole map ---
        b.Solid(new Vector3(-HX, -1.4f, -HZ), new Vector3(HX, 0f, HZ), MatId.Concrete, true, 0.6f);
        b.Solid(new Vector3(-HX - 2f, -2f, -HZ - 2f), new Vector3(-HX, CeilY, HZ + 2f), MatId.Rock, true, 0.5f);
        b.Solid(new Vector3(HX, -2f, -HZ - 2f), new Vector3(HX + 2f, CeilY, HZ + 2f), MatId.Rock, true, 0.5f);
        b.Solid(new Vector3(-HX, -2f, -HZ - 2f), new Vector3(HX, CeilY, -HZ), MatId.Rock, true, 0.5f);
        b.Solid(new Vector3(-HX, -2f, HZ), new Vector3(HX, CeilY, HZ + 2f), MatId.Rock, true, 0.5f);

        // --- upper courtyard: a broad deck over the hall, open down the middle ---
        b.Solid(new Vector3(-HX, Upper - 0.8f, -HZ), new Vector3(HX, Upper, -8f), MatId.Concrete, true, 0.7f);
        b.Solid(new Vector3(-HX, Upper - 0.8f, 8f), new Vector3(HX, Upper, HZ), MatId.Concrete, true, 0.7f);
        b.Solid(new Vector3(-HX, Upper - 0.8f, -8f), new Vector3(-20f, Upper, 8f), MatId.Concrete, true, 0.7f);
        b.Solid(new Vector3(20f, Upper - 0.8f, -8f), new Vector3(HX, Upper, 8f), MatId.Concrete, true, 0.7f);
        RailRun(b, new Vector3(-20f, Upper, -8f), new Vector3(20f, Upper, -8f));
        RailRun(b, new Vector3(-20f, Upper, 8f), new Vector3(20f, Upper, 8f));

        // The bridge across the open middle.
        b.Solid(new Vector3(-4f, Upper - 0.8f, -8f), new Vector3(4f, Upper, 8f), MatId.Trim, true, 0.9f);
        RailRun(b, new Vector3(-4f, Upper, -8f), new Vector3(-4f, Upper, 8f));
        RailRun(b, new Vector3(4f, Upper, -8f), new Vector3(4f, Upper, 8f));

        // --- routes between the levels ---
        b.Stairs(new Vector3(-HX + 4f, 0f, -18f), new Vector3(-HX + 4f, Upper, -8f), 6f, 14, MatId.Concrete, false);
        b.Stairs(new Vector3(HX - 4f, 0f, 18f), new Vector3(HX - 4f, Upper, 8f), 6f, 14, MatId.Concrete, false);
        b.Ramp(new Vector3(-14f, 0f, HZ - 9f), new Vector3(-6f, Upper, HZ - 1f), 3, MatId.Concrete);
        b.Ramp(new Vector3(6f, 0f, -HZ + 1f), new Vector3(14f, Upper, -HZ + 9f), 2, MatId.Concrete);
        b.Lift(new Vector3(24f, 0.2f, -3f), new Vector3(28f, 0.6f, 3f), new Vector3(0f, Upper, 0f),
            MatId.TechPanelDark, period: 7f);
        b.AddJumpPad(new Vector3(-26f, 0.1f, 0f), new Vector3(-26f, Upper + 1.8f, 12f),
            new Vector3(0.4f, 0.85f, 1f));

        // --- ledges around the upper walls ---
        const float LedgeY = 15f;
        b.Solid(new Vector3(-HX, LedgeY - 0.5f, -HZ), new Vector3(HX, LedgeY, -HZ + 5f), MatId.MetalGrate, true, 0.9f);
        b.Solid(new Vector3(-HX, LedgeY - 0.5f, HZ - 5f), new Vector3(HX, LedgeY, HZ), MatId.MetalGrate, true, 0.9f);
        RailRun(b, new Vector3(-HX, LedgeY, -HZ + 5f), new Vector3(HX, LedgeY, -HZ + 5f));
        RailRun(b, new Vector3(-HX, LedgeY, HZ - 5f), new Vector3(HX, LedgeY, HZ - 5f));
        b.Ramp(new Vector3(-HX, Upper, -HZ + 5f), new Vector3(-HX + 12f, LedgeY, -HZ + 11f), 1, MatId.Concrete);
        b.Ramp(new Vector3(HX - 12f, Upper, HZ - 11f), new Vector3(HX, LedgeY, HZ - 5f), 0, MatId.Concrete);

        // --- the false wall and the room behind it ---
        b.Solid(new Vector3(-HX + 2f, 0f, -6f), new Vector3(-HX + 10f, 6f, 6f), MatId.Rock, true, 0.6f);
        b.Decor(new Vector3(-HX + 9.9f, 0.4f, -3f), new Vector3(-HX + 10.1f, 4.4f, 3f), MatId.EnergyPanel, 0.6f);
        b.Item(new Vector3(-HX + 6f, 0.8f, 0f), PickupKind.ShieldBelt);
        b.AddLight(new Vector3(-HX + 6f, 3f, 0f), new Vector3(1f, 0.35f, 0.9f), 10f, 3.2f);

        // --- columns and light ---
        for (int i = 0; i < 5; i++)
        {
            float x = -26f + i * 13f;
            foreach (float z in new[] { -15f, 15f })
            {
                b.Prism(new Vector3(x, Upper * 0.5f, z), 1.5f, Upper, 8, MatId.Rock);
                b.Decor(new Vector3(x - 0.7f, Upper + 1.4f, z - 0.7f),
                        new Vector3(x + 0.7f, Upper + 2.4f, z + 0.7f), MatId.Lava, 0.9f);
                b.AddLight(new Vector3(x, Upper + 2.8f, z), new Vector3(1f, 0.5f, 0.16f), 14f, 3.6f, 5f, 0.26f);
            }
        }
        for (int x = -1; x <= 1; x++)
            b.CeilingLamp(new Vector3(x * 20f, CeilY - 1.5f, 0f), new Vector3(0.85f, 0.88f, 1f), 30f, 9f, 1.6f);

        // --- placements ---
        b.Weapon(new Vector3(0f, Upper + 0.9f, 0f), WeaponKind.RocketLauncher);
        b.Item(new Vector3(0f, Upper + 0.8f, -5f), PickupKind.DamageAmp);
        b.Weapon(new Vector3(0f, LedgeY + 0.9f, -HZ + 2.5f), WeaponKind.SniperRifle);
        b.Weapon(new Vector3(0f, LedgeY + 0.9f, HZ - 2.5f), WeaponKind.Ripper);
        b.Weapon(new Vector3(-28f, 0.9f, -18f), WeaponKind.FlakCannon);
        b.Weapon(new Vector3(28f, 0.9f, 18f), WeaponKind.Minigun);
        b.Weapon(new Vector3(28f, 0.9f, -18f), WeaponKind.ShockRifle);
        b.Weapon(new Vector3(-28f, 0.9f, 18f), WeaponKind.PulseGun);
        b.Weapon(new Vector3(0f, 0.9f, 0f), WeaponKind.BioRifle);
        b.Item(new Vector3(-20f, Upper + 0.8f, 0f), PickupKind.BodyArmor);
        b.Item(new Vector3(20f, Upper + 0.8f, 0f), PickupKind.BodyArmor);
        b.Item(new Vector3(0f, 0.7f, -18f), PickupKind.SuperHealth);
        b.Item(new Vector3(0f, LedgeY + 0.8f, 8f), PickupKind.Invisibility);
        b.Ammo(new Vector3(-26f, 0.7f, -18f), AmmoKind.FlakShells);
        b.Ammo(new Vector3(26f, 0.7f, 18f), AmmoKind.MinigunBullets);
        b.Ammo(new Vector3(26f, 0.7f, -18f), AmmoKind.ShockCore);
        b.Ammo(new Vector3(-26f, 0.7f, 18f), AmmoKind.PulseCells);
        b.Ammo(new Vector3(3f, Upper + 0.7f, 0f), AmmoKind.Rockets);
        b.Ammo(new Vector3(3f, LedgeY + 0.7f, -HZ + 2.5f), AmmoKind.SniperRounds);
        for (int i = 0; i < 5; i++)
        {
            float x = -24f + i * 12f;
            b.Item(new Vector3(x, 0.6f, 8f), PickupKind.HealthVial);
            b.Item(new Vector3(x, Upper + 0.6f, -18f), PickupKind.HealthVial);
        }

        b.Spawn(new Vector3(-30f, 0.2f, -20f), 45f);
        b.Spawn(new Vector3(30f, 0.2f, 20f), -135f);
        b.Spawn(new Vector3(30f, 0.2f, -20f), 135f);
        b.Spawn(new Vector3(-30f, 0.2f, 20f), -45f);
        b.Spawn(new Vector3(-16f, Upper + 0.2f, -16f), 135f);
        b.Spawn(new Vector3(16f, Upper + 0.2f, 16f), -45f);
        b.Spawn(new Vector3(0f, Upper + 0.2f, -18f), 180f);
        b.Spawn(new Vector3(0f, Upper + 0.2f, 18f), 0f);
        b.Spawn(new Vector3(-20f, LedgeY + 0.2f, -HZ + 2.5f), 180f);
        b.Spawn(new Vector3(20f, LedgeY + 0.2f, HZ - 2.5f), 0f);

        return b.Build(gl);
    }

    // ================================================================ DM-古籍密室

    /// <summary>
    /// A ring of ancient reading rooms wrapped around a deep central shaft, with a high
    /// balcony overlooking everything.
    /// </summary>
    private static Level BuildCodex(GL gl)
    {
        var b = new LevelBuilder(Loc.MapCodex, Loc.MapCodexDesc);
        var env = b.Level.Environment;
        env.SunDirection = Vector3.Normalize(new Vector3(0.1f, -0.95f, 0.28f));
        env.SunColor = new Vector3(2.2f, 2.0f, 1.7f);
        env.AmbientSky = new Vector3(0.26f, 0.25f, 0.28f);
        env.AmbientGround = new Vector3(0.11f, 0.10f, 0.09f);
        env.SkyTop = new Vector3(0.04f, 0.05f, 0.10f);
        env.SkyHorizon = new Vector3(0.22f, 0.19f, 0.20f);
        env.StarStrength = 0.9f;
        env.CloudStrength = 0.4f;
        env.EnvIntensity = 0.38f;
        env.FogColor = new Vector3(0.12f, 0.11f, 0.11f);
        env.FogDensity = 0.024f;

        const float H = 30f;
        const float CeilY = 20f;
        const float Balcony = 9.5f;
        const float PitFloor = -9f;

        // --- floor with a square shaft punched through the middle ---
        b.Solid(new Vector3(-H, -1.4f, -H), new Vector3(H, 0f, -9f), MatId.Concrete, true, 0.6f);
        b.Solid(new Vector3(-H, -1.4f, 9f), new Vector3(H, 0f, H), MatId.Concrete, true, 0.6f);
        b.Solid(new Vector3(-H, -1.4f, -9f), new Vector3(-9f, 0f, 9f), MatId.Concrete, true, 0.6f);
        b.Solid(new Vector3(9f, -1.4f, -9f), new Vector3(H, 0f, 9f), MatId.Concrete, true, 0.6f);
        b.Solid(new Vector3(-9f, PitFloor - 1.4f, -9f), new Vector3(9f, PitFloor, 9f), MatId.Rock, true, 0.6f);
        // Shaft walls.
        b.Solid(new Vector3(-9.6f, PitFloor, -9.6f), new Vector3(-9f, 0.2f, 9.6f), MatId.Rock, true, 0.5f);
        b.Solid(new Vector3(9f, PitFloor, -9.6f), new Vector3(9.6f, 0.2f, 9.6f), MatId.Rock, true, 0.5f);
        b.Solid(new Vector3(-9.6f, PitFloor, -9.6f), new Vector3(9.6f, 0.2f, -9f), MatId.Rock, true, 0.5f);
        b.Solid(new Vector3(-9.6f, PitFloor, 9f), new Vector3(9.6f, 0.2f, 9.6f), MatId.Rock, true, 0.5f);

        b.Room(new Vector3(-H - 2f, -2f, -H - 2f), new Vector3(H + 2f, CeilY, H + 2f), 2f,
            MatId.Concrete, MatId.Rock, MatId.Concrete, withCeiling: true, withFloor: false);

        // --- inner ring of bookcase walls forming the maze of reading rooms ---
        for (int i = 0; i < 4; i++)
        {
            float a = i / 4f * MathX.TwoPi + MathX.Pi / 4f;
            Vector3 d = new(MathF.Cos(a), 0f, MathF.Sin(a));
            Vector3 c = d * 18f;
            b.Solid(c - new Vector3(5.5f, 0f, 0.9f), c + new Vector3(5.5f, 6.5f, 0.9f), MatId.RustMetal, true, 0.9f);
            b.Solid(c - new Vector3(0.9f, 0f, 5.5f), c + new Vector3(0.9f, 6.5f, 5.5f), MatId.RustMetal, true, 0.9f);
        }
        for (int i = 0; i < 4; i++)
        {
            float a = i / 4f * MathX.TwoPi;
            Vector3 d = new(MathF.Cos(a), 0f, MathF.Sin(a));
            Vector3 c = d * 22f;
            b.Solid(c - new Vector3(1.2f, 0f, 7f), c + new Vector3(1.2f, 7.5f, 7f), MatId.Concrete, true, 0.8f);
        }

        // --- balcony ring overlooking the shaft ---
        b.Annulus(Vector3.Zero, Balcony - 0.6f, Balcony, 12f, 20f, MatId.MetalGrate, 22);
        RingPosts(b, Balcony, 12.3f, 20);
        b.Ramp(new Vector3(-24f, 0f, -3f), new Vector3(-13f, Balcony, 3f), 0, MatId.Concrete);
        b.Ramp(new Vector3(13f, 0f, -3f), new Vector3(24f, Balcony, 3f), 1, MatId.Concrete);
        b.Stairs(new Vector3(-3f, 0f, -24f), new Vector3(-3f, Balcony, -13f), 6f, 12, MatId.Concrete, false);
        b.Stairs(new Vector3(3f, 0f, 24f), new Vector3(3f, Balcony, 13f), 6f, 12, MatId.Concrete, false);

        // --- the pit: dangerous but rewarding ---
        b.AddJumpPad(new Vector3(0f, PitFloor + 0.1f, 0f), new Vector3(0f, Balcony + 2f, 15f),
            new Vector3(0.45f, 0.85f, 1f));
        b.AddLight(new Vector3(0f, PitFloor + 4f, 0f), new Vector3(0.4f, 0.7f, 1f), 18f, 4.5f);
        b.Item(new Vector3(0f, PitFloor + 0.8f, 0f), PickupKind.SuperHealth);
        b.Weapon(new Vector3(-4f, PitFloor + 0.9f, -4f), WeaponKind.RocketLauncher);
        b.Ammo(new Vector3(4f, PitFloor + 0.7f, 4f), AmmoKind.Rockets);

        for (int i = 0; i < 4; i++)
        {
            float a = i / 4f * MathX.TwoPi + MathX.Pi / 4f;
            b.CeilingLamp(new Vector3(MathF.Cos(a) * 20f, CeilY - 1.5f, MathF.Sin(a) * 20f),
                new Vector3(0.9f, 0.86f, 0.76f), 28f, 8.5f, 1.5f);
        }

        // --- placements ---
        b.Weapon(new Vector3(0f, Balcony + 0.9f, 16f), WeaponKind.SniperRifle);
        b.Item(new Vector3(0f, Balcony + 0.8f, -16f), PickupKind.ShieldBelt);
        b.Item(new Vector3(16f, Balcony + 0.8f, 0f), PickupKind.DamageAmp);
        b.Item(new Vector3(-16f, Balcony + 0.8f, 0f), PickupKind.Invisibility);
        for (int i = 0; i < 4; i++)
        {
            float a = i / 4f * MathX.TwoPi + MathX.Pi / 4f;
            Vector3 d = new(MathF.Cos(a), 0f, MathF.Sin(a));
            b.Weapon(d * 25f + new Vector3(0f, 0.9f, 0f),
                i == 0 ? WeaponKind.FlakCannon : i == 1 ? WeaponKind.Minigun
                : i == 2 ? WeaponKind.ShockRifle : WeaponKind.PulseGun);
            b.Ammo(d * 27f + new Vector3(0f, 0.7f, 0f), (AmmoKind)(i % (int)AmmoKind.Count));
            b.Item(d * 13f + new Vector3(0f, 0.8f, 0f), i % 2 == 0 ? PickupKind.BodyArmor : PickupKind.ThighPads);
            b.Spawn(d * 26f + new Vector3(0f, 0.2f, 0f), -a * MathX.Rad2Deg + 180f);
        }
        for (int i = 0; i < 4; i++)
        {
            float a = i / 4f * MathX.TwoPi;
            Vector3 d = new(MathF.Cos(a), 0f, MathF.Sin(a));
            b.Weapon(d * 28f + new Vector3(0f, 0.9f, 0f), i % 2 == 0 ? WeaponKind.Ripper : WeaponKind.BioRifle);
            b.Item(d * 16f + new Vector3(0f, 0.6f, 0f), PickupKind.HealthVial);
            b.Spawn(d * 14f + new Vector3(0f, 0.2f, 0f), -a * MathX.Rad2Deg);
            b.Spawn(d * 17f + new Vector3(0f, Balcony + 0.2f, 0f), -a * MathX.Rad2Deg + 180f);
        }

        return b.Build(gl);
    }

    // ================================================================ DM-火衛基地

    /// <summary>
    /// A modular research station on a moon: two habitat blocks joined by a glassed-over
    /// connector, with the airless surface visible overhead.
    /// </summary>
    private static Level BuildPhobos(GL gl)
    {
        var b = new LevelBuilder(Loc.MapPhobos, Loc.MapPhobosDesc);
        b.Level.GravityScale = 0.78f;
        var env = b.Level.Environment;
        env.SunDirection = Vector3.Normalize(new Vector3(0.55f, -0.55f, -0.62f));
        env.SunColor = new Vector3(3.6f, 3.5f, 3.4f);
        env.AmbientSky = new Vector3(0.22f, 0.24f, 0.32f);
        env.AmbientGround = new Vector3(0.08f, 0.08f, 0.09f);
        env.SkyTop = new Vector3(0.006f, 0.008f, 0.020f);
        env.SkyHorizon = new Vector3(0.05f, 0.06f, 0.12f);
        env.SkyGround = new Vector3(0.05f, 0.045f, 0.04f);
        env.StarStrength = 2.8f;
        env.CloudStrength = 0f;
        env.EnvIntensity = 0.8f;
        env.FogColor = new Vector3(0.05f, 0.055f, 0.08f);
        env.FogDensity = 0.010f;

        const float BlockHalf = 20f;
        const float BlockZ = 30f;
        const float CeilY = 15f;
        const float Upper = 7f;

        // --- the regolith the station stands on ---
        b.Solid(new Vector3(-36f, -2f, -BlockZ - 30f), new Vector3(36f, 0f, BlockZ + 30f), MatId.Rock, true, 0.4f);

        for (int end = 0; end < 2; end++)
        {
            float sign = end == 0 ? -1f : 1f;
            float z = BlockZ * sign;

            // --- habitat block ---
            b.Room(new Vector3(-BlockHalf, 0f, z - 18f), new Vector3(BlockHalf, CeilY, z + 18f), 1.6f,
                MatId.TechFloor, MatId.SkyMetal, MatId.TechPanelDark, withCeiling: true, withFloor: true);
            // Glass roof panel so the star field is visible from inside.
            b.Decor(new Vector3(-9f, CeilY - 1.6f, z - 9f), new Vector3(9f, CeilY - 1.4f, z + 9f), MatId.Glass);

            // Upper walkway ring inside the block.
            b.Solid(new Vector3(-BlockHalf + 1.6f, Upper - 0.5f, z - 18f + 1.6f),
                    new Vector3(-BlockHalf + 8f, Upper, z + 18f - 1.6f), MatId.MetalGrate, true, 0.9f);
            b.Solid(new Vector3(BlockHalf - 8f, Upper - 0.5f, z - 18f + 1.6f),
                    new Vector3(BlockHalf - 1.6f, Upper, z + 18f - 1.6f), MatId.MetalGrate, true, 0.9f);
            b.Solid(new Vector3(-BlockHalf + 8f, Upper - 0.5f, z + (18f - 1.6f - 6f) * sign),
                    new Vector3(BlockHalf - 8f, Upper, z + (18f - 1.6f) * sign), MatId.MetalGrate, true, 0.9f);
            RailRun(b, new Vector3(-BlockHalf + 8f, Upper, z - 16f), new Vector3(-BlockHalf + 8f, Upper, z + 16f));
            RailRun(b, new Vector3(BlockHalf - 8f, Upper, z - 16f), new Vector3(BlockHalf - 8f, Upper, z + 16f));
            b.Ramp(new Vector3(-BlockHalf + 8f, 0f, z - 4f), new Vector3(-BlockHalf + 16f, Upper, z + 4f),
                1, MatId.TechFloor);
            b.Ramp(new Vector3(BlockHalf - 16f, 0f, z - 4f), new Vector3(BlockHalf - 8f, Upper, z + 4f),
                0, MatId.TechFloor);

            // Reactor column in the middle of each block.
            b.Prism(new Vector3(0f, 5f, z), 3.2f, 10f, 8, MatId.TechPanelDark);
            b.Decor(new Vector3(-1.2f, 2f, z - 3.4f), new Vector3(1.2f, 8f, z - 3.2f), MatId.EnergyPanel, 0.6f);
            b.AddLight(new Vector3(0f, 6f, z), new Vector3(0.35f, 0.8f, 1f), 20f, 5f, 2.5f, 0.14f);

            b.CeilingLamp(new Vector3(-11f, CeilY - 1.8f, z - 9f), new Vector3(0.85f, 0.9f, 1f), 24f, 7f);
            b.CeilingLamp(new Vector3(11f, CeilY - 1.8f, z + 9f), new Vector3(0.85f, 0.9f, 1f), 24f, 7f);

            b.Weapon(new Vector3(-BlockHalf + 4f, Upper + 0.9f, z), end == 0 ? WeaponKind.SniperRifle : WeaponKind.ShockRifle);
            b.Weapon(new Vector3(BlockHalf - 4f, Upper + 0.9f, z), end == 0 ? WeaponKind.Minigun : WeaponKind.Ripper);
            b.Weapon(new Vector3(0f, 0.9f, z + 12f * sign), end == 0 ? WeaponKind.FlakCannon : WeaponKind.RocketLauncher);
            b.Weapon(new Vector3(-12f, 0.9f, z - 12f * sign), WeaponKind.PulseGun);
            b.Item(new Vector3(12f, 0.9f, z - 12f * sign), end == 0 ? PickupKind.BodyArmor : PickupKind.ShieldBelt);
            b.Item(new Vector3(0f, 0.8f, z - 8f * sign), PickupKind.HealthPack);
            b.Ammo(new Vector3(-BlockHalf + 4f, Upper + 0.7f, z + 5f), AmmoKind.SniperRounds);
            b.Ammo(new Vector3(BlockHalf - 4f, Upper + 0.7f, z + 5f), AmmoKind.MinigunBullets);
            b.Ammo(new Vector3(3f, 0.7f, z + 12f * sign), AmmoKind.FlakShells);
            for (int i = 0; i < 4; i++)
                b.Spawn(new Vector3(-12f + i * 8f, 0.2f, z + 6f * sign), sign > 0 ? 180f : 0f);
            b.Spawn(new Vector3(-BlockHalf + 4f, Upper + 0.2f, z), -90f);
            b.Spawn(new Vector3(BlockHalf - 4f, Upper + 0.2f, z), 90f);
        }

        // --- the connector between the blocks, glassed on both sides ---
        b.Solid(new Vector3(-7f, -1.4f, -14f), new Vector3(7f, 0f, 14f), MatId.TechFloor, true, 0.9f);
        b.Solid(new Vector3(-7f, 0f, -14f), new Vector3(-5.4f, 8f, 14f), MatId.SkyMetal, true, 0.8f);
        b.Solid(new Vector3(5.4f, 0f, -14f), new Vector3(7f, 8f, 14f), MatId.SkyMetal, true, 0.8f);
        b.Solid(new Vector3(-7f, 8f, -14f), new Vector3(7f, 9.2f, 14f), MatId.TechPanelDark, true, 0.8f);
        b.Decor(new Vector3(-5.4f, 1.5f, -14f), new Vector3(-5.2f, 7f, 14f), MatId.Glass);
        b.Decor(new Vector3(5.2f, 1.5f, -14f), new Vector3(5.4f, 7f, 14f), MatId.Glass);
        b.CeilingLamp(new Vector3(0f, 8.8f, 0f), new Vector3(0.8f, 0.88f, 1f), 24f, 7f, 1.2f);

        // --- exposed surface route around the outside, the risky flank ---
        for (int i = -1; i <= 1; i += 2)
        {
            b.Solid(new Vector3(i * 26f - 5f, 0f, -18f), new Vector3(i * 26f + 5f, 3.2f, 18f), MatId.Rock, true, 0.5f);
            b.Ramp(new Vector3(i * 26f - 5f, 0f, -24f), new Vector3(i * 26f + 5f, 3.2f, -18f), 2, MatId.Rock, true, 0.5f);
            b.Ramp(new Vector3(i * 26f - 5f, 0f, 18f), new Vector3(i * 26f + 5f, 3.2f, 24f), 3, MatId.Rock, true, 0.5f);
            b.Item(new Vector3(i * 26f, 4.0f, 0f), PickupKind.SuperHealth);
            b.AddJumpPad(new Vector3(i * 26f, 3.3f, 8f), new Vector3(i * 14f, Upper + 2f, BlockZ - 14f),
                new Vector3(0.4f, 0.85f, 1f));
        }

        b.Weapon(new Vector3(0f, 0.9f, 0f), WeaponKind.BioRifle);
        b.Item(new Vector3(0f, 0.8f, 8f), PickupKind.DamageAmp);
        b.Item(new Vector3(0f, 0.8f, -8f), PickupKind.Invisibility);
        for (int i = 0; i < 4; i++)
        {
            float z = -12f + i * 8f;
            b.Item(new Vector3(-3f, 0.6f, z), PickupKind.HealthVial);
            b.Item(new Vector3(3f, 0.6f, z), PickupKind.HealthVial);
        }
        b.Spawn(new Vector3(0f, 0.2f, 0f), 0f);

        return b.Build(gl);
    }

    // ================================================================ DM-磚牆競技場

    /// <summary>
    /// A compact brick arena: one main room with a raised gallery, two side chambers and a
    /// short connecting tunnel. Small, fast, and built for one-on-one.
    /// </summary>
    private static Level BuildStalwart(GL gl)
    {
        var b = new LevelBuilder(Loc.MapStalwart, Loc.MapStalwartDesc);
        var env = b.Level.Environment;
        env.SunDirection = Vector3.Normalize(new Vector3(-0.35f, -0.85f, -0.40f));
        env.SunColor = new Vector3(2.6f, 2.3f, 1.9f);
        env.AmbientSky = new Vector3(0.28f, 0.26f, 0.28f);
        env.AmbientGround = new Vector3(0.14f, 0.11f, 0.09f);
        env.SkyTop = new Vector3(0.05f, 0.07f, 0.14f);
        env.SkyHorizon = new Vector3(0.30f, 0.22f, 0.18f);
        env.StarStrength = 0.7f;
        env.CloudStrength = 0.55f;
        env.EnvIntensity = 0.4f;
        env.FogColor = new Vector3(0.13f, 0.11f, 0.10f);
        env.FogDensity = 0.024f;

        const float HX = 20f, HZ = 16f;
        const float CeilY = 15f;
        const float Gallery = 6.5f;

        // --- main hall ---
        b.Solid(new Vector3(-HX, -1.4f, -HZ), new Vector3(HX, 0f, HZ), MatId.Concrete, true, 0.8f);
        b.Room(new Vector3(-HX - 2f, -2f, -HZ - 2f), new Vector3(HX + 2f, CeilY, HZ + 2f), 2f,
            MatId.Concrete, MatId.RustMetal, MatId.TechPanelDark, withCeiling: true, withFloor: false);

        // --- gallery ledge around three sides ---
        b.Solid(new Vector3(-HX, Gallery - 0.6f, -HZ), new Vector3(HX, Gallery, -HZ + 5f), MatId.Concrete, true, 0.9f);
        b.Solid(new Vector3(-HX, Gallery - 0.6f, -HZ + 5f), new Vector3(-HX + 5f, Gallery, HZ - 5f), MatId.Concrete, true, 0.9f);
        b.Solid(new Vector3(HX - 5f, Gallery - 0.6f, -HZ + 5f), new Vector3(HX, Gallery, HZ - 5f), MatId.Concrete, true, 0.9f);
        RailRun(b, new Vector3(-HX + 5f, Gallery, -HZ + 5f), new Vector3(HX - 5f, Gallery, -HZ + 5f));
        RailRun(b, new Vector3(-HX + 5f, Gallery, -HZ + 5f), new Vector3(-HX + 5f, Gallery, HZ - 5f));
        RailRun(b, new Vector3(HX - 5f, Gallery, -HZ + 5f), new Vector3(HX - 5f, Gallery, HZ - 5f));

        b.Stairs(new Vector3(-HX + 2.5f, 0f, HZ - 4f), new Vector3(-HX + 2.5f, Gallery, HZ - 12f), 5f, 11,
            MatId.Concrete, alongX: false);
        b.Stairs(new Vector3(HX - 2.5f, 0f, HZ - 4f), new Vector3(HX - 2.5f, Gallery, HZ - 12f), 5f, 11,
            MatId.Concrete, alongX: false);

        // --- central block: cover on the floor, a perch on top ---
        b.Solid(new Vector3(-5f, 0f, -4f), new Vector3(5f, 4.2f, 4f), MatId.RustMetal, true, 1.0f);
        b.Ramp(new Vector3(-3f, 0f, 4f), new Vector3(3f, 4.2f, 9f), 3, MatId.Concrete);
        b.Decor(new Vector3(-5.2f, 4.2f, -4.2f), new Vector3(5.2f, 4.45f, 4.2f), MatId.Trim, 1.1f);

        // --- two side chambers, each joined to the hall by a short corridor ---
        // Floors are laid explicitly at the hall's level; letting Room() build them would put the
        // chamber floor a step and a half above the hall, which nothing could climb.
        for (int i = -1; i <= 1; i += 2)
        {
            float x = i * (HX + 10f);
            const float ChamberHalf = 9f;
            const float DoorHalf = 2.5f;
            const float DoorTop = 5f;

            b.Solid(new Vector3(x - ChamberHalf, -1.4f, -8f), new Vector3(x + ChamberHalf, 0f, 8f),
                MatId.Concrete, true, 0.9f);
            b.Room(new Vector3(x - ChamberHalf, 0f, -8f), new Vector3(x + ChamberHalf, 10f, 8f), 1.6f,
                MatId.Concrete, MatId.RustMetal, MatId.TechPanelDark, withCeiling: true, withFloor: false);

            // Punch matching openings through the chamber's inner wall and the hall's outer wall,
            // then floor and roof the corridor between them.
            float chamberInner = i < 0 ? x + ChamberHalf - 1.6f : x - ChamberHalf;
            float hallOuter = i < 0 ? -HX - 2f : HX;
            foreach (float wx in new[] { chamberInner, hallOuter })
            {
                b.Solid(new Vector3(wx, 0f, -8f), new Vector3(wx + 1.6f, 10f, -DoorHalf), MatId.RustMetal, true, 0.9f);
                b.Solid(new Vector3(wx, 0f, DoorHalf), new Vector3(wx + 1.6f, 10f, 8f), MatId.RustMetal, true, 0.9f);
                b.Solid(new Vector3(wx, DoorTop, -DoorHalf), new Vector3(wx + 1.6f, 10f, DoorHalf), MatId.RustMetal, true, 0.9f);
            }
            float corridorA = i < 0 ? x + ChamberHalf : hallOuter + 1.6f;
            float corridorB = i < 0 ? hallOuter : x - ChamberHalf;
            float c0 = MathF.Min(corridorA, corridorB), c1 = MathF.Max(corridorA, corridorB);
            b.Solid(new Vector3(c0, -1.4f, -DoorHalf), new Vector3(c1, 0f, DoorHalf), MatId.Concrete, true, 0.9f);
            b.Solid(new Vector3(c0, DoorTop, -DoorHalf), new Vector3(c1, DoorTop + 1f, DoorHalf),
                MatId.TechPanelDark, true, 0.9f);
            b.Solid(new Vector3(c0, 0f, -DoorHalf - 1.4f), new Vector3(c1, DoorTop + 1f, -DoorHalf),
                MatId.RustMetal, true, 0.9f);
            b.Solid(new Vector3(c0, 0f, DoorHalf), new Vector3(c1, DoorTop + 1f, DoorHalf + 1.4f),
                MatId.RustMetal, true, 0.9f);

            b.CeilingLamp(new Vector3(x, 8.8f, 0f), new Vector3(0.92f, 0.85f, 0.72f), 20f, 6.5f, 1.2f);
            b.Weapon(new Vector3(x, 0.9f, i * 5f), i < 0 ? WeaponKind.FlakCannon : WeaponKind.RocketLauncher);
            b.Ammo(new Vector3(x + i * 3f, 0.7f, i * 5f), i < 0 ? AmmoKind.FlakShells : AmmoKind.Rockets);
            b.Item(new Vector3(x, 0.8f, -i * 5f), i < 0 ? PickupKind.BodyArmor : PickupKind.ShieldBelt);
            b.Spawn(new Vector3(x, 0.2f, 0f), i < 0 ? -90f : 90f);
            b.AddJumpPad(new Vector3(x, 0.1f, i * 6f), new Vector3(i * (HX - 2.5f), Gallery + 1.6f, 0f),
                new Vector3(0.4f, 0.85f, 1f));
        }

        b.CeilingLamp(new Vector3(0f, CeilY - 1.5f, -8f), new Vector3(0.95f, 0.88f, 0.74f), 26f, 8f, 1.5f);
        b.CeilingLamp(new Vector3(0f, CeilY - 1.5f, 8f), new Vector3(0.95f, 0.88f, 0.74f), 26f, 8f, 1.5f);

        // --- placements: everything within a few seconds of everything else ---
        b.Weapon(new Vector3(0f, 5.1f, 0f), WeaponKind.ShockRifle);
        b.Item(new Vector3(0f, 5.0f, -2.5f), PickupKind.DamageAmp);
        b.Weapon(new Vector3(0f, Gallery + 0.9f, -HZ + 2.5f), WeaponKind.SniperRifle);
        b.Weapon(new Vector3(-HX + 2.5f, Gallery + 0.9f, 0f), WeaponKind.Minigun);
        b.Weapon(new Vector3(HX - 2.5f, Gallery + 0.9f, 0f), WeaponKind.PulseGun);
        b.Weapon(new Vector3(-14f, 0.9f, 12f), WeaponKind.Ripper);
        b.Weapon(new Vector3(14f, 0.9f, 12f), WeaponKind.BioRifle);
        b.Item(new Vector3(-14f, 0.8f, -12f), PickupKind.HealthPack);
        b.Item(new Vector3(14f, 0.8f, -12f), PickupKind.HealthPack);
        b.Item(new Vector3(0f, 0.7f, 13f), PickupKind.SuperHealth);
        b.Ammo(new Vector3(3f, Gallery + 0.7f, -HZ + 2.5f), AmmoKind.SniperRounds);
        b.Ammo(new Vector3(-14f, 0.7f, 9f), AmmoKind.Blades);
        b.Ammo(new Vector3(14f, 0.7f, 9f), AmmoKind.BioSludge);
        b.Ammo(new Vector3(3f, 5.0f, 0f), AmmoKind.ShockCore);
        for (int i = 0; i < 4; i++)
        {
            float x = -12f + i * 8f;
            b.Item(new Vector3(x, 0.6f, -13f), PickupKind.HealthVial);
        }

        b.Spawn(new Vector3(-16f, 0.2f, -12f), 135f);
        b.Spawn(new Vector3(16f, 0.2f, -12f), -135f);
        b.Spawn(new Vector3(-16f, 0.2f, 12f), 45f);
        b.Spawn(new Vector3(16f, 0.2f, 12f), -45f);
        b.Spawn(new Vector3(0f, 0.2f, 12f), 0f);
        b.Spawn(new Vector3(0f, Gallery + 0.2f, -HZ + 2.5f), 180f);
        b.Spawn(new Vector3(-HX + 2.5f, Gallery + 0.2f, 0f), -90f);
        b.Spawn(new Vector3(HX - 2.5f, Gallery + 0.2f, 0f), 90f);

        return b.Build(gl);
    }
}
