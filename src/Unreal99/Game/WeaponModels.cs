using System.Numerics;
using Silk.NET.OpenGL;
using Unreal99.Core;
using Unreal99.Rendering;

namespace Unreal99.Game;

/// <summary>
/// Procedurally built weapon meshes. Each is modelled in a local frame where <b>-Z is forward</b>,
/// +Y is up and the origin sits at the grip, so the same mesh serves the first-person view, the
/// third-person model in the character's right hand, and the HUD icon atlas.
///
/// These carry the highest detail budget in the game and should: a weapon occupies a third of the
/// screen for the entire match, so every silhouette is built from swept and revolved surfaces —
/// receivers with real shoulders, barrels with machined muzzles, magazines that sit in a well —
/// rather than the stacks of boxes and plain cylinders they used to be.
/// </summary>
public sealed class WeaponModels : IDisposable
{
    private readonly Mesh[] _meshes = new Mesh[(int)WeaponKind.Count];
    private readonly MeshSection[][] _sections = new MeshSection[(int)WeaponKind.Count][];
    private readonly (Vector3 Min, Vector3 Max)[] _bounds = new (Vector3, Vector3)[(int)WeaponKind.Count];
    private readonly Vector3[][] _hulls = new Vector3[(int)WeaponKind.Count][];

    public Mesh MeshFor(WeaponKind k) => _meshes[(int)k];
    public MeshSection[] SectionsFor(WeaponKind k) => _sections[(int)k];

    /// <summary>Model-space bounds of the built mesh; the turntable camera frames from these.</summary>
    public (Vector3 Min, Vector3 Max) BoundsFor(WeaponKind k) => _bounds[(int)k];

    /// <summary>
    /// Model-space offset that puts the mesh's footprint centre on the turntable's spin axis.
    /// Weapons are modelled from the grip and reach forwards, so spinning about the model origin
    /// swings the whole barrel out to one side of the frame instead of turning it in place.
    /// </summary>
    public Vector3 TurntablePivot(WeaponKind k)
    {
        var (lo, hi) = _bounds[(int)k];
        return new Vector3((lo.X + hi.X) * 0.5f, 0f, (lo.Z + hi.Z) * 0.5f);
    }

    /// <summary>Silhouette support points, in model space. See <see cref="MeshBuilder.SupportCloud"/>.</summary>
    public Vector3[] HullFor(WeaponKind k) => _hulls[(int)k];

    /// <summary>
    /// Builds one weapon without a GPU and reports its triangle count. Used by the headless
    /// density check: a weapon added later must not quietly be a coarser model than the ones
    /// beside it in the same weapon guide.
    /// </summary>
    public static int TriangleCountFor(WeaponKind kind)
    {
        var mb = new MeshBuilder { WorldUv = false, Material = (int)MatId.WeaponMetal };
        Build(kind, mb);
        var (_, indices, _) = mb.Build();
        return indices.Length / 3;
    }

    private Mesh _handMesh;
    private MeshSection[] _handSections = [];

    /// <summary>The supporting left hand, shared by every two-handed weapon.</summary>
    public Mesh HandMesh => _handMesh;
    public MeshSection[] HandSections => _handSections;

    private readonly Vector3[] _supportGrip = new Vector3[(int)WeaponKind.Count];
    private readonly float[] _supportScale = new float[(int)WeaponKind.Count];

    /// <summary>
    /// Uniform scale for the support hand on this weapon. The hand is modelled closing on a
    /// barrel of one nominal girth; a Flak Cannon's fore-end is far fatter than a Sniper Rifle's,
    /// and a fixed-size hand would either float off the slim ones or sink into the fat ones.
    /// Scaling uniformly keeps the hand undistorted and reads as a hand on a bigger gun.
    /// </summary>
    public float SupportScaleFor(WeaponKind k) => _supportScale[(int)k];

    /// <summary>
    /// Where the supporting hand closes on this weapon, in model space. Measured off the built
    /// mesh rather than hand-tuned per weapon, so a weapon added later gets a correctly placed
    /// hand for free — and measured from the cross-section at the fore-end rather than from the
    /// whole-model box, which on a Flak Cannon is set by the drum and buries the hand in the body.
    /// </summary>
    public Vector3 SupportGripFor(WeaponKind k) => _supportGrip[(int)k];

    /// <summary>
    /// Finds the fore-end and measures how thick the weapon is there, so the hand can be centred
    /// on the barrel and sized to it. The hand's fingers close around its own origin, so the grip
    /// point is the centre of the weapon's cross-section rather than a point on its surface.
    /// </summary>
    private static (Vector3 Grip, float Scale) MeasureSupportGrip(MeshBuilder mb,
        in (Vector3 Min, Vector3 Max) bounds)
    {
        const float NominalGirth = 0.052f;   // what BuildSupportHand closes around

        // Models are built from the grip at the origin reaching forward along -Z.
        float reach = MathF.Min(-bounds.Min.Z, 1.2f);
        float z = -reach * 0.60f;
        var (lo, hi) = mb.BoundsInSlab(z - 0.05f, z + 0.05f);
        if (hi.X <= lo.X) return (new Vector3(0f, 0f, z), 1f);

        // Width only. Many weapons carry a scope or a drum well above the barrel, and including
        // height would hang the hand in mid-air beside the sight rather than on the fore-end.
        float girth = MathF.Max((hi.X - lo.X) * 0.5f, 0.026f);
        float centreY = MathF.Min((lo.Y + hi.Y) * 0.5f, lo.Y + girth);
        return (new Vector3((lo.X + hi.X) * 0.5f, centreY, z),
                MathX.Clamp(girth / NominalGirth, 0.85f, 1.35f));
    }

    public WeaponModels(GL gl)
    {
        for (int i = 0; i < (int)WeaponKind.Count; i++)
        {
            var mb = new MeshBuilder { WorldUv = false, Material = (int)MatId.WeaponMetal };
            Build((WeaponKind)i, mb);
            mb.RecalculateTangents();
            _bounds[i] = mb.Bounds();
            _hulls[i] = mb.SupportCloud();
            (_supportGrip[i], _supportScale[i]) = MeasureSupportGrip(mb, _bounds[i]);
            var (v, ind, s) = mb.Build();
            _meshes[i] = Mesh.CreateStatic<Vertex>(gl, v, ind, VertexLayouts.Static);
            _sections[i] = s;
        }

        var hand = new MeshBuilder { WorldUv = false, Material = (int)MatId.ArmorPlate };
        BuildSupportHand(hand);
        hand.RecalculateTangents();
        var (hv, hi2, hs) = hand.Build();
        _handMesh = Mesh.CreateStatic<Vertex>(gl, hv, hi2, VertexLayouts.Static);
        _handSections = hs;
    }

    /// <summary>Triangle count of the shared support hand, without a GPU.</summary>
    public static int SupportHandTriangleCount()
    {
        var mb = new MeshBuilder { WorldUv = false, Material = (int)MatId.ArmorPlate };
        BuildSupportHand(mb);
        var (_, indices, _) = mb.Build();
        return indices.Length / 3;
    }

    /// <summary>
    /// The bare left forearm and hand that steadies a two-handed weapon, modelled about the
    /// fore-end it closes on: the barrel runs along Z and the arm comes up from below.
    ///
    /// Built to match the original's first-person art, which is worth stating because the obvious
    /// guess is wrong. The original does not show a gloved fist wrapped over the top of the gun.
    /// It shows a bare forearm rising steeply out of the bottom of the screen to meet the weapon
    /// from underneath, with the hand itself mostly hidden behind the body of the gun — the arm is
    /// the part that does the work of making the weapon look held. So the forearm is the hero
    /// element here, the fingers only clear the near side, and none of it is armoured.
    ///
    /// Placement is the caller's job — see <see cref="SupportGripFor"/> and
    /// <see cref="SupportScaleFor"/> — so this one mesh serves the whole arsenal.
    /// </summary>
    private static void BuildSupportHand(MeshBuilder mb)
    {
        // Everything is laid out on one circle about the grip axis. That is the whole trick: the
        // first attempt put the palm on its own path below the barrel and started the fingers on a
        // wrap arc, so on any weapon thicker than the palm was deep the palm sank into the body
        // and left four fingers apparently floating in front of the gun, attached to nothing.
        // Sharing a radius means the finger roots are always buried in the palm, whatever the
        // weapon underneath is doing.
        const float GripR = 0.055f;       // centreline radius of palm and fingers alike
        const float PalmAngle = 3.93f;    // ~225 degrees: under the fore-end, near side
        const float BackZ = 0.062f;       // wrist end of the palm
        const float FrontZ = -0.076f;     // knuckle end

        static Vector3 OnGrip(float angle, float radius, float z)
            => new(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius, z);

        mb.Material = (int)MatId.ArmorPlate;

        // --- palm: a slab lying along the fore-end, tangent to the grip circle ---
        Vector3 palmBack = OnGrip(PalmAngle, GripR, BackZ);
        Vector3 palmFront = OnGrip(PalmAngle, GripR, FrontZ);
        Span<MeshBuilder.LoftStation> palm =
        [
            new(palmBack + new Vector3(0f, 0f, 0.026f), new Vector2(0.030f, 0.020f), PalmAngle),
            new(palmBack, new Vector2(0.038f, 0.024f), PalmAngle),
            new(Vector3.Lerp(palmBack, palmFront, 0.5f), new Vector2(0.043f, 0.026f), PalmAngle),
            new(palmFront, new Vector2(0.040f, 0.024f), PalmAngle),
            new(palmFront - new Vector3(0f, 0f, 0.020f), new Vector2(0.030f, 0.019f), PalmAngle),
        ];
        mb.AddLoft(Sections.Superellipse(1f, 1f, 2.6f, 14), palm, capStart: true, capEnd: true);

        // --- forearm, rising out of the bottom of the frame to meet the wrist ---
        Vector3 wrist = palmBack + new Vector3(0f, 0f, 0.020f);
        Span<MeshBuilder.LoftStation> arm =
        [
            new(wrist + new Vector3(-0.088f, -0.345f, 0.196f), new Vector2(0.060f, 0.058f)),
            new(wrist + new Vector3(-0.066f, -0.246f, 0.142f), new Vector2(0.058f, 0.055f)),
            new(wrist + new Vector3(-0.044f, -0.152f, 0.090f), new Vector2(0.052f, 0.049f)),
            new(wrist + new Vector3(-0.022f, -0.070f, 0.042f), new Vector2(0.044f, 0.042f)),
            new(wrist, new Vector2(0.036f, 0.030f)),
        ];
        mb.AddLoft(Sections.Superellipse(1f, 1f, 2.6f, 16), arm, capStart: true, capEnd: false);

        // --- four fingers, rooted in the palm and closing up the near flank to the crown ---
        // The reference shows them silhouetted over the barrel rather than tucked underneath, so
        // they sweep the short way round — up the side facing the camera — and stop at the top.
        float[] fingerZ = [0.040f, 0.008f, -0.024f, -0.056f];
        float[] fingerLength = [0.86f, 1f, 0.95f, 0.78f];
        for (int f = 0; f < 4; f++)
        {
            // Start inside the palm slab, not at its surface, so the root is never exposed.
            float start = PalmAngle + 0.16f;
            float sweep = -(2.30f * fingerLength[f] + 0.16f);
            const int Segments = 4;
            Span<MeshBuilder.LoftStation> finger = stackalloc MeshBuilder.LoftStation[Segments + 1];
            for (int i = 0; i <= Segments; i++)
            {
                float t = i / (float)Segments;
                float taper = MathX.Lerp(0.0150f, 0.0100f, t);
                finger[i] = new MeshBuilder.LoftStation(
                    OnGrip(start + sweep * t, GripR, fingerZ[f]), new Vector2(taper, taper));
            }
            mb.AddLoft(Sections.Circle(1f, 10), finger, capStart: true, capEnd: true);

            // Knuckle at the middle joint, so a finger reads as jointed rather than as a bent tube.
            mb.AddTorus(OnGrip(start + sweep * 0.5f, GripR, fingerZ[f]), 0.0132f, 0.0040f, 10, 6);
        }

        // --- thumb, rooted in the palm and lying forward along the near side ---
        float thumbAngle = PalmAngle - 0.34f;
        Span<MeshBuilder.LoftStation> thumb =
        [
            new(OnGrip(PalmAngle, GripR, BackZ + 0.008f), new Vector2(0.0170f, 0.0170f)),
            new(OnGrip(thumbAngle, GripR + 0.004f, BackZ - 0.036f), new Vector2(0.0158f, 0.0158f)),
            new(OnGrip(thumbAngle - 0.16f, GripR + 0.008f, BackZ - 0.082f), new Vector2(0.0134f, 0.0134f)),
            new(OnGrip(thumbAngle - 0.26f, GripR + 0.010f, BackZ - 0.118f), new Vector2(0.0104f, 0.0104f)),
        ];
        mb.AddLoft(Sections.Circle(1f, 10), thumb, capStart: true, capEnd: true);
        mb.AddTorus(OnGrip(thumbAngle, GripR + 0.004f, BackZ - 0.036f), 0.0148f, 0.0040f, 10, 6);
    }

