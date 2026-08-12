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
    // Domination. Every stock DOM map carries exactly three control points.
    Leadworks,
    Sesmar,
    Olden,
    Cinder,
    // Onslaught. Built for the mode: open ground, a node chain, vehicles at every node.
    Torlan,
    Primeval,
    Crossfire,
    Dria,
    // Assault. One-way maps: a fixed objective sequence and forward spawns behind it.
    Convoy,
    Frigate,
    Glacier,
    // Warfare. Onslaught's successor: orbs, auxiliary nodes and a hoverboard for everyone.
    WarTorlan,
    WarTorlanNecris,
    Serenity,
    Avalanche,
    OnyxCoast,
    Islander,
    // Bombing Run. Symmetrical ball arenas: a hoop at each end and one ball at midfield.
    Anubis,
    Colossus,
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
        MapId.Leadworks => Loc.MapLeadworks,
        MapId.Sesmar => Loc.MapSesmar,
        MapId.Olden => Loc.MapOlden,
        MapId.Cinder => Loc.MapCinder,
        MapId.Torlan => Loc.MapTorlan,
        MapId.Primeval => Loc.MapPrimeval,
        MapId.Crossfire => Loc.MapCrossfire,
        MapId.Dria => Loc.MapDria,
        MapId.Convoy => Loc.MapConvoy,
        MapId.Frigate => Loc.MapFrigate,
        MapId.Glacier => Loc.MapGlacier,
        MapId.WarTorlan => Loc.MapWarTorlan,
        MapId.WarTorlanNecris => Loc.MapWarTorlanNecris,
        MapId.Serenity => Loc.MapSerenity,
        MapId.Avalanche => Loc.MapAvalanche,
        MapId.OnyxCoast => Loc.MapOnyxCoast,
        MapId.Islander => Loc.MapIslander,
        MapId.Anubis => Loc.MapAnubis,
        MapId.Colossus => Loc.MapColossus,
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
        MapId.Leadworks => Loc.MapLeadworksDesc,
        MapId.Sesmar => Loc.MapSesmarDesc,
        MapId.Olden => Loc.MapOldenDesc,
        MapId.Cinder => Loc.MapCinderDesc,
        MapId.Torlan => Loc.MapTorlanDesc,
        MapId.Primeval => Loc.MapPrimevalDesc,
        MapId.Crossfire => Loc.MapCrossfireDesc,
        MapId.Dria => Loc.MapDriaDesc,
        MapId.Convoy => Loc.MapConvoyDesc,
        MapId.Frigate => Loc.MapFrigateDesc,
        MapId.Glacier => Loc.MapGlacierDesc,
        MapId.WarTorlan => Loc.MapWarTorlanDesc,
        MapId.WarTorlanNecris => Loc.MapWarTorlanNecrisDesc,
        MapId.Serenity => Loc.MapSerenityDesc,
        MapId.Avalanche => Loc.MapAvalancheDesc,
        MapId.OnyxCoast => Loc.MapOnyxCoastDesc,
        MapId.Islander => Loc.MapIslanderDesc,
        MapId.Anubis => Loc.MapAnubisDesc,
        MapId.Colossus => Loc.MapColossusDesc,
        _ => Loc.MapDeck16Desc,
    };

    public static bool SupportsCtf(MapId id)
        => id is MapId.Coret or MapId.November or MapId.FacingWorlds or MapId.LavaGiant;

    /// <summary>Onslaught needs a node graph, so only the ONS arenas can host it.</summary>
    public static bool SupportsOnslaught(MapId id)
        => id is MapId.Torlan or MapId.Primeval or MapId.Crossfire or MapId.Dria;

    /// <summary>Assault needs an objective sequence, so only the AS arenas can host it.</summary>
    public static bool SupportsAssault(MapId id)
        => id is MapId.Convoy or MapId.Frigate or MapId.Glacier;

    /// <summary>Warfare needs a node graph with orb spawns, so only the WAR arenas can host it.</summary>
    public static bool SupportsWarfare(MapId id)
        => id is MapId.WarTorlan or MapId.WarTorlanNecris or MapId.Serenity or MapId.Avalanche
            or MapId.OnyxCoast or MapId.Islander;

    /// <summary>Bombing Run needs a ball spawn and two hoops, so only the BR arenas can host it.</summary>
    public static bool SupportsBombingRun(MapId id)
        => id is MapId.Anubis or MapId.Colossus;

    /// <summary>Domination needs control points, so only the DOM arenas can host it.</summary>
    public static bool SupportsDomination(MapId id)
        => id is MapId.Leadworks or MapId.Sesmar or MapId.Olden or MapId.Cinder;

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
        MapId.Leadworks => BuildLeadworks(gl),
        MapId.Sesmar => BuildSesmar(gl),
        MapId.Olden => BuildOlden(gl),
        MapId.Cinder => BuildCinder(gl),
        MapId.Torlan => BuildTorlan(gl),
        MapId.Primeval => BuildPrimeval(gl),
        MapId.Crossfire => BuildCrossfire(gl),
        MapId.Dria => BuildDria(gl),
        MapId.Convoy => BuildConvoy(gl),
        MapId.Frigate => BuildFrigate(gl),
        MapId.Glacier => BuildGlacier(gl),
        MapId.WarTorlan => BuildWarTorlan(gl, necris: false),
        MapId.WarTorlanNecris => BuildWarTorlan(gl, necris: true),
        MapId.Serenity => BuildSerenity(gl),
        MapId.Avalanche => BuildAvalanche(gl),
        MapId.OnyxCoast => BuildOnyxCoast(gl),
        MapId.Islander => BuildIslander(gl),
        MapId.Anubis => BuildAnubis(gl),
        MapId.Colossus => BuildColossus(gl),
        _ => BuildDeck16(gl),
    };

    // ================================================================ shared helpers

    /// <summary>
    /// Non-colliding guard rail run: top rail, mid rail, kick plate and posts on base flanges.
    ///
    /// A single bar on bare posts is the industrial equivalent of an untrimmed wall. Real handrail
    /// has three horizontals and a plate at deck level, and this runs along every catwalk, gallery
    /// and bridge in the game, so the profile is worth building properly once.
    /// </summary>
    private static void RailRun(LevelBuilder b, Vector3 a, Vector3 c, float height = 0.95f,
        MatId mat = MatId.Trim)
    {
        Vector3 min = Vector3.Min(a, c), max = Vector3.Max(a, c);
        // Top rail, doubled into a slight cap so it has an edge rather than one flat face.
        b.Decor(new Vector3(min.X - 0.07f, min.Y + height - 0.10f, min.Z - 0.07f),
                new Vector3(max.X + 0.07f, max.Y + height, max.Z + 0.07f), mat, 1.4f);
        b.Decor(new Vector3(min.X - 0.10f, min.Y + height, min.Z - 0.10f),
                new Vector3(max.X + 0.10f, max.Y + height + 0.06f, max.Z + 0.10f), mat, 1.4f);
        // Mid rail and kick plate.
        b.Decor(new Vector3(min.X - 0.05f, min.Y + height * 0.52f, min.Z - 0.05f),
                new Vector3(max.X + 0.05f, max.Y + height * 0.52f + 0.09f, max.Z + 0.05f), mat, 1.4f);
        b.Decor(new Vector3(min.X - 0.06f, min.Y + 0.02f, min.Z - 0.06f),
                new Vector3(max.X + 0.06f, max.Y + 0.20f, max.Z + 0.06f), mat, 1.4f);

        float len = Vector3.Distance(a, c);
        int posts = Math.Max(2, (int)(len / 2.6f));
        for (int i = 0; i <= posts; i++)
        {
            Vector3 p = Vector3.Lerp(a, c, i / (float)posts);
            b.Decor(p - new Vector3(0.06f, 0f, 0.06f), p + new Vector3(0.06f, height, 0.06f), mat, 1.4f);
            b.Decor(p - new Vector3(0.13f, 0f, 0.13f), p + new Vector3(0.13f, 0.07f, 0.13f), mat, 1.4f);
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
