using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>A gun picture on a loadout card (ImGui pixel coordinates). Program draws these over the UI.</summary>
public readonly record struct GunThumb(WeaponDef Def, float2 Min, float2 Max);

/// <summary>
/// The loadout screen, used from the main menu and in matches. Left: your loadout slots (more unlock
/// as you level up). Right: the selected loadout as cards (primary, secondary, perks, lethal, tactical);
/// click a card to change that part. A second tab holds your progression.
/// </summary>
public sealed partial class Ui
{
    private enum LoadoutView
    {
        Overview,
        Primary,
        Secondary,
        Lethal,
        Tactical,
        Perks,
    }

    private LoadoutView _view;
    private int _gunTab; // Primary/Secondary view: 0 = choose the gun, 1 = its attachments
    private int _screenTab; // 0 = loadouts, 1 = progression
    private int _editSlot;
    private PerkCategory _perkFocus;
    private PerkDef? _hoverPerk;
    private ImInputString? _renameInput;
    private bool _renaming;
    private int _hotCard = -1; // card under the mouse last frame (highlight)
    private bool _loadoutDrawn; // was the loadout screen on screen this frame?
    private int _forceView = -1, _forceGunTab = -1; // smoke test

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

    private void SetKitOf(WeaponDef w, uint kit)
    {
        if (w.Id == Edit.Primary)
            LoadoutStore.SetKit(Edit, 0, kit);
        else if (w.Id == Edit.Secondary)
            LoadoutStore.SetKit(Edit, 1, kit);
        LoadoutStore.Save();
    }

