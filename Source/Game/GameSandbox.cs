using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>What a <see cref="Msg.CAct"/> asks for (its first byte).</summary>
public enum CActKind : byte
{
    Tool, // u8 tool
    Attach, // u16 body, u8 part, u8 socket, u8 variant
    PlaceNew, // vec ground, f32 yaw
    Remove, // u16 body, u8 part
    Cancel,
    Use, // u16 body, u8 part
    Stand,
    Rack,
    Drop,
    PickUp, // u16 body
    Wire, // u16 body, u8 from part, u8 to part (the linking tool; milestone 2)
    Unwire, // u16 body, u8 part: cut every wire on it
    Setting, // u16 body, u8 part: a logic part's next setting
    SeatKeys, // u8: seated, the fire / aim / reload keys held (input readers)
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
        _sbPoseTick = 0;
    }

    internal void SandboxOnSpawn(Player p)
    {
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
        Constructs.PlayerSpotCount = 0;
        foreach (var p in Players.Values)
        {
            ref var h = ref SandboxHands.Hand(p.Id);
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
            if (seat == c.DriveSeat)
            {
                c.Driver = p.Id;
                c.DriverSeat = seat;
                ConstructEdits.DriveInput(c, b, World.TickDt);
            }
        }
        _sbEye ??= id => Players.TryGetValue(id, out var pl) && pl.Alive ? pl.Eye : null;
        SandboxHands.Tick(Constructs, now, _sbEye);
        using (Perf.Measure(PerfArea.Players))
            Constructs.Step(World.Query, World.TickDt);
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
        for (var from = 0; from < Constructs.HealthDirty.Count;)
        {
            from = ConstructNet.WriteHealth(w, Constructs, Constructs.HealthDirty, from);
            Net.Broadcast(w.Span, PacketReliability.ReliableOrdered);
        }
        Constructs.HealthDirty.Clear();
        if (ServerTick % ConstructNet.PoseEvery == 0 && ConstructNet.WritePoses(w, Constructs, ServerTick, ref _sbSleepCursor))
            Net.Broadcast(w.Span, PacketReliability.UnreliableSequenced);
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
    }

    // ---- Packets ----------------------------------------------------------------------------------------------------

    internal void OnSandboxPacket(byte id, ref PacketReader r, RakNetGUID sender, double now)
    {
        if (!IsServer)
            SandboxNow = now;
        switch (id)
        {
            case (byte)Msg.CBody when !IsServer:
                ConstructNet.Read(ref r, Constructs, SandboxNow);
                break;
            case (byte)Msg.CPose when !IsServer:
                ConstructNet.ReadPoses(ref r, Constructs, SandboxNow, ref _sbPoseTick);
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
                var yaw = r.F32();
                if (!float.IsFinite(at.X + at.Y + at.Z + yaw))
                    return "bad place";
                // The ground really is there (a ray from just above the point the client chose).
                var down = World.Query.Raycast(at + new Vector3(0, 0.5f, 0), -Vector3.UnitY, 1.5f, QueryFilter.All);
                if (down >= 1.5f)
                    return "put it on the ground";
                return hands.BeginPlaceNew(Constructs, p.Id, eye, at + new Vector3(0, 0.5f - down, 0), yaw, now);
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
                return hands.TakeFromRack(World.Map.Sandbox, p.Id, p.Position);
            case CActKind.Drop:
                return hands.Drop(Constructs, p.Id, p.Position, p.Yaw);
            case CActKind.PickUp:
                return hands.PickUp(Constructs, p.Id, eye, r.U16());
            case CActKind.Wire:
            {
                var body = r.U16();
                var from = r.U8();
                var to = r.U8();
                return hands.Wire(Constructs, p.Id, eye, body, from, to);
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
        }
        return null;
    }

    /// <summary>Client (or the host itself): ask the server for an action. The host's own action is handled at once.</summary>
    public void SandboxSend(CActKind kind, ushort body = 0, byte part = 0, byte socket = 0, byte variant = 0, Vector3 at = default, float yaw = 0)
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
                w.Vec(at).F32(yaw);
                break;
            case CActKind.Remove:
            case CActKind.Use:
            case CActKind.Unwire:
            case CActKind.Setting:
                w.U16(body).U8(part);
                break;
            case CActKind.Wire:
                w.U16(body).U8(part).U8(socket); // (from, to)
                break;
            case CActKind.SeatKeys:
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
        Constructs.DamagePart(c, hit.Part, damage, out _);
    }

    /// <summary>A blast near constructs (grenades, kill streaks, explosive props): every part in range takes its share.</summary>
    private void SandboxBlast(Vector3 center, float radius, float damage)
    {
        if (SandboxOn && Constructs.Bodies.Count > 0)
            Constructs.Blast(center, radius, damage);
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

    private SbText _sbDrive, _sbProblem, _sbHand, _sbEdit, _sbHint;

    /// <summary>The objective panel: what is in your hands, what you look at, the drive.</summary>
    internal void SandboxHud(Player me, List<HudLine> lines, Vector4 white, Vector4 dim, Vector4 orange, Vector4 green, Vector4 red)
    {
        ref var h = ref SandboxHands.Hand(me.Id);
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
            if (_sbDrive.Stale(kmh * 100000L + fuel))
                _sbDrive.Text = $"Driving: {kmh} km/h, fuel {fuel / 10f:0.0} l";
            lines.Add(new HudLine(_sbDrive.Text, white));
            if (car.CanDrive)
                lines.Add(new HudLine("W/S drive, A/D steer, Space brake, F get out", dim));
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
            lines.Add(new HudLine("F at the rack: take a part. 3 wrench, 4 welder, 5 linking tool, 1/2 guns", dim));
        else
        {
            if (_sbHint.Stale(held.Id))
                _sbHint.Text = $"{held.Name}: needs the {held.Tool.ToString().ToLowerInvariant()}. LMB hold: fit, R: turn, T: drop";
            lines.Add(new HudLine(_sbHint.Text, dim));
        }
    }

    /// <summary>What the crosshair is on (set by the client's look each frame; Program.Sandbox.cs).</summary>
    public string SandboxLookText = "";

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
