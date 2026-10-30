using System.Numerics;

namespace CallOfSlop;

/// <summary>An edit in progress: attaching takes a few seconds, removing a little less (decision 2).</summary>
public enum EditKind : byte
{
    None,
    Attach, // the held part onto a socket of a construct
    PlaceNew, // the held part on the ground: a new construct
    Remove, // a part off a construct, into the hand (full hands: the backpack, else dropped; ConstructGive.cs)
}

/// <summary>What one player holds and is doing (server-owned, replicated in the Sandbox mode-state tail).</summary>
public struct SandboxHand
{
    public ConstructTool Tool; // None: a gun or bare hands (use only, never edit)
    public ushort Held; // the part in hand (ConstructParts id, ConstructParts.None: none)
    public EditKind Edit;
    public ushort Body;
    public byte Part, Socket, Variant;
    public Vector3 At; // PlaceNew: the new construct's origin
    public float Yaw;
    public Quaternion PlaceRot; // PlaceNew: the new construct's frame (free placement)
    public byte PlaceTurn; // ...and the part's Rot24 turn in it
    public bool PlaceAnchor; // ...pinned to the ground
    public double Start, Duration;
    public ushort SeatBody; // 0: not sitting
    public byte SeatPart;
    public ushort SeatKeys; // seated: the fire, aim and reload keys the client holds (input readers; ConstructParts.KeyFire...)
    public double RackAt; // last rack take (a press cycles to the next part)
    public float SeatLook; // seated: the look relative to the seat's facing, as the client sends it (CActKind.SeatLook; protocol 80)
    public bool HasSeatLook; // (none heard since sitting down: the server works it out from the world look)
    public ushort GrabBody; // the physics gun holds this construct (0: nothing; ConstructGrab.cs)
    public Vector3 GrabLocal; // ...at this point (its body frame)
    public float GrabDist; // ...this far out along the look
    public Quaternion GrabRel; // ...turned this way relative to the look's yaw (the held thing swings round with you)
    public double GrabAskedAt; // client: when it last asked to grab or let go (the 10 Hz tail doesn't undo it meanwhile)

    public readonly bool Seated => SeatBody != 0;

    public readonly float Progress(double now) => Edit == EditKind.None || Duration <= 0 ? 0 : (float)Math.Clamp((now - Start) / Duration, 0, 1);

    public static SandboxHand Empty => new() { Held = ConstructParts.None, GrabRel = Quaternion.Identity };
}

/// <summary>
/// The Sandbox editing rules on the server (the client asks; this decides): tools, the held part, timed attach and
/// remove, the parts rack, picking up and dropping loose parts, seats and doors. Every check the client could fake is
/// redone here: the tool must fit the part, the socket must be free and within reach, nothing may be in the way.
/// Per-player state is a fixed array: nothing allocates.
/// </summary>
public sealed partial class ConstructEdits
{
    public readonly SandboxHand[] Hands = new SandboxHand[256];

    /// <summary>A line per completed edit (the self-test reads them; the game logs them).</summary>
    public readonly List<string> Done = new(16);

    /// <summary>
    /// Every player's inventory (milestone 3: salvage; the game's, set by it). Null: no inventories (a bare construct
    /// world in a test): everything taken off goes to the player by <see cref="Give"/> (the hands, the backpack, or dropped).
    /// </summary>
    public SandboxInventories? Inventory;

    /// <summary>The map's <c>infiniteParts</c> (the server sets it every tick): the rack and parts menu hand out parts from nothing.</summary>
    public bool InfiniteParts = true;

    /// <summary>
    /// Survival (physical storage, decision 18; the server sets it every tick): a map with infinite parts off and the parts
    /// menu off. Nothing is counted abstractly there: a part salvaged or taken off comes to the player by <see cref="Give"/>
    /// (the hands; full hands, the backpack, else set down where it was), to be carried and set into storage. Scrap
    /// still counts on the player.
    /// </summary>
    public bool Survival;

    public ConstructEdits() => Reset();

    /// <summary>
    /// Salvage (milestone 3, the loot loop): what anyone may take apart into their inventory: a wreck (cut off, or a part
    /// broken off), a loose part, a construct nobody built (the map's), or someone else's once it is disabled (a part of it
    /// wrecked). Someone else's working construct can't be taken apart; your own can (its parts go back into the hand, or
    /// with <see cref="InfiniteParts"/> off back into the inventory).
    /// </summary>
    public static bool Salvageable(Construct c, byte player) => c.Wreck || c.Loose || c.Owner == 255 || c.Owner != player && c.Damaged;

    /// <summary>Why a player may not take parts off this construct, or null.</summary>
    public static string? SalvageRefusal(Construct c, byte player) =>
        c.Owner == player || Salvageable(c, player) ? null : "that construct is someone else's: disable it first (shoot a part off)";

    /// <summary>Server: the part in the player's hands goes back into their inventory (the parts are counted with infinite parts off).</summary>
    public void ReturnHeld(byte player)
    {
        ref var h = ref Hands[player];
        if (InfiniteParts || Inventory == null || h.Held == ConstructParts.None)
            return;
        if (Inventory.Of(player).Add(h.Held))
            h.Held = ConstructParts.None;
    }

    public void Reset()
    {
        for (var i = 0; i < Hands.Length; i++)
            Hands[i] = SandboxHand.Empty;
    }

    public ref SandboxHand Hand(byte player) => ref Hands[player];

