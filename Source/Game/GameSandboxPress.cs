using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Server: the Press's clamshell never stops for people (design page drawing 17b; ConstructPressShells.cs). Ten times a second, for
/// each working Press with shells: a player between the shells as their segment slams shut is crushed, and one in a side's swing strip
/// while that side's segment swings is struck and thrown out of it. The swing is the shells' own pose (the stroke's clock), so what a
/// player sees hit them is what the server tests. Nothing runs while no press works; nothing allocates.
/// </summary>
public sealed partial class Game
{
    private const int PressCrushEvery = World.TickRate / 10; // ticks between crush tests (10 Hz, never per frame)
    private const int PressStrikeDamage = 35; // a swinging shell's blow
    private const double PressStrikeGrace = 1.0; // s: one blow a swing at most
    private readonly double[] _pressStruckAt = new double[256];

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
                if (!c.Parts[core].D.IsPress || c.Parts[core].Broken || !ConstructPress.ShellSpace(c, core, out var mid, out var rot, out var lo, out var hi, out var strip)
                    || Constructs.StoreOf(c, core) is not { } st || !ProductionLines.Working(Constructs, c, core, st, now))
                    continue;
                var into = ConstructPress.StrokeSecondsServer(c, core, st); // (the stroke's own clock: the swing every peer draws)
                PressCrushPlayers(c, core, mid, rot, lo, hi, strip, into, into - PressCrushEvery / (double)World.TickRate, now);
            }
        }
    }

    private void PressCrushPlayers(Construct c, int core, Vector3 mid, byte rot, Vector3 lo, Vector3 hi, float strip, double into, double before, double now)
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
            if (at.Z > lo.Z && at.Z < hi.Z && at.Y > lo.Y)
            {
                // Between the shells: crushed as they slam (the phase passed the slam since the last test).
                if (was < ConstructPress.SlamAt && ph >= ConstructPress.SlamAt)
                {
                    Constructs.Events.Add($"player {p.Id} crushed by a press on construct {c.Id}");
                    DamagePlayer(p, p, 100000, false, SuicideKillId, now, DamageKind.Melee);
                }
                continue;
            }
            // In a swing strip while that side's segment is moving (rising to the slam, or falling open): struck and thrown out.
            var side = at.Z <= lo.Z - cell ? -1 : at.Z >= hi.Z + cell ? 1 : 0;
            var outside = side < 0 ? lo.Z - cell - at.Z : at.Z - (hi.Z + cell);
            if (side == 0 || outside > strip || at.Y > lo.Y + strip)
                continue;
            var moving = ph is > 0 and < ConstructPress.OpenTo || ph is > ConstructPress.SlamFrom and < ConstructPress.SlamAt; // (swinging open, or slamming)
            if (!moving || now < _pressStruckAt[p.Id] + PressStrikeGrace)
                continue;
            _pressStruckAt[p.Id] = now;
            var away = Vector3.Transform(Rot24.Apply(rot, new Vector3(0, 0, side)), c.Rot);
            Knock(p, away * 7f + new Vector3(0, 4f, 0)); // (the client hears of it for its prediction; mantling and noclip ignore it)
            Constructs.Events.Add($"player {p.Id} struck by a press shell on construct {c.Id}");
            DamagePlayer(p, p, PressStrikeDamage, false, SuicideKillId, now, DamageKind.Melee);
        }
    }
}
