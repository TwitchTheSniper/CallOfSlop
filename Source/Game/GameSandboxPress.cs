using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Server: the Press's round dies never stop for people (ConstructPressShells.cs). Ten times a second, for each working Press with
/// housings: a player on the bed between them as their die segment closes is crushed. The dies' swing is the stroke's own clock, so
/// what a player sees close on them is what the server tests. (An open die stands in its housing: nobody beside the press is struck.) Nothing runs while no press works; nothing allocates.
/// </summary>
public sealed partial class Game
{
    private const int PressCrushEvery = World.TickRate / 10; // ticks between crush tests (10 Hz, never per frame)

    private void SandboxPressCrush(double now)
    {
        if (ServerTick % PressCrushEvery != 0 || Players.Count == 0)
            return;
        foreach (var c in Constructs.Bodies)
        {
            if (c.MachineAsmCount == 0)
                continue;
            for (var a = 0; a < c.MachineAsmCount; a++)
            {
                var core = c.MachineAsms[a].Core;
                if (!c.Parts[core].D.IsPress || c.Parts[core].Broken || !ConstructPress.ShellSpace(c, core, out var mid, out var rot, out var lo, out var hi, out _)
                    || Constructs.StoreOf(c, core) is not { } st || !ProductionLines.Working(Constructs, c, core, st, now))
                    continue;
                var into = ConstructPress.StrokeSecondsServer(c, core, st); // (the stroke's own clock: the swing every peer draws)
                PressCrushPlayers(c, core, mid, rot, lo, hi, into, into - PressCrushEvery / (double)World.TickRate, now);
            }
        }
    }

    private void PressCrushPlayers(Construct c, int core, Vector3 mid, byte rot, Vector3 lo, Vector3 hi, double into, double before, double now)
    {
        var un = Quaternion.Conjugate(Rot24.Quat(rot));
        var cell = c.Parts[core].D.BlockCell;
        foreach (var p in Players.Values)
        {
            if (!p.Alive)
                continue;
            var at = Vector3.Transform(c.ToLocal(p.Position + new Vector3(0, 0.9f, 0)) - mid, un); // (the body's middle, in the Core's cell frame)
            if (at.X < lo.X || at.X > hi.X || at.Y < lo.Y - cell || at.Y > hi.Y)
                continue;
            var index = Math.Clamp((int)MathF.Floor((at.X - lo.X) / cell), 0, 63);
            double ph = ConstructPress.Phase(index, into), was = ConstructPress.Phase(index, before);
            // On the bed between the housings: crushed as the dies close (the phase passed the slam since the last test).
            if (at.Z > lo.Z && at.Z < hi.Z && at.Y > lo.Y && was < ConstructPress.SlamAt && ph >= ConstructPress.SlamAt)
            {
                Constructs.Events.Add($"player {p.Id} crushed by a press on construct {c.Id}");
                DamagePlayer(p, p, 100000, false, SuicideKillId, now, DamageKind.Melee);
            }
        }
    }
}