    public string? SetTool(byte player, ConstructTool tool)
    {
        ref var h = ref Hands[player];
        if (h.Tool != tool)
            h.Edit = EditKind.None; // putting the tool away stops the edit
        h.Tool = tool;
        return null;
    }

    private static string? Reachable(Vector3 eye, Vector3 at, float reach) =>
        Vector3.Distance(eye, at) <= reach + 0.75f ? null : "too far away";

    public string? BeginAttach(ConstructWorld world, byte player, Vector3 eye, ushort body, int part, int socket, int variant, double now)
    {
        ref var h = ref Hands[player];
        if (h.Seated)
            return "get out of the seat first";
        if (ConstructParts.Get(h.Held) is not { } def)
            return "hold a part first (take one from the rack)";
        if (h.Tool == ConstructTool.None)
            return "take out a tool to build (3: wrench, 4: welder)";
        if (!ConstructBuild.ToolFits(h.Tool, def))
            return $"the {def.Name} needs the {def.Tool.ToString().ToLowerInvariant()}";
        if (world.Get(body) is not { } c)
            return "that construct is gone";
        if (Contested(c, player) is { } defended)
            return defended;
        if (c.Driver != 255 && c.Driver != player)
            return "someone is driving it";
        if (Claims(c, player) && ConstructPlace.LimitRefusal(world, player) is { } full)
            return full;
        var v = variant % Math.Max(1, def.Sockets.Length * 4);
        if (ConstructBuild.Place(c, part, socket, def, v / 4, v % 4, out var g) is { } why)
            return why;
        if (Reachable(eye, c.ToWorld(g.Pos), ConstructBuild.Reach) is { } far)
            return far;
        h.Edit = EditKind.Attach;
        h.Body = body;
        h.Part = (byte)part;
        h.Socket = (byte)socket;
        h.Variant = (byte)v;
        h.Start = now;
        h.Duration = EditSeconds(h.Tool, ConstructBuild.AttachSeconds(def));
        return FinishNow(world, player, eye);
    }

    /// <summary>The map the edits are checked against (the server sets it; null in a bare construct world: no map checks).</summary>
    public IWorldQuery? Query;

    /// <summary>Where the players stand (the server's <see cref="ConstructWorld.PlayerSpots"/>): nothing is set down on them.</summary>
    private static ReadOnlySpan<Vector3> Feet(ConstructWorld world) => world.PlayerSpots.AsSpan(0, world.PlayerSpotCount);

    /// <summary>
    /// Free placement (milestone 1's "set it down on the ground", generalised): the held part on the surface at
    /// <paramref name="point"/> facing <paramref name="normal"/>, turned <paramref name="turn"/> (Rot24), as a new construct
    /// rooted at it. <paramref name="flags"/>: <see cref="ConstructPlace.FlagGrid"/> snaps it to the world grid,
    /// <see cref="ConstructPlace.FlagAnchor"/> pins it to the ground (creative only: infinite parts). Null, or why not.
    /// </summary>
    public string? BeginPlaceNew(ConstructWorld world, byte player, Vector3 eye, Vector3 point, Vector3 normal, float yaw, byte turn, byte flags, double now)
    {
        ref var h = ref Hands[player];
        if (h.Seated)
            return "get out of the seat first";
        if (ConstructParts.Get(h.Held) is not { } def)
            return "hold a part first (take one from the rack)";
        if (h.Tool == ConstructTool.None)
            return "take out a tool to build (3: wrench, 4: welder)";
        if (!ConstructBuild.ToolFits(h.Tool, def))
            return $"the {def.Name} needs the {def.Tool.ToString().ToLowerInvariant()}";
        if (Reachable(eye, point, ConstructBuild.Reach) is { } far)
            return far;
        var anchor = (flags & ConstructPlace.FlagAnchor) != 0;
        if (anchor && !InfiniteParts)
            return "anchoring to the ground is for creative: build on a foundation instead";
        if (ConstructPlace.LimitRefusal(world, player) is { } full)
            return full;
        if (ConstructPlace.Pose(Query, def, point, normal, yaw, turn, (flags & ConstructPlace.FlagGrid) != 0, out var fp) is { } where)
            return where;
        if (ConstructPlace.Fits(world, Query, def, fp, Feet(world)) is { } blocked)
            return blocked;
        h.Edit = EditKind.PlaceNew;
        h.At = fp.Pos;
        h.PlaceRot = fp.Rot;
        h.PlaceTurn = fp.Turn;
        h.PlaceAnchor = anchor;
        h.Yaw = yaw;
        h.Start = now;
        h.Duration = EditSeconds(h.Tool, ConstructBuild.AttachSeconds(def));
        return FinishNow(world, player, eye);
    }

    /// <summary>Milestone 1's form: upright on the ground at <paramref name="ground"/>, turned to <paramref name="yaw"/>.</summary>
    public string? BeginPlaceNew(ConstructWorld world, byte player, Vector3 eye, Vector3 ground, float yaw, double now) =>
        BeginPlaceNew(world, player, eye, ground, Vector3.UnitY, yaw, 0, 0, now);

