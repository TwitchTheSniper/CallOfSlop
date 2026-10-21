using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// Kill streaks in the UI: the overview card and picker page of the loadout screen, the streak icons, and the in-match
/// HUD (three badges with costs, progress pips and a ready glow, banners, the targeting hint and the jamming static).
/// </summary>
public sealed partial class Ui
{
    private int _streakFocus = 2; // picker: the slot a new pick replaces when all three are full
    private StreakDef? _hoverStreak;

    private static float4 Rgba(System.Numerics.Vector3 c, float a = 1f) => new(c.X, c.Y, c.Z, a);

    /// <summary>Text with an explicit size on any draw list; <paramref name="align"/> 0 = left, 0.5 = centred, 1 = right.</summary>
    private static void SizedText(ImDrawListPtr dl, float2 pos, float size, ImColor8 col, string text, float align = 0f)
    {
        var w = ImGui.CalcTextSize(text).X * size / MathF.Max(ImGui.GetFontSize(), 1f);
        dl.AddText(ImGui.GetFont(), size, new float2(pos.X - w * align, pos.Y), col, text);
    }

    // ---- Overview card ---------------------------------------------------------------------------

    /// <summary>The Kill Streaks card on the overview: the three streaks with icons and costs. Returns true when clicked.</summary>
    private bool StreaksCard(StreakSet set, float width, float height, ref int hot)
    {
        var pos = ImGui.GetCursorScreenPos();
        ImGui.PushStyleColor(ImGuiCol.ChildBg, _hotCard == 5 ? CardHover : CardBg);
        if (ImGui.BeginChild("##streakcard"u8, new float2(width, height), ImGuiChildFlags.Borders, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            ImGui.TextColored(Orange, "Kill Streaks"u8);
            var dl = ImGui.GetWindowDrawList();
            var colW = (width - 20 * _scale) / 3;
            for (var i = 0; i < 3; i++)
            {
                var id = set[i];
                var def = KillStreaks.Get(id);
                var x0 = pos.X + 10 * _scale + i * colW;
                var cy = pos.Y + height * 0.5f + 9 * _scale;
                DrawStreakIcon(dl, id, new float2(x0 + 22 * _scale, cy), 18 * _scale);
                ImGui.SetCursorScreenPos(new float2(x0 + 48 * _scale, cy - 18 * _scale));
                ImGui.Text(id == StreakId.None ? "Empty" : def.Name);
                ImGui.SetCursorScreenPos(new float2(x0 + 48 * _scale, cy + 1 * _scale));
                ImGuiSafe.TextColored(id == StreakId.None ? Dim : Rgba(def.Color), id == StreakId.None ? "-" : $"{def.Kills} kills");
            }
        }
        ImGui.EndChild();
        ImGui.PopStyleColor();
        if (ImGui.IsItemHovered())
            hot = 5;
        return ImGui.IsItemClicked(ImGuiMouseButton.Left) && !SecondClick;
    }

    // ---- Picker page -----------------------------------------------------------------------------

    /// <summary>
    /// The kill streak page: your three slots on top (click one to choose which slot a new pick replaces), every streak
    /// below with its cost, effect and lock state. Click (or double-click) a streak to carry it; click a carried one to drop it.
    /// </summary>
    private void DrawStreakPicker(Audio audio, float2 area)
    {
        var prog = Progress;
        var l = Edit;
        var set = l.Streaks.Sanitized();
        var gap = ImGui.GetStyle().ItemSpacing.X;

        // Slots
        ImGui.TextColored(Dim, "Carry three. Each one is earned by that many kills in one life (kills by streaks don't count), and kept until you use it."u8);
        var slotW = (area.X - gap * 2) / 3;
        var slotH = 64 * _scale;
        if (set.Count < 3)
            _streakFocus = Enumerable.Range(0, 3).First(i => set[i] == StreakId.None);
        for (var i = 0; i < 3; i++)
        {
            if (i > 0)
                ImGui.SameLine();
            var id = set[i];
            var def = KillStreaks.Get(id);
            var focus = i == _streakFocus;
            var pos = ImGui.GetCursorScreenPos();
            ImGui.PushStyleColor(ImGuiCol.ChildBg, focus ? SelectedBg : CardBg);
            if (ImGui.BeginChild($"##streakslot{i}", new float2(slotW, slotH), ImGuiChildFlags.Borders, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
            {
                var dl = ImGui.GetWindowDrawList();
                DrawStreakIcon(dl, id, pos + new float2(30 * _scale, slotH / 2), 20 * _scale);
                ImGui.SetCursorScreenPos(pos + new float2(60 * _scale, 10 * _scale));
                ImGui.PushFont(default, 18);
                ImGuiSafe.TextColored(id == StreakId.None ? Dim : new float4(1, 1, 1, 1), id == StreakId.None ? "Empty slot" : def.Name);
                ImGui.PopFont();
                ImGui.SetCursorScreenPos(pos + new float2(60 * _scale, 34 * _scale));
                ImGuiSafe.TextColored(id == StreakId.None ? Dim : Rgba(def.Color), id == StreakId.None ? "Pick a streak below" : $"{def.Kills} kills  -  streak key {i + 1}");
                if (focus && set.Count == 3)
                {
                    var a = pos;
                    var b = pos + new float2(slotW, slotH);
                    dl.AddRect(a, b, new ImColor8(240, 150, 40, 255), 4 * _scale, ImDrawFlags.None, 2f * _scale);
                }
            }
            ImGui.EndChild();
            ImGui.PopStyleColor();
            if (ImGui.IsItemClicked(ImGuiMouseButton.Left) && !SecondClick)
            {
                _streakFocus = i;
                audio.Effect(Sfx.HitTick, 0.3f);
            }
        }
        ImGui.TextColored(Dim, set.Count == 3
            ? "All three slots are full: a new pick replaces the highlighted one (click a slot to choose)."u8
            : "Click a streak to add it."u8);

        // Every streak
        var gridH = MathF.Max(80 * _scale, area.Y - (ImGui.GetCursorScreenPos().Y - _bodyTopY));
        StreakDef? hovered = null;
        if (ImGui.BeginChild("##streakgrid"u8, new float2(area.X, gridH), ImGuiChildFlags.None))
        {
            var cols = 2;
            var cw = (ImGui.GetContentRegionAvail().X - gap * (cols - 1)) / cols;
            var ch = 104 * _scale;
            var n = 0;
            foreach (var def in KillStreaks.All.Where(s => s.Id != StreakId.None).OrderBy(s => s.Kills).ThenBy(s => (byte)s.Id))
            {
                if (n++ % cols != 0)
                    ImGui.SameLine();
                var unlocked = prog.IsUnlocked(def.Id);
                var selected = set.Has(def.Id);
                var pos = ImGui.GetCursorScreenPos();
                ImGui.PushStyleColor(ImGuiCol.ChildBg, selected ? SelectedBg : !unlocked ? LockedBg : _hoverStreak == def ? CardHover : CardBg);
                if (ImGui.BeginChild($"##streak{(int)def.Id}", new float2(cw, ch), ImGuiChildFlags.Borders, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
                {
                    var dl = ImGui.GetWindowDrawList();
                    DrawStreakIcon(dl, def.Id, pos + new float2(30 * _scale, 32 * _scale), 21 * _scale, !unlocked);
                    ImGui.SetCursorScreenPos(pos + new float2(60 * _scale, 8 * _scale));
                    ImGui.PushFont(default, 19);
                    ImGuiSafe.TextColored(unlocked ? new float4(1, 1, 1, 1) : Dim, def.Name);
                    ImGui.PopFont();
                    ImGui.SetCursorScreenPos(pos + new float2(60 * _scale, 32 * _scale));
                    ImGuiSafe.TextColored(unlocked ? Rgba(def.Color) : Dim, unlocked ? def.Short : $"LOCKED  -  unlocks at level {KillStreaks.UnlockLevel(def.Id)}");
                    // Cost badge, top right
                    var badge = $"{def.Kills} KILLS";
                    var bw = ImGui.CalcTextSize(badge).X + 14 * _scale;
                    var b0 = pos + new float2(cw - bw - (selected ? 30 : 10) * _scale, 8 * _scale);
                    var b1 = b0 + new float2(bw, 20 * _scale);
                    dl.AddRectFilled(b0, b1, unlocked ? Col(def.Color, 60) : new ImColor8(60, 60, 64, 120), 4 * _scale);
                    SizedText(dl, new float2((b0.X + b1.X) / 2, b0.Y + 2 * _scale), ImGui.GetFontSize(), unlocked ? Col(def.Color) : Dim8, badge, 0.5f);
                    if (selected)
                        DrawStar(dl, pos + new float2(cw - 16 * _scale, 18 * _scale), 7 * _scale, new ImColor8(240, 150, 40, 255));
                    ImGui.SetCursorScreenPos(pos + new float2(10 * _scale, 58 * _scale));
                    ImGui.PushTextWrapPos(ImGui.GetCursorPos().X + cw - 20 * _scale);
                    ImGui.PushStyleColor(ImGuiCol.Text, unlocked ? new float4(0.8f, 0.8f, 0.84f, 1f) : Dim);
                    ImGuiSafe.TextWrapped(def.Effect);
                    ImGui.PopStyleColor();
                    ImGui.PopTextWrapPos();
                }
                ImGui.EndChild();
                ImGui.PopStyleColor();
                if (ImGui.IsItemHovered())
                    hovered = def;
                // A click picks (a double-click's second click is swallowed, so it never undoes the pick).
                if (ImGui.IsItemClicked(ImGuiMouseButton.Left) && !SecondClick)
                {
                    if (!unlocked)
                    {
                        LockedFeedback(audio);
                    }
                    else
                    {
                        l.Streaks = selected ? set.Without(def.Id) : set.With(def.Id, set[_streakFocus]);
                        if (!selected)
                            _streakFocus = Math.Max(0, Enumerable.Range(0, 3).FirstOrDefault(i => l.Streaks[i] == def.Id));
                        set = l.Streaks;
                        LoadoutStore.Save();
                        Acted();
                        audio.Effect(selected ? Sfx.DryFire : Sfx.HitTick, 0.4f);
                    }
                }
            }
        }
        ImGui.EndChild();
        _hoverStreak = hovered;
    }

    // ---- Icons -----------------------------------------------------------------------------------

    /// <summary>A round badge with a simple glyph per streak, in the streak's colour (grey when locked).</summary>
    private void DrawStreakIcon(ImDrawListPtr dl, StreakId id, float2 c, float r, bool locked = false, float glow = 0f)
    {
        var def = KillStreaks.Get(id);
        var col = locked || id == StreakId.None ? new ImColor8(110, 110, 115, 255) : Col(def.Color);
        var dark = new ImColor8(18, 20, 26, 255);
        var center = c;
        if (glow > 0)
            ImDrawListExtensions.AddCircleFilled(dl, in center, r * (1.25f + 0.1f * glow), Col(def.Color, (byte)(90 * glow)), 28);
        ImDrawListExtensions.AddCircleFilled(dl, in center, r, dark, 28);
        ImDrawListExtensions.AddCircle(dl, in center, r, col, 28, 2f * _scale);
        var t = 2.2f * _scale * MathF.Max(r / (20 * _scale), 0.7f);
        var u = r * 0.5f;
        float2 P(float x, float y) => c + new float2(x * u, y * u);
        switch (id)
        {
            case StreakId.None:
                Line(dl, P(-0.6f, 0), P(0.6f, 0), col, t);
                break;
            case StreakId.ReconSweep: // radar: rings and a sweep line
                ImDrawListExtensions.AddCircle(dl, in center, u * 1.2f, col, 24, t * 0.7f);
                ImDrawListExtensions.AddCircle(dl, in center, u * 0.6f, col, 18, t * 0.7f);
                Line(dl, c, P(0.95f, -0.75f), col, t);
                ImDrawListExtensions.AddCircleFilled(dl, P(0.45f, 0.5f), u * 0.18f, col, 8);
                break;
            case StreakId.CounterSweep: // radar crossed out
                ImDrawListExtensions.AddCircle(dl, in center, u * 1.1f, col, 24, t * 0.7f);
                Line(dl, c, P(0.8f, -0.6f), col, t * 0.8f);
                Line(dl, P(-1.2f, 1.2f), P(1.2f, -1.2f), col, t * 1.2f);
                break;
            case StreakId.Overdrive: // lightning bolt
            {
                Span<float2> bolt = [P(0.25f, -1.3f), P(-0.7f, 0.15f), P(-0.05f, 0.15f), P(-0.3f, 1.3f), P(0.7f, -0.2f), P(0.05f, -0.2f)];
                ImDrawListExtensions.AddConcavePolyFilled(dl, bolt, col);
                break;
            }
            case StreakId.MortarBarrage: // three shells falling
                for (var i = -1; i <= 1; i++)
                {
                    var x = i * 0.75f;
                    var y = i == 0 ? -0.35f : 0.15f;
                    var a = P(x - 0.22f, y - 0.5f);
                    var b = P(x + 0.22f, y + 0.4f);
                    ImDrawListExtensions.AddRectFilled(dl, in a, in b, col, 2 * _scale, ImDrawFlags.None);
                    Tri(dl, P(x - 0.22f, y + 0.4f), P(x + 0.22f, y + 0.4f), P(x, y + 0.85f), col);
                }
                break;
            case StreakId.SentryTurret: // tripod with a gun head
            {
                var a = P(-0.55f, -0.75f);
                var b = P(0.45f, -0.05f);
                ImDrawListExtensions.AddRectFilled(dl, in a, in b, col, 2 * _scale, ImDrawFlags.None);
                Line(dl, P(0.4f, -0.45f), P(1.25f, -0.45f), col, t);
                Line(dl, P(0, 0), P(-0.9f, 1.15f), col, t);
                Line(dl, P(0, 0), P(0.9f, 1.15f), col, t);
                Line(dl, P(0, 0), P(0, 1.2f), col, t);
                break;
            }
            case StreakId.AttackDrone: // quadcopter from above
            {
                Line(dl, P(-0.85f, -0.85f), P(0.85f, 0.85f), col, t);
                Line(dl, P(-0.85f, 0.85f), P(0.85f, -0.85f), col, t);
                foreach (var (x, y) in new[] { (-0.85f, -0.85f), (0.85f, -0.85f), (-0.85f, 0.85f), (0.85f, 0.85f) })
                    ImDrawListExtensions.AddCircle(dl, P(x, y), u * 0.38f, col, 12, t * 0.7f);
                var a = P(-0.3f, -0.3f);
                var b = P(0.3f, 0.3f);
                ImDrawListExtensions.AddRectFilled(dl, in a, in b, col, 2 * _scale, ImDrawFlags.None);
                break;
            }
            case StreakId.StrafeRun: // jet with a line of impacts
            {
                Span<float2> jet = [P(0, -1.3f), P(0.25f, -0.4f), P(1.1f, 0.1f), P(0.25f, 0.05f), P(0.2f, 0.6f), P(0.5f, 0.85f),
                    P(-0.5f, 0.85f), P(-0.2f, 0.6f), P(-0.25f, 0.05f), P(-1.1f, 0.1f), P(-0.25f, -0.4f)];
                ImDrawListExtensions.AddConcavePolyFilled(dl, jet, col);
                break;
            }
        }
    }

    // ---- HUD -------------------------------------------------------------------------------------

    /// <summary>
    /// Bottom centre: the three carried streaks (cheapest first) with their key, kill cost, progress pips and a glow when
    /// ready; your current streak; banners for streaks earned and called in; the targeting hint; Counter Sweep static.
    /// Drawn on the background draw list so menus always cover it.
    /// </summary>
    private void DrawStreakHud(Game game, double now, float2 pos, float2 size, Settings settings)
    {
        if (!game.StreaksEnabled || game.Local is not { } me)
            return;
        var dl = ImGui.GetBackgroundDrawList();
        var s = me.Streak;
        var font = ImGui.GetFontSize();

        // Jammed by an enemy Counter Sweep: static over the screen.
        if (game.StreakJammed(me, now) && me.Alive)
        {
            var rng = new Random((int)(now * 20));
            for (var i = 0; i < 60; i++)
            {
                var y = pos.Y + (float)rng.NextDouble() * size.Y;
                var h = (1 + (float)rng.NextDouble() * 4) * _scale;
                var x = pos.X + (float)rng.NextDouble() * size.X * 0.6f;
                var g = (byte)(120 + rng.Next(120));
                dl.AddRectFilled(new float2(x, y), new float2(x + size.X * (0.1f + (float)rng.NextDouble() * 0.4f), y + h), new ImColor8(g, g, g, (byte)(25 + rng.Next(40))));
            }
            SizedText(dl, new float2(pos.X + size.X / 2, pos.Y + size.Y * 0.12f), font * 1.4f, new ImColor8(200, 120, 255, 230), "SIGNAL JAMMED", 0.5f);
        }

        if (s.Slots.Count > 0)
        {
            var r = 19 * _scale;
            var spacing = 76 * _scale;
            var baseY = pos.Y + size.Y - 70 * _scale;
            var cx0 = pos.X + size.X / 2 - spacing;
            for (var i = 0; i < 3; i++)
            {
                var id = s.Slots[i];
                if (id == StreakId.None)
                    continue;
                var def = KillStreaks.Get(id);
                var c = new float2(cx0 + i * spacing, baseY);
                var ready = s.IsReady(id);
                var targeting = game.StreakTargeting == id;
                var pulse = ready ? 0.6f + 0.4f * MathF.Sin((float)now * 6f) : 0f;
                DrawStreakIcon(dl, id, c, r, locked: !ready, glow: targeting ? 1f : pulse);
                // Key above, cost and progress below
                var key = Keybinds.Name(settings.Bind(GameAction.Streak1 + i));
                SizedText(dl, new float2(c.X, c.Y - r - font - 4 * _scale), font, ready ? new ImColor8(255, 255, 255, 240) : Dim8, ready ? $"[{key}] READY" : $"[{key}]", 0.5f);
                var cost = Perks.StreakCost(me.Perks, def.Kills); // Hardline: one less
                var got = Math.Min(s.ProgressOf(id), cost);
                var py = c.Y + r + 7 * _scale;
                if (cost <= 8)
                {
                    var pip = 6 * _scale;
                    var pg = 2.5f * _scale;
                    var total = cost * pip + (cost - 1) * pg;
                    for (var k = 0; k < cost; k++)
                    {
                        var a = new float2(c.X - total / 2 + k * (pip + pg), py);
                        var filled = ready || k < got;
                        dl.AddRectFilled(a, a + new float2(pip, pip), filled ? Col(def.Color) : new ImColor8(60, 62, 70, 200), 1.5f * _scale);
                    }
                }
                else
                {
                    var bw = 54 * _scale;
                    var a = new float2(c.X - bw / 2, py);
                    dl.AddRectFilled(a, a + new float2(bw, 5 * _scale), new ImColor8(60, 62, 70, 200), 2 * _scale);
                    dl.AddRectFilled(a, a + new float2(bw * (ready ? 1f : got / (float)cost), 5 * _scale), Col(def.Color), 2 * _scale);
                }
                SizedText(dl, new float2(c.X, py + 8 * _scale), font * 0.85f, ready ? Col(def.Color) : Dim8, ready ? def.Name : $"{cost} kills", 0.5f);
            }
            if (me.Alive)
                SizedText(dl, new float2(pos.X + size.X / 2, baseY - r - font * 2 - 8 * _scale), font * 0.9f, Dim8, $"STREAK {s.Count}", 0.5f);
            var od = game.StreakEffectLeft(StreakId.Overdrive, me.Id, now);
            if (od > 0 && me.Alive)
                SizedText(dl, new float2(pos.X + size.X / 2, baseY - r - font * 3 - 10 * _scale), font, new ImColor8(255, 205, 60, 255), $"OVERDRIVE {od:0}s", 0.5f);
        }

        // Targeting an aimed streak
        if (game.StreakTargeting != StreakId.None)
        {
            var def = KillStreaks.Get(game.StreakTargeting);
            var line = def.Aim == StreakAim.Line ? "the run passes over the spot, along the way you face" : "shells land around the spot";
            CenterText(size, pos, $"{def.Name.ToUpperInvariant()}: look at a spot and press {Keybinds.Name(settings.Bind(GameAction.Fire))} ({line})", 0.62f);
            CenterText(size, pos, game.StreakAimValid ? $"{Keybinds.Name(settings.Bind(GameAction.Aim))} or the streak key to cancel" : "No spot under the crosshair", 0.655f);
        }

        // Banner: earned / called in
        var age = now - game.StreakBannerTime;
        if (age < 3.5 && game.StreakBanner.Length > 0)
        {
            var a = (byte)(255 * Math.Clamp(3.5 - age, 0, 1));
            var col = game.StreakBannerEnemy ? new ImColor8(255, 90, 80, a) : new ImColor8(255, 200, 70, a);
            SizedText(dl, new float2(pos.X + size.X / 2, pos.Y + size.Y * 0.21f), font * 1.35f, col, game.StreakBanner, 0.5f);
        }
    }
}
