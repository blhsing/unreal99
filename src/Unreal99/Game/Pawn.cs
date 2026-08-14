using System.Numerics;
using Unreal99.Core;
using Unreal99.World;

namespace Unreal99.Game;

/// <summary>One frame of intent for a pawn. Players and bots both produce these.</summary>
public struct PawnInput
{
    public Vector2 Move;          // x = strafe right, y = forward, each in [-1,1]
    public float Yaw;
    public float Pitch;
    public bool Jump;
    public bool Crouch;
    public bool Fire;
    public bool AltFire;
    /// <summary>Edge-triggered dodge direction in local space, zero when not dodging.</summary>
    public Vector2 Dodge;
    public int WeaponCycle;       // -1 previous, +1 next
    public int WeaponSelect;      // direct slot, or -1
    /// <summary>Edge-triggered: board the nearest vehicle, or leave the one you are in.</summary>
    public bool UseVehicle;
    /// <summary>Edge-triggered: deploy or stow the hoverboard every player carries.</summary>
    public bool Hoverboard;
    /// <summary>Edge-triggered: move to the next vacant seat of the vehicle already being ridden.</summary>
    public bool SwitchSeat;
    /// <summary>
    /// Bots set this while ordinary walking, combat strafing, or pickup steering. A planned
    /// special-link route clears it as the pawn approaches the authored launcher. Human input
    /// leaves it false, preserving normal physical jump-pad behavior.
    /// </summary>
    public bool AvoidJumpPads;
}

/// <summary>
/// A player or bot character: movement, health, armour, inventory and weapon timing.
/// Firing itself lives in <see cref="GameWorld"/> because it needs to spawn projectiles
/// and resolve damage against other pawns.
/// </summary>
public sealed class Pawn
{
    public int Id;
    public string Name = "";
    public Team Team = Team.None;
    public bool IsBot;
    public int PlayerIndex = -1;       // local split-screen slot, -1 for bots
    public Vector3 AccentColor = Vector3.One;

    // --- transform ---
    public Vector3 Position;           // feet
    public Vector3 Velocity;
    public float Yaw;
    public float Pitch;
    public float ViewRoll;

    // --- state ---
    public bool Alive = true;
    public float Health = 100f;
    public float Armor;
    public float MaxHealth = 100f;
    /// <summary>Ignores damage, lethal hazards and combat knockback while enabled.</summary>
    public bool Invulnerable;
    public float DeathTime;
    public Vector3 DeathImpulseDir;
    public bool Gibbed;

    // --- vehicle occupancy ---
    /// <summary>Vehicle this pawn is aboard, or -1 on foot.</summary>
    public int VehicleId = -1;
    public int VehicleSeat = -1;
    public bool InVehicle => VehicleId >= 0;
    /// <summary>Driving input, forwarded from the controller when this pawn holds seat 0.</summary>
    public Vector2 VehicleDrive;
    public bool VehicleUp;
    public bool VehicleDown;

    /// <summary>Shield Gun alt held down: incoming fire from the front is heavily reduced.</summary>
    public bool ShieldRaised;
    public float ShieldEnergy = 100f;
    public float ShieldRechargeDelay;
    /// <summary>Link Gun team boost: a linked gunner temporarily amplifies this pawn's output.</summary>
    public float LinkBoostTimer;

    // --- hoverboard ---
    /// <summary>
    /// Riding the personal hoverboard. It is a pawn state rather than a vehicle because the rider
    /// is still very much a pawn: they can be shot off it, and they can carry the Warfare orb,
    /// neither of which is true of anyone sitting in a cockpit.
    /// </summary>
    public bool OnHoverboard;
    /// <summary>Vehicle being towed behind via the grapple, or -1.</summary>
    public int GrappleVehicleId = -1;
    /// <summary>Seconds of forced dismount after being knocked off. No board, no weapons.</summary>
    public float HoverboardStun;
    public bool CanRideHoverboard => Alive && !InVehicle && HoverboardStun <= 0f;

    public bool OnGround;
    public bool Crouching;
    public float CrouchBlend;
    public float CurrentHeight = Physics.PawnHeight;
    public Vector3 GroundNormal = MathX.Up;
    public bool InWater;
    public float Breath = Physics.BreathSeconds;

