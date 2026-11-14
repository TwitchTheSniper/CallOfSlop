namespace CallOfSlop;

/// <summary>
/// The window tool's <c>logi</c> verbs (docs/sandbox/constructs/production.md "Smart logistics"): what the network did, as <c>[logi]</c>
/// lines, so a run is judged without pictures. On the body spawned by <c>--blueprint</c>. Either peer: <c>riders</c>, <c>why PART#n</c>,
/// <c>net PART#n</c>, <c>chain</c> (chained orders' links, the Assemblers' queues and the Orders rows' chain lines), <c>tier NAME N</c> (a part's tier in
/// this run: a two-level recipe on the showcase). Host: <c>remove PART</c>, <c>cancel ID</c> (a terminal order), <c>order [station] NAME [N]</c> (at the first Crafting Terminal, or Building Station), <c>cut PART#n WAY</c> (the conveyor out of a Router's
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
        var c = WsSpawned(g) ?? (WsFind(g, "Conveyor Router") is ({ } rc, _) ? rc : WsFind(g, "Crafting Terminal") is ({ } ctc, _) ? ctc : null); // (a joined client spawns nothing: the body with a Router, or a terminal)
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
                WsSay($"[logi] net {rest}: {m.NodeN} nodes, {m.RouterN} routers, {m.DestN} destinations, {m.LiftN} lifter edges, routes {fc.RouteCount}; map built in {build:0.000} ms, {cols} columns in {colMs:0.000} ms");
                for (var ds = 0; ds < m.DestN && s >= 0; ds++)
                {
                    if (m.NodeNet[m.Dests[ds]] != net)
                        continue;
                    var at = s * LogiMap.MaxDests + ds;
                    int dp = m.NodePart[m.Dests[ds]];
                    WsSay($"[logi]   -> {fc.DefOf(dp).Name}#{dp}{(m.DestStore[ds] ? " (storage)" : "")}: {(m.Hop[at] == LogiMap.None ? "no way" : $"way {ConstructLogistics.WayOf(fc, m.Hop[at])} by conveyor {m.Hop[at]}, {m.Dist[at]:0.0} m, {m.Narrow[at] * ConstructConveyor.StepCm} cm{(m.ViaLift[at] ? ", by a lifter" : "")}")}");
                }
                return;
            }
            case "cost" when g.IsServer: // "logi cost [N]": the planner's 2 Hz step (terminals, chains, stock targets) run N times and the network board's list filled N times: time and bytes each (phase 5)
            {
                var n = int.TryParse(rest, out var cn) && cn > 0 ? cn : 200;
                var net = (0, 0, 0);
                for (var i = 0; i < c.PartCount; i++)
                    if (c.DefOf(i).IsCraftTerminal)
                    {
                        var cnet = ConstructCrafting.NetOf(w, c, i);
                        net = (Math.Max(net.Item1, cnet.MachineN), Math.Max(net.Item2, cnet.StoreN), net.Item3 + 1);
                    }
                ConstructCrafting.Step(w); // (warm: a first step may make its lines)
                long worstTicks = 0;
                var a0 = GC.GetAllocatedBytesForCurrentThread();
                var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                for (var k = 0; k < n; k++)
                {
                    w.CraftTicks = ConstructCrafting.StepEvery - 1;
                    var s0 = System.Diagnostics.Stopwatch.GetTimestamp();
                    ConstructCrafting.Step(w);
                    worstTicks = Math.Max(worstTicks, System.Diagnostics.Stopwatch.GetTimestamp() - s0);
                }
                var us = System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds * 1000 / n;
                var bytes = (GC.GetAllocatedBytesForCurrentThread() - a0) / n;
                WsSay($"[logi] cost: planner step {us:0.0} us mean, {worstTicks * 1e6 / System.Diagnostics.Stopwatch.Frequency:0.0} us worst, {bytes} bytes a step over {n}; {w.ChainCount} links, {w.CraftOpen.Count} bodies with orders, {w.StockTargetN} stock targets; {net.Item3} terminals, biggest network {net.Item1} machines {net.Item2} stores; {c.ConveyedCount} riders");
                var (bc, bp) = WsFind(g, "Conveyor Router");
                if (bc != null)
                {
                    var card = new PanelCard { Rows = 16, TextRows = 16, Cols = 64 };
                    ProductionLines.FillMonitor(w, bc, bp, card);
                    a0 = GC.GetAllocatedBytesForCurrentThread();
                    t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                    for (var k = 0; k < n; k++)
                    {
                        card.Clear();
                        ProductionLines.FillMonitor(w, bc, bp, card);
                    }
                    WsSay($"[logi] cost: board list fill (16 rows) {System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds * 1000 / n:0.0} us, {(GC.GetAllocatedBytesForCurrentThread() - a0) / n} bytes a fill");
                }
                return;
            }
            case "steps" when g.IsServer: // "logi steps [N]": the conveyor step (riders, Routers' passes) run N times as it is now: time and bytes a step, riders and stuck ones (fix-ups 2)
            {
                var n = int.TryParse(rest, out var sn) && sn > 0 ? sn : 100;
                int riders0 = 0, stuck0 = 0;
                foreach (var b in w.Bodies)
                {
                    riders0 += b.ConveyedCount;
                    for (var i = 0; i < b.ConveyedCount; i++)
                        if (((LogiFlags)b.Conveyed[i].Flags & LogiFlags.Stuck) != 0)
                            stuck0++;
                }
                w.ConveyorTicks = ConstructConveyor.Every - 1;
                ConstructConveyor.Step(w, World.Query); // (warm)
                long worstTicks = 0;
                var a0 = GC.GetAllocatedBytesForCurrentThread();
                var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                for (var k = 0; k < n; k++)
                {
                    w.ConveyorTicks = ConstructConveyor.Every - 1;
                    var s0 = System.Diagnostics.Stopwatch.GetTimestamp();
                    ConstructConveyor.Step(w, World.Query);
                    worstTicks = Math.Max(worstTicks, System.Diagnostics.Stopwatch.GetTimestamp() - s0);
                }
                var us = System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds * 1000 / n;
                WsSay($"[logi] steps: conveyor step {us:0.0} us mean, {worstTicks * 1e6 / System.Diagnostics.Stopwatch.Frequency:0.0} us worst, {(GC.GetAllocatedBytesForCurrentThread() - a0) / n} bytes a step over {n}; {riders0} riders ({stuck0} stuck) before, {c.ConveyedCount} on this body after");
                return;
            }
            case "cfg": // "logi cfg PART|key|value": a setting through this peer's own configure message, as the configure tool sends it (stand in reach first)
            {
                var bits = rest.Split('|');
                var (fc, part) = bits.Length == 3 ? LogiFind(g, bits[0]) : (null, -1);
                var key = CfgKey.None;
                for (var k = CfgKey.None + 1; k < CfgKey.COUNT; k++)
                    if (ConstructConfig.KeyName(k) == (bits.Length == 3 ? bits[1] : ""))
                        key = k;
                if (fc == null || key == CfgKey.None || fc.DefOf(part).CfgSlot[(int)key] < 0 || !float.TryParse(bits[2], System.Globalization.CultureInfo.InvariantCulture, out var v))
                {
                    WsFail($"logi cfg: \"{rest}\" (PART|key|value, a key the part has)");
                    return;
                }
                g.SandboxConfigure(fc.Id, [(ushort)part], [(byte)fc.DefOf(part).CfgSlot[(int)key]], [v]);
                WsSay($"[logi] cfg sent {fc.DefOf(part).Name}#{part} {bits[1]} = {v}");
                return;
            }
            case "get": // "logi get PART|key": a setting as this peer holds it
            {
                var bits = rest.Split('|');
                var (fc, part) = bits.Length == 2 ? LogiFind(g, bits[0]) : (null, -1);
                for (var k = CfgKey.None + 1; k < CfgKey.COUNT && fc != null; k++)
                    if (ConstructConfig.KeyName(k) == bits[1])
                        WsSay($"[logi] get {fc.DefOf(part).Name}#{part} {bits[1]} = {ConstructConfig.Get(fc.Parts[part], fc.DefOf(part), k, -1)}");
                return;
            }
            case "send": // "logi send PART@N NAME [N]": an order through this peer's own message, as the terminal's Order button sends it (stand in reach first: the host's "bring")
            {
                var sp = rest.IndexOf(' ');
                var (tc, t) = sp > 0 ? LogiFind(g, rest[..sp]) : (null, -1);
                var tail = sp > 0 ? rest[(sp + 1)..] : "";
                var cut = tail.LastIndexOf(' ');
                var n = cut > 0 && ushort.TryParse(tail[(cut + 1)..], out var cn) ? cn : (ushort)1;
                var name = cut > 0 && tail[(cut + 1)..] == n.ToString() ? tail[..cut] : tail;
                if (tc == null || ConstructParts.Find(name) is not { } pd)
                {
                    WsFail($"logi send: no terminal or no part in \"{rest}\"");
                    return;
                }
                g.SandboxCraftOrder(tc.Id, t, ConstructStation.OrderFor(pd, n));
                WsSay($"[logi] send {n} {name} at {tc.DefOf(t).Name}#{t}");
                return;
            }
            case "order" when g.IsServer:
            {
                var atStation = rest.StartsWith("station ", StringComparison.Ordinal); // ("logi order station NAME [N]": at the first Building Station, a chain root too)
                if (atStation)
                    rest = rest[8..];
                var spec = atStation ? "Building Station" : "Crafting Terminal";
                if (rest.StartsWith('@') && rest.IndexOf(' ') is var sp and > 1) // ("logi order @N NAME [N]": at part N, a second terminal)
                    (spec, rest) = ("x" + rest[..sp], rest[(sp + 1)..]);
                var (tc, t) = LogiFind(g, spec);
                var cut = rest.LastIndexOf(' ');
                var n = cut > 0 && ushort.TryParse(rest[(cut + 1)..], out var cn) ? cn : (ushort)1;
                var name = cut > 0 && n > 0 && rest[(cut + 1)..] == n.ToString() ? rest[..cut] : rest;
                if (tc == null || ConstructParts.Find(name) is not { } pd)
                {
                    WsFail($"logi order: no terminal or no part \"{name}\"");
                    return;
                }
                var why = ConstructCrafting.Place(w, tc, t, ConstructStation.OrderFor(pd, n)); // (a piece, a panel or a pressed part by its key; a catalogue part by its id)
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
            case "feed" when g.IsServer: // "logi feed N [NAME]": N pieces (Gear Set) with no order put on the conveyors into a Router, one at a time as room comes
            {
                var fb = rest.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                var left = fb.Length > 0 && int.TryParse(fb[0], out var fn) ? fn : 1;
                var piece = ConstructParts.Find(fb.Length > 1 ? fb[1] : "Gear Set");
                if (piece == null)
                {
                    WsFail($"logi feed: no part \"{fb[1]}\"");
                    return;
                }
                for (var k = 0; k < c.RouteCount && left > 0; k++)
                    if (c.Routes[k].Type == RouteType.Conveyor && c.Routes[k].To < c.PartCount && c.DefOf(c.Routes[k].To).IsRouter
                        && ConstructConveyor.PutMade(w, c, k, piece, 1, out var fwhy) && fwhy == ExtrudeStall.None)
                        left--;
                if (left > 0 && ++_wsSpawnTries < 300)
                {
                    _wsQueue.EnqueueFront($"logi feed {left} {piece.Name}");
                    _wsWait = 30;
                    return;
                }
                _wsSpawnTries = 0;
                WsSay($"[logi] feed: {(left > 0 ? $"{left} not put (no room)" : "all put")}; {c.ConveyedCount} riding");
                return;
            }
            case "mend" when g.IsServer: // "logi mend PART WAY": a cut way repaired whole, as the repair tool leaves it
            {
                var cut = rest.LastIndexOf(' ');
                var (fc, part) = LogiFind(g, cut > 0 ? rest[..cut] : rest);
                var way = cut > 0 && int.TryParse(rest[(cut + 1)..], out var wn) ? wn : 1;
                for (var k = 0; fc != null && k < fc.RouteCount; k++)
                    if (fc.Routes[k].Type == RouteType.Conveyor && fc.Routes[k].From == part && ConstructLogistics.WayOf(fc, k) == way)
                    {
                        var ok = ConstructRoutes.Repair(w, fc, k);
                        WsSay($"[logi] mend conveyor {k} (way {way} of {fc.DefOf(part).Name}#{part}): {(ok ? "repaired" : "nothing to repair")}");
                        return;
                    }
                WsFail($"logi mend: no way {way} out of \"{rest}\"");
                return;
            }
            case "stow" when g.IsServer: // "logi stow PART|NAME": one part into a Storage Volume's crates (stock a terminal may fetch: fix-ups 2)
            {
                var sb = rest.Split('|');
                var (sc, part) = LogiFind(g, sb[0]);
                if (sc == null || sb.Length < 2 || ConstructParts.Find(sb[1]) is not { } sd)
                {
                    WsFail($"logi stow: \"{rest}\"");
                    return;
                }
                WsSay($"[logi] stow {sd.Name} in {sc.DefOf(part).Name}#{part}: {w.PutPart(sc, part, sd) ?? "stowed"}");
                return;
            }
            case "haul" when g.IsServer: // "logi haul PART|N": N loose one-part bodies within 6 m of a part taken out of the world (hauled away, as a player would; fix-ups 2's pile)
            {
                var hb = rest.Split('|');
                var (hc, part) = LogiFind(g, hb[0]);
                var most = hb.Length > 1 && int.TryParse(hb[1], out var hn) ? hn : 99;
                if (hc == null)
                {
                    WsFail($"logi haul: \"{rest}\"");
                    return;
                }
                hc.WorldFrame(hc.Parts[part].Seg, out var hq, out var ht);
                var near = ht + System.Numerics.Vector3.Transform(hc.Parts[part].Pos, hq);
                var gone = 0;
                for (var bi = w.Bodies.Count - 1; bi >= 0 && gone < most; bi--)
                    if (w.Bodies[bi] is { PartCount: 1 } lb && lb != hc && System.Numerics.Vector3.Distance(lb.WorldCom, near) < 6f)
                    {
                        w.Remove(lb);
                        gone++;
                    }
                WsSay($"[logi] haul: {gone} loose bodies taken from near {hc.DefOf(part).Name}#{part}");
                return;
            }
            case "mix" when g.IsServer: // "logi mix PART|MATERIAL|N": N units of a material into an Assembler's mixed input (a recipe's second material)
            {
                var mb = rest.Split('|');
                var (mc, part) = LogiFind(g, mb[0]);
                var mat = mb.Length > 1 ? RecyclingTable.Current.IdOf(mb[1]) : (byte)0;
                if (mc == null || mat == 0 || w.StoreOf(mc, part) is not { } ms)
                {
                    WsFail($"logi mix: \"{rest}\"");
                    return;
                }
                ms.AddMix(mat, mb.Length > 2 && int.TryParse(mb[2], out var mn) ? mn : 20);
                ms.Version++;
                w.StoreDirty.Add(ms.Handle);
                WsSay($"[logi] mix {mc.DefOf(part).Name}#{part}: {ms.MixOf(mat)} {mb[1]}");
                return;
            }
            case "fill" when g.IsServer:
            {
                var fb = rest.Split('|'); // ("PART|NAME|N": at most N)
                var (fc, part) = LogiFind(g, fb[0]);
                var what = ConstructParts.Find(fb.Length > 1 ? fb[1] : "Steel Plate");
                var most = fb.Length > 2 && int.TryParse(fb[2], out var fm) ? fm : 4000;
                if (fc == null || what == null)
                {
                    WsFail($"logi fill: \"{rest}\"");
                    return;
                }
                var put = 0;
                while (put < most && w.RackPut(fc, part, what))
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
                    if (c.Parts[i].Orders is { Bill: false, Count: > 0 } oq && WorkOrders.TakesOrders(c.DefOf(i)))
                        for (var k = 0; k < oq.Count; k++)
                            WsSay($"[logi]   queue {c.DefOf(i).Name}#{i} ({(w.StoreOf(c, i) is { } qst ? ProductionLines.Raw(w, c, i, qst) : null)}, stall {w.StoreOf(c, i)?.Refused}): {ConstructCrafting.NameOf(oq.Items[k])} {oq.Items[k].Made}/{oq.Items[k].Count} chain {oq.Items[k].Chain} rank {oq.Items[k].Rank}");
                for (var i = 0; i < c.PartCount; i++) // (a press's bays and stall: Press phase 4's chained blanks)
                    if (c.DefOf(i) is { IsPress: true, IsLathe: false } pdef && w.StoreOf(c, i) is { } pst)
                    {
                        var bays = string.Join(", ", Enumerable.Range(0, pdef.PressBays.Length).Select(b => ConstructPress.PieceIn(c, i, b) is var pi and >= 0 ? c.DefOf(pi).Name : "-"));
                        WsSay($"[logi]   press {pdef.Name}#{i}: line {ProductionLines.Raw(w, c, i, pst)}; {ConstructPress.StallText((PressStall)pst.Refused)}; bays [{bays}]; refused {pst.Blocked / 4} at bay {pst.Blocked % 4 + 1}; queue {(c.Parts[i].Orders is { } pq ? pq.Count.ToString() : "none")}");
                    }
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
            case "name": // "logi name PART = TEXT": a part's text (a Router's name) set as the configure tool's Text box sets it, through this peer's own message
            {
                var sp = rest.IndexOf(" = ", StringComparison.Ordinal);
                var (nc, np) = LogiFind(g, sp > 0 ? rest[..sp] : rest);
                if (nc == null)
                {
                    WsFail($"logi name: no part \"{rest}\"");
                    return;
                }
                g.SandboxPartText(nc.Id, (ushort)np, 0, sp > 0 ? rest[(sp + 3)..] : "");
                WsSay($"[logi] name {nc.DefOf(np).Name}#{np}: sent \"{(sp > 0 ? rest[(sp + 3)..] : "")}\"");
                return;
            }
            case "list": // "logi list [FILTER]": the first Crafting Terminal's list rows (name, make 0-3, its machine, what is missing), filtered by name
            {
                var (tc, t) = LogiFind(g, "Crafting Terminal");
                if (tc == null)
                {
                    WsFail("logi list: no Crafting Terminal");
                    return;
                }
                var rows = new List<CraftRow>();
                ConstructCrafting.Catalogue(w, tc, t, rows);
                var shown = rows.Where(r => rest.Length == 0 || r.Name.Contains(rest, StringComparison.OrdinalIgnoreCase)).ToList();
                WsSay($"[logi] list: {rows.Count} rows, {shown.Count} shown");
                foreach (var r in shown)
                    WsSay($"[logi]   {r.Name}: make {r.Make} stock {r.Stock} by {(r.Machine >= 0 ? $"{tc.DefOf(r.Machine).Name}#{r.Machine}" : "-")} {r.Missing}");
                return;
            }
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
            case "set" when g.IsServer: // "logi set PART|key|value": a setting as the configure tool sets it (phase 3: storeKeep, routerSpill ...)
            {
                var bits = rest.Split('|');
                var (fc, part) = bits.Length == 3 ? LogiFind(g, bits[0]) : (null, -1);
                var key = CfgKey.None;
                for (var k = CfgKey.None + 1; k < CfgKey.COUNT; k++)
                    if (ConstructConfig.KeyName(k) == (bits.Length == 3 ? bits[1] : ""))
                        key = k;
                if (fc == null || key == CfgKey.None || !ConstructConfig.Has(fc.DefOf(part), key) || !float.TryParse(bits[2], System.Globalization.CultureInfo.InvariantCulture, out var v))
                {
                    WsFail($"logi set: \"{rest}\" (PART|key|value, a key the part has)");
                    return;
                }
                ConstructConfig.Put(ref fc.Parts[part], fc.DefOf(part), key, v);
                w.StructureDirty.Add(fc.Id);
                WsSay($"[logi] set {fc.DefOf(part).Name}#{part} {bits[1]} = {ConstructConfig.Get(fc.Parts[part], fc.DefOf(part), key, -1)}");
                return;
            }
            case "take" when g.IsServer: // "logi take PART|NAME|N": N pieces of a kind taken out of a rack by hand
            {
                var bits = rest.Split('|');
                var (fc, part) = bits.Length >= 2 ? LogiFind(g, bits[0]) : (null, -1);
                var what = bits.Length >= 2 ? ConstructParts.Find(bits[1]) : null;
                if (fc == null || what == null || w.StoreOf(fc, part) is not { } tst)
                {
                    WsFail($"logi take: \"{rest}\"");
                    return;
                }
                var want = bits.Length > 2 && int.TryParse(bits[2], out var tn) ? tn : 1;
                var took = 0;
                while (took < want && ConstructCrafting.TakeOne(w, fc, part, tst, what))
                    took++;
                WsSay($"[logi] took {took} {what.Name} from {fc.DefOf(part).Name}#{part}: {ConstructCrafting.Holds(tst, (ushort)what.Id)} left");
                return;
            }
            case "targets": // the stock targets (server) and each target rack's have / keep, and its open refill
            {
                WsSay($"[logi] targets: {w.StockTargetN}");
                for (var i = 0; i < c.PartCount; i++) // (either peer: each rack's Logistics look, from its settings)
                    if (StockTargets.IsStore(c.DefOf(i)) && StockTargets.Look(c, i) is { Length: > 0 } look)
                        WsSay($"[logi]   look {c.DefOf(i).Name}#{i}: {look}");
                for (var i = 0; i < w.StockTargetN; i++)
                {
                    ref readonly var t = ref w.StockTargets[i];
                    var tp = ConstructLogistics.PartOfStore(c, t.Store);
                    var have = tp >= 0 && w.StoreOf(c, tp) is { } tst ? ConstructCrafting.Holds(tst, t.Kind) : -1;
                    var refill = tp >= 0 && ConstructLogisticsPlan.Refill(w, c, tp, t.Kind, out var e)
                        ? $"refill {e.Made}/{e.Asked} by {(e.Maker < c.PartCount ? $"{c.DefOf(e.Maker).Name}#{e.Maker}" : "nobody")} {e.Reason}" : "no refill";
                    WsSay($"[logi]   {(tp >= 0 ? c.DefOf(tp).Name : "?")}#{tp} keeps {(tp >= 0 ? StockTargets.Keep(c, tp) : 0)} {ConstructParts.Get(t.Kind)?.Name}: has {have}; {refill}");
                }
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
