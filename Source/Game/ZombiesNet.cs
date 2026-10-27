using System.Numerics;
using System.Text;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>One-off Zombies events the server sends (<see cref="Msg.ZEvent"/>).</summary>
public enum ZEventKind : byte
{
    /// <summary>A kill-feed line for one player, with a sound.</summary>
    Feed,

    /// <summary>Your gun in slot (arg) was refilled (wall ammo).</summary>
    Refill,

    /// <summary>A big line across the middle of the screen (ROUND 3, THE POWER IS ON).</summary>
    Banner,

    /// <summary>Max Ammo: every gun in your slots gets a full reserve (protocol 40).</summary>
    MaxAmmo,

    /// <summary>A special ammo effect went off (arg: the <see cref="ZFxKind"/>; the position follows the text; protocol 40).</summary>
    Fx,
}

/// <summary>
/// Zombies networking (protocol 18; power-ups and special ammo effects since 40; the messages are at the end of <see cref="Msg"/>): the zombies (<see cref="Msg.ZEnts"/>,
/// with every snapshot), the run state (<see cref="Msg.ZWorld"/>, 10 times a second: round, doors, boards, the box,
/// every player's points, downs and perks, parts on the floor), events (<see cref="Msg.ZEvent"/>), a player's loose
/// parts (<see cref="Msg.ZRun"/>), built weapons (<see cref="Msg.ZBuild"/>: the build's text, so every PC assembles the
/// same weapon and co-op clients can see and fire built guns), and from clients the interact key (<see cref="Msg.ZUse"/>)
/// and workbench changes (<see cref="Msg.ZBench"/>).
/// </summary>
public sealed partial class Game
{
    private bool _zEntsLive;
    private readonly HashSet<ulong> _zSynced = new();

    /// <summary>Client: builds for players not known yet (applied once they show up).</summary>
    private readonly List<(byte Player, int Slot, byte Weapon, byte Flags, string Text)> _zPendingBuilds = new();

    /// <summary>The big centre line (ROUND 3...) and when it went up (local clock).</summary>
    public string ZBannerText = "";

    public double ZBannerTime = -100;

    /// <summary>The local player's +N / -N points popups (local clock, amount).</summary>
    public readonly List<(double Time, int Amount)> ZPopups = new();

    // ---- Server: sending ----------------------------------------------------------------------------------

    private void ServerSendZombies(double now)
    {
        if (Net.IsOffline)
            return;
        // Someone new: every player's builds, so they see (and can use) them.
        foreach (var (guid, id) in _clients.Values)
        {
            if (_zSynced.Contains(guid.G))
                continue;
            _zSynced.Add(guid.G);
            foreach (var p in Players.Values)
                if (ZPlayers.TryGetValue(p.Id, out var zp))
                    for (var s = 0; s < BuildSlots; s++)
                        if (zp.Slots[s] is { } b)
                        {
                            var w = s switch { 0 => p.Primary, 1 => p.Secondary, 2 => p.BuiltExtra[0]?.Id ?? 0, 3 => p.BuiltExtra[1]?.Id ?? 0, _ => (byte)0 };
                            WriteBuild(_w, p.Id, s, w, 0, b.ToText());
                            Net.SendTo(guid, _w.Span, PacketReliability.ReliableOrdered);
                        }
            foreach (var p in Players.Values)
                if (ZPlayers.TryGetValue(p.Id, out var zp))
                    SendEmptySlotsTo(guid, p, zp); // slots taken apart at a workbench (protocol 41, ZombiesDisassemble.cs)
            if (Players.TryGetValue(id, out var me))
                SendZRun(me);
            SendWeaponDropsTo(guid, now); // the builds on the floor (ZombiesDrops.cs)
            SendCasinoTo(guid); // the casino tables (ZombiesCasinoNet.cs)
        }
        if (ServerTick % World.SnapshotEvery == 0 && (Horde.Count > 0 || _zEntsLive))
        {
            // Interest-managed (protocol 58): each client gets the zombies that matter most to it this snapshot.
            foreach (var (guid, id) in _clients.Values)
            {
                if (!_zViews.TryGetValue(guid.G, out var view))
                    _zViews[guid.G] = view = new ZEntView(); // (once per client)
                var at = Players.TryGetValue(id, out var cp) ? cp.Position : Vector3.Zero;
                WriteZEntsFor(_w, view, at);
                Net.SendTo(guid, _w.Span, PacketReliability.Unreliable); // (not sequenced: a late packet still has news of the zombies the next one left out)
                ZEntsBytesSent += _w.Length;
                ZEntsPacketsSent++;
            }
            _zEntsLive = Horde.Count > 0;
            if (_zViews.Count > _clients.Count)
                ForgetGoneViews();
        }
        if (ServerTick % (World.SnapshotEvery * 3) == 0)
        {
            WriteZWorld(_w);
            Net.Broadcast(_w.Span, PacketReliability.Unreliable);
        }
    }

    /// <summary>Most bytes per zombie in <see cref="Msg.ZEnts"/> with all its limbs (12 with the window byte, 11 without: protocol 58).</summary>
    public const int ZEntBytes = 12;

    /// <summary>
    /// A zombie that has lost a limb carries one more byte (protocol 65): the top bit of its id says so, the byte holds the
    /// lost limbs (bits 0-3: right arm, left arm, right leg, left leg; both legs is a crawler). 9 + 80 x 13 = 1049 bytes at most.
    /// </summary>
    public const int ZEntLimbBytes = 1, ZEntLimbFlag = 0x8000;

    /// <summary>The header of <see cref="Msg.ZEnts"/>: the message id, the tick, the horde's size and the entry count (protocol 58).</summary>
    public const int ZEntsHeader = 1 + 4 + 2 + 2;

