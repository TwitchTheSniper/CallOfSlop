namespace CallOfSlop;

/// <summary>
/// The window tool's <c>logi</c> verbs (docs/sandbox/constructs/production.md "Smart logistics"): what the network did, as <c>[logi]</c>
/// lines, so a run is judged without pictures. On the body spawned by <c>--blueprint</c>. Either peer: <c>riders</c>, <c>why PART#n</c>,
/// <c>net PART#n</c>. Host: <c>order NAME [N]</c> (at the first Crafting Terminal), <c>cut PART#n WAY</c> (the conveyor out of a Router's
/// way 1 to 3), <c>fill PART#n [NAME]</c> (a Piece Rack filled with a part), <c>spawn N</c> (pieces put on every whole conveyor until N ride),
/// <c>perf</c> (the conveyor step's times since the last <c>perf</c>).
/// </summary>
public static partial class Program
{
    private static int _wsSpawnLeft, _wsSpawnTries;

    private static void WsLogi(Game g, string arg)
    {
        var words = arg.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var verb = words.Length > 0 ? words[0] : "riders";
        var rest = words.Length > 1 ? words[1] : "";
        var c = WsSpawned(g);
        if (c == null)
        {
            WsFail("logi: no spawned blueprint");
            return;
        }
        var w = g.Constructs;
        switch (verb)
        {
            case "riders":
                WsSay($"[logi] riders: {c.ConveyedCount}");
                for (var i = 0; i < c.ConveyedCount; i++)
                {
                    ref readonly var r = ref c.Conveyed[i];
                    var rt = r.Route < c.RouteCount ? c.Routes[r.Route] : default;
                    var path = r.Route < c.RouteCount && rt.From < c.PartCount && rt.To < c.PartCount ? $"{c.DefOf(rt.From).Name}#{rt.From} -> {c.DefOf(rt.To).Name}#{rt.To}" : "?";
                    var dest = ConstructLogistics.PartOfStore(c, r.Dest);
                    WsSay($"[logi]   {ConstructParts.Get(r.Def)?.Name} on {r.Route} ({path}) at {r.At:0.00}: dest {(dest < 0 ? "none" : $"{c.DefOf(dest).Name}#{dest}")} tag {r.Tag} flags {(LogiFlags)(r.Flags & 15)} reason {ConstructLogistics.ReasonOf(r)} - {ConstructLogistics.RiderNote(c, r) ?? "no note"}");
                }
                return;
            case "why":
            {
                var (fc, part) = WsFind(g, rest);
                if (fc == null)
                {
                    WsFail($"logi why: no part \"{rest}\"");
                    return;
                }
                WsSay($"[logi] why {rest}: {(fc.DefOf(part).IsRouter ? ConstructLogistics.Look(w, fc, part) : "not a Router")}; alarm {fc.Parts[part].Out:0} moving {fc.Parts[part].Out1:0}");
                for (var k = 0; k < fc.RouteCount; k++)
                    if (fc.Routes[k].Type == RouteType.Conveyor && fc.Routes[k].To == part && ConstructConveyor.JamText(fc, k, ConstructConveyor.Jam(w, fc, k)) is { } jam)
                        WsSay($"[logi]   its input {k}: {jam}");
                return;
            }
            case "net":
            {
                var (fc, part) = WsFind(g, rest);
                if (fc == null)
                {
                    WsFail($"logi net: no part \"{rest}\"");
                    return;
                }
                // The map built again from nothing, and every column of this Router's network, timed (the rebuild a link change costs).
                fc.LogiStamp++;
                var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                var m = ConstructLogistics.Map(fc);
                var build = System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
                var s = m.RouterSlot(part);
                var net = m.NetOf(part);
                t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                var cols = 0;
                for (var ds = 0; ds < m.DestN; ds++)
                    if (m.NodeNet[m.Dests[ds]] == net && ConstructLogistics.Column(fc, m, ds, force: true))
                        cols++;
                var colMs = System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
                WsSay($"[logi] net {rest}: {m.NodeN} nodes, {m.RouterN} routers, {m.DestN} destinations, routes {fc.RouteCount}; map built in {build:0.000} ms, {cols} columns in {colMs:0.000} ms");
                for (var ds = 0; ds < m.DestN && s >= 0; ds++)
                {
                    if (m.NodeNet[m.Dests[ds]] != net)
                        continue;
                    var at = s * LogiMap.MaxDests + ds;
                    int dp = m.NodePart[m.Dests[ds]];
                    WsSay($"[logi]   -> {fc.DefOf(dp).Name}#{dp}{(m.DestStore[ds] ? " (storage)" : "")}: {(m.Hop[at] == LogiMap.None ? "no way" : $"way {ConstructLogistics.WayOf(fc, m.Hop[at])} by conveyor {m.Hop[at]}, {m.Dist[at]:0.0} m, {m.Narrow[at] * ConstructConveyor.StepCm} cm")}");
                }
                return;
            }
            case "order" when g.IsServer:
            {
                var (tc, t) = WsFind(g, "Crafting Terminal");
                var cut = rest.LastIndexOf(' ');
                var n = cut > 0 && ushort.TryParse(rest[(cut + 1)..], out var cn) ? cn : (ushort)1;
                var name = cut > 0 && n > 0 && rest[(cut + 1)..] == n.ToString() ? rest[..cut] : rest;
                if (tc == null || ConstructParts.Find(name) is not { } pd)
                {
                    WsFail($"logi order: no terminal or no part \"{name}\"");
                    return;
                }
                var why = ConstructCrafting.Place(w, tc, t, new WorkOrder { Part = pd.Id, Count = n, Key = default });
                WsSay($"[logi] order {n} {name}: {why ?? "placed"}");
                return;
            }
            case "cut" when g.IsServer:
            {
                var cut = rest.LastIndexOf(' ');
                var (fc, part) = WsFind(g, cut > 0 ? rest[..cut] : rest);
                var way = cut > 0 && int.TryParse(rest[(cut + 1)..], out var wn) ? wn : 1;
                for (var k = 0; fc != null && k < fc.RouteCount; k++)
                    if (fc.Routes[k].Type == RouteType.Conveyor && fc.Routes[k].From == part && ConstructLogistics.WayOf(fc, k) == way)
                    {
                        ConstructRoutes.Damage(w, fc, k, 0, 1e9f);
                        WsSay($"[logi] cut conveyor {k} (way {way} of {fc.DefOf(part).Name}#{part})");
                        return;
                    }
                WsFail($"logi cut: no way {way} out of \"{rest}\"");
                return;
            }
            case "fill" when g.IsServer:
            {
                var cut = rest.IndexOf(' ');
                var (fc, part) = WsFind(g, cut > 0 ? rest[..cut] : rest);
                var what = ConstructParts.Find(cut > 0 ? rest[(cut + 1)..] : "Steel Plate");
                if (fc == null || what == null)
                {
                    WsFail($"logi fill: \"{rest}\"");
                    return;
                }
                var put = 0;
                while (put < 4000 && w.RackPut(fc, part, what))
                    put++;
                WsSay($"[logi] fill {fc.DefOf(part).Name}#{part}: {put} x {what.Name}");
                return;
            }
            case "spawn" when g.IsServer:
            {
                if (_wsSpawnLeft == 0)
                    _wsSpawnLeft = int.TryParse(rest, out var sn) ? Math.Max(1, sn) : 50;
                var piece = ConstructParts.Find("Gear Set")!;
                for (var k = 0; k < c.RouteCount && c.ConveyedCount < _wsSpawnLeft; k++)
                    if (c.Routes[k].Type == RouteType.Conveyor && c.Routes[k].FirstCut > c.Routes[k].Count && c.Routes[k].From < c.PartCount && !c.DefOf(c.Routes[k].From).IsConveyorJunction)
                        ConstructConveyor.PutMade(w, c, k, piece, 1, out _);
                if (c.ConveyedCount < _wsSpawnLeft && ++_wsSpawnTries < 300)
                {
                    _wsQueue.EnqueueFront("logi spawn");
                    _wsWait = 6;
                    return;
                }
                WsSay($"[logi] spawn: {c.ConveyedCount} riding");
                _wsSpawnLeft = _wsSpawnTries = 0;
                return;
            }
            case "perf":
                WsSay("[logi] perf: " + ConstructConveyor.ProbeReport());
                return;
        }
        WsFail($"logi {arg}: riders, why PART, net PART; host: order NAME N, cut PART WAY, fill PART [NAME], spawn N, perf");
    }
}
