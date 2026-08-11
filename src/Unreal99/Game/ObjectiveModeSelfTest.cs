using System.Numerics;
using Unreal99.World;

namespace Unreal99.Game;

/// <summary>Headless invariants for the two multi-stage objective modes.</summary>
public static class ObjectiveModeSelfTest
{
    public static int Run()
    {
        var failures = new List<string>();
        Check(OnslaughtState.GoalScore == 3, "Onslaught goal score", failures);
        Check(OnslaughtState.OvertimeCoreDrainPerSecond == 20f, "Onslaught core drain", failures);

        var ons = new OnslaughtState();
        ons.Nodes.Add(new PowerNode
        {
            IsCore = true, Team = Team.Red, Health = OnslaughtState.CoreHealth,
            MaxHealth = OnslaughtState.CoreHealth, Built = 1f, Links = [1],
        });
        ons.Nodes.Add(new PowerNode
        {
            Team = Team.None, Health = OnslaughtState.NodeHealth,
            MaxHealth = OnslaughtState.NodeHealth, Links = [0, 2],
        });
        ons.Nodes.Add(new PowerNode
        {
            IsCore = true, Team = Team.Blue, Health = OnslaughtState.CoreHealth,
            MaxHealth = OnslaughtState.CoreHealth, Built = 1f, Links = [1],
        });
        ons.RedCore = 0;
        ons.BlueCore = 2;

        NodeEvent touch = ons.Touch(1, Team.Red, 42, out PowerNode node);
        Check(touch == NodeEvent.Building && node.BuildingFor == Team.Red && node.Built < 0.01f,
            "touch activates rather than captures/damages", failures);
        NodeEvent built = ons.TickConstruction(1, OnslaughtState.BuildSeconds, out node, out int builder);
        Check(built == NodeEvent.Captured && node.Team == Team.Red && builder == 42,
            "automatic node construction", failures);
        node.Health = 1000f;
        NodeEvent repaired = ons.Support(1, Team.Red, 42, 120f, out node);
        Check(repaired == NodeEvent.Repaired && MathF.Abs(node.Health - 1120f) < 0.01f,
            "beam repairs friendly node", failures);
        float coreBefore = ons.Nodes[0].Health;
        Check(ons.Support(0, Team.Red, 42, 500f, out _) == NodeEvent.None
              && ons.Nodes[0].Health == coreBefore, "core cannot be repaired", failures);
        Check(ons.Touch(1, Team.Blue, 7, out _) == NodeEvent.None,
            "standing on enemy node does not capture or damage it", failures);
        NodeEvent neutralised = ons.Hurt(1, Team.Blue, OnslaughtState.NodeHealth, out node);
        Check(neutralised == NodeEvent.Neutralised && node.Team == Team.None && node.Built == 0f,
            "reachable enemy node must be destroyed back to neutral", failures);
        Team originalRedCoreTeam = ons.Nodes[ons.RedCore].Team;
        ons.ResetRound(swapSides: true);
        Check(ons.Nodes[ons.RedCore].Team == Team.Red
              && ons.Nodes[ons.BlueCore].Team == Team.Blue
              && ons.Nodes[0].Team != originalRedCoreTeam,
            "Onslaught round swaps physical core sides", failures);

        // ---------------------------------------------------------------- warfare
        var war = new OnslaughtState { Warfare = true };
        war.Nodes.Add(new PowerNode
        {
            IsCore = true, Team = Team.Red, Health = OnslaughtState.CoreHealth,
            MaxHealth = OnslaughtState.CoreHealth, Built = 1f, Links = [1],
        });
        war.Nodes.Add(new PowerNode
        {
            Team = Team.None, Health = OnslaughtState.NodeHealth,
            MaxHealth = OnslaughtState.NodeHealth, Links = [0, 2], IsPrime = true,
        });
        war.Nodes.Add(new PowerNode
        {
            IsCore = true, Team = Team.Blue, Health = OnslaughtState.CoreHealth,
            MaxHealth = OnslaughtState.CoreHealth, Built = 1f, Links = [1],
        });
        // A support node with no links at all: unreachable under Onslaught rules, always reachable
        // under Warfare's, which is the entire point of the auxiliary node.
        war.Nodes.Add(new PowerNode
        {
            Team = Team.None, Health = OnslaughtState.NodeHealth,
            MaxHealth = OnslaughtState.NodeHealth, Links = [], Role = NodeRole.Support,
        });
        war.RedCore = 0;
        war.BlueCore = 2;

        Check(war.IsReachable(3, Team.Blue), "warfare support node needs no link", failures);
        war.Warfare = false;
        Check(!war.IsReachable(3, Team.Blue), "onslaught keeps the link rule for every node", failures);
        war.Warfare = true;

        Check(war.OrbCapture(1, Team.Blue, 9, out PowerNode orbNode)
              && orbNode.Team == Team.Blue && orbNode.Built >= 1f
              && orbNode.Health == orbNode.MaxHealth,
            "orb captures a node instantly and at full health", failures);
        orbNode.OrbShield = Team.Blue;
        Check(war.Hurt(1, Team.Red, 500f, out _) == NodeEvent.Blocked
              && orbNode.Health == orbNode.MaxHealth,
            "an orb-shielded node cannot be damaged", failures);
        Check(!war.OrbCapture(1, Team.Red, 3, out _),
            "an orb-shielded node cannot be flipped by the other orb", failures);
        orbNode.OrbShield = Team.None;

        // Red's own prime is Blue's now, and Red has lost every link to it — under Onslaught that
        // would be unattackable forever. Warfare keeps a prime node permanently in reach.
        Check(war.IsReachable(1, Team.Red), "an enemy prime node is never shielded", failures);

        var countdown = new OnslaughtState { Warfare = true };
        countdown.Nodes.Add(new PowerNode
        {
            Team = Team.Red, Built = 1f, Health = 10f, MaxHealth = 10f,
            Role = NodeRole.Countdown, CountdownSeconds = 4f,
        });
        Check(countdown.TickCountdowns(1f) == null && countdown.Nodes[0].CountdownRemaining == 4f,
            "capturing a countdown node arms its clock", failures);
        Check(countdown.TickCountdowns(3f) == null, "countdown does not fire early", failures);
        Check(countdown.TickCountdowns(1.5f) == countdown.Nodes[0], "countdown fires at zero", failures);
        countdown.Nodes[0].Team = Team.None;
        countdown.Nodes[0].Built = 0f;
        countdown.TickCountdowns(0.1f);
        Check(countdown.Nodes[0].CountdownRemaining < 0f,
            "losing a countdown node discards its progress", failures);

        var orb = new WarfareOrb { Team = Team.Red };
        orb.ResetTo(new Vector3(5f, 0f, 0f));
        Check(!orb.Held && !orb.Dropped, "a returned orb is neither held nor dropped", failures);
        orb.CarrierId = 4;
        Check(orb.Held, "orb tracks its carrier", failures);
        orb.CarrierId = -1;
        orb.DropTimer = WarfareOrb.DropTimeout;
        Check(orb.Dropped && WarfareOrb.DropTimeout == 18f,
            "a dropped orb runs the original's 18-second timer", failures);

        // Bombing Run. The two scoring values and the last-touch rule are the whole mode: get
        // either wrong and a defender clearing their own ring scores for the attackers.
        var br = new BombingRunState { Home = Vector3.Zero };
        br.Goals[Team.Red] = new Vector3(-50f, 0f, 0f);
        br.Goals[Team.Blue] = new Vector3(50f, 0f, 0f);
        br.ReturnToMidfield();
        Check(BombingRunState.RunGoalScore == 7 && BombingRunState.ThrowGoalScore == 3,
            "carried goals are worth seven and thrown goals three", failures);
        Check(br.TargetGoal(Team.Red) == br.Goals[Team.Blue]
              && br.OwnGoal(Team.Red) == br.Goals[Team.Red],
            "a team scores in the hoop it does not defend", failures);
        Check(br.CheckGoal(out _, out _) == BallEvent.None,
            "an untouched ball at midfield scores nothing", failures);

        br.Position = br.Goals[Team.Blue];
        Check(br.CheckGoal(out _, out _) == BallEvent.None,
            "a ball in a hoop scores nothing until somebody has touched it", failures);
        br.LastTouch = Team.Blue;
        br.LastTouchPawn = 3;
        Check(br.CheckGoal(out _, out _) == BallEvent.None,
            "a defender cannot score in their own hoop", failures);

        br.LastTouch = Team.Red;
        br.LastTouchPawn = 8;
        var thrown = br.CheckGoal(out Team scorer, out int scorerPawn);
        Check(thrown == BallEvent.ThrowGoal && scorer == Team.Red && scorerPawn == 8
              && br.ScoreFor(thrown) == 3,
            "a loose ball through the enemy hoop is a three-point throw", failures);
        br.Carrier = 8;
        var run = br.CheckGoal(out _, out _);
        Check(run == BallEvent.RunGoal && br.ScoreFor(run) == 7,
            "carrying it through the same hoop is worth seven", failures);

        br.ReturnToMidfield();
        Check(!br.Held && br.LastTouch == Team.None && br.Position == br.Home,
            "a returned ball is unheld, unowned and back at midfield", failures);
        Check(BombingRunState.ReturnSeconds == 25f,
            "an abandoned ball returns after the original twenty-five seconds", failures);
        Check(BombingRunState.ThrowerTouchDelay == 1f
              && BombingRunState.CarrierHealPerSecond == 5f,
            "thrower pickup lockout and carrier regeneration match the original", failures);
        br.BeginRoundReset();
        Check(br.RoundResetActive && br.ResetRemaining == BombingRunState.RoundResetSeconds
              && BombingRunState.RoundResetSeconds == 11f && !br.Held,
            "a goal starts the original eleven-second field reset", failures);

        var assault = new AssaultState
        {
            Attackers = Team.Red,
            FirstAttackers = Team.Red,
            Elapsed = 48f,
        };
        assault.Objectives.Add(new AssaultObjective { Completed = true });
        assault.Objectives.Add(new AssaultObjective());
        assault.SwapSides(attackersFinished: false);
        assault.Objectives[0].Completed = true;
        assault.Objectives[1].Completed = true;
        Check(assault.ResolveWinner(secondRoundFinished: false) == Team.Blue,
            "second attackers win on surpassing failed objective count", failures);

        var timed = new AssaultState
        {
            Attackers = Team.Red,
            FirstAttackers = Team.Red,
            Elapsed = 70f,
        };
        timed.Objectives.Add(new AssaultObjective { Completed = true });
        timed.SwapSides(attackersFinished: true);
        timed.Elapsed = 69f;
        Check(timed.ResolveWinner(secondRoundFinished: true) == Team.Blue,
            "strictly faster second attack wins", failures);
        timed.Elapsed = 70f;
        Check(timed.ResolveWinner(secondRoundFinished: true) == Team.Red,
            "equal time leaves first result standing", failures);

        var ordered = new AssaultState { Attackers = Team.Red, FirstAttackers = Team.Red };
        ordered.Objectives.Add(new AssaultObjective
        {
            Position = Vector3.Zero, Kind = ObjectiveKind.Hold,
            Radius = 2f, HoldSeconds = 2f,
        });
        ordered.Objectives.Add(new AssaultObjective
        {
            Position = new Vector3(10f, 0f, 0f), Kind = ObjectiveKind.Touch, Radius = 2f,
        });
        Check(ordered.Touch(Team.Red, new Vector3(10f, 0f, 0f), 1f, out _)
              == ObjectiveEvent.None, "Assault objectives cannot be completed out of order", failures);
        Check(ordered.Touch(Team.Blue, Vector3.Zero, 2f, out _) == ObjectiveEvent.None
              && ordered.Objectives[0].HoldProgress == 0f,
            "defenders cannot advance an attacker objective", failures);
        Check(ordered.Touch(Team.Red, new Vector3(3f, 0f, 0f), 2f, out _) == ObjectiveEvent.None
              && ordered.Objectives[0].HoldProgress == 0f,
            "hold progress pauses while no attacker is in range", failures);
        Check(ordered.Touch(Team.Red, Vector3.Zero, 2f, out _)
              == ObjectiveEvent.Completed && ordered.SpawnGroup == 0,
            "an attacker in range completes a hold objective", failures);

        var tiedProgress = new AssaultState
        {
            Attackers = Team.Red,
            FirstAttackers = Team.Red,
            Elapsed = 90f,
        };
        tiedProgress.Objectives.Add(new AssaultObjective { Completed = true });
        tiedProgress.Objectives.Add(new AssaultObjective());
        tiedProgress.SwapSides(attackersFinished: false);
        tiedProgress.Objectives[0].Completed = true;
        Check(tiedProgress.ResolveWinner(secondRoundFinished: false) == Team.None,
            "equal partial progress is an Assault draw", failures);
        tiedProgress.Objectives[0].Completed = false;
        Check(tiedProgress.ResolveWinner(secondRoundFinished: false) == Team.Red,
            "first attackers win when second attackers make less partial progress", failures);

        var failedReply = new AssaultState
        {
            Attackers = Team.Red,
            FirstAttackers = Team.Red,
            Elapsed = 55f,
        };
        failedReply.Objectives.Add(new AssaultObjective { Completed = true });
        failedReply.SwapSides(attackersFinished: true);
        Check(failedReply.ResolveWinner(secondRoundFinished: false) == Team.Red,
            "first attackers win when their completed run is not answered", failures);

        var resetRound = new AssaultState
        {
            Attackers = Team.Red,
            FirstAttackers = Team.Red,
            Elapsed = 42f,
            SpawnGroup = 3,
        };
        resetRound.Objectives.Add(new AssaultObjective
        {
            Kind = ObjectiveKind.Destroy, Completed = true, Health = 0f, MaxHealth = 100f,
        });
        resetRound.Objectives.Add(new AssaultObjective
        {
            Kind = ObjectiveKind.Hold, HoldProgress = 2f, HoldSeconds = 4f,
        });
        resetRound.SwapSides(attackersFinished: true);
        Check(resetRound.Attackers == Team.Blue && resetRound.Round == 2
              && resetRound.SpawnGroup == 0 && resetRound.Elapsed == 0f
              && !resetRound.Objectives[0].Completed && resetRound.Objectives[0].Health == 100f
              && resetRound.Objectives[1].HoldProgress == 0f,
            "side swap restores objectives, progress, clock, and attacker spawn group", failures);

        foreach (string failure in failures) Console.Error.WriteLine($"MODE_RULE_TEST FAIL: {failure}");
        Console.WriteLine(failures.Count == 0
            ? "MODE_RULE_TEST PASS"
            : $"MODE_RULE_TEST FAIL ({failures.Count})");
        return failures.Count == 0 ? 0 : 2;
    }

    private static void Check(bool condition, string label, List<string> failures)
    {
        if (!condition) failures.Add(label);
    }
}
