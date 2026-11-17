using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// A Manned Manoeuvring Unit on a suited player's back (docs/sandbox/EVA.md): the MMU part's own boxes, latched to the space
/// suit's attach point, and its thrusters' puffs while it fires. The immediate path (PlayerRender) and the scene graph
/// (PlayerRig's <see cref="PackNode"/>) emit the same boxes. Nothing allocates.
/// </summary>
public static class EvaRender
{
    /// <summary>The suit's attach point in the standing torso's frame (the space suit uniform's plate, Cosmetics/uniforms.json).</summary>
    private static readonly Vector3 Attach = new(0, -0.02f, 0.455f);

    private static ConstructPartDef? _mmu;
    private static bool _looked;

    /// <summary>The MMU part, or null when the part files have none.</summary>
    public static ConstructPartDef? MmuDef
    {
        get
        {
            if (!_looked)
            {
                _mmu = ConstructParts.Find("Manned Manoeuvring Unit");
                _looked = true;
            }
            return _mmu;
        }
    }

    /// <summary>Does this player wear an MMU now: on their back, alive, not seated (seated in its dock, the dock's own part is drawn).</summary>
    public static bool Wears(Player p) => (p.Eva & Eva.Mmu) != 0 && p.Alive && p.RenderSeat == SeatKind.None && MmuDef != null;

    /// <summary>The pack's frame (its latch at the suit's attach point, +z away from the back) in the upper body's frame, for the player's stance.</summary>
    public static Matrix4x4 OnBack(Player p)
    {
        var s = BodyModel.Stances[BodyModel.StanceIndex(p.RenderStance)];
        return Matrix4x4.CreateTranslation(Attach) * Matrix4x4.CreateScale(s.TorsoScale) * s.TorsoFrame;
    }

    /// <summary>The pack, and while it fires (<paramref name="burning"/>) a puff off each corner quad; <paramref name="m"/> is the pack frame -> the drawing space.</summary>
    public static void Emit<T>(ref T o, in Matrix4x4 m, bool burning, double time) where T : struct, IToolBoxOut
    {
        if (MmuDef is not { } d)
            return;
        foreach (ref readonly var b in d.Boxes.AsSpan())
            if (!b.Hidden)
                o.Box(b.Model(0, b.Center) * m, b.Color, 0, b.Shape, b.Glass);
        if (!burning)
            return;
        // Cold gas: short pale puffs off the quads, flickering one after another.
        for (var k = 0; k < 4; k++)
        {
            var on = 0.5f + 0.5f * MathF.Sin((float)(time * 31 + k * 1.7));
            if (on < 0.35f)
                continue;
            var x = (k & 1) == 0 ? -0.5f : 0.5f;
            var y = (k & 2) == 0 ? -0.5f : 0.46f;
            var size = 0.06f + 0.1f * on;
            var at = new Vector3(x + MathF.Sign(x) * size * 0.6f, y, 0.17f);
            o.Box(Matrix4x4.CreateScale(size, size * 0.7f, size * 0.7f) * Matrix4x4.CreateTranslation(at) * m, Puff, 0);
        }
    }

    private static readonly Vector3 Puff = new(0.86f, 0.88f, 0.9f);
}
