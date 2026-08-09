using System.Numerics;
using Silk.NET.OpenGL;
using Unreal99.Core;
using Unreal99.Rendering;

namespace Unreal99.Game;

/// <summary>
/// Procedural hulls for every vehicle. Like everything else in this project these are built from
/// primitives at load time rather than imported, so each one is a readable silhouette rather than
/// a detailed model: what matters at gameplay distance is telling a Goliath from a Manta at a
/// glance, and a walker from either.
/// </summary>
public sealed class VehicleModels : IDisposable
{
    private readonly Mesh[] _meshes = new Mesh[(int)VehicleKind.Count];
    private readonly MeshSection[][] _sections = new MeshSection[(int)VehicleKind.Count][];

    public Mesh MeshFor(VehicleKind k) => _meshes[(int)k];
    public MeshSection[] SectionsFor(VehicleKind k) => _sections[(int)k];

    public VehicleModels(GL gl)
    {
        for (int i = 0; i < (int)VehicleKind.Count; i++)
        {
            var mb = new MeshBuilder { WorldUv = false, Material = (int)MatId.ArmorPlate };
            Build((VehicleKind)i, mb);
            mb.RecalculateTangents();
            var (v, ind, s) = mb.Build();
            _meshes[i] = Mesh.CreateStatic<Vertex>(gl, v, ind, VertexLayouts.Static);
            _sections[i] = s;
        }
    }

    private static void Wheels(MeshBuilder mb, float x, float z, float radius, float y = 0f)
    {
        mb.Material = (int)MatId.TechPanelDark;
        foreach (float sx in new[] { -x, x })
            foreach (float sz in new[] { -z, z })
            {
                mb.PushTransform(Matrix4x4.CreateRotationZ(MathX.HalfPi)
                    * Matrix4x4.CreateTranslation(new Vector3(sx, y + radius, sz)));
                mb.AddCylinder(Vector3.Zero, radius, radius, 0.42f, 10);
                mb.PopTransform();
            }
    }

    /// <summary>Barrel lying along the hull's forward axis (-Z), like the weapon models use.</summary>
    private static void Barrel(MeshBuilder mb, Vector3 centre, float radius, float length, int segs = 10)
    {
        mb.PushTransform(Matrix4x4.CreateRotationX(-MathX.HalfPi) * Matrix4x4.CreateTranslation(centre));
        mb.AddCylinder(Vector3.Zero, radius, radius, length, segs);
        mb.PopTransform();
    }

    private static void Legs(MeshBuilder mb, float spread, float height, int count)
    {
        mb.Material = (int)MatId.TechPanelDark;
        for (int i = 0; i < count; i++)
        {
            float a = i / (float)count * MathX.TwoPi + 0.4f;
            Vector3 foot = new(MathF.Cos(a) * spread, 0f, MathF.Sin(a) * spread);
            mb.AddBox(foot + new Vector3(0f, height * 0.5f, 0f), new Vector3(0.22f, height * 0.5f, 0.22f));
            mb.AddBox(foot + new Vector3(0f, 0.12f, 0f), new Vector3(0.45f, 0.12f, 0.45f));
        }
    }

