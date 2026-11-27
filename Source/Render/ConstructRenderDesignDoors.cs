using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// A ship design's doors drawn on their own (DesignParts.Doors.cs): a door element moves (an event door settles at a new Pos and Rot, its
/// moving pose is ConstructDoors.DrawPose), and none of that changes the body's StructStamp, so the merged mesh (ConstructRenderDesign.cs)
/// leaves them out and they are drawn here: each door def's look uploaded once in its part's frame, then drawn a door a draw at its part's
/// place now, times its moving pose. A door move costs no merge. Allocates nothing a frame once a body's door list and its defs' buffers are made.
/// </summary>
public static partial class ConstructRender
{
    private sealed class DesignDoorList
    {
        public int Stamp = int.MinValue;
        public int[] Parts = [];
        public int Count;
    }

    private const int DesignDoorLooksMax = 1024; // door looks kept on the GPU (past it they are all let go and made again as they are drawn)
    private static readonly Dictionary<Construct, DesignDoorList> _designDoorLists = new(ReferenceEqualityComparer.Instance);
    private static readonly Dictionary<ConstructPartDef, Renderer.DesignGpu?> _designDoorLooks = new(ReferenceEqualityComparer.Instance);

    /// <summary>Whether a design part is a door drawn by <see cref="DrawDesignDoors"/>, not in the merged mesh.</summary>
    private static bool DesignDoorApart(ConstructPartDef d) => d.Door != null && d.Role == PartRole.Door;

    /// <summary>Draws body <paramref name="c"/>'s design doors at <paramref name="body"/>. Returns the draws made.</summary>
    private static int DrawDesignDoors(Renderer gfx, Construct c, in Matrix4x4 body)
    {
        if (!_designDoorLists.TryGetValue(c, out var list))
            _designDoorLists[c] = list = new DesignDoorList();
        if (list.Stamp != c.StructStamp)
        {
            list.Stamp = c.StructStamp;
            list.Count = 0;
            for (var i = 0; i < c.PartCount; i++)
                if (DesignDoorApart(c.Parts[i].D) && DesignParts.LookOf(c.Parts[i].D) is { Vertices: > 0 })
                {
                    if (list.Count == list.Parts.Length)
                        Array.Resize(ref list.Parts, Math.Max(8, list.Count * 2));
                    list.Parts[list.Count++] = i;
                }
            if (_designDoorLists.Count > 256)
                _designDoorLists.Clear(); // (bodies gone: made again as they are drawn)
        }
        var n = 0;
        for (var k = 0; k < list.Count; k++)
        {
            var i = list.Parts[k];
            if (i >= c.PartCount)
                continue;
            ref var p = ref c.Parts[i];
            if (p.Broken)
                continue;
            var d = p.D;
            if (!_designDoorLooks.TryGetValue(d, out var gpu))
            {
                if (_designDoorLooks.Count >= DesignDoorLooksMax)
                {
                    foreach (var g in _designDoorLooks.Values)
                        if (g != null)
                            gfx.FreeDesign(g);
                    _designDoorLooks.Clear();
                }
                var l = DesignParts.LookOf(d);
                gpu = l == null ? null : gfx.UploadDesign(null, l.Pos, l.Normal, l.Colour, l.Vertices);
                _designDoorLooks[d] = gpu;
            }
            if (gpu == null)
                continue;
            // Its place in the body now (a settled door's Pos and Rot), then its way there while it moves (ConstructDoors.DrawPose, the body's frame).
            var place = Matrix4x4.CreateFromQuaternion(Rot24.Quat(p.Rot)) * Matrix4x4.CreateTranslation(p.Pos);
            if ((c.DoorMovingCount | c.DoorHeldCount) > 0 && c.MovingDoorOf(i) is var door and >= 0)
                place *= ConstructDoors.DrawPose(c, door);
            gfx.DrawDesign(gpu, place * body);
            n++;
        }
        return n;
    }
}