    /// <summary>
    /// The most zombies in one <see cref="Msg.ZEnts"/> (9 + 80 x 12 = 969 bytes at most, one datagram; about 28 KB/s per
    /// client at 30 Hz, what 90 zombies a packet cost before protocol 58). A horde bigger than this is interest-managed.
    /// </summary>
    public const int ZEntsMax = 80;

    /// <summary>Interest management: zombies nearer a client than these (m) count 8x, 3x and 1.5x as much as far ones.</summary>
    public const float ZEntNear = 12f, ZEntMid = 30f, ZEntFar = 60f;

    /// <summary>A zombie a client hasn't heard about for this many snapshots goes in the next one whatever (0.8 s at 30 Hz).</summary>
    public const int ZEntMaxAge = 24;

    /// <summary>Client: a zombie not heard about for this long (s) is dropped (the server sends each at least every <see cref="ZEntMaxAge"/> snapshots).</summary>
    public const double ZEntTimeout = 1.6;

    /// <summary>Bytes and packets of <see cref="Msg.ZEnts"/> sent this run (self-test, logs).</summary>
    public long ZEntsBytesSent, ZEntsPacketsSent;

    /// <summary>What one client has been sent, by zombie slot: the id there, snapshots since, and the state byte it last got.</summary>
    public sealed class ZEntView
    {
        public readonly ushort[] SentId = new ushort[MaxZombies];
        public readonly byte[] Age = new byte[MaxZombies];
        public readonly byte[] SentState = new byte[MaxZombies];
        public readonly byte[] SentLimbs = new byte[MaxZombies];
    }

    private readonly Dictionary<ulong, ZEntView> _zViews = new();
    private float[] _zEntKey = new float[MaxZombies];
    private int[] _zEntIdx = new int[MaxZombies];
    private readonly List<ulong> _zGoneViews = new();

    private void ForgetGoneViews()
    {
        _zGoneViews.Clear();
        foreach (var g in _zViews.Keys)
            if (!_clients.ContainsKey(g))
                _zGoneViews.Add(g);
        foreach (var g in _zGoneViews)
            _zViews.Remove(g);
    }

    /// <summary>Every zombie (up to <see cref="ZEntsMax"/>, in list order): what a client with nothing to prioritise gets (self-tests).</summary>
    public void WriteZEnts(PacketWriter w) => WriteZEntsFor(w, null, Vector3.Zero);

    /// <summary>
    /// The zombies for a client at <paramref name="viewer"/> (protocol 58): each zombie's priority is how many snapshots
    /// since that client last heard of it times how near it is (8x within <see cref="ZEntNear"/>, 3x within
    /// <see cref="ZEntMid"/>, 1.5x within <see cref="ZEntFar"/>), with a new zombie, a changed state (a death, a climb)
    /// or one unheard of for <see cref="ZEntMaxAge"/> snapshots going first; the top <see cref="ZEntsMax"/> go. A horde of
    /// up to that many is sent whole every snapshot (as before); a bigger one keeps the near zombies at 30 Hz and the far
    /// ones at a few Hz. No allocation. <paramref name="view"/> null: the first <see cref="ZEntsMax"/> in list order.
    /// </summary>
    public void WriteZEntsFor(PacketWriter w, ZEntView? view, Vector3 viewer)
    {
        var count = Horde.Count;
        if (_zEntKey.Length < count)
        {
            _zEntKey = new float[count];
            _zEntIdx = new int[count];
        }
        var n = 0;
        for (var i = 0; i < count; i++)
        {
            var z = Horde[i];
            var key = (float)i; // (no view: list order)
            if (view != null)
            {
                var slot = z.Hm.Slot;
                if (slot < 0 || slot >= MaxZombies)
                    key = -1e6f; // (no slot: always)
                else
                {
                    var dx = z.Position.X - viewer.X;
                    var dz = z.Position.Z - viewer.Z;
                    var d2 = dx * dx + dz * dz;
                    var weight = d2 < ZEntNear * ZEntNear ? 8f : d2 < ZEntMid * ZEntMid ? 3f : d2 < ZEntFar * ZEntFar ? 1.5f : 1f;
                    var isNew = view.SentId[slot] != z.Id;
                    var age = isNew ? 255 : Math.Min(255, view.Age[slot] + 1);
                    var p = age * weight;
                    if (isNew)
                        p += 1000f;
                    else if (view.SentState[slot] != ZStateByte(z) || view.SentLimbs[slot] != z.Limbs)
                        p += 600f; // (a limb coming off goes out at once, like a death)
                    if (age >= ZEntMaxAge)
                        p += 5000f;
                    key = -p;
                }
            }
            _zEntKey[n] = key;
            _zEntIdx[n] = i;
            n++;
        }
        if (view != null && n > ZEntsMax)
            Array.Sort(_zEntKey, _zEntIdx, 0, n);
        var send = Math.Min(n, ZEntsMax);
        w.Begin(Msg.ZEnts).U32(ServerTick).U16((ushort)Math.Min(count, 65535)).U16((ushort)send);
        for (var k = 0; k < send; k++)
        {
            var z = Horde[_zEntIdx[k]];
            var p = z.Position;
            var sb = ZStateByte(z);
            w.U16((ushort)(z.Id | (z.Limbs != 0 ? ZEntLimbFlag : 0))).U16(Q(p.X, 32)).U16(Q(p.Y, 64)).U16(Q(p.Z, 32))
                .U8(ZAnimByteState(z.State, z.Window) ? z.AnimByte : (byte)((int)MathF.Round(World.WrapAngle(z.Yaw) / MathF.Tau * 256f) & 255))
                .U8(sb)
                .U8((byte)Math.Clamp(z.MaxHealth > 0 ? z.Health * 255 / z.MaxHealth : 0, 0, 255));
            if (ZEntHasWindow(z.State))
                w.U8(z.Window is >= 0 and < 255 ? (byte)z.Window : (byte)255);
            if (z.Limbs != 0)
                w.U8(z.Limbs); // the lost limbs (protocol 65, ZombieLimbs.cs)
            if (view != null && z.Hm.Slot is >= 0 and < MaxZombies)
            {
                view.SentId[z.Hm.Slot] = z.Id;
                view.Age[z.Hm.Slot] = 0;
                view.SentState[z.Hm.Slot] = sb;
                view.SentLimbs[z.Hm.Slot] = z.Limbs;
            }
        }
        if (view != null)
            for (var k = send; k < n; k++)
            {
                var slot = Horde[_zEntIdx[k]].Hm.Slot;
                if (slot is >= 0 and < MaxZombies && view.Age[slot] < 255)
                    view.Age[slot]++;
            }
    }

