using System.Numerics;
using Unreal99.Core;
using Unreal99.Rendering;

namespace Unreal99.World;

/// <summary>
/// The architectural vocabulary the arenas are detailed with.
///
/// The layouts here were already researched against the originals, but the geometry was not: every
/// room was a plain box, so a whole arena averaged 2,700 triangles — fewer than a single vehicle in
/// this game carries. That is what made the maps read as blocking volumes rather than as places.
/// The originals are not more complicated in plan; they are covered in trim. Every doorway has a
/// frame, every wall a base and a cornice, every span a girder, every column a base and a capital,
/// and every industrial surface is broken up by panels, ribs, bolts and pipework.
///
/// Everything in this file is non-colliding. Detail is pure decoration: it must never be able to
/// change where a player can stand, which keeps the whole traversal suite valid across a visual
/// pass. Helpers take the same shapes the arenas are already authored in — a min/max box, a run
/// between two points — so they can be layered onto existing geometry without moving it.
/// </summary>
public static partial class Maps
{
    // ---------------------------------------------------------------- surface trim

    /// <summary>
    /// Stepped skirting and cornice around a rectangular room, which is the single cheapest thing
    /// that stops a wall reading as an untextured plane. Pass the room's inner floor bounds.
    /// </summary>
    private static void RoomTrim(LevelBuilder b, Vector3 min, Vector3 max, float ceiling,
        MatId mat, float depth = 0.22f)
    {
        foreach (float y in new[] { min.Y, ceiling })
        {
            bool floorBand = y <= min.Y + 0.01f;
            float y0 = floorBand ? y : y - 0.62f;
            float y1 = floorBand ? y + 0.62f : y;
            // Two courses of slightly different projection so the band has a profile, not a face.
            for (int course = 0; course < 2; course++)
            {
                float d = depth * (course == 0 ? 1f : 0.55f);
                float a = floorBand
                    ? (course == 0 ? y0 : y0 + 0.30f)
                    : (course == 0 ? y0 : y0 + 0.32f);
                float c = floorBand
                    ? (course == 0 ? y0 + 0.30f : y1)
                    : (course == 0 ? y0 + 0.32f : y1);
                b.Decor(new Vector3(min.X - d, a, min.Z - d), new Vector3(max.X + d, c, min.Z + d), mat, 1.6f);
                b.Decor(new Vector3(min.X - d, a, max.Z - d), new Vector3(max.X + d, c, max.Z + d), mat, 1.6f);
                b.Decor(new Vector3(min.X - d, a, min.Z - d), new Vector3(min.X + d, c, max.Z + d), mat, 1.6f);
                b.Decor(new Vector3(max.X - d, a, min.Z - d), new Vector3(max.X + d, c, max.Z + d), mat, 1.6f);
            }
        }
    }

    /// <summary>
    /// Recessed panel grid on one vertical wall face. <paramref name="axis"/> 0 = the wall faces
    /// along X (panels run in Z), 1 = the wall faces along Z. This is the texture-scale detail the
    /// originals get from their trim sheets, rebuilt as actual relief.
    /// </summary>
    private static void WallPanels(LevelBuilder b, Vector3 min, Vector3 max, int axis, MatId mat,
        int columns = 4, int rows = 2, float relief = 0.10f)
    {
        float inset = 0.35f;
        for (int c = 0; c < columns; c++)
            for (int r = 0; r < rows; r++)
            {
                float u0 = MathX.Lerp(axis == 0 ? min.Z : min.X, axis == 0 ? max.Z : max.X,
                    (c + 0.12f) / columns);
                float u1 = MathX.Lerp(axis == 0 ? min.Z : min.X, axis == 0 ? max.Z : max.X,
                    (c + 0.88f) / columns);
                float v0 = MathX.Lerp(min.Y, max.Y, (r + 0.14f) / rows);
                float v1 = MathX.Lerp(min.Y, max.Y, (r + 0.86f) / rows);
                if (u1 - u0 < 0.3f || v1 - v0 < 0.3f) continue;

                // Frame first, then a slightly proud plate inside it: two depths read as a recess.
                if (axis == 0)
                {
                    float x0 = min.X - relief, x1 = min.X + relief;
                    b.Decor(new Vector3(x0, v0, u0), new Vector3(x1, v1, u0 + inset), mat, 1.8f);
                    b.Decor(new Vector3(x0, v0, u1 - inset), new Vector3(x1, v1, u1), mat, 1.8f);
                    b.Decor(new Vector3(x0, v0, u0), new Vector3(x1, v0 + inset, u1), mat, 1.8f);
                    b.Decor(new Vector3(x0, v1 - inset, u0), new Vector3(x1, v1, u1), mat, 1.8f);
                }
                else
                {
                    float z0 = min.Z - relief, z1 = min.Z + relief;
                    b.Decor(new Vector3(u0, v0, z0), new Vector3(u0 + inset, v1, z1), mat, 1.8f);
                    b.Decor(new Vector3(u1 - inset, v0, z0), new Vector3(u1, v1, z1), mat, 1.8f);
                    b.Decor(new Vector3(u0, v0, z0), new Vector3(u1, v0 + inset, z1), mat, 1.8f);
                    b.Decor(new Vector3(u0, v1 - inset, z0), new Vector3(u1, v1, z1), mat, 1.8f);
                }
            }
    }

