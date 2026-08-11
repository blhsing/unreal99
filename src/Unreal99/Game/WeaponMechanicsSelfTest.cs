namespace Unreal99.Game;

/// <summary>
/// Headless contract for every post-UT99 weapon. It guards the behavioral fire-mode pairing;
/// model/pickup coverage is enforced separately by <c>--weaponcoverage</c>.
/// </summary>
public static class WeaponMechanicsSelfTest
{
    public static int Run()
    {
        int failures = 0;
        void Check(bool condition, string name)
        {
            if (condition) return;
            failures++;
            Console.Error.WriteLine($"WEAPON_MECHANICS FAIL {name}");
        }

        WeaponDef shield = Weapons.Get(WeaponKind.ShieldGun);
        Check(shield.Primary.Mode == FireMode.Melee && shield.Primary.Chargeable
              && shield.Alt.Mode == FireMode.Shield, "ShieldGun charge/shield");

        WeaponDef rifle = Weapons.Get(WeaponKind.AssaultRifle);
        Check(rifle.Primary.Mode == FireMode.Hitscan && rifle.Primary.Automatic
              && rifle.Alt.Projectile == ProjectileKind.RifleGrenade && rifle.Alt.Chargeable,
            "AssaultRifle bullet/charged grenade");

        WeaponDef link = Weapons.Get(WeaponKind.LinkGun);
        Check(link.Primary.Projectile == ProjectileKind.PlasmaBolt
              && link.Alt.Mode == FireMode.Beam && link.Alt.Automatic, "LinkGun plasma/beam");

        WeaponDef lightning = Weapons.Get(WeaponKind.LightningGun);
        Check(lightning.Primary.Mode == FireMode.Hitscan
              && lightning.Primary.HeadshotMultiplier > 1f && lightning.Alt.ZoomFov > 0f,
            "LightningGun headshot/zoom");

        WeaponDef mines = Weapons.Get(WeaponKind.MineLayer);
        Check(mines.Primary.Projectile == ProjectileKind.SpiderMine
              && mines.Alt.Mode == FireMode.Painter, "MineLayer deploy/redirect");

        WeaponDef grenades = Weapons.Get(WeaponKind.GrenadeLauncher);
        Check(grenades.Primary.Projectile == ProjectileKind.StickyGrenade
              && grenades.Alt.Mode == FireMode.Detonate, "GrenadeLauncher plant/detonate");

        WeaponDef avril = Weapons.Get(WeaponKind.Avril);
        Check(avril.Primary.Mode == FireMode.LockOn
              && avril.Primary.Projectile == ProjectileKind.SeekerMissile
              && avril.Alt.ZoomFov > 0f, "AVRiL dumbfire/vehicle lock zoom");

        WeaponDef ion = Weapons.Get(WeaponKind.IonPainter);
        WeaponDef target = Weapons.Get(WeaponKind.TargetPainter);
        Check(ion.Primary.Mode == FireMode.Painter && ion.Alt.ZoomFov > 0f,
            "IonPainter paint/zoom");
        Check(target.Primary.Mode == FireMode.Painter && target.Alt.ZoomFov > 0f,
            "TargetPainter paint/zoom");

        WeaponDef translocator = Weapons.Get(WeaponKind.Translocator);
        Check(translocator.Primary.Projectile == ProjectileKind.TranslocatorDisc
              && translocator.Primary.Damage == 0f && translocator.Alt.Mode == FireMode.Recall,
            "Translocator disc/recall");

        WeaponDef superShock = Weapons.Get(WeaponKind.SuperShockRifle);
        Check(superShock.Primary.Mode == FireMode.Hitscan && superShock.Primary.Damage >= 1000f
              && superShock.Alt.Damage >= 1000f, "SuperShockRifle instagib");

        WeaponDef stinger = Weapons.Get(WeaponKind.Stinger);
        Check(stinger.Primary.Automatic && stinger.Primary.Projectile == ProjectileKind.Shard
              && stinger.Alt.Damage > stinger.Primary.Damage
              && stinger.Alt.Knockback > stinger.Primary.Knockback,
            "Stinger rapid/heavy shard");

        WeaponDef ball = Weapons.Get(WeaponKind.BallLauncher);
        Check(ball.Primary.Projectile == ProjectileKind.Ball && ball.Primary.Damage == 0f
              && ball.Alt.Damage == 0f, "BallLauncher harmless pass/throw");

        Console.WriteLine(failures == 0
            ? "WEAPON_MECHANICS PASS"
            : $"WEAPON_MECHANICS FAILURES={failures}");
        return failures == 0 ? 0 : 1;
    }
}