    // --- combat ---
    public readonly bool[] HasWeapon = new bool[(int)WeaponKind.Count];
    public readonly int[] Ammo = new int[(int)AmmoKind.Count];
    public WeaponKind Weapon = WeaponKind.ImpactHammer;
    public WeaponKind PendingWeapon = WeaponKind.Count;
    public float SwitchTimer;
    public float FireCooldown;
    public float ChargeTime;
    public bool ChargingPrimary;
    public float SpinUp;
    public bool FiringBeam;
    public float BeamDamageAccumulator;

    // --- power-ups ---
    public float DamageAmpTime;
    public float InvisibilityTime;
    public int JumpBootCharges;
    public bool HasShieldBelt;

    // --- feel / presentation ---
    public float ViewBobPhase;
    public float StepPhase;
    public float FireBlend;
    public float LandBlend;
    public float DodgeBlend;
    public Vector2 DodgeDirection;
    public float DodgeCooldown;
    public float DamageFlash;
    public float CameraShake;
    public Vector3 ShakeOffset;
    public float ZoomFov;
    public float RespawnTimer;
    public float SpawnProtection;

    // --- scoring ---
    public int Frags;
    public int Deaths;
    public int Suicides;
    public int Captures;
    /// <summary>Fractional personal score earned by control points this pawn captured.</summary>
    public float DominationScore;
    public int FlagCarrierKills;
    public int Streak;
    public int MultiKillCount;
    public float MultiKillTimer;
    public int ShotsFired;
    public int ShotsHit;
    public bool HasFlag;
    public Team CarriedFlag = Team.None;
    /// <summary>Bombing Run: holding the ball, and therefore holding nothing else.</summary>
    public bool HasBall;
    /// <summary>Team-mate selected with Ball Launcher alternate fire; primary passes to them.</summary>
    public int BallPassTargetId = -1;

    public int LastAttackerId = -1;
    public float LastDamageTime;
    /// <summary>Last position at which the pawn was standing on solid ground; used for respawn nudges.</summary>
    public Vector3 LastGroundPosition;

    private float _lastDodgeTapTime;
    private Vector2 _lastDodgeTapDir;

    public Vector3 Center => Position + new Vector3(0, CurrentHeight * 0.5f, 0);
    public Vector3 EyePosition => Position + new Vector3(0, CurrentHeight * Physics.EyeHeightFraction, 0);
    public Vector3 HalfExtents => new(Physics.PawnRadius, CurrentHeight * 0.5f, Physics.PawnRadius);
    public Vector3 ViewDirection => MathX.DirFromYawPitch(Yaw, Pitch);
    public Vector3 ForwardFlat => MathX.SafeNormalize(MathX.DirFromYawPitch(Yaw, 0f), MathX.Forward);
    public Vector3 RightFlat => Vector3.Cross(ForwardFlat, MathX.Up);
    public float Speed => Velocity.Horizontal();
    public bool IsInvisible => InvisibilityTime > 0f;
    public bool HasDamageAmp => DamageAmpTime > 0f;
    // Older saves can contain the former per-pellet hit count. Keep their results screen valid
    // while new attacks use one hit credit per trigger pull in GameWorld.
    public float Accuracy => ShotsFired > 0 ? MathX.Saturate(ShotsHit / (float)ShotsFired) : 0f;

    public WeaponDef WeaponDef => Weapons.Get(Weapon);

    public int AmmoFor(WeaponKind w)
    {
        var def = Weapons.Get(w);
        return def.Ammo == AmmoKind.None ? 999 : Ammo[(int)def.Ammo];
    }

    public bool CanFire(WeaponKind w, bool alt)
    {
        var def = Weapons.Get(w);
        int cost = alt ? def.Alt.AmmoCost : def.Primary.AmmoCost;
        if (def.Ammo == AmmoKind.None) return true;
        return Ammo[(int)def.Ammo] >= Math.Max(1, cost);
    }

    public void ConsumeAmmo(bool alt)
    {
        var def = WeaponDef;
        if (def.Ammo == AmmoKind.None) return;
        int cost = Math.Max(1, alt ? def.Alt.AmmoCost : def.Primary.AmmoCost);
        Ammo[(int)def.Ammo] = Math.Max(0, Ammo[(int)def.Ammo] - cost);
    }

