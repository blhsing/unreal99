using System.Numerics;
using Unreal99.Core;
using Unreal99.UI;

namespace Unreal99.Game;

public enum MatchState { Warmup, InProgress, Overtime, Finished }

/// <summary>
/// Match rules for every supported mode. One class with mode-dependent behaviour rather than a
/// hierarchy, because the rule differences are small and mostly about scoring and win checks.
/// </summary>
public sealed class GameMode
{
    public GameModeKind Kind = GameModeKind.Deathmatch;
    public bool TeamBased;
    public float FriendlyFire = 0.25f;
    public float RespawnDelay = 1.7f;

    public int FragLimit = 20;
    public int CaptureLimit = 5;
    public int DominationLimit = 100;
    /// <summary>
    /// Seconds a team must hold a control point to bank one point for it. The original scores
    /// every held point once per five seconds, so holding two ticks twice as fast as holding one.
    /// </summary>
    public const float DominationTickSeconds = 5f;
    private readonly float[] _dominationTick = new float[2];
    public float TimeLimit = 600f;       // seconds; 0 = unlimited
    public int LivesPerPlayer = 3;       // last man standing

    public MatchState State = MatchState.Warmup;
    public float WarmupRemaining = 4f;
    public float TimeRemaining;
    public float PostMatchTimer;
    public bool FirstBloodPending = true;

    public readonly int[] TeamScores = new int[2];
    public readonly Dictionary<int, int> LivesLeft = new();

    public Pawn Winner;
    public Team WinningTeam = Team.None;

    private int _lastLeaderId = -1;
    private float _announceTimer;
    private int _lastCountdownSecond = -1;

    public static GameMode Create(GameModeKind kind, int fragLimit, float timeLimitMinutes, int captureLimit)
    {
        var mode = new GameMode
        {
            Kind = kind,
            FragLimit = fragLimit,
            CaptureLimit = captureLimit,
            TimeLimit = timeLimitMinutes * 60f,
            TeamBased = kind is GameModeKind.TeamDeathmatch or GameModeKind.CaptureTheFlag
                or GameModeKind.Domination,
        };
        mode.TimeRemaining = mode.TimeLimit;
        // Domination scores in ticks rather than kills, so it needs its own limit: the stock
        // maps run to 100-125, which takes a few minutes of holding two of three points.
        if (kind == GameModeKind.Domination) mode.DominationLimit = 100;
        if (kind == GameModeKind.LastManStanding) mode.RespawnDelay = 2.4f;
        if (kind == GameModeKind.Instagib) mode.RespawnDelay = 1.2f;
        return mode;
    }

    public bool IsOver => State == MatchState.Finished;

    /// <summary>Score used for ranking: frags, team frags, or captures depending on the mode.</summary>
    public int ScoreOf(Pawn p) => Kind switch
    {
        GameModeKind.CaptureTheFlag => p.Frags + p.Captures * 7 + p.FlagCarrierKills * 3,
        // Captures here are control points touched. Weighted well above a frag so the
        // scoreboard rewards the player who actually runs the points rather than the one who
        // camped a corridor with a minigun.
        GameModeKind.Domination => p.Frags + p.Captures * 5,
        _ => p.Frags,
    };

    public int LimitValue => Kind switch
    {
        GameModeKind.CaptureTheFlag => CaptureLimit,
        GameModeKind.Domination => DominationLimit,
        _ => FragLimit,
    };

    public int TeamScore(Team t) => t == Team.None ? 0 : TeamScores[(int)t];

    // ---------------------------------------------------------------- lifecycle

