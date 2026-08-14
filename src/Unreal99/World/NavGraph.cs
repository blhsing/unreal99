using System.Numerics;
using Unreal99.Core;

namespace Unreal99.World;

[Flags]
public enum NavFlags
{
    None = 0,
    NearPickup = 1 << 0,
    JumpPad = 1 << 1,
    Teleporter = 1 << 2,
    Sniper = 1 << 3,     // elevated spot with long sightlines
    Choke = 1 << 4,
}

public struct NavNode
{
    public Vector3 Position;
    public NavFlags Flags;
    public int FirstEdge;
    public int EdgeCount;
    /// <summary>How exposed this spot is; bots retreat toward lower values when hurt.</summary>
    public float Openness;
}

public struct NavEdge
{
    public int To;
    public float Cost;
    /// <summary>Requires a jump (or a jump pad) to traverse.</summary>
    public bool Jump;
}

/// <summary>
/// Waypoint graph auto-generated from the collision world, plus A* over it.
/// Bots plan on this graph and steer locally, which keeps them moving naturally
/// through arenas that were never hand-annotated.
/// </summary>
public sealed class NavGraph
{
    public NavNode[] Nodes = [];
    public NavEdge[] Edges = [];

    private readonly Dictionary<long, List<int>> _spatial = new();
    private const float CellSize = 6f;

    // A* working set, reused between queries so pathfinding allocates nothing per call.
    private float[] _gScore = [];
    private float[] _fScore = [];
    private int[] _cameFrom = [];
    private int[] _openHeap = [];
    private int[] _heapIndex = [];
    private bool[] _closed = [];
    private int _heapCount;
    private int _searchStamp;
    private int[] _stamp = [];

    public int NodeCount => Nodes.Length;

    // ---------------------------------------------------------------- generation

    /// <summary>
    /// Samples a horizontal grid over the level, drops a ray at each cell to find every floor
    /// level (so multi-storey arenas get nodes on each deck), then links neighbouring nodes
    /// that a pawn could actually walk or step between.
    /// </summary>
    public void Generate(CollisionWorld world, float spacing = 2.0f, float pawnHeight = 1.85f,
        float pawnRadius = 0.45f)
    {
        var nodes = new List<NavNode>(2048);
        Vector3 min = world.WorldMin, max = world.WorldMax;
        if (max.X <= min.X) return;

        var scratch = new List<int>(32);
        Vector3 half = new(pawnRadius, pawnHeight * 0.5f, pawnRadius);

        for (float x = min.X + spacing * 0.5f; x < max.X; x += spacing)
        {
            for (float z = min.Z + spacing * 0.5f; z < max.Z; z += spacing)
            {
                float scanY = max.Y + 1f;
                // Walk downward finding each successive floor in this column.
                for (int level = 0; level < 8 && scanY > min.Y - 1f; level++)
                {
                    var hit = world.Raycast(new Vector3(x, scanY, z), new Vector3(x, min.Y - 2f, z));
                    if (!hit.Hit) break;
                    scanY = hit.Point.Y - 0.35f;

                    if (hit.Kind == BrushKind.Lava) continue;
                    if (hit.Normal.Y < world.MaxWalkableY) continue;

                    Vector3 center = new(x, hit.Point.Y + pawnHeight * 0.5f + 0.03f, z);
                    if (world.BoxOverlapsSolid(center - half, center + half, scratch)) continue;
                    if (world.VolumeAt(center - half, center + half, scratch) == BrushKind.Void) continue;

                    nodes.Add(new NavNode
                    {
                        Position = new Vector3(x, hit.Point.Y + 0.05f, z),
                        Flags = NavFlags.None,
                        Openness = 0f,
                    });
                }
            }
        }

        Nodes = nodes.ToArray();
        BuildSpatialIndex();
        BuildEdges(world, spacing, pawnHeight, pawnRadius);
        ComputeOpenness(world);
        AllocateSearchBuffers();
    }

