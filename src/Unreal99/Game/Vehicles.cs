using System.Numerics;
using Unreal99.Core;
using Unreal99.UI;

namespace Unreal99.Game;

/// <summary>
/// Every vehicle from UT2004 and UT3. Seat counts, locomotion and armament follow the sourced
/// roster in docs/original-map-reference.md rather than memory — the Hellbender's driver really
/// does have no weapon, and the Leviathan really does seat five.
/// </summary>
public enum VehicleKind
{
    // --- Axon (UT2004, carried into UT3) ---
    Scorpion = 0,
    Hellbender,
    Goliath,
    Leviathan,
    Paladin,
    Spma,
    Manta,
    Raptor,
    Cicada,
    IonTank,
    // --- Necris (UT3) ---
    Viper,
    Scavenger,
    Nemesis,
    Nightshade,
    Fury,
    Darkwalker,
    // --- personal ---
    Hoverboard,
    Count
}

/// <summary>
/// How a vehicle relates to the ground. This drives the whole movement solver: wheeled hugs the
/// surface and cannot cross a gap, hover floats a fixed height and can, air ignores the ground
/// entirely, and a walker strides — tall enough to step over what would stop a wheel.
/// </summary>
public enum VehicleMotion { Wheeled, Hover, Air, Walker }

/// <summary>One crew position: where it sits, and what it can shoot from there.</summary>
public struct VehicleSeatDef
{
    public Vector3 Offset;        // local, relative to the hull origin
    public string Role;
    /// <summary>False for the Hellbender's driver, who genuinely has no weapon of any kind.</summary>
    public bool Armed;
    public FireDef Primary;
    public FireDef Alt;
    /// <summary>Turret seats aim independently of the hull; the driver's aim steers instead.</summary>
    public bool Turret;
}

public sealed class VehicleDef
{
    public VehicleKind Kind;
    public string Name;
    public VehicleMotion Motion;
    public float Health = 800f;
    public float MaxSpeed = 18f;
    public float Acceleration = 22f;
    public float TurnRate = 1.7f;          // radians per second
    public float HoverHeight;              // Hover and Air only
    public Vector3 HalfExtents = new(1.6f, 1.0f, 2.6f);
    public Vector3 Tint = Vector3.One;
    public VehicleSeatDef[] Seats = [];
    /// <summary>Crushes players it drives into. Everything with mass does; the hoverboard does not.</summary>
    public bool Crushes = true;
    /// <summary>Deploys into a stationary heavy-weapon platform (Leviathan's Ion Cannon).</summary>
    public bool CanDeploy;
    public float DeploySeconds = 5.5f;
    /// <summary>Holds a damage-absorbing shield while alt-fire is held (Paladin).</summary>
    public bool HasShield;
    /// <summary>Can turn itself into a weapon (Viper). Kills the driver too.</summary>
    public bool CanSelfDestruct;
    /// <summary>Goes invisible while stationary and unfired (Nightshade).</summary>
    public bool CanCloak;
    public int SeatCount => Seats.Length;

    public static readonly VehicleDef[] All = new VehicleDef[(int)VehicleKind.Count];
    public static VehicleDef Get(VehicleKind k) => All[(int)k];

    private static FireDef Cannon(float damage, float interval, float speed, float splash) => new()
    {
        Mode = FireMode.Projectile, Projectile = ProjectileKind.Rocket, Damage = damage,
        Interval = interval, Shots = 1, ProjectileSpeed = speed,
        SplashRadius = splash, SplashDamage = damage * 0.7f, Knockback = 14f, ShakeAmount = 0.8f,
    };

    private static FireDef Plasma(float damage, float interval, float speed) => new()
    {
        Mode = FireMode.Projectile, Projectile = ProjectileKind.PlasmaBolt, Damage = damage,
        Interval = interval, Shots = 1, ProjectileSpeed = speed,
        SplashRadius = 2.2f, SplashDamage = damage * 0.4f, Automatic = true, ShakeAmount = 0.2f,
    };

    private static FireDef Beam(float damage, float interval, float range, float zoom = 0f) => new()
    {
        Mode = FireMode.Hitscan, Damage = damage, Interval = interval, Shots = 1,
        Range = range, ZoomFov = zoom, ShakeAmount = 0.3f,
    };

