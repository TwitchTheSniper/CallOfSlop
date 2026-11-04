using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    private static void CheckTmpSelect()
    {
        void Say(string s) => Line("[tmp] " + s);
        var buggy = ConstructBuild.FindBlueprint("Buggy")!;
        // ---- Round trip of every shipped blueprint through capture and the writer.
        int ok = 0, bad = 0;
        foreach (var bp in ConstructBuild.Blueprints)
        {
            var w0 = new ConstructWorld();
            var c0 = ConstructBuild.Build(w0, bp, Vector3.Zero, Quaternion.Identity, out _);
            if (c0 == null || bp.Cargo.Count > 0)
                continue;
            var clip = ConstructClip.Of(c0, bp.Name);
            var text = clip.ToBlueprint(bp.Name, "", out var why);
            if (text == null)
            {
                bad++;
                Say($"{bp.Name}: writer: {why}");
                continue;
            }
            try
            {
                var again = ConstructBuild.ParseBlueprint(text, "x.json");
                var w1 = new ConstructWorld();
                var c1 = ConstructBuild.Build(w1, again, Vector3.Zero, Quaternion.Identity, out var why2);
                if (c1 == null)
                {
                    bad++;
                    Say($"{bp.Name} ({c0.PartCount}): {why2}");
                }
                else if (c1.PartCount != c0.PartCount || c1.LinkCount < c0.LinkCount || c1.JointCount != c0.JointCount || c1.RouteCount != c0.RouteCount)
                {
                    bad++;
                    Say($"{bp.Name}: parts {c0.PartCount}/{c1.PartCount} links {c0.LinkCount}/{c1.LinkCount} joints {c0.JointCount}/{c1.JointCount} routes {c0.RouteCount}/{c1.RouteCount}");
                }
                else
                    ok++;
            }
            catch (Exception ex)
            {
                bad++;
                Say($"{bp.Name}: {ex.Message}");
            }
        }
        Say($"blueprint round trip: {ok} ok, {bad} bad");

        // ---- The big hull: timings.
        var w = new ConstructWorld();
        var edits = new ConstructEdits { InfiniteParts = true };
        var def = ConstructParts.Find("Frame Block")!;
        var size = def.BoundsMax - def.BoundsMin;
        var hull = w.Create(new Vector3(0, 5, 0), Quaternion.Identity)!;
        for (var x = 0; x < 89; x++)
            for (var z = 0; z < 89; z++)
                w.AddPart(hull, def, new Vector3(x * size.X, 0, z * size.Z), 0);
        var sw = Stopwatch.StartNew();
        hull.Rebuild();
        Say($"hull {hull.PartCount} parts, first rebuild {sw.Elapsed.TotalMilliseconds:0.0} ms");
        sw.Restart();
        var links = w.AutoLinkMany(hull, 0, hull.PartCount);
        Say($"AutoLinkMany: {links} links, {sw.Elapsed.TotalMilliseconds:0.0} ms");
        hull.Pinned = true;
        for (var k = 0; k < 3; k++)
        {
            hull.Dirty = true;
            sw.Restart();
            hull.Rebuild();
            Say($"rebuild {sw.Elapsed.TotalMilliseconds:0.0} ms");
        }
        var pw = new PacketWriter(16384);
        sw.Restart();
        ConstructNet.WriteFull(pw, hull);
        Say($"WriteFull {sw.Elapsed.TotalMilliseconds:0.0} ms {pw.Length} B");
        var items = new ushort[2000];
        for (var i = 0; i < items.Length; i++)
            items[i] = (ushort)(i + 1000);
        var sel = new PartSelection();
        sw.Restart();
        foreach (var i in items)
            sel.Add(hull, i);
        Say($"select 2000: {sw.Elapsed.TotalMilliseconds:0.00} ms");
        sw.Restart();
        sel.Check(w);
        Say($"check: {sw.Elapsed.TotalMilliseconds:0.000} ms");
        for (var pass = 0; pass < 3; pass++)
        {
            sw.Restart();
            var clip = ConstructClip.Capture(hull, items, out var cw)!;
            Say($"capture {clip.Parts}: {sw.Elapsed.TotalMilliseconds:0.0} ms");
            sw.Restart();
            var order = new int[clip.Parts];
            for (var i = 0; i < order.Length; i++)
                order[i] = i;
            var pasted = w.CopyPiece(clip.Body, order, new Vector3(0, 40, 0), Quaternion.Identity, out _)!;
            Say($"paste copy: {sw.Elapsed.TotalMilliseconds:0.0} ms");
            sw.Restart();
            var text = clip.ToBlueprint("T", "", out _)!;
            Say($"writer: {sw.Elapsed.TotalMilliseconds:0.0} ms, {text.Length} B, deflated {ConstructEdits.Deflate(text).Length} B");
            sw.Restart();
            var bp = ConstructBuild.ParseBlueprint(text, "t.json");
            Say($"parse: {sw.Elapsed.TotalMilliseconds:0.0} ms");
            sw.Restart();
            var w2 = new ConstructWorld();
            var run = new ConstructBuild.BuildRun();
            var worst = 0.0;
            var sw2 = new Stopwatch();
            var e = ConstructBuild.BuildSteps(w2, bp, Vector3.Zero, Quaternion.Identity, run).GetEnumerator();
            for (var more = true; more;)
            {
                sw2.Restart();
                more = e.MoveNext();
                worst = Math.Max(worst, sw2.Elapsed.TotalMilliseconds);
            }
            Say($"blueprint build {run.Built?.PartCount}: {sw.Elapsed.TotalMilliseconds:0.0} ms, worst step {worst:0.00} ms {run.Why}");
            w.Remove(pasted);
        }
        // Cut and delete through the job paths, timed.
        var eye = new Vector3(20, 8, 20);
        edits.Hands[0].Tool = ConstructTool.Selector;
        double Ticks()
        {
            var worst = 0.0;
            for (var t = 0; t < 2000; t++)
            {
                sw.Restart();
                edits.SelectTick(w, _ => eye);
                worst = Math.Max(worst, sw.Elapsed.TotalMilliseconds);
                if (edits.Notices.Count > 0)
                {
                    foreach (var n in edits.Notices)
                        Say($"notice: {n.Text} (tick {t})");
                    edits.Notices.Clear();
                    break;
                }
            }
            return worst;
        }
        void Sel()
        {
            edits.SelectBegin(0, hull.Id, items.Length, ConstructEdits.SelectHash(hull, items));
            var pw2 = new PacketWriter(8192);
            for (var from = 0; from < items.Length; from += ConstructEdits.MaxChunk)
            {
                var n = Math.Min(items.Length - from, ConstructEdits.MaxChunk);
                pw2.Begin(Msg.CAct).U16((ushort)n);
                for (var k = 0; k < n; k++)
                    pw2.U16(items[from + k]);
                var r = new PacketReader(pw2.Span[1..]);
                var why = edits.SelectChunk(0, ref r);
                if (why != null)
                    Say("chunk: " + why);
            }
        }
        Sel();
        sw.Restart();
        Say($"paint: {edits.SelectDo(w, 0, eye, SelAct.Paint, PaintMode.Part, ConstructPaint.Rgb(200, 10, 10), default)} {sw.Elapsed.TotalMilliseconds:0.0} ms, painted part {hull.Parts[1500].Paint:x}");
        Sel();
        sw.Restart();
        Say($"weld: {edits.SelectDo(w, 0, eye, SelAct.Weld, default, 0, default)}");
        Say($"weld worst tick {Ticks():0.0} ms");
        Sel();
        var bodies0 = w.Bodies.Count;
        var parts0 = hull.PartCount;
        Say($"move ask: {edits.SelectDo(w, 0, eye, SelAct.Move, default, 0, new ClipPlace { Pos = new Vector3(0, 60, 0), Rot = Quaternion.Identity })}");
        var moveW = Ticks();
        sw.Restart();
        hull.Rebuild();
        Say($"move worst tick {moveW:0.0} ms, then rebuild {sw.Elapsed.TotalMilliseconds:0.0}; bodies {bodies0} -> {w.Bodies.Count}, hull {parts0} -> {hull.PartCount}");
        var piece = w.Bodies[^1];
        Say($"piece {piece.Id}: {piece.PartCount} parts at {piece.Pos} frozen {piece.Frozen} owner {piece.Owner} wreck {piece.Wreck}");
        // Fresh hull: delete, survival delete; rebuild timing, best of ten.
        var h2 = w.Create(new Vector3(0, 80, 0), Quaternion.Identity)!;
        for (var x = 0; x < 89; x++)
            for (var z = 0; z < 89; z++)
                w.AddPart(h2, def, new Vector3(x * size.X, 0, z * size.Z), 0);
        h2.Rebuild();
        w.AutoLinkMany(h2, 0, h2.PartCount);
        var best = double.MaxValue;
        for (var k = 0; k < 10; k++)
        {
            h2.Dirty = true;
            sw.Restart();
            h2.Rebuild();
            best = Math.Min(best, sw.Elapsed.TotalMilliseconds);
        }
        Say($"rebuild best of ten {best:0.0} ms");
        hull = h2;
        items = Enumerable.Range(0, 2000).Select(i => (ushort)i).ToArray();
        Sel();
        sw.Restart();
        var w3 = edits.SelectDo(w, 0, eye + new Vector3(0, 75, 0), SelAct.Delete, default, 0, default);
        Say($"delete: {w3} {sw.Elapsed.TotalMilliseconds:0.0} ms; hull {hull.PartCount}, bodies {w.Bodies.Count}");
        sw.Restart();
        hull.Rebuild();
        Say($"then rebuild {sw.Elapsed.TotalMilliseconds:0.0} ms");
        var inv = new SandboxInventories();
        edits.Inventory = inv;
        edits.InfiniteParts = false;
        edits.Survival = true;
        items = Enumerable.Range(0, 2000).Select(i => (ushort)i).ToArray();
        hull.Owner = 0;
        Sel();
        sw.Restart();
        var w4 = edits.SelectDo(w, 0, eye + new Vector3(0, 75, 0), SelAct.Delete, default, 0, default);
        Say($"survival delete: {w4} {sw.Elapsed.TotalMilliseconds:0.0} ms; hull {hull.PartCount}, bodies {w.Bodies.Count}; physics-only {def.PhysicsOnly}");
        Say($"worst {Ticks():0.0}");
        Say($"bodies now {w.Bodies.Count}, last {w.Bodies[^1].PartCount} parts");
        // Paying: an inventory with too few, then enough; loose bodies for physics-only.
        var lamp = ConstructParts.Find("Lamp")!;
        var plate = ConstructBuild.Build(w, ConstructBuild.ParseBlueprint($$"""{ "format": "call-of-slop-construct-blueprint/1", "name": "L", "steps": [ { "part": "Frame Block" }, { "part": "Lamp", "on": 0, "socket": "{{def.Sockets[0].Name}}", "by": "{{lamp.Sockets[0].Name}}" } ] }""", "l"), new Vector3(0, 120, 0), Quaternion.Identity, out var lw);
        Say($"pay test construct: {plate?.PartCount} {lw}");
        plate!.Owner = 0;
        hull = plate;
        items = [0, 1];
        Sel();
        var peye = new Vector3(0, 120, 5);
        Say($"copy: {edits.SelectDo(w, 0, peye, SelAct.Copy, default, 0, default)}");
        var place = new ClipPlace { Pos = new Vector3(5, 120, 0), Rot = Quaternion.Identity };
        Say($"paste with none: {edits.Paste(w, 0, peye, place)}");
        inv.Of(0).Add(lamp.Id, 1);
        Say($"paste with a lamp: {edits.Paste(w, 0, peye, place)}");
        var loose = ConstructBuild.PlaceNew(w, def, new FreePlacement { Pos = new Vector3(0, 121, 8), Rot = Quaternion.Identity })!;
        loose.Owner = 0;
        var b0 = w.Bodies.Count;
        Say($"paste with both: {edits.Paste(w, 0, peye, place)}");
        Say($"worst {Ticks():0.0}; lamps {inv.Of(0).Parts[lamp.Id]}, bodies {b0} -> {w.Bodies.Count}, loose live {w.IsLive(loose)}");
    }
}
