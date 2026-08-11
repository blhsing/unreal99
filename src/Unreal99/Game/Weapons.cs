using System.Numerics;
using Silk.NET.OpenGL;
using Unreal99.Core;
using Unreal99.Rendering;

namespace Unreal99.Game;

public enum FireMode
{
    Hitscan, Projectile, Melee, Beam, Burst,
    /// <summary>
    /// Holds a lock on a vehicle before it will fire, then guides the missile in. The AVRiL's
    /// whole identity — an infantry weapon that beats armour but cannot touch a person.
    /// </summary>
    LockOn,
    /// <summary>Paints a spot and calls something down on it a few seconds later.</summary>
    Painter,
    /// <summary>Sets off whatever this weapon has already left lying about.</summary>
    Detonate,
    /// <summary>Recalls the owner to their deployed disc.</summary>
    Recall,
    /// <summary>Raises a directional shield instead of shooting.</summary>
    Shield,
}

public enum ProjectileKind
{
    None = 0, Rocket, Grenade, FlakShell, FlakShard, BioGlob, ShockBall,
    PlasmaBolt, RipperBlade, Warhead,
    // --- UT2004 / UT3 ---
    /// <summary>Assault Rifle's underslung M355 round.</summary>
    RifleGrenade,
    /// <summary>Sticks where it lands and waits for the thrower to set it off.</summary>
    StickyGrenade,
    /// <summary>Spider mine: crawls at whatever comes near, then detonates on contact.</summary>
    SpiderMine,
    /// <summary>Locks on and turns after its target.</summary>
    SeekerMissile,
    /// <summary>Stinger shard. Pins a body to the wall behind it on a solid hit.</summary>
    Shard,
    /// <summary>Translocator disc. Harmless until you recall yourself into it.</summary>
    TranslocatorDisc,
    /// <summary>Bombing Run ball, in flight between hands or towards the hoop.</summary>
    Ball,
}

/// <summary>Static tuning for one weapon's primary or alternate fire.</summary>
public struct FireDef
{
    public FireMode Mode;
    public ProjectileKind Projectile;
    public float Damage;
    public float Interval;          // seconds between shots
    public float Spread;            // cone half-angle, radians
    public int Shots;               // pellets per trigger pull
    public int AmmoCost;
    public float Range;             // hitscan only
    public float ProjectileSpeed;
    public float SplashRadius;
    public float SplashDamage;
    public float Knockback;
    public float SelfKnockback;
    public float Recoil;            // radians of upward camera kick
    public float ShakeAmount;
    public bool Automatic;
    public bool Chargeable;
    public float MaxCharge;
    public float ZoomFov;           // 0 = no zoom
    public float HeadshotMultiplier;
}

public sealed class WeaponDef
{
    public WeaponKind Kind;
    public string Name = "";
    public AmmoKind Ammo;
    public int MaxAmmo;
    public int PickupAmmo;
    public int StartingAmmo;
    public FireDef Primary;
    public FireDef Alt;
    public float SwitchTime = 0.32f;
    public Vector3 Tint = Vector3.One;         // muzzle flash / projectile colour
    public float MuzzleLightRadius = 7f;
    public float MuzzleLightIntensity = 6f;
    /// <summary>Higher is preferred by bots when several weapons have ammo.</summary>
    public float BotPreference = 1f;
    /// <summary>Bots avoid firing this at close range (splash) or long range (spread).</summary>
    public float IdealRangeMin;
    public float IdealRangeMax = 100f;
    /// <summary>View-model placement in camera space (+X right, +Y up, -Z forward).</summary>
    public Vector3 FpOffset = new(0.170f, -0.240f, -0.42f);
    /// <summary>View models are scaled down; at arm's length a 1:1 model swallows the screen.</summary>
    public float FpScale = 0.82f;
    public Vector3 MuzzleLocal = new(0, 0, -0.6f);
    public bool SpinUp;
}

public static class Weapons
{
    public static readonly WeaponDef[] All = new WeaponDef[(int)WeaponKind.Count];

