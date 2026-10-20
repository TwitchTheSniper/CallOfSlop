using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

public enum LobbyAction
{
    None,
    SinglePlayer,
    Host,
    Join,
    Quit,
}

public enum PauseAction
{
    None,
    Resume,
    Loadout,
    Leave,
    Quit,
}

/// <summary>The lobby (main menu), the in-game pause menu and the shared settings panel.</summary>
public sealed partial class Ui
{
    private enum Page
    {
        Main,
        SinglePlayer,
        Multiplayer,
        Settings,
    }

    private Page _page = Page.Main;
    public bool PauseOpen;
    private bool _pauseSettings;
    private ImInputString? _nameInput;
    private bool _confirmReset;
    private ImInputString? _addressInput;
    private int _portInput;

    private const ImGuiWindowFlags Panel =
        ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.AlwaysAutoResize;

    private void EnsureInputs(Settings s)
    {
        if (_nameInput != null)
            return;
        _nameInput = new ImInputString(32);
        _nameInput.SetValue(s.Name.AsSpan());
        _addressInput = new ImInputString(128);
        _addressInput.SetValue(s.JoinAddress.AsSpan());
        _portInput = s.Port;
        _serverNameInput = new ImInputString(33);
        _serverNameInput.SetValue(s.ServerName.AsSpan());
        _masterInput = new ImInputString(128);
        _masterInput.SetValue(s.MasterServer.AsSpan());
    }

    private ImInputString? _serverNameInput, _masterInput;
    private ServerBrowser? _browser;
    private int _mpTab; // 0 browser, 1 host, 2 direct

    private bool BigButton(string label, float width = 360)
    {
        ImGui.PushFont(default, 20);
        var clicked = ImGui.Button(label, new float2(width * _scale, 52 * _scale));
        ImGui.PopFont();
        return clicked;
    }

    private void Title(string subtitle)
    {
        ImGui.PushFont(default, 44);
        ImGui.TextColored(Orange, "CALL OF SLOP"u8);
        ImGui.PopFont();
        ImGui.TextColored(Dim, subtitle);
        ImGui.Spacing();
        ImGui.Spacing();
    }

    private static void CenterNext()
    {
        var vp = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(new float2(vp.WorkPos.X + vp.WorkSize.X / 2, vp.WorkPos.Y + vp.WorkSize.Y / 2), ImGuiCond.Always, new float2(0.5f, 0.5f));
    }

    private void Footer()
    {
        var vp = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(new float2(vp.WorkPos.X + vp.WorkSize.X / 2, vp.WorkPos.Y + vp.WorkSize.Y - 16 * _scale), ImGuiCond.Always, new float2(0.5f, 1f));
        if (ImGui.Begin("##credits"u8, Overlay))
            ImGui.TextColored(Dim, "Built with the BRUTAL framework - Copyright (c) 2024 Ahwoo Limited - www.ahwoo.com/brutal"u8);
        ImGui.End();
    }

    private string? _nowPlayingName, _nowPlayingText;

    /// <summary>A subtle "Now playing" line in the bottom-left corner of the main menu.</summary>
    private void NowPlaying(Audio audio)
    {
        if (audio.Music.NowPlaying is not { } name || audio.Music.Volume <= 0f)
            return;
        if (!ReferenceEquals(name, _nowPlayingName))
        {
            _nowPlayingName = name;
            _nowPlayingText = $"Now playing: {name}";
        }
        var vp = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(new float2(vp.WorkPos.X + 16 * _scale, vp.WorkPos.Y + vp.WorkSize.Y - 44 * _scale), ImGuiCond.Always, new float2(0f, 1f)); // just above the credits line
        if (ImGui.Begin("##nowplaying"u8, Overlay))
            ImGui.TextColored(Dim, _nowPlayingText!);
        ImGui.End();
    }

    // =====================================================================
    // Lobby
    // =====================================================================

