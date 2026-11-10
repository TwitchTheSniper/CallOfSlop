using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>The piece of a key, or a test failure.</summary>
    private static ConstructPartDef ExPiece(int preset, int w, int h, int l, string material = "metal", bool growing = false) =>
        Extrusion.Def(new ExtrusionKey(Extrusion.Presets[preset].Mask, (ushort)w, (ushort)h, (ushort)l, RecyclingTable.Current.IdOf(material), growing))
        ?? throw new InvalidOperationException($"no extrusion {preset} {w}x{h}x{l} {material}");

    private static Construct? ExBody(ConstructWorld w, ConstructPartDef d, Vector3 pos, Quaternion rot, bool pinned)
    {
        var c = ConstructBuild.PlaceNew(w, d, new FreePlacement { Pos = pos, Rot = rot, Anchor = pinned });
        c?.Rebuild();
        return c;
    }

    /// <summary>A loose piece as the physics gun leaves it: frozen where it is, and free to be welded.</summary>
    private static Construct? ExLoose(ConstructWorld w, ConstructPartDef d, Vector3 pos, Quaternion rot)
    {
        var c = ExBody(w, d, pos, rot, false);
        if (c != null)
        {
            c.Frozen = true;
            c.Rebuild();
        }
        return c;
    }

    /// <summary>The bits, parts and files: the table, the sections, the machines' parts, every kind of bad file refused with a reason.</summary>
    private static void ExtrusionData(Action<bool, string> want)
    {
        var table = ExtrusionTable.Current;
        var names = RecyclingTable.Current;
        want(table.Problem.Length == 0 && table.Find(names.IdOf("metal")) != null && table.Find(names.IdOf("planks")) != null, $"the shipped table (\"{table.Problem}\")");
        want(table.Find(names.IdOf("rubber")) == null && table.Find(0) == null, "rubber or nothing can be extruded");
        (string Json, string Want)[] bad =
        [
            ("{", "depth"), ("{\"format\": \"nope\"}", "format"), ("{\"format\": \"call-of-slop-extrusion/1\"}", "materials"),
            ("{\"format\": \"call-of-slop-extrusion/1\", \"materials\": [{\"material\": \"metal\"}]}", "unitsPerM3"),
            ("{\"format\": \"call-of-slop-extrusion/1\", \"materials\": [{\"material\": \"metal\", \"unitsPerM3\": 1, \"healthPerM3\": 1, \"class\": \"jam\"}]}", "jam"),
            ("{\"format\": \"call-of-slop-extrusion/1\", \"extra\": 1}", "extra"),
        ];
        foreach (var (json, why) in bad)
        {
            var t = ExtrusionTable.Parse(json);
            want(t.Problem.Contains(why) && t.Rows.Length > 0, $"{json} wasn't refused for \"{why}\" (\"{t.Problem}\")");
        }
        var extruder = ConstructParts.Need("Extruder");
        var frame = ConstructParts.Need("Die Frame");
        want(extruder.IsExtruder && extruder.IsBin && extruder.PhysicsOnly && extruder.MaxInputs == 2 && extruder.HasOutput && extruder.PowerDraw > 0 && extruder.DataIn is [ConstructGatherer.RunPort, ConstructExtruder.CutName], "the Extruder part");
        var former = ConstructParts.Need("Wheel Former");
        want(former.IsExtruder && former.IsWheelFormer && former.MaxInputs == 3 && former.DataIn is [ConstructGatherer.RunPort, ConstructExtruder.CutName, ConstructExtruder.ModeName], "the Wheel Former part");
        want(ConstructParts.Find("Cut-off Saw") == null && ConstructParts.Find("Large Cut-off Saw") == null, "the saws are still parts");
        want(frame.IsDieFrame && ConstructParts.Need("Large Die Frame").IsDieFrame, "the Die Frames");
        want(!ConstructParts.All.Any(d => d.IsExtrusion), "a piece is listed among the part files");
        var file = File.ReadAllText(Path.Combine(ConstructParts.Folder, "Extrusion", "Extruder.json")).Replace("\r\n", "\n");
        ConstructParts.Parse(file, "good.json");
        (string From, string To, string Want)[] parts =
        [
            ("\"speed\": 0.2", "\"speed\": 90", "speed"),
            ("\"kind\": \"die\"", "\"kind\": \"struct\"", "die"),
            ("\"maxSection\": [\n        6,\n        6\n      ]", "\"maxSection\": [0.2, 0.2]", "maxSection"),
            ("\"extrude\": {", "\"extrude\": { \"haste\": 2, ", "haste"),
            ("\"material\": \"any\"", "\"material\": \"metal\"", "any"),
        ];
        foreach (var (from, to, why) in parts)
            try
            {
                ConstructParts.Parse(file.Replace(from, to), "bad.json");
                want(false, $"an Extruder with {to} wasn't refused");
            }
            catch (ConstructPartException ex)
            {
                want(ex.Message.Contains(why), $"an Extruder with {to}: \"{ex.Message}\", want \"{why}\"");
            }
        // Sections.
        want(Extrusion.Refusal(0) != null, "an empty section is allowed");
        want(Extrusion.Refusal(1ul | 1ul << 63) != null, "a section in two pieces is allowed");
        var check = 0ul;
        for (var y = 0; y < 8; y++)
            for (var x = 0; x < 8; x++)
                if ((x + y) % 2 == 0 || y % 2 == 0)
                    check |= 1ul << (y * 8 + x);
        want(Extrusion.Refusal(check) != null, "a section of more than 16 boxes is allowed");
        foreach (var p in Extrusion.Presets)
            want(Extrusion.Refusal(p.Mask) == null, $"the preset {p.Id}: {Extrusion.Refusal(p.Mask)}");
        want(Extrusion.Def(new ExtrusionKey(0, 25, 25, 100, 1, false)) == null && Extrusion.Def(new ExtrusionKey(ulong.MaxValue, 25, 25, 19205, 1, false)) == null
            && Extrusion.Def(new ExtrusionKey(ulong.MaxValue, 25, 25, 103, 1, false)) == null && Extrusion.Def(new ExtrusionKey(ulong.MaxValue, 3300, 25, 100, 1, false)) == null, "a bad key gave a piece");
        // Names: a piece names itself, whatever its section.
        foreach (var d in (ConstructPartDef[])[ExPiece(4, 25, 25, 400), ExPiece(5, 50, 100, 150, "planks"), ExPiece(0, 25, 25, 100, "metal", true), Extrusion.Def(new ExtrusionKey(0xFFFF, 40, 40, 200, names.IdOf("wood"), false))!,
            Extrusion.Def(new ExtrusionKey(0, 50, 50, 200, names.IdOf("metal"), false, ExShape.Round))!, Extrusion.Def(new ExtrusionKey(0, 50, 50, 200, names.IdOf("metal"), false, ExShape.Pipe, 3))!, Extrusion.Def(new ExtrusionKey(0, 30, 100, 0, names.IdOf("rubber"), false, ExShape.Wheel))!])
            want(ConstructParts.Find(d.Name) == d && d.Name.All(ch => ch is >= ' ' and <= '~'), $"the name \"{d.Name}\" doesn't find its piece");
        want(ConstructParts.Find("Extrusion jam 25x25x100 metal") == null && ConstructParts.Find("Extrusion i-beam 25x25x100 jam") == null, "a bad name found a piece");
    }

    /// <summary>Every preset through every way a part's shape is read: rays, the player's box, the ghost's overlap, the outline, mass, health and sockets.</summary>
    private static void ExtrusionShapes(Action<bool, string> want, Vector3 spot)
    {
        var names = RecyclingTable.Current;
        var metal = names.IdOf("metal");
        var row = ExtrusionTable.Current.Find(metal)!;
        for (var k = 0; k < Extrusion.Presets.Length; k++)
        {
            var w = new ConstructWorld { CreativeRecycling = false };
            var d = ExPiece(k, 100, 80, 300);
            var name = Extrusion.Presets[k].Id;
            var at = spot + new Vector3(0, 5, 0);
            if (ExBody(w, d, at, Quaternion.Identity, true) is not { } c)
            {
                want(false, $"{name}: no body");
                continue;
            }
            var mask = Extrusion.Presets[k].Mask;
            var volume = Extrusion.Fill(mask) * 1f * 0.8f * 3f;
            want(MathF.Abs(d.Mass - volume * row.UnitsPerM3 * names.WeightOf(metal)) < 0.5f, $"{name}: mass {d.Mass}, want by volume {volume * row.UnitsPerM3 * names.WeightOf(metal)}");
            want(MathF.Abs(c.Mass - d.Mass) < 0.5f, $"{name}: the body weighs {c.Mass}, the piece {d.Mass}");
            want(MathF.Abs(d.Health - volume * row.HealthPerM3) < 1f && d.Armour is > 0 and <= 68, $"{name}: health {d.Health}, armour {d.Armour}");
            want(d.Sockets.Length is >= 6 and <= 40 && d.PhysicsOnly && d.SolidBoxes <= Extrusion.MaxRects && d.Offered == false, $"{name}: {d.Sockets.Length} sockets, {d.SolidBoxes} boxes");
            want(ZFight.Report(d.Name, ZFight.Boxes(d)).Count() == 0, $"{name} has coplanar faces: {string.Join("; ", ZFight.Report(d.Name, ZFight.Boxes(d)))}");
            // A ray along the piece through each cell's middle: it hits exactly the cells that are there, at the piece's end.
            for (var y = 0; y < 8; y++)
                for (var x = 0; x < 8; x++)
                {
                    var local = new Vector3(-0.5f + (x + 0.5f) / 8f, -0.4f + (y + 0.5f) * 0.1f, -1f);
                    var hit = w.Raycast(at + local, Vector3.UnitZ, 6, out var h);
                    var there = (mask >> (y * 8 + x) & 1) != 0;
                    want(hit == there && (!there || MathF.Abs(h.Distance - 1f) < 0.02f), $"{name}: the ray through cell {x},{y} ({(there ? "there" : "empty")}) {(hit ? $"hit at {h.Distance}" : "missed")}");
                    // A small part inside the cell: the ghost's overlap sees the same shape.
                    var inside = new Vector3(local.X, local.Y, 1.5f);
                    var hits = false;
                    for (var b = 0; b < c.SolidCount(0) && !hits; b++)
                    {
                        c.SolidBox(0, b, out var sc, out var sh);
                        hits = MathF.Abs(inside.X - sc.X) < sh.X && MathF.Abs(inside.Y - sc.Y) < sh.Y && MathF.Abs(inside.Z - sc.Z) < sh.Z;
                    }
                    want(hits == there, $"{name}: the solid boxes at cell {x},{y}: {hits}");
                }
            c.ShapeBounds(0, out var mn, out var mx);
            want(Vector3.Distance(mn, d.BoundsMin) < 0.001f && Vector3.Distance(mx, d.BoundsMax) < 0.001f && mx.Z - mn.Z > 2.99f, $"{name}: the outline {mn} to {mx}");
            // Every socket on the piece's surface: a part welded there touches the piece.
            for (var s = 0; s < d.Sockets.Length; s++)
            {
                var sock = d.Sockets[s];
                var probe = sock.At + ConstructParts.DirVector(sock.Dir) * -0.01f;
                var solid = false;
                for (var b = 0; b < d.SolidBoxes && !solid; b++)
                {
                    var box = d.Boxes[b];
                    var o = Vector3.Abs(probe - box.Center) - box.Half;
                    solid = o.X < 0.011f && o.Y < 0.011f && o.Z < 0.011f;
                }
                want(solid || s < 2, $"{name}: socket {sock.Name} at {sock.At} isn't on the piece");
            }
        }
        // Long pieces: the same budget, and the mass goes as the length.
        var long32 = ExPiece(4, 400, 400, 3200);
        want(long32.Sockets.Length <= 40 && long32.SolidBoxes <= 4, $"the longest piece: {long32.Sockets.Length} sockets, {long32.SolidBoxes} boxes");
    }

    /// <summary>
    /// "Extruder" (docs/sandbox/CONSTRUCTS.md "Extrusion"): the data and every preset's shape; then the Extruder Line blueprint on
    /// the yard, in survival, through the real step: power, material by volume, the Run wire, the die's growth, stalls with their
    /// reasons, pushing, the cap; the saw by signal and by length; the cut piece welded by socket, point and freeform weld, a panel
    /// on it; an I-beam against a post; a blueprint with pieces in it and back through the wire; split, destroy, salvage, paint; no
    /// allocation in a steady run.
    /// </summary>
    private static void CheckExtruder()
    {
        if (Maps.Find(ConstructYardMap) is not { } yard || yard.Sandbox == null)
        {
            Fail($"extruder: the {ConstructYardMap} map is missing");
            return;
        }
        var problems = new List<string>();
        void Want(bool ok, string what)
        {
            if (!ok)
                problems.Add(what);
        }
        ExtrusionData(Want);
        yard.RestoreProps();
        World.SetMap(yard);
        var q = World.Query;
        var w = new ConstructWorld { CreativeRecycling = false };
        void Run(float seconds)
        {
            for (var t = 0; t < seconds * World.TickRate; t++)
            {
                w.Step(q, World.TickDt);
                w.ExtrudeDirty.Clear();
            }
        }
        if (OpenSpot(w) is not { } spot)
        {
            Fail("extruder: no open ground on the yard");
            return;
        }
        ExtrusionShapes(Want, spot);
        var line = BuildBlueprint(w, "Extruder Line", spot.X, spot.Z);
        if (line == null)
        {
            Fail("extruder: no Extruder Line");
            return;
        }
        line.Owner = 255;
        var ei = PartNamed(line, "Extruder");
        var bin = PartNamed(line, "Materials Bin");
        var ti = PartNamed(line, "Timer");
        var st = w.StoreOf(line, ei)!;
        var def = line.DefOf(ei);
        var inv0 = new SandboxInventory();
        var metal = RecyclingTable.Current.IdOf("metal");
        var wood = RecyclingTable.Current.IdOf("wood");
        var rubber = RecyclingTable.Current.IdOf("rubber");
        void WantLook(string text)
        {
            var look = ConstructExtruder.Look(line, ei, st, false, inv0);
            Want(look.Contains(text) && look.All(ch => ch is >= ' ' and <= '~'), $"the look line \"{look}\" should say \"{text}\"");
        }
        int Len() => ConstructExtruder.LengthOf(line, ei);
        Want(line.HasExtruder && line.Anchored && line.RouteCount >= 2 && line.HasBattery, $"the line ({line.HasExtruder}, anchored {line.Anchored}, {line.RouteCount} links)");
        // The timer stays out of it until the cut's own tests.
        ConstructRoutes.RemoveAt(w, line, ti, RouteType.Data);
        line.Rebuild();
        var bare = line.Mass;
        Run(0.3f);
        Want(!st.Busy && (ExtrudeStall)st.Refused == ExtrudeStall.NoMaterial && line.PowerUse < def.PowerDraw, $"an empty Extruder ({st.Busy}, {(ExtrudeStall)st.Refused}, {line.PowerUse} W)");
        WantLook("needs material");
        Want(ExtrusionTable.Current.Takes().Contains("metal"), "what it takes");

        // ---- No power: nothing grows.
        line.Charge = 0;
        Run(0.2f);
        w.Deposit(line, bin, 400, metal);
        Run(4);
        Want(st.Amount > 0 && st.Material == metal && Len() == 0 && !st.Busy, $"a dead Extruder ({st.Amount} metal, {Len()} cm, {st.Busy})");
        WantLook("no power");

        // ---- Power: the rate, material by volume, the mass, the real piece.
        line.Charge = line.ChargeCapacity;
        Run(0.25f);
        Want(st.Busy && line.PowerUse >= def.PowerDraw, $"powered, it should run and draw ({st.Busy}, {line.PowerUse} W, {(ExtrudeStall)st.Refused})");
        WantLook("extruding");
        var fed0 = st.Amount + w.StoreOf(line, bin)!.Amount;
        var len0 = Len();
        Run(5);
        var grown = Len() - len0;
        var used = fed0 - st.Amount - w.StoreOf(line, bin)!.Amount;
        var row = ExtrusionTable.Current.Find(metal)!;
        var wantUse = 0.25f * 0.25f * grown / 100f * row.UnitsPerM3;
        Line($"extruder: 5 s: {grown} cm, {used} metal (by volume {wantUse:0.0}), {line.PowerUse:0} W");
        Want(grown is >= 90 and <= 110, $"the rate ({grown} cm in 5 s at {def.ExtSpeed} m/s)");
        Want(MathF.Abs(used - wantUse) <= 2, $"material by volume ({used} used for {wantUse:0.0})");
        var pi = ConstructExtruder.ProductOf(line, ei);
        Want(pi >= 0 && line.DefOf(pi) is { IsExtrusion: true, ExKey.Growing: true, Sockets.Length: 1 }, "the piece on the die");
        var piece = line.DefOf(pi);
        line.Rebuild();
        Want(line.Mass > bare + piece.Mass * 0.8f, $"the machine carries its piece ({line.Mass} kg, was {bare}, the piece {piece.Mass})");
        Want(line.SocketCount(pi) == 1 && piece.Sockets[0].Kind == SocketKind.Die, "a growing piece offers sockets");
        // Solid as it grows: a ray at its tip, and it stays welded to the die as the machine turns.
        line.WorldFrame(line.Parts[pi].Seg, out var fq, out var ft);
        var tipWorld = ft + Vector3.Transform(line.Parts[pi].Pos + Rot24.Apply(line.Parts[pi].Rot, new Vector3(0, 0, Len() / 100f)), fq);
        var axis = Vector3.Transform(Rot24.Apply(line.Parts[pi].Rot, Vector3.UnitZ), fq);
        var rayHit = w.Raycast(tipWorld + axis * 0.5f, -axis, 2f, out var tipHit);
        Want(rayHit && tipHit.Body == line && tipHit.Part == pi && MathF.Abs(tipHit.Distance - 0.5f) < 0.06f, $"a ray at the tip ({rayHit}, {tipHit.Distance})");

        // ---- Run: a wire holds it off, drawing nothing.
        var sw = ModFit(w, line, "Switch", "-y", "extruder");
        if (sw < 0)
            return;
        Wire(w, line, sw, ei);
        line.Parts[sw].State = 0;
        Run(0.4f);
        var held = Len();
        Run(1);
        Want(Len() == held && st.Held && !st.Busy, $"held off ({Len()} from {held}, held {st.Held}, busy {st.Busy})");
        WantLook(Program.HeldLook.Trim(' ', '-'));
        line.Parts[sw].State = 1;
        Run(1);
        Want(Len() > held && !st.Held, $"Run on again ({Len()} from {held})");

        // ---- Its output is the length so far.
        Want(MathF.Abs(line.Parts[ei].Out - Len() / 100f) < 0.21f, $"the length output ({line.Parts[ei].Out}, {Len()} cm)");

        // ---- A wrong material: refused with a reason; the piece stays.
        var keepAmount = st.Amount;
        var keepMat = st.Material;
        st.Amount = 50;
        st.Material = wood;
        Run(0.4f);
        Want((ExtrudeStall)st.Refused == ExtrudeStall.Mixed && !st.Busy, $"wood behind a metal piece ({(ExtrudeStall)st.Refused})");
        WantLook("cut it or take it off");
        st.Amount = keepAmount;
        st.Material = keepMat;

        // ---- The cut by signal: all of it comes off at the die, and the next piece starts from the settings.
        var bodies0 = w.Bodies.Count;
        var before = Len();
        line.Parts[ei].State |= ConstructExtruder.CutPending;
        Run(0.25f);
        var cut = w.Bodies.FirstOrDefault(b => b != line && b.PartCount == 1 && b.Parts[0].D.IsExtrusion);
        Want(cut != null && w.Bodies.Count == bodies0 + 1, $"a pulse should cut a piece off ({w.Bodies.Count} bodies, was {bodies0})");
        Want(cut?.DefOf(0).ExKey.L >= before && Len() < before, $"the whole piece comes off ({cut?.DefOf(0).ExKey.L} cm, {Len()} on the die, was {before})");
        if (cut != null)
        {
            var cd = cut.DefOf(0);
            Want(!cd.ExKey.Growing && cd.Sockets.Length >= 6 && cd.ExKey.L >= 5 && cd.ExKey.L % Extrusion.StepCm == 0, $"the cut piece ({cd.Name}, {cd.Sockets.Length} sockets)");
            Want(cut.Owner == line.Owner && !cut.Anchored, "the cut piece is free");
            var y0 = cut.WorldCom.Y;
            Run(2.5f);
            Want(cut.WorldCom.Y < y0 - 0.05f || cut.Asleep, $"the cut piece falls ({cut.WorldCom.Y} from {y0})");
        }
        // The next one grows from the die.
        var after = Len();
        Run(1);
        Want(Len() > after, "a new piece grows after a cut");

        // ---- The cut by a real signal: the timer on the Cut input; a rising edge once a period. Run stays free of it.
        SpWire(w, line, ti, ei, ConstructExtruder.CutPort);
        line.Parts[ti].Cfg[line.DefOf(ti).CfgSlot[(int)CfgKey.Period]] = 4;
        Run(0.2f);
        var made = 0;
        for (var k = 0; k < 13 && line.Charge > 0; k++)
        {
            var was = Len();
            for (var t = 0; t < 4; t++)
            {
                Run(0.25f);
                made += Len() < was ? 1 : 0;
                was = Len();
            }
            w.Deposit(line, bin, 50, metal);
            foreach (var b in w.Bodies.Where(b => b != line && b.PartCount == 1 && b.Parts[0].D.IsExtrusion).ToArray())
                w.Remove(b); // (carried off: a heap in front of the die isn't this check)
        }
        Want(!st.Held, "a wire into Cut holds the machine off");
        Want(made is >= 2 and <= 4, $"a 4 s timer over 13 s cut {made} pieces");
        ConstructRoutes.RemoveAt(w, line, ti, RouteType.Data);
        line.Rebuild();

        // ---- The cut by length: "cut every 1 m" with no logic at all.
        foreach (var b in w.Bodies.Where(b => b != line).ToArray())
            w.Remove(b);
        line.Parts[ei].Cfg[def.CfgSlot[(int)CfgKey.CutLength]] = 1;
        var lengths = new List<int>();
        for (var k = 0; k < 16; k++)
        {
            Run(1);
            w.Deposit(line, bin, 50, metal);
            foreach (var b in w.Bodies.Where(b => b != line).ToArray())
            {
                if (b.Pos.Y < spot.Y - 0.5f)
                    Want(false, "a cut piece fell through the ground");
                if (b.PartCount == 1 && b.Parts[0].D.IsExtrusion)
                    lengths.Add(b.DefOf(0).ExKey.L);
                w.Remove(b);
            }
        }
        Want(lengths.Count is >= 2 and <= 4 && lengths.All(l => l == 100), $"cut every 1 m ({lengths.Count} pieces: {string.Join(", ", lengths)})");
        line.Parts[ei].Cfg[def.CfgSlot[(int)CfgKey.CutLength]] = 0;

        // ---- Blocked: something fixed in the path stops it with a reason; nothing grows into it.
        ExtruderPath(Want, spot);

        // ---- The die grows with the frames, and the machine with it.
        ExtruderDie(Want);

        // ---- A custom section through the configure tool's rules; a bad one stalls with a reason.
        ExtruderCustom(Want, spot);

        // ---- The cap: it stops at the cap.
        ExtruderCap(Want, spot);

        // ---- Welds, the I-beam, blueprints, split, destroy, salvage.
        ExtrusionWelds(Want, spot);
        ExtrusionPost(Want, spot);
        ExtrusionBlueprint(Want, spot);
        ExtrusionLife(Want, spot);

        // ---- Allocation: a steady run allocates nothing (its shapes made once).
        ExtruderAlloc(Want, spot);
        if (problems.Count > 0)
            Fail($"extruder: {problems.Count} problem(s): " + string.Join("; ", problems.Take(14)));
    }

    /// <summary>A bare Extruder with a battery on a pinned foundation, high in the air so a long piece meets nothing; full of metal.</summary>
    private static (Construct C, int E, CStore St)? ExBench(ConstructWorld w, Vector3 at, string? bp = null)
    {
        var base0 = ConstructParts.Need("Foundation");
        var c = ConstructBuild.PlaceNew(w, base0, new FreePlacement { Pos = at, Rot = Quaternion.Identity, Anchor = true });
        if (c == null)
            return null;
        c.Rebuild();
        int Fit2(int on, string socket, string part, string by)
        {
            var td = c.DefOf(on);
            var d = ConstructParts.Need(part);
            var ts = Array.FindIndex(td.Sockets, s => s.Name == socket);
            var ds = Array.FindIndex(d.Sockets, s => s.Name == by);
            if (ConstructBuild.Place(c, on, ts, d, ds, 0, out var g) is { } why)
            {
                Fail($"extruder: {part} on {td.Name} {socket}: {why}");
                return -1;
            }
            var i = ConstructBuild.Attach(w, c, d, g);
            c.Rebuild();
            return i;
        }
        var e = Fit2(0, "top21", "Extruder", "bottom");
        Fit2(0, "top00", "Battery", "-y");
        if (e < 0)
            return null;
        var st = w.StoreOf(c, e)!;
        w.Deposit(c, e, def400(c, e), RecyclingTable.Current.IdOf("metal"));
        c.Rebuild();
        return (c, e, st);

        static int def400(Construct c, int e) => c.DefOf(e).BulkCapacity;
    }

    private static void ExRun(ConstructWorld w, float seconds, Action? each = null)
    {
        var q = World.Query;
        for (var t = 0; t < seconds * World.TickRate; t++)
        {
            each?.Invoke();
            w.Step(q, World.TickDt);
            w.ExtrudeDirty.Clear();
        }
    }

    private static void ExtruderPath(Action<bool, string> want, Vector3 spot)
    {
        var w = new ConstructWorld { CreativeRecycling = false };
        var at = spot + new Vector3(0, 12, 0);
        if (ExBench(w, at) is not { } b)
        {
            want(false, "no bench");
            return;
        }
        var (c, e, st) = b;
        ExRun(w, 1.1f);
        var len = ConstructExtruder.LengthOf(c, e);
        want(len > 0, $"the bench grows ({(ExtrudeStall)st.Refused}, {c.PowerUse} W)");
        var pi = ConstructExtruder.ProductOf(c, e);
        c.WorldFrame(0, out var q, out var t);
        var tip = t + Vector3.Transform(c.Parts[pi].Pos + new Vector3(0, 0, len / 100f), q);
        // A fixed block a metre on.
        var block = ConstructParts.Need("Frame Block");
        var mid = (block.BoundsMin + block.BoundsMax) * 0.5f;
        var wall = ExBody(w, block, tip + new Vector3(0, 0, 0.5f + block.BoundsMax.Z - mid.Z) - mid, Quaternion.Identity, true);
        want(wall != null, "no wall");
        ExRun(w, 5);
        var reached = ConstructExtruder.LengthOf(c, e);
        want((ExtrudeStall)st.Refused == ExtrudeStall.Blocked && !st.Busy, $"a fixed block stops it with a reason ({(ExtrudeStall)st.Refused}, busy {st.Busy}, {reached} cm)");
        want(ConstructExtruder.Status(c, e, st).Contains("blocked"), "the look line says it is blocked");
        c.WorldFrame(0, out q, out t);
        var tipNow = t + Vector3.Transform(c.Parts[pi].Pos + new Vector3(0, 0, reached / 100f), q);
        wall!.PartBounds(0, out var wmn, out _);
        want(tipNow.Z <= wall.ToWorld(wmn).Z + 0.02f && reached > len, $"it stops at the block (tip {tipNow.Z}, block at {wall.ToWorld(wmn).Z}, from {len} cm)");
        w.Remove(wall);
        ExRun(w, 1);
        want(ConstructExtruder.LengthOf(c, e) > reached && (ExtrudeStall)st.Refused == ExtrudeStall.None, "with the block gone it goes on");

        // A loose block: pushed along, and the piece grows after it; nothing overlaps.
        reached = ConstructExtruder.LengthOf(c, e);
        c.WorldFrame(0, out q, out t);
        tip = t + Vector3.Transform(c.Parts[pi].Pos + new Vector3(0, 0, reached / 100f), q);
        var small = ExBody(w, ConstructParts.Need("Steel Tube 0.5 m"), tip + new Vector3(0, -0.01f, 0.2f), Quaternion.Identity, false);
        want(small != null, "no loose piece");
        if (small != null)
        {
            small.Frozen = false;
            ExRun(w, 0.2f); // (it hangs in the air: a push in the air is a push)
            var z0 = small.WorldCom.Z;
            ExRun(w, 1.4f);
            want(small.WorldCom.Z > z0 || small.WorldCom.Y < tip.Y - 0.3f, $"a loose piece is moved on or falls away ({small.WorldCom} from {z0})");
            w.Remove(small);
        }

        // Something free and far too heavy, kept on the path: it says so, and grows into nothing.
        ExRun(w, 1);
        reached = ConstructExtruder.LengthOf(c, e);
        c.WorldFrame(0, out q, out t);
        tip = t + Vector3.Transform(c.Parts[pi].Pos + new Vector3(0, 0, reached / 100f), q);
        var heavy = ExBody(w, ExPiece(0, 200, 200, 300), tip + new Vector3(0, -1f, 0.02f), Quaternion.Identity, false);
        if (heavy != null)
        {
            var hold = heavy.Pos;
            ExRun(w, 4, () =>
            {
                heavy.Pos = heavy.PrevPos = hold; // (wedged: whatever pushes it, it stays)
                heavy.Vel = Vector3.Zero;
            });
            want((ExtrudeStall)st.Refused == ExtrudeStall.TooHeavy && ConstructExtruder.LengthOf(c, e) - reached <= Extrusion.StepCm, $"a body that doesn't move ({(ExtrudeStall)st.Refused}, {ConstructExtruder.LengthOf(c, e) - reached} cm on)");
            want(ConstructExtruder.Status(c, e, st).Contains("too heavy"), "the look line says it is too heavy");
        }
    }

    private static void ExtruderDie(Action<bool, string> want)
    {
        var w = new ConstructWorld { CreativeRecycling = false };
        if (OpenSpot(w) is not { } spot || ExBench(w, spot + new Vector3(0, 12, 0)) is not { } b)
        {
            want(false, "no bench for the die");
            return;
        }
        var (c, e, st) = b;
        var d = c.DefOf(e);
        ConstructExtruder.Die(c, e, out var mid0, out var size0);
        want(Vector2.Distance(size0, new Vector2(0.5f, 0.5f)) < 0.01f, $"the base die is {size0}");
        var bounds0 = c.LocalMax - c.LocalMin;
        // The section is held to the die, and the look line says so.
        ref var p = ref c.Parts[e];
        p.Cfg[d.CfgSlot[(int)CfgKey.Width]] = 2;
        p.Cfg[d.CfgSlot[(int)CfgKey.Height]] = 0.25f;
        ConstructExtruder.Section(c, e, out _, out var w0, out var h0, out _);
        want(w0 == 50 && h0 == 25, $"a section held to the die: {w0} by {h0}");
        want(ConstructExtruder.Look(c, e, st, false, new SandboxInventory()).Contains("Die Frames"), "the look line says what to add");
        // A Die Frame on each side, through the real rules: wider; the machine is wider too.
        var frame = ConstructParts.Need("Die Frame");
        var fits = new List<int>();
        foreach (var sock in (string[])["+x", "-x"])
        {
            var ts = Array.FindIndex(d.Sockets, s => s.Name == sock);
            if (ConstructBuild.Place(c, e, ts, frame, Array.FindIndex(frame.Sockets, s => s.Name == "-x"), 0, out var g) is { } why)
            {
                want(false, $"a Die Frame on {sock}: {why}");
                continue;
            }
            fits.Add(ConstructBuild.Attach(w, c, frame, g));
            c.Rebuild();
        }
        ConstructExtruder.Die(c, e, out _, out var size1);
        want(fits.Count == 2 && MathF.Abs(size1.X - 2f) < 0.02f, $"two frames make a {size1.X} m die");
        want(c.LocalMax.X - c.LocalMin.X >= bounds0.X, "the machine grew");
        ConstructExtruder.Section(c, e, out _, out var w1, out _, out var centre1);
        want(w1 == 200 && MathF.Abs(centre1.X) < 0.01f, $"the section takes the die ({w1}, centre {centre1})");
        if (fits.Count < 2)
            return;
        // A frame on one side only moves the die's centre; a broken frame gives its width back.
        w.DamagePart(c, fits[1], 1e6f, out _);
        c.Rebuild();
        ConstructExtruder.Die(c, e, out var mid2, out var size2);
        want(size2.X < size1.X && MathF.Abs(mid2.X) > 0.1f, $"a frame gone: {size2.X} m about {mid2.X}");
        _ = mid0;
    }

    private static void ExtruderCustom(Action<bool, string> want, Vector3 spot)
    {
        var w = new ConstructWorld { CreativeRecycling = false };
        if (ExBench(w, spot + new Vector3(0, 12, 0)) is not { } b)
        {
            want(false, "no bench for a custom section");
            return;
        }
        var (c, e, st) = b;
        var d = c.DefOf(e);
        ref var p = ref c.Parts[e];
        // Through the server's own check: clamped, one kind, the tool out.
        var hands = FwHands();
        hands.Hand(0).Tool = ConstructTool.Configure;
        var eye = ConstructRender.PartCentre(c, e) + new Vector3(2, 1, 0);
        Span<ushort> one = [(ushort)e];
        var plus = 0x0018_1818_FFFF_1818ul; // (a thick cross, not the preset's)
        var profile = d.CfgSlot[(int)CfgKey.Profile];
        byte[] slots = [(byte)profile, (byte)d.CfgSlot[(int)CfgKey.MaskA], (byte)d.CfgSlot[(int)CfgKey.MaskB], (byte)d.CfgSlot[(int)CfgKey.MaskC], (byte)d.CfgSlot[(int)CfgKey.MaskD]];
        float[] vals = [Extrusion.Presets.Length, (ushort)plus, (ushort)(plus >> 16), (ushort)(plus >> 32), (ushort)(plus >> 48)];
        var why = hands.Configure(w, 0, eye, c.Id, one, slots, vals, null);
        want(why == null, $"a custom section refused: {why}");
        want(ConstructExtruder.MaskOf(p, d) == plus, $"the custom cells ({ConstructExtruder.MaskOf(p, d):x16})");
        ExRun(w, 1.1f);
        var pi = ConstructExtruder.ProductOf(c, e);
        want(pi >= 0 && c.DefOf(pi).ExKey.Mask == plus, "the piece has the custom section");
        // A setting change while a piece is on the die is for the next piece.
        want(pi >= 0 && c.DefOf(pi).ExKey.W == 25, "the piece's width");
        // A bad section: refused with a reason, whichever way it was set.
        w.RemovePart(c, pi);
        c.Rebuild();
        ulong split = 1ul | 1ul << 63;
        p = ref c.Parts[e];
        p.Cfg[d.CfgSlot[(int)CfgKey.MaskA]] = (ushort)split;
        p.Cfg[d.CfgSlot[(int)CfgKey.MaskB]] = 0;
        p.Cfg[d.CfgSlot[(int)CfgKey.MaskC]] = 0;
        p.Cfg[d.CfgSlot[(int)CfgKey.MaskD]] = (ushort)(split >> 48);
        ExRun(w, 0.5f);
        want((ExtrudeStall)st.Refused == ExtrudeStall.BadShape && ConstructExtruder.Status(c, e, st).Contains("separate"), $"a section in two pieces ({(ExtrudeStall)st.Refused}: {ConstructExtruder.Status(c, e, st)})");
    }

    private static void ExtruderCap(Action<bool, string> want, Vector3 spot)
    {
        var w = new ConstructWorld { CreativeRecycling = false };
        if (ExBench(w, spot + new Vector3(0, 30, 0)) is not { } b)
        {
            want(false, "no bench for the cap");
            return;
        }
        var (c, e, st) = b;
        ExRun(w, 0.5f);
        var pi = ConstructExtruder.ProductOf(c, e);
        if (pi < 0)
        {
            want(false, "no piece for the cap");
            return;
        }
        ref var pp = ref c.Parts[pi];
        var cap = (int)(c.DefOf(e).ExtMaxLength * 100);
        pp.Def = Extrusion.Def(pp.D.ExKey with { L = (ushort)(cap - 10) })!.Id;
        c.Rebuild();
        ExRun(w, 2);
        want(ConstructExtruder.LengthOf(c, e) == cap && (ExtrudeStall)st.Refused == ExtrudeStall.AtCap && !st.Busy, $"at the cap ({ConstructExtruder.LengthOf(c, e)} cm, {(ExtrudeStall)st.Refused})");
        want(ConstructExtruder.Status(c, e, st).Contains("as long as it can be"), "the look line says it is at the cap");
    }

    private static void ExtrusionWelds(Action<bool, string> want, Vector3 spot)
    {
        var w = new ConstructWorld { CreativeRecycling = false };
        var at = spot + new Vector3(0, 8, 0);
        var hands = FwHands();
        // ---- A piece as it came off the die: an I-beam, by its end socket onto a block's side.
        var beam = ExPiece(4, 25, 25, 200);
        var block = ConstructParts.Need("Frame Block");
        var host = ExBody(w, block, at, Quaternion.Identity, true);
        var loose = ExLoose(w, beam, at + new Vector3(3, 0.2f, 0), Quaternion.Identity);
        if (host == null || loose == null)
        {
            want(false, "no bodies for the welds");
            return;
        }
        var ts = Array.FindIndex(block.Sockets, s => s.Dir == 0);
        var target = new WeldTarget { Body = host.Id, Part = 0, Socket = (byte)ts };
        var eye = at + new Vector3(1.4f, 0.6f, 0.8f);
        SocketWeld.SourceFrame(loose, 0, 0, out _, out var sd);
        SocketWeld.TargetFrame(host, target, out _, out var td);
        var degrees = SocketWeld.FromWire(SocketWeld.WireAngle(SocketWeld.AngleOf(sd, td, Quaternion.Identity)));
        var why = hands.BeginSocketWeld(w, 0, eye, loose.Id, 0, 0, target, degrees, 0);
        want(why == null && host.PartCount == 2 && w.Get(loose.Id) == null, $"the socket weld of a cut piece onto a block: {why} ({host.PartCount} parts)");
        host.Rebuild();
        want(host.PartCount == 2 && host.DefOf(1).IsExtrusion, "the welded piece");
        // A panel welded to the piece's face, through the same path.
        if (host.PartCount == 2)
        {
            var panel = ConstructParts.Need("Sheet Steel 0.5 m");
            var ps = Array.FindIndex(host.DefOf(1).Sockets, s => s.Name == "+y0");
            var loosePanel = ExLoose(w, panel, at + new Vector3(1.5f, 1.2f, 0), Quaternion.Identity);
            if (loosePanel != null && ps >= 0)
            {
                var wt = new WeldTarget { Body = host.Id, Part = 1, Socket = (byte)ps };
                var src = Array.FindIndex(panel.Sockets, s => s.Dir == 5);
                SocketWeld.SourceFrame(loosePanel, 0, src, out _, out var pdir);
                SocketWeld.TargetFrame(host, wt, out var pat, out var tdir);
                var deg = SocketWeld.FromWire(SocketWeld.WireAngle(SocketWeld.AngleOf(pdir, tdir, Quaternion.Identity)));
                var worldAt = host.ToWorld(pat);
                var eye2 = worldAt + new Vector3(0.5f, 1, 1);
                var pwhy = hands.BeginSocketWeld(w, 0, eye2, loosePanel.Id, 0, src, wt, deg, 0);
                want(pwhy == null && host.PartCount == 3, $"a panel welded to a piece's face: {pwhy} ({host.PartCount} parts)");
            }
            else
                want(false, $"no panel or face socket ({ps})");
        }
        // ---- Freeform: another piece held near the first one's middle, off the grid.
        host.Rebuild();
        var second = ExPiece(0, 25, 25, 100);
        var other = ExLoose(w, second, Vector3.Zero, Quaternion.Identity);
        if (other != null)
        {
            host.PartBounds(1, out var mn, out var mx);
            var tilt = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.2f);
            other.Pos = other.PrevPos = host.ToWorld((mn + mx) * 0.5f) + new Vector3(0, 0.125f + 0.13f, -0.5f);
            other.Rot = other.PrevRot = tilt;
            other.Rebuild();
            var fwhy = hands.BeginFreeform(w, 0, other.Pos + new Vector3(1, 1, 1), other.Id, 0, host.Id, 1, 0);
            want(fwhy == null, $"a freeform weld onto a piece: {fwhy}");
        }
        // ---- Point weld: a click on a third piece's face onto a click on the first one's flange.
        var third = ExLoose(w, ExPiece(1, 50, 50, 100), at + new Vector3(0.5f, 3, 0), Quaternion.Identity);
        if (third != null && host.DefOf(1).IsExtrusion)
        {
            var bottom = third.ToWorld(new Vector3(0, third.DefOf(0).BoundsMin.Y, 0.3f));
            host.PartBounds(1, out var bmn, out var bmx);
            var above = host.ToWorld(new Vector3(bmx.X - 0.2f, bmx.Y + 1, (bmn.Z + bmx.Z) * 0.5f));
            var ray = w.Raycast(above, -Vector3.UnitY, 2, out var flange, third);
            var onBeam = flange.Point;
            var got = PointWeld.OriginAt(third, 0, bottom, -Vector3.UnitY, out var src);
            var aim = ray && flange.Body == host && SocketWeld.FaceAt(host, flange.Part, onBeam, flange.Normal, out _) && flange.Part == 1;
            SocketWeld.FaceAt(host, 1, onBeam, flange.Normal, out var face);
            face.Body = host.Id;
            face.Part = 1;
            var parts0 = host.PartCount;
            var pw = got && aim ? hands.BeginPointWeld(w, 0, onBeam + new Vector3(0.3f, 1, 1), third.Id, src, face, 0, 0) : $"no face (piece {got}, target {aim})";
            want(pw == null && host.PartCount > parts0, $"a point weld onto a piece: {pw} ({host.PartCount} parts)");
        }
        // ---- A piece still on its die refuses all of them, and says why.
        var w2 = new ConstructWorld { CreativeRecycling = false };
        if (ExBench(w2, spot + new Vector3(0, 12, 0)) is not { } b)
        {
            want(false, "no bench for the refusals");
            return;
        }
        var (c, e, _) = b;
        ExRun(w2, 1.1f);
        var pi = ConstructExtruder.ProductOf(c, e);
        var free = ExLoose(w2, second, c.ToWorld(c.Parts[pi].Pos) + new Vector3(0.5f, 0, 0.2f), Quaternion.Identity);
        if (free != null && pi >= 0)
        {
            var refuse = Freeform.Check(w2, free, 0, c, pi);
            want(refuse != null && refuse.Contains("die"), $"a freeform weld onto a growing piece: {refuse}");
            var face = new WeldTarget { Body = c.Id, Part = (byte)pi, Socket = 0 };
            var sw = SocketWeld.Check(w2, null, free, 0, 0, c, face, 0, default);
            want(sw != null && sw.Contains("die"), $"a socket weld onto a growing piece: {sw}");
            want(ConstructBuild.Ghosts(c, ConstructParts.Need("Lamp"), new ConstructGhost[8], c.ToWorld(c.Parts[pi].Pos), 0.01f) == 0, "a ghost on the die");
        }
    }

    private static void ExtrusionPost(Action<bool, string> want, Vector3 spot)
    {
        // An I-beam 1 m high and wide, 3 m long, fixed; a post a little shorter runs at it sideways, along its notch: the beam is an I to it, so the post goes in past
        // the bounding box's edge and stops at the web. As a solid bar it would stop 37 cm sooner. (A moving beam's own shape against a fixed post is the compound collider's, feat/collision.)
        var w = new ConstructWorld { CreativeRecycling = false };
        var at = spot + new Vector3(0, 10, 0);
        var beam = ExBody(w, ExPiece(4, 100, 100, 300), at, Quaternion.Identity, true);
        var post = ExBody(w, ExPiece(0, 15, 15, 100), at + new Vector3(0.9f, 0.1f, 1), Quaternion.Identity, false);
        if (post == null || beam == null)
        {
            want(false, "no post or beam");
            return;
        }
        post.Vel = new Vector3(-3, 0, 0);
        post.Wake();
        var most = post.Pos.X;
        ExRun(w, 0.35f, () => most = MathF.Min(most, post.Pos.X));
        var reach = most - at.X;
        Line($"extruder: a post against an I-beam stops {reach:0.00} m from its middle (web 0.125 + post 0.075: 0.20; bounds: 0.575)");
        want(reach is > 0.12f and < 0.32f, $"the post against the I-beam stopped at {reach} (the web is at 0.2, its bounds at 0.575)");
        // From above, the flange meets it first.
        var w2 = new ConstructWorld { CreativeRecycling = false };
        var beam2 = ExBody(w2, ExPiece(4, 100, 100, 300), at, Quaternion.Identity, true);
        var post2 = ExBody(w2, ExPiece(0, 15, 15, 100), at + new Vector3(0.3f, 0.9f, 1), Quaternion.Identity, false);
        if (post2 != null && beam2 != null)
        {
            post2.Vel = new Vector3(0, -3, 0);
            post2.Wake();
            var low = post2.Pos.Y;
            ExRun(w2, 0.4f, () => low = MathF.Min(low, post2.Pos.Y));
            want(low - at.Y > 0.5f, $"a post lands on the I-beam's flange ({low - at.Y})");
        }
    }

    private static void ExtrusionBlueprint(Action<bool, string> want, Vector3 spot)
    {
        // ---- A blueprint with pieces: by name, a preset and a custom section, a weld by their sockets, a colour.
        var custom = Extrusion.Def(new ExtrusionKey(0x0000_FFFF_FFFF_FFFFul, 50, 50, 100, RecyclingTable.Current.IdOf("metal"), false))!;
        var beam = ExPiece(4, 25, 25, 200);
        var json = $$"""
            { "format": "call-of-slop-construct-blueprint/1", "name": "Pieces", "steps": [
              { "part": "Frame Block" },
              { "part": "{{beam.Name}}", "on": 0, "socket": "+x", "by": "base", "paint": "#c04020" },
              { "part": "{{custom.Name}}", "on": 1, "socket": "tip", "by": "base" }
            ] }
            """;
        var bp = ConstructBuild.ParseBlueprint(json, "pieces.json");
        var w = new ConstructWorld { CreativeRecycling = false };
        var c = ConstructBuild.Build(w, bp, spot + new Vector3(0, 8, 0), Quaternion.Identity, out var why);
        want(c != null && c.PartCount == 3 && c.DefOf(1) == beam && c.DefOf(2) == custom && c.LinkCount == 2, $"a blueprint with pieces: {why}");
        if (c == null)
            return;
        want(c.Parts[1].Paint != 0, "a piece keeps its paint");
        // ---- A body on the wire and back: its key, not an id; the same hash.
        var pw = new PacketWriter(4096);
        ConstructNet.WriteFull(pw, c, 1);
        var w2 = new ConstructWorld { CreativeRecycling = false };
        var r = new PacketReader(pw.Span);
        r.U8();
        ConstructNet.Read(ref r, w2, 0);
        var got = w2.Get(c.Id);
        want(got != null && got.PartCount == 3 && got.DefOf(1) == beam && got.DefOf(2) == custom && ConstructNet.StructureHash(got) == ConstructNet.StructureHash(c), "a body with pieces through the wire");
        var sized = new PacketWriter(256);
        Extrusion.Write(sized, beam);
        want(sized.Length is > 0 and <= 12, $"a preset piece on the wire is {sized.Length} bytes");
        var sizedC = new PacketWriter(256);
        Extrusion.Write(sizedC, custom);
        want(sizedC.Length <= 20, $"a custom piece on the wire is {sizedC.Length} bytes");
        // ---- Bad keys are malformed, not a crash.
        var bad = new PacketWriter(64);
        bad.U8(200).U16(25).U16(25).U16(100).U8(1);
        var br = new PacketReader(bad.Span);
        try
        {
            Extrusion.Read(ref br);
            want(false, "a bad preset was read");
        }
        catch (MalformedPacketException)
        {
        }
    }

    private static void ExtrusionLife(Action<bool, string> want, Vector3 spot)
    {
        // ---- Split: a piece in the middle of a construct: cut one end's block off the rest, its piece goes with what it is joined to.
        var w = new ConstructWorld { CreativeRecycling = false };
        var beam = ExPiece(4, 25, 25, 200);
        var json = $$"""
            { "format": "call-of-slop-construct-blueprint/1", "name": "Pieces", "steps": [
              { "part": "Frame Block" },
              { "part": "{{beam.Name}}", "on": 0, "socket": "+x", "by": "base" },
              { "part": "Frame Block", "on": 1, "socket": "tip", "by": "-x" }
            ] }
            """;
        var c = ConstructBuild.Build(w, ConstructBuild.ParseBlueprint(json, "p.json"), spot + new Vector3(0, 8, 0), Quaternion.Identity, out var why);
        if (c == null)
        {
            want(false, $"the split blueprint: {why}");
            return;
        }
        w.RemovePart(c, 0);
        c.Rebuild();
        want(c.PartCount == 2 && c.DefOf(0) == beam || c.DefOf(1) == beam, "the piece stayed with its block");
        w.RemovePart(c, c.DefOf(0) == beam ? (c.DefOf(1).IsExtrusion ? 1 : 1) : 0);
        var alone = w.Bodies.FirstOrDefault(b => b.PartCount == 1 && b.DefOf(0) == beam);
        want(alone != null, "a piece cut from the block is a body of its own");

        // ---- The machine goes: the piece on its die becomes a finished piece.
        var w2 = new ConstructWorld { CreativeRecycling = false };
        if (ExBench(w2, spot + new Vector3(0, 12, 0)) is not { } b)
        {
            want(false, "no bench for the rest");
            return;
        }
        var (m, e, st) = b;
        ExRun(w2, 1.1f);
        var len = ConstructExtruder.LengthOf(m, e);
        w2.RemovePart(m, e);
        foreach (var x in w2.Bodies)
            if (x.Dirty)
                x.Rebuild();
        var freed = w2.Bodies.FirstOrDefault(x => x.PartCount == 1 && x.DefOf(0).IsExtrusion);
        want(freed != null && !freed.DefOf(0).ExKey.Growing && freed.DefOf(0).ExKey.L == len && freed.DefOf(0).Sockets.Length > 1, $"the piece of a machine that went ({freed?.DefOf(0).Name}, {len} cm)");
        _ = st;

        // ---- Taking the piece off with the remover's path: the machine starts a new one.
        var w3 = new ConstructWorld { CreativeRecycling = false };
        if (ExBench(w3, spot + new Vector3(0, 12, 0)) is not { } b3)
            return;
        var (m3, e3, _) = b3;
        ExRun(w3, 1.1f);
        var pi = ConstructExtruder.ProductOf(m3, e3);
        w3.RemovePart(m3, pi);
        m3.Rebuild();
        want(ConstructExtruder.ProductOf(m3, e3) < 0, "the piece is gone from the die");
        ExRun(w3, 1.1f);
        want(ConstructExtruder.ProductOf(m3, e3) >= 0, "the machine starts a new piece");

        // ---- Destroyed: damage by volume and armour, then a wreck the Recycler takes for its metal.
        var w4 = new ConstructWorld { CreativeRecycling = false };
        var piece = ExPiece(1, 100, 100, 200);
        var body = ExBody(w4, piece, spot + new Vector3(0, 4, 0), Quaternion.Identity, false);
        if (body == null)
            return;
        body.Parts[0].Paint = 0x01C04020;
        var hp = body.Parts[0].Health;
        w4.DamagePart(body, 0, 1e7f, out var dealt);
        foreach (var x in w4.Bodies)
            if (x.Dirty)
                x.Rebuild();
        want(dealt > 0 && hp == piece.Health, $"a piece takes damage ({dealt}, health {hp} of {piece.Health})");
        var table = RecyclingTable.Current;
        var (mat, amount) = table.YieldOf(new CPart { Def = piece.Id, Broken = true });
        want(mat == table.IdOf("metal") && MathF.Abs(amount - piece.Mass * 0.5f) <= 1, $"salvage gives metal by mass ({amount} of {table.NameOf(mat)} for {piece.Mass} kg)");
        var wooden = ExPiece(0, 25, 25, 200, "wood");
        want(wooden.Material == "wood" && wooden.Flammable, "a wooden piece burns");
        want(table.YieldOf(new CPart { Def = wooden.Id, Broken = true }).Material == table.IdOf("wood"), "a wooden piece gives wood");
        // A wreck in a Recycler, for real.
        var w5 = new ConstructWorld { CreativeRecycling = true };
        var rec = ConstructParts.Need("Recycler");
        var host = ExBody(w5, rec, spot + new Vector3(0, 0.6f, 0), Quaternion.Identity, true);
        var small = ExPiece(0, 25, 25, 50);
        if (host != null)
        {
            var mid = (rec.IntakeMin + rec.IntakeMax) * 0.5f;
            var wreck = ConstructEdits.DropWreck(w5, new CPart { Def = small.Id, Broken = true }, host.ToWorld(mid), Quaternion.Identity, Vector3.Zero, 0);
            ExRun(w5, 0.5f);
            want(wreck != null && w5.Get(wreck.Id) == null && w5.StoreOf(host, 0)!.Amount >= 1, $"a wrecked piece goes through the Recycler ({w5.StoreOf(host, 0)!.Amount} metal)");
        }
        // Paint rides on a cut: the piece that comes off has the piece's colour.
        var w6 = new ConstructWorld { CreativeRecycling = false };
        if (ExBench(w6, spot + new Vector3(0, 12, 0)) is not { } b6)
            return;
        var (m6, e6, _) = b6;
        ExRun(w6, 5.5f);
        var pi6 = ConstructExtruder.ProductOf(m6, e6);
        m6.Parts[pi6].Paint = 0x01102030;
        m6.Parts[e6].State |= ConstructExtruder.CutPending;
        ExRun(w6, 0.2f);
        var off = w6.Bodies.FirstOrDefault(x => x != m6);
        want(off != null && off.Parts[0].Paint == 0x01102030, $"a cut keeps the paint ({off?.Parts[0].Paint:x})");
    }

    private static void ExtruderAlloc(Action<bool, string> want, Vector3 spot)
    {
        var w = new ConstructWorld { CreativeRecycling = false };
        if (ExBench(w, spot + new Vector3(0, 12, 0)) is not { } b)
        {
            want(false, "no bench for the allocation check");
            return;
        }
        var (c, e, st) = b;
        var q = World.Query;
        for (var t = 0; t < 900; t++)
        {
            w.Step(q, World.TickDt);
            w.ExtrudeDirty.Clear();
        }
        // The same lengths again: every shape is made already.
        var pi = ConstructExtruder.ProductOf(c, e);
        ref var pp = ref c.Parts[pi];
        var key = pp.D.ExKey;
        pp.Def = Extrusion.Def(key with { L = Extrusion.StepCm })!.Id;
        c.Rebuild();
        for (var t = 0; t < 30; t++)
        {
            w.Step(q, World.TickDt);
            w.ExtrudeDirty.Clear();
        }
        var a0 = GC.GetAllocatedBytesForCurrentThread();
        var clock = Stopwatch.StartNew();
        for (var t = 0; t < 600; t++)
        {
            w.Step(q, World.TickDt);
            w.ExtrudeDirty.Clear();
        }
        var alloc = GC.GetAllocatedBytesForCurrentThread() - a0;
        Line($"extruder: {alloc} B over 600 ticks of steady growth, {clock.Elapsed.TotalMilliseconds / 600:0.000} ms a tick");
        want(st.Busy && alloc < 1024, $"a steady run allocates ({alloc} B over 600 ticks, busy {st.Busy})");
    }

    /// <summary>
    /// "Extruder network": a real host, a client and a late joiner: the Extruder Line spawned by the client, fed on the host; the
    /// client's copy of the growing piece follows (the same key, the same hash), its look line says why it stopped, a cut piece
    /// reaches both by key, and a joiner mid-growth gets the piece on the die as it stands and follows on.
    /// </summary>
    private static void CheckExtruderNet()
    {
        if (Maps.Find(ConstructYardMap) is not { } yard || yard.Sandbox is not { } sb)
        {
            Fail($"extruder network: the {ConstructYardMap} map is missing");
            return;
        }
        const ushort port = 26491;
        var keep = sb.InfiniteParts;
        var clock = Stopwatch.StartNew();
        var now = 0.0;
        yard.RestoreProps();
        World.SetMap(yard);
        sb.InfiniteParts = false;
        var dropped0 = NetSafety.Dropped;
        try
        {
            using var server = new Game(NetPeer.Host(port, 4), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: yard.Id) { MapVoteEnabled = false };
            server.Mode = GameMode.Sandbox;
            using var a = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Alpha", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
            Pump(server, [a], 1.0, ref now, clock);
            for (var wait = 0; wait < 40 && server.Phase != MatchPhase.Playing; wait++)
                PumpFast(server, [a], 0.25, ref now);
            Pump(server, [a], 0.3, ref now, clock);
            if (a.LocalId == byte.MaxValue || !server.Players.TryGetValue(a.LocalId, out var sp))
            {
                Fail("extruder network: the client didn't join");
                return;
            }
            if (!sp.Alive)
                server.SelfTestRespawn(sp, now);
            Pump(server, [a], 0.3, ref now, clock);
            var w = server.Constructs;
            if (OpenSpot(w) is not { } spot)
            {
                Fail("extruder network: no open ground on the yard");
                return;
            }
            sp.Teleport(spot + new Vector3(0, 0.05f, 0));
            sp.Yaw = 0;
            Pump(server, [a], 0.3, ref now, clock);
            var known = w.Bodies.Select(b => b.Id).ToHashSet();
            a.SandboxSend(CActKind.Blueprint, part: (ushort)IndexOfBlueprint("Extruder Line"));
            Pump(server, [a], 1.0, ref now, clock);
            var line = w.Bodies.FirstOrDefault(b => !known.Contains(b.Id) && b.HasExtruder);
            if (line == null)
            {
                Fail($"extruder network: the line didn't spawn (\"{a.SandboxNotice}\")");
                return;
            }
            var ei = PartNamed(line, "Extruder");
            var ti = PartNamed(line, "Timer");
            var st = w.StoreOf(line, ei)!;
            ConstructRoutes.RemoveAt(w, line, ti, RouteType.Data);
            line.Rebuild();
            w.StructureDirty.Add(line.Id);
            var metal = RecyclingTable.Current.IdOf("metal");
            w.Deposit(line, ei, 300, metal);
            bool Await(Func<bool> done, double seconds, Game[] peers)
            {
                for (var t = 0.0; t < seconds && !done(); t += 0.1)
                    Pump(server, peers, 0.1, ref now, clock);
                return done();
            }
            bool Same(Game peer) => peer.Constructs.Get(line.Id) is { } g && g.PartCount == line.PartCount && ConstructNet.StructureHash(g) == ConstructNet.StructureHash(line);
            if (!Await(() => ConstructExtruder.LengthOf(line, ei) >= 40, 6, [a]))
            {
                Fail($"extruder network: it doesn't grow ({(ExtrudeStall)st.Refused}, {st.Amount} metal, charge {line.Charge})");
                return;
            }
            // The client's copy follows, by small updates, not whole structures.
            var copy = a.Constructs.Get(line.Id);
            if (copy == null || !Await(() => copy.DefOf(ei).IsExtruder && ConstructExtruder.LengthOf(copy, ei) >= 40, 3, [a]))
            {
                Fail($"extruder network: the client has no piece (copy {copy != null})");
                return;
            }
            copy = a.Constructs.Get(line.Id)!;
            var pi = ConstructExtruder.ProductOf(copy, ei);
            if (pi < 0 || !copy.DefOf(pi).IsExtrusion || !copy.DefOf(pi).ExKey.Growing)
            {
                Fail("extruder network: the client's piece isn't growing");
                return;
            }
            var lag = ConstructExtruder.LengthOf(line, ei) - ConstructExtruder.LengthOf(copy, ei);
            if (lag is < -5 or > 30)
                Fail($"extruder network: the client's piece is {lag} cm behind");
            var mass = copy.Mass;
            Pump(server, [a], 1.0, ref now, clock);
            if (ConstructExtruder.LengthOf(copy, ei) < 40 || copy.Mass <= mass)
                Fail($"extruder network: the client's copy doesn't keep up ({copy.Mass} from {mass} kg, {ConstructExtruder.LengthOf(copy, ei)} cm)");
            // The look line on the client, from what it heard.
            var cst = a.Constructs.StoreOf(copy, ei);
            var look = cst == null ? "" : ConstructExtruder.Look(copy, ei, cst, false, new SandboxInventory());
            if (!look.Contains("/") || !look.Contains("extruding"))
                Fail($"extruder network: the client's look line: \"{look}\"");
            // A joiner mid-growth.
            using var b = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Beta", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
            if (!Await(() => b.Constructs.Get(line.Id) is { } g && ConstructExtruder.LengthOf(g, ei) >= 40, 8, [a, b]))
            {
                Fail("extruder network: the joiner has no piece");
                return;
            }
            var bc = b.Constructs.Get(line.Id)!;
            var len = ConstructExtruder.LengthOf(bc, ei);
            Pump(server, [a, b], 1.0, ref now, clock);
            if (ConstructExtruder.LengthOf(bc, ei) <= len)
                Fail("extruder network: the joiner's copy doesn't follow");
            // A player in the path: the piece isn't grown through them.
            var pp0 = ConstructExtruder.ProductOf(line, ei);
            line.WorldFrame(line.Parts[pp0].Seg, out var pq, out var pt);
            var turn = pq * Rot24.Quat(line.Parts[pp0].Rot);
            var origin = pt + Vector3.Transform(line.Parts[pp0].Pos, pq);
            var axis = Vector3.Transform(Vector3.UnitZ, turn);
            var tip = origin + axis * (ConstructExtruder.LengthOf(line, ei) / 100f);
            sp.Teleport(tip + axis * 0.5f + new Vector3(0, -0.8f, 0));
            Pump(server, [a, b], 2.0, ref now, clock);
            var inPath = Vector3.Dot(sp.Position - origin, axis) - ConstructExtruder.LengthOf(line, ei) / 100f;
            Line($"extruder network: a player in the path ends {inPath:0.00} m past the tip");
            if (inPath < 0)
                Fail($"extruder network: the piece grew through a player ({inPath} m)");
            sp.Teleport(spot + new Vector3(-3, 0.05f, 0));
            // The stall reason reaches them: no power.
            line.Charge = 0;
            if (!Await(() => a.Constructs.StoreOf(a.Constructs.Get(line.Id)!, ei) is { } s && (ExtrudeStall)s.Refused == ExtrudeStall.NoPower, 2, [a, b]))
                Fail("extruder network: the client didn't hear why it stopped");
            line.Charge = line.ChargeCapacity;
            if (!Await(() => a.Constructs.StoreOf(a.Constructs.Get(line.Id)!, ei) is { Refused: 0 }, 3, [a, b]))
                Fail("extruder network: the stall reason stays on the client");
            // A cut: the piece comes off on every peer, by key, the hashes meet.
            var nb = w.Bodies.Count;
            line.Parts[ei].State |= ConstructExtruder.CutPending;
            if (!Await(() => w.Bodies.Count > nb, 1, [a, b]))
                Fail("extruder network: the Extruder didn't cut");
            var cutBody = w.Bodies.FirstOrDefault(x => x != line && x.PartCount == 1 && x.DefOf(0).IsExtrusion);
            if (cutBody != null)
            {
                if (!Await(() => a.Constructs.Get(cutBody.Id) is { } g && g.DefOf(0).Id == cutBody.DefOf(0).Id && b.Constructs.Get(cutBody.Id) is { } h && h.DefOf(0).Id == cutBody.DefOf(0).Id, 3, [a, b]))
                    Fail("extruder network: the cut piece didn't reach the clients");
            }
            // Machines' structure hashes meet once it settles (a stopped machine: no length change on the way).
            st.Amount = 0;
            Await(() => !st.Busy, 2, [a, b]);
            if (!Await(() => Same(a) && Same(b), 4, [a, b]))
                Fail("extruder network: the structures don't match when it stops");
            if (NetSafety.Dropped != dropped0)
                Fail($"extruder network: {NetSafety.Dropped - dropped0} packet(s) dropped: {NetSafety.LastDrop}");
        }
        finally
        {
            sb.InfiniteParts = keep;
        }
    }
}
