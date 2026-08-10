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
        Check(ordered.Touch(Team.Red, new Vector3(10f, 0f, 0f), false, 1f, out _)
              == ObjectiveEvent.None, "Assault objectives cannot be completed out of order", failures);
        ordered.Touch(Team.Red, Vector3.Zero, true, 2f, out AssaultObjective held);
        Check(held != null && held.HoldProgress == 0f,
            "defender contests a hold objective without reversing it", failures);
        Check(ordered.Touch(Team.Red, Vector3.Zero, false, 2f, out _)
              == ObjectiveEvent.Completed && ordered.SpawnGroup == 0,
            "uncontested hold objective completes", failures);

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