    /// <summary>
    /// The welder on a part that touches another construct (<see cref="ConstructPlace.FindJoin"/>): the two become one.
    /// Your own, or one anyone may salvage (a wreck, a loose part, the map's); not someone else's working construct.
    /// </summary>
    public string? Join(ConstructWorld world, byte player, Vector3 eye, ushort body, int part)
    {
        ref var h = ref Hands[player];
        if (h.Seated)
            return "get out of the seat first";
        if (h.Tool is not (ConstructTool.Welder or ConstructTool.Wrench))
            return "take out the welder (4) to weld two constructs together (or the wrench, to bolt on a loose wrench part)";
        if (world.Get(body) is not { } c || part < 0 || part >= c.PartCount)
            return "that part is gone";
        c.WorldFrame(c.Parts[part].Seg, out var q, out var t);
        c.PartBounds(part, out var mn, out var mx);
        if (Reachable(eye, t + Vector3.Transform((mn + mx) * 0.5f, q), ConstructBuild.Reach) is { } far)
            return far;
        if (!ConstructPlace.FindJoin(world, c, part, out var plan))
            return "it doesn't touch another construct (free sockets face to face, square to each other)";
        if (!JoinTool(h.Tool, c, plan.Other))
            return "the wrench bolts on a loose wrench part: use the welder to weld two constructs together";
        if ((Contested(c, player) ?? Contested(plan.Other, player)) is { } defended)
            return defended;
        if (SalvageRefusal(c, player) != null || SalvageRefusal(plan.Other, player) != null)
            return "that construct is someone else's: you can only join your own (or wrecks and loose parts)";
        // Which one survives: the one aimed at, unless only the other is the player's own.
        var keep = c;
        if (c.Owner != player && plan.Other.Owner == player)
        {
            keep = plan.Other;
            if (!ConstructPlace.FindJoin(world, keep, plan.PartB, out plan, only: c))
                return "they don't meet square";
        }
        var other = plan.Other;
        if (world.CanMerge(keep, plan.Other) is { } why)
            return why;
        // Edits in progress on the one that goes stop.
        for (var i = 0; i < Hands.Length; i++)
            if (Hands[i].Edit is EditKind.Attach or EditKind.Remove && Hands[i].Body == other.Id)
                Hands[i].Edit = EditKind.None;
        var from = other.Id;
        if (!world.Merge(keep, plan))
            return "they can't be joined";
        keep.Owner = player; // (both were the player's, or free for anyone to take)
        Done.Add($"player {player}: joined construct {from} into construct {keep.Id}");
        return null;
    }

    public string? BeginRemove(ConstructWorld world, byte player, Vector3 eye, ushort body, int part, double now)
    {
        ref var h = ref Hands[player];
        if (h.Seated)
            return "get out of the seat first";
        if (world.Get(body) is not { } c || part < 0 || part >= c.PartCount)
            return "that part is gone";
        var def = c.DefOf(part);
        if (h.Tool == ConstructTool.None)
            return "take out a tool to take parts off (3: wrench, 4: welder)";
        if (!ConstructBuild.ToolFits(h.Tool, def))
            return $"the {def.Name} comes off with the {def.Tool.ToString().ToLowerInvariant()}";
        if (c.Driver != 255)
            return "someone is driving it";
        if (SalvageRefusal(c, player) is { } theirs)
            return theirs;
        if (Contested(c, player) is { } defended)
            return defended;
        c.WorldFrame(c.Parts[part].Seg, out var q, out var t);
        c.PartBounds(part, out var mn, out var mx);
        if (Reachable(eye, t + Vector3.Transform((mn + mx) * 0.5f, q), ConstructBuild.Reach) is { } far)
            return far;
        h.Edit = EditKind.Remove;
        h.Body = body;
        h.Part = (byte)part;
        h.Start = now;
        h.Duration = EditSeconds(h.Tool, ConstructBuild.RemoveSeconds(def));
        return FinishNow(world, player, eye);
    }

    public void Cancel(byte player) => Hands[player].Edit = EditKind.None;

    /// <summary>Every tick: edits whose time is up are done (checked again: the construct may have changed meanwhile).</summary>
    public void Tick(ConstructWorld world, double now, Func<byte, Vector3?> eyeOf)
    {
        for (var pid = 0; pid < Hands.Length; pid++)
        {
            ref var h = ref Hands[pid];
            CheckHand((byte)pid, ref h); // (a bad hands state is logged and emptied, never indexed with)
            if (h.Edit == EditKind.None || now < h.Start + h.Duration)
                continue;
            var kind = h.Edit;
            h.Edit = EditKind.None;
            if (eyeOf((byte)pid) is not { } eye)
                continue; // left, or dead
            string result;
            world.MergePlacer = (byte)pid; // (parts a merged receiver pops off go to them: ConstructMerge.cs)
            try
            {
                result = Complete(world, (byte)pid, ref h, kind, eye);
            }
            catch (Exception ex)
            {
                // Never a crash: the edit is dropped and logged (a bug to fix, not a game to end).
                result = $"failed: {ex.GetType().Name}: {ex.Message}";
                Console.WriteLine($"[sandbox] error: player {pid}'s {kind} threw: {ex}");
            }
            finally
            {
                world.MergePlacer = 255;
            }
            Done.Add($"player {pid}: {kind} {result}");
            GiveMergePops(world, (byte)pid);
        }
    }

