using System.Numerics;
using Silk.NET.OpenGL;
using Unreal99.Core;
using Unreal99.Rendering;

namespace Unreal99.Game;

/// <summary>
/// Procedural hulls for every vehicle, built from swept sections rather than stacked boxes.
///
/// The models are laid out with <b>+Z forward</b>, matching the physics: <see cref="Vehicle.Move"/>
/// drives along <c>(sin Yaw, 0, cos Yaw)</c>, which is model +Z once the yaw rotation is applied,
/// and seat offsets are transformed by the same rotation. Guns, noses and canopies therefore all
/// face +Z, and the rearmost seat of a multi-crew vehicle sits at negative Z.
///
/// Each vehicle is a few thousand triangles. That is trivial for the renderer at these object
/// counts, and it is what buys a silhouette that reads as a tank or an aircraft from any angle
/// instead of only from the three-quarter view a box happens to flatter.
/// </summary>
public sealed class VehicleModels : IDisposable
{
    private readonly Mesh[] _meshes = new Mesh[(int)VehicleKind.Count];
    private readonly MeshSection[][] _sections = new MeshSection[(int)VehicleKind.Count][];
    private readonly (Vector3 Min, Vector3 Max)[] _bounds = new (Vector3, Vector3)[(int)VehicleKind.Count];

    public Mesh MeshFor(VehicleKind k) => _meshes[(int)k];
    public MeshSection[] SectionsFor(VehicleKind k) => _sections[(int)k];

    /// <summary>
    /// Model-space bounds of the built mesh, in the vehicle's own frame. Used to frame the
    /// documentation turntable — a Goliath's gun and a Darkwalker's legs both reach far outside
    /// the collision extents the gameplay code uses.
    /// </summary>
    public (Vector3 Min, Vector3 Max) BoundsFor(VehicleKind k) => _bounds[(int)k];

    public VehicleModels(GL gl)
    {
        for (int i = 0; i < (int)VehicleKind.Count; i++)
        {
            var mb = new MeshBuilder { WorldUv = false, Material = (int)MatId.ArmorPlate };
            Build((VehicleKind)i, mb);
            mb.RecalculateTangents();
            _bounds[i] = mb.Bounds();
            var (v, ind, s) = mb.Build();
            _meshes[i] = Mesh.CreateStatic<Vertex>(gl, v, ind, VertexLayouts.Static);
            _sections[i] = s;
        }
    }

    // ================================================================ shared parts

    /// <summary>Four (or six) road wheels on two axles, mirrored across the centreline.</summary>
    private static void WheelPairs(MeshBuilder mb, float x, float radius, float width,
        params float[] axleZ)
    {
        foreach (float z in axleZ)
            foreach (float sx in new[] { -x, x })
                Shapes.Wheel(mb, new Vector3(sx, radius, z), radius, width, 14, 10);
    }

    /// <summary>
    /// A suspension arm reaching from the hull out to a wheel hub — the detail that stops a
    /// wheeled vehicle looking like a slab with cylinders glued to it.
    /// </summary>
    private static void Suspension(MeshBuilder mb, Vector3 hull, Vector3 hub, float thickness)
    {
        int restore = mb.Material;
        mb.Material = (int)MatId.WeaponMetal;
        Span<MeshBuilder.LoftStation> arm =
        [
            new(hull, new Vector2(thickness * 1.5f, thickness * 1.5f)),
            new(Vector3.Lerp(hull, hub, 0.55f), new Vector2(thickness, thickness)),
            new(hub, new Vector2(thickness * 0.8f, thickness * 0.8f)),
        ];
        mb.AddLoft(Sections.RoundedRect(1f, 0.6f, 0.35f, 2), arm);
        mb.Material = (int)MatId.Trim;
        // Damper: angled up and inboard from the hub into the hull side.
        Shapes.Strut(mb, hub + new Vector3(0f, 0.04f, 0f),
            hull + new Vector3(0f, 0.34f, 0f), thickness * 0.55f, thickness * 0.4f, 8);
        mb.Material = restore;
    }

    /// <summary>
    /// An articulated walker leg: hip yoke, thigh, reversed knee, shin and a splayed foot. The
    /// Necris walkers live or die on this — a straight post reads as scaffolding.
    /// </summary>
    private static void WalkerLeg(MeshBuilder mb, Vector3 hip, float angle, float reach, float height,
        float thickness)
    {
        int restore = mb.Material;
        Vector3 dir = new(MathF.Cos(angle), 0f, MathF.Sin(angle));
        Vector3 knee = hip + dir * reach * 0.52f + new Vector3(0f, height * 0.30f, 0f);
        Vector3 ankle = hip + dir * reach + new Vector3(0f, -height * 0.86f, 0f);
        Vector3 foot = ankle + dir * reach * 0.22f + new Vector3(0f, -height * 0.14f, 0f);

        mb.Material = (int)MatId.TechPanelDark;
        var limb = Sections.Superellipse(1f, 0.7f, 3.2f, 12);
        // Thigh sweeps up and out to a high knee; shin drops back in. The reversed joint is the
        // whole reason these read as Necris rather than as a table.
        Span<MeshBuilder.LoftStation> thigh =
        [
            new(hip, new Vector2(thickness * 1.35f, thickness * 1.35f)),
            new(Vector3.Lerp(hip, knee, 0.5f), new Vector2(thickness * 1.05f, thickness * 1.2f)),
            new(knee, new Vector2(thickness * 0.95f, thickness * 0.95f)),
        ];
        mb.AddLoft(limb, thigh);

        Span<MeshBuilder.LoftStation> shin =
        [
            new(knee, new Vector2(thickness * 0.95f, thickness * 0.95f)),
            new(Vector3.Lerp(knee, ankle, 0.45f), new Vector2(thickness * 0.72f, thickness * 0.85f)),
            new(ankle, new Vector2(thickness * 0.42f, thickness * 0.5f)),
        ];
        mb.AddLoft(limb, shin);

        // Knee cowl and a clawed foot.
        mb.Material = (int)MatId.ArmorPlate;
        Shapes.RoundedBox(mb, knee, new Vector3(thickness * 1.5f, thickness * 1.4f, thickness * 1.5f),
            thickness * 0.7f, 3, 3);
        mb.Material = (int)MatId.Trim;
        for (int t = 0; t < 3; t++)
        {
            float ta = angle + (t - 1) * 0.7f;
            Vector3 toe = foot + new Vector3(MathF.Cos(ta), 0f, MathF.Sin(ta)) * thickness * 2.4f;
            Span<MeshBuilder.LoftStation> claw =
            [
                new(ankle, new Vector2(thickness * 0.42f, thickness * 0.42f)),
                new(Vector3.Lerp(ankle, toe, 0.6f), new Vector2(thickness * 0.34f, thickness * 0.26f)),
                new(toe, new Vector2(thickness * 0.06f, thickness * 0.06f)),
            ];
            mb.AddLoft(Sections.Circle(1f, 7), claw);
        }
        mb.Material = restore;
    }

    /// <summary>A turret ring: the raised collar a turret sits and swivels on.</summary>
    private static void TurretRing(MeshBuilder mb, Vector3 center, float radius, float height)
    {
        int restore = mb.Material;
        mb.Material = (int)MatId.Trim;
        mb.AddLathe(
        [
            new(radius * 0.86f, 0f), new(radius, height * 0.35f),
            new(radius, height * 0.8f), new(radius * 0.88f, height),
        ], center, 20, capBottom: false, capTop: false);
        mb.Material = restore;
    }

