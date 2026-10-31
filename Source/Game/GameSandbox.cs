using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>What a <see cref="Msg.CAct"/> asks for (its first byte).</summary>
public enum CActKind : byte
{
    Tool, // u8 tool
    Attach, // u16 body, u8 part, u8 socket, u8 variant
    PlaceNew, // vec surface point, vec normal, f32 yaw, u8 Rot24 turn, u8 flags (grid, anchor): free placement (protocol 84; before: vec ground, f32 yaw)
    Remove, // u16 body, u8 part
    Cancel,
    Use, // u16 body, u8 part
    Stand,
    Rack, // u16 pick (protocol 90; u8 since 79): 0 the rack's next kind (at the rack); n: part id n - 1 from the debug parts menu, anywhere
    Drop,
    PickUp, // u16 body
    Wire, // u16 body, u8 from part, u8 to part, u16 to body (the linking tool; milestone 2; protocol 83: the to body, 0 the same construct)
    Unwire, // u16 body, u8 part: cut every wire on it
    Setting, // u16 body, u8 part: a logic part's next setting
    SeatKeys, // u8: seated, the fire / aim / reload keys held (input readers)
    Blueprint, // u8 blueprint index: the debug parts menu builds it in front of the player (protocol 79)
    SeatLook, // i16: seated, the look's yaw relative to the seat's facing (x pi / 32767), unreliable, up to ~30 Hz (protocol 80)
    Join, // u16 body, u8 part: the welder joins the construct this part touches into this one (protocol 84)
    StoreIn, // u16 body, u8 part, u8 slot: the part in the hands into a storage slot (physical storage; protocol 85)
    TakeStored, // u16 body, u8 part, u8 slot: the stored part into empty hands (protocol 85)
    Bin, // u16 body, u8 part: tip scrap into a bin, or take a load out (protocol 85)
    Stow, // the part in the hands into the backpack (protocol 85)
    Draw, // the next part in the backpack into empty hands (protocol 85)
    Grab, // u16 body, 3 x i16 grab point (body frame, cm): the physics gun takes hold (protocol 88; GameSandboxPhysGun.cs)
    GrabSteer, // u16 distance (cm), 4 x i16 turn relative to the look's yaw: the held thing's distance and turn, unreliable, up to ~30 Hz (protocol 88)
    GrabRelease, // let go of what the physics gun holds (protocol 88)
    GrabFreeze, // freeze what the physics gun holds where it is, and let go (protocol 88)
    UnfreezeAll, // u8 everyone (the host only): unfreeze everything this player froze (protocol 88)
    Configure, // u16 body, u8 n, n x u8 part, u8 m, m x (u8 slot, f32 value): the configure tool sets settings (protocol 89)
    Route, // u16 body, u8 type (protocol 98: the high nibble, a pipe style, is gone), u8 from, u8 from port, u8 parent link, u8 parent segment, 3 x i16 junction (cm), u8 to, u8 to port, u8 nodes, then each: u8 host part, 3 x i16 (cm): a routed link (protocol 90)
    RouteEdit, // u16 body, u8 op, u8 link, u8 node, u8 its source, u8 its to, 3 x i16 (cm), u8 host: delete, move a node, repair (protocol 90)
    Spawn, // u8 SbSpawnKind, u8 difficulty, u8 SbLoadKind, u8 its value, u8 zombie mix, u32 count, vec aimed spot: a creative spawn (protocol 92; GameSandboxSpawns.cs)
    ClearSpawns, // every spawned bot and zombie goes (protocol 92)
    Snap, // u8 op (0 undo, 1 quick building off, 2 quick building on, 3 pick: then u16 body, u8 part; 4 take a kind: u16 part id): snappy building (protocol 94; GameSandboxSnap.cs)
    WorldRoute, // u8 type (protocol 98: the high nibble, a pipe style, is gone), u16 from body, u8 from part, u8 from port, u16 to body, u8 to part, u8 to port, u8 nodes, then each: u16 body (0: the world), u8 part, vec (world, or the part's segment rest frame): a world link (protocol 95; board item wl01)
    WorldRouteEdit, // u8 op (delete), u8 link, u16 from body, u8 from part, u16 to body, u8 to part (protocol 95)
    Bench, // u8 kind (0 put, 3 take apart, 6 open, 7 spawn a standard build), u8 slot, u16 length, the build's text: the Gun Workbench (protocol 96; GameSandboxBench.cs)
    FreeformWeld, // u16 body, u8 part, u16 second body, u8 its part: the welder welds the two where they are (protocol 97; board item fw01)
    FreeformCut, // u16 body, u8 part, u8 weld (its joint): the welder cuts a freeform weld's seam free (protocol 97)
    SocketWeld, // u16 piece, u8 part, u8 socket, u16 construct, u8 part, u8 socket (255: a face point), vec face point, u8 its facing, u16 angle (0.01 deg): the welder's three-step socket weld (protocol 99; board item sw01)
    Paint, // u16 body, u8 mode (PaintMode) | 4 for a link, u8 part or link, 3 x u8 colour (r, g, b): the paint tool paints, or resets with mode Reset (protocol 100; board item pp01)
    Notice = 200, // server -> one client: why its action was refused (text)
}

/// <summary>
/// The Sandbox mode in the game (SandboxModule's work; docs/sandbox/CONSTRUCTS.md): the constructs (simulated on the
/// server, mirrored on clients), every player's hands (tool, held part, seat, edit in progress), the actions clients ask
/// for (Msg.CAct) and the server's checks, drivers pinned to their seats, players kept out of constructs, bullets and
/// blasts on construct parts, and the construct messages (Msg.CBody structure and health, Msg.CPose poses at 20 Hz).
/// Other modes never touch any of it: every hook returns at once when the mode isn't Sandbox.
/// </summary>
public sealed partial class Game
{
    /// <summary>The welder's socket-weld step on this PC, for the HUD (Program.SandboxSocketWeld.cs sets it; empty: none).</summary>
    public string SandboxWeldHint = "";

    /// <summary>Every construct of the match (server: simulated; client: as last heard).</summary>
    public readonly ConstructWorld Constructs = new();

    /// <summary>Every player's hands (server-owned; clients read them from the mode-state tail).</summary>
    public readonly ConstructEdits SandboxHands = new();

    /// <summary>The clock edits are timed on (the server's match clock; a client's own).</summary>
    public double SandboxNow;

    private int _sbSleepCursor;
    private readonly bool[] _sbAlive = new bool[256]; // (last tick: a death drops the backpack)
    private uint _sbPoseTick;
    private string _sbNotice = "";
    private double _sbNoticeUntil;
    private PacketWriter? _sbW;
    private Func<byte, Vector3?>? _sbEye;

    public bool SandboxOn => Mode == GameMode.Sandbox;

    /// <summary>How far the frame is between the last two ticks (drawing blends).</summary>
    public float TickAlpha => _alpha;

    private PacketWriter SbW => _sbW ??= new PacketWriter(16384);

    /// <summary>A refusal or a hint for the local player (shown on the HUD for a few seconds).</summary>
    public string SandboxNotice => SandboxNow < _sbNoticeUntil ? _sbNotice : "";

    public void SandboxSay(string text)
    {
        _sbNotice = text;
        _sbNoticeUntil = SandboxNow + 3;
    }

    // ---- Lifecycle -----------------------------------------------------------------------------------------------

    /// <summary>Server, every new match (every mode's reset runs): constructs cleared, and on a sandbox map the map's set built.</summary>
    internal void ResetSandbox()
    {
        var had = Constructs.Bodies.Count > 0;
        Constructs.Clear();
        Constructs.Removed.Clear();
        Constructs.Events.Clear();
        SandboxHands.Reset();
        SandboxHands.ResetPacks(); // (backpacks empty with the match)
        SandboxInv.Reset(); // (salvage: inventories reset with the match, like the constructs)
        SandboxClearBuilds(); // (built weapons go with the match: GameSandboxBench.cs)
        ResetSandboxSpawns(); // (no spawns queued, no spawned zombies: GameSandboxSpawns.cs)
        if (!IsServer)
            return;
        if (had || SandboxOn)
        {
            ConstructNet.WriteClear(SbW);
            Net.Broadcast(SbW.Span, PacketReliability.ReliableOrdered);
        }
        if (SandboxOn && World.Map.Sandbox is { } sandbox)
            Constructs.Populate(sandbox, World.Query); // (their structure goes out on the first playing tick)
        // Decision 21: a physics-only part held, in a backpack or in storage (a part file that became physics-only, a
        // world load) drops into the world as a body.
        SandboxHands.DropPhysicsOnly(Constructs, id => Players.TryGetValue(id, out var pl) && pl.Alive ? pl.Eye : null);
    }

