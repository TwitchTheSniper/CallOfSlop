using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// Browser Screens stay together: what leaves a PC of an address, and a real host and two clients, one a late joiner: the full address, the scroll and a page's own media reach everyone, the last to report leads,
    /// and a body isn't resent for it. The pages themselves are MediaDirector's and need a browser, so this stops at the wire.
    /// </summary>
    private static void CheckWebSync()
    {
        void Want(bool ok, string what)
        {
            if (!ok)
                Fail("web sync: " + what);
        }
        foreach (var (name, secret) in new (string, bool)[]
        {
            ("access_token", true), ("token", true), ("id_token", true), ("auth", true), ("authCode", true), ("session", true), ("sessionid", true), ("PHPSESSID", true), ("sid", true), ("api_key", true), ("apikey", true), ("key", true),
            ("code", true), ("sig", true), ("signature", true), ("password", true), ("state", true), ("jwt", true), ("X-Amz-Signature", true), ("csrf_token", true),
            ("q", false), ("v", false), ("t", false), ("list", false), ("search_query", false), ("page", false), ("author", false), ("keyword", false), ("country", false), ("side", false), ("lang", false), ("sort", false),
        })
            Want(WebScrub.SecretName(name) == secret, $"the name {name}");
        foreach (var (from, to) in new[]
        {
            ("https://a.com/x", "https://a.com/x"),
            ("https://www.youtube.com/watch?v=dQw4w9WgXcQ&t=30", "https://www.youtube.com/watch?v=dQw4w9WgXcQ&t=30"),
            ("https://a.com/s?q=cats&token=abc&page=2#top", "https://a.com/s?q=cats&page=2#top"),
            ("https://a.com/cb?code=abc&state=xyz", "https://a.com/cb"),
            ("https://a.com/#access_token=abc&view=1", "https://a.com/#view=1"),
            ("https://a.com/#/route/x", "https://a.com/#/route/x"),
            ("https://a.com/x?v=eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.abcdefghijklmnop", "https://a.com/x"),
        })
            Want(WebScrub.Clean(from) == to, $"{from} came to {WebScrub.Clean(from)}");
        Want(MediaEntry.FromAddress("https://a.com/?q=1&token=abc", out _) == "web:https://a.com/?q=1", "a typed address keeps its secrets on this PC");
        Want(MediaEntry.Clean("web:http://192.168.1.1/?q=1") == null, "a private address with a query");
        Want(MediaEntry.Clean("web:https://a.com/" + new string('a', 900)) != null, "a long address");

        if (Maps.Find(ConstructYardMap) is not { } yard || yard.Sandbox is not { } sb)
            return;
        const ushort port = 26503;
        var keep = sb.InfiniteParts;
        var clock = Stopwatch.StartNew();
        var now = 0.0;
        yard.RestoreProps();
        World.SetMap(yard);
        sb.InfiniteParts = false;
        try
        {
            using var server = new Game(NetPeer.Host(port, 4), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: yard.Id) { MapVoteEnabled = false };
            server.Mode = GameMode.Sandbox;
            using var a = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Alpha", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
            Pump(server, [a], 1.2, ref now, clock);
            for (var i = 0; i < 40 && server.Phase != MatchPhase.Playing; i++)
                Pump(server, [a], 0.25, ref now, clock);
            var sp = server.Players[a.LocalId];
            if (!sp.Alive)
                server.SelfTestRespawn(sp, now);
            if (server.Local is { Alive: false } host)
                server.SelfTestRespawn(host, now);
            Pump(server, [a], 0.3, ref now, clock);
            server.Net.SimulateBadNetwork(30, 8, 2);
            a.Net.SimulateBadNetwork(30, 8, 2);
            var w = server.Constructs;
            bool Await(Func<bool> done, params Game[] others)
            {
                var until = clock.Elapsed.TotalSeconds + 4;
                while (!done() && clock.Elapsed.TotalSeconds < until)
                    Pump(server, others.Length > 0 ? others : [a], 0.05, ref now, clock);
                return done();
            }
            var spot = OpenSpot(w)!.Value;
            var c = ConstructBuild.PlaceNew(w, ConstructParts.Need("Browser Screen"), new FreePlacement { Pos = spot + new Vector3(0, 1.2f, 0), Rot = Quaternion.Identity, Anchor = true })!;
            c.Owner = sp.Id;
            c.Rebuild();
            Want(Await(() => a.Constructs.Get(c.Id) != null), "the client's copy");
            sp.Teleport(ConstructWorld.Ground(World.Query, spot.X, spot.Z - 1) + new Vector3(0, 0.05f, 0));
            Pump(server, [a], 0.4, ref now, clock);
            CMedia? Copy() => a.Constructs.Get(c.Id)?.Parts[0].Media;
            const string address = "web:https://www.example.com/watch?v=abc&t=30#top";
            a.SandboxMedia(c.Id, 0, MediaOp.Go, entry: address);
            Want(Await(() => Copy() is { Entry: address } && Copy()!.Lead == a.LocalId), $"the whole address, and the lead ({a.SandboxNotice})");

            // The leader's page reports; the host and a late joiner hear it, and no body goes out for it.
            var stamp = c.StructSentTick;
            a.SandboxWebState(c.Id, 0, 32768, 1, 40);
            Want(Await(() => c.Parts[0].Media is { Scroll: 32768, Inner: 1 }), "the scroll and media reach the server");
            Want(Await(() => Copy() is { Scroll: 32768, Inner: 1 }), "the echo reaches the leader");
            Want(c.StructSentTick == stamp, "a body went out for a scroll");
            var m = c.Parts[0].Media!;
            Want(MathF.Abs(m.InnerPosition(m.InnerTick + 120) - 42) < 0.01f, "the clock runs while it plays");
            using var b = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Beta", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
            Want(Await(() => b.Constructs.Get(c.Id)?.Parts[0].Media is { Scroll: 32768, Inner: 1, Entry: address }, a, b), "a joiner gets the page's state");
            var late = b.Constructs.Get(c.Id)!.Parts[0].Media!;
            Want(MathF.Abs(late.InnerPosition(b.ServerTick) - m.InnerPosition(server.ServerTick)) < 1f, $"a joiner's clock: {late.InnerPosition(b.ServerTick)} against {m.InnerPosition(server.ServerTick)}");

            // Paused, the clock stands.
            a.SandboxWebState(c.Id, 0, 40000, 2, 50);
            Want(Await(() => c.Parts[0].Media is { Inner: 2 }, a, b), "a pause");
            Want(m.InnerPosition(m.InnerTick + 600) == 50, "the clock stands while paused");

            // Last input wins: the host takes over.
            server.SandboxWebState(c.Id, 0, 77, 2, 50);
            Pump(server, [a, b], 0.3, ref now, clock);
            Want(c.Parts[0].Media!.Scroll == 40000 && c.Parts[0].Media!.Lead == a.LocalId, "someone the owner hasn't let in took the lead");
            c.Owner = 255;
            Pump(server, [a, b], 0.2, ref now, clock);
            server.SandboxWebState(c.Id, 0, 100, 2, 50);
            Pump(server, [a, b], 0.3, ref now, clock);
            Want(Copy() is { Scroll: 100 } && Copy()!.Lead == server.LocalId, $"the host takes the lead ({Copy()?.Lead})");

            // The leader may walk off: its page's address still goes out, though a typed one wouldn't.
            sp.Teleport(sp.Position + new Vector3(40, 0, 40));
            Pump(server, [a, b], 0.3, ref now, clock);
            a.SandboxWebState(c.Id, 0, 200, 0, 0);
            Want(Await(() => c.Parts[0].Media is { Scroll: 200 }, a, b), "the leader's page, from far off");
            a.SandboxMedia(c.Id, 0, MediaOp.Go, entry: "web:https://www.example.com/next?a=1");
            Want(Await(() => c.Parts[0].Media is { Entry: "web:https://www.example.com/next?a=1" }, a, b), "the leader's link, from far off");
            server.SandboxWebState(c.Id, 0, 300, 0, 0);
            Pump(server, [a, b], 0.3, ref now, clock);
            a.SandboxMedia(c.Id, 0, MediaOp.Go, entry: "web:https://www.example.com/typed");
            Pump(server, [a, b], 0.5, ref now, clock);
            Want(c.Parts[0].Media!.Entry == "web:https://www.example.com/next?a=1", "someone not leading can't send it anywhere from far off");

            // Someone the owner hasn't let in: nothing.
            c.Owner = server.LocalId;
            sb.InfiniteParts = false;
            var was = c.Parts[0].Media!.Scroll;
            var wasLead = c.Parts[0].Media!.Lead;
            b.SandboxWebState(c.Id, 0, 999, 0, 0);
            Pump(server, [a, b], 0.4, ref now, clock);
            if (server.SandboxConfigRefusal(c, b.LocalId) != null)
                Want(c.Parts[0].Media!.Scroll == was && c.Parts[0].Media!.Lead == wasLead, "a stranger's page reported");

            // Rate: a flood from one peer is cut to ten a second.
            var flood = new CMedia { Lead = 3 };
            Want(ConstructMedia.WebState(flood, 3, 1, 0, 0, 1000) && !ConstructMedia.WebState(flood, 3, 2, 0, 0, 1001) && ConstructMedia.WebState(flood, 3, 3, 0, 0, 1007), "the rate limit");
            Want(!ConstructMedia.WebState(flood, 3, 1, 3, 0, 2000) && !ConstructMedia.WebState(flood, 3, 1, 1, float.NaN, 3000), "a bad report");
        }
        finally
        {
            sb.InfiniteParts = keep;
            yard.RestoreProps();
        }
    }
}