    /// <summary>Studs along a run, the detail that makes plate read as riveted rather than painted.</summary>
    private static void BoltLine(LevelBuilder b, Vector3 from, Vector3 to, float radius, MatId mat,
        float spacing = 1.6f)
    {
        float length = Vector3.Distance(from, to);
        int count = Math.Max(1, (int)(length / spacing));
        for (int i = 0; i <= count; i++)
            b.Sphere(Vector3.Lerp(from, to, i / (float)count), radius, mat, 4, 6);
    }

    // ---------------------------------------------------------------- structure

    /// <summary>
    /// An I-section girder. Two flanges and a web, which is three boxes doing what one box was
    /// doing before and reading as structure instead of a bar.
    /// </summary>
    private static void Girder(LevelBuilder b, Vector3 from, Vector3 to, float size, MatId mat)
    {
        Vector3 delta = to - from;
        float length = delta.Length();
        if (length < 1e-4f) return;
        Vector3 forward = delta / length;
        Vector3 reference = MathF.Abs(forward.Y) > 0.98f ? MathX.Right : MathX.Up;
        Vector3 side = Vector3.Normalize(Vector3.Cross(reference, forward));
        Vector3 up = Vector3.Cross(forward, side);

        b.DecorBeam(from + up * size, to + up * size, size, size * 0.22f, mat, 1.5f);
        b.DecorBeam(from - up * size, to - up * size, size, size * 0.22f, mat, 1.5f);
        b.DecorBeam(from, to, size * 0.22f, size, mat, 1.5f);
        _ = side;
    }

    /// <summary>
    /// A lattice truss: two chords, verticals, and alternating diagonals. This is the single
    /// highest-value shape for the industrial arenas — the originals hang them over every hall.
    /// </summary>
    private static void Truss(LevelBuilder b, Vector3 from, Vector3 to, float depth, int bays,
        MatId mat, float size = 0.16f)
    {
        Vector3 down = new(0f, -depth, 0f);
        Girder(b, from, to, size, mat);
        Girder(b, from + down, to + down, size, mat);
        for (int i = 0; i <= bays; i++)
        {
            Vector3 p = Vector3.Lerp(from, to, i / (float)bays);
            b.DecorBeam(p, p + down, size * 0.7f, size * 0.7f, mat, 1.5f);
            if (i == bays) continue;
            Vector3 q = Vector3.Lerp(from, to, (i + 1) / (float)bays);
            // Alternating so the lattice zig-zags rather than leaning all one way.
            if ((i & 1) == 0) b.DecorBeam(p + down, q, size * 0.55f, size * 0.55f, mat, 1.5f);
            else b.DecorBeam(p, q + down, size * 0.55f, size * 0.55f, mat, 1.5f);
        }
    }

    /// <summary>
    /// A segmented arch built from voussoirs. <paramref name="axis"/> 0 = the arch spans along X,
    /// 1 = along Z. Gothic and temple arenas live or die on these.
    /// </summary>
    private static void Arch(LevelBuilder b, Vector3 springing, float span, float rise, float depth,
        int axis, MatId mat, int voussoirs = 9, float thickness = 0.34f)
    {
        Vector3 Point(float t)
        {
            float angle = MathF.PI * t;
            float off = -MathF.Cos(angle) * span * 0.5f;
            float y = MathF.Sin(angle) * rise;
            return springing + (axis == 0 ? new Vector3(off, y, 0f) : new Vector3(0f, y, off));
        }

        // DecorBeam's width axis is the horizontal perpendicular to the run, which for an arch
        // curving in a vertical plane is the arch's depth on either axis.
        for (int i = 0; i < voussoirs; i++)
        {
            Vector3 a = Point(i / (float)voussoirs);
            Vector3 c = Point((i + 1) / (float)voussoirs);
            b.DecorBeam(a, c, depth, thickness, mat, 1.5f);
        }
    }

