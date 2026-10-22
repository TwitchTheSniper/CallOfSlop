using System.Numerics;

namespace CallOfSlop;

public sealed partial class Game
{
    /// <summary>
    /// Smoke test only (the frenzy visuals step, Program.FrenzyFx.cs): one cosmetic tracer from every live player through
    /// the normal tracer path (so the frenzy look applies): yours along your aim, each other player's at the ground a few
    /// metres to the side of you. No damage, nothing sent. Returns how many tracers were added.
    /// </summary>
    internal int SmokeFrenzyShots(double now)
    {
        if (Local is not { Alive: true } me)
            return 0;
        var n = 0;
        var aim = World.Forward(LookYaw, LookPitch);
        var dist = World.Query.Raycast(me.RenderEye, aim, World.Range, QueryFilter.Shots);
        AddTracer(MuzzleWorld(me), me.RenderEye + aim * dist, me.WeaponDef, now, -1, me);
        n++;
        var flat = World.Forward(LookYaw, 0);
        var right = World.FlatRight(LookYaw);
        foreach (var p in Players.Values)
        {
            if (p.Id == LocalId || !p.Alive)
                continue;
            var side = (p.Id & 1) == 0 ? 1f : -1f;
            var target = me.RenderPosition + flat * 7f + right * (3f * side) + new Vector3(0, 0.02f, 0);
            var from = MuzzleWorld(p);
            var d = target - from;
            var len = d.Length();
            if (len < 0.5f)
                continue;
            var hit = World.Query.Raycast(from, d / len, len, QueryFilter.Shots);
            AddTracer(from, from + d / len * hit, p.WeaponDef, now, -1, p);
            n++;
        }
        return n;
    }
}
