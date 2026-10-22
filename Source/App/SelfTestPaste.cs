using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Brutal.GlfwApi;
using Brutal.ImGuiApi;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// Self-test section "Paste and packets": the playtester crash (an image pasted into a text box) and the hardening
/// around it.
///   - The clipboard hooks (<see cref="SafeClipboard"/>), called through the same native function pointer ImGui uses:
///     an image (null), an error, 1 MB of text, line breaks, emoji, NULs, broken UTF-8... all paste as printable ASCII
///     of at most 4096 characters, never NULL, never an exception.
///   - Every text box type: a paste of each of those into a box of the real size, read back by the code that reads
///     that box, gives clean, capped text that goes back into the box without throwing.
///   - Text sanitizers and the build parser under thousands of random strings (a deep build can't overflow the stack).
///   - Names, feed lines, banners, match results and server-browser entries from other players arrive clean.
///   - Packet fuzzing: thousands of random packets of every message type into a client and a host. A short packet
///     may only fail as <see cref="MalformedPacketException"/> (dropped by the safety net); anything else is a bug.
///     Through the safety net, nothing escapes.
/// </summary>
public static partial class SelfTest
{
    private static volatile string _pasteStep = "";
    private static volatile byte[]? _pastePacket;
    private static volatile bool _pasteAbandoned;

    /// <summary>
    /// Runs the checks on a worker thread with a deadline, so a handler that loops forever on a garbage packet shows up
    /// as a failure naming the message and the packet instead of hanging the whole self-test.
    /// </summary>
    private static void CheckPaste()
    {
        var worker = new Thread(() =>
        {
            try
            {
                CheckPasteBody();
            }
            catch (Exception ex)
            {
                try
                {
                    Fail($"Paste and packets threw at {_pasteStep}: {ex}");
                }
                catch
                {
                    // the log is already closed (we were abandoned): nothing left to report to
                }
            }
        }, 64 << 20) { IsBackground = true, Name = "paste and packets" };
        worker.Start();
        if (!worker.Join(TimeSpan.FromSeconds(150)))
        {
            _pasteAbandoned = true;
            var packet = _pastePacket;
            Fail($"Paste and packets hung at: {_pasteStep}"
                 + (packet != null ? $" (packet {Convert.ToHexString(packet.AsSpan(0, Math.Min(packet.Length, 48)))}, {packet.Length} bytes)" : ""));
        }
    }

    private static unsafe void CheckPasteBody()
    {
        _pasteStep = "root cause";
        // ---- The root cause -------------------------------------------------------------------------------------
        // BRUTAL's GlfwBackend.GetClipboardText: var str = Glfw.Clipboard; Utf8.GetByteCount(str) -> Encoding.UTF8.GetByteCount.
        try
        {
            _ = Encoding.UTF8.GetByteCount((string)null!);
            Line("note: GetByteCount(null) no longer throws (the BRUTAL paste hook may be safe now)");
        }
        catch (ArgumentNullException)
        {
            Line("root cause reproduced: with an image on the clipboard Glfw.Clipboard is null, and the BRUTAL backend's paste hook "
                 + "passes it to Utf8.GetByteCount, which throws ArgumentNullException inside a native callback (the game closes)");
        }

        var oldSource = SafeClipboard.Source;
        var oldSink = SafeClipboard.Sink;
        var sw = Stopwatch.StartNew();
        var savedOut = Console.Out;
        Console.SetOut(TextWriter.Null); // the deliberate clipboard failures log exception names, which CI reads as crashes
        try
        {
            _pasteStep = "clipboard hooks and text boxes";
            CheckClipboardHooks();
        }
        finally
        {
            Console.SetOut(savedOut);
            SafeClipboard.Source = oldSource;
            SafeClipboard.Sink = oldSink;
        }

        // The host froze on a garbage yaw: WrapAngle looped forever on huge values (1e34 - 2 pi == 1e34).
        _pasteStep = "WrapAngle";
        foreach (var a in new[] { 1e34f, -3e38f, float.MaxValue, float.PositiveInfinity, float.NegativeInfinity, 12345.6f, -98765.4f })
        {
            var w = World.WrapAngle(a);
            if (!(w >= -MathF.PI - 1e-4f && w <= MathF.PI + 1e-4f))
                Fail($"WrapAngle({a}) = {w}");
        }
        for (var i = -2000; i <= 2000; i++)
        {
            var a = i * 0.37f; // ordinary angles: exactly the old loop's answer
            var old = a;
            while (old > MathF.PI)
                old -= MathF.Tau;
            while (old < -MathF.PI)
                old += MathF.Tau;
            if (MathF.Abs(a) <= 1000f && World.WrapAngle(a) != old)
            {
                Fail($"WrapAngle({a}) changed for an ordinary angle");
                break;
            }
        }
        Line($"  (clipboard and text boxes: {sw.ElapsedMilliseconds} ms)");
        _pasteStep = "sanitizers";
        CheckSanitizers();
        _pasteStep = "received text";
        CheckReceivedText();
        _pasteStep = "packet fuzzing";
        CheckPacketFuzz();
        _pasteStep = "done";
    }

