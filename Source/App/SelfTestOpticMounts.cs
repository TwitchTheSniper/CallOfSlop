using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// Optic mounts (the user, 2026-10-23: "large receivers need to have the optic mount location adjusted accordingly"):
    /// on every root grip, with one receiver and with every receiver socket filled, every receiver (all of Parts/Receiver)
    /// takes every optic (all of Parts/Optic, whatever exists) ON its top: the optic's mount (where its rail starts, the
    /// lowest thing of any optic) is at or above the top of every receiver box under the rail, so nothing of the receiver
    /// rises into the optic, and it doesn't hover far above the receiver either. The sight line (aim point) is built from
    /// that mount. Iron sights still show only with a receiver and no optic.
    /// </summary>
    private static void CheckOpticMounts()
    {
        const float Eps = 1e-4f;
        const float MaxGap = 0.03f; // an optic may stand this far above a small receiver (the grip's rail), no more
        var optics = Parts.All.Where(p => p.Category == PartCategory.Optic).ToList();
        var receivers = Parts.All.Where(p => p.Category == PartCategory.Receiver).ToList();
        var grips = Parts.All.Where(p => p.Frame != null && p.Sockets.Any(s => s.Type == "optic")).ToList();
        if (optics.Count == 0 || receivers.Count == 0 || grips.Count == 0)
        {
            Fail($"optic mounts: needs optics, receivers and grips ({optics.Count}, {receivers.Count}, {grips.Count})");
            return;
        }
        int builds = 0, raised = 0;
        float worstGap = 0, worstRise = 0;
        string worstGapAt = "";
        foreach (var g in grips)
        {
            var recvSockets = g.Sockets.Where(s => s.Type == "receiver").ToList();
            var opticSocket = g.Sockets.First(s => s.Type == "optic");
            foreach (var r in receivers)
            {
                foreach (var all in new[] { false, true })
                {
                    if (all && recvSockets.Count < 2)
                        continue;
                    var sockets = all ? recvSockets : recvSockets.Take(1).ToList();
                    foreach (var o in optics)
                    {
                        var b = new WeaponBuild("Optic mount", g);
                        foreach (var s in sockets)
                            b.Root!.Attach(s.Name, r);
                        b.Root!.Attach(opticSocket.Name, o);
                        var m = BuildModel.Build(b, Vector3.One);
                        builds++;
                        var who = $"{g.Name} + {(all ? "every " : "")}{r.Name} + {o.Name}";
                        var rest = g.Sockets.First(s => s.Type == "optic").Pos + BuildModel.GripAnchor - (g.Hand ?? Vector3.Zero);
                        var node = m.Root.Find("optic_mount");
                        if (node == null)
                        {
                            Fail($"optic mounts: {who}: no optic_mount on the model");
                            continue;
                        }
                        var at = node.WorldPosition;
                        if (MathF.Abs(m.Mesh.Mount - at.Y) > Eps || MathF.Abs(m.Mesh.OpticZ - at.Z) > Eps)
                            Fail($"optic mounts: {who}: the mount ({m.Mesh.Mount:0.000}, {m.Mesh.OpticZ:0.000}) isn't the optic socket ({at.Y:0.000}, {at.Z:0.000})");
                        // The highest box of any receiver under the rail strip.
                        var top = float.MinValue;
                        foreach (var s in sockets)
                        {
                            var rn = b.Root!.Child(s.Name)!;
                            // Only the boxes under the rail strip count (a tall rear sight off to one side doesn't).
                            top = MathF.Max(top, StripTop(m, rn, at));
                        }
                        if (top == float.MinValue)
                            continue; // no receiver box under the strip: nothing to clear
                        if (at.Y < top - Eps)
                            Fail($"optic mounts: {who}: the optic mount (y {at.Y:0.000}) is inside the receiver, whose top is y {top:0.000}");
                        // Hovering: nothing drawn at all (the receiver, or the grip's own frame) under the rail within MaxGap below it.
                        var below = BuildModel.MountSeat(m.Mesh, at, float.MinValue);
                        var gap = at.Y - MathF.Max(top, below);
                        // (Multi-receiver frames stand their optic over the whole rack, so a part-filled one has a gap by design.)
                        if (gap > MaxGap && recvSockets.Count == 1)
                            Fail($"optic mounts: {who}: the optic hovers {gap * 100:0.0} cm above everything under it (mount y {at.Y:0.000}, top of the receiver y {top:0.000}, of the gun y {below:0.000})");
                        if (gap > worstGap)
                            (worstGap, worstGapAt) = (gap, who);
                        var rise = at.Y - (rest.Y);
                        if (rise > Eps)
                            raised++;
                        worstRise = MathF.Max(worstRise, rise);
                        // Nothing else drawn under the rail pokes into the optic either.
                        var seat = BuildModel.MountSeat(m.Mesh, at, float.MinValue);
                        if (seat > at.Y + Eps)
                            Fail($"optic mounts: {who}: something drawn under the optic reaches y {seat:0.000}, above the mount y {at.Y:0.000}");
                    }
                }
            }
        }
        Line($"optic mounts: {builds} builds ({grips.Count} grips x {receivers.Count} receivers x {optics.Count} optics, one and every receiver); {raised} raised above the grip's socket (most {worstRise * 100:0.0} cm); biggest gap above a receiver {worstGap * 100:0.0} cm ({worstGapAt})");

        // The aim point follows the mount: the sight line is the mount plus the optic's rise.
        if (Parts.Find("Hammerfall Autocannon Receiver") is { } big && Parts.Find("Plinker Receiver") is { } small
            && Parts.Find("Pistol Grip") is { } pg && optics.FirstOrDefault(p => p.Optic == OpticKind.Holo) is { } holo)
        {
            float Sight(PartDef recv)
            {
                var b = new WeaponBuild("t", pg);
                b.Root!.Attach("receiver_1", recv);
                b.Root!.Attach("optic", holo);
                var w = WeaponBuilder.Assemble(b);
                return w.PreviewDef == null ? 0 : GunModel.SightHeight(w.PreviewDef);
            }
            float sBig = Sight(big), sSmall = Sight(small);
            Line($"sight height on the Pistol Grip: Plinker {sSmall:0.000}, Hammerfall {sBig:0.000}");
            if (sBig <= sSmall + 0.01f)
                Fail("optic mounts: the sight line should rise with a bigger receiver (the aim point follows the optic)");
        }
    }

    /// <summary>Top of the receiver's own boxes under the optic's rail strip at <paramref name="at"/> (min value if none).</summary>
    private static float StripTop(BuiltModel m, BuildNode recv, Vector3 at)
    {
        var top = float.MinValue;
        var sn = m.Nodes[recv];
        void Visit(SceneNode n)
        {
            for (var i = 0; i < n.ChildCount; i++)
            {
                var c = n.Child(i);
                if ((c.Flags & NodeFlags.Socket) != 0 || c.Renderable.Kind != RenderKind.Box)
                    continue;
                if (c.Renderable.Alpha < 0.999f)
                    continue;
                var (lo, hi) = BuildModel.Bounds(c, false);
                if (hi.X > at.X - BuildModel.OpticHalfWidth && lo.X < at.X + BuildModel.OpticHalfWidth
                    && hi.Z > at.Z - BuildModel.OpticHalfLength && lo.Z < at.Z + BuildModel.OpticHalfLength)
                    top = MathF.Max(top, hi.Y);
            }
        }
        Visit(sn);
        return top;
    }
}