    // ================================================================ shared parts

    /// <summary>
    /// A pistol grip: swept back from the receiver, swelling into a palm bulge and finishing in a
    /// rounded heel, with a trigger and guard. The old grip was two boxes and read as a handle
    /// glued to a crate.
    /// </summary>
    private static void Grip(MeshBuilder mb, float length = 0.20f, float z = 0.06f, float rake = 0.055f)
    {
        int restore = mb.Material;
        mb.Material = (int)MatId.TechPanelDark;
        Span<MeshBuilder.LoftStation> grip =
        [
            new(new Vector3(0f, 0.010f, z), new Vector2(0.030f, 0.052f)),
            new(new Vector3(0f, -length * 0.30f, z + rake * 0.35f), new Vector2(0.032f, 0.055f)),
            new(new Vector3(0f, -length * 0.62f, z + rake * 0.80f), new Vector2(0.030f, 0.050f)),
            new(new Vector3(0f, -length * 0.92f, z + rake), new Vector2(0.026f, 0.042f)),
            new(new Vector3(0f, -length * 1.02f, z + rake * 1.06f), new Vector2(0.012f, 0.020f)),
        ];
        mb.AddLoft(Sections.Superellipse(1f, 1f, 2.8f, 14), grip, capStart: false, capEnd: false);

        // Trigger and guard.
        mb.Material = (int)MatId.Trim;
        Span<MeshBuilder.LoftStation> guard =
        [
            new(new Vector3(0f, -0.012f, z - 0.030f), 0.008f),
            new(new Vector3(0f, -0.052f, z - 0.048f), 0.008f),
            new(new Vector3(0f, -0.070f, z - 0.020f), 0.008f),
            new(new Vector3(0f, -0.058f, z + 0.018f), 0.008f),
        ];
        mb.AddLoft(Sections.Circle(1f, 7), guard, capStart: false, capEnd: false);
        mb.AddBox(new Vector3(0f, -0.038f, z - 0.028f), new Vector3(0.006f, 0.018f, 0.005f));
        mb.Material = restore;
    }

    /// <summary>
    /// A receiver: the body of the weapon, swept so it has a shoulder where the barrel leaves it
    /// and a slightly narrowed tail rather than being a constant-section brick.
    /// </summary>
    private static void Receiver(MeshBuilder mb, ReadOnlySpan<Vector4> stations, float exponent = 3.4f)
    {
        var section = Sections.Superellipse(1f, 1f, exponent, 18);
        var list = new MeshBuilder.LoftStation[stations.Length];
        for (int i = 0; i < stations.Length; i++)
            list[i] = new MeshBuilder.LoftStation(
                new Vector3(0f, stations[i].W, stations[i].X), new Vector2(stations[i].Y, stations[i].Z));
        mb.AddLoft(section, list, capStart: true, capEnd: true);
    }

    /// <summary>Vent slots cut around a barrel shroud — the detail that says "this gets hot".</summary>
    private static void Vents(MeshBuilder mb, Vector3 center, float radius, float length, int count = 6)
    {
        int restore = mb.Material;
        mb.Material = (int)MatId.TechPanelDark;
        for (int i = 0; i < count; i++)
        {
            float a = i / (float)count * MathX.TwoPi;
            mb.PushTransform(Matrix4x4.CreateRotationZ(a) * Matrix4x4.CreateTranslation(center));
            mb.AddBox(new Vector3(0f, radius * 0.96f, 0f), new Vector3(radius * 0.20f, radius * 0.14f, length * 0.5f));
            mb.PopTransform();
        }
        mb.Material = restore;
    }

    /// <summary>Iron sights: a hooded front post and a rear notch.</summary>
    private static void Sights(MeshBuilder mb, float y, float frontZ, float rearZ, float scale = 1f)
    {
        int restore = mb.Material;
        mb.Material = (int)MatId.Trim;
        mb.AddBox(new Vector3(0f, y + 0.010f * scale, frontZ), new Vector3(0.004f * scale, 0.012f * scale, 0.006f * scale));
        Span<MeshBuilder.LoftStation> hood =
        [
            new(new Vector3(0f, y, frontZ + 0.012f * scale), new Vector2(0.011f * scale, 0.013f * scale)),
            new(new Vector3(0f, y, frontZ - 0.012f * scale), new Vector2(0.011f * scale, 0.013f * scale)),
        ];
        mb.AddLoft(Sections.RoundedRect(1f, 1f, 0.5f, 3), hood, capStart: false, capEnd: false);
        mb.AddBox(new Vector3(-0.009f * scale, y + 0.008f * scale, rearZ), new Vector3(0.004f * scale, 0.008f * scale, 0.006f * scale));
        mb.AddBox(new Vector3(0.009f * scale, y + 0.008f * scale, rearZ), new Vector3(0.004f * scale, 0.008f * scale, 0.006f * scale));
        mb.Material = restore;
    }

    /// <summary>
    /// A row of fasteners along a body panel. Cheap in authoring terms and the single most
    /// effective way to stop a swept hull reading as a smooth plastic shell — the 1999 weapons all
    /// carry them, so anything added later that does not looks like a placeholder beside them.
    /// </summary>
    private static void Bolts(MeshBuilder mb, float y, float z0, float z1, int count,
        float halfWidth, float size = 0.005f)
    {
        int restore = mb.Material;
        mb.Material = (int)MatId.Trim;
        for (int i = 0; i < count; i++)
        {
            float t = count == 1 ? 0.5f : i / (float)(count - 1);
            float z = MathX.Lerp(z0, z1, t);
            foreach (float sx in new[] { -halfWidth, halfWidth })
                mb.AddSphere(new Vector3(sx, y, z), size, 4, 6);
        }
        mb.Material = restore;
    }

    /// <summary>A raised accessory rail: the notched strip optics and grips clamp onto.</summary>
    private static void Rail(MeshBuilder mb, float y, float z0, float z1, int teeth, float halfWidth = 0.011f)
    {
        int restore = mb.Material;
        mb.Material = (int)MatId.TechPanelDark;
        mb.AddBox(new Vector3(0f, y, (z0 + z1) * 0.5f),
            new Vector3(halfWidth, 0.004f, MathF.Abs(z1 - z0) * 0.5f));
        mb.Material = (int)MatId.Trim;
        for (int i = 0; i < teeth; i++)
        {
            float t = (i + 0.5f) / teeth;
            mb.AddBox(new Vector3(0f, y + 0.005f, MathX.Lerp(z0, z1, t)),
                new Vector3(halfWidth * 0.92f, 0.004f, MathF.Abs(z1 - z0) / teeth * 0.32f));
        }
        mb.Material = restore;
    }

    /// <summary>Cooling fins stacked along a barrel or a housing.</summary>
    private static void Fins(MeshBuilder mb, Vector3 from, Vector3 to, int count, float radius,
        float thickness = 0.005f)
    {
        int restore = mb.Material;
        mb.Material = (int)MatId.Trim;
        for (int i = 0; i < count; i++)
        {
            Vector3 at = Vector3.Lerp(from, to, count == 1 ? 0.5f : i / (float)(count - 1));
            Shapes.Collar(mb, at, radius, thickness, 16);
        }
        mb.Material = restore;
    }

    /// <summary>A box magazine seated in a well, canted slightly forward.</summary>
    private static void Magazine(MeshBuilder mb, Vector3 top, float halfW, float halfD, float depth)
    {
        int restore = mb.Material;
        mb.Material = (int)MatId.TechPanelDark;
        Span<MeshBuilder.LoftStation> mag =
        [
            new(top, new Vector2(halfW, halfD)),
            new(top + new Vector3(0f, -depth * 0.75f, depth * 0.10f), new Vector2(halfW * 0.98f, halfD * 0.96f)),
            new(top + new Vector3(0f, -depth, depth * 0.14f), new Vector2(halfW * 0.88f, halfD * 0.86f)),
        ];
        mb.AddLoft(Sections.RoundedRect(1f, 1f, 0.3f, 2), mag, capStart: false, capEnd: true);
        mb.Material = restore;
    }

    // ================================================================ the weapons

