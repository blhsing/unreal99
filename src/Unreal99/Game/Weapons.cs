using System.Numerics;
using Silk.NET.OpenGL;
using Unreal99.Core;
using Unreal99.Rendering;

namespace Unreal99.Game;

public enum FireMode { Hitscan, Projectile, Melee, Beam, Burst }

public enum ProjectileKind
{
    None = 0, Rocket, Grenade, FlakShell, FlakShard, BioGlob, ShockBall,
    PlasmaBolt, RipperBlade, Warhead,
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
            MuzzleLocal = new Vector3(0, 0.02f, -0.52f),
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
            MuzzleLocal = new Vector3(0, 0.015f, -0.34f),
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
            MuzzleLocal = new Vector3(0, 0.03f, -0.58f),
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
            MuzzleLocal = new Vector3(0, 0.02f, -0.66f),
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
            MuzzleLocal = new Vector3(0, 0.02f, -0.62f),
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
            MuzzleLocal = new Vector3(0, 0.02f, -0.56f),
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
            MuzzleLocal = new Vector3(0, 0.02f, -0.72f),
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
            MuzzleLocal = new Vector3(0, 0.03f, -0.6f),
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
            MuzzleLocal = new Vector3(0, 0.04f, -0.66f),
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
            MuzzleLocal = new Vector3(0, 0.02f, -0.8f),
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
            MuzzleLocal = new Vector3(0, 0.04f, -0.9f),
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
    }

    public static WeaponDef Get(WeaponKind k) => All[(int)MathX.Clamp((int)k, 0, (int)WeaponKind.Count - 1)];

    public static AmmoKind AmmoFor(WeaponKind k) => Get(k).Ammo;

    /// <summary>Default loadout every pawn starts with.</summary>
    public static readonly WeaponKind[] StartingWeapons = [WeaponKind.ImpactHammer, WeaponKind.Enforcer];

    /// <summary>Order used by the next/previous weapon keys, weakest first.</summary>
    public static readonly WeaponKind[] CycleOrder =
    [
        WeaponKind.ImpactHammer, WeaponKind.Enforcer, WeaponKind.BioRifle, WeaponKind.ShockRifle,
        WeaponKind.PulseGun, WeaponKind.Ripper, WeaponKind.Minigun, WeaponKind.FlakCannon,
        WeaponKind.RocketLauncher, WeaponKind.SniperRifle, WeaponKind.Redeemer,
    ];
}

/// <summary>
/// Procedurally built weapon meshes. Each is modelled in a local frame where -Z is forward,
/// +Y is up and the origin sits at the grip, so the same mesh works for the first-person
/// view and for the third-person model held in the character's right hand.
/// </summary>
public sealed class WeaponModels : IDisposable
{
    private readonly Mesh[] _meshes = new Mesh[(int)WeaponKind.Count];
    private readonly MeshSection[][] _sections = new MeshSection[(int)WeaponKind.Count][];

    public Mesh MeshFor(WeaponKind k) => _meshes[(int)k];
    public MeshSection[] SectionsFor(WeaponKind k) => _sections[(int)k];

    public WeaponModels(GL gl)
    {
        for (int i = 0; i < (int)WeaponKind.Count; i++)
        {
            var mb = new MeshBuilder { WorldUv = false, Material = (int)MatId.WeaponMetal };
            Build((WeaponKind)i, mb);
            mb.RecalculateTangents();
            var (v, ind, s) = mb.Build();
            _meshes[i] = Mesh.CreateStatic<Vertex>(gl, v, ind, VertexLayouts.Static);
            _sections[i] = s;
        }
    }

    private static void Grip(MeshBuilder mb, float length = 0.20f, float z = 0.06f)
    {
        mb.Material = (int)MatId.TechPanelDark;
        mb.AddBox(new Vector3(0, -length * 0.5f, z), new Vector3(0.032f, length * 0.5f, 0.05f));
        mb.AddBox(new Vector3(0, -0.02f, z - 0.075f), new Vector3(0.026f, 0.022f, 0.055f));
    }

    /// <summary>
    /// Cylinder lying along the weapon's forward axis (-Z). MeshBuilder builds cylinders along
    /// +Y, so everything barrel-shaped goes through here to get rotated into place.
    /// <paramref name="rearRadius"/> is the breech end, <paramref name="frontRadius"/> the muzzle.
    /// </summary>
    private static void Barrel(MeshBuilder mb, Vector3 center, float rearRadius, float frontRadius,
        float length, int segments = 10)
    {
        mb.PushTransform(Matrix4x4.CreateRotationX(-MathX.HalfPi) * Matrix4x4.CreateTranslation(center));
        mb.AddCylinder(Vector3.Zero, rearRadius, frontRadius, length, segments);
        mb.PopTransform();
    }