    /// <summary>The state byte: state (bits 0-2), head hit (3), speed (4-5), burning and zapped (6-7).</summary>
    private byte ZStateByte(Zombie z) =>
        (byte)((byte)z.State | (ZHeadBit(z) ? 8 : 0) | ((byte)z.Speed << 4) | (z.FxBurning ? 64 : 0) | (z.FxZapped ? 128 : 0)); // bits 6-7: burning, zapped (protocol 40); bit 3: head hit (protocol 50)

    /// <summary>Does a zombie's entry carry its window byte (protocol 58: only outside, at a window or swiping)?</summary>
    public static bool ZEntHasWindow(ZState s) => s is ZState.ToWindow or ZState.Tearing or ZState.Climbing or ZState.Attack;

    /// <summary>
    /// Bit 3 of a zombie's state byte (protocol 50): alive, a head hit landed in the last few ticks (two snapshots see it);
    /// dead, the kill was a headshot. Clients use it for the headshot screech and the headshot ragdoll.
    /// </summary>
    private bool ZHeadBit(Zombie z) => z.State == ZState.Dead ? z.KilledByHead : ServerTick - z.HeadHitTick <= 4;

    private static ushort Q(float v, float scale) => (ushort)(short)Math.Clamp((int)MathF.Round(v * scale), short.MinValue, short.MaxValue);

    private static float DQ(ushort v, float scale) => (short)v / scale;

    /// <summary>The run: round, phase, doors, boards, the box, every player's points, downs and perks, parts on the floor.</summary>
    public void WriteZWorld(PacketWriter w)
    {
        uint doors = 0;
        if (World.Map.Zombies is { } zm)
            for (var d = 0; d < zm.Doors.Count; d++)
                if (World.Map.IsDoorOpen(d))
                    doors |= 1u << d;
        w.Begin(Msg.ZWorld).U32(ServerTick).U16((ushort)Math.Min(ZRound, 65535)).U8((byte)ZPhase).F32((float)ZPhaseLeft).U16((ushort)Math.Min(ZRemaining, 65535))
            .U8(ZPower ? (byte)1 : (byte)0).U32(doors).U8((byte)ZBoards.Length);
        foreach (var b in ZBoards)
            w.U8(b);
        w.U8((byte)BoxSpot).U8((byte)BoxState).F32((float)BoxLeft).U16(BoxBuild < 0 ? (ushort)(BoxBuild == -2 ? 0xFFFE : 0xFFFF) : (ushort)BoxBuild).U8(BoxOwner);
        var players = Players.Values.Take(32).ToList();
        w.U8((byte)players.Count);
        foreach (var p in players)
        {
            var zp = ZP(p);
            w.U8(p.Id).U32((uint)Math.Max(0, zp.Points)).U8((byte)((zp.Downed ? 1 : 0) | (p.ExtraSlots ? 2 : 0) | (zp.SelfReviveAt > 0 ? 4 : 0) | (zp.RoundReady ? 8 : 0) | (zp.ReadyVoter ? 16 : 0) | (zp.PlateReady ? 32 : 0))).U16(zp.Perks) // bits 3-4: ready-up (protocol 47); bit 5: the armour plate is up, perks 16 bits (protocol 57)
                .U8((byte)(Math.Clamp(zp.BleedFraction, 0, 1) * 255)).U8((byte)(Math.Clamp(zp.BeingRevived, 0, 1) * 255))
                .U16((ushort)Math.Min(zp.Kills, 65535)).U16((ushort)Math.Min(zp.Downs, 65535)).U16((ushort)Math.Min(zp.Revives, 65535))
                .U8((byte)p.Grenade).U8((byte)Math.Clamp(p.GrenadesLeft, 0, 255)); // the grenade and how many are left (protocol 63)
        }
        var drops = ZDrops.Take(32).ToList();
        w.U8((byte)drops.Count);
        foreach (var d in drops)
        {
            var idx = Array.FindIndex(Parts.All, x => x.Name == d.Part);
            w.U16(d.Id).U16((ushort)Math.Max(0, idx)).U16(Q(d.At.X, 32)).U16(Q(d.At.Y, 64)).U16(Q(d.At.Z, 32));
        }
        WritePowerUps(w, _zNow); // power-ups and their timers (protocol 40, ZombiesPowerUps.cs)
        w.U8(ZWaitingReady ? (byte)1 : (byte)0); // the break waits for ready-up (protocol 47, ZombiesFlow.cs)
        WriteSlots(w); // the slot machines (protocol 51, ZombiesSlots.cs)
    }

