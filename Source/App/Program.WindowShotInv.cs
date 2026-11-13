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
            case "reload" when g.IsServer:
                WsSay("inv reload: " + g.StationReloadProbe());
                return true;
        }
        WsFail($"inv {arg}: hold|give NAME, store STORE NAME, show [STORE], reload (host for all but show)");
        return true;
    }
}
