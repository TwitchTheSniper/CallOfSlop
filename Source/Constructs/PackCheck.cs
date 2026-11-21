using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// <c>--pack-check</c>: welded groups on a Large Lay-down Pad, headless, in a world of its own (PackedGroups.cs): a group set down on the
/// pad goes in as one packed kind; the same group built in another order and turned a quarter goes in as the same kind; one taken out
/// becomes its group again, welded, painted and placed as before; a damaged group and a wired one are refused with their reasons; the
/// design is kept for the save. Exit code: the failures.
/// </summary>
public static class PackCheck
{
    public static int Cli(string[] args)
    {
        var fails = 0;
        void Fail(string why)
        {
            fails++;
            Console.WriteLine("FAIL " + why);
        }
        var world = new ConstructWorld();
        var pad = world.Create(new Vector3(0, 0, 0), Quaternion.Identity)!;
        world.AddPart(pad, ConstructParts.Need("Large Lay-down Pad"), Vector3.Zero, 0);
        pad.Anchored = pad.Pinned = true;
        pad.Rebuild();
        var st = world.StoreOf(pad, 0)!;

        Construct Group(bool reverse, Quaternion rot, bool damage = false)
        {
            var g = world.Create(new Vector3(0, 5, 0), rot)!;
            (string Name, Vector3 At, uint Paint)[] parts =
            [
                ("Frame Beam", Vector3.Zero, 0),
                ("Frame Block", new Vector3(0, 0, 1.25f), ConstructPaint.Rgb(200, 40, 30)),
                ("Frame Block", new Vector3(0.5f, 0, 0.75f), 0),
            ];
            if (reverse)
                Array.Reverse(parts);
            foreach (var (n, at, paint) in parts)
            {
                var i = world.AddPart(g, ConstructParts.Need(n), at, 0);
                g.Parts[i].Paint = paint;
            }
            world.AutoLinkMany(g, 0, g.PartCount);
            if (damage)
                g.Parts[0].Health *= 0.5f;
            g.Rebuild();
            Lay(g);
            return g;
        }

        // Sets a group down over the middle of the pad's hold, its lowest corner 2 cm over the hold's floor, still.
        void Lay(Construct g)
        {
            var d = pad.DefOf(0);
            Freeform.PartWorld(pad, 0, out var pp, out var pr);
            var mid = pp + Vector3.Transform(((d.RackHoldMin + d.RackHoldMax) * 0.5f) with { Y = d.RackHoldMin.Y }, pr);
            var low = float.MaxValue;
            for (var i = 0; i < g.PartCount; i++)
            {
                Freeform.PartWorld(g, i, out var at, out var rot);
                var pd = g.DefOf(i);
                for (var b = 0; b < pd.SolidBoxes; b++)
                    for (var k = 0; k < 8; k++)
                    {
                        var bx = pd.Boxes[b];
                        var corner = bx.Center + new Vector3((k & 1) != 0 ? bx.Half.X : -bx.Half.X, (k & 2) != 0 ? bx.Half.Y : -bx.Half.Y, (k & 4) != 0 ? bx.Half.Z : -bx.Half.Z);
                        low = MathF.Min(low, (at + Vector3.Transform(corner, rot)).Y);
                    }
            }
            var shift = new Vector3(mid.X - g.WorldCom.X, mid.Y + 0.02f - low, mid.Z - g.WorldCom.Z);
            g.Pos += shift;
            g.Vel = g.AngVel = Vector3.Zero;
        }

        var g1 = Group(false, Quaternion.Identity);
        var before = Parts(g1);
        var links = g1.LinkCount;
        Console.WriteLine($"group 1: {g1.PartCount} parts, {links} welds; on the pad: {ConstructLifters.PadWhy(world, pad, 0, g1) ?? "taken"}");
        if (ConstructLifters.PadTakeInNow(world, pad, 0) != 1)
            Fail($"group 1 wasn't taken in: {ConstructLifters.PadWhy(world, pad, 0, g1)}");
        var g2 = Group(true, Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2));
        Console.WriteLine($"group 2 (built the other way round, a quarter turn): {ConstructLifters.PadWhy(world, pad, 0, g2) ?? "taken"}");
        if (ConstructLifters.PadTakeInNow(world, pad, 0) != 1)
            Fail($"group 2 wasn't taken in: {ConstructLifters.PadWhy(world, pad, 0, g2)}");
        Console.WriteLine($"pad: {st.KindN} kind(s), {st.ItemCount} piece(s){(st.KindN > 0 ? $": {ConstructParts.Def(st.KindDef![0]).Name} x{st.KindCount![0]}" : "")}");
        if (st.KindN != 1 || st.ItemCount != 2)
            Fail("the two groups should be one kind, two pieces");
        if (world.Get(g1.Id) != null || world.Get(g2.Id) != null)
            Fail("a group taken in is still in the world");

