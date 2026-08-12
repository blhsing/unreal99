using System.Numerics;
using Silk.NET.OpenGL;
using Unreal99.Core;
using Unreal99.Rendering;

namespace Unreal99.Game;

/// <summary>
/// Which cockpit a seat shows. Four archetypes rather than one per vehicle: what a player needs
/// to read at a glance is <em>which job they have</em> — am I steering, am I on a gun, or am I
/// just being carried — and that is a property of the seat, not of the chassis. The vehicle's own
/// tint is applied on top, so a Goliath's interior and a Manta's are still visibly different.
/// </summary>
public enum CockpitKind
{
    /// <summary>Ground driver: a steering yoke across the lower view.</summary>
    Wheel = 0,
    /// <summary>Air and hover pilot: a centre control column with a throttle grip.</summary>
    Stick,
    /// <summary>Any armed seat: a gun mount with twin grips and an ammo feed.</summary>
    Gun,
    /// <summary>Unarmed and not driving: a grab rail, so the seat reads as a passenger seat.</summary>
    Rail,
    Count,
}

/// <summary>
/// First-person interiors. A rider used to see the same floating hands-and-gun as someone on
/// foot, which said nothing about being in a vehicle at all — let alone which seat. These meshes
/// replace the weapon view model for anyone aboard.
/// </summary>
public sealed class CockpitModels : IDisposable
{
    private readonly Mesh[] _meshes = new Mesh[(int)CockpitKind.Count];
    private readonly MeshSection[][] _sections = new MeshSection[(int)CockpitKind.Count][];

    public Mesh MeshFor(CockpitKind k) => _meshes[(int)k];
    public MeshSection[] SectionsFor(CockpitKind k) => _sections[(int)k];

    /// <summary>Which interior a given seat of a given vehicle shows.</summary>
    public static CockpitKind For(VehicleDef def, int seat)
    {
        if (seat < 0 || seat >= def.Seats.Length) return CockpitKind.Rail;
        var s = def.Seats[seat];
        if (seat == 0)
            return def.Motion is VehicleMotion.Air or VehicleMotion.Hover
                ? CockpitKind.Stick
                : CockpitKind.Wheel;
        return s.Armed ? CockpitKind.Gun : CockpitKind.Rail;
    }

    /// <summary>
    /// Triangle count without a GL context, so the density floor can be enforced by a headless
    /// self-test. Mirrors <see cref="WeaponModels.TriangleCountFor"/>.
    /// </summary>
    public static int TriangleCountFor(CockpitKind kind)
    {
        var mb = new MeshBuilder { WorldUv = false, Material = (int)MatId.ArmorPlate };
        Build(kind, mb);
        var (_, indices, _) = mb.Build();
        return indices.Length / 3;
    }

    public CockpitModels(GL gl)
    {
        for (int i = 0; i < (int)CockpitKind.Count; i++)
        {
            var mb = new MeshBuilder { WorldUv = false, Material = (int)MatId.ArmorPlate };
            Build((CockpitKind)i, mb);
            mb.RecalculateTangents();
            var (v, ind, s) = mb.Build();
            _meshes[i] = Mesh.CreateStatic<Vertex>(gl, v, ind, VertexLayouts.Static);
            _sections[i] = s;
        }
    }

    private static void Build(CockpitKind kind, MeshBuilder mb)
    {
        // Every interior shares the same surround: a dashboard shelf below the view and two
        // canopy pillars at the edges. That framing is what makes the shot read as "inside
        // something" instead of "a prop floating in front of the camera".
        Surround(mb);
        switch (kind)
        {
            case CockpitKind.Wheel: SteeringYoke(mb); break;
            case CockpitKind.Stick: ControlColumn(mb); break;
            case CockpitKind.Gun: GunMount(mb); break;
            default: GrabRail(mb); break;
        }
    }

    // ================================================================ shared detail
    // The same reasoning as the weapon models: an interior seen from half a metre away needs
    // hardware on it. These are the parts that appear on every panel of every machine.

