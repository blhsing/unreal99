using System.Numerics;
using Unreal99.Core;

namespace Unreal99.Rendering;

/// <summary>
/// Closed 2D cross-sections for <see cref="MeshBuilder.AddLoft"/>, plus the shapes that are
/// nothing but a loft with a particular section and station list.
///
/// Everything here exists to get away from axis-aligned boxes. A box has eight vertices and reads
/// as a crate from every angle; the same volume swept as a rounded-rectangle section with a
/// tapering station list reads as a hull, a fairing or a receiver, and costs a few hundred
/// triangles — which at these object counts is free.
///
/// Sections are wound counter-clockwise in their own XY plane so lofted normals point outward.
/// </summary>
public static class Sections
{
    /// <summary>Rectangle with rounded corners: the workhorse for hulls, receivers and fairings.</summary>
    public static Vector2[] RoundedRect(float halfWidth, float halfHeight, float radius, int cornerSteps = 3)
    {
        radius = MathF.Min(radius, MathF.Min(halfWidth, halfHeight) * 0.999f);
        cornerSteps = Math.Max(1, cornerSteps);
        float ix = halfWidth - radius, iy = halfHeight - radius;

        var pts = new List<Vector2>((cornerSteps + 1) * 4);
        // Corner centres, counter-clockwise from +X+Y.
        ReadOnlySpan<Vector2> centres =
        [
            new(ix, iy), new(-ix, iy), new(-ix, -iy), new(ix, -iy),
        ];
        for (int c = 0; c < 4; c++)
        {
            float start = c * MathX.HalfPi;
            for (int s = 0; s <= cornerSteps; s++)
            {
                float a = start + s / (float)cornerSteps * MathX.HalfPi;
                pts.Add(centres[c] + new Vector2(MathF.Cos(a), MathF.Sin(a)) * radius);
            }
        }
        return Dedupe(pts);
    }

    public static Vector2[] Ellipse(float radiusX, float radiusY, int segments = 16)
    {
        segments = Math.Max(3, segments);
        var pts = new Vector2[segments];
        for (int i = 0; i < segments; i++)
        {
            float a = i / (float)segments * MathX.TwoPi;
            pts[i] = new Vector2(MathF.Cos(a) * radiusX, MathF.Sin(a) * radiusY);
        }
        return pts;
    }

    public static Vector2[] Circle(float radius, int segments = 16) => Ellipse(radius, radius, segments);

    /// <summary>
    /// Superellipse — |x/a|^n + |y/b|^n = 1. Exponent 2 is an ellipse, 4–8 gives the "soft box"
    /// that armour plate reads as, and very high exponents approach a rectangle. This is what
    /// makes a tank hull look milled rather than assembled from crates.
    /// </summary>
    public static Vector2[] Superellipse(float radiusX, float radiusY, float exponent, int segments = 20)
    {
        segments = Math.Max(4, segments);
        float e = 2f / MathF.Max(exponent, 0.2f);
        var pts = new Vector2[segments];
        for (int i = 0; i < segments; i++)
        {
            float a = i / (float)segments * MathX.TwoPi;
            float c = MathF.Cos(a), s = MathF.Sin(a);
            pts[i] = new Vector2(
                MathF.Sign(c) * MathF.Pow(MathF.Abs(c), e) * radiusX,
                MathF.Sign(s) * MathF.Pow(MathF.Abs(s), e) * radiusY);
        }
        return pts;
    }

    /// <summary>
    /// Trapezoid with rounded corners: wider at the bottom than the top, which is the shape of a
    /// vehicle hull with sloped side armour seen head-on.
    /// </summary>
    public static Vector2[] Keel(float halfBottom, float halfTop, float halfHeight, float radius,
        int cornerSteps = 2)
    {
        var raw = new Vector2[]
        {
            new(halfTop, halfHeight), new(-halfTop, halfHeight),
            new(-halfBottom, -halfHeight), new(halfBottom, -halfHeight),
        };
        return Fillet(raw, radius, cornerSteps);
    }

