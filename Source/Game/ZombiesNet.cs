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
}

/// <summary>
/// Zombies networking (protocol 18; the messages are at the end of <see cref="Msg"/>): the zombies (<see cref="Msg.ZEnts"/>,
/// with every snapshot), the run state (<see cref="Msg.ZWorld"/>, 10 times a second: round, doors, boards, the box,
/// every player's points, downs and perks, parts on the floor), events (<see cref="Msg.ZEvent"/>), a player's parts and
/// scrap (<see cref="Msg.ZRun"/>), built weapons (<see cref="Msg.ZBuild"/>: the build's text, so every PC assembles the
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
                    for (var s = 0; s < 5; s++)
                        if (zp.Slots[s] is { } b)
                        {
                            var w = s switch { 0 => p.Primary, 1 => p.Secondary, 2 => p.BuiltExtra[0]?.Id ?? 0, 3 => p.BuiltExtra[1]?.Id ?? 0, _ => (byte)0 };
                            WriteBuild(_w, p.Id, s, w, 0, b.ToText());
                            Net.SendTo(guid, _w.Span, PacketReliability.ReliableOrdered);
                        }
            if (Players.TryGetValue(id, out var me))
                SendZRun(me);
            SendWeaponDropsTo(guid, now); // the builds on the floor (ZombiesDrops.cs)
        }
        if (ServerTick % World.SnapshotEvery == 0 && (Horde.Count > 0 || _zEntsLive))
        {
            WriteZEnts(_w);
            Net.Broadcast(_w.Span, PacketReliability.UnreliableSequenced);
            _zEntsLive = Horde.Count > 0;
        }
        if (ServerTick % (World.SnapshotEvery * 3) == 0)
        {
            WriteZWorld(_w);
            Net.Broadcast(_w.Span, PacketReliability.Unreliable);
        }
    }

    /// <summary>Bytes per zombie in <see cref="Msg.ZEnts"/>.</summary>
    public const int ZEntBytes = 12;

    /// <summary>Every zombie: id, position (quantised: 3 cm across, 1.6 cm up), yaw, state and speed, health and window.</summary>
    public void WriteZEnts(PacketWriter w)
    {
        var n = Math.Min(Horde.Count, 90);
        w.Begin(Msg.ZEnts).U32(ServerTick).U8((byte)n);
        for (var i = 0; i < n; i++)
        {
            var z = Horde[i];
            var p = z.Position;
            w.U16(z.Id).U16(Q(p.X, 32)).U16(Q(p.Y, 64)).U16(Q(p.Z, 32))
                .U8((byte)((int)MathF.Round(World.WrapAngle(z.Yaw) / MathF.Tau * 256f) & 255))
                .U8((byte)((byte)z.State | ((byte)z.Speed << 4)))
                .U8((byte)Math.Clamp(z.MaxHealth > 0 ? z.Health * 255 / z.MaxHealth : 0, 0, 255))
                .U8(z.Window is >= 0 and < 255 ? (byte)z.Window : (byte)255);
        }
    }

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
            w.U8(p.Id).U32((uint)Math.Max(0, zp.Points)).U8((byte)((zp.Downed ? 1 : 0) | (p.ExtraSlots ? 2 : 0) | (zp.SelfReviveAt > 0 ? 4 : 0))).U8(zp.Perks)
                .U8((byte)(Math.Clamp(zp.BleedFraction, 0, 1) * 255)).U8((byte)(Math.Clamp(zp.BeingRevived, 0, 1) * 255))
                .U16((ushort)Math.Min(zp.Kills, 65535)).U16((ushort)Math.Min(zp.Downs, 65535)).U16((ushort)Math.Min(zp.Revives, 65535));
        }
        var drops = ZDrops.Take(32).ToList();
        w.U8((byte)drops.Count);
        foreach (var d in drops)
        {
            var idx = Array.FindIndex(Parts.All, x => x.Name == d.Part);
            w.U16(d.Id).U16((ushort)Math.Max(0, idx)).U16(Q(d.At.X, 32)).U16(Q(d.At.Y, 64)).U16(Q(d.At.Z, 32));
        }
    }

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

    /// <summary>Server: <paramref name="p"/>'s loose parts and scrap (to that player).</summary>
    private void SendZRun(Player p)
    {
        if (p.Id == LocalId || p.Bot != null || Net.IsOffline || GuidOf(p) is not { } g)
            return;
        var zp = ZP(p);
        _w.Begin(Msg.ZRun).U32((uint)zp.Scrap).U8((byte)Math.Min(zp.Parts.Count, 200));
        foreach (var (name, n) in zp.Parts.Take(200))
            _w.Str(name).U8((byte)Math.Min(n, 255));
        Net.SendTo(g, _w.Span, PacketReliability.ReliableOrdered);
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
            Audio.Effect(sound, 0.6f);
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
        var bytes = Encoding.UTF8.GetBytes(text);
        var n = Math.Min(bytes.Length, 6000);
        _w.Begin(Msg.ZBench).U8((byte)kind).U8((byte)slot).U16((ushort)n).Bytes(bytes.AsSpan(0, n));
        Net.SendToServer(_w.Span, PacketReliability.ReliableOrdered);
    }

    // ---- Packets ------------------------------------------------------------------------------------------

    private void OnZombiesPacket(byte id, ref PacketReader r, RakNetGUID sender, double now)
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
                    var kind = r.U8();
                    var slot = r.U8();
                    var text = ReadLong(ref r);
                    if (ServerBench(bp, kind, slot, text) is { } why)
                        ZFeedTo(bp, $"Workbench: {why}", Sfx.DryFire);
                }
                break;
            case (byte)Msg.ZEnts when !IsServer:
                ReadZEnts(ref r, now);
                break;
            case (byte)Msg.ZWorld when !IsServer:
                ReadZWorld(ref r, now);
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
                            Audio.Effect(sound, 0.6f);
                        break;
                    case ZEventKind.Refill:
                        LocalRefill(arg);
                        break;
                    case ZEventKind.Banner:
                        ZBannerText = text;
                        ZBannerTime = now;
                        break;
                }
                break;
            }
            case (byte)Msg.ZRun when !IsServer && r.Has(5):
            {
                var zp = ZP(LocalId);
                zp.Scrap = (int)r.U32();
                int n = r.U8();
                zp.Parts.Clear();
                for (var i = 0; i < n && r.Has(2); i++)
                {
                    var name = r.Str();
                    var count = r.Has(1) ? r.U8() : 0;
                    if (count > 0)
                        zp.Parts[name] = count;
                }
                break;
            }
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
        if (!r.Has(5))
            return;
        var tick = r.U32();
        if (tick < _zEntsTick)
            return;
        _zEntsTick = tick;
        int n = r.U8();
        _zSeen.Clear();
        for (var i = 0; i < n && r.Has(ZEntBytes); i++)
        {
            var id = r.U16();
            var pos = new Vector3(DQ(r.U16(), 32), DQ(r.U16(), 64), DQ(r.U16(), 32));
            var yaw = r.U8() / 256f * MathF.Tau;
            var sb = r.U8();
            var health = r.U8();
            var window = r.U8();
            _zSeen.Add(id);
            var z = Horde.FirstOrDefault(x => x.Id == id);
            if (z == null)
            {
                z = new Zombie { Id = id };
                z.Sim.Position = z.RenderPos = pos;
                z.RenderYaw = yaw;
                Horde.Add(z);
            }
            var state = (ZState)Math.Min(sb & 15, (int)ZState.Dead);
            if (state == ZState.Dead && z.State != ZState.Dead)
            {
                z.DiedAt = now;
                Audio.Effect3D(Sfx.FleshHit3D, pos + new Vector3(0, 1.2f, 0), 0.8f);
            }
            z.State = state;
            z.Speed = (ZSpeed)Math.Min(sb >> 4, (int)ZSpeed.Sprint);
            z.HealthByte = health;
            z.Window = window == 255 ? -1 : window;
            z.Sim.Position = pos;
            z.Yaw = yaw;
            z.Snaps.Add((tick, pos, yaw));
            if (z.Snaps.Count > 6)
                z.Snaps.RemoveAt(0);
        }
        Horde.RemoveAll(z => !_zSeen.Contains(z.Id));
    }

    private uint _zEntsTick, _zWorldTick;
    private readonly HashSet<ushort> _zSeen = new();

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
                        Audio.Effect3D(zm.Doors[d].Debris ? Sfx.BreakWood : Sfx.BreakMetal, zm.Doors[d].Box.Center, 1f);
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
                Audio.Effect3D(Sfx.BreakWood, zw.Windows[i].Opening.Center, 0.8f);
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
        for (var i = 0; i < n && r.Has(15); i++)
        {
            var id = r.U8();
            var zp = ZP(id);
            zp.Points = (int)r.U32();
            var flags = r.U8();
            zp.Downed = (flags & 1) != 0;
            zp.SelfReviveAt = (flags & 4) != 0 ? 1 : -1;
            if (Players.TryGetValue(id, out var p) && id != LocalId)
                p.ExtraSlots = (flags & 2) != 0;
            zp.Perks = r.U8();
            zp.BleedFraction = r.U8() / 255f;
            zp.BeingRevived = r.U8() / 255f;
            zp.Kills = r.U16();
            zp.Downs = r.U16();
            zp.Revives = r.U16();
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
    }

    // ---- Both sides, every frame ------------------------------------------------------------------------

    /// <summary>Where every zombie is drawn this frame (the host blends ticks; clients interpolate snapshots like players), the points popups, late builds.</summary>
    private void UpdateZombiesFrame(float dt)
    {
        if (Mode != GameMode.Zombies)
        {
            if (World.Map.Zombies is { } zmap && zmap.Doors.Count > 0 && !World.Map.IsDoorOpen(0))
                World.Map.OpenAllDoors(); // a Zombies map in another mode: one big map (clients learn the mode after joining)
            return;
        }
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
                    (z.RenderPos, z.RenderYaw) = (snaps[^1].Pos, snaps[^1].Yaw);
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
            if (z.State is ZState.ToWindow or ZState.Chase)
                z.Gait += dt * (z.Speed == ZSpeed.Walk ? 4.5f : z.Speed == ZSpeed.Run ? 8f : 11f);
        }
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
        ZPopups.RemoveAll(x => t0 - x.Time > 1.6);
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
