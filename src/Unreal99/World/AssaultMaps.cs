using System.Numerics;
using Silk.NET.OpenGL;
using Unreal99.Core;
using Unreal99.Game;
using Unreal99.Rendering;
using Unreal99.UI;

namespace Unreal99.World;

/// <summary>
/// Assault arenas. These are one-way maps: a defended line the attackers work along in a fixed
/// order, with the attacker spawns stepping forward behind each objective. They are shaped like
/// a corridor rather than an arena, because that asymmetry is the mode.
/// </summary>
public static partial class Maps
{
    // ================================================================ AS-車隊

    /// <summary>
    /// Convoy. A column of transports crossing a desert, with the attackers boarding from the
    /// rear and fighting forward one vehicle at a time to the missile on the front trailer.
    ///
    /// The original's seven-step sequence is reproduced exactly: extend the boarding platform,
    /// open the weapons bay panel, plant a charge on the bay door, open the rear bay door,
    /// trip the forward side-door switch, drop into the Nexus missile trailer, take the missile.
    /// </summary>
    private static Level BuildConvoy(GL gl)
    {
        var b = new LevelBuilder(Loc.MapConvoy, Loc.MapConvoyDesc);
        var env = b.Level.Environment;
        env.SunDirection = Vector3.Normalize(new Vector3(-0.55f, -0.62f, -0.31f));
        env.SunColor = new Vector3(4.4f, 3.7f, 2.7f);
        env.AmbientSky = new Vector3(0.38f, 0.34f, 0.28f);
        env.AmbientGround = new Vector3(0.30f, 0.24f, 0.16f);
        env.EnvIntensity = 0.85f;
        env.SkyTop = new Vector3(0.32f, 0.44f, 0.70f);
        env.SkyHorizon = new Vector3(0.86f, 0.76f, 0.56f);
        env.SkyGround = new Vector3(0.55f, 0.44f, 0.28f);
        env.StarStrength = 0f;
        env.CloudStrength = 0.25f;
        env.FogColor = new Vector3(0.82f, 0.73f, 0.55f);
        env.FogDensity = 0.0075f;                  // heat haze, and it buries the arena boundary

        // Attackers come from the rear (−X) and work forward to the missile at +X.
        b.Level.AssaultAttackers = Team.Red;

        const float Deck = 8f;          // the convoy's running deck height above the sand
        const float Sand = 0f;

        // --- the desert ---
        // Wide and low. The convoy runs down the middle of it, and the arena boundary is pushed
        // far enough out that the haze swallows it — the point of this map is the horizon, and a
        // wall twenty metres behind the tail rig destroys that completely.
        b.Solid(new Vector3(-250f, -6f, -130f), new Vector3(250f, Sand, 130f), MatId.Rock, true, 0.35f);
        b.Room(new Vector3(-254f, -6f, -134f), new Vector3(254f, 34f, 134f), 4f,
            MatId.Rock, MatId.Rock, MatId.Rock, withCeiling: false, withFloor: false);

        var rng = new Rng(0x0C07);
        // Dunes: low and broad near the convoy, taller further out, so the eye reads distance.
        for (int i = 0; i < 120; i++)
        {
            float px = rng.Range(-244f, 244f);
            float pz = rng.Range(-126f, 126f);
            if (MathF.Abs(pz) < 22f) continue;              // keep the convoy's lane clear
            float far = MathX.Saturate((MathF.Abs(pz) - 22f) / 100f);
            // Distant dunes get wider, not taller. Height reads as a cliff to fall off; width
            // reads as depth, which is what the horizon actually needs.
            float s = rng.Range(3f, 9f) * (0.6f + far * 2.2f);
            b.Solid(new Vector3(px - s, Sand, pz - s),
                new Vector3(px + s, Sand + rng.Range(1.5f, 4.5f), pz + s), MatId.Rock, true, 0.6f);
        }

        // ---------------------------------------------------------------- the convoy
        // Six vehicles nose to tail. Each is a deck slab with a low kerb, so a fight on one of
        // them has edges to be pushed off — which is most of what makes this map memorable.
        (float x0, float x1, float w, string tag)[] rigs =
        [
            (-158f, -122f, 13f, "escort"),   // attacker transport
            (-112f,  -70f, 15f, "weapons"),  // weapons bay
            ( -60f,  -18f, 15f, "cargo"),    // rear bay
            (  -8f,   34f, 15f, "flank"),    // side-door section
            (  44f,   86f, 16f, "nexus"),    // Nexus missile trailer
            (  96f,  132f, 13f, "cab"),      // lead cab
        ];

        foreach (var (x0, x1, w, tag) in rigs)
        {
            b.Solid(new Vector3(x0, Deck - 4.5f, -w), new Vector3(x1, Deck, w), MatId.RustMetal, true, 0.55f);
            b.Solid(new Vector3(x0, Deck, -w), new Vector3(x1, Deck + 0.6f, -w + 1.2f), MatId.ArmorPlate, true, 0.7f);
            b.Solid(new Vector3(x0, Deck, w - 1.2f), new Vector3(x1, Deck + 0.6f, w), MatId.ArmorPlate, true, 0.7f);
            // Wheels, which is what sells these as vehicles rather than floating platforms.
            for (float wx = x0 + 6f; wx < x1 - 4f; wx += 12f)
                for (int s = -1; s <= 1; s += 2)
                    b.Cylinder(new Vector3(wx, Deck - 6.4f, s * (w - 1.6f)), 2.2f, 2.2f, 1.1f, 10, MatId.TechPanelDark);
            _ = tag;
        }

        // Gaps between rigs are bridged by coupling plates dropped just below deck level. The
        // drop has to stay under StepHeight (0.55) or the nav graph refuses to link across it
        // and the whole convoy becomes six disconnected islands.
        for (int i = 0; i < rigs.Length - 1; i++)
        {
            float a = rigs[i].x1, c = rigs[i + 1].x0;
            float w = MathF.Min(rigs[i].w, rigs[i + 1].w) - 2f;
            b.Solid(new Vector3(a, Deck - 0.4f, -w), new Vector3(c, Deck, w), MatId.MetalGrate, true, 0.85f);
        }

        // ---------------------------------------------------------------- objectives
        // Declaration order is completion order, and each one opens the next spawn group.

        // 1 — the boarding platform between the escort and the weapons rig.
        b.AddObjective(new Vector3(-118f, Deck, 0f), Loc.ObjBoardingPlatform, ObjectiveKind.Destroy,
            radius: 3.6f, health: 700f, unlocksSpawnGroup: 1);

        // 2 — the weapons bay panel on the second rig's flank.
        b.AddObjective(new Vector3(-88f, Deck, -9f), Loc.ObjWeaponsPanel, ObjectiveKind.Destroy,
            radius: 3.4f, health: 900f, unlocksSpawnGroup: 1);

        // 3 — the charge on the bay door. This one is held, not shot, which is where the
        //     defenders get their best stand: one body inside the ring stalls the whole push.
        b.AddObjective(new Vector3(-64f, Deck, 0f), Loc.ObjPlantCharge, ObjectiveKind.Hold,
            radius: 4.2f, holdSeconds: 9f, unlocksSpawnGroup: 2);

        // 4 — the rear bay door mechanism.
        b.AddObjective(new Vector3(-30f, Deck, 8f), Loc.ObjRearDoor, ObjectiveKind.Destroy,
            radius: 3.4f, health: 1000f, unlocksSpawnGroup: 2);

        // 5 — the forward side-door switch.
        b.AddObjective(new Vector3(20f, Deck, -10f), Loc.ObjSideSwitch, ObjectiveKind.Hold,
            radius: 3.2f, holdSeconds: 5f, unlocksSpawnGroup: 3);

        // 6 — drop into the Nexus trailer. A reach objective: getting there is the whole task.
        b.AddObjective(new Vector3(56f, Deck - 3.2f, 0f), Loc.ObjEnterNexus, ObjectiveKind.Touch,
            radius: 4.5f, unlocksSpawnGroup: 4);

        // 7 — the missile itself.
        b.AddObjective(new Vector3(78f, Deck - 3.2f, 0f), Loc.ObjTakeMissile, ObjectiveKind.Touch,
            radius: 3.6f, unlocksSpawnGroup: 4);

        // The Nexus trailer is a sunken bay, which is why objectives 6 and 7 sit below deck.
        // Carved out of the slab so the walls are the trailer sides.
        b.Solid(new Vector3(46f, Deck - 3.4f, -11f), new Vector3(84f, Deck - 3.2f, 11f), MatId.ArmorPlate, true, 0.7f);
        for (int s = -1; s <= 1; s += 2)
            b.Solid(new Vector3(46f, Deck - 3.2f, s * 11f), new Vector3(84f, Deck + 0.4f, s * 13f),
                MatId.RustMetal, true, 0.6f);
        // A 1:4 ramp down into it. Anything past 1:3 carries no bot route at all, so the last two
        // objectives would be unreachable by everyone except a human.
        b.Ramp(new Vector3(33f, Deck - 3.2f, -6f), new Vector3(46f, Deck, 6f), 0, MatId.MetalGrate, true, 0.85f);
        // The missile on its cradle.
        b.Decor(new Vector3(74f, Deck - 3.0f, -1.6f), new Vector3(83f, Deck + 0.4f, 1.6f), MatId.TechPanelDark, 0.7f);
        b.AddLight(new Vector3(78f, Deck + 1.6f, 0f), new Vector3(1f, 0.5f, 0.25f), 22f, 5f);

        // ---------------------------------------------------------------- cover and structure
        // Containers and bulkheads: without them the deck is a shooting gallery for the defenders.
        (float x, float z, float w, float d, float h)[] crates =
        [
            (-100f, 5f, 3.5f, 3f, 3f), (-96f, -4f, 3f, 3.5f, 2.4f), (-78f, 6f, 3f, 3f, 3f),
            (-48f, -6f, 4f, 3f, 3.2f), (-40f, 5f, 3f, 3.5f, 2.6f), (-22f, -7f, 3.5f, 3f, 3f),
            (2f, 6f, 3.5f, 3f, 3.2f), (10f, -4f, 3f, 3f, 2.6f), (28f, 6f, 3f, 3.5f, 3f),
            (104f, 0f, 4f, 5f, 5f), (118f, 4f, 3.5f, 3f, 3.4f),
        ];
        foreach (var (x, z, w, d, h) in crates)
            b.Solid(new Vector3(x - w, Deck, z - d), new Vector3(x + w, Deck + h, z + d), MatId.TechPanelDark, true, 0.75f);

        // The lead cab, which the defenders spawn behind. Deliberately low: a tall perch here
        // leaves a defender staring almost straight down at the deck, which is both a miserable
        // firing position and what the traversal gate flags as a steep-down stall.
        b.Solid(new Vector3(112f, Deck, -9f), new Vector3(130f, Deck + 4.4f, 9f), MatId.ArmorPlate, true, 0.7f);
        b.Solid(new Vector3(112f, Deck + 4.4f, -7f), new Vector3(126f, Deck + 5f, 7f), MatId.MetalGrate, true, 0.9f);
        // A 1:4 ramp up the flank rather than a pad, so the roof is a route and not a launch.
        b.Ramp(new Vector3(94f, Deck, 7f), new Vector3(112f, Deck + 4.4f, 11f), 0, MatId.MetalGrate, true, 0.85f);

        // ---------------------------------------------------------------- spawns
        // Group 0 is the attackers' escort at the tail; each later group is a rig further forward.
        for (int i = 0; i < 5; i++)
            b.Spawn(new Vector3(-152f + i * 5f, Deck + 0.2f, -6f + i * 3f), 0f, Team.Red, 0);
        for (int i = 0; i < 4; i++)
            b.Spawn(new Vector3(-108f + i * 6f, Deck + 0.2f, -7f + i * 4f), 0f, Team.Red, 1);
        for (int i = 0; i < 4; i++)
            b.Spawn(new Vector3(-56f + i * 6f, Deck + 0.2f, -7f + i * 4f), 0f, Team.Red, 2);
        for (int i = 0; i < 4; i++)
            b.Spawn(new Vector3(-4f + i * 6f, Deck + 0.2f, -7f + i * 4f), 0f, Team.Red, 3);
        for (int i = 0; i < 4; i++)
            b.Spawn(new Vector3(40f + i * 4f, Deck + 0.2f, -8f + i * 5f), 0f, Team.Red, 4);

        // Defenders hold the front and never move up, so they stay on group 0.
        for (int i = 0; i < 6; i++)
            b.Spawn(new Vector3(96f + i * 6f, Deck + 0.2f, -8f + i * 3f), 180f, Team.Blue, 0);

        // ---------------------------------------------------------------- loadout
        b.Weapon(new Vector3(-140f, Deck + 0.8f, 0f), WeaponKind.FlakCannon);
        b.Weapon(new Vector3(-130f, Deck + 0.8f, -5f), WeaponKind.RocketLauncher);
        b.Weapon(new Vector3(-92f, Deck + 0.8f, 6f), WeaponKind.Minigun);
        b.Weapon(new Vector3(-52f, Deck + 0.8f, -6f), WeaponKind.ShockRifle);
        b.Weapon(new Vector3(-14f, Deck + 0.8f, 6f), WeaponKind.RocketLauncher);
        b.Weapon(new Vector3(26f, Deck + 0.8f, 7f), WeaponKind.SniperRifle);
        b.Weapon(new Vector3(66f, Deck - 2.4f, -7f), WeaponKind.FlakCannon);
        b.Weapon(new Vector3(120f, Deck + 5.8f, 0f), WeaponKind.SniperRifle);
        b.Item(new Vector3(-84f, Deck + 0.7f, 0f), PickupKind.ThighPads);
        b.Item(new Vector3(-34f, Deck + 0.7f, -8f), PickupKind.BodyArmor);
        b.Item(new Vector3(14f, Deck + 0.7f, 8f), PickupKind.HealthPack);
        b.Item(new Vector3(60f, Deck - 2.5f, 6f), PickupKind.BodyArmor);
        b.Item(new Vector3(104f, Deck + 0.7f, -6f), PickupKind.ShieldBelt);
        b.Ammo(new Vector3(-120f, Deck + 0.7f, 4f), AmmoKind.Rockets);
        b.Ammo(new Vector3(-44f, Deck + 0.7f, 4f), AmmoKind.FlakShells);
        b.Ammo(new Vector3(6f, Deck + 0.7f, -4f), AmmoKind.Bullets);
        b.Ammo(new Vector3(70f, Deck - 2.5f, 4f), AmmoKind.Rockets);

        // Vehicles: light and fast only. The convoy deck is too tight for armour, and the
        // original is a foot fight with the odd Manta run along the flanks.
        b.AddVehicle(VehicleKind.Manta, new Vector3(-146f, Deck + 2.4f, 8f), 0f, Team.Red);
        b.AddVehicle(VehicleKind.Manta, new Vector3(-146f, Deck + 2.4f, -8f), 0f, Team.Red);
        b.AddVehicle(VehicleKind.Raptor, new Vector3(-150f, Deck + 12f, 0f), 0f, Team.Red);
        b.AddVehicle(VehicleKind.Manta, new Vector3(104f, Deck + 2.4f, -6f), 180f, Team.Blue);

        return b.Build(gl);
    }

