using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>What the player chose on the lobby room screen this frame.</summary>
public enum LobbyRoomAction
{
    None,
    Leave,
    Loadout,
}

/// <summary>
/// The pre-match lobby room screen (rules in Source/Game/PreMatchLobby.cs): the player list (rank, hat, team, ping,
/// ready, host), the teams, the map / mode / pack card with the host's settings, the chat, and READY / START. The
/// text comes from a <see cref="LobbyRoomView"/>, rebuilt only when something changes, so a quiet frame builds no strings.
/// </summary>
public sealed partial class Ui
{
    private readonly LobbyRoomView _room = new();
    private ImInputString? _chatInput;
    private string _chatNotice = "";
    private double _chatNoticeAt = -10;
    private int _chatSeen = -1;
    private int _roomPack = -2; // the pack the host's map list shows (-1 True Random, -2: not chosen yet)

    /// <summary>The lobby room's text (the self-test checks it allocates nothing when nothing changed).</summary>
    public LobbyRoomView LobbyRoom => _room;

    /// <summary>A new lobby room session: forget the last one's text and the host's list position.</summary>
    public void ResetLobbyRoom()
    {
        _room.Reset();
        _roomPack = -2;
        _chatSeen = -1;
    }

    private static readonly float4 Gold = new(1f, 0.82f, 0.25f, 1f);

    /// <summary>What a hosted game is doing, for the server browser ("In lobby": you join before the match starts).</summary>
    public static string PhaseLabel(MatchPhase phase) => phase switch
    {
        MatchPhase.Lobby => "In lobby",
        MatchPhase.Warmup => "Warmup",
        MatchPhase.Countdown => "Starting",
        MatchPhase.Ended => "Results",
        _ => "In match",
    };

    /// <summary>Draws the lobby room. <paramref name="address"/>: the host's join address line (empty for clients).</summary>
    public LobbyRoomAction DrawLobbyRoom(Game game, Settings s, string address, double now)
    {
        var action = LobbyRoomAction.None;
        _room.Refresh(game, game.Progress);
        var vp = ImGui.GetMainViewport();
        var size = new float2(MathF.Min(1260 * _scale, vp.WorkSize.X - 24), MathF.Min(800 * _scale, vp.WorkSize.Y - 24));
        ImGui.SetNextWindowPos(new float2(vp.WorkPos.X + vp.WorkSize.X / 2, vp.WorkPos.Y + vp.WorkSize.Y / 2), ImGuiCond.Always, new float2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(size);
        ImGui.SetNextWindowBgAlpha(0.9f);
        const ImGuiWindowFlags flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoScrollbar
                                       | ImGuiWindowFlags.NoScrollWithMouse;
        if (ImGui.Begin("##lobbyroom"u8, flags))
        {
            // ---- Header ---------------------------------------------------------------------------------------
            ImGui.PushFont(default, 30);
            ImGui.TextColored(Orange, "LOBBY"u8);
            ImGui.PopFont();
            ImGui.SameLine(0, 18 * _scale);
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 8 * _scale);
            ImGuiSafe.TextColored(Dim, game.IsServer ? game.ServerName : game.Status);
            if (address.Length > 0)
            {
                ImGui.SameLine(0, 18 * _scale);
                ImGuiSafe.TextColored(Dim, address);
                InviteHeader(); // Copy invite link / code (UiInvite.cs)
            }
            var leaveW = 120 * _scale;
            ImGui.SameLine(ImGui.GetWindowSize().X - ImGui.GetStyle().WindowPadding.X - leaveW);
            if (ImGui.Button("LEAVE##room"u8, new float2(leaveW, 0)))
                action = LobbyRoomAction.Leave;
            ImGui.Separator();

            var avail = ImGui.GetContentRegionAvail();
            var footerH = 64 * _scale;
            var bodyH = MathF.Max(200 * _scale, avail.Y - footerH - 8 * _scale);
            var leftW = MathF.Floor(avail.X * 0.54f);

            // ---- Left: players, teams, chat -----------------------------------------------------------------
            if (ImGui.BeginChild("##roomleft"u8, new float2(leftW, bodyH), ImGuiChildFlags.None, ImGuiWindowFlags.NoScrollbar))
            {
                var chatH = MathF.Max(150 * _scale, bodyH * 0.36f);
                var teamH = game.IsTeamMode ? 92 * _scale : 0;
                var listH = MathF.Max(110 * _scale, bodyH - chatH - teamH - 12 * _scale);
                DrawRoomPlayers(game, new float2(leftW, listH));
                if (game.IsTeamMode)
                    DrawRoomTeams(game, now);
                DrawRoomChat(game, now, chatH);
            }
            ImGui.EndChild();

            // ---- Right: the map and the settings -----------------------------------------------------------
            ImGui.SameLine(0, 12 * _scale);
            if (ImGui.BeginChild("##roomright"u8, new float2(avail.X - leftW - 12 * _scale, bodyH), ImGuiChildFlags.None))
                DrawRoomMatch(game, s);
            ImGui.EndChild();

            // ---- Footer: loadout, ready, start ----------------------------------------------------------------
            DrawRoomFooter(game, footerH, ref action);
        }
        ImGui.End();
        return action;
    }