    private static void Build(WeaponKind kind, MeshBuilder mb)
    {
        switch (kind)
        {
            case WeaponKind.ImpactHammer: BuildImpactHammer(mb); break;
            case WeaponKind.Enforcer: BuildEnforcer(mb); break;
            case WeaponKind.BioRifle: BuildBioRifle(mb); break;
            case WeaponKind.ShockRifle: BuildShockRifle(mb); break;
            case WeaponKind.PulseGun: BuildPulseGun(mb); break;
            case WeaponKind.Ripper: BuildRipper(mb); break;
            case WeaponKind.Minigun: BuildMinigun(mb); break;
            case WeaponKind.FlakCannon: BuildFlakCannon(mb); break;
            case WeaponKind.RocketLauncher: BuildRocketLauncher(mb); break;
            case WeaponKind.SniperRifle: BuildSniperRifle(mb); break;
            case WeaponKind.Redeemer: BuildRedeemer(mb); break;
            case WeaponKind.ShieldGun: BuildShieldGun(mb); break;
            case WeaponKind.AssaultRifle: BuildAssaultRifle(mb); break;
            case WeaponKind.LinkGun: BuildLinkGun(mb); break;
            case WeaponKind.LightningGun: BuildLightningGun(mb); break;
            case WeaponKind.MineLayer: BuildMineLayer(mb); break;
            case WeaponKind.GrenadeLauncher: BuildGrenadeLauncher(mb); break;
            case WeaponKind.Avril: BuildAvril(mb); break;
            case WeaponKind.IonPainter: BuildPainter(mb, ion: true); break;
            case WeaponKind.TargetPainter: BuildPainter(mb, ion: false); break;
            case WeaponKind.Translocator: BuildTranslocator(mb); break;
            case WeaponKind.SuperShockRifle: BuildSuperShockRifle(mb); break;
            case WeaponKind.Stinger: BuildStinger(mb); break;
            case WeaponKind.BallLauncher: BuildBallLauncher(mb); break;
        }
    }

    /// <summary>
    /// Impact Hammer: a pneumatic demolition tool, not a gun. Stubby body, a pressure bottle on
    /// top, exposed piston rods, and a percussion head wider than anything behind it — proportion
    /// is what makes this read as a hammer however it is textured.
    /// </summary>
    private static void BuildImpactHammer(MeshBuilder mb)
    {
        Grip(mb, 0.22f, 0.03f);

        mb.Material = (int)MatId.WeaponMetal;
        Receiver(mb,
        [
            new(0.055f, 0.052f, 0.058f, 0.045f),
            new(-0.020f, 0.062f, 0.068f, 0.045f),
            new(-0.120f, 0.058f, 0.062f, 0.045f),
            new(-0.185f, 0.048f, 0.050f, 0.045f),
        ]);

        // Pressure bottle with end caps and a regulator.
        mb.Material = (int)MatId.RustMetal;
        Shapes.BarrelBack(mb, new Vector3(0f, 0.128f, 0.040f),
        [
            new(0f, 0.020f), new(0.012f, 0.042f), new(0.180f, 0.042f),
            new(0.196f, 0.020f),
        ], 16);
        mb.Material = (int)MatId.Trim;
        Shapes.Collar(mb, new Vector3(0f, 0.128f, -0.055f), 0.044f, 0.007f);
        mb.AddSphere(new Vector3(0.038f, 0.128f, 0.030f), 0.018f, 7, 11);

        // Twin piston rods carrying the head, deliberately left exposed.
        mb.Material = (int)MatId.Trim;
        foreach (float rodX in new[] { -0.055f, 0.055f })
        {
            Shapes.BarrelBack(mb, new Vector3(rodX, 0.045f, -0.185f),
                [new Vector2(0f, 0.017f), new Vector2(0.115f, 0.014f)], 10);
            Shapes.Collar(mb, new Vector3(rodX, 0.045f, -0.196f), 0.021f, 0.005f, 10);
        }

        // The head: a flared anvil, the widest thing in the arsenal but only just.
        mb.Material = (int)MatId.WeaponMetal;
        Shapes.BarrelBack(mb, new Vector3(0f, 0.045f, -0.295f),
        [
            new(0f, 0.062f), new(0.018f, 0.078f), new(0.052f, 0.098f),
            new(0.062f, 0.100f), new(0.070f, 0.092f),
        ], 18);
        mb.Material = (int)MatId.Trim;
        Shapes.Collar(mb, new Vector3(0f, 0.045f, -0.360f), 0.100f, 0.011f, 20);
        // Percussion face, recessed inside the rim.
        mb.Material = (int)MatId.RustMetal;
        Shapes.BarrelBack(mb, new Vector3(0f, 0.045f, -0.352f),
            [new Vector2(0f, 0.086f), new Vector2(0.008f, 0.084f)], 18);

        mb.Material = (int)MatId.EnergyPanel;
        mb.AddBox(new Vector3(0f, 0.172f, -0.060f), new Vector3(0.016f, 0.006f, 0.070f));
    }

    /// <summary>Enforcer: a heavy service pistol — slide, frame, magazine well and a squared muzzle.</summary>
    private static void BuildEnforcer(MeshBuilder mb)
    {
        Grip(mb, 0.17f, 0.045f, 0.045f);
        Magazine(mb, new Vector3(0f, -0.020f, 0.048f), 0.021f, 0.036f, 0.100f);

        // Frame.
        mb.Material = (int)MatId.WeaponMetal;
        Receiver(mb,
        [
            new(0.060f, 0.026f, 0.030f, 0.028f),
            new(-0.020f, 0.030f, 0.034f, 0.030f),
            new(-0.140f, 0.028f, 0.030f, 0.030f),
            new(-0.190f, 0.024f, 0.026f, 0.030f),
        ]);
        // Slide sitting on top, with cocking serrations at the rear.
        mb.Material = (int)MatId.Trim;
        Receiver(mb,
        [
            new(0.052f, 0.028f, 0.024f, 0.072f),
            new(-0.060f, 0.030f, 0.026f, 0.072f),
            new(-0.200f, 0.028f, 0.024f, 0.070f),
            new(-0.235f, 0.024f, 0.020f, 0.068f),
        ]);
        mb.Material = (int)MatId.TechPanelDark;
        for (int i = 0; i < 5; i++)
            mb.AddBox(new Vector3(0f, 0.072f, 0.030f - i * 0.013f), new Vector3(0.031f, 0.024f, 0.003f));

        // Barrel protruding from the slide, and the recoil-spring rod below it.
        mb.Material = (int)MatId.WeaponMetal;
        Shapes.BarrelBack(mb, new Vector3(0f, 0.070f, -0.230f),
        [
            new(0f, 0.019f), new(0.010f, 0.022f), new(0.020f, 0.019f),
            new(0.048f, 0.018f), new(0.052f, 0.014f),
        ], 20);
        mb.Material = (int)MatId.TechPanelDark;
        Shapes.BarrelBack(mb, new Vector3(0f, 0.048f, -0.200f),
            [new Vector2(0f, 0.010f), new Vector2(0.010f, 0.013f), new Vector2(0.052f, 0.010f)], 14);

        // Slide rails, ejection port and frame pins — the small hardware that separates a service
        // pistol from a block with a barrel, and what the rest of the arsenal already carries.
        mb.Material = (int)MatId.Trim;
        foreach (float sx in new[] { -0.031f, 0.031f })
            mb.AddBox(new Vector3(sx, 0.056f, -0.070f), new Vector3(0.004f, 0.005f, 0.100f));
        mb.Material = (int)MatId.TechPanelDark;
        mb.AddBox(new Vector3(0.028f, 0.082f, -0.090f), new Vector3(0.005f, 0.014f, 0.034f));
        Bolts(mb, 0.030f, -0.140f, 0.040f, 4, 0.028f, 0.004f);
        Fins(mb, new Vector3(0f, 0.070f, -0.240f), new Vector3(0f, 0.070f, -0.272f), 3, 0.023f, 0.004f);

        Sights(mb, 0.086f, -0.215f, -0.010f);
    }

    /// <summary>
    /// Bio Rifle: a corroded industrial sprayer. Ribbed tank of sludge over a heavy body, with a
    /// wide nozzle and a dripping collector ring.
    /// </summary>
    private static void BuildBioRifle(MeshBuilder mb)
    {
        Grip(mb, 0.20f, 0.06f);

        mb.Material = (int)MatId.RustMetal;
        Receiver(mb,
        [
            new(0.085f, 0.050f, 0.052f, 0.040f),
            new(-0.020f, 0.058f, 0.064f, 0.042f),
            new(-0.180f, 0.056f, 0.060f, 0.042f),
            new(-0.290f, 0.050f, 0.052f, 0.044f),
        ]);

        // Sludge tank: a ribbed cylinder along the top, translucent green.
        mb.Material = (int)MatId.Flesh;
        Shapes.BarrelBack(mb, new Vector3(0f, 0.128f, 0.075f),
        [
            new(0f, 0.028f), new(0.016f, 0.066f), new(0.055f, 0.072f),
            new(0.130f, 0.072f), new(0.170f, 0.064f), new(0.188f, 0.030f),
        ], 18);
        mb.Material = (int)MatId.RustMetal;
        for (int i = 0; i < 4; i++)
            Shapes.Collar(mb, new Vector3(0f, 0.128f, 0.030f - i * 0.038f), 0.074f, 0.008f, 16);
        // Feed pipe running down into the receiver.
        mb.Material = (int)MatId.Trim;
        Shapes.Strut(mb, new Vector3(0.052f, 0.120f, -0.100f), new Vector3(0.052f, 0.055f, -0.180f),
            0.010f, 0.010f, 8);

        // Barrel and the flared nozzle.
        mb.Material = (int)MatId.RustMetal;
        Shapes.BarrelBack(mb, new Vector3(0f, 0.050f, -0.330f),
        [
            new(0f, 0.052f), new(0.090f, 0.046f), new(0.185f, 0.042f),
            new(0.200f, 0.056f), new(0.230f, 0.060f), new(0.238f, 0.046f),
        ], 18);
        mb.Material = (int)MatId.EnergyPanel;
        Shapes.BarrelBack(mb, new Vector3(0f, 0.050f, -0.560f),
            [new Vector2(0f, 0.038f), new Vector2(0.016f, 0.026f)], 14);
        mb.Material = (int)MatId.Trim;
        Shapes.Collar(mb, new Vector3(0f, 0.050f, -0.520f), 0.056f, 0.010f, 16);
    }

