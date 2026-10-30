using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The weapon picker on the loadout screen ("Choose primary" / "Choose secondary"): class tabs across
/// the top, a scrolling column of gun tiles on the left (3D thumbnail with this loadout's attachments,
/// name, weapon level and XP, EQUIPPED / LOCKED), and on the right the selected (or hovered) gun
/// rendered large with its stats compared against the gun you have equipped. Click a tile to select it
/// (preview), then EQUIP; double-clicking a tile does exactly what EQUIP does. ATTACHMENTS (top right)
/// opens the shown gun's attachments.
/// </summary>
public sealed partial class Ui
{
    private WeaponClass _wsTab; // open class tab
    private int _wsSlot = -1; // the slot the picker was last drawn for
    private bool _wsFresh; // the page was just opened: start on the equipped gun
    private bool _wsScrollToEquipped; // scroll the selected (or equipped) gun's tile into view this frame
    private WeaponDef? _wsSelected; // the tile you clicked (shown in the hero); null = the equipped gun
    private int _wsClickedId = -1; // the tile the last single click landed on (a double-click must start on the same tile)
    private int _wsFlashId = -1; // tile flashing after an equip (or a refused locked one)
    private double _wsFlashTime = -10;
    private bool _wsFlashLocked;

    private static readonly ImColor8 Orange8 = new(240, 150, 40, 255);
    private static readonly ImColor8 White8 = new(236, 238, 242, 255);
    private static readonly ImColor8 Dim8 = new(160, 162, 172, 255);
    private static readonly ImColor8 Red8 = new(240, 92, 80, 255);
    private static readonly ImColor8 Green8 = new(96, 225, 120, 255);
    private static readonly ImColor8 Card8 = new(23, 26, 31, 235);
    private static readonly ImColor8 Hover8 = new(38, 43, 53, 242);
    private static readonly ImColor8 Panel8 = new(13, 14, 18, 215);
    private static readonly ImColor8 XpBar8 = new(100, 242, 255, 255);
    private static readonly ImColor8 Select8 = new(110, 200, 255, 255); // the selected tile's outline (hover is orange)

    /// <summary>
    /// EQUIP (or a double-click on a tile): puts the gun in this loadout's slot, saves, plays its equip
    /// sound and flashes the tile. A locked gun only gets a short "locked" shake and click.
    /// </summary>
    private void EquipFromSelect(Audio audio, int gunSlot, WeaponDef w, WeaponDef equipped)
    {
        _wsFlashId = w.Id;
        _wsFlashTime = ImGui.GetTime();
        if (WeaponSelect.UnlockLevel(w) > Progress.Level)
        {
            _wsFlashLocked = true;
            LockedFeedback(audio);
            return;
        }
        _wsFlashLocked = false;
        _wsSelected = w;
        Acted();
        if (w.Id == equipped.Id)
            return;
        LoadoutStore.SetWeapon(Edit, gunSlot, w.Id, Progress);
        LoadoutStore.Save();
        audio.Equip(w);
    }

