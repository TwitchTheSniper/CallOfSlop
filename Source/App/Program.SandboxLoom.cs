using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The Loom, a mode of the linking tool (docs/sandbox/CONSTRUCTS.md, "Cable alignment"): LMB picks links, R goes on to the run, LMB lays
/// its points (each square to the last; the Free routing key lets it go any way), R lays the links along it. RMB takes back the last
/// thing done. The preview is what the server will do: <see cref="ConstructRoutes.LoomPlanFor"/> from the same picks. Links are held by
/// index, so each is checked against the construct as it is now, every frame (the stale index lesson).
/// </summary>
public static partial class Program
{
    private static readonly int[] _lmRoutes = new int[ConstructRoutes.LoomMaxLinks];
    private static readonly ushort[] _lmSource = new ushort[ConstructRoutes.LoomMaxLinks], _lmTo = new ushort[ConstructRoutes.LoomMaxLinks];
    private static readonly CRouteNode[] _lmGuide = new CRouteNode[ConstructRoutes.LoomMaxGuide];
    private static readonly LoomPlan _lmPlan = new();
    private static int _lmCount, _lmPoints, _lmCheckKey;
    private static ushort _lmBody;
    private static bool _lmRun; // laying the run
    private static Vector3 _lmNormal;
    private static string? _lmWhy, _lmCheckWhy;

    private static int LoomHudKey => HashCode.Combine(_lmCount, _lmPoints, _lmRun, _lmWhy);

    private static string LoomHud
    {
        get
        {
            var (fire, aim, reload) = (SandboxKeys.Fire, SandboxKeys.Aim, SandboxKeys.Reload);
            var why = _lmWhy != null ? $" - {_lmWhy}" : "";
            return _lmRun
                ? $"Loom: {_lmCount} links, {_lmPoints} points{why} - {fire}: point, {reload}: lay them, {aim}: back"
                : $"Loom: {_lmCount} links - {fire}: pick a link, {reload}: lay the run, {aim}: back";
        }
    }

    private static void LoomReset()
    {
        _lmCount = _lmPoints = 0;
        _lmBody = 0;
        _lmRun = false;
        _lmWhy = null;
        _lmCheckKey = 0;
    }

    /// <summary>Drops the links that have gone or changed since they were picked; true when anything is left to work on.</summary>
    private static bool LoomCheck(Game g, out Construct? c)
    {
        c = _lmBody != 0 ? g.Constructs.Get(_lmBody) : null;
        if (c == null)
        {
            if (_lmBody != 0)
                LoomReset();
            return false;
        }
        var kept = 0;
        for (var i = 0; i < _lmCount; i++)
            if (_lmRoutes[i] < c.RouteCount && c.Routes[_lmRoutes[i]].Source == _lmSource[i] && c.Routes[_lmRoutes[i]].To == _lmTo[i])
            {
                _lmRoutes[kept] = _lmRoutes[i];
                _lmSource[kept] = _lmSource[i];
                _lmTo[kept++] = _lmTo[i];
            }
        _lmCount = kept;
        for (var i = 0; i < _lmPoints; i++)
            if (_lmGuide[i].Host >= c.PartCount)
                _lmPoints = 0;
        if (_lmCount == 0)
        {
            LoomReset();
            return false;
        }
        return true;
    }

    private static void LoomPick(Game g, Construct c, int k)
    {
        if (_lmBody != c.Id)
        {
            LoomReset();
            _lmBody = c.Id;
        }
        for (var i = 0; i < _lmCount; i++)
            if (_lmRoutes[i] == k)
            {
                _lmCount--;
                _lmRoutes[i] = _lmRoutes[_lmCount];
                _lmSource[i] = _lmSource[_lmCount];
                _lmTo[i] = _lmTo[_lmCount];
                if (_lmCount == 0)
                    LoomReset();
                return;
            }
        if (ConstructRoutes.LoomRefuses(c, k) is { } why)
            g.SandboxSay(why);
        else if (_lmCount >= ConstructRoutes.LoomMaxLinks)
            g.SandboxSay($"a bundle is at most {ConstructRoutes.LoomMaxLinks} links");
        else
        {
            _lmRoutes[_lmCount] = k;
            _lmSource[_lmCount] = c.Routes[k].Source;
            _lmTo[_lmCount++] = c.Routes[k].To;
        }
    }

