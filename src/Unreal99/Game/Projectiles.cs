using System.Numerics;
using Silk.NET.OpenGL;
using Unreal99.Core;
using Unreal99.Rendering;

namespace Unreal99.Game;

/// <summary>An in-flight projectile. Pooled in a flat array inside <see cref="GameWorld"/>.</summary>
public struct Projectile
{
    public bool Active;
    public ProjectileKind Kind;
    public Vector3 Position;
    public Vector3 Velocity;
    public Vector3 Up;              // spin reference for blades and rockets
    public int OwnerId;
    public Team OwnerTeam;

    public float Damage;
    public float SplashRadius;
    public float SplashDamage;
    public float Knockback;
    public float HeadshotMultiplier;
    public float DamageScale;       // damage amplifier at the moment of firing

    public float Life;
    public float MaxLife;
    public float Radius;
    public Vector3 Color;
    public float Spin;

    public int BouncesLeft;
    public bool AffectedByGravity;
    public bool StickOnImpact;
    public bool Stuck;
    public bool ExplodeOnTimeout;
    public bool ComboTarget;        // shock ball: can be detonated by the shock beam
    public float ArmDelay;
    public float TrailTimer;
}

/// <summary>Procedural meshes for the projectiles that are large enough to need real geometry.</summary>
public sealed class ProjectileModels : IDisposable
{
    private readonly Mesh[] _meshes = new Mesh[16];
    private readonly MeshSection[][] _sections = new MeshSection[16][];

    public Mesh MeshFor(ProjectileKind k) => _meshes[(int)k];
    public MeshSection[] SectionsFor(ProjectileKind k) => _sections[(int)k];

    public ProjectileModels(GL gl)
    {
        Build(gl, ProjectileKind.Rocket, mb =>
        {
            mb.Material = (int)MatId.WeaponMetal;
            mb.AddCylinder(Vector3.Zero, 0.075f, 0.075f, 0.36f, 10);
            mb.AddCylinder(new Vector3(0, 0.24f, 0), 0.075f, 0.012f, 0.12f, 10);
            mb.Material = (int)MatId.Trim;
            mb.AddCylinder(new Vector3(0, -0.14f, 0), 0.085f, 0.085f, 0.05f, 10);
            for (int i = 0; i < 4; i++)
            {
                float a = i / 4f * MathX.TwoPi;
                Vector3 d = new(MathF.Cos(a), 0, MathF.Sin(a));
                mb.AddBox(d * 0.10f + new Vector3(0, -0.14f, 0),
                    new Vector3(MathF.Abs(d.X) * 0.05f + 0.008f, 0.05f, MathF.Abs(d.Z) * 0.05f + 0.008f));
            }
            mb.Material = (int)MatId.Lava;
            mb.AddCylinder(new Vector3(0, -0.20f, 0), 0.055f, 0.030f, 0.04f, 10);
        });

        Build(gl, ProjectileKind.Grenade, mb =>
        {
            mb.Material = (int)MatId.RustMetal;
            mb.AddSphere(Vector3.Zero, 0.095f, 8, 12);
            mb.Material = (int)MatId.Trim;
            mb.AddCylinder(new Vector3(0, 0.095f, 0), 0.035f, 0.030f, 0.05f, 8);
            mb.Material = (int)MatId.EnergyPanel;
            mb.AddTorus(Vector3.Zero, 0.098f, 0.014f, 12, 6);
        });

        Build(gl, ProjectileKind.FlakShell, mb =>
        {
            mb.Material = (int)MatId.RustMetal;
            mb.AddCylinder(Vector3.Zero, 0.075f, 0.055f, 0.19f, 8);
            mb.Material = (int)MatId.Lava;
            mb.AddCylinder(new Vector3(0, -0.11f, 0), 0.045f, 0.020f, 0.04f, 8);
        });

        Build(gl, ProjectileKind.RipperBlade, mb =>
        {
            mb.Material = (int)MatId.Trim;
            mb.AddCylinder(Vector3.Zero, 0.135f, 0.135f, 0.016f, 12);
            mb.Material = (int)MatId.EnergyPanel;
            mb.AddTorus(Vector3.Zero, 0.140f, 0.012f, 14, 5);
            for (int i = 0; i < 5; i++)
            {
                float a = i / 5f * MathX.TwoPi;
                Vector3 d = new(MathF.Cos(a), 0, MathF.Sin(a));
                mb.Material = (int)MatId.WeaponMetal;
                mb.AddBox(d * 0.155f, new Vector3(0.032f, 0.011f, 0.032f));
            }
        });

        Build(gl, ProjectileKind.Warhead, mb =>
        {
            mb.Material = (int)MatId.WeaponMetal;
            mb.AddCylinder(Vector3.Zero, 0.16f, 0.16f, 0.62f, 12);
            mb.AddCylinder(new Vector3(0, 0.42f, 0), 0.16f, 0.02f, 0.22f, 12);
            mb.Material = (int)MatId.Trim;
            mb.AddTorus(new Vector3(0, 0.10f, 0), 0.168f, 0.020f, 14, 6);
            mb.AddTorus(new Vector3(0, -0.14f, 0), 0.168f, 0.020f, 14, 6);
            for (int i = 0; i < 4; i++)
            {
                float a = i / 4f * MathX.TwoPi + 0.4f;
                Vector3 d = new(MathF.Cos(a), 0, MathF.Sin(a));
                mb.AddBox(d * 0.21f + new Vector3(0, -0.26f, 0),
                    new Vector3(MathF.Abs(d.X) * 0.07f + 0.012f, 0.09f, MathF.Abs(d.Z) * 0.07f + 0.012f));
            }
            mb.Material = (int)MatId.Lava;
            mb.AddCylinder(new Vector3(0, -0.36f, 0), 0.11f, 0.06f, 0.07f, 12);
        });
    }

