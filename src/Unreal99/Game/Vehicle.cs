using System.Numerics;
using Unreal99.Core;
using Unreal99.World;

namespace Unreal99.Game;

/// <summary>
/// A live vehicle. Occupancy is by pawn id rather than by reference so a vehicle survives its
/// crew dying, and so save/restore can rebuild the seating without object graphs.
/// </summary>
public sealed class Vehicle
{
    public int Id;
    public VehicleKind Kind;
    public VehicleDef Def => VehicleDef.Get(Kind);
    /// <summary>Whoever last drove it. Neutral until someone gets in; it does not reset on exit.</summary>
    public Team Team = Team.None;

    public Vector3 Position;
    public Vector3 Velocity;
    public float Yaw;
    /// <summary>Hull pitch. Wheeled and walker vehicles take it from the ground they stand on.</summary>
    public float Pitch;
    public float Roll;

    public float Health;
    public bool Alive = true;
    public float RespawnTimer;
    /// <summary>Where it was placed, so a destroyed vehicle comes back where it belongs.</summary>
    public Vector3 SpawnPosition;
    public float SpawnYaw;

    /// <summary>Pawn id per seat, or -1. Index 0 is always the driver.</summary>
    public int[] Occupants = [];
    /// <summary>Independent turret aim per seat, in world yaw/pitch.</summary>
    public float[] SeatYaw = [];
    public float[] SeatPitch = [];
    public float[] SeatCooldown = [];

    // --- special states, each driven by a def flag ---
    /// <summary>0 = stowed, 1 = fully deployed. Moves over DeploySeconds and blocks movement.</summary>
    public float Deploy;
    public bool Deploying;
    public float ShieldHealth;
    public bool ShieldUp;
    public float CloakBlend;
    public float SelfDestructTimer = -1f;

    public bool Occupied
    {
        get { foreach (int o in Occupants) if (o >= 0) return true; return false; }
    }
    public int Driver => Occupants.Length > 0 ? Occupants[0] : -1;
    /// <summary>Deployed or deploying vehicles cannot drive — that is the whole trade.</summary>
    public bool Immobile => Deploy > 0.001f || Deploying;

    public void Reset()
    {
        Position = SpawnPosition;
        Yaw = SpawnYaw;
        Pitch = Roll = 0f;
        Velocity = Vector3.Zero;
        Health = Def.Health;
        Alive = true;
        Team = Team.None;
        Deploy = 0f; Deploying = false;
        ShieldHealth = 0f; ShieldUp = false;
        CloakBlend = 0f; SelfDestructTimer = -1f;
        for (int i = 0; i < Occupants.Length; i++) { Occupants[i] = -1; SeatYaw[i] = Yaw; SeatPitch[i] = 0f; SeatCooldown[i] = 0f; }
    }

    public void Configure(VehicleKind kind, Vector3 position, float yawDegrees)
    {
        Kind = kind;
        SpawnPosition = position;
        SpawnYaw = yawDegrees * MathX.Deg2Rad;
        int seats = VehicleDef.Get(kind).SeatCount;
        Occupants = new int[seats];
        SeatYaw = new float[seats];
        SeatPitch = new float[seats];
        SeatCooldown = new float[seats];
        Reset();
    }

    public int FreeSeat()
    {
        for (int i = 0; i < Occupants.Length; i++) if (Occupants[i] < 0) return i;
        return -1;
    }

    public Vector3 SeatWorld(int seat)
    {
        var def = Def;
        if (seat < 0 || seat >= def.Seats.Length) return Position;
        Vector3 o = def.Seats[seat].Offset;
        Matrix4x4 rot = Matrix4x4.CreateRotationY(Yaw);
        return Position + Vector3.Transform(o, rot);
    }

    /// <summary>Where a player standing next to it ends up when they get out.</summary>
    public Vector3 ExitPosition(CollisionWorld world)
    {
        var def = Def;
        float side = def.HalfExtents.X + Physics.PawnRadius + 0.6f;
        Matrix4x4 rot = Matrix4x4.CreateRotationY(Yaw);
        foreach (float dx in new[] { -side, side })
        {
            Vector3 candidate = Position + Vector3.Transform(new Vector3(dx, 0.4f, 0f), rot);
            Vector3 half = new(Physics.PawnRadius, Physics.PawnHeight * 0.5f, Physics.PawnRadius);
            Vector3 centre = candidate + new Vector3(0, Physics.PawnHeight * 0.5f, 0);
            if (!world.BoxOverlapsSolid(centre - half, centre + half, new List<int>(8)))
                return candidate;
        }
        return Position + new Vector3(0f, def.HalfExtents.Y + 1.2f, 0f);
    }

