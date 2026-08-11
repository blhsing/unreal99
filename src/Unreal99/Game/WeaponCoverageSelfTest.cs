using Unreal99.UI;
using Unreal99.World;

namespace Unreal99.Game;

/// <summary>
/// Two things every weapon has to satisfy, both read from the real data rather than a hand-kept
/// list: it has to be reachable on at least two arenas, and it has to be modelled to the same
/// density as the weapons it sits beside in the weapon guide. A weapon that exists only in an
/// enum, or that is visibly coarser than its neighbours, is not finished.
/// </summary>
public static class WeaponCoverageSelfTest
{
    public const int RequiredMaps = 2;

    /// <summary>
    /// Floor for triangle count. The whole arsenal is meant to look like one generation of models:
    /// after the high-poly rebuild the 1999 weapons run from roughly 1,200 to 2,500 triangles, and
    /// the UT2004/UT3 additions sit in the same band. A weapon that lands appreciably under this
    /// reads as a placeholder next to the one beside it in the weapon guide — which is exactly how
    /// the first cut of the new weapons looked at 650–1,100.
    /// </summary>
    public const int MinimumTriangles = 1200;

    public static int Run()
    {
        var failures = new List<string>();

        // ---------------------------------------------------------------- placement
        var appearances = new Dictionary<WeaponKind, List<MapId>>();
        foreach (WeaponKind kind in Enum.GetValues<WeaponKind>())
            if (kind != WeaponKind.Count) appearances[kind] = new List<MapId>();

        for (var id = MapId.Morbias; id < MapId.Count; id++)
        {
            using Level level = Maps.Build(null, id);
            var seen = new HashSet<WeaponKind>();
            foreach (var p in level.Pickups)
            {
                if (p.Kind == PickupKind.WeaponPickup && p.Weapon < WeaponKind.Count) seen.Add(p.Weapon);
                // Lockers are how UT2004 and UT3 arm most of their maps, so what is on the rack
                // counts exactly as much as a weapon lying on the floor.
                if (p.Kind == PickupKind.WeaponLocker && p.LockerWeapons != null)
                    foreach (WeaponKind w in p.LockerWeapons) seen.Add(w);
            }
            foreach (WeaponKind kind in seen) appearances[kind].Add(id);
        }

        foreach ((WeaponKind kind, List<MapId> maps) in appearances)
        {
            if (Exempt(kind)) continue;
            if (maps.Count >= RequiredMaps) continue;
            failures.Add($"{GameTypes.WeaponName(kind)} appears on {maps.Count} map(s), needs {RequiredMaps}");
        }

        // ---------------------------------------------------------------- model density
        foreach (WeaponKind kind in Enum.GetValues<WeaponKind>())
        {
            if (kind == WeaponKind.Count) continue;
            int triangles = WeaponModels.TriangleCountFor(kind);
            if (triangles < MinimumTriangles)
                failures.Add($"{GameTypes.WeaponName(kind)} is {triangles} triangles, floor is {MinimumTriangles}");
        }

        // ---------------------------------------------------------------- report
        foreach (WeaponKind kind in Enum.GetValues<WeaponKind>().OrderBy(k => (int)k))
        {
            if (kind == WeaponKind.Count) continue;
            List<MapId> maps = appearances[kind];
            string where = Exempt(kind) ? ExemptionReason(kind)
                : maps.Count == 0 ? "—" : string.Join(", ", maps.Select(Maps.Name));
            Console.WriteLine($"WEAPON_COVERAGE {GameTypes.WeaponName(kind),-8} "
                + $"{WeaponModels.TriangleCountFor(kind),6} tris  {maps.Count} :: {where}");
        }

        foreach (string failure in failures) Console.Error.WriteLine($"WEAPON_COVERAGE FAIL: {failure}");
        Console.WriteLine(failures.Count == 0
            ? "WEAPON_COVERAGE PASS"
            : $"WEAPON_COVERAGE FAIL ({failures.Count})");
        return failures.Count == 0 ? 0 : 2;
    }

    /// <summary>
    /// Three weapons are never map pickups in the originals either, so counting arenas is the
    /// wrong measure for them — they still have to pass the model-density check.
    /// </summary>
    private static bool Exempt(WeaponKind kind) => kind
        is WeaponKind.Translocator or WeaponKind.SuperShockRifle or WeaponKind.BallLauncher
        or WeaponKind.ShieldGun or WeaponKind.AssaultRifle;

    private static string ExemptionReason(WeaponKind kind) => kind switch
    {
        WeaponKind.Translocator => "隨身配備（UT2004／UT3 出生即持有）",
        WeaponKind.SuperShockRifle => "瞬殺模式專用",
        WeaponKind.BallLauncher => "轟炸模式專用；拿到球才會自動裝備",
        WeaponKind.ShieldGun or WeaponKind.AssaultRifle => "UT2004 地圖的出生武器",
        _ => "",
    };
}