    private void BuildSpatialIndex()
    {
        _spatial.Clear();
        for (int i = 0; i < Nodes.Length; i++)
        {
            long k = Key(Nodes[i].Position);
            if (!_spatial.TryGetValue(k, out var list)) _spatial[k] = list = new List<int>(8);
            list.Add(i);
        }
    }

    private static long Key(Vector3 p)
        => ((long)(int)MathF.Floor(p.X / CellSize) << 32) ^ (uint)(int)MathF.Floor(p.Z / CellSize);

    private void BuildEdges(CollisionWorld world, float spacing, float pawnHeight, float pawnRadius)
    {
        var edges = new List<NavEdge>(Nodes.Length * 6);
        float linkRadius = spacing * 1.55f;
        float maxStep = world.StepHeight + 0.05f;
        // A 2.4m ceiling left 2.6m parapets as isolated navigation islands even though stepping
        // down from them lands well below the game's fall-damage threshold. Allow a little over
        // three metres: destinations still require a real walkable node and the edge is one-way,
        // so this connects safe ledge-to-floor drops without authoring routes into open voids.
        const float maxSafeDrop = 3.25f;
        float maxVertical = MathF.Max(maxSafeDrop, linkRadius * 0.95f);
        float candidateRadius = MathF.Sqrt(linkRadius * linkRadius + maxVertical * maxVertical);
        Vector3 half = new(pawnRadius, pawnHeight * 0.5f, pawnRadius);
        var scratch = new List<int>(32);
        var neighbours = new List<int>(16);

        for (int i = 0; i < Nodes.Length; i++)
        {
            var node = Nodes[i];
            int first = edges.Count;
            neighbours.Clear();
            // QueryRadius measures full 3D distance. Querying with only the horizontal link
            // radius discarded legitimate ledge-to-floor neighbours before the separate dy and
            // floor-support checks below could validate them.
            QueryRadius(node.Position, candidateRadius, neighbours);

            foreach (int j in neighbours)
            {
                if (j == i) continue;
                Vector3 a = Nodes[i].Position, b = Nodes[j].Position;
                Vector3 flat = (b - a).FlatXZ();
                float horizontal = flat.Length();
                if (horizontal > linkRadius || horizontal < 1e-3f) continue;
                float dy = b.Y - a.Y;
                // A pair of grid samples on a walkable ramp can differ by more than StepHeight.
                // Permit that rise when it remains within the collision world's 45-degree
                // walkable-surface limit; the floor-continuity probe below rejects vertical
                // ledges. Downward edges may still take a safe one-way drop.
                float maxWalkableRise = MathF.Max(maxStep, horizontal * 0.95f);
                if (dy > maxWalkableRise || dy < -maxSafeDrop) continue;
                bool dropping = dy < -maxStep;
                Vector3 lateral = new(-flat.Z / horizontal, 0f, flat.X / horizontal);

                // Sample the span at torso height to make sure a pawn can actually pass.
                bool clear = true;
                int samples = Math.Max(2, (int)(horizontal / 0.5f));
                for (int s = 1; s < samples; s++)
                {
                    float t = s / (float)samples;
                    Vector3 p = Vector3.Lerp(a, b, t);
                    float walkY = p.Y;
                    // During a drop the pawn's torso crosses above the ledge before descending;
                    // interpolating straight through the ledge would reject every valid edge.
                    if (dropping) { p.Y = a.Y; walkY = a.Y; }
                    else
                    {
                        // A clear torso is not enough: without continuous floor support the
                        // graph links across pits, making bots run off edges. This also tells a
                        // genuine ramp from an impassable vertical lip.
                        float tolerance = maxStep + 0.18f;
                        float sampledFloorY = 0f;
                        // A centerline alone permits diagonal corner cuts: it can touch floor at
                        // the exact meeting point of two platforms while half the pawn crosses a
                        // pit or deep water. Require support under both sides of the capsule too.
                        for (int lane = -1; lane <= 1; lane++)
                        {
                            Vector3 support = p + lateral * (lane * pawnRadius * 0.9f);
                            var floorHit = world.Raycast(
                                support + new Vector3(0f, tolerance, 0f),
                                support - new Vector3(0f, tolerance, 0f));
                            if (!floorHit.Hit || floorHit.Normal.Y < world.MaxWalkableY
                                || MathF.Abs(floorHit.Point.Y - p.Y) > tolerance)
                            {
                                clear = false;
                                break;
                            }
                            if (lane == 0) sampledFloorY = floorHit.Point.Y;
                        }
                        if (!clear) break;
                        // Follow the sampled floor rather than the straight chord between grid
                        // nodes. At a ramp-to-platform seam that chord can sit just below the
                        // higher surface and falsely report the floor itself as a torso obstacle.
                        walkY = sampledFloorY + 0.05f;
                    }
                    Vector3 c = new(p.X, walkY + pawnHeight * 0.5f, p.Z);
                    if (world.BoxOverlapsSolid(c - half, c + half, scratch)) { clear = false; break; }
                }
                if (!clear) continue;

                edges.Add(new NavEdge { To = j, Cost = horizontal + MathF.Abs(dy) * 1.6f, Jump = false });
            }

            node.FirstEdge = first;
            node.EdgeCount = edges.Count - first;
            Nodes[i] = node;
        }

        Edges = edges.ToArray();
    }