    /// <summary>Client: a match starts: nothing until the server sends it.</summary>
    internal void ResetSandboxClient()
    {
        if (IsServer)
            return;
        SandboxClearBuilds();
        Constructs.Clear();
        Constructs.Removed.Clear();
        SandboxHands.Reset();
        SandboxHands.ResetPacks();
        SandboxInv.Reset();
        ResetSandboxSpawns();
        SpawnPlacing = false;
        _sbPoseTick = 0;
    }

    internal void SandboxOnSpawn(Player p)
    {
        if (IsServer)
        {
            SandboxRules();
            SandboxHands.ReturnHeld(p.Id); // (counted parts aren't lost on death)
        }
        ref var h = ref SandboxHands.Hand(p.Id);
        var tool = h.Tool;
        h = SandboxHand.Empty;
        h.Tool = p.Bot == null ? tool : ConstructTool.None;
    }

    /// <summary>Server: the player gets out of their seat (to beside it, when <paramref name="teleport"/>).</summary>
    internal void SandboxStand(Player p, bool teleport)
    {
        if (SandboxHands.Stand(Constructs, p.Id) is not { } feet || !teleport)
            return;
        // Beside the seat, or the other side, or above it: the first spot that's clear.
        var c = Constructs.Get(SandboxHands.Hand(p.Id).SeatBody);
        Span<Vector3> tries = [feet, feet + new Vector3(0, 1.5f, 0)];
        foreach (var t in tries)
        {
            var at = World.ClampToArena(t);
            if (!World.Blocked(at))
            {
                p.Teleport(at);
                return;
            }
        }
        p.Teleport(World.ClampToArena(feet + new Vector3(0, 2.5f, 0)));
        _ = c;
    }

    // ---- Server tick ------------------------------------------------------------------------------------------------

    internal void ServerTickSandbox(double now)
    {
        SandboxNow = now;
        if (!SandboxOn)
            return;
        SandboxRules();
        Constructs.PlayerSpotCount = 0;
        foreach (var p in Players.Values)
        {
            ref var h = ref SandboxHands.Hand(p.Id);
            if (_sbAlive[p.Id] && !p.Alive)
                SandboxHands.DropPack(Constructs, p.Id, p.Position, p.Yaw); // (a death drops the backpack's contents as pickups)
            _sbAlive[p.Id] = p.Alive;
            if (!p.Alive)
            {
                h.Edit = EditKind.None;
                SandboxHands.Release(Constructs, p.Id); // (the physics gun lets go on death)
            }
            else if (h.GrabBody != 0)
                SandboxHands.GrabTick(Constructs, p.Id, p.Eye, World.Forward(p.Yaw, p.Pitch), p.Yaw); // (the held thing follows the look)
            else if (Constructs.PlayerSpotCount < Constructs.PlayerSpots.Length)
                Constructs.PlayerSpots[Constructs.PlayerSpotCount++] = p.Position; // (proximity sensors)
            if (!h.Seated)
                continue;
            var c = Constructs.Get(h.SeatBody);
            var seat = c?.SeatOf(p.Id) ?? -1;
            if (!p.Alive || c == null || seat < 0 || c.Parts[seat].Broken || c.Parts[seat].D.Role != PartRole.Seat)
            {
                SandboxStand(p, teleport: p.Alive);
                continue;
            }
            h.SeatPart = (byte)seat; // (parts are renumbered when others come off)
            var b = p.Id == LocalId ? HeldButtons : p.LastInput.Buttons;
            c.Parts[seat].Keys = (ushort)((ushort)b | (h.SeatKeys & 0x7F00) | CPart.Occupied); // (input readers; ConstructLogic.cs)
            c.Parts[seat].AimYaw = p.Yaw; // (a gunner points the guns with their look; ConstructWeapons.cs)
            c.Parts[seat].AimPitch = p.Pitch;
            if (seat == c.DriveSeat)
            {
                c.Driver = p.Id;
                c.DriverSeat = seat;
                ConstructEdits.DriveInput(c, b, World.TickDt);
            }
        }
        _sbEye ??= id => Players.TryGetValue(id, out var pl) && pl.Alive ? pl.Eye : null;
        SandboxHands.Tick(Constructs, now, _sbEye);
        Constructs.Targets = this; // (the guns hit players, bots and zombies: GameConstructWeapons.cs)
        Constructs.Hands ??= SandboxHandOf; // (a held hose follows its holder's hands)
        using (Perf.Measure(PerfArea.Players))
            Constructs.Step(World.Query, World.TickDt);
        SandboxSpawnTick(now); // the Spawn category: queued spawns, the spawner parts, spawned zombies (GameSandboxSpawns.cs)
        SandboxFireFlush(now); // (before the structure: the shots name the gun parts as they were when they fired)
        SandboxDebris(now);
        SandboxFlush();
    }

    /// <summary>Server: what changed this tick goes out (structure and health reliably, poses at 20 Hz).</summary>
    private void SandboxFlush()
    {
        var w = SbW;
        foreach (var id in Constructs.Removed)
        {
            ConstructNet.WriteGone(w, id);
            Net.Broadcast(w.Span, PacketReliability.ReliableOrdered);
        }
        Constructs.Removed.Clear();
        foreach (var id in Constructs.StructureDirty)
            if (Constructs.Get(id) is { } c)
            {
                if (c.Dirty)
                    c.Rebuild();
                ConstructNet.WriteFull(w, c);
                Net.Broadcast(w.Span, PacketReliability.ReliableOrdered);
            }
        Constructs.StructureDirty.Clear();
        for (var from = 0; from < Constructs.PaintDirty.Count;)
        {
            from = ConstructPaint.WriteChanges(w, Constructs, Constructs.PaintDirty, from);
            Net.Broadcast(w.Span, PacketReliability.ReliableOrdered);
        }
        Constructs.PaintDirty.Clear();
        foreach (var handle in Constructs.StoreDirty)
        {
            // (after the structures: a client finds the store by the handle its part came with)
            ConstructNet.WriteStore(w, Constructs.Stores[handle]);
            Net.Broadcast(w.Span, PacketReliability.ReliableOrdered);
        }
        Constructs.StoreDirty.Clear();
        foreach (var who in SandboxHands.PackDirty)
        {
            ConstructNet.WritePack(w, who, SandboxHands.Pack(who));
            Net.Broadcast(w.Span, PacketReliability.ReliableOrdered);
        }
        SandboxHands.PackDirty.Clear();
        if (Constructs.LinksDirty)
        {
            // (after the structures: a client resolves the links against the parts as they are now)
            Constructs.LinksDirty = false;
            ConstructNet.WriteLinks(w, Constructs);
            Net.Broadcast(w.Span, PacketReliability.ReliableOrdered);
        }
        SandboxWorldLinksFlush(); // (after the structures, like the links between constructs)
        for (var from = 0; from < Constructs.HealthDirty.Count;)
        {
            from = ConstructNet.WriteHealth(w, Constructs, Constructs.HealthDirty, from);
            Net.Broadcast(w.Span, PacketReliability.ReliableOrdered);
        }
        Constructs.HealthDirty.Clear();
        if (ServerTick % ConstructNet.PoseEvery == 0)
            SandboxPoses(w);
        SandboxStreamTick(); // (joiners in a big world: the rest of the constructs, a slice a tick: GameSandboxStream.cs)
        SandboxInventoryFlush();
        foreach (var e in Constructs.Events)
            Console.WriteLine($"[sandbox] {e}");
        Constructs.Events.Clear();
        foreach (var e in SandboxHands.Done)
            Console.WriteLine($"[sandbox] {e}");
        SandboxHands.Done.Clear();
        SandboxNoticesFlush();
    }