    /// <summary>Pastes <paramref name="source"/>'s text through the real hook (a native function pointer call, as ImGui makes it).</summary>
    private static unsafe string HookPaste(Func<string?> source, string what)
    {
        SafeClipboard.Source = source;
        var hook = SafeClipboard.GetTextHook;
        var p = hook(0);
        if (p == null)
        {
            Fail($"paste ({what}): the hook returned NULL");
            return "";
        }
        var bytes = MemoryMarshal.CreateReadOnlySpanFromNullTerminated(p);
        foreach (var b in bytes)
            if (b is < 0x20 or > 0x7E)
            {
                Fail($"paste ({what}): byte 0x{b:X2} reached ImGui");
                break;
            }
        if (bytes.Length > SafeClipboard.MaxPaste)
            Fail($"paste ({what}): {bytes.Length} bytes, over the {SafeClipboard.MaxPaste} cap");
        return Encoding.ASCII.GetString(bytes);
    }

    private static readonly (string Name, Func<string?> Source)[] PasteSamples =
    [
        ("an image (no text)", () => null),
        ("an empty clipboard", () => ""),
        ("a clipboard error", () => throw new InvalidOperationException("clipboard locked by another program")),
        ("1 MB of text", () => new string('A', 1 << 20)),
        ("line breaks and tabs", () => "line one\r\nline two\tthree\n"),
        ("emoji, accents and CJK", () => "Caf\u00E9 \U0001F642 \u4E2D\u6587 name"),
        ("lone surrogates", () => "a\uD800b\uDC00c"),
        ("NUL characters", () => "abc\0def\0"),
        ("broken UTF-8", () => Encoding.UTF8.GetString([0xC3, 0x28, 0xA0, 0xA1, 0xE2, 0x28, 0xA1, 0xF0, 0x28, 0x8C, 0xBC, 0x41])),
        ("control characters", () => "\u001b[31mred\u007f\u0007\u0085bell"),
        ("printf and save-file separators", () => "%s%n%x | { } : , = 100%"),
        ("nothing printable", () => "\0\u0001\u0002\U0001F642\uFFFD"),
        ("an IP address", () => "  203.0.113.45:27015 \n"),
    ];

