using System.Numerics;
using Silk.NET.OpenGL;
using Unreal99.Core;
using Unreal99.Game;
using Unreal99.Rendering;
using Unreal99.UI;

namespace Unreal99.World;

/// <summary>
/// The two Onslaught arenas that carry UT2004's painters. Between them they are the only stock
/// maps in the game that hand out an Ion Painter or a Target Painter, so without them those two
/// weapons exist in the code and nowhere a player can reach.
/// </summary>
public static partial class Maps
{
    // ================================================================ ONS-交叉火網

    /// <summary>
    /// Crossfire. Excavated ruins under Jerusalem, and the only stock map that hands out an Ion
    /// Painter. Nine node sites are built, but only the five the Default link setup uses are live
    /// power nodes — the other four hold their real weapons, lockers and vehicles as landmarks.
    /// Two separate shelves carry the superweapons: the south one holds the Lightning Gun with the
    /// Redeemer on the ground beneath it, the central one the Target Painter and, a step higher,
    /// the Ion Painter looking down on the centre node.
    /// </summary>
    private static Level BuildCrossfire(GL gl)
    {
        var b = new LevelBuilder(Loc.MapCrossfire, Loc.MapCrossfireDesc);
        var env = b.Level.Environment;
        env.SunDirection = Vector3.Normalize(new Vector3(-0.48f, -0.70f, -0.53f));
        env.SunColor = new Vector3(4.3f, 3.7f, 2.9f);
        env.AmbientSky = new Vector3(0.40f, 0.36f, 0.30f);
        env.AmbientGround = new Vector3(0.30f, 0.24f, 0.17f);
        env.EnvIntensity = 0.82f;
        env.SkyTop = new Vector3(0.28f, 0.42f, 0.68f);
        env.SkyHorizon = new Vector3(0.84f, 0.74f, 0.55f);
        env.SkyGround = new Vector3(0.52f, 0.42f, 0.28f);
        env.StarStrength = 0f;
        env.CloudStrength = 0.3f;
        env.FogColor = new Vector3(0.80f, 0.72f, 0.56f);
        env.FogDensity = 0.0040f;

        const float HX = 130f, HZ = 108f, Ground = 0f;
        b.Solid(new Vector3(-HX, -6f, -HZ), new Vector3(HX, Ground, HZ), MatId.Rock, true, 0.4f);
        b.Room(new Vector3(-HX - 4f, -6f, -HZ - 4f), new Vector3(HX + 4f, 72f, HZ + 4f), 4f,
            MatId.Rock, MatId.Rock, MatId.Rock, withCeiling: false, withFloor: false);

        var rng = new Rng(0xC205);
        int redCore = -1, blueCore = -1;

        // --- the two bases, diagonally opposite as in the original ---
        foreach (var (team, bx, bz) in new[] { (Team.Red, HX - 24f, -(HZ - 26f)), (Team.Blue, -(HX - 24f), HZ - 26f) })
        {
            float sign = team == Team.Red ? 1f : -1f;
            MatId teamMat = team == Team.Red ? MatId.TeamRed : MatId.TeamBlue;
            b.Solid(new Vector3(bx - 20f, Ground, bz - 20f), new Vector3(bx + 20f, Ground + 1.2f, bz + 20f),
                MatId.Concrete, true, 0.6f);
            b.Solid(new Vector3(bx + sign * 20f, Ground, bz - 20f),
                new Vector3(bx + sign * 23f, Ground + 9f, bz + 20f), teamMat, true, 0.7f);
            b.AddLight(new Vector3(bx, Ground + 15f, bz), GameTypes.TeamColor(team) * 1.3f, 40f, 7f);

            Vector3 corePos = new(bx, Ground + 1.2f, bz);
            int core = b.AddPowerNode(corePos, team == Team.Red ? Loc.NodeRedCore : Loc.NodeBlueCore,
                [], isCore: true, team: team);
            if (team == Team.Red) redCore = core; else blueCore = core;

            // Two lockers per core, exactly as the original splits them.
            b.Locker(corePos + new Vector3(-sign * 7f, 0.2f, -6f),
                WeaponKind.ShockRifle, WeaponKind.LinkGun, WeaponKind.FlakCannon, WeaponKind.Avril);
            b.Locker(corePos + new Vector3(-sign * 7f, 0.2f, 6f),
                WeaponKind.ShockRifle, WeaponKind.LinkGun, WeaponKind.FlakCannon, WeaponKind.Avril);
            b.Ammo(corePos + new Vector3(-sign * 11f, 0.9f, 0f), AmmoKind.AvrilMissiles);
            b.Item(corePos + new Vector3(0f, 0.8f, -9f), PickupKind.BodyArmor);
            b.Item(corePos + new Vector3(0f, 0.8f, 9f), PickupKind.HealthPack);
            b.AddVehicle(VehicleKind.Manta, corePos + new Vector3(-sign * 12f, 1.4f, -12f), 0f, team);
            b.AddVehicle(VehicleKind.Hellbender, corePos + new Vector3(-sign * 12f, 1.6f, 12f), 0f, team);
            b.AddVehicle(VehicleKind.Raptor, corePos + new Vector3(-sign * 17f, 10f, 0f), 0f, team);
            for (int i = 0; i < 4; i++)
                b.Spawn(corePos + new Vector3(-sign * (4f + i * 3f), 0.2f, -10f + i * 6f), 0f, team);
        }

        // --- the nine node sites ---
        // Crossfire ships three link setups. This is the Default one, which is what a player
        // gets on launching the map:
        //   Red Core → Red North (5) → Centre (8)     → Blue South (4) → Blue Core
        //   Red Core → Red North (5) → Mid North (7)  → Mid South (6)  → Blue South (4) → Blue Core
        // Only five of the nine nodes are live in that setup. The remaining four — Red East,
        // Red Northeast, Blue West, Blue Southwest — are built as landmarks holding their real
        // weapons, lockers and vehicles, exactly where the Split Paths setup would light them up.
        Vector3 redNorthPrime = new(94f, Ground + 1.2f, -46f);
        Vector3 blueSouthPrime = new(-94f, Ground + 1.2f, 46f);
        Vector3 middleNorth = new(34f, Ground + 1.2f, 44f);
        Vector3 middleSouth = new(-34f, Ground + 1.2f, -44f);
        Vector3 centre = new(0f, Ground + 1.2f, 0f);
        // Inactive in the Default setup, but every one of them is a real place on the map.
        Vector3 redNePrime = new(112f, Ground + 1.2f, -34f);
        Vector3 redEastPrime = new(74f, Ground + 1.2f, -92f);
        Vector3 blueWestPrime = new(-74f, Ground + 1.2f, 92f);
        Vector3 blueSwPrime = new(-112f, Ground + 1.2f, 34f);

        int iRedN = b.AddPowerNode(redNorthPrime, Loc.NodeNorthPrime, []);
        int iBlueS = b.AddPowerNode(blueSouthPrime, Loc.NodeSouthPrime, []);
        int iMidN = b.AddPowerNode(middleNorth, Loc.NodeMiddleNorth, []);
        int iMidS = b.AddPowerNode(middleSouth, Loc.NodeMiddleSouth, []);
        int iCentre = b.AddPowerNode(centre, Loc.NodeMiddle, []);

        b.LinkPowerNodes(redCore, [iRedN]);
        b.LinkPowerNodes(iRedN, [redCore, iCentre, iMidN]);
        b.LinkPowerNodes(iMidN, [iRedN, iMidS]);
        b.LinkPowerNodes(iMidS, [iMidN, iBlueS]);
        b.LinkPowerNodes(iCentre, [iRedN, iBlueS]);
        b.LinkPowerNodes(iBlueS, [blueCore, iCentre, iMidS]);
        b.LinkPowerNodes(blueCore, [iBlueS]);

        // The two live prime nodes. Each carries a locker, a Manta, a Scorpion and a Hellbender,
        // plus the Mine Layer ammo the original stocks here.
        foreach (var pos in new[] { redNorthPrime, blueSouthPrime })
        {
            NodePad(b, pos, 13f, MatId.Concrete);
            NodeShelter(b, pos, 8f, 8f, MatId.RustMetal, MatId.MetalGrate);
            b.Locker(pos + new Vector3(-5f, 0.4f, 0f),
                WeaponKind.ShockRifle, WeaponKind.LinkGun, WeaponKind.FlakCannon,
                WeaponKind.RocketLauncher);
            b.Ammo(pos + new Vector3(3f, 0.9f, 5f), AmmoKind.Mines);
            b.Ammo(pos + new Vector3(5f, 0.9f, 5f), AmmoKind.Mines);
            b.Item(pos + new Vector3(0f, 0.9f, -6f), PickupKind.HealthPack);
            b.Item(pos + new Vector3(3f, 0.9f, -6f), PickupKind.HealthPack);
            b.Spawn(pos + new Vector3(0f, 0.4f, 9f), 0f);
            b.AddVehicle(VehicleKind.Manta, pos + new Vector3(-9f, 1.4f, 6f), 0f);
            b.AddVehicle(VehicleKind.Scorpion, pos + new Vector3(9f, 0.7f, 6f), 0f);
            b.AddVehicle(VehicleKind.Hellbender, pos + new Vector3(13f, 1.6f, -6f), 0f);
        }

        // Middle nodes: the Goliath pads, each with a Raptor and a Manta as well.
        foreach (var pos in new[] { middleNorth, middleSouth })
        {
            NodePad(b, pos, 12f, MatId.Concrete);
            NodeShelter(b, pos, 7.5f, 8f, MatId.RustMetal, MatId.ArmorPlate);
            b.Locker(pos + new Vector3(0f, 0.4f, -6f),
                WeaponKind.ShockRifle, WeaponKind.LinkGun, WeaponKind.Minigun, WeaponKind.FlakCannon);
            b.Ammo(pos + new Vector3(-3f, 0.9f, -6f), AmmoKind.RifleGrenades);
            b.Item(pos + new Vector3(0f, 0.9f, 6f), PickupKind.HealthPack);
            b.Spawn(pos + new Vector3(5f, 0.4f, 0f), 0f);
            b.AddVehicle(VehicleKind.Goliath, pos + new Vector3(-8f, 1.8f, 0f), 0f);
            b.AddVehicle(VehicleKind.Manta, pos + new Vector3(8f, 1.4f, 6f), 0f);
            b.AddVehicle(VehicleKind.Raptor, pos + new Vector3(0f, 10f, -10f), 0f);
        }
        b.Item(middleNorth + new Vector3(-4f, 0.9f, 6f), PickupKind.ShieldBelt);
        b.Ammo(middleSouth + new Vector3(3f, 0.9f, -6f), AmmoKind.Grenades);
        b.Ammo(middleSouth + new Vector3(5f, 0.9f, -6f), AmmoKind.Grenades);

        // --- the centre node: the Double Damage, and the map's only central locker ---
        NodePad(b, centre, 16f, MatId.Concrete);
        NodeShelter(b, centre, 10f, 9f, MatId.TechPanelDark, MatId.ArmorPlate);
        b.Locker(centre + new Vector3(0f, 0.4f, -8f), WeaponKind.ShockRifle, WeaponKind.LinkGun,
            WeaponKind.FlakCannon, WeaponKind.RocketLauncher, WeaponKind.LightningGun);
        b.Item(centre + new Vector3(0f, 0.9f, 0f), PickupKind.DamageAmp);
        b.Item(centre + new Vector3(-5f, 0.9f, 6f), PickupKind.HealthPack);
        b.Item(centre + new Vector3(5f, 0.9f, 6f), PickupKind.HealthPack);
        b.Ammo(centre + new Vector3(-3f, 0.9f, -8f), AmmoKind.Grenades);
        b.Ammo(centre + new Vector3(-5f, 0.9f, -8f), AmmoKind.Grenades);
        b.Spawn(centre + new Vector3(0f, 0.4f, 8f), 0f);
        b.AddVehicle(VehicleKind.Manta, centre + new Vector3(-12f, 1.4f, 0f), 0f);
        b.AddVehicle(VehicleKind.Scorpion, centre + new Vector3(12f, 0.7f, 0f), 0f);
        b.AddVehicle(VehicleKind.Raptor, centre + new Vector3(0f, 11f, -14f), 0f);

        // --- the four Split-Paths node sites, built as landmarks ---
        // Red Northeast and Blue Southwest carry the Mine Layer, the Big Keg and a Shield Pack.
        foreach (var pos in new[] { redNePrime, blueSwPrime })
        {
            NodePad(b, pos, 12f, MatId.Concrete);
            NodeShelter(b, pos, 7f, 7.5f, MatId.RustMetal, MatId.MetalGrate);
            b.Locker(pos + new Vector3(-5f, 0.4f, 0f), WeaponKind.MineLayer, WeaponKind.LinkGun,
                WeaponKind.Minigun, WeaponKind.Avril);
            b.Item(pos + new Vector3(0f, 0.9f, 5f), PickupKind.SuperHealth);
            b.Item(pos + new Vector3(0f, 0.9f, -5f), PickupKind.BodyArmor);
            for (int i = 0; i < 2; i++)
                b.Ammo(pos + new Vector3(4f + i * 2f, 0.9f, -5f), AmmoKind.Grenades);
            b.AddVehicle(VehicleKind.Scorpion, pos + new Vector3(9f, 0.7f, 6f), 0f);
            b.AddVehicle(VehicleKind.Hellbender, pos + new Vector3(-11f, 1.6f, 6f), 0f);
        }
        // The map's single Mine Layer sits at the Red Northeast site with four boxes of mines.
        b.Weapon(redNePrime + new Vector3(5f, 1f, 0f), WeaponKind.MineLayer);
        for (int i = 0; i < 4; i++)
            b.Ammo(redNePrime + new Vector3(4f + i * 2f, 0.9f, 4f), AmmoKind.Mines);
        for (int i = 0; i < 2; i++)
            b.Ammo(blueSwPrime + new Vector3(4f + i * 2f, 0.9f, 4f), AmmoKind.Mines);

        // Red East and Blue West: the AVRiL stock and the rest of the light armour.
        foreach (var pos in new[] { redEastPrime, blueWestPrime })
        {
            NodePad(b, pos, 12f, MatId.Concrete);
            NodeShelter(b, pos, 7f, 7.5f, MatId.RustMetal, MatId.ArmorPlate);
            b.Locker(pos + new Vector3(-5f, 0.4f, 0f), WeaponKind.ShockRifle, WeaponKind.LinkGun,
                WeaponKind.FlakCannon, WeaponKind.Avril);
            b.Item(pos + new Vector3(0f, 0.9f, 5f), PickupKind.HealthPack);
            b.Item(pos + new Vector3(3f, 0.9f, 5f), PickupKind.HealthPack);
            b.AddVehicle(VehicleKind.Manta, pos + new Vector3(-9f, 1.4f, 6f), 0f);
            b.AddVehicle(VehicleKind.Scorpion, pos + new Vector3(9f, 0.7f, 6f), 0f);
            b.AddVehicle(VehicleKind.Hellbender, pos + new Vector3(13f, 1.6f, -6f), 0f);
        }
        for (int i = 0; i < 2; i++)
            b.Ammo(redEastPrime + new Vector3(4f + i * 2f, 0.9f, -5f), AmmoKind.AvrilMissiles);

        // --- the two plateaus that decide the map ---
        // South shelf: overlooks the Middle South node and the Red East site. Lightning Gun on
        // top, Redeemer on the ground beneath it, exactly as the original stacks them.
        Vector3 southShelf = new(6f, Ground + 15f, -66f);
        b.Solid(southShelf + new Vector3(-15f, -15f, -13f), southShelf + new Vector3(15f, 0f, 13f),
            MatId.Rock, true, 0.6f);
        b.Ramp(southShelf + new Vector3(15f, -15f, -8f), southShelf + new Vector3(45f, 0f, 8f), 0,
            MatId.Rock, true, 0.5f);
        b.Weapon(southShelf + new Vector3(0f, 0.9f, 0f), WeaponKind.LightningGun);
        b.Ammo(southShelf + new Vector3(0f, 0.8f, -3f), AmmoKind.LightningCells);
        b.Ammo(southShelf + new Vector3(2f, 0.8f, -3f), AmmoKind.LightningCells);
        b.Item(southShelf + new Vector3(-6f, 0.8f, 6f), PickupKind.ShieldBelt);
        b.Weapon(southShelf + new Vector3(0f, -14.1f, 19f), WeaponKind.Redeemer, 110f);

        // Centre shelf: near the middle, looking down on the Middle North and Blue West sites.
        // The Ion Painter watches the centre node from the higher step of the same rise.
        Vector3 centreShelf = new(-22f, Ground + 13f, 26f);
        b.Solid(centreShelf + new Vector3(-14f, -13f, -12f), centreShelf + new Vector3(14f, 0f, 12f),
            MatId.Rock, true, 0.6f);
        b.Ramp(centreShelf + new Vector3(-44f, -13f, -8f), centreShelf + new Vector3(-14f, 0f, 8f), 1,
            MatId.Rock, true, 0.5f);
        b.Weapon(centreShelf + new Vector3(-4f, 0.9f, 0f), WeaponKind.TargetPainter, 120f);
        b.Solid(centreShelf + new Vector3(2f, 0f, -8f), centreShelf + new Vector3(13f, 4f, 8f),
            MatId.Rock, true, 0.6f);
        b.Ramp(centreShelf + new Vector3(-2f, 0f, -6f), centreShelf + new Vector3(2f, 4f, 6f), 0,
            MatId.Rock, true, 0.5f);
        b.Weapon(centreShelf + new Vector3(8f, 4.9f, 0f), WeaponKind.IonPainter, 120f);
        b.Item(centreShelf + new Vector3(-8f, 0.8f, 6f), PickupKind.HealthPack);

        // --- canyon walls and scattered rock ---
        Vector3[] keepClear =
        [
            centre, redNorthPrime, blueSouthPrime, middleNorth, middleSouth,
            redNePrime, redEastPrime, blueWestPrime, blueSwPrime,
        ];
        for (int i = 0; i < 46; i++)
        {
            float px = rng.Range(-HX + 14f, HX - 14f);
            float pz = rng.Range(-HZ + 14f, HZ - 14f);
            var here = new Vector3(px, 0f, pz);
            if (Vector3.Distance(here, southShelf with { Y = 0f }) < 32f) continue;
            if (Vector3.Distance(here, centreShelf with { Y = 0f }) < 30f) continue;
            bool nearNode = false;
            foreach (var n in keepClear)
                if (Vector3.Distance(here, n with { Y = 0f }) < 24f) { nearNode = true; break; }
            if (nearNode) continue;
            float sz = rng.Range(2.6f, 6.5f);
            b.Solid(new Vector3(px - sz, Ground, pz - sz),
                new Vector3(px + sz, Ground + rng.Range(3f, 9f), pz + sz), MatId.Rock, true, 0.6f);
        }

        return b.Build(gl);
    }

