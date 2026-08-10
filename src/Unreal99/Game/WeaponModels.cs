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

    public Mesh MeshFor(WeaponKind k) => _meshes[(int)k];
    public MeshSection[] SectionsFor(WeaponKind k) => _sections[(int)k];

    /// <summary>Model-space bounds of the built mesh; the turntable camera frames from these.</summary>
    public (Vector3 Min, Vector3 Max) BoundsFor(WeaponKind k) => _bounds[(int)k];

    public WeaponModels(GL gl)
    {
        for (int i = 0; i < (int)WeaponKind.Count; i++)
        {
            var mb = new MeshBuilder { WorldUv = false, Material = (int)MatId.WeaponMetal };
            Build((WeaponKind)i, mb);
            mb.RecalculateTangents();
            _bounds[i] = mb.Bounds();
            var (v, ind, s) = mb.Build();
            _meshes[i] = Mesh.CreateStatic<Vertex>(gl, v, ind, VertexLayouts.Static);
            _sections[i] = s;
        }
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
            [new Vector2(0f, 0.019f), new Vector2(0.048f, 0.018f), new Vector2(0.052f, 0.014f)], 14);
        mb.Material = (int)MatId.TechPanelDark;
        Shapes.BarrelBack(mb, new Vector3(0f, 0.048f, -0.200f),
            [new Vector2(0f, 0.010f), new Vector2(0.052f, 0.010f)], 10);

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

    public void Dispose()
    {
        foreach (var m in _meshes) m?.Dispose();
    }
}
