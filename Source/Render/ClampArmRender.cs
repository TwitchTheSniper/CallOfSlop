using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Station berth clamp arms' moving half (ConstructClampArms.cs): the turning yoke, five telescoping boom sections and the clamp head,
/// drawn from their part files (Constructs/Parts/SpaceStations/ClampArm, the space_station generator) where each arm's wrist is. A
/// client steps the arms itself each frame (a host's tick already does). Drawn within <see cref="DrawNear"/>. Allocates nothing.
/// </summary>
public static class ClampArmRender
{
    public const float DrawNear = 3000f;
    private const float Cell = 0.125f, Tail = 1.5f, Sect = 13f, MinSlide = 0.15f; // (the generator's P offset and boom numbers: SpaceStationClampArm.cs)
    private static ConstructPartDef? _yoke, _head, _seek, _lock;
    private static readonly ConstructPartDef?[] Boom = new ConstructPartDef?[5];
    private static bool _found;
    private static double _last;

    public static void Draw(Renderer gfx, ConstructWorld world)
    {
        var now = ConstructConveyor.Clock;
        var dt = (float)Math.Clamp(now - _last, 0, 0.1);
        _last = now;
        foreach (var st in world.Bodies)
        {
            if (!st.IsStation || ConstructClampArms.Of(st) is not { } set)
                continue;
            if (Vector3.DistanceSquared(gfx.CameraPosition, st.WorldCom) > (DrawNear + st.Radius) * (DrawNear + st.Radius))
                continue;
            if (Environment.TickCount64 - set.HostStepped > 500)
                ConstructClampArms.Step(world, st, dt, null, host: false); // (a client's arms: the host's tick steps its own)
            if (!Find())
                return;
            for (var k = 0; k < set.N; k++)
                DrawArm(gfx, st, set.Arms[k]);
        }
    }

    private static bool Find()
    {
        if (_found)
            return _yoke != null;
        _found = true;
        _yoke = ConstructParts.Find("Station Clamp Arm Yoke");
        _head = ConstructParts.Find("Station Clamp Arm Head");
        _seek = ConstructParts.Find("Station Clamp Arm Head Seek");
        _lock = ConstructParts.Find("Station Clamp Arm Head Lock");
        for (var i = 0; i < Boom.Length; i++)
            Boom[i] = ConstructParts.Find($"Station Clamp Arm Boom {i + 1}");
        if (_yoke == null || _head == null || _seek == null || _lock == null || Array.IndexOf(Boom, null) >= 0)
        {
            Console.WriteLine("[station] clamp arm pieces missing (Constructs/Parts/SpaceStations/ClampArm): the arms aren't drawn");
            _yoke = null;
        }
        return _yoke != null;
    }

    private static void DrawArm(Renderer gfx, Construct st, in ClampArm a)
    {
        if (a.Part >= st.PartCount || st.Parts[a.Part].Broken)
            return;
        var d = st.DefOf(a.Part);
        Freeform.PartWorld(st, a.Part, out var pos, out var rot);
        var frame = Matrix4x4.CreateFromQuaternion(rot) * Matrix4x4.CreateTranslation(pos);
        var shoulder = d.ClampShoulder;
        var gap = a.Wrist - shoulder;
        var len = MathF.Max(1f, gap.Length());
        var u = gap / len;
        var yaw = MathF.Atan2(u.X, u.Z);
        var pitch = MathF.Asin(Math.Clamp(u.Y, -1f, 1f));
        var cell = Matrix4x4.CreateTranslation(Cell, Cell, Cell);
        var turn = Matrix4x4.CreateRotationY(yaw) * Matrix4x4.CreateTranslation(shoulder) * frame;
        Pieces(gfx, _yoke!, cell * turn);
        var boom = Matrix4x4.CreateRotationX(-pitch) * turn;
        var slide = MathF.Max(MinSlide, (len + Tail - Sect) / (Boom.Length - 1));
        for (var i = 0; i < Boom.Length; i++)
        {
            var s0 = i == Boom.Length - 1 ? len - Sect : -Tail + i * slide;
            Pieces(gfx, Boom[i]!, cell * Matrix4x4.CreateTranslation(0, 0, s0) * boom);
        }
        var up = Vector3.Normalize(a.Out);
        var q = Quaternion.Identity;
        var dot = Vector3.Dot(Vector3.UnitY, up);
        if (dot < -0.9999f)
            q = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI);
        else if (dot < 0.9999f)
            q = Quaternion.Normalize(Quaternion.CreateFromAxisAngle(Vector3.Normalize(Vector3.Cross(Vector3.UnitY, up)), MathF.Acos(dot)));
        var head = a.Holds ? _lock! : a.Ship != 0 ? _seek! : _head!;
        Pieces(gfx, head, cell * Matrix4x4.CreateFromQuaternion(q) * Matrix4x4.CreateTranslation(a.Wrist) * frame);
    }

    private static void Pieces(Renderer gfx, ConstructPartDef d, in Matrix4x4 m)
    {
        for (var bx = 0; bx < d.Boxes.Length; bx++)
        {
            ref readonly var box = ref d.Boxes[bx];
            if (!box.Hidden)
                gfx.Draw(box.Model(0, box.Center) * m, box.Color, mode: box.Glow ? 2 : 0, shape: box.Shape); // (lamps and the pad's ring glow)
        }
    }
}