    /// <summary>A row of bolt heads along an edge — the cheapest thing that reads as "assembled".</summary>
    private static void Bolts(MeshBuilder mb, Vector3 from, Vector3 to, int count, float radius)
    {
        int restore = mb.Material;
        mb.Material = (int)MatId.WeaponMetal;
        for (int i = 0; i < count; i++)
        {
            Vector3 p = Vector3.Lerp(from, to, count == 1 ? 0.5f : i / (float)(count - 1));
            mb.AddSphere(p, radius, 6, 8);
        }
        mb.Material = restore;
    }

    /// <summary>A dial with a raised bezel, a face and a needle. Dashboards live on these.</summary>
    private static void Gauge(MeshBuilder mb, Vector3 centre, float radius, float needle)
    {
        int restore = mb.Material;
        mb.Material = (int)MatId.WeaponMetal;
        mb.AddTorus(centre, radius, radius * 0.17f, 18, 8);
        mb.Material = (int)MatId.TechPanelDark;
        mb.AddBox(centre + new Vector3(0f, 0f, 0.006f), new Vector3(radius * 0.9f, radius * 0.9f, 0.006f));
        mb.Material = (int)MatId.EnergyPanel;
        // Needle, laid across the face at the given angle.
        float c = MathF.Cos(needle), s = MathF.Sin(needle);
        for (int i = 1; i <= 4; i++)
        {
            float t = i / 4f * radius * 0.72f;
            mb.AddBox(centre + new Vector3(c * t, s * t, -0.004f),
                new Vector3(radius * 0.055f, radius * 0.055f, 0.005f));
        }
        mb.Material = restore;
    }

    /// <summary>Vent louvres: a stack of angled slats over an intake.</summary>
    private static void Louvres(MeshBuilder mb, Vector3 centre, Vector2 half, int slats)
    {
        int restore = mb.Material;
        mb.Material = (int)MatId.TechPanelDark;
        mb.AddBox(centre, new Vector3(half.X, half.Y, 0.012f));
        mb.Material = (int)MatId.RustMetal;
        for (int i = 0; i < slats; i++)
        {
            float t = slats == 1 ? 0.5f : i / (float)(slats - 1);
            float y = MathX.Lerp(-half.Y + 0.012f, half.Y - 0.012f, t);
            mb.AddBox(centre + new Vector3(0f, y, -0.010f),
                new Vector3(half.X * 0.92f, 0.006f, 0.010f));
        }
        mb.Material = restore;
    }

    /// <summary>A bank of toggle switches, the detail that fills the dead space on a console.</summary>
    private static void Switches(MeshBuilder mb, Vector3 from, Vector3 to, int count)
    {
        int restore = mb.Material;
        for (int i = 0; i < count; i++)
        {
            Vector3 p = Vector3.Lerp(from, to, count == 1 ? 0.5f : i / (float)(count - 1));
            mb.Material = (int)MatId.WeaponMetal;
            mb.AddBox(p, new Vector3(0.016f, 0.011f, 0.010f));
            mb.Material = (int)MatId.Trim;
            mb.AddBox(p + new Vector3(0f, 0.016f, -0.004f), new Vector3(0.006f, 0.014f, 0.006f));
        }
        mb.Material = restore;
    }

    /// <summary>Ribbing around a grip, so a hand-held control does not read as a smooth rod.</summary>
    private static void GripRibs(MeshBuilder mb, Vector3 from, Vector3 to, int ribs, float radius)
    {
        int restore = mb.Material;
        mb.Material = (int)MatId.TechPanelDark;
        for (int i = 0; i < ribs; i++)
        {
            Vector3 p = Vector3.Lerp(from, to, (i + 0.5f) / ribs);
            mb.AddTorus(p, radius, radius * 0.32f, 10, 6);
        }
        mb.Material = restore;
    }

