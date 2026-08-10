using System.Numerics;
using Unreal99.Core;
using Unreal99.World;

namespace Unreal99.Game;

/// <summary>
/// The Warfare orb — the one mechanic that separates Warfare from Onslaught. Carrying it onto a
/// node takes that node instantly and at full health, and standing beside a node you already own
/// makes it untouchable. Onslaught stalls once a team loses its prime node, because the chain rule
/// leaves nothing left to attack; the orb is the answer to that, so it deliberately ignores the
/// chain entirely.
/// </summary>
public sealed class WarfareOrb
{
    /// <summary>Seconds a dropped orb waits for a team-mate before it gives up and respawns.</summary>
    public const float DropTimeout = 18f;
    /// <summary>How close the carrier has to stay for the shield to hold.</summary>
    public const float ShieldRadius = 11f;
    /// <summary>Pickup and capture reach. Generous, because the carrier is usually moving fast.</summary>
    public const float TouchRadius = 3.2f;
    /// <summary>What forcing an enemy orb to respawn costs the player who does it.</summary>
    public const float SacrificeHealth = 100f;

    public Team Team;
    public Vector3 Position;
    /// <summary>Pawn carrying it, or -1 when it is sitting on the ground or at home.</summary>
    public int CarrierId = -1;
    /// <summary>Counts down only while dropped; negative when carried or at a spawn point.</summary>
    public float DropTimer = -1f;
    public bool Dropped => CarrierId < 0 && DropTimer >= 0f;
    public bool Held => CarrierId >= 0;

    public void ResetTo(Vector3 home)
    {
        Position = home;
        CarrierId = -1;
        DropTimer = -1f;
    }
}

/// <summary>
/// Warfare's per-match state: two orbs and the bookkeeping for auxiliary node payouts. The node
/// graph itself is shared with Onslaught — see <see cref="OnslaughtState"/> — because the link
/// network is identical and only the rules layered on top differ.
/// </summary>
public sealed class WarfareState
{
    public readonly WarfareOrb RedOrb = new() { Team = Team.Red };
    public readonly WarfareOrb BlueOrb = new() { Team = Team.Blue };

    /// <summary>Vehicles delivered by vehicle nodes, so a second one is not granted while the first lives.</summary>
    public readonly Dictionary<int, int> NodeVehicles = new();

    public WarfareOrb OrbOf(Team t) => t == Team.Red ? RedOrb : t == Team.Blue ? BlueOrb : null;

    public IEnumerable<WarfareOrb> Orbs { get { yield return RedOrb; yield return BlueOrb; } }

    public void Reset(Level level, OnslaughtState nodes)
    {
        NodeVehicles.Clear();
        RedOrb.ResetTo(HomeFor(level, nodes, Team.Red));
        BlueOrb.ResetTo(HomeFor(level, nodes, Team.Blue));
    }

    /// <summary>
    /// Where a team's orb belongs right now: the furthest-forward live spawn point it owns, or the
    /// core if it has none. Forward spawns are the reason an orb run can be repeated quickly
    /// instead of starting from the back of the map every time.
    /// </summary>
    public Vector3 HomeFor(Level level, OnslaughtState nodes, Team team)
    {
        Vector3 fallback = nodes.CoreOf(team)?.Position ?? Vector3.Zero;
        Vector3 best = fallback;
        float bestScore = float.MinValue;
        Vector3 enemyCore = nodes.CoreOf(team == Team.Red ? Team.Blue : Team.Red)?.Position ?? fallback;
        foreach (var spawn in level.OrbSpawns)
        {
            if (spawn.Team != team) continue;
            if (spawn.NodeIndex >= 0)
            {
                if (spawn.NodeIndex >= nodes.Nodes.Count) continue;
                var node = nodes.Nodes[spawn.NodeIndex];
                if (node.Team != team || !node.IsActive) continue;
            }
            float score = -Vector3.Distance(spawn.Position, enemyCore);
            if (score > bestScore) { bestScore = score; best = spawn.Position; }
        }
        // Authored positions already sit at chest height on their pad; lifting again here would
        // float the orb out of a walking player's reach.
        return best;
    }
}