    /// <summary>Regular polygon, flat side down when <paramref name="rotation"/> is zero.</summary>
    public static Vector2[] Polygon(float radius, int sides, float rotation = 0f)
    {
        sides = Math.Max(3, sides);
        var pts = new Vector2[sides];
        for (int i = 0; i < sides; i++)
        {
            float a = rotation + i / (float)sides * MathX.TwoPi;
            pts[i] = new Vector2(MathF.Cos(a) * radius, MathF.Sin(a) * radius);
        }
        return pts;
    }

    /// <summary>
    /// A blade profile: sharp at +X, thick at -X, symmetric about the X axis. Wings, fins,
    /// rotor blades and the Necris hulls' edges all use this.
    /// </summary>
    public static Vector2[] Airfoil(float chord, float thickness, int steps = 8)
    {
        steps = Math.Max(3, steps);
        var pts = new List<Vector2>(steps * 2);
        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps;
            float x = (0.5f - t) * chord;
            // Classic 4-digit-ish thickness distribution, blunt at the root, sharp at the tip.
            float u = 1f - t;
            float y = thickness * (1.4845f * MathF.Sqrt(u) - 0.63f * u - 1.758f * u * u
                                   + 1.4215f * u * u * u - 0.5075f * u * u * u * u);
            pts.Add(new Vector2(x, y));
        }
        for (int i = steps - 1; i >= 1; i--)
        {
            float t = i / (float)steps;
            float x = (0.5f - t) * chord;
            float u = 1f - t;
            float y = thickness * (1.4845f * MathF.Sqrt(u) - 0.63f * u - 1.758f * u * u
                                   + 1.4215f * u * u * u - 0.5075f * u * u * u * u);
            pts.Add(new Vector2(x, -y));
        }
        return Dedupe(pts);
    }

    /// <summary>Rounds every corner of an arbitrary convex-ish outline.</summary>
    public static Vector2[] Fillet(ReadOnlySpan<Vector2> outline, float radius, int steps = 2)
    {
        if (outline.Length < 3 || radius <= 1e-4f) return outline.ToArray();
        steps = Math.Max(1, steps);
        var pts = new List<Vector2>(outline.Length * (steps + 1));
        int n = outline.Length;
        for (int i = 0; i < n; i++)
        {
            Vector2 prev = outline[(i + n - 1) % n], cur = outline[i], next = outline[(i + 1) % n];
            Vector2 toPrev = prev - cur, toNext = next - cur;
            float lp = toPrev.Length(), ln = toNext.Length();
            if (lp < 1e-5f || ln < 1e-5f) { pts.Add(cur); continue; }
            toPrev /= lp; toNext /= ln;
            float r = MathF.Min(radius, MathF.Min(lp, ln) * 0.49f);
            // Distance from the corner to each tangent point for the inscribed arc.
            float half = MathF.Acos(MathX.Clamp(Vector2.Dot(toPrev, toNext), -1f, 1f)) * 0.5f;
            float tanLen = r / MathF.Max(MathF.Tan(half), 1e-3f);
            tanLen = MathF.Min(tanLen, MathF.Min(lp, ln) * 0.49f);
            Vector2 a = cur + toPrev * tanLen, b = cur + toNext * tanLen;
            for (int s = 0; s <= steps; s++)
            {
                float t = s / (float)steps;
                // Quadratic Bezier through the corner approximates the arc closely enough here.
                Vector2 p = a * ((1 - t) * (1 - t)) + cur * (2 * (1 - t) * t) + b * (t * t);
                pts.Add(p);
            }
        }
        return Dedupe(pts);
    }

    private static Vector2[] Dedupe(List<Vector2> pts)
    {
        var outPts = new List<Vector2>(pts.Count);
        foreach (var p in pts)
            if (outPts.Count == 0 || Vector2.DistanceSquared(outPts[^1], p) > 1e-8f) outPts.Add(p);
        if (outPts.Count > 1 && Vector2.DistanceSquared(outPts[0], outPts[^1]) < 1e-8f)
            outPts.RemoveAt(outPts.Count - 1);
        return outPts.ToArray();
    }
}