    static Weapons()
    {
        All[(int)WeaponKind.ImpactHammer] = new WeaponDef
        {
            Kind = WeaponKind.ImpactHammer,
            Name = GameTypes.WeaponName(WeaponKind.ImpactHammer),
            Ammo = AmmoKind.None,
            MaxAmmo = 0,
            Tint = new Vector3(0.6f, 0.8f, 1f),
            BotPreference = 0.15f,
            IdealRangeMax = 3.2f,
            SwitchTime = 0.22f,
            FpOffset = new Vector3(0.185f, -0.260f, -0.38f), FpScale = 0.86f,
            // The head, not a muzzle: this is where the swing originates from. Kept in step with
            // the model, which now ends at -0.379 rather than the old -0.53.
            MuzzleLocal = new Vector3(0, 0.045f, -0.365f),
            Primary = new FireDef
            {
                Mode = FireMode.Melee, Damage = 42f, Interval = 0.85f, Range = 3.0f, Shots = 1,
                Knockback = 16f, Chargeable = true, MaxCharge = 1.1f, ShakeAmount = 0.5f, Recoil = 0.05f,
            },
            Alt = new FireDef
            {
                Mode = FireMode.Melee, Damage = 22f, Interval = 0.42f, Range = 2.6f, Shots = 1,
                Knockback = 9f, SelfKnockback = 11f, ShakeAmount = 0.3f,
            },
        };

        All[(int)WeaponKind.Enforcer] = new WeaponDef
        {
            Kind = WeaponKind.Enforcer,
            Name = GameTypes.WeaponName(WeaponKind.Enforcer),
            Ammo = AmmoKind.Bullets,
            MaxAmmo = 99, PickupAmmo = 25, StartingAmmo = 40,
            Tint = new Vector3(1f, 0.86f, 0.55f),
            BotPreference = 0.4f,
            IdealRangeMax = 32f,
            MuzzleLocal = new Vector3(0, 0.070f, -0.285f),
            FpOffset = new Vector3(0.165f, -0.225f, -0.36f), FpScale = 0.86f,
            Primary = new FireDef
            {
                Mode = FireMode.Hitscan, Damage = 19f, Interval = 0.17f, Spread = 0.008f, Shots = 1,
                AmmoCost = 1, Range = 140f, Knockback = 2.5f, Recoil = 0.016f, ShakeAmount = 0.14f,
                Automatic = true, HeadshotMultiplier = 1.6f,
            },
            Alt = new FireDef
            {
                Mode = FireMode.Hitscan, Damage = 17f, Interval = 0.10f, Spread = 0.034f, Shots = 1,
                AmmoCost = 1, Range = 140f, Knockback = 2.0f, Recoil = 0.022f, ShakeAmount = 0.16f,
                Automatic = true, HeadshotMultiplier = 1.4f,
            },
        };

        All[(int)WeaponKind.BioRifle] = new WeaponDef
        {
            Kind = WeaponKind.BioRifle,
            Name = GameTypes.WeaponName(WeaponKind.BioRifle),
            Ammo = AmmoKind.BioSludge,
            MaxAmmo = 100, PickupAmmo = 25,
            Tint = new Vector3(0.45f, 1f, 0.25f),
            BotPreference = 0.75f,
            IdealRangeMin = 2f, IdealRangeMax = 26f,
            MuzzleLocal = new Vector3(0, 0.050f, -0.580f),
            Primary = new FireDef
            {
                Mode = FireMode.Projectile, Projectile = ProjectileKind.BioGlob, Damage = 22f,
                Interval = 0.36f, Spread = 0.02f, Shots = 1, AmmoCost = 1, ProjectileSpeed = 26f,
                SplashRadius = 2.6f, SplashDamage = 30f, Knockback = 6f, Recoil = 0.02f,
                ShakeAmount = 0.2f, Automatic = true,
            },
            Alt = new FireDef
            {
                Mode = FireMode.Projectile, Projectile = ProjectileKind.BioGlob, Damage = 45f,
                Interval = 0.75f, Shots = 1, AmmoCost = 5, ProjectileSpeed = 21f,
                SplashRadius = 4.6f, SplashDamage = 72f, Knockback = 11f, Chargeable = true,
                MaxCharge = 1.5f, Recoil = 0.045f, ShakeAmount = 0.45f,
            },
        };

        All[(int)WeaponKind.ShockRifle] = new WeaponDef
        {
            Kind = WeaponKind.ShockRifle,
            Name = GameTypes.WeaponName(WeaponKind.ShockRifle),
            Ammo = AmmoKind.ShockCore,
            MaxAmmo = 90, PickupAmmo = 20,
            Tint = new Vector3(0.55f, 0.35f, 1f),
            BotPreference = 1.25f,
            IdealRangeMax = 90f,
            MuzzleLocal = new Vector3(0, 0.050f, -0.640f),
            Primary = new FireDef
            {
                Mode = FireMode.Hitscan, Damage = 42f, Interval = 0.65f, Spread = 0f, Shots = 1,
                AmmoCost = 1, Range = 200f, Knockback = 8f, Recoil = 0.035f, ShakeAmount = 0.35f,
            },
            Alt = new FireDef
            {
                Mode = FireMode.Projectile, Projectile = ProjectileKind.ShockBall, Damage = 33f,
                Interval = 0.62f, Shots = 1, AmmoCost = 1, ProjectileSpeed = 22f,
                SplashRadius = 3.4f, SplashDamage = 40f, Knockback = 9f, Recoil = 0.02f,
                ShakeAmount = 0.2f,
            },
        };

        All[(int)WeaponKind.PulseGun] = new WeaponDef
        {
            Kind = WeaponKind.PulseGun,
            Name = GameTypes.WeaponName(WeaponKind.PulseGun),
            Ammo = AmmoKind.PulseCells,
            MaxAmmo = 199, PickupAmmo = 40,
            Tint = new Vector3(0.35f, 1f, 0.55f),
            BotPreference = 0.95f,
            IdealRangeMax = 40f,
            MuzzleLocal = new Vector3(0, 0.050f, -0.575f),
            Primary = new FireDef
            {
                Mode = FireMode.Projectile, Projectile = ProjectileKind.PlasmaBolt, Damage = 17f,
                Interval = 0.11f, Spread = 0.012f, Shots = 1, AmmoCost = 1, ProjectileSpeed = 52f,
                SplashRadius = 1.1f, SplashDamage = 8f, Knockback = 3f, Recoil = 0.010f,
                ShakeAmount = 0.10f, Automatic = true,
            },
            Alt = new FireDef
            {
                Mode = FireMode.Beam, Damage = 118f, Interval = 0.05f, Shots = 1, AmmoCost = 1,
                Range = 26f, Knockback = 2.5f, ShakeAmount = 0.22f, Automatic = true,
            },
        };

        All[(int)WeaponKind.Ripper] = new WeaponDef
        {
            Kind = WeaponKind.Ripper,
            Name = GameTypes.WeaponName(WeaponKind.Ripper),
            Ammo = AmmoKind.Blades,
            MaxAmmo = 100, PickupAmmo = 25,
            Tint = new Vector3(0.85f, 0.95f, 1f),
            BotPreference = 0.9f,
            IdealRangeMax = 45f,
            MuzzleLocal = new Vector3(0, 0.046f, -0.480f),
            Primary = new FireDef
            {
                Mode = FireMode.Projectile, Projectile = ProjectileKind.RipperBlade, Damage = 34f,
                Interval = 0.42f, Spread = 0.006f, Shots = 1, AmmoCost = 1, ProjectileSpeed = 48f,
                Knockback = 5f, Recoil = 0.018f, ShakeAmount = 0.16f, Automatic = true,
                HeadshotMultiplier = 3.0f,
            },
            Alt = new FireDef
            {
                Mode = FireMode.Projectile, Projectile = ProjectileKind.RipperBlade, Damage = 26f,
                Interval = 0.55f, Shots = 1, AmmoCost = 2, ProjectileSpeed = 40f,
                SplashRadius = 3.0f, SplashDamage = 38f, Knockback = 9f, Recoil = 0.028f,
                ShakeAmount = 0.28f,
            },
        };

        All[(int)WeaponKind.Minigun] = new WeaponDef
        {
            Kind = WeaponKind.Minigun,
            Name = GameTypes.WeaponName(WeaponKind.Minigun),
            Ammo = AmmoKind.MinigunBullets,
            MaxAmmo = 299, PickupAmmo = 50,
            Tint = new Vector3(1f, 0.82f, 0.45f),
            BotPreference = 1.1f,
            IdealRangeMax = 55f,
            SpinUp = true,
            MuzzleLocal = new Vector3(0, 0.050f, -0.730f),
            FpOffset = new Vector3(0.190f, -0.255f, -0.52f), FpScale = 0.76f,
            Primary = new FireDef
            {
                Mode = FireMode.Hitscan, Damage = 11f, Interval = 0.085f, Spread = 0.020f, Shots = 1,
                AmmoCost = 1, Range = 150f, Knockback = 1.4f, Recoil = 0.009f, ShakeAmount = 0.10f,
                Automatic = true, HeadshotMultiplier = 1.5f,
            },
            Alt = new FireDef
            {
                Mode = FireMode.Hitscan, Damage = 9f, Interval = 0.048f, Spread = 0.045f, Shots = 1,
                AmmoCost = 1, Range = 150f, Knockback = 1.1f, Recoil = 0.011f, ShakeAmount = 0.13f,
                Automatic = true, HeadshotMultiplier = 1.3f,
            },
        };

        All[(int)WeaponKind.FlakCannon] = new WeaponDef
        {
            Kind = WeaponKind.FlakCannon,
            Name = GameTypes.WeaponName(WeaponKind.FlakCannon),
            Ammo = AmmoKind.FlakShells,
            MaxAmmo = 50, PickupAmmo = 10,
            Tint = new Vector3(1f, 0.55f, 0.15f),
            BotPreference = 1.35f,
            IdealRangeMax = 20f,
            MuzzleLocal = new Vector3(0, 0.055f, -0.595f),
            FpOffset = new Vector3(0.180f, -0.250f, -0.48f), FpScale = 0.78f,
            Primary = new FireDef
            {
                Mode = FireMode.Projectile, Projectile = ProjectileKind.FlakShard, Damage = 17f,
                Interval = 0.92f, Spread = 0.13f, Shots = 9, AmmoCost = 1, ProjectileSpeed = 58f,
                Knockback = 3f, Recoil = 0.055f, ShakeAmount = 0.5f,
            },
            Alt = new FireDef
            {
                Mode = FireMode.Projectile, Projectile = ProjectileKind.FlakShell, Damage = 55f,
                Interval = 1.05f, Shots = 1, AmmoCost = 1, ProjectileSpeed = 30f,
                SplashRadius = 5.0f, SplashDamage = 78f, Knockback = 15f, SelfKnockback = 9f,
                Recoil = 0.05f, ShakeAmount = 0.45f,
            },
        };

        All[(int)WeaponKind.RocketLauncher] = new WeaponDef
        {
            Kind = WeaponKind.RocketLauncher,
            Name = GameTypes.WeaponName(WeaponKind.RocketLauncher),
            Ammo = AmmoKind.Rockets,
            MaxAmmo = 48, PickupAmmo = 12,
            Tint = new Vector3(1f, 0.6f, 0.2f),
            BotPreference = 1.6f,
            IdealRangeMin = 5f, IdealRangeMax = 60f,
            MuzzleLocal = new Vector3(0, 0.062f, -0.655f),
            FpOffset = new Vector3(0.190f, -0.255f, -0.50f), FpScale = 0.76f,
            Primary = new FireDef
            {
                Mode = FireMode.Projectile, Projectile = ProjectileKind.Rocket, Damage = 78f,
                Interval = 0.95f, Spread = 0.004f, Shots = 1, AmmoCost = 1, ProjectileSpeed = 34f,
                SplashRadius = 5.6f, SplashDamage = 88f, Knockback = 20f, SelfKnockback = 14f,
                Recoil = 0.05f, ShakeAmount = 0.55f,
            },
            Alt = new FireDef
            {
                Mode = FireMode.Projectile, Projectile = ProjectileKind.Grenade, Damage = 62f,
                Interval = 0.72f, Spread = 0.02f, Shots = 1, AmmoCost = 1, ProjectileSpeed = 24f,
                SplashRadius = 5.0f, SplashDamage = 74f, Knockback = 17f, SelfKnockback = 12f,
                Recoil = 0.035f, ShakeAmount = 0.4f,
            },
        };

        All[(int)WeaponKind.SniperRifle] = new WeaponDef
        {
            Kind = WeaponKind.SniperRifle,
            Name = GameTypes.WeaponName(WeaponKind.SniperRifle),
            Ammo = AmmoKind.SniperRounds,
            MaxAmmo = 50, PickupAmmo = 10,
            Tint = new Vector3(0.8f, 0.9f, 1f),
            BotPreference = 1.2f,
            IdealRangeMin = 12f, IdealRangeMax = 200f,
            MuzzleLocal = new Vector3(0, 0.048f, -0.785f),
            FpOffset = new Vector3(0.165f, -0.220f, -0.56f), FpScale = 0.72f,
            Primary = new FireDef
            {
                Mode = FireMode.Hitscan, Damage = 52f, Interval = 1.35f, Spread = 0f, Shots = 1,
                AmmoCost = 1, Range = 300f, Knockback = 6f, Recoil = 0.07f, ShakeAmount = 0.4f,
                HeadshotMultiplier = 2.6f,
            },
            Alt = new FireDef
            {
                Mode = FireMode.Hitscan, Damage = 52f, Interval = 1.35f, Spread = 0f, Shots = 1,
                AmmoCost = 1, Range = 300f, Knockback = 6f, Recoil = 0.07f, ShakeAmount = 0.4f,
                ZoomFov = 24f, HeadshotMultiplier = 2.6f,
            },
        };

        All[(int)WeaponKind.Redeemer] = new WeaponDef
        {
            Kind = WeaponKind.Redeemer,
            Name = GameTypes.WeaponName(WeaponKind.Redeemer),
            Ammo = AmmoKind.Warhead,
            MaxAmmo = 2, PickupAmmo = 1,
            Tint = new Vector3(1f, 0.75f, 0.3f),
            BotPreference = 3.0f,
            IdealRangeMin = 14f, IdealRangeMax = 150f,
            SwitchTime = 0.7f,
            MuzzleLocal = new Vector3(0, 0.060f, -0.870f),
            FpOffset = new Vector3(0.205f, -0.275f, -0.62f), FpScale = 0.68f,
            Primary = new FireDef
            {
                Mode = FireMode.Projectile, Projectile = ProjectileKind.Warhead, Damage = 260f,
                Interval = 2.2f, Shots = 1, AmmoCost = 1, ProjectileSpeed = 22f,
                SplashRadius = 22f, SplashDamage = 320f, Knockback = 55f, SelfKnockback = 30f,
                Recoil = 0.09f, ShakeAmount = 1.0f,
            },
            Alt = new FireDef
            {
                Mode = FireMode.Projectile, Projectile = ProjectileKind.Warhead, Damage = 260f,
                Interval = 2.2f, Shots = 1, AmmoCost = 1, ProjectileSpeed = 15f,
                SplashRadius = 27f, SplashDamage = 360f, Knockback = 62f, SelfKnockback = 34f,
                Recoil = 0.09f, ShakeAmount = 1.0f,
            },
        };

        // ================================================================ UT2004

        // Slot 1. UT2004's answer to the Impact Hammer: the same close-range wallop, but the alt
        // raises a shield that eats incoming fire instead of shoving you around.
        All[(int)WeaponKind.ShieldGun] = new WeaponDef
        {
            Kind = WeaponKind.ShieldGun,
            Name = GameTypes.WeaponName(WeaponKind.ShieldGun),
            Ammo = AmmoKind.None,
            MaxAmmo = 0,
            Tint = new Vector3(0.55f, 0.85f, 1f),
            BotPreference = 0.2f,
            IdealRangeMax = 3.4f,
            SwitchTime = 0.24f,
            MuzzleLocal = new Vector3(0, 0.05f, -0.34f),
            FpOffset = new Vector3(0.180f, -0.250f, -0.38f), FpScale = 0.86f,
            Primary = new FireDef
            {
                Mode = FireMode.Melee, Damage = 46f, Interval = 0.9f, Range = 3.2f, Shots = 1,
                Knockback = 18f, Chargeable = true, MaxCharge = 1.2f, ShakeAmount = 0.5f, Recoil = 0.05f,
            },
            Alt = new FireDef
            {
                Mode = FireMode.Shield, Damage = 0f, Interval = 0.1f, Range = 3.0f, Shots = 0,
            },
        };

        // Slot 2. UT2004's starting rifle: weak bullets, and an underslung grenade that is the
        // reason anyone keeps it past the first pickup.
        All[(int)WeaponKind.AssaultRifle] = new WeaponDef
        {
            Kind = WeaponKind.AssaultRifle,
            Name = GameTypes.WeaponName(WeaponKind.AssaultRifle),
            Ammo = AmmoKind.RifleRounds,
            MaxAmmo = 200, PickupAmmo = 50, StartingAmmo = 60,
            Tint = new Vector3(1f, 0.9f, 0.6f),
            BotPreference = 0.45f,
            IdealRangeMax = 40f,
            MuzzleLocal = new Vector3(0, 0.062f, -0.40f),
            FpOffset = new Vector3(0.170f, -0.235f, -0.40f), FpScale = 0.84f,
            Primary = new FireDef
            {
                Mode = FireMode.Hitscan, Damage = 8f, Interval = 0.09f, Spread = 0.028f, Shots = 1,
                AmmoCost = 1, Range = 130f, Knockback = 1.2f, Recoil = 0.010f, ShakeAmount = 0.10f,
                Automatic = true, HeadshotMultiplier = 1.5f,
            },
            Alt = new FireDef
            {
                Mode = FireMode.Projectile, Projectile = ProjectileKind.RifleGrenade, Damage = 48f,
                Interval = 1.0f, Spread = 0.006f, Shots = 1, AmmoCost = 1, ProjectileSpeed = 26f,
                SplashRadius = 4.4f, SplashDamage = 56f, Knockback = 13f, SelfKnockback = 10f,
                Chargeable = true, MaxCharge = 1.4f, Recoil = 0.03f, ShakeAmount = 0.32f,
            },
        };

        // Slot 5. The Pulse Gun's successor. Primary is plasma bolts rather than a stream, and the
        // beam is a proper tool: it hurts enemies, repairs vehicles, builds nodes and — the part
        // that gives it its name — boosts a team-mate who is beaming the same target.
        All[(int)WeaponKind.LinkGun] = new WeaponDef
        {
            Kind = WeaponKind.LinkGun,
            Name = GameTypes.WeaponName(WeaponKind.LinkGun),
            Ammo = AmmoKind.LinkCells,
            MaxAmmo = 220, PickupAmmo = 55,
            Tint = new Vector3(0.35f, 1f, 0.55f),
            BotPreference = 1.1f,
            IdealRangeMax = 45f,
            MuzzleLocal = new Vector3(0, 0.052f, -0.60f),
            FpOffset = new Vector3(0.175f, -0.240f, -0.44f), FpScale = 0.80f,
            Primary = new FireDef
            {
                Mode = FireMode.Projectile, Projectile = ProjectileKind.PlasmaBolt, Damage = 26f,
                Interval = 0.18f, Spread = 0.004f, Shots = 1, AmmoCost = 1, ProjectileSpeed = 58f,
                Knockback = 3.4f, Recoil = 0.014f, ShakeAmount = 0.12f, Automatic = true,
            },
            Alt = new FireDef
            {
                Mode = FireMode.Beam, Damage = 62f, Interval = 0.1f, Shots = 1, AmmoCost = 1,
                Range = 26f, Knockback = 1.6f, ShakeAmount = 0.08f, Automatic = true,
            },
        };

        // Slot 9. UT2004 shipped this in the Sniper Rifle's place: same job, no bullet drop to
        // speak of, and a bolt of lightning instead of a report.
        All[(int)WeaponKind.LightningGun] = new WeaponDef
        {
            Kind = WeaponKind.LightningGun,
            Name = GameTypes.WeaponName(WeaponKind.LightningGun),
            Ammo = AmmoKind.LightningCells,
            MaxAmmo = 40, PickupAmmo = 10,
            Tint = new Vector3(0.7f, 0.85f, 1f),
            BotPreference = 1.25f,
            IdealRangeMin = 14f, IdealRangeMax = 220f,
            MuzzleLocal = new Vector3(0, 0.05f, -0.80f),
            FpOffset = new Vector3(0.165f, -0.220f, -0.56f), FpScale = 0.72f,
            Primary = new FireDef
            {
                Mode = FireMode.Hitscan, Damage = 58f, Interval = 1.45f, Spread = 0f, Shots = 1,
                AmmoCost = 1, Range = 320f, Knockback = 6f, Recoil = 0.075f, ShakeAmount = 0.45f,
                HeadshotMultiplier = 2.8f,
            },
            Alt = new FireDef
            {
                Mode = FireMode.Hitscan, Damage = 58f, Interval = 1.45f, Spread = 0f, Shots = 1,
                AmmoCost = 1, Range = 320f, Knockback = 6f, Recoil = 0.075f, ShakeAmount = 0.45f,
                ZoomFov = 22f, HeadshotMultiplier = 2.8f,
            },
        };

        // Slot 3. Four drones a clip. They sit still until something comes near, then run it down.
        // Alt paints a spot and the whole flock goes for it.
        All[(int)WeaponKind.MineLayer] = new WeaponDef
        {
            Kind = WeaponKind.MineLayer,
            Name = GameTypes.WeaponName(WeaponKind.MineLayer),
            Ammo = AmmoKind.Mines,
            MaxAmmo = 24, PickupAmmo = 8,
            Tint = new Vector3(0.9f, 0.5f, 0.25f),
            BotPreference = 0.9f,
            IdealRangeMin = 4f, IdealRangeMax = 50f,
            MuzzleLocal = new Vector3(0, 0.05f, -0.46f),
            FpOffset = new Vector3(0.180f, -0.245f, -0.44f), FpScale = 0.80f,
            Primary = new FireDef
            {
                Mode = FireMode.Projectile, Projectile = ProjectileKind.SpiderMine, Damage = 40f,
                Interval = 0.55f, Spread = 0.01f, Shots = 1, AmmoCost = 1, ProjectileSpeed = 18f,
                SplashRadius = 4.2f, SplashDamage = 52f, Knockback = 10f, SelfKnockback = 6f,
                Recoil = 0.02f, ShakeAmount = 0.2f,
            },
            Alt = new FireDef
            {
                Mode = FireMode.Painter, Damage = 0f, Interval = 0.2f, Shots = 0, Range = 90f,
            },
        };

        // Slot 7. Eight grenades in the air at once, all of them waiting for you to press the
        // button. Nothing else in the game lets a defender prepare a room like this.
        All[(int)WeaponKind.GrenadeLauncher] = new WeaponDef
        {
            Kind = WeaponKind.GrenadeLauncher,
            Name = GameTypes.WeaponName(WeaponKind.GrenadeLauncher),
            Ammo = AmmoKind.Grenades,
            MaxAmmo = 50, PickupAmmo = 12,
            Tint = new Vector3(0.85f, 0.75f, 0.35f),
            BotPreference = 1.3f,
            IdealRangeMin = 6f, IdealRangeMax = 55f,
            MuzzleLocal = new Vector3(0, 0.058f, -0.52f),
            FpOffset = new Vector3(0.185f, -0.250f, -0.46f), FpScale = 0.78f,
            Primary = new FireDef
            {
                Mode = FireMode.Projectile, Projectile = ProjectileKind.StickyGrenade, Damage = 44f,
                Interval = 0.35f, Spread = 0.012f, Shots = 1, AmmoCost = 1, ProjectileSpeed = 28f,
                SplashRadius = 5.2f, SplashDamage = 62f, Knockback = 15f, SelfKnockback = 11f,
                Recoil = 0.028f, ShakeAmount = 0.3f, Automatic = true,
            },
            Alt = new FireDef
            {
                Mode = FireMode.Detonate, Damage = 0f, Interval = 0.35f, Shots = 0,
            },
        };

        // Slot 8/10. Locks on to armour and cannot lock on to a person at all, which is the entire
        // reason infantry are not simply roadkill on the vehicle maps.
        All[(int)WeaponKind.Avril] = new WeaponDef
        {
            Kind = WeaponKind.Avril,
            Name = GameTypes.WeaponName(WeaponKind.Avril),
            Ammo = AmmoKind.AvrilMissiles,
            MaxAmmo = 12, PickupAmmo = 4,
            Tint = new Vector3(1f, 0.55f, 0.35f),
            BotPreference = 1.4f,
            IdealRangeMin = 10f, IdealRangeMax = 250f,
            SwitchTime = 0.45f,
            MuzzleLocal = new Vector3(0, 0.06f, -0.74f),
            FpOffset = new Vector3(0.195f, -0.255f, -0.54f), FpScale = 0.74f,
            Primary = new FireDef
            {
                Mode = FireMode.LockOn, Projectile = ProjectileKind.SeekerMissile, Damage = 130f,
                Interval = 2.6f, Shots = 1, AmmoCost = 1, ProjectileSpeed = 40f, Range = 280f,
                SplashRadius = 6.5f, SplashDamage = 90f, Knockback = 22f, SelfKnockback = 14f,
                Recoil = 0.06f, ShakeAmount = 0.6f,
            },
            Alt = new FireDef
            {
                Mode = FireMode.LockOn, Projectile = ProjectileKind.SeekerMissile, Damage = 130f,
                Interval = 2.6f, Shots = 1, AmmoCost = 1, ProjectileSpeed = 40f, Range = 280f,
                SplashRadius = 6.5f, SplashDamage = 90f, Knockback = 22f, SelfKnockback = 14f,
                ZoomFov = 40f, Recoil = 0.06f, ShakeAmount = 0.6f,
            },
        };

        // Slot 10. Paint a spot, wait, and an orbital cannon deletes it. One charge, no reloads.
        All[(int)WeaponKind.IonPainter] = new WeaponDef
        {
            Kind = WeaponKind.IonPainter,
            Name = GameTypes.WeaponName(WeaponKind.IonPainter),
            Ammo = AmmoKind.IonCharge,
            MaxAmmo = 2, PickupAmmo = 1,
            Tint = new Vector3(0.6f, 0.9f, 1f),
            BotPreference = 2.8f,
            IdealRangeMin = 25f, IdealRangeMax = 300f,
            SwitchTime = 0.7f,
            MuzzleLocal = new Vector3(0, 0.05f, -0.66f),
            FpOffset = new Vector3(0.175f, -0.235f, -0.52f), FpScale = 0.76f,
            Primary = new FireDef
            {
                Mode = FireMode.Painter, Damage = 320f, Interval = 2.4f, Shots = 1, AmmoCost = 1,
                Range = 300f, SplashRadius = 20f, SplashDamage = 380f, Knockback = 50f,
                ShakeAmount = 1.0f,
            },
            Alt = new FireDef
            {
                Mode = FireMode.Painter, Damage = 320f, Interval = 2.4f, Shots = 1, AmmoCost = 1,
                Range = 300f, SplashRadius = 20f, SplashDamage = 380f, Knockback = 50f,
                ZoomFov = 30f, ShakeAmount = 1.0f,
            },
        };

        // Slot 10. Same idea, but what arrives is a bomber laying a line of ordnance rather than a
        // single beam — wider, messier, and much better against a column of vehicles.
        All[(int)WeaponKind.TargetPainter] = new WeaponDef
        {
            Kind = WeaponKind.TargetPainter,
            Name = GameTypes.WeaponName(WeaponKind.TargetPainter),
            Ammo = AmmoKind.TargetBeacon,
            MaxAmmo = 2, PickupAmmo = 1,
            Tint = new Vector3(1f, 0.7f, 0.35f),
            BotPreference = 2.7f,
            IdealRangeMin = 25f, IdealRangeMax = 300f,
            SwitchTime = 0.7f,
            MuzzleLocal = new Vector3(0, 0.05f, -0.66f),
            FpOffset = new Vector3(0.175f, -0.235f, -0.52f), FpScale = 0.76f,
            Primary = new FireDef
            {
                Mode = FireMode.Painter, Damage = 150f, Interval = 2.4f, Shots = 1, AmmoCost = 1,
                Range = 300f, SplashRadius = 11f, SplashDamage = 190f, Knockback = 34f,
                ShakeAmount = 0.9f,
            },
            Alt = new FireDef
            {
                Mode = FireMode.Painter, Damage = 150f, Interval = 2.4f, Shots = 1, AmmoCost = 1,
                Range = 300f, SplashRadius = 11f, SplashDamage = 190f, Knockback = 34f,
                ZoomFov = 30f, ShakeAmount = 0.9f,
            },
        };

        // Slot 1. Throw the disc, press again to be where it is. Landing on somebody telefrags
        // them, which is the reason it is a weapon and not a movement key.
        All[(int)WeaponKind.Translocator] = new WeaponDef
        {
            Kind = WeaponKind.Translocator,
            Name = GameTypes.WeaponName(WeaponKind.Translocator),
            Ammo = AmmoKind.TranslocatorCharge,
            MaxAmmo = 1, PickupAmmo = 1, StartingAmmo = 1,
            Tint = new Vector3(0.5f, 0.8f, 1f),
            BotPreference = 0.1f,
            IdealRangeMax = 40f,
            SwitchTime = 0.2f,
            MuzzleLocal = new Vector3(0, 0.045f, -0.30f),
            FpOffset = new Vector3(0.170f, -0.240f, -0.34f), FpScale = 0.88f,
            Primary = new FireDef
            {
                Mode = FireMode.Projectile, Projectile = ProjectileKind.TranslocatorDisc, Damage = 0f,
                Interval = 0.5f, Shots = 1, AmmoCost = 0, ProjectileSpeed = 30f,
            },
            Alt = new FireDef { Mode = FireMode.Recall, Damage = 0f, Interval = 0.4f, Shots = 0 },
        };

        // Slot 4. Instagib. One beam, one kill, no ammo worth counting.
        All[(int)WeaponKind.SuperShockRifle] = new WeaponDef
        {
            Kind = WeaponKind.SuperShockRifle,
            Name = GameTypes.WeaponName(WeaponKind.SuperShockRifle),
            Ammo = AmmoKind.ShockCore,
            MaxAmmo = 99, PickupAmmo = 99, StartingAmmo = 99,
            Tint = new Vector3(0.85f, 0.5f, 1f),
            BotPreference = 3.2f,
            IdealRangeMax = 300f,
            MuzzleLocal = new Vector3(0, 0.05f, -0.66f),
            FpOffset = new Vector3(0.175f, -0.235f, -0.48f), FpScale = 0.78f,
            Primary = new FireDef
            {
                Mode = FireMode.Hitscan, Damage = 1000f, Interval = 1.0f, Shots = 1, AmmoCost = 0,
                Range = 320f, Knockback = 30f, Recoil = 0.05f, ShakeAmount = 0.5f,
            },
            Alt = new FireDef
            {
                Mode = FireMode.Hitscan, Damage = 1000f, Interval = 1.0f, Shots = 1, AmmoCost = 0,
                Range = 320f, Knockback = 30f, Recoil = 0.05f, ShakeAmount = 0.5f,
            },
        };

        // ================================================================ UT3

        // Slot 6. UT3 dropped the Minigun for this: tarydium shards instead of bullets, and an alt
        // that throws a heavy crystal hard enough to pin a body to the wall behind it.
        All[(int)WeaponKind.Stinger] = new WeaponDef
        {
            Kind = WeaponKind.Stinger,
            Name = GameTypes.WeaponName(WeaponKind.Stinger),
            Ammo = AmmoKind.Shards,
            MaxAmmo = 300, PickupAmmo = 75,
            Tint = new Vector3(0.55f, 0.85f, 0.95f),
            BotPreference = 1.15f,
            IdealRangeMax = 55f,
            SpinUp = true,
            MuzzleLocal = new Vector3(0, 0.05f, -0.70f),
            FpOffset = new Vector3(0.190f, -0.255f, -0.52f), FpScale = 0.76f,
            Primary = new FireDef
            {
                Mode = FireMode.Projectile, Projectile = ProjectileKind.Shard, Damage = 15f,
                Interval = 0.08f, Spread = 0.020f, Shots = 1, AmmoCost = 1, ProjectileSpeed = 90f,
                Knockback = 1.4f, Recoil = 0.009f, ShakeAmount = 0.10f, Automatic = true,
            },
            Alt = new FireDef
            {
                Mode = FireMode.Projectile, Projectile = ProjectileKind.Shard, Damage = 48f,
                Interval = 0.52f, Spread = 0.004f, Shots = 1, AmmoCost = 3, ProjectileSpeed = 66f,
                Knockback = 22f, Recoil = 0.035f, ShakeAmount = 0.3f,
            },
        };

        // Bombing Run only. It cannot hurt anyone — the primary throws the ball at the hoop or at
        // a team-mate, and that is the whole weapon.
        All[(int)WeaponKind.BallLauncher] = new WeaponDef
        {
            Kind = WeaponKind.BallLauncher,
            Name = GameTypes.WeaponName(WeaponKind.BallLauncher),
            Ammo = AmmoKind.None,
            MaxAmmo = 0,
            Tint = new Vector3(1f, 0.85f, 0.4f),
            BotPreference = 0.05f,
            IdealRangeMax = 60f,
            SwitchTime = 0.15f,
            MuzzleLocal = new Vector3(0, 0.06f, -0.40f),
            FpOffset = new Vector3(0.175f, -0.245f, -0.40f), FpScale = 0.84f,
            Primary = new FireDef
            {
                Mode = FireMode.Projectile, Projectile = ProjectileKind.Ball, Damage = 0f,
                Interval = 0.6f, Shots = 1, AmmoCost = 0, ProjectileSpeed = 34f,
            },
            Alt = new FireDef
            {
                Mode = FireMode.Projectile, Projectile = ProjectileKind.Ball, Damage = 0f,
                Interval = 0.6f, Shots = 1, AmmoCost = 0, ProjectileSpeed = 26f,
            },
        };
    }

