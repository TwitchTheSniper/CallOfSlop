using System.Numerics;
using Brutal.GlfwApi;

namespace CallOfSlop;

public static partial class SelfTest
{
    private static readonly TurnKey[] AllTurnKeys = [TurnKey.Left, TurnKey.Right, TurnKey.Up, TurnKey.Down, TurnKey.RollLeft, TurnKey.RollRight];

    private static TurnKey Opposite(TurnKey k) => (TurnKey)((int)k ^ 1);

    /// <summary>
    /// "Sandbox ghost turns" (board item dev-sandbox-arrow-rotate-ar02; Constructs/GhostTurn.cs): the rotate keys from any
    /// player yaw, on level and tilted frames and every starting turn: a key and its opposite undo each other, four of one
    /// key make a full turn, each is an exact quarter turn about the camera's (snapped) vertical, right or forward axis in
    /// the right direction (Left swings the front to your left, Up brings the top towards you, roll left takes the top to your
    /// left); on a socket the same keys give the same turn as free placement in the same frame (a cube on every yaw), a beam's
    /// skipped turns still undo; the default keys (the arrows, Shift + Left / Right) share the arrows with the Zombies
    /// spectate keys without a clash, a Shift chord hides its plain key, a binds version 11 file gets them; nothing allocates.
    /// </summary>
    private static void CheckGhostTurn()
    {
        var problems = new List<string>();
        void Want(bool ok, string what)
        {
            if (!ok)
                problems.Add(what);
        }

        // 1. The keys' maths over yaws, frames and turns.
        var frames = new List<(string Name, Quaternion Q)>();
        foreach (var fy in new[] { 0f, 0.4f, MathF.PI / 2, 2.9f, -2f })
            frames.Add(($"level yaw {fy:0.0}", ConstructPlace.Frame(Vector3.UnitY, fy, out _)));
        frames.Add(("a wall", ConstructPlace.Frame(Vector3.UnitX, 0.3f, out _)));
        frames.Add(("a slope", ConstructPlace.Frame(Vector3.Normalize(new Vector3(0.7f, 1, 0.2f)), -1.1f, out _)));
        frames.Add(("upside down", Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI) * Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.7f)));
        var probes = new[] { new Vector3(0.3f, -1.7f, 2.9f), Vector3.UnitY, -Vector3.UnitZ };
        int undo = 0, cycle = 0, exact = 0, wrongWay = 0, cases = 0;
        foreach (var (fname, frame) in frames)
            for (var yawDeg = 0; yawDeg < 360; yawDeg += 15)
            {
                var yaw = yawDeg * MathF.PI / 180;
                var right = GhostTurn.CameraRight(yaw);
                var fwd = World.Forward(yaw, 0);
                var up = Vector3.UnitY;
                foreach (var key in AllTurnKeys)
                {
                    var d = GhostTurn.Delta(key, frame, right, up);
                    var back = GhostTurn.Delta(Opposite(key), frame, right, up);
                    // The world axis it turns about and which way, from the probes.
                    var camAxis = key is TurnKey.Left or TurnKey.Right ? up : key is TurnKey.Up or TurnKey.Down ? right : fwd;
                    for (byte t = 0; t < Rot24.Count; t++)
                    {
                        cases++;
                        if (Rot24.Compose(back, Rot24.Compose(d, t)) != t)
                            undo++;
                        var four = t;
                        for (var k = 0; k < 4; k++)
                            four = Rot24.Compose(d, four);
                        if (four != t || Rot24.Compose(d, t) == t)
                            cycle++;
                        if (t != 0 || fname == "a slope")
                            continue; // (the axis and direction checks: frames square to the vertical; on a slope the snapped axes can be far off the camera's, by design only the turn rules above hold)
                        // An exact quarter turn about a frame axis near the camera's, the right way round.
                        var before = new Vector3[3];
                        var after = new Vector3[3];
                        for (var p = 0; p < 3; p++)
                        {
                            before[p] = Vector3.Transform(Rot24.Apply(t, probes[p]), frame);
                            after[p] = Vector3.Transform(Rot24.Apply(Rot24.Compose(d, t), probes[p]), frame);
                        }
                        var axis = Vector3.Normalize(Vector3.Cross(before[1], after[1]).LengthSquared() > 0.5f ? Vector3.Cross(before[1], after[1]) : Vector3.Cross(before[2], after[2]));
                        var turn = Quaternion.CreateFromAxisAngle(axis, MathF.PI / 2);
                        var isQuarter = true;
                        for (var p = 0; p < 3; p++)
                            isQuarter &= Vector3.Distance(Vector3.Transform(before[p], turn), after[p]) < 1e-3f;
                        if (!isQuarter || MathF.Abs(Vector3.Dot(axis, camAxis)) < 0.6f) // (snapped: a camera on a tie between two axes on a tilted frame is up to ~50 degrees off)
                            exact++;
                        // Which way: rotate the camera's own vectors about the world axis the key used.
                        var q = Quaternion.CreateFromAxisAngle(axis, MathF.PI / 2);
                        var ok = key switch
                        {
                            TurnKey.Left => Vector3.Dot(Vector3.Transform(fwd, q), -right) > 0.3f,
                            TurnKey.Right => Vector3.Dot(Vector3.Transform(fwd, q), right) > 0.3f,
                            TurnKey.Up => Vector3.Dot(Vector3.Transform(up, q), -fwd) > 0.3f,
                            TurnKey.Down => Vector3.Dot(Vector3.Transform(up, q), fwd) > 0.3f,
                            TurnKey.RollLeft => Vector3.Dot(Vector3.Transform(up, q), -right) > 0.3f,
                            _ => Vector3.Dot(Vector3.Transform(up, q), right) > 0.3f,
                        };
                        if (!ok)
                        {
                            wrongWay++;
                            if (wrongWay <= 3)
                                problems.Add($"{key} on {fname} at yaw {yawDeg} turns the wrong way");
                        }
                    }
                }
            }
        Line($"keys: {cases} key x frame x yaw x turn cases; a key then its opposite undo it ({undo} not), four make a full turn ({cycle} not), exact quarter turns about the camera's axes ({exact} not), the right way ({wrongWay} not)");
        Want(undo == 0, "a rotate key and its opposite don't undo each other");
        Want(cycle == 0, "four presses of a rotate key don't make a full turn (or one press does nothing)");
        Want(exact == 0, "a rotate key isn't a quarter turn about the camera's snapped axis");
        // Free placement on level ground, looking along the frame: from the default turn, Left points the front left, Up points it up, roll left the top left.
        {
            var frame = ConstructPlace.Frame(Vector3.UnitY, 0.8f, out _);
            var right = GhostTurn.CameraRight(0.8f);
            var fwd = World.Forward(0.8f, 0);
            Vector3 W(byte t, Vector3 local) => Vector3.Transform(Rot24.Apply(t, local), frame);
            var l = GhostTurn.Delta(TurnKey.Left, frame, right, Vector3.UnitY);
            var u = GhostTurn.Delta(TurnKey.Up, frame, right, Vector3.UnitY);
            var r = GhostTurn.Delta(TurnKey.RollLeft, frame, right, Vector3.UnitY);
            var front0 = W(0, -Vector3.UnitZ);
            var frontL = W(l, -Vector3.UnitZ);
            var frontU = W(u, -Vector3.UnitZ);
            var topR = W(r, Vector3.UnitY);
            Line($"keys at yaw 0.8 on level ground: front {front0.X:0.00},{front0.Z:0.00} (the look {fwd.X:0.00},{fwd.Z:0.00}); Left: front {frontL.X:0.00},{frontL.Y:0.00},{frontL.Z:0.00}; Up: front {frontU.X:0.00},{frontU.Y:0.00},{frontU.Z:0.00}; roll left: top {topR.X:0.00},{topR.Y:0.00},{topR.Z:0.00}");
            Want(Vector3.Dot(front0, fwd) > 0.99f && Vector3.Dot(frontL, -right) > 0.99f && frontU.Y > 0.99f && Vector3.Dot(topR, -right) > 0.99f,
                "on level ground Left doesn't point the front left, Up doesn't raise the front, or roll left doesn't tip the top left");
        }

        // 2. On a socket: the same keys as free placement, in the construct's frame.
        var block = ConstructParts.Need("Frame Block");
        var beam = ConstructParts.Need("Frame Beam");
        int sameAsFree = 0, attachCases = 0, attachUndo = 0, attachCycle = 0, beamUndo = 0, beamCases = 0;
        foreach (var cy in new[] { 0f, 0.6f, 2.2f })
        {
            var w = new ConstructWorld();
            var c = ConstructBuild.PlaceNew(w, block, new Vector3(0, 1, 0), cy);
            if (c == null)
            {
                problems.Add("a Frame Block couldn't be set down to build on");
                break;
            }
            c.WorldFrame(0, out var cq, out _);
            for (var socket = 0; socket < block.Sockets.Length; socket++)
                for (var yawDeg = 0; yawDeg < 360; yawDeg += 30)
                {
                    var yaw = yawDeg * MathF.PI / 180;
                    var right = GhostTurn.CameraRight(yaw);
                    var v0 = ConstructBuild.NextVariant(c, block, 0, socket, block.Sockets.Length * 4 - 1); // (the first that fits: the ghost's default)
                    if (v0 < 0 || ConstructBuild.Place(c, 0, socket, block, v0 / 4, v0 % 4, out var g0) != null)
                    {
                        problems.Add($"no Frame Block fits socket {socket} of a Frame Block");
                        continue;
                    }
                    foreach (var key in AllTurnKeys)
                    {
                        attachCases++;
                        var d = GhostTurn.Delta(key, cq, right, Vector3.UnitY);
                        var v1 = GhostTurn.AttachVariant(c, block, 0, socket, g0.Rot, d);
                        ConstructBuild.Place(c, 0, socket, block, Math.Max(v1, 0) / 4, Math.Max(v1, 0) % 4, out var g1);
                        if (v1 < 0 || g1.Rot != Rot24.Compose(d, g0.Rot))
                            sameAsFree++;
                        var v2 = v1 < 0 ? -1 : GhostTurn.AttachVariant(c, block, 0, socket, g1.Rot, GhostTurn.Delta(Opposite(key), cq, right, Vector3.UnitY));
                        if (v2 != v0)
                            attachUndo++;
                        var rot = g0.Rot;
                        var v = v0;
                        for (var k = 0; k < 4; k++)
                        {
                            v = GhostTurn.AttachVariant(c, block, 0, socket, rot, d);
                            if (v < 0)
                                break;
                            ConstructBuild.Place(c, 0, socket, block, v / 4, v % 4, out var gk);
                            rot = gk.Rot;
                        }
                        if (v != v0)
                            attachCycle++;
                    }
                }
            // A beam on the block's top: it can't take every turn there (it would hit the ground side of nothing, but its
            // sockets limit which faces meet); whatever it skips, a key and its opposite still come back.
            var top = Enumerable.Range(0, block.Sockets.Length).First(s => block.Sockets[s].Dir == 2);
            var bv0 = ConstructBuild.NextVariant(c, beam, 0, top, beam.Sockets.Length * 4 - 1);
            if (bv0 >= 0 && ConstructBuild.Place(c, 0, top, beam, bv0 / 4, bv0 % 4, out var b0) == null)
                foreach (var key in AllTurnKeys)
                    for (var yawDeg = 0; yawDeg < 360; yawDeg += 45)
                    {
                        beamCases++;
                        var right = GhostTurn.CameraRight(yawDeg * MathF.PI / 180);
                        var b1 = GhostTurn.AttachVariant(c, beam, 0, top, b0.Rot, GhostTurn.Delta(key, cq, right, Vector3.UnitY));
                        if (b1 < 0)
                            continue;
                        ConstructBuild.Place(c, 0, top, beam, b1 / 4, b1 % 4, out var bg1);
                        if (GhostTurn.AttachVariant(c, beam, 0, top, bg1.Rot, GhostTurn.Delta(Opposite(key), cq, right, Vector3.UnitY)) != bv0)
                            beamUndo++;
                    }
            else
                problems.Add("a Frame Beam doesn't fit on a Frame Block's top");
        }
        Line($"sockets: {attachCases} key x socket x yaw cases on a Frame Block (3 construct yaws): the turn free placement gives in the same frame ({sameAsFree} not), a key then its opposite back to the default ({attachUndo} not), four back round ({attachCycle} not); a Frame Beam on top: {beamCases} cases, undone ({beamUndo} not)");
        Want(sameAsFree == 0, "a rotate key on a socket doesn't give the turn it gives in free placement");
        Want(attachUndo == 0 && beamUndo == 0, "on a socket a rotate key and its opposite don't come back to the same variant");
        Want(attachCycle == 0, "on a socket four presses of a rotate key don't come back round");

        // 3. The keys: defaults, contexts, chords, migration, the HUD text.
        var kd = Keybinds.Defaults;
        Want(kd[(int)GameAction.RotateLeft] == Keybinds.Key(GlfwKey.Left) && kd[(int)GameAction.RotateRight] == Keybinds.Key(GlfwKey.Right)
             && kd[(int)GameAction.RotateUp] == Keybinds.Key(GlfwKey.Up) && kd[(int)GameAction.RotateDown] == Keybinds.Key(GlfwKey.Down)
             && kd[(int)GameAction.RollLeft] == Keybinds.Shift(GlfwKey.Left) && kd[(int)GameAction.RollRight] == Keybinds.Shift(GlfwKey.Right),
            "the rotate keys don't default to the arrows (roll: Shift + Left / Right)");
        Want(Keybinds.Context(GameAction.RotateLeft) == Keybinds.BindContext.Sandbox && !Keybinds.Overlap(GameAction.RotateLeft, GameAction.SpectatePrev)
             && Keybinds.Overlap(GameAction.RotateLeft, GameAction.Jump) && Duplicates(kd).Length == 0, "the rotate keys clash with the Zombies spectate keys (or with nothing at all)");
        var down = new bool[(int)GameAction.COUNT];
        void Hold(GlfwKey key, bool shift, bool sandbox)
        {
            // (what Keybinds.IsDown reads with the key held, and Shift too or not: the chord and the plain key both)
            for (var i = 0; i < down.Length; i++)
                down[i] = kd[i] == Keybinds.Key(key) || (shift && kd[i] == Keybinds.Shift(key));
            Keybinds.Resolve(kd, sandbox, down);
        }
        Hold(GlfwKey.Left, true, true);
        var rolls = down[(int)GameAction.RollLeft] && !down[(int)GameAction.RotateLeft] && !down[(int)GameAction.SpectatePrev];
        Hold(GlfwKey.Left, false, true);
        var turns = down[(int)GameAction.RotateLeft] && !down[(int)GameAction.RollLeft] && !down[(int)GameAction.SpectatePrev];
        Hold(GlfwKey.Left, false, false);
        var spectates = down[(int)GameAction.SpectatePrev] && !down[(int)GameAction.RotateLeft];
        Line($"keys: Shift + Left in Sandbox rolls only {rolls}; Left turns {turns}; Left elsewhere spectates only {spectates}; HUD: \"{Program.TurnKeysText(kd)}\"");
        Want(rolls && turns && spectates, "Shift + Left doesn't roll alone, Left doesn't turn in Sandbox, or Left doesn't spectate elsewhere");
        Want(Program.TurnKeysText(kd) == "<- -> ^ v: turn, Shift + <- ->: roll, R: reset", "the rotate keys' HUD text isn't \"<- -> ^ v: turn, Shift + <- ->: roll, R: reset\"");
        var old = (int[])kd.Clone();
        foreach (var a in new[] { GameAction.RotateLeft, GameAction.RotateRight, GameAction.RotateUp, GameAction.RotateDown, GameAction.RollLeft, GameAction.RollRight })
            old[(int)a] = Keybinds.Unbound;
        var given = (int[])old.Clone();
        Keybinds.Migrate(given, 11);
        var taken = (int[])old.Clone();
        taken[(int)GameAction.Jump] = Keybinds.Key(GlfwKey.Up); // (a player who jumps on the up arrow keeps it)
        Keybinds.Migrate(taken, 11);
        Want(given.SequenceEqual(kd), "a binds version 11 file doesn't get the rotate keys");
        Want(taken[(int)GameAction.Jump] == Keybinds.Key(GlfwKey.Up) && taken[(int)GameAction.RotateUp] == Keybinds.Unbound && taken[(int)GameAction.RotateLeft] == Keybinds.Key(GlfwKey.Left),
            "migrating took the up arrow from a key used everywhere, or didn't give the rotate keys that are free");
        var swap = (int[])kd.Clone();
        Keybinds.Assign(swap, GameAction.RotateLeft, Keybinds.Key(GlfwKey.Number7)); // (nothing on 7)
        Keybinds.Assign(swap, GameAction.RotateLeft, Keybinds.Key(GlfwKey.Left));
        Keybinds.Assign(swap, GameAction.Jump, Keybinds.Shift(GlfwKey.Space)); // (only Sandbox actions take chords)
        Want(swap[(int)GameAction.SpectatePrev] == Keybinds.Key(GlfwKey.Left) && swap[(int)GameAction.RotateLeft] == Keybinds.Key(GlfwKey.Left) && swap[(int)GameAction.Jump] == Keybinds.Key(GlfwKey.Space),
            "binding Left to the rotate key took it off the spectate key, or a chord bound to an action outside Sandbox");

        // 4. Nothing allocates.
        {
            var w = new ConstructWorld();
            var c = ConstructBuild.PlaceNew(w, block, new Vector3(0, 1, 0), 0.3f)!;
            c.WorldFrame(0, out var cq, out _);
            GhostTurn.AttachVariant(c, block, 0, 0, 0, GhostTurn.Delta(TurnKey.Left, cq, Vector3.UnitX, Vector3.UnitY));
            Keybinds.Resolve(kd, true, down);
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var k = 0; k < 200; k++)
            {
                var d = GhostTurn.Delta((TurnKey)(k % 6), cq, GhostTurn.CameraRight(k * 0.1f), Vector3.UnitY);
                GhostTurn.AttachVariant(c, block, 0, k % block.Sockets.Length, (byte)(k % 24), d);
                Keybinds.Resolve(kd, (k & 1) == 0, down);
            }
            var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            Line($"allocations: 200 key turns on a socket and key resolves: {bytes} bytes");
            Want(bytes == 0, "turning the ghost or resolving the keys allocates");
        }
        if (problems.Count > 0)
            Fail("sandbox ghost turns: " + string.Join("; ", problems));
    }
}
