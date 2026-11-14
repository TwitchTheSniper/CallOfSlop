using System.Numerics;

namespace CallOfSlop;

/// <summary>An edit in progress: attaching takes a few seconds, removing a little less (decision 2).</summary>
public enum EditKind : byte
{
    None,
    Attach, // the held part onto a socket of a construct
    PlaceNew, // the held part on the ground: a new construct
    Remove, // a part off a construct, into the hand (full hands: the backpack, else dropped; ConstructGive.cs)
    Weld, // a freeform weld: part Part of Body to part Part2 of Body2 where they are (board item fw01; ConstructEditsFreeform.cs)
    SocketWeld, // a socket weld: Body's part Part by its socket Socket onto Body2's part Part2 (socket Variant, or a face point At / PlaceTurn), turned Yaw degrees (board item sw01; SocketWeld.cs)
}

/// <summary>What one player holds and is doing (server-owned, replicated in the Sandbox mode-state tail).</summary>
public struct SandboxHand
{
    public ConstructTool Tool; // None: a gun or bare hands (use only, never edit)
    public ushort Held; // the part in hand (ConstructParts id, ConstructParts.None: none)
    public double ToolAskedAt, HeldGoneUntil; // client: when the local player last changed tool, and until when a snapshot still showing the put-away part is old news
    public ushort HeldGone; // client: the part a tool change put away (it is gone at once here; the server follows)
    public ushort MagFor, MagPack; // a magazine in hand: which part kind, and its rounds and type (ConstructAmmo.Pack); it goes with the hands, not into a backpack
    public EditKind Edit;
    public ushort Body;
    public ushort Part, Part2;
    public byte Socket, Variant;
    public ushort Body2; // Weld: the second body and its part
    public Vector3 At; // PlaceNew: the new construct's origin
    public Vector3 SrcAt; // SocketWeld from a point weld (Socket SocketWeld.FacePoint): the piece's point in its body frame (board item pw01)
    public byte SrcDir; // ...and its facing
    public float Yaw;
    public Quaternion PlaceRot; // PlaceNew: the new construct's frame (free placement)
    public byte PlaceTurn; // ...and the part's Rot24 turn in it
    public bool PlaceAnchor; // ...pinned to the ground
    public double Start, Duration;
    public ushort SeatBody; // 0: not sitting
    public ushort SeatPart;
    public ushort SeatKeys; // seated: the fire, aim, reload and roll keys the client holds (input readers; ConstructParts.KeyFire...)
    public double RackAt; // last rack take (a press cycles to the next part)
    public float SeatLook; // seated: the look relative to the seat's facing, as the client sends it (CActKind.SeatLook; protocol 80)
    public ushort RemoteBody; // the drone this sitter flies (GameSandboxRemote.cs): server
    public byte RemoteScan;
    public bool HasSeatLook; // (none heard since sitting down: the server works it out from the world look)
    public ushort GrabBody; // the physics gun holds this construct (0: nothing; ConstructGrab.cs)
    public Vector3 GrabLocal; // ...at this point (its body frame)
    public float GrabDist; // ...this far out along the look
    public byte GrabSeg; // a free joint's segment: the beam moves that alone, GrabLocal in its frame (0: the whole construct, ConstructGrab.cs)
    public ushort GrabHinge; // ...and the joint's part, to tell it from another once the construct has changed
    public Quaternion GrabRel; // ...turned this way relative to the look's yaw (the held thing swings round with you)
    public double GrabAskedAt; // client: when it last asked to grab or let go (the 10 Hz tail doesn't undo it meanwhile)
    // Contraption space (GameSandboxRide.cs; each machine's own, never sent): the frame this player stands in.
    public ushort RideBody; // 0: none
    public byte RideSeg;
    public Quaternion RideTurn; // their look's yaw, as a turn in the world about their up (the physics gun's "relative to the look")

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

    public ConstructEdits()
    {
        Reset();
        RuntimeParts.Track(this); // (what hands hold keeps a run-time part: RuntimeParts.cs)
    }

    /// <summary>
    /// Salvage (milestone 3, the loot loop): what anyone may take apart into their inventory: a wreck (cut off, or a part
    /// broken off), a loose part, a construct nobody built (the map's), or someone else's once it is disabled (a part of it
    /// wrecked). Someone else's working construct can't be taken apart; your own can (its parts go back into the hand, or
    /// with <see cref="InfiniteParts"/> off back into the inventory).
    /// </summary>
    public static bool Salvageable(Construct c, byte player) => c.Wreck || c.Loose || c.Owner == 255 || !Factions.Shares(c.Owner, player) && c.Damaged;

    /// <summary>Why a player may not take parts off this construct, or null.</summary>
    public static string? SalvageRefusal(Construct c, byte player) =>
        Factions.Shares(c.Owner, player) || Salvageable(c, player) ? null : "that construct is someone else's: disable it first (shoot a part off)";

    /// <summary>
    /// Server: the part in the player's hands goes back: to the network of a beacon in range of <paramref name="at"/> if it
    /// has room, else into their inventory (the parts are counted with infinite parts off).
    /// </summary>
    public void ReturnHeld(byte player, ConstructWorld? world = null, Vector3? at = null)
    {
        ref var h = ref Hands[player];
        if (InfiniteParts || Inventory == null || h.Held == ConstructParts.None)
            return;
        if (world != null && at is { } spot && ConstructParts.Get(h.Held) is { } held && ToNetwork(world, player, held, spot))
        {
            h.Held = ConstructParts.None;
            return;
        }
        if (Inventory.Of(player).Add(h.Held))
            h.Held = ConstructParts.None;
    }

    public void Reset()
    {
        for (var i = 0; i < Hands.Length; i++)
        {
            NudgeRelease((byte)i);
            Hands[i] = SandboxHand.Empty;
        }
        _magsPut.Clear();
        SelectResetAll();
        Array.Clear(Levels); // (tool upgrades: a saved world's records bring theirs back)
    }

    /// <summary>One player's hands and backpack empty again (a new match, a mode switch): no tool, part, grab, seat or edit.</summary>
    public void ResetPlayer(byte player)
    {
        NudgeRelease(player);
        Hands[player] = SandboxHand.Empty;
        MagsForget(player);
        ResetPack(player);
        SelectResetPlayer(player);
        ResetLevels(player); // (tool upgrades, ConstructEditsUpgrades.cs: a saved world's record brings theirs back)
    }