    /// <summary>
    /// A tank gun: breech, tapered tube, fume extractor and a slotted muzzle brake. Barrel
    /// silhouette is most of what tells two tanks apart at range.
    /// </summary>
    private static void TankGun(MeshBuilder mb, Vector3 breech, float length, float calibre,
        bool fumeExtractor = true, bool muzzleBrake = true)
    {
        int restore = mb.Material;
        float r = calibre;
        var prof = new List<Vector2>
        {
            new(0f, r * 1.55f),
            new(length * 0.06f, r * 1.55f),
            new(length * 0.09f, r * 1.15f),
        };
        if (fumeExtractor)
        {
            prof.Add(new Vector2(length * 0.44f, r * 1.05f));
            prof.Add(new Vector2(length * 0.48f, r * 1.6f));
            prof.Add(new Vector2(length * 0.60f, r * 1.6f));
            prof.Add(new Vector2(length * 0.64f, r * 1.0f));
        }
        prof.Add(new Vector2(length * 0.88f, r * 0.92f));
        if (muzzleBrake)
        {
            prof.Add(new Vector2(length * 0.90f, r * 1.45f));
            prof.Add(new Vector2(length * 0.97f, r * 1.45f));
            prof.Add(new Vector2(length * 0.98f, r * 1.15f));
        }
        prof.Add(new Vector2(length, r * 1.05f));
        prof.Add(new Vector2(length, r * 0.62f));       // bore

        mb.Material = (int)MatId.WeaponMetal;
        Shapes.Barrel(mb, breech, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(prof), 18);

        if (muzzleBrake)
        {
            // Blast slots cut into the brake, as ports rather than a smooth collar.
            mb.Material = (int)MatId.TechPanelDark;
            for (int i = 0; i < 4; i++)
            {
                float a = i / 4f * MathX.TwoPi + MathX.HalfPi * 0.5f;
                mb.PushTransform(Matrix4x4.CreateTranslation(
                    breech + new Vector3(MathF.Cos(a) * r * 1.5f, MathF.Sin(a) * r * 1.5f, length * 0.935f)));
                mb.AddBox(Vector3.Zero, new Vector3(r * 0.22f, r * 0.22f, length * 0.02f));
                mb.PopTransform();
            }
        }
        mb.Material = restore;
    }

    /// <summary>Twin-linked light cannons, as carried under the nose of most of the fliers.</summary>
    private static void LinkedCannons(MeshBuilder mb, Vector3 center, float spread, float length,
        float calibre, MatId muzzle = MatId.EnergyPanel)
    {
        int restore = mb.Material;
        foreach (float sx in new[] { -spread, spread })
        {
            mb.Material = (int)MatId.WeaponMetal;
            Shapes.Barrel(mb, center + new Vector3(sx, 0f, 0f),
            [
                new(0f, calibre * 1.5f), new(length * 0.2f, calibre * 1.3f),
                new(length * 0.85f, calibre), new(length, calibre * 1.25f),
            ], 12);
            mb.Material = (int)muzzle;
            Shapes.Barrel(mb, center + new Vector3(sx, 0f, length * 0.98f),
                [new Vector2(0f, calibre * 0.85f), new Vector2(calibre * 1.2f, calibre * 0.5f)], 10);
        }
        mb.Material = restore;
    }

    /// <summary>A pod of missile tubes, open at the front.</summary>
    private static void MissilePod(MeshBuilder mb, Vector3 center, float halfW, float halfH, float halfL,
        int cols = 2, int rows = 2)
    {
        int restore = mb.Material;
        mb.Material = (int)MatId.WeaponMetal;
        Shapes.RoundedBox(mb, center, new Vector3(halfW, halfH, halfL), MathF.Min(halfW, halfH) * 0.4f, 3, 3);
        mb.Material = (int)MatId.TechPanelDark;
        float tr = MathF.Min(halfW / cols, halfH / rows) * 0.72f;
        for (int cx = 0; cx < cols; cx++)
            for (int cy = 0; cy < rows; cy++)
            {
                float px = (cx + 0.5f) / cols * halfW * 2f - halfW;
                float py = (cy + 0.5f) / rows * halfH * 2f - halfH;
                Shapes.Barrel(mb, center + new Vector3(px, py, halfL * 0.55f),
                    [new Vector2(0f, tr), new Vector2(halfL * 0.5f, tr * 0.95f)], 8);
            }
        mb.Material = restore;
    }

    /// <summary>
    /// Engine exhaust. <paramref name="exit"/> is the nozzle mouth at the tail; the bell narrows
    /// forward into the hull, so the flare faces the way the thrust goes.
    /// </summary>
    private static void Thruster(MeshBuilder mb, Vector3 exit, float radius, float length)
    {
        int restore = mb.Material;
        mb.Material = (int)MatId.RustMetal;
        Shapes.Barrel(mb, exit,
        [
            new(0f, radius * 1.06f), new(length * 0.16f, radius),
            new(length * 0.48f, radius * 0.86f), new(length, radius * 0.72f),
        ], 14);
        mb.Material = (int)MatId.EnergyPanel;
        // The glowing throat, recessed just inside the mouth.
        Shapes.Barrel(mb, exit + new Vector3(0f, 0f, length * 0.10f),
            [new Vector2(0f, radius * 0.82f), new Vector2(length * 0.16f, radius * 0.5f)], 12);
        mb.Material = restore;
    }

    // ================================================================ the vehicles

    /// <summary>Sloped-armour cross-section: flat deck, chined sides. Unit-sized, scaled per station.</summary>
    private static Vector2[] ArmourSection()
        => Sections.Fillet([new(0.82f, 1f), new(-0.82f, 1f), new(-1f, 0.10f),
                            new(-0.86f, -1f), new(0.86f, -1f), new(1f, 0.10f)], 0.16f, 2);

    /// <summary>Smooth aerodynamic cross-section for fliers and hovercraft.</summary>
    private static Vector2[] FairingSection() => Sections.Superellipse(1f, 1f, 2.6f, 18);

    private static void Build(VehicleKind kind, MeshBuilder mb)
    {
        switch (kind)
        {
            // ---------------------------------------------------------------- Axon
            case VehicleKind.Scorpion: BuildScorpion(mb); break;
            case VehicleKind.Hellbender: BuildHellbender(mb); break;
            case VehicleKind.Goliath: BuildGoliath(mb); break;
            case VehicleKind.Leviathan: BuildLeviathan(mb); break;
            case VehicleKind.Paladin: BuildPaladin(mb); break;
            case VehicleKind.Spma: BuildSpma(mb); break;
            case VehicleKind.Manta: BuildManta(mb); break;
            case VehicleKind.Raptor: BuildRaptor(mb); break;
            case VehicleKind.Cicada: BuildCicada(mb); break;
            case VehicleKind.IonTank: BuildIonTank(mb); break;
            // ---------------------------------------------------------------- Necris
            case VehicleKind.Viper: BuildViper(mb); break;
            case VehicleKind.Scavenger: BuildScavenger(mb); break;
            case VehicleKind.Nemesis: BuildNemesis(mb); break;
            case VehicleKind.Nightshade: BuildNightshade(mb); break;
            case VehicleKind.Fury: BuildFury(mb); break;
            case VehicleKind.Darkwalker: BuildDarkwalker(mb); break;
            case VehicleKind.Hoverboard: BuildHoverboard(mb); break;
        }
    }

