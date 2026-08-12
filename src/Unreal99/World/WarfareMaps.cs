using System.Numerics;
using Silk.NET.OpenGL;
using Unreal99.Core;
using Unreal99.Game;
using Unreal99.Rendering;
using Unreal99.UI;

namespace Unreal99.World;

/// <summary>
/// Warfare arenas, rebuilt from the UT3 originals. Every link setup, node role and vehicle roster
/// here comes from the map's own documented loadout — the Regular link setup in each case, since
/// that is what the game ships with and what the bots are tuned against.
///
/// The Axon-versus-Necris maps deliberately park two vehicles on the same pad, one per side. That
/// is how the originals do it: a node is worth a Manta to one team and a Viper to the other, and
/// which one you get is decided by who holds the node.
/// </summary>
public static partial class Maps
{
    /// <summary>Raised platform a node stands on, so it reads as a built emplacement.</summary>
    private static void NodePad(LevelBuilder b, Vector3 centre, float radius, MatId material)
    {
        b.Solid(centre + new Vector3(-radius, -0.6f, -radius),
                centre + new Vector3(radius, 0f, radius), material, true, 0.6f);
    }

    /// <summary>
    /// The shelter every node in these maps sits inside: four corner pillars, a roof, and open
    /// sides. A node on a bare slab reads as a parking space from any distance; this gives the
    /// eye something to range against and gives infantry cover to fight over the node from.
    /// </summary>
    private static void NodeShelter(LevelBuilder b, Vector3 centre, float radius, float height,
        MatId pillar, MatId roof)
    {
        for (int i = -1; i <= 1; i += 2)
            for (int j = -1; j <= 1; j += 2)
                b.Solid(centre + new Vector3(i * radius - 1.3f, 0f, j * radius - 1.3f),
                        centre + new Vector3(i * radius + 1.3f, height, j * radius + 1.3f),
                        pillar, true, 0.8f);
        b.Solid(centre + new Vector3(-radius - 2f, height, -radius - 2f),
                centre + new Vector3(radius + 2f, height + 1f, radius + 2f), roof, true, 0.9f);
        // Half-height screens on two sides, so the shelter is cover rather than just a canopy.
        for (int i = -1; i <= 1; i += 2)
            b.Solid(centre + new Vector3(-radius, 0f, i * radius - 0.6f),
                    centre + new Vector3(radius, height * 0.42f, i * radius + 0.6f), pillar, true, 0.8f);
        b.AddLight(centre + new Vector3(0f, height - 0.6f, 0f), new Vector3(0.8f, 0.86f, 1f),
            radius * 2.4f, 3.2f);
    }

    /// <summary>
    /// A blocky prefab hut, scattered to give open ground something to hide behind. Both long
    /// walls carry a doorway: a sealed box is a rock with extra triangles, and worse, the nav
    /// graph happily puts cells inside one and then strands anything that ends up in there.
    /// </summary>
    private static void Outbuilding(LevelBuilder b, Vector3 at, float w, float d, float h, MatId mat)
    {
        float door = MathF.Min(2.6f, w * 0.7f);
        for (int s = -1; s <= 1; s += 2)
        {
            Vector3 lo = at + new Vector3(-w, 0f, s * d - (s < 0 ? 0f : 0.9f));
            Vector3 hi = at + new Vector3(w, h, s * d + (s < 0 ? 0.9f : 0f));
            b.Solid(lo, new Vector3(-door, hi.Y, hi.Z) + new Vector3(at.X, 0f, 0f), mat, true, 0.7f);
            b.Solid(new Vector3(at.X + door, lo.Y, lo.Z), hi, mat, true, 0.7f);
            b.Solid(new Vector3(at.X - door, lo.Y + 3.2f, lo.Z), new Vector3(at.X + door, hi.Y, hi.Z),
                mat, true, 0.7f);
        }
        b.Solid(at + new Vector3(-w, 0f, -d), at + new Vector3(-w + 0.9f, h, d), mat, true, 0.7f);
        b.Solid(at + new Vector3(w - 0.9f, 0f, -d), at + new Vector3(w, h, d), mat, true, 0.7f);
        b.Solid(at + new Vector3(-w - 0.6f, h, -d - 0.6f), at + new Vector3(w + 0.6f, h + 0.8f, d + 0.6f),
            MatId.MetalGrate, true, 0.9f);
    }

    // ================================================================ WAR-托蘭