    public ref SandboxHand Hand(byte player) => ref Hands[player];

    private readonly PartRef.Cache?[] _heldKeys = new PartRef.Cache?[256];

    /// <summary>A hand's last held key on the wire (protocol 292: a run-time part in the hands travels by key; PartRef.WriteCached).</summary>
    public PartRef.Cache HeldKey(byte player) => _heldKeys[player] ??= new PartRef.Cache();

    public string? SetTool(byte player, ConstructTool tool, ConstructWorld? world = null, Vector3? eye = null, float yaw = 0)
    {
        ref var h = ref Hands[player];
        if (h.Tool != tool)
            h.Edit = EditKind.None; // putting the tool away stops the edit
        if (h.Held != ConstructParts.None && (h.Tool != tool || tool == ConstructTool.None) && !KeepsHeld(tool, ConstructParts.Get(h.Held)))
            PutAwayHeld(player, world, eye, yaw); // (None asked for again: a gun picked with a part in the hands, which would block firing)
        h.Tool = tool;
        return null;
    }

    /// <summary>
    /// Does a change to this tool keep the part in the hands (the user, 2026-11-06: switching tool puts it away)? Only the hands
    /// and the tool that fits that part: without those a part taken off a shelf could never be fitted. Client and server ask the same.
    /// </summary>
    public static bool KeepsHeld(ConstructTool tool, ConstructPartDef? held) =>
        tool == ConstructTool.Hands || held != null && ConstructBuild.ToolFits(tool, held);

    /// <summary>
    /// Server: a tool change puts the held part away. Creative: it simply goes. Otherwise a beacon's network or the inventory
    /// (<see cref="ReturnHeld"/>); a kind at its count limit, or no inventory, takes <see cref="Give"/>'s path with the hands
    /// full: the backpack, else dropped in front of the player. A magazine's rounds stay noted (<see cref="CarryMagazine"/>).
    /// </summary>
    private void PutAwayHeld(byte player, ConstructWorld? world, Vector3? eye, float yaw)
    {
        ref var h = ref Hands[player];
        var def = ConstructParts.Get(h.Held);
        if (InfiniteParts || def == null)
        {
            h.Held = ConstructParts.None;
            return;
        }
        ReturnHeld(player, world, eye);
        if (h.Held == ConstructParts.None)
            Notices.Add((player, $"{def.Name} put away"));
        else if (world != null && eye is { } at && Give(world, player, def, at + GravityFrames.FlatForward(at, yaw) * 0.8f) is not GiveResult.Lost)
            h.Held = ConstructParts.None;
    }

    private string? Reachable(Vector3 eye, Vector3 at, float reach) =>
        Vector3.Distance(eye, at) <= reach + 0.75f || _gantryReach && Vector3.Distance(_gantrySpot, at) <= _gantryRange ? null : "too far away"; // (the Build Gantry's camera: ConstructEditsAids.cs)

    /// <summary>
    /// Can a part be fitted on socket <paramref name="socket"/> of <paramref name="part"/> from <paramref name="eye"/>? The server's check and the
    /// client's ghost, so a ghost never offers what the click is refused. A big part's origin can be tens of metres from
    /// where it goes on, so the socket counts as well as the part's place; both in the frame of the segment they are on. Null, or why not.
    /// </summary>
    public static string? AttachReach(Vector3 eye, Construct c, int part, int socket, in ConstructGhost g, bool gantry = false, Vector3 spot = default, float range = 0)
    {
        c.SocketAt(part, socket, out var at, out _);
        var seg = c.Parts[part].Seg;
        Vector3 a = c.SegToWorld(seg, at), b = c.SegToWorld(seg, g.Pos);
        if (ConstructPhysics.RideFrameOf(c, part, socket, c.Pos, c.Rot, out var lq, out var lt))
        {
            a = lt + Vector3.Transform(at, lq); // (a weld socket on a walker leg's segment: as the leg is posed, ConstructWalkRiders.cs)
            b = lt + Vector3.Transform(g.Pos, lq);
        }
        var limit = ConstructBuild.Reach + 0.75f;
        if (Vector3.Distance(eye, a) <= limit || Vector3.Distance(eye, b) <= limit)
            return null;
        return gantry && (Vector3.Distance(spot, a) <= range || Vector3.Distance(spot, b) <= range) ? null : "too far away";
    }

    /// <summary>A part longer than this a side is reached at its nearest point, not its centre (a 10 m Smelter is used from beside it).</summary>
    public const float BigPartHalf = 1.6f;

    /// <summary>
    /// Where reach to a part is measured: its centre, or for a big part the point of its bounds nearest the eye, so the server
    /// agrees with the look ray (which stops at the surface). Allocates nothing.
    /// </summary>
    public static Vector3 ReachPoint(Construct c, int part, Vector3 eye)
    {
        c.WorldFrame(c.Parts[part].Seg, out var q, out var t);
        c.PartBounds(part, out var mn, out var mx);
        var half = (mx - mn) * 0.5f;
        if (MathF.Max(half.X, MathF.Max(half.Y, half.Z)) <= BigPartHalf)
            return t + Vector3.Transform((mn + mx) * 0.5f, q);
        return t + Vector3.Transform(Vector3.Clamp(Vector3.Transform(eye - t, Quaternion.Conjugate(q)), mn, mx), q);
    }

