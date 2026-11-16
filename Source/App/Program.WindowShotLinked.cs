namespace CallOfSlop;

/// <summary>
/// The window tool's <c>linked</c> verb (docs/sandbox/design/linked-logistics.md): <c>linked</c> prints every linked logistics part as this
/// peer has it (its status lines, a port's manifest: on a client, what CBodyKind.Link said); <c>linked pair</c> types the first
/// Transmitter's code into every Receiver's panel (the panel's own CActKind.Link); <c>linked fill N PART</c> (host, a server shortcut)
/// puts N parts into every Storage Volume of the bodies with a Receiver; <c>linked order N PART</c> orders at the first Crafting Terminal
/// of a body with a Transmitter (host: a server shortcut, the panel's order without its reach; a client: the panel's own message); <c>linked open PART[#n]</c> opens that part's panel as the use
/// key does; <c>linked route A B</c> gives the first Route Computer a route between the first two Cargo Docks it knows and starts it.
/// </summary>
public static unsafe partial class Program
{
    private static void WsLinked(Game g, string arg)
    {
        var words = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var verb = words.Length > 0 ? words[0] : "";
        var w = g.Constructs;
        (Construct C, int Part) First(Func<ConstructPartDef, bool> f)
        {
            foreach (var c in w.Bodies)
                for (var i = 0; i < c.PartCount; i++)
                    if (f(c.DefOf(i)))
                        return (c, i);
            return (null!, -1);
        }
        switch (verb)
        {
            case "":
                foreach (var c in w.Bodies)
                    for (var i = 0; i < c.PartCount; i++)
                        if (c.DefOf(i).LinkKind != LinkKind.None)
                            WsSay($"linked: body {c.Id} {c.DefOf(i).Name} {i}: {(c.Parts[i].Link is { } lp ? $"\"{lp.Name}\" {lp.Status.Replace('\n', '|')}; offers {lp.Offers.Length}; manifest {lp.Manifest.Sum(m => m.Aboard)}" : "no state heard")}");
                foreach (var c in w.Bodies)
                    if (LinkRoutes.RouteOf(c) is { } rr)
                        WsSay($"linked: route ship {c.Id} at {c.WorldCom:0.0} vel {c.Vel.Length():0.0} held by {c.CarriedBy}, AP {ConstructFlight.ApText(c)}; stop {rr.At + 1} {rr.Phase}: {rr.Why}{(w.Get(rr.Stops[rr.At].Body) is { } db && rr.Stops[rr.At].Part < db.PartCount ? $"; its dock at {LinkDocks.PartAt(db, rr.Stops[rr.At].Part):0.0}" : "")}");
                if (g.IsServer)
                    foreach (var l in LinkLedger.Lines)
                        WsSay($"linked: line {l.Id} {l.State} {ConstructParts.Get(l.Kind)?.Name} {l.Count}: ready {l.Ready} loaded {l.Loaded} delivered {l.Delivered} lost {l.Lost} taken {l.Taken} \"{l.Note}\"");
                return;
            case "pair":
            {
                var (tc, tp) = First(d => d.LinkKind == LinkKind.Transmitter);
                if (tc == null || tc.Parts[tp].Link is not { Id: not 0 } tl)
                {
                    WsFail("linked pair: no Transmitter with a code yet");
                    return;
                }
                foreach (var c in w.Bodies)
                    for (var i = 0; i < c.PartCount; i++)
                        if (c.DefOf(i).LinkKind == LinkKind.Receiver)
                            g.SandboxLink(c.Id, i, LinkOp.Pair, text: LinkedLogistics.Code(tl.Id));
                WsSay($"linked: paired every Receiver with {LinkedLogistics.Code(tl.Id)}");
                return;
            }
            case "fill" when words.Length >= 3 && g.IsServer && int.TryParse(words[1], out var n) && ConstructParts.Find(string.Join(' ', words.Skip(2))) is { } d:
                foreach (var c in w.Bodies)
                {
                    var works = false;
                    for (var i = 0; i < c.PartCount; i++)
                        works |= c.DefOf(i).LinkKind == LinkKind.Receiver;
                    for (var i = 0; works && i < c.PartCount; i++)
                        if (c.DefOf(i).IsVolumeStore)
                            for (var k = 0; k < n; k++)
                                w.PutPart(c, i, d);
                }
                WsSay($"linked: {n} {d.Name} into the works' Storage Volumes");
                return;
            case "order" when words.Length >= 3 && ushort.TryParse(words[1], out var count) && ConstructParts.Find(string.Join(' ', words.Skip(2))) is { } od:
            {
                foreach (var c in w.Bodies)
                {
                    var yard = false;
                    for (var i = 0; i < c.PartCount; i++)
                        yard |= c.DefOf(i).LinkKind == LinkKind.Transmitter;
                    for (var i = 0; yard && i < c.PartCount; i++)
                        if (c.DefOf(i) is { IsCraftTerminal: true, LinkKind: LinkKind.None, IsBuildStation: false })
                        {
                            var said = g.IsServer ? ConstructCrafting.Place(w, c, i, new WorkOrder { Part = (ushort)od.Id, Count = count }) : null; // (host, a server shortcut: the terminal panel's order without its reach)
                            if (!g.IsServer)
                                g.SandboxCraftOrder(c.Id, i, new WorkOrder { Part = (ushort)od.Id, Count = count });
                            WsSay($"linked: ordered {count} {od.Name} at body {c.Id}'s {c.DefOf(i).Name}: {said ?? "placed"}");
                            return;
                        }
                }
                WsFail("linked order: no Crafting Terminal on a body with a Transmitter");
                return;
            }
            case "open" when words.Length >= 2:
            {
                var (oc, op) = WsFind(g, string.Join(' ', words.Skip(1)));
                if (oc == null || op < 0)
                {
                    WsFail($"linked open: no {arg[5..]}");
                    return;
                }
                OpenLinkedPanel(oc, op);
                WsSay($"linked: panel {(Ui.LinkedOpen ? "open" : "NOT OPEN")} on body {oc.Id} part {op} ({oc.DefOf(op).Name})");
                return;
            }
            case "route":
            {
                var (rc, rp) = First(d => d.LinkKind == LinkKind.Route);
                if (rc == null || rc.Parts[rp].Link is not { Docks.Length: >= 2 } rl)
                {
                    WsFail("linked route: no Route Computer that knows two Cargo Docks");
                    return;
                }
                g.SandboxLink(rc.Id, rp, LinkOp.RouteClear);
                for (var k = 0; k < 2; k++)
                    g.SandboxLink(rc.Id, rp, LinkOp.RouteAdd, (uint)rl.Docks[k].Body << 16 | rl.Docks[k].Part, LinkRoutes.LeaveFill | LinkRoutes.LeaveTime | LinkRoutes.LeaveDone | 1u << 8 | 95u << 24, rl.Docks[k].World);
                g.SandboxLink(rc.Id, rp, LinkOp.RouteStart);
                WsSay($"linked: route {rl.Docks[0].Name} -> {rl.Docks[1].Name} sent and started");
                return;
            }
        }
        WsFail($"linked: what is \"{arg}\"?");
    }
}
