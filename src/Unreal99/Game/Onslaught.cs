using System.Numerics;
using Unreal99.Core;
using Unreal99.UI;

namespace Unreal99.Game;

/// <summary>
/// Onslaught's node graph. This is the whole mode: a network of power nodes joined by links,
/// with a core at each end. What makes it Onslaught rather than "Domination with more points"
/// is the reachability rule — a node can only be touched if it already connects back to
/// something you hold, so the front line advances one link at a time and cannot be leapfrogged.
/// </summary>
public sealed class PowerNode
{
    /// <summary>Cores are nodes too: destroying one ends the match, and they cannot change hands.</summary>
    public bool IsCore;
    public Vector3 Position;
    public string Name = "";
    /// <summary>Fixed for a core; None for a neutral node until somebody builds it.</summary>
    public Team Team = Team.None;
    public float Health;
    public float MaxHealth;
    /// <summary>0 = neutral or destroyed, 1 = fully built and scoring.</summary>
    public float Built;
    /// <summary>Who is currently building it, so a contested node does not flicker.</summary>
    public Team BuildingFor = Team.None;
    public int[] Links = [];

    public bool IsActive => IsCore || (Team != Team.None && Built >= 1f);
}

public sealed class OnslaughtState
{
    public const float CoreHealth = 5000f;
    public const float NodeHealth = 2000f;
    /// <summary>Seconds of uncontested link-gun fire to raise a neutral node from nothing.</summary>
    public const float BuildSeconds = 6f;

    public readonly List<PowerNode> Nodes = new();
    public int RedCore = -1;
    public int BlueCore = -1;

    public PowerNode CoreOf(Team t)
    {
        int i = t == Team.Red ? RedCore : BlueCore;
        return i >= 0 && i < Nodes.Count ? Nodes[i] : null;
    }

    /// <summary>
    /// The link-chain rule. A node is only attackable or capturable by a team if at least one of
    /// its links leads to something that team already owns and has active. Without this the mode
    /// collapses into a race to the enemy core.
    /// </summary>
    public bool IsReachable(int index, Team by)
    {
        if (index < 0 || index >= Nodes.Count || by == Team.None) return false;
        foreach (int link in Nodes[index].Links)
        {
            if (link < 0 || link >= Nodes.Count) continue;
            var neighbour = Nodes[link];
            if (neighbour.Team == by && neighbour.IsActive) return true;
        }
        return false;
    }

    /// <summary>A core can only be hurt while an enemy holds a node wired directly into it.</summary>
    public bool CoreVulnerable(Team owner)
    {
        var core = CoreOf(owner);
        if (core == null) return false;
        Team enemy = owner == Team.Red ? Team.Blue : Team.Red;
        foreach (int link in core.Links)
        {
            if (link < 0 || link >= Nodes.Count) continue;
            var n = Nodes[link];
            if (n.Team == enemy && n.IsActive) return true;
        }
        return false;
    }

    public int NodesHeldBy(Team t)
    {
        int n = 0;
        foreach (var node in Nodes) if (!node.IsCore && node.Team == t && node.IsActive) n++;
        return n;
    }

    /// <summary>
    /// The node a team should push next: reachable, not already theirs, and nearest to the pawn
    /// asking. Bots use this so they advance along the chain instead of running at the core.
    /// </summary>
    public int NextObjectiveFor(Team team, Vector3 from)
    {
        int best = -1;
        float bestScore = float.MaxValue;
        for (int i = 0; i < Nodes.Count; i++)
        {
            var n = Nodes[i];
            if (n.IsCore)
            {
                // The enemy core is only worth approaching once it is actually vulnerable.
                if (n.Team == team || !CoreVulnerable(n.Team)) continue;
            }
            else
            {
                if (n.Team == team && n.IsActive) continue;
                if (!IsReachable(i, team)) continue;
            }
            float d = Vector3.Distance(from, n.Position);
            // Prefer neutral ground to a defended node at the same distance: it is cheaper.
            if (n.Team != Team.None && n.Team != team) d *= 1.35f;
            if (d < bestScore) { bestScore = d; best = i; }
        }
        return best;
    }

    /// <summary>A node of ours that an enemy could hit next, for bots told to defend.</summary>
    public int MostThreatenedFriendly(Team team, Vector3 from)
    {
        Team enemy = team == Team.Red ? Team.Blue : Team.Red;
        int best = -1;
        float bestScore = float.MaxValue;
        for (int i = 0; i < Nodes.Count; i++)
        {
            var n = Nodes[i];
            if (n.Team != team) continue;
            if (!IsReachable(i, enemy)) continue;   // safe behind the line; nothing to guard
            float d = Vector3.Distance(from, n.Position);
            if (n.IsCore) d *= 0.6f;                // the core matters more than any node
            if (d < bestScore) { bestScore = d; best = i; }
        }
        return best;
    }

