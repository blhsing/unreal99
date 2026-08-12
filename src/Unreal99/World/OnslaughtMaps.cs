using System.Numerics;
using Silk.NET.OpenGL;
using Unreal99.Core;
using Unreal99.Game;
using Unreal99.Rendering;
using Unreal99.UI;

namespace Unreal99.World;

/// <summary>
/// Onslaught arenas. These are built for the mode rather than adapted to it: open ground wide
/// enough to drive across, a node chain the front line advances along, and a vehicle set at each
/// node. Nothing here is a retrofitted deathmatch arena — the scale is completely different.
/// </summary>
public static partial class Maps
{
    // ================================================================ ONS-托蘭

    /// <summary>
    /// Torlan. The flagship Onslaught map: dried jungle outskirts, two mirrored bases, a
    /// communications tower over the middle, and a dry riverbed cutting the field in half as the
    /// route through the centre.
    ///
    /// Five nodes in a line, per the original — corners at each end, a pair flanking the middle,
    /// and the centre node wired into both cores. That chain is the map: neither team can touch
    /// a core until it has walked the links all the way across.
    /// </summary>
    private static Level BuildTorlan(GL gl)
    {
        var b = new LevelBuilder(Loc.MapTorlan, Loc.MapTorlanDesc);
        var env = b.Level.Environment;
        env.SunDirection = Vector3.Normalize(new Vector3(-0.34f, -0.78f, -0.52f));
        env.SunColor = new Vector3(3.5f, 3.25f, 2.75f);
        env.AmbientSky = new Vector3(0.30f, 0.34f, 0.44f);
        env.AmbientGround = new Vector3(0.20f, 0.18f, 0.14f);
        env.EnvIntensity = 0.70f;
        env.SkyTop = new Vector3(0.10f, 0.22f, 0.48f);
        env.SkyHorizon = new Vector3(0.62f, 0.62f, 0.55f);
        env.SkyGround = new Vector3(0.30f, 0.26f, 0.20f);
        env.StarStrength = 0f;
        env.CloudStrength = 0.9f;
        env.FogColor = new Vector3(0.60f, 0.60f, 0.55f);
        env.FogDensity = 0.0035f;

        const float HX = 120f, HZ = 90f;
        const float Ground = 0f;
        const float WallTop = 60f;

        // --- the plain, and the ridge wall that closes it ---
        b.Solid(new Vector3(-HX, -6f, -HZ), new Vector3(HX, Ground, HZ), MatId.Rock, true, 0.35f);
        DressOutdoor(b, HX, HZ, 0f, WallTop, MatId.Rock, MatId.Trim, 7);
        b.Room(new Vector3(-HX - 4f, -6f, -HZ - 4f), new Vector3(HX + 4f, WallTop, HZ + 4f), 4f,
            MatId.Rock, MatId.Rock, MatId.Rock, withCeiling: false, withFloor: false);

        // --- the dried riverbed: a sunken lane straight through the middle ---
        // Shallow enough to drive in and out of, which is the point — it is a route, not a trap.
        b.Solid(new Vector3(-HX, -3.2f, -11f), new Vector3(HX, Ground - 0.01f, 11f), MatId.Concrete, true, 0.5f);
        for (int s = -1; s <= 1; s += 2)
            b.Ramp(new Vector3(-HX, -3.2f, s * 11f), new Vector3(HX, Ground, s * 17f), s < 0 ? 3 : 2,
                MatId.Rock, true, 0.4f);

        var rng = new Rng(0x707A);

        // --- bases and cores at each end ---
        int redCore = -1, blueCore = -1;
        for (int side = 0; side < 2; side++)
        {
            float sign = side == 0 ? -1f : 1f;
            Team team = side == 0 ? Team.Red : Team.Blue;
            MatId teamMat = team == Team.Red ? MatId.TeamRed : MatId.TeamBlue;
            float x = sign * (HX - 22f);

            // Base pad with a low wall around the back, so a core has something to sit behind.
            b.Solid(new Vector3(x - 20f, Ground, -26f), new Vector3(x + 20f, Ground + 1.2f, 26f),
                MatId.Concrete, true, 0.6f);
            // These are vehicle aprons, not display plinths. Their 1.2 m vertical front used to
            // exceed the pawn step height, leaving four perfectly usable vehicles per base on a
            // slab the player could only mount by an awkward running jump. A broad driveable
            // approach makes the whole apron walk-on accessible and also gives vehicles a clean
            // way off it.
            float apronEdge = x - sign * 20f;
            b.Ramp(new Vector3(MathF.Min(apronEdge, apronEdge - sign * 5f), Ground, -5f),
                new Vector3(MathF.Max(apronEdge, apronEdge - sign * 5f), Ground + 1.2f, 5f),
                sign < 0f ? 1 : 0, MatId.Concrete, true, 0.6f);
            b.Solid(new Vector3(x + sign * 20f, Ground, -26f), new Vector3(x + sign * 23f, Ground + 7f, 26f),
                teamMat, true, 0.7f);
            for (int i = -2; i <= 2; i++)
                b.Solid(new Vector3(x - 20f, Ground + 1.2f, i * 11f - 1.4f),
                        new Vector3(x - 20f + 3f, Ground + 5f, i * 11f + 1.4f), teamMat, true, 0.8f);

            b.AddLight(new Vector3(x, Ground + 14f, 0f), GameTypes.TeamColor(team) * 1.3f, 40f, 7f);

            int core = b.AddPowerNode(new Vector3(x, Ground + 1.2f, 0f),
                team == Team.Red ? Loc.NodeRedCore : Loc.NodeBlueCore, [], isCore: true, team: team);
            if (team == Team.Red) redCore = core; else blueCore = core;

            // Per the original: Manta, Hellbender, Raptor and a Cicada at each core.
            b.AddVehicle(VehicleKind.Manta, new Vector3(x - sign * 10f, Ground + 2.6f, -12f), sign < 0 ? 90f : -90f, team);
            b.AddVehicle(VehicleKind.Hellbender, new Vector3(x - sign * 10f, Ground + 1.4f, 12f), sign < 0 ? 90f : -90f, team);
            b.AddVehicle(VehicleKind.Raptor, new Vector3(x - sign * 16f, Ground + 12f, -18f), sign < 0 ? 90f : -90f, team);
            b.AddVehicle(VehicleKind.Cicada, new Vector3(x - sign * 16f, Ground + 14f, 18f), sign < 0 ? 90f : -90f, team);

            // The original's base loadout: a Grenade Launcher and an AVRiL on the bridge by the
            // core, with two lockers. Rocket/Shock/Sniper were what this map carried before the
            // UT2004 arsenal existed here — none of the three is on the real ONS-Torlan.
            b.Weapon(new Vector3(x - sign * 4f, Ground + 2.1f, -6f), WeaponKind.GrenadeLauncher);
            b.Weapon(new Vector3(x - sign * 4f, Ground + 2.1f, 6f), WeaponKind.Avril);
            b.Ammo(new Vector3(x - sign * 6f, Ground + 2.0f, -6f), AmmoKind.Grenades);
            b.Ammo(new Vector3(x - sign * 6f, Ground + 2.0f, 6f), AmmoKind.AvrilMissiles);
            b.Locker(new Vector3(x - sign * 9f, Ground + 1.4f, -11f),
                WeaponKind.ShockRifle, WeaponKind.LinkGun, WeaponKind.FlakCannon);
            b.Locker(new Vector3(x - sign * 9f, Ground + 1.4f, 11f),
                WeaponKind.ShockRifle, WeaponKind.LinkGun, WeaponKind.FlakCannon);
            b.Item(new Vector3(x, Ground + 2.0f, -9f), PickupKind.BodyArmor);
            b.Item(new Vector3(x, Ground + 2.0f, 9f), PickupKind.HealthPack);
            for (int i = 0; i < 4; i++)
                b.Spawn(new Vector3(x - sign * (2f + i * 3f), Ground + 1.4f, -14f + i * 9f),
                    sign < 0 ? 90f : -90f, team);
        }

        // --- the five-node chain, corners out and centre in the middle ---
        // Order matters: links are indices, and the chain is what the whole mode runs on.
        Vector3 n1 = new(-62f, Ground, -58f);
        Vector3 n2 = new(-34f, Ground, 8f);
        Vector3 n3 = new(0f, Ground, -4f);
        Vector3 n4 = new(34f, Ground, 8f);
        Vector3 n5 = new(62f, Ground, -58f);

        int i1 = b.AddPowerNode(n1 + new Vector3(0f, 1.2f, 0f), Loc.NodeWestCorner, []);
        int i2 = b.AddPowerNode(n2 + new Vector3(0f, 1.2f, 0f), Loc.NodeWestFlank, []);
        int i3 = b.AddPowerNode(n3 + new Vector3(0f, 5.2f, 0f), Loc.NodeTower, []);
        int i4 = b.AddPowerNode(n4 + new Vector3(0f, 1.2f, 0f), Loc.NodeEastFlank, []);
        int i5 = b.AddPowerNode(n5 + new Vector3(0f, 1.2f, 0f), Loc.NodeEastCorner, []);

        // Official default Torlan layout: both cores first link to their nearby prime/flank node.
        // Between those primes are two parallel routes, one through the central tower and one
        // around both Goliath corner nodes. This creates the original strategic choice without
        // making any middle node directly reachable from a protected core.
        b.LinkPowerNodes(redCore, [i2]);
        b.LinkPowerNodes(i1, [i2, i5]);
        b.LinkPowerNodes(i2, [redCore, i1, i3]);
        b.LinkPowerNodes(i3, [i2, i4]);
        b.LinkPowerNodes(i4, [i3, i5, blueCore]);
        b.LinkPowerNodes(i5, [i1, i4]);
        b.LinkPowerNodes(blueCore, [i4]);

        foreach (var (pos, kinds) in new (Vector3, VehicleKind[])[]
                 {
                     (n1, [VehicleKind.Manta, VehicleKind.Goliath]),
                     (n2, [VehicleKind.Manta, VehicleKind.Scorpion, VehicleKind.Paladin]),
                     (n4, [VehicleKind.Manta, VehicleKind.Scorpion, VehicleKind.Paladin]),
                     (n5, [VehicleKind.Manta, VehicleKind.Goliath]),
                 })
        {
            // A pad so the node reads as somewhere worth holding rather than a pole in a field.
            b.Solid(pos + new Vector3(-9f, 0f, -9f), pos + new Vector3(9f, 1.2f, 9f), MatId.Concrete, true, 0.6f);
            b.Ramp(pos + new Vector3(9f, 0f, -3.5f),
                pos + new Vector3(14f, 1.2f, 3.5f), 1, MatId.Concrete, true, 0.6f);
            for (int i = 0; i < kinds.Length; i++)
                b.AddVehicle(kinds[i], pos + new Vector3(-6f + i * 6f, 2.6f, 11f), 0f);
            // Prime nodes get the Mine Layer locker; the corner Goliath nodes get the Grenade
            // Launcher and Lightning Gun one, exactly as the original splits them.
            b.Locker(pos + new Vector3(-4f, 1.4f, 0f),
                WeaponKind.MineLayer, WeaponKind.LinkGun, WeaponKind.Minigun);
            b.Locker(pos + new Vector3(4f, 1.4f, 0f),
                WeaponKind.LinkGun, WeaponKind.GrenadeLauncher, WeaponKind.LightningGun);
            b.Item(pos + new Vector3(0f, 2.0f, -5f), PickupKind.HealthPack);
            b.Spawn(pos + new Vector3(0f, 1.4f, -7f), 0f);
        }

        // --- the communications tower over the centre node ---
        b.Solid(n3 + new Vector3(-13f, 0f, -13f), n3 + new Vector3(13f, 1.2f, 13f), MatId.Concrete, true, 0.6f);
        b.Ramp(n3 + new Vector3(-5f, 0f, -18f),
            n3 + new Vector3(5f, 1.2f, -13f), 2, MatId.Concrete, true, 0.6f);
        for (int i = 0; i < 4; i++)
        {
            float a = i / 4f * MathX.TwoPi + MathX.Pi * 0.25f;
            // Prism is centred on the position, so the column's midpoint sits at half its height.
            b.Prism(n3 + new Vector3(MathF.Cos(a) * 9f, 1.2f + 10.4f, MathF.Sin(a) * 9f), 1.1f, 20.8f, 6,
                MatId.RustMetal);
        }
        // Upper deck: the sniping platform the tower exists for.
        b.Solid(n3 + new Vector3(-11f, 22f, -11f), n3 + new Vector3(11f, 23f, 11f), MatId.MetalGrate, true, 0.9f);
        RingPosts(b, 23f, 10.4f, 16);
        b.Prism(n3 + new Vector3(0f, 30f, 0f), 2.2f, 14f, 8, MatId.TechPanelDark);
        b.Decor(n3 + new Vector3(-2.6f, 37f, -2.6f), n3 + new Vector3(2.6f, 38.4f, 2.6f), MatId.EnergyPanel, 0.7f);
        b.AddLight(n3 + new Vector3(0f, 39f, 0f), new Vector3(1f, 0.55f, 0.3f), 46f, 8f, 1.4f, 0.2f);
        // Pads up, because a 22m climb is far too steep for the nav graph to route a bot over.
        for (int s = -1; s <= 1; s += 2)
            b.AddJumpPad(n3 + new Vector3(s * 10f, 1.3f, 0f), n3 + new Vector3(s * 8f, 25f, 0f),
                new Vector3(0.45f, 0.85f, 1f));
        b.AddVehicle(VehicleKind.Spma, n3 + new Vector3(0f, 2.6f, 14f), 0f);
        b.AddVehicle(VehicleKind.Hellbender, n3 + new Vector3(-8f, 23.6f, 0f), 0f);
        b.AddVehicle(VehicleKind.Raptor, n3 + new Vector3(8f, 34f, 0f), 0f);
        b.Weapon(n3 + new Vector3(0f, 23.9f, -6f), WeaponKind.LightningGun);
        b.Ammo(n3 + new Vector3(0f, 23.8f, -9f), AmmoKind.LightningCells);
        b.Item(n3 + new Vector3(0f, 23.8f, 6f), PickupKind.ShieldBelt);
        // The Redeemer sits on top of the tower bridge in the original.
        b.Weapon(n3 + new Vector3(0f, 31.0f, 0f), WeaponKind.Redeemer, 100f);
        b.Locker(n3 + new Vector3(0f, 1.4f, 6f),
            WeaponKind.ShockRifle, WeaponKind.LinkGun, WeaponKind.Avril);

        // --- scattered rock cover so the open ground is not featureless ---
        for (int i = 0; i < 40; i++)
        {
            float px = rng.Range(-HX + 12f, HX - 12f);
            float pz = rng.Range(-HZ + 12f, HZ - 12f);
            if (MathF.Abs(pz) < 18f && MathF.Abs(px) < 100f) continue;   // keep the riverbed clear
            Vector3 rockPosition = new(px, Ground, pz);
            // Procedural cover must not turn authored vehicle bays into accidental plinths. The
            // collision settle pass correctly parks a vehicle on the highest surface beneath its
            // centre; before this exclusion, a random boulder could therefore lift a node vehicle
            // several metres onto a surface that had no authored access route.
            if ((MathF.Abs(px - (HX - 22f)) < 27f && MathF.Abs(pz) < 31f)
                || (MathF.Abs(px + (HX - 22f)) < 27f && MathF.Abs(pz) < 31f)
                || new[] { n1, n2, n3, n4, n5 }.Any(node =>
                    (rockPosition - node).FlatXZ().LengthSquared() < 22f * 22f))
                continue;
            float sz = rng.Range(1.8f, 4.4f);
            b.Solid(new Vector3(px - sz, Ground, pz - sz),
                    new Vector3(px + sz, Ground + rng.Range(2.2f, 5.5f), pz + sz), MatId.Rock, true, 0.6f);
        }

        return b.Build(gl);
    }