    public bool GiveAmmo(AmmoKind kind, int amount)
    {
        if (kind == AmmoKind.None || amount <= 0) return false;
        int max = MaxAmmoFor(kind);
        if (Ammo[(int)kind] >= max) return false;
        Ammo[(int)kind] = Math.Min(max, Ammo[(int)kind] + amount);
        return true;
    }

    public static int MaxAmmoFor(AmmoKind kind)
    {
        for (int i = 0; i < (int)WeaponKind.Count; i++)
            if (Weapons.All[i].Ammo == kind) return Weapons.All[i].MaxAmmo;
        return 99;
    }

    public bool GiveWeapon(WeaponKind w, bool autoSwitch = true)
    {
        var def = Weapons.Get(w);
        bool isNew = !HasWeapon[(int)w];
        HasWeapon[(int)w] = true;
        bool gotAmmo = GiveAmmo(def.Ammo, def.PickupAmmo);
        if (isNew && def.Ammo != AmmoKind.None && Ammo[(int)def.Ammo] < def.PickupAmmo)
            GiveAmmo(def.Ammo, def.PickupAmmo);

        // Auto-switch when the new weapon is a clear upgrade and we are not mid-fight with a better one.
        if (isNew && autoSwitch && def.BotPreference > Weapons.Get(Weapon).BotPreference)
            RequestWeapon(w);
        return isNew || gotAmmo;
    }

    public void RequestWeapon(WeaponKind w)
    {
        if (w == Weapon && PendingWeapon == WeaponKind.Count) return;
        if (!HasWeapon[(int)w]) return;
        if (AmmoFor(w) <= 0 && Weapons.Get(w).Ammo != AmmoKind.None) return;
        PendingWeapon = w;
        SwitchTimer = Weapons.Get(Weapon).SwitchTime;
    }

    public void CycleWeapon(int direction)
    {
        var order = Weapons.CycleOrder;
        int current = Array.IndexOf(order, PendingWeapon != WeaponKind.Count ? PendingWeapon : Weapon);
        if (current < 0) current = 0;
        for (int i = 1; i <= order.Length; i++)
        {
            int idx = ((current + direction * i) % order.Length + order.Length) % order.Length;
            WeaponKind w = order[idx];
            if (!HasWeapon[(int)w]) continue;
            if (Weapons.Get(w).Ammo != AmmoKind.None && AmmoFor(w) <= 0) continue;
            RequestWeapon(w);
            return;
        }
    }

    /// <summary>Falls back to the best weapon that still has ammo — used when one runs dry.</summary>
    public void SwitchToBestAvailable()
    {
        WeaponKind best = WeaponKind.ImpactHammer;
        float bestScore = -1f;
        for (int i = 0; i < (int)WeaponKind.Count; i++)
        {
            var w = (WeaponKind)i;
            if (!HasWeapon[i]) continue;
            var def = Weapons.Get(w);
            if (def.Ammo != AmmoKind.None && AmmoFor(w) <= 0) continue;
            if (def.BotPreference > bestScore) { bestScore = def.BotPreference; best = w; }
        }
        RequestWeapon(best);
    }

