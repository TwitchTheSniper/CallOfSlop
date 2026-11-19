using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The straight way first (user, 2026-11-19: "conveyor placement should try to take a straight path to its destination"): before
/// the lattice is built, a chute's or conveyor's auto-route tries the routes a fitter would lay by eye, fewest nodes first and then
/// the shortest: one straight run (sloped where the ends are at different heights: a grade change is free on a chute or conveyor,
/// <see cref="ConstructRoutes.BendRule(RouteType, Vector3, Vector3)"/>), one turn (where the way out of the start meets the way into
/// the goal, or a 45 degree leg), then two (out of the start, across, into the goal). Each leaves its port and enters the goal's
/// within <see cref="DirectPortDeg"/> of the port's facing, keeps a conveyor's turns as roomy as its bed is wide, and passes the
/// server's whole check (<see cref="Build"/>); the first that passes is the route. Only when none does (a part in the way, a port
/// facing away) does the lattice search go round things. Pipes and cables keep the lattice: the tool fixes them to structure and
/// runs them along walls (<see cref="ConstructRoutes.Tidies"/>). Nothing about what a link carries is touched: a shorter link is
/// only its geometry. Nothing allocates.
/// </summary>
public sealed partial class RouteSearch
{
    public const float DirectPortDeg = 45f + ConstructRoutes.AngleSlackDeg; // the most a straight run may leave or enter a port off its facing (a conveyor's plan step)
    private const int DirectMax = 48; // candidate routes
    private const int DirectBuilds = 16; // ... checked with the server's rule at most, best first
    private const float DirectNodeCost = 2f; // metres a node is worth: fewer nodes first, then the shorter

    private readonly Vector3[] _dirC = new Vector3[DirectMax * 2];
    private readonly byte[] _dirN = new byte[DirectMax];
    private readonly float[] _dirScore = new float[DirectMax];
    private readonly int[] _dirOrder = new int[DirectMax];
    private readonly Vector3[] _dirPath = new Vector3[4];
    private int _dirCount;