    /// <summary>
    /// Scorpion: an open-framed four-wheel raider. Narrow tub, exposed roll cage, and the
    /// retractable blade booms that are the only reason anyone remembers it.
    /// </summary>
    private static void BuildScorpion(MeshBuilder mb)
    {
        mb.Material = (int)MatId.ArmorPlate;
        // Body: wide over the axles, pinched at the waist, nose dropping away at the front.
        Shapes.Hull(mb, Vector3.Zero,
        [
            new(-2.05f, 0.30f, 0.16f, 0.92f),   // tail
            new(-1.60f, 0.72f, 0.34f, 0.86f),   // engine deck
            new(-0.70f, 0.86f, 0.40f, 0.82f),
            new( 0.20f, 0.80f, 0.42f, 0.80f),   // cockpit
            new( 1.20f, 0.64f, 0.30f, 0.74f),
            new( 1.85f, 0.42f, 0.18f, 0.66f),   // nose
            new( 2.15f, 0.10f, 0.06f, 0.62f),
        ], 0.30f);

        // Cockpit tub scooped out of the deck.
        mb.Material = (int)MatId.TechPanelDark;
        Shapes.RoundedBox(mb, new Vector3(0f, 1.14f, 0.15f), new Vector3(0.52f, 0.16f, 0.62f), 0.14f);
        mb.Material = (int)MatId.Trim;
        // Roll cage over the driver.
        for (int s = -1; s <= 1; s += 2)
        {
            Span<MeshBuilder.LoftStation> hoop =
            [
                new(new Vector3(s * 0.55f, 1.05f, -0.35f), 0.055f),
                new(new Vector3(s * 0.62f, 1.55f, -0.25f), 0.055f),
                new(new Vector3(s * 0.40f, 1.72f, 0.15f), 0.05f),
                new(new Vector3(s * 0.30f, 1.60f, 0.60f), 0.05f),
                new(new Vector3(s * 0.42f, 1.18f, 0.72f), 0.05f),
            ];
            mb.AddLoft(Sections.Circle(1f, 7), hoop);
        }
        Span<MeshBuilder.LoftStation> spine =
        [
            new(new Vector3(-0.62f, 1.55f, -0.25f), 0.05f),
            new(new Vector3(0.62f, 1.55f, -0.25f), 0.05f),
        ];
        mb.AddLoft(Sections.Circle(1f, 7), spine);

        // Blade booms: swept arms carrying a razor edge, angled out from the flanks.
        mb.Material = (int)MatId.WeaponMetal;
        foreach (int s in new[] { -1, 1 })
        {
            Span<MeshBuilder.LoftStation> boom =
            [
                new(new Vector3(s * 0.80f, 0.86f, -0.30f), new Vector2(0.10f, 0.10f)),
                new(new Vector3(s * 1.18f, 0.88f, -0.10f), new Vector2(0.09f, 0.06f)),
                new(new Vector3(s * 1.34f, 0.90f, 0.55f), new Vector2(0.30f, 0.030f)),
                new(new Vector3(s * 1.30f, 0.90f, 1.25f), new Vector2(0.26f, 0.020f)),
                new(new Vector3(s * 1.10f, 0.90f, 1.60f), new Vector2(0.10f, 0.010f)),
            ];
            mb.AddLoft(Sections.Airfoil(1f, 0.5f, 5), boom);
        }

        // Rear engine and exhausts.
        mb.Material = (int)MatId.RustMetal;
        Shapes.RoundedBox(mb, new Vector3(0f, 1.12f, -1.35f), new Vector3(0.50f, 0.24f, 0.45f), 0.14f);
        Thruster(mb, new Vector3(-0.30f, 1.02f, -2.05f), 0.15f, 0.30f);
        Thruster(mb, new Vector3(0.30f, 1.02f, -2.05f), 0.15f, 0.30f);

        foreach (float z in new[] { 1.35f, -1.35f })
            foreach (int s in new[] { -1, 1 })
                Suspension(mb, new Vector3(s * 0.72f, 0.72f, z), new Vector3(s * 1.02f, 0.50f, z), 0.09f);
        WheelPairs(mb, 1.12f, 0.50f, 0.36f, 1.35f, -1.35f);
    }

    /// <summary>
    /// Hellbender: a heavy four-wheel gun car. Sloped bonnet and glazed cab up front, then a long
    /// flat bed carrying two independent turrets — the driver has no weapon, which is why the
    /// silhouette has to make those two rear mounts obvious.
    /// </summary>
    private static void BuildHellbender(MeshBuilder mb)
    {
        mb.Material = (int)MatId.ArmorPlate;
        Shapes.Hull(mb, Vector3.Zero,
        [
            new(-2.55f, 0.55f, 0.22f, 1.05f),
            new(-2.10f, 1.10f, 0.42f, 1.00f),
            new(-0.90f, 1.30f, 0.46f, 0.98f),   // gun deck
            new( 0.40f, 1.30f, 0.48f, 0.98f),
            new( 1.05f, 1.22f, 0.52f, 1.02f),   // cab
            new( 1.95f, 1.02f, 0.40f, 0.92f),   // bonnet slopes away
            new( 2.45f, 0.78f, 0.24f, 0.82f),
            new( 2.70f, 0.42f, 0.10f, 0.78f),
        ], 0.26f);

        // Glazed cab, set back over the front axle.
        mb.Material = (int)MatId.Glass;
        Shapes.Canopy(mb, new Vector3(0f, 1.50f, 1.00f), 0.78f, 0.86f, 0.78f, 6);
        mb.Material = (int)MatId.Trim;
        // Roll-over bar behind the cab, and the deck rails along the bed.
        Span<MeshBuilder.LoftStation> bar =
        [
            new(new Vector3(-0.95f, 1.42f, 0.28f), 0.07f),
            new(new Vector3(-0.80f, 1.92f, 0.24f), 0.07f),
            new(new Vector3(0.80f, 1.92f, 0.24f), 0.07f),
            new(new Vector3(0.95f, 1.42f, 0.28f), 0.07f),
        ];
        mb.AddLoft(Sections.Circle(1f, 8), bar);

        // Skymine turret (mid deck) and laser turret (rear), each on its own ring.
        TurretRing(mb, new Vector3(0f, 1.44f, -0.40f), 0.42f, 0.12f);
        mb.Material = (int)MatId.WeaponMetal;
        Shapes.RoundedBox(mb, new Vector3(0f, 1.72f, -0.40f), new Vector3(0.40f, 0.22f, 0.46f), 0.16f);
        Shapes.Barrel(mb, new Vector3(0f, 1.78f, -0.10f),
            [new Vector2(0f, 0.16f), new Vector2(0.34f, 0.19f), new Vector2(0.42f, 0.13f)], 12);

        TurretRing(mb, new Vector3(0f, 1.44f, -1.70f), 0.46f, 0.12f);
        mb.Material = (int)MatId.WeaponMetal;
        Shapes.RoundedBox(mb, new Vector3(0f, 1.76f, -1.70f), new Vector3(0.44f, 0.26f, 0.44f), 0.18f);
        LinkedCannons(mb, new Vector3(0f, 1.80f, -1.40f), 0.17f, 0.95f, 0.055f);

        mb.Material = (int)MatId.RustMetal;
        Thruster(mb, new Vector3(-0.55f, 1.02f, -2.60f), 0.20f, 0.34f);
        Thruster(mb, new Vector3(0.55f, 1.02f, -2.60f), 0.20f, 0.34f);

        foreach (float z in new[] { 1.55f, -1.55f })
            foreach (int s in new[] { -1, 1 })
                Suspension(mb, new Vector3(s * 1.05f, 0.86f, z), new Vector3(s * 1.36f, 0.60f, z), 0.11f);
        WheelPairs(mb, 1.48f, 0.60f, 0.44f, 1.55f, -1.55f);
    }

