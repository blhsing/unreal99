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

    private readonly Rng _rng;
    private readonly List<int> _path = new(64);
    private readonly List<int> _navScratch = new(32);

    private BotState _state = BotState.Roam;
    private int _goalNode = -1;
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
    private float _jumpTimer;
    private Vector3 _lastPosition;
    private Vector3 _aimPoint;
    private Vector3 _aimVelocity;
    private float _aimYaw, _aimPitch;
    private int _targetId = -1;
    private Vector3 _lastKnownTargetPos;
    private float _lastSeenTargetTime = -999f;
    private PickupEntity _itemGoal;
    private float _threatTimer;
    private Vector3 _threatDirection;

    public BotController(uint seed, string name, float skill)
    {
        _rng = new Rng(seed == 0 ? 1u : seed);
        DisplayName = name;
        Skill = MathX.Clamp(skill, 0f, 1f);
    }

    // Skill-derived tuning.
    private float ReactionTime => MathX.Lerp(0.62f, 0.09f, Skill);
    private float AimError => MathX.Lerp(0.115f, 0.007f, Skill * Skill);
    private float AimSpeed => MathX.Lerp(5.5f, 22f, Skill);
    private float SightRange => MathX.Lerp(38f, 110f, Skill);
    private float LeadAccuracy => MathX.Lerp(0.15f, 1.0f, Skill);
    private float DodgeChance => MathX.Lerp(0.10f, 0.85f, Skill);
    private float StrafeAmount => MathX.Lerp(0.35f, 1.0f, Skill);

    public override void OnSpawned(GameWorld world)
    {
        _state = BotState.Roam;
        _goalNode = -1;
        _path.Clear();
        _targetId = -1;
        _aimYaw = Pawn.Yaw;
        _aimPitch = 0f;
        _lastPosition = Pawn.Position;
        _repathTimer = 0f;
        _itemGoal = null;
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
        var input = new PawnInput { WeaponSelect = -1 };
        var pawn = Pawn;
        if (!pawn.Alive) return input;

        TickTimers(dt);

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
        if (targetVisible && _reactionTimer <= 0f && target != null)
            DecideFire(world, target, ref input);

        // --- avoid falling into hazards while roaming ---
        AvoidLedges(world, ref input);

        return input;
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
        _jumpTimer -= dt;
        _threatTimer = MathF.Max(0f, _threatTimer - dt);
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

        if (best != null) { _targetId = best.Id; return; }

        // Nothing visible: keep hunting the last known position for a while.
        if (world.Time - _lastSeenTargetTime > 5f) _targetId = -1;
    }

    private void UpdateState(GameWorld world, Pawn target, bool visible)
    {
        float healthFraction = (Pawn.Health + Pawn.Armor * 0.6f) / 160f;

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
        var def = Pawn.WeaponDef;
        float range = Vector3.Distance(Pawn.Position, target.Position);

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

        if (target != null && (visible || world.Time - _lastSeenTargetTime < 1.6f))
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
                        aimAt += MathX.Up * (0.5f * Physics.Gravity * travel * travel * LeadAccuracy);
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
        if (nav.NodeCount == 0) return Vector2.Zero;

        // --- choose a goal ---
        if (_goalTimer <= 0f || _goalNode < 0 || (_path.Count > 0 && _pathCursor >= _path.Count))
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
            if (start >= 0 && nav.FindPath(start, _goalNode, _path)) _pathCursor = 0;
            else { _path.Clear(); _pathCursor = 0; }
        }

        // --- follow the path ---
        Vector3 steer = Vector3.Zero;
        if (_path.Count > 0 && _pathCursor < _path.Count)
        {
            Vector3 node = nav.Nodes[_path[_pathCursor]].Position;
            Vector3 flat = (node - Pawn.Position).FlatXZ();
            float dist = flat.Length();
            float heightDelta = node.Y - Pawn.Position.Y;

            if (dist < 1.25f && MathF.Abs(heightDelta) < 2.2f)
            {
                _pathCursor++;
                if (_pathCursor < _path.Count) node = nav.Nodes[_path[_pathCursor]].Position;
            }
            steer = MathX.SafeNormalize(flat, Vector3.Zero);

            // Jump when the next waypoint is meaningfully above us or the link needs it.
            if (heightDelta > 0.65f && dist < 3.2f && Pawn.OnGround && _jumpTimer <= 0f)
            {
                input.Jump = true;
                _jumpTimer = 0.5f;
            }
        }

        // --- combat strafing ---
        Vector3 strafe = Vector3.Zero;
        if (visible && target != null && _state == BotState.Attack)
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
        else if (_state == BotState.Retreat && target != null)
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
                if (_stuckTimer > 1.6f)
                {
                    _stuckTimer = 0f;
                    _goalNode = -1;
                    _goalTimer = 0f;
                    _path.Clear();
                }
            }
        }
        else _stuckTimer = MathF.Max(0f, _stuckTimer - dt);

        if (steer == Vector3.Zero) return Vector2.Zero;

        // Convert world steering into local move axes.
        Vector3 dir = MathX.SafeNormalize(steer, Pawn.ForwardFlat);
        float forwardAmount = Vector3.Dot(dir, Pawn.ForwardFlat);
        float rightAmount = Vector3.Dot(dir, Pawn.RightFlat);
        return new Vector2(rightAmount, forwardAmount);
    }

    private void ChooseGoal(GameWorld world, Pawn target, bool visible)
    {
        var nav = world.Level.Nav;

        // CTF objectives always win.
        if (world.Mode.Kind == GameModeKind.CaptureTheFlag && Pawn.Team != Team.None)
        {
            Team enemy = Pawn.Team == Team.Red ? Team.Blue : Team.Red;
            if (Pawn.HasFlag && world.FlagHome.TryGetValue(Pawn.Team, out Vector3 home))
            {
                _goalNode = nav.FindNearest(home);
                if (_goalNode >= 0) return;
            }
            if (!Pawn.HasFlag && world.FlagPosition.TryGetValue(enemy, out Vector3 enemyFlag))
            {
                bool taken = world.FlagCarrier.TryGetValue(enemy, out int carrier) && carrier >= 0;
                if (!taken && _rng.Chance(0.7f))
                {
                    _goalNode = nav.FindNearest(enemyFlag);
                    if (_goalNode >= 0) return;
                }
            }
            // Defend if our own flag has been taken.
            if (world.FlagCarrier.TryGetValue(Pawn.Team, out int ourCarrier) && ourCarrier >= 0)
            {
                var thief = world.FindPawn(ourCarrier);
                if (thief != null)
                {
                    _goalNode = nav.FindNearest(thief.Position);
                    if (_goalNode >= 0) return;
                }
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
                    PickupEntity bestItem = null;
                    float bestScore = 0.18f;
                    foreach (var item in world.Pickups)
                    {
                        if (!item.Active) continue;
                        float desire = item.DesireFor(Pawn);
                        if (desire <= 0.05f) continue;
                        float dist = Vector3.Distance(Pawn.Position, item.Position);
                        if (dist > 65f) continue;
                        float score = desire * 40f / MathF.Max(dist, 3f);
                        if (score > bestScore) { bestScore = score; bestItem = item; }
                    }
                    _itemGoal = bestItem;
                    _goalNode = bestItem != null
                        ? nav.FindNearest(bestItem.Position)
                        : nav.RandomNode(_rng, NavFlags.NearPickup);
                    break;
                }

            default:
                _goalNode = nav.RandomNode(_rng, _rng.Chance(0.35f) ? NavFlags.NearPickup : NavFlags.None);
                break;
        }

        if (_goalNode < 0) _goalNode = nav.RandomNode(_rng);
    }

    /// <summary>Refuses to walk off a ledge into the void or lava while not actively fighting.</summary>
    private void AvoidLedges(GameWorld world, ref PawnInput input)
    {
        if (input.Move == Vector2.Zero || !Pawn.OnGround) return;

        Vector3 dir = Pawn.ForwardFlat * input.Move.Y + Pawn.RightFlat * input.Move.X;
        dir = MathX.SafeNormalize(dir, Vector3.Zero);
        if (dir == Vector3.Zero) return;

        Vector3 probe = Pawn.Position + dir * 1.35f + new Vector3(0, 0.35f, 0);
        var hit = world.Level.Collision.Raycast(probe, probe - new Vector3(0, 5.5f, 0));

        bool danger = !hit.Hit || hit.Kind == BrushKind.Lava;
        if (!danger) return;

        // Jump the gap when there is ground on the other side, otherwise back off.
        Vector3 far = Pawn.Position + dir * 4.2f + new Vector3(0, 0.35f, 0);
        var farHit = world.Level.Collision.Raycast(far, far - new Vector3(0, 4.5f, 0));
        if (farHit.Hit && farHit.Kind != BrushKind.Lava && _jumpTimer <= 0f)
        {
            input.Jump = true;
            _jumpTimer = 0.6f;
            return;
        }
        input.Move = -input.Move * 0.6f;
        _goalNode = -1;
        _goalTimer = 0f;
    }
}
