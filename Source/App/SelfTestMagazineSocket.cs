using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>"Magazine sockets": a magazine clipped onto an Ammo Socket, through a real host and client with lag and loss: the player's whole chain (fill at a box, the held-part path, fire, empty, take off, refill, swap), a socket fed through a link, a merged gun, the order of the sources, the refusal, and a magazine's rounds put away and drawn again.</summary>
public static partial class SelfTest
{
    private static void CheckMagazineSockets()
    {
        var yard = Maps.Find("Proving Ground")!;
        const ushort port = 26397;
        var clock = Stopwatch.StartNew();
        var now = 0.0;
        yard.RestoreProps();
        World.SetMap(yard);
        using var server = new Game(NetPeer.Host(port, 4), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: yard.Id) { MapVoteEnabled = false };
        server.Mode = GameMode.Sandbox;
        server.SetSandboxSurvival(true); // (magazines come empty and parts are counted, as in the early game)
        using var a = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Alpha", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        Pump(server, [a], 1.0, ref now, clock);
        for (var wait = 0; wait < 40 && server.Phase != MatchPhase.Playing; wait++)
            PumpFast(server, [a], 0.25, ref now);
        Pump(server, [a], 0.5, ref now, clock);
        if (!server.Players.TryGetValue(a.LocalId, out var sp))
        {
            Fail("magazine sockets: the client never joined");
            return;
        }
        var w = server.Constructs;
        var hands = server.SandboxHands;
        while (w.Bodies.Count > 0)
            w.Remove(w.Bodies[^1]);
        server.Net.SimulateBadNetwork(80, 15, 4);
        a.Net.SimulateBadNetwork(80, 15, 4);
        var problems = new List<string>();
        bool Until(Func<bool> done, double seconds, string what)
        {
            var end = now + seconds;
            while (now < end)
            {
                if (done())
                    return true;
                Pump(server, [a], 0.05, ref now, clock);
            }
            if (done())
                return true;
            problems.Add($"timed out: {what}");
            return false;
        }
        Until(() => sp.Alive || Respawn(sp), 8, "the player spawned");
        Until(() => a.Local is { Alive: true }, 4, "the client saw itself alive");
        ref SandboxHand Hand() => ref hands.Hand(sp.Id);
        var inv = server.SandboxInv.Of(sp.Id);
        var mag = ConstructParts.Need("Box Magazine");
        var socketDef = ConstructParts.Need("Ammo Socket");
        Construct Build(float x, string extra, string wires)
        {
            var bp = ConstructBuild.ParseBlueprint($$"""
                {"format":"call-of-slop-construct-blueprint/1","name":"zz","description":"x","steps":[
                {"part":"Foundation"},
                {"part":"Frame Block","on":0,"socket":"top11","by":"-y"},
                {"part":"Rifle Receiver","on":1,"socket":"+y","by":"bottom"},
                {"part":"Gun Barrel","on":2,"socket":"front","by":"back"},
                {"part":"Ammo Crate","on":0,"socket":"top00","by":"bottom"},
                {"part":"Switch","on":0,"socket":"top33","by":"-y"}{{extra}}
                ],"wires":[{{wires}}]}
                """, "magazine socket test");
            var c = ConstructBuild.BuildOnGround(w, bp, ConstructWorld.Ground(World.Query, x, 10), 0f, out var why);
            if (c == null)
                throw new InvalidOperationException(why);
            c.Owner = sp.Id;
            return c;
        }
        int Find(Construct c, string name)
        {
            for (var i = 0; i < c.PartCount; i++)
                if (c.DefOf(i).Name == name)
                    return i;
            return -1;
        }
        void Stand(Construct c, Vector3 rest)
        {
            c.SegPose(0, out var q, out var t);
            var target = c.ToWorld(t + Vector3.Transform(rest, q));
            var from = target + new Vector3(-1.7f, 0, -0.4f);
            sp.Sim.Position = ConstructWorld.Ground(World.Query, from.X, from.Z) + new Vector3(0, 0.05f, 0);
            sp.Sim.Velocity = default;
            Pump(server, [a], 0.3, ref now, clock);
        }
        // The player's own held-part frame, on the client, at a rest-frame point.
        string Held(Construct onServer, Vector3 rest, bool fire, out int sel, out bool valid)
        {
            var c = a.Constructs.Get(onServer.Id);
            sel = -1;
            valid = false;
            if (c == null)
                return "the client has no such construct";
            c.SegPose(0, out var q, out var t);
            var target = c.ToWorld(t + Vector3.Transform(rest, q));
            var eye = sp.Sim.Position + new Vector3(0, 1.6f, 0);
            var dir = Vector3.Normalize(target - eye);
            var vp = Matrix4x4.CreateLookAt(eye, eye + dir, Vector3.UnitY) * Matrix4x4.CreatePerspectiveFieldOfView(1.2f, 16f / 9f, 0.1f, 100f);
            return Program.SandboxHeldFrame(a, eye, dir, new Vector2(1920, 1080), vp, fire, now, out sel, out valid);
        }
        // A part from the inventory into the hands (the parts menu's take), its tool out.
        void Take(ConstructPartDef d)
        {
            inv.Add(d.Id);
            a.SandboxSend(CActKind.Tool, part: (byte)d.Tool);
            Pump(server, [a], 0.2, ref now, clock);
            hands.TakePart(World.Map.Sandbox, sp.Id, d.Id, w, sp.Sim.Position, 0); // (survival: from the inventory, as the Tab screen takes one)
            Until(() => a.SandboxHands.Hand(a.LocalId).Held == d.Id && a.SandboxHands.Hand(a.LocalId).Tool == d.Tool, 2, $"the {d.Name} reached the client's hands");
        }
        Vector3 Point(Construct c, int part)
        {
            c.PartBounds(part, out var lo, out var hi);
            return (lo + hi) * 0.5f;
        }
        void Fill(Construct c, int box)
        {
            Stand(c, Point(c, box));
            a.SandboxSend(CActKind.Use, c.Id, (ushort)box);
            Until(() => ConstructAmmo.PackRounds(Hand().MagPack) > 0, 2, $"F at the {c.DefOf(box).Name} filled the magazine");
        }
        // Through the held-part path: a ghost, then the click.
        int Clip(Construct c, Vector3 rest, string tag)
        {
            Stand(c, rest);
            var look = Held(c, rest, false, out var sel, out var valid);
            if (sel < 0 || !valid)
            {
                problems.Add($"{tag}: no ghost (selected {sel}, valid {valid}; \"{look}\")");
                return -1;
            }
            var before = c.PartCount;
            Held(c, rest, true, out _, out _);
            return Until(() => c.PartCount == before + 1, 4, $"{tag}: the click attached it") ? c.PartCount - 1 : -1;
        }
        int Rounds(Construct c, int part) => part >= 0 && part < c.PartCount ? ConstructAmmo.PackRounds(c.Parts[part].Rounds) : -1;
        // Fires until the gun has nothing to load, with a few seconds' bound.
        int Fire(Construct c, int sw, int gun, int perShot, int expect)
        {
            var shots0 = w.ShotsFired;
            c.Parts[sw].State = 1;
            Until(() => (w.ShotsFired - shots0) * perShot >= expect, 6, $"the gun fired {expect} rounds");
            Pump(server, [a], 0.4, ref now, clock);
            c.Parts[sw].State = 0;
            return (w.ShotsFired - shots0) * perShot;
        }

        // ---- The chain: a socket on the gun, no crate, reel or belt ----
        var c1 = Build(10, """,{"part":"Ammo Socket","on":2,"socket":"top","by":"-z"}""", "[5,2]");
        var gun1 = Find(c1, "Rifle Receiver");
        var crate = Find(c1, "Ammo Crate");
        var sw1 = Find(c1, "Switch");
        var sock = Find(c1, "Ammo Socket");
        ConstructConfig.Set(ref c1.Parts[crate], c1.DefOf(crate), c1.DefOf(crate).CfgSlot[(int)CfgKey.AmmoType], 1);
        c1.Parts[crate].Rounds = 12; // (a few rounds: the early game's handful)
        Until(() => a.Constructs.Get(c1.Id) != null, 3, "the client has the first gun");
        Take(mag);
        Fill(c1, crate);
        Line($"chain: filled {ConstructAmmo.PackRounds(Hand().MagPack)} of type {ConstructAmmo.PackType(Hand().MagPack)}, crate left {c1.Parts[crate].Rounds}");
        if (ConstructAmmo.PackRounds(Hand().MagPack) != 12 || ConstructAmmo.PackType(Hand().MagPack) != 1 || c1.Parts[crate].Rounds != 0)
            problems.Add("F at the crate didn't fill the magazine with its 12 rounds and its type");
        var m1 = Clip(c1, Point(c1, sock), "socket on the gun");
        if (m1 >= 0)
        {
            Until(() => c1.Parts[gun1].Rounds > 0, 2, "the gun loaded from the socket's magazine");
            Line($"chain: on socket as part {m1}: magazine {Rounds(c1, m1)}, gun {c1.Parts[gun1].Rounds} of {c1.Guns[0].Mag}, type {c1.Parts[gun1].AmmoType}, problem \"{c1.Guns[0].Problem}\"");
            if (c1.Parts[gun1].AmmoType != 2 || c1.Guns[0].Problem.Length > 0)
                problems.Add("the gun didn't take the socket's type from the magazine's rounds");
            var fired = Fire(c1, sw1, gun1, 1, 12);
            Line($"chain: fired {fired}, gun {c1.Parts[gun1].Rounds}, magazine {Rounds(c1, m1)}, client magazine {Rounds(a.Constructs.Get(c1.Id)!, m1)}");
            if (fired != 12 || Rounds(c1, m1) != 0 || c1.Parts[gun1].Rounds != 0)
                problems.Add("the gun didn't fire the magazine's 12 rounds and stop");
            // Take it off: the empty one comes into the hands; refill it; back on.
            Stand(c1, Point(c1, m1));
            a.SandboxSend(CActKind.Remove, c1.Id, (ushort)m1);
            Until(() => c1.PartCount == m1 && Hand().Held == mag.Id, 4, "the empty magazine came off into the hands");
            c1.Parts[crate].Rounds = 9;
            Fill(c1, crate);
            var m2 = Clip(c1, Point(c1, sock), "refilled");
            // A full one in the hands swaps for the one on the socket.
            if (m2 >= 0)
            {
                Until(() => c1.Parts[gun1].Rounds > 0, 2, "the gun loaded the refilled magazine");
                Fire(c1, sw1, gun1, 1, 9);
                Take(mag);
                c1.Parts[crate].Rounds = 5;
                Fill(c1, crate);
                Stand(c1, Point(c1, m2));
                a.SandboxSend(CActKind.Use, c1.Id, (ushort)m2);
                Until(() => Rounds(c1, m2) > 0 || c1.Parts[gun1].Rounds > 0, 2, "F with a full magazine swapped it for the empty one");
                Line($"chain: swapped: socket {Rounds(c1, m2)}, gun {c1.Parts[gun1].Rounds}, hand {ConstructAmmo.PackRounds(Hand().MagPack)}");
                if (Rounds(c1, m2) + c1.Parts[gun1].Rounds != 5 || ConstructAmmo.PackRounds(Hand().MagPack) != 0)
                    problems.Add("the swap didn't put the 5 rounds on and the empty one in the hands");
            }
        }

        // ---- Put away and drawn again ----
        hands.Drop(w, sp.Id, sp.Sim.Position, 0); // (the empty one from the swap)
        Take(mag);
        c1.Parts[crate].Rounds = 7;
        Fill(c1, crate);
        var other = ConstructParts.Need("Switch");
        a.SandboxSend(CActKind.Tool, part: (byte)other.Tool);
        Pump(server, [a], 0.2, ref now, clock);
        inv.Add(other.Id);
        hands.TakePart(World.Map.Sandbox, sp.Id, other.Id, w, sp.Sim.Position, 0); // (the hands' part goes back to the inventory)
        Until(() => Hand().Held == other.Id, 2, "another part replaced the magazine in the hands");
        Pump(server, [a], 0.2, ref now, clock);
        a.SandboxSend(CActKind.Tool, part: (byte)mag.Tool);
        Pump(server, [a], 0.2, ref now, clock);
        hands.TakePart(World.Map.Sandbox, sp.Id, mag.Id, w, sp.Sim.Position, 0);
        Until(() => Hand().Held == mag.Id && ConstructAmmo.PackRounds(Hand().MagPack) > 0, 2, "a magazine drawn from the inventory has its rounds");
        Line($"carry: back from the inventory with {ConstructAmmo.PackRounds(Hand().MagPack)} rounds, type {ConstructAmmo.PackType(Hand().MagPack)}");
        if (ConstructAmmo.PackRounds(Hand().MagPack) != 7 || ConstructAmmo.PackType(Hand().MagPack) != 1)
            problems.Add("a magazine put in the inventory lost its rounds or type");
        if (ConstructEdits.Backpack != null && hands.Stow(sp.Id) == null)
        {
            Pump(server, [a], 0.2, ref now, clock);
            var drew = hands.DrawFromPack(sp.Id);
            Pump(server, [a], 0.2, ref now, clock);
            Line($"carry: through the backpack ({drew ?? "drawn"}): {ConstructAmmo.PackRounds(Hand().MagPack)} rounds");
            if (ConstructAmmo.PackRounds(Hand().MagPack) != 7)
                problems.Add("a magazine put in the backpack lost its rounds");
        }

        // ---- A socket fed through a link to the gun ----
        var c2 = Build(30, """,{"part":"Ammo Socket","on":0,"socket":"top30","by":"-y"}""", "[5,2]");
        var gun2 = Find(c2, "Rifle Receiver");
        var sock2 = Find(c2, "Ammo Socket");
        byte Port(Construct c, int part, string name) => Array.Find(c.DefOf(part).NamedPorts, n => n.Name == name)!.Byte;
        ConstructRoutes.Add(w, c2, RouteType.Feed, sock2, Port(c2, sock2, "Ammo out"), 255, 0, default, default, gun2, Port(c2, gun2, "Ammo in"));
        c2.Rebuild();
        w.StructureDirty.Add(c2.Id);
        Pump(server, [a], 0.5, ref now, clock);
        var m3 = Clip(c2, Point(c2, sock2) + new Vector3(0, 0.1f, 0), "socket by a link");
        if (m3 >= 0)
        {
            Until(() => c2.Parts[gun2].Rounds > 0, 2, "the linked gun loaded");
            var fired = Fire(c2, Find(c2, "Switch"), gun2, 1, 7);
            Line($"link: gun fed {fired} through the link, magazine {Rounds(c2, m3)}, feed route {c2.Guns[0].FeedRoute}, problem \"{c2.Guns[0].Problem}\"");
            if (fired != 7 || Rounds(c2, m3) != 0)
                problems.Add("a magazine on a socket didn't feed the gun through its link");
        }

        // ---- A merged gun: 4 rounds a shot ----
        var rec = ConstructParts.Need("Rifle Receiver");
        var c3 = w.Create(ConstructWorld.Ground(World.Query, 50, 10) + new Vector3(0, 1.1f, 0), Quaternion.Identity)!;
        c3.Owner = sp.Id;
        c3.Anchored = true;
        var size = rec.BoundsMax - rec.BoundsMin;
        for (var y = 0; y < 2; y++)
            for (var x = 0; x < 2; x++)
                ConstructBuild.AutoLink(w, c3, w.AddPart(c3, rec, new Vector3(x * size.X, y * size.Y, 0), 0));
        c3.Rebuild();
        var left = Array.FindIndex(rec.Sockets, s => s.Name == "left");
        var fit3 = ConstructBuild.Place(c3, 0, left, socketDef, Array.FindIndex(socketDef.Sockets, s => s.Name == "-z"), 0, out var g3);
        ConstructMerge.MergedBounds(c3, c3.Merges[0], out var mc, out var mh);
        var barrel = ConstructParts.Need("Gun Barrel");
        var sw3 = -1;
        if (c3.MergeCount != 1 || fit3 != null)
            problems.Add($"merged gun: no merge, or the socket didn't go on a member ({fit3})");
        else
        {
            var s3 = ConstructBuild.Attach(w, c3, socketDef, g3);
            c3.Rebuild();
            w.StructureDirty.Add(c3.Id);
            Pump(server, [a], 0.5, ref now, clock);
            Take(barrel);
            Clip(c3, new Vector3(mc.X, mc.Y, mc.Z - mh.Z + 0.001f), "merged barrel");
            sw3 = c3.Merges[0].Lead;
            Take(mag);
            c3.Parts[sw3].Rounds = 0;
            var c0 = Build(70, "", "[5,2]");
            var crate3 = Find(c0, "Ammo Crate");
            c0.Parts[crate3].Rounds = 20;
            Fill(c0, crate3);
            var m4 = Clip(c3, Point(c3, s3), "socket on a merged member");
            if (m4 >= 0)
            {
                Until(() => c3.Parts[sw3].Rounds > 0, 2, "the merged gun loaded");
                var swDef = ConstructParts.Need("Switch");
                ConstructBuild.Place(c3, 1, Array.FindIndex(rec.Sockets, s => s.Name == "right"), swDef, Array.FindIndex(swDef.Sockets, s => s.Name == "-x"), 0, out var gw);
                var trigger = ConstructBuild.Attach(w, c3, swDef, gw);
                c3.Rebuild();
                hands.SetTool(sp.Id, ConstructTool.Linker);
                Stand(c3, Point(c3, trigger));
                var wired = hands.Wire(w, sp.Id, sp.Sim.Position + new Vector3(0, 1.6f, 0), c3.Id, trigger, c3.Merges[0].Lead);
                if (wired != null)
                    problems.Add($"merged gun: the wire: {wired}");
                var shots0 = w.ShotsFired;
                c3.Parts[trigger].State = 1;
                Line($"merged: gun {c3.Parts[sw3].Rounds} of {c3.Guns[0].Mag}, {c3.Guns[0].PerShot} a shot, problem \"{c3.Guns[0].Problem}\", magazine {Rounds(c3, m4)}");
                Until(() => w.ShotsFired - shots0 >= 5, 6, "the merged gun fired 5 shots");
                if (c3.Guns[0].PerShot != 4 || c3.Guns[0].Problem.Length > 0)
                    problems.Add("a merged gun on a socket's magazine isn't 4 rounds a shot");
                c3.Parts[trigger].State = 0;
                Line($"merged: {w.ShotsFired - shots0} shots from 20 rounds, gun {c3.Parts[sw3].Rounds}");
                if (w.ShotsFired - shots0 != 5 || c3.Parts[sw3].Rounds != 0)
                    problems.Add("a merged gun didn't fire 20 rounds as 5 shots");
            }
        }

        // ---- The order: a working feed, a magazine on a socket, a magazine on the gun ----
        var c4 = Build(90, """,{"part":"Ammo Socket","on":2,"socket":"top","by":"-z"},{"part":"Ammo Reel","on":0,"socket":"top30","by":"-y"}""", "[5,2]");
        var gun4 = Find(c4, "Rifle Receiver");
        var crate4 = Find(c4, "Ammo Crate");
        var reel4 = Find(c4, "Ammo Reel");
        var sock4 = Find(c4, "Ammo Socket");
        var sw4 = Find(c4, "Switch");
        ConstructRoutes.Add(w, c4, RouteType.Feed, crate4, Port(c4, crate4, "Ammo out"), 255, 0, default, default, reel4, Port(c4, reel4, "Ammo in"));
        w.PlugHose(c4, reel4, c4, sock4);
        ConstructBuild.Place(c4, sock4, Array.FindIndex(socketDef.Sockets, s => s.Name == "back"), mag, Array.FindIndex(mag.Sockets, s => s.Name == "bottom"), 0, out var gs);
        var onSock = ConstructBuild.Attach(w, c4, mag, gs);
        ConstructBuild.Place(c4, gun4, Array.FindIndex(rec.Sockets, s => s.Name == "right"), mag, Array.FindIndex(mag.Sockets, s => s.Name == "left"), 0, out var gg);
        var onGun = ConstructBuild.Attach(w, c4, mag, gg);
        c4.Rebuild();
        c4.Parts[onSock].Rounds = ConstructAmmo.Pack(6, 1);
        c4.Parts[onGun].Rounds = ConstructAmmo.Pack(6, 2);
        c4.Parts[crate4].Rounds = 10;
        c4.Parts[gun4].Rounds = 0;
        Until(() => c4.Parts[gun4].Rounds > 0, 5, "the fed gun loaded"); // (a gun with a magazine on it keeps that magazine's reload)
        var first = (c4.Parts[crate4].Rounds, Rounds(c4, onSock), Rounds(c4, onGun), (int)c4.Parts[gun4].AmmoType);
        var f4 = Fire(c4, sw4, gun4, 1, 10);
        Until(() => c4.Parts[gun4].Rounds > 0, 5, "the socket's magazine loaded");
        var second = (c4.Parts[crate4].Rounds, Rounds(c4, onSock), Rounds(c4, onGun), (int)c4.Parts[gun4].AmmoType);
        var f5 = Fire(c4, sw4, gun4, 1, 6);
        Until(() => c4.Parts[gun4].Rounds > 0, 5, "the gun's magazine loaded");
        var third = (c4.Parts[crate4].Rounds, Rounds(c4, onSock), Rounds(c4, onGun), (int)c4.Parts[gun4].AmmoType);
        Line($"order: (crate, on socket, on gun, type) loaded {first}; after {f4} {second}; after {f5} more {third}");
        if (first != (0, 6, 6, 1) || second != (0, 0, 6, 2) || third != (0, 0, 0, 3))
            problems.Add("the sources didn't go feed, then the socket's magazine, then the gun's");

        // ---- A magazine the gun can't take ----
        var shellMag = ConstructParts.All.FirstOrDefault(d => ConstructAmmo.IsMagazine(d) && ConstructAmmo.GunTakes(rec, d) != null);
        if (shellMag == null)
            problems.Add("no magazine a Rifle Receiver refuses");
        else
        {
            var c5 = Build(110, """,{"part":"Ammo Socket","on":2,"socket":"top","by":"-z"}""", "[5,2]");
            var s5 = Find(c5, "Ammo Socket");
            var by = Array.FindIndex(socketDef.Sockets, s => s.Name == "back");
            var refused = ConstructBuild.Place(c5, s5, by, shellMag, Array.FindIndex(shellMag.Sockets, s => s.Name == "bottom"), 0, out _);
            Take(shellMag);
            Stand(c5, Point(c5, s5));
            Pump(server, [a], 0.3, ref now, clock);
            var look = Held(c5, Point(c5, s5), false, out var sel, out var valid);
            Line($"refusal: {shellMag.Name}: \"{refused}\"; HUD \"{look}\", ghost {sel} valid {valid}");
            if (refused == null || !refused.Contains("doesn't fit") || sel >= 0 || !look.Contains("doesn't fit"))
                problems.Add($"a {shellMag.Name} on a rifle's socket wasn't refused with its reason");
        }
        foreach (var p in problems)
            Fail($"magazine sockets: {p}");
    }

    private static bool Respawn(Player p)
    {
        p.WantsRespawn = true;
        return false;
    }
}