/// <summary>
/// Composite shapes assembled from lofts and lathes. These are the parts that recur across
/// vehicles and weapons — a rounded body, a wheel, a gun barrel — factored out so every model
/// gets the same quality instead of each one reinventing a box.
/// </summary>
public static class Shapes
{
    /// <summary>
    /// A box with every edge rounded off. The single highest-value replacement for
    /// <see cref="MeshBuilder.AddBox"/>: same call shape, but the silhouette catches light along
    /// its edges instead of terminating in a hard corner.
    /// </summary>
    public static void RoundedBox(MeshBuilder mb, Vector3 center, Vector3 half, float radius,
        int cornerSteps = 3, int endSteps = 3, uint? color = null)
    {
        radius = MathF.Min(radius, MathF.Min(half.X, MathF.Min(half.Y, half.Z)) * 0.98f);
        var section = Sections.RoundedRect(half.X, half.Y, radius, cornerSteps);

        // Stations run along Z. The end zones follow a circular shoulder so the cap is a dome of
        // the same radius as the side fillets, which is what makes it read as one milled solid.
        var stations = new List<MeshBuilder.LoftStation>(endSteps * 2 + 2);
        float flat = MathF.Max(0f, half.Z - radius);
        for (int i = endSteps; i >= 0; i--)
        {
            float a = i / (float)endSteps * MathX.HalfPi;
            float z = -flat - MathF.Sin(a) * radius;
            float k = ScaleAtShoulder(MathF.Cos(a), half, radius);
            stations.Add(new MeshBuilder.LoftStation(center + new Vector3(0, 0, z), k));
        }
        for (int i = 0; i <= endSteps; i++)
        {
            float a = i / (float)endSteps * MathX.HalfPi;
            float z = flat + MathF.Sin(a) * radius;
            float k = ScaleAtShoulder(MathF.Cos(a), half, radius);
            stations.Add(new MeshBuilder.LoftStation(center + new Vector3(0, 0, z), k));
        }
        mb.AddLoft(section, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(stations),
            capStart: false, capEnd: false, color);
    }

    /// <summary>
    /// Section scale at a rounded end, where the shoulder has pulled in by <c>radius*(1-cos)</c>.
    /// Scaling the whole section uniformly would shrink the fillet too; this keeps the flat part
    /// of the face shrinking at the right rate for the corner radius to stay constant.
    /// </summary>
    private static float ScaleAtShoulder(float cos, Vector3 half, float radius)
    {
        float inset = radius * (1f - cos);
        float sx = MathF.Max(0f, half.X - inset) / MathF.Max(half.X, 1e-4f);
        float sy = MathF.Max(0f, half.Y - inset) / MathF.Max(half.Y, 1e-4f);
        return MathF.Min(sx, sy);
    }

    /// <summary>
    /// A body swept along -Z (forward) through the given waist half-widths. Each entry is
    /// (z, halfWidth, halfHeight, yOffset), letting a hull pinch at the waist, rise over the
    /// engine deck and taper to a nose in one shape rather than three stacked boxes.
    /// </summary>
    public static void Hull(MeshBuilder mb, Vector3 origin, ReadOnlySpan<Vector4> stations,
        float cornerRadius = 0.18f, int cornerSteps = 3, uint? color = null)
        => Hull(mb, origin, stations, Sections.RoundedRect(1f, 1f, cornerRadius, cornerSteps), color);

