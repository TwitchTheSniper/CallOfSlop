using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Server: the Press's lid never stops for people (ConstructPressLid.cs). Ten times a second, for each working Press with a bed: a player
/// on the bed between its side frames as the lid slams shut is crushed. The lid's swing is the stroke's own clock, so what a player sees
/// close on them is what the server tests. Nothing runs while no press works; nothing allocates.
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
                if (!c.Parts[core].D.IsPress || c.Parts[core].Broken || !ConstructPress.LidSpace(c, core, out var mid, out var rot, out var lo, out var hi)
                    || Constructs.StoreOf(c, core) is not { } st || !ProductionLines.Working(Constructs, c, core, st, now))
                    continue;
                var into = ConstructPress.StrokeSecondsServer(c, core, st); // (the stroke's own clock: the swing every peer draws)
                if (into - PressCrushEvery / (double)World.TickRate < ConstructPress.ShutSec && into >= ConstructPress.ShutSec)
                    PressCrushPlayers(c, mid, rot, lo, hi, now);
            }
        }
    }

    private void PressCrushPlayers(Construct c, Vector3 mid, byte rot, Vector3 lo, Vector3 hi, double now)
    {
        var un = Quaternion.Conjugate(Rot24.Quat(rot));
        foreach (var p in Players.Values)
        {
            if (!p.Alive)
                continue;
            var at = Vector3.Transform(c.ToLocal(p.Position + new Vector3(0, 0.9f, 0)) - mid, un); // (the body's middle, in the Core's cell frame)
            if (at.X > lo.X && at.X < hi.X && at.Z > lo.Z && at.Z < hi.Z && at.Y > lo.Y - 1f && at.Y < hi.Y)
            {
                Constructs.Events.Add($"player {p.Id} crushed by a press on construct {c.Id}");
                DamagePlayer(p, p, 100000, false, SuicideKillId, now, DamageKind.Melee);
            }
        }
    }
}
