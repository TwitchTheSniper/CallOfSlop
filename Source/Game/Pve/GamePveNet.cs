using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>One-off co-op events (<see cref="Msg.PEvent"/>).</summary>
public enum PveEventKind : byte
{
    /// <summary>A big centre line (OBJECTIVE 2/4: ..., SQUAD WIPED).</summary>
    Banner,

    /// <summary>You killed an enemy (arg: bit 0 headshot, bits 1-8 the weapon id): co-op XP.</summary>
    Kill,

    /// <summary>An objective was completed (arg: XP).</summary>
    Objective,

    /// <summary>The mission was completed (arg: XP).</summary>
    Mission,

    /// <summary>Every enemy is gone (a wipe or a restart).</summary>
    Clear,
}

/// <summary>
/// Co-op networking (protocol 37; docs/pve/DESIGN.md "Network and performance budget"): the enemies near each client in
/// <see cref="Msg.PEnts"/> (20 Hz, interest-managed: the nearest within 150 m, the far half every other packet, 13 bytes
/// each, one datagram), events in <see cref="Msg.PEvent"/>. The objective state rides in the mode-state tail
/// (CoopModule). Clients interpolate enemies between their last two updates and drop one not heard of for 1.2 s.
/// </summary>
public sealed partial class Game
{
    /// <summary>Bytes per enemy in <see cref="Msg.PEnts"/>.</summary>
    public const int PEntBytes = 13;

    /// <summary>The most enemies in one <see cref="Msg.PEnts"/> (6 + 56 x 13 = 734 bytes, well under one datagram).</summary>
    public const int PEntsMax = 56;

    /// <summary>Enemies within this distance of a client go in every packet; up to <see cref="PveInterestRange"/> in every other one.</summary>
    public const float PveNearRange = 45f, PveInterestRange = 150f;

    /// <summary>Send enemies every this many ticks (60 / 3 = 20 Hz).</summary>
    public const int PEntsEvery = 3;

    private readonly float[] _pveKeys = new float[PveMaxAlive + 8];
    private readonly int[] _pveIdx = new int[PveMaxAlive + 8];
    private int _pveSendCount;

    /// <summary>Bytes of <see cref="Msg.PEnts"/> sent this match, and how many packets (self-test, logs).</summary>
    public long PveBytesSent, PvePacketsSent;

    private void ServerSendPve(double now)
    {
        if (Net.IsOffline || ServerTick % PEntsEvery != 0)
            return;
        _pveSendCount++;
        foreach (var (guid, id) in _clients.Values)
        {
            if (!Players.TryGetValue(id, out var p))
                continue;
            WritePEnts(_w, p.Position, _pveSendCount, now);
            Net.SendTo(guid, _w.Span, PacketReliability.UnreliableSequenced);
            PveBytesSent += _w.Length;
            PvePacketsSent++;
        }
    }

    /// <summary>
    /// The enemies a client at <paramref name="viewer"/> gets this time: the living within <see cref="PveNearRange"/>
    /// always, those out to <see cref="PveInterestRange"/> on even packets (<paramref name="sendIndex"/>), the dead for
    /// 1.5 s while close; the nearest <see cref="PEntsMax"/> of them.
    /// </summary>
    public void WritePEnts(PacketWriter w, Vector3 viewer, int sendIndex, double now)
    {
        var n = 0;
        for (var i = 0; i < Enemies.Count && n < _pveKeys.Length; i++)
        {
            var e = Enemies[i];
            var d = Vector3.Distance(e.Position, viewer);
            if (d > PveInterestRange || (!e.Alive && (d > 60f || now - e.DiedAt > 1.5)))
                continue;
            if (d > PveNearRange && (sendIndex & 1) != 0 && e.Alive)
                continue;
            _pveKeys[n] = d;
            _pveIdx[n] = i;
            n++;
        }
        if (n > PEntsMax)
            Array.Sort(_pveKeys, _pveIdx, 0, n);
        n = Math.Min(n, PEntsMax);
        w.Begin(Msg.PEnts).U32(ServerTick).U8((byte)n);
        for (var k = 0; k < n; k++)
        {
            var e = Enemies[_pveIdx[k]];
            var p = e.Position;
            var pitch = (sbyte)Math.Clamp((int)MathF.Round(e.Pitch / (MathF.PI / 2) * 127f), -127, 127);
            w.U16(e.Id).U16(Q(p.X, 32)).U16(Q(p.Y, 64)).U16(Q(p.Z, 32))
                .U8((byte)((int)MathF.Round(World.WrapAngle(e.Yaw) / MathF.Tau * 256f) & 255))
                .U8((byte)pitch)
                .U8((byte)((byte)e.State | ((byte)e.Arch << 4)))
                .U8((byte)Math.Clamp(e.MaxHealth > 0 ? e.Health * 255 / e.MaxHealth : 0, 0, 255))
                .U8((byte)((e.ShotSeq & 15) | (e.Crouched ? 16 : 0) | (e.Named ? 32 : 0) | (e.Moving ? 64 : 0)));
        }
    }

