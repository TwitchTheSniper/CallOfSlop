using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The window tool's verb for an Extruder's expansion pieces (ConstructExtruderGrowth.cs): <c>extr PART[#n] [W H L]</c> prints the machine's
/// die and longest piece as this peer has them, and with sizes (cm) what an order of a solid bar that size meets (WorkOrders.Refusal, as a
/// terminal's order does); <c>extr build NAME X Z</c> (host) stands a part on the ground X, Z metres from the player as a body of its own;
/// <c>extr queue PART[#n] W H L</c> (host) queues that order; <c>extr shoot PART[#n]</c> (host) wrecks the part as a hit does; <c>extr made</c>
/// prints the loose extruded pieces this peer has.
/// </summary>
public static unsafe partial class Program
{
    private static bool WsExtrVerb(Game g, Player me, string verb, string arg)
    {
        if (verb != "extr")
            return false;
        var w = g.Constructs;
        var sub = arg.Split(' ', 2, StringSplitOptions.TrimEntries);
        var rest = sub.Length > 1 ? sub[1] : "";
        switch (sub[0])
        {
            case "build" when g.IsServer:
            {
                var bits = rest.Split(' ');
                if (bits.Length < 3 || ConstructParts.Find(string.Join(' ', bits[..^2])) is not { } pd
                    || !float.TryParse(bits[^2], System.Globalization.CultureInfo.InvariantCulture, out var x) || !float.TryParse(bits[^1], System.Globalization.CultureInfo.InvariantCulture, out var z))
                {
                    WsFail("extr build NAME X Z (host)");
                    return true;
                }
                var at = me.Position + new Vector3(x, 0, z);
                at.Y = World.Map.GroundHeight(at.X, at.Z);
                var body = ConstructBuild.PlaceNew(w, pd, at, 0);
                WsSay($"extr: built {pd.Name}: {(body == null ? "refused" : $"body {body.Id}")}");
                _wsWait = 3;
                return true;
            }
            case "queue" when g.IsServer:
            case "shoot" when g.IsServer:
            {
                var (c, part) = Spec(rest, out var o);
                if (c == null)
                    return true;
                if (sub[0] == "shoot")
                {
                    var name = c.DefOf(part).Name;
                    w.DamagePart(c, part, 1e9f, out _, raw: true);
                    WsSay($"extr: shot {name}#{part}");
                }
                else
                    WsSay($"extr: queue {o.Key.W}x{o.Key.H}x{o.Key.L} at {c.DefOf(part).Name}: {WorkOrders.Queue(w, c, part, o) ?? "queued"}");
                _wsWait = 3;
                return true;
            }
            case "set" when g.IsServer: // "extr set PART[#n]|key|value": a setting as the configure tool sets it
            {
                var bits = rest.Split('|');
                var (fc, part) = bits.Length == 3 ? WsFind(g, bits[0]) : (null, -1);
                var key = CfgKey.None;
                for (var k = CfgKey.None + 1; k < CfgKey.COUNT; k++)
                    if (bits.Length == 3 && ConstructConfig.KeyName(k) == bits[1])
                        key = k;
                if (fc == null || key == CfgKey.None || !float.TryParse(bits[2], System.Globalization.CultureInfo.InvariantCulture, out var v))
                {
                    WsFail($"extr set: \"{rest}\"");
                    return true;
                }
                ConstructConfig.Put(ref fc.Parts[part], fc.DefOf(part), key, v);
                w.StructureDirty.Add(fc.Id);
                WsSay($"extr: set {fc.DefOf(part).Name} {bits[1]} = {ConstructConfig.Get(fc.Parts[part], fc.DefOf(part), key, -1)}");
                return true;
            }
            case "made":
            {
                var n = 0;
                foreach (var b in w.Bodies)
                    if (b.PartCount == 1 && b.DefOf(0) is { IsExtrusion: true } pd)
                        WsSay($"extr: made #{++n} {Extrusion.Label(pd.ExKey)} {pd.ExKey.W}x{pd.ExKey.H}x{pd.ExKey.L} cm");
                if (n == 0)
                    WsSay("extr: made none");
                return true;
            }
            default:
            {
                var (c, part) = Spec(arg, out var o);
                if (c == null)
                    return true;
                var d = c.DefOf(part);
                ConstructExtruder.Reach(c, part, out var centre, out var die, out var longest);
                var product = ConstructExtruder.ProductOf(c, part);
                WsSay(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                    $"extr: {(g.IsServer ? "host" : "client")} {d.Name}#{part}: die {die.X:0.##} x {die.Y:0.##} m at {centre.X:0.##},{centre.Y:0.##}; longest {longest:0.#} m; parts {c.PartCount}; growing {(product >= 0 ? c.DefOf(product).ExKey.L + " cm" : "none")}"));
                var port = Array.Find(d.NamedPorts, x => x.Name == "Piece out");
                WsSay($"extr: status{(w.StoreOf(c, part) is { } est ? ConstructExtruder.Status(c, part, est) : " none")}; Piece out opens {port.Opening.X:0.##} x {port.Opening.Y:0.##} m; last blocked by: {(ConstructExtruder.LastBlockBody == c.Id && ConstructExtruder.LastBlockPart == part ? ConstructExtruder.LastBlock : "nothing now")}");
                if (o.Key.W > 0)
                    WsSay($"extr: order {o.Key.W}x{o.Key.H}x{o.Key.L}: {WorkOrders.Refusal(w, c, part, o) ?? "takes it"}");
                return true;
            }
        }

        (Construct? C, int Part) Spec(string s, out WorkOrder o)
        {
            o = new WorkOrder { Part = ConstructParts.None, Count = 1 };
            var bits = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var sizes = bits.Length >= 4 && bits[^3..].All(b => ushort.TryParse(b, out _));
            var (c, part) = WsFind(g, string.Join(' ', sizes ? bits[..^3] : bits));
            if (c == null)
            {
                WsFail($"extr: no part \"{s}\"");
                return (null, -1);
            }
            if (sizes)
            {
                var mat = w.StoreOf(c, part) is { Amount: > 0 } st ? st.Material : RecyclingTable.Current.IdOf("metal");
                o.Key = new ExtrusionKey(ulong.MaxValue, ushort.Parse(bits[^3]), ushort.Parse(bits[^2]), ushort.Parse(bits[^1]), mat, false, ExShape.Section);
            }
            return (c, part);
        }
    }
}