    /// <summary>
    /// Shock Rifle: a precision energy weapon. Slim milled body, a long barrel caged by two
    /// conductor rails, and a glowing emitter recessed in the muzzle.
    /// </summary>
    private static void BuildShockRifle(MeshBuilder mb)
    {
        Grip(mb, 0.19f, 0.07f);

        mb.Material = (int)MatId.WeaponMetal;
        Receiver(mb,
        [
            new(0.100f, 0.036f, 0.044f, 0.045f),
            new(0.010f, 0.044f, 0.056f, 0.048f),
            new(-0.180f, 0.042f, 0.052f, 0.048f),
            new(-0.300f, 0.036f, 0.044f, 0.048f),
        ]);
        // Cheek plate and the capacitor housing on top.
        mb.Material = (int)MatId.TechPanelDark;
        Receiver(mb,
        [
            new(0.030f, 0.022f, 0.020f, 0.104f),
            new(-0.120f, 0.026f, 0.024f, 0.106f),
            new(-0.230f, 0.020f, 0.018f, 0.104f),
        ]);

        // Barrel with a machined shoulder, then the emitter.
        mb.Material = (int)MatId.WeaponMetal;
        Shapes.BarrelBack(mb, new Vector3(0f, 0.050f, -0.310f),
        [
            new(0f, 0.046f), new(0.020f, 0.048f), new(0.030f, 0.038f),
            new(0.250f, 0.036f), new(0.268f, 0.048f), new(0.300f, 0.048f),
            new(0.308f, 0.040f),
        ], 18);
        mb.Material = (int)MatId.EnergyPanel;
        Shapes.BarrelBack(mb, new Vector3(0f, 0.050f, -0.606f),
            [new Vector2(0f, 0.034f), new Vector2(0.022f, 0.024f)], 16);

        // Conductor rails running alongside the barrel, tied to it by collars.
        mb.Material = (int)MatId.EnergyPanel;
        foreach (float sx in new[] { -0.048f, 0.048f })
            Shapes.Strut(mb, new Vector3(sx, 0.050f, -0.330f), new Vector3(sx, 0.050f, -0.585f),
                0.007f, 0.006f, 8);
        mb.Material = (int)MatId.Trim;
        foreach (float z in new[] { -0.380f, -0.480f, -0.570f })
            Shapes.Collar(mb, new Vector3(0f, 0.050f, z), 0.050f, 0.008f, 16);

        Sights(mb, 0.124f, -0.240f, 0.000f);
    }

    /// <summary>
    /// Pulse Gun: three plasma emitters clustered around a core, with a heat sink stack above the
    /// receiver and a lit accelerator ring at the muzzle.
    /// </summary>
    private static void BuildPulseGun(MeshBuilder mb)
    {
        Grip(mb, 0.20f, 0.07f);

        mb.Material = (int)MatId.WeaponMetal;
        Receiver(mb,
        [
            new(0.100f, 0.046f, 0.052f, 0.048f),
            new(0.000f, 0.056f, 0.066f, 0.050f),
            new(-0.190f, 0.052f, 0.060f, 0.050f),
            new(-0.290f, 0.044f, 0.050f, 0.050f),
        ]);
        // Heat-sink fins along the top.
        mb.Material = (int)MatId.TechPanelDark;
        for (int i = 0; i < 7; i++)
            mb.AddBox(new Vector3(0f, 0.118f, 0.030f - i * 0.032f), new Vector3(0.030f, 0.014f, 0.008f));

        // Three emitters around the axis, each with its own collar and lit tip.
        for (int i = 0; i < 3; i++)
        {
            float a = i / 3f * MathX.TwoPi + MathX.HalfPi;
            Vector3 off = new(MathF.Cos(a) * 0.036f, 0.050f + MathF.Sin(a) * 0.036f, 0f);
            mb.Material = (int)MatId.WeaponMetal;
            Shapes.BarrelBack(mb, off + new Vector3(0f, 0f, -0.290f),
            [
                new(0f, 0.021f), new(0.016f, 0.023f), new(0.026f, 0.018f),
                new(0.240f, 0.017f), new(0.256f, 0.023f), new(0.272f, 0.020f),
            ], 12);
            mb.Material = (int)MatId.EnergyPanel;
            Shapes.BarrelBack(mb, off + new Vector3(0f, 0f, -0.560f),
                [new Vector2(0f, 0.015f), new Vector2(0.010f, 0.010f)], 10);
        }

        // Accelerator ring binding the three barrels together.
        mb.Material = (int)MatId.Trim;
        Shapes.Collar(mb, new Vector3(0f, 0.050f, -0.420f), 0.058f, 0.012f, 18);
        mb.Material = (int)MatId.EnergyPanel;
        Shapes.Collar(mb, new Vector3(0f, 0.050f, -0.545f), 0.056f, 0.009f, 18);
        mb.AddBox(new Vector3(0f, 0.116f, -0.220f), new Vector3(0.022f, 0.008f, 0.150f));
    }

    /// <summary>
    /// Ripper: a blade thrower. Flat wide body, a disc magazine standing proud of the receiver,
    /// and an open launch slot at the front with the next blade visible in it.
    /// </summary>
    private static void BuildRipper(MeshBuilder mb)
    {
        Grip(mb, 0.19f, 0.055f);

        mb.Material = (int)MatId.WeaponMetal;
        // Deliberately wide and shallow: this thing throws discs edge-on.
        Receiver(mb,
        [
            new(0.090f, 0.052f, 0.044f, 0.044f),
            new(0.000f, 0.062f, 0.052f, 0.046f),
            new(-0.190f, 0.066f, 0.046f, 0.046f),
            new(-0.330f, 0.072f, 0.032f, 0.046f),
            new(-0.470f, 0.070f, 0.028f, 0.046f),
            new(-0.520f, 0.058f, 0.024f, 0.046f),
        ], 4.5f);

        // Disc magazine: a lathed drum with blade edges showing through.
        mb.Material = (int)MatId.Trim;
        mb.PushTransform(Matrix4x4.CreateTranslation(new Vector3(0f, 0.116f, -0.140f)));
        mb.AddLathe(
        [
            new(0.020f, -0.028f), new(0.058f, -0.030f), new(0.064f, -0.014f),
            new(0.064f, 0.014f), new(0.058f, 0.030f), new(0.020f, 0.028f),
        ], Vector3.Zero, 20);
        mb.PopTransform();
        mb.Material = (int)MatId.EnergyPanel;
        for (int i = 0; i < 8; i++)
        {
            float a = i / 8f * MathX.TwoPi;
            mb.PushTransform(Matrix4x4.CreateRotationZ(a)
                * Matrix4x4.CreateTranslation(new Vector3(0f, 0.116f, -0.140f)));
            mb.AddBox(new Vector3(0f, 0.052f, 0f), new Vector3(0.006f, 0.014f, 0.032f));
            mb.PopTransform();
        }

        // Launch slot: two rails with a blade sitting between them.
        mb.Material = (int)MatId.TechPanelDark;
        foreach (float sy in new[] { -0.028f, 0.028f })
            mb.AddBox(new Vector3(0f, 0.046f + sy, -0.430f), new Vector3(0.070f, 0.008f, 0.100f));
        mb.Material = (int)MatId.EnergyPanel;
        mb.PushTransform(Matrix4x4.CreateRotationX(MathX.HalfPi)
            * Matrix4x4.CreateTranslation(new Vector3(0f, 0.046f, -0.470f)));
        mb.AddLathe([new Vector2(0.010f, -0.004f), new Vector2(0.052f, -0.002f),
                     new Vector2(0.052f, 0.002f), new Vector2(0.010f, 0.004f)], Vector3.Zero, 16);
        mb.PopTransform();
        mb.Material = (int)MatId.Trim;
        foreach (float sx in new[] { -0.076f, 0.076f })
            mb.AddBox(new Vector3(sx, 0.046f, -0.420f), new Vector3(0.006f, 0.022f, 0.115f));

        // Blades stacked in the drum, visible through the housing, plus the hardware that holds
        // the whole thing together. Without them the drum is a smooth cylinder and the weapon
        // reads a generation older than everything beside it.
        mb.Material = (int)MatId.WeaponMetal;
        for (int i = 0; i < 4; i++)
        {
            mb.PushTransform(Matrix4x4.CreateRotationX(MathX.HalfPi)
                * Matrix4x4.CreateTranslation(new Vector3(0f, 0.116f, -0.170f + i * 0.020f)));
            mb.AddLathe([new Vector2(0.014f, -0.003f), new Vector2(0.050f, -0.002f),
                         new Vector2(0.050f, 0.002f), new Vector2(0.014f, 0.003f)], Vector3.Zero, 18);
            mb.PopTransform();
        }
        Fins(mb, new Vector3(0f, 0.046f, -0.250f), new Vector3(0f, 0.046f, -0.370f), 4, 0.062f, 0.005f);
        Bolts(mb, 0.082f, -0.300f, 0.060f, 6, 0.056f);
        Rail(mb, 0.150f, -0.260f, -0.060f, 6);
    }

    /// <summary>
    /// Minigun: six barrels on a rotor, a heavy receiver, a spade grip and a feed chute from an
    /// ammunition drum. The barrel cluster and its clamps are the whole silhouette.
    /// </summary>
    private static void BuildMinigun(MeshBuilder mb)
    {
        Grip(mb, 0.20f, 0.10f);

        mb.Material = (int)MatId.WeaponMetal;
        Receiver(mb,
        [
            new(0.130f, 0.056f, 0.062f, 0.050f),
            new(0.020f, 0.066f, 0.074f, 0.052f),
            new(-0.160f, 0.062f, 0.070f, 0.052f),
            new(-0.250f, 0.052f, 0.058f, 0.052f),
        ]);

        // Rotor plates the barrels run through.
        mb.Material = (int)MatId.TechPanelDark;
        foreach (float z in new[] { -0.270f, -0.500f, -0.700f })
        {
            mb.PushTransform(Matrix4x4.CreateRotationX(MathX.HalfPi)
                * Matrix4x4.CreateTranslation(new Vector3(0f, 0.050f, z)));
            mb.AddLathe(
            [
                new(0.018f, -0.026f), new(0.070f, -0.030f), new(0.074f, -0.010f),
                new(0.074f, 0.010f), new(0.070f, 0.030f), new(0.018f, 0.026f),
            ], Vector3.Zero, 20);
            mb.PopTransform();
        }

        // Six barrels with muzzle collars.
        for (int i = 0; i < 6; i++)
        {
            float a = i / 6f * MathX.TwoPi;
            Vector3 off = new(MathF.Cos(a) * 0.046f, 0.050f + MathF.Sin(a) * 0.046f, 0f);
            mb.Material = (int)MatId.WeaponMetal;
            Shapes.BarrelBack(mb, off + new Vector3(0f, 0f, -0.250f),
            [
                new(0f, 0.015f), new(0.400f, 0.014f), new(0.436f, 0.014f),
                new(0.444f, 0.018f), new(0.470f, 0.018f), new(0.474f, 0.011f),
            ], 10);
        }

        // Spade grip and the ammunition feed.
        mb.Material = (int)MatId.Trim;
        Shapes.Strut(mb, new Vector3(0.072f, 0.086f, -0.090f), new Vector3(0.104f, 0.010f, -0.060f),
            0.014f, 0.018f, 8);
        mb.Material = (int)MatId.RustMetal;
        Shapes.RoundedBox(mb, new Vector3(-0.092f, 0.026f, 0.012f), new Vector3(0.034f, 0.058f, 0.086f), 0.024f);
        mb.Material = (int)MatId.TechPanelDark;
        Shapes.Strut(mb, new Vector3(-0.070f, 0.050f, -0.020f), new Vector3(-0.020f, 0.044f, -0.090f),
            0.018f, 0.014f, 8);

        Vents(mb, new Vector3(0f, 0.050f, -0.360f), 0.080f, 0.120f, 6);
    }