    /// <summary>Server: the time of the last Zombies tick (what the power-up times in ZWorld count from).</summary>
    private double _zNow;

    private static void WriteBuild(PacketWriter w, byte player, int slot, byte weapon, byte flags, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var n = Math.Min(bytes.Length, 6000);
        w.Begin(Msg.ZBuild).U8(player).U8((byte)slot).U8(weapon).U8(flags).U16((ushort)n).Bytes(bytes.AsSpan(0, n));
    }

    private static string ReadLong(ref PacketReader r)
    {
        if (!r.Has(2))
            return "";
        int n = Math.Min(r.U16(), r.Remaining); // a lying length can't allocate more than the packet holds
        var bytes = new byte[n];
        for (var i = 0; i < n; i++)
            bytes[i] = r.U8();
        return Encoding.UTF8.GetString(bytes);
    }

    /// <summary>Server: a build went into a player's slot: the host's own slots change now, every client hears it.</summary>
    private void BroadcastBuild(Player p, int slot, byte weapon, WeaponBuild b, bool full, bool start)
    {
        if (p.Id == LocalId && slot < 4)
            LocalSetSlot(slot, p.DefFor(weapon), full, start, select: !start || slot == 0);
        if (Net.IsOffline)
            return;
        WriteBuild(_w, p.Id, slot, weapon, (byte)((full ? 1 : 0) | (start ? 2 : 0)), b.ToText());
        Net.Broadcast(_w.Span, PacketReliability.ReliableOrdered);
    }

    /// <summary>Server: <paramref name="p"/>'s loose parts (to that player).</summary>
    private void SendZRun(Player p)
    {
        if (p.Id == LocalId || p.Bot != null || Net.IsOffline || GuidOf(p) is not { } g)
            return;
        WriteZRun(_w, ZP(p));
        Net.SendTo(g, _w.Span, PacketReliability.ReliableOrdered);
    }

    private static void WriteZRun(PacketWriter w, ZPlayer zp)
    {
        w.Begin(Msg.ZRun).U8((byte)Math.Min(zp.Parts.Count, 200));
        foreach (var (name, n) in zp.Parts.Take(200))
            w.Str(name).U8((byte)Math.Min(n, 255));
    }

    /// <summary>Client: your loose parts (<see cref="Msg.ZRun"/>, after the message id; no scrap since protocol 56).</summary>
    private void ReadZRun(ref PacketReader r)
    {
        if (!r.Has(1))
            return;
        var zp = ZP(LocalId);
        int n = r.U8();
        zp.Parts.Clear();
        for (var i = 0; i < n && r.Has(2); i++)
        {
            var name = r.Str();
            var count = r.Has(1) ? r.U8() : 0;
            if (count > 0)
                zp.Parts[name] = count;
        }
    }

    /// <summary>The connection of a remote person, or null (the host, bots, unknown).</summary>
    private RakNetGUID? GuidOf(Player p)
    {
        foreach (var c in _clients.Values)
            if (c.Id == p.Id && c.Guid.G != 0)
                return c.Guid;
        return null;
    }

    private void SendZEvent(Player p, ZEventKind kind, ushort arg, string text, Sfx sound = Sfx.HitTick)
    {
        if (Net.IsOffline || GuidOf(p) is not { } g)
            return;
        _w.Begin(Msg.ZEvent).U8((byte)kind).U16(arg).U8((byte)sound).Str(text.Length > 200 ? text[..200] : text);
        Net.SendTo(g, _w.Span, PacketReliability.ReliableOrdered);
    }

    /// <summary>Server: a kill-feed line for one player (the host's own goes straight in).</summary>
    private void ZFeedTo(Player p, string text, Sfx sound)
    {
        if (p.Id == LocalId)
        {
            Log(text, true);
            Audio.Effect(sound, 0.6f, MixBus.Notify);
        }
        else if (p.Bot == null)
            SendZEvent(p, ZEventKind.Feed, 0, text, sound);
    }

    /// <summary>Server: the big centre line for everyone.</summary>
    private void ZBanner(string text)
    {
        ZBannerText = text;
        ZBannerTime = Brutal.GlfwApi.Glfw.Time;
        if (Net.IsOffline)
            return;
        _w.Begin(Msg.ZEvent).U8((byte)ZEventKind.Banner).U16(0).U8((byte)Sfx.HitTick).Str(text);
        Net.Broadcast(_w.Span, PacketReliability.ReliableOrdered);
    }

    // ---- Both sides: the interact key and the workbench ------------------------------------------------------

    private bool _localUseHeld;

    /// <summary>Every frame from the input code: is the interact key held? (Zombies only: buys, revives and rebuilding boards go on the hold.)</summary>
    public void SetUseHeld(bool held, double now)
    {
        if (Mode != GameMode.Zombies || Local is not { } me)
            return;
        if (held == _localUseHeld)
            return;
        _localUseHeld = held;
        if (IsServer)
            ServerSetUse(me, held, now);
        else
        {
            _w.Begin(Msg.ZUse).U8(held ? (byte)1 : (byte)0);
            Net.SendToServer(_w.Span, PacketReliability.ReliableOrdered);
        }
    }

    /// <summary>The local player's workbench change (see <see cref="ServerBench"/>): applied now on the host, sent to the server by a client.</summary>
    public void BenchRequest(int kind, int slot, string text)
    {
        if (Local is not { } me)
            return;
        if (IsServer)
        {
            if (ServerBench(me, kind, slot, text) is { } why)
                LoadoutNotice = $"Workbench: {why}";
            return;
        }
        WriteBenchRequest(_w, kind, slot, text);
        Net.SendToServer(_w.Span, PacketReliability.ReliableOrdered);
    }

