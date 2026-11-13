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
                // "PART[#n] NAME": the store is the words before the part's name, which starts with a known prefix or is the last word.
                var at = rest.IndexOf(" Pressed Part (", StringComparison.Ordinal) is var p and >= 0 ? p : rest.IndexOf(" Extrusion ", StringComparison.OrdinalIgnoreCase);
                if (at < 0 || WsFind(g, rest[..at]) is not ({ } c, var part) || ConstructParts.Find(rest[(at + 1)..]) is not { } d || g.Constructs.StoreOf(c, part) is not { } st)
                {
                    WsFail($"inv store: STORE[#n] NAME (\"{rest}\")");
                    return true;
                }
                string? why = "no slots";
                for (var k = 0; k < st.SlotCount && why != null; k++)
                    if (st.Def[k] == ConstructParts.None)
                        why = g.Constructs.SetIn(c, part, k, d);
                WsSay($"inv store: {Named(d.Id)} into the {c.DefOf(part).Name}: {why ?? "stored"}");
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
                    var why = hands.BeginRemove(g.Constructs, pl.Id, best.WorldCom + new System.Numerics.Vector3(0, 1, 0), best.Id, 0, g.SandboxNow);
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
        }
        WsFail($"inv {arg}: hold|give NAME, store STORE NAME, show [STORE], reload (host for all but show)");
        return true;
    }
}