    /// <summary>Openness = how much of the surrounding sky/space is visible; snipers like high values.</summary>
    private void ComputeOpenness(CollisionWorld world)
    {
        Span<Vector3> dirs = stackalloc Vector3[8];
        for (int d = 0; d < 8; d++)
        {
            float a = d / 8f * MathX.TwoPi;
            dirs[d] = new Vector3(MathF.Cos(a), 0.12f, MathF.Sin(a));
        }
        const float probe = 14f;
        for (int i = 0; i < Nodes.Length; i++)
        {
            Vector3 origin = Nodes[i].Position + new Vector3(0, 1.5f, 0);
            float sum = 0f;
            for (int d = 0; d < 8; d++)
            {
                var hit = world.Raycast(origin, origin + dirs[d] * probe);
                sum += hit.Hit ? hit.Distance / probe : 1f;
            }
            var n = Nodes[i];
            n.Openness = sum / 8f;
            if (n.Openness > 0.72f) n.Flags |= NavFlags.Sniper;
            if (n.Openness < 0.28f) n.Flags |= NavFlags.Choke;
            Nodes[i] = n;
        }
    }

    /// <summary>Adds a one-way traversal link, used for jump pads, lifts and teleporters.</summary>
    public void AddSpecialLink(Vector3 from, Vector3 to, NavFlags flagOnSource,
        bool discourageOrdinaryTraversal = false)
    {
        int a = FindNearest(from), b = FindNearest(to);
        if (a < 0 || b < 0 || a == b) return;

        var edgeList = new List<NavEdge>(Edges);
        // Rebuilding the edge array keeps the packed layout that A* iterates over.
        var rebuilt = new List<NavEdge>(Edges.Length + 1);
        for (int i = 0; i < Nodes.Length; i++)
        {
            var n = Nodes[i];
            int first = rebuilt.Count;
            for (int e = 0; e < n.EdgeCount; e++)
            {
                NavEdge edge = edgeList[n.FirstEdge + e];
                // A physical launch pad is hazardous floor unless this exact special edge is
                // useful. Keep ordinary graph connectivity as a fallback, but make A* prefer a
                // modest walk around its trigger. Lifts deliberately do not request this cost.
                if (discourageOrdinaryTraversal && !edge.Jump && (i == a || edge.To == a))
                    edge.Cost += 12f;
                rebuilt.Add(edge);
            }
            if (i == a)
            {
                rebuilt.Add(new NavEdge { To = b, Cost = Vector3.Distance(Nodes[a].Position, Nodes[b].Position) * 0.35f, Jump = true });
                n.Flags |= flagOnSource;
            }
            n.FirstEdge = first;
            n.EdgeCount = rebuilt.Count - first;
            Nodes[i] = n;
        }
        Edges = rebuilt.ToArray();
    }

