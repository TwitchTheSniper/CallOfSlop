using System.Globalization;
using System.Numerics;
using System.Text;
using CallOfSlop.Story;

namespace CallOfSlop;

/// <summary>
/// <c>--check-building NAME[,NAME...]|all</c>: can a standing person use the building? (docs/sandbox/constructs/buildings.md
/// "Laying out a building"). The blueprint is built headless as the game builds it, and its solid boxes are laid on a grid
/// of 12.5 cm columns. Each surface in a column (the ground, a floor, a step) is a place a body may stand when a standing
/// player (<see cref="World.PlayerRadius"/>, <see cref="World.PlayerHeight"/>) fits over it; from one column to the next
/// a body walks up at most <see cref="World.StepHeight"/> and drops any height, as the game's walk does. Door leaves are
/// left out (F opens them). From the ground round the building it finds where a person can go and come back from, then
/// says what they can't reach: a room's floor, a doorway, a stair's top, a seat or a store, and where the way is narrower
/// than a comfortable 1.1 m. Each straight stair is then climbed by the game's own walker (BlockPlayer), standing, off its
/// middle line as a person who turned onto it would be. A map of each level goes to <c>building-walk.txt</c> next to the
/// log. It changes nothing; the exit code is the number of buildings with a failure.
/// </summary>
public static class BuildingWalkCheck
{
    private const float G = 0.125f; // the grid's step
    private const float Comfort = 0.55f; // a body this wide (1.1 m) passes without brushing
    private const float Reach = 1.6f; // a seat, a store or a switch is used from this close (horizontally)
    private const float Margin = 2.5f; // ground round the building

    private struct Box
    {
        public Vector3 Min, Max;
        public int Part;
    }

    private struct Node
    {
        public float Top;
        public int Part; // the part whose top it is (-1: the ground)
        public bool Fit, Wide;
        public byte Reach; // 1: reached from outside, 2: gets back out, 3: both
    }

    public static int Cli(string arg)
    {
        var names = arg.Equals("all", StringComparison.OrdinalIgnoreCase)
            ? ConstructBuild.Blueprints.Where(IsBuilding).Select(b => b.Name).ToList()
            : arg.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        var map = new StringBuilder();
        var bad = 0;
        var lore = StoryRules.LoadFolder().Lore.Items; // (the story's lore items stand in the buildings at run time: Story/Lore)
        foreach (var name in names)
        {
            var bp = ConstructBuild.FindBlueprint(name);
            if (bp == null)
            {
                Say($"[building] {name}: no such blueprint");
                bad++;
                continue;
            }
            var (fails, notes) = Check(bp, map, lore.Where(t => t.In.Equals(bp.Name, StringComparison.OrdinalIgnoreCase)).ToList());
            Say($"[building] {bp.Name}: {(fails.Count == 0 ? "ok" : $"{fails.Count} FAIL")}");
            foreach (var f in fails)
                Say($"[building]   FAIL {f}");
            foreach (var n in notes)
                Say($"[building]   {n}");
            if (fails.Count > 0)
                bad++;
        }
        var file = Path.Combine(AppContext.BaseDirectory, "building-walk.txt");
        File.WriteAllText(file, map.ToString());
        Say($"[building] {names.Count} checked, {bad} with failures; maps in {file}");
        return bad;
    }

    private static void Say(string s)
    {
        Console.WriteLine(s); // (the game's console goes to callofslop.log as well)
    }

    /// <summary>A blueprint that stands on foundations and has a door or a floor: a building, not a vehicle.</summary>
    private static bool IsBuilding(ConstructBuild.Blueprint bp)
    {
        if (bp.Steps.Count == 0 || ConstructParts.Find(bp.Steps[0].Part) is not { } first || first.Role != PartRole.Foundation)
            return false;
        return bp.Steps.Any(s => ConstructParts.Find(s.Part) is { } d && (d.Role == PartRole.Door || d.Walk is WalkKind.Stairs));
    }