    /// <summary>The links picked, or the plan, on the linking tool's preview: green or red, with a gap between links.</summary>
    private static void LoomPreview(Construct c, bool planned)
    {
        RouteRender.PreviewBody = c;
        RouteRender.PreviewType = RouteType.Data;
        RouteRender.PreviewFrom = 1;
        var n = 0;
        var max = RouteRender.PreviewPts.Length;
        void Put(Vector3 p, int seg)
        {
            if (n < max)
            {
                RouteRender.PreviewPts[n] = p;
                RouteRender.PreviewSeg[n++] = (byte)seg;
            }
        }
        var links = planned ? _lmPlan.Count : _lmCount;
        for (var i = 0; i < links; i++)
        {
            var k = planned ? _lmPlan.Links[i] : _lmRoutes[i];
            ref var r = ref c.Routes[k];
            var count = planned ? _lmPlan.Counts[i] : r.Count;
            Put(ConstructRoutes.Point(c, k, -1, out var s0), s0);
            for (var j = 0; j < count; j++)
                if (planned)
                    Put(_lmPlan.Nodes[i][j].Pos, c.Parts[_lmPlan.Nodes[i][j].Host].Seg);
                else
                {
                    var p = ConstructRoutes.Point(c, k, j, out var s);
                    Put(p, s);
                }
            Put(ConstructRoutes.Point(c, k, r.Count, out var s1), s1);
            Put(new Vector3(float.NaN), 0);
        }
        RouteRender.PreviewCount = n;
    }

    private static void LoomFrame(Game g, bool aimed, in ConstructHit hit, bool onRoute, bool fire)
    {
        var live = LoomCheck(g, out var c);
        var back = Pressed(GameAction.Aim);
        if (!_lmRun)
        {
            if (fire && onRoute)
                LoomPick(g, hit.Body, hit.RoutePlus1 - 1);
            else if (back)
                LoomReset();
            else if (live && Pressed(GameAction.Reload))
            {
                if (_lmCount < 2)
                    g.SandboxSay("pick at least two links");
                else
                    _lmRun = true;
            }
            _lmWhy = null;
            if (LoomCheck(g, out c) && c != null)
            {
                RouteRender.PreviewOk = true;
                LoomPreview(c, false);
            }
            LinkHud(g);
            return;
        }
        if (!live || c == null)
        {
            LoomReset();
            LinkHud(g);
            return;
        }
        // The next point: on the surface, or a tray's lane; square to the last one unless the Free routing key is held.
        var have = _lmPoints;
        if (aimed && hit.Body == c && !onRoute && _lmPoints < _lmGuide.Length)
        {
            RestPoint(hit, out var p, out var n);
            var at = ConstructRoutes.Snap(c, RouteType.Pipe, p, Vector3.Zero, p, n);
            if (ConstructTrays.Snap(c, hit.Part, p, out var lane))
                at = lane;
            else if (_lmPoints > 0 && !Down(GameAction.FreeRouting))
            {
                var last = _lmGuide[_lmPoints - 1].Pos;
                var d = Vector3.Abs(at - last);
                var axis = d.X > d.Y ? d.X > d.Z ? 0 : 2 : d.Y > d.Z ? 1 : 2;
                var square = last;
                square[axis] = at[axis];
                at = square;
            }
            _lmGuide[_lmPoints] = new CRouteNode { Pos = at, Host = (ushort)hit.Part };
            have = _lmPoints + 1;
            if (_lmPoints == 0)
                _lmNormal = n;
            if (fire)
                _lmPoints++;
        }
        var key = HashCode.Combine(c.RebuildStamp, have, have > 0 ? _lmGuide[have - 1].Pos : default);
        if (key != _lmCheckKey)
        {
            _lmCheckKey = key;
            _lmCheckWhy = have < 2 ? "lay the run" : ConstructRoutes.LoomPlanFor(c, _lmRoutes.AsSpan(0, _lmCount), _lmGuide.AsSpan(0, have), _lmNormal, _lmPlan);
        }
        _lmWhy = _lmCheckWhy;
        RouteRender.PreviewOk = _lmWhy == null;
        LoomPreview(c, _lmWhy == null && have >= 2);
        if (back)
        {
            if (_lmPoints > 0)
                _lmPoints--;
            else
                _lmRun = false;
        }
        else if (Pressed(GameAction.Reload))
        {
            // Laid as far as the last point placed.
            var why = _lmPoints < 2 ? "lay the run: at least two points" : ConstructRoutes.LoomPlanFor(c, _lmRoutes.AsSpan(0, _lmCount), _lmGuide.AsSpan(0, _lmPoints), _lmNormal, _lmPlan);
            if (why != null)
                g.SandboxSay(why);
            else
            {
                g.SandboxSendLoom(c.Id, _lmRoutes.AsSpan(0, _lmCount), _lmSource.AsSpan(0, _lmCount), _lmTo.AsSpan(0, _lmCount), _lmGuide.AsSpan(0, _lmPoints), _lmNormal);
                LoomReset();
                _linkWheel.Mode = LinkMode.Link;
            }
        }
        LinkHud(g);
    }
}