    private string Complete(ConstructWorld world, byte player, ref SandboxHand h, EditKind kind, Vector3 eye)
    {
        switch (kind)
        {
            case EditKind.Attach:
            {
                if (ConstructParts.Get(h.Held) is not { } def || !ConstructBuild.ToolFits(h.Tool, def) || world.Get(h.Body) is not { } c)
                    return "failed: nothing to attach";
                if (ConstructBuild.Place(c, h.Part, h.Socket, def, h.Variant / 4, h.Variant % 4, out var g) is { } why)
                    return $"failed: {why}";
                var claim = Claims(c, player);
                var i = ConstructBuild.Attach(world, c, def, g);
                if (i < 0)
                    return "failed: too many parts";
                if (claim)
                {
                    // Building onto a wreck or a loose part: it becomes a construct, the builder's.
                    c.Wreck = false;
                    c.Owner = player;
                    world.StructureDirty.Add(c.Id);
                }
                h.Held = ConstructParts.None;
                Refill(player, ref h, def);
                var touches = ConstructPlace.FindJoin(world, c, i, out var jp) ? $" (it touches construct {jp.Other.Id}: F with the welder joins them)" : "";
                return $"ok: {def.Name} as part {i} of construct {c.Id}{(claim ? " (claimed)" : "")}{touches}";
            }
            case EditKind.PlaceNew:
            {
                if (ConstructParts.Get(h.Held) is not { } def || !ConstructBuild.ToolFits(h.Tool, def))
                    return "failed: nothing to place";
                // Checked again: something may have moved there meanwhile.
                var fp = new FreePlacement { Pos = h.At, Rot = h.PlaceRot, Turn = h.PlaceTurn, Anchor = h.PlaceAnchor };
                if ((ConstructPlace.LimitRefusal(world, player) ?? ConstructPlace.Fits(world, Query, def, fp, Feet(world))) is { } why)
                    return $"failed: {why}";
                if (ConstructBuild.PlaceNew(world, def, fp) is not { } c)
                    return "failed: the world is full";
                c.Owner = player; // (what a player sets down is theirs, and everything built onto it)
                h.Held = ConstructParts.None;
                Refill(player, ref h, def);
                return $"ok: {def.Name} as new construct {c.Id}";
            }
            case EditKind.Remove:
            {
                if (world.Get(h.Body) is not { } c || h.Part >= c.PartCount || !ConstructBuild.ToolFits(h.Tool, c.DefOf(h.Part)) || c.Driver != 255)
                    return "failed: that part is gone";
                if (SalvageRefusal(c, player) is { } theirs)
                    return $"failed: {theirs}";
                if (Contested(c, player) is { } defended)
                    return $"failed: {defended}";
                c.WorldFrame(c.Parts[h.Part].Seg, out var q, out var t);
                c.PartBounds(h.Part, out var mn, out var mx);
                var where = t + Vector3.Transform((mn + mx) * 0.5f, q);
                var salvage = Salvageable(c, player);
                var wrecked = c.Parts[h.Part].Broken;
                var partRot = c.Parts[h.Part].Rot;
                var from = c.Id;
                var vel = c.PointVel(where);
                var def = world.RemovePart(c, h.Part);
                if (def.PhysicsOnly && !wrecked)
                {
                    // Decision 21: a physics-only part is never an item: it is cut free as a body where it was.
                    if (DropLoose(world, def, where, q * Rot24.Quat(partRot)) is { } freed)
                    {
                        freed.Owner = player;
                        freed.Vel = vel;
                        return $"ok: {def.Name} cut free of construct {from} as construct {freed.Id} (physics-only: {def.PhysicsWhy})";
                    }
                    // (the world is full: counted, as below)
                }
                // Salvage (and, with infinite parts off, your own parts) goes into the inventory: a wrecked part as scrap.
                if (Inventory != null && (salvage || !InfiniteParts) && !(Survival && !wrecked))
                {
                    var inv = Inventory.Of(player);
                    if (wrecked)
                    {
                        inv.AddScrap(def.Scrap);
                        return $"ok: {def.Name} (wrecked) salvaged from construct {from} for {def.Scrap} scrap";
                    }
                    if (inv.Add(def.Id))
                        return $"ok: {def.Name} {(salvage ? "salvaged" : "taken back")} from construct {from} into the inventory ({inv.Count(def.Id)})";
                    // (that kind is full: to the player, as below)
                }
                // The hands; full hands, the backpack; else a pickup dropped where it was (what is held stays held).
                return Give(world, player, def, where, q * Rot24.Quat(partRot)) switch
                {
                    GiveResult.Hands => $"ok: {def.Name} into the hand",
                    GiveResult.Backpack => $"ok: {def.Name} into the backpack (hands full)",
                    GiveResult.Dropped => $"ok: {def.Name} dropped (hands full)",
                    GiveResult.Inventory => $"ok: {def.Name} into the inventory (hands full, no room to drop it)",
                    _ => $"failed: nowhere for the {def.Name}",
                };
            }
        }
        return "nothing";
    }

    /// <summary>Building onto this claims it: a wreck, or a loose part on the ground (it becomes the builder's construct).</summary>
    public static bool Claims(Construct c, byte player) => c.Wreck || c.Loose && c.Owner != player;

    /// <summary>Infinite parts off: after a fit, the next of the same kind comes out of the inventory into the hand (build a row of them without going back for each).</summary>
    private void Refill(byte player, ref SandboxHand h, ConstructPartDef def)
    {
        if (!InfiniteParts && Inventory != null && h.Held == ConstructParts.None && !def.PhysicsOnly && Inventory.Of(player).Take(def.Id))
            h.Held = def.Id;
    }

    // ---- The linking tool (milestone 2) -------------------------------------------------------------------------------

    private string? Linking(ConstructWorld world, byte player, Vector3 eye, ushort body, int part, out Construct? c)
    {
        ref var h = ref Hands[player];
        c = null;
        if (h.Seated)
            return "get out of the seat first";
        if (h.Tool != ConstructTool.Linker)
            return "take out the linking tool (5) to wire";
        if (world.Get(body) is not { } cc || part < 0 || part >= cc.PartCount)
            return "that part is gone";
        c = cc;
        if (Contested(cc, player) is { } defended)
            return defended;
        cc.WorldFrame(cc.Parts[part].Seg, out var q, out var t);
        cc.PartBounds(part, out var mn, out var mx);
        return Reachable(eye, t + Vector3.Transform((mn + mx) * 0.5f, q), ConstructBuild.Reach);
    }