    /// <summary>
    /// Goliath: the main battle tank. Sloped glacis, sponsons overhanging the tracks, a cast
    /// turret with a mantlet, and a long gun with a fume extractor and muzzle brake.
    /// </summary>
    private static void BuildGoliath(MeshBuilder mb)
    {
        mb.Material = (int)MatId.ArmorPlate;
        // Lower hull: a tub between the tracks.
        Shapes.Hull(mb, Vector3.Zero,
        [
            new(-3.05f, 1.35f, 0.36f, 0.86f),
            new(-2.40f, 1.52f, 0.42f, 0.88f),
            new( 1.60f, 1.55f, 0.42f, 0.88f),
            new( 2.55f, 1.42f, 0.34f, 0.84f),
            new( 3.00f, 1.20f, 0.22f, 0.78f),
        ], 0.20f);

        // Upper hull with the glacis: the deck runs back level, then dives to a sharp nose. The
        // slope is the point — a flat front plate is what made the old model read as a crate.
        Shapes.Hull(mb, Vector3.Zero,
        [
            new(-3.00f, 1.62f, 0.26f, 1.42f),
            new(-1.90f, 1.74f, 0.32f, 1.46f),
            new( 0.30f, 1.74f, 0.32f, 1.46f),
            new( 1.35f, 1.70f, 0.28f, 1.40f),
            new( 2.35f, 1.55f, 0.20f, 1.20f),   // glacis
            new( 3.05f, 1.30f, 0.11f, 0.98f),
            new( 3.30f, 1.05f, 0.06f, 0.90f),
        ], 0.22f);

        // Deck furniture: stowage bins, hatches and exhaust grilles.
        mb.Material = (int)MatId.RustMetal;
        foreach (int s in new[] { -1, 1 })
            Shapes.RoundedBox(mb, new Vector3(s * 1.36f, 1.78f, -2.20f), new Vector3(0.30f, 0.16f, 0.62f), 0.10f);
        mb.Material = (int)MatId.MetalGrate;
        Shapes.RoundedBox(mb, new Vector3(0f, 1.80f, -2.55f), new Vector3(0.95f, 0.10f, 0.42f), 0.06f);

        // Turret: a cast shape, widest at the ring and drawn in toward the bustle and the mantlet.
        mb.Material = (int)MatId.ArmorPlate;
        TurretRing(mb, new Vector3(0f, 1.70f, 0.10f), 1.02f, 0.14f);
        Shapes.Hull(mb, new Vector3(0f, 0f, 0.10f),
        [
            new(-1.30f, 0.86f, 0.24f, 2.10f),   // bustle
            new(-1.05f, 0.98f, 0.34f, 2.14f),
            new(-0.20f, 1.06f, 0.38f, 2.18f),
            new( 0.70f, 0.95f, 0.34f, 2.14f),
            new( 1.18f, 0.62f, 0.26f, 2.08f),   // mantlet shoulder
            new( 1.34f, 0.40f, 0.22f, 2.06f),
        ], 0.26f);

        // Mantlet and gun.
        mb.Material = (int)MatId.WeaponMetal;
        Shapes.Barrel(mb, new Vector3(0f, 2.06f, 1.20f),
            [new Vector2(0f, 0.34f), new Vector2(0.24f, 0.36f), new Vector2(0.30f, 0.26f)], 14);
        TankGun(mb, new Vector3(0f, 2.06f, 1.44f), 3.30f, 0.115f);

        // Commander's cupola with its machine gun.
        mb.Material = (int)MatId.Trim;
        mb.AddLathe(
        [
            new(0.34f, 0f), new(0.34f, 0.22f), new(0.30f, 0.28f),
        ], new Vector3(0.44f, 2.44f, -0.10f), 14, capBottom: false);
        mb.Material = (int)MatId.WeaponMetal;
        Shapes.Barrel(mb, new Vector3(0.44f, 2.70f, 0.05f),
            [new Vector2(0f, 0.05f), new Vector2(0.62f, 0.035f)], 8);

        // Optics block on the turret face.
        mb.Material = (int)MatId.Glass;
        Shapes.RoundedBox(mb, new Vector3(-0.52f, 2.30f, 0.92f), new Vector3(0.16f, 0.10f, 0.06f), 0.03f);

        Shapes.TrackRun(mb, -1.62f, 0f, 2.70f, 0.52f, 0.62f, 5);
        Shapes.TrackRun(mb, 1.62f, 0f, 2.70f, 0.52f, 0.62f, 5);
    }

    /// <summary>
    /// Leviathan: a mobile fortress on eight wheels, with four corner turrets and the ion cannon
    /// that only unfolds once it is deployed. Everything about it should read as oversized.
    /// </summary>
    private static void BuildLeviathan(MeshBuilder mb)
    {
        mb.Material = (int)MatId.ArmorPlate;
        Shapes.Hull(mb, Vector3.Zero,
        [
            new(-5.20f, 2.30f, 0.55f, 1.35f),
            new(-4.30f, 2.75f, 0.85f, 1.55f),
            new(-1.50f, 2.90f, 0.95f, 1.60f),
            new( 1.80f, 2.90f, 0.95f, 1.60f),
            new( 3.90f, 2.70f, 0.80f, 1.50f),
            new( 4.90f, 2.20f, 0.50f, 1.30f),
            new( 5.30f, 1.60f, 0.24f, 1.16f),
        ], 0.32f);

        // Superstructure: the raised command deck the ion cannon rises from.
        mb.Material = (int)MatId.TechPanelDark;
        Shapes.Hull(mb, Vector3.Zero,
        [
            new(-2.60f, 1.85f, 0.42f, 2.85f),
            new(-1.60f, 2.05f, 0.55f, 2.95f),
            new( 1.20f, 2.05f, 0.55f, 2.95f),
            new( 2.30f, 1.75f, 0.40f, 2.80f),
        ], 0.28f);
        mb.Material = (int)MatId.Glass;
        Shapes.Canopy(mb, new Vector3(0f, 3.42f, 1.30f), 1.10f, 1.35f, 0.85f, 6);

        // Ion cannon on a raised trunnion.
        mb.Material = (int)MatId.WeaponMetal;
        Shapes.RoundedBox(mb, new Vector3(0f, 3.62f, -0.60f), new Vector3(0.85f, 0.55f, 1.15f), 0.30f);
        Shapes.Barrel(mb, new Vector3(0f, 3.72f, 0.40f),
        [
            new(0f, 0.42f), new(0.30f, 0.46f), new(0.36f, 0.30f),
            new(2.40f, 0.27f), new(2.55f, 0.44f), new(3.00f, 0.44f),
            new(3.10f, 0.34f), new(3.30f, 0.34f), new(3.30f, 0.18f),
        ], 18);
        mb.Material = (int)MatId.EnergyPanel;
        for (int i = 0; i < 3; i++)
            mb.AddLathe([new Vector2(0.30f, 0f), new Vector2(0.50f, 0.06f), new Vector2(0.30f, 0.12f)],
                new Vector3(0f, 3.72f, 1.30f + i * 0.62f), 16, capBottom: false, capTop: false);

        // Four corner plasma turrets.
        foreach (var (cx, cz) in new[] { (-2.45f, 3.55f), (2.45f, 3.55f), (-2.45f, -3.55f), (2.45f, -3.55f) })
        {
            TurretRing(mb, new Vector3(cx, 2.50f, cz), 0.52f, 0.10f);
            mb.Material = (int)MatId.WeaponMetal;
            Shapes.RoundedBox(mb, new Vector3(cx, 2.78f, cz), new Vector3(0.46f, 0.26f, 0.50f), 0.20f);
            float face = cz > 0 ? 1f : -1f;
            LinkedCannons(mb, new Vector3(cx, 2.82f, cz + face * 0.42f), 0.14f, 0.62f, 0.05f);
        }

        mb.Material = (int)MatId.RustMetal;
        foreach (float sx in new[] { -1.4f, 0f, 1.4f })
            Thruster(mb, new Vector3(sx, 1.55f, -5.30f), 0.34f, 0.55f);

        foreach (float z in new[] { 3.30f, 1.15f, -1.15f, -3.30f })
            foreach (int s in new[] { -1, 1 })
                Suspension(mb, new Vector3(s * 2.30f, 1.20f, z), new Vector3(s * 2.86f, 0.92f, z), 0.16f);
        WheelPairs(mb, 3.05f, 0.92f, 0.70f, 3.30f, 1.15f, -1.15f, -3.30f);
    }

