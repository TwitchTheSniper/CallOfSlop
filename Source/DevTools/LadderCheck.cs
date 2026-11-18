using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// <c>--ladder-check [BLUEPRINT,...]</c>: every vertical ladder (a part with "ladder") on the shipped blueprints that hold one
/// (or the named ones) climbed headless, up from its foot and down from its top, through <see cref="ConstructRide.ClimbLadder"/>
/// as the player's F does; a refusal names the part in the way. A dev tool: it allocates freely.
/// </summary>
public static class LadderCheck
{
    public static int Cli(string[] args)
    {
        var named = args.SkipWhile(a => !a.Equals("--ladder-check", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault();
        var names = named == null || named.StartsWith("--") ? null : named.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (Maps.Find("Proving Ground") is not { } yard)
        {
            Console.WriteLine("no Proving Ground map");
            return 1;
        }
        yard.RestoreProps();
        World.SetMap(yard);
        int bad = 0, ladders = 0;
        foreach (var bp in ConstructBuild.Blueprints)
        {
            if (names != null ? !names.Any(n => n.Equals(bp.Name, StringComparison.OrdinalIgnoreCase))
                : !bp.Steps.Any(st => ConstructParts.Find(st.Part) is { LadderTop: > 0 }))
                continue;
            var w = new ConstructWorld();
            var c = ConstructBuild.BuildOnGround(w, bp, ConstructWorld.Ground(World.Query, 0, 0), 0, out _);
            if (c == null)
            {
                Console.WriteLine($"{bp.Name}: doesn't build");
                bad++;
                continue;
            }
            c.Rebuild();
            c.Pos += new Vector3(0, 30f, 0); // (clear of the yard's ground)
            c.PrevPos = c.Pos;
            c.Rebuild();
            for (var i = 0; i < c.PartCount; i++)
            {
                var d = c.Parts[i].D;
                if (d.LadderTop <= 0)
                    continue;
                ladders++;
                var (up, down) = (Try(c, i, false), Try(c, i, true));
                var ok = up == null && down == null;
                bad += ok ? 0 : 1;
                Console.WriteLine($"{bp.Name}: {d.Name} #{i}: up {up ?? "ok"}, down {down ?? "ok"}");
            }
        }
        Console.WriteLine($"ladder check: {ladders} ladder(s), {bad} refused");
        return bad;
    }

    /// <summary>One climb from the ladder's foot (or its top): null when it starts, else why not.</summary>
    private static string? Try(Construct c, int part, bool fromTop)
    {
        ref var p = ref c.Parts[part];
        var d = p.D;
        var stand = World.PlayerRadius + 0.12f;
        var cx = (d.BoundsMin.X + d.BoundsMax.X) * 0.5f;
        var local = fromTop ? new Vector3(cx, d.LadderTop + 0.02f, d.BoundsMax.Z + stand) : new Vector3(cx, d.BoundsMin.Y + 0.02f, d.BoundsMin.Z - stand);
        c.WorldFrame(p.Seg, out var q, out var t);
        var s = new MoveState { Position = t + Vector3.Transform(p.Pos + Rot24.Apply(p.Rot, local), q) };
        var why = ConstructRide.ClimbLadder(ref s, c, part, World.HeightOf(Stance.Stand));
        if (why == null)
            return null;
        if (!fromTop)
        {
            var top = t + Vector3.Transform(p.Pos + Rot24.Apply(p.Rot, new Vector3(cx, d.LadderTop + 0.02f, d.BoundsMax.Z + stand)), q);
            var by = ConstructRide.BlockedBy(c, part, top, World.HeightOf(Stance.Stand));
            if (by >= 0)
            {
                why += $" (in the way: {c.Parts[by].D.Name} #{by}, top at {c.ToLocal(top)}";
                for (var b = 0; b < c.Parts[by].D.SolidBoxes; b++)
                {
                    c.BoxOf(by, b, out var centre, out var half);
                    if (Vector3.Distance(centre, c.ToLocal(top)) < 2.5f)
                        why += $"; box {centre - half} .. {centre + half}";
                }
                why += ")";
            }
        }
        return why;
    }
}