    /// <summary>
    /// One movement step. The four motion types differ enough that they are solved separately
    /// rather than through a shared "apply gravity then slide" path — a Raptor that fell to the
    /// ground when idle, or a Goliath that floated, would be wrong in opposite directions.
    /// </summary>
    public void Move(Level level, Vector2 input, bool wantUp, bool wantDown, float dt)
    {
        var def = Def;
        if (Immobile) { Velocity = Vector3.Zero; return; }

        float gravity = Physics.Gravity * level.GravityScale;
        Yaw = MathX.WrapAngle(Yaw - input.X * def.TurnRate * dt);
        Vector3 forward = new(MathF.Sin(Yaw), 0f, MathF.Cos(Yaw));

        switch (def.Motion)
        {
            case VehicleMotion.Air:
            {
                // Full 3D: thrust along the facing, climb and dive on demand, and no gravity —
                // an aircraft that sinks whenever the pilot stops accelerating is a glider.
                Vector3 wish = forward * input.Y * def.MaxSpeed;
                float climb = (wantUp ? 1f : 0f) - (wantDown ? 1f : 0f);
                wish.Y = climb * def.MaxSpeed * 0.55f;
                Velocity = Vector3.Lerp(Velocity, wish, MathX.Saturate(def.Acceleration * dt / MathF.Max(def.MaxSpeed, 1f)));
                Position += Velocity * dt;
                // Keep it off the terrain without letting it land.
                float ground = GroundHeight(level, Position);
                float floor = ground + def.HalfExtents.Y + 1.2f;
                if (Position.Y < floor) { Position.Y = floor; if (Velocity.Y < 0f) Velocity.Y = 0f; }
                Pitch = MathX.Damp(Pitch, -input.Y * 0.18f, 6f, dt);
                Roll = MathX.Damp(Roll, input.X * 0.35f, 6f, dt);
                break;
            }

            case VehicleMotion.Hover:
            {
                // Floats at a fixed height over whatever is below, so it crosses gaps a wheel
                // cannot. Gravity still applies when there is nothing under it to hover over.
                Vector3 wish = forward * input.Y * def.MaxSpeed;
                Velocity = new Vector3(
                    MathX.Damp(Velocity.X, wish.X, def.Acceleration * 0.35f, dt),
                    Velocity.Y,
                    MathX.Damp(Velocity.Z, wish.Z, def.Acceleration * 0.35f, dt));

                float ground = GroundHeight(level, Position);
                float target = ground + def.HoverHeight;
                float gap = target - Position.Y;
                if (gap > -6f)
                {
                    // Spring toward the hover height, damped so it does not oscillate.
                    Velocity.Y += MathX.Clamp(gap * 26f, -34f, 34f) * dt;
                    Velocity.Y *= MathF.Exp(-5.5f * dt);
                    if (wantUp) Velocity.Y += 22f * dt;
                }
                else Velocity.Y -= gravity * dt;

                Position += Velocity * dt;
                Roll = MathX.Damp(Roll, input.X * 0.45f, 7f, dt);
                Pitch = MathX.Damp(Pitch, -input.Y * 0.12f, 7f, dt);
                break;
            }

            default:
            {
                // Wheeled and walker: hug the surface, fall when unsupported. A walker simply
                // steps over more, so it gets a taller step allowance.
                float step = def.Motion == VehicleMotion.Walker ? 2.6f : 0.9f;
                Vector3 wish = forward * input.Y * def.MaxSpeed;
                Velocity = new Vector3(
                    MathX.Damp(Velocity.X, wish.X, def.Acceleration * 0.4f, dt),
                    Velocity.Y - gravity * dt,
                    MathX.Damp(Velocity.Z, wish.Z, def.Acceleration * 0.4f, dt));
                Position += Velocity * dt;

                float ground = GroundHeight(level, Position);
                float rest = ground + def.HalfExtents.Y;
                if (Position.Y <= rest + step)
                {
                    Position.Y = rest;
                    if (Velocity.Y < 0f) Velocity.Y = 0f;
                }
                Roll = MathX.Damp(Roll, input.X * 0.22f, 8f, dt);
                break;
            }
        }

        // Horizontal walls stop everything, including aircraft — the arena has edges.
        ResolveWalls(level, def);
    }

    private void ResolveWalls(Level level, VehicleDef def)
    {
        var scratch = new List<int>(16);
        Vector3 half = def.HalfExtents;
        // Test each axis on its own so a glancing hit slides instead of stopping dead.
        foreach (int axis in new[] { 0, 2 })
        {
            Vector3 probe = Position;
            if (!level.Collision.BoxOverlapsSolid(probe - half, probe + half, scratch)) break;
            float push = axis == 0 ? Velocity.X : Velocity.Z;
            if (MathF.Abs(push) < 0.001f) continue;
            for (int i = 0; i < 6; i++)
            {
                if (axis == 0) Position.X -= MathF.Sign(push) * 0.25f;
                else Position.Z -= MathF.Sign(push) * 0.25f;
                if (!level.Collision.BoxOverlapsSolid(Position - half, Position + half, scratch)) break;
            }
            if (axis == 0) Velocity.X = 0f; else Velocity.Z = 0f;
        }
    }

    private static float GroundHeight(Level level, Vector3 at)
    {
        Vector3 from = at + new Vector3(0f, 4f, 0f);
        var hit = level.Collision.Raycast(from, from - new Vector3(0f, 400f, 0f));
        return hit.Hit ? hit.Point.Y : level.Min.Y;
    }
}