    public string? BeginAttach(ConstructWorld world, byte player, Vector3 eye, ushort body, int part, int socket, int variant, double now)
    {
        ref var h = ref Hands[player];
        if (h.Seated)
            return "get out of the seat first";
        if (ConstructParts.Get(h.Held) is not { } def)
            return "hold a part first (take one from the rack)";
        if (h.Tool == ConstructTool.None)
            return "take out the attach tool (4) to build";
        if (!ConstructBuild.ToolFits(h.Tool, def))
            return $"the {def.Name} needs the {def.Tool.Label()}";
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
        if (AttachReach(eye, c, part, socket, in g, _gantryReach, _gantrySpot, _gantryRange) is { } far)
            return far;
        h.Edit = EditKind.Attach;
        h.Body = body;
        h.Part = (ushort)part;
        h.Socket = (byte)socket;
        h.Variant = (byte)v;
        h.Start = now;
        h.Duration = EditSeconds(player, def, ConstructBuild.AttachSeconds(def));
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
            return "take out the attach tool (4) to build";
        if (!ConstructBuild.ToolFits(h.Tool, def))
            return $"the {def.Name} needs the {def.Tool.Label()}";
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
        h.Duration = EditSeconds(player, def, ConstructBuild.AttachSeconds(def));
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
        if (h.Tool != ConstructTool.Welder)
            return "take out the attach tool (4) to attach two constructs together";
        if (world.Get(body) is not { } c || part < 0 || part >= c.PartCount)
            return "that part is gone";
        c.WorldFrame(c.Parts[part].Seg, out var q, out var t);
        c.PartBounds(part, out var mn, out var mx);
        if (Reachable(eye, ReachPoint(c, part, eye), ConstructBuild.Reach) is { } far)
            return far;
        if (!ConstructPlace.FindJoin(world, c, part, out var plan))
            return "it doesn't touch another construct (free sockets face to face, square to each other)";
        if (c.FreeformCount > 0 && c.FrameOf(c.Parts[part].Seg) != 0 || plan.Other.FreeformCount > 0 && plan.Other.FrameOf(plan.Other.Parts[plan.PartB].Seg) != 0)
            return "that part is on a freeform-attached piece: use Freeform attach (the attach tool's outer ring)";
        if ((Contested(c, player) ?? Contested(plan.Other, player)) is { } defended)
            return defended;
        if (SalvageRefusal(c, player) != null || SalvageRefusal(plan.Other, player) != null)
            return "that construct is someone else's";
        // Which one survives: the one aimed at, unless only the other is the player's own.
        var keep = c;
        if (!Factions.Shares(c.Owner, player) && Factions.Shares(plan.Other.Owner, player))
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
            return "take out the attach tool (4) to take parts off";
        if (!ConstructBuild.ToolFits(h.Tool, def))
            return $"the {def.Name} comes off with the {def.Tool.Label()}";
        if (SandboxAim.Remove(h.Tool, c, part, player, Contested(c, player)).Refusal is { } refused)
            return refused; // (the shared predicate: driven, someone else's, contested; SandboxAim.cs)
        c.WorldFrame(c.Parts[part].Seg, out var q, out var t);
        c.PartBounds(part, out var mn, out var mx);
        if (Reachable(eye, ReachPoint(c, part, eye), ConstructBuild.Reach) is { } far)
            return far;
        h.Edit = EditKind.Remove;
        h.Body = body;
        h.Part = (ushort)part;
        h.Start = now;
        h.Duration = EditSeconds(player, def, ConstructBuild.RemoveSeconds(def));
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
            CarryMagazine((byte)pid, ref h); // (a magazine put away keeps its rounds: ConstructEditsMags.cs)
            if (_nudges[pid] is { Count: > 0 })
                NudgeTick(world, (byte)pid, eyeOf((byte)pid));
            if (h.Edit == EditKind.None || now < h.Start + h.Duration)
                continue;
            var kind = h.Edit;
            h.Edit = EditKind.None;
            if (eyeOf((byte)pid) is not { } eye)
                continue; // left, or dead
            Finish(world, (byte)pid, ref h, kind, eye);
        }
    }

    /// <summary>Does one finished edit (never throws: a bug is logged and the edit dropped) and logs it.</summary>
    private void Finish(ConstructWorld world, byte pid, ref SandboxHand h, EditKind kind, Vector3 eye)
    {
        string result;
        world.MergePlacer = pid; // (parts a merged receiver pops off go to them: ConstructMerge.cs)
        try
        {
            result = Complete(world, pid, ref h, kind, eye);
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
        GiveMergePops(world, pid);
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
                ConstructAmmo.Fitted(world, c, i, def, ref h, InfiniteParts); // (a magazine goes on with what is in it)
                if (claim)
                {
                    // Building onto a wreck or a loose part: it becomes a construct, the builder's.
                    c.Wreck = false;
                    c.Owner = player;
                    world.StructureDirty.Add(c.Id);
                }
                h.Held = ConstructParts.None;
                Refill(world, player, ref h, def, eye);
                SnapAfter(player, ref h, def, c, i);
                MirrorAttach(world, player, ref h, c, def, i, eye); // (a Mirror Table: its image too; ConstructEditsAids.cs)
                var touches = ConstructPlace.FindJoin(world, c, i, out var jp) ? $" (it touches construct {jp.Other.Id}: F with the attach tool joins them)" : "";
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
                ConstructAmmo.Fitted(world, c, 0, def, ref h, InfiniteParts); // (a magazine set down keeps what is in it)
                h.Held = ConstructParts.None;
                Refill(world, player, ref h, def, eye);
                SnapAfter(player, ref h, def, c, 0);
                return $"ok: {def.Name} as new construct {c.Id}";
            }
            case EditKind.Remove:
            {
                if (world.Get(h.Body) is not { } c || h.Part >= c.PartCount || !ConstructBuild.ToolFits(h.Tool, c.DefOf(h.Part)) || c.Driver != 255)
                    return "failed: that part is gone";
                if ((SalvageRefusal(c, player) ?? (Salvageable(c, player) ? null : Factions.RankRefusal(c.Owner, player))) is { } theirs)
                    return $"failed: {theirs}";
                if (Contested(c, player) is { } defended)
                    return $"failed: {defended}";
                if (WreckedAlone(c))
                    return "failed: that is a wrecked part already: haul it to a Recycler";
                c.WorldFrame(c.Parts[h.Part].Seg, out var q, out var t);
                c.PartBounds(h.Part, out var mn, out var mx);
                var where = t + Vector3.Transform((mn + mx) * 0.5f, q);
                var salvage = Salvageable(c, player);
                if (salvage || c.Damaged)
                    world.Note(CNoteKind.Strip, c.Id, player); // (a wreck: the story keeps someone else's, and any a faction claims: ConstructWorldNotes.cs, Game/GameStory.cs)
                var wrecked = c.Parts[h.Part].Broken;
                var wreckedPart = c.Parts[h.Part];
                var magPack = c.Parts[h.Part].Rounds;
                var partRot = c.Parts[h.Part].Rot;
                var from = c.Id;
                var vel = c.PointVel(where);
                var image = MirrorRemoveFind(world, player, eye, c, h.Part, out var imageDef, out var imagePos); // (a Mirror Table: its image comes off too)
                var def = world.RemovePart(c, h.Part);
                if (image)
                    MirrorRemove(world, player, ref h, from, imageDef, imagePos, eye);
                if (def.RuntimeMade && !wrecked && Inventory != null && (salvage || !InfiniteParts) && !Survival && Inventory.Of(player).Add(def.Id))
                {
                    // A made part (an extrusion, a pressed or cut part...) is counted the way catalogue parts are, never held
                    // (docs/sandbox/design/inventory-run-time-parts.md phase 2); 64 kinds full, it is cut free as below.
                    return $"ok: {PartRef.Shown(def)} {(salvage ? "salvaged" : "taken back")} from construct {from} into the inventory ({Inventory.Of(player).Count(def.Id)})";
                }
                if (def.RuntimeMade && !wrecked && (Survival || !InfiniteParts || salvage) && ToNetwork(world, player, def, eye))
                    return $"ok: {PartRef.Shown(def)} from construct {from} onto a rack on the network"; // (survival, in reach of a beacon: inventories phase 4)
                if ((def.PhysicsOnly || def.RuntimeMade) && !wrecked) // (a made part is always a body: the user's answer 1)
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
                if (wrecked)
                {
                    // Decision 31: no on-site scrap. A wrecked part stays a physical, darkened wrecked part (a body that can't be
                    // fitted to anything) for the player to haul to a Recycler.
                    if (DropWreck(world, wreckedPart, where, q * Rot24.Quat(partRot), vel, player) is { } wreck)
                        return $"ok: {def.Name} (wrecked) taken off construct {from} as wrecked part {wreck.Id}: haul it to a Recycler";
                    return $"failed: the world is full: the wrecked {def.Name} is lost";
                }
                // In range of a working beacon: into its network, if it has room (ConstructEditsBeacon.cs).
                if (ToNetwork(world, player, def, eye))
                    return $"ok: {def.Name} into the network from construct {from}";
                if (_mirroring && InfiniteParts)
                    return $"ok: {def.Name} off construct {from}"; // (a mirrored removal's image, in creative: the hands have the first one)
                // Salvage (and, with infinite parts off, your own parts) goes into the inventory.
                if (Inventory != null && (salvage || !InfiniteParts) && !Survival)
                {
                    var inv = Inventory.Of(player);
                    if (inv.Add(def.Id))
                        return $"ok: {def.Name} {(salvage ? "salvaged" : "taken back")} from construct {from} into the inventory ({inv.Count(def.Id)})";
                    // (that kind is full: to the player, as below)
                }
                // The hands; full hands, the backpack; else a pickup dropped where it was (what is held stays held).
                var given = Give(world, player, def, where, q * Rot24.Quat(partRot));
                MagazineBack(player, ref h, def, given, magPack); // (what is left in it comes off with it)
                return given switch
                {
                    GiveResult.Hands => $"ok: {def.Name} into the hand",
                    GiveResult.Backpack => $"ok: {def.Name} into the backpack (hands full)",
                    GiveResult.Dropped => $"ok: {def.Name} dropped (hands full)",
                    GiveResult.Inventory => $"ok: {def.Name} into the inventory (hands full, no room to drop it)",
                    _ => $"failed: nowhere for the {def.Name}",
                };
            }
            case EditKind.Weld:
                return CompleteFreeform(world, player, ref h);
            case EditKind.SocketWeld:
                return CompleteSocketWeld(world, player, ref h, eye);
        }
        return "nothing";
    }

    /// <summary>Building onto this claims it: a wreck, or a loose part on the ground (it becomes the builder's construct).</summary>
    public static bool Claims(Construct c, byte player) => c.Wreck || c.Loose && c.Owner != player;

    /// <summary>Infinite parts off: after a fit, the next of the same kind comes out of the inventory, else a beacon's network, into the hand (build a row of them without going back for each).</summary>
    private void Refill(ConstructWorld world, byte player, ref SandboxHand h, ConstructPartDef def, Vector3 eye)
    {
        if (InfiniteParts || h.Held != ConstructParts.None || def.PhysicsOnly || def.RuntimeMade)
            return; // (a made part is never held: the user's answer 1)
        if (Inventory != null && Inventory.Of(player).Take(def.Id) || NetworkOf(world, player, eye) is var net and not 0 && world.Beacons.TakePart(world, this, player, net, def))
            h.Held = def.Id;
    }

    // ---- The linking tool (milestone 2) -------------------------------------------------------------------------------

    private string? Linking(ConstructWorld world, byte player, Vector3 eye, ushort body, int part, out Construct? c, bool inReach = true)
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
        return inReach ? Reachable(eye, ReachPoint(cc, part, eye), ConstructBuild.Reach) : null;
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
        ReturnHeld(player, world, eye);
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
        return "your inventory has none of the rack's parts: salvage some from a wreck";
    }

    /// <summary>
    /// The debug parts menu (UiSandboxParts.cs): any loaded part, by id, straight into the hands, wherever the player is
    /// (what is in them is replaced, as at the rack). Refused when the map turns the menu off (<see cref="SandboxMap.PartsMenu"/>).
    /// </summary>
    public string? TakePart(SandboxMap? sandbox, byte player, ushort part, ConstructWorld? world = null, Vector3? eye = null, float yaw = 0)
    {
        ref var h = ref Hands[player];
        var net = NetworkOf(world, player, eye); // (a working beacon in range: its network hands out what it holds, ConstructEditsBeacon.cs)
        if (sandbox is not { PartsMenu: true } && net == 0)
            return NoMenuWhy(world, player, eye);
        if (h.Seated)
            return "get out of the seat first";
        if (ConstructParts.Get(part) is not { } def)
            return "no such part";
        if (def.CreativeOnly && !InfiniteParts)
            return $"the {def.Name} is creative-only (a Zombies-economy part)";
        if (def.PhysicsOnly || def.RuntimeMade)
        {
            // Decision 21: the creative inventory spawns a physics-only part as a body in front of you (never into the hands). A made part
            // (a lofted or cut panel, a rack: not every one is physics-only by its own rule) is always a body too: the user's answer 1,
            // docs/sandbox/design/inventory-run-time-parts.md.
            if (world == null || eye is not { } e)
                return PhysicsOnlyRefusal(def, "be carried");
            if (def.RuntimeMade && Inventory == null)
                return $"no {PartRef.Shown(def)} in your inventory";
            var counted = (!InfiniteParts || def.RuntimeMade) && Inventory != null; // (a made part is never endless: it comes out of the count, creative too)
            if (counted && Inventory!.Of(player).Count(def.Id) == 0 && net != 0) // (a made part too: off a rack on the network, inventories phase 4)
                return PhysicsFromNetwork(world, player, def, e, yaw, net);
            if (counted && !Inventory!.Of(player).Take(def.Id))
                return $"no {PartRef.Shown(def)} in your inventory (salvage one)";
            var why = SpawnInFront(world, player, def, e, yaw, out _);
            if (why != null && counted)
                Inventory!.Of(player).Add(def.Id);
            else if (why == null && InfiniteParts && !def.RuntimeMade)
            {
                h.Held = ConstructParts.None; // (creative: a new pick replaces what the hands hold; parts are endless, so it just goes back, never dropped)
                h.Edit = EditKind.None;
            }
            return why;
        }
        if (!InfiniteParts && Inventory != null)
        {
            // Counted: one out of the inventory (what the hands held goes back first).
            var inv = Inventory.Of(player);
            if (inv.Count(def.Id) == 0 && h.Held != def.Id)
                return net != 0 ? FromNetwork(world!, player, def, eye!.Value, net) : $"no {def.Name} in your inventory (salvage one)";
            if (h.Held == def.Id)
                return null;
            ReturnHeld(player, world, eye);
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
        if (h.Held != ConstructParts.None && !PackTakes(player, def))
            return "your hands are full (drop what you hold first)";
        var magPack = c.Parts[0].Rounds;
        world.Remove(c);
        MagazineBack(player, ref h, def, Give(world, player, def, c.WorldCom), magPack); // (a loose magazine keeps what is in it)
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
        if (cc.DefOf(part).IsVolumeStore)
            return Reachable(eye, ConstructStoreVolume.NearestPoint(cc, part, eye), ConstructBuild.UseReach); // (its box: the part itself may be far off along it)
        cc.WorldFrame(cc.Parts[part].Seg, out var q, out var t);
        cc.PartBounds(part, out var mn, out var mx);
        return Reachable(eye, ReachPoint(cc, part, eye), ConstructBuild.UseReach);
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
        if (h.Held != ConstructParts.None && !PackTakes(player, top))
            return "your hands are full (put down what you hold first)";
        if (world.TakeOut(c!, part, slot) is not { } def)
            return "that slot is empty (someone took it)";
        Give(world, player, def, eye);
        Done.Add($"player {player}: took {def.Name} from slot {slot} of the {c!.DefOf(part).Name} (construct {c.Id})");
        return null;
    }

    /// <summary>The material a player would tip into this bin: the bin's own when it has some, else the first one they carry that it takes (0: none).</summary>
    public static byte TipMaterial(SandboxInventory inv, ConstructPartDef d, CStore st)
    {
        if (st.Amount > 0)
            return inv.Materials[st.Material] > 0 ? st.Material : (byte)0;
        for (byte m = 1; m < SandboxInventory.MaxMaterials; m++)
            if (inv.Materials[m] > 0 && ConstructStorage.BinTakes(d, m))
                return m;
        return 0;
    }

    /// <summary>A bin: tip in the materials the player carries (as much as fits); with none to tip, take out a load. Null, or why not.</summary>
    public string? UseBin(ConstructWorld world, byte player, Vector3 eye, ushort body, int part, Vector3 look = default)
    {
        if (world.Get(body) is { } holder && ConstructPress.MachineOf(holder, part) is var machine and >= 0)
            return TakeFromMachine(world, player, eye, holder, machine, part, out _);
        if (Storage(world, player, eye, body, part, out var c) is { } why)
            return why;
        var d = c!.DefOf(part);
        if (d.IsAssembler)
            return UseAssembler(world, player, eye, c, part);
        if (d.IsPress)
            return UsePress(world, player, eye, c, part);
        if (d.IsPieceRack)
        {
            // A Piece Rack: the top piece of the stack looked at (else the last in) comes off, into the hands (a physics-only piece: a body over the rack).
            if (!world.RackPick(c, part, world.StoreOf(c, part)!, eye, look, out var slab, out var lane) || world.RackTakeAt(c, part, slab, lane) is not { } piece)
                return "the rack is empty";
            ConstructWorld.RackBeside(c, part, piece, eye, 0, out var over, out var turn);
            Give(world, player, piece, over, turn);
            Done.Add($"player {player}: took {piece.Name} off the {d.Name} (construct {c.Id})");
            return null;
        }
        if (!d.IsBin)
            return "that isn't a bin";
        var st = world.StoreOf(c, part)!;
        if (d.IsVolumeStore && st.KindN > 0)
        {
            // Parts: the kind put in last, one at a time, by the same rules as a slot.
            var top = ConstructParts.Get(st.KindDef![st.KindN - 1]);
            if (top == null)
                return "that is empty";
            ref var h = ref Hands[player];
            if (h.Held != ConstructParts.None && !PackTakes(player, top))
                return "your hands are full (put down what you hold first)";
            world.TakePart(c, part, top.Id);
            Give(world, player, top, eye);
            Done.Add($"player {player}: took {top.Name} from the {d.Name} (construct {c.Id})");
            return null;
        }
        if (Inventory == null)
            return "nothing to put in";
        var inv = Inventory.Of(player);
        var names = RecyclingTable.Current;
        if (d.IsRefiner && ConstructRefining.TipMaterial(inv, d, st, ConstructRefining.SetOf(c.Parts[part], d)) is var feed and not 0)
        {
            var fed = world.Feed(c, part, inv.Materials[feed], feed);
            inv.TakeMaterial(feed, fed);
            Done.Add($"player {player}: tipped {fed} {names.NameOf(feed)} into the {d.Name} (construct {c.Id}): {st.InAmount}/{d.InCapacity}");
            return null;
        }
        var tip = d.IsRefiner ? (byte)0 : TipMaterial(inv, d, st); // (a production machine's output only ever comes out)
        if (tip != 0)
        {
            var cap = ConstructStorage.Capacity(c, part);
            var room = cap - st.Amount;
            if (room <= 0)
                return $"the {d.Name} is full";
            var n = world.Deposit(c, part, Math.Min(room, inv.Materials[tip]), tip);
            inv.TakeMaterial(tip, n);
            Done.Add($"player {player}: tipped {n} {names.NameOf(tip)} into the {d.Name} (construct {c.Id}): {st.Amount}/{cap}");
            return null;
        }
        if (st.Amount <= 0)
            return d.IsRefiner ? $"nothing in the {d.Name}'s output, and you carry nothing it takes ({RefiningTable.Current.Takes(d.RefineMachine, d.Tier)})" : $"the {d.Name} is empty (feed wrecked parts to a Recycler)";
        var mat = st.Material;
        var got = world.Withdraw(c, part, Math.Min(ConstructStorage.TakeLoad, SandboxInventory.MaxCount - inv.Materials[mat]));
        if (got <= 0)
            return "you can't carry any more of that";
        inv.AddMaterial(mat, got);
        Done.Add($"player {player}: took {got} {names.NameOf(mat)} from the {d.Name} (construct {c.Id}): {st.Amount}/{ConstructStorage.Capacity(c, part)}");
        return null;
    }

    /// <summary>
    /// F at an Assembler: the held part if its recipe wants it, else the materials it wants from what you carry, else a part
    /// off the tray, else its input back (the way to clear a machine whose recipe changed). Null, or why not.
    /// </summary>
    private string? UseAssembler(ConstructWorld world, byte player, Vector3 eye, Construct c, int part)
    {
        var d = c.DefOf(part);
        var st = world.StoreOf(c, part)!;
        var r = ConstructAssembling.RecipeOf(AssemblingTable.Current, c.Parts[part], d);
        ref var h = ref Hands[player];
        var names = RecyclingTable.Current;
        if (ConstructParts.Get(h.Held) is { } held && world.WantsPart(c, part, held.Id) && world.FeedPart(c, part, held))
        {
            h.Held = ConstructParts.None;
            h.Edit = EditKind.None;
            Done.Add($"player {player}: put a {held.Name} into the {d.Name} (construct {c.Id})");
            return null;
        }
        var inv = Inventory?.Of(player);
        if (inv != null && ConstructAssembling.TipMaterial(inv, d, st, r) is var tip and not 0)
        {
            var fed = world.FeedAssembler(c, part, inv.Materials[tip], tip);
            inv.TakeMaterial(tip, fed);
            Done.Add($"player {player}: tipped {fed} {names.NameOf(tip)} into the {d.Name} (construct {c.Id})");
            return null;
        }
        for (var k = 0; k < st.SlotCount; k++)
            if (st.Def[k] != ConstructParts.None)
                return TakeStored(world, player, eye, c.Id, part, k);
        if (st.MixN > 0 && inv != null)
        {
            var m = st.MixMat![0];
            var n = Math.Min(Math.Min(ConstructStorage.TakeLoad, st.MixAmount![0]), SandboxInventory.MaxCount - inv.Materials[m]);
            if (n <= 0)
                return "you can't carry any more of that";
            world.AddInput(st, m, -n);
            inv.AddMaterial(m, n);
            Done.Add($"player {player}: took {n} {names.NameOf(m)} back from the {d.Name} (construct {c.Id})");
            return null;
        }
        if (st.KindN > 0 && ConstructParts.Get(st.KindDef![st.KindN - 1]) is { } back)
        {
            if (h.Held != ConstructParts.None && !PackTakes(player, back))
                return "your hands are full (put down what you hold first)";
            world.AddInputPart(st, back.Id, -1);
            Give(world, player, back, eye);
            Done.Add($"player {player}: took a {back.Name} back from the {d.Name} (construct {c.Id})");
            return null;
        }
        return r == null ? $"the {d.Name} has no recipe: choose one with the configure tool" : $"the {r.Name} takes {r.Batch}, and you carry none of what it still needs";
    }

    /// <summary>F on what a machine holds (<see cref="ConstructPress.MachineOf"/>): it comes out, whatever state the machine and the piece are in. Null, or why not.</summary>
    public string? TakeFromMachine(ConstructWorld world, byte player, Vector3 eye, Construct c, int machine, int held, out Construct? piece)
    {
        piece = null;
        if (Storage(world, player, eye, c.Id, machine, out _) is { } why)
            return why;
        var name = c.Parts[held].D.Name;
        var machineName = c.DefOf(machine).Name;
        if (ConstructPress.TakeOut(world, c, machine, held, out var die, out piece) is { } refused)
            return refused;
        if (die != null)
            Give(world, player, die, eye);
        Done.Add($"player {player}: took the {name} out of the {machineName} (construct {c.Id})");
        return null;
    }

    /// <summary>F on a Press or Die Lathe: a held die goes in (the one there comes back); else what is nearest comes out, then the tray.</summary>
    private string? UsePress(ConstructWorld world, byte player, Vector3 eye, Construct c, int part)
    {
        var d = c.DefOf(part);
        var st = world.StoreOf(c, part)!;
        ref var h = ref Hands[player];
        if (ConstructParts.Get(h.Held) is { IsPressDie: true } die)
        {
            if (d.PressDieSocket < 0)
                return $"the {d.Name} takes no die: a setting says what it cuts";
            if (ConstructPress.DieClassOf(die) != d.Sockets[d.PressDieSocket].Class)
                return $"the {die.Name} doesn't fit the {d.Name}: a die goes in a press of its own size";
            ConstructPartDef? out0 = null;
            if (ConstructPress.DieIn(c, part) is var old and >= 0)
            {
                ConstructPress.TakeOut(world, c, part, old, out out0);
                c.Rebuild();
                if (old < part)
                    part--; // (the parts close up, in order)
            }
            h.Held = ConstructParts.None;
            h.Edit = EditKind.None;
            if (!ConstructPress.FitDie(world, c, part, die))
                return $"the {d.Name} has no room for the die";
            if (out0 != null)
                Give(world, player, out0, eye);
            Done.Add($"player {player}: fitted a {die.Name} in the {d.Name} (construct {c.Id})");
            return null;
        }
        // Bay 2's kit: the part held, or the material carried, that the pattern wants.
        if (ConstructParts.Get(h.Held) is { } kitPart && !d.IsLathe && ConstructPress.WantsKitPart(world, c, part, kitPart.Id) && ConstructPress.FeedKitPart(world, c, part, kitPart))
        {
            h.Held = ConstructParts.None;
            h.Edit = EditKind.None;
            Done.Add($"player {player}: put a {kitPart.Name} in the {d.Name}'s bay 2 (construct {c.Id})");
            return null;
        }
        if (!d.IsLathe && Inventory?.Of(player) is { } kitInv && ConstructPress.SizedDieOf(PressTable.Current, c, part) is { } kitDie
            && ConstructPress.KitWanted(kitDie, c, part, out _, out _) is { Part: null, Material: var km } && km != 0 && kitInv.Materials[km] > 0)
        {
            var fed = ConstructPress.FeedKitMaterial(world, c, part, kitInv.Materials[km], km);
            if (fed > 0)
            {
                kitInv.TakeMaterial(km, fed);
                Done.Add($"player {player}: tipped {fed} {RecyclingTable.Current.NameOf(km)} into the {d.Name}'s bay 2 (construct {c.Id})");
                return null;
            }
        }
        if (ConstructPress.Nearest(c, part, eye) is var take and >= 0)
        {
            var name = c.Parts[take].D.Name;
            if (ConstructPress.TakeOut(world, c, part, take, out var off) is { } why)
                return why;
            if (off != null)
                Give(world, player, off, eye);
            Done.Add($"player {player}: took the {name} out of the {d.Name} (construct {c.Id})");
            return null;
        }
        for (var k = 0; k < st.SlotCount; k++)
            if (st.Def[k] != ConstructParts.None)
                return TakeStored(world, player, eye, c.Id, part, k);
        // Bay 2's kit back: the part kinds, then the material.
        if (!d.IsLathe && st.KindN > 0 && ConstructParts.Get(st.KindDef![st.KindN - 1]) is { } back)
        {
            if (h.Held != ConstructParts.None && !PackTakes(player, back))
                return "your hands are full (put down what you hold first)";
            world.AddInputPart(st, back.Id, -1);
            Give(world, player, back, eye);
            Done.Add($"player {player}: took a {back.Name} back from the {d.Name}'s bay 2 (construct {c.Id})");
            return null;
        }
        if (!d.IsLathe && st.MixN > 0 && Inventory?.Of(player) is { } inv)
        {
            var m = st.MixMat![0];
            var n = Math.Min(Math.Min(ConstructStorage.TakeLoad, st.MixAmount![0]), SandboxInventory.MaxCount - inv.Materials[m]);
            if (n <= 0)
                return "you can't carry any more of that";
            world.AddInput(st, m, -n);
            inv.AddMaterial(m, n);
            Done.Add($"player {player}: took {n} {RecyclingTable.Current.NameOf(m)} back from the {d.Name}'s bay 2 (construct {c.Id})");
            return null;
        }
        return d.IsLathe ? $"the {d.Name} is empty: set a block in its bay with the physics gun" : $"the {d.Name} is empty: set blanks in bay 1 (and 3) with the physics gun, a kit in bay 2 if its pattern wants one, and fit a die";
    }

    public string? Drop(ConstructWorld world, byte player, Vector3 feet, float yaw)
    {
        ref var h = ref Hands[player];
        if (ConstructParts.Get(h.Held) is not { } def)
            return "nothing in your hands";
        var at = feet + GravityFrames.FlatForward(feet, yaw) * 1.3f + GravityFrames.UpAt(feet) * 0.5f; // (a planet: ahead and up in its frame there, never into the ground)
        if (ConstructBuild.PlaceNew(world, def, at, yaw) is not { } dropped)
            return "the world is full of constructs";
        dropped.Owner = player;
        ConstructAmmo.Fitted(world, dropped, 0, def, ref h, false); // (a magazine dropped keeps what is in it)
        if (h.RideBody != 0 && world.Get(h.RideBody) is { } deck && h.RideSeg <= deck.JointCount)
            dropped.Vel = ConstructRide.SurfaceVel(deck, h.RideSeg, at); // (dropped on a moving deck, it keeps the deck's speed)
        h.Held = ConstructParts.None;
        h.Edit = EditKind.None;
        return null;
    }

    /// <summary>
    /// Use (any hands): a seat to sit in (one linked to a Driving Module on the drivetrain drives; any other just sits), a door or hinge to open
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
        // A seat on a moving construct is reached a little further (it moved on since the player aimed: board item ba01).
        var reach = ConstructBuild.UseReach + (d.Role == PartRole.Seat && !c.Anchored && !c.Asleep ? MathF.Min(c.Vel.Length(), SeatReachSpeed) * 0.2f : 0);
        if (Reachable(eye, ReachPoint(c, part, eye), reach) is { } far)
            return far;
        if (d.Role == PartRole.Seat)
        {
            ref var seat = ref c.Parts[part];
            if (d.RemoteReceiver)
                return $"the {d.Name} is flown from a console or a transmitter on its channel";
            if (seat.Broken)
                return "the seat is wrecked";
            if (seat.SitterPlus1 != 0 || part == c.DriveSeat && c.Driver != 255)
                return "someone is already in it";
            if (h.Seated)
                return "you are already sitting";
            h.Edit = EditKind.None;
            h.HasSeatLook = false;
            h.SeatBody = c.Id;
            h.SeatPart = (ushort)part;
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
                case CLogic.Switch when ConstructConfig.Get(lp, d, CfgKey.Latched, 1) < 0.5f:
                    lp.Count = (ushort)Math.Max(1, (int)MathF.Round(ConstructConfig.Get(lp, d, CfgKey.Hold, ConstructLogic.ButtonSeconds) * World.TickRate));
                    break;
                case CLogic.KeySwitch:
                    if (LogicInputs.KeyRefusal(c, player, h.Held) is { } locked)
                        return locked;
                    lp.State ^= 1;
                    break;
                case CLogic.Switch:
                case CLogic.Cargo: // (a cargo volume: frozen or not)
                case CLogic.Conveyor: // (a conveyor nothing is wired into: on or off)
                case CLogic.GravityWell: // (gravity parts: on or off)
                case CLogic.GravityGen:
                    lp.State ^= 1;
                    break;
                case CLogic.JumpDrive: // (phase 6, Game/GameSpaceJump.cs: start or stop the charge, in space only)
                    if (c.SpaceShip == 0)
                        return $"the {d.Name} works only in space: fly out past the sky first";
                    lp.State ^= 1;
                    if ((lp.State & 1) == 0)
                        lp.Count = 0;
                    break;
                case CLogic.Launch:
                    lp.State |= 1; // (a catapult: fire)
                    break;
                case CLogic.Mooring:
                    lp.State ^= 1; // (a mast: cast off, tie on)
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
        if (ConstructDoors.Use(world, c, part) is { } door)
            return door.Length == 0 ? null : door; // (an event door, or a part on its leaf: ConstructDoors.cs)
        var seg = c.MoveSeg(c.Parts[part].Seg); // (a freeform weld is rigid: nothing there to open)
        if (d.HasJoint && c.RailJointOf(part) is var own and >= 0)
            seg = own + 1;
        if (seg > 0)
        {
            ref var jt = ref c.Joints[seg - 1];
            if (jt.Locked)
                return "it's locked";
            if (jt.Wired && jt.Motorised)
                return "its wires run it";
            if (jt.Type is JointType.Turntable or JointType.Steering)
                return "a turntable or steering pivot turns by its wires";
            if (jt.Type == JointType.Flex)
            {
                // (the joint itself says what it does; a part hanging on it is like any other: the game then tries grabbing on)
                if (d.HasJoint)
                    return "a flexible joint has no motor: it bends under load";
                return c.Loose ? PickUp(world, player, eye, body) : NothingToUse;
            }
            if (jt.FreeMoving)
                return d.HasJoint ? "it has no motor: push it, or use the physics gun" : c.Loose ? PickUp(world, player, eye, body) : NothingToUse;
            jt.Open = !jt.Open;
            c.Wake();
            return null;
        }
        if (c.Loose)
            return PickUp(world, player, eye, body);
        return NothingToUse;
    }

    /// <summary>What <see cref="Use"/> says when the part does nothing (the game then tries grabbing on: ConstructRide.GrabOn).</summary>
    public const string NothingToUse = "nothing to use there";

    /// <summary>Board item ba01: a seat is boarded from a moving construct up to this speed (m/s) with the extra reach.</summary>
    public const float SeatReachSpeed = 8f;

    /// <summary>
    /// Use on a hitch (milestone 3) with the attach tool out: uncouple it, or couple it to the nearest free hitch of another
    /// construct that fits (a ball or a hook to an eye, the points together or within the chain's reach).
    /// </summary>
    private string? Hitch(ConstructWorld world, byte player, Construct c, int part)
    {
        ref var h = ref Hands[player];
        if (h.Tool != ConstructTool.Welder)
            return "take out the attach tool (4) to couple or uncouple";
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
            world.Note(CNoteKind.Tow, c.Id, player, other.Id); // (the story: ConstructWorldNotes.cs)
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
            h.SeatPart = (ushort)mine;
        }
        if (c.Driver == player)
            StopDriving(c);
        var side = Vector3.Transform(Vector3.UnitX, c.Rot);
        side.Y = 0;
        side = side.LengthSquared() > 1e-4f ? Vector3.Normalize(side) : Vector3.UnitX;
        var seatPos = SeatFeet(c, h.SeatPart);
        return seatPos with { Y = MathF.Max(seatPos.Y, c.WorldCom.Y - 0.6f) } - side * (MathF.Max(c.LocalMax.X - c.LocalMin.X, 1.5f) * 0.5f + 0.9f);
    }

    /// <summary>Nobody drives it now (out of the seat, or the seat lost its link to the drivetrain).</summary>
    public void StopDriving(Construct c)
    {
        c.Driver = 255;
        c.DriverSeat = -1;
        c.Throttle = c.Steer = c.SteerInput = 0;
        c.Brake = false;
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

    /// <summary>The roll axis of a seat's keys (-1 left, 1 right; <see cref="ConstructParts.KeyRollLeft"/>).</summary>
    public static float RollOf(int keys) => ((keys & ConstructParts.KeyRollRight) != 0 ? 1f : 0f) - ((keys & ConstructParts.KeyRollLeft) != 0 ? 1f : 0f);

    /// <summary>Driving: the seated player's keys become throttle, steering and brake (server, every tick).</summary>
    public static void DriveInput(Construct c, Buttons b, float dt)
    {
        var throttle = ((b & Buttons.Forward) != 0 ? 1f : 0f) - ((b & Buttons.Back) != 0 ? 1f : 0f);
        var steer = ((b & Buttons.Right) != 0 ? 1f : 0f) - ((b & Buttons.Left) != 0 ? 1f : 0f);
        c.SteerInput = steer;
        c.DriveKeys = b;
        c.LiftInput = ((b & Buttons.Sprint) != 0 ? 1f : 0f) - ((b & Buttons.Crouch) != 0 ? 1f : 0f); // (the hover hold: ConstructThrust.cs)
        c.Brake = (b & Buttons.Jump) != 0;
        // Reversing: Back while rolling forward brakes first.
        var fwd = Vector3.Dot(c.Vel, Vector3.Transform(c.DriveFwd, c.Rot));
        if (throttle < 0 && fwd > 1f && c.StabCount == 0) // (on a Flight Stabilizer S is the elevator: it doesn't brake a plane as it rotates; the handbrake still does)
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
        if (throttle != 0 || steer != 0 || c.LiftInput != 0 && (c.ThrusterCount > 0 || c.RotorCount > 0 || c.LegCount > 0))
            c.Wake();
    }
}