    /// <summary>Draws the lobby. Returns what the player chose this frame.</summary>
    public LobbyAction DrawLobby(Settings s, string message, Audio audio)
    {
        EnsureInputs(s);
        var action = LobbyAction.None;
        if (_page != Page.Multiplayer && _browser != null)
        {
            _browser.Dispose(); // close the discovery socket when the browser isn't on screen
            _browser = null;
        }
        Footer();
        if (_page == Page.Main)
            NowPlaying(audio);

        CenterNext();
        ImGui.SetNextWindowBgAlpha(0.88f);
        if (ImGui.Begin("##lobby"u8, Panel))
        {
            switch (_page)
            {
                case Page.Main:
                    Title($"{Weapons.All.Length} weapons. One prototype box. No mercy.");
                    if (message.Length > 0)
                    {
                        ImGui.TextColored(Red, message);
                        ImGui.Spacing();
                    }
                    if (WeaponSet.ErrorSummary is { } weaponErrors)
                    {
                        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + 560 * _scale); // long file paths: wrap instead of widening the panel
                        ImGui.TextColored(Red, weaponErrors);
                        ImGui.PopTextWrapPos();
                        ImGui.Spacing();
                    }
                    LevelSummary();
                    if (BigButton("SINGLE PLAYER"))
                        _page = Page.SinglePlayer;
                    if (BigButton("MULTIPLAYER"))
                        _page = Page.Multiplayer;
                    if (BigButton("LOADOUTS"))
                        OpenLobbyLoadouts();
                    if (BigButton("SETTINGS"))
                        _page = Page.Settings;
                    ImGui.Spacing();
                    if (BigButton("QUIT"))
                        action = LobbyAction.Quit;
                    break;

                case Page.SinglePlayer:
                    Title("Single player - you against bots");
                    ModePicker(s);
                    MapPicker(s);
                    ImGui.SetNextItemWidth(260 * _scale);
                    ImGui.SliderInt("Bots"u8, ref s.Bots, 1, Game.MaxPlayers - 1);
                    DifficultyPicker(ref s.Difficulty);
                    ImGui.TextColored(Dim, s.Difficulty switch
                    {
                        BotDifficulty.Easy => "Slow to react, wild aim. Good for trying weapons.",
                        BotDifficulty.Normal => "A fair fight.",
                        _ => "Fast reactions, tight aim, they strafe and go for heads.",
                    });
                    ImGui.Spacing();
                    if (BigButton("START"))
                    {
                        s.Save();
                        action = LobbyAction.SinglePlayer;
                    }
                    if (BigButton("BACK"))
                        _page = Page.Main;
                    break;

                case Page.Multiplayer:
                    Title("Multiplayer - up to 8 players over LAN or the internet");
                    ImGui.TextColored(Dim, "The host picks the game mode. Each match starts once everyone has readied up in the warmup."u8);
                    if (ImGui.BeginTabBar("##mp"u8))
                    {
                        if (ImGui.BeginTabItem("Server browser"u8))
                        {
                            _mpTab = 0;
                            ImGui.EndTabItem();
                        }
                        if (ImGui.BeginTabItem("Host a game"u8))
                        {
                            _mpTab = 1;
                            ImGui.EndTabItem();
                        }
                        if (ImGui.BeginTabItem("Join by address"u8))
                        {
                            _mpTab = 2;
                            ImGui.EndTabItem();
                        }
                        ImGui.EndTabBar();
                    }
                    if (_mpTab != 0 && _browser != null)
                    {
                        _browser.Dispose();
                        _browser = null;
                    }
                    if (_mpTab == 0 && DrawServerBrowser(s, ref action))
                        break;
                    if (_mpTab == 0)
                    {
                        if (BigButton("BACK"))
                            _page = Page.Main;
                        break;
                    }
                    if (_mpTab == 2)
                    {
                        DrawDirectJoin(s, ref action);
                        if (BigButton("BACK"))
                            _page = Page.Main;
                        break;
                    }
                    ModePicker(s);
                    MapPicker(s);
                    AutoStartPicker(s);
                    ImGui.SetNextItemWidth(200 * _scale);
                    if (ImGui.InputInt("Port"u8, ref _portInput, 1, 100))
                        _portInput = Math.Clamp(_portInput, 1024, 65535);
                    ImGui.Checkbox("Open the port on my router automatically (UPnP)"u8, ref s.UseUpnp);
                    ImGui.SetNextItemWidth(260 * _scale);
                    ImGui.SliderInt("Fill with bots"u8, ref s.HostBots, 0, Game.MaxPlayers - 2);
                    if (s.HostBots > 0)
                        DifficultyPicker(ref s.Difficulty);
                    ImGui.SetNextItemWidth(360 * _scale);
                    if (ImGui.InputTextWithHint("Server name"u8, s.ServerNameOrDefault, _serverNameInput!))
                        s.ServerName = Settings.CleanServerName(_serverNameInput!.ToString());
                    ImGui.Checkbox("Advertise server (list it publicly in the server browser)"u8, ref s.Advertise);
                    if (s.Advertise && s.Master == null)
                        ImGui.TextColored(Orange, "No master server set (Settings > Player), so only people on your network will see it."u8);
                    else
                        ImGui.TextColored(Dim, s.Advertise
                            ? "Listed publicly through your master server; people on your network always see it too."
                            : "Not listed publicly. People on your network still see it in their browser.");
                    if (BigButton("HOST GAME"))
                    {
                        s.Port = (ushort)_portInput;
                        s.Save();
                        action = LobbyAction.Host;
                    }
                    ImGui.Spacing();
                    if (BigButton("BACK"))
                        _page = Page.Main;
                    break;

                case Page.Settings:
                    Title("Settings");
                    SettingsPanel(s, audio);
                    ImGui.Spacing();
                    if (BigButton("SAVE & BACK"))
                    {
                        s.Save();
                        _page = Page.Main;
                    }
                    break;
            }
        }
        ImGui.End();
        if (action != LobbyAction.None && _browser != null)
        {
            _browser.Dispose();
            _browser = null;
        }
        return action;
    }

    /// <summary>The server browser tab. Returns true if it already handled the rest of the page.</summary>
    private bool DrawServerBrowser(Settings s, ref LobbyAction action)
    {
        _browser ??= new ServerBrowser();
        _browser.Tick(s);
        var list = _browser.Servers.ToList();

        if (ImGui.Button("Refresh"u8))
            _browser.Refresh(s);
        ImGui.SameLine();
        ImGui.TextColored(Dim, _browser.Searching ? "Searching your network..." : $"{list.Count} game(s) found");
        ImGui.TextColored(Dim, s.Master is { } m
            ? $"Public list: {(_browser.MasterStatus.Length > 0 ? _browser.MasterStatus : $"master server {m.Host}:{m.Port}")}"
            : "Showing games on your network. To see public internet games, set a master server in Settings > Player.");

        if (ImGui.BeginTable("##servers"u8, 5, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.ScrollY, new float2(820 * _scale, 300 * _scale)))
        {
            ImGui.TableSetupColumn("Server"u8, ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Map"u8, ImGuiTableColumnFlags.WidthFixed, 150 * _scale);
            ImGui.TableSetupColumn("Players"u8, ImGuiTableColumnFlags.WidthFixed, 90 * _scale);
            ImGui.TableSetupColumn("Ping"u8, ImGuiTableColumnFlags.WidthFixed, 70 * _scale);
            ImGui.TableSetupColumn(""u8, ImGuiTableColumnFlags.WidthFixed, 80 * _scale);
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableHeadersRow();
            foreach (var e in list)
            {
                var i = e.Info;
                var humans = i.Players - i.Bots;
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text(i.Name.Length > 0 ? i.Name : "(unnamed)");
                ImGui.SameLine();
                ImGui.TextColored(Dim, e.FromMaster ? "  public" : "  LAN");
                ImGui.TableNextColumn();
                ImGui.TextColored(Maps.Find(i.MapName) != null ? new float4(1, 1, 1, 1) : Orange, i.MapName); // orange: you don't have this map
                ImGui.SameLine();
                ImGui.TextColored(Dim, GameModes.Short(i.Mode));
                ImGui.TableNextColumn();
                ImGui.TextColored(i.Players >= i.MaxPlayers && i.Bots == 0 ? Red : new float4(1, 1, 1, 1), i.Bots > 0 ? $"{humans}/{i.MaxPlayers} +{i.Bots} bots" : $"{humans}/{i.MaxPlayers}");
                ImGui.TableNextColumn();
                ImGui.TextColored(e.PingMs < 80 ? Green : e.PingMs < 160 ? Orange : Red, $"{e.PingMs} ms");
                ImGui.TableNextColumn();
                if (ImGui.Button($"Join##{e.Address}:{i.Port}"))
                {
                    s.JoinAddress = e.Address;
                    s.Port = i.Port;
                    s.Save();
                    action = LobbyAction.Join;
                }
            }
            ImGui.EndTable();
        }
        if (list.Count == 0)
            ImGui.TextColored(Dim, "No games yet. Games hosted on your network show up here automatically; ask a friend for their address to use Join by address."u8);
        ImGui.Spacing();
        return false;
    }

    private void DrawDirectJoin(Settings s, ref LobbyAction action)
    {
        ImGui.SetNextItemWidth(360 * _scale);
        ImGui.InputTextWithHint("Address"u8, "host IP, e.g. 192.168.1.20 or 203.0.113.45"u8, _addressInput!);
        ImGui.SetNextItemWidth(200 * _scale);
        if (ImGui.InputInt("Port"u8, ref _portInput, 1, 100))
            _portInput = Math.Clamp(_portInput, 1024, 65535);
        ImGui.TextColored(Dim, "Ask the host for the address shown at the top-left of their screen."u8);
        if (BigButton("JOIN GAME"))
        {
            var addr = _addressInput!.ToString().Trim();
            s.JoinAddress = addr.Length > 0 ? addr : "127.0.0.1";
            s.Port = (ushort)_portInput;
            s.Save();
            action = LobbyAction.Join;
        }
        ImGui.Spacing();
    }

    /// <summary>
    /// Map pack list, then "random from pack" or a map from that pack. The lobby background previews the selection
    /// (it tours the pack when random).
    /// </summary>
    private void MapPicker(Settings s)
    {
        ImGui.Text("Map pack"u8);
        ImGui.SameLine(0, 20 * _scale);
        if (ImGui.Button("Refresh maps"u8))
            _mapReloadMsg = (Maps.Reload().Summary, (Environment.TickCount64 / 1000.0) + 8);
        if (_mapReloadMsg is { } rm && (Environment.TickCount64 / 1000.0) < rm.Until)
            ImGui.TextColored(Dim, rm.Text);
        if (Maps.LoadErrors.Count > 0)
            ImGui.TextColored(Red,
                $"{Maps.LoadErrors.Count} map file(s) in the Maps folder didn't load: {Maps.LoadErrors[0]}{(Maps.LoadErrors.Count > 1 ? " (more in callofslop.log)" : "")}");

        // The saved pack may be gone (folder removed or renamed): show the first one, but only change the
        // setting when the player clicks, so the pack comes back if its folder does.
        var saved = Maps.FindPack(s.MapPack);
        var pack = saved ?? Maps.Packs[0];
        var rows = Math.Min(Maps.Packs.Length, 4);
        if (ImGui.BeginTable("##packs"u8, 2, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.ScrollY,
                new float2(720 * _scale, rows * 46 * _scale + 6 * _scale)))
        {
            ImGui.TableSetupColumn("Pack"u8, ImGuiTableColumnFlags.WidthFixed, 200 * _scale);
            ImGui.TableSetupColumn("About"u8, ImGuiTableColumnFlags.WidthStretch);
            foreach (var p in Maps.Packs)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                if (ImGui.Selectable($"{p.Name}##pack{p.Name}", p == saved, ImGuiSelectableFlags.SpanAllColumns))
                {
                    s.MapPack = p.Name;
                    if (!p.Maps.Any(m => m.Name.Equals(s.MapName, StringComparison.OrdinalIgnoreCase)))
                        s.MapName = p.Maps[0].Name; // "Choose map" starts on a map of the new pack
                }
                ImGui.TableNextColumn();
                ImGui.PushStyleColor(ImGuiCol.Text, Dim);
                ImGui.TextWrapped($"{p.Maps.Count} maps: {p.SizeSummary}{(p.Author.Length > 0 ? $" (by {p.Author})" : "")}");
                if (p.Description.Length > 0)
                    ImGui.TextWrapped(p.Description);
                ImGui.PopStyleColor();
            }
            ImGui.EndTable();
        }

        ImGui.Text("Play"u8);
        ImGui.SameLine(0, 20 * _scale);
        if (ImGui.RadioButton("Random from pack"u8, s.MapRandom))
            s.MapRandom = true;
        ImGui.SameLine();
        if (ImGui.RadioButton("Choose map"u8, !s.MapRandom))
        {
            s.MapRandom = false;
            s.MapPack = pack.Name;
            if (!pack.Maps.Any(m => m.Name.Equals(s.MapName, StringComparison.OrdinalIgnoreCase)))
                s.MapName = pack.Maps[0].Name;
        }
        if (s.MapRandom)
        {
            ImGui.TextColored(Dim, saved != null
                ? $"A random one of the {saved.Maps.Count} maps in {saved.Name} each time. After a match, players vote on the next one from the same pack."
                : $"The pack '{s.MapPack}' isn't in the Maps folder, so it's a random one of all {Maps.All.Length} maps. Pick a pack above.");
            ImGui.Spacing();
            return;
        }
        if (ImGui.BeginTable("##maps"u8, 2, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.ScrollY, new float2(720 * _scale, 260 * _scale)))
        {
            ImGui.TableSetupColumn("Map"u8, ImGuiTableColumnFlags.WidthFixed, 200 * _scale);
            ImGui.TableSetupColumn("About"u8, ImGuiTableColumnFlags.WidthStretch);
            MapSize? group = null;
            foreach (var m in pack.Maps.OrderBy(x => x.Size).ThenBy(x => x.Id))
            {
                if (m.Size != group)
                {
                    group = m.Size;
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.TextColored(Orange, m.Size switch
                    {
                        MapSize.Small => "SMALL",
                        MapSize.Medium => "MEDIUM",
                        _ => "LARGE",
                    });
                    ImGui.TableNextColumn();
                    ImGui.TextColored(Dim, m.Size switch
                    {
                        MapSize.Small => "Up to ~75 m. Fast and close, 2-6 players.",
                        MapSize.Medium => "80-120 m. Mixed ranges, 4-8 players.",
                        _ => "160-240 m. Long sightlines, bring a scope. 6-8 players.",
                    });
                }
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                if (ImGui.Selectable($"{m.Name}##map{m.Id}", m.Name == s.MapName, ImGuiSelectableFlags.SpanAllColumns))
                {
                    s.MapName = m.Name;
                    s.MapPack = pack.Name;
                }
                ImGui.TableNextColumn();
                ImGui.PushStyleColor(ImGuiCol.Text, Dim);
                ImGui.TextWrapped(m.Author.Length > 0 ? $"{m.SizeLabel} - {m.Description} (by {m.Author})" : $"{m.SizeLabel} - {m.Description}");
                ImGui.PopStyleColor();
            }
            ImGui.EndTable();
        }
        ImGui.Spacing();
    }

    private void LevelSummary()
    {
        var p = Progress;
        ImGui.PushFont(default, 22);
        ImGui.TextColored(Orange, $"LEVEL {p.Level}");
        ImGui.PopFont();
        ImGui.SameLine();
        ImGui.TextColored(Dim, p.Level >= CallOfSlop.Progress.MaxLevel
            ? $"  max level - {p.Xp} XP, {p.Kills} kills, {p.Headshots} headshots"
            : $"  {p.XpIntoLevel} / {CallOfSlop.Progress.XpForNext(p.Level)} XP to level {p.Level + 1}");
        ImGui.PushStyleColor(ImGuiCol.PlotHistogram, Orange);
        ImGui.ProgressBar(p.LevelFraction, new float2(360 * _scale, 10 * _scale), ""u8);
        ImGui.PopStyleColor();
        if (p.NextUnlock is { } next)
            ImGui.TextColored(Dim, $"Next unlock: {next.Name} ({next.Kind.ToLowerInvariant()}) at level {next.Level}");
        ImGui.TextColored(Dim, $"Kill +{CallOfSlop.Progress.KillXp} XP   Headshot kill +{CallOfSlop.Progress.HeadshotBonus}   Grenade kill +{CallOfSlop.Progress.GrenadeBonus}   Match +{CallOfSlop.Progress.MatchXp}   Win +{CallOfSlop.Progress.WinXp}");
        ImGui.TextColored(Dim, $"Every level unlocks something: weapons, grenades, perks and up to {CallOfSlop.Progress.MaxLoadoutSlots} loadout slots. Each gun also earns its own weapon XP for attachments (3-7 matches to max one out).");
        ImGui.Spacing();
    }

    private void ProgressionTable()
    {
        var p = Progress;
        var height = MathF.Min(460 * _scale, ImGui.GetMainViewport().WorkSize.Y * 0.5f);
        if (!ImGui.BeginTable("##prog"u8, 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.ScrollY, new float2(720 * _scale, height)))
            return;
        ImGui.TableSetupColumn("Level"u8, ImGuiTableColumnFlags.WidthFixed, 70 * _scale);
        ImGui.TableSetupColumn("XP"u8, ImGuiTableColumnFlags.WidthFixed, 90 * _scale);
        ImGui.TableSetupColumn("Unlocks"u8, ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableHeadersRow();
        for (var level = 1; level <= CallOfSlop.Progress.MaxLevel; level++)
        {
            var reached = p.Level >= level;
            var unlocks = string.Join(", ", p.UnlocksAt(level).Select(CallOfSlop.Progress.Describe));
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextColored(reached ? Green : Dim, $"{level}");
            ImGui.TableNextColumn();
            ImGui.TextColored(Dim, $"{CallOfSlop.Progress.TotalFor(level)}");
            ImGui.TableNextColumn();
            ImGui.PushStyleColor(ImGuiCol.Text, reached ? new float4(1, 1, 1, 1) : Dim);
            ImGui.TextWrapped(unlocks.Length > 0 ? unlocks : "-");
            ImGui.PopStyleColor();
        }
        ImGui.EndTable();
    }

    private void DifficultyPicker(ref BotDifficulty d)
    {
        var v = (int)d;
        ImGui.Text("Difficulty"u8);
        ImGui.SameLine();
        ImGui.RadioButton("Easy"u8, ref v, 0);
        ImGui.SameLine();
        ImGui.RadioButton("Normal"u8, ref v, 1);
        ImGui.SameLine();
        ImGui.RadioButton("Hard"u8, ref v, 2);
        d = (BotDifficulty)v;
    }

    /// <summary>Settings controls shared by the lobby and the pause menu. Changes apply live.</summary>
    // ---- Keybind capture (driven by Program's key/mouse callbacks) -------------------
    public GameAction? Rebinding;
    public double RebindStarted;
    private float _pendingUiScale = -1;

    private void SettingsPanel(Settings s, Audio audio)
    {
        EnsureInputs(s);
        var w = 320 * _scale;
        var panel = new float2(760 * _scale, MathF.Min(560 * _scale, ImGui.GetMainViewport().WorkSize.Y * 0.6f));

        if (!ImGui.BeginTabBar("##settings_tabs"u8))
            return;

        // ---- Player ----------------------------------------------------------------
        if (ImGui.BeginTabItem("Player"u8))
        {
            ImGui.BeginChild("##player"u8, panel);
            ImGui.SetNextItemWidth(w);
            if (ImGui.InputText("Name"u8, _nameInput!))
                s.Name = Settings.Clean(_nameInput!.ToString());
            ImGui.TextColored(Dim, "Name changes apply to your next match."u8);
            ImGui.SetNextItemWidth(w);
            if (ImGui.InputTextWithHint("Master server"u8, "optional, e.g. 203.0.113.45 or myhost.example.com:27050"u8, _masterInput!))
                s.MasterServer = _masterInput!.ToString().Trim();
            ImGui.TextColored(Dim, "Public server list. Anyone can run one with: CallOfSlop.exe --master  (UDP 27050). Leave empty for LAN only."u8);
            ImGui.Spacing();
            ImGui.SetNextItemWidth(w);
            ImGui.SliderFloat("Mouse sensitivity"u8, ref s.Sensitivity, 0.1f, 4f, "%.2fx"u8);
            ImGui.Checkbox("Invert mouse Y"u8, ref s.InvertY);
            ImGui.SetNextItemWidth(w);
            ImGui.SliderFloat("Field of view"u8, ref s.Fov, 60f, 110f, "%.0f deg"u8);
            ImGui.Checkbox("Show network stats"u8, ref s.ShowNetStats);

            ImGui.Spacing();
            ImGui.SeparatorText("Progression"u8);
            ImGui.Text($"Level {Progress.Level}  ({Progress.Xp} XP, {Progress.Kills} kills)");
            if (!_confirmReset)
            {
                if (ImGui.Button("Reset level"u8))
                    _confirmReset = true;
                ImGui.SameLine();
                ImGui.TextColored(Dim, "Back to level 1 - all unlocks lock again."u8);
            }
            else
            {
                ImGui.TextColored(Red, "Reset to level 1 and lose all XP and unlocks? This can't be undone."u8);
                if (ImGui.Button("Yes, reset"u8))
                {
                    Progress.Reset();
                    LoadoutStore.Selected = 0; // only one loadout slot at level 1 (locked picks fall back to defaults when used)
                    LoadoutStore.Save();
                    _confirmReset = false;
                }
                ImGui.SameLine();
                if (ImGui.Button("Cancel"u8))
                    _confirmReset = false;
            }
            ImGui.EndChild();
            ImGui.EndTabItem();
        }

        // ---- Controls ----------------------------------------------------------------
        if (ImGui.BeginTabItem("Controls"u8))
        {
            ImGui.BeginChild("##controls"u8, panel);
            ImGui.TextColored(Dim, "Click a binding, then press a key or mouse button. Esc cancels. Taken keys swap."u8);
            if (ImGui.BeginTable("##binds"u8, 2, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV))
            {
                ImGui.TableSetupColumn("Action"u8, ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Binding"u8, ImGuiTableColumnFlags.WidthFixed, 260 * _scale);
                ImGui.TableHeadersRow();
                for (var i = 0; i < (int)GameAction.COUNT; i++)
                {
                    var action = (GameAction)i;
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.AlignTextToFramePadding();
                    ImGui.Text(Keybinds.Label(action));
                    ImGui.TableNextColumn();
                    var waiting = Rebinding == action;
                    if (waiting)
                        ImGui.PushStyleColor(ImGuiCol.Button, Orange);
                    var label = waiting ? "press a key...##bind" + i : Keybinds.Name(s.Binds[i]) + "##bind" + i;
                    if (ImGui.Button(label, new float2(-1, 0)) && !waiting)
                    {
                        Rebinding = action;
                        RebindStarted = Brutal.GlfwApi.Glfw.Time;
                    }
                    if (waiting)
                        ImGui.PopStyleColor();
                }
                ImGui.EndTable();
            }
            ImGui.TextColored(Dim, "Esc (menu) and the mouse wheel (swap weapon) are fixed."u8);
            if (ImGui.Button("Reset to defaults"u8))
            {
                Keybinds.Defaults.CopyTo(s.Binds, 0);
                Rebinding = null;
            }
            ImGui.EndChild();
            ImGui.EndTabItem();
        }

        // ---- Video -------------------------------------------------------------------
        if (ImGui.BeginTabItem("Video"u8))
        {
            ImGui.BeginChild("##video"u8, panel);
            ImGui.Text("Display mode"u8);
            var mode = (int)s.Display;
            var changed = ImGui.RadioButton("Windowed"u8, ref mode, 0);
            ImGui.SameLine();
            changed |= ImGui.RadioButton("Borderless fullscreen"u8, ref mode, 1);
            ImGui.SameLine();
            changed |= ImGui.RadioButton("Fullscreen"u8, ref mode, 2);
            if (changed)
            {
                s.Display = (DisplayMode)mode;
                s.DisplayDirty = true;
            }

            // Resolutions the primary monitor supports (largest first).
            var modes = MonitorResolutions();
            var current = $"{s.Width} x {s.Height}";
            ImGui.Spacing();
            ImGui.BeginDisabled(s.Display == DisplayMode.Borderless);
            ImGui.SetNextItemWidth(w);
            if (ImGui.BeginCombo("Resolution"u8, current))
            {
                foreach (var (rw, rh) in modes)
                {
                    if (ImGui.Selectable($"{rw} x {rh}", rw == s.Width && rh == s.Height))
                    {
                        s.Width = rw;
                        s.Height = rh;
                        s.DisplayDirty = true;
                    }
                }
                ImGui.EndCombo();
            }
            ImGui.EndDisabled();
            ImGui.TextColored(Dim, s.Display switch
            {
                DisplayMode.Borderless => "Borderless always uses your monitor's native resolution.",
                DisplayMode.Fullscreen => "Exclusive fullscreen switches your monitor to this resolution.",
                _ => "Window size. You can also drag the window edges.",
            });

            ImGui.Spacing();
            ImGui.SetNextItemWidth(w);
            if (_pendingUiScale < 0)
                _pendingUiScale = s.UiScale;
            ImGui.SliderFloat("UI scale"u8, ref _pendingUiScale, 0.6f, 2.0f, "%.2fx"u8);
            if (ImGui.IsItemDeactivatedAfterEdit())
                s.UiScale = _pendingUiScale; // apply on release so the menu doesn't resize under the mouse
            ImGui.SameLine();
            if (ImGui.Button("Reset##ui"u8))
                s.UiScale = _pendingUiScale = 1f;
            ImGui.TextColored(Dim, "Menus and HUD text scale with the window size automatically; this adjusts on top of that."u8);

            ImGui.Spacing();
            ImGui.Text("HUD width"u8);
            var hud = (int)s.HudWidth;
            for (var i = 0; i <= (int)HudWidth.Full; i++)
            {
                if (i > 0)
                    ImGui.SameLine();
                if (ImGui.RadioButton($"{HudLayout.Label((HudWidth)i)}##hud", ref hud, i))
                    s.HudWidth = (HudWidth)hud;
            }
            ImGui.TextColored(Dim, "On ultra-wide monitors, keeps the in-match HUD in a centred area this wide. The crosshair is always centred."u8);

            ImGui.Spacing();
            ImGui.Text("Foliage"u8);
            var foliage = (int)s.FoliageQuality;
            for (var i = 0; i <= (int)FoliageQuality.High; i++)
            {
                if (i > 0)
                    ImGui.SameLine();
                if (ImGui.RadioButton($"{(FoliageQuality)i}##foliage", ref foliage, i))
                    s.FoliageQuality = (FoliageQuality)foliage;
            }
            ImGui.TextColored(Dim, "Grass, bushes and tree tops on maps that have them. Lower it for more fps. Tree trunks are always there (they stop bullets)."u8);

            ImGui.Spacing();
            ImGui.Checkbox("VSync"u8, ref s.VSync);
            ImGui.EndChild();
            ImGui.EndTabItem();
        }

        // ---- Audio -------------------------------------------------------------------
        if (ImGui.BeginTabItem("Audio"u8))
        {
            ImGui.BeginChild("##audio"u8, panel);
            ImGui.SetNextItemWidth(w);
            if (ImGui.SliderFloat("Master volume"u8, ref s.Volume, 0f, 1f, "%.2f"u8))
                audio.MasterVolume = s.Volume;
            ImGui.SameLine();
            if (ImGui.Button("Test"u8))
                audio.Shot(Weapons.DefaultPrimary, null);
            ImGui.SetNextItemWidth(w);
            if (ImGui.SliderFloat("Music volume"u8, ref s.MusicVolume, 0f, 1f, "%.2f"u8))
                audio.Music.Volume = s.MusicVolume;
            ImGui.TextColored(Dim, "Generated hardstyle, industrial, drum & bass and darksynth in the menus and lobbies; silent while a match is live."u8);
            ImGui.EndChild();
            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
    }

    private static List<(int W, int H)>? _resolutions;

    private static List<(int W, int H)> MonitorResolutions()
    {
        if (_resolutions != null)
            return _resolutions;
        var list = new List<(int, int)>();
        try
        {
            foreach (var m in Brutal.GlfwApi.Glfw.PrimaryMonitor.VideoModes)
                if (m.Width >= 800 && m.Height >= 600 && !list.Contains((m.Width, m.Height)))
                    list.Add((m.Width, m.Height));
        }
        catch
        {
            // No monitor info: offer common sizes.
        }
        if (list.Count == 0)
            list.AddRange([(1280, 720), (1600, 900), (1920, 1080), (2560, 1440), (3840, 2160)]);
        _resolutions = list.OrderByDescending(r => r.Item1 * r.Item2).ToList();
        return _resolutions;
    }

    // =====================================================================
    // Pause menu (Esc in a match)
    // =====================================================================

    private (string Text, double Until)? _mapReloadMsg; // result of the last map reload, shown for a few seconds

    public PauseAction DrawPauseMenu(Game game, Settings s)
    {
        if (!PauseOpen)
            return PauseAction.None;
        var action = PauseAction.None;

        CenterNext();
        ImGui.SetNextWindowBgAlpha(0.92f);
        if (ImGui.Begin("##pause"u8, Panel))
        {
            if (_pauseSettings)
            {
                Title("Settings");
                SettingsPanel(s, game.Audio);
                ImGui.Spacing();
                if (BigButton("SAVE & BACK"))
                {
                    s.Save();
                    _pauseSettings = false;
                }
            }
            else
            {
                Title(game.Net.IsOffline ? "Single player - paused" : "Menu - the match keeps running");
                if (BigButton("RESUME"))
                    action = PauseAction.Resume;
                if (BigButton("CHANGE LOADOUT"))
                    action = PauseAction.Loadout;
                if (BigButton("SETTINGS"))
                    _pauseSettings = true;
                if (game.IsServer && BigButton("RELOAD MAP FILES"))
                    _mapReloadMsg = (game.ReloadMapFiles(), (Environment.TickCount64 / 1000.0) + 10);
                if (_mapReloadMsg is { } rm && (Environment.TickCount64 / 1000.0) < rm.Until)
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, Dim);
                    ImGui.TextWrapped(rm.Text);
                    ImGui.PopStyleColor();
                }
                ImGui.Spacing();
                if (BigButton(game.IsServer && !game.Net.IsOffline ? "END MATCH (back to lobby)" : "LEAVE MATCH (back to lobby)"))
                    action = PauseAction.Leave;
                if (BigButton("QUIT GAME"))
                    action = PauseAction.Quit;
            }
        }
        ImGui.End();

        if (action != PauseAction.None)
        {
            PauseOpen = false;
            _pauseSettings = false;
        }
        return action;
    }

    public void ResetLobby() => _page = Page.Main;
}