    private void DrawWeaponSelect(Audio audio, int gunSlot, WeaponDef gun, float2 area, float2 winPos, float2 winSize)
    {
        var prog = Progress;
        var tabs = WeaponSelect.Tabs(gunSlot == 0 ? Slot.Primary : Slot.Secondary);
        if (tabs.Length == 0)
            return;
        if (_wsFresh || _wsSlot != gunSlot)
        {
            // Just opened: start on the equipped gun's class, scrolled to it, nothing else selected.
            _wsSelected = null;
            _wsClickedId = -1;
            _wsTab = Array.IndexOf(tabs, gun.Class) >= 0 ? gun.Class : tabs[0];
            _wsScrollToEquipped = true;
            _wsFresh = false;
        }
        _wsSlot = gunSlot;
        if (_wsSelected != null && (int)_wsSelected.Slot != gunSlot)
            _wsSelected = null;
        if (Array.IndexOf(tabs, _wsTab) < 0)
            _wsTab = tabs[0];
        var selected = _wsSelected ?? gun;

        var origin = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();

        // ---- Class tabs --------------------------------------------------------------------
        var tabH = 34 * _scale;
        var gap = 6 * _scale;
        var tabW = MathF.Min(210 * _scale, (area.X - gap * (tabs.Length - 1)) / tabs.Length);
        for (var i = 0; i < tabs.Length; i++)
        {
            var c = tabs[i];
            var p0 = origin + new float2(i * (tabW + gap), 0);
            var p1 = p0 + new float2(tabW, tabH);
            ImGui.SetCursorScreenPos(p0);
            if (ImGui.InvisibleButton($"##wstab{i}", new float2(tabW, tabH)) && c != _wsTab && !SecondClick)
            {
                _wsTab = c;
                _wsScrollToEquipped = true;
                audio.Effect(Sfx.HitTick, 0.3f);
            }
            var hot = ImGui.IsItemHovered();
            var sel = c == _wsTab;
            dl.AddRectFilled(p0, p1, sel ? new ImColor8(44, 38, 32, 245) : hot ? Hover8 : Card8, 3 * _scale);
            if (sel)
                dl.AddRectFilled(new float2(p0.X, p1.Y - 3 * _scale), p1, Orange8);
            var ty = p0.Y + (tabH - ImGui.GetFontSize()) / 2;
            var clip = new float4(p0.X, p0.Y, p1.X, p1.Y);
            // Right: gun count, then a padlock and how many are locked.
            var rx = p1.X - 8 * _scale;
            var locked = WeaponSelect.LockedCount(c, prog.Level);
            if (locked > 0)
            {
                rx -= ImGui.CalcTextSize($"{locked}").X;
                DlText(dl, new float2(rx, ty), Dim8, $"{locked}", clip);
                rx -= 9 * _scale;
                DrawPadlock(dl, new float2(rx, p0.Y + tabH / 2), 11 * _scale, Dim8);
                rx -= 14 * _scale;
            }
            var total = WeaponSelect.InTab(c).Length;
            rx -= ImGui.CalcTextSize($"{total}").X;
            DlText(dl, new float2(rx, ty), sel ? Orange8 : Dim8, $"{total}", clip);
            DlText(dl, new float2(p0.X + 10 * _scale, ty), sel || hot ? White8 : Dim8, WeaponSelect.TabName(c), new float4(p0.X, p0.Y, rx - 4 * _scale, p1.Y));
        }

        var bodyTop = origin.Y + tabH + 8 * _scale;
        var bodyH = MathF.Max(120 * _scale, area.Y - (tabH + 8 * _scale));
        var listW = MathF.Min(MathF.Max(area.X * 0.34f, 250 * _scale), area.X * 0.45f);

        // ---- Tile column -------------------------------------------------------------------
        WeaponDef? hovered = null;
        ImGui.SetCursorScreenPos(new float2(origin.X, bodyTop));
        if (ImGui.BeginChild($"##wstiles{(int)_wsTab}", new float2(listW, bodyH), ImGuiChildFlags.None))
        {
            var clipMin = ImGui.GetWindowPos();
            var clipMax = clipMin + ImGui.GetWindowSize();
            var tw = ImGui.GetContentRegionAvail().X;
            var tileH = 88 * _scale;
            var tdl = ImGui.GetWindowDrawList();
            foreach (var w in WeaponSelect.InTab(_wsTab))
            {
                var p0 = ImGui.GetCursorScreenPos();
                var equipped = w.Id == gun.Id;
                var isTarget = w.Id == selected.Id;
                if (p0.Y + tileH < clipMin.Y || p0.Y > clipMax.Y)
                {
                    // Scrolled out of view: just reserve the space (no drawing, no thumbnail).
                    ImGui.Dummy(new float2(tw, tileH));
                    if (isTarget && _wsScrollToEquipped)
                        ImGui.SetScrollHereY(0.5f);
                    continue;
                }
                var clicked = ImGui.InvisibleButton($"##wst{w.Id}", new float2(tw, tileH)) && !SecondClick;
                var hovering = ImGui.IsItemHovered();
                // Double-click = EQUIP. It must start on this tile (not on a card of the page before).
                var doubleClicked = hovering && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left) && _wsClickedId == w.Id && !SecondClick;
                if (isTarget && _wsScrollToEquipped)
                    ImGui.SetScrollHereY(0.5f);
                if (hovering)
                    hovered = w;
                var unlocked = WeaponSelect.UnlockLevel(w) <= prog.Level;
                if (doubleClicked)
                {
                    EquipFromSelect(audio, gunSlot, w, gun);
                }
                else if (clicked)
                {
                    // A single click selects (previews) the gun, locked or not; the second click of a double-click lands here again and changes nothing.
                    if (_wsSelected != w)
                        audio.Effect(Sfx.HitTick, 0.3f);
                    _wsSelected = w;
                    _wsClickedId = w.Id;
                }
                DrawWeaponTile(tdl, w, p0, new float2(tw, tileH), unlocked, equipped, hovering, (_wsSelected ?? gun).Id == w.Id, clipMin, clipMax);
            }
        }
        ImGui.EndChild();
        _wsScrollToEquipped = false;

