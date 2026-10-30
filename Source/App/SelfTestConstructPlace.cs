using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// "Construct placement" (board item dev-sandbox-new-construct-nc01; docs/sandbox/CONSTRUCTS.md "New constructs"): the
    /// Rot24 helpers and the rotate key's order; the pose on level ground, on the grid and on a wall; in an offline Sandbox
    /// game on the yard the Construct Core set down on open ground is a new one-part construct of the player's with a working
    /// socket grid, a second part attaches to it; placing over a construct, over a player or in the air is refused with a
    /// reason; a free part moves when pushed and a pinned one (creative's anchor) and a foundation don't, the anchor is refused
    /// outside creative; building onto the map's loose part claims it; a part welded so it touches another construct joins the
    /// two into one (links, owner, the other gone); the per-player limit; the placement checks allocate nothing; and over a
    /// loopback a client's placement, pin and join show on its copy.
    /// </summary>
    private static void CheckConstructPlacement()
    {
        if (Maps.Find(ConstructYardMap) is not { } yard || yard.Sandbox is not { } sb)
        {
            Fail($"construct placement: the {ConstructYardMap} map (with its sandbox section) is missing");
            return;
        }
        PlacementData();
        var keepInfinite = sb.InfiniteParts;
        var keepSeconds = sb.DebrisSeconds;
        try
        {
            PlacementOffline(yard, sb);
            sb.InfiniteParts = keepInfinite;
            PlacementOverWire(yard, sb);
        }
        finally
        {
            sb.InfiniteParts = keepInfinite;
            sb.DebrisSeconds = keepSeconds;
        }
    }

    private static void PlacementData()
    {
        // Rot24: composing turns is applying one after the other; each turn's quaternion is the same turn; Nearest finds it.
        var composeOk = true;
        var quatOk = true;
        var probe = new Vector3(0.3f, -1.7f, 2.9f);
        for (byte a = 0; a < Rot24.Count; a++)
        {
            if (Vector3.Distance(Vector3.Transform(probe, Rot24.Quat(a)), Rot24.Apply(a, probe)) > 1e-4f || Rot24.Nearest(Rot24.Quat(a), out var m) != a || m < 0.9999f)
                quatOk = false;
            for (byte b = 0; b < Rot24.Count; b++)
                if (Vector3.Distance(Rot24.Apply(Rot24.Compose(a, b), probe), Rot24.Apply(a, Rot24.Apply(b, probe))) > 1e-5f)
                    composeOk = false;
        }
        var turns = ConstructPlace.Turns;
        var distinct = turns.Distinct().Count() == Rot24.Count;
        var upright = turns.Take(4).All(r => Rot24.Apply(r, Vector3.UnitY).Y > 0.99f) && turns[0] == 0;
        var cycle = true;
        byte t = 0;
        for (var k = 0; k < Rot24.Count; k++)
            t = ConstructPlace.NextTurn(t);
        cycle = t == 0;
        Line($"placement data: Rot24 compose right {composeOk}, quaternions and Nearest right {quatOk}; the rotate key's 24 turns distinct {distinct}, upright first {upright}, cycle back {cycle}");
        if (!composeOk || !quatOk || !distinct || !upright || !cycle)
            Fail("construct placement: the Rot24 helpers or the rotate key's order are wrong");

        // The starter core: loaded, a core (never loose, the root), the welder's.
        var core = ConstructParts.Find("Construct Core");
        var w = new ConstructWorld();
        var placed = core == null ? null : ConstructBuild.PlaceNew(w, core, Vector3.Zero, 0);
        Line($"placement data: the Construct Core {(core == null ? "is missing" : $"loads (core {core.Core}, {core.Tool}, {core.Sockets.Length} sockets), set down it is loose {placed?.Loose}")}");
        if (core is not { Core: true, Tool: ConstructTool.Welder } || placed == null || placed.Loose || ConstructParts.Need("Frame Block").Core)
            Fail("construct placement: the Construct Core part isn't a welded core that never counts as a loose part");

        // Poses without a map: level (upright, on the point), on the grid (snapped, yaw squared), on a wall (up out of it).
        var block = ConstructParts.Need("Frame Block");
        var beamDef = ConstructParts.Need("Frame Beam");
        ConstructPlace.Pose(null, block, new Vector3(1.13f, 2f, -0.4f), new Vector3(0.05f, 1, 0), 0.3f, 0, false, out var level);
        ConstructPlace.Pose(null, block, new Vector3(1.13f, 2f, -0.4f), Vector3.UnitY, 0.3f, 0, true, out var grid);
        ConstructPlace.Pose(null, block, new Vector3(4f, 1.5f, 0), Vector3.UnitX, 0, 0, false, out var wall);
        var levelOk = Vector3.Transform(Vector3.UnitY, level.Rot).Y > 0.9999f && MathF.Abs(level.Pos.Y - 2.26f) < 0.005f;
        var gridOk = MathF.Abs(grid.Pos.X - 1.0f) < 1e-4f && MathF.Abs(grid.Pos.Z + 0.5f) < 1e-4f && Vector3.Transform(Vector3.UnitZ, grid.Rot).Z > 0.9999f;
        var wallUp = Vector3.Transform(Vector3.UnitY, wall.Rot);
        var wallOk = wallUp.X > 0.9999f && MathF.Abs(wall.Pos.X - 4.26f) < 0.005f;
        Line($"placement poses: level ground upright at {level.Pos.Y:0.000} {levelOk}; on the grid at {grid.Pos.X:0.00}, {grid.Pos.Z:0.00} squared {gridOk}; on a wall its up {wallUp.X:0.00}, {wallUp.Y:0.00}, {wallUp.Z:0.00} at x {wall.Pos.X:0.000} {wallOk}");
        if (!levelOk || !gridOk || !wallOk)
            Fail("construct placement: the pose doesn't follow the surface, the level snap or the grid");

        // Joining a construct turned a quarter round (and on its side): its parts keep their places in the world.
        var jw = new ConstructWorld();
        var ja = ConstructBuild.PlaceNew(jw, block, new FreePlacement { Pos = Vector3.Zero, Rot = Quaternion.Identity });
        // (the beam's middle side socket on the block's +x face, the beam turned so that socket faces -x and it isn't upright)
        var bs = Array.FindIndex(beamDef.Sockets, x => x.Name == "x+1");
        Span<byte> cands = stackalloc byte[4];
        var nc = Rot24.Matching(beamDef.Sockets[Math.Max(0, bs)].Dir, 1, cands);
        byte br = 0;
        for (var k = 0; k < nc; k++)
            if (Rot24.Apply(cands[k], Vector3.UnitY).Y < 0.5f)
                br = cands[k];
        var jb = bs < 0 ? null : ConstructBuild.PlaceNew(jw, beamDef, new FreePlacement { Pos = new Vector3(0.25f, 0, 0) - Rot24.Apply(br, beamDef.Sockets[bs].At), Rot = Rot24.Quat(br) });
        var joinTurned = false;
        var mergedSame = false;
        if (ja != null && jb != null)
        {
            jb.PartBounds(0, out var bmn, out var bmx);
            var before = jb.ToWorld((bmn + bmx) * 0.5f);
            var half = (bmx - bmn) * 0.5f;
            var worldHalf = Vector3.Abs(Vector3.Transform(half, jb.Rot));
            joinTurned = ConstructPlace.FindJoin(jw, ja, 0, out var jp) && jp.Turn != 0;
            if (joinTurned && jw.Merge(ja, jp) && ja.PartCount == 2)
            {
                ja.PartBounds(1, out var amn, out var amx);
                var after = ja.ToWorld((amn + amx) * 0.5f);
                mergedSame = Vector3.Distance(before, after) < 0.011f && Vector3.Distance(Vector3.Abs(Vector3.Transform((amx - amn) * 0.5f, ja.Rot)), worldHalf) < 0.011f && ja.LinkCount >= 1 && jw.Get(jb.Id) == null;
            }
        }
        Line($"placement data: a Frame Beam turned on its side beside a block: they touch {joinTurned}; joined, the beam stays where it was {mergedSame}");
        if (!joinTurned || !mergedSame)
            Fail("construct placement: joining a construct that is turned to the other doesn't keep its parts in place");
    }

    /// <summary>Where part <paramref name="part"/>'s socket facing body direction <paramref name="dir"/> is (rest frame; its centre if it has none).</summary>
    private static Vector3 FaceTowards(Construct c, int part, byte dir)
    {
        for (var s = 0; s < c.DefOf(part).Sockets.Length; s++)
        {
            c.SocketAt(part, s, out var at, out var d);
            if (d == dir)
                return at;
        }
        c.PartBounds(part, out var mn, out var mx);
        return (mn + mx) * 0.5f;
    }

    /// <summary>An open, level spot on the yard (no construct within 6 m, the ground the same height over 4 m).</summary>
    private static Vector3? OpenSpot(ConstructWorld w)
    {
        for (var x = -26f; x <= 26f; x += 2f)
        for (var z = -26f; z <= 26f; z += 2f)
        {
            var g0 = ConstructWorld.Ground(World.Query, x, z);
            var flat = true;
            for (var dx = -2f; dx <= 2f && flat; dx += 1f)
            for (var dz = -2f; dz <= 4f && flat; dz += 1f)
                flat = MathF.Abs(ConstructWorld.Ground(World.Query, x + dx, z + dz).Y - g0.Y) < 0.002f;
            if (!flat)
                continue;
            var clear = true;
            foreach (var c in w.Bodies)
                if (Vector3.Distance(c.WorldCom with { Y = 0 }, new Vector3(x, 0, z)) < 6f + c.Radius)
                    clear = false;
            if (World.Map.Sandbox is { } s && Vector3.Distance(s.Rack, new Vector3(x, 0, z)) < 6f)
                clear = false;
            if (clear)
                return g0;
        }
        return null;
    }

    private static void PlacementOffline(Map yard, SandboxMap sb)
    {
        yard.RestoreProps();
        World.SetMap(yard);
        sb.DebrisSeconds = 0;
        var now = 0.0;
        using var g = ModeGame(0, GameMode.Sandbox, yard.Id);
        Run(g, ref now, Game.CountdownSeconds + 0.3);
        g.RequestRespawn();
        Run(g, ref now, 1.0);
        if (g.Local is not { Alive: true } me)
        {
            Fail("construct placement: no local player in the Sandbox game");
            return;
        }
        var problems = new List<string>();
        void Want(bool ok, string what)
        {
            if (!ok)
                problems.Add(what);
        }
        ref var hand = ref g.SandboxHands.Hand(me.Id);
        var w = g.Constructs;
        var core = ConstructParts.Need("Construct Core");
        var block = ConstructParts.Need("Frame Block");
        if (OpenSpot(w) is not { } spot)
        {
            Fail("construct placement: no open level ground on the yard");
            return;
        }
        // Stand 2.5 m south of the spot, looking north.
        me.Teleport(spot + new Vector3(0, 0.05f, 2.5f));
        me.Yaw = 0;
        Run(g, ref now, 0.3);
        g.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.Welder);
        string Place(Vector3 at, byte turn = 0, byte flags = ConstructPlace.FlagGrid, Vector3? normal = null)
        {
            g.SandboxSay("");
            g.SandboxSend(CActKind.PlaceNew, at: at, yaw: 0, normal: normal ?? Vector3.UnitY, variant: turn, flags: flags);
            return g.SandboxNotice;
        }
        Construct? Newest(int before) => w.Bodies.Count > before ? w.Bodies[^1] : null;

        // 1. The core on open ground: a new construct of one part, the player's, with its socket grid.
        PlaceHold(g, me.Id, core);
        var n0 = w.Bodies.Count;
        var why1 = Place(spot);
        Run(g, ref now, core.AttachSeconds + 0.2);
        var a = Newest(n0);
        Span<ConstructGhost> ghosts = stackalloc ConstructGhost[ConstructBuild.MaxGhosts];
        var ghostN = a == null ? 0 : ConstructBuild.Ghosts(a, block, ghosts, Vector3.Zero, 2f);
        var onGrid = a != null && MathF.Abs(a.Pos.X / ConstructPlace.GridStep - MathF.Round(a.Pos.X / ConstructPlace.GridStep)) < 1e-3f;
        Line($"placement: the core set down {(a != null ? $"as construct {a.Id}: {a.PartCount} part, owner {a.Owner} (the player {me.Id}), anchored {a.Anchored}, loose {a.Loose}, on the grid {onGrid}, {ghostN} ghost sockets for a Frame Block" : $"no (\"{why1}\")")}");
        Want(a is { PartCount: 1, Anchored: false, Loose: false } && a.Owner == me.Id && ghostN == 6 && onGrid && hand.Held == ConstructParts.None, "the core on open ground is a new one-part construct of the player's with six sockets to build on");
        if (a == null)
        {
            Fail("construct placement: " + string.Join("; ", problems));
            return;
        }
        Run(g, ref now, 1.5); // (settles and sleeps)

        // 2. A second part attaches to it (its +x face).
        PlaceHold(g, me.Id, block);
        var plusX = -1;
        ghostN = ConstructBuild.Ghosts(a, block, ghosts, Vector3.Zero, 2f);
        for (var k = 0; k < ghostN; k++)
            if (a.DefOf(ghosts[k].Part).Sockets[ghosts[k].Socket].Name == "+x")
                plusX = k;
        if (plusX >= 0)
        {
            g.SandboxSay("");
            g.SandboxSend(CActKind.Attach, body: a.Id, part: (byte)ghosts[plusX].Part, socket: (byte)ghosts[plusX].Socket, variant: (byte)ghosts[plusX].Variant);
        }
        var why2 = g.SandboxNotice;
        Run(g, ref now, block.AttachSeconds + 0.2);
        Line($"placement: a Frame Block on the core's +x socket: {a.PartCount} parts, {a.LinkCount} weld(s) (\"{why2}\")");
        Want(plusX >= 0 && a.PartCount == 2 && a.LinkCount == 1, "a second part attaches to the new construct");
        Run(g, ref now, 1.5);

        // 3. Refused with a reason: over the construct, over a player, in the air, outside creative with the anchor.
        PlaceHold(g, me.Id, block);
        var over = Place(spot);
        var onMe = Place(me.Position + new Vector3(0, 0.02f, 0), flags: 0);
        var air = Place(spot + new Vector3(0, 2.5f, -1.5f), flags: 0);
        sb.InfiniteParts = false;
        g.SandboxInventory(me.Id).Add(block.Id);
        var anchorSurvival = Place(spot + new Vector3(-2f, 0, 0), flags: ConstructPlace.FlagAnchor | ConstructPlace.FlagGrid);
        sb.InfiniteParts = true;
        Run(g, ref now, 0.1);
        Line($"placement refused: over the construct \"{over}\"; on the player \"{onMe}\"; in the air \"{air}\"; anchored outside creative \"{anchorSurvival}\"");
        Want(over.Contains("another construct") && onMe.Contains("standing there") && air.Contains("no surface") && anchorSurvival.Contains("creative"), "overlapping or unsupported placements are refused with a reason");
        g.SandboxSend(CActKind.Cancel);

        // 4. Free vs anchored: a free block, a pinned block and a foundation, each pushed sideways.
        Construct? PlaceAndWait(ConstructPartDef def, Vector3 at, byte flags)
        {
            if (def.Tool != g.SandboxHands.Hand(me.Id).Tool)
                g.SandboxSend(CActKind.Tool, part: (byte)def.Tool);
            PlaceHold(g, me.Id, def);
            var before = w.Bodies.Count;
            var why = Place(at, flags: flags);
            Run(g, ref now, def.AttachSeconds + 0.2);
            if (why.Length > 0)
                problems.Add($"placing a {def.Name}: {why}");
            return Newest(before);
        }
        var free = PlaceAndWait(block, spot + new Vector3(-2f, 0, 0), ConstructPlace.FlagGrid);
        var pinned = PlaceAndWait(block, spot + new Vector3(-2f, 0, -1f), ConstructPlace.FlagGrid | ConstructPlace.FlagAnchor);
        var found = PlaceAndWait(ConstructParts.Need("Foundation"), spot + new Vector3(2.5f, 0, 0), ConstructPlace.FlagGrid);
        g.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.Welder);
        Run(g, ref now, 1.0);
        var rest = free?.Pos ?? default;
        var groundY = ConstructWorld.Ground(World.Query, rest.X, rest.Z).Y;
        var restsOn = free != null && MathF.Abs(free.Pos.Y + free.LocalMin.Y - groundY) < 0.03f;
        Vector3 P(Construct? c) => c?.Pos ?? default;
        var p0 = (P(free), P(pinned), P(found));
        foreach (var c in new[] { free, pinned, found })
            if (c != null)
            {
                c.Vel = new Vector3(-3f, 0, 0);
                c.Wake();
            }
        Run(g, ref now, 0.6);
        var moved = (Vector3.Distance(P(free), p0.Item1), Vector3.Distance(P(pinned), p0.Item2), Vector3.Distance(P(found), p0.Item3));
        Line($"placement: free / pinned / foundation: anchored {free?.Anchored} / {pinned?.Anchored} (pinned {pinned?.Pinned}) / {found?.Anchored}; the free one rests on the ground {restsOn}; pushed they moved {moved.Item1:0.00} / {moved.Item2:0.00} / {moved.Item3:0.00} m");
        Want(free is { Anchored: false } && pinned is { Anchored: true, Pinned: true } && found is { Anchored: true, Pinned: false } && restsOn
             && moved.Item1 > 0.3f && moved.Item2 < 0.001f && moved.Item3 < 0.001f, "a free placement moves by physics, a pinned one or a foundation stays put");

        // 5. Building onto the map's loose part claims it.
        var scratch = new ConstructGhost[ConstructBuild.MaxGhosts];
        var loose = w.Bodies.FirstOrDefault(c => c.Loose && c.Owner == 255 && c.DefOf(0).Tool == ConstructTool.Welder && ConstructBuild.Ghosts(c, block, scratch, Vector3.Zero, 3f) > 0);
        if (loose != null)
        {
            StandBy(me, loose.WorldCom, 2.0f);
            Run(g, ref now, 0.3);
            PlaceHold(g, me.Id, block);
            var gn = ConstructBuild.Ghosts(loose, block, ghosts, Vector3.Zero, 3f);
            var pick = 0;
            for (var k = 0; k < gn; k++)
                if (ConstructBuild.Place(loose, ghosts[k].Part, ghosts[k].Socket, block, ghosts[k].Variant / 4, ghosts[k].Variant % 4, out var gg) == null && gg.Pos.Y > 0.1f)
                    pick = k; // (on top, clear of the ground)
            g.SandboxSay("");
            g.SandboxSend(CActKind.Attach, body: loose.Id, part: (byte)ghosts[pick].Part, socket: (byte)ghosts[pick].Socket, variant: (byte)ghosts[pick].Variant);
            var whyLoose = g.SandboxNotice;
            Run(g, ref now, block.AttachSeconds + 0.2);
            Line($"placement: building onto the map's loose {loose.DefOf(0).Name}: {loose.PartCount} parts, owner {loose.Owner}, loose {loose.Loose} (\"{whyLoose}\")");
            Want(loose.PartCount == 2 && loose.Owner == me.Id && !loose.Loose, "building onto a loose part makes it the builder's construct");
        }
        else
            problems.Add("the yard has no loose welded part to build onto");

        // 6. Joining: a block set down beside the first construct's block (their faces meet) is welded into it.
        me.Teleport(spot + new Vector3(0, 0.05f, 2.5f));
        me.Yaw = 0;
        Run(g, ref now, 0.3);
        a.Wake();
        Run(g, ref now, 1.0);
        var blockOnA = 1;
        var faceAt = FaceTowards(a, blockOnA, 0);
        var beside = a.ToWorld(faceAt) + new Vector3(0.25f, 0, 0);
        PlaceHold(g, me.Id, block);
        var nb = w.Bodies.Count;
        var whyB = Place(beside with { Y = spot.Y }, flags: 0);
        Run(g, ref now, block.AttachSeconds + 0.2);
        var b = Newest(nb);
        Run(g, ref now, 1.0);
        var canJoin = ConstructPlace.FindJoin(w, a, blockOnA, out var plan) && plan.Other == b;
        var aParts = a.PartCount;
        var bId = b?.Id ?? 0;
        g.SandboxSay("");
        g.SandboxSend(CActKind.Join, body: a.Id, part: (byte)blockOnA);
        var whyJoin = g.SandboxNotice;
        Run(g, ref now, 0.3);
        var reached = 0;
        if (w.Get(a.Id) is { } joined)
        {
            // Every part reaches the root through the welds.
            Span<bool> seen = stackalloc bool[Construct.MaxParts];
            seen[joined.Root] = true;
            reached = 1;
            for (var changed = true; changed;)
            {
                changed = false;
                for (var k = 0; k < joined.LinkCount; k++)
                {
                    var l = joined.Links[k];
                    if (seen[l.A] != seen[l.B])
                    {
                        seen[l.A] = seen[l.B] = true;
                        reached++;
                        changed = true;
                    }
                }
            }
        }
        Line($"placement: join: a block set down beside it {(b != null ? $"as construct {b.Id}" : $"refused (\"{whyB}\")")}, found to touch {canJoin}; joined (\"{whyJoin}\"): {aParts} + 1 -> {a.PartCount} parts, {reached} reach the root, construct {bId} gone {w.Get(bId) == null}, owner {a.Owner}");
        Want(b != null && canJoin && a.PartCount == aParts + 1 && reached == a.PartCount && w.Get(bId) == null && a.Owner == me.Id, "a part touching another construct joins the two into one");

        // 7. The per-player limit.
        var filler = new List<Construct>();
        while (ConstructPlace.Owned(w, me.Id) < ConstructPlace.MaxPerPlayer && ConstructBuild.PlaceNew(w, core, spot + new Vector3(-20, 30, filler.Count * 2f), 0) is { } f)
        {
            f.Owner = me.Id;
            f.Pinned = true;
            f.Rebuild();
            filler.Add(f);
        }
        PlaceHold(g, me.Id, block);
        var limit = Place(spot + new Vector3(-1f, 0, 1.2f), flags: 0);
        foreach (var f in filler)
            w.Remove(f);
        Line($"placement: at {ConstructPlace.MaxPerPlayer} constructs a new one is refused: \"{limit}\"");
        Want(limit.Contains(ConstructPlace.MaxPerPlayerText), "a player's constructs are limited");
        g.SandboxSend(CActKind.Cancel);

        // 8. The placement checks allocate nothing (the client runs them every frame).
        Span<Vector3> feet = stackalloc Vector3[2];
        feet[0] = me.Position;
        feet[1] = me.Position + Vector3.UnitX * 5;
        void Work(Span<Vector3> f2)
        {
            for (var k = 0; k < 50; k++)
            {
                ConstructPlace.Pose(World.Query, block, spot + new Vector3(0.1f * k, 0, -1.2f), Vector3.UnitY, 0.01f * k, (byte)(k % 24), (k & 1) == 0, out var fp);
                ConstructPlace.Fits(w, World.Query, block, fp, f2);
                ConstructPlace.FindJoin(w, a, 0, out _);
                ConstructPlace.LimitRefusal(w, me.Id);
            }
        }
        Work(feet);
        var least = long.MaxValue;
        for (var round = 0; round < 3; round++)
        {
            var b0 = GC.GetAllocatedBytesForCurrentThread();
            Work(feet);
            least = Math.Min(least, GC.GetAllocatedBytesForCurrentThread() - b0);
        }
        Line($"placement: 50 poses, fit checks, join searches and limit checks allocate {least} bytes (least of three)");
        Want(least == 0, $"the placement checks allocate ({least} bytes)");

        if (problems.Count > 0)
            Fail("construct placement: " + string.Join("; ", problems));
    }

    /// <summary>A real host and a client: the client's core, pinned, then a block on it and a block beside it joined: its copy shows each step.</summary>
    /// <summary>
    /// A part into the hands for free placement: an item from the parts menu, as a player asks for it; a physics-only one
    /// (decision 21: welded, so never an item: the menu spawns it as a body) set in the server's hands directly, since
    /// the placement rules these tests check still apply to whatever is held.
    /// </summary>
    private static void PlaceHold(Game server, byte player, ConstructPartDef def, Game? client = null)
    {
        if (def.PhysicsOnly)
            server.SandboxHands.Hand(player).Held = def.Id;
        else
            (client ?? server).SandboxTakePart(def.Id);
    }

    private static void PlacementOverWire(Map yard, SandboxMap sb)
    {
        const ushort port = 26351;
        var clock = Stopwatch.StartNew();
        var now = 0.0;
        yard.RestoreProps();
        World.SetMap(yard);
        sb.DebrisSeconds = 0;
        using var server = new Game(NetPeer.Host(port, 4), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: yard.Id) { MapVoteEnabled = false };
        server.Mode = GameMode.Sandbox;
        using var a = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Alpha", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        Pump(server, [a], 1.0, ref now, clock);
        for (var wait = 0; wait < 40 && server.Phase != MatchPhase.Playing; wait++)
            PumpFast(server, [a], 0.25, ref now);
        Pump(server, [a], 0.3, ref now, clock);
        if (a.LocalId == byte.MaxValue || !server.Players.TryGetValue(a.LocalId, out var sp))
        {
            Fail("construct placement over the wire: the client didn't join");
            return;
        }
        if (!sp.Alive)
            server.SelfTestRespawn(sp, now);
        Pump(server, [a], 0.3, ref now, clock);
        var w = server.Constructs;
        if (OpenSpot(w) is not { } spot)
        {
            Fail("construct placement over the wire: no open level ground on the yard");
            return;
        }
        sp.Teleport(spot + new Vector3(0, 0.05f, 2.5f));
        Pump(server, [a], 0.3, ref now, clock);
        var core = ConstructParts.Need("Construct Core");
        var block = ConstructParts.Need("Frame Block");
        a.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.Welder);
        PlaceHold(server, a.LocalId, core, a);
        Pump(server, [a], 0.2, ref now, clock);
        var n0 = w.Bodies.Count;
        a.SandboxSend(CActKind.PlaceNew, at: spot, yaw: 0, normal: Vector3.UnitY, variant: 0, flags: ConstructPlace.FlagGrid | ConstructPlace.FlagAnchor);
        Pump(server, [a], core.AttachSeconds + 0.5, ref now, clock);
        var c = w.Bodies.Count > n0 ? w.Bodies[^1] : null;
        var copy = c == null ? null : a.Constructs.Get(c.Id);
        var placedOk = c != null && copy is { PartCount: 1, Pinned: true, Anchored: true } && copy.Owner == a.LocalId && copy.DefOf(0).Core;
        // A block on it, then one beside that, joined.
        PlaceHold(server, a.LocalId, block, a);
        Pump(server, [a], 0.2, ref now, clock);
        var joinedOk = false;
        var b = (Construct?)null;
        if (c != null)
        {
            Span<ConstructGhost> ghosts = stackalloc ConstructGhost[ConstructBuild.MaxGhosts];
            var gn = ConstructBuild.Ghosts(c, block, ghosts, Vector3.Zero, 2f);
            for (var k = 0; k < gn; k++)
                if (c.DefOf(ghosts[k].Part).Sockets[ghosts[k].Socket].Name == "+x")
                    a.SandboxSend(CActKind.Attach, body: c.Id, part: (byte)ghosts[k].Part, socket: (byte)ghosts[k].Socket, variant: (byte)ghosts[k].Variant);
            Pump(server, [a], block.AttachSeconds + 0.5, ref now, clock);
            if (c.PartCount == 2)
            {
                var faceAt = FaceTowards(c, 1, 0);
                var beside = c.ToWorld(faceAt) + new Vector3(0.25f, 0, 0);
                PlaceHold(server, a.LocalId, block, a);
                Pump(server, [a], 0.2, ref now, clock);
                var nb = w.Bodies.Count;
                a.SandboxSend(CActKind.PlaceNew, at: beside with { Y = spot.Y }, yaw: 0, normal: Vector3.UnitY, variant: 0, flags: ConstructPlace.FlagAnchor);
                Pump(server, [a], block.AttachSeconds + 0.5, ref now, clock);
                b = w.Bodies.Count > nb ? w.Bodies[^1] : null;
                var sawB = b != null && a.Constructs.Get(b.Id) != null;
                a.SandboxSend(CActKind.Join, body: c.Id, part: 1);
                Pump(server, [a], 0.5, ref now, clock);
                joinedOk = sawB && b != null && w.Get(b.Id) == null && a.Constructs.Get(b.Id) == null && a.Constructs.Get(c.Id) is { PartCount: 3 } && c.PartCount == 3;
            }
        }
        Line($"placement over the wire: the client's pinned core {(c != null ? $"construct {c.Id}" : "missing")} on its copy: {(copy != null ? $"{copy.PartCount} part, pinned {copy.Pinned}, anchored {copy.Anchored}, owner {copy.Owner} (client {a.LocalId})" : "missing")}; "
             + $"a block on it and one beside it joined: server {c?.PartCount} parts, the client's copy {(c != null ? a.Constructs.Get(c.Id)?.PartCount : null)}, the other gone everywhere {joinedOk} (last notice \"{a.SandboxNotice}\")");
        if (!placedOk)
            Fail("construct placement over the wire: the client's new construct (pinned, its owner) isn't mirrored");
        if (!joinedOk)
            Fail("construct placement over the wire: joining two constructs doesn't show on the client");
    }
}