        var bad = Group(false, Quaternion.Identity, damage: true);
        var why = ConstructLifters.PadWhy(world, pad, 0, bad);
        Console.WriteLine($"a damaged group: {why ?? "taken"}");
        if (why == null || !why.Contains("damaged"))
            Fail("a damaged group should be refused as damaged");
        world.Remove(bad, quiet: true);

        if (st.KindN > 0)
        {
            var pd = ConstructParts.Def(st.KindDef![0]);
            BakedParts.NoteInUse(world);
            if (!world.Baked.Contains(BakedParts.DesignOf(pd)!.Hash))
                Fail("the packed design isn't kept for the save");
            if (!world.StationTake(pad, 0, st, pd))
                Fail("couldn't take a piece off the pad");
            var out1 = world.Create(new Vector3(20, 1, 0), Quaternion.Identity)!;
            world.AddPart(out1, pd, Vector3.Zero, 0);
            out1.Rebuild();
            ConstructLifters.Scan(world, server: true);
            ConstructLifters.UnpackNow(world);
            var back = world.Bodies.FirstOrDefault(b => b.Id != pad.Id && b.PartCount > 1);
            if (back == null)
                Fail($"the piece taken out wasn't unpacked: {BakedParts.LastRefusal}");
            else
            {
                var after = Parts(back);
                Console.WriteLine($"taken out: {back.PartCount} parts, {back.LinkCount} welds");
                if (!before.SequenceEqual(after))
                {
                    Fail("the unpacked group differs:");
                    foreach (var l in before)
                        Console.WriteLine("  before " + l);
                    foreach (var l in after)
                        Console.WriteLine("  after  " + l);
                }
                if (back.LinkCount != links)
                    Fail($"{links} welds before, {back.LinkCount} after");
            }
        }
        Console.WriteLine(fails == 0 ? "pack check: passed" : $"pack check: {fails} failure(s)");
        return fails;
    }

    /// <summary>Every part relative to the group's first Frame Beam, in its frame: name, place, turn and paint, sorted.</summary>
    private static List<string> Parts(Construct c)
    {
        var b = Array.FindIndex(c.Parts, 0, c.PartCount, p => ConstructParts.Def(p.Def).Name == "Frame Beam");
        var o = c.Parts[b].Pos;
        var inv = Rot24.Inverse(c.Parts[b].Rot);
        var l = new List<string>();
        for (var i = 0; i < c.PartCount; i++)
        {
            ref var p = ref c.Parts[i];
            var at = Rot24.Apply(inv, p.Pos - o);
            l.Add($"{c.DefOf(i).Name} {at.X:0.00},{at.Y:0.00},{at.Z:0.00} turn {Rot24.Compose(inv, p.Rot)} paint {p.Paint:x}");
        }
        l.Sort(StringComparer.Ordinal);
        return l;
    }
}

public static partial class ConstructLifters
{
    /// <summary>(--pack-check) Why pad <paramref name="part"/> won't take <paramref name="lo"/> now, or null.</summary>
    internal static string? PadWhy(ConstructWorld world, Construct c, int part, Construct lo)
    {
        Freeform.PartWorld(c, part, out var pp, out var pr);
        return TakeInWhy(world, c, part, world.StoreOf(c, part)!, pp, Quaternion.Conjugate(pr), c.Radius + Vector3.Distance(c.Pos, pp) + 2f, lo);
    }

    internal static int PadTakeInNow(ConstructWorld world, Construct c, int part) => PadTakeIn(world, c, part, PadTakesAStep);

    internal static void UnpackNow(ConstructWorld world) => UnpackWaiting(world);
}
