using System.Numerics;
using Silk.NET.OpenGL;
using Unreal99.Core;
using Unreal99.Game;
using Unreal99.Rendering;
using Unreal99.UI;

namespace Unreal99.World;

/// <summary>
/// Arenas built as homages to the layouts that made the 1999 original famous.
///
/// These are original geometry — every brush is authored here through <see cref="LevelBuilder"/>,
/// nothing is imported — but the shapes deliberately chase the feel of the classics: a pair of
/// towers staring at each other across an open void, rooftops you cross by leaping through thin
/// gravity, a symmetric starship whose flanks open onto nothing, a moonlit courtyard that touches
/// every room, an industrial hall stacked with crates, and an island cut in half by a ridge.
/// </summary>
public static partial class Maps
{
    // ================================================================ CTF-對峙世界

    /// <summary>
    /// Two identical towers at opposite ends of a narrow strip of rock adrift in orbit, joined
    /// by a split central bridge. Flags sit at each tower's base, snipers own the roofs, and the
    /// long open middle is the whole game.
    ///
    /// The towers are solid blocks with three openings punched in the face that looks down the
    /// map — entrance, a Redeemer alcove partway up, and a pillared sniping gallery above it.
    /// An earlier version climbed them with internal switchback ramps, which was wrong twice
    /// over: the original moves you between floors by teleporter (it shipped with lifts and
    /// swapped them because the bots could not use them), and the floors are separate chambers
    /// rather than one shaft. The teleporters happen to be what this engine's nav graph needs
    /// too, since ramps that steep carry no bot routes at all.
    /// </summary>
    private static Level BuildFacingWorlds(GL gl)
    {
        var b = new LevelBuilder(Loc.MapFacingWorlds, Loc.MapFacingWorldsDesc);
        var env = b.Level.Environment;
        env.SunDirection = Vector3.Normalize(new Vector3(0.24f, -0.42f, -0.87f));
        env.SunColor = new Vector3(4.4f, 4.0f, 3.4f);
        env.AmbientSky = new Vector3(0.34f, 0.38f, 0.52f);
        env.AmbientGround = new Vector3(0.14f, 0.14f, 0.17f);
        env.SkyTop = new Vector3(0.004f, 0.006f, 0.020f);
        env.SkyHorizon = new Vector3(0.03f, 0.05f, 0.14f);
        env.SkyGround = new Vector3(0.006f, 0.008f, 0.022f);
        env.StarStrength = 3.0f;
        env.CloudStrength = 0f;
        env.EnvIntensity = 0.85f;
        env.FogColor = new Vector3(0.03f, 0.04f, 0.08f);
        env.FogDensity = 0.006f;

        const float StripHalfX = 22f;      // the asteroid is narrow; stepping off is fatal
        const float TowerZ = 74f;
        const float TowerHalf = 11f;
        const float TowerTop = 42f;
        const float DeckY = 0f;

        // --- the rock the whole map sits on ---
        b.Solid(new Vector3(-StripHalfX, -4f, -TowerZ - 20f),
                new Vector3(StripHalfX, DeckY, TowerZ + 20f), MatId.Rock, true, 0.5f);
        // Tapered underside so the strip reads as an asteroid rather than a floating slab.
        b.Decor(new Vector3(-StripHalfX + 5f, -11f, -TowerZ - 14f),
                new Vector3(StripHalfX - 5f, -3.5f, TowerZ + 14f), MatId.Rock, 0.4f);
        b.Decor(new Vector3(-StripHalfX + 11f, -17f, -TowerZ + 4f),
                new Vector3(StripHalfX - 11f, -10f, TowerZ - 4f), MatId.Rock, 0.35f);

        // The underside is a broken asteroid, not a chamfered slab. Ragged spurs hang off both
        // flanks and taper away below, which is the profile the map is recognised by when you are
        // falling past it — and every one of them is decoration well outside the playable deck.
        var rockRng = new Rng(0x5FA1);
        for (int i = 0; i < 46; i++)
        {
            float pz = rockRng.Range(-TowerZ - 16f, TowerZ + 16f);
            float side = rockRng.Chance(0.5f) ? -1f : 1f;
            float w = rockRng.Range(1.8f, 5.2f);
            float drop = rockRng.Range(4f, 17f);
            float lift = rockRng.Range(-3.6f, -0.6f);
            float reach = rockRng.Range(0f, 4.5f);
            b.Decor(new Vector3(side * (StripHalfX - w * 0.5f) - side * reach * 0.2f, lift - drop, pz - w),
                    new Vector3(side * (StripHalfX + reach), lift, pz + w), MatId.Rock, 0.4f);
            // A tapered point under the widest ones, so they end in a spur rather than a stump.
            if (w < 3.4f) continue;
            b.DecorBeam(new Vector3(side * (StripHalfX + reach * 0.4f), lift - drop, pz),
                        new Vector3(side * (StripHalfX + reach * 0.9f), lift - drop - rockRng.Range(4f, 11f), pz),
                        w * 0.42f, w * 0.42f, MatId.Rock, 0.4f);
        }
        // Keel ridge running the length of the underside.
        for (int i = 0; i < 12; i++)
        {
            float pz = MathX.Lerp(-TowerZ - 12f, TowerZ + 12f, i / 11f);
            float w = 4f + MathF.Sin(i * 1.7f) * 2.6f;
            b.Decor(new Vector3(-w, -24f - MathF.Abs(MathF.Cos(i * 0.9f)) * 6f, pz - 7f),
                    new Vector3(w, -14f, pz + 7f), MatId.Rock, 0.4f);
        }

        for (int side = 0; side < 2; side++)
        {
            float sign = side == 0 ? -1f : 1f;
            Team team = side == 0 ? Team.Red : Team.Blue;
            Vector3 teamColor = GameTypes.TeamColor(team);
            MatId teamMat = team == Team.Red ? MatId.TeamRed : MatId.TeamBlue;
            float z = TowerZ * sign;

            // --- tower shell ---
            // The tower is a solid block, not a frame: the original's whole geometry budget went
            // into these two shapes, and what it has are three openings punched in the face that
            // looks down the map — the entrance at the bottom, a Redeemer alcove partway up, and
            // a pillared sniping gallery above that. You cannot see through a tower.
            const float Wall = 1.6f;
            float innerFace = z - TowerHalf * sign;    // the face looking down the map
            float outerFace = z + TowerHalf * sign;

            b.Solid(new Vector3(-TowerHalf - Wall, DeckY, MathF.Min(innerFace, outerFace)),
                    new Vector3(-TowerHalf, TowerTop, MathF.Max(innerFace, outerFace)), teamMat, true, 0.55f);
            b.Solid(new Vector3(TowerHalf, DeckY, MathF.Min(innerFace, outerFace)),
                    new Vector3(TowerHalf + Wall, TowerTop, MathF.Max(innerFace, outerFace)), teamMat, true, 0.55f);
            b.Solid(new Vector3(-TowerHalf - Wall, DeckY, MathF.Min(outerFace, outerFace + Wall * sign)),
                    new Vector3(TowerHalf + Wall, TowerTop, MathF.Max(outerFace, outerFace + Wall * sign)),
                    teamMat, true, 0.55f);

            // The towers are the map: ribbed pilasters up all four faces, banded string courses,
            // a corbelled parapet and team banners hanging down the inner face.
            //
            // Every band here is emitted as four wall strips. The first attempt wrote them as
            // filled boxes spanning the whole tower footprint, which is fine on a solid pier and
            // catastrophic on a hollow tower: six horizontal slabs sliced straight through the
            // flag room, and the inside of the tower read as a striped cage.
            Vector3 towerMid = new(0f, 0f, z);

            void Course(float y0, float y1, float outset, MatId mat, float uv, float skin = 0.7f)
            {
                float h = TowerHalf + Wall + outset;
                b.Decor(towerMid + new Vector3(-h, y0, -h), towerMid + new Vector3(-h + skin, y1, h), mat, uv);
                b.Decor(towerMid + new Vector3(h - skin, y0, -h), towerMid + new Vector3(h, y1, h), mat, uv);
                b.Decor(towerMid + new Vector3(-h, y0, -h), towerMid + new Vector3(h, y1, -h + skin), mat, uv);
                b.Decor(towerMid + new Vector3(-h, y0, h - skin), towerMid + new Vector3(h, y1, h), mat, uv);
            }

            // Pilaster ribs standing proud of each face.
            for (int i = 0; i <= 6; i++)
            {
                float u = MathX.Lerp(-TowerHalf + 1f, TowerHalf - 1f, i / 6f);
                foreach (int sz in new[] { -1, 1 })
                    b.Decor(new Vector3(u - 0.42f, DeckY, z + sz * (TowerHalf + Wall) - sz * 0.1f),
                            new Vector3(u + 0.42f, TowerTop, z + sz * (TowerHalf + Wall) + sz * 0.5f), MatId.Trim, 1.4f);
                foreach (int sx in new[] { -1, 1 })
                    b.Decor(new Vector3(sx * (TowerHalf + Wall) - sx * 0.1f, DeckY, z + u - 0.42f),
                            new Vector3(sx * (TowerHalf + Wall) + sx * 0.5f, TowerTop, z + u + 0.42f), MatId.Trim, 1.4f);
            }
            // String courses at three heights, then a corbelled cap over the parapet.
            foreach (float bandY in new[] { TowerTop * 0.30f, TowerTop * 0.58f, TowerTop * 0.82f })
                for (int step = 0; step < 2; step++)
                    Course(bandY + step * 0.34f, bandY + step * 0.34f + 0.34f, 0.55f + step * 0.28f,
                        MatId.Trim, 1.5f, 1.1f);
            for (int step = 0; step < 3; step++)
            {
                float o = 0.4f + step * 0.45f;
                b.Decor(new Vector3(-TowerHalf - Wall - o, TowerTop + step * 0.5f, z - TowerHalf - Wall - o),
                        new Vector3(TowerHalf + Wall + o, TowerTop + step * 0.5f + 0.5f, z + TowerHalf + Wall + o), MatId.Trim, 1.5f);
            }
            // Merlons round the roof, and the team's banners on the face that looks down the map.
            for (int i = 0; i < 9; i++)
            {
                float u = MathX.Lerp(-TowerHalf - Wall, TowerHalf + Wall, i / 8f);
                foreach (int sz in new[] { -1, 1 })
                    b.Decor(new Vector3(u - 1.05f, TowerTop + 1.5f, z + sz * (TowerHalf + Wall + 1.2f) - 0.7f),
                            new Vector3(u + 1.05f, TowerTop + 3.6f, z + sz * (TowerHalf + Wall + 1.2f) + 0.7f), teamMat, 1.3f);
                foreach (int sx in new[] { -1, 1 })
                    b.Decor(new Vector3(sx * (TowerHalf + Wall + 1.2f) - 0.7f, TowerTop + 1.5f, z + u - 1.05f),
                            new Vector3(sx * (TowerHalf + Wall + 1.2f) + 0.7f, TowerTop + 3.6f, z + u + 1.05f), teamMat, 1.3f);
            }
            foreach (float bx in new[] { -6.5f, 6.5f })
                Banner(b, new Vector3(bx, TowerTop * 0.80f, innerFace - Wall * sign * 1.4f), 3.4f, 11f, 1, teamMat);
            b.AddLight(new Vector3(0f, TowerTop * 0.62f, innerFace - Wall * sign * 2f), teamColor * 1.4f, 26f, 4f);

            // --- the silhouette from the official map ---
            // A plain rectangular block is the one thing these towers are not. The original reads
            // as a battered obelisk: the shaft steps inward as it rises, breaks out into a flared
            // crown near the top with team-coloured panels hung off it on brackets, and finishes
            // in a tall thin spire that is most of the map's skyline. All decoration — the solid
            // shell underneath is untouched, so the routes through the towers are exactly as they
            // were, which is what keeps the teleporters and the nav graph valid.
            // Battered shaft: six courses stepping inward, each with a shadow reveal at its base.
            for (int step = 0; step < 6; step++)
            {
                float t0 = step / 6f, t1 = (step + 1) / 6f;
                float y0 = MathX.Lerp(DeckY, TowerTop, t0);
                float y1 = MathX.Lerp(DeckY, TowerTop, t1);
                float out0 = MathX.Lerp(2.6f, 0.15f, t0);
                Course(y0, y1 - 0.5f, out0, teamMat, 0.6f);
                Course(y1 - 0.5f, y1, out0 + 0.35f, MatId.Trim, 1.3f);
            }

            // Round ports punched down the face that looks along the map, as the original has.
            for (int i = 0; i < 3; i++)
            {
                float py = MathX.Lerp(TowerTop * 0.30f, TowerTop * 0.74f, i / 2f);
                float pz = innerFace - Wall * sign * 1.2f;
                b.Torus(new Vector3(0f, py, pz), 2.1f, 0.34f, MatId.Trim, 20, 8);
                b.Torus(new Vector3(0f, py, pz), 1.55f, 0.16f, MatId.Trim, 18, 6);
                b.Decor(new Vector3(-1.5f, py - 1.5f, pz - 0.18f), new Vector3(1.5f, py + 1.5f, pz + 0.18f),
                    MatId.EnergyPanel, 0.9f);
                b.AddLight(new Vector3(0f, py, pz - sign * 1.5f), teamColor * 1.1f, 15f, 2.6f);
            }

            // Flared crown: three corbelled courses breaking out well past the shaft. Above the
            // roof deck, so these can be solid without intruding on anything.
            for (int step = 0; step < 3; step++)
            {
                float o = 1.6f + step * 1.5f;
                float y0 = TowerTop + 3.6f + step * 1.5f;
                b.Decor(towerMid + new Vector3(-TowerHalf - Wall - o, y0, -TowerHalf - Wall - o),
                        towerMid + new Vector3(TowerHalf + Wall + o, y0 + 1.5f, TowerHalf + Wall + o),
                        step == 1 ? MatId.Trim : teamMat, 1.2f);
            }
            // Team panels hung off the crown on angled brackets — the strongest colour on the map.
            for (int face = 0; face < 4; face++)
            {
                float a = face * MathX.HalfPi;
                Vector3 dir = new(MathF.Sin(a), 0f, MathF.Cos(a));
                Vector3 tan = new(dir.Z, 0f, -dir.X);
                Vector3 root = towerMid + new Vector3(0f, TowerTop + 4.4f, 0f) + dir * (TowerHalf + Wall + 1.4f);
                Vector3 tip = towerMid + new Vector3(0f, TowerTop + 8.6f, 0f) + dir * (TowerHalf + Wall + 6.4f);
                foreach (float e in new[] { -1f, 1f })
                    b.DecorBeam(root + tan * (e * 4.6f), tip + tan * (e * 5.8f), 0.30f, 0.30f, MatId.Trim, 1.3f);
                b.DecorBeam(root, tip, 5.0f, 0.16f, teamMat, 0.9f);
                b.DecorBeam(root + new Vector3(0f, 0.5f, 0f), tip + new Vector3(0f, 0.5f, 0f), 3.4f, 0.10f,
                    MatId.EnergyPanel, 0.9f);
            }

            // The spire: a tall tapered needle, the thing you navigate by from anywhere on the map.
            for (int step = 0; step < 9; step++)
            {
                float t0 = step / 9f, t1 = (step + 1) / 9f;
                float y0 = MathX.Lerp(TowerTop + 8.2f, TowerTop + 34f, t0);
                float y1 = MathX.Lerp(TowerTop + 8.2f, TowerTop + 34f, t1);
                float r0 = MathX.Lerp(5.2f, 0.30f, t0 * t0 * 0.55f + t0 * 0.45f);
                b.Decor(towerMid + new Vector3(-r0, y0, -r0), towerMid + new Vector3(r0, y1, r0),
                    step % 3 == 1 ? MatId.Trim : teamMat, 0.7f);
            }
            b.AddLight(towerMid + new Vector3(0f, TowerTop + 34f, 0f), teamColor * 1.6f, 30f, 5f, 1.5f, 0.35f);

            float f0 = MathF.Min(innerFace, innerFace - Wall * sign);
            float f1 = MathF.Max(innerFace, innerFace - Wall * sign);

            const float EntryTop = 7.5f;
            const float MidFloor = 15f, MidTop = 21f;
            const float GalleryFloor = 26f, GalleryTop = 33f;

            // Front face, emitted as horizontal bands so the three openings are simply gaps.
            void FrontBand(float y0, float y1, float holeHalf)
            {
                if (holeHalf <= 0f)
                {
                    b.Solid(new Vector3(-TowerHalf - Wall, y0, f0), new Vector3(TowerHalf + Wall, y1, f1),
                        teamMat, true, 0.55f);
                    return;
                }
                b.Solid(new Vector3(-TowerHalf - Wall, y0, f0), new Vector3(-holeHalf, y1, f1), teamMat, true, 0.55f);
                b.Solid(new Vector3(holeHalf, y0, f0), new Vector3(TowerHalf + Wall, y1, f1), teamMat, true, 0.55f);
            }
            FrontBand(DeckY, EntryTop, 5.5f);
            FrontBand(EntryTop, MidFloor + 1f, 0f);
            FrontBand(MidFloor + 1f, MidTop, 6f);
            FrontBand(MidTop, GalleryFloor + 1f, 0f);
            FrontBand(GalleryFloor + 1f, GalleryTop, 8.5f);
            FrontBand(GalleryTop, TowerTop, 0f);
            // The two pillars that split the gallery into three firing slots.
            foreach (float px in new[] { -3.0f, 3.0f })
                b.Solid(new Vector3(px - 0.7f, GalleryFloor + 1f, f0), new Vector3(px + 0.7f, GalleryTop, f1),
                    teamMat, true, 0.55f);

            // --- interior floors and ceilings ---
            void Slab(float y0, float y1, MatId mat) => b.Solid(
                new Vector3(-TowerHalf, y0, MathF.Min(innerFace, outerFace)),
                new Vector3(TowerHalf, y1, MathF.Max(innerFace, outerFace)), mat, true, 0.8f);
            Slab(11f, 12f, MatId.TechPanelDark);              // flag room ceiling
            Slab(MidFloor, MidFloor + 1f, MatId.MetalGrate);   // Redeemer alcove floor
            Slab(24f, 25f, MatId.TechPanelDark);
            Slab(GalleryFloor, GalleryFloor + 1f, MatId.MetalGrate);
            Slab(38f, 39f, MatId.TechPanelDark);
            Slab(TowerTop - 0.8f, TowerTop, MatId.TechPanelDark);   // roof deck

            // --- flag room ---
            Vector3 flagPos = new(0f, 0.6f, z + 4.5f * sign);
            b.Solid(new Vector3(-6f, DeckY, flagPos.Z - 4.5f), new Vector3(6f, 0.6f, flagPos.Z + 4.5f),
                MatId.TechPanelDark);
            b.Ramp(new Vector3(-4f, DeckY, MathF.Min(flagPos.Z - 4.5f, flagPos.Z - 7.5f)),
                   new Vector3(4f, 0.6f, MathF.Max(flagPos.Z - 4.5f, flagPos.Z - 7.5f)),
                   sign > 0 ? 2 : 3, MatId.TechFloor);
            b.AddFlagBase(flagPos, team, sign > 0 ? 180f : 0f);

            // Platform under the entrance ceiling holding the amplifier, as in the original.
            b.Solid(new Vector3(-4f, 8.2f, MathF.Min(innerFace, innerFace + 5f * sign)),
                    new Vector3(4f, 8.8f, MathF.Max(innerFace, innerFace + 5f * sign)),
                    MatId.MetalGrate, true, 0.9f);
            // Keep the optional amplifier launcher out of the centreline. At x=0 it occupied
            // the only direct CTF route out of the tower, repeatedly launching flag runners
            // back into the upper chambers instead of letting them cross the map.
            b.AddJumpPad(new Vector3(8.5f, DeckY + 0.1f, z - 7.5f * sign),
                         new Vector3(0f, 10.2f, z - 8f * sign + 5f * sign), new Vector3(0.4f, 0.85f, 1f));

            // --- three teleporters out of the flag room, exactly as the original does it ---
            // Not ramps and not lifts. The original shipped with lifts and swapped them for
            // teleporters because the bots could not cope; this engine agrees for its own reason,
            // since only pads, lifts and teleporters create nav links at all.
            Vector3 midChamber = new(0f, MidFloor + 1.2f, z + 3f * sign);
            Vector3 gallery = new(0f, GalleryFloor + 1.2f, z + 3f * sign);
            Vector3 roof = new(0f, TowerTop + 0.2f, z + 4f * sign);
            float faceIn = sign > 0 ? 180f : 0f;

            b.AddTeleporter(new Vector3(-6f, DeckY + 0.2f, z), midChamber, faceIn, teamColor * 0.6f + new Vector3(0.3f));
            b.AddTeleporter(new Vector3(6f, DeckY + 0.2f, z), gallery, faceIn, teamColor * 0.6f + new Vector3(0.3f));
            b.AddTeleporter(new Vector3(0f, DeckY + 0.2f, z + 1f * sign), roof, faceIn, new Vector3(1f, 0.75f, 0.35f));
            // Return trips. Without a way down the nav graph strands anyone it sends up, and the
            // drop from the roof to the rock is lethal.
            Vector3 lobby = new(0f, DeckY + 0.2f, z - 4f * sign);
            b.AddTeleporter(midChamber + new Vector3(-5f, 0f, 0f), lobby, faceIn, teamColor * 0.5f + new Vector3(0.25f));
            b.AddTeleporter(gallery + new Vector3(-6.5f, 0f, 0f), lobby, faceIn, teamColor * 0.5f + new Vector3(0.25f));
            b.AddTeleporter(roof + new Vector3(-7f, 0f, 0f), lobby, faceIn, teamColor * 0.5f + new Vector3(0.25f));

            // --- roof: the sniper perch, with battlements you can duck behind ---
            for (int i = -2; i <= 2; i++)
            {
                float bx = i * 4.4f;
                b.Solid(new Vector3(bx - 1.5f, TowerTop, innerFace - 1.0f * sign),
                        new Vector3(bx + 1.5f, TowerTop + 1.5f, innerFace + 0.6f * sign),
                        MatId.Trim, true, 1.1f);
            }
            b.AddLight(new Vector3(0f, TowerTop + 4f, z), teamColor, 26f, 6f);
            b.AddLight(new Vector3(0f, 6f, z), teamColor * 0.55f + new Vector3(0.35f), 20f, 4.2f);
            b.AddLight(new Vector3(0f, MidFloor + 4f, z), teamColor * 0.5f + new Vector3(0.3f), 16f, 3.4f);
            b.AddLight(new Vector3(0f, GalleryFloor + 4f, z), teamColor * 0.5f + new Vector3(0.3f), 16f, 3.4f);

            // --- loadout, matching the original's per-base inventory ---
            // Per base: Redeemer x1, Sniper Rifle x3, Ripper x1, Shock Rifle x1, Rocket Launcher
            // x1, Health Pack x4, Body Armor x1, Damage Amplifier x1, plus shock cores, rocket
            // packs and rifle rounds. Everything the map gives you is inside your own tower.
            b.Weapon(new Vector3(0f, 0.9f, z - 6f * sign), WeaponKind.ShockRifle);
            b.Weapon(new Vector3(-4f, 1.4f, flagPos.Z), WeaponKind.RocketLauncher);
            b.Weapon(new Vector3(4f, 1.4f, flagPos.Z), WeaponKind.Ripper);
            foreach (float hx in new[] { -8f, -3f, 3f, 8f })
                b.Item(new Vector3(hx, 0.7f, z - 8.5f * sign), PickupKind.HealthPack);
            b.Ammo(new Vector3(-7f, 0.7f, z - 2f * sign), AmmoKind.Rockets);
            b.Ammo(new Vector3(-9f, 0.7f, z - 2f * sign), AmmoKind.Rockets);
            b.Ammo(new Vector3(7f, 0.7f, z - 2f * sign), AmmoKind.ShockCore);
            b.Ammo(new Vector3(9f, 0.7f, z - 2f * sign), AmmoKind.ShockCore);
            b.Ammo(new Vector3(-6f, 0.7f, z + 1f * sign), AmmoKind.SniperRounds);
            b.Ammo(new Vector3(6f, 0.7f, z + 1f * sign), AmmoKind.SniperRounds);
            b.Item(new Vector3(0f, 9.7f, z - 8.5f * sign), PickupKind.DamageAmp);
            b.Ammo(new Vector3(2.5f, 9.5f, z - 8.5f * sign), AmmoKind.Rockets);
            b.Ammo(new Vector3(-2.5f, 9.5f, z - 8.5f * sign), AmmoKind.Rockets);

            b.Weapon(midChamber + new Vector3(3f, 0.7f, 0f), WeaponKind.Redeemer, respawn: 95f);
            // Two of the gallery's three firing slots hold a rifle; the third is the free spot.
            b.Weapon(gallery + new Vector3(-6f, 0.7f, 0f), WeaponKind.SniperRifle);
            b.Weapon(gallery + new Vector3(6f, 0.7f, 0f), WeaponKind.SniperRifle);
            b.Ammo(gallery + new Vector3(0f, 0.5f, 2.5f * sign), AmmoKind.SniperRounds);
            b.Weapon(roof + new Vector3(0f, 0.7f, 0f), WeaponKind.SniperRifle);
            b.Ammo(roof + new Vector3(3.5f, 0.5f, 0f), AmmoKind.SniperRounds);
            b.Item(roof + new Vector3(-3.5f, 0.6f, 0f), PickupKind.BodyArmor);

            // Clear of the flag dais, which occupies the back of the tower floor.
            for (int i = 0; i < 5; i++)
                b.Spawn(new Vector3(-8f + i * 4f, DeckY + 0.2f, z - 2f * sign), faceIn, team);
        }

        // --- the central bridge: two lanes with a gap down the middle ---
        const float BridgeY = 9f;
        foreach (float lane in new[] { -6.5f, 6.5f })
        {
            b.Solid(new Vector3(lane - 4f, BridgeY - 0.5f, -46f), new Vector3(lane + 4f, BridgeY, 46f),
                MatId.MetalGrate, true, 1.0f);
            RailRun(b, new Vector3(lane - 4f, BridgeY, -46f), new Vector3(lane - 4f, BridgeY, 46f));
            RailRun(b, new Vector3(lane + 4f, BridgeY, -46f), new Vector3(lane + 4f, BridgeY, 46f));
            // Ramps down to the strip at both ends.
            b.Ramp(new Vector3(lane - 3.5f, 0f, -58f), new Vector3(lane + 3.5f, BridgeY, -46f), 3, MatId.TechFloor);
            b.Ramp(new Vector3(lane - 3.5f, 0f, 46f), new Vector3(lane + 3.5f, BridgeY, 58f), 2, MatId.TechFloor);
        }
        // A single crossover in the very middle, so the two lanes are not fully separate worlds.
        b.Solid(new Vector3(-6.5f, BridgeY - 0.5f, -3.5f), new Vector3(6.5f, BridgeY, 3.5f),
            MatId.MetalGrate, true, 1.0f);

        // --- ground-level cover and the centre pieces worth fighting over ---
        for (int i = -1; i <= 1; i += 2)
        {
            b.Solid(new Vector3(i * 15f - 3f, 0f, -22f), new Vector3(i * 15f + 3f, 3.4f, -14f), MatId.Rock, true, 0.7f);
            b.Solid(new Vector3(i * 15f - 3f, 0f, 14f), new Vector3(i * 15f + 3f, 3.4f, 22f), MatId.Rock, true, 0.7f);
            b.AddJumpPad(new Vector3(i * 16f, 0.1f, 0f), new Vector3(i * 7.5f, BridgeY + 1.6f, 0f),
                new Vector3(0.35f, 0.8f, 1f));
        }
        // --- the two stepped pyramids out on the strip ---
        // These are in the official map's silhouette and were the one thing left out of the first
        // pass, because dropping solid geometry into a lane is what cost ONS-Dria every node
        // capture earlier. Three things make them safe here. They are mirrored, because a single
        // one on a CTF map is a balance bug rather than a landmark. They are stepped in 0.5 m
        // rises — exactly what `--collisiontest` certifies as walkable without jumping — so each
        // one is a route up, not a wall across. And at 6 m they pass under the bridge decks at
        // 8.5 m and leave 14 m of open strip either side.
        foreach (float pz in new[] { -34f, 34f })
        {
            const int Steps = 12;
            const float BaseHalf = 8f, TopHalf = 1.2f, Rise = 0.5f;
            for (int i = 0; i < Steps; i++)
            {
                float half = MathX.Lerp(BaseHalf, TopHalf, i / (float)Steps);
                b.Solid(new Vector3(-half, i * Rise, pz - half),
                        new Vector3(half, (i + 1) * Rise, pz + half),
                        i % 3 == 2 ? MatId.Trim : MatId.Rock, true, 0.7f);
            }
            // Apex marker, and a lit face on the side that looks down the map.
            float top = Steps * Rise;
            b.Decor(new Vector3(-TopHalf * 0.7f, top, pz - TopHalf * 0.7f),
                    new Vector3(TopHalf * 0.7f, top + 1.6f, pz + TopHalf * 0.7f), MatId.Trim, 1.2f);
            b.Decor(new Vector3(-0.35f, top + 1.6f, pz - 0.35f),
                    new Vector3(0.35f, top + 3.4f, pz + 0.35f), MatId.EnergyPanel, 0.9f);
            b.AddLight(new Vector3(0f, top + 3.2f, pz), new Vector3(0.55f, 0.75f, 1f), 22f, 4f);
            // Corner ribs down all four arrises, so it reads as built rather than piled.
            foreach (var (sx, sz) in new[] { (-1f, -1f), (1f, -1f), (-1f, 1f), (1f, 1f) })
                b.DecorBeam(new Vector3(sx * BaseHalf, 0.1f, pz + sz * BaseHalf),
                            new Vector3(sx * TopHalf, top, pz + sz * TopHalf), 0.30f, 0.30f, MatId.Trim, 1.2f);
        }

        // The middle holds exactly one thing: the big keg at the centre of the asteroid. No
        // weapons, no armour, no vials. Crossing it with nothing to pick up on the way, under
        // fire from two towers, is the entire map — an earlier pass had a Redeemer, a shield
        // belt and four guns strewn along the bridge, which quietly turned the crossing into
        // the safest place to shop.
        b.Item(new Vector3(0f, 0.7f, 0f), PickupKind.SuperHealth);

        return b.Build(gl);
    }

