using System.Numerics;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>
/// Robot shells (docs/sandbox/CONSTRUCTS.md, "Robot shells"; Karl, 2026-11-08). A <b>Robot Shell</b> is a part standing on its
/// own (a construct of that one part); a <b>Shell Control Chair</b> is a seat with a range and a channel. A player who sits in
/// the chair wakes in the nearest free shell on its channel within range that is theirs or nobody's: the game moves the player
/// onto the shell and takes the shell's construct away while they wear it (Game/GameRobotShell.cs), so a shell is played as
/// a player is, with every tool and seat. This file is the parts' side: the part file keys, and which shell a chair reaches.
/// </summary>
public static class ConstructShell
{
    /// <summary>The part files' <c>"shell"</c> key: <c>{"body": true}</c> on a Robot Shell, <c>{"range": metres}</c> on a chair (a seat).</summary>
    public static void Parse(ConstructPartDef d, JsonElement root, string file)
    {
        if (ConstructParts.Opt(root, "shell") is not { } s)
            return;
        ConstructParts.Known(s, ["body", "range"], $"{file}, shell");
        d.ShellBody = ConstructParts.Opt(s, "body") is { ValueKind: JsonValueKind.True };
        d.ShellRange = ConstructParts.Opt(s, "range") is { } g ? ConstructParts.Num(g, file, "shell.range") : 0;
        ConstructParts.Range(d.ShellRange, 0, 20000, file, "shell.range");
        if (d.ShellBody == d.ShellRange > 0)
            throw new ConstructPartException($"{file}: \"shell\" is a body (\"body\": true) or a chair (\"range\")");
        if (d.ShellRange > 0 && d.Role != PartRole.Seat)
            throw new ConstructPartException($"{file}: a shell chair (\"shell\": {{\"range\"}}) must be a seat");
    }

    /// <summary>Is this seat a shell chair?</summary>
    public static bool IsChair(Construct c, int seat) => (uint)seat < (uint)c.PartCount && c.Parts[seat].D.ShellRange > 0;

    public static int ChannelOf(in CPart p) => (int)ConstructConfig.Get(p, p.D, CfgKey.Channel, 1);

    /// <summary>Is this construct a shell that can be woken: one Robot Shell part standing by itself, not wrecked?</summary>
    public static bool Wakeable(Construct d) => d.PartCount == 1 && d.Parts[0].D.ShellBody && !d.Parts[0].Broken && !d.Dirty;

    /// <summary>
    /// The nearest shell a sitter of this chair may wake in: on its channel, within its range, theirs or nobody's (or their
    /// faction's). Null: none (<paramref name="why"/> says why).
    /// </summary>
    public static Construct? Find(ConstructWorld world, Construct chair, int seat, byte player, out string why)
    {
        ref var cp = ref chair.Parts[seat];
        var range = cp.D.ShellRange;
        var channel = ChannelOf(cp);
        chair.WorldFrame(cp.Seg, out var q, out var t);
        var at = t + Vector3.Transform(cp.Pos, q);
        Construct? best = null;
        var near = range * range;
        var fitted = false;
        foreach (var d in world.Bodies)
        {
            if (d == chair || d.PartCount == 0)
                continue;
            if (d.PartCount > 1)
            {
                if (!fitted && Vector3.DistanceSquared(d.WorldCom, at) < near && HasShellPart(d))
                    fitted = true;
                continue;
            }
            ref var p = ref d.Parts[0];
            if (!p.D.ShellBody || p.Broken || ChannelOf(p) != channel)
                continue;
            if (d.Owner != 255 && d.Owner != player && !Factions.Shares(d.Owner, player))
                continue;
            var f = Vector3.DistanceSquared(d.WorldCom, at);
            if (f < near)
            {
                near = f;
                best = d;
            }
        }
        why = best != null ? "" : fitted ? "a Robot Shell fitted onto something can't be woken: stand it on its own"
            : $"no free Robot Shell on channel {channel} within {range / 1000:0.#} km";
        return best;
    }

    private static bool HasShellPart(Construct d)
    {
        for (var i = 0; i < d.PartCount; i++)
            if (d.Parts[i].D.ShellBody)
                return true;
        return false;
    }
}