    public void Update(GameWorld world, float dt)
    {
        switch (State)
        {
            case MatchState.Warmup:
                {
                    WarmupRemaining -= dt;
                    int second = (int)MathF.Ceiling(WarmupRemaining);
                    if (second != _lastCountdownSecond && second is >= 1 and <= 3)
                    {
                        _lastCountdownSecond = second;
                        string text = second switch
                        {
                            3 => Loc.AnnCountdown3,
                            2 => Loc.AnnCountdown2,
                            _ => Loc.AnnCountdown1,
                        };
                        world.Broadcast(text, new Vector3(1f, 0.85f, 0.3f), 0.9f);
                        world.OnSound?.Invoke(SoundId.MenuMove, Vector3.Zero, 0.8f);
                    }
                    if (WarmupRemaining <= 0f)
                    {
                        State = MatchState.InProgress;
                        world.Broadcast(Loc.AnnMatchStart, new Vector3(0.4f, 1f, 0.5f), 1.5f);
                        world.OnSound?.Invoke(SoundId.AnnounceMajor, Vector3.Zero, 1.2f);
                        foreach (var p in world.Pawns) LivesLeft.TryAdd(p.Id, LivesPerPlayer);
                    }
                    break;
                }

            case MatchState.InProgress:
            case MatchState.Overtime:
                {
                    if (TimeLimit > 0f && State == MatchState.InProgress)
                    {
                        TimeRemaining -= dt;
                        if (TimeRemaining <= 0f)
                        {
                            TimeRemaining = 0f;
                            if (IsTied(world))
                            {
                                State = MatchState.Overtime;
                                world.Broadcast(Loc.AnnOvertime, new Vector3(1f, 0.4f, 0.2f), 2.5f);
                            }
                            else Finish(world);
                        }
                    }
                    if (Kind == GameModeKind.Domination) TickDomination(world, dt);

                    _announceTimer -= dt;
                    if (_announceTimer <= 0f)
                    {
                        _announceTimer = 0.5f;
                        CheckLeadChange(world);
                        CheckNearLimit(world);
                    }
                    break;
                }

            case MatchState.Finished:
                PostMatchTimer += dt;
                break;
        }
    }

    /// <summary>
    /// Banks a point for every control point a team holds, once per tick. Each point keeps its
    /// own share of the clock through a single per-team accumulator: holding two points fills it
    /// twice as fast, which is the whole economy of the mode.
    /// </summary>
    private void TickDomination(GameWorld world, float dt)
    {
        for (int t = 0; t < 2; t++)
        {
            int held = world.ControlPointsHeldBy((Team)t);
            if (held <= 0) continue;
            _dominationTick[t] += dt * held;
            while (_dominationTick[t] >= DominationTickSeconds)
            {
                _dominationTick[t] -= DominationTickSeconds;
                TeamScores[t]++;
                if (DominationLimit > 0 && TeamScores[t] >= DominationLimit)
                {
                    WinningTeam = (Team)t;
                    Finish(world);
                    return;
                }
            }
        }
    }

    private bool IsTied(GameWorld world)
    {
        if (TeamBased) return TeamScores[0] == TeamScores[1];
        var ranked = Ranking(world);
        return ranked.Count >= 2 && ScoreOf(ranked[0]) == ScoreOf(ranked[1]);
    }

    private void CheckLeadChange(GameWorld world)
    {
        if (TeamBased) return;
        var ranked = Ranking(world);
        if (ranked.Count == 0) return;
        Pawn leader = ranked[0];
        if (leader.Id == _lastLeaderId) return;

        Pawn previous = world.FindPawn(_lastLeaderId);
        _lastLeaderId = leader.Id;
        if (leader.PlayerIndex >= 0)
            world.FeedbackFor(leader).Big(Loc.AnnTakenLead, new Vector3(0.4f, 1f, 0.6f), 1.8f);
        if (previous != null && previous.PlayerIndex >= 0 && previous != leader)
            world.FeedbackFor(previous).Big(Loc.AnnLostLead, new Vector3(1f, 0.5f, 0.3f), 1.8f);
    }

    private void CheckNearLimit(GameWorld world)
    {
        if (LimitValue <= 0) return;
        int best = TeamBased ? Math.Max(TeamScores[0], TeamScores[1]) : 0;
        if (!TeamBased)
        {
            foreach (var p in world.Pawns) best = Math.Max(best, ScoreOf(p));
        }
        int remaining = LimitValue - best;
        if (remaining is <= 0 or > 3) return;

        // Announce the last three only once each.
        string text = remaining switch
        {
            3 => Loc.AnnThreeFrags,
            2 => Loc.AnnTwoFrags,
            _ => Loc.AnnOneFrag,
        };
        if (_lastAnnouncedRemaining == remaining) return;
        _lastAnnouncedRemaining = remaining;
        world.Broadcast(text, new Vector3(1f, 0.7f, 0.2f), 1.6f);
        world.OnSound?.Invoke(SoundId.AnnounceMajor, Vector3.Zero, 0.9f);
    }

    private int _lastAnnouncedRemaining = -1;

    public void OnFrag(GameWorld world, Pawn killer, Pawn victim)
    {
        if (State is MatchState.Warmup or MatchState.Finished) return;
        // CTF team score is captures only; ordinary kills contribute to personal score but must
        // never advance the capture limit.
        if (Kind == GameModeKind.TeamDeathmatch && killer.Team != Team.None)
            TeamScores[(int)killer.Team]++;
        CheckWinCondition(world);
    }