        var shown = hovered ?? _wsSelected ?? gun;
        DrawWeaponHero(audio, gunSlot, shown, gun, new float2(origin.X + listW + 14 * _scale, bodyTop), new float2(area.X - listW - 14 * _scale, bodyH), winPos, winSize);
    }

    /// <summary>
    /// One tile: gun picture on the left, name / level / XP / status on the right. Hover = orange outline,
    /// selected = blue outline and tint, equipped = orange bar on the left and the EQUIPPED tag.
    /// </summary>
    private void DrawWeaponTile(ImDrawListPtr dl, WeaponDef w, float2 p0, float2 size, bool unlocked, bool equipped, bool hot, bool selected, float2 clipMin, float2 clipMax)
    {
        // Equip flash (orange pulse) or the locked shake.
        var flash = 0f;
        if (w.Id == _wsFlashId)
        {
            var age = (float)(ImGui.GetTime() - _wsFlashTime);
            flash = Math.Clamp(1f - age / 0.55f, 0f, 1f);
            if (_wsFlashLocked && flash > 0)
                p0.X += MathF.Sin(age * 55f) * 5f * _scale * flash;
        }
        var p1 = p0 + size;
        var r = 4 * _scale;
        var bg = !unlocked
            ? selected ? new ImColor8(26, 30, 38, 240) : new ImColor8(15, 15, 18, hot ? (byte)240 : (byte)215)
            : selected
                ? hot ? new ImColor8(38, 54, 74, 245) : new ImColor8(30, 43, 60, 245)
                : hot ? Hover8 : Card8;
        dl.AddRectFilled(p0, p1, bg, r);
        if (equipped)
            dl.AddRectFilled(p0, new float2(p0.X + 4 * _scale, p1.Y), Orange8, r, ImDrawFlags.RoundCornersLeft);
        if (selected)
            dl.AddRect(p0, p1, Select8, r, ImDrawFlags.None, 2.2f * _scale);
        else if (hot)
            dl.AddRect(p0, p1, unlocked ? Orange8 : Dim8, r, ImDrawFlags.None, 1.5f * _scale);
        else if (equipped)
            dl.AddRect(p0, p1, new ImColor8(240, 150, 40, 110), r, ImDrawFlags.None, 1f * _scale);
        if (flash > 0)
        {
            var fc = _wsFlashLocked ? new ImColor8(240, 92, 80, (byte)(110 * flash)) : new ImColor8(240, 150, 40, (byte)(120 * flash));
            dl.AddRectFilled(p0, p1, fc, r);
            dl.AddRect(p0, p1, _wsFlashLocked ? Red8 : Orange8, r, ImDrawFlags.None, (1.5f + 2.5f * flash) * _scale);
        }

        // Gun picture (Program draws it over the UI, clipped to the list and dimmed when locked).
        var pad = 8 * _scale;
        var t0 = p0 + new float2(pad + 4 * _scale, pad);
        var t1 = new float2(p0.X + size.X * 0.47f, p1.Y - pad);
        dl.AddRectFilled(t0, t1, new ImColor8(11, 12, 15, 255), 3 * _scale);
        Thumbs.Add(new GunThumb(Attachments.Apply(w, KitOf(w)), t0, t1, clipMin, clipMax, unlocked ? 0f : 0.62f));

        // Text column
        var x = t1.X + 10 * _scale;
        var clip = new float4(x, p0.Y, p1.X - 6 * _scale, p1.Y);
        ImGui.PushFont(default, 17);
        DlText(dl, new float2(x, p0.Y + 9 * _scale), unlocked ? White8 : Dim8, w.Name, clip);
        ImGui.PopFont();
        var options = Attachments.For(w);
        if (options.Count > 0)
        {
            var wxp = Progress.WeaponXp(w.Id);
            var maxXp = Attachments.MaxXp(w);
            DlText(dl, new float2(x, p0.Y + 32 * _scale), Dim8, $"LV {WeaponSelect.UnlockedAttachments(w, wxp)}/{options.Count}", clip);
            var b0 = new float2(x, p0.Y + 52 * _scale);
            var barW = MathF.Max(0, p1.X - 10 * _scale - x);
            dl.AddRectFilled(b0, b0 + new float2(barW, 4 * _scale), new ImColor8(48, 52, 60, 255));
            var f = maxXp <= 0 ? 0f : Math.Clamp(wxp / (float)maxXp, 0f, 1f);
            if (f > 0)
                dl.AddRectFilled(b0, b0 + new float2(barW * f, 4 * _scale), unlocked ? XpBar8 : new ImColor8(70, 100, 110, 255));
        }
        else
        {
            DlText(dl, new float2(x, p0.Y + 32 * _scale), Dim8, "NO ATTACHMENTS"u8, clip);
        }

        // Status line
        var sy = p1.Y - 24 * _scale;
        if (!unlocked)
        {
            DrawPadlock(dl, new float2(x + 6 * _scale, sy + 7 * _scale), 13 * _scale, Red8);
            DlText(dl, new float2(x + 17 * _scale, sy), Red8, $"LOCKED - LEVEL {WeaponSelect.UnlockLevel(w)}", clip);
        }
        else if (equipped)
        {
            var ts = ImGui.CalcTextSize("EQUIPPED"u8);
            var a = new float2(x, sy - 2 * _scale);
            dl.AddRectFilled(a, a + new float2(ts.X + 12 * _scale, ts.Y + 4 * _scale), Orange8, 2 * _scale);
            DlText(dl, new float2(x + 6 * _scale, sy), new ImColor8(22, 16, 8, 255), "EQUIPPED"u8, clip);
        }
        else if (selected)
        {
            var ts = ImGui.CalcTextSize("SELECTED"u8);
            var a = new float2(x, sy - 2 * _scale);
            dl.AddRect(a, a + new float2(ts.X + 12 * _scale, ts.Y + 4 * _scale), Select8, 2 * _scale, ImDrawFlags.None, 1.2f * _scale);
            DlText(dl, new float2(x + 6 * _scale, sy), Select8, "SELECTED"u8, clip);
        }
        else if (hot)
        {
            DlText(dl, new float2(x, sy), Dim8, "Click to preview"u8, clip);
        }
    }

    /// <summary>
    /// The big picture on the right: name, class, blurb, the 3D gun, and stats against the equipped gun.
    /// ATTACHMENTS sits top right, EQUIP bottom right; both act on the gun shown.
    /// </summary>
    private void DrawWeaponHero(Audio audio, int gunSlot, WeaponDef shownBase, WeaponDef gunBase, float2 pos, float2 size, float2 winPos, float2 winSize)
    {
        var prog = Progress;
        var dl = ImGui.GetWindowDrawList();
        var shown = Attachments.Apply(shownBase, KitOf(shownBase));
        var equipped = Attachments.Apply(gunBase, KitOf(gunBase));
        var isEquipped = shownBase.Id == gunBase.Id;
        var level = WeaponSelect.UnlockLevel(shownBase);
        var unlocked = level <= prog.Level;

        // ---- Title block -----------------------------------------------------------------
        ImGui.SetCursorScreenPos(pos);
        ImGui.BeginGroup();
        ImGuiSafe.TextColored(Orange, WeaponSelect.ClassLabel(shown.Class));
        ImGui.SameLine();
        ImGui.TextColored(Dim, "/"u8);
        ImGui.SameLine();
        if (shown.Mode == FireMode.Burst)
            ImGuiSafe.TextColored(Dim, $"{shown.BurstCount}-ROUND BURST");
        else
            ImGui.TextColored(Dim, shown.Mode == FireMode.Auto ? "FULL AUTO"u8 : "SEMI AUTO"u8);
        if (shown.Pellets > 1)
        {
            ImGui.SameLine();
            ImGuiSafe.TextColored(Dim, $"/  {shown.Pellets} PELLETS");
        }
        var namePos = ImGui.GetCursorScreenPos();
        if (!unlocked)
        {
            DrawPadlock(dl, namePos + new float2(13 * _scale, 22 * _scale), 28 * _scale, Red8);
            ImGui.SetCursorScreenPos(namePos + new float2(34 * _scale, 0));
        }
        ImGui.PushFont(default, 40);
        ImGuiSafe.TextColored(unlocked ? new float4(1, 1, 1, 1) : Dim, WeaponSelect.UpperName(shown));
        ImGui.PopFont();
        if (!unlocked)
        {
            ImGui.SetCursorScreenPos(new float2(pos.X, ImGui.GetCursorScreenPos().Y));
            ImGuiSafe.TextColored(Red, $"LOCKED  -  reach level {level} to unlock (you're level {prog.Level})");
        }
        else if (isEquipped)
            ImGuiSafe.TextColored(Orange, $"EQUIPPED in {Edit.Name}");
        else
            ImGui.TextColored(Dim, "EQUIP it (or double-click its tile) to use it in this loadout"u8);
        ImGui.PushTextWrapPos(pos.X - ImGui.GetWindowPos().X + size.X * 0.62f);
        ImGui.PushStyleColor(ImGuiCol.Text, new float4(0.78f, 0.79f, 0.83f, 1f));
        ImGuiSafe.TextWrapped(shown.Blurb);
        ImGui.PopStyleColor();
        ImGui.PopTextWrapPos();
        ImGui.EndGroup();
        var textBottom = ImGui.GetItemRectMax().Y;

        // ---- Attachments button + sound tests (top right) ------------------------------------
        {
            var options = Attachments.For(shownBase);
            var bw = 210 * _scale;
            var bh = 34 * _scale;
            var right = pos.X + size.X;
            ImGui.SetCursorScreenPos(new float2(right - bw, pos.Y));
            var label = !unlocked
                ? "Attachments###wsatt"
                : options.Count == 0
                    ? "No attachments###wsatt"
                    : $"Attachments  {WeaponSelect.UnlockedAttachments(shownBase, prog.WeaponXp(shownBase.Id))}/{options.Count}###wsatt";
            ImGui.BeginDisabled(!unlocked || options.Count == 0);
            if (ImGui.Button(label, new float2(bw, bh)) && !SecondClick)
                OpenPage(LoadoutPage.Attachments, shownBase.Id); // a gun that isn't equipped opens as a preview
            ImGui.EndDisabled();
            if (unlocked && !isEquipped && options.Count > 0)
                ImGui.SetItemTooltip("Preview its attachments. Picking one equips this gun."u8);

            var style = ImGui.GetStyle();
            var rowW = ImGui.CalcTextSize("Sound:"u8).X + ImGui.CalcTextSize("Fire"u8).X + ImGui.CalcTextSize("Reload"u8).X + ImGui.CalcTextSize("Equip"u8).X
                       + 6 * style.FramePadding.X + 3 * style.ItemSpacing.X;
            ImGui.SetCursorScreenPos(new float2(right - rowW, pos.Y + bh + 6 * _scale));
            ImGui.TextColored(Dim, "Sound:"u8);
            ImGui.SameLine();
            if (ImGui.SmallButton("Fire##wsfire"u8))
                audio.Shot(shown, null);
            ImGui.SameLine();
            if (ImGui.SmallButton("Reload##wsreload"u8))
                audio.Reload(shown, null);
            ImGui.SameLine();
            if (ImGui.SmallButton("Equip##wsequip"u8))
                audio.Equip(shown);
        }

        // ---- Stats panel (bottom) ------------------------------------------------------------
        var rowH = 21 * _scale;
        var statsH = rowH * WeaponSelect.StatCount + 32 * _scale;
        var statsTop = pos.Y + size.Y - statsH;
        var statsW = size.X * 0.64f;

        // ---- The gun, between the title and the stats (drawn behind the UI by Program) --------
        var viewBottom = statsTop - 4 * _scale;
        var viewTop = MathF.Min(textBottom + 4 * _scale, viewBottom - size.Y * 0.3f);
        Preview = new GunsmithPreview(shown, winPos, winPos + winSize, new float2(pos.X, viewTop), new float2(pos.X + size.X, viewBottom));

        dl.AddRectFilled(new float2(pos.X, statsTop), new float2(pos.X + statsW, pos.Y + size.Y), Panel8, 4 * _scale);
        var lx = pos.X + 10 * _scale;
        if (isEquipped)
            DlText(dl, new float2(lx, statsTop + 7 * _scale), Dim8, "YOUR EQUIPPED WEAPON"u8);
        else
            DlText(dl, new float2(lx, statsTop + 7 * _scale), Dim8, $"STATS COMPARED TO {WeaponSelect.UpperName(equipped)}");
        var bx0 = pos.X + 120 * _scale;
        var bx1 = pos.X + statsW - 128 * _scale;
        var fh = ImGui.GetFontSize();
        for (var i = 0; i < WeaponSelect.StatCount; i++)
        {
            var s = (SelectStat)i;
            var y = statsTop + 28 * _scale + i * rowH;
            DlText(dl, new float2(lx, y), Dim8, WeaponSelect.StatName(s));
            var before = MathF.Max(WeaponSelect.Score(equipped, s), 0.02f);
            var after = MathF.Max(WeaponSelect.Score(shown, s), 0.02f);
            var barW = MathF.Max(0, bx1 - bx0);
            var b0 = new float2(bx0, y + (fh - 5 * _scale) / 2);
            dl.AddRectFilled(b0, b0 + new float2(barW, 5 * _scale), new ImColor8(48, 52, 60, 255));
            var common = MathF.Min(before, after);
            dl.AddRectFilled(b0, b0 + new float2(barW * common, 5 * _scale), new ImColor8(205, 208, 214, 255));
            var d = WeaponSelect.Delta(equipped, shown, s);
            if (d != 0)
                dl.AddRectFilled(b0 + new float2(barW * common, 0), b0 + new float2(barW * MathF.Max(before, after), 5 * _scale), d > 0 ? Green8 : Red8);
            StatValueText(dl, new float2(bx1 + 10 * _scale, y), shown, s);
            var dx = pos.X + statsW - 36 * _scale;
            if (d > 0)
                DlText(dl, new float2(dx, y), Green8, $"+{d}");
            else if (d < 0)
                DlText(dl, new float2(dx, y), Red8, $"{d}");
        }

        // ---- Magazine, reload, shots to kill and the sound tests (bottom right) --------------
        var rx = pos.X + statsW + 10 * _scale;
        var rMax = new float2(pos.X + size.X, pos.Y + size.Y);
        dl.AddRectFilled(new float2(rx - 2 * _scale, statsTop), rMax, Panel8, 4 * _scale);
        rx += 8 * _scale;
        var ry = statsTop + 7 * _scale;
        DlText(dl, new float2(rx, ry), Dim8, "ROUNDS"u8);
        ImGui.PushFont(default, 28);
        DlText(dl, new float2(rx, ry + 15 * _scale), White8, $"{shown.Mag}");
        var magW = ImGui.CalcTextSize($"{shown.Mag}").X;
        ImGui.PopFont();
        var dm = shown.Mag - equipped.Mag;
        if (dm > 0)
            DlText(dl, new float2(rx + magW + 8 * _scale, ry + 27 * _scale), Green8, $"+{dm}");
        else if (dm < 0)
            DlText(dl, new float2(rx + magW + 8 * _scale, ry + 27 * _scale), Red8, $"{dm}");
        ry += 46 * _scale;
        DlText(dl, new float2(rx, ry), Dim8, "RELOAD"u8);
        ImGui.PushFont(default, 28);
        DlText(dl, new float2(rx, ry + 15 * _scale), White8, $"{shown.Reload:0.0}s");
        var relW = ImGui.CalcTextSize($"{shown.Reload:0.0}s").X;
        ImGui.PopFont();
        var dr = shown.Reload - equipped.Reload;
        if (dr >= 0.05f)
            DlText(dl, new float2(rx + relW + 8 * _scale, ry + 27 * _scale), Red8, $"+{dr:0.0}s");
        else if (dr <= -0.05f)
            DlText(dl, new float2(rx + relW + 8 * _scale, ry + 27 * _scale), Green8, $"{dr:0.0}s");
        ry += 46 * _scale;
        DlText(dl, new float2(rx, ry), Dim8, "SHOTS TO KILL"u8);
        DlText(dl, new float2(rx, ry + 16 * _scale), White8, $"{shown.ShotsToKill(false)} body  /  {shown.ShotsToKill(true)} head");

        // ---- EQUIP, bottom right: greyed out for a locked gun, "EQUIPPED" for the current one ----
        var eh = 34 * _scale;
        ImGui.SetCursorScreenPos(new float2(rx, rMax.Y - eh - 6 * _scale));
        var canEquip = unlocked && !isEquipped;
        ImGui.BeginDisabled(!canEquip);
        ImGui.PushStyleColor(ImGuiCol.Button, canEquip ? new float4(0.94f, 0.59f, 0.16f, 0.92f) : new float4(0.2f, 0.21f, 0.24f, 0.9f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new float4(1f, 0.68f, 0.28f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new float4(0.85f, 0.5f, 0.1f, 1f));
        ImGui.PushStyleColor(ImGuiCol.Text, canEquip ? new float4(0.09f, 0.06f, 0.03f, 1f) : Dim);
        var equipLabel = !unlocked ? $"LOCKED - LEVEL {level}###wsequipbtn" : isEquipped ? "EQUIPPED###wsequipbtn" : "EQUIP###wsequipbtn";
        if (ImGui.Button(equipLabel, new float2(MathF.Max(60 * _scale, rMax.X - 8 * _scale - rx), eh)) && !SecondClick)
            EquipFromSelect(audio, gunSlot, shownBase, gunBase);
        ImGui.PopStyleColor(4);
        ImGui.EndDisabled();
    }

    /// <summary>The raw number beside a stat bar.</summary>
    private static void StatValueText(ImDrawListPtr dl, float2 pos, WeaponDef w, SelectStat s)
    {
        switch (s)
        {
            case SelectStat.Damage when w.Pellets > 1:
                DlText(dl, pos, White8, $"{w.Damage} x {w.Pellets}");
                break;
            case SelectStat.Damage:
                DlText(dl, pos, White8, $"{w.Damage}");
                break;
            case SelectStat.FireRate:
                DlText(dl, pos, White8, $"{w.Rpm} rpm");
                break;
            case SelectStat.Range:
                DlText(dl, pos, White8, $"{w.RangeM:0} m");
                break;
            case SelectStat.Accuracy:
                DlText(dl, pos, White8, $"{w.HipSpreadDeg:0.0} deg");
                break;
            case SelectStat.Control:
                DlText(dl, pos, White8, $"{w.RecoilDeg:0.0} deg");
                break;
            case SelectStat.Mobility:
                DlText(dl, pos, White8, $"{w.Mobility * 100:0}%");
                break;
            default:
                DlText(dl, pos, White8, $"{w.AdsTime:0.00}s aim");
                break;
        }
    }

    /// <summary>Text at a screen position in the current font and size (optionally clipped to a rectangle).</summary>
    private static void DlText(ImDrawListPtr dl, float2 pos, ImColor8 col, ImString text, float4? clip = null) =>
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(), pos, col, text, 0f, clip);

    /// <summary>A small padlock centred on <paramref name="c"/>, about <paramref name="s"/> pixels tall.</summary>
    private static void DrawPadlock(ImDrawListPtr dl, float2 c, float s, ImColor8 col)
    {
        var t = MathF.Max(1.2f, 0.13f * s);
        var bodyMin = c + new float2(-0.42f * s, -0.04f * s);
        var bodyMax = c + new float2(0.42f * s, 0.5f * s);
        var r = 0.25f * s;
        var arc = c + new float2(0, -0.2f * s);
        dl.PathArcTo(arc, r, MathF.PI, MathF.Tau, 12);
        dl.PathStroke(col, ImDrawFlags.None, t);
        dl.AddLine(arc - new float2(r, 0), new float2(arc.X - r, bodyMin.Y), col, t);
        dl.AddLine(arc + new float2(r, 0), new float2(arc.X + r, bodyMin.Y), col, t);
        dl.AddRectFilled(bodyMin, bodyMax, col, 0.1f * s);
        dl.AddCircleFilled(c + new float2(0, 0.22f * s), 0.09f * s, new ImColor8(15, 15, 18, 255), 8);
    }
}
