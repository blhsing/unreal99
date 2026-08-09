using System.Numerics;
using Unreal99.Core;
using Unreal99.Rendering;
using Unreal99.UI;
using Unreal99.World;

namespace Unreal99.Game;

/// <summary>Produces one frame of input for a pawn. Implemented by the player and bot controllers.</summary>
public abstract class Controller
{
    public Pawn Pawn;
    public abstract PawnInput Update(GameWorld world, float dt);
    public virtual void OnSpawned(GameWorld world) { }
    public virtual void OnDamaged(GameWorld world, Pawn attacker, float amount, Vector3 direction) { }
    public virtual void OnKilled(GameWorld world, Pawn killer) { }
}

/// <summary>Transient on-screen feedback owned by one pawn (only rendered for local players).</summary>
public sealed class Feedback
{
    public string BigText = "";
    public float BigTextTimer;
    public Vector3 BigTextColor = Vector3.One;
    public string SubText = "";
    public float SubTextTimer;
    public string PickupText = "";
    public float PickupTimer;
    public float HitMarkerTimer;
    public bool HitMarkerLethal;
    public float DamageDirection;    // yaw-relative angle of the last hit, radians
    public float DamageDirectionTimer;
    public readonly List<DamageNumberEvent> DamageNumbers = new(6);

    /// <summary>
    /// Adds one fading combat number. Very rapid ticks of the same kind are combined so beam and
    /// minigun fire remain readable instead of filling the viewport with overlapping glyphs.
    /// </summary>
    public void DamageNumber(float amount, bool dealt)
    {
        if (amount <= 0.01f) return;
        const float duration = 1.05f;
        for (int i = DamageNumbers.Count - 1; i >= 0; i--)
        {
            DamageNumberEvent current = DamageNumbers[i];
            if (current.Dealt != dealt || current.Timer < duration - 0.18f) continue;
            current.Amount += amount;
            current.Timer = duration;
            DamageNumbers[i] = current;
            return;
        }
        if (DamageNumbers.Count >= 6) DamageNumbers.RemoveAt(0);
        DamageNumbers.Add(new DamageNumberEvent
        {
            Amount = amount,
            Timer = duration,
            Duration = duration,
            Dealt = dealt,
        });
    }

    public void Big(string text, Vector3 color, float duration = 2.2f)
    {
        BigText = text; BigTextColor = color; BigTextTimer = duration;
    }

    public void Sub(string text, float duration = 2.4f)
    {
        SubText = text; SubTextTimer = duration;
    }

    public void Pickup(string text, float duration = 1.8f)
    {
        PickupText = text; PickupTimer = duration;
    }

    public void Update(float dt)
    {
        BigTextTimer = MathF.Max(0f, BigTextTimer - dt);
        SubTextTimer = MathF.Max(0f, SubTextTimer - dt);
        PickupTimer = MathF.Max(0f, PickupTimer - dt);
        HitMarkerTimer = MathF.Max(0f, HitMarkerTimer - dt);
        DamageDirectionTimer = MathF.Max(0f, DamageDirectionTimer - dt);
        for (int i = DamageNumbers.Count - 1; i >= 0; i--)
        {
            DamageNumberEvent number = DamageNumbers[i];
            number.Timer = MathF.Max(0f, number.Timer - dt);
            if (number.Timer <= 0f) DamageNumbers.RemoveAt(i);
            else DamageNumbers[i] = number;
        }
    }
}

public struct DamageNumberEvent
{
    public float Amount;
    public float Timer;
    public float Duration;
    public bool Dealt;
}

public struct KillFeedEntry
{
    public string Text;
    public float Timer;
    public Vector3 Color;
}

/// <summary>
/// The simulation. Owns every pawn, projectile and pickup, resolves all damage, and builds
/// the per-frame render list. Split-screen views all read from this single world.
/// </summary>
public sealed class GameWorld
{
    public const int MaxProjectiles = 512;

    public Level Level;
    public GameMode Mode;
    public readonly List<Pawn> Pawns = new(16);
    public readonly List<Controller> Controllers = new(16);
    public readonly List<PickupEntity> Pickups = new(96);
    public readonly Projectile[] Projectiles = new Projectile[MaxProjectiles];
    public readonly List<KillFeedEntry> KillFeed = new(8);
    public readonly Dictionary<int, Feedback> Feedbacks = new();

    public readonly Rng Rng = new(0xC0FFEE);
    public float Time;
    public int NextPawnId = 1;

    /// <summary>
    /// Seconds left on a "get ready" hold. While this is running the world is drawn but not
    /// simulated: nobody moves, shoots, respawns or bleeds, and the match clock does not run.
    /// Used after loading a save so the player can see where they are before anything can
    /// shoot them — dropping straight back into a firefight mid-air is not a fair resume.
    /// </summary>
    public float ResumeCountdown;
    public bool Frozen => ResumeCountdown > 0f;
    private int _lastResumeSecond = -1;

    /// <summary>Starts the hold. Use this rather than assigning the field, so the first second gets called out.</summary>
    public void BeginResumeCountdown(float seconds)
    {
        ResumeCountdown = MathF.Max(0f, seconds);
        _lastResumeSecond = -1;
    }

    private readonly Renderer _renderer;
    private readonly CharacterModel _character;
    private readonly WeaponModels _weaponModels;
    private readonly ProjectileModels _projectileModels;
    private readonly PickupModels _pickupModels;
    private readonly VehicleModels _vehicleModels;

    private readonly Dictionary<int, Matrix4x4[]> _boneWorld = new();
    private readonly Dictionary<int, Matrix4x4[]> _boneSkin = new();
    private readonly List<Vector3> _spawnAvoid = new(16);

    // Domination state
    /// <summary>The Onslaught node network. Empty on every other mode.</summary>
    public readonly OnslaughtState Onslaught = new();

    /// <summary>The Assault objective sequence and round bookkeeping. Empty on every other mode.</summary>
    public readonly AssaultState Assault = new();

    public readonly List<Vehicle> Vehicles = new(16);
    public int NextVehicleId = 1;

    public Vehicle FindVehicle(int id)
    {
        foreach (var v in Vehicles) if (v.Id == id) return v;
        return null;
    }

    /// <summary>Nearest boardable vehicle within reach of a pawn on foot, or null.</summary>
    public Vehicle VehicleToBoard(Pawn pawn, float reach = 3.6f)
    {
        Vehicle best = null;
        float bestDist = reach * reach;
        foreach (var v in Vehicles)
        {
            if (!v.Alive || v.FreeSeat() < 0) continue;
            if (v.Team != Team.None && pawn.Team != Team.None && v.Team != pawn.Team) continue;
            float d = Vector3.DistanceSquared(v.Position, pawn.Position);
            float radius = MathF.Max(v.Def.HalfExtents.X, v.Def.HalfExtents.Z) + reach;
            if (d > radius * radius || d > bestDist) continue;
            bestDist = d; best = v;
        }
        return best;
    }

    public bool EnterVehicle(Pawn pawn, Vehicle vehicle)
    {
        if (pawn == null || vehicle == null || !vehicle.Alive || pawn.VehicleId >= 0) return false;
        int seat = vehicle.FreeSeat();
        if (seat < 0) return false;
        vehicle.Occupants[seat] = pawn.Id;
        vehicle.SeatYaw[seat] = vehicle.Yaw;
        vehicle.SeatPitch[seat] = 0f;
        // First one aboard claims it. It stays claimed after they leave, so an abandoned tank
        // does not immediately become a free gift to whoever walks past.
        if (vehicle.Team == Team.None) vehicle.Team = pawn.Team;
        pawn.VehicleId = vehicle.Id;
        pawn.VehicleSeat = seat;
        OnSound?.Invoke(SoundId.Respawn, vehicle.Position, 0.7f);
        return true;
    }

    public void ExitVehicle(Pawn pawn)
    {
        if (pawn == null || pawn.VehicleId < 0) return;
        var v = FindVehicle(pawn.VehicleId);
        if (v != null && pawn.VehicleSeat >= 0 && pawn.VehicleSeat < v.Occupants.Length)
        {
            v.Occupants[pawn.VehicleSeat] = -1;
            pawn.Position = v.ExitPosition(Level.Collision);
            pawn.Velocity = v.Velocity * 0.35f;
        }
        pawn.VehicleId = -1;
        pawn.VehicleSeat = -1;
    }

    /// <summary>Who holds each control point, indexed alongside <see cref="Level.ControlPoints"/>.</summary>
    public readonly List<Team> ControlPointOwners = new();
    /// <summary>Seconds since each point last changed hands, for the capture flash on the HUD.</summary>
    public readonly List<float> ControlPointSince = new();
    /// <summary>Pawn currently receiving the personal hold score for each point.</summary>
    public readonly List<int> ControlPointControllers = new();
    /// <summary>QA telemetry: number of real touch captures made at each point this match.</summary>
    public readonly List<int> ControlPointCaptures = new();
    private HashSet<long> _controlPointContacts = new();
    private HashSet<long> _nextControlPointContacts = new();

    public int ControlPointsHeldBy(Team team)
    {
        int n = 0;
        foreach (var owner in ControlPointOwners) if (owner == team) n++;
        return n;
    }

    public readonly Dictionary<Team, Vector3> FlagHome = new();
    public readonly Dictionary<Team, Vector3> FlagPosition = new();
    public readonly Dictionary<Team, int> FlagCarrier = new();
    public readonly Dictionary<Team, float> FlagDroppedTimer = new();
    /// <summary>QA telemetry for detecting navigation-related environmental deaths.</summary>
    public int VoidDeaths { get; private set; }
    public int FallDeaths { get; private set; }
    public int LavaDeaths { get; private set; }
    public readonly List<string> EnvironmentalDeathDetails = new();

    public ParticleSystem Particles => _renderer.Particles;
    public EffectRenderer Effects => _renderer.Effects;
    public MaterialLibrary Materials => _renderer.Materials;
    public CharacterModel Character => _character;
    public WeaponModels WeaponMeshes => _weaponModels;

    /// <summary>Raised whenever something should make a noise; the audio layer subscribes.</summary>
    public Action<SoundId, Vector3, float> OnSound;

    public GameWorld(Renderer renderer, CharacterModel character, WeaponModels weaponModels,
        ProjectileModels projectileModels, PickupModels pickupModels, VehicleModels vehicleModels)
    {
        _renderer = renderer;
        _character = character;
        _weaponModels = weaponModels;
        _projectileModels = projectileModels;
        _pickupModels = pickupModels;
        _vehicleModels = vehicleModels;
    }

    // ---------------------------------------------------------------- setup

    public void LoadLevel(Level level, GameMode mode)
    {
        Level = level;
        Mode = mode;
        Pawns.Clear();
        Controllers.Clear();
        Pickups.Clear();
        KillFeed.Clear();
        Feedbacks.Clear();
        Array.Clear(Projectiles);
        Particles.Clear();
        Effects.Clear();
        Time = 0f;
        NextPawnId = 1;
        VoidDeaths = 0;
        FallDeaths = 0;
        LavaDeaths = 0;
        EnvironmentalDeathDetails.Clear();

        foreach (var p in level.Pickups)
        {
            Pickups.Add(new PickupEntity
            {
                Kind = p.Kind,
                Weapon = p.Weapon,
                Ammo = p.Ammo,
                Position = p.Position,
                RespawnTime = p.RespawnTime,
                Active = true,
                Phase = Rng.Range(0f, MathX.TwoPi),
            });
        }

        Onslaught.Reset(level);
        Assault.Reset(level);

        Vehicles.Clear();
        NextVehicleId = 1;
        // Vehicles belong to Onslaught and Assault only. The same arena loaded as a deathmatch
        // is meant to be a foot fight, and a Goliath in one would not be a nod to the original —
        // it would be a different game.
        bool vehiclesAllowed = Mode.Kind is GameModeKind.Onslaught or GameModeKind.Assault;
        foreach (var vs in vehiclesAllowed ? level.VehicleSpawns : [])
        {
            var v = new Vehicle { Id = NextVehicleId++ };
            v.Configure(vs.Kind, vs.Position + new Vector3(0f, VehicleDef.Get(vs.Kind).HalfExtents.Y, 0f), vs.Yaw);
            v.Team = vs.Team;
            Vehicles.Add(v);
        }

        ControlPointOwners.Clear();
        ControlPointSince.Clear();
        ControlPointControllers.Clear();
        ControlPointCaptures.Clear();
        _controlPointContacts.Clear();
        _nextControlPointContacts.Clear();
        foreach (var _ in level.ControlPoints)
        {
            ControlPointOwners.Add(Team.None);
            ControlPointSince.Add(99f);
            ControlPointControllers.Add(-1);
            ControlPointCaptures.Add(0);
        }

        FlagHome.Clear(); FlagPosition.Clear(); FlagCarrier.Clear(); FlagDroppedTimer.Clear();
        foreach (var fb in level.FlagBases)
        {
            FlagHome[fb.Team] = fb.Position;
            FlagPosition[fb.Team] = fb.Position;
            FlagCarrier[fb.Team] = -1;
            FlagDroppedTimer[fb.Team] = 0f;
        }

        Particles.RaycastFunc = (from, to) =>
        {
            var hit = Level.Collision.Raycast(from, to);
            return (hit.Hit, hit.Point, hit.Normal);
        };
    }

    public Pawn AddPawn(Controller controller, string name, Team team, bool isBot, int playerIndex,
        Vector3 accentColor)
    {
        var pawn = new Pawn
        {
            Id = NextPawnId++,
            Name = name,
            Team = team,
            IsBot = isBot,
            PlayerIndex = playerIndex,
            AccentColor = accentColor,
        };
        controller.Pawn = pawn;
        Pawns.Add(pawn);
        Controllers.Add(controller);
        Feedbacks[pawn.Id] = new Feedback();
        _boneWorld[pawn.Id] = new Matrix4x4[(int)Bone.Count];
        _boneSkin[pawn.Id] = new Matrix4x4[(int)Bone.Count];
        RespawnPawn(pawn);
        return pawn;
    }

    public Feedback FeedbackFor(Pawn p) => Feedbacks.TryGetValue(p.Id, out var f) ? f : new Feedback();

    public Pawn FindPawn(int id)
    {
        foreach (var p in Pawns) if (p.Id == id) return p;
        return null;
    }

    public Controller ControllerFor(Pawn p)
    {
        for (int i = 0; i < Pawns.Count; i++) if (Pawns[i] == p) return Controllers[i];
        return null;
    }

    public void RespawnPawn(Pawn pawn)
    {
        _spawnAvoid.Clear();
        foreach (var other in Pawns)
            if (other != pawn && other.Alive) _spawnAvoid.Add(other.Position);

        // Assault attackers come in at the furthest spawn group their progress has opened;
        // defenders always start at the back, so their group index stays at zero.
        int assaultGroup = -1;
        if (Mode.Kind == GameModeKind.Assault)
            assaultGroup = pawn.Team == Assault.Attackers ? Assault.SpawnGroup : 0;

        var spawn = Level.PickSpawn(Rng, Mode.TeamBased ? pawn.Team : Team.None, _spawnAvoid, 9f, assaultGroup);
        Vector3 pos = spawn.Position + new Vector3(0, 0.06f, 0);

        // Safety net: if the authored yaw points into a nearby wall, look at the arena instead.
        Vector3 eye = pos + new Vector3(0, Physics.PawnHeight * Physics.EyeHeightFraction, 0);
        Vector3 facing = MathX.DirFromYawPitch(spawn.Yaw, 0f);
        if (Level.Collision.Raycast(eye, eye + facing * 5.0f).Hit)
        {
            Vector3 toCenter = (Level.Center - pos).FlatXZ();
            if (toCenter.LengthSquared() > 0.01f)
                MathX.YawPitchFromDir(Vector3.Normalize(toCenter), out spawn.Yaw, out _);
        }

        // Telefrag anything already standing on the spawn point.
        foreach (var other in Pawns)
        {
            if (other == pawn || !other.Alive) continue;
            if (Vector3.Distance(other.Position, pos) < 1.1f)
                Kill(other, pawn, DamageType.Telefrag);
        }

        pawn.ResetForSpawn(pos, spawn.Yaw, Weapons.StartingWeapons, Mode.Kind == GameModeKind.Instagib);
        pawn.RespawnTimer = 0f;
        Particles.EnergyBurst(pos + new Vector3(0, 1f, 0), pawn.AccentColor, 1.1f);
        OnSound?.Invoke(SoundId.Respawn, pos, 1f);
        ControllerFor(pawn)?.OnSpawned(this);
    }

