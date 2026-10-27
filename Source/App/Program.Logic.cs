using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Drawing map logic (Maps/README.md, "Map logic"; GameLogic.cs): doors (they sink into the floor as they open and rise as
/// they shut; the collision box moves at once), lamps (lit or dark), buttons (a plate on the wall with a cap that dims while
/// it cools down), levers (the handle up or down) and pressure plates (pressed down). Zones, timers and gates aren't drawn.
/// </summary>
public static partial class Program
{
    private static float[] _logicAnim = [];
    private static double _logicAnimLast;

    private static void DrawLogic(double now)
    {
        if (_game.Logic is not { } s)
            return;
        var d = s.Def;
        if (_logicAnim.Length != d.Entities.Count)
            _logicAnim = new float[d.Entities.Count];
        var dt = (float)Math.Clamp(now - _logicAnimLast, 0, 0.1);
        _logicAnimLast = now;
        for (var i = 0; i < d.Entities.Count; i++)
        {
            var e = d.Entities[i];
            var on = s.On(i);
            ref var anim = ref _logicAnim[i];
            anim = on ? MathF.Min(1, anim + dt * 2.5f) : MathF.Max(0, anim - dt * 2.5f);
            var dim = s.Enabled(i) && (s.Flags[i] & LogicState.FlagSpent) == 0 ? 1f : 0.45f;
            switch (e.Kind)
            {
                case LogicKind.Door:
                {
                    if (anim >= 0.999f)
                        break;
                    var b = e.Box;
                    var sink = anim * b.Half.Y * 2;
                    var shown = new Box(b.Center - new Vector3(0, sink / 2, 0), b.Half - new Vector3(0, sink / 2, 0));
                    Gfx.DrawBox(shown, e.Color, material: Material.Metal);
                    // A band across it, so a door reads as a door (not just another wall).
                    var band = new Box(shown.Center with { Y = MathF.Min(shown.Max.Y - 0.15f, b.Min.Y + 1.0f) },
                        new Vector3(MathF.Max(b.Half.X, 0.03f) + 0.01f, 0.06f, MathF.Max(b.Half.Z, 0.03f) + 0.01f));
                    if (band.Center.Y > shown.Min.Y)
                        Gfx.DrawBox(band, new Vector3(0.85f, 0.7f, 0.15f), material: Material.Metal);
                    break;
                }
                case LogicKind.Lamp:
                    Gfx.DrawBox(e.Box, on ? e.Color : e.ColorOff, mode: on ? 2 : 0);
                    break;
                case LogicKind.Button when e.Draw:
                {
                    var rot = Matrix4x4.CreateRotationY(-e.Yaw);
                    var at = Matrix4x4.CreateTranslation(e.At);
                    Gfx.Draw(Matrix4x4.CreateScale(0.28f, 0.28f, 0.06f) * rot * at, new Vector3(0.3f, 0.3f, 0.32f));
                    var cool = (s.Flags[i] & LogicState.FlagCooling) != 0;
                    var cap = cool ? e.Color * 0.35f : e.Color * dim;
                    Gfx.Draw(Matrix4x4.CreateScale(0.14f, 0.14f, cool ? 0.05f : 0.09f) * Matrix4x4.CreateTranslation(0, 0, -0.04f) * rot * at, cap, 1f, mode: cool || dim < 1 ? 0 : 2);
                    break;
                }
                case LogicKind.Lever when e.Draw:
                {
                    var rot = Matrix4x4.CreateRotationY(-e.Yaw);
                    var at = Matrix4x4.CreateTranslation(e.At);
                    Gfx.Draw(Matrix4x4.CreateScale(0.24f, 0.4f, 0.08f) * rot * at, new Vector3(0.28f, 0.28f, 0.3f));
                    var tilt = (anim * 2 - 1) * 0.7f; // up when on
                    var handle = Matrix4x4.CreateScale(0.05f, 0.34f, 0.05f) * Matrix4x4.CreateTranslation(0, 0.17f, 0) * Matrix4x4.CreateRotationX(-tilt)
                                 * Matrix4x4.CreateTranslation(0, 0, -0.07f) * rot * at;
                    Gfx.Draw(handle, e.Color * dim);
                    break;
                }
                case LogicKind.Plate when e.Draw:
                {
                    var b = e.Box;
                    var top = b.Min.Y + 0.06f - anim * 0.035f;
                    Gfx.DrawBox(new Box(new Vector3(b.Center.X, (b.Min.Y + top) / 2, b.Center.Z), new Vector3(b.Half.X, MathF.Max(0.005f, (top - b.Min.Y) / 2), b.Half.Z)),
                        on ? e.Color * 1.25f : e.Color, material: Material.Metal);
                    break;
                }
            }
        }
    }
}