    // ================================================================ DM-摩菲斯之塔

    /// <summary>
    /// Three skyscraper roofs above a night city. Gravity is light enough that a running jump
    /// clears the gaps, and everything between the towers is a very long way down.
    /// </summary>
    private static Level BuildMorpheus(GL gl)
    {
        var b = new LevelBuilder(Loc.MapMorpheus, Loc.MapMorpheusDesc);
        // Floaty, but not so floaty that the arena cannot hold anyone. At 0.42 a standing jump
        // cleared 3.3m and a dodge carried 18m before landing, so every rail was decorative and
        // every roof leaked players into the void faster than they could fight. 0.60 keeps the
        // long, hanging leaps this map is remembered for and brings a jump down to 2.2m, which
        // a parapet can actually contain.
        b.Level.GravityScale = 0.60f;
        var env = b.Level.Environment;
        env.SunDirection = Vector3.Normalize(new Vector3(-0.5f, -0.35f, 0.6f));
        env.SunColor = new Vector3(1.3f, 1.4f, 2.0f);
        env.AmbientSky = new Vector3(0.27f, 0.30f, 0.45f);
        env.AmbientGround = new Vector3(0.11f, 0.11f, 0.15f);
        env.SkyTop = new Vector3(0.012f, 0.018f, 0.055f);
        env.SkyHorizon = new Vector3(0.10f, 0.09f, 0.20f);
        env.SkyGround = new Vector3(0.02f, 0.02f, 0.05f);
        env.StarStrength = 1.7f;
        env.CloudStrength = 0.25f;
        env.EnvIntensity = 0.7f;
        env.FogColor = new Vector3(0.05f, 0.06f, 0.12f);
        env.FogDensity = 0.012f;
        env.FogHeightFalloff = 0.03f;

        const float RoofY = 46f;
        const float Half = 15f;
        Vector3[] towers =
        [
            new(0f, 0f, -30f),
            new(-30f, 0f, 24f),
            new(30f, 0f, 24f),
        ];

        for (int t = 0; t < towers.Length; t++)
        {
            Vector3 c = towers[t];
            float roof = RoofY - t * 3.5f;   // staggered heights make the jumps read differently

            // A skyscraper is a curtain wall: mullions the full height, spandrel bands at every
            // floor, and the plant and parapet that make a roof somewhere rather than a lid.
            for (int i = 0; i <= 10; i++)
            {
                float u = MathX.Lerp(-Half, Half, i / 10f);
                foreach (int s in new[] { -1, 1 })
                {
                    b.Decor(c + new Vector3(u - 0.20f, 0f, s * Half - s * 0.35f), c + new Vector3(u + 0.20f, roof, s * Half + s * 0.15f), MatId.Trim, 1.6f);
                    b.Decor(c + new Vector3(s * Half - s * 0.35f, 0f, u - 0.20f), c + new Vector3(s * Half + s * 0.15f, roof, u + 0.20f), MatId.Trim, 1.6f);
                }
            }
            for (int floor = 1; floor * 4.2f < roof - 1f; floor++)
            {
                float y = floor * 4.2f;
                foreach (int s in new[] { -1, 1 })
                {
                    b.Decor(c + new Vector3(-Half - 0.35f, y - 0.42f, s * Half - s * 0.4f), c + new Vector3(Half + 0.35f, y, s * Half + s * 0.2f), MatId.TechPanelDark, 1.6f);
                    b.Decor(c + new Vector3(s * Half - s * 0.4f, y - 0.42f, -Half - 0.35f), c + new Vector3(s * Half + s * 0.2f, y, Half + 0.35f), MatId.TechPanelDark, 1.6f);
                }
            }
            // Parapet, roof plant and a mast with warning lamps.
            foreach (int s in new[] { -1, 1 })
            {
                b.Decor(c + new Vector3(-Half - 0.5f, roof, s * Half - s * 0.6f), c + new Vector3(Half + 0.5f, roof + 1.15f, s * Half + s * 0.4f), MatId.Trim, 1.5f);
                b.Decor(c + new Vector3(s * Half - s * 0.6f, roof, -Half - 0.5f), c + new Vector3(s * Half + s * 0.4f, roof + 1.15f, Half + 0.5f), MatId.Trim, 1.5f);
            }
            b.Decor(c + new Vector3(-4.5f, roof, -4.5f), c + new Vector3(4.5f, roof + 2.4f, 4.5f), MatId.TechPanelDark, 1.4f);
            Louvres(b, c + new Vector3(-4.2f, roof + 0.4f, -4.7f), c + new Vector3(4.2f, roof + 2.2f, -4.4f), MatId.Trim, 7);
            foreach (var (bx, bz) in new[] { (-8.5f, 7.5f), (8.5f, 7.5f), (0f, -9f) })
            {
                Barrel(b, c + new Vector3(bx, roof, bz), MatId.RustMetal, 1.5f, 0.75f);
                Pipe(b, c + new Vector3(bx, roof + 1.5f, bz), c + new Vector3(0f, roof + 1.9f, 0f), 0.18f, MatId.Trim, 5f);
            }
            Truss(b, c + new Vector3(0f, roof + 2.4f, 0f), c + new Vector3(0f, roof + 12f, 0f), 0.85f, 5, MatId.Trim, 0.12f);
            b.Sphere(c + new Vector3(0f, roof + 12.4f, 0f), 0.5f, MatId.EnergyPanel, 6, 10);
            b.AddLight(c + new Vector3(0f, roof + 12.4f, 0f), new Vector3(1f, 0.2f, 0.15f), 12f, 3f, 2.2f, 0.5f);

            // --- the building itself, tapering slightly toward the top ---
            b.Solid(new Vector3(c.X - Half, roof - 62f, c.Z - Half),
                    new Vector3(c.X + Half, roof - 0.8f, c.Z + Half), MatId.TechPanelDark, true, 0.6f);
            b.Solid(new Vector3(c.X - Half - 1.2f, roof - 0.8f, c.Z - Half - 1.2f),
                    new Vector3(c.X + Half + 1.2f, roof, c.Z + Half + 1.2f), MatId.SkyMetal, true, 0.8f);

            // Lit window bands, purely decorative but they sell the height.
            for (int floor = 1; floor <= 9; floor++)
            {
                float wy = roof - 3f - floor * 6f;
                b.Decor(new Vector3(c.X - Half - 0.15f, wy, c.Z - Half - 0.15f),
                        new Vector3(c.X + Half + 0.15f, wy + 0.7f, c.Z + Half + 0.15f), MatId.EnergyPanel, 0.35f);
            }

            // --- rooftop structures: a central block and corner vents for cover ---
            b.Solid(new Vector3(c.X - 4.5f, roof, c.Z - 4.5f), new Vector3(c.X + 4.5f, roof + 4.5f, c.Z + 4.5f),
                MatId.TechWall, true, 0.9f);
            b.Ramp(new Vector3(c.X - 3f, roof, c.Z + 4.5f), new Vector3(c.X + 3f, roof + 4.5f, c.Z + 9f),
                3, MatId.TechFloor);
            foreach (var (ox, oz) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
            {
                Vector3 v = new(c.X + ox * 10.5f, roof, c.Z + oz * 10.5f);
                b.Solid(v - new Vector3(2.2f, 0f, 2.2f), v + new Vector3(2.2f, 2.6f, 2.2f),
                    MatId.RustMetal, true, 1.1f);
            }
            // Which faces carry a launcher to another tower. Only those get an opening — an
            // earlier pass put an 11m gap in all four faces, leaving a third of every roof edge
            // as open drop, and with eight bots on three small roofs in low gravity the match
            // was decided by who fell off least. The gap is now barely wider than the pad.
            var padFaces = new List<(int nx, int nz)>();
            for (int other = 0; other < towers.Length; other++)
            {
                if (other == t) continue;
                Vector3 d = towers[other] - c;
                // The north tower sees both southern towers through the same dominant (+Z)
                // face. Give those routes separate ±X launch bays instead of stacking two pads
                // with different destinations in one trigger volume.
                bool alongX = MathF.Abs(d.X) >= MathF.Abs(d.Z) || t == 0;
                padFaces.Add(alongX
                    ? ((int)MathF.Sign(d.X), 0)
                    : (0, (int)MathF.Sign(d.Z)));
            }

            const float Parapet = 2.6f;   // above the 2.2m standing jump, so it actually stops people
            // Barely wider than the pad that sits in it (2.2m), leaving 0.4m either side against a
            // 0.84m-wide pawn. A 4.8m opening let people stroll straight past the launcher and off
            // the roof; at this width the only way through the gap is to be thrown through it.
            const float GapHalf = 1.5f;
            const float Edge = Half + 1.2f;      // the roof cap overhangs the shaft by this much
            foreach (var face in new[] { (nx: 0, nz: -1), (nx: 0, nz: 1), (nx: -1, nz: 0), (nx: 1, nz: 0) })
            {
                bool alongX = face.nz != 0;
                float fixedCoord = (alongX ? c.Z : c.X) + (alongX ? face.nz : face.nx) * (Edge - 0.6f);
                float lo = (alongX ? c.X : c.Z) - Edge;
                float hi = (alongX ? c.X : c.Z) + Edge;
                float gapCentre = alongX ? c.X : c.Z;

                void Segment(float a, float d)
                {
                    if (d - a < 0.3f) return;
                    Vector3 min = alongX ? new Vector3(a, roof, fixedCoord - 0.6f)
                                         : new Vector3(fixedCoord - 0.6f, roof, a);
                    Vector3 max = alongX ? new Vector3(d, roof + Parapet, fixedCoord + 0.6f)
                                         : new Vector3(fixedCoord + 0.6f, roof + Parapet, d);
                    b.Solid(min, max, MatId.Trim, true, 1.2f);
                }

                if (!padFaces.Contains(face)) { Segment(lo, hi); continue; }
                Segment(lo, gapCentre - GapHalf);
                Segment(gapCentre + GapHalf, hi);
            }

            // --- setback ledge, seven metres below the roof ---
            // This gravity makes a standing jump 3.3m, so no parapet low enough to see the city
            // over can actually keep anyone in — bots clear a 1.1m rail without meaning to, and
            // a full match was decided by who fell off least. Rather than wall the roof in, the
            // tower steps out beneath it. Going over the edge now costs position instead of the
            // round, and the ledge is a flanking route in its own right.
            float ledgeY = roof - 7f;
            float ledgeOut = Edge + 4.5f;
            b.Solid(new Vector3(c.X - ledgeOut, ledgeY - 0.9f, c.Z - ledgeOut),
                    new Vector3(c.X + ledgeOut, ledgeY, c.Z + ledgeOut), MatId.SkyMetal, true, 0.8f);
            // A real wall, all the way round. The first version put a 0.35m Decor strip on two of
            // the four sides — non-colliding, so the ledge caught people off the roof and then let
            // them walk straight out of it again.
            const float LedgeWall = 2.6f;
            foreach (var (wx, wz) in new[] { (wx: 0, wz: -1), (wx: 0, wz: 1), (wx: -1, wz: 0), (wx: 1, wz: 0) })
            {
                bool wallAlongX = wz != 0;
                float wallAt = (wallAlongX ? c.Z : c.X) + (wallAlongX ? wz : wx) * (ledgeOut - 0.5f);
                Vector3 wMin = wallAlongX ? new Vector3(c.X - ledgeOut, ledgeY, wallAt - 0.5f)
                                          : new Vector3(wallAt - 0.5f, ledgeY, c.Z - ledgeOut);
                Vector3 wMax = wallAlongX ? new Vector3(c.X + ledgeOut, ledgeY + LedgeWall, wallAt + 0.5f)
                                          : new Vector3(wallAt + 0.5f, ledgeY + LedgeWall, c.Z + ledgeOut);
                b.Solid(wMin, wMax, MatId.Trim, true, 1.2f);
            }
            b.AddJumpPad(new Vector3(c.X, ledgeY + 0.1f, c.Z + Edge + 2.2f),
                         new Vector3(c.X + 7f, roof + 2.5f, c.Z - 7f), new Vector3(0.4f, 0.85f, 1f));
            b.Item(new Vector3(c.X - Edge - 2.2f, ledgeY + 0.8f, c.Z), PickupKind.HealthPack);
            b.Ammo(new Vector3(c.X, ledgeY + 0.7f, c.Z - Edge - 2.2f), AmmoKind.SniperRounds);

            b.CeilingLamp(new Vector3(c.X, roof + 9f, c.Z), new Vector3(0.65f, 0.78f, 1f), 34f, 9f, 1.4f);
            b.AddLight(new Vector3(c.X, roof + 5.2f, c.Z), new Vector3(1f, 0.25f, 0.2f), 10f, 3f, 2.2f, 0.5f);

            // --- loadout ---
            // The original's list for this map is seven weapons across the whole arena — shock,
            // pulse, ripper, minigun, two rockets, one sniper, one Redeemer — and for pickups
            // only six health packs, one body armour and one invisibility. No bio rifle, no flak
            // cannon, no shield belt. Only the tallest tower gets the rifle.
            if (t == 0)
            {
                b.Weapon(new Vector3(c.X, roof + 5.4f, c.Z), WeaponKind.SniperRifle);
                b.Ammo(new Vector3(c.X + 3f, roof + 5.2f, c.Z), AmmoKind.SniperRounds);
            }
            // Keep gameplay pickups out of the solid corner vents. Placing them at the vent
            // centres embedded the models inside collision and mapped their nav goals to the
            // inaccessible vent tops, so dry bots could never re-arm.
            b.Weapon(new Vector3(c.X - 10.5f, roof + 0.9f, c.Z),
                t == 0 ? WeaponKind.RocketLauncher : t == 1 ? WeaponKind.RocketLauncher : WeaponKind.ShockRifle);
            b.Weapon(new Vector3(c.X + 10.5f, roof + 0.9f, c.Z),
                t == 0 ? WeaponKind.Minigun : t == 1 ? WeaponKind.PulseGun : WeaponKind.Ripper);
            if (t == 0) b.Item(new Vector3(c.X - 10.5f, roof + 0.8f, c.Z + 5.5f), PickupKind.BodyArmor);
            if (t == 2) b.Item(new Vector3(c.X + 10.5f, roof + 0.8f, c.Z - 5.5f), PickupKind.Invisibility);
            b.Item(new Vector3(c.X - 5.5f, roof + 0.8f, c.Z - 10.5f), PickupKind.HealthPack);
            b.Item(new Vector3(c.X + 5.5f, roof + 0.8f, c.Z + 10.5f), PickupKind.HealthPack);
            b.Ammo(new Vector3(c.X, roof + 0.7f, c.Z - 11f), AmmoKind.Rockets);
            b.Ammo(new Vector3(c.X, roof + 0.7f, c.Z + 11f), AmmoKind.MinigunBullets);

            for (int i = 0; i < 4; i++)
            {
                float a = i / 4f * MathX.TwoPi + 0.6f;
                b.Spawn(new Vector3(c.X + MathF.Cos(a) * 8f, roof + 0.2f, c.Z + MathF.Sin(a) * 8f),
                    -a * MathX.Rad2Deg + 180f);
            }
        }

        // --- pads linking the three roofs ---
        // Without these the towers are three disconnected islands. Nothing in the nav graph can
        // express a 40m leap — only pads, lifts and teleporters create links — so bots could never
        // path between roofs, and the parapet gaps, which are deliberately aimed at the
        // neighbouring towers, became the precise spot they walked off into the void. Three of
        // eight bots were dead inside ten seconds. Each pad sits in the gap that faces its target,
        // so the launch is clear of the parapet and the gap now reads as a launcher.
        for (int from = 0; from < towers.Length; from++)
        {
            float roofFrom = RoofY - from * 3.5f;
            for (int to = 0; to < towers.Length; to++)
            {
                if (to == from) continue;
                float roofTo = RoofY - to * 3.5f;
                Vector3 delta = towers[to] - towers[from];
                // Launch from whichever face points most directly at the target, so the two pads
                // on a roof never land on the same gap.
                bool alongX = MathF.Abs(delta.X) >= MathF.Abs(delta.Z) || from == 0;
                Vector3 pad = towers[from] + (alongX
                    ? new Vector3(MathF.Sign(delta.X) * (Half - 1.5f), 0f, 0f)
                    : new Vector3(0f, 0f, MathF.Sign(delta.Z) * (Half - 1.5f)));
                // Land short of the target's centre block, on the side facing the source.
                Vector3 approach = MathX.SafeNormalize(-delta.FlatXZ(), MathX.Forward);
                Vector3 dest = towers[to] + approach * 10f;
                b.AddJumpPad(new Vector3(pad.X, roofFrom + 0.1f, pad.Z),
                    new Vector3(dest.X, roofTo + 2.5f, dest.Z), new Vector3(0.4f, 0.85f, 1f));
            }
        }

        // --- the prize sits on a small platform in the dead centre, reachable only by leaping ---
        b.Solid(new Vector3(-4f, 30f, 2f), new Vector3(4f, 31f, 10f), MatId.Trim, true, 0.9f);
        b.Torus(new Vector3(0f, 32.4f, 6f), 3.2f, 0.22f, MatId.EnergyPanel, 20, 8);
        b.AddLight(new Vector3(0f, 33.5f, 6f), new Vector3(1f, 0.6f, 0.2f), 22f, 7f);
        b.Weapon(new Vector3(0f, 31.9f, 6f), WeaponKind.Redeemer, 95f);
        b.Item(new Vector3(0f, 31.8f, 9f), PickupKind.HealthPack);

        // Pads back up from the centre platform, otherwise it is a one-way trip.
        b.AddJumpPad(new Vector3(0f, 31.1f, 3f), new Vector3(0f, RoofY + 3f, -22f),
            new Vector3(0.4f, 0.85f, 1f));

        // --- distant skyline: pure decoration far below the play space ---
        var rng = new Rng(0x5C1F1);
        for (int i = 0; i < 26; i++)
        {
            float a = rng.Range(0f, MathX.TwoPi);
            float r = rng.Range(58f, 118f);
            float h = rng.Range(6f, 34f);
            Vector3 p = new(MathF.Cos(a) * r, 0f, MathF.Sin(a) * r);
            float w = rng.Range(4f, 9f);
            b.Decor(new Vector3(p.X - w, -46f, p.Z - w), new Vector3(p.X + w, -46f + h, p.Z + w),
                MatId.TechPanelDark, 0.6f);
        }

        return b.Build(gl);
    }

    // ================================================================ DM-超載星艦

    /// <summary>
    /// A warship's spine: three decks joined by ramps, mirrored about the centre, with the
    /// flanks open to space. Knocking someone out of a side port is half the fun.
    /// </summary>
    private static Level BuildHyperBlast(GL gl)
    {
        var b = new LevelBuilder(Loc.MapHyperBlast, Loc.MapHyperBlastDesc);
        var env = b.Level.Environment;
        env.SunDirection = Vector3.Normalize(new Vector3(0.62f, -0.30f, 0.72f));
        env.SunColor = new Vector3(3.0f, 3.0f, 3.6f);
        env.AmbientSky = new Vector3(0.34f, 0.36f, 0.46f);
        env.AmbientGround = new Vector3(0.14f, 0.14f, 0.16f);
        env.SkyTop = new Vector3(0.006f, 0.008f, 0.024f);
        env.SkyHorizon = new Vector3(0.05f, 0.06f, 0.16f);
        env.SkyGround = new Vector3(0.01f, 0.012f, 0.03f);
        env.StarStrength = 2.6f;
        env.CloudStrength = 0f;
        env.EnvIntensity = 0.9f;
        env.FogColor = new Vector3(0.04f, 0.05f, 0.09f);
        env.FogDensity = 0.008f;

        const float HullHalfX = 15f;
        const float HullHalfZ = 44f;
        const float Lower = 0f, Mid = 7f, Upper = 14f;

        // --- lower deck: the hull floor, open at both flanks ---
        b.Solid(new Vector3(-HullHalfX, Lower - 1.4f, -HullHalfZ), new Vector3(HullHalfX, Lower, HullHalfZ),
            MatId.SkyMetal, true, 0.7f);
        // Prow and stern taper.
        b.Solid(new Vector3(-9f, Lower - 1.4f, -HullHalfZ - 12f), new Vector3(9f, Lower, -HullHalfZ),
            MatId.SkyMetal, true, 0.7f);
        b.Solid(new Vector3(-9f, Lower - 1.4f, HullHalfZ), new Vector3(9f, Lower, HullHalfZ + 12f),
            MatId.SkyMetal, true, 0.7f);

        // A starship's interior is its frame: transverse rib arches the length of the hull, a
        // keel girder down the spine, and conduit banks following both flanks between decks.
        DressHull(b, new Vector3(-HullHalfX, Lower, -HullHalfZ), new Vector3(HullHalfX, Upper + 6f, HullHalfZ),
            MatId.SkyMetal, MatId.Trim, 14);
        Girder(b, new Vector3(0f, Lower - 1.6f, -HullHalfZ - 12f), new Vector3(0f, Lower - 1.6f, HullHalfZ + 12f), 0.5f, MatId.SkyMetal);
        for (int s = -1; s <= 1; s += 2)
        {
            Pipe(b, new Vector3(s * (HullHalfX - 0.8f), Mid - 1.2f, -HullHalfZ), new Vector3(s * (HullHalfX - 0.8f), Mid - 1.2f, HullHalfZ), 0.24f, MatId.Trim, 7f);
            Pipe(b, new Vector3(s * (HullHalfX - 0.8f), Upper - 1.2f, -HullHalfZ), new Vector3(s * (HullHalfX - 0.8f), Upper - 1.2f, HullHalfZ), 0.2f, MatId.RustMetal, 7f);
            // Hull plate seams and rivets along the flanks.
            BoltLine(b, new Vector3(s * (HullHalfX - 0.25f), Lower + 0.5f, -HullHalfZ + 2f), new Vector3(s * (HullHalfX - 0.25f), Lower + 0.5f, HullHalfZ - 2f), 0.085f, MatId.Trim, 3f);
            for (int i = 0; i < 8; i++)
            {
                float z = MathX.Lerp(-HullHalfZ + 4f, HullHalfZ - 4f, i / 7f);
                WallLamp(b, new Vector3(s * (HullHalfX - 0.5f), Mid + 2.6f, z), 0, MatId.Trim, new Vector3(0.6f, 0.8f, 1f), 12f, 2.4f);
            }
        }
        // Prow framing, so the nose is a shape rather than a wedge.
        for (int i = 0; i < 6; i++)
        {
            float t = i / 5f;
            float w = MathX.Lerp(9f, 1.6f, t);
            foreach (int s in new[] { -1, 1 })
            {
                b.DecorBeam(new Vector3(s * w, Lower, -HullHalfZ - 12f * t), new Vector3(s * w, Lower + 3.4f - t * 1.6f, -HullHalfZ - 12f * t), 0.22f, 0.22f, MatId.SkyMetal, 1.4f);
                b.DecorBeam(new Vector3(s * w, Lower, HullHalfZ + 12f * t), new Vector3(s * w, Lower + 3.4f - t * 1.6f, HullHalfZ + 12f * t), 0.22f, 0.22f, MatId.SkyMetal, 1.4f);
            }
        }

        // --- the two end rooms and the spine corridor between them ---
        for (int end = 0; end < 2; end++)
        {
            float sign = end == 0 ? -1f : 1f;
            float z = 30f * sign;

            // Room walls, leaving the flanks open on purpose.
            b.Solid(new Vector3(-HullHalfX, Lower, z - 11f), new Vector3(-HullHalfX + 1.4f, Mid, z + 11f),
                MatId.TechWall, true, 0.7f);
            b.Solid(new Vector3(HullHalfX - 1.4f, Lower, z - 11f), new Vector3(HullHalfX, Mid, z + 11f),
                MatId.TechWall, true, 0.7f);
            float back = z + 11f * sign;
            b.Solid(new Vector3(-HullHalfX, Lower, MathF.Min(back, back - 1.4f * sign)),
                    new Vector3(HullHalfX, Mid, MathF.Max(back, back - 1.4f * sign)), MatId.TechWall, true, 0.7f);

            // Mid-deck balcony wrapping the room, reached by a ramp on each side.
            b.Solid(new Vector3(-HullHalfX, Mid - 0.5f, z - 11f), new Vector3(-8f, Mid, z + 11f),
                MatId.MetalGrate, true, 0.9f);
            b.Solid(new Vector3(8f, Mid - 0.5f, z - 11f), new Vector3(HullHalfX, Mid, z + 11f),
                MatId.MetalGrate, true, 0.9f);
            b.Ramp(new Vector3(-8f, Lower, z - 4f), new Vector3(-1.5f, Mid, z + 4f), 1, MatId.TechFloor);
            b.Ramp(new Vector3(1.5f, Lower, z - 4f), new Vector3(8f, Mid, z + 4f), 0, MatId.TechFloor);
            // Overlap the high ends with the balcony by more than one pawn radius. Exact
            // ramp-to-box seams left the capsule pressed against x=+/-8 even though the nav
            // graph correctly saw a continuous floor, causing rapid reversals at the opening.
            b.Solid(new Vector3(-9.5f, Mid - 0.5f, z - 5f),
                new Vector3(-7.2f, Mid, z + 5f), MatId.MetalGrate, true, 0.9f);
            b.Solid(new Vector3(7.2f, Mid - 0.5f, z - 5f),
                new Vector3(9.5f, Mid, z + 5f), MatId.MetalGrate, true, 0.9f);
            // Leave the inner rails open where the lower-to-mid ramps meet the balcony. A
            // continuous rail across this span trapped bots on the deck at x=+/-8 while their
            // valid route continued through the ramp entrance on the other side.
            foreach (float railX in new[] { -8f, 8f })
            {
                RailRun(b, new Vector3(railX, Mid, z - 11f), new Vector3(railX, Mid, z - 5f));
                RailRun(b, new Vector3(railX, Mid, z + 5f), new Vector3(railX, Mid, z + 11f));
            }

            b.CeilingLamp(new Vector3(0f, Mid + 7.5f, z), new Vector3(0.75f, 0.85f, 1f), 26f, 7.5f, 1.4f);
            b.Weapon(new Vector3(0f, Lower + 0.9f, z + 6f * sign),
                end == 0 ? WeaponKind.FlakCannon : WeaponKind.RocketLauncher);
            b.Weapon(new Vector3(-11.5f, Mid + 0.9f, z), end == 0 ? WeaponKind.Minigun : WeaponKind.PulseGun);
            b.Weapon(new Vector3(11.5f, Mid + 0.9f, z), end == 0 ? WeaponKind.Ripper : WeaponKind.BioRifle);
            b.Ammo(new Vector3(-11.5f, Mid + 0.7f, z + 4f), AmmoKind.MinigunBullets);
            b.Ammo(new Vector3(11.5f, Mid + 0.7f, z + 4f), AmmoKind.Blades);
            b.Item(new Vector3(0f, Lower + 0.8f, z - 6f * sign),
                end == 0 ? PickupKind.BodyArmor : PickupKind.ThighPads);
            for (int i = 0; i < 3; i++)
                b.Spawn(new Vector3(-6f + i * 6f, Lower + 0.2f, z), sign > 0 ? 180f : 0f);
            b.Spawn(new Vector3(0f, Mid + 0.2f, z + 8f * sign), sign > 0 ? 180f : 0f);
        }

        // --- the spine: a narrow corridor connecting both rooms, flanked by open drops ---
        // Carry the spine walls up to the upper catwalk. Their old seven-metre caps were
        // walkable but disconnected navigation islands: a knocked bot could land there and
        // stand forever because no route left the 1.4-metre strip. At upper-deck height these
        // structural walls meet the catwalk and every sampled top surface has a way home.
        b.Solid(new Vector3(-6f, Lower, -19f), new Vector3(-4.6f, Upper, 19f), MatId.TechWall, true, 0.7f);
        b.Solid(new Vector3(4.6f, Lower, -19f), new Vector3(6f, Upper, 19f), MatId.TechWall, true, 0.7f);

        // --- upper deck: a catwalk spanning the whole ship, the sniper's road ---
        // The jump pads below launch from z=+/-12 toward z=+/-4 and cross this deck plane near
        // z=+/-9. A single uninterrupted slab made both ballistic routes hit its underside and
        // fall the full fourteen metres back to the lower deck. Cut two centre apertures while
        // retaining broad side strips, so the launch is clear and the catwalk remains walkable.
        b.Solid(new Vector3(-4.5f, Upper - 0.5f, -34f), new Vector3(4.5f, Upper, -11f),
            MatId.MetalGrate, true, 1.0f);
        b.Solid(new Vector3(-4.5f, Upper - 0.5f, -7f), new Vector3(4.5f, Upper, 7f),
            MatId.MetalGrate, true, 1.0f);
        b.Solid(new Vector3(-4.5f, Upper - 0.5f, 11f), new Vector3(4.5f, Upper, 34f),
            MatId.MetalGrate, true, 1.0f);
        foreach (float gapZ in new[] { -9f, 9f })
        {
            b.Solid(new Vector3(-4.5f, Upper - 0.5f, gapZ - 2f),
                new Vector3(-1.6f, Upper, gapZ + 2f), MatId.MetalGrate, true, 1.0f);
            b.Solid(new Vector3(1.6f, Upper - 0.5f, gapZ - 2f),
                new Vector3(4.5f, Upper, gapZ + 2f), MatId.MetalGrate, true, 1.0f);
        }
        RailRun(b, new Vector3(-4.5f, Upper, -34f), new Vector3(-4.5f, Upper, 34f));
        RailRun(b, new Vector3(4.5f, Upper, -34f), new Vector3(4.5f, Upper, 34f));
        for (int end = 0; end < 2; end++)
        {
            float sign = end == 0 ? -1f : 1f;
            b.Ramp(new Vector3(-4f, Mid, MathF.Min(34f * sign, 26f * sign)),
                   new Vector3(4f, Upper, MathF.Max(34f * sign, 26f * sign)), sign > 0 ? 2 : 3, MatId.TechFloor);
        }
        // Seven weapons, no sniper rifle and no Redeemer; the invisibility sits in the middle of
        // the upper corridor, which is the original's one power-up.
        b.Item(new Vector3(0f, Upper + 0.8f, 0f), PickupKind.Invisibility);
        b.Item(new Vector3(0f, Upper + 0.8f, -8f), PickupKind.ShieldBelt);
        b.Item(new Vector3(0f, Upper + 0.8f, 8f), PickupKind.BodyArmor);
        b.Weapon(new Vector3(0f, Lower + 0.9f, 0f), WeaponKind.ShockRifle);
        b.Weapon(new Vector3(0f, Lower + 0.9f, -14f), WeaponKind.FlakCannon);
        for (int i = 0; i < 8; i++)
            b.Item(new Vector3(-10f + (i % 4) * 6.5f, Lower + 0.7f, i < 4 ? -10f : 10f), PickupKind.HealthPack);
        b.AddJumpPad(new Vector3(0f, Lower + 0.1f, 12f), new Vector3(0f, Upper + 1.8f, 4f),
            new Vector3(0.4f, 0.85f, 1f));
        b.AddJumpPad(new Vector3(0f, Lower + 0.1f, -12f), new Vector3(0f, Upper + 1.8f, -4f),
            new Vector3(0.4f, 0.85f, 1f));

        // --- engines and hull detail ---
        for (int i = -1; i <= 1; i += 2)
        {
            b.Cylinder(new Vector3(i * 7f, 2.5f, HullHalfZ + 13f), 3.2f, 2.4f, 5f, 14, MatId.RustMetal);
            b.AddLight(new Vector3(i * 7f, 2.5f, HullHalfZ + 16f), new Vector3(0.4f, 0.7f, 1f), 20f, 8f, 3f, 0.2f);
        }
        b.Solid(new Vector3(-9f, Lower, -HullHalfZ - 12f), new Vector3(9f, Mid - 1f, -HullHalfZ - 10.5f),
            MatId.TechWall, true, 0.7f);
        for (int i = 0; i < 5; i++)
        {
            float z = -30f + i * 15f;
            b.Item(new Vector3(-12f, Lower + 0.6f, z), PickupKind.HealthVial);
            b.Item(new Vector3(12f, Lower + 0.6f, z), PickupKind.HealthVial);
        }
        b.Spawn(new Vector3(0f, Upper + 0.2f, 0f), 90f);

        return b.Build(gl);
    }

    // ================================================================ DM-哥德庭園

    /// <summary>
    /// A moonlit palace built around a two-level courtyard that every room opens onto.
    /// Stone, gold trim and firelight, with a purple sky overhead.
    /// </summary>
    private static Level BuildGothic(GL gl)
    {
        var b = new LevelBuilder(Loc.MapGothic, Loc.MapGothicDesc);
        var env = b.Level.Environment;
        env.SunDirection = Vector3.Normalize(new Vector3(-0.25f, -0.80f, 0.55f));
        env.SunColor = new Vector3(1.5f, 1.4f, 2.3f);
        env.AmbientSky = new Vector3(0.26f, 0.22f, 0.38f);
        env.AmbientGround = new Vector3(0.10f, 0.07f, 0.09f);
        env.SkyTop = new Vector3(0.05f, 0.025f, 0.11f);
        env.SkyHorizon = new Vector3(0.22f, 0.12f, 0.30f);
        env.SkyGround = new Vector3(0.04f, 0.025f, 0.06f);
        env.StarStrength = 1.5f;
        env.CloudStrength = 0.45f;
        env.EnvIntensity = 0.4f;
        env.FogColor = new Vector3(0.12f, 0.09f, 0.18f);
        env.FogSunColor = new Vector3(0.65f, 0.45f, 0.9f);
        env.FogDensity = 0.020f;

        const float H = 34f;
        const float WallTop = 22f;
        const float Gallery = 8.5f;

        // --- courtyard floor, genuinely sunken in the middle ---
        // The former single full-size slab still covered the alleged sunken court. Its Redeemer
        // and super-health sat below that intact floor, so bots could reach the nearest nav node
        // but never touch the pickup and oscillated around it forever. Build the outer floor as
        // four slabs and descend through four visible one-metre rings into the open centre.
        const float StairOuter = 14f;
        b.Solid(new Vector3(-H, -1.5f, -H), new Vector3(H, 0f, -StairOuter), MatId.Concrete, true, 0.55f);
        b.Solid(new Vector3(-H, -1.5f, StairOuter), new Vector3(H, 0f, H), MatId.Concrete, true, 0.55f);
        b.Solid(new Vector3(-H, -1.5f, -StairOuter), new Vector3(-StairOuter, 0f, StairOuter), MatId.Concrete, true, 0.55f);
        b.Solid(new Vector3(StairOuter, -1.5f, -StairOuter), new Vector3(H, 0f, StairOuter), MatId.Concrete, true, 0.55f);
        b.Solid(new Vector3(-11f, -3.5f, -11f), new Vector3(11f, -2f, 11f), MatId.Concrete, true, 0.55f);
        for (int i = 0; i < 4; i++)
        {
            float outer = StairOuter - i;
            float inner = outer - 1f;
            float top = -0.5f * i;
            b.Solid(new Vector3(-outer, -2.5f, -outer), new Vector3(outer, top, -inner), MatId.Concrete);
            b.Solid(new Vector3(-outer, -2.5f, inner), new Vector3(outer, top, outer), MatId.Concrete);
            b.Solid(new Vector3(-outer, -2.5f, -inner), new Vector3(-inner, top, inner), MatId.Concrete);
            b.Solid(new Vector3(inner, -2.5f, -inner), new Vector3(outer, top, inner), MatId.Concrete);
        }

        // --- outer walls ---
        b.Solid(new Vector3(-H - 2f, -2f, -H - 2f), new Vector3(-H, WallTop, H + 2f), MatId.Rock, true, 0.5f);
        b.Solid(new Vector3(H, -2f, -H - 2f), new Vector3(H + 2f, WallTop, H + 2f), MatId.Rock, true, 0.5f);
        b.Solid(new Vector3(-H, -2f, -H - 2f), new Vector3(H, WallTop, -H), MatId.Rock, true, 0.5f);
        b.Solid(new Vector3(-H, -2f, H), new Vector3(H, WallTop, H + 2f), MatId.Rock, true, 0.5f);

        // --- the gallery ring: a covered walkway around the courtyard on arches ---
        b.Solid(new Vector3(-H, Gallery - 0.6f, -H), new Vector3(H, Gallery, -H + 8f), MatId.Concrete, true, 0.6f);
        b.Solid(new Vector3(-H, Gallery - 0.6f, H - 8f), new Vector3(H, Gallery, H), MatId.Concrete, true, 0.6f);
        b.Solid(new Vector3(-H, Gallery - 0.6f, -H + 8f), new Vector3(-H + 8f, Gallery, H - 8f), MatId.Concrete, true, 0.6f);
        b.Solid(new Vector3(H - 8f, Gallery - 0.6f, -H + 8f), new Vector3(H, Gallery, H - 8f), MatId.Concrete, true, 0.6f);
        RailRun(b, new Vector3(-H + 8f, Gallery, -H + 8f), new Vector3(H - 8f, Gallery, -H + 8f));
        RailRun(b, new Vector3(-H + 8f, Gallery, H - 8f), new Vector3(H - 8f, Gallery, H - 8f));
        RailRun(b, new Vector3(-H + 8f, Gallery, -H + 8f), new Vector3(-H + 8f, Gallery, H - 8f));
        RailRun(b, new Vector3(H - 8f, Gallery, -H + 8f), new Vector3(H - 8f, Gallery, H - 8f));

        // Arcade columns holding the gallery up, with a brazier on every other one.
        for (int i = 0; i < 7; i++)
        {
            float t = -27f + i * 9f;
            foreach (var (px, pz) in new[] { (t, -H + 8f), (t, H - 8f), (-H + 8f, t), (H - 8f, t) })
            {
                b.Prism(new Vector3(px, Gallery * 0.5f, pz), 1.5f, Gallery, 8, MatId.Concrete);
                b.Prism(new Vector3(px, Gallery + 6.5f, pz), 1.2f, 13f, 8, MatId.Concrete);
                if (i % 2 != 0) continue;
                b.Decor(new Vector3(px - 0.7f, Gallery + 1.2f, pz - 0.7f),
                        new Vector3(px + 0.7f, Gallery + 2.2f, pz + 0.7f), MatId.Lava, 0.9f);
                b.AddLight(new Vector3(px, Gallery + 2.6f, pz), new Vector3(1f, 0.52f, 0.16f), 15f, 4.0f, 5f, 0.28f);
            }
        }

        // --- the arcade gets the arches it is named for ---
        // The gallery was described as a covered walkway "on arches" and stood on bare octagonal
        // posts. Arcades are the whole visual identity of this arena, so the bays are sprung twice:
        // once under the gallery deck, once between the upper columns carrying the roofline.
        for (int i = 0; i < 6; i++)
        {
            float mid = -27f + i * 9f + 4.5f;
            foreach (var (ax, az, axis) in new[]
                     {
                         (mid, -H + 8f, 0), (mid, H - 8f, 0), (-H + 8f, mid, 1), (H - 8f, mid, 1),
                     })
            {
                Arch(b, new Vector3(ax, Gallery - 3.1f, az), 9f, 2.5f, 1.5f, axis, MatId.Concrete, 9, 0.36f);
                Arch(b, new Vector3(ax, Gallery + 10.4f, az), 9f, 2.9f, 1.2f, axis, MatId.Concrete, 9, 0.30f);
                // Keystone.
                b.Decor(new Vector3(ax - 0.42f, Gallery - 0.75f, az - 1.6f),
                        new Vector3(ax + 0.42f, Gallery - 0.05f, az + 1.6f), MatId.Trim, 1.4f);
            }
        }

        // Bases and capitals on every arcade post, and a moulded string course at deck level.
        for (int i = 0; i < 7; i++)
        {
            float t = -27f + i * 9f;
            foreach (var (px, pz) in new[] { (t, -H + 8f), (t, H - 8f), (-H + 8f, t), (H - 8f, t) })
            {
                b.Decor(new Vector3(px - 2.1f, 0f, pz - 2.1f), new Vector3(px + 2.1f, 0.5f, pz + 2.1f), MatId.Trim, 1.4f);
                b.Decor(new Vector3(px - 1.8f, 0.5f, pz - 1.8f), new Vector3(px + 1.8f, 0.85f, pz + 1.8f), MatId.Trim, 1.4f);
                b.Torus(new Vector3(px, Gallery - 0.95f, pz), 1.62f, 0.16f, MatId.Trim, 14, 6);
                b.Decor(new Vector3(px - 1.95f, Gallery - 0.62f, pz - 1.95f),
                        new Vector3(px + 1.95f, Gallery - 0.2f, pz + 1.95f), MatId.Trim, 1.4f);
                b.Torus(new Vector3(px, Gallery + 12.9f, pz), 1.32f, 0.13f, MatId.Trim, 14, 6);
            }
        }

        // --- outer wall: string courses, buttresses, traceried windows and a crenellated top ---
        foreach (int s in new[] { -1, 1 })
        {
            for (int i = 0; i < 7; i++)
            {
                float t = -27f + i * 9f;
                Buttress(b, new Vector3(t, -2f, s * H), WallTop - 2f, new Vector3(0f, 0f, s * 2.6f), MatId.Rock);
                Buttress(b, new Vector3(s * H, -2f, t), WallTop - 2f, new Vector3(s * 2.6f, 0f, 0f), MatId.Rock);
                if (i % 2 == 0) continue;
                Window(b, new Vector3(t - 2.6f, Gallery + 3f, s * H - 0.4f),
                    new Vector3(t + 2.6f, Gallery + 9.5f, s * H + 0.4f), 1, MatId.Trim, 2);
                Window(b, new Vector3(s * H - 0.4f, Gallery + 3f, t - 2.6f),
                    new Vector3(s * H + 0.4f, Gallery + 9.5f, t + 2.6f), 0, MatId.Trim, 2);
            }
            // String course, then merlons along the parapet.
            b.Decor(new Vector3(-H - 2.4f, Gallery + 0.9f, s * H - 0.3f), new Vector3(H + 2.4f, Gallery + 1.5f, s * H + 2.3f), MatId.Trim, 1.5f);
            b.Decor(new Vector3(s * H - 0.3f, Gallery + 0.9f, -H - 2.4f), new Vector3(s * H + 2.3f, Gallery + 1.5f, H + 2.4f), MatId.Trim, 1.5f);
            b.Decor(new Vector3(-H - 2.4f, WallTop - 0.9f, s * H - 0.4f), new Vector3(H + 2.4f, WallTop - 0.2f, s * H + 2.4f), MatId.Trim, 1.5f);
            b.Decor(new Vector3(s * H - 0.4f, WallTop - 0.9f, -H - 2.4f), new Vector3(s * H + 2.4f, WallTop - 0.2f, H + 2.4f), MatId.Trim, 1.5f);
            for (int i = 0; i < 19; i++)
            {
                float t = -H - 1f + i * 3.8f;
                b.Decor(new Vector3(t, WallTop - 0.2f, s * H - 0.4f), new Vector3(t + 2f, WallTop + 1.5f, s * H + 2.4f), MatId.Rock, 1.3f);
                b.Decor(new Vector3(s * H - 0.4f, WallTop - 0.2f, t), new Vector3(s * H + 2.4f, WallTop + 1.5f, t + 2f), MatId.Rock, 1.3f);
            }
        }

        // --- stairs up to the gallery at the four corners ---
        b.Stairs(new Vector3(-H + 4f, 0f, -H + 10f), new Vector3(-H + 4f, Gallery, -H + 20f), 6f, 14, MatId.Concrete, false);
        b.Stairs(new Vector3(H - 4f, 0f, H - 10f), new Vector3(H - 4f, Gallery, H - 20f), 6f, 14, MatId.Concrete, false);
        b.Stairs(new Vector3(-H + 10f, 0f, H - 4f), new Vector3(-H + 20f, Gallery, H - 4f), 6f, 14, MatId.Concrete);
        b.Stairs(new Vector3(H - 10f, 0f, -H + 4f), new Vector3(H - 20f, Gallery, -H + 4f), 6f, 14, MatId.Concrete);

        // --- the raised bridge crossing the courtyard, and the altar beneath it ---
        b.Solid(new Vector3(-4f, Gallery + 5f, -H + 8f), new Vector3(4f, Gallery + 5.6f, H - 8f),
            MatId.Trim, true, 0.8f);
        RailRun(b, new Vector3(-4f, Gallery + 5.6f, -H + 8f), new Vector3(-4f, Gallery + 5.6f, H - 8f));
        RailRun(b, new Vector3(4f, Gallery + 5.6f, -H + 8f), new Vector3(4f, Gallery + 5.6f, H - 8f));
        b.Ramp(new Vector3(-4f, Gallery, -H + 16f), new Vector3(4f, Gallery + 5f, -H + 8f), 3, MatId.Concrete);
        b.Ramp(new Vector3(-4f, Gallery, H - 16f), new Vector3(4f, Gallery + 5f, H - 8f), 2, MatId.Concrete);

        b.Prism(new Vector3(0f, -1f, 0f), 3.4f, 3f, 6, MatId.Concrete);
        b.Sphere(new Vector3(0f, 2.4f, 0f), 1.15f, MatId.EnergyPanel, 12, 18);
        b.AddLight(new Vector3(0f, 2.4f, 0f), new Vector3(0.6f, 0.35f, 1f), 22f, 7f, 2f, 0.1f);

        // --- placements ---
        b.Weapon(new Vector3(0f, 0.4f, 0f), WeaponKind.RocketLauncher);
        b.Item(new Vector3(0f, Gallery + 6.5f, 0f), PickupKind.ShieldBelt);
        b.Weapon(new Vector3(0f, Gallery + 6.6f, -12f), WeaponKind.SniperRifle);
        b.Weapon(new Vector3(0f, Gallery + 6.6f, 12f), WeaponKind.Ripper);
        b.Weapon(new Vector3(-26f, 0.9f, -26f), WeaponKind.FlakCannon);
        b.Weapon(new Vector3(26f, 0.9f, 26f), WeaponKind.Minigun);
        b.Weapon(new Vector3(26f, 0.9f, -26f), WeaponKind.ShockRifle);
        b.Weapon(new Vector3(-26f, 0.9f, 26f), WeaponKind.PulseGun);
        b.Weapon(new Vector3(-28f, Gallery + 0.9f, 0f), WeaponKind.BioRifle);
        // Doubled entries from the original's list — shock, minigun and flak all appear twice —
        // plus the Redeemer it carries. No invisibility and no Enforcer on this one.
        b.Weapon(new Vector3(28f, Gallery + 0.9f, 0f), WeaponKind.ShockRifle);
        b.Weapon(new Vector3(-26f, Gallery + 0.9f, -26f), WeaponKind.Minigun);
        b.Weapon(new Vector3(26f, Gallery + 0.9f, 26f), WeaponKind.FlakCannon);
        b.Weapon(new Vector3(0f, -1.4f, 8f), WeaponKind.Redeemer, respawn: 100f);
        b.Item(new Vector3(0f, Gallery + 0.8f, -28f), PickupKind.DamageAmp);
        b.Item(new Vector3(0f, Gallery + 0.8f, 28f), PickupKind.ThighPads);
        b.Item(new Vector3(-16f, 0.8f, 0f), PickupKind.BodyArmor);
        b.Item(new Vector3(0f, -1.4f, -8f), PickupKind.SuperHealth);
        for (int i = 0; i < 14; i++)
        {
            float a = i / 14f * MathX.TwoPi;
            b.Item(new Vector3(MathF.Cos(a) * 30f, 0.7f, MathF.Sin(a) * 30f), PickupKind.HealthPack);
        }
        b.Ammo(new Vector3(-24f, 0.7f, -26f), AmmoKind.FlakShells);
        b.Ammo(new Vector3(24f, 0.7f, 26f), AmmoKind.MinigunBullets);
        b.Ammo(new Vector3(24f, 0.7f, -26f), AmmoKind.ShockCore);
        b.Ammo(new Vector3(-24f, 0.7f, 26f), AmmoKind.PulseCells);
        b.Ammo(new Vector3(3f, 0.4f, 3f), AmmoKind.Rockets);
        b.Ammo(new Vector3(0f, Gallery + 6.4f, -15f), AmmoKind.SniperRounds);
        for (int i = 0; i < 4; i++)
        {
            float a = i / 4f * MathX.TwoPi + MathX.Pi / 4f;
            b.Item(new Vector3(MathF.Cos(a) * 20f, 0.6f, MathF.Sin(a) * 20f), PickupKind.HealthVial);
        }

        b.Spawn(new Vector3(-28f, 0.2f, -28f), 45f);
        b.Spawn(new Vector3(28f, 0.2f, 28f), -135f);
        b.Spawn(new Vector3(28f, 0.2f, -28f), 135f);
        b.Spawn(new Vector3(-28f, 0.2f, 28f), -45f);
        b.Spawn(new Vector3(0f, 0.2f, -24f), 180f);
        b.Spawn(new Vector3(0f, 0.2f, 24f), 0f);
        b.Spawn(new Vector3(-24f, 0.2f, 0f), 90f);
        b.Spawn(new Vector3(24f, 0.2f, 0f), -90f);
        b.Spawn(new Vector3(-28f, Gallery + 0.2f, -12f), 90f);
        b.Spawn(new Vector3(28f, Gallery + 0.2f, 12f), -90f);
        b.Spawn(new Vector3(0f, Gallery + 5.8f, 0f), 0f);

        return b.Build(gl);
    }

    // ================================================================ DM-渦輪機房

    /// <summary>
    /// An industrial hall around a huge turbine, ringed by ledges and stacked with crates,
    /// with a flooded basement channel running underneath.
    /// </summary>
    private static Level BuildTurbine(GL gl)
    {
        var b = new LevelBuilder(Loc.MapTurbine, Loc.MapTurbineDesc);
        var env = b.Level.Environment;
        env.SunDirection = Vector3.Normalize(new Vector3(-0.20f, -0.92f, -0.32f));
        env.SunColor = new Vector3(2.0f, 1.9f, 1.7f);
        env.AmbientSky = new Vector3(0.24f, 0.25f, 0.30f);
        env.AmbientGround = new Vector3(0.10f, 0.09f, 0.08f);
        env.SkyTop = new Vector3(0.03f, 0.04f, 0.07f);
        env.SkyHorizon = new Vector3(0.16f, 0.14f, 0.13f);
        env.StarStrength = 0.4f;
        env.CloudStrength = 0.5f;
        env.EnvIntensity = 0.45f;
        env.FogColor = new Vector3(0.11f, 0.11f, 0.12f);
        env.FogDensity = 0.022f;

        const float HX = 32f, HZ = 26f;
        const float CeilY = 20f;
        const float Ledge = 7.5f;

        b.Solid(new Vector3(-HX, -1.4f, -HZ), new Vector3(HX, 0f, HZ), MatId.TechFloor, true, 0.9f);
        b.Room(new Vector3(-HX - 2f, -6f, -HZ - 2f), new Vector3(HX + 2f, CeilY, HZ + 2f), 2f,
            MatId.TechFloor, MatId.RustMetal, MatId.TechPanelDark, withCeiling: true, withFloor: false);
        DressIndustrial(b, new Vector3(-HX, 0f, -HZ), new Vector3(HX, CeilY, HZ),
            MatId.RustMetal, MatId.TechPanelDark, 4);

        // --- the turbine: a huge drum in the middle you can run around and climb ---
        b.Prism(new Vector3(0f, 6f, 0f), 7.5f, 12f, 12, MatId.RustMetal);
        b.Prism(new Vector3(0f, 12.4f, 0f), 8.6f, 1.2f, 12, MatId.Trim);
        // The machine itself: casing ribs, bolted flanges, and the ducts feeding it.
        for (int i = 0; i < 12; i++)
        {
            float a = i / 12f * MathX.TwoPi;
            Vector3 d = new(MathF.Cos(a), 0f, MathF.Sin(a));
            b.DecorBeam(d * 7.6f + new Vector3(0f, 0.4f, 0f), d * 7.6f + new Vector3(0f, 12.2f, 0f),
                0.22f, 0.22f, MatId.Trim, 1.3f);
            Pipe(b, d * 8.2f + new Vector3(0f, 9.5f, 0f), d * 15f + new Vector3(0f, 13.5f, 0f), 0.3f, MatId.RustMetal);
        }
        foreach (float ry in new[] { 2.2f, 6f, 9.8f })
            b.Torus(new Vector3(0f, ry, 0f), 7.9f, 0.28f, MatId.Trim, 24, 8);
        BoltLine(b, new Vector3(-7.4f, 12.6f, 0f), new Vector3(7.4f, 12.6f, 0f), 0.11f, MatId.Trim, 1.4f);
        BoltLine(b, new Vector3(0f, 12.6f, -7.4f), new Vector3(0f, 12.6f, 7.4f), 0.11f, MatId.Trim, 1.4f);
        b.Prism(new Vector3(0f, 0.4f, 0f), 9.2f, 1.2f, 12, MatId.Trim);
        for (int i = 0; i < 6; i++)
        {
            float a = i / 6f * MathX.TwoPi;
            Vector3 d = new(MathF.Cos(a), 0f, MathF.Sin(a));
            b.Decor(d * 9.4f + new Vector3(-0.9f, 2.5f, -0.9f), d * 9.4f + new Vector3(0.9f, 10f, 0.9f),
                MatId.EnergyPanel, 0.7f);
            b.AddLight(d * 10.5f + new Vector3(0f, 6f, 0f), new Vector3(0.35f, 0.75f, 1f), 14f, 3.2f, 4f, 0.18f);
        }

        // --- ledge ring around the hall, reached by ramps and a lift ---
        b.Solid(new Vector3(-HX, Ledge - 0.5f, -HZ), new Vector3(HX, Ledge, -HZ + 6f), MatId.MetalGrate, true, 0.9f);
        b.Solid(new Vector3(-HX, Ledge - 0.5f, HZ - 6f), new Vector3(HX, Ledge, HZ), MatId.MetalGrate, true, 0.9f);
        // Overlap the side ledges with their catwalks by at least a full capsule diameter.
        // Ending exactly at x=+/-26 left a pawn centred just 0.42 m outside the slab hanging
        // against its vertical lip, even though the connected nav nodes were valid.
        b.Solid(new Vector3(-HX, Ledge - 0.5f, -HZ + 6f), new Vector3(-HX + 7f, Ledge, HZ - 6f), MatId.MetalGrate, true, 0.9f);
        b.Solid(new Vector3(HX - 7f, Ledge - 0.5f, -HZ + 6f), new Vector3(HX, Ledge, HZ - 6f), MatId.MetalGrate, true, 0.9f);
        RailRun(b, new Vector3(-HX + 6f, Ledge, -HZ + 6f), new Vector3(HX - 6f, Ledge, -HZ + 6f));
        RailRun(b, new Vector3(-HX + 6f, Ledge, HZ - 6f), new Vector3(HX - 6f, Ledge, HZ - 6f));
        // Meet the open inner edges of the west/east ledges. The old footprints overlapped the
        // south/north overhead decks, leaving half of each slope buried and making a valid nav
        // route physically enter the ramp from its vertical side.
        b.Ramp(new Vector3(-HX + 6f, 0f, -20f), new Vector3(-HX + 18f, Ledge, -14f), 1, MatId.TechFloor);
        b.Ramp(new Vector3(HX - 18f, 0f, 14f), new Vector3(HX - 6f, Ledge, 20f), 0, MatId.TechFloor);
        // A broader car gives a capsule knocked during the ride enough landing margin; the old
        // 3.6-by-4 metre platform let even correct air recovery miss by half a metre.
        b.Lift(new Vector3(HX - 11f, 0.05f, -23f), new Vector3(HX - 5.5f, 0.45f, -17f),
            new Vector3(0f, Ledge, 0f), MatId.TechPanelDark, period: 7f, navigable: false);

        // --- catwalks reaching the turbine top from the ledge ---
        foreach (var (ax, az) in new[] { (-1f, 0f), (1f, 0f), (0f, -1f), (0f, 1f) })
        {
            Vector3 from = new(ax * (HX - 6f), Ledge, az * (HZ - 6f));
            Vector3 to = new(ax * 8f, Ledge, az * 8f);
            Vector3 min = Vector3.Min(from, to) - new Vector3(MathF.Abs(az) * 2.2f, 0.5f, MathF.Abs(ax) * 2.2f);
            Vector3 max = Vector3.Max(from, to) + new Vector3(MathF.Abs(az) * 2.2f, 0f, MathF.Abs(ax) * 2.2f);
            b.Solid(min, max, MatId.MetalGrate, true, 1.0f);
        }
        b.Solid(new Vector3(-8.2f, Ledge - 0.5f, -8.2f), new Vector3(8.2f, Ledge, 8.2f),
            MatId.MetalGrate, true, 1.0f);
        b.Ramp(new Vector3(-3f, Ledge, -3f), new Vector3(3f, 12.4f, 3f), 0, MatId.TechFloor);

        // --- crates: the cover that makes the floor readable ---
        var rng = new Rng(0x7B1E);
        for (int i = 0; i < 14; i++)
        {
            float x = rng.Range(-HX + 5f, HX - 5f);
            float z = rng.Range(-HZ + 5f, HZ - 5f);
            if (new Vector2(x, z).Length() < 12f) continue;
            float sz = rng.Range(1.4f, 2.3f);
            float stack = rng.Chance(0.35f) ? 2f : 1f;
            b.Solid(new Vector3(x - sz, 0f, z - sz), new Vector3(x + sz, sz * 1.6f * stack, z + sz),
                rng.Chance(0.5f) ? MatId.RustMetal : MatId.TechPanelDark, true, 1.3f);
        }

        // The "flooded service channel under the floor" that used to sit here was stillborn: the
        // hall floor above is one unbroken slab over y=[-1.4,0] across the whole arena, so the
        // channel had no opening. Its ramps climbed to a ceiling, its end walls faced sealed void,
        // its light lit the inside of a rock, and its water volume was entirely inside the fill
        // block that shared the channel's footprint - drowning geometry nobody could ever see or
        // swim in. Opening it would mean cutting a 56x8 m trench straight through the middle of
        // the hall, which is a redesign, not a fix, so the dead brushes come out instead.

        // --- the hidden alcove behind a false panel, in the spirit of the original's secret ---
        // An alcove is an enclosed *space*, not one solid six-metre block. The old block buried
        // the shield belt inside collision. Keep a substantial floor, side walls and ceiling,
        // with the decorative false panel marking the open entrance.
        b.Solid(new Vector3(-HX, Ledge - 0.5f, -3.5f), new Vector3(-HX + 6f, Ledge, 3.5f), MatId.TechPanelDark, true, 0.8f);
        b.Solid(new Vector3(-HX, Ledge, -3.5f), new Vector3(-HX + 6f, Ledge + 5f, -2.8f), MatId.TechPanelDark, true, 0.8f);
        b.Solid(new Vector3(-HX, Ledge, 2.8f), new Vector3(-HX + 6f, Ledge + 5f, 3.5f), MatId.TechPanelDark, true, 0.8f);
        b.Solid(new Vector3(-HX, Ledge + 4.4f, -3.5f), new Vector3(-HX + 6f, Ledge + 5f, 3.5f), MatId.TechPanelDark, true, 0.8f);
        b.Decor(new Vector3(-HX + 5.9f, Ledge, -2.2f), new Vector3(-HX + 6.1f, Ledge + 3.4f, 2.2f), MatId.EnergyPanel, 0.6f);
        b.Item(new Vector3(-HX + 3f, Ledge + 0.8f, 0f), PickupKind.ShieldBelt);
        b.AddLight(new Vector3(-HX + 3f, Ledge + 2f, 0f), new Vector3(1f, 0.4f, 0.9f), 9f, 3f);

        for (int x = -1; x <= 1; x++)
            for (int z = -1; z <= 1; z += 2)
                b.CeilingLamp(new Vector3(x * 20f, CeilY - 1.5f, z * 17f), new Vector3(0.9f, 0.88f, 0.8f), 30f, 9f, 1.6f);

        // --- placements ---
        b.Weapon(new Vector3(0f, 13.3f, 0f), WeaponKind.RocketLauncher);
        b.Item(new Vector3(3f, 13.2f, 0f), PickupKind.DamageAmp);
        b.Weapon(new Vector3(-26f, 0.9f, -20f), WeaponKind.FlakCannon);
        b.Weapon(new Vector3(26f, 0.9f, 20f), WeaponKind.Minigun);
        b.Weapon(new Vector3(26f, 0.9f, -20f), WeaponKind.PulseGun);
        b.Weapon(new Vector3(-26f, 0.9f, 20f), WeaponKind.BioRifle);
        b.Weapon(new Vector3(0f, Ledge + 0.9f, -HZ + 3f), WeaponKind.ShockRifle);
        b.Weapon(new Vector3(0f, Ledge + 0.9f, HZ - 3f), WeaponKind.SniperRifle);
        b.Weapon(new Vector3(0f, -4.4f, 0f), WeaponKind.Ripper);
        b.Item(new Vector3(-14f, 0.8f, 14f), PickupKind.BodyArmor);
        b.Item(new Vector3(14f, 0.8f, -14f), PickupKind.ThighPads);
        b.Item(new Vector3(HX - 3f, Ledge + 0.8f, 0f), PickupKind.Invisibility);
        // Eight packs and fifteen vials, and no keg — the original leans on volume of small
        // pickups rather than one big one.
        for (int i = 0; i < 8; i++)
        {
            float a = i / 8f * MathX.TwoPi + 0.3f;
            b.Item(new Vector3(MathF.Cos(a) * 22f, 0.7f, MathF.Sin(a) * 18f), PickupKind.HealthPack);
        }
        b.Ammo(new Vector3(-24f, 0.7f, -20f), AmmoKind.FlakShells);
        b.Ammo(new Vector3(24f, 0.7f, 20f), AmmoKind.MinigunBullets);
        b.Ammo(new Vector3(3f, Ledge + 0.7f, -HZ + 3f), AmmoKind.ShockCore);
        b.Ammo(new Vector3(3f, Ledge + 0.7f, HZ - 3f), AmmoKind.SniperRounds);
        b.Ammo(new Vector3(-3f, 13.1f, 0f), AmmoKind.Rockets);
        for (int i = 0; i < 6; i++)
        {
            float a = i / 6f * MathX.TwoPi;
            b.Item(new Vector3(MathF.Cos(a) * 15f, 0.6f, MathF.Sin(a) * 15f), PickupKind.HealthVial);
            b.Spawn(new Vector3(MathF.Cos(a) * 22f, 0.2f, MathF.Sin(a) * 18f), -a * MathX.Rad2Deg + 180f);
        }
        b.Spawn(new Vector3(-HX + 12f, Ledge + 0.2f, 0f), -90f);
        b.Spawn(new Vector3(HX - 12f, Ledge + 0.2f, 0f), 90f);
        b.Spawn(new Vector3(0f, Ledge + 0.2f, -HZ + 3f), 180f);
        b.Spawn(new Vector3(0f, Ledge + 0.2f, HZ - 3f), 0f);

        return b.Build(gl);
    }
}
