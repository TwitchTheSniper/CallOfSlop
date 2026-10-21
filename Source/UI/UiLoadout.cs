using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// A gun picture on a loadout card (ImGui pixel coordinates). Program draws these over the UI.
/// Optional: a clip rectangle (e.g. a scrolling list's visible area; the picture is cut to it, and
/// skipped when it's entirely outside) and a dim amount (0-1, a dark veil over the picture, for locked guns).
/// </summary>
public readonly record struct GunThumb(WeaponDef Def, float2 Min, float2 Max, float2 ClipMin = default, float2 ClipMax = default, float Dim = 0f)
{
    /// <summary>Has a clip rectangle been given?</summary>
    public bool Clipped => ClipMax.X > ClipMin.X && ClipMax.Y > ClipMin.Y;
}

/// <summary>
/// The loadout screen, used from the main menu and in matches. Left: your loadout slots (more unlock
/// as you level up). Right: the selected loadout as cards (primary, secondary, perks, lethal, tactical);
/// click a card to change that part. The pages form a stack (<see cref="LoadoutNav"/>): the footer's
/// Back button and Esc go back one page. A progress bar in the footer opens the progression page.
/// </summary>
public sealed partial class Ui
{
    private readonly LoadoutNav _nav = new();
    private int _editSlot;
    private PerkCategory _perkFocus;
    private PerkDef? _hoverPerk;
    private ImInputString? _renameInput;
    private bool _renaming;
    private int _hotCard = -1; // card under the mouse last frame (highlight)
    private bool _loadoutDrawn; // was the loadout screen on screen this frame?
    private (float2 Min, float2 Max) _loadoutRect; // where the loadout window was this frame
    private double _actTime = -10; // ImGui time of the last click that picked something or changed page (see SecondClick)

    /// <summary>Gun pictures for the cards this frame (drawn by Program after the UI).</summary>
    public readonly List<GunThumb> Thumbs = new();

    /// <summary>The loadout screen opened from the main menu (no match running).</summary>
    public bool LobbyLoadouts;

    private static readonly float4 SelectedBg = new(0.16f, 0.2f, 0.26f, 0.97f);
    private static readonly float4 LockedBg = new(0.07f, 0.07f, 0.08f, 0.85f);

    private Loadout Edit => LoadoutStore.Slots[Math.Clamp(_editSlot, 0, LoadoutStore.Slots.Length - 1)];

    /// <summary>Attachments on a gun in the loadout being edited (or the gun's last build if it isn't in it).</summary>
    private uint KitOf(WeaponDef w)
    {
        if (LoadoutStore.SmokeKit is { } smoke)
            return Attachments.Sanitize(w, smoke(w.Id));
        var kit = w.Id == Edit.Primary ? Edit.PrimaryKit : w.Id == Edit.Secondary ? Edit.SecondaryKit : LoadoutStore.KitMemory(w.Id);
        return Attachments.Sanitize(w, kit, Progress);
    }

    /// <summary>Is the gun in the loadout being edited?</summary>
    private bool InEdit(WeaponDef w) => w.Id == Edit.Primary || w.Id == Edit.Secondary;

    /// <summary>
    /// Fits attachments to a gun. A gun that isn't in the loadout yet (its attachments were opened from
    /// the weapon select as a preview) is equipped into its slot first, with the usual equip sound.
    /// </summary>
    private void SetKitOf(Audio audio, WeaponDef w, uint kit)
    {
        if (!InEdit(w) && WeaponSelect.UnlockLevel(w) <= Progress.Level)
        {
            LoadoutStore.SetWeapon(Edit, (int)w.Slot, w.Id, Progress);
            audio.Equip(w);
        }
        if (w.Id == Edit.Primary)
            LoadoutStore.SetKit(Edit, 0, kit);
        else if (w.Id == Edit.Secondary)
            LoadoutStore.SetKit(Edit, 1, kit);
        LoadoutStore.Save();
    }

    /// <summary>Opens a page of the loadout screen on top of the current one.</summary>
    private void OpenPage(LoadoutPage page, int arg = -1)
    {
        if (page is LoadoutPage.Primary or LoadoutPage.Secondary)
            _wsFresh = true; // start the weapon select on the equipped gun
        _nav.Open(page, arg);
        Acted();
        _hoverThrowable = null;
        _hoverAttachment = null;
        _hoverPerk = null;
        _openSlot = null;
    }

    /// <summary>
    /// Back one page (the footer's Back button and Esc). On the overview it closes the screen: back to the
    /// main menu, or the menu closes in a match. Returns true when the screen closed.
    /// </summary>
    public bool LoadoutBack()
    {
        if (_renaming)
        {
            _renaming = false; // Esc cancels a rename first
            return false;
        }
        if (_openSlot != null)
            return false; // an attachment list is open: ImGui closes it on Esc
        return GoBack();
    }

    /// <summary>The footer's Back button: one page back, or close the screen from the overview.</summary>
    private bool GoBack()
    {
        _renaming = false;
        _wsSmoke = false;
        Acted();
        if (!_nav.Back())
        {
            _hoverThrowable = null;
            _hoverAttachment = null;
            _hoverPerk = null;
            return false;
        }
        LoadoutStore.Save();
        if (LobbyLoadouts)
            LobbyLoadouts = false;
        else
            MenuOpen = false;
        return true;
    }

    private void ResetLoadoutScreen()
    {
        _nav.Reset();
        _wsSmoke = false;
        _renaming = false;
        _editSlot = Math.Clamp(LoadoutStore.Selected, 0, Math.Max(0, Progress.LoadoutSlots - 1));
        _hoverThrowable = null;
        _hoverAttachment = null;
        _hoverPerk = null;
    }

    /// <summary>Main menu > Loadouts.</summary>
    public void OpenLobbyLoadouts()
    {
        LobbyLoadouts = true;
        ResetLoadoutScreen();
    }

    // ---- Deploying -----------------------------------------------------------------------

    /// <summary>Deploys the selected loadout and closes the menu (used when the countdown hits GO).</summary>
    public void AutoDeploy(Game game, double now)
    {
        if (!MenuOpen)
            return;
        ApplySelection(game, now);
        MenuOpen = false;
    }

    private void ApplySelection(Game game, double now)
    {
        LoadoutStore.Selected = _editSlot;
        game.ChooseLoadout(LoadoutStore.Active(Progress), now);
        LoadoutStore.Save();
    }

