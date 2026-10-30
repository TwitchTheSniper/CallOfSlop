using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// "Construct smart rotation" (board item dev-sandbox-smart-rotation-sr01, decision 22; Constructs/SmartRotation.cs):
    /// seeded, scripted builds: a wheel on the left gets its outer face outward and the right one mirrors the left's turn;
    /// a seat faces the vehicle's forward on a chassis built turned 90 degrees (from the root, then from the first seat);
    /// a third panel continues its row's turn; after one correction the next same placement (and free placement) uses it,
    /// and it survives settings.txt; a blocked turn is never guessed when a free one exists; the same inputs give the same
    /// guess; a blueprint step's "auto" spin; the weights file and the part files' "faces"; nothing allocates.
    /// </summary>
    private static void CheckSmartRotation()
    {
        var problems = new List<string>();
        void Want(bool ok, string what)
        {
            if (!ok)
                problems.Add(what);
        }

        SmartRotationWeights.Use(null);
        var weights = SmartRotationWeights.Current;
        Want(weights.Problem.Length == 0, $"Constructs/SmartRotation.json was refused: {weights.Problem}");
        Want(SmartRotationWeights.Parse("{\"format\": \"call-of-slop-smart-rotation/1\", \"sideways\": 2}").Problem.Contains("unknown key"), "a weights file with an unknown key wasn't refused");
        Want(SmartRotationWeights.Parse("{\"format\": \"call-of-slop-smart-rotation/1\", \"mirror\": -1}").Problem.Length > 0, "a negative weight wasn't refused");

        var block = ConstructParts.Need("Frame Block");
        var beam = ConstructParts.Need("Frame Beam");
        var seat = ConstructParts.Need("Seat");
        var susp = ConstructParts.Need("Suspension");
        var wheel = ConstructParts.Need("Wheel");
        var plate = ConstructParts.Need("Plate Steel 0.25 m");
        Want(seat.Faces == FaceHint.Forward && wheel.Faces == FaceHint.Side && wheel.FaceAxis == 0 && plate.Faces == FaceHint.Outward && plate.FaceAxis == 2,
            "the role defaults: a seat faces forward, a wheel's +x to the side, a panel's +y outward");
        Want(ConstructParts.Need("Bumper") is { Faces: FaceHint.Outward, FaceAxis: 5 }, "the Bumper's file says it faces outward by its -z");
        try
        {
            var bad = File.ReadAllText(Path.Combine(ConstructParts.Folder, "Seat.json")).Replace("\"role\": \"seat\",", "\"role\": \"seat\", \"faces\": \"sideways\",");
            ConstructParts.Parse(bad, "Seat.json");
            problems.Add("a part file with \"faces\": \"sideways\" wasn't refused");
        }
        catch (ConstructPartException)
        {
        }

        var turn90 = SmartRotation.FromDirs(0, 2); // (front to +x, top up)
        Span<int> vars = stackalloc int[Rot24.Count];
        Span<byte> rots = stackalloc byte[Rot24.Count];
        Span<int> vars2 = stackalloc int[Rot24.Count];
        Span<byte> rots2 = stackalloc byte[Rot24.Count];

        // 1. Wheels: the left's outer face outward; the right mirrors the left (even turned off its default).
        {
            var w = new ConstructWorld();
            var c = Root(w, block, 0, Quaternion.Identity);
            var sl = Attach(c, w, susp, 0, SocketFacing(c, 0, 1), null);
            var sr = Attach(c, w, susp, 0, SocketFacing(c, 0, 0), null);
            var axleL = AxleOf(c, sl);
            var axleR = AxleOf(c, sr);
            var n = sl < 0 || sr < 0 || axleL < 0 || axleR < 0 ? 0 : SmartRotation.Rank(c, wheel, sl, axleL, null, vars, rots);
            Want(n >= 2, $"the left wheel has {n} turns to guess from (the suspensions: {sl}, {sr})");
            if (n >= 2)
            {
                Want(ConstructParts.DirVector(Rot24.ApplyDir(rots[0], 0)).X < 0, "the left wheel's guess doesn't put its outer face (+x) out to the left");
                var leftRot = rots[1]; // (the player turned it off the guess)
                var lw = AttachRot(c, w, wheel, sl, axleL, leftRot);
                var m = lw < 0 ? 0 : SmartRotation.Rank(c, wheel, sr, axleR, null, vars, rots);
                Want(m > 0 && Rot24.ApplyDir(rots[0], 0) == Mirror(Rot24.ApplyDir(leftRot, 0)) && Rot24.ApplyDir(rots[0], 2) == Mirror(Rot24.ApplyDir(leftRot, 2)),
                    $"the right wheel's guess (turn {(m > 0 ? rots[0] : -1)}) isn't the mirror of the left's (turn {leftRot})");
                Want(m > 0 && ConstructParts.DirVector(Rot24.ApplyDir(rots[0], 0)).X > 0, "the right wheel's guess doesn't put its outer face out to the right");
            }
        }

        // 2. A seat faces the vehicle's forward on a chassis turned 90 degrees (the root part's front, then the first seat's).
        {
            var w = new ConstructWorld();
            var c = Root(w, block, turn90, Quaternion.CreateFromAxisAngle(Vector3.UnitY, 1.1f));
            var top = SocketFacing(c, 0, 2);
            var n = SmartRotation.Rank(c, seat, 0, top, null, vars, rots);
            Want(n > 0 && Rot24.ApplyDir(rots[0], 5) == 0 && Rot24.ApplyDir(rots[0], 2) == 2, $"a seat on a chassis turned 90 degrees doesn't face its forward (+x): turn {(n > 0 ? rots[0] : -1)}");
            var old = ConstructBuild.NextVariant(c, seat, 0, top, seat.Sockets.Length * 4 - 1);
            ConstructBuild.Place(c, 0, top, seat, old / 4, old % 4, out var og);
            Want(Rot24.ApplyDir(og.Rot, 5) == 5, "the old fixed order (smart rotation off) no longer faces the rest frame's -z");
            var s1 = n > 0 ? AttachRot(c, w, seat, 0, top, rots[0]) : -1;
            var b2 = Attach(c, w, block, 0, SocketFacing(c, 0, 4), null); // (beside the seat: the chassis goes on along +z)
            var n2 = s1 < 0 || b2 < 0 ? 0 : SmartRotation.Rank(c, seat, b2, SocketFacing(c, b2, 2), null, vars, rots);
            Want(n2 > 0 && Rot24.ApplyDir(rots[0], 5) == 0, "a second seat doesn't face the first seat's forward (+x)");
            Want(SmartRotation.FramePart(c) == s1, "the vehicle frame doesn't come from the first seat");
        }

        // 3. A third panel continues its row's turn.
        {
            var w = new ConstructWorld();
            var c = Root(w, beam, 0, Quaternion.Identity);
            int Sock(string name) => Array.FindIndex(beam.Sockets, s => s.Name == name);
            var n = SmartRotation.Rank(c, plate, 0, Sock("y+0"), null, vars, rots);
            var pick = -1;
            for (var k = 1; k < n && pick < 0; k++)
                if (Rot24.ApplyDir(rots[k], 2) == 2)
                    pick = k; // (flat, but not the guess: a turn about the vertical)
            Want(pick > 0, "a panel on a beam's top has no flat turn other than the guess");
            if (pick > 0)
            {
                var rowRot = rots[pick];
                var p0 = AttachRot(c, w, plate, 0, Sock("y+0"), rowRot);
                var p1 = AttachRot(c, w, plate, 0, Sock("y+1"), rowRot);
                var m = p0 < 0 || p1 < 0 ? 0 : SmartRotation.Rank(c, plate, 0, Sock("y+2"), null, vars, rots);
                Want(m > 0 && rots[0] == rowRot, $"the third panel's guess (turn {(m > 0 ? rots[0] : -1)}) doesn't continue the row (turn {rowRot})");
            }
        }

        // 4. One correction: the next same placement uses it (sockets and free placement), and it survives settings.txt.
        {
            var mem = new SmartRotationMemory();
            var w = new ConstructWorld();
            var c1 = Root(w, block, 0, Quaternion.Identity);
            var top = SocketFacing(c1, 0, 2);
            var n = SmartRotation.Rank(c1, seat, 0, top, mem, vars, rots);
            Want(n >= 2, "a seat on a block's top has fewer than two turns");
            if (n >= 2)
            {
                var chosen = rots[1];
                Want(SmartRotation.Placed(mem, c1, seat, 0, top, chosen, rots[0]), "turning away from the guess wasn't learned");
                var c2 = Root(w, block, 0, Quaternion.Identity, new Vector3(6, 1, 0));
                var m = SmartRotation.Rank(c2, seat, 0, SocketFacing(c2, 0, 2), mem, vars, rots);
                Want(m > 0 && rots[0] == chosen, $"after one correction the next seat's guess (turn {(m > 0 ? rots[0] : -1)}) isn't the corrected turn {chosen}");
                var path = Path.Combine(Path.GetTempPath(), $"cos_smartrot_{Environment.ProcessId}.txt");
                try
                {
                    var st = new Settings { SmartRotationLearned = mem.Save() };
                    Want(new Settings().SmartRotation, "smart rotation isn't on by default");
                    st.SmartRotation = false;
                    st.Save(path);
                    File.WriteAllLines(path, File.ReadAllLines(path).Where(l => l.StartsWith("version=", StringComparison.Ordinal) || l.StartsWith("smart_rotation", StringComparison.Ordinal))); // (just these: the map lines would load every map)
                    var back = Settings.Load(path);
                    Want(!back.SmartRotation && back.SmartRotationLearned.SequenceEqual(st.SmartRotationLearned) && back.SmartRotationLearned.Count == 1,
                        $"settings.txt didn't keep the setting and the correction ({back.SmartRotationLearned.Count} entries)");
                    var mem2 = new SmartRotationMemory();
                    mem2.Load(back.SmartRotationLearned);
                    var c3 = Root(w, block, 0, Quaternion.Identity, new Vector3(-6, 1, 0));
                    var k = SmartRotation.Rank(c3, seat, 0, SocketFacing(c3, 0, 2), mem2, vars, rots);
                    Want(k > 0 && rots[0] == chosen, "a correction loaded from settings.txt isn't used");
                }
                finally
                {
                    foreach (var f in new[] { path, path + ".bak", path + ".tmp" })
                        try
                        {
                            File.Delete(f);
                        }
                        catch (IOException)
                        {
                        }
                }
            }
            Span<byte> free = stackalloc byte[Rot24.Count];
            var fm = new SmartRotationMemory();
            Want(SmartRotation.RankFree(seat, fm, free) == Rot24.Count && free[0] == 0, "free placement's first guess for a seat isn't upright facing forward (turn 0)");
            var turned = ConstructPlace.Turns[2];
            Want(SmartRotation.PlacedFree(fm, seat, turned, free[0]), "turning a part away from the guess when setting it down wasn't learned");
            SmartRotation.RankFree(seat, fm, free);
            Want(free[0] == turned, "free placement's next guess doesn't use the correction");
        }

        // 5. A blocked turn is never guessed when a free one exists.
        {
            var w = new ConstructWorld();
            var c = Root(w, block, turn90, Quaternion.Identity);
            var side = Attach(c, w, block, 0, SocketFacing(c, 0, 0), null);
            var post = side < 0 ? -1 : Attach(c, w, block, side, SocketFacing(c, side, 2), null);
            var top = SocketFacing(c, 0, 2);
            var forwardFits = false;
            for (var v = 0; v < seat.Sockets.Length * 4; v++)
                if (ConstructBuild.Place(c, 0, top, seat, v / 4, v % 4, out var g) == null && (Rot24.ApplyDir(g.Rot, 5) >> 1) == 0)
                    forwardFits = true;
            Want(post >= 0 && !forwardFits, "the scene doesn't block the seat facing the chassis's forward (+x)");
            var n = SmartRotation.Rank(c, seat, 0, top, null, vars, rots);
            var allFit = n > 0;
            for (var k = 0; k < n; k++)
                allFit &= ConstructBuild.Place(c, 0, top, seat, vars[k] / 4, vars[k] % 4, out var g) == null && g.Rot == rots[k];
            Want(allFit, "smart rotation listed a turn that doesn't fit (or listed none though one fits)");
            Want(n > 0 && (Rot24.ApplyDir(rots[0], 5) >> 1) == 2 && Rot24.ApplyDir(rots[0], 2) == 2, "with forward blocked the guess isn't an upright free turn");
        }

        // 6. The same inputs give the same guess; nothing allocates.
        {
            var w = new ConstructWorld();
            var c = Root(w, beam, 0, Quaternion.Identity);
            var cB = Root(w, beam, 0, Quaternion.Identity, new Vector3(8, 1, 0));
            var s = Array.FindIndex(beam.Sockets, x => x.Name == "x+1");
            var n1 = SmartRotation.Rank(c, plate, 0, s, null, vars, rots);
            var n2 = SmartRotation.Rank(cB, plate, 0, s, null, vars2, rots2);
            Want(n1 > 1 && n1 == n2 && vars[..n1].SequenceEqual(vars2[..n2]) && rots[..n1].SequenceEqual(rots2[..n2]), "the same build gave two different rankings");
            var mem = new SmartRotationMemory();
            SmartRotation.Rank(c, seat, 0, s, mem, vars, rots);
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var k = 0; k < 100; k++)
                SmartRotation.Rank(c, (k & 1) == 0 ? plate : seat, 0, k % beam.Sockets.Length, mem, vars, rots);
            var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            Line($"allocations: 100 rankings on a beam: {bytes} bytes");
            Want(bytes == 0, "ranking a socket's turns allocates");
        }

        // 7. A blueprint step's "auto" spin is the context guess (a seat on a turned root faces its front); a number keeps the fixed order.
        {
            var json = $$"""
                {"format": "{{ConstructBuild.BlueprintFormat}}", "name": "Smart test", "steps": [
                  {"part": "Frame Block", "spin": {{turn90}}},
                  {"part": "Seat", "on": 0, "socket": "+y", "by": "bottom", "spin": "auto"},
                  {"part": "Frame Block", "on": 0, "socket": "+x", "by": "-x"},
                  {"part": "Frame Block", "on": 2, "socket": "+x", "by": "-x"},
                  {"part": "Seat", "on": 3, "socket": "+y", "by": "bottom", "spin": 0}]}
                """;
            var bp = ConstructBuild.ParseBlueprint(json, "smart.json");
            var w = new ConstructWorld();
            var c = ConstructBuild.Build(w, bp, Vector3.Zero, Quaternion.Identity, out var why);
            Want(c != null, $"the blueprint with an auto spin didn't build: {why}");
            if (c != null)
            {
                int seats = 0, faced = 0, fixedOld = 0;
                for (var i = 0; i < c.PartCount; i++)
                    if (c.DefOf(i).Role == PartRole.Seat)
                    {
                        seats++;
                        if (Rot24.ApplyDir(c.Parts[i].Rot, 5) == Rot24.ApplyDir(turn90, 5))
                            faced++;
                        else if (Rot24.ApplyDir(c.Parts[i].Rot, 5) == 5)
                            fixedOld++;
                    }
                Want(seats == 2 && faced == 1 && fixedOld == 1, $"the auto step's seat doesn't face the root's front while spin 0 keeps the fixed order ({seats} seats, {faced} facing it, {fixedOld} the old way)");
            }
        }

        if (problems.Count > 0)
            Fail("construct smart rotation: " + string.Join("; ", problems));
        return;

        static byte Mirror(byte dir) => (dir >> 1) == 0 ? (byte)(dir ^ 1) : dir; // (across the x = 0 plane)

        static Construct Root(ConstructWorld w, ConstructPartDef def, byte turn, Quaternion rot, Vector3? at = null) =>
            ConstructBuild.PlaceNew(w, def, new FreePlacement { Pos = at ?? new Vector3(0, 1, 0), Rot = rot, Turn = turn })
            ?? throw new InvalidOperationException($"a {def.Name} couldn't be set down");

        // The free socket of part i facing rest-frame direction dir (-1: none).
        static int SocketFacing(Construct c, int i, byte dir)
        {
            for (var s = 0; s < c.SocketCount(i); s++)
            {
                c.SocketAt(i, s, out _, out var d);
                if (d == dir && !c.SocketUsed(i, s))
                    return s;
            }
            return -1;
        }

        static int AxleOf(Construct c, int i) => i < 0 ? -1 : Array.FindIndex(c.DefOf(i).Sockets, s => s.Kind == SocketKind.Axle);

        // Attaches def on that socket: smart rotation's context guess (or the given turn). Returns the new part or -1.
        static int Attach(Construct c, ConstructWorld w, ConstructPartDef def, int part, int socket, byte? rot)
        {
            if (socket < 0)
                return -1;
            Span<int> v = stackalloc int[Rot24.Count];
            Span<byte> r = stackalloc byte[Rot24.Count];
            if (rot is { } want)
                return AttachRot(c, w, def, part, socket, want);
            return SmartRotation.Rank(c, def, part, socket, null, v, r) > 0 ? AttachRot(c, w, def, part, socket, r[0]) : -1;
        }

        static int AttachRot(Construct c, ConstructWorld w, ConstructPartDef def, int part, int socket, byte rot)
        {
            var v = GhostTurn.VariantFor(c, def, part, socket, rot);
            if (v < 0 || ConstructBuild.Place(c, part, socket, def, v / 4, v % 4, out var g) != null)
                return -1;
            var i = ConstructBuild.Attach(w, c, def, g);
            c.Rebuild();
            return i;
        }
    }
}