    public void ResetForSpawn(Vector3 position, float yaw, IEnumerable<WeaponKind> loadout, bool instagib)
    {
        Position = position;
        LastGroundPosition = position;
        Velocity = Vector3.Zero;
        Yaw = yaw;
        Pitch = 0f;
        ViewRoll = 0f;
        Alive = true;
        Gibbed = false;
        Invulnerable = false;
        Health = MaxHealth;
        Armor = 0f;
        HasShieldBelt = false;
        DeathTime = 0f;
        OnGround = false;
        Crouching = false;
        CrouchBlend = 0f;
        CurrentHeight = Physics.PawnHeight;
        DamageAmpTime = 0f;
        InvisibilityTime = 0f;
        JumpBootCharges = 0;
        Breath = Physics.BreathSeconds;
        DamageFlash = 0f;
        CameraShake = 0f;
        FireCooldown = 0f;
        ChargeTime = 0f;
        SpinUp = 0f;
        FiringBeam = false;
        ZoomFov = 0f;
        Streak = 0;
        SpawnProtection = 1.6f;
        HasFlag = false;
        CarriedFlag = Team.None;
        HasBall = false;
        BallPassTargetId = -1;
        ShieldRaised = false;
        ShieldEnergy = 100f;
        ShieldRechargeDelay = 0f;
        LinkBoostTimer = 0f;
        OnHoverboard = false;
        GrappleVehicleId = -1;
        HoverboardStun = 0f;

        Array.Clear(HasWeapon);
        Array.Clear(Ammo);
        if (instagib)
        {
            HasWeapon[(int)WeaponKind.ShockRifle] = true;
            Ammo[(int)AmmoKind.ShockCore] = 99;
            Weapon = WeaponKind.ShockRifle;
        }
        else
        {
            foreach (var w in loadout)
            {
                HasWeapon[(int)w] = true;
                var def = Weapons.Get(w);
                if (def.Ammo != AmmoKind.None)
                    Ammo[(int)def.Ammo] = Math.Max(Ammo[(int)def.Ammo], def.StartingAmmo > 0 ? def.StartingAmmo : def.PickupAmmo);
            }
            Weapon = WeaponKind.Enforcer;
            if (!HasWeapon[(int)Weapon]) Weapon = WeaponKind.ImpactHammer;
        }
        PendingWeapon = WeaponKind.Count;
        SwitchTimer = 0f;
    }

    // ---------------------------------------------------------------- movement