    // Smoke-test hooks (see Program.SmokeTick)
    /// <summary>
    /// Smoke test: 4/5 = primary/secondary attachments (opened through the weapon select, so Back would
    /// return there), 6 = perks, 7 = the progression page, 8 = kill streaks, 0 = back to the overview.
    /// </summary>
    public void SmokeTab(int tab)
    {
        _nav.Reset();
        _wsSmoke = false;
        var eff = LoadoutStore.Effective(Edit, Progress);
        switch (tab)
        {
            case 4:
            case 5:
                OpenPage(tab == 4 ? LoadoutPage.Primary : LoadoutPage.Secondary);
                OpenPage(LoadoutPage.Attachments, eff.WeaponIn(tab - 4));
                break;
            case 6:
                OpenPage(LoadoutPage.Perks);
                break;
            case 7:
                OpenPage(LoadoutPage.Progression);
                break;
            case 8:
                OpenPage(LoadoutPage.Streaks);
                break;
        }
    }

    public void SmokeRespawn(Game game, double now)
    {
        ApplySelection(game, now);
        game.RequestRespawn();
        MenuOpen = false;
    }

    public void SmokeSelect(byte primary, byte secondary)
    {
        LoadoutStore.SetWeapon(Edit, 0, primary, Progress);
        LoadoutStore.SetWeapon(Edit, 1, secondary, Progress);
    }

    // ---- Entry points ----------------------------------------------------------------------

    /// <summary>In a match: draws the loadout screen if it's open. Returns true when the player deployed / respawned.</summary>
    public bool DrawLoadoutMenu(Game game, double now)
    {
        if (!MenuOpen)
            return false;
        return DrawLoadoutScreen(game, game.Audio, now);
    }

    /// <summary>From the main menu.</summary>
    public void DrawLobbyLoadouts(Audio audio, double now)
    {
        if (LobbyLoadouts)
            DrawLoadoutScreen(null, audio, now);
    }