    /// <summary>A client's <see cref="Msg.ZBench"/>: the kind, the slot and the text (a build, a part name, or empty).</summary>
    public static void WriteBenchRequest(PacketWriter w, int kind, int slot, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var n = Math.Min(bytes.Length, 6000);
        w.Begin(Msg.ZBench).U8((byte)kind).U8((byte)slot).U16((ushort)n).Bytes(bytes.AsSpan(0, n));
    }

    /// <summary>Server: runs a <see cref="Msg.ZBench"/> from <paramref name="p"/> (after the message id); returns why not, or null.</summary>
    private string? OnBenchRequest(Player p, ref PacketReader r)
    {
        var kind = r.U8();
        var slot = r.U8();
        var text = ReadLong(ref r);
        return ServerBench(p, kind, slot, text);
    }

    /// <summary>Self-test: the server receives <paramref name="msg"/> (a whole <see cref="Msg.ZBench"/>) from player <paramref name="p"/>.</summary>
    public string? SelfTestBenchPacket(Player p, ReadOnlySpan<byte> msg)
    {
        var r = new PacketReader(msg[1..]);
        return OnBenchRequest(p, ref r);
    }

    // ---- Packets ------------------------------------------------------------------------------------------

    internal void OnZombiesPacket(byte id, ref PacketReader r, RakNetGUID sender, double now)
    {
        switch (id)
        {
            case (byte)Msg.ZUse when IsServer && r.Has(1):
                if (_clients.TryGetValue(sender.G, out var c) && Players.TryGetValue(c.Id, out var p))
                    ServerSetUse(p, r.U8() != 0, now);
                break;
            case (byte)Msg.ZBench when IsServer && r.Has(2):
                if (_clients.TryGetValue(sender.G, out var bc) && Players.TryGetValue(bc.Id, out var bp))
                {
                    if (OnBenchRequest(bp, ref r) is { } why)
                        ZFeedTo(bp, $"Workbench: {why}", Sfx.DryFire);
                }
                break;
            case (byte)Msg.ZEnts when !IsServer:
                ReadZEnts(ref r, now);
                break;
            case (byte)Msg.ZWorld when !IsServer:
                ReadZWorld(ref r, now);
                break;
            case (byte)Msg.ZCasino when !IsServer:
                ReadCasino(ref r); // a casino table's state (ZombiesCasinoNet.cs)
                break;
            case (byte)Msg.ZCasinoAct when IsServer && r.Has(7):
                if (_clients.TryGetValue(sender.G, out var cc) && Players.TryGetValue(cc.Id, out var cp2))
                {
                    if (OnCasinoAct(cp2, ref r, now) is { } why)
                        ZFeedTo(cp2, why, Sfx.DryFire);
                }
                break;
            case (byte)Msg.ZSlotWin when !IsServer && r.Has(ZSlotWinBytes):
                ReadSlotWin(ref r); // a slot machine win: over the machine and in the feed (ZombiesSlotWin.cs)
                break;
            case (byte)Msg.ZEvent when !IsServer && r.Has(4):
            {
                var kind = (ZEventKind)r.U8();
                var arg = r.U16();
                var sound = (Sfx)r.U8();
                var text = r.Text(); // a feed line or banner: printable ASCII
                switch (kind)
                {
                    case ZEventKind.Feed:
                        Log(text, true);
                        if (sound < Sfx.COUNT)
                            Audio.Effect(sound, 0.6f, MixBus.Notify);
                        break;
                    case ZEventKind.Refill:
                        LocalRefill(arg);
                        break;
                    case ZEventKind.Banner:
                        ZBannerText = text;
                        ZBannerTime = now;
                        break;
                    case ZEventKind.MaxAmmo:
                        LocalMaxAmmo();
                        break;
                    case ZEventKind.Fx when r.Has(12):
                    {
                        var at = new Vector3(r.F32(), r.F32(), r.F32());
                        if (float.IsFinite(at.X + at.Y + at.Z))
                            LocalFx((ZFxKind)Math.Min((int)arg, (int)ZFxKind.COUNT - 1), at, sound, now);
                        break;
                    }
                }
                break;
            }
            case (byte)Msg.ZRun when !IsServer && r.Has(5):
                ReadZRun(ref r);
                break;
            case (byte)Msg.ZPickup when !IsServer:
                ReadWeaponDrop(ref r, now); // a dropped build appeared or went (ZombiesDrops.cs)
                break;
            case (byte)Msg.ZBuild when !IsServer && r.Has(6):
            {
                var who = r.U8();
                var slot = r.U8();
                var weapon = r.U8();
                var flags = r.U8();
                var text = ReadLong(ref r);
                if (!ApplyBuild(who, slot, weapon, flags, text))
                    _zPendingBuilds.Add((who, slot, weapon, flags, text));
                break;
            }
        }
    }

    /// <summary>Client: a build arrived for player <paramref name="who"/>. False if that player isn't known yet.</summary>
    private bool ApplyBuild(byte who, int slot, byte weapon, byte flags, string text)
    {
        if (!Players.TryGetValue(who, out var p))
            return false;
        if ((flags & ZBuildCleared) != 0)
        {
            ApplyCleared(p, slot, weapon); // the slot was taken apart at a workbench (protocol 41, ZombiesDisassemble.cs)
            return true;
        }
        WeaponBuild b;
        try
        {
            b = WeaponBuild.Parse(text, Parts.Find);
        }
        catch (FormatException)
        {
            return true; // garbage: drop it
        }
        var a = AssembleCached(b);
        if (slot == 4)
        {
            p.MeleeBuild = a.Melee;
            if (who == LocalId)
                ZP(p).Slots[4] = b;
            return true;
        }
        if (slot == GrenadeSlot)
        {
            p.GrenadeBuild = a.Grenade; // (the grenade kind and count come in Msg.ZWorld)
            if (who == LocalId)
                ZP(p).Slots[GrenadeSlot] = b;
            return true;
        }
        if (a.Def == null || slot is < 0 or > 3)
            return true;
        if (slot >= 2 && who != LocalId)
            p.ExtraSlots = true; // others' slots 3-4 (for drawing their guns)
        var def = PlaceBuilt(p, slot, a.Def, weapon);
        if (who == LocalId)
        {
            ZP(p).Slots[slot] = b;
            var start = (flags & 2) != 0;
            LocalSetSlot(slot, def, (flags & 1) != 0, start, select: !start || slot == 0);
        }
        return true;
    }