    /// <summary>
    /// Dashboard shelf and canopy pillars, common to every seat.
    ///
    /// Sized against the actual projection rather than by eye. The camera runs a 90° vertical
    /// field of view, so at depth d the visible half-height is exactly d and the half-width is
    /// d × aspect — about 1.1 at the <see cref="Depth"/> used here on a 16:9 view. Authoring to
    /// half those numbers, which is what "looks about right" produces, puts the whole interior in
    /// the middle of the screen where it blocks the crosshair and still fails to reach the edges.
    /// </summary>
    private const float Depth = 0.62f;

    private static void Surround(MeshBuilder mb)
    {
        mb.Material = (int)MatId.ArmorPlate;
        // Dash. Its top edge sits about two thirds of the way down the view, so it frames the
        // bottom without eating the part of the screen a driver is actually looking through.
        mb.AddBox(new Vector3(0f, -0.60f, -Depth), new Vector3(0.95f, 0.19f, 0.16f));
        mb.Material = (int)MatId.TechPanelDark;
        mb.AddBox(new Vector3(0f, -0.405f, -Depth - 0.03f), new Vector3(0.74f, 0.022f, 0.11f));
        // A lit instrument strip. Emissive trim is what sells a dark interior at a glance.
        mb.Material = (int)MatId.EnergyPanel;
        for (int i = -3; i <= 3; i++)
            mb.AddBox(new Vector3(i * 0.17f, -0.386f, -Depth - 0.05f), new Vector3(0.055f, 0.007f, 0.035f));

        // Instrument cluster: three dials, a vent and a switch bank. This is what turns the dash
        // from a shelf into something a crew would actually work at.
        Gauge(mb, new Vector3(-0.46f, -0.50f, -Depth - 0.12f), 0.085f, 2.3f);
        Gauge(mb, new Vector3(-0.25f, -0.53f, -Depth - 0.12f), 0.062f, 1.1f);
        Gauge(mb, new Vector3(0.46f, -0.50f, -Depth - 0.12f), 0.085f, 0.7f);
        Louvres(mb, new Vector3(0.22f, -0.53f, -Depth - 0.13f), new Vector2(0.13f, 0.055f), 5);
        Switches(mb, new Vector3(-0.10f, -0.62f, -Depth - 0.13f),
            new Vector3(0.10f, -0.62f, -Depth - 0.13f), 5);
        // Seams and fasteners along the dash lip.
        Bolts(mb, new Vector3(-0.88f, -0.415f, -Depth - 0.10f),
            new Vector3(0.88f, -0.415f, -Depth - 0.10f), 13, 0.014f);

        // Canopy pillars at the frame edges, swept back so they never cross the crosshair.
        mb.Material = (int)MatId.WeaponMetal;
        foreach (float sx in new[] { -1f, 1f })
        {
            Span<MeshBuilder.LoftStation> pillar =
            [
                new(new Vector3(sx * 0.90f, -0.52f, -Depth + 0.04f), new Vector2(0.070f, 0.070f)),
                new(new Vector3(sx * 0.93f, -0.26f, -Depth), new Vector2(0.062f, 0.062f)),
                new(new Vector3(sx * 0.96f, 0.02f, -Depth - 0.02f), new Vector2(0.055f, 0.055f)),
                new(new Vector3(sx * 0.98f, 0.27f, -Depth - 0.05f), new Vector2(0.049f, 0.049f)),
                new(new Vector3(sx * 1.00f, 0.50f, -Depth - 0.08f), new Vector2(0.045f, 0.045f)),
            ];
            mb.AddLoft(Sections.RoundedRect(1f, 0.85f, 0.35f, 3), pillar, false, false);
            // Rivet lines up the pillar, and a reinforcing collar where it meets the dash.
            Bolts(mb, new Vector3(sx * 0.885f, -0.44f, -Depth + 0.055f),
                new Vector3(sx * 0.975f, 0.44f, -Depth - 0.06f), 7, 0.013f);
            mb.Material = (int)MatId.RustMetal;
            mb.AddTorus(new Vector3(sx * 0.905f, -0.44f, -Depth + 0.02f), 0.085f, 0.022f, 12, 6);
            mb.Material = (int)MatId.WeaponMetal;
        }
        // Roof lip across the top of the view, with its own fastener row.
        mb.Material = (int)MatId.ArmorPlate;
        mb.AddBox(new Vector3(0f, 0.58f, -Depth - 0.06f), new Vector3(1.02f, 0.055f, 0.09f));
        mb.Material = (int)MatId.TechPanelDark;
        mb.AddBox(new Vector3(0f, 0.522f, -Depth - 0.08f), new Vector3(0.94f, 0.018f, 0.055f));
        Bolts(mb, new Vector3(-0.90f, 0.575f, -Depth - 0.12f),
            new Vector3(0.90f, 0.575f, -Depth - 0.12f), 11, 0.015f);
        // Corner gussets tying the roof to the pillars.
        mb.Material = (int)MatId.ArmorPlate;
        foreach (float sx in new[] { -1f, 1f })
            mb.AddBox(new Vector3(sx * 0.93f, 0.50f, -Depth - 0.055f), new Vector3(0.075f, 0.075f, 0.06f));
    }