    /// <summary>Server: the HUD lines edits left for players (a part dropped because the hands were full...): the host's own at once, a client's as a notice.</summary>
    private void SandboxNoticesFlush()
    {
        foreach (var (who, text) in SandboxHands.Notices)
        {
            if (who == LocalId)
                SandboxSay(text);
            else if (!Net.IsOffline && Players.TryGetValue(who, out var p) && GuidOf(p) is { } g)
            {
                SbW.Begin(Msg.CAct).U8((byte)CActKind.Notice).Str(text);
                Net.SendTo(g, SbW.Span, PacketReliability.ReliableOrdered);
            }
        }
        SandboxHands.Notices.Clear();
    }

    /// <summary>
    /// Server: a joining client gets every construct as it is now. In a big world the constructs are streamed over the
    /// next ticks (<see cref="SandboxStreamTick"/>) and what refers to them follows the last one (<see cref="SendSandboxTail"/>).
    /// </summary>
    internal void SendSandboxState(RakNetGUID to)
    {
        if (!SandboxOn)
            return;
        var w = SbW;
        ConstructNet.WriteClear(w);
        Net.SendTo(to, w.Span, PacketReliability.ReliableOrdered);
        if (Constructs.UseGrid)
        {
            SandboxStreamStart(to);
            return;
        }
        foreach (var c in Constructs.Bodies)
        {
            ConstructNet.WriteFull(w, c);
            Net.SendTo(to, w.Span, PacketReliability.ReliableOrdered);
        }
        SendSandboxTail(to);
    }

    /// <summary>Server: what a joiner gets after every construct: the stores, the backpacks, the links, the builds and its inventory.</summary>
    private void SendSandboxTail(RakNetGUID to)
    {
        var w = SbW;
        for (var k = 1; k <= ConstructWorld.MaxStores; k++)
            if (Constructs.Stores[k].Live)
            {
                ConstructNet.WriteStore(w, Constructs.Stores[k]);
                Net.SendTo(to, w.Span, PacketReliability.ReliableOrdered);
            }
        foreach (var p in Players.Values)
            if (SandboxHands.Pack(p.Id).Items > 0)
            {
                ConstructNet.WritePack(w, p.Id, SandboxHands.Pack(p.Id));
                Net.SendTo(to, w.Span, PacketReliability.ReliableOrdered);
            }
        ConstructNet.WriteLinks(w, Constructs);
        Net.SendTo(to, w.Span, PacketReliability.ReliableOrdered);
        ConstructNet.WriteWorldLinks(w, Constructs); // (world links and hoses: board item wl01)
        Net.SendTo(to, w.Span, PacketReliability.ReliableOrdered);
        SendSandboxBuildsTo(to);
        SendSandboxInventory(to);
    }

    // ---- Packets ----------------------------------------------------------------------------------------------------

    internal void OnSandboxPacket(byte id, ref PacketReader r, RakNetGUID sender, double now)
    {
        if (!IsServer)
            SandboxNow = now;
        switch (id)
        {
            case (byte)Msg.CBody when !IsServer:
                ConstructNet.Read(ref r, Constructs, SandboxNow, SandboxInv.Of(LocalId), SandboxHands);
                break;
            case (byte)Msg.CPose when !IsServer:
                ConstructNet.ReadPoses(ref r, Constructs, SandboxNow, ref _sbPoseTick);
                break;
            case (byte)Msg.CFire when !IsServer:
                OnConstructFire(ref r, now);
                break;
            case (byte)Msg.CAct when IsServer:
                if (_clients.TryGetValue(sender.G, out var cl) && Players.TryGetValue(cl.Id, out var p))
                {
                    var why = SandboxAct(p, ref r, now);
                    if (why != null)
                    {
                        SbW.Begin(Msg.CAct).U8((byte)CActKind.Notice).Str(why);
                        Net.SendTo(sender, SbW.Span, PacketReliability.ReliableOrdered);
                    }
                }
                break;
            case (byte)Msg.CAct when !IsServer:
                if (r.U8() == (byte)CActKind.Notice)
                    SandboxSay(r.Text(120));
                break;
        }
    }

    /// <summary>Server: one action from a player (the local host's own go straight here). Returns why it was refused, or null.</summary>
    private string? SandboxAct(Player p, ref PacketReader r, double now)
    {
        if (!SandboxOn || !p.Alive)
            return null;
        var kind = (CActKind)r.U8();
        var eye = p.Eye;
        var hands = SandboxHands;
        SandboxRules();
        switch (kind)
        {
            case CActKind.Spawn:
            case CActKind.ClearSpawns:
                return SandboxSpawnAct(p, kind, ref r); // the Spawn tab (GameSandboxSpawns.cs)
            case CActKind.Snap:
                return SandboxSnapAct(p, ref r, eye);
            case CActKind.WorldRoute:
            case CActKind.WorldRouteEdit:
                return SandboxWorldLinkAct(p, kind, ref r); // world links (GameSandboxWorldLinks.cs)
            case CActKind.Bench:
                return SandboxBenchAct(p, ref r);
            case CActKind.Tool:
                return hands.SetTool(p.Id, ConstructToolWire.From(r.U8()));
            case CActKind.Grab:
            case CActKind.GrabSteer:
            case CActKind.GrabRelease:
            case CActKind.GrabFreeze:
            case CActKind.UnfreezeAll:
                return SandboxPhysGunAct(p, kind, ref r);
            case CActKind.Attach:
            {
                var body = r.U16();
                var part = r.U8();
                var socket = r.U8();
                var variant = r.U8();
                return hands.BeginAttach(Constructs, p.Id, eye, body, part, socket, variant, now);
            }
            case CActKind.PlaceNew:
            {
                var at = r.Vec();
                var normal = r.Vec();
                var yaw = r.F32();
                var turn = r.U8();
                var flags = r.U8();
                if (!float.IsFinite(at.X + at.Y + at.Z + normal.X + normal.Y + normal.Z + yaw))
                    return "bad place";
                // (ConstructPlace.Pose finds the surface again under the point the client chose)
                return hands.BeginPlaceNew(Constructs, p.Id, eye, at, normal, yaw, turn, flags, now);
            }
            case CActKind.Join:
            {
                var body = r.U16();
                return hands.Join(Constructs, p.Id, eye, body, r.U8());
            }
            case CActKind.Remove:
            {
                var body = r.U16();
                var part = r.U8();
                return hands.BeginRemove(Constructs, p.Id, eye, body, part, now);
            }
            case CActKind.FreeformWeld:
            {
                var body = r.U16();
                var part = r.U8();
                var body2 = r.U16();
                return hands.BeginFreeform(Constructs, p.Id, eye, body, part, body2, r.U8(), now);
            }
            case CActKind.FreeformCut:
            {
                var body = r.U16();
                var part = r.U8();
                return hands.FreeformCut(Constructs, p.Id, eye, body, part, r.U8());
            }
            case CActKind.SocketWeld:
            {
                var body = r.U16();
                var part = r.U8();
                var socket = r.U8();
                var w = new WeldTarget { Body = r.U16(), Part = r.U8(), Socket = r.U8(), At = r.Vec(), Dir = r.U8() };
                var angle = SocketWeld.FromWire(r.U16());
                if (!float.IsFinite(w.At.X + w.At.Y + w.At.Z) || w.Dir > 5)
                    return "bad weld";
                return hands.BeginSocketWeld(Constructs, p.Id, eye, body, part, socket, w, angle, now);
            }
            case CActKind.Paint:
            {
                var body = r.U16();
                var mode = r.U8();
                var index = r.U8();
                var rgb = (uint)(r.U8() << 16 | r.U8() << 8 | r.U8());
                return hands.Paint(Constructs, p.Id, eye, body, (PaintMode)(mode & 3), (mode & 4) != 0, index, ConstructPaint.SetBit | rgb);
            }
            case CActKind.Cancel:
                hands.Cancel(p.Id);
                return null;
            case CActKind.Use:
            {
                var body = r.U16();
                var part = r.U8();
                if (SandboxHoseUse(p, body, part, out var hoseWhy))
                    return hoseWhy; // (a hose part: take, plug, unplug or hang back its hose; GameSandboxWorldLinks.cs)
                return hands.Use(Constructs, p.Id, eye, body, part);
            }
            case CActKind.Stand:
                SandboxStand(p, teleport: true);
                return null;
            case CActKind.Rack:
            {
                var pick = r.U16(); // (protocol 90: part ids are two bytes)
                return pick == 0 ? hands.TakeFromRack(World.Map.Sandbox, p.Id, p.Position, Constructs, eye, p.Yaw) : hands.TakePart(World.Map.Sandbox, p.Id, (ushort)(pick - 1), Constructs, eye, p.Yaw);
            }
            case CActKind.Blueprint:
                return SandboxSpawnBlueprint(p, r.U8());
            case CActKind.Drop:
                if (Constructs.HoseLetGo(p.Id))
                    return null; // (the hose's receiver first: it reels back)
                return hands.Drop(Constructs, p.Id, p.Position, p.Yaw);
            case CActKind.PickUp:
                return hands.PickUp(Constructs, p.Id, eye, r.U16());
            case CActKind.Wire:
            {
                var body = r.U16();
                var from = r.U8();
                var to = r.U8();
                var toBody = r.U16();
                return hands.Wire(Constructs, p.Id, eye, body, from, to, toBody);
            }
            case CActKind.Unwire:
            {
                var body = r.U16();
                return hands.Unwire(Constructs, p.Id, eye, body, r.U8());
            }
            case CActKind.Setting:
            {
                var body = r.U16();
                return hands.CycleSetting(Constructs, p.Id, eye, body, r.U8());
            }
            case CActKind.Configure:
                return SandboxConfigureAct(p, ref r, eye);
            case CActKind.SeatKeys:
                hands.Hand(p.Id).SeatKeys = (ushort)((r.U8() & 7) << 8);
                return null;
            case CActKind.StoreIn:
            case CActKind.TakeStored:
            {
                var body = r.U16();
                var part = r.U8();
                var slot = r.U8();
                return kind == CActKind.StoreIn ? hands.StoreHeld(Constructs, p.Id, eye, body, part, slot) : hands.TakeStored(Constructs, p.Id, eye, body, part, slot);
            }
            case CActKind.Stow:
                return hands.Stow(p.Id);
            case CActKind.Draw:
                return hands.DrawFromPack(p.Id);
            case CActKind.Bin:
            {
                var body = r.U16();
                return hands.UseBin(Constructs, p.Id, eye, body, r.U8());
            }
            case CActKind.Route:
            {
                var body = r.U16();
                var ts = r.U8();
                var from = r.U8();
                var fromPort = r.U8();
                var parent = r.U8();
                var parentSeg = r.U8();
                var junction = ReadCm(ref r);
                var to = r.U8();
                var toPort = r.U8();
                int count = r.U8();
                if (count > Construct.MaxNodesPerRoute)
                    return "too many nodes";
                Span<CRouteNode> nodes = stackalloc CRouteNode[Construct.MaxNodesPerRoute];
                for (var i = 0; i < count; i++)
                {
                    var host = r.U8();
                    nodes[i] = new CRouteNode { Host = host, Pos = ReadCm(ref r) };
                }
                return hands.Route(Constructs, p.Id, eye, body, (RouteType)(ts & 15), from, fromPort, parent, parentSeg, junction, nodes[..count], to, toPort);
            }
            case CActKind.RouteEdit:
            {
                var body = r.U16();
                var op = r.U8();
                var route = r.U8();
                var node = r.U8();
                var source = r.U8();
                var to = r.U8();
                var pos = ReadCm(ref r);
                var host = r.U8();
                return op > (byte)RouteEditOp.Repair ? "no such edit" : hands.RouteEdit(Constructs, p.Id, eye, body, (RouteEditOp)op, route, node, source, to, pos, host);
            }
            case CActKind.SeatLook:
            {
                ref var sh = ref hands.Hand(p.Id);
                var look = (short)r.U16() / 32767f * MathF.PI;
                if (sh.Seated)
                {
                    sh.SeatLook = look;
                    sh.HasSeatLook = true;
                }
                return null;
            }
        }
        return null;
    }

