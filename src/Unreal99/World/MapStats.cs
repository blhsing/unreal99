using Unreal99.Core;

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

    public static int Run(string[] args)
    {
        bool gateOnly = args.Contains("--gate");
        int total = 0, sparse = 0;
        var rows = new List<(int Id, string Name, int Tris)>();

        for (var id = MapId.Morbias; id < MapId.Count; id++)
        {
            using Level level = Maps.Build(null, id);
            rows.Add(((int)id, Maps.Name(id), level.GeometryTriangles));
            total += level.GeometryTriangles;
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
        Console.WriteLine(sparse == 0 ? "MAP_STATS PASS" : $"MAP_STATS FAIL sparse={sparse}");
        return sparse == 0 ? 0 : 1;
    }
}
