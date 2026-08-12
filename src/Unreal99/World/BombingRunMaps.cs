using System.Numerics;
using Silk.NET.OpenGL;
using Unreal99.Core;
using Unreal99.Game;
using Unreal99.Rendering;
using Unreal99.UI;

namespace Unreal99.World;

/// <summary>
/// The Bombing Run arenas. Both are symmetrical by design — the mode gives each side the same
/// job in mirror image, so any asymmetry is a balance bug rather than a feature.
/// </summary>
public static partial class Maps
{
    // ================================================================ BR-阿努比斯神殿

    /// <summary>
    /// Anubis. An Egyptian temple laid out as two courtyards either side of a central hall, with
    /// each goal hoop hung over a pit at the back of its base — in the original, anything that
    /// goes through the ring keeps falling, so scoring costs the scorer their life. The two sand
    /// dunes outside the courtyards hold the map's Super Shield and its Redeemer.
    /// </summary>
    private static Level BuildAnubis(GL gl)
    {
        var b = new LevelBuilder(Loc.MapAnubis, Loc.MapAnubisDesc);
        var env = b.Level.Environment;
        env.SunDirection = Vector3.Normalize(new Vector3(-0.42f, -0.76f, -0.50f));
        env.SunColor = new Vector3(4.6f, 4.0f, 3.0f);
        env.AmbientSky = new Vector3(0.44f, 0.39f, 0.31f);
        env.AmbientGround = new Vector3(0.34f, 0.28f, 0.20f);
        env.EnvIntensity = 0.86f;
        env.SkyTop = new Vector3(0.32f, 0.46f, 0.70f);
        env.SkyHorizon = new Vector3(0.90f, 0.80f, 0.58f);
        env.SkyGround = new Vector3(0.58f, 0.47f, 0.31f);
        env.StarStrength = 0f;
        env.CloudStrength = 0.18f;
        env.FogColor = new Vector3(0.86f, 0.78f, 0.60f);
        env.FogDensity = 0.0028f;

        const float HX = 92f, HZ = 46f, Ground = 0f;
        // The floor is laid in strips so the two goal pits are genuine holes. Anubis hangs each
        // hoop over one of them: running the ball through the ring drops the scorer into the pit,
        // and that trade — seven points for your own life — is the map's whole character.
        const float PitX = 74f, PitHalf = 11f;
        foreach (float sign in new[] { -1f, 1f })
        {
            b.Solid(new Vector3(sign > 0 ? PitX + PitHalf : -HX, -16f, -HZ),
                    new Vector3(sign > 0 ? HX : -PitX - PitHalf, Ground, HZ), MatId.Rock, true, 0.5f);
            foreach (float s in new[] { -1f, 1f })
                b.Solid(new Vector3(sign * PitX - PitHalf, -16f, s > 0 ? PitHalf : -HZ),
                        new Vector3(sign * PitX + PitHalf, Ground, s > 0 ? HZ : -PitHalf),
                        MatId.Rock, true, 0.5f);
        }
        b.Solid(new Vector3(-PitX + PitHalf, -16f, -HZ), new Vector3(PitX - PitHalf, Ground, HZ),
            MatId.Rock, true, 0.5f);
        // An Egyptian temple court: a colonnade round the field and strata up the enclosing walls.
        DressOutdoor(b, HX, HZ, Ground, 40f, MatId.Rock, MatId.Trim, 6);
        for (int i = 0; i <= 11; i++)
        {
            float x = MathX.Lerp(-HX + 8f, HX - 8f, i / 11f);
            foreach (int s in new[] { -1, 1 })
                Column(b, new Vector3(x, Ground, s * (HZ - 3.5f)), 11f, 1.7f, MatId.Concrete, MatId.Trim, 12);
        }
        b.Room(new Vector3(-HX - 4f, -20f, -HZ - 4f), new Vector3(HX + 4f, 40f, HZ + 4f), 4f,
            MatId.Rock, MatId.Rock, MatId.Rock, withCeiling: false, withFloor: false);

        var rng = new Rng(0xA9B5);

        // --- the two bases: a goal hoop over a pit, reached only from that side's courtyard ---
        foreach (var (team, sign) in new[] { (Team.Red, 1f), (Team.Blue, -1f) })
        {
            float bx = sign * 74f;
            MatId teamMat = team == Team.Red ? MatId.TeamRed : MatId.TeamBlue;

            // Lasers at the bottom of the shaft, so the drop reads as lethal before you take it.
            b.Lava(new Vector3(bx - 10.6f, -16f, -10.6f), new Vector3(bx + 10.6f, -14.6f, 10.6f));
            // A lip all the way round so the pit is approached, not stumbled into.
            foreach (var (ox, oz, sx, sz) in new[]
                     {
                         (0f, -12.5f, 13f, 1.5f), (0f, 12.5f, 13f, 1.5f),
                         (sign * 12.5f, 0f, 1.5f, 14f),
                     })
                b.Solid(new Vector3(bx + ox - sx, Ground, oz - sz),
                        new Vector3(bx + ox + sx, Ground + 0.5f, oz + sz), teamMat, true, 0.7f);

            // The hoop hangs over the middle of the pit, facing down the map.
            b.AddGoalHoop(new Vector3(bx, Ground + 3.4f, 0f), team, 90f);

            // Base walls: one opening, towards the courtyard.
            b.Solid(new Vector3(bx + sign * 15f, Ground, -18f),
                    new Vector3(bx + sign * 17f, Ground + 12f, 18f), MatId.Rock, true, 0.6f);
            foreach (float s in new[] { -1f, 1f })
                b.Solid(new Vector3(bx - 16f, Ground, s * 16f), new Vector3(bx + 16f, Ground + 12f, s * 18f),
                    MatId.Rock, true, 0.6f);

            // Six vials at each goal, and the spawn line behind it.
            for (int i = 0; i < 6; i++)
                b.Item(new Vector3(bx + sign * 13.5f, Ground + 0.8f, -7.5f + i * 3f), PickupKind.HealthVial);
            for (int i = 0; i < 4; i++)
                b.Spawn(new Vector3(bx + sign * 14f, Ground + 0.2f, -9f + i * 6f), sign > 0 ? 180f : 0f, team);
            b.AddLight(new Vector3(bx, Ground + 10f, 0f), GameTypes.TeamColor(team) * 1.4f, 34f, 6f);
        }

        // --- the courtyards: the working half of the map, one per side ---
        foreach (var (team, sign) in new[] { (Team.Red, 1f), (Team.Blue, -1f) })
        {
            float cx = sign * 44f;
            // Bio Rifle below the north top entrance; Shock towards the centre; Link in the south
            // corner; Minigun by the south respawn; Lightning by the north respawn.
            b.Weapon(new Vector3(cx + sign * 8f, Ground + 1f, 26f), WeaponKind.BioRifle);
            b.Weapon(new Vector3(cx - sign * 12f, Ground + 1f, 0f), WeaponKind.ShockRifle);
            b.Weapon(new Vector3(cx + sign * 6f, Ground + 1f, -26f), WeaponKind.LinkGun);
            b.Weapon(new Vector3(cx + sign * 14f, Ground + 1f, -18f), WeaponKind.Minigun);
            b.Weapon(new Vector3(cx + sign * 14f, Ground + 1f, 18f), WeaponKind.LightningGun);

            b.Ammo(new Vector3(cx - sign * 10f, Ground + 0.9f, 30f), AmmoKind.BioSludge);
            b.Ammo(new Vector3(cx - sign * 8f, Ground + 0.9f, 30f), AmmoKind.BioSludge);
            b.Ammo(new Vector3(cx - sign * 10f, Ground + 0.9f, -30f), AmmoKind.ShockCore);
            b.Ammo(new Vector3(cx + sign * 12f, Ground + 0.9f, 4f), AmmoKind.ShockCore);
            b.Ammo(new Vector3(cx + sign * 10f, Ground + 0.9f, 32f), AmmoKind.LinkCells);
            b.Ammo(new Vector3(cx + sign * 10f, Ground + 0.9f, -32f), AmmoKind.LinkCells);
            b.Ammo(new Vector3(cx - sign * 14f, Ground + 0.9f, -6f), AmmoKind.LinkCells);
            b.Ammo(new Vector3(cx - sign * 6f, Ground + 0.9f, 6f), AmmoKind.Bullets);
            b.Ammo(new Vector3(cx - sign * 4f, Ground + 0.9f, 6f), AmmoKind.Bullets);
            b.Ammo(new Vector3(cx + sign * 8f, Ground + 0.9f, 34f), AmmoKind.FlakShells);
            b.Ammo(new Vector3(cx + sign * 8f, Ground + 0.9f, -34f), AmmoKind.Rockets);
            b.Ammo(new Vector3(cx + sign * 16f, Ground + 0.9f, 22f), AmmoKind.LightningCells);

            // Two health packs under each top entrance.
            foreach (float s in new[] { -1f, 1f })
            {
                b.Item(new Vector3(cx + sign * 4f, Ground + 0.9f, s * 30f), PickupKind.HealthPack);
                b.Item(new Vector3(cx + sign * 7f, Ground + 0.9f, s * 30f), PickupKind.HealthPack);
            }

            // Colonnade down both long sides, so the courtyard is crossable but never open.
            for (int i = 0; i < 5; i++)
            {
                float pz = -32f + i * 16f;
                foreach (float s in new[] { -1f, 1f })
                    b.Solid(new Vector3(cx + s * 15f - 1.4f, Ground, pz - 1.4f),
                            new Vector3(cx + s * 15f + 1.4f, Ground + 9f, pz + 1.4f),
                            MatId.Rock, true, 0.6f);
            }

            // The two respawn corridors, at the far corners.
            foreach (float s in new[] { -1f, 1f })
                for (int i = 0; i < 2; i++)
                    b.Spawn(new Vector3(cx + sign * 2f + i * sign * 4f, Ground + 0.2f, s * 38f),
                        sign > 0 ? 180f : 0f, team);
        }

        // --- centre: the ball, the Flak room north of it and the Rocket room south of it ---
        b.AddBallSpawn(new Vector3(0f, Ground + 1.2f, 0f));
        b.Weapon(new Vector3(0f, Ground + 4.4f, 24f), WeaponKind.FlakCannon);
        b.Weapon(new Vector3(0f, Ground + 4.4f, -24f), WeaponKind.RocketLauncher);
        foreach (float s in new[] { -1f, 1f })
        {
            // Each room is a two-level box off the middle, with a ramp to the ledge that holds
            // the weapon and a rank of vials among the statues below it.
            b.Solid(new Vector3(-14f, Ground, s * 20f), new Vector3(14f, Ground + 3.6f, s * 28f),
                MatId.Rock, true, 0.6f);
            b.Ramp(new Vector3(-20f, Ground, s * 18f), new Vector3(-14f, Ground + 3.6f, s * 28f),
                s > 0 ? 1 : 1, MatId.Rock, true, 0.5f);
            b.Ramp(new Vector3(14f, Ground, s * 18f), new Vector3(20f, Ground + 3.6f, s * 28f),
                0, MatId.Rock, true, 0.5f);
            for (int i = 0; i < 12; i++)
                b.Item(new Vector3(-11f + i * 2f, Ground + 0.8f, s * 33f), PickupKind.HealthVial);
            // Sphinx monoliths flanking the room.
            foreach (float t in new[] { -1f, 1f })
                b.Solid(new Vector3(t * 17f - 2.2f, Ground, s * 24f - 2.2f),
                        new Vector3(t * 17f + 2.2f, Ground + 7f, s * 24f + 2.2f),
                        MatId.Rock, true, 0.7f);
        }
        // The two dunes beyond the rooms: Super Shield on the north one, Redeemer on the south.
        b.Item(new Vector3(0f, Ground + 5.2f, 40f), PickupKind.ShieldBelt);
        b.Solid(new Vector3(-9f, Ground, 36f), new Vector3(9f, Ground + 4.4f, 44f), MatId.Rock, true, 0.5f);
        b.Ramp(new Vector3(-16f, Ground, 36f), new Vector3(-9f, Ground + 4.4f, 44f), 1, MatId.Rock, true, 0.5f);
        b.Weapon(new Vector3(0f, Ground + 5.2f, -40f), WeaponKind.Redeemer, 90f);
        b.Solid(new Vector3(-9f, Ground, -44f), new Vector3(9f, Ground + 4.4f, -36f), MatId.Rock, true, 0.5f);
        b.Ramp(new Vector3(9f, Ground, -44f), new Vector3(16f, Ground + 4.4f, -36f), 0, MatId.Rock, true, 0.5f);

        // Scattered obelisks in the open ground between the courtyards and the centre.
        for (int i = 0; i < 16; i++)
        {
            float px = rng.Range(-HX + 26f, HX - 26f);
            float pz = rng.Range(-HZ + 8f, HZ - 8f);
            var here = new Vector3(px, 0f, pz);
            if (MathF.Abs(px) < 24f) continue;
            if (Vector3.Distance(here, Vector3.Zero) < 30f) continue;
            b.Solid(new Vector3(px - 1.6f, Ground, pz - 1.6f),
                    new Vector3(px + 1.6f, Ground + rng.Range(4f, 8f), pz + 1.6f), MatId.Rock, true, 0.7f);
        }

        return b.Build(gl);
    }