    private static unsafe void CheckClipboardHooks()
    {
        var noText = SafeClipboard.NoText;
        var image = HookPaste(() => null, "an image");
        if (image.Length != 0 || SafeClipboard.NoText != noText + 1)
            Fail($"an image on the clipboard pasted '{image}' (expected nothing, counted as no text)");
        if (!SafeClipboard.LastLog.Contains("no text"))
            Fail($"an image paste didn't log the expected line (got '{SafeClipboard.LastLog}')");
        Line($"image on the clipboard: empty paste, log line \"{SafeClipboard.LastLog}\"");
        for (var i = 0; i < 1000; i++)
            HookPaste(() => null, "an image, held down"); // Ctrl+V held: no crash, no log flood (rate-limited)

        foreach (var (name, source) in PasteSamples)
        {
            var got = HookPaste(source, name);
            Line($"  paste {name}: {got.Length} char(s) \"{(got.Length > 40 ? got[..40] + "..." : got)}\"");
        }
        if (HookPaste(() => new string('A', 1 << 20), "1 MB").Length != SafeClipboard.MaxPaste)
            Fail("1 MB of text wasn't cut to the paste cap");
        if (HookPaste(() => "  203.0.113.45:27015 \n", "address") != "  203.0.113.45:27015 ")
            Fail("an ordinary paste changed");

        // Every text box type, at its real size, read back by the code that reads it.
        var fields = new (string Name, int Capacity, Func<string, string> Read, int Max, bool Spaces)[]
        {
            ("player name", 32, x => Settings.Clean(x), 16, true),
            ("server name", 33, x => Settings.CleanServerName(x), 32, true),
            ("join address", 128, x => Settings.CleanAddress(x), 127, false),
            ("master server", 128, x => Settings.CleanAddress(x), 127, false),
            ("loadout name", 25, x => LoadoutStore.CleanName(x), 24, true),
            ("build name", 33, x => WeaponBuild.CleanName(x), 32, true),
            ("lobby chat", ChatRules.BoxCapacity, x => ChatRules.Clean(x), ChatRules.MaxLength, true),
        };
        var rng = new Random(7001);
        var pastes = PasteSamples.Select(x => (x.Name, x.Source)).ToList();
        for (var i = 0; i < 200; i++)
        {
            var s = RandomText(rng);
            pastes.Add(($"random text {i}", () => s));
        }
        var checks = 0;
        foreach (var f in fields)
        {
            var bad = 0;
            foreach (var (name, source) in pastes)
            {
                _pasteStep = $"text box {f.Name}: pasting {name}";
                try
                {
                    var box = new ImInputString(f.Capacity);
                    ImGuiSafe.SetInput(box, "start ");
                    SimulatePaste(box, HookPaste(source, name));
                    var value = f.Read(box.ToString());
                    if (!IsPrintableAscii(value) || value.Length > f.Max || (!f.Spaces && value.Contains(' ')))
                    {
                        if (bad++ < 3)
                            Fail($"{f.Name}: pasting {name} gave \"{value}\" (printable ASCII, at most {f.Max})");
                    }
                    ImGuiSafe.SetInput(box, value); // the box is refilled from the stored value (next time the screen opens)
                    if (box.ToString() != value || f.Read(value) != value)
                    {
                        if (bad++ < 3)
                            Fail($"{f.Name}: \"{value}\" doesn't survive a round trip through the box");
                    }
                    if (f.Name == "master server")
                        _ = new Settings { MasterServer = value }.Master;
                    checks++;
                }
                catch (Exception ex)
                {
                    if (bad++ < 3)
                        Fail($"{f.Name}: pasting {name} threw {ex.GetType().Name}: {ex.Message}");
                }
            }
        }
        Line($"{checks} pastes into {fields.Length} text box types ({pastes.Count} clipboard contents each), all clean");

        // The bug next door: ImInputString.SetValue throws when text fills the whole box; SetInput cuts it to fit.
        var emoji = string.Concat(Enumerable.Repeat("\U0001F642", 20));
        try
        {
            new ImInputString(25).SetValue(emoji.AsSpan());
            Line("note: ImInputString.SetValue accepted text bigger than its box");
        }
        catch (Exception)
        {
            Line("ImInputString.SetValue throws on text that fills its box (e.g. emoji); ImGuiSafe.SetInput is used instead");
        }
        try
        {
            var box = new ImInputString(25);
            ImGuiSafe.SetInput(box, emoji + new string('x', 500));
            ImGuiSafe.SetInput(new ImInputString(128), new string('9', 5000));
            if (box.Length >= box.Capacity)
                Fail("SetInput overfilled a box");
        }
        catch (Exception ex)
        {
            Fail($"ImGuiSafe.SetInput threw: {ex.Message}");
        }

        // Copy (Ctrl+C / Ctrl+X) goes through the other hook.
        string? copied = null;
        SafeClipboard.Sink = s => copied = s;
        var text = "copy me 100%\0"u8.ToArray();
        var copy = SafeClipboard.SetTextHook;
        fixed (byte* t = text)
            copy(0, t);
        copy(0, null);
        var delivered = copied;
        SafeClipboard.Sink = _ => throw new InvalidOperationException("clipboard busy");
        fixed (byte* t = text)
            copy(0, t); // must not escape
        if (delivered != "copy me 100%")
            Fail($"copy hook delivered '{delivered}'");

        // GLFW's error callback: an image paste raises FormatUnavailable each time; logged, rate-limited, never thrown.
        for (var i = 0; i < 100; i++)
            SafeClipboard.LogGlfwError(GlfwErrorCode.FormatUnavailable, "Win32: Failed to convert clipboard to string");
    }

