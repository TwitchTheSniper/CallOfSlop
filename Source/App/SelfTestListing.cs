using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// Self-test for playing with friends without setup (docs/net/SERVER-DISCOVERY.md): invite codes (round trip, typos,
/// versions, the Join box), the STUN client (canned answers, fuzz, a loopback fake server), the server info and list
/// reply extensions, address substitution, server list settings, the browser's merge, and on loopback the automatic
/// list: a host with no list starts one and is listed, a second host registers on it, a client learns it from the
/// invite and from the host, the first host quits and the second starts its own, then hands over to a better list.
/// No internet: everything talks to 127.0.0.1.
/// </summary>
public static partial class SelfTest
{
    private static void CheckInviteCodes()
    {
        var rng = new Random(1234);
        var bad = 0;
        for (var i = 0; i < 2000; i++)
        {
            var ip = new IPAddress([(byte)rng.Next(1, 224), (byte)rng.Next(256), (byte)rng.Next(256), (byte)rng.Next(256)]);
            var port = (ushort)rng.Next(1, 65536);
            var list = (i % 3) switch { 0 => (ushort)0, 1 => (ushort)(InviteCode.ListPortBase + rng.Next(255)), _ => MasterServerApp.DefaultPort };
            var inv = new Invite(ip, port, (byte)rng.Next(256), list);
            var code = InviteCode.Encode(inv);
            if (!InviteCode.TryDecode(code, out var back, out var err) || back != inv)
            {
                if (bad++ < 3)
                    Fail($"invite {ip}:{port} list {list} -> {code} -> {(err.Length > 0 ? err : back.ToString())}");
            }
        }
        var sample = new Invite(IPAddress.Parse("203.0.113.45"), 27015, ServerInfo.ProtocolVersion, 27050);
        var c = InviteCode.Encode(sample);
        Line($"invite for 203.0.113.45:27015 list 27050: {c} ({InviteCode.Link(c)}); 2000 random round trips, {bad} bad");
        if (c.Length != 19 || c[4] != '-' || c[9] != '-' || c[14] != '-')
            Fail($"invite code '{c}' isn't four groups of four");
        // Forms a person might paste: lower case, no dashes, spaces, the link, look-alike letters.
        var plain = c.Replace("-", "");
        foreach (var form in new[] { c.ToLowerInvariant(), plain, $"  {c[..9]} {c[10..]}  ", InviteCode.Link(c), $"CALLOFSLOP://join/{plain.ToLowerInvariant()}/",
                     c.Replace('0', 'O').Replace('1', 'I') })
            if (!InviteCode.TryDecode(form, out var f, out var e) || f != sample)
                Fail($"invite form '{form}' didn't decode ({e})");
        // Every single-character typo is caught by the check digits (a 16-bit checksum: one in 65536 could slip by).
        var undetected = 0;
        const string alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
        for (var pos = 0; pos < plain.Length; pos++)
            foreach (var ch in alphabet)
            {
                if (ch == plain[pos])
                    continue;
                var typo = plain[..pos] + ch + plain[(pos + 1)..];
                if (InviteCode.TryDecode(typo, out var t, out _) && t == sample)
                    Fail("a typo decoded to the same invite");
                else if (InviteCode.TryDecode(typo, out _, out _))
                    undetected++;
            }
        Line($"single-character typos: {plain.Length * 31} tried, {undetected} slipped past the check digits");
        if (undetected > 2)
            Fail($"{undetected} single-character typos passed the check digits");
        if (InviteCode.TryDecode(plain[..15], out _, out var shortErr) || !shortErr.Contains("16"))
            Fail($"a short code wasn't explained ({shortErr})");
        if (InviteCode.TryDecode("hello world!", out _, out _))
            Fail("nonsense decoded as an invite");
        // Fuzz: random text never throws.
        for (var i = 0; i < 3000; i++)
        {
            var len = rng.Next(0, 40);
            var chars = new char[len];
            for (var k = 0; k < len; k++)
                chars[k] = (char)rng.Next(0, 0x2000);
            try
            {
                InviteCode.TryDecode(new string(chars), out _, out _);
                InviteCode.TryParseJoin(new string(chars), 27015, out _, out _, out _, out _);
            }
            catch (Exception ex)
            {
                Fail($"invite decode threw on fuzz: {ex.GetType().Name}");
                break;
            }
        }
        // The Join box: codes, addresses, ports, versions.
        if (!InviteCode.TryParseJoin(c, 1, out var h, out var p, out var ji, out var je) || h != "203.0.113.45" || p != 27015 || ji?.ListPort != 27050)
            Fail($"Join box: code -> {h}:{p} ({je})");
        if (!InviteCode.TryParseJoin("192.168.1.20:27016", 27015, out h, out p, out ji, out _) || h != "192.168.1.20" || p != 27016 || ji != null)
            Fail($"Join box: ip:port -> {h}:{p}");
        if (!InviteCode.TryParseJoin("localhost", 27015, out h, out p, out _, out _) || h != "localhost" || p != 27015)
            Fail($"Join box: host name -> {h}:{p}");
        if (InviteCode.TryParseJoin("fe80::1", 27015, out _, out _, out _, out var v6) || !v6.Contains("IPv6"))
            Fail($"Join box: IPv6 wasn't refused clearly ({v6})");
        if (InviteCode.TryParseJoin("1.2.3.4:99999", 27015, out _, out _, out _, out var badPort) || !badPort.Contains("port"))
            Fail($"Join box: a bad port wasn't refused ({badPort})");
        var other = InviteCode.Encode(sample with { Protocol = (byte)(ServerInfo.ProtocolVersion - 1) });
        if (InviteCode.TryParseJoin(other, 27015, out _, out _, out _, out var verErr) || !verErr.Contains("different version"))
            Fail($"an invite from another build wasn't refused with the version ({verErr})");
        else
            Line($"other build's invite: {verErr}");
        foreach (var notInvite in new[] { "localhost", "192.168.1.1", "my-pc", "10.0.0.5:27015" })
            if (InviteCode.LooksLikeInvite(notInvite))
                Fail($"'{notInvite}' was taken for an invite code");
    }

