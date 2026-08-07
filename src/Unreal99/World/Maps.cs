using System.Numerics;
using Silk.NET.OpenGL;
using Unreal99.Core;
using Unreal99.Game;
using Unreal99.Rendering;
using Unreal99.UI;

namespace Unreal99.World;

public enum MapId
{
    // Deathmatch, roughly smallest to largest.
    Morbias = 0,
    Stalwart,
    Curse,
    Grinder,
    Codex,
    Gothic,
    Deck16,
    Turbine,
    Phobos,
    Peak,
    Liandri,
    Morpheus,
    HyperBlast,
    // Capture the flag.
    Coret,
    November,
    FacingWorlds,
    LavaGiant,
    Count
}

/// <summary>
/// The arenas. Every one is generated from code — no editor files, no imported assets —
/// using <see cref="LevelBuilder"/> to emit render geometry and collision brushes together.
///
/// All of them are homages to the arenas that made the 1999 original famous: the layout,
/// the routes and the pacing are rebuilt from memory, but every brush, material and pickup
/// is written here from scratch. Nothing is decompiled, converted or imported.
/// </summary>
public static partial class Maps
{
    public static string Name(MapId id) => id switch
    {
        MapId.Morbias => Loc.MapMorbias,
        MapId.Stalwart => Loc.MapStalwart,
        MapId.Curse => Loc.MapCurse,
        MapId.Grinder => Loc.MapGrinder,
        MapId.Codex => Loc.MapCodex,
        MapId.Gothic => Loc.MapGothic,
        MapId.Deck16 => Loc.MapDeck16,
        MapId.Turbine => Loc.MapTurbine,
        MapId.Phobos => Loc.MapPhobos,
        MapId.Peak => Loc.MapPeak,
        MapId.Liandri => Loc.MapLiandri,
        MapId.Morpheus => Loc.MapMorpheus,
        MapId.HyperBlast => Loc.MapHyperBlast,
        MapId.Coret => Loc.MapCoret,
        MapId.November => Loc.MapNovember,
        MapId.FacingWorlds => Loc.MapFacingWorlds,
        MapId.LavaGiant => Loc.MapLavaGiant,
        _ => Loc.MapDeck16,
    };

    public static string Description(MapId id) => id switch
    {
        MapId.Morbias => Loc.MapMorbiasDesc,
        MapId.Stalwart => Loc.MapStalwartDesc,
        MapId.Curse => Loc.MapCurseDesc,
        MapId.Grinder => Loc.MapGrinderDesc,
        MapId.Codex => Loc.MapCodexDesc,
        MapId.Gothic => Loc.MapGothicDesc,
        MapId.Deck16 => Loc.MapDeck16Desc,
        MapId.Turbine => Loc.MapTurbineDesc,
        MapId.Phobos => Loc.MapPhobosDesc,
        MapId.Peak => Loc.MapPeakDesc,
        MapId.Liandri => Loc.MapLiandriDesc,
        MapId.Morpheus => Loc.MapMorpheusDesc,
        MapId.HyperBlast => Loc.MapHyperBlastDesc,
        MapId.Coret => Loc.MapCoretDesc,
        MapId.November => Loc.MapNovemberDesc,
        MapId.FacingWorlds => Loc.MapFacingWorldsDesc,
        MapId.LavaGiant => Loc.MapLavaGiantDesc,
        _ => Loc.MapDeck16Desc,
    };

    public static bool SupportsCtf(MapId id)
        => id is MapId.Coret or MapId.November or MapId.FacingWorlds or MapId.LavaGiant;

    public static Level Build(GL gl, MapId id) => id switch
    {
        MapId.Morbias => BuildMorbias(gl),
        MapId.Stalwart => BuildStalwart(gl),
        MapId.Curse => BuildCurse(gl),
        MapId.Grinder => BuildGrinder(gl),
        MapId.Codex => BuildCodex(gl),
        MapId.Gothic => BuildGothic(gl),
        MapId.Deck16 => BuildDeck16(gl),
        MapId.Turbine => BuildTurbine(gl),
        MapId.Phobos => BuildPhobos(gl),
        MapId.Peak => BuildPeak(gl),
        MapId.Liandri => BuildLiandri(gl),
        MapId.Morpheus => BuildMorpheus(gl),
        MapId.HyperBlast => BuildHyperBlast(gl),
        MapId.Coret => BuildCoret(gl),
        MapId.November => BuildNovember(gl),
        MapId.FacingWorlds => BuildFacingWorlds(gl),
        MapId.LavaGiant => BuildLavaGiant(gl),
        _ => BuildDeck16(gl),
    };

    // ================================================================ shared helpers

    /// <summary>Non-colliding guard rail run: a top bar plus evenly spaced posts.</summary>
    private static void RailRun(LevelBuilder b, Vector3 a, Vector3 c, float height = 0.95f,
        MatId mat = MatId.Trim)
    {
        Vector3 min = Vector3.Min(a, c), max = Vector3.Max(a, c);
        b.Decor(new Vector3(min.X - 0.07f, min.Y + height - 0.10f, min.Z - 0.07f),
                new Vector3(max.X + 0.07f, max.Y + height, max.Z + 0.07f), mat, 1.4f);
        float len = Vector3.Distance(a, c);
        int posts = Math.Max(2, (int)(len / 2.6f));
        for (int i = 0; i <= posts; i++)
        {
            Vector3 p = Vector3.Lerp(a, c, i / (float)posts);
            b.Decor(p - new Vector3(0.06f, 0f, 0.06f), p + new Vector3(0.06f, height, 0.06f), mat, 1.4f);
        }
    }

    /// <summary>
    /// Guard posts spaced around a circle. Only the posts are emitted (no connecting bar),
    /// because a bar between two off-axis points would need a rotated box.
    /// </summary>
    private static void RingPosts(LevelBuilder b, float y, float radius, int count, float height = 0.95f,
        MatId mat = MatId.Trim)
    {
        for (int i = 0; i < count; i++)
        {
            float a = i / (float)count * MathX.TwoPi;
            Vector3 p = new(MathF.Cos(a) * radius, y, MathF.Sin(a) * radius);
            b.Decor(p - new Vector3(0.07f, 0f, 0.07f), p + new Vector3(0.07f, height, 0.07f), mat, 1.4f);
            b.Decor(p - new Vector3(0.14f, -height + 0.12f, 0.14f),
                    p + new Vector3(0.14f, height, 0.14f), mat, 1.4f);
        }
    }
}