    // ---------------------------------------------------------------- main update

    public void Update(float dt)
    {
        if (Frozen)
        {
            UpdateResumeCountdown(dt);
            return;
        }

        Time += dt;
        Level.Update(dt, Time);
        Mode.Update(this, dt);

        // The countdown is a true pre-match lock, not live play with scoring disabled. Do not
        // poll controllers, integrate movement, fire weapons, collect pickups, or process flags
        // until GameMode transitions to InProgress. Presentation still ticks so the countdown
        // announcements and idle characters remain animated.
        if (Mode.State == MatchState.Warmup)
        {
            foreach (Pawn pawn in Pawns)
            {
                Feedbacks[pawn.Id].Update(dt);
                pawn.Velocity = Vector3.Zero;
                pawn.TickPresentation(dt);
            }
            UpdatePickups(dt);
            Particles.Update(dt);
            Effects.Update(dt);
            return;
        }

        for (int i = 0; i < Pawns.Count; i++)
        {
            var pawn = Pawns[i];
            var feedback = Feedbacks[pawn.Id];
            feedback.Update(dt);

            if (!pawn.Alive)
            {
                pawn.DeathTime += dt;
                pawn.RespawnTimer -= dt;
                pawn.TickPresentation(dt);
                if (pawn.RespawnTimer <= 0f && Mode.AllowsRespawn(this, pawn)) RespawnPawn(pawn);
                continue;
            }

            PawnInput input = Controllers[i].Update(this, dt);

            // A pawn aboard a vehicle does not walk. UpdateVehicles places it in its seat, and
            // letting Move() also run would have the two fighting over the same position every
            // frame — the rider would jitter and could shove itself through the hull.
            if (pawn.InVehicle)
            {
                // Use dismounts. Held down it would leave and re-board every frame, so it only
                // fires on the press edge.
                if (input.UseVehicle) { ExitVehicle(pawn); continue; }
                pawn.VehicleDrive = input.Move;
                pawn.VehicleUp = input.Jump;
                pawn.VehicleDown = input.Crouch;
                pawn.TickPresentation(dt);
                HandleVehicleFire(pawn, input, dt);
                Mode.OnPawnUpdate(this, pawn, dt);
                continue;
            }

            if (input.UseVehicle)
            {
                var boardable = VehicleToBoard(pawn);
                if (boardable != null && EnterVehicle(pawn, boardable)) continue;
            }

            var events = pawn.Move(Level, input, dt);
            HandleMoveEvents(pawn, events, dt);
            if (!pawn.Alive) continue;

            HandleWeapons(pawn, input, dt);
            HandlePickups(pawn);
            Mode.OnPawnUpdate(this, pawn, dt);
            UpdateCarriedFlag(pawn);
        }

        UpdateProjectiles(dt);
        UpdatePickups(dt);
        UpdateFlags(dt);
        UpdateControlPoints(dt);
        UpdateVehicles(dt);
        UpdateOnslaught(dt);
        UpdateAssault(dt);

        for (int i = KillFeed.Count - 1; i >= 0; i--)
        {
            var e = KillFeed[i];
            e.Timer -= dt;
            if (e.Timer <= 0f) KillFeed.RemoveAt(i);
            else KillFeed[i] = e;
        }

        Particles.Update(dt);
        Effects.Update(dt);
    }

    /// <summary>
    /// Runs while the world is held. Only the announcement counter advances — deliberately not
    /// the particles or the level movers, so the scene the player is looking at is exactly the
    /// one that was saved rather than a lift that has wandered off while they read the screen.
    /// </summary>
    private void UpdateResumeCountdown(float dt)
    {
        float before = ResumeCountdown;
        ResumeCountdown = MathF.Max(0f, ResumeCountdown - dt);

        // Compared against the last second actually announced, not against the previous frame's
        // value: on the opening frame those are the same number, which would swallow the first call.
        int second = (int)MathF.Ceiling(ResumeCountdown);
        if (second != _lastResumeSecond && second is >= 1 and <= 3)
        {
            _lastResumeSecond = second;
            string text = second switch
            {
                3 => Loc.AnnCountdown3,
                2 => Loc.AnnCountdown2,
                _ => Loc.AnnCountdown1,
            };
            Broadcast(text, new Vector3(1f, 0.85f, 0.3f), 0.9f);
            OnSound?.Invoke(SoundId.MenuMove, Vector3.Zero, 0.8f);
        }

        if (ResumeCountdown <= 0f && before > 0f)
        {
            Broadcast(Loc.AnnMatchResume, new Vector3(0.4f, 1f, 0.5f), 1.4f);
            OnSound?.Invoke(SoundId.AnnounceMajor, Vector3.Zero, 1.2f);
        }

        foreach (var f in Feedbacks.Values) f.Update(dt);
    }

    private void HandleMoveEvents(Pawn pawn, in MoveEvents e, float dt)
    {
        if (e.Jumped) OnSound?.Invoke(e.UsedJumpBoots ? SoundId.JumpBoots : SoundId.Jump, pawn.Position, 0.6f);
        if (e.Dodged)
        {
            OnSound?.Invoke(SoundId.Dodge, pawn.Position, 0.7f);
            Particles.Dust(pawn.Position, 0.55f, 6);
        }
        if (e.Footstep)
        {
            OnSound?.Invoke(SoundId.Footstep, pawn.Position, 0.32f);
        }
        if (e.Landed)
        {
            OnSound?.Invoke(SoundId.Land, pawn.Position, MathX.Saturate(e.LandingSpeed / 14f) * 0.8f);
            if (e.LandingSpeed > 8f) Particles.Dust(pawn.Position, 0.7f, 8);
        }
        if (e.FallDamage > 0f)
        {
            Damage(pawn, null, e.FallDamage, DamageType.Fall, MathX.Down);
            if (!pawn.Alive) return;
        }
        if (e.JumpPad)
        {
            OnSound?.Invoke(SoundId.JumpPad, pawn.Position, 0.9f);
            Particles.EnergyBurst(pawn.Position, e.JumpPadColor, 0.9f);
        }
        if (e.Teleported)
        {
            OnSound?.Invoke(SoundId.Teleport, e.TeleportFrom, 0.9f);
            Particles.EnergyBurst(e.TeleportFrom + new Vector3(0, 1f, 0), new Vector3(0.6f, 0.3f, 1f), 1.2f);
            Particles.EnergyBurst(pawn.Position + new Vector3(0, 1f, 0), new Vector3(0.6f, 0.3f, 1f), 1.2f);
        }
        if (e.InLava)
        {
            Damage(pawn, null, Physics.LavaDamagePerSecond * dt, DamageType.Lava, MathX.Up);
            if (Rng.Chance(dt * 12f))
                Particles.Trail(pawn.Position + Rng.InsideUnitSphere() * 0.4f + MathX.Up * 0.3f,
                    new Vector3(1f, 0.4f, 0.1f), 0.35f, 0.5f);
        }
        if (e.Drowning) Damage(pawn, null, Physics.DrownDamagePerSecond * dt, DamageType.Drowning, MathX.Up);
        if (e.EnteredVoid) Kill(pawn, null, DamageType.Void);
    }

    // ---------------------------------------------------------------- weapons

    private void HandleWeapons(Pawn pawn, in PawnInput input, float dt)
    {
        if (input.WeaponSelect >= 0 && input.WeaponSelect < (int)WeaponKind.Count)
            pawn.RequestWeapon((WeaponKind)input.WeaponSelect);
        else if (input.WeaponCycle != 0)
            pawn.CycleWeapon(input.WeaponCycle);

        if (pawn.UpdateWeaponTimers(dt)) OnSound?.Invoke(SoundId.WeaponSwitch, pawn.Position, 0.4f);

        var def = pawn.WeaponDef;
        bool zoomHeld = input.AltFire && def.Alt.ZoomFov > 0f;
        pawn.ZoomFov = zoomHeld ? def.Alt.ZoomFov : 0f;

        // --- minigun spin-up ---
        if (def.SpinUp)
        {
            bool spinning = (input.Fire || input.AltFire) && !pawn.IsSwitching;
            pawn.SpinUp = MathX.Clamp(pawn.SpinUp + (spinning ? dt * 2.6f : -dt * 2.2f), 0f, 1f);
        }
        else pawn.SpinUp = 0f;

        // --- pulse gun beam ---
        if (def.Alt.Mode == FireMode.Beam && input.AltFire && !pawn.IsSwitching && pawn.CanFire(pawn.Weapon, true))
        {
            pawn.FiringBeam = true;
            FireBeam(pawn, def.Alt, dt);
            return;
        }
        pawn.FiringBeam = false;

        if (pawn.IsSwitching) return;

        // --- charged fire (impact hammer primary, bio alt) ---
        bool primaryChargeable = def.Primary.Chargeable;
        bool altChargeable = def.Alt.Chargeable;
        if (primaryChargeable && input.Fire)
        {
            pawn.ChargingPrimary = true;
            pawn.ChargeTime = MathF.Min(def.Primary.MaxCharge, pawn.ChargeTime + dt);
            return;
        }
        if (pawn.ChargingPrimary && !input.Fire)
        {
            pawn.ChargingPrimary = false;
            float charge = pawn.ChargeTime / MathF.Max(def.Primary.MaxCharge, 0.01f);
            pawn.ChargeTime = 0f;
            if (pawn.FireCooldown <= 0f) Fire(pawn, false, 0.55f + charge * 0.85f);
            return;
        }
        if (altChargeable && input.AltFire)
        {
            pawn.ChargeTime = MathF.Min(def.Alt.MaxCharge, pawn.ChargeTime + dt);
            return;
        }
        if (altChargeable && !input.AltFire && pawn.ChargeTime > 0.05f)
        {
            float charge = pawn.ChargeTime / MathF.Max(def.Alt.MaxCharge, 0.01f);
            pawn.ChargeTime = 0f;
            if (pawn.FireCooldown <= 0f) Fire(pawn, true, 0.6f + charge * 1.4f);
            return;
        }

        if (pawn.FireCooldown > 0f) return;

        if (input.Fire && pawn.CanFire(pawn.Weapon, false))
        {
            if (!def.SpinUp || pawn.SpinUp > 0.55f) Fire(pawn, false, 1f);
        }
        else if (input.AltFire && def.Alt.ZoomFov <= 0f && pawn.CanFire(pawn.Weapon, true))
        {
            if (!def.SpinUp || pawn.SpinUp > 0.55f) Fire(pawn, true, 1f);
        }
        else if ((input.Fire || input.AltFire) && !pawn.CanFire(pawn.Weapon, input.AltFire))
        {
            OnSound?.Invoke(SoundId.DryFire, pawn.Position, 0.4f);
            pawn.FireCooldown = 0.35f;
            pawn.SwitchToBestAvailable();
            if (pawn.PlayerIndex >= 0) FeedbackFor(pawn).Pickup(Loc.HudNoAmmo, 1.2f);
        }
    }

    private void Fire(Pawn pawn, bool alt, float chargeScale)
    {
        var def = pawn.WeaponDef;
        FireDef fire = alt ? def.Alt : def.Primary;
        if (fire.Mode == FireMode.Beam) return;

        pawn.ConsumeAmmo(alt);
        pawn.FireCooldown = fire.Interval;
        pawn.FireBlend = 1f;
        pawn.CameraShake = MathF.Min(1.5f, pawn.CameraShake + fire.ShakeAmount);
        pawn.Pitch = MathX.Clamp(pawn.Pitch + fire.Recoil, -1.5f, 1.5f);
        pawn.ShotsFired++;

        Vector3 origin = pawn.MuzzleWorld();
        Vector3 aim = pawn.ViewDirection;
        float damageScale = chargeScale * (pawn.HasDamageAmp ? 2f : 1f);
        if (Mode.Kind == GameModeKind.Instagib) damageScale *= 40f;

        // A melee weapon has no muzzle. Firing a flash and a flare into the air in front of the
        // impact hammer made a contact weapon look like it was launching something — which is
        // exactly how it read on screen. Melee shows its effect where it lands, in MeleeSwing.
        bool hasMuzzle = fire.Mode != FireMode.Melee;
        if (hasMuzzle) Particles.MuzzleFlash(origin, aim, alt ? 1.2f : 1f, def.Tint);
        OnSound?.Invoke(WeaponSound(def.Kind, alt), pawn.Position, 1f);

        switch (fire.Mode)
        {
            case FireMode.Hitscan:
                for (int i = 0; i < Math.Max(1, fire.Shots); i++)
                {
                    float spread = fire.Spread;
                    // Minigun and enforcer bloom with sustained fire; sniper never spreads.
                    if (def.SpinUp) spread *= 0.6f + pawn.SpinUp * 0.8f;
                    Vector3 dir = spread > 0f ? Rng.ConeDirection(aim, spread) : aim;
                    HitscanShot(pawn, origin, dir, fire, damageScale, def.Tint);
                }
                break;

            case FireMode.Projectile:
                for (int i = 0; i < Math.Max(1, fire.Shots); i++)
                {
                    Vector3 dir = fire.Spread > 0f ? Rng.ConeDirection(aim, fire.Spread) : aim;
                    SpawnProjectile(fire.Projectile, fire, origin, dir, pawn, damageScale, def.Tint);
                }
                if (fire.SelfKnockback > 0f && !pawn.OnGround)
                    pawn.Velocity -= aim * fire.SelfKnockback * 0.25f;
                break;

            case FireMode.Melee:
                MeleeSwing(pawn, origin, aim, fire, damageScale);
                break;
        }

        if (!pawn.CanFire(pawn.Weapon, alt) && pawn.AmmoFor(pawn.Weapon) <= 0)
            pawn.SwitchToBestAvailable();

        // Muzzle light: brief, bright, and priority-boosted so it survives the light cull.
        if (hasMuzzle)
            _renderer.Particles.Spawn(BlendMode.Additive, origin, Vector3.Zero,
                new Vector4(def.Tint * 3f, 0.9f), new Vector4(def.Tint, 0f), 0.3f, 0.05f, 0.05f, Spr.Flare);
    }

    private void HitscanShot(Pawn shooter, Vector3 origin, Vector3 dir, in FireDef fire, float damageScale,
        Vector3 tint)
    {
        Vector3 end = origin + dir * fire.Range;

        // Shock combo: a shock beam that clips a shock ball detonates it.
        int comboIndex = FindProjectileAlongRay(origin, end, shooter.Id, out float comboDist);
        var worldHit = Level.Collision.Raycast(origin, end);
        float worldDist = worldHit.Hit ? worldHit.Distance : fire.Range;

        Pawn hitPawn = TracePawns(origin, dir, MathF.Min(worldDist, fire.Range), shooter, out float pawnDist,
            out Vector3 pawnPoint, out bool headshot);

        if (comboIndex >= 0 && comboDist < MathF.Min(pawnDist, worldDist))
        {
            ShockCombo(comboIndex, shooter);
            Effects.AddTracer(origin, Projectiles[comboIndex].Position, tint, 0.05f, 0.10f);
            return;
        }

        if (hitPawn != null && pawnDist <= worldDist)
        {
            float dmg = fire.Damage * damageScale;
            if (headshot && fire.HeadshotMultiplier > 1f) dmg *= fire.HeadshotMultiplier;
            shooter.ShotsHit++;
            Damage(hitPawn, shooter, dmg, DamageType.Hitscan, dir, headshot);
            if (fire.Knockback > 0f) hitPawn.Velocity += dir * fire.Knockback * 0.25f;
            Particles.BloodSpray(pawnPoint, -dir, 0.8f);
            Effects.AddTracer(origin, pawnPoint, tint, 0.04f, 0.08f);
            OnSound?.Invoke(SoundId.HitFlesh, pawnPoint, 0.6f);
            return;
        }

        if (HitStructures(shooter, origin, dir, MathF.Min(worldDist, fire.Range),
                fire.Damage * damageScale, tint))
            return;

        if (worldHit.Hit)
        {
            Effects.AddTracer(origin, worldHit.Point, tint, 0.04f, 0.08f);
            Particles.ImpactSparks(worldHit.Point, worldHit.Normal, 0.9f, tint);
            Effects.AddBulletHole(worldHit.Point, worldHit.Normal, 0.14f);
            OnSound?.Invoke(SoundId.HitWall, worldHit.Point, 0.5f);
        }
        else
        {
            Effects.AddTracer(origin, end, tint, 0.03f, 0.07f);
        }
    }