    private void ResetLoadoutScreen()
    {
        _view = LoadoutView.Overview;
        _screenTab = 0;
        _gunTab = 0;
        _renaming = false;
        _editSlot = Math.Clamp(LoadoutStore.Selected, 0, Math.Max(0, Progress.LoadoutSlots - 1));
        _hover = null;
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
    /// <summary>Smoke test: 4/5 = primary/secondary attachments, 6 = perks, 0 = back to the overview.</summary>
    public void SmokeTab(int tab)
    {
        (_forceView, _forceGunTab) = tab switch
        {
            4 => ((int)LoadoutView.Primary, 1),
            5 => ((int)LoadoutView.Secondary, 1),
            6 => ((int)LoadoutView.Perks, -1),
            _ => (-1, -1),
        };
        if (tab == 0)
            _view = LoadoutView.Overview;
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
        if (_forceView >= 0)
        {
            _view = (LoadoutView)_forceView;
            if (_forceGunTab >= 0)
                _gunTab = _forceGunTab;
        }

        var deployed = false;
        var prog = Progress;
        if (_editSlot >= prog.LoadoutSlots)
            _editSlot = 0;
        var vp = ImGui.GetMainViewport();
        var center = new float2(vp.WorkPos.X + vp.WorkSize.X / 2, vp.WorkPos.Y + vp.WorkSize.Y / 2);
        ImGui.SetNextWindowPos(center, ImGuiCond.Always, new float2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new float2(MathF.Min(1150 * _scale, vp.WorkSize.X - 40), MathF.Min(760 * _scale, vp.WorkSize.Y - 40)));

        // In the attachment showcase the window background is painted by Program (behind the 3D gun) instead.
        var winFlags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings
                       | (_showcaseLastFrame ? ImGuiWindowFlags.NoBackground : ImGuiWindowFlags.None);
        _showcaseLastFrame = false;
        if (ImGui.Begin("LOADOUTS"u8, winFlags))
        {
            var winPos = ImGui.GetWindowPos();
            var winSize = ImGui.GetWindowSize();
            if (ImGui.BeginTabBar("##screen"u8))
            {
                if (ImGui.BeginTabItem("Edit loadouts"u8))
                {
                    _screenTab = 0;
                    ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem($"Progression - level {prog.Level}###prog"))
                {
                    _screenTab = 1;
                    ImGui.EndTabItem();
                }
                ImGui.EndTabBar();
            }

            var footer = 52 * _scale;
            _bodyTopY = ImGui.GetCursorScreenPos().Y;
            var avail = ImGui.GetContentRegionAvail();
            var body = new float2(avail.X, avail.Y - footer);
            if (_screenTab == 1)
            {
                if (ImGui.BeginChild("##progression"u8, body, ImGuiChildFlags.None))
                {
                    LevelSummary();
                    ImGui.TextColored(Dim, $"Kills {prog.Kills}   Headshots {prog.Headshots}   Matches {prog.Matches}   Wins {prog.Wins}   Loadout slots {prog.LoadoutSlots}/{CallOfSlop.Progress.MaxLoadoutSlots}");
                    ImGui.Spacing();
                    ProgressionTable();
                }
                ImGui.EndChild();
            }
            else if (_view == LoadoutView.Overview)
            {
                DrawOverview(game, body);
            }
            else
            {
                DrawEditView(audio, body, winPos, winSize);
            }

            // ---- Footer ----------------------------------------------------------------
            var name = Edit.Name;
            if (game != null)
            {
                var dead = game.Local is not { Alive: true };
                if (dead)
                {
                    var wait = game.RespawnWait(now);
                    if (ImGui.Button(wait > 0 ? $"RESPAWN ({wait:0.0})###respawn" : "RESPAWN###respawn", new float2(220 * _scale, 38 * _scale)))
                    {
                        ApplySelection(game, now);
                        game.RequestRespawn();
                        MenuOpen = false;
                        deployed = true;
                    }
                }
                else if (game.CanChangeLoadout)
                {
                    if (ImGui.Button("DEPLOY"u8, new float2(220 * _scale, 38 * _scale)))
                    {
                        ApplySelection(game, now);
                        MenuOpen = false;
                        deployed = true;
                    }
                }
                ImGui.SameLine();
                if (ImGui.Button("Close"u8, new float2(140 * _scale, 38 * _scale)))
                {
                    LoadoutStore.Save();
                    MenuOpen = false;
                }
                ImGui.SameLine();
                ImGui.TextColored(Dim, dead
                    ? $"Respawn with \"{name}\". Loadouts only change between lives."
                    : game.CanChangeLoadout
                        ? $"Pre-match: \"{name}\" applies right away."
                        : "Edit freely - the selected loadout is used the next time you spawn.");
            }
            else
            {
                if (ImGui.Button("BACK"u8, new float2(220 * _scale, 38 * _scale)))
                {
                    LoadoutStore.Save();
                    LobbyLoadouts = false;
                }
                ImGui.SameLine();
                ImGui.TextColored(Dim, $"Saved automatically. You'll spawn with \"{name}\" (change it here, or in a match while you're dead).");
            }
        }
        ImGui.End();
        return deployed;
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
                    ImGui.TextColored(!unlocked ? Dim : selected ? new float4(0.55f, 0.85f, 1f, 1f) : new float4(1, 1, 1, 1), unlocked ? l.Name : DefaultLockedName(i));
                    ImGui.PopFont();
                    var eff = LoadoutStore.Effective(l, prog);
                    ImGui.TextColored(Dim, unlocked
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
            ImGui.TextColored(new float4(0.55f, 0.85f, 1f, 1f), l.Name);
            ImGui.PopFont();
            ImGui.SameLine(0, 16 * _scale);
            if (!_renaming)
            {
                if (ImGui.Button("Rename"u8))
                {
                    _renameInput ??= new ImInputString(25);
                    _renameInput.SetValue(l.Name.AsSpan());
                    _renaming = true;
                }
            }
            else
            {
                ImGui.SetNextItemWidth(240 * _scale);
                var enter = ImGui.InputText("##rename"u8, _renameInput!, ImGuiInputTextFlags.EnterReturnsTrue);
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
            if (GunCard(0, eff, w, gunH, ref hot))
                OpenView(LoadoutView.Primary);
            if (GunCard(1, eff, w, gunH, ref hot))
                OpenView(LoadoutView.Secondary);
            if (PerksCard(eff.Perks, w, 108 * _scale, ref hot) is { } cat)
            {
                _perkFocus = cat;
                OpenView(LoadoutView.Perks);
            }
            var gap = ImGui.GetStyle().ItemSpacing.X;
            var half = (w - gap) / 2;
            if (ThrowableCard(3, "Lethal", eff.Grenade, half, 70 * _scale, ref hot))
                OpenView(LoadoutView.Lethal);
            ImGui.SameLine();
            if (ThrowableCard(4, "Tactical", eff.Equipment, half, 70 * _scale, ref hot))
                OpenView(LoadoutView.Tactical);

            if (!SameAsStored(l, eff))
                ImGui.TextColored(Orange, "Some items in this loadout are locked at your level, so defaults stand in for them."u8);
        }
        ImGui.EndChild();
        _hotCard = hot;
    }

    private static string DefaultLockedName(int slot) => $"Loadout slot {slot + 1}";

    private static bool SameAsStored(Loadout l, Loadout eff) =>
        l.Primary == eff.Primary && l.Secondary == eff.Secondary && l.Grenade == eff.Grenade && l.Equipment == eff.Equipment && l.Perks == eff.Perks;

    private void OpenView(LoadoutView v)
    {
        _view = v;
        _gunTab = 0;
        _hover = null;
        _hoverThrowable = null;
        _hoverAttachment = null;
        _hoverPerk = null;
    }

    /// <summary>A weapon card: name, class, weapon XP and attachment dots, with the gun drawn on the right.</summary>
    private bool GunCard(int slot, Loadout eff, float width, float height, ref int hot)
    {
        var def = Weapons.Get(eff.WeaponIn(slot));
        var kit = eff.KitFor(slot);
        var pos = ImGui.GetCursorScreenPos();
        ImGui.PushStyleColor(ImGuiCol.ChildBg, _hotCard == slot ? CardHover : CardBg);
        if (ImGui.BeginChild($"##gun{slot}", new float2(width, height), ImGuiChildFlags.Borders, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            ImGui.TextColored(Orange, slot == 0 ? "Primary Weapon"u8 : "Secondary Weapon"u8);
            ImGui.PushFont(default, 22);
            ImGui.Text(def.Name);
            ImGui.PopFont();
            ImGui.TextColored(Dim, $"{def.ClassName} - {def.ModeName}");
            var fitted = Attachments.InKit(kit).ToArray();
            ImGui.PushTextWrapPos(width * 0.5f); // keep clear of the gun picture on the right
            ImGui.PushStyleColor(ImGuiCol.Text, SlotBlue);
            ImGui.TextWrapped(fitted.Length == 0 ? "No attachments" : string.Join(", ", fitted.Select(a => a.Name)));
            ImGui.PopStyleColor();
            ImGui.PopTextWrapPos();

            var options = Attachments.For(def);
            var wxp = Progress.WeaponXp(def.Id);
            var max = Attachments.MaxXp(def);
            ImGui.SetCursorScreenPos(pos + new float2(10 * _scale, height - 30 * _scale));
            ImGui.TextColored(Dim, options.Count == 0
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
        if (ImGui.IsItemHovered())
            hot = slot;
        var clicked = ImGui.IsItemClicked(ImGuiMouseButton.Left);
        Thumbs.Add(new GunThumb(Attachments.Apply(def, kit), pos + new float2(width * 0.52f, 22 * _scale), pos + new float2(width - 10 * _scale, height - 6 * _scale)));
        return clicked;
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
                ImGui.TextColored(new float4(col.X, col.Y, col.Z, 1f), Perks.CategoryName(cat).ToUpperInvariant());
                ImGui.SetCursorScreenPos(new float2(x0 + 52 * _scale, pos.Y + 54 * _scale));
                ImGui.Text(def.Id == PerkId.None ? "Empty" : def.Name);
                ImGui.SetCursorScreenPos(new float2(x0 + 52 * _scale, pos.Y + 72 * _scale));
                ImGui.TextColored(Dim, def.Id == PerkId.None ? "-" : def.When);
            }
        }
        ImGui.EndChild();
        ImGui.PopStyleColor();
        if (ImGui.IsItemHovered())
            hot = 2;
        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
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
            ImGui.TextColored(Orange, title);
            ImGui.Text(def.Kind == ThrowableKind.None ? "Empty" : def.Name);
            ImGui.TextColored(Dim, def.Kind == ThrowableKind.None ? "-" : $"{def.PerLife} per life");
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
        return ImGui.IsItemClicked(ImGuiMouseButton.Left);
    }

    // ---- Edit views ----------------------------------------------------------------------------

    private void DrawEditView(Audio audio, float2 size, float2 winPos, float2 winSize)
    {
        var prog = Progress;
        var l = Edit;
        if (ImGui.Button("< Back"u8, new float2(100 * _scale, 0)))
        {
            _view = LoadoutView.Overview;
            _forceView = -1;
            return;
        }
        ImGui.SameLine();
        var part = _view switch
        {
            LoadoutView.Primary => "Primary weapon",
            LoadoutView.Secondary => "Secondary weapon",
            LoadoutView.Lethal => "Lethal",
            LoadoutView.Tactical => "Tactical",
            _ => "Perks",
        };
        ImGui.TextColored(Dim, $"{l.Name}  /  {part}");

        var gunSlot = _view == LoadoutView.Secondary ? 1 : 0;
        var gun = Weapons.Get(LoadoutStore.Effective(l, prog).WeaponIn(gunSlot));
        if (_view is LoadoutView.Primary or LoadoutView.Secondary)
        {
            var options = Attachments.For(gun);
            var wxp = prog.WeaponXp(gun.Id);
            var open = options.Count(a => wxp >= Attachments.UnlockXp(gun, a));
            if (ImGui.BeginTabBar(gunSlot == 0 ? "##sub0"u8 : "##sub1"u8))
            {
                if (ImGui.BeginTabItem(gunSlot == 0 ? "Choose primary###w"u8 : "Choose secondary###w"u8))
                {
                    if (_forceGunTab < 0)
                        _gunTab = 0;
                    ImGui.EndTabItem();
                }
                var attLabel = options.Count == 0
                    ? $"{gun.Name}: no attachments###a"
                    : $"{gun.Name} attachments ({open}/{options.Count})  -  {Attachments.Describe(KitOf(gun))}###a";
                if (ImGui.BeginTabItem(attLabel))
                {
                    _gunTab = 1;
                    ImGui.EndTabItem();
                }
                ImGui.EndTabBar();
            }
            if (_forceGunTab >= 0)
                _gunTab = _forceGunTab;
        }

        // What's left of the body area under the header and tabs.
        var area = new float2(ImGui.GetContentRegionAvail().X, MathF.Max(100 * _scale, size.Y - (ImGui.GetCursorScreenPos().Y - _bodyTopY)));

        if (_view is LoadoutView.Primary or LoadoutView.Secondary && _gunTab == 1)
        {
            _showcaseLastFrame = true;
            DrawShowcase(audio, gun, area, winPos, winSize);
            return;
        }
        if (_view == LoadoutView.Perks)
        {
            DrawPerkPicker(audio, area);
            return;
        }

        var listWidth = area.X * 0.42f;
        WeaponDef? hovered = null;
        ThrowableDef? hoveredThrowable = null;
        if (ImGui.BeginChild("##list"u8, new float2(listWidth, area.Y), ImGuiChildFlags.Borders))
        {
            if (_view is LoadoutView.Primary or LoadoutView.Secondary)
            {
                var slot = gunSlot == 0 ? Slot.Primary : Slot.Secondary;
                foreach (var group in Weapons.All.Where(w => w.Slot == slot).GroupBy(w => w.Class))
                {
                    ImGui.SeparatorText(group.First().ClassName);
                    foreach (var w in group.OrderBy(x => CallOfSlop.Progress.UnlockLevel(x)))
                    {
                        var unlocked = prog.IsUnlocked(w);
                        var selected = w.Id == gun.Id;
                        var label = unlocked ? $"{w.Name}##{w.Id}" : $"[LOCKED] {w.Name}  -  level {CallOfSlop.Progress.UnlockLevel(w)}##{w.Id}";
                        if (!unlocked)
                            ImGui.PushStyleColor(ImGuiCol.Text, Dim);
                        if (ImGui.Selectable(label, selected) && unlocked)
                        {
                            LoadoutStore.SetWeapon(l, gunSlot, w.Id, prog);
                            LoadoutStore.Save();
                            audio.Equip(w);
                        }
                        if (!unlocked)
                            ImGui.PopStyleColor();
                        if (ImGui.IsItemHovered())
                            hovered = w;
                    }
                }
            }
            else
            {
                var equipmentTab = _view == LoadoutView.Tactical;
                foreach (var t in Throwables.All.Where(t => t.Kind == ThrowableKind.None || t.IsEquipment == equipmentTab))
                {
                    var unlocked = prog.IsUnlocked(t.Kind);
                    var selected = equipmentTab ? l.Equipment == t.Kind : l.Grenade == t.Kind;
                    var label = unlocked ? $"{t.Name}##t{(int)t.Kind}" : $"[LOCKED] {t.Name}  -  level {CallOfSlop.Progress.UnlockLevel(t.Kind)}##t{(int)t.Kind}";
                    if (!unlocked)
                        ImGui.PushStyleColor(ImGuiCol.Text, Dim);
                    if (ImGui.Selectable(label, selected) && unlocked)
                    {
                        if (equipmentTab)
                            l.Equipment = t.Kind;
                        else
                            l.Grenade = t.Kind;
                        LoadoutStore.Save();
                    }
                    if (!unlocked)
                        ImGui.PopStyleColor();
                    if (ImGui.IsItemHovered())
                        hoveredThrowable = t;
                }
            }
        }
        ImGui.EndChild();

        ImGui.SameLine();
        if (ImGui.BeginChild("##stats"u8, new float2(0, area.Y), ImGuiChildFlags.Borders))
        {
            if (_view is LoadoutView.Primary or LoadoutView.Secondary)
            {
                if (hovered != null)
                    _hover = hovered;
                var shown = _hover is { } h && h.Slot == gun.Slot ? h : gun;
                if (!prog.IsUnlocked(shown))
                    ImGui.TextColored(Red, $"Locked - reach level {CallOfSlop.Progress.UnlockLevel(shown)} to use this.");
                DrawStats(audio, Attachments.Apply(shown, KitOf(shown)));
            }
            else
            {
                if (hoveredThrowable != null)
                    _hoverThrowable = hoveredThrowable;
                var equipmentTab = _view == LoadoutView.Tactical;
                var t = _hoverThrowable is { } h && (h.Kind == ThrowableKind.None || h.IsEquipment == equipmentTab)
                    ? h
                    : Throwables.Get(equipmentTab ? l.Equipment : l.Grenade);
                DrawThrowable(t);
            }
        }
        ImGui.EndChild();
    }

    private float _bodyTopY; // screen Y where the body area starts this frame

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
                ImGui.TextColored(col4, Perks.CategoryName(cat).ToUpperInvariant());
                ImGui.PopFont();
                ImGui.TextColored(Dim, cat switch
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
                            ImGui.TextColored(unlocked ? new float4(1, 1, 1, 1) : Dim, def.Name);
                            ImGui.PopFont();
                            ImGui.SetCursorScreenPos(pos + new float2(52 * _scale, 30 * _scale));
                            ImGui.TextColored(unlocked ? col4 : Dim, def.When);
                            ImGui.SetCursorScreenPos(pos + new float2(8 * _scale, 54 * _scale));
                            ImGui.PushStyleColor(ImGuiCol.Text, unlocked ? new float4(0.8f, 0.8f, 0.84f, 1f) : Dim);
                            ImGui.TextWrapped(unlocked ? def.Effect : $"Unlocks at level {CallOfSlop.Progress.PerkLevel(def.Id)}. {def.Effect}");
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
                    if (ImGui.IsItemClicked(ImGuiMouseButton.Left) && unlocked && !selected)
                    {
                        l.Perks = l.Perks.With(cat, def.Id);
                        LoadoutStore.Save();
                        audio.Effect(Sfx.HitTick, 0.4f);
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
        PerkId.LastStand => p.Health == 1,
        _ => false,
    };
}
