using Brutal.GlfwApi;
using Brutal.ImGuiApi;
using Brutal.ImGuiApi.Backends;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// Dear ImGui (Brutal.ImGui + its GLFW/OpenGL backends) for the loadout menu and HUD text.
/// </summary>
public sealed partial class Ui : IDisposable
{
    private GlfwBackend? _platform;
    private OpenGlBackend? _renderer;
    private float _scale = 1f;
    private GlfwWindow? _window;
    private ImGuiStyle _baseStyle; // unscaled style, so rescaling never compounds

    /// <summary>The player's UI scale setting (multiplies the automatic, resolution-based scale).</summary>
    public float UserScale = 1f;

    /// <summary>Total scale currently applied (also used for HUD bars drawn outside ImGui).</summary>
    public float Scale => _scale;

    public bool MenuOpen = true; // open on launch so you pick a loadout first
    public bool ShowScoreboard;

    /// <summary>Smoke test: holds the scoreboard open for its screenshot (Program.Scoreboard.cs).</summary>
    public bool SmokeScoreboard;

    private AttachmentDef? _hoverAttachment;
    private string _lastNotice = "";
    private double _noticeTime;

    private static readonly float4 Orange = new(0.95f, 0.55f, 0.18f, 1f);
    private static readonly float4 Dim = new(0.65f, 0.65f, 0.7f, 1f);
    private static readonly float4 Red = new(1f, 0.35f, 0.35f, 1f);
    private static readonly float4 Green = new(0.4f, 1f, 0.5f, 1f);

    private const ImGuiWindowFlags Overlay =
        ImGuiWindowFlags.NoDecoration
        | ImGuiWindowFlags.NoInputs
        | ImGuiWindowFlags.NoBackground
        | ImGuiWindowFlags.AlwaysAutoResize
        | ImGuiWindowFlags.NoSavedSettings
        | ImGuiWindowFlags.NoFocusOnAppearing
        | ImGuiWindowFlags.NoNav
        | ImGuiWindowFlags.NoMove;

    public unsafe void Init(GlfwWindow window)
    {
        _window = window;
        ImGui.CreateContext();
        var io = ImGui.GetIO();
        io.IniFilename = default; // don't write imgui.ini next to the game

        ImGui.StyleColorsDark();
        _baseStyle = *ImGui.GetStyle().Ptr;
        _scale = 0; // forces the first ApplyScale

        _platform = new GlfwBackend(window, GlfwBackend.ClientApi.OpenGL);
        SafeClipboard.Install(); // replaces the backend's paste hook, which crashes on an image clipboard (SafeClipboard.cs)
        _renderer = new OpenGlBackend();
    }

    /// <summary>Automatic scale from the window height (1080p = 1.2x, 1440p = 1.6x) times the player's setting.</summary>
    private float TargetScale()
    {
        // The window's height in screen units: ImGui lays out in those. On Windows and Linux that's the framebuffer
        // height; a Mac Retina framebuffer is twice the window, and ImGui's DisplayFramebufferScale already makes it sharp.
        var h = _window == null ? 900 : OperatingSystem.IsMacOS() ? _window.Size.Y : _window.FramebufferSize.Y;
        var auto = Math.Clamp(h / 900f, 0.8f, 3f);
        return Math.Clamp(auto * UserScale, 0.6f, 5f);
    }

    private unsafe void ApplyScale(float scale)
    {
        var style = ImGui.GetStyle();
        *style.Ptr = _baseStyle;
        style.ScaleAllSizes(scale);
        style.FontScaleMain = scale;
        _scale = scale;
    }

    public bool WantsMouse => ImGui.GetIO().WantCaptureMouse;

    public bool WantsTextInput => ImGui.GetIO().WantTextInput;

    public void BeginFrame()
    {
        var target = TargetScale();
        if (MathF.Abs(target - _scale) > 0.01f)
            ApplyScale(target); // between frames, so ImGui never sees a half-changed style
        _platform!.NewFrame();
        _renderer!.NewFrame();
        ImGui.NewFrame();
        _loadoutDrawn = false;
        Thumbs.Clear(); // re-added every frame by whatever draws them (loadout cards, the kill card)
    }

    public void EndFrame()
    {
        if (!_loadoutDrawn)
            Preview = null; // the loadout screen closed: no gun preview to draw
        ImGui.Render();
        _renderer!.RenderDrawData(ImGui.GetDrawData());
        ImString.SharedStorage.Reset(); // strings built this frame are no longer needed
    }

    /// <summary>The local player's progression (set by Program).</summary>
    public Progress Progress = new();
    private ThrowableDef? _hoverThrowable;

    /// <summary>Opens the loadout screen in a match (on the overview of the loadout you're using).</summary>
    public void OpenMenu(Game game)
    {
        MenuOpen = true;
        ResetLoadoutScreen();
    }

    // =====================================================================
    // HUD
    // =====================================================================

