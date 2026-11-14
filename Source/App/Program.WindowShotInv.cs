namespace CallOfSlop;

/// <summary>
/// The window tool's "inv" step (docs/sandbox/design/inventory-run-time-parts.md): run-time parts in the hands, the backpack and a
/// store, as this peer has them, to compare a host with a joined client and a world before and after a save.
/// <list type="bullet">
/// <item>"inv hold NAME" (host): every player's hands hold the part NAME (a run-time part by its key name).</item>
/// <item>"inv give NAME" (host): the part given to every player as the game gives one (the hands, else the backpack, else dropped).</item>
/// <item>"inv store PART[#n] NAME" (host): the part into the first slot of that store that takes it.</item>
/// <item>"inv show [PART[#n]]": each player's hands and backpack, and the store's slots, by name and shown name.</item>
/// <item>"inv reload" (host): the world through the real save and the real load, in place.</item>
/// </list>
/// </summary>
public static unsafe partial class Program
{
    private static int _wsInvTries; // ("inv players" and "inv beacon": waits so far)

    private static bool WsInvVerb(Game g, Player me, string verb, string arg)
    {
        if (verb != "inv")
            return false;
        var words = arg.Split(' ', 2, StringSplitOptions.TrimEntries);
        var sub = words[0].ToLowerInvariant();
        var rest = words.Length > 1 ? words[1] : "";
        var hands = g.SandboxHands;
        string Named(ushort id) => ConstructParts.Get(id) is { } d ? $"{d.Name} [{PartRef.Shown(d)}]{(d.RuntimeMade ? " (run-time)" : "")}" : id == ConstructParts.None ? "nothing" : $"UNKNOWN {id}";
        switch (sub)
        {
            case "hold" when g.IsServer:
            case "give" when g.IsServer:
            {
                if (ConstructParts.Find(rest) is not { } d)
                {
                    WsFail($"inv {sub}: no part \"{rest}\"");
                    return true;
                }
                foreach (var pl in g.Players.Values)
                {
                    if (sub == "hold")
                        hands.Hands[pl.Id].Held = d.Id;
                    else
                        WsSay($"inv give: player {pl.Id}: {hands.Give(g.Constructs, pl.Id, d, pl.Position + new System.Numerics.Vector3(0, 1, 1))}");
                }
                WsSay($"inv {sub}: {Named(d.Id)}{(d.PhysicsOnly ? " (PHYSICS-ONLY)" : "")}");
                return true;
            }
            case "store" when g.IsServer:
            {
                // "PART[#n] NAME": the store is the words before the part's name (the first split where both are found).
                if (!WsSplitStore(g, rest, out var c, out var part, out var d) || g.Constructs.StoreOf(c!, part) is not { } st)
                {
                    WsFail($"inv store: STORE[#n] NAME (\"{rest}\")");
                    return true;
                }
                string? why = "no slots";
                for (var k = 0; k < st.SlotCount && why != null; k++)
                    if (st.Def[k] == ConstructParts.None)
                        why = g.Constructs.SetIn(c!, part, k, d!);
                WsSay($"inv store: {Named(d!.Id)} into the {c!.DefOf(part).Name}: {why ?? "stored"}");
                return true;
            }
            case "show":
            {
                foreach (var pl in g.Players.Values)
                {
                    if (pl.Id >= Game.MaxPeople)
                        continue;
                    WsSay($"inv show: player {pl.Id}{(pl.Id == me.Id ? " (me)" : "")} hands: {Named(hands.Hands[pl.Id].Held)}");
                    var pack = hands.Pack(pl.Id);
                    for (var k = 0; k < pack.SlotCount; k++)
                        if (pack.Def[k] != ConstructParts.None)
                            WsSay($"inv show: player {pl.Id} pack slot {k}: {Named(pack.Def[k])} x{pack.Count[k]}");
                }
                if (rest.Length > 0)
                {
                    if (WsFind(g, rest) is not ({ } c, var part) || g.Constructs.StoreOf(c, part) is not { } st)
                    {
                        WsFail($"inv show: no store \"{rest}\"");
                        return true;
                    }
                    for (var k = 0; k < st.SlotCount; k++)
                        if (st.Def[k] != ConstructParts.None)
                            WsSay($"inv show: {c.DefOf(part).Name} slot {k}: {Named(st.Def[k])} x{st.Count[k]}");
                }
                return true;
            }
            case "drop" when g.IsServer:
            {
                // A loose made part 2 m in front of every player (as a machine leaves one), to be taken back with "inv takeback".
                if (ConstructParts.Find(rest) is not { } d)
                {
                    WsFail($"inv drop: no part \"{rest}\"");
                    return true;
                }
                foreach (var pl in g.Players.Values)
                {
                    if (pl.Id >= Game.MaxPeople)
                        continue;
                    if (ConstructEdits.DropLoose(g.Constructs, d, pl.Position + new System.Numerics.Vector3(MathF.Sin(pl.Yaw) * 2, 1, MathF.Cos(pl.Yaw) * 2), System.Numerics.Quaternion.Identity) is not { } body)
                    {
                        WsFail("inv drop: no room");
                        return true;
                    }
                    body.Owner = pl.Id;
                    WsSay($"inv drop: {Named(d.Id)} as construct {body.Id} for player {pl.Id}");
                }
                return true;
            }
            case "takeback" when g.IsServer:
            {
                // For every player, the nearest loose body that is this part, taken apart with the real remove edit (its tool, from beside it).
                if (ConstructParts.Find(rest) is not { } d)
                {
                    WsFail($"inv takeback: no part \"{rest}\"");
                    return true;
                }
                var taken = new HashSet<ushort>();
                foreach (var pl in g.Players.Values)
                {
                    if (pl.Id >= Game.MaxPeople)
                        continue;
                    Construct? best = null;
                    foreach (var c in g.Constructs.Bodies)
                        if (c.Loose && c.PartCount == 1 && c.Parts[0].Def == d.Id && !taken.Contains(c.Id) && (best == null || System.Numerics.Vector3.Distance(c.WorldCom, pl.Position) < System.Numerics.Vector3.Distance(best.WorldCom, pl.Position)))
                            best = c;
                    if (best == null)
                    {
                        WsFail($"inv takeback: no loose {Named(d.Id)} for player {pl.Id}");
                        return true;
                    }
                    taken.Add(best.Id);
                    ref var h = ref hands.Hands[pl.Id];
                    h.Tool = d.Tool == ConstructTool.None ? ConstructTool.Welder : d.Tool;
                    var eye = best.WorldCom + new System.Numerics.Vector3(0, 1, 0);
                    var why = hands.BeginRemove(g.Constructs, pl.Id, eye, best.Id, 0, g.SandboxNow);
                    if (why == null && h.Edit == EditKind.Remove)
                    {
                        h.Start = g.SandboxNow - h.Duration; // (survival's timed edit done now, as holding fire to its end does: the tool holds no button)
                        hands.Tick(g.Constructs, g.SandboxNow, _ => eye);
                    }
                    WsSay($"inv takeback: player {pl.Id}: {Named(d.Id)} construct {best.Id}: {why ?? "begun"} (edit {h.Edit}, {h.Duration:0.0} s)");
                }
                return true;
            }
            case "count":
            {
                // Counted made parts as this peer has them: the host every player's, a client its own copy (it comes by key); then this
                // window's Tab inventory "Made parts" rows and the loose made bodies near the player.
                foreach (var pl in g.Players.Values)
                {
                    if (pl.Id >= Game.MaxPeople || !g.IsServer && pl.Id != me.Id)
                        continue;
                    var inv = g.SandboxInventory(pl.Id);
                    WsSay($"inv count: player {pl.Id}{(pl.Id == me.Id ? " (me)" : "")} made kinds {inv.MadeN}, parts in all {inv.TotalParts}");
                    for (var k = 0; k < inv.MadeN; k++)
                        WsSay($"inv count: player {pl.Id}: {Named(inv.MadeDef[k])} x{inv.MadeCount[k]}");
                }
                foreach (var (id, text) in Ui.SandboxInventoryMadeRows(g))
                    WsSay($"inv count: Made parts row: {text.Split("##")[0]} ({ConstructParts.Get(id)?.Name})");
                foreach (var c in g.Constructs.Bodies)
                    if (c.Loose && c.PartCount == 1 && c.DefOf(0).RuntimeMade && System.Numerics.Vector3.Distance(c.WorldCom, me.Position) < 12)
                        WsSay($"inv count: loose body {c.Id} near player {me.Id}: {Named(c.Parts[0].Def)}");
                return true;
            }
            case "setdown":
            {
                // One out of the count, set down in front of the player, as the "Made parts" row's click asks (a client: by key).
                if (ConstructParts.Find(rest) is not { } d)
                {
                    WsFail($"inv setdown: no part \"{rest}\"");
                    return true;
                }
                g.SandboxTakePart(d.Id);
                WsSay($"inv setdown: asked for {Named(d.Id)}");
                return true;
            }
            case "reload" when g.IsServer:
                WsSay("inv reload: " + g.StationReloadProbe());
                return true;
            case "fill" when g.IsServer:
            {
                // "fill N TEMPLATE" (phase 3): N made parts into every player's count, one of each, named by TEMPLATE with {i} as 0..N-1 (and {j} as
                // 10 + i, {k} as 100 + i, {e} as 100 + 2i, {t} as 12 + i mod 29, {q} as i / 29), to drive the inventory message past its room; the key bytes in all are printed.
                var w2 = arg.Split(' ', 3, StringSplitOptions.TrimEntries);
                if (w2.Length < 3 || !int.TryParse(w2[1], out var n) || n < 1)
                {
                    WsFail("inv fill N TEMPLATE");
                    return true;
                }
                var bytes = 0;
                var made = 0;
                var scratch = new PacketWriter(1024);
                for (var i = 0; i < n; i++)
                {
                    var name = w2[2].Replace("{i}", i.ToString()).Replace("{j}", (10 + i).ToString()).Replace("{k}", (100 + i).ToString()).Replace("{e}", (100 + 2 * i).ToString()).Replace("{t}", (12 + i % 29).ToString()).Replace("{q}", (i / 29).ToString());
                    if (ConstructParts.Find(name) is not { RuntimeMade: true } d)
                    {
                        WsFail($"inv fill: no made part \"{name}\" ({(HullLoft.IsCutName(name) ? HullLoft.CutRefusal(name) : "")})");
                        return true;
                    }
                    scratch.Begin(Msg.CAct);
                    PartRef.Write(scratch, d.Id);
                    bytes += scratch.Length - 1 + 2;
                    foreach (var pl in g.Players.Values)
                        if (pl.Id < Game.MaxPeople && g.SandboxInventory(pl.Id).Add(d.Id, 1 + i % 3))
                            made++;
                }
                WsSay($"inv fill: {n} kinds, {made} adds, {bytes} bytes of made entries a player (the inventory message splits past {ConstructNet.InventoryBudget})");
                return true;
            }
            case "sum":
            {
                // One line per inventory as this peer has it: made kinds, made parts and a digest of (key, count) in order of key, to compare a host with a client.
                foreach (var pl in g.Players.Values)
                {
                    if (pl.Id >= Game.MaxPeople || !g.IsServer && pl.Id != me.Id)
                        continue;
                    var inv = g.SandboxInventory(pl.Id);
                    var list = new List<string>();
                    var total = 0;
                    for (var k = 0; k < inv.MadeN; k++)
                    {
                        list.Add($"{ConstructParts.Get(inv.MadeDef[k])?.Name}={inv.MadeCount[k]}");
                        total += inv.MadeCount[k];
                    }
                    list.Sort(StringComparer.Ordinal);
                    var h = 2166136261u;
                    foreach (var s in list)
                        foreach (var ch in s)
                            h = (h ^ ch) * 16777619u;
                    WsSay($"inv sum: player {pl.Id}{(pl.Id == me.Id ? " (me)" : "")} made kinds {inv.MadeN}, made parts {total}, digest {h:X8}, catalogue parts {inv.TotalParts - total}");
                }
                return true;
            }
            case "open":
                // The Tab inventory on its "Made parts" category, as a click on the node leaves it.
                Ui.OpenSandboxParts(false);
                Ui.SetSandboxInventoryMade(true);
                WsSay("inv open: Made parts");
                return true;
            case "tiles":
            {
                // "tiles FROM N TEMPLATE": the pictures of N made parts asked for in one frame, as rows on screen ask ({i} counts from FROM, the
                // rest of the template as "fill"); with the pool full, the longest unseen slots are handed on. Prints how many show a picture now.
                var w2 = rest.Split(' ', 3, StringSplitOptions.TrimEntries);
                if (w2.Length < 3 || !int.TryParse(w2[0], out var from) || !int.TryParse(w2[1], out var n))
                {
                    WsFail("inv tiles FROM N TEMPLATE");
                    return true;
                }
                var shown = 0;
                for (var i = from; i < from + n; i++)
                {
                    var name = w2[2].Replace("{i}", i.ToString()).Replace("{f}", (100 + 5 * (i % 60)).ToString()).Replace("{s}", (20 + 5 * (i / 60)).ToString()); // ({f} a length in 5 cm steps, {s} a section that moves on every 60)
                    if (ConstructParts.Find(name) is not { RuntimeMade: true } d)
                    {
                        WsFail($"inv tiles: no made part \"{name}\"");
                        return true;
                    }
                    if (SandboxTiles.TryGetPart(d.Id, out _, out _, out _))
                        shown++;
                }
                WsSay($"inv tiles: {from}..{from + n - 1}: {shown} with a picture; in use {SandboxTiles.MadeInUse} of {SandboxTiles.MadePool}, drawn {SandboxTiles.MadeRendered}, handed on {SandboxTiles.MadeEvicted}");
                return true;
            }
            case "icons":
                WsSay($"inv icons: rows with a picture {Ui.SandboxMadeIcons}, swatches {Ui.SandboxMadeSwatches}; made tiles drawn {SandboxTiles.MadeRendered}, in use {SandboxTiles.MadeInUse} of {SandboxTiles.MadePool}, handed on {SandboxTiles.MadeEvicted}, drawn again for new boxes {SandboxTiles.MadeRedrawn}, waits for a stand-in {SandboxTiles.MadeStandInWaits}, an id made again {SandboxTiles.MadeIdRemade}");
                return true;
            case "force" when g.IsServer:
            {
                // "force N STORE[#n] NAME" (a test state, as "hold" is): N of a made part put straight into the store's first empty slot past the
                // physics-only rule, so a network lists it; marked changed as any store change is.
                var w2 = rest.Split(' ', 2, StringSplitOptions.TrimEntries);
                if (w2.Length < 2 || !int.TryParse(w2[0], out var n) || !WsSplitStore(g, w2[1], out var c, out var part, out var d) || g.Constructs.StoreOf(c!, part) is not { } st)
                {
                    WsFail($"inv force N STORE[#n] NAME (\"{rest}\")");
                    return true;
                }
                for (var k = 0; k < st.SlotCount; k++)
                    if (st.Def[k] == ConstructParts.None)
                    {
                        st.Def[k] = d!.Id;
                        st.Count[k] = (byte)Math.Min(n, 255);
                        g.Constructs.ChangedForTool(st);
                        WsSay($"inv force: {n} x {Named(d.Id)} into the {c!.DefOf(part).Name} slot {k}{(d.PhysicsOnly ? " (PHYSICS-ONLY: a test state)" : "")}");
                        return true;
                    }
                WsFail("inv force: no empty slot");
                return true;
            }
            case "pull":
            {
                // The first made row of this peer's terminal view asked for, to the hands, as the use key at the terminal asks (a client: by key).
                var tv = g.SandboxTerminal(me.Id);
                foreach (var row in tv.Rows)
                    if (!row.Material && Extrusion.IsRuntimeId(row.Id) && WsFind(g, "Smart Terminal") is ({ } tc, var tp))
                    {
                        g.SandboxTerminalPull(tc.Id, tp, row, hands: true);
                        WsSay($"inv pull: asked for {row.Text} at construct {tc.Id} part {tp}");
                        return true;
                    }
                WsFail("inv pull: no made row at a terminal");
                return true;
            }
            case "rack" when g.IsServer:
            {
                // "rack N RACK[#n] NAME" (phase 4): N of a made part onto a Piece Rack by the conveyors' own packing (as a line would leave them).
                var w2 = rest.Split(' ', 2, StringSplitOptions.TrimEntries);
                if (w2.Length < 2 || !int.TryParse(w2[0], out var n) || !WsSplitStore(g, w2[1], out var c, out var part, out var d))
                {
                    WsFail($"inv rack N RACK[#n] NAME (\"{rest}\")");
                    return true;
                }
                var put = 0;
                while (put < n && g.Constructs.RackPut(c!, part, d!))
                    put++;
                WsSay($"inv rack: {put} of {n} x {Named(d!.Id)} onto the {c!.DefOf(part).Name}");
                return true;
            }
            case "players" or "beacon":
            {
                // Two windows keep no common clock: "players N" waits until N other players are alive here; "beacon" until this peer's beacon
                // view is on (its network theirs). Each tries again every 30 frames, 600 times at most.
                var others = 0;
                foreach (var pl in g.Players.Values)
                    if (pl.Id != me.Id && pl.Alive)
                        others++;
                var ready = sub == "beacon" ? g.SandboxBeaconOn : int.TryParse(rest, out var need) && others >= need;
                if (!ready)
                {
                    if (++_wsInvTries > 6000)
                    {
                        WsFail($"inv {arg}: never happened");
                        return true;
                    }
                    _wsQueue.EnqueueFront("inv " + arg);
                    _wsWait = 30;
                    return true;
                }
                WsSay($"inv {arg}: after {_wsInvTries} waits");
                _wsInvTries = 0;
                return true;
            }
            case "survival":
                // "survival on|off" (a test state, run on each peer): the rules switched after the blueprint went down through the menu, so beacons work.
                g.SurvivalForTool(rest != "off");
                WsSay($"inv survival: infinite parts {g.SandboxInfiniteParts}, survival {g.SandboxHands.Survival}");
                return true;
            case "rackforce" when g.IsServer:
            {
                // "rackforce N TEMPLATE" (a test state, as "force" is): N made kinds, one of each, counted on the first Piece Rack's kinds
                // past its packing (TEMPLATE as "fill" reads it), to drive a network's made list past a message's room; the key bytes are printed.
                var w2 = arg.Split(' ', 3, StringSplitOptions.TrimEntries);
                if (w2.Length < 3 || !int.TryParse(w2[1], out var n) || WsFind(g, "Piece Rack") is not ({ } rc, var rp) || g.Constructs.StoreOf(rc, rp) is not { } rst)
                {
                    WsFail("inv rackforce N TEMPLATE (and a Piece Rack)");
                    return true;
                }
                var bytes = 0;
                var scratch = new PacketWriter(1024);
                for (var i = 0; i < n; i++)
                {
                    var name = w2[2].Replace("{i}", i.ToString()).Replace("{j}", (10 + i).ToString()).Replace("{k}", (100 + i).ToString()).Replace("{e}", (100 + 2 * i).ToString()).Replace("{t}", (12 + i % 29).ToString()).Replace("{q}", (i / 29).ToString());
                    if (ConstructParts.Find(name) is not { RuntimeMade: true } d)
                    {
                        WsFail($"inv rackforce: no made part \"{name}\" ({(HullLoft.IsCutName(name) ? HullLoft.CutRefusal(name) : "")})");
                        return true;
                    }
                    scratch.Begin(Msg.CAct);
                    PartRef.Write(scratch, d.Id);
                    bytes += scratch.Length - 1 + 2;
                    rst.AddKind(d.Id, 1);
                }
                g.Constructs.ChangedForTool(rst);
                WsSay($"inv rackforce: {n} kinds on the Piece Rack, {bytes} bytes of made entries (a beacon message's made list goes on in the next past {ConstructBeacons.MadeBudget})");
                return true;
            }
            case "madesum":
            {
                // This peer's beacon view's made list as a digest of (key, count), to compare a host with a client.
                var bv = g.SandboxBeacon(me.Id);
                var list = new List<string>();
                var total = 0;
                for (var k = 0; k < bv.MadeN; k++)
                {
                    list.Add($"{ConstructParts.Get(bv.MadeDef[k])?.Name}={bv.MadeCount[k]}");
                    total += bv.MadeCount[k];
                }
                list.Sort(StringComparer.Ordinal);
                var h = 2166136261u;
                foreach (var s in list)
                    foreach (var ch in s)
                        h = (h ^ ch) * 16777619u;
                WsSay($"inv madesum: player {me.Id} network made kinds {bv.MadeN} (+{bv.MadeOver}), parts {total}, digest {h:X8}");
                return true;
            }
            case "made":
            {
                // This peer's "Made parts" rows (the count's and the network's), their tooltips, and its beacon view's made list.
                var bv = g.SandboxBeacon(me.Id);
                WsSay($"inv made: beacon {bv.State}, on {g.SandboxBeaconOn}, made kinds on the network {bv.MadeN}{(bv.MadeOver > 0 ? $" (+{bv.MadeOver})" : "")}");
                for (var k = 0; k < bv.MadeN; k++)
                    WsSay($"inv made: network {Named(bv.MadeDef[k])} x{bv.MadeCount[k]}");
                var r = 0;
                foreach (var (id, text) in Ui.SandboxInventoryMadeRows(g))
                {
                    WsSay($"inv made: row {text.Split("##")[0]} ({ConstructParts.Get(id)?.Name}) tip: {Ui.SandboxInventoryMadeTip(r).Replace('\n', '|')}");
                    r++;
                }
                return true;
            }
            case "search":
                // "search TEXT": the Tab inventory's search as typed; the next frames draw it (then "inv hits").
                Ui.OpenSandboxParts(false);
                Ui.SetSandboxPartsSearch(rest);
                WsSay($"inv search: \"{rest}\"");
                return true;
            case "hits":
                WsSay($"inv hits: made rows found {Ui.SandboxMadeSearchHits}, pictures {Ui.SandboxMadeIcons}, swatches {Ui.SandboxMadeSwatches}");
                return true;
            case "mode":
                // "mode line|replace|place": the build mode as its key leaves it (no station check: a test state), for a made row's click.
                _buildMode = rest == "line" ? BuildMode.Line : rest == "replace" ? BuildMode.Replace : BuildMode.Place;
                WsSay($"inv mode: {_buildMode}");
                return true;
            case "aidpick":
            {
                // "aidpick NAME": the Made parts row's click with a mode on (picked for the aid, not set down).
                if (ConstructParts.Find(rest) is not { } d)
                {
                    WsFail($"inv aidpick: no part \"{rest}\"");
                    return true;
                }
                Ui.SandboxMadePick(g, d);
                WsSay($"inv aidpick: picked {Named(_aidPick)}, mode {_buildMode}, pinned {RuntimeParts.Pinned == d.Id}");
                return true;
            }
            case "line":
            {
                // "line N PART[#n] NAME": a Line Layer stroke of N of a part along +x from the first socket of PART that takes it, sent as the drag's release sends it.
                var w2 = rest.Split(' ', 2, StringSplitOptions.TrimEntries);
                if (w2.Length < 2 || !int.TryParse(w2[0], out var n) || !WsSplitStore(g, w2[1], out var c, out var part, out var d))
                {
                    WsFail($"inv line N PART[#n] NAME (\"{rest}\")");
                    return true;
                }
                // The part of that name nearest this player's eye, on any body (a map's own kit may have one too).
                var named = c!.DefOf(part).Name;
                var near = float.MaxValue;
                foreach (var b in g.Constructs.Bodies)
                    for (var i = 0; i < b.PartCount; i++)
                        if (b.DefOf(i).Name == named && System.Numerics.Vector3.Distance(b.SegToWorld(b.Parts[i].Seg, b.Parts[i].Pos), me.Eye) is var pd && pd < near)
                        {
                            near = pd;
                            c = b;
                            part = i;
                        }
                // The free socket and turn whose ghost is nearest this player's eye (the server checks reach from there).
                int bs = -1, bv = 0;
                var best = float.MaxValue;
                for (var s = 0; s < c!.SocketCount(part); s++)
                    for (var v = 0; v < d!.Sockets.Length * 4 && v < 256; v++)
                        if (ConstructBuild.Place(c, part, s, d, v / 4, v % 4, out var g0) == null && System.Numerics.Vector3.Distance(c.SegToWorld(g0.Seg, g0.Pos), me.Eye) is var dist && dist < best)
                        {
                            best = dist;
                            bs = s;
                            bv = v;
                        }
                if (bs >= 0 && ConstructBuild.Place(c, part, bs, d!, bv / 4, bv % 4, out var gh) == null)
                        {
                            var dir = (byte)0;
                            var pitch = ConstructLine.Pitch(c, d, gh.Rot, gh, dir);
                            var pw = g.SandboxAidBegin(BuildAidOp.Line);
                            pw.U16(c.Id).U16((ushort)part);
                            PartRef.Write(pw, d.Id);
                            pw.U8((byte)bs).U8((byte)bv).U8(dir).F32(pitch).U16((ushort)n).U8(2).F32(1).U16(1);
                            g.SandboxAidSend(pw);
                            WsSay($"inv line: {n} x {Named(d.Id)} from socket {bs} of the {c.DefOf(part).Name}, variant {bv}, pitch {pitch:0.###} m, ghost {System.Numerics.Vector3.Distance(c.SegToWorld(gh.Seg, gh.Pos), me.Eye):0.0} m from the eye at {me.Eye}");
                            return true;
                        }
                WsFail($"inv line: no socket of the {c.DefOf(part).Name} takes the {Named(d!.Id)}");
                return true;
            }
            case "replace":
            {
                // "replace PART[#n] NAME": the Replacer's swap of that part for NAME, as its click (or its picker) sends it.
                if (!WsSplitStore(g, rest, out var c, out var part, out var d))
                {
                    WsFail($"inv replace PART[#n] NAME (\"{rest}\")");
                    return true;
                }
                var plan = ConstructReplace.Plan(g.Constructs, c!, part, d!, out _);
                SandboxReplaceSend(g, c!.Id, part, d!.Id);
                WsSay($"inv replace: the {Named(c.Parts[part].Def)} for {Named(d.Id)} (this peer's plan: {plan ?? "fits"})");
                return true;
            }
            case "net":
            {
                // What this peer's Smart Terminal view lists (the rows, made parts marked) and its beacon view's made list.
                var tv = g.SandboxTerminal(me.Id);
                WsSay($"inv net: terminal {tv.Handle}, state {tv.Stock.State}, {tv.Rows.Count} rows, made kinds {tv.Stock.MadeN}");
                foreach (var row in tv.Rows)
                    if (!row.Material && Extrusion.IsRuntimeId(row.Id))
                        WsSay($"inv net: made row {row.Text} ({ConstructParts.Get(row.Id)?.Name})");
                return true;
            }
        }
        WsFail($"inv {arg}: hold|give NAME, store STORE NAME, show [STORE], reload, drop|takeback|setdown NAME, count, fill N TEMPLATE, sum, open, icons, force N STORE NAME, net");
        return true;
    }

    /// <summary>"STORE[#n] NAME": the store is the words before the part's name, at the first space where both are found.</summary>
    private static bool WsSplitStore(Game g, string text, out Construct? c, out int part, out ConstructPartDef? d)
    {
        for (var at = text.IndexOf(' '); at > 0; at = text.IndexOf(' ', at + 1))
            if (WsFind(g, text[..at]) is ({ } body, var p) && ConstructParts.Find(text[(at + 1)..]) is { } found)
            {
                c = body;
                part = p;
                d = found;
                return true;
            }
        c = null;
        part = 0;
        d = null;
        return false;
    }
}
