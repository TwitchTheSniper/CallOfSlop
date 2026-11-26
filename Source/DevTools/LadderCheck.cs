using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// <c>--ladder-check [BLUEPRINT,...]</c>: every vertical ladder (a part with "ladder") on the shipped blueprints that hold one
/// (or the named ones) climbed headless, up from its foot and down from its top, through <see cref="ConstructRide.ClimbLadder"/>
/// as the player's F does, and played through to its end; a climb that ends inside something names it. <c>--ladder-check --design NAME|FILE[,...]</c>
/// climbs a ship design's instead (a shipped or saved design by name, or a design file, built from its elements as <see cref="DesignCheck"/>
/// builds it). A dev tool: it allocates freely.
/// </summary>
public static class LadderCheck
{
    public static int Cli(string[] args)
    {
        var named = args.SkipWhile(a => !a.Equals("--ladder-check", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault();
        var names = named == null || named.StartsWith("--") ? null : named.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var designs = args.SkipWhile(a => !a.Equals("--design", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault()?
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (Maps.Find("Proving Ground") is not { } yard)
        {
            Console.WriteLine("no Proving Ground map");
            return 1;
        }
        yard.RestoreProps();
        World.SetMap(yard);
        int bad = 0, ladders = 0;
        var prints = new List<ConstructBuild.Blueprint>();
        if (designs != null)
            foreach (var what in designs)
            {
                if (DesignByName.Blueprint(what, out var why) is { } dbp)
                    prints.Add(dbp);
                else
                {
                    Console.WriteLine($"{what}: {why}");
                    bad++;
                }
            }
        else
            foreach (var bp in ConstructBuild.Blueprints)
                if (names != null ? names.Any(n => n.Equals(bp.Name, StringComparison.OrdinalIgnoreCase))
                    : bp.Steps.Any(st => ConstructParts.Find(st.Part) is { LadderTop: > 0 }))
                    prints.Add(bp);
        foreach (var bp in prints)
        {
            var w = new ConstructWorld();
            var c = ConstructBuild.BuildOnGround(w, bp, ConstructWorld.Ground(World.Query, 0, 0), 0, out _);
            if (c == null)
            {
                Console.WriteLine($"{bp.Name}: doesn't build");
                bad++;
                continue;
            }
            c.Rebuild();
            c.Pos += new Vector3(0, 30f, 0); // (clear of the yard's ground)
            c.PrevPos = c.Pos;
            c.Rebuild();
            ConstructRide.Bodies = w; // (a ladder on through a dock finds the docked craft's: ConstructRide.Chained)
            foreach (var b in w.Bodies)
            {
                if (b.CarriedBy != c.Id)
                    continue;
                ConstructCargo.Follow(c, b); // (the craft it carries, where its dock holds them)
                b.Rebuild();
                foreach (var body in new[] { b, c }) // (docked: both docking hatches open, as the dock leaves them)
                    for (var k = 0; body.EnsureDoors() && k < body.DoorCount; k++)
                        if (body.Parts[body.Doors[k].Part].D.Door is { Dock: true })
                            ConstructDoors.Settle(body, k, true);
            }
            var top = c;
            foreach (var cb in w.Bodies.Where(b => b == top || b.CarriedBy == top.Id).ToList()) // (the craft it carries too: their ladders on through a dock)
            {
                c = cb;
                for (var i = 0; i < c.PartCount; i++)
                {
                    var d = c.Parts[i].D;
                    if (d.LadderTop <= 0)
                        continue;
                    ladders++;
                    if (Game.LadderOnShutDoor(c, i)) // (one stowed on a shut door's leaf, the Dray's belly hatch: climbed with that door open, as F leaves it)
                        ConstructDoors.Settle(c, c.DoorOf(i), true);
                    for (var k = ConstructRide.NextLid(c, i); k >= 0; k = ConstructRide.NextLid(c, i, k + 1)) // (a ladder in a shaft: climbed with its hatches open and its sections down, as F on it leaves them)
                    {
                        var lid = c.Doors[k].Part;
                        c.SolidBox(lid, 0, out var lc, out var lh);
                        var landing = c.Parts[i].Pos.Y + d.LadderTop;
                        Console.WriteLine($"    through {c.Parts[lid].D.Name} #{lid}, its top {lc.Y + lh.Y - landing:+0.000;-0.000} m from the landing");
                    }
                    ConstructRide.SettleWay(c, i);
                    var (up, down) = (Try(w, c, i, false), Try(w, c, i, true));
                    static bool Docked(string? why) => why == null || why.StartsWith("the docking hatch is shut"); // (a ladder through a dock: climbed only with a craft docked there)
                    var ok = Docked(up) && Docked(down) && (up == null || down == null);
                    bad += ok ? 0 : 1;
                    Console.WriteLine($"{bp.Name}{(c == top ? "" : $" (docked craft {c.Id})")}: {d.Name} #{i}: up {up ?? "ok"}, down {down ?? "ok"}");
                }
            }
        }
        Console.WriteLine($"ladder check: {ladders} ladder(s), {bad} refused");
        return bad;
    }

    /// <summary>
    /// One climb from the ladder's foot (or its top), played through to its end in the movement step's climb as a player's F
    /// starts it: null when it ends standing clear (a line names an end moved off its usual spot), else why not.
    /// </summary>
    private static string? Try(ConstructWorld w, Construct c, int part, bool fromTop)
    {
        var p = c.Parts[part];
        var d = p.D;
        ConstructRide.LadderEnds(d, out var foot, out var top);
        c.WorldFrame(p.Seg, out var q, out var t);
        Vector3 ToWorld(Vector3 l) => t + Vector3.Transform(p.Pos + Rot24.Apply(p.Rot, l), q);
        var height = World.HeightOf(Stance.Stand);
        var s = new MoveState { Position = ToWorld(fromTop ? top : foot), Grounded = true };
        var why = ConstructRide.ClimbLadder(ref s, c, part, height, frame: true);
        if (why != null)
            return why;
        var ticks = 0;
        while ((s.RideClimb > 0 || s.RideLadder != 0) && ticks++ < 2000)
            ConstructRide.Begin(w, ref s, height, true, out _, out _);
        if (s.RideLadder != 0)
            return "the climb never ended";
        var end = s.Position;
        var nominal = ToWorld(fromTop ? foot : top);
        var by = ConstructRide.BlockedBy(c, -1, end, height);
        if (by >= 0)
            return $"ends in {c.Parts[by].D.Name} #{by} at {c.ToLocal(end)}";
        var moved = Vector3.Distance(end, nominal);
        Console.WriteLine($"    {(fromTop ? "down" : "up")}: {ticks / (float)World.TickRate:0.0} s{(moved > 0.05f ? $", ends {moved:0.00} m from its usual spot (something stands there)" : "")}");
        return null;
    }
}

/// <summary>
/// A ship design picked by the test tools (<c>--ladder-check --design</c>, <c>--window-shot --design</c>): a design file, or a shipped or saved
/// design by name (ShipDesignStore), read and registered as <see cref="DesignCheck"/> does, and a blueprint of its body (DesignParts.BlueprintOf).
/// </summary>
public static class DesignByName
{
    /// <summary>The design's hash, name and canonical text (null: <paramref name="why"/> says why not).</summary>
    public static (string Hash, string Name, string Text)? Find(string what, out string why)
    {
        why = "";
        var json = File.Exists(what) ? File.ReadAllText(what)
            : ShipDesignStore.List().FirstOrDefault(x => x.Name.Equals(what, StringComparison.OrdinalIgnoreCase)).Json;
        if (json == null)
        {
            why = $"no ship design \"{what}\" (a file, or one of: {string.Join(", ", ShipDesignStore.List().Select(x => x.Name))})";
            return null;
        }
        if (DesignParts.Canon(json) is not { } text || DesignParts.Register(text) is not { } hash)
        {
            why = $"not a design this build reads: {DesignParts.LastRefusal}";
            return null;
        }
        return (hash, DesignParts.Find(hash)?.Name is { Length: > 0 } n ? n : Path.GetFileNameWithoutExtension(what), text);
    }

    public static ConstructBuild.Blueprint? Blueprint(string what, out string why) =>
        Find(what, out why) is { } f ? DesignParts.BlueprintOf(f.Hash, f.Name) : null;

    /// <summary>
    /// The names a design element answers to in the window tool's verbs (near, panel, sit, ...): its element id ("p3"), its name ("Helm
    /// seat"), its kind ("piece") and what it does ("seat"); null when the part isn't a design's. A dev tool's lookup: it allocates.
    /// </summary>
    public static string[]? NamesOf(ConstructPartDef d)
    {
        if (DesignParts.EntryOf(d) is not { } e || DesignParts.Elements(e.Hash) is not { } els)
            return null;
        var id = d.Name[(d.Name.LastIndexOf(' ') + 1)..];
        foreach (var el in els)
            if (el.Id == id)
                return [el.Id, el.Name, el.Kind, el.Fn, el.Role];
        return [id];
    }
}