    /// <summary>
    /// The HUD safe area in ImGui coordinates: centred on the screen, full height, and no wider than
    /// the HUD width setting allows (see <see cref="HudLayout.Span"/>). Everything in the in-match HUD
    /// that anchors to a screen edge anchors to this instead, so it stays readable on ultra-wide monitors.
    /// </summary>
    public static (float2 Pos, float2 Size) HudArea(HudWidth mode)
    {
        var vp = ImGui.GetMainViewport();
        var (left, width) = HudLayout.Span(vp.WorkSize.X, vp.WorkSize.Y, mode);
        return (new float2(vp.WorkPos.X + left, vp.WorkPos.Y), new float2(width, vp.WorkSize.Y));
    }

    public void DrawHud(Game game, double now, string joinInfo, Settings settings)
    {
        var showNetStats = settings.ShowNetStats;
        string K(GameAction a) => Keybinds.Name(settings.Bind(a));
        var (pos, size) = HudArea(settings.HudWidth);
        var pad = 24 * _scale;
        var local = game.Local;

        // ---- Top-left: session info -------------------------------------------------
        ImGui.SetNextWindowPos(new float2(pos.X + pad, pos.Y + pad));
        if (ImGui.Begin("##info"u8, Overlay))
        {
            ImGui.TextColored(Orange, "CALL OF SLOP"u8);
            ImGuiSafe.TextColored(Dim, $"Map: {World.Map.Name}");
            if (game.Net.IsOffline)
                ImGuiSafe.TextColored(Dim, $"Single player - {game.Players.Count - 1} bots");
            else if (game.IsServer)
                ImGuiSafe.TextColored(Dim, $"Hosting - friends join: {joinInfo}");
            else if (showNetStats)
            {
                ImGuiSafe.TextColored(Dim, $"Ping {game.Net.PingTo(game.Net.ServerGuid)} ms   interp {game.InterpDelayTicks * 1000 / World.TickRate:0} ms   correction {game.LastCorrection * 100:0} cm");
                if (game.Net.Kind == "steam" && game.ConnectionStats(game.LocalId, out var st)) // Steam sockets: the relay's view (GameNetStats.cs)
                    ImGuiSafe.TextColored(Dim, $"Steam {(st.Relayed ? "relay" : "direct")}   quality {st.QualityLocal * 100:0}% / {st.QualityRemote * 100:0}%   out {st.OutBytesPerSec / 1024:0.0} KB/s   in {st.InBytesPerSec / 1024:0.0} KB/s   queued {st.PendingReliable + st.PendingUnreliable} B");
            }
            ImGui.PushTextWrapPos(MathF.Max(size.X * 0.32f, 300 * _scale)); // keep the key help clear of the match bar
            ImGuiSafe.TextColored(Dim, $"Esc: menu   {K(GameAction.Loadout)}: loadout   {K(GameAction.Scoreboard)}: scores   {K(GameAction.SwapWeapon)}/wheel: switch   {K(GameAction.Aim)}: aim   {K(GameAction.Melee)}: melee   {K(GameAction.CrouchHold)}: crouch (tap) / prone (hold)   {K(GameAction.ChatAll)}/{K(GameAction.ChatTeam)}: chat");
            ImGui.PopTextWrapPos();
        }
        ImGui.End();

        // ---- Top-right: kill feed -----------------------------------------------------
        ImGui.SetNextWindowPos(new float2(pos.X + size.X - pad, pos.Y + pad), ImGuiCond.Always, new float2(1, 0));
        if (ImGui.Begin("##feed"u8, Overlay))
        {
            foreach (var f in game.Feed)
            {
                var fade = (float)Math.Clamp(8 - (now - f.Time), 0, 1); // lines leave after 8 s: fade over the last one
                ImGuiSafe.TextColored(f.Colour != default ? new float4(f.Colour.X, f.Colour.Y, f.Colour.Z, fade) // a coloured line (a slot machine win in its rarity colour)
                    : f.Mine ? new float4(Orange.X, Orange.Y, Orange.Z, fade) : new float4(1, 1, 1, 0.9f * fade), f.Text);
            }
        }
        ImGui.End();

        _hudSettings = settings;
        DrawMatchBar(game, pos, size);
        DrawModeHud(game, now, pos, size, settings); // warmup panel, hill / flag / gun indicators (UiModes.cs)
        if (game.Phase == MatchPhase.Playing)
            DrawZombiesHud(game, now, pos, size, settings); // round, points, prompts, downs (UiZombies.cs)
        DrawCoopBanner(game, now, pos, size); // co-op: OBJECTIVE 2/4, SQUAD WIPED (UiCoop.cs)
        if (game.Phase == MatchPhase.Ended)
        {
            DrawResults(game, pos, size);
            return;
        }

        if (local == null)
        {
            CenterText(size, pos, game.Status, 0.5f);
            CenterText(size, pos, "Press Esc to cancel", 0.56f);
            return;
        }

        // ---- Bottom-right: weapon + ammo ---------------------------------------------------
        var w = game.Active;
        ImGui.SetNextWindowPos(new float2(pos.X + size.X - pad, pos.Y + size.Y - pad - 40 * _scale), ImGuiCond.Always, new float2(1, 1));
        if (ImGui.Begin("##ammo"u8, Overlay))
        {
            ImGui.PushFont(default, 28);
            var reload = game.ReloadProgress(now);
            if (reload >= 0)
                ImGuiSafe.TextColored(Orange, $"RELOADING {reload * 100:0}%");
            else
                ImGuiSafe.TextColored(Game.IsEmptyGun(w.Def) ? Dim : w.Ammo == 0 ? Red : new float4(1, 1, 1, 1), Game.IsEmptyGun(w.Def) ? "-" : game.InfiniteAmmoOn(now) ? $"{w.Ammo} | INF" : w.Reserve >= 0 ? $"{w.Ammo} | {w.Reserve}" : $"{w.Ammo} / {w.Def.Mag}"); // Zombies: magazine | reserve (an empty slot: nothing; Infinite Ammo: INF)
            ImGui.PopFont();
            if (reload < 0 && w.Def.FireGroups is { } fireGroups && w.Groups is { } groups && FireGroupState.FeedsOf(fireGroups) > 1)
                ImGuiSafe.TextColored(Dim, FeedLine(fireGroups, groups, now)); // each receiver's own magazine (GameFireGroups.cs)
            ImGui.Text($"{w.Def.Name}");
            ImGuiSafe.TextColored(Dim, $"{w.Def.ClassName} - {w.Def.ModeName}  [{game.ActiveSlot + 1}]");
            for (var i = 0; i < game.WeaponSlots; i++) // 2 slots, or up to 4 with the Zombies slot upgrade
                if (i != game.ActiveSlot)
                    ImGuiSafe.TextColored(Dim, game.SlotHasGun(i) ? $"[{i + 1}] {game.Slots[i].Def.Name}  {game.Slots[i].Ammo}/{game.Slots[i].Def.Mag}" : $"[{i + 1}] (empty)");
            if (game.MannedCart != null) // manning a push cart's gun (GameZombies.cs)
                ImGuiSafe.TextColored(Orange, $"Leave: {K(GameAction.Interact)}   Push it: hold {K(GameAction.Interact)}");
            else if (game.InteractHint() is { } hint)
                ImGuiSafe.TextColored(Orange, $"[{K(GameAction.Interact)}] {hint}");
            if (game.LocalGrenade != ThrowableKind.None)
                ImGuiSafe.TextColored(game.GrenadesLeft > 0 ? new float4(1, 1, 1, 1) : Dim,
                    game.LocalGrenade == ThrowableKind.Built && game.LocalGrenadeBuild is { } gb // a built grenade (Zombies): its name, and the cook
                        ? (game.CookingSince >= 0 ? $"[{K(GameAction.Grenade)}] COOKING {gb.Name}: {MathF.Max(0, gb.FuseSeconds - (float)(Brutal.GlfwApi.Glfw.Time - game.CookingSince)):0.0} s" : $"[{K(GameAction.Grenade)}] {gb.Name} x{game.GrenadesLeft}")
                        : $"[{K(GameAction.Grenade)}] {Throwables.Get(game.LocalGrenade).Name} x{game.GrenadesLeft}");
            if (game.LocalEquipment != ThrowableKind.None)
                ImGuiSafe.TextColored(game.EquipmentLeft > 0 ? new float4(0.4f, 0.95f, 1f, 1) : Dim,
                    $"[{K(GameAction.Equipment)}] {Throwables.Get(game.LocalEquipment).Name} x{game.EquipmentLeft}");
        }
        ImGui.End();

        // ---- Bottom-left: health number ---------------------------------------------
        ImGui.SetNextWindowPos(new float2(pos.X + pad, pos.Y + size.Y - pad - 46 * _scale), ImGuiCond.Always, new float2(0, 1));
        if (ImGui.Begin("##hp"u8, Overlay))
        {
            ImGui.PushFont(default, 24);
            ImGuiSafe.TextColored(local.Health > World.HealthAt(35) ? new float4(1, 1, 1, 1) : Red, local.Alive ? $"{local.Health} HP" : "DEAD");
            ImGui.PopFont();
            ImGuiSafe.TextColored(Dim, $"Kills {local.Kills}   Deaths {local.Deaths}");
            ImGui.PushStyleColor(ImGuiCol.PlotHistogram, Orange);
            ImGui.ProgressBar(Progress.LevelFraction, new float2(220 * _scale, 6 * _scale), ""u8);
            ImGui.PopStyleColor();
            var prestigeTag = Progress.Prestige > 0 ? $"P{Progress.Prestige} " : ""; // prestige (Progression.cs)
            ImGuiSafe.TextColored(Dim, Progress.Level >= CallOfSlop.Progress.MaxLevel
                ? $"{prestigeTag}Level {Progress.Level} (max)"
                : $"{prestigeTag}Level {Progress.Level}  {Progress.XpIntoLevel}/{CallOfSlop.Progress.XpForNext(Progress.Level)} XP");
            if (local.Alive)
                ImGuiSafe.TextColored(local.Sim.Stance == Stance.Stand ? Dim : Orange, local.Sim.Stance switch
                {
                    Stance.Crouch => "CROUCHED",
                    Stance.Prone => "PRONE",
                    _ => "STANDING",
                });
            // Perks: lit up while their situation is happening.
            if (local.Alive)
            {
                foreach (var cat in new[] { PerkCategory.Movement, PerkCategory.Survival, PerkCategory.Damage })
                {
                    var id = local.Perks[cat];
                    if (id == PerkId.None)
                        continue;
                    var c = Perks.Color(cat);
                    var active = PerkActive(local, id);
                    ImGuiSafe.TextColored(active ? new float4(c.X, c.Y, c.Z, 1f) : new float4(c.X, c.Y, c.Z, 0.45f),
                        active ? $"{Perks.Get(id).Name}  ACTIVE" : Perks.Get(id).Name);
                }
            }
        }
        ImGui.End();

        if (now - game.PerkBannerTime < 3 && game.PerkBanner.Length > 0)
        {
            ImGui.SetNextWindowPos(new float2(pos.X + size.X / 2, pos.Y + size.Y * 0.3f), ImGuiCond.Always, new float2(0.5f, 0.5f));
            if (ImGui.Begin("##perkbanner"u8, Overlay))
            {
                ImGui.PushFont(default, 24);
                ImGuiSafe.TextColored(new float4(0.4f, 0.95f, 0.5f, (float)Math.Clamp(3 - (now - game.PerkBannerTime), 0, 1)), game.PerkBanner);
                ImGui.PopFont();
            }
            ImGui.End();
        }

        DrawStreakHud(game, now, pos, size, settings); // kill streaks (UiStreaks.cs)
        DrawFrenzyHud(game, now, pos, size); // frenzy edge tint, class screen effect and meter (UiFrenzy.cs)
        DrawOwnStreakEntities(game, now, pos, size); // your turret / drone's health (UiKillNotice.cs)
        DrawKillNotices(game, now, pos, size); // "KILLED Ash" under the crosshair (UiKillNotice.cs)

        // ---- XP: popups by the crosshair, level-up banner, bar under the health ------------
        var prog = Progress;
        ImGui.SetNextWindowPos(new float2(pos.X + size.X / 2 + 60 * _scale, pos.Y + size.Y / 2 - 20 * _scale), ImGuiCond.Always, new float2(0, 0.5f));
        if (ImGui.Begin("##xp"u8, Overlay))
        {
            foreach (var x in prog.Popups)
            {
                var age = now - x.Time;
                if (age > 2.2)
                    continue;
                var alpha = (float)Math.Clamp(2.2 - age, 0, 1);
                ImGuiSafe.TextColored(new float4(1f, 0.85f, 0.3f, alpha), $"+{x.Amount} {x.Text}");
            }
        }
        ImGui.End();
        if (now - prog.LevelUpTime < 5)
        {
            ImGui.SetNextWindowPos(new float2(pos.X + size.X / 2, pos.Y + size.Y * 0.18f), ImGuiCond.Always, new float2(0.5f, 0.5f));
            if (ImGui.Begin("##levelup"u8, Overlay))
            {
                ImGui.PushFont(default, 26);
                ImGuiSafe.TextColored(Orange, prog.LevelUpBanner);
                ImGui.PopFont();
            }
            ImGui.End();
        }

        if (now - prog.AttachmentBannerTime < 4)
        {
            ImGui.SetNextWindowPos(new float2(pos.X + size.X / 2, pos.Y + size.Y * 0.24f), ImGuiCond.Always, new float2(0.5f, 0.5f));
            if (ImGui.Begin("##attachunlock"u8, Overlay))
                ImGuiSafe.TextColored(new float4(0.4f, 0.95f, 1f, 1f), $"NEW ATTACHMENT  -  {prog.AttachmentBanner}");
            ImGui.End();
        }

        // ---- Centre messages --------------------------------------------------------
        if (!local.Alive && !MenuOpen && game.Module.ShowsRespawnMessage) // (Zombies says it itself: back next round)
        {
            var wait = game.RespawnWait(now);
            CenterText(size, pos, game.RespawnRequested || game.SkipsLoadout
                ? wait > 0 ? $"Respawning in {wait:0.0} s..." : "Respawning..."
                : $"Press {K(GameAction.Loadout)} to pick your loadout and respawn", 0.38f);
        }

        if (game.LoadoutNotice != _lastNotice)
        {
            _lastNotice = game.LoadoutNotice;
            _noticeTime = now;
        }
        if (_lastNotice.Length > 0 && now - _noticeTime < 4 && !MenuOpen)
            CenterText(size, pos, _lastNotice.Replace("[E]", K(GameAction.Interact)), 0.75f); // "[E]": the interact key (as in Zombies prompts)

        if (ShowScoreboard || SmokeScoreboard)
        {
            DrawScoreboard(game, size, pos);
            if (game.WeaponsTweaked)
                DrawTweakedBadge(size, pos); // the host's weapon tweak window changed stats (UiWeaponTweaks.cs)
        }
    }

