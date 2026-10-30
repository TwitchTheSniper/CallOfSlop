using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The Sandbox backpack on a player's back (ConstructBackpack.cs): the Backpack part's boxes and, in their slots, the
/// parts it holds, drawn on every player in third person (the immediate path, PlayerRender, and the scene graph,
/// PlayerRig's <see cref="PackNode"/>, emit the same boxes). Nothing allocates.
/// </summary>
public static class BackpackRender
{
    /// <summary>The pack's frame (its +Z away from the back) in the upper body's frame, for the player's stance.</summary>
    public static Matrix4x4 OnBack(Player p)
    {
        var s = BodyModel.Stances[BodyModel.StanceIndex(p.RenderStance)];
        var depth = s.Lying ? s.TorsoSize.Y : s.TorsoSize.Z;
        return Matrix4x4.CreateTranslation(0, -0.08f, depth * 0.5f + 0.005f) * s.TorsoFrame;
    }

    /// <summary>Should this player wear one: Sandbox (the hands are known), alive, not seated.</summary>
    public static bool Wears(Player p, out CStore? pack)
    {
        pack = null;
        if (ToolRender.Hands is not { } hands || ConstructEdits.Backpack == null || !p.Alive || p.RenderSeat != SeatKind.None)
            return false;
        pack = hands.Pack(p.Id);
        return true;
    }

    /// <summary>The pack frame and what is in it, through <paramref name="o"/>; <paramref name="m"/> is the pack frame -> the drawing space. Returns the boxes.</summary>
    public static int Emit<T>(ref T o, CStore pack, in Matrix4x4 m) where T : struct, IToolBoxOut
    {
        if (ConstructEdits.Backpack is not { } bp)
            return 0;
        var n = 0;
        foreach (ref readonly var b in bp.Boxes.AsSpan())
        {
            if (b.Hidden)
                continue;
            o.Box(b.Model(0, b.Center) * m, b.Color, 0, b.Shape);
            n++;
        }
        var frame = new CPart { Pos = Vector3.Zero, Rot = 0 };
        for (var k = 0; k < pack.SlotCount && k < bp.Slots.Length; k++)
        {
            if (ConstructParts.Get(pack.Def[k]) is not { } d)
                continue;
            for (var b = 0; b < d.Boxes.Length; b++)
            {
                ref readonly var box = ref d.Boxes[b];
                if (box.Hidden)
                    continue;
                ConstructStorage.ItemBox(frame, bp.Slots[k], d, pack.Rot[k], 0, b, out var c, out _);
                o.Box(box.Model(pack.Rot[k], c) * m, box.Color, 0, box.Shape);
                n++;
            }
        }
        return n;
    }
}

/// <summary>A player rig's backpack node (PlayerRig.cs): emits the pack <see cref="BackpackRender"/> worked out for it.</summary>
public sealed class PackNode : IRenderable
{
    public CStore? Pack;
    public Matrix4x4 Frame;

    public void Emit(DrawList list, SceneNode node)
    {
        if (Pack == null)
            return;
        var o = new ToolListOut(list);
        BackpackRender.Emit(ref o, Pack, Frame);
    }
}