    /// <summary>Ring encircling the forward axis: muzzle brakes, barrel clamps, collars.</summary>
    private static void Collar(MeshBuilder mb, Vector3 center, float major, float minor, int segments = 14)
    {
        mb.PushTransform(Matrix4x4.CreateRotationX(-MathX.HalfPi) * Matrix4x4.CreateTranslation(center));
        mb.AddTorus(Vector3.Zero, major, minor, segments, 6);
        mb.PopTransform();
    }

    /// <summary>Disc facing along the forward axis: rotor plates, blade magazines.</summary>
    private static void Disc(MeshBuilder mb, Vector3 center, float radius, float thickness, int segments = 14)
    {
        mb.PushTransform(Matrix4x4.CreateRotationX(-MathX.HalfPi) * Matrix4x4.CreateTranslation(center));
        mb.AddCylinder(Vector3.Zero, radius, radius, thickness, segments);
        mb.PopTransform();
    }

    private static void Build(WeaponKind kind, MeshBuilder mb)
    {
        switch (kind)
        {
            case WeaponKind.ImpactHammer:
                // A pneumatic hammer, not a gun. The first version was a long slim body with a
                // forward cylinder and a small muzzle plate, which is a gun silhouette however
                // it is textured. What makes this read as a hammer is proportion: a stubby body,
                // exposed piston rods, and a percussion head far wider than anything behind it.
                Grip(mb, 0.22f, 0.03f);
                mb.Material = (int)MatId.WeaponMetal;
                // Compact housing sitting straight on the grip.
                mb.AddBox(new Vector3(0, 0.045f, -0.07f), new Vector3(0.058f, 0.062f, 0.12f));
                // Pressure bottle along the top.
                Barrel(mb, new Vector3(0, 0.125f, -0.06f), 0.042f, 0.042f, 0.19f, 12);
                // Twin piston rods carrying the head, deliberately left exposed.
                mb.Material = (int)MatId.Trim;
                foreach (float rodX in new[] { -0.055f, 0.055f })
                    Barrel(mb, new Vector3(rodX, 0.045f, -0.245f), 0.016f, 0.016f, 0.12f, 8);
                // The head flares to a touch wider than the flak cannon's muzzle — the widest
                // thing in the arsenal, but only just. A first attempt at 0.15 was two thirds
                // wider again and swallowed a quarter of the screen.
                mb.Material = (int)MatId.WeaponMetal;
                Barrel(mb, new Vector3(0, 0.045f, -0.325f), 0.072f, 0.100f, 0.06f, 12);
                mb.Material = (int)MatId.Trim;
                Collar(mb, new Vector3(0, 0.045f, -0.367f), 0.104f, 0.024f);
                mb.Material = (int)MatId.EnergyPanel;
                mb.AddBox(new Vector3(0, 0.168f, -0.06f), new Vector3(0.020f, 0.008f, 0.075f));
                break;

            case WeaponKind.Enforcer:
                Grip(mb, 0.17f, 0.045f);
                mb.Material = (int)MatId.WeaponMetal;
                mb.AddBox(new Vector3(0, 0.045f, -0.09f), new Vector3(0.030f, 0.048f, 0.145f));
                Barrel(mb, new Vector3(0, 0.055f, -0.28f), 0.021f, 0.019f, 0.20f);
                mb.Material = (int)MatId.Trim;
                mb.AddBox(new Vector3(0, 0.095f, -0.16f), new Vector3(0.008f, 0.010f, 0.05f));
                mb.Material = (int)MatId.TechPanelDark;
                mb.AddBox(new Vector3(0, 0.012f, -0.055f), new Vector3(0.020f, 0.028f, 0.05f));
                break;

            case WeaponKind.BioRifle:
                Grip(mb, 0.20f, 0.06f);
                mb.Material = (int)MatId.RustMetal;
                mb.AddBox(new Vector3(0, 0.04f, -0.16f), new Vector3(0.055f, 0.06f, 0.24f));
                Barrel(mb, new Vector3(0, 0.05f, -0.46f), 0.055f, 0.045f, 0.24f, 12);
                mb.Material = (int)MatId.Flesh;
                mb.AddSphere(new Vector3(0.0f, 0.125f, -0.10f), 0.075f, 8, 12);
                mb.AddSphere(new Vector3(0.0f, 0.115f, 0.02f), 0.055f, 8, 12);
                mb.Material = (int)MatId.EnergyPanel;
                Barrel(mb, new Vector3(0, 0.05f, -0.60f), 0.036f, 0.030f, 0.05f, 10);
                mb.Material = (int)MatId.Trim;
                mb.AddBox(new Vector3(0, 0.05f, -0.34f), new Vector3(0.062f, 0.065f, 0.022f));
                break;

            case WeaponKind.ShockRifle:
                Grip(mb, 0.19f, 0.07f);
                mb.Material = (int)MatId.WeaponMetal;
                mb.AddBox(new Vector3(0, 0.045f, -0.18f), new Vector3(0.042f, 0.055f, 0.28f));
                Barrel(mb, new Vector3(0, 0.05f, -0.52f), 0.042f, 0.048f, 0.30f, 12);
                mb.Material = (int)MatId.EnergyPanel;
                Barrel(mb, new Vector3(0, 0.05f, -0.68f), 0.036f, 0.030f, 0.06f, 12);
                mb.AddBox(new Vector3(0.045f, 0.05f, -0.30f), new Vector3(0.006f, 0.016f, 0.14f));
                mb.AddBox(new Vector3(-0.045f, 0.05f, -0.30f), new Vector3(0.006f, 0.016f, 0.14f));
                mb.Material = (int)MatId.Trim;
                Collar(mb, new Vector3(0, 0.05f, -0.62f), 0.055f, 0.012f);
                mb.Material = (int)MatId.TechPanelDark;
                mb.AddBox(new Vector3(0, 0.115f, -0.20f), new Vector3(0.024f, 0.024f, 0.13f));
                break;

            case WeaponKind.PulseGun:
                Grip(mb, 0.20f, 0.07f);
                mb.Material = (int)MatId.WeaponMetal;
                mb.AddBox(new Vector3(0, 0.05f, -0.18f), new Vector3(0.05f, 0.06f, 0.27f));
                for (int i = 0; i < 3; i++)
                {
                    float a = i / 3f * MathX.TwoPi;
                    Barrel(mb, new Vector3(MathF.Cos(a) * 0.035f, 0.05f + MathF.Sin(a) * 0.035f, -0.50f),
                        0.018f, 0.018f, 0.28f, 8);
                }
                mb.Material = (int)MatId.EnergyPanel;
                Barrel(mb, new Vector3(0, 0.05f, -0.58f), 0.052f, 0.045f, 0.07f, 12);
                mb.AddBox(new Vector3(0, 0.115f, -0.22f), new Vector3(0.030f, 0.010f, 0.16f));
                mb.Material = (int)MatId.Trim;
                Collar(mb, new Vector3(0, 0.05f, -0.40f), 0.062f, 0.014f);
                break;

            case WeaponKind.Ripper:
                Grip(mb, 0.19f, 0.055f);
                mb.Material = (int)MatId.WeaponMetal;
                mb.AddBox(new Vector3(0, 0.045f, -0.16f), new Vector3(0.058f, 0.05f, 0.24f));
                mb.AddBox(new Vector3(0, 0.045f, -0.44f), new Vector3(0.075f, 0.028f, 0.14f));
                mb.Material = (int)MatId.Trim;
                // Blade magazine: a disc standing proud of the receiver.
                mb.AddCylinder(new Vector3(0, 0.115f, -0.14f), 0.062f, 0.062f, 0.028f, 14);
                mb.Material = (int)MatId.EnergyPanel;
                mb.AddBox(new Vector3(0.076f, 0.045f, -0.44f), new Vector3(0.006f, 0.020f, 0.12f));
                mb.AddBox(new Vector3(-0.076f, 0.045f, -0.44f), new Vector3(0.006f, 0.020f, 0.12f));
                break;

            case WeaponKind.Minigun:
                Grip(mb, 0.20f, 0.10f);
                mb.Material = (int)MatId.WeaponMetal;
                mb.AddBox(new Vector3(0, 0.05f, -0.14f), new Vector3(0.06f, 0.065f, 0.24f));
                for (int i = 0; i < 6; i++)
                {
                    float a = i / 6f * MathX.TwoPi;
                    Barrel(mb, new Vector3(MathF.Cos(a) * 0.045f, 0.05f + MathF.Sin(a) * 0.045f, -0.52f),
                        0.014f, 0.014f, 0.42f, 8);
                }
                mb.Material = (int)MatId.TechPanelDark;
                Disc(mb, new Vector3(0, 0.05f, -0.31f), 0.072f, 0.07f);
                Disc(mb, new Vector3(0, 0.05f, -0.72f), 0.066f, 0.05f);
                mb.Material = (int)MatId.Trim;
                mb.AddBox(new Vector3(0.075f, 0.02f, -0.10f), new Vector3(0.022f, 0.055f, 0.10f));
                mb.Material = (int)MatId.RustMetal;
                mb.AddBox(new Vector3(-0.085f, 0.03f, 0.0f), new Vector3(0.035f, 0.06f, 0.09f));
                break;

            case WeaponKind.FlakCannon:
                Grip(mb, 0.20f, 0.09f);
                mb.Material = (int)MatId.RustMetal;
                mb.AddBox(new Vector3(0, 0.05f, -0.14f), new Vector3(0.065f, 0.07f, 0.24f));
                // Flared muzzle: narrow at the breech, wide at the mouth.
                Barrel(mb, new Vector3(0, 0.055f, -0.46f), 0.052f, 0.088f, 0.30f, 12);
                mb.Material = (int)MatId.TechPanelDark;
                Disc(mb, new Vector3(0, 0.055f, -0.30f), 0.082f, 0.06f);
                mb.Material = (int)MatId.Trim;
                Collar(mb, new Vector3(0, 0.055f, -0.60f), 0.092f, 0.016f);
                mb.Material = (int)MatId.WeaponMetal;
                mb.AddBox(new Vector3(0.070f, 0.11f, -0.12f), new Vector3(0.018f, 0.030f, 0.10f));
                mb.AddBox(new Vector3(-0.070f, 0.11f, -0.12f), new Vector3(0.018f, 0.030f, 0.10f));
                break;

            case WeaponKind.RocketLauncher:
                Grip(mb, 0.20f, 0.10f);
                mb.Material = (int)MatId.WeaponMetal;
                mb.AddBox(new Vector3(0, 0.05f, -0.12f), new Vector3(0.055f, 0.06f, 0.22f));
                for (int i = 0; i < 3; i++)
                {
                    float a = i / 3f * MathX.TwoPi + MathX.HalfPi;
                    Barrel(mb, new Vector3(MathF.Cos(a) * 0.055f, 0.06f + MathF.Sin(a) * 0.055f, -0.46f),
                        0.042f, 0.042f, 0.44f, 10);
                }
                mb.Material = (int)MatId.TechPanelDark;
                Disc(mb, new Vector3(0, 0.06f, -0.26f), 0.098f, 0.07f);
                mb.Material = (int)MatId.Trim;
                Disc(mb, new Vector3(0, 0.06f, -0.66f), 0.098f, 0.05f);
                mb.Material = (int)MatId.EnergyPanel;
                mb.AddBox(new Vector3(0, 0.135f, -0.12f), new Vector3(0.024f, 0.010f, 0.08f));
                break;

            case WeaponKind.SniperRifle:
                Grip(mb, 0.19f, 0.08f);
                mb.Material = (int)MatId.TechPanelDark;
                mb.AddBox(new Vector3(0, 0.04f, -0.06f), new Vector3(0.036f, 0.055f, 0.22f));
                mb.AddBox(new Vector3(0, 0.005f, 0.14f), new Vector3(0.032f, 0.045f, 0.09f));
                mb.Material = (int)MatId.WeaponMetal;
                Barrel(mb, new Vector3(0, 0.05f, -0.50f), 0.022f, 0.020f, 0.62f);
                mb.AddBox(new Vector3(0, 0.115f, -0.16f), new Vector3(0.026f, 0.028f, 0.16f));
                mb.Material = (int)MatId.Glass;
                Disc(mb, new Vector3(0, 0.115f, -0.325f), 0.024f, 0.012f, 12);
                mb.Material = (int)MatId.Trim;
                Collar(mb, new Vector3(0, 0.115f, -0.32f), 0.030f, 0.008f, 12);
                mb.AddBox(new Vector3(0, 0.05f, -0.80f), new Vector3(0.028f, 0.028f, 0.03f));
                break;

            case WeaponKind.Redeemer:
                Grip(mb, 0.21f, 0.12f);
                mb.Material = (int)MatId.TechPanelDark;
                Barrel(mb, new Vector3(0, 0.06f, -0.28f), 0.10f, 0.10f, 0.66f, 14);
                mb.Material = (int)MatId.Trim;
                Barrel(mb, new Vector3(0, 0.06f, -0.66f), 0.10f, 0.055f, 0.16f, 14);
                Collar(mb, new Vector3(0, 0.06f, -0.10f), 0.105f, 0.016f, 16);
                Collar(mb, new Vector3(0, 0.06f, -0.44f), 0.105f, 0.016f, 16);
                mb.Material = (int)MatId.Lava;
                Barrel(mb, new Vector3(0, 0.06f, -0.755f), 0.052f, 0.030f, 0.05f, 12);
                mb.Material = (int)MatId.EnergyPanel;
                mb.AddBox(new Vector3(0, 0.175f, -0.24f), new Vector3(0.035f, 0.012f, 0.20f));
                mb.Material = (int)MatId.WeaponMetal;
                mb.AddBox(new Vector3(0.105f, 0.02f, -0.10f), new Vector3(0.020f, 0.05f, 0.12f));
                mb.AddBox(new Vector3(-0.105f, 0.02f, -0.10f), new Vector3(0.020f, 0.05f, 0.12f));
                break;
        }
    }

    public void Dispose()
    {
        foreach (var m in _meshes) m?.Dispose();
    }
}