    /// <summary>
    /// The linking tool: a wire from part <paramref name="from"/>'s output into part <paramref name="to"/>: on the same
    /// construct, or (milestone 3) into another construct's part within <see cref="ConstructWorld.XWireReach"/> (a wall
    /// button into a gate). Null, or why not.
    /// </summary>
    public string? Wire(ConstructWorld world, byte player, Vector3 eye, ushort body, int from, int to, ushort toBody = 0)
    {
        if (Linking(world, player, eye, body, from, out var c) is { } why)
            return why;
        if (toBody != 0 && toBody != body)
        {
            if (Linking(world, player, eye, toBody, to, out var ct) is { } far)
                return far;
            if (world.CanCrossWire(c!, from, ct!, to) is { } no)
                return no;
            if (!world.AddXWire(c!, from, ct!, to))
                return "no room for another wire";
            Done.Add($"player {player}: wire {c!.DefOf(from).Name} {from} of construct {c.Id} -> {ct!.DefOf(to).Name} {to} of construct {ct.Id}");
            return null;
        }
        if (Linking(world, player, eye, body, to, out _) is { } why2)
            return why2;
        if (ConstructBuild.CanWire(world, c!, from, to) is { } bad)
            return bad;
        if (!world.AddWire(c!, from, to))
            return "no room for another wire";
        Done.Add($"player {player}: wire {c!.DefOf(from).Name} {from} -> {c.DefOf(to).Name} {to} on construct {c.Id}");
        return null;
    }

    /// <summary>The linking tool: cuts every wire into or out of a part.</summary>
    public string? Unwire(ConstructWorld world, byte player, Vector3 eye, ushort body, int part)
    {
        if (Linking(world, player, eye, body, part, out var c) is { } why)
            return why;
        var cut = world.RemoveWires(c!, part) + world.RemoveXWires(c!, part); // (its own construct's wires, and those to other constructs)
        return cut > 0 ? null : $"no wires on the {c!.DefOf(part).Name}";
    }

    /// <summary>The linking tool: the next value of a logic part's setting (a timer's period, a reader's key...).</summary>
    public string? CycleSetting(ConstructWorld world, byte player, Vector3 eye, ushort body, int part)
    {
        if (Linking(world, player, eye, body, part, out var c) is { } why)
            return why;
        var d = c!.DefOf(part);
        if (d.Settings.Length == 0)
            return $"a {d.Name} has no setting";
        ref var p = ref c.Parts[part];
        p.Setting = (byte)((p.Setting + 1) % d.Settings.Length);
        ConstructConfig.ApplyPreset(ref p, d); // (a timer's period, a sensor's range... take the next preset)
        p.Count = 0;
        world.StructureDirty.Add(c.Id);
        c.Wake();
        return null;
    }

    /// <summary>Takes the next part type from the rack (each press moves on to the next kind).</summary>
    public string? TakeFromRack(SandboxMap? sandbox, byte player, Vector3 feet, ConstructWorld? world = null, Vector3? eye = null, float yaw = 0)
    {
        ref var h = ref Hands[player];
        if (sandbox == null || sandbox.RackParts.Count == 0)
            return "no parts rack on this map";
        if (Vector3.Distance(feet with { Y = 0 }, sandbox.Rack) > 4.5f)
            return "go to the parts rack";
        if (!InfiniteParts && Inventory != null)
            return RackFromInventory(sandbox, player, world, eye, yaw);
        var cur = ConstructParts.Get(h.Held)?.Name;
        var at = cur == null ? _rackAt[player] - 1 : sandbox.RackParts.FindIndex(n => n.Equals(cur, StringComparison.OrdinalIgnoreCase));
        var k = (at + 1) % sandbox.RackParts.Count;
        var next = ConstructParts.Find(sandbox.RackParts[k]);
        for (var skip = 0; next is { Offered: false } && skip < sandbox.RackParts.Count; skip++)
        {
            k = (at + 2 + skip) % sandbox.RackParts.Count;
            next = ConstructParts.Find(sandbox.RackParts[k]); // (replaced by routed links: not offered)
        }
        if (next == null)
            return "the rack is empty";
        _rackAt[player] = k + 1;
        if (next.PhysicsOnly)
        {
            // Decision 21: a physics-only part comes as a body in front of you (the next press, the next kind).
            if (world == null || eye is not { } e)
                return PhysicsOnlyRefusal(next, "be carried");
            return SpawnInFront(world, player, next, e, yaw, out _);
        }
        h.Held = next.Id;
        h.Edit = EditKind.None;
        return null;
    }

    private readonly int[] _rackAt = new int[256]; // (each player's last rack kind plus one: a physics-only part isn't in the hands to say)