    /// <summary>Square pier with a stepped base and capital: the workhorse of the stone arenas.</summary>
    private static void Pier(LevelBuilder b, Vector3 at, float height, float width, MatId shaft,
        MatId trim)
    {
        float w = width * 0.5f;
        b.Decor(at + new Vector3(-w * 1.34f, 0f, -w * 1.34f), at + new Vector3(w * 1.34f, 0.28f, w * 1.34f), trim, 1.4f);
        b.Decor(at + new Vector3(-w * 1.16f, 0.28f, -w * 1.16f), at + new Vector3(w * 1.16f, 0.56f, w * 1.16f), trim, 1.4f);
        b.Decor(at + new Vector3(-w, 0.56f, -w), at + new Vector3(w, height - 0.56f, w), shaft, 1.2f);
        // Shallow flutes on all four faces.
        for (int i = 0; i < 3; i++)
        {
            float o = MathX.Lerp(-w * 0.55f, w * 0.55f, i / 2f);
            b.Decor(at + new Vector3(o - 0.05f, 0.62f, -w - 0.05f), at + new Vector3(o + 0.05f, height - 0.62f, -w + 0.03f), trim, 1.4f);
            b.Decor(at + new Vector3(o - 0.05f, 0.62f, w - 0.03f), at + new Vector3(o + 0.05f, height - 0.62f, w + 0.05f), trim, 1.4f);
            b.Decor(at + new Vector3(-w - 0.05f, 0.62f, o - 0.05f), at + new Vector3(-w + 0.03f, height - 0.62f, o + 0.05f), trim, 1.4f);
            b.Decor(at + new Vector3(w - 0.03f, 0.62f, o - 0.05f), at + new Vector3(w + 0.05f, height - 0.62f, o + 0.05f), trim, 1.4f);
        }
        b.Decor(at + new Vector3(-w * 1.16f, height - 0.56f, -w * 1.16f), at + new Vector3(w * 1.16f, height - 0.28f, w * 1.16f), trim, 1.4f);
        b.Decor(at + new Vector3(-w * 1.34f, height - 0.28f, -w * 1.34f), at + new Vector3(w * 1.34f, height, w * 1.34f), trim, 1.4f);
    }

    /// <summary>Round fluted column with a moulded base and capital.</summary>
    private static void Column(LevelBuilder b, Vector3 at, float height, float radius, MatId shaft,
        MatId trim, int flutes = 12)
    {
        b.Cylinder(at + new Vector3(0f, 0.16f, 0f), radius * 1.42f, radius * 1.24f, 0.32f, 14, trim);
        b.Torus(at + new Vector3(0f, 0.40f, 0f), radius * 1.12f, 0.11f, trim, 16, 6);
        b.Prism(at + new Vector3(0f, height * 0.5f, 0f), radius, height - 1f, flutes, shaft, false);
        b.Torus(at + new Vector3(0f, height - 0.52f, 0f), radius * 1.12f, 0.11f, trim, 16, 6);
        b.Cylinder(at + new Vector3(0f, height - 0.34f, 0f), radius * 1.24f, radius * 1.5f, 0.34f, 14, trim);
        b.Decor(at + new Vector3(-radius * 1.62f, height - 0.18f, -radius * 1.62f),
                at + new Vector3(radius * 1.62f, height, radius * 1.62f), trim, 1.4f);
    }

    /// <summary>A raked buttress: stepped pier plus the angled brace that leans into the wall.</summary>
    private static void Buttress(LevelBuilder b, Vector3 foot, float height, Vector3 reach, MatId mat)
    {
        Vector3 outer = foot + reach;
        b.Decor(outer + new Vector3(-0.55f, 0f, -0.55f), outer + new Vector3(0.55f, height * 0.45f, 0.55f), mat, 1.3f);
        b.Decor(outer + new Vector3(-0.42f, height * 0.45f, -0.42f), outer + new Vector3(0.42f, height * 0.62f, 0.42f), mat, 1.3f);
        b.DecorBeam(outer + new Vector3(0f, height * 0.62f, 0f), foot + new Vector3(0f, height, 0f),
            0.28f, 0.34f, mat, 1.3f);
        b.Decor(outer + new Vector3(-0.30f, height * 0.62f, -0.30f), outer + new Vector3(0.30f, height * 0.72f, 0.30f), mat, 1.3f);
    }

    // ---------------------------------------------------------------- services and fittings

