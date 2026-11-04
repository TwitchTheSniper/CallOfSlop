using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    private static void CheckTmpSelectNet()
    {
        void Say(string s) => Line("[net] " + s);
        var yard = Maps.Find(ConstructYardMap)!;
        const ushort port = 26491;
        var clock = Stopwatch.StartNew();
        var now = 0.0;
        yard.RestoreProps();
        World.SetMap(yard);
        var sb = yard.Sandbox!;
        var (inf, menu) = (sb.InfiniteParts, sb.PartsMenu);
        using var server = new Game(NetPeer.Host(port, 4), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: yard.Id) { MapVoteEnabled = false };
        server.Mode = GameMode.Sandbox;
        using var a = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Alpha", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        Pump(server, [a], 1.5, ref now, clock);
        for (var wait = 0; wait < 40 && server.Phase != MatchPhase.Playing; wait++)
            Pump(server, [a], 0.25, ref now, clock);
        Pump(server, [a], 0.5, ref now, clock);
        var sp = server.Players[a.LocalId];
        if (!sp.Alive)
            server.SelfTestRespawn(sp, now);
        server.Net.SimulateBadNetwork(40, 10, 2);
        a.Net.SimulateBadNetwork(40, 10, 2);
        var w = server.Constructs;
        var spot = OpenSpot(w, sp.Position);
        var ground = ConstructWorld.Ground(World.Query, spot.X, spot.Z);
        var def = ConstructParts.Find("Frame Block")!;
        var plate = w.Create(ground + new Vector3(0, 1.5f, 0), Quaternion.Identity)!;
        for (var x = 0; x < 6; x++)
            for (var z = 0; z < 6; z++)
                w.AddPart(plate, def, new Vector3(x * 0.5f, 0, z * 0.5f), 0);
        plate.Rebuild();
        w.AutoLinkMany(plate, 0, plate.PartCount);
        plate.Pinned = true;
        plate.Owner = a.LocalId;
        plate.Rebuild();
        w.StructureDirty.Add(plate.Id);
        var eyeAt = ground + new Vector3(1.25f, 3.5f, 1.25f);
        sp.Teleport(ground + new Vector3(-3f, 0, 1.25f));
        server.SandboxHands.Hand(a.LocalId).Tool = ConstructTool.Selector;
        a.SandboxHands.Hand(a.LocalId).Tool = ConstructTool.Selector;
        a.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.Selector);
        Pump(server, [a], 1.0, ref now, clock);
        var cp = a.Constructs.Get(plate.Id);
        Say($"client has plate: {cp?.PartCount}, me {a.Local != null} alive {a.Local?.Alive}");
        var me = a.Local!;
        var ctl = new SelectController();
        var vp = Matrix4x4.CreateLookAt(eyeAt, eyeAt - Vector3.UnitY, Vector3.UnitZ) * Matrix4x4.CreatePerspectiveFieldOfView(1.2f, 16f / 9, 0.05f, 500f);
        SelectInput In(Action<SelectInput>? _ = null) => new() { NowMs = clock.ElapsedMilliseconds, Screen = new Vector2(1920, 1080), ViewProj = vp, CamRight = Vector3.UnitX, CamUp = Vector3.UnitY, Colour = ConstructPaint.Rgb(10, 200, 30) };
        void F(SelectInput inp, Vector3 eye, Vector3 dir)
        {
            ctl.Tick(a, true, clock.ElapsedMilliseconds);
            var surf = World.Query.Raycast(eye, dir, 64, QueryFilter.All, out var s);
            var aimed = a.Constructs.Raycast(eye, dir, surf ? s.Distance : 64, out var hit);
            if (aimed)
                surf = false;
            ctl.Frame(a, a.Local!, in inp, eye, dir, aimed, in hit, surf, surf ? s.Point : default, surf ? s.Normal : default);
            Pump(server, [a], 0.02, ref now, clock);
        }
        var down = -Vector3.UnitY;
        Vector3 Over(float x, float z) => a.Constructs.Get(plate.Id)!.ToWorld(new Vector3(x, 2f, z));
        // Click, shift-click.
        var i1 = In(); i1.FireDown = i1.FirePressed = true;
        F(i1, Over(0, 0), down);
        F(In(), Over(0, 0), down);
        Say($"click: {ctl.Selection.Count} ({a.SandboxLookText} | {a.SandboxSelectHint})");
        var i2 = In(); i2.FireDown = i2.FirePressed = true; i2.Shift = true;
        F(i2, Over(0.5f, 0), down);
        var i2b = In(); i2b.Shift = true;
        F(i2b, Over(0.5f, 0), down);
        Say($"shift-click: {ctl.Selection.Count}");
        // Box drag 3 x 3.
        var d0 = In(); d0.FireDown = d0.FirePressed = true;
        F(d0, Over(0, 0), down);
        var d1 = In(); d1.FireDown = true;
        F(d1, Over(0.6f, 0.6f), down);
        F(d1, Over(1.0f, 1.0f), down);
        F(In(), Over(1.0f, 1.0f), down);
        Say($"box: {ctl.Selection.Count}");
        // Double-click: a section.
        var c0 = In(); c0.FireDown = c0.FirePressed = true;
        F(c0, Over(2, 2), down); F(In(), Over(2, 2), down);
        var c1 = In(); c1.FireDown = c1.FirePressed = true;
        F(c1, Over(2, 2), down); F(In(), Over(2, 2), down);
        Say($"double-click: {ctl.Selection.Count}");
        // RMB click clears.
        var r0 = In(); r0.AimDown = r0.AimPressed = true;
        F(r0, Over(2, 2), down); F(In(), Over(2, 2), down);
        Say($"rmb click: {ctl.Selection.Count}");
        // Marquee over the plate from straight above.
        var m0 = In(); m0.AimDown = m0.AimPressed = true;
        F(m0, Over(1.25f, 1.25f), down);
        ctl.Mouse(new Vector2(500, 300), true);
        var m1 = In(); m1.AimDown = true;
        F(m1, Over(1.25f, 1.25f), down);
        F(In(), Over(1.25f, 1.25f), down);
        Say($"marquee: {ctl.Selection.Count}");
        // Copy and paste.
        var body0 = w.Bodies.Count;
        var cc = In(); cc.Copy = true;
        F(cc, Over(1.25f, 1.25f), down);
        Say($"copy: mode {ctl.Mode} clip {ctl.Clip?.Parts}");
        var side = ground + new Vector3(12, 4, 1);
        var look = Vector3.Normalize(new Vector3(0.1f, -1, 0));
        sp.Teleport(ground + new Vector3(4f, 0, 1.25f));
        F(In(), side, look);
        Say($"ghost: on {ctl.GhostOn} ok {ctl.GhostOk} at {ctl.GhostPos} ({a.SandboxLookText})");
        var pp = In(); pp.FirePressed = pp.FireDown = true;
        F(pp, side, look);
        F(In(), side, look);
        Pump(server, [a], 0.8, ref now, clock);
        Say($"pasted: bodies {body0} -> {w.Bodies.Count}, newest {w.Bodies[^1].PartCount} parts frozen {w.Bodies[^1].Frozen} owner {w.Bodies[^1].Owner} (me {a.LocalId}); client sees {a.Constructs.Bodies.Count}; notice '{a.SandboxNotice}'");
        var pasted = w.Bodies[^1];
        var rc = In(); rc.AimDown = rc.AimPressed = true;
        F(rc, side, look);
        F(In(), side, look);
        Say($"cancel: mode {ctl.Mode}");
        // Paint, weld.
        var sel = ctl.Selection;
        var p1 = In(); p1.Paint = true;
        F(p1, Over(2, 2), down);
        Pump(server, [a], 0.5, ref now, clock);
        var painted = 0;
        for (var i = 0; i < plate.PartCount; i++)
            painted += plate.Parts[i].Paint != 0 ? 1 : 0;
        Say($"paint: {painted} of {sel.Count} selected; client part paint {a.Constructs.Get(plate.Id)!.Parts[sel.Items[0]].Paint:x}");
        // A move onto a socket of the pasted group: pick 4 parts, attach by aiming at pasted's side.
        F(In(), Over(2, 2), down);
        // Select the last row: box.
        var e0 = In(); e0.FireDown = e0.FirePressed = true;
        F(e0, Over(2.5f, 0), down);
        var e1 = In(); e1.FireDown = true;
        F(e1, Over(2.5f, 2.5f), down);
        F(In(), Over(2.5f, 2.5f), down);
        Say($"row: {ctl.Selection.Count}");
        var mv = In(); mv.Move = true;
        F(mv, Over(2, 2), down);
        Say($"move: mode {ctl.Mode} clip {ctl.Clip?.Parts}");
        var cpasted = a.Constructs.Get(pasted.Id)!;
        var aimAt = cpasted.ToWorld((cpasted.LocalMin + cpasted.LocalMax) * 0.5f + new Vector3(0, 0, 0));
        // Aim at the pasted group's +x side.
        var from = aimAt + new Vector3(cpasted.LocalMax.X - cpasted.LocalMin.X + 2, 0, 0);
        sp.Teleport(ground + new Vector3(8f, 0, 1.25f));
        var aimDir = Vector3.Normalize(aimAt - from);
        F(In(), from, aimDir);
        Say($"attach ghost: ok {ctl.GhostOk} at {ctl.GhostPos} ({a.SandboxLookText})");
        var bodiesBefore = w.Bodies.Count;
        var plateBefore = plate.PartCount;
        var pastedBefore = pasted.PartCount;
        var cm = In(); cm.FireDown = cm.FirePressed = true;
        F(cm, from, aimDir);
        F(In(), from, aimDir);
        Pump(server, [a], 0.8, ref now, clock);
        Say($"moved: plate {plateBefore} -> {plate.PartCount}, pasted {pastedBefore} -> {pasted.PartCount}, bodies {bodiesBefore} -> {w.Bodies.Count}; notice '{a.SandboxNotice}'; client plate {a.Constructs.Get(plate.Id)?.PartCount}, pasted {a.Constructs.Get(pasted.Id)?.PartCount}");
        // Stale: select 4, another peer's edit shifts indices.
        var s0 = In(); s0.FireDown = s0.FirePressed = true;
        F(s0, Over(1.0f, 1.0f), down);
        var s1 = In(); s1.FireDown = true;
        F(s1, Over(1.6f, 1.6f), down);
        F(In(), Over(1.6f, 1.6f), down);
        var first = plate.PartCount;
        Say($"stale select {ctl.Selection.Count}");
        w.RemovePart(plate, 0);
        Pump(server, [a], 0.6, ref now, clock);
        F(In(), Over(1.6f, 1.6f), down);
        Say($"after edit: plate {first} -> {plate.PartCount}, selection {ctl.Selection.Count}");
        var dl = In(); dl.Delete = true;
        F(dl, Over(1.6f, 1.6f), down);
        Say($"delete asks: {ctl.Asking} ({a.SandboxLookText})");
        F(dl, Over(1.6f, 1.6f), down);
        Pump(server, [a], 0.6, ref now, clock);
        Say($"deleted: plate {plate.PartCount}, client {a.Constructs.Get(plate.Id)?.PartCount}, notice '{a.SandboxNotice}'");
        // Save and stamp through the file.
        F(In(), Over(0.5f, 2.5f), down);
        var clip = ConstructClip.Capture(a.Constructs.Get(pasted.Id)!, Enumerable.Range(0, a.Constructs.Get(pasted.Id)!.PartCount).Select(i => (ushort)i).ToArray(), out _)!;
        var text = clip.ToBlueprint("Tmp Plate", "", out var bw);
        Say($"writer: {text?.Length} {bw}");
        Say($"bad name: {PlayerBlueprints.NameRefusal("a/b")}, folder {PlayerBlueprints.Folder}");
        Say($"write: {PlayerBlueprints.Write("Tmp Plate", text!)}; list {string.Join(",", PlayerBlueprints.List().Select(e => e.Name + ":" + e.Parts))}");
        var bb = w.Bodies.Count;
        Say($"stamp: {ctl.BeginStamp(a, "Tmp Plate")}");
        for (var t = 0; t < 200 && ctl.Mode == SelectController.Placing.None; t++)
            F(In(), side + new Vector3(0, 0, -8), look);
        Say($"stamp mode {ctl.Mode} clip {ctl.Clip?.Parts}");
        var sp1 = In(); sp1.FireDown = sp1.FirePressed = true;
        F(sp1, side + new Vector3(0, 0, -8), look);
        F(In(), side + new Vector3(0, 0, -8), look);
        Pump(server, [a], 1.2, ref now, clock);
        Say($"stamped: bodies {bb} -> {w.Bodies.Count}, newest {w.Bodies[^1].PartCount}; notice '{a.SandboxNotice}'");
        PlayerBlueprints.Delete("Tmp Plate");
        Say($"deleted file, list {PlayerBlueprints.List().Count}");
        ctl.Reset();
        // Survival, on the server's rules with a Selector and Blueprint Desk.
        sb.InfiniteParts = false;
        sb.PartsMenu = false;
        try
        {
            var bp = ConstructBuild.ParseBlueprint("""{ "format": "call-of-slop-construct-blueprint/1", "name": "D", "steps": [ { "part": "Foundation" }, { "part": "Selector", "on": 0, "socket": "top00", "by": "bottom" }, { "part": "Battery", "on": 0, "socket": "top30", "by": "-y" }, { "part": "Chest", "on": 0, "socket": "top03", "by": "bottom" }, { "part": "Blueprint Desk", "on": 0, "socket": "top22", "by": "bottom" } ], "links": [ { "type": "chute", "from": 1, "to": 3 }, { "type": "chute", "from": 4, "to": 3 } ] }""", "d");
            var depot = ConstructBuild.BuildOnGround(w, bp, ground + new Vector3(-8, 0, 0), 0, out var dw);
            Say($"depot: {depot?.PartCount} {dw}; refused: {string.Join(" | ", ConstructParts.Refused.Where(x => x.Contains("Desk") || x.Contains("Select")))}");
            depot!.Owner = a.LocalId;
            sp.Teleport(ground + new Vector3(-6f, 0, 1.25f));
            Pump(server, [a], 1.2, ref now, clock);
            var v = a.SandboxAids(a.LocalId);
            Say($"survival: infinite {a.SandboxInfiniteParts}; select state {v.State[(int)BuildAid.Select]} live {a.SandboxAidLive(BuildAid.Select)}; desk {v.State[(int)BuildAid.Blueprint]}; off '{a.SandboxAidOff(BuildAid.Select)}'");
            var lamp = ConstructParts.Find("Lamp")!;
            var small = ConstructBuild.BuildOnGround(w, ConstructBuild.ParseBlueprint($$"""{ "format": "call-of-slop-construct-blueprint/1", "name": "S", "steps": [ { "part": "Foundation" }, { "part": "Lamp", "on": 0, "socket": "top00", "by": "{{lamp.Sockets[0].Name}}" } ] }""", "s"), ground + new Vector3(-6, 0, 6), 0, out var sw2)!;
            Say($"small {small?.PartCount} {sw2}");
            small!.Owner = a.LocalId;
            Pump(server, [a], 0.6, ref now, clock);
            var cs = a.Constructs.Get(small.Id)!;
            ctl.Selection.Add(cs, 1);
            var eyeS = a.Local!.Eye;
            var dirS = Vector3.Normalize(cs.ToWorld(Vector3.Zero) - eyeS);
            var cop = In(); cop.Copy = true;
            F(cop, eyeS, dirS);
            Say($"survival copy {ctl.Mode}");
            var paste = In(); paste.FirePressed = paste.FireDown = true;
            var spot2 = ground + new Vector3(-6, 3, 9);
            var dir2 = Vector3.Normalize(new Vector3(0, -1, 0.01f));
            F(In(), spot2, dir2);
            Say($"cost line: {a.SandboxLookText}");
            F(paste, spot2, dir2);
            F(In(), spot2, dir2);
            Pump(server, [a], 0.6, ref now, clock);
            Say($"no lamps: notice '{a.SandboxNotice}'");
            w.TakePart(depot, 3, lamp.Id);
            var store = w.StoreOf(depot, 3);
            Say($"chest store {store != null}: {w.SetIn(depot, 3, 0, lamp)}");
            Pump(server, [a], 1.0, ref now, clock);
            F(In(), spot2, dir2);
            Say($"cost line: {a.SandboxLookText}; beacon {a.SandboxBeacon(a.LocalId).Counts[lamp.Id]}");
            var n0 = w.Bodies.Count;
            F(paste, spot2, dir2);
            F(In(), spot2, dir2);
            Pump(server, [a], 1.0, ref now, clock);
            Say($"paid: bodies {n0} -> {w.Bodies.Count}, notice '{a.SandboxNotice}', beacon {a.SandboxBeacon(a.LocalId).Counts[lamp.Id]}");
            ctl.Cancel(a);
            // Delete it again: back to the network.
            var nb = w.Bodies[^1];
            Pump(server, [a], 0.4, ref now, clock);
            var cn = a.Constructs.Get(nb.Id)!;
            ctl.Selection.Clear();
            ctl.Selection.Add(cn, 0);
            var del = In(); del.Delete = true;
            F(del, eyeS, dirS);
            F(del, eyeS, dirS);
            Pump(server, [a], 1.0, ref now, clock);
            Say($"returned: live {w.IsLive(nb)}, notice '{a.SandboxNotice}', beacon {a.SandboxBeacon(a.LocalId).Counts[lamp.Id]}");
        }
        finally
        {
            sb.InfiniteParts = inf;
            sb.PartsMenu = menu;
        }
    }
}