    /// <summary>
    /// Paladin: a tracked support tank whose whole identity is the shield projector — a dish on
    /// the bow flanked by emitter pylons, with a stubby energy mortar above.
    /// </summary>
    private static void BuildPaladin(MeshBuilder mb)
    {
        mb.Material = (int)MatId.ArmorPlate;
        Shapes.Hull(mb, Vector3.Zero,
        [
            new(-2.80f, 1.40f, 0.38f, 0.88f),
            new(-2.10f, 1.60f, 0.46f, 0.92f),
            new( 1.40f, 1.62f, 0.46f, 0.92f),
            new( 2.30f, 1.48f, 0.36f, 0.88f),
            new( 2.80f, 1.20f, 0.22f, 0.82f),
        ], 0.22f);
        Shapes.Hull(mb, Vector3.Zero,
        [
            new(-2.70f, 1.55f, 0.28f, 1.44f),
            new(-1.60f, 1.66f, 0.34f, 1.48f),
            new( 0.80f, 1.66f, 0.34f, 1.48f),
            new( 2.00f, 1.50f, 0.22f, 1.28f),
            new( 2.70f, 1.24f, 0.10f, 1.02f),
        ], 0.24f);

        // Shield projector: a concave dish that sits proud of the glacis.
        mb.Material = (int)MatId.EnergyPanel;
        mb.PushTransform(Matrix4x4.CreateRotationX(MathX.HalfPi)
            * Matrix4x4.CreateTranslation(new Vector3(0f, 1.55f, 2.55f)));
        mb.AddLathe(
        [
            new(0.10f, 0f), new(0.55f, -0.14f), new(0.95f, -0.30f),
            new(1.05f, -0.16f), new(1.05f, -0.06f), new(0.20f, 0.10f),
        ], Vector3.Zero, 22, capBottom: false, capTop: false);
        mb.PopTransform();
        mb.Material = (int)MatId.Trim;
        foreach (int s in new[] { -1, 1 })
        {
            Span<MeshBuilder.LoftStation> pylon =
            [
                new(new Vector3(s * 1.05f, 1.55f, 1.30f), new Vector2(0.14f, 0.16f)),
                new(new Vector3(s * 1.16f, 1.95f, 2.05f), new Vector2(0.11f, 0.12f)),
                new(new Vector3(s * 1.12f, 2.05f, 2.55f), new Vector2(0.08f, 0.08f)),
            ];
            mb.AddLoft(Sections.RoundedRect(1f, 1f, 0.4f, 2), pylon);
        }

        // Energy mortar in a low turret.
        TurretRing(mb, new Vector3(0f, 1.72f, -0.20f), 0.86f, 0.12f);
        mb.Material = (int)MatId.WeaponMetal;
        Shapes.Hull(mb, new Vector3(0f, 0f, -0.20f),
        [
            new(-0.95f, 0.72f, 0.28f, 2.06f),
            new(-0.10f, 0.86f, 0.34f, 2.10f),
            new( 0.72f, 0.66f, 0.28f, 2.04f),
        ], 0.24f);
        Shapes.Barrel(mb, new Vector3(0f, 2.06f, 0.50f),
        [
            new(0f, 0.26f), new(0.20f, 0.28f), new(0.26f, 0.19f),
            new(1.35f, 0.19f), new(1.42f, 0.30f), new(1.60f, 0.30f), new(1.60f, 0.16f),
        ], 14);

        Shapes.TrackRun(mb, -1.68f, 0f, 2.45f, 0.50f, 0.58f, 5);
        Shapes.TrackRun(mb, 1.68f, 0f, 2.45f, 0.50f, 0.58f, 5);
    }

    /// <summary>
    /// SPMA: wheeled artillery. Long barrel carried high on a recoil cradle, with hydraulic
    /// spades at the rear that plant it when it deploys.
    /// </summary>
    private static void BuildSpma(MeshBuilder mb)
    {
        mb.Material = (int)MatId.ArmorPlate;
        Shapes.Hull(mb, Vector3.Zero,
        [
            new(-2.80f, 1.05f, 0.34f, 0.96f),
            new(-2.20f, 1.28f, 0.44f, 1.00f),
            new(-0.40f, 1.34f, 0.48f, 1.02f),
            new( 1.30f, 1.28f, 0.44f, 1.00f),
            new( 2.30f, 1.05f, 0.32f, 0.94f),
            new( 2.75f, 0.62f, 0.16f, 0.86f),
        ], 0.24f);
        mb.Material = (int)MatId.Glass;
        Shapes.Canopy(mb, new Vector3(0f, 1.52f, 1.35f), 0.62f, 0.72f, 0.62f, 5);

        // Cradle and elevated barrel. Artillery points high even at rest.
        mb.Material = (int)MatId.WeaponMetal;
        Shapes.RoundedBox(mb, new Vector3(0f, 1.72f, -0.85f), new Vector3(0.60f, 0.32f, 0.85f), 0.22f);
        foreach (int s in new[] { -1, 1 })
            Shapes.RoundedBox(mb, new Vector3(s * 0.56f, 2.05f, -0.60f), new Vector3(0.10f, 0.34f, 0.44f), 0.08f);

        mb.PushTransform(Matrix4x4.CreateRotationX(-0.62f)
            * Matrix4x4.CreateTranslation(new Vector3(0f, 2.10f, -0.60f)));
        mb.Material = (int)MatId.WeaponMetal;
        Shapes.Barrel(mb, Vector3.Zero,
        [
            new(0f, 0.30f), new(0.42f, 0.32f), new(0.50f, 0.20f),
            new(2.30f, 0.175f), new(2.42f, 0.30f), new(2.72f, 0.30f),
            new(2.80f, 0.22f), new(3.05f, 0.22f), new(3.05f, 0.12f),
        ], 16);
        mb.Material = (int)MatId.Trim;
        // Recuperator cylinders riding alongside the tube.
        foreach (int s in new[] { -1, 1 })
            Shapes.Barrel(mb, new Vector3(s * 0.26f, 0.24f, 0.30f),
                [new Vector2(0f, 0.09f), new Vector2(1.10f, 0.09f)], 10);
        mb.PopTransform();

        // Deployment spades.
        mb.Material = (int)MatId.RustMetal;
        foreach (int s in new[] { -1, 1 })
        {
            Span<MeshBuilder.LoftStation> spade =
            [
                new(new Vector3(s * 0.70f, 1.00f, -2.55f), new Vector2(0.16f, 0.14f)),
                new(new Vector3(s * 0.86f, 0.62f, -3.05f), new Vector2(0.30f, 0.09f)),
                new(new Vector3(s * 0.90f, 0.34f, -3.25f), new Vector2(0.34f, 0.05f)),
            ];
            mb.AddLoft(Sections.RoundedRect(1f, 1f, 0.35f, 2), spade);
        }

        foreach (float z in new[] { 1.70f, 0.05f, -1.70f })
            foreach (int s in new[] { -1, 1 })
                Suspension(mb, new Vector3(s * 1.00f, 0.92f, z), new Vector3(s * 1.32f, 0.62f, z), 0.11f);
        WheelPairs(mb, 1.44f, 0.62f, 0.46f, 1.70f, 0.05f, -1.70f);
    }

    /// <summary>
    /// Manta: a hover skimmer built around two huge downward fans. Broad curved shell, bubble
    /// canopy, twin plasma guns under the chin, and the fan ducts that make it unmistakable.
    /// </summary>
    private static void BuildManta(MeshBuilder mb)
    {
        mb.Material = (int)MatId.ArmorPlate;
        // Shell: a wide manta-ray plan, thickest over the fans, thinning to the wing tips.
        Shapes.Hull(mb, Vector3.Zero,
        [
            new(-1.75f, 0.55f, 0.14f, 0.66f),
            new(-1.20f, 1.35f, 0.26f, 0.62f),
            new(-0.30f, 1.95f, 0.32f, 0.60f),
            new( 0.55f, 1.90f, 0.30f, 0.60f),
            new( 1.30f, 1.30f, 0.22f, 0.60f),
            new( 1.85f, 0.66f, 0.14f, 0.58f),
            new( 2.15f, 0.20f, 0.06f, 0.56f),
        ], FairingSection());

        // Fan ducts: open rings under the wings with a hub and blades inside.
        foreach (int s in new[] { -1, 1 })
        {
            Vector3 c = new(s * 1.15f, 0.42f, 0.10f);
            mb.Material = (int)MatId.TechPanelDark;
            mb.AddLathe(
            [
                new(0.50f, 0.16f), new(0.60f, 0.10f), new(0.62f, -0.06f), new(0.54f, -0.18f),
            ], c, 20, capBottom: false, capTop: false);
            mb.Material = (int)MatId.Trim;
            mb.AddLathe([new Vector2(0f, 0.10f), new Vector2(0.16f, 0.06f), new Vector2(0.13f, -0.10f)],
                c, 12);
            mb.Material = (int)MatId.EnergyPanel;
            for (int b = 0; b < 6; b++)
            {
                float a = b / 6f * MathX.TwoPi;
                mb.PushTransform(Matrix4x4.CreateRotationY(a) * Matrix4x4.CreateTranslation(c));
                mb.AddBox(new Vector3(0.30f, 0f, 0f), new Vector3(0.24f, 0.015f, 0.09f));
                mb.PopTransform();
            }
        }

        mb.Material = (int)MatId.Glass;
        Shapes.Canopy(mb, new Vector3(0f, 0.80f, 0.55f), 0.72f, 0.44f, 0.52f, 6);

        // Chin guns.
        LinkedCannons(mb, new Vector3(0f, 0.52f, 1.30f), 0.26f, 0.70f, 0.058f);

        mb.Material = (int)MatId.EnergyPanel;
        Thruster(mb, new Vector3(-0.42f, 0.62f, -1.60f), 0.16f, 0.28f);
        Thruster(mb, new Vector3(0.42f, 0.62f, -1.60f), 0.16f, 0.28f);
    }