    private void Build(GL gl, ProjectileKind kind, Action<MeshBuilder> build)
    {
        var mb = new MeshBuilder { WorldUv = false, Material = (int)MatId.WeaponMetal };
        build(mb);
        mb.RecalculateTangents();
        var (v, i, s) = mb.Build();
        _meshes[(int)kind] = Mesh.CreateStatic<Vertex>(gl, v, i, VertexLayouts.Static);
        _sections[(int)kind] = s;
    }

    public void Dispose()
    {
        foreach (var m in _meshes) m?.Dispose();
    }
}

public static class ProjectileFactory
{
    /// <summary>Fills in the per-kind behaviour for a projectile spawned from <paramref name="fire"/>.</summary>
    public static Projectile Create(ProjectileKind kind, in FireDef fire, Vector3 position, Vector3 direction,
        int ownerId, Team ownerTeam, Vector3 tint, float damageScale, Rng rng)
    {
        var p = new Projectile
        {
            Active = true,
            Kind = kind,
            Position = position,
            Velocity = direction * fire.ProjectileSpeed,
            Up = MathX.Up,
            OwnerId = ownerId,
            OwnerTeam = ownerTeam,
            Damage = fire.Damage,
            SplashRadius = fire.SplashRadius,
            SplashDamage = fire.SplashDamage,
            Knockback = fire.Knockback,
            HeadshotMultiplier = MathF.Max(1f, fire.HeadshotMultiplier),
            DamageScale = damageScale,
            Color = tint,
            ArmDelay = 0.02f,
        };

        switch (kind)
        {
            case ProjectileKind.Rocket:
                p.Radius = 0.14f; p.MaxLife = 6f; p.AffectedByGravity = false; p.Spin = 6f;
                break;
            case ProjectileKind.Grenade:
                p.Radius = 0.12f; p.MaxLife = 2.6f; p.AffectedByGravity = true;
                p.BouncesLeft = 4; p.ExplodeOnTimeout = true; p.Spin = 12f;
                p.Velocity += new Vector3(0, fire.ProjectileSpeed * 0.24f, 0);
                break;
            case ProjectileKind.FlakShell:
                p.Radius = 0.11f; p.MaxLife = 3.2f; p.AffectedByGravity = true;
                p.BouncesLeft = 1; p.ExplodeOnTimeout = true; p.Spin = 9f;
                p.Velocity += new Vector3(0, fire.ProjectileSpeed * 0.20f, 0);
                break;
            case ProjectileKind.FlakShard:
                p.Radius = 0.06f; p.MaxLife = 1.5f; p.AffectedByGravity = true;
                p.BouncesLeft = 2; p.Spin = 22f;
                // Slight speed variance so a blast reads as a spray rather than a wall.
                p.Velocity *= rng.Range(0.82f, 1.18f);
                break;
            case ProjectileKind.BioGlob:
                p.Radius = 0.13f; p.MaxLife = 7f; p.AffectedByGravity = true;
                p.StickOnImpact = true; p.ExplodeOnTimeout = true; p.Spin = 4f;
                p.Velocity += new Vector3(0, fire.ProjectileSpeed * 0.13f, 0);
                break;
            case ProjectileKind.ShockBall:
                p.Radius = 0.24f; p.MaxLife = 5f; p.ComboTarget = true; p.Spin = 3f;
                break;
            case ProjectileKind.PlasmaBolt:
                p.Radius = 0.10f; p.MaxLife = 2.4f; p.Spin = 14f;
                break;
            case ProjectileKind.RipperBlade:
                p.Radius = 0.14f; p.MaxLife = 3.2f; p.BouncesLeft = 5; p.Spin = 34f;
                p.AffectedByGravity = false;
                break;
            case ProjectileKind.Warhead:
                p.Radius = 0.30f; p.MaxLife = 9f; p.Spin = 2f;
                break;
        }
        p.Life = p.MaxLife;
        return p;
    }
}