    // ================================================================ ONS-德里亞冰河

    /// <summary>
    /// Dria. A frozen river on Na Pali, and the map that carries four Lightning Guns and two
    /// Target Painters — one on each side's tallest tower. Vast, barren and built around long
    /// sight lines, which is exactly what those two weapons are for.
    /// </summary>
    private static Level BuildDria(GL gl)
    {
        var b = new LevelBuilder(Loc.MapDria, Loc.MapDriaDesc);
        var env = b.Level.Environment;
        env.SunDirection = Vector3.Normalize(new Vector3(-0.32f, -0.62f, -0.72f));
        env.SunColor = new Vector3(3.2f, 3.3f, 3.8f);
        env.AmbientSky = new Vector3(0.44f, 0.50f, 0.64f);
        env.AmbientGround = new Vector3(0.30f, 0.34f, 0.44f);
        env.EnvIntensity = 0.80f;
        env.SkyTop = new Vector3(0.18f, 0.32f, 0.58f);
        env.SkyHorizon = new Vector3(0.72f, 0.78f, 0.88f);
        env.SkyGround = new Vector3(0.52f, 0.58f, 0.66f);
        env.StarStrength = 0f;
        env.CloudStrength = 0.9f;
        env.FogColor = new Vector3(0.74f, 0.80f, 0.90f);
        env.FogDensity = 0.0040f;

        const float HX = 138f, HZ = 96f, Ground = 0f;
        b.Solid(new Vector3(-HX, -6f, -HZ), new Vector3(HX, Ground, HZ), MatId.Rock, true, 0.4f);
        b.Room(new Vector3(-HX - 4f, -6f, -HZ - 4f), new Vector3(HX + 4f, 78f, HZ + 4f), 4f,
            MatId.Rock, MatId.Rock, MatId.Rock, withCeiling: false, withFloor: false);

        // The frozen river down the middle, with a bridge at each end of it.
        b.Solid(new Vector3(-HX, -2.4f, -20f), new Vector3(HX, Ground - 0.01f, 20f), MatId.Concrete, true, 0.5f);
        b.Water(new Vector3(-HX, -2.4f, -20f), new Vector3(HX, -1.4f, 20f));
        for (int s = -1; s <= 1; s += 2)
            b.Ramp(new Vector3(-HX, -2.4f, s * 20f), new Vector3(HX, Ground, s * 28f), s < 0 ? 3 : 2,
                MatId.Rock, true, 0.4f);
        foreach (float bx in new[] { -58f, 58f })
            b.Solid(new Vector3(bx - 9f, Ground - 0.4f, -22f), new Vector3(bx + 9f, Ground, 22f),
                MatId.MetalGrate, true, 0.8f);

        var rng = new Rng(0xD817);
        int redCore = -1, blueCore = -1;

        for (int side = 0; side < 2; side++)
        {
            float sign = side == 0 ? -1f : 1f;
            Team team = side == 0 ? Team.Red : Team.Blue;
            MatId teamMat = team == Team.Red ? MatId.TeamRed : MatId.TeamBlue;
            float x = sign * (HX - 24f);

            b.Solid(new Vector3(x - 20f, Ground, -24f), new Vector3(x + 20f, Ground + 1.2f, 24f),
                MatId.Concrete, true, 0.6f);
            b.Solid(new Vector3(x + sign * 20f, Ground, -24f), new Vector3(x + sign * 23f, Ground + 9f, 24f),
                teamMat, true, 0.7f);
            b.AddLight(new Vector3(x, Ground + 15f, 0f), GameTypes.TeamColor(team) * 1.3f, 40f, 7f);

            Vector3 corePos = new(x, Ground + 1.2f, 0f);
            int core = b.AddPowerNode(corePos, team == Team.Red ? Loc.NodeRedCore : Loc.NodeBlueCore,
                [], isCore: true, team: team);
            if (team == Team.Red) redCore = core; else blueCore = core;

            b.Locker(corePos + new Vector3(-sign * 7f, 0.2f, -7f),
                WeaponKind.ShockRifle, WeaponKind.LinkGun, WeaponKind.Minigun, WeaponKind.Avril);
            b.Locker(corePos + new Vector3(-sign * 7f, 0.2f, 7f),
                WeaponKind.ShockRifle, WeaponKind.LinkGun, WeaponKind.Minigun, WeaponKind.Avril);
            b.Ammo(corePos + new Vector3(-sign * 11f, 0.9f, 0f), AmmoKind.AvrilMissiles);
            b.Item(corePos + new Vector3(0f, 0.8f, -11f), PickupKind.BodyArmor);
            b.Item(corePos + new Vector3(0f, 0.8f, 11f), PickupKind.HealthPack);
            b.AddVehicle(VehicleKind.Manta, corePos + new Vector3(-sign * 12f, 1.4f, -14f), 0f, team);
            b.AddVehicle(VehicleKind.Scorpion, corePos + new Vector3(-sign * 12f, 0.7f, 14f), 0f, team);
            b.AddVehicle(VehicleKind.Hellbender, corePos + new Vector3(-sign * 17f, 1.6f, 0f), 0f, team);
            for (int i = 0; i < 4; i++)
                b.Spawn(corePos + new Vector3(-sign * (4f + i * 3f), 0.2f, -12f + i * 7f), 0f, team);

            // The base tower. In the original the Lightning Gun sits on top of it.
            Vector3 tower = new(x - sign * 4f, Ground + 1.2f, -sign * 18f);
            b.Solid(tower + new Vector3(-5f, 0f, -5f), tower + new Vector3(5f, 22f, 5f),
                MatId.TechPanelDark, true, 0.8f);
            b.Solid(tower + new Vector3(-7f, 22f, -7f), tower + new Vector3(7f, 23f, 7f),
                MatId.MetalGrate, true, 0.9f);
            b.AddJumpPad(tower + new Vector3(7f, 0.1f, 0f), tower + new Vector3(4f, 25f, 0f),
                new Vector3(0.5f, 0.85f, 1f));
            b.Weapon(tower + new Vector3(0f, 23.9f, 0f), WeaponKind.LightningGun);
            b.Ammo(tower + new Vector3(0f, 23.8f, 3f), AmmoKind.LightningCells);
        }

        // --- four prime nodes and two middle nodes ---
        Vector3 redWest = new(-70f, Ground + 1.2f, -50f);
        Vector3 redEast = new(-70f, Ground + 1.2f, 50f);
        Vector3 blueWest = new(70f, Ground + 1.2f, -50f);
        Vector3 blueEast = new(70f, Ground + 1.2f, 50f);
        Vector3 redMid = new(-26f, Ground + 1.2f, 0f);
        Vector3 blueMid = new(26f, Ground + 1.2f, 0f);

        int iRw = b.AddPowerNode(redWest, Loc.NodeWestPrime, []);
        int iRe = b.AddPowerNode(redEast, Loc.NodeEastPrime, []);
        int iBw = b.AddPowerNode(blueWest, Loc.NodeWestPrime, []);
        int iBe = b.AddPowerNode(blueEast, Loc.NodeEastPrime, []);
        int iRm = b.AddPowerNode(redMid, Loc.NodeMiddleNorth, []);
        int iBm = b.AddPowerNode(blueMid, Loc.NodeMiddleSouth, []);

        b.LinkPowerNodes(redCore, [iRw, iRe]);
        b.LinkPowerNodes(iRw, [redCore, iRm]);
        b.LinkPowerNodes(iRe, [redCore, iRm]);
        b.LinkPowerNodes(iRm, [iRw, iRe, iBm]);
        b.LinkPowerNodes(iBm, [iBw, iBe, iRm]);
        b.LinkPowerNodes(iBw, [blueCore, iBm]);
        b.LinkPowerNodes(iBe, [blueCore, iBm]);
        b.LinkPowerNodes(blueCore, [iBw, iBe]);

        foreach (var pos in new[] { redWest, redEast, blueWest, blueEast })
        {
            NodePad(b, pos, 13f, MatId.Concrete);
            NodeShelter(b, pos, 8f, 8f, MatId.TechPanelDark, MatId.MetalGrate);
            b.Locker(pos + new Vector3(-5f, 0.4f, 0f),
                WeaponKind.LinkGun, WeaponKind.FlakCannon, WeaponKind.Avril, WeaponKind.LightningGun);
            b.Weapon(pos + new Vector3(5f, 1f, 0f), WeaponKind.MineLayer);
            b.Ammo(pos + new Vector3(8f, 0.9f, 0f), AmmoKind.Mines);
            b.Item(pos + new Vector3(0f, 0.9f, -6f), PickupKind.HealthPack);
            b.Spawn(pos + new Vector3(0f, 0.4f, 8f), 0f);
            b.AddVehicle(VehicleKind.Manta, pos + new Vector3(-9f, 1.4f, 7f), 0f);
            b.AddVehicle(VehicleKind.Scorpion, pos + new Vector3(9f, 0.7f, 7f), 0f);
            b.AddVehicle(VehicleKind.Raptor, pos + new Vector3(0f, 11f, -9f), 0f);
        }

        foreach (var pos in new[] { redMid, blueMid })
        {
            NodePad(b, pos, 14f, MatId.Concrete);
            NodeShelter(b, pos, 9f, 9f, MatId.RustMetal, MatId.ArmorPlate);
            b.Locker(pos + new Vector3(0f, 0.4f, -7f),
                WeaponKind.ShockRifle, WeaponKind.LinkGun, WeaponKind.RocketLauncher, WeaponKind.Avril);
            b.Item(pos + new Vector3(0f, 0.9f, 7f), PickupKind.BodyArmor);
            b.Spawn(pos + new Vector3(6f, 0.4f, 0f), 0f);
            b.AddVehicle(VehicleKind.Goliath, pos + new Vector3(-9f, 1.8f, 0f), 0f);
            b.AddVehicle(VehicleKind.Hellbender, pos + new Vector3(9f, 1.6f, 0f), 0f);
        }

        // --- the support towers, each with a Target Painter on top ---
        // The originals put one on each side's tallest tower, and there is nothing else up there:
        // taking the shot means climbing away from the fight and staying still while it lands.
        foreach (var (pos, team) in new[]
                 {
                     (new Vector3(-40f, Ground, -76f), Team.Red),
                     (new Vector3(40f, Ground, 76f), Team.Blue),
                 })
        {
            b.Solid(pos + new Vector3(-14f, 0f, -14f), pos + new Vector3(14f, 1.2f, 14f),
                MatId.Concrete, true, 0.6f);
            b.Solid(pos + new Vector3(-6f, 1.2f, -6f), pos + new Vector3(6f, 30f, 6f),
                MatId.TechPanelDark, true, 0.8f);
            b.Solid(pos + new Vector3(-9f, 30f, -9f), pos + new Vector3(9f, 31f, 9f),
                MatId.MetalGrate, true, 0.9f);
            b.AddJumpPad(pos + new Vector3(10f, 1.3f, 0f), pos + new Vector3(6f, 33f, 0f),
                new Vector3(0.5f, 0.85f, 1f));
            int node = b.AddPowerNode(pos + new Vector3(0f, 1.2f, 11f), Loc.NodeSupport, [],
                role: NodeRole.Support);
            _ = node;
            b.Weapon(pos + new Vector3(0f, 31.9f, 0f), WeaponKind.TargetPainter, 120f);
            b.Locker(pos + new Vector3(-9f, 1.4f, 0f),
                WeaponKind.LinkGun, WeaponKind.Minigun, WeaponKind.Avril, WeaponKind.LightningGun);
            b.Item(pos + new Vector3(0f, 2.0f, -10f), PickupKind.ShieldBelt);
            b.Spawn(pos + new Vector3(0f, 1.4f, 8f), 0f, team);
            b.AddVehicle(VehicleKind.Goliath, pos + new Vector3(-11f, 2.0f, 8f), 0f);
        }

        // Our own addition, flagged as such: the Ion Painter has exactly one home map in UT2004
        // (ONS-Crossfire), so it gets its second outing on the far side of Dria's frozen lake —
        // in the open, a long way from either base, where taking it costs you the fight you were
        // in. Nothing else about the map's loadout departs from the original.
        b.Weapon(new Vector3(0f, Ground + 0.9f, -84f), WeaponKind.IonPainter, 150f);
        b.Solid(new Vector3(-8f, Ground - 0.6f, -92f), new Vector3(8f, Ground, -76f),
            MatId.Concrete, true, 0.6f);

        // --- ice ridges ---
        for (int i = 0; i < 40; i++)
        {
            float px = rng.Range(-HX + 14f, HX - 14f);
            float pz = rng.Range(-HZ + 12f, HZ - 12f);
            if (MathF.Abs(pz) < 26f) continue;
            float sz = rng.Range(2.4f, 6f);
            b.Solid(new Vector3(px - sz, Ground, pz - sz),
                new Vector3(px + sz, Ground + rng.Range(2.4f, 6.5f), pz + sz), MatId.Rock, true, 0.6f);
        }

        return b.Build(gl);
    }
}
