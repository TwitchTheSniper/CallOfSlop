using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Self-test section "Chat and voice" (LobbyChat.cs, GameVoice.cs, Source/Audio/Voice, UiChat.cs): chat routing (all / team
/// / system lines / mute) over a loopback host and two clients, the free-for-all and dead rules and limits offline; the voice
/// codec's quality and frame size, the jitter buffer (reorder, loss, late frames, bounded delay), proximity gain and wall
/// occlusion, the routing table, the server's relay (range, team radio, never back to the talker, rate limits) over
/// loopback, bandwidth per talker, no allocations on the voice path, and settings / keybind persistence. Short: a few
/// seconds of loopback, everything else on simulated time. No microphone is ever opened (and CI has none).
/// </summary>
public static partial class SelfTest
{
    private static readonly List<string> VoiceNotes = new();

    /// <summary>A result line, also collected for the CI summary annotation.</summary>
    private static void VLine(string text)
    {
        Line(text);
        VoiceNotes.Add(text);
    }

    private static void CheckChatVoice()
    {
        VoiceNotes.Clear();
        CheckVoiceCodec();
        CheckJitterBuffer();
        CheckVoiceRules();
        CheckChatOffline();
        CheckChatVoiceLoopback();
        CheckVoiceAllocations();
        CheckChatVoiceSettings();
        var devices = MicCapture.Devices();
        VLine($"capture: {devices.Length} capture device(s) listed; opening one is skipped here (CI has no microphone, and the self-test never opens a mic)");
        Line("::notice title=Chat and voice (results)::" + string.Join("%0A", VoiceNotes.Select(n => n.Replace("%", "%25").Replace("\r", "").Replace("\n", " "))));
    }

    // ---- Test signals ------------------------------------------------------------------------------------------

    private static short[] VoiceTone(int samples, params (float Hz, float Amp)[] parts)
    {
        var pcm = new short[samples];
        for (var i = 0; i < samples; i++)
        {
            var v = 0.0;
            foreach (var (hz, amp) in parts)
                v += amp * Math.Sin(2 * Math.PI * hz * i / VoiceCodec.SampleRate);
            pcm[i] = (short)Math.Clamp(v * 32767, -32767, 32767);
        }
        return pcm;
    }

    /// <summary>Speech-like noise: low-passed noise with a 4 Hz syllable envelope.</summary>
    private static short[] VoiceBabble(int samples, int seed)
    {
        var rng = new Random(seed);
        var pcm = new short[samples];
        double lp = 0, lp2 = 0;
        for (var i = 0; i < samples; i++)
        {
            var w = rng.NextDouble() * 2 - 1;
            lp += 0.3 * (w - lp);
            lp2 += 0.3 * (lp - lp2);
            var env = 0.5 + 0.5 * Math.Sin(2 * Math.PI * 4 * i / VoiceCodec.SampleRate);
            pcm[i] = (short)Math.Clamp(lp2 * env * 2.2 * 32767, -32767, 32767);
        }
        return pcm;
    }

    private static double CodecSnr(short[] signal)
    {
        var state = new VoiceCodec.EncoderState();
        var frame = new byte[VoiceCodec.FrameBytes];
        var decoded = new short[signal.Length];
        for (var f = 0; f + VoiceCodec.FrameSamples <= signal.Length; f += VoiceCodec.FrameSamples)
        {
            VoiceCodec.Encode(signal.AsSpan(f, VoiceCodec.FrameSamples), frame, ref state);
            VoiceCodec.Decode(frame, decoded.AsSpan(f, VoiceCodec.FrameSamples));
        }
        return VoiceCodec.Snr(signal, decoded);
    }

    // ---- Codec -------------------------------------------------------------------------------------------------

    private static void CheckVoiceCodec()
    {
        var n = VoiceCodec.SampleRate; // one second
        var tone = CodecSnr(VoiceTone(n, (440, 0.3f)));
        var chord = CodecSnr(VoiceTone(n, (300, 0.18f), (1200, 0.12f)));
        var babble = CodecSnr(VoiceBabble(n, 11));
        var quiet = CodecSnr(VoiceTone(n, (700, 0.01f)));
        VLine($"codec: IMA-ADPCM {VoiceCodec.SampleRate / 1000} kHz mono, {VoiceCodec.FrameMs} ms frames of {VoiceCodec.FrameBytes} bytes "
             + $"({VoiceCodec.FrameBytes * 8 * VoiceCodec.FramesPerSecond / 1000} kbps); SNR tone {tone:0.0} dB, two tones {chord:0.0} dB, speech-like noise {babble:0.0} dB, quiet tone {quiet:0.0} dB");
        int samples = VoiceCodec.FrameSamples, bytes = VoiceCodec.FrameBytes; // (locals: comparing the constants directly is folded and warns)
        if (samples != 320 || bytes != 164)
            Fail($"voice frames are {VoiceCodec.FrameSamples} samples / {VoiceCodec.FrameBytes} bytes (expected 320 / 164)");
        if (tone < 25 || chord < 22 || babble < 15 || quiet < 12)
            Fail($"voice codec quality too low (SNR tone {tone:0.0} >= 25, two tones {chord:0.0} >= 22, speech {babble:0.0} >= 15, quiet {quiet:0.0} >= 12 dB)");

        // Garbage from the network decodes to silence, never throws; a frame decodes on its own (after a loss).
        var pcm = new short[VoiceCodec.FrameSamples];
        var junk = new byte[VoiceCodec.FrameBytes];
        new Random(5).NextBytes(junk);
        junk[2] = 200;
        var okJunk = VoiceCodec.Decode(junk, pcm);
        var okShort = VoiceCodec.Decode(junk.AsSpan(0, 50), pcm);
        var silent = pcm.All(v => v == 0);
        var sig = VoiceTone(VoiceCodec.FrameSamples * 3, (500, 0.3f));
        var st = new VoiceCodec.EncoderState();
        var f1 = new byte[VoiceCodec.FrameBytes];
        var f3 = new byte[VoiceCodec.FrameBytes];
        VoiceCodec.Encode(sig.AsSpan(0, 320), f1, ref st);
        VoiceCodec.Encode(sig.AsSpan(320, 320), f1, ref st);
        VoiceCodec.Encode(sig.AsSpan(640, 320), f3, ref st); // frame 3 decoded without frames 1 and 2
        VoiceCodec.Decode(f3, pcm);
        var alone = VoiceCodec.Snr(sig.AsSpan(640, 320), pcm);
        VLine($"codec: junk frame -> {(okJunk ? "decoded" : "rejected")}, short frame -> {(okShort ? "decoded" : "rejected")}, silent {silent}; a frame decoded alone after two losses: SNR {alone:0.0} dB");
        if (okJunk || okShort || !silent)
            Fail("a garbage voice frame wasn't rejected as silence");
        if (alone < 20)
            Fail($"a voice frame doesn't decode on its own after a loss (SNR {alone:0.0} dB)");
    }

    // ---- Jitter buffer -----------------------------------------------------------------------------------------

    /// <summary>A frame whose audio is a constant level that identifies it (seq * 40 + 1000).</summary>
    private static byte[] MarkedFrame(int seq)
    {
        var pcm = new short[VoiceCodec.FrameSamples];
        Array.Fill(pcm, (short)(1000 + (seq % 500) * 40));
        var st = new VoiceCodec.EncoderState { Predictor = 1000 + (seq % 500) * 40, Index = 20 };
        var frame = new byte[VoiceCodec.FrameBytes];
        VoiceCodec.Encode(pcm, frame, ref st);
        return frame;
    }

    private static int FrameMark(short[] pcm)
    {
        long sum = 0;
        for (var i = VoiceCodec.FrameSamples / 2; i < VoiceCodec.FrameSamples; i++)
            sum += pcm[i];
        var mean = sum / (VoiceCodec.FrameSamples / 2.0);
        return (int)Math.Round((mean - 1000) / 40.0);
    }

    private static void CheckJitterBuffer()
    {
        var pcm = new short[VoiceCodec.FrameSamples];

        // 1. Reordered pairs (0 2 1 3 5 4 ...): every frame plays, in order, none concealed.
        var jb = new JitterBuffer();
        var order = new List<int> { 0 }; // 0 2 1 4 3 ... 58 57 59: every pair after the first swapped
        for (var i = 1; i < 59; i += 2)
        {
            order.Add(i + 1);
            order.Add(i);
        }
        order.Add(59);
        var played = new List<int>();
        var silenceFirst = 0;
        var pushed = 0;
        for (var t = 0; t < 80; t++)
        {
            if (pushed < order.Count)
                jb.Push((ushort)(order[pushed] + 1000), MarkedFrame(order[pushed++]));
            var r = jb.Pull(pcm);
            if (r == VoicePull.Silence && played.Count == 0)
                silenceFirst++;
            if (r == VoicePull.Frame)
                played.Add(FrameMark(pcm));
        }
        var inOrder = played.Count == 60 && played.Select((m, i) => m == i).All(x => x);
        VLine($"jitter: reordered arrival -> {played.Count}/60 played {(inOrder ? "in order" : "OUT OF ORDER")}, {jb.Concealed} concealed (the fade-out at the end), {silenceFirst} pulls waited for the first {JitterBuffer.MinTargetFrames} frames");
        if (!inOrder || jb.Concealed > 1 || silenceFirst < 1) // (one concealed frame at the very end is the fade-out when the talker stops)
            Fail($"the jitter buffer didn't put reordered frames back in order ({string.Join(",", played.Take(12))}...)");

        // 2. Loss: every 7th frame dropped: each is concealed (fading), the rest play in order.
        jb = new JitterBuffer();
        played.Clear();
        var concealedLevels = new List<int>();
        var dropped = 0;
        for (var t = 0; t < 120; t++)
        {
            if (t < 100)
            {
                if (t % 7 == 3)
                    dropped++;
                else
                    jb.Push((ushort)(65500 + t), MarkedFrame(t)); // wraps past 65535
            }
            var r = jb.Pull(pcm);
            if (r == VoicePull.Frame)
                played.Add(FrameMark(pcm));
            else if (r == VoicePull.Concealed)
                concealedLevels.Add(Math.Abs((int)pcm[200]));
        }
        var monotonic = played.Zip(played.Skip(1)).All(p => p.Second > p.First);
        VLine($"jitter: {dropped} of 100 frames lost -> {played.Count} played (in order {monotonic}, sequence wrapped), {jb.Concealed} concealed, delay target {jb.TargetFrames} frames");
        if (played.Count != 100 - dropped || !monotonic || jb.Concealed < dropped || jb.Concealed > dropped + JitterBuffer.MaxConceal)
            Fail($"packet loss concealment: {played.Count} played, {jb.Concealed} concealed for {dropped} lost");

        // 3. A late frame (already concealed) is dropped; a burst is cut down so the delay stays bounded.
        jb = new JitterBuffer();
        for (var i = 0; i < 4; i++)
            jb.Push((ushort)i, MarkedFrame(i));
        for (var i = 0; i < 4; i++)
            jb.Pull(pcm);
        var lateTaken = jb.Push(1, MarkedFrame(1));
        for (var i = 4; i < 15; i++)
            jb.Push((ushort)i, MarkedFrame(i)); // 11 frames at once
        jb.Pull(pcm);
        var depthAfter = jb.Buffered;
        VLine($"jitter: late frame taken {lateTaken} (late count {jb.Late}); a burst of 11 frames -> {depthAfter} left after one pull (skipped {jb.Skipped})");
        if (lateTaken || jb.Late < 1 || depthAfter > JitterBuffer.MaxTargetFrames + 3)
            Fail("the jitter buffer kept a late frame, or let a burst build up a long delay");

        // 4. Garbage sizes are refused; the talker stopping ends playback (Silence), and a new spurt starts again.
        var refused = !jb.Push(99, new byte[10]);
        var tail = 0;
        while (jb.Pull(pcm) != VoicePull.Silence && tail < 40)
            tail++;
        var after = jb.Pull(pcm);
        for (var i = 0; i < 4; i++)
            jb.Push((ushort)(40 + i), MarkedFrame(40 + i));
        var restarted = jb.Pull(pcm);
        VLine($"jitter: wrong-size frame refused {refused}; after the talker stopped: {tail} pulls then {after}; a new talk spurt -> {restarted}");
        if (!refused || after != VoicePull.Silence || restarted != VoicePull.Frame)
            Fail("the jitter buffer didn't stop when the talker stopped, or didn't restart");
    }

    // ---- Rules: routing, gain, occlusion --------------------------------------------------------------------------

    private static void CheckVoiceRules()
    {
        var at = new Vector3(0, 1.6f, 0);
        VoiceParty P(float x, byte team = 0, bool alive = true) => new(at + new Vector3(x, 0, 0), team, alive);
        var cases = new (string Name, VoiceRoute Got, VoiceRoute Want)[]
        {
            ("near, proximity", VoiceRules.Route(P(0), P(10), false, MatchPhase.Playing, false, false), VoiceRoute.Proximity),
            ("out of range", VoiceRules.Route(P(0), P(60), false, MatchPhase.Playing, false, false), VoiceRoute.None),
            ("just past the range (margin)", VoiceRules.Route(P(0), P(VoiceRules.Range + 1), false, MatchPhase.Playing, false, false), VoiceRoute.Proximity),
            ("team radio, mate far away", VoiceRules.Route(P(0, 1), P(500, 1), true, MatchPhase.Playing, true, false), VoiceRoute.Radio),
            ("team radio, enemy near", VoiceRules.Route(P(0, 1), P(2, 0), true, MatchPhase.Playing, true, false), VoiceRoute.None),
            ("team radio in free-for-all", VoiceRules.Route(P(0), P(2), true, MatchPhase.Playing, false, false), VoiceRoute.None),
            ("team radio in co-op", VoiceRules.Route(P(0, 255), P(300, 255), true, MatchPhase.Playing, false, true), VoiceRoute.Radio),
            ("dead to living (proximity)", VoiceRules.Route(P(0, 0, false), P(2), false, MatchPhase.Playing, false, false), VoiceRoute.None),
            ("living to dead", VoiceRules.Route(P(0), P(2, 0, false), false, MatchPhase.Playing, false, false), VoiceRoute.None),
            ("dead to dead, far", VoiceRules.Route(P(0, 0, false), P(300, 1, false), false, MatchPhase.Playing, true, false), VoiceRoute.Open),
            ("dead on team radio", VoiceRules.Route(P(0, 0, false), P(300, 0), true, MatchPhase.Playing, true, false), VoiceRoute.Radio),
            ("warmup: not spawned still talks", VoiceRules.Route(P(0, 0, false), P(5), false, MatchPhase.Warmup, false, false), VoiceRoute.Proximity),
            ("lobby room: everyone, flat", VoiceRules.Route(P(0), P(900), false, MatchPhase.Lobby, false, false), VoiceRoute.Open),
            ("results screen: everyone", VoiceRules.Route(P(0, 0), P(900, 1), true, MatchPhase.Ended, true, false), VoiceRoute.Open),
        };
        var wrong = cases.Where(c => c.Got != c.Want).ToList();
        VLine($"routing: {cases.Length - wrong.Count}/{cases.Length} cases right"
             + (wrong.Count > 0 ? $"; wrong: {string.Join("; ", wrong.Select(c => $"{c.Name} -> {c.Got} (want {c.Want})"))}" : ""));
        foreach (var c in wrong)
            Fail($"voice routing: {c.Name} gave {c.Got}, expected {c.Want}");

        // Gain against distance.
        float[] ds = [0, 3, 10, 20, 30, 39, 39.9f, 40, 60];
        var gains = ds.Select(d => VoiceRules.ProximityGain(d)).ToArray();
        var falls = gains.Zip(gains.Skip(1)).All(p => p.Second <= p.First + 1e-6f);
        var occluded = VoiceRules.ProximityGain(10, 1);
        var hfClear = VoiceRules.ProximityHf(10, 0);
        var hfWall = VoiceRules.ProximityHf(10, 1);
        VLine($"proximity gain: {string.Join(", ", ds.Zip(gains).Select(p => $"{p.First:0.#} m {p.Second:0.000}"))}; behind a wall at 10 m {occluded:0.000} (HF {hfWall:0.00} vs {hfClear:0.00} clear)");
        if (MathF.Abs(gains[0] - 1) > 1e-4f || !falls || gains[6] > 0.01f || gains[7] != 0 || gains[8] != 0 || gains[2] < 0.3f)
            Fail("proximity gain should be 1 up close, fall with distance and reach 0 at the range");
        if (occluded >= gains[2] * 0.7f || hfWall > hfClear * 0.3f)
            Fail("a wall between talker and listener doesn't muffle the voice enough");

        // Occlusion on a real map: a ray through the biggest box is blocked, one over it is clear.
        var map = FindMap("Proto Box", m => m.Solid.Count > 4)!;
        World.SetMap(map);
        var boxes = map.Solid.Where(b => b.Half.Y > 0.8f && b.Half.X > 0.3f && b.Max.Y < 30).OrderByDescending(b => b.Half.X * b.Half.Y * b.Half.Z).ToList();
        if (boxes.Count == 0)
        {
            VLine($"occlusion: no solid box on {map.Name} to test with (skipped)");
            return;
        }
        var box = boxes[0];
        var y = box.Center.Y;
        var a = new Vector3(box.Min.X - 2, y, box.Center.Z);
        var b2 = new Vector3(box.Max.X + 2, y, box.Center.Z);
        var through = Audio.VoiceOccluded(a, b2);
        var over = Audio.VoiceOccluded(new Vector3(a.X, box.Max.Y + 3, a.Z), new Vector3(b2.X, box.Max.Y + 3, b2.Z));
        VLine($"occlusion on {map.Name}: through a {box.Half.X * 2:0.#} m box -> {through}, over it -> {over}");
        if (!through)
            Fail("a voice through a solid box isn't occluded");
    }

    // ---- Chat offline: free-for-all, the dead, limits, mute -------------------------------------------------------

    private static void CheckChatOffline()
    {
        using (var ffa = ModeGame(0, GameMode.FreeForAll))
        {
            var guest = ffa.SelfTestAddHuman();
            var why = ffa.SendChat("team?", 10, team: true);
            var before = ffa.ChatsRouted;
            ffa.SelfTestChatFrom(guest, "team from guest", 10, team: true);
            VLine($"free-for-all: team chat from you -> \"{why}\"; from a guest -> routed {ffa.ChatsRouted - before}");
            if (why != ChatRules.NoTeamHint || ffa.ChatsRouted != before || ffa.Chat.Count != 0)
                Fail("team chat in free-for-all should be refused with a hint (and nothing sent)");
        }
        using (var tdm = ModeGame(0, GameMode.TeamDeathmatch))
        {
            var me = tdm.Local!;
            var mate = tdm.SelfTestAddHuman();
            var enemy = tdm.SelfTestAddHuman();
            me.Team = mate.Team = 0;
            enemy.Team = 1;
            tdm.Phase = MatchPhase.Playing;
            me.Alive = mate.Alive = enemy.Alive = true;
            tdm.SelfTestChatFrom(enemy, "enemy team line", 10, team: true);
            tdm.SelfTestChatFrom(mate, "mate team line", 11, team: true);
            var gotEnemy = tdm.Chat.Any(c => c.Text == "enemy team line");
            var mateLine = tdm.Chat.FirstOrDefault(c => c.Text == "mate team line");
            // The dead: tagged [DEAD]; with the host's "dead only" the living don't see it.
            enemy.Alive = false;
            tdm.SelfTestChatFrom(enemy, "from the grave", 12);
            var deadLine = tdm.Chat.FirstOrDefault(c => c.Text == "from the grave");
            tdm.DeadChatPrivate = true;
            tdm.SelfTestChatFrom(enemy, "dead only", 13);
            var livingSaw = tdm.Chat.Any(c => c.Text == "dead only");
            me.Alive = false;
            tdm.SelfTestChatFrom(enemy, "dead to dead", 14);
            var deadSaw = tdm.Chat.Any(c => c.Text == "dead to dead");
            // Mute: a muted player's lines don't show; system lines always do.
            me.Alive = true;
            tdm.MutedChat.Add(mate.Name);
            tdm.SelfTestChatFrom(mate, "muted line", 15);
            var mutedShown = tdm.Chat.Any(c => c.Text == "muted line");
            tdm.ServerSystemChat($"{mate.Name} joined", 16);
            var sys = tdm.Chat[^1];
            // Team chat shares the rate limit.
            var n0 = tdm.Chat.Count;
            tdm.MutedChat.Clear();
            for (var i = 0; i < 10; i++)
                tdm.SelfTestChatFrom(mate, $"spam {i}", 100, team: true);
            var kept = tdm.Chat.Count - n0;
            VLine($"team deathmatch (host on Blue): enemy team line shown {gotEnemy}, mate's team line \"{(mateLine.Text == null ? "-" : ChatRules.Format(mateLine))}\"; "
                 + $"dead line \"{(deadLine.Text == null ? "-" : ChatRules.Format(deadLine))}\"; dead-only: living saw {livingSaw}, dead saw {deadSaw}; "
                 + $"muted shown {mutedShown}; system line \"{ChatRules.Format(sys)}\" ({sys.Flags}); 10 team lines at once -> {kept} kept");
            if (gotEnemy || mateLine.Text == null || (mateLine.Flags & ChatFlags.Team) == 0)
                Fail("team chat reached the other team, or didn't reach the sender's team");
            if (deadLine.Text == null || (deadLine.Flags & ChatFlags.Dead) == 0 || livingSaw || !deadSaw)
                Fail("the dead chat rule didn't apply ([DEAD] tag, dead-only reaching only the dead)");
            if (mutedShown || (sys.Flags & ChatFlags.System) == 0 || ChatRules.Format(sys) != $"{mate.Name} joined")
                Fail("a muted player's line showed, or a system line is wrong");
            if (kept != ChatRules.Burst)
                Fail($"team chat isn't rate-limited like all chat ({kept} of 10 kept, burst {ChatRules.Burst})");
        }
    }

    // ---- Loopback: chat and voice through RakNet ---------------------------------------------------------------

    private static void PumpEach(Game server, Game[] clients, double seconds, ref double now, Stopwatch clock, Action<double> each)
    {
        var until = clock.Elapsed.TotalSeconds + seconds;
        while (clock.Elapsed.TotalSeconds < until)
        {
            var t = clock.Elapsed.TotalSeconds;
            var dt = Math.Clamp(t - now, 0.001, 0.1);
            now = t;
            each(now);
            server.Update(now, dt);
            foreach (var c in clients)
                c.Update(now, dt);
            Thread.Sleep(2);
        }
    }

    private static void CheckChatVoiceLoopback()
    {
        var small = FindMap("Proto Box", m => m.MaxPlayers >= 8)!;
        const ushort port = 26213;
        var clock = Stopwatch.StartNew();
        var now = 0.0;
        World.SetMap(small);
        using var server = new Game(NetPeer.Host(port, Game.MaxPlayers - 1), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id,
            bots: 1, mapId: small.Id);
        server.MapVoteEnabled = false;
        server.WarmupEnabled = true;
        server.Mode = GameMode.TeamDeathmatch;
        using var a = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Alpha", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        using var b = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Bravo", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        Game[] clients = [a, b];
        Pump(server, clients, 2.0, ref now, clock);
        if (a.LocalId == byte.MaxValue || b.LocalId == byte.MaxValue || server.Local == null)
        {
            Fail("chat/voice loopback: the clients didn't join");
            return;
        }
        var host = server.Local;
        var sa = server.Players[a.LocalId];
        var sb = server.Players[b.LocalId];

        // System lines and all chat.
        bool SystemLine(Game g, string text) => g.Chat.Any(c => (c.Flags & ChatFlags.System) != 0 && c.Text == text);
        var joinLine = SystemLine(server, "Alpha joined") && SystemLine(server, "Bravo joined") && (SystemLine(b, "Alpha joined") || SystemLine(a, "Bravo joined"));
        a.SendChat("hello everyone", now);
        Pump(server, clients, 0.4, ref now, clock);
        var allOk = server.Chat.Any(c => c.Text == "hello everyone") && b.Chat.Any(c => c.Text == "hello everyone") && a.Chat.Any(c => c.Text == "hello everyone");

        // Team chat: Host + Alpha on Blue, Bravo on Red (set on the server; the warmup doesn't rebalance in this short run).
        host.Team = sa.Team = 0;
        sb.Team = 1;
        Pump(server, clients, 0.3, ref now, clock);
        server.SendChat("host to blue", now, team: true);
        a.SendChat("alpha to blue", now, team: true);
        b.SendChat("bravo to red", now, team: true);
        Pump(server, clients, 0.5, ref now, clock);
        var teamsHeld = host.Team == 0 && sa.Team == 0 && sb.Team == 1;
        bool Has(Game g, string text) => g.Chat.Any(c => c.Text == text && (c.Flags & ChatFlags.Team) != 0);
        var teamOk = Has(a, "host to blue") && !Has(b, "host to blue") && Has(server, "alpha to blue") && Has(a, "alpha to blue") && !Has(b, "alpha to blue")
                     && Has(b, "bravo to red") && !Has(a, "bravo to red") && !Has(server, "bravo to red");

        // Mute over the network: Bravo mutes Alpha.
        b.MutedChat.Add(a.Local!.Name);
        a.SendChat("can bravo hear me", now);
        Pump(server, clients, 0.4, ref now, clock);
        var muteOk = !b.Chat.Any(c => c.Text == "can bravo hear me") && server.Chat.Any(c => c.Text == "can bravo hear me");
        b.MutedChat.Clear();
        VLine($"chat loopback: join line {joinLine}; all chat everywhere {allOk}; team chat Blue (host, Alpha) / Red (Bravo) right {teamOk} (teams held {teamsHeld}); Bravo muting Alpha works {muteOk}");
        if (!joinLine || !allOk || !muteOk)
            Fail("chat over the network: a join line, an all-chat line or a mute didn't work");
        if (teamsHeld && !teamOk)
            Fail("team chat over the network reached the wrong people");

        // ---- Voice ----
        var sender = new VoiceSender();
        var tone = VoiceTone(VoiceCodec.FrameSamples * 50, (300, 0.2f), (900, 0.1f));
        var pcm = new short[VoiceCodec.FrameSamples];
        var spot = sa.Position;
        Vector3 hostAt = spot, bravoAt = spot;
        var talkTeam = false;
        var nextSend = now;
        var sent = 0;
        void Each(double t)
        {
            sa.Position = spot;
            sb.Position = bravoAt;
            host.Position = hostAt;
            while (nextSend <= t)
            {
                nextSend += VoiceCodec.FrameMs / 1000.0;
                tone.AsSpan((sent % 50) * VoiceCodec.FrameSamples, VoiceCodec.FrameSamples).CopyTo(pcm);
                if (sender.Process(pcm, true, false, -40, t) && a.SendVoice(sender, talkTeam, t))
                    sent++;
            }
        }
        (int Bravo, int Host, int Alpha, long Bytes, int Frames) Burst(double seconds, ref double clockNow)
        {
            var b0 = server.VoiceRelayedTo[b.LocalId];
            var h0 = server.VoiceRelayedTo[host.Id];
            var a0 = server.VoiceRelayedTo[a.LocalId];
            var bytes0 = a.Net.BytesSent;
            var s0 = sent;
            nextSend = clockNow;
            PumpEach(server, clients, seconds, ref clockNow, clock, Each);
            Pump(server, clients, 0.15, ref clockNow, clock); // the last frames land
            return (server.VoiceRelayedTo[b.LocalId] - b0, server.VoiceRelayedTo[host.Id] - h0, server.VoiceRelayedTo[a.LocalId] - a0, a.Net.BytesSent - bytes0, sent - s0);
        }

        VLine($"voice loopback: phase {server.Phase}, voice allowed on the clients {a.VoiceAllowed}/{b.VoiceAllowed}");
        // 1. Proximity: Bravo 5 m away hears; the host 80 m up doesn't; Alpha never gets herself back.
        bravoAt = spot + new Vector3(5, 0, 0);
        hostAt = spot + new Vector3(0, 80, 0);
        var bravoIn0 = b.Voice.FramesIn;
        var near = Burst(0.6, ref now);
        var bravoGot = b.Voice.FramesIn - bravoIn0;
        var stream = b.Voice.Get(a.LocalId);
        var bytesPerSecond = near.Bytes / 0.6;
        VLine($"voice near: Alpha sent {near.Frames} frames ({bytesPerSecond / 1024:0.0} KB/s up incl. other traffic; voice alone {VoiceRules.TalkerBytesPerSecond / 1024.0:0.0} KB/s = {VoiceRules.TalkerBytesPerSecond * 8 / 1000} kbps); "
             + $"relayed to Bravo (5 m) {near.Bravo}, Bravo took in {bravoGot} (route {stream?.Route}, talking {b.IsTalking(sa, now)}), to the host (80 m) {near.Host}, back to Alpha {near.Alpha}");
        if (near.Frames < 20 || near.Bravo < near.Frames * 0.8 || bravoGot < near.Frames * 0.8 || stream?.Route != VoiceRoute.Proximity)
            Fail($"proximity voice didn't reach a listener 5 m away ({near.Bravo} of {near.Frames} relayed, {bravoGot} received)");
        if (near.Host != 0 || near.Alpha != 0 || a.Voice.FramesIn != 0)
            Fail("voice reached a listener out of range, or came back to the talker");
        var perFrame = server.VoiceBytesRelayed / Math.Max(1, server.VoiceFramesRelayed);
        if (VoiceRules.TalkerBytesPerSecond > 10 * 1024 || perFrame != 1 + VoiceRules.DownBytes)
            Fail($"a talker sends too much: {VoiceRules.TalkerBytesPerSecond} B/s of voice, {perFrame} B per relayed frame (expected {1 + VoiceRules.DownBytes})");

        // 2. Bravo walks 60 m away: nothing more is relayed to him.
        bravoAt = spot + new Vector3(0, 60, 0);
        var far = Burst(0.4, ref now);
        VLine($"voice far: Bravo 60 m away got {far.Bravo} of {far.Frames} frames, the host {far.Host}");
        if (far.Frames < 10 || far.Bravo != 0 || far.Host != 0)
            Fail("the server relayed voice to listeners out of range");

        // 3. Team radio: the host (Blue, Alpha's team) hears at 80 m; Bravo (Red) doesn't, even 5 m away.
        talkTeam = true;
        bravoAt = spot + new Vector3(5, 0, 0);
        var radio = Burst(0.4, ref now);
        var hostStream = server.Voice.Get(a.LocalId);
        VLine($"team radio: host (Blue, 80 m) got {radio.Host} of {radio.Frames} (route {hostStream?.Route}), Bravo (Red, 5 m) {radio.Bravo}");
        if (teamsHeld && (radio.Host < radio.Frames * 0.8 || radio.Bravo != 0 || hostStream?.Route != VoiceRoute.Radio))
            Fail("team radio didn't reach the team at any distance, or reached the other team");
        talkTeam = false;

        // 4. Voice off on Bravo (Settings "Off"): the server stops sending to him.
        b.SetVoiceListen(false);
        Pump(server, clients, 0.3, ref now, clock);
        var deaf = Burst(0.3, ref now);
        b.SetVoiceListen(true);
        // 5. The host turns voice off: nothing is relayed.
        server.VoiceAllowed = false;
        var off = Burst(0.3, ref now);
        server.VoiceAllowed = true;
        VLine($"voice off on Bravo -> {deaf.Bravo} frames sent to him; host voice off -> {off.Bravo + off.Host} relayed; server totals: {server.VoiceFramesUp} up, {server.VoiceFramesRelayed} relayed ({server.VoiceBytesRelayed / Math.Max(1, server.VoiceFramesRelayed)} B each), {server.VoiceRateDropped} rate-dropped");
        if (deaf.Bravo != 0 || off.Bravo + off.Host != 0)
            Fail("voice was relayed to a player with voice off, or with the host's voice chat off");

        // 6. Server rate limit: a flood of frames at one instant is cut to the burst.
        var floodBefore = server.VoiceFramesUp;
        for (var i = 0; i < 100; i++)
            server.SelfTestVoiceFrom(sa, sender.Frame, (ushort)i, false, now + 50);
        var flooded = server.VoiceFramesUp - floodBefore;
        VLine($"rate limit: 100 frames at once from one talker -> {flooded} accepted (burst {VoiceRules.TalkBurst}, then {VoiceRules.TalkRate}/s)");
        if (flooded > VoiceRules.TalkBurst)
            Fail("the server's voice rate limit let a flood through");
    }

    // ---- No allocations ---------------------------------------------------------------------------------------

    private static void CheckVoiceAllocations()
    {
        using var game = ModeGame(0, GameMode.FreeForAll);
        var guest = game.SelfTestAddHuman();
        game.Phase = MatchPhase.Warmup;
        guest.Position = game.Local!.Position + new Vector3(3, 0, 0);
        var sender = new VoiceSender();
        var tone = VoiceTone(VoiceCodec.FrameSamples, (440, 0.2f));
        var pcm = new short[VoiceCodec.FrameSamples];
        var outPcm = new short[VoiceCodec.FrameSamples];
        var radio = new RadioFilter();
        var jb = new JitterBuffer();
        double now = 100;
        void Step(int i)
        {
            tone.CopyTo(pcm, 0);
            sender.Process(pcm, true, false, -40, now);
            jb.Push(sender.Seq, sender.Frame);
            game.SelfTestVoiceFrom(guest, sender.Frame, sender.Seq, false, now); // server relay -> the host hears it (hub, jitter)
            jb.Pull(outPcm);
            radio.Process(outPcm);
            game.Voice.Get(guest.Id)?.Jitter.Pull(outPcm);
            now += 0.02;
        }
        for (var i = 0; i < 50; i++)
            Step(i); // warm up (the talker's stream is made once)
        var before = GC.GetAllocatedBytesForCurrentThread();
        var t0 = Stopwatch.GetTimestamp();
        for (var i = 0; i < 1000; i++)
            Step(i);
        var us = (Stopwatch.GetTimestamp() - t0) * 1e6 / Stopwatch.Frequency / 1000;
        var alloc = GC.GetAllocatedBytesForCurrentThread() - before;
        var heard = game.Voice.Get(guest.Id)?.Jitter.Played ?? 0;
        // Only if this window saw bytes: the least of it and three more identical rounds (SelfTestCpuRatio.cs), so a one-off runtime allocation can't fail it.
        alloc = RecheckAllocBytes(alloc, () =>
        {
            for (var i = 0; i < 1000; i++)
                Step(i);
        });
        VLine($"voice path (process + encode + relay + 2 jitter buffers + decode + radio EQ): {alloc} bytes allocated over 1000 frames, {us:0.0} us a frame ({us / 200:0.000}% of a core per talker); host heard {heard} frames");
        if (alloc > 0)
            Fail($"the voice path allocates ({alloc} bytes over 1000 frames)");
        if (heard < 900)
            Fail($"the host didn't hear a guest 3 m away ({heard} frames played)");
    }

    // ---- Settings and keybinds ---------------------------------------------------------------------------------

    private static void CheckChatVoiceSettings()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "callofslop-voice-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            var fresh = new Settings();
            File.WriteAllLines(tmp, ["name=Old", "volume=0.5"]);
            var old = Settings.Load(tmp);
            var s = new Settings
            {
                VoiceMode = VoiceMode.TeamRadio, VoiceTransmit = true, VoiceActivation = true, VoiceThresholdDb = -33f, VoiceDevice = "USB Mic (2- Blue Yeti)",
                VoiceVolume = 0.4f, VoiceMuteAll = true, AllowVoice = false, DeadChatPrivate = true,
            };
            s.Save(tmp);
            var back = Settings.Load(tmp);
            var same = back.VoiceMode == s.VoiceMode && back.VoiceTransmit && back.VoiceActivation && MathF.Abs(back.VoiceThresholdDb + 33f) < 1e-3f
                       && back.VoiceDevice == s.VoiceDevice && MathF.Abs(back.VoiceVolume - 0.4f) < 1e-4f && back.VoiceMuteAll && !back.AllowVoice && back.DeadChatPrivate;
            VLine($"settings: first run mic {(fresh.VoiceTransmit ? "ON" : "off")}, voice {fresh.VoiceMode}, playback volume {fresh.VoiceVolume}; an old file -> mic {(old.VoiceTransmit ? "ON" : "off")}, allow voice {old.AllowVoice}; saved and loaded back the same: {same}");
            if (fresh.VoiceTransmit || old.VoiceTransmit || fresh.VoiceMode != VoiceMode.Proximity || !old.AllowVoice || fresh.VoiceMuteAll)
                Fail("voice defaults: the mic must be off until turned on, playback on, voice allowed");
            if (!same)
                Fail("voice settings don't survive a save and load");

            // Keybinds: the chat and voice keys are new in binds version 3; an old file keeps its own keys.
            var d = Keybinds.Defaults;
            var oldBinds = new List<string> { "binds_version=2" };
            for (var i = 0; i < (int)GameAction.ChatAll; i++)
                oldBinds.Add($"bind_{(GameAction)i}={(i == (int)GameAction.Melee ? Keybinds.Key(Brutal.GlfwApi.GlfwKey.Y) : d[i])}");
            File.WriteAllLines(tmp, oldBinds);
            var mig = Settings.Load(tmp).Binds;
            var dup = Duplicates(mig);
            VLine($"keybinds v3: defaults Chat (all) {Code(d[(int)GameAction.ChatAll])}, Chat (team) {Code(d[(int)GameAction.ChatTeam])}, Push to talk {Code(d[(int)GameAction.PushToTalk])}; "
                 + $"an old file with Melee on Y -> Melee {Code(mig[(int)GameAction.Melee])}, Chat (team) {Code(mig[(int)GameAction.ChatTeam])}, Chat (all) {Code(mig[(int)GameAction.ChatAll])}, shared keys: {(dup.Length > 0 ? dup : "none")}");
            if (mig[(int)GameAction.Melee] != Keybinds.Key(Brutal.GlfwApi.GlfwKey.Y) || mig[(int)GameAction.ChatTeam] != Keybinds.Unbound
                || mig[(int)GameAction.ChatAll] != d[(int)GameAction.ChatAll] || mig[(int)GameAction.PushToTalk] != d[(int)GameAction.PushToTalk] || dup.Length > 0)
                Fail("the chat / voice keys' migration took a key the player uses, or didn't add the free ones");
        }
        finally
        {
            try { File.Delete(tmp); } catch (IOException) { }
        }
    }
}