    /// <summary>Server: an event for everyone (<paramref name="toClientsOnly"/>: the host already handled it).</summary>
    private void SendPveEvent(PveEventKind kind, ushort arg, byte extra, string text, bool toClientsOnly = false)
    {
        if (Net.IsOffline)
            return;
        _w.Begin(Msg.PEvent).U8((byte)kind).U16(arg).U8(extra).Str(text.Length > 100 ? text[..100] : text);
        Net.Broadcast(_w.Span, PacketReliability.ReliableOrdered);
    }

    private void SendPveEventTo(Player p, PveEventKind kind, ushort arg, string text)
    {
        if (Net.IsOffline || p.Bot != null || GuidOf(p) is not { } g)
            return;
        _w.Begin(Msg.PEvent).U8((byte)kind).U16(arg).U8(0).Str(text);
        Net.SendTo(g, _w.Span, PacketReliability.ReliableOrdered);
    }

    // ---- Client ---------------------------------------------------------------------------------------------

    private readonly Dictionary<ushort, PveEnemy> _pveById = new();
    private uint _pEntsTick;

    internal void OnPvePacket(byte id, ref PacketReader r, double now)
    {
        switch (id)
        {
            case (byte)Msg.PEnts when !IsServer:
                ReadPEnts(ref r, now);
                break;
            case (byte)Msg.PEvent when !IsServer && r.Has(4):
            {
                var kind = (PveEventKind)r.U8();
                var arg = r.U16();
                r.U8();
                var text = r.Has(1) ? r.Text(100) : "";
                switch (kind)
                {
                    case PveEventKind.Banner:
                        CoopBannerText = text;
                        CoopBannerTime = now;
                        break;
                    case PveEventKind.Clear:
                        ClearEnemies();
                        break;
                    default:
                        CoopLocalAward(kind, arg, now);
                        break;
                }
                break;
            }
        }
    }