    /// <summary>The straight route, or one or two turns, when one passes: the search is done (Found). False: search the lattice.</summary>
    private bool Direct()
    {
        if (!_flat || !_closed || _ask.Type is not (RouteType.Conveyor or RouteType.Chute) || _ask.Conduit > 0f || _loose || _lay != LinkLay.Near)
            return false;
        _dirCount = 0;
        var w = _width > 0f ? _width : 0f;
        var stub = MathF.Max(StubMin, w > 0f ? w : _radius * 4f);
        var d0 = _ask.Parent != 255 ? Vector3.Zero : _hasPrev ? _prevDir : _sDir; // (the way on from the start: its port's, or the last node's)
        var d1 = _closed ? _gDir : Vector3.Zero; // (the goal's port faces out: the link comes in along -d1)
        var v = _g - _s;
        // Straight.
        AddDirect(0, default, default);
        // One turn: where the ways out and in meet (in plan, at either end's height), or a corner along one end's way.
        if (d0 != Vector3.Zero && d1 != Vector3.Zero && MeetPlan(_s, d0, _g, d1, out var t, out var u))
        {
            AddDirect(1, _s + d0 * t, default);
            AddDirect(1, _g + d1 * u, default);
        }
        if (d0 != Vector3.Zero)
        {
            var ahead = Vector3.Dot(v, d0);
            var side = (v - d0 * ahead).Length();
            AddCorner(_s, d0, stub);
            AddCorner(_s, d0, ahead - side); // (the second leg at 45 degrees)
            AddCorner(_s, d0, ahead);
            AddCorner(_s, d0, ahead * 0.5f);
        }
        if (d1 != Vector3.Zero)
        {
            var back = Vector3.Dot(-v, d1); // (how far the start lies out of the goal's face)
            var side = (-v - d1 * back).Length();
            AddCorner(_g, d1, stub);
            AddCorner(_g, d1, back - side);
            AddCorner(_g, d1, back);
            AddCorner(_g, d1, back * 0.5f);
        }
        // Two turns: out of the start, across (square, at 45 degrees or sloped), into the goal.
        if (d0 != Vector3.Zero && d1 != Vector3.Zero)
        {
            // (each end's run out: a stub, two, half the way, or what leaves a 45 degree leg across; level, so a rise is one incline)
            var ahead = Vector3.Dot(v, d0);
            var back = Vector3.Dot(-v, d1);
            var sideS = Plan(v - d0 * ahead).Length();
            var sideG = Plan(-v - d1 * back).Length();
            Span<float> ts = [stub, stub * 2f, ahead * 0.5f, (ahead - sideS) * 0.5f];
            Span<float> us = [stub, stub * 2f, back * 0.5f, (back - sideG) * 0.5f];
            for (var i = 0; i < ts.Length; i++)
                for (var j = 0; j < us.Length; j++)
                    if (ts[i] > 0.01f && us[j] > 0.01f)
                        AddDirect(2, _s + d0 * ts[i], _g + d1 * us[j]);
            // (and the leg across square to either end's way: a U between ports facing the same way, a Z between ports not in line)
            var dd = Vector3.Dot(Plan(d0), Plan(d1));
            if (MathF.Abs(dd) > 0.5f)
                for (var i = 0; i < ts.Length; i++)
                {
                    var b = (ts[i] - Vector3.Dot(Plan(v), Plan(d0))) / dd; // (the goal's run that puts the far corner level with the near one along d0)
                    if (ts[i] > 0.01f && b > 0.01f)
                        AddDirect(2, _s + d0 * ts[i], _g + d1 * b);
                    var a = (us[i] + Vector3.Dot(Plan(v), Plan(d1))) / dd;
                    if (us[i] > 0.01f && a > 0.01f)
                        AddDirect(2, _s + d0 * a, _g + d1 * us[i]);
                }
        }
        if (_dirCount == 0)
            return false;
        // Best first: fewest nodes, then the shortest.
        for (var i = 0; i < _dirCount; i++)
            _dirOrder[i] = i;
        for (var i = 1; i < _dirCount; i++)
        {
            var k = _dirOrder[i];
            var j = i - 1;
            while (j >= 0 && _dirScore[_dirOrder[j]] > _dirScore[k])
            {
                _dirOrder[j + 1] = _dirOrder[j];
                j--;
            }
            _dirOrder[j + 1] = k;
        }
        var tries = Math.Min(_dirCount, DirectBuilds);
        for (var i = 0; i < tries; i++)
        {
            var k = _dirOrder[i];
            var n = DirectPath(k);
            if (Build(_dirPath, n, out var total, out var length) != null)
                continue;
            Done(RouteSearchState.Found, null, total - _ask.Count, length, BedWidth(total));
            return true;
        }
        return false;
    }

    private void AddCorner(Vector3 end, Vector3 dir, float along)
    {
        if (along <= 0.01f)
            return;
        AddDirect(1, end + dir * along, default);
    }

    private static Vector3 Plan(Vector3 v) => new(v.X, 0f, v.Z);

    /// <summary>Where the start's way and the goal's (in plan) meet: <paramref name="t"/> out of the start, <paramref name="u"/> out of the goal; false when parallel or behind either.</summary>
    private static bool MeetPlan(Vector3 s, Vector3 d0, Vector3 g, Vector3 d1, out float t, out float u)
    {
        t = u = 0f;
        var a = new Vector2(d0.X, d0.Z);
        var b = new Vector2(d1.X, d1.Z);
        if (a.LengthSquared() < 0.25f || b.LengthSquared() < 0.25f)
            return false;
        var den = a.X * b.Y - a.Y * b.X;
        if (MathF.Abs(den) < 1e-3f)
            return false;
        var w = new Vector2(g.X - s.X, g.Z - s.Z);
        var tp = (w.X * b.Y - w.Y * b.X) / den; // (in plan units of a, b)
        var up = (w.X * a.Y - w.Y * a.X) / den;
        if (tp <= 0.01f || up <= 0.01f)
            return false;
        t = tp;
        u = up;
        return true;
    }

