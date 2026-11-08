using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Per-player tool upgrades on both sides (docs/sandbox/TOOL-UPGRADES.md; the table: <see cref="ToolUpgrades"/>). Each player
/// has a level per <see cref="ToolStat"/>; the server sets them (a module fitted, a saved world's record) and sends them in the
/// hands state, so a client aims, reaches and refuses as the server will. Creative ignores the levels: every stat is at its
/// creative value. A fixed array: nothing allocates.
/// </summary>
public sealed partial class ConstructEdits
{
    /// <summary>Each player's level of each stat (<see cref="ToolUpgrades.Slots"/> columns a player).</summary>
    public readonly byte[] Levels = new byte[256 * ToolUpgrades.Slots];

    public byte Level(byte player, ToolStat s) => Levels[player * ToolUpgrades.Slots + (int)s];

    public void SetLevel(byte player, ToolStat s, byte level) => Levels[player * ToolUpgrades.Slots + (int)s] = level;

    /// <summary>One player's levels (a span of <see cref="ToolUpgrades.Slots"/>).</summary>
    public Span<byte> LevelsOf(byte player) => Levels.AsSpan(player * ToolUpgrades.Slots, ToolUpgrades.Slots);

    /// <summary>A player's value of a stat, with creative given (a client knows creative from the lobby, not from <see cref="InfiniteParts"/>).</summary>
    public float StatOf(byte player, ToolStat s, bool creative) => ToolUpgrades.Current.Value(s, Level(player, s), creative);

    /// <summary>The server's: a player's value of a stat in this game.</summary>
    public float Stat(byte player, ToolStat s) => StatOf(player, s, InfiniteParts);

    /// <summary>Has this player a function (creative: every one)?</summary>
    public bool Has(byte player, ToolStat s, bool creative) => StatOf(player, s, creative) >= 0.5f;

    /// <summary>The heaviest thing this player's physics gun takes (kg).</summary>
    public float LiftOf(byte player, bool creative) => StatOf(player, ToolStat.Lift, creative);

    /// <summary>How hard this player's beam pulls (N): creative's fixed strength, or the lift's share.</summary>
    public float PullOf(byte player, bool creative) => creative ? ConstructGrab.Strength : LiftOf(player, false) * ToolUpgrades.PullPerKg;

    /// <summary>How far this player's beam reaches (m).</summary>
    public float ReachOf(byte player, bool creative) => StatOf(player, ToolStat.Reach, creative);

    /// <summary>How far this player's beam catches something (m): its reach, or twice it with the tractor beam (what it catches is reeled in to its reach).</summary>
    public float CatchOf(byte player, bool creative) => ReachOf(player, creative) * (Has(player, ToolStat.Tractor, creative) ? 2 : 1);

    /// <summary>A timed edit's seconds for this player (the attach tool's fitting speed divides them).</summary>
    public double FitSeconds(byte player, double seconds) => seconds <= 0 ? 0 : seconds / Math.Max(0.1f, Stat(player, ToolStat.Fitter));

    /// <summary>Every level back to the base (a new match, a mode switch; a saved world's record brings theirs back).</summary>
    public void ResetLevels(byte player) => LevelsOf(player).Clear();

    /// <summary>Why this player can't fit the module that loose body <paramref name="c"/> is (null: they can), from what either side knows.</summary>
    public string? UpgradeRefusal(Construct c, byte player, bool creative, out ToolStat stat, out byte level)
    {
        stat = default;
        level = 0;
        if (c.PartCount != 1 || ToolUpgrades.Current.ModuleOf(c.Parts[0].Def) is not { } m)
            return "that isn't a tool upgrade";
        (stat, level) = m;
        if (creative)
            return "creative: every tool is at its best already";
        var have = Level(player, stat);
        var st = ToolUpgrades.Current.Of(stat);
        if (have >= level)
            return st.Function ? $"your {ToolUpgrades.ToolName(st.Tool)} has the {st.Name.ToLowerInvariant()} already" : $"your {ToolUpgrades.ToolName(st.Tool)}'s {st.Name.ToLowerInvariant()} is Mk {have + 1} already";
        return null;
    }

    /// <summary>
    /// Server: F on a loose tool upgrade: it is fitted (the module is used up) and the player's level of its stat rises to the
    /// module's (a higher Mk may skip the ones under it). Null, or why not.
    /// </summary>
    public string? FitUpgrade(ConstructWorld world, byte player, Vector3 eye, Construct c)
    {
        if (!c.Loose || c.GrabbedBy != 255 || c.CarriedBy != 0)
            return "let go of it first";
        if (Reachable(eye, ReachPoint(c, 0, eye), ConstructBuild.UseReach) is { } far)
            return far;
        if (UpgradeRefusal(c, player, InfiniteParts, out var stat, out var level) is { } why)
            return why;
        if (Contested(c, player) is { } contested)
            return contested;
        world.Remove(c);
        SetLevel(player, stat, level);
        var table = ToolUpgrades.Current;
        Notices.Add((player, $"Fitted: {table.Gives(stat, level)}"));
        Done.Add($"player {player}: fitted {table.Of(stat).Name} level {level}");
        return null;
    }

    /// <summary>The fastest a throw sends something (m/s), and the share of a second of the beam's pull it gives a heavy thing.</summary>
    public const float ThrowSpeed = 25f, ThrowPullSeconds = 0.3f;

    /// <summary>
    /// Server: with the throw function, Reload while holding: what the beam holds leaves along the look, as fast as the beam can
    /// push it in <see cref="ThrowPullSeconds"/> (at most <see cref="ThrowSpeed"/>), and is let go. Null, or why not.
    /// </summary>
    public string? Throw(ConstructWorld world, byte player, Vector3 look)
    {
        ref var h = ref Hands[player];
        if (!Has(player, ToolStat.Throw, InfiniteParts))
            return "fit a Kinetic Coil to throw with the physics gun";
        if (h.GrabBody == 0 || h.GrabSeg != 0 || world.Get(h.GrabBody) is not { } c || c.GrabbedBy != player)
            return null; // (a joint held on its own isn't thrown: it stays on its construct)
        if (!float.IsFinite(look.X + look.Y + look.Z) || look.LengthSquared() < 0.5f)
            return null;
        var speed = MathF.Min(ThrowSpeed, PullOf(player, InfiniteParts) / MathF.Max(c.Mass, 1f) * ThrowPullSeconds);
        Release(world, player);
        c.Vel += Vector3.Normalize(look) * speed;
        c.Wake();
        Done.Add($"player {player}: threw construct {c.Id} at {speed:0.0} m/s");
        return null;
    }
}
