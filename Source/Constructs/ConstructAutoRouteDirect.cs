using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The straight way first (user, 2026-11-19: "conveyor placement should try to take a straight path to its destination"): before
/// the lattice is built, a chute's or conveyor's auto-route into an input tries the routes a fitter would lay by eye, fewest nodes
/// first and then the shortest: one straight run (sloped where the ends are at different heights: a grade change is free on a chute
/// or conveyor, <see cref="ConstructRoutes.BendRule(RouteType, Vector3, Vector3)"/>), one turn (where the way out of the start meets
/// the way into the goal, or a 45 degree leg), two turns (out, across, in; or a climb, a level run and straight down into a port
/// that faces up, or over a machine into one entered from above). Each leaves its port and enters the goal's as the lattice allows
/// (<see cref="PortCost"/>: near its facing free, out of its side at a price, never back into it), never doubles back, keeps a
/// conveyor's turns as roomy as its bed is wide, and passes the server's whole check (<see cref="Build"/>); the first that passes is
/// the route. Only when none does (a part in the way, a port facing away) does the lattice search go round things. A route to a
/// waypoint keeps the lattice's square arrival, which the player's next leg turns from in its steps; pipes and cables keep the
/// lattice too: the tool fixes them to structure and runs them along walls (<see cref="ConstructRoutes.Tidies"/>). Nothing about
/// what a link carries is touched: a shorter link is only its geometry (the user chose straight direct routes, 2026-11-19), but a
/// conveyor is never shorter than the longest piece its output makes (<see cref="LongestPiece"/>), and its slopes keep to the tool's
/// grade snap (<see cref="RouteAsk.Grade"/>: level, 20 or 40 degrees). The candidates are made at once (arithmetic only) and checked
/// a few a frame under the search's budget (<see cref="DirectSlice"/>). Nothing allocates.
/// </summary>
public sealed partial class RouteSearch
{
    public const float DirectPortDeg = 45f + ConstructRoutes.AngleSlackDeg; // the most a straight run may leave or enter a port off its facing (a conveyor's plan step)
    private const int DirectMax = 64; // candidate routes
    private const int DirectBuilds = 16; // ... checked with the server's rule at most, best first
    private const float DirectNodeCost = 2f;
    private const float DirectMinConveyor = 1f; // m: the shortest straight-way conveyor from a maker whose longest piece isn't known (the 1 m blank of production.md, 2026-11-13)
    private const float DirectGradeSlackDeg = 1.5f; // a sloped leg within this of the tool's snapped grade (nodes sit on the 1 cm grid) // metres a node is worth: fewer nodes first, then the shorter

    private readonly Vector3[] _dirC = new Vector3[DirectMax * 2];
    private readonly byte[] _dirN = new byte[DirectMax];
    private readonly float[] _dirScore = new float[DirectMax];
    private readonly int[] _dirOrder = new int[DirectMax];
    private readonly Vector3[] _dirPath = new Vector3[4];
    private int _dirCount, _dirAt, _dirTries;
    private float _dirMinLen; // a conveyor's: no shorter than the longest piece its output makes (production.md, 2026-11-13)

