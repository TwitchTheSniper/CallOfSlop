using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// A splitter on a merged receiver (board item dev-merged-splitter-ms02): scaled sockets apply to every part fitted on a
    /// scaled part. A 2x2 and a 3x3 of Rifle Receivers, a 2-way (hand-made Twin Splitter) and builder splitters (Y 2-way,
    /// Pyramid 3-way, Quad Line 4-way): the splitter's back sits flush on the merged front, its output sockets are its file's
    /// times N, a barrel (and a muzzle device) on each is N times bigger and mates its socket, the gun's numbers are the
    /// single splitter gun's through the merge rules once, each barrel's rounds leave its own muzzle, and taking a cell off
    /// loses no part.
    /// </summary>
    private static void CMergedSplitter()
    {
        foreach (var n in new[] { 2, 3 })
            foreach (var name in new[] { "Twin Splitter", "Pyramid Splitter", "Quad Line Splitter" })
                MsOne(n, name);
    }

    private static ConstructPartDef? MsDef(string name) =>
        ConstructParts.Find(name) ?? ConstructParts.All.FirstOrDefault(d => d.Generated && d.GunPartName.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>A splitter gun on an n x n Rifle Receiver square (n = 1: one receiver): splitter, a barrel on each output, a muzzle brake on some, a magazine and a box.</summary>
    private sealed class MsGun
    {
        public ConstructWorld W = null!;
        public Construct C = null!;
        public int Lead, Splitter, Mag;
        public ConstructPartDef Spl = null!;
        public List<int> Outputs = new(), Barrels = new(), Tips = new(); // (socket indices on the splitter, barrel parts, the part each barrel's rounds leave from)
        public List<string> Problems = new();
    }

    private static MsGun MsBuild(int n, ConstructPartDef spl, float x0)
    {
        var r = new MsGun { Spl = spl, W = new ConstructWorld() };
        var rec = ConstructParts.Need("Rifle Receiver");
        var barrelDef = ConstructParts.Need("Gun Barrel");
        var muzzleDef = ConstructParts.Need("Gun Muzzle Brake");
        var w = r.W;
        var c = r.C = w.Create(ConstructWorld.Ground(World.Query, x0, -6) + new Vector3(0, 0.5f, 0), Quaternion.Identity)!;
        var size = rec.BoundsMax - rec.BoundsMin;
        for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
                ConstructBuild.AutoLink(w, c, w.AddPart(c, rec, new Vector3(x * size.X, y * size.Y, 0), 0));
        c.Rebuild();
        if (n > 1 && c.MergeCount != 1)
        {
            r.Problems.Add("the receivers didn't merge");
            return r;
        }
        r.Lead = n > 1 ? c.Merges[0].Lead : 0;
        var own = n > 1 ? rec.Sockets.Length : 0;
        int Fit(int on, int socket, ConstructPartDef def, string by, out string why)
        {
            why = ConstructBuild.Place(c, on, socket, def, Array.FindIndex(def.Sockets, s => s.Name == by), 0, out var g) ?? "";
            if (why.Length > 0)
                return -1;
            var i = ConstructBuild.Attach(w, c, def, g);
            c.Rebuild();
            return i;
        }
        r.Splitter = Fit(r.Lead, own + Array.FindIndex(rec.Sockets, s => s.Name == "front"), spl, "back", out var why1);
        if (r.Splitter < 0)
        {
            r.Problems.Add("the splitter wouldn't go on the front: " + why1);
            return r;
        }
        var backIdx = Array.FindIndex(spl.Sockets, s => s.Name == "back");
        for (var s = 0; s < spl.Sockets.Length; s++)
            if (spl.Sockets[s].Dir == 5 && s != backIdx)
                r.Outputs.Add(s);
        foreach (var s in r.Outputs)
        {
            var b = Fit(r.Splitter, s, barrelDef, "back", out var why2);
            if (b < 0)
            {
                r.Problems.Add($"barrel on {spl.Sockets[s].Name}: {why2}");
                continue;
            }
            r.Barrels.Add(b);
            // (a muzzle brake is wider than a 4-way's spacing: every other barrel gets one, the rest fire from the barrel's end)
            if (r.Outputs.Count == 4 && r.Tips.Count % 2 == 1)
            {
                r.Tips.Add(b);
                continue;
            }
            var m = Fit(b, Array.FindIndex(barrelDef.Sockets, q => q.Name == "front"), muzzleDef, "back", out var why3);
            if (m < 0)
                r.Problems.Add($"muzzle on barrel {spl.Sockets[s].Name}: {why3}");
            r.Tips.Add(m < 0 ? b : m);
        }
        var magDef = ConstructParts.Need("Box Magazine");
        r.Mag = Fit(r.Lead, own + Array.FindIndex(rec.Sockets, s => s.Name == "left"), magDef, "right", out var why4);
        if (r.Mag < 0)
            r.Problems.Add("magazine: " + why4);
        else
            Fit(r.Mag, Array.FindIndex(magDef.Sockets, s => s.Name == "left"), ConstructParts.Need("Ammo Box"), "right", out _);
        return r;
    }

    private static void MsOne(int n, string splitterName)
    {
        var tag = $"merged splitter {n}x{n} {splitterName}";
        var spl = MsDef(splitterName);
        if (spl == null)
        {
            Fail($"{tag}: no such splitter part");
            return;
        }
        var m = MsBuild(n, spl, -8);
        var single = MsBuild(1, spl, -12);
        var problems = m.Problems.Concat(single.Problems.Select(p => "(single) " + p)).ToList();
        if (m.Splitter < 0 || single.Splitter < 0)
        {
            foreach (var p in problems)
                Fail($"{tag}: {p}");
            return;
        }
        var c = m.C;
        var w = m.W;
        var barrelDef = ConstructParts.Need("Gun Barrel");
        // 1) The back flush on the merged front, the outputs at the file's layout times N.
        c.SocketAt(m.Lead, c.DefOf(m.Lead).Sockets.Length + Array.FindIndex(c.DefOf(m.Lead).Sockets, s => s.Name == "front"), out var frontAt, out _);
        c.SocketAt(m.Splitter, Array.FindIndex(spl.Sockets, s => s.Name == "back"), out var backAt, out _);
        if (Vector3.Distance(frontAt, backAt) > 0.01f)
            problems.Add($"the splitter's back is {Vector3.Distance(frontAt, backAt):0.000} m off the merged front");
        if (c.PartScale[m.Splitter] != n)
            problems.Add($"the splitter is {c.PartScale[m.Splitter]}x, not {n}x");
        for (var k = 0; k < m.Outputs.Count; k++)
        {
            var s = m.Outputs[k];
            c.SocketAt(m.Splitter, s, out var at, out _);
            var want = c.Parts[m.Splitter].Pos + Rot24.Apply(c.Parts[m.Splitter].Rot, spl.Sockets[s].At * n);
            single.C.SocketAt(single.Splitter, s, out var at1, out _);
            // (the same spacing as the single splitter's, N times: the first output is the reference)
            c.SocketAt(m.Splitter, m.Outputs[0], out var first, out _);
            single.C.SocketAt(single.Splitter, m.Outputs[0], out var first1, out _);
            if (Vector3.Distance(at, want) > 0.01f || Vector3.Distance(at - first, (at1 - first1) * n) > 0.01f)
                problems.Add($"output {spl.Sockets[s].Name} isn't at the scaled spacing");
        }
        for (var k = 0; k < m.Barrels.Count; k++)
        {
            c.SocketAt(m.Splitter, m.Outputs[k], out var sat, out _);
            c.SocketAt(m.Barrels[k], Array.FindIndex(barrelDef.Sockets, q => q.Name == "back"), out var bat, out _);
            if (c.PartScale[m.Barrels[k]] != n || Vector3.Distance(sat, bat) > 0.01f)
                problems.Add($"barrel {k}: {c.PartScale[m.Barrels[k]]}x, {Vector3.Distance(sat, bat):0.000} m off its socket");
            if (c.PartScale[m.Tips[k]] != n)
                problems.Add($"the part at barrel {k}'s tip is {c.PartScale[m.Tips[k]]}x");
        }
        // 4) The numbers: the single splitter gun's through the merge rules once.
        if (c.GunCount != 1 || c.Guns[0].Def == null || single.C.GunCount != 1 || single.C.Guns[0].Def == null)
        {
            problems.Add($"no gun ({(c.GunCount > 0 ? c.Guns[0].Problem : "none")}; single {(single.C.GunCount > 0 ? single.C.Guns[0].Problem : "none")})");
            foreach (var p in problems)
                Fail($"{tag}: {p}");
            return;
        }
        ref var g = ref c.Guns[0];
        var one = single.C.Guns[0];
        var rules = ReceiverMergeRules.Current;
        var wantDamage = Math.Max(1, (int)MathF.Round(one.Def!.Damage * ReceiverMergeRules.Times(n, rules.Damage)));
        var wantRpm = Math.Max(1, (int)MathF.Round(one.Def.Rpm * ReceiverMergeRules.Times(n, rules.FireRate)));
        var wantMag = Math.Max(1, (int)MathF.Round(one.Def.Mag * ReceiverMergeRules.Times(n, rules.Magazine)));
        Line($"{tag}: {m.Outputs.Count} outputs, {g.Shots} shots, {g.MuzzleCount} muzzles, {g.Def!.Damage} damage (single {one.Def.Damage}), {g.Def.Rpm} rpm (single {one.Def.Rpm}), {g.Mag} a load (single {one.Mag})");
        if (g.Def.Damage != wantDamage || g.Def.Rpm != wantRpm || g.Def.Mag != wantMag || g.Shots != one.Shots || g.PerShot != n * n)
            problems.Add($"the numbers aren't the single gun's through the rules once (damage {g.Def.Damage} want {wantDamage}, rpm {g.Def.Rpm} want {wantRpm}, mag {g.Def.Mag} want {wantMag}, shots {g.Shots} want {one.Shots})");
        // 3) Each barrel's rounds leave its own scaled tip.
        if (g.MuzzleCount != m.Outputs.Count || single.C.Guns[0].MuzzleCount != m.Outputs.Count)
            problems.Add($"{g.MuzzleCount} muzzle(s) for {m.Outputs.Count} barrels");
        else
        {
            for (var k = 0; k < m.Outputs.Count; k++)
            {
                var tip = ConstructWeapons.MuzzleRest(c, 0, k);
                c.PartBounds(m.Tips[k], out var mn, out var mx);
                var want = new Vector3((mn.X + mx.X) * 0.5f, (mn.Y + mx.Y) * 0.5f, mn.Z);
                if (Vector3.Distance(tip, want) > 0.01f)
                    problems.Add($"muzzle {k} at {tip} not at the tip {want}");
            }
            MsFire(m, tag, problems);
        }
        // 5) The splitter and barrels are solid at their drawn size.
        MsSolid(m, tag, problems);
        // 6) Taking a cell off: the splitter and its barrels re-seat or pop off, none lost.
        MsUnmerge(m, n, problems);
        foreach (var p in problems)
            Fail($"{tag}: {p}");
    }

    /// <summary>Pulls the trigger (a wire value on the receiver) with no spread: every shot's end lies on the line from its own muzzle.</summary>
    private static void MsFire(MsGun m, string tag, List<string> problems)
    {
        var c = m.C;
        var w = m.W;
        c.Guns[0].Spread = 0;
        var seen = new HashSet<int>();
        var misses = 0;
        for (var t = 0; t < 900 && seen.Count < m.Outputs.Count; t++)
        {
            c.Parts[m.Lead].InCount = 1;
            c.Parts[m.Lead].In = 1;
            w.Step(World.Query, World.TickDt);
            for (var k = 0; k < w.FireCount; k++)
            {
                ref var e = ref w.Fires[k];
                if (e.Kind != CFireKind.Shot || e.Body != c.Id)
                    continue;
                ConstructWeapons.MuzzleWorld(c, 0, e.Muzzle, out var from, out var fwd);
                var along = Vector3.Dot(e.At - from, fwd);
                var off = (e.At - from) - fwd * along;
                if (off.Length() > 0.02f)
                    misses++;
                seen.Add(e.Muzzle);
            }
        }
        c.Parts[m.Lead].InCount = 0;
        c.Parts[m.Lead].In = 0;
        if (seen.Count != m.Outputs.Count)
            problems.Add($"rounds left {seen.Count} of {m.Outputs.Count} muzzles (loaded {c.Parts[m.Lead].Rounds}, reload {c.Parts[m.Lead].Reload}, guns {c.GunCount}, shots {w.ShotsFired})");
        if (misses > 0)
            problems.Add($"{misses} round(s) didn't leave along their own muzzle's line");
    }

    /// <summary>The scaled splitter and each barrel stop a ray at their drawn size, the body's shape bounds hold them.</summary>
    private static void MsSolid(MsGun m, string tag, List<string> problems)
    {
        var c = m.C;
        var w = m.W;
        c.PartBounds(m.Splitter, out var smn, out var smx);
        var half = ConstructParts.Need("Gun Barrel").Boxes[0].Half;
        for (var k = 0; k < m.Barrels.Count; k++)
        {
            var b = m.Barrels[k];
            c.BoxOf(b, 0, out var bc, out var bh);
            var n = c.PartScale[b];
            if (MathF.Abs(bh.Z - half.Z * n) > 0.002f || MathF.Abs(bh.X - half.X * n) > 0.002f)
                problems.Add($"barrel {k}'s solid box is {bh} (its file's {half} x {n})");
            c.SegPose(c.Parts[b].Seg, out var q, out var t);
            var top = bc + new Vector3(0, bh.Y + 1f, 0); // (down onto the barrel's middle from above: its muzzle device is in front)
            var origin = c.ToWorld(t + Vector3.Transform(top, q));
            var dir = Vector3.Normalize(c.ToWorld(t + Vector3.Transform(bc, q)) - origin);
            if (!w.Raycast(origin, dir, 3f, out var hit) || hit.Body != c || hit.Part != b)
                problems.Add($"a ray down onto barrel {k} hits {(w.Raycast(origin, dir, 3f, out var h2) ? $"part {h2.Part}" : "nothing")}, not the barrel");
        }
        var sc = (smn + smx) * 0.5f;
        c.SegPose(c.Parts[m.Splitter].Seg, out var sq, out var st);
        var so = c.ToWorld(st + Vector3.Transform(sc + new Vector3(0, smx.Y - sc.Y + 1f, 0), sq));
        var sd = Vector3.Normalize(c.ToWorld(st + Vector3.Transform(sc, sq)) - so);
        if (!w.Raycast(so, sd, 3f, out var sh) || sh.Body != c || sh.Part != m.Splitter)
            problems.Add("a ray down onto the splitter's top doesn't hit it");
    }

    /// <summary>A 3x3's corner cell off: a 2x2 re-seats the splitter and barrels at 2x (or pops them); the middle then pops everything. No part is lost.</summary>
    private static void MsUnmerge(MsGun m, int n, List<string> problems)
    {
        var c = m.C;
        var w = m.W;
        int Total() => w.Bodies.Sum(b => b.PartCount);
        // Every receiver cell, last added first; the splitter, barrels and so on stay.
        var cells = new List<int>();
        for (var i = 0; i < c.PartCount; i++)
            if (c.DefOf(i).IsReceiverGun)
                cells.Add(c.Parts[i].Tag);
        var guns = 0;
        for (var round = 0; round < 2; round++)
        {
            var total = Total();
            var tagToRemove = cells[^1];
            cells.RemoveAt(cells.Count - 1);
            var idx = -1;
            for (var i = 0; i < c.PartCount; i++)
                if (c.Parts[i].Tag == tagToRemove)
                    idx = i;
            if (idx < 0)
                break;
            w.RemovePart(c, idx);
            if (w.Bodies.Contains(c) && c.Dirty)
                c.Rebuild();
            // (other bodies: what popped off, and what the removal split away, all kept)
            var after = Total();
            if (after < total - 1)
                problems.Add($"removing a cell lost {total - 1 - after} part(s) ({total} -> {after})");
            var spl2 = Enumerable.Range(0, c.PartCount).Count(i => c.DefOf(i) == m.Spl);
            Line($"  unmerge {n}x{n} {m.Spl.Name}, cell {round + 1} off: {total} -> {after} parts in all, {c.MergeCount} merge(s){(c.MergeCount == 1 ? $" {c.Merges[0].N}x" : "")}, splitter {(spl2 > 0 ? "still on" : "popped off")}");
            if (n == 3 && round == 0 && (c.MergeCount != 1 || spl2 != 1))
                problems.Add("a 3x3 less a corner is a 2x2 that keeps the splitter and its barrels");
            if (c.MergeCount == 1)
            {
                var sp = -1;
                for (var i = 0; i < c.PartCount; i++)
                    if (c.DefOf(i) == m.Spl)
                        sp = i;
                if (sp >= 0)
                {
                    var nn = c.Merges[0].N;
                    if (c.PartScale[sp] != nn)
                        problems.Add($"after a cell came off the splitter is {c.PartScale[sp]}x on a {nn}x merge");
                    var barrelDef = ConstructParts.Need("Gun Barrel");
                    for (var i = 0; i < c.PartCount; i++)
                        if (c.DefOf(i) == barrelDef && c.PartScale[i] != nn && c.PartScale[i] > 1)
                            problems.Add($"a barrel is {c.PartScale[i]}x on a {nn}x merge");
                    foreach (var l in Enumerable.Range(0, c.LinkCount))
                    {
                        ref var lk = ref c.Links[l];
                        if (lk.A != sp && lk.B != sp)
                            continue;
                        c.SocketAt(lk.A, lk.SA, out var pa, out _);
                        c.SocketAt(lk.B, lk.SB, out var pb, out _);
                        if (Vector3.Distance(pa, pb) > 0.01f)
                            problems.Add($"after a cell came off a link on the splitter is {Vector3.Distance(pa, pb):0.000} m apart");
                    }
                }
                guns++;
            }
        }
        _ = n;
        _ = guns;
    }

    /// <summary>
    /// The player's path for the same gun (the held part, <see cref="Program.SandboxHeldFrame"/>): a splitter in hand aimed at a
    /// merged receiver's front shows a ghost and the click fits it; a barrel in hand aimed at each splitter output shows the ghost
    /// on that output, and the click fits a barrel N times bigger with its back on the socket.
    /// </summary>
    private static void MsHeldPath(Map yard)
    {
        yard.RestoreProps();
        World.SetMap(yard);
        if (yard.Sandbox is { } sb)
            sb.DebrisSeconds = 0;
        var now = 0.0;
        using var g = ModeGame(0, GameMode.Sandbox, yard.Id);
        Run(g, ref now, Game.CountdownSeconds + 0.3);
        g.RequestRespawn();
        Run(g, ref now, 1.0);
        if (g.Local is not { Alive: true } me || OpenSpot(g.Constructs) is not { } spot)
        {
            Fail("merged splitter held path: no local player or no open spot in the Sandbox game");
            return;
        }
        var w = g.Constructs;
        var rec = ConstructParts.Need("Rifle Receiver");
        var barrelDef = ConstructParts.Need("Gun Barrel");
        var problems = new List<string>();
        void Hold(ConstructPartDef d)
        {
            g.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.None);
            g.SandboxSend(CActKind.Tool, part: (byte)d.Tool);
            g.SandboxTakePart(d.Id);
            Run(g, ref now, 0.2);
        }
        // Aims at a rest-frame point of construct c from in front of it (its -Z side); fire: a fresh click.
        string Aim(Construct c, Vector3 rest, bool fire, out int sel, out bool valid)
        {
            c.SegPose(0, out var q, out var t);
            var target = c.ToWorld(t + Vector3.Transform(rest, q));
            var from = target + new Vector3(0.1f, 0.3f, -1.8f);
            me.Teleport(ConstructWorld.Ground(World.Query, from.X, from.Z) + new Vector3(0, 0.05f, 0));
            Run(g, ref now, 0.03);
            var eye = me.RenderEye;
            var dir = Vector3.Normalize(target - eye);
            var vp = Matrix4x4.CreateLookAt(eye, eye + dir, Vector3.UnitY) * Matrix4x4.CreatePerspectiveFieldOfView(1.2f, 16f / 9f, 0.1f, 100f);
            var look = Program.SandboxHeldFrame(g, eye, dir, new Vector2(1920, 1080), vp, fire, now, out sel, out valid);
            if (fire)
                Run(g, ref now, 1.8);
            return look;
        }
        foreach (var (n, splitterName) in new[] { (2, "Pyramid Splitter"), (3, "Twin Splitter") })
        {
            var tag = $"{n}x{n} {splitterName}";
            var spl = MsDef(splitterName)!;
            while (w.Bodies.Count > 0)
                w.Remove(w.Bodies[^1]);
            var c = w.Create(spot + new Vector3(0, 0.6f, 0), Quaternion.Identity)!;
            var size = rec.BoundsMax - rec.BoundsMin;
            for (var y = 0; y < n; y++)
                for (var x = 0; x < n; x++)
                    ConstructBuild.AutoLink(w, c, w.AddPart(c, rec, new Vector3(x * size.X, y * size.Y, 0), 0));
            c.Rebuild();
            if (c.MergeCount != 1)
            {
                problems.Add($"{tag}: no merge");
                continue;
            }
            var lead = c.Merges[0].Lead;
            ConstructMerge.MergedBounds(c, c.Merges[0], out var mc, out var mh);
            // The splitter on the merged front.
            Hold(spl);
            var look = Aim(c, new Vector3(mc.X, mc.Y, mc.Z - mh.Z + 0.001f), false, out var sel, out var valid);
            if (sel < 0 || !valid)
            {
                problems.Add($"{tag}: the splitter shows no ghost on the merged front (selected {sel}, valid {valid}; \"{look}\")");
                continue;
            }
            var before = c.PartCount;
            Aim(c, new Vector3(mc.X, mc.Y, mc.Z - mh.Z + 0.001f), true, out _, out _);
            if (c.PartCount != before + 1)
            {
                problems.Add($"{tag}: the click didn't fit the splitter (\"{g.SandboxNotice}\")");
                continue;
            }
            var sp = c.PartCount - 1;
            if (c.PartScale[sp] != n)
                problems.Add($"{tag}: the splitter is {c.PartScale[sp]}x");
            // A barrel on each output.
            var shown = 0;
            var fitted = 0;
            var outs = 0;
            for (var s = 0; s < spl.Sockets.Length; s++)
            {
                if (spl.Sockets[s].Dir != 5 || spl.Sockets[s].Name == "back")
                    continue;
                outs++;
                Hold(barrelDef);
                c.SocketAt(sp, s, out var at, out _);
                look = Aim(c, at + new Vector3(0, 0, 0.002f), false, out sel, out valid);
                if (sel < 0 || !valid || ConstructRender.Ghosts[sel].Part != sp || ConstructRender.Ghosts[sel].Socket != s)
                {
                    problems.Add($"{tag}: a barrel aimed at the splitter's {spl.Sockets[s].Name} shows {(sel < 0 ? "no ghost" : $"a ghost on part {ConstructRender.Ghosts[sel].Part} socket {ConstructRender.Ghosts[sel].Socket}")} (valid {valid}; \"{look}\")");
                    continue;
                }
                shown++;
                before = c.PartCount;
                Aim(c, at + new Vector3(0, 0, 0.002f), true, out _, out _);
                if (c.PartCount != before + 1)
                {
                    problems.Add($"{tag}: the click on the splitter's {spl.Sockets[s].Name} fitted nothing (\"{g.SandboxNotice}\")");
                    continue;
                }
                var b = c.PartCount - 1;
                c.SocketAt(b, Array.FindIndex(barrelDef.Sockets, q => q.Name == "back"), out var bat, out _);
                c.SocketAt(sp, s, out var sat, out _);
                if (c.PartScale[b] != n || Vector3.Distance(bat, sat) > 0.01f)
                    problems.Add($"{tag}: the barrel on {spl.Sockets[s].Name} is {c.PartScale[b]}x and {Vector3.Distance(bat, sat):0.000} m off its socket");
                else
                    fitted++;
                w.RemovePart(c, b);
            }
            Line($"held path {tag}: ghost on {shown} of {outs} outputs, {fitted} fitted at {n}x");
            if (outs == 0 || shown != outs || fitted != outs)
                problems.Add($"{tag}: only {shown} of {outs} outputs showed a ghost and {fitted} took a scaled barrel");
        }
        foreach (var p in problems)
            Fail("merged splitter held path: " + p);
    }
}
