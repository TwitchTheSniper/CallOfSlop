using System.Numerics;
using CallOfSlop.Space;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// "Gravity parts" (phase 5, docs/space/GRAVITY.md "Gravity parts"): the Gravity Well pulls a loose block to it when on
    /// (F), not when off, pushes it on a negative signal (a lever wired in), never pulls its own construct, and is in the
    /// bubble's gravity for players; the Gravity Generator, turned on its side, makes a block in its box fall sideways
    /// towards its base while one outside falls as usual, turns "up" in its box for players, never pushes its own construct,
    /// and lets go when switched off; wells on ships in space pull the other ships but not their own; the refresh and the
    /// queries allocate nothing.
    /// </summary>
    private static void CheckGravityParts()
    {
        if (Maps.Find("Physics Test") is not { } map)
        {
            Fail("gravity parts: the Physics Test map is missing");
            return;
        }
        map.RestoreProps();
        World.SetMap(map);
        try
        {
            if (LsFlat(20) is not { } g)
            {
                Fail("gravity parts: no flat ground on the Physics Test map");
                return;
            }
            GpWell(g);
            GpGenerator(g);
            GpSpaceWells();
        }
        finally
        {
            ConstructGravity.Clear();
            GravityFrames.Use(map.Gravity);
        }
    }

    /// <summary>A pinned well and a loose block 6 m from it, in the air: how far the block moves towards the well in 0.5 s.</summary>
    private static float GpWellPull(Vector3 g, int mode, out ConstructWorld w)
    {
        w = new ConstructWorld();
        var well = LsBase(w, ConstructParts.Need("Gravity Well"), g + new Vector3(0, 4f, 0), pinned: true);
        if (mode == 1)
            well.Parts[0].State = 1; // (F: on)
        else if (mode == 2)
        {
            var lever = LsBolt(w, well, 0, ConstructParts.Need("Lever"), new Vector3(0.7f, 0, 0));
            w.AddWire(well, lever, 0);
            well.Parts[lever].State = 3; // (back: -1, push)
            well.Rebuild();
        }
        var block = LsBase(w, ConstructParts.Need("Frame Block"), g + new Vector3(6f, 4f, 0), pinned: false);
        var x0 = block.WorldCom.X;
        LsTicks(w, 30);
        return x0 - block.WorldCom.X;
    }

    private static void GpWell(Vector3 g)
    {
        var off = GpWellPull(g, 0, out _);
        var on = GpWellPull(g, 1, out var w);
        Line($"  gravity parts: a block 6 m from a well moved {on:0.000} m towards it in 0.5 s (off: {off:0.000} m)");
        if (on < 0.2f || MathF.Abs(off) > 0.02f)
            Fail($"gravity parts: a well that is on should pull a loose block to it ({on:0.000} m in 0.5 s; off: {off:0.000} m)");
        if (ConstructGravity.Wells != 1 || GravityFrames.HookCount != 1)
            Fail($"gravity parts: a working well should be in the bubble's gravity ({ConstructGravity.Wells} wells, {GravityFrames.HookCount} hooks)");
        else
        {
            var at = g + new Vector3(0, 4.3f, 3f); // (beside the well: a player's pull leans towards it)
            var pull = GravityFrames.GravityAt(at) - new Vector3(0, -World.Gravity * World.GravityScale, 0);
            if (pull.Z > -0.5f)
                Fail($"gravity parts: a player beside a well should be pulled towards it (extra pull {pull})");
        }
        var push = GpWellPull(g, 2, out _);
        if (push > -0.2f)
            Fail($"gravity parts: a well on a negative signal should push a block away ({push:0.000} m towards it)");

        // Its own construct: a free body with a well bolted to one side falls straight down.
        var w2 = new ConstructWorld();
        var body = LsBase(w2, ConstructParts.Need("Frame Block"), g + new Vector3(0, 6f, 0), pinned: false);
        var wi = LsBolt(w2, body, 0, ConstructParts.Need("Gravity Well"), new Vector3(1.2f, 0, 0));
        body.Parts[wi].State = 1;
        var c0 = body.WorldCom;
        LsTicks(w2, 30);
        var side = new Vector2(body.WorldCom.X - c0.X, body.WorldCom.Z - c0.Z).Length();
        if (side > 0.01f || c0.Y - body.WorldCom.Y < 0.5f)
            Fail($"gravity parts: a well shouldn't pull its own construct (drifted {side:0.000} m sideways, fell {c0.Y - body.WorldCom.Y:0.00} m)");

        // Off again: the hook goes.
        foreach (var c in w.Bodies)
            if (c.GravityCount > 0)
                c.Parts[0].State = 0;
        LsTicks(w, 2);
        if (GravityFrames.HookCount != 0)
            Fail("gravity parts: a well switched off should leave the bubble's gravity");
    }

    private static void GpGenerator(Vector3 g)
    {
        // A generator on its side (its -Y along world +X): its box reaches 0.15 to 3.15 m along world -X from it.
        var w = new ConstructWorld();
        var rot = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2); // (+Y to -X)
        var gen = w.Create(g + new Vector3(0, 6f, 0), rot)!;
        w.AddPart(gen, ConstructParts.Need("Gravity Generator"), Vector3.Zero, 0);
        gen.Pinned = true;
        gen.Rebuild();
        gen.Parts[0].State = 1;
        LsTicks(w, 2); // (on: its output, then its box, before anything is in it)
        var inside = LsBase(w, ConstructParts.Need("Frame Block"), g + new Vector3(-2f, 6f, 0), pinned: false);
        var outside = LsBase(w, ConstructParts.Need("Frame Block"), g + new Vector3(-2f, 6f, 6f), pinned: false);
        var i0 = inside.WorldCom;
        var o0 = outside.WorldCom;
        LsTicks(w, 2);
        var probe = g + new Vector3(-1.5f, 6.5f, 0.5f);
        var up = GravityFrames.UpAt(probe);
        if (ConstructGravity.Generators != 1 || Vector3.Dot(up, -Vector3.UnitX) < 0.99f || GravityFrames.UpAt(g + new Vector3(-1.5f, 6.5f, 8f)) != Vector3.UnitY)
            Fail($"gravity parts: inside a generator's box up should be its own (-X here, is {up}), outside the map's");
        LsTicks(w, 18);
        var di = inside.WorldCom - i0;
        var dO = outside.WorldCom - o0;
        Line($"  gravity parts: a generator on its side: a block in its box moved {di}, one outside {dO} in 0.33 s");
        if (di.X < 0.2f || MathF.Abs(di.Y) > 0.05f)
            Fail($"gravity parts: a block in a sideways generator's box should fall towards its base (+X), not down (moved {di})");
        if (dO.Y > -0.2f || MathF.Abs(dO.X) > 0.02f)
            Fail($"gravity parts: a block outside the box should fall as usual (moved {dO})");

        // Its own construct: a free body with a sideways generator whose box holds the body's centre doesn't slide.
        var w2 = new ConstructWorld();
        var body = w2.Create(g + new Vector3(10f, 8f, 0), rot)!;
        w2.AddPart(body, ConstructParts.Need("Gravity Generator"), Vector3.Zero, 0);
        w2.AddPart(body, ConstructParts.Need("Frame Block"), new Vector3(0, 1.5f, 0), 0); // (inside its own box)
        w2.AddLink(body, 0, 0, 1, 0);
        body.Rebuild();
        body.Parts[0].State = 1;
        var b0 = body.WorldCom;
        LsTicks(w2, 20);
        if (MathF.Abs(body.WorldCom.X - b0.X) > 0.01f)
            Fail($"gravity parts: a generator shouldn't push its own construct ({body.WorldCom.X - b0.X:0.000} m along its down)");

        // Off: the box goes, up is the map's again.
        gen.Parts[0].State = 0;
        foreach (var c in w2.Bodies)
            c.Parts[0].State = 0;
        LsTicks(w, 2);
        LsTicks(w2, 2);
        if (GravityFrames.Volumes != null || GravityFrames.UpAt(probe) != Vector3.UnitY)
            Fail("gravity parts: a generator switched off should leave the bubble's gravity");

        // No allocation: a refresh with a well and a generator working, and the queries.
        gen.Parts[0].State = 1;
        LsTicks(w, 2);
        var acc = Vector3.Zero;
        var a0 = GC.GetAllocatedBytesForCurrentThread();
        for (var k = 0; k < 50; k++)
        {
            ConstructGravity.Refresh(w);
            acc += GravityFrames.GravityAt(probe) + GravityFrames.UpAt(probe) + ConstructGravity.GravityFor(inside, inside.WorldCom);
        }
        var alloc = GC.GetAllocatedBytesForCurrentThread() - a0;
        if (alloc != 0 || !float.IsFinite(acc.X))
            Fail($"gravity parts: 50 refreshes and queries allocated {alloc} B");
    }

    private static void GpSpaceWells()
    {
        // Two ships 60 m apart in the same orbit round a rock; a well on the first pulls the second, never the first.
        const double mu = 1.8e9, r = 30_000;
        var sys = RockSystem(mu);
        void Run(double wellMu, out Vec3d a, out Vec3d b)
        {
            var ships = new SpaceShips(sys, 2);
            var wells = new ConstructGravity.SpaceWells(ships);
            ships.Forces.Add(wells);
            var v = Math.Sqrt(mu / r);
            var s0 = ships.AddRelative(0, new Vec3d(r, 0, 0), new Vec3d(0, v, 0), ShipFlags.NoRails);
            var s1 = ships.AddRelative(0, new Vec3d(r, 0, 60), new Vec3d(0, v, 0), ShipFlags.NoRails);
            for (var k = 1; k <= 120; k++) // 2 s of ticks
            {
                wells.Refresh(); // (no construct wells: then the test's own)
                if (wellMu != 0)
                    wells.Add(s0, wellMu, 200);
                ships.Advance(new SpaceTime(SpaceTime.TickNs(k)));
            }
            a = ships.PositionOf(s0) - SpacePos.Origin;
            b = ships.PositionOf(s1) - SpacePos.Origin;
        }
        Run(0, out var a0, out var b0);
        Run(9.81 * 16, out var a1, out var b1);
        var gap0 = (b0 - a0).Length;
        var gap1 = (b1 - a1).Length;
        Line($"  gravity parts: two ships 60 m apart in orbit, 2 s: gap {gap0:0.000} m without a well, {gap1:0.000} m with one on the first; the first moved {(a1 - a0).Length * 1000:0.000} mm");
        if (gap0 - gap1 < 0.03)
            Fail($"gravity parts: a well on a ship in space should pull the ship next to it (gap {gap0:0.000} -> {gap1:0.000} m)");
        if ((a1 - a0).Length > 1e-6)
            Fail($"gravity parts: a well shouldn't pull its own ship ({(a1 - a0).Length:g3} m off)");
    }
}