    public void MarkFlag(Vector3 position, NavFlags flags, float radius = 2.5f)
    {
        var found = new List<int>(8);
        QueryRadius(position, radius, found);
        foreach (int i in found)
        {
            var n = Nodes[i];
            n.Flags |= flags;
            Nodes[i] = n;
        }
    }

    // ---------------------------------------------------------------- queries

    public void QueryRadius(Vector3 position, float radius, List<int> output)
    {
        int cx0 = (int)MathF.Floor((position.X - radius) / CellSize);
        int cx1 = (int)MathF.Floor((position.X + radius) / CellSize);
        int cz0 = (int)MathF.Floor((position.Z - radius) / CellSize);
        int cz1 = (int)MathF.Floor((position.Z + radius) / CellSize);
        float r2 = radius * radius;
        for (int cx = cx0; cx <= cx1; cx++)
            for (int cz = cz0; cz <= cz1; cz++)
            {
                long k = ((long)cx << 32) ^ (uint)cz;
                if (!_spatial.TryGetValue(k, out var list)) continue;
                foreach (int i in list)
                    if (Vector3.DistanceSquared(Nodes[i].Position, position) <= r2) output.Add(i);
            }
    }

    public int FindNearest(Vector3 position, float maxRadius = 12f)
    {
        var found = new List<int>(32);
        for (float r = 3f; r <= maxRadius; r *= 2f)
        {
            found.Clear();
            QueryRadius(position, r, found);
            if (found.Count == 0) continue;
            int best = -1;
            float bestD = float.MaxValue;
            foreach (int i in found)
            {
                // Prefer nodes at a similar height so a bot on a catwalk does not snap to the floor below.
                float d = Vector3.DistanceSquared(Nodes[i].Position, position)
                        + MathF.Abs(Nodes[i].Position.Y - position.Y) * 6f;
                if (d < bestD) { bestD = d; best = i; }
            }
            if (best >= 0) return best;
        }
        return -1;
    }

    private void AllocateSearchBuffers()
    {
        int n = Nodes.Length;
        _gScore = new float[n];
        _fScore = new float[n];
        _cameFrom = new int[n];
        _closed = new bool[n];
        _stamp = new int[n];
        _openHeap = new int[n + 1];
        _heapIndex = new int[n];
    }

    // ---------------------------------------------------------------- A*

    /// <summary>
    /// Finds a node path from start to goal. Returns false if unreachable.
    /// The output list is filled with node indices in travel order (excluding the start node).
    /// </summary>
    public bool FindPath(int start, int goal, List<int> outPath, int maxExpansions = 4000,
        Func<int, bool> canVisit = null, Func<int, int, bool> canTraverse = null)
    {
        outPath.Clear();
        if (start < 0 || goal < 0 || start >= Nodes.Length || goal >= Nodes.Length) return false;
        if (start == goal) return true;
        if (_gScore.Length != Nodes.Length) AllocateSearchBuffers();

        _searchStamp++;
        _heapCount = 0;

        Touch(start);
        _gScore[start] = 0f;
        _fScore[start] = Heuristic(start, goal);
        _cameFrom[start] = -1;
        HeapPush(start);

        int expansions = 0;
        while (_heapCount > 0 && expansions++ < maxExpansions)
        {
            int current = HeapPop();
            if (current == goal)
            {
                // Walk the parent chain back and reverse it.
                int c = goal;
                while (c != -1 && c != start)
                {
                    outPath.Add(c);
                    c = _cameFrom[c];
                }
                outPath.Reverse();
                return true;
            }
            _closed[current] = true;

            var node = Nodes[current];
            for (int e = 0; e < node.EdgeCount; e++)
            {
                var edge = Edges[node.FirstEdge + e];
                int next = edge.To;
                if (canVisit != null && !canVisit(next)) continue;
                if (canTraverse != null && !canTraverse(current, next)) continue;
                Touch(next);
                if (_closed[next]) continue;

                float tentative = _gScore[current] + edge.Cost;
                if (tentative >= _gScore[next]) continue;

                _cameFrom[next] = current;
                _gScore[next] = tentative;
                _fScore[next] = tentative + Heuristic(next, goal);
                if (_heapIndex[next] > 0) HeapSiftUp(_heapIndex[next]);
                else HeapPush(next);
            }
        }
        return false;
    }