    /// <summary>
    /// Resolves a direct shot against the hardware — vehicles and Onslaught nodes — once the
    /// pawn trace has come up empty. Returns true when something absorbed the shot.
    /// </summary>
    private bool HitStructures(Pawn shooter, Vector3 origin, Vector3 dir, float maxDist, float damage,
        Vector3 tint)
    {
        var vehicle = TraceVehicles(origin, dir, maxDist, shooter, out float vDist, out Vector3 vPoint);
        int node = TraceNodes(origin, dir, maxDist, out float nDist);

        if (vehicle != null && (node < 0 || vDist <= nDist))
        {
            shooter.ShotsHit++;
            float dmg = damage;
            if (Mode.TeamBased && vehicle.Team != Team.None && vehicle.Team == shooter.Team)
                dmg *= Mode.FriendlyFire;
            DamageVehicle(vehicle, shooter, dmg);
            Effects.AddTracer(origin, vPoint, tint, 0.04f, 0.08f);
            Particles.ImpactSparks(vPoint, -dir, 1.2f, new Vector3(1f, 0.8f, 0.4f));
            OnSound?.Invoke(SoundId.HitWall, vPoint, 0.6f);
            return true;
        }

        if (node >= 0 && shooter.Team != Team.None)
        {
            Vector3 point = origin + dir * nDist;
            var evt = Onslaught.Hurt(node, shooter.Team, damage, out var hit);
            Effects.AddTracer(origin, point, tint, 0.04f, 0.08f);
            if (evt == NodeEvent.None) return false;    // our own node, or an untouchable core
            shooter.ShotsHit++;
            Particles.EnergyBurst(point, evt == NodeEvent.Blocked ? new Vector3(1f, 0.4f, 0.2f) : tint, 0.7f);
            if (evt == NodeEvent.Blocked && shooter.PlayerIndex >= 0)
                FeedbackFor(shooter).Sub(Loc.OnsNodeBlocked, 1f);
            else HandleNodeEvent(evt, hit, shooter);
            return true;
        }

        if (HitObjective(shooter, origin, dir, maxDist, damage, tint)) return true;
        return false;
    }

    /// <summary>
    /// A direct shot at the current Assault objective. Only the current one is a target — an
    /// attacker who shoots the next generator in the line is wasting ammunition, exactly as in
    /// the original.
    /// </summary>
    private bool HitObjective(Pawn shooter, Vector3 origin, Vector3 dir, float maxDist, float damage,
        Vector3 tint)
    {
        if (Mode.Kind != GameModeKind.Assault) return false;
        var o = Assault.CurrentObjective;
        if (o == null || o.Kind != ObjectiveKind.Destroy || shooter.Team != Assault.Attackers) return false;

        Vector3 centre = o.Position + MathX.Up * 1.5f;
        Vector3 m = origin - centre;
        float radius = MathF.Max(o.Radius * 0.6f, 1.9f);
        float b = Vector3.Dot(m, dir);
        float c = m.LengthSquared() - radius * radius;
        if (c > 0f && b > 0f) return false;
        float disc = b * b - c;
        if (disc < 0f) return false;
        float t = MathF.Max(0f, -b - MathF.Sqrt(disc));
        if (t > maxDist) return false;

        shooter.ShotsHit++;
        Vector3 point = origin + dir * t;
        Effects.AddTracer(origin, point, tint, 0.04f, 0.08f);
        Particles.ImpactSparks(point, -dir, 1.4f, new Vector3(1f, 0.75f, 0.3f));
        var evt = Assault.Hurt(shooter.Team, damage, out var hit);
        HandleObjectiveEvent(evt, hit, shooter);
        return true;
    }

    private void FireBeam(Pawn pawn, in FireDef fire, float dt)
    {
        pawn.BeamDamageAccumulator += dt;
        Vector3 origin = pawn.MuzzleWorld();
        Vector3 dir = pawn.ViewDirection;
        Vector3 end = origin + dir * fire.Range;

        var worldHit = Level.Collision.Raycast(origin, end);
        float maxDist = worldHit.Hit ? worldHit.Distance : fire.Range;
        Pawn target = TracePawns(origin, dir, maxDist, pawn, out float pawnDist, out Vector3 point, out _);

        Vector3 tip = target != null ? point : (worldHit.Hit ? worldHit.Point : end);
        Effects.AddLightning(origin, tip, pawn.WeaponDef.Tint, 0.14f, 0.06f);

        // Damage ticks on a fixed cadence so frame rate never changes DPS.
        if (pawn.BeamDamageAccumulator >= 0.1f)
        {
            pawn.BeamDamageAccumulator -= 0.1f;
            pawn.ConsumeAmmo(true);
            pawn.ShotsFired++;
            OnSound?.Invoke(SoundId.PulseBeam, pawn.Position, 0.5f);
            if (target != null)
            {
                pawn.ShotsHit++;
                float amp = pawn.HasDamageAmp ? 2f : 1f;
                Damage(target, pawn, fire.Damage * 0.1f * amp, DamageType.Energy, dir);
                Particles.BloodSpray(point, -dir, 0.3f);
            }
            else if (!HitStructures(pawn, origin, dir, maxDist, fire.Damage * 0.1f, pawn.WeaponDef.Tint)
                     && worldHit.Hit)
            {
                Particles.ImpactSparks(worldHit.Point, worldHit.Normal, 0.4f, pawn.WeaponDef.Tint);
            }
        }
        _ = pawnDist;
    }

    private void MeleeSwing(Pawn pawn, Vector3 origin, Vector3 dir, in FireDef fire, float damageScale)
    {
        Vector3 end = origin + dir * fire.Range;
        var worldHit = Level.Collision.Raycast(origin, end);
        float maxDist = worldHit.Hit ? worldHit.Distance : fire.Range;
        Pawn target = TracePawns(origin, dir, maxDist, pawn, out _, out Vector3 point, out bool head);

        if (target != null)
        {
            pawn.ShotsFired++;
            pawn.ShotsHit++;
            Damage(target, pawn, fire.Damage * damageScale, DamageType.Melee, dir, head);
            target.Velocity += dir * fire.Knockback + MathX.Up * fire.Knockback * 0.3f;
            Particles.BloodSpray(point, -dir, 1.4f);
            Particles.EnergyBurst(point, new Vector3(0.6f, 0.85f, 1f), 0.7f);
            OnSound?.Invoke(SoundId.HammerHit, point, 1f);
        }
        else if (worldHit.Hit)
        {
            Particles.ImpactSparks(worldHit.Point, worldHit.Normal, 1.6f, new Vector3(0.7f, 0.85f, 1f));
            Effects.AddScorch(worldHit.Point, worldHit.Normal, 0.7f);
            OnSound?.Invoke(SoundId.HammerHit, worldHit.Point, 0.8f);
            // Hammer jump: firing at the floor launches the user.
            if (fire.SelfKnockback > 0f || Vector3.Dot(worldHit.Normal, MathX.Up) > 0.6f)
                pawn.Velocity += worldHit.Normal * MathF.Max(fire.SelfKnockback, 10.5f);
        }
        pawn.ShotsFired++;
    }

    // ---------------------------------------------------------------- projectiles

    private void SpawnProjectile(ProjectileKind kind, in FireDef fire, Vector3 origin, Vector3 dir,
        Pawn owner, float damageScale, Vector3 tint)
    {
        for (int i = 0; i < Projectiles.Length; i++)
        {
            if (Projectiles[i].Active) continue;
            Projectiles[i] = ProjectileFactory.Create(kind, fire, origin, dir, owner.Id, owner.Team,
                tint, damageScale, Rng);
            return;
        }
    }

    private void UpdateProjectiles(float dt)
    {
        for (int i = 0; i < Projectiles.Length; i++)
        {
            ref Projectile p = ref Projectiles[i];
            if (!p.Active) continue;

            p.Life -= dt;
            p.ArmDelay -= dt;
            if (p.Life <= 0f)
            {
                if (p.ExplodeOnTimeout) ExplodeProjectile(ref p);
                else FizzleProjectile(ref p);
                continue;
            }

            if (p.Stuck)
            {
                EmitProjectileTrail(ref p, dt);
                continue;
            }

            // Ballistic projectiles obey the arena's gravity, so grenades really do float on
            // the low-gravity rooftop maps.
            if (p.AffectedByGravity) p.Velocity.Y -= Physics.Gravity * Level.GravityScale * dt;

            Vector3 next = p.Position + p.Velocity * dt;

            // --- pawn hits ---
            Pawn hit = TracePawnsSphere(p.Position, next, p.Radius, p.OwnerId, out Vector3 hitPoint,
                out bool headshot);
            if (hit != null && p.ArmDelay <= 0f)
            {
                var owner = FindPawn(p.OwnerId);
                float dmg = p.Damage * p.DamageScale;
                if (headshot && p.HeadshotMultiplier > 1f) dmg *= p.HeadshotMultiplier;
                if (owner != null) owner.ShotsHit++;
                Damage(hit, owner, dmg, DamageType.Generic, MathX.SafeNormalize(p.Velocity, MathX.Forward),
                    headshot);
                hit.Velocity += MathX.SafeNormalize(p.Velocity, Vector3.Zero) * p.Knockback * 0.22f;
                Particles.BloodSpray(hitPoint, -MathX.SafeNormalize(p.Velocity, MathX.Up), 1.1f);
                p.Position = hitPoint;
                if (p.SplashRadius > 0f) ExplodeProjectile(ref p);
                else { FizzleProjectile(ref p); OnSound?.Invoke(SoundId.HitFlesh, hitPoint, 0.7f); }
                continue;
            }

            // --- vehicle hits ---
            // A rocket has to stop at a tank, not fly through it and detonate on the ground
            // behind. Splash out of the explosion then applies to the crew as well.
            if (p.ArmDelay <= 0f)
            {
                Vector3 step = next - p.Position;
                float stepLen = step.Length();
                if (stepLen > 1e-5f)
                {
                    var owner = FindPawn(p.OwnerId);
                    var struckV = TraceVehicles(p.Position, step / stepLen, stepLen, owner,
                        out float vDist, out Vector3 vPoint);
                    if (struckV != null)
                    {
                        float dmg = p.Damage * p.DamageScale;
                        if (Mode.TeamBased && owner != null && struckV.Team != Team.None
                            && owner.Team == struckV.Team) dmg *= Mode.FriendlyFire;
                        if (owner != null) owner.ShotsHit++;
                        DamageVehicle(struckV, owner, dmg);
                        p.Position = vPoint;
                        if (p.SplashRadius > 0f) ExplodeProjectile(ref p);
                        else
                        {
                            Particles.ImpactSparks(vPoint, -step / stepLen, 1.3f, p.Color);
                            FizzleProjectile(ref p);
                            OnSound?.Invoke(SoundId.HitWall, vPoint, 0.7f);
                        }
                        _ = vDist;
                        continue;
                    }
                }
            }

            // --- world hits ---
            var worldHit = Level.Collision.Raycast(p.Position, next);
            if (worldHit.Hit)
            {
                if (p.StickOnImpact)
                {
                    p.Position = worldHit.Point + worldHit.Normal * 0.06f;
                    p.Velocity = Vector3.Zero;
                    p.Stuck = true;
                    p.Life = MathF.Min(p.Life, 3.2f);
                    OnSound?.Invoke(SoundId.BioSplat, p.Position, 0.6f);
                    continue;
                }
                if (p.BouncesLeft > 0)
                {
                    p.BouncesLeft--;
                    p.Position = worldHit.Point + worldHit.Normal * 0.04f;
                    Vector3 v = p.Velocity;
                    v -= worldHit.Normal * (2f * Vector3.Dot(v, worldHit.Normal));
                    float restitution = p.Kind switch
                    {
                        ProjectileKind.RipperBlade => 0.98f,
                        ProjectileKind.FlakShard => 0.55f,
                        ProjectileKind.Grenade => 0.52f,
                        _ => 0.45f,
                    };
                    p.Velocity = v * restitution;
                    Particles.ImpactSparks(worldHit.Point, worldHit.Normal, 0.5f, p.Color);
                    OnSound?.Invoke(p.Kind == ProjectileKind.RipperBlade ? SoundId.BladeBounce : SoundId.Bounce,
                        worldHit.Point, 0.45f);
                    if (p.Velocity.LengthSquared() < 1.2f && p.ExplodeOnTimeout) p.Life = MathF.Min(p.Life, 0.8f);
                    continue;
                }
                p.Position = worldHit.Point;
                if (p.SplashRadius > 0f) ExplodeProjectile(ref p);
                else
                {
                    Particles.ImpactSparks(worldHit.Point, worldHit.Normal, 1.0f, p.Color);
                    Effects.AddBulletHole(worldHit.Point, worldHit.Normal, 0.18f);
                    FizzleProjectile(ref p);
                    OnSound?.Invoke(SoundId.HitWall, worldHit.Point, 0.5f);
                }
                continue;
            }

            p.Position = next;
            EmitProjectileTrail(ref p, dt);
        }
    }

    private void EmitProjectileTrail(ref Projectile p, float dt)
    {
        p.TrailTimer -= dt;
        if (p.TrailTimer > 0f) return;

        switch (p.Kind)
        {
            case ProjectileKind.Rocket:
                p.TrailTimer = 0.014f;
                Particles.Trail(p.Position, new Vector3(1f, 0.55f, 0.15f), 0.42f, 0.28f, Spr.Puff);
                Particles.Smoke(p.Position, -p.Velocity * 0.04f, 0.26f, 1.0f, 0.35f);
                break;
            case ProjectileKind.Warhead:
                p.TrailTimer = 0.012f;
                Particles.Trail(p.Position, new Vector3(1f, 0.7f, 0.2f), 0.85f, 0.4f, Spr.Puff);
                Particles.Smoke(p.Position, -p.Velocity * 0.05f, 0.55f, 1.6f, 0.5f);
                break;
            case ProjectileKind.ShockBall:
                p.TrailTimer = 0.02f;
                Particles.Trail(p.Position, new Vector3(0.55f, 0.35f, 1f), 0.75f, 0.24f, Spr.Swirl);
                break;
            case ProjectileKind.PlasmaBolt:
                p.TrailTimer = 0.022f;
                Particles.Trail(p.Position, new Vector3(0.35f, 1f, 0.5f), 0.30f, 0.14f, Spr.Plasma);
                break;
            case ProjectileKind.BioGlob:
                p.TrailTimer = 0.05f;
                Particles.Trail(p.Position, new Vector3(0.4f, 1f, 0.2f), 0.26f, 0.22f, Spr.Plasma);
                break;
            case ProjectileKind.FlakShard:
                p.TrailTimer = 0.03f;
                Particles.Trail(p.Position, new Vector3(1f, 0.6f, 0.2f), 0.14f, 0.12f, Spr.Spark);
                break;
            case ProjectileKind.RipperBlade:
                p.TrailTimer = 0.02f;
                Particles.Trail(p.Position, new Vector3(0.7f, 0.9f, 1f), 0.18f, 0.12f, Spr.Spark);
                break;
            case ProjectileKind.Grenade:
            case ProjectileKind.FlakShell:
                p.TrailTimer = 0.05f;
                Particles.Smoke(p.Position, Vector3.Zero, 0.14f, 0.5f, 0.24f);
                break;
        }
    }

    private void ExplodeProjectile(ref Projectile p)
    {
        Vector3 pos = p.Position;
        float radius = p.SplashRadius;
        float damage = p.SplashDamage * p.DamageScale;
        var owner = FindPawn(p.OwnerId);
        Vector3 tint = p.Kind switch
        {
            ProjectileKind.BioGlob => new Vector3(0.45f, 1f, 0.25f),
            ProjectileKind.ShockBall => new Vector3(0.6f, 0.4f, 1f),
            ProjectileKind.PlasmaBolt => new Vector3(0.4f, 1f, 0.55f),
            _ => new Vector3(1f, 0.6f, 0.18f),
        };

        float scale = p.Kind == ProjectileKind.Warhead ? 4.5f : MathX.Clamp(radius / 5f, 0.45f, 2.2f);
        Particles.Explosion(pos, scale, tint);
        Effects.AddScorch(pos, MathX.Up, radius * 0.5f);
        OnSound?.Invoke(p.Kind == ProjectileKind.Warhead ? SoundId.Nuke : SoundId.Explosion, pos,
            p.Kind == ProjectileKind.Warhead ? 2f : 1f);

        Explode(pos, radius, damage, p.Knockback, owner, p.Kind == ProjectileKind.Warhead
            ? DamageType.Explosion : DamageType.Explosion);
        p.Active = false;
    }

