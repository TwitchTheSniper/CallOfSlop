using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// "Joint welds" (board item dev-socket-weld-hinge-side-wh01; docs/sandbox/CONSTRUCTS.md, "Socket weld" and "Freeform
    /// welds"): for every joint part (passive and motorised hinges, the lid hinge, turntables, pistons and the steering
    /// pivot, every actuator size) on a powered, anchored rig with a lever wired into it, a panel (a door on the passive
    /// hinge) welded onto the joint's leaf through the server's weld path (socket weld on the grid, socket weld 30 degrees off
    /// it, freeform weld where it is held) sits on the leaf's outward side at the ghost's pose (the wrench's pose for the
    /// grid weld, within 1 cm and 1 degree), hangs on the joint's segment, and a client's copy agrees; then the lever drives
    /// the joint out and back and the welded panel moves with it exactly as the wrench-built panel does (driving allocates
    /// nothing). A piece welded onto the base side (the frame block, a piston's or turntable's side socket, a freeform weld
    /// onto the block) stays rigid with the base; a piece that would weld to both the leaf and the fixed side is refused
    /// ("that would lock the joint"). Short scripted scenarios on a flat test floor.
    /// </summary>
    private static void CheckJointWelds()
    {
        var joints = new List<ConstructPartDef>();
        foreach (var d in ConstructParts.All)
            if (d.HasJoint && d.LeafIndex >= 0)
                joints.Add(d);
        var bad = new List<string>();
        var summary = new List<string>();
        foreach (var d in joints)
            JwJoint(d, bad, summary);
        Line($"joint welds: {joints.Count} joint parts: {string.Join("; ", summary)}");
        JwLock(bad);
        foreach (var b in bad.Take(60))
            Fail($"joint welds: {b}");
        if (bad.Count > 60)
            Fail($"joint welds: and {bad.Count - 60} more");
    }

    private const int JwDriveTicks = 90;

    /// <summary>
    /// The rig: an anchored foundation with a battery and a lever on it, a frame block, and the joint part on the block's
    /// top by its base (its leaf up), the lever wired into it (a motorised joint). Null if it doesn't build.
    /// </summary>
    private static Construct? JwRig(ConstructWorld w, IWorldQuery q, ConstructPartDef joint, out int jp, out int lever, out int block)
    {
        jp = lever = block = -1;
        if (JwOnChest(joint))
        {
            // A lid hinge (its leaf square to its base, 2 cm off it): the Chest blueprint without its lid, Interact works it.
            if (ConstructBuild.FindBlueprint("Chest") is not { } bp || ConstructBuild.BuildOnGround(w, bp, ConstructWorld.Ground(q, 0, 0), 0, out _) is not { } chest)
                return null;
            var lid = PartNamed(chest, "Chest Lid");
            if (lid >= 0)
                w.RemovePart(chest, lid);
            chest.Pinned = true;
            chest.Rebuild();
            jp = PartNamed(chest, joint.Name);
            return jp < 0 ? null : chest;
        }
        if (ConstructBuild.PlaceNew(w, ConstructParts.Need("Foundation"), ConstructWorld.Ground(q, 0, 0), 0) is not { } c)
            return null;
        c.Pinned = true;
        block = FitTurned(w, c, 0, "top11", "Frame Block", "-y", _ => true);
        var battery = FitTurned(w, c, 0, "top00", "Battery", "-y", _ => true);
        lever = FitTurned(w, c, 0, "top22", "Lever", "-y", _ => true);
        jp = block < 0 ? -1 : FitTurned(w, c, block, "+y", joint.Name, joint.Sockets[0].Name == "base" ? "base" : BaseSocket(joint), _ => true);
        if (block < 0 || battery < 0 || lever < 0 || jp < 0)
        {
            w.Remove(c);
            return null;
        }
        if (!(joint.Role == PartRole.Joint && ConstructBuild.CanWire(w, c, lever, jp) == null && w.AddWire(c, lever, jp)))
            lever = -1; // (not wired: a passive hinge, which Interact opens)
        c.Rebuild();
        return c;
    }

    /// <summary>A joint whose leaf isn't opposite its base (the lid hinge) is tested on the Chest it is made for.</summary>
    private static bool JwOnChest(ConstructPartDef d) => d.Sockets.All(s => s.Dir != (d.Sockets[d.LeafIndex].Dir ^ 1));

    /// <summary>A joint part's base socket: the one opposite its leaf.</summary>
    private static string BaseSocket(ConstructPartDef d)
    {
        var leafDir = d.Sockets[d.LeafIndex].Dir;
        foreach (var s in d.Sockets)
            if (s.Dir == (leafDir ^ 1) && s.Kind == SocketKind.Struct)
                return s.Name;
        return d.Sockets[0].Name;
    }

    /// <summary>The piece that goes on a joint's leaf: a door (by its hang socket) on a passive hinge, else a panel (by its back).</summary>
    private static (string Part, string By) JwPiece(ConstructPartDef joint) =>
        JwOnChest(joint) ? ("Chest Lid", "hang") : joint.Sockets[joint.LeafIndex].Kind == SocketKind.Leaf ? ("Door", "hang") : ("Panel", "back");

    /// <summary>Drives the joint (the lever's state, or a passive hinge's latch) and steps.</summary>
    private static void JwDrive(ConstructWorld w, IWorldQuery q, Construct c, int jp, int lever, byte state, int ticks)
    {
        var j = c.JointOf(jp);
        if (lever >= 0)
            c.Parts[lever].State = state;
        else if (j >= 0)
            c.Joints[j].Open = state == 1;
        c.Wake();
        for (var t = 0; t < ticks; t++)
            w.Step(q, World.TickDt);
    }

    private readonly record struct JwPose(Vector3 P, Quaternion Q, float Angle);

    private static JwPose JwNow(Construct c, int jp, int piece)
    {
        Freeform.PartWorld(c, piece, out var p, out var r);
        var j = c.JointOf(jp);
        return new JwPose(p, r, j >= 0 ? c.Joints[j].Angle : float.NaN);
    }

    /// <summary>The leaf socket of the joint part in the world (at rest: the joint part's own frame) and its outward facing.</summary>
    private static void JwLeaf(Construct c, int jp, out Vector3 at, out Vector3 dir)
    {
        var d = c.DefOf(jp);
        c.SocketAt(jp, d.LeafIndex, out var la, out var ld);
        c.WorldFrame(c.Parts[jp].Seg, out var q, out var t);
        at = t + Vector3.Transform(la, q);
        dir = Vector3.Transform(ConstructParts.DirVector(ld), q);
    }

    private static string JwUnit(ConstructPartDef d, float v) => d.JointType == JointType.Piston ? $"{v * 100:0.0} cm" : $"{Deg(v):0.0} deg";

    /// <summary>One joint part: the wrench's reference, then the three welds onto its leaf, then the base-side welds.</summary>
    private static void JwJoint(ConstructPartDef joint, List<string> bad, List<string> summary)
    {
        var q = new JointTestWorld();
        var (pieceName, by) = JwPiece(joint);
        var pieceDef = ConstructParts.Need(pieceName);
        var pieceSock = SwSocket(pieceDef, by);

        // The wrench's reference: the piece put on the leaf with the wrench (ConstructBuild.Place / Attach), driven out and back.
        var rw = new ConstructWorld();
        if (JwRig(rw, q, joint, out var rjp, out var rlever, out _) is not { } rc)
        {
            bad.Add($"the {joint.Name} rig doesn't build");
            return;
        }
        var leafSock = joint.LeafIndex;
        ConstructGhost g = default;
        var spin = -1;
        for (var s = 0; s < 4 && spin < 0; s++)
            if (ConstructBuild.Place(rc, rjp, leafSock, pieceDef, pieceSock, s, out g) == null)
                spin = s;
        if (spin < 0 || ConstructBuild.Attach(rw, rc, pieceDef, g) is not (var rpiece and >= 0))
        {
            bad.Add($"the wrench can't put a {pieceName} on the {joint.Name}'s leaf");
            return;
        }
        rc.Rebuild();
        var rRest = JwNow(rc, rjp, rpiece);
        JwDrive(rw, q, rc, rjp, rlever, 1, JwDriveTicks);
        var powered = ConstructPower.On(rc, rjp) && (JwOnChest(joint) || rc.Electrical && rc.HasBattery);
        var rOut = JwNow(rc, rjp, rpiece);
        JwDrive(rw, q, rc, rjp, rlever, 2, JwDriveTicks);
        var rBack = JwNow(rc, rjp, rpiece);
        var wired = rc.JointOf(rjp) is var rj and >= 0 && (rlever < 0 ? joint.Role != PartRole.Joint || JwOnChest(joint) : rc.Joints[rj].Wired);
        if (!powered || !wired)
            bad.Add($"the {joint.Name} rig: power {powered}, wired {wired}");
        var travel = MathF.Abs(rOut.Angle - rRest.Angle);
        if (!(travel > (joint.JointType == JointType.Piston ? 0.2f : 0.15f)))
            bad.Add($"the wrench-built {joint.Name} didn't move its {pieceName} ({JwUnit(joint, travel)})");

        var parts = new List<string>();
        foreach (var how in new[] { "socket weld", "socket weld +30", "freeform weld" })
        {
            var w = new ConstructWorld();
            if (JwRig(w, q, joint, out var jp, out var lever, out _) is not { } c)
            {
                bad.Add($"the {joint.Name} rig doesn't build");
                return;
            }
            JwLeaf(c, jp, out var leafAt, out var leafDir);
            var eye = leafAt + new Vector3(1.0f, 0.6f, 1.0f);
            var hands = FwHands();
            string? why;
            Vector3 gp;
            Quaternion gq;
            if (how != "freeform weld")
            {
                if (FwLoose(w, pieceName, leafAt + new Vector3(1.8f, 0.8f, -0.4f), Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.4f), frozen: true) is not { } loose)
                    return;
                var target = new WeldTarget { Body = c.Id, Part = (byte)jp, Socket = (byte)leafSock };
                SocketWeld.SourceFrame(loose, 0, pieceSock, out _, out var sDir);
                SocketWeld.TargetFrame(c, target, out _, out var tDir);
                var degrees = SocketWeld.FromWire(SocketWeld.WireAngle(SocketWeld.AngleOfPartTurn(loose, 0, sDir, tDir, g.Rot) + (how == "socket weld" ? 0 : 30)));
                if (how != "socket weld" && JwOnChest(joint))
                {
                    parts.Add($"{how}: none (a chest lid turned off the grid either way is inside the chest)");
                    continue;
                }
                SocketWeld.Pose(loose, 0, pieceSock, c, target, degrees, out _, out _, out gp, out gq);
                why = hands.BeginSocketWeld(w, 0, eye, loose.Id, 0, pieceSock, target, degrees, 0);
            }
            else
            {
                // Held by hand where the wrench would put it, 2 cm out from the leaf and a few degrees out.
                if (FwLoose(w, pieceName, Vector3.Zero, Quaternion.Identity, frozen: true) is not { } loose)
                    return;
                var tilt = Quaternion.CreateFromAxisAngle(Vector3.Normalize(new Vector3(0.3f, 0.2f, 1)), 3f * MathF.PI / 180);
                c.WorldFrame(c.Parts[jp].Seg, out var cq, out var ct);
                var rq = Quaternion.Normalize(cq * Rot24.Quat(g.Rot));
                gp = ct + Vector3.Transform(g.Pos, cq) + leafDir * 0.02f;
                gq = Quaternion.Normalize(tilt * rq);
                loose.Pos = loose.PrevPos = gp;
                loose.Rot = loose.PrevRot = gq;
                var side = Freeform.OnLeafSide(c, jp, loose, 0);
                why = side ? hands.BeginFreeform(w, 0, eye, loose.Id, 0, c.Id, jp, 0) : "it isn't seen as on the leaf side";
            }
            c.Rebuild();
            var piece = c.PartCount - 1;
            var j = c.JointOf(jp);
            var onLeaf = why == null && j >= 0 && c.DefOf(piece) == pieceDef && c.Under(piece, j + 1);
            if (!onLeaf)
            {
                bad.Add($"{how} of a {pieceName} onto the {joint.Name}'s leaf: {why ?? (j < 0 ? "no joint made" : "it isn't on the joint's segment")}");
                parts.Add($"{how}: not on the leaf");
                continue;
            }
            var rest = JwNow(c, jp, piece);
            var (move, turn) = FwDiff((rest.P, rest.Q), (gp, gq));
            var centre = ConstructRender.PartCentre(c, piece);
            var outward = Vector3.Dot(centre - leafAt, leafDir);
            if (move > 0.01f || turn > 1f)
                bad.Add($"{how} onto the {joint.Name}'s leaf: the {pieceName} isn't where its ghost was ({move * 100:0.0} cm, {turn:0.0} deg off)");
            if (!(outward > 0))
                bad.Add($"{how} onto the {joint.Name}'s leaf: the {pieceName} is on the wrong side ({outward * 100:0.0} cm along the leaf's facing)");
            if (how == "socket weld")
            {
                var (wm, wt) = FwDiff((rest.P, rest.Q), (rRest.P, rRest.Q));
                if (wm > 0.01f || wt > 1f || c.Parts[piece].Seg != rc.Parts[rpiece].Seg || !c.SocketUsed(jp, leafSock))
                    bad.Add($"socket weld onto the {joint.Name}'s leaf: not as the wrench puts it ({wm * 100:0.0} cm, {wt:0.0} deg off; segment {c.Parts[piece].Seg} vs {rc.Parts[rpiece].Seg}; leaf linked {c.SocketUsed(jp, leafSock)})");
            }
            // A client's copy (the construct message) hangs it on the same joint.
            var copy = new ConstructWorld();
            var pw = new PacketWriter(65536);
            ConstructNet.WriteFull(pw, c);
            var pr = new PacketReader(pw.Span[1..]);
            ConstructNet.Read(ref pr, copy, 0);
            var cc = copy.Get(c.Id);
            cc?.Rebuild();
            var same = cc != null && cc.PartCount == c.PartCount && cc.JointCount == c.JointCount && cc.Under(piece, j + 1);
            if (!same)
                bad.Add($"{how} onto the {joint.Name}'s leaf: a client's copy doesn't hang it on the joint");

            // Driven out and back: the joint carries the panel, as the wrench-built one does.
            JwDrive(w, q, c, jp, lever, 1, 30);
            var a0 = GC.GetAllocatedBytesForCurrentThread();
            JwDrive(w, q, c, jp, lever, 1, JwDriveTicks - 30);
            var bytes = GC.GetAllocatedBytesForCurrentThread() - a0;
            var outP = JwNow(c, jp, piece);
            JwDrive(w, q, c, jp, lever, 2, JwDriveTicks);
            var backP = JwNow(c, jp, piece);
            var (moved, turned) = FwDiff((outP.P, outP.Q), (rest.P, rest.Q));
            var exact = how == "socket weld";
            var tol = exact ? (joint.JointType == JointType.Piston ? 0.01f : MathF.PI / 180) : (joint.JointType == JointType.Piston ? 0.03f : 5 * MathF.PI / 180); // (off the grid: the load sits a little differently)
            var angOut = MathF.Abs(outP.Angle - rOut.Angle);
            var angBack = MathF.Abs(backP.Angle - rBack.Angle);
            if (angOut > tol || angBack > tol)
                bad.Add($"{how} onto the {joint.Name}: the joint went to {JwUnit(joint, outP.Angle)} and back to {JwUnit(joint, backP.Angle)}, the wrench's to {JwUnit(joint, rOut.Angle)} and {JwUnit(joint, rBack.Angle)}");
            if (!(moved > 0.05f || turned > 10f))
                bad.Add($"{how} onto the {joint.Name}: driven, the {pieceName} moved {moved * 100:0.0} cm and turned {turned:0.0} deg (it doesn't move with the joint)");
            if (exact)
            {
                var (om, ot) = FwDiff((outP.P, outP.Q), (rOut.P, rOut.Q));
                var (bm, bt) = FwDiff((backP.P, backP.Q), (rBack.P, rBack.Q));
                if (om > 0.01f || ot > 1f || bm > 0.01f || bt > 1f)
                    bad.Add($"socket weld onto the {joint.Name}: driven, the {pieceName} is {om * 100:0.0} cm / {ot:0.0} deg (out) and {bm * 100:0.0} cm / {bt:0.0} deg (back) from the wrench-built one");
            }
            if (bytes > 0)
                bad.Add($"{how} onto the {joint.Name}: {bytes} bytes allocated in {JwDriveTicks - 30} ticks of driving it");
            parts.Add($"{how} {moved * 100:0} cm {turned:0} deg");
        }
        if (!JwOnChest(joint))
            JwBaseSide(joint, q, bad, parts);
        summary.Add($"{joint.Name} ({JwUnit(joint, rOut.Angle)} out, {JwUnit(joint, rBack.Angle)} back): {string.Join(", ", parts)}");
    }

    /// <summary>Welds onto the base side stay rigid with the base: a lever socket-welded onto the frame block (and onto the joint part's own side socket, when it has one), a lever freeform-welded to the block.</summary>
    private static void JwBaseSide(ConstructPartDef joint, IWorldQuery q, List<string> bad, List<string> parts)
    {
        var w = new ConstructWorld();
        if (JwRig(w, q, joint, out var jp, out var lever, out var block) is not { } c)
            return;
        var leverDef = ConstructParts.Need("Lever");
        var by = SwSocket(leverDef, "-x");
        var hands = FwHands();
        var targets = new List<(int Part, int Socket)> { (block, SwSocket(c.DefOf(block), "+x")) };
        for (var s = 0; s < joint.Sockets.Length; s++)
            if (s != joint.LeafIndex && joint.Sockets[s].Kind == SocketKind.Struct && !c.SocketUsed(jp, s))
            {
                targets.Add((jp, s));
                break;
            }
        var rigid = 0;
        foreach (var (part, socket) in targets)
        {
            c.WorldFrame(c.Parts[part].Seg, out var cq, out var ct);
            c.SocketAt(part, socket, out var sa, out _);
            var at = ct + Vector3.Transform(sa, cq);
            if (FwLoose(w, "Lever", at + new Vector3(1.2f, 0.6f, 0.6f), Quaternion.Identity, frozen: true) is not { } loose)
                return;
            var target = new WeldTarget { Body = c.Id, Part = (byte)part, Socket = (byte)socket };
            var why = hands.BeginSocketWeld(w, 0, at + new Vector3(1, 0.6f, 1), loose.Id, 0, by, target, 0, 0);
            c.Rebuild();
            if (why != null || !c.Rigid(c.Parts[c.PartCount - 1].Seg))
                bad.Add($"a lever socket-welded onto the {c.DefOf(part).Name}'s {c.DefOf(part).Sockets[socket].Name} (the {joint.Name}'s base side): {why ?? "it isn't rigid with the base"}");
            else
                rigid++;
        }
        // Freeform: held 1 cm off the frame block's -x face.
        c.WorldFrame(0, out var bq, out var bt);
        c.SocketAt(block, SwSocket(c.DefOf(block), "-x"), out var fa, out _);
        if (FwLoose(w, "Lever", bt + Vector3.Transform(fa, bq) + new Vector3(-0.11f, 0, 0), Quaternion.Identity, frozen: true) is not { } held)
            return;
        var ffWhy = hands.BeginFreeform(w, 0, bt + Vector3.Transform(fa, bq) + new Vector3(-1, 0.6f, 1), held.Id, 0, c.Id, block, 0);
        c.Rebuild();
        if (ffWhy != null || !c.Rigid(c.Parts[c.PartCount - 1].Seg))
            bad.Add($"a lever freeform-welded onto the frame block under the {joint.Name}: {ffWhy ?? "it isn't rigid with the base"}");
        else
            rigid++;
        parts.Add($"{rigid} base-side welds rigid");
    }

    /// <summary>
    /// A piece on a motor hinge's leaf that would also weld to a block on the fixed side (their sockets meet): both the socket
    /// weld and the freeform weld are refused (that would lock the joint), and without that block the same weld goes on.
    /// (Resting against the fixed side is fine: the chest's lid on the lid hinge, above.)
    /// </summary>
    private static void JwLock(List<string> bad)
    {
        var q = new JointTestWorld();
        var joint = ConstructParts.Need("Motor Hinge");
        var results = new List<string>();
        for (var k = 0; k < 3; k++)
        {
            var w = new ConstructWorld();
            if (JwRig(w, q, joint, out var jp, out _, out var block) is not { } c)
            {
                bad.Add("the lock test's rig doesn't build");
                return;
            }
            JwLeaf(c, jp, out var leafAt, out _);
            var piece = ConstructParts.Need("Frame Block");
            // The piece's place on the leaf (the wrench's pose), then a wall block standing flush against its side.
            ConstructGhost g = default;
            var ok = false;
            for (var s = 0; s < 4 && !ok; s++)
                ok = ConstructBuild.Place(c, jp, joint.LeafIndex, piece, SwSocket(piece, "-y"), s, out g) == null;
            c.WorldFrame(0, out var cq, out var ct);
            var pieceAt = ct + Vector3.Transform(g.Pos, cq);
            // A wall beside the rig's block (a lever, then two frame blocks): the top block's -x socket meets the piece's +x one.
            var b0 = k < 2 ? FitTurned(w, c, 0, "top21", "Lever", "-y", _ => true) : -1;
            var b1 = b0 < 0 ? -1 : FitTurned(w, c, b0, "+y", "Frame Block", "-y", _ => true);
            var b2 = b1 < 0 ? -1 : FitTurned(w, c, b1, "+y", "Frame Block", "-y", _ => true);
            if (k < 2 && b2 < 0)
            {
                bad.Add("the lock test's wall doesn't build");
                return;
            }
            string? why;
            if (k != 1)
            {
                if (FwLoose(w, "Frame Block", leafAt + new Vector3(-1.5f, 0.8f, 0.4f), Quaternion.Identity, frozen: true) is not { } loose)
                    return;
                var target = new WeldTarget { Body = c.Id, Part = (byte)jp, Socket = (byte)joint.LeafIndex };
                SocketWeld.SourceFrame(loose, 0, SwSocket(piece, "-y"), out _, out var sDir);
                SocketWeld.TargetFrame(c, target, out _, out var tDir);
                var degrees = SocketWeld.AngleOfPartTurn(loose, 0, sDir, tDir, g.Rot);
                why = FwHands().BeginSocketWeld(w, 0, leafAt + new Vector3(-1, 0.6f, 1), loose.Id, 0, SwSocket(piece, "-y"), target, degrees, 0);
            }
            else
            {
                if (FwLoose(w, "Frame Block", pieceAt + new Vector3(0, 0.003f, 0), Quaternion.Normalize(cq * Rot24.Quat(g.Rot)), frozen: true) is not { } loose)
                    return;
                why = FwHands().BeginFreeform(w, 0, leafAt + new Vector3(-1, 0.6f, 1), loose.Id, 0, c.Id, jp, 0);
            }
            results.Add(k switch { 0 => "socket weld", 1 => "freeform weld", _ => "no wall: socket weld" } + $": {why ?? "welded"}");
            var locked = why != null && why.Contains("lock");
            if (k < 2 && !locked)
                bad.Add($"a piece on the motor hinge's leaf welding to the fixed side too wasn't refused ({(k == 0 ? "socket" : "freeform")} weld: {why ?? "welded"})");
            if (k == 2 && why != null)
                bad.Add($"a piece on the motor hinge's leaf with nothing beside it was refused: {why}");
        }
        Line($"joint welds: a frame block on a motor hinge's leaf whose side socket meets a block's on the fixed side: {string.Join("; ", results)}");
    }
}
