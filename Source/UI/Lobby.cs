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
    Suicide,
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
        ImGuiSafe.SetInput(_nameInput, s.Name);
        _addressInput = new ImInputString(128);
        ImGuiSafe.SetInput(_addressInput, s.JoinAddress);
        _portInput = s.Port;
        _serverNameInput = new ImInputString(33);
        ImGuiSafe.SetInput(_serverNameInput, s.ServerName);
        _masterInput = new ImInputString(128);
        ImGuiSafe.SetInput(_masterInput, s.MasterServer);
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
        ImGuiSafe.TextColored(Dim, subtitle);
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
            ImGuiSafe.TextColored(Dim, _nowPlayingText!); // track names are plain text, never formats
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
        if (_msOpen && _page is (Page.SinglePlayer or Page.Multiplayer))
        {
            DrawMapSelect(s, audio); // the map select screen replaces the lobby panel until it's closed (UiMapSelect.cs)
            return action;
        }
        _msOpen = false;

        CenterNext();
        ImGui.SetNextWindowBgAlpha(0.88f);
        if (ImGui.Begin("##lobby"u8, Panel))
        {
            switch (_page)
            {
                case Page.Main:
                    Title($"{Weapons.All.Length} weapons. {Maps.All.Length} maps. No mercy.");
                    if (message.Length > 0)
                    {
                        ImGuiSafe.TextColored(Red, message);
                        ImGui.Spacing();
                    }
                    if (WeaponSet.ErrorSummary is { } weaponErrors)
                    {
                        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + 560 * _scale); // long file paths: wrap instead of widening the panel
                        ImGuiSafe.TextColored(Red, weaponErrors);
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
                    if (BigButton("WORKBENCH (ZOMBIES PREVIEW)"))
                        OpenWorkbench(); // the Weapon Workbench (UiWorkbench.cs)
                    if (ImGui.IsItemHovered())
                        ImGui.SetItemTooltip("Weapon Workbench: build guns from parts and test fire them. A preview of the coming Zombies mode."u8);
                    if (BigButton("SETTINGS"))
                        _page = Page.Settings;
                    ImGui.Spacing();
                    if (BigButton("QUIT"))
                        action = LobbyAction.Quit;
                    break;

                case Page.SinglePlayer:
                    Title("Single player - you against bots");
                    ModePicker(s);
                    TeamRulesPicker(s); // (UiTeams.cs)
                    MapPicker(s);
                    var spMax = s.LobbyMaxPlayers(Maps.Packs, Maps.All); // the map's player limit (GameLobby.cs)
                    s.Bots = Math.Clamp(s.Bots, 0, spMax - 1);
                    ImGui.SetNextItemWidth(260 * _scale);
                    ImGui.SliderInt("Bots"u8, ref s.Bots, 0, spMax - 1);
                    ImGuiSafe.TextColored(Dim, PlayerCountLine(s, s.Bots + 1, spMax));
                    if (s.Bots == 0)
                        TestModeHint();
                    else
                    {
                        DifficultyPicker(ref s.Difficulty);
                        ImGuiSafe.TextColored(Dim, s.Difficulty switch
                        {
                            BotDifficulty.Easy => "Slow to react, wild aim. Good for trying weapons.",
                            BotDifficulty.Normal => "A fair fight.",
                            BotDifficulty.Insane => "Hard+ aim. In team modes they work as squads: one pins you down while another flanks you.",
                            _ => "Fast reactions, tight aim, they strafe and go for heads.",
                        });
                    }
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
                    Title($"Multiplayer - up to {Game.MaxPlayers} players over LAN or the internet (each map has its own limit)");
                    ImGui.TextColored(Dim, "Hosting or joining opens the lobby: pick teams and loadouts, chat, ready up; the host starts the match."u8);
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
                    TeamRulesPicker(s); // team modes: Manual / Auto only, max difference (UiTeams.cs)
                    MapPicker(s);
                    AutoStartPicker(s);
                    ImGui.Checkbox("Frenzy buffs"u8, ref s.FrenzyBuffs); // FrenzyBuffs.cs
                    ImGuiSafe.TextColored(Dim, s.FrenzyBuffs
                        ? "On: a kill frenzy (level 3+) gives a small buff to guns of the class you last killed with."
                        : "Off: kill frenzies are music and effects only.");
                    ImGui.Checkbox("Destruction"u8, ref s.Destruction); // destructible buildings (GameDestruction.cs)
                    ImGuiSafe.TextColored(Dim, s.Destruction
                        ? "On: on maps with destructible buildings (World Tour), explosives blast holes and floors can come down."
                        : "Off: buildings stay whole (the map is the same, nothing breaks).");
                    ImGui.Checkbox("Allow voice chat"u8, ref s.AllowVoice); // GameVoice.cs
                    ImGui.SameLine();
                    ImGui.Checkbox("Dead players' chat reaches only the dead"u8, ref s.DeadChatPrivate); // LobbyChat.cs
                    ImGui.SetNextItemWidth(200 * _scale);
                    if (ImGui.InputInt("Port"u8, ref _portInput, 1, 100))
                        _portInput = Math.Clamp(_portInput, 1024, 65535);
                    ImGui.Checkbox("Open the port on my router automatically (UPnP)"u8, ref s.UseUpnp);
                    var hostMax = s.LobbyMaxPlayers(Maps.Packs, Maps.All);
                    s.HostBots = Math.Clamp(s.HostBots, 0, Math.Max(0, hostMax - 2)); // room for at least one friend (people replace bots anyway)
                    ImGui.SetNextItemWidth(260 * _scale);
                    ImGui.SliderInt("Fill with bots"u8, ref s.HostBots, 0, Math.Max(0, hostMax - 2));
                    ImGuiSafe.TextColored(Dim, PlayerCountLine(s, s.HostBots + 1, hostMax) + ". People who join take a bot's place.");
                    if (s.HostBots > 0)
                        DifficultyPicker(ref s.Difficulty);
                    else
                        TestModeHint();
                    ImGui.SetNextItemWidth(360 * _scale);
                    try
                    {
                        if (ImGui.InputTextWithHint("Server name"u8, s.ServerNameOrDefault, _serverNameInput!))
                            s.ServerName = Settings.CleanServerName(_serverNameInput!.ToString());
                    }
                    catch (Exception ex)
                    {
                        ImGuiSafe.FieldFailed("server name", ex);
                    }
                    ImGui.Checkbox("Advertise server (list it publicly in the server browser)"u8, ref s.Advertise);
                    if (s.Advertise && s.Master == null)
                        ImGui.TextColored(Orange, "No master server set (Settings > Player), so only people on your network will see it."u8);
                    else
                        ImGuiSafe.TextColored(Dim, s.Advertise
                            ? "Listed publicly through your master server; people on your network always see it too."
                            : "Not listed publicly. People on your network still see it in their browser.");
                    ImGui.Checkbox("Return to lobby between matches"u8, ref s.LobbyReturn); // the lobby room (PreMatchLobby.cs)
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

    /// <summary>"8 / 16 players on Quarry" (or, random from a pack, how many of its maps take that many).</summary>
    private static string PlayerCountLine(Settings s, int players, int max)
    {
        if (!s.MapRandom)
            return $"{players} / {max} players on {s.MapName}";
        var pool = MapPack.RandomPool(s.MapPack, Maps.Packs, Maps.All); // the pack, or every pack for True Random
        var fit = pool.Count(m => m.MaxPlayers >= players);
        return $"{players} / {max} players - {(MapPack.IsTrueRandom(s.MapPack) ? "True Random" : "random")} from the {fit} of {pool.Count} maps that take {players}";
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
        ImGuiSafe.TextColored(Dim, _browser.Searching ? "Searching your network..." : $"{list.Count} game(s) found");
        ImGuiSafe.TextColored(Dim, s.Master is { } m
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
                ImGuiSafe.TextColored(Dim, e.FromMaster ? "  public" : "  LAN");
                ImGui.TableNextColumn();
                ImGuiSafe.TextColored(Maps.Find(i.MapName) != null ? new float4(1, 1, 1, 1) : Orange, i.MapName); // orange: you don't have this map
                ImGui.SameLine();
                ImGuiSafe.TextColored(Dim, GameModes.Short(i.Mode));
                ImGui.SameLine();
                ImGuiSafe.TextColored(i.Phase == MatchPhase.Lobby ? Green : Dim, PhaseLabel(i.Phase)); // "In lobby": join before it starts (UiLobbyRoom.cs)
                if (i.Rotation.Length > 0)
                    ImGuiSafe.TextColored(Orange, i.Rotation); // e.g. "True Random": the next maps come from every pack
                ImGui.TableNextColumn();
                ImGuiSafe.TextColored(i.Players >= i.MaxPlayers && i.Bots == 0 ? Red : new float4(1, 1, 1, 1), i.Bots > 0 ? $"{humans}/{i.MaxPlayers} +{i.Bots} bots" : $"{humans}/{i.MaxPlayers}");
                ImGui.TableNextColumn();
                ImGuiSafe.TextColored(e.PingMs < 80 ? Green : e.PingMs < 160 ? Orange : Red, $"{e.PingMs} ms");
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
        try
        {
            ImGui.InputTextWithHint("Address"u8, "host IP, e.g. 192.168.1.20 or 203.0.113.45"u8, _addressInput!);
        }
        catch (Exception ex)
        {
            ImGuiSafe.FieldFailed("address", ex);
        }
        ImGui.SetNextItemWidth(200 * _scale);
        if (ImGui.InputInt("Port"u8, ref _portInput, 1, 100))
            _portInput = Math.Clamp(_portInput, 1024, 65535);
        ImGui.TextColored(Dim, "Ask the host for the address shown at the top-left of their screen."u8);
        if (BigButton("JOIN GAME"))
        {
            var addr = Settings.CleanAddress(_addressInput!.ToString());
            s.JoinAddress = addr.Length > 0 ? addr : "127.0.0.1";
            s.Port = (ushort)_portInput;
            s.Save();
            action = LobbyAction.Join;
        }
        ImGui.Spacing();
    }

    /// <summary>
    /// The current map choice and "Change map...", which opens the map select screen (UiMapSelect.cs). The lobby
    /// background previews the choice (it tours the pack, or every pack, when random).
    /// </summary>
    private void MapPicker(Settings s)
    {
        ImGui.Text("Map"u8);
        ImGui.SameLine(0, 20 * _scale);
        ImGui.PushFont(default, 18);
        ImGuiSafe.TextColored(Orange, MapSelect.ChoiceText(s));
        ImGui.PopFont();
        if (ImGui.Button("Change map...##openmapselect"u8, new float2(200 * _scale, 0)))
            OpenMapSelect(s);
        ImGui.SameLine();
        if (ImGui.Button("Refresh maps"u8))
            _mapReloadMsg = (Maps.Reload().Summary, (Environment.TickCount64 / 1000.0) + 8);
        if (_mapReloadMsg is { } rm && (Environment.TickCount64 / 1000.0) < rm.Until)
            ImGuiSafe.TextColored(Dim, rm.Text);
        if (Maps.LoadErrors.Count > 0)
            ImGuiSafe.TextColored(Red,
                $"{Maps.LoadErrors.Count} map file(s) in the Maps folder didn't load: {Maps.LoadErrors[0]}{(Maps.LoadErrors.Count > 1 ? " (more in callofslop.log)" : "")}");
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + 640 * _scale);
        if (!s.MapRandom && Maps.Find(s.MapName) is { } chosen)
            ImGuiSafe.TextColored(Dim, $"{chosen.SizeLabel}, up to {chosen.MaxPlayers} players. {chosen.Description}");
        else if (MapPack.IsTrueRandom(s.MapPack))
            ImGuiSafe.TextColored(Dim, $"A random one of {MapPack.TrueRandomPool(Maps.Packs, Maps.All).Count} maps from every pack each time (Dev test maps left out). After a match, players vote on the next one from any pack.");
        else if (Maps.FindPack(s.MapPack) is { } pack)
            ImGuiSafe.TextColored(Dim, $"A random one of the {pack.Maps.Count} maps in {pack.Name} each time. After a match, players vote on the next one from the same pack.");
        else
            ImGuiSafe.TextColored(Dim, $"The pack '{s.MapPack}' isn't in the Maps folder, so it's a random one of all {Maps.All.Length} maps. Change map to pick a pack.");
        ImGui.PopTextWrapPos();
        ImGui.Spacing();
    }

    private void LevelSummary()
    {
        var p = Progress;
        ImGui.PushFont(default, 22);
        ImGuiSafe.TextColored(Orange, $"{PrestigePrefix(p)}LEVEL {p.Level}");
        ImGui.PopFont();
        ImGui.SameLine();
        ImGuiSafe.TextColored(Dim, p.Level >= CallOfSlop.Progress.MaxLevel
            ? $"  max level - {p.Xp} XP, {p.Kills} kills, {p.Headshots} headshots"
            : $"  {p.XpIntoLevel} / {CallOfSlop.Progress.XpForNext(p.Level)} XP to level {p.Level + 1}");
        ImGui.PushStyleColor(ImGuiCol.PlotHistogram, Orange);
        ImGui.ProgressBar(p.LevelFraction, new float2(360 * _scale, 10 * _scale), ""u8);
        ImGui.PopStyleColor();
        PrestigeButton(); // UiPrestige.cs
        if (p.NextUnlock is { } next)
            ImGuiSafe.TextColored(Dim, $"Next unlock: {next.Name} ({next.Kind.ToLowerInvariant()}) at level {next.Level}");
        ImGuiSafe.TextColored(Dim, $"Kill +{CallOfSlop.Progress.KillXp} XP   Headshot kill +{CallOfSlop.Progress.HeadshotBonus}   Grenade kill +{CallOfSlop.Progress.GrenadeBonus}   Match +{CallOfSlop.Progress.MatchXp}   Win +{CallOfSlop.Progress.WinXp}");
        ImGuiSafe.TextColored(Dim, $"Every level unlocks something: weapons, grenades, perks and up to {CallOfSlop.Progress.MaxLoadoutSlots} loadout slots. Each gun also earns its own weapon XP for attachments (3-7 matches to max one out).");
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
            ImGuiSafe.TextColored(reached ? Green : Dim, $"{level}");
            ImGui.TableNextColumn();
            ImGuiSafe.TextColored(Dim, $"{CallOfSlop.Progress.TotalFor(level)}");
            ImGui.TableNextColumn();
            ImGui.PushStyleColor(ImGuiCol.Text, reached ? new float4(1, 1, 1, 1) : Dim);
            ImGuiSafe.TextWrapped(unlocks.Length > 0 ? unlocks : "-");
            ImGui.PopStyleColor();
        }
        ImGui.EndTable();
    }

    /// <summary>Shown under a bot slider at 0: what a match with nobody else in it does.</summary>
    private void TestModeHint()
    {
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + 560 * _scale);
        ImGui.TextColored(Orange, "No bots (test mode): while you're alone the match clock stops and nobody can win, so try anything for as long as you like."u8);
        ImGui.PopTextWrapPos();
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
        ImGui.SameLine();
        ImGui.RadioButton("Insane"u8, ref v, 3);
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
            try
            {
                if (ImGui.InputText("Name"u8, _nameInput!))
                    s.Name = Settings.Clean(_nameInput!.ToString());
            }
            catch (Exception ex)
            {
                ImGuiSafe.FieldFailed("player name", ex);
            }
            ImGui.TextColored(Dim, "Name changes apply to your next match."u8);
            ImGui.SetNextItemWidth(w);
            try
            {
                if (ImGui.InputTextWithHint("Master server"u8, "optional, e.g. 203.0.113.45 or myhost.example.com:27050"u8, _masterInput!))
                    s.MasterServer = Settings.CleanAddress(_masterInput!.ToString());
            }
            catch (Exception ex)
            {
                ImGuiSafe.FieldFailed("master server", ex);
            }
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
            // Where the saves live (SavePaths.cs): they carry over to new builds automatically.
            ImGui.Spacing();
            ImGui.SeparatorText("Saves"u8);
            ImGuiSafe.TextColored(Dim, $"Save folder: {SavePaths.Folder}");
            if (ImGui.Button("Open save folder"u8))
                SavePaths.OpenFolder();
            ImGui.SameLine();
            ImGui.TextColored(Dim, "Progress, loadouts and settings carry over to new builds."u8);
            if (SavePaths.Warning is { } saveWarning)
                ImGuiSafe.TextColored(Red, saveWarning);
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
            ImGuiSafe.TextColored(Dim, s.Display switch
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
            ImGui.Text("Name tags"u8);
            var tagMode = (int)s.NameTags;
            for (var i = 0; i <= (int)NameTagMode.Off; i++)
            {
                if (i > 0)
                    ImGui.SameLine();
                if (ImGui.RadioButton($"{NameTagRules.Label((NameTagMode)i)}##nametags", ref tagMode, i))
                    s.NameTags = (NameTagMode)tagMode;
            }
            ImGui.SameLine();
            ImGui.Checkbox("Teammate health bars"u8, ref s.NameTagHealth);
            ImGui.TextColored(Dim, "Names over heads. Teammates show within 60 m, through walls. Enemies show only while you aim at one you can see (40 m)."u8);

            ImGui.Spacing();
            ImGui.SetNextItemWidth(w);
            ImGui.SliderInt("Head bob"u8, ref s.HeadBob, 0, 100, "%d%%"u8, ImGuiSliderFlags.AlwaysClamp);
            ImGui.TextColored(Dim, "How much the camera bobs as you walk and sprint. At 0 the view stays perfectly steady. Never affects your aim."u8);

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
            ImGui.Text("Sky quality"u8);
            var skyQ = (int)s.SkyQuality;
            for (var i = 0; i <= (int)SkyQuality.High; i++)
            {
                if (i > 0)
                    ImGui.SameLine();
                if (ImGui.RadioButton($"{(SkyQuality)i}##sky", ref skyQ, i))
                    s.SkyQuality = (SkyQuality)skyQ;
            }
            ImGui.TextColored(Dim, "Maps with a sky: Low draws the gradient, sun and moon; High adds drifting clouds and stars."u8);

            ImGui.Spacing();
            ImGui.Text("Bullet impacts"u8);
            var impacts = (int)s.ImpactDecals;
            for (var i = 0; i <= (int)ImpactDecalQuality.High; i++)
            {
                if (i > 0)
                    ImGui.SameLine();
                if (ImGui.RadioButton($"{(ImpactDecalQuality)i}##impacts", ref impacts, i))
                    s.ImpactDecals = (ImpactDecalQuality)impacts;
            }
            ImGui.TextColored(Dim, "Bullet holes left on walls and floors: High keeps 1024, Medium 512, Low 128 (each player at most 64; the oldest fade out)."u8);

            ImGui.Spacing();
            ImGui.Text("Frenzy visuals"u8);
            var frenzyFx = (int)s.FrenzyVisuals;
            for (var i = 0; i <= (int)FrenzyVisualMode.Off; i++)
            {
                if (i > 0)
                    ImGui.SameLine();
                if (ImGui.RadioButton($"{(FrenzyVisualMode)i}##frenzyfx", ref frenzyFx, i))
                    s.FrenzyVisuals = (FrenzyVisualMode)frenzyFx;
            }
            ImGui.TextColored(Dim, "On a kill spree, each weapon class gets its own look: coloured tracers, sparks, a glowing gun, a tint at the screen edges. Subtle keeps the colours with fewer effects; Off shows none."u8);

            ImGui.Spacing();
            ImGui.Text("Blood overlay"u8);
            var blood = (int)s.BloodOverlay;
            for (var i = 0; i <= (int)BloodMode.Off; i++)
            {
                if (i > 0)
                    ImGui.SameLine();
                if (ImGui.RadioButton($"{(BloodMode)i}##blood", ref blood, i))
                    s.BloodOverlay = (BloodMode)blood;
            }
            ImGui.Text("Blood colour"u8);
            var bloodColour = (int)s.BloodColour;
            for (var i = 0; i <= (int)BloodColour.Dark; i++)
            {
                ImGui.SameLine();
                if (ImGui.RadioButton($"{(BloodColour)i}##bloodcol", ref bloodColour, i))
                    s.BloodColour = (BloodColour)bloodColour;
            }
            ImGui.TextColored(Dim, "Blood splatters at the screen edges as your health drops (more the lower it gets, from the side you were hit), and blood marks on hurt players and zombies. They fade as health comes back. Subtle: fewer and fainter; Off: none. Dark: a darker colour instead of red."u8);
            ImGui.Text("Gore"u8);
            var gore = (int)s.Gore;
            for (var i = 0; i <= (int)GoreMode.Off; i++)
            {
                if (i > 0)
                    ImGui.SameLine();
                if (ImGui.RadioButton($"{(GoreMode)i}##gore", ref gore, i))
                    s.Gore = (GoreMode)gore;
            }
            ImGui.TextColored(Dim, "Animals you shoot and anyone killed right next to a blast burst into meaty gibs. Reduced throws fewer and leaves no blood; Off: animals just poof, blast kills fall as ragdolls."u8);

            ImGui.Spacing();
            ImGui.Checkbox("VSync"u8, ref s.VSync);
            ImGui.Checkbox("Show FPS counter"u8, ref s.ShowFps);
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
            var musicStyle = (int)s.MusicChoice;
            ImGui.Text("Music style"u8);
            ImGui.SameLine();
            ImGui.RadioButton("Mixed"u8, ref musicStyle, (int)MusicChoice.Mixed);
            ImGui.SameLine();
            ImGui.RadioButton("Hardbass only"u8, ref musicStyle, (int)MusicChoice.HardbassOnly);
            s.MusicChoice = (MusicChoice)musicStyle;
            audio.Music.HardbassOnly = s.MusicChoice == MusicChoice.HardbassOnly;
            if (ImGui.Checkbox("Kill frenzy music"u8, ref s.FrenzyMusic))
                audio.Music.Frenzy.Enabled = s.FrenzyMusic;
            if (ImGui.IsItemHovered())
                ImGui.SetItemTooltip("During a match: hard music fades in as you chain kills, gets harder the more and the faster you kill, and fades out when the killing stops. A long spree reaches FRENZY (louder than the menu music) and MAX FRENZY (as loud as the gunfire)."u8);
            if (s.FrenzyMusic)
            {
                ImGui.SameLine();
                ImGui.SetNextItemWidth(w * 0.5f);
                if (ImGui.SliderFloat("Frenzy volume"u8, ref s.FrenzyVolume, 0f, 1f, "%.2f"u8))
                    audio.Music.Frenzy.Volume = s.FrenzyVolume;
            }
            ImGui.SetNextItemWidth(w);
            if (ImGui.SliderFloat("Effects volume"u8, ref s.EffectsVolume, 0f, 1f, "%.2f"u8))
                audio.EffectsVolume = s.EffectsVolume;
            ImGui.SameLine();
            if (ImGui.Button("Test##fx"u8))
                audio.Effect(Sfx.Headshot, 0.8f);
            ImGui.Text("Frenzy gun sound"u8);
            var frenzyGun = (int)s.FrenzyGunSound;
            for (var i = 0; i <= (int)FrenzyGunSoundMode.Off; i++)
            {
                ImGui.SameLine();
                if (ImGui.RadioButton($"{(FrenzyGunSoundMode)i}##frenzygun", ref frenzyGun, i))
                    s.FrenzyGunSound = (FrenzyGunSoundMode)frenzyGun;
            }
            if (ImGui.IsItemHovered())
                ImGui.SetItemTooltip("On a kill spree your own gun takes on the character of your frenzy class (the weapon of your last kill): extra punch, a class sound (pistol twang, SMG buzz, sniper whip crack, shotgun double boom...), more echo and width the higher the frenzy. Subtle keeps it lighter; Off leaves your gun as it is."u8);
            ImGui.SetNextItemWidth(w);
            if (ImGui.SliderFloat("Own gun volume"u8, ref s.OwnGunVolume, 0f, 1f, "%.2f"u8))
                audio.OwnGunVolume = s.OwnGunVolume;
            ImGui.SameLine();
            if (ImGui.Button("Test##owngun"u8))
                audio.Shot(Weapons.DefaultPrimary, null);
            if (ImGui.IsItemHovered())
                ImGui.SetItemTooltip("Your own gunshots, on top of the effects volume. At 1 your gun is the loudest thing you hear (other players' shots are about 7.5 dB quieter up close and fade with distance)."u8);
            ImGui.SetNextItemWidth(w);
            if (ImGui.SliderFloat("Footstep volume"u8, ref s.FootstepVolume, 0f, 1f, "%.2f"u8))
                audio.FootstepVolume = s.FootstepVolume;
            ImGui.SameLine();
            if (ImGui.Button("Test##steps"u8))
                audio.Footstep(new StepEvent(0, StepKind.Run, FootSurface.Concrete, default, 1f), self: true);
            ImGui.TextColored(Dim, "Generated hardbass, hardstyle, industrial, drum & bass and darksynth in the menus and lobbies; silent while a match is live."u8);
            DrawVoiceSettings(s, audio); // voice chat (UiChat.cs)
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
        {
            _pauseTeams = false;
            ResetPausePlayers(); // UiChat.cs
            return PauseAction.None;
        }
        var action = PauseAction.None;

        CenterNext();
        ImGui.SetNextWindowBgAlpha(0.92f);
        if (ImGui.Begin("##pause"u8, Panel))
        {
            if (_pauseTeams)
                DrawPauseTeams(game, s); // UiTeams.cs
            else if (_pausePlayers)
                DrawPausePlayers(game, s); // mute voice / chat (UiChat.cs)
            else if (_pauseSettings)
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
                if (game.IsTeamMode && BigButton("TEAMS"))
                    _pauseTeams = true; // Join Blue / Join Red / Auto and the rosters (UiTeams.cs)
                if (!game.Net.IsOffline && BigButton("PLAYERS (mute)"))
                    _pausePlayers = true; // mute someone's voice or chat (UiChat.cs)
                ImGui.BeginDisabled(!game.CanRequestSuicide);
                if (BigButton("KILL YOURSELF (respawn)"))
                    action = PauseAction.Suicide;
                ImGui.EndDisabled();
                if (BigButton("SETTINGS"))
                    _pauseSettings = true;
                if (game.IsServer && BigButton("RELOAD MAP FILES"))
                    _mapReloadMsg = (game.ReloadMapFiles(), (Environment.TickCount64 / 1000.0) + 10);
                if (_mapReloadMsg is { } rm && (Environment.TickCount64 / 1000.0) < rm.Until)
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, Dim);
                    ImGuiSafe.TextWrapped(rm.Text);
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
            _pauseTeams = false;
            ResetPausePlayers();
        }
        return action;
    }

    public void ResetLobby() => _page = Page.Main;
}