    /// <summary>A ground driver's steering yoke: a flattened wheel on a short column.</summary>
    private static void SteeringYoke(MeshBuilder mb)
    {
        mb.Material = (int)MatId.WeaponMetal;
        mb.AddBox(new Vector3(0f, -0.60f, -0.50f), new Vector3(0.045f, 0.11f, 0.045f));
        mb.Material = (int)MatId.Trim;
        // Ring, squashed vertically the way a racing yoke is.
        mb.AddTorus(new Vector3(0f, -0.47f, -0.50f), 0.235f, 0.032f, 32, 10);
        mb.AddBox(new Vector3(0f, -0.47f, -0.50f), new Vector3(0.20f, 0.022f, 0.022f));
        // Grip wrap either side of the rim, where the hands would sit.
        foreach (float side in new[] { -1f, 1f })
            for (int i = 0; i < 5; i++)
            {
                float a = side * (0.55f + i * 0.16f);
                mb.Material = (int)MatId.TechPanelDark;
                mb.AddBox(new Vector3(MathF.Cos(a) * 0.235f, -0.47f + MathF.Sin(a) * 0.235f, -0.50f),
                    new Vector3(0.030f, 0.030f, 0.041f));
            }
        // Hub with a horn boss and the spokes out to the rim.
        mb.Material = (int)MatId.WeaponMetal;
        mb.AddSphere(new Vector3(0f, -0.47f, -0.505f), 0.058f, 10, 14);
        mb.Material = (int)MatId.EnergyPanel;
        mb.AddTorus(new Vector3(0f, -0.47f, -0.53f), 0.036f, 0.010f, 14, 6);
        mb.Material = (int)MatId.TechPanelDark;
        foreach (float sx in new[] { -1f, 1f })
            mb.AddBox(new Vector3(sx * 0.205f, -0.47f, -0.50f), new Vector3(0.042f, 0.062f, 0.040f));
        Bolts(mb, new Vector3(-0.06f, -0.53f, -0.520f), new Vector3(0.06f, -0.53f, -0.520f), 3, 0.012f);
        // Gear lever to the right, which reads immediately as a driving position.
        mb.Material = (int)MatId.WeaponMetal;
        Span<MeshBuilder.LoftStation> lever =
        [
            new(new Vector3(0.50f, -0.62f, -0.46f), new Vector2(0.026f, 0.026f)),
            new(new Vector3(0.53f, -0.47f, -0.43f), new Vector2(0.019f, 0.019f)),
        ];
        mb.AddLoft(Sections.Circle(1f, 8), lever, true, false);
        GripRibs(mb, new Vector3(0.505f, -0.58f, -0.45f), new Vector3(0.525f, -0.49f, -0.435f), 4, 0.026f);
        mb.Material = (int)MatId.Trim;
        mb.AddSphere(new Vector3(0.53f, -0.452f, -0.428f), 0.036f, 10, 14);
        // Gate plate the lever runs in, and the pedal box below the column.
        mb.Material = (int)MatId.TechPanelDark;
        mb.AddBox(new Vector3(0.50f, -0.635f, -0.46f), new Vector3(0.075f, 0.014f, 0.075f));
        Bolts(mb, new Vector3(0.44f, -0.618f, -0.46f), new Vector3(0.56f, -0.618f, -0.46f), 4, 0.011f);
        mb.Material = (int)MatId.RustMetal;
        foreach (float sx in new[] { -0.19f, 0.06f })
        {
            mb.AddBox(new Vector3(sx, -0.78f, -0.44f), new Vector3(0.055f, 0.014f, 0.075f));
            Bolts(mb, new Vector3(sx - 0.035f, -0.762f, -0.44f),
                new Vector3(sx + 0.035f, -0.762f, -0.44f), 2, 0.010f);
        }
    }

