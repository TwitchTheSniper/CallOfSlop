namespace CallOfSlop;

/// <summary>
/// The window tool's <c>logi</c> verbs (docs/sandbox/constructs/production.md "Smart logistics"): what the network did, as <c>[logi]</c>
/// lines, so a run is judged without pictures. On the body spawned by <c>--blueprint</c>. Either peer: <c>riders</c>, <c>why PART#n</c>,
/// <c>net PART#n</c>, <c>chain</c> (chained orders' links and the Assemblers' queues). Host: <c>remove PART</c>, <c>cancel ID</c> (a terminal order), <c>order NAME [N]</c> (at the first Crafting Terminal), <c>cut PART#n WAY</c> (the conveyor out of a Router's
/// way 1 to 3), <c>fill PART#n[|NAME]</c> (a Piece Rack filled with a part), <c>spawn N</c> (pieces put on every whole conveyor until
/// N ride). A part is also found by its index on that body: <c>Piece Rack@286</c>.
/// </summary>
public static partial class Program
{
    private static int _wsSpawnLeft, _wsSpawnTries;

    /// <summary>A part by name ("Piece Rack#2": the second), or by its index on the spawned body ("Piece Rack@286").</summary>
    private static (Construct? C, int Part) LogiFind(Game g, string spec)
    {
        var at = spec.LastIndexOf('@');
        if (at > 0 && int.TryParse(spec[(at + 1)..], out var idx) && (WsSpawned(g) ?? WsFind(g, "Conveyor Router").C) is { } c && idx >= 0 && idx < c.PartCount)
            return (c, idx);
        return WsFind(g, spec);
    }

    private static void WsLogi(Game g, string arg)
    {
        var words = arg.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var verb = words.Length > 0 ? words[0] : "riders";
        var rest = words.Length > 1 ? words[1] : "";
        var c = WsSpawned(g) ?? (WsFind(g, "Conveyor Router") is ({ } rc, _) ? rc : null); // (a joined client spawns nothing: the body with a Router)
        if (c == null)
        {
            WsFail("logi: no spawned blueprint and no Router");
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
                var (fc, part) = LogiFind(g, rest);
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
                var (fc, part) = LogiFind(g, rest);
                if (fc == null)
                {
                    WsFail($"logi net: no part \"{rest}\"");
                    return;
                }
                // The map built again from nothing, and every column of this Router's network, timed (the rebuild a link change costs).
                fc.LogiStamp++;
                if (fc.Logi != null)
                    fc.Logi.LinkHash = 0; // (built whole, as after a link change)
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
                var atStation = rest.StartsWith("station ", StringComparison.Ordinal); // ("logi order station NAME [N]": at the first Building Station, a chain root too)
                if (atStation)
                    rest = rest[8..];
                var (tc, t) = LogiFind(g, atStation ? "Building Station" : "Crafting Terminal");
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
                var (fc, part) = LogiFind(g, cut > 0 ? rest[..cut] : rest);
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
                var cut = rest.IndexOf('|');
                var (fc, part) = LogiFind(g, cut > 0 ? rest[..cut] : rest);
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
            case "chain":
                WsSay($"[logi] chain: {w.ChainCount} link(s)");
                for (var i = 0; i < w.ChainCount; i++)
                {
                    ref readonly var e = ref w.Chains[i];
                    WsSay($"[logi]   #{e.Id} {ConstructParts.Get(e.Kind)?.Name} {e.Made}/{e.Asked} for {(e.Dest < c.PartCount ? c.DefOf(e.Dest).Name : "?")}#{e.Dest} by {(e.Maker < c.PartCount ? $"{c.DefOf(e.Maker).Name}#{e.Maker}" : "nobody")} (root {e.Root} order {e.RootId}) {e.Reason}");
                }
                for (var i = 0; i < c.PartCount; i++)
                    if (c.Parts[i].Orders is { Bill: false, Count: > 0 } oq && c.DefOf(i).IsAssembler)
                        for (var k = 0; k < oq.Count; k++)
                            WsSay($"[logi]   queue {c.DefOf(i).Name}#{i}: {ConstructParts.Get(oq.Items[k].Part)?.Name} {oq.Items[k].Made}/{oq.Items[k].Count} chain {oq.Items[k].Chain}");
                for (var i = 0; i < c.PartCount; i++) // (the Orders rows' chain lines, as this peer has them: the server's, or a client's from CBodyKind.Craft)
                    if (c.Parts[i].Craft is { Count: > 0 } cq)
                        for (var k = 0; k < cq.Count; k++)
                        {
                            WsSay($"[logi]   row {c.DefOf(i).Name}#{i} order {cq.Items[k].Id}: {ConstructCrafting.NameOf(cq.Items[k].What)} {cq.Items[k].State} \"{cq.Items[k].Note}\"");
                            foreach (var l in cq.Items[k].Lines ?? [])
                                WsSay($"[logi]     |{ConstructLogisticsPlan.LineText(c, l)}");
                            if (cq.Items[k].LinesMore > 0)
                                WsSay($"[logi]     | and {cq.Items[k].LinesMore} more");
                        }
                return;
            case "tier": // a test state in this process: "logi tier Assembler 4" sets every part of that name to that tier (a two-level recipe on the showcase)
            {
                var sp = rest.LastIndexOf(' ');
                if (sp < 0 || ConstructParts.Find(rest[..sp]) is not { } td || !int.TryParse(rest[(sp + 1)..], out var tier))
                {
                    WsFail($"logi tier NAME N: \"{rest}\"");
                    return;
                }
                td.Tier = tier;
                WsSay($"[logi] {td.Name} is tier {tier} in this run");
                return;
            }
            case "remove" when g.IsServer: // a part off the body (a maker gone): "logi remove PART@n"
            {
                var (fc, part) = LogiFind(g, rest);
                if (fc == null)
                {
                    WsFail($"logi remove: \"{rest}\"");
                    return;
                }
                var name = fc.DefOf(part).Name;
                w.RemovePart(fc, part);
                WsSay($"[logi] removed {name}#{part}");
                return;
            }
            case "cancel" when g.IsServer: // "logi cancel ID": a terminal order cancelled
            {
                var (tc, t) = WsFind(g, "Crafting Terminal");
                WsSay($"[logi] cancel {rest}: {(tc == null ? "no terminal" : ConstructCrafting.Cancel(w, tc, t, ushort.TryParse(rest, out var cid) ? cid : (ushort)1) ?? "cancelled")}");
                return;
            }
        }
        WsFail($"logi {arg}: riders, why PART, net PART; host: order NAME N, cut PART WAY, fill PART[|NAME], spawn N");
    }
}
