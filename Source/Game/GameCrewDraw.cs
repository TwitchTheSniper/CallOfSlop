using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Crew with no body, drawn as cheap stand-ins at their seats (the user's answer 9, bot-brain.md): a seated figure of four boxes, no animation,
/// no physics, in the renderer's box batch (one draw with the rest), so a ship passing or seen through a scope shows its crew. From
/// <see cref="CrewStandIns"/> (Msg.CrewSeats); a seat a real sitter is in is skipped. At most <see cref="CrewStandInMax"/> a frame, nearest
/// bodies first by the table's order; none past <see cref="CrewStandInRange"/>. Allocates nothing.
/// </summary>
public sealed partial class Game
{
    public const float CrewStandInRange = 2500;
    public const int CrewStandInMax = 600;

    private static readonly Vector3 StandInSuit = new(0.32f, 0.36f, 0.3f), StandInSkin = new(0.78f, 0.62f, 0.5f), StandInHelmet = new(0.2f, 0.22f, 0.24f);

    /// <summary>Draws the stand-ins (any PC; the host's table is its own).</summary>
    public void DrawCrewStandIns(Renderer gfx)
    {
        var cam = gfx.CameraPosition;
        var drawn = 0;
        foreach (var (body, parts) in CrewStandIns)
        {
            if (Constructs.Get(body) is not { } c || c.Wreck || Vector3.DistanceSquared(c.Pos, cam) > (CrewStandInRange + c.Radius) * (CrewStandInRange + c.Radius))
                continue;
            foreach (var part in parts)
            {
                if (part >= c.PartCount || c.Parts[part].SitterPlus1 != 0 || c.Parts[part].Broken || c.DefOf(part).Role != PartRole.Seat)
                    continue;
                ConstructRender.SeatPose(c, part, _alpha, IsServer, out var q, out var feet);
                var frame = Matrix4x4.CreateFromQuaternion(q) * Matrix4x4.CreateTranslation(feet);
                // Seated: thighs forward at the hips, a torso, a head; the seat's -Z is the way they face, +Y up.
                gfx.Draw(Matrix4x4.CreateScale(0.36f, 0.16f, 0.46f) * Matrix4x4.CreateTranslation(0, 0.5f, -0.2f) * frame, StandInSuit, material: Material.Concrete);
                gfx.Draw(Matrix4x4.CreateScale(0.4f, 0.6f, 0.24f) * Matrix4x4.CreateTranslation(0, 0.88f, 0.02f) * frame, StandInSuit, material: Material.Concrete);
                gfx.Draw(Matrix4x4.CreateScale(0.2f, 0.22f, 0.22f) * Matrix4x4.CreateTranslation(0, 1.32f, 0) * frame, StandInSkin, material: Material.Concrete);
                gfx.Draw(Matrix4x4.CreateScale(0.23f, 0.09f, 0.24f) * Matrix4x4.CreateTranslation(0, 1.46f, 0.005f) * frame, StandInHelmet, material: Material.Concrete);
                if (++drawn >= CrewStandInMax)
                    return;
            }
        }
    }
}