    /// <summary>Client (or the host itself): ask the server for an action. The host's own action is handled at once.</summary>
    public void SandboxSend(CActKind kind, ushort body = 0, byte part = 0, byte socket = 0, byte variant = 0, Vector3 at = default, float yaw = 0, ushort body2 = 0, Vector3? normal = null, byte flags = 0)
    {
        var w = SbW;
        w.Begin(Msg.CAct).U8((byte)kind);
        switch (kind)
        {
            case CActKind.Tool:
                w.U8(part);
                break;
            case CActKind.Attach:
                w.U16(body).U8(part).U8(socket).U8(variant);
                break;
            case CActKind.PlaceNew:
                w.Vec(at).Vec(normal ?? Vector3.UnitY).F32(yaw).U8(variant).U8(flags); // (variant: the part's Rot24 turn)
                break;
            case CActKind.Join:
            case CActKind.Remove:
            case CActKind.Use:
            case CActKind.Unwire:
            case CActKind.Setting:
            case CActKind.Bin:
                w.U16(body).U8(part);
                break;
            case CActKind.StoreIn:
            case CActKind.TakeStored:
                w.U16(body).U8(part).U8(socket); // (the slot)
                break;
            case CActKind.Wire:
                w.U16(body).U8(part).U8(socket).U16(body2); // (from, to, to's construct)
                break;
            case CActKind.FreeformWeld:
                w.U16(body).U8(part).U16(body2).U8(socket); // (socket: the second body's part)
                break;
            case CActKind.FreeformCut:
                w.U16(body).U8(part).U8(socket); // (socket: the weld)
                break;
            case CActKind.SeatKeys:
            case CActKind.Blueprint:
                w.U8(part);
                break;
            case CActKind.Rack:
                w.U16(body); // (the pick: 0 the rack's next kind, n part id n - 1)
                break;
            case CActKind.PickUp:
                w.U16(body);
                break;
        }
        if (kind == CActKind.Tool)
            SandboxHands.Hand(LocalId).Tool = (ConstructTool)part; // (shown at once; the server follows)
        if (IsServer)
        {
            if (Local is { } me)
            {
                var r = new PacketReader(w.Span[1..]);
                if (SandboxAct(me, ref r, SandboxNow) is { } why)
                    SandboxSay(why);
            }
            return;
        }
        Net.SendToServer(w.Span, PacketReliability.ReliableOrdered);
    }

    private static Vector3 ReadCm(ref PacketReader r) => new(unchecked((short)r.U16()) / 100f, unchecked((short)r.U16()) / 100f, unchecked((short)r.U16()) / 100f);

    private static PacketWriter Cm(PacketWriter w, Vector3 v) =>
        w.U16(unchecked((ushort)(short)Math.Clamp(MathF.Round(v.X * 100), short.MinValue, short.MaxValue)))
            .U16(unchecked((ushort)(short)Math.Clamp(MathF.Round(v.Y * 100), short.MinValue, short.MaxValue)))
            .U16(unchecked((ushort)(short)Math.Clamp(MathF.Round(v.Z * 100), short.MinValue, short.MaxValue)));

    /// <summary>Client (or host): ask for a routed link the linking tool laid out (the server checks it all again).</summary>
    public void SandboxSendRoute(ushort body, RouteType type, int from, byte fromPort, int parent, int parentSeg, Vector3 junction, ReadOnlySpan<CRouteNode> nodes, int to, byte toPort)
    {
        var w = SbW;
        w.Begin(Msg.CAct).U8((byte)CActKind.Route).U16(body).U8((byte)type).U8((byte)from).U8(fromPort).U8((byte)parent).U8((byte)parentSeg);
        Cm(w, junction).U8((byte)to).U8(toPort).U8((byte)nodes.Length);
        foreach (var n in nodes)
            Cm(w.U8(n.Host), n.Pos);
        SandboxSendBuilt(w);
    }

    /// <summary>Client (or host): an edit of a routed link (delete, move a node, repair).</summary>
    public void SandboxSendRouteEdit(ushort body, RouteEditOp op, int route, int node, byte source, byte to, Vector3 pos = default, byte host = 0)
    {
        var w = SbW;
        w.Begin(Msg.CAct).U8((byte)CActKind.RouteEdit).U16(body).U8((byte)op).U8((byte)route).U8((byte)node).U8(source).U8(to);
        Cm(w, pos).U8(host);
        SandboxSendBuilt(w);
    }

    /// <summary>Client (or host): the welder's socket weld of piece <paramref name="body"/> (its part's socket) onto <paramref name="target"/> at <paramref name="degrees"/> (SocketWeld.cs).</summary>
    public void SandboxSendSocketWeld(ushort body, int part, int socket, in WeldTarget target, float degrees)
    {
        var w = SbW;
        w.Begin(Msg.CAct).U8((byte)CActKind.SocketWeld).U16(body).U8((byte)part).U8((byte)socket)
            .U16(target.Body).U8(target.Part).U8(target.Socket).Vec(target.At).U8(target.Dir).U16(SocketWeld.WireAngle(degrees));
        SandboxSendBuilt(w);
    }

