using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Groundwork for player-piloted vehicles: a vehicle frame posed from an <see cref="IMoverPose"/> (as movers are, so the
/// same carrying, pushing and ray code can serve them), a gravity frame of its own (its +Y is "up" for whoever is aboard),
/// and seats as sockets ("seat_0", "seat_1"...). Sitting a player in a seat parents their presentation node under the
/// seat socket, so they ride along with no per-frame bookkeeping. Nothing in the game uses it yet.
/// </summary>
public sealed class VehicleRig
{
    public readonly SceneNode Frame;
    public readonly IMoverPose Source;
    private readonly List<SceneNode> _seats = new();

    public VehicleRig(string name, IMoverPose source)
    {
        Source = source;
        Frame = new SceneNode(name, NodeFlags.GravityFrame);
    }

    public int SeatCount => _seats.Count;

    /// <summary>Adds a seat socket at <paramref name="position"/> (vehicle space), facing <paramref name="rotation"/>.</summary>
    public SceneNode AddSeat(Vector3 position, Quaternion rotation)
    {
        var s = Frame.AddSocket("seat_" + _seats.Count, position, rotation);
        _seats.Add(s);
        return s;
    }

    public SceneNode Seat(int i) => _seats[i];

    /// <summary>Puts <paramref name="rider"/> in seat <paramref name="i"/> (it then moves with the vehicle).</summary>
    public void Board(SceneNode rider, int i)
    {
        rider.SetParent(_seats[i], keepWorld: false);
        rider.SetLocal(Vector3.Zero, Quaternion.Identity, Vector3.One);
    }

    /// <summary>Takes a rider out, keeping where it is in the world (it becomes a child of <paramref name="newParent"/>).</summary>
    public static void Leave(SceneNode rider, SceneNode? newParent) => rider.SetParent(newParent, keepWorld: true);

    /// <summary>Poses the frame at the (fractional) server tick.</summary>
    public void Pose(double tick) => Frame.SetLocalMatrix(MoverRig.FrameMatrix(Source.PoseAt(tick)));
}
