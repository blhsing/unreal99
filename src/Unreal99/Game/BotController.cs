using System.Numerics;
using Unreal99.Core;
using Unreal99.UI;
using Unreal99.World;

namespace Unreal99.Game;

public enum BotState { Roam, SeekItem, Attack, Retreat, Hunt, Camp }

/// <summary>
/// Solves the first physically reachable projectile interception. The relative equation includes
/// the target's current velocity and acceleration as well as the projectile's gravity and fixed
/// upward launch boost; its result is therefore the direction to fire, not merely the target's
/// estimated future position.
/// </summary>
internal static class BotAimPrediction
{
    internal readonly record struct Solution(
        Vector3 AimPoint, Vector3 ProjectedTargetPoint, float TravelTime, float MissDistance);

    internal static bool TrySolveIntercept(
        Vector3 origin,
        Vector3 targetPoint,
        Vector3 targetVelocity,
        Vector3 targetAcceleration,
        float projectileSpeed,
        Vector3 projectileLaunchBoost,
        Vector3 projectileAcceleration,
        float maximumTravelTime,
        out Solution solution)
    {
        solution = default;
        if (projectileSpeed <= 0.01f || maximumTravelTime <= 0.01f) return false;

        Vector3 relativePosition = targetPoint - origin;
        if (relativePosition.LengthSquared() <= 1e-6f)
        {
            solution = new Solution(targetPoint, targetPoint, 0f, 0f);
            return true;
        }

        Vector3 relativeVelocity = targetVelocity - projectileLaunchBoost;
        Vector3 relativeAcceleration = targetAcceleration - projectileAcceleration;

        static float Equation(Vector3 p, Vector3 v, Vector3 a, float speed, float time)
        {
            Vector3 required = p + v * time + a * (0.5f * time * time);
            return required.Length() - speed * time;
        }

        // With equal/no acceleration this is the standard exact moving-target quadratic. It is
        // both cheaper and more accurate than scanning, which covers the genuinely ballistic case.
        float interceptTime = -1f;
        if (relativeAcceleration.LengthSquared() <= 1e-6f)
        {
            float a = Vector3.Dot(relativeVelocity, relativeVelocity)
                - projectileSpeed * projectileSpeed;
            float b = 2f * Vector3.Dot(relativePosition, relativeVelocity);
            float c = Vector3.Dot(relativePosition, relativePosition);
            if (MathF.Abs(a) <= 1e-6f)
            {
                if (MathF.Abs(b) > 1e-6f) interceptTime = -c / b;
            }
            else
            {
                float discriminant = b * b - 4f * a * c;
                if (discriminant >= 0f)
                {
                    float root = MathF.Sqrt(discriminant);
                    float t0 = (-b - root) / (2f * a);
                    float t1 = (-b + root) / (2f * a);
                    if (t0 > 0.001f) interceptTime = t0;
                    if (t1 > 0.001f && (interceptTime < 0f || t1 < interceptTime)) interceptTime = t1;
                }
            }
        }
        else
        {
            // Gravity turns the equation quartic after squaring. Find its earliest positive root
            // directly; an early root is the useful low arc and avoids choosing a late lob when
            // both are mathematically possible.
            const int searchSteps = 40;
            float previousTime = 0f;
            float previousValue = relativePosition.Length();
            for (int i = 1; i <= searchSteps; i++)
            {
                float time = maximumTravelTime * i / searchSteps;
                float value = Equation(relativePosition, relativeVelocity, relativeAcceleration,
                    projectileSpeed, time);
                if (value <= 0f && previousValue > 0f)
                {
                    float low = previousTime;
                    float high = time;
                    for (int iteration = 0; iteration < 20; iteration++)
                    {
                        float middle = (low + high) * 0.5f;
                        if (Equation(relativePosition, relativeVelocity, relativeAcceleration,
                            projectileSpeed, middle) > 0f)
                            low = middle;
                        else
                            high = middle;
                    }
                    interceptTime = (low + high) * 0.5f;
                    break;
                }
                previousTime = time;
                previousValue = value;
            }
        }

        if (interceptTime <= 0.001f || interceptTime > maximumTravelTime) return false;

        Vector3 requiredDisplacement = relativePosition + relativeVelocity * interceptTime
            + relativeAcceleration * (0.5f * interceptTime * interceptTime);
        Vector3 aimDirection = MathX.SafeNormalize(requiredDisplacement, MathX.Forward);
        Vector3 projectedTarget = targetPoint + targetVelocity * interceptTime
            + targetAcceleration * (0.5f * interceptTime * interceptTime);
        Vector3 projectedProjectile = origin + aimDirection * projectileSpeed * interceptTime
            + projectileLaunchBoost * interceptTime
            + projectileAcceleration * (0.5f * interceptTime * interceptTime);
        float miss = Vector3.Distance(projectedProjectile, projectedTarget);
        solution = new Solution(origin + requiredDisplacement, projectedTarget, interceptTime, miss);
        return miss <= 0.05f;
    }

