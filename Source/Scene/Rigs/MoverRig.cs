using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// A moving platform as a scene graph: the mover frame (its pose at the render tick: origin + yaw, from
/// <see cref="IMoverPose"/>) with one child box per <see cref="MoverPart"/> and a "riders" node. Riders are conceptually
/// children of the frame: gameplay already stores them relative to it (<see cref="MoverPose.ToLocal"/>), and a rider's
/// presentation node parented under <see cref="Riders"/> rides along. A piloted vehicle is the same thing with seats
/// (<see cref="VehicleRig"/>).
/// </summary>
public sealed class MoverRig
{
    public readonly SceneNode Frame;

    /// <summary>Parent for things riding the mover (local = the mover's own frame).</summary>
    public readonly SceneNode Riders;

    public readonly IMoverPose Source;

    public MoverRig(string name, IMoverPose source, MoverPart[] parts)
    {
        Source = source;
        Frame = new SceneNode(name);
        foreach (var p in parts)
            Frame.Add("part", Matrix4x4.CreateScale(p.Half * 2) * Matrix4x4.CreateRotationY(-p.Yaw) * Matrix4x4.CreateTranslation(p.Center), Renderable.Box(p.Color));
        Riders = Frame.Add("riders");
    }

    /// <summary>The frame's matrix for a mover pose (our yaw turns +X towards +Z; CreateRotationY turns the other way).</summary>
    public static Matrix4x4 FrameMatrix(in MoverPose pose) => Matrix4x4.CreateRotationY(-pose.Yaw) * Matrix4x4.CreateTranslation(pose.Origin);

    /// <summary>Poses the frame at the (fractional) server tick.</summary>
    public void Pose(double tick) => Frame.SetLocalMatrix(FrameMatrix(Source.PoseAt(tick)));
}

/// <summary>The movers of one map as rigs (rebuilt when the map changes), drawn through the scene graph.</summary>
public sealed class MoverRigSet
{
    public readonly Map Map;
    public readonly SceneNode Root;
    public readonly MoverRig[] Rigs;
    public readonly int Boxes;

    public MoverRigSet(Map map)
    {
        Map = map;
        Root = new SceneNode("movers");
        Rigs = new MoverRig[map.Movers.Count];
        for (var i = 0; i < Rigs.Length; i++)
        {
            var m = map.Movers[i];
            Rigs[i] = new MoverRig(m.Name.Length > 0 ? m.Name : "mover", m, m.Parts);
            Root.Add(Rigs[i].Frame);
            Boxes += m.Parts.Length;
        }
    }

    public void Pose(double tick)
    {
        foreach (var r in Rigs)
            r.Pose(tick);
    }
}
