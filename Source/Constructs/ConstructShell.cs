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
    /// faction's). A shell standing alone (<paramref name="part"/> 0 of its own construct), or one clamped on a Robot Shell Dock
    /// (Karl, 2026-11-17: <paramref name="part"/> is the shell's part in the ship; the caller cuts it free). Null: none
    /// (<paramref name="why"/> says why).
    /// </summary>
    public static Construct? Find(ConstructWorld world, Construct chair, int seat, byte player, out int part, out string why)
    {
        ref var cp = ref chair.Parts[seat];
        var range = cp.D.ShellRange;
        var channel = ChannelOf(cp);
        chair.WorldFrame(cp.Seg, out var q, out var t);
        var at = t + Vector3.Transform(cp.Pos, q);
        Construct? best = null;
        part = -1;
        var near = range * range;
        var fitted = false;
        foreach (var d in world.Bodies)
        {
            if (d.PartCount == 0 || d.Owner != 255 && d.Owner != player && !Factions.Shares(d.Owner, player))
                continue;
            if (d.PartCount > 1)
            {
                // A ship's docked shells: only a ship in reach is looked through, and only when it holds a shell at all.
                if (Vector3.DistanceSquared(d.WorldCom, at) > (range + d.Radius) * (range + d.Radius) || !HasShellPart(d))
                    continue;
                if (d.Dirty)
                    d.Rebuild();
                for (var i = 0; i < d.PartCount; i++)
                {
                    ref var sp = ref d.Parts[i];
                    if (!sp.D.ShellBody)
                        continue;
                    if (sp.Broken || ChannelOf(sp) != channel || !Docked(d, i))
                    {
                        fitted |= !Docked(d, i);
                        continue;
                    }
                    d.WorldFrame(sp.Seg, out var sq, out var st);
                    var f = Vector3.DistanceSquared(st + Vector3.Transform(sp.Pos, sq), at);
                    if (f < near)
                        (near, best, part) = (f, d, i);
                }
                continue;
            }
            ref var p = ref d.Parts[0];
            if (d == chair || !p.D.ShellBody || p.Broken || ChannelOf(p) != channel || ConstructGantry.Holds(world, d.Id))
                continue; // (a shell a Shell Gantry is working on waits for it)
            var g = Vector3.DistanceSquared(d.WorldCom, at);
            if (g < near)
                (near, best, part) = (g, d, 0);
        }
        why = best != null ? "" : fitted ? "a Robot Shell fitted onto something can't be woken: stand it on its own or hang it on a Robot Shell Dock"
            : $"no free Robot Shell on channel {channel} within {range / 1000:0.#} km";
        return best;
    }

    /// <summary>Does dock part <paramref name="dock"/> of <paramref name="c"/> hold something on its dock socket?</summary>
    public static bool DockHolds(Construct c, int dock)
    {
        var d = c.DefOf(dock);
        for (var s = 0; s < d.Sockets.Length; s++)
            if (d.Sockets[s].Name == "dock")
                return c.SocketUsed(dock, s);
        return false;
    }

    /// <summary>Is shell part <paramref name="shell"/> of <paramref name="c"/> clamped on a Robot Shell Dock and on nothing else?</summary>
    public static bool Docked(Construct c, int shell)
    {
        var dock = false;
        for (var e = c.FirstLink(shell); e >= 0; e = c.NextLink(e))
        {
            var o = c.DefOf(c.OtherEnd(e));
            if (!o.ToolDock || !o.ShellBodyFits)
                return false;
            dock = true;
        }
        return dock;
    }

    private static bool HasShellPart(Construct d)
    {
        for (var i = 0; i < d.PartCount; i++)
            if (d.Parts[i].D.ShellBody)
                return true;
        return false;
    }
}
