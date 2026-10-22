using System.Numerics;

namespace CallOfSlop;

/// <summary>One line of the killer's centre-screen notice: "KILLED Ash" plus tags like "HEADSHOT  DOUBLE KILL".</summary>
/// <param name="Time">Clock time of the kill.</param>
/// <param name="Victim">The victim's name (from the network: draw it with ImGuiSafe).</param>
/// <param name="Color">The victim's colour (team colour in team modes, else their player colour).</param>
/// <param name="Tags">Extra tags, already joined ("" for none).</param>
/// <param name="Multi">Which kill of the multi-kill chain this was (1 = the first).</param>
public readonly record struct KillNotice(double Time, string Victim, Vector3 Color, string Tags, int Multi);

/// <summary>
/// The local player's kill notices (CoD style, under the crosshair). Kills within <see cref="ChainWindow"/> seconds of each
/// other form a multi-kill chain: their lines stack and stay up together, and the newest gets "DOUBLE KILL", "TRIPLE KILL"...
/// The whole stack holds for <see cref="Hold"/> seconds after the last kill, then fades over <see cref="Fade"/>.
/// Pure (no ImGui, no network), so the self-test can drive it.
/// </summary>
public sealed class KillNoticeList
{
    /// <summary>Seconds the stack stays fully visible after the newest kill, then how long it fades.</summary>
    public const double Hold = 1.6, Fade = 0.6;

    /// <summary>A kill within this many seconds of the previous one continues the multi-kill chain.</summary>
    public const double ChainWindow = 2.5;

    /// <summary>Most lines on screen at once (the oldest of a long chain drop off).</summary>
    public const int MaxLines = 4;

    /// <summary>Gun kills at least this far away get "LONGSHOT 45 m".</summary>
    public const float LongshotMetres = 40f;

    /// <summary>The lines of the current chain, oldest first.</summary>
    public readonly List<KillNotice> Lines = new();

    private double _lastKill = -100;
    private int _multi;

    /// <summary>Kills in the current chain so far.</summary>
    public int Multi => _multi;

    /// <summary>Adds a kill. Starts a new chain (clearing the old lines) if the last kill was too long ago.</summary>
    public KillNotice Add(double now, string victim, Vector3 color, KillKind kind, bool headshot, float distance, string weapon,
        int streak = 0, bool revenge = false)
    {
        if (now - _lastKill > ChainWindow || now < _lastKill)
        {
            _multi = 0;
            Lines.Clear();
        }
        _multi++;
        _lastKill = now;
        var notice = new KillNotice(now, victim, color, Tags(kind, headshot, distance, weapon, _multi, streak, revenge), _multi);
        Lines.Add(notice);
        while (Lines.Count > MaxLines)
            Lines.RemoveAt(0);
        return notice;
    }

    /// <summary>Opacity of the whole stack: 1 while holding, then fading to 0.</summary>
    public float Alpha(double now)
    {
        if (Lines.Count == 0)
            return 0;
        var age = now - _lastKill;
        return age < 0 ? 1f : (float)Math.Clamp(1 - (age - Hold) / Fade, 0, 1);
    }

    /// <summary>Forgets everything (new match).</summary>
    public void Clear()
    {
        Lines.Clear();
        _multi = 0;
        _lastKill = -100;
    }

    /// <summary>The tag line for a kill, e.g. "HEADSHOT  LONGSHOT 45 m  DOUBLE KILL". ASCII only.</summary>
    public static string Tags(KillKind kind, bool headshot, float distance, string weapon, int multi, int streak, bool revenge)
    {
        var parts = new List<string>(4);
        switch (kind)
        {
            case KillKind.Melee:
                parts.Add("MELEE");
                break;
            case KillKind.Throwable or KillKind.Streak when weapon.Length > 0:
                parts.Add(weapon.ToUpperInvariant()); // "FRAG GRENADE", "SENTRY TURRET", "MORTAR BARRAGE"
                break;
        }
        if (headshot)
            parts.Add("HEADSHOT");
        if (kind == KillKind.Gun && distance >= LongshotMetres && float.IsFinite(distance))
            parts.Add($"LONGSHOT {distance:0} m");
        if (multi >= 2)
            parts.Add(multi switch { 2 => "DOUBLE KILL", 3 => "TRIPLE KILL", 4 => "QUAD KILL", _ => $"MULTI KILL x{multi}" });
        if (revenge)
            parts.Add("REVENGE");
        if (streak >= 5 && streak % 5 == 0)
            parts.Add($"{streak} KILL STREAK");
        return string.Join("  ", parts);
    }
}

public sealed partial class Game
{
    /// <summary>The local player's kill notices (drawn by Ui.DrawKillNotices). Not used in Zombies.</summary>
    public readonly KillNoticeList KillNotices = new();

    private double _revengeTakenFor = -1; // LastDeath.Time already avenged

    /// <summary>
    /// Both sides, from <see cref="RecordKill"/> (host: the damage path; clients: the replicated kill message): when the
    /// local player killed someone else, adds a kill notice naming the victim. Call after the streak counts are updated.
    /// </summary>
    private void NoteLocalKill(in KillPacket kill)
    {
        if (kill.Killer != LocalId || kill.Victim == LocalId || kill.Killer == kill.Victim || !Module.ShowsKillNotices)
            return;
        var victim = Players.GetValueOrDefault(kill.Victim);
        var kind = KillInfo.KindOf(kill.Killer, kill.Victim, kill.KillId);
        var distance = -1f;
        var from = kill.Source ?? Local?.Eye;
        if (victim != null && from is { } f)
            distance = Vector3.Distance(f, IsServer ? victim.Eye : victim.RenderEye);
        var revenge = LastDeath is { } death && death.KillerId == kill.Victim && death.Kind != KillKind.Self && death.Time != _revengeTakenFor;
        if (revenge)
            _revengeTakenFor = LastDeath!.Time;
        var streak = _deathCamStreaks.GetValueOrDefault(kill.Killer);
        KillNotices.Add(Brutal.GlfwApi.Glfw.Time, NameOf(kill.Victim), victim?.Color ?? new Vector3(1f, 0.35f, 0.3f), kind, kill.Headshot, distance,
            KillInfo.NameFor(kill.KillId), streak, revenge);
    }

    /// <summary>
    /// Smoke test (single player): the local player kills the nearest living bot with a headshot through the normal
    /// damage path, so the kill notice shows. Returns a line for the log.
    /// </summary>
    public string SmokeKillNearestBot(double now)
    {
        if (!IsServer || Local is not { Alive: true } me)
            return "skipped (no living local player)";
        Player? best = null;
        foreach (var p in Players.Values)
            if (p.Bot != null && p.Alive && (best == null || Vector3.DistanceSquared(p.Position, me.Position) < Vector3.DistanceSquared(best.Position, me.Position)))
                best = p;
        if (best == null || !DebugKill(me, best, me.Weapon, true, now))
            return "skipped (no bot to kill)";
        var n = KillNotices.Lines.Count > 0 ? KillNotices.Lines[^1] : default;
        return $"killed {NameOf(best.Id)}: notice 'KILLED {n.Victim}' [{n.Tags}], {KillNotices.Lines.Count} line(s)";
    }

    /// <summary>Self-test: feeds a packet (with its message id) to this game as if it arrived from the network.</summary>
    public void SelfTestPacket(ReadOnlySpan<byte> packet, double now) => OnPacket(default, packet, now);
}
