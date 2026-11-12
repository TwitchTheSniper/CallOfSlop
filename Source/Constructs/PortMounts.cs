using System.Numerics;
using System.Text;

namespace CallOfSlop;

/// <summary>
/// <c>--check-ports [NAME]</c>: does every link port of every part sit on something drawn? For each port (the named ones,
/// the loader's implicit ones and the data port a link ends at) it measures the gap from the port's point to the nearest
/// drawn box ("near": zero when the point is in or on a box) and the gap straight back into the part, against the way the
/// port faces ("back": how far a link's end hangs in front of the machine; none when nothing is drawn behind it). A port
/// floats when its near gap is over <see cref="Floating"/>. Shapes that are not plain boxes count by their bounds. The
/// table, worst first, goes to <c>check-ports.txt</c> next to the DLL; the summary to the console. NAME: only parts whose
/// name contains it. Exit code 0: it is a report, not a gate.
/// </summary>
public static class PortMounts
{
    public const float Floating = 0.03f; // a port further than this from every drawn box hangs in the air (a spout's mouth keeps 2.6 cm clear of its port, so the link's end shows)

    public readonly record struct Row(string Part, string Port, string Type, Vector3 At, byte Face, float Near, float Back, string File);

    /// <summary>The gap from <paramref name="p"/> to the nearest drawn box of <paramref name="d"/>, and the gap along -facing (infinity: nothing behind it).</summary>
    public static (float Near, float Back) Gap(ConstructPartDef d, Vector3 p, byte face, bool faced)
    {
        var near = float.PositiveInfinity;
        var back = float.PositiveInfinity;
        var axis = face >> 1;
        var sign = (face & 1) == 0 ? 1f : -1f; // (the port faces +axis or -axis)
        foreach (var b in d.Boxes)
        {
            if (b.Hidden)
                continue;
            var mn = b.Center - b.Half;
            var mx = b.Center + b.Half;
            var q = Vector3.Clamp(p, mn, mx);
            near = MathF.Min(near, Vector3.Distance(p, q));
            if (!faced)
                continue;
            var over = true; // is the box behind the point, on its line?
            for (var a = 0; a < 3; a++)
                if (a != axis && (p[a] < mn[a] - 0.001f || p[a] > mx[a] + 0.001f))
                    over = false;
            if (!over)
                continue;
            var top = sign > 0 ? mx[axis] : mn[axis]; // the box's face towards the port
            var gap = (p[axis] - top) * sign;
            var far = (p[axis] - (sign > 0 ? mn[axis] : mx[axis])) * sign;
            if (far < -0.001f)
                continue; // (the whole box is in front of the port)
            back = MathF.Min(back, MathF.Max(0, gap));
        }
        return (near, back);
    }

    private static bool Still(in CPartBox b) => !b.Hidden && !b.Spin && !b.Roll && b.Pump == 0 && b.Work is not (BoxWork.Spin or BoxWork.Stroke) && b.RigTravel == 0; // drawn, and not a box that moves (a ram, a chuck, a lever)

    /// <summary>
    /// A port the loader makes (an implicit "Power in": the middle of a bounds face) seated on the part: <paramref name="want"/>
    /// when something drawn is there, else the nearest point where a line straight back from the bounds face first meets a
    /// drawn box, so the link still leaves the surface with nothing in front of it. A part that draws nothing keeps its point.
    /// </summary>
    public static Vector3 Seat(ConstructPartDef d, Vector3 want, byte face)
    {
        var (near, back) = Gap(d, want, face, true);
        if (near <= Floating && (back <= Floating || float.IsInfinity(back)))
            return want;
        var axis = face >> 1;
        var sign = (face & 1) == 0 ? 1f : -1f;
        var best = want;
        var bestScore = float.PositiveInfinity;
        void Try(Vector3 from)
        {
            // The first drawn face a line from the bounds plane straight back meets, at this place across.
            var hit = float.NegativeInfinity;
            foreach (var b in d.Boxes)
            {
                if (!Still(b))
                    continue;
                var over = true;
                for (var a = 0; a < 3; a++)
                    if (a != axis && MathF.Abs(from[a] - b.Center[a]) > b.Half[a] + 0.0005f)
                        over = false;
                if (over)
                    hit = MathF.Max(hit, (b.Center[axis] + sign * b.Half[axis]) * sign);
            }
            if (float.IsNegativeInfinity(hit))
                return;
            var p = from;
            p[axis] = MathF.Min(hit, want[axis] * sign) * sign;
            var score = Vector3.Distance(p, want);
            if (score < bestScore - 0.0005f)
                (best, bestScore) = (p, score);
        }
        Try(want);
        foreach (var b in d.Boxes)
        {
            if (!Still(b))
                continue;
            var p = want;
            var wide = true;
            for (var a = 0; a < 3; a++)
            {
                if (a == axis)
                    continue;
                if (b.Half[a] < 0.02f)
                    wide = false; // (too thin a face to carry a port: an edge strip, a rail)
                var inset = MathF.Min(0.05f, b.Half[a] * 0.5f);
                p[a] = Math.Clamp(want[a], b.Center[a] - b.Half[a] + inset, b.Center[a] + b.Half[a] - inset);
            }
            if (wide)
                Try(p);
        }
        return best;
    }