    /// <summary>A Binding success response carrying <paramref name="attrs"/> (type, value) for <paramref name="tx"/>.</summary>
    private static byte[] StunResponse(byte[] tx, params (ushort Type, byte[] Value)[] attrs)
    {
        var body = new List<byte>();
        foreach (var (type, value) in attrs)
        {
            body.AddRange([(byte)(type >> 8), (byte)type, (byte)(value.Length >> 8), (byte)value.Length]);
            body.AddRange(value);
            while (body.Count % 4 != 0)
                body.Add(0);
        }
        var msg = new byte[20 + body.Count];
        BinaryPrimitives.WriteUInt16BigEndian(msg, 0x0101);
        BinaryPrimitives.WriteUInt16BigEndian(msg.AsSpan(2), (ushort)body.Count);
        BinaryPrimitives.WriteUInt32BigEndian(msg.AsSpan(4), Stun.MagicCookie);
        tx.CopyTo(msg, 8);
        body.CopyTo(msg, 20);
        return msg;
    }

    private static byte[] StunAddr(IPEndPoint ep, bool xor)
    {
        var v = new byte[8];
        v[1] = 1;
        var ip = BinaryPrimitives.ReadUInt32BigEndian(ep.Address.GetAddressBytes());
        BinaryPrimitives.WriteUInt16BigEndian(v.AsSpan(2), (ushort)(xor ? ep.Port ^ (int)(Stun.MagicCookie >> 16) : ep.Port));
        BinaryPrimitives.WriteUInt32BigEndian(v.AsSpan(4), xor ? ip ^ Stun.MagicCookie : ip);
        return v;
    }

