using System.Numerics;
using Silk.NET.OpenGL;
using Unreal99.Core;
using Unreal99.Rendering;

namespace Unreal99.Game;

/// <summary>A respawning item sitting in the world.</summary>
public sealed class PickupEntity
{
    public PickupKind Kind;
    public WeaponKind Weapon = WeaponKind.Count;
    public AmmoKind Ammo = AmmoKind.None;
    /// <summary>Everything a <see cref="PickupKind.WeaponLocker"/> hands out at once.</summary>
    public WeaponKind[] LockerWeapons = [];
    public Vector3 Position;
    public float RespawnTime = 20f;
    public float Timer;
    public bool Active = true;
    public float Phase;
    /// <summary>Respawn animation: items scale up as they return.</summary>
    public float SpawnBlend = 1f;

    public float PickupRadius => Kind switch
    {
        // A locker is a rack you walk up to, not a floating pickup — it needs a wider reach.
        PickupKind.WeaponLocker => 1.9f,
        PickupKind.WeaponPickup => 1.35f,
        PickupKind.SuperHealth or PickupKind.ShieldBelt => 1.25f,
        _ => 1.15f,
    };

    public Vector3 GlowColor => Kind switch
    {
        PickupKind.HealthVial => new Vector3(0.25f, 0.85f, 1.0f),
        PickupKind.HealthPack => new Vector3(0.30f, 1.0f, 0.55f),
        PickupKind.SuperHealth => new Vector3(0.20f, 1.0f, 0.85f),
        PickupKind.ThighPads => new Vector3(1.0f, 0.85f, 0.35f),
        PickupKind.BodyArmor => new Vector3(1.0f, 0.70f, 0.25f),
        PickupKind.ShieldBelt => new Vector3(1.0f, 0.35f, 0.90f),
        PickupKind.DamageAmp => new Vector3(1.0f, 0.25f, 0.20f),
        PickupKind.Invisibility => new Vector3(0.55f, 0.75f, 1.0f),
        PickupKind.JumpBoots => new Vector3(0.45f, 1.0f, 0.35f),
        PickupKind.AmmoPickup => new Vector3(0.85f, 0.80f, 0.55f),
        _ => new Vector3(0.6f, 0.85f, 1.0f),
    };

    /// <summary>How badly a bot wants this item right now, given its current state.</summary>
    public float DesireFor(Pawn p)
    {
        if (!Active) return 0f;
        switch (Kind)
        {
            case PickupKind.HealthVial:
                return p.Health < 100f ? 0.35f : 0.05f;
            case PickupKind.HealthPack:
                return p.Health < 90f ? 1.1f * (1f - p.Health / 100f) + 0.3f : 0.05f;
            case PickupKind.SuperHealth:
                return p.Health < 190f ? 1.8f : 0.1f;
            case PickupKind.ThighPads:
                return p.Armor < 100f ? 0.7f : 0.05f;
            case PickupKind.BodyArmor:
                return p.Armor < 130f ? 1.3f : 0.05f;
            case PickupKind.ShieldBelt:
                return p.HasShieldBelt ? 0.1f : 2.4f;
            case PickupKind.DamageAmp:
                return p.HasDamageAmp ? 0.1f : 2.2f;
            case PickupKind.Invisibility:
                return p.IsInvisible ? 0.1f : 1.5f;
            case PickupKind.JumpBoots:
                return p.JumpBootCharges > 0 ? 0.1f : 0.7f;
            case PickupKind.WeaponPickup:
                {
                    var def = Weapons.Get(Weapon);
                    if (!p.HasWeapon[(int)Weapon]) return 0.6f + def.BotPreference * 0.9f;
                    return p.AmmoFor(Weapon) < def.MaxAmmo / 3 ? 0.5f + def.BotPreference * 0.3f : 0.12f;
                }
            case PickupKind.AmmoPickup:
                {
                    if (Ammo == AmmoKind.None) return 0.1f;
                    bool ownsMatchingWeapon = false;
                    for (int i = 0; i < (int)WeaponKind.Count; i++)
                    {
                        if (p.HasWeapon[i] && Weapons.All[i].Ammo == Ammo)
                        {
                            ownsMatchingWeapon = true;
                            break;
                        }
                    }
                    if (!ownsMatchingWeapon) return 0.02f;
                    int max = Pawn.MaxAmmoFor(Ammo);
                    float have = p.Ammo[(int)Ammo] / (float)MathF.Max(1, max);
                    return have <= 0f ? 1.4f : have < 0.5f ? 0.7f * (1f - have) : 0.05f;
                }
            case PickupKind.WeaponLocker:
                {
                    // Worth exactly as much as the best thing on it that the bot does not have —
                    // and worth nothing at all once it holds every gun on the rack at healthy
                    // ammo. A residual floor here reads as a permanently mildly-interesting
                    // destination, so a bot with a full inventory parks at the rack and waits
                    // out its respawn instead of going back to the objective.
                    float best = 0f;
                    foreach (WeaponKind w in LockerWeapons)
                    {
                        var def = Weapons.Get(w);
                        float value = !p.HasWeapon[(int)w]
                            ? 0.7f + def.BotPreference * 0.9f
                            : p.AmmoFor(w) < def.MaxAmmo / 3 ? 0.5f + def.BotPreference * 0.3f : 0f;
                        best = MathF.Max(best, value);
                    }
                    return best;
                }
            default:
                return 0.2f;
        }
    }
}