    /// <summary>
    /// Hull sweep with an explicit cross-section, so a vehicle can pick its own character: a
    /// chined <see cref="Sections.Keel"/> for sloped armour, a <see cref="Sections.Superellipse"/>
    /// for a milled fairing, a rounded rectangle for a utility body.
    /// </summary>
    public static void Hull(MeshBuilder mb, Vector3 origin, ReadOnlySpan<Vector4> stations,
        ReadOnlySpan<Vector2> section, uint? color = null)
    {
        if (stations.Length < 2) return;
        var list = new MeshBuilder.LoftStation[stations.Length];
        for (int i = 0; i < stations.Length; i++)
        {
            var s = stations[i];
            list[i] = new MeshBuilder.LoftStation(
                origin + new Vector3(0f, s.W, s.X), new Vector2(s.Y, s.Z));
        }
        mb.AddLoft(section, list, capStart: true, capEnd: true, color);
    }

    /// <summary>
    /// A gun barrel lying along <b>+Z</b>, the direction vehicles and weapons point: chamber,
    /// tapering tube, and whatever muzzle furniture the profile asks for. Profile entries are
    /// (distanceFromBreech, radius).
    /// </summary>
    public static void Barrel(MeshBuilder mb, Vector3 breech, ReadOnlySpan<Vector2> profile,
        int segments = 16, uint? color = null)
        => BarrelAxis(mb, breech, profile, MathX.HalfPi, segments, color);

    /// <summary>
    /// Barrel pointing along <b>-Z</b>. Weapon models use the opposite convention to vehicles —
    /// they are built in the hand, where -Z is down the sights — so they go through here.
    /// </summary>
    public static void BarrelBack(MeshBuilder mb, Vector3 breech, ReadOnlySpan<Vector2> profile,
        int segments = 16, uint? color = null)
        => BarrelAxis(mb, breech, profile, -MathX.HalfPi, segments, color);

    private static void BarrelAxis(MeshBuilder mb, Vector3 breech, ReadOnlySpan<Vector2> profile,
        float pitch, int segments, uint? color)
    {
        if (profile.Length < 2) return;
        // The profile is authored as (distance, radius); the lathe wants (radius, height) and
        // builds around +Y, so rotating a quarter turn about X carries that axis onto ±Z.
        var prof = new Vector2[profile.Length];
        for (int i = 0; i < profile.Length; i++) prof[i] = new Vector2(profile[i].Y, profile[i].X);
        mb.PushTransform(Matrix4x4.CreateRotationX(pitch) * Matrix4x4.CreateTranslation(breech));
        mb.AddLathe(prof, Vector3.Zero, segments, capBottom: true, capTop: true, color);
        mb.PopTransform();
    }

    /// <summary>Ring around the -Z axis: muzzle brakes, barrel clamps, cooling collars.</summary>
    public static void Collar(MeshBuilder mb, Vector3 center, float major, float minor, int segments = 18)
    {
        mb.PushTransform(Matrix4x4.CreateRotationX(-MathX.HalfPi) * Matrix4x4.CreateTranslation(center));
        mb.AddTorus(Vector3.Zero, major, minor, segments, 8);
        mb.PopTransform();
    }

    /// <summary>
    /// A tapered strut between two arbitrary points: suspension arms, dampers, aerials, cage
    /// tubing. Anything structural that does not happen to lie along an axis goes through here.
    /// </summary>
    public static void Strut(MeshBuilder mb, Vector3 from, Vector3 to, float radiusFrom,
        float radiusTo, int segments = 8, uint? color = null)
    {
        if (Vector3.DistanceSquared(from, to) < 1e-8f) return;
        Span<MeshBuilder.LoftStation> run =
        [
            new(from, radiusFrom),
            new(Vector3.Lerp(from, to, 0.5f), (radiusFrom + radiusTo) * 0.5f),
            new(to, radiusTo),
        ];
        mb.AddLoft(Sections.Circle(1f, segments), run, capStart: true, capEnd: true, color);
    }