    /// <summary>
    /// Paths to <paramref name="goal"/>, or—when that node belongs to a disconnected navigation
    /// island—to the reachable node closest to it. Objective-driven bots must keep advancing
    /// instead of standing still merely because a flag dais sampled onto the wrong nav island.
    /// </summary>
    public bool FindPathToward(int start, int goal, List<int> outPath, int maxExpansions = 4000,
        Func<int, bool> canVisit = null, Func<int, int, bool> canTraverse = null)
    {
        if (FindPath(start, goal, outPath, maxExpansions, canVisit, canTraverse)) return true;
        outPath.Clear();
        if (start < 0 || goal < 0 || start >= Nodes.Length || goal >= Nodes.Length) return false;
        if (_gScore.Length != Nodes.Length) AllocateSearchBuffers();

        _searchStamp++;
        int read = 0, write = 0;
        _openHeap[write++] = start;
        Touch(start);
        _cameFrom[start] = -1;

        int closest = start;
        float closestDistance = Vector3.DistanceSquared(Nodes[start].Position, Nodes[goal].Position);
        int expansions = 0;
        while (read < write && expansions++ < maxExpansions)
        {
            int current = _openHeap[read++];
            var node = Nodes[current];
            for (int e = 0; e < node.EdgeCount; e++)
            {
                int next = Edges[node.FirstEdge + e].To;
                if (canVisit != null && !canVisit(next)) continue;
                if (canTraverse != null && !canTraverse(current, next)) continue;
                if (_stamp[next] == _searchStamp) continue;
                Touch(next);
                _cameFrom[next] = current;
                _openHeap[write++] = next;

                float distance = Vector3.DistanceSquared(Nodes[next].Position, Nodes[goal].Position);
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closest = next;
                }
            }
        }

