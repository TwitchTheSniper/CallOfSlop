using System.Globalization;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The window tool's verbs for holes through ships (the spike, docs/sandbox/design/hull-holes-spike.md):
/// <c>bore N [R] [GAP]</c> (host) N railgun lines from the eye along the look, on a square grid GAP metres apart (default 1.5), each of radius R
/// metres (default 0.3), through every body they cross, printing what each made; <c>holes</c> each body's holed parts as this peer has them.
/// </summary>
public static unsafe partial class Program
{
    private static bool WsHoleVerb(Game g, Player me, string verb, string arg)
    {
        if (verb == "holes")
        {
            foreach (var c in g.Constructs.Bodies)
            {
                var n = 0;
                for (var i = 0; i < c.PartCount; i++)
                    n += HullHoles.IsHoled(c.DefOf(i)) ? 1 : 0;
                if (n > 0)
                    WsSay($"holes: body {c.Id}, {n} of {c.PartCount} parts holed");
            }
            return true;
        }
        if (verb != "bore")
            return false;
        if (!g.IsServer)
        {
            WsFail("bore runs on the host");
            return true;
        }
        var bits = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var count = bits.Length > 0 && int.TryParse(bits[0], out var nc) ? Math.Clamp(nc, 1, 400) : 1;
        var radius = bits.Length > 1 && float.TryParse(bits[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var nr) ? nr : 0.3f;
        var gap = bits.Length > 2 && float.TryParse(bits[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var ng) ? ng : 1.5f;
        var fwd = g.LookDir(g.LookYaw, g.LookPitch);
        var side = Vector3.Normalize(Vector3.Cross(fwd, MathF.Abs(fwd.Y) > 0.95f ? Vector3.UnitX : Vector3.UnitY));
        var up = Vector3.Cross(side, fwd);
        var cols = (int)MathF.Ceiling(MathF.Sqrt(count));
        var made = 0;
        var refused = 0;
        var ms = 0.0;
        for (var k = 0; k < count; k++)
        {
            var from = me.Eye + side * ((k % cols - (cols - 1) * 0.5f) * gap) + up * ((k / cols - (cols - 1) * 0.5f) * gap);
            foreach (var c in g.Constructs.Bodies.ToList())
            {
                var r = HullHoles.Bore(g.Constructs, c, from, fwd, radius, 1500f);
                made += r.Holed;
                refused += r.Refused;
                ms += r.Ms;
            }
        }
        WsSay($"bore: {count} lines of {radius:0.##} m, {made} holes made, {refused} refused{(refused > 0 ? $" ({HullHoles.LastRefusal})" : "")}, {ms:0.0} ms; {HullHoles.DefsMade} holed parts so far, {HullHoles.FacetsCut} facets and {HullHoles.BoxesCut} boxes cut");
        _wsWait = 3;
        return true;
    }
}