    private void FizzleProjectile(ref Projectile p)
    {
        Particles.EnergyBurst(p.Position, p.Color, 0.45f);
        p.Active = false;
    }

    private void ShockCombo(int projectileIndex, Pawn shooter)
    {
        ref Projectile p = ref Projectiles[projectileIndex];
        Vector3 pos = p.Position;
        Particles.Explosion(pos, 2.4f, new Vector3(0.62f, 0.38f, 1f));
        Particles.EnergyBurst(pos, new Vector3(0.7f, 0.45f, 1f), 3.2f);
        Effects.AddScorch(pos, MathX.Up, 4.5f);
        OnSound?.Invoke(SoundId.ShockCombo, pos, 1.6f);
        Explode(pos, 7.2f, 145f * (shooter.HasDamageAmp ? 2f : 1f), 30f, shooter, DamageType.Energy);
        p.Active = false;
        if (shooter.PlayerIndex >= 0) FeedbackFor(shooter).Sub("震盪連鎖", 1.4f);
    }

    private int FindProjectileAlongRay(Vector3 from, Vector3 to, int shooterId, out float distance)
    {
        distance = float.MaxValue;
        int best = -1;
        Vector3 dir = to - from;
        float len = dir.Length();
        if (len < 1e-4f) return -1;
        dir /= len;

        for (int i = 0; i < Projectiles.Length; i++)
        {
            ref Projectile p = ref Projectiles[i];
            if (!p.Active || !p.ComboTarget || p.OwnerId != shooterId) continue;
            Vector3 toBall = p.Position - from;
            float t = Vector3.Dot(toBall, dir);
            if (t < 0f || t > len) continue;
            float perp = (toBall - dir * t).Length();
            if (perp > p.Radius + 0.35f) continue;
            if (t < distance) { distance = t; best = i; }
        }
        return best;
    }

    // ---------------------------------------------------------------- damage

    /// <summary>Radial damage with line-of-sight occlusion and distance falloff.</summary>
    public void Explode(Vector3 center, float radius, float damage, float knockback, Pawn attacker,
        DamageType type)
    {
        foreach (var target in Pawns)
        {
            if (!target.Alive) continue;
            Vector3 targetCenter = target.Center;
            float dist = Vector3.Distance(center, targetCenter);
            if (dist > radius) continue;

            // Occlusion: only ignore geometry when the blast originates inside the target.
            if (dist > 0.6f)
            {
                var occl = Level.Collision.Raycast(center, targetCenter);
                if (occl.Hit && occl.Distance < dist - 0.7f) continue;
            }

            float falloff = 1f - MathX.Saturate(dist / radius);
            falloff = falloff * falloff * 0.55f + falloff * 0.45f;
            float dmg = damage * falloff;
            bool selfDamage = attacker == target;
            if (selfDamage) dmg *= 0.42f;
            if (Mode.TeamBased && attacker != null && !selfDamage && attacker.Team == target.Team)
                dmg *= Mode.FriendlyFire;

            Vector3 push = MathX.SafeNormalize(targetCenter - center, MathX.Up);
            push.Y = MathF.Max(push.Y, 0.35f);
            if (!target.Invulnerable)
                target.Velocity += push * knockback * falloff * (selfDamage ? 1.25f : 1f);

            if (dmg > 0.5f) Damage(target, attacker, dmg, type, push);
        }

        ExplodeStructures(center, radius, damage, attacker);
    }

    /// <summary>
    /// Blast damage against the things the pawn loop cannot see. Vehicles take splash like
    /// anything else; Onslaught nodes take it too, because shelling a node from a tank or SPMA
    /// rather than standing on it is the mode's whole ranged game.
    /// </summary>
    private void ExplodeStructures(Vector3 center, float radius, float damage, Pawn attacker)
    {
        foreach (var v in Vehicles)
        {
            if (!v.Alive) continue;
            float bulk = v.Def.HalfExtents.Length();
            float dist = Vector3.Distance(center, v.Position) - bulk;
            if (dist > radius) continue;
            float falloff = 1f - MathX.Saturate(MathF.Max(dist, 0f) / radius);
            float dmg = damage * falloff;
            if (Mode.TeamBased && attacker != null && v.Team != Team.None && attacker.Team == v.Team)
                dmg *= Mode.FriendlyFire;
            if (dmg > 0.5f) DamageVehicle(v, attacker, dmg);
        }

        if (attacker == null || attacker.Team == Team.None) return;

        if (Mode.Kind == GameModeKind.Assault)
        {
            var o = Assault.CurrentObjective;
            if (o == null || o.Kind != ObjectiveKind.Destroy || attacker.Team != Assault.Attackers) return;
            float d = MathF.Max(0f, Vector3.Distance(center, o.Position + MathX.Up * 1.5f) - o.Radius * 0.6f);
            if (d > radius) return;
            var oEvt = Assault.Hurt(attacker.Team, damage * (1f - MathX.Saturate(d / radius)), out var hit);
            HandleObjectiveEvent(oEvt, hit, attacker);
            return;
        }

        if (Mode.Kind != GameModeKind.Onslaught) return;
        int index = Onslaught.NearestWithin(center, radius + 4f);
        if (index < 0) return;
        float nodeDist = MathF.Max(0f, Vector3.Distance(center, Onslaught.Nodes[index].Position) - 4f);
        float nodeDmg = damage * (1f - MathX.Saturate(nodeDist / radius));
        if (nodeDmg <= 0.5f) return;
        var evt = Onslaught.Hurt(index, attacker.Team, nodeDmg, out var node);
        HandleNodeEvent(evt, node, attacker);
    }

    public void Damage(Pawn target, Pawn attacker, float amount, DamageType type, Vector3 direction,
        bool headshot = false)
    {
        if (!target.Alive || target.Invulnerable || amount <= 0f) return;
        if (attacker != null && attacker != target && Mode.Kind != GameModeKind.Instagib
            && ControllerFor(attacker) is BotController bot)
            amount *= bot.DamageScale;
        if (Mode.TeamBased && attacker != null && attacker != target && attacker.Team == target.Team)
        {
            amount *= Mode.FriendlyFire;
            if (amount <= 0.01f) return;
        }

        float healthBefore = target.Health;
        float armorBefore = target.Armor;
        target.ApplyDamage(amount, type);
        float appliedDamage = MathF.Max(0f,
            healthBefore - target.Health + armorBefore - target.Armor);
        target.LastDamageTime = Time;
        if (attacker != null && attacker != target) target.LastAttackerId = attacker.Id;

        if (target.PlayerIndex >= 0)
        {
            var fb = Feedbacks[target.Id];
            Vector3 fromDir = attacker != null
                ? MathX.SafeNormalize((attacker.Position - target.Position).FlatXZ(), -direction.FlatXZ())
                : -MathX.SafeNormalize(direction.FlatXZ(), target.ForwardFlat);
            fb.DamageDirection = MathF.Atan2(
                Vector3.Dot(fromDir, target.RightFlat), Vector3.Dot(fromDir, target.ForwardFlat));
            fb.DamageDirectionTimer = 1.3f;
            fb.DamageNumber(appliedDamage, dealt: false);
        }

        if (attacker != null && attacker != target && attacker.PlayerIndex >= 0)
        {
            var fb = Feedbacks[attacker.Id];
            fb.HitMarkerTimer = 0.22f;
            fb.HitMarkerLethal = target.Health <= 0f;
            fb.DamageNumber(appliedDamage, dealt: true);
            if (headshot) fb.Sub(Loc.AnnHeadshot, 1.1f);
        }

        ControllerFor(target)?.OnDamaged(this, attacker, amount, direction);

        if (target.Health <= 0f) Kill(target, attacker, type, headshot);
    }

    public void Kill(Pawn victim, Pawn killer, DamageType type, bool headshot = false)
    {
        if (!victim.Alive || victim.Invulnerable) return;
        bool killedFlagCarrier = Mode.Kind == GameModeKind.CaptureTheFlag && victim.HasFlag;
        if (type == DamageType.Void) VoidDeaths++;
        else if (type == DamageType.Fall) FallDeaths++;
        else if (type == DamageType.Lava) LavaDeaths++;
        if (type is DamageType.Void or DamageType.Fall or DamageType.Lava)
        {
            if (EnvironmentalDeathDetails.Count >= 32) EnvironmentalDeathDetails.RemoveAt(0);
            EnvironmentalDeathDetails.Add($"{victim.Name}: {type} at {victim.Position} velocity {victim.Velocity} " +
                $"last-ground {victim.LastGroundPosition}");
        }
        victim.Alive = false;
        victim.Health = 0f;
        victim.DeathTime = 0.0001f;
        victim.Deaths++;
        victim.RespawnTimer = Mode.RespawnDelay;
        victim.FiringBeam = false;

        bool gib = type is DamageType.Explosion or DamageType.Telefrag
                || (type == DamageType.Energy && victim.Health < -40f)
                || Mode.Kind == GameModeKind.Instagib;
        victim.Gibbed = gib;

        if (gib)
        {
            Particles.Gibs(victim.Center, 1.3f);
            OnSound?.Invoke(SoundId.Gib, victim.Position, 1f);
        }
        else
        {
            Particles.BloodSpray(victim.Center, MathX.Up, 2f);
            OnSound?.Invoke(SoundId.Death, victim.Position, 1f);
        }
        Effects.AddBloodSplat(victim.Position + new Vector3(0, 0.05f, 0), MathX.Up, 0.62f);

        DropFlag(victim, type);

        // --- scoring and announcements ---
        if (killer != null && killer != victim)
        {
            bool teamKill = Mode.TeamBased && killer.Team == victim.Team;
            killer.Frags += teamKill ? -1 : 1;
            if (!teamKill)
            {
                killer.Streak++;
                killer.MultiKillCount++;
                killer.MultiKillTimer = 3.2f;
                if (killedFlagCarrier) killer.FlagCarrierKills++;
                Mode.OnFrag(this, killer, victim);
                AnnounceKill(killer, victim, headshot);
            }
            else
            {
                AddKillFeed(Loc.KillFeed(killer.Name, victim.Name), new Vector3(1f, 0.8f, 0.2f));
            }
        }
        else
        {
            victim.Frags--;
            victim.Suicides++;
            string text = type switch
            {
                DamageType.Lava => Loc.LavaDeathFeed(victim.Name),
                DamageType.Void => Loc.FallDeathFeed(victim.Name),
                DamageType.Fall => Loc.FallDamageFeed(victim.Name),
                _ => Loc.SuicideFeed(victim.Name),
            };
            AddKillFeed(text, new Vector3(0.75f, 0.75f, 0.8f));
        }

        // Ending someone's spree is worth calling out.
        if (victim.Streak >= 5 && killer != null && killer != victim)
            AddKillFeed(Loc.SpreeEnded(victim.Name, killer.Name), new Vector3(1f, 0.6f, 0.2f));
        victim.Streak = 0;
        victim.MultiKillCount = 0;

        ControllerFor(victim)?.OnKilled(this, killer);
        Mode.OnDeath(this, victim, killer);
    }

    private void AnnounceKill(Pawn killer, Pawn victim, bool headshot)
    {
        AddKillFeed(Loc.KillFeed(killer.Name, victim.Name), GameTypes.TeamColor(killer.Team));

        var fb = killer.PlayerIndex >= 0 ? Feedbacks[killer.Id] : null;

        if (Mode.FirstBloodPending)
        {
            Mode.FirstBloodPending = false;
            Broadcast(Loc.AnnFirstBlood, new Vector3(1f, 0.25f, 0.2f));
            OnSound?.Invoke(SoundId.AnnounceMajor, killer.Position, 1f);
        }

        string multi = Loc.MultiKillAnnouncement(killer.MultiKillCount);
        if (multi != null)
        {
            fb?.Big(multi, new Vector3(1f, 0.7f, 0.15f), 2.0f);
            AddKillFeed($"{killer.Name} — {multi}", new Vector3(1f, 0.7f, 0.15f));
            OnSound?.Invoke(SoundId.AnnounceMajor, killer.Position, 1f);
        }

        string spree = Loc.SpreeAnnouncement(killer.Streak);
        if (spree != null)
        {
            fb?.Big(spree, new Vector3(1f, 0.35f, 0.15f), 2.4f);
            AddKillFeed($"{killer.Name} — {spree}", new Vector3(1f, 0.35f, 0.15f));
            OnSound?.Invoke(SoundId.AnnounceMajor, killer.Position, 1f);
        }

        if (headshot && multi == null && spree == null)
            fb?.Sub(Loc.AnnHeadshot, 1.3f);
    }

    public void AddKillFeed(string text, Vector3 color)
    {
        KillFeed.Add(new KillFeedEntry { Text = text, Timer = 5.5f, Color = color });
        if (KillFeed.Count > 6) KillFeed.RemoveAt(0);
    }

    public void Broadcast(string text, Vector3 color, float duration = 2.2f)
    {
        foreach (var p in Pawns)
            if (p.PlayerIndex >= 0) Feedbacks[p.Id].Big(text, color, duration);
    }

    // ---------------------------------------------------------------- tracing

    /// <summary>Nearest pawn intersected by a ray, treating pawns as capsules.</summary>
    public Pawn TracePawns(Vector3 origin, Vector3 dir, float maxDist, Pawn ignore, out float distance,
        out Vector3 point, out bool headshot)
    {
        distance = maxDist;
        point = origin + dir * maxDist;
        headshot = false;
        Pawn best = null;

        foreach (var target in Pawns)
        {
            if (target == ignore || !target.Alive) continue;
            Vector3 feet = target.Position;
            Vector3 head = target.Position + new Vector3(0, target.CurrentHeight, 0);
            if (!RayCapsule(origin, dir, maxDist, feet, head, Physics.PawnRadius, out float t)) continue;
            if (t >= distance) continue;
            distance = t;
            point = origin + dir * t;
            best = target;
            headshot = point.Y > target.Position.Y + target.CurrentHeight - 0.30f;
        }
        return best;
    }

    /// <summary>
    /// Ray against every live vehicle's box. Vehicles are far too big to leave out of the shot
    /// path — without this a Goliath sitting in the open would soak every bullet aimed at it and
    /// take nothing. The box is axis-aligned in the vehicle's own frame, so the ray is rotated
    /// into that frame rather than the box out of it.
    /// </summary>
    public Vehicle TraceVehicles(Vector3 origin, Vector3 dir, float maxDist, Pawn ignore,
        out float distance, out Vector3 point)
    {
        distance = maxDist;
        point = origin + dir * maxDist;
        Vehicle best = null;

        foreach (var v in Vehicles)
        {
            if (!v.Alive) continue;
            // Do not let a driver shoot their own ride from inside it.
            if (ignore != null && ignore.InVehicle && ignore.VehicleId == v.Id) continue;

            var inv = Matrix4x4.CreateRotationY(-v.Yaw);
            Vector3 lo = Vector3.Transform(origin - v.Position, inv);
            Vector3 ld = Vector3.TransformNormal(dir, inv);
            if (!RayBox(lo, ld, v.Def.HalfExtents, out float t) || t < 0f || t >= distance) continue;
            distance = t;
            point = origin + dir * t;
            best = v;
        }
        return best;
    }

    /// <summary>Slab test against a box centred on the origin.</summary>
    private static bool RayBox(Vector3 origin, Vector3 dir, Vector3 halfExtents, out float t)
    {
        t = 0f;
        float near = -float.MaxValue, far = float.MaxValue;
        for (int axis = 0; axis < 3; axis++)
        {
            float o = axis == 0 ? origin.X : axis == 1 ? origin.Y : origin.Z;
            float d = axis == 0 ? dir.X : axis == 1 ? dir.Y : dir.Z;
            float h = axis == 0 ? halfExtents.X : axis == 1 ? halfExtents.Y : halfExtents.Z;
            if (MathF.Abs(d) < 1e-6f)
            {
                if (o < -h || o > h) return false;
                continue;
            }
            float t1 = (-h - o) / d, t2 = (h - o) / d;
            if (t1 > t2) (t1, t2) = (t2, t1);
            near = MathF.Max(near, t1);
            far = MathF.Min(far, t2);
            if (near > far) return false;
        }
        if (far < 0f) return false;
        t = near > 0f ? near : far;
        return true;
    }