    /// <summary>Infinite parts off: the rack's next kind that the player's inventory holds (what is in the hands goes back first).</summary>
    private string? RackFromInventory(SandboxMap sandbox, byte player, ConstructWorld? world, Vector3? eye, float yaw)
    {
        ref var h = ref Hands[player];
        var inv = Inventory!.Of(player);
        var cur = ConstructParts.Get(h.Held)?.Name;
        var at = cur == null ? -1 : sandbox.RackParts.FindIndex(n => n.Equals(cur, StringComparison.OrdinalIgnoreCase));
        ReturnHeld(player);
        if (h.Held != ConstructParts.None)
            return $"your inventory has no room for the {ConstructParts.Get(h.Held)?.Name ?? "part"} you hold";
        for (var k = 1; k <= sandbox.RackParts.Count; k++)
            if (ConstructParts.Find(sandbox.RackParts[(at + k) % sandbox.RackParts.Count]) is { } next && (!next.PhysicsOnly || world != null && eye != null) && inv.Take(next.Id))
            {
                if (next.PhysicsOnly)
                {
                    var why = SpawnInFront(world!, player, next, eye!.Value, yaw, out _);
                    if (why != null)
                        inv.Add(next.Id);
                    return why;
                }
                h.Held = next.Id;
                h.Edit = EditKind.None;
                return null;
            }
        return "your inventory has none of the rack's parts (salvage some: hold aim on a wreck with the tool it needs)";
    }

    /// <summary>
    /// The debug parts menu (UiSandboxParts.cs): any loaded part, by id, straight into the hands, wherever the player is
    /// (what is in them is replaced, as at the rack). Refused when the map turns the menu off (<see cref="SandboxMap.PartsMenu"/>).
    /// </summary>
    public string? TakePart(SandboxMap? sandbox, byte player, ushort part, ConstructWorld? world = null, Vector3? eye = null, float yaw = 0)
    {
        ref var h = ref Hands[player];
        if (sandbox is not { PartsMenu: true })
            return "the parts menu is off on this map";
        if (h.Seated)
            return "get out of the seat first";
        if (ConstructParts.Get(part) is not { } def)
            return "no such part";
        if (def.CreativeOnly && !InfiniteParts)
            return $"the {def.Name} is creative-only (a Zombies-economy part)";
        if (def.PhysicsOnly)
        {
            // Decision 21: the creative inventory spawns a physics-only part as a body in front of you (never into the hands).
            if (world == null || eye is not { } e)
                return PhysicsOnlyRefusal(def, "be carried");
            var counted = !InfiniteParts && Inventory != null;
            if (counted && !Inventory!.Of(player).Take(def.Id))
                return $"no {def.Name} in your inventory (salvage one)";
            var why = SpawnInFront(world, player, def, e, yaw, out _);
            if (why != null && counted)
                Inventory!.Of(player).Add(def.Id);
            return why;
        }
        if (!InfiniteParts && Inventory != null)
        {
            // Counted: one out of the inventory (what the hands held goes back first).
            var inv = Inventory.Of(player);
            if (inv.Count(def.Id) == 0 && h.Held != def.Id)
                return $"no {def.Name} in your inventory (salvage one)";
            if (h.Held == def.Id)
                return null;
            ReturnHeld(player);
            if (h.Held != ConstructParts.None)
                return $"your inventory has no room for the {ConstructParts.Get(h.Held)?.Name ?? "part"} you hold";
            inv.Take(def.Id);
        }
        h.Held = def.Id;
        h.Edit = EditKind.None;
        return null;
    }

    public string? PickUp(ConstructWorld world, byte player, Vector3 eye, ushort body)
    {
        ref var h = ref Hands[player];
        if (world.Get(body) is not { } c || !c.Loose)
            return "only loose parts can be picked up";
        if (world.StoreOf(c, 0) is { Empty: false })
            return $"empty the {c.DefOf(0).Name} first";
        if (Reachable(eye, c.WorldCom, ConstructBuild.UseReach) is { } far)
            return far;
        var def = c.DefOf(0);
        if (def.PhysicsOnly)
            return PhysicsOnlyRefusal(def, "be picked up");
        // Full hands: into the backpack if it fits there (else refused: it stays where it lies).
        if (h.Held != ConstructParts.None && (Backpack == null || PackSlotFor(Packs[player], def, out _, out _) < 0))
            return "your hands are full (drop what you hold first)";
        world.Remove(c);
        Give(world, player, def, c.WorldCom);
        return null;
    }

    // ---- Physical storage (decision 18; ConstructStorage.cs) --------------------------------------------------------

    private string? Storage(ConstructWorld world, byte player, Vector3 eye, ushort body, int part, out Construct? c)
    {
        ref var h = ref Hands[player];
        c = null;
        if (h.Seated)
            return "get out of the seat first";
        if (world.Get(body) is not { } cc || part < 0 || part >= cc.PartCount)
            return "that is gone";
        if (world.StoreOf(cc, part) == null)
            return "that isn't storage";
        c = cc;
        if (Contested(cc, player) is { } defended)
            return defended;
        cc.WorldFrame(cc.Parts[part].Seg, out var q, out var t);
        cc.PartBounds(part, out var mn, out var mx);
        return Reachable(eye, t + Vector3.Transform((mn + mx) * 0.5f, q), ConstructBuild.UseReach);
    }

    /// <summary>The part in the hands into slot <paramref name="slot"/> of a storage part (any hands, at once). Null, or why not.</summary>
    public string? StoreHeld(ConstructWorld world, byte player, Vector3 eye, ushort body, int part, int slot)
    {
        ref var h = ref Hands[player];
        if (ConstructParts.Get(h.Held) is not { } def)
            return "hold a part to put it away";
        if (Storage(world, player, eye, body, part, out var c) is { } why)
            return why;
        if (world.SetIn(c!, part, slot, def) is { } refused)
            return refused;
        h.Held = ConstructParts.None;
        h.Edit = EditKind.None;
        Done.Add($"player {player}: stored {def.Name} in slot {slot} of the {c!.DefOf(part).Name} (construct {c.Id})");
        return null;
    }

