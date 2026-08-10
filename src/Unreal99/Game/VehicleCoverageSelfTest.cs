using Unreal99.UI;
using Unreal99.World;

namespace Unreal99.Game;

/// <summary>
/// Asserts that every vehicle actually reaches a player. A vehicle that is modelled, simulated and
/// documented but parked on no map is invisible in normal play, which is exactly the state the
/// roster was in before the Warfare maps landed. This reads the real level data rather than a
/// hand-kept table, so it fails the moment a map's roster drifts.
/// </summary>
public static class VehicleCoverageSelfTest
{
    /// <summary>How many distinct arenas each vehicle has to appear on.</summary>
    public const int RequiredMaps = 2;

    public static int Run()
    {
        var appearances = new Dictionary<VehicleKind, List<MapId>>();
        foreach (VehicleKind kind in Enum.GetValues<VehicleKind>())
            if (kind != VehicleKind.Count) appearances[kind] = new List<MapId>();

        for (var id = MapId.Morbias; id < MapId.Count; id++)
        {
            // Null GL: gameplay placements only, no geometry upload and no nav bake.
            using Level level = Maps.Build(null, id);
            var seen = new HashSet<VehicleKind>();
            foreach (var spawn in level.VehicleSpawns) seen.Add(spawn.Kind);
            // Vehicle nodes hand out a vehicle that never appears as a spawn pad — the Leviathan
            // on Serenity is only ever earned — so the node rewards count too.
            foreach (var node in level.PowerNodes)
                if (node.RewardVehicle != VehicleKind.Count) seen.Add(node.RewardVehicle);
            foreach (VehicleKind kind in seen) appearances[kind].Add(id);
        }

        var failures = new List<string>();
        foreach ((VehicleKind kind, List<MapId> maps) in appearances)
        {
            // The hoverboard is carried by every player in the vehicle gametypes rather than
            // parked anywhere, so map spawns are the wrong measure for it.
            if (kind == VehicleKind.Hoverboard)
            {
                if (maps.Count > 0)
                    failures.Add($"{VehicleDef.Get(kind).Name} should not be a map spawn any more");
                continue;
            }
            if (maps.Count >= RequiredMaps) continue;
            failures.Add($"{VehicleDef.Get(kind).Name} appears on {maps.Count} map(s), needs {RequiredMaps}");
        }

        foreach ((VehicleKind kind, List<MapId> maps) in appearances.OrderBy(kv => (int)kv.Key))
        {
            string where = kind == VehicleKind.Hoverboard
                ? "所有載具模式（隨身攜帶）"
                : maps.Count == 0 ? "—" : string.Join(", ", maps.Select(Maps.Name));
            Console.WriteLine($"VEHICLE_COVERAGE {VehicleDef.Get(kind).Name,-12} {maps.Count} :: {where}");
        }

        foreach (string failure in failures) Console.Error.WriteLine($"VEHICLE_COVERAGE FAIL: {failure}");
        Console.WriteLine(failures.Count == 0
            ? "VEHICLE_COVERAGE PASS"
            : $"VEHICLE_COVERAGE FAIL ({failures.Count})");
        return failures.Count == 0 ? 0 : 2;
    }
}
