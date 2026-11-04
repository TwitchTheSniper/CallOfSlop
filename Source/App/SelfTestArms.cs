using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>Helpers other sections still use; the section this file held was removed (user, 2026-11-03).</summary>
public static partial class SelfTest
{
    private static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    private static bool Finite(in Matrix4x4 m) =>
        Finite(new Vector3(m.M11, m.M12, m.M13)) && Finite(new Vector3(m.M21, m.M22, m.M23)) && Finite(new Vector3(m.M31, m.M32, m.M33)) && Finite(m.Translation);

    private static ViewModelInput BaseInput(WeaponDef d) =>
        new() { Def = d, Now = 10, Dt = 1f / 60, Reload = -1, Melee = -1, Grounded = true, Eye = new Vector3(0, 1.6f, 0) };
}
