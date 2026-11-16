namespace CallOfSlop;

/// <summary>The client's side of a craft's radar (ConstructRadar.cs): the radar key in a seat.</summary>
public static partial class Program
{
    /// <summary>Seated on a craft with a radar, the radar key (RadarHud keeps the contacts each frame) locks the contact nearest the crosshair, the next one out, or none.</summary>
    private static void SeatedRadar(Game g, Player me)
    {
        if (g.RadarCraft == null || !Pressed(GameAction.SeatRadar))
            return;
        SandboxAimRay(g, me, out var eye, out var dir);
        AudioSystem.Effect(g.RadarPick(eye, dir) ? Sfx.LockedTone : Sfx.HitTick, 0.5f);
    }
}