    /// <summary>
    /// A road wheel or tyre: treaded outer band, sidewalls, and a hub with spokes. Four of the
    /// old vehicles' worst edges were the plain cylinders standing in for wheels.
    /// </summary>
    public static void Wheel(MeshBuilder mb, Vector3 center, float radius, float width,
        int segments = 16, int treads = 12, MatId tyre = MatId.TechPanelDark, MatId hub = MatId.Trim)
    {
        int restore = mb.Material;
        float hw = width * 0.5f;
        float shoulder = radius * 0.88f;
        // Tyre: a lathed cross-section, so the sidewalls bulge and the tread face is flat.
        Span<Vector2> prof =
        [
            new(radius * 0.42f, -hw * 0.55f),
            new(shoulder, -hw),
            new(radius, -hw * 0.72f),
            new(radius, hw * 0.72f),
            new(shoulder, hw),
            new(radius * 0.42f, hw * 0.55f),
        ];
        mb.Material = (int)tyre;
        mb.PushTransform(Matrix4x4.CreateRotationZ(MathX.HalfPi) * Matrix4x4.CreateTranslation(center));
        mb.AddLathe(prof, Vector3.Zero, segments, capBottom: false, capTop: false);

        // Tread blocks around the circumference — cheap, and they catch the light so the wheel
        // reads as rubber rather than a smooth drum.
        for (int i = 0; i < treads; i++)
        {
            float a = i / (float)treads * MathX.TwoPi;
            var m = Matrix4x4.CreateRotationY(a);
            mb.PushTransform(m);
            mb.AddBox(new Vector3(radius * 0.99f, 0f, 0f), new Vector3(radius * 0.045f, hw * 0.78f, radius * 0.075f));
            mb.PopTransform();
        }

        mb.Material = (int)hub;
        Span<Vector2> hubProf =
        [
            new(0f, -hw * 0.45f),
            new(radius * 0.34f, -hw * 0.5f),
            new(radius * 0.40f, 0f),
            new(radius * 0.34f, hw * 0.5f),
            new(0f, hw * 0.45f),
        ];
        mb.AddLathe(hubProf, Vector3.Zero, segments);
        mb.PopTransform();
        mb.Material = restore;
    }

    /// <summary>
    /// A tank's running gear along one side: track band, road wheels, drive sprocket and idler.
    /// Modelled as a closed loop so the track has a visible top run and return rollers.
    /// </summary>
    public static void TrackRun(MeshBuilder mb, float sideX, float groundY, float halfLength,
        float wheelRadius, float width, int roadWheels = 5)
    {
        int restore = mb.Material;
        float top = groundY + wheelRadius * 2.05f;
        float bottom = groundY + wheelRadius * 0.18f;
        float hw = width * 0.5f;

        // Track band: a rounded slab following the outline, built as a loft around the loop.
        mb.Material = (int)MatId.TechPanelDark;
        var loop = new List<MeshBuilder.LoftStation>();
        int arc = 6;
        for (int i = 0; i <= arc; i++)
        {
            float a = -MathX.HalfPi + i / (float)arc * MathX.Pi;
            loop.Add(new MeshBuilder.LoftStation(
                new Vector3(sideX, (top + bottom) * 0.5f + MathF.Cos(a) * (top - bottom) * 0.5f,
                    -halfLength - MathF.Sin(a) * wheelRadius * 0.8f), 1f));
        }
        for (int i = 0; i <= arc; i++)
        {
            float a = MathX.HalfPi - i / (float)arc * MathX.Pi;
            loop.Add(new MeshBuilder.LoftStation(
                new Vector3(sideX, (top + bottom) * 0.5f + MathF.Cos(a) * (top - bottom) * 0.5f,
                    halfLength + MathF.Sin(a) * wheelRadius * 0.8f), 1f));
        }
        loop.Add(loop[0]);
        var band = Sections.RoundedRect(hw, wheelRadius * 0.30f, wheelRadius * 0.12f, 2);
        mb.AddLoft(band, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(loop),
            capStart: false, capEnd: false);

        // Track links, so the band is not a smooth ribbon.
        int links = 18;
        for (int i = 0; i < links; i++)
        {
            float t = i / (float)links;
            float z = -halfLength + t * halfLength * 2f;
            foreach (float y in new[] { bottom - wheelRadius * 0.06f, top + wheelRadius * 0.06f })
                mb.AddBox(new Vector3(sideX, y, z), new Vector3(hw * 0.98f, wheelRadius * 0.05f, halfLength / links * 0.42f));
        }

        // Road wheels between the sprocket and idler.
        for (int i = 0; i < roadWheels; i++)
        {
            float t = roadWheels == 1 ? 0.5f : i / (float)(roadWheels - 1);
            float z = MathX.Lerp(-halfLength * 0.82f, halfLength * 0.82f, t);
            Wheel(mb, new Vector3(sideX, groundY + wheelRadius, z), wheelRadius * 0.92f, width * 0.7f,
                12, 0, MatId.RustMetal, MatId.Trim);
        }
        // Drive sprocket and idler sit higher and are larger, as on a real hull.
        foreach (float z in new[] { -halfLength, halfLength })
            Wheel(mb, new Vector3(sideX, groundY + wheelRadius * 1.12f, z), wheelRadius * 1.02f, width * 0.7f,
                14, 10, MatId.RustMetal, MatId.TechPanelDark);
        mb.Material = restore;
    }

