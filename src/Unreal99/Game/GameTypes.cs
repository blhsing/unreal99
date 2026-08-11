using System.Numerics;
using Unreal99.Core;
using Unreal99.UI;

namespace Unreal99.Game;

public enum Team { None = -1, Red = 0, Blue = 1 }

/// <summary>
/// Every weapon across the three games. The first eleven are the 1999 arsenal and keep their
/// original indices, because save files and the <c>--weapon</c> switches refer to them by number.
/// The rest are what UT2004 and UT3 added — including the ones that replaced a 1999 weapon rather
/// than joining it, which are separate entries here because they behave differently enough to be
/// different weapons: the Shield Gun is not an Impact Hammer, and the Stinger is not a Minigun.
/// </summary>
public enum WeaponKind
{
    // --- Unreal Tournament (1999) ---
    ImpactHammer = 0,
    Enforcer,
    BioRifle,
    ShockRifle,
    PulseGun,
    Ripper,
    Minigun,
    FlakCannon,
    RocketLauncher,
    SniperRifle,
    Redeemer,
    // --- Unreal Tournament 2004 ---
    ShieldGun,
    AssaultRifle,
    LinkGun,
    LightningGun,
    MineLayer,
    GrenadeLauncher,
    Avril,
    IonPainter,
    TargetPainter,
    Translocator,
    SuperShockRifle,
    // --- Unreal Tournament 3 ---
    Stinger,
    /// <summary>Bombing Run only, and never a map pickup: taking the ball equips it.</summary>
    BallLauncher,
    Count
}

public enum AmmoKind
{
    None = -1,
    Bullets = 0,     // Enforcer
    BioSludge,
    ShockCore,
    PulseCells,
    Blades,
    MinigunBullets,
    FlakShells,
    Rockets,
    SniperRounds,
    Warhead,
    // --- UT2004 / UT3 ---
    /// <summary>Assault Rifle's 5.56mm, kept apart from the Enforcer's so neither refills the other.</summary>
    RifleRounds,
    /// <summary>The M355 grenades slung under the Assault Rifle.</summary>
    RifleGrenades,
    LinkCells,
    LightningCells,
    Mines,
    Grenades,
    AvrilMissiles,
    /// <summary>One shot each, and each is a match-deciding one.</summary>
    IonCharge,
    TargetBeacon,
    /// <summary>The Translocator recharges rather than consuming pickups.</summary>
    TranslocatorCharge,
    Shards,          // Stinger
    Count
}

public enum PickupKind
{
    HealthVial = 0,
    HealthPack,
    SuperHealth,
    ThighPads,
    BodyArmor,
    ShieldBelt,
    DamageAmp,
    Invisibility,
    JumpBoots,
    WeaponPickup,
    AmmoPickup,
    /// <summary>A rack that hands out several weapons at once, as UT2004 and UT3 maps do.</summary>
    WeaponLocker,
    Count
}

public enum DamageType
{
    Generic,
    Hitscan,
    Explosion,
    Energy,
    Melee,
    Fall,
    Lava,
    Drowning,
    Telefrag,
    Void,
}

public static class GameTypes
{
    public static string WeaponName(WeaponKind w) => w switch
    {
        WeaponKind.ImpactHammer => Loc.WeaponImpactHammer,
        WeaponKind.Enforcer => Loc.WeaponEnforcer,
        WeaponKind.BioRifle => Loc.WeaponBioRifle,
        WeaponKind.ShockRifle => Loc.WeaponShockRifle,
        WeaponKind.PulseGun => Loc.WeaponPulseGun,
        WeaponKind.Ripper => Loc.WeaponRipper,
        WeaponKind.Minigun => Loc.WeaponMinigun,
        WeaponKind.FlakCannon => Loc.WeaponFlakCannon,
        WeaponKind.RocketLauncher => Loc.WeaponRocketLauncher,
        WeaponKind.SniperRifle => Loc.WeaponSniperRifle,
        WeaponKind.Redeemer => Loc.WeaponRedeemer,
        WeaponKind.ShieldGun => Loc.WeaponShieldGun,
        WeaponKind.AssaultRifle => Loc.WeaponAssaultRifle,
        WeaponKind.LinkGun => Loc.WeaponLinkGun,
        WeaponKind.LightningGun => Loc.WeaponLightningGun,
        WeaponKind.MineLayer => Loc.WeaponMineLayer,
        WeaponKind.GrenadeLauncher => Loc.WeaponGrenadeLauncher,
        WeaponKind.Avril => Loc.WeaponAvril,
        WeaponKind.IonPainter => Loc.WeaponIonPainter,
        WeaponKind.TargetPainter => Loc.WeaponTargetPainter,
        WeaponKind.Translocator => Loc.WeaponTranslocator,
        WeaponKind.SuperShockRifle => Loc.WeaponSuperShockRifle,
        WeaponKind.Stinger => Loc.WeaponStinger,
        WeaponKind.BallLauncher => Loc.WeaponBallLauncher,
        _ => "",
    };

    public static string PickupName(PickupKind p) => p switch
    {
        PickupKind.HealthVial => Loc.PickupHealthVial,
        PickupKind.HealthPack => Loc.PickupHealthPack,
        PickupKind.SuperHealth => Loc.PickupSuperHealth,
        PickupKind.ThighPads => Loc.PickupThighPads,
        PickupKind.BodyArmor => Loc.PickupBodyArmor,
        PickupKind.ShieldBelt => Loc.PickupShieldBelt,
        PickupKind.DamageAmp => Loc.PickupDamageAmp,
        PickupKind.Invisibility => Loc.PickupInvisibility,
        PickupKind.JumpBoots => Loc.PickupJumpBoots,
        _ => Loc.PickupAmmo,
    };

    public static string TeamName(Team t) => t switch
    {
        Team.Red => Loc.HudTeamRed,
        Team.Blue => Loc.HudTeamBlue,
        _ => "",
    };

    /// <summary>Team colours, in linear space so they read correctly through the tone mapper.</summary>
    public static Vector3 TeamColor(Team t) => t switch
    {
        Team.Red => new Vector3(1.0f, 0.16f, 0.12f),
        Team.Blue => new Vector3(0.18f, 0.42f, 1.0f),
        _ => new Vector3(0.85f, 0.85f, 0.88f),
    };

    /// <summary>Per-player accent colours used for HUD chrome and armour tint in free-for-all.</summary>
    public static Vector3 PlayerColor(int index) => index switch
    {
        0 => new Vector3(0.20f, 0.72f, 1.00f),
        1 => new Vector3(1.00f, 0.55f, 0.10f),
        2 => new Vector3(0.35f, 1.00f, 0.35f),
        _ => new Vector3(1.00f, 0.30f, 0.75f),
    };

    public static Vector3 BotColor(int seed)
    {
        var rng = new Rng((uint)(seed * 2654435761u + 1013904223u));
        return MathX.HsvToRgb(rng.NextFloat(), 0.65f, 0.85f);
    }
}
