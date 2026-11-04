using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Cargo volumes with the configure tool (decision 21; docs/sandbox/CONSTRUCTS.md "Cargo volumes"; the rules are
/// ConstructCargo.cs). With the configure tool out, every cargo volume near you shows its box with a handle on each face:
/// hold Fire on a handle and drag to move that face (snapped to the construct's 10 cm grid, the opposite face stays);
/// let go to set it. R on a volume's control box starts drawing a new box: hold Fire on the construct for one corner,
/// drag to the other, let go (the height stays). Fire on the control box itself opens its settings panel as for any
/// part (the numeric size). Each change goes to the server as the configure tool's ordinary request (CActKind.Configure):
/// nothing new on the wire.
/// </summary>
public static partial class Program
{
    private static ushort _cvBody; // the volume being dragged (0: none) ...
    private static int _cvPart = -1, _cvFace = -1; // ...its part, and the face (-1 with _cvDrawing: a new box being drawn)
    private static bool _cvDrawing, _cvArmed; // drawing a box (armed: R pressed, waiting for Fire)
    private static Vector3 _cvCorner; // drawing: the first corner (part frame)
    private static readonly byte[] _cvSlots = new byte[6];
    private static readonly float[] _cvValues = new float[6];
    private static readonly ushort[] _cvParts = new ushort[1];

    /// <summary>The handle nearest the crosshair within reach (a face of a volume of a construct near the eye), or false.</summary>
    private static bool CargoHandle(ConstructWorld w, Vector3 eye, Vector3 dir, out Construct? body, out int part, out int face)
    {
        body = null;
        part = face = -1;
        var best = 0.16f; // (metres off the look ray)
        foreach (var c in w.Bodies)
        {
            if (Vector3.DistanceSquared(c.WorldCom, eye) > (ConstructConfig.Reach + c.Radius) * (ConstructConfig.Reach + c.Radius))
                continue;
            for (var i = 0; i < c.PartCount; i++)
            {
                if (!ConstructCargo.HasBox(c.Parts[i].D))
                    continue;
                ConstructCargo.Settings(c.Parts[i], c.Parts[i].D, out var size, out var at);
                for (var f = 0; f < 6; f++)
                {
                    var p = ConstructCargo.FromPart(c, i, ConstructCargo.FaceAt(size, at, f));
                    var along = Vector3.Dot(p - eye, dir);
                    if (along <= 0 || along > ConstructConfig.Reach)
                        continue;
                    var off = Vector3.Distance(p, eye + dir * along);
                    if (off < best)
                    {
                        best = off;
                        body = c;
                        part = i;
                        face = f;
                    }
                }
            }
        }
        return body != null;
    }

    /// <summary>The configure tool's cargo volume handles (before its panel input). True: it took this frame's keys.</summary>
    private static bool SandboxCargoInput(Game g, Player me, Vector3 eye, Vector3 dir, bool aimed, in ConstructHit hit)
    {
        ConstructRender.VolumesShown = true;
        // A drag in progress: follows the look until Fire is let go, then asks the server.
        if (_cvBody != 0)
        {
            if (g.Constructs.Get(_cvBody) is not { } c || _cvPart >= c.PartCount || !ConstructCargo.HasBox(c.DefOf(_cvPart)) || CargoStamp(c, _cvPart) != _cvStamp)
            {
                CargoReset();
                return false;
            }
            var d = c.DefOf(_cvPart);
            ConstructCargo.Settings(c.Parts[_cvPart], d, out var size0, out var at0);
            Vector3 size, at;
            if (_cvDrawing)
            {
                var corner = aimed && hit.Body == c ? ConstructCargo.ToPart(c, _cvPart, hit.Point) : PlaneHit(c, _cvPart, eye, dir, _cvCorner.Y);
                ConstructCargo.DragOut(_cvCorner, corner, size0.Y, out size, out at, ConstructCargo.SideLimit(d));
            }
            else
                ConstructCargo.DragFace(size0, at0, _cvFace, ConstructCargo.ToPart(c, _cvPart, eye), Vector3.Normalize(ConstructCargo.ToPart(c, _cvPart, eye + dir) - ConstructCargo.ToPart(c, _cvPart, eye)), out size, out at, ConstructCargo.SideLimit(d));
            ConstructRender.VolumeDragBody = c;
            ConstructRender.VolumeDragPart = _cvPart;
            ConstructRender.VolumeDragSize = size;
            ConstructRender.VolumeDragAt = at;
            g.SandboxLookText = $"{d.Name}: {size.X:0.0} x {size.Y:0.0} x {size.Z:0.0} m{CargoHolds(c, _cvPart, d, size, at)}";
            if (!Down(GameAction.Fire))
            {
                if (ConstructCargo.BoxSlots(d, _cvSlots))
                {
                    _cvValues[0] = size.X;
                    _cvValues[1] = size.Y;
                    _cvValues[2] = size.Z;
                    _cvValues[3] = at.X;
                    _cvValues[4] = at.Y;
                    _cvValues[5] = at.Z;
                    _cvParts[0] = (ushort)_cvPart;
                    g.SandboxConfigure(c.Id, _cvParts, _cvSlots, _cvValues);
                }
                CargoReset();
            }
            return true;
        }
        ConstructRender.VolumeDragBody = null;
        // R on a volume's control box: draw a new box with the next Fire.
        if (aimed && ConstructCargo.HasBox(hit.Body.DefOf(hit.Part)) && Pressed(GameAction.Reload))
        {
            _cvArmed = !_cvArmed;
            _cvBody = 0;
            _cvPart = hit.Part;
            _cvStamp = CargoStamp(hit.Body, hit.Part);
            _cvArmedBody = hit.Body.Id;
            g.SandboxSay(_cvArmed ? $"Drawing a {hit.Body.DefOf(hit.Part).Name}: hold {SandboxKeys.Fire} and drag, {SandboxKeys.Reload}: cancel" : "drawing cancelled");
            return true;
        }
        if (_cvArmed)
        {
            if (g.Constructs.Get(_cvArmedBody) is not { } ac || _cvPart >= ac.PartCount || !ConstructCargo.HasBox(ac.DefOf(_cvPart)) || CargoStamp(ac, _cvPart) != _cvStamp)
            {
                _cvArmed = false;
                return false;
            }
            g.SandboxLookText = aimed && hit.Body == ac ? SandboxKeys.CargoDraw : SandboxKeys.CargoAim;
            if (Pressed(GameAction.Fire) && !_swallowClick && aimed && hit.Body == ac)
            {
                _cvArmed = false;
                _cvDrawing = true;
                _cvBody = ac.Id;
                _cvFace = -1;
                _cvCorner = ConstructCargo.ToPart(ac, _cvPart, hit.Point);
            }
            return true;
        }
        if (!CargoHandle(g.Constructs, eye, dir, out var hb, out var hp, out var hf))
        {
            ConstructRender.VolumeHotBody = null;
            return false;
        }
        ConstructRender.VolumeHotBody = hb;
        ConstructRender.VolumeHotPart = hp;
        ConstructRender.VolumeHotFace = hf;
        var why = g.SandboxConfigRefusal(hb!, me.Id);
        g.SandboxLookText = why ?? SandboxKeys.Line(hb!.DefOf(hp).Name, null, null, $"hold {SandboxKeys.Fire}: drag this face");
        if (Pressed(GameAction.Fire) && !_swallowClick)
        {
            if (why != null)
                g.SandboxSay(why);
            else
            {
                _cvBody = hb!.Id;
                _cvPart = hp;
                _cvStamp = CargoStamp(hb, hp);
                _cvFace = hf;
                _cvDrawing = false;
            }
        }
        return true;
    }

    private static ushort _cvArmedBody;
    private static int _cvStamp; // (a part index is checked against the construct as it is when used: a client's copy has no part tags)

    private static int CargoStamp(Construct c, int part) => HashCode.Combine(c.Parts[part].Def, c.Parts[part].Pos);

    /// <summary>A Storage Volume being dragged: what that box would hold, inside the construct as it stands.</summary>
    private static string CargoHolds(Construct c, int part, ConstructPartDef d, Vector3 size, Vector3 at)
    {
        if (!d.IsVolumeStore)
            return "";
        ConstructStoreVolume.Usable(c, part, size, at, out var min, out var max);
        return $", holds {ConstructStoreVolume.UnitsFor(min, max)} units";
    }

    /// <summary>Where the look ray meets the volume's level plane at height <paramref name="y"/> (part frame), for drawing past the construct's edge.</summary>
    private static Vector3 PlaneHit(Construct c, int part, Vector3 eye, Vector3 dir, float y)
    {
        var o = ConstructCargo.ToPart(c, part, eye);
        var d = ConstructCargo.ToPart(c, part, eye + dir) - o;
        if (MathF.Abs(d.Y) < 1e-4f)
            return o + d * 4f;
        var t = Math.Clamp((y - o.Y) / d.Y, 0, ConstructConfig.Reach);
        return o + d * t;
    }

    private static void CargoReset()
    {
        _cvBody = 0;
        _cvPart = _cvFace = -1;
        _cvDrawing = _cvArmed = false;
        ConstructRender.VolumeDragBody = null;
    }
}