    private static void CheckStun()
    {
        var tx = Enumerable.Range(1, 12).Select(i => (byte)i).ToArray();
        var pub = new IPEndPoint(IPAddress.Parse("198.51.100.23"), 54321);
        var other = new IPEndPoint(IPAddress.Parse("192.0.2.9"), 1111);
        var req = Stun.BuildRequest(tx);
        if (req.Length != 20 || req[0] != 0 || req[1] != 1 || BinaryPrimitives.ReadUInt32BigEndian(req.AsSpan(4)) != Stun.MagicCookie)
            Fail("STUN request isn't a 20-byte Binding request");
        var xorOk = Stun.ParseResponse(StunResponse(tx, (0x0020, StunAddr(pub, true))), tx);
        var plainOk = Stun.ParseResponse(StunResponse(tx, (0x0001, StunAddr(pub, false))), tx);
        var both = Stun.ParseResponse(StunResponse(tx, (0x0001, StunAddr(other, false)), (0x8022, new byte[5]), (0x0020, StunAddr(pub, true))), tx);
        Line($"STUN: xor {xorOk}, mapped {plainOk}, both {both}");
        if (!pub.Equals(xorOk) || !pub.Equals(plainOk) || !pub.Equals(both))
            Fail("STUN didn't read the mapped address (XOR preferred, an unknown attribute skipped)");
        var wrongTx = (byte[])tx.Clone();
        wrongTx[3] ^= 1;
        if (Stun.ParseResponse(StunResponse(wrongTx, (0x0020, StunAddr(pub, true))), tx) != null)
            Fail("STUN accepted another transaction's answer");
        var badCookie = StunResponse(tx, (0x0020, StunAddr(pub, true)));
        badCookie[5] ^= 0xFF;
        var cut = StunResponse(tx, (0x0020, StunAddr(pub, true)))[..26];
        var v6 = StunAddr(pub, true);
        v6[1] = 2;
        var lyingLength = StunResponse(tx, (0x0020, StunAddr(pub, true)));
        lyingLength[3] = 200;
        var request = Stun.BuildRequest(tx);
        foreach (var (name, msg) in new[] { ("bad cookie", badCookie), ("cut short", cut), ("IPv6 only", StunResponse(tx, (0x0020, v6))), ("lying length", lyingLength), ("a request", request) })
            if (Stun.ParseResponse(msg, tx) != null)
                Fail($"STUN accepted a response that is {name}");
        // Fuzz: random bytes and mutations of a good answer never throw.
        var rng = new Random(77);
        var good = StunResponse(tx, (0x0020, StunAddr(pub, true)));
        try
        {
            for (var i = 0; i < 20000; i++)
            {
                byte[] m;
                if (i % 2 == 0)
                {
                    m = new byte[rng.Next(0, 80)];
                    rng.NextBytes(m);
                }
                else
                {
                    m = (byte[])good.Clone();
                    m[rng.Next(m.Length)] = (byte)rng.Next(256);
                    if (rng.Next(4) == 0)
                        m = m[..rng.Next(m.Length)];
                }
                Stun.ParseResponse(m, tx);
            }
        }
        catch (Exception ex)
        {
            Fail($"STUN parse threw on fuzz: {ex.GetType().Name}: {ex.Message}");
        }

        // A fake STUN server on loopback, and a silent one: the first answer wins; silence is null in time.
        using var fake = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        using var silent = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var fakePort = ((IPEndPoint)fake.Client.LocalEndPoint!).Port;
        var silentPort = ((IPEndPoint)silent.Client.LocalEndPoint!).Port;
        var server = Task.Run(async () =>
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var r = await fake.ReceiveAsync(cts.Token);
            var answer = StunResponse(r.Buffer[8..20], (0x0020, StunAddr(pub, true)));
            await fake.SendAsync(answer, r.RemoteEndPoint, cts.Token);
        });
        var sw = Stopwatch.StartNew();
        var got = Stun.QueryAsync([("127.0.0.1", silentPort), ("127.0.0.1", fakePort)], TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
        var t1 = sw.ElapsedMilliseconds;
        sw.Restart();
        var none = Stun.QueryAsync([("127.0.0.1", silentPort)], TimeSpan.FromMilliseconds(600)).GetAwaiter().GetResult();
        var t2 = sw.ElapsedMilliseconds;
        Line($"STUN on loopback: fake server -> {got} in {t1} ms; silent server -> {(none?.ToString() ?? "null")} in {t2} ms");
        if (!pub.Equals(got))
            Fail("STUN client didn't get the fake server's answer");
        if (none != null || t2 > 2500)
            Fail("STUN client didn't give up on a silent server in time");
        try
        {
            server.Wait(1000);
        }
        catch (Exception)
        {
            // the fake already did its job (or the query timed out, which failed above)
        }
    }