    // ================================================================ ONS-原始林

    /// <summary>
    /// Primeval. The small Onslaught map: a forest clearing with only three nodes, sized for 6–10
    /// players rather than a full 16. The flanks are the two routes to the enemy core; the centre
    /// node is off the critical path and exists purely as the prize — it is the only Goliath on
    /// the map, which is why both teams keep leaving the front line to fight over it.
    /// </summary>
    private static Level BuildPrimeval(GL gl)
    {
        var b = new LevelBuilder(Loc.MapPrimeval, Loc.MapPrimevalDesc);
        var env = b.Level.Environment;
        env.SunDirection = Vector3.Normalize(new Vector3(-0.22f, -0.90f, -0.38f));
        env.SunColor = new Vector3(1.85f, 2.05f, 1.55f);
        env.AmbientSky = new Vector3(0.16f, 0.24f, 0.20f);
        env.AmbientGround = new Vector3(0.09f, 0.11f, 0.07f);
        env.EnvIntensity = 0.55f;
        env.SkyTop = new Vector3(0.05f, 0.11f, 0.09f);
        env.SkyHorizon = new Vector3(0.24f, 0.31f, 0.22f);
        env.SkyGround = new Vector3(0.10f, 0.12f, 0.07f);
        env.StarStrength = 0f;
        env.CloudStrength = 0.35f;
        env.FogColor = new Vector3(0.13f, 0.19f, 0.15f);
        env.FogDensity = 0.0075f;                    // forest gloom; sightlines are short here

        const float HX = 92f, HZ = 70f;
        const float Ground = 0f;

        b.Solid(new Vector3(-HX, -6f, -HZ), new Vector3(HX, Ground, HZ), MatId.Rock, true, 0.4f);
        DressOutdoor(b, HX, HZ, 0f, 54f, MatId.Rock, MatId.Trim, 6);
        b.Room(new Vector3(-HX - 4f, -6f, -HZ - 4f), new Vector3(HX + 4f, 54f, HZ + 4f), 4f,
            MatId.Rock, MatId.Rock, MatId.Rock, withCeiling: false, withFloor: false);

        var rng = new Rng(0x5A17);

        // --- bases ---
        int redCore = -1, blueCore = -1;
        for (int side = 0; side < 2; side++)
        {
            float sign = side == 0 ? -1f : 1f;
            Team team = side == 0 ? Team.Red : Team.Blue;
            MatId teamMat = team == Team.Red ? MatId.TeamRed : MatId.TeamBlue;
            float x = sign * (HX - 18f);

            b.Solid(new Vector3(x - 15f, Ground, -20f), new Vector3(x + 15f, Ground + 1.2f, 20f),
                MatId.Concrete, true, 0.6f);
            b.Solid(new Vector3(x + sign * 15f, Ground, -20f), new Vector3(x + sign * 18f, Ground + 6f, 20f),
                teamMat, true, 0.7f);
            b.AddLight(new Vector3(x, Ground + 12f, 0f), GameTypes.TeamColor(team) * 1.2f, 34f, 6f);

            int core = b.AddPowerNode(new Vector3(x, Ground + 1.2f, 0f),
                team == Team.Red ? Loc.NodeRedCore : Loc.NodeBlueCore, [], isCore: true, team: team);
            if (team == Team.Red) redCore = core; else blueCore = core;

            // Per the original: a Scorpion and a Hellbender at each base, and nothing heavier.
            b.AddVehicle(VehicleKind.Scorpion, new Vector3(x - sign * 9f, Ground + 1.9f, -9f),
                sign < 0 ? 90f : -90f, team);
            b.AddVehicle(VehicleKind.Hellbender, new Vector3(x - sign * 9f, Ground + 1.6f, 9f),
                sign < 0 ? 90f : -90f, team);

            // ONS-Primeval carries no loose weapons whatsoever in the original — ten lockers and
            // nothing else. Two of them stand in each base.
            b.Locker(new Vector3(x - sign * 3f, Ground + 1.4f, -5f),
                WeaponKind.MineLayer, WeaponKind.LinkGun, WeaponKind.FlakCannon, WeaponKind.Avril);
            b.Locker(new Vector3(x - sign * 3f, Ground + 1.4f, 5f),
                WeaponKind.MineLayer, WeaponKind.LinkGun, WeaponKind.FlakCannon, WeaponKind.Avril);
            b.Item(new Vector3(x, Ground + 2.0f, 0f), PickupKind.BodyArmor);
            for (int i = 0; i < 3; i++)
                b.Spawn(new Vector3(x - sign * (2f + i * 3f), Ground + 1.4f, -10f + i * 10f),
                    sign < 0 ? 90f : -90f, team);
        }

        // --- three nodes: two flanks and a centre that is not on the way to anything ---
        Vector3 n1 = new(0f, Ground, -38f);
        Vector3 n2 = new(0f, Ground, 38f);
        Vector3 n3 = new(0f, Ground, 0f);

        int i1 = b.AddPowerNode(n1 + new Vector3(0f, 1.2f, 0f), Loc.NodeNorthTrail, []);
        int i2 = b.AddPowerNode(n2 + new Vector3(0f, 1.2f, 0f), Loc.NodeSouthTrail, []);
        int i3 = b.AddPowerNode(n3 + new Vector3(0f, 1.2f, 0f), Loc.NodeGrove, []);

        // Official Primeval default: each core links to its own side/prime node and both cores
        // also link directly to the contested centre. The side nodes link onward to the centre;
        // the alternative One-Way setup is the same chain without the core-to-centre shortcuts.
        b.LinkPowerNodes(redCore, [i1, i3]);
        b.LinkPowerNodes(blueCore, [i2, i3]);
        b.LinkPowerNodes(i1, [redCore, i3]);
        b.LinkPowerNodes(i2, [blueCore, i3]);
        b.LinkPowerNodes(i3, [redCore, blueCore, i1, i2]);

        foreach (var (pos, name) in new[] { (n1, "n1"), (n2, "n2") })
        {
            b.Solid(pos + new Vector3(-8f, 0f, -8f), pos + new Vector3(8f, 1.2f, 8f), MatId.Concrete, true, 0.6f);
            b.AddVehicle(VehicleKind.Manta, pos + new Vector3(-5f, 2.6f, 10f), 0f);
            b.AddVehicle(VehicleKind.Scorpion, pos + new Vector3(5f, 1.9f, 10f), 0f);
            b.Locker(pos + new Vector3(-3f, 1.4f, 0f),
                WeaponKind.ShockRifle, WeaponKind.LinkGun, WeaponKind.Avril);
            b.Locker(pos + new Vector3(3f, 1.4f, 0f),
                WeaponKind.ShockRifle, WeaponKind.LinkGun, WeaponKind.Avril);
            b.Spawn(pos + new Vector3(0f, 1.4f, -6f), 0f);
            _ = name;
        }

        // The centre: the only heavy armour on the map, and the four health packs that go with it.
        b.Solid(n3 + new Vector3(-11f, 0f, -11f), n3 + new Vector3(11f, 1.2f, 11f), MatId.Concrete, true, 0.6f);
        b.AddVehicle(VehicleKind.Goliath, n3 + new Vector3(0f, 2.2f, 14f), 0f);
        for (int s = -1; s <= 1; s += 2)
        {
            b.Item(n3 + new Vector3(s * 6f, 2.0f, -3f), PickupKind.HealthPack);
            b.Item(n3 + new Vector3(s * 6f, 2.0f, 3f), PickupKind.HealthPack);
        }
        b.Locker(n3 + new Vector3(-3f, 1.4f, -7f), WeaponKind.LinkGun, WeaponKind.RocketLauncher);
        b.Locker(n3 + new Vector3(3f, 1.4f, -7f), WeaponKind.LinkGun, WeaponKind.RocketLauncher);
        b.Item(n3 + new Vector3(0f, 2.1f, 7f), PickupKind.ShieldBelt);
        b.Spawn(n3 + new Vector3(0f, 1.4f, -9f), 0f);

        // --- the forest itself ---
        // Trunks are collidable and dense enough to break sightlines, but the node pads and the
        // lanes between them stay clear so a Goliath can actually drive from one to the next.
        for (int i = 0; i < 90; i++)
        {
            float px = rng.Range(-HX + 8f, HX - 8f);
            float pz = rng.Range(-HZ + 8f, HZ - 8f);
            bool nearNode = MathF.Abs(px) < 15f && (MathF.Abs(pz) < 15f
                || MathF.Abs(pz - 38f) < 13f || MathF.Abs(pz + 38f) < 13f);
            if (nearNode) continue;
            if (MathF.Abs(px) > HX - 26f && MathF.Abs(pz) < 24f) continue;   // keep the bases open
            if (MathF.Abs(pz) < 5f) continue;                                // the centre lane

            float r = rng.Range(0.55f, 1.15f);
            float h = rng.Range(11f, 22f);
            // Prism centres its box on the position, so a trunk has to be raised by half its
            // height to stand on the ground rather than be buried to the waist in it.
            b.Prism(new Vector3(px, Ground + h * 0.5f, pz), r, h, 6, MatId.Rock);
            // Canopy: three tapering non-colliding tiers rather than one slab, so it reads as a
            // crown of foliage instead of a floating platform. Nothing here blocks a Raptor.
            for (int tier = 0; tier < 3; tier++)
            {
                float spread = r * (5.4f - tier * 1.5f);
                float ty = Ground + h - 3.2f + tier * 1.7f;
                b.Prism(new Vector3(px, ty, pz), spread, 1.5f, 7, MatId.Rock,
                    collide: false, rotation: tier * 0.5f);
            }
        }

        // Fallen logs give the open lanes something to break a charge on.
        for (int i = 0; i < 14; i++)
        {
            float px = rng.Range(-HX + 20f, HX - 20f);
            float pz = rng.Range(-HZ + 14f, HZ - 14f);
            if (MathF.Abs(px) < 14f && MathF.Abs(pz) < 14f) continue;
            bool alongX = rng.NextFloat() < 0.5f;
            float len = rng.Range(5f, 11f);
            Vector3 half = alongX ? new Vector3(len, 1.0f, 1.0f) : new Vector3(1.0f, 1.0f, len);
            b.Solid(new Vector3(px, Ground, pz) - half with { Y = 0f },
                    new Vector3(px, Ground, pz) + half, MatId.Rock, true, 0.7f);
        }

        return b.Build(gl);
    }
}
