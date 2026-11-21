using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The window tool's verb for bores (docs/sandbox/design/capital-battles.md): <c>bore PART[#n] [N] [SIDE]</c> (host) fires N railgun
/// slugs (the Zero Day Railgun's damage and budget) at that part from 80 m out on SIDE (x, -x, y, -y, z or -z of its body; x by
/// default), a metre or two apart, through the server's own bore (<see cref="ConstructBores.Through"/>): its damage, spall, the
/// holes on every PC and the break-up. It says how far each went and how many parts it went through.
/// </summary>
public static unsafe partial class Program
{
    private static bool WsBoreKnown(string action) => action.Split(' ', 2)[0].ToLowerInvariant() is "bore";

    private static bool WsBoreVerb(Game g, string verb, string arg)
    {
        if (verb != "bore")
            return false;
        if (!g.IsServer)
        {
            WsFail("bore: host only");
            return true;
        }
        var words = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        var side = "x";
        if (words.Count > 1 && words[^1] is "x" or "-x" or "y" or "-y" or "z" or "-z")
        {
            side = words[^1];
            words.RemoveAt(words.Count - 1);
        }
        var n = 1;
        if (words.Count > 1 && int.TryParse(words[^1], out var nn))
        {
            n = Math.Clamp(nn, 1, 200);
            words.RemoveAt(words.Count - 1);
        }
        var name = string.Join(' ', words);
        var (c, part) = WsFind(g, name);
        if (c == null || part < 0)
        {
            WsFail($"bore: no part \"{name}\"");
            return true;
        }
        var gun = ConstructParts.Find("Zero Day Railgun");
        var damage = gun?.GunDamage ?? 900f;
        var budget = gun?.GunPierce is > 0 ? gun.GunPierce : 3000f;
        var axis = side.TrimStart('-') switch { "y" => Vector3.UnitY, "z" => Vector3.UnitZ, _ => Vector3.UnitX } * (side[0] == '-' ? -1 : 1);
        var dir = -Vector3.Transform(axis, c.Rot);
        var right = Vector3.Normalize(Vector3.Cross(dir, MathF.Abs(dir.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX));
        var up = Vector3.Cross(right, dir);
        var at = ConstructRender.PartCentre(c, part);
        var w = g.Constructs;
        for (var k = 0; k < n; k++)
        {
            var off = right * ((k % 5) - 2) * 1.5f + up * ((k / 5 % 5) - 2) * 1.5f;
            var from = at + off - dir * 80f;
            if (!w.Raycast(from, dir, 200f, out var hit))
            {
                WsSay($"bore {k}: missed");
                continue;
            }
            var before = ConstructBores.Bored;
            ConstructBores.Through(w, 0, hit, dir, damage, budget, 1100f, 1100f, 0f, (byte)g.LocalId, 0, out var end);
            WsSay($"bore {k}: in at {hit.Body.DefOf(hit.Part).Name}, through {ConstructBores.Bored - before} part(s), {Vector3.Distance(hit.Point, end):0.0} m, entering at {hit.Point.X.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)},{hit.Point.Y.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)},{hit.Point.Z.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)} (for view X,Y,Z | ...)");
        }
        return true;
    }
}