    /// <summary>
    /// Flak Cannon: a scrap-built shotgun. Fat breech, a big flared muzzle, and a shell drum
    /// hanging under the receiver.
    /// </summary>
    private static void BuildFlakCannon(MeshBuilder mb)
    {
        Grip(mb, 0.20f, 0.09f);

        mb.Material = (int)MatId.RustMetal;
        Receiver(mb,
        [
            new(0.130f, 0.058f, 0.066f, 0.052f),
            new(0.020f, 0.070f, 0.080f, 0.055f),
            new(-0.140f, 0.066f, 0.076f, 0.055f),
            new(-0.230f, 0.058f, 0.064f, 0.055f),
        ]);
        // Shell drum below, with visible rounds.
        mb.Material = (int)MatId.TechPanelDark;
        mb.PushTransform(Matrix4x4.CreateRotationX(MathX.HalfPi)
            * Matrix4x4.CreateTranslation(new Vector3(0f, -0.010f, -0.020f)));
        mb.AddLathe(
        [
            new(0.014f, -0.032f), new(0.062f, -0.036f), new(0.068f, -0.016f),
            new(0.068f, 0.016f), new(0.062f, 0.036f), new(0.014f, 0.032f),
        ], Vector3.Zero, 18);
        mb.PopTransform();
        mb.Material = (int)MatId.Trim;
        for (int i = 0; i < 6; i++)
        {
            float a = i / 6f * MathX.TwoPi;
            mb.PushTransform(Matrix4x4.CreateRotationZ(a)
                * Matrix4x4.CreateTranslation(new Vector3(0f, -0.010f, -0.020f)));
            mb.AddBox(new Vector3(0f, 0.048f, 0f), new Vector3(0.010f, 0.016f, 0.030f));
            mb.PopTransform();
        }

        // The signature flared muzzle: narrow at the breech, trumpet at the mouth.
        mb.Material = (int)MatId.RustMetal;
        Shapes.BarrelBack(mb, new Vector3(0f, 0.055f, -0.250f),
        [
            new(0f, 0.058f), new(0.024f, 0.060f), new(0.036f, 0.050f),
            new(0.210f, 0.056f), new(0.290f, 0.078f), new(0.336f, 0.094f),
            new(0.344f, 0.090f), new(0.320f, 0.074f),
        ], 20);
        mb.Material = (int)MatId.Trim;
        Shapes.Collar(mb, new Vector3(0f, 0.055f, -0.470f), 0.060f, 0.011f, 16);
        // Reinforcing straps welded along the barrel — this weapon is meant to look improvised.
        mb.Material = (int)MatId.WeaponMetal;
        foreach (float sx in new[] { -0.062f, 0.062f })
            mb.AddBox(new Vector3(sx, 0.110f, -0.130f), new Vector3(0.014f, 0.026f, 0.098f));
    }

    /// <summary>
    /// Rocket Launcher: three tubes in a triangular cluster with an armoured shroud, a load-plate
    /// at the rear and a laser sight on top.
    /// </summary>
    private static void BuildRocketLauncher(MeshBuilder mb)
    {
        Grip(mb, 0.20f, 0.10f);

        mb.Material = (int)MatId.WeaponMetal;
        Receiver(mb,
        [
            new(0.120f, 0.052f, 0.058f, 0.055f),
            new(0.020f, 0.062f, 0.070f, 0.058f),
            new(-0.120f, 0.058f, 0.066f, 0.058f),
            new(-0.200f, 0.050f, 0.056f, 0.058f),
        ]);

        // Three tubes with rifled mouths.
        for (int i = 0; i < 3; i++)
        {
            float a = i / 3f * MathX.TwoPi + MathX.HalfPi;
            Vector3 off = new(MathF.Cos(a) * 0.056f, 0.062f + MathF.Sin(a) * 0.056f, 0f);
            mb.Material = (int)MatId.WeaponMetal;
            Shapes.BarrelBack(mb, off + new Vector3(0f, 0f, -0.220f),
            [
                new(0f, 0.044f), new(0.020f, 0.046f), new(0.400f, 0.046f),
                new(0.420f, 0.050f), new(0.428f, 0.044f),
            ], 14);
            mb.Material = (int)MatId.TechPanelDark;
            Shapes.BarrelBack(mb, off + new Vector3(0f, 0f, -0.640f),
                [new Vector2(0f, 0.038f), new Vector2(0.014f, 0.036f)], 12);
        }

        // Load plate and muzzle plate binding the cluster.
        mb.Material = (int)MatId.TechPanelDark;
        foreach (float z in new[] { -0.240f, -0.630f })
        {
            mb.PushTransform(Matrix4x4.CreateRotationX(MathX.HalfPi)
                * Matrix4x4.CreateTranslation(new Vector3(0f, 0.062f, z)));
            mb.AddLathe(
            [
                new(0.030f, -0.026f), new(0.100f, -0.030f), new(0.104f, -0.010f),
                new(0.104f, 0.010f), new(0.100f, 0.030f), new(0.030f, 0.026f),
            ], Vector3.Zero, 18);
            mb.PopTransform();
        }
        // Shroud over the top pair of tubes.
        mb.Material = (int)MatId.Trim;
        Receiver(mb,
        [
            new(-0.260f, 0.078f, 0.030f, 0.140f),
            new(-0.420f, 0.084f, 0.032f, 0.142f),
            new(-0.590f, 0.078f, 0.028f, 0.140f),
        ]);
        mb.Material = (int)MatId.EnergyPanel;
        mb.AddBox(new Vector3(0f, 0.166f, -0.300f), new Vector3(0.018f, 0.008f, 0.090f));
    }

    /// <summary>
    /// Sniper Rifle: a long bolt-action with a fluted barrel, a big scope on rings, a bipod under
    /// the fore-end and a shoulder stock.
    /// </summary>
    private static void BuildSniperRifle(MeshBuilder mb)
    {
        Grip(mb, 0.19f, 0.08f, 0.04f);

        // Stock running back over the shoulder, with a cheek rest.
        mb.Material = (int)MatId.TechPanelDark;
        Receiver(mb,
        [
            new(0.235f, 0.026f, 0.030f, -0.010f),
            new(0.170f, 0.030f, 0.042f, 0.006f),
            new(0.090f, 0.032f, 0.048f, 0.026f),
            new(0.010f, 0.034f, 0.052f, 0.038f),
            new(-0.110f, 0.032f, 0.050f, 0.040f),
            new(-0.180f, 0.028f, 0.044f, 0.040f),
        ]);
        mb.Material = (int)MatId.Trim;
        Shapes.RoundedBox(mb, new Vector3(0f, 0.084f, 0.140f), new Vector3(0.026f, 0.014f, 0.070f), 0.012f);

        // Fluted barrel with a muzzle brake.
        mb.Material = (int)MatId.WeaponMetal;
        Shapes.BarrelBack(mb, new Vector3(0f, 0.048f, -0.190f),
        [
            new(0f, 0.028f), new(0.030f, 0.030f), new(0.044f, 0.021f),
            new(0.480f, 0.019f), new(0.500f, 0.026f), new(0.556f, 0.026f),
            new(0.564f, 0.020f), new(0.590f, 0.020f), new(0.590f, 0.012f),
        ], 16);
        mb.Material = (int)MatId.TechPanelDark;
        for (int i = 0; i < 6; i++)
        {
            float a = i / 6f * MathX.TwoPi;
            mb.PushTransform(Matrix4x4.CreateRotationZ(a)
                * Matrix4x4.CreateTranslation(new Vector3(0f, 0.048f, -0.360f)));
            mb.AddBox(new Vector3(0f, 0.021f, 0f), new Vector3(0.004f, 0.005f, 0.120f));
            mb.PopTransform();
        }

        // Scope on two rings.
        mb.Material = (int)MatId.TechPanelDark;
        Shapes.BarrelBack(mb, new Vector3(0f, 0.118f, -0.030f),
        [
            new(0f, 0.020f), new(0.020f, 0.028f), new(0.090f, 0.026f),
            new(0.230f, 0.026f), new(0.270f, 0.032f), new(0.310f, 0.032f),
        ], 16);
        mb.Material = (int)MatId.Trim;
        foreach (float z in new[] { -0.070f, -0.230f })
            Shapes.Collar(mb, new Vector3(0f, 0.118f, z), 0.030f, 0.008f, 14);
        mb.Material = (int)MatId.Glass;
        Shapes.BarrelBack(mb, new Vector3(0f, 0.118f, -0.336f),
            [new Vector2(0f, 0.030f), new Vector2(0.006f, 0.030f)], 16);

        // Bolt handle and bipod.
        mb.Material = (int)MatId.Trim;
        Shapes.Strut(mb, new Vector3(0.032f, 0.058f, 0.020f), new Vector3(0.070f, 0.038f, 0.032f),
            0.008f, 0.011f, 8);
        mb.Material = (int)MatId.TechPanelDark;
        foreach (float sx in new[] { -1f, 1f })
            Shapes.Strut(mb, new Vector3(0f, 0.026f, -0.310f), new Vector3(sx * 0.055f, -0.070f, -0.350f),
                0.007f, 0.005f, 7);
    }

