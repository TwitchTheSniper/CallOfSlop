using System.Numerics;

namespace CallOfSlop;

/// <summary>A pilot's gear in the air (PilotGear.cs), as plain boxes over the body: a parachute's dome, a glider's wing, a jetpack's flame.</summary>
public static class CanopyRender
{
    private static readonly Vector3 Silk = new(0.82f, 0.32f, 0.2f), Line = new(0.75f, 0.75f, 0.7f), Sail = new(0.2f, 0.42f, 0.62f), Flame = new(1f, 0.6f, 0.2f);

    private static Matrix4x4 Piece(Vector3 size, Vector3 at, float tilt = 0) =>
        Matrix4x4.CreateScale(size) * Matrix4x4.CreateRotationZ(tilt) * Matrix4x4.CreateTranslation(at);

    /// <summary>Emits it; <paramref name="m"/> is the body's frame (y up from the feet) to the drawing space.</summary>
    public static void Emit<T>(ref T o, byte canopy, in Matrix4x4 m) where T : struct, IToolBoxOut
    {
        switch (canopy)
        {
            case PilotGear.OpenChute:
                o.Box(Piece(new Vector3(3.2f, 0.25f, 3.6f), new Vector3(0, 5.6f, 0)) * m, Silk, 0);
                o.Box(Piece(new Vector3(1.5f, 0.2f, 3.6f), new Vector3(2f, 5.2f, 0), -0.55f) * m, Silk, 0);
                o.Box(Piece(new Vector3(1.5f, 0.2f, 3.6f), new Vector3(-2f, 5.2f, 0), 0.55f) * m, Silk, 0);
                o.Box(Piece(new Vector3(0.03f, 3.6f, 0.03f), new Vector3(1.05f, 3.3f, 0), 0.3f) * m, Line, 0);
                o.Box(Piece(new Vector3(0.03f, 3.6f, 0.03f), new Vector3(-1.05f, 3.3f, 0), -0.3f) * m, Line, 0);
                break;
            case PilotGear.OpenGlider:
                o.Box(Piece(new Vector3(2.4f, 0.1f, 1.2f), new Vector3(1.15f, 2.2f, 0.1f), 0.08f) * m, Sail, 0);
                o.Box(Piece(new Vector3(2.4f, 0.1f, 1.2f), new Vector3(-1.15f, 2.2f, 0.1f), -0.08f) * m, Sail, 0);
                o.Box(Piece(new Vector3(0.06f, 0.9f, 0.06f), new Vector3(0, 1.8f, 0.1f)) * m, Line, 0);
                break;
            case PilotGear.Burning:
                o.Box(Piece(new Vector3(0.18f, 0.7f, 0.18f), new Vector3(0, 0.55f, 0.38f)) * m, Flame, 0);
                break;
        }
    }
}
