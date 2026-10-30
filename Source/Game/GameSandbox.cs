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
    Rack, // u8 pick: 0 the rack's next kind (at the rack); n: part id n - 1 from the debug parts menu, anywhere (protocol 79)
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
        if (!IsServer)
            return;
        if (had || SandboxOn)
        {
            ConstructNet.WriteClear(SbW);
            Net.Broadcast(SbW.Span, PacketReliability.ReliableOrdered);
        }
        if (SandboxOn && World.Map.Sandbox is { } sandbox)
            Constructs.Populate(sandbox, World.Query); // (their structure goes out on the first playing tick)
    }

    /// <summary>Client: a match starts: nothing until the server sends it.</summary>
    internal void ResetSandboxClient()
    {
        if (IsServer)
            return;
        Constructs.Clear();
        Constructs.Removed.Clear();
        SandboxHands.Reset();
        SandboxHands.ResetPacks();
        SandboxInv.Reset();
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
                h.Edit = EditKind.None;
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
        using (Perf.Measure(PerfArea.Players))
            Constructs.Step(World.Query, World.TickDt);
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
        for (var from = 0; from < Constructs.HealthDirty.Count;)
        {
            from = ConstructNet.WriteHealth(w, Constructs, Constructs.HealthDirty, from);
            Net.Broadcast(w.Span, PacketReliability.ReliableOrdered);
        }
        Constructs.HealthDirty.Clear();
        if (ServerTick % ConstructNet.PoseEvery == 0 && ConstructNet.WritePoses(w, Constructs, ServerTick, ref _sbSleepCursor))
            Net.Broadcast(w.Span, PacketReliability.UnreliableSequenced);
        SandboxInventoryFlush();
        foreach (var e in Constructs.Events)
            Console.WriteLine($"[sandbox] {e}");
        Constructs.Events.Clear();
        foreach (var e in SandboxHands.Done)
            Console.WriteLine($"[sandbox] {e}");
        SandboxHands.Done.Clear();
    }

    /// <summary>Server: a joining client gets every construct as it is now.</summary>
    internal void SendSandboxState(RakNetGUID to)
    {
        if (!SandboxOn)
            return;
        var w = SbW;
        ConstructNet.WriteClear(w);
        Net.SendTo(to, w.Span, PacketReliability.ReliableOrdered);
        foreach (var c in Constructs.Bodies)
        {
            ConstructNet.WriteFull(w, c);
            Net.SendTo(to, w.Span, PacketReliability.ReliableOrdered);
        }
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
            case CActKind.Tool:
                return hands.SetTool(p.Id, (ConstructTool)Math.Min(r.U8(), (byte)ConstructTool.Linker));
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
            case CActKind.Cancel:
                hands.Cancel(p.Id);
                return null;
            case CActKind.Use:
            {
                var body = r.U16();
                var part = r.U8();
                return hands.Use(Constructs, p.Id, eye, body, part);
            }
            case CActKind.Stand:
                SandboxStand(p, teleport: true);
                return null;
            case CActKind.Rack:
            {
                var pick = r.U8();
                return pick == 0 ? hands.TakeFromRack(World.Map.Sandbox, p.Id, p.Position) : hands.TakePart(World.Map.Sandbox, p.Id, (byte)(pick - 1));
            }
            case CActKind.Blueprint:
                return SandboxSpawnBlueprint(p, r.U8());
            case CActKind.Drop:
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
            case CActKind.SeatKeys:
            case CActKind.Rack:
            case CActKind.Blueprint:
                w.U8(part);
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

    // ---- The debug parts menu (UiSandboxParts.cs) ------------------------------------------------------------------

    /// <summary>The debug parts menu is allowed: Sandbox on a map whose sandbox section doesn't turn it off (a future survival map can).</summary>
    public bool SandboxPartsMenu => SandboxOn && World.Map.Sandbox is { PartsMenu: true };

    /// <summary>Client (or host): ask for a part by id from the debug parts menu (the rack's request with a part id).</summary>
    public void SandboxTakePart(byte id) => SandboxSend(CActKind.Rack, part: (byte)(id + 1));

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

    /// <summary>A moving construct part closing on a player faster than this (m/s) hurts them (run over, or hit by a swinging arm).</summary>
    public const float RunOverSpeed = 4f;

    public const double RunOverCooldown = 0.6;

    private readonly double[] _sbHitAt = new double[256];

    /// <summary>Run-over damage: by how fast the part closed on them and how heavy its construct is; the driver (if any) gets the credit.</summary>
    public static int RunOverDamage(float closing, float mass) =>
        closing <= RunOverSpeed ? 0 : (int)MathF.Round((closing - RunOverSpeed) * 9f * Math.Clamp(MathF.Sqrt(mass / 500f), 0.4f, 2.5f));

    private void SandboxRunOver(Player p, ref MoveState s, Construct car)
    {
        var now = SandboxNow;
        if (now < _sbHitAt[p.Id] + RunOverCooldown)
            return;
        _sbHitAt[p.Id] = now;
        var dmg = RunOverDamage(Constructs.LastHitSpeed, Constructs.LastHitMass);
        // Knocked along with the car, and a little up.
        s.Velocity += Constructs.LastHitVel * 0.5f + new Vector3(0, 2.5f, 0);
        s.Grounded = false;
        var by = car.Driver != 255 && car.Driver != p.Id && Players.TryGetValue(car.Driver, out var d) ? d : p;
        Constructs.Events.Add($"player {p.Id} hit by construct {car.Id} at {Constructs.LastHitSpeed:0.0} m/s: {dmg} damage");
        if (dmg > 0)
            DamagePlayer(by, p, dmg, false, MeleeKillId, now, DamageKind.Melee, car.WorldCom);
    }

    /// <summary>Is this player in a seat? (Drivers and passengers don't shoot: construct weapons are milestone 3.)</summary>
    public bool SandboxSeated(Player p) => SandboxOn && SandboxHands.Hand(p.Id).Seated;

    /// <summary>A bullet's ray against the constructs: shortens <paramref name="dist"/> to the part it hits.</summary>
    private ConstructHit? SandboxRay(Vector3 o, Vector3 d, ref float dist)
    {
        if (!SandboxOn || Constructs.Bodies.Count == 0 || !Constructs.Raycast(o, d, dist, out var hit))
            return null;
        dist = hit.Distance;
        return hit;
    }

    private void SandboxShotDamage(ConstructHit hit, float damage)
    {
        if (Constructs.Get(hit.Body.Id) is not { } c || hit.Part >= c.PartCount)
            return;
        if (hit.SlotPlus1 > 0)
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
            var tool = h.Tool switch { ConstructTool.Wrench => "Wrench", ConstructTool.Welder => "Welder", ConstructTool.Linker => "Linking tool", _ => "Gun" };
            _sbHand.Text = $"In hand: {tool}{(held != null ? $" + {held.Name}" : "")}";
        }
        lines.Add(new HudLine(_sbHand.Text, h.Tool == ConstructTool.None ? dim : orange));
        if (h.Edit != EditKind.None)
        {
            var pct = (int)(h.Progress(SandboxNow) * 100);
            if (_sbEdit.Stale((long)h.Edit * 1000 + pct))
                _sbEdit.Text = $"{(h.Edit == EditKind.Remove ? "Taking it off" : "Fitting")}... {pct}%";
            lines.Add(new HudLine(_sbEdit.Text, green));
        }
        else if (SandboxLookText.Length > 0)
            lines.Add(new HudLine(SandboxLookText, white));
        if (SandboxNotice is { Length: > 0 } notice)
            lines.Add(new HudLine(notice, red));
        else if (h.Tool == ConstructTool.Linker)
            lines.Add(new HudLine(SandboxWireFrom.Length > 0 ? SandboxWireFrom : "LMB: wire from an output, then LMB on an input. RMB: cut its wires. R: its setting", dim));
        else if (held == null)
        {
            if (_sbKeys.Stale(SandboxWheelKey.GetHashCode()))
                _sbKeys.Text = $"F at the rack or I (parts menu): take a part. Hold {SandboxWheelKey}: tool wheel. 3 wrench, 4 welder, 5 linking tool, 1/2 guns";
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

    private readonly double[] _sbHornAt = new double[ConstructWorld.MaxBodies * 4];

    /// <summary>Every frame (host and clients): each horn that is on sounds, a beep every 0.3 s, where it is.</summary>
    internal void SandboxSounds(double now)
    {
        if (!SandboxOn)
            return;
        var slot = 0;
        foreach (var c in Constructs.Bodies)
            for (var i = 0; i < c.PartCount && slot < _sbHornAt.Length; i++)
            {
                if (c.Parts[i].D.Logic != CLogic.Horn)
                    continue;
                if (c.Parts[i].Out >= 0.5f && now >= _sbHornAt[slot] + 0.3)
                {
                    _sbHornAt[slot] = now;
                    c.WorldFrame(c.Parts[i].Seg, out var q, out var t);
                    Audio.Effect3D(Sfx.LockTone, t + Vector3.Transform(c.Parts[i].Pos, q), 1f);
                }
                slot++;
            }
    }
}