    /// <summary>Client: an enemy update (stale ones ignored). Self-test: also the replication round trip.</summary>
    public void ReadPEnts(ref PacketReader r, double now)
    {
        if (!r.Has(5))
            return;
        var tick = r.U32();
        if (tick < _pEntsTick)
            return;
        _pEntsTick = tick;
        int n = r.U8();
        for (var i = 0; i < n && r.Has(PEntBytes); i++)
        {
            var id = r.U16();
            var pos = new Vector3(DQ(r.U16(), 32), DQ(r.U16(), 64), DQ(r.U16(), 32));
            var yaw = r.U8() / 256f * MathF.Tau;
            var pitch = (sbyte)r.U8() / 127f * (MathF.PI / 2);
            var sa = r.U8();
            var health = r.U8();
            var flags = r.U8();
            if (!_pveById.TryGetValue(id, out var e))
            {
                if (TakeEnemy() is not { } fresh)
                    continue;
                e = fresh;
                e.Id = id;
                e.Sim.Position = e.RenderPos = e.SnapPosA = e.SnapPosB = pos;
                e.RenderYaw = e.SnapYawA = e.SnapYawB = yaw;
                e.SnapTickA = e.SnapTickB = tick;
                e.ShotSeq = (byte)(flags & 15);
                e.State = PveState.Advance;
                Enemies.Add(e);
                _pveById[id] = e;
            }
            var state = (PveState)Math.Min(sa & 15, (int)PveState.Dead);
            var arch = (PveArch)Math.Min(sa >> 4, PveArchetypes.All.Length - 1);
            if (state == PveState.Dead && e.State != PveState.Dead)
                e.DiedAt = now;
            e.State = state;
            e.Arch = arch;
            e.Def = PveArchetypes.Get(arch);
            e.HealthByte = health;
            e.Crouched = (flags & 16) != 0;
            e.Named = (flags & 32) != 0;
            e.Moving = (flags & 64) != 0;
            e.Sim.Position = pos;
            e.Yaw = yaw;
            e.Pitch = pitch;
            e.LastUpdate = now;
            e.SnapTickA = e.SnapTickB;
            e.SnapPosA = e.SnapPosB;
            e.SnapYawA = e.SnapYawB;
            e.SnapTickB = tick;
            e.SnapPosB = pos;
            e.SnapYawB = yaw;
            // New shots since the last update: a tracer and a gunshot each (up to 3).
            var shots = ((flags & 15) - e.ShotSeq) & 15;
            e.ShotSeq = (byte)(flags & 15);
            if (shots > 0 && state != PveState.Dead && Local is { } me && Vector3.DistanceSquared(me.RenderPosition, pos) < 140f * 140f)
            {
                var eye = pos + new Vector3(0, e.Crouched ? 1.05f : 1.55f, 0);
                var dir = World.Forward(yaw, pitch);
                for (var k = 0; k < Math.Min(shots, 3); k++)
                {
                    var d = Vector3.Normalize(dir + new Vector3(MathF.Sin(k * 2.3f + tick), MathF.Cos(k * 1.7f + tick), MathF.Sin(k * 3.1f)) * e.Def.Spread);
                    var len = World.Query.Raycast(eye, d, World.Range, QueryFilter.Shots);
                    PveShotFx(e, eye, eye + d * len, now);
                }
            }
        }
    }

    /// <summary>Both sides, every frame: where enemies are drawn (the host blends ticks; clients interpolate their last two updates), and stale ones go.</summary>
    private void UpdatePveFrame(double now, float dt)
    {
        if (Enemies.Count == 0)
            return;
        var rt = RenderTick;
        for (var i = Enemies.Count - 1; i >= 0; i--)
        {
            var e = Enemies[i];
            if (IsServer)
            {
                e.RenderPos = Vector3.Lerp(e.PrevPos, e.Position, _alpha);
                e.RenderYaw = e.Yaw;
                e.RenderPitch = e.Pitch;
            }
            else
            {
                if (now - e.LastUpdate > 1.2)
                {
                    Enemies.RemoveAt(i);
                    _pveById.Remove(e.Id);
                    ReleaseEnemy(e);
                    continue;
                }
                var span = e.SnapTickB - e.SnapTickA;
                var t = span > 0 ? (float)Math.Clamp((rt - e.SnapTickA) / span, 0, 1.5) : 1f;
                e.RenderPos = t <= 1f ? Vector3.Lerp(e.SnapPosA, e.SnapPosB, t) : e.SnapPosB + (e.SnapPosB - e.SnapPosA) * MathF.Min(t - 1f, 0.5f);
                e.RenderYaw = World.LerpAngle(e.SnapYawA, e.SnapYawB, MathF.Min(t, 1f));
                e.RenderPitch = e.Pitch;
            }
            if (e.Moving && e.Alive)
                e.Gait += dt * (e.Def.Run > 5f && e.State is PveState.Rush or PveState.Cover ? 10f : 7f);
        }
    }

    /// <summary>Self-test: the enemy packet a client at <paramref name="viewer"/> would get.</summary>
    public byte[] SelfTestPEnts(Vector3 viewer, int sendIndex, double now)
    {
        var w = new PacketWriter();
        WritePEnts(w, viewer, sendIndex, now);
        return w.Span.ToArray();
    }

    /// <summary>Self-test: a client reads an enemy packet (the message id included).</summary>
    public void SelfTestReadPEnts(byte[] packet, double now)
    {
        var r = new PacketReader(packet.AsSpan(1));
        ReadPEnts(ref r, now);
    }
}