    /// <summary>Top-centre: clock, your score vs the best other score, and the win condition.</summary>
    private void DrawMatchBar(Game game, float2 pos, float2 size)
    {
        ImGui.SetNextWindowPos(new float2(pos.X + size.X / 2, pos.Y + 12 * _scale), ImGuiCond.Always, new float2(0.5f, 0));
        ImGui.SetNextWindowBgAlpha(0.45f);
        if (ImGui.Begin("##match"u8, ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.AlwaysAutoResize
                                    | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav))
        {
            var t = Math.Max(0, (int)Math.Ceiling(game.TimeLeft));
            var clock = $"{t / 60}:{t % 60:00}";
            if (game.Phase == MatchPhase.Ended)
            {
                ImGuiSafe.TextColored(Dim, $"Next match in {clock}");
            }
            else if (game.Phase == MatchPhase.Warmup)
            {
                DrawWarmupBar(game, _hudSettings);
            }
            else if (game.Phase == MatchPhase.Countdown)
            {
                var text = t > 0 ? $"MATCH STARTS IN {t}" : "GO!";
                ImGui.PushFont(default, 40);
                ImGuiSafe.TextColored(Orange, text);
                ImGui.PopFont();
                ImGui.TextColored(Dim, MenuOpen ? "Pick your loadout - it deploys automatically at GO."u8 : "Get ready..."u8);
                ImGuiSafe.TextColored(Dim, game.ModeGoalText);
            }
            else if (DrawModeScoreBar(game, clock, t))
            {
            }
            else
            {
                var me = game.Local;
                var mine = me?.Kills ?? 0;
                var bestOther = game.Players.Values.Where(p => p.Id != game.LocalId).Select(p => (int)p.Kills).DefaultIfEmpty(0).Max();
                var leaderOther = game.Players.Values.Where(p => p.Id != game.LocalId).OrderByDescending(p => p.Kills).FirstOrDefault();

                ImGui.PushFont(default, 24);
                ImGuiSafe.TextColored(t <= 60 ? Red : new float4(1, 1, 1, 1), clock);
                ImGui.SameLine(0, 24 * _scale);
                ImGuiSafe.TextColored(mine >= bestOther ? Green : new float4(1, 1, 1, 1), $"YOU {mine}");
                ImGui.SameLine();
                ImGui.TextColored(Dim, "-"u8);
                ImGui.SameLine();
                ImGuiSafe.TextColored(bestOther > mine ? Red : new float4(1, 1, 1, 1), $"{bestOther} {(leaderOther != null ? game.NameOf(leaderOther.Id) : "")}");
                ImGui.PopFont();

                var top = Math.Max(mine, bestOther);
                var limit = game.ScoreLimit; // scales with the player count (ScoreLimits.cs)
                var deuce = top >= limit - 1 && Math.Abs(mine - bestOther) < Game.WinMargin;
                ImGuiSafe.TextColored(deuce ? Orange : Dim, deuce
                    ? $"Win by {Game.WinMargin}!  ({Math.Max(limit, top + 1)}+ kills and {Game.WinMargin} clear)"
                    : $"First to {limit} kills, win by {Game.WinMargin}");
            }
        }
        ImGui.End();
    }