    /// <summary>
    /// Ray against the Onslaught nodes, treated as spheres around the pillar. Link-gun-style
    /// direct fire is how a node is attacked at close range, and hitscan needs to see it.
    /// </summary>
    private int TraceNodes(Vector3 origin, Vector3 dir, float maxDist, out float distance)
    {
        distance = maxDist;
        int best = -1;
        if (Mode.Kind != GameModeKind.Onslaught) return -1;

        var nodes = Onslaught.Nodes;
        for (int i = 0; i < nodes.Count; i++)
        {
            float radius = nodes[i].IsCore ? 4.5f : 3.2f;
            Vector3 centre = nodes[i].Position + MathX.Up * 2.4f;
            Vector3 m = origin - centre;
            float b = Vector3.Dot(m, dir);
            float c = m.LengthSquared() - radius * radius;
            if (c > 0f && b > 0f) continue;
            float disc = b * b - c;
            if (disc < 0f) continue;
            float t = -b - MathF.Sqrt(disc);
            if (t < 0f) t = 0f;
            if (t >= distance) continue;
            distance = t;
            best = i;
        }
        return best;
    }

    private Pawn TracePawnsSphere(Vector3 from, Vector3 to, float radius, int ignoreId, out Vector3 point,
        out bool headshot)
    {
        point = to;
        headshot = false;
        Vector3 delta = to - from;
        float len = delta.Length();
        if (len < 1e-5f) return null;
        Vector3 dir = delta / len;

        Pawn best = null;
        float bestT = len;
        foreach (var target in Pawns)
        {
            if (target.Id == ignoreId || !target.Alive) continue;
            Vector3 feet = target.Position;
            Vector3 head = target.Position + new Vector3(0, target.CurrentHeight, 0);
            if (!RayCapsule(from, dir, len, feet, head, Physics.PawnRadius + radius, out float t)) continue;
            if (t >= bestT) continue;
            bestT = t;
            best = target;
            point = from + dir * t;
            headshot = point.Y > target.Position.Y + target.CurrentHeight - 0.30f;
        }
        return best;
    }

    /// <summary>Ray versus a vertical capsule. Good enough for hit registration at these speeds.</summary>
    private static bool RayCapsule(Vector3 origin, Vector3 dir, float maxDist, Vector3 a, Vector3 b,
        float radius, out float t)
    {
        t = 0f;
        // Solve against the infinite cylinder in XZ, then clamp against the end caps.
        Vector2 o = new(origin.X - a.X, origin.Z - a.Z);
        Vector2 d = new(dir.X, dir.Z);
        float dd = Vector2.Dot(d, d);

        if (dd < 1e-8f)
        {
            // Vertical shot: hit if inside the radius horizontally and within the span.
            if (o.LengthSquared() > radius * radius) return false;
            float lo = (a.Y - origin.Y) / dir.Y;
            float hi = (b.Y - origin.Y) / dir.Y;
            if (lo > hi) (lo, hi) = (hi, lo);
            if (hi < 0f || lo > maxDist) return false;
            t = MathF.Max(lo, 0f);
            return true;
        }

        float bq = 2f * Vector2.Dot(o, d);
        float c = Vector2.Dot(o, o) - radius * radius;
        float disc = bq * bq - 4f * dd * c;
        if (disc < 0f) return false;
        float sq = MathF.Sqrt(disc);
        float t0 = (-bq - sq) / (2f * dd);
        float t1 = (-bq + sq) / (2f * dd);
        if (t1 < 0f) return false;
        float enter = t0 > 0f ? t0 : 0f;
        if (enter > maxDist) return false;

        // Clamp to the vertical extent, extending slightly for the rounded caps.
        float yAtEnter = origin.Y + dir.Y * enter;
        if (yAtEnter >= a.Y - radius * 0.5f && yAtEnter <= b.Y + radius * 0.5f)
        {
            t = enter;
            return true;
        }

        float targetY = yAtEnter < a.Y ? a.Y : b.Y;
        if (MathF.Abs(dir.Y) < 1e-6f) return false;
        float tPlane = (targetY - origin.Y) / dir.Y;
        if (tPlane < 0f || tPlane > maxDist || tPlane < enter || tPlane > t1) return false;
        Vector3 p = origin + dir * tPlane;
        if (new Vector2(p.X - a.X, p.Z - a.Z).LengthSquared() > radius * radius) return false;
        t = tPlane;
        return true;
    }

    // ---------------------------------------------------------------- pickups

    private void UpdatePickups(float dt)
    {
        foreach (var pu in Pickups)
        {
            pu.Phase += dt;
            if (!pu.Active)
            {
                pu.Timer -= dt;
                if (pu.Timer <= 0f)
                {
                    pu.Active = true;
                    pu.SpawnBlend = 0f;
                    Particles.EnergyBurst(pu.Position + new Vector3(0, 0.4f, 0), pu.GlowColor, 0.7f);
                    OnSound?.Invoke(SoundId.ItemRespawn, pu.Position, 0.5f);
                }
            }
            else pu.SpawnBlend = MathF.Min(1f, pu.SpawnBlend + dt * 3.5f);
        }
    }

    private void HandlePickups(Pawn pawn)
    {
        foreach (var pu in Pickups)
        {
            if (!pu.Active) continue;
            Vector3 delta = pu.Position - (pawn.Position + new Vector3(0, 0.6f, 0));
            if (delta.LengthSquared() > pu.PickupRadius * pu.PickupRadius) continue;
            if (!TryGivePickup(pawn, pu)) continue;

            pu.Active = false;
            pu.Timer = pu.RespawnTime;
            Particles.EnergyBurst(pu.Position + new Vector3(0, 0.35f, 0), pu.GlowColor, 0.55f);
            OnSound?.Invoke(PickupSound(pu.Kind), pu.Position, 0.75f);
        }
    }

    private bool TryGivePickup(Pawn pawn, PickupEntity pu)
    {
        var fb = pawn.PlayerIndex >= 0 ? Feedbacks[pawn.Id] : null;
        switch (pu.Kind)
        {
            case PickupKind.HealthVial:
                if (pawn.Health >= 199f) return false;
                pawn.GiveHealth(5f, 199f);
                fb?.Pickup(Loc.PickedUp(Loc.PickupHealthVial));
                return true;
            case PickupKind.HealthPack:
                if (pawn.Health >= 100f) return false;
                pawn.GiveHealth(25f, 100f);
                fb?.Pickup(Loc.PickedUp(Loc.PickupHealthPack));
                return true;
            case PickupKind.SuperHealth:
                if (pawn.Health >= 199f) return false;
                pawn.GiveHealth(100f, 199f);
                fb?.Pickup(Loc.PickedUp(Loc.PickupSuperHealth));
                return true;
            case PickupKind.ThighPads:
                if (pawn.Armor >= 150f) return false;
                pawn.GiveArmor(30f, 150f);
                fb?.Pickup(Loc.PickedUp(Loc.PickupThighPads));
                return true;
            case PickupKind.BodyArmor:
                if (pawn.Armor >= 150f) return false;
                pawn.GiveArmor(80f, 150f);
                fb?.Pickup(Loc.PickedUp(Loc.PickupBodyArmor));
                return true;
            case PickupKind.ShieldBelt:
                if (pawn.HasShieldBelt && pawn.Armor >= 150f) return false;
                pawn.GiveArmor(150f, 150f, shieldBelt: true);
                fb?.Pickup(Loc.PickedUp(Loc.PickupShieldBelt));
                return true;
            case PickupKind.DamageAmp:
                pawn.DamageAmpTime = 30f;
                fb?.Pickup(Loc.PickedUp(Loc.PickupDamageAmp));
                return true;
            case PickupKind.Invisibility:
                pawn.InvisibilityTime = 28f;
                fb?.Pickup(Loc.PickedUp(Loc.PickupInvisibility));
                return true;
            case PickupKind.JumpBoots:
                pawn.JumpBootCharges = 3;
                fb?.Pickup(Loc.PickedUp(Loc.PickupJumpBoots));
                return true;
            case PickupKind.WeaponPickup:
                {
                    if (Mode.Kind == GameModeKind.Instagib) return false;
                    if (!pawn.GiveWeapon(pu.Weapon)) return false;
                    fb?.Pickup(Loc.PickedUp(GameTypes.WeaponName(pu.Weapon)));
                    return true;
                }
            case PickupKind.AmmoPickup:
                {
                    if (Mode.Kind == GameModeKind.Instagib) return false;
                    int amount = AmmoPickupAmount(pu.Ammo);
                    if (!pawn.GiveAmmo(pu.Ammo, amount)) return false;
                    fb?.Pickup(Loc.PickedUp(Loc.PickupAmmo));
                    return true;
                }
            default:
                return false;
        }
    }

    private static int AmmoPickupAmount(AmmoKind kind)
    {
        for (int i = 0; i < (int)WeaponKind.Count; i++)
            if (Weapons.All[i].Ammo == kind) return Math.Max(5, Weapons.All[i].PickupAmmo);
        return 10;
    }

    // ---------------------------------------------------------------- flags (CTF)

    private void UpdateCarriedFlag(Pawn pawn)
    {
        if (Mode.Kind != GameModeKind.CaptureTheFlag
            || Mode.State is MatchState.Warmup or MatchState.Finished) return;

        foreach (var team in FlagHome.Keys)
        {
            if (FlagCarrier[team] == pawn.Id)
            {
                FlagPosition[team] = pawn.Position;
                continue;
            }
            if (FlagCarrier[team] >= 0) continue;

            float dist = Vector3.Distance(pawn.Position, FlagPosition[team]);
            if (dist > 1.7f) continue;

            bool atHome = Vector3.Distance(FlagPosition[team], FlagHome[team]) < 0.4f;
            if (team == pawn.Team)
            {
                if (!atHome)
                {
                    ReturnFlag(team, pawn.Position);
                }
                else if (pawn.HasFlag && pawn.CarriedFlag != pawn.Team)
                {
                    // Standing on your own flag while holding theirs scores a capture.
                    Team enemy = pawn.CarriedFlag;
                    FlagCarrier[enemy] = -1;
                    FlagPosition[enemy] = FlagHome[enemy];
                    FlagDroppedTimer[enemy] = 0f;
                    pawn.HasFlag = false;
                    pawn.CarriedFlag = Team.None;
                    pawn.Captures++;
                    Mode.OnCapture(this, pawn);
                    Broadcast(pawn.Team == Team.Red ? Loc.AnnRedScores : Loc.AnnBlueScores,
                        GameTypes.TeamColor(pawn.Team), 2.4f);
                    AddKillFeed($"{pawn.Name} {Loc.HudFlagCaptured}", GameTypes.TeamColor(pawn.Team));
                    OnSound?.Invoke(SoundId.FlagCapture, pawn.Position, 1.4f);
                }
            }
            else if (!pawn.HasFlag)
            {
                FlagCarrier[team] = pawn.Id;
                FlagDroppedTimer[team] = 0f;
                pawn.HasFlag = true;
                pawn.CarriedFlag = team;
                Broadcast(team == Team.Red ? Loc.AnnFlagTakenRed : Loc.AnnFlagTakenBlue,
                    GameTypes.TeamColor(team), 2.0f);
                OnSound?.Invoke(SoundId.FlagTaken, pawn.Position, 1.2f);
            }
        }
    }

    /// <summary>
    /// Onslaught. Standing at a node builds, tears down or attacks it depending on who owns it
    /// and whether the chain reaches — the reachability test is what makes this the mode rather
    /// than Domination with extra steps.
    /// </summary>
    private void UpdateOnslaught(float dt)
    {
        if (Mode.Kind != GameModeKind.Onslaught) return;
        if (Mode.State is MatchState.Warmup or MatchState.Finished) return;
        var state = Onslaught;

        for (int i = 0; i < state.Nodes.Count; i++)
        {
            var node = state.Nodes[i];
            float reach = node.IsCore ? 6f : 4.5f;

            // Building stacks — more link guns raise a node faster, as in the original — but
            // tearing one down does not, or four bodies standing on a 2000-point node flatten it
            // in a second and the mode has no defence worth mounting.
            bool hostileHandled = false;
            foreach (var pawn in Pawns)
            {
                if (!pawn.Alive || pawn.Team == Team.None) continue;
                Vector3 d = pawn.Position - node.Position;
                if (MathF.Abs(d.Y) > 6f) continue;
                if (new Vector2(d.X, d.Z).LengthSquared() > reach * reach) continue;

                bool hostile = node.IsCore || (node.Team != Team.None && node.Team != pawn.Team);
                if (hostile)
                {
                    if (hostileHandled) continue;
                    hostileHandled = true;
                }

                var evt = state.Touch(i, pawn.Team, dt, out var touched);
                if (HandleNodeEvent(evt, touched, pawn)) return;
            }
        }
    }

    /// <summary>
    /// Shared reaction to a node changing state, whether it was built by hand or shelled from a
    /// kilometre away. Returns true if the match just ended, so callers stop iterating.
    /// </summary>
    private bool HandleNodeEvent(NodeEvent evt, PowerNode node, Pawn actor)
    {
        if (node == null || actor == null) return false;
        switch (evt)
        {
            case NodeEvent.Captured:
                actor.Captures++;
                AddKillFeed(Loc.OnsNodeCaptured(actor.Name, node.Name), GameTypes.TeamColor(actor.Team));
                OnSound?.Invoke(SoundId.FlagCapture, node.Position, 1f);
                AnnounceCoreState();
                return false;

            case NodeEvent.Neutralised:
                AddKillFeed(Loc.OnsNodeLost(node.Name), new Vector3(1f, 0.7f, 0.3f));
                OnSound?.Invoke(SoundId.Explosion, node.Position, 1f);
                Particles.Explosion(node.Position + MathX.Up * 2f, 2.6f, new Vector3(1f, 0.6f, 0.2f));
                AnnounceCoreState();
                return false;

            case NodeEvent.CoreDestroyed:
                // Two points in regulation, one in sudden death, per the original.
                Mode.TeamScores[(int)actor.Team] += Mode.State == MatchState.Overtime ? 1 : 2;
                Mode.WinningTeam = actor.Team;
                Particles.Explosion(node.Position + MathX.Up * 3f, 5f, new Vector3(1f, 0.75f, 0.3f));
                OnSound?.Invoke(SoundId.Nuke, node.Position, 2f);
                Mode.Finish(this);
                return true;
        }
        return false;
    }

    private Team _lastVulnerable = Team.None;

    /// <summary>Tells each side when the balance of the chain has actually changed.</summary>
    private void AnnounceCoreState()
    {
        Team exposed = Team.None;
        if (Onslaught.CoreVulnerable(Team.Red)) exposed = Team.Red;
        else if (Onslaught.CoreVulnerable(Team.Blue)) exposed = Team.Blue;
        if (exposed == _lastVulnerable) return;
        _lastVulnerable = exposed;
        if (exposed == Team.None) return;

        foreach (var viewer in Pawns)
        {
            if (viewer.PlayerIndex < 0) continue;
            bool ours = viewer.Team == exposed;
            FeedbackFor(viewer).Big(ours ? Loc.OnsOurCoreExposed : Loc.OnsEnemyCoreExposed,
                ours ? new Vector3(1f, 0.35f, 0.25f) : new Vector3(0.4f, 1f, 0.5f), 2f);
        }
        OnSound?.Invoke(SoundId.AnnounceMajor, Vector3.Zero, 1.1f);
    }