    // ================================================================ BR-巨像基地

    /// <summary>
    /// Colossus. A facility on rocky ground, built as rear base → forward base → neutral zone and
    /// mirrored. Each rear base has a jump pad that fires straight at the goal, which is the fast
    /// way in and the reason the mode's long passes work here; the neutral zone holds the ball,
    /// the Redeemer and the Super Shield.
    /// </summary>
    private static Level BuildColossus(GL gl)
    {
        var b = new LevelBuilder(Loc.MapColossus, Loc.MapColossusDesc);
        var env = b.Level.Environment;
        env.SunDirection = Vector3.Normalize(new Vector3(-0.35f, -0.72f, -0.60f));
        env.SunColor = new Vector3(3.6f, 3.6f, 3.9f);
        env.AmbientSky = new Vector3(0.38f, 0.42f, 0.52f);
        env.AmbientGround = new Vector3(0.26f, 0.27f, 0.31f);
        env.EnvIntensity = 0.82f;
        env.SkyTop = new Vector3(0.20f, 0.32f, 0.54f);
        env.SkyHorizon = new Vector3(0.62f, 0.68f, 0.78f);
        env.SkyGround = new Vector3(0.40f, 0.42f, 0.46f);
        env.StarStrength = 0f;
        env.CloudStrength = 0.55f;
        env.FogColor = new Vector3(0.60f, 0.66f, 0.76f);
        env.FogDensity = 0.0032f;

        const float HX = 104f, HZ = 54f, Ground = 0f;
        b.Solid(new Vector3(-HX, -6f, -HZ), new Vector3(HX, Ground, HZ), MatId.Rock, true, 0.5f);
        DressOutdoor(b, HX, HZ, Ground, 46f, MatId.Rock, MatId.Trim, 7);
        b.Room(new Vector3(-HX - 4f, -6f, -HZ - 4f), new Vector3(HX + 4f, 46f, HZ + 4f), 4f,
            MatId.Rock, MatId.Rock, MatId.Rock, withCeiling: false, withFloor: false);

        foreach (var (team, sign) in new[] { (Team.Red, 1f), (Team.Blue, -1f) })
        {
            MatId teamMat = team == Team.Red ? MatId.TeamRed : MatId.TeamBlue;
            float rear = sign * 84f;      // rear base, where the goal is
            float bunker = sign * 56f;    // the bunker between rear and forward
            float forward = sign * 30f;   // forward base, facing the neutral zone

            // --- rear base: goal on the upper level, ramps up each side, jump pad from below ---
            b.Solid(new Vector3(rear - sign * 18f, Ground, -26f), new Vector3(rear + sign * 18f, Ground + 0.4f, 26f),
                MatId.Concrete, true, 0.6f);
            b.Solid(new Vector3(rear - sign * 6f, Ground, -14f), new Vector3(rear + sign * 14f, Ground + 6f, 14f),
                teamMat, true, 0.7f);
            foreach (float s in new[] { -1f, 1f })
                b.Ramp(new Vector3(rear - sign * 6f, Ground, s * 20f),
                       new Vector3(rear + sign * 14f, Ground + 6f, s * 14f), s > 0 ? 3 : 2,
                       MatId.Concrete, true, 0.5f);
            b.AddGoalHoop(new Vector3(rear, Ground + 9.4f, 0f), team, 90f);
            // The jump pad that throws an attacker straight at the ring.
            b.AddJumpPad(new Vector3(rear - sign * 22f, Ground + 0.4f, 0f),
                new Vector3(rear, Ground + 10f, 0f), GameTypes.TeamColor(team), 4f);

            // Two Miniguns beside the goal on the lower level, and the concrete arches above it.
            foreach (float s in new[] { -1f, 1f })
            {
                b.Weapon(new Vector3(rear - sign * 10f, Ground + 1f, s * 18f), WeaponKind.Minigun);
                b.Ammo(new Vector3(rear - sign * 12f, Ground + 0.9f, s * 18f), AmmoKind.Bullets);
                b.Item(new Vector3(rear - sign * 14f, Ground + 0.9f, s * 12f), PickupKind.HealthPack);
                b.Item(new Vector3(rear - sign * 14f, Ground + 0.9f, s * 15f), PickupKind.HealthPack);
                b.Solid(new Vector3(rear + sign * 2f, Ground + 6f, s * 12f - 1.4f),
                        new Vector3(rear + sign * 12f, Ground + 9f, s * 12f + 1.4f),
                        MatId.Concrete, true, 0.6f);
            }
            b.Ammo(new Vector3(rear + sign * 6f, Ground + 6.9f, -12f), AmmoKind.ShockCore);
            b.Ammo(new Vector3(rear + sign * 6f, Ground + 6.9f, 12f), AmmoKind.LinkCells);
            b.Ammo(new Vector3(rear + sign * 9f, Ground + 6.9f, -12f), AmmoKind.Rockets);
            for (int i = 0; i < 4; i++)
                b.Spawn(new Vector3(rear + sign * 15f, Ground + 0.6f, -9f + i * 6f),
                    sign > 0 ? 180f : 0f, team);

            // --- the bunker: three ways in, a jump pad up to the forward base's upper level ---
            // A roofed box with three doorways — two from the forward base, one from the rear.
            b.WallWithDoors(new Vector3(bunker - sign * 9f, Ground, -9f),
                new Vector3(bunker - sign * 8f, Ground + 5f, 9f), 3.2f, MatId.TechPanelDark, (0f, 4f));
            b.WallWithDoors(new Vector3(bunker + sign * 8f, Ground, -9f),
                new Vector3(bunker + sign * 9f, Ground + 5f, 9f), 3.2f, MatId.TechPanelDark, (-4f, 4f), (4f, 4f));
            foreach (float s in new[] { -1f, 1f })
                b.Solid(new Vector3(bunker - sign * 9f, Ground, s * 8f),
                        new Vector3(bunker + sign * 9f, Ground + 5f, s * 9f), MatId.TechPanelDark, true, 0.7f);
            b.Solid(new Vector3(bunker - sign * 9f, Ground + 5f, -9f),
                    new Vector3(bunker + sign * 9f, Ground + 5.6f, 9f), MatId.TechPanelDark, true, 0.7f);
            b.AddJumpPad(new Vector3(bunker, Ground + 0.2f, 0f), new Vector3(forward, Ground + 8.5f, 0f),
                GameTypes.TeamColor(team), 6f);
            b.Item(new Vector3(bunker - sign * 4f, Ground + 0.9f, 0f), PickupKind.DamageAmp);
            b.Ammo(new Vector3(bunker + sign * 9.5f, Ground + 5.9f, 0f), AmmoKind.RifleRounds);
            b.Ammo(new Vector3(bunker, Ground + 5.9f, -9.5f), AmmoKind.ShockCore);
            b.Ammo(new Vector3(bunker, Ground + 5.9f, 9.5f), AmmoKind.Bullets);
            b.Ammo(new Vector3(bunker + sign * 9.5f, Ground + 5.9f, 4f), AmmoKind.Rockets);

            // --- east and west buildings: Shock below the bridge, Link on the floor, vials up top ---
            foreach (float s in new[] { -1f, 1f })
            {
                float bz = s * 34f;
                b.Solid(new Vector3(bunker - sign * 14f, Ground, bz - 10f),
                        new Vector3(bunker + sign * 14f, Ground + 7f, bz + 10f), MatId.Concrete, true, 0.6f);
                b.Ramp(new Vector3(bunker - sign * 22f, Ground, bz - 5f),
                       new Vector3(bunker - sign * 14f, Ground + 7f, bz + 5f), sign > 0 ? 1 : 0,
                       MatId.Concrete, true, 0.5f);
                b.Weapon(new Vector3(bunker, Ground + 1f, bz - 12f), WeaponKind.ShockRifle);
                b.Weapon(new Vector3(bunker, Ground + 1f, bz + 12f), WeaponKind.LinkGun);
                for (int i = 0; i < 2; i++)
                    b.Ammo(new Vector3(bunker + 2f + i * 2f, Ground + 0.9f, bz - 12f), AmmoKind.ShockCore);
                for (int i = 0; i < 4; i++)
                    b.Ammo(new Vector3(bunker - 6f + i * 2f, Ground + 0.9f, bz + 12f), AmmoKind.LinkCells);
                for (int i = 0; i < 5; i++)
                    b.Item(new Vector3(bunker - 4f + i * 2f, Ground + 7.8f, bz), PickupKind.HealthVial);
                b.Item(new Vector3(bunker + sign * 10f, Ground + 7.8f, bz), PickupKind.ThighPads);
            }

            // --- forward base: Flak in each wing, Rocket at the jump-pad exit, Lightning on a rock ---
            b.Solid(new Vector3(forward - sign * 12f, Ground, -22f),
                    new Vector3(forward + sign * 12f, Ground + 8f, 22f), MatId.Concrete, true, 0.6f);
            foreach (float s in new[] { -1f, 1f })
            {
                b.Ramp(new Vector3(forward - sign * 12f, Ground, s * 26f),
                       new Vector3(forward + sign * 12f, Ground + 8f, s * 22f), s > 0 ? 3 : 2,
                       MatId.Concrete, true, 0.5f);
                b.Weapon(new Vector3(forward + sign * 16f, Ground + 1f, s * 20f), WeaponKind.FlakCannon);
                b.Ammo(new Vector3(forward + sign * 18f, Ground + 0.9f, s * 20f), AmmoKind.FlakShells);
            }
            b.Weapon(new Vector3(forward, Ground + 8.9f, 0f), WeaponKind.RocketLauncher);
            for (int i = 0; i < 2; i++)
                b.Ammo(new Vector3(forward - sign * 3f - sign * i * 2f, Ground + 8.8f, 0f), AmmoKind.Rockets);
            b.Solid(new Vector3(forward - sign * 20f, Ground, -5f), new Vector3(forward - sign * 14f, Ground + 6f, 5f),
                MatId.Rock, true, 0.7f);
            b.Weapon(new Vector3(forward - sign * 17f, Ground + 6.9f, 0f), WeaponKind.LightningGun);
            b.Item(new Vector3(forward - sign * 8f, Ground + 0.9f, -6f), PickupKind.HealthPack);
            b.Item(new Vector3(forward - sign * 8f, Ground + 0.9f, 6f), PickupKind.HealthPack);
            b.AddLight(new Vector3(rear, Ground + 14f, 0f), GameTypes.TeamColor(team) * 1.4f, 40f, 6f);
        }

        // --- the neutral zone: the ball, the Redeemer west, the Super Shield east ---
        b.AddBallSpawn(new Vector3(0f, Ground + 1.2f, 0f));
        b.Ammo(new Vector3(-3f, Ground + 0.9f, 0f), AmmoKind.LightningCells);
        b.Ammo(new Vector3(3f, Ground + 0.9f, 0f), AmmoKind.LightningCells);
        b.Solid(new Vector3(-8f, Ground, -34f), new Vector3(8f, Ground + 4f, -22f), MatId.Rock, true, 0.6f);
        b.Ramp(new Vector3(-16f, Ground, -32f), new Vector3(-8f, Ground + 4f, -24f), 1, MatId.Rock, true, 0.5f);
        b.Weapon(new Vector3(0f, Ground + 4.9f, -28f), WeaponKind.Redeemer, 0f);
        b.Solid(new Vector3(-8f, Ground, 22f), new Vector3(8f, Ground + 4f, 34f), MatId.Rock, true, 0.6f);
        b.Ramp(new Vector3(8f, Ground, 24f), new Vector3(16f, Ground + 4f, 32f), 0, MatId.Rock, true, 0.5f);
        b.Item(new Vector3(0f, Ground + 4.9f, 28f), PickupKind.ShieldBelt);
        foreach (float s in new[] { -1f, 1f })
        {
            b.Item(new Vector3(s * 12f, Ground + 0.9f, -14f), PickupKind.HealthPack);
            b.Item(new Vector3(s * 12f, Ground + 0.9f, 14f), PickupKind.HealthPack);
        }

        return b.Build(gl);
    }
}
