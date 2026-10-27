using System.Numerics;

namespace CallOfSlop;

/// <summary>Your gun as drawn, handed to the game so your tracers leave its muzzle (Source/Game/GameMuzzle.cs).</summary>
public static unsafe partial class Program
{
    /// <summary>The world camera's vertical field of view this frame (radians).</summary>
    private static float _worldFov = 1.2f;

    /// <summary>
    /// Records the gun just drawn in your hands: <paramref name="pose"/> is gun space -> view model camera space, or gun
    /// space -> world when <paramref name="inWorld"/> (a manned push cart's gun).
    /// </summary>
    private static void RememberViewGun(WeaponDef def, in Matrix4x4 pose, bool inWorld)
    {
        if (!Matrix4x4.Invert(_worldView, out var inv))
            return;
        _game.ViewGun = new Game.FirstPersonGun { Def = def, Pose = pose, InvView = inv, TanHalfFov = MathF.Tan(_worldFov * 0.5f), InWorld = inWorld };
    }

    /// <summary>The barrels of your last shot (barrel 0 if none is known): the muzzles your flash shows at.</summary>
    private static ulong LocalFlashBarrels(double now)
    {
        var m = _game.FlashBarrels(_game.LocalId, now, 0.1);
        return m != 0 ? m : 1UL;
    }
}
