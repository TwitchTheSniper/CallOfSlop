namespace CallOfSlop;

/// <summary>
/// The window tool's verb for Laser Cutters (docs/sandbox/design/laser-cutter.md, phase 3; ConstructLaserCutter.cs), on the spawned blueprint:
/// <c>cutter</c> prints each cutter as this peer has it (its state, offcut bin and tenths, bed, stroke, head order) and the cut parts lying
/// loose; <c>cutter order KEY [N]</c> (host) queues an order at the first cutter that takes it, as a designer's ORDER does;
/// <c>cutter feed KEY</c> (host) puts a blank on the conveyor into a cutter, or with none lays it on the first cutter's bed by its Piece in's
/// own rule; <c>cutter await</c> waits (a tick at a time, 120 s at most) until the first busy cutter's cut ends, printing the seconds it took.
/// </summary>
public static unsafe partial class Program
{
    private static uint _wsCutFrom; // (await: the server tick the stroke was first seen at; 0: not yet)
    private static int _wsCutTries;

    private static bool WsCutterVerb(Game g, string verb, string arg)
    {
        if (verb != "cutter")
            return false;
        var w = g.Constructs;
        var sub = arg.Split(' ', 2, StringSplitOptions.TrimEntries);
        var rest = sub.Length > 1 ? sub[1] : "";
        var bodies = WsSpawned(g) is { } s ? [s] : w.Bodies;
        switch (sub[0])
        {
            case "order" when g.IsServer:
            {
                var cut = rest.LastIndexOf(' ');
                var n = cut > 0 && ushort.TryParse(rest[(cut + 1)..], out var cn) ? cn : (ushort)1;
                var name = cut > 0 && rest[(cut + 1)..] == n.ToString() ? rest[..cut] : rest;
                string? why = "no Laser Cutter";
                for (var pass = 0; pass < 2 && why != null; pass++) // (cutters first; then any machine that takes orders: a Panel Former for a blank)
                foreach (var c in bodies)
                    for (var i = 0; i < c.PartCount && why != null; i++)
                        if (pass == 0 ? c.DefOf(i).IsLaserCutter : !c.DefOf(i).IsLaserCutter && WorkOrders.TakesOrders(c.DefOf(i)))
                        {
                            why = WorkOrders.Queue(w, c, i, ConstructPanelFormer.Order(name, n));
                            if (why == null)
                                WsSay($"cutter: {c.DefOf(i).Name}#{i} queued {n} of {name}");
                            else
                                WsSay($"cutter: {c.DefOf(i).Name}#{i} refused: {why}");
                        }
                _wsWait = 2;
                return true;
            }
            case "feed" when g.IsServer:
            {
                if (ConstructParts.Find(rest) is not { } piece)
                {
                    WsFail($"cutter feed: no part \"{rest}\"");
                    return true;
                }
                foreach (var c in bodies)
                    for (var i = 0; i < c.PartCount; i++)
                        if (c.DefOf(i).IsLaserCutter)
                        {
                            for (var k = 0; k < c.RouteCount; k++)
                                if (c.Routes[k].Type == RouteType.Conveyor && c.Routes[k].To == i)
                                {
                                    var put = ConstructConveyor.PutMade(w, c, k, piece, 1, out var stall);
                                    WsSay($"cutter feed: {piece.Name} {(put ? "put on the conveyor into" : $"not put ({stall}) on the conveyor into")} {c.DefOf(i).Name}#{i}");
                                    _wsWait = 2;
                                    return true;
                                }
                            var took = ConstructLaserCutter.TakeFromConveyor(w, c, i, piece);
                            WsSay($"cutter feed: {piece.Name} {(took ? "laid on the bed of" : "refused by")} {c.DefOf(i).Name}#{i} (no conveyor into it)");
                            _wsWait = 2;
                            return true;
                        }
                WsFail("cutter feed: no Laser Cutter");
                return true;
            }
            case "wreck" when g.IsServer: // the first cutter wrecked; then the Material Bundles it spilled, each with its worth
            {
                foreach (var c in bodies)
                    for (var i = 0; i < c.PartCount; i++)
                        if (c.DefOf(i).IsLaserCutter && w.StoreOf(c, i) is { } st)
                        {
                            WsSay($"cutter wreck: {c.DefOf(i).Name}#{i} holding {st.Amount} {RecyclingTable.Current.NameOf(st.Material)} +{st.InAmount} tenths");
                            var before = new HashSet<Construct>(w.Bodies);
                            w.DamagePart(c, i, 1e9f, out _, raw: true);
                            var worth = new List<string>();
                            foreach (var b in w.Bodies)
                                if (!before.Contains(b) && b.PartCount == 1 && b.DefOf(0).Name == ConstructStorage.MaterialBundle)
                                    worth.Add($"{b.Parts[0].Rounds} {RecyclingTable.Current.NameOf((byte)b.Parts[0].Setting)}");
                            WsSay($"cutter wreck: spilled {worth.Count} bundle(s): {string.Join(", ", worth)}");
                            _wsWait = 2;
                            return true;
                        }
                WsFail("cutter wreck: no Laser Cutter");
                return true;
            }
            case "await":
            {
                foreach (var c in bodies)
                    for (var i = 0; i < c.PartCount; i++)
                        if (c.DefOf(i).IsLaserCutter && w.StoreOf(c, i) is { } st)
                        {
                            if (st.Busy && _wsCutFrom == 0)
                                _wsCutFrom = g.ServerTick;
                            if (_wsCutFrom != 0 && !st.Busy)
                            {
                                WsSay($"cutter await: {c.DefOf(i).Name}#{i} cut for {(g.ServerTick - _wsCutFrom) / (float)World.TickRate:0.0} s (seen on this peer, 0.1 s steps)");
                                _wsCutFrom = 0;
                                _wsCutTries = 0;
                                return true;
                            }
                        }
                if (++_wsCutTries > 120 * 60)
                {
                    _wsCutTries = 0;
                    _wsCutFrom = 0;
                    WsFail("cutter await: no cut ended in 120 s");
                    return true;
                }
                _wsQueue.EnqueueFront("cutter await");
                WsWaitTicks(1);
                return true;
            }
        }
        var any = false;
        foreach (var c in bodies)
            for (var i = 0; i < c.PartCount; i++)
                if (c.DefOf(i) is { IsLaserCutter: true } d && w.StoreOf(c, i) is { } st)
                {
                    any = true;
                    var head = WorkOrders.HeadOf(c.Parts[i], out var h) && h.Name != null ? $"{h.Name} {h.Made}/{h.Count}" : "none";
                    var job = h.Name != null ? ConstructLaserCutter.JobOf(h.Name) : null;
                    WsSay($"cutter: {d.Name}#{i}: {(ExtrudeStall)st.Refused}{(st.Busy ? " busy" : "")}, offcut {st.Amount} {RecyclingTable.Current.NameOf(st.Material)} +{st.InAmount} tenths {RecyclingTable.Current.NameOf(st.InMaterial)}, "
                        + $"bed {(st.OnBed ? "blank" : "empty")} {st.Progress}%, orders {c.Parts[i].Orders?.Count ?? 0}, head {head}"
                        + (job != null ? $"; job: edge {job.EdgeM:0.00} m, {job.Mm:0} mm, {ConstructLaserCutter.Seconds(job, d):0.0} s by the law, offcut {job.OffTenths / 10f:0.0}" : ""));
                }
        var loose = new Dictionary<string, int>();
        foreach (var b in w.Bodies)
            if (b.PartCount == 1 && HullLoft.IsCutName(b.DefOf(0).Name))
                loose[b.DefOf(0).Name] = loose.GetValueOrDefault(b.DefOf(0).Name) + 1;
        WsSay($"cutter: {(any ? "" : "no Laser Cutter; ")}cut parts loose: {(loose.Count == 0 ? "none" : string.Join(", ", loose.Select(kv => $"{kv.Key} x{kv.Value}")))}");
        return true;
    }
}
