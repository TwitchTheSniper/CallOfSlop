using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// Voice chat over the network (protocol 36). A talker's client sends each 20 ms frame to the server
/// (<see cref="Msg.VoiceUp"/>, unreliable: a late voice frame is useless, and the jitter buffer reorders and conceals).
/// The server rate-limits each talker (<see cref="VoiceRules.TalkRate"/>) and each listener
/// (<see cref="VoiceRules.ListenRate"/>), works out who hears the frame and how (<see cref="VoiceRules.Route"/>: proximity
/// range, team radio, the dead), and relays it only to them (<see cref="Msg.VoiceDown"/>), never back to the talker and never
/// to bots. Voice uses plain unreliable packets with its own sequence numbers rather than RakNet's unreliable-sequenced
/// channel: sequencing on one channel would make frames from different talkers (and the game's own sequenced snapshots on
/// channel 0) drop each other, and would throw away late-but-usable frames the jitter buffer can still place.
/// The host's "Allow voice chat" (<see cref="VoiceAllowed"/>) turns relaying off; clients learn it from
/// <see cref="Msg.VoiceInfo"/>. Each client tells the server whether it listens at all (<see cref="Msg.VoiceCtl"/>), so a
/// player with voice off costs no bandwidth. No allocations per frame.
/// </summary>
public sealed partial class Game
{
    /// <summary>Everyone this player hears (jitter buffers by talker id).</summary>
    public readonly VoiceHub Voice = new();

    /// <summary>Host: "Allow voice chat" (relay voice at all). Client: what the host said.</summary>
    public bool VoiceAllowed = true;

    /// <summary>This player listens to voice (Settings > Audio "Voice chat" not Off, and not "Mute all voice").</summary>
    public bool VoiceListen { get; private set; } = true;

    /// <summary>People you don't want to hear (by name; Settings remembers them, <see cref="LoadVoiceMutes"/>).</summary>
    public readonly HashSet<string> MutedVoice = new(StringComparer.Ordinal);

    /// <summary>When the local player last sent a voice frame (the HUD's mic icon, the scoreboard).</summary>
    public double LocalVoiceAt = double.NegativeInfinity;

    private readonly PacketWriter _vw = new();
    private readonly bool[] _voiceDeaf = new bool[256];
    private readonly double[] _talkTokens = new double[256], _talkLast = NewVoiceClock(), _listenTokens = new double[256], _listenLast = NewVoiceClock();

    /// <summary>Server statistics: frames in from talkers, relayed (one per listener), dropped by the rate limits; bytes relayed.</summary>
    public long VoiceFramesUp, VoiceFramesRelayed, VoiceRateDropped, VoiceBytesRelayed;

    /// <summary>Server: frames relayed to each player id (self-test).</summary>
    public readonly int[] VoiceRelayedTo = new int[256];

    private static double[] NewVoiceClock()
    {
        var a = new double[256];
        Array.Fill(a, -1e9); // a full bucket the first time
        return a;
    }

    /// <summary>Is <paramref name="p"/> talking (the speaker icon)? You: while you transmit.</summary>
    public bool IsTalking(Player p, double now) =>
        p.Id == LocalId ? now - LocalVoiceAt < VoiceRules.TalkingHold : Voice.Talking(p.Id, now);

    /// <summary>A short tag after a name on the scoreboard: talking, or muted by you.</summary>
    public string VoiceTag(Player p, double now) =>
        p.Id != LocalId && (MutedVoice.Contains(p.Name) || MutedChat.Contains(p.Name)) ? "  (muted)"
        : IsTalking(p, now) ? "  <))" : "";

    /// <summary>Mutes or unmutes someone's voice (their buffered audio goes at once).</summary>
    public void SetVoiceMuted(Player p, bool muted)
    {
        if (muted)
        {
            MutedVoice.Add(p.Name);
            Voice.Clear(p.Id);
        }
        else
            MutedVoice.Remove(p.Name);
    }

    /// <summary>Turns hearing voice on or off (tells the server, so it stops sending).</summary>
    public void SetVoiceListen(bool listen)
    {
        if (listen == VoiceListen)
            return;
        VoiceListen = listen;
        if (!listen)
            Voice.ClearAll();
        SendVoiceCtl();
    }