    /// <summary>Pipe run with flange collars, the industrial arenas' connective tissue.</summary>
    private static void Pipe(LevelBuilder b, Vector3 from, Vector3 to, float radius, MatId mat,
        float collarSpacing = 5f)
    {
        Vector3 delta = to - from;
        float length = delta.Length();
        if (length < 1e-4f) return;
        b.DecorBeam(from, to, radius, radius, mat, 1.2f);
        b.DecorBeam(from, to, radius * 0.72f, radius * 1.28f, mat, 1.2f);
        b.DecorBeam(from, to, radius * 1.28f, radius * 0.72f, mat, 1.2f);
        int collars = Math.Max(1, (int)(length / collarSpacing));
        for (int i = 0; i <= collars; i++)
        {
            Vector3 p = Vector3.Lerp(from, to, i / (float)collars);
            b.Sphere(p, radius * 1.5f, mat, 5, 8);
        }
    }

    /// <summary>Louvred vent panel set into a wall face.</summary>
    private static void Louvres(LevelBuilder b, Vector3 min, Vector3 max, MatId mat, int blades = 6)
    {
        b.Decor(min, new Vector3(max.X, max.Y, min.Z + (max.Z - min.Z) * 0.18f), mat, 1.6f);
        for (int i = 0; i < blades; i++)
        {
            float y0 = MathX.Lerp(min.Y, max.Y, (i + 0.15f) / blades);
            float y1 = MathX.Lerp(min.Y, max.Y, (i + 0.72f) / blades);
            b.Decor(new Vector3(min.X + 0.10f, y0, min.Z), new Vector3(max.X - 0.10f, y1, max.Z), mat, 1.6f);
        }
    }

    /// <summary>
    /// Wall-mounted lamp on a bracket. The ceiling equivalent lives on
    /// <see cref="LevelBuilder.CeilingLamp"/>, which every arena already calls.
    /// </summary>
    private static void WallLamp(LevelBuilder b, Vector3 at, int axis, MatId housing, Vector3 tint,
        float range = 13f, float intensity = 3.2f)
    {
        Vector3 out0 = axis == 0 ? new Vector3(0.5f, 0f, 0f) : new Vector3(0f, 0f, 0.5f);
        Vector3 wide = axis == 0 ? new Vector3(0f, 0f, 0.42f) : new Vector3(0.42f, 0f, 0f);
        b.Decor(at - wide * 0.7f - new Vector3(0.1f, 0.5f, 0.1f),
                at + wide * 0.7f + new Vector3(0.1f, 0.5f, 0.1f), housing, 1.4f);
        b.DecorBeam(at, at + out0, 0.09f, 0.09f, housing, 1.4f);
        b.Decor(at + out0 - wide - new Vector3(0.12f, 0.26f, 0.12f),
                at + out0 + wide + new Vector3(0.12f, 0.10f, 0.12f), housing, 1.4f);
        b.Decor(at + out0 - wide * 0.82f - new Vector3(0.08f, 0.20f, 0.08f),
                at + out0 + wide * 0.82f + new Vector3(0.08f, 0.02f, 0.08f), MatId.EnergyPanel, 1.4f);
        b.AddLight(at + out0 - new Vector3(0f, 0.35f, 0f), tint, range, intensity);
    }