        if (closest == start) return false;
        for (int node = closest; node != -1 && node != start; node = _cameFrom[node])
            outPath.Add(node);
        outPath.Reverse();
        return outPath.Count > 0;
    }

    /// <summary>
    /// Builds a useful roaming path when a requested goal is on another navigation island.
    /// Choosing a distant node in the current directed component keeps bots exploring instead
    /// of repeatedly standing still while random goals or visible opponents remain unreachable.
    /// </summary>
    public bool FindPathToFarthestReachable(int start, List<int> outPath, int maxExpansions = 4000,
        Func<int, bool> canVisit = null, Func<int, int, bool> canTraverse = null)
    {
        outPath.Clear();
        if (start < 0 || start >= Nodes.Length) return false;
        if (_gScore.Length != Nodes.Length) AllocateSearchBuffers();

        _searchStamp++;
        int read = 0, write = 0;
        _openHeap[write++] = start;
        Touch(start);
        _cameFrom[start] = -1;

        int farthest = start;
        float farthestDistance = 0f;
        int expansions = 0;
        while (read < write && expansions++ < maxExpansions)
        {
            int current = _openHeap[read++];
            var node = Nodes[current];
            for (int e = 0; e < node.EdgeCount; e++)
            {
                int next = Edges[node.FirstEdge + e].To;
                if (canVisit != null && !canVisit(next)) continue;
                if (canTraverse != null && !canTraverse(current, next)) continue;
                if (_stamp[next] == _searchStamp) continue;
                Touch(next);
                _cameFrom[next] = current;
                _openHeap[write++] = next;

                Vector3 delta = Nodes[next].Position - Nodes[start].Position;
                float distance = delta.X * delta.X + delta.Z * delta.Z + delta.Y * delta.Y * 0.35f;
                if (distance > farthestDistance)
                {
                    farthestDistance = distance;
                    farthest = next;
                }
            }
        }

        if (farthest == start) return false;
        for (int node = farthest; node != -1 && node != start; node = _cameFrom[node])
            outPath.Add(node);
        outPath.Reverse();
        return outPath.Count > 0;
    }

    /// <summary>
    /// Breadth-first search of the start node's directed component, stopping at the first node
    /// accepted by the caller. Water escape uses this instead of measuring straight-line range:
    /// a dry floor across a harbour wall can be geometrically close and completely unreachable,
    /// while the real submerged ramp is farther away along the basin floor.
    /// </summary>
    public bool FindPathToNearestReachable(int start, List<int> outPath,
        Func<int, bool> accepts, int maxExpansions = 4000)
    {
        outPath.Clear();
        if (start < 0 || start >= Nodes.Length || accepts == null) return false;
        if (_gScore.Length != Nodes.Length) AllocateSearchBuffers();

        _searchStamp++;
        int read = 0, write = 0;
        _openHeap[write++] = start;
        Touch(start);
        _cameFrom[start] = -1;

        int found = -1;
        int expansions = 0;
        while (read < write && expansions++ < maxExpansions)
        {
            int current = _openHeap[read++];
            if (current != start && accepts(current))
            {
                found = current;
                break;
            }
            NavNode node = Nodes[current];
            for (int edgeIndex = 0; edgeIndex < node.EdgeCount; edgeIndex++)
            {
                int next = Edges[node.FirstEdge + edgeIndex].To;
                if (_stamp[next] == _searchStamp) continue;
                Touch(next);
                _cameFrom[next] = current;
                _openHeap[write++] = next;
            }
        }

        if (found < 0) return false;
        for (int node = found; node != -1 && node != start; node = _cameFrom[node])
            outPath.Add(node);
        outPath.Reverse();
        return outPath.Count > 0;
    }

    private void Touch(int i)
    {
        if (_stamp[i] == _searchStamp) return;
        _stamp[i] = _searchStamp;
        _gScore[i] = float.MaxValue;
        _fScore[i] = float.MaxValue;
        _cameFrom[i] = -1;
        _closed[i] = false;
        _heapIndex[i] = 0;
    }

    private float Heuristic(int a, int b) => Vector3.Distance(Nodes[a].Position, Nodes[b].Position);

    private void HeapPush(int node)
    {
        _heapCount++;
        _openHeap[_heapCount] = node;
        _heapIndex[node] = _heapCount;
        HeapSiftUp(_heapCount);
    }

    private int HeapPop()
    {
        int top = _openHeap[1];
        _heapIndex[top] = 0;
        _openHeap[1] = _openHeap[_heapCount];
        _heapCount--;
        if (_heapCount > 0)
        {
            _heapIndex[_openHeap[1]] = 1;
            HeapSiftDown(1);
        }
        return top;
    }

    private void HeapSiftUp(int i)
    {
        while (i > 1)
        {
            int parent = i >> 1;
            if (_fScore[_openHeap[parent]] <= _fScore[_openHeap[i]]) break;
            Swap(parent, i);
            i = parent;
        }
    }

    private void HeapSiftDown(int i)
    {
        while (true)
        {
            int l = i << 1, r = l + 1, best = i;
            if (l <= _heapCount && _fScore[_openHeap[l]] < _fScore[_openHeap[best]]) best = l;
            if (r <= _heapCount && _fScore[_openHeap[r]] < _fScore[_openHeap[best]]) best = r;
            if (best == i) break;
            Swap(best, i);
            i = best;
        }
    }

    private void Swap(int a, int b)
    {
        (_openHeap[a], _openHeap[b]) = (_openHeap[b], _openHeap[a]);
        _heapIndex[_openHeap[a]] = a;
        _heapIndex[_openHeap[b]] = b;
    }

    /// <summary>Picks a random node, optionally biased toward one matching <paramref name="preferred"/>.</summary>
    public int RandomNode(Rng rng, NavFlags preferred = NavFlags.None)
    {
        if (Nodes.Length == 0) return -1;
        if (preferred != NavFlags.None)
        {
            for (int attempt = 0; attempt < 24; attempt++)
            {
                int i = rng.Range(0, Nodes.Length);
                if ((Nodes[i].Flags & preferred) != 0) return i;
            }
        }
        return rng.Range(0, Nodes.Length);
    }
}
