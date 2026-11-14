using System.Diagnostics;
using System.Numerics;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// The board's stuck stub: a piece too short for a stroke sat in a press bay and nothing got it out, since the crosshair lands on the piece,
    /// not on the machine. Through the player's input (Program.SweepFrame) on a real client and host: F and the physics gun take what a press,
    /// an Extruder or a Wheel Former holds, in every state, whole; a refused piece stays out; a piece taken out isn't drawn back in.
    /// </summary>
    private static void CheckPressStock()
    {
        if (Maps.Find(ConstructYardMap) is not { } yard || yard.Sandbox is not { } sb)
        {
            Fail("press stock: no Construct Yard");
            return;
        }
        const ushort port = 26477;
        var keep = sb.InfiniteParts;
        var clock = Stopwatch.StartNew();
        var now = 0.0;
        yard.RestoreProps();
        World.SetMap(yard);
        sb.InfiniteParts = false;
        var dropped = NetSafety.Dropped;
        var ctx = ImGui.CreateContext();
        var io = ImGui.GetIO();
        io.IniFilename = default;
        io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset | ImGuiBackendFlags.RendererHasTextures;
        io.DisplaySize = new float2(1920, 1080);
        io.DisplayFramebufferScale = new float2(1, 1);
        Ui.SafeErrors(io);
        Program.SandboxGuardRethrow = true;
        Game.SandboxActRethrow = true;
        try
        {
            using var server = new Game(NetPeer.Host(port, 4), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: yard.Id) { MapVoteEnabled = false };
            server.Mode = GameMode.Sandbox;
            using var a = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Alpha", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
            Pump(server, [a], 1.2, ref now, clock);
            for (var wait = 0; wait < 40 && server.Phase != MatchPhase.Playing; wait++)
                Pump(server, [a], 0.25, ref now, clock);
            Pump(server, [a], 0.3, ref now, clock);
            if (a.LocalId == byte.MaxValue || !server.Players.TryGetValue(a.LocalId, out var sp) || server.Local == null)
            {
                Fail("press stock: the client didn't join");
                return;
            }
            if (!sp.Alive)
                server.SelfTestRespawn(sp, now);
            if (!server.Local.Alive)
                server.SelfTestRespawn(server.Local, now);
            Pump(server, [a], 0.4, ref now, clock);
            sp.ImmuneUntil = server.Local.ImmuneUntil = 1e9;
            server.Net.SimulateBadNetwork(30, 8, 2);
            a.Net.SimulateBadNetwork(30, 8, 2);
            var w = server.Constructs;
            if (OpenSpot(w) is not { } spot)
            {
                Fail("press stock: no open ground");
                return;
            }
            var bp = ConstructBuild.Blueprints.First(b => b.Name == "Press Line");
            if (ConstructBuild.BuildOnGround(w, bp, ConstructWorld.Ground(World.Query, spot.X, spot.Z), 0, out var why) is not { } line)
            {
                Fail($"press stock: the Press Line: {why}");
                return;
            }
            line.Owner = 255;
            line.Rebuild();
            var strip = RecyclingTable.Current.IdOf("laminate");
            var plate = Extrusion.Presets[1].Mask;
            bool Await(Func<bool> done, double seconds = 3)
            {
                // Waits for <seconds> of simulation, not of wall clock: each update moves at most 0.1 s of game time, so under load (other
                // sections, other builds) 3 s of wall clock could be well under 3 s of game and the client's resend not in yet (leftovers 3).
                // The wall cap only stops a hang.
                var sim = 0.0;
                var cap = clock.Elapsed.TotalSeconds + seconds * 20 + 10;
                while (!done() && sim < seconds && clock.Elapsed.TotalSeconds < cap)
                {
                    var t = clock.Elapsed.TotalSeconds;
                    var dt = Math.Clamp(t - now, 0.001, 0.1);
                    now = t;
                    server.Update(now, dt);
                    a.Update(now, dt);
                    sim += dt;
                    Thread.Sleep(2);
                }
                return done();
            }
            void Tap(Game g, GameAction key)
            {
                Program.SweepRelease(g);
                Program.SweepFrame(g, now, []);
                Program.SweepFrame(g, now, [key]);
                Program.SweepFrame(g, now, []);
            }
            void Tool(Game g, ConstructTool tool)
            {
                g.SandboxSend(CActKind.Tool, part: (byte)tool);
                Await(() => server.SandboxHands.Hand(g.LocalId).Tool == tool && g.SandboxHands.Hand(g.LocalId).Tool == tool);
            }
            void Clear(Func<Construct, bool>? also = null)
            {
                foreach (var b in w.Bodies.ToArray())
                    if (b.PartCount == 1 && b.Parts[0].D.IsExtrusion && (also == null || also(b)))
                        w.Remove(b);
            }
            /// A peer's player where its crosshair lands on the part, or false.
            bool AimAt(Game g, Construct hostBody, int part, float reach)
            {
                var pl = g.Local!;
                var spl = server.Players[pl.Id];
                var mine = g.Constructs.Get(hostBody.Id);
                if (mine == null || part >= mine.PartCount)
                    return false;
                mine.Rebuild();
                var at = ConstructRender.PartCentre(mine, part);
                for (var k = 0; k < 16; k++)
                {
                    var side = new Vector3(MathF.Cos(k * MathF.PI / 8), 0, MathF.Sin(k * MathF.PI / 8));
                    foreach (var dist in (float[])[1.4f, 2f, 0.9f])
                    {
                        var from = at + side * dist;
                        var feet = ConstructWorld.Ground(World.Query, from.X, from.Z) + new Vector3(0, 0.05f, 0);
                        var guess = feet + new Vector3(0, 1.6f, 0);
                        if ((at - guess).Length() > reach || !g.Constructs.Raycast(guess, Vector3.Normalize(at - guess), reach, out var first, look: true, noTracks: true) || first.Body.Id != mine.Id || first.Part != part)
                            continue;
                        spl.Teleport(feet);
                        pl.Teleport(feet);
                        Pump(server, [a], 0.15, ref now, clock);
                        var eye = pl.RenderEye;
                        var dir = Vector3.Normalize(at - eye);
                        g.LookPitch = MathF.Asin(Math.Clamp(dir.Y, -1f, 1f));
                        g.LookYaw = MathF.Atan2(dir.X, -dir.Z);
                        if (g.Constructs.Raycast(eye, g.LookDir(g.LookYaw, g.LookPitch), reach, out var hit, look: true, noTracks: true) && hit.Body.Id == mine.Id && hit.Part == part)
                            return true;
                    }
                }
                return false;
            }
            Construct? LooseOf(int cm, byte material) =>
                w.Bodies.FirstOrDefault(b => b.PartCount == 1 && b.Parts[0].D is { IsExtrusion: true, ExKey.Growing: false } d && d.ExKey.L == cm && d.ExKey.Material == material);
            int Press() => PartNamed(line, "Press");
            void Die(string? name)
            {
                if (ConstructPress.DieIn(line, Press()) is var old and >= 0)
                    w.RemovePart(line, old);
                line.Rebuild();
                if (name != null)
                    ConstructPress.FitDie(w, line, Press(), ConstructParts.Need(name));
                line.Rebuild();
            }
            /// A piece in bay 0, out by this peer's key; it must come out whole on both peers.
            void Case(string name, int cm, Game peer, bool gun = false, Action? state = null)
            {
                Clear();
                var pi = Press();
                if (ConstructPress.PieceIn(line, pi, 0) is var was and >= 0)
                    w.RemovePart(line, was);
                line.Rebuild();
                pi = Press();
                var key = new ExtrusionKey(plate, 30, 30, (ushort)cm, strip, false);
                if (!ConstructPress.FitPiece(w, line, pi, 0, key, 0.7f, 0x123456))
                {
                    Fail($"press stock: {name}: the piece didn't fit");
                    return;
                }
                line.Rebuild();
                var piece = ConstructPress.PieceIn(line, pi, 0);
                var count = line.PartCount;
                state?.Invoke();
                Pump(server, [a], 0.2, ref now, clock);
                if (!Await(() => a.Constructs.Get(line.Id) is { } cc && cc.PartCount == line.PartCount && ConstructPress.PieceIn(cc, PartNamed(cc, "Press"), 0) >= 0))
                {
                    Fail($"press stock: {name}: the client never saw the piece");
                    return;
                }
                pi = Press();
                piece = ConstructPress.PieceIn(line, pi, 0);
                var health = line.Parts[piece].Health / line.Parts[piece].D.Health;
                if (ConstructPress.MachineOf(line, piece) != pi || ConstructPress.MachineOf(a.Constructs.Get(line.Id)!, piece) != pi)
                    Fail($"press stock: {name}: the press doesn't hold the piece on both peers");
                Tool(peer, gun ? ConstructTool.PhysicsGun : ConstructTool.Hands);
                if (!AimAt(peer, line, piece, gun ? 6 : ConstructBuild.UseReach))
                {
                    Fail($"press stock: {name}: no spot where the crosshair finds the piece");
                    return;
                }
                if (gun)
                {
                    Program.SweepRelease(peer);
                    Program.SweepFrame(peer, now, []);
                    Program.SweepFrame(peer, now, [GameAction.Fire]);
                }
                else
                    Tap(peer, GameAction.Interact);
                var taken = Await(() =>
                {
                    if (gun)
                        Program.SweepFrame(peer, now, [GameAction.Fire]); // (the gun holds while the button does)
                    return ConstructPress.PieceIn(line, Press(), 0) < 0 && LooseOf(cm, strip) is { } out0 && (!gun || out0.GrabbedBy == peer.LocalId) && a.Constructs.Get(line.Id)?.PartCount == line.PartCount;
                });
                var body = LooseOf(cm, strip);
                Line($"press stock: {name}: {(gun ? "physics gun" : "F")} on the {(peer == server ? "host" : "client")}: out {taken}, \"{peer.SandboxNotice}\", held by {body?.GrabbedBy}");
                if (!taken || body == null)
                {
                    Fail($"press stock: {name}: the piece didn't come out (\"{peer.SandboxNotice}\")");
                    return;
                }
                if (!Await(() => a.Constructs.Get(body.Id) != null))
                    Fail($"press stock: {name}: the client has no copy of the piece");
                var share = body.Parts[0].Health / body.Parts[0].D.Health;
                if (MathF.Abs(share - health) > 0.01f || body.Parts[0].Paint != 0x123456 || body.Parts[0].D.ExKey.W != 30)
                    Fail($"press stock: {name}: it came out changed (health {share} from {health}, paint {body.Parts[0].Paint:x})");
                if (gun && body.GrabbedBy != peer.LocalId)
                    Fail($"press stock: {name}: the physics gun drew it out but doesn't hold it (held by {body.GrabbedBy})");
                if (gun)
                {
                    Program.SweepRelease(peer);
                    Program.SweepFrame(peer, now, []);
                    Await(() => body.GrabbedBy == 255);
                }
            }
            Die("Button Die");
            Case("too short", 5, a);
            Case("too short", 5, a, gun: true);
            Case("too short, the host", 5, server);
            Case("wrong die", 50, a, state: () => Die("Gear Die"));
            Die(null);
            Case("no die", 50, a);
            Case("no die", 50, a, gun: true);
            Die("Button Die");
            // Part-way through a run: a blank in bay 1, the kit in bay 2 and a stroke under way. It stops, and nothing is used: a stroke takes its blank and kit only when it ends.
            var made = 0;
            Case("part-way", 50, a, state: () =>
            {
                ConstructPress.FeedKitMaterial(w, line, Press(), 10, RecyclingTable.Current.IdOf("copper")); // (bay 2 is the kit hopper: the Button die's copper)
                line.Rebuild();
                if (!Await(() => w.StoreOf(line, Press())!.Busy))
                    Fail($"press stock: part-way: the press never started ({ConstructPress.Look(line, Press(), w.StoreOf(line, Press())!, false, null, default)})");
                made = w.StoreOf(line, Press())!.Items;
            });
            Await(() => !w.StoreOf(line, Press())!.Busy, 1); // (the press looks at 10 Hz)
            if (w.StoreOf(line, Press())!.Items != made || w.StoreOf(line, Press())!.Busy)
                Fail($"press stock: part-way: taking a piece out left a stroke running or made something ({w.StoreOf(line, Press())!.Items} from {made})");
            // A loose piece out of the die's sizes (too short) in the mouth is refused, and stays out where it is for the physics gun.
            Clear();
            var p0 = Press();
            line.WorldFrame(0, out var fq, out var ft);
            var mouth = ft + Vector3.Transform(line.Parts[p0].Pos + Rot24.Apply(line.Parts[p0].Rot, (line.DefOf(p0).PressBays[0].Min + line.DefOf(p0).PressBays[0].Max) * 0.5f), fq);
            var key2 = new ExtrusionKey(plate, 30, 30, 5, strip, false);
            w.StoreOf(line, p0)!.JobMaterial = 0; // (the last take-out's wait is over)
            var stub = ConstructEdits.DropLoose(w, Extrusion.Def(key2)!, mouth, line.Rot);
            if (stub == null)
                Fail("press stock: no loose stub");
            else
            {
                stub.Parts[0].Paint = 7;
                if (!Await(() => w.StoreOf(line, Press())!.Blocked >> 2 == (int)BayRefuse.Bounds, 6) || ConstructPress.PieceIn(line, Press(), 0) >= 0 || w.Get(stub.Id) == null)
                    Fail($"press stock: a piece out of the die's sizes in the mouth wasn't refused (refusal {w.StoreOf(line, Press())!.Blocked >> 2}, in the bay {ConstructPress.PieceIn(line, Press(), 0)}, at {stub.Pos} mouth {mouth})");
            }
            // A piece taken out isn't drawn back in while it lies in the mouth, however long it waits; picked up and put back, it goes in.
            Case("taken out", 50, a);
            var back = LooseOf(50, strip);
            if (back != null)
            {
                var st = w.StoreOf(line, Press())!;
                st.JobMaterial = 0;
                Pump(server, [a], 0.5, ref now, clock);
                if (ConstructPress.PieceIn(line, Press(), 0) >= 0)
                    Fail("press stock: a piece taken out was drawn straight back in");
            }
            // Screens: nothing runs off, in any of the states above.
            var card = new PanelCard();
            var slotClass = line.DefOf(Press()).Sockets[line.DefOf(Press()).PressDieSocket].Class;
            foreach (var die in ConstructParts.All.Where(x => x.IsPressDie && ConstructPress.DieClassOf(x) == slotClass).ToArray())
            {
                Die(die.Name);
                var d = line.DefOf(Press());
                for (var n = 0; n < d.Panels.Length; n++)
                {
                    var panel = d.Panels[n];
                    card.Clear();
                    panel.Fill!(w, line, Press(), panel, card);
                    var room = PanelRender.Columns(panel.Size, panel.Rows);
                    for (var l = 0; l < panel.Rows; l++)
                        if (card.Line(l).Length > (card.HasIcon && l is 1 or 2 ? room - 6 : room))
                            Fail($"press stock: {die.Name}: \"{new string(card.Line(l))}\" runs off the {d.Name}'s screen ({room} characters)");
                }
            }
            // Unpowered: the battery goes.
            Die("Button Die");
            Case("unpowered", 50, a, state: () =>
            {
                w.RemovePart(line, PartNamed(line, "Battery"));
                line.Rebuild();
            });
            // An Extruder's die and a Wheel Former's hub: the same trap, with no power and no Cut wire.
            foreach (var name in (string[])["Extruder", "Wheel Former"])
            {
                Clear();
                w.Remove(line);
                var mbp = ConstructBuild.ParseBlueprint($$"""
                    { "format": "call-of-slop-construct-blueprint/1", "name": "Stock Stand", "steps": [
                      { "part": "Concrete Foundation 4x4 m" },
                      { "part": "{{name}}", "on": 0, "socket": "t7_7", "by": "bottom" },
                      { "part": "Battery", "on": 0, "socket": "t15_8", "by": "-y" }
                    ] }
                    """, "stock-stand.json");
                if (ConstructBuild.BuildOnGround(w, mbp, ConstructWorld.Ground(World.Query, spot.X, spot.Z), 0, out var mwhy) is not { } m)
                {
                    Fail($"press stock: no {name}: {mwhy}");
                    break;
                }
                m.Owner = 255;
                m.Rebuild();
                line = m;
                var mi = PartNamed(m, name);
                w.Deposit(m, mi, 50, RecyclingTable.Current.IdOf("metal"));
                var grew = Await(() => ConstructExtruder.ProductOf(m, mi) >= 0, 5);
                if (!grew)
                {
                    Fail($"press stock: the {name} made nothing");
                    continue;
                }
                w.RemovePart(m, PartNamed(m, "Battery")); // (the power goes, and there is no Cut wire)
                m.Rebuild();
                mi = PartNamed(m, name);
                var metal = RecyclingTable.Current.IdOf("metal");
                Pump(server, [a], 0.3, ref now, clock);
                var piece = ConstructExtruder.ProductOf(m, mi);
                // (wait for the client's copy to catch up, as Case does: AimAt looks the server's part index up in the client's
                // copy, and on a loaded machine 0.3 s can leave it without the piece or still with the battery, indices shifted)
                if (!Await(() => a.Constructs.Get(m.Id) is { } cm2 && cm2.PartCount == m.PartCount && ConstructExtruder.ProductOf(cm2, PartNamed(cm2, name)) == piece))
                {
                    Fail($"press stock: the client never saw the {name}'s piece");
                    w.Remove(m);
                    continue;
                }
                var cm = m.Parts[piece].D.ExKey.L;
                Tool(a, ConstructTool.Hands);
                if (!AimAt(a, m, piece, ConstructBuild.UseReach))
                {
                    Fail($"press stock: no spot where the crosshair finds the {name}'s piece");
                    continue;
                }
                Tap(a, GameAction.Interact);
                var off = Await(() => ConstructExtruder.ProductOf(m, PartNamed(m, name)) < 0 && w.Bodies.Any(b => b != m && b.PartCount == 1 && b.Parts[0].D is { IsExtrusion: true, ExKey.Growing: false } d && d.ExKey.Material == metal));
                Line($"press stock: the {name}'s piece ({cm} cm): off {off}, \"{a.SandboxNotice}\"");
                if (!off)
                    Fail($"press stock: F didn't take the piece off the {name} (\"{a.SandboxNotice}\")");
                w.Remove(m);
            }
            if (NetSafety.Dropped != dropped)
                Fail($"press stock: {NetSafety.Dropped - dropped} packets dropped");
        }
        finally
        {
            sb.InfiniteParts = keep;
            yard.RestoreProps();
            Program.SandboxGuardRethrow = false;
            Game.SandboxActRethrow = false;
            ImGui.DestroyContext(ctx);
        }
    }
}