    public static WeaponDef Get(WeaponKind k) => All[(int)MathX.Clamp((int)k, 0, (int)WeaponKind.Count - 1)];

    public static AmmoKind AmmoFor(WeaponKind k) => Get(k).Ammo;

    /// <summary>Default loadout every pawn starts with.</summary>
    public static readonly WeaponKind[] StartingWeapons = [WeaponKind.ImpactHammer, WeaponKind.Enforcer];

    /// <summary>
    /// Order used by the next/previous weapon keys, weakest first. New weapons sit beside the
    /// 1999 weapon they share a slot with in their own game, so the ordering still reads as the
    /// familiar 1..10 progression rather than "everything from 1999, then everything else".
    /// </summary>
    public static readonly WeaponKind[] CycleOrder =
    [
        WeaponKind.Translocator, WeaponKind.ImpactHammer, WeaponKind.ShieldGun,
        WeaponKind.Enforcer, WeaponKind.AssaultRifle,
        WeaponKind.BioRifle, WeaponKind.MineLayer,
        WeaponKind.ShockRifle, WeaponKind.PulseGun, WeaponKind.LinkGun,
        WeaponKind.Ripper, WeaponKind.Minigun, WeaponKind.Stinger,
        WeaponKind.FlakCannon, WeaponKind.GrenadeLauncher,
        WeaponKind.RocketLauncher, WeaponKind.Avril,
        WeaponKind.SniperRifle, WeaponKind.LightningGun,
        WeaponKind.TargetPainter, WeaponKind.IonPainter, WeaponKind.Redeemer,
        WeaponKind.SuperShockRifle, WeaponKind.BallLauncher,
    ];

    /// <summary>
    /// Which era's sidearms a map hands out. UT2004's arenas start you with a Shield Gun and an
    /// Assault Rifle, not an Impact Hammer and an Enforcer; UT3 went back to the 1999 pair. A map
    /// that opens with the wrong two weapons feels wrong before anything else happens.
    /// </summary>
    public enum Era { Ut99, Ut2004, Ut3 }

    public static WeaponKind[] StartingWeaponsFor(Era era) => era switch
    {
        Era.Ut2004 => [WeaponKind.ShieldGun, WeaponKind.AssaultRifle, WeaponKind.Translocator],
        Era.Ut3 => [WeaponKind.ImpactHammer, WeaponKind.Enforcer, WeaponKind.Translocator],
        _ => StartingWeapons,
    };
}