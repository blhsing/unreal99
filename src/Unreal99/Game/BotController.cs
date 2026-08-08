using System.Numerics;
using Unreal99.Core;
using Unreal99.UI;
using Unreal99.World;

namespace Unreal99.Game;

public enum BotState { Roam, SeekItem, Attack, Retreat, Hunt, Camp }

/// <summary>
/// Bot AI: plans on the level's waypoint graph, steers locally, picks targets and weapons,
/// and aims with a skill-scaled error model. Skill affects reaction time, aim jitter,
/// projectile leading, dodge frequency and situational awareness.
/// </summary>
public sealed class BotController : Controller
{
    public float Skill = 0.6f;              // 0 = novice, 1 = godlike
    public string DisplayName = "";

    // Read-only behavioral-test telemetry. Keeping this on the production controller lets a
    // failed all-map run identify the exact state and route cursor without reflection or a
    // separate AI implementation that could diverge from the game.
    public BotState DiagnosticState => _state;
    public int DiagnosticGoalNode => _goalNode;
    public int DiagnosticPathCursor => _pathCursor;
    public int DiagnosticPathCount => _path.Count;
    public int DiagnosticWaypointNode => _pathCursor < _path.Count ? _path[_pathCursor] : -1;
    public int DiagnosticNextWaypointNode => _pathCursor + 1 < _path.Count ? _path[_pathCursor + 1] : -1;
    public int DiagnosticActiveLiftBrush => _activeLiftBrushIndex;
    public Vector3 DiagnosticLiftSource => _activeLiftSource;
    public Vector3 DiagnosticLiftDestination => _activeLiftDestination;
    public bool DiagnosticLiftCommitted => _activeLiftCommitted;

    private readonly Rng _rng;
    private readonly List<int> _path = new(64);
    private readonly List<int> _navScratch = new(32);
    private readonly List<(PickupEntity Item, int GoalNode, float Score)> _pickupChoices = new(64);
    private readonly Queue<RouteProgressSample> _routeProgressSamples = new();

    private readonly record struct RouteProgressSample(float Time, Vector3 Position);

    private BotState _state = BotState.Roam;
    private int _goalNode = -1;
    private bool _objectiveGoal;
    private int _ctfHoldStep;
    private int _ctfRearmAttempts;
    private bool _hasGoalPosition;
    private bool _pathFound;
    private Vector3 _goalPosition;
    private float _goalRadius = 0.45f;
    private int _pathCursor;
    private float _repathTimer;
    private float _targetTimer;
    private float _weaponTimer;
    private float _goalTimer;
    private float _stuckTimer;
    private float _strafeTimer;
    private float _strafeSign = 1f;
    private float _dodgeTimer;
    private float _reactionTimer;
    private float _fireHoldTimer;
    private float _fireBurstTimer;
    private float _firePauseTimer;
    private float _jumpTimer;
    private Vector3 _lastPosition;
    private Vector3 _aimPoint;
    private Vector3 _aimVelocity;
    private float _aimYaw, _aimPitch;
    private int _targetId = -1;
    private Vector3 _lastKnownTargetPos;
    private float _lastSeenTargetTime = -999f;
    private PickupEntity _itemGoal;
    private PickupEntity _blockedItem;
    private float _blockedItemTimer;
    private float _routeProgressSampleTimer;
    private float _routeRecoveryTimer;
    private int _routeRecoveryGoalNode = -1;
    private int _routeRecoveryReports;
    private float _threatTimer;
    private Vector3 _threatDirection;
    private int _navDebugReports;
    private int _movementDebugReports;
    private int _pickupDebugReports;
    private bool _jumpPadFlight;
    private float _jumpPadFlightTimer;
    private bool _hasSafeGroundPosition;
    private Vector3 _safeGroundPosition;
    private float _edgeRecoveryTimer;
    private Vector3 _edgeRecoveryTarget;
    private int _activeLiftBrushIndex = -1;
    private Vector3 _activeLiftSource;
    private Vector3 _activeLiftDestination;
    private float _activeLiftTimer;
    private bool _activeLiftCommitted;
    private bool _specialTraversalLock;

    /// <summary>Kept so a saved match can rebuild this bot as the same opponent, not a new one.</summary>
    public uint Seed { get; }

    public BotController(uint seed, string name, float skill)
    {
        Seed = seed == 0 ? 1u : seed;
        _rng = new Rng(Seed);
        DisplayName = name;
        Skill = MathX.Clamp(skill, 0f, 1f);
    }

    // Skill-derived tuning.
    private float ReactionTime
    {
        get
        {
            float original = MathX.Lerp(0.62f, 0.09f, Skill);
            return Skill >= 0.85f ? original : original + 0.53f * (1f - Skill / 0.85f);
        }
    }
    private float AimError
    {
        get
        {
            float original = MathX.Lerp(0.115f, 0.007f, Skill * Skill);
            return Skill >= 0.85f ? original : original + 0.045f * (1f - Skill / 0.85f);
        }
    }
    private float AimSpeed => MathX.Lerp(5.5f, 22f, Skill);
    private float SightRange => MathX.Lerp(38f, 110f, Skill);
    private float LeadAccuracy => MathX.Lerp(0.15f, 1.0f, Skill);
    private float DodgeChance => MathX.Lerp(0.10f, 0.85f, Skill);
    private float StrafeAmount => MathX.Lerp(0.35f, 1.0f, Skill);
    /// <summary>Lower tiers cannot match the player's full running speed.</summary>
    public float MovementScale => Skill >= 0.85f ? 1f : MathX.Lerp(0.52f, 0.92f, Skill / 0.85f);
    /// <summary>Outgoing damage handicap. Godlike bots retain the original 100% damage.</summary>
    public float DamageScale => Skill >= 0.85f ? 1f : MathX.Lerp(0.52f, 0.90f, Skill / 0.85f);

    public override void OnSpawned(GameWorld world)
    {
        _state = BotState.Roam;
        _goalNode = -1;
        _objectiveGoal = false;
        _ctfHoldStep = 0;
        _ctfRearmAttempts = 0;
        _hasGoalPosition = false;
        _pathFound = false;
        _path.Clear();
        _targetId = -1;
        _aimYaw = Pawn.Yaw;
        _aimPitch = 0f;
        _lastPosition = Pawn.Position;
        _repathTimer = 0f;
        _itemGoal = null;
        _blockedItem = null;
        _blockedItemTimer = 0f;
        _routeProgressSamples.Clear();
        _routeProgressSampleTimer = 0f;
        _routeRecoveryTimer = 0f;
        _routeRecoveryGoalNode = -1;
        _routeRecoveryReports = 0;
        _reactionTimer = Skill < 0.85f ? ReactionTime * _rng.Range(0.85f, 1.15f) : 0f;
        _fireBurstTimer = 0f;
        _firePauseTimer = 0f;
        _jumpPadFlight = false;
        _jumpPadFlightTimer = 0f;
        _hasSafeGroundPosition = false;
        _edgeRecoveryTimer = 0f;
        _edgeRecoveryTarget = Vector3.Zero;
        _activeLiftBrushIndex = -1;
        _activeLiftSource = Vector3.Zero;
        _activeLiftDestination = Vector3.Zero;
        _activeLiftTimer = 0f;
        _activeLiftCommitted = false;
        _specialTraversalLock = false;
        _movementDebugReports = 0;
        _pickupDebugReports = 0;
    }

    public override void OnDamaged(GameWorld world, Pawn attacker, float amount, Vector3 direction)
    {
        _threatTimer = 2.2f;
        _threatDirection = attacker != null
            ? MathX.SafeNormalize((attacker.Position - Pawn.Position).FlatXZ(), -direction)
            : -MathX.SafeNormalize(direction.FlatXZ(), Pawn.ForwardFlat);

        // Being shot from off-screen is the main reason a bot turns around.
        if (attacker != null && attacker.Alive && _targetId != attacker.Id)
        {
            bool noCurrentTarget = world.FindPawn(_targetId) is not { Alive: true };
            if (noCurrentTarget || _rng.Chance(0.35f + Skill * 0.4f))
            {
                _targetId = attacker.Id;
                _lastKnownTargetPos = attacker.Position;
                _lastSeenTargetTime = world.Time;
                _reactionTimer = ReactionTime * 0.5f;
            }
        }

        // Reflexive dodge when hurt badly.
        if (amount > 22f && _dodgeTimer <= 0f && _rng.Chance(DodgeChance)) _dodgeTimer = 0.01f;
    }

    public override PawnInput Update(GameWorld world, float dt)
    {
        var input = new PawnInput { WeaponSelect = -1, Yaw = Pawn.Yaw, Pitch = Pawn.Pitch };
        var pawn = Pawn;
        if (!pawn.Alive) return input;

        TickTimers(dt);

        // Air control can correct an ordinary edge mistake, but a large explosion may throw a
        // pawn farther than the remaining fall time allows. Once a bot has fallen well below
        // its last floor and there is no real landing beneath it, complete the attempted ledge
        // recovery at the last verified safe point rather than letting it repeat a void death.
        if (RecoverFromFatalFall(world)) return input;

        if (_targetTimer <= 0f)
        {
            _targetTimer = 0.22f;
            SelectTarget(world);
        }

        Pawn target = world.FindPawn(_targetId);
        bool targetVisible = target is { Alive: true } && CanSee(world, target);
        if (targetVisible)
        {
            _lastKnownTargetPos = target.Position;
            _lastSeenTargetTime = world.Time;
        }

        UpdateState(world, target, targetVisible);
        DetectAndRecoverRouteOscillation(world, dt);

        if (_weaponTimer <= 0f)
        {
            _weaponTimer = 0.45f;
            SelectWeapon(world, target, targetVisible, ref input);
        }

        // --- aiming ---
        UpdateAim(world, target, targetVisible, dt);
        input.Yaw = _aimYaw;
        input.Pitch = _aimPitch;

        // --- movement ---
        Vector2 move = ComputeMovement(world, target, targetVisible, dt, ref input);
        input.Move = move;

        // --- firing ---
        if (targetVisible && _reactionTimer <= 0f && target != null
            && !_specialTraversalLock && !_jumpPadFlight)
            DecideFire(world, target, ref input);

        // --- avoid falling into hazards while roaming ---
        AvoidLedges(world, ref input);

        return input;
    }

    private bool RecoverFromFatalFall(GameWorld world)
    {
        if (Pawn.OnGround || _jumpPadFlight) return false;
        if (Pawn.LastGroundPosition.Y - Pawn.Position.Y < 7f) return false;
        if (HasGroundAt(world, Pawn.Position, 12f)) return false;

        Vector3 fallenPosition = Pawn.Position;
        Vector3 anchor = _hasSafeGroundPosition ? _safeGroundPosition : Pawn.LastGroundPosition;
        if (!HasGroundAt(world, anchor, 3f)) anchor = Pawn.LastGroundPosition;
        Pawn.Position = anchor + new Vector3(0f, 0.08f, 0f);
        Pawn.Velocity = Vector3.Zero;
        Pawn.OnGround = false;
        _goalNode = -1;
        _goalTimer = 0f;
        _pathFound = false;
        _path.Clear();
        _activeLiftBrushIndex = -1;
        _activeLiftTimer = 0f;
        _activeLiftCommitted = false;
        _routeProgressSamples.Clear();
        _routeProgressSampleTimer = 0f;
        _edgeRecoveryTimer = 0.35f;
        _edgeRecoveryTarget = anchor;
        if (Environment.GetEnvironmentVariable("UNREAL99_NAV_DEBUG") == "1"
            && Pawn.PlayerIndex >= 0 && _movementDebugReports++ < 48)
            Console.WriteLine($"邊緣救援: 玩家 {Pawn.PlayerIndex + 1} · 從 {fallenPosition} 回到 {anchor}");
        return true;
    }

    private void TickTimers(float dt)
    {
        _targetTimer -= dt;
        _weaponTimer -= dt;
        _repathTimer -= dt;
        _goalTimer -= dt;
        _strafeTimer -= dt;
        _dodgeTimer -= dt;
        _reactionTimer -= dt;
        _fireHoldTimer -= dt;
        if (_fireBurstTimer > 0f)
        {
            _fireBurstTimer -= dt;
            if (_fireBurstTimer <= 0f && Skill < 0.85f)
                _firePauseTimer = _rng.Range(0.35f, 1.35f) * (1f - Skill / 0.95f);
        }
        else _firePauseTimer -= dt;
        _jumpTimer -= dt;
        _threatTimer = MathF.Max(0f, _threatTimer - dt);
        _blockedItemTimer = MathF.Max(0f, _blockedItemTimer - dt);
        if (_blockedItemTimer <= 0f) _blockedItem = null;
        _routeRecoveryTimer = MathF.Max(0f, _routeRecoveryTimer - dt);
        if (_routeRecoveryTimer <= 0f) _routeRecoveryGoalNode = -1;
        _edgeRecoveryTimer = MathF.Max(0f, _edgeRecoveryTimer - dt);
        _activeLiftTimer = MathF.Max(0f, _activeLiftTimer - dt);
        if (_activeLiftTimer <= 0f)
        {
            _activeLiftBrushIndex = -1;
            _activeLiftCommitted = false;
        }
    }

    /// <summary>
    /// Production recovery for the same failure measured by the all-map suite. A bot can cover
    /// plenty of aggregate distance while reversing over one short line, so ordinary zero-speed
    /// stuck detection never fires. Sample route and attack movement for roughly four
    /// seconds and divert to a reachable node when repeated sharp reversals produce almost no
    /// displacement. Authored jump-pad flights remain exempt because ordinary steering is
    /// intentionally suspended until their ballistic arc finishes.
    /// </summary>
    private void DetectAndRecoverRouteOscillation(GameWorld world, float dt)
    {
        bool routeDriven = !_jumpPadFlight && _activeLiftBrushIndex < 0;
        if (!routeDriven)
        {
            _routeProgressSamples.Clear();
            _routeProgressSampleTimer = 0f;
            return;
        }

        _routeProgressSampleTimer += dt;
        if (_routeProgressSampleTimer < 0.20f) return;
        _routeProgressSampleTimer = 0f;
        _routeProgressSamples.Enqueue(new RouteProgressSample(world.Time, Pawn.Position));
        while (_routeProgressSamples.Count > 0 &&
               _routeProgressSamples.Peek().Time < world.Time - 5.2f)
            _routeProgressSamples.Dequeue();
        if (_routeProgressSamples.Count < 8) return;

        RouteProgressSample[] points = _routeProgressSamples.ToArray();
        float duration = points[^1].Time - points[0].Time;
        if (duration < 3.6f) return;

        float path = 0f;
        int reversals = 0;
        Vector3 previousDirection = Vector3.Zero;
        float minX = float.MaxValue, maxX = float.MinValue;
        float minZ = float.MaxValue, maxZ = float.MinValue;
        for (int i = 0; i < points.Length; i++)
        {
            Vector3 position = points[i].Position;
            minX = MathF.Min(minX, position.X); maxX = MathF.Max(maxX, position.X);
            minZ = MathF.Min(minZ, position.Z); maxZ = MathF.Max(maxZ, position.Z);
            if (i == 0) continue;
            Vector3 segment = (position - points[i - 1].Position).FlatXZ();
            float length = segment.Length();
            path += length;
            if (length < 0.22f) continue;
            Vector3 direction = segment / length;
            if (previousDirection != Vector3.Zero && Vector3.Dot(previousDirection, direction) < -0.45f)
                reversals++;
            previousDirection = direction;
        }

        float net = (points[^1].Position - points[0].Position).FlatXZ().Length();
        float extent = new Vector2(maxX - minX, maxZ - minZ).Length();
        if (path < 7f || net > 3.8f || extent > 7f || reversals < 2) return;

        PickupEntity rejectedItem = _itemGoal;
        int recovery = BeginRouteRecovery(world, rejectedItem);

        if (Environment.GetEnvironmentVariable("UNREAL99_NAV_DEBUG") == "1"
            && Pawn.PlayerIndex >= 0 && _routeRecoveryReports++ < 16)
        {
            string firstStep = _navScratch.Count > 0
                ? $"{_navScratch[0]}@{world.Level.Nav.Nodes[_navScratch[0]].Position}"
                : "無";
            string endpoint = recovery >= 0
                ? $"{recovery}@{world.Level.Nav.Nodes[recovery].Position}"
                : "無";
            Console.WriteLine($"振盪重劃: 玩家 {Pawn.PlayerIndex + 1} · 位置 {Pawn.Position} · " +
                $"狀態 {_state} · 路程 {path:0.0} · 位移 {net:0.0} · 反轉 {reversals} · " +
                $"迴避物品 {rejectedItem?.Position.ToString() ?? "無"} · " +
                $"脫困首點 {firstStep} · 脫困終點 {endpoint}");
        }
    }

    /// <summary>
    /// Replaces the current movement intent with a short path that the navigation graph has
    /// already proved reachable. This is shared by the time-based loop watcher and immediate
    /// ledge rejection so clearing an unsafe goal cannot simply select it again next frame.
    /// </summary>
    private int BeginRouteRecovery(GameWorld world, PickupEntity rejectedItem)
    {
        if (rejectedItem != null)
        {
            _blockedItem = rejectedItem;
            _blockedItemTimer = MathF.Max(_blockedItemTimer, 10f);
        }

        int start = world.Level.Nav.FindNearest(Pawn.Position);
        int recovery = -1;
        _navScratch.Clear();
        if (start >= 0 && world.Level.Nav.FindPathToFarthestReachable(start, _navScratch))
        {
            // Use the distant endpoint, not merely another point in the same confined area.
            // The bot can resume its normal role after the short recovery timer, but its path
            // will first include any required lift or teleporter out of the current island.
            recovery = _navScratch[^1];
        }

        _routeRecoveryGoalNode = recovery;
        _routeRecoveryTimer = recovery >= 0 ? 8f : 0f;
        _goalNode = -1;
        _goalTimer = 0f;
        _hasGoalPosition = false;
        _objectiveGoal = false;
        _itemGoal = null;
        _pathFound = false;
        _path.Clear();
        _pathCursor = 0;
        _repathTimer = 0f;
        _routeProgressSamples.Clear();
        return recovery;
    }

    // ---------------------------------------------------------------- perception

    private bool CanSee(GameWorld world, Pawn target)
    {
        if (target == null || !target.Alive) return false;
        Vector3 eye = Pawn.EyePosition;
        Vector3 targetPoint = target.Position + new Vector3(0, target.CurrentHeight * 0.6f, 0);
        float dist = Vector3.Distance(eye, targetPoint);
        float range = SightRange * (target.IsInvisible ? 0.22f : 1f);
        if (dist > range) return false;

        // Wide field of view, but not omniscient — things directly behind are missed.
        Vector3 toTarget = MathX.SafeNormalize(targetPoint - eye, Pawn.ViewDirection);
        if (Vector3.Dot(toTarget, Pawn.ViewDirection) < -0.25f) return false;

        return world.Level.Collision.LineOfSight(eye, targetPoint);
    }

    private void SelectTarget(GameWorld world)
    {
        Pawn best = null;
        float bestScore = float.MinValue;
        foreach (var candidate in world.Pawns)
        {
            if (candidate == Pawn || !candidate.Alive) continue;
            if (world.Mode.TeamBased && candidate.Team == Pawn.Team) continue;
            if (!CanSee(world, candidate)) continue;

            float dist = Vector3.Distance(Pawn.Position, candidate.Position);
            float score = 220f - dist;
            // Prefer wounded enemies, flag carriers and whoever is already the target.
            score += (1f - MathX.Saturate(candidate.Health / 100f)) * 55f;
            if (candidate.HasFlag) score += 140f;
            if (candidate.Id == _targetId) score += 35f;
            if (candidate.HasDamageAmp) score += 40f;
            if (score > bestScore) { bestScore = score; best = candidate; }
        }

        if (best != null)
        {
            if (_targetId != best.Id && Skill < 0.85f)
                _reactionTimer = ReactionTime * _rng.Range(0.85f, 1.20f);
            _targetId = best.Id;
            return;
        }

        // Nothing visible: keep hunting the last known position for a while.
        if (world.Time - _lastSeenTargetTime > 5f) _targetId = -1;
    }

    private void UpdateState(GameWorld world, Pawn target, bool visible)
    {
        float healthFraction = (Pawn.Health + Pawn.Armor * 0.6f) / 160f;

        // A hammer is a last-ditch close-range tool, not a reason to keep charging a distant
        // opponent. Break contact and deliberately re-arm whenever every ranged weapon is dry.
        if (world.Mode.Kind != GameModeKind.Instagib && !HasUsableRangedWeapon(Pawn))
        {
            _state = BotState.SeekItem;
            return;
        }

        if (visible && target != null)
        {
            _state = healthFraction < 0.3f && _rng.Chance(0.55f - Skill * 0.25f)
                ? BotState.Retreat
                : BotState.Attack;
            _reactionTimer = _reactionTimer > 0f ? _reactionTimer : 0f;
            return;
        }

        if (_targetId >= 0 && world.Time - _lastSeenTargetTime < 4.5f)
        {
            _state = BotState.Hunt;
            return;
        }

        // No contact: go shopping. Low health pushes hard toward pickups.
        _state = healthFraction < 0.55f || _rng.Chance(0.6f) ? BotState.SeekItem : BotState.Roam;
    }

    // ---------------------------------------------------------------- weapons

    private void SelectWeapon(GameWorld world, Pawn target, bool visible, ref PawnInput input)
    {
        if (world.Mode.Kind == GameModeKind.Instagib) return;

        float range = target != null ? Vector3.Distance(Pawn.Position, target.Position) : 25f;
        WeaponKind best = Pawn.Weapon;
        float bestScore = float.MinValue;

        for (int i = 0; i < (int)WeaponKind.Count; i++)
        {
            var kind = (WeaponKind)i;
            if (!Pawn.HasWeapon[i]) continue;
            var def = Weapons.Get(kind);
            if (def.Ammo != AmmoKind.None && Pawn.AmmoFor(kind) <= 0) continue;

            float score = def.BotPreference * 10f;
            // Penalise weapons used outside their comfortable range.
            if (range < def.IdealRangeMin) score -= (def.IdealRangeMin - range) * 1.4f;
            if (range > def.IdealRangeMax) score -= (range - def.IdealRangeMax) * 0.55f;
            // Self-splash awareness: don't pick a rocket at point-blank range.
            if (def.Primary.SplashRadius > 0f && range < def.Primary.SplashRadius * 1.25f)
                score -= 14f * (1f - Skill * 0.5f);
            score += _rng.Symmetric(1.5f);
            if (kind == Pawn.Weapon) score += 3f;   // hysteresis stops constant switching

            if (score > bestScore) { bestScore = score; best = kind; }
        }

        if (best != Pawn.Weapon) input.WeaponSelect = (int)best;
        _ = visible;
    }

    private void DecideFire(GameWorld world, Pawn target, ref PawnInput input)
    {
        // Novice through master bots fire in increasingly long bursts with a visible pause
        // between them. Godlike retains the original relentless trigger behavior.
        if (Skill < 0.85f)
        {
            if (_firePauseTimer > 0f) return;
            if (_fireBurstTimer <= 0f)
                _fireBurstTimer = _rng.Range(0.18f, 0.55f) + Skill * 0.55f;
        }

        var def = Pawn.WeaponDef;
        float range = Vector3.Distance(Pawn.Position, target.Position);

        // Do not stand at rifle range swinging the impact hammer into empty space.
        if (def.Primary.Mode == FireMode.Melee && range > def.Primary.Range * 0.92f) return;

        // Don't blow yourself up.
        if (def.Primary.SplashRadius > 0f && range < def.Primary.SplashRadius * 0.85f
            && !_rng.Chance(0.12f)) return;

        // Only shoot when roughly on target; better bots demand tighter alignment.
        Vector3 toTarget = MathX.SafeNormalize(_aimPoint - Pawn.EyePosition, Pawn.ViewDirection);
        float alignment = Vector3.Dot(toTarget, Pawn.ViewDirection);
        float required = MathX.Lerp(0.965f, 0.9975f, Skill);
        if (alignment < required) return;

        // Verify the shot is actually clear so bots stop firing into walls.
        var hit = world.Level.Collision.Raycast(Pawn.EyePosition,
            Pawn.EyePosition + Pawn.ViewDirection * MathF.Min(range + 1f, 200f));
        if (hit.Hit && hit.Distance < range - 1.2f) return;

        bool automatic = def.Primary.Automatic;
        if (automatic)
        {
            input.Fire = true;
            return;
        }

        // Semi-automatic weapons: hold the trigger just long enough to register.
        if (_fireHoldTimer > 0f) { input.Fire = true; return; }
        if (_rng.Chance(0.55f + Skill * 0.4f))
        {
            _fireHoldTimer = 0.09f;
            input.Fire = true;
        }

        // Shock combo: fire an alt ball, then snap-shoot it. Only skilled bots try.
        if (def.Kind == WeaponKind.ShockRifle && Skill > 0.7f && range > 10f && _rng.Chance(0.10f))
        {
            input.Fire = false;
            input.AltFire = true;
        }
    }

    // ---------------------------------------------------------------- aiming

    private void UpdateAim(GameWorld world, Pawn target, bool visible, float dt)
    {
        Vector3 desired;
        // Once every ranged weapon is dry, looking at an enemy no longer serves combat and can
        // hide the bot's actual re-arm intent. Face the pickup route instead so aim and movement
        // agree until a usable weapon has been collected.
        bool rearming = _state == BotState.SeekItem && !HasUsableRangedWeapon(Pawn);

        if (!rearming && target != null && (visible || world.Time - _lastSeenTargetTime < 1.6f))
        {
            Vector3 aimAt = visible
                ? target.Position + new Vector3(0, target.CurrentHeight * 0.62f, 0)
                : _lastKnownTargetPos + new Vector3(0, 1.0f, 0);

            if (visible)
            {
                var def = Pawn.WeaponDef;
                float projectileSpeed = def.Primary.Mode == FireMode.Projectile
                    ? def.Primary.ProjectileSpeed : 0f;
                if (projectileSpeed > 0f)
                {
                    // Lead the target, scaled by skill so weak bots miss moving targets.
                    float dist = Vector3.Distance(Pawn.EyePosition, aimAt);
                    float travel = dist / projectileSpeed;
                    aimAt += target.Velocity * travel * LeadAccuracy;
                    // Compensate for projectile drop on ballistic weapons.
                    if (def.Primary.Projectile is ProjectileKind.Grenade or ProjectileKind.FlakShell
                        or ProjectileKind.BioGlob)
                        aimAt += MathX.Up * (0.5f * Physics.Gravity * world.Level.GravityScale
                            * travel * travel * LeadAccuracy);
                }
            }

            // Aim error: a slow wander plus per-frame jitter, both shrinking with skill.
            float wander = AimError;
            Vector3 error = new(
                MathF.Sin(world.Time * 2.3f + Pawn.Id) * wander,
                MathF.Sin(world.Time * 1.7f + Pawn.Id * 2.1f) * wander * 0.6f,
                MathF.Cos(world.Time * 2.1f + Pawn.Id * 1.3f) * wander);
            aimAt += error * MathF.Max(1f, Vector3.Distance(Pawn.EyePosition, aimAt) * 0.35f);
            desired = aimAt;
            _aimPoint = aimAt;
        }
        else
        {
            // No target: look where we are heading.
            Vector3 ahead = _path.Count > 0 && _pathCursor < _path.Count
                ? world.Level.Nav.Nodes[_path[_pathCursor]].Position
                : _hasGoalPosition
                    ? _goalPosition
                : Pawn.Position + Pawn.ForwardFlat * 6f;
            desired = ahead + new Vector3(0, 1.4f, 0);
            _aimPoint = desired;
        }

        Vector3 dir = MathX.SafeNormalize(desired - Pawn.EyePosition, Pawn.ViewDirection);
        MathX.YawPitchFromDir(dir, out float wantYaw, out float wantPitch);

        float speed = AimSpeed * (visible ? 1f : 0.5f);
        _aimYaw = MathX.WrapAngle(_aimYaw + MathX.WrapAngle(wantYaw - _aimYaw)
            * MathX.Saturate(speed * dt));
        _aimPitch = MathX.Clamp(MathX.Lerp(_aimPitch, wantPitch, MathX.Saturate(speed * dt)), -1.4f, 1.4f);
        _aimVelocity = dir;
    }

    // ---------------------------------------------------------------- movement

    private Vector2 ComputeMovement(GameWorld world, Pawn target, bool visible, float dt, ref PawnInput input)
    {
        var nav = world.Level.Nav;
        _specialTraversalLock = false;
        if (nav.NodeCount == 0) return Vector2.Zero;

        // A jump pad already solved the ballistic trajectory. Air-strafing—especially the
        // aggressive strafing used at maximum skill—changes that velocity enough to miss a roof.
        // Preserve the launch until the bot has landed, then resume normal path planning.
        if (_jumpPadFlight)
        {
            input.Jump = false;
            input.Dodge = Vector2.Zero;
            _jumpPadFlightTimer -= dt;
            if (!Pawn.OnGround && _jumpPadFlightTimer > 0f) return Vector2.Zero;
            _jumpPadFlight = false;
            _jumpPadFlightTimer = 0f;
            _repathTimer = 0f;
        }

        // --- choose a goal ---
        bool itemGoalInvalid = _itemGoal != null
            && (!_itemGoal.Active || _itemGoal.DesireFor(Pawn) <= 0.05f);
        bool preciseGoalReached = _hasGoalPosition
            && (_goalPosition - Pawn.Position).FlatXZ().LengthSquared() <= _goalRadius * _goalRadius;
        // A successful empty path means the selected nav goal is the node beneath our feet.
        // Treat it as complete immediately; waiting for a non-existent waypoint burns the whole
        // goal timeout at zero movement and can repeat indefinitely during close combat.
        bool nodeGoalFinished = !_hasGoalPosition && _pathFound
            && (_path.Count == 0 || _pathCursor >= _path.Count);
        if (_goalTimer <= 0f || _goalNode < 0 || itemGoalInvalid
            || nodeGoalFinished || (_hasGoalPosition && _pathFound && preciseGoalReached))
        {
            _goalTimer = _rng.Range(2.2f, 4.5f);
            ChooseGoal(world, target, visible);
            _repathTimer = 0f;
        }

        // --- path planning ---
        if (_repathTimer <= 0f && _goalNode >= 0)
        {
            _repathTimer = _rng.Range(0.7f, 1.3f);
            int start = nav.FindNearest(Pawn.Position);
            bool found = start >= 0 && (_objectiveGoal
                ? nav.FindPathToward(start, _goalNode, _path)
                : nav.FindPath(start, _goalNode, _path));
            // Random pickups and visible enemies can live on a disconnected navigation island.
            // Do not burn the whole goal timeout at zero input: traverse a distant reachable
            // point, then choose a fresh goal from there. Precise positions must be cleared or
            // the bot would steer back toward the unreachable item after finishing this path.
            if (!found && start >= 0 && nav.FindPathToFarthestReachable(start, _path))
            {
                found = true;
                _goalNode = _path[^1];
                _hasGoalPosition = false;
                _objectiveGoal = false;
                _itemGoal = null;
                _goalTimer = MathF.Min(_goalTimer, 3.5f);
            }
            _pathFound = found;
            if (found) _pathCursor = 0;
            else { _path.Clear(); _pathCursor = 0; }
            if (Environment.GetEnvironmentVariable("UNREAL99_NAV_DEBUG") == "1" &&
                Pawn.PlayerIndex >= 0 && _navDebugReports++ < 16)
            {
                Vector3 startPosition = start >= 0 ? nav.Nodes[start].Position : Pawn.Position;
                Vector3 goalPosition = _goalNode >= 0 ? nav.Nodes[_goalNode].Position : Pawn.Position;
                Vector3 firstPosition = _path.Count > 0 ? nav.Nodes[_path[0]].Position : Pawn.Position;
                Vector3 lastPosition = _path.Count > 0 ? nav.Nodes[_path[^1]].Position : Pawn.Position;
                Console.WriteLine($"電腦導航: 玩家 {Pawn.PlayerIndex + 1} · 起點 {startPosition} · " +
                    $"目標 {goalPosition} · 路徑 {_path.Count} · 首點 {firstPosition} · " +
                    $"末點 {lastPosition} · 角色位置 {Pawn.Position}");
            }
        }

        // --- follow the path ---
        Vector3 steer = Vector3.Zero;
        if (_path.Count > 0 && _pathCursor < _path.Count)
        {
            int waypointIndex = _path[_pathCursor];
            Vector3 node = nav.Nodes[waypointIndex].Position;
            bool waitingForJumpPad = false;
            bool waitingForTeleporter = false;

            // A special nav edge starts at the grid node nearest the pad, which can still be
            // outside the pad's trigger. Do not advance to the far-side node and steer into the
            // gap until the pawn has actually entered the physical launcher.
            if ((nav.Nodes[waypointIndex].Flags & NavFlags.JumpPad) != 0
                && TryNearestJumpPad(world, node, out JumpPad pad))
            {
                float padDistance = (pad.Position - Pawn.Position).FlatXZ().Length();
                // Proximity plus airborne state is not proof that the physical pad fired: a
                // normal jump beside its narrower trigger used to enter permanent flight mode
                // and disable ledge recovery over the launch gap. The pad overwrites velocity,
                // so matching that authored impulse is an unambiguous launch signal.
                bool launched = !Pawn.OnGround && padDistance < 2.2f
                    && Pawn.Position.Y < pad.Position.Y + 3.2f
                    && Vector3.DistanceSquared(Pawn.Velocity, pad.LaunchVelocity) < 2.25f;
                if (launched && _pathCursor + 1 < _path.Count)
                {
                    _jumpPadFlight = true;
                    float horizontalSpeed = MathF.Max(pad.LaunchVelocity.Horizontal(), 0.01f);
                    float horizontalDistance = (pad.Destination - pad.Position).FlatXZ().Length();
                    float gravity = Physics.Gravity * world.Level.GravityScale;
                    float expectedFlight = horizontalDistance > 0.1f
                        ? horizontalDistance / horizontalSpeed
                        : pad.LaunchVelocity.Y * 2f / MathF.Max(gravity, 0.01f);
                    // Preserve the authored ballistic arc, but not forever: a combat impulse can
                    // knock a pawn off-course and a permanent flight state disables all recovery.
                    _jumpPadFlightTimer = MathF.Max(0.8f, expectedFlight + 0.65f);
                    _pathCursor++;
                    return Vector2.Zero;
                }
                else
                {
                    node = pad.Position;
                    waitingForJumpPad = true;
                    _specialTraversalLock = true;
                }
            }

            // Lifts use the same special-link flag as jump pads, but they need the opposite
            // behavior: walk onto the physical platform, wait while it carries the pawn, and
            // consume the link only after reaching the opposite stop.
            if ((nav.Nodes[waypointIndex].Flags & NavFlags.JumpPad) != 0
                && !waitingForJumpPad
                && TryNearestLift(world, node, out Mover lift, out Vector3 liftBottom,
                    out Vector3 liftTop)
                && PathUsesLift(nav, waypointIndex, lift, liftBottom, liftTop))
            {
                // A* normally omits the starting node. A path planned at the lower stop can
                // therefore begin with the upper lift node (and vice versa). Inferring the
                // direction from that first node made every replan reverse the intended ride,
                // producing the rapid back-and-forth seen around Curse and Turbine lifts.
                // Choose from the pawn's nearest stop once, then preserve that direction until
                // this physical lift reaches its destination.
                if (_activeLiftBrushIndex != lift.BrushIndex)
                {
                    bool nearerBottom = Vector3.DistanceSquared(Pawn.Position, liftBottom)
                        <= Vector3.DistanceSquared(Pawn.Position, liftTop);
                    _activeLiftBrushIndex = lift.BrushIndex;
                    _activeLiftSource = nearerBottom ? liftBottom : liftTop;
                    _activeLiftDestination = nearerBottom ? liftTop : liftBottom;
                    _activeLiftTimer = MathF.Max(12f, lift.Period * 1.6f);
                    _activeLiftCommitted = false;
                    _routeProgressSamples.Clear();
                    _routeProgressSampleTimer = 0f;
                }

                Vector3 center = (lift.BaseMin + lift.BaseMax) * 0.5f + lift.CurrentOffset;
                Vector3 currentSurface = center + new Vector3(0f,
                    (lift.BaseMax.Y - lift.BaseMin.Y) * 0.5f + 0.05f, 0f);
                float interiorX = MathF.Max(0.35f,
                    (lift.BaseMax.X - lift.BaseMin.X) * 0.5f - 0.55f);
                float interiorZ = MathF.Max(0.35f,
                    (lift.BaseMax.Z - lift.BaseMin.Z) * 0.5f - 0.55f);
                bool pawnAboard = MathF.Abs(Pawn.Position.X - currentSurface.X) <= interiorX
                    && MathF.Abs(Pawn.Position.Z - currentSurface.Z) <= interiorZ
                    && MathF.Abs(Pawn.Position.Y - currentSurface.Y) < 1.35f;
                bool platformAtDestination = Vector3.DistanceSquared(currentSurface,
                    _activeLiftDestination) < 0.65f * 0.65f;
                Vector3 destinationDelta = Pawn.Position - _activeLiftDestination;
                bool pawnAtDestinationStop = destinationDelta.FlatXZ().LengthSquared()
                        < 4.5f * 4.5f
                    && MathF.Abs(destinationDelta.Y) < 1.6f;
                // A rider may naturally step from the car onto the adjacent authored floor as
                // it reaches a stop. That is successful arrival, not a reason to turn around
                // and chase the centre of the lift again.
                bool arrived = platformAtDestination && (pawnAboard || pawnAtDestinationStop);
                if (arrived)
                {
                    _activeLiftBrushIndex = -1;
                    _activeLiftTimer = 0f;
                    _activeLiftCommitted = false;
                    _pathCursor++;
                    if (_pathCursor < _path.Count)
                    {
                        waypointIndex = _path[_pathCursor];
                        node = nav.Nodes[waypointIndex].Position;
                    }
                }
                else
                {
                    // Do not chase a lift around its shaft. Wait where we are until it reaches
                    // the boarding stop; if already aboard, zero input lets mover carry logic
                    // transport the pawn without walking it off the platform.
                    bool platformParkedAtSource = Vector3.DistanceSquared(currentSurface,
                            _activeLiftSource) < 0.45f * 0.45f
                        && lift.Velocity.LengthSquared() < 0.12f * 0.12f;
                    if (platformParkedAtSource) _activeLiftCommitted = true;
                    // Once a parked platform is available, finish boarding and stay centered
                    // while it moves. Requiring it to remain at the source every frame made the
                    // command disappear as soon as the lift began to rise, so a half-boarded bot
                    // would step backward or fall into the shaft.
                    node = _activeLiftCommitted && !pawnAboard ? currentSurface : Pawn.Position;
                    waitingForJumpPad = true;
                    _specialTraversalLock = true;
                }
            }

            // Nav nodes are a coarse grid and can sit more than a metre from the physical
            // teleporter trigger. Steer to the actual device and retain this path step until
            // the pawn appears at its authored destination; otherwise fast bots can consume
            // the grid node without ever entering the trigger volume.
            if ((nav.Nodes[waypointIndex].Flags & NavFlags.Teleporter) != 0
                && TryNearestTeleporter(world, node, out Teleporter teleporter))
            {
                bool teleported = Vector3.DistanceSquared(Pawn.Position, teleporter.Destination)
                    < 3.5f * 3.5f;
                if (teleported)
                {
                    _pathCursor++;
                    if (_pathCursor < _path.Count)
                    {
                        waypointIndex = _path[_pathCursor];
                        node = nav.Nodes[waypointIndex].Position;
                    }
                }
                else
                {
                    node = teleporter.Position;
                    waitingForTeleporter = true;
                    _specialTraversalLock = true;
                }
            }

            Vector3 flat = (node - Pawn.Position).FlatXZ();
            float dist = flat.Length();
            float heightDelta = node.Y - Pawn.Position.Y;

            if (!waitingForJumpPad && !waitingForTeleporter
                && dist < 1.25f && MathF.Abs(heightDelta) < 2.2f)
            {
                _pathCursor++;
                if (_pathCursor < _path.Count)
                {
                    node = nav.Nodes[_path[_pathCursor]].Position;
                    flat = (node - Pawn.Position).FlatXZ();
                    dist = flat.Length();
                    heightDelta = node.Y - Pawn.Position.Y;
                }
            }
            steer = MathX.SafeNormalize(flat, Vector3.Zero);

            // Jump when the next waypoint is meaningfully above us or the link needs it.
            if (!waitingForJumpPad && !waitingForTeleporter
                && heightDelta > 0.65f && dist < 3.2f
                && Pawn.OnGround && _jumpTimer <= 0f)
            {
                input.Jump = true;
                _jumpTimer = 0.5f;
            }
        }
        else if (_pathFound && _hasGoalPosition)
        {
            // A nav node can be more than a pickup radius away from the actual item or flag.
            // Finish the route against the precise world position instead of abandoning it at
            // the last grid point.
            Vector3 flat = (_goalPosition - Pawn.Position).FlatXZ();
            float dist = flat.Length();
            float heightDelta = _goalPosition.Y - Pawn.Position.Y;
            if (dist > _goalRadius)
            {
                steer = MathX.SafeNormalize(flat, Vector3.Zero);
                if (heightDelta > 0.65f && dist < 2.8f && Pawn.OnGround && _jumpTimer <= 0f)
                {
                    input.Jump = true;
                    _jumpTimer = 0.5f;
                }
            }
            else
            {
                _goalTimer = 0f;
            }
        }

        // --- combat strafing ---
        Vector3 strafe = Vector3.Zero;
        if (visible && target != null && _state == BotState.Attack && !_objectiveGoal
            && !_specialTraversalLock
            && _routeRecoveryTimer <= 0f)
        {
            if (_strafeTimer <= 0f)
            {
                _strafeTimer = _rng.Range(0.55f, 1.5f);
                _strafeSign = _rng.Chance(0.5f) ? 1f : -1f;
            }
            Vector3 toTarget = (target.Position - Pawn.Position).FlatXZ();
            float range = toTarget.Length();
            Vector3 forward = MathX.SafeNormalize(toTarget, Pawn.ForwardFlat);
            Vector3 side = new(-forward.Z, 0, forward.X);

            var def = Pawn.WeaponDef;
            float ideal = MathX.Clamp((def.IdealRangeMin + MathF.Min(def.IdealRangeMax, 45f)) * 0.5f, 5f, 32f);
            float approach = MathX.Clamp((range - ideal) / 12f, -1f, 1f);

            strafe = side * (_strafeSign * StrafeAmount) + forward * approach;
            steer = Vector3.Lerp(steer, MathX.SafeNormalize(strafe, steer), 0.75f);

            // Combat dodging.
            if (_dodgeTimer <= 0f && _rng.Chance(dt * (0.35f + Skill * 1.5f)))
            {
                _dodgeTimer = _rng.Range(0.9f, 2.4f) * (1.4f - Skill);
                input.Dodge = new Vector2(_strafeSign, _rng.Symmetric(0.35f));
            }
        }
        else if (_state == BotState.Retreat && target != null && !_objectiveGoal
            && !_specialTraversalLock
            && _routeRecoveryTimer <= 0f)
        {
            Vector3 away = MathX.SafeNormalize((Pawn.Position - target.Position).FlatXZ(), Pawn.ForwardFlat);
            steer = Vector3.Lerp(steer, away, 0.6f);
        }

        // Reflex dodge queued by OnDamaged.
        if (_dodgeTimer > 0f && _dodgeTimer < 0.05f && Pawn.OnGround)
        {
            input.Dodge = new Vector2(_rng.Chance(0.5f) ? 1f : -1f, 0f);
            _dodgeTimer = _rng.Range(0.7f, 1.6f);
        }

        // --- stuck recovery ---
        float moved = Vector3.Distance(Pawn.Position, _lastPosition);
        _lastPosition = Pawn.Position;
        if (moved < 0.02f && steer != Vector3.Zero)
        {
            _stuckTimer += dt;
            if (_stuckTimer > 0.55f)
            {
                // Jump and veer; if that fails for long enough, replan entirely.
                input.Jump = true;
                steer = Vector3.Transform(steer, Matrix4x4.CreateRotationY(_rng.Symmetric(1.4f)));
                if (_stuckTimer > 1.0f)
                {
                    if (Environment.GetEnvironmentVariable("UNREAL99_NAV_DEBUG") == "1"
                        && Pawn.PlayerIndex >= 0 && _movementDebugReports++ < 48)
                        Console.WriteLine($"停滯重劃: 玩家 {Pawn.PlayerIndex + 1} · 位置 {Pawn.Position} · " +
                            $"目標 {_goalNode} · 路徑游標 {_pathCursor}/{_path.Count}");
                    _stuckTimer = 0f;
                    _goalNode = -1;
                    _goalTimer = 0f;
                    _pathFound = false;
                    _path.Clear();
                }
            }
        }
        else _stuckTimer = MathF.Max(0f, _stuckTimer - dt);

        if (steer == Vector3.Zero) return Vector2.Zero;

        // Convert world steering into local move axes using the yaw that Pawn.Move will apply
        // this frame. Using Pawn.ForwardFlat here refers to the previous frame; when aim turns
        // toward a visible enemy, Pawn.Move updates yaw before interpreting these axes and would
        // rotate the pickup path sideways (often into a ledge).
        Vector3 dir = MathX.SafeNormalize(steer, Pawn.ForwardFlat);
        InputBasis(input.Yaw, out Vector3 inputForward, out Vector3 inputRight);
        float forwardAmount = Vector3.Dot(dir, inputForward);
        float rightAmount = Vector3.Dot(dir, inputRight);
        return new Vector2(rightAmount, forwardAmount) * MovementScale;
    }

    private static void InputBasis(float yaw, out Vector3 forward, out Vector3 right)
    {
        forward = MathX.SafeNormalize(MathX.DirFromYawPitch(yaw, 0f), MathX.Forward);
        right = Vector3.Cross(forward, MathX.Up);
    }

    private static bool TryNearestJumpPad(GameWorld world, Vector3 navPosition, out JumpPad nearest)
    {
        nearest = default;
        float best = 4.5f * 4.5f;
        bool found = false;
        foreach (JumpPad pad in world.Level.JumpPads)
        {
            float distance = Vector3.DistanceSquared(navPosition, pad.Position);
            if (distance >= best) continue;
            best = distance;
            nearest = pad;
            found = true;
        }
        return found;
    }

    private static bool TryNearestTeleporter(GameWorld world, Vector3 navPosition,
        out Teleporter nearest)
    {
        nearest = default;
        float best = 4.5f * 4.5f;
        bool found = false;
        foreach (Teleporter teleporter in world.Level.Teleporters)
        {
            float distance = Vector3.DistanceSquared(navPosition, teleporter.Position);
            if (distance >= best) continue;
            best = distance;
            nearest = teleporter;
            found = true;
        }
        return found;
    }

    private static bool TryNearestLift(GameWorld world, Vector3 navPosition, out Mover nearest,
        out Vector3 bottom, out Vector3 top)
    {
        nearest = null!;
        bottom = top = Vector3.Zero;
        float best = 4.5f * 4.5f;
        foreach (Mover lift in world.Level.Movers)
        {
            if (!lift.Navigable) continue;
            float halfHeight = (lift.BaseMax.Y - lift.BaseMin.Y) * 0.5f;
            Vector3 candidateBottom = (lift.BaseMin + lift.BaseMax) * 0.5f
                + new Vector3(0f, halfHeight + 0.4f, 0f);
            Vector3 candidateTop = candidateBottom + lift.Offset;
            float bottomDistance = Vector3.DistanceSquared(navPosition, candidateBottom);
            float topDistance = Vector3.DistanceSquared(navPosition, candidateTop);
            float distance = MathF.Min(bottomDistance, topDistance);
            if (distance >= best) continue;
            best = distance;
            nearest = lift;
            bottom = candidateBottom;
            top = candidateTop;
        }
        return nearest != null;
    }

    /// <summary>
    /// Both stops carry the special-link flag, but they are also ordinary walkable nodes. Only
    /// board when this route crosses between the two stops, when A* omitted the source and its
    /// first node is the stop opposite the pawn, or when a ride is already underway.
    /// </summary>
    private bool PathUsesLift(NavGraph nav, int waypointIndex, Mover lift, Vector3 bottom,
        Vector3 top)
    {
        bool activeRide = _activeLiftBrushIndex == lift.BrushIndex;
        bool platformDeparted = false;
        if (activeRide)
        {
            Vector3 center = (lift.BaseMin + lift.BaseMax) * 0.5f + lift.CurrentOffset;
            Vector3 surface = center + new Vector3(0f,
                (lift.BaseMax.Y - lift.BaseMin.Y) * 0.5f + 0.05f, 0f);
            platformDeparted = Vector3.DistanceSquared(surface, _activeLiftSource)
                >= 0.65f * 0.65f;
        }

        const float StopRadius = 4.5f;
        float stopRadiusSquared = StopRadius * StopRadius;
        Vector3 waypoint = nav.Nodes[waypointIndex].Position;
        bool waypointBottom = Vector3.DistanceSquared(waypoint, bottom) < stopRadiusSquared;
        bool waypointTop = Vector3.DistanceSquared(waypoint, top) < stopRadiusSquared;
        bool pawnBottom = Vector3.DistanceSquared(Pawn.Position, bottom) < stopRadiusSquared;
        bool pawnTop = Vector3.DistanceSquared(Pawn.Position, top) < stopRadiusSquared;

        // The source node is usually omitted from a freshly planned path.
        bool crossesStops = (pawnBottom && waypointTop) || (pawnTop && waypointBottom);

        if (_pathCursor + 1 < _path.Count)
        {
            Vector3 next = nav.Nodes[_path[_pathCursor + 1]].Position;
            bool nextBottom = Vector3.DistanceSquared(next, bottom) < stopRadiusSquared;
            bool nextTop = Vector3.DistanceSquared(next, top) < stopRadiusSquared;
            crossesStops |= (waypointBottom && nextTop) || (waypointTop && nextBottom);
        }

        if (activeRide && !crossesStops)
        {
            // Once the car has departed, keep a rider committed while it is genuinely between
            // stops. If the pawn is already grounded at either stop (for example after stepping
            // off or missing the car), a new same-floor route may safely cancel the stale ride.
            bool safelyAtStop = Pawn.OnGround && (pawnBottom || pawnTop);
            if (!platformDeparted || safelyAtStop)
            {
                _activeLiftBrushIndex = -1;
                _activeLiftTimer = 0f;
                _activeLiftCommitted = false;
                return false;
            }
            return true;
        }
        return crossesStops;
    }

    private void ChooseGoal(GameWorld world, Pawn target, bool visible)
    {
        var nav = world.Level.Nav;
        _objectiveGoal = false;
        _hasGoalPosition = false;
        _pathFound = false;
        _itemGoal = null;

        // A short verified diversion takes precedence over normal shopping/objective selection
        // after the progress watcher detects a route loop.
        if (_routeRecoveryTimer > 0f && _routeRecoveryGoalNode >= 0
            && _routeRecoveryGoalNode < nav.NodeCount)
        {
            if ((nav.Nodes[_routeRecoveryGoalNode].Position - Pawn.Position).FlatXZ().Length() > 1.4f)
            {
                _goalNode = _routeRecoveryGoalNode;
                _goalTimer = MathF.Min(_goalTimer, _routeRecoveryTimer);
                return;
            }
            _routeRecoveryTimer = 0f;
            _routeRecoveryGoalNode = -1;
        }

        // CTF carriers, recoveries and team roles take priority over ordinary combat.
        if (world.Mode.Kind == GameModeKind.CaptureTheFlag && Pawn.Team != Team.None)
        {
            Team enemy = Pawn.Team == Team.Red ? Team.Blue : Team.Red;
            int ourCarrier = world.FlagCarrier.TryGetValue(Pawn.Team, out int oc) ? oc : -1;
            bool ourFlagHome = ourCarrier < 0
                && world.FlagHome.TryGetValue(Pawn.Team, out Vector3 ourHome)
                && Vector3.Distance(world.FlagPosition[Pawn.Team], ourHome) < 0.4f;

            if (Pawn.HasFlag && world.FlagHome.TryGetValue(Pawn.Team, out Vector3 home))
            {
                // A carrier cannot score while its own flag is away. Once safely back at the
                // base, patrol nearby cover instead of vibrating against the flag stand forever;
                // recovery bots can then return the flag and the carrier immediately runs in.
                float homeDistance = (home - Pawn.Position).FlatXZ().Length();
                if (!ourFlagHome && homeDistance < 2.2f && TryChooseFlagHoldGoal(world, home)) return;
                if (SetPreciseGoal(nav, home, objective: true, radius: 0.45f, refresh: 1.0f)) return;
            }

            // A dropped friendly flag is a short, decisive recovery for every nearby role.
            if (ourCarrier < 0 && !ourFlagHome
                && world.FlagPosition.TryGetValue(Pawn.Team, out Vector3 droppedFlag)
                && SetPreciseGoal(nav, droppedFlag, objective: true, radius: 0.45f, refresh: 0.7f))
                return;

            int enemyCarrier = world.FlagCarrier.TryGetValue(enemy, out int ec) ? ec : -1;
            // Pawn ids alternate between teams, so global id parity assigns an entire team the
            // same role. Number actual bots within their own team to guarantee both recovery and
            // offense roles on each side (the demo-controlled local player is not counted).
            int teamBotSlot = 0;
            foreach (Pawn teammate in world.Pawns)
                if (teammate.IsBot && teammate.Team == Pawn.Team && teammate.Id < Pawn.Id)
                    teamBotSlot++;
            bool defend = Pawn.IsBot && (teamBotSlot & 1) == 0;

            // When our flag is stolen, alternating bots defend while the others maintain
            // offensive pressure. A one-role swarm otherwise never reaches the enemy base.
            if (ourCarrier >= 0 && defend)
            {
                var thief = world.FindPawn(ourCarrier);
                if (thief != null && SetPreciseGoal(nav, thief.Position, objective: true,
                    radius: 2.2f, refresh: 0.55f)) return;
            }

            // Re-arm before a flag run when only the starter pistol remains, or search farther
            // when every ranged weapon is dry. This also makes CTF bots use the map's arsenal.
            bool noRangedAmmo = !HasUsableRangedWeapon(Pawn);
            bool needsUpgrade = noRangedAmmo || !HasUsefulWeaponUpgrade(Pawn);
            if (!needsUpgrade) _ctfRearmAttempts = 0;
            if (ourCarrier < 0 && needsUpgrade && _ctfRearmAttempts < 2
                && TryChoosePickupGoal(world, noRangedAmmo ? 100f : 28f, combatOnly: true))
            {
                _ctfRearmAttempts++;
                return;
            }

            if (enemyCarrier < 0 && world.FlagPosition.TryGetValue(enemy, out Vector3 enemyFlag)
                && SetPreciseGoal(nav, enemyFlag, objective: true, radius: 0.45f, refresh: 1.0f))
                return;

            // Once a teammate has the enemy flag, escort the moving carrier toward home.
            if (enemyCarrier >= 0)
            {
                Pawn carrier = world.FindPawn(enemyCarrier);
                if (carrier != null && carrier.Team == Pawn.Team
                    && SetPreciseGoal(nav, carrier.Position, objective: true, radius: 3.0f,
                        refresh: 0.55f)) return;
            }

            // If no offensive objective was available, reinforce the flag recovery.
            if (ourCarrier >= 0)
            {
                Pawn thief = world.FindPawn(ourCarrier);
                if (thief != null && SetPreciseGoal(nav, thief.Position, objective: true,
                    radius: 2.2f, refresh: 0.55f)) return;
            }
        }

        switch (_state)
        {
            case BotState.Attack when visible && target != null:
                _goalNode = nav.FindNearest(target.Position);
                break;

            case BotState.Hunt:
                _goalNode = nav.FindNearest(_lastKnownTargetPos);
                break;

            case BotState.Retreat:
                {
                    // Head for cover: a low-openness node away from the threat.
                    int best = -1;
                    float bestScore = float.MinValue;
                    _navScratch.Clear();
                    nav.QueryRadius(Pawn.Position, 26f, _navScratch);
                    foreach (int i in _navScratch)
                    {
                        var node = nav.Nodes[i];
                        float score = (1f - node.Openness) * 22f;
                        if (target != null) score += Vector3.Distance(node.Position, target.Position) * 0.6f;
                        if ((node.Flags & NavFlags.NearPickup) != 0) score += 14f;
                        if (score > bestScore) { bestScore = score; best = i; }
                    }
                    _goalNode = best >= 0 ? best : nav.RandomNode(_rng);
                    break;
                }

            case BotState.SeekItem:
                {
                    if (TryChoosePickupGoal(world, HasUsableRangedWeapon(Pawn) ? 65f : 110f,
                        combatOnly: false)) return;
                    _goalNode = nav.RandomNode(_rng, NavFlags.NearPickup);
                    break;
                }

            default:
                _goalNode = nav.RandomNode(_rng, _rng.Chance(0.35f) ? NavFlags.NearPickup : NavFlags.None);
                break;
        }

        if (_goalNode < 0) _goalNode = nav.RandomNode(_rng);
    }