    /// <summary>
    /// Redeemer: a shoulder-launched nuclear missile. Fat tube with a blast shield at the rear,
    /// carry handle, guidance box, and the warhead nose visible in the mouth.
    /// </summary>
    private static void BuildRedeemer(MeshBuilder mb)
    {
        Grip(mb, 0.21f, 0.12f);

        // Launch tube.
        mb.Material = (int)MatId.TechPanelDark;
        Shapes.BarrelBack(mb, new Vector3(0f, 0.060f, 0.100f),
        [
            new(0f, 0.070f), new(0.030f, 0.098f), new(0.070f, 0.102f),
            new(0.830f, 0.102f), new(0.890f, 0.098f), new(0.950f, 0.072f),
        ], 22);
        mb.Material = (int)MatId.Trim;
        foreach (float z in new[] { 0.010f, -0.190f, -0.430f, -0.680f })
            Shapes.Collar(mb, new Vector3(0f, 0.060f, z), 0.104f, 0.013f, 20);

        // Rear blast shield — a cone opening backwards.
        mb.Material = (int)MatId.RustMetal;
        Shapes.Barrel(mb, new Vector3(0f, 0.060f, 0.100f),
        [
            new(0f, 0.072f), new(0.040f, 0.098f), new(0.090f, 0.126f), new(0.098f, 0.120f),
        ], 20);

        // Warhead nose showing in the mouth.
        mb.Material = (int)MatId.Lava;
        Shapes.BarrelBack(mb, new Vector3(0f, 0.060f, -0.790f),
        [
            new(0f, 0.086f), new(0.030f, 0.080f), new(0.062f, 0.058f),
            new(0.086f, 0.030f), new(0.096f, 0.008f),
        ], 18);

        // Carry handle over the top and the guidance box on the left.
        mb.Material = (int)MatId.WeaponMetal;
        Span<MeshBuilder.LoftStation> handle =
        [
            new(new Vector3(0f, 0.150f, 0.010f), new Vector2(0.014f, 0.010f)),
            new(new Vector3(0f, 0.196f, -0.030f), new Vector2(0.016f, 0.012f)),
            new(new Vector3(0f, 0.198f, -0.190f), new Vector2(0.016f, 0.012f)),
            new(new Vector3(0f, 0.150f, -0.230f), new Vector2(0.014f, 0.010f)),
        ];
        mb.AddLoft(Sections.RoundedRect(1f, 1f, 0.45f, 3), handle, capStart: false, capEnd: false);
        mb.Material = (int)MatId.TechPanelDark;
        Shapes.RoundedBox(mb, new Vector3(-0.116f, 0.060f, -0.070f), new Vector3(0.028f, 0.046f, 0.090f), 0.018f);
        mb.Material = (int)MatId.EnergyPanel;
        mb.AddBox(new Vector3(-0.146f, 0.070f, -0.070f), new Vector3(0.004f, 0.026f, 0.060f));

        // Fore grip under the tube.
        mb.Material = (int)MatId.TechPanelDark;
        Span<MeshBuilder.LoftStation> fore =
        [
            new(new Vector3(0f, -0.030f, -0.380f), new Vector2(0.022f, 0.026f)),
            new(new Vector3(0f, -0.090f, -0.398f), new Vector2(0.024f, 0.028f)),
            new(new Vector3(0f, -0.140f, -0.410f), new Vector2(0.018f, 0.022f)),
        ];
        mb.AddLoft(Sections.Superellipse(1f, 1f, 2.8f, 12), fore, capStart: false, capEnd: false);
    }

    // ================================================================ UT2004 / UT3

    /// <summary>
    /// Shield Gun: a riot tool rather than a firearm. Short body, a wide emitter dish at the front
    /// that the shield projects from, and two capacitor bottles slung underneath — the dish is
    /// what separates it at a glance from the Impact Hammer's flat percussion head.
    /// </summary>
    private static void BuildShieldGun(MeshBuilder mb)
    {
        Grip(mb, 0.19f, 0.05f);

        mb.Material = (int)MatId.WeaponMetal;
        Receiver(mb,
        [
            new(0.080f, 0.040f, 0.050f, 0.048f),
            new(-0.020f, 0.052f, 0.062f, 0.050f),
            new(-0.160f, 0.048f, 0.058f, 0.050f),
            new(-0.230f, 0.038f, 0.044f, 0.050f),
        ]);

        // The emitter: a shallow dish on a short throat, so it reads as a projector.
        mb.Material = (int)MatId.Trim;
        Shapes.BarrelBack(mb, new Vector3(0f, 0.050f, -0.236f),
        [
            new(0f, 0.038f), new(0.028f, 0.044f), new(0.040f, 0.074f),
            new(0.062f, 0.104f), new(0.076f, 0.100f), new(0.078f, 0.060f),
        ], 20);
        mb.Material = (int)MatId.EnergyPanel;
        Shapes.BarrelBack(mb, new Vector3(0f, 0.050f, -0.300f),
            [new Vector2(0f, 0.086f), new Vector2(0.006f, 0.090f), new Vector2(0.014f, 0.084f)], 26);
        // Emitter ribs radiating out across the dish face.
        mb.Material = (int)MatId.Trim;
        for (int i = 0; i < 8; i++)
        {
            float a = i / 8f * MathX.TwoPi;
            Shapes.Strut(mb, new Vector3(MathF.Cos(a) * 0.026f, 0.050f + MathF.Sin(a) * 0.026f, -0.290f),
                new Vector3(MathF.Cos(a) * 0.084f, 0.050f + MathF.Sin(a) * 0.084f, -0.302f),
                0.005f, 0.004f, 6);
        }
        Bolts(mb, 0.086f, -0.180f, 0.050f, 4, 0.030f);
        Rail(mb, 0.100f, -0.170f, 0.020f, 5);

        // Capacitor bottles under the barrel.
        mb.Material = (int)MatId.TechPanelDark;
        foreach (float sx in new[] { -0.030f, 0.030f })
        {
            Shapes.Barrel(mb, new Vector3(sx, 0.008f, -0.120f),
                [new Vector2(0f, 0.018f), new Vector2(0.010f, 0.026f), new Vector2(0.020f, 0.024f),
                 new Vector2(0.100f, 0.024f), new Vector2(0.112f, 0.026f),
                 new Vector2(0.122f, 0.016f)], 18);
            Fins(mb, new Vector3(sx, 0.030f, -0.120f), new Vector3(sx, 0.100f, -0.120f), 4, 0.028f);
        }
        Sights(mb, 0.108f, -0.190f, 0.010f, 0.9f);
    }

    /// <summary>
    /// Assault Rifle: a plain 5.56mm carbine with a curved magazine and the M355 grenade tube
    /// clamped under the barrel. The tube is the silhouette cue — without it this is any rifle.
    /// </summary>
    private static void BuildAssaultRifle(MeshBuilder mb)
    {
        Grip(mb, 0.20f, 0.075f);

        mb.Material = (int)MatId.WeaponMetal;
        Receiver(mb,
        [
            new(0.140f, 0.030f, 0.040f, 0.040f),
            new(0.060f, 0.038f, 0.050f, 0.044f),
            new(-0.120f, 0.036f, 0.048f, 0.046f),
            new(-0.220f, 0.028f, 0.036f, 0.046f),
        ]);
        // Stock behind the receiver.
        mb.Material = (int)MatId.TechPanelDark;
        Receiver(mb,
        [
            new(0.150f, 0.022f, 0.030f, 0.030f),
            new(0.260f, 0.020f, 0.034f, 0.018f),
        ]);
        Magazine(mb, new Vector3(0f, 0.014f, -0.030f), 0.016f, 0.034f, 0.115f);

        // Barrel, shroud, then the underslung grenade tube.
        mb.Material = (int)MatId.WeaponMetal;
        Shapes.BarrelBack(mb, new Vector3(0f, 0.046f, -0.226f),
        [
            new(0f, 0.034f), new(0.014f, 0.030f), new(0.150f, 0.028f),
            new(0.164f, 0.034f), new(0.176f, 0.030f),
        ], 14);
        Vents(mb, new Vector3(0f, 0.046f, -0.300f), 0.030f, 0.070f, 8);
        Fins(mb, new Vector3(0f, 0.046f, -0.250f), new Vector3(0f, 0.046f, -0.370f), 5, 0.033f);
        Bolts(mb, 0.070f, -0.180f, 0.120f, 5, 0.032f);
        Rail(mb, 0.086f, -0.170f, 0.090f, 7);
        mb.Material = (int)MatId.TechPanelDark;
        Shapes.BarrelBack(mb, new Vector3(0f, 0.006f, -0.190f),
        [
            new(0f, 0.026f), new(0.018f, 0.030f), new(0.130f, 0.030f), new(0.142f, 0.024f),
        ], 12);
        Sights(mb, 0.086f, -0.290f, 0.060f, 0.9f);
    }

    /// <summary>
    /// Link Gun: three prongs around a central emitter, with a translucent energy conduit running
    /// the length of the body. The prongs are what the beam arcs between, so they have to be the
    /// thing you notice.
    /// </summary>
    private static void BuildLinkGun(MeshBuilder mb)
    {
        Grip(mb, 0.20f, 0.07f);

        mb.Material = (int)MatId.WeaponMetal;
        Receiver(mb,
        [
            new(0.100f, 0.034f, 0.044f, 0.046f),
            new(0.000f, 0.046f, 0.058f, 0.050f),
            new(-0.180f, 0.044f, 0.056f, 0.050f),
            new(-0.300f, 0.034f, 0.042f, 0.050f),
        ]);
        // Energy conduit along the spine.
        mb.Material = (int)MatId.EnergyPanel;
        Receiver(mb,
        [
            new(0.020f, 0.014f, 0.012f, 0.104f),
            new(-0.280f, 0.014f, 0.012f, 0.104f),
        ]);

        // Central emitter throat.
        mb.Material = (int)MatId.WeaponMetal;
        Shapes.BarrelBack(mb, new Vector3(0f, 0.050f, -0.306f),
        [
            new(0f, 0.042f), new(0.020f, 0.036f), new(0.130f, 0.032f), new(0.146f, 0.026f),
        ], 16);
        // The three prongs, splayed forward around it.
        mb.Material = (int)MatId.Trim;
        for (int i = 0; i < 3; i++)
        {
            float a = i / 3f * MathX.TwoPi + MathX.HalfPi;
            float sx = MathF.Cos(a) * 0.044f, sy = MathF.Sin(a) * 0.044f;
            Shapes.Strut(mb, new Vector3(sx, 0.050f + sy, -0.320f),
                new Vector3(sx * 0.62f, 0.050f + sy * 0.62f, -0.492f), 0.010f, 0.007f, 8);
        }
        mb.Material = (int)MatId.EnergyPanel;
        Shapes.Collar(mb, new Vector3(0f, 0.050f, -0.470f), 0.030f, 0.008f, 20);
        Fins(mb, new Vector3(0f, 0.050f, -0.330f), new Vector3(0f, 0.050f, -0.440f), 4, 0.038f);
        // Coolant lines running from the conduit down to each prong root.
        mb.Material = (int)MatId.TechPanelDark;
        for (int i = 0; i < 3; i++)
        {
            float a = i / 3f * MathX.TwoPi + MathX.HalfPi;
            Shapes.Strut(mb, new Vector3(0f, 0.104f, -0.200f),
                new Vector3(MathF.Cos(a) * 0.044f, 0.050f + MathF.Sin(a) * 0.044f, -0.318f),
                0.006f, 0.005f, 8);
        }
        Bolts(mb, 0.084f, -0.260f, 0.070f, 5, 0.042f);
        Sights(mb, 0.118f, -0.250f, 0.010f, 0.9f);
    }