/// <summary>Procedural meshes for every pickup type, plus the CTF flag.</summary>
public sealed class PickupModels : IDisposable
{
    private readonly Mesh[] _meshes = new Mesh[(int)PickupKind.Count];
    private readonly MeshSection[][] _sections = new MeshSection[(int)PickupKind.Count][];
    public Mesh Flag { get; }
    public MeshSection[] FlagSections { get; }
    public Mesh ObjectiveBeacon { get; }
    public MeshSection[] ObjectiveBeaconSections { get; }
    public Mesh AmmoBox { get; }
    public MeshSection[] AmmoBoxSections { get; }

    public Mesh MeshFor(PickupKind k) => _meshes[(int)k];
    public MeshSection[] SectionsFor(PickupKind k) => _sections[(int)k];

    public PickupModels(GL gl)
    {
        Build(gl, PickupKind.HealthVial, mb =>
        {
            mb.Material = (int)MatId.Glass;
            mb.AddCylinder(new Vector3(0, 0.10f, 0), 0.075f, 0.075f, 0.20f, 10);
            mb.Material = (int)MatId.EnergyPanel;
            mb.AddCylinder(new Vector3(0, 0.085f, 0), 0.058f, 0.058f, 0.14f, 10);
            mb.Material = (int)MatId.Trim;
            mb.AddCylinder(new Vector3(0, 0.215f, 0), 0.048f, 0.042f, 0.045f, 10);
        });

        Build(gl, PickupKind.HealthPack, mb =>
        {
            mb.Material = (int)MatId.ArmorPlate;
            mb.AddBox(new Vector3(0, 0.14f, 0), new Vector3(0.20f, 0.13f, 0.14f));
            mb.Material = (int)MatId.EnergyPanel;
            mb.AddBox(new Vector3(0, 0.14f, -0.145f), new Vector3(0.13f, 0.038f, 0.012f));
            mb.AddBox(new Vector3(0, 0.14f, -0.145f), new Vector3(0.038f, 0.09f, 0.012f));
            mb.AddBox(new Vector3(0, 0.14f, 0.145f), new Vector3(0.13f, 0.038f, 0.012f));
            mb.AddBox(new Vector3(0, 0.14f, 0.145f), new Vector3(0.038f, 0.09f, 0.012f));
            mb.Material = (int)MatId.Trim;
            mb.AddBox(new Vector3(0, 0.275f, 0), new Vector3(0.21f, 0.018f, 0.15f));
        });

        Build(gl, PickupKind.SuperHealth, mb =>
        {
            mb.Material = (int)MatId.Glass;
            mb.AddCylinder(new Vector3(0, 0.30f, 0), 0.24f, 0.24f, 0.52f, 14);
            mb.Material = (int)MatId.EnergyPanel;
            mb.AddCylinder(new Vector3(0, 0.29f, 0), 0.20f, 0.20f, 0.44f, 14);
            mb.Material = (int)MatId.Trim;
            mb.AddCylinder(new Vector3(0, 0.05f, 0), 0.27f, 0.27f, 0.08f, 14);
            mb.AddCylinder(new Vector3(0, 0.57f, 0), 0.27f, 0.20f, 0.09f, 14);
            mb.AddTorus(new Vector3(0, 0.30f, 0), 0.245f, 0.022f, 16, 6);
        });

        Build(gl, PickupKind.ThighPads, mb =>
        {
            mb.Material = (int)MatId.ArmorPlate;
            mb.AddBox(new Vector3(-0.11f, 0.16f, 0), new Vector3(0.075f, 0.14f, 0.10f));
            mb.AddBox(new Vector3(0.11f, 0.16f, 0), new Vector3(0.075f, 0.14f, 0.10f));
            mb.Material = (int)MatId.Trim;
            mb.AddBox(new Vector3(0, 0.16f, 0), new Vector3(0.05f, 0.045f, 0.075f));
        });

        Build(gl, PickupKind.BodyArmor, mb =>
        {
            mb.Material = (int)MatId.ArmorPlate;
            mb.AddBox(new Vector3(0, 0.28f, 0), new Vector3(0.21f, 0.22f, 0.12f));
            mb.AddBox(new Vector3(0.20f, 0.34f, 0), new Vector3(0.055f, 0.10f, 0.10f));
            mb.AddBox(new Vector3(-0.20f, 0.34f, 0), new Vector3(0.055f, 0.10f, 0.10f));
            mb.Material = (int)MatId.Trim;
            mb.AddBox(new Vector3(0, 0.32f, -0.125f), new Vector3(0.10f, 0.09f, 0.02f));
            mb.Material = (int)MatId.EnergyPanel;
            mb.AddBox(new Vector3(0, 0.42f, -0.13f), new Vector3(0.06f, 0.022f, 0.015f));
        });

        Build(gl, PickupKind.ShieldBelt, mb =>
        {
            mb.Material = (int)MatId.Trim;
            mb.AddTorus(new Vector3(0, 0.22f, 0), 0.24f, 0.045f, 20, 8);
            mb.Material = (int)MatId.EnergyPanel;
            mb.AddBox(new Vector3(0, 0.22f, -0.24f), new Vector3(0.09f, 0.075f, 0.035f));
            mb.AddTorus(new Vector3(0, 0.22f, 0), 0.245f, 0.016f, 20, 6);
            mb.Material = (int)MatId.ArmorPlate;
            for (int i = 0; i < 6; i++)
            {
                float a = i / 6f * MathX.TwoPi;
                mb.AddBox(new Vector3(MathF.Cos(a) * 0.24f, 0.22f, MathF.Sin(a) * 0.24f),
                    new Vector3(0.045f, 0.055f, 0.045f));
            }
        });

        Build(gl, PickupKind.DamageAmp, mb =>
        {
            mb.Material = (int)MatId.Trim;
            mb.AddCylinder(new Vector3(0, 0.06f, 0), 0.13f, 0.10f, 0.10f, 10);
            mb.Material = (int)MatId.Lava;
            mb.AddSphere(new Vector3(0, 0.30f, 0), 0.16f, 10, 14);
            mb.Material = (int)MatId.Trim;
            mb.AddTorus(new Vector3(0, 0.30f, 0), 0.19f, 0.018f, 16, 6);
            mb.AddBox(new Vector3(0, 0.16f, 0), new Vector3(0.035f, 0.10f, 0.035f));
        });

        Build(gl, PickupKind.Invisibility, mb =>
        {
            mb.Material = (int)MatId.Glass;
            mb.AddPrism(new Vector3(0, 0.28f, 0), 0.17f, 0.46f, 6);
            mb.Material = (int)MatId.EnergyPanel;
            mb.AddPrism(new Vector3(0, 0.28f, 0), 0.09f, 0.34f, 6, 0.4f);
            mb.Material = (int)MatId.Trim;
            mb.AddPrism(new Vector3(0, 0.045f, 0), 0.20f, 0.09f, 6);
        });

        Build(gl, PickupKind.JumpBoots, mb =>
        {
            mb.Material = (int)MatId.ArmorPlate;
            mb.AddBox(new Vector3(-0.10f, 0.10f, 0), new Vector3(0.07f, 0.09f, 0.15f));
            mb.AddBox(new Vector3(0.10f, 0.10f, 0), new Vector3(0.07f, 0.09f, 0.15f));
            mb.Material = (int)MatId.EnergyPanel;
            mb.AddBox(new Vector3(-0.10f, 0.02f, 0), new Vector3(0.075f, 0.022f, 0.155f));
            mb.AddBox(new Vector3(0.10f, 0.02f, 0), new Vector3(0.075f, 0.022f, 0.155f));
            mb.Material = (int)MatId.Trim;
            mb.AddBox(new Vector3(-0.10f, 0.21f, 0.03f), new Vector3(0.075f, 0.032f, 0.12f));
            mb.AddBox(new Vector3(0.10f, 0.21f, 0.03f), new Vector3(0.075f, 0.032f, 0.12f));
        });

        // Weapon pickups render the weapon mesh itself; this is the pedestal ring under it.
        Build(gl, PickupKind.WeaponPickup, mb =>
        {
            mb.Material = (int)MatId.Trim;
            mb.AddTorus(new Vector3(0, 0.03f, 0), 0.34f, 0.025f, 20, 6);
        });

        // A weapon locker: a floor-standing rack with a lit back panel and empty slots. It has to
        // read as furniture rather than as a floating item, because that is what it is — you walk
        // up to it and take everything, rather than running over one gun.
        Build(gl, PickupKind.WeaponLocker, mb =>
        {
            mb.Material = (int)MatId.TechPanelDark;
            mb.AddBox(new Vector3(0f, 0.06f, 0f), new Vector3(0.62f, 0.06f, 0.24f));
            mb.AddBox(new Vector3(0f, 0.80f, 0.18f), new Vector3(0.62f, 0.80f, 0.06f));
            mb.Material = (int)MatId.Trim;
            for (int i = -1; i <= 1; i += 2)
                mb.AddBox(new Vector3(i * 0.58f, 0.80f, 0f), new Vector3(0.045f, 0.80f, 0.22f));
            mb.AddBox(new Vector3(0f, 1.58f, 0f), new Vector3(0.62f, 0.05f, 0.24f));
            // Four empty slots across the back panel.
            mb.Material = (int)MatId.EnergyPanel;
            for (int i = 0; i < 4; i++)
                mb.AddBox(new Vector3(-0.42f + i * 0.28f, 0.82f, 0.11f),
                    new Vector3(0.085f, 0.56f, 0.012f));
        });

        Build(gl, PickupKind.AmmoPickup, mb =>
        {
            mb.Material = (int)MatId.RustMetal;
            mb.AddBox(new Vector3(0, 0.11f, 0), new Vector3(0.17f, 0.10f, 0.12f));
            mb.Material = (int)MatId.Trim;
            mb.AddBox(new Vector3(0, 0.22f, 0), new Vector3(0.175f, 0.018f, 0.125f));
            mb.Material = (int)MatId.EnergyPanel;
            mb.AddBox(new Vector3(0, 0.11f, -0.125f), new Vector3(0.10f, 0.03f, 0.012f));
        });

        {
            var mb = new MeshBuilder { WorldUv = false, Material = (int)MatId.Trim };
            mb.AddCylinder(new Vector3(0, 0.80f, 0), 0.045f, 0.035f, 1.6f, 8);
            mb.AddCylinder(new Vector3(0, 0.03f, 0), 0.20f, 0.16f, 0.07f, 12);
            mb.Material = (int)MatId.EnergyPanel;
            // Cloth: a shallow wave so the banner reads as fabric even without simulation.
            for (int i = 0; i < 6; i++)
            {
                float t0 = i / 6f, t1 = (i + 1) / 6f;
                float z0 = MathF.Sin(t0 * 5f) * 0.045f, z1 = MathF.Sin(t1 * 5f) * 0.045f;
                Span<Vector3> quad =
                [
                    new(0.05f + t0 * 0.70f, 1.55f, z0),
                    new(0.05f + t1 * 0.70f, 1.55f, z1),
                    new(0.05f + t1 * 0.70f, 1.00f, z1),
                    new(0.05f + t0 * 0.70f, 1.00f, z0),
                ];
                mb.AddPolygon(quad);
                Span<Vector3> back =
                [
                    new(0.05f + t0 * 0.70f, 1.00f, z0),
                    new(0.05f + t1 * 0.70f, 1.00f, z1),
                    new(0.05f + t1 * 0.70f, 1.55f, z1),
                    new(0.05f + t0 * 0.70f, 1.55f, z0),
                ];
                mb.AddPolygon(back);
            }
            mb.RecalculateTangents();
            var (v, i2, s) = mb.Build();
            Flag = Mesh.CreateStatic<Vertex>(gl, v, i2, VertexLayouts.Static);
            FlagSections = s;
        }

        {
            var mb = new MeshBuilder { WorldUv = false, Material = (int)MatId.RustMetal };
            mb.AddBox(new Vector3(0, 0.11f, 0), new Vector3(0.17f, 0.10f, 0.12f));
            mb.RecalculateTangents();
            var (v, i3, s) = mb.Build();
            AmmoBox = Mesh.CreateStatic<Vertex>(gl, v, i3, VertexLayouts.Static);
            AmmoBoxSections = s;
        }

        {
            // Assault objectives need a neutral beacon rather than a flag: using the CTF cloth
            // mesh made a yellow banner appear to float in mid-air above the live target.
            var mb = new MeshBuilder { WorldUv = false, Material = (int)MatId.EnergyPanel };
            // This is deliberately a free-floating holographic glyph, not a pole or banner:
            // objective labels already explain the action, while this gives the player a clear
            // world-space landmark without looking like a misplaced third CTF flag.
            mb.AddPrism(Vector3.Zero, 0.34f, 0.50f, 6, MathX.Pi / 6f);
            mb.Material = (int)MatId.Trim;
            mb.AddTorus(Vector3.Zero, 0.47f, 0.05f, 20, 6);
            mb.RecalculateTangents();
            var (v, i4, sections) = mb.Build();
            ObjectiveBeacon = Mesh.CreateStatic<Vertex>(gl, v, i4, VertexLayouts.Static);
            ObjectiveBeaconSections = sections;
        }
    }

    private void Build(GL gl, PickupKind kind, Action<MeshBuilder> build)
    {
        var mb = new MeshBuilder { WorldUv = false, Material = (int)MatId.ArmorPlate };
        build(mb);
        mb.RecalculateTangents();
        var (v, i, s) = mb.Build();
        _meshes[(int)kind] = Mesh.CreateStatic<Vertex>(gl, v, i, VertexLayouts.Static);
        _sections[(int)kind] = s;
    }

    public void Dispose()
    {
        foreach (var m in _meshes) m?.Dispose();
        Flag?.Dispose();
        AmmoBox?.Dispose();
        ObjectiveBeacon?.Dispose();
    }
}