    /// <summary>Framed and mullioned window with a glass pane behind it.</summary>
    private static void Window(LevelBuilder b, Vector3 min, Vector3 max, int axis, MatId frame,
        int mullions = 2)
    {
        float t = 0.18f;
        if (axis == 0)
        {
            b.Decor(new Vector3(min.X - t, min.Y, min.Z), new Vector3(max.X + t, min.Y + 0.3f, max.Z), frame, 1.5f);
            b.Decor(new Vector3(min.X - t, max.Y - 0.3f, min.Z), new Vector3(max.X + t, max.Y, max.Z), frame, 1.5f);
            b.Decor(new Vector3(min.X - t, min.Y, min.Z), new Vector3(max.X + t, max.Y, min.Z + 0.3f), frame, 1.5f);
            b.Decor(new Vector3(min.X - t, min.Y, max.Z - 0.3f), new Vector3(max.X + t, max.Y, max.Z), frame, 1.5f);
            for (int i = 1; i <= mullions; i++)
            {
                float z = MathX.Lerp(min.Z, max.Z, i / (float)(mullions + 1));
                b.Decor(new Vector3(min.X - t * 0.6f, min.Y, z - 0.10f), new Vector3(max.X + t * 0.6f, max.Y, z + 0.10f), frame, 1.5f);
            }
            b.Decor(new Vector3((min.X + max.X) * 0.5f - 0.05f, min.Y + 0.3f, min.Z + 0.3f),
                    new Vector3((min.X + max.X) * 0.5f + 0.05f, max.Y - 0.3f, max.Z - 0.3f), MatId.Glass, 1.5f);
        }
        else
        {
            b.Decor(new Vector3(min.X, min.Y, min.Z - t), new Vector3(max.X, min.Y + 0.3f, max.Z + t), frame, 1.5f);
            b.Decor(new Vector3(min.X, max.Y - 0.3f, min.Z - t), new Vector3(max.X, max.Y, max.Z + t), frame, 1.5f);
            b.Decor(new Vector3(min.X, min.Y, min.Z - t), new Vector3(min.X + 0.3f, max.Y, max.Z + t), frame, 1.5f);
            b.Decor(new Vector3(max.X - 0.3f, min.Y, min.Z - t), new Vector3(max.X, max.Y, max.Z + t), frame, 1.5f);
            for (int i = 1; i <= mullions; i++)
            {
                float x = MathX.Lerp(min.X, max.X, i / (float)(mullions + 1));
                b.Decor(new Vector3(x - 0.10f, min.Y, min.Z - t * 0.6f), new Vector3(x + 0.10f, max.Y, max.Z + t * 0.6f), frame, 1.5f);
            }
            b.Decor(new Vector3(min.X + 0.3f, min.Y + 0.3f, (min.Z + max.Z) * 0.5f - 0.05f),
                    new Vector3(max.X - 0.3f, max.Y - 0.3f, (min.Z + max.Z) * 0.5f + 0.05f), MatId.Glass, 1.5f);
        }
    }

    /// <summary>
    /// Doorway surround. The originals frame every opening; an unframed hole in a wall is the
    /// clearest single tell that a room was built from raw brushes.
    /// </summary>
    private static void DoorFrame(LevelBuilder b, Vector3 min, Vector3 max, int axis, MatId mat)
    {
        float t = 0.30f, p = 0.16f;
        if (axis == 0)
        {
            b.Decor(new Vector3(min.X - p, min.Y, min.Z - t), new Vector3(max.X + p, max.Y + t, min.Z), mat, 1.5f);
            b.Decor(new Vector3(min.X - p, min.Y, max.Z), new Vector3(max.X + p, max.Y + t, max.Z + t), mat, 1.5f);
            b.Decor(new Vector3(min.X - p, max.Y, min.Z - t), new Vector3(max.X + p, max.Y + t, max.Z + t), mat, 1.5f);
            b.Decor(new Vector3(min.X - p * 1.6f, max.Y + t, min.Z - t * 1.3f),
                    new Vector3(max.X + p * 1.6f, max.Y + t * 1.6f, max.Z + t * 1.3f), mat, 1.5f);
        }
        else
        {
            b.Decor(new Vector3(min.X - t, min.Y, min.Z - p), new Vector3(min.X, max.Y + t, max.Z + p), mat, 1.5f);
            b.Decor(new Vector3(max.X, min.Y, min.Z - p), new Vector3(max.X + t, max.Y + t, max.Z + p), mat, 1.5f);
            b.Decor(new Vector3(min.X - t, max.Y, min.Z - p), new Vector3(max.X + t, max.Y + t, max.Z + p), mat, 1.5f);
            b.Decor(new Vector3(min.X - t * 1.3f, max.Y + t, min.Z - p * 1.6f),
                    new Vector3(max.X + t * 1.3f, max.Y + t * 1.6f, max.Z + p * 1.6f), mat, 1.5f);
        }
    }

    /// <summary>
    /// Industrial crate with corner brackets and a banded face. Defaults to non-colliding so a
    /// dressing pass cannot alter where anyone can walk; pass <paramref name="collide"/> only when
    /// the crate is meant to be cover the layout was designed around.
    /// </summary>
    private static void Crate(LevelBuilder b, Vector3 at, float size, MatId body, MatId trim,
        bool collide = false)
    {
        float h = size * 0.5f;
        b.Solid(at + new Vector3(-h, 0f, -h), at + new Vector3(h, size, h), body, collide, 1.3f);
        float e = 0.09f;
        foreach (float sx in new[] { -1f, 1f })
            foreach (float sz in new[] { -1f, 1f })
                b.Decor(at + new Vector3(sx * h - e, 0f, sz * h - e),
                        at + new Vector3(sx * h + e, size, sz * h + e), trim, 1.5f);
        foreach (float y in new[] { size * 0.28f, size * 0.72f })
        {
            b.Decor(at + new Vector3(-h - e, y - 0.06f, -h - e), at + new Vector3(h + e, y + 0.06f, h + e), trim, 1.5f);
        }
        b.Decor(at + new Vector3(-h * 0.55f, size - 0.05f, -h * 0.55f),
                at + new Vector3(h * 0.55f, size + 0.06f, h * 0.55f), trim, 1.5f);
    }

