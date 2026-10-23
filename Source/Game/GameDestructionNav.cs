using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Bots and destruction (BotNavPatch.cs): when chunks break or fall the server queues a patch of the nav graph around them
/// (floors that fell switch their nodes off, links across the change are walked again, holes at floor level get nodes on
/// both sides), run a slice per tick within <see cref="NavPatchBudgetMs"/>. The patches are undone at the next match, and a
/// graph built while something was broken is thrown away then and built again from the whole map.
/// </summary>
public sealed partial class Game
{
    /// <summary>Server time per tick (ms) the nav patches may use.</summary>
    public const double NavPatchBudgetMs = 0.4;

    /// <summary>Most queued patches (older ones are merged into the newest when it overflows).</summary>
    public const int MaxNavPatches = 16;

    private readonly Queue<(Box Region, Vector3[] Holes)> _navPatches = new();
    private IEnumerator<bool>? _navPatch;
    private NavGraph? _navPatchGraph;

    /// <summary>Nav patches finished this game (self-test).</summary>
    public int NavPatchesDone { get; private set; }

    /// <summary>Server: structure <paramref name="s"/> changed shape (what broke is in <c>_dBroken</c>, what fell in <c>_dFallen</c>): queue a nav patch.</summary>
    private void NoteStructureChanged(int s, double now)
    {
        _ = s;
        _ = now;
        if (NavGraph.Current is not { } graph || graph == NavGraph.Empty || graph.ExtraHead.Length == 0 || !EnsureNavOwner(graph, World.Map))
            return; // no graph (yet): it's built from the map as it is
        var d = World.Map.Destruction!;
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        var holes = new List<Vector3>();
        void Grow(int c)
        {
            var b = d.Original[c];
            min = Vector3.Min(min, b.Min);
            max = Vector3.Max(max, b.Max);
        }
        foreach (var c in _dBroken)
        {
            Grow(c);
            if ((d.Flags[c] & (MapDestruction.FlagFloor | MapDestruction.FlagSkin)) != 0 || holes.Count >= 24)
                continue;
            // A hole at floor level: is there floor right under the chunk's bottom?
            var b = d.Original[c];
            var probe = new Vector3(b.Center.X, b.Min.Y + 0.3f, b.Center.Z);
            var down = World.Query.Raycast(probe, -Vector3.UnitY, 0.7f, QueryFilter.Navigation);
            if (down >= 0.7f)
                continue;
            var n = b.Half.X < b.Half.Z ? Vector3.UnitX : Vector3.UnitZ; // through the wall
            foreach (var side in new[] { -1f, 1f })
            {
                var p = new Vector3(b.Center.X, b.Min.Y + 0.3f - down, b.Center.Z) + n * side * (MathF.Min(b.Half.X, b.Half.Z) + 0.75f);
                if (!holes.Any(h => Vector3.DistanceSquared(h, p) < 0.8f * 0.8f))
                    holes.Add(p);
            }
        }
        foreach (var c in _dFallen)
            Grow(c);
        if (min.X > max.X)
            return;
        var region = new Box((min + max) / 2, (max - min) / 2);
        if (_navPatches.Count >= MaxNavPatches)
        {
            // Too many at once (a chain of blasts): merge them into one patch over all of it.
            var (r0, h0) = _navPatches.Dequeue();
            var all = new List<Vector3>(h0);
            while (_navPatches.Count > 0)
            {
                var (r, h) = _navPatches.Dequeue();
                r0 = Union(r0, r);
                all.AddRange(h);
            }
            region = Union(r0, region);
            all.AddRange(holes);
            holes = all.Take(48).ToList();
        }
        _navPatches.Enqueue((region, holes.ToArray()));
    }

    /// <summary>
    /// A new match (or another game) on this graph: everything is whole again, so it goes back to as built (and a graph built
    /// with holes in it is thrown away to be built again). False if it was thrown away.
    /// </summary>
    private bool EnsureNavOwner(NavGraph g, Map map)
    {
        if (ReferenceEquals(g.PatchOwner, this) && g.PatchMatch == MatchNumber)
            return true;
        _navPatches.Clear();
        _navPatch = null;
        g.ResetRuntime();
        g.PatchOwner = this;
        g.PatchMatch = MatchNumber;
        if (g.Tainted && map.Destruction is { } d && d.Structures.All(s => s.BrokenCount == 0))
        {
            NavGraph.Forget(map); // built with holes in it: build it again from the whole map
            return false;
        }
        // A graph that arrived mid-match is built from the map as built (NavGraph.PumpGraph): patch in what's already broken.
        if (map.Destruction is { } dest && dest.Structures.Any(s => s.BrokenCount > 0))
        {
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            for (var c = 0; c < dest.Count; c++)
                if (dest.Broken[c])
                {
                    min = Vector3.Min(min, dest.Original[c].Min);
                    max = Vector3.Max(max, dest.Original[c].Max);
                }
            if (min.X <= max.X)
                _navPatches.Enqueue((new Box((min + max) / 2, (max - min) / 2), []));
        }
        return true;
    }

    private static Box Union(Box a, Box b)
    {
        var min = Vector3.Min(a.Min, b.Min);
        var max = Vector3.Max(a.Max, b.Max);
        return new Box((min + max) / 2, (max - min) / 2);
    }

    /// <summary>Server, every tick: undoes last match's patches when a new one started, then runs queued patches within the budget.</summary>
    private void ServerTickDestructionNav()
    {
        var map = World.Map;
        if (map.Destruction == null)
            return;
        var g = NavGraph.Current;
        if (g == NavGraph.Empty || g.ExtraHead.Length == 0)
        {
            _navPatches.Clear(); // no graph yet: it will be built from the map as it is now
            _navPatch = null;
            return;
        }
        if (!EnsureNavOwner(g, map))
            return;
        if (_navPatch == null && _navPatches.Count == 0)
            return;
        var start = Stopwatch.GetTimestamp();
        while (Stopwatch.GetElapsedTime(start).TotalMilliseconds < NavPatchBudgetMs)
        {
            if (_navPatch == null || !ReferenceEquals(_navPatchGraph, g))
            {
                if (_navPatches.Count == 0)
                {
                    _navPatch = null;
                    return;
                }
                var (region, holes) = _navPatches.Dequeue();
                _navPatch = g.PatchSteps(map, region, holes).GetEnumerator();
                _navPatchGraph = g;
            }
            if (!_navPatch.MoveNext() || _navPatch.Current)
            {
                _navPatch = null;
                NavPatchesDone++;
            }
        }
    }

    /// <summary>Self-test: runs the queued nav patches to the end now.</summary>
    public void SelfTestFinishNavPatches()
    {
        for (var guard = 0; guard < 100000 && (_navPatch != null || _navPatches.Count > 0); guard++)
            ServerTickDestructionNav();
    }
}