    /// <summary>
    /// Torlan, rebuilt for Warfare. The UT3 version keeps the delta but rewires it: both cores
    /// feed their own prime node, the two primes are joined by the East and West road nodes, and
    /// the two tank nodes and the centre bridge node sit outside the chain as support nodes.
    ///
    /// The Necris variant is the same ground with the blue side re-equipped, which is exactly what
    /// the original is — one map, two vehicle sets, mirrored pad for pad.
    /// </summary>
    private static Level BuildWarTorlan(GL gl, bool necris)
    {
        var b = new LevelBuilder(
            necris ? Loc.MapWarTorlanNecris : Loc.MapWarTorlan,
            necris ? Loc.MapWarTorlanNecrisDesc : Loc.MapWarTorlanDesc);
        var env = b.Level.Environment;
        env.SunDirection = Vector3.Normalize(new Vector3(-0.30f, -0.76f, -0.58f));
        env.SunColor = necris ? new Vector3(2.4f, 2.3f, 2.8f) : new Vector3(3.5f, 3.25f, 2.75f);
        env.AmbientSky = necris ? new Vector3(0.24f, 0.24f, 0.40f) : new Vector3(0.30f, 0.34f, 0.44f);
        env.AmbientGround = new Vector3(0.18f, 0.17f, 0.15f);
        env.EnvIntensity = 0.68f;
        env.SkyTop = necris ? new Vector3(0.06f, 0.06f, 0.20f) : new Vector3(0.10f, 0.22f, 0.48f);
        env.SkyHorizon = necris ? new Vector3(0.34f, 0.26f, 0.42f) : new Vector3(0.62f, 0.62f, 0.55f);
        env.SkyGround = new Vector3(0.26f, 0.23f, 0.20f);
        env.StarStrength = necris ? 0.5f : 0f;
        env.CloudStrength = 0.85f;
        env.FogColor = necris ? new Vector3(0.30f, 0.26f, 0.40f) : new Vector3(0.60f, 0.60f, 0.55f);
        env.FogDensity = 0.0038f;

        const float HX = 132f, HZ = 104f, Ground = 0f;

        // The delta itself: a shallow river straight through the middle, crossed by the bridge the
        // centre node sits under. Driveable in and out, because it is a route rather than a moat.
        //
        // The channel has to be left out of the base slab. Previously the arena floor was laid as
        // one block filling everything up to Ground, and the riverbed, its water volume and both
        // banks were then authored *inside* it — so the whole river was buried and that stretch of
        // the map was simply flat rock. The water never showed, and the two Scorpions the centre
        // section parks "in the riverbed under the bridge" spawned inside solid ground.
        const float Bank = 20f;      // where the banks meet the flat ground
        const float Shore = 13f;     // where they meet the water
        const float RiverBed = Ground - 1.6f;

        foreach (int s in new[] { -1, 1 })
            b.Solid(new Vector3(-HX, -6f, s > 0 ? Bank : -HZ), new Vector3(HX, Ground, s > 0 ? HZ : -Bank),
                MatId.Rock, true, 0.35f);
        b.Room(new Vector3(-HX - 4f, -6f, -HZ - 4f), new Vector3(HX + 4f, 64f, HZ + 4f), 4f,
            MatId.Rock, MatId.Rock, MatId.Rock, withCeiling: false, withFloor: false);

        // Bed, then the banks up to the flat ground. 1.6 m of drop over a 7 m bank is about 1:4,
        // well inside the roughly 1:3 the navigation graph will route over — a steeper cut would
        // strand on foot anyone who went down for the Redeemer under the bridge.
        b.Solid(new Vector3(-HX, -6f, -Shore), new Vector3(HX, RiverBed, Shore), MatId.Concrete, true, 0.5f);
        b.Water(new Vector3(-HX, RiverBed, -Shore), new Vector3(HX, Ground - 0.45f, Shore));
        for (int s = -1; s <= 1; s += 2)
            b.Ramp(new Vector3(-HX, RiverBed, s > 0 ? Shore : -Bank),
                new Vector3(HX, Ground, s > 0 ? Bank : -Shore), s < 0 ? 3 : 2,
                MatId.Rock, true, 0.4f);

        var rng = new Rng(0x7031);
        int redCore = -1, blueCore = -1;
        var basePos = new Vector3[2];

        for (int side = 0; side < 2; side++)
        {
            float sign = side == 0 ? -1f : 1f;
            Team team = side == 0 ? Team.Red : Team.Blue;
            bool necrisSide = necris && team == Team.Blue;
            MatId teamMat = team == Team.Red ? MatId.TeamRed : MatId.TeamBlue;
            float x = sign * (HX - 24f);

            b.Solid(new Vector3(x - 22f, Ground, -28f), new Vector3(x + 22f, Ground + 1.2f, 28f),
                MatId.Concrete, true, 0.6f);
            b.Solid(new Vector3(x + sign * 22f, Ground, -28f), new Vector3(x + sign * 25f, Ground + 8f, 28f),
                teamMat, true, 0.7f);
            // Upper deck over the core, which is where the original parks its flyer.
            b.Solid(new Vector3(x - 8f, Ground + 7f, -20f), new Vector3(x + 8f, Ground + 8f, -8f),
                MatId.MetalGrate, true, 0.9f);
            b.Stairs(new Vector3(x - 8f, Ground + 1.2f, -8f), new Vector3(x - 8f, Ground + 7f, -20f),
                5f, 9, MatId.Concrete, alongX: false);
            b.AddLight(new Vector3(x, Ground + 15f, 0f), GameTypes.TeamColor(team) * 1.3f, 42f, 7f);

            Vector3 corePos = new(x, Ground + 1.2f, 0f);
            basePos[side] = corePos;
            int core = b.AddPowerNode(corePos, team == Team.Red ? Loc.NodeRedCore : Loc.NodeBlueCore,
                [], isCore: true, team: team);
            if (team == Team.Red) redCore = core; else blueCore = core;

            // Base loadout, straight from the original: a light hover, a heavy support car and a
            // flyer on the upper level, plus the team's orb.
            b.AddVehicle(necrisSide ? VehicleKind.Viper : VehicleKind.Manta,
                new Vector3(x - sign * 11f, Ground + 2.6f, -13f), sign < 0 ? 90f : -90f, team);
            b.AddVehicle(necrisSide ? VehicleKind.Nemesis : VehicleKind.Hellbender,
                new Vector3(x - sign * 11f, Ground + 1.6f, 13f), sign < 0 ? 90f : -90f, team);
            b.AddVehicle(necrisSide ? VehicleKind.Fury : VehicleKind.Raptor,
                new Vector3(x, Ground + 9f, -14f), sign < 0 ? 90f : -90f, team);
            b.AddOrbSpawn(corePos + new Vector3(-sign * 5f, 1.1f, 0f), team);

            // Every Warfare base carries a Longbow AVRiL — these are vehicle maps, and the
            // original hands infantry the answer to armour at the door.
            b.Weapon(new Vector3(x - sign * 5f, Ground + 2.1f, -7f), WeaponKind.Avril);
            b.Weapon(new Vector3(x - sign * 5f, Ground + 2.1f, 7f), WeaponKind.ShockRifle);
            b.Weapon(new Vector3(x, Ground + 8.9f, -14f), WeaponKind.SniperRifle);
            b.Ammo(new Vector3(x - sign * 8f, Ground + 2.0f, -7f), AmmoKind.AvrilMissiles);
            b.Locker(new Vector3(x - sign * 8f, Ground + 1.4f, 0f),
                WeaponKind.ShockRifle, WeaponKind.LinkGun, WeaponKind.Stinger, WeaponKind.FlakCannon);
            b.Item(new Vector3(x, Ground + 2.0f, -10f), PickupKind.BodyArmor);
            b.Item(new Vector3(x, Ground + 2.0f, 10f), PickupKind.HealthPack);
            for (int i = 0; i < 4; i++)
                b.Spawn(new Vector3(x - sign * (3f + i * 3f), Ground + 1.4f, -15f + i * 10f),
                    sign < 0 ? 90f : -90f, team);
        }

        // --- the seven-node layout ---
        // The primes sit close to their cores, as in the original: the base-to-prime run is meant
        // to be a few seconds, not a trek. Pushing them out to mid-field made the opening minute
        // of every match a walk, and the traversal harness caught it as zero captures.
        Vector3 redPrime = new(-80f, Ground + 1.2f, 20f);
        Vector3 bluePrime = new(80f, Ground + 1.2f, -20f);
        Vector3 westRoad = new(-26f, Ground + 1.2f, -62f);
        Vector3 eastRoad = new(26f, Ground + 1.2f, 62f);
        Vector3 northTank = new(-30f, Ground + 1.2f, 70f);
        Vector3 southTank = new(30f, Ground + 1.2f, -70f);
        Vector3 centreRoad = new(0f, Ground + 1.2f, 0f);

        int iRedPrime = b.AddPowerNode(redPrime, Loc.NodeRedPrime, []);
        int iBluePrime = b.AddPowerNode(bluePrime, Loc.NodeBluePrime, []);
        int iWest = b.AddPowerNode(westRoad, Loc.NodeWestRoad, []);
        int iEast = b.AddPowerNode(eastRoad, Loc.NodeEastRoad, []);
        int iNorth = b.AddPowerNode(northTank, Loc.NodeNorthTank, [], role: NodeRole.Support);
        int iSouth = b.AddPowerNode(southTank, Loc.NodeSouthTank, [], role: NodeRole.Support);
        int iCentre = b.AddPowerNode(centreRoad + new Vector3(0f, 8f, 0f), Loc.NodeCenterRoad, [],
            role: NodeRole.Support);

        // Regular link setup: core → own prime, and the two primes joined by both road nodes.
        b.LinkPowerNodes(redCore, [iRedPrime]);
        b.LinkPowerNodes(iRedPrime, [redCore, iWest, iEast]);
        b.LinkPowerNodes(iWest, [iRedPrime, iBluePrime]);
        b.LinkPowerNodes(iEast, [iRedPrime, iBluePrime]);
        b.LinkPowerNodes(iBluePrime, [blueCore, iWest, iEast]);
        b.LinkPowerNodes(blueCore, [iBluePrime]);

        // --- prime nodes: a pad, cover and the light hover pair ---
        foreach (var (pos, index, team) in new[]
                 {
                     (redPrime, iRedPrime, Team.Red), (bluePrime, iBluePrime, Team.Blue),
                 })
        {
            NodePad(b, pos, 13f, MatId.Concrete);
            NodeShelter(b, pos, 8f, 8.5f, MatId.TechPanelDark, MatId.MetalGrate);
            for (int i = 0; i < 3; i++)
                b.Solid(pos + new Vector3(-12f + i * 11f, 0f, -12f),
                        pos + new Vector3(-8f + i * 11f, 4.5f, -9f), MatId.TechPanelDark, true, 0.7f);
            b.Weapon(pos + new Vector3(-6f, 1f, 6f), WeaponKind.FlakCannon);
            b.Locker(pos + new Vector3(0f, 0.4f, 11f),
                WeaponKind.ShockRifle, WeaponKind.LinkGun, WeaponKind.Stinger, WeaponKind.Avril);
            b.Weapon(pos + new Vector3(6f, 1f, 6f), WeaponKind.Stinger);
            b.Item(pos + new Vector3(0f, 0.9f, -6f), PickupKind.ThighPads);
            b.Spawn(pos + new Vector3(0f, 0.4f, 9f), 0f, team);

            // Both a Manta and a Scorpion here in the original, one per side in the Necris cut.
            b.AddVehicle(VehicleKind.Manta, pos + new Vector3(-9f, 1.4f, 0f), 0f,
                necris ? Team.Red : Team.None);
            b.AddVehicle(VehicleKind.Scorpion, pos + new Vector3(9f, 0.7f, 0f), 0f,
                necris ? Team.Red : Team.None);
            if (!necris) continue;
            b.AddVehicle(VehicleKind.Viper, pos + new Vector3(-9f, 1.4f, 4f), 0f, Team.Blue);
            b.AddVehicle(VehicleKind.Scavenger, pos + new Vector3(9f, 1.0f, 4f), 0f, Team.Blue);
        }

        // --- road nodes: the artillery pair ---
        foreach (var (pos, _) in new[] { (westRoad, iWest), (eastRoad, iEast) })
        {
            NodePad(b, pos, 11f, MatId.Concrete);
            NodeShelter(b, pos, 7f, 7.5f, MatId.RustMetal, MatId.MetalGrate);
            b.Solid(pos + new Vector3(-11f, 0f, 6f), pos + new Vector3(11f, 5f, 9f), MatId.RustMetal, true, 0.8f);
            b.Weapon(pos + new Vector3(0f, 1f, -6f), WeaponKind.Avril);
            b.Locker(pos + new Vector3(-6f, 0.4f, -6f),
                WeaponKind.LinkGun, WeaponKind.Stinger, WeaponKind.RocketLauncher, WeaponKind.SniperRifle);
            b.Item(pos + new Vector3(-5f, 0.9f, -6f), PickupKind.HealthPack);
            b.Spawn(pos + new Vector3(5f, 0.4f, -8f), 180f);
            b.AddVehicle(VehicleKind.Spma, pos + new Vector3(0f, 1.4f, 0f), 180f,
                necris ? Team.Red : Team.None);
            if (necris) b.AddVehicle(VehicleKind.Nightshade, pos + new Vector3(6f, 1.4f, 0f), 180f, Team.Blue);
        }

        // --- tank nodes: the support nodes worth detouring for ---
        foreach (var (pos, _) in new[] { (northTank, iNorth), (southTank, iSouth) })
        {
            NodePad(b, pos, 14f, MatId.Concrete);
            NodeShelter(b, pos, 9f, 9f, MatId.RustMetal, MatId.ArmorPlate);
            for (int i = -1; i <= 1; i += 2)
                b.Solid(pos + new Vector3(i * 12f, 0f, -13f), pos + new Vector3(i * 14f, 6f, 13f),
                    MatId.RustMetal, true, 0.8f);
            b.Weapon(pos + new Vector3(0f, 1f, 8f), WeaponKind.FlakCannon);
            b.Item(pos + new Vector3(0f, 0.9f, -8f), PickupKind.BodyArmor);
            b.Spawn(pos + new Vector3(0f, 0.4f, 0f), 0f);
            b.AddVehicle(VehicleKind.Goliath, pos + new Vector3(-5f, 1.8f, 0f), 0f,
                necris ? Team.Red : Team.None);
            if (necris) b.AddVehicle(VehicleKind.Darkwalker, pos + new Vector3(7f, 4.6f, 0f), 0f, Team.Blue);
        }

        // --- the centre bridge, with the node underneath it and the gunship on top ---
        b.Solid(centreRoad + new Vector3(-46f, 7f, -9f), centreRoad + new Vector3(46f, 8f, 9f),
            MatId.Concrete, true, 0.7f);
        for (int i = -2; i <= 2; i++)
            b.Solid(centreRoad + new Vector3(i * 18f - 2f, -3.4f, -9f),
                    centreRoad + new Vector3(i * 18f + 2f, 7f, 9f), MatId.Concrete, true, 0.8f);
        for (int s = -1; s <= 1; s += 2)
            b.Ramp(centreRoad + new Vector3(s * 46f, 0f, -9f), centreRoad + new Vector3(s * 70f, 8f, 9f),
                s < 0 ? 3 : 2, MatId.Concrete, true, 0.6f);
        b.Weapon(centreRoad + new Vector3(0f, 8.9f, 0f), WeaponKind.SniperRifle);
        b.Item(centreRoad + new Vector3(-14f, 8.8f, 0f), PickupKind.ShieldBelt);
        b.Weapon(centreRoad + new Vector3(14f, -2.3f, 0f), WeaponKind.Redeemer, 90f);
        b.AddOrbSpawn(centreRoad + new Vector3(0f, 9f, 5f), Team.Red, iCentre);
        b.AddOrbSpawn(centreRoad + new Vector3(0f, 9f, -5f), Team.Blue, iCentre);
        // Scorpions in the riverbed under the bridge, the gunship on the deck above it.
        b.AddVehicle(VehicleKind.Scorpion, centreRoad + new Vector3(-20f, -2.6f, 0f), 90f,
            necris ? Team.Red : Team.None);
        b.AddVehicle(VehicleKind.Scorpion, centreRoad + new Vector3(20f, -2.6f, 0f), -90f,
            necris ? Team.Red : Team.None);
        b.AddVehicle(necris ? VehicleKind.Goliath : VehicleKind.Cicada,
            centreRoad + new Vector3(-30f, necris ? 9.8f : 12f, 0f), 90f, necris ? Team.Red : Team.None);
        if (necris)
        {
            b.AddVehicle(VehicleKind.Scavenger, centreRoad + new Vector3(-20f, -2.2f, 5f), 90f, Team.Blue);
            b.AddVehicle(VehicleKind.Scavenger, centreRoad + new Vector3(20f, -2.2f, 5f), -90f, Team.Blue);
            b.AddVehicle(VehicleKind.Darkwalker, centreRoad + new Vector3(-30f, 12.6f, 0f), 90f, Team.Blue);
        }
        for (int s = -1; s <= 1; s += 2)
            b.AddJumpPad(centreRoad + new Vector3(s * 40f, -3.2f, 0f),
                centreRoad + new Vector3(s * 36f, 9f, 0f), new Vector3(0.45f, 0.85f, 1f));

        // --- scattered rock so the open ground reads as terrain ---
        for (int i = 0; i < 44; i++)
        {
            float px = rng.Range(-HX + 14f, HX - 14f);
            float pz = rng.Range(-HZ + 14f, HZ - 14f);
            if (MathF.Abs(pz) < 22f) continue;
            if (Vector3.Distance(new Vector3(px, Ground, pz), basePos[0]) < 34f) continue;
            if (Vector3.Distance(new Vector3(px, Ground, pz), basePos[1]) < 34f) continue;
            if (i % 5 == 0)
            {
                Outbuilding(b, new Vector3(px, Ground, pz), rng.Range(4f, 7f), rng.Range(4f, 7f),
                    rng.Range(5f, 9f), MatId.RustMetal);
                continue;
            }
            float sz = rng.Range(1.8f, 4.6f);
            b.Solid(new Vector3(px - sz, Ground, pz - sz),
                    new Vector3(px + sz, Ground + rng.Range(2.2f, 5.5f), pz + sz), MatId.Rock, true, 0.6f);
        }

        return b.Build(gl);
    }