    /// <summary>What ImGui does with a paste in a single-line box: appends as many bytes as fit (leaving the NUL), then the box re-reads its length.</summary>
    private static void SimulatePaste(ImInputString box, string pasted)
    {
        var bytes = Encoding.UTF8.GetBytes(pasted);
        var room = box.Capacity - 1 - box.Length;
        var n = Math.Min(room, bytes.Length);
        bytes.AsSpan(0, n).CopyTo(box.Buffer.AsSpan(box.Length));
        box.Buffer[box.Length + n] = 0;
        box.EvaluateLength();
    }

    private static bool IsPrintableAscii(string s)
    {
        foreach (var c in s)
            if (c is < ' ' or > '~')
                return false;
        return true;
    }

    /// <summary>Hostile text: ASCII, control characters, NULs, Latin-1, emoji, lone surrogates, U+FFFD, format separators, sometimes very long.</summary>
    private static string RandomText(Random rng)
    {
        var len = rng.Next(5) switch { 0 => rng.Next(0, 8), 1 => rng.Next(8, 40), 2 => rng.Next(40, 300), 3 => rng.Next(300, 3000), _ => rng.Next(3000, 20000) };
        if (rng.Next(6) == 0)
        {
            var raw = new byte[len];
            rng.NextBytes(raw);
            return Encoding.UTF8.GetString(raw); // broken UTF-8, decoded the way the network and clipboard decode it
        }
        var sb = new StringBuilder(len);
        while (sb.Length < len)
        {
            switch (rng.Next(10))
            {
                case 0: sb.Append((char)rng.Next(0, 32)); break;
                case 1: sb.Append('\0'); break;
                case 2: sb.Append((char)rng.Next(0x80, 0x100)); break;
                case 3: sb.Append("\U0001F642"); break;
                case 4: sb.Append((char)rng.Next(0xD800, 0xE000)); break;
                case 5: sb.Append("\uFFFD"); break;
                case 6: sb.Append("|{}:,=%\t\r\n"[rng.Next(10)]); break;
                case 7: sb.Append((char)127); break;
                default: sb.Append((char)rng.Next(32, 127)); break;
            }
        }
        return sb.ToString();
    }

    private static void CheckSanitizers()
    {
        var rng = new Random(7002);
        var bad = 0;
        void Check(string what, string input, string output, int max, bool nonEmpty = false)
        {
            if (IsPrintableAscii(output) && output.Length <= max && (!nonEmpty || output.Length > 0))
                return;
            if (bad++ < 5)
                Fail($"{what}: \"{Escape(input)}\" -> \"{Escape(output)}\" (printable ASCII, at most {max})");
        }
        var count = 0;
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < 3000; i++)
        {
            var s = RandomText(rng);
            _pasteStep = $"sanitizers: string {i} ({s.Length} chars)";
            try
            {
                Check("NetSafety.CleanText", s, NetSafety.CleanText(s, 64), 64);
                Check("SafeClipboard.CleanPaste", s, SafeClipboard.CleanPaste(s), SafeClipboard.MaxPaste);
                Check("Settings.Clean", s, Settings.Clean(s), 16, nonEmpty: true);
                Check("Settings.CleanServerName", s, Settings.CleanServerName(s), 32);
                Check("Settings.CleanAddress", s, Settings.CleanAddress(s), 127);
                Check("LoadoutStore.CleanName", s, LoadoutStore.CleanName(s), 24, nonEmpty: true);
                Check("WeaponBuild.CleanName", s, WeaponBuild.CleanName(s), 32, nonEmpty: true);
                if (NetSafety.StripControls(s).Any(char.IsControl))
                    Fail("NetSafety.StripControls left a control character");
                try
                {
                    WeaponBuild.Parse(s, Parts.Find); // garbage: a build or FormatException, nothing else
                    WeaponBuild.Parse("Fuzz|auto|" + s, Parts.Find);
                }
                catch (FormatException)
                {
                }
                count++;
            }
            catch (Exception ex)
            {
                if (bad++ < 5)
                    Fail($"a sanitizer threw on \"{Escape(s)}\": {ex}");
            }
        }
        Line($"{count} random strings through 7 sanitizers and the build parser in {sw.ElapsedMilliseconds} ms");