    private static void CheckServerListBasics()
    {
        // Server info extensions: this hosting's id and its list, with and without a rotation; older info still reads.
        var info = new ServerInfo("Friends", "Harbor", 2, 8, 1, MatchPhase.Lobby, 27015, ServerInfo.ProtocolVersion, GameMode.FreeForAll, PhysicsMode.Classic, "", 0xDEADBEEF, "", 27050);
        var info2 = info with { Rotation = "True Random", ListHost = "list.example.com", ListPort = 27051 };
        var old = info with { InstanceId = 0, ListPort = 0 };
        foreach (var i in new[] { info, info2, old })
            if (ServerInfo.Find(new byte[] { 0x1C, 1, 2, 3 }.Concat(i.Encode()).ToArray()) != i)
                Fail($"server info didn't round-trip: {i}");
        var enc = info2.Encode();
        var rng = new Random(5);
        try
        {
            for (var n = 0; n <= enc.Length; n++)
                ServerInfo.Find(enc.AsSpan(0, n)); // every truncation
            for (var k = 0; k < 5000; k++)
            {
                var m = (byte[])enc.Clone();
                m[4 + rng.Next(m.Length - 4)] = (byte)rng.Next(256);
                if (ServerInfo.Find(m) is { } f && (f.ListHost.Any(ch => ch is < ' ' or > '~') || f.Name.Any(ch => ch is < ' ' or > '~')))
                    Fail("fuzzed server info gave unprintable text");
            }
        }
        catch (Exception ex)
        {
            Fail($"server info parse threw on fuzz: {ex.GetType().Name}");
        }
        Line($"server info with id + list: {enc.Length} bytes ({old.Encode().Length} without)");

        // List replies: the list server's rank and id ride after the entries; cut-off replies never throw.
        var reply = new List<byte> { (byte)DefaultMessageIDTypes.AdvertiseSystem };
        reply.AddRange("COLS"u8.ToArray());
        reply.AddRange([1, 9]);
        reply.AddRange("127.0.0.1"u8.ToArray());
        reply.AddRange([0x87, 0x69, (byte)'L', (byte)'S', (byte)'I', (byte)'D', 1, 4, 3, 2, 1]);
        var parsed = MasterServerApp.ParseListFull(reply.ToArray(), out var rank, out var id);
        if (parsed is not [("127.0.0.1", 27015)] || rank != 1 || id != 0x01020304)
            Fail($"list reply with id: {parsed?.Count} entries, rank {rank}, id {id:x8}");
        for (var n = 0; n <= reply.Count; n++)
            MasterServerApp.ParseListFull(reply.Take(n).ToArray(), out _, out _);

        // Who gets which address for a game on the list's own PC or LAN.
        var table = new[]
        {
            ("127.0.0.1", "127.0.0.1", "127.0.0.1"), ("127.0.0.1", "192.168.1.7", "192.168.1.5"), ("127.0.0.1", "8.8.4.4", "203.0.113.45"),
            ("192.168.1.9", "8.8.4.4", "203.0.113.45"), ("192.168.1.9", "192.168.1.7", "192.168.1.9"), ("198.51.100.3", "8.8.4.4", "198.51.100.3"),
        };
        foreach (var (entry, querier, want) in table)
            if (MasterServerApp.AddressFor(entry, querier, "203.0.113.45", "192.168.1.5") != want)
                Fail($"list address for a game at {entry} asked by {querier}: {MasterServerApp.AddressFor(entry, querier, "203.0.113.45", "192.168.1.5")}, want {want}");
        if (MasterServerApp.AddressFor("127.0.0.1", "8.8.4.4", null, null) != "127.0.0.1")
            Fail("unknown public address should leave the entry as it is");

        // Settings: several lists, learning, the cap, the shipped file, persistence and the old advertise key.
        var parsedLists = Settings.ParseMasters("a.example.com, b.example.com:27051; a.example.com ,:5, c:0, fe80::1, d:65000");
        if (parsedLists.Count != 3 || parsedLists[1] != ("b.example.com", 27051) || parsedLists[2] != ("d", 65000))
            Fail($"server lists parsed as {string.Join(" | ", parsedLists)}");
        var s = new Settings { MasterServer = "a.example.com" };
        if (!s.LearnMaster("203.0.113.45:27050") || s.LearnMaster("203.0.113.45") || s.Masters.Count != 2)
            Fail($"learning a list: '{s.MasterServer}'");
        for (var i = 0; i < 12; i++)
            s.LearnMaster($"10.0.0.{i}:27050");
        if (s.Masters.Count != Settings.MaxMasters || s.Masters[^1].Host != "10.0.0.11")
            Fail($"learned lists aren't capped at {Settings.MaxMasters} keeping the newest: '{s.MasterServer}'");
        var dir = Path.Combine(Path.GetTempPath(), $"callofslop-lists-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var oldShipped = Settings.ShippedMastersPath;
        try
        {
            var file = Path.Combine(dir, "settings.txt");
            var a = new Settings { MasterServer = "x.example.com, 198.51.100.1:27052", ShareOnLists = false, AutoListServer = false, RunListServer = true, UseStun = false };
            a.Save(file);
            var b = Settings.Load(file);
            if (b.MasterServer != a.MasterServer || b.ShareOnLists || b.AutoListServer || !b.RunListServer || b.UseStun)
                Fail($"server list settings didn't persist: '{b.MasterServer}' share {b.ShareOnLists} auto {b.AutoListServer} run {b.RunListServer} stun {b.UseStun}");
            File.WriteAllLines(file, ["name=Old", "advertise=0", "master=203.0.113.9"]);
            var c = Settings.Load(file);
            if (!c.ShareOnLists || !c.AutoListServer || c.RunListServer || c.Masters is not [("203.0.113.9", 27050)])
                Fail("an old settings file didn't load with the new defaults and its master kept");
            Settings.ShippedMastersPath = Path.Combine(dir, "masters.txt");
            File.WriteAllLines(Settings.ShippedMastersPath, ["# community lists", "list.example.org  # the group's", "", "198.51.100.77:27060"]);
            var all = new Settings { MasterServer = "list.example.org" }.AllMasters;
            if (Settings.ShippedMasters.Count != 2 || all.Count(m => m.Host == "list.example.org") != 1 || !all.Contains(("198.51.100.77", 27060)))
                Fail($"masters.txt: {string.Join(", ", Settings.ShippedMasters)}; all {string.Join(", ", all)}");
            Line($"server lists: parse, learn (cap {Settings.MaxMasters}), masters.txt ({Settings.ShippedMasters.Count}), settings round trip OK");
        }
        finally
        {
            Settings.ShippedMastersPath = oldShipped;
            try
            {
                Directory.Delete(dir, true);
            }
            catch (Exception)
            {
                // temp
            }
        }

        // The browser's merge: one row per hosting, every source kept, other builds last but shown.
        ServerBrowser.Entry E(string addr, uint inst, ServerSource src, int ping, byte ver = ServerInfo.ProtocolVersion) => new()
        {
            Address = addr, Info = info with { InstanceId = inst, Version = ver }, PingMs = ping, Sources = src,
        };
        var rows = ServerBrowser.Merge([
            E("192.168.1.5", 7, ServerSource.Lan, 3), E("203.0.113.45", 7, ServerSource.List, 40), E("127.0.0.1", 7, ServerSource.List, 1),
            E("198.51.100.2", 8, ServerSource.List, 60), E("198.51.100.3", 0, ServerSource.List, 70), E("198.51.100.4", 0, ServerSource.List, 20, 1),
        ]);
        Line($"browser merge: {string.Join(", ", rows.Select(r => $"{r.Address} {r.Sources} {r.PingMs} ms v{r.Info.Version}"))}");
        if (rows.Count != 4 || rows[0].Address != "127.0.0.1" || rows[0].Sources != (ServerSource.Lan | ServerSource.List))
            Fail("the same game found on the LAN and on lists isn't one row with both sources");
        if (rows[^1].VersionOk || rows[^1].Info.Version != 1)
            Fail("a game from another build isn't shown last");
    }

    /// <summary>A fake host game for the list scenario: a game socket answering pings and sending heartbeats.</summary>
    private sealed class ListTestHost(ushort port, HostListing listing) : IDisposable
    {
        public readonly NetPeer Net = NetPeer.Host(port, 2);
        public readonly HostListing Listing = listing;
        private readonly Heartbeats _beats = new();
        public readonly uint Id = (uint)Random.Shared.Next(1, int.MaxValue);

        public void Tick(double now)
        {
            Listing.Tick(now);
            Net.SetOfflineInfo(new ServerInfo($"Host {port}", "Harbor", 1, 8, 0, MatchPhase.Lobby, port, ServerInfo.ProtocolVersion,
                InstanceId: Id, ListHost: Listing.ListHost, ListPort: Listing.ListPort).Encode());
            _beats.Tick(Net, port, Listing.Targets, now, 0.5);
            Net.Poll((_, _) => { });
        }

        public void Dispose()
        {
            Listing.Dispose();
            Net.Dispose();
        }
    }

    private static void CheckAutoServerList()
    {
        const ushort listBase = 60150, gameA = 60140, gameB = 60141, manualPort = 60165;
        var clock = Stopwatch.StartNew();
        HostListing.Config Cfg() => new() { ProbeTimeout = TimeSpan.FromMilliseconds(400), ReprobeSeconds = 1, FirstListPort = listBase, ListPorts = 5, Log = Line };
        bool Wait(Func<bool> done, double seconds, params ListTestHost?[] hosts)
        {
            var until = clock.Elapsed.TotalSeconds + seconds;
            while (clock.Elapsed.TotalSeconds < until)
            {
                foreach (var h in hosts)
                    h?.Tick(clock.Elapsed.TotalSeconds);
                if (done())
                    return true;
                Thread.Sleep(10);
            }
            return false;
        }

        var bLists = new List<(string Host, ushort Port)>();
        ListTestHost? a = null, b = null;
        ListServer? manual = null;
        try
        {
            // 1. A host with no list that answers starts one and registers its own game on it.
            a = new ListTestHost(gameA, new HostListing(() => [], Cfg()));
            if (!Wait(() => a.Listing.OwnList is { GameCount: 1 }, 5, a))
            {
                Fail($"a host with no list didn't start one and register ({a.Listing.Status}; games {a.Listing.OwnList?.GameCount})");
                return;
            }
            var aList = a.Listing.OwnList!.Port;
            Line($"host A: {a.Listing.Status} (after {clock.ElapsedMilliseconds} ms)");
            var probe = ListProbe.Run([("127.0.0.1", aList)], TimeSpan.FromSeconds(1));
            if (probe is not [{ Rank: 1, Games: 1 }])
                Fail($"A's list didn't answer a probe with one game (rank 1): {string.Join(", ", probe)}");

            // 2. A second host that knows A's list registers there instead of starting its own.
            bLists.Add(("127.0.0.1", aList));
            b = new ListTestHost(gameB, new HostListing(() => bLists, Cfg()));
            if (!Wait(() => b.Listing.Probes >= 1 && a.Listing.OwnList is { GameCount: 2 }, 5, a, b) || b.Listing.OwnList != null)
                Fail($"host B didn't register on A's list (B: {b.Listing.Status}; A's games {a.Listing.OwnList?.GameCount})");
            else
                Line($"host B: {b.Listing.Status}");

            // 3. A client joining A by invite code learns A's list; so does one that just pings A (LAN / address joins).
            var code = InviteCode.Encode(new Invite(IPAddress.Loopback, gameA, ServerInfo.ProtocolVersion, aList));
            var client = new Settings();
            if (!InviteCode.TryParseJoin(code, 27015, out _, out _, out var inv, out var err) || inv is not { } invite || !client.LearnMaster(ListLearner.FromInvite(invite)))
                Fail($"joining by code didn't learn the list ({err})");
            var probeTask = ListLearner.ProbeHostAsync("127.0.0.1", gameA, TimeSpan.FromSeconds(2));
            Wait(() => probeTask.IsCompleted, 3, a, b);
            var fromHost = probeTask.IsCompletedSuccessfully ? probeTask.Result : null;
            Line($"client: learned '{client.MasterServer}' from the code, '{fromHost}' from A's server info");
            if (client.Masters is not [("127.0.0.1", var learnedPort)] || learnedPort != aList || fromHost != $"127.0.0.1:{aList}")
                Fail("the client didn't learn A's list address");
            // ...and the browser sees both hosts through it.
            using (var browser = NetPeer.Discovery())
            {
                browser.Advertise("127.0.0.1", aList, MasterServerApp.ListQuery);
                List<(string Address, ushort Port)>? listed = null;
                Wait(() =>
                {
                    browser.PollRaw((_, data) =>
                    {
                        if ((DefaultMessageIDTypes)data[0] == DefaultMessageIDTypes.AdvertiseSystem)
                            listed = MasterServerApp.ParseList(data);
                    });
                    return listed != null;
                }, 2, a, b);
                if (listed == null || !listed.Contains(("127.0.0.1", gameA)) || !listed.Contains(("127.0.0.1", gameB)))
                    Fail($"A's list doesn't show both hosts: {(listed == null ? "no reply" : string.Join(", ", listed))}");
            }

            // 4. A quits: its list stops; B finds no list at its next check and starts its own.
            a.Dispose();
            a = null;
            if (!Wait(() => b.Listing.OwnList is { GameCount: 1 }, 6, b))
                Fail($"after A quit, B didn't start its own list ({b.Listing.Status})");
            else
                Line($"after A quit: B {b.Listing.Status}");

            // 5. A better list appears (hand-started, rank 0): B moves there and stops its own.
            manual = ListServer.TryStart(manualPort, 1, 0, false, Line, out var manualErr);
            if (manual == null)
                Fail($"couldn't start a hand-started list for the hand-over test: {manualErr}");
            else
            {
                bLists.Add(("127.0.0.1", manualPort));
                if (!Wait(() => b.Listing.OwnList == null && manual.GameCount == 1, 6, b))
                    Fail($"B didn't hand over to the better list ({b.Listing.Status}; manual has {manual.GameCount})");
                else
                    Line($"hand-over: B {b.Listing.Status}; lists running in this process: {ListServer.Running.Count}");
            }
        }
        finally
        {
            a?.Dispose();
            b?.Dispose();
            manual?.Dispose();
        }
        if (ListServer.Running.Count != 0)
            Fail($"{ListServer.Running.Count} list server(s) still running after the hosts quit");
    }
}