    /// <summary>
    /// Integrates one movement step. Returns events the caller needs to react to
    /// (landing sounds, jump-pad launches, teleports).
    /// </summary>
    public MoveEvents Move(Level level, in PawnInput input, float dt)
    {
        MoveEvents events = default;
        var world = level.Collision;

        Yaw = input.Yaw;
        Pitch = MathX.Clamp(input.Pitch, -1.50f, 1.50f);

        // --- crouch transition, blocked if there is no room to stand ---
        bool wantCrouch = input.Crouch && OnGround;
        if (!wantCrouch && Crouching)
        {
            Vector3 standCenter = Position + new Vector3(0, Physics.PawnHeight * 0.5f, 0);
            Vector3 standHalf = new(Physics.PawnRadius, Physics.PawnHeight * 0.5f - 0.01f, Physics.PawnRadius);
            if (world.BoxOverlapsSolid(standCenter - standHalf, standCenter + standHalf)) wantCrouch = true;
        }
        Crouching = wantCrouch;
        CrouchBlend = MathX.MoveToward(CrouchBlend, Crouching ? 1f : 0f, Physics.CrouchTransitionSpeed * dt);
        CurrentHeight = MathX.Lerp(Physics.PawnHeight, Physics.PawnCrouchHeight, CrouchBlend);

        // --- water state ---
        Vector3 halfNow = HalfExtents;
        Vector3 centerNow = Center;
        var volume = world.VolumeAt(centerNow - halfNow, centerNow + halfNow);
        InWater = volume == BrushKind.Water;
        if (volume == BrushKind.Void) events.EnteredVoid = true;

        // --- desired direction ---
        Vector3 wish = ForwardFlat * input.Move.Y + RightFlat * input.Move.X;
        float wishLen = wish.Length();
        Vector3 wishDir = wishLen > 1e-4f ? wish / wishLen : Vector3.Zero;
        float wishSpeed = MathF.Min(wishLen, 1f)
            * (OnHoverboard ? Physics.HoverboardSpeed : Physics.GroundSpeed);
        if (Crouching) wishSpeed *= Physics.CrouchSpeedScale;

        if (InWater)
        {
            Velocity = Physics.ApplyFriction(Velocity, Physics.WaterFriction, dt);
            Vector3 swimDir = MathX.SafeNormalize(
                ForwardFlat * input.Move.Y + RightFlat * input.Move.X + MathX.Up * (input.Jump ? 1f : 0f),
                Vector3.Zero);
            Velocity = Physics.Accelerate(Velocity, swimDir, Physics.WaterSpeed, Physics.WaterAcceleration, dt);
            Velocity.Y += (Physics.WaterBuoyancy - Physics.Gravity) * dt;
            Velocity.Y = MathX.Clamp(Velocity.Y, -6f, 6f);
            Breath -= dt;
            if (Breath < 0f) events.Drowning = true;
        }
        else
        {
            Breath = MathF.Min(Physics.BreathSeconds, Breath + dt * 4f);

            if (OnGround)
            {
                // The board glides: barely any friction and a slow build-up, so it is fast in a
                // straight line and clumsy in a fight — which is the trade the original makes.
                float friction = OnHoverboard
                    ? Physics.HoverboardFriction
                    : Physics.GroundFriction + (DodgeBlend > 0.4f ? Physics.DodgeLandFriction : 0f);
                float acceleration = OnHoverboard
                    ? Physics.HoverboardAcceleration : Physics.GroundAcceleration;
                Velocity = Physics.ApplyFriction(Velocity, friction, dt);
                Velocity = Physics.Accelerate(Velocity, wishDir, wishSpeed, acceleration, dt);
            }
            else
            {
                // Air control is deliberately generous — it is what makes UT movement expressive.
                float airWish = MathF.Min(wishSpeed, Physics.MaxAirSpeed);
                Velocity = Physics.Accelerate(Velocity, wishDir, airWish,
                    Physics.AirAcceleration * Physics.AirControl, dt);
            }
            Velocity.Y -= Physics.Gravity * level.GravityScale * dt;
            if (Velocity.Y < -Physics.TerminalVelocity) Velocity.Y = -Physics.TerminalVelocity;
        }

        // --- jump ---
        if (input.Jump && OnGround && !InWater)
        {
            float jumpVel = JumpBootCharges > 0 ? Physics.JumpBootsVelocity : Physics.JumpVelocity;
            if (JumpBootCharges > 0) { JumpBootCharges--; events.UsedJumpBoots = true; }
            Velocity.Y = jumpVel;
            OnGround = false;
            events.Jumped = true;
        }
        else if (input.Jump && InWater)
        {
            Velocity.Y = MathF.Max(Velocity.Y, Physics.WaterJumpVelocity);
        }

        // --- dodge ---
        DodgeCooldown = MathF.Max(0f, DodgeCooldown - dt);
        if (input.Dodge != Vector2.Zero && DodgeCooldown <= 0f && OnGround && !InWater)
        {
            Vector2 d = MathX.SafeNormalize(input.Dodge, Vector2.Zero);
            Vector3 dir = RightFlat * d.X + ForwardFlat * d.Y;
            dir = MathX.SafeNormalize(dir, ForwardFlat);
            Velocity = dir * Physics.DodgeSpeed + new Vector3(0, Physics.DodgeVertical, 0);
            DodgeCooldown = Physics.DodgeCooldown;
            DodgeBlend = 1f;
            DodgeDirection = d;
            OnGround = false;
            events.Dodged = true;
        }

        // --- integrate ---
        Vector3 half = HalfExtents;
        Vector3 center = Position + new Vector3(0, CurrentHeight * 0.5f, 0);
        var result = world.MoveBox(center, half, Velocity, dt, initiallyGrounded: OnGround);
        Position = result.Position - new Vector3(0, CurrentHeight * 0.5f, 0);
        Velocity = result.Velocity;
        bool wasOnGround = OnGround;
        OnGround = result.OnGround;
        GroundNormal = result.GroundNormal;

        if (OnGround)
        {
            LastGroundPosition = Position;
            // Ride any mover we are standing on.
            if (result.GroundBrush >= 0)
            {
                foreach (var m in level.Movers)
                {
                    if (m.BrushIndex != result.GroundBrush) continue;
                    Position += m.Velocity * dt;
                    break;
                }
            }
        }

        if (!wasOnGround && OnGround && result.LandingSpeed > 1.5f)
        {
            events.Landed = true;
            events.LandingSpeed = result.LandingSpeed;
            LandBlend = MathX.Saturate(result.LandingSpeed / 14f);
            // Water breaks a fall. Without this a dive off a ship's rail into the harbour is
            // lethal, which is neither what the genre does nor what any player expects.
            // The board absorbs a chunk of the impact too, which is how a hoverboard run gets to
            // take the shortcut down the cliff — but a long enough drop still ends with the rider
            // on the floor rather than gliding away.
            float impact = OnHoverboard
                ? MathF.Max(0f, result.LandingSpeed - Physics.HoverboardFallAbsorb)
                : result.LandingSpeed;
            float fall = InWater ? 0f : Physics.FallDamage(impact);
            if (fall > 0f) events.FallDamage = fall;
            if (OnHoverboard && fall > 0f) events.KnockedOffBoard = true;
        }

        if (result.HitCeiling) Velocity.Y = MathF.Min(Velocity.Y, 0f);

        // --- volumes ---
        half = HalfExtents;
        center = Center;
        if (world.TouchingLava(center - half, center + half)) events.InLava = true;
        if (Position.Y < level.KillPlaneY + 2f) events.EnteredVoid = true;

        // --- jump pads ---
        foreach (var pad in level.JumpPads)
        {
            if (input.AvoidJumpPads) break;
            Vector3 pmin = pad.Position - pad.HalfExtents;
            Vector3 pmax = pad.Position + pad.HalfExtents + new Vector3(0, 1.4f, 0);
            if (center.X + half.X < pmin.X || center.X - half.X > pmax.X) continue;
            if (center.Z + half.Z < pmin.Z || center.Z - half.Z > pmax.Z) continue;
            if (Position.Y > pmax.Y || Position.Y + CurrentHeight < pmin.Y) continue;
            Velocity = pad.LaunchVelocity;
            OnGround = false;
            events.JumpPad = true;
            events.JumpPadColor = pad.Color;
            break;
        }

        // --- teleporters ---
        foreach (var tp in level.Teleporters)
        {
            Vector3 tmin = tp.Position - tp.HalfExtents;
            Vector3 tmax = tp.Position + tp.HalfExtents;
            if (center.X + half.X < tmin.X || center.X - half.X > tmax.X) continue;
            if (center.Z + half.Z < tmin.Z || center.Z - half.Z > tmax.Z) continue;
            if (center.Y + half.Y < tmin.Y || center.Y - half.Y > tmax.Y) continue;
            events.Teleported = true;
            events.TeleportFrom = Position;
            Position = tp.Destination;
            Yaw = tp.DestinationYaw;
            Velocity = MathX.DirFromYawPitch(Yaw, 0f) * MathF.Max(Velocity.Horizontal(), 6f);
            break;
        }

        // --- presentation timers ---
        float speed01 = MathX.Saturate(Speed / Physics.GroundSpeed);
        if (OnGround)
        {
            ViewBobPhase += dt * (6.5f + speed01 * 6.5f);
            float prev = StepPhase;
            StepPhase += dt * speed01 * 2.4f;
            if (MathF.Floor(StepPhase) > MathF.Floor(prev) && speed01 > 0.25f) events.Footstep = true;
        }
        TickPresentation(dt);

        // Roll the view slightly when strafing; a small touch that makes movement feel physical.
        float targetRoll = -input.Move.X * 0.035f - DodgeDirection.X * DodgeBlend * 0.09f;
        ViewRoll = MathX.Damp(ViewRoll, targetRoll, 9f, dt);

        return events;
    }