    /// <summary>
    /// Raptor: a small attack flier. Slim fuselage with a pointed nose, high swept wings carrying
    /// the engines, twin canted tail fins, and a blown canopy.
    /// </summary>
    private static void BuildRaptor(MeshBuilder mb)
    {
        mb.Material = (int)MatId.ArmorPlate;
        Shapes.Hull(mb, Vector3.Zero,
        [
            new(-2.30f, 0.40f, 0.32f, 0.92f),
            new(-1.70f, 0.58f, 0.44f, 0.92f),
            new(-0.40f, 0.66f, 0.50f, 0.92f),
            new( 0.80f, 0.60f, 0.44f, 0.90f),
            new( 1.70f, 0.42f, 0.30f, 0.86f),
            new( 2.35f, 0.20f, 0.14f, 0.82f),
            new( 2.65f, 0.05f, 0.04f, 0.80f),
        ], FairingSection());

        mb.Material = (int)MatId.SkyMetal;
        foreach (int s in new[] { -1, 1 })
        {
            Shapes.Wing(mb, new Vector3(s * 0.55f, 0.98f, -0.10f), s * 1.55f, 1.30f, 0.62f, 0.16f,
                sweepBack: -0.55f, dihedral: 0.16f, steps: 4);
            // Engine nacelle slung under each wing.
            mb.Material = (int)MatId.TechPanelDark;
            Shapes.Hull(mb, new Vector3(s * 1.35f, 0f, 0f),
            [
                new(-0.95f, 0.24f, 0.22f, 0.82f),
                new(-0.55f, 0.30f, 0.28f, 0.82f),
                new( 0.45f, 0.30f, 0.28f, 0.82f),
                new( 0.85f, 0.20f, 0.18f, 0.82f),
            ], FairingSection());
            mb.Material = (int)MatId.EnergyPanel;
            Thruster(mb, new Vector3(s * 1.35f, 0.82f, -1.05f), 0.24f, 0.32f);
            mb.Material = (int)MatId.SkyMetal;
        }

        // Twin canted tail fins.
        foreach (int s in new[] { -1, 1 })
            Shapes.Wing(mb, new Vector3(s * 0.32f, 1.05f, -1.85f), s * 0.42f, 0.85f, 0.42f, 0.10f,
                sweepBack: -0.40f, dihedral: 0.72f, steps: 3);

        mb.Material = (int)MatId.Glass;
        Shapes.Canopy(mb, new Vector3(0f, 1.16f, 0.85f), 0.72f, 0.36f, 0.42f, 6);

        LinkedCannons(mb, new Vector3(0f, 0.80f, 1.60f), 0.24f, 0.80f, 0.05f);
        // Missile rails outboard.
        mb.Material = (int)MatId.WeaponMetal;
        foreach (int s in new[] { -1, 1 })
            MissilePod(mb, new Vector3(s * 1.95f, 0.86f, 0.10f), 0.13f, 0.13f, 0.46f, 1, 2);
    }

    /// <summary>
    /// Cicada: a two-seat gunship. Deep fuselage, stub wings loaded with missile pods, twin
    /// ducted lift fans and a belly turret for the gunner.
    /// </summary>
    private static void BuildCicada(MeshBuilder mb)
    {
        mb.Material = (int)MatId.ArmorPlate;
        Shapes.Hull(mb, Vector3.Zero,
        [
            new(-2.50f, 0.55f, 0.40f, 1.05f),
            new(-1.90f, 0.85f, 0.58f, 1.05f),
            new(-0.60f, 1.05f, 0.66f, 1.05f),
            new( 0.70f, 1.00f, 0.62f, 1.05f),
            new( 1.70f, 0.78f, 0.46f, 1.02f),
            new( 2.35f, 0.42f, 0.24f, 0.98f),
            new( 2.60f, 0.14f, 0.08f, 0.96f),
        ], FairingSection());

        mb.Material = (int)MatId.Glass;
        Shapes.Canopy(mb, new Vector3(0f, 1.62f, 0.95f), 0.95f, 0.60f, 0.55f, 6);

        // Stub wings and the lift-fan ducts on their tips.
        mb.Material = (int)MatId.SkyMetal;
        foreach (int s in new[] { -1, 1 })
        {
            Shapes.Wing(mb, new Vector3(s * 0.95f, 1.20f, -0.10f), s * 1.00f, 1.10f, 0.85f, 0.20f,
                sweepBack: -0.15f, dihedral: 0.06f, steps: 3);
            Vector3 duct = new(s * 1.95f, 1.26f, -0.20f);
            mb.Material = (int)MatId.TechPanelDark;
            mb.AddLathe(
            [
                new(0.52f, 0.26f), new(0.64f, 0.16f), new(0.66f, -0.08f), new(0.56f, -0.24f),
            ], duct, 20, capBottom: false, capTop: false);
            mb.Material = (int)MatId.EnergyPanel;
            for (int b = 0; b < 5; b++)
            {
                float a = b / 5f * MathX.TwoPi;
                mb.PushTransform(Matrix4x4.CreateRotationY(a) * Matrix4x4.CreateTranslation(duct));
                mb.AddBox(new Vector3(0.30f, 0f, 0f), new Vector3(0.26f, 0.018f, 0.11f));
                mb.PopTransform();
            }
            MissilePod(mb, new Vector3(s * 1.35f, 0.86f, 0.20f), 0.26f, 0.20f, 0.85f, 2, 2);
            mb.Material = (int)MatId.SkyMetal;
        }

        // Belly turret on a short ring.
        mb.Material = (int)MatId.WeaponMetal;
        mb.AddLathe([new Vector2(0.34f, 0f), new Vector2(0.30f, -0.14f)],
            new Vector3(0f, 0.52f, 0.30f), 14, capBottom: false, capTop: false);
        mb.AddSphere(new Vector3(0f, 0.30f, 0.30f), 0.34f, 8, 14);
        LinkedCannons(mb, new Vector3(0f, 0.28f, 0.58f), 0.11f, 0.46f, 0.038f);

        mb.Material = (int)MatId.RustMetal;
        Thruster(mb, new Vector3(-0.45f, 1.10f, -2.55f), 0.24f, 0.34f);
        Thruster(mb, new Vector3(0.45f, 1.10f, -2.55f), 0.24f, 0.34f);
    }

    /// <summary>
    /// Ion Tank: a tracked chassis carrying an ion projector — a caged emitter rather than a
    /// gun tube, with accelerator rings down its length.
    /// </summary>
    private static void BuildIonTank(MeshBuilder mb)
    {
        mb.Material = (int)MatId.ArmorPlate;
        Shapes.Hull(mb, Vector3.Zero,
        [
            new(-3.20f, 1.45f, 0.40f, 0.92f),
            new(-2.50f, 1.66f, 0.50f, 0.96f),
            new( 1.70f, 1.68f, 0.50f, 0.96f),
            new( 2.70f, 1.50f, 0.38f, 0.90f),
            new( 3.20f, 1.20f, 0.22f, 0.84f),
        ], 0.22f);
        Shapes.Hull(mb, Vector3.Zero,
        [
            new(-3.10f, 1.62f, 0.30f, 1.52f),
            new(-1.80f, 1.76f, 0.36f, 1.56f),
            new( 1.00f, 1.76f, 0.36f, 1.56f),
            new( 2.40f, 1.55f, 0.22f, 1.30f),
            new( 3.20f, 1.26f, 0.10f, 1.04f),
        ], 0.24f);

        TurretRing(mb, new Vector3(0f, 1.86f, 0.10f), 1.05f, 0.14f);
        mb.Material = (int)MatId.WeaponMetal;
        Shapes.Hull(mb, new Vector3(0f, 0f, 0.10f),
        [
            new(-1.20f, 0.92f, 0.32f, 2.24f),
            new(-0.20f, 1.05f, 0.42f, 2.30f),
            new( 0.85f, 0.80f, 0.34f, 2.22f),
        ], 0.28f);

        // Projector: a slim core inside a cage of accelerator rings.
        mb.Material = (int)MatId.EnergyPanel;
        Shapes.Barrel(mb, new Vector3(0f, 2.24f, 0.80f),
            [new Vector2(0f, 0.16f), new Vector2(2.60f, 0.13f), new Vector2(2.75f, 0.24f)], 14);
        mb.Material = (int)MatId.Trim;
        for (int i = 0; i < 5; i++)
            mb.AddLathe([new Vector2(0.22f, 0f), new Vector2(0.40f, 0.05f), new Vector2(0.22f, 0.10f)],
                new Vector3(0f, 2.24f, 1.05f + i * 0.55f), 18, capBottom: false, capTop: false);
        foreach (int s in new[] { -1, 1 })
            Shapes.Barrel(mb, new Vector3(s * 0.30f, 2.24f, 0.80f),
                [new Vector2(0f, 0.05f), new Vector2(2.55f, 0.05f)], 8);

        Shapes.TrackRun(mb, -1.78f, 0f, 2.85f, 0.54f, 0.66f, 6);
        Shapes.TrackRun(mb, 1.78f, 0f, 2.85f, 0.54f, 0.66f, 6);
    }