    private void ReadZEnts(ref PacketReader r, double now)
    {
        if (!r.Has(8))
            return;
        var tick = r.U32();
        var latest = tick >= _zEntsTick;
        if (latest)
            _zEntsTick = tick;
        ZEntsRead++;
        if (_zEntsReadAt > 0)
            ZEntsGapWorst = Math.Max(ZEntsGapWorst, now - _zEntsReadAt);
        _zEntsReadAt = now;
        int total = r.U16();
        int n = r.U16();
        for (var i = 0; i < n && r.Has(ZEntBytes - 1); i++)
        {
            var rawId = r.U16();
            var id = (ushort)(rawId & ~ZEntLimbFlag);
            var pos = new Vector3(DQ(r.U16(), 32), DQ(r.U16(), 64), DQ(r.U16(), 32));
            var yawByte = r.U8();
            var yaw = yawByte / 256f * MathF.Tau;
            var sb = r.U8();
            var health = r.U8();
            var state = (ZState)Math.Min(sb & 7, (int)ZState.Dead);
            var window = ZEntHasWindow(state) && r.Has(1) ? r.U8() : (byte)255;
            var limbs = (rawId & ZEntLimbFlag) != 0 && r.Has(1) ? (byte)(r.U8() & AllLimbs) : (byte)0; // (protocol 65)
            if (_zById.TryGetValue(id, out var known) && known.Snaps.Count > 0 && tick <= known.Snaps[^1].Tick)
                continue; // a late packet: this zombie already has newer news
            if (!latest && known == null)
                continue; // (a late packet doesn't bring back one the newer ones dropped)
            var fresh = false;
            if (!_zById.TryGetValue(id, out var z))
            {
                z = new Zombie { Id = id };
                z.Sim.Position = z.RenderPos = pos;
                z.RenderYaw = yaw;
                Horde.Add(z);
                _zById[id] = z;
                fresh = true;
            }
            if (limbs != z.Limbs)
            {
                z.Limbs = (byte)(z.Limbs | limbs); // (limbs never grow back: a late packet can't put one on again)
                ShowLimbs(z, z.RenderPos, z.RenderYaw, now, fresh); // the severing effect, once (ZombieLimbs.cs)
            }
            z.LastHeard = now;
            if ((sb & 8) != 0)
            {
                // The head bit (protocol 50): a fresh head hit, or a headshot kill.
                if (state == ZState.Dead)
                    z.KilledByHead = true;
                else if (tick - z.HeadHitTick > 4)
                    z.HeadHitTick = tick;
            }
            if (state == ZState.Dead && z.State != ZState.Dead)
            {
                z.DiedAt = now;
                Audio.Effect3D(Sfx.FleshHit3D, pos + new Vector3(0, 1.2f, 0), 0.8f, MixBus.Zombies);
            }
            z.State = state;
            z.Speed = (ZSpeed)Math.Min((sb >> 4) & 3, (int)ZSpeed.Sprint);
            z.FxBurning = (sb & 64) != 0;
            z.FxZapped = (sb & 128) != 0;
            z.HealthByte = health;
            z.Window = window == 255 ? -1 : window;
            if (ZAnimByteState(state, z.Window))
            {
                // The yaw byte is the animation byte (protocol 43): it faces its window.
                z.AnimByte = yawByte;
                z.AnimByteTick = tick;
                yaw = World.Map.Zombies is { } zwin && z.Window < zwin.Windows.Count ? zwin.Windows[z.Window].Yaw : z.Yaw;
            }
            ZombieAnimTrack(z, tick, health); // state, swipe, landing and hurt clocks on the server's ticks (ZombieAnimNet.cs)
            z.Sim.Position = pos;
            z.Yaw = yaw;
            z.Snaps.Add((tick, pos, yaw));
            if (z.Snaps.Count > 6)
                z.Snaps.RemoveAt(0);
        }
        if (latest && total == 0 && Horde.Count > 0)
        {
            Horde.Clear(); // the server has none left
            _zById.Clear();
        }
        if (latest)
            ZEntsLastTotal = total;
    }

    /// <summary>Client: the server's horde size in the last <see cref="Msg.ZEnts"/> (it may send fewer than that).</summary>
    public int ZEntsLastTotal;

    /// <summary>Client: <see cref="Msg.ZEnts"/> packets read, and the longest wait between two (self-test, logs).</summary>
    public int ZEntsRead;

    public double ZEntsGapWorst;
    private double _zEntsReadAt;

    /// <summary>Every zombie by id (both sides: the client's lookup for each entry, the server's ids in use).</summary>
    private readonly Dictionary<ushort, Zombie> _zById = new();

    /// <summary>Client: drops zombies the server stopped sending (gone, or long dead).</summary>
    private void DropStaleZombies(double now)
    {
        for (var i = Horde.Count - 1; i >= 0; i--)
        {
            var z = Horde[i];
            if (now - z.LastHeard > ZEntTimeout || (z.State == ZState.Dead && z.DiedAt >= 0 && now - z.DiedAt > 1.7))
            {
                Horde.RemoveAt(i);
                _zById.Remove(z.Id);
            }
        }
    }

