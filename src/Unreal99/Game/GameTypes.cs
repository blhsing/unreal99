using System.Numerics;
using Unreal99.Core;
using Unreal99.UI;

namespace Unreal99.Game;

public enum Team { None = -1, Red = 0, Blue = 1 }

public enum WeaponKind
{
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
