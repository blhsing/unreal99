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
    private readonly CockpitModels _cockpitModels;

    private readonly Dictionary<int, Matrix4x4[]> _boneWorld = new();
    private readonly Dictionary<int, Matrix4x4[]> _boneSkin = new();
    private readonly List<Vector3> _spawnAvoid = new(16);

    // Domination state
    /// <summary>The Onslaught node network. Empty on every other mode.</summary>
    public readonly OnslaughtState Onslaught = new();

    /// <summary>The Assault objective sequence and round bookkeeping. Empty on every other mode.</summary>
    public readonly AssaultState Assault = new();

    /// <summary>Warfare's orbs and auxiliary-node payouts. Idle on every other mode.</summary>
    public readonly WarfareState Warfare = new();

    /// <summary>The Bombing Run ball and the two hoops. Idle on every other mode.</summary>
    public readonly BombingRunState BombingRun = new();

    /// <summary>True for the two modes built on the power-node network.</summary>
    public bool NodeNetworkMode => Mode.Kind is GameModeKind.Onslaught or GameModeKind.Warfare;

    public readonly List<Vehicle> Vehicles = new(16);
    /// <summary>Behavioral-test telemetry accumulated through production gameplay paths.</summary>
    public int VehicleBoardings { get; private set; }
    public int AssaultAttackerVehicleBoardings { get; private set; }
    public int AssaultDefenderVehicleBoardings { get; private set; }
    public readonly HashSet<VehicleKind> VehicleKindsDriven = new();
    public int OnslaughtNodeCaptures { get; private set; }
    public int WarfareOrbPickups { get; private set; }
    public int WarfareOrbCaptures { get; private set; }
    public int BallPickups { get; private set; }
    public int BallGoals { get; private set; }
    public int BallPasses { get; private set; }
    public int HoverboardRides { get; private set; }
    public int HoverboardTows { get; private set; }
    public int AssaultObjectiveCompletions { get; private set; }
    public int AssaultRoundsCompleted { get; private set; }
    public int NextVehicleId = 1;

    public Vehicle FindVehicle(int id)
    {
        foreach (var v in Vehicles) if (v.Id == id) return v;
        return null;
    }

    private static Team Opposite(Team team) => team switch
    {
        Team.Red => Team.Blue,
        Team.Blue => Team.Red,
        _ => Team.None,
    };

    private int NearestPowerNode(Vector3 position, float maximumDistance)
    {
        int best = -1;
        float bestDistance = maximumDistance * maximumDistance;
        for (int i = 0; i < Onslaught.Nodes.Count; i++)
        {
            float distance = Vector3.DistanceSquared(position, Onslaught.Nodes[i].Position);
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            best = i;
        }
        return best;
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
            Vector3 delta = v.Position - pawn.Position;
            float hullRadius = MathF.Max(v.Def.HalfExtents.X, v.Def.HalfExtents.Z);
            float horizontalGap = MathF.Max(0f, delta.FlatXZ().Length() - hullRadius);
            float verticalGap = MathF.Max(0f, MathF.Abs(delta.Y) - v.Def.HalfExtents.Y);
            float surfaceDistance = horizontalGap * horizontalGap + verticalGap * verticalGap;
            if (surfaceDistance > bestDist) continue;
            bestDist = surfaceDistance; best = v;
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
        // First one aboard claims a neutral vehicle. Fresh team vehicles remain locked while
        // occupied; a driver exit deliberately unlocks them below, matching Onslaught stealing.
        if (vehicle.Team == Team.None) vehicle.Team = pawn.Team;
        pawn.VehicleId = vehicle.Id;
        pawn.VehicleSeat = seat;
        VehicleBoardings++;
        if (Mode.Kind == GameModeKind.Assault)
        {
            if (pawn.Team == Assault.Attackers) AssaultAttackerVehicleBoardings++;
            else if (pawn.Team == Assault.Defenders) AssaultDefenderVehicleBoardings++;
        }
        if (seat == 0) VehicleKindsDriven.Add(vehicle.Kind);
        OnSound?.Invoke(SoundId.Respawn, vehicle.Position, 0.7f);
        return true;
    }

    /// <summary>
    /// Moves a rider to the next vacant seat of the vehicle they are already in. Multi-seat
    /// vehicles are otherwise a lottery: whichever seat happened to be free when you pressed use
    /// is the one you are stuck with, so a player who wanted the Hellbender's rear turret and
    /// landed in the driver's chair had to get out on foot and hope for better next time.
    /// </summary>
    public bool SwitchVehicleSeat(Pawn pawn)
    {
        if (pawn == null || pawn.VehicleId < 0) return false;
        var v = FindVehicle(pawn.VehicleId);
        if (v == null || !v.Alive) return false;
        int from = pawn.VehicleSeat;
        if (from < 0 || from >= v.Occupants.Length) return false;
        int to = v.NextFreeSeatAfter(from);
        if (to < 0)
        {
            FeedbackFor(pawn).Big(Loc.HudNoFreeSeat, new Vector3(1f, 0.62f, 0.30f), 1.2f);
            return false;
        }

        v.Occupants[from] = -1;
        v.Occupants[to] = pawn.Id;
        pawn.VehicleSeat = to;
        // A turret keeps its own aim; carry the rider's current view into the new seat so the
        // switch does not spin the camera to wherever the last occupant left the mount.
        v.SeatYaw[to] = v.Def.Seats[to].Turret ? pawn.Yaw : v.Yaw;
        v.SeatPitch[to] = MathX.Clamp(pawn.Pitch, -0.9f, 0.9f);
        v.SeatCooldown[to] = MathF.Max(v.SeatCooldown[to], 0.25f);
        if (to == 0) VehicleKindsDriven.Add(v.Kind);
        FeedbackFor(pawn).Big(Loc.SeatMoved(v.Def.Seats[to].Role), GameTypes.TeamColor(pawn.Team), 1.2f);
        OnSound?.Invoke(SoundId.Respawn, v.Position, 0.5f);
        return true;
    }

    public void ExitVehicle(Pawn pawn)
    {
        if (pawn == null || pawn.VehicleId < 0) return;
        var v = FindVehicle(pawn.VehicleId);
        if (v != null && pawn.VehicleSeat >= 0 && pawn.VehicleSeat < v.Occupants.Length)
        {
            bool wasDriver = pawn.VehicleSeat == 0;
            v.Occupants[pawn.VehicleSeat] = -1;
            pawn.Position = v.ExitPosition(Level.Collision);
            pawn.Velocity = v.Velocity * 0.35f;
            // Fresh team vehicles are locked. Once a teammate has actually driven and abandoned
            // one, it becomes stealable by either side, matching the original Onslaught rule.
            if (wasDriver) v.Team = Team.None;
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
    public VehicleModels VehicleMeshes => _vehicleModels;

    /// <summary>Raised whenever something should make a noise; the audio layer subscribes.</summary>
    public Action<SoundId, Vector3, float> OnSound;

    public GameWorld(Renderer renderer, CharacterModel character, WeaponModels weaponModels,
        ProjectileModels projectileModels, PickupModels pickupModels, VehicleModels vehicleModels,
        CockpitModels cockpitModels = null)
    {
        _renderer = renderer;
        _character = character;
        _weaponModels = weaponModels;
        _projectileModels = projectileModels;
        _pickupModels = pickupModels;
        _vehicleModels = vehicleModels;
        _cockpitModels = cockpitModels;
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
        VehicleBoardings = 0;
        AssaultAttackerVehicleBoardings = 0;
        AssaultDefenderVehicleBoardings = 0;
        VehicleKindsDriven.Clear();
        _strikes.Clear();
        OnslaughtNodeCaptures = 0;
        WarfareOrbPickups = 0;
        WarfareOrbCaptures = 0;
        HoverboardRides = 0;
        HoverboardTows = 0;
        AssaultObjectiveCompletions = 0;
        AssaultRoundsCompleted = 0;

        foreach (var p in level.Pickups)
        {
            Pickups.Add(new PickupEntity
            {
                Kind = p.Kind,
                Weapon = p.Weapon,
                Ammo = p.Ammo,
                LockerWeapons = p.LockerWeapons ?? [],
                Position = p.Position,
                RespawnTime = p.RespawnTime,
                Active = true,
                Phase = Rng.Range(0f, MathX.TwoPi),
            });
        }

        Onslaught.Warfare = Mode.Kind == GameModeKind.Warfare;
        Onslaught.Reset(level);
        Assault.Reset(level);
        Warfare.Reset(level, Onslaught);
        BombingRun.Reset(level);

        Vehicles.Clear();
        NextVehicleId = 1;
        // Vehicles belong to the three vehicle gametypes only. The same arena loaded as a
        // deathmatch is meant to be a foot fight, and a Goliath in one would not be a nod to the
        // original — it would be a different game.
        bool vehiclesAllowed = Mode.Kind is GameModeKind.Onslaught or GameModeKind.Assault
            or GameModeKind.Warfare;
        foreach (var vs in vehiclesAllowed ? level.VehicleSpawns : [])
        {
            var v = new Vehicle { Id = NextVehicleId++ };
            v.Configure(vs.Kind, vs.Position + new Vector3(0f, VehicleDef.Get(vs.Kind).HalfExtents.Y, 0f), vs.Yaw);
            v.SpawnRespawnSeconds = vs.RespawnSeconds;
            v.AuthoredSpawnTeam = vs.Team;
            v.SpawnTeam = vs.Team;
            v.Team = vs.Team;
            if (NodeNetworkMode)
            {
                v.SpawnNodeIndex = NearestPowerNode(vs.Position, 48f);
                if (v.SpawnNodeIndex >= 0)
                {
                    PowerNode node = Onslaught.Nodes[v.SpawnNodeIndex];
                    // Warfare pads are authored per team — Torlan Necris parks an Axon and a
                    // Necris vehicle at the same node — so an authored team wins over the node's.
                    if (vs.Team == Team.None)
                    {
                        v.SpawnTeam = node.Team;
                        v.Team = node.Team;
                    }
                    if (!node.IsActive)
                    {
                        v.Alive = false;
                        v.RespawnTimer = float.PositiveInfinity;
                    }
                }
            }
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
        Team spawnTeam = Mode.TeamBased ? pawn.Team : Team.None;
        if (Mode.Kind == GameModeKind.Assault)
        {
            assaultGroup = pawn.Team == Assault.Attackers ? Assault.SpawnGroup : 0;
            // Map spawn colours describe round one roles. In round two both teams use the other
            // side's physical starts while keeping their own score/team identity.
            if (Assault.Round == 2) spawnTeam = Opposite(spawnTeam);
        }

        SpawnPoint spawn = NodeNetworkMode
            ? PickOnslaughtSpawn(pawn.Team, _spawnAvoid, 9f)
            : Level.PickSpawn(Rng, spawnTeam, _spawnAvoid, 9f, assaultGroup);
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

    /// <summary>
    /// Onslaught respawns only at the team's active core/nodes, and never at a node currently
    /// taking damage. Untagged map spawns are associated with their nearest power structure.
    /// </summary>
    private SpawnPoint PickOnslaughtSpawn(Team team, IReadOnlyList<Vector3> avoid, float minDistance)
    {
        var candidates = new List<SpawnPoint>();
        foreach (SpawnPoint spawn in Level.Spawns)
        {
            int index = NearestPowerNode(spawn.Position, 34f);
            if (index < 0) continue;
            PowerNode node = Onslaught.Nodes[index];
            if (!node.IsActive || node.Team != team || node.Health < node.MaxHealth - 0.5f) continue;
            bool underAttack = false;
            foreach (Pawn other in Pawns)
            {
                if (!other.Alive || other.Team == Team.None || other.Team == team) continue;
                if (Vector3.DistanceSquared(other.Position, node.Position) <= 12f * 12f)
                {
                    underAttack = true;
                    break;
                }
            }
            if (!underAttack) candidates.Add(spawn);
        }
        if (candidates.Count == 0) return Level.PickSpawn(Rng, team, avoid, minDistance);

        SpawnPoint best = candidates[0];
        float bestClearance = float.MinValue;
        foreach (SpawnPoint candidate in candidates)
        {
            float clearance = avoid.Count == 0 ? 1000f : float.MaxValue;
            foreach (Vector3 occupied in avoid)
                clearance = MathF.Min(clearance, Vector3.Distance(candidate.Position, occupied));
            if (clearance >= minDistance) return candidate;
            if (clearance > bestClearance) { best = candidate; bestClearance = clearance; }
        }
        return best;
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
        // A Bombing Run goal starts an eleven-second round reset. No player may move, shoot or
        // collect the waiting midfield ball during it; presentation and the countdown continue.
        if (Mode.Kind == GameModeKind.BombingRun && BombingRun.RoundResetActive)
        {
            foreach (Pawn pawn in Pawns)
            {
                Feedbacks[pawn.Id].Update(dt);
                pawn.Velocity = Vector3.Zero;
                pawn.TickPresentation(dt);
            }
            UpdateBombingRun(dt);
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
                if (input.SwitchSeat) { SwitchVehicleSeat(pawn); continue; }
                // Pawn.Move is what normally copies the look angles across, and riders skip it —
                // which left the camera frozen at whatever direction the player happened to be
                // facing when they boarded. Nothing about steering or mouse look reached the
                // view at all until this was applied here as well.
                pawn.Yaw = input.Yaw;
                pawn.Pitch = MathX.Clamp(input.Pitch, -1.50f, 1.50f);
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
                // Nothing to board: use is also how you force a dropped enemy orb to respawn.
                if (SacrificeToEnemyOrb(pawn)) continue;
            }

            UpdateHoverboard(pawn, input, dt);

            var events = pawn.Move(Level, input, dt);
            HandleMoveEvents(pawn, events, dt);
            if (!pawn.Alive) continue;
            if (events.KnockedOffBoard) KnockOffHoverboard(pawn, stun: false);

            HandleWeapons(pawn, input, dt);
            HandlePickups(pawn);
            Mode.OnPawnUpdate(this, pawn, dt);
            UpdateCarriedFlag(pawn);
            UpdateBallCarry(pawn, dt);
        }

        UpdateProjectiles(dt);
        UpdateAirStrikes(dt);
        UpdatePickups(dt);
        UpdateFlags(dt);
        UpdateControlPoints(dt);
        UpdateVehicles(dt);
        UpdateOnslaught(dt);
        UpdateAssault(dt);
        UpdateBombingRun(dt);

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
        // Hands are on the board. Alt-fire is the grapple and primary does nothing at all — the
        // rider's only options are to keep going or step off, which is the point of the trade.
        if (pawn.OnHoverboard)
        {
            pawn.SpinUp = 0f;
            pawn.ZoomFov = 0f;
            pawn.FiringBeam = false;
            pawn.ChargingPrimary = false;
            pawn.UpdateWeaponTimers(dt);
            return;
        }

        if (input.WeaponSelect >= 0 && input.WeaponSelect < (int)WeaponKind.Count)
            pawn.RequestWeapon((WeaponKind)input.WeaponSelect);
        else if (input.WeaponCycle != 0)
            pawn.CycleWeapon(input.WeaponCycle);

        if (pawn.UpdateWeaponTimers(dt)) OnSound?.Invoke(SoundId.WeaponSwitch, pawn.Position, 0.4f);

        var def = pawn.WeaponDef;

        pawn.ShieldRechargeDelay = MathF.Max(0f, pawn.ShieldRechargeDelay - dt);
        if (!pawn.ShieldRaised && pawn.ShieldRechargeDelay <= 0f)
            pawn.ShieldEnergy = MathF.Min(100f, pawn.ShieldEnergy + 18f * dt);
        pawn.LinkBoostTimer = MathF.Max(0f, pawn.LinkBoostTimer - dt);

        // The Ball Launcher follows the original two-step pass control: alternate fire acquires
        // a team-mate, primary fire throws to that lock (or free-throws along the sightline when
        // no lock exists). Merely holding alternate fire must never release the ball.
        if (pawn.Weapon == WeaponKind.BallLauncher && pawn.HasBall)
        {
            pawn.ZoomFov = 0f;
            if (input.AltFire)
                pawn.BallPassTargetId = BestBallPassTarget(pawn)?.Id ?? -1;
            if (!pawn.IsSwitching && pawn.FireCooldown <= 0f && input.Fire)
                Fire(pawn, false, 1f);
            return;
        }

        bool zoomHeld = input.AltFire && def.Alt.ZoomFov > 0f;
        pawn.ZoomFov = zoomHeld ? def.Alt.ZoomFov : 0f;
        // The shield is a held state rather than a shot, so it lives here and not in Fire().
        pawn.ShieldRaised = def.Alt.Mode == FireMode.Shield && input.AltFire && !pawn.IsSwitching
            && pawn.ShieldEnergy > 0.01f;

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

        // The Ball Launcher does not fire a projectile — it hands the mode's one ball back to the
        // world with a velocity. Alternate fire has already selected a team-mate in
        // HandleWeapons; primary either passes to that lock or free-throws along the sightline.
        if (pawn.Weapon == WeaponKind.BallLauncher)
        {
            if (!pawn.HasBall) return;
            pawn.FireCooldown = fire.Interval;
            pawn.FireBlend = 1f;
            pawn.ShotsFired++;
            Vector3 launch = pawn.ViewDirection * fire.ProjectileSpeed;
            Pawn mate = FindPawn(pawn.BallPassTargetId);
            if (mate is { Alive: true } && mate.Team == pawn.Team && mate != pawn)
            {
                Vector3 to = mate.Center - pawn.Center;
                float range = to.Length();
                if (range > 0.5f)
                {
                    // Lead the arc so the pass lands on the team-mate rather than short of them.
                    float flight = range / fire.ProjectileSpeed;
                    launch = to / flight + mate.Velocity * 0.5f;
                    launch.Y += 0.5f * Physics.Gravity * Level.GravityScale * flight;
                    BallPasses++;
                }
            }
            ReleaseBall(pawn, launch, thrown: true);
            OnSound?.Invoke(SoundId.FlagTaken, pawn.Position, 0.9f);
            return;
        }

        pawn.ConsumeAmmo(alt);
        pawn.FireCooldown = fire.Interval;
        pawn.FireBlend = 1f;
        pawn.CameraShake = MathF.Min(1.5f, pawn.CameraShake + fire.ShakeAmount);
        pawn.Pitch = MathX.Clamp(pawn.Pitch + fire.Recoil, -1.5f, 1.5f);
        pawn.ShotsFired++;

        Vector3 origin = pawn.MuzzleWorld();
        Vector3 aim = pawn.ViewDirection;
        float damageScale = chargeScale * (pawn.HasDamageAmp ? 2f : 1f)
            * (pawn.LinkBoostTimer > 0f ? 1.5f : 1f);
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
                if (fire.Projectile == ProjectileKind.SpiderMine)
                    EnforceOwnedProjectileLimit(pawn.Id, ProjectileKind.SpiderMine, 4);
                else if (fire.Projectile == ProjectileKind.StickyGrenade)
                    EnforceOwnedProjectileLimit(pawn.Id, ProjectileKind.StickyGrenade, 8);
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

            // Primary may be dumb-fired at anything; a vehicle under the reticle turns it into
            // the original guided anti-armour missile. Alternate fire supplies zoom/lock view.
            case FireMode.LockOn:
            {
                Vehicle target = LockOnTarget(pawn, fire.Range);
                var missile = SpawnProjectile(fire.Projectile, fire, origin, aim, pawn,
                    damageScale, def.Tint);
                if (missile >= 0 && target != null) _projectiles[missile].HomingVehicleId = target.Id;
                break;
            }

            // Paint a spot; something arrives on it a few seconds later. The delay is the counter­
            // play — everyone can see the beam and has time to leave.
            case FireMode.Painter:
            {
                float paintRange = MathF.Max(40f, fire.Range);
                var hit = Level.Collision.Raycast(origin, origin + aim * paintRange);
                float worldDistance = hit.Hit ? hit.Distance : paintRange;
                Pawn painted = TracePawns(origin, aim, worldDistance, pawn, out float pawnDistance,
                    out _, out _);
                Vector3 spot = painted != null && pawnDistance <= worldDistance
                    ? painted.Position : hit.Hit ? hit.Point : origin + aim * fire.Range;
                if (def.Kind == WeaponKind.MineLayer) { RedirectMines(pawn, spot); break; }
                _strikes.Add(new AirStrike
                {
                    Position = spot, Delay = 2.6f, OwnerId = pawn.Id, Team = pawn.Team,
                    Radius = fire.SplashRadius, Damage = fire.SplashDamage,
                    Knockback = fire.Knockback, Scale = damageScale,
                    Bomber = def.Kind == WeaponKind.TargetPainter,
                    Direction = MathX.SafeNormalize(aim.FlatXZ(), MathX.Forward),
                });
                AddKillFeed(def.Kind == WeaponKind.IonPainter
                    ? Loc.AnnIonStrike(pawn.Name) : Loc.AnnBomberStrike(pawn.Name), def.Tint);
                OnSound?.Invoke(SoundId.AnnounceMajor, spot, 0.9f);
                break;
            }

            case FireMode.Detonate:
                DetonateOwnedGrenades(pawn);
                break;

            case FireMode.Recall:
                RecallToTranslocator(pawn);
                break;

            case FireMode.Shield:
                break;   // handled continuously in HandleWeapons, not as a discrete shot
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
        Vector3 tint, bool supportFriendlyNodes = false)
    {
        var vehicle = TraceVehicles(origin, dir, maxDist, shooter, out float vDist, out Vector3 vPoint);
        int node = TraceNodes(origin, dir, maxDist, out float nDist);

        if (vehicle != null && (node < 0 || vDist <= nDist))
        {
            shooter.ShotsHit++;
            if (supportFriendlyNodes && vehicle.Team == shooter.Team)
            {
                vehicle.Health = MathF.Min(vehicle.Def.Health, vehicle.Health + damage);
                Effects.AddTracer(origin, vPoint, tint, 0.04f, 0.08f);
                Particles.EnergyBurst(vPoint, tint, 0.45f);
                return true;
            }
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
            NodeEvent evt = supportFriendlyNodes
                ? Onslaught.Support(node, shooter.Team, shooter.Id, damage, out var hit)
                : Onslaught.Hurt(node, shooter.Team, damage, out hit);
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
                if (target.Team != Team.None && target.Team == pawn.Team)
                {
                    // A Link beam on a team-mate is harmless and boosts the teammate's Link Gun
                    // output, matching the behavior that gives the weapon its name.
                    if (target.Weapon == WeaponKind.LinkGun) target.LinkBoostTimer = 0.22f;
                    Particles.EnergyBurst(point, pawn.WeaponDef.Tint, 0.28f);
                }
                else
                {
                    float amp = (pawn.HasDamageAmp ? 2f : 1f)
                        * (pawn.LinkBoostTimer > 0f ? 1.5f : 1f);
                    Damage(target, pawn, fire.Damage * 0.1f * amp, DamageType.Energy, dir);
                    Particles.BloodSpray(point, -dir, 0.3f);
                }
            }
            else if (!HitStructures(pawn, origin, dir, maxDist, fire.Damage * 0.1f,
                         pawn.WeaponDef.Tint, supportFriendlyNodes: true)
                     && worldHit.Hit)
            {
                Particles.ImpactSparks(worldHit.Point, worldHit.Normal, 0.4f, pawn.WeaponDef.Tint);
            }
        }
        _ = pawnDist;
    }

    /// <summary>UT2004 caps one owner's live mines/grenades; recycle the oldest before spawning.</summary>
    private void EnforceOwnedProjectileLimit(int ownerId, ProjectileKind kind, int maximum)
    {
        int count = 0;
        int oldest = -1;
        float leastLife = float.MaxValue;
        for (int i = 0; i < Projectiles.Length; i++)
        {
            ref Projectile projectile = ref Projectiles[i];
            if (!projectile.Active || projectile.OwnerId != ownerId || projectile.Kind != kind) continue;
            count++;
            if (projectile.Life < leastLife) { leastLife = projectile.Life; oldest = i; }
        }
        if (count >= maximum && oldest >= 0) Projectiles[oldest].Active = false;
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
        // A demolition tool has to be able to demolish things. Without this a bot reduced to the
        // hammer stands next to a generator swinging at it forever, and a player doing the same
        // gets no feedback at all.
        else if (HitStructures(pawn, origin, dir, maxDist, fire.Damage * damageScale,
                     new Vector3(0.7f, 0.85f, 1f)))
        {
            pawn.ShotsFired++;
            OnSound?.Invoke(SoundId.HammerHit, origin + dir * MathF.Min(maxDist, fire.Range), 1f);
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

    /// <summary>Spawns one projectile and returns its slot, or -1 when the pool is full.</summary>
    private int SpawnProjectile(ProjectileKind kind, in FireDef fire, Vector3 origin, Vector3 dir,
        Pawn owner, float damageScale, Vector3 tint)
    {
        for (int i = 0; i < Projectiles.Length; i++)
        {
            if (Projectiles[i].Active) continue;
            Projectiles[i] = ProjectileFactory.Create(kind, fire, origin, dir, owner.Id, owner.Team,
                tint, damageScale, Rng);
            return i;
        }
        return -1;
    }

    private Projectile[] _projectiles => Projectiles;

    /// <summary>
    /// A mine that has landed looks for something to run down. It only ever chases what comes to
    /// it, which is why a laid minefield is a defensive tool rather than a guided missile battery.
    /// </summary>
    private void WakeSpiderMine(ref Projectile p)
    {
        const float noticeRange = 16f;
        if (p.HasHomingPoint)
        {
            if (Vector3.Distance(p.Position, p.HomingPoint) > 1.2f) { p.Stuck = false; return; }
            p.HasHomingPoint = false;
        }

        foreach (var v in Vehicles)
        {
            if (!v.Alive) continue;
            if (v.Team != Team.None && p.OwnerTeam != Team.None && v.Team == p.OwnerTeam) continue;
            if (Vector3.Distance(v.Position, p.Position) > noticeRange) continue;
            p.HomingVehicleId = v.Id;
            p.Stuck = false;
            return;
        }
        foreach (var target in Pawns)
        {
            if (!target.Alive || target.Id == p.OwnerId) continue;
            if (Mode.TeamBased && target.Team == p.OwnerTeam) continue;
            if (Vector3.Distance(target.Position, p.Position) > noticeRange) continue;
            p.HomingPawnId = target.Id;
            p.Stuck = false;
            return;
        }
    }

    /// <summary>Turns a seeker or a woken mine towards whatever it has decided to chase.</summary>
    private void SteerHomingProjectile(ref Projectile p, float dt)
    {
        Vector3 goal;
        if (p.HomingVehicleId >= 0 && FindVehicle(p.HomingVehicleId) is { Alive: true } v)
            goal = v.Position;
        else if (p.HomingPawnId >= 0 && FindPawn(p.HomingPawnId) is { Alive: true } target)
            goal = target.Center;
        else if (p.HasHomingPoint) goal = p.HomingPoint;
        else return;

        float speed = p.Velocity.Length();
        if (speed < 1e-4f) return;
        Vector3 want = MathX.SafeNormalize(goal - p.Position, p.Velocity / speed);
        Vector3 current = p.Velocity / speed;
        // Cap the turn so a seeker can be dodged by a fast vehicle rather than being a hitscan.
        float maxTurn = p.TurnRate * dt;
        float angle = MathF.Acos(MathX.Clamp(Vector3.Dot(current, want), -1f, 1f));
        Vector3 dir = angle <= maxTurn ? want
            : MathX.SafeNormalize(Vector3.Lerp(current, want, maxTurn / angle), current);
        p.Velocity = dir * speed;
    }

    // ---------------------------------------------------------------- UT2004 weapon behaviours

    /// <summary>
    /// The vehicle an AVRiL will chase: whatever enemy armour is closest to the crosshair. Only
    /// vehicles — locking on to a person is the one thing this weapon deliberately cannot do.
    /// </summary>
    private Vehicle LockOnTarget(Pawn pawn, float range)
    {
        Vector3 eye = pawn.EyePosition, aim = pawn.ViewDirection;
        Vehicle best = null;
        float bestScore = 0.86f;   // roughly a 30-degree cone
        foreach (var v in Vehicles)
        {
            if (!v.Alive) continue;
            if (v.Team != Team.None && pawn.Team != Team.None && v.Team == pawn.Team) continue;
            Vector3 to = v.Position - eye;
            float distance = to.Length();
            if (distance > range || distance < 0.01f) continue;
            float score = Vector3.Dot(to / distance, aim);
            if (score > bestScore) { bestScore = score; best = v; }
        }
        return best;
    }

    /// <summary>Sends every spider mine this pawn has laid at a painted spot.</summary>
    private void RedirectMines(Pawn pawn, Vector3 spot)
    {
        for (int i = 0; i < Projectiles.Length; i++)
        {
            ref Projectile p = ref Projectiles[i];
            if (!p.Active || p.Kind != ProjectileKind.SpiderMine || p.OwnerId != pawn.Id) continue;
            p.HomingPoint = spot;
            p.HasHomingPoint = true;
            p.HomingPawnId = -1;
            p.HomingVehicleId = -1;
        }
        Particles.Spawn(BlendMode.Additive, spot, Vector3.Zero,
            new Vector4(1f, 0.6f, 0.3f, 0.9f), new Vector4(1f, 0.6f, 0.3f, 0f), 1.2f, 0.4f, 0.4f, Spr.Flare);
    }

    /// <summary>Sets off every grenade this pawn is still holding the clicker for.</summary>
    private void DetonateOwnedGrenades(Pawn pawn)
    {
        for (int i = 0; i < Projectiles.Length; i++)
        {
            ref Projectile p = ref Projectiles[i];
            if (!p.Active || p.Kind != ProjectileKind.StickyGrenade || p.OwnerId != pawn.Id) continue;
            ExplodeProjectile(ref p);
        }
    }

    /// <summary>
    /// Translocator recall. Landing inside somebody telefrags them — the reason this counts as a
    /// weapon rather than a movement key.
    /// </summary>
    private void RecallToTranslocator(Pawn pawn)
    {
        for (int i = 0; i < Projectiles.Length; i++)
        {
            ref Projectile p = ref Projectiles[i];
            if (!p.Active || p.Kind != ProjectileKind.TranslocatorDisc || p.OwnerId != pawn.Id) continue;
            Vector3 destination = p.Position + new Vector3(0f, 0.1f, 0f);
            p.Active = false;

            foreach (var other in Pawns)
            {
                if (other == pawn || !other.Alive) continue;
                if (Vector3.Distance(other.Position, destination) > 1.3f) continue;
                Damage(other, pawn, 10000f, DamageType.Telefrag, MathX.Up);
            }

            Particles.Spawn(BlendMode.Additive, pawn.Center, Vector3.Zero,
                new Vector4(0.5f, 0.8f, 1f, 0.9f), new Vector4(0.5f, 0.8f, 1f, 0f), 1.4f, 0.35f, 0.35f, Spr.Flare);
            pawn.Position = destination;
            pawn.Velocity *= 0.2f;
            OnSound?.Invoke(SoundId.Teleport, destination, 0.9f);
            return;
        }
    }

    /// <summary>A called-in strike waiting on its delay.</summary>
    private struct AirStrike
    {
        public Vector3 Position;
        public Vector3 Direction;
        public float Delay;
        public int OwnerId;
        public Team Team;
        public float Radius;
        public float Damage;
        public float Knockback;
        public float Scale;
        /// <summary>True for the Target Painter: a line of bombs instead of one beam.</summary>
        public bool Bomber;
    }

    private readonly List<AirStrike> _strikes = new();

    private void UpdateAirStrikes(float dt)
    {
        for (int i = _strikes.Count - 1; i >= 0; i--)
        {
            AirStrike s = _strikes[i];
            s.Delay -= dt;
            _strikes[i] = s;
            // Warn the ground: a beam growing over the target is what makes this survivable.
            Particles.Spawn(BlendMode.Additive, s.Position + new Vector3(0f, 1f, 0f), Vector3.Zero,
                new Vector4(s.Bomber ? 1f : 0.6f, 0.8f, 1f, 0.5f), new Vector4(0.6f, 0.8f, 1f, 0f),
                0.9f, 0.2f, 0.2f, Spr.Flare);
            if (s.Delay > 0f) continue;
            _strikes.RemoveAt(i);

            Pawn owner = FindPawn(s.OwnerId);
            if (s.Bomber)
            {
                // A bomber run: five blasts walked along the painter's line of sight.
                for (int b = 0; b < 5; b++)
                {
                    Vector3 at = s.Position + s.Direction * (b - 2) * 7f;
                    Explode(at, s.Radius, s.Damage * s.Scale, s.Knockback, owner, DamageType.Explosion);
                }
            }
            else
            {
                Explode(s.Position, s.Radius, s.Damage * s.Scale, s.Knockback, owner, DamageType.Explosion);
                for (int k = 0; k < 8; k++)
                    Particles.Spawn(BlendMode.Additive, s.Position + new Vector3(0f, k * 4f, 0f),
                        Vector3.Zero, new Vector4(0.7f, 0.9f, 1f, 0.9f), new Vector4(0.4f, 0.7f, 1f, 0f),
                        3.4f, 0.5f, 0.5f, Spr.Flare);
            }
            OnSound?.Invoke(SoundId.Nuke, s.Position, 1.2f);
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
                // A landed spider mine is not finished: it picks a victim and starts crawling.
                if (p.Kind == ProjectileKind.SpiderMine && p.ArmDelay <= 0f) WakeSpiderMine(ref p);
                else continue;
            }

            if (p.Homing) SteerHomingProjectile(ref p, dt);

            // Ballistic projectiles obey the arena's gravity, so grenades really do float on
            // the low-gravity rooftop maps.
            if (p.AffectedByGravity) p.Velocity.Y -= Physics.Gravity * Level.GravityScale * dt;

            Vector3 next = p.Position + p.Velocity * dt;

            // --- pawn hits ---
            Pawn hit = TracePawnsSphere(p.Position, next, p.Radius, p.OwnerId, out Vector3 hitPoint,
                out bool headshot);
            // A translocator disc and a bombing-run ball are not ordnance: they bounce off people.
            // A laid grenade sticks to whoever it lands on and still waits for the clicker.
            if (hit != null && (p.Recallable || p.Catchable)) hit = null;
            if (hit != null && p.RemoteDetonated && p.ArmDelay <= 0f)
            {
                p.Position = hitPoint;
                p.Velocity = Vector3.Zero;
                p.Stuck = true;
                continue;
            }
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

        if (!NodeNetworkMode) return;
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
        // A raised Shield Gun only covers the direction it is pointed. Facing the wrong way with
        // it up is exactly as bad as not having it, which is what keeps it from being a free
        // damage reduction you leave switched on.
        if (target.ShieldRaised && attacker != null)
        {
            Vector3 from = MathX.SafeNormalize((attacker.Position - target.Position).FlatXZ(),
                target.ForwardFlat);
            if (Vector3.Dot(from, target.ForwardFlat) > 0.35f)
            {
                float absorbed = MathF.Min(target.ShieldEnergy, amount * 0.75f);
                target.ShieldEnergy -= absorbed;
                target.ShieldRechargeDelay = 1.1f;
                amount -= absorbed;
                if (target.ShieldEnergy <= 0.01f) target.ShieldRaised = false;
            }
        }

        float healthBefore = target.Health;
        float armorBefore = target.Armor;
        target.ApplyDamage(amount, type);
        float appliedDamage = MathF.Max(0f,
            healthBefore - target.Health + armorBefore - target.Armor);
        target.LastDamageTime = Time;
        // Riding the board means giving up the ability to take a hit. Anything past a graze puts
        // the rider on the floor for a moment, which is what stops it being a free speed boost in
        // a firefight. Fall damage is handled where the landing is, so it does not double-stun.
        if (target.OnHoverboard && type != DamageType.Fall
            && appliedDamage >= Physics.HoverboardKnockoffDamage)
            KnockOffHoverboard(target, stun: true);
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
        DropCarriedOrb(victim);
        // A killed carrier drops the ball where they fell, still live for either side to take.
        if (victim.HasBall) ReleaseBall(victim, victim.Velocity * 0.5f, thrown: false);
        victim.OnHoverboard = false;
        victim.GrappleVehicleId = -1;

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
        if (!NodeNetworkMode) return -1;

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
            case PickupKind.WeaponLocker:
                {
                    if (Mode.Kind == GameModeKind.Instagib) return false;
                    // Takes if anything on the rack is new or if it can top somebody up. Refusing
                    // when the whole rack is already owned is what keeps a locker from being a
                    // tripwire that respawns forever under a player standing next to it.
                    bool tookAnything = false;
                    foreach (WeaponKind w in pu.LockerWeapons)
                    {
                        if (pawn.GiveWeapon(w, autoSwitch: false)) tookAnything = true;
                        var def = Weapons.Get(w);
                        if (def.Ammo != AmmoKind.None && pawn.GiveAmmo(def.Ammo, def.PickupAmmo))
                            tookAnything = true;
                    }
                    if (!tookAnything) return false;
                    fb?.Pickup(Loc.PickedUp(Loc.WeaponLocker));
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

    // ---------------------------------------------------------------- bombing run

    /// <summary>
    /// Picks the ball up, and carries it. Holding it costs the carrier every weapon but the Ball
    /// Launcher: that restriction is the mode, because it means a runner cannot clear their own
    /// path and the ball has to be moved by a team rather than an individual.
    /// </summary>
    private void UpdateBallCarry(Pawn pawn, float dt)
    {
        if (Mode.Kind != GameModeKind.BombingRun
            || Mode.State is MatchState.Warmup or MatchState.Finished) return;
        var br = BombingRun;
        if (br.RoundResetActive) return;

        if (br.Carrier == pawn.Id)
        {
            br.Position = pawn.Center;
            br.LooseTimer = 0f;
            pawn.HasBall = true;
            // Re-assert every frame rather than only at pickup: walking over a weapon while
            // carrying would otherwise auto-switch the carrier back onto a gun, which is exactly
            // the thing the mode forbids.
            if (pawn.Weapon != WeaponKind.BallLauncher) ForceBallLauncher(pawn);
            pawn.Health = MathF.Min(pawn.MaxHealth,
                pawn.Health + BombingRunState.CarrierHealPerSecond * dt);
            return;
        }
        if (br.Carrier >= 0 || !pawn.Alive) return;
        if (br.LastThrowerPawn == pawn.Id && br.ThrowerPickupDelay > 0f) return;
        if (Vector3.Distance(pawn.Center, br.Position) > BombingRunState.PickupRadius) return;

        br.Carrier = pawn.Id;
        br.LastTouch = pawn.Team;
        br.LastTouchPawn = pawn.Id;
        br.LooseTimer = 0f;
        br.InFlight = false;
        pawn.HasBall = true;
        pawn.BallPassTargetId = -1;
        BallPickups++;
        ForceBallLauncher(pawn);
        Broadcast(pawn.Team == Team.Red ? Loc.AnnBallTakenRed : Loc.AnnBallTakenBlue,
            GameTypes.TeamColor(pawn.Team), 2.0f);
        OnSound?.Invoke(SoundId.FlagTaken, pawn.Position, 1.2f);
    }

    /// <summary>
    /// The carrier's inventory is replaced by the Ball Launcher for as long as they hold the ball.
    /// The weapons themselves are untouched, so dropping it hands everything straight back.
    /// </summary>
    private static void ForceBallLauncher(Pawn pawn)
    {
        pawn.HasWeapon[(int)WeaponKind.BallLauncher] = true;
        pawn.Weapon = WeaponKind.BallLauncher;
        pawn.PendingWeapon = WeaponKind.BallLauncher;
    }

    /// <summary>
    /// The team-mate an alternate-fire pass should go to: the closest one ahead of the carrier
    /// with a clear line, so a pass cannot be thrown into the wall the carrier is hiding behind.
    /// </summary>
    public Pawn BestBallPassTarget(Pawn pawn)
    {
        Pawn best = null;
        float bestDist = float.MaxValue;
        Vector3 goal = BombingRun.TargetGoal(pawn.Team);
        float carrierToGoal = Vector3.Distance(pawn.Center, goal);
        foreach (var mate in Pawns)
        {
            if (mate == pawn || !mate.Alive || mate.Team != pawn.Team) continue;
            float d = Vector3.Distance(pawn.Center, mate.Center);
            if (d > 55f || d >= bestDist) continue;
            // Only pass forwards. A backward pass is legal but never what the bot wanted.
            if (Vector3.Distance(mate.Center, goal) > carrierToGoal - 2f) continue;
            if (Level.Collision.Raycast(pawn.Center, mate.Center).Hit) continue;
            best = mate;
            bestDist = d;
        }
        return best;
    }

    /// <summary>Hands the ball back: the carrier gets their guns, the ball gets its physics.</summary>
    public void ReleaseBall(Pawn pawn, Vector3 velocity, bool thrown)
    {
        var br = BombingRun;
        if (br.Carrier != pawn.Id) return;
        br.Carrier = -1;
        br.Position = pawn.Center;
        br.Velocity = velocity;
        br.InFlight = thrown;
        br.LooseTimer = 0f;
        br.LastTouch = pawn.Team;
        br.LastTouchPawn = pawn.Id;
        br.LastThrowerPawn = thrown ? pawn.Id : -1;
        br.ThrowerPickupDelay = thrown ? BombingRunState.ThrowerTouchDelay : 0f;
        pawn.HasBall = false;
        pawn.BallPassTargetId = -1;
        pawn.HasWeapon[(int)WeaponKind.BallLauncher] = false;
        if (pawn.Weapon == WeaponKind.BallLauncher) pawn.SwitchToBestAvailable();
    }

    /// <summary>
    /// Moves a loose ball, tests both hoops, and returns an abandoned ball to midfield. A goal is
    /// only credited to the side that last touched it, so a defender booting it clear of their own
    /// ring can never score on themselves.
    /// </summary>
    private void UpdateBombingRun(float dt)
    {
        if (Mode.Kind != GameModeKind.BombingRun
            || Mode.State is MatchState.Warmup or MatchState.Finished) return;
        var br = BombingRun;

        br.ThrowerPickupDelay = MathF.Max(0f, br.ThrowerPickupDelay - dt);
        if (br.ThrowerPickupDelay <= 0f) br.LastThrowerPawn = -1;

        if (br.RoundResetActive)
        {
            br.ResetRemaining = MathF.Max(0f, br.ResetRemaining - dt);
            if (br.ResetRemaining <= 0f) ResetBombingRunRound();
            return;
        }

        if (!br.Held)
        {
            br.Velocity.Y -= Physics.Gravity * Level.GravityScale * dt;
            Vector3 next = br.Position + br.Velocity * dt;
            var hit = Level.Collision.Raycast(br.Position, next);
            if (hit.Hit)
            {
                // Bounces, losing most of its energy, so a missed shot stays near the hoop
                // instead of skittering back to midfield on its own.
                br.Position = hit.Point + hit.Normal * 0.2f;
                br.Velocity = Vector3.Reflect(br.Velocity, hit.Normal) * 0.32f;
                br.InFlight = false;
            }
            else
            {
                br.Position = next;
            }

            br.LooseTimer += dt;
            if (br.Position.Y < Level.KillPlaneY || br.LooseTimer >= BombingRunState.ReturnSeconds)
            {
                br.ReturnToMidfield();
                Broadcast(Loc.AnnBallReturned, new Vector3(0.9f, 0.85f, 0.5f), 1.8f);
                return;
            }
        }

        var evt = br.CheckGoal(out Team scorer, out int scorerPawn);
        if (evt is BallEvent.None) return;

        var by = FindPawn(scorerPawn);
        int points = br.ScoreFor(evt);
        BallGoals++;
        Mode.OnBombingRunScore(this, scorer, points);
        if (by != null)
        {
            by.Captures++;
            AddKillFeed(evt == BallEvent.RunGoal ? Loc.BrRunGoal(by.Name) : Loc.BrThrowGoal(by.Name),
                GameTypes.TeamColor(scorer));
        }
        Broadcast(scorer == Team.Red ? Loc.AnnRedScores : Loc.AnnBlueScores,
            GameTypes.TeamColor(scorer), 2.4f);
        OnSound?.Invoke(SoundId.FlagCapture, br.Position, 1.4f);

        // Running it in drops the carrier through the ring. On Anubis that is a pit, and the
        // original kills you for it; here the ring simply hands the ball back to midfield and
        // whatever is under it decides the runner's fate.
        if (by != null && evt == BallEvent.RunGoal)
        {
            by.HasBall = false;
            by.HasWeapon[(int)WeaponKind.BallLauncher] = false;
            if (by.Weapon == WeaponKind.BallLauncher) by.SwitchToBestAvailable();
        }
        if (Mode.State != MatchState.Finished) br.BeginRoundReset();
    }

    private void ResetBombingRunRound()
    {
        // The original removes every live projectile and returns every player to team starts with
        // a restored default loadout. This prevents a pre-goal rocket or mine deciding the next
        // possession before anyone can move.
        Array.Clear(Projectiles);
        foreach (Pawn pawn in Pawns)
        {
            if (pawn.InVehicle) ExitVehicle(pawn);
            RespawnPawn(pawn);
        }
        BombingRun.ReturnToMidfield();
        Broadcast(Loc.AnnBombingRunRestart, new Vector3(1f, 0.85f, 0.3f), 1.5f);
    }

    /// <summary>
    /// Onslaught. Touching a reachable neutral pad starts autonomous construction; enemy nodes
    /// and cores take weapon damage only. In overtime each core drains in proportion to how much
    /// of the non-core network the opposing team controls.
    /// </summary>
    private void UpdateOnslaught(float dt)
    {
        if (!NodeNetworkMode) return;
        if (Mode.State is MatchState.Warmup or MatchState.Finished) return;
        var state = Onslaught;

        if (Mode.State == MatchState.Overtime && DrainOnslaughtCores(dt)) return;

        // Orbs first: shielding a node has to be established before anything can shoot at it this
        // frame, or a node the carrier is standing on would still take a tick of damage.
        if (Mode.Kind == GameModeKind.Warfare)
        {
            UpdateOrbs(dt);
            if (TickAuxiliaryNodes(dt)) return;
        }

        for (int i = 0; i < state.Nodes.Count; i++)
        {
            var node = state.Nodes[i];
            NodeEvent construction = state.TickConstruction(i, dt, out PowerNode completed,
                out int builderId);
            if (construction == NodeEvent.Captured)
            {
                Pawn builder = FindPawn(builderId)
                    ?? Pawns.FirstOrDefault(p => p.Team == completed.Team);
                if (builder != null) HandleNodeEvent(construction, completed, builder);
                ActivateNodeVehicles(i, completed.Team);
                node = state.Nodes[i];
            }
            float reach = node.IsCore ? 6f : 4.5f;
            foreach (var pawn in Pawns)
            {
                if (!pawn.Alive || pawn.Team == Team.None) continue;
                Vector3 d = pawn.Position - node.Position;
                if (MathF.Abs(d.Y) > 6f) continue;
                if (new Vector2(d.X, d.Z).LengthSquared() > reach * reach) continue;

                var evt = state.Touch(i, pawn.Team, pawn.Id, out var touched);
                HandleNodeEvent(evt, touched, pawn);
            }
        }
    }

    // ---------------------------------------------------------------- hoverboard

    /// <summary>Every player carries a hoverboard in the vehicle gametypes, and nowhere else.</summary>
    public bool HoverboardAllowed => Mode.Kind is GameModeKind.Warfare or GameModeKind.Onslaught
        or GameModeKind.Assault;

    /// <summary>
    /// Deploy, stow, grapple and tow. The board is not a vehicle: the rider stays a pawn, keeps
    /// carrying the orb, and can be shot off — which is the whole character of the thing.
    /// </summary>
    private void UpdateHoverboard(Pawn pawn, in PawnInput input, float dt)
    {
        pawn.HoverboardStun = MathF.Max(0f, pawn.HoverboardStun - dt);
        if (!HoverboardAllowed)
        {
            pawn.OnHoverboard = false;
            pawn.GrappleVehicleId = -1;
            return;
        }

        if (input.Hoverboard)
        {
            if (pawn.OnHoverboard) KnockOffHoverboard(pawn, stun: false);
            else if (pawn.CanRideHoverboard)
            {
                pawn.OnHoverboard = true;
                HoverboardRides++;
                OnSound?.Invoke(SoundId.JumpPad, pawn.Position, 0.5f);
            }
        }
        if (!pawn.OnHoverboard) { pawn.GrappleVehicleId = -1; return; }

        // Alt-fire is the grapple while riding — there is no weapon to fire it at anyway.
        if (input.AltFire && pawn.GrappleVehicleId < 0) AttachGrapple(pawn);
        else if (input.AltFire && pawn.GrappleVehicleId >= 0) pawn.GrappleVehicleId = -1;

        Vehicle tow = FindVehicle(pawn.GrappleVehicleId);
        if (tow == null || !tow.Alive)
        {
            pawn.GrappleVehicleId = -1;
            return;
        }

        // Tow point sits behind the vehicle. The rider is dragged toward it rather than snapped
        // to it, so they swing out on corners and clip scenery — which is what makes towing risky.
        Vector3 back = new(-MathF.Sin(tow.Yaw), 0f, -MathF.Cos(tow.Yaw));
        Vector3 anchor = tow.Position + back * (VehicleDef.Get(tow.Kind).HalfExtents.Z + 3.4f);
        Vector3 toAnchor = anchor - pawn.Position;
        if (toAnchor.Length() > Physics.GrappleBreakRange)
        {
            pawn.GrappleVehicleId = -1;
            return;
        }
        pawn.Velocity += MathX.SafeNormalize(toAnchor, Vector3.Zero)
            * Physics.GrappleAcceleration * dt;
        // Flyers lift the rider off the ground entirely, exactly as in the original.
        if (VehicleDef.Get(tow.Kind).Motion == VehicleMotion.Air && anchor.Y > pawn.Position.Y + 0.5f)
            pawn.Velocity.Y = MathF.Max(pawn.Velocity.Y, 4f);
    }

    private void AttachGrapple(Pawn pawn)
    {
        Vehicle best = null;
        float bestDistance = Physics.GrappleRange;
        foreach (var v in Vehicles)
        {
            if (!v.Alive || v.Kind == VehicleKind.Hoverboard) continue;
            if (v.Team != Team.None && pawn.Team != Team.None && v.Team != pawn.Team) continue;
            float d = Vector3.Distance(v.Position, pawn.Position);
            if (d < bestDistance) { bestDistance = d; best = v; }
        }
        if (best == null) return;
        pawn.GrappleVehicleId = best.Id;
        HoverboardTows++;
        OnSound?.Invoke(SoundId.HammerHit, pawn.Position, 0.45f);
    }

    /// <summary>
    /// Throws a rider off. Damage does this with a stun; stowing it voluntarily does not. Either
    /// way the tow line goes with it.
    /// </summary>
    public void KnockOffHoverboard(Pawn pawn, bool stun)
    {
        if (pawn == null || !pawn.OnHoverboard) return;
        pawn.OnHoverboard = false;
        pawn.GrappleVehicleId = -1;
        if (!stun) return;
        pawn.HoverboardStun = Physics.HoverboardStunSeconds;
        // Landing on your face costs momentum as well as time.
        pawn.Velocity.X *= 0.25f;
        pawn.Velocity.Z *= 0.25f;
        OnSound?.Invoke(SoundId.Land, pawn.Position, 0.7f);
    }

    // ---------------------------------------------------------------- warfare

    /// <summary>
    /// Moves both orbs, applies node shields, and resolves instant captures. Everything the orb
    /// does happens here; the node graph itself only exposes <see cref="OnslaughtState.OrbCapture"/>
    /// and an <see cref="PowerNode.OrbShield"/> flag it reads back.
    /// </summary>
    private void UpdateOrbs(float dt)
    {
        foreach (var node in Onslaught.Nodes) node.OrbShield = Team.None;

        foreach (WarfareOrb orb in Warfare.Orbs)
        {
            Pawn carrier = FindPawn(orb.CarrierId);
            // A carrier who dies, leaves the match, or climbs into a vehicle drops it. Riding the
            // hoverboard is fine, which is exactly what makes a fast orb run possible.
            if (carrier != null && (!carrier.Alive || carrier.Team != orb.Team || carrier.VehicleId > 0))
                DropOrb(orb, carrier);
            carrier = FindPawn(orb.CarrierId);

            if (carrier != null)
            {
                orb.Position = carrier.Position + new Vector3(0f, 0.9f, 0f);
                ResolveOrbAtNodes(orb, carrier, dt);
                continue;
            }

            if (orb.Dropped)
            {
                orb.DropTimer -= dt;
                if (orb.DropTimer <= 0f) ReturnOrb(orb, null);
                else CollectDroppedOrb(orb);
                continue;
            }

            // Sitting at home: keep it on the furthest-forward live spawn and wait to be taken.
            orb.Position = Warfare.HomeFor(Level, Onslaught, orb.Team);
            CollectDroppedOrb(orb);
        }
    }

    /// <summary>Instant capture on contact, and the protective shield on nodes already held.</summary>
    private void ResolveOrbAtNodes(WarfareOrb orb, Pawn carrier, float dt)
    {
        for (int i = 0; i < Onslaught.Nodes.Count; i++)
        {
            PowerNode node = Onslaught.Nodes[i];
            if (node.IsCore) continue;
            float distance = Vector3.Distance(carrier.Position, node.Position);

            if (node.Team == orb.Team && node.IsActive)
            {
                if (distance > WarfareOrb.ShieldRadius) continue;
                node.OrbShield = orb.Team;
                node.Health = MathF.Min(node.MaxHealth, node.Health + node.MaxHealth * 0.35f * dt);
                continue;
            }

            if (distance > WarfareOrb.TouchRadius) continue;
            if (!Onslaught.OrbCapture(i, orb.Team, carrier.Id, out PowerNode captured)) continue;

            OnslaughtNodeCaptures++;
            WarfareOrbCaptures++;
            carrier.Captures++;
            AddKillFeed(Loc.WarOrbCaptured(carrier.Name, captured.Name), GameTypes.TeamColor(orb.Team));
            OnSound?.Invoke(SoundId.FlagCapture, captured.Position, 1f);
            ActivateNodeVehicles(i, orb.Team);
            AnnounceCoreState();
            // The orb is spent on the capture: it goes home, which is what stops one carrier from
            // sweeping the whole map in a single run.
            ReturnOrb(orb, carrier);
            return;
        }
    }

    private void CollectDroppedOrb(WarfareOrb orb)
    {
        foreach (var pawn in Pawns)
        {
            if (!pawn.Alive || pawn.Team != orb.Team || pawn.VehicleId > 0) continue;
            // Measure from the chest: an orb resting at waist height on a pad sits 2 m above the
            // feet, and a foot-to-orb test left a bot standing right on top of it unable to reach.
            if (Vector3.Distance(pawn.Center, orb.Position) > WarfareOrb.TouchRadius) continue;
            orb.CarrierId = pawn.Id;
            orb.DropTimer = -1f;
            WarfareOrbPickups++;
            FeedbackFor(pawn).Big(Loc.WarOrbTaken, GameTypes.TeamColor(orb.Team), 1.4f);
            AddKillFeed(Loc.WarOrbCarrier(pawn.Name), GameTypes.TeamColor(orb.Team));
            OnSound?.Invoke(SoundId.FlagTaken, orb.Position, 1f);
            return;
        }
    }

    public void DropOrb(WarfareOrb orb, Pawn carrier)
    {
        if (orb == null || orb.CarrierId < 0) return;
        orb.Position = (carrier?.Position ?? orb.Position) + new Vector3(0f, 0.4f, 0f);
        orb.CarrierId = -1;
        orb.DropTimer = WarfareOrb.DropTimeout;
        AddKillFeed(Loc.WarOrbDropped, GameTypes.TeamColor(orb.Team));
    }

    private void ReturnOrb(WarfareOrb orb, Pawn by)
    {
        orb.ResetTo(Warfare.HomeFor(Level, Onslaught, orb.Team));
        if (by == null) AddKillFeed(Loc.WarOrbReturned, GameTypes.TeamColor(orb.Team));
        OnSound?.Invoke(SoundId.FlagReturn, orb.Position, 0.9f);
    }

    /// <summary>Drops whichever orb a pawn was carrying. Called on death and on boarding.</summary>
    public void DropCarriedOrb(Pawn pawn)
    {
        if (Mode.Kind != GameModeKind.Warfare || pawn == null) return;
        foreach (WarfareOrb orb in Warfare.Orbs)
            if (orb.CarrierId == pawn.Id) DropOrb(orb, pawn);
    }

    /// <summary>
    /// The sacrifice play: walking up to a dropped enemy orb and forcing it to respawn, at the
    /// cost of 100 health. Worth it only when that orb is about to take a node back.
    /// </summary>
    public bool SacrificeToEnemyOrb(Pawn pawn)
    {
        if (Mode.Kind != GameModeKind.Warfare || pawn == null || !pawn.Alive) return false;
        foreach (WarfareOrb orb in Warfare.Orbs)
        {
            if (orb.Team == pawn.Team || !orb.Dropped) continue;
            if (Vector3.Distance(pawn.Center, orb.Position) > WarfareOrb.TouchRadius) continue;
            ReturnOrb(orb, pawn);
            // Armour soaks it, so a shield belt turns this into a free play — as in the original.
            Damage(pawn, null, WarfareOrb.SacrificeHealth, DamageType.Energy, MathX.Up);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Runs the auxiliary-node clocks and pays them out. Returns true if a core died, so the
    /// caller stops walking the node list this frame.
    /// </summary>
    private bool TickAuxiliaryNodes(float dt)
    {
        PowerNode finished = Onslaught.TickCountdowns(dt);
        if (finished == null) return false;

        if (finished.Role == NodeRole.Vehicle)
        {
            GrantNodeVehicle(finished);
            // A vehicle node re-arms only once its reward is gone, so the map never stacks two.
            finished.CountdownRemaining = -1f;
            return false;
        }

        Team owner = finished.Team;
        Team enemy = Opposite(owner);
        AddKillFeed(Loc.WarCountdownDone(finished.Name), GameTypes.TeamColor(owner));
        OnSound?.Invoke(SoundId.AnnounceMajor, finished.Position, 1f);
        // The clock finishing also knocks the node down: the fight over it starts again.
        finished.Team = Team.None;
        finished.Built = 0f;
        finished.Health = finished.MaxHealth;
        finished.CountdownRemaining = -1f;

        // Two payouts, distinguished by whether the map asked for core damage. Avalanche's
        // countdown nodes take out the enemy's prime node instead of chipping the core, which is
        // a far bigger swing: it drops the whole link into their base.
        if (finished.CoreDamageFraction <= 0f)
        {
            DestroyPrimeNodeOf(enemy);
            return false;
        }

        PowerNode core = Onslaught.CoreOf(enemy);
        if (core == null) return false;
        core.Health -= core.MaxHealth * finished.CoreDamageFraction;
        if (core.Health > 0f) return false;
        core.Health = 0f;
        CompleteOnslaughtRound(owner, core.Position);
        return true;
    }

    private void DestroyPrimeNodeOf(Team team)
    {
        for (int i = 0; i < Onslaught.Nodes.Count; i++)
        {
            PowerNode node = Onslaught.Nodes[i];
            if (!node.IsPrime || node.Team != team || !node.IsActive) continue;
            node.Team = Team.None;
            node.Built = 0f;
            node.BuildingFor = Team.None;
            node.BuilderPawnId = -1;
            node.Health = node.MaxHealth;
            AddKillFeed(Loc.OnsNodeLost(node.Name), new Vector3(1f, 0.7f, 0.3f));
            ResetOnslaughtVehiclePads();
            AnnounceCoreState();
            return;
        }
    }

    /// <summary>Parks the vehicle a vehicle node just earned, unless the previous one is still alive.</summary>
    private void GrantNodeVehicle(PowerNode node)
    {
        if (node.RewardVehicle >= VehicleKind.Count) return;
        int index = Onslaught.Nodes.IndexOf(node);
        if (Warfare.NodeVehicles.TryGetValue(index, out int existingId))
        {
            Vehicle existing = FindVehicle(existingId);
            // Only one Leviathan may be in play at a time; the last one has to burn first.
            if (existing != null && existing.Alive) return;
        }

        var v = new Vehicle { Id = NextVehicleId++ };
        VehicleDef def = VehicleDef.Get(node.RewardVehicle);
        v.Configure(node.RewardVehicle, node.RewardPosition + new Vector3(0f, def.HalfExtents.Y, 0f),
            node.RewardYaw);
        v.SpawnRespawnSeconds = float.PositiveInfinity;   // no automatic replacement; earn another
        v.AuthoredSpawnTeam = node.Team;
        v.SpawnTeam = node.Team;
        v.Team = node.Team;
        v.SpawnNodeIndex = -1;
        Vehicles.Add(v);
        Warfare.NodeVehicles[index] = v.Id;
        AddKillFeed(Loc.WarVehicleReady(VehicleDef.Get(node.RewardVehicle).Name),
            GameTypes.TeamColor(node.Team));
        OnSound?.Invoke(SoundId.AnnounceMajor, node.RewardPosition, 1.1f);
    }

    private bool DrainOnslaughtCores(float dt)
    {
        int total = Onslaught.Nodes.Count(n => !n.IsCore);
        if (total <= 0) return false;
        PowerNode red = Onslaught.CoreOf(Team.Red);
        PowerNode blue = Onslaught.CoreOf(Team.Blue);
        if (red == null || blue == null) return false;
        float redRate = OnslaughtState.OvertimeCoreDrainPerSecond
            * Onslaught.NodesHeldBy(Team.Blue) / total;
        float blueRate = OnslaughtState.OvertimeCoreDrainPerSecond
            * Onslaught.NodesHeldBy(Team.Red) / total;
        red.Health = MathF.Max(0f, red.Health - redRate * dt);
        blue.Health = MathF.Max(0f, blue.Health - blueRate * dt);
        if (red.Health > 0f && blue.Health > 0f) return false;

        Team winner = red.Health <= 0f && blue.Health <= 0f
            ? (redRate < blueRate ? Team.Red : blueRate < redRate ? Team.Blue : Team.None)
            : red.Health <= 0f ? Team.Blue : Team.Red;
        if (winner == Team.None) winner = Onslaught.NodesHeldBy(Team.Red) >= Onslaught.NodesHeldBy(Team.Blue)
            ? Team.Red : Team.Blue;
        PowerNode destroyed = winner == Team.Red ? blue : red;
        CompleteOnslaughtRound(winner, destroyed.Position);
        return true;
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
                OnslaughtNodeCaptures++;
                actor.Captures++;
                AddKillFeed(Loc.OnsNodeCaptured(actor.Name, node.Name), GameTypes.TeamColor(actor.Team));
                OnSound?.Invoke(SoundId.FlagCapture, node.Position, 1f);
                int index = Onslaught.Nodes.IndexOf(node);
                if (index >= 0) ActivateNodeVehicles(index, node.Team);
                AnnounceCoreState();
                return false;

            case NodeEvent.Neutralised:
                AddKillFeed(Loc.OnsNodeLost(node.Name), new Vector3(1f, 0.7f, 0.3f));
                OnSound?.Invoke(SoundId.Explosion, node.Position, 1f);
                Particles.Explosion(node.Position + MathX.Up * 2f, 2.6f, new Vector3(1f, 0.6f, 0.2f));
                AnnounceCoreState();
                return false;

            case NodeEvent.CoreDestroyed:
                CompleteOnslaughtRound(actor.Team, node.Position);
                return true;
        }
        return false;
    }

    private void CompleteOnslaughtRound(Team winner, Vector3 destroyedCore)
    {
        int points = Mode.State == MatchState.Overtime ? 1 : 2;
        Mode.TeamScores[(int)winner] += points;
        Particles.Explosion(destroyedCore + MathX.Up * 3f, 5f, new Vector3(1f, 0.75f, 0.3f));
        OnSound?.Invoke(SoundId.Nuke, destroyedCore, 2f);
        if (Mode.TeamScores[(int)winner] >= OnslaughtState.GoalScore)
        {
            Mode.WinningTeam = winner;
            Mode.Finish(this);
            return;
        }

        foreach (Pawn pawn in Pawns) if (pawn.InVehicle) ExitVehicle(pawn);
        Onslaught.ResetRound(swapSides: true);
        ResetOnslaughtVehiclePads();
        foreach (Pawn pawn in Pawns) RespawnPawn(pawn);
        Mode.State = MatchState.InProgress;
        Mode.TimeRemaining = Mode.TimeLimit;
        _lastVulnerable = Team.None;
        Broadcast(Loc.OnsNextRound, GameTypes.TeamColor(winner), 3f);
    }

    private void ActivateNodeVehicles(int nodeIndex, Team owner)
    {
        foreach (Vehicle vehicle in Vehicles)
        {
            if (vehicle.SpawnNodeIndex != nodeIndex || vehicle.Alive) continue;
            // A pad authored for one side stays that side's; only unclaimed pads follow the node.
            // Warfare's mirrored maps rely on this: the Manta and the Viper share a node.
            if (vehicle.AuthoredSpawnTeam != Team.None && vehicle.AuthoredSpawnTeam != owner) continue;
            if (vehicle.AuthoredSpawnTeam == Team.None) vehicle.SpawnTeam = owner;
            vehicle.Reset();
        }
        // Losing the node takes the other side's cars away again, or a captured node would end up
        // with both teams' vehicles parked on it.
        foreach (Vehicle vehicle in Vehicles)
        {
            if (vehicle.SpawnNodeIndex != nodeIndex || !vehicle.Alive) continue;
            if (vehicle.AuthoredSpawnTeam == Team.None || vehicle.AuthoredSpawnTeam == owner) continue;
            if (vehicle.Occupied) continue;   // never delete one out from under a driver
            vehicle.Alive = false;
            vehicle.RespawnTimer = float.PositiveInfinity;
        }
    }

    private void ResetOnslaughtVehiclePads()
    {
        foreach (Vehicle vehicle in Vehicles)
        {
            if (vehicle.SpawnNodeIndex < 0) { vehicle.Reset(); continue; }
            PowerNode node = Onslaught.Nodes[vehicle.SpawnNodeIndex];
            vehicle.SpawnTeam = node.Team;
            if (node.IsActive) vehicle.Reset();
            else
            {
                vehicle.Alive = false;
                vehicle.RespawnTimer = float.PositiveInfinity;
            }
        }
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
            // Exactly one attacker advances the objective per frame. Charges are planted by a
            // person, not by a crowd — letting every body in the ring add its own dt would make
            // a four-man rush complete a nine-second plant in two.
            Pawn planter = null;
            foreach (var pawn in Pawns)
            {
                // Touch/hold objectives are infantry interactions. AI drivers dismount as they
                // arrive, and this simulation-level check also prevents a human from capturing
                // one while insulated inside a vehicle hull.
                if (!pawn.Alive || pawn.InVehicle || pawn.Team != st.Attackers) continue;
                if (Vector3.Distance(pawn.Position, target.Position) > target.Radius) continue;
                planter = pawn;
                break;
            }

            if (planter != null)
            {
                var evt = st.Touch(planter.Team, planter.Position, dt, out var touched);
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
                AssaultObjectiveCompletions++;
                actor.Captures++;
                AddKillFeed(Loc.AsObjectiveDone(actor.Name, objective.Name), GameTypes.TeamColor(actor.Team));
                OnSound?.Invoke(SoundId.FlagCapture, objective.Position, 1.2f);
                Particles.EnergyBurst(objective.Position + MathX.Up * 1.5f, new Vector3(1f, 0.8f, 0.35f), 2.4f);
                Broadcast(Loc.AsNextObjective(Assault.CurrentObjective?.Name ?? ""),
                    new Vector3(1f, 0.85f, 0.4f), 2f);
                // When round one timed out, round two ends the instant it gets one objective
                // farther. Making it wait out the clock after already winning is not Assault.
                if (Assault.Round == 2 && Assault.TargetTime == float.MaxValue
                    && Assault.CompletedCount > Assault.TargetObjectives)
                {
                    FinishAssaultRound(false);
                    return true;
                }
                return false;

            case ObjectiveEvent.AllCompleted:
                AssaultObjectiveCompletions++;
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
        AssaultRoundsCompleted++;
        if (st.Round == 1)
        {
            st.SwapSides(attackersFinished);
            // Everyone changes ends, so everyone respawns; the clock restarts for the new side.
            foreach (var p in Pawns)
            {
                if (p.InVehicle) ExitVehicle(p);
                RespawnPawn(p);
            }
            foreach (var v in Vehicles)
            {
                v.SpawnTeam = Opposite(v.AuthoredSpawnTeam);
                v.Reset();
            }
            // If round one finished, round two receives exactly that completion time. If it did
            // not, the ordinary round limit remains and objective count becomes the target.
            Mode.TimeRemaining = st.TargetTime < float.MaxValue
                ? MathF.Min(Mode.TimeLimit, st.TargetTime)
                : Mode.TimeLimit;
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
                if (NodeNetworkMode && v.SpawnNodeIndex >= 0)
                {
                    PowerNode pad = Onslaught.Nodes[v.SpawnNodeIndex];
                    if (!pad.IsActive)
                    {
                        v.RespawnTimer = float.PositiveInfinity;
                        continue;
                    }
                    v.SpawnTeam = pad.Team;
                }
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
        // A turret aims where its occupant looks, in the pawn's view convention. A hull-mounted
        // weapon aims where the vehicle is pointed — and the vehicle's forward is +Z in model
        // space, the opposite of the pawn convention, so it needs the half turn. Without it a
        // Manta's plasma cannons fired out of the back of the vehicle.
        Vector3 aim = seatDef.Turret
            ? MathX.DirFromYawPitch(v.SeatYaw[seat], v.SeatPitch[seat])
            : MathX.DirFromYawPitch(v.Yaw + MathX.Pi, v.SeatPitch[seat]);

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
        v.RespawnTimer = v.SpawnRespawnSeconds;
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
        SubmitOrbs(scene);
        SubmitBall(scene);
        SubmitHoverboards(scene);
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
        // Studio lighting, matching the vehicle plate. With no arena and no sky there is nothing
        // else lighting the subject, so the key and ambient have to carry it on their own.
        scene.SunDirection = Vector3.Normalize(new Vector3(-0.45f, -0.72f, -0.38f));
        scene.SunColor = new Vector3(4.6f, 4.4f, 4.1f);
        scene.AmbientSky = new Vector3(0.74f, 0.76f, 0.80f);
        scene.AmbientGround = new Vector3(0.38f, 0.38f, 0.40f);
        scene.SkyTop = new Vector3(0.015f, 0.028f, 0.065f);
        scene.SkyHorizon = new Vector3(0.10f, 0.16f, 0.26f);
        scene.SkyGround = new Vector3(0.025f, 0.03f, 0.045f);
        scene.StarStrength = 0f;
        scene.CloudStrength = 0f;
        scene.EnvIntensity = 0.85f;
        scene.FogDensity = 0f;

        const float weaponScale = 1.25f;
        Vector3 weaponPosition = groundPosition + new Vector3(0f, 0.55f, 0f);
        // Shift the footprint centre onto the spin axis before rotating, so the weapon turns in
        // place rather than orbiting the grip it happens to be modelled around.
        Matrix4x4 weaponTransform = Matrix4x4.CreateScale(weaponScale)
            * Matrix4x4.CreateTranslation(_weaponModels.TurntablePivot(weapon) * -weaponScale)
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

        // Deliberately no pickup ring. It anchors the weapon to a floor in the arena, but on a
        // studio plate there is no floor — it just costs a third of the frame the weapon itself
        // should be filling.

        Vector3 tint = Weapons.Get(weapon).Tint;
        scene.AddLight(weaponPosition + new Vector3(1.8f, 2.2f, 1.3f), 7f,
            tint * 0.55f + new Vector3(0.45f), 3.8f, 2f);
        scene.AddLight(weaponPosition + new Vector3(-1.8f, 0.8f, -1.4f), 5.5f,
            new Vector3(1f, 0.38f, 0.16f), 2.1f, 1.6f);
    }

    /// <summary>
    /// One cockpit interior on the same studio plate the weapons and vehicles use. Photographing
    /// these from the driver's seat means fighting the first-person projection for every shot;
    /// what the vehicle guide actually needs is a clear look at each variant, which is a product
    /// shot. Same mesh the game draws in the seat, viewed from outside it.
    /// </summary>
    public void SubmitCockpitTurntable(RenderScene scene, CockpitKind kind, Vector3 groundPosition,
        float yaw, Vector3 tintColor)
    {
        if (_cockpitModels == null) return;
        Mesh mesh = _cockpitModels.MeshFor(kind);
        if (mesh == null) return;

        scene.SunDirection = Vector3.Normalize(new Vector3(-0.45f, -0.72f, -0.38f));
        scene.SunColor = new Vector3(4.6f, 4.4f, 4.1f);
        scene.AmbientSky = new Vector3(0.74f, 0.76f, 0.80f);
        scene.AmbientGround = new Vector3(0.38f, 0.38f, 0.40f);
        scene.SkyTop = new Vector3(0.015f, 0.028f, 0.065f);
        scene.SkyHorizon = new Vector3(0.10f, 0.16f, 0.26f);
        scene.SkyGround = new Vector3(0.025f, 0.03f, 0.045f);
        scene.StarStrength = 0f;
        scene.CloudStrength = 0f;
        scene.EnvIntensity = 0.85f;
        scene.FogDensity = 0f;

        // The interior is modelled around the eye point, which sits above and behind everything
        // in it, so it is recentred on its own bulk before spinning.
        const float scale = 1.9f;
        Vector3 centre = groundPosition + new Vector3(0f, 0.75f, 0f);
        Matrix4x4 transform = Matrix4x4.CreateTranslation(new Vector3(0f, 0.10f, 0.55f))
            * Matrix4x4.CreateScale(scale)
            * Matrix4x4.CreateRotationY(yaw)
            * Matrix4x4.CreateTranslation(centre);

        foreach (MeshSection section in _cockpitModels.SectionsFor(kind))
        {
            Material material = Materials.Get(section.Material);
            bool structural = section.Material == (int)MatId.ArmorPlate
                || section.Material == (int)MatId.RustMetal;
            var draw = MakePickupDraw(mesh, section, material, transform, centre, 2.6f);
            draw.Tint = structural ? new Vector4(tintColor * 0.55f, 1f) : material.BaseColor;
            draw.CastShadow = true;
            scene.Opaque.Add(draw);
        }

        scene.AddLight(centre + new Vector3(1.8f, 2.2f, 1.3f), 7f,
            tintColor * 0.55f + new Vector3(0.45f), 3.8f, 2f);
        scene.AddLight(centre + new Vector3(-1.8f, 0.8f, -1.4f), 5.5f,
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
        if (!NodeNetworkMode) return;
        var nodes = Onslaught.Nodes;

        for (int i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            float scale = node.IsCore ? 0.9f : 0.5f;
            Vector3 pos = node.Position + new Vector3(0, node.IsCore ? 4.2f : 3.2f, 0);
            Team visibleTeam = node.Team != Team.None ? node.Team : node.BuildingFor;
            Vector3 col = visibleTeam == Team.None ? new Vector3(0.75f, 0.75f, 0.8f)
                : GameTypes.TeamColor(visibleTeam);

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

    /// <summary>Stages the exact live vehicle mesh and materials for documentation turntables.</summary>
    public void SubmitVehicleTurntable(RenderScene scene, VehicleKind kind, Vector3 groundPosition,
        float yaw)
    {
        scene.SunDirection = Vector3.Normalize(new Vector3(-0.45f, -0.72f, -0.38f));
        scene.SunColor = new Vector3(4.6f, 4.4f, 4.1f);
        scene.AmbientSky = new Vector3(0.70f, 0.72f, 0.78f);
        scene.AmbientGround = new Vector3(0.34f, 0.34f, 0.38f);
        scene.SkyTop = new Vector3(0.015f, 0.028f, 0.065f);
        scene.SkyHorizon = new Vector3(0.10f, 0.16f, 0.26f);
        scene.SkyGround = new Vector3(0.025f, 0.03f, 0.045f);
        scene.StarStrength = 0f;
        scene.CloudStrength = 0f;
        scene.EnvIntensity = 0.75f;
        scene.FogDensity = 0f;
        VehicleDef def = VehicleDef.Get(kind);
        if (kind == VehicleKind.Darkwalker)
        {
            // Its nearly black body sits one full hull-height above the feet. Give that unique
            // silhouette enough fill to read against the studio sky without changing gameplay.
            scene.AmbientSky = new Vector3(0.86f, 0.88f, 0.94f);
            scene.AmbientGround = new Vector3(0.42f, 0.43f, 0.48f);
            scene.SunColor = new Vector3(5.6f, 5.4f, 5.1f);
        }
        Vector3 position = groundPosition + new Vector3(0f, def.HalfExtents.Y, 0f);
        // Shift the footprint centre onto the spin axis first: a Goliath's gun and a Darkwalker's
        // legs both hang well off the model origin, and without this they orbit it.
        Matrix4x4 transform = Matrix4x4.CreateTranslation(-_vehicleModels.TurntablePivot(kind))
            * Matrix4x4.CreateRotationY(yaw)
            * Matrix4x4.CreateTranslation(position);
        Mesh mesh = _vehicleModels.MeshFor(kind);
        foreach (MeshSection section in _vehicleModels.SectionsFor(kind))
        {
            scene.Opaque.Add(new DrawCall
            {
                Mesh = mesh,
                IndexOffset = section.IndexOffset,
                IndexCount = section.IndexCount,
                Transform = transform,
                Material = Materials.Get(section.Material),
                Tint = new Vector4(def.Tint, 1f),
                Alpha = 1f,
                Center = position,
                Radius = def.HalfExtents.Length() + 1f,
                BoneBase = -1,
                CastShadow = true,
            });
        }
        scene.AddLight(position + new Vector3(def.HalfExtents.X * 1.6f,
                def.HalfExtents.Y * 1.8f + 2f, def.HalfExtents.Z * 0.9f),
            MathF.Max(10f, def.HalfExtents.Length() * 4f),
            new Vector3(0.65f, 0.82f, 1f), 4.5f, 2f);
        scene.AddLight(position + new Vector3(-def.HalfExtents.X * 1.4f,
                def.HalfExtents.Y * 0.8f + 1f, -def.HalfExtents.Z),
            MathF.Max(8f, def.HalfExtents.Length() * 3f),
            new Vector3(1f, 0.38f, 0.16f), 2.4f, 1.5f);
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

    /// <summary>
    /// Draws both Warfare orbs. The carrier's beacon is deliberately loud: the original makes the
    /// orb runner the most visible player on the map, and the mode is balanced around that.
    /// </summary>
    private void SubmitOrbs(RenderScene scene)
    {
        if (Mode.Kind != GameModeKind.Warfare) return;
        foreach (WarfareOrb orb in Warfare.Orbs)
        {
            Vector3 col = GameTypes.TeamColor(orb.Team);
            Vector3 pos = orb.Position + new Vector3(0f, MathF.Sin(Time * 2.2f) * 0.12f, 0f);
            Matrix4x4 xf = Matrix4x4.CreateScale(0.62f)
                * Matrix4x4.CreateRotationY(Time * 1.4f)
                * Matrix4x4.CreateTranslation(pos);
            Mesh mesh = _pickupModels.MeshFor(PickupKind.DamageAmp);
            foreach (var section in _pickupModels.SectionsFor(PickupKind.DamageAmp))
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
                    Tint = new Vector4(col, 1f),
                    Emissive = col * 3.6f,
                    OverrideEmissive = true,
                    Alpha = 1f,
                    Center = pos,
                    Radius = 1.2f,
                    CastShadow = false,
                    UvScale = mat.UvScale,
                    OwnerView = -1,
                });
            }
            scene.AddLight(pos, orb.Held ? 16f : 10f, col, orb.Held ? 7f : 4f, 1.6f);
            // The sky beam. Only the carrier gets one — a dropped orb is meant to be findable but
            // not a lighthouse, and a parked one is already where everyone knows to look.
            if (!orb.Held) continue;
            for (int i = 1; i <= 6; i++)
                scene.AddLight(pos + new Vector3(0f, i * 3.5f, 0f), 9f, col, 2.4f, 1.4f);
        }
    }

    /// <summary>
    /// Draws the Bombing Run ball, in a carrier's hands or loose on the field. It is lit brightly
    /// on purpose: with only one ball in play, everyone needs to be able to find it instantly.
    /// </summary>
    private void SubmitBall(RenderScene scene)
    {
        if (Mode.Kind != GameModeKind.BombingRun) return;
        var br = BombingRun;
        if (br.RoundResetActive) return;
        Vector3 col = br.Carrier >= 0 && FindPawn(br.Carrier) is { } holder
            ? GameTypes.TeamColor(holder.Team)
            : new Vector3(0.95f, 0.85f, 0.42f);
        Vector3 pos = br.Held
            ? br.Position + new Vector3(0f, 0.45f, 0f)
            : br.Position + new Vector3(0f, MathF.Sin(Time * 2.4f) * 0.10f, 0f);
        Matrix4x4 xf = Matrix4x4.CreateScale(0.46f)
            * Matrix4x4.CreateRotationY(Time * 2.0f)
            * Matrix4x4.CreateTranslation(pos);
        Mesh mesh = _pickupModels.MeshFor(PickupKind.DamageAmp);
        if (mesh == null) return;
        foreach (var section in _pickupModels.SectionsFor(PickupKind.DamageAmp))
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
                Tint = new Vector4(col, 1f),
                Emissive = col * 3.2f,
                OverrideEmissive = true,
                Alpha = 1f,
                Center = pos,
                Radius = 1f,
                CastShadow = false,
                UvScale = mat.UvScale,
                OwnerView = -1,
            });
        }
        scene.AddLight(pos, br.Held ? 15f : 11f, col, br.Held ? 6f : 4f, 1.6f);
    }

    /// <summary>Draws the board under anyone riding one, using the real vehicle mesh.</summary>
    private void SubmitHoverboards(RenderScene scene)
    {
        if (!HoverboardAllowed) return;
        Mesh mesh = _vehicleModels.MeshFor(VehicleKind.Hoverboard);
        if (mesh == null) return;
        foreach (var pawn in Pawns)
        {
            if (!pawn.Alive || !pawn.OnHoverboard) continue;
            Vector3 pos = pawn.Position + new Vector3(0f, 0.14f, 0f);
            // Leans into the turn a little, so a board run reads as a board run from a distance.
            float lean = MathX.Clamp(Vector3.Dot(pawn.Velocity, pawn.RightFlat) * 0.035f, -0.35f, 0.35f);
            Matrix4x4 xf = Matrix4x4.CreateRotationZ(lean)
                * Matrix4x4.CreateRotationY(pawn.Yaw)
                * Matrix4x4.CreateTranslation(pos);
            foreach (var section in _vehicleModels.SectionsFor(VehicleKind.Hoverboard))
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
                    Tint = new Vector4(VehicleDef.Get(VehicleKind.Hoverboard).Tint, 1f),
                    Emissive = mat.Emissive,
                    Alpha = 1f,
                    Center = pos,
                    Radius = 1.6f,
                    CastShadow = true,
                    UvScale = mat.UvScale,
                    OwnerView = -1,
                });
            }
            // Tow line to the vehicle being grappled, so bystanders can see who is hitching a ride.
            Vehicle tow = FindVehicle(pawn.GrappleVehicleId);
            if (tow == null || !tow.Alive) continue;
            Vector3 a = pawn.Center;
            for (int i = 1; i <= 6; i++)
            {
                Vector3 p = Vector3.Lerp(a, tow.Position, i / 7f);
                Particles.Spawn(BlendMode.Additive, p, Vector3.Zero,
                    new Vector4(0.6f, 0.85f, 1f, 0.55f), new Vector4(0.6f, 0.85f, 1f, 0f),
                    0.16f, 0.06f, 0.06f, Spr.Flare);
            }
        }
    }

    /// <summary>Adds the first-person weapon for one view. Called once per local player.</summary>
    public void SubmitViewModel(RenderScene scene, int viewIndex, Pawn pawn, in Camera camera,
        float aspect = 16f / 9f)
    {
        if (!pawn.Alive) return;
        // Aboard a vehicle the hands and gun are simply wrong: the pawn is strapped into
        // something, and which seat it is strapped into is information the player needs.
        if (pawn.InVehicle) { SubmitCockpit(scene, viewIndex, pawn, camera, aspect); return; }
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

    /// <summary>
    /// The interior of the seat the player is riding in. The archetype tells them their job — a
    /// yoke means they are steering, a gun mount means they are on a weapon, a rail means they
    /// are a passenger — and the hull's own tint tells them what they are riding in.
    /// </summary>
    private void SubmitCockpit(RenderScene scene, int viewIndex, Pawn pawn, in Camera camera, float aspect)
    {
        var v = FindVehicle(pawn.VehicleId);
        if (v == null || !v.Alive) return;
        // The interior is bolted to the hull, so it takes the ride rather than the head: it leans
        // with roll, pitches with the chassis, and shrugs off the bob a walking pawn would have.
        // The steering lean is deliberately larger than the hull's — a driver reads their turn
        // from the yoke swinging, and a rigidly-welded interior gives no such feedback.
        SubmitCockpitPlate(scene, viewIndex, CockpitModels.For(v.Def, pawn.VehicleSeat), v.Def.Tint,
            camera, aspect, MathX.Clamp(v.YawDelta * 26f, -0.34f, 0.34f),
            MathX.Clamp(v.Roll, -0.5f, 0.5f), MathX.Clamp(v.Pitch, -0.4f, 0.4f));
    }

    /// <summary>
    /// Draws one interior against an explicit kind and tint. Separated from the riding path so
    /// the documentation capture can photograph every variant without needing a live vehicle and
    /// a seat to sit in — the whole point being that the picture shows the same mesh the game
    /// draws, not a stand-in built for the screenshot.
    /// </summary>
    public void SubmitCockpitPlate(RenderScene scene, int viewIndex, CockpitKind kind, Vector3 tintColor,
        in Camera camera, float aspect, float steer = 0f, float sway = 0f, float lift = 0f)
    {
        if (_cockpitModels == null) return;
        Mesh mesh = _cockpitModels.MeshFor(kind);
        if (mesh == null) return;
        // CockpitModels authors its geometry against a 90° vertical field of view, where the
        // visible half-height at depth d is exactly d. It has to be brought in to match the
        // projection it is actually drawn with — and that is NOT the player's camera. The
        // renderer gives first-person geometry its own fixed 58° frustum so a wide FOV setting
        // cannot stretch the held weapon, which at 16:9 is about 35° vertical against the
        // camera's 63°. Fitting to the camera left the interior roughly twice too large, with
        // only its outer corners intruding on the frame.
        //
        // The correction is in X and Y only. Scaling uniformly is what an earlier attempt did and
        // it changes nothing at all: a uniform scale about the camera moves every vertex along
        // its own view ray, so every angle — and therefore the whole picture — is identical.
        float fit = Renderer.ViewModelFit(aspect);
        Matrix4x4 local = Matrix4x4.CreateScale(fit, fit, 1f)
                        * Matrix4x4.CreateRotationZ(-steer * 0.9f + sway * 0.35f)
                        * Matrix4x4.CreateRotationX(lift * 0.5f);

        Matrix4x4 view = Matrix4x4.CreateWorld(camera.Position, camera.Forward, camera.Up);
        Matrix4x4 xf = local * view;

        foreach (var section in _cockpitModels.SectionsFor(kind))
        {
            Material mat = Materials.Get(section.Material);
            // Tint the structural panels with the vehicle's colour and leave the lit strips and
            // grips alone, so each chassis reads as its own machine without turning the whole
            // interior into a block of flat colour. Darkened well below the exterior: an interior
            // is a shaded box, and at full brightness under an open sky the dashboard lit up the
            // same value as the sand in front of it and stopped reading as an interior at all.
            bool structural = section.Material == (int)MatId.ArmorPlate
                || section.Material == (int)MatId.RustMetal;
            Vector4 tint = structural
                ? new Vector4(tintColor * 0.32f, 1f)
                : new Vector4(mat.BaseColor.X * 0.55f, mat.BaseColor.Y * 0.55f,
                    mat.BaseColor.Z * 0.55f, mat.BaseColor.W);
            scene.Opaque.Add(new DrawCall
            {
                Mesh = mesh,
                IndexOffset = section.IndexOffset,
                IndexCount = section.IndexCount,
                Material = mat,
                Transform = xf,
                BoneBase = -1,
                Tint = tint,
                Emissive = mat.Emissive,
                Alpha = 1f,
                Center = camera.Position,
                Radius = 2f,
                CastShadow = false,
                RimStrength = 0.30f,
                RimColor = tintColor * 0.6f,
                UvScale = mat.UvScale,
                OwnerView = viewIndex,
                FirstPerson = true,
            });
        }
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