    /// <summary>
    /// Lightning Gun: a long rifle with a coil stack instead of a barrel shroud and a big scope.
    /// The coils are the whole point — it should read as an electrical instrument, not a firearm.
    /// </summary>
    private static void BuildLightningGun(MeshBuilder mb)
    {
        Grip(mb, 0.20f, 0.10f);

        mb.Material = (int)MatId.WeaponMetal;
        Receiver(mb,
        [
            new(0.190f, 0.026f, 0.036f, 0.038f),
            new(0.090f, 0.036f, 0.048f, 0.044f),
            new(-0.120f, 0.034f, 0.046f, 0.046f),
            new(-0.260f, 0.026f, 0.034f, 0.046f),
        ]);
        mb.Material = (int)MatId.TechPanelDark;
        Receiver(mb,
        [
            new(0.200f, 0.022f, 0.032f, 0.026f),
            new(0.330f, 0.020f, 0.036f, 0.012f),
        ]);

        // Barrel with a stack of induction coils along it.
        mb.Material = (int)MatId.WeaponMetal;
        Shapes.BarrelBack(mb, new Vector3(0f, 0.046f, -0.268f),
        [
            new(0f, 0.028f), new(0.014f, 0.024f), new(0.420f, 0.022f),
            new(0.440f, 0.030f), new(0.456f, 0.024f),
        ], 14);
        mb.Material = (int)MatId.EnergyPanel;
        for (int i = 0; i < 6; i++)
            Shapes.Collar(mb, new Vector3(0f, 0.046f, -0.320f - i * 0.062f), 0.038f, 0.012f, 16);

        // Scope on a pair of rings.
        mb.Material = (int)MatId.TechPanelDark;
        Shapes.Barrel(mb, new Vector3(0f, 0.116f, -0.040f),
            [new Vector2(0f, 0.016f), new Vector2(0.012f, 0.022f),
             new Vector2(0.180f, 0.022f), new Vector2(0.194f, 0.016f)], 14);
        mb.Material = (int)MatId.Trim;
        foreach (float z in new[] { -0.060f, -0.170f })
            mb.AddBox(new Vector3(0f, 0.092f, z), new Vector3(0.010f, 0.020f, 0.008f));
    }

    /// <summary>
    /// Mine Layer: a squat launcher with a four-round drum on top and a claw muzzle the drones are
    /// pushed out through, plus the guidance laser housing under the barrel.
    /// </summary>
    private static void BuildMineLayer(MeshBuilder mb)
    {
        Grip(mb, 0.20f, 0.06f);

        mb.Material = (int)MatId.WeaponMetal;
        Receiver(mb,
        [
            new(0.110f, 0.040f, 0.048f, 0.046f),
            new(0.000f, 0.054f, 0.062f, 0.050f),
            new(-0.160f, 0.050f, 0.058f, 0.050f),
            new(-0.240f, 0.040f, 0.046f, 0.050f),
        ]);

        // Drum of four drones sitting proud of the receiver.
        mb.Material = (int)MatId.TechPanelDark;
        Shapes.Barrel(mb, new Vector3(0f, 0.112f, -0.060f),
            [new Vector2(0f, 0.052f), new Vector2(0.014f, 0.058f),
             new Vector2(0.086f, 0.058f), new Vector2(0.100f, 0.050f)], 16);
        mb.Material = (int)MatId.Trim;
        for (int i = 0; i < 4; i++)
        {
            float a = i / 4f * MathX.TwoPi;
            mb.PushTransform(Matrix4x4.CreateTranslation(
                new Vector3(MathF.Cos(a) * 0.034f, 0.112f + MathF.Sin(a) * 0.034f, -0.060f)));
            mb.AddBox(Vector3.Zero, new Vector3(0.012f, 0.012f, 0.042f));
            mb.PopTransform();
        }

        // Claw muzzle: four fingers around the launch throat.
        mb.Material = (int)MatId.WeaponMetal;
        Shapes.BarrelBack(mb, new Vector3(0f, 0.050f, -0.248f),
            [new Vector2(0f, 0.046f), new Vector2(0.020f, 0.050f), new Vector2(0.090f, 0.048f)], 14);
        mb.Material = (int)MatId.Trim;
        for (int i = 0; i < 4; i++)
        {
            float a = i / 4f * MathX.TwoPi + MathX.Pi * 0.25f;
            float sx = MathF.Cos(a) * 0.046f, sy = MathF.Sin(a) * 0.046f;
            Shapes.Strut(mb, new Vector3(sx, 0.050f + sy, -0.330f),
                new Vector3(sx * 1.25f, 0.050f + sy * 1.25f, -0.392f), 0.008f, 0.005f, 6);
        }
        // Guidance laser under the barrel.
        mb.Material = (int)MatId.EnergyPanel;
        Shapes.BarrelBack(mb, new Vector3(0f, 0.006f, -0.230f),
            [new Vector2(0f, 0.014f), new Vector2(0.012f, 0.018f), new Vector2(0.084f, 0.016f),
             new Vector2(0.096f, 0.012f)], 16);
        Fins(mb, new Vector3(0f, 0.112f, -0.020f), new Vector3(0f, 0.112f, -0.140f), 5, 0.062f);
        Bolts(mb, 0.086f, -0.200f, 0.080f, 5, 0.040f);
        Rail(mb, 0.100f, -0.190f, 0.040f, 6);
    }

    /// <summary>
    /// Grenade Launcher: a fat drum-fed tube. Almost all of its bulk is the drum, which is what
    /// tells you at a glance that it holds eight of something heavy.
    /// </summary>
    private static void BuildGrenadeLauncher(MeshBuilder mb)
    {
        Grip(mb, 0.20f, 0.075f);

        mb.Material = (int)MatId.WeaponMetal;
        Receiver(mb,
        [
            new(0.120f, 0.036f, 0.044f, 0.044f),
            new(0.020f, 0.048f, 0.056f, 0.048f),
            new(-0.120f, 0.044f, 0.052f, 0.048f),
            new(-0.200f, 0.036f, 0.042f, 0.048f),
        ]);

        // Drum magazine on the side, canted, with visible rounds.
        mb.Material = (int)MatId.TechPanelDark;
        mb.PushTransform(Matrix4x4.CreateRotationY(MathX.HalfPi)
            * Matrix4x4.CreateTranslation(new Vector3(0f, 0.048f, -0.020f)));
        Shapes.Barrel(mb, Vector3.Zero,
            [new Vector2(-0.030f, 0.070f), new Vector2(-0.018f, 0.086f),
             new Vector2(0.018f, 0.086f), new Vector2(0.030f, 0.070f)], 18);
        mb.PopTransform();
        mb.Material = (int)MatId.Trim;
        for (int i = 0; i < 8; i++)
        {
            float a = i / 8f * MathX.TwoPi;
            mb.PushTransform(Matrix4x4.CreateRotationY(MathX.HalfPi)
                * Matrix4x4.CreateTranslation(new Vector3(
                    0f, 0.048f + MathF.Sin(a) * 0.062f, -0.020f + MathF.Cos(a) * 0.062f)));
            mb.AddBox(Vector3.Zero, new Vector3(0.030f, 0.013f, 0.013f));
            mb.PopTransform();
        }

        // Short, wide tube.
        mb.Material = (int)MatId.WeaponMetal;
        Shapes.BarrelBack(mb, new Vector3(0f, 0.048f, -0.208f),
        [
            new(0f, 0.050f), new(0.020f, 0.044f), new(0.170f, 0.042f),
            new(0.186f, 0.052f), new(0.202f, 0.044f),
        ], 16);
        Fins(mb, new Vector3(0f, 0.048f, -0.230f), new Vector3(0f, 0.048f, -0.380f), 5, 0.050f);
        Bolts(mb, 0.084f, -0.170f, 0.090f, 5, 0.038f);
        Rail(mb, 0.098f, -0.160f, 0.060f, 6);
        Sights(mb, 0.116f, -0.300f, 0.040f, 0.9f);
    }

    /// <summary>
    /// AVRiL: a shoulder tube with a big optical tracker box on top. Blast cone at the back,
    /// because it fires from the shoulder and the backblast has to go somewhere.
    /// </summary>
    private static void BuildAvril(MeshBuilder mb)
    {
        Grip(mb, 0.19f, 0.02f);

        mb.Material = (int)MatId.WeaponMetal;
        Shapes.BarrelBack(mb, new Vector3(0f, 0.062f, 0.260f),
        [
            new(0f, 0.066f), new(0.026f, 0.048f), new(0.060f, 0.046f),
            new(0.760f, 0.046f), new(0.790f, 0.056f), new(0.820f, 0.050f),
        ], 18);
        // Reinforcing bands.
        mb.Material = (int)MatId.Trim;
        foreach (float z in new[] { 0.120f, -0.060f, -0.240f, -0.400f })
            Shapes.Collar(mb, new Vector3(0f, 0.062f, z), 0.052f, 0.010f, 16);

        // Optical tracker: a boxy housing with a lens, sitting over the tube.
        mb.Material = (int)MatId.TechPanelDark;
        Receiver(mb,
        [
            new(0.090f, 0.034f, 0.030f, 0.126f),
            new(-0.130f, 0.038f, 0.034f, 0.130f),
            new(-0.200f, 0.030f, 0.026f, 0.126f),
        ]);
        mb.Material = (int)MatId.EnergyPanel;
        Shapes.BarrelBack(mb, new Vector3(0f, 0.130f, -0.206f),
            [new Vector2(0f, 0.024f), new Vector2(0.016f, 0.018f)], 14);
        // Forward hand grip under the tube.
        mb.Material = (int)MatId.TechPanelDark;
        Shapes.Strut(mb, new Vector3(0f, 0.020f, -0.230f), new Vector3(0f, -0.070f, -0.250f),
            0.016f, 0.020f, 8);
    }