    private void SandboxSendBuilt(PacketWriter w)
    {
        if (IsServer)
        {
            if (Local is { } me)
            {
                var r = new PacketReader(w.Span[1..]);
                if (SandboxAct(me, ref r, SandboxNow) is { } why)
                    SandboxSay(why);
            }
            return;
        }
        Net.SendToServer(w.Span, PacketReliability.ReliableOrdered);
    }

    // ---- The debug parts menu (UiSandboxParts.cs) ------------------------------------------------------------------

    /// <summary>The debug parts menu is allowed: Sandbox on a map whose sandbox section doesn't turn it off (a future survival map can).</summary>
    public bool SandboxPartsMenu => SandboxOn && World.Map.Sandbox is { PartsMenu: true };

    /// <summary>Client (or host): ask for a part by id from the debug parts menu (the rack's request with a part id).</summary>
    public void SandboxTakePart(ushort id) => SandboxSend(CActKind.Rack, body: (ushort)(id + 1));

    /// <summary>
    /// Server: the debug parts menu's blueprint button: the blueprint is built standing on the ground in front of the
    /// player, facing the way they look, far enough out that it doesn't land on them. Returns why it was refused, or null.
    /// </summary>
    internal string? SandboxSpawnBlueprint(Player p, int index)
    {
        if (!SandboxOn)
            return "blueprints spawn only in Sandbox";
        if (World.Map.Sandbox is not { PartsMenu: true })
            return "the parts menu is off on this map";
        if (!p.Alive)
            return "you are dead";
        if (SandboxHands.Hand(p.Id).Seated)
            return "get out of the seat first";
        var all = ConstructBuild.Blueprints;
        if (index < 0 || index >= all.Count)
            return "no such blueprint";
        var bp = all[index];
        var fwd = World.FlatForward(p.Yaw);
        var probe = p.Position + fwd * 6f;
        var c = ConstructBuild.BuildOnGround(Constructs, bp, ConstructWorld.Ground(World.Query, probe.X, probe.Z), p.Yaw, out var why);
        if (c == null)
            return why.Length > 0 ? why : "the world is full of constructs";
        c.Owner = p.Id; // (spawned from the menu: theirs)
        // Out past its own size (a buggy is longer than a gate): its widest reach from its origin, plus a metre and a half.
        var reach = MathF.Max(MathF.Max(MathF.Abs(c.LocalMin.X), MathF.Abs(c.LocalMax.X)), MathF.Max(MathF.Abs(c.LocalMin.Z), MathF.Abs(c.LocalMax.Z)));
        var dist = MathF.Max(3f, reach + 1.5f);
        if (MathF.Abs(dist - 6f) > 0.05f)
        {
            var at = p.Position + fwd * dist;
            var g0 = ConstructWorld.Ground(World.Query, probe.X, probe.Z);
            var g1 = ConstructWorld.Ground(World.Query, at.X, at.Z);
            c.Pos += new Vector3(at.X - probe.X, g1.Y - g0.Y, at.Z - probe.Z);
            c.PrevPos = c.NetPos = c.NetFromPos = c.Pos;
            ConstructCargo.FollowAll(Constructs); // (what its cargo volumes carry moves with it)
        }
        Constructs.Events.Add($"player {p.Id} spawned the {bp.Name} blueprint as construct {c.Id} (parts menu)");
        return null;
    }

    // ---- Players in and on constructs ------------------------------------------------------------------------------

    /// <summary>After each movement step (server and the local prediction): a driver stays in the seat; anyone else is kept out of constructs (or stands on them).</summary>
    private void SandboxPlayer(Player p, ref MoveState s)
    {
        if (!SandboxOn || Constructs.Bodies.Count == 0)
            return;
        ref var h = ref SandboxHands.Hand(p.Id);
        if (h.Seated && Constructs.Get(h.SeatBody) is { } c)
        {
            s.Position = ConstructEdits.SeatFeet(c, h.SeatPart);
            s.Velocity = c.Vel;
            s.Grounded = true;
            s.MantleTicks = 0;
            return;
        }
        Constructs.BlockPlayer(ref s, World.HeightOf(s.Stance), IsServer, p.Id);
        if (IsServer && Constructs.LastHitSpeed > RunOverSpeed && Constructs.LastHitBody is { } car)
            SandboxRunOver(p, ref s, car);
    }

    /// <summary>
    /// A construct part moving towards a player faster than this (m/s: its own speed at the contact, along the normal,
    /// whatever the player is doing) hurts them (run over, or hit by a swinging arm or a wreck); slower, it only pushes.
    /// Tunable (the developer's proposal: 3 to 4).
    /// </summary>
    public static float RunOverSpeed = 4f;

    public const double RunOverCooldown = 0.6;

    private readonly double[] _sbHitAt = new double[256];

    /// <summary>Run-over damage: by how fast the part moved towards them (its own speed) and how heavy its construct is; the driver (if any) gets the credit.</summary>
    public static int RunOverDamage(float speed, float mass) =>
        speed <= RunOverSpeed ? 0 : (int)MathF.Round((speed - RunOverSpeed) * 9f * RunOverMass(mass));

    /// <summary>How much a construct's mass scales run-over damage and knockback: 1 at 500 kg, 0.4 to 2.5.</summary>
    public static float RunOverMass(float mass) => Math.Clamp(MathF.Sqrt(mass / 500f), 0.4f, 2.5f);

    /// <summary>
    /// The knockback a run-over adds on top of the push (which already carries the player along at the part's speed):
    /// out along the contact normal and up, growing with the speed over <see cref="RunOverSpeed"/> and the mass.
    /// </summary>
    public static Vector3 RunOverKnock(float speed, float mass, Vector3 normal)
    {
        var over = MathF.Max(0, speed - RunOverSpeed) * RunOverMass(mass);
        return normal * MathF.Min(over * 0.45f, 14f) + new Vector3(0, MathF.Min(1.5f + over * 0.18f, 7f), 0);
    }

    private void SandboxRunOver(Player p, ref MoveState s, Construct car)
    {
        var now = SandboxNow;
        if (now < _sbHitAt[p.Id] + RunOverCooldown)
            return;
        _sbHitAt[p.Id] = now;
        var dmg = RunOverDamage(Constructs.LastHitSpeed, Constructs.LastHitMass);
        // Knocked on with the car (out along the contact and up), harder the faster and heavier it is.
        s.Velocity += RunOverKnock(Constructs.LastHitSpeed, Constructs.LastHitMass, Constructs.LastHitNormal);
        s.Grounded = false;
        var by = car.Driver != 255 && car.Driver != p.Id && Players.TryGetValue(car.Driver, out var d) ? d : p;
        Constructs.Events.Add($"player {p.Id} hit by construct {car.Id} at {Constructs.LastHitSpeed:0.0} m/s: {dmg} damage");
        if (dmg > 0)
            DamagePlayer(by, p, dmg, false, MeleeKillId, now, DamageKind.Melee, car.WorldCom);
    }

    /// <summary>Is this player in a seat? (Drivers and passengers don't shoot: construct weapons are milestone 3.)</summary>
    public bool SandboxSeated(Player p) => SandboxOn && SandboxHands.Hand(p.Id).Seated;

    /// <summary>The glass panes the last <see cref="SandboxRay"/> went through (where it met each, and how far along).</summary>
    private readonly (Vector3 At, float Dist)[] _sbGlass = new (Vector3, float)[4];

    private int _sbGlassCount;

    /// <summary>
    /// A bullet's ray against the constructs: shortens <paramref name="dist"/> to the part it hits. Glass panes don't stop
    /// it: it goes on through them (up to four; noted for <see cref="SandboxGlassShot"/>, which breaks them), so a player
    /// behind a windscreen can be hit.
    /// </summary>
    private ConstructHit? SandboxRay(Vector3 o, Vector3 d, ref float dist)
    {
        _sbGlassCount = 0;
        if (!SandboxOn || Constructs.Bodies.Count == 0)
            return null;
        var from = 0f;
        while (from < dist && Constructs.Raycast(o + d * from, d, dist - from, out var hit))
        {
            hit.Distance += from;
            if (!hit.Glass || _sbGlassCount == _sbGlass.Length)
            {
                dist = hit.Distance;
                return hit;
            }
            _sbGlass[_sbGlassCount++] = (hit.Point, hit.Distance);
            from = hit.Distance + ConstructWeapons.ExitDistance(hit.Body, hit.Part, hit.Point, d) + 0.01f;
        }
        return null;
    }