    // ================================================================ AS-護衛艦

    /// <summary>
    /// Frigate. The restored warship SS Victory at its dock. Two objectives, and two ways in:
    /// over the wooden bridge, which the defenders cover from a turret nest, or through the
    /// flooded passage under the hull, which is slower but arrives unseen.
    ///
    /// The sequence from the original: destroy the hydraulic compressor in the aft cabin — it is
    /// what locks the control room — then reach the control room on the top deck and hit the
    /// button that fires the ship's cannons.
    /// </summary>
    private static Level BuildFrigate(GL gl)
    {
        var b = new LevelBuilder(Loc.MapFrigate, Loc.MapFrigateDesc);
        var env = b.Level.Environment;
        env.SunDirection = Vector3.Normalize(new Vector3(0.28f, -0.52f, -0.81f));
        env.SunColor = new Vector3(2.2f, 2.25f, 2.6f);
        env.AmbientSky = new Vector3(0.24f, 0.28f, 0.38f);
        env.AmbientGround = new Vector3(0.10f, 0.12f, 0.15f);
        env.EnvIntensity = 0.62f;
        env.SkyTop = new Vector3(0.06f, 0.10f, 0.20f);
        env.SkyHorizon = new Vector3(0.34f, 0.38f, 0.48f);
        env.SkyGround = new Vector3(0.10f, 0.12f, 0.16f);
        env.StarStrength = 0.55f;
        env.CloudStrength = 0.7f;
        env.FogColor = new Vector3(0.28f, 0.32f, 0.40f);
        env.FogDensity = 0.008f;

        b.Level.AssaultAttackers = Team.Red;

        const float Quay = 6f;          // dock level
        const float WaterTop = 1.6f;
        const float MainDeck = 11f;
        // Deliberately only 5.5 m above the main deck. At seven metres a step off the edge lands
        // right on the fall-damage threshold, so anyone already wounded died walking downstairs.
        const float TopDeck = 16.5f;

        // --- the harbour basin, with the dock on one side and the ship on the other ---
        b.Solid(new Vector3(-90f, -10f, -60f), new Vector3(90f, -1.5f, 60f), MatId.Rock, true, 0.5f);
        // Low harbour walls: a 60-metre cliff behind a warship reads as a bathtub, and the
        // arena only needs to be closed, not enclosed.
        b.Room(new Vector3(-94f, -10f, -64f), new Vector3(94f, 38f, 64f), 4f,
            MatId.Rock, MatId.Rock, MatId.Rock, withCeiling: false, withFloor: false);
        b.Water(new Vector3(-90f, -1.5f, -60f), new Vector3(90f, WaterTop, 60f));

        // --- the cargo bay the attackers start in, on the quay ---
        b.Solid(new Vector3(-88f, -1.5f, -34f), new Vector3(-40f, Quay, 34f), MatId.Concrete, true, 0.6f);
        // Build the cargo-bay shell explicitly so the east wall contains a real opening. Adding
        // a non-colliding decorative brush over Room() does not subtract its solid wall and had
        // trapped every attacker inside this box.
        b.Solid(new Vector3(-86f, Quay + 9.8f, -22f), new Vector3(-52f, Quay + 11f, 22f), MatId.Concrete);
        b.Solid(new Vector3(-86f, Quay, -22f), new Vector3(-84.8f, Quay + 11f, 22f), MatId.TechPanelDark);
        b.Solid(new Vector3(-84.8f, Quay, -22f), new Vector3(-53.2f, Quay + 11f, -20.8f), MatId.TechPanelDark);
        b.Solid(new Vector3(-84.8f, Quay, 20.8f), new Vector3(-53.2f, Quay + 11f, 22f), MatId.TechPanelDark);
        b.WallWithDoor(new Vector3(-53.2f, Quay, -22f), new Vector3(-52f, Quay + 11f, 22f),
            doorCenter: 0f, doorWidth: 12f, doorHeight: 5f, material: MatId.TechPanelDark, alongX: false);
        for (int i = 0; i < 8; i++)
            b.Solid(new Vector3(-82f + (i % 4) * 7f, Quay, -16f + (i / 4) * 26f),
                    new Vector3(-78f + (i % 4) * 7f, Quay + 3f, -12f + (i / 4) * 26f), MatId.TechPanelDark, true, 0.75f);

        // A gantry down both long walls, reached by a 1:4 ramp. The original's cargo bay has a
        // second level, and without one this room is a flat box with nowhere to fight from.
        for (int s = -1; s <= 1; s += 2)
        {
            b.Solid(new Vector3(-84f, Quay + 4.6f, s * 18f), new Vector3(-56f, Quay + 5f, s * 21f),
                MatId.MetalGrate, true, 0.9f);
            RailRun(b, new Vector3(-84f, Quay + 5f, s * 18f), new Vector3(-56f, Quay + 5f, s * 18f));
        }
        b.Ramp(new Vector3(-84f, Quay, -18f), new Vector3(-66f, Quay + 4.6f, -14f), 0, MatId.MetalGrate, true, 0.85f);
        b.Ramp(new Vector3(-66f, Quay, 14f), new Vector3(-84f, Quay + 4.6f, 18f), 1, MatId.MetalGrate, true, 0.85f);
        b.Weapon(new Vector3(-70f, Quay + 5.4f, -19.5f), WeaponKind.SniperRifle);
        b.Item(new Vector3(-70f, Quay + 5.3f, 19.5f), PickupKind.ThighPads);

        for (int i = 0; i < 5; i++)
            b.CeilingLamp(new Vector3(-80f + i * 6f, Quay + 10.4f, -6f + (i % 2) * 12f),
                new Vector3(1f, 0.92f, 0.75f), 20f, 3.4f, 1.5f);
        b.AddLight(new Vector3(-70f, Quay + 8f, 0f), new Vector3(0.9f, 0.85f, 0.7f), 34f, 4.5f);

        // --- route A: the wooden bridge, covered by the defenders' nest ---
        b.Solid(new Vector3(-40f, Quay - 0.4f, -6f), new Vector3(-8f, Quay, 6f), MatId.Trim, true, 0.85f);
        for (int i = 0; i < 6; i++)
        {
            float x = -38f + i * 6f;
            for (int s = -1; s <= 1; s += 2)
                b.Solid(new Vector3(x, Quay, s * 6f - 0.5f), new Vector3(x + 0.6f, Quay + 1.5f, s * 6f + 0.5f),
                    MatId.Trim, true, 0.85f);
        }
        // Meet the hull exactly at its stern. Extending this ramp six metres inside the solid hull
        // buried its upper half and left a 1.7 m collision step at x=6; the nav graph correctly
        // stopped there, making both ship objectives unreachable.
        b.Ramp(new Vector3(-8f, Quay, -6f), new Vector3(6f, MainDeck, 6f), 0, MatId.Trim, true, 0.85f);

        // --- route B: the flooded passage under the hull ---
        // Down off the quay's edge, along the bottom, and up inside the hull. Slower and silent.
        b.Solid(new Vector3(-44f, -1.5f, -22f), new Vector3(-40f, WaterTop + 0.4f, -14f), MatId.ArmorPlate, false, 0.7f);
        b.Solid(new Vector3(-40f, -1.5f, -20f), new Vector3(4f, 0.6f, -14f), MatId.Rock, true, 0.5f);
        b.Solid(new Vector3(4f, -1.5f, -22f), new Vector3(20f, 0.6f, -8f), MatId.Rock, true, 0.5f);
        // A 1:4 ramp up into the hull's flooded bilge, then a lift to the main deck.
        b.Ramp(new Vector3(20f, 0.6f, -18f), new Vector3(38f, 5.0f, -10f), 0, MatId.RustMetal, true, 0.6f);
        b.Solid(new Vector3(38f, 4.8f, -14f), new Vector3(54f, 5.0f, -6f), MatId.RustMetal, true, 0.6f);
        b.Lift(new Vector3(52f, 5.0f, -12f), new Vector3(56f, 5.4f, -8f), new Vector3(0f, MainDeck - 5.0f, 0f), MatId.MetalGrate, 6.5f);
        b.AddLight(new Vector3(30f, 3.5f, -13f), new Vector3(0.45f, 0.65f, 0.8f), 22f, 3f);

        // ---------------------------------------------------------------- the ship
        const float Bow = 74f, Stern = 6f, Beam = 17f;

        // Hull and main deck.
        b.Solid(new Vector3(Stern, WaterTop - 4f, -Beam), new Vector3(Bow, MainDeck, Beam),
            MatId.RustMetal, true, 0.55f);
        // Bulwarks, so a fight on deck has cover and edges.
        for (int s = -1; s <= 1; s += 2)
            b.Solid(new Vector3(Stern, MainDeck, s * Beam - s * 1.4f), new Vector3(Bow, MainDeck + 1.6f, s * Beam),
                MatId.ArmorPlate, true, 0.7f);
        b.Solid(new Vector3(Bow - 1.6f, MainDeck, -Beam), new Vector3(Bow, MainDeck + 1.6f, Beam),
            MatId.ArmorPlate, true, 0.7f);

        // Decorative cannons along both rails, as the original has.
        for (int i = 0; i < 5; i++)
        {
            float x = Stern + 10f + i * 12f;
            for (int s = -1; s <= 1; s += 2)
            {
                b.Decor(new Vector3(x - 1.2f, MainDeck, s * (Beam - 3.2f) - 1.2f),
                        new Vector3(x + 1.2f, MainDeck + 1.1f, s * (Beam - 3.2f) + 1.2f), MatId.TechPanelDark, 0.7f);
                b.Decor(new Vector3(x - 0.35f, MainDeck + 0.7f, s * (Beam - 3.2f)),
                        new Vector3(x + 0.35f, MainDeck + 1.3f, s * Beam), MatId.ArmorPlate, 0.85f);
            }
        }

        // --- the aft cabin, holding objective 1 ---
        Vector3 aftMin = new(Stern + 2f, MainDeck, -11f);
        Vector3 aftMax = new(Stern + 22f, MainDeck + 7f, 11f);
        b.Solid(new Vector3(aftMin.X, aftMax.Y - 1f, aftMin.Z), aftMax, MatId.ArmorPlate);
        b.Solid(new Vector3(aftMin.X + 1f, aftMin.Y, aftMin.Z), new Vector3(aftMax.X - 1f, aftMax.Y, aftMin.Z + 1f), MatId.RustMetal);
        b.Solid(new Vector3(aftMin.X + 1f, aftMin.Y, aftMax.Z - 1f), new Vector3(aftMax.X - 1f, aftMax.Y, aftMax.Z), MatId.RustMetal);
        b.WallWithDoor(aftMin, new Vector3(aftMin.X + 1f, aftMax.Y, aftMax.Z),
            doorCenter: 0f, doorWidth: 8f, doorHeight: 4.4f, material: MatId.RustMetal, alongX: false);
        b.WallWithDoor(new Vector3(aftMax.X - 1f, aftMin.Y, aftMin.Z), aftMax,
            doorCenter: 0f, doorWidth: 8f, doorHeight: 4.4f, material: MatId.RustMetal, alongX: false);
        b.AddLight(new Vector3(Stern + 12f, MainDeck + 5f, 0f), new Vector3(0.9f, 0.6f, 0.4f), 20f, 4f);

        // Toughest single objective in the game — but not so tough that one attacker who has
        // fought their way aboard on a light loadout can never finish it.
        b.AddObjective(new Vector3(Stern + 8f, MainDeck, 0f), Loc.ObjCompressor, ObjectiveKind.Destroy,
            radius: 3.6f, health: 700f, unlocksSpawnGroup: 1);

        // --- the superstructure and control room, holding objective 2 ---
        b.Solid(new Vector3(40f, MainDeck, -11f), new Vector3(60f, TopDeck, 11f), MatId.ArmorPlate, true, 0.7f);
        Vector3 controlMin = new(41f, TopDeck, -9f);
        Vector3 controlMax = new(59f, TopDeck + 6f, 9f);
        b.Solid(new Vector3(controlMin.X, controlMax.Y - 1f, controlMin.Z), controlMax, MatId.MetalGrate);
        b.Solid(new Vector3(controlMin.X + 1f, controlMin.Y, controlMin.Z), new Vector3(controlMax.X - 1f, controlMax.Y, controlMin.Z + 1f), MatId.TechPanelDark);
        b.Solid(new Vector3(controlMin.X + 1f, controlMin.Y, controlMax.Z - 1f), new Vector3(controlMax.X - 1f, controlMax.Y, controlMax.Z), MatId.TechPanelDark);
        b.WallWithDoor(controlMin, new Vector3(controlMin.X + 1f, controlMax.Y, controlMax.Z),
            doorCenter: 0f, doorWidth: 7f, doorHeight: 4.2f, material: MatId.TechPanelDark, alongX: false);
        b.WallWithDoor(new Vector3(controlMax.X - 1f, controlMin.Y, controlMin.Z), controlMax,
            doorCenter: 0f, doorWidth: 7f, doorHeight: 4.2f, material: MatId.TechPanelDark, alongX: false);
        b.AddLight(new Vector3(50f, TopDeck + 4.5f, 0f), new Vector3(0.55f, 0.75f, 1f), 22f, 4.5f);

        // The ladders the original uses are jump pads here: a 7m climb has no nav route otherwise.
        for (int s = -1; s <= 1; s += 2)
            b.AddJumpPad(new Vector3(37f, MainDeck + 0.1f, s * 7f), new Vector3(45f, TopDeck + 1.4f, s * 6f),
                new Vector3(0.5f, 0.8f, 1f));
        b.Lift(new Vector3(62f, MainDeck, -3f), new Vector3(66f, MainDeck + 0.4f, 3f), new Vector3(0f, TopDeck - MainDeck, 0f), MatId.MetalGrate, 6f);

        b.AddObjective(new Vector3(50f, TopDeck, 0f), Loc.ObjFireCannons, ObjectiveKind.Hold,
            radius: 3.4f, holdSeconds: 6f, unlocksSpawnGroup: 1);

        // --- the defenders' turret nest covering the bridge ---
        b.Solid(new Vector3(14f, MainDeck, 9f), new Vector3(24f, MainDeck + 4.5f, Beam - 1.4f),
            MatId.ArmorPlate, true, 0.7f);
        b.Solid(new Vector3(14f, MainDeck + 4.5f, 8f), new Vector3(26f, MainDeck + 5.1f, Beam),
            MatId.MetalGrate, true, 0.9f);
        b.Weapon(new Vector3(20f, MainDeck + 5.4f, 12f), WeaponKind.Minigun);
        b.AddJumpPad(new Vector3(12f, MainDeck + 0.1f, 12f), new Vector3(20f, MainDeck + 7f, 12f),
            new Vector3(0.9f, 0.7f, 0.4f));

        // ---------------------------------------------------------------- spawns
        // Attackers start in the cargo bay; once the compressor is gone they come in on deck.
        for (int i = 0; i < 6; i++)
            b.Spawn(new Vector3(-78f + i * 4f, Quay + 0.2f, -14f + i * 6f), 0f, Team.Red, 0);
        for (int i = 0; i < 4; i++)
            b.Spawn(new Vector3(Stern + 10f + i * 3f, MainDeck + 0.2f, -6f + i * 4f), 0f, Team.Red, 1);

        for (int i = 0; i < 6; i++)
            b.Spawn(new Vector3(46f + i * 5f, MainDeck + 0.2f, -9f + i * 4f), 180f, Team.Blue, 0);

        // ---------------------------------------------------------------- loadout
        b.Weapon(new Vector3(-72f, Quay + 0.8f, 0f), WeaponKind.FlakCannon);
        b.Weapon(new Vector3(-64f, Quay + 0.8f, -10f), WeaponKind.RocketLauncher);
        b.Weapon(new Vector3(-60f, Quay + 0.8f, 12f), WeaponKind.Minigun);
        b.Weapon(new Vector3(Stern + 16f, MainDeck + 0.8f, 8f), WeaponKind.ShockRifle);
        // Resupply beside the forward attacker spawn. An attacker who fought their way aboard
        // arrives empty; without this the aft cabin is where the assault quietly dies.
        b.Weapon(new Vector3(Stern + 12f, MainDeck + 0.8f, -7f), WeaponKind.Minigun);
        b.Ammo(new Vector3(Stern + 10f, MainDeck + 0.7f, -8f), AmmoKind.FlakShells);
        b.Ammo(new Vector3(Stern + 14f, MainDeck + 0.7f, -8f), AmmoKind.MinigunBullets);
        b.Ammo(new Vector3(Stern + 12f, MainDeck + 0.7f, 6f), AmmoKind.Rockets);
        b.Weapon(new Vector3(32f, MainDeck + 0.8f, -12f), WeaponKind.FlakCannon);
        b.Weapon(new Vector3(66f, MainDeck + 0.8f, 0f), WeaponKind.RocketLauncher);
        b.Weapon(new Vector3(50f, TopDeck + 0.8f, -6f), WeaponKind.SniperRifle);
        b.Weapon(new Vector3(30f, 5.4f, -10f), WeaponKind.Ripper);
        b.Item(new Vector3(-56f, Quay + 0.7f, 0f), PickupKind.ThighPads);
        b.Item(new Vector3(Stern + 6f, MainDeck + 0.7f, -8f), PickupKind.BodyArmor);
        b.Item(new Vector3(70f, MainDeck + 0.7f, 8f), PickupKind.ShieldBelt);
        b.Item(new Vector3(46f, 5.4f, -10f), PickupKind.SuperHealth);
        b.Item(new Vector3(28f, MainDeck + 0.7f, 0f), PickupKind.HealthPack);
        b.Ammo(new Vector3(-66f, Quay + 0.7f, 6f), AmmoKind.Rockets);
        b.Ammo(new Vector3(Stern + 18f, MainDeck + 0.7f, -8f), AmmoKind.FlakShells);
        b.Ammo(new Vector3(56f, MainDeck + 0.7f, -10f), AmmoKind.Bullets);

        return b.Build(gl);
    }
}