    /// <summary>
    /// Assault. The clock is the scoreboard here: round one sets a time, round two has to beat
    /// it. The objective sequence advances one step at a time and pushes the attackers' spawns
    /// forward as it does, which is what keeps the last objective winnable.
    /// </summary>
    private void UpdateAssault(float dt)
    {
        if (Mode.Kind != GameModeKind.Assault) return;
        if (Mode.State is MatchState.Warmup or MatchState.Finished) return;
        var st = Assault;
        st.Elapsed += dt;

        var target = st.CurrentObjective;
        if (target != null && target.Kind != ObjectiveKind.Destroy)
        {
            // A defender inside the ring stalls the plant; that contest is the fight the mode
            // is actually about, so it is resolved before anyone's progress ticks.
            bool defenderPresent = false;
            foreach (var p in Pawns)
            {
                if (!p.Alive || p.Team != st.Defenders) continue;
                if (Vector3.Distance(p.Position, target.Position) <= target.Radius) { defenderPresent = true; break; }
            }

            // Exactly one attacker advances the objective per frame. Charges are planted by a
            // person, not by a crowd — letting every body in the ring add its own dt would make
            // a four-man rush complete a nine-second plant in two.
            Pawn planter = null;
            foreach (var pawn in Pawns)
            {
                if (!pawn.Alive || pawn.Team != st.Attackers) continue;
                if (Vector3.Distance(pawn.Position, target.Position) > target.Radius) continue;
                planter = pawn;
                break;
            }

            if (planter != null)
            {
                var evt = st.Touch(planter.Team, planter.Position, defenderPresent, dt, out var touched);
                if (evt == ObjectiveEvent.Progress && defenderPresent)
                    foreach (var pawn in Pawns)
                        if (pawn.PlayerIndex >= 0 && pawn.Team == st.Attackers)
                            FeedbackFor(pawn).Sub(Loc.AsContested, 0.4f);
                if (HandleObjectiveEvent(evt, touched, planter)) return;
            }
        }

        // Out of time: the attackers failed this round.
        if (Mode.TimeLimit > 0f && Mode.TimeRemaining <= 0f) FinishAssaultRound(false);
    }

    /// <summary>
    /// Shared reaction to an objective advancing, whether it was stood on or shot. Returns true
    /// if the round ended.
    /// </summary>
    private bool HandleObjectiveEvent(ObjectiveEvent evt, AssaultObjective objective, Pawn actor)
    {
        if (objective == null || actor == null) return false;
        switch (evt)
        {
            case ObjectiveEvent.Completed:
                actor.Captures++;
                AddKillFeed(Loc.AsObjectiveDone(actor.Name, objective.Name), GameTypes.TeamColor(actor.Team));
                OnSound?.Invoke(SoundId.FlagCapture, objective.Position, 1.2f);
                Particles.EnergyBurst(objective.Position + MathX.Up * 1.5f, new Vector3(1f, 0.8f, 0.35f), 2.4f);
                Broadcast(Loc.AsNextObjective(Assault.CurrentObjective?.Name ?? ""),
                    new Vector3(1f, 0.85f, 0.4f), 2f);
                return false;

            case ObjectiveEvent.AllCompleted:
                actor.Captures++;
                AddKillFeed(Loc.AsObjectiveDone(actor.Name, objective.Name), GameTypes.TeamColor(actor.Team));
                Broadcast(Loc.AsObjectivesCleared, GameTypes.TeamColor(actor.Team), 3f);
                OnSound?.Invoke(SoundId.AnnounceMajor, Vector3.Zero, 1.4f);
                FinishAssaultRound(true);
                return true;
        }
        return false;
    }

    /// <summary>
    /// Ends an Assault round. Round one turns the map around; round two decides the match on the
    /// time comparison, falling back to how far each side got when neither finished.
    /// </summary>
    private void FinishAssaultRound(bool attackersFinished)
    {
        var st = Assault;
        if (st.Round == 1)
        {
            st.SwapSides(attackersFinished);
            // Everyone changes ends, so everyone respawns; the clock restarts for the new side.
            foreach (var p in Pawns)
            {
                if (p.InVehicle) ExitVehicle(p);
                RespawnPawn(p);
            }
            foreach (var v in Vehicles) v.Reset();
            Mode.TimeRemaining = Mode.TimeLimit;
            Broadcast(Loc.AsSidesSwapped, new Vector3(1f, 0.85f, 0.35f), 3f);
            OnSound?.Invoke(SoundId.AnnounceMajor, Vector3.Zero, 1.3f);
            return;
        }

        Team winner = st.ResolveWinner(attackersFinished);
        if (winner != Team.None) Mode.TeamScores[(int)winner] += 1;
        Mode.WinningTeam = winner;
        Mode.Finish(this);
    }

    /// <summary>
    /// Drives every vehicle from its driver's input, carries the crew, handles the special
    /// states, and crushes anyone the heavy ones run into.
    /// </summary>
    private void UpdateVehicles(float dt)
    {
        foreach (var v in Vehicles)
        {
            if (!v.Alive)
            {
                v.RespawnTimer -= dt;
                if (v.RespawnTimer <= 0f)
                {
                    v.Reset();
                    Particles.EnergyBurst(v.Position + new Vector3(0, 1f, 0), new Vector3(0.6f, 0.85f, 1f), 1.4f);
                }
                continue;
            }

            var def = v.Def;
            for (int s = 0; s < v.SeatCooldown.Length; s++)
                v.SeatCooldown[s] = MathF.Max(0f, v.SeatCooldown[s] - dt);

            // Evict anyone who died in their seat.
            for (int s = 0; s < v.Occupants.Length; s++)
            {
                if (v.Occupants[s] < 0) continue;
                var occupant = FindPawn(v.Occupants[s]);
                if (occupant == null || !occupant.Alive)
                {
                    if (occupant != null) { occupant.VehicleId = -1; occupant.VehicleSeat = -1; }
                    v.Occupants[s] = -1;
                }
            }

            var driver = v.Driver >= 0 ? FindPawn(v.Driver) : null;
            Vector2 drive = Vector2.Zero;
            bool up = false, down = false;
            if (driver != null)
            {
                drive = driver.VehicleDrive;
                up = driver.VehicleUp;
                down = driver.VehicleDown;
                // A non-turret driver seat aims by steering: the hull is the weapon mount.
                if (!def.Seats[0].Turret) v.SeatYaw[0] = v.Yaw;
            }

            // Deploy transition. It must finish before the heavy weapon is available, and the
            // vehicle cannot move throughout — that window is the price of the firepower.
            if (def.CanDeploy && v.Deploying)
            {
                v.Deploy = MathX.Saturate(v.Deploy + dt / MathF.Max(def.DeploySeconds, 0.1f));
                if (v.Deploy >= 1f) v.Deploying = false;
            }

            if (def.CanCloak)
                v.CloakBlend = MathX.Damp(v.CloakBlend, v.Velocity.LengthSquared() < 1f ? 1f : 0f, 3f, dt);

            if (v.SelfDestructTimer > 0f)
            {
                v.SelfDestructTimer -= dt;
                if (v.SelfDestructTimer <= 0f) DetonateVehicle(v);
            }

            v.Move(Level, drive, up, down, dt);

            // Carry the crew.
            for (int s = 0; s < v.Occupants.Length; s++)
            {
                if (v.Occupants[s] < 0) continue;
                var rider = FindPawn(v.Occupants[s]);
                if (rider == null) continue;
                rider.Position = v.SeatWorld(s);
                rider.Velocity = v.Velocity;
                rider.OnGround = true;
            }

            // Crush anyone on foot the heavy ones run into.
            if (def.Crushes && v.Velocity.LengthSquared() > 36f)
            {
                float reach = MathF.Max(def.HalfExtents.X, def.HalfExtents.Z) + Physics.PawnRadius;
                foreach (var p in Pawns)
                {
                    if (!p.Alive || p.VehicleId >= 0) continue;
                    if (Vector3.DistanceSquared(p.Position, v.Position) > reach * reach) continue;
                    if (MathF.Abs(p.Position.Y - v.Position.Y) > def.HalfExtents.Y + 1.4f) continue;
                    Damage(p, driver, 160f, DamageType.Explosion,
                        MathX.SafeNormalize(v.Velocity, MathX.Forward), false);
                }
            }
        }
    }

    /// <summary>
    /// Firing from a seat. Turret seats aim where the occupant looks, independently of where
    /// the hull points; the driver of a hull-mounted weapon aims by steering. Unarmed seats —
    /// the Hellbender's driver, the hoverboard — simply cannot shoot, which is the point of them.
    /// </summary>
    private void HandleVehicleFire(Pawn pawn, in PawnInput input, float dt)
    {
        var v = FindVehicle(pawn.VehicleId);
        if (v == null || !v.Alive) return;
        int seat = pawn.VehicleSeat;
        if (seat < 0 || seat >= v.Def.Seats.Length) return;
        var seatDef = v.Def.Seats[seat];

        // A turret seat aims freely. A hull-mounted weapon takes its yaw from the vehicle — you
        // aim it by steering — but its elevation still follows the occupant's view, otherwise a
        // Manta could never shoot at anything that is not exactly level with it.
        if (seatDef.Turret) { v.SeatYaw[seat] = input.Yaw; v.SeatPitch[seat] = input.Pitch; }
        else v.SeatPitch[seat] = MathX.Clamp(input.Pitch, -0.9f, 0.9f);

        // Special alt-fire behaviours that are states rather than shots.
        var def = v.Def;
        if (def.HasShield && seat == 0)
        {
            v.ShieldUp = input.AltFire;
            if (v.ShieldUp && v.ShieldHealth <= 0f) v.ShieldHealth = 600f;
            if (input.AltFire) return;
        }
        if (def.CanDeploy && seat == 0 && input.AltFire && !v.Deploying && v.Deploy <= 0f)
        {
            v.Deploying = true;
            OnSound?.Invoke(SoundId.Respawn, v.Position, 0.9f);
            return;
        }
        if (def.CanSelfDestruct && seat == 0 && input.AltFire && v.SelfDestructTimer < 0f)
        {
            v.SelfDestructTimer = 1.2f;
            return;
        }

        if (!seatDef.Armed) return;
        bool alt = input.AltFire;
        FireDef fire = alt ? seatDef.Alt : seatDef.Primary;
        if (fire.Interval <= 0f) return;
        if (!(alt ? input.AltFire : input.Fire)) return;
        if (v.SeatCooldown[seat] > 0f) return;

        // A deployed Leviathan fires the Ion Cannon from the driver's seat instead.
        if (def.CanDeploy && seat == 0 && v.Deploy >= 1f) fire = seatDef.Alt;

        v.SeatCooldown[seat] = fire.Interval;
        Vector3 origin = v.SeatWorld(seat) + new Vector3(0f, 0.4f, 0f);
        Vector3 aim = seatDef.Turret
            ? MathX.DirFromYawPitch(v.SeatYaw[seat], v.SeatPitch[seat])
            : MathX.DirFromYawPitch(v.Yaw, v.SeatPitch[seat]);

        switch (fire.Mode)
        {
            case FireMode.Hitscan:
                HitscanShot(pawn, origin, aim, fire, 1f, def.Tint);
                break;
            case FireMode.Projectile:
                SpawnProjectile(fire.Projectile, fire, origin, aim, pawn, 1f, def.Tint);
                break;
            case FireMode.Melee:
                MeleeSwing(pawn, origin, aim, fire, 1f);
                break;
        }
        pawn.ShotsFired++;
        OnSound?.Invoke(WeaponSound(WeaponKind.RocketLauncher, alt), v.Position, 0.9f);
    }

    public void DamageVehicle(Vehicle v, Pawn attacker, float amount)
    {
        if (v == null || !v.Alive) return;
        // The Paladin's shield absorbs before the hull takes anything.
        if (v.ShieldUp && v.ShieldHealth > 0f)
        {
            float absorbed = MathF.Min(v.ShieldHealth, amount);
            v.ShieldHealth -= absorbed;
            amount -= absorbed;
            if (amount <= 0f) return;
        }
        v.Health -= amount;
        if (v.Health <= 0f) DetonateVehicle(v, attacker);
    }

    private void DetonateVehicle(Vehicle v, Pawn attacker = null)
    {
        var def = v.Def;
        v.Alive = false;
        v.RespawnTimer = 30f;
        Particles.Explosion(v.Position, MathF.Max(2.5f, def.HalfExtents.Length()));
        OnSound?.Invoke(SoundId.Explosion, v.Position, 1.3f);
        // Everyone aboard goes up with it, and so does anyone standing too close.
        float radius = def.HalfExtents.Length() + 5f;
        var caught = new List<Pawn>(Pawns);
        foreach (var p in caught)
        {
            if (!p.Alive) continue;
            if (p.VehicleId == v.Id) { ExitVehicle(p); Kill(p, attacker, DamageType.Explosion); continue; }
            float d = Vector3.Distance(p.Position, v.Position);
            if (d > radius) continue;
            Damage(p, attacker, 140f * (1f - d / radius), DamageType.Explosion,
                MathX.SafeNormalize(p.Position - v.Position, MathX.Up), false);
        }
    }

    /// <summary>
    /// Domination capture. Entering a point takes it — there is no channel, no timer and no
    /// requirement to stand and hold. This deliberately models an edge-triggered Touch event:
    /// players who remain inside do not trade the same point every frame when both teams overlap.
    /// </summary>
    private void UpdateControlPoints(float dt)
    {
        if (Mode.Kind != GameModeKind.Domination) return;
        var points = Level.ControlPoints;
        _nextControlPointContacts.Clear();

        for (int i = 0; i < points.Count && i < ControlPointOwners.Count; i++)
        {
            ControlPointSince[i] += dt;
            var point = points[i];

            foreach (var pawn in Pawns)
            {
                if (!pawn.Alive || pawn.Team == Team.None) continue;
                // Generous vertically so standing on the dais counts, tight horizontally so you
                // cannot take a point by brushing past it in a corridor.
                Vector3 d = pawn.Position - point.Position;
                if (MathF.Abs(d.Y) > 2.6f) continue;
                if (new Vector2(d.X, d.Z).LengthSquared() > point.Radius * point.Radius) continue;

                long contact = ControlPointContactKey(i, pawn.Id);
                _nextControlPointContacts.Add(contact);
                bool entered = !_controlPointContacts.Contains(contact);
                if (!entered || pawn.Team == ControlPointOwners[i]) continue;

                Team previous = ControlPointOwners[i];
                ControlPointOwners[i] = pawn.Team;
                ControlPointSince[i] = 0f;
                ControlPointControllers[i] = pawn.Id;
                ControlPointCaptures[i]++;
                pawn.Captures++;

                AddKillFeed(Loc.DomCaptured(pawn.Name, point.Name), GameTypes.TeamColor(pawn.Team));
                OnSound?.Invoke(SoundId.FlagCapture, point.Position, 1f);
                foreach (var viewer in Pawns)
                {
                    if (viewer.PlayerIndex < 0) continue;
                    if (viewer.Team == pawn.Team)
                        FeedbackFor(viewer).Sub($"{Loc.AnnDomTaken}：{point.Name}", 1.6f);
                    else if (viewer.Team == previous)
                        FeedbackFor(viewer).Sub($"{Loc.AnnDomLost}：{point.Name}", 1.6f);
                }
            }
        }

        (_controlPointContacts, _nextControlPointContacts) =
            (_nextControlPointContacts, _controlPointContacts);
    }

    private static long ControlPointContactKey(int pointIndex, int pawnId)
        => ((long)pointIndex << 32) | (uint)pawnId;

    /// <summary>
    /// Marks every pawn currently inside a control point as an existing contact. Save restoration
    /// calls this after replacing pawn positions so resuming does not manufacture a new capture.
    /// </summary>
    public void SynchronizeControlPointContacts()
    {
        _controlPointContacts.Clear();
        for (int i = 0; i < Level.ControlPoints.Count; i++)
        {
            ControlPoint point = Level.ControlPoints[i];
            foreach (Pawn pawn in Pawns)
            {
                if (!pawn.Alive || pawn.Team == Team.None) continue;
                Vector3 d = pawn.Position - point.Position;
                if (MathF.Abs(d.Y) <= 2.6f
                    && new Vector2(d.X, d.Z).LengthSquared() <= point.Radius * point.Radius)
                    _controlPointContacts.Add(ControlPointContactKey(i, pawn.Id));
            }
        }
    }

    private void UpdateFlags(float dt)
    {
        if (Mode.Kind != GameModeKind.CaptureTheFlag) return;
        foreach (var team in FlagHome.Keys.ToArray())
        {
            if (FlagCarrier[team] >= 0) continue;
            if (Vector3.Distance(FlagPosition[team], FlagHome[team]) < 0.4f) continue;
            FlagDroppedTimer[team] += dt;
            if (FlagDroppedTimer[team] > 15f)
            {
                ReturnFlag(team, FlagPosition[team]);
            }
        }
    }