    /// <summary>
    /// The glass the last <see cref="SandboxRay"/> went through before <paramref name="dist"/> (where the round ended)
    /// takes the round's damage (found again by where it was met: a pane that broke renumbers the parts after it).
    /// </summary>
    private void SandboxGlassShot(Vector3 d, float dist, float damage)
    {
        for (var k = 0; k < _sbGlassCount; k++)
        {
            var (at, along) = _sbGlass[k];
            if (along > dist + 1e-3f)
                break;
            if (Constructs.Raycast(at - d * 0.05f, d, 0.1f, out var gh) && gh.Glass)
                SandboxShotDamage(gh, damage);
        }
        _sbGlassCount = 0;
    }

    private void SandboxShotDamage(ConstructHit hit, float damage)
    {
        if (Constructs.Get(hit.Body.Id) is not { } c || hit.Part >= c.PartCount)
            return;
        if (hit.RoutePlus1 > 0)
            ConstructRoutes.Damage(Constructs, c, hit.RoutePlus1 - 1, hit.RouteSeg, damage); // (a routed link's segment: cut, it stops what it carries)
        else if (hit.SlotPlus1 > 0)
            Constructs.DamageStored(c, hit.Part, hit.SlotPlus1 - 1, damage); // (a part stored in a container: physical storage)
        else
            Constructs.DamagePart(c, hit.Part, damage, out _);
    }

    /// <summary>A blast near constructs (grenades, kill streaks, explosive props): every part in range takes its share.</summary>
    private void SandboxBlast(Vector3 center, float radius, float damage)
    {
        if (SandboxOn && Constructs.Bodies.Count > 0)
            Constructs.Blast(center, radius, damage);
    }

    // ---- Seated players: part of the construct ---------------------------------------------------------------------

    /// <summary>How far a seated player's look turns from the seat's facing (radians either way): the driver a little less than a passenger; a gunner all the way round.</summary>
    public static float SeatLookLimit(SeatKind kind) => kind switch
    {
        SeatKind.Driver => 1.75f,
        SeatKind.Passenger => 2.1f,
        _ => MathF.PI,
    };

    /// <summary>The yaw a seat faces in the world (its -Z, flattened).</summary>
    public static float SeatYaw(in Quaternion seat)
    {
        var f = Vector3.Transform(-Vector3.UnitZ, seat);
        return MathF.Abs(f.X) + MathF.Abs(f.Z) < 1e-4f ? 0f : MathF.Atan2(f.X, -f.Z);
    }

    /// <summary>A player's look relative to a seat's facing, inside the seat type's limits.</summary>
    public static float SeatLocalYaw(float worldYaw, float seatYaw, SeatKind kind)
    {
        var lim = SeatLookLimit(kind);
        var local = World.WrapAngle(worldYaw - seatYaw);
        return lim >= MathF.PI ? local : Math.Clamp(local, -lim, lim);
    }

    private uint _sbViewSeat; // the seat the local view last turned with (body << 8 | part + 1; 0: none)
    private float _sbViewYaw;

    private float _sbLookSent = float.NaN;
    private double _sbLookSentAt;

    /// <summary>The local player's seat-relative look to the server (the host's straight into its hands): on a change, at most ~30 times a second, and every 0.25 s anyway (it goes unreliably).</summary>
    private void SandboxSeatLookOut(float look, double now)
    {
        if (IsServer)
        {
            ref var h = ref SandboxHands.Hand(LocalId);
            h.SeatLook = look;
            h.HasSeatLook = h.Seated;
            return;
        }
        var since = now - _sbLookSentAt;
        if (since < 1.0 / 30 || (MathF.Abs(look - _sbLookSent) < 0.003f && since < 0.25))
            return;
        _sbLookSent = look;
        _sbLookSentAt = now;
        var w = SbW;
        w.Begin(Msg.CAct).U8((byte)CActKind.SeatLook).U16(unchecked((ushort)(short)Math.Clamp((int)MathF.Round(look / MathF.PI * 32767f), -32767, 32767)));
        Net.SendToServer(w.Span, PacketReliability.UnreliableSequenced);
    }

    /// <summary>Server, each snapshot: a seated player's entry carries the seat and the look relative to it (protocol 80), not a world look.</summary>
    private void SandboxSnapSeat(Player p, ref SnapPlayer f)
    {
        if (!SandboxOn || !p.Alive)
            return;
        ref var h = ref SandboxHands.Hand(p.Id);
        if (!h.Seated || Constructs.Get(h.SeatBody) is not { } c || h.SeatPart >= c.PartCount)
            return;
        ConstructRender.SeatPose(c, h.SeatPart, 1f, true, out var q, out _);
        f.SeatBody = h.SeatBody;
        f.SeatPart = h.SeatPart;
        f.Yaw = h.HasSeatLook ? h.SeatLook : World.WrapAngle(p.Yaw - SeatYaw(q));
        f.MantleTicks = 0;
    }

    /// <summary>Client: a snapshot's yaw in the world (a seated player's came relative to their seat; the seat as drawn now).</summary>
    private float SandboxWorldYaw(float yaw, ushort body, byte part)
    {
        if (body == 0 || Constructs.Get(body) is not { } c || part >= c.PartCount)
            return yaw;
        ConstructRender.SeatPose(c, part, 1f, false, out var q, out _);
        return World.WrapAngle(SeatYaw(q) + yaw);
    }

    /// <summary>
    /// Every frame, after the render states (host and clients): the constructs are posed for this frame (a client blends
    /// its received poses here, before the camera is set), then every seated player is put in their seat as it is drawn:
    /// feet at the seat's "at" point, the body in the seat's frame (tilting and turning with it, on a moving joint too),
    /// the seated pose for the seat type, the look relative to the seat (the local view turns with the seat, inside the
    /// seat type's limits). A remote player's seat and seat-relative look come from their snapshots (protocol 80), so every
    /// machine composes the same seat-local pose with its own drawing of the construct: no lag, jitter or slide.
    /// </summary>
    internal void UpdateSandboxSeats(double now, float dt)
    {
        if (Constructs.Bodies.Count == 0)
        {
            _sbViewSeat = 0;
            foreach (var p in Players.Values)
            {
                p.RenderSeat = SeatKind.None;
                if (p.Alive)
                    p.RenderSeatWas = SeatKind.None;
            }
            return;
        }
        if (!IsServer)
        {
            ConstructNet.Blend(Constructs, now, dt);
            ConstructRender.FitWheels(Constructs, World.Query);
        }
        var localSeat = 0u;
        foreach (var p in Players.Values)
        {
            p.RenderSeat = SeatKind.None;
            if (!p.Alive)
                continue; // (RenderSeatWas stays: a ragdoll of a seated death starts seated)
            p.RenderSeatWas = SeatKind.None;
            if (!SandboxOn)
                continue;
            ushort body;
            byte part;
            var remote = !IsServer && p.Id != LocalId;
            if (remote)
            {
                body = p.SnapSeatBody;
                part = p.SnapSeatPart;
            }
            else
            {
                ref var h = ref SandboxHands.Hand(p.Id);
                body = h.SeatBody;
                part = h.SeatPart;
            }
            if (body == 0 || Constructs.Get(body) is not { } c || part >= c.PartCount || c.Parts[part].D.Role != PartRole.Seat)
                continue;
            ConstructRender.SeatPose(c, part, _alpha, IsServer, out var q, out var feet);
            var kind = c.SeatKindOf(part);
            var seatYaw = SeatYaw(q);
            p.RenderSeat = p.RenderSeatWas = kind;
            p.RenderSeatRot = q;
            p.RenderPosition = feet;
            p.RenderStance = Stance.Stand;
            p.RenderGrounded = true;
            p.RenderMantling = false;
            p.RenderVelocity = c.PointVel(feet) + c.SegPointVel(c.Parts[part].Seg, feet); // (not walked: the legs ignore it seated; a ragdoll keeps it)
            if (kind == SeatKind.Driver)
                ConstructRender.WheelGrips(c, q, feet, out p.RenderWheelR, out p.RenderWheelL, out p.RenderWheelRot);
            if (p.Id == LocalId)
            {
                // The view turns with the seat, then stays inside the seat type's limits.
                localSeat = ((uint)body << 8) | (uint)(part + 1);
                if (_sbViewSeat == localSeat && !(kind == SeatKind.Gunner && c.SeatAims(part))) // (a gunner who points the turret looks where they look: the turret follows)
                    LookYaw = World.WrapAngle(LookYaw + World.WrapAngle(seatYaw - _sbViewYaw));
                _sbViewYaw = seatYaw;
                p.RenderLookYaw = SeatLocalYaw(LookYaw, seatYaw, kind);
                LookYaw = World.WrapAngle(seatYaw + p.RenderLookYaw);
                p.RenderYaw = LookYaw;
                SandboxSeatLookOut(p.RenderLookYaw, now);
            }
            else
            {
                // A client's remote player: the snapshot yaw is seat-relative. The host's: the look its client sent
                // (seat-relative: exact however the seat turns), else the world look it simulates.
                ref var h = ref SandboxHands.Hand(p.Id);
                p.RenderLookYaw = remote ? SeatLocalYaw(p.RenderYaw, 0, kind) : h.HasSeatLook ? SeatLocalYaw(h.SeatLook, 0, kind) : SeatLocalYaw(p.RenderYaw, seatYaw, kind);
                p.RenderYaw = World.WrapAngle(seatYaw + p.RenderLookYaw);
            }
        }
        _sbViewSeat = localSeat;
    }

