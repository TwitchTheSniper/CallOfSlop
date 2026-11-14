using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The story world's paths (docs/story/PATHS.md; Karl, 2026-11-14: people walked into the colony's buildings). The host
/// names the planetoid's sites, the penal colony and the quarry, and asks for the bots' nav graph over them
/// (<see cref="NavGraph.RequestSites"/>, NavSites.cs), again when what stands there has changed and has stayed so a while.
/// With that graph the story's people, the guards and the riot squad walk the arena bots' paths (A*, path following, the
/// stuck watch) round walls and through doorways, opening the doors on their way and shutting them after; and the convict
/// gangs that walk (barrows, hauls, the march) have their ways routed round the buildings once (GameLabour.cs). Nothing
/// here allocates in a steady tick: a look every <see cref="NavSitesLookEvery"/> s, a few compares a bot.
/// </summary>
public sealed partial class Game
{
    public const double NavSitesLookEvery = 2; // s between the host's looks at what stands on the sites
    public const double NavSitesSettle = 4, NavSitesResettle = 12; // s what stands must stay the same before the first build, and before a new one
    public const float NavSiteMargin = 8; // m of open ground round the colony's constructs the graph covers
    public const float NavDoorReach = 2.2f, NavDoorEarly = 3.5f; // m: a bot this near a shut door ahead opens it; this near one its path goes through
    public const double NavDoorShutAfter = 5; // s: a door a bot opened shuts this long after, once nobody is in it

    private double _nsAt = double.NegativeInfinity, _nsChangedAt;
    private long _nsKey, _nsAsked = -1;
    private Map? _nsMap;
    private NavGraph? _nsLabourFor, _nsDoorsFor; // the graph the gangs' ways were last routed on
    private Func<Vector3, Vector3, bool>? _nsClear;
    private readonly List<Construct> _nsNear = new(256);
    private (int Door, double At)[] _nsOpened = []; // doors a bot opened (NavGraph.SiteDoors), to shut again: one each at most
    private int _nsOpenedCount;
    private float[] _nsSeen = []; // per site door: the short angle its leaf stood at in the last look (NaN: none)

    /// <summary>Server, every Sandbox tick: the sites' graph asked for when the colony stands, and kept up with it.</summary>
    private void NavSitesTick(double now)
    {
        var nav = NavGraph.Current;
        NavGraph.ConstructsClear = nav.Sites.Length > 0 ? (_nsClear ??= NavConstructsClear) : null;
        if (now - _nsAt < NavSitesLookEvery && now >= _nsAt)
            return;
        _nsAt = now;
        if (!ReferenceEquals(_nsDoorsFor, nav))
        {
            _nsDoorsFor = nav;
            _nsOpenedCount = 0; // (they were the old graph's doors)
            _nsOpened = new (int, double)[nav.SiteDoors.Length];
            _nsSeen = new float[nav.SiteDoors.Length];
            Array.Fill(_nsSeen, float.NaN);
        }
        NavDoorsShut(now);
        NavDoorsLearn(nav);
        if (nav.Sites.Length > 0 && !ReferenceEquals(_nsLabourFor, nav) && LabourCount > 0)
        {
            _nsLabourFor = nav;
            Log($"[paths] the sites' graph: {nav.Nodes.Length} nodes, {nav.MainNodes.Length} joined up, {nav.SiteDoors.Length} doors, {nav.SiteBoxes} boxes of {nav.SiteBodies} constructs; {nav.BuildMs:0} ms of work, the longest slice {nav.SiteSliceMs:0.0} ms");
            NavReachReport(nav);
            LabourResolve(); // (the gangs' ways, routed on the new graph)
        }
        var map = World.Map;
        if (!ReferenceEquals(_nsMap, map))
        {
            _nsMap = map;
            _nsAsked = -1;
            _nsKey = 0;
        }
        if (map.Planetoid == null)
            return;
        var key = NavSitesKey();
        if (key != _nsKey)
        {
            _nsKey = key;
            _nsChangedAt = now;
            return;
        }
        if (key == _nsAsked || now - _nsChangedAt < (_nsAsked == -1 ? NavSitesSettle : NavSitesResettle))
            return;
        _nsAsked = key;
        var sites = key == 0 ? [] : NavSitesNow();
        NavGraph.RequestSites(map, sites, Constructs);
        Log($"[paths] {(sites.Length == 0 ? "no sites" : string.Join(", ", sites.Select(s => $"{s.Name} {s.MaxX - s.MinX:0} by {s.MaxZ - s.MinZ:0} m")))}: the bots' graph is built again");
    }