    /// <summary>A pilot's centre stick, plus a throttle on the left.</summary>
    private static void ControlColumn(MeshBuilder mb)
    {
        mb.Material = (int)MatId.WeaponMetal;
        Span<MeshBuilder.LoftStation> column =
        [
            new(new Vector3(0f, -0.78f, -0.44f), new Vector2(0.048f, 0.048f)),
            new(new Vector3(0f, -0.58f, -0.46f), new Vector2(0.034f, 0.034f)),
            new(new Vector3(0f, -0.46f, -0.47f), new Vector2(0.040f, 0.040f)),
        ];
        mb.AddLoft(Sections.Circle(1f, 8), column, true, false);
        mb.Material = (int)MatId.TechPanelDark;
        mb.AddBox(new Vector3(0f, -0.415f, -0.47f), new Vector3(0.070f, 0.055f, 0.046f));
        mb.Material = (int)MatId.EnergyPanel;
        mb.AddBox(new Vector3(0f, -0.368f, -0.492f), new Vector3(0.028f, 0.009f, 0.018f));

        // Throttle quadrant.
        mb.Material = (int)MatId.WeaponMetal;
        mb.AddBox(new Vector3(-0.56f, -0.62f, -0.42f), new Vector3(0.065f, 0.028f, 0.10f));
        Span<MeshBuilder.LoftStation> throttle =
        [
            new(new Vector3(-0.56f, -0.60f, -0.36f), new Vector2(0.024f, 0.024f)),
            new(new Vector3(-0.56f, -0.49f, -0.40f), new Vector2(0.019f, 0.019f)),
        ];
        mb.AddLoft(Sections.Circle(1f, 8), throttle, true, false);
        GripRibs(mb, new Vector3(-0.56f, -0.58f, -0.37f), new Vector3(-0.56f, -0.50f, -0.395f), 3, 0.024f);
        mb.Material = (int)MatId.Trim;
        mb.AddSphere(new Vector3(-0.56f, -0.475f, -0.405f), 0.032f, 10, 14);
        // Detent notches along the quadrant, plus a trim wheel on the other side.
        mb.Material = (int)MatId.TechPanelDark;
        for (int i = 0; i < 5; i++)
            mb.AddBox(new Vector3(-0.56f, -0.592f, -0.48f + i * 0.036f),
                new Vector3(0.048f, 0.007f, 0.008f));
        mb.Material = (int)MatId.WeaponMetal;
        mb.AddTorus(new Vector3(0.50f, -0.60f, -0.44f), 0.070f, 0.020f, 18, 8);
        Bolts(mb, new Vector3(0.44f, -0.66f, -0.44f), new Vector3(0.56f, -0.66f, -0.44f), 3, 0.012f);
        // Rudder pedals, which is what says "aircraft" rather than "car" at a glance.
        mb.Material = (int)MatId.RustMetal;
        foreach (float sx in new[] { -0.20f, 0.08f })
        {
            mb.AddBox(new Vector3(sx, -0.80f, -0.46f), new Vector3(0.060f, 0.016f, 0.085f));
            GripRibs(mb, new Vector3(sx, -0.784f, -0.53f), new Vector3(sx, -0.784f, -0.39f), 3, 0.020f);
        }
    }