    // ---- HUD ----------------------------------------------------------------------------------------------------------

    private string _sbBar = "";
    private int _sbBarKey = -1;

    public string SandboxBarText()
    {
        var key = Constructs.Bodies.Count * 10000 + Constructs.TotalParts;
        if (key != _sbBarKey)
        {
            _sbBarKey = key;
            _sbBar = $"{Constructs.Bodies.Count} constructs, {Constructs.TotalParts} parts - no limit";
        }
        return _sbBar;
    }

    /// <summary>A line of text rebuilt only when what it shows changes (the HUD runs every frame: no allocation per frame).</summary>
    private struct SbText
    {
        public long Key;
        public string Text;

        public bool Stale(long key)
        {
            if (Text != null && key == Key)
                return false;
            Key = key;
            return true;
        }
    }

    private SbText _sbDrive, _sbProblem, _sbHand, _sbEdit, _sbHint, _sbKeys, _sbInvLine;

    /// <summary>The objective panel: what is in your hands, what you look at, the drive.</summary>
    internal void SandboxHud(Player me, List<HudLine> lines, Vector4 white, Vector4 dim, Vector4 orange, Vector4 green, Vector4 red)
    {
        ref var h = ref SandboxHands.Hand(me.Id);
        if (h.Seated && Constructs.Get(h.SeatBody) is { } gunship && gunship.SeatKindOf(h.SeatPart) == SeatKind.Gunner && GunOfSeat(gunship, h.SeatPart) is var gk and >= 0)
        {
            lines.Add(new HudLine(GunLine(gunship, gk), gunship.Guns[gk].Problem.Length > 0 ? red : white));
            lines.Add(new HudLine("Fire: shoot, look: aim the turret. F get out", dim));
            return;
        }
        if (h.Seated && Constructs.Get(h.SeatBody) is { } ride && h.SeatPart != ride.DriveSeat && ride.SeatOf(me.Id) != ride.DriveSeat)
        {
            lines.Add(new HudLine("Riding as a passenger", white));
            lines.Add(new HudLine("Fire, aim, R: the construct's input readers. F get out", dim));
            return;
        }
        if (h.Seated && Constructs.Get(h.SeatBody) is { } car)
        {
            var kmh = (int)MathF.Round(MathF.Abs(Vector3.Dot(car.Vel, Vector3.Transform(car.SeatForward(), car.Rot))) * 3.6f);
            var fuel = (int)(car.Fuel * 10);
            var charge = car.Electrical ? (int)MathF.Round(ConstructPower.Share(car) * 100) : -1;
            var gears = car.Transmission >= 0 && car.Transmission < car.PartCount && car.DefOf(car.Transmission).HasGears ? car.DefOf(car.Transmission) : null;
            var rpm = (int)MathF.Round(car.Rpm / 100) * 100; // (the configure tool is still to come: the engine's rpm and torque show here and on the look line)
            var engine = car.Engine >= 0 && car.Engine < car.PartCount ? car.DefOf(car.Engine) : null;
            var torque = engine == null || !car.EngineRunning ? 0 : (int)MathF.Round(engine.EngineTorque * ConstructDrive.Curve(engine, car.Rpm) / 10) * 10;
            if (_sbDrive.Stale(kmh * 100000L + fuel + ((long)(charge + 1) << 40) + ((long)car.Gear << 50) + (car.PowerOn ? 1L << 56 : 0) + (car.EngineRunning ? 1L << 57 : 0) + (gears != null ? 1L << 58 : 0) ^ ((long)rpm << 20) ^ ((long)torque << 33)))
                _sbDrive.Text = $"Driving: {kmh} km/h, {rpm} rpm, {torque} N m, {(engine?.FuelKind == EngineFuel.Fuel ? $"fuel {fuel / 10f:0.0} l" : engine?.FuelKind == EngineFuel.Electric ? "electric" : "no fuel needed")}"
                                + (gears != null ? $", gear {gears.SettingLabels[Math.Min(car.Gear, gears.SettingLabels.Length - 1)]}" : "")
                                + (charge >= 0 ? $", charge {charge}%{(car.PowerOn ? "" : " (flat)")}" : "")
                                + (car.Electrical && car.CanDrive && !car.EngineRunning ? ", engine off" : "");
            lines.Add(new HudLine(_sbDrive.Text, white));
            if (car.GunCount > 0)
                lines.Add(new HudLine(GunLine(car, 0), car.Guns[0].Problem.Length > 0 ? red : white));
            if (car.CanDrive && car.StarterFailed)
                lines.Add(new HudLine(car.Electrical ? "It won't start: the battery is flat (or no battery reaches the engine)" : ConstructDrive.NoteText(DriveNote.NeedsStarter), red));
            else if (car.CanDrive)
            {
                if (car.Note != DriveNote.None)
                    lines.Add(new HudLine(ConstructDrive.NoteText(car.Note), car.Note is DriveNote.Shifting or DriveNote.Spooling ? dim : orange));
                var shifts = gears is { Trans: TransKind.Manual or TransKind.Sequential } && gears.Settings.Length > 1 && car.Parts[car.Transmission].InCount == 0;
                lines.Add(new HudLine(car.Electrical && !car.EngineRunning ? "W to start the engine, S reverse, A/D steer, Space brake, F get out"
                    : shifts ? "W/S drive, A/D steer, Space brake, R gear up, Shift gear down, F get out" : "W/S drive, A/D steer, Space brake, F get out", dim));
            }
            else
            {
                if (_sbProblem.Stale(car.DriveProblem.GetHashCode()))
                    _sbProblem.Text = $"It won't go: {car.DriveProblem}";
                lines.Add(new HudLine(_sbProblem.Text, red));
            }
            return;
        }
        var held = ConstructParts.Get(h.Held);
        if (_sbHand.Stale((long)h.Tool * 1000 + h.Held))
        {
            var tool = h.Tool switch { ConstructTool.Wrench => "Wrench", ConstructTool.Welder => "Welder", ConstructTool.Linker => "Linking tool", ConstructTool.PhysicsGun => "Physics gun", ConstructTool.Configure => "Configure tool", ConstructTool.Paint => "Paint tool", _ => "Gun" };
            _sbHand.Text = $"In hand: {tool}{(held != null ? $" + {held.Name}" : "")}";
        }
        lines.Add(new HudLine(_sbHand.Text, h.Tool == ConstructTool.None ? dim : orange));
        if (h.Edit != EditKind.None)
        {
            var pct = (int)(h.Progress(SandboxNow) * 100);
            if (_sbEdit.Stale((long)h.Edit * 1000 + pct))
                _sbEdit.Text = $"{(h.Edit == EditKind.Remove ? "Taking it off" : h.Edit is EditKind.Weld or EditKind.SocketWeld ? "Welding" : "Fitting")}... {pct}%";
            lines.Add(new HudLine(_sbEdit.Text, green));
        }
        else if (SandboxLookText.Length > 0)
            lines.Add(new HudLine(SandboxLookText, white));
        if (SandboxNotice is { Length: > 0 } notice)
            lines.Add(new HudLine(notice, red));
        else if (h.Tool == ConstructTool.PhysicsGun)
            lines.Add(new HudLine(SandboxPhysGunHint(h), dim));
        else if (h.Tool == ConstructTool.Linker)
            lines.Add(new HudLine(SandboxWireFrom.Length > 0 ? SandboxWireFrom : "LMB: wire from an output, then LMB on an input. RMB: cut its wires. R: its setting", dim));
        else if (h.Tool == ConstructTool.Configure)
            lines.Add(new HudLine(SandboxConfigHint, dim));
        else if (h.Tool == ConstructTool.Welder && held == null && SandboxWeldHint.Length > 0)
            lines.Add(new HudLine(SandboxWeldHint, dim));
        else if (held == null)
        {
            if (_sbKeys.Stale(SandboxWheelKey.GetHashCode() ^ (long)SandboxMenuKey.GetHashCode() << 32))
                _sbKeys.Text = $"F at the rack or {SandboxMenuKey} (inventory): take a part. Hold {SandboxWheelKey}: tool wheel. 3 wrench, 4 welder, 5 linking tool, 1/2 guns";
            lines.Add(new HudLine(_sbKeys.Text, dim));
        }
        else
        {
            // The counts while holding a part (milestone 3): how many more of it the inventory holds.
            var inv = SandboxInv.Of(me.Id);
            var more = inv.Count(held.Id);
            if (_sbHint.Stale(held.Id * 100000L + more + (inv.Infinite ? 1L << 40 : 0) + ((long)(SandboxTurnKeys.GetHashCode() & 0xffff) << 41)))
                _sbHint.Text = $"{held.Name}: needs the {held.Tool.ToString().ToLowerInvariant()}. LMB hold: fit, {SandboxTurnKeys}, T: drop. "
                               + (inv.Infinite ? $"Inventory: {more} (parts are endless here)" : more > 0 ? $"{more} more in your inventory" : "the last one in your inventory");
            lines.Add(new HudLine(_sbHint.Text, dim));
        }
        SandboxInventoryLine(me, lines, dim);
        SandboxPackLine(me, lines, dim);
    }