    /// <summary>The straight way's candidates, best first. False when there are none (or the link isn't a chute's or conveyor's into an input): search the lattice.</summary>
    private bool DirectStart()
    {
        if (!_flat || !_closed || _ask.Type is not (RouteType.Conveyor or RouteType.Chute) || _ask.Conduit > 0f || _loose || _lay != LinkLay.Near)
            return false;
        _dirCount = 0;
        _dirMinLen = _ask.Type == RouteType.Conveyor ? LongestPiece() : 0f;
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
        // A port facing up or down (a Cargo Dock's Piece in, a hopper's mouth): over it and straight down into it, after a climb and a
        // level run toward it; or out of such a start, level, then a slope into the goal (grade bends only, so a 45 degree step isn't needed).
        // (a level goal port may be entered from above too, at a side arrival's price, as the lattice allows: over the machine and down)
        var over = d1 != Vector3.Zero && MathF.Abs(d1.Y) > 0.9f ? d1 : _closed ? Vector3.UnitY : Vector3.Zero;
        if (over != Vector3.Zero)
            for (var i = 0; i < 2; i++)
            {
                var c1 = _g + over * (stub * (i + 1));
                AddOver(c1, Plan(c1 - _s), stub, true);
                if (d0 != Vector3.Zero && MathF.Abs(d0.Y) < 0.1f)
                    AddOver(c1, Plan(d0), stub, true);
            }
        if (d0 != Vector3.Zero && MathF.Abs(d0.Y) > 0.9f)
            for (var i = 0; i < 2; i++)
            {
                var c0 = _s + d0 * (stub * (i + 1));
                AddOver(c0, Plan(_g - c0), stub, false);
                if (d1 != Vector3.Zero && MathF.Abs(d1.Y) < 0.1f)
                    AddOver(c0, -Plan(d1), stub, false);
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
        _dirTries = Math.Min(_dirCount, DirectBuilds);
        _dirAt = 0;
        return true;
    }

    /// <summary>
    /// The candidates checked with the server's rule, best first, until <paramref name="end"/> (at least one a call, so it gets on):
    /// true when done (the first that passes is the route; none: the lattice's stages begin), false when out of time.
    /// </summary>
    private bool DirectSlice(long end)
    {
        for (var checkedNow = 0; _dirAt < _dirTries; _dirAt++, checkedNow++)
        {
            if (checkedNow > 0 && Stopwatch.GetTimestamp() >= end)
                return false;
            var n = DirectPath(_dirOrder[_dirAt]);
            if (Build(_dirPath, n, out var total, out var length) != null)
                continue;
            Done(RouteSearchState.Found, null, total - _ask.Count, length, BedWidth(total));
            return true;
        }
        BeginStage();
        return true;
    }

    /// <summary>
    /// The longest piece a conveyor from this output may carry (m): an Extruder's (or a Former's) longest cut as it is set, and at
    /// least <see cref="DirectMinConveyor"/> for any other maker, whose pieces aren't known here. A straight way shorter than this is
    /// left to the lattice: a conveyor into a machine shorter than its piece can't take it on (production.md, 2026-11-13).
    /// </summary>
    private float LongestPiece()
    {
        var c = _ask.C;
        var least = DirectMinConveyor;
        if (_ask.Parent == 255 && _ask.From >= 0 && _ask.From < c.PartCount && c.DefOf(_ask.From).IsExtruder)
        {
            ConstructExtruder.Reach(c, _ask.From, out _, out _, out var longest);
            least = MathF.Max(least, longest + ConstructConveyor.Gap);
        }
        return least;
    }

    private void AddCorner(Vector3 end, Vector3 dir, float along)
    {
        if (along <= 0.01f)
            return;
        AddDirect(1, end + dir * along, default);
    }

    private static Vector3 Plan(Vector3 v) => new(v.X, 0f, v.Z);

    /// <summary>The tool's grade snap (<see cref="RouteAsk.Grade"/>): level only, or slopes at its 20 or 40 degrees; a level or upright leg always does.</summary>
    private bool GradeOk(Vector3 dir)
    {
        var plan = MathF.Sqrt(dir.X * dir.X + dir.Z * dir.Z);
        if (plan < 0.03f)
            return true; // (straight up or down: the plain steps make that corner)
        var deg = MathF.Abs(MathF.Atan2(dir.Y, plan)) * 180f / MathF.PI;
        if (deg < 0.5f)
            return true;
        return _ask.Grade switch
        {
            ConstructRoutes.GradeMode.Level => false,
            ConstructRoutes.GradeMode.Gentle => MathF.Abs(deg - ConstructRoutes.GentleDeg) <= DirectGradeSlackDeg,
            ConstructRoutes.GradeMode.Steep => MathF.Abs(deg - ConstructRoutes.SteepDeg) <= DirectGradeSlackDeg,
            _ => true,
        };
    }

    /// <summary>The snapped grade's slope (degrees), or 0 for free or level.</summary>
    private float SnapDeg => _ask.Grade switch
    {
        ConstructRoutes.GradeMode.Gentle => ConstructRoutes.GentleDeg,
        ConstructRoutes.GradeMode.Steep => ConstructRoutes.SteepDeg,
        _ => 0f,
    };

    /// <summary>
    /// Two corners by a port that faces up or down: <paramref name="over"/> straight out of it, and a level run of a few lengths
    /// along <paramref name="way"/> (in plan) toward it (<paramref name="atGoal"/>) or away from it, so the rest is one climb or drop.
    /// </summary>
    private void AddOver(Vector3 over, Vector3 way, float stub, bool atGoal)
    {
        var plan = way.Length();
        if (plan < 0.05f)
            return;
        way /= plan;
        var reach = Plan(atGoal ? over - _s : _g - over).Length();
        // (with the grade snapped, a run that leaves the climb or the drop at that grade)
        var snap = SnapDeg;
        var graded = snap > 0f ? reach - MathF.Abs(atGoal ? over.Y - _s.Y : _g.Y - over.Y) / MathF.Tan(snap * MathF.PI / 180f) : -1f;
        Span<float> runs = [stub, stub * 2f, reach * 0.5f, graded];
        foreach (var run in runs)
        {
            if (run < 0.05f || run > reach - 0.05f)
                continue;
            if (atGoal)
                AddDirect(2, over - way * run, over);
            else
                AddDirect(2, over, over + way * run);
        }
    }

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
        var extra = 0f; // (a side departure or arrival)
        for (var i = 1; i < n; i++)
        {
            var d = p[i] - p[i - 1];
            var len = d.Length();
            if (len < (i == 1 || i == n - 1 ? ConstructRoutes.MinEndSegment : ConstructRoutes.MinSegment) + 0.005f || len > ConstructRoutes.MaxSegment * 3f)
                return;
            length += len;
            var dir = d / len;
            if (!GradeOk(dir))
                return;
            if (i == 1)
            {
                var leave = DirectLeaves(dir);
                if (leave < 0f)
                    return;
                extra += leave;
            }
            if (i == n - 1 && _closed && _gDir != Vector3.Zero)
            {
                var arrive = PortCost(dir, -_gDir);
                if (arrive < 0f)
                    return;
                extra += arrive;
            }
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
        if (_alongSoFar + length < _dirMinLen)
            return; // (shorter than the longest piece it carries: the lattice's longer way)
        if (_width > 0f && room + 0.005f < _width)
            return; // (a turn that would narrow the bed: the lattice keeps the width)
        // (the same route twice: once is enough)
        for (var k = 0; k < _dirCount; k++)
            if (_dirN[k] == corners && Vector3.DistanceSquared(_dirC[k * 2], _dirC[_dirCount * 2]) < 1e-6f && Vector3.DistanceSquared(_dirC[k * 2 + 1], _dirC[_dirCount * 2 + 1]) < 1e-6f)
                return;
        _dirScore[_dirCount] = corners * DirectNodeCost + length + extra;
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

    /// <summary>The first leg's price for its way (-1: not allowed): out of a port by <see cref="PortCost"/>, on from the last node by the bend rule, any way from a junction.</summary>
    private float DirectLeaves(Vector3 dir)
    {
        if (_ask.Parent != 255)
            return 0f;
        if (_hasPrev)
            return _prevDir == Vector3.Zero || ConstructRoutes.BendRule(_ask.Type, _prevDir, dir) == null ? 0f : -1f;
        return _sDir == Vector3.Zero ? 0f : PortCost(dir, _sDir);
    }

    /// <summary>
    /// A way's price at a port, as the lattice prices it (<see cref="StartFlat"/>): within <see cref="DirectPortDeg"/> of its facing
    /// free (in plan for a level port: the grade is free, a chute or conveyor may climb or drop straight out of it as one laid by hand
    /// does), out of its side (up or down out of a level port too) <see cref="SideCost"/>, back into it never (-1).
    /// </summary>
    private static float PortCost(Vector3 dir, Vector3 port)
    {
        var cos = MathF.Cos(DirectPortDeg * MathF.PI / 180f);
        if (Vector3.Dot(dir, port) < -0.3f)
            return -1f;
        if (MathF.Abs(port.Y) < 0.1f)
        {
            var a = new Vector2(dir.X, dir.Z);
            var la = a.Length();
            if (la < 0.03f)
                return SideCost;
            var b = Vector2.Normalize(new Vector2(port.X, port.Z));
            var pd = Vector2.Dot(a / la, b);
            return pd >= cos ? 0f : pd >= -0.3f ? SideCost : -1f;
        }
        return Vector3.Dot(dir, port) >= cos ? 0f : SideCost;
    }
}
