using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// What the player has learnt, and the moment they learn it (docs/story/STORY.md "Technologies"): the Logbook's Technologies
/// page (each story faction's technologies, learnt or locked, who teaches them and at what standing, then the player's own tool
/// upgrades), and a short celebration in the middle of the screen when a faction teaches them something or they fit a tool
/// upgrade. Both read what every peer has: the faction table's per-player masks (<see cref="FactionTable.PlayerLearnt"/>)
/// and the hands state's tool levels, so each player in co-op sees their own. The watcher compares a few bytes a frame and
/// only makes strings when something is learnt; the celebration waits out the opening, a story ride (a cinematic), the
/// workbench and the menus, and plays one at a time. Nothing is drawn or made while there is nothing to show.
/// </summary>
public sealed partial class Ui
{
    /// <summary>How long one celebration stays (s), its rise and its fade.</summary>
    private const double CelShow = 5.0, CelIn = 0.35, CelOut = 0.7;

    /// <summary>For the first seconds in a game what is already known is taken as known (a join, a load, a save's levels): no celebration.</summary>
    private const double CelSettle = 4.0;

    private Game? _celGame;
    private byte _celSelf = byte.MaxValue;
    private double _celFrom, _celLast = -1, _celT = -1; // the watcher's start; the last frame; the current one's age (-1: none showing)
    private int _celVersion = -1;
    private readonly byte[] _celMask = new byte[FactionTable.MaxStory];
    private readonly byte[] _celLevels = new byte[(int)ToolStat.COUNT];
    private readonly (string Title, string Name, string Line, string Hint)[] _celQ = new (string, string, string, string)[8];
    private int _celHead, _celCount;
    private int _celHintBind = int.MinValue;
    private string _celHint = "";
    private readonly float[] _celWidth = new float[4]; // the current one's line widths at the font's own size (measured once)

    /// <summary>A technology's line as the table holds it ("Remote Control (Registered): Remote Transmitter, ..."), in its parts.</summary>
    private static (string Name, string Tier, string Parts) TechLine(string line)
    {
        var colon = line.IndexOf("): ", StringComparison.Ordinal);
        if (colon < 0)
            return (line, "", "");
        var open = line.LastIndexOf(" (", colon, StringComparison.Ordinal);
        return open < 0 ? (line[..colon], "", line[(colon + 3)..]) : (line[..open], line[(open + 2)..colon], line[(colon + 3)..]);
    }

    private void CelAdd(string title, string name, string line, string hint)
    {
        if (_celCount == _celQ.Length) // (a flood: the oldest waiting one gives way)
        {
            _celHead = (_celHead + 1) % _celQ.Length;
            _celCount--;
        }
        _celQ[(_celHead + _celCount) % _celQ.Length] = (title, name, line, hint);
        _celCount++;
    }

    /// <summary>Each frame in a game: what the local player has learnt since last frame goes in the queue.</summary>
    private void CelWatch(Game game, double now, int logbookBind)
    {
        var self = game.LocalId;
        var t = game.FactionTable;
        if (!ReferenceEquals(game, _celGame) || self != _celSelf)
        {
            (_celGame, _celSelf, _celFrom, _celVersion, _celCount, _celT) = (game, self, now, -1, 0, -1);
            _celLevels.AsSpan().Clear();
            _celMask.AsSpan().Clear();
        }
        while (game.Celebrations.TryDequeue(out var c)) // (what the server or this peer's own code decided: GameCelebrate.cs)
            CelAdd(c.Title, c.Name, c.Line, "");
        if (self == byte.MaxValue)
            return;
        var settling = now - _celFrom < CelSettle;
        if (logbookBind != _celHintBind)
            (_celHintBind, _celHint) = (logbookBind, $"Logbook ({Keybinds.Name(logbookBind)}): Technologies");
        var hint = _celHint;
        if (t.Version != _celVersion)
        {
            _celVersion = t.Version;
            var count = Math.Min(t.StoryTech.Length, Math.Min(t.StoryNames.Length, FactionTable.MaxStory));
            for (var f = 0; f < FactionTable.MaxStory; f++)
            {
                var m = t.PlayerLearnt[self * FactionTable.MaxStory + f];
                var fresh = (byte)(m & ~_celMask[f]);
                _celMask[f] = m;
                if (fresh == 0 || settling || f >= count)
                    continue;
                for (var k = 0; k < t.StoryTech[f].Length && k < 8; k++)
                    if ((fresh & (1 << k)) != 0)
                    {
                        var (name, _, parts) = TechLine(t.StoryTech[f][k]);
                        var list = parts.Split(", ");
                        if (list.Length > 3)
                            parts = $"{string.Join(", ", list.Take(3))} and {list.Length - 3} more";
                        CelAdd("TECHNOLOGY LEARNT", name, parts.Length > 0 ? $"Taught by {t.StoryNames[f]}. Your Assemblers build {parts}." : $"Taught by {t.StoryNames[f]}.", hint);
                    }
            }
        }
        if (!game.SandboxOn)
            return;
        var creative = game.SandboxInfiniteParts;
        var table = ToolUpgrades.Current;
        for (var s = 0; s < _celLevels.Length; s++)
        {
            var level = game.SandboxHands.Level(self, (ToolStat)s);
            var was = _celLevels[s];
            _celLevels[s] = level;
            if (level <= was || settling || creative)
                continue;
            var st = table.Of((ToolStat)s);
            var tool = ToolUpgrades.ToolName(st.Tool);
            tool = char.ToUpperInvariant(tool[0]) + tool[1..];
            CelAdd("TOOL UPGRADE", st.Function ? $"{tool}: {st.Name}" : $"{tool}: {st.Name} Mk {level + 1}",
                st.About.Length > 0 ? st.About : $"Your {table.Gives((ToolStat)s, level)}.", hint);
        }
    }