    // ================================================================ WAR-寧謐林地

    /// <summary>
    /// Serenity. Three nodes in a straight line — blue core, blue prime, red prime, red core — and
    /// off to one side the mine, a vehicle node with a 40-second clock that hands the holder a
    /// Leviathan. Only one Leviathan exists at a time, so the mine is the whole match.
    /// </summary>
    private static Level BuildSerenity(GL gl)
    {
        var b = new LevelBuilder(Loc.MapSerenity, Loc.MapSerenityDesc);
        var env = b.Level.Environment;
        env.SunDirection = Vector3.Normalize(new Vector3(-0.40f, -0.72f, -0.44f));
        env.SunColor = new Vector3(3.1f, 3.2f, 2.7f);
        env.AmbientSky = new Vector3(0.26f, 0.34f, 0.30f);
        env.AmbientGround = new Vector3(0.12f, 0.16f, 0.10f);
        env.EnvIntensity = 0.62f;
        env.SkyTop = new Vector3(0.10f, 0.24f, 0.40f);
        env.SkyHorizon = new Vector3(0.52f, 0.60f, 0.52f);
        env.SkyGround = new Vector3(0.14f, 0.18f, 0.12f);
        env.StarStrength = 0f;
        env.CloudStrength = 0.7f;
        env.FogColor = new Vector3(0.42f, 0.52f, 0.44f);
        env.FogDensity = 0.0045f;

        const float HX = 120f, HZ = 86f, Ground = 0f;
        // This river runs along X, so the valley floor is laid as the two banks either side of it
        // rather than one slab the river would then be buried inside.
        const float Bank = 24f, Shore = 16f, RiverBed = Ground - 1.5f;
        foreach (int s in new[] { -1, 1 })
            b.Solid(new Vector3(s > 0 ? Bank : -HX, -6f, -HZ), new Vector3(s > 0 ? HX : -Bank, Ground, HZ),
                MatId.Rock, true, 0.4f);
        b.Room(new Vector3(-HX - 4f, -6f, -HZ - 4f), new Vector3(HX + 4f, 58f, HZ + 4f), 4f,
            MatId.Rock, MatId.Rock, MatId.Rock, withCeiling: false, withFloor: false);

        // The river the valley is named for, running north to south past the prime nodes.
        b.Solid(new Vector3(-Shore, -6f, -HZ), new Vector3(Shore, RiverBed, HZ), MatId.Concrete, true, 0.5f);
        b.Water(new Vector3(-Shore, RiverBed, -HZ), new Vector3(Shore, Ground - 0.35f, HZ));
        for (int s = -1; s <= 1; s += 2)
            b.Ramp(new Vector3(s > 0 ? Shore : -Bank, RiverBed, -HZ),
                new Vector3(s > 0 ? Bank : -Shore, Ground, HZ), 0, MatId.Rock, true, 0.4f);

        var rng = new Rng(0x5E12);
        int redCore = -1, blueCore = -1;

        // Bases at the two polar ends: red south-east, blue north-east, as in the original.
        for (int side = 0; side < 2; side++)
        {
            Team team = side == 0 ? Team.Red : Team.Blue;
            float z = side == 0 ? -(HZ - 20f) : HZ - 20f;
            float x = HX - 26f;
            MatId teamMat = team == Team.Red ? MatId.TeamRed : MatId.TeamBlue;

            b.Solid(new Vector3(x - 20f, Ground, z - 16f), new Vector3(x + 20f, Ground + 1.2f, z + 16f),
                MatId.Concrete, true, 0.6f);
            b.Solid(new Vector3(x + 20f, Ground, z - 16f), new Vector3(x + 23f, Ground + 7f, z + 16f),
                teamMat, true, 0.7f);
            b.AddLight(new Vector3(x, Ground + 13f, z), GameTypes.TeamColor(team) * 1.25f, 38f, 6.5f);

            Vector3 corePos = new(x, Ground + 1.2f, z);
            int core = b.AddPowerNode(corePos, team == Team.Red ? Loc.NodeRedCore : Loc.NodeBlueCore,
                [], isCore: true, team: team);
            if (team == Team.Red) redCore = core; else blueCore = core;

            b.AddVehicle(VehicleKind.Manta, corePos + new Vector3(-12f, 1.4f, -8f), 180f, team);
            b.AddVehicle(VehicleKind.Scorpion, corePos + new Vector3(-12f, 0.7f, 8f), 180f, team);
            b.AddVehicle(VehicleKind.Hellbender, corePos + new Vector3(-17f, 0.4f, 0f), 180f, team);
            // A Goliath just outside each base, per the original's "northwest of the Red Base".
            b.AddVehicle(VehicleKind.Goliath, corePos + new Vector3(-30f, 0.8f, side == 0 ? 14f : -14f),
                180f, team);
            b.AddOrbSpawn(corePos + new Vector3(-5f, 1.1f, 0f), team);

            b.Weapon(corePos + new Vector3(-4f, 0.9f, -6f), WeaponKind.SniperRifle);
            b.Weapon(corePos + new Vector3(-4f, 0.9f, 6f), WeaponKind.Avril);
            b.Locker(corePos + new Vector3(-8f, 0.2f, 0f),
                WeaponKind.ShockRifle, WeaponKind.LinkGun, WeaponKind.Stinger, WeaponKind.FlakCannon);
            b.Item(corePos + new Vector3(0f, 0.8f, -9f), PickupKind.ShieldBelt);
            b.Item(corePos + new Vector3(-24f, 0.8f, 0f), PickupKind.SuperHealth);
            for (int i = 0; i < 4; i++)
                b.Spawn(corePos + new Vector3(-6f - i * 3f, 0.2f, -9f + i * 6f), 180f, team);
        }

        // --- the two prime nodes, west of the map and joined to each other ---
        Vector3 redPrime = new(-52f, Ground + 1.2f, -34f);
        Vector3 bluePrime = new(-52f, Ground + 1.2f, 34f);
        int iRed = b.AddPowerNode(redPrime, Loc.NodeRedPrime, []);
        int iBlue = b.AddPowerNode(bluePrime, Loc.NodeBluePrime, []);
        b.LinkPowerNodes(redCore, [iRed]);
        b.LinkPowerNodes(iRed, [redCore, iBlue]);
        b.LinkPowerNodes(iBlue, [blueCore, iRed]);
        b.LinkPowerNodes(blueCore, [iBlue]);

        foreach (var (pos, team) in new[] { (redPrime, Team.Red), (bluePrime, Team.Blue) })
        {
            NodePad(b, pos, 13f, MatId.Concrete);
            NodeShelter(b, pos, 8f, 8f, MatId.RustMetal, MatId.MetalGrate);
            // Upper walkway with the sniper spot, reached by a short flight of steps.
            b.Solid(pos + new Vector3(-13f, 5f, -5f), pos + new Vector3(-3f, 6f, 5f), MatId.MetalGrate, true, 0.9f);
            b.Stairs(pos + new Vector3(-3f, 0f, -4f), pos + new Vector3(-3f, 5f, 4f), 4f, 8,
                MatId.Concrete, alongX: false);
            b.Weapon(pos + new Vector3(-8f, 6.9f, 0f), WeaponKind.SniperRifle);
            b.Weapon(pos + new Vector3(6f, 1f, 0f), WeaponKind.FlakCannon);
            b.Item(pos + new Vector3(0f, 0.9f, 8f), PickupKind.HealthPack);
            b.Item(pos + new Vector3(0f, 0.9f, -8f), PickupKind.ThighPads);
            b.Spawn(pos + new Vector3(8f, 0.4f, 0f), -90f, team);
            b.AddVehicle(VehicleKind.Manta, pos + new Vector3(9f, 1.4f, -6f), 0f);
            b.AddVehicle(VehicleKind.Scorpion, pos + new Vector3(9f, 0.7f, 6f), 0f);
        }
        // The Redeemer sits between the primes on the lower path, as in the original.
        b.Weapon(new Vector3(-52f, Ground + 0.9f, 0f), WeaponKind.Redeemer, 100f);
        b.Item(new Vector3(-40f, Ground + 0.8f, 0f), PickupKind.DamageAmp, 90f);

        // --- the mine: an unlinked vehicle node that builds a Leviathan ---
        Vector3 mine = new(34f, Ground + 1.2f, 0f);
        b.Solid(mine + new Vector3(-18f, -0.6f, -18f), mine + new Vector3(18f, 0f, 18f), MatId.Concrete, true, 0.6f);
        b.Solid(mine + new Vector3(12f, 0f, -18f), mine + new Vector3(18f, 9f, 18f), MatId.RustMetal, true, 0.8f);
        // Elevated walkways north and south of it, which is where the Berserk and snipers live.
        for (int s = -1; s <= 1; s += 2)
        {
            b.Solid(mine + new Vector3(-16f, 6f, s * 15f - 3f), mine + new Vector3(10f, 7f, s * 15f + 3f),
                MatId.MetalGrate, true, 0.9f);
            b.Stairs(mine + new Vector3(-16f, 0f, s * 15f - 3f), mine + new Vector3(-16f, 6f, s * 15f + 3f),
                5f, 9, MatId.Concrete, alongX: false);
            b.Weapon(mine + new Vector3(-4f, 7.9f, s * 15f), WeaponKind.SniperRifle);
            b.Item(mine + new Vector3(4f, 7.8f, s * 15f), PickupKind.DamageAmp, 110f);
        }
        int iMine = b.AddPowerNode(mine, Loc.NodeMine, [], role: NodeRole.Vehicle,
            countdownSeconds: 40f, rewardVehicle: VehicleKind.Leviathan,
            rewardPosition: mine + new Vector3(-14f, -0.6f, 0f), rewardYawDegrees: -90f);
        // A Hellbender appears with the node and is scrapped when the clock runs out.
        b.AddVehicle(VehicleKind.Hellbender, mine + new Vector3(0f, 0.4f, 10f), -90f);
        // On the walkways, not between them: an orb spawned over thin air is one the bots path
        // underneath and then stand beneath forever.
        b.AddOrbSpawn(mine + new Vector3(-6f, 7.2f, -15f), Team.Red, iMine);
        b.AddOrbSpawn(mine + new Vector3(-6f, 7.2f, 15f), Team.Blue, iMine);
        b.Weapon(mine + new Vector3(0f, 0.9f, -10f), WeaponKind.Avril);
        b.Locker(mine + new Vector3(-6f, 0.2f, -10f),
            WeaponKind.ShockRifle, WeaponKind.LinkGun, WeaponKind.Stinger, WeaponKind.RocketLauncher);
        b.Item(mine + new Vector3(6f, 0.8f, 0f), PickupKind.BodyArmor);
        b.Spawn(mine + new Vector3(-8f, 0.2f, -8f), -90f);
        b.Spawn(mine + new Vector3(-8f, 0.2f, 8f), -90f);

        // --- trees ---
        for (int i = 0; i < 70; i++)
        {
            float px = rng.Range(-HX + 10f, HX - 10f);
            float pz = rng.Range(-HZ + 10f, HZ - 10f);
            if (MathF.Abs(px) < 26f) continue;                       // keep the river clear
            if (Vector3.Distance(new Vector3(px, 0f, pz), mine) < 26f) continue;
            if (Vector3.Distance(new Vector3(px, 0f, pz), redPrime) < 20f) continue;
            if (Vector3.Distance(new Vector3(px, 0f, pz), bluePrime) < 20f) continue;
            if (px > HX - 50f && MathF.Abs(MathF.Abs(pz) - (HZ - 20f)) < 22f) continue;
            // Trunk plus canopy. A bare pole reads as a lamp post from any distance; the crown is
            // what makes the valley look like the forest the map is named for.
            float h = rng.Range(9f, 17f);
            b.Prism(new Vector3(px, Ground + h * 0.5f, pz), rng.Range(0.7f, 1.2f), h, 6, MatId.RustMetal);
            // Rock reads as dark foliage under this map's green light; the team materials are far
            // too saturated and turned the forest into a field of blue mushrooms.
            b.Prism(new Vector3(px, Ground + h + 1.8f, pz), rng.Range(3.4f, 5.6f), 7.2f, 7, MatId.Rock);
        }
        // A handful of supply huts around the depot, which is what the mine is here for.
        for (int i = 0; i < 8; i++)
            Outbuilding(b, new Vector3(rng.Range(20f, 70f), Ground, rng.Range(-60f, 60f)),
                rng.Range(4f, 7f), rng.Range(4f, 7f), rng.Range(5f, 8f), MatId.RustMetal);

        return b.Build(gl);
    }

