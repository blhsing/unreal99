using System.Numerics;
using Unreal99.Core;
using Unreal99.World;

namespace Unreal99.Game;

/// <summary>
/// One live Assault objective. The definition comes from the map; everything mutable — how much
/// of the panel is left, how far the charge has been planted — lives here so a round swap can
/// reset it without rebuilding the level.
/// </summary>
public sealed class AssaultObjective
{
    public Vector3 Position;
    public string Name = "";
    public ObjectiveKind Kind;
    public float Radius;
    public float Health;
    public float MaxHealth;
    public float HoldSeconds;
    public float HoldProgress;
    public int UnlocksSpawnGroup;
    public bool Completed;

    /// <summary>0–1 for the HUD, whichever way this objective happens to be completed.</summary>
    public float Progress => Kind switch
    {
        ObjectiveKind.Destroy => MaxHealth > 0f ? 1f - MathX.Saturate(Health / MaxHealth) : 0f,
        ObjectiveKind.Hold => HoldSeconds > 0f ? MathX.Saturate(HoldProgress / HoldSeconds) : 0f,
        _ => Completed ? 1f : 0f,
    };
}

public enum ObjectiveEvent { None, Progress, Completed, AllCompleted }

/// <summary>
/// Assault. One team attacks a fixed sequence of objectives while the other defends; then the
/// sides swap and the new attackers have to beat the first round's time. Everything that makes
/// this mode distinct is in the round bookkeeping, not the shooting: the sequence, the forward
/// spawns that open as it advances, and the time comparison at the end.
/// </summary>
public sealed class AssaultState
{
    public readonly List<AssaultObjective> Objectives = new();

    /// <summary>Which side is attacking right now.</summary>
    public Team Attackers = Team.Red;
    /// <summary>The side that attacked in round one, kept so the swap is unambiguous.</summary>
    public Team FirstAttackers = Team.Red;
    public int Round = 1;

    /// <summary>Seconds the round-one attackers took. <see cref="float.MaxValue"/> if they failed.</summary>
    public float TargetTime = float.MaxValue;
    /// <summary>How many objectives round one reached, for the tie-break when nobody finished.</summary>
    public int TargetObjectives;
    /// <summary>Elapsed attack time in the current round.</summary>
    public float Elapsed;

    /// <summary>Highest spawn group the attackers have unlocked, so they push forward as they win.</summary>
    public int SpawnGroup;

    public int Current
    {
        get
        {
            for (int i = 0; i < Objectives.Count; i++) if (!Objectives[i].Completed) return i;
            return -1;
        }
    }

    public AssaultObjective CurrentObjective
    {
        get { int i = Current; return i >= 0 ? Objectives[i] : null; }
    }

    public int CompletedCount
    {
        get { int n = 0; foreach (var o in Objectives) if (o.Completed) n++; return n; }
    }

    public bool AllComplete => Objectives.Count > 0 && Current < 0;

    public Team Defenders => Attackers == Team.Red ? Team.Blue : Team.Red;

    public void Reset(Level level)
    {
        Objectives.Clear();
        foreach (var def in level.Objectives)
            Objectives.Add(new AssaultObjective
            {
                Position = def.Position,
                Name = def.Name,
                Kind = def.Kind,
                Radius = def.Radius <= 0f ? 3.4f : def.Radius,
                Health = def.Health,
                MaxHealth = def.Health,
                HoldSeconds = def.HoldSeconds,
                UnlocksSpawnGroup = def.UnlocksSpawnGroup,
            });

        FirstAttackers = level.AssaultAttackers == Team.None ? Team.Red : level.AssaultAttackers;
        Attackers = FirstAttackers;
        Round = 1;
        TargetTime = float.MaxValue;
        TargetObjectives = 0;
        Elapsed = 0f;
        SpawnGroup = 0;
    }

    /// <summary>
    /// Ends round one and turns it around. The attackers' result becomes the target the new
    /// attackers must beat — a time if they finished, otherwise the count they reached.
    /// </summary>
    public void SwapSides(bool attackersFinished)
    {
        TargetTime = attackersFinished ? Elapsed : float.MaxValue;
        TargetObjectives = CompletedCount;
        Round = 2;
        Attackers = Defenders;
        Elapsed = 0f;
        SpawnGroup = 0;
        foreach (var o in Objectives)
        {
            o.Completed = false;
            o.Health = o.MaxHealth;
            o.HoldProgress = 0f;
        }
    }

    /// <summary>
    /// Who won after round two. Faster wins; if neither side finished, whoever got further wins;
    /// a dead-even result is a draw.
    /// </summary>
    public Team ResolveWinner(bool secondRoundFinished)
    {
        Team second = Attackers;
        Team first = FirstAttackers;
        if (secondRoundFinished)
        {
            // Strictly faster: matching the target exactly leaves the record standing.
            return Elapsed < TargetTime ? second : first;
        }
        if (TargetTime < float.MaxValue) return first;
        int reached = CompletedCount;
        if (reached > TargetObjectives) return second;
        if (reached < TargetObjectives) return first;
        return Team.None;
    }

    /// <summary>
    /// Progresses the current objective for a pawn standing at it. Only the current one responds:
    /// you cannot work ahead, which is what forces the push to move as a front rather than
    /// scatter across the map.
    /// </summary>
    public ObjectiveEvent Touch(Team by, Vector3 position, float dt, out AssaultObjective objective)
    {
        objective = null;
        int index = Current;
        if (index < 0 || by != Attackers) return ObjectiveEvent.None;

        var o = Objectives[index];
        objective = o;
        if (Vector3.Distance(position, o.Position) > o.Radius) return ObjectiveEvent.None;
        if (o.Kind == ObjectiveKind.Destroy) return ObjectiveEvent.None;   // shot, not stood on

        if (o.Kind == ObjectiveKind.Touch) return Complete(o);

        // Hold: progress exists only while an attacker remains in range. Defenders stop it by
        // killing or displacing that attacker; merely sharing the radius is not an objective
        // rule and must not turn a surviving attacker's interaction off.
        o.HoldProgress += dt;
        if (o.HoldProgress < o.HoldSeconds) return ObjectiveEvent.Progress;
        return Complete(o);
    }

    /// <summary>Weapon damage against a destroy-type objective, from any range.</summary>
    public ObjectiveEvent Hurt(Team by, float amount, out AssaultObjective objective)
    {
        objective = null;
        int index = Current;
        if (index < 0 || by != Attackers || amount <= 0f) return ObjectiveEvent.None;
        var o = Objectives[index];
        if (o.Kind != ObjectiveKind.Destroy) return ObjectiveEvent.None;
        objective = o;
        o.Health -= amount;
        if (o.Health > 0f) return ObjectiveEvent.Progress;
        o.Health = 0f;
        return Complete(o);
    }

    private ObjectiveEvent Complete(AssaultObjective o)
    {
        o.Completed = true;
        SpawnGroup = Math.Max(SpawnGroup, o.UnlocksSpawnGroup);
        return AllComplete ? ObjectiveEvent.AllCompleted : ObjectiveEvent.Completed;
    }
}
