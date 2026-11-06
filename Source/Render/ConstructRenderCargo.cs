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

    private static readonly Vector3 VolFrozen = new(0.45f, 0.85f, 1f), VolFree = new(1f, 0.72f, 0.25f), VolHot = new(1f, 1f, 1f), VolStore = new(0.5f, 0.9f, 0.55f), VolTank = new(0.4f, 0.65f, 1f), VolOpen = new(1f, 0.25f, 0.2f);

    /// <summary>Every cargo volume of construct <paramref name="c"/> (its body matrix <paramref name="body"/>).</summary>
    private static int DrawVolumes(Renderer gfx, Construct c, in Matrix4x4 body, float alpha)
    {
        var n = 0;
        for (var i = 0; i < c.PartCount; i++)
        {
            ref var p = ref c.Parts[i];
            if (!ConstructCargo.HasBox(p.D))
                continue;
            var tank = p.D.IsTankVolume;
            var store = p.D.IsSizedBox;
            var frozen = !store && ConstructCargo.IsFrozen(p);
            var colour = tank ? VolTank : store ? VolStore : frozen ? VolFrozen : VolFree;
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
            if (p.D.Envelope is { } env && !dragged && !p.Broken)
            {
                // A lift cell's envelope: solid, a bag slack until its gas fills it, a tinted skin by what it holds.
                var fill = ConstructFluid.Held(p, p.D) is { } gas ? (Fluid: (Fluid?)gas, Share: Math.Clamp(ConstructConfig.Get(p, p.D, CfgKey.Fill, 0) / 100f, 0, 1)) : (Fluid: null, Share: 0f);
                var slack = env.Vacuum || p.D.Armour > 8 ? 1f : 0.7f + 0.3f * MathF.Sqrt(fill.Share);
                var bare = p.D.Boxes[0].Color;
                var skin = Vector3.Lerp(bare, fill.Fluid is { } g ? ConstructFluid.Colour(g) : bare, 0.35f);
                gfx.Draw(Matrix4x4.CreateScale(half * 2 * slack) * Matrix4x4.CreateTranslation(centre) * m, skin, 1f);
                n++;
            }
            if (tank && !p.D.IsEnvelope && !dragged && ConstructFluid.Fill(c, i, out var tc, out var th) && ConstructFluid.Held(p, p.D) is { } fluid)
            {
                // The fluid: see-through, clear of the player's walls, with a brighter surface so the level reads.
                th = Vector3.Max(th - new Vector3(0.015f), new Vector3(0.005f));
                QueueGlass(Matrix4x4.CreateScale(th * 2) * Matrix4x4.CreateTranslation(tc) * m, ConstructFluid.Colour(fluid), 0.45f, 0);
                var top = tc + Rot24.Apply(p.Rot, Vector3.UnitY) * Vector3.Dot(th, Vector3.Abs(Rot24.Apply(p.Rot, Vector3.UnitY)));
                var flat = th;
                flat[AxisOf(p.Rot)] = 0.006f;
                QueueGlass(Matrix4x4.CreateScale(flat * 2) * Matrix4x4.CreateTranslation(top) * m, ConstructFluid.Colour(fluid) * 1.25f, 0.7f, 0);
                n += 2;
            }
            // The control box's lamp: its state at a glance.
            var lamp = p.Pos + Rot24.Apply(p.Rot, new Vector3(0, store ? p.D.BoundsMax.Y + 0.012f : 0.115f, 0));
            gfx.Draw(Matrix4x4.CreateScale(0.07f, 0.02f, 0.07f) * Matrix4x4.CreateTranslation(lamp) * m, colour, 0.95f, mode: 2);
            n++;
            if (!VolumesShown && !dragged)
            {
                // Idle: the floor's corners only, faint.
                var floor = centre - Rot24.Apply(p.Rot, new Vector3(0, size.Y * 0.5f, 0));
                var fh = half;
                fh[AxisOf(p.Rot)] = 0.004f;
                Outline(gfx, floor, fh, m, colour, frozen || store ? 0.28f : 0.14f);
                n += 12;
                continue;
            }
            Outline(gfx, centre, half, m, colour, dragged ? 0.9f : 0.6f);
            n += 12;
            if (store && !dragged)
                for (var f = 0; f < 6; f++)
                    if ((p.SealedFaces >> f & 1) == 0)
                    {
                        // An open face: its frame in red (ConstructSeal.cs).
                        var flat = size * 0.5f;
                        flat[f >> 1] = 0.01f;
                        Outline(gfx, p.Pos + Rot24.Apply(p.Rot, ConstructCargo.FaceAt(size, at, f)), Rot24.ApplyHalf(p.Rot, flat), m, VolOpen, 0.85f);
                        n += 12;
                    }
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