    /// <summary>
    /// A canopy or cockpit glass: a half-dome stretched along the body, flat-bottomed so it sits
    /// on a deck without a visible seam.
    /// </summary>
    public static void Canopy(MeshBuilder mb, Vector3 center, float halfLength, float halfWidth,
        float height, int steps = 6, uint? color = null)
    {
        var section = Sections.RoundedRect(1f, 1f, 0.45f, 3);
        var stations = new List<MeshBuilder.LoftStation>(steps * 2 + 1);
        for (int i = 0; i <= steps * 2; i++)
        {
            float t = i / (float)(steps * 2);
            float z = MathX.Lerp(-halfLength, halfLength, t);
            // Elliptical plan and profile: widest and tallest at the middle.
            float k = MathF.Sqrt(MathF.Max(0f, 1f - (t * 2f - 1f) * (t * 2f - 1f)));
            stations.Add(new MeshBuilder.LoftStation(center + new Vector3(0, 0, z),
                new Vector2(halfWidth * MathF.Max(k, 0.05f), height * MathF.Max(k, 0.05f) * 0.5f)));
        }
        mb.AddLoft(section, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(stations),
            capStart: false, capEnd: false, color);
    }

    /// <summary>
    /// A wing or fin swept out along ±X from the hull: tapered in chord and thickness, with an
    /// optional dihedral rise and sweep-back so it does not read as a plank.
    /// </summary>
    public static void Wing(MeshBuilder mb, Vector3 root, float span, float rootChord, float tipChord,
        float thickness, float sweepBack = 0f, float dihedral = 0f, int steps = 4, float roll = 0f,
        uint? color = null)
    {
        var section = Sections.Airfoil(1f, thickness / MathF.Max(rootChord, 1e-3f), 6);
        var stations = new MeshBuilder.LoftStation[steps + 1];
        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps;
            float chord = MathX.Lerp(rootChord, tipChord, t);
            // Stations run out along the span. With no roll the loft's frame puts the chord
            // fore-and-aft and the thickness vertical, which is a wing; a quarter turn of roll
            // stands the same shape on edge, which is a fin.
            stations[i] = new MeshBuilder.LoftStation(
                root + new Vector3(span * t, dihedral * t, sweepBack * t),
                new Vector2(chord, chord), roll);
        }
        mb.AddLoft(section, stations, capStart: true, capEnd: true, color);
    }
}