    public static (List<string> Fails, List<string> Notes) Check(ConstructBuild.Blueprint bp, StringBuilder map, List<LoreDef> lore)
    {
        var fails = new List<string>();
        var notes = new List<string>();
        var w = new ConstructWorld();
        var c = ConstructBuild.BuildOnGround(w, bp, new Vector3(0, SelfTest.BuildingY, 0), 0, out var why);
        if (c == null)
        {
            fails.Add($"it doesn't build: {why}");
            return (fails, notes);
        }
        var boxes = new List<Box>();
        var lo = new Vector3(float.MaxValue);
        var hi = new Vector3(float.MinValue);
        for (var i = 0; i < c.PartCount; i++)
        {
            if (c.DefOf(i).Role == PartRole.Door)
                continue; // (F opens it)
            c.WorldFrame(c.Parts[i].Seg, out var q, out var t);
            var m = Matrix4x4.CreateFromQuaternion(q);
            for (var k = 0; k < c.SolidCount(i); k++)
            {
                c.SolidBox(i, k, out var centre, out var half);
                var wc = t + Vector3.Transform(centre, q);
                var wh = new Vector3(
                    MathF.Abs(m.M11) * half.X + MathF.Abs(m.M21) * half.Y + MathF.Abs(m.M31) * half.Z,
                    MathF.Abs(m.M12) * half.X + MathF.Abs(m.M22) * half.Y + MathF.Abs(m.M32) * half.Z,
                    MathF.Abs(m.M13) * half.X + MathF.Abs(m.M23) * half.Y + MathF.Abs(m.M33) * half.Z);
                boxes.Add(new Box { Min = wc - wh, Max = wc + wh, Part = i });
                lo = Vector3.Min(lo, wc - wh);
                hi = Vector3.Max(hi, wc + wh);
            }
        }
        // The lore items the story stands in it (GameStoryLore: "at" in the building's frame, turned by -yaw), each as its bounds
        // (part -2 - k: no part of the building).
        var loreBox = new List<(Vector3 Min, Vector3 Max, string Name)>();
        c.WorldFrame(0, out var bq, out var bt);
        foreach (var t in lore)
        {
            if (ConstructParts.Find(t.Part) is not { } ld)
                continue;
            var turn = bq * Quaternion.CreateFromAxisAngle(Vector3.UnitY, -t.Yaw * MathF.PI / 180f);
            var at = bt + Vector3.Transform(new Vector3(t.At[0], t.At[1], t.At[2]), bq);
            Vector3 mn = new(float.MaxValue), mx = new(float.MinValue);
            for (var k = 0; k < 8; k++)
            {
                var corner = new Vector3((k & 1) != 0 ? ld.BoundsMax.X : ld.BoundsMin.X, (k & 2) != 0 ? ld.BoundsMax.Y : ld.BoundsMin.Y, (k & 4) != 0 ? ld.BoundsMax.Z : ld.BoundsMin.Z);
                var wpt = at + Vector3.Transform(corner, turn);
                (mn, mx) = (Vector3.Min(mn, wpt), Vector3.Max(mx, wpt));
            }
            Vector3 imn = mn + new Vector3(0.01f, 0.03f, 0.01f), imx = mx - new Vector3(0.01f, 0, 0.01f);
            foreach (var bb in boxes)
                if (bb.Part >= 0 && bb.Min.X < imx.X && bb.Max.X > imn.X && bb.Min.Y < imx.Y && bb.Max.Y > imn.Y && bb.Min.Z < imx.Z && bb.Max.Z > imn.Z)
                {
                    fails.Add($"the lore item {t.Part} ({t.Id}) stands inside the {c.DefOf(bb.Part).Name}: move its \"at\" (Story/Lore)");
                    break;
                }
            boxes.Add(new Box { Min = mn, Max = mx, Part = -2 - loreBox.Count });
            loreBox.Add((mn, mx, $"{t.Part} ({t.Id})"));
        }
        var x0 = lo.X - Margin;
        var z0 = lo.Z - Margin;
        var nx = (int)MathF.Ceiling((hi.X - lo.X + 2 * Margin) / G);
        var nz = (int)MathF.Ceiling((hi.Z - lo.Z + 2 * Margin) / G);
        // Buckets of 1 m: the boxes near each column.
        const float B = 1f;
        var bx = (int)MathF.Ceiling(nx * G / B) + 1;
        var bz = (int)MathF.Ceiling(nz * G / B) + 1;
        var bucket = new List<int>[bx * bz];
        for (var k = 0; k < bucket.Length; k++)
            bucket[k] = [];
        for (var b = 0; b < boxes.Count; b++)
        {
            var bb = boxes[b];
            for (var ix = Math.Max(0, (int)((bb.Min.X - x0 - Comfort) / B)); ix <= Math.Min(bx - 1, (int)((bb.Max.X - x0 + Comfort) / B)); ix++)
                for (var iz = Math.Max(0, (int)((bb.Min.Z - z0 - Comfort) / B)); iz <= Math.Min(bz - 1, (int)((bb.Max.Z - z0 + Comfort) / B)); iz++)
                    bucket[ix * bz + iz].Add(b);
        }
        List<int> Near(float x, float z) => bucket[Math.Clamp((int)((x - x0) / B), 0, bx - 1) * bz + Math.Clamp((int)((z - z0) / B), 0, bz - 1)];
        var groundY = SelfTest.BuildingY;

        bool Blocked(float x, float z, float top, float r)
        {
            // The feet: walking in, a body steps up onto any top its disc reaches a step above them (a steep stair lifts
            // it before the next step's riser is in the way), so they stand on the highest such top.
            var feet = top;
            for (var pass = 0; pass < 4; pass++)
            {
                var lifted = feet;
                foreach (var b in Near(x, z))
                {
                    var bb = boxes[b];
                    if (bb.Max.Y <= lifted + 1e-3f || bb.Max.Y > feet + World.StepHeight + 1e-3f)
                        continue;
                    var dx = MathF.Max(0, MathF.Max(bb.Min.X - x, x - bb.Max.X));
                    var dz = MathF.Max(0, MathF.Max(bb.Min.Z - z, z - bb.Max.Z));
                    if (dx * dx + dz * dz < World.PlayerRadius * World.PlayerRadius)
                        lifted = bb.Max.Y;
                }
                if (lifted <= feet + 1e-3f)
                    break;
                feet = lifted;
            }
            foreach (var b in Near(x, z))
            {
                var bb = boxes[b];
                if (bb.Max.Y <= feet + World.StepHeight + 1e-3f || bb.Min.Y >= feet + World.PlayerHeight)
                    continue;
                var dx = MathF.Max(0, MathF.Max(bb.Min.X - x, x - bb.Max.X));
                var dz = MathF.Max(0, MathF.Max(bb.Min.Z - z, z - bb.Max.Z));
                if (dx * dx + dz * dz < r * r - 1e-4f)
                    return true;
            }
            return false;
        }

        // The surfaces of each column.
        var cols = new Node[nx * nz][];
        for (var ix = 0; ix < nx; ix++)
            for (var iz = 0; iz < nz; iz++)
            {
                var x = x0 + (ix + 0.5f) * G;
                var z = z0 + (iz + 0.5f) * G;
                var tops = new List<(float Top, int Part)>();
                var near = Near(x, z);
                var covered = false;
                foreach (var b in near)
                {
                    var bb = boxes[b];
                    if (x < bb.Min.X || x > bb.Max.X || z < bb.Min.Z || z > bb.Max.Z)
                        continue;
                    tops.Add((bb.Max.Y, bb.Part));
                    if (bb.Min.Y <= groundY + 0.1f)
                        covered = true;
                }
                if (!covered)
                    tops.Add((groundY, -1));
                var list = new List<Node>();
                foreach (var (top, part) in tops.OrderBy(t => t.Top))
                {
                    if (list.Count > 0 && MathF.Abs(list[^1].Top - top) < 0.02f)
                        continue;
                    var inside = false; // (a top inside another box is no surface)
                    foreach (var b in near)
                    {
                        var bb = boxes[b];
                        if (x > bb.Min.X && x < bb.Max.X && z > bb.Min.Z && z < bb.Max.Z && top + 0.02f > bb.Min.Y && top + 0.02f < bb.Max.Y)
                        {
                            inside = true;
                            break;
                        }
                    }
                    if (inside)
                        continue;
                    var fit = !Blocked(x, z, top, World.PlayerRadius);
                    list.Add(new Node { Top = top, Part = part, Fit = fit, Wide = fit && !Blocked(x, z, top, Comfort) });
                }
                cols[ix * nz + iz] = [.. list];
            }

        // Where a body goes from a node: the highest surface a step up or lower in the next column, if it fits there.
        int Next(int col, float top)
        {
            var best = -1;
            var n = cols[col];
            for (var k = 0; k < n.Length; k++)
                if (n[k].Top <= top + World.StepHeight + 1e-3f)
                    best = k;
            return best >= 0 && n[best].Fit ? best : -1;
        }
        ReadOnlySpan<(int, int)> dirs = [(1, 0), (-1, 0), (0, 1), (0, -1)];
        // Forward: from the ground round the edge. Back: the reverse edges, into the same edge.
        var fwd = new Queue<(int, int)>();
        for (var ix = 0; ix < nx; ix++)
            for (var iz = 0; iz < nz; iz++)
                if (ix == 0 || iz == 0 || ix == nx - 1 || iz == nz - 1)
                {
                    var col = ix * nz + iz;
                    for (var k = 0; k < cols[col].Length; k++)
                        if (cols[col][k].Part == -1 && cols[col][k].Fit)
                        {
                            cols[col][k].Reach |= 3;
                            fwd.Enqueue((col, k));
                        }
                }
        var back = new Queue<(int, int)>(fwd);
        while (fwd.Count > 0)
        {
            var (col, k) = fwd.Dequeue();
            int ix = col / nz, iz = col % nz;
            foreach (var (dx, dz) in dirs)
            {
                int jx = ix + dx, jz = iz + dz;
                if (jx < 0 || jz < 0 || jx >= nx || jz >= nz)
                    continue;
                var nc = jx * nz + jz;
                var nk = Next(nc, cols[col][k].Top);
                if (nk >= 0 && (cols[nc][nk].Reach & 1) == 0)
                {
                    cols[nc][nk].Reach |= 1;
                    fwd.Enqueue((nc, nk));
                }
            }
        }
        while (back.Count > 0)
        {
            var (col, k) = back.Dequeue();
            int ix = col / nz, iz = col % nz;
            foreach (var (dx, dz) in dirs)
            {
                int jx = ix + dx, jz = iz + dz;
                if (jx < 0 || jz < 0 || jx >= nx || jz >= nz)
                    continue;
                var nc = jx * nz + jz;
                for (var nk = 0; nk < cols[nc].Length; nk++)
                    if ((cols[nc][nk].Reach & 2) == 0 && cols[nc][nk].Fit && Next(col, cols[nc][nk].Top) == k)
                    {
                        cols[nc][nk].Reach |= 2;
                        back.Enqueue((nc, nk));
                    }
            }
        }
        bool Usable(in Node n) => n.Fit && n.Reach == 3;

        // 1. Floors under a roof that nobody can stand on and leave: rooms cut off (a roof's top is left alone).
        bool Indoor(float x, float z, float top)
        {
            foreach (var b in Near(x, z))
            {
                var bb = boxes[b];
                if (x >= bb.Min.X && x <= bb.Max.X && z >= bb.Min.Z && z <= bb.Max.Z && bb.Min.Y > top + 1.5f && bb.Min.Y < top + 4.5f)
                    return true;
            }
            return false;
        }
        // A floor under something a body steps up onto (a bunk, a low crate) is the same place as that top: the walk takes the top.
        bool SteppedOver(int col, int k)
        {
            var n = cols[col];
            for (var j = k + 1; j < n.Length; j++)
                if (n[j].Top <= n[k].Top + World.StepHeight + 1e-3f && Usable(n[j]))
                    return true;
            return false;
        }
        var seen = new HashSet<(int, int)>();
        for (var col = 0; col < cols.Length; col++)
            for (var k = 0; k < cols[col].Length; k++)
            {
                var n = cols[col][k];
                if (!n.Fit || Usable(n) || n.Part < 0 || !Ground(c.DefOf(n.Part)) || seen.Contains((col, k)) || SteppedOver(col, k))
                    continue;
                // The pocket: fitting nodes at about this level joined to it, not usable.
                var pocket = new List<(int, int)>();
                var q = new Queue<(int, int)>();
                q.Enqueue((col, k));
                seen.Add((col, k));
                while (q.Count > 0)
                {
                    var (cc, kk) = q.Dequeue();
                    pocket.Add((cc, kk));
                    int ix = cc / nz, iz = cc % nz;
                    foreach (var (dx, dz) in dirs)
                    {
                        int jx = ix + dx, jz = iz + dz;
                        if (jx < 0 || jz < 0 || jx >= nx || jz >= nz)
                            continue;
                        var nc = jx * nz + jz;
                        for (var nk = 0; nk < cols[nc].Length; nk++)
                        {
                            var o = cols[nc][nk];
                            if (o.Fit && !Usable(o) && MathF.Abs(o.Top - cols[cc][kk].Top) <= World.StepHeight && seen.Add((nc, nk)))
                                q.Enqueue((nc, nk));
                        }
                    }
                }
                var area = pocket.Count * G * G;
                pocket.RemoveAll(t => cols[t.Item1][t.Item2].Part < 0);
                if (pocket.Count == 0)
                    continue;
                var (pc, pk) = pocket[pocket.Count / 2];
                var px = x0 + (pc / nz + 0.5f) * G;
                var pz = z0 + (pc % nz + 0.5f) * G;
                if (area >= 0.75f && Indoor(px, pz, cols[pc][pk].Top))
                    fails.Add($"{area:0.0} m2 of floor at {Spot(c, px, cols[pc][pk].Top, pz)} on {c.DefOf(cols[pc][pk].Part).Name} can't be reached and left standing up");
            }

        // 2. Doorways: a person stands on both sides of every door leaf.
        for (var i = 0; i < c.PartCount; i++)
        {
            if (c.DefOf(i).Role != PartRole.Door)
                continue;
            c.ShapeBounds(i, out var dmin, out var dmax);
            c.WorldFrame(c.Parts[i].Seg, out var q, out var t);
            var a = t + Vector3.Transform(dmin, q);
            var b2 = t + Vector3.Transform(dmax, q);
            var mn = Vector3.Min(a, b2);
            var mx = Vector3.Max(a, b2);
            var mid = (mn + mx) * 0.5f;
            var thinX = mx.X - mn.X < mx.Z - mn.Z;
            foreach (var side in new[] { -1f, 1f })
            {
                var p = mid + (thinX ? new Vector3(side * 0.9f, 0, 0) : new Vector3(0, 0, side * 0.9f));
                if (!UsableNear(p.X, p.Z, mn.Y - 0.75f, mn.Y + 0.6f, 0.2f))
                    fails.Add($"nobody stands {(side < 0 ? "on one side" : "on the other side")} of the {c.DefOf(i).Name} at {Spot(c, mid.X, mn.Y, mid.Z)}: the doorway's landing is blocked or cut off");
            }
        }

        bool UsableNear(float x, float z, float yLo, float yHi, float within)
        {
            for (var ix = Math.Max(0, (int)((x - within - x0) / G)); ix <= Math.Min(nx - 1, (int)((x + within - x0) / G)); ix++)
                for (var iz = Math.Max(0, (int)((z - within - z0) / G)); iz <= Math.Min(nz - 1, (int)((z + within - z0) / G)); iz++)
                    foreach (var n in cols[ix * nz + iz])
                        if (Usable(n) && n.Top >= yLo && n.Top <= yHi)
                            return true;
            return false;
        }

        // 3. What people use: seats, stores, switches, terminals, within reach of somewhere a person can stand.
        for (var i = 0; i < c.PartCount; i++)
        {
            var d = c.DefOf(i);
            var used = d.Role is PartRole.Seat or PartRole.Storage || (d.Role == PartRole.Logic && d.Logic is CLogic.Switch or CLogic.Button);
            if (!used)
                continue;
            c.ShapeBounds(i, out var pmin, out var pmax);
            c.WorldFrame(c.Parts[i].Seg, out var q, out var t);
            var a = t + Vector3.Transform(pmin, q);
            var b2 = t + Vector3.Transform(pmax, q);
            var mn = Vector3.Min(a, b2);
            var mx = Vector3.Max(a, b2);
            var ok = Reached(mn, mx);
            if (!ok)
                fails.Add($"nobody reaches the {d.Name} at {Spot(c, (mn.X + mx.X) / 2, mn.Y, (mn.Z + mx.Z) / 2)}");
        }

        foreach (var (mn, mx, name) in loreBox)
            if (!Reached(mn, mx))
                fails.Add($"nobody reaches the lore item {name} at {Spot(c, (mn.X + mx.X) / 2, mn.Y, (mn.Z + mx.Z) / 2)} (Story/Lore)");

        bool Reached(Vector3 mn, Vector3 mx)
        {
            var ok = false;
            for (var ix = Math.Max(0, (int)((mn.X - Reach - x0) / G)); ix <= Math.Min(nx - 1, (int)((mx.X + Reach - x0) / G)) && !ok; ix++)
                for (var iz = Math.Max(0, (int)((mn.Z - Reach - z0) / G)); iz <= Math.Min(nz - 1, (int)((mx.Z + Reach - z0) / G)) && !ok; iz++)
                {
                    var x = x0 + (ix + 0.5f) * G;
                    var z = z0 + (iz + 0.5f) * G;
                    var dx = MathF.Max(0, MathF.Max(mn.X - x, x - mx.X));
                    var dz = MathF.Max(0, MathF.Max(mn.Z - z, z - mx.Z));
                    if (dx * dx + dz * dz > Reach * Reach)
                        continue;
                    foreach (var n in cols[ix * nz + iz])
                        if (Usable(n) && n.Top > mn.Y - 2.2f && n.Top < mx.Y + 0.3f)
                        {
                            ok = true;
                            break;
                        }
                }
            return ok;
        }

        // 4. Stairs: a standing person climbs each straight flight from its foot to its head (the game's own walker).
        for (var i = 0; i < c.PartCount; i++)
        {
            var d = c.DefOf(i);
            if (d.Walk != WalkKind.Stairs || d.Name.Contains("Half-Turn", StringComparison.Ordinal))
                continue;
            Freeform.PartWorld(c, i, out var pos, out var rot);
            var up = Vector3.Transform(Vector3.UnitZ, rot);
            var side = Vector3.Transform(Vector3.UnitX, rot);
            var rise = d.BoundsMax.Y - d.BoundsMin.Y;
            var width = d.BoundsMax.X - d.BoundsMin.X;
            var midX = (d.BoundsMin.X + d.BoundsMax.X) * 0.5f;
            var foot = pos + Vector3.Transform(new Vector3(midX, d.BoundsMin.Y, d.BoundsMin.Z - 0.7f), rot);
            var worst = float.MaxValue;
            const float off = 0.15f; // (a person who turned onto it from the side is this far off its middle)
            foreach (var o in new[] { 0f, -off, off })
            {
                var start = foot + side * o + new Vector3(0, 0.01f, 0);
                var s = SelfTest.BuildingWalk(w, start, _ => up, (int)(3f * World.TickRate), World.PlayerHeight, out var top);
                worst = MathF.Min(worst, top - start.Y);
            }
            if (worst < rise - 0.1f)
                fails.Add($"a standing person doesn't get up the {d.Name} at {Spot(c, foot.X, foot.Y, foot.Z)}: {worst:0.00} of {rise:0.00} m (off its middle by up to {off:0.00} m)");
            else
                notes.Add($"{d.Name}: climbed standing, {rise:0.00} m, {width:0.00} m wide");
        }

        // 5. Narrow places: where a person fits but brushes both sides (under 1.1 m), indoors.
        var narrow = 0;
        var usable = 0;
        for (var col = 0; col < cols.Length; col++)
            foreach (var n in cols[col])
                if (Usable(n) && n.Part >= 0)
                {
                    usable++;
                    if (!n.Wide)
                        narrow++;
                }
        notes.Add($"{usable * G * G:0} m2 a person can stand on and leave from; {narrow * G * G:0.0} m2 of it under 1.1 m wide");

        // The maps: each level of surfaces, one character a column.
        map.AppendLine($"== {bp.Name} (x across, z down; '.' usable, ',' narrow, 'o' fits but cut off, '#' no room to stand, ' ' ground, 'S' stair)");
        var levels = cols.SelectMany(n => n).Where(n => n.Part >= 0 && n.Fit).Select(n => MathF.Round((n.Top - SelfTest.BuildingY) * 2) / 2).Distinct().OrderBy(v => v).ToList();
        var bands = new List<float>();
        foreach (var l in levels)
            if (bands.Count == 0 || l - bands[^1] > 1.5f)
                bands.Add(l);
        foreach (var band in bands)
        {
            map.AppendLine($"-- level {band:0.0} m");
            for (var iz = 0; iz < nz; iz += 2)
            {
                var line = new StringBuilder();
                for (var ix = 0; ix < nx; ix += 2)
                {
                    var ch = ' ';
                    foreach (var n in cols[ix * nz + iz])
                    {
                        var h = n.Top - SelfTest.BuildingY;
                        if (n.Part < 0 || h < band - 0.25f || h >= band + 1.5f)
                            continue;
                        ch = !n.Fit ? '#' : !Usable(n) ? 'o' : c.DefOf(n.Part).Walk == WalkKind.Stairs ? 'S' : n.Wide ? '.' : ',';
                    }
                    line.Append(ch);
                }
                if (line.ToString().Trim().Length > 0)
                    map.AppendLine(line.ToString().TrimEnd());
            }
        }
        return (fails, notes);
    }

    /// <summary>A part people walk on as ground (a floor, a foundation, a stair, a ramp), not a table's top.</summary>
    private static bool Ground(ConstructPartDef d) => d.Walk != WalkKind.None || d.Role == PartRole.Foundation;

    /// <summary>A world point as the blueprint's own metres (x, up, z from its first part), for the generator's author.</summary>
    private static string Spot(Construct c, float x, float y, float z)
    {
        var l = c.ToLocal(new Vector3(x, y, z));
        return string.Create(CultureInfo.InvariantCulture, $"({l.X:0.0}, {l.Y:0.0}, {l.Z:0.0})");
    }
}
