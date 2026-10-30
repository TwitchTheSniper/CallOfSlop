using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Cargo volumes drawn (decision 21; ConstructCargo.cs): invisible when idle but for a faint marking of the box's floor;
/// with a tool out, the whole box outlined (ice blue while frozen, amber while free); the control box's lamp in the same
/// colour; with the configure tool out, a handle on each face (the one under the crosshair bright, a box being dragged
/// shown as it will be). Unlit bars through the renderer's box draw; no allocation.
/// </summary>
public static partial class ConstructRender
{
    public static bool VolumesShown; // a tool is out (build mode): the boxes are outlined
    public static Construct? VolumeHotBody, VolumeDragBody; // the configure tool: the handle under the crosshair; the box being dragged
    public static int VolumeHotPart = -1, VolumeHotFace = -1, VolumeDragPart = -1;
    public static Vector3 VolumeDragSize, VolumeDragAt;

    private static readonly Vector3 VolFrozen = new(0.45f, 0.85f, 1f), VolFree = new(1f, 0.72f, 0.25f), VolHot = new(1f, 1f, 1f);

    /// <summary>Every cargo volume of construct <paramref name="c"/> (its body matrix <paramref name="body"/>).</summary>
    private static int DrawVolumes(Renderer gfx, Construct c, in Matrix4x4 body, float alpha)
    {
        var n = 0;
        for (var i = 0; i < c.PartCount; i++)
        {
            ref var p = ref c.Parts[i];
            if (!ConstructCargo.IsVolume(p.D))
                continue;
            var frozen = ConstructCargo.IsFrozen(p);
            var colour = frozen ? VolFrozen : VolFree;
            var seg = p.Seg == 0 ? Matrix4x4.Identity : SegMatrix(c, p.Seg, alpha);
            var m = seg * body;
            ConstructCargo.Settings(p, p.D, out var size, out var at);
            var dragged = VolumeDragBody == c && VolumeDragPart == i;
            if (dragged)
            {
                size = VolumeDragSize;
                at = VolumeDragAt;
            }
            ConstructCargo.BoxFrom(p, size, at, out var centre, out var half);
            // The control box's lamp: its state at a glance.
            var lamp = p.Pos + Rot24.Apply(p.Rot, new Vector3(0, 0.115f, 0));
            gfx.Draw(Matrix4x4.CreateScale(0.07f, 0.02f, 0.07f) * Matrix4x4.CreateTranslation(lamp) * m, colour, 0.95f, mode: 2);
            n++;
            if (!VolumesShown && !dragged)
            {
                // Idle: the floor's corners only, faint.
                var floor = centre - Rot24.Apply(p.Rot, new Vector3(0, size.Y * 0.5f, 0));
                var fh = half;
                fh[AxisOf(p.Rot)] = 0.004f;
                Outline(gfx, floor, fh, m, colour, frozen ? 0.28f : 0.14f);
                n += 12;
                continue;
            }
            Outline(gfx, centre, half, m, colour, dragged ? 0.9f : 0.6f);
            n += 12;
            if (VolumeHotBody != c && !dragged)
                continue;
            // The configure tool's face handles.
            for (var f = 0; f < 6; f++)
            {
                var hp = p.Pos + Rot24.Apply(p.Rot, ConstructCargo.FaceAt(size, at, f));
                var hot = VolumeHotBody == c && VolumeHotPart == i && VolumeHotFace == f;
                gfx.Draw(Matrix4x4.CreateScale(hot ? 0.14f : 0.09f) * Matrix4x4.CreateTranslation(hp) * m, hot ? VolHot : colour, hot ? 0.95f : 0.7f, mode: 2);
                n++;
            }
        }
        return n;
    }

    /// <summary>The axis of the rest frame a part's own y (its "up") lies along.</summary>
    private static int AxisOf(byte rot)
    {
        var up = Rot24.Apply(rot, Vector3.UnitY);
        return MathF.Abs(up.X) > 0.5f ? 0 : MathF.Abs(up.Y) > 0.5f ? 1 : 2;
    }
}