    /// <summary>The top stored part of a slot into empty hands. Null, or why not (someone else may have taken it first).</summary>
    public string? TakeStored(ConstructWorld world, byte player, Vector3 eye, ushort body, int part, int slot)
    {
        ref var h = ref Hands[player];
        if (Storage(world, player, eye, body, part, out var c) is { } why)
            return why;
        var st = world.StoreOf(c!, part)!;
        if (slot < 0 || slot >= st.SlotCount || ConstructParts.Get(st.Def[slot]) is not { } top)
            return "that slot is empty (someone took it)";
        // Full hands: into the backpack if it fits there (else refused: it stays stored).
        if (h.Held != ConstructParts.None && (Backpack == null || PackSlotFor(Packs[player], top, out _, out _) < 0))
            return "your hands are full (put down what you hold first)";
        if (world.TakeOut(c!, part, slot) is not { } def)
            return "that slot is empty (someone took it)";
        Give(world, player, def, eye);
        Done.Add($"player {player}: took {def.Name} from slot {slot} of the {c!.DefOf(part).Name} (construct {c.Id})");
        return null;
    }

    /// <summary>A bin: tip in the player's scrap (all of it that fits); with none on them, take out a load. Null, or why not.</summary>
    public string? UseBin(ConstructWorld world, byte player, Vector3 eye, ushort body, int part)
    {
        if (Storage(world, player, eye, body, part, out var c) is { } why)
            return why;
        var d = c!.DefOf(part);
        if (!d.IsBin)
            return "that isn't a bin";
        if (Inventory == null)
            return "nothing to put in";
        var inv = Inventory.Of(player);
        var st = world.StoreOf(c, part)!;
        if (inv.Scrap > 0)
        {
            var room = d.BulkCapacity - st.Amount;
            if (room <= 0)
                return $"the {d.Name} is full";
            var n = world.Deposit(c, part, inv.TakeScrap(Math.Min(room, inv.Scrap)));
            Done.Add($"player {player}: tipped {n} {d.BulkMaterial} into the {d.Name} (construct {c.Id}): {st.Amount}/{d.BulkCapacity}");
            return null;
        }
        if (st.Amount <= 0)
            return $"the {d.Name} is empty (salvage wrecked parts for scrap)";
        var got = world.Withdraw(c, part, Math.Min(ConstructStorage.TakeLoad, SandboxInventory.MaxScrap - inv.Scrap));
        inv.AddScrap(got);
        Done.Add($"player {player}: took {got} {d.BulkMaterial} from the {d.Name} (construct {c.Id}): {st.Amount}/{d.BulkCapacity}");
        return null;
    }

    public string? Drop(ConstructWorld world, byte player, Vector3 feet, float yaw)
    {
        ref var h = ref Hands[player];
        if (ConstructParts.Get(h.Held) is not { } def)
            return "nothing in your hands";
        var at = feet + World.FlatForward(yaw) * 1.3f + new Vector3(0, 0.5f, 0);
        if (ConstructBuild.PlaceNew(world, def, at, yaw) is not { } dropped)
            return "the world is full of constructs";
        dropped.Owner = player;
        h.Held = ConstructParts.None;
        h.Edit = EditKind.None;
        return null;
    }

    /// <summary>
    /// Use (any hands): a seat to sit in (the first seat drives; any other carries a passenger), a door or hinge to open
    /// or shut (refused while locked or run by its wires), a button, switch or lever to work.
    /// </summary>
    public string? Use(ConstructWorld world, byte player, Vector3 eye, ushort body, int part)
    {
        ref var h = ref Hands[player];
        if (world.Get(body) is not { } c || part < 0 || part >= c.PartCount)
            return "nothing there";
        if (c.Dirty)
            c.Rebuild();
        var d = c.DefOf(part);
        c.WorldFrame(c.Parts[part].Seg, out var q, out var t);
        c.PartBounds(part, out var mn, out var mx);
        if (Reachable(eye, t + Vector3.Transform((mn + mx) * 0.5f, q), ConstructBuild.UseReach) is { } far)
            return far;
        if (d.Role == PartRole.Seat)
        {
            ref var seat = ref c.Parts[part];
            if (seat.Broken)
                return "the seat is wrecked";
            if (seat.SitterPlus1 != 0 || part == c.DriveSeat && c.Driver != 255)
                return "someone is already in it";
            if (h.Seated)
                return "you are already sitting";
            h.Edit = EditKind.None;
            h.HasSeatLook = false;
            h.SeatBody = c.Id;
            h.SeatPart = (byte)part;
            seat.SitterPlus1 = (byte)(player + 1);
            if (part == c.DriveSeat)
            {
                c.Driver = player;
                c.DriverSeat = part;
            }
            c.Wake();
            return null;
        }
        if (d.Role == PartRole.Logic)
        {
            ref var lp = ref c.Parts[part];
            if (lp.Broken)
                return $"the {d.Name} is wrecked";
            switch (d.Logic)
            {
                case CLogic.Button:
                    if (ConstructConfig.Get(lp, d, CfgKey.Latched, 0) >= 0.5f)
                        lp.State ^= 1; // (latched: push on, push off)
                    else
                        lp.Count = (ushort)Math.Max(1, (int)MathF.Round(ConstructConfig.Get(lp, d, CfgKey.Hold, ConstructLogic.ButtonSeconds) * World.TickRate));
                    break;
                case CLogic.Switch:
                case CLogic.Cargo: // (a cargo volume: frozen or not)
                    lp.State ^= 1;
                    break;
                case CLogic.Lever:
                    lp.State = (byte)((lp.State + 1) & 3);
                    break;
                case CLogic.Selector:
                    lp.State = (byte)((lp.State + 1) % Math.Max(2, (int)d.Settings[Math.Min(lp.Setting, (byte)(d.Settings.Length - 1))]));
                    break;
                default:
                    return d.Configurable ? $"{d.Name}: the configure tool (tool wheel) changes its settings" : "nothing to use there";
            }
            c.Wake();
            return null;
        }
        if (d.Role == PartRole.Hitch)
            return Hitch(world, player, c, part);
        var seg = c.Parts[part].Seg;
        if (d.HasJoint && c.JointOf(part) is var own and >= 0)
            seg = (byte)(own + 1);
        if (seg > 0)
        {
            ref var jt = ref c.Joints[seg - 1];
            if (jt.Locked)
                return "it's locked";
            if (jt.Wired && jt.Motorised)
                return "its wires run it";
            if (jt.Type is JointType.Turntable or JointType.Steering)
                return "a turntable or steering pivot turns by its wires";
            jt.Open = !jt.Open;
            c.Wake();
            return null;
        }
        if (c.Loose)
            return PickUp(world, player, eye, body);
        return "nothing to use there";
    }