    public void OnDeath(GameWorld world, Pawn victim, Pawn killer)
    {
        if (Kind != GameModeKind.LastManStanding) return;
        if (!LivesLeft.ContainsKey(victim.Id)) LivesLeft[victim.Id] = LivesPerPlayer;
        LivesLeft[victim.Id] = Math.Max(0, LivesLeft[victim.Id] - 1);
        if (LivesLeft[victim.Id] <= 0 && victim.PlayerIndex >= 0)
            world.FeedbackFor(victim).Big(Loc.HudSpectating, new Vector3(1f, 0.4f, 0.3f), 3f);
        CheckWinCondition(world);
    }

    public void OnCapture(GameWorld world, Pawn scorer)
    {
        if (State is MatchState.Warmup or MatchState.Finished) return;
        if (scorer.Team != Team.None) TeamScores[(int)scorer.Team]++;
        CheckWinCondition(world);
    }

    public void OnPawnUpdate(GameWorld world, Pawn pawn, float dt) { }

    public bool AllowsRespawn(GameWorld world, Pawn pawn)
    {
        if (State == MatchState.Finished) return false;
        if (Kind != GameModeKind.LastManStanding) return true;
        return LivesLeft.TryGetValue(pawn.Id, out int lives) && lives > 0;
    }

    public int LivesFor(Pawn pawn)
        => Kind == GameModeKind.LastManStanding && LivesLeft.TryGetValue(pawn.Id, out int l) ? l : -1;

    private void CheckWinCondition(GameWorld world)
    {
        if (State == MatchState.Finished) return;

        if (Kind == GameModeKind.LastManStanding)
        {
            var alive = world.Pawns.Where(p => LivesFor(p) > 0 || p.Alive).ToList();
            if (alive.Count <= 1)
            {
                Winner = alive.FirstOrDefault();
                Finish(world);
            }
            return;
        }

        if (TeamBased)
        {
            // A tied timed match enters sudden-death overtime. The first subsequent team score
            // breaks the tie and ends the match even when it is below the configured limit.
            if (State == MatchState.Overtime && TeamScores[0] != TeamScores[1])
            {
                WinningTeam = TeamScores[0] > TeamScores[1] ? Team.Red : Team.Blue;
                Finish(world);
                return;
            }

            for (int t = 0; t < 2; t++)
            {
                if (LimitValue > 0 && TeamScores[t] >= LimitValue)
                {
                    WinningTeam = (Team)t;
                    Finish(world);
                    return;
                }
            }
            return;
        }

        foreach (var p in world.Pawns)
        {
            if (LimitValue > 0 && ScoreOf(p) >= LimitValue)
            {
                Winner = p;
                Finish(world);
                return;
            }
        }
    }

    public void Finish(GameWorld world)
    {
        if (State == MatchState.Finished) return;
        State = MatchState.Finished;
        PostMatchTimer = 0f;

        if (TeamBased && WinningTeam == Team.None)
            WinningTeam = TeamScores[0] > TeamScores[1] ? Team.Red
                        : TeamScores[1] > TeamScores[0] ? Team.Blue : Team.None;

        if (!TeamBased && Winner == null)
        {
            var ranked = Ranking(world);
            Winner = ranked.FirstOrDefault();
        }

        string text = TeamBased
            ? WinningTeam switch
            {
                Team.Red => Loc.ResultRedWins,
                Team.Blue => Loc.ResultBlueWins,
                _ => Loc.ResultDraw,
            }
            : Loc.AnnMatchOver;
        world.Broadcast(text, new Vector3(1f, 0.85f, 0.35f), 5f);
        world.OnSound?.Invoke(SoundId.AnnounceMajor, Vector3.Zero, 1.4f);
    }

    /// <summary>Players ordered for the scoreboard: score first, then fewer deaths.</summary>
    public List<Pawn> Ranking(GameWorld world)
    {
        var list = new List<Pawn>(world.Pawns);
        list.Sort((a, b) =>
        {
            int cmp = ScoreOf(b).CompareTo(ScoreOf(a));
            if (cmp != 0) return cmp;
            cmp = a.Deaths.CompareTo(b.Deaths);
            if (cmp != 0) return cmp;
            return string.CompareOrdinal(a.Name, b.Name);
        });
        return list;
    }

    public string ResultTextFor(GameWorld world, Pawn viewer)
    {
        if (TeamBased)
        {
            if (WinningTeam == Team.None) return Loc.ResultDraw;
            return viewer.Team == WinningTeam ? Loc.ResultVictory : Loc.ResultDefeat;
        }
        if (Winner == null) return Loc.ResultDraw;
        return Winner == viewer ? Loc.ResultVictory : Loc.ResultDefeat;
    }
}