    // ---------------------------------------------------------------- Necris
    // Bladed, asymmetric and organic where the Axon machines are plated and boxy.

    /// <summary>Viper: a bladed hover bike, all leading edge and no bulk.</summary>
    private static void BuildViper(MeshBuilder mb)
    {
        mb.Material = (int)MatId.TechPanelDark;
        Shapes.Hull(mb, Vector3.Zero,
        [
            new(-1.75f, 0.22f, 0.16f, 0.58f),
            new(-1.20f, 0.42f, 0.26f, 0.56f),
            new(-0.20f, 0.52f, 0.28f, 0.54f),
            new( 0.70f, 0.44f, 0.22f, 0.52f),
            new( 1.45f, 0.26f, 0.12f, 0.50f),
            new( 1.90f, 0.06f, 0.03f, 0.48f),
        ], FairingSection());

        // Forward canards and rear blades sweep out from the spine.
        mb.Material = (int)MatId.Trim;
        foreach (int s in new[] { -1, 1 })
        {
            Span<MeshBuilder.LoftStation> canard =
            [
                new(new Vector3(s * 0.30f, 0.54f, 0.55f), new Vector2(0.42f, 0.055f)),
                new(new Vector3(s * 0.90f, 0.58f, 0.30f), new Vector2(0.36f, 0.035f)),
                new(new Vector3(s * 1.25f, 0.62f, -0.10f), new Vector2(0.20f, 0.018f)),
            ];
            mb.AddLoft(Sections.Airfoil(1f, 0.36f, 5), canard);

            Span<MeshBuilder.LoftStation> blade =
            [
                new(new Vector3(s * 0.24f, 0.52f, -1.05f), new Vector2(0.34f, 0.05f)),
                new(new Vector3(s * 0.78f, 0.60f, -1.45f), new Vector2(0.26f, 0.03f)),
                new(new Vector3(s * 1.02f, 0.70f, -1.90f), new Vector2(0.12f, 0.012f)),
            ];
            mb.AddLoft(Sections.Airfoil(1f, 0.36f, 5), blade);
        }

        mb.Material = (int)MatId.EnergyPanel;
        Thruster(mb, new Vector3(0f, 0.56f, -1.75f), 0.20f, 0.30f);
        // The self-destruct core, glowing through the spine.
        mb.AddLathe([new Vector2(0.13f, -0.28f), new Vector2(0.19f, 0f), new Vector2(0.13f, 0.28f)],
            new Vector3(0f, 0.60f, -0.20f), 12, capBottom: false, capTop: false);
    }

    /// <summary>Scavenger: an energy sphere carried in a three-legged cradle.</summary>
    private static void BuildScavenger(MeshBuilder mb)
    {
        mb.Material = (int)MatId.EnergyPanel;
        mb.AddSphere(new Vector3(0f, 1.55f, 0f), 0.72f, 12, 18);
        // Cradle: three curved ribs wrapping the core.
        mb.Material = (int)MatId.TechPanelDark;
        for (int i = 0; i < 3; i++)
        {
            float a = i / 3f * MathX.TwoPi + 0.4f;
            var arc = new List<MeshBuilder.LoftStation>();
            for (int k = 0; k <= 8; k++)
            {
                float t = k / 8f * MathX.Pi - MathX.HalfPi;
                arc.Add(new MeshBuilder.LoftStation(
                    new Vector3(MathF.Cos(a) * MathF.Cos(t) * 0.82f, 1.55f + MathF.Sin(t) * 0.82f,
                        MathF.Sin(a) * MathF.Cos(t) * 0.82f), new Vector2(0.10f, 0.05f)));
            }
            mb.AddLoft(Sections.RoundedRect(1f, 1f, 0.4f, 2),
                System.Runtime.InteropServices.CollectionsMarshal.AsSpan(arc), false, false);
        }
        for (int i = 0; i < 3; i++)
            WalkerLeg(mb, new Vector3(0f, 1.45f, 0f), i / 3f * MathX.TwoPi + 0.4f, 1.15f, 1.55f, 0.10f);
    }

    /// <summary>
    /// Nemesis: a low tracked tank whose turret rides on a telescoping column — it rises for
    /// reach and hunkers for cover. The exposed column between chassis and turret is the whole
    /// silhouette, so it is modelled as visible ram segments rather than hidden inside the body.
    /// </summary>
    private static void BuildNemesis(MeshBuilder mb)
    {
        mb.Material = (int)MatId.TechPanelDark;
        // Chassis: deliberately squat and wide, so the raised turret reads as tall by contrast.
        Shapes.Hull(mb, Vector3.Zero,
        [
            new(-2.10f, 1.05f, 0.26f, 0.72f),
            new(-1.55f, 1.28f, 0.34f, 0.76f),
            new( 1.05f, 1.30f, 0.34f, 0.76f),
            new( 1.90f, 1.10f, 0.26f, 0.72f),
            new( 2.30f, 0.72f, 0.14f, 0.66f),
        ], ArmourSection());

        // Telescoping column: three ram segments of decreasing diameter.
        mb.Material = (int)MatId.Trim;
        mb.AddLathe(
        [
            new(0.44f, 0f), new(0.44f, 0.34f), new(0.38f, 0.36f),
            new(0.38f, 0.68f), new(0.32f, 0.70f), new(0.32f, 1.02f),
        ], new Vector3(0f, 1.04f, 0.10f), 18, capBottom: false, capTop: false);
        mb.Material = (int)MatId.EnergyPanel;
        foreach (float y in new[] { 1.40f, 1.74f })
            mb.AddLathe([new Vector2(0.34f, 0f), new Vector2(0.42f, 0.05f), new Vector2(0.34f, 0.10f)],
                new Vector3(0f, y, 0.10f), 16, capBottom: false, capTop: false);

        // Turret: a narrow blade-fronted head carrying an oversized cannon.
        TurretRing(mb, new Vector3(0f, 2.06f, 0.10f), 0.56f, 0.10f);
        mb.Material = (int)MatId.ArmorPlate;
        Shapes.Hull(mb, new Vector3(0f, 0f, 0.10f),
        [
            new(-0.90f, 0.52f, 0.30f, 2.32f),
            new(-0.15f, 0.66f, 0.40f, 2.36f),
            new( 0.55f, 0.48f, 0.30f, 2.30f),
            new( 0.85f, 0.24f, 0.16f, 2.26f),
        ], FairingSection());
        mb.Material = (int)MatId.WeaponMetal;
        Shapes.Barrel(mb, new Vector3(0f, 2.30f, 0.70f),
        [
            new(0f, 0.22f), new(0.22f, 0.24f), new(0.28f, 0.15f),
            new(1.55f, 0.13f), new(1.66f, 0.24f), new(1.90f, 0.24f), new(1.90f, 0.10f),
        ], 14);
        mb.Material = (int)MatId.EnergyPanel;
        foreach (int s in new[] { -1, 1 })
            Shapes.RoundedBox(mb, new Vector3(s * 0.46f, 2.36f, 0.05f), new Vector3(0.06f, 0.18f, 0.30f), 0.04f);

        Shapes.TrackRun(mb, -1.42f, 0f, 2.00f, 0.40f, 0.50f, 4);
        Shapes.TrackRun(mb, 1.42f, 0f, 2.00f, 0.40f, 0.50f, 4);
    }