    /// <summary>Headless deterministic regression gate, invoked by <c>--aimtest</c>.</summary>
    internal static int RunSelfTest()
    {
        int failures = 0;

        void Check(string name, Vector3 origin, Vector3 target, Vector3 velocity,
            Vector3 targetAcceleration, float speed, Vector3 launchBoost,
            Vector3 projectileAcceleration, float lifetime, bool shouldSolve = true)
        {
            bool solved = TrySolveIntercept(origin, target, velocity, targetAcceleration, speed,
                launchBoost, projectileAcceleration, lifetime, out Solution result);
            bool passed = solved == shouldSolve && (!solved || result.MissDistance <= 0.02f);
            if (!passed) failures++;
            Console.WriteLine($"AIM_CASE {name} {(passed ? "PASS" : "FAIL")} " +
                $"solved={solved} time={(solved ? result.TravelTime : 0f):F4} " +
                $"miss={(solved ? result.MissDistance : 0f):F5}");
        }

        Check("stationary", Vector3.Zero, new Vector3(30f, 1f, 0f), Vector3.Zero,
            Vector3.Zero, 30f, Vector3.Zero, Vector3.Zero, 5f);
        Check("lateral", Vector3.Zero, new Vector3(30f, 1f, 0f), new Vector3(0f, 0f, 7f),
            Vector3.Zero, 30f, Vector3.Zero, Vector3.Zero, 5f);
        Check("airborne-target", new Vector3(0f, 2f, 0f), new Vector3(24f, 8f, 0f),
            new Vector3(2f, 5f, 4f), -MathX.Up * Physics.Gravity, 34f,
            Vector3.Zero, Vector3.Zero, 6f);
        Check("ballistic-bio", new Vector3(0f, 2f, 0f), new Vector3(25f, 2f, 0f),
            new Vector3(0f, 0f, 5f), Vector3.Zero, 26f,
            MathX.Up * ProjectileFactory.VerticalLaunchSpeed(ProjectileKind.BioGlob, 26f),
            -MathX.Up * Physics.Gravity, ProjectileFactory.Lifetime(ProjectileKind.BioGlob));
        Check("unreachable", Vector3.Zero, new Vector3(12f, 0f, 0f), new Vector3(40f, 0f, 0f),
            Vector3.Zero, 20f, Vector3.Zero, Vector3.Zero, 4f, shouldSolve: false);

        Console.WriteLine($"AIM_TEST {(failures == 0 ? "PASS" : "FAIL")} failures={failures}");
        return failures == 0 ? 0 : 1;
    }
}

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
    public bool DiagnosticObjectiveGoal => _objectiveGoal;
    /// <summary>
    /// True while production navigation has already detected a reversal loop and committed to a
    /// recovery route. The traversal harness uses this to judge whether recovery happened before
    /// a bad episode became sustained, rather than counting the detector's own escape turn as a
    /// fresh failure.
    /// </summary>
    public bool DiagnosticRouteRecoveryActive => _routeRecoveryTimer > 0f
        || _edgeRecoveryTimer > 0f || _skirtTimer > 0f;
    public int DiagnosticActiveLiftBrush => _activeLiftBrushIndex;
    public Vector3 DiagnosticLiftSource => _activeLiftSource;
    public Vector3 DiagnosticLiftDestination => _activeLiftDestination;
    public bool DiagnosticLiftCommitted => _activeLiftCommitted;
    public int DiagnosticWeaponPickupGoals { get; private set; }
    public int DiagnosticAmmoPickupGoals { get; private set; }

    private readonly Rng _rng;
    private readonly List<int> _path = new(64);
    private readonly List<int> _navScratch = new(32);
    private readonly List<int> _collisionScratch = new(32);
    private readonly List<int> _waterNodeScratch = new(96);
    private readonly List<(PickupEntity Item, int GoalNode, float Score)> _pickupChoices = new(64);
    private readonly Queue<RouteProgressSample> _routeProgressSamples = new();

    private readonly record struct RouteProgressSample(float Time, Vector3 Position);

    private BotState _state = BotState.Roam;
    private int _goalNode = -1;
    private bool _objectiveGoal;
    private int _ctfHoldStep;
    private int _ctfRearmAttempts;
    private int _dominationPatrolStep;
    /// <summary>Shared by every objective mode: how many times this bot has broken off to re-arm.</summary>
    private int _objectiveRearmAttempts;
    /// <summary>
    /// Assault permits one deliberate supply stop for each newly unlocked objective. Keeping
    /// this separate from the transient "do I need ammo now?" answer prevents a bot from using
    /// some ammunition, deciding it is low again, and shopping for most of a timed attack.
    /// </summary>
    private int _assaultRearmedObjective = -1;
    private int _onslaughtPatrolStep;
    private int _assaultPatrolStep;
    /// <summary>Which vehicle this bot has decided to fetch, so it does not change its mind every tick.</summary>
    private int _vehicleTargetId = -1;
    private float _vehicleBoardTimer;
    /// <summary>How long the bot has been driving without getting anywhere, so it can bail out.</summary>
    private float _vehicleStuckTimer;
    private Vector3 _lastVehiclePosition;
    /// <summary>
    /// Short sliding window used to distinguish useful driving from rapid forward/reverse motion
    /// against the same obstacle. Per-frame speed alone cannot detect that failure because the
    /// odometer still rises while the vehicle remains inside a tiny footprint.
    /// </summary>
    private float _vehicleProgressTimer;
    private float _vehicleProgressPath;
    private Vector3 _vehicleProgressOrigin;
    /// <summary>
    /// Closest the bot has come to its current vehicle destination, and how long it has gone
    /// without beating that. Displacement alone cannot catch a vehicle circling its goal: the
    /// odometer and the net-motion window both look healthy every sample while the bot never
    /// actually arrives. Distance to the destination is the only measure that notices.
    /// </summary>
    private float _vehicleBestDistance = float.MaxValue;
    private float _vehicleNoGainTimer;
    private Vector3 _vehicleBestDestination;
    /// <summary>The same measure for the hoverboard, which has no other way out of a snag.</summary>
    private float _boardBestDistance = float.MaxValue;
    private float _boardNoGainTimer;
    private float _boardBanTimer;
    /// <summary>The same measure for the Warfare orb-fetch role, which is otherwise permanent.</summary>
    private float _orbFetchBestDistance = float.MaxValue;
    private float _orbFetchStallSince;
    private float _orbFetchBanUntil;
    /// <summary>How long this bot has stood still without a reason to. See the anti-park backstop.</summary>
    private float _parkedTimer;
    private Vector3 _parkedLastPosition;
    private int _parkedLastShots;
    private float _vehicleRecoveryTimer;
    private int _vehicleRecoveryAttempts;
    private readonly List<int> _vehiclePath = new(64);
    private int _vehiclePathCursor;
    private float _vehiclePathTimer;
    private Vector3 _vehiclePathDestination;
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
    // Grinding along an obstacle is not the same as being stopped by one; see the waypoint-stall
    // block in ComputeMovement for why raw displacement cannot detect it.
    private float _waypointStallTimer;
    private int _stallWaypointNode = -1;
    private float _stallBestDistance = float.MaxValue;
    private float _skirtTimer;
    private float _skirtSign = 1f;
    private float _strafeTimer;
    private float _strafeSign = 1f;
    private float _dodgeTimer;
    private float _reactionTimer;
    private float _fireHoldTimer;
    private bool _fireHoldAlt;
    private float _fireBurstTimer;
    private float _firePauseTimer;
    private float _jumpTimer;
    private float _translocatorCooldown;
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
    /// <summary>
    /// A destination the route watcher caught this bot looping in front of, and how long it stays
    /// off the menu. Blacklisting only the pickup was not enough: an objective goal — a power
    /// node across a river, say — has no <see cref="PickupEntity"/> to reject, so the recovery
    /// re-routed and then immediately re-selected the same unreachable node and looped again.
    /// </summary>
    private Vector3 _blockedGoalPosition;
    private float _blockedGoalTimer;
    private const float BlockedGoalRadius = 6f;
    private float _routeProgressSampleTimer;
    private float _routeRecoveryTimer;
    private int _routeRecoveryGoalNode = -1;
    // A bot can pace across a corridor wider than the local oscillation detector's footprint and
    // still never close on the flag/node it is meant to reach. Track objective distance separately
    // so useful travel is measured against match progress, not the odometer.
    private Vector3 _objectiveProgressDestination;
    private float _objectiveProgressBestDistance = float.MaxValue;
    private float _objectiveProgressNoGainTimer;
    private Vector3 _lastRouteIntent;
    private float _routeReversalWindow;
    private int _routeReversals;
    private int _routeRecoveryReports;
    private float _threatTimer;
    private Vector3 _threatDirection;

    private string DiagnosticActor => Pawn.PlayerIndex >= 0
        ? $"玩家 {Pawn.PlayerIndex + 1} ({Pawn.Name})"
        : $"電腦 {Pawn.Name}";
    private int _navDebugReports;
    private int _movementDebugReports;
    private int _pickupDebugReports;
    private bool _jumpPadFlight;
    private float _jumpPadFlightTimer;
    // A jump pad is a deliberate one-way route, not a patrol toy. Remember the last physical
    // launcher this pawn used so an optional pickup or random route cannot send it straight back
    // through the same launch/return cycle. Objective routes remain eligible: some arenas really
    // do put their flag, node, or Assault target above a required pad.
    private Vector3 _lastJumpPadPosition;
    private float _jumpPadReuseTimer;
    private float _airbornePeakY;
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
    /// <summary>
    /// Water is navigable, but ordinary objective/combat steering does not know that vertical
    /// movement is mandatory there. Keep a dedicated route to a dry node so a bot that falls
    /// into a pool surfaces and takes an authored exit instead of fighting underwater until its
    /// breath runs out.
    /// </summary>
    private readonly List<int> _waterEscapePath = new(32);
    private int _waterEscapeCursor;
    private float _waterEscapeRepathTimer;
    private bool _waterEscapeActive;
    private float _waterEscapeNoPathTimer;
    private float _waterEscapeOriginY;
    private float _waterEscapeBestWaypointDistance = float.MaxValue;
    private float _waterEscapeNoProgressTimer;
    private int _waterEscapeProgressCursor = -1;
    private float _waterAvoidTimer;
    private Vector3 _lastWaterExitPosition;

    /// <summary>Kept so a saved match can rebuild this bot as the same opponent, not a new one.</summary>
    public uint Seed { get; }

    public BotController(uint seed, string name, float skill)
    {
        Seed = seed == 0 ? 1u : seed;
        _rng = new Rng(Seed);
        DisplayName = name;
        Skill = MathX.Clamp(skill, 0f, 1f);
    }

    /// <summary>
    /// Menu tiers are intentionally compressed below Godlike. The final tier remains exactly 1.0;
    /// tiers 0-4 leave substantially more room for learning and traversal.
    /// </summary>
    public static ReadOnlySpan<float> TierSkillCurve => [0f, 0.035f, 0.09f, 0.18f, 0.36f, 1f];

    public static float SkillForTier(int tier)
    {
        ReadOnlySpan<float> curve = TierSkillCurve;
        return curve[MathX.Clamp(tier, 0, curve.Length - 1)];
    }

    // Skill-derived tuning.
    private float ReactionTime
    {
        get
        {
            float original = MathX.Lerp(0.62f, 0.09f, Skill);
            return Skill >= 0.85f ? original : original + 1.0f * (1f - Skill / 0.85f);
        }
    }
    private float AimError
    {
        get
        {
            float original = MathX.Lerp(0.115f, 0.007f, Skill * Skill);
            return Skill >= 0.85f ? original : original + 0.11f * (1f - Skill / 0.85f);
        }
    }
    private float AimSpeed => Skill >= 0.85f
        ? MathX.Lerp(5.5f, 22f, Skill)
        : MathX.Lerp(2.5f, 8.5f, Skill / 0.85f);
    private float SightRange => Skill >= 0.85f
        ? MathX.Lerp(38f, 110f, Skill)
        : MathX.Lerp(22f, 60f, Skill / 0.85f);
    private float LeadAccuracy => Skill >= 0.85f
        ? MathX.Lerp(0.15f, 1.0f, Skill)
        : MathX.Lerp(0.02f, 0.45f, Skill / 0.85f);
    private float DodgeChance => Skill >= 0.85f
        ? MathX.Lerp(0.10f, 0.85f, Skill)
        : MathX.Lerp(0.01f, 0.32f, Skill / 0.85f);
    private float StrafeAmount => Skill >= 0.85f
        ? MathX.Lerp(0.35f, 1.0f, Skill)
        : MathX.Lerp(0.10f, 0.55f, Skill / 0.85f);
    /// <summary>Lower tiers cannot match the player's full running speed.</summary>
    public float MovementScale => Skill >= 0.85f ? 1f : MathX.Lerp(0.30f, 0.70f, Skill / 0.85f);
    /// <summary>Outgoing damage handicap. Godlike bots retain the original 100% damage.</summary>
    public float DamageScale => Skill >= 0.85f ? 1f : MathX.Lerp(0.22f, 0.65f, Skill / 0.85f);

    public static int RunDifficultySelfTest()
    {
        ReadOnlySpan<float> curve = TierSkillCurve;
        var newbie = new BotController(1, "Newbie", curve[0]);
        var master = new BotController(2, "Master", curve[4]);
        var godlike = new BotController(3, "Godlike", curve[5]);
        // 0-4 are substantially compressed; the exact 1.0 tier and its full movement/damage stay.
        bool pass = curve.Length == 6 && curve[0] == 0f && curve[4] <= 0.36f && curve[5] == 1f
            && newbie.MovementScale <= 0.30f && newbie.DamageScale <= 0.22f
            && master.MovementScale < 0.50f && master.DamageScale < 0.45f
            && godlike.MovementScale == 1f && godlike.DamageScale == 1f;
        Console.WriteLine($"電腦難度大幅縮放且神級不變: {(pass ? "通過" : "失敗")} " +
                          $"新手 移動={newbie.MovementScale:0.00} 傷害={newbie.DamageScale:0.00} · " +
                          $"大師 移動={master.MovementScale:0.00} 傷害={master.DamageScale:0.00} · " +
                          $"神級 移動={godlike.MovementScale:0.00} 傷害={godlike.DamageScale:0.00}");
        return pass ? 0 : 1;
    }

    public override void OnSpawned(GameWorld world)
    {
        _state = BotState.Roam;
        _goalNode = -1;
        _objectiveGoal = false;
        _ctfHoldStep = 0;
        _ctfRearmAttempts = 0;
        _dominationPatrolStep = 0;
        _objectiveRearmAttempts = 0;
        _assaultRearmedObjective = -1;
        _onslaughtPatrolStep = 0;
        _assaultPatrolStep = 0;
        _vehicleTargetId = -1;
        _vehicleBoardTimer = 0f;
        _vehicleStuckTimer = 0f;
        _lastVehiclePosition = Pawn.Position;
        _vehicleProgressTimer = 0f;
        _vehicleProgressPath = 0f;
        _vehicleProgressOrigin = Pawn.Position;
        _vehicleBestDistance = float.MaxValue;
        _vehicleNoGainTimer = 0f;
        _vehicleRecoveryTimer = 0f;
        _vehicleRecoveryAttempts = 0;
        _vehiclePath.Clear();
        _vehiclePathCursor = 0;
        _vehiclePathTimer = 0f;
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
        _blockedGoalTimer = 0f;
        _waypointStallTimer = 0f;
        _stallWaypointNode = -1;
        _stallBestDistance = float.MaxValue;
        _skirtTimer = 0f;
        _routeProgressSamples.Clear();
        _routeProgressSampleTimer = 0f;
        _routeRecoveryTimer = 0f;
        _routeRecoveryGoalNode = -1;
        _objectiveProgressDestination = Pawn.Position;
        _objectiveProgressBestDistance = float.MaxValue;
        _objectiveProgressNoGainTimer = 0f;
        _lastRouteIntent = Vector3.Zero;
        _routeReversalWindow = 0f;
        _routeReversals = 0;
        _routeRecoveryReports = 0;
        _reactionTimer = Skill < 0.85f ? ReactionTime * _rng.Range(0.85f, 1.15f) : 0f;
        _fireBurstTimer = 0f;
        _firePauseTimer = 0f;
        _fireHoldTimer = 0f;
        _fireHoldAlt = false;
        _translocatorCooldown = 0f;
        _jumpPadFlight = false;
        _jumpPadFlightTimer = 0f;
        _lastJumpPadPosition = Vector3.Zero;
        _jumpPadReuseTimer = 0f;
        _airbornePeakY = Pawn.Position.Y;
        _hasSafeGroundPosition = false;
        _edgeRecoveryTimer = 0f;
        _edgeRecoveryTarget = Vector3.Zero;
        _activeLiftBrushIndex = -1;
        _activeLiftSource = Vector3.Zero;
        _activeLiftDestination = Vector3.Zero;
        _activeLiftTimer = 0f;
        _activeLiftCommitted = false;
        _specialTraversalLock = false;
        _waterEscapePath.Clear();
        _waterEscapeCursor = 0;
        _waterEscapeActive = false;
        _waterEscapeNoPathTimer = 0f;
        _waterEscapeOriginY = Pawn.Position.Y;
        _waterEscapeRepathTimer = 0f;
        _waterEscapeBestWaypointDistance = float.MaxValue;
        _waterEscapeNoProgressTimer = 0f;
        _waterEscapeProgressCursor = -1;
        _waterAvoidTimer = 0f;
        _lastWaterExitPosition = Pawn.Position;
        _movementDebugReports = 0;
        _pickupDebugReports = 0;
    }

    public override void OnDamaged(GameWorld world, Pawn attacker, float amount, Vector3 direction)
    {
        _threatTimer = 2.2f;
        _threatDirection = attacker != null
            ? MathX.SafeNormalize((attacker.Position - Pawn.Position).FlatXZ(), -direction)
            : -MathX.SafeNormalize(direction.FlatXZ(), Pawn.ForwardFlat);

        // Being shot from off-screen is the main reason a bot turns around. Self-inflicted splash
        // must not count: taking yourself as the target makes the aim solver point at your own
        // feet, which reads on screen as a bot standing still staring at the floor.
        if (attacker != null && attacker != Pawn && attacker.Alive && _targetId != attacker.Id)
        {
            bool noCurrentTarget = world.FindPawn(_targetId) is not { Alive: true };
            float awarenessChance = Skill >= 0.85f
                ? 0.35f + Skill * 0.4f
                : 0.10f + Skill * 0.18f;
            if (noCurrentTarget || _rng.Chance(awarenessChance))
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

    /// <summary>
    /// Deterministic behavioral-suite hook: remember a real enemy through the same target fields
    /// perception normally fills. The next update still has to pass CanSee, aim and firing gates;
    /// this only removes test-order dependence on which way a hull happened to face after driving.
    /// </summary>
    internal void RememberVisibleEnemyForTest(Pawn enemy, float worldTime)
    {
        if (enemy == null || !enemy.Alive) return;
        _targetId = enemy.Id;
        _lastKnownTargetPos = enemy.Position;
        _lastSeenTargetTime = worldTime;
        _targetTimer = 0.22f;
        _reactionTimer = 0f;
    }

    public override PawnInput Update(GameWorld world, float dt)
    {
        var input = new PawnInput
        {
            WeaponSelect = -1,
            Yaw = Pawn.Yaw,
            Pitch = Pawn.Pitch,
            AvoidJumpPads = true,
        };
        var pawn = Pawn;
        if (!pawn.Alive) return input;

        TickTimers(dt);
        _waterAvoidTimer = MathF.Max(0f, _waterAvoidTimer - dt);

        if (Pawn.OnGround) _airbornePeakY = Pawn.Position.Y;
        else _airbornePeakY = MathF.Max(_airbornePeakY, Pawn.Position.Y);

        // Air control can correct an ordinary edge mistake, but a large explosion may throw a
        // pawn farther than the remaining fall time allows. Once a bot has fallen well below
        // its last floor and there is no real landing beneath it, complete the attempted ledge
        // recovery at the last verified safe point rather than letting it repeat a void death.
        if (RecoverFromFatalFall(world)) return input;

        // Drowning outranks every match objective and every opponent. Swimming uses the same
        // movement keys as walking plus Jump for vertical thrust, so handle it before target
        // selection, weapon choice and firing can pull the bot back toward the pool floor.
        if (TryEscapeWater(world, ref input, dt)) return input;

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

        // Crewing a vehicle replaces on-foot behaviour outright: the nav graph, the dodging and
        // the ledge avoidance all describe a body running around, none of which applies to
        // something with a turning circle.
        _vehicleBoardTimer = MathF.Max(0f, _vehicleBoardTimer - dt);

        // --- anti-park backstop ---
        // A bot that has covered no ground for several seconds is not playing the match, whatever
        // branch chose its goal. Each selector has its own way of sending a bot somewhere it then
        // has nothing to do — a node already held, an orb it cannot reach, a shielded enemy core
        // it cannot shoot — and patching them one at a time only moves the symptom to the next
        // map. Measured against the same 0.20 m/s bar the traversal gate uses, and from position
        // rather than velocity, because a passenger's velocity does not track the hull it rides.
        // Two kinds of standing still are deliberate: holding an Assault objective, which is the
        // objective, and fighting somebody.
        float parkedStep = (pawn.Position - _parkedLastPosition).FlatXZ().Length();
        _parkedLastPosition = pawn.Position;
        // Seeing an enemy is not enough to justify standing still — on an open arena a bot can
        // hold a target in view for the whole round without ever having a shot. Actually shooting
        // is, and it is also exactly what the gate forgives.
        bool firedRecently = pawn.ShotsFired != _parkedLastShots;
        _parkedLastShots = pawn.ShotsFired;
        bool deliberateHold = firedRecently
            || (world.Mode.Kind == GameModeKind.Assault
                && world.Assault.CurrentObjective is { Kind: not ObjectiveKind.Destroy } ring
                && Vector3.Distance(pawn.Position, ring.Position) <= ring.Radius + 1.5f)
            || (world.Mode.Kind == GameModeKind.Warfare
                && world.Warfare.OrbOf(pawn.Team) is { } orb && orb.CarrierId == pawn.Id);
        _parkedTimer = pawn.Alive && !deliberateHold && parkedStep < 0.20f * dt
            ? _parkedTimer + dt
            : 0f;

        if (pawn.InVehicle)
        {
            _vehicleTargetId = -1;
            // Inside a vehicle the only useful move is to get out. The driving controller has
            // already concluded it is where it wants to be, so another tick of it changes nothing.
            if (_parkedTimer > 5f && _vehicleBoardTimer <= 0f)
            {
                _parkedTimer = 0f;
                _vehicleBoardTimer = 1.2f;
                input.UseVehicle = true;
                return input;
            }
            DriveVehicle(world, target, targetVisible, ref input, dt);
            return input;
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
        DetectRapidRouteReversal(world, dt, move);
        DetectAndRecoverObjectiveNoProgress(world, dt, move);

        // --- firing ---
        // A visible opponent is an immediate combat problem at every range. The old objective
        // exception made a bot stare through an enemy and keep shooting a node or generator
        // whenever that enemy was more than 18 metres away. Objective fire remains the fallback
        // as soon as there is no clear opponent.
        bool handledBombingRun = HandleBombingRunCarrierTactics(world, target, targetVisible,
            ref input, dt);
        bool priorityObjectiveShot = HasClearObjectiveShot(world);
        // A timed structure objective does not become optional because a defender crosses the
        // bot's view. If the objective is in weapon range, keep damaging it; defenders are still
        // handled normally while approaching or whenever no clear objective shot is available.
        if (!handledBombingRun && priorityObjectiveShot
            && !_specialTraversalLock && !_jumpPadFlight)
            ShootObjective(world, ref input, dt);
        else if (!handledBombingRun && targetVisible && _reactionTimer <= 0f && target != null
            && !_specialTraversalLock && !_jumpPadFlight)
            DecideFire(world, target, ref input);
        else if (!handledBombingRun && !_specialTraversalLock && !_jumpPadFlight)
            ShootObjective(world, ref input, dt);

        if (!handledBombingRun && !targetVisible)
            TryUseTranslocator(world, ref input);

        // --- avoid falling into hazards while roaming ---
        AvoidLedges(world, ref input);


        // Board the vehicle we walked over here for. Edge-triggered, because holding use down
        // would board and immediately dismount on alternate frames.
        if (_vehicleTargetId >= 0 && _vehicleBoardTimer <= 0f)
        {
            var wanted = world.FindVehicle(_vehicleTargetId);
            if (wanted == null || !wanted.Alive || wanted.FreeSeat() < 0) _vehicleTargetId = -1;
            else if (world.VehicleToBoard(pawn) is { } boardable)
            {
                _vehicleTargetId = boardable.Id;
                input.UseVehicle = true;
                _vehicleBoardTimer = 0.8f;
                _vehicleStuckTimer = 0f;
                _lastVehiclePosition = boardable.Position;
                _vehicleProgressOrigin = boardable.Position;
                _vehicleProgressTimer = 0f;
                _vehicleProgressPath = 0f;
                _vehicleBestDistance = float.MaxValue;
                _vehicleNoGainTimer = 0f;
                _vehicleRecoveryAttempts = 0;
            }
        }

        DecideHoverboard(world, pawn, ref input, dt);
        return input;
    }

    private float _boardToggleTimer;

    /// <summary>
    /// The bot's hoverboard policy: ride it across open ground, step off the moment there is
    /// anything to shoot. Riding costs the ability to fire entirely, so the trade is only worth
    /// making when the alternative is a long walk with nobody in sight.
    /// </summary>
    private void DecideHoverboard(GameWorld world, Pawn pawn, ref PawnInput input, float dt)
    {
        _boardToggleTimer = MathF.Max(0f, _boardToggleTimer - dt);
        if (!world.HoverboardAllowed || pawn.InVehicle) return;

        Pawn enemy = world.FindPawn(_targetId);
        bool threatened = enemy is { Alive: true }
            && Vector3.Distance(enemy.Position, pawn.Position) < 34f;
        float toGoal = _goalNode >= 0 && _goalNode < world.Level.Nav.NodeCount
            ? Vector3.Distance(world.Level.Nav.Nodes[_goalNode].Position, pawn.Position)
            : 0f;
        // Carrying the orb is the one case where the board is always worth it: the carrier cannot
        // shoot anyway, and the whole point of the run is arriving before the defence forms up.
        bool carryingOrb = world.Mode.Kind == GameModeKind.Warfare
            && world.Warfare.OrbOf(pawn.Team) is { } orb && orb.CarrierId == pawn.Id;
        // The board is fast and turns badly, so a rider that clips a kerb can grind against it
        // indefinitely — and a rider cannot shoot, so nothing else interrupts the state. Stow it
        // after a stretch with no ground made towards the goal and finish the trip on foot. The
        // ban afterwards is what makes that stick: without it the bot stows, immediately sees a
        // distant goal again, remounts into the same kerb, and the whole trip becomes a stutter.
        _boardBanTimer = MathF.Max(0f, _boardBanTimer - dt);
        if (pawn.OnHoverboard && toGoal > 0f)
        {
            // Speed is not the signal — a board wedged against a kerb keeps sliding along it at
            // full tilt while the goal stays exactly as far away as it was. Closing distance is
            // the only thing that separates a useful run from a grind.
            if (toGoal < _boardBestDistance - 1.5f)
            {
                _boardBestDistance = toGoal;
                _boardNoGainTimer = 0f;
            }
            else if ((_boardNoGainTimer += dt) > 4f)
            {
                _boardBanTimer = 8f;
                _boardNoGainTimer = 0f;
                _boardBestDistance = float.MaxValue;
            }
        }
        else if (!pawn.OnHoverboard)
        {
            _boardBestDistance = float.MaxValue;
            _boardNoGainTimer = 0f;
        }

        bool want = pawn.CanRideHoverboard && !threatened && _boardBanTimer <= 0f
            && _waterAvoidTimer <= 0f
            && (carryingOrb || toGoal > 45f);

        if (want == pawn.OnHoverboard || _boardToggleTimer > 0f) return;
        input.Hoverboard = true;
        _boardToggleTimer = 1.2f;
    }

    private bool RecoverFromFatalFall(GameWorld world)
    {
        if (Pawn.OnGround || _jumpPadFlight) return false;
        float descentOrigin = MathF.Max(Pawn.LastGroundPosition.Y, _airbornePeakY);
        if (descentOrigin - Pawn.Position.Y < 4f) return false;

        // A real floor below does not automatically make a fall safe. Vertical arenas can put
        // another gallery ten metres beneath an exposed ledge; a bot knocked from the upper
        // route used to accept that floor, land for lethal damage, and repeat the same mistake.
        // Estimate the eventual impact and rescue only a fatal landing. Ordinary shortcuts and
        // survivable tactical drops retain their original risk and movement.
        const float landingProbe = 28f;
        Vector3 probeStart = Pawn.Position + new Vector3(0f, 0.2f, 0f);
        var landing = world.Level.Collision.Raycast(probeStart,
            probeStart - new Vector3(0f, landingProbe, 0f));
        bool hasPlayableLanding = landing.Hit && landing.Kind != BrushKind.Lava
            && landing.Normal.Y >= world.Level.Collision.MaxWalkableY;
        bool fatalLanding = false;
        if (hasPlayableLanding && Pawn.Velocity.Y < 0f)
        {
            float remainingDrop = MathF.Max(0f, Pawn.Position.Y - landing.Point.Y);
            float downwardSpeed = -Pawn.Velocity.Y;
            float impactSpeed = MathF.Sqrt(downwardSpeed * downwardSpeed
                + 2f * Physics.Gravity * world.Level.GravityScale * remainingDrop);
            fatalLanding = Physics.FallDamage(impactSpeed) + 0.5f >= Pawn.Health;
        }
        if (hasPlayableLanding && !fatalLanding) return false;

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
            Console.WriteLine($"邊緣救援: 玩家 {Pawn.PlayerIndex + 1} · 從 {fallenPosition} 回到 {anchor} · " +
                $"原因 {(fatalLanding ? "致命摔落" : "無落腳處")}");
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
                _firePauseTimer = 0.30f
                    + _rng.Range(1.20f, 2.80f) * (1f - Skill / 0.95f);
        }
        else _firePauseTimer -= dt;
        _jumpTimer -= dt;
        _translocatorCooldown = MathF.Max(0f, _translocatorCooldown - dt);
        _jumpPadReuseTimer = MathF.Max(0f, _jumpPadReuseTimer - dt);
        _threatTimer = MathF.Max(0f, _threatTimer - dt);
        _blockedItemTimer = MathF.Max(0f, _blockedItemTimer - dt);
        if (_blockedItemTimer <= 0f) _blockedItem = null;
        _blockedGoalTimer = MathF.Max(0f, _blockedGoalTimer - dt);
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
        if (duration < 2.4f) return;

        float path = 0f;
        int reversals = 0;
        Vector3 previousDirection = Vector3.Zero;
        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;
        float minZ = float.MaxValue, maxZ = float.MinValue;
        for (int i = 0; i < points.Length; i++)
        {
            Vector3 position = points[i].Position;
            minX = MathF.Min(minX, position.X); maxX = MathF.Max(maxX, position.X);
            minY = MathF.Min(minY, position.Y); maxY = MathF.Max(maxY, position.Y);
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
        float horizontalExtent = new Vector2(maxX - minX, maxZ - minZ).Length();
        float verticalExtent = maxY - minY;
        float spatialExtent = MathF.Sqrt(horizontalExtent * horizontalExtent
            + verticalExtent * verticalExtent);
        // Riding a launcher between distinct floors can return to the same X/Z footprint while
        // making real vertical progress. Count confinement in three dimensions so that route is
        // not mistaken for shaking in place.
        bool earlyRoutePacing = _state != BotState.Attack
            && duration >= 2.4f && path >= 4.5f && net <= 1.8f
            && spatialExtent <= 4.8f && reversals >= 2;
        bool sustainedOscillation = _state != BotState.Attack
            && duration >= 3.6f && path >= 7f && net <= 3.8f
            && spatialExtent <= 7f && reversals >= 2;
        // A wider two-point shuttle is still a route failure. Catch it before the five-second
        // harness window qualifies, but only outside Attack so normal combat strafing survives.
        bool wideRoutePacing = _state != BotState.Attack && duration >= 3f && path >= 9f
            && net <= 5.5f && horizontalExtent <= 14f && verticalExtent <= 3f
            && reversals >= 2;
        if (!earlyRoutePacing && !sustainedOscillation && !wideRoutePacing) return;

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
        else if (_hasGoalPosition)
        {
            // No pickup to blame, so the destination itself is what the bot cannot get to from
            // here. Park it briefly; the selectors below fall through to their next choice, which
            // is what breaks the loop rather than just interrupting it for a few seconds.
            _blockedGoalPosition = _goalPosition;
            _blockedGoalTimer = MathF.Max(_blockedGoalTimer, 9f);
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
        _objectiveProgressBestDistance = float.MaxValue;
        _objectiveProgressNoGainTimer = 0f;
        _lastRouteIntent = Vector3.Zero;
        _routeReversalWindow = 0f;
        _routeReversals = 0;
        return recovery;
    }

    /// <summary>
    /// Catches the visually worst form of a navigation loop immediately: route steering flipping
    /// almost 180 degrees several times in a short interval. Distance-window recovery deliberately
    /// waits longer and can therefore allow a conspicuous shake before it has enough samples.
    /// </summary>
    private void DetectRapidRouteReversal(GameWorld world, float dt, Vector2 move)
    {
        bool routeDriven = move != Vector2.Zero && _state != BotState.Attack
            && !_jumpPadFlight && !_specialTraversalLock && _activeLiftBrushIndex < 0
            && _edgeRecoveryTimer <= 0f;
        if (!routeDriven)
        {
            _lastRouteIntent = Vector3.Zero;
            _routeReversalWindow = 0f;
            _routeReversals = 0;
            return;
        }

        InputBasis(_aimYaw, out Vector3 forward, out Vector3 right);
        Vector3 intent = MathX.SafeNormalize(forward * move.Y + right * move.X, Vector3.Zero);
        if (intent == Vector3.Zero) return;

        _routeReversalWindow += dt;
        if (_routeReversalWindow > 1.6f)
        {
            _routeReversalWindow = 0f;
            _routeReversals = 0;
        }
        if (_lastRouteIntent != Vector3.Zero && Vector3.Dot(_lastRouteIntent, intent) < -0.55f)
            _routeReversals++;
        _lastRouteIntent = intent;

        if (_routeReversals < 2) return;

        // Commit to one open side of the current route instead of immediately asking A* for the
        // graph's farthest point. Replanning from the same blocked spot returns the same first
        // edge and can turn the recovery itself into a rapid loop. The ordinary rolling detector
        // remains behind this one and will blacklist/replan if the committed skirt also fails.
        Vector3 lateral = new(-intent.Z, 0f, intent.X);
        Vector3 eye = Pawn.Position + new Vector3(0f, Pawn.CurrentHeight * 0.5f, 0f);
        bool leftClear = !world.Level.Collision.Raycast(eye, eye + lateral * 3.5f).Hit
            && HasSafePath(world, lateral, 2.6f);
        bool rightClear = !world.Level.Collision.Raycast(eye, eye - lateral * 3.5f).Hit
            && HasSafePath(world, -lateral, 2.6f);
        float side = leftClear == rightClear ? (_rng.Chance(0.5f) ? 1f : -1f)
            : (leftClear ? 1f : -1f);
        Vector3 escape = MathX.SafeNormalize(intent * 0.25f + lateral * side, lateral * side);
        _edgeRecoveryTarget = Pawn.Position + escape * 4.5f;
        _edgeRecoveryTimer = 1.25f;
        if (Environment.GetEnvironmentVariable("UNREAL99_NAV_DEBUG") == "1"
            && _movementDebugReports++ < 48)
            Console.WriteLine($"快速反轉繞障: {DiagnosticActor} · 位置 {Pawn.Position} · "
                + $"狀態 {_state} · 目標 {_goalNode} · 路徑游標 {_pathCursor}/{_path.Count}");
        _lastRouteIntent = Vector3.Zero;
        _routeReversalWindow = 0f;
        _routeReversals = 0;
    }

    /// <summary>
    /// Breaks objective-route pacing that still covers too much ground for the confined-footprint
    /// oscillation detector. A route is useful only while it beats its closest distance to the
    /// objective. Special links reset the clock because lifts, teleporters and jump pads can
    /// legitimately move away from their destination before completing the authored transition.
    /// </summary>
    private void DetectAndRecoverObjectiveNoProgress(GameWorld world, float dt, Vector2 move)
    {
        if (!_objectiveGoal || !_hasGoalPosition || !_pathFound || move == Vector2.Zero
            || _jumpPadFlight || _specialTraversalLock || _activeLiftBrushIndex >= 0)
        {
            _objectiveProgressBestDistance = float.MaxValue;
            _objectiveProgressNoGainTimer = 0f;
            return;
        }

        Vector3 destination = _goalPosition;
        if (Vector3.DistanceSquared(destination, _objectiveProgressDestination) > 6f * 6f)
        {
            _objectiveProgressDestination = destination;
            _objectiveProgressBestDistance = float.MaxValue;
            _objectiveProgressNoGainTimer = 0f;
        }

        float distance = (destination - Pawn.Position).FlatXZ().Length();
        if (distance <= _goalRadius + 0.5f || distance < _objectiveProgressBestDistance - 0.75f)
        {
            _objectiveProgressBestDistance = distance;
            _objectiveProgressNoGainTimer = 0f;
            return;
        }

        _objectiveProgressNoGainTimer += dt;
        if (_objectiveProgressNoGainTimer < 3.5f) return;

        if (Environment.GetEnvironmentVariable("UNREAL99_NAV_DEBUG") == "1"
            && _movementDebugReports++ < 48)
            Console.WriteLine($"目標無進展重劃: {DiagnosticActor} · 位置 {Pawn.Position} · "
                + $"目標 {destination} · 最近 {_objectiveProgressBestDistance:0.0} · "
                + $"目前 {distance:0.0} · 路徑游標 {_pathCursor}/{_path.Count}");

        BeginRouteRecovery(world, _itemGoal);
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

        // Wide field of view, but not omniscient — things directly behind are missed. A driver
        // of a hull-mounted weapon sees along the vehicle model's +Z, while pawn yaw zero looks
        // down -Z. Using the ordinary pawn direction here made fixed-gun vehicles blind in the
        // same direction their cannons fired.
        Vector3 viewDirection = Pawn.ViewDirection;
        if (Pawn.InVehicle && world.FindVehicle(Pawn.VehicleId) is { Alive: true } vehicle
            && Pawn.VehicleSeat >= 0 && Pawn.VehicleSeat < vehicle.Def.Seats.Length
            && !vehicle.Def.Seats[Pawn.VehicleSeat].Turret)
            viewDirection = MathX.DirFromYawPitch(vehicle.Yaw + MathX.Pi, Pawn.Pitch);
        Vector3 toTarget = MathX.SafeNormalize(targetPoint - eye, viewDirection);
        if (Vector3.Dot(toTarget, viewDirection) < -0.25f) return false;

        return world.Level.Collision.LineOfSight(eye, targetPoint);
    }

    private void SelectTarget(GameWorld world)
    {
        Pawn best = null;
        Pawn onlyEnemy = null;
        int eligibleEnemies = 0;
        float bestScore = float.MinValue;
        foreach (var candidate in world.Pawns)
        {
            if (candidate == Pawn || !candidate.Alive) continue;
            if (world.Mode.TeamBased && candidate.Team == Pawn.Team) continue;
            onlyEnemy = candidate;
            eligibleEnemies++;
            if (!CanSee(world, candidate)) continue;

            float dist = Vector3.Distance(Pawn.Position, candidate.Position);
            float score = 220f - dist;
            // Prefer wounded enemies, flag carriers and whoever is already the target.
            score += (1f - MathX.Saturate(candidate.Health / 100f)) * 55f;
            if (candidate.HasFlag) score += 140f;
            if (candidate.HasBall) score += 180f;
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

        // In a one-on-one deathmatch, wandering randomly on the opposite navigation island can
        // leave the only opponent absent for most of the opening minute. Give the bot one sampled
        // search position for five seconds. The position is not refreshed through walls, and the
        // bot still cannot aim or fire until ordinary sight and line-of-sight checks succeed.
        if (world.Mode.Kind == GameModeKind.Deathmatch && eligibleEnemies == 1
            && onlyEnemy != null && _targetId < 0)
        {
            _targetId = onlyEnemy.Id;
            _lastKnownTargetPos = onlyEnemy.Position;
            _lastSeenTargetTime = world.Time;
            return;
        }

        // Nothing visible: keep hunting the last known position for a while.
        if (world.Time - _lastSeenTargetTime > 5f) _targetId = -1;
    }

    private void UpdateState(GameWorld world, Pawn target, bool visible)
    {
        float healthFraction = (Pawn.Health + Pawn.Armor * 0.6f) / 160f;

        // Once a reachable pickup route has been selected, finish the short detour even if an
        // enemy enters view. Replacing SeekItem with Attack every frame made combat strafing pull
        // the bot away from weapons it had already decided to collect.
        if (_itemGoal is { Active: true } && _goalTimer > 0f
            && _itemGoal.DesireFor(Pawn) > 0.05f)
        {
            _state = BotState.SeekItem;
            return;
        }

        // A hammer is a last-ditch close-range tool, and one remaining pistol round is not a
        // healthy arsenal. Skilled bots maintain a reserve and acquire a real weapon before
        // committing to another fight instead of waiting until every ranged weapon is dry.
        if (world.Mode.Kind != GameModeKind.Instagib && NeedsCombatResupply(Pawn))
        {
            if (_state != BotState.SeekItem) _goalTimer = 0f;
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

            // Weapon-specific jobs from the original games. These modifiers keep an AVRiL out
            // of an infantry duel, save painters for exposed long-range targets and stop a
            // movement tool from masquerading as a usable gun when the bot is under fire.
            switch (kind)
            {
                case WeaponKind.ShieldGun:
                    if (visible && Pawn.Health < 55f) score += 10f;
                    if (range > 5f) score -= 8f;
                    break;
                case WeaponKind.AssaultRifle:
                    if (range is >= 7f and <= 30f) score += 2f;
                    break;
                case WeaponKind.LinkGun:
                    if (range <= def.Alt.Range) score += 5f;
                    break;
                case WeaponKind.MineLayer:
                    score += OwnedProjectileCount(world, Pawn.Id, ProjectileKind.SpiderMine) < 4
                        ? 3f : -2f;
                    break;
                case WeaponKind.GrenadeLauncher:
                    if (OwnedProjectileNear(world, Pawn.Id, ProjectileKind.StickyGrenade,
                            target?.Position ?? Vector3.Zero, 6.5f)) score += 8f;
                    break;
                case WeaponKind.Avril:
                    score += target is { InVehicle: true } ? 24f : -20f;
                    break;
                case WeaponKind.IonPainter:
                case WeaponKind.TargetPainter:
                {
                    bool exposed = target != null && !world.Level.Collision.Raycast(target.Center,
                        target.Center + MathX.Up * 80f).Hit;
                    score += visible && range >= 24f && exposed ? 13f : -32f;
                    break;
                }
                case WeaponKind.Translocator:
                case WeaponKind.BallLauncher:
                    score -= 100f;
                    break;
                case WeaponKind.Stinger:
                    if (range is >= 8f and <= 38f) score += 3f;
                    break;
            }
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
                _fireBurstTimer = _rng.Range(0.08f, 0.28f) + Skill * 0.25f;
        }

        var def = Pawn.WeaponDef;
        float range = Vector3.Distance(Pawn.Position, target.Position);

        if (def.Kind == WeaponKind.Translocator || def.Kind == WeaponKind.BallLauncher) return;

        // Shield while closing or when badly hurt. It is directional and finite, so the bot only
        // spends charge with the attacker in front rather than leaving it raised indefinitely.
        if (def.Kind == WeaponKind.ShieldGun && range > def.Primary.Range * 0.8f
            && Pawn.ShieldEnergy > 1f && Pawn.Health < 62f)
        {
            input.AltFire = true;
            return;
        }

        bool useAlt = def.Kind switch
        {
            WeaponKind.AssaultRifle => range is >= 7f and <= 32f
                && Pawn.OnGround && _rng.Chance(0.48f + Skill * 0.22f),
            WeaponKind.LinkGun => range <= def.Alt.Range * 0.92f,
            WeaponKind.MineLayer => OwnedProjectileCount(world, Pawn.Id,
                ProjectileKind.SpiderMine) >= 2,
            WeaponKind.GrenadeLauncher => OwnedProjectileNear(world, Pawn.Id,
                ProjectileKind.StickyGrenade, target.Position, 6.5f),
            WeaponKind.Stinger => range is >= 8f and <= 38f && _rng.Chance(0.42f + Skill * 0.3f),
            _ => false,
        };
        FireDef chosen = useAlt ? def.Alt : def.Primary;

        // Remote detonation needs neither sight alignment nor another planted grenade. Pull the
        // clicker as soon as an armed charge is close enough to hurt the target.
        if (chosen.Mode == FireMode.Detonate)
        {
            input.AltFire = true;
            return;
        }

        // Do not stand at rifle range swinging the impact hammer into empty space.
        if (chosen.Mode == FireMode.Melee && range > chosen.Range * 0.92f) return;

        // Don't blow yourself up.
        if (chosen.SplashRadius > 0f && range < chosen.SplashRadius * 0.85f
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

        bool automatic = chosen.Automatic;
        if (automatic)
        {
            if (useAlt) input.AltFire = true;
            else input.Fire = true;
            return;
        }

        // Semi-automatic weapons: hold the trigger just long enough to register.
        if (_fireHoldTimer > 0f)
        {
            if (_fireHoldAlt) input.AltFire = true;
            else input.Fire = true;
            return;
        }
        if (_rng.Chance(0.55f + Skill * 0.4f))
        {
            _fireHoldTimer = 0.09f;
            _fireHoldAlt = useAlt;
            if (useAlt) input.AltFire = true;
            else input.Fire = true;
        }

        // Shock combo: fire an alt ball, then snap-shoot it. Only skilled bots try.
        if (def.Kind == WeaponKind.ShockRifle && Skill > 0.7f && range > 10f && _rng.Chance(0.10f))
        {
            input.Fire = false;
            input.AltFire = true;
        }
    }

    /// <summary>
    /// A carrier cannot fight. Under pressure it passes to a clear team-mate ahead; with an open
    /// medium-range hoop it takes the three-point shot; otherwise it keeps moving for seven.
    /// </summary>
    private bool HandleBombingRunCarrierTactics(GameWorld world, Pawn enemy, bool enemyVisible,
        ref PawnInput input, float dt)
    {
        if (world.Mode.Kind != GameModeKind.BombingRun || !Pawn.HasBall) return false;

        Pawn pass = world.BestBallPassTarget(Pawn);
        bool threatened = enemyVisible && enemy != null
            && Vector3.Distance(Pawn.Position, enemy.Position) < 22f;
        if (pass != null && threatened)
        {
            Pawn.BallPassTargetId = pass.Id;
            AimDirectly(pass.Center, ref input, dt);
            if (Vector3.Dot(Pawn.ViewDirection,
                    MathX.SafeNormalize(pass.Center - Pawn.EyePosition, Pawn.ViewDirection)) > 0.94f)
                input.Fire = true;
            return true;
        }

        Vector3 hoop = world.BombingRun.TargetGoal(Pawn.Team);
        Vector3 toHoop = hoop - Pawn.Center;
        float distance = toHoop.Length();
        bool clear = distance > 0.5f && !world.Level.Collision
            .Raycast(Pawn.Center, hoop).Hit;
        if (clear && distance is >= 8f and <= 25f)
        {
            Pawn.BallPassTargetId = -1;
            const float speed = 34f;
            float gravity = Physics.Gravity * world.Level.GravityScale;
            float horizontal = toHoop.FlatXZ().Length();
            float flight = MathF.Max(0.18f, horizontal / (speed * 0.82f));
            Vector3 launch = toHoop / flight + MathX.Up * (0.5f * gravity * flight);
            AimDirectly(Pawn.EyePosition + MathX.SafeNormalize(launch, Pawn.ViewDirection) * 20f,
                ref input, dt);
            Vector3 desired = MathX.SafeNormalize(launch, Pawn.ViewDirection);
            if (Vector3.Dot(Pawn.ViewDirection, desired) > 0.985f) input.Fire = true;
        }
        return true;
    }

    private void AimDirectly(Vector3 point, ref PawnInput input, float dt)
    {
        Vector3 direction = MathX.SafeNormalize(point - Pawn.EyePosition, Pawn.ViewDirection);
        MathX.YawPitchFromDir(direction, out float yaw, out float pitch);
        input.Yaw = Pawn.Yaw + MathX.WrapAngle(yaw - Pawn.Yaw) * (1f - MathF.Exp(-14f * dt));
        input.Pitch = MathX.Damp(Pawn.Pitch, pitch, 14f, dt);
    }

    /// <summary>
    /// Surfaces and follows the shortest available navigation route to a node whose pawn capsule
    /// is outside water. Maps still need real ramps or stairs out of their pools; this controller
    /// makes bots use those exits and provides a direct nearest-dry fallback for legacy maps.
    /// </summary>
    private bool TryEscapeWater(GameWorld world, ref PawnInput input, float dt)
    {
        if (Pawn.InWater && !_waterEscapeActive)
        {
            _waterEscapeActive = true;
            _waterEscapeOriginY = Pawn.Position.Y;
            _waterEscapeBestWaypointDistance = float.MaxValue;
            _waterEscapeNoProgressTimer = 0f;
            _waterEscapeProgressCursor = -1;
        }
        // Hoverboards are land vehicles. Leaving one deployed after an accidental water entry
        // preserves its high horizontal momentum and can carry the swimmer straight past an
        // exit or back over the bank on the next frame. Stow it before applying swim controls.
        if (Pawn.InWater && Pawn.OnHoverboard) input.Hoverboard = true;
        if (!_waterEscapeActive)
        {
            _waterEscapePath.Clear();
            _waterEscapeCursor = 0;
            _waterEscapeRepathTimer = 0f;
            _waterEscapeNoPathTimer = 0f;
            _waterEscapeBestWaypointDistance = float.MaxValue;
            _waterEscapeNoProgressTimer = 0f;
            _waterEscapeProgressCursor = -1;
            return false;
        }

        // Buoyancy plus Jump can briefly put the capsule above the water volume while the pawn
        // is still in the middle of the pool. Do not hand control back to combat/objective code
        // at the surface: that was the Frigate spin. Escape ends only on grounded dry floor.
        if (!Pawn.InWater && Pawn.OnGround)
        {
            _waterEscapeActive = false;
            // Once this life has proved the pool is a trap, do not let a later item/objective
            // replan undo the escape. Every affected arena provides a dry alternative; death and
            // OnSpawned reset the controller if a later life begins elsewhere.
            _waterAvoidTimer = float.PositiveInfinity;
            _lastWaterExitPosition = Pawn.Position;
            _waterEscapePath.Clear();
            _waterEscapeCursor = 0;
            _waterEscapeRepathTimer = 0f;
            _waterEscapeNoPathTimer = 0f;
            _waterEscapeBestWaypointDistance = float.MaxValue;
            _waterEscapeNoProgressTimer = 0f;
            _waterEscapeProgressCursor = -1;
            return false;
        }

        NavGraph nav = world.Level.Nav;
        _waterEscapeRepathTimer -= dt;
        // Commit to the selected exit. Rebuilding the breadth-first route every 0.8 seconds
        // allowed two similarly near ramps to alternate as the pawn crossed the pool, visibly
        // sending swimmers back and forth. Replan only when no route exists or genuine lack of
        // waypoint progress proves the committed route is blocked.
        bool needsPath = _waterEscapePath.Count == 0
            || _waterEscapeCursor >= _waterEscapePath.Count;
        if (needsPath && _waterEscapeRepathTimer <= 0f)
        {
            _waterEscapeRepathTimer = 0.8f;
            _waterEscapePath.Clear();
            _waterEscapeCursor = 0;
            _waterEscapeBestWaypointDistance = float.MaxValue;
            _waterEscapeNoProgressTimer = 0f;
            _waterEscapeProgressCursor = -1;
            // The generic nearest node can be on a dry deck directly above the swimmer. Olden's
            // central island is only 2.4 m above the basin, so that snap seeded a perfectly valid
            // dry-land path whose first segment ran through the island wall. Seed from the actual
            // water floor/ramp component instead.
            _waterNodeScratch.Clear();
            nav.QueryRadius(Pawn.Position, 18f, _waterNodeScratch);
            int start = -1;
            float nearest = float.MaxValue;
            Vector3 waterHalf = new(Physics.PawnRadius, Physics.PawnHeight * 0.5f,
                Physics.PawnRadius);
            foreach (int nodeIndex in _waterNodeScratch)
            {
                Vector3 feet = nav.Nodes[nodeIndex].Position;
                Vector3 center = feet + MathX.Up * waterHalf.Y;
                BrushKind volume = world.Level.Collision.VolumeAt(center - waterHalf,
                    center + waterHalf, _collisionScratch);
                if (volume != BrushKind.Water) continue;
                float distance = Vector3.DistanceSquared(feet, Pawn.Position)
                    + MathF.Abs(feet.Y - Pawn.Position.Y) * 6f;
                if (distance < nearest) { nearest = distance; start = nodeIndex; }
            }
            if (start >= 0)
            {
                bool DryNode(int nodeIndex)
                {
                    Vector3 feet = nav.Nodes[nodeIndex].Position;
                    // A node just outside an axis-aligned water volume can sit on the same basin
                    // floor and report "not water" merely because its capsule crosses the volume
                    // boundary. That is a harbour wall, not an exit. Require meaningful ascent;
                    // real ramps will contribute progressively higher reachable nodes.
                    // Compare with the basin height at entry, not the pawn's current height. On
                    // the last metre of a slope the old moving threshold rejected the same dry
                    // destination it had been following, cleared the path, and stopped swimming.
                    if (feet.Y < _waterEscapeOriginY + 0.75f) return false;
                    Vector3 center = feet + MathX.Up * waterHalf.Y;
                    return world.Level.Collision.VolumeAt(center - waterHalf, center + waterHalf,
                        _collisionScratch) != BrushKind.Water;
                }
                if (nav.FindPathToNearestReachable(start, _waterEscapePath, DryNode))
                    _waterEscapeNoPathTimer = 0f;
            }
        }

        const float reach = 0.9f;
        while (_waterEscapeCursor < _waterEscapePath.Count
            && (nav.Nodes[_waterEscapePath[_waterEscapeCursor]].Position - Pawn.Position)
                .FlatXZ().LengthSquared() <= reach * reach)
        {
            _waterEscapeCursor++;
            _waterEscapeBestWaypointDistance = float.MaxValue;
            _waterEscapeNoProgressTimer = 0f;
            _waterEscapeProgressCursor = _waterEscapeCursor;
        }

        bool hasPath = _waterEscapeCursor < _waterEscapePath.Count;
        Vector3 target = hasPath
            ? nav.Nodes[_waterEscapePath[_waterEscapeCursor]].Position
            : Pawn.Position;
        Vector3 flat = (target - Pawn.Position).FlatXZ();
        if (hasPath)
        {
            float waypointDistance = flat.Length();
            if (_waterEscapeProgressCursor != _waterEscapeCursor
                || waypointDistance < _waterEscapeBestWaypointDistance - 0.18f)
            {
                _waterEscapeProgressCursor = _waterEscapeCursor;
                _waterEscapeBestWaypointDistance = waypointDistance;
                _waterEscapeNoProgressTimer = 0f;
            }
            else _waterEscapeNoProgressTimer += dt;

            if (_waterEscapeNoProgressTimer > 2.4f)
            {
                _waterEscapePath.Clear();
                _waterEscapeCursor = 0;
                _waterEscapeRepathTimer = 0f;
                _waterEscapeBestWaypointDistance = float.MaxValue;
                _waterEscapeNoProgressTimer = 0f;
                _waterEscapeProgressCursor = -1;
                input.Move = Vector2.Zero;
                input.Jump = true;
                input.Fire = false;
                input.AltFire = false;
                return true;
            }
        }
        if (flat.LengthSquared() > 0.04f)
        {
            MathX.YawPitchFromDir(MathX.SafeNormalize(flat, Pawn.ForwardFlat),
                out float yaw, out _);
            input.Yaw = Pawn.Yaw + MathX.WrapAngle(yaw - Pawn.Yaw)
                * (1f - MathF.Exp(-10f * dt));
            input.Move = new Vector2(0f, 1f);
        }
        input.Pitch = MathX.Damp(Pawn.Pitch, 0f, 10f, dt);
        // Jump is swim-up here. It is useful while following a verified route, but holding it
        // after pathfinding failed produces the conspicuous wall-jumping loop from Frigate.
        input.Jump = hasPath;
        if (!hasPath)
        {
            _waterEscapeNoPathTimer += dt;
            input.Move = Vector2.Zero;
        }
        input.Fire = false;
        input.AltFire = false;
        return true;
    }

    private static int OwnedProjectileCount(GameWorld world, int ownerId, ProjectileKind kind)
    {
        int count = 0;
        foreach (Projectile projectile in world.Projectiles)
            if (projectile.Active && projectile.OwnerId == ownerId && projectile.Kind == kind) count++;
        return count;
    }

    private static bool OwnedProjectileNear(GameWorld world, int ownerId, ProjectileKind kind,
        Vector3 point, float radius)
    {
        float radiusSq = radius * radius;
        foreach (Projectile projectile in world.Projectiles)
            if (projectile.Active && projectile.OwnerId == ownerId && projectile.Kind == kind
                && Vector3.DistanceSquared(projectile.Position, point) <= radiusSq) return true;
        return false;
    }

    /// <summary>
    /// Uses the Translocator as a traversal tool, never as a zero-damage combat weapon. The disc
    /// is recalled only after landing near a real navigation node with room for the pawn capsule,
    /// which avoids teleporting into a wall or over an unplayable ledge.
    /// </summary>
    private bool TryUseTranslocator(GameWorld world, ref PawnInput input)
    {
        if (_translocatorCooldown > 0f || Pawn.HasFlag || Pawn.HasBall || !Pawn.OnGround
            || !Pawn.HasWeapon[(int)WeaponKind.Translocator] || !_hasGoalPosition) return false;

        ref readonly Projectile disc = ref FindOwnedTranslocator(world, Pawn.Id, out bool found);
        if (found)
        {
            if (!disc.Stuck && disc.Velocity.LengthSquared() > 1.5f) return false;
            if (Vector3.Distance(Pawn.Position, disc.Position) < 5f) return false;
            int node = world.Level.Nav.FindNearest(disc.Position);
            if (node < 0 || Vector3.Distance(world.Level.Nav.Nodes[node].Position, disc.Position) > 4f)
                return false;
            Vector3 half = new(Physics.PawnRadius, Physics.PawnHeight * 0.5f, Physics.PawnRadius);
            Vector3 center = disc.Position + MathX.Up * half.Y;
            if (world.Level.Collision.BoxOverlapsSolid(center - half, center + half)) return false;

            if (Pawn.Weapon != WeaponKind.Translocator)
                input.WeaponSelect = (int)WeaponKind.Translocator;
            else
            {
                input.AltFire = true;
                _translocatorCooldown = 4.5f;
            }
            return true;
        }

        float distance = Vector3.Distance(Pawn.Position, _goalPosition);
        if (distance < 22f || distance > 70f || _specialTraversalLock) return false;
        if (Pawn.Weapon != WeaponKind.Translocator)
            input.WeaponSelect = (int)WeaponKind.Translocator;
        else
        {
            // Aim the disc along the active route, rather than at the last enemy the general
            // aiming pass happened to remember. A modest lift clears ordinary kerbs while the
            // landing/nav/capsule checks above still decide whether recall is safe.
            Vector3 routeAim = _goalPosition + MathX.Up * MathX.Clamp(distance * 0.10f, 1.5f, 5f);
            Vector3 routeDirection = MathX.SafeNormalize(routeAim - Pawn.EyePosition,
                Pawn.ViewDirection);
            MathX.YawPitchFromDir(routeDirection, out input.Yaw, out input.Pitch);
            input.Fire = true;
            _translocatorCooldown = 0.8f;
        }
        return true;
    }

    private static ref readonly Projectile FindOwnedTranslocator(GameWorld world, int ownerId,
        out bool found)
    {
        for (int i = 0; i < world.Projectiles.Length; i++)
        {
            if (world.Projectiles[i].Active
                && world.Projectiles[i].Kind == ProjectileKind.TranslocatorDisc
                && world.Projectiles[i].OwnerId == ownerId)
            {
                found = true;
                return ref world.Projectiles[i];
            }
        }
        found = false;
        return ref world.Projectiles[0];
    }

    // ---------------------------------------------------------------- aiming

    private void UpdateAim(GameWorld world, Pawn target, bool visible, float dt)
    {
        Vector3 desired;
        bool objectiveAim = TryGetClearAssaultObjectiveAim(world, out Vector3 assaultAim,
            out _, rejectUnsafeSplash: true);
        // Once every ranged weapon is dry, looking at an enemy no longer serves combat and can
        // hide the bot's actual re-arm intent. Face the pickup route instead so aim and movement
        // agree until a usable weapon has been collected.
        bool rearming = _state == BotState.SeekItem && !HasUsableRangedWeapon(Pawn);
        // A flag/control-point route can legitimately put an enemy several floors below the
        // bot. Tracking that pawn through an atrium made demo players stare almost vertically
        // into the floor while their real objective was elsewhere. Keep route awareness unless
        // the target is on a tactically relevant level; ordinary combat remains unrestricted.
        bool targetOffObjectiveLevel = _objectiveGoal && target != null
            && MathF.Abs((target.Position.Y + target.CurrentHeight * 0.5f)
                - (Pawn.Position.Y + Pawn.CurrentHeight * 0.5f)) > 5f;
        bool useTargetAim = !objectiveAim && !rearming && !targetOffObjectiveLevel && target != null
            && (visible || world.Time - _lastSeenTargetTime < 1.6f);

        if (objectiveAim)
        {
            // A destroy objective in reach is the thing the attacker must shoot. Tracking a
            // defender on the floor above made the Glacier bot look into—and fire into—the low
            // station ceiling while standing two metres from the gate panel.
            desired = assaultAim;
            _aimPoint = desired;
        }
        else if (useTargetAim)
        {
            Vector3 aimAt = visible
                ? target.Position + new Vector3(0, target.CurrentHeight * 0.62f, 0)
                : _lastKnownTargetPos + new Vector3(0, 1.0f, 0);

            if (visible)
            {
                var def = Pawn.WeaponDef;
                // Skilled marksmen exploit the Lightning/Sniper/Assault headshot behavior rather
                // than always aiming at the same chest point used by ordinary weapons.
                if (def.Primary.HeadshotMultiplier > 1f && Skill >= 0.62f)
                    aimAt = target.Position + new Vector3(0,
                        target.CurrentHeight * MathX.Lerp(0.70f, 0.91f, Skill), 0);
                float projectileSpeed = def.Primary.Mode == FireMode.Projectile
                    ? def.Primary.ProjectileSpeed : 0f;
                if (projectileSpeed > 0f)
                {
                    ProjectileKind projectile = def.Primary.Projectile;
                    float gravity = Physics.Gravity * world.Level.GravityScale;
                    Vector3 targetAcceleration = !target.OnGround && !target.InWater
                        ? -MathX.Up * gravity
                        : Vector3.Zero;
                    Vector3 projectileAcceleration = ProjectileFactory.AffectedByGravity(projectile)
                        ? -MathX.Up * gravity
                        : Vector3.Zero;
                    Vector3 launchBoost = MathX.Up
                        * ProjectileFactory.VerticalLaunchSpeed(projectile, projectileSpeed);
                    if (BotAimPrediction.TrySolveIntercept(Pawn.EyePosition, aimAt,
                        target.Velocity, targetAcceleration, projectileSpeed, launchBoost,
                        projectileAcceleration, ProjectileFactory.Lifetime(projectile),
                        out BotAimPrediction.Solution prediction))
                    {
                        // Lower skill tiers deliberately apply only part of the physically exact
                        // lead. Godlike uses the complete speed/direction/gravity projection.
                        Vector3 predicted = Vector3.Lerp(aimAt, prediction.AimPoint, LeadAccuracy);
                        // The current target point is visible, but its projected future point may
                        // be behind a pillar or above a low ceiling. Leading into solid geometry
                        // wastes the whole burst, so fall back to the visible body position until
                        // the predicted intercept itself has a clear line.
                        if (world.Level.Collision.LineOfSight(Pawn.EyePosition, predicted))
                            aimAt = predicted;
                    }
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
        // Route-following never benefits from staring almost straight into a lower waypoint. A
        // modest downward view still shows descents without hiding the map and objective ahead.
        if (!useTargetAim)
            wantPitch = MathF.Max(wantPitch, -0.65f);

        float speed = AimSpeed * (visible || objectiveAim ? 1f : 0.5f);
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
        bool jumpPadRouteIntent = false;
        if (nav.NodeCount == 0) return Vector2.Zero;

        // Pawn.Move applies a pad after the controller has produced this frame's input. Observe
        // the authored launch impulse on the following tick even when the route did not request
        // it (for example a combat strafe across the trigger). This is what makes the same-pad
        // cooldown cover accidental launches as well as planned ones.
        if (!_jumpPadFlight && TryDetectPhysicalJumpPadLaunch(world, out JumpPad launchedPad))
        {
            BeginJumpPadFlight(world, launchedPad);
            if (_path.Count > 0 && _pathCursor < _path.Count
                && TryRouteJumpPad(world, nav, _path[_pathCursor],
                    nav.Nodes[_path[_pathCursor]].Position, out JumpPad routePad)
                && Vector3.DistanceSquared(routePad.Position, launchedPad.Position) < 2f * 2f
                && _pathCursor + 1 < _path.Count)
                _pathCursor++;
        }

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

        // On foot, the answer to being parked is somewhere else to walk. The timer itself is
        // maintained in Update, which runs for riders too — see the anti-park backstop there.
        if (_parkedTimer > 5f
            && TryChoosePatrolGoal(world.Level.Nav, Pawn.Position, ref _onslaughtPatrolStep, 16f))
        {
            _parkedTimer = 0f;
            _goalTimer = _rng.Range(2.2f, 4.5f);
            _repathTimer = 0f;
        }

        // --- path planning ---
        if (_repathTimer <= 0f && _goalNode >= 0)
        {
            _repathTimer = _rng.Range(0.7f, 1.3f);
            int start = nav.FindNearest(Pawn.Position);
            bool AvoidRecentWater(int nodeIndex)
            {
                if (_waterAvoidTimer <= 0f || nodeIndex == start) return true;
                Vector3 feet = nav.Nodes[nodeIndex].Position;
                Vector3 half = new(Physics.PawnRadius, Physics.PawnHeight * 0.5f,
                    Physics.PawnRadius);
                Vector3 center = feet + MathX.Up * half.Y;
                return world.Level.Collision.VolumeAt(center - half, center + half,
                    _collisionScratch) != BrushKind.Water;
            }
            bool AvoidWaterCrossing(int fromNode, int toNode)
            {
                if (_waterAvoidTimer <= 0f) return true;
                // A jump-pad edge may arc over, or through, a pool even when both endpoint
                // capsules are dry. After a rescue, use the dry walkable alternative instead.
                if ((nav.Nodes[fromNode].Flags & NavFlags.JumpPad) != 0) return false;
                return !SegmentCrossesWater(world, nav.Nodes[fromNode].Position,
                    nav.Nodes[toNode].Position);
            }
            Func<int, bool> routeFilter = _waterAvoidTimer > 0f ? AvoidRecentWater : null;
            Func<int, int, bool> transitionFilter = _waterAvoidTimer > 0f
                ? AvoidWaterCrossing : null;
            bool found = start >= 0 && (_objectiveGoal
                ? nav.FindPathToward(start, _goalNode, _path, canVisit: routeFilter,
                    canTraverse: transitionFilter)
                : nav.FindPath(start, _goalNode, _path, canVisit: routeFilter,
                    canTraverse: transitionFilter));
            // A target whose only available route immediately dives into the pool must wait;
            // keep moving on dry land until the post-rescue cooldown expires instead of undoing
            // the escape on the following objective tick.
            if (!found && _waterAvoidTimer > 0f)
            {
                int dryFallback = nav.FindNearest(_lastWaterExitPosition);
                if (dryFallback >= 0 && start >= 0)
                    found = nav.FindPath(start, dryFallback, _path, canVisit: routeFilter,
                        canTraverse: transitionFilter);
            }
            // Random pickups and visible enemies can live on a disconnected navigation island.
            // Do not burn the whole goal timeout at zero input: traverse a distant reachable
            // point, then choose a fresh goal from there. Precise positions must be cleared or
            // the bot would steer back toward the unreachable item after finishing this path.
            if (!found && start >= 0
                && nav.FindPathToFarthestReachable(start, _path, canVisit: routeFilter,
                    canTraverse: transitionFilter))
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
            if (Environment.GetEnvironmentVariable("UNREAL99_NAV_DEBUG") == "1"
                && _navDebugReports++ < 16)
            {
                Vector3 startPosition = start >= 0 ? nav.Nodes[start].Position : Pawn.Position;
                Vector3 goalPosition = _goalNode >= 0 ? nav.Nodes[_goalNode].Position : Pawn.Position;
                Vector3 firstPosition = _path.Count > 0 ? nav.Nodes[_path[0]].Position : Pawn.Position;
                Vector3 lastPosition = _path.Count > 0 ? nav.Nodes[_path[^1]].Position : Pawn.Position;
                Console.WriteLine($"電腦導航: {DiagnosticActor} · 起點 {startPosition} · " +
                    $"目標 {goalPosition} · 路徑 {_path.Count} · 首點 {firstPosition} · " +
                    $"末點 {lastPosition} · 角色位置 {Pawn.Position}");
            }
        }

        // --- follow the path ---
        Vector3 steer = Vector3.Zero;
        bool pathTraversesSlope = false;
        if (_path.Count > 0 && _pathCursor < _path.Count)
        {
            int waypointIndex = _path[_pathCursor];
            Vector3 node = nav.Nodes[waypointIndex].Position;
            bool waitingForJumpPad = false;
            bool waitingForTeleporter = false;

            // A special nav edge starts at the grid node nearest the pad, which can still be
            // outside the pad's trigger. Do not advance to the far-side node and steer into the
            // gap until the pawn has actually entered the physical launcher.
            if (TryRouteJumpPad(world, nav, waypointIndex, node, out JumpPad pad))
            {
                // Optional elevated weapons and random roam nodes are not a clear reason to take
                // a one-way launcher. They created Olden's repeated bounce even though every
                // control point is reachable on foot. Keep pads available to players and to an
                // actual objective route, but make bot shopping/patrol choose dry ground.
                if (!_objectiveGoal)
                {
                    if (_itemGoal != null)
                    {
                        _blockedItem = _itemGoal;
                        _blockedItemTimer = MathF.Max(_blockedItemTimer, 30f);
                    }
                    _goalNode = -1;
                    _goalTimer = 0f;
                    _hasGoalPosition = false;
                    _itemGoal = null;
                    _pathFound = false;
                    _path.Clear();
                    _pathCursor = 0;
                    _repathTimer = 0f;
                    return Vector2.Zero;
                }
                bool recentlyUsed = _jumpPadReuseTimer > 0f
                    && Vector3.DistanceSquared(pad.Position, _lastJumpPadPosition) < 2f * 2f;
                if (recentlyUsed)
                {
                    // The previous launch did not satisfy this optional goal. Retrying the same
                    // ballistic route is the visible Olden bounce loop, so take the item off the
                    // menu long enough for a different tactical decision and replan immediately.
                    if (_itemGoal != null)
                    {
                        _blockedItem = _itemGoal;
                        _blockedItemTimer = MathF.Max(_blockedItemTimer, 30f);
                    }
                    else if (_hasGoalPosition)
                    {
                        _blockedGoalPosition = _goalPosition;
                        _blockedGoalTimer = MathF.Max(_blockedGoalTimer, 8.5f);
                    }
                    _goalNode = -1;
                    _goalTimer = 0f;
                    _hasGoalPosition = false;
                    _itemGoal = null;
                    _pathFound = false;
                    _path.Clear();
                    _pathCursor = 0;
                    _repathTimer = 0f;
                    return Vector2.Zero;
                }

                jumpPadRouteIntent = true;
                input.AvoidJumpPads = false;
                float padDistance = (pad.Position - Pawn.Position).FlatXZ().Length();
                // Proximity plus airborne state is not proof that the physical pad fired: a
                // normal jump beside its narrower trigger used to enter permanent flight mode
                // and disable ledge recovery over the launch gap. The pad overwrites velocity,
                // so matching that authored impulse is an unambiguous launch signal.
                bool launched = !Pawn.OnGround && padDistance < 2.2f
                    && Pawn.Position.Y < pad.Position.Y + 3.2f
                    && Vector3.DistanceSquared(Pawn.Velocity, pad.LaunchVelocity) < 2.25f;
                if (launched)
                {
                    BeginJumpPadFlight(world, pad);
                    if (_pathCursor + 1 < _path.Count) _pathCursor++;
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
                    // Feet on the adjacent floor can be less than a metre below a thin lift.
                    // That is not aboard: require the capsule's feet to be on its live surface.
                    && MathF.Abs(Pawn.Position.Y - currentSurface.Y) < 0.32f;
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
            pathTraversesSlope = MathF.Abs(heightDelta) > 0.45f;
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
        bool combatSteering = visible && target != null && _state == BotState.Attack && !_objectiveGoal
            && !pathTraversesSlope
            && !_specialTraversalLock
            && _routeRecoveryTimer <= 0f;
        if (combatSteering)
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
        if (_dodgeTimer > 0f && _dodgeTimer < 0.05f && Pawn.OnGround
            && !pathTraversesSlope && !_specialTraversalLock)
        {
            input.Dodge = new Vector2(_rng.Chance(0.5f) ? 1f : -1f, 0f);
            _dodgeTimer = _rng.Range(0.7f, 1.6f);
        }

        // --- grinding along an obstacle ---
        // Raw displacement, which the stuck recovery below keys on, only catches a bot that has
        // been brought to a dead stop. Walk into a wall at an angle and the collision resolver
        // slides the pawn along it at close to full speed, so it covers ground continuously while
        // getting nowhere: a bot pressed into a Torlan bridge pier travelled 21 m in one five
        // second window for 0.25 m of net progress, and nothing in here noticed. Progress toward
        // the waypoint is the thing that has actually stalled, so measure that instead.
        //
        // The response is to skirt: pick a side and add lateral steering, which walks the pawn
        // along the obstacle until its corner clears and the direct line opens up. Re-planning
        // cannot fix this on its own — the route is fine, and the planner hands back the same one.
        Vector3 stallWaypoint = _path.Count > 0 && _pathCursor < _path.Count
            ? nav.Nodes[_path[_pathCursor]].Position
            : Pawn.Position;
        // Only for a waypoint that is near enough that an obstacle, rather than the length of the
        // walk, explains the lack of progress. The graph is a two-metre grid, so an ordinary step
        // is a couple of metres; anything far away is a special edge and handled above.
        bool trackingWaypoint = steer != Vector3.Zero && !combatSteering && !_specialTraversalLock
            && _path.Count > 0 && _pathCursor < _path.Count
            && (nav.Nodes[_path[_pathCursor]].Position - Pawn.Position).FlatXZ().LengthSquared() < 12f * 12f;
        if (trackingWaypoint)
        {
            int waypointNode = _path[_pathCursor];
            if (waypointNode != _stallWaypointNode)
            {
                _stallWaypointNode = waypointNode;
                _stallBestDistance = float.MaxValue;
                _waypointStallTimer = 0f;
            }
            float toWaypoint = (stallWaypoint - Pawn.Position).FlatXZ().Length();
            if (toWaypoint < _stallBestDistance - 0.2f)
            {
                _stallBestDistance = toWaypoint;
                _waypointStallTimer = 0f;
            }
            else _waypointStallTimer += dt;
        }
        else
        {
            _stallWaypointNode = -1;
            _stallBestDistance = float.MaxValue;
            _waypointStallTimer = 0f;
        }

        if (_waypointStallTimer > 1.1f && trackingWaypoint)
        {
            if (_skirtTimer <= 0f)
            {
                // Commit to one side for long enough to actually clear a corner. Alternating every
                // frame is what the failure already looks like from the outside.
                _skirtTimer = 1.4f;
                Vector3 toWaypointFlat = MathX.SafeNormalize(
                    (stallWaypoint - Pawn.Position).FlatXZ(), Pawn.ForwardFlat);
                Vector3 open = new(-toWaypointFlat.Z, 0f, toWaypointFlat.X);
                // Prefer whichever side has more room, so the bot rounds an obstacle rather than
                // burrowing further into the inside of a corner.
                Vector3 eye = Pawn.Position + new Vector3(0f, Pawn.CurrentHeight * 0.5f, 0f);
                bool leftClear = !world.Level.Collision.Raycast(eye, eye + open * 3.5f).Hit;
                bool rightClear = !world.Level.Collision.Raycast(eye, eye - open * 3.5f).Hit;
                _skirtSign = leftClear == rightClear
                    ? (_rng.Chance(0.5f) ? 1f : -1f)
                    : (leftClear ? 1f : -1f);
                if (!leftClear && !rightClear) _skirtTimer = 0.6f;
            }
            Vector3 forwardToWaypoint = MathX.SafeNormalize(
                (stallWaypoint - Pawn.Position).FlatXZ(), Pawn.ForwardFlat);
            Vector3 lateral = new(-forwardToWaypoint.Z, 0f, forwardToWaypoint.X);
            steer = MathX.SafeNormalize(
                forwardToWaypoint * 0.35f + lateral * (_skirtSign * 1.0f), steer);
            if (Pawn.OnGround && _jumpTimer <= 0f && _waypointStallTimer > 2.2f)
            {
                // A kerb or a step is the other thing that eats waypoint progress silently.
                input.Jump = true;
                _jumpTimer = 0.55f;
            }
            // Still nowhere after skirting both ways: the route itself is the problem.
            if (_waypointStallTimer > 4f)
            {
                if (Environment.GetEnvironmentVariable("UNREAL99_NAV_DEBUG") == "1"
                    && Pawn.PlayerIndex >= 0 && _movementDebugReports++ < 48)
                    Console.WriteLine($"貼牆重劃: 玩家 {Pawn.PlayerIndex + 1} · 位置 {Pawn.Position} · "
                        + $"航點 {_stallWaypointNode}@{stallWaypoint} · 路徑游標 {_pathCursor}/{_path.Count}");
                BeginRouteRecovery(world, _itemGoal);
                _waypointStallTimer = 0f;
                _stallWaypointNode = -1;
                _stallBestDistance = float.MaxValue;
                _skirtTimer = 0f;
            }
        }
        _skirtTimer = MathF.Max(0f, _skirtTimer - dt);

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

        // A combat strafe or an unavoidable coarse grid edge can still point through the small
        // physical trigger even after A* chose a route around it. Unless this route explicitly
        // uses the pad's authored airborne edge, bend around the trigger before Pawn.Move sees it.
        if (!jumpPadRouteIntent && Pawn.OnGround)
            steer = SteerAroundUnusedJumpPads(world, steer);

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

    private Vector3 SteerAroundUnusedJumpPads(GameWorld world, Vector3 desired)
    {
        Vector3 direction = MathX.SafeNormalize(desired.FlatXZ(), Vector3.Zero);
        if (direction == Vector3.Zero) return desired;
        const float ProbeDistance = 4f;
        Vector3 end = Pawn.Position + direction * ProbeDistance;
        foreach (JumpPad pad in world.Level.JumpPads)
        {
            if (MathF.Abs(Pawn.Position.Y - pad.Position.Y) > 1.5f) continue;
            Vector3 toPad = (pad.Position - Pawn.Position).FlatXZ();
            float along = MathX.Clamp(Vector3.Dot(toPad, direction), 0f, ProbeDistance);
            Vector3 closest = Pawn.Position + direction * along;
            float clearance = MathF.Max(pad.HalfExtents.X, pad.HalfExtents.Z)
                + Physics.PawnRadius + 0.45f;
            if ((pad.Position - closest).FlatXZ().LengthSquared() >= clearance * clearance)
                continue;

            Vector3 left = new(-direction.Z, 0f, direction.X);
            float side = Vector3.Dot(toPad, left);
            float sign = MathF.Abs(side) > 0.08f
                ? -MathF.Sign(side)
                : (((Pawn.Id + _goalNode) & 1) == 0 ? 1f : -1f);
            return MathX.SafeNormalize(direction * 0.35f + left * sign, direction);
        }
        return direction;
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

    private bool TryDetectPhysicalJumpPadLaunch(GameWorld world, out JumpPad launched)
    {
        launched = default;
        if (Pawn.OnGround) return false;
        float best = 2.6f * 2.6f;
        bool found = false;
        foreach (JumpPad pad in world.Level.JumpPads)
        {
            float distance = Vector3.DistanceSquared(Pawn.Position, pad.Position);
            if (distance >= best
                || Vector3.DistanceSquared(Pawn.Velocity, pad.LaunchVelocity) >= 2.25f) continue;
            best = distance;
            launched = pad;
            found = true;
        }
        return found;
    }

    private void BeginJumpPadFlight(GameWorld world, JumpPad pad)
    {
        _jumpPadFlight = true;
        _lastJumpPadPosition = pad.Position;
        _jumpPadReuseTimer = 18f;
        float horizontalSpeed = MathF.Max(pad.LaunchVelocity.Horizontal(), 0.01f);
        float horizontalDistance = (pad.Destination - pad.Position).FlatXZ().Length();
        float gravity = Physics.Gravity * world.Level.GravityScale;
        float expectedFlight = horizontalDistance > 0.1f
            ? horizontalDistance / horizontalSpeed
            : pad.LaunchVelocity.Y * 2f / MathF.Max(gravity, 0.01f);
        // Preserve the authored ballistic arc, but not forever: a combat impulse can knock a
        // pawn off-course and a permanent flight state disables every other recovery mechanism.
        _jumpPadFlightTimer = MathF.Max(0.8f, expectedFlight + 0.65f);
    }

    /// <summary>
    /// A node can be both normal floor and the source of a pad link. The flag alone does not say
    /// which edge A* selected, so require the following path node to be the authored landing and
    /// require the packed graph edge between them to be the special jump edge.
    /// </summary>
    private bool PathUsesJumpPad(NavGraph nav, int waypointIndex, JumpPad pad)
    {
        if (_pathCursor + 1 >= _path.Count) return false;
        int nextIndex = _path[_pathCursor + 1];
        if (Vector3.DistanceSquared(nav.Nodes[waypointIndex].Position, pad.Position) > 4.5f * 4.5f
            || Vector3.DistanceSquared(nav.Nodes[nextIndex].Position, pad.Destination) > 4.5f * 4.5f)
            return false;
        NavNode source = nav.Nodes[waypointIndex];
        for (int edgeIndex = 0; edgeIndex < source.EdgeCount; edgeIndex++)
        {
            NavEdge edge = nav.Edges[source.FirstEdge + edgeIndex];
            if (edge.To == nextIndex && edge.Jump) return true;
        }
        return false;
    }

    private bool TryRouteJumpPad(GameWorld world, NavGraph nav, int waypointIndex,
        Vector3 waypoint, out JumpPad pad)
    {
        // Normal case: the packed path contains source then landing.
        if ((nav.Nodes[waypointIndex].Flags & NavFlags.JumpPad) != 0
            && TryNearestJumpPad(world, waypoint, out pad)
            && PathUsesJumpPad(nav, waypointIndex, pad)) return true;

        // A* omits its starting node. If the pawn is already beside a pad, the first path node
        // can therefore be the landing. Confirm both physical proximity and the exact packed
        // source-to-landing jump edge before treating that omission as permission to launch.
        if (!TryNearestJumpPad(world, Pawn.Position, out pad)
            || Vector3.DistanceSquared(Pawn.Position, pad.Position) > 4.5f * 4.5f
            || Vector3.DistanceSquared(waypoint, pad.Destination) > 4.5f * 4.5f)
            return false;
        int sourceIndex = nav.FindNearest(pad.Position, 6f);
        if (sourceIndex < 0) return false;
        NavNode source = nav.Nodes[sourceIndex];
        for (int edgeIndex = 0; edgeIndex < source.EdgeCount; edgeIndex++)
        {
            NavEdge edge = nav.Edges[source.FirstEdge + edgeIndex];
            if (edge.To == waypointIndex && edge.Jump) return true;
        }
        return false;
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

        // Domination is won by standing on ground, not by winning fights near it.
        if (world.Mode.Kind == GameModeKind.Domination && Pawn.Team != Team.None
            && TryChooseDominationGoal(world, nav))
            return;

        // Warfare layers the orb on top of the node network: the orb decides the match, so it is
        // checked before the ordinary node push, which it then falls through to.
        if (world.Mode.Kind == GameModeKind.Warfare && Pawn.Team != Team.None
            && TryChooseWarfareGoal(world, nav))
            return;

        if (world.NodeNetworkMode && Pawn.Team != Team.None
            && TryChooseOnslaughtGoal(world, nav))
            return;

        if (world.Mode.Kind == GameModeKind.Assault && Pawn.Team != Team.None
            && TryChooseAssaultGoal(world, nav))
            return;

        if (world.Mode.Kind == GameModeKind.BombingRun && Pawn.Team != Team.None
            && TryChooseBombingRunGoal(world, nav))
            return;

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

            // Re-arm before a flag run when only the starter pistol or a low reserve remains,
            // and search farther when every ranged weapon is dry. This also makes CTF bots use
            // the map's arsenal regardless of their ordinary combat skill.
            bool noRangedAmmo = !HasUsableRangedWeapon(Pawn);
            bool needsSupply = noRangedAmmo || !HasUsefulWeaponUpgrade(Pawn)
                || NeedsCombatResupply(Pawn);
            if (!needsSupply) _ctfRearmAttempts = 0;
            if (ourCarrier < 0 && needsSupply && _ctfRearmAttempts < 2
                && TryChoosePickupGoal(world, noRangedAmmo ? 100f : 45f, combatOnly: true))
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
                // A nearby missing weapon is worth a brief, reachable detour even during combat.
                // The radius grows with skill: Godlike notices the arsenal along its route while
                // Newbie remains easier to starve and distract.
                if (world.Mode.Kind != GameModeKind.Instagib
                    && TryChoosePickupGoal(world, MathX.Lerp(5.5f, 14f, Skill),
                        combatOnly: true, opportunistic: true))
                    return;
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
        // Every mode's objective selector funnels through here, so refusing a destination the
        // route watcher just caught this bot looping in front of makes each of them fall through
        // to its next choice on its own. Returning false rather than silently substituting keeps
        // that decision with the selector that understands the mode.
        if (_blockedGoalTimer > 0f
            && Vector3.DistanceSquared(position, _blockedGoalPosition)
                < BlockedGoalRadius * BlockedGoalRadius)
            return false;

        int node = nav.FindNearest(position);
        if (node < 0) return false;
        _goalNode = node;
        _goalPosition = position;
        _goalRadius = radius;
        _hasGoalPosition = true;
        _objectiveGoal = objective;
        // The caller owns the lifetime of a precise goal. In particular, pickup routes compute a
        // distance-based six-to-fourteen second commitment; clamping that against ChooseGoal's
        // random 2.2–4.5 second seed silently expired a long locker run halfway there. Re-scoring
        // from the new position could then choose a locker behind the bot and produce the saved
        // game's conspicuous back-and-forth shuttle.
        _goalTimer = refresh;
        return true;
    }

    /// <summary>
    /// Domination target selection.
    ///
    /// The score in this mode accrues from ground held, so a bot that simply fights whoever it
    /// can see contributes nothing — it has to go and touch things. Points the team does not own
    /// are worth taking; points it does own are worth a body only once the team is already ahead,
    /// because a defender parked on a lead is worth more than a fourth attacker on the same pad.
    /// </summary>
    private bool TryChooseDominationGoal(GameWorld world, NavGraph nav)
    {
        var points = world.Level.ControlPoints;
        if (points.Count == 0) return false;

        // Objective code runs before the ordinary SeekItem state, so Domination needs the same
        // explicit re-arm diversion as CTF. Two attempts keep a bot from shopping forever while
        // still preventing the common empty-pistol run straight into a defended control point.
        bool noRangedAmmo = !HasUsableRangedWeapon(Pawn);
        bool needsSupply = noRangedAmmo || !HasUsefulWeaponUpgrade(Pawn)
            || NeedsCombatResupply(Pawn);
        if (!needsSupply) _objectiveRearmAttempts = 0;
        if (needsSupply && _objectiveRearmAttempts < 2
            && TryChoosePickupGoal(world, noRangedAmmo ? 100f : 45f, combatOnly: true))
        {
            _objectiveRearmAttempts++;
            return true;
        }

        Team enemy = Pawn.Team == Team.Red ? Team.Blue : Team.Red;
        int ours = world.ControlPointsHeldBy(Pawn.Team);
        int theirs = world.ControlPointsHeldBy(enemy);

        // Number AI-driven teammates within their own team so both ordinary bots and the local
        // demo autopilot receive stable, distributed roles. Counting only Pawn.IsBot made the
        // showcased Godlike player overlap the first bot's assignment.
        int teamBotSlot = 0;
        foreach (Pawn mate in world.Pawns)
            if (mate.Team == Pawn.Team && mate.Id < Pawn.Id && IsAiDriven(world, mate)) teamBotSlot++;

        // Only spare someone for defence while ahead and holding more than one point. Behind or
        // level, every body attacks — sitting on a losing position just loses more slowly.
        bool defend = ours > theirs && ours >= 2 && teamBotSlot % 3 == 0;
        var candidates = new List<int>(points.Count);
        for (int i = 0; i < points.Count && i < world.ControlPointOwners.Count; i++)
        {
            Team owner = world.ControlPointOwners[i];
            bool mine = owner == Pawn.Team;
            // Defenders want ours; attackers want everything that is not.
            if (defend == mine) candidates.Add(i);
        }

        if (candidates.Count == 0) return false;

        // Stable slot assignment spreads a squad, but rotate that assignment over time as well.
        // A team with fewer AI drivers than remaining neutral/enemy points otherwise has no slot
        // assigned to the last point at all: Leadworks' Bridge stayed neutral for an entire run
        // while the same two bots repeatedly retook Tower and Storage. Goal selection already
        // happens on a short cadence, so a deliberately slow phase preserves commitment while
        // guaranteeing every candidate enters the rotation.
        int assignmentPhase = (int)(world.Time / 12f);
        int assigned = candidates[(teamBotSlot + assignmentPhase) % candidates.Count];

        if (defend) return TryChooseDominationPatrolGoal(nav, points[assigned].Position);

        // Attackers commit to entering the point's actual touch radius. Stopping at 0.9 m forced
        // the camera into the decorative marker and created artificial stalls after a capture.
        float captureRadius = MathF.Max(0.75f, points[assigned].Radius * 0.72f);
        return SetPreciseGoal(nav, points[assigned].Position, objective: true,
            radius: captureRadius, refresh: 1.2f);
    }

    /// <summary>
    /// Onslaught target selection.
    ///
    /// The mistake a naive bot makes here is running at the enemy core: it cannot be hurt until
    /// the chain reaches it, so that is a bot jogging across the map to stand somewhere harmless.
    /// The chain therefore drives everything — <see cref="OnslaughtState.NextObjectiveFor"/>
    /// returns only what is actually reachable, and a share of each team stays back on whichever
    /// of its own nodes the enemy can currently touch.
    ///
    /// The other half of the mode is the vehicles. Onslaught maps are big enough that crossing
    /// them on foot loses the game on its own, so before committing to a node a bot will take
    /// anything parked nearby that is pointed the right way.
    /// </summary>
    private int _orbPatrolStep;

    /// <summary>
    /// The three Warfare-specific jobs, in the order the mode rewards them: run our orb at the
    /// enemy prime node, hunt theirs, or go and fetch ours. Anything else falls through to the
    /// ordinary node push, which already understands support nodes and the link chain.
    /// </summary>
    private bool TryChooseWarfareGoal(GameWorld world, NavGraph nav)
    {
        var state = world.Onslaught;
        if (state.Nodes.Count == 0) return false;
        Team enemy = Pawn.Team == Team.Red ? Team.Blue : Team.Red;
        WarfareOrb ours = world.Warfare.OrbOf(Pawn.Team);
        WarfareOrb theirs = world.Warfare.OrbOf(enemy);

        // --- carrying it: the enemy prime node is the prize, because it is the one node they can
        // never shield. Taking it puts their core in reach immediately.
        if (ours != null && ours.CarrierId == Pawn.Id)
        {
            int target = NearestOrbTarget(state, enemy, primeOnly: true);
            if (target < 0) target = NearestOrbTarget(state, enemy, primeOnly: false);
            if (target >= 0)
                return SetPreciseGoal(nav, state.Nodes[target].Position, objective: true,
                    radius: 1.6f, refresh: 0.7f);
        }

        // --- their carrier is the single most valuable target on the map. Chase it.
        if (theirs is { Held: true })
        {
            Pawn carrier = world.FindPawn(theirs.CarrierId);
            if (carrier is { Alive: true }
                && Vector3.Distance(carrier.Position, Pawn.Position) < 90f)
                return SetPreciseGoal(nav, carrier.Position, objective: true, radius: 2.5f, refresh: 0.5f);
        }
        // A dropped enemy orb is worth walking to as well: using it costs 100 health and denies
        // them the run entirely.
        if (theirs is { Dropped: true }
            && Vector3.Distance(theirs.Position, Pawn.Position) < 40f && Pawn.Health > 110f)
            return SetPreciseGoal(nav, theirs.Position, objective: true, radius: 1.4f, refresh: 0.6f);

        // --- nobody has ours: one bot per team goes and gets it, the rest keep pushing nodes.
        if (ours is { Held: false })
        {
            int slot = 0;
            foreach (Pawn mate in world.Pawns)
                if (mate.Team == Pawn.Team && mate.Id < Pawn.Id && IsAiDriven(world, mate)) slot++;
            float toOrb = Vector3.Distance(ours.Position, Pawn.Position);
            // The fetch role is otherwise permanent: the lowest-numbered bot on the team owns it
            // for the whole match. If it cannot actually reach the orb — blocked route, an orb
            // parked somewhere the nav graph does not lead — it stands there for the rest of the
            // round rather than playing. Give the role up for a while once it stops closing.
            if (toOrb < _orbFetchBestDistance - 1.5f)
            {
                _orbFetchBestDistance = toOrb;
                _orbFetchStallSince = world.Time;
            }
            else if (slot == 0 && world.Time - _orbFetchStallSince > 8f)
            {
                _orbFetchBanUntil = world.Time + 12f;
                _orbFetchStallSince = world.Time;
                _orbFetchBestDistance = float.MaxValue;
            }
            if (slot == 0 && world.Time >= _orbFetchBanUntil && toOrb < 130f)
                return SetPreciseGoal(nav, ours.Position, objective: true, radius: 1.4f, refresh: 0.8f);
        }
        else
        {
            _orbFetchBestDistance = float.MaxValue;
            _orbFetchStallSince = world.Time;
        }

        // --- defending a node our orb is shielding: stay inside the shield radius.
        if (ours is { Held: true } && ours.CarrierId != Pawn.Id)
        {
            Pawn carrier = world.FindPawn(ours.CarrierId);
            if (carrier is { Alive: true }
                && Vector3.Distance(carrier.Position, Pawn.Position) < 45f)
                return TryChoosePatrolGoal(nav, carrier.Position, ref _orbPatrolStep, 11f);
        }

        return false;
    }

    /// <summary>Nearest node an orb carrier could flip, optionally restricted to enemy primes.</summary>
    private int NearestOrbTarget(OnslaughtState state, Team enemy, bool primeOnly)
    {
        int best = -1;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < state.Nodes.Count; i++)
        {
            var node = state.Nodes[i];
            if (node.IsCore) continue;
            if (node.Team == Pawn.Team && node.IsActive) continue;
            if (primeOnly && !(node.IsPrime && node.Team == enemy)) continue;
            if (node.OrbShield != Team.None && node.OrbShield != Pawn.Team) continue;
            float d = Vector3.Distance(Pawn.Position, node.Position);
            if (d < bestDistance) { bestDistance = d; best = i; }
        }
        return best;
    }

    private bool TryChooseOnslaughtGoal(GameWorld world, NavGraph nav)
    {
        var state = world.Onslaught;
        if (state.Nodes.Count == 0) return false;

        Team enemy = Pawn.Team == Team.Red ? Team.Blue : Team.Red;

        int teamBotSlot = 0;
        foreach (Pawn mate in world.Pawns)
            if (mate.Team == Pawn.Team && mate.Id < Pawn.Id && IsAiDriven(world, mate)) teamBotSlot++;

        // Everything stops if our own core is exposed: at that point there is no attack worth
        // making, because one more enemy push ends the match outright.
        bool coreExposed = state.CoreVulnerable(Pawn.Team);
        bool defend = coreExposed || teamBotSlot % 3 == 2;

        int goal = -1;
        if (defend)
        {
            goal = state.MostThreatenedFriendly(Pawn.Team, Pawn.Position);
            // Nothing of ours is under threat, so there is nothing to guard — go and push.
            if (goal < 0) defend = false;
        }
        if (goal < 0) goal = state.NextObjectiveFor(Pawn.Team, Pawn.Position);
        // Every reachable node is already ours and the enemy core is still shielded: the front
        // has stalled somewhere, so fall back to guarding whatever the enemy can still reach.
        if (goal < 0) goal = state.MostThreatenedFriendly(Pawn.Team, Pawn.Position);
        if (goal < 0) return false;

        var node = state.Nodes[goal];

        // Once a neutral pad has been activated it builds on its own. A bot without the Pulse
        // beam should guard it while the timer runs, not stand dead still on the exact centre for
        // six seconds. A bot that can accelerate construction deliberately remains in beam range.
        if (!node.IsCore && node.Team == Team.None && node.BuildingFor == Pawn.Team)
        {
            bool canSupport = Pawn.HasWeapon[(int)WeaponKind.PulseGun]
                && Pawn.AmmoFor(WeaponKind.PulseGun) > 0;
            if (!canSupport)
                return TryChoosePatrolGoal(nav, node.Position, ref _onslaughtPatrolStep, 9f);
        }

        // Re-arm before setting out. A bot that walks a hundred metres to a node with an empty
        // pistol arrives as a free frag rather than a capture.
        bool noRangedAmmo = !HasUsableRangedWeapon(Pawn);
        bool needsSupply = noRangedAmmo || NeedsCombatResupply(Pawn);
        if (!needsSupply) _objectiveRearmAttempts = 0;
        if (needsSupply && _objectiveRearmAttempts < 2
            && TryChoosePickupGoal(world, noRangedAmmo ? 100f : 45f, combatOnly: true))
        {
            _objectiveRearmAttempts++;
            return true;
        }

        // Worth driving? Only for a real journey, and only if the ride is closer than the walk.
        float distance = Vector3.Distance(Pawn.Position, node.Position);
        if (distance > 34f && TryBoardVehicle(world, nav, node.Position)) return true;

        // Defenders circle their node rather than standing on it, so they are not free frags and
        // do not block the pad the mode needs them to keep clear. The same applies to anyone sent
        // to a node we already hold at full health — that happens through the stalled-front
        // fallback above, and there is nothing to do on arrival but guard it. Standing on the pad
        // instead left bots motionless on their own core for the rest of the round.
        bool nothingToDoHere = node.Team == Pawn.Team && node.IsActive
            && node.Health >= node.MaxHealth - 0.01f;
        if (node.Team == Pawn.Team && (defend || nothingToDoHere))
            return TryChoosePatrolGoal(nav, node.Position, ref _onslaughtPatrolStep, 10f);

        _ = enemy;
        return SetPreciseGoal(nav, node.Position, objective: true, radius: 3.0f, refresh: 1.2f);
    }

    /// <summary>
    /// Assault target selection.
    ///
    /// Only one objective is live at a time, so both sides have exactly one place to be — which
    /// makes the roles unusually clean. Attackers converge on the current objective; defenders
    /// hold the ground around it rather than standing on it, because a defender inside the ring
    /// of a hold objective stalls the plant but a defender standing on a generator is just a
    /// target. The rest is ordinary combat, which the base behaviour already handles.
    /// </summary>
    /// <summary>
    /// Bombing Run. Four situations, and the right answer differs sharply between them: holding
    /// the ball means run for the hoop and pass rather than fight, because the carrier has no
    /// gun; a team-mate holding it means escort, because the carrier cannot defend themselves;
    /// an enemy holding it means hunt that one pawn rather than the nearest one; and a loose ball
    /// means everyone converges on it. Falling through to ordinary combat is only correct when
    /// the bot is already where it needs to be.
    /// </summary>
    private bool TryChooseBombingRunGoal(GameWorld world, NavGraph nav)
    {
        var br = world.BombingRun;
        if (br.Goals.Count == 0) return false;

        if (Pawn.HasBall)
        {
            // Run it in. The hoop is the goal and nothing else matters — the carrier holds only
            // the Ball Launcher, so stopping to fight is never the better play.
            Vector3 target = br.TargetGoal(Pawn.Team);
            return SetPreciseGoal(nav, target, objective: true, radius: 1.4f, refresh: 0.7f);
        }

        var carrier = br.Carrier >= 0 ? world.FindPawn(br.Carrier) : null;
        if (carrier is { Alive: true })
        {
            if (carrier.Team == Pawn.Team)
            {
                // Escort: sit between the carrier and the hoop so the screen is ahead of them,
                // and stay close enough to take a pass.
                Vector3 ahead = Vector3.Lerp(carrier.Center, br.TargetGoal(Pawn.Team), 0.28f);
                return SetPreciseGoal(nav, ahead, objective: true, radius: 3.2f, refresh: 0.6f);
            }
            // Hunt the enemy carrier specifically. Any other kill leaves the ball moving.
            return SetPreciseGoal(nav, carrier.Position, objective: true, radius: 2.0f, refresh: 0.5f);
        }

        // Loose ball. Chase it unless somebody is clearly closer, in which case cover the hoop
        // this bot is defending so the mode does not collapse into eight players in one spot.
        float mine = Vector3.Distance(Pawn.Position, br.Position);
        int closer = 0;
        foreach (var mate in world.Pawns)
        {
            if (mate == Pawn || !mate.Alive || mate.Team != Pawn.Team) continue;
            if (Vector3.Distance(mate.Position, br.Position) < mine) closer++;
        }
        if (closer >= 2)
            return SetPreciseGoal(nav, br.OwnGoal(Pawn.Team), objective: true, radius: 6f, refresh: 1.2f);
        return SetPreciseGoal(nav, br.Position, objective: true, radius: 1.2f, refresh: 0.6f);
    }

    private bool TryChooseAssaultGoal(GameWorld world, NavGraph nav)
    {
        var state = world.Assault;
        var objective = state.CurrentObjective;
        if (objective == null) return false;

        bool attacking = Pawn.Team == state.Attackers;

        // Convoy deliberately parks transports beside the initial attackers, only about thirty
        // metres from the first objective. Choose that nearby ride before a weapon-locker detour;
        // after walking to the locker the vehicle is behind the bot and no longer shortens the
        // route, so the previous 40 m threshold made the entire Assault fleet decorative.
        float objectiveDistance = Vector3.Distance(Pawn.Position, objective.Position);
        // Both roles use transport. Attackers need to reach the next breach before the clock
        // runs out; defenders need to fall back to that breach before the attackers arrive.
        // Convoy supplies vehicles to both sides specifically for these two journeys.
        float boardDistance = attacking ? 22f : 30f;
        if (objectiveDistance > boardDistance
            && TryBoardVehicle(world, nav, objective.Position, maxJourneyFactor: 1.65f)) return true;

        bool noRangedAmmo = !HasUsableRangedWeapon(Pawn);
        bool needsSupply = noRangedAmmo || NeedsCombatResupply(Pawn);
        // A timed Assault push cannot repeatedly return to a locker during one objective. One
        // opening stop is enough to acquire a real weapon; after that, completing the breach is
        // more valuable than topping up a partially used magazine.
        if (needsSupply && _assaultRearmedObjective != state.Current
            && TryChoosePickupGoal(world, noRangedAmmo ? 100f : 45f, combatOnly: true))
        {
            _assaultRearmedObjective = state.Current;
            return true;
        }

        if (!attacking)
        {
            // Once an attacker starts interacting, collapse onto that location to kill or push
            // them away. Presence alone does not cancel progress; the defence must win the fight.
            if (objective.Kind == ObjectiveKind.Hold && objective.HoldProgress > 0.01f)
                return SetPreciseGoal(nav, objective.Position, objective: true, radius: objective.Radius * 0.6f,
                    refresh: 0.8f);
            return TryChoosePatrolGoal(nav, objective.Position, ref _assaultPatrolStep, 14f);
        }

        // A destroy objective is shot, so stopping at weapon range is correct; the other kinds
        // need a body inside the ring. The standoff has to clear a rocket's own blast radius,
        // or the bot spends the fight blowing itself up against the thing it is attacking —
        // but a bot down to the hammer has to walk right up to it instead.
        bool ranged = HasUsableRangedWeapon(Pawn);
        // The standoff is only worth holding while there is actually a shot from it. Bay panels
        // and doors sit inside their rigs, so a bot that arms itself and then stops at weapon
        // range can end up staring at the hull it is meant to be shooting through — motionless,
        // not firing, and waiting out the round. Close in whenever the line is blocked.
        bool clearShot = ranged && !world.Level.Collision
            .Raycast(Pawn.Center, objective.Position + MathX.Up * 1.5f).Hit;
        float radius = objective.Kind == ObjectiveKind.Destroy
            ? (clearShot ? MathF.Max(objective.Radius + 6f, 10f) : 1.6f)
            : MathF.Max(0.8f, objective.Radius * 0.6f);

        // Hold position at weapon range and let ShootObjective do the work. Earlier attempts to
        // make the bot circle while it fired traded a stall on one map for oscillation on
        // another: the decks these objectives sit on are too narrow for a useful orbit.
        return SetPreciseGoal(nav, objective.Position, objective: true, radius: radius, refresh: 1.0f);
    }

    /// <summary>
    /// Shoots the thing the mode wants shot when there is nobody to fight. Assault's destroy
    /// objectives and Onslaught's nodes are inert scenery to the ordinary combat code — a bot
    /// that only ever fires at pawns walks all the way to a generator and then stands admiring
    /// it, which is exactly what happened before this existed.
    /// </summary>
    /// <summary>
    /// Whether this bot currently has a mode objective worth shooting at all. Used to decide
    /// whether a far-off enemy is worth turning away for.
    /// </summary>
    private bool HasClearObjectiveShot(GameWorld world)
    {
        if (Pawn.Team == Team.None) return false;
        Vector3 aimAt;
        float reach;
        switch (world.Mode.Kind)
        {
            case GameModeKind.Assault:
            {
                return TryGetClearAssaultObjectiveAim(world, out _, out _,
                    rejectUnsafeSplash: true);
            }
            case GameModeKind.Onslaught:
            case GameModeKind.Warfare:
            {
                int i = world.Onslaught.NextObjectiveFor(Pawn.Team, Pawn.Position);
                if (i < 0) return false;
                var node = world.Onslaught.Nodes[i];
                if (!node.IsCore && node.Team == Team.None) return false;
                // Shooting an orb-shielded node accomplishes nothing but giving away your position.
                if (node.OrbShield != Team.None && node.OrbShield != Pawn.Team) return false;
                aimAt = node.Position + MathX.Up * 2.4f;
                reach = ObjectiveReach(Pawn, 55f);
                break;
            }
            default:
                return false;
        }

        FireDef fire = ObjectiveFire(Pawn);
        if (fire.AmmoCost > 0 && Pawn.AmmoFor(Pawn.Weapon) < fire.AmmoCost) return false;
        Vector3 eye = Pawn.EyePosition;
        Vector3 delta = aimAt - eye;
        float distance = delta.Length();
        if (distance < 0.2f || distance > reach) return false;
        if (fire.SplashRadius > 0f && distance < fire.SplashRadius + 2f) return false;
        Vector3 direction = delta / distance;
        return !world.Level.Collision.Raycast(eye,
            eye + direction * MathF.Max(0.1f, distance - 1.6f)).Hit;
    }

    private void ShootObjective(GameWorld world, ref PawnInput input, float dt)
    {
        if (Pawn.Team == Team.None) return;
        Vector3 aimAt;
        float reach;
        bool support = false;

        switch (world.Mode.Kind)
        {
            case GameModeKind.Assault:
            {
                if (!TryGetClearAssaultObjectiveAim(world, out aimAt, out _,
                        rejectUnsafeSplash: false)) return;
                reach = ObjectiveReach(Pawn, 40f);
                break;
            }
            case GameModeKind.Onslaught:
            case GameModeKind.Warfare:
            {
                var state = world.Onslaught;
                int index = state.NextObjectiveFor(Pawn.Team, Pawn.Position);
                if (index < 0) return;
                var node = state.Nodes[index];
                if (node.OrbShield != Team.None && node.OrbShield != Pawn.Team) return;
                // Once a neutral pad has been activated, a Link/Pulse beam accelerates
                // construction. Enemy structures continue to use ordinary fire.
                support = !node.IsCore && node.Team == Team.None
                    && node.BuildingFor == Pawn.Team;
                aimAt = node.Position + MathX.Up * 2.4f;
                reach = support ? Weapons.Get(WeaponKind.PulseGun).Alt.Range : ObjectiveReach(Pawn, 55f);
                break;
            }
            default:
                return;
        }

        Vector3 eye = Pawn.EyePosition;
        Vector3 delta = aimAt - eye;
        float distance = delta.Length();

        if (distance > reach || distance < 0.2f) return;
        if (support && Pawn.Weapon is not (WeaponKind.LinkGun or WeaponKind.PulseGun))
        {
            if (Pawn.HasWeapon[(int)WeaponKind.LinkGun]
                && Pawn.AmmoFor(WeaponKind.LinkGun) > 0)
                input.WeaponSelect = (int)WeaponKind.LinkGun;
            else if (Pawn.HasWeapon[(int)WeaponKind.PulseGun]
                && Pawn.AmmoFor(WeaponKind.PulseGun) > 0)
                input.WeaponSelect = (int)WeaponKind.PulseGun;
            return;
        }
        // Never rocket something you are standing next to. Onslaught in particular wants bots
        // right on top of a node to build it, and a splash weapon fired from there costs more
        // health than the shot is worth.
        if (SwitchIfDryAgainstObjective(ref input)) return;
        float splash = ObjectiveFire(Pawn).SplashRadius;
        if (splash > 0f && distance < splash + 2f) return;
        Vector3 dir = delta / distance;
        // Do not shoot through the map: the line has to be clear or the shot is wasted, and on
        // Assault a rocket into the bulkhead in front of you is worse than wasted.
        var blocked = world.Level.Collision.Raycast(eye,
            eye + dir * MathF.Max(0.05f, distance - 0.12f));
        if (blocked.Hit) return;

        MathX.YawPitchFromDir(dir, out float yaw, out float pitch);

        input.Yaw = Pawn.Yaw + MathX.WrapAngle(yaw - Pawn.Yaw) * (1f - MathF.Exp(-9f * dt));
        input.Pitch = MathX.Damp(Pawn.Pitch, pitch, 9f, dt);
        // Only pull the trigger once actually pointed at it, so the first shots do not spray.
        if (MathF.Abs(MathX.WrapAngle(yaw - Pawn.Yaw)) < 0.14f && MathF.Abs(pitch - Pawn.Pitch) < 0.14f)
        {
            if (support || UseAltAgainstObjectives(Pawn)) input.AltFire = true;
            else input.Fire = true;
        }
    }

    /// <summary>
    /// Which of a weapon's two modes a bot should use on a structure. A charged mode only fires
    /// when the trigger is *released*, so a bot that simply holds fire down charges forever and
    /// never lands a blow — against a target that cannot dodge, the uncharged mode is both
    /// simpler and better.
    /// </summary>
    private static bool UseAltAgainstObjectives(Pawn pawn)
    {
        var def = pawn.WeaponDef;
        return def.Primary.Chargeable && !def.Alt.Chargeable && def.Alt.Interval > 0f
            && (def.Alt.AmmoCost == 0 || pawn.AmmoFor(pawn.Weapon) >= def.Alt.AmmoCost);
    }

    private static FireDef ObjectiveFire(Pawn pawn)
        => UseAltAgainstObjectives(pawn) ? pawn.WeaponDef.Alt : pawn.WeaponDef.Primary;

    /// <summary>
    /// Supplies one shared, verified aim point for Assault movement, aim priority and firing.
    /// Keeping these three users on the same line prevents the controller from deciding a panel
    /// is shootable with one ray, looking somewhere else, and then spending ammo on that view.
    /// </summary>
    private bool TryGetClearAssaultObjectiveAim(GameWorld world, out Vector3 aimAt,
        out float distance, bool rejectUnsafeSplash)
    {
        aimAt = Vector3.Zero;
        distance = 0f;
        AssaultObjective objective = world.Assault.CurrentObjective;
        if (world.Mode.Kind != GameModeKind.Assault
            || objective is not { Kind: ObjectiveKind.Destroy }
            || Pawn.Team != world.Assault.Attackers) return false;

        aimAt = objective.Position + MathX.Up * 1.5f;
        Vector3 origin = Pawn.EyePosition;
        Vector3 delta = aimAt - origin;
        distance = delta.Length();
        float reach = ObjectiveReach(Pawn, 40f);
        if (distance < 0.2f || distance > reach) return false;
        FireDef fire = ObjectiveFire(Pawn);
        if (fire.AmmoCost > 0 && Pawn.AmmoFor(Pawn.Weapon) < fire.AmmoCost) return false;
        if (rejectUnsafeSplash && fire.SplashRadius > 0f
            && distance < fire.SplashRadius + 2f) return false;

        // The objective has a real hit sphere even though its marker mesh is decorative. Permit
        // collision at that sphere's near surface, but reject any ceiling/wall before it.
        float radius = MathF.Max(objective.Radius * 0.6f, 1.9f);
        float clearDistance = MathF.Max(0.05f, distance - radius - 0.08f);
        Vector3 direction = delta / distance;
        return !world.Level.Collision.Raycast(origin,
            origin + direction * clearDistance).Hit;
    }

    /// <summary>
    /// How far this weapon can usefully engage a structure. <see cref="FireDef.Range"/> only
    /// bounds the modes that trace a line; a projectile weapon leaves it at zero, so clamping
    /// against it unconditionally would silently stop the bot ever firing a rocket or a flak
    /// shell at anything.
    /// </summary>
    private static float ObjectiveReach(Pawn pawn, float cap)
    {
        var fire = ObjectiveFire(pawn);
        bool traces = fire.Mode is FireMode.Hitscan or FireMode.Beam or FireMode.Melee;
        return traces && fire.Range > 0f ? MathF.Min(cap, fire.Range) : cap;
    }

    /// <summary>
    /// Falls back to the hammer when the equipped weapon is dry. A structure does not shoot back,
    /// so an empty flak cannon held against a generator is strictly worse than walking up and
    /// hitting it.
    /// </summary>
    private bool SwitchIfDryAgainstObjective(ref PawnInput input)
    {
        var fire = ObjectiveFire(Pawn);
        if (fire.AmmoCost <= 0 || Pawn.AmmoFor(Pawn.Weapon) >= fire.AmmoCost) return false;
        for (int i = (int)WeaponKind.Count - 1; i >= 0; i--)
        {
            var kind = (WeaponKind)i;
            if (!Pawn.HasWeapon[i] || kind == Pawn.Weapon) continue;
            var candidate = Weapons.Get(kind);
            if (candidate.Primary.AmmoCost > 0 && Pawn.AmmoFor(kind) < candidate.Primary.AmmoCost) continue;
            input.WeaponSelect = i;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Picks a vehicle worth walking to on the way somewhere. Only worth it if the ride is much
    /// closer than the destination — a bot that walks eighty metres to a Manta in order to save
    /// a hundred has gained nothing and has spent the whole trip defenceless.
    /// </summary>
    private bool TryBoardVehicle(GameWorld world, NavGraph nav, Vector3 destination,
        float maxJourneyFactor = 1.25f)
    {
        if (Pawn.InVehicle || world.Vehicles.Count == 0) return false;
        // Boarding drops the orb. A carrier walking past a Manta must keep walking.
        if (world.Mode.Kind == GameModeKind.Warfare
            && world.Warfare.OrbOf(Pawn.Team) is { } carried && carried.CarrierId == Pawn.Id)
            return false;

        // Keep the last choice while it is still valid, so the bot does not swap targets
        // every time another vehicle happens to become marginally closer.
        var held = _vehicleTargetId >= 0 ? world.FindVehicle(_vehicleTargetId) : null;
        if (held != null && held.Alive && held.FreeSeat() >= 0
            && (held.Team == Team.None || held.Team == Pawn.Team))
            return SetPreciseGoal(nav, held.Position, objective: true, radius: 2.4f, refresh: 0.8f);

        _vehicleTargetId = -1;
        float toDestination = Vector3.Distance(Pawn.Position, destination);
        Vehicle best = null;
        float bestScore = float.MaxValue;

        foreach (var v in world.Vehicles)
        {
            if (!v.Alive || v.FreeSeat() < 0) continue;
            if (v.Team != Team.None && v.Team != Pawn.Team) continue;
            Vector3 delta = v.Position - Pawn.Position;
            float hullRadius = MathF.Max(v.Def.HalfExtents.X, v.Def.HalfExtents.Z);
            float horizontalGap = MathF.Max(0f, delta.FlatXZ().Length() - hullRadius);
            float verticalGap = MathF.Max(0f, MathF.Abs(delta.Y) - v.Def.HalfExtents.Y);
            // Parked aircraft several metres overhead are not pickups. The former centre-to-centre
            // goal sent infantry underneath a Raptor where UseVehicle could never reach it.
            if (verticalGap > 3.2f) continue;
            float walk = MathF.Sqrt(horizontalGap * horizontalGap + verticalGap * verticalGap);
            if (walk > 45f) continue;
            // It has to actually shorten the journey: a vehicle behind us is a detour.
            float remaining = Vector3.Distance(v.Position, destination);
            if (walk + remaining > toDestination * maxJourneyFactor) continue;
            if (walk < bestScore) { bestScore = walk; best = v; }
        }
        if (best == null) return false;

        _vehicleTargetId = best.Id;
        return SetPreciseGoal(nav, best.Position, objective: true, radius: 2.4f, refresh: 0.8f);
    }

    /// <summary>
    /// Where a crewed vehicle should be heading. Whatever the mode wants is also what the vehicle
    /// wants — there is no separate vehicle objective.
    /// </summary>
    private Vector3 VehicleDestination(GameWorld world, Pawn target)
    {
        switch (world.Mode.Kind)
        {
            case GameModeKind.Onslaught:
            case GameModeKind.Warfare:
            {
                var state = world.Onslaught;
                int index = state.CoreVulnerable(Pawn.Team)
                    ? state.MostThreatenedFriendly(Pawn.Team, Pawn.Position)
                    : state.NextObjectiveFor(Pawn.Team, Pawn.Position);
                if (index < 0) index = state.MostThreatenedFriendly(Pawn.Team, Pawn.Position);
                if (index >= 0) return state.Nodes[index].Position;
                break;
            }
            case GameModeKind.Assault:
            {
                var objective = world.Assault.CurrentObjective;
                if (objective != null) return objective.Position;
                break;
            }
        }
        return target is { Alive: true } ? target.Position : world.Level.Center;
    }

    /// <summary>
    /// Gives ground vehicles the same authored route awareness as infantry. Directly steering at
    /// an objective worked on an empty test plane but drove tanks into the first wall on Convoy
    /// and Frigate. Aircraft retain direct three-dimensional steering; ground/hover craft follow
    /// ordinary nav waypoints with a size-aware arrival radius and periodic replanning.
    /// </summary>
    private Vector3 VehicleSteeringTarget(GameWorld world, Vehicle vehicle, Vector3 destination,
        float dt)
    {
        if (vehicle.Def.Motion == VehicleMotion.Air) return destination;
        _vehiclePathTimer -= dt;
        bool destinationMoved = Vector3.DistanceSquared(destination, _vehiclePathDestination) > 8f * 8f;
        if (_vehiclePathTimer <= 0f || destinationMoved || _vehiclePathCursor >= _vehiclePath.Count)
        {
            _vehiclePathTimer = 1.25f;
            _vehiclePathDestination = destination;
            _vehiclePath.Clear();
            _vehiclePathCursor = 0;
            NavGraph nav = world.Level.Nav;
            int start = nav.FindNearest(vehicle.Position, 30f);
            int goal = nav.FindNearest(destination, 30f);
            if (start >= 0 && goal >= 0) nav.FindPathToward(start, goal, _vehiclePath);
        }

        float reach = MathF.Max(vehicle.Def.HalfExtents.X, vehicle.Def.HalfExtents.Z) + 1.6f;
        while (_vehiclePathCursor < _vehiclePath.Count)
        {
            Vector3 waypoint = world.Level.Nav.Nodes[_vehiclePath[_vehiclePathCursor]].Position;
            if ((waypoint - vehicle.Position).FlatXZ().LengthSquared() > reach * reach) break;
            _vehiclePathCursor++;
        }
        if (_vehiclePathCursor < _vehiclePath.Count)
        {
            // The infantry graph is sampled every two metres. Steering a fast vehicle at the
            // first node outside its hull asks it to turn inside a radius it physically cannot
            // make; hover craft then orbit that node at full steering lock. Aim several nodes
            // ahead, while retaining the cursor so walls and completed nodes are still respected.
            // Use at least the vehicle's full-speed turning radius. A target inside that circle
            // is geometrically impossible to intercept without orbiting it; that was the exact
            // full-steering-lock spin seen on Mantas, Vipers and hoverboards.
            float lookAhead = MathX.Clamp(vehicle.Def.MaxSpeed
                / MathF.Max(vehicle.Def.TurnRate, 0.1f) * 1.25f, reach, 24f);
            int targetCursor = _vehiclePathCursor;
            for (int i = _vehiclePathCursor; i < _vehiclePath.Count; i++)
            {
                targetCursor = i;
                Vector3 candidate = world.Level.Nav.Nodes[_vehiclePath[i]].Position;
                if ((candidate - vehicle.Position).FlatXZ().Length() >= lookAhead) break;
            }
            return world.Level.Nav.Nodes[_vehiclePath[targetCursor]].Position;
        }
        return destination;
    }

    /// <summary>
    /// Flies, drives or walks a vehicle. What separates a competent crew from a bad one in this
    /// game is not aim, it is standoff: armour that closes to knife range dies to the rockets it
    /// could have out-ranged, and light attack vehicles that hang back never trade at all. So the
    /// hold distance is derived from the vehicle's own class, and everything else — steering,
    /// altitude, when to bail out — follows from trying to sit at it.
    ///
    /// Gunners in the passenger seats are handled separately: they never touch the controls and
    /// simply fight from a moving platform, which is the entire reason those seats exist.
    /// </summary>
    private void DriveVehicle(GameWorld world, Pawn target, bool targetVisible, ref PawnInput input,
        float dt)
    {
        var v = world.FindVehicle(Pawn.VehicleId);
        if (v == null || !v.Alive) return;
        var def = v.Def;
        bool driver = Pawn.VehicleSeat == 0;

        Vector3 destination = VehicleDestination(world, target);
        Vector3 aimAt = targetVisible && target != null ? target.Center : destination;
        AssaultObjective assaultTarget = world.Mode.Kind == GameModeKind.Assault
            ? world.Assault.CurrentObjective : null;
        if (!targetVisible && assaultTarget is { Kind: ObjectiveKind.Destroy })
            aimAt = assaultTarget.Position + MathX.Up * 1.5f;
        // Lead a moving target: at vehicle-weapon ranges the flight time is long enough to miss by
        // a whole body length otherwise.
        if (targetVisible && target != null)
            aimAt += target.Velocity * MathX.Clamp(Vector3.Distance(v.Position, target.Center) / 90f, 0f, 0.55f);

        // --- gunner seats ---
        if (!driver)
        {
            Vector3 muzzle = v.SeatWorld(Pawn.VehicleSeat);
            MathX.YawPitchFromDir(MathX.SafeNormalize(aimAt - muzzle, MathX.Forward),
                out float gYaw, out float gPitch);
            input.Yaw = Pawn.Yaw + MathX.WrapAngle(gYaw - Pawn.Yaw) * (1f - MathF.Exp(-7f * dt));
            input.Pitch = MathX.Damp(Pawn.Pitch, gPitch, 7f, dt);
            input.Fire = targetVisible && target != null && target.Team != Pawn.Team;
            // A gunner whose driver has abandoned the vehicle is just a stationary target.
            if (!v.Occupied || v.Driver < 0) { _vehicleBoardTimer = 0.6f; input.UseVehicle = true; }
            return;
        }

        // Occupying a genuinely unarmed driver seat is transport work, not combat. Leave it
        // alone here; crew in armed passenger seats continue fighting through the gunner branch.
        if (def.Seats.Length == 0 || !def.Seats[0].Armed)
        {
            input.Fire = false;
            input.AltFire = false;
        }

        // --- driver ---
        // Hold distance by class. Artillery and heavy armour fight from range and lose if they
        // close; light attack vehicles have to be on top of things to do anything at all.
        float hold = def.Kind switch
        {
            VehicleKind.Spma or VehicleKind.Leviathan => 55f,
            VehicleKind.Goliath or VehicleKind.IonTank or VehicleKind.Darkwalker
                or VehicleKind.Nemesis or VehicleKind.Paladin => 34f,
            VehicleKind.Hellbender or VehicleKind.Cicada => 26f,
            VehicleKind.Raptor or VehicleKind.Fury => 20f,
            VehicleKind.Hoverboard => 4f,
            _ => 6f,
        };

        bool turret = def.Seats.Length > 0 && def.Seats[0].Turret;
        float distance = (destination - v.Position).FlatXZ().Length();
        bool hasModeRoute = world.Mode.Kind is GameModeKind.Assault
            or GameModeKind.Onslaught or GameModeKind.Warfare;
        // Fixed guns do need the hull to aim, but seeing an enemy must not replace a long mode
        // route. That old policy made every moving opponent a new steering destination and left
        // objective vehicles turning in circles. Deliver the vehicle to its objective first;
        // once it reaches its class standoff, it can pivot and fight normally.
        Vector3 weaponFlat = target != null ? (aimAt - v.Position).FlatXZ() : Vector3.Zero;
        float enemyDistance = weaponFlat.Length();
        float enemyYawError = enemyDistance > 0.01f
            ? MathF.Abs(MathX.WrapAngle(MathF.Atan2(weaponFlat.X, weaponFlat.Z) - v.Yaw))
            : 0f;
        bool closeAlignedTarget = enemyDistance <= 18f && enemyYawError <= 0.35f;
        bool hullCombatSteering = !turret && targetVisible && target != null
            && (!hasModeRoute || distance <= hold + 3f || closeAlignedTarget);
        Vector3 steeringTarget = hullCombatSteering
            ? aimAt : VehicleSteeringTarget(world, v, destination, dt);
        Vector3 flat = (steeringTarget - v.Position).FlatXZ();
        float steeringDistance = flat.Length();

        // Deployable artillery is worthless mobile and devastating parked, so deploy on arrival.
        if (def.CanDeploy && distance <= hold * 1.2f && v.Deploy <= 0f && !v.Deploying)
            input.AltFire = true;

        float desiredYaw = steeringDistance > 0.01f
            ? MathF.Atan2(flat.X, flat.Z)
            : v.Yaw;
        float yawError = MathX.WrapAngle(desiredYaw - v.Yaw);
        // Steering is inverted relative to the yaw error: the solver subtracts the input.
        input.Move.X = MathX.Clamp(-yawError * 1.8f, -1f, 1f);

        // Throttle. Back off when a standoff vehicle has been pushed inside its own hold range —
        // a tank that lets infantry get underneath it cannot depress far enough to answer.
        float throttle;
        if (distance > hold) throttle = MathF.Abs(yawError) > 1.25f ? 0.45f : 1f;
        else if (distance < hold * 0.55f && hold > 12f) throttle = -0.7f;
        else throttle = 0f;
        input.Move.Y = throttle;

        // Aircraft hold an altitude over whatever they are attacking rather than orbiting at
        // whatever height they happened to take off at.
        if (def.Motion == VehicleMotion.Air)
        {
            float desiredY = aimAt.Y + (targetVisible ? 14f : 20f);
            if (v.Position.Y < desiredY - 2f) input.Jump = true;
            else if (v.Position.Y > desiredY + 2f) input.Crouch = true;
        }

        // A hull-mounted weapon aims by steering, so it fires only when already pointed there;
        // a turret seat aims independently and can shoot across the arc.
        if (turret)
        {
            Vector3 muzzle = v.SeatWorld(0);
            MathX.YawPitchFromDir(MathX.SafeNormalize(aimAt - muzzle, MathX.Forward),
                out float tYaw, out float tPitch);
            input.Yaw = Pawn.Yaw + MathX.WrapAngle(tYaw - Pawn.Yaw) * (1f - MathF.Exp(-6f * dt));
            input.Pitch = MathX.Damp(Pawn.Pitch, tPitch, 6f, dt);
        }
        else
        {
            // Hull-mounted weapon: the yaw is the vehicle's, but the elevation still comes from
            // the driver's view, so a Manta can shoot at something above or below it.
            // Vehicle model yaw 0 faces +Z; pawn/camera yaw 0 faces -Z. The half-turn is the same
            // convention conversion used by HandleVehicleFire. Without it CanSee looked behind
            // every hull-mounted driver and never acquired the opponent straight ahead.
            input.Yaw = MathX.WrapAngle(v.Yaw + MathX.Pi);
            input.Pitch = MathX.Damp(Pawn.Pitch,
                MathF.Atan2(aimAt.Y - (v.Position.Y + 1f), MathF.Max(distance, 1f)), 6f, dt);
        }

        if (def.Seats.Length > 0 && def.Seats[0].Armed && !input.AltFire)
        {
            weaponFlat = (aimAt - v.Position).FlatXZ();
            float weaponYaw = weaponFlat.LengthSquared() > 0.001f
                ? MathF.Atan2(weaponFlat.X, weaponFlat.Z) : v.Yaw;
            float weaponYawError = MathX.WrapAngle(weaponYaw - v.Yaw);
            bool aimed = turret || MathF.Abs(weaponYawError) < 0.22f;
            bool assaultObjectiveShot = Pawn.Team == world.Assault.Attackers
                && assaultTarget is { Kind: ObjectiveKind.Destroy };
            // The camera can see over a cockpit rim or through a gap that the actual weapon
            // muzzle cannot use. Validate every bot vehicle shot from the physical firing seat;
            // this prevents Mantas on Frigate from repeatedly firing into the deck beneath them.
            bool clearWeaponShot = true;
            if ((targetVisible && target != null) || assaultObjectiveShot)
            {
                Vector3 muzzle = v.SeatWorld(0) + MathX.Up * 0.4f;
                Vector3 shot = aimAt - muzzle;
                float shotDistance = shot.Length();
                if (shotDistance > 1.8f)
                {
                    Vector3 direction = shot / shotDistance;
                    clearWeaponShot = !world.Level.Collision.Raycast(muzzle,
                        muzzle + direction * (shotDistance - 1.6f)).Hit;
                }
            }
            // Objectives are legitimate targets in their own right: shelling a node or a
            // generator from outside its defenders' range is what the heavy vehicles are for.
            bool objectiveInRange = distance < hold * 1.6f && world.Mode.Kind switch
            {
                GameModeKind.Assault => assaultObjectiveShot && clearWeaponShot,
                GameModeKind.Onslaught or GameModeKind.Warfare => true,
                _ => false,
            };
            input.Fire = aimed && clearWeaponShot
                && ((targetVisible && target != null && target.Team != Pawn.Team)
                || objectiveInRange);
        }


        // --- obstacle recovery and bail-out conditions ---
        float frameTravel = Vector3.Distance(v.Position, _lastVehiclePosition);
        // Deliberately permissive: this only has to catch a hull that has stopped dead. A fixed
        // 0.30 m per frame branded every vehicle below 18 m/s stationary at 60 Hz and sent slow
        // tanks into recovery while they were visibly advancing. Anything subtler than a dead
        // stop — grinding along a wall, circling the goal — is caught by the no-gain test below,
        // which measures the only thing that matters: whether the destination is getting closer.
        float stationaryDistance = MathF.Max(0.025f, dt * 0.75f);
        _vehicleStuckTimer = frameTravel < stationaryDistance
            ? _vehicleStuckTimer + dt
            : 0f;
        _vehicleProgressTimer += dt;
        _vehicleProgressPath += frameTravel;
        _lastVehiclePosition = v.Position;

        // Closing on the destination is the only thing that counts as progress. A vehicle that
        // has stopped closing is either wedged, orbiting, or driving a route that does not
        // actually reach the goal, and in every one of those cases the bot is better off on
        // foot. Chasing a visible enemy is exempt: that destination moves by design.
        bool chasingTarget = hullCombatSteering && !hasModeRoute;
        // A goal that advances — the next node in the chain, the next objective — legitimately
        // puts the bot further away than it has ever been. Start the measurement over instead
        // of reading that jump as a failure to close.
        if (Vector3.DistanceSquared(destination, _vehicleBestDestination) > 8f * 8f)
        {
            _vehicleBestDestination = destination;
            _vehicleBestDistance = float.MaxValue;
            _vehicleNoGainTimer = 0f;
        }
        if (chasingTarget || distance < _vehicleBestDistance - 1.5f)
        {
            _vehicleBestDistance = MathF.Min(_vehicleBestDistance, distance);
            _vehicleNoGainTimer = 0f;
        }
        else
        {
            _vehicleNoGainTimer += dt;
        }

        if (_vehicleProgressTimer >= 1.5f)
        {
            float net = Vector3.Distance(v.Position, _vehicleProgressOrigin);
            bool stationary = net < 1.1f;
            bool oscillating = _vehicleProgressPath > 6f && net < 2.8f;
            if ((stationary || oscillating) && throttle != 0f)
            {
                _vehicleRecoveryTimer = 1.15f;
                _vehicleRecoveryAttempts++;
                _vehiclePathTimer = 0f;
            }
            else if (net > 5f)
            {
                _vehicleRecoveryAttempts = 0;
            }
            _vehicleProgressTimer = 0f;
            _vehicleProgressPath = 0f;
            _vehicleProgressOrigin = v.Position;
        }

        if (_vehicleRecoveryTimer > 0f)
        {
            _vehicleRecoveryTimer = MathF.Max(0f, _vehicleRecoveryTimer - dt);
            // Reverse through a fixed arc before replanning. Alternating the arc after every
            // failed attempt prevents the same nose-first collision from repeating forever.
            input.Move.Y = -0.85f;
            input.Move.X = ((_vehicleRecoveryAttempts + Pawn.Id) & 1) == 0 ? 1f : -1f;
            input.Fire = false;
        }

        bool wrecked = v.Health < def.Health * 0.14f;
        bool arrivedOnTransport = def.Kind == VehicleKind.Hoverboard && distance < 8f;
        bool hasArmedPassenger = false;
        for (int seat = 1; seat < v.Occupants.Length; seat++)
            if (v.Occupants[seat] >= 0 && def.Seats[seat].Armed) { hasArmedPassenger = true; break; }
        bool arrivedUnarmedTransport = !def.Seats[0].Armed && !hasArmedPassenger && distance < 10f;
        // A vehicle deliberately stops at its class hold distance. The dismount threshold must
        // therefore include that distance; using only objective radius left a Manta parked at
        // 6 m forever while waiting to get within a 5.4 m interaction radius.
        bool arrivedAtInfantryObjective = assaultTarget is { Kind: not ObjectiveKind.Destroy }
            && distance <= MathF.Max(assaultTarget.Radius + 2f, hold + 0.5f);
        // Neutral Onslaught nodes are activated by a pawn touching the pad; vehicle weapons do
        // nothing to them. An armed driver otherwise obeys its combat standoff and circles/fires
        // forever just outside the touch radius. Dismount once the hull has delivered the pawn.
        bool arrivedAtNeutralPowerNode = false;
        if (world.NodeNetworkMode)
        {
            int nodeIndex = world.Onslaught.NearestWithin(destination, 5f);
            if (nodeIndex >= 0)
            {
                PowerNode node = world.Onslaught.Nodes[nodeIndex];
                arrivedAtNeutralPowerNode = !node.IsCore && node.Team == Team.None
                    && distance <= MathF.Max(7f, hold + 0.5f);
            }
        }
        // At a destroy objective, stationary firing with a clear line is useful. Stationary and
        // unable to shoot is a blocked approach, even though the standoff controller has set
        // throttle to zero; bail out so the pawn can finish on foot instead of looking stuck.
        bool blockedAtAssaultObjective = Pawn.Team == world.Assault.Attackers
            && assaultTarget is { Kind: ObjectiveKind.Destroy }
            && distance <= hold * 1.1f && !input.Fire;
        // Only judge a lack of gain once the bot is still meaningfully short of where it wanted
        // to be — a vehicle sitting at its standoff distance has arrived, not stalled. Seven
        // seconds is long enough to cover a wide craft working its way around an obstacle and
        // short enough that a wedged one does not strand its passenger for the whole round.
        bool notClosing = _vehicleNoGainTimer > 7f && distance > hold + 4f;
        bool jammed = _vehicleRecoveryAttempts >= 3 || notClosing
            || _vehicleStuckTimer > 4.5f && (throttle != 0f || blockedAtAssaultObjective);
        if (_vehicleStuckTimer > 2.25f && _vehicleRecoveryTimer <= 0f)
        {
            // Give a blocked vehicle one fresh route before abandoning it. This prevents a wide
            // craft from repeatedly dismounting at the same wall without trying the next aisle.
            _vehiclePathTimer = 0f;
            input.Move.Y = -0.8f;
            input.Move.X = Pawn.Id % 2 == 0 ? 1f : -1f;
        }
        if ((wrecked || arrivedOnTransport || arrivedUnarmedTransport
             || arrivedAtInfantryObjective || arrivedAtNeutralPowerNode || jammed)
            && _vehicleBoardTimer <= 0f)
        {
            _vehicleBoardTimer = 0.8f;
            _vehicleStuckTimer = 0f;
            _vehicleRecoveryTimer = 0f;
            _vehicleRecoveryAttempts = 0;
            input.UseVehicle = true;
        }
    }

    /// <summary>
    /// Generic "hold this area without standing on it" movement, shared by the Onslaught and
    /// Assault defenders. A goal placed exactly on the thing being guarded completes every frame
    /// and leaves the bot vibrating in place.
    /// </summary>
    private bool TryChoosePatrolGoal(NavGraph nav, Vector3 point, ref int step, float range)
    {
        _navScratch.Clear();
        nav.QueryRadius(point, range, _navScratch);
        if (_navScratch.Count == 0) return false;

        // Advance around the point from wherever the bot already stands rather than jumping to an
        // unrelated bearing. A golden-angle hop of ~137° reads as pacing back and forth — and on a
        // narrow deck, where only a couple of nodes qualify, it degenerates into a shuttle that
        // the oscillation detector rightly flags. A steady 60° step is an orbit.
        Vector3 bearing = (Pawn.Position - point).FlatXZ();
        float current = bearing.LengthSquared() > 0.04f
            ? MathF.Atan2(bearing.Z, bearing.X)
            : Pawn.Id * 0.71f;
        // Direction of travel is fixed per bot, so two defenders circle opposite ways.
        float sweep = ((Pawn.Id & 1) == 0 ? 1f : -1f) * 1.05f;
        float angle = current + sweep;
        step++;
        Vector3 desired = point + new Vector3(MathF.Cos(angle) * range * 0.65f, 0f,
            MathF.Sin(angle) * range * 0.65f);
        int best = -1;
        float bestScore = float.MaxValue;
        foreach (int nodeIndex in _navScratch)
        {
            NavNode node = nav.Nodes[nodeIndex];
            float fromPoint = (node.Position - point).FlatXZ().Length();
            if (fromPoint < range * 0.28f || fromPoint > range * 0.95f) continue;
            float score = (node.Position - desired).FlatXZ().Length() + node.Openness * 1.2f;
            if (score < bestScore) { bestScore = score; best = nodeIndex; }
        }
        if (best < 0) return false;
        return SetPreciseGoal(nav, nav.Nodes[best].Position, objective: false, radius: 1.2f, refresh: 3.0f);
    }

    private static bool IsAiDriven(GameWorld world, Pawn pawn)
    {
        Controller controller = world.ControllerFor(pawn);
        return controller is BotController || controller is PlayerController { AutoPilot: not null };
    }

    /// <summary>
    /// Moves a defender between reachable navigation nodes around its assigned point. A precise
    /// goal placed directly on an already-owned marker completed every frame and left the bot
    /// motionless long enough to fail the traversal gate.
    /// </summary>
    private bool TryChooseDominationPatrolGoal(NavGraph nav, Vector3 point)
    {
        _navScratch.Clear();
        nav.QueryRadius(point, 9f, _navScratch);
        if (_navScratch.Count == 0) return false;

        float angle = _dominationPatrolStep++ * 2.3999632f + Pawn.Id * 0.71f;
        Vector3 desired = point + new Vector3(MathF.Cos(angle) * 6f, 0f, MathF.Sin(angle) * 6f);
        int best = -1;
        float bestScore = float.MaxValue;
        foreach (int nodeIndex in _navScratch)
        {
            NavNode node = nav.Nodes[nodeIndex];
            float fromPoint = (node.Position - point).FlatXZ().Length();
            if (fromPoint < 2.5f || fromPoint > 8.5f) continue;
            float score = (node.Position - desired).FlatXZ().Length() + node.Openness * 1.2f;
            if (score < bestScore) { bestScore = score; best = nodeIndex; }
        }
        if (best < 0) return false;

        return SetPreciseGoal(nav, nav.Nodes[best].Position, objective: false,
            radius: 1.0f, refresh: 3.0f);
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

    private bool TryChoosePickupGoal(GameWorld world, float maxDistance, bool combatOnly,
        bool opportunistic = false)
    {
        NavGraph nav = world.Level.Nav;
        int start = nav.FindNearest(Pawn.Position);
        if (start < 0) return false;

        _pickupChoices.Clear();
        bool noRangedAmmo = !HasUsableRangedWeapon(Pawn);
        bool needsCombatSupply = NeedsCombatResupply(Pawn);

        foreach (PickupEntity item in world.Pickups)
        {
            if (!item.Active) continue;
            if (item == _blockedItem && _blockedItemTimer > 0f) continue;
            // A locker is the most combat-relevant pickup on the map — it hands over a whole
            // arsenal at once — so it has to survive the combat-only filter. Leaving it out made
            // every locker-armed arena unplayable for bots: they went looking for a gun, found
            // only lockers, matched nothing, and stood still.
            if (combatOnly && item.Kind is not (PickupKind.WeaponPickup or PickupKind.AmmoPickup
                    or PickupKind.WeaponLocker))
                continue;
            if (opportunistic && !IsUsefulOpportunisticPickup(Pawn, item)) continue;

            float desire = item.DesireFor(Pawn);
            if (desire <= 0.05f) continue;
            float distance = Vector3.Distance(Pawn.Position, item.Position);
            if (distance > maxDistance) continue;

            float score = desire * 55f / MathF.Max(distance, 3f);
            if (item.Kind == PickupKind.WeaponPickup)
            {
                if (!Pawn.HasWeapon[(int)item.Weapon])
                    score += 16f + Weapons.Get(item.Weapon).BotPreference * 5f;
                if (noRangedAmmo) score += 28f;
                else if (needsCombatSupply) score += 22f;
            }
            else if (item.Kind == PickupKind.WeaponLocker)
            {
                // Scored off the best gun on the rack the bot is missing, then weighted like a
                // weapon pickup — one trip here settles rearming outright, so a bot with nothing
                // to shoot with should prefer it over a single loose gun at the same range.
                float bestOnRack = 0f;
                foreach (WeaponKind w in item.LockerWeapons)
                    if (!Pawn.HasWeapon[(int)w])
                        bestOnRack = MathF.Max(bestOnRack, Weapons.Get(w).BotPreference);
                if (bestOnRack > 0f) score += 18f + bestOnRack * 5f;
                if (noRangedAmmo) score += 30f;
                else if (needsCombatSupply) score += 22f;
            }
            else if (item.Kind == PickupKind.AmmoPickup && needsCombatSupply)
            {
                score += noRangedAmmo ? 22f : 16f;
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
            if (choice.Item.Kind == PickupKind.WeaponPickup) DiagnosticWeaponPickupGoals++;
            else if (choice.Item.Kind == PickupKind.AmmoPickup) DiagnosticAmmoPickupGoals++;
            if (Environment.GetEnvironmentVariable("UNREAL99_BOT_DEBUG") == "1"
                && _pickupDebugReports++ < 16)
            {
                string itemName = choice.Item.Kind switch
                {
                    PickupKind.WeaponPickup => GameTypes.WeaponName(choice.Item.Weapon),
                    PickupKind.AmmoPickup => $"{choice.Item.Ammo} ammo",
                    _ => choice.Item.Kind.ToString(),
                };
                Console.WriteLine($"電腦補給: {DiagnosticActor} · {itemName} · " +
                    $"物品 {choice.Item.Position} · 節點 {nav.Nodes[choice.GoalNode].Position} · " +
                    $"距離 {distance:0.0} · 期限 {refresh:0.0}s");
            }
            return true;
        }
        if (Environment.GetEnvironmentVariable("UNREAL99_BOT_DEBUG") == "1"
            && _pickupDebugReports++ < 16)
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
            Console.WriteLine($"電腦補給失敗: {DiagnosticActor} · " +
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
            if (!IsCombatWeapon(def) || def.Primary.Mode == FireMode.Melee) continue;
            if (def.Ammo == AmmoKind.None || pawn.AmmoFor(kind) > 0) return true;
        }
        return false;
    }

    private static bool HasUsefulWeaponUpgrade(Pawn pawn)
    {
        for (int i = 0; i < (int)WeaponKind.Count; i++)
        {
            WeaponKind kind = (WeaponKind)i;
            if (kind is WeaponKind.ImpactHammer or WeaponKind.Enforcer || !pawn.HasWeapon[i]) continue;
            WeaponDef def = Weapons.Get(kind);
            if (!IsCombatWeapon(def)) continue;
            if (def.Ammo == AmmoKind.None || pawn.AmmoFor(kind) > 0) return true;
        }
        return false;
    }

    /// <summary>
    /// Resource-management threshold used before the binary "all ammo is gone" fallback.
    /// Higher-skill bots maintain a larger reserve and deliberately upgrade the starter loadout;
    /// lower tiers retain less disciplined resource management.
    /// </summary>
    private bool NeedsCombatResupply(Pawn pawn)
    {
        if (!HasUsableRangedWeapon(pawn)) return true;
        // Godlike and the upper tiers deliberately secure a real weapon before combat. Newbie
        // opponents still encounter weapons during ordinary roaming but do not gain the same
        // disciplined opening route, preserving their intended difficulty handicap.
        if (!HasUsefulWeaponUpgrade(pawn)) return Skill >= 0.55f;

        float bestReserve = 0f;
        for (int i = 0; i < (int)WeaponKind.Count; i++)
        {
            if (!pawn.HasWeapon[i]) continue;
            WeaponDef def = Weapons.Get((WeaponKind)i);
            if (!IsCombatWeapon(def) || def.Primary.Mode == FireMode.Melee
                || def.Ammo == AmmoKind.None) continue;
            float fraction = pawn.AmmoFor(def.Kind) / (float)Math.Max(1, def.MaxAmmo);
            bestReserve = MathF.Max(bestReserve, fraction);
        }
        return bestReserve < MathX.Lerp(0.06f, 0.24f, Skill);
    }

    private static bool IsCombatWeapon(WeaponDef def)
        => def.Kind is not (WeaponKind.Translocator or WeaponKind.BallLauncher)
           && (def.Primary.Damage > 0f || def.Primary.SplashDamage > 0f
               || def.Alt.Damage > 0f || def.Alt.SplashDamage > 0f);

    /// <summary>Whether a combatant should step off its current route for this nearby pickup.</summary>
    private bool IsUsefulOpportunisticPickup(Pawn pawn, PickupEntity item)
    {
        if (item.Kind == PickupKind.WeaponPickup)
        {
            WeaponDef def = Weapons.Get(item.Weapon);
            if (def.Primary.Mode == FireMode.Melee) return false;
            if (!pawn.HasWeapon[(int)item.Weapon]) return true;
            return pawn.AmmoFor(item.Weapon) / (float)Math.Max(1, def.MaxAmmo)
                < MathX.Lerp(0.16f, 0.42f, Skill);
        }
        if (item.Kind != PickupKind.AmmoPickup || item.Ammo == AmmoKind.None) return false;

        bool ownsMatchingWeapon = false;
        for (int i = 0; i < (int)WeaponKind.Count; i++)
        {
            if (!pawn.HasWeapon[i] || Weapons.All[i].Ammo != item.Ammo) continue;
            ownsMatchingWeapon = true;
            break;
        }
        if (!ownsMatchingWeapon) return false;
        float fraction = pawn.Ammo[(int)item.Ammo]
            / (float)Math.Max(1, Pawn.MaxAmmoFor(item.Ammo));
        return fraction < MathX.Lerp(0.14f, 0.38f, Skill);
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
            {
                if (_waterAvoidTimer <= 0f
                    || !SegmentCrossesWater(world, Pawn.Position,
                        world.Level.Nav.Nodes[_path[_pathCursor]].Position))
                    return;
            }
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
        Vector3 rayStart = probe + new Vector3(0f, 2.4f, 0f);
        var hit = world.Level.Collision.Raycast(rayStart,
            rayStart - new Vector3(0f, 6.65f, 0f));
        if (!hit.Hit || hit.Kind == BrushKind.Lava) return false;
        return _waterAvoidTimer <= 0f || !IsWaterAtFeet(world, hit.Point + MathX.Up * 0.05f);
    }

    private bool SegmentCrossesWater(GameWorld world, Vector3 from, Vector3 to)
    {
        float distance = Vector3.Distance(from, to);
        int samples = Math.Max(1, (int)MathF.Ceiling(distance / 0.55f));
        for (int sample = 1; sample <= samples; sample++)
        {
            Vector3 feet = Vector3.Lerp(from, to, sample / (float)samples);
            // Coarse graph endpoints can both sit at bank height while the straight chord
            // between them passes over the pool. Sampling the interpolated Y would call that
            // dry air. Drop onto the real supporting floor at every sample; a causeway hits its
            // solid deck, while an unsafe chord hits the submerged basin floor.
            Vector3 top = feet + MathX.Up * 2.4f;
            var support = world.Level.Collision.Raycast(top, top - MathX.Up * 9f);
            if (!support.Hit || IsWaterAtFeet(world, support.Point + MathX.Up * 0.05f)) return true;
        }
        return false;
    }

    private bool IsWaterAtFeet(GameWorld world, Vector3 feet)
    {
        Vector3 half = new(Physics.PawnRadius, Physics.PawnHeight * 0.5f,
            Physics.PawnRadius);
        Vector3 center = feet + MathX.Up * half.Y;
        return world.Level.Collision.VolumeAt(center - half, center + half,
            _collisionScratch) == BrushKind.Water;
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