    private void DrawRoomPlayers(Game game, float2 size)
    {
        ImGui.TextColored(Orange, "PLAYERS"u8);
        ImGui.SameLine();
        ImGuiSafe.TextColored(Dim, _room.ReadyLine);
        ImGui.SameLine();
        ImGuiSafe.TextColored(Dim, _room.BotsLine);
        if (!ImGui.BeginTable("##roomplayers"u8, 5, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.ScrollY,
                new float2(size.X, size.Y - ImGui.GetFrameHeightWithSpacing())))
            return;
        ImGui.TableSetupColumn("Player"u8, ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Rank"u8, ImGuiTableColumnFlags.WidthFixed, 70 * _scale);
        ImGui.TableSetupColumn("Team"u8, ImGuiTableColumnFlags.WidthFixed, 60 * _scale);
        ImGui.TableSetupColumn("Ping"u8, ImGuiTableColumnFlags.WidthFixed, 64 * _scale);
        ImGui.TableSetupColumn("Ready"u8, ImGuiTableColumnFlags.WidthFixed, 56 * _scale);
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableHeadersRow();
        var dl = ImGui.GetWindowDrawList();
        var icon = ImGui.GetTextLineHeight();
        foreach (var r in _room.Rows)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            // A crown for the host, the hat they wear (a little front view of its model), then the name.
            var p0 = ImGui.GetCursorScreenPos();
            if (r.Host)
                DrawCrown(dl, p0, icon);
            if (r.Hat != HatId.None)
                DrawHatIcon(dl, r.Hat, p0 + new float2(icon + 2 * _scale, 0), icon);
            ImGui.Dummy(new float2(icon * 2 + 4 * _scale, icon));
            ImGui.SameLine();
            ImGuiSafe.TextColored(r.Bot ? Dim : Rgba(r.Color), r.Name);
            ImGui.TableNextColumn();
            ImGuiSafe.TextColored(r.Bot ? Dim : Gold, r.Rank);
            ImGui.TableNextColumn();
            if (r.Team == 0)
                ImGui.TextColored(TeamRgba(0), r.Picked ? "Blue *"u8 : "Blue"u8);
            else if (r.Team == 1)
                ImGui.TextColored(TeamRgba(1), r.Picked ? "Red *"u8 : "Red"u8);
            else
                ImGui.TextColored(Dim, "-"u8);
            ImGui.TableNextColumn();
            ImGuiSafe.TextColored(r.Bot || r.Host ? Dim : r.PingMs < 80 ? Green : r.PingMs < 160 ? Orange : Red, r.Ping);
            ImGui.TableNextColumn();
            ImGui.TextColored(r.Ready ? Green : Red, r.Ready ? "[x]"u8 : "[ ]"u8);
        }
        ImGui.EndTable();
    }

