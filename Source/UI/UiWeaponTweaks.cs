using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The weapon tweak debug window (F7, rebindable; dev builds, or Settings > Graphics > "Allow the weapon tweak window" in
/// a release): the weapons by class on the left; for the picked weapon every stat its file holds as a slider (Ctrl+click
/// types a value), the file's value and a reset per stat, and the numbers that matter for feel (DPS, shots to kill, time to
/// kill at 5/15/30/50 m); per class, multipliers on damage, fire rate, recoil, spread, range, reload time and mobility that
/// stack on every weapon of the class. Changes apply live (<see cref="WeaponTweaks"/>); Save writes them into the weapon
/// files, Revert puts one weapon back, Reset all to files drops everything. Online only the host can change values (they go
/// to every player, GameWeaponTweaks.cs); a client sees the host's. Closed, it costs one bool check a frame.
/// </summary>
public sealed partial class Ui
{
    /// <summary>The weapon tweak window is open.</summary>
    public bool WeaponTweaksOpen;

    private bool _twWasOpen;
    private int _twWeapon = -1;
    private string _twMsg = "";
    private double _twMsgAt = -10;
    private int[][]? _twByClass; // weapon ids per class (made when the window opens)
    private static readonly string[] ClassLabels = Enum.GetValues<WeaponClass>().Select(c => c switch
    {
        WeaponClass.AssaultRifle => "Assault rifles",
        WeaponClass.DMR => "Marksman",
        WeaponClass.LMG => "LMGs",
        WeaponClass.SMG => "SMGs",
        WeaponClass.Special => "Special",
        _ => c + "s",
    }).ToArray();
    private static readonly string[] TwGroupNames = ["Damage", "Handling", "Penetration", "Recoil (the recoil block)", "Launcher (the launcher block)"];
    private static readonly string[] TwMultIds = WeaponTweaks.StatNames.Select(n => "##twm_" + n).ToArray();
    private static readonly string[] TwMultResetIds = WeaponTweaks.StatNames.Select(n => "R##twmr_" + n).ToArray();
    private static readonly string[] FireModeNames = Enum.GetNames<FireMode>();

    private void TweakMessage(string text, double now)
    {
        _twMsg = text;
        _twMsgAt = now;
        Console.WriteLine($"[weapontweaks] {text}");
    }

    /// <summary>
    /// Draws the window if it's open. <paramref name="session"/>: the game in progress (null in the menus);
    /// <paramref name="current"/>: the weapon it starts on; <paramref name="bind"/>: the key that opens it.
    /// </summary>
    public void DrawWeaponTweaks(Game? session, double now, WeaponDef current, int bind)
    {
        if (!WeaponTweaksOpen)
        {
            _twWasOpen = false;
            return; // closed: one bool check a frame
        }
        var all = Weapons.All;
        if (!_twWasOpen || _twByClass == null)
        {
            _twWasOpen = true;
            _twByClass = TweakClassLists(all);
            if (_twWeapon < 0 || _twWeapon >= all.Length)
                _twWeapon = current.Id < all.Length ? current.Id : 0;
            ImGui.SetNextWindowFocus();
        }
        var readOnly = session is { IsServer: false };
        var vp = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(new float2(vp.WorkPos.X + 24 * _scale, vp.WorkPos.Y + 40 * _scale), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new float2(MathF.Min(1000 * _scale, vp.WorkSize.X * 0.9f), MathF.Min(820 * _scale, vp.WorkSize.Y * 0.9f)), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowBgAlpha(0.94f);
        if (ImGui.Begin("Weapon tweaks###weapontweaks"u8, ref WeaponTweaksOpen))
        {
            DrawTweakToolbar(session, readOnly, now, bind);
            var avail = ImGui.GetContentRegionAvail();
            var listW = 220 * _scale;
            if (ImGui.BeginChild("##twlist"u8, new float2(listW, avail.Y), ImGuiChildFlags.Borders))
                DrawTweakList(all);
            ImGui.EndChild();
            ImGui.SameLine();
            if (ImGui.BeginChild("##twbody"u8, new float2(avail.X - listW - 8 * _scale, avail.Y), ImGuiChildFlags.None))
            {
                ImGui.BeginDisabled(readOnly);
                if (_twWeapon >= 0 && _twWeapon < all.Length)
                    DrawTweakWeapon(all[_twWeapon], now);
                ImGui.EndDisabled();
            }
            ImGui.EndChild();
        }
        ImGui.End();
        DrawRagdollTuning(); // the ragdoll panel (UiRagdollTuning.cs), opened from the toolbar
    }

    /// <summary>Weapon ids per class (its own method: a lambda in DrawWeaponTweaks would allocate its closure on every call, closed too).</summary>
    private static int[][] TweakClassLists(WeaponDef[] all) =>
        Enum.GetValues<WeaponClass>().Select(c => all.Where(w => w.Class == c).Select(w => (int)w.Id).ToArray()).ToArray();

    private void DrawTweakToolbar(Game? session, bool readOnly, double now, int bind)
    {
        if (readOnly)
            ImGui.TextColored(Orange, "Joined game: only the host can change weapons. These are the host's values."u8);
        else if (session is { } g && !g.Net.IsOffline)
            ImGui.TextColored(Green, "Hosting: changes apply live and go to every player."u8);
        else
            ImGui.TextColored(Dim, "Changes apply live. They aren't saved anywhere until you press Save to weapon files."u8);
        ImGui.BeginDisabled(readOnly || !WeaponTweaks.Active);
        if (ImGui.Button("Save to weapon files"u8))
        {
            var folders = new List<string> { Weapons.Folder };
            if (WeaponTweaks.RepoWeaponsFolder() is { } repo)
                folders.Add(repo);
            var (saved, lines) = WeaponTweaks.Save(folders);
            foreach (var l in lines)
                Console.WriteLine($"[weapontweaks] {l}");
            var problems = lines.Count(l => !l.StartsWith("saved ", StringComparison.Ordinal));
            TweakMessage(problems == 0 ? $"saved {saved} weapon(s) to {folders.Count} folder(s); the weapon fingerprint changed (players need the same files to join)"
                : $"saved {saved} weapon(s), {problems} problem(s): {lines.First(l => !l.StartsWith("saved ", StringComparison.Ordinal))}", now);
        }
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetItemTooltip("Writes every changed weapon's values into its file (only those numbers change in the file) in the game's Weapons folder, and in a dev build the repository's too. They are the new baseline from then on."u8);
        ImGui.SameLine();
        if (ImGui.Button("Reset all to files"u8))
        {
            WeaponTweaks.ResetAll();
            TweakMessage("every weapon is back to its file", now);
        }
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGuiSafe.TextColored(Dim, WeaponTweaks.Active ? $"{WeaponTweaks.OverrideCount} stat(s) and {WeaponTweaks.MultCount} class multiplier(s) changed" : "nothing changed");
        ImGui.SameLine();
        if (ImGui.Button("Ragdoll tuning"u8))
            RagdollTuningOpen = !RagdollTuningOpen;
        if (ImGui.IsItemHovered())
            ImGui.SetItemTooltip("Soft joint limits, the relax pull and how hard shots and blasts push bodies (Ragdolls/Ragdolls.json)."u8);
        ImGui.SameLine();
        ImGuiSafe.TextColored(Dim, $"({Keybinds.Name(bind)} closes)");
        if (now - _twMsgAt < 8)
            ImGuiSafe.TextWrapped(_twMsg);
    }

    private void DrawTweakList(WeaponDef[] all)
    {
        for (var c = 0; c < _twByClass!.Length; c++)
        {
            var ids = _twByClass[c];
            if (ids.Length == 0)
                continue;
            ImGui.SeparatorText(ClassLabels[c]);
            foreach (var id in ids)
            {
                var tweaked = WeaponTweaks.IsTweaked(id);
                if (tweaked)
                    ImGui.PushStyleColor(ImGuiCol.Text, Orange);
                if (ImGui.Selectable($"{all[id].Name}{(tweaked ? " *" : "")}##twl{id}", id == _twWeapon))
                    _twWeapon = id;
                if (tweaked)
                    ImGui.PopStyleColor();
            }
        }
    }

    private void DrawTweakWeapon(WeaponDef w, double now)
    {
        var file = WeaponTweaks.FileDef(w);
        ImGuiSafe.TextColored(Orange, $"{w.Name}  ({w.ClassName}, {w.ModeName})");
        ImGui.SameLine();
        ImGuiSafe.TextColored(Dim, file.SourceFile ?? "built-in, no file");
        DrawTweakFeel(w, file);
        if (ImGui.BeginTabBar("##twtabs"u8))
        {
            if (ImGui.BeginTabItem("Weapon"u8))
            {
                if (ImGui.Button("Revert this weapon"u8))
                {
                    WeaponTweaks.RevertWeapon(w.Id);
                    TweakMessage($"{w.Name}: back to its file (its class multipliers stay)", now);
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetItemTooltip("Drops this weapon's own changes; the class multipliers (other tab) still apply."u8);
                for (var g = 0; g <= (int)TweakGroup.Launcher; g++)
                {
                    if (g == (int)TweakGroup.Launcher && !w.IsLauncher)
                        continue;
                    if (ImGui.CollapsingHeader(TwGroupNames[g], g == (int)TweakGroup.Recoil ? ImGuiTreeNodeFlags.None : ImGuiTreeNodeFlags.DefaultOpen)
                        && ImGui.BeginTable(TwGroupNames[g], 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.SizingFixedFit))
                    {
                        ImGui.TableSetupColumn("Stat"u8, ImGuiTableColumnFlags.WidthFixed, 190 * _scale);
                        ImGui.TableSetupColumn("Value"u8, ImGuiTableColumnFlags.WidthStretch);
                        ImGui.TableSetupColumn("File"u8, ImGuiTableColumnFlags.WidthFixed, 110 * _scale);
                        ImGui.TableSetupColumn("Reset"u8, ImGuiTableColumnFlags.WidthFixed, 30 * _scale);
                        foreach (var f in WeaponTweaks.Fields)
                            if ((int)f.Group == g)
                                TweakRow(w, file, f);
                        ImGui.EndTable();
                    }
                }
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Class multipliers"u8))
            {
                DrawTweakClass(w.Class, now);
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }
    }

    private void TweakRow(WeaponDef w, WeaponDef file, TweakField f)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        var has = WeaponTweaks.HasOverride(w.Id, f);
        if (has)
            ImGuiSafe.TextColored(Orange, f.Label);
        else
            ImGui.Text(f.Label);
        ImGui.TableNextColumn();
        ImGui.SetNextItemWidth(-1);
        var v = WeaponTweaks.OverrideOr(w.Id, f);
        if (f.IsEnum)
        {
            var mode = Math.Clamp((int)v, 0, FireModeNames.Length - 1);
            if (ImGui.BeginCombo(f.SliderId, FireModeNames[mode]))
            {
                for (var i = 0; i < FireModeNames.Length; i++)
                    if (ImGui.Selectable(FireModeNames[i], i == mode))
                        WeaponTweaks.SetOverride(w.Id, f, i);
                ImGui.EndCombo();
            }
        }
        else if (ImGui.SliderFloat(f.SliderId, ref v, f.SoftMin, f.SoftMax, f.IsInt ? "%.0f"u8 : "%.4g"u8))
            WeaponTweaks.SetOverride(w.Id, f, v);
        var live = f.Get(w);
        if (live != f.Sanitize(v) && ImGui.IsItemHovered())
            ImGuiSafe.SetItemTooltip($"With the class multiplier: {live:0.####}");
        ImGui.TableNextColumn();
        var fv = f.Get(file);
        if (f.IsEnum)
            ImGuiSafe.TextColored(Dim, FireModeNames[Math.Clamp((int)fv, 0, FireModeNames.Length - 1)]);
        else if (live != fv)
            ImGuiSafe.TextColored(Dim, $"{fv:0.####} > {live:0.####}");
        else
            ImGuiSafe.TextColored(Dim, $"{fv:0.####}");
        ImGui.TableNextColumn();
        ImGui.BeginDisabled(!has);
        if (ImGui.SmallButton(f.ResetId))
            WeaponTweaks.ClearOverride(w.Id, f);
        ImGui.EndDisabled();
    }

    private void DrawTweakClass(WeaponClass c, double now)
    {
        ImGuiSafe.TextColored(Dim, $"Multipliers on every {ClassLabels[(int)c].ToLowerInvariant()} weapon, on top of each weapon's own values (whole numbers are rounded; the file ranges still clamp).");
        if (ImGui.Button("Revert class"u8))
        {
            WeaponTweaks.RevertClass(c);
            TweakMessage($"{ClassLabels[(int)c]}: multipliers back to 1", now);
        }
        if (!ImGui.BeginTable("##twclass"u8, 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.SizingFixedFit))
            return;
        ImGui.TableSetupColumn("Stat"u8, ImGuiTableColumnFlags.WidthFixed, 190 * _scale);
        ImGui.TableSetupColumn("Multiplier"u8, ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Reset"u8, ImGuiTableColumnFlags.WidthFixed, 30 * _scale);
        for (var s = 0; s < (int)TweakStat.COUNT; s++)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(WeaponTweaks.StatNames[s]);
            ImGui.TableNextColumn();
            ImGui.SetNextItemWidth(-1);
            var m = WeaponTweaks.ClassMult(c, (TweakStat)s);
            if (ImGui.SliderFloat(TwMultIds[s], ref m, WeaponTweaks.MinMult, WeaponTweaks.MaxMult, "x%.2f"u8, ImGuiSliderFlags.Logarithmic))
                WeaponTweaks.SetClassMult(c, (TweakStat)s, m);
            ImGui.TableNextColumn();
            ImGui.BeginDisabled(m == 1f);
            if (ImGui.SmallButton(TwMultResetIds[s]))
                WeaponTweaks.SetClassMult(c, (TweakStat)s, 1f);
            ImGui.EndDisabled();
        }
        ImGui.EndTable();
    }

    /// <summary>DPS, shots to kill and time to kill, live and (when they differ) as the file has them.</summary>
    private void DrawTweakFeel(WeaponDef w, WeaponDef file)
    {
        if (!ImGui.BeginTable("##twfeel"u8, 2 + WeaponTweaks.TtkRanges.Length, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.SizingFixedFit))
            return;
        ImGui.TableSetupColumn(""u8, ImGuiTableColumnFlags.WidthFixed, 60 * _scale);
        ImGui.TableSetupColumn("DPS / to kill"u8, ImGuiTableColumnFlags.WidthFixed, 150 * _scale);
        ImGui.TableSetupColumn("TTK 5 m"u8, ImGuiTableColumnFlags.WidthFixed, 110 * _scale);
        ImGui.TableSetupColumn("TTK 15 m"u8, ImGuiTableColumnFlags.WidthFixed, 110 * _scale);
        ImGui.TableSetupColumn("TTK 30 m"u8, ImGuiTableColumnFlags.WidthFixed, 110 * _scale);
        ImGui.TableSetupColumn("TTK 50 m"u8, ImGuiTableColumnFlags.WidthFixed, 110 * _scale);
        ImGui.TableHeadersRow();
        FeelRow("Live"u8, w, Orange);
        if (!ReferenceEquals(file, w) && WeaponTweaks.IsTweaked(w.Id))
            FeelRow("File"u8, file, Dim);
        ImGui.EndTable();
        ImGuiSafe.TextColored(Dim, $"Against {World.MaxHealth} health, body shots, every pellet landing; time to kill counts reloads. Head: {w.ShotsToKill(true)} shot(s) close up.");
    }

    private void FeelRow(ImString label, WeaponDef w, in float4 col)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGuiSafe.TextColored(col, label);
        ImGui.TableNextColumn();
        ImGuiSafe.TextColored(col, $"{w.Dps:0} DPS, {w.ShotsToKill(false)} shots");
        foreach (var r in WeaponTweaks.TtkRanges)
        {
            ImGui.TableNextColumn();
            ImGuiSafe.TextColored(col, $"{WeaponTweaks.TimeToKill(w, r) * 1000:0} ms ({WeaponTweaks.ShotsToKill(w, r)})");
        }
    }

    /// <summary>Over the scoreboard when the weapons differ from their files (the host's tweak window).</summary>
    private void DrawTweakedBadge(float2 size, float2 pos)
    {
        ImGui.SetNextWindowPos(new float2(pos.X + size.X / 2, pos.Y + size.Y * 0.3f - 34 * _scale), ImGuiCond.Always, new float2(0.5f, 0));
        if (ImGui.Begin("##tweakedbadge"u8, ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.AlwaysAutoResize))
            ImGui.TextColored(Orange, "TWEAKED WEAPONS: the host changed weapon stats in this match (weapon tweak window)"u8);
        ImGui.End();
    }
}
