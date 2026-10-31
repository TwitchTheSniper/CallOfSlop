using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// "Joint sides" (board item dev-joint-side-indicators-js01; docs/sandbox/CONSTRUCTS.md, "Joint sides"): every joint part (hinges,
    /// motor hinges, turntables, pistons, the steering pivot, the lid hinge, every actuator size) tags boxes "base" and "leaf", the
    /// leaf boxes are drawn in the moving-side colour (not paintable, details only) and differ from the base ones, a joint file with
    /// a side missing is refused; the leaf socket's marker side and colour differ from every base socket's; the ghost line names
    /// the sides; and the look-at counts the parts on each side for a scripted hinged door (Gate) and a turntable turret, again
    /// after a part is removed. Counting, the side of a part and a socket's side allocate nothing.
    /// </summary>
    private static void CheckJointSides()
    {
        var joints = ConstructParts.All.Where(d => d.HasJoint && d.LeafIndex >= 0).ToList();
        var bad = new List<string>();
        foreach (var d in joints)
        {
            var baseBoxes = d.Boxes.Where(b => b.IsBase).ToList();
            var leafBoxes = d.Boxes.Where(b => b.IsLeaf).ToList();
            if (baseBoxes.Count == 0 || leafBoxes.Count == 0)
                bad.Add($"{d.Name}: boxes tagged base {baseBoxes.Count}, leaf {leafBoxes.Count}");
            if (leafBoxes.Any(b => !b.Detail || b.Paintable))
                bad.Add($"{d.Name}: a leaf box is solid or paintable (the colour could be painted over)");
            // The moving side is visibly different: no base box shares a leaf box's colour, and the main (solid) box is a base one.
            if (leafBoxes.Any(l => baseBoxes.Any(b => b.Color == l.Color)))
                bad.Add($"{d.Name}: a base and a leaf box share a colour");
            if (d.Boxes.Take(d.SolidBoxes).Any(b => !b.IsBase))
                bad.Add($"{d.Name}: a solid box is not on the base side");
            // Markers: the leaf socket is a leaf-side marker, every other one a base-side marker.
            for (var s = 0; s < d.Sockets.Length; s++)
            {
                var side = JointSides.SocketSide(d, s);
                if (side != (s == d.LeafIndex ? JointSides.Leaf : JointSides.Base))
                    bad.Add($"{d.Name}: socket {d.Sockets[s].Name} has side {side}");
            }
            if (JointSides.Label(JointSides.SocketSide(d, d.LeafIndex)) == JointSides.Label(JointSides.SocketSide(d, d.LeafIndex == 0 ? 1 : 0)))
                bad.Add($"{d.Name}: leaf and base share a label");
        }
        if (JointSides.LeafColour == JointSides.BaseColour)
            bad.Add("the leaf and base marker colours are the same");
        // A part that is no joint has no side; the ghost box colour differs for a leaf box and a base one, valid or not.
        var wheel = ConstructParts.All.First(d => !d.HasJoint);
        if (JointSides.SocketSide(wheel, 0) != 0)
            bad.Add("a part with no joint has a side");
        var jd = joints[0];
        var lb = jd.Boxes.First(b => b.IsLeaf);
        var bb = jd.Boxes.First(b => b.IsBase);
        if (ConstructRender.GhostBoxColour(in lb, true) == ConstructRender.GhostBoxColour(in bb, true) || ConstructRender.GhostBoxColour(in lb, false) == ConstructRender.GhostBoxColour(in bb, false))
            bad.Add("the ghost draws a leaf box like a base one");
        // The ghost line says which side goes where.
        var hinge = ConstructParts.Need("Hinge");
        var frame = ConstructParts.Need("Frame Block");
        var onLeaf = JointSides.Ghost(hinge, hinge.LeafIndex, frame, 0) ?? "";
        var onBase = JointSides.Ghost(hinge, 0 == hinge.LeafIndex ? 1 : 0, frame, 0) ?? "";
        var own = JointSides.Ghost(frame, 0, hinge, hinge.LeafIndex) ?? "";
        if (!onLeaf.Contains("moving side") || !onBase.Contains("base") || onLeaf == onBase || !own.Contains("moving side"))
            bad.Add($"ghost lines: on the leaf \"{onLeaf}\", on the base \"{onBase}\", the hinge's own leaf \"{own}\"");
        if (JointSides.Ghost(frame, 0, frame, 0) != null)
            bad.Add("a ghost line for two parts that are no joints");
        // A joint file missing a side is refused.
        const string noSides = "{\"name\":\"Bad Hinge\",\"role\":\"hinge\",\"mass\":6,\"health\":100,\"boxes\":[{\"size\":[0.2,0.5,0.2]}],\"sockets\":[{\"name\":\"base\",\"at\":[-0.1,0,0],\"dir\":\"-x\"},{\"name\":\"leaf\",\"at\":[0.1,0,0],\"dir\":\"+x\",\"kind\":\"leaf\"}],\"hinge\":{\"axis\":\"+y\",\"min\":-90,\"max\":90,\"leaf\":\"leaf\"}}";
        try
        {
            ConstructParts.Parse(noSides, "Bad.json");
            bad.Add("a joint part with no tagged sides was accepted");
        }
        catch (ConstructPartException ex) when (ex.Message.Contains("side"))
        {
        }
        try
        {
            ConstructParts.Parse(noSides.Replace("\"boxes\":[{\"size\":[0.2,0.5,0.2]}]", "\"boxes\":[{\"size\":[0.2,0.5,0.2],\"side\":\"up\"}]"), "Bad.json");
            bad.Add("a box side other than base or leaf was accepted");
        }
        catch (ConstructPartException)
        {
        }
        JsLook("Gate", "Hinge", bad);
        JsLook("Turret", "Turntable", bad);
        Line($"joint sides: {joints.Count} joint parts tagged");
        foreach (var b in bad.Take(40))
            Fail($"joint sides: {b}");
    }

    /// <summary>Is part <paramref name="i"/> carried by segment <paramref name="seg"/> (own or up its chain)? A second way to count, apart from Construct.Under.</summary>
    private static bool JsRides(Construct c, int i, int seg)
    {
        for (var s = (int)c.Parts[i].Seg; s > 0; s = c.Joints[s - 1].ParentSeg)
            if (s == seg)
                return true;
        return false;
    }

    private static void JsLook(string blueprint, string jointPart, List<string> bad)
    {
        var w = new ConstructWorld();
        if (ConstructBuild.FindBlueprint(blueprint) is not { } bp || ConstructBuild.BuildOnGround(w, bp, Vector3.Zero, 0, out _) is not { } c)
        {
            bad.Add($"{blueprint}: doesn't build");
            return;
        }
        var part = PartNamed(c, jointPart);
        if (part < 0 || !JointSides.IsJoint(c, part, out var j))
        {
            bad.Add($"{blueprint}: no {jointPart} joint");
            return;
        }
        int Expect(bool leaf)
        {
            var n = 0;
            for (var i = 0; i < c.PartCount; i++)
                if (i != part && JsRides(c, i, j + 1) == leaf)
                    n++;
            return n;
        }
        JointSides.Count(c, j, out var b0, out var l0);
        if (b0 != Expect(false) || l0 != Expect(true) || l0 == 0 || b0 == 0 || b0 + l0 + 1 != c.PartCount)
            bad.Add($"{blueprint}: {jointPart} counts base {b0}, moving {l0}; expected {Expect(false)} and {Expect(true)} of {c.PartCount} parts");
        var look = JointSides.Look(c, part, j);
        if (!look.Contains($"({b0} part") || !look.Contains($"Moving side: {l0} part"))
            bad.Add($"{blueprint}: the look line \"{look}\" doesn't carry {b0} and {l0}");
        // The two groups, part by part: each part is on exactly one side, the joint part on none.
        for (var i = 0; i < c.PartCount; i++)
        {
            var want = i == part ? 0 : JsRides(c, i, j + 1) ? JointSides.Leaf : JointSides.Base;
            if (JointSides.SideOf(c, j, i) != want)
            {
                bad.Add($"{blueprint}: part {i} ({c.DefOf(i).Name}) is on side {JointSides.SideOf(c, j, i)}, expected {want}");
                break;
            }
        }
        // Take a moving-side part off: the moving side loses one.
        var gone = -1;
        for (var i = c.PartCount - 1; i >= 0 && gone < 0; i--)
            if (i != part && JsRides(c, i, j + 1) && c.DefOf(i).Role is not (PartRole.Hinge or PartRole.Joint))
                gone = i;
        if (gone >= 0)
        {
            w.RemovePart(c, gone);
            var part2 = PartNamed(c, jointPart);
            if (JointSides.IsJoint(c, part2, out var j2))
            {
                JointSides.Count(c, j2, out var b1, out var l1);
                if (l1 != l0 - 1 || b1 != b0)
                    bad.Add($"{blueprint}: after taking a part off the moving side: base {b1}, moving {l1} (was {b0} and {l0})");
            }
            else
                bad.Add($"{blueprint}: the joint went with the part taken off");
        }
        // Counting allocates nothing.
        JointSides.Count(c, j, out _, out _);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var sum = 0;
        for (var k = 0; k < 200; k++)
        {
            JointSides.Count(c, j, out var bb, out var ll);
            sum += bb + ll + JointSides.SideOf(c, j, k % c.PartCount) + JointSides.SocketSide(c.DefOf(part), k % 2);
        }
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        if (bytes > 0 || sum < 0)
            bad.Add($"{blueprint}: counting the sides allocated {bytes} B");
    }
}