    private void DropFlag(Pawn pawn, DamageType deathType)
    {
        if (!pawn.HasFlag) return;
        Team team = pawn.CarriedFlag;
        if (team == Team.None)
        {
            foreach (var entry in FlagCarrier)
                if (entry.Value == pawn.Id) { team = entry.Key; break; }
        }
        if (team == Team.None || !FlagHome.ContainsKey(team))
        {
            pawn.HasFlag = false;
            pawn.CarriedFlag = Team.None;
            return;
        }

        FlagCarrier[team] = -1;
        float floor = Level.Collision.FloorHeight(pawn.Position + new Vector3(0, 1f, 0));
        bool outsidePlay = deathType is DamageType.Void or DamageType.Lava
            || float.IsNaN(floor) || pawn.Position.Y <= Level.KillPlaneY + 0.5f;
        if (outsidePlay)
        {
            pawn.HasFlag = false;
            pawn.CarriedFlag = Team.None;
            ReturnFlag(team, FlagHome[team]);
            return;
        }

        FlagPosition[team] = new Vector3(pawn.Position.X, float.IsNaN(floor) ? pawn.Position.Y : floor,
            pawn.Position.Z);
        FlagDroppedTimer[team] = 0f;
        pawn.HasFlag = false;
        pawn.CarriedFlag = Team.None;
        Broadcast(Loc.HudFlagDropped, GameTypes.TeamColor(team), 1.4f);
        OnSound?.Invoke(SoundId.FlagDrop, pawn.Position, 0.9f);
    }

    private void ReturnFlag(Team team, Vector3 soundPosition)
    {
        if (!FlagHome.TryGetValue(team, out Vector3 home)) return;
        FlagCarrier[team] = -1;
        FlagPosition[team] = home;
        FlagDroppedTimer[team] = 0f;
        Broadcast(team == Team.Red ? Loc.AnnRedFlagReturned : Loc.AnnBlueFlagReturned,
            GameTypes.TeamColor(team), 1.8f);
        OnSound?.Invoke(SoundId.FlagReturn, soundPosition, 1f);
    }

    // ---------------------------------------------------------------- rendering

    /// <summary>Fills the render scene with the level, every pawn, projectile, pickup and light.</summary>
    public void Submit(RenderScene scene, int viewCount, IReadOnlyList<int> viewPawnIds)
    {
        scene.Time = Time;
        Level.Environment.ApplyTo(scene);
        Level.Submit(scene, Materials, Time);

        SubmitPawns(scene, viewPawnIds);
        SubmitProjectiles(scene);
        SubmitPickups(scene);
        SubmitFlags(scene);
        SubmitVehicles(scene);
        SubmitControlPoints(scene);
        SubmitPowerNodes(scene);
        SubmitObjectives(scene);
        _ = viewCount;
    }

    private void SubmitPawns(RenderScene scene, IReadOnlyList<int> viewPawnIds)
    {
        foreach (var pawn in Pawns)
        {
            if (!pawn.Alive && pawn.DeathTime > 6f) continue;

            var world = _boneWorld[pawn.Id];
            var skin = _boneSkin[pawn.Id];

            Vector3 localMove = new(
                Vector3.Dot(pawn.Velocity, pawn.RightFlat) / Physics.GroundSpeed, 0f,
                Vector3.Dot(pawn.Velocity, pawn.ForwardFlat) / Physics.GroundSpeed);

            var pose = new PoseInput
            {
                Time = pawn.ViewBobPhase,
                Speed = pawn.Speed,
                LocalMove = localMove,
                InAir = !pawn.OnGround,
                Crouching = pawn.Crouching,
                AimPitch = pawn.Pitch,
                FireBlend = pawn.FireBlend,
                DodgeBlend = pawn.DodgeBlend,
                DeathTime = pawn.Alive ? 0f : pawn.DeathTime,
                LandBlend = pawn.LandBlend,
                Health01 = MathX.Saturate(pawn.Health / 100f),
            };

            // Dead pawns topple at the root so the body actually ends up lying on the floor;
            // the per-bone death pose only adds the limp detail.
            Matrix4x4 root;
            if (pawn.Alive)
            {
                root = Matrix4x4.CreateRotationY(pawn.Yaw) * Matrix4x4.CreateTranslation(pawn.Position);
            }
            else
            {
                float t = MathX.Saturate(pawn.DeathTime / 0.9f);
                float fall = 1f - (1f - t) * (1f - t);
                float lift = MathF.Sin(fall * MathX.Pi) * 0.12f + fall * 0.24f;
                root = Matrix4x4.CreateRotationX(1.48f * fall)
                     * Matrix4x4.CreateRotationY(pawn.Yaw)
                     * Matrix4x4.CreateTranslation(pawn.Position + new Vector3(0, lift, 0));
            }
            _character.Animate(pose, root, world, skin);

            bool isOwnView = viewPawnIds.Contains(pawn.Id);
            if (isOwnView && pawn.Alive) continue;      // first-person: body is not drawn for its own view
            if (pawn.Gibbed && !pawn.Alive) continue;

            int boneBase = scene.AddBones(skin);
            Vector3 tint = Mode.TeamBased ? GameTypes.TeamColor(pawn.Team) : pawn.AccentColor;
            float alpha = pawn.IsInvisible ? 0.16f : 1f;
            Vector3 emissive = pawn.HasDamageAmp
                ? new Vector3(1.4f, 0.25f, 0.15f)
                : (pawn.HasShieldBelt ? new Vector3(0.5f, 0.15f, 0.9f) : Vector3.Zero);

            foreach (var section in _character.Sections)
            {
                Material mat = Materials.Get(section.Material);
                bool bodyPlate = section.Material == (int)MatId.ArmorPlate;
                var dc = new DrawCall
                {
                    Mesh = _character.Mesh,
                    IndexOffset = section.IndexOffset,
                    IndexCount = section.IndexCount,
                    Material = mat,
                    Transform = Matrix4x4.Identity,
                    BoneBase = boneBase,
                    BoneCount = (int)Bone.Count,
                    Tint = bodyPlate ? new Vector4(tint * 1.15f, 1f) : mat.BaseColor,
                    Emissive = section.Material == (int)MatId.EnergyPanel
                        ? new Vector3(tint.X * 2.2f + 0.4f, tint.Y * 2.2f + 0.6f, tint.Z * 2.2f + 1.0f)
                        : emissive,
                    OverrideEmissive = true,
                    Alpha = alpha,
                    Center = pawn.Center,
                    Radius = 1.4f,
                    CastShadow = !pawn.IsInvisible,
                    RimStrength = 0.55f,
                    RimColor = tint * 0.9f,
                    UvScale = mat.UvScale,
                    OwnerView = -1,
                };
                if (alpha < 0.999f) scene.Transparent.Add(dc); else scene.Opaque.Add(dc);
            }

            // Third-person weapon in the right hand.
            if (pawn.Alive)
            {
                // Anchor to the hand's position but orient by the aim, so the muzzle always
                // points where shots actually go rather than wherever the arm animation lands.
                Matrix4x4 hand = world[(int)Bone.HandR];
                Vector3 handPos = new(hand.M41, hand.M42, hand.M43);
                Matrix4x4 grip = Matrix4x4.CreateRotationX(pawn.Pitch)
                               * Matrix4x4.CreateRotationY(pawn.Yaw)
                               * Matrix4x4.CreateTranslation(handPos);
                var wMesh = _weaponModels.MeshFor(pawn.Weapon);
                foreach (var section in _weaponModels.SectionsFor(pawn.Weapon))
                {
                    Material mat = Materials.Get(section.Material);
                    scene.Opaque.Add(new DrawCall
                    {
                        Mesh = wMesh,
                        IndexOffset = section.IndexOffset,
                        IndexCount = section.IndexCount,
                        Material = mat,
                        Transform = grip,
                        BoneBase = -1,
                        Tint = mat.BaseColor,
                        Emissive = mat.Emissive,
                        Alpha = alpha,
                        Center = pawn.Center,
                        Radius = 1.6f,
                        CastShadow = false,
                        UvScale = mat.UvScale,
                        OwnerView = -1,
                    });
                }
            }

            // Power-up auras read at a glance in a firefight.
            if (pawn.HasDamageAmp)
                scene.AddLight(pawn.Center, 5.5f, new Vector3(1f, 0.25f, 0.15f), 2.6f, 1.6f);
            if (pawn.HasShieldBelt)
                scene.AddLight(pawn.Center, 4.5f, new Vector3(0.6f, 0.2f, 1f), 1.8f, 1.4f);
            if (pawn.FiringBeam)
                scene.AddLight(pawn.MuzzleWorld(), 8f, new Vector3(0.4f, 1f, 0.6f), 5f, 2.5f);
        }
    }

    private void SubmitProjectiles(RenderScene scene)
    {
        for (int i = 0; i < Projectiles.Length; i++)
        {
            ref Projectile p = ref Projectiles[i];
            if (!p.Active) continue;

            float lightRadius = p.Kind switch
            {
                ProjectileKind.Warhead => 20f,
                ProjectileKind.Rocket => 11f,
                ProjectileKind.ShockBall => 10f,
                ProjectileKind.FlakShell => 7f,
                ProjectileKind.PlasmaBolt => 6f,
                ProjectileKind.BioGlob => 6f,
                _ => 4.5f,
            };
            float lightIntensity = p.Kind switch
            {
                ProjectileKind.Warhead => 10f,
                ProjectileKind.Rocket => 6f,
                ProjectileKind.ShockBall => 5.5f,
                _ => 3f,
            };
            scene.AddLight(p.Position, lightRadius, p.Color, lightIntensity, 2.2f);

            var mesh = _projectileModels.MeshFor(p.Kind);
            if (mesh == null)
            {
                // Small energy projectiles are pure particles; a bright sprite is enough.
                Particles.Spawn(BlendMode.Additive, p.Position, Vector3.Zero,
                    new Vector4(p.Color * 4f, 1f), new Vector4(p.Color * 4f, 1f),
                    p.Radius * 3.4f, p.Radius * 3.4f, 0.02f,
                    p.Kind == ProjectileKind.ShockBall ? Spr.Swirl : Spr.Plasma);
                continue;
            }

            Vector3 dir = MathX.SafeNormalize(p.Velocity, MathX.Up);
            Matrix4x4 orient = p.Kind == ProjectileKind.RipperBlade
                ? Matrix4x4.CreateRotationY(Time * p.Spin) * AlignYTo(dir)
                : Matrix4x4.CreateRotationY(Time * p.Spin) * AlignYTo(dir);
            Matrix4x4 xf = orient * Matrix4x4.CreateTranslation(p.Position);

            foreach (var section in _projectileModels.SectionsFor(p.Kind))
            {
                Material mat = Materials.Get(section.Material);
                scene.Opaque.Add(new DrawCall
                {
                    Mesh = mesh,
                    IndexOffset = section.IndexOffset,
                    IndexCount = section.IndexCount,
                    Material = mat,
                    Transform = xf,
                    BoneBase = -1,
                    Tint = mat.BaseColor,
                    Emissive = mat.Emissive,
                    Alpha = 1f,
                    Center = p.Position,
                    Radius = 0.8f,
                    CastShadow = false,
                    UvScale = mat.UvScale,
                    OwnerView = -1,
                });
            }
        }
    }

    /// <summary>Builds a rotation that maps local +Y onto <paramref name="dir"/>.</summary>
    private static Matrix4x4 AlignYTo(Vector3 dir)
    {
        Vector3 up = MathX.SafeNormalize(dir, MathX.Up);
        MathX.OrthoBasis(up, out Vector3 right, out Vector3 fwd);
        Matrix4x4 m = Matrix4x4.Identity;
        m.M11 = right.X; m.M12 = right.Y; m.M13 = right.Z;
        m.M21 = up.X; m.M22 = up.Y; m.M23 = up.Z;
        m.M31 = fwd.X; m.M32 = fwd.Y; m.M33 = fwd.Z;
        return m;
    }

    private void SubmitPickups(RenderScene scene)
    {
        foreach (var pu in Pickups)
        {
            if (!pu.Active) continue;
            float bob = MathF.Sin(pu.Phase * 1.9f) * 0.10f;
            float spin = pu.Phase * 1.15f;
            float scale = MathX.Lerp(0.2f, 1f, MathX.SmoothStep(0f, 1f, pu.SpawnBlend));

            Matrix4x4 xf = Matrix4x4.CreateScale(scale)
                         * Matrix4x4.CreateRotationY(spin)
                         * Matrix4x4.CreateTranslation(pu.Position + new Vector3(0, bob + 0.15f, 0));

            if (pu.Kind == PickupKind.WeaponPickup)
            {
                // Keep the weapon upright so its silhouette stays readable as it slowly rotates.
                Matrix4x4 wxf = Matrix4x4.CreateScale(scale * 1.25f)
                              * Matrix4x4.CreateRotationY(spin)
                              * Matrix4x4.CreateTranslation(pu.Position + new Vector3(0, bob + 0.55f, 0));
                var wm = _weaponModels.MeshFor(pu.Weapon);
                foreach (var section in _weaponModels.SectionsFor(pu.Weapon))
                {
                    Material mat = Materials.Get(section.Material);
                    scene.Opaque.Add(MakePickupDraw(wm, section, mat, wxf, pu.Position, 1.4f));
                }
            }
            else if (pu.Kind == PickupKind.AmmoPickup)
            {
                foreach (var section in _pickupModels.SectionsFor(PickupKind.AmmoPickup))
                {
                    Material mat = Materials.Get(section.Material);
                    scene.Opaque.Add(MakePickupDraw(_pickupModels.MeshFor(PickupKind.AmmoPickup), section,
                        mat, xf, pu.Position, 0.9f));
                }
            }

            var mesh = _pickupModels.MeshFor(pu.Kind);
            if (mesh != null && pu.Kind != PickupKind.AmmoPickup)
            {
                foreach (var section in _pickupModels.SectionsFor(pu.Kind))
                {
                    Material mat = Materials.Get(section.Material);
                    var dc = MakePickupDraw(mesh, section, mat, xf, pu.Position, 1.1f);
                    if (mat.Transparent) scene.Transparent.Add(dc); else scene.Opaque.Add(dc);
                }
            }

            scene.AddLight(pu.Position + new Vector3(0, 0.55f, 0), 5.5f, pu.GlowColor, 2.2f, 0.85f);
        }
    }

    private static DrawCall MakePickupDraw(Mesh mesh, MeshSection section, Material mat, in Matrix4x4 xf,
        Vector3 center, float radius) => new()
        {
            Mesh = mesh,
            IndexOffset = section.IndexOffset,
            IndexCount = section.IndexCount,
            Material = mat,
            Transform = xf,
            BoneBase = -1,
            Tint = mat.BaseColor,
            Emissive = mat.Emissive,
            Alpha = mat.Alpha,
            Center = center,
            Radius = radius,
            CastShadow = false,
            UvScale = mat.UvScale,
            OwnerView = -1,
        };

    /// <summary>
    /// Stages an actual weapon mesh upright and broadside for documentation profile captures.
    /// The orientation, scale, sections and materials are identical to the live pickup.
    /// </summary>
    public void SubmitWeaponProfile(RenderScene scene, WeaponKind weapon, Vector3 position)
    {
        // A quiet in-engine studio backdrop keeps the procedural mesh readable while still
        // exercising the same renderer, material library and lighting path as gameplay.
        scene.SunDirection = Vector3.Normalize(new Vector3(-0.45f, -0.75f, -0.35f));
        scene.SunColor = new Vector3(3.8f, 3.55f, 3.25f);
        scene.AmbientSky = new Vector3(0.22f, 0.27f, 0.38f);
        scene.AmbientGround = new Vector3(0.07f, 0.08f, 0.11f);
        scene.SkyTop = new Vector3(0.018f, 0.028f, 0.065f);
        scene.SkyHorizon = new Vector3(0.11f, 0.15f, 0.24f);
        scene.SkyGround = new Vector3(0.025f, 0.03f, 0.045f);
        scene.StarStrength = 0f;
        scene.CloudStrength = 0f;
        scene.EnvIntensity = 0.7f;
        scene.FogDensity = 0f;
        scene.AddLight(position + new Vector3(2.2f, 1.8f, 1.4f), 7f,
            new Vector3(0.72f, 0.86f, 1f), 4.2f, 2f);
        scene.AddLight(position + new Vector3(-1.2f, 0.7f, -1.8f), 6f,
            new Vector3(1f, 0.36f, 0.14f), 2.4f, 1.5f);

        Matrix4x4 transform = Matrix4x4.CreateScale(1.25f)
                            * Matrix4x4.CreateTranslation(position);
        Mesh mesh = _weaponModels.MeshFor(weapon);
        foreach (MeshSection section in _weaponModels.SectionsFor(weapon))
        {
            Material material = Materials.Get(section.Material);
            scene.Opaque.Add(new DrawCall
            {
                Mesh = mesh,
                IndexOffset = section.IndexOffset,
                IndexCount = section.IndexCount,
                Material = material,
                Transform = transform,
                BoneBase = -1,
                Tint = material.BaseColor,
                Emissive = material.Emissive,
                Alpha = 1f,
                Center = position,
                Radius = 2.2f,
                CastShadow = true,
                UvScale = material.UvScale,
                OwnerView = -1,
            });
        }
    }