    public void Reset(World.Level level)
    {
        Nodes.Clear();
        RedCore = BlueCore = -1;
        foreach (var def in level.PowerNodes)
        {
            var node = new PowerNode
            {
                IsCore = def.IsCore,
                Position = def.Position,
                Name = def.Name,
                Team = def.IsCore ? def.Team : Team.None,
                MaxHealth = def.IsCore ? CoreHealth : NodeHealth,
                Links = def.Links,
            };
            node.Health = node.MaxHealth;
            node.Built = def.IsCore ? 1f : 0f;
            Nodes.Add(node);
            if (def.IsCore && def.Team == Team.Red) RedCore = Nodes.Count - 1;
            if (def.IsCore && def.Team == Team.Blue) BlueCore = Nodes.Count - 1;
        }
    }

    /// <summary>
    /// Builds or attacks whichever node a pawn is standing at. Neutral ground is built up,
    /// enemy nodes are torn down first and only then rebuilt — you cannot simply overwrite
    /// someone else's node by walking onto it, which is what separates this from Domination.
    /// </summary>
    public NodeEvent Touch(int index, Team by, float dt, out PowerNode node)
    {
        node = null;
        if (index < 0 || index >= Nodes.Count || by == Team.None) return NodeEvent.None;
        node = Nodes[index];

        if (node.IsCore)
        {
            if (node.Team == by || !CoreVulnerable(node.Team)) return NodeEvent.None;
            node.Health -= 260f * dt;
            if (node.Health <= 0f) { node.Health = 0f; return NodeEvent.CoreDestroyed; }
            return NodeEvent.Damaged;
        }

        if (!IsReachable(index, by)) return NodeEvent.Blocked;

        if (node.Team != Team.None && node.Team != by)
        {
            node.Health -= 420f * dt;
            if (node.Health > 0f) return NodeEvent.Damaged;
            // Torn down to neutral rather than captured outright. Clearing BuildingFor matters:
            // a stale value would put the next team to walk on into the contested branch and
            // leave them unable to build the node they just took down.
            node.Team = Team.None;
            node.Built = 0f;
            node.BuildingFor = Team.None;
            node.Health = node.MaxHealth;
            return NodeEvent.Neutralised;
        }

        if (node.Team == by && node.Built >= 1f) return NodeEvent.None;

        // Contested neutral ground is a tug of war, not a shared effort. Without this both sides
        // pour progress into the same bar and it completes twice as fast for whichever team's
        // frame happens to cross the line first.
        if (node.Built > 0f && node.BuildingFor != Team.None && node.BuildingFor != by)
        {
            node.Built = MathF.Max(0f, node.Built - dt / BuildSeconds);
            if (node.Built <= 0f) node.BuildingFor = Team.None;
            return NodeEvent.Building;
        }

        node.BuildingFor = by;
        node.Built += dt / BuildSeconds;
        if (node.Built < 1f) return NodeEvent.Building;
        node.Built = 1f;
        node.Team = by;
        node.BuildingFor = Team.None;
        node.Health = node.MaxHealth;
        return NodeEvent.Captured;
    }

    /// <summary>
    /// Weapon damage against a node or core, from anywhere. Shelling a node from across the map
    /// with a Goliath or SPMA is the intended way to break a front line, so this deliberately has
    /// no range condition — but it obeys the same reachability rule as <see cref="Touch"/>, so
    /// artillery still cannot skip ahead of the chain.
    /// </summary>
    public NodeEvent Hurt(int index, Team by, float amount, out PowerNode node)
    {
        node = null;
        if (index < 0 || index >= Nodes.Count || by == Team.None || amount <= 0f) return NodeEvent.None;
        node = Nodes[index];
        if (node.Team == by) return NodeEvent.None;

        if (node.IsCore)
        {
            if (!CoreVulnerable(node.Team)) return NodeEvent.None;
            node.Health -= amount;
            if (node.Health <= 0f) { node.Health = 0f; return NodeEvent.CoreDestroyed; }
            return NodeEvent.Damaged;
        }

        // A node still being raised is soft: knocking it back down costs nothing but the shot.
        if (node.Team == Team.None)
        {
            if (node.Built <= 0f) return NodeEvent.None;
            if (node.BuildingFor == by) return NodeEvent.None;   // do not shoot down our own work
            node.Built = MathF.Max(0f, node.Built - amount / NodeHealth);
            if (node.Built <= 0f) node.BuildingFor = Team.None;
            return NodeEvent.Damaged;
        }

        if (!IsReachable(index, by)) return NodeEvent.Blocked;
        node.Health -= amount;
        if (node.Health > 0f) return NodeEvent.Damaged;
        node.Team = Team.None;
        node.Built = 0f;
        node.BuildingFor = Team.None;
        node.Health = node.MaxHealth;
        return NodeEvent.Neutralised;
    }

    /// <summary>The node nearest a blast, for splash damage that has no explicit target.</summary>
    public int NearestWithin(Vector3 point, float radius)
    {
        int best = -1;
        float bestD = radius;
        for (int i = 0; i < Nodes.Count; i++)
        {
            float d = Vector3.Distance(point, Nodes[i].Position);
            if (d < bestD) { bestD = d; best = i; }
        }
        return best;
    }
}

public enum NodeEvent { None, Blocked, Building, Captured, Damaged, Neutralised, CoreDestroyed }