    private bool SetPreciseGoal(NavGraph nav, Vector3 position, bool objective, float radius,
        float refresh)
    {
        int node = nav.FindNearest(position);
        if (node < 0) return false;
        _goalNode = node;
        _goalPosition = position;
        _goalRadius = radius;
        _hasGoalPosition = true;
        _objectiveGoal = objective;
        _goalTimer = MathF.Min(_goalTimer, refresh);
        return true;
    }

    private bool TryChooseFlagHoldGoal(GameWorld world, Vector3 home)
    {
        NavGraph nav = world.Level.Nav;
        _navScratch.Clear();
        nav.QueryRadius(home, 10f, _navScratch);
        if (_navScratch.Count == 0) return false;

        // Move between several points around the stand. Preferring less exposed nodes makes this
        // useful evasive movement rather than a cosmetic circle in the open.
        float angle = (_ctfHoldStep++ * 2.3999632f) + Pawn.Id * 0.73f;
        Vector3 desired = home + new Vector3(MathF.Cos(angle) * 6f, 0f, MathF.Sin(angle) * 6f);
        int current = nav.FindNearest(Pawn.Position);
        int best = -1;
        float bestScore = float.MaxValue;
        foreach (int nodeIndex in _navScratch)
        {
            NavNode node = nav.Nodes[nodeIndex];
            float fromHome = (node.Position - home).FlatXZ().Length();
            if (fromHome is < 3f or > 9f || MathF.Abs(node.Position.Y - home.Y) > 2.2f) continue;
            float score = (node.Position - desired).FlatXZ().Length() + node.Openness * 2.5f;
            if (nodeIndex == current) score += 4f;
            if (score < bestScore) { bestScore = score; best = nodeIndex; }
        }
        if (best < 0) return false;

        return SetPreciseGoal(nav, nav.Nodes[best].Position, objective: true, radius: 0.75f,
            refresh: 2.6f);
    }

    private bool TryChoosePickupGoal(GameWorld world, float maxDistance, bool combatOnly)
    {
        NavGraph nav = world.Level.Nav;
        int start = nav.FindNearest(Pawn.Position);
        if (start < 0) return false;

        _pickupChoices.Clear();
        bool noRangedAmmo = !HasUsableRangedWeapon(Pawn);

        foreach (PickupEntity item in world.Pickups)
        {
            if (!item.Active) continue;
            if (item == _blockedItem && _blockedItemTimer > 0f) continue;
            if (combatOnly && item.Kind is not (PickupKind.WeaponPickup or PickupKind.AmmoPickup))
                continue;

            float desire = item.DesireFor(Pawn);
            if (desire <= 0.05f) continue;
            float distance = Vector3.Distance(Pawn.Position, item.Position);
            if (distance > maxDistance) continue;

            float score = desire * 55f / MathF.Max(distance, 3f);
            if (item.Kind == PickupKind.WeaponPickup)
            {
                if (!Pawn.HasWeapon[(int)item.Weapon]) score += 10f + Weapons.Get(item.Weapon).BotPreference * 4f;
                if (noRangedAmmo) score += 28f;
            }
            else if (item.Kind == PickupKind.AmmoPickup && noRangedAmmo)
            {
                score += 18f;
            }

            int goalNode = nav.FindNearest(item.Position);
            if (goalNode >= 0) _pickupChoices.Add((item, goalNode, score));
        }

        // Desirability alone can select a pickup on another floor or a disconnected roof island.
        // Check candidates in score order and take the first one this pawn can actually reach.
        _pickupChoices.Sort(static (a, b) => b.Score.CompareTo(a.Score));
        foreach (var choice in _pickupChoices)
        {
            _navScratch.Clear();
            if (!nav.FindPath(start, choice.GoalNode, _navScratch)) continue;
            // A fixed 1.4-second refresh made medium and long pickup routes expire while the bot
            // was still travelling. Re-scoring from the new position could then select an item
            // behind it, producing an endless back-and-forth loop. Keep a reachable item goal
            // long enough to traverse its route, with stuck recovery still able to cancel it.
            float distance = Vector3.Distance(Pawn.Position, choice.Item.Position);
            float travelSpeed = Physics.GroundSpeed * MathF.Max(MovementScale, 0.35f);
            float refresh = MathX.Clamp(distance / travelSpeed + 4f, 6f, 14f);
            if (!SetPreciseGoal(nav, choice.Item.Position,
                objective: false, radius: 0.35f, refresh: refresh)) continue;

            _itemGoal = choice.Item;
            _state = BotState.SeekItem;
            if (Environment.GetEnvironmentVariable("UNREAL99_BOT_DEBUG") == "1"
                && Pawn.PlayerIndex >= 0 && _pickupDebugReports++ < 16)
            {
                string itemName = choice.Item.Kind switch
                {
                    PickupKind.WeaponPickup => GameTypes.WeaponName(choice.Item.Weapon),
                    PickupKind.AmmoPickup => $"{choice.Item.Ammo} ammo",
                    _ => choice.Item.Kind.ToString(),
                };
                Console.WriteLine($"電腦補給: 玩家 {Pawn.PlayerIndex + 1} · {itemName} · " +
                    $"物品 {choice.Item.Position} · 節點 {nav.Nodes[choice.GoalNode].Position} · " +
                    $"距離 {distance:0.0} · 期限 {refresh:0.0}s");
            }
            return true;
        }
        if (Environment.GetEnvironmentVariable("UNREAL99_BOT_DEBUG") == "1"
            && Pawn.PlayerIndex >= 0 && _pickupDebugReports++ < 16)
        {
            string candidates = string.Join(" | ", _pickupChoices.Take(8).Select(choice =>
            {
                string name = choice.Item.Kind switch
                {
                    PickupKind.WeaponPickup => GameTypes.WeaponName(choice.Item.Weapon),
                    PickupKind.AmmoPickup => $"{choice.Item.Ammo} ammo",
                    _ => choice.Item.Kind.ToString(),
                };
                return $"{name}@{choice.Item.Position}->#{choice.GoalNode}:{nav.Nodes[choice.GoalNode].Position}";
            }));
            Console.WriteLine($"電腦補給失敗: 玩家 {Pawn.PlayerIndex + 1} · " +
                $"起點 #{start}:{nav.Nodes[start].Position} · 候選 {_pickupChoices.Count} · {candidates}");
        }
        return false;
    }

    private static bool HasUsableRangedWeapon(Pawn pawn)
    {
        for (int i = 0; i < (int)WeaponKind.Count; i++)
        {
            if (!pawn.HasWeapon[i]) continue;
            WeaponKind kind = (WeaponKind)i;
            WeaponDef def = Weapons.Get(kind);
            if (def.Primary.Mode != FireMode.Melee && pawn.AmmoFor(kind) > 0) return true;
        }
        return false;
    }

    private static bool HasUsefulWeaponUpgrade(Pawn pawn)
    {
        for (int i = 0; i < (int)WeaponKind.Count; i++)
        {
            WeaponKind kind = (WeaponKind)i;
            if (kind is WeaponKind.ImpactHammer or WeaponKind.Enforcer || !pawn.HasWeapon[i]) continue;
            if (pawn.AmmoFor(kind) > 0) return true;
        }
        return false;
    }

    /// <summary>
    /// Brakes before unsafe drops and cancels combat dodges that would carry a bot over an edge.
    /// Jump pads provide their own launch velocity, so normal steering must never invent a blind
    /// gap jump merely because it can see floor on the far side.
    /// </summary>
    private void AvoidLedges(GameWorld world, ref PawnInput input)
    {
        // Special traversal is issued only while the physical jump pad, lift, or teleporter is
        // present and ready. Its authored route may intentionally cross a gap that the generic
        // floor probe cannot distinguish from an accidental ledge move. Rechecking it here used
        // to alternate the bot between boarding and braking at lift thresholds.
        if (_specialTraversalLock && Pawn.OnGround)
        {
            input.Jump = false;
            input.Dodge = Vector2.Zero;
            return;
        }

        if (!Pawn.OnGround)
        {
            // Preserve intentional pad ballistics, but use the game's generous air control to
            // recover any ordinary jump, dodge or knockback whose projected path misses all
            // playable ground. Highest-skill combat movement otherwise turns a single dodge at
            // a roof edge into a guaranteed death.
            if (_jumpPadFlight) return;
            Mover activeLift = _activeLiftBrushIndex >= 0
                ? world.Level.Movers.FirstOrDefault(m => m.BrushIndex == _activeLiftBrushIndex)
                : null;
            Vector3 projected = Pawn.Position + Pawn.Velocity.FlatXZ() * 0.75f;
            // A rider knocked clear of a moving platform must correct immediately. Waiting
            // until a downward probe can no longer see the platform leaves too little airtime
            // to get the capsule back over its relatively small footprint.
            if (activeLift != null || !HasGroundAt(world, projected, 14f))
            {
                Vector3 recoveryTarget = _hasSafeGroundPosition
                    ? _safeGroundPosition : Pawn.LastGroundPosition;
                // During a lift ride the last ordinary safe-floor sample can be on the far
                // side of the shaft. If a rocket knocks the rider airborne, steering toward
                // that stale point carries it away from the platform. Recover toward the live
                // platform centre instead, including while the mover descends beneath us.
                if (activeLift != null)
                {
                    Vector3 center = (activeLift.BaseMin + activeLift.BaseMax) * 0.5f
                        + activeLift.CurrentOffset;
                    recoveryTarget = center + new Vector3(0f,
                        (activeLift.BaseMax.Y - activeLift.BaseMin.Y) * 0.5f + 0.05f, 0f);
                }
                Vector3 recovery = (recoveryTarget - Pawn.Position).FlatXZ();
                Vector3 direction = MathX.SafeNormalize(recovery, -Pawn.Velocity.FlatXZ());
                if (direction != Vector3.Zero)
                {
                    float speed = activeLift != null ? Physics.MaxAirSpeed
                        : MathX.Clamp(Pawn.Velocity.Horizontal(), Physics.GroundSpeed,
                            Physics.MaxAirSpeed);
                    Pawn.Velocity = direction * speed + MathX.Up * Pawn.Velocity.Y;
                    input.Move = new Vector2(Vector3.Dot(direction, Pawn.RightFlat),
                        Vector3.Dot(direction, Pawn.ForwardFlat));
                }
                input.Jump = false;
                input.Dodge = Vector2.Zero;
            }
            return;
        }

        // After rejecting an outward move, keep walking inward for a short interval. Merely
        // clearing the goal lets an isolated platform select and reject the same direction on
        // every frame, which looks like rapid shaking despite the bot never falling.
        if (_edgeRecoveryTimer > 0f)
        {
            Vector3 recovery = (_edgeRecoveryTarget - Pawn.Position).FlatXZ();
            if (recovery.LengthSquared() > 0.35f * 0.35f)
            {
                Vector3 direction = MathX.SafeNormalize(recovery, Vector3.Zero);
                InputBasis(input.Yaw, out Vector3 inputForward, out Vector3 inputRight);
                input.Move = new Vector2(Vector3.Dot(direction, inputRight),
                    Vector3.Dot(direction, inputForward)) * MovementScale;
                input.Jump = false;
                input.Dodge = Vector2.Zero;
                return;
            }
            _edgeRecoveryTimer = 0f;
        }

        // Do not begin any voluntary airborne move while standing inside the edge buffer. This
        // also covers a jump queued by stuck recovery or by a higher waypoint, not only dodges.
        bool nearLethalEdge = IsNearLethalEdge(world);
        if (!nearLethalEdge)
        {
            _safeGroundPosition = Pawn.Position;
            _hasSafeGroundPosition = true;
        }
        else
        {
            input.Jump = false;
            input.Dodge = Vector2.Zero;
        }

        // High-skill bots dodge more often. Validate the full dodge direction first so greater
        // combat reflexes do not paradoxically make them more likely to leap into the void.
        if (input.Dodge != Vector2.Zero)
        {
            InputBasis(input.Yaw, out Vector3 inputForward, out Vector3 inputRight);
            Vector3 dodgeDir = inputForward * input.Dodge.Y + inputRight * input.Dodge.X;
            dodgeDir = MathX.SafeNormalize(dodgeDir, Vector3.Zero);
            if (dodgeDir != Vector3.Zero
                && !HasSafePath(world, dodgeDir, 3.0f))
            {
                input.Dodge = Vector2.Zero;
                _dodgeTimer = MathF.Max(_dodgeTimer, 0.35f);
            }
        }

        if (input.Move == Vector2.Zero) return;

        InputBasis(input.Yaw, out Vector3 moveForward, out Vector3 moveRight);
        Vector3 dir = moveForward * input.Move.Y + moveRight * input.Move.X;
        dir = MathX.SafeNormalize(dir, Vector3.Zero);
        if (dir == Vector3.Zero) return;

        // The nav graph already validates continuous floor support (including both sides of the
        // pawn) for each waypoint edge. A fixed-height forward probe falsely rejects long
        // downhill ramps and safe one-way drops because the floor naturally falls more than its
        // maximum-drop window several metres ahead. Defer to the graph only while the requested
        // movement is closely following the current segment; combat strafes and direct steering
        // after a partial path still receive the full ledge check below.
        if (_pathFound && _pathCursor < _path.Count)
        {
            Vector3 waypointDirection = (world.Level.Nav.Nodes[_path[_pathCursor]].Position
                - Pawn.Position).FlatXZ();
            float waypointDistance = waypointDirection.Length();
            if (waypointDistance > 0.35f
                && Vector3.Dot(dir, waypointDirection / waypointDistance) > 0.88f)
                return;
        }

        float stoppingProbe = MathX.Clamp(1.45f + Pawn.Velocity.Horizontal() * 0.16f, 1.45f, 3.6f);
        if (HasSafePath(world, dir, stoppingProbe)) return;

        if (Environment.GetEnvironmentVariable("UNREAL99_NAV_DEBUG") == "1"
            && Pawn.PlayerIndex >= 0 && _movementDebugReports++ < 48)
            Console.WriteLine($"邊緣煞停: 玩家 {Pawn.PlayerIndex + 1} · 位置 {Pawn.Position} · " +
                $"方向 {dir} · 探測 {stoppingProbe:0.00}");

        input.Jump = false;
        input.Dodge = Vector2.Zero;
        input.Move = Vector2.Zero;

        // Remove velocity aimed over the edge immediately. Input reversal alone can take several
        // frames to overcome the momentum of a running or dodging master-level bot.
        Vector3 flatVelocity = Pawn.Velocity.FlatXZ();
        float outwardSpeed = Vector3.Dot(flatVelocity, dir);
        if (outwardSpeed > 0f) Pawn.Velocity -= dir * outwardSpeed;

        Vector3 inward = _hasSafeGroundPosition
            ? (_safeGroundPosition - Pawn.Position).FlatXZ()
            : -dir;
        Vector3 inwardDirection = MathX.SafeNormalize(inward, -dir);
        if (!HasSafePath(world, inwardDirection, 2.5f)) inwardDirection = -dir;
        _edgeRecoveryTarget = Pawn.Position + inwardDirection * 3.5f;
        _edgeRecoveryTimer = 0.9f;

        // Do not merely clear the rejected movement: that permits combat or item selection to
        // issue the same unsafe command on the next frame. Follow a graph-verified route far
        // enough inward to break the loop before normal goal selection resumes.
        BeginRouteRecovery(world, _itemGoal);
    }

    private bool HasSafeGround(GameWorld world, Vector3 direction, float distance)
    {
        // Start well above the pawn's feet so an uphill ramp is not mistaken for empty space.
        Vector3 probe = Pawn.Position + direction * distance;
        return HasGroundAt(world, probe, 4.25f);
    }

    private static bool HasGroundAt(GameWorld world, Vector3 point, float maximumDrop)
    {
        Vector3 probe = point + new Vector3(0f, 2.4f, 0f);
        var hit = world.Level.Collision.Raycast(probe,
            probe - new Vector3(0f, maximumDrop + 2.4f, 0f));
        return hit.Hit && hit.Kind != BrushKind.Lava;
    }

    private bool HasSafePath(GameWorld world, Vector3 direction, float distance)
    {
        // Sampling only the landing point lets a bot accept a narrow void with floor beyond it.
        // Check the whole projected run/dodge instead, at sub-pawn-length intervals.
        for (float d = 0.9f; d < distance; d += 0.8f)
            if (!HasSafeGround(world, direction, d)) return false;
        return HasSafeGround(world, direction, distance);
    }

    private bool IsNearLethalEdge(GameWorld world)
    {
        const float radius = 1.25f;
        for (int i = 0; i < 8; i++)
        {
            float angle = i * (MathX.TwoPi / 8f);
            Vector3 direction = new(MathF.Cos(angle), 0f, MathF.Sin(angle));
            if (!HasSafeGround(world, direction, radius)) return true;
        }
        return false;
    }
}