    /// <summary>
    /// Where a data link ends on a part whose middle is in the air (an open frame, a rack, a gantry): the nearest point of a
    /// drawn box to <paramref name="mid"/>, a solid one for choice. Null: the middle is in or on something drawn, or nothing is.
    /// </summary>
    public static Vector3? SeatData(ConstructPartDef d, Vector3 mid)
    {
        if (Gap(d, mid, 2, false).Near <= Floating)
            return null;
        Vector3? best = null;
        var bestScore = float.PositiveInfinity;
        for (var i = 0; i < d.Boxes.Length; i++)
        {
            var b = d.Boxes[i];
            if (!Still(b))
                continue;
            var q = Vector3.Clamp(mid, b.Center - b.Half, b.Center + b.Half);
            var score = Vector3.Distance(q, mid) + (i < d.SolidBoxes ? 0 : 0.05f); // (a solid box wins over a detail 5 cm nearer)
            if (score < bestScore)
                (best, bestScore) = (q, score);
        }
        return best;
    }

    public static List<Row> Measure(ConstructPartDef d)
    {
        var rows = new List<Row>();
        foreach (var np in d.NamedPorts)
        {
            var (near, back) = Gap(d, np.At, np.Face, true);
            rows.Add(new Row(d.Name, np.Name + (np.Implicit ? " (implicit)" : ""), ConstructRoutes.TypeKey(np.Type), np.At, np.Face, near, back, d.File));
        }
        if (ConstructRoutes.IsOutput(d, RouteType.Data) || ConstructRoutes.IsInput(d, RouteType.Data))
        {
            var at = DataPoint(d);
            var (near, _) = Gap(d, at, 2, false);
            rows.Add(new Row(d.Name, "data", "data", at, 255, near, float.PositiveInfinity, d.File));
        }
        return rows;
    }

    /// <summary>Where a data link meets the part (ConstructRoutes.PortPoint): its seat, else the middle of its bounds or the top of a capital part.</summary>
    public static Vector3 DataPoint(ConstructPartDef d) => ConstructRoutes.DataSeat(d) ?? ConstructRoutes.DataPortAt(d);

    public static int Cli(string[] args)
    {
        var at = Array.FindIndex(args, a => a.Equals("--check-ports", StringComparison.OrdinalIgnoreCase));
        var only = at >= 0 && at + 1 < args.Length && !args[at + 1].StartsWith("--") ? args[at + 1] : "";
        var rows = new List<Row>();
        var parts = 0;
        foreach (var d in ConstructParts.All)
        {
            if (only.Length > 0 && !d.Name.Contains(only, StringComparison.OrdinalIgnoreCase))
                continue;
            var mine = Measure(d);
            if (mine.Count > 0)
                parts++;
            rows.AddRange(mine);
        }
        rows.Sort((a, b) => b.Near != a.Near ? b.Near.CompareTo(a.Near) : string.CompareOrdinal(a.Part + a.Port, b.Part + b.Port));
        var sb = new StringBuilder();
        sb.AppendLine("near_cm back_cm type     face  at                     part / port (file)");
        foreach (var r in rows)
            sb.AppendLine(Line(r));
        var path = Path.Combine(AppContext.BaseDirectory, "check-ports.txt");
        File.WriteAllText(path, sb.ToString());
        var floating = rows.Where(r => r.Near > Floating).ToList();
        Console.WriteLine($"[check-ports] {rows.Count} ports on {parts} parts; {floating.Count} float over {Floating * 100:0} cm on {floating.Select(r => r.Part).Distinct().Count()} parts ({rows.Count(r => r.Near > 0.1f)} over 10 cm); table in {path}");
        foreach (var g in floating.GroupBy(r => r.Type).OrderByDescending(g => g.Count()))
            Console.WriteLine($"[check-ports]   {g.Key}: {g.Count()} floating, worst {g.Max(r => r.Near) * 100:0} cm");
        foreach (var r in floating.Take(20))
            Console.WriteLine("[check-ports] " + Line(r));
        return 0;
    }

    private static string Line(Row r) =>
        $"{r.Near * 100,7:0.0} {(float.IsInfinity(r.Back) ? "      -" : $"{r.Back * 100,7:0.0}")} {r.Type,-8} {(r.Face == 255 ? "-" : ConstructParts.DirName(r.Face)),-5} {r.At.X,6:0.00} {r.At.Y,6:0.00} {r.At.Z,6:0.00}   {r.Part} / {r.Port} ({Path.GetFileName(r.File)})";
}
