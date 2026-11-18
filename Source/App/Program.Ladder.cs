using System.Numerics;

namespace CallOfSlop;

public static unsafe partial class Program
{
    private static bool _ladderHands;

    /// <summary>
    /// First person on a ladder (ConstructRideLadder.cs): nothing in your hands, both of them on the rungs where the body's
    /// climb puts them (LadderAnim.cs), drawn in camera space like the gun. Off the ladder, the gun comes back up.
    /// </summary>
    private static bool DrawLadderHands(double now, Vector3 eye, Vector3 fwd, Vector3 camUp)
    {
        if (_game.Local is not { Alive: true, RenderLadder: true } me || me.RenderSeat != SeatKind.None)
        {
            if (_ladderHands)
                _game.EquipUntil = Math.Max(_game.EquipUntil, now + _game.Active.Def.EquipTime); // (off the ladder: the gun comes up)
            _ladderHands = false;
            return false;
        }
        _ladderHands = true;
        // The hands from the feet and the eye from the feet: small numbers, however far out the ship is (render.md, 2026-11-16).
        LadderAnim.Hands(me, LadderAnim.Weight(me), LadderAnim.BodyRot(me), out var right, out var left);
        var view = Matrix4x4.CreateLookAt(Vector3.Zero, fwd, camUp);
        var viewRot = ThirdPersonArms.RotationOf(view);
        var eyeRel = eye - me.RenderPosition;
        HandTarget ToCamera(in HandTarget h) => new(Vector3.Transform(h.Pos - eyeRel, view), Quaternion.Concatenate(h.Rot, viewRot), h.Shape);
        Gfx.ClearDepthBuffer();
        LightGrid.BeginViewModel(World.Map, _worldEye, _worldView, now);
        var fb = _window.FramebufferSize;
        Gfx.SetCamera(Matrix4x4.Identity, Sights.ViewModelProjection(fb.X / (float)Math.Max(fb.Y, 1)));
        Gfx.BeginBatch();
        _vmAnim.Right.Solve(ToCamera(right), Matrix4x4.Identity, Vector3.Zero);
        _vmAnim.Left.Solve(ToCamera(left), Matrix4x4.Identity, Vector3.Zero);
        var armColors = ArmColors.For(me.Color * 0.5f + new Vector3(0.06f));
        _vmAnim.Right.Draw(Gfx, armColors);
        _vmAnim.Left.Draw(Gfx, armColors);
        Gfx.EndBatch();
        return true;
    }
}