    /// <summary>
    /// Decays the cosmetic timers. Runs while dead as well as alive, otherwise a fatal hit
    /// would freeze the damage flash on screen for the whole respawn.
    /// </summary>
    public void TickPresentation(float dt)
    {
        DodgeBlend = MathF.Max(0f, DodgeBlend - dt * 3.0f);
        LandBlend = MathF.Max(0f, LandBlend - dt * 4.0f);
        FireBlend = MathF.Max(0f, FireBlend - dt * 7.0f);
        DamageFlash = MathF.Max(0f, DamageFlash - dt * 2.6f);
        CameraShake = MathF.Max(0f, CameraShake - dt * 3.4f);
        SpawnProtection = MathF.Max(0f, SpawnProtection - dt);
        DamageAmpTime = MathF.Max(0f, DamageAmpTime - dt);
        InvisibilityTime = MathF.Max(0f, InvisibilityTime - dt);
        MultiKillTimer = MathF.Max(0f, MultiKillTimer - dt);
        if (MultiKillTimer <= 0f) MultiKillCount = 0;
    }

    /// <summary>Records a double-tap in a direction and returns the dodge vector if one triggered.</summary>
    public Vector2 RegisterDodgeTap(Vector2 direction, float time)
    {
        if (direction == Vector2.Zero) return Vector2.Zero;
        bool sameDirection = Vector2.Dot(_lastDodgeTapDir, direction) > 0.85f;
        if (sameDirection && time - _lastDodgeTapTime < Physics.DoubleTapWindow)
        {
            _lastDodgeTapTime = -10f;
            _lastDodgeTapDir = Vector2.Zero;
            return direction;
        }
        _lastDodgeTapTime = time;
        _lastDodgeTapDir = direction;
        return Vector2.Zero;
    }