    private void SendVoiceCtl()
    {
        if (IsServer || Net.IsOffline || LocalId == byte.MaxValue)
            return;
        _vw.Begin(Msg.VoiceCtl).U8(VoiceListen ? (byte)1 : (byte)0);
        Net.SendToServer(_vw.Span, PacketReliability.ReliableOrdered);
    }

    /// <summary>Server: tells one client the host's voice settings.</summary>
    private void SendVoiceInfo(RakNetGUID to)
    {
        if (!IsServer || Net.IsOffline)
            return;
        _vw.Begin(Msg.VoiceInfo).U8(VoiceAllowed ? (byte)1 : (byte)0);
        Net.SendTo(to, _vw.Span, PacketReliability.ReliableOrdered);
    }

    /// <summary>
    /// Sends the local player's voice frame (<paramref name="s"/>.Frame, just encoded). <paramref name="teamRadio"/>: only
    /// teammates hear it. Returns false when the host doesn't allow voice or there is no player yet.
    /// </summary>
    public bool SendVoice(VoiceSender s, bool teamRadio, double now) => SendVoice(s.Frame, s.Seq, teamRadio, now);

    /// <summary><see cref="SendVoice(VoiceSender, bool, double)"/> with the frame and its sequence number.</summary>
    public bool SendVoice(ReadOnlySpan<byte> frame, ushort seq, bool teamRadio, double now)
    {
        if (!VoiceAllowed || Local is not { } me || frame.Length != VoiceCodec.FrameBytes)
            return false;
        LocalVoiceAt = now;
        var flags = teamRadio ? VoiceRules.FlagTeam : (byte)0;
        if (IsServer)
        {
            ServerVoice(me, seq, flags, frame, now);
            return true;
        }
        _vw.Begin(Msg.VoiceUp).U16(seq).U8(flags).Bytes(frame);
        Net.SendToServer(_vw.Span, PacketReliability.Unreliable);
        return true;
    }

    private static VoiceParty PartyOf(Player p) => new(p.Eye, p.Team, p.Alive);

    /// <summary>Server: relays <paramref name="from"/>'s frame to everyone who should hear it (and plays it for the host if so).</summary>
    private void ServerVoice(Player from, ushort seq, byte flags, ReadOnlySpan<byte> frame, double now)
    {
        if (!VoiceAllowed || from.Bot != null)
            return;
        if (!VoiceRules.Allow(ref _talkTokens[from.Id], ref _talkLast[from.Id], now, VoiceRules.TalkBurst, VoiceRules.TalkRate))
        {
            VoiceRateDropped++;
            return; // more than a microphone makes: dropped
        }
        VoiceFramesUp++;
        var team = (flags & VoiceRules.FlagTeam) != 0;
        var talker = PartyOf(from);
        var teamMode = IsTeamMode;
        var coOp = Module.CoOp;

        // The host listening.
        if (Local is { } me && me != from && VoiceListen)
        {
            var route = VoiceRules.Route(talker, PartyOf(me), team, Phase, teamMode, coOp);
            if (route != VoiceRoute.None)
            {
                VoiceRelayedTo[me.Id]++;
                ReceiveVoice(from.Id, seq, route, frame, now);
            }
        }
        if (Net.IsOffline)
            return;

        _vw.Begin(Msg.VoiceDown).U8(from.Id).U16(seq).U8(0).Bytes(frame);
        const int routeAt = 4; // [id][talker][seq lo][seq hi][route]
        foreach (var c in _clients.Values)
        {
            if (c.Id == from.Id || _voiceDeaf[c.Id] || !Players.TryGetValue(c.Id, out var listener))
                continue;
            var route = VoiceRules.Route(talker, PartyOf(listener), team, Phase, teamMode, coOp);
            if (route == VoiceRoute.None)
                continue;
            if (!VoiceRules.Allow(ref _listenTokens[c.Id], ref _listenLast[c.Id], now, VoiceRules.ListenBurst, VoiceRules.ListenRate))
            {
                VoiceRateDropped++;
                continue; // this listener already gets as many talkers as we send anyone
            }
            _vw.PatchU8(routeAt, (byte)route);
            Net.SendTo(c.Guid, _vw.Span, PacketReliability.Unreliable);
            VoiceFramesRelayed++;
            VoiceBytesRelayed += _vw.Length;
            VoiceRelayedTo[c.Id]++;
        }
    }