    static VehicleDef()
    {
        // ---------------------------------------------------------------- Axon

        All[(int)VehicleKind.Scorpion] = new VehicleDef
        {
            Kind = VehicleKind.Scorpion, Name = Loc.VehScorpion, Motion = VehicleMotion.Wheeled,
            Health = 500f, MaxSpeed = 26f, Acceleration = 30f, TurnRate = 2.1f,
            HalfExtents = new Vector3(1.4f, 0.85f, 2.4f), Tint = new Vector3(0.75f, 0.72f, 0.55f),
            Seats =
            [
                new VehicleSeatDef
                {
                    Offset = new Vector3(0f, 0.9f, -0.2f), Role = Loc.VehSeatDriver, Armed = true,
                    // Energy bola: charge it wider, and it wraps whatever it touches.
                    Primary = new FireDef
                    {
                        Mode = FireMode.Projectile, Projectile = ProjectileKind.BioGlob, Damage = 45f,
                        Interval = 1.1f, Shots = 1, ProjectileSpeed = 34f, SplashRadius = 3.4f,
                        SplashDamage = 40f, Chargeable = true, MaxCharge = 1.2f, ShakeAmount = 0.4f,
                    },
                    // Blades: lethal to anyone on foot, useless against anything armoured.
                    Alt = new FireDef
                    {
                        Mode = FireMode.Melee, Damage = 220f, Interval = 0.5f, Shots = 1,
                        Range = 3.2f, Knockback = 12f,
                    },
                },
            ],
        };

        All[(int)VehicleKind.Hellbender] = new VehicleDef
        {
            Kind = VehicleKind.Hellbender, Name = Loc.VehHellbender, Motion = VehicleMotion.Wheeled,
            Health = 800f, MaxSpeed = 20f, Acceleration = 22f, TurnRate = 1.6f,
            HalfExtents = new Vector3(1.7f, 1.05f, 3.0f), Tint = new Vector3(0.55f, 0.58f, 0.66f),
            Seats =
            [
                // The driver has no weapon at all — unique among the roster, and deliberate.
                new VehicleSeatDef { Offset = new Vector3(0f, 1.1f, 0.6f), Role = Loc.VehSeatDriver, Armed = false },
                new VehicleSeatDef
                {
                    Offset = new Vector3(0f, 1.5f, -0.4f), Role = Loc.VehSeatSkymine, Armed = true, Turret = true,
                    Primary = new FireDef
                    {
                        Mode = FireMode.Projectile, Projectile = ProjectileKind.ShockBall, Damage = 32f,
                        Interval = 0.45f, Shots = 1, ProjectileSpeed = 20f, SplashRadius = 4.2f,
                        SplashDamage = 55f, Automatic = true, ShakeAmount = 0.3f,
                    },
                    Alt = Beam(40f, 0.7f, 140f),
                },
                new VehicleSeatDef
                {
                    Offset = new Vector3(0f, 1.5f, -1.6f), Role = Loc.VehSeatLaser, Armed = true, Turret = true,
                    Primary = Beam(55f, 1.0f, 220f),
                    Alt = Beam(55f, 1.0f, 220f, zoom: 28f),
                },
            ],
        };

        All[(int)VehicleKind.Goliath] = new VehicleDef
        {
            Kind = VehicleKind.Goliath, Name = Loc.VehGoliath, Motion = VehicleMotion.Wheeled,
            Health = 1400f, MaxSpeed = 14f, Acceleration = 14f, TurnRate = 1.0f,
            HalfExtents = new Vector3(2.0f, 1.3f, 3.4f), Tint = new Vector3(0.45f, 0.48f, 0.40f),
            Seats =
            [
                new VehicleSeatDef
                {
                    Offset = new Vector3(0f, 1.9f, 0.2f), Role = Loc.VehSeatDriver, Armed = true, Turret = true,
                    Primary = Cannon(180f, 2.6f, 90f, 6.5f),
                    Alt = Cannon(180f, 2.6f, 90f, 6.5f),
                },
                new VehicleSeatDef
                {
                    Offset = new Vector3(0f, 2.5f, 0.2f), Role = Loc.VehSeatMachineGun, Armed = true, Turret = true,
                    Primary = new FireDef
                    {
                        Mode = FireMode.Hitscan, Damage = 14f, Interval = 0.09f, Shots = 1,
                        Range = 160f, Spread = 0.022f, Automatic = true,
                    },
                    Alt = Beam(14f, 0.09f, 160f, zoom: 34f),
                },
            ],
        };

        All[(int)VehicleKind.Leviathan] = new VehicleDef
        {
            Kind = VehicleKind.Leviathan, Name = Loc.VehLeviathan, Motion = VehicleMotion.Wheeled,
            Health = 5000f, MaxSpeed = 8f, Acceleration = 7f, TurnRate = 0.55f,
            HalfExtents = new Vector3(3.2f, 2.0f, 5.4f), Tint = new Vector3(0.42f, 0.45f, 0.50f),
            CanDeploy = true, DeploySeconds = 5.5f,
            Seats =
            [
                new VehicleSeatDef
                {
                    Offset = new Vector3(0f, 2.8f, 0.8f), Role = Loc.VehSeatDriver, Armed = true, Turret = true,
                    // Homing rockets in a continuous stream; the Ion Cannon replaces this once deployed.
                    Primary = new FireDef
                    {
                        Mode = FireMode.Projectile, Projectile = ProjectileKind.Rocket, Damage = 60f,
                        Interval = 0.35f, Shots = 1, ProjectileSpeed = 55f, SplashRadius = 4.5f,
                        SplashDamage = 45f, Automatic = true, ShakeAmount = 0.4f,
                    },
                    Alt = Cannon(400f, 4.5f, 200f, 14f),
                },
                new VehicleSeatDef { Offset = new Vector3(-2.4f, 2.4f, 3.4f), Role = Loc.VehSeatCornerTurret, Armed = true, Turret = true, Primary = Plasma(20f, 0.22f, 70f), Alt = Plasma(20f, 0.22f, 70f) },
                new VehicleSeatDef { Offset = new Vector3(2.4f, 2.4f, 3.4f), Role = Loc.VehSeatCornerTurret, Armed = true, Turret = true, Primary = Plasma(20f, 0.22f, 70f), Alt = Plasma(20f, 0.22f, 70f) },
                new VehicleSeatDef { Offset = new Vector3(-2.4f, 2.4f, -3.4f), Role = Loc.VehSeatCornerTurret, Armed = true, Turret = true, Primary = Plasma(20f, 0.22f, 70f), Alt = Plasma(20f, 0.22f, 70f) },
                new VehicleSeatDef { Offset = new Vector3(2.4f, 2.4f, -3.4f), Role = Loc.VehSeatCornerTurret, Armed = true, Turret = true, Primary = Plasma(20f, 0.22f, 70f), Alt = Plasma(20f, 0.22f, 70f) },
            ],
        };

        All[(int)VehicleKind.Paladin] = new VehicleDef
        {
            Kind = VehicleKind.Paladin, Name = Loc.VehPaladin, Motion = VehicleMotion.Wheeled,
            Health = 1400f, MaxSpeed = 11f, Acceleration = 12f, TurnRate = 0.85f,
            HalfExtents = new Vector3(2.0f, 1.3f, 3.2f), Tint = new Vector3(0.50f, 0.55f, 0.62f),
            HasShield = true,
            Seats =
            [
                new VehicleSeatDef
                {
                    Offset = new Vector3(0f, 1.9f, 0.2f), Role = Loc.VehSeatDriver, Armed = true, Turret = true,
                    Primary = Cannon(120f, 1.8f, 42f, 5.5f),
                    Alt = new FireDef { Mode = FireMode.Melee, Damage = 0f, Interval = 0.2f, Range = 0f },
                },
            ],
        };

        All[(int)VehicleKind.Spma] = new VehicleDef
        {
            Kind = VehicleKind.Spma, Name = Loc.VehSpma, Motion = VehicleMotion.Wheeled,
            Health = 900f, MaxSpeed = 21f, Acceleration = 24f, TurnRate = 1.5f,
            HalfExtents = new Vector3(1.7f, 1.1f, 3.2f), Tint = new Vector3(0.52f, 0.54f, 0.46f),
            Seats =
            [
                new VehicleSeatDef
                {
                    Offset = new Vector3(0f, 1.6f, 0.4f), Role = Loc.VehSeatArtillery, Armed = true, Turret = true,
                    // Lobbed, not aimed flat: the shell arcs and bursts into shrapnel above the target.
                    Primary = new FireDef
                    {
                        Mode = FireMode.Projectile, Projectile = ProjectileKind.Grenade, Damage = 150f,
                        Interval = 3.0f, Shots = 1, ProjectileSpeed = 46f, SplashRadius = 9f,
                        SplashDamage = 110f, ShakeAmount = 0.9f,
                    },
                    Alt = new FireDef
                    {
                        Mode = FireMode.Projectile, Projectile = ProjectileKind.Grenade, Damage = 150f,
                        Interval = 3.0f, Shots = 1, ProjectileSpeed = 46f, SplashRadius = 9f,
                        SplashDamage = 110f, ShakeAmount = 0.9f,
                    },
                },
                new VehicleSeatDef
                {
                    Offset = new Vector3(0f, 1.6f, -1.4f), Role = Loc.VehSeatSkymine, Armed = true, Turret = true,
                    Primary = new FireDef
                    {
                        Mode = FireMode.Projectile, Projectile = ProjectileKind.ShockBall, Damage = 30f,
                        Interval = 0.5f, Shots = 1, ProjectileSpeed = 20f, SplashRadius = 4f,
                        SplashDamage = 50f, Automatic = true,
                    },
                    Alt = Beam(36f, 0.7f, 140f),
                },
            ],
        };

        All[(int)VehicleKind.Manta] = new VehicleDef
        {
            Kind = VehicleKind.Manta, Name = Loc.VehManta, Motion = VehicleMotion.Hover,
            Health = 350f, MaxSpeed = 32f, Acceleration = 40f, TurnRate = 2.8f, HoverHeight = 2.2f,
            HalfExtents = new Vector3(2.0f, 0.7f, 2.2f), Tint = new Vector3(0.60f, 0.62f, 0.70f),
            Seats =
            [
                new VehicleSeatDef
                {
                    Offset = new Vector3(0f, 0.8f, 0f), Role = Loc.VehSeatPilot, Armed = true,
                    Primary = Plasma(18f, 0.16f, 78f),
                    // Dive: drop the nose and crush whatever is underneath.
                    Alt = new FireDef { Mode = FireMode.Melee, Damage = 140f, Interval = 0.6f, Range = 3.6f, Knockback = 10f },
                },
            ],
        };

        All[(int)VehicleKind.Raptor] = new VehicleDef
        {
            Kind = VehicleKind.Raptor, Name = Loc.VehRaptor, Motion = VehicleMotion.Air,
            Health = 500f, MaxSpeed = 28f, Acceleration = 18f, TurnRate = 1.5f, HoverHeight = 14f,
            HalfExtents = new Vector3(2.2f, 1.0f, 2.6f), Tint = new Vector3(0.48f, 0.52f, 0.58f),
            Seats =
            [
                new VehicleSeatDef
                {
                    Offset = new Vector3(0f, 0.9f, 0.2f), Role = Loc.VehSeatPilot, Armed = true,
                    Primary = Plasma(22f, 0.18f, 82f),
                    Alt = new FireDef
                    {
                        Mode = FireMode.Projectile, Projectile = ProjectileKind.Rocket, Damage = 70f,
                        Interval = 1.4f, Shots = 1, ProjectileSpeed = 60f, SplashRadius = 4.5f,
                        SplashDamage = 50f,
                    },
                },
            ],
        };

        All[(int)VehicleKind.Cicada] = new VehicleDef
        {
            Kind = VehicleKind.Cicada, Name = Loc.VehCicada, Motion = VehicleMotion.Air,
            Health = 800f, MaxSpeed = 20f, Acceleration = 13f, TurnRate = 1.1f, HoverHeight = 16f,
            HalfExtents = new Vector3(2.6f, 1.2f, 3.0f), Tint = new Vector3(0.44f, 0.47f, 0.52f),
            Seats =
            [
                new VehicleSeatDef
                {
                    Offset = new Vector3(0f, 1.0f, 0.6f), Role = Loc.VehSeatPilot, Armed = true,
                    Primary = new FireDef
                    {
                        Mode = FireMode.Projectile, Projectile = ProjectileKind.Rocket, Damage = 45f,
                        Interval = 0.28f, Shots = 1, ProjectileSpeed = 48f, SplashRadius = 4f,
                        SplashDamage = 35f, Spread = 0.06f, Automatic = true,
                    },
                    Alt = Cannon(90f, 1.6f, 70f, 6f),
                },
                new VehicleSeatDef
                {
                    Offset = new Vector3(0f, 0.2f, -0.4f), Role = Loc.VehSeatGunner, Armed = true, Turret = true,
                    Primary = Plasma(24f, 0.2f, 76f),
                    Alt = Beam(45f, 0.9f, 180f, zoom: 30f),
                },
            ],
        };

        All[(int)VehicleKind.IonTank] = new VehicleDef
        {
            Kind = VehicleKind.IonTank, Name = Loc.VehIonTank, Motion = VehicleMotion.Wheeled,
            Health = 1600f, MaxSpeed = 12f, Acceleration = 12f, TurnRate = 0.9f,
            HalfExtents = new Vector3(2.1f, 1.4f, 3.6f), Tint = new Vector3(0.40f, 0.46f, 0.54f),
            Seats =
            [
                new VehicleSeatDef
                {
                    Offset = new Vector3(0f, 2.0f, 0.2f), Role = Loc.VehSeatDriver, Armed = true, Turret = true,
                    Primary = Cannon(320f, 4.0f, 180f, 12f),
                    Alt = Cannon(320f, 4.0f, 180f, 12f),
                },
            ],
        };

        // ---------------------------------------------------------------- Necris

        All[(int)VehicleKind.Viper] = new VehicleDef
        {
            Kind = VehicleKind.Viper, Name = Loc.VehViper, Motion = VehicleMotion.Hover,
            Health = 300f, MaxSpeed = 36f, Acceleration = 46f, TurnRate = 3.1f, HoverHeight = 1.8f,
            HalfExtents = new Vector3(1.2f, 0.6f, 2.2f), Tint = new Vector3(0.30f, 0.34f, 0.40f),
            CanSelfDestruct = true,
            Seats =
            [
                new VehicleSeatDef
                {
                    Offset = new Vector3(0f, 0.7f, 0f), Role = Loc.VehSeatPilot, Armed = true,
                    Primary = Plasma(16f, 0.14f, 80f),
                    Alt = new FireDef { Mode = FireMode.Melee, Damage = 300f, Interval = 1f, Range = 6f, SplashRadius = 7f, SplashDamage = 220f },
                },
            ],
        };

        All[(int)VehicleKind.Scavenger] = new VehicleDef
        {
            Kind = VehicleKind.Scavenger, Name = Loc.VehScavenger, Motion = VehicleMotion.Walker,
            Health = 600f, MaxSpeed = 24f, Acceleration = 28f, TurnRate = 2.4f,
            HalfExtents = new Vector3(1.6f, 1.6f, 1.6f), Tint = new Vector3(0.34f, 0.32f, 0.38f),
            Seats =
            [
                new VehicleSeatDef
                {
                    Offset = new Vector3(0f, 1.6f, 0f), Role = Loc.VehSeatPilot, Armed = true,
                    Primary = Beam(26f, 0.25f, 26f),
                    // Curl into a ball and roll through whatever is in the way.
                    Alt = new FireDef { Mode = FireMode.Melee, Damage = 160f, Interval = 0.8f, Range = 4.5f, Knockback = 18f },
                },
            ],
        };

        All[(int)VehicleKind.Nemesis] = new VehicleDef
        {
            Kind = VehicleKind.Nemesis, Name = Loc.VehNemesis, Motion = VehicleMotion.Walker,
            Health = 1200f, MaxSpeed = 15f, Acceleration = 16f, TurnRate = 1.2f,
            HalfExtents = new Vector3(1.8f, 1.6f, 2.6f), Tint = new Vector3(0.32f, 0.36f, 0.42f),
            Seats =
            [
                new VehicleSeatDef
                {
                    Offset = new Vector3(0f, 2.2f, 0f), Role = Loc.VehSeatDriver, Armed = true, Turret = true,
                    Primary = Plasma(60f, 0.8f, 74f),
                    // Stance change: rise for reach, hunker for protection.
                    Alt = new FireDef { Mode = FireMode.Melee, Damage = 0f, Interval = 0.8f, Range = 0f },
                },
            ],
        };

        All[(int)VehicleKind.Nightshade] = new VehicleDef
        {
            Kind = VehicleKind.Nightshade, Name = Loc.VehNightshade, Motion = VehicleMotion.Hover,
            Health = 600f, MaxSpeed = 24f, Acceleration = 26f, TurnRate = 2.2f, HoverHeight = 1.6f,
            HalfExtents = new Vector3(1.6f, 0.8f, 2.6f), Tint = new Vector3(0.28f, 0.30f, 0.36f),
            CanCloak = true,
            Seats =
            [
                new VehicleSeatDef
                {
                    Offset = new Vector3(0f, 0.9f, 0f), Role = Loc.VehSeatDriver, Armed = true,
                    // Deployables rather than a gun: mines laid where the enemy has to come through.
                    Primary = new FireDef
                    {
                        Mode = FireMode.Projectile, Projectile = ProjectileKind.BioGlob, Damage = 70f,
                        Interval = 1.2f, Shots = 1, ProjectileSpeed = 26f, SplashRadius = 5f,
                        SplashDamage = 60f,
                    },
                    Alt = new FireDef { Mode = FireMode.Melee, Damage = 0f, Interval = 1f, Range = 0f },
                },
            ],
        };

        All[(int)VehicleKind.Fury] = new VehicleDef
        {
            Kind = VehicleKind.Fury, Name = Loc.VehFury, Motion = VehicleMotion.Air,
            Health = 450f, MaxSpeed = 34f, Acceleration = 24f, TurnRate = 1.9f, HoverHeight = 15f,
            HalfExtents = new Vector3(2.0f, 0.8f, 2.4f), Tint = new Vector3(0.30f, 0.33f, 0.40f),
            Seats =
            [
                new VehicleSeatDef
                {
                    Offset = new Vector3(0f, 0.8f, 0f), Role = Loc.VehSeatPilot, Armed = true,
                    Primary = Beam(30f, 0.3f, 150f),
                    Alt = Beam(30f, 0.3f, 150f),
                },
            ],
        };

        All[(int)VehicleKind.Darkwalker] = new VehicleDef
        {
            Kind = VehicleKind.Darkwalker, Name = Loc.VehDarkwalker, Motion = VehicleMotion.Walker,
            Health = 2200f, MaxSpeed = 13f, Acceleration = 12f, TurnRate = 0.9f,
            HalfExtents = new Vector3(2.6f, 4.4f, 2.6f), Tint = new Vector3(0.26f, 0.28f, 0.34f),
            Seats =
            [
                new VehicleSeatDef
                {
                    Offset = new Vector3(0f, 5.2f, 0f), Role = Loc.VehSeatDriver, Armed = true, Turret = true,
                    // The sweeping twin beam that disintegrates whatever it crosses.
                    Primary = Beam(70f, 0.12f, 120f),
                    Alt = new FireDef { Mode = FireMode.Melee, Damage = 200f, Interval = 1.6f, Range = 6f, Knockback = 22f, SplashRadius = 6f, SplashDamage = 120f },
                },
                new VehicleSeatDef
                {
                    Offset = new Vector3(0f, 4.4f, 1.2f), Role = Loc.VehSeatGunner, Armed = true, Turret = true,
                    Primary = Plasma(26f, 0.2f, 74f),
                    Alt = Plasma(26f, 0.2f, 74f),
                },
            ],
        };

        All[(int)VehicleKind.Hoverboard] = new VehicleDef
        {
            Kind = VehicleKind.Hoverboard, Name = Loc.VehHoverboard, Motion = VehicleMotion.Hover,
            Health = 1f, MaxSpeed = 30f, Acceleration = 34f, TurnRate = 3.0f, HoverHeight = 0.9f,
            HalfExtents = new Vector3(0.5f, 0.3f, 1.3f), Tint = new Vector3(0.55f, 0.58f, 0.64f),
            Crushes = false,
            Seats =
            [
                // Unarmed by design: it is transport, and riding it means giving up your guns.
                new VehicleSeatDef { Offset = new Vector3(0f, 0.5f, 0f), Role = Loc.VehSeatRider, Armed = false },
            ],
        };
    }
}
