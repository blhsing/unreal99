using System.Numerics;
using Unreal99.Core;
using Unreal99.World;

namespace Unreal99.Game;

/// <summary>What just happened to the ball, so the caller can score it and tell the player.</summary>
public enum BallEvent { None, PickedUp, Dropped, Returned, RunGoal, ThrowGoal }

/// <summary>
/// Bombing Run. One ball spawns at midfield and each team has a hoop to defend; you score by
/// putting the ball through the other side's hoop.
///
/// Two things make the mode play unlike Capture the Flag, and both are rules rather than
/// geometry. First, carrying the ball in costs you your guns — the carrier holds the Ball
/// Launcher and nothing else, so a lone runner cannot shoot their way through and the mode turns
/// into passing. Second, the two ways of scoring are not worth the same: running it in is seven
/// points and throwing it in is three, which is what makes a contested hoop worth defending
/// rather than conceding for field position.
/// </summary>
public sealed class BombingRunState
{
    /// <summary>Carried through the hoop. The original's reward for getting a body in there.</summary>
    public const int RunGoalScore = 7;
    /// <summary>Thrown or shot through it from outside. Safer, and worth less than half.</summary>
    public const int ThrowGoalScore = 3;

    /// <summary>Midfield spawn. The ball comes back here after a goal or after lying idle.</summary>
    public Vector3 Home;
    /// <summary>Where the ball is while nobody is holding it.</summary>
    public Vector3 Position;
    public Vector3 Velocity;

    /// <summary>Pawn id of the carrier, or −1 when the ball is loose.</summary>
    public int Carrier = -1;
    /// <summary>Last side to hold or throw it, so a throw-goal can be credited after release.</summary>
    public Team LastTouch = Team.None;
    public int LastTouchPawn = -1;

    /// <summary>The last deliberate thrower cannot catch their own pass for one second.</summary>
    public int LastThrowerPawn = -1;
    public float ThrowerPickupDelay;

    /// <summary>Seconds the ball has lain untouched, counting towards an automatic return.</summary>
    public float LooseTimer;
    /// <summary>True while it is still travelling from a throw, so it can score in flight.</summary>
    public bool InFlight;

    /// <summary>
    /// After a goal the original pauses for eleven seconds, then clears the field and restarts
    /// everyone from team starts with the ball at midfield.
    /// </summary>
    public float ResetRemaining;

    /// <summary>Each team's own hoop — the one they defend. You score in the other one.</summary>
    public readonly Dictionary<Team, Vector3> Goals = new();

    /// <summary>An untouched ball returns to midfield rather than staying lost in a pit.</summary>
    public const float ReturnSeconds = 25f;
    public const float ThrowerTouchDelay = 1f;
    public const float RoundResetSeconds = 11f;
    public const float CarrierHealPerSecond = 5f;
    /// <summary>How close counts as through the hoop.</summary>
    public const float GoalRadius = 2.6f;
    /// <summary>How close a pawn has to get to pick a loose ball up.</summary>
    public const float PickupRadius = 1.7f;

    public bool Held => Carrier >= 0;
    public bool RoundResetActive => ResetRemaining > 0f;

    public void Reset(Level level)
    {
        Goals.Clear();
        foreach (var hoop in level.GoalHoops) Goals[hoop.Team] = hoop.Position;
        Home = level.BallSpawn;
        ReturnToMidfield();
    }

    public void ReturnToMidfield()
    {
        Position = Home;
        Velocity = Vector3.Zero;
        Carrier = -1;
        LastTouch = Team.None;
        LastTouchPawn = -1;
        LastThrowerPawn = -1;
        ThrowerPickupDelay = 0f;
        LooseTimer = 0f;
        InFlight = false;
        ResetRemaining = 0f;
    }

    public void BeginRoundReset()
    {
        Position = Home;
        Velocity = Vector3.Zero;
        Carrier = -1;
        LastTouch = Team.None;
        LastTouchPawn = -1;
        LastThrowerPawn = -1;
        ThrowerPickupDelay = 0f;
        LooseTimer = 0f;
        InFlight = false;
        ResetRemaining = RoundResetSeconds;
    }

    /// <summary>The hoop <paramref name="team"/> is trying to score in — the other side's.</summary>
    public Vector3 TargetGoal(Team team)
    {
        Team enemy = team == Team.Red ? Team.Blue : Team.Red;
        return Goals.TryGetValue(enemy, out var p) ? p : Home;
    }

    /// <summary>The hoop <paramref name="team"/> has to keep the ball out of.</summary>
    public Vector3 OwnGoal(Team team) => Goals.TryGetValue(team, out var p) ? p : Home;

    /// <summary>
    /// Tests the ball's current position against both hoops. A goal only counts for the side that
    /// last touched it, which is what stops a defender's clearance from scoring on themselves.
    /// </summary>
    public BallEvent CheckGoal(out Team scorer, out int scorerPawn)
    {
        scorer = Team.None;
        scorerPawn = -1;
        if (LastTouch == Team.None) return BallEvent.None;

        Vector3 target = TargetGoal(LastTouch);
        if (Vector3.Distance(Position, target) > GoalRadius) return BallEvent.None;

        scorer = LastTouch;
        scorerPawn = LastTouchPawn;
        // Carried in is worth more than thrown in. That difference is the mode.
        return Held ? BallEvent.RunGoal : BallEvent.ThrowGoal;
    }

    public int ScoreFor(BallEvent e) => e switch
    {
        BallEvent.RunGoal => RunGoalScore,
        BallEvent.ThrowGoal => ThrowGoalScore,
        _ => 0,
    };
}