    /// <summary>A frame for us to hear (from the network, or the host relaying to itself).</summary>
    private void ReceiveVoice(byte talker, ushort seq, VoiceRoute route, ReadOnlySpan<byte> frame, double now)
    {
        if (talker == LocalId || !VoiceListen || !VoiceAllowed || route == VoiceRoute.None)
        {
            Voice.FramesDropped++;
            return; // never your own voice back
        }
        if (Players.TryGetValue(talker, out var p) && (p.Bot != null || MutedVoice.Contains(p.Name)))
        {
            Voice.FramesDropped++;
            return;
        }
        Voice.Push(talker, seq, route, frame, now);
    }

    private void OnVoicePacket(byte id, ref PacketReader r, RakNetGUID sender, double now)
    {
        switch (id)
        {
            case (byte)Msg.VoiceUp when IsServer && r.Has(VoiceRules.UpBytes):
            {
                if (!_clients.TryGetValue(sender.G, out var c) || !Players.TryGetValue(c.Id, out var p))
                    break;
                var seq = r.U16();
                var flags = r.U8();
                var frame = r.Take(VoiceCodec.FrameBytes);
                if (r.Remaining == 0)
                    ServerVoice(p, seq, flags, frame, now);
                break;
            }
            case (byte)Msg.VoiceDown when !IsServer && r.Has(VoiceRules.DownBytes):
            {
                var talker = r.U8();
                var seq = r.U16();
                var route = r.U8();
                var frame = r.Take(VoiceCodec.FrameBytes);
                if (route is >= (byte)VoiceRoute.Proximity and <= (byte)VoiceRoute.Open && r.Remaining == 0)
                    ReceiveVoice(talker, seq, (VoiceRoute)route, frame, now);
                else
                    Voice.FramesDropped++;
                break;
            }
            case (byte)Msg.VoiceCtl when IsServer && r.Has(1):
                if (_clients.TryGetValue(sender.G, out var who))
                    _voiceDeaf[who.Id] = (r.U8() & 1) == 0;
                break;
            case (byte)Msg.VoiceInfo when !IsServer && r.Has(1):
                VoiceAllowed = (r.U8() & 1) != 0;
                if (!VoiceAllowed)
                    Voice.ClearAll();
                SendVoiceCtl(); // and our side: do we listen?
                break;
        }
    }

    /// <summary>Server: someone left: forget their voice state (ids are reused).</summary>
    private void OnVoicePlayerLeft(byte id)
    {
        Voice.Clear(id);
        _voiceDeaf[id] = false;
        _talkTokens[id] = _listenTokens[id] = 0;
        _talkLast[id] = _listenLast[id] = -1e9;
    }

    /// <summary>Each frame before playback: where every talker's head is (their stream plays from there).</summary>
    public void UpdateVoiceHeads()
    {
        foreach (var s in Voice.Active)
        {
            if (Players.TryGetValue(s.Talker, out var p))
            {
                s.Head = p.RenderEye;
                s.HeadKnown = true;
            }
            else
            {
                s.HeadKnown = false;
                if (s.Jitter.Buffered > 0)
                    s.Jitter.Reset(); // they left
            }
        }
    }

    /// <summary>Each talker's volume from <paramref name="s"/> (Settings' per-player voice volumes, by name). Once a frame; no allocations.</summary>
    public void UpdateVoiceVolumes(Settings s)
    {
        foreach (var v in Voice.Active)
            v.Volume = Players.TryGetValue(v.Talker, out var p) ? s.VoicePlayerGain(p.Name) : 1f;
    }

    /// <summary>A new session: the players you muted before stay muted (Settings remembers them by name).</summary>
    public void LoadVoiceMutes(Settings s) => MutedVoice.UnionWith(s.VoicePlayerMuted);

    /// <summary>Self-test: <paramref name="p"/>'s voice frame arriving at the server, as if from the network.</summary>
    public void SelfTestVoiceFrom(Player p, ReadOnlySpan<byte> frame, ushort seq, bool teamRadio, double now) =>
        ServerVoice(p, seq, teamRadio ? VoiceRules.FlagTeam : (byte)0, frame, now);
}