    /// <summary>Advances weapon switch and cooldown timers. Returns true when a switch completes.</summary>
    public bool UpdateWeaponTimers(float dt)
    {
        FireCooldown = MathF.Max(0f, FireCooldown - dt);
        bool switched = false;
        if (PendingWeapon != WeaponKind.Count)
        {
            SwitchTimer -= dt;
            if (SwitchTimer <= 0f)
            {
                Weapon = PendingWeapon;
                PendingWeapon = WeaponKind.Count;
                SwitchTimer = 0f;
                ChargeTime = 0f;
                SpinUp = 0f;
                switched = true;
            }
        }
        return switched;
    }

    public bool IsSwitching => PendingWeapon != WeaponKind.Count;

    /// <summary>Applies damage after armour, returning the health actually lost.</summary>
    public float ApplyDamage(float amount, DamageType type)
    {
        if (amount <= 0f || !Alive) return 0f;
        if (SpawnProtection > 0f && type is not (DamageType.Lava or DamageType.Void or DamageType.Fall))
            amount *= 0.35f;

        float toHealth = amount;
        if (Armor > 0f && type != DamageType.Drowning && type != DamageType.Void)
        {
            float absorbFraction = HasShieldBelt ? 0.85f : 0.65f;
            float absorbed = MathF.Min(Armor, amount * absorbFraction);
            Armor -= absorbed;
            if (Armor <= 0.01f) { Armor = 0f; HasShieldBelt = false; }
            toHealth = amount - absorbed;
        }

        Health -= toHealth;
        DamageFlash = MathF.Min(1f, DamageFlash + MathX.Saturate(amount / 55f) * 0.85f);
        CameraShake = MathF.Min(1.4f, CameraShake + MathX.Saturate(amount / 70f));
        return toHealth;
    }

    public void GiveHealth(float amount, float max)
    {
        if (Health >= max) return;
        Health = MathF.Min(max, Health + amount);
    }

    public void GiveArmor(float amount, float max, bool shieldBelt = false)
    {
        Armor = MathF.Min(max, Armor + amount);
        if (shieldBelt) HasShieldBelt = true;
    }

    /// <summary>Where a shot from this pawn originates: the eye, nudged toward the weapon.</summary>
    public Vector3 MuzzleWorld()
    {
        var def = WeaponDef;
        Vector3 dir = ViewDirection;
        Vector3 right = MathX.SafeNormalize(Vector3.Cross(dir, MathX.Up), RightFlat);
        Vector3 up = Vector3.Cross(right, dir);
        return EyePosition + dir * MathF.Abs(def.MuzzleLocal.Z) * 0.55f + right * 0.16f - up * 0.09f;
    }
}

/// <summary>Everything the world needs to react to after a pawn's movement step.</summary>
public struct MoveEvents
{
    public bool Jumped;
    public bool Dodged;
    public bool Landed;
    public float LandingSpeed;
    public float FallDamage;
    public bool Footstep;
    public bool InLava;
    public bool Drowning;
    public bool EnteredVoid;
    public bool JumpPad;
    public Vector3 JumpPadColor;
    public bool UsedJumpBoots;
    public bool Teleported;
    public Vector3 TeleportFrom;
    /// <summary>The landing was hard enough to throw the rider off the hoverboard.</summary>
    public bool KnockedOffBoard;
}