    /// <summary>
    /// Use on a hitch (milestone 3) with the wrench out: uncouple it, or couple it to the nearest free hitch of another
    /// construct that fits (a ball or a hook to an eye, the points together or within the chain's reach).
    /// </summary>
    private string? Hitch(ConstructWorld world, byte player, Construct c, int part)
    {
        ref var h = ref Hands[player];
        if (h.Tool != ConstructTool.Wrench)
            return "take out the wrench (3) to couple or uncouple";
        if (c.Parts[part].Broken)
            return "that hitch is wrecked";
        if (world.CouplingOf(c, part) is var k and >= 0)
        {
            world.Decouple(k, $"player {player} uncoupled it");
            Done.Add($"player {player}: uncoupled construct {c.Id}");
            return null;
        }
        if (world.NearestHitch(c, part, out var other) is var at and >= 0 && other != null)
        {
            world.Couple(c, part, other, at);
            Done.Add($"player {player}: coupled construct {c.Id} to construct {other.Id}");
            return null;
        }
        return c.DefOf(part).Hitch == HitchKind.Eye ? "no ball or hook near enough to couple to" : c.DefOf(part).HitchChain > 0 ? "no eye within the chain's reach" : "no eye near enough: back the ball up to one";
    }

    /// <summary>Out of the seat (or the seat went). Returns where the feet should go (beside the seat), or null if not seated.</summary>
    public Vector3? Stand(ConstructWorld world, byte player)
    {
        ref var h = ref Hands[player];
        if (!h.Seated)
            return null;
        var c = world.Get(h.SeatBody);
        h.SeatBody = 0;
        h.SeatKeys = 0;
        h.HasSeatLook = false;
        if (c == null)
            return null;
        var mine = c.SeatOf(player);
        if (mine >= 0)
        {
            c.Parts[mine].SitterPlus1 = 0;
            h.SeatPart = (byte)mine;
        }
        if (c.Driver == player)
        {
            c.Driver = 255;
            c.DriverSeat = -1;
            c.Throttle = c.Steer = c.SteerInput = 0;
        }
        var side = Vector3.Transform(Vector3.UnitX, c.Rot);
        side.Y = 0;
        side = side.LengthSquared() > 1e-4f ? Vector3.Normalize(side) : Vector3.UnitX;
        var seatPos = SeatFeet(c, h.SeatPart);
        return seatPos with { Y = MathF.Max(seatPos.Y, c.WorldCom.Y - 0.6f) } - side * (MathF.Max(c.LocalMax.X - c.LocalMin.X, 1.5f) * 0.5f + 0.9f);
    }

    /// <summary>Where a sitter's feet are: the seat's "at" point.</summary>
    public static Vector3 SeatFeet(Construct c, int part)
    {
        if (part < 0 || part >= c.PartCount)
            return c.WorldCom;
        ref var p = ref c.Parts[part];
        c.WorldFrame(p.Seg, out var q, out var t);
        return t + Vector3.Transform(p.Pos + Rot24.Apply(p.Rot, p.D.SeatAt), q);
    }

    /// <summary>Driving: the seated player's keys become throttle, steering and brake (server, every tick).</summary>
    public static void DriveInput(Construct c, Buttons b, float dt)
    {
        var throttle = ((b & Buttons.Forward) != 0 ? 1f : 0f) - ((b & Buttons.Back) != 0 ? 1f : 0f);
        var steer = ((b & Buttons.Right) != 0 ? 1f : 0f) - ((b & Buttons.Left) != 0 ? 1f : 0f);
        c.SteerInput = steer;
        c.Brake = (b & Buttons.Jump) != 0;
        // Reversing: Back while rolling forward brakes first.
        var fwd = Vector3.Dot(c.Vel, Vector3.Transform(c.SeatForward(), c.Rot));
        if (throttle < 0 && fwd > 1f)
        {
            c.Brake = true;
            throttle = 0;
        }
        c.Throttle = throttle;
        // Steering eases in and out, and less of it at speed.
        var speed = MathF.Abs(fwd);
        var max = 0.55f / (1f + speed * 0.06f);
        var target = steer * max;
        var rate = 2.5f * dt;
        c.Steer += Math.Clamp(target - c.Steer, -rate, rate);
        if (throttle != 0 || steer != 0)
            c.Wake();
    }
}