    /// <summary>Join Blue / Join Red / Auto (the team-pick rules, TeamPick.cs) and the host's SHUFFLE / BALANCE.</summary>
    private void DrawRoomTeams(Game game, double now)
    {
        if (game.Local is not { } me)
            return;
        ImGui.TextColored(Orange, "TEAMS"u8);
        ImGui.SameLine();
        ImGui.TextColored(TeamRgba(0), "Blue"u8);
        ImGui.SameLine(0, 4 * _scale);
        ImGui.Text(_room.TeamCounts[0] switch { 0 => "0", 1 => "1", 2 => "2", 3 => "3", 4 => "4", 5 => "5", 6 => "6", 7 => "7", 8 => "8", _ => "8+" });
        ImGui.SameLine();
        ImGui.TextColored(TeamRgba(1), "Red"u8);
        ImGui.SameLine(0, 4 * _scale);
        ImGui.Text(_room.TeamCounts[1] switch { 0 => "0", 1 => "1", 2 => "2", 3 => "3", 4 => "4", 5 => "5", 6 => "6", 7 => "7", 8 => "8", _ => "8+" });
        var btn = new float2(118 * _scale, 30 * _scale);
        string? blocked = null;
        for (byte t = 0; t < 2; t++)
        {
            var why = t == 0 ? _room.RefuseBlue : _room.RefuseRed; // TeamRefusal, worked out when the lobby changed
            if (why != null && me.TeamChoice != t)
                blocked ??= why;
            ImGui.BeginDisabled(why != null);
            var tc = TeamRgba(t);
            ImGui.PushStyleColor(ImGuiCol.Button, new float4(tc.X * 0.6f, tc.Y * 0.6f, tc.Z * 0.6f, 1f));
            var on = me.TeamChoice == t;
            if (ImGui.Button(t == 0 ? (on ? "ON BLUE###rblue"u8 : "JOIN BLUE###rblue"u8) : (on ? "ON RED###rred"u8 : "JOIN RED###rred"u8), btn))
                game.RequestTeam(t, now);
            ImGui.PopStyleColor();
            ImGui.EndDisabled();
            ImGui.SameLine();
        }
        ImGui.BeginDisabled(me.TeamChoice > 1);
        if (ImGui.Button(me.TeamChoice > 1 ? "ON AUTO###rauto"u8 : "AUTO###rauto"u8, btn))
            game.RequestTeam(Game.NoTeam, now);
        ImGui.EndDisabled();
        if (game.IsServer)
        {
            ImGui.SameLine(0, 16 * _scale);
            ImGui.BeginDisabled(game.HumansVsBots); // (Humans vs Bots: the sides are fixed)
            if (ImGui.Button("SHUFFLE"u8, new float2(90 * _scale, btn.Y)))
                game.LobbyShuffleTeams(keepPicks: false);
            if (ImGui.IsItemHovered())
                ImGui.SetItemTooltip("Everyone back on Auto, then a random even split."u8);
            ImGui.SameLine();
            if (ImGui.Button("BALANCE"u8, new float2(90 * _scale, btn.Y)))
                game.LobbyShuffleTeams(keepPicks: true);
            if (ImGui.IsItemHovered())
                ImGui.SetItemTooltip("Keep everyone's picks; split the rest evenly."u8);
            ImGui.EndDisabled();
        }
        if (now - game.TeamNoticeAt < 5 && game.TeamNotice.Length > 0)
            ImGuiSafe.TextColored(new float4(1, 1, 1, 1), game.TeamNotice);
        else if (blocked != null)
            ImGuiSafe.TextColored(Orange, blocked);
        else
            ImGui.TextColored(Dim, "* picked their side (autobalance leaves them be). Pick freely here: no cooldown before the match."u8);
    }