    private bool DrawLoadoutScreen(Game? game, Audio audio, double now)
    {
        Preview = null;
        Thumbs.Clear();
        _loadoutDrawn = true;
        TrackSecondClick();

        var deployed = false;
        var prog = Progress;
        if (_editSlot >= prog.LoadoutSlots)
            _editSlot = 0;
        var vp = ImGui.GetMainViewport();
        var center = new float2(vp.WorkPos.X + vp.WorkSize.X / 2, vp.WorkPos.Y + vp.WorkSize.Y / 2);
        ImGui.SetNextWindowPos(center, ImGuiCond.Always, new float2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new float2(MathF.Min(1150 * _scale, vp.WorkSize.X - 40), MathF.Min(760 * _scale, vp.WorkSize.Y - 40)));

        // In the attachment showcase the window background is painted by Program (behind the 3D gun) instead.
        // No scrollbar: the footer is pinned to the bottom edge and every page sizes itself to the body area.
        var winFlags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings
                       | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse
                       | (_showcaseLastFrame ? ImGuiWindowFlags.NoBackground : ImGuiWindowFlags.None);
        _showcaseLastFrame = false;
        if (ImGui.Begin("LOADOUTS"u8, winFlags))
        {
            var winPos = ImGui.GetWindowPos();
            var winSize = ImGui.GetWindowSize();
            _loadoutRect = (winPos, winPos + winSize); // the kill card sits beside it if there's room
            var style = ImGui.GetStyle();
            var contentMin = winPos + style.WindowPadding;
            var contentMax = winPos + winSize - style.WindowPadding;
            var footerH = 46 * _scale;
            var footerTop = contentMax.Y - footerH;

            // Breadcrumb on the sub-pages (the one Back is in the footer).
            if (_nav.Depth > 1)
                Breadcrumb();

            _bodyTopY = ImGui.GetCursorScreenPos().Y;
            var body = new float2(ImGui.GetContentRegionAvail().X, MathF.Max(100 * _scale, footerTop - 8 * _scale - _bodyTopY));
            switch (_nav.Current)
            {
                case LoadoutPage.Overview:
                    DrawOverview(game, body);
                    break;
                case LoadoutPage.Progression:
                    DrawProgressionPage(body);
                    break;
                default:
                    DrawEditView(audio, body, winPos, winSize);
                    break;
            }

            // ---- Footer: buttons on the left, the level bar on the right -------------------
            ImGui.SetCursorScreenPos(new float2(contentMin.X, footerTop + (footerH - 40 * _scale) / 2));
            var btn = new float2(200 * _scale, 40 * _scale);
            var backBtn = new float2(150 * _scale, 40 * _scale);
            var name = Edit.Name;
            string hint;
            if (game != null)
            {
                var dead = game.Local is not { Alive: true };
                if (dead)
                {
                    var wait = game.RespawnWait(now);
                    if (ImGui.Button(wait > 0 ? $"RESPAWN ({wait:0.0})###respawn" : "RESPAWN###respawn", btn))
                    {
                        ApplySelection(game, now);
                        game.RequestRespawn();
                        MenuOpen = false;
                        deployed = true;
                    }
                    ImGui.SameLine();
                }
                else if (game.CanChangeLoadout)
                {
                    if (ImGui.Button("DEPLOY"u8, btn))
                    {
                        ApplySelection(game, now);
                        MenuOpen = false;
                        deployed = true;
                    }
                    ImGui.SameLine();
                }
                ReadyButton(game, backBtn); // warmup only (UiModes.cs)
                if (ImGui.Button(_nav.Depth > 1 ? "< BACK###back"u8 : "CLOSE###back"u8, backBtn))
                    GoBack();
                hint = game.Mode == GameMode.GunGame ? "Gun Game: everyone gets the same gun, so your loadout isn't used this match."
                    : game.IsWarmup ? $"Warmup: \"{name}\" applies right away. Ready up when you're set."
                    : dead
                    ? $"Respawn with \"{name}\". Loadouts only change between lives."
                    : game.CanChangeLoadout
                        ? $"Pre-match: \"{name}\" applies right away."
                        : "Edit freely - the selected loadout is used the next time you spawn.";
            }
            else
            {
                if (ImGui.Button("< BACK###back"u8, btn))
                    GoBack();
                hint = $"Saved automatically. You'll spawn with \"{name}\".";
            }

            // Level bar, right-aligned; the hint text fills the gap (there's room for one more button).
            var barW = MathF.Min(430 * _scale, (contentMax.X - contentMin.X) * 0.38f);
            var barMin = new float2(contentMax.X - barW, footerTop);
            ImGui.SameLine();
            var hintX = ImGui.GetCursorScreenPos().X + 6 * _scale;
            var hintW = barMin.X - 14 * _scale - hintX;
            if (hintW > 90 * _scale)
            {
                ImGui.SetCursorScreenPos(new float2(hintX, footerTop + 4 * _scale));
                if (ImGui.BeginChild("##hint"u8, new float2(hintW, footerH - 4 * _scale), ImGuiChildFlags.None,
                        ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoBackground))
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, Dim);
                    ImGuiSafe.TextWrapped(hint);
                    ImGui.PopStyleColor();
                }
                ImGui.EndChild();
            }
            LevelBar(barMin, new float2(barW, footerH));
        }
        ImGui.End();
        return deployed;
    }

    /// <summary>Where you are: "Loadout name / Primary weapon / Kestrel PDW attachments".</summary>
    private void Breadcrumb()
    {
        ImGuiSafe.TextColored(Dim, Edit.Name);
        for (var i = 1; i < _nav.Depth; i++)
        {
            var e = _nav[i];
            ImGui.SameLine();
            ImGui.TextColored(Dim, " / "u8);
            ImGui.SameLine();
            var label = e.Page switch
            {
                LoadoutPage.Primary => "Primary weapon",
                LoadoutPage.Secondary => "Secondary weapon",
                LoadoutPage.Lethal => "Lethal",
                LoadoutPage.Tactical => "Tactical",
                LoadoutPage.Perks => "Perks",
                LoadoutPage.Attachments => $"{Weapons.Get((byte)Math.Clamp(e.Arg, 0, 255)).Name} attachments",
                LoadoutPage.Streaks => "Kill streaks",
                _ => "Progression",
            };
            if (i == _nav.Depth - 1)
                ImGui.Text(label);
            else
                ImGuiSafe.TextColored(Dim, label);
        }
        ImGui.Spacing();
    }

    /// <summary>The progression page: level, XP, stats and every unlock by level.</summary>
    private void DrawProgressionPage(float2 size)
    {
        var prog = Progress;
        if (ImGui.BeginChild("##progression"u8, size, ImGuiChildFlags.None))
        {
            LevelSummary();
            ImGuiSafe.TextColored(Dim, $"Kills {prog.Kills}   Headshots {prog.Headshots}   Matches {prog.Matches}   Wins {prog.Wins}   Loadout slots {prog.LoadoutSlots}/{CallOfSlop.Progress.MaxLoadoutSlots}");
            ImGui.Spacing();
            ProgressionTable();
        }
        ImGui.EndChild();
    }

    /// <summary>
    /// The level bar in the footer: LEVEL N, XP into the level, the next unlock. Clicking it opens the
    /// progression page (or goes back from it).
    /// </summary>
    private void LevelBar(float2 min, float2 size)
    {
        var p = Progress;
        var open = _nav.Current == LoadoutPage.Progression;
        ImGui.SetCursorScreenPos(min);
        if (ImGui.InvisibleButton("##levelbar"u8, size))
        {
            if (open)
                GoBack();
            else
                OpenPage(LoadoutPage.Progression);
        }
        var hot = ImGui.IsItemHovered();
        if (hot)
            ImGui.SetItemTooltip(open ? "Back"u8 : "Show every unlock by level"u8);
        var dl = ImGui.GetWindowDrawList();
        var max = min + size;
        var r = 4 * _scale;
        dl.AddRectFilled(min, max, open ? new ImColor8(44, 38, 32, 245) : hot ? Hover8 : Card8, r);
        if (open || hot)
            dl.AddRect(min, max, Orange8, r, ImDrawFlags.None, 1.5f * _scale);
        var pad = 10 * _scale;
        var clip = new float4(min.X, min.Y, max.X, max.Y);
        var maxed = p.Level >= CallOfSlop.Progress.MaxLevel;

        ImGui.PushFont(default, 18);
        DlText(dl, new float2(min.X + pad, min.Y + 3 * _scale), Orange8, $"LEVEL {p.Level}", clip);
        ImGui.PopFont();
        ImGui.PushFont(default, 14);
        var link = open ? "< BACK"u8 : "ALL UNLOCKS >"u8;
        var lw = ImGui.CalcTextSize(link).X;
        DlText(dl, new float2(max.X - pad - lw, min.Y + 6 * _scale), hot || open ? White8 : new ImColor8(115, 190, 242, 255), link, clip);

        var b0 = new float2(min.X + pad, min.Y + 24 * _scale);
        var bw = size.X - 2 * pad;
        dl.AddRectFilled(b0, b0 + new float2(bw, 4 * _scale), new ImColor8(48, 52, 60, 255));
        var f = maxed ? 1f : Math.Clamp(p.LevelFraction, 0f, 1f);
        if (f > 0)
            dl.AddRectFilled(b0, b0 + new float2(bw * f, 4 * _scale), Orange8);

        var cy = min.Y + 30 * _scale;
        var xp = maxed ? $"MAX LEVEL  -  {p.Xp:N0} XP" : $"{p.XpIntoLevel:N0} / {CallOfSlop.Progress.XpForNext(p.Level):N0} XP";
        DlText(dl, new float2(min.X + pad, cy), Dim8, xp, clip);
        if (p.NextUnlock is { } next)
        {
            var xw = ImGui.CalcTextSize(xp).X;
            var text = $"Next: {next.Name} at {next.Level}";
            var tw = ImGui.CalcTextSize(text).X;
            var tx = MathF.Max(min.X + pad + xw + 14 * _scale, max.X - pad - tw);
            DlText(dl, new float2(tx, cy), Dim8, text, new float4(min.X, min.Y, max.X - pad, max.Y));
        }
        ImGui.PopFont();
    }

    // ---- Overview: slot list + cards ----------------------------------------------------------

    private void DrawOverview(Game? game, float2 size)
    {
        var prog = Progress;
        var hot = -1;
        var leftW = size.X * 0.3f;
        if (ImGui.BeginChild("##slots"u8, new float2(leftW, size.Y), ImGuiChildFlags.None))
        {
            var rowH = 50 * _scale;
            for (var i = 0; i < LoadoutStore.Slots.Length; i++)
            {
                var l = LoadoutStore.Slots[i];
                var level = CallOfSlop.Progress.LoadoutSlotLevel(i);
                var unlocked = prog.Level >= level;
                var selected = i == _editSlot && unlocked;
                ImGui.PushStyleColor(ImGuiCol.ChildBg, !unlocked ? LockedBg : selected ? SelectedBg : _hotCard == 10 + i ? CardHover : CardBg);
                if (ImGui.BeginChild($"##slot{i}", new float2(ImGui.GetContentRegionAvail().X, rowH), ImGuiChildFlags.Borders,
                        ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
                {
                    ImGui.PushFont(default, 18);
                    ImGuiSafe.TextColored(!unlocked ? Dim : selected ? new float4(0.55f, 0.85f, 1f, 1f) : new float4(1, 1, 1, 1), unlocked ? l.Name : DefaultLockedName(i));
                    ImGui.PopFont();
                    var eff = LoadoutStore.Effective(l, prog);
                    ImGuiSafe.TextColored(Dim, unlocked
                        ? $"{Weapons.Get(eff.Primary).Name} + {Weapons.Get(eff.Secondary).Name}"
                        : $"Unlocks at level {level}");
                    if (selected)
                    {
                        // Orange marker: this is the loadout you'll spawn with.
                        var dl = ImGui.GetWindowDrawList();
                        var wp = ImGui.GetWindowPos();
                        var a = wp;
                        var b = wp + new float2(4 * _scale, rowH);
                        ImDrawListExtensions.AddRectFilled(dl, in a, in b, new ImColor8(240, 150, 40, 255), 0, ImDrawFlags.None);
                        var star = wp + new float2(ImGui.GetWindowSize().X - 16 * _scale, 16 * _scale);
                        DrawStar(dl, star, 8 * _scale, new ImColor8(240, 150, 40, 255));
                    }
                }
                ImGui.EndChild();
                ImGui.PopStyleColor();
                if (ImGui.IsItemHovered() && unlocked)
                    hot = 10 + i;
                if (ImGui.IsItemClicked(ImGuiMouseButton.Left) && unlocked && i != _editSlot)
                {
                    _editSlot = i;
                    LoadoutStore.Selected = i;
                    LoadoutStore.Save();
                    _renaming = false;
                }
            }
        }
        ImGui.EndChild();

        ImGui.SameLine();
        if (ImGui.BeginChild("##cards"u8, new float2(0, size.Y), ImGuiChildFlags.None, ImGuiWindowFlags.NoScrollbar))
        {
            var l = Edit;
            var eff = LoadoutStore.Effective(l, prog);

            // Name + rename
            ImGui.PushFont(default, 26);
            ImGuiSafe.TextColored(new float4(0.55f, 0.85f, 1f, 1f), l.Name);
            ImGui.PopFont();
            ImGui.SameLine(0, 16 * _scale);
            if (!_renaming)
            {
                if (ImGui.Button("Rename"u8))
                {
                    _renameInput ??= new ImInputString(25);
                    ImGuiSafe.SetInput(_renameInput, l.Name);
                    _renaming = true;
                }
            }
            else
            {
                ImGui.SetNextItemWidth(240 * _scale);
                bool enter;
                try
                {
                    enter = ImGui.InputText("##rename"u8, _renameInput!, ImGuiInputTextFlags.EnterReturnsTrue);
                }
                catch (Exception ex)
                {
                    ImGuiSafe.FieldFailed("loadout name", ex);
                    enter = false;
                }
                ImGui.SameLine();
                if (ImGui.Button("OK"u8) || enter)
                {
                    l.Name = LoadoutStore.CleanName(_renameInput!.ToString());
                    LoadoutStore.Save();
                    _renaming = false;
                }
                ImGui.SameLine();
                if (ImGui.Button("Cancel"u8))
                    _renaming = false;
            }
            ImGui.TextColored(Dim, game != null && game.Local is { Alive: true } && !game.CanChangeLoadout
                ? "Click a card to change it. Changes apply the next time you spawn."u8
                : "Click a card to change it."u8);

            var w = ImGui.GetContentRegionAvail().X;
            var gunH = 124 * _scale;
            for (var slot = 0; slot < 2; slot++)
            {
                var click = GunCard(slot, eff, w, gunH, ref hot);
                if (click == CardClick.Card)
                    OpenPage(slot == 0 ? LoadoutPage.Primary : LoadoutPage.Secondary);
                else if (click == CardClick.Attachments)
                    OpenPage(LoadoutPage.Attachments, eff.WeaponIn(slot));
            }
            if (PerksCard(eff.Perks, w, 108 * _scale, ref hot) is { } cat)
            {
                _perkFocus = cat;
                OpenPage(LoadoutPage.Perks);
            }
            var gap = ImGui.GetStyle().ItemSpacing.X;
            var half = (w - gap) / 2;
            if (ThrowableCard(3, "Lethal", eff.Grenade, half, 70 * _scale, ref hot))
                OpenPage(LoadoutPage.Lethal);
            ImGui.SameLine();
            if (ThrowableCard(4, "Tactical", eff.Equipment, half, 70 * _scale, ref hot))
                OpenPage(LoadoutPage.Tactical);
            if (StreaksCard(eff.Streaks, w, 76 * _scale, ref hot))
                OpenPage(LoadoutPage.Streaks);

            if (!SameAsStored(l, eff))
                ImGui.TextColored(Orange, "Some items in this loadout are locked at your level, so defaults stand in for them."u8);
        }
        ImGui.EndChild();
        _hotCard = hot;
    }

    private static string DefaultLockedName(int slot) => $"Loadout slot {slot + 1}";

    private static bool SameAsStored(Loadout l, Loadout eff) =>
        l.Primary == eff.Primary && l.Secondary == eff.Secondary && l.Grenade == eff.Grenade && l.Equipment == eff.Equipment && l.Perks == eff.Perks
        && l.Streaks.Sanitized() == eff.Streaks;

    private enum CardClick
    {
        None,
        Card,
        Attachments,
    }

    /// <summary>
    /// A weapon card: name, class, weapon XP and attachment dots, with the gun drawn on the right, and an
    /// "Edit attachments" button beside the name. Says whether the card or the button was clicked.
    /// </summary>
    private CardClick GunCard(int slot, Loadout eff, float width, float height, ref int hot)
    {
        var def = Weapons.Get(eff.WeaponIn(slot));
        var kit = eff.KitFor(slot);
        var pos = ImGui.GetCursorScreenPos();
        bool buttonClicked = false, overButton = false;
        ImGui.PushStyleColor(ImGuiCol.ChildBg, _hotCard == slot ? CardHover : CardBg);
        if (ImGui.BeginChild($"##gun{slot}", new float2(width, height), ImGuiChildFlags.Borders, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            ImGui.TextColored(Orange, slot == 0 ? "Primary Weapon"u8 : "Secondary Weapon"u8);
            var nameY = ImGui.GetCursorScreenPos().Y;
            ImGui.PushFont(default, 22);
            ImGui.Text(def.Name);
            var nameW = ImGui.GetItemRectSize().X;
            ImGui.PopFont();
            ImGuiSafe.TextColored(Dim, $"{def.ClassName} - {def.ModeName}");
            var textW = MathF.Max(nameW, ImGui.GetItemRectSize().X);
            var after = ImGui.GetCursorScreenPos();

            // "Edit attachments", right of the name and class (the gun picture starts at 52% of the width).
            var takes = Attachments.For(def).Count > 0;
            var bw = 150 * _scale;
            var bx = MathF.Min(pos.X + 10 * _scale + textW + 28 * _scale, pos.X + width * 0.52f - bw - 8 * _scale);
            ImGui.SetCursorScreenPos(new float2(MathF.Max(bx, pos.X + 10 * _scale), nameY + 2 * _scale));
            ImGui.BeginDisabled(!takes);
            if (ImGui.Button(takes ? $"Edit attachments##ea{slot}" : $"No attachments##ea{slot}", new float2(bw, 30 * _scale)))
                buttonClicked = true;
            ImGui.EndDisabled();
            overButton = ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled);
            ImGui.SetCursorScreenPos(after);
            var fitted = Attachments.InKit(kit).ToArray();
            ImGui.PushTextWrapPos(width * 0.5f); // keep clear of the gun picture on the right
            ImGui.PushStyleColor(ImGuiCol.Text, SlotBlue);
            ImGuiSafe.TextWrapped(fitted.Length == 0 ? "No attachments" : string.Join(", ", fitted.Select(a => a.Name)));
            ImGui.PopStyleColor();
            ImGui.PopTextWrapPos();

            var options = Attachments.For(def);
            var wxp = Progress.WeaponXp(def.Id);
            var max = Attachments.MaxXp(def);
            ImGui.SetCursorScreenPos(pos + new float2(10 * _scale, height - 30 * _scale));
            ImGuiSafe.TextColored(Dim, options.Count == 0
                ? "Takes no attachments"
                : $"Weapon XP {Math.Min(wxp, max):N0} / {max:N0}  -  {options.Count(a => wxp >= Attachments.UnlockXp(def, a))}/{options.Count} unlocked");
            ImGui.SetCursorScreenPos(pos + new float2(10 * _scale, height - 10 * _scale));
            ImGui.PushStyleColor(ImGuiCol.PlotHistogram, new float4(0.4f, 0.95f, 1f, 1f));
            ImGui.ProgressBar(options.Count == 0 ? 0 : Math.Clamp(wxp / (float)max, 0f, 1f), new float2(width * 0.42f, 4 * _scale), ""u8);
            ImGui.PopStyleColor();

            // Attachment dots, top right (like the slot cards in the gunsmith)
            var slots = Enumerable.Range(0, Attachments.SlotCount).Count(sl => options.Any(a => (int)a.Slot == sl));
            var dl = ImGui.GetWindowDrawList();
            for (var i = 0; i < slots; i++)
            {
                var c = pos + new float2(width - 16 * _scale - (slots - 1 - i) * 18 * _scale, 16 * _scale);
                if (i < fitted.Length)
                    ImDrawListExtensions.AddCircleFilled(dl, in c, 5 * _scale, new ImColor8(240, 150, 40, 255), 16);
                var c2 = c;
                ImDrawListExtensions.AddCircle(dl, in c2, 7 * _scale, new ImColor8(240, 150, 40, i < fitted.Length ? (byte)200 : (byte)80), 16, 1.5f);
            }
        }
        ImGui.EndChild();
        ImGui.PopStyleColor();
        if (ImGui.IsItemHovered() && !overButton)
            hot = slot;
        // The button's own click mustn't also open the weapon select.
        var clicked = !overButton && ImGui.IsItemClicked(ImGuiMouseButton.Left) && !SecondClick;
        Thumbs.Add(new GunThumb(Attachments.Apply(def, kit), pos + new float2(width * 0.52f, 22 * _scale), pos + new float2(width - 10 * _scale, height - 6 * _scale)));
        return buttonClicked ? CardClick.Attachments : clicked ? CardClick.Card : CardClick.None;
    }

    /// <summary>The three perks with their icons. Returns the category clicked, if any.</summary>
    private PerkCategory? PerksCard(PerkSet perks, float width, float height, ref int hot)
    {
        var pos = ImGui.GetCursorScreenPos();
        PerkCategory? clicked = null;
        ImGui.PushStyleColor(ImGuiCol.ChildBg, _hotCard == 2 ? CardHover : CardBg);
        if (ImGui.BeginChild("##perkcard"u8, new float2(width, height), ImGuiChildFlags.Borders, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            ImGui.TextColored(Orange, "Perks"u8);
            var dl = ImGui.GetWindowDrawList();
            var colW = (width - 20 * _scale) / 3;
            for (var c = 0; c < 3; c++)
            {
                var cat = (PerkCategory)c;
                var def = Perks.Get(perks[cat]);
                var x0 = pos.X + 10 * _scale + c * colW;
                var iconC = new float2(x0 + 24 * _scale, pos.Y + 60 * _scale);
                DrawPerkIcon(dl, perks[cat], cat, iconC, 20 * _scale);
                ImGui.SetCursorScreenPos(new float2(x0 + 52 * _scale, pos.Y + 36 * _scale));
                var col = Perks.Color(cat);
                ImGuiSafe.TextColored(new float4(col.X, col.Y, col.Z, 1f), Perks.CategoryName(cat).ToUpperInvariant());
                ImGui.SetCursorScreenPos(new float2(x0 + 52 * _scale, pos.Y + 54 * _scale));
                ImGui.Text(def.Id == PerkId.None ? "Empty" : def.Name);
                ImGui.SetCursorScreenPos(new float2(x0 + 52 * _scale, pos.Y + 72 * _scale));
                ImGuiSafe.TextColored(Dim, def.Id == PerkId.None ? "-" : def.When);
            }
        }
        ImGui.EndChild();
        ImGui.PopStyleColor();
        if (ImGui.IsItemHovered())
            hot = 2;
        if (ImGui.IsItemClicked(ImGuiMouseButton.Left) && !SecondClick)
        {
            var mx = ImGui.GetMousePos().X - pos.X;
            clicked = (PerkCategory)Math.Clamp((int)(mx / (width / 3)), 0, 2);
        }
        return clicked;
    }

    private bool ThrowableCard(int key, string title, ThrowableKind kind, float width, float height, ref int hot)
    {
        var def = Throwables.Get(kind);
        var pos = ImGui.GetCursorScreenPos();
        ImGui.PushStyleColor(ImGuiCol.ChildBg, _hotCard == key ? CardHover : CardBg);
        if (ImGui.BeginChild($"##throw{key}", new float2(width, height), ImGuiChildFlags.Borders, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            ImGuiSafe.TextColored(Orange, title);
            ImGui.Text(def.Kind == ThrowableKind.None ? "Empty" : def.Name);
            ImGuiSafe.TextColored(Dim, def.Kind == ThrowableKind.None ? "-" : $"{def.PerLife} per life");
            if (def.Kind != ThrowableKind.None)
            {
                // Simple grenade icon in the card's colour
                var dl = ImGui.GetWindowDrawList();
                var c = pos + new float2(width - 34 * _scale, height / 2 + 4 * _scale);
                var col = new ImColor8((byte)(def.Color.X * 255), (byte)(def.Color.Y * 255), (byte)(def.Color.Z * 255), 255);
                if (def.IsEquipment)
                    ImDrawListExtensions.AddNgonFilled(dl, in c, 15 * _scale, col, 6);
                else
                    ImDrawListExtensions.AddCircleFilled(dl, in c, 14 * _scale, col, 20);
                var top = c - new float2(4 * _scale, 22 * _scale);
                var top2 = c + new float2(4 * _scale, -12 * _scale);
                ImDrawListExtensions.AddRectFilled(dl, in top, in top2, new ImColor8(150, 150, 155, 255), 1, ImDrawFlags.None);
            }
        }
        ImGui.EndChild();
        ImGui.PopStyleColor();
        if (ImGui.IsItemHovered())
            hot = key;
        return ImGui.IsItemClicked(ImGuiMouseButton.Left) && !SecondClick;
    }

    // ---- Edit views ----------------------------------------------------------------------------

    private void DrawEditView(Audio audio, float2 size, float2 winPos, float2 winSize)
    {
        var prog = Progress;
        var l = Edit;
        var page = _nav.Current;
        var area = new float2(ImGui.GetContentRegionAvail().X, size.Y);

        if (page == LoadoutPage.Attachments)
        {
            _showcaseLastFrame = true;
            DrawShowcase(audio, Weapons.Get((byte)Math.Clamp(_nav.Arg, 0, 255)), area, winPos, winSize);
            return;
        }
        if (page == LoadoutPage.Perks)
        {
            DrawPerkPicker(audio, area);
            return;
        }
        if (page == LoadoutPage.Streaks)
        {
            DrawStreakPicker(audio, area);
            return;
        }
        if (page is LoadoutPage.Primary or LoadoutPage.Secondary)
        {
            var gunSlot = page == LoadoutPage.Secondary ? 1 : 0;
            var gun = Weapons.Get(LoadoutStore.Effective(l, prog).WeaponIn(gunSlot));
            _showcaseLastFrame = true; // the hero gun is drawn behind the UI, like the showcase
            DrawWeaponSelect(audio, gunSlot, gun, area, winPos, winSize);
            return;
        }

        var listWidth = area.X * 0.42f;
        ThrowableDef? hoveredThrowable = null;
        var equipmentTab = page == LoadoutPage.Tactical;
        if (ImGui.BeginChild("##list"u8, new float2(listWidth, area.Y), ImGuiChildFlags.Borders))
        {
            foreach (var t in Throwables.All.Where(t => t.Kind == ThrowableKind.None || t.IsEquipment == equipmentTab))
            {
                var unlocked = prog.IsUnlocked(t.Kind);
                var selected = equipmentTab ? l.Equipment == t.Kind : l.Grenade == t.Kind;
                var label = unlocked ? $"{t.Name}##t{(int)t.Kind}" : $"[LOCKED] {t.Name}  -  level {CallOfSlop.Progress.UnlockLevel(t.Kind)}##t{(int)t.Kind}";
                if (!unlocked)
                    ImGui.PushStyleColor(ImGuiCol.Text, Dim);
                // A click (or a double-click: its second click lands on the same, now picked, item) picks it.
                if (ImGui.Selectable(label, selected) && !SecondClick)
                {
                    if (!unlocked)
                        LockedFeedback(audio);
                    else if (!selected)
                    {
                        if (equipmentTab)
                            l.Equipment = t.Kind;
                        else
                            l.Grenade = t.Kind;
                        LoadoutStore.Save();
                        audio.Effect(Sfx.HitTick, 0.4f);
                    }
                }
                if (!unlocked)
                    ImGui.PopStyleColor();
                if (ImGui.IsItemHovered())
                    hoveredThrowable = t;
            }
        }
        ImGui.EndChild();

        ImGui.SameLine();
        if (ImGui.BeginChild("##stats"u8, new float2(0, area.Y), ImGuiChildFlags.Borders))
        {
            if (hoveredThrowable != null)
                _hoverThrowable = hoveredThrowable;
            var t = _hoverThrowable is { } h && (h.Kind == ThrowableKind.None || h.IsEquipment == equipmentTab)
                ? h
                : Throwables.Get(equipmentTab ? l.Equipment : l.Grenade);
            DrawThrowable(t);
        }
        ImGui.EndChild();
    }

    private float _bodyTopY; // screen Y where the body area starts this frame

    /// <summary>
    /// Is this click the second half of a double-click whose first click just acted (picked something or
    /// changed page)? Then it mustn't act again on whatever is now under the mouse.
    /// </summary>
    private bool SecondClick => _swallowClick;

    private bool _swallowClick;

    /// <summary>Once per frame: notices a double-click whose first click acted, and swallows its second click until the button is released.</summary>
    private void TrackSecondClick()
    {
        if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left) && ImGui.GetTime() - _actTime < 0.5)
            _swallowClick = true;
        else if (_swallowClick && !ImGui.IsMouseDown(ImGuiMouseButton.Left) && !ImGui.IsMouseReleased(ImGuiMouseButton.Left))
            _swallowClick = false;
    }

    /// <summary>Remembers that a click just acted (see <see cref="SecondClick"/>).</summary>
    private void Acted() => _actTime = ImGui.GetTime();

    /// <summary>Clicked something locked: a dry click instead of a pick.</summary>
    private static void LockedFeedback(Audio audio) => audio.Effect(Sfx.DryFire, 0.5f);

    /// <summary>Three columns, one per category: click a perk to carry it in this loadout.</summary>
    private void DrawPerkPicker(Audio audio, float2 area)
    {
        var prog = Progress;
        var l = Edit;
        var gap = ImGui.GetStyle().ItemSpacing.X;
        var colW = (area.X - gap * 2) / 3;
        PerkDef? hovered = null;
        for (var c = 0; c < 3; c++)
        {
            var cat = (PerkCategory)c;
            if (c > 0)
                ImGui.SameLine();
            var col = Perks.Color(cat);
            var col4 = new float4(col.X, col.Y, col.Z, 1f);
            ImGui.PushStyleColor(ImGuiCol.ChildBg, cat == _perkFocus ? new float4(0.08f, 0.1f, 0.13f, 0.95f) : new float4(0.05f, 0.05f, 0.06f, 0.9f));
            if (ImGui.BeginChild($"##perkcol{c}", new float2(colW, area.Y), ImGuiChildFlags.Borders))
            {
                ImGui.PushFont(default, 20);
                ImGuiSafe.TextColored(col4, Perks.CategoryName(cat).ToUpperInvariant());
                ImGui.PopFont();
                ImGuiSafe.TextColored(Dim, cat switch
                {
                    PerkCategory.Movement => "How you get around."u8,
                    PerkCategory.Survival => "Staying alive."u8,
                    _ => "Hitting harder."u8,
                });
                ImGui.Spacing();
                foreach (var def in Perks.In(cat).Prepend(Perks.Get(PerkId.None)))
                {
                    var unlocked = prog.IsUnlocked(def.Id);
                    var selected = l.Perks[cat] == def.Id;
                    var cardH = def.Id == PerkId.None ? 36 * _scale : 112 * _scale;
                    var pos = ImGui.GetCursorScreenPos();
                    var cw = ImGui.GetContentRegionAvail().X;
                    ImGui.PushStyleColor(ImGuiCol.ChildBg, selected ? SelectedBg : !unlocked ? LockedBg : _hoverPerk == def && _perkFocus == cat ? CardHover : CardBg);
                    if (ImGui.BeginChild($"##perk{c}_{(int)def.Id}", new float2(cw, cardH), ImGuiChildFlags.Borders, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
                    {
                        if (def.Id == PerkId.None)
                        {
                            ImGui.TextColored(selected ? col4 : Dim, "Empty slot"u8);
                        }
                        else
                        {
                            var dl = ImGui.GetWindowDrawList();
                            DrawPerkIcon(dl, def.Id, cat, pos + new float2(26 * _scale, 30 * _scale), 18 * _scale, !unlocked);
                            ImGui.SetCursorScreenPos(pos + new float2(52 * _scale, 8 * _scale));
                            ImGui.PushFont(default, 18);
                            ImGuiSafe.TextColored(unlocked ? new float4(1, 1, 1, 1) : Dim, def.Name);
                            ImGui.PopFont();
                            ImGui.SetCursorScreenPos(pos + new float2(52 * _scale, 30 * _scale));
                            ImGuiSafe.TextColored(unlocked ? col4 : Dim, def.When);
                            ImGui.SetCursorScreenPos(pos + new float2(8 * _scale, 54 * _scale));
                            ImGui.PushStyleColor(ImGuiCol.Text, unlocked ? new float4(0.8f, 0.8f, 0.84f, 1f) : Dim);
                            ImGuiSafe.TextWrapped(unlocked ? def.Effect : $"Unlocks at level {CallOfSlop.Progress.PerkLevel(def.Id)}. {def.Effect}");
                            ImGui.PopStyleColor();
                            if (selected)
                            {
                                var star = pos + new float2(cw - 16 * _scale, 16 * _scale);
                                DrawStar(dl, star, 7 * _scale, new ImColor8(240, 150, 40, 255));
                            }
                        }
                    }
                    ImGui.EndChild();
                    ImGui.PopStyleColor();
                    if (ImGui.IsItemHovered())
                    {
                        hovered = def;
                        _perkFocus = cat;
                    }
                    // A click picks it (a double-click's second click lands on the picked card and does nothing more).
                    if (ImGui.IsItemClicked(ImGuiMouseButton.Left) && !SecondClick)
                    {
                        if (!unlocked)
                            LockedFeedback(audio);
                        else if (!selected)
                        {
                            l.Perks = l.Perks.With(cat, def.Id);
                            LoadoutStore.Save();
                            audio.Effect(Sfx.HitTick, 0.4f);
                        }
                    }
                }
            }
            ImGui.EndChild();
            ImGui.PopStyleColor();
        }
        _hoverPerk = hovered;
    }

    // ---- Icons ---------------------------------------------------------------------------------

    private static ImColor8 Col(System.Numerics.Vector3 c, byte a = 255) => new((byte)(c.X * 255), (byte)(c.Y * 255), (byte)(c.Z * 255), a);

    private static void Line(ImDrawListPtr dl, float2 a, float2 b, ImColor8 col, float t) => ImDrawListExtensions.AddLine(dl, in a, in b, col, t);

    private static void Tri(ImDrawListPtr dl, float2 a, float2 b, float2 c, ImColor8 col) => ImDrawListExtensions.AddTriangleFilled(dl, in a, in b, in c, col);

    private static void DrawStar(ImDrawListPtr dl, float2 c, float r, ImColor8 col)
    {
        Span<float2> pts = stackalloc float2[10];
        for (var i = 0; i < 10; i++)
        {
            var a = -MathF.PI / 2 + i * MathF.PI / 5;
            var rr = i % 2 == 0 ? r : r * 0.45f;
            pts[i] = c + new float2(MathF.Cos(a) * rr, MathF.Sin(a) * rr);
        }
        ImDrawListExtensions.AddConcavePolyFilled(dl, pts, col);
    }

    /// <summary>A round badge with a simple glyph per perk, tinted by category.</summary>
    private void DrawPerkIcon(ImDrawListPtr dl, PerkId id, PerkCategory cat, float2 c, float r, bool locked = false)
    {
        var tint = Perks.Color(cat);
        var col = locked ? new ImColor8(110, 110, 115, 255) : Col(tint);
        var center = c;
        ImDrawListExtensions.AddCircleFilled(dl, in center, r, new ImColor8(18, 20, 26, 255), 28);
        ImDrawListExtensions.AddCircle(dl, in center, r, col, 28, 2f * _scale);
        var t = 2.4f * _scale;
        var u = r * 0.5f;
        float2 P(float x, float y) => c + new float2(x * u, y * u);
        switch (id)
        {
            case PerkId.None:
                Line(dl, P(-0.6f, 0), P(0.6f, 0), col, t);
                break;
            case PerkId.LowProfile: // low chevron over the ground
                Line(dl, P(-1f, -0.6f), P(0, 0.2f), col, t);
                Line(dl, P(0, 0.2f), P(1f, -0.6f), col, t);
                Line(dl, P(-1.1f, 0.8f), P(1.1f, 0.8f), col, t);
                break;
            case PerkId.HeadStart: // >>
                Line(dl, P(-1f, -0.8f), P(-0.1f, 0), col, t);
                Line(dl, P(-0.1f, 0), P(-1f, 0.8f), col, t);
                Line(dl, P(0.1f, -0.8f), P(1f, 0), col, t);
                Line(dl, P(1f, 0), P(0.1f, 0.8f), col, t);
                break;
            case PerkId.Parkour: // steps with an arrow up them
                Line(dl, P(-1.1f, 0.9f), P(-0.4f, 0.9f), col, t);
                Line(dl, P(-0.4f, 0.9f), P(-0.4f, 0.2f), col, t);
                Line(dl, P(-0.4f, 0.2f), P(0.3f, 0.2f), col, t);
                Line(dl, P(0.3f, 0.2f), P(0.3f, -0.5f), col, t);
                Line(dl, P(0.3f, -0.5f), P(1.1f, -0.5f), col, t);
                Line(dl, P(-0.9f, 0.3f), P(0.4f, -1f), col, t * 0.8f);
                Tri(dl, P(0.6f, -1.2f), P(0.05f, -1.05f), P(0.45f, -0.6f), col);
                break;
            case PerkId.Afterburner: // flame
                Tri(dl, P(0, -1.2f), P(-0.8f, 0.6f), P(0.8f, 0.6f), col);
                ImDrawListExtensions.AddCircleFilled(dl, in center, u * 0.75f, col, 16);
                Tri(dl, P(0, -0.3f), P(-0.35f, 0.6f), P(0.35f, 0.6f), new ImColor8(18, 20, 26, 255));
                break;
            case PerkId.FlakJacket: // shield
            {
                Span<float2> s = [P(-0.9f, -0.9f), P(0.9f, -0.9f), P(0.9f, 0.1f), P(0, 1.1f), P(-0.9f, 0.1f)];
                ImDrawListExtensions.AddConvexPolyFilled(dl, s, col);
                Line(dl, P(-0.5f, -0.3f), P(0.5f, -0.3f), new ImColor8(18, 20, 26, 255), t);
                break;
            }
            case PerkId.LastStand: // heart with a "1"
            {
                var l1 = P(-0.45f, -0.35f);
                var l2 = P(0.45f, -0.35f);
                ImDrawListExtensions.AddCircleFilled(dl, in l1, u * 0.55f, col, 16);
                ImDrawListExtensions.AddCircleFilled(dl, in l2, u * 0.55f, col, 16);
                Tri(dl, P(-1f, -0.15f), P(1f, -0.15f), P(0, 1.1f), col);
                Line(dl, P(0, -0.5f), P(0, 0.4f), new ImColor8(18, 20, 26, 255), t);
                break;
            }
            case PerkId.Bloodthirst: // drop
            {
                var d = P(0, 0.35f);
                ImDrawListExtensions.AddCircleFilled(dl, in d, u * 0.7f, col, 18);
                Tri(dl, P(0, -1.2f), P(-0.62f, 0.15f), P(0.62f, 0.15f), col);
                break;
            }
            case PerkId.Entrenched: // sandbags
            {
                foreach (var (x, y) in new[] { (-0.55f, 0.55f), (0.55f, 0.55f), (0f, -0.15f) })
                {
                    var a = P(x - 0.5f, y - 0.32f);
                    var b = P(x + 0.5f, y + 0.32f);
                    ImDrawListExtensions.AddRectFilled(dl, in a, in b, col, 3 * _scale, ImDrawFlags.None);
                }
                break;
            }
            case PerkId.PointBlank: // big dot in a tight ring
            {
                ImDrawListExtensions.AddCircle(dl, in center, u * 0.9f, col, 20, t);
                ImDrawListExtensions.AddCircleFilled(dl, in center, u * 0.45f, col, 16);
                break;
            }
            case PerkId.Deadeye: // scope crosshair
            {
                ImDrawListExtensions.AddCircle(dl, in center, u * 1.1f, col, 24, t * 0.8f);
                Line(dl, P(-1.4f, 0), P(-0.25f, 0), col, t * 0.8f);
                Line(dl, P(0.25f, 0), P(1.4f, 0), col, t * 0.8f);
                Line(dl, P(0, -1.4f), P(0, -0.25f), col, t * 0.8f);
                Line(dl, P(0, 0.25f), P(0, 1.4f), col, t * 0.8f);
                break;
            }
            case PerkId.Flanker: // arrow curving round behind
            {
                Span<float2> arc = stackalloc float2[9];
                for (var i = 0; i < arc.Length; i++)
                {
                    var a = MathF.PI * (0.9f + 1.1f * i / (arc.Length - 1));
                    arc[i] = c + new float2(MathF.Cos(a), MathF.Sin(a)) * (u * 0.95f);
                }
                ImDrawListExtensions.AddPolyline(dl, arc, col, ImDrawFlags.None, t);
                var end = arc[^1];
                Tri(dl, end + new float2(0.55f * u, 0.1f * u), end + new float2(-0.2f * u, -0.45f * u), end + new float2(-0.15f * u, 0.5f * u), col);
                break;
            }
            case PerkId.Opener: // burst
                for (var i = 0; i < 8; i++)
                {
                    var a = i * MathF.PI / 4;
                    var dir = new float2(MathF.Cos(a), MathF.Sin(a));
                    Line(dl, c + dir * (u * 0.35f), c + dir * (u * (i % 2 == 0 ? 1.25f : 0.85f)), col, t);
                }
                break;
        }
    }

    // ---- HUD ------------------------------------------------------------------------------------

    /// <summary>Is the perk's situation happening right now? (Lights it up on the HUD.)</summary>
    public static bool PerkActive(Player p, PerkId id) => id switch
    {
        PerkId.LowProfile => p.Sim.Stance != Stance.Stand,
        PerkId.HeadStart or PerkId.Afterburner => p.Sim.BoostTicks > 0,
        PerkId.Parkour => p.Sim.MantleTicks > 0,
        PerkId.Entrenched => Game.IsDugIn(p),
        PerkId.LastStand => p.ShieldCharges > 0 || p.Health == 1,
        _ => false,
    };
}