    /// <summary>
    /// Once per graph, to the log: how much of each colony building's floor can be walked to from outside (the rest is walled
    /// off by furniture, a doorway too tight or a step too high, or no stair a body can climb). For whoever lays the rooms out.
    /// </summary>
    private void NavReachReport(NavGraph nav)
    {
        foreach (var c in Constructs.Bodies)
        {
            if (!(c.Anchored || c.Pinned) || nav.SiteOf(c.WorldCom) < 0 || c.PartCount < 12)
                continue;
            int all = 0, reached = 0, upper = 0, upperReached = 0;
            for (var i = 0; i < nav.Nodes.Length; i++)
            {
                var l = c.ToLocal(nav.Nodes[i]);
                if (l.X < c.LocalMin.X + 0.3f || l.X > c.LocalMax.X - 0.3f || l.Z < c.LocalMin.Z + 0.3f || l.Z > c.LocalMax.Z - 0.3f || l.Y < c.LocalMin.Y - 0.5f || l.Y > c.LocalMax.Y - 1.6f)
                    continue;
                var main = nav.Component[i] == nav.MainComponent;
                if (l.Y < c.LocalMin.Y + 1.5f)
                {
                    all++;
                    reached += main ? 1 : 0;
                }
                else
                {
                    upper++;
                    upperReached += main ? 1 : 0;
                }
            }
            if (all + upper < 8 || reached + upperReached == all + upper)
                continue;
            Log($"[paths] {ColonySiteName(c)}: {reached} of {all} spots on the ground floor and {upperReached} of {upper} above it (roofs too) can be walked to from outside");
        }
    }

    /// <summary>What stands on the sites, as one number: the colony's and the works' ids, and the anchored constructs near them with their part counts.</summary>
    private long NavSitesKey()
    {
        if (_opColony == 0 || Constructs.Get(_opColony) is not { } colony)
            return 0;
        long key = _opColony * 31L + _qrWorks + NavGraph.LeafStandsVersion * 7_919_000L;
        key = key * 31 + NavNearKey(colony.WorldCom, 110f);
        if (Constructs.Get(_qrWorks) is { } works)
            key = key * 31 + NavNearKey(works.WorldCom, 50f);
        return key == 0 ? 1 : key;
    }

    private long NavNearKey(Vector3 at, float reach)
    {
        long key = 17;
        foreach (var c in Constructs.Bodies)
            if ((c.Anchored || c.Pinned || c.Frozen) && Vector3.DistanceSquared(c.WorldCom, at) < reach * reach)
                key = key * 1_000_003L + c.Id * 7919L + c.PartCount; // (as the build takes them: NavSites.cs)
        return key;
    }

    /// <summary>The colony's site (its anchored constructs' extent in the colony's frame, and a margin) and the quarry's.</summary>
    private NavSite[] NavSitesNow()
    {
        var list = new List<NavSite>(2);
        if (Constructs.Get(_opColony) is { } colony && NavSiteRound("colony", colony, 110f, NavSiteMargin) is { } a)
            list.Add(a);
        if (Constructs.Get(_qrWorks) is { } works && NavSiteRound("quarry", works, 50f, 16f) is { } b)
            list.Add(b);
        return list.ToArray();
    }