    /// <summary>
    /// Each frame in a game (Program.cs, after the HUD): the watcher, then the current celebration, unless <paramref name="hold"/>
    /// (the opening, a ride, the workbench) or a menu is up, when it waits where it is. Returns true when one began this frame
    /// (the caller plays its sound).
    /// </summary>
    public bool DrawCelebration(Game game, double now, bool hold, int logbookBind)
    {
        CelWatch(game, now, logbookBind);
        var dt = _celLast < 0 ? 0 : Math.Clamp(now - _celLast, 0, 0.25);
        _celLast = now;
        if (_celCount == 0 || hold || PauseOpen || MenuOpen)
            return false;
        var began = false;
        if (_celT < 0)
        {
            _celT = 0;
            began = true;
            var c = _celQ[_celHead];
            _celWidth[0] = ImGui.CalcTextSize(c.Title).X;
            _celWidth[1] = ImGui.CalcTextSize(c.Name).X;
            _celWidth[2] = ImGui.CalcTextSize(c.Line).X;
            _celWidth[3] = ImGui.CalcTextSize(c.Hint).X;
        }
        else
            _celT += dt;
        if (_celT >= CelShow)
        {
            _celQ[_celHead] = default;
            _celHead = (_celHead + 1) % _celQ.Length;
            _celCount--;
            _celT = -1;
            return false;
        }
        DrawCelebrationCard(_celQ[_celHead], (float)_celT);
        return began;
    }

    private void DrawCelebrationCard((string Title, string Name, string Line, string Hint) c, float t)
    {
        var vp = ImGui.GetMainViewport();
        var dl = ImGui.GetForegroundDrawList();
        var font = ImGui.GetFont();
        var fs = ImGui.GetFontSize();
        var a = t < CelIn ? t / (float)CelIn : t > CelShow - CelOut ? (float)((CelShow - t) / CelOut) : 1f;
        a = Math.Clamp(a, 0f, 1f);
        var rise = t < CelIn ? t / (float)CelIn : 1f;
        var pop = 1f + 0.25f * MathF.Max(0f, 1f - rise) * MathF.Sin(rise * MathF.PI); // (a little overshoot as it comes in)
        float2 centre = new(vp.WorkPos.X + vp.WorkSize.X * 0.5f, vp.WorkPos.Y + vp.WorkSize.Y * 0.34f);

        var pxTitle = SubtitlePx(17f);
        var pxName = SubtitlePx(34f) * pop;
        var pxLine = SubtitlePx(15f);
        var pxHint = SubtitlePx(12f);
        var width = MathF.Max(MathF.Max(_celWidth[0] * pxTitle / fs, _celWidth[1] * pxName / fs), _celWidth[2] * pxLine / fs) + 80 * _scale;
        var lineH = pxLine * 1.25f;
        var height = pxTitle * 1.4f + pxName * 1.25f + lineH + pxHint * 1.6f + 36 * _scale;
        var top = centre.Y - height * 0.5f;

        // Rays behind it, turning slowly, and sparks flying out as it comes: a few dozen shapes, worked out from the age alone.
        var gold = new System.Numerics.Vector3(1f, 0.78f, 0.3f);
        var reach = width * 0.48f;
        for (var i = 0; i < 14; i++)
        {
            var ang = i * (MathF.Tau / 14) + t * 0.25f;
            var (sn, cs) = MathF.SinCos(ang);
            var len = reach * (0.75f + 0.25f * MathF.Sin(i * 2.3f + t * 1.7f)) * rise;
            ImDrawListExtensions.AddLine(dl, centre + new float2(cs, sn) * (height * 0.3f), centre + new float2(cs, sn) * len,
                Hud.Colour(gold, a * 0.13f), 5f * _scale);
        }
        if (t < 2.2f)
            for (var i = 0; i < 36; i++)
            {
                var ang = i * 2.39996f; // (the golden angle: an even spread with no table)
                var (sn, cs) = MathF.SinCos(ang);
                var speed = (180f + 140f * ((i * 37 % 17) / 17f)) * _scale;
                var age = t - (i % 4) * 0.06f;
                if (age <= 0)
                    continue;
                var at = centre + new float2(cs * 1.4f, sn) * (speed * age) + new float2(0, 60f * _scale * age * age);
                var fade = Math.Clamp(1f - age / 2f, 0f, 1f) * a;
                var tint = (i % 3) switch { 0 => gold, 1 => SubtitleTint, _ => new System.Numerics.Vector3(1f, 1f, 1f) };
                ImDrawListExtensions.AddCircleFilled(dl, in at, (2.5f + i % 3) * _scale, Hud.Colour(tint, fade), 6);
            }

        // The card.
        ImDrawListExtensions.AddRectFilled(dl, new float2(centre.X - width * 0.5f, top), new float2(centre.X + width * 0.5f, top + height),
            Hud.Colour(new System.Numerics.Vector3(0.05f, 0.05f, 0.07f), a * 0.82f), 8 * _scale, ImDrawFlags.None);
        ImDrawListExtensions.AddRectFilled(dl, new float2(centre.X - width * 0.5f, top), new float2(centre.X + width * 0.5f, top + 3 * _scale),
            Hud.Colour(gold, a), 0, ImDrawFlags.None);
        var y = top + 14 * _scale;
        CelText(dl, font, c.Title, _celWidth[0] * pxTitle / fs, pxTitle, centre.X, y, gold, a);
        y += pxTitle * 1.4f;
        CelText(dl, font, c.Name, _celWidth[1] * pxName / fs, pxName, centre.X, y, SubtitleTint, a);
        y += pxName * 1.25f;
        CelText(dl, font, c.Line, _celWidth[2] * pxLine / fs, pxLine, centre.X, y, new System.Numerics.Vector3(0.85f, 0.85f, 0.88f), a);
        y += lineH + 6 * _scale;
        CelText(dl, font, c.Hint, _celWidth[3] * pxHint / fs, pxHint, centre.X, y, new System.Numerics.Vector3(0.6f, 0.6f, 0.65f), a);
    }