    /// <summary>Barrel: a ribbed drum, the other half of every industrial arena's set dressing.</summary>
    private static void Barrel(LevelBuilder b, Vector3 at, MatId mat, float height = 1.15f,
        float radius = 0.42f)
    {
        b.Prism(at + new Vector3(0f, height * 0.5f, 0f), radius, height, 12, mat, false);
        foreach (float f in new[] { 0.22f, 0.5f, 0.78f })
            b.Torus(at + new Vector3(0f, height * f, 0f), radius * 1.04f, 0.055f, mat, 12, 5);
        b.Cylinder(at + new Vector3(0f, height, 0f), radius * 0.94f, radius * 0.8f, 0.07f, 12, mat);
    }

    // ---------------------------------------------------------------- whole-hall dressing
    //
    // Each of these takes an arena's shell — floor corner to ceiling corner — and fills in the
    // storey-scale detail that theme implies. Per-map passes then add only what is specific to
    // that arena. All non-colliding, so a dressing pass is always safe to apply.

    /// <summary>Refinery and deck interiors: roof trusses, panelled bulkheads, pipe runs, vents.</summary>
    private static void DressIndustrial(LevelBuilder b, Vector3 min, Vector3 max, MatId structure,
        MatId panel, int trusses = 4, float pipeHeight = 0.72f)
    {
        float ceiling = max.Y;
        bool longAlongX = (max.X - min.X) >= (max.Z - min.Z);

        for (int i = 0; i < trusses; i++)
        {
            float t = MathX.Lerp(0.5f / trusses, 1f - 0.5f / trusses, trusses == 1 ? 0.5f : i / (float)(trusses - 1));
            if (longAlongX)
            {
                float z = MathX.Lerp(min.Z, max.Z, t);
                Truss(b, new Vector3(min.X, ceiling - 1.2f, z), new Vector3(max.X, ceiling - 1.2f, z),
                    1.4f, Math.Max(4, (int)((max.X - min.X) / 5.5f)), structure, 0.15f);
            }
            else
            {
                float x = MathX.Lerp(min.X, max.X, t);
                Truss(b, new Vector3(x, ceiling - 1.2f, min.Z), new Vector3(x, ceiling - 1.2f, max.Z),
                    1.4f, Math.Max(4, (int)((max.Z - min.Z) / 5.5f)), structure, 0.15f);
            }
        }

        float py = MathX.Lerp(min.Y, ceiling, pipeHeight);
        foreach (int s in new[] { -1, 1 })
        {
            float x = s < 0 ? min.X : max.X;
            float z = s < 0 ? min.Z : max.Z;
            Pipe(b, new Vector3(x - s * 0.9f, py, min.Z), new Vector3(x - s * 0.9f, py, max.Z), 0.26f, structure);
            Pipe(b, new Vector3(min.X, py - 0.95f, z - s * 0.9f), new Vector3(max.X, py - 0.95f, z - s * 0.9f), 0.20f, MatId.Trim);
            WallPanels(b, new Vector3(x, min.Y + 0.5f, min.Z + 1.5f), new Vector3(x, py - 2f, max.Z - 1.5f), 0, panel, 8, 1);
            WallPanels(b, new Vector3(min.X + 1.5f, min.Y + 0.5f, z), new Vector3(max.X - 1.5f, py - 2f, z), 1, panel, 8, 1);
            for (int i = 0; i < 3; i++)
            {
                float u = MathX.Lerp(min.Z, max.Z, (i + 0.5f) / 3f);
                Louvres(b, new Vector3(x - s * 0.42f, min.Y + 2.6f, u - 1.5f),
                        new Vector3(x - s * 0.06f, min.Y + 5.0f, u + 1.5f), structure, 6);
            }
        }
    }