    /// <summary>A site in <paramref name="frame"/>'s own x and z round every anchored construct within <paramref name="reach"/> m of it.</summary>
    private NavSite? NavSiteRound(string name, Construct frame, float reach, float margin)
    {
        var origin = frame.ToWorld(Vector3.Zero);
        var ax = Vector3.Normalize(frame.ToWorld(Vector3.UnitX) - origin);
        var az = Vector3.Normalize(frame.ToWorld(Vector3.UnitZ) - origin);
        var up = GravityFrames.UpAt(origin);
        float x0 = float.MaxValue, x1 = float.MinValue, z0 = float.MaxValue, z1 = float.MinValue;
        Constructs.Near(origin, reach, _nsNear);
        foreach (var c in _nsNear)
        {
            if (!(c.Anchored || c.Pinned))
                continue;
            var d = c.WorldCom - origin;
            if (MathF.Abs(Vector3.Dot(d, up)) > 30f || d.LengthSquared() > reach * reach)
                continue; // (a craft parked over the colony, or one far off)
            var r = MathF.Min(c.Radius, 30f);
            var x = Vector3.Dot(d, ax);
            var z = Vector3.Dot(d, az);
            x0 = MathF.Min(x0, x - r);
            x1 = MathF.Max(x1, x + r);
            z0 = MathF.Min(z0, z - r);
            z1 = MathF.Max(z1, z + r);
        }
        if (x0 > x1)
            return null;
        return new NavSite(name, origin, ax, az, x0 - margin, x1 + margin, z0 - margin, z1 + margin);
    }

    /// <summary>Is the way between two feet positions clear of constructs: at knee height either side of the body, and at the chest? (NavGraph.Clear on a site graph.)</summary>
    private bool NavConstructsClear(Vector3 a, Vector3 b)
    {
        var d = b - a;
        var len = d.Length();
        if (len < 0.05f)
            return true;
        d /= len;
        var up = GravityFrames.UpAt(a);
        var side = Vector3.Cross(d, up);
        side = side.LengthSquared() > 1e-6f ? Vector3.Normalize(side) * (World.PlayerRadius * 0.8f) : Vector3.Zero;
        var knee = up * 0.6f;
        var chest = up * 1.3f;
        return !Constructs.Raycast(a + knee + side, d, len, out _) && !Constructs.Raycast(a + knee - side, d, len, out _) && !Constructs.Raycast(a + chest, d, len, out _);
    }

    /// <summary>Server, a bot's tick on a site graph: the shut door just ahead on its way, opened as F opens it (and noted, to shut again).</summary>
    private void BotSiteDoors(Player p, BotBrain b, NavGraph nav, Vector3 wish, double now)
    {
        if (wish.LengthSquared() < 0.01f)
            return;
        var pos = p.Position;
        var doors = nav.SiteDoors;
        for (var i = 0; i < doors.Length; i++)
        {
            var to = doors[i].At - pos;
            var d2 = to.LengthSquared();
            if (d2 > NavDoorEarly * NavDoorEarly)
                continue;
            // On its way: the path's next nodes pass the door (opened early, so the leaf has swung before the bot is in
            // its way), or it is near and ahead (a bot walking straight).
            var onWay = d2 < NavDoorReach * NavDoorReach && Vector3.Dot(to, wish) > 0;
            if (ReferenceEquals(b.SearchGraph, nav))
                for (var k = b.PathIdx; k < b.PathLen && k < b.PathIdx + 3 && !onWay; k++)
                    onWay = Vector3.DistanceSquared(nav.Nodes[b.Path[k]], doors[i].At) < 2f * 2f;
            if (!onWay || Constructs.Get(doors[i].Body) is not { } c || !NavDoor(c, doors[i], out var shut) || !shut)
                continue;
            NavDoorSet(c, doors[i], true);
            NavDoorOpened(i, now);
        }
    }

    private void NavDoorOpened(int door, double now)
    {
        for (var k = 0; k < _nsOpenedCount; k++)
            if (_nsOpened[k].Door == door)
            {
                _nsOpened[k].At = now;
                return;
            }
        if (_nsOpenedCount < _nsOpened.Length)
            _nsOpened[_nsOpenedCount++] = (door, now);
    }