    private static void CelText(ImDrawListPtr dl, ImFontPtr font, string text, float w, float px, float cx, float y, System.Numerics.Vector3 rgb, float a)
    {
        var at = new float2(cx - w * 0.5f, y);
        var off = MathF.Max(1f, px / 14f);
        dl.AddText(font, px, at + new float2(off, off), Hud.Colour(System.Numerics.Vector3.Zero, a * 0.8f), text);
        dl.AddText(font, px, at, Hud.Colour(rgb, a), text);
    }

    // ---- The Logbook's Technologies page ------------------------------------------------------------------------------

    private readonly List<(string Text, byte Kind)> _techRows = new(64); // Kind: 0 heading, 1 learnt, 2 locked, 3 detail, 4 note
    private int _techRowsVersion = -1;
    private byte _techRowsSelf = byte.MaxValue;
    private string _techSummary = "";

    private void DrawLogbookTechnologies(Game game)
    {
        var t = game.FactionTable;
        var self = game.LocalId;
        if (_techRowsVersion != t.Version || _techRowsSelf != self)
        {
            (_techRowsVersion, _techRowsSelf) = (t.Version, self);
            _techRows.Clear();
            int have = 0, all = 0;
            var count = Math.Min(t.StoryTech.Length, Math.Min(t.StoryNames.Length, FactionTable.MaxStory));
            for (var f = 0; f < count; f++)
            {
                var techs = t.StoryTech[f];
                if (techs.Length == 0)
                    continue;
                _techRows.Add((t.StoryNames[f], 0));
                var mask = self == byte.MaxValue ? 0 : t.PlayerLearnt[self * FactionTable.MaxStory + f];
                for (var k = 0; k < techs.Length; k++)
                {
                    var (name, tier, parts) = TechLine(techs[k]);
                    var learnt = k < 8 && (mask & (1 << k)) != 0;
                    all++;
                    if (learnt)
                    {
                        have++;
                        _techRows.Add(($"  [learnt] {name}", 1));
                        _techRows.Add(($"      Your Assemblers build {parts}.", 3));
                    }
                    else
                    {
                        _techRows.Add(($"  [locked] {name}{(tier.Length > 0 ? $": taught at {tier}" : "")}", 2));
                        _techRows.Add(($"      {parts}", 4));
                    }
                }
            }
            _techSummary = all == 0
                ? "No faction teaches anything in this world."
                : $"Technologies learnt: {have} of {all}. A faction teaches its technologies when its standing with you (or your faction) first reaches the tier shown; learnt for good. Earn standing with jobs and deeds (F2).";
        }
        ImGuiSafe.TextWrapped(_techSummary);
        ImGui.Spacing();
        foreach (var (text, kind) in _techRows)
            switch (kind)
            {
                case 0:
                    ImGui.Spacing();
                    ImGuiSafe.TextColored(Orange, text);
                    break;
                case 1:
                    ImGuiSafe.TextColored(Green, text);
                    break;
                default:
                    ImGuiSafe.TextColored(Dim, text);
                    break;
            }
        ImGui.Spacing();
        ImGui.Separator();
        ImGuiSafe.TextWrapped("Tool upgrades");
        if (game.Local is { } me)
            SbDrawUpgrades(game, me); // (the inventory's Upgrades tab, the same lines: UiSandboxUpgrades.cs)
    }
}