    /// <summary>Stone arenas: a pilaster rhythm, string course and cornice around the shell.</summary>
    private static void DressStone(LevelBuilder b, Vector3 min, Vector3 max, MatId stone, MatId trim,
        int bays = 6, bool arcade = false)
    {
        float top = max.Y;
        RoomTrim(b, new Vector3(min.X, min.Y, min.Z), new Vector3(max.X, min.Y, max.Z), top, trim, 0.30f);

        for (int i = 0; i <= bays; i++)
        {
            float x = MathX.Lerp(min.X, max.X, i / (float)bays);
            float z = MathX.Lerp(min.Z, max.Z, i / (float)bays);
            foreach (int s in new[] { -1, 1 })
            {
                float wz = s < 0 ? min.Z : max.Z;
                float wx = s < 0 ? min.X : max.X;
                Pier(b, new Vector3(x, min.Y, wz - s * 0.55f), top - min.Y, 1.5f, stone, trim);
                Pier(b, new Vector3(wx - s * 0.55f, min.Y, z), top - min.Y, 1.5f, stone, trim);
                if (!arcade || i >= bays) continue;
                float xm = MathX.Lerp(min.X, max.X, (i + 0.5f) / bays);
                float zm = MathX.Lerp(min.Z, max.Z, (i + 0.5f) / bays);
                float span = (max.X - min.X) / bays;
                Arch(b, new Vector3(xm, min.Y + (top - min.Y) * 0.62f, wz - s * 0.55f),
                    span, span * 0.28f, 0.7f, 0, stone, 7, 0.3f);
                Arch(b, new Vector3(wx - s * 0.55f, min.Y + (top - min.Y) * 0.62f, zm),
                    (max.Z - min.Z) / bays, (max.Z - min.Z) / bays * 0.28f, 0.7f, 1, stone, 7, 0.3f);
            }
        }
    }

    /// <summary>Ship and station interiors: rib frames, conduit runs and panelled hull plate.</summary>
    private static void DressHull(LevelBuilder b, Vector3 min, Vector3 max, MatId hull, MatId trim,
        int ribs = 6)
    {
        bool longAlongX = (max.X - min.X) >= (max.Z - min.Z);
        for (int i = 0; i <= ribs; i++)
        {
            float t = i / (float)ribs;
            if (longAlongX)
            {
                float x = MathX.Lerp(min.X, max.X, t);
                b.Decor(new Vector3(x - 0.24f, min.Y, min.Z), new Vector3(x + 0.24f, max.Y, min.Z + 0.42f), hull, 1.4f);
                b.Decor(new Vector3(x - 0.24f, min.Y, max.Z - 0.42f), new Vector3(x + 0.24f, max.Y, max.Z), hull, 1.4f);
                b.Decor(new Vector3(x - 0.24f, max.Y - 0.42f, min.Z), new Vector3(x + 0.24f, max.Y, max.Z), hull, 1.4f);
                Arch(b, new Vector3(x, max.Y - (max.Z - min.Z) * 0.16f, (min.Z + max.Z) * 0.5f),
                    max.Z - min.Z, (max.Z - min.Z) * 0.16f, 0.22f, 1, hull, 7, 0.24f);
            }
            else
            {
                float z = MathX.Lerp(min.Z, max.Z, t);
                b.Decor(new Vector3(min.X, min.Y, z - 0.24f), new Vector3(min.X + 0.42f, max.Y, z + 0.24f), hull, 1.4f);
                b.Decor(new Vector3(max.X - 0.42f, min.Y, z - 0.24f), new Vector3(max.X, max.Y, z + 0.24f), hull, 1.4f);
                b.Decor(new Vector3(min.X, max.Y - 0.42f, z - 0.24f), new Vector3(max.X, max.Y, z + 0.24f), hull, 1.4f);
                Arch(b, new Vector3((min.X + max.X) * 0.5f, max.Y - (max.X - min.X) * 0.16f, z),
                    max.X - min.X, (max.X - min.X) * 0.16f, 0.22f, 0, hull, 7, 0.24f);
            }
        }
        foreach (int s in new[] { -1, 1 })
        {
            float x = s < 0 ? min.X : max.X;
            float z = s < 0 ? min.Z : max.Z;
            Pipe(b, new Vector3(x - s * 0.7f, min.Y + 1.9f, min.Z), new Vector3(x - s * 0.7f, min.Y + 1.9f, max.Z), 0.15f, trim, 6f);
            Pipe(b, new Vector3(min.X, min.Y + 1.9f, z - s * 0.7f), new Vector3(max.X, min.Y + 1.9f, z - s * 0.7f), 0.15f, trim, 6f);
        }
    }