    private uint _zEntsTick, _zWorldTick;

    private void ReadZWorld(ref PacketReader r, double now)
    {
        if (!r.Has(19))
            return;
        var tick = r.U32();
        if (tick < _zWorldTick)
            return;
        _zWorldTick = tick;
        ZRound = r.U16();
        ZPhase = (ZRoundPhase)Math.Min(r.U8(), (byte)ZRoundPhase.Break);
        ZPhaseLeft = r.F32();
        ZRemaining = r.U16();
        ZPower = r.U8() != 0;
        var doors = r.U32();
        if (World.Map.Zombies is { } zm)
            for (var d = 0; d < zm.Doors.Count; d++)
            {
                var open = (doors & (1u << d)) != 0;
                if (open != World.Map.IsDoorOpen(d))
                {
                    World.Map.SetDoorOpen(d, open);
                    if (open)
                    {
                        ZDoorOpenedAt[d] = now;
                        Audio.Effect3D(zm.Doors[d].Debris ? Sfx.BreakWood : Sfx.BreakMetal, zm.Doors[d].Box.Center, 1f, MixBus.Zombies);
                    }
                }
            }
        int nb = r.U8();
        if (ZBoards.Length != nb)
            ZBoards = new byte[nb];
        for (var i = 0; i < nb && r.Has(1); i++)
        {
            var b = r.U8();
            if (b < ZBoards[i] && World.Map.Zombies is { } zw && i < zw.Windows.Count)
                Audio.Effect3D(Sfx.BreakWood, zw.Windows[i].Opening.Center, 0.8f, MixBus.Zombies);
            ZBoards[i] = b;
        }
        if (!r.Has(9))
            return;
        BoxSpot = r.U8();
        BoxState = (ZBoxState)Math.Min(r.U8(), (byte)ZBoxState.Teddy);
        BoxLeft = r.F32();
        var bb = r.U16();
        BoxBuild = bb == 0xFFFF ? -1 : bb == 0xFFFE ? -2 : bb;
        BoxOwner = r.U8();
        if (!r.Has(1))
            return;
        int n = r.U8();
        for (var i = 0; i < n && r.Has(17); i++)
        {
            var id = r.U8();
            var zp = ZP(id);
            zp.Points = (int)r.U32();
            var flags = r.U8();
            zp.Downed = (flags & 1) != 0;
            zp.SelfReviveAt = (flags & 4) != 0 ? 1 : -1;
            if (id != LocalId || Brutal.GlfwApi.Glfw.Time - _zReadyPressedAt > 0.5) // (your own press shows at once; the server's copy catches up)
                zp.RoundReady = (flags & 8) != 0;
            zp.ReadyVoter = (flags & 16) != 0;
            if (Players.TryGetValue(id, out var p) && id != LocalId)
                p.ExtraSlots = (flags & 2) != 0;
            zp.PlateReady = (flags & 32) != 0;
            zp.Perks = r.U16();
            zp.BleedFraction = r.U8() / 255f;
            zp.BeingRevived = r.U8() / 255f;
            zp.Kills = r.U16();
            zp.Downs = r.U16();
            zp.Revives = r.U16();
            var grenade = r.U8();
            var grenades = r.U8();
            if (Players.TryGetValue(id, out var gp))
            {
                gp.Grenade = Enum.IsDefined((ThrowableKind)grenade) ? (ThrowableKind)grenade : ThrowableKind.None;
                gp.GrenadesLeft = grenades; // yours: SyncZombiesGrenades takes it over when it changes
            }
        }
        if (!r.Has(1))
            return;
        int nd = r.U8();
        ZDrops.Clear();
        for (var i = 0; i < nd && r.Has(10); i++)
        {
            var id = r.U16();
            var part = r.U16();
            var at = new Vector3(DQ(r.U16(), 32), DQ(r.U16(), 64), DQ(r.U16(), 32));
            ZDrops.Add(new ZDrop { Id = id, Part = part < Parts.All.Length ? Parts.All[part].Name : "?", At = at });
        }
        ReadPowerUps(ref r, now); // (ZombiesPowerUps.cs)
        ZWaitingReady = r.Has(1) && (r.U8() & 1) != 0; // (protocol 47)
        ReadSlots(ref r); // the slot machines (protocol 51, ZombiesSlots.cs)
    }

    // ---- Both sides, every frame ------------------------------------------------------------------------

    private ThrowableKind _zSyncGrenade = ThrowableKind.None;
    private int _zSyncGrenades = -1;

    /// <summary>
    /// Both sides, every frame in Zombies: the server's grenade count for you (your <see cref="Player"/>: the server's
    /// own on the host, from <see cref="Msg.ZWorld"/> on a client) becomes the HUD's and the grenade key's whenever it
    /// changes, so a throw counts down at once (<see cref="TryThrow"/>) and a round's refill shows up; and a second
    /// after your last throw it wins anyway (a throw the server refused gives the grenade back).
    /// </summary>
    private void SyncZombiesGrenades(double now)
    {
        if (Local is not { } me)
            return;
        var changed = me.Grenade != _zSyncGrenade || me.GrenadesLeft != _zSyncGrenades;
        if (!changed && (now < _nextThrow + 0.5 || (LocalGrenade == me.Grenade && GrenadesLeft == me.GrenadesLeft)))
            return;
        _zSyncGrenade = me.Grenade;
        _zSyncGrenades = me.GrenadesLeft;
        LocalGrenade = me.Grenade;
        GrenadesLeft = me.GrenadesLeft;
    }