    /// <summary>Is the door still the one the graph was built with, and is it shut (and free to open: not locked, not wrecked, no wire running it)?</summary>
    private static bool NavDoor(Construct c, in NavSiteDoor d, out bool shut)
    {
        shut = false;
        if (d.Part >= c.PartCount || c.Parts[d.Part].Broken)
            return false;
        if (d.Joint >= 0)
        {
            if (d.Joint >= c.JointCount || ConstructDoor.JointOf(c, d.Part) != d.Joint)
                return false;
            ref var jt = ref c.Joints[d.Joint];
            shut = !jt.Open && !jt.Locked && !jt.FreeMoving;
            return true;
        }
        if (c.DoorOf(d.Part) is not (var k and >= 0) || c.Doors[k].Part != d.Part)
            return false;
        shut = !ConstructDoors.TargetOpen(c, d.Part) && !ConstructDoors.Locked(c, d.Part);
        return true;
    }

    private void NavDoorSet(Construct c, in NavSiteDoor d, bool open)
    {
        if (d.Joint >= 0)
        {
            c.Joints[d.Joint].Open = open; // (as F does: ConstructEdits.Use)
            c.Wake();
        }
        else if (c.DoorOf(d.Part) is var k and >= 0)
            ConstructDoors.Command(Constructs, c, k, open);
    }

    /// <summary>Server, each look: doors a bot opened a while ago, shut again once nobody stands in them.</summary>
    private void NavDoorsShut(double now)
    {
        var doors = NavGraph.Current.SiteDoors;
        for (var i = _nsOpenedCount - 1; i >= 0; i--)
        {
            var (door, at) = _nsOpened[i];
            if (now - at < NavDoorShutAfter && now >= at)
                continue;
            if (door < doors.Length && Constructs.Get(doors[door].Body) is { } c && NavDoor(c, doors[door], out var shut) && !shut)
            {
                var someone = false;
                foreach (var pl in Players.Values)
                    if (pl.Alive && Vector3.DistanceSquared(pl.Position, doors[door].At) < 2.5f * 2.5f)
                    {
                        someone = true;
                        break;
                    }
                if (someone)
                {
                    _nsOpened[i].At = now - NavDoorShutAfter + NavSitesLookEvery; // (looked at again next time)
                    continue;
                }
                NavDoorSet(c, doors[door], false);
            }
            _nsOpened[i] = _nsOpened[--_nsOpenedCount];
        }
    }

    /// <summary>
    /// Server, each look: a hinged door standing still, open, well short of where the graph has it (the build can't see
    /// everything that stops a leaf), and at the same angle two looks running (not a body leaning on it for a moment), is
    /// learnt, and the graph is built again with it there (<see cref="NavGraph.LeafStands"/>).
    /// </summary>
    private void NavDoorsLearn(NavGraph nav)
    {
        var doors = nav.SiteDoors;
        for (var i = 0; i < doors.Length && i < _nsSeen.Length; i++)
        {
            ref readonly var d = ref doors[i];
            if (d.Joint < 0 || Constructs.Get(d.Body) is not { } c || d.Joint >= c.JointCount)
                continue;
            ref var jt = ref c.Joints[d.Joint];
            var still = jt.Open && !jt.Locked && MathF.Abs(jt.Vel) < 0.05f && MathF.Abs(jt.Angle - d.Stands) > 0.2f
                && MathF.Abs(jt.Angle) < MathF.Abs(d.Stands) && jt.Angle * d.Stands > 0;
            if (!still)
            {
                _nsSeen[i] = float.NaN;
                continue;
            }
            if (!(MathF.Abs(jt.Angle - _nsSeen[i]) < 0.03f))
            {
                _nsSeen[i] = jt.Angle;
                continue;
            }
            _nsSeen[i] = float.NaN;
            if (NavGraph.LeafStands.TryGetValue((World.Map, d.Body, d.Joint), out var known) && MathF.Abs(known - jt.Angle) < 0.1f)
                continue; // (learnt already: the graph with it is on its way)
            NavGraph.LeafStands[(World.Map, d.Body, d.Joint)] = jt.Angle;
            NavGraph.LeafStandsVersion++;
            Log($"[paths] a door of {ColonySiteName(c)} stands open at {jt.Angle * 180 / MathF.PI:0} degrees, not {d.Stands * 180 / MathF.PI:0}: the graph is built again with it there");
        }
    }

    // ---- The gangs' ways (GameLabour.cs) ------------------------------------------------------------------------------

    private readonly NavSearch _nsSearch = new();
    private readonly int[] _nsPath = new int[128];
    private readonly List<Vector3> _nsWay = new(LabourScene.MaxPoints * 4);