    /// <summary>
    /// Open vehicle arenas, where the shell is a valley wall rather than a room. Cuts strata
    /// ledges into the perimeter so the cliffs have relief, then runs a line of power pylons
    /// around the field — the Onslaught arenas are about an energy grid, and the originals show it.
    /// </summary>
    private static void DressOutdoor(LevelBuilder b, float halfX, float halfZ, float ground,
        float wallTop, MatId rock, MatId trim, int pylons = 6)
    {
        // Strata: staggered ledges up the inside face of the perimeter, so a 60 m wall is not
        // one flat plane of rock.
        for (int band = 0; band < 5; band++)
        {
            float y = MathX.Lerp(ground + 2f, wallTop * 0.85f, band / 4f);
            float depth = 1.5f + (band % 2) * 1.1f;
            float h = 1.1f + (band % 3) * 0.5f;
            foreach (int s in new[] { -1, 1 })
            {
                b.Decor(new Vector3(-halfX, y, s * halfZ - s * depth), new Vector3(halfX, y + h, s * halfZ), rock, 0.5f);
                b.Decor(new Vector3(s * halfX - s * depth, y, -halfZ), new Vector3(s * halfX, y + h, halfZ), rock, 0.5f);
            }
        }

        // Buttressing spurs, so the corners are not simply two planes meeting.
        for (int i = 0; i <= pylons; i++)
        {
            float x = MathX.Lerp(-halfX, halfX, i / (float)pylons);
            float z = MathX.Lerp(-halfZ, halfZ, i / (float)pylons);
            foreach (int s in new[] { -1, 1 })
            {
                b.Decor(new Vector3(x - 2.6f, ground, s * halfZ - s * 3.4f), new Vector3(x + 2.6f, wallTop * 0.5f, s * halfZ), rock, 0.5f);
                b.Decor(new Vector3(s * halfX - s * 3.4f, ground, z - 2.6f), new Vector3(s * halfX, wallTop * 0.5f, z + 2.6f), rock, 0.5f);
            }
        }

        // The grid itself: lattice masts with cross-arms and a catenary of cable between them.
        float mastY = ground + 15f;
        for (int i = 0; i < pylons; i++)
        {
            float t = (i + 0.5f) / pylons;
            foreach (int s in new[] { -1, 1 })
            {
                Vector3 at = new(MathX.Lerp(-halfX + 10f, halfX - 10f, t), ground, s * (halfZ - 7f));
                Truss(b, at, at + new Vector3(0f, mastY - ground, 0f), 1.5f, 6, trim, 0.19f);
                for (int arm = 0; arm < 2; arm++)
                {
                    float ay = mastY - arm * 3.2f;
                    b.DecorBeam(at + new Vector3(-4.2f, ay, 0f), at + new Vector3(4.2f, ay, 0f), 0.18f, 0.18f, trim, 1.4f);
                    b.DecorBeam(at + new Vector3(-4.2f, ay, 0f), at + new Vector3(0f, ay - 2.4f, 0f), 0.11f, 0.11f, trim, 1.4f);
                    b.DecorBeam(at + new Vector3(4.2f, ay, 0f), at + new Vector3(0f, ay - 2.4f, 0f), 0.11f, 0.11f, trim, 1.4f);
                    foreach (float ex in new[] { -4.2f, 4.2f })
                        b.Decor(at + new Vector3(ex - 0.16f, ay - 0.75f, -0.16f), at + new Vector3(ex + 0.16f, ay, 0.16f), trim, 1.4f);
                }
            }
        }
    }

    /// <summary>Hanging team banner with a rail and a weighted hem.</summary>
    private static void Banner(LevelBuilder b, Vector3 top, float width, float drop, int axis, MatId mat)
    {
        float w = width * 0.5f;
        if (axis == 0)
        {
            b.Decor(top + new Vector3(-0.09f, -0.12f, -w * 1.1f), top + new Vector3(0.09f, 0.06f, w * 1.1f), MatId.Trim, 1.4f);
            b.Decor(top + new Vector3(-0.05f, -drop, -w), top + new Vector3(0.05f, -0.10f, w), mat, 1.2f);
            b.Decor(top + new Vector3(-0.08f, -drop - 0.14f, -w), top + new Vector3(0.08f, -drop, w), MatId.Trim, 1.4f);
        }
        else
        {
            b.Decor(top + new Vector3(-w * 1.1f, -0.12f, -0.09f), top + new Vector3(w * 1.1f, 0.06f, 0.09f), MatId.Trim, 1.4f);
            b.Decor(top + new Vector3(-w, -drop, -0.05f), top + new Vector3(w, -0.10f, 0.05f), mat, 1.2f);
            b.Decor(top + new Vector3(-w, -drop - 0.14f, -0.08f), top + new Vector3(w, -drop, 0.08f), MatId.Trim, 1.4f);
        }
    }
}