    /// <summary>End-of-match screen: who won, final scores, countdown to the next match.</summary>
    private void DrawResults(Game game, float2 pos, float2 size)
    {
        ImGui.SetNextWindowPos(new float2(pos.X + size.X / 2, pos.Y + size.Y * 0.42f), ImGuiCond.Always, new float2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new float2(MathF.Min(640 * _scale, size.X - 16 * _scale), 0));
        ImGui.SetNextWindowBgAlpha(0.9f);
        if (ImGui.Begin("##results"u8, ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoMove))
        {
            var won = game.LocalWon;
            var draw = game.Winner == Game.NoWinner;
            ImGui.PushFont(default, 48);
            if (game.Module.CoOp)
                ImGuiSafe.TextColored(Red, "GAME OVER"u8); // (Zombies: the run ended)
            else
                ImGuiSafe.TextColored(won ? Green : draw ? Orange : Red, won ? "VICTORY"u8 : draw ? "DRAW"u8 : "DEFEAT"u8);
            ImGui.PopFont();
            ImGui.Text(game.MatchResult.Length > 0 ? game.MatchResult : draw ? "No winner" : $"{game.NameOf(game.Winner)} wins");
            DrawModeResultsLine(game);
            ImGui.Spacing();
            if (ImGui.BeginTable("##final"u8, 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV))
            {
                ImGui.TableSetupColumn("#"u8, ImGuiTableColumnFlags.WidthFixed, 30 * _scale);
                ImGui.TableSetupColumn("Player"u8, ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Kills"u8, ImGuiTableColumnFlags.WidthFixed, 70 * _scale);
                ImGui.TableSetupColumn("Deaths"u8, ImGuiTableColumnFlags.WidthFixed, 70 * _scale);
                ImGui.TableHeadersRow();
                var rank = 1;
                foreach (var p in game.Players.Values.OrderBy(q => game.IsTeamMode ? q.Team : 0).ThenByDescending(game.Module.RankScore).ThenByDescending(q => q.Kills).ThenBy(q => q.Deaths))
                {
                    var c = game.SideColor(p); // Zombies: everyone in the friendly colour
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.Text($"{rank++}");
                    ImGui.TableNextColumn();
                    ImGuiSafe.TextColored(new float4(c.X, c.Y, c.Z, 1), (p.Id == game.LocalId ? $"{game.NameOf(p.Id)} (you)" : game.NameOf(p.Id)) + game.RankTag(p) + game.VoiceTag(p, Brutal.GlfwApi.Glfw.Time)); // talking / muted (GameVoice.cs)
                    ImGui.TableNextColumn();
                    ImGui.Text($"{p.Kills}");
                    ImGui.TableNextColumn();
                    ImGui.Text($"{p.Deaths}");
                }
                ImGui.EndTable();
            }
            ImGui.Spacing();
            if (game.VoteOptions.Length > 0)
            {
                ImGui.SeparatorText("VOTE FOR THE NEXT MAP"u8);
                var n = game.VoteOptions.Length;
                var gap = ImGui.GetStyle().ItemSpacing.X;
                var bw = (ImGui.GetContentRegionAvail().X - gap * (n - 1)) / n;
                var lead = game.VoteTally.Length > 0 ? game.VoteTally.Max() : (byte)0;
                for (var i = 0; i < n; i++)
                {
                    if (i > 0)
                        ImGui.SameLine();
                    var m = Maps.Find(game.VoteNames[i]);
                    var votes = i < game.VoteTally.Length ? game.VoteTally[i] : 0;
                    var mine = game.MyVote == i;
                    if (mine)
                        ImGui.PushStyleColor(ImGuiCol.Button, new float4(0.85f, 0.45f, 0.12f, 1f));
                    var about = m != null ? $"{m.Size} - {m.SizeLabel}" : "(you don't have this map)";
                    var label = $"[{i + 1}] {game.VoteNames[i]}\n{about}\n{votes} vote{(votes == 1 ? "" : "s")}{(votes > 0 && votes == lead ? "  (leading)" : "")}###vote{i}";
                    if (ImGui.Button(label, new float2(bw, 78 * _scale)))
                        game.CastVote(i);
                    if (mine)
                        ImGui.PopStyleColor();
                }
                ImGuiSafe.TextColored(Dim, $"Click a map or press 1-{n}. Voting closes in {Math.Ceiling(game.TimeLeft):0} s; ties are broken at random. (Bots don't vote.)");
            }
            else
            {
                ImGuiSafe.TextColored(Dim, $"Next match starts in {Math.Ceiling(game.TimeLeft):0} s on {World.Map.Name}.  Esc for the menu.");
            }
        }
        ImGui.End();
    }

    private static void CenterText(float2 size, float2 pos, string text, float yFrac)
    {
        ImGui.SetNextWindowPos(new float2(pos.X + size.X / 2, pos.Y + size.Y * yFrac), ImGuiCond.Always, new float2(0.5f, 0.5f));
        if (ImGui.Begin("##center" + yFrac, Overlay))
            ImGui.Text(text);
        ImGui.End();
    }

    private void DrawScoreboard(Game game, float2 size, float2 pos)
    {
        if (game.Players.Count > BigScoreboardFrom)
        {
            DrawBigScoreboard(game, size, pos); // two compact columns for big lobbies (UiScoreboard.cs)
            return;
        }
        if (DrawModeScoreboard(game, size, pos))
            return; // team modes and Gun Game (UiModes.cs)
        ImGui.SetNextWindowPos(new float2(pos.X + size.X / 2, pos.Y + size.Y * 0.3f), ImGuiCond.Always, new float2(0.5f, 0));
        ImGui.SetNextWindowSize(new float2(MathF.Min(ScoreboardWidth * _scale, size.X - 16 * _scale), 0));
        if (ImGui.Begin("Scoreboard"u8, ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.TextColored(Orange, "SCOREBOARD"u8);
            var heads = ScoreboardModel.Headers(game.Module, false); // Player, Level, Kills, Deaths, Weapon, Status (no HP)
            if (ImGui.BeginTable("##scores"u8, heads.Length, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV))
            {
                SetupScoreColumns(heads, false);
                ScoreboardModel.Rank(game, _scoreRanked);
                foreach (var p in _scoreRanked)
                {
                    var c = game.SideColor(p); // Zombies: everyone in the friendly colour
                    var col = new float4(c.X, c.Y, c.Z, 1);
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGuiSafe.TextColored(col, (p.Id == game.LocalId ? $"{game.NameOf(p.Id)} (you)" : game.NameOf(p.Id)) + game.RankTag(p) + game.VoiceTag(p, Brutal.GlfwApi.Glfw.Time)); // talking / muted (GameVoice.cs)
                    ImGuiSafe.TextColored(col, $"{game.NameOf(p.Id)}{(p.Id == game.LocalId ? " (you)" : "")}");
                    ImGui.TableNextColumn();
                    DrawLevelCell(game, p);
                    var (cell1, cell2) = game.Module.ScoreboardCells(game, p);
                    ImGui.TableNextColumn();
                    ImGui.Text(cell1);
                    ImGui.TableNextColumn();
                    ImGui.Text(cell2);
                    ImGui.TableNextColumn();
                    ImGui.Text(p.WeaponDef.Name);
                    ImGui.TableNextColumn();
                    ImGuiSafe.TextColored(p.Alive ? Green : Red, ScoreboardModel.Status(p, false));
                }
                ImGui.EndTable();
            }
        }
        ImGui.End();
    }

    // =====================================================================
    // Loadout menu
    // =====================================================================

    // ---- Gunsmith ---------------------------------------------------------------------

    private void DrawGunsmithList(Audio audio, WeaponDef w)
    {
        var prog = Progress;
        var wxp = prog.WeaponXp(w.Id);
        var maxXp = Attachments.MaxXp(w);
        var kit = KitOf(w);
        var options = Attachments.For(w);
        ImGuiSafe.TextColored(Orange, w.Name);
        ImGuiSafe.TextColored(Dim, options.Count == 0
            ? "This weapon takes no attachments."
            : $"Weapon XP {Math.Min(wxp, maxXp):N0} / {maxXp:N0}  -  {options.Count(a => wxp >= Attachments.UnlockXp(w, a))} / {options.Count} attachments unlocked");
        if (options.Count > 0)
        {
            ImGui.PushStyleColor(ImGuiCol.PlotHistogram, new float4(0.4f, 0.95f, 1f, 1f));
            ImGui.ProgressBar(Math.Clamp(wxp / (float)maxXp, 0f, 1f), new float2(-1, 6 * _scale), ""u8);
            ImGui.PopStyleColor();
            ImGui.TextColored(Dim, "Earn weapon XP with kills using this gun (+100, headshot +50), plus +150 per match as your primary (+75 as your secondary)."u8);
        }
        AttachmentDef? hovered = null;
        for (var si = 0; si < Attachments.SlotCount; si++)
        {
            var slot = (AttachSlot)si;
            var inSlot = options.Where(a => a.Slot == slot).ToArray();
            if (inSlot.Length == 0)
                continue;
            ImGui.SeparatorText(Attachments.SlotName(slot));
            var current = Attachments.Slot(kit, slot);
            // On a gun that starts scoped (snipers) the empty optic slot is its iron sights, an unlock of their own.
            var ironsNeed = slot == AttachSlot.Optic ? Attachments.IronSightsXp(w) : 0;
            var noneName = slot == AttachSlot.Optic ? Attachments.NoOpticName(w) : "None";
            if (wxp < ironsNeed)
                ImGui.PushStyleColor(ImGuiCol.Text, Dim);
            if (ImGui.Selectable(wxp >= ironsNeed ? $"{noneName}##none{si}" : $"[LOCKED] {noneName}  -  {ironsNeed:N0} weapon XP##none{si}", current == 0) && wxp >= ironsNeed)
                SetKitOf(audio, w, Attachments.With(kit, slot, 0));
            if (wxp < ironsNeed)
                ImGui.PopStyleColor();
            foreach (var a in inSlot)
            {
                var need = Attachments.UnlockXp(w, a);
                var unlocked = wxp >= need;
                var label = unlocked ? $"{a.Name}##a{si}_{a.Index}" : $"[LOCKED] {a.Name}  -  {need:N0} weapon XP##a{si}_{a.Index}";
                if (!unlocked)
                    ImGui.PushStyleColor(ImGuiCol.Text, Dim);
                if (ImGui.Selectable(label, current == a.Index) && unlocked)
                {
                    kit = Attachments.With(kit, slot, a.Index);
                    SetKitOf(audio, w, kit);
                    audio.Effect(Sfx.HitTick, 0.4f);
                }
                if (!unlocked)
                    ImGui.PopStyleColor();
                if (ImGui.IsItemHovered())
                    hovered = a;
            }
        }
        _hoverAttachment = hovered;
    }

    private void DrawGunsmithDetails(Audio audio, WeaponDef w)
    {
        var prog = Progress;
        var kit = KitOf(w);
        if (_hoverAttachment is { } a)
        {
            ImGui.PushFont(default, 22);
            ImGuiSafe.TextColored(new float4(0.4f, 0.95f, 1f, 1f), a.Name);
            ImGui.PopFont();
            ImGuiSafe.TextColored(Dim, Attachments.SlotName(a.Slot));
            ImGuiSafe.TextWrapped(a.Blurb);
            foreach (var pro in a.Pros)
                ImGuiSafe.TextColored(Green, $"+ {pro}");
            foreach (var con in a.Cons)
                ImGuiSafe.TextColored(Red, $"- {con}");
            var need = Attachments.UnlockXp(w, a);
            if (prog.WeaponXp(w.Id) < need)
                ImGuiSafe.TextColored(Red, $"Locked - needs {need:N0} weapon XP on the {w.Name} ({prog.WeaponXp(w.Id):N0} so far, about {MathF.Ceiling((need - prog.WeaponXp(w.Id)) / 1000f)} more match(es)).");
            ImGui.Separator();
            ImGui.TextColored(Dim, "With this attachment:"u8);
            DrawStats(audio, Attachments.Apply(w, Attachments.With(kit, a.Slot, a.Index)));
        }
        else
        {
            ImGuiSafe.TextColored(Dim, $"Equipped: {Attachments.Describe(kit)}");
            ImGui.Separator();
            DrawStats(audio, Attachments.Apply(w, kit));
        }
    }

    private void DrawThrowable(ThrowableDef t)
    {
        ImGui.PushFont(default, 22);
        ImGuiSafe.TextColored(Orange, t.Name);
        ImGui.PopFont();
        if (t.Kind == ThrowableKind.None)
        {
            ImGui.TextWrapped("Leave this slot empty."u8);
            return;
        }
        ImGuiSafe.TextColored(Dim, t.IsEquipment ? "Equipment" : "Grenade");
        ImGuiSafe.TextWrapped(t.Blurb);
        ImGui.Spacing();
        ImGui.Text($"Per life: {t.PerLife}");
        if (t.MaxDamage > 0)
        {
            ImGui.Text($"Damage: up to {t.MaxDamage:0} (falls off to 0 at {t.Radius:0} m; walls block it)");
            ImGui.Text($"Fuse: {(t.Kind == ThrowableKind.Impact ? "on impact" : $"{t.Fuse:0.0} s")}");
        }
        else if (TacticalGear.Is(t.Kind))
        {
            foreach (var line in TacticalGear.StatLines(t.Kind)) // the tactical items' numbers (TacticalGear.cs)
                ImGuiSafe.TextWrapped(line);
        }
        else
        {
            ImGui.Text($"Radius: {t.Radius:0} m");
        }
        if (!Progress.IsUnlocked(t.Kind))
            ImGuiSafe.TextColored(Red, $"Locked - reach level {CallOfSlop.Progress.UnlockLevel(t.Kind)}.");
        ImGui.Spacing();
        ImGuiSafe.TextColored(Dim, t.IsEquipment ? "Use it with your Use equipment key (E by default)." : "Throw it with your Throw grenade key (G by default).");
    }

    private void DrawStats(Audio audio, WeaponDef w)
    {
        ImGui.PushFont(default, 22);
        ImGuiSafe.TextColored(Orange, w.Name);
        ImGui.PopFont();
        ImGuiSafe.TextColored(Dim, $"{w.ClassName} - {w.ModeName}{(w.Pellets > 1 ? $" - {w.Pellets} pellets" : "")}");
        ImGuiSafe.TextWrapped(w.Blurb);
        ImGui.Spacing();

        Bar("Damage", (w.Damage * w.Pellets) / 140f, w.Pellets > 1 ? $"{w.Damage} x {w.Pellets}" : $"{w.Damage}");
        Bar("Fire rate", w.Rpm / 1500f, $"{w.Rpm} rpm");
        Bar("Damage / sec", w.Dps / 1100f, $"{w.Dps:0}");
        Bar("Range", StatBars.Range(w), $"{w.RangeM:0} m full damage");
        Bar("Accuracy", StatBars.Accuracy(w), $"{w.HipSpreadDeg:0.0#} deg hip, {w.HipSpreadDeg * w.AdsSpreadMult:0.00} aimed");
        Bar("Control", StatBars.Control(w), $"{Recoil.ControlText(w)} deg kick");
        Bar("Mobility", (w.Mobility - 0.7f) / 0.45f, $"{w.Mobility * 100:0}% speed");
        Bar("Magazine", StatBars.Magazine(w), $"{w.Mag} rounds");
        Bar("Reload speed", StatBars.Reload(w), $"{w.Reload:0.0} s");
        Bar("Handling", 1f - Math.Clamp(w.EquipTime / 1.2f, 0f, 1f), $"{w.EquipTime:0.00} s draw");
        Bar("Aim speed", 1f - Math.Clamp(w.AdsTime / 0.45f, 0f, 1f), $"{w.AdsTime:0.00} s to aim, {w.AdsZoom:0.0}x zoom");
        if (w.Kit != 0)
            ImGuiSafe.TextColored(new float4(0.4f, 0.95f, 1f, 1f), $"Attachments: {Attachments.Describe(w.Kit)}");

        ImGui.Spacing();
        ImGui.SeparatorText("Shots to kill"u8);
        ImGui.Text($"Body {w.ShotsToKill(false)}   Head {w.ShotsToKill(true)}   (x{w.HeadMult:0.0} headshot)");
        ImGuiSafe.TextColored(Dim, $"Damage per shot at 10 m: {w.Damage * w.Pellets * w.DamageMultAt(10):0}   25 m: {w.Damage * w.Pellets * w.DamageMultAt(25):0}   45 m: {w.Damage * w.Pellets * w.DamageMultAt(45):0}");

        ImGui.Spacing();
        if (ImGui.Button("Test fire"u8))
            audio.Shot(w, null);
        ImGui.SameLine();
        if (ImGui.Button("Test reload"u8))
            audio.Reload(w, null);
        ImGui.SameLine();
        if (ImGui.Button("Test equip"u8))
            audio.Equip(w);
    }

    private void Bar(string label, float fraction, string value)
    {
        ImGui.Text(label);
        ImGui.SameLine(130 * _scale);
        ImGui.PushStyleColor(ImGuiCol.PlotHistogram, Orange);
        ImGui.ProgressBar(Math.Clamp(fraction, 0.02f, 1f), new float2(-1, 0), value);
        ImGui.PopStyleColor();
    }

    public void Dispose()
    {
        _renderer?.Dispose();
        _platform?.Dispose();
    }
}