    /// <summary>
    /// Server, as the gangs are worked out: a walking gang's way (its points in the world, a loop for the march) routed round
    /// the buildings on the site graph: each leg's path, pulled tight (a point kept only where the straight line to the next
    /// would meet a wall), its ends moved out of anything solid. Kept as it was if the graph has no way, or the route needs
    /// more than <see cref="LabourScene.MaxPoints"/> points. Returns whether it changed.
    /// </summary>
    private bool LabourRoute(ref LabourScene s, bool loop)
    {
        var nav = NavGraph.Current;
        if (nav.Sites.Length == 0 || s.PointCount < 2)
            return false;
        if (nav.SiteOf(s.P0) < 0)
            return LabourRouteFailed("off the sites");
        _nsWay.Clear();
        var n = s.PointCount;
        var legs = loop ? n : n - 1;
        for (var leg = 0; leg < legs; leg++)
        {
            var from = s.Point(leg);
            var to = s.Point((leg + 1) % n);
            var a = nav.Nearest(from, needSight: false);
            var b = nav.Nearest(to, needSight: false);
            if (a < 0 || b < 0)
                return LabourRouteFailed($"no path node near point {(a < 0 ? leg : (leg + 1) % n)}");
            var len = _nsSearch.Find(nav, a, b, _nsPath);
            if (len == 0)
                return LabourRouteFailed($"no path from point {leg} to point {(leg + 1) % n}");
            var start = NavGraph.Clear(from, nav.Nodes[a]) ? from : nav.Nodes[a]; // (a point inside a wall: from its node)
            var end = NavGraph.Clear(nav.Nodes[b], to) ? to : nav.Nodes[b];
            if (leg == 0)
                _nsWay.Add(start);
            var anchor = _nsWay[^1];
            for (var i = 0; i < len; i++)
            {
                var next = i + 1 < len ? nav.Nodes[_nsPath[i + 1]] : end;
                if (!NavGraph.Clear(anchor, next))
                {
                    anchor = nav.Nodes[_nsPath[i]];
                    _nsWay.Add(anchor);
                }
            }
            if (!loop || leg < legs - 1)
                _nsWay.Add(end);
        }
        if (_nsWay.Count > LabourScene.MaxPoints || _nsWay.Count < 2)
            return LabourRouteFailed($"the route needs {_nsWay.Count} points");
        if (!loop)
        {
            // What goes ahead of them at a way's end (a barrow's nose is 1.5 m in front of the man) stops short of a wall:
            // both ends drawn back along the way until it is clear.
            var reach = s.Kind == LabourKind.Barrow ? 1.6f : 0.6f;
            LabourEndClear(0, 1, reach);
            LabourEndClear(_nsWay.Count - 1, _nsWay.Count - 2, reach);
        }
        var changed = _nsWay.Count != n;
        for (var i = 0; i < _nsWay.Count && !changed; i++)
            changed = Vector3.DistanceSquared(_nsWay[i], s.Point(i)) > 0.01f;
        if (!changed)
            return false;
        for (var i = 0; i < _nsWay.Count; i++)
            s.SetPoint(i, _nsWay[i]);
        s.PointCount = (byte)_nsWay.Count;
        return true;
    }

    /// <summary>Moves way point <paramref name="end"/> back towards point <paramref name="towards"/> until <paramref name="reach"/> m beyond it is clear (at most half way).</summary>
    private void LabourEndClear(int end, int towards, float reach)
    {
        var a = _nsWay[towards];
        var b = _nsWay[end];
        var len = Vector3.Distance(a, b);
        if (len < 0.5f)
            return;
        var dir = (b - a) / len;
        for (var back = 0f; back <= len * 0.5f; back += 0.25f)
        {
            var at = b - dir * back;
            if (NavGraph.Clear(at, at + dir * reach))
            {
                _nsWay[end] = at;
                return;
            }
        }
    }

    private string _nsGang = "";

    private bool LabourRouteFailed(string why)
    {
        Log($"[paths] {_nsGang}: its way kept as written ({why})");
        return false;
    }
}