        // A hostile build nested far deeper than any real one must fail as garbage, not overflow the stack.
        var deep = "Deep|auto|" + string.Concat(Enumerable.Repeat("Grip{a:", 20000)) + "Grip" + new string('}', 20000);
        try
        {
            WeaponBuild.Parse(deep, Parts.Find);
            Fail("a build nested 20000 deep parsed");
        }
        catch (FormatException ex)
        {
            Line($"a build nested 20000 deep is refused: {ex.Message}");
        }
    }

    private static string Escape(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in s.Length > 60 ? s[..60] : s)
            sb.Append(c is >= ' ' and <= '~' ? c.ToString() : $"\\u{(int)c:X4}");
        return sb.ToString();
    }

    private static readonly RakNetGUID FuzzGuid = new() { G = 0xC0FFEE, SystemIndex = 0 };

    /// <summary>A host with one (pretend) remote client connected, and a client: both offline, packets fed straight in.</summary>
    private static (Game Server, Game Client) FuzzGames()
    {
        var server = new Game(NetPeer.Offline(), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        server.SelfTestPacketRaw(FuzzGuid, [(byte)DefaultMessageIDTypes.NewIncomingConnection], 0);
        var client = new Game(NetPeer.OfflineClient(), new Audio(), "Guest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        return (server, client);
    }

    private static void CheckReceivedText()
    {
        var savedMap = World.Map;
        var savedPhysics = World.PhysicsMode;
        var (server, client) = FuzzGames();
        using var s = server;
        using var c = client;
        try
        {
            // A garbage name in the Hello (NUL, 0xFF, line breaks, emoji bytes, '%', a length longer than the packet).
            byte[] junk = [0, 0xFF, (byte)'\n', (byte)'B', (byte)'o', (byte)'b', 0xF0, 0x9F, 0x99, 0x82, (byte)'%', (byte)'s', 7, 0x7F, .. Enumerable.Repeat((byte)'Z', 40)];
            var w = new PacketWriter();
            w.Begin(Msg.Hello).U8(200).Bytes(junk);
            s.SelfTestPacket(FuzzGuid, w.Span, 0);
            var joined = s.Players.Values.FirstOrDefault(p => p.Id != 0);
            if (joined == null)
                Fail("received text: the pretend client didn't join");
            else if (!IsPrintableAscii(joined.Name) || joined.Name.Length is 0 or > 16)
                Fail($"received text: a garbage Hello name became \"{Escape(joined.Name)}\"");
            else
                Line($"garbage Hello name -> \"{joined.Name}\"");

            // Roster names, a match result, a feed line and a banner from a (hostile) host.
            w.Begin(Msg.Roster).U8(2).U8(1).U8(0).U8((byte)junk.Length).Bytes(junk).U8(5).U8(48).U8(255) // + prestige, level, hat (protocol 24)
                .U8(2).U8(1).U8((byte)junk.Length).Bytes(junk).U8(0).U8(0).U8(0);
            c.SelfTestPacket(default, w.Span, 0);
            w.Begin(Msg.MatchEnd).U8(1).U8((byte)junk.Length).Bytes(junk);
            c.SelfTestPacket(default, w.Span, 0);
            w.Begin(Msg.ModeEvent).U8(255).U8((byte)junk.Length).Bytes(junk);
            c.SelfTestPacket(default, w.Span, 0);
            w.Begin(Msg.ZEvent).U8((byte)ZEventKind.Banner).U16(0).U8(255).U8((byte)junk.Length).Bytes(junk);
            c.SelfTestPacket(default, w.Span, 0);
            foreach (var (what, text) in new[]
                     {
                         ("roster name", c.NameOf(1)), ("bot roster name", c.NameOf(2)), ("match result", c.MatchResult),
                         ("feed line", c.Feed.Count > 0 ? c.Feed[^1].Text : ""), ("banner", c.ZBannerText),
                     })
            {
                if (!IsPrintableAscii(text) || text.Length > 255)
                    Fail($"received text: the {what} arrived as \"{Escape(text)}\"");
            }
            Line($"from a hostile host: roster \"{c.NameOf(1)}\", result \"{c.MatchResult}\", banner \"{c.ZBannerText}\"");

            // Server browser: a reply with a garbage name, map and rotation.
            byte[] info = [.. "COS1"u8, ServerInfo.ProtocolVersion, 0, 1, 8, 0, 0, 0x87, 0x69, (byte)junk.Length, .. junk, (byte)junk.Length, .. junk, 0, (byte)junk.Length, .. junk];
            if (ServerInfo.Find(info) is not { } found)
                Fail("server browser: a reply with garbage names wasn't read at all");
            else if (!IsPrintableAscii(found.Name + found.MapName + found.Rotation) || found.Name.Length > 32 || found.MapName.Length > 40 || found.Rotation.Length > 24)
                Fail($"server browser: garbage names shown as \"{Escape(found.Name)}\" / \"{Escape(found.MapName)}\" / \"{Escape(found.Rotation)}\"");
            else
                Line($"server browser entry with garbage names -> \"{found.Name}\" on \"{found.MapName}\"");

            // A normal name still round-trips untouched.
            var normal = new ServerInfo("Friday Night 100%", Maps.All[0].Name, 3, 8, 2, MatchPhase.Playing, 27015, ServerInfo.ProtocolVersion, Rotation: "True Random");
            if (ServerInfo.Find(normal.Encode()) is not { } back || back.Name != normal.Name || back.MapName != normal.MapName || back.Rotation != normal.Rotation)
                Fail("server browser: a normal server info didn't round-trip");
        }
        finally
        {
            if (World.Map != savedMap)
                World.SetMap(savedMap);
            World.PhysicsMode = savedPhysics;
        }
    }

    private static void CheckPacketFuzz()
    {
        const int perType = 1500;
        var savedMap = World.Map;
        var savedPhysics = World.PhysicsMode;
        var savedOut = Console.Out;
        var (server, client) = FuzzGames();
        using var s = server;
        using var c = client;
        var rng = new Random(7003);
        var bugs = new Dictionary<string, string>();
        int sent = 0, shortOnes = 0, escaped = 0;
        var slow = new List<string>();
        var dropped = NetSafety.Dropped;
        var sw = Stopwatch.StartNew();
        Console.SetOut(TextWriter.Null); // the handlers log feed lines; 100k of them would bury the log
        try
        {
            var now = 1.0;
            foreach (var id in Enum.GetValues<Msg>())
            {
                var typeTime = Stopwatch.StartNew();
                var done = 0;
                for (var i = 0; i < perType && !_pasteAbandoned; i++, done++)
                {
                    if (typeTime.Elapsed.TotalSeconds > 1.5)
                        break; // time budget per message type (reported below): some host messages are slow on purpose (a prop state rebuilds the map's props)
                    var packet = FuzzPacket(rng, (byte)id);
                    _pastePacket = packet;
                    now += 0.001;
                    foreach (var (side, game) in new[] { ("client", c), ("host", s) })
                    {
                        sent++;
                        _pasteStep = $"packet fuzzing: {id} #{i} on the {side}";
                        var one = Stopwatch.StartNew();
                        try
                        {
                            game.SelfTestPacketRaw(FuzzGuid, packet, now);
                        }
                        catch (MalformedPacketException)
                        {
                            shortOnes++; // dropped by the safety net in a real game: fine
                        }
                        catch (Exception ex)
                        {
                            var key = $"{id} on the {side}: {ex.GetType().Name} at {TopFrame(ex)}";
                            bugs.TryAdd(key, $"{ex.Message} (packet {Convert.ToHexString(packet.AsSpan(0, Math.Min(packet.Length, 24)))})");
                        }
                        // A client must not be able to stall the host: anything that slow on the host side is a bug. (A host can slow its
                        // clients down in many legitimate ways, so the client side only gets a line.)
                        if (one.Elapsed.TotalSeconds > 0.5 && side == "host")
                            bugs.TryAdd($"{id} on the {side}: one packet took {one.Elapsed.TotalSeconds:0.0} s", Convert.ToHexString(packet.AsSpan(0, Math.Min(packet.Length, 24))));
                        if (i % 8 == 0)
                        {
                            try
                            {
                                game.SelfTestPacket(FuzzGuid, packet, now); // the real path: nothing may escape it
                            }
                            catch (Exception)
                            {
                                escaped++;
                            }
                        }
                    }
                }
                if (done < perType || typeTime.ElapsedMilliseconds > 1000)
                    slow.Add($"{id}: {done} in {typeTime.ElapsedMilliseconds} ms");
            }
            if (slow.Count > 0)
                Line($"  slow message types: {string.Join(", ", slow)}");

            // The browser and the master server's parsers.
            _pasteStep = "server browser parsers";
            for (var i = 0; i < 4000; i++)
            {
                var packet = FuzzPacket(rng, (byte)DefaultMessageIDTypes.UnconnectedPong);
                var tag = i % 2 == 0 ? "COS1"u8 : "COLS"u8;
                var at = rng.Next(packet.Length);
                tag[..Math.Min(4, packet.Length - at)].CopyTo(packet.AsSpan(at));
                try
                {
                    if (ServerInfo.Find(packet) is { } info && !IsPrintableAscii(info.Name + info.MapName + info.Rotation))
                        bugs.TryAdd("server info: unclean text", info.Name);
                    if (MasterServerApp.ParseList(packet) is { } list && list.Any(x => !IsPrintableAscii(x.Address)))
                        bugs.TryAdd("master list: unclean address", "");
                }
                catch (Exception ex)
                {
                    bugs.TryAdd($"server browser parsers: {ex.GetType().Name} at {TopFrame(ex)}", ex.Message);
                }
            }
        }
        finally
        {
            Console.SetOut(savedOut);
            World.Map.RestoreProps(); // prop state packets broke props on the shared map
            if (World.Map != savedMap)
                World.SetMap(savedMap);
            World.PhysicsMode = savedPhysics;
        }
        Line($"fuzzed {sent} random packets ({perType} of each of {Enum.GetValues<Msg>().Length} message types, client and host) in {sw.ElapsedMilliseconds} ms: "
             + $"{shortOnes} too short (dropped), {bugs.Count} other failure kind(s), {NetSafety.Dropped - dropped} dropped by the safety net, {escaped} escaped it");
        Line($"  last drop line: {NetSafety.LastDrop}");
        if (escaped > 0)
            Fail($"{escaped} exception(s) escaped the packet safety net");
        foreach (var (key, example) in bugs.Take(12))
            Fail($"packet handler bug: {key}: {example}");
    }

    /// <summary>A random packet: lengths from 1 byte to a full datagram, filled with noise, zeros, 0xFF or small numbers (valid-looking counts and ids).</summary>
    private static byte[] FuzzPacket(Random rng, byte id)
    {
        var len = rng.Next(5) switch { 0 => rng.Next(1, 4), 1 => rng.Next(4, 24), 2 => rng.Next(24, 96), 3 => rng.Next(96, 400), _ => rng.Next(400, 1400) };
        var b = new byte[len];
        switch (rng.Next(5))
        {
            case 0: rng.NextBytes(b); break;
            case 1: break; // zeros
            case 2: Array.Fill(b, (byte)0xFF); break;
            case 3:
                for (var i = 0; i < len; i++)
                    b[i] = (byte)rng.Next(0, 4);
                break;
            default:
                for (var i = 0; i < len; i++)
                    b[i] = rng.Next(4) == 0 ? (byte)rng.Next(256) : (byte)rng.Next(0, 3);
                break;
        }
        b[0] = id;
        return b;
    }

    private static string TopFrame(Exception ex)
    {
        var lines = (ex.StackTrace ?? "").Split('\n');
        var frame = lines.FirstOrDefault(l => l.Contains("CallOfSlop.")) ?? lines.FirstOrDefault() ?? "?";
        frame = frame.Trim();
        var inAt = frame.IndexOf(" in ", StringComparison.Ordinal);
        return inAt > 0 ? frame[..inAt] : frame;
    }
}
