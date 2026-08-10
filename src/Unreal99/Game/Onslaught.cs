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
    /// <summary>The player who activated the pad, retained while automatic construction runs.</summary>
    public int BuilderPawnId = -1;
    public int[] Links = [];

    public bool IsActive => IsCore || (Team != Team.None && Built >= 1f);
}

public sealed class OnslaughtState
{
    public const int GoalScore = 3;
    public const float CoreHealth = 5000f;
    public const float NodeHealth = 2000f;
    public const float OvertimeCoreDrainPerSecond = 20f;
    /// <summary>Seconds for an activated neutral pad to construct itself without link support.</summary>
    public const float BuildSeconds = 6f;
    public const float LinkBuildEnergy = 700f;

    public readonly List<PowerNode> Nodes = new();
    public int RedCore = -1;
    public int BlueCore = -1;
    public bool SidesSwapped { get; private set; }

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
        SidesSwapped = false;
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
    /// Starts another scored round. The original swaps physical sides after a reset; changing
    /// core ownership is sufficient here because respawns and vehicle pads follow the nearest
    /// live node instead of a permanently coloured half of the level.
    /// </summary>
    public void ResetRound(bool swapSides)
    {
        if (swapSides) SidesSwapped = !SidesSwapped;
        RedCore = BlueCore = -1;
        for (int i = 0; i < Nodes.Count; i++)
        {
            PowerNode node = Nodes[i];
            if (node.IsCore)
            {
                if (swapSides) node.Team = node.Team == Team.Red ? Team.Blue : Team.Red;
                node.Built = 1f;
                if (node.Team == Team.Red) RedCore = i;
                else if (node.Team == Team.Blue) BlueCore = i;
            }
            else
            {
                node.Team = Team.None;
                node.Built = 0f;
                node.BuildingFor = Team.None;
                node.BuilderPawnId = -1;
            }
            node.Health = node.MaxHealth;
        }
    }

    /// <summary>
    /// Activates a reachable neutral pad. Construction then continues by itself; touching an
    /// enemy node never damages it, because enemy infrastructure must be destroyed with weapons.
    /// </summary>
    public NodeEvent Touch(int index, Team by, int pawnId, out PowerNode node)
    {
        node = null;
        if (index < 0 || index >= Nodes.Count || by == Team.None) return NodeEvent.None;
        node = Nodes[index];
        if (node.IsCore) return NodeEvent.None;
        if (!IsReachable(index, by)) return NodeEvent.Blocked;
        if (node.Team != Team.None) return NodeEvent.None;
        if (node.BuildingFor != Team.None && node.BuildingFor != by) return NodeEvent.Blocked;
        if (node.BuildingFor == Team.None)
        {
            node.BuildingFor = by;
            node.BuilderPawnId = pawnId;
            node.Built = MathF.Max(node.Built, 0.001f);
        }
        return NodeEvent.Building;
    }

    /// <summary>Advances one activated node's autonomous construction clock.</summary>
    public NodeEvent TickConstruction(int index, float dt, out PowerNode node, out int builderPawnId)
    {
        node = null;
        builderPawnId = -1;
        if (index < 0 || index >= Nodes.Count || dt <= 0f) return NodeEvent.None;
        node = Nodes[index];
        if (node.IsCore || node.Team != Team.None || node.BuildingFor == Team.None) return NodeEvent.None;
        if (!IsReachable(index, node.BuildingFor)) return NodeEvent.None;
        node.Built = MathF.Min(1f, node.Built + dt / BuildSeconds);
        if (node.Built < 1f) return NodeEvent.Building;
        node.Team = node.BuildingFor;
        node.BuildingFor = Team.None;
        node.Health = node.MaxHealth;
        builderPawnId = node.BuilderPawnId;
        node.BuilderPawnId = -1;
        return NodeEvent.Captured;
    }

    /// <summary>Pulse-beam support accelerates construction and repairs nodes, but never cores.</summary>
    public NodeEvent Support(int index, Team by, int pawnId, float energy, out PowerNode node)
    {
        node = null;
        if (index < 0 || index >= Nodes.Count || by == Team.None || energy <= 0f) return NodeEvent.None;
        node = Nodes[index];
        if (node.IsCore) return NodeEvent.None;
        if (node.Team == by && node.IsActive)
        {
            float before = node.Health;
            node.Health = MathF.Min(node.MaxHealth, node.Health + energy);
            return node.Health > before ? NodeEvent.Repaired : NodeEvent.None;
        }
        if (node.Team != Team.None || !IsReachable(index, by)) return NodeEvent.None;
        if (node.BuildingFor != Team.None && node.BuildingFor != by) return NodeEvent.None;
        if (node.BuildingFor == Team.None)
        {
            node.BuildingFor = by;
            node.BuilderPawnId = pawnId;
            node.Built = MathF.Max(node.Built, 0.001f);
        }
        node.Built = MathF.Min(0.999f, node.Built + energy / LinkBuildEnergy);
        return NodeEvent.Building;
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
            if (node.Built <= 0f)
            {
                node.BuildingFor = Team.None;
                node.BuilderPawnId = -1;
            }
            return NodeEvent.Damaged;
        }

        if (!IsReachable(index, by)) return NodeEvent.Blocked;
        node.Health -= amount;
        if (node.Health > 0f) return NodeEvent.Damaged;
        node.Team = Team.None;
        node.Built = 0f;
        node.BuildingFor = Team.None;
        node.BuilderPawnId = -1;
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

public enum NodeEvent { None, Blocked, Building, Captured, Damaged, Repaired, Neutralised, CoreDestroyed }