    /// <summary>A candidate (0 to 2 corners) kept when its legs, its bends and its ends could pass; scored by nodes and length.</summary>
    private void AddDirect(int corners, Vector3 c0, Vector3 c1)
    {
        if (_dirCount >= DirectMax)
            return;
        _dirC[_dirCount * 2] = ConstructWorld.Quantise(c0);
        _dirC[_dirCount * 2 + 1] = ConstructWorld.Quantise(c1);
        _dirN[_dirCount] = (byte)corners;
        var n = DirectPath(_dirCount);
        var p = _dirPath;
        var length = 0f;
        var cos = MathF.Cos(ConstructRoutes.ConveyorBendDeg * MathF.PI / 180f);
        var room = float.MaxValue;
        var bentBefore = false;
        for (var i = 1; i < n; i++)
        {
            var d = p[i] - p[i - 1];
            var len = d.Length();
            if (len < (i == 1 || i == n - 1 ? ConstructRoutes.MinEndSegment : ConstructRoutes.MinSegment) + 0.005f || len > ConstructRoutes.MaxSegment * 3f)
                return;
            length += len;
            var dir = d / len;
            if (i == 1 && !DirectLeaves(dir))
                return;
            if (i == n - 1 && _closed && _gDir != Vector3.Zero && !PortOk(dir, -_gDir))
                return;
            var bentAfter = false;
            if (i + 1 < n)
            {
                var next = Vector3.Normalize(p[i + 1] - p[i]);
                if (ConstructRoutes.BendRule(_ask.Type, dir, next) != null || Vector3.Dot(dir, next) < -0.06f)
                    return; // (no turn past a right angle: a route that doubles back is never the straight way)
                bentAfter = Vector3.Dot(dir, next) < cos;
            }
            var share = (bentBefore ? 0.5f : 0f) + (bentAfter ? 0.5f : 0f);
            if (share > 0f)
                room = MathF.Min(room, len / share);
            bentBefore = bentAfter;
        }
        if (_width > 0f && room + 0.005f < _width)
            return; // (a turn that would narrow the bed: the lattice keeps the width)
        // (the same route twice: once is enough)
        for (var k = 0; k < _dirCount; k++)
            if (_dirN[k] == corners && Vector3.DistanceSquared(_dirC[k * 2], _dirC[_dirCount * 2]) < 1e-6f && Vector3.DistanceSquared(_dirC[k * 2 + 1], _dirC[_dirCount * 2 + 1]) < 1e-6f)
                return;
        _dirScore[_dirCount] = corners * DirectNodeCost + length;
        _dirCount++;
    }

    private int DirectPath(int k)
    {
        var n = 0;
        _dirPath[n++] = _s;
        for (var i = 0; i < _dirN[k]; i++)
            _dirPath[n++] = _dirC[k * 2 + i];
        _dirPath[n++] = _g;
        return n;
    }

    /// <summary>The first leg's way: out of a port near its facing, on from the last node by the bend rule, any way from a junction.</summary>
    private bool DirectLeaves(Vector3 dir)
    {
        if (_ask.Parent != 255)
            return true;
        if (_hasPrev)
            return _prevDir == Vector3.Zero || ConstructRoutes.BendRule(_ask.Type, _prevDir, dir) == null;
        return _sDir == Vector3.Zero || PortOk(dir, _sDir);
    }

    /// <summary>
    /// A way within <see cref="DirectPortDeg"/> of a port's: in plan for a level port (the grade is free: a chute or conveyor may climb
    /// or drop straight out of it, as one laid by hand does), else in full.
    /// </summary>
    private static bool PortOk(Vector3 dir, Vector3 port)
    {
        var cos = MathF.Cos(DirectPortDeg * MathF.PI / 180f);
        if (MathF.Abs(port.Y) < 0.1f)
        {
            var a = new Vector2(dir.X, dir.Z);
            var la = a.Length();
            if (la < 0.03f)
                return false; // (straight up or down out of a level port: the lattice's business)
            var b = Vector2.Normalize(new Vector2(port.X, port.Z));
            return Vector2.Dot(a / la, b) >= cos;
        }
        return Vector3.Dot(dir, port) >= cos;
    }
}