    private static void Build(VehicleKind kind, MeshBuilder mb)
    {
        switch (kind)
        {
            case VehicleKind.Scorpion:
                mb.Material = (int)MatId.ArmorPlate;
                mb.AddBox(new Vector3(0, 0.85f, 0), new Vector3(1.0f, 0.34f, 2.1f));
                mb.AddBox(new Vector3(0, 1.25f, 0.5f), new Vector3(0.62f, 0.30f, 0.7f));
                mb.Material = (int)MatId.Trim;   // the blade booms that make it what it is
                mb.AddBox(new Vector3(-1.25f, 0.85f, -0.4f), new Vector3(0.42f, 0.07f, 0.9f));
                mb.AddBox(new Vector3(1.25f, 0.85f, -0.4f), new Vector3(0.42f, 0.07f, 0.9f));
                Wheels(mb, 1.1f, 1.5f, 0.5f);
                break;

            case VehicleKind.Hellbender:
                mb.Material = (int)MatId.ArmorPlate;
                mb.AddBox(new Vector3(0, 1.0f, 0), new Vector3(1.35f, 0.42f, 2.6f));
                mb.AddBox(new Vector3(0, 1.5f, 0.9f), new Vector3(0.95f, 0.38f, 1.0f));
                mb.Material = (int)MatId.WeaponMetal;
                mb.AddBox(new Vector3(0, 1.72f, -0.4f), new Vector3(0.5f, 0.3f, 0.5f));   // skymine turret
                mb.AddBox(new Vector3(0, 1.72f, -1.7f), new Vector3(0.42f, 0.28f, 0.5f)); // laser turret
                Barrel(mb, new Vector3(0, 1.72f, -2.4f), 0.14f, 0.9f);
                Wheels(mb, 1.45f, 1.9f, 0.6f);
                break;

            case VehicleKind.Goliath:
                mb.Material = (int)MatId.ArmorPlate;
                mb.AddBox(new Vector3(0, 1.15f, 0), new Vector3(1.75f, 0.55f, 3.0f));
                mb.Material = (int)MatId.WeaponMetal;
                mb.AddBox(new Vector3(0, 1.95f, 0.2f), new Vector3(1.05f, 0.42f, 1.3f));   // turret
                Barrel(mb, new Vector3(0, 1.95f, -1.9f), 0.18f, 2.6f);
                mb.AddBox(new Vector3(0, 2.45f, 0.2f), new Vector3(0.28f, 0.18f, 0.4f));   // coax MG
                mb.Material = (int)MatId.TechPanelDark;   // tracks
                mb.AddBox(new Vector3(-1.85f, 0.65f, 0f), new Vector3(0.34f, 0.62f, 3.0f));
                mb.AddBox(new Vector3(1.85f, 0.65f, 0f), new Vector3(0.34f, 0.62f, 3.0f));
                break;

            case VehicleKind.Leviathan:
                mb.Material = (int)MatId.ArmorPlate;
                mb.AddBox(new Vector3(0, 1.6f, 0), new Vector3(2.9f, 0.9f, 5.0f));
                mb.AddBox(new Vector3(0, 2.7f, 0.6f), new Vector3(1.9f, 0.55f, 2.2f));
                mb.Material = (int)MatId.WeaponMetal;
                Barrel(mb, new Vector3(0, 3.1f, -2.2f), 0.30f, 3.4f, 12);   // ion cannon
                foreach (var (cx, cz) in new[] { (-2.4f, 3.4f), (2.4f, 3.4f), (-2.4f, -3.4f), (2.4f, -3.4f) })
                    mb.AddBox(new Vector3(cx, 2.5f, cz), new Vector3(0.42f, 0.35f, 0.42f));
                mb.Material = (int)MatId.TechPanelDark;
                mb.AddBox(new Vector3(-3.0f, 0.8f, 0f), new Vector3(0.42f, 0.8f, 4.8f));
                mb.AddBox(new Vector3(3.0f, 0.8f, 0f), new Vector3(0.42f, 0.8f, 4.8f));
                break;

            case VehicleKind.Paladin:
                mb.Material = (int)MatId.ArmorPlate;
                mb.AddBox(new Vector3(0, 1.15f, 0), new Vector3(1.75f, 0.55f, 2.8f));
                mb.Material = (int)MatId.WeaponMetal;
                mb.AddBox(new Vector3(0, 1.95f, 0.2f), new Vector3(1.0f, 0.45f, 1.2f));
                Barrel(mb, new Vector3(0, 1.95f, -1.6f), 0.22f, 1.8f);
                mb.Material = (int)MatId.EnergyPanel;   // shield emitters
                mb.AddBox(new Vector3(-1.1f, 2.1f, -1.0f), new Vector3(0.12f, 0.5f, 0.12f));
                mb.AddBox(new Vector3(1.1f, 2.1f, -1.0f), new Vector3(0.12f, 0.5f, 0.12f));
                mb.Material = (int)MatId.TechPanelDark;
                mb.AddBox(new Vector3(-1.85f, 0.65f, 0f), new Vector3(0.34f, 0.62f, 2.8f));
                mb.AddBox(new Vector3(1.85f, 0.65f, 0f), new Vector3(0.34f, 0.62f, 2.8f));
                break;

            case VehicleKind.Spma:
                mb.Material = (int)MatId.ArmorPlate;
                mb.AddBox(new Vector3(0, 1.05f, 0), new Vector3(1.35f, 0.45f, 2.8f));
                mb.Material = (int)MatId.WeaponMetal;
                mb.AddBox(new Vector3(0, 1.7f, 0.4f), new Vector3(0.75f, 0.36f, 1.0f));
                // Steeply elevated: this one lobs rather than aims flat.
                mb.PushTransform(Matrix4x4.CreateRotationX(-0.9f) * Matrix4x4.CreateTranslation(new Vector3(0, 2.3f, -0.6f)));
                mb.AddCylinder(Vector3.Zero, 0.17f, 0.17f, 2.8f, 10);
                mb.PopTransform();
                Wheels(mb, 1.4f, 1.9f, 0.62f);
                break;

            case VehicleKind.Manta:
                mb.Material = (int)MatId.ArmorPlate;
                mb.AddBox(new Vector3(0, 0.6f, 0), new Vector3(0.75f, 0.25f, 1.6f));
                mb.Material = (int)MatId.SkyMetal;   // the broad delta wings
                mb.AddBox(new Vector3(-1.25f, 0.6f, 0.1f), new Vector3(0.75f, 0.10f, 1.2f));
                mb.AddBox(new Vector3(1.25f, 0.6f, 0.1f), new Vector3(0.75f, 0.10f, 1.2f));
                mb.Material = (int)MatId.EnergyPanel;   // fans
                foreach (float sx in new[] { -1.2f, 1.2f })
                    mb.AddCylinder(new Vector3(sx, 0.42f, 0.2f), 0.55f, 0.55f, 0.10f, 12);
                mb.Material = (int)MatId.Glass;
                mb.AddBox(new Vector3(0, 0.95f, 0.4f), new Vector3(0.42f, 0.22f, 0.6f));
                break;

            case VehicleKind.Raptor:
                mb.Material = (int)MatId.ArmorPlate;
                mb.AddBox(new Vector3(0, 0.9f, 0), new Vector3(0.7f, 0.35f, 2.2f));
                mb.Material = (int)MatId.SkyMetal;
                mb.AddBox(new Vector3(-1.5f, 0.9f, -0.2f), new Vector3(0.95f, 0.10f, 0.85f));
                mb.AddBox(new Vector3(1.5f, 0.9f, -0.2f), new Vector3(0.95f, 0.10f, 0.85f));
                mb.AddBox(new Vector3(0, 1.35f, 1.4f), new Vector3(0.18f, 0.45f, 0.5f));  // tail fin
                mb.Material = (int)MatId.EnergyPanel;
                mb.AddBox(new Vector3(-1.5f, 0.72f, 0.5f), new Vector3(0.28f, 0.14f, 0.4f));
                mb.AddBox(new Vector3(1.5f, 0.72f, 0.5f), new Vector3(0.28f, 0.14f, 0.4f));
                mb.Material = (int)MatId.Glass;
                mb.AddBox(new Vector3(0, 1.2f, -0.8f), new Vector3(0.4f, 0.26f, 0.7f));
                break;

            case VehicleKind.Cicada:
                mb.Material = (int)MatId.ArmorPlate;
                mb.AddBox(new Vector3(0, 1.05f, 0), new Vector3(1.1f, 0.5f, 2.4f));
                mb.Material = (int)MatId.SkyMetal;
                mb.AddBox(new Vector3(-1.9f, 1.15f, 0f), new Vector3(0.9f, 0.12f, 1.1f));
                mb.AddBox(new Vector3(1.9f, 1.15f, 0f), new Vector3(0.9f, 0.12f, 1.1f));
                mb.Material = (int)MatId.WeaponMetal;   // missile pods
                mb.AddBox(new Vector3(-1.9f, 0.85f, -0.2f), new Vector3(0.4f, 0.2f, 0.9f));
                mb.AddBox(new Vector3(1.9f, 0.85f, -0.2f), new Vector3(0.4f, 0.2f, 0.9f));
                mb.AddBox(new Vector3(0, 0.5f, -0.4f), new Vector3(0.42f, 0.3f, 0.5f));   // belly turret
                break;

            case VehicleKind.IonTank:
                mb.Material = (int)MatId.ArmorPlate;
                mb.AddBox(new Vector3(0, 1.2f, 0), new Vector3(1.8f, 0.6f, 3.2f));
                mb.Material = (int)MatId.EnergyPanel;
                Barrel(mb, new Vector3(0, 2.2f, -2.2f), 0.26f, 3.0f, 12);
                mb.Material = (int)MatId.WeaponMetal;
                mb.AddBox(new Vector3(0, 2.1f, 0.4f), new Vector3(1.1f, 0.5f, 1.4f));
                mb.Material = (int)MatId.TechPanelDark;
                mb.AddBox(new Vector3(-1.9f, 0.7f, 0f), new Vector3(0.35f, 0.7f, 3.2f));
                mb.AddBox(new Vector3(1.9f, 0.7f, 0f), new Vector3(0.35f, 0.7f, 3.2f));
                break;

            // ---------------------------------------------------------------- Necris
            // Darker, spikier, and built around blades rather than plate.

            case VehicleKind.Viper:
                mb.Material = (int)MatId.TechPanelDark;
                mb.AddBox(new Vector3(0, 0.55f, 0), new Vector3(0.42f, 0.22f, 1.7f));
                mb.Material = (int)MatId.Trim;
                mb.AddBox(new Vector3(-0.85f, 0.55f, 0.1f), new Vector3(0.5f, 0.07f, 0.85f));
                mb.AddBox(new Vector3(0.85f, 0.55f, 0.1f), new Vector3(0.5f, 0.07f, 0.85f));
                mb.Material = (int)MatId.EnergyPanel;
                mb.AddBox(new Vector3(0, 0.4f, 1.0f), new Vector3(0.2f, 0.08f, 0.4f));
                break;

            case VehicleKind.Scavenger:
                mb.Material = (int)MatId.EnergyPanel;
                mb.AddCylinder(new Vector3(0, 1.6f, 0), 0.8f, 0.8f, 1.0f, 12);
                mb.Material = (int)MatId.TechPanelDark;
                mb.AddBox(new Vector3(0, 1.6f, 0), new Vector3(0.62f, 0.62f, 0.62f));
                Legs(mb, 1.35f, 1.6f, 3);
                break;

            case VehicleKind.Nemesis:
                mb.Material = (int)MatId.ArmorPlate;
                mb.AddBox(new Vector3(0, 1.3f, 0), new Vector3(1.35f, 0.55f, 2.1f));
                mb.Material = (int)MatId.WeaponMetal;
                mb.AddBox(new Vector3(0, 2.2f, 0.2f), new Vector3(0.75f, 0.4f, 0.9f));
                Barrel(mb, new Vector3(0, 2.2f, -1.5f), 0.16f, 1.8f);
                Legs(mb, 1.4f, 1.3f, 4);
                break;

            case VehicleKind.Nightshade:
                mb.Material = (int)MatId.TechPanelDark;
                mb.AddBox(new Vector3(0, 0.8f, 0), new Vector3(1.1f, 0.34f, 2.2f));
                mb.Material = (int)MatId.Trim;
                mb.AddBox(new Vector3(0, 1.15f, -0.3f), new Vector3(0.62f, 0.22f, 1.0f));
                mb.Material = (int)MatId.EnergyPanel;
                foreach (float sx in new[] { -0.95f, 0.95f })
                    mb.AddCylinder(new Vector3(sx, 0.55f, 0.3f), 0.34f, 0.34f, 0.14f, 10);
                break;

            case VehicleKind.Fury:
                mb.Material = (int)MatId.TechPanelDark;
                mb.AddBox(new Vector3(0, 0.8f, 0), new Vector3(0.55f, 0.3f, 2.0f));
                mb.Material = (int)MatId.Trim;   // swept blade wings
                mb.AddBox(new Vector3(-1.5f, 0.8f, 0.3f), new Vector3(1.0f, 0.08f, 0.7f));
                mb.AddBox(new Vector3(1.5f, 0.8f, 0.3f), new Vector3(1.0f, 0.08f, 0.7f));
                mb.Material = (int)MatId.EnergyPanel;
                mb.AddBox(new Vector3(0, 0.8f, 1.4f), new Vector3(0.28f, 0.16f, 0.5f));
                break;

            case VehicleKind.Darkwalker:
                // The signature is height: a body on tall legs, well above everything else.
                mb.Material = (int)MatId.ArmorPlate;
                mb.AddBox(new Vector3(0, 4.6f, 0), new Vector3(1.5f, 0.85f, 2.0f));
                mb.Material = (int)MatId.WeaponMetal;
                mb.AddBox(new Vector3(0, 4.0f, -1.4f), new Vector3(0.85f, 0.42f, 0.85f));
                Barrel(mb, new Vector3(-0.5f, 4.0f, -2.6f), 0.16f, 1.6f);
                Barrel(mb, new Vector3(0.5f, 4.0f, -2.6f), 0.16f, 1.6f);
                mb.Material = (int)MatId.EnergyPanel;
                mb.AddBox(new Vector3(0, 5.4f, 0.4f), new Vector3(0.5f, 0.2f, 0.5f));
                Legs(mb, 2.3f, 4.2f, 3);
                break;

            case VehicleKind.Hoverboard:
                mb.Material = (int)MatId.Trim;
                mb.AddBox(new Vector3(0, 0.42f, 0), new Vector3(0.36f, 0.06f, 1.15f));
                mb.Material = (int)MatId.EnergyPanel;
                mb.AddBox(new Vector3(0, 0.32f, 0.7f), new Vector3(0.24f, 0.04f, 0.28f));
                mb.AddBox(new Vector3(0, 0.32f, -0.7f), new Vector3(0.24f, 0.04f, 0.28f));
                break;
        }
    }

    public void Dispose()
    {
        foreach (var m in _meshes) m?.Dispose();
    }
}