    /// <summary>A gunner's mount: breech block, twin grips and a belt feed.</summary>
    private static void GunMount(MeshBuilder mb)
    {
        mb.Material = (int)MatId.WeaponMetal;
        // Breech, receding towards the view so it reads as the back end of a weapon.
        mb.AddBox(new Vector3(0f, -0.46f, -0.56f), new Vector3(0.135f, 0.105f, 0.20f));
        mb.Material = (int)MatId.TechPanelDark;
        mb.AddBox(new Vector3(0f, -0.348f, -0.56f), new Vector3(0.085f, 0.024f, 0.17f));
        // Twin spade grips, angled back into the hands.
        mb.Material = (int)MatId.Trim;
        foreach (float sx in new[] { -1f, 1f })
        {
            Span<MeshBuilder.LoftStation> grip =
            [
                new(new Vector3(sx * 0.165f, -0.48f, -0.46f), new Vector2(0.034f, 0.034f)),
                new(new Vector3(sx * 0.200f, -0.60f, -0.38f), new Vector2(0.028f, 0.028f)),
            ];
            mb.AddLoft(Sections.Circle(1f, 8), grip, true, false);
            GripRibs(mb, new Vector3(sx * 0.170f, -0.50f, -0.45f),
                new Vector3(sx * 0.198f, -0.59f, -0.385f), 4, 0.032f);
            mb.Material = (int)MatId.Trim;
            mb.AddSphere(new Vector3(sx * 0.205f, -0.618f, -0.368f), 0.036f, 10, 14);
            // Thumb trigger on the inside face of each grip.
            mb.Material = (int)MatId.EnergyPanel;
            mb.AddBox(new Vector3(sx * 0.140f, -0.545f, -0.415f), new Vector3(0.016f, 0.026f, 0.014f));
            mb.Material = (int)MatId.Trim;
        }
        // Ammo feed running in from the right, with belt links and a fastener row.
        mb.Material = (int)MatId.RustMetal;
        mb.AddBox(new Vector3(0.28f, -0.45f, -0.56f), new Vector3(0.13f, 0.045f, 0.075f));
        mb.Material = (int)MatId.WeaponMetal;
        for (int i = 0; i < 6; i++)
            mb.AddBox(new Vector3(0.17f + i * 0.042f, -0.405f, -0.56f),
                new Vector3(0.016f, 0.016f, 0.048f));
        Bolts(mb, new Vector3(0.17f, -0.492f, -0.50f), new Vector3(0.39f, -0.492f, -0.50f), 5, 0.012f);
        // Recoil buffer and elevation screw on the left, so the mount reads as a machine.
        mb.Material = (int)MatId.WeaponMetal;
        Span<MeshBuilder.LoftStation> buffer =
        [
            new(new Vector3(-0.30f, -0.46f, -0.50f), new Vector2(0.042f, 0.042f)),
            new(new Vector3(-0.30f, -0.46f, -0.66f), new Vector2(0.030f, 0.030f)),
        ];
        mb.AddLoft(Sections.Circle(1f, 10), buffer, true, true);
        GripRibs(mb, new Vector3(-0.30f, -0.46f, -0.52f), new Vector3(-0.30f, -0.46f, -0.64f), 5, 0.036f);
        mb.Material = (int)MatId.TechPanelDark;
        mb.AddTorus(new Vector3(-0.30f, -0.58f, -0.54f), 0.055f, 0.016f, 16, 8);
        // Sight bracket above the breech.
        mb.Material = (int)MatId.WeaponMetal;
        mb.AddBox(new Vector3(0f, -0.322f, -0.66f), new Vector3(0.055f, 0.028f, 0.020f));
        mb.Material = (int)MatId.EnergyPanel;
        mb.AddBox(new Vector3(0f, -0.372f, -0.74f), new Vector3(0.045f, 0.011f, 0.028f));
        mb.AddTorus(new Vector3(0f, -0.322f, -0.68f), 0.030f, 0.008f, 14, 6);
    }