    // ================================================================ WAR-雪崩山道

    /// <summary>
    /// Avalanche. Two bases either side of a hollow mountain, each with its own prime node, and
    /// three nodes inside the mountain: East, West and Centre. In the Regular setup the primes
    /// join through the Centre node and the two side nodes are countdown nodes — holding one for a
    /// minute destroys the enemy's prime outright, which is a far bigger swing than chipping a core.
    /// </summary>
    private static Level BuildAvalanche(GL gl)
    {
        var b = new LevelBuilder(Loc.MapAvalanche, Loc.MapAvalancheDesc);
        var env = b.Level.Environment;
        env.SunDirection = Vector3.Normalize(new Vector3(-0.28f, -0.66f, -0.70f));
        env.SunColor = new Vector3(3.4f, 3.5f, 3.9f);
        env.AmbientSky = new Vector3(0.42f, 0.48f, 0.62f);
        env.AmbientGround = new Vector3(0.30f, 0.34f, 0.42f);
        env.EnvIntensity = 0.80f;
        env.SkyTop = new Vector3(0.24f, 0.38f, 0.62f);
        env.SkyHorizon = new Vector3(0.74f, 0.80f, 0.90f);
        env.SkyGround = new Vector3(0.55f, 0.60f, 0.68f);
        env.StarStrength = 0f;
        env.CloudStrength = 1f;
        env.FogColor = new Vector3(0.76f, 0.82f, 0.92f);
        env.FogDensity = 0.0040f;

        const float HX = 128f, HZ = 78f, Ground = 0f;
        b.Solid(new Vector3(-HX, -6f, -HZ), new Vector3(HX, Ground, HZ), MatId.Concrete, true, 0.4f);
        b.Room(new Vector3(-HX - 4f, -6f, -HZ - 4f), new Vector3(HX + 4f, 70f, HZ + 4f), 4f,
            MatId.Concrete, MatId.Rock, MatId.Rock, withCeiling: false, withFloor: false);

        // The mountain: a shell around the middle with a hollow interior and two mouths.
        for (int s = -1; s <= 1; s += 2)
        {
            b.Solid(new Vector3(s * 36f, Ground, -HZ), new Vector3(s * 46f, 40f, -22f), MatId.Rock, true, 0.7f);
            b.Solid(new Vector3(s * 36f, Ground, 22f), new Vector3(s * 46f, 40f, HZ), MatId.Rock, true, 0.7f);
        }
        b.Solid(new Vector3(-46f, 26f, -HZ), new Vector3(46f, 40f, HZ), MatId.Rock, true, 0.7f);
        b.Solid(new Vector3(-46f, Ground, -HZ), new Vector3(46f, 26f, -HZ + 12f), MatId.Rock, true, 0.7f);
        b.Solid(new Vector3(-46f, Ground, HZ - 12f), new Vector3(46f, 26f, HZ), MatId.Rock, true, 0.7f);
        b.AddLight(new Vector3(0f, 20f, 0f), new Vector3(0.7f, 0.82f, 1f), 70f, 4.5f);

        var rng = new Rng(0xA71C);
        int redCore = -1, blueCore = -1;
        Vector3 redPrime = new(-70f, Ground + 1.2f, 0f);
        Vector3 bluePrime = new(70f, Ground + 1.2f, 0f);

        for (int side = 0; side < 2; side++)
        {
            float sign = side == 0 ? -1f : 1f;
            Team team = side == 0 ? Team.Red : Team.Blue;
            bool necrisSide = team == Team.Blue;   // Avalanche is Axon red against Necris blue
            MatId teamMat = team == Team.Red ? MatId.TeamRed : MatId.TeamBlue;
            float x = sign * (HX - 22f);

            b.Solid(new Vector3(x - 20f, Ground, -24f), new Vector3(x + 20f, Ground + 1.2f, 24f),
                MatId.Concrete, true, 0.6f);
            b.Solid(new Vector3(x + sign * 20f, Ground, -24f), new Vector3(x + sign * 23f, Ground + 9f, 24f),
                teamMat, true, 0.7f);
            // Corner towers, which is where the fixed turrets live in the original.
            for (int i = -1; i <= 1; i += 2)
                b.Solid(new Vector3(x - 6f, Ground + 1.2f, i * 20f - 4f),
                        new Vector3(x + 6f, Ground + 10f, i * 20f + 4f), MatId.TechPanelDark, true, 0.8f);
            b.AddLight(new Vector3(x, Ground + 15f, 0f), GameTypes.TeamColor(team) * 1.3f, 40f, 7f);

            Vector3 corePos = new(x, Ground + 1.2f, 0f);
            int core = b.AddPowerNode(corePos, team == Team.Red ? Loc.NodeRedCore : Loc.NodeBlueCore,
                [], isCore: true, team: team);
            if (team == Team.Red) redCore = core; else blueCore = core;
            b.AddOrbSpawn(corePos + new Vector3(-sign * 5f, 1.1f, 0f), team);

            if (necrisSide)
            {
                for (int i = -1; i <= 1; i += 2)
                    b.AddVehicle(VehicleKind.Viper, corePos + new Vector3(-sign * 10f, 1.4f, i * 7f), -90f, team);
                for (int i = -1; i <= 1; i += 2)
                    b.AddVehicle(VehicleKind.Viper, corePos + new Vector3(-sign * 15f, 1.4f, i * 12f), -90f, team);
                b.AddVehicle(VehicleKind.Nemesis, corePos + new Vector3(-sign * 20f, 1.4f, 0f), -90f, team);
                b.AddVehicle(VehicleKind.Nightshade, corePos + new Vector3(-sign * 20f, 1.4f, 10f), -90f, team);
            }
            else
            {
                for (int i = -1; i <= 1; i += 2)
                    b.AddVehicle(VehicleKind.Manta, corePos + new Vector3(-sign * 10f, 1.4f, i * 7f), 90f, team);
                for (int i = -1; i <= 1; i += 2)
                    b.AddVehicle(VehicleKind.Hellbender, corePos + new Vector3(-sign * 16f, 1.6f, i * 12f), 90f, team);
            }

            b.Weapon(corePos + new Vector3(-sign * 4f, 0.9f, -7f), WeaponKind.Avril);
            b.Locker(corePos + new Vector3(-sign * 8f, 0.2f, 0f),
                WeaponKind.ShockRifle, WeaponKind.LinkGun, WeaponKind.Stinger, WeaponKind.RocketLauncher);
            b.Weapon(corePos + new Vector3(-sign * 4f, 0.9f, 7f), WeaponKind.FlakCannon);
            b.Item(corePos + new Vector3(0f, 0.8f, -11f), PickupKind.BodyArmor);
            b.Item(corePos + new Vector3(0f, 0.8f, 11f), PickupKind.HealthPack);
            for (int i = 0; i < 4; i++)
                b.Spawn(corePos + new Vector3(-sign * (3f + i * 3f), 0.2f, -12f + i * 8f), sign < 0 ? 90f : -90f, team);
        }

        int iRedPrime = b.AddPowerNode(redPrime, Loc.NodeRedPrime, []);
        int iBluePrime = b.AddPowerNode(bluePrime, Loc.NodeBluePrime, []);
        Vector3 centre = new(0f, Ground + 1.2f, 0f);
        Vector3 west = new(0f, Ground + 1.2f, -46f);
        Vector3 east = new(0f, Ground + 1.2f, 46f);
        int iCentre = b.AddPowerNode(centre, Loc.NodeCentre, []);
        // Countdown nodes with no core-damage fraction: their payout is the enemy prime node.
        int iWest = b.AddPowerNode(west, Loc.NodeWest, [], role: NodeRole.Countdown,
            countdownSeconds: OnslaughtState.DefaultCountdownSeconds);
        int iEast = b.AddPowerNode(east, Loc.NodeEast, [], role: NodeRole.Countdown,
            countdownSeconds: OnslaughtState.DefaultCountdownSeconds);

        b.LinkPowerNodes(redCore, [iRedPrime]);
        b.LinkPowerNodes(iRedPrime, [redCore, iCentre]);
        b.LinkPowerNodes(iCentre, [iRedPrime, iBluePrime]);
        b.LinkPowerNodes(iBluePrime, [blueCore, iCentre]);
        b.LinkPowerNodes(blueCore, [iBluePrime]);

        foreach (var (pos, team) in new[] { (redPrime, Team.Red), (bluePrime, Team.Blue) })
        {
            NodePad(b, pos, 14f, MatId.Concrete);
            NodeShelter(b, pos, 9f, 8.5f, MatId.TechPanelDark, MatId.ArmorPlate);
            b.Solid(pos + new Vector3(-13f, 0f, -14f), pos + new Vector3(13f, 5f, -11f), MatId.Rock, true, 0.7f);
            b.Solid(pos + new Vector3(-13f, 0f, 11f), pos + new Vector3(13f, 5f, 14f), MatId.Rock, true, 0.7f);
            b.Weapon(pos + new Vector3(0f, 1f, -8f), WeaponKind.Stinger);
            b.Item(pos + new Vector3(0f, 0.9f, 8f), PickupKind.ThighPads);
            b.Spawn(pos + new Vector3(0f, 0.4f, 0f), 0f, team);
            b.AddVehicle(team == Team.Red ? VehicleKind.Scorpion : VehicleKind.Viper,
                pos + new Vector3(0f, 1.2f, 5f), 0f, team);
            b.AddVehicle(team == Team.Red ? VehicleKind.Scorpion : VehicleKind.Viper,
                pos + new Vector3(0f, 1.2f, -5f), 0f, team);
        }

        // Inside the mountain: the centre node and the two countdown nodes flanking it.
        NodePad(b, centre, 16f, MatId.MetalGrate);
        NodeShelter(b, centre, 10f, 10f, MatId.TechPanelDark, MatId.ArmorPlate);
        b.Weapon(centre + new Vector3(0f, 1f, 0f), WeaponKind.Redeemer, 110f);
        b.Item(centre + new Vector3(-8f, 0.9f, 0f), PickupKind.ShieldBelt);
        b.Spawn(centre + new Vector3(8f, 0.4f, 0f), 180f);
        b.AddVehicle(VehicleKind.Scavenger, centre + new Vector3(-11f, 1.6f, -6f), 0f, Team.Blue);
        b.AddVehicle(VehicleKind.Scavenger, centre + new Vector3(-11f, 1.6f, 6f), 0f, Team.Blue);
        // Our own addition, not the original's. The Ion Plasma Tank has exactly one home map in
        // UT2004 — AS-Glacier — so the centre node is where it gets a second outing. It is a
        // vehicle on an existing node, not a new node or a changed link setup, and the README
        // says plainly that this one placement is ours rather than Epic's.
        b.AddVehicle(VehicleKind.IonTank, centre + new Vector3(11f, 2.2f, 0f), 180f);

        foreach (var (pos, team) in new[] { (west, Team.Red), (east, Team.Blue) })
        {
            NodePad(b, pos, 12f, MatId.MetalGrate);
            NodeShelter(b, pos, 7.5f, 8f, MatId.RustMetal, MatId.MetalGrate);
            b.Weapon(pos + new Vector3(0f, 1f, 0f), WeaponKind.FlakCannon);
            b.Item(pos + new Vector3(0f, 0.9f, 6f), PickupKind.HealthPack);
            b.Spawn(pos + new Vector3(0f, 0.4f, -6f), 0f);
            _ = team;
        }
        b.AddVehicle(VehicleKind.Goliath, west + new Vector3(0f, 1.8f, 8f), 0f, Team.Red);
        b.AddVehicle(VehicleKind.Darkwalker, east + new Vector3(0f, 4.6f, 8f), 0f, Team.Blue);

        // Snow drifts outside the mountain so the approach is not a flat plate.
        for (int i = 0; i < 34; i++)
        {
            float px = rng.Range(-HX + 16f, HX - 16f);
            float pz = rng.Range(-HZ + 10f, HZ - 10f);
            if (MathF.Abs(px) < 50f) continue;
            if (i % 4 == 0)
            {
                Outbuilding(b, new Vector3(px, Ground, pz), rng.Range(4f, 6f), rng.Range(4f, 6f),
                    rng.Range(5f, 8f), MatId.TechPanelDark);
                continue;
            }
            float sz = rng.Range(2.2f, 5f);
            b.Solid(new Vector3(px - sz, Ground, pz - sz),
                    new Vector3(px + sz, Ground + rng.Range(2.2f, 4.2f), pz + sz), MatId.Rock, true, 0.6f);
        }

        _ = (iWest, iEast);
        return b.Build(gl);
    }

