using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Ordnance hit in the air (docs/sandbox/CONSTRUCTS.md, "Cook-offs of ordnance"): a designed bomb or missile, or a bomb part let go from a rack,
/// takes the damage of the rounds that meet it and the blasts that reach it, less its casing's armour, and at its part's health it goes off where it
/// is, as if its fuse had fired (<see cref="Burst"/>: in the blasts' turn). A shell with no warhead, a bomblet or a plain artillery shell isn't hit.
/// Server only; nothing allocates.
/// </summary>
public sealed partial class ConstructWorld
{
    /// <summary>What a shell in the air can take, and its armour: its part's (a designed weapon's casing, a bomb's file). False: it can't be set off.</summary>
    private static bool ShellBody(in CShell s, out float health, out float armour)
    {
        var d = s.Design ?? (s.Kind == ShellKind.Bomb ? s.Look : null);
        health = d?.Health ?? 0;
        armour = d?.Armour ?? 0;
        return d is { Warhead: true } && !s.Landed && s.Damage > 0 && health > 0;
    }

    /// <summary>Server: shell <paramref name="k"/> takes <paramref name="damage"/>; at its health it goes off. True if it did.</summary>
    public bool HurtShell(int k, float damage)
    {
        if ((uint)k >= (uint)ShellCount)
            return false;
        ref var s = ref Shells[k];
        if (!ShellBody(s, out var health, out var armour) || damage - armour <= 0)
            return false;
        s.Hurt += damage - armour;
        if (s.Hurt < health)
            return false;
        Burst(ref s);
        ShellsShotDown++;
        return true;
    }

    /// <summary>Shells set off in the air by a round or a blast (the bench and the perf window).</summary>
    public int ShellsShotDown;

    /// <summary>
    /// Server: a blast of <paramref name="radius"/> at <paramref name="at"/> reaches the shells in the air between <paramref name="from"/> and
    /// <paramref name="to"/> (a massive blast's front, a tick's ring at a time; a plain blast all of it). Damage falls off as the square, as on parts.
    /// </summary>
    public void BlastShells(Vector3 at, float radius, float damage, float from = -1f, float to = -1f)
    {
        if (ShellCount == 0 || radius <= 0 || damage <= 0)
            return;
        var reach = to > 0 ? MathF.Min(to, radius) : radius;
        var r2 = reach * reach;
        var f2 = from > 0 ? from * from : -1f;
        for (var k = 0; k < ShellCount; k++)
        {
            ref var s = ref Shells[k];
            if (s.Landed || s.Design == null && s.Look == null)
                continue;
            var d2 = Vector3.DistanceSquared(s.Pos, at);
            if (d2 >= r2 || d2 < f2)
                continue;
            var f = 1f - MathF.Sqrt(d2) / radius;
            HurtShell(k, damage * f * f);
        }
    }

    /// <summary>
    /// The nearest shell that can be set off along a ray (a capsule along its way, as long as its part and as wide), before <paramref name="maxDist"/>.
    /// Server, for hitscan rounds: the construct guns', flying rounds' and players' (<c>Game.SandboxShellShot</c>).
    /// </summary>
    public bool RayShell(Vector3 o, Vector3 dir, float maxDist, out float dist, out int shell)
    {
        dist = maxDist;
        shell = -1;
        for (var k = 0; k < ShellCount; k++)
        {
            ref var s = ref Shells[k];
            if (s.Landed || s.Design == null && s.Look == null || !ShellBody(s, out _, out _))
                continue;
            var radius = MathF.Max(s.Size.X, s.Size.Y) * 0.5f + 0.1f;
            var half = MathF.Max(0f, s.Size.Z * 0.5f - radius);
            var sp = s.Vel.LengthSquared();
            var axis = sp > 1f ? s.Vel / MathF.Sqrt(sp) : Vector3.Transform(-Vector3.UnitZ, s.Rot);
            // Cheap reject: the ray's nearest point to the shell's centre.
            var rel = s.Pos - o;
            var along = Vector3.Dot(rel, dir);
            var reach = half + radius;
            if (along < -reach || along > dist + reach || (rel - dir * along).LengthSquared() > reach * reach)
                continue;
            // Ray against the capsule's segment: the closest approach of two lines, each held to its stretch.
            var a = s.Pos - axis * half;
            var w = o - a;
            var b = Vector3.Dot(dir, axis);
            var dd = Vector3.Dot(dir, w);
            var e = Vector3.Dot(axis, w);
            var den = 1f - b * b;
            var t = den > 1e-6f ? (b * e - dd) / den : -dd;
            t = Math.Clamp(t, 0f, dist);
            var u = Math.Clamp(e + b * t, 0f, 2f * half);
            t = Math.Clamp(Vector3.Dot(a + axis * u - o, dir), 0f, dist); // (back onto the ray from the segment's point)
            var gap = Vector3.DistanceSquared(o + dir * t, a + axis * u);
            if (gap > radius * radius)
                continue;
            var at = MathF.Max(0f, t - MathF.Sqrt(radius * radius - gap));
            if (at >= dist)
                continue;
            dist = at;
            shell = k;
        }
        return shell >= 0;
    }
}