    /// <summary>The Sandbox inventory key's name for the HUD (Program sets it from the binds).</summary>
    public string SandboxMenuKey = "Tab";

    /// <summary>The ghost's rotate keys for the HUD (Program sets them from the binds; GhostTurn.cs).</summary>
    public string SandboxTurnKeys = "<- -> ^ v: turn, Shift + <- ->: roll, R: reset";

    /// <summary>The keys the backpack's line names (Program sets them from the binds).</summary>
    public string SandboxPackKeys = "H: stow, J: draw";

    private SbText _sbPackLine;

    /// <summary>The backpack on the HUD (when it holds anything): how full, what is in it, the keys.</summary>
    private void SandboxPackLine(Player me, List<HudLine> lines, Vector4 dim)
    {
        var pack = SandboxHands.Pack(me.Id);
        if (_sbPackLine.Stale(pack.Version * 64L + (SandboxPackKeys.GetHashCode() & 63)))
        {
            var used = pack.Items;
            var names = new List<string>(8);
            for (var k = 0; k < pack.SlotCount; k++)
                if (ConstructParts.Get(pack.Def[k]) is { } d)
                    names.Add(d.Name);
            _sbPackLine.Text = used == 0 ? "" : $"Backpack {used}/{pack.SlotCount}: {string.Join(", ", names)} ({SandboxPackKeys})";
        }
        if (_sbPackLine.Text.Length > 0)
            lines.Add(new HudLine(_sbPackLine.Text, dim));
    }

    /// <summary>The inventory's totals on the HUD (when it holds anything): parts and scrap.</summary>
    private void SandboxInventoryLine(Player me, List<HudLine> lines, Vector4 dim)
    {
        var inv = SandboxInv.Of(me.Id);
        if (_sbInvLine.Stale(inv.Version * 2L + (inv.Infinite ? 1 : 0)))
        {
            var parts = inv.TotalParts;
            _sbInvLine.Text = parts == 0 && inv.Scrap == 0 ? "" : $"Inventory: {parts} part{(parts == 1 ? "" : "s")} ({inv.Kinds} kinds), {inv.Scrap} scrap";
        }
        if (_sbInvLine.Text.Length > 0)
            lines.Add(new HudLine(_sbInvLine.Text, dim));
    }

    private SbText _sbGun;

    /// <summary>The gun a gunner's seat works (the first), or -1.</summary>
    private static int GunOfSeat(Construct c, int seat)
    {
        for (var k = 0; k < c.GunCount; k++)
            if (c.Guns[k].Seat == seat)
                return k;
        return -1;
    }

    /// <summary>A gun on the HUD: its rounds loaded and in the boxes, reloading, or why it won't fire (rebuilt only when that changes).</summary>
    private string GunLine(Construct c, int k)
    {
        ref var g = ref c.Guns[k];
        ref var p = ref c.Parts[g.Part];
        var boxes = ConstructWeapons.BoxRounds(c, k);
        var key = ((long)c.Id << 48) ^ ((long)g.Part << 40) ^ ((long)p.Rounds << 24) ^ ((long)boxes << 2) ^ (p.Reload > 0 ? 1 : 0) ^ (p.Broken ? 2 : 0) ^ ((long)g.Problem.GetHashCode() << 16);
        if (_sbGun.Stale(key))
            _sbGun.Text = p.Broken ? $"{p.D.Name}: wrecked"
                : g.Problem.Length > 0 ? $"{p.D.Name} won't fire: {g.Problem}"
                : g.BoxCount == 0 && p.Rounds == 0 ? $"{p.D.Name}: no ammo box on its feed"
                : p.Reload > 0 ? $"{p.D.Name}: reloading... ({boxes} in the boxes)"
                : p.Rounds == 0 && boxes == 0 ? $"{p.D.Name}: out of ammo"
                : $"{p.D.Name}: {p.Rounds}/{g.Mag}, {boxes} in the boxes";
        return _sbGun.Text;
    }

    /// <summary>What the crosshair is on (set by the client's look each frame; Program.Sandbox.cs).</summary>
    public string SandboxLookText = "";

    /// <summary>Free placement on this PC (the inventory window's toggles): snap new constructs to the world grid; anchor them to the ground (creative only).</summary>
    public bool SandboxPlaceGrid, SandboxPlaceAnchor;

    /// <summary>The tool wheel key's name (set by the client every frame; Program.SandboxWheel.cs), for the HUD's key line.</summary>
    public string SandboxWheelKey = "Mouse Middle";

    /// <summary>The linking tool's wire in progress (Program.Sandbox.cs), for the HUD.</summary>
    public string SandboxWireFrom = "";

    private readonly double[] _sbHornAt = new double[256]; // (the horns heard: the first 256)

    /// <summary>Every frame (host and clients): each horn that is on sounds, a beep every 0.3 s, where it is.</summary>
    internal void SandboxSounds(double now)
    {
        if (!SandboxOn)
            return;
        var slot = 0;
        foreach (var c in Constructs.Bodies)
            for (var i = 0; c.HasLogic && i < c.PartCount && slot < _sbHornAt.Length; i++) // (a horn is a logic part: thousands of bodies without logic cost a test each)
            {
                if (c.Parts[i].D.Logic != CLogic.Horn)
                    continue;
                if (c.Parts[i].Out >= 0.5f && now >= _sbHornAt[slot] + 0.3)
                {
                    _sbHornAt[slot] = now;
                    c.WorldFrame(c.Parts[i].Seg, out var q, out var t);
                    ref var hp = ref c.Parts[i];
                    var hd = hp.D; // (its pitch and volume settings: the configure tool)
                    Audio.Effect3DPitched(Sfx.LockTone, t + Vector3.Transform(hp.Pos, q), ConstructConfig.Get(hp, hd, CfgKey.Volume, 100) / 100f, ConstructConfig.Get(hp, hd, CfgKey.Pitch, 1));
                }
                slot++;
            }
    }
}