    /// <summary>
    /// Nightshade: a low, wide hover platform. Smooth carapace with almost no protrusions —
    /// what makes it read is the way its edges taper to a blade all the way round.
    /// </summary>
    private static void BuildNightshade(MeshBuilder mb)
    {
        mb.Material = (int)MatId.TechPanelDark;
        Shapes.Hull(mb, Vector3.Zero,
        [
            new(-2.20f, 0.55f, 0.14f, 0.82f),
            new(-1.55f, 1.00f, 0.28f, 0.80f),
            new(-0.45f, 1.20f, 0.34f, 0.78f),
            new( 0.70f, 1.12f, 0.30f, 0.78f),
            new( 1.65f, 0.78f, 0.18f, 0.76f),
            new( 2.25f, 0.24f, 0.05f, 0.74f),
        ], FairingSection());

        // Dorsal spine housing the deployables.
        mb.Material = (int)MatId.Trim;
        Shapes.Hull(mb, Vector3.Zero,
        [
            new(-1.15f, 0.52f, 0.16f, 1.14f),
            new(-0.30f, 0.62f, 0.22f, 1.18f),
            new( 0.70f, 0.48f, 0.16f, 1.12f),
        ], 0.34f, 3);
        mb.Material = (int)MatId.EnergyPanel;
        foreach (int s in new[] { -1, 1 })
        {
            mb.AddLathe([new Vector2(0.30f, 0.06f), new Vector2(0.36f, 0f), new Vector2(0.30f, -0.08f)],
                new Vector3(s * 0.95f, 0.58f, 0.20f), 16, capBottom: false, capTop: false);
        }
        Thruster(mb, new Vector3(-0.42f, 0.78f, -2.10f), 0.17f, 0.26f);
        Thruster(mb, new Vector3(0.42f, 0.78f, -2.10f), 0.17f, 0.26f);
    }

    /// <summary>Fury: a Necris interceptor — a thin dart with long curved blade wings.</summary>
    private static void BuildFury(MeshBuilder mb)
    {
        mb.Material = (int)MatId.TechPanelDark;
        Shapes.Hull(mb, Vector3.Zero,
        [
            new(-2.05f, 0.30f, 0.24f, 0.82f),
            new(-1.40f, 0.46f, 0.36f, 0.82f),
            new(-0.20f, 0.52f, 0.40f, 0.82f),
            new( 1.00f, 0.42f, 0.30f, 0.80f),
            new( 1.85f, 0.22f, 0.14f, 0.78f),
            new( 2.30f, 0.05f, 0.03f, 0.76f),
        ], FairingSection());

        // Blade wings sweeping back and curving up at the tips.
        mb.Material = (int)MatId.Trim;
        foreach (int s in new[] { -1, 1 })
        {
            Span<MeshBuilder.LoftStation> wing =
            [
                new(new Vector3(s * 0.35f, 0.84f, 0.35f), new Vector2(0.85f, 0.075f)),
                new(new Vector3(s * 1.05f, 0.90f, -0.05f), new Vector2(0.72f, 0.055f)),
                new(new Vector3(s * 1.70f, 1.02f, -0.55f), new Vector2(0.50f, 0.035f)),
                new(new Vector3(s * 2.05f, 1.30f, -0.95f), new Vector2(0.22f, 0.018f)),
            ];
            mb.AddLoft(Sections.Airfoil(1f, 0.30f, 6), wing);
        }
        mb.Material = (int)MatId.EnergyPanel;
        Thruster(mb, new Vector3(0f, 0.82f, -2.05f), 0.24f, 0.34f);
        LinkedCannons(mb, new Vector3(0f, 0.74f, 1.35f), 0.17f, 0.60f, 0.042f);
    }

    /// <summary>
    /// Darkwalker: a tripod. The body hangs from the top of three very long reversed-knee legs,
    /// so the whole thing reads as height and reach before anything else.
    /// </summary>
    private static void BuildDarkwalker(MeshBuilder mb)
    {
        const float bodyY = 4.60f;
        mb.Material = (int)MatId.ArmorPlate;
        // Carapace: a broad curved shell, deepest at the front where the head hangs.
        Shapes.Hull(mb, new Vector3(0f, 0f, 0f),
        [
            new(-2.10f, 0.75f, 0.30f, bodyY),
            new(-1.40f, 1.30f, 0.55f, bodyY + 0.10f),
            new(-0.20f, 1.55f, 0.70f, bodyY + 0.15f),
            new( 1.00f, 1.40f, 0.62f, bodyY + 0.05f),
            new( 1.90f, 0.95f, 0.40f, bodyY - 0.15f),
            new( 2.35f, 0.45f, 0.18f, bodyY - 0.35f),
        ], FairingSection());

        // Head: slung below and forward, carrying the twin beam cannons.
        mb.Material = (int)MatId.TechPanelDark;
        Shapes.Hull(mb, Vector3.Zero,
        [
            new( 0.85f, 0.72f, 0.42f, bodyY - 0.55f),
            new( 1.55f, 0.88f, 0.52f, bodyY - 0.70f),
            new( 2.30f, 0.70f, 0.42f, bodyY - 0.80f),
            new( 2.70f, 0.34f, 0.20f, bodyY - 0.85f),
        ], FairingSection());
        mb.Material = (int)MatId.EnergyPanel;
        foreach (int s in new[] { -1, 1 })
        {
            Shapes.Barrel(mb, new Vector3(s * 0.44f, bodyY - 0.78f, 2.35f),
                [new Vector2(0f, 0.13f), new Vector2(1.20f, 0.10f), new Vector2(1.35f, 0.19f)], 12);
        }
        // Sensor cluster on the crown.
        mb.AddLathe([new Vector2(0.36f, 0f), new Vector2(0.44f, 0.14f), new Vector2(0.22f, 0.30f)],
            new Vector3(0f, bodyY + 0.80f, 0.20f), 16, capBottom: false);

        // Three legs on tall hips.
        mb.Material = (int)MatId.ArmorPlate;
        for (int i = 0; i < 3; i++)
        {
            float a = i / 3f * MathX.TwoPi + MathX.HalfPi;
            Vector3 hip = new(MathF.Cos(a) * 1.05f, bodyY - 0.25f, MathF.Sin(a) * 1.05f);
            Shapes.RoundedBox(mb, hip, new Vector3(0.42f, 0.38f, 0.42f), 0.20f, 3, 3);
            WalkerLeg(mb, hip, a, 2.55f, bodyY - 0.25f, 0.26f);
        }
    }

    /// <summary>Hoverboard: a deck with foot bindings and a lit underside. No weapons at all.</summary>
    private static void BuildHoverboard(MeshBuilder mb)
    {
        mb.Material = (int)MatId.Trim;
        // Deck: a long tapered plank with turned-up tips.
        Span<MeshBuilder.LoftStation> deck =
        [
            new(new Vector3(0f, 0.50f, -1.22f), new Vector2(0.14f, 0.020f)),
            new(new Vector3(0f, 0.44f, -1.00f), new Vector2(0.26f, 0.035f)),
            new(new Vector3(0f, 0.42f, -0.35f), new Vector2(0.34f, 0.045f)),
            new(new Vector3(0f, 0.42f, 0.35f), new Vector2(0.34f, 0.045f)),
            new(new Vector3(0f, 0.44f, 1.00f), new Vector2(0.26f, 0.035f)),
            new(new Vector3(0f, 0.50f, 1.22f), new Vector2(0.14f, 0.020f)),
        ];
        mb.AddLoft(Sections.RoundedRect(1f, 1f, 0.45f, 3), deck);

        mb.Material = (int)MatId.TechPanelDark;
        foreach (float z in new[] { 0.48f, -0.48f })
            Shapes.RoundedBox(mb, new Vector3(0f, 0.49f, z), new Vector3(0.20f, 0.035f, 0.12f), 0.03f);

        mb.Material = (int)MatId.EnergyPanel;
        foreach (float z in new[] { 0.70f, -0.70f })
            mb.AddLathe([new Vector2(0.20f, 0f), new Vector2(0.24f, -0.04f), new Vector2(0.16f, -0.09f)],
                new Vector3(0f, 0.39f, z), 14, capBottom: false, capTop: false);
    }

    public void Dispose()
    {
        foreach (var m in _meshes) m?.Dispose();
    }
}
