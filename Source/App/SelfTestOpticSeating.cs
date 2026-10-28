using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// Optic seating (the developer, 2026-10-27: a bot's built gun showed its optic floating at head height): every
    /// optic's base (its rail, or the bridge under it) rests ON the build's top, with no gap under it and nothing deep
    /// inside it. Checked on every grip with an optic socket x every receiver, with each receiver socket filled alone
    /// (side slots, upper rack rows) and with every slot filled, and on every standard gun build with an optic (the
    /// builds without one get one fitted), in both the view model (<see cref="GunModel.Emit"/>, the immediate draw) and
    /// the world model (the scene graph gun, <see cref="GunRig"/>, posed in a player's hands by
    /// <see cref="ThirdPersonArms"/>). The aim point (<see cref="GunModel.SightHeight"/>) sits inside the optic and the
    /// sight line in front of it is clear of the gun.
    /// </summary>
    private static void CheckOpticSeating()
    {
        const float Tol = 0.002f; // 2 mm: touching, not hovering and not sunk in
        var optics = Parts.All.Where(p => p.Category == PartCategory.Optic).ToList();
        var receivers = Parts.All.Where(p => p.Category == PartCategory.Receiver).ToList();
        var grips = Parts.All.Where(p => p.Frame != null && p.Sockets.Any(s => s.Type == "optic")).ToList();
        if (optics.Count == 0 || receivers.Count == 0 || grips.Count == 0)
        {
            Fail($"optic seating: needs optics, receivers and grips ({optics.Count}, {receivers.Count}, {grips.Count})");
            return;
        }
        var cases = new List<(string Who, WeaponBuild Build)>();
        var k = 0;
        foreach (var g in grips)
        {
            var recv = g.Sockets.Where(s => s.Type == "receiver").ToList();
            var optic = g.Sockets.First(s => s.Type == "optic");
            var fills = recv.Select(s => new List<PartSocket> { s }).ToList();
            if (recv.Count > 1)
                fills.Add(recv);
            foreach (var r in receivers)
                foreach (var fill in fills)
                {
                    var o = optics[k++ % optics.Count]; // every optic shares the rail; spread them over the cases
                    var b = new WeaponBuild("Optic seat", g);
                    foreach (var s in fill)
                        b.Root!.Attach(s.Name, r);
                    b.Root!.Attach(optic.Name, o);
                    cases.Add(($"{g.Name} + {(fill.Count > 1 ? "every " : fill[0].Name + " ")}{r.Name} + {o.Name}", b));
                }
        }
        int standard = 0, fitted = 0;
        foreach (var sb in StandardBuilds.All)
        {
            var b = sb.Build();
            if (b.IsMelee || b.Root == null)
                continue;
            if (b.Nodes.Any(n => n.Part.Category == PartCategory.Optic))
            {
                standard++;
                cases.Add(($"standard build {sb.Name}", b));
                continue;
            }
            var host = b.Nodes.FirstOrDefault(n => n.Part.Sockets.Any(s => s.Type == "optic" && n.Child(s.Name) == null));
            if (host == null)
                continue;
            var o = optics[fitted % optics.Count];
            host.Attach(host.Part.Sockets.First(s => s.Type == "optic" && host.Child(s.Name) == null).Name, o);
            fitted++;
            cases.Add(($"standard build {sb.Name} + {o.Name}", b));
        }

        var me = new Player { Alive = true };
        var body = Matrix4x4.CreateRotationY(-me.RenderYaw) * Matrix4x4.CreateTranslation(me.RenderPosition);
        var rig = new GunRig();
        var probe = new OpticSeatProbe();
        int checkedCount = 0, bad = 0, bridged = 0;
        float worstGap = 0, worstSink = 0, worstWorld = 0;
        string worstGapAt = "", worstSinkAt = "";
        foreach (var (who, b) in cases)
        {
            var a = WeaponBuilder.Assemble(b);
            if (a.PreviewDef is not { } def || def.Optic == OpticKind.Iron || def.BuiltMesh is not { } mesh)
                continue;
            checkedCount++;
            if (mesh.RiserLow < mesh.Mount)
                bridged++;
            // View model: the immediate draw's boxes in gun space.
            probe.Clear();
            GunModel.Emit(def, probe, 0f);
            var view = OpticSeat(probe.Boxes, Matrix4x4.Identity);
            // World model: the scene graph gun in a player's hands (scaled up, pitched), measured along the gun's own axes.
            var pose = ThirdPersonArms.Animate(me, def, ThirdPersonArms.RestGun(me), body, 50);
            rig.Build(def);
            rig.Pose(pose, 0f, Vector3.Zero, false, false, 0f);
            rig.Root.UpdateWorld();
            probe.Clear();
            probe.FromRig(rig);
            var world = OpticSeat(probe.Boxes, pose);
            var scale = new Vector3(pose.M21, pose.M22, pose.M23).Length();
            if (!float.IsFinite(view.Gap))
            {
                if (bad++ < 12)
                    Fail($"optic seating: {who}: nothing of the gun under the optic's base (it floats; base at y {view.Bottom:0.000})");
                continue;
            }
            if (view.Gap > worstGap)
                (worstGap, worstGapAt) = (view.Gap, who);
            if (-view.Gap > worstSink)
                (worstSink, worstSinkAt) = (-view.Gap, who);
            if (MathF.Abs(view.Gap) > Tol && bad++ < 12)
                Fail(view.Gap > 0
                    ? $"optic seating: {who}: the optic floats {view.Gap * 100:0.0} cm above the gun's top under it (base y {view.Bottom:0.000}, top y {view.Bottom - view.Gap:0.000})"
                    : $"optic seating: {who}: the gun reaches {-view.Gap * 100:0.0} cm into the optic's base (base y {view.Bottom:0.000})");
            var wd = MathF.Abs(world.Gap - view.Gap * scale);
            worstWorld = MathF.Max(worstWorld, float.IsFinite(wd) ? wd : 1f);
            if (!(MathF.Abs(world.Gap) <= Tol * scale) && bad++ < 12)
                Fail($"optic seating: {who}: in a player's hands the optic sits {world.Gap * 100:0.0} cm off the gun (view model {view.Gap * 100:0.0} cm x {scale:0.00})");
            // The aim point: inside the optic, above its base, and nothing of the gun in front of it on the sight line.
            var sight = GunModel.SightHeight(def);
            if ((sight <= view.Bottom || sight > view.OpticTop + 1e-4f) && bad++ < 12)
                Fail($"optic seating: {who}: the sight line (y {sight:0.000}) isn't inside the optic (y {view.Bottom:0.000} to {view.OpticTop:0.000})");
            if (SightBlocked(def, sight, mesh.OpticZ) is { } block && bad++ < 12)
                Fail($"optic seating: {who}: the sight line (y {sight:0.000}) runs into the gun in front of the optic ({block})");
        }
        Line($"optic seating: {checkedCount} builds ({grips.Count} grips x {receivers.Count} receivers, each slot alone and every slot; {standard} standard builds with an optic, {fitted} fitted with one); {bridged} on a bridge or riser; worst gap {worstGap * 1000:0.0} mm ({worstGapAt}), worst sink {worstSink * 1000:0.0} mm ({worstSinkAt}); world model differs by {worstWorld * 1000:0.00} mm at most");
    }

    private readonly record struct SeatResult(float Bottom, float Gap, float OpticTop);

    /// <summary>
    /// The optic's base (its lowest boxes) against the gun under it, in the frame of <paramref name="pose"/> (gun axes,
    /// world metres): base bottom, gap (base bottom minus the top of the gun boxes under the base; +inf if none) and the
    /// optic's top.
    /// </summary>
    private static SeatResult OpticSeat(List<(Matrix4x4 M, bool Optic, bool Clear)> boxes, in Matrix4x4 pose)
    {
        var frame = GunFrame(pose);
        var bottom = float.MaxValue;
        var top = float.MinValue;
        foreach (var (m, optic, clear) in boxes)
            if (optic && !clear)
            {
                var (lo, hi) = InFrame(m, frame);
                bottom = MathF.Min(bottom, lo.Y);
                top = MathF.Max(top, hi.Y);
            }
        if (bottom == float.MaxValue)
            return new SeatResult(0, float.PositiveInfinity, 0);
        var under = float.MinValue;
        var shrink = 0.001f * frame.Scale;
        foreach (var (m, optic, clear) in boxes)
        {
            if (!optic || clear)
                continue;
            var (blo, bhi) = InFrame(m, frame);
            if (blo.Y > bottom + shrink)
                continue; // not the base layer
            foreach (var (g, gOptic, gClear) in boxes)
            {
                if (gOptic || gClear)
                    continue;
                var (lo, hi) = InFrame(g, frame);
                if (hi.X > blo.X + shrink && lo.X < bhi.X - shrink && hi.Z > blo.Z + shrink && lo.Z < bhi.Z - shrink && lo.Y < bottom + 0.05f * frame.Scale)
                    under = MathF.Max(under, hi.Y);
            }
        }
        return new SeatResult(bottom, under == float.MinValue ? float.PositiveInfinity : bottom - under, top);
    }

    /// <summary>What of the gun the sight line in front of the optic runs into (null if clear), checked in the view model's gun space.</summary>
    private static string? SightBlocked(WeaponDef def, float sight, float opticZ)
    {
        var p = new OpticSeatProbe();
        GunModel.Emit(def, p, 0f);
        foreach (var (m, optic, clear) in p.Boxes)
        {
            if (optic || clear)
                continue;
            var (lo, hi) = InFrame(m, GunFrame(Matrix4x4.Identity));
            if (lo.X < -0.002f && hi.X > 0.002f && lo.Y < sight - 0.002f && hi.Y > sight + 0.002f && lo.Z < opticZ - 0.06f)
                return $"a box from y {lo.Y:0.000} to {hi.Y:0.000}, z {lo.Z:0.000} to {hi.Z:0.000}";
        }
        return null;
    }

    private readonly record struct Frame3(Vector3 Origin, Vector3 X, Vector3 Y, Vector3 Z, float Scale);

    private static Frame3 GunFrame(in Matrix4x4 pose)
    {
        var x = new Vector3(pose.M11, pose.M12, pose.M13);
        var y = new Vector3(pose.M21, pose.M22, pose.M23);
        var z = new Vector3(pose.M31, pose.M32, pose.M33);
        return new Frame3(pose.Translation, Vector3.Normalize(x), Vector3.Normalize(y), Vector3.Normalize(z), y.Length());
    }

    /// <summary>A box's extent along the gun's axes (metres, from the gun's origin).</summary>
    private static (Vector3 Min, Vector3 Max) InFrame(in Matrix4x4 m, in Frame3 f)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        for (var i = 0; i < 8; i++)
        {
            var w = Vector3.Transform(new Vector3((i & 1) - 0.5f, ((i >> 1) & 1) - 0.5f, ((i >> 2) & 1) - 0.5f), m) - f.Origin;
            var q = new Vector3(Vector3.Dot(w, f.X), Vector3.Dot(w, f.Y), Vector3.Dot(w, f.Z));
            min = Vector3.Min(min, q);
            max = Vector3.Max(max, q);
        }
        return (min, max);
    }

    /// <summary>Collects a gun's boxes: optic or not, see-through or not.</summary>
    private sealed class OpticSeatProbe : GunModel.IPartSink
    {
        public readonly List<(Matrix4x4 M, bool Optic, bool Clear)> Boxes = new();

        public void Clear() => Boxes.Clear();

        public void Part(in Matrix4x4 local, Vector3 color, float alpha, int mode, GunModel.PartGroup group, GunModel.EmitFlags flags, int shape) =>
            Boxes.Add((local, group == GunModel.PartGroup.Optic, alpha < 0.999f || (flags & GunModel.EmitFlags.Blend) != 0));

        public void Flash(Vector3 muzzle, float size, Vector3 color, float alpha)
        {
        }

        /// <summary>The scene graph gun's boxes (world matrices) by their group node ("optic" or not).</summary>
        public void FromRig(GunRig rig)
        {
            for (var i = 0; i < rig.Root.ChildCount; i++)
            {
                var g = rig.Root.Child(i);
                if ((g.Flags & NodeFlags.Socket) != 0)
                    continue;
                var optic = g.Name == "optic";
                for (var j = 0; j < g.ChildCount; j++)
                {
                    var n = g.Child(j);
                    if (n.Renderable.Kind != RenderKind.Box)
                        continue;
                    var r = n.Renderable;
                    Boxes.Add((n.World, optic, r.Alpha < 0.999f || (r.Flags & RenderFlags.Blend) != 0));
                }
            }
        }
    }
}