    // ================================================================ WAR-黑曜海岸

    /// <summary>
    /// Onyx Coast. Axon against Necris on a frozen shore: a small eastern base that starts with a
    /// Leviathan, a fortified western base with two Darkwalkers and a Fury, and a U-shaped link
    /// between them. The bridge control node decides whether the Leviathan can cross at all.
    /// </summary>
    private static Level BuildOnyxCoast(GL gl)
    {
        var b = new LevelBuilder(Loc.MapOnyxCoast, Loc.MapOnyxCoastDesc);
        var env = b.Level.Environment;
        env.SunDirection = Vector3.Normalize(new Vector3(-0.44f, -0.60f, -0.66f));
        env.SunColor = new Vector3(2.6f, 2.7f, 3.2f);
        env.AmbientSky = new Vector3(0.34f, 0.40f, 0.56f);
        env.AmbientGround = new Vector3(0.22f, 0.26f, 0.34f);
        env.EnvIntensity = 0.72f;
        env.SkyTop = new Vector3(0.10f, 0.18f, 0.36f);
        env.SkyHorizon = new Vector3(0.52f, 0.58f, 0.70f);
        env.SkyGround = new Vector3(0.34f, 0.40f, 0.48f);
        env.StarStrength = 0.3f;
        env.CloudStrength = 0.95f;
        env.FogColor = new Vector3(0.56f, 0.62f, 0.74f);
        env.FogDensity = 0.0045f;

        const float HX = 118f, HZ = 82f, Ground = 0f;
        // Coast either side of the channel, so the sea is a real gap rather than a texture buried
        // under the shore. The bridge stays the fast way over; the water is the slow way.
        const float Bank = 30f, Shore = 22f, SeaBed = Ground - 2.2f;
        foreach (int s in new[] { -1, 1 })
            b.Solid(new Vector3(s > 0 ? Bank : -HX, -6f, -HZ), new Vector3(s > 0 ? HX : -Bank, Ground, HZ),
                MatId.Concrete, true, 0.4f);
        b.Room(new Vector3(-HX - 4f, -6f, -HZ - 4f), new Vector3(HX + 4f, 60f, HZ + 4f), 4f,
            MatId.Concrete, MatId.Rock, MatId.Rock, withCeiling: false, withFloor: false);

        // The sea channel that splits the coast, spanned by the bridge the support node raises.
        // Shelving banks rather than a sheer drop: anything that goes in can get back out, which
        // is what keeps the channel a route instead of a hole that swallows a team's vehicles.
        b.Solid(new Vector3(-Shore, -6f, -HZ), new Vector3(Shore, SeaBed, HZ), MatId.Rock, true, 0.5f);
        b.Water(new Vector3(-Shore, SeaBed, -HZ), new Vector3(Shore, Ground - 0.5f, HZ));
        for (int s = -1; s <= 1; s += 2)
            b.Ramp(new Vector3(s > 0 ? Shore : -Bank, SeaBed, -HZ),
                new Vector3(s > 0 ? Bank : -Shore, Ground, HZ), 0, MatId.Rock, true, 0.45f);

        int redCore = -1, blueCore = -1;

        // Red: the small Axon base to the east, with the Leviathan.
        {
            float x = HX - 22f;
            b.Solid(new Vector3(x - 18f, Ground, -20f), new Vector3(x + 18f, Ground + 1.2f, 20f),
                MatId.Concrete, true, 0.6f);
            b.Solid(new Vector3(x - 8f, Ground + 1.2f, -20f), new Vector3(x + 18f, Ground + 8f, -14f),
                MatId.TeamRed, true, 0.7f);
            b.Solid(new Vector3(x - 8f, Ground + 7f, -14f), new Vector3(x + 18f, Ground + 8f, 6f),
                MatId.MetalGrate, true, 0.9f);
            b.Stairs(new Vector3(x - 8f, Ground + 1.2f, 6f), new Vector3(x - 8f, Ground + 7f, 16f),
                6f, 9, MatId.Concrete, alongX: false);
            Vector3 corePos = new(x, Ground + 1.2f, 12f);
            redCore = b.AddPowerNode(corePos, Loc.NodeRedCore, [], isCore: true, team: Team.Red);
            b.AddOrbSpawn(corePos + new Vector3(-5f, 1.1f, 0f), Team.Red);
            b.AddVehicle(VehicleKind.Leviathan, new Vector3(x - 12f, Ground + 3.2f, -6f), -90f, Team.Red);
            b.AddVehicle(VehicleKind.Manta, new Vector3(x + 2f, Ground + 8.4f, -8f), -90f, Team.Red);
            b.AddVehicle(VehicleKind.Manta, new Vector3(x + 8f, Ground + 8.4f, -8f), -90f, Team.Red);
            b.Weapon(new Vector3(x, Ground + 8.9f, 0f), WeaponKind.SniperRifle);
            b.Weapon(new Vector3(x - 4f, Ground + 2.1f, 16f), WeaponKind.Avril);
            b.Locker(new Vector3(x - 8f, Ground + 1.4f, 16f),
                WeaponKind.ShockRifle, WeaponKind.LinkGun, WeaponKind.Stinger, WeaponKind.RocketLauncher);
            b.Item(new Vector3(x + 6f, Ground + 2.0f, 16f), PickupKind.BodyArmor);
            for (int i = 0; i < 4; i++)
                b.Spawn(new Vector3(x - 4f - i * 3f, Ground + 1.4f, 14f - i * 5f), -90f, Team.Red);
            b.AddLight(new Vector3(x, Ground + 14f, 0f), GameTypes.TeamColor(Team.Red) * 1.3f, 38f, 6.5f);
        }

        // Blue: the fortified Necris base to the west.
        {
            float x = -(HX - 26f);
            b.Solid(new Vector3(x - 22f, Ground, -26f), new Vector3(x + 22f, Ground + 1.2f, 26f),
                MatId.Concrete, true, 0.6f);
            b.Solid(new Vector3(x - 22f, Ground, -26f), new Vector3(x - 18f, Ground + 12f, 26f),
                MatId.TeamBlue, true, 0.7f);
            for (int i = -1; i <= 1; i += 2)
                b.Solid(new Vector3(x - 18f, Ground, i * 22f), new Vector3(x + 22f, Ground + 12f, i * 26f),
                    MatId.TeamBlue, true, 0.7f);
            Vector3 corePos = new(x, Ground + 1.2f, 0f);
            blueCore = b.AddPowerNode(corePos, Loc.NodeBlueCore, [], isCore: true, team: Team.Blue);
            b.AddOrbSpawn(corePos + new Vector3(6f, 1.1f, 0f), Team.Blue);
            b.AddVehicle(VehicleKind.Darkwalker, new Vector3(x + 8f, Ground + 5.8f, -12f), 90f, Team.Blue);
            b.AddVehicle(VehicleKind.Darkwalker, new Vector3(x + 8f, Ground + 5.8f, 12f), 90f, Team.Blue);
            b.AddVehicle(VehicleKind.Fury, new Vector3(x + 2f, Ground + 14f, 0f), 90f, Team.Blue);
            b.AddVehicle(VehicleKind.Viper, new Vector3(x + 16f, Ground + 2.6f, -6f), 90f, Team.Blue);
            b.AddVehicle(VehicleKind.Viper, new Vector3(x + 16f, Ground + 2.6f, 6f), 90f, Team.Blue);
            b.Weapon(new Vector3(x + 4f, Ground + 2.1f, -8f), WeaponKind.FlakCannon);
            b.Weapon(new Vector3(x + 4f, Ground + 2.1f, 8f), WeaponKind.Stinger);
            b.Item(new Vector3(x - 8f, Ground + 2.0f, 0f), PickupKind.ShieldBelt);
            for (int i = 0; i < 4; i++)
                b.Spawn(new Vector3(x + 4f + i * 3f, Ground + 1.4f, -12f + i * 8f), 90f, Team.Blue);
            b.AddLight(new Vector3(x, Ground + 16f, 0f), GameTypes.TeamColor(Team.Blue) * 1.3f, 42f, 7f);
        }

        // --- the U: two prime nodes either side of the channel ---
        Vector3 northPrime = new(-8f, Ground + 1.2f, -52f);
        Vector3 southPrime = new(-8f, Ground + 1.2f, 52f);
        int iNorth = b.AddPowerNode(northPrime, Loc.NodeBluePrime, []);
        int iSouth = b.AddPowerNode(southPrime, Loc.NodeRedPrime, []);
        b.LinkPowerNodes(blueCore, [iNorth]);
        b.LinkPowerNodes(iNorth, [blueCore, iSouth]);
        b.LinkPowerNodes(iSouth, [redCore, iNorth]);
        b.LinkPowerNodes(redCore, [iSouth]);

        foreach (var (pos, team) in new[] { (northPrime, Team.Blue), (southPrime, Team.Red) })
        {
            NodePad(b, pos, 14f, MatId.Concrete);
            NodeShelter(b, pos, 9f, 9f, MatId.TechPanelDark, MatId.ArmorPlate);
            b.Solid(pos + new Vector3(-14f, 0f, -3f), pos + new Vector3(-10f, 8f, 3f), MatId.RustMetal, true, 0.8f);
            b.Solid(pos + new Vector3(-14f, 8f, -8f), pos + new Vector3(4f, 9f, 8f), MatId.MetalGrate, true, 0.9f);
            b.Stairs(pos + new Vector3(4f, 0f, -4f), pos + new Vector3(4f, 8f, 4f), 5f, 11,
                MatId.Concrete, alongX: false);
            b.Weapon(pos + new Vector3(-6f, 9.9f, 0f), WeaponKind.SniperRifle);
            b.Weapon(pos + new Vector3(6f, 1f, 0f), WeaponKind.Avril);
            b.Locker(pos + new Vector3(0f, 0.4f, -9f),
                WeaponKind.ShockRifle, WeaponKind.LinkGun, WeaponKind.Stinger, WeaponKind.RocketLauncher);
            b.Item(pos + new Vector3(0f, 0.9f, 9f), PickupKind.HealthPack);
            b.Spawn(pos + new Vector3(8f, 0.4f, 0f), 0f, team);
        }

        // --- the bridge and its control node ---
        b.Solid(new Vector3(-22f, Ground - 0.4f, -14f), new Vector3(22f, Ground, 14f), MatId.MetalGrate, true, 0.8f);
        for (int s = -1; s <= 1; s += 2)
            b.Solid(new Vector3(s * 20f, -8f, -14f), new Vector3(s * 22f, Ground, 14f), MatId.RustMetal, true, 0.9f);
        Vector3 bridgeNode = new(0f, Ground + 1.2f, -24f);
        b.Solid(bridgeNode + new Vector3(-10f, -0.6f, -8f), bridgeNode + new Vector3(10f, 0f, 8f),
            MatId.Concrete, true, 0.6f);
        b.Solid(bridgeNode + new Vector3(-10f, 0f, -8f), bridgeNode + new Vector3(-7f, 12f, 8f),
            MatId.TechPanelDark, true, 0.8f);
        b.AddPowerNode(bridgeNode, Loc.NodeBridgeControl, [], role: NodeRole.Support);
        b.Weapon(bridgeNode + new Vector3(4f, 1f, 0f), WeaponKind.FlakCannon);
        b.Item(bridgeNode + new Vector3(0f, 0.9f, 5f), PickupKind.BodyArmor);
        b.Spawn(bridgeNode + new Vector3(0f, 0.4f, -5f), 0f);
        b.Weapon(new Vector3(0f, Ground + 0.9f, 24f), WeaponKind.Redeemer, 110f);

        // Ice ridges and shore installations, so the coast is not a flat white sheet.
        var coastRng = new Rng(0x0C0A);
        for (int i = 0; i < 30; i++)
        {
            float px = coastRng.Range(-HX + 16f, HX - 16f);
            float pz = coastRng.Range(-HZ + 12f, HZ - 12f);
            if (MathF.Abs(px) < 30f) continue;                    // keep the channel clear
            if (MathF.Abs(px) > HX - 46f && MathF.Abs(pz) < 30f) continue;   // and the bases
            if (i % 4 == 0)
            {
                Outbuilding(b, new Vector3(px, Ground, pz), coastRng.Range(4f, 7f),
                    coastRng.Range(4f, 7f), coastRng.Range(6f, 10f), MatId.TechPanelDark);
                continue;
            }
            float sz = coastRng.Range(2.6f, 6f);
            b.Solid(new Vector3(px - sz, Ground, pz - sz),
                new Vector3(px + sz, Ground + coastRng.Range(3f, 9f), pz + sz), MatId.Rock, true, 0.6f);
        }

        return b.Build(gl);
    }

