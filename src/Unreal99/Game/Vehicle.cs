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
    /// <summary>Current lock team. A fresh pad assigns it; a driver's exit unlocks it.</summary>
    public Team Team = Team.None;
    /// <summary>Freshly spawned ownership; parked team vehicles unlock after their driver exits.</summary>
    public Team SpawnTeam = Team.None;
    public Team AuthoredSpawnTeam = Team.None;
    /// <summary>Nearest Onslaught node controlling this pad, or -1 for a base/Assault pad.</summary>
    public int SpawnNodeIndex = -1;
    public float SpawnRespawnSeconds = 30f;

    public Vector3 Position;
    public Vector3 Velocity;
    public float Yaw;
    /// <summary>
    /// How far the hull turned on the last movement tick. The crew's view is carried round by
    /// this, so steering points everyone aboard where the vehicle is now heading instead of
    /// leaving them staring at the direction they happened to board facing.
    /// </summary>
    public float YawDelta;
    /// <summary>Hull pitch. Wheeled and walker vehicles take it from the ground they stand on.</summary>
    public float Pitch;
    public float Roll;
    /// <summary>Last collision result for a ground vehicle; keeps step-up available frame to frame.</summary>
    public bool OnGround;

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
        OnGround = false;
        Velocity = Vector3.Zero;
        Health = Def.Health;
        Alive = true;
        Team = SpawnTeam;
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

    /// <summary>
    /// The next vacant seat after <paramref name="from"/>, wrapping round. Returns −1 when the
    /// occupant is the only one who could sit anywhere, so a single-seat vehicle and a full one
    /// both simply refuse rather than pretending to move somebody.
    /// </summary>
    public int NextFreeSeatAfter(int from)
    {
        for (int step = 1; step < Occupants.Length; step++)
        {
            int seat = (from + step) % Occupants.Length;
            if (Occupants[seat] < 0) return seat;
        }
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
    public void Move(Level level, Vector2 input, bool wantUp, bool wantDown, float dt,
        bool hasDriver = true)
    {
        var def = Def;
        if (Immobile) { Velocity = Vector3.Zero; YawDelta = 0f; return; }

        float gravity = Physics.Gravity * level.GravityScale;
        float steer = -input.X * def.TurnRate * dt;
        YawDelta = steer;
        Yaw = MathX.WrapAngle(Yaw + steer);
        Vector3 forward = new(MathF.Sin(Yaw), 0f, MathF.Cos(Yaw));

        switch (def.Motion)
        {
            case VehicleMotion.Air:
            {
                if (hasDriver)
                {
                    // Full 3D while crewed: thrust along the facing and hold altitude unless the
                    // pilot explicitly climbs or dives.
                    Vector3 wish = forward * input.Y * def.MaxSpeed;
                    float climb = (wantUp ? 1f : 0f) - (wantDown ? 1f : 0f);
                    wish.Y = climb * def.MaxSpeed * 0.55f;
                    Velocity = Vector3.Lerp(Velocity, wish,
                        MathX.Saturate(def.Acceleration * dt / MathF.Max(def.MaxSpeed, 1f)));
                }
                else
                {
                    // Once the pilot is gone an aircraft is no longer an immortal platform in
                    // the sky. Bleed its horizontal momentum and descend under controlled
                    // gravity until the collision solver puts the hull on the terrain.
                    Velocity.X *= MathF.Exp(-1.2f * dt);
                    Velocity.Z *= MathF.Exp(-1.2f * dt);
                    Velocity.Y = MathF.Max(Velocity.Y - gravity * 0.62f * dt, -24f);
                }

                MoveResult air = level.Collision.MoveBox(Position, def.HalfExtents, Velocity, dt,
                    stepUp: false, initiallyGrounded: OnGround);
                Position = air.Position;
                Velocity = air.Velocity;
                OnGround = air.OnGround;
                Pitch = MathX.Damp(Pitch, hasDriver ? -input.Y * 0.18f : 0f, 6f, dt);
                Roll = MathX.Damp(Roll, hasDriver ? input.X * 0.35f : 0f, 6f, dt);
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

                // Hover craft may climb a low pad instead of treating its vertical lip as an
                // arena wall. High walls still block because they exceed this class-specific
                // step allowance, and the same swept solver prevents tunnelling at full speed.
                MoveResult hover = level.Collision.MoveBox(Position, def.HalfExtents, Velocity, dt,
                    initiallyGrounded: true, stepHeight: MathF.Max(2.4f, def.HoverHeight + 0.8f));
                Position = hover.Position;
                Velocity = hover.Velocity;
                OnGround = hover.OnGround;
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
                MoveResult ground = level.Collision.MoveBox(Position, def.HalfExtents, Velocity, dt,
                    initiallyGrounded: OnGround, stepHeight: step);
                Position = ground.Position;
                Velocity = ground.Velocity;
                OnGround = ground.OnGround;
                Pitch = MathX.Damp(Pitch, MathF.Atan2(-ground.GroundNormal.Z,
                    MathF.Max(ground.GroundNormal.Y, 0.01f)), 7f, dt);
                Roll = MathX.Damp(Roll, input.X * 0.22f, 8f, dt);
                break;
            }
        }
    }

    /// <summary>
    /// Height of the surface under a vehicle. A miss must not report the bottom of the world:
    /// the probe starts just above the hull and a vehicle wedged inside geometry makes the ray
    /// begin inside a brush, which reads as no hit. Answering "the floor of the level" there
    /// teleports the vehicle — and its crew — through the map. Holding station is the safe
    /// answer, because the caller only ever uses this to decide whether to rest or keep falling.
    /// </summary>
    private float GroundHeight(Level level, Vector3 at)
    {
        Vector3 from = at + new Vector3(0f, 4f, 0f);
        var hit = level.Collision.Raycast(from, from - new Vector3(0f, 400f, 0f));
        return hit.Hit ? hit.Point.Y : at.Y - Def.HalfExtents.Y;
    }
}