    /// <summary>
    /// The two painters share a chassis — a squat body with a tripod-braced optic and a laser
    /// aperture. They differ only in the head, which is the honest way round: in the original they
    /// are the same device pointed at two different things in orbit.
    /// </summary>
    private static void BuildPainter(MeshBuilder mb, bool ion)
    {
        Grip(mb, 0.19f, 0.06f);

        mb.Material = (int)MatId.WeaponMetal;
        Receiver(mb,
        [
            new(0.110f, 0.032f, 0.042f, 0.044f),
            new(0.010f, 0.044f, 0.054f, 0.048f),
            new(-0.140f, 0.040f, 0.050f, 0.048f),
            new(-0.220f, 0.032f, 0.040f, 0.048f),
        ]);

        // Optic block with a wide objective.
        mb.Material = (int)MatId.TechPanelDark;
        Shapes.Barrel(mb, new Vector3(0f, 0.122f, -0.030f),
            [new Vector2(0f, 0.026f), new Vector2(0.014f, 0.034f),
             new Vector2(0.210f, 0.034f), new Vector2(0.226f, 0.026f)], 16);
        mb.Material = ion ? (int)MatId.EnergyPanel : (int)MatId.Trim;
        Shapes.BarrelBack(mb, new Vector3(0f, 0.122f, -0.244f),
            [new Vector2(0f, 0.032f), new Vector2(0.014f, 0.024f)], 16);

        // Laser aperture on the barrel line.
        mb.Material = (int)MatId.WeaponMetal;
        Shapes.BarrelBack(mb, new Vector3(0f, 0.046f, -0.226f),
            [new Vector2(0f, 0.032f), new Vector2(0.018f, 0.026f), new Vector2(0.190f, 0.024f)], 14);
        mb.Material = (int)MatId.EnergyPanel;
        Shapes.BarrelBack(mb, new Vector3(0f, 0.046f, -0.420f),
            [new Vector2(0f, 0.022f), new Vector2(0.010f, 0.014f)], 12);

        // Folding brace under the fore-end, which is how you hold a thing this long steady.
        mb.Material = (int)MatId.Trim;
        foreach (float sx in new[] { -0.026f, 0.026f })
            Shapes.Strut(mb, new Vector3(0f, 0.020f, -0.260f), new Vector3(sx, -0.086f, -0.320f),
                0.006f, 0.005f, 6);
        // The head: a ring of emitters for the ion beam, a stubby antenna for the bomber call.
        if (ion)
        {
            mb.Material = (int)MatId.EnergyPanel;
            Shapes.Collar(mb, new Vector3(0f, 0.046f, -0.396f), 0.040f, 0.012f, 18);
        }
        else
        {
            mb.Material = (int)MatId.Trim;
            Shapes.Strut(mb, new Vector3(0f, 0.086f, -0.150f), new Vector3(0f, 0.220f, -0.120f),
                0.007f, 0.005f, 8);
            // Dish antenna at the mast head: this one talks to a bomber rather than to orbit.
            Shapes.BarrelBack(mb, new Vector3(0f, 0.226f, -0.118f),
                [new Vector2(0f, 0.010f), new Vector2(0.014f, 0.034f), new Vector2(0.022f, 0.030f)], 18);
        }
        Fins(mb, new Vector3(0f, 0.122f, -0.060f), new Vector3(0f, 0.122f, -0.200f), 5, 0.040f);
        Fins(mb, new Vector3(0f, 0.046f, -0.260f), new Vector3(0f, 0.046f, -0.390f), 4, 0.030f);
        Bolts(mb, 0.080f, -0.190f, 0.080f, 5, 0.038f);
        Rail(mb, 0.094f, -0.180f, 0.050f, 6);
    }

    /// <summary>
    /// Translocator: a compact launcher with the disc visibly seated in a fork at the front. Barely
    /// a weapon, and it should look like it — the disc is the only part that matters.
    /// </summary>
    private static void BuildTranslocator(MeshBuilder mb)
    {
        Grip(mb, 0.18f, 0.05f);

        mb.Material = (int)MatId.WeaponMetal;
        Receiver(mb,
        [
            new(0.080f, 0.028f, 0.038f, 0.040f),
            new(0.000f, 0.036f, 0.046f, 0.044f),
            new(-0.100f, 0.032f, 0.042f, 0.044f),
            new(-0.150f, 0.026f, 0.034f, 0.044f),
        ]);
        // Fork holding the disc.
        mb.Material = (int)MatId.Trim;
        foreach (float sx in new[] { -0.036f, 0.036f })
            Shapes.Strut(mb, new Vector3(sx, 0.044f, -0.150f), new Vector3(sx, 0.044f, -0.240f),
                0.008f, 0.008f, 6);
        // The disc itself, on edge between the tines.
        mb.Material = (int)MatId.EnergyPanel;
        mb.PushTransform(Matrix4x4.CreateRotationX(MathX.HalfPi)
            * Matrix4x4.CreateTranslation(new Vector3(0f, 0.044f, -0.226f)));
        Shapes.Barrel(mb, Vector3.Zero,
            [new Vector2(-0.008f, 0.030f), new Vector2(0f, 0.042f), new Vector2(0.008f, 0.030f)], 18);
        mb.PopTransform();
        mb.Material = (int)MatId.TechPanelDark;
        Magazine(mb, new Vector3(0f, 0.012f, -0.020f), 0.014f, 0.026f, 0.070f);
        // Beacon lamps around the disc rim, so it reads as a recall marker rather than a coin.
        mb.Material = (int)MatId.EnergyPanel;
        for (int i = 0; i < 8; i++)
        {
            float a = i / 8f * MathX.TwoPi;
            mb.AddSphere(new Vector3(MathF.Cos(a) * 0.038f, 0.044f + MathF.Sin(a) * 0.038f, -0.226f),
                0.006f, 5, 7);
        }
        Fins(mb, new Vector3(0f, 0.044f, -0.060f), new Vector3(0f, 0.044f, -0.140f), 4, 0.030f);
        Bolts(mb, 0.072f, -0.130f, 0.060f, 4, 0.028f);
    }

    /// <summary>
    /// Super Shock Rifle: the Shock Rifle with the restraint removed — heavier coils, a longer
    /// emitter, and everything that glowed on the original glowing harder.
    /// </summary>
    private static void BuildSuperShockRifle(MeshBuilder mb)
    {
        BuildShockRifle(mb);
        mb.Material = (int)MatId.EnergyPanel;
        foreach (float z in new[] { -0.350f, -0.430f, -0.510f })
            Shapes.Collar(mb, new Vector3(0f, 0.050f, z), 0.058f, 0.014f, 18);
        Shapes.BarrelBack(mb, new Vector3(0f, 0.050f, -0.628f),
            [new Vector2(0f, 0.040f), new Vector2(0.034f, 0.020f)], 18);
    }

    /// <summary>
    /// Stinger: a mining tool pressed into service. A crystal hopper on top feeding a stubby
    /// multi-throat muzzle, with raw tarydium visible in the feed — nothing about it should look
    /// like the machined Minigun it replaced.
    /// </summary>
    private static void BuildStinger(MeshBuilder mb)
    {
        Grip(mb, 0.20f, 0.09f);

        mb.Material = (int)MatId.RustMetal;
        Receiver(mb,
        [
            new(0.150f, 0.042f, 0.052f, 0.046f),
            new(0.030f, 0.058f, 0.068f, 0.052f),
            new(-0.160f, 0.054f, 0.064f, 0.052f),
            new(-0.260f, 0.042f, 0.050f, 0.052f),
        ]);

        // Crystal hopper.
        mb.Material = (int)MatId.TechPanelDark;
        Receiver(mb,
        [
            new(0.080f, 0.036f, 0.030f, 0.118f),
            new(-0.020f, 0.048f, 0.044f, 0.132f),
            new(-0.140f, 0.036f, 0.030f, 0.118f),
        ]);
        mb.Material = (int)MatId.EnergyPanel;
        for (int i = 0; i < 5; i++)
        {
            float z = 0.050f - i * 0.042f;
            mb.PushTransform(Matrix4x4.CreateRotationZ(0.4f * (i % 2 == 0 ? 1f : -1f))
                * Matrix4x4.CreateTranslation(new Vector3(0f, 0.166f, z)));
            mb.AddBox(Vector3.Zero, new Vector3(0.014f, 0.026f, 0.014f));
            mb.PopTransform();
        }

        // Muzzle: four short throats around a common axis.
        mb.Material = (int)MatId.RustMetal;
        Shapes.BarrelBack(mb, new Vector3(0f, 0.054f, -0.268f),
            [new Vector2(0f, 0.052f), new Vector2(0.024f, 0.056f), new Vector2(0.120f, 0.050f)], 16);
        mb.Material = (int)MatId.WeaponMetal;
        for (int i = 0; i < 4; i++)
        {
            float a = i / 4f * MathX.TwoPi + MathX.Pi * 0.25f;
            Shapes.BarrelBack(mb,
                new Vector3(MathF.Cos(a) * 0.030f, 0.054f + MathF.Sin(a) * 0.030f, -0.300f),
                [new Vector2(0f, 0.014f), new Vector2(0.120f, 0.012f)], 10);
        }
        mb.Material = (int)MatId.Trim;
        Shapes.Collar(mb, new Vector3(0f, 0.054f, -0.400f), 0.050f, 0.012f, 20);
        Fins(mb, new Vector3(0f, 0.054f, -0.290f), new Vector3(0f, 0.054f, -0.390f), 4, 0.056f);
        Bolts(mb, 0.092f, -0.230f, 0.120f, 6, 0.046f);
        Rail(mb, 0.108f, -0.220f, 0.080f, 7);
        // Feed pipes carrying crystal from the hopper down into the breech.
        mb.Material = (int)MatId.RustMetal;
        foreach (float sx in new[] { -0.040f, 0.040f })
            Shapes.Strut(mb, new Vector3(sx, 0.140f, -0.020f), new Vector3(sx * 0.6f, 0.060f, -0.180f),
                0.008f, 0.007f, 8);
    }

    /// <summary>
    /// Ball Launcher: a cradle rather than a barrel. It holds the ball in an open ring and throws
    /// it, and since it cannot hurt anybody it deliberately has no muzzle at all.
    /// </summary>
    private static void BuildBallLauncher(MeshBuilder mb)
    {
        Grip(mb, 0.19f, 0.06f);

        mb.Material = (int)MatId.WeaponMetal;
        Receiver(mb,
        [
            new(0.090f, 0.030f, 0.040f, 0.042f),
            new(0.000f, 0.040f, 0.050f, 0.046f),
            new(-0.110f, 0.036f, 0.046f, 0.046f),
            new(-0.170f, 0.028f, 0.036f, 0.046f),
        ]);
        // Open cradle: three arms curving forward around where the ball sits.
        mb.Material = (int)MatId.Trim;
        for (int i = 0; i < 3; i++)
        {
            float a = i / 3f * MathX.TwoPi + MathX.HalfPi;
            float sx = MathF.Cos(a), sy = MathF.Sin(a);
            Span<MeshBuilder.LoftStation> arm =
            [
                new(new Vector3(sx * 0.030f, 0.046f + sy * 0.030f, -0.176f), 0.009f),
                new(new Vector3(sx * 0.062f, 0.046f + sy * 0.062f, -0.240f), 0.009f),
                new(new Vector3(sx * 0.052f, 0.046f + sy * 0.052f, -0.320f), 0.007f),
            ];
            mb.AddLoft(Sections.Circle(1f, 7), arm, capStart: false, capEnd: false);
        }
        mb.Material = (int)MatId.EnergyPanel;
        mb.AddSphere(new Vector3(0f, 0.046f, -0.256f), 0.046f, 14, 20);
        Fins(mb, new Vector3(0f, 0.046f, -0.120f), new Vector3(0f, 0.046f, -0.180f), 3, 0.032f);
        Bolts(mb, 0.070f, -0.140f, 0.060f, 4, 0.030f);
        Rail(mb, 0.082f, -0.130f, 0.040f, 5);
    }

    public void Dispose()
    {
        foreach (var m in _meshes) m?.Dispose();
    }
}