    /// <summary>
    /// Stages the exact live weapon-pickup mesh, scale, materials and pedestal at ground level for
    /// a documentation turntable. Only the yaw is supplied by the capture frame so the resulting
    /// animation covers one uniform, seamless 360-degree revolution.
    /// </summary>
    public void SubmitWeaponTurntable(RenderScene scene, WeaponKind weapon, Vector3 groundPosition,
        float yaw)
    {
        Vector3 weaponPosition = groundPosition + new Vector3(0f, 0.55f, 0f);
        Matrix4x4 weaponTransform = Matrix4x4.CreateScale(1.25f)
            * Matrix4x4.CreateRotationY(yaw)
            * Matrix4x4.CreateTranslation(weaponPosition);
        Mesh weaponMesh = _weaponModels.MeshFor(weapon);
        foreach (MeshSection section in _weaponModels.SectionsFor(weapon))
        {
            Material material = Materials.Get(section.Material);
            var draw = MakePickupDraw(weaponMesh, section, material, weaponTransform,
                weaponPosition, 2.2f);
            draw.CastShadow = true;
            scene.Opaque.Add(draw);
        }

        // This is the same torus rendered underneath every live weapon pickup, not a
        // documentation-only stand. It anchors the elevated camera view to the arena floor.
        Matrix4x4 pedestalTransform = Matrix4x4.CreateTranslation(groundPosition);
        Mesh pedestal = _pickupModels.MeshFor(PickupKind.WeaponPickup);
        foreach (MeshSection section in _pickupModels.SectionsFor(PickupKind.WeaponPickup))
        {
            Material material = Materials.Get(section.Material);
            var draw = MakePickupDraw(pedestal, section, material, pedestalTransform,
                groundPosition, 0.6f);
            draw.CastShadow = true;
            if (material.Transparent) scene.Transparent.Add(draw); else scene.Opaque.Add(draw);
        }

        Vector3 tint = Weapons.Get(weapon).Tint;
        scene.AddLight(weaponPosition + new Vector3(1.8f, 2.2f, 1.3f), 7f,
            tint * 0.55f + new Vector3(0.45f), 3.8f, 2f);
        scene.AddLight(weaponPosition + new Vector3(-1.8f, 0.8f, -1.4f), 5.5f,
            new Vector3(1f, 0.38f, 0.16f), 2.1f, 1.6f);
    }

    /// <summary>
    /// Draws each control point in its owner's colour. The dais and pillar are baked into the
    /// level, but ownership is the one thing about a control point that changes, so the coloured
    /// part has to be submitted per frame: a slowly turning marker above the pillar plus a light
    /// in the same colour, which is what makes the state readable across a room.
    /// </summary>
    private void SubmitControlPoints(RenderScene scene)
    {
        if (Mode.Kind != GameModeKind.Domination) return;
        var points = Level.ControlPoints;

        for (int i = 0; i < points.Count && i < ControlPointOwners.Count; i++)
        {
            Team owner = ControlPointOwners[i];
            Vector3 pos = points[i].Position + new Vector3(0, 3.0f, 0);
            Vector3 col = owner == Team.None ? new Vector3(0.75f, 0.75f, 0.8f) : GameTypes.TeamColor(owner);

            // A capture flashes for a moment so a change of hands is obvious even off-screen edge.
            float since = ControlPointSince[i];
            float flash = since < 0.9f ? 1f + (0.9f - since) * 3.4f : 1f;

            Matrix4x4 xf = Matrix4x4.CreateScale(0.42f)
                * Matrix4x4.CreateRotationY(Time * 1.1f + i)
                * Matrix4x4.CreateTranslation(pos);
            foreach (var section in _pickupModels.FlagSections)
            {
                Material mat = Materials.Get(section.Material);
                scene.Opaque.Add(new DrawCall
                {
                    Mesh = _pickupModels.Flag,
                    IndexOffset = section.IndexOffset,
                    IndexCount = section.IndexCount,
                    Material = mat,
                    Transform = xf,
                    BoneBase = -1,
                    Tint = new Vector4(col * flash, 1f),
                    Center = pos,
                    Radius = 1.4f,
                    CastShadow = false,
                });
            }
            scene.AddLight(pos, 13f, col, 3.4f * flash, 1.2f);
        }
    }

    /// <summary>
    /// Draws the Onslaught nodes. A node has to read at a distance across a map this size, so
    /// each one gets a floating orb in its owner's colour and a light to match: neutral white,
    /// team colour when held, dimmed while it is still building, and a core reads twice the size.
    /// </summary>
    private void SubmitPowerNodes(RenderScene scene)
    {
        if (Mode.Kind != GameModeKind.Onslaught) return;
        var nodes = Onslaught.Nodes;

        for (int i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            float scale = node.IsCore ? 0.9f : 0.5f;
            Vector3 pos = node.Position + new Vector3(0, node.IsCore ? 4.2f : 3.2f, 0);
            Vector3 col = node.Team == Team.None ? new Vector3(0.75f, 0.75f, 0.8f) : GameTypes.TeamColor(node.Team);

            // Half brightness while under construction, and a slow throb once it is up. A node
            // that is losing health pulses harder — damage is the thing you want seen first.
            float health = node.MaxHealth > 0f ? node.Health / node.MaxHealth : 1f;
            float bright = node.IsActive ? 1f : 0.45f + node.Built * 0.55f;
            if (node.IsActive && health < 0.999f)
                bright *= 1f + (1f - health) * (0.5f + 0.5f * MathF.Sin(Time * 9f)) * 1.4f;

            Matrix4x4 xf = Matrix4x4.CreateScale(scale)
                * Matrix4x4.CreateRotationY(Time * 0.8f + i)
                * Matrix4x4.CreateTranslation(pos);
            foreach (var section in _pickupModels.FlagSections)
            {
                scene.Opaque.Add(new DrawCall
                {
                    Mesh = _pickupModels.Flag,
                    IndexOffset = section.IndexOffset,
                    IndexCount = section.IndexCount,
                    Material = Materials.Get(section.Material),
                    Transform = xf,
                    BoneBase = -1,
                    Tint = new Vector4(col * bright, 1f),
                    Center = pos,
                    Radius = 2.4f * scale,
                    CastShadow = false,
                });
            }
            scene.AddLight(pos, node.IsCore ? 34f : 20f, col, 4f * bright, 1.3f);
        }
    }

    /// <summary>
    /// Draws the current Assault objective and nothing else. Only one thing at a time is live in
    /// this mode, and highlighting exactly that is the clearest signal the HUD can give: the
    /// marker pulses over whatever the attackers are supposed to be working on.
    /// </summary>
    private void SubmitObjectives(RenderScene scene)
    {
        if (Mode.Kind != GameModeKind.Assault) return;
        var o = Assault.CurrentObjective;
        if (o == null) return;

        Vector3 pos = o.Position + new Vector3(0, 3.4f, 0);
        float pulse = 0.75f + 0.25f * MathF.Sin(Time * 4f);
        Vector3 col = Vector3.Lerp(new Vector3(1f, 0.65f, 0.2f), new Vector3(1f, 0.95f, 0.6f), o.Progress);

        Matrix4x4 xf = Matrix4x4.CreateScale(0.55f)
            * Matrix4x4.CreateRotationY(Time * 1.4f)
            * Matrix4x4.CreateTranslation(pos);
        foreach (var section in _pickupModels.FlagSections)
        {
            scene.Opaque.Add(new DrawCall
            {
                Mesh = _pickupModels.Flag,
                IndexOffset = section.IndexOffset,
                IndexCount = section.IndexCount,
                Material = Materials.Get(section.Material),
                Transform = xf,
                BoneBase = -1,
                Tint = new Vector4(col * (1f + pulse * 0.5f), 1f),
                Center = pos,
                Radius = 1.6f,
                CastShadow = false,
            });
        }
        scene.AddLight(pos, 18f, col, 4.2f * pulse, 1.2f);
    }

    /// <summary>
    /// Draws each live vehicle. Team colour is applied to the hull tint rather than baked into
    /// the mesh, because a vehicle changes hands — the same Goliath can be red then blue.
    /// </summary>
    private void SubmitVehicles(RenderScene scene)
    {
        foreach (var v in Vehicles)
        {
            if (!v.Alive) continue;
            var def = v.Def;
            Matrix4x4 xf = Matrix4x4.CreateFromYawPitchRoll(v.Yaw, v.Pitch, v.Roll)
                * Matrix4x4.CreateTranslation(v.Position);

            Vector3 tint = def.Tint;
            if (v.Team != Team.None) tint = Vector3.Lerp(tint, GameTypes.TeamColor(v.Team), 0.55f);
            // The Nightshade fades out when it holds still; that is its whole defence.
            float alpha = 1f - v.CloakBlend * 0.82f;

            var mesh = _vehicleModels.MeshFor(v.Kind);
            foreach (var section in _vehicleModels.SectionsFor(v.Kind))
            {
                scene.Opaque.Add(new DrawCall
                {
                    Mesh = mesh,
                    IndexOffset = section.IndexOffset,
                    IndexCount = section.IndexCount,
                    Transform = xf,
                    Material = Materials.Get(section.Material),
                    Tint = new Vector4(tint, alpha),
                    Alpha = alpha,
                    Center = v.Position,
                    Radius = def.HalfExtents.Length() + 1f,
                    BoneBase = -1,
                    CastShadow = true,
                });
            }

            if (v.ShieldUp && v.ShieldHealth > 0f)
                Particles.Spawn(BlendMode.Additive, v.Position + new Vector3(0, def.HalfExtents.Y + 1f, 0),
                    Vector3.Zero, new Vector4(0.4f, 0.8f, 1f, 0.5f), new Vector4(0.4f, 0.8f, 1f, 0f),
                    def.HalfExtents.X * 2.4f, 0.08f, 0.08f, Spr.Flare);
        }
    }

    private void SubmitFlags(RenderScene scene)
    {
        if (Mode.Kind != GameModeKind.CaptureTheFlag) return;
        foreach (var kv in FlagPosition)
        {
            Team team = kv.Key;
            Vector3 pos = kv.Value;
            int carrier = FlagCarrier[team];
            float yaw = Time * 0.6f;
            if (carrier >= 0)
            {
                var p = FindPawn(carrier);
                if (p != null) { pos = p.Position + new Vector3(0, 0.2f, 0); yaw = p.Yaw + MathX.Pi; }
            }

            Matrix4x4 xf = Matrix4x4.CreateRotationY(yaw) * Matrix4x4.CreateTranslation(pos);
            Vector3 col = GameTypes.TeamColor(team);
            foreach (var section in _pickupModels.FlagSections)
            {
                Material mat = Materials.Get(section.Material);
                bool cloth = section.Material == (int)MatId.EnergyPanel;
                scene.Opaque.Add(new DrawCall
                {
                    Mesh = _pickupModels.Flag,
                    IndexOffset = section.IndexOffset,
                    IndexCount = section.IndexCount,
                    Material = mat,
                    Transform = xf,
                    BoneBase = -1,
                    Tint = cloth ? new Vector4(col, 1f) : mat.BaseColor,
                    Emissive = cloth ? col * 2.4f : mat.Emissive,
                    OverrideEmissive = true,
                    Alpha = 1f,
                    Center = pos + new Vector3(0, 1f, 0),
                    Radius = 1.8f,
                    CastShadow = true,
                    UvScale = mat.UvScale,
                    OwnerView = -1,
                });
            }
            scene.AddLight(pos + new Vector3(0, 1.3f, 0), 9f, col, 4f, 1.5f);
        }
    }

    /// <summary>Adds the first-person weapon for one view. Called once per local player.</summary>
    public void SubmitViewModel(RenderScene scene, int viewIndex, Pawn pawn, in Camera camera)
    {
        if (!pawn.Alive) return;
        var def = pawn.WeaponDef;
        var mesh = _weaponModels.MeshFor(pawn.Weapon);
        if (mesh == null) return;

        // Weapon bob and sway, plus a switch dip and recoil kick.
        float speed01 = MathX.Saturate(pawn.Speed / Physics.GroundSpeed);
        float bobX = MathF.Sin(pawn.ViewBobPhase) * 0.016f * speed01;
        float bobY = MathF.Abs(MathF.Cos(pawn.ViewBobPhase)) * 0.014f * speed01;
        float switchDip = pawn.IsSwitching
            ? MathF.Sin(MathX.Saturate(1f - pawn.SwitchTimer / MathF.Max(def.SwitchTime, 0.01f)) * MathX.Pi) * 0.22f
            : 0f;
        float recoilZ = pawn.FireBlend * 0.075f;
        float spin = def.SpinUp ? pawn.SpinUp * Time * 26f : 0f;

        Vector3 offset = def.FpOffset + new Vector3(bobX, bobY - switchDip, recoilZ);
        if (pawn.ZoomFov > 0f) offset = new Vector3(0f, -0.06f, -0.30f);

        Matrix4x4 local = Matrix4x4.CreateScale(def.FpScale)
                        * Matrix4x4.CreateRotationZ(spin)
                        * Matrix4x4.CreateRotationX(-pawn.FireBlend * 0.16f)
                        * Matrix4x4.CreateTranslation(offset);

        Matrix4x4 view = Matrix4x4.CreateWorld(camera.Position, camera.Forward, camera.Up);
        Matrix4x4 xf = local * view;

        foreach (var section in _weaponModels.SectionsFor(pawn.Weapon))
        {
            Material mat = Materials.Get(section.Material);
            scene.Opaque.Add(new DrawCall
            {
                Mesh = mesh,
                IndexOffset = section.IndexOffset,
                IndexCount = section.IndexCount,
                Material = mat,
                Transform = xf,
                BoneBase = -1,
                Tint = mat.BaseColor,
                Emissive = mat.Emissive,
                Alpha = 1f,
                Center = camera.Position,
                Radius = 2f,
                CastShadow = false,
                RimStrength = 0.35f,
                RimColor = def.Tint * 0.6f,
                UvScale = mat.UvScale,
                OwnerView = viewIndex,
                FirstPerson = true,
            });
        }

        if (pawn.FireBlend > 0.05f)
            scene.AddLight(pawn.MuzzleWorld(), def.MuzzleLightRadius,
                def.Tint, def.MuzzleLightIntensity * pawn.FireBlend, 4f);
    }

    // ---------------------------------------------------------------- sound mapping

    private static SoundId WeaponSound(WeaponKind kind, bool alt) => kind switch
    {
        WeaponKind.ImpactHammer => SoundId.HammerSwing,
        WeaponKind.Enforcer => SoundId.Enforcer,
        WeaponKind.BioRifle => SoundId.BioFire,
        WeaponKind.ShockRifle => alt ? SoundId.ShockAlt : SoundId.ShockPrimary,
        WeaponKind.PulseGun => SoundId.PulseFire,
        WeaponKind.Ripper => SoundId.RipperFire,
        WeaponKind.Minigun => SoundId.MinigunFire,
        WeaponKind.FlakCannon => alt ? SoundId.FlakAlt : SoundId.FlakPrimary,
        WeaponKind.RocketLauncher => SoundId.RocketFire,
        WeaponKind.SniperRifle => SoundId.SniperFire,
        WeaponKind.Redeemer => SoundId.RedeemerFire,
        _ => SoundId.Enforcer,
    };

    private static SoundId PickupSound(PickupKind kind) => kind switch
    {
        PickupKind.HealthVial or PickupKind.HealthPack or PickupKind.SuperHealth => SoundId.PickupHealth,
        PickupKind.ThighPads or PickupKind.BodyArmor or PickupKind.ShieldBelt => SoundId.PickupArmor,
        PickupKind.DamageAmp or PickupKind.Invisibility or PickupKind.JumpBoots => SoundId.PickupPower,
        PickupKind.WeaponPickup => SoundId.PickupWeapon,
        _ => SoundId.PickupAmmo,
    };
}
