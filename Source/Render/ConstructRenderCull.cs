using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Culling and the far level of detail for the constructs (board item cl01): a body outside the view, or past
/// <see cref="CullDistance"/>, isn't drawn; one past <see cref="LodDistance"/> with more than a few parts is drawn as a
/// single box over its bounds in its main colour (its parts merged), so thousands of constructs cost a sphere test each
/// and a box for each far one. Near ones draw part by part as before.
/// </summary>
public static partial class ConstructRender
{
    public static float LodDistance = 140f;
    public static float CullDistance = 700f;
    public const int LodMinParts = 5;

    private static readonly Vector4[] _planes = new Vector4[6];

    /// <summary>Bodies culled and drawn as one box last frame (the F9 window, the self-test).</summary>
    public static int LastCulled { get; private set; }
    public static int LastLod { get; private set; }

    private static void CullPlanes(in Matrix4x4 m)
    {
        var c1 = new Vector4(m.M11, m.M21, m.M31, m.M41);
        var c2 = new Vector4(m.M12, m.M22, m.M32, m.M42);
        var c3 = new Vector4(m.M13, m.M23, m.M33, m.M43);
        var c4 = new Vector4(m.M14, m.M24, m.M34, m.M44);
        _planes[0] = c4 + c1;
        _planes[1] = c4 - c1;
        _planes[2] = c4 + c2;
        _planes[3] = c4 - c2;
        _planes[4] = c4 + c3;
        _planes[5] = c4 - c3;
        for (var k = 0; k < 6; k++)
        {
            var len = new Vector3(_planes[k].X, _planes[k].Y, _planes[k].Z).Length();
            if (len > 1e-6f)
                _planes[k] /= len;
        }
    }

    /// <summary>0: not drawn (out of view or too far); 1: part by part; 2: one box (far).</summary>
    private static int CullLevel(Vector3 cam, Construct c, Vector3 pos, Quaternion rot)
    {
        var centre = pos + Vector3.Transform(c.Com, rot);
        var r = c.Radius + 0.5f;
        var dist = Vector3.Distance(cam, centre) - r;
        if (dist > CullDistance)
            return 0;
        for (var k = 0; k < 6; k++)
        {
            var p = _planes[k];
            if (p.X * centre.X + p.Y * centre.Y + p.Z * centre.Z + p.W < -r)
                return 0;
        }
        return dist > LodDistance && c.PartCount >= LodMinParts ? 2 : 1;
    }

    /// <summary>A far body as one box over its parts' bounds, in its first part's colour.</summary>
    private static int DrawLod(Renderer gfx, Construct c, in Matrix4x4 body)
    {
        var mn = c.LocalMin;
        var mx = c.LocalMax;
        var colour = new Vector3(0.5f);
        if (c.PartCount > 0 && c.Parts[0].D.Boxes.Length > 0)
            colour = c.Parts[0].D.Boxes[0].Color;
        if (c.Damaged || c.Wreck)
            colour *= 0.4f;
        gfx.Draw(Matrix4x4.CreateScale(Vector3.Max(mx - mn, new Vector3(0.05f))) * Matrix4x4.CreateTranslation((mn + mx) * 0.5f) * body, colour * 0.8f, 1f, mode: 0);
        return 1;
    }
}