    /// <summary>Where every zombie is drawn this frame (the host blends ticks; clients interpolate snapshots like players), the points popups, late builds.</summary>
    private void UpdateZombiesFrame(double now, float dt)
    {
        if (Mode != GameMode.Zombies)
        {
            if (World.Map.Zombies is { } zmap && zmap.Doors.Count > 0 && !World.Map.IsDoorOpen(0))
                World.Map.OpenAllDoors(); // a Zombies map in another mode: one big map (clients learn the mode after joining)
            return;
        }
        if (!IsServer)
            DropStaleZombies(now);
        SyncZombiesGrenades(now);
        foreach (var z in Horde)
        {
            if (IsServer)
            {
                z.RenderPos = Vector3.Lerp(z.PrevPos, z.Position, _alpha);
                z.RenderYaw = z.Yaw;
            }
            else if (z.Snaps.Count > 0)
            {
                var snaps = z.Snaps;
                var rt = RenderTick;
                if (rt <= snaps[0].Tick)
                    (z.RenderPos, z.RenderYaw) = (snaps[0].Pos, snaps[0].Yaw);
                else if (rt >= snaps[^1].Tick)
                {
                    // Past its last update (a far zombie of a big horde is sent a few times a second): carry on at its
                    // last speed for up to a quarter of a second rather than stopping and jumping.
                    var (t1, p1, y1) = snaps[^1];
                    z.RenderPos = p1;
                    z.RenderYaw = y1;
                    if (snaps.Count >= 2 && z.State is ZState.Chase or ZState.ToWindow)
                    {
                        var (s0, p0, _) = snaps[^2];
                        var span = t1 - s0;
                        if (span > 0 && span <= 30)
                            z.RenderPos = p1 + (p1 - p0) * (float)(Math.Min(rt - t1, 15) / span);
                    }
                }
                else
                    for (var i = 0; i < snaps.Count - 1; i++)
                    {
                        if (rt < snaps[i].Tick || rt > snaps[i + 1].Tick)
                            continue;
                        var t = (float)((rt - snaps[i].Tick) / Math.Max(1, snaps[i + 1].Tick - snaps[i].Tick));
                        z.RenderPos = Vector3.Lerp(snaps[i].Pos, snaps[i + 1].Pos, t);
                        z.RenderYaw = World.LerpAngle(snaps[i].Yaw, snaps[i + 1].Yaw, t);
                        break;
                    }
            }
            ZombieAnimFrame(z, dt); // drawn speed and the walk cycle (ZombieAnimNet.cs)
        }
        ZSounds.Frame(Audio, Horde, dt); // their voices and footsteps, from what this side knows (Source/Audio/ZombieSounds.cs)
        if (Local is { } me)
        {
            var zp = ZP(me);
            if (zp.ShownPoints != zp.Points)
            {
                if (zp.ShownPoints != int.MinValue)
                    ZPopups.Add((Brutal.GlfwApi.Glfw.Time, zp.Points - zp.ShownPoints));
                zp.ShownPoints = zp.Points;
            }
        }
        var t0 = Brutal.GlfwApi.Glfw.Time;
        for (var i = ZPopups.Count - 1; i >= 0; i--) // (a plain loop: RemoveAll with a lambda would allocate a closure every frame)
            if (t0 - ZPopups[i].Time > 1.6)
                ZPopups.RemoveAt(i);
        while (ZPopups.Count > 8)
            ZPopups.RemoveAt(0);
        for (var i = _zPendingBuilds.Count - 1; i >= 0; i--)
        {
            var (who, slot, weapon, flags, text) = _zPendingBuilds[i];
            if (ApplyBuild(who, slot, weapon, flags, text))
                _zPendingBuilds.RemoveAt(i);
        }
        if (_zPendingBuilds.Count > 64)
            _zPendingBuilds.RemoveRange(0, _zPendingBuilds.Count - 64);
    }

    // ---- Self-test hooks ----------------------------------------------------------------------------------

    /// <summary>Self-test: a client's copy reads the server's zombies and run state (in-process; no network).</summary>
    public void SelfTestReadZombies(ReadOnlySpan<byte> ents, ReadOnlySpan<byte> world)
    {
        var r = new PacketReader(ents[1..]);
        ReadZEnts(ref r, 0);
        var r2 = new PacketReader(world[1..]);
        ReadZWorld(ref r2, 0);
    }

    /// <summary>Self-test: a client's copy reads one <see cref="Msg.ZEnts"/> (the message id included) at local time <paramref name="now"/>.</summary>
    public void SelfTestReadEnts(ReadOnlySpan<byte> ents, double now)
    {
        var r = new PacketReader(ents[1..]);
        ReadZEnts(ref r, now);
    }

    /// <summary>Self-test: a client's copy applies a build message.</summary>
    public bool SelfTestApplyBuild(ReadOnlySpan<byte> msg)
    {
        var r = new PacketReader(msg[1..]);
        var who = r.U8();
        var slot = r.U8();
        var weapon = r.U8();
        var flags = r.U8();
        return ApplyBuild(who, slot, weapon, flags, ReadLong(ref r));
    }

    /// <summary>Self-test: the build message for player <paramref name="p"/>'s slot.</summary>
    public static void SelfTestWriteBuild(PacketWriter w, Player p, int slot, WeaponBuild b) =>
        WriteBuild(w, p.Id, slot, slot switch { 0 => p.Primary, 1 => p.Secondary, 2 => p.BuiltExtra[0]?.Id ?? 0, 3 => p.BuiltExtra[1]?.Id ?? 0, _ => 0 }, 1, b.ToText());
}