    /// <summary>An unarmed passenger's grab rail.</summary>
    private static void GrabRail(MeshBuilder mb)
    {
        mb.Material = (int)MatId.WeaponMetal;
        Span<MeshBuilder.LoftStation> rail =
        [
            new(new Vector3(-0.62f, -0.50f, -0.50f), new Vector2(0.038f, 0.038f)),
            new(new Vector3(-0.30f, -0.42f, -0.56f), new Vector2(0.038f, 0.038f)),
            new(new Vector3(0.30f, -0.42f, -0.56f), new Vector2(0.038f, 0.038f)),
            new(new Vector3(0.62f, -0.50f, -0.50f), new Vector2(0.038f, 0.038f)),
        ];
        mb.AddLoft(Sections.Circle(1f, 10), rail, true, true);
        // Hand positions worn into the rail, and the brackets that hold it to the hull.
        GripRibs(mb, new Vector3(-0.26f, -0.42f, -0.56f), new Vector3(-0.02f, -0.42f, -0.56f), 5, 0.044f);
        GripRibs(mb, new Vector3(0.02f, -0.42f, -0.56f), new Vector3(0.26f, -0.42f, -0.56f), 5, 0.044f);
        mb.Material = (int)MatId.Trim;
        foreach (float sx in new[] { -0.44f, 0.44f })
        {
            mb.AddBox(new Vector3(sx, -0.448f, -0.535f), new Vector3(0.045f, 0.032f, 0.045f));
            Bolts(mb, new Vector3(sx - 0.030f, -0.478f, -0.535f),
                new Vector3(sx + 0.030f, -0.478f, -0.535f), 2, 0.012f);
        }
        // A passenger has nothing to operate, so the seat is what tells them where they are:
        // a bucket with a harness anchor either side.
        mb.Material = (int)MatId.RustMetal;
        mb.AddBox(new Vector3(0f, -0.80f, -0.34f), new Vector3(0.30f, 0.035f, 0.24f));
        mb.Material = (int)MatId.TechPanelDark;
        for (int i = -2; i <= 2; i++)
            mb.AddBox(new Vector3(i * 0.11f, -0.762f, -0.34f), new Vector3(0.040f, 0.012f, 0.22f));
        mb.Material = (int)MatId.WeaponMetal;
        foreach (float sx in new[] { -1f, 1f })
        {
            Span<MeshBuilder.LoftStation> strap =
            [
                new(new Vector3(sx * 0.28f, -0.76f, -0.30f), new Vector2(0.030f, 0.014f)),
                new(new Vector3(sx * 0.20f, -0.60f, -0.44f), new Vector2(0.028f, 0.013f)),
                new(new Vector3(sx * 0.11f, -0.50f, -0.52f), new Vector2(0.026f, 0.012f)),
            ];
            mb.AddLoft(Sections.RoundedRect(1f, 0.5f, 0.3f, 3), strap, true, true);
        }
        mb.Material = (int)MatId.EnergyPanel;
        mb.AddBox(new Vector3(0f, -0.505f, -0.53f), new Vector3(0.048f, 0.030f, 0.020f));
        Louvres(mb, new Vector3(0f, -0.66f, -0.60f), new Vector2(0.16f, 0.055f), 5);
    }

    public void Dispose()
    {
        foreach (var m in _meshes) m?.Dispose();
    }
}