    private void DrawRoomChat(Game game, double now, float height)
    {
        ImGui.TextColored(Orange, "CHAT"u8);
        var inputH = ImGui.GetFrameHeightWithSpacing();
        if (ImGui.BeginChild("##roomchat"u8, new float2(0, height - inputH - ImGui.GetTextLineHeightWithSpacing()), ImGuiChildFlags.Borders))
        {
            var lines = _room.ChatText;
            var chat = game.Chat;
            for (var i = 0; i < lines.Count && i < chat.Count; i++)
            {
                var from = chat[i].From;
                ImGuiSafe.TextColored(game.Players.TryGetValue(from, out var p) ? Rgba(game.IsTeamMode && p.Team < 2 ? GameModes.TeamColor(p.Team) : p.Color) : Dim, lines[i]);
            }
            if (lines.Count == 0)
                ImGui.TextColored(Dim, "Say hello. Enter sends."u8);
            if (_chatSeen != game.ChatVersion)
            {
                _chatSeen = game.ChatVersion;
                ImGui.SetScrollHereY(1f); // a new line: show it
            }
        }
        ImGui.EndChild();
        _chatInput ??= new ImInputString(ChatRules.BoxCapacity);
        var sendW = 80 * _scale;
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - sendW - ImGui.GetStyle().ItemSpacing.X);
        var send = false;
        try
        {
            send = ImGui.InputTextWithHint("##chatinput"u8, "Type a message..."u8, _chatInput, ImGuiInputTextFlags.EnterReturnsTrue);
        }
        catch (Exception ex)
        {
            ImGuiSafe.FieldFailed("lobby chat", ex);
        }
        if (send)
            ImGui.SetKeyboardFocusHere(-1); // keep typing after Enter
        ImGui.SameLine();
        send |= ImGui.Button("SEND##chat"u8, new float2(sendW, 0));
        if (send)
        {
            _chatInput.EvaluateLength(); // (the box only updates its length itself when Enter is pressed)
            var text = ChatRules.Clean(_chatInput.ToString());
            if (text.Length > 0)
            {
                var why = game.SendChat(text, now);
                if (why is { Length: > 0 })
                {
                    _chatNotice = why;
                    _chatNoticeAt = now;
                }
                else
                    _chatInput.Clear();
            }
        }
        if (now - _chatNoticeAt < 3)
        {
            ImGui.SameLine();
            ImGuiSafe.TextColored(Orange, _chatNotice);
        }
    }

    /// <summary>The map, mode and pack card, then (host) the settings, changed live for everyone.</summary>
    private void DrawRoomMatch(Game game, Settings s)
    {
        ImGui.TextColored(Orange, "NEXT MATCH"u8);
        ImGui.PushFont(default, 26);
        ImGuiSafe.TextColored(new float4(1, 1, 1, 1), _room.MapTitle);
        ImGui.PopFont();
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - 8 * _scale);
        ImGuiSafe.TextColored(Dim, _room.MapInfo);
        ImGuiSafe.TextColored(new float4(0.9f, 0.9f, 0.92f, 1f), _room.ModeLine);
        ImGuiSafe.TextColored(Dim, _room.PackLine);
        ImGuiSafe.TextColored(Dim, _room.SettingsLine);
        ImGui.PopTextWrapPos();
        ImGui.Spacing();
        if (!game.IsServer)
        {
            ImGui.TextColored(Dim, "The host picks the map, mode and settings; you see every change here as it happens."u8);
            return;
        }
        if (!ImGui.CollapsingHeader("HOST SETTINGS"u8, ImGuiTreeNodeFlags.DefaultOpen))
            return;
        ImGui.BeginDisabled(game.LobbyCountdown > 0); // nothing changes under a running countdown

        // Mode
        ImGui.SetNextItemWidth(240 * _scale);
        if (ImGui.BeginCombo("Mode##room"u8, game.Module.Name))
        {
            foreach (var m in GameModeRegistry.ForMap(World.Map))
            {
                var ok = game.LobbyModeAllowed(m.Id);
                ImGui.BeginDisabled(!ok);
                if (ImGui.Selectable(m.Name, m.Id == game.Mode) && game.LobbySetMode(m.Id))
                    s.Mode = m.Id;
                ImGui.EndDisabled();
            }
            ImGui.EndCombo();
        }

        // Pack and map (maps too small for the people here are greyed out: the vote's rule)
        var packs = Maps.Packs;
        if (_roomPack == -2)
            _roomPack = game.TrueRandom ? -1 : Math.Max(0, Array.FindIndex(packs, p => p == World.Map.Pack || (World.Map.Pack != null && p.Name == World.Map.Pack.Name)));
        _roomPack = Math.Clamp(_roomPack, -1, packs.Length - 1);
        ImGui.SetNextItemWidth(240 * _scale);
        if (ImGui.BeginCombo("Pack##room"u8, _roomPack >= 0 ? packs[_roomPack].Name : MapPack.TrueRandomName))
        {
            for (var i = 0; i < packs.Length; i++)
                if (packs[i].Maps.Count > 0 && ImGui.Selectable(packs[i].Name, i == _roomPack))
                    _roomPack = i;
            if (ImGui.Selectable(MapPack.TrueRandomName, _roomPack == -1))
                _roomPack = -1;
            ImGui.EndCombo();
        }
        ImGui.SameLine();
        if (ImGui.Button("Random map##room"u8))
        {
            var pack = _roomPack >= 0 ? packs[_roomPack] : null;
            if (game.LobbyRandomMap(pack) is { } picked)
                RememberMap(s, picked, pack, random: true);
        }
        if (ImGui.IsItemHovered())
            ImGui.SetItemTooltip("A random map from this pack that takes everyone here (True Random: from every pack). It loads now, so the start is quick."u8);
        _room.RefreshMapList(game, _roomPack);
        if (ImGui.BeginChild("##roommaps"u8, new float2(0, 150 * _scale), ImGuiChildFlags.Borders))
        {
            foreach (var e in _room.MapList)
            {
                ImGui.BeginDisabled(!e.Allowed);
                if (ImGui.Selectable(e.Label, e.Current) && !e.Current && game.LobbySetMap(e.Map, _roomPack == -1))
                    RememberMap(s, e.Map, _roomPack >= 0 ? packs[_roomPack] : null, random: false);
                ImGui.EndDisabled();
            }
        }
        ImGui.EndChild();

        // Score limit, time limit, auto-start, between matches
        var limitMode = game.ScoreLimitMode;
        var minutes = game.MatchMinutes;
        var auto = game.AutoStartSeconds;
        var back = game.ReturnToLobby;
        var buffs = game.FrenzyBuffsEnabled;
        if (ScoreLimits.HasLimit(game.Mode))
        {
            ImGui.SetNextItemWidth(240 * _scale);
            if (ImGui.BeginCombo("Score limit##room"u8, ScoreLimits.ChoiceName(limitMode)))
            {
                if (ImGui.Selectable(ScoreLimits.ChoiceName(ScoreLimitMode.Auto), limitMode == ScoreLimitMode.Auto))
                    limitMode = ScoreLimitMode.Auto;
                if (ImGui.Selectable(ScoreLimits.ChoiceName(ScoreLimitMode.Fixed), limitMode == ScoreLimitMode.Fixed))
                    limitMode = ScoreLimitMode.Fixed;
                ImGui.EndCombo();
            }
        }
        if (game.Module.HasTimeLimit)
        {
            ImGui.SetNextItemWidth(240 * _scale);
            if (ImGui.BeginCombo("Time limit##room"u8, MinutesLabel(minutes)))
            {
                foreach (var m in Game.MatchMinuteChoices)
                    if (ImGui.Selectable(MinutesLabel(m), m == minutes))
                        minutes = m;
                ImGui.EndCombo();
            }
        }
        var autoOn = auto > 0;
        if (ImGui.Checkbox("Auto-start once everyone is ready##room"u8, ref autoOn))
            auto = autoOn ? 30 : 0;
        if (ImGui.IsItemHovered())
            ImGui.SetItemTooltip("On: the countdown starts by itself once everyone is ready, or this many seconds after the first person readies up. Off: you press START."u8);
        if (auto > 0)
        {
            ImGui.SameLine();
            ImGui.SetNextItemWidth(120 * _scale);
            ImGui.SliderInt("s##roomauto"u8, ref auto, 5, 120);
        }
        ImGui.Checkbox("Return to lobby between matches##room"u8, ref back);
        if (ImGui.IsItemHovered())
            ImGui.SetItemTooltip("On: after the results and the map vote everyone comes back here. Off: the next match's warmup starts straight away."u8);
        ImGui.Checkbox("Frenzy buffs##room"u8, ref buffs);
        ImGui.SetNextItemWidth(160 * _scale);
        if (ImGui.BeginCombo("Gravity##room"u8, $"{game.GravityPercent}%"))
        {
            foreach (var g in Game.GravityPercentChoices)
                if (ImGui.Selectable($"{g}%{(g == 100 ? " (the map's own)" : "")}", g == game.GravityPercent))
                {
                    game.GravityPercent = g; // told to everyone in the lobby state (GameGravity.cs)
                    s.Gravity = g;
                }
            ImGui.EndCombo();
        }
        if (limitMode != game.ScoreLimitMode || minutes != game.MatchMinutes || auto != game.AutoStartSeconds || back != game.ReturnToLobby || buffs != game.FrenzyBuffsEnabled)
        {
            game.LobbySetOptions(limitMode, minutes, auto, back, buffs);
            s.ScoreLimit = limitMode;
            s.MatchMinutes = minutes;
            s.AutoStart = auto;
            s.LobbyReturn = back;
            s.FrenzyBuffs = buffs;
        }

        // Bots
        var bots = game.WantedBots;
        var maxBots = Math.Max(0, game.MapMaxPlayers - _room.Humans);
        ImGui.SetNextItemWidth(160 * _scale);
        var botsChanged = ImGui.SliderInt("Bots##room"u8, ref bots, 0, Math.Max(maxBots, Math.Min(game.WantedBots, Game.MaxPlayers - 1)));
        var skill = game.BotSkill;
        ImGui.SameLine();
        ImGui.SetNextItemWidth(120 * _scale);
        if (ImGui.BeginCombo("##roomskill"u8, SkillName(skill)))
        {
            foreach (var d in new[] { BotDifficulty.Easy, BotDifficulty.Normal, BotDifficulty.Hard, BotDifficulty.Insane })
                if (ImGui.Selectable(SkillName(d), d == skill))
                {
                    skill = d;
                    botsChanged = true;
                }
            ImGui.EndCombo();
        }
        if (botsChanged)
        {
            game.LobbySetBots(bots, skill);
            s.HostBots = Math.Max(0, bots);
            s.Difficulty = skill;
        }

        // Team rules
        if (game.IsTeamMode)
        {
            var sel = (int)game.TeamSelection;
            var diff = game.MaxTeamDiff;
            ImGui.TextColored(Dim, "Team selection:"u8);
            ImGui.SameLine();
            ImGui.RadioButton("Manual##roomteam"u8, ref sel, (int)TeamSelection.Manual);
            ImGui.SameLine();
            ImGui.RadioButton("Auto only##roomteam"u8, ref sel, (int)TeamSelection.AutoOnly);
            if (sel == (int)TeamSelection.Manual)
            {
                ImGui.SameLine();
                ImGui.SetNextItemWidth(110 * _scale);
                ImGui.SliderInt("max difference##room"u8, ref diff, 0, TeamRules.MaxDiffLimit);
            }
            if (sel != (int)game.TeamSelection || diff != game.MaxTeamDiff)
            {
                game.LobbySetTeamRules((TeamSelection)sel, diff);
                s.TeamSelect = game.TeamSelection;
                s.TeamMaxDiff = game.MaxTeamDiff;
            }
            var balance = game.TeamBalance;
            var humansTeam = game.HumansTeam;
            if (TeamBalancePicker(ref balance, ref humansTeam, "room")) // Auto / Off / Humans vs Bots (UiTeams.cs, TeamBalance.cs)
            {
                game.SetTeamBalance(balance, humansTeam);
                s.TeamBalance = game.TeamBalance;
                s.HumansTeam = game.HumansTeam;
            }
        }
        ImGui.EndDisabled();
    }

    private static string SkillName(BotDifficulty d) => d switch
    {
        BotDifficulty.Easy => "Easy",
        BotDifficulty.Hard => "Hard",
        BotDifficulty.Insane => "Insane",
        _ => "Normal",
    };

    private static string MinutesLabel(int m) => m switch
    {
        5 => "5 minutes",
        10 => "10 minutes",
        15 => "15 minutes",
        20 => "20 minutes",
        30 => "30 minutes",
        _ => $"{m} minutes",
    };

    /// <summary>The host's map pick is remembered for the next time they host (saved when the match starts).</summary>
    private static void RememberMap(Settings s, Map map, MapPack? pack, bool random)
    {
        s.MapName = map.Name;
        s.MapRandom = random;
        s.MapPack = pack?.Name ?? MapPack.TrueRandomKey;
    }

    private void DrawRoomFooter(Game game, float footerH, ref LobbyRoomAction action)
    {
        ImGui.Separator();
        var me = game.Local;
        var btn = new float2(180 * _scale, footerH - 18 * _scale);
        if (ImGui.Button("LOADOUT"u8, btn))
            action = LobbyRoomAction.Loadout;
        if (ImGui.IsItemHovered())
            ImGui.SetItemTooltip("Edit your loadouts. You spawn with the selected one; you can still change it in the countdown."u8);
        ImGui.SameLine();
        // Your hat, as the others see it in the list.
        if (me != null && me.Hat != HatId.None)
        {
            var p0 = ImGui.GetCursorScreenPos();
            DrawHatIcon(ImGui.GetWindowDrawList(), me.Hat, p0 + new float2(0, 4 * _scale), btn.Y - 8 * _scale);
            ImGui.Dummy(new float2(btn.Y, btn.Y));
            ImGui.SameLine();
        }
        var ready = me?.Ready ?? false;
        ImGui.PushStyleColor(ImGuiCol.Button, ready ? new float4(0.2f, 0.55f, 0.25f, 1f) : new float4(0.75f, 0.42f, 0.12f, 1f));
        ImGui.BeginDisabled(me == null);
        if (ImGui.Button(ready ? "READY!  (click to un-ready)###roomready"u8 : "READY UP###roomready"u8, new float2(260 * _scale, btn.Y)))
            game.ToggleReady();
        ImGui.EndDisabled();
        ImGui.PopStyleColor();
        ImGui.SameLine(0, 16 * _scale);

        var counting = game.LobbyCountdown > 0;
        if (game.IsServer)
        {
            if (counting)
            {
                if (ImGui.Button("CANCEL"u8, new float2(140 * _scale, btn.Y)))
                    game.LobbyCancel();
            }
            else
            {
                ImGui.BeginDisabled(!game.CanLobbyStart);
                ImGui.PushStyleColor(ImGuiCol.Button, new float4(0.94f, 0.59f, 0.16f, 0.92f));
                ImGui.PushStyleColor(ImGuiCol.Text, new float4(0.09f, 0.06f, 0.03f, 1f));
                if (ImGui.Button("START"u8, new float2(160 * _scale, btn.Y)))
                    game.LobbyStart(force: false);
                ImGui.PopStyleColor(2);
                ImGui.EndDisabled();
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    ImGui.SetItemTooltip("Starts the 5 second countdown once everyone else is ready."u8);
                ImGui.SameLine();
                if (ImGui.Button("FORCE START"u8, new float2(140 * _scale, btn.Y)))
                    game.LobbyStart(force: true);
            }
            ImGui.SameLine(0, 16 * _scale);
        }
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (btn.Y - ImGui.GetTextLineHeight() * 1.4f) / 2);
        ImGui.PushFont(default, 22);
        ImGuiSafe.TextColored(counting ? Orange : Dim, _room.CountdownLine);
        ImGui.PopFont();
    }

    /// <summary>A little gold crown (the host).</summary>
    private static void DrawCrown(ImDrawListPtr dl, float2 p0, float s)
    {
        var col = new ImColor8(240, 196, 60, 255);
        var b = p0.Y + s * 0.85f;
        dl.AddRectFilled(new float2(p0.X + s * 0.1f, b - s * 0.22f), new float2(p0.X + s * 0.9f, b), col);
        ImDrawListExtensions.AddTriangleFilled(dl, new float2(p0.X + s * 0.1f, b - s * 0.2f), new float2(p0.X + s * 0.2f, p0.Y + s * 0.15f), new float2(p0.X + s * 0.38f, b - s * 0.2f), col);
        ImDrawListExtensions.AddTriangleFilled(dl, new float2(p0.X + s * 0.32f, b - s * 0.2f), new float2(p0.X + s * 0.5f, p0.Y + s * 0.05f), new float2(p0.X + s * 0.68f, b - s * 0.2f), col);
        ImDrawListExtensions.AddTriangleFilled(dl, new float2(p0.X + s * 0.62f, b - s * 0.2f), new float2(p0.X + s * 0.8f, p0.Y + s * 0.15f), new float2(p0.X + s * 0.9f, b - s * 0.2f), col);
    }

    /// <summary>A hat seen from the front: each box of its model as a coloured rectangle, fitted into a <paramref name="s"/> square.</summary>
    private static void DrawHatIcon(ImDrawListPtr dl, HatId id, float2 p0, float s)
    {
        if (Cosmetics.Get(id) is not { } hat || hat.Boxes.Count == 0)
            return;
        var w = MathF.Max(hat.Max.X - hat.Min.X, 0.01f);
        var h = MathF.Max(hat.Max.Y - hat.Min.Y, 0.01f);
        var k = s / MathF.Max(w, h);
        var ox = p0.X + (s - w * k) / 2 - hat.Min.X * k;
        var oy = p0.Y + s - (s - h * k) / 2 + hat.Min.Y * k; // y up in the model, down on screen
        var boxes = hat.Boxes;
        for (var i = 0; i < boxes.Count; i++) // (an index loop: no enumerator to allocate every frame)
        {
            var box = boxes[i];
            var m = box.Local;
            var cx = m.M41;
            var cy = m.M42;
            var hx = 0.5f * MathF.Sqrt(m.M11 * m.M11 + m.M12 * m.M12 + m.M13 * m.M13);
            var hy = 0.5f * MathF.Sqrt(m.M21 * m.M21 + m.M22 * m.M22 + m.M23 * m.M23);
            var c = box.Color;
            dl.AddRectFilled(new float2(ox + (cx - hx) * k, oy - (cy + hy) * k), new float2(ox + (cx + hx) * k, oy - (cy - hy) * k),
                new ImColor8((byte)Math.Clamp(c.X * 255, 0, 255), (byte)Math.Clamp(c.Y * 255, 0, 255), (byte)Math.Clamp(c.Z * 255, 0, 255), 255));
        }
    }
}