    // ================================================================ WAR-群島通訊站

    /// <summary>
    /// Islander. The deliberately lopsided one: the attacking side spawns west with the ground
    /// armour and a short run to the single prime node, the defenders sit east in a walled base
    /// with turrets, and the air node in between is the only way to the Redeemer island.
    /// </summary>
    private static Level BuildIslander(GL gl)
    {
        var b = new LevelBuilder(Loc.MapIslander, Loc.MapIslanderDesc);
        var env = b.Level.Environment;
        env.SunDirection = Vector3.Normalize(new Vector3(-0.36f, -0.70f, -0.60f));
        env.SunColor = new Vector3(3.2f, 3.2f, 3.4f);
        env.AmbientSky = new Vector3(0.36f, 0.44f, 0.58f);
        env.AmbientGround = new Vector3(0.20f, 0.26f, 0.32f);
        env.EnvIntensity = 0.78f;
        env.SkyTop = new Vector3(0.14f, 0.30f, 0.56f);
        env.SkyHorizon = new Vector3(0.62f, 0.70f, 0.80f);
        env.SkyGround = new Vector3(0.24f, 0.34f, 0.44f);
        env.StarStrength = 0f;
        env.CloudStrength = 0.8f;
        env.FogColor = new Vector3(0.60f, 0.70f, 0.82f);
        env.FogDensity = 0.0042f;

        const float HX = 116f, HZ = 84f, SeaY = -3.4f;
        b.Solid(new Vector3(-HX, -14f, -HZ), new Vector3(HX, SeaY - 0.01f, HZ), MatId.Rock, true, 0.5f);
        b.Water(new Vector3(-HX, -14f, -HZ), new Vector3(HX, SeaY, HZ));
        b.Room(new Vector3(-HX - 4f, -14f, -HZ - 4f), new Vector3(HX + 4f, 62f, HZ + 4f), 4f,
            MatId.Rock, MatId.Rock, MatId.Rock, withCeiling: false, withFloor: false);

        const float Ground = 0f;
        // Main island: one long landmass carrying both bases and the prime node.
        b.Solid(new Vector3(-HX + 10f, SeaY, -34f), new Vector3(HX - 10f, Ground, 34f), MatId.Concrete, true, 0.5f);

        int redCore, blueCore;

        // Red: the light western base, with the ground armour.
        {
            float x = -(HX - 30f);
            b.Solid(new Vector3(x - 14f, Ground, -18f), new Vector3(x + 14f, Ground + 1.2f, 18f),
                MatId.Concrete, true, 0.6f);
            b.Solid(new Vector3(x - 14f, Ground, -18f), new Vector3(x - 11f, Ground + 6f, 18f),
                MatId.TeamRed, true, 0.7f);
            Vector3 corePos = new(x, Ground + 1.2f, 0f);
            redCore = b.AddPowerNode(corePos, Loc.NodeRedCore, [], isCore: true, team: Team.Red);
            b.AddOrbSpawn(corePos + new Vector3(6f, 1.1f, 0f), Team.Red);
            b.AddVehicle(VehicleKind.Goliath, corePos + new Vector3(10f, 1.8f, -9f), 90f, Team.Red);
            b.AddVehicle(VehicleKind.Goliath, corePos + new Vector3(10f, 1.8f, 9f), 90f, Team.Red);
            b.AddVehicle(VehicleKind.Paladin, corePos + new Vector3(16f, 1.6f, 0f), 90f, Team.Red);
            b.AddVehicle(VehicleKind.Hellbender, corePos + new Vector3(4f, 1.6f, 13f), 90f, Team.Red);
            b.Weapon(corePos + new Vector3(2f, 0.9f, -8f), WeaponKind.Avril);
            b.Locker(corePos + new Vector3(2f, 0.2f, 4f),
                WeaponKind.ShockRifle, WeaponKind.LinkGun, WeaponKind.Stinger, WeaponKind.FlakCannon);
            b.Item(corePos + new Vector3(-6f, 0.8f, 0f), PickupKind.BodyArmor);
            for (int i = 0; i < 4; i++)
                b.Spawn(corePos + new Vector3(3f + i * 3f, 0.2f, -10f + i * 6f), 90f, Team.Red);
            b.AddLight(new Vector3(x, Ground + 13f, 0f), GameTypes.TeamColor(Team.Red) * 1.25f, 34f, 6f);
        }

        // Blue: the fortified eastern base, ringed by a wall with a single vehicle gap.
        {
            float x = HX - 30f;
            b.Solid(new Vector3(x - 18f, Ground, -22f), new Vector3(x + 18f, Ground + 1.2f, 22f),
                MatId.Concrete, true, 0.6f);
            b.Solid(new Vector3(x + 14f, Ground, -22f), new Vector3(x + 18f, Ground + 12f, 22f),
                MatId.TeamBlue, true, 0.7f);
            for (int i = -1; i <= 1; i += 2)
                b.Solid(new Vector3(x - 18f, Ground, i * 18f), new Vector3(x + 18f, Ground + 12f, i * 22f),
                    MatId.TeamBlue, true, 0.7f);
            // Barricades across the western approach: infantry through, vehicles around.
            for (int i = -2; i <= 2; i++)
                b.Solid(new Vector3(x - 20f, Ground + 1.2f, i * 7f - 2.2f),
                        new Vector3(x - 17f, Ground + 4.6f, i * 7f + 2.2f), MatId.RustMetal, true, 0.8f);
            b.Solid(new Vector3(x - 18f, Ground + 11f, -18f), new Vector3(x + 14f, Ground + 12f, 18f),
                MatId.MetalGrate, true, 0.9f);
            b.Stairs(new Vector3(x - 12f, Ground + 1.2f, 0f), new Vector3(x + 2f, Ground + 11f, 0f),
                5f, 14, MatId.Concrete);
            Vector3 corePos = new(x, Ground + 1.2f, 0f);
            blueCore = b.AddPowerNode(corePos, Loc.NodeBlueCore, [], isCore: true, team: Team.Blue);
            b.AddOrbSpawn(corePos + new Vector3(0f, 11f, 8f), Team.Blue);
            b.Weapon(corePos + new Vector3(0f, 12.9f, -8f), WeaponKind.SniperRifle);
            b.Weapon(corePos + new Vector3(-8f, 0.9f, 8f), WeaponKind.FlakCannon);
            b.Item(corePos + new Vector3(-8f, 0.8f, -8f), PickupKind.ShieldBelt);
            for (int i = 0; i < 4; i++)
                b.Spawn(corePos + new Vector3(-2f - i * 3f, 0.2f, -10f + i * 6f), -90f, Team.Blue);
            b.AddLight(new Vector3(x, Ground + 16f, 0f), GameTypes.TeamColor(Team.Blue) * 1.3f, 40f, 7f);
        }

        // --- the single prime node both cores share ---
        Vector3 prime = new(-24f, Ground + 1.2f, 0f);
        int iPrime = b.AddPowerNode(prime, Loc.NodePrime, []);
        b.LinkPowerNodes(redCore, [iPrime]);
        b.LinkPowerNodes(iPrime, [redCore, blueCore]);
        b.LinkPowerNodes(blueCore, [iPrime]);
        b.Solid(prime + new Vector3(-14f, -1.2f, -14f), prime + new Vector3(14f, 0f, 14f), MatId.Concrete, true, 0.6f);
        b.Solid(prime + new Vector3(-14f, 0f, -14f), prime + new Vector3(14f, 9f, -11f), MatId.TechPanelDark, true, 0.8f);
        b.Solid(prime + new Vector3(-14f, 0f, 11f), prime + new Vector3(14f, 9f, 14f), MatId.TechPanelDark, true, 0.8f);
        b.Solid(prime + new Vector3(-14f, 9f, -14f), prime + new Vector3(14f, 10f, 14f), MatId.MetalGrate, true, 0.9f);
        b.Weapon(prime + new Vector3(0f, 10.9f, 0f), WeaponKind.Avril);
        b.Locker(prime + new Vector3(0f, 0.4f, -8f),
            WeaponKind.ShockRifle, WeaponKind.LinkGun, WeaponKind.Stinger, WeaponKind.RocketLauncher);
        b.Item(prime + new Vector3(-6f, 10.8f, 0f), PickupKind.BodyArmor);
        b.Weapon(prime + new Vector3(6f, 1f, 0f), WeaponKind.Stinger);
        b.Spawn(prime + new Vector3(0f, 0.4f, 8f), 0f);
        for (int s = -1; s <= 1; s += 2)
            b.AddJumpPad(prime + new Vector3(s * 9f, 0.2f, 0f), prime + new Vector3(s * 7f, 12f, 0f),
                new Vector3(0.5f, 0.85f, 1f));

        // --- the air node on high ground, and the Redeemer island it unlocks ---
        Vector3 airNode = new(20f, Ground + 9.2f, -26f);
        b.Solid(airNode + new Vector3(-16f, -9.2f, -14f), airNode + new Vector3(16f, -1f, 14f),
            MatId.Rock, true, 0.6f);
        b.Solid(airNode + new Vector3(-16f, -1f, -14f), airNode + new Vector3(16f, 0f, 14f),
            MatId.Concrete, true, 0.6f);
        b.Ramp(airNode + new Vector3(-30f, -9.2f, -8f), airNode + new Vector3(-16f, 0f, 8f), 0,
            MatId.Rock, true, 0.5f);
        int iAir = b.AddPowerNode(airNode, Loc.NodeAir, [], role: NodeRole.Support);
        b.AddVehicle(VehicleKind.Raptor, airNode + new Vector3(-8f, 12f, 0f), 180f);
        b.AddVehicle(VehicleKind.Raptor, airNode + new Vector3(8f, 12f, 0f), 180f);
        b.Weapon(airNode + new Vector3(0f, 1f, 8f), WeaponKind.SniperRifle);
        b.Item(airNode + new Vector3(0f, 0.9f, -8f), PickupKind.HealthPack);
        b.Spawn(airNode + new Vector3(-6f, 0.4f, 0f), 180f);
        b.AddOrbSpawn(airNode + new Vector3(6f, 1.1f, 0f), Team.Red, iAir);
        b.AddOrbSpawn(airNode + new Vector3(6f, 1.1f, 4f), Team.Blue, iAir);

        // The archipelago: reachable by air only, which is what the air node is worth.
        Vector3 island = new(6f, Ground + 16f, 56f);
        b.Solid(island + new Vector3(-9f, -20f, -9f), island + new Vector3(9f, 0f, 9f), MatId.Rock, true, 0.6f);
        b.Weapon(island + new Vector3(0f, 0.9f, 0f), WeaponKind.Redeemer, 120f);
        b.Item(island + new Vector3(0f, 0.8f, 5f), PickupKind.SuperHealth, 90f);

        // The rest of the archipelago: rocks out in the water and comms huts along the spine, so
        // the island reads as an installation rather than a runway.
        var islandRng = new Rng(0x1571);
        for (int i = 0; i < 26; i++)
        {
            float px = islandRng.Range(-HX + 14f, HX - 14f);
            float pz = islandRng.Range(-HZ + 12f, HZ - 12f);
            bool onLand = MathF.Abs(pz) < 32f && MathF.Abs(px) < HX - 12f;
            if (onLand && (MathF.Abs(px + (HX - 30f)) < 24f || MathF.Abs(px - (HX - 30f)) < 26f
                || MathF.Abs(px + 24f) < 18f)) continue;          // bases and the prime node
            if (onLand && i % 3 == 0)
            {
                Outbuilding(b, new Vector3(px, Ground, pz), islandRng.Range(3.5f, 6f),
                    islandRng.Range(3.5f, 6f), islandRng.Range(5f, 9f), MatId.TechPanelDark);
                continue;
            }
            float sz = islandRng.Range(3f, 7f);
            float baseY = onLand ? Ground : SeaY - 2f;
            b.Solid(new Vector3(px - sz, baseY, pz - sz),
                new Vector3(px + sz, baseY + islandRng.Range(4f, 11f), pz + sz), MatId.Rock, true, 0.6f);
        }

        return b.Build(gl);
    }
}
