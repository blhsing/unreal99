using Unreal99.Core;
using Unreal99.Game;

namespace Unreal99.World;

/// <summary>
/// Reports how much geometry each arena actually carries. Map detail is otherwise judged by eye,
/// which makes it impossible to say whether a pass over an arena added real structure or merely
/// moved boxes around; this prints the static triangle count for every map, without a GL context.
/// </summary>
public static class MapStats
{
    /// <summary>Arenas below this read as untextured blocking volumes rather than as places.</summary>
    public const int SparseThreshold = 6000;

    /// <summary>
    /// Floor for the player character. Set against the vehicle roster (1,708–8,516) rather than
    /// against what the model happened to cost: an opponent is looked at more than any vehicle.
    /// </summary>
    public const int MinimumCharacterTriangles = 3000;

    public static int Run(string[] args)
    {
        bool gateOnly = args.Contains("--gate");
        bool listVehicles = args.Contains("--vehicles");
        int total = 0, sparse = 0;
        var rows = new List<(int Id, string Name, int Tris)>();

        for (var id = MapId.Morbias; id < MapId.Count; id++)
        {
            using Level level = Maps.Build(null, id);
            rows.Add(((int)id, Maps.Name(id), level.GeometryTriangles));
            total += level.GeometryTriangles;
            if (listVehicles && level.VehicleSpawns.Count > 0)
            {
                Console.WriteLine($"VEHICLE_MAP {(int)id} {Maps.Name(id)}");
                for (int i = 0; i < level.VehicleSpawns.Count; i++)
                {
                    VehicleSpawn spawn = level.VehicleSpawns[i];
                    Console.WriteLine($"  {i,2} {spawn.Kind,-12} "
                        + $"({spawn.Position.X,7:0.0}, {spawn.Position.Y,6:0.0}, {spawn.Position.Z,7:0.0})");
                }
            }
        }

        if (!gateOnly)
        {
            Console.WriteLine("編號  三角形    地圖");
            foreach (var (id, name, tris) in rows)
                Console.WriteLine($"{id,3}  {tris,8:N0}  {name}"
                    + (tris < SparseThreshold ? "  ← 幾何量偏低" : string.Empty));
            Console.WriteLine();
        }

        foreach (var (_, _, tris) in rows) if (tris < SparseThreshold) sparse++;

        var ordered = rows.OrderBy(r => r.Tris).ToList();
        Console.WriteLine($"MAP_STATS 合計={total:N0} 平均={total / rows.Count:N0} "
            + $"最低={ordered[0].Tris:N0}（{ordered[0].Name}） "
            + $"最高={ordered[^1].Tris:N0}（{ordered[^1].Name}） "
            + $"低於門檻={sparse}/{rows.Count}");
        // The player model is measured here too. It is the thing on screen most of the match and
        // was the only major model in the game without a density floor.
        int character = CharacterModel.TriangleCount();
        int hand = WeaponModels.SupportHandTriangleCount();
        bool characterSparse = character < MinimumCharacterTriangles;
        Console.WriteLine($"MODEL_STATS 角色={character:N0} 支撐手={hand:N0} "
            + $"（角色下限 {MinimumCharacterTriangles:N0}）");

        bool pass = sparse == 0 && !characterSparse;
        Console.WriteLine(pass ? "MAP_STATS PASS"
            : $"MAP_STATS FAIL sparse={sparse} character={(characterSparse ? "低" : "足")}");
        return pass ? 0 : 1;
    }
}
