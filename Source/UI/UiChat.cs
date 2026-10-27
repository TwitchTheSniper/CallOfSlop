using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// In-match text chat on the HUD (the same chat as the lobby room's, LobbyChat.cs), the voice chat's indicators (your mic
/// while you transmit, speaker icons on name tags), Settings > Audio's voice chat section, and the pause menu's PLAYERS page
/// (mute someone's voice or chat). The chat box sits bottom-left above the health panel: the last lines fade after
/// <see cref="ChatRules.ShowSeconds"/>; while typing the whole history shows. Typing suspends the game controls (Program).
/// </summary>
public sealed partial class Ui
{
    /// <summary>The chat line is open for typing (game controls are suspended).</summary>
    public bool ChatOpen { get; private set; }

    /// <summary>The open chat line is team chat.</summary>
    public bool ChatTeam { get; private set; }

    /// <summary>"Test mic" in Settings > Audio: your voice played straight back to you.</summary>
    public bool MicTestOn;

    private bool _chatClosed, _chatFocused;
    private int _chatFocusWait;
    private ImInputString? _hudChatInput;
    private string _hudChatNotice = "";
    private double _hudChatNoticeAt = -10;
    private int _hudChatSeen = -1, _hudChatTextVersion = -1;
    private readonly List<string> _hudChatText = new();
    private double _micPanelAt = -10;
    private string _voiceHint = "";
    private double _voiceHintAt = -10;
    private bool _pausePlayers; // the pause menu shows the PLAYERS page
    private string[] _micDevices = [];

    /// <summary>Most recent lines shown when not typing.</summary>
    private const int ChatHudLines = 8;

    private static readonly float4 SystemChat = new(0.85f, 0.8f, 0.55f, 1f);

    /// <summary>Opens the chat line (Program, on the chat keys). Focus goes to the box a frame later (so the key's own character isn't typed).</summary>
    public void OpenChat(bool team)
    {
        ChatOpen = true;
        ChatTeam = team;
        _chatFocusWait = 1;
        _chatFocused = false;
        _hudChatInput?.Clear();
    }

    /// <summary>Closes the chat line without sending.</summary>
    public void CloseChat()
    {
        if (!ChatOpen)
            return;
        ChatOpen = false;
        _chatClosed = true;
    }

    /// <summary>True once after the chat line closed (Program then waits for the chat keys to come up).</summary>
    public bool TakeChatClosed()
    {
        var c = _chatClosed;
        _chatClosed = false;
        return c;
    }

    /// <summary>A short line by the mic icon ("Voice chat is off on this server"...).</summary>
    public void VoiceHint(string text, double now)
    {
        _voiceHint = text;
        _voiceHintAt = now;
    }

    /// <summary>Settings > Audio's voice section was on screen just now (Program keeps the mic open for the level meter).</summary>
    public bool MicPanelOpen(double now) => now - _micPanelAt < 0.5;

    private float4 ChatColour(Game game, in ChatLine c, float alpha)
    {
        if ((c.Flags & ChatFlags.System) != 0)
            return new float4(SystemChat.X, SystemChat.Y, SystemChat.Z, alpha);
        var rgb = c.Team < 2 ? GameModes.TeamColor(c.Team)
            : game.Players.TryGetValue(c.From, out var p) ? game.SideColor(p)
            : new System.Numerics.Vector3(0.9f, 0.9f, 0.9f);
        return Rgba(rgb, alpha);
    }

    /// <summary>The HUD chat (in a match, warmup and the results screen), and your mic icon while you transmit.</summary>
    public void DrawChat(Game game, double now, Settings s)
    {
        var (pos, size) = HudArea(s.HudWidth);
        var pad = 24 * _scale;
        if (_hudChatTextVersion != game.ChatVersion)
        {
            _hudChatTextVersion = game.ChatVersion;
            _hudChatText.Clear();
            foreach (var c in game.Chat)
                _hudChatText.Add(ChatRules.Format(c));
        }
        var width = MathF.Min(560 * _scale, size.X * 0.42f);
        var bottom = new float2(pos.X + pad, pos.Y + size.Y - pad - 230 * _scale); // above the health panel
        var chat = game.Chat;
        var lines = _hudChatText;

        if (!ChatOpen)
        {
            ImGui.SetNextWindowPos(bottom, ImGuiCond.Always, new float2(0, 1));
            if (ImGui.Begin("##hudchat"u8, Overlay))
            {
                ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width);
                var first = Math.Max(0, Math.Min(lines.Count, chat.Count) - ChatHudLines);
                for (var i = first; i < lines.Count && i < chat.Count; i++)
                {
                    var age = now - chat[i].Time;
                    if (age > ChatRules.ShowSeconds || age < -1)
                        continue;
                    var alpha = (float)Math.Clamp(ChatRules.ShowSeconds - age, 0, 1);
                    ImGui.PushStyleColor(ImGuiCol.Text, ChatColour(game, chat[i], alpha));
                    ImGuiSafe.TextWrapped(lines[i]);
                    ImGui.PopStyleColor();
                }
                if (now - _hudChatNoticeAt < 4)
                    ImGuiSafe.TextColored(Orange, _hudChatNotice);
                ImGui.PopTextWrapPos();
            }
            ImGui.End();
        }
        else
            DrawChatInput(game, now, bottom, width);

        DrawMicIndicator(game, now, s, pos, size);
    }

    private void DrawChatInput(Game game, double now, float2 bottom, float width)
    {
        var height = 250 * _scale;
        ImGui.SetNextWindowPos(bottom, ImGuiCond.Always, new float2(0, 1));
        ImGui.SetNextWindowSize(new float2(width, height));
        ImGui.SetNextWindowBgAlpha(0.6f);
        const ImGuiWindowFlags flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoNav;
        if (ImGui.Begin("##hudchatbox"u8, flags))
        {
            var inputH = ImGui.GetFrameHeightWithSpacing() * 2;
            if (ImGui.BeginChild("##hudchathistory"u8, new float2(0, height - inputH - ImGui.GetStyle().WindowPadding.Y * 2)))
            {
                var chat = game.Chat;
                for (var i = 0; i < _hudChatText.Count && i < chat.Count; i++)
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, ChatColour(game, chat[i], 1f));
                    ImGuiSafe.TextWrapped(_hudChatText[i]);
                    ImGui.PopStyleColor();
                }
                if (_hudChatText.Count == 0)
                    ImGui.TextColored(Dim, "No messages yet."u8);
                if (_hudChatSeen != game.ChatVersion)
                {
                    _hudChatSeen = game.ChatVersion;
                    ImGui.SetScrollHereY(1f);
                }
            }
            ImGui.EndChild();

            if (ChatTeam)
                ImGui.TextColored(new float4(0.45f, 0.85f, 1f, 1f), "TEAM:"u8);
            else
                ImGui.TextColored(Orange, "ALL:"u8);
            ImGui.SameLine();
            _hudChatInput ??= new ImInputString(ChatRules.BoxCapacity);
            ImGui.SetNextItemWidth(-1);
            if (_chatFocusWait > 0)
                _chatFocusWait--;
            else if (!_chatFocused)
                ImGui.SetKeyboardFocusHere();
            var send = false;
            try
            {
                send = ImGui.InputTextWithHint("##hudchatinput"u8, ChatTeam ? "Message your team..."u8 : "Message everyone..."u8, _hudChatInput, ImGuiInputTextFlags.EnterReturnsTrue);
            }
            catch (Exception ex)
            {
                ImGuiSafe.FieldFailed("match chat", ex);
            }
            var active = ImGui.IsItemActive();
            ImGui.TextColored(Dim, "Enter: send    Esc: cancel"u8);
            if (send)
            {
                _hudChatInput.EvaluateLength(); // (the box only updates its length itself when Enter is pressed)
                var text = ChatRules.Clean(ImGuiSafe.Text(_hudChatInput));
                var why = text.Length > 0 ? game.SendChat(text, now, ChatTeam) : null;
                if (why is { Length: > 0 })
                {
                    _hudChatNotice = why;
                    _hudChatNoticeAt = now;
                }
                _hudChatInput.Clear();
                CloseChat();
            }
            else if (active)
                _chatFocused = true;
            else if (_chatFocused)
                CloseChat(); // the box lost focus (Esc inside it, or a click elsewhere)
        }
        ImGui.End();
    }

    // ---- Voice indicators ------------------------------------------------------------------------------------

    private void DrawMicIndicator(Game game, double now, Settings s, float2 pos, float2 size)
    {
        var talking = now - game.LocalVoiceAt < VoiceRules.TalkingHold;
        var hint = now - _voiceHintAt < 3 ? _voiceHint : null;
        if (!talking && hint == null)
            return;
        var at = new float2(pos.X + size.X / 2 - 120 * _scale, pos.Y + size.Y - 120 * _scale);
        var dl = ImGui.GetForegroundDrawList();
        var px = 26 * _scale;
        if (talking)
        {
            var radio = s.VoiceMode == VoiceMode.TeamRadio;
            var col = radio ? new System.Numerics.Vector3(0.45f, 0.85f, 1f) : new System.Numerics.Vector3(0.4f, 1f, 0.5f);
            dl.AddRectFilled(new float2(at.X - 6 * _scale, at.Y - 6 * _scale), new float2(at.X + px + 150 * _scale, at.Y + px + 6 * _scale), new ImColor8(0, 0, 0, 140), 6 * _scale);
            DrawMicIcon(dl, new float2(at.X + px / 2, at.Y + px / 2), px, Hud.Colour(col, 1f));
            Hud.ShadowText(dl, new float2(at.X + px + 8 * _scale, at.Y + px * 0.15f), 18 * _scale, radio ? "TEAM RADIO"u8 : "TALKING"u8, col, 1f);
        }
        else if (hint != null)
            Hud.ShadowText(dl, at, 17 * _scale, hint, new System.Numerics.Vector3(1f, 0.75f, 0.35f), 1f);
    }

    /// <summary>A microphone: a capsule, its cradle and a stand. <paramref name="size"/> is the icon's height.</summary>
    public static void DrawMicIcon(ImDrawListPtr dl, float2 centre, float size, ImColor8 col)
    {
        var s = size;
        float x = centre.X, y = centre.Y;
        dl.AddRectFilled(new float2(x - 0.18f * s, y - 0.5f * s), new float2(x + 0.18f * s, y + 0.12f * s), col, 0.18f * s);
        dl.PathArcTo(new float2(x, y - 0.05f * s), 0.32f * s, 0f, MathF.PI);
        dl.PathStroke(col, default, MathF.Max(1.5f, 0.08f * s));
        dl.AddLine(new float2(x, y + 0.27f * s), new float2(x, y + 0.46f * s), col, MathF.Max(1.5f, 0.08f * s));
        dl.AddLine(new float2(x - 0.2f * s, y + 0.48f * s), new float2(x + 0.2f * s, y + 0.48f * s), col, MathF.Max(1.5f, 0.08f * s));
    }

    /// <summary>A speaker with sound waves (someone talking). <paramref name="left"/> is the left middle; <paramref name="size"/> its height.</summary>
    public static void DrawSpeakerIcon(ImDrawListPtr dl, float2 left, float size, ImColor8 col)
    {
        var s = size;
        float x = left.X, y = left.Y;
        dl.AddRectFilled(new float2(x, y - 0.16f * s), new float2(x + 0.22f * s, y + 0.16f * s), col);
        dl.AddQuadFilled(new float2(x + 0.2f * s, y - 0.16f * s), new float2(x + 0.5f * s, y - 0.42f * s), new float2(x + 0.5f * s, y + 0.42f * s), new float2(x + 0.2f * s, y + 0.16f * s), col);
        var t = MathF.Max(1.2f, 0.07f * s);
        dl.PathArcTo(new float2(x + 0.5f * s, y), 0.2f * s, -0.8f, 0.8f);
        dl.PathStroke(col, default, t);
        dl.PathArcTo(new float2(x + 0.5f * s, y), 0.38f * s, -0.85f, 0.85f);
        dl.PathStroke(col, default, t);
    }

    // ---- Pause menu: PLAYERS ---------------------------------------------------------------------------------

    /// <summary>The pause menu's PLAYERS page: everyone (not bots), with Mute voice / Mute chat and who is talking.</summary>
    private void DrawPausePlayers(Game game, Settings s)
    {
        Title("Players - mute voice or chat");
        var now = Brutal.GlfwApi.Glfw.Time;
        var any = false;
        if (ImGui.BeginTable("##players"u8, 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV))
        {
            ImGui.TableSetupColumn("Player"u8, ImGuiTableColumnFlags.WidthFixed, 260 * _scale);
            ImGui.TableSetupColumn("Voice"u8, ImGuiTableColumnFlags.WidthFixed, 140 * _scale);
            ImGui.TableSetupColumn("Chat"u8, ImGuiTableColumnFlags.WidthFixed, 140 * _scale);
            ImGui.TableHeadersRow();
            foreach (var p in game.Players.Values)
            {
                if (p.Bot != null || p.Id == game.LocalId)
                    continue;
                any = true;
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                ImGuiSafe.TextColored(Rgba(game.SideColor(p)), game.NameOf(p.Id) + (game.IsTalking(p, now) ? "  <))" : ""));
                ImGui.TableNextColumn();
                var mv = game.MutedVoice.Contains(p.Name);
                if (ImGui.Checkbox($"Mute voice##mv{p.Id}", ref mv))
                    game.SetVoiceMuted(p, mv);
                ImGui.TableNextColumn();
                var mc = game.MutedChat.Contains(p.Name);
                if (ImGui.Checkbox($"Mute chat##mc{p.Id}", ref mc))
                {
                    if (mc)
                        game.MutedChat.Add(p.Name);
                    else
                        game.MutedChat.Remove(p.Name);
                }
            }
            ImGui.EndTable();
        }
        if (!any)
            ImGui.TextColored(Dim, "Nobody else here (bots don't talk)."u8);
        ImGui.Spacing();
        ImGui.Checkbox("Mute all voice"u8, ref s.VoiceMuteAll);
        if (!game.VoiceAllowed)
            ImGui.TextColored(Dim, "The host has turned voice chat off."u8);
        ImGui.Spacing();
        if (BigButton("BACK"))
        {
            _pausePlayers = false;
            s.Save();
        }
    }

    // ---- Settings > Audio: voice chat ------------------------------------------------------------------------

    /// <summary>The voice chat part of Settings > Audio (mode, mic on/off, push to talk or voice activation, device, meter, test, volumes).</summary>
    private void DrawVoiceSettings(Settings s, Audio audio)
    {
        var now = Brutal.GlfwApi.Glfw.Time;
        _micPanelAt = now;
        var w = 320 * _scale;
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextColored(Orange, "VOICE CHAT"u8);
        ImGui.Text("Voice"u8);
        var mode = (int)s.VoiceMode;
        for (var i = 0; i <= (int)VoiceMode.Off; i++)
        {
            ImGui.SameLine();
            if (ImGui.RadioButton($"{VoiceRules.Label((VoiceMode)i)}##voicemode", ref mode, i))
                s.VoiceMode = (VoiceMode)mode;
        }
        if (ImGui.IsItemHovered())
            ImGui.SetItemTooltip("Proximity: people near you hear you from where you stand (up to 40 m, muffled through walls). Team radio: only your team hears you, at any distance. Off: no voice at all, you hear nobody either. Dead players talk to the dead (and on team radio)."u8);

        ImGui.Checkbox("Use my microphone"u8, ref s.VoiceTransmit);
        if (ImGui.IsItemHovered())
            ImGui.SetItemTooltip("Off until you turn it on: nothing is recorded or sent before that. Your mic is only open while this is on (or while testing)."u8);
        if (s.VoiceTransmit)
        {
            ImGui.Text("Transmit"u8);
            ImGui.SameLine();
            var vad = s.VoiceActivation ? 1 : 0;
            ImGui.RadioButton($"Push to talk ({Keybinds.Name(s.Bind(GameAction.PushToTalk))})##ptt", ref vad, 0);
            ImGui.SameLine();
            ImGui.RadioButton("Voice activation"u8, ref vad, 1);
            s.VoiceActivation = vad == 1;
            if (s.VoiceActivation)
            {
                ImGui.SetNextItemWidth(w);
                ImGui.SliderFloat("Activation level (dB)"u8, ref s.VoiceThresholdDb, VoiceSender.MinThresholdDb, VoiceSender.MaxThresholdDb, "%.0f"u8);
            }

            // Device picker.
            ImGui.SetNextItemWidth(w);
            var current = s.VoiceDevice.Length == 0 ? "System default" : s.VoiceDevice;
            if (ImGui.BeginCombo("Microphone"u8, current))
            {
                if (ImGui.IsWindowAppearing())
                    _micDevices = MicCapture.Devices();
                if (ImGui.Selectable("System default"u8, s.VoiceDevice.Length == 0))
                    s.VoiceDevice = "";
                for (var i = 0; i < _micDevices.Length; i++)
                    if (ImGui.Selectable($"{_micDevices[i]}##mic{i}", s.VoiceDevice == _micDevices[i]))
                        s.VoiceDevice = _micDevices[i];
                ImGui.EndCombo();
            }

            // Level meter (with the activation level marked).
            var level = audio.Mic.IsOpen ? audio.MicLevelDb : -96f;
            ImGui.PushStyleColor(ImGuiCol.PlotHistogram, audio.MicTransmitting ? Green : Dim);
            ImGui.ProgressBar(VoiceSender.Meter(level), new float2(w, 10 * _scale), ""u8);
            ImGui.PopStyleColor();
            if (s.VoiceActivation)
            {
                var min = ImGui.GetItemRectMin();
                var max = ImGui.GetItemRectMax();
                var x = min.X + (max.X - min.X) * VoiceSender.Meter(s.VoiceThresholdDb);
                ImGui.GetWindowDrawList().AddLine(new float2(x, min.Y - 2), new float2(x, max.Y + 2), new ImColor8(255, 200, 80, 255), 2f);
            }
            ImGui.SameLine();
            ImGuiSafe.TextColored(Dim, audio.Mic.IsOpen ? $"{level:0} dB" : "mic closed");
            if (ImGui.Button(MicTestOn ? "Stop test"u8 : "Test mic"u8))
                MicTestOn = !MicTestOn;
            if (ImGui.IsItemHovered())
                ImGui.SetItemTooltip("Hear yourself as others will (through the voice codec). Use headphones, or you'll hear an echo."u8);
            ImGui.SameLine();
            ImGuiSafe.TextColored(Dim, audio.Mic.Status);
        }
        else
            MicTestOn = false;

        ImGui.SetNextItemWidth(w);
        if (ImGui.SliderFloat("Voice volume"u8, ref s.VoiceVolume, 0f, 1f, "%.2f"u8))
            audio.VoiceVolume = s.VoiceVolume;
        ImGui.Checkbox("Mute all voice"u8, ref s.VoiceMuteAll);
        ImGuiSafe.TextColored(Dim, $"Chat: {Keybinds.Name(s.Bind(GameAction.ChatAll))} (everyone), {Keybinds.Name(s.Bind(GameAction.ChatTeam))} (your team). Mute someone: Esc > PLAYERS.");
    }

    /// <summary>When the pause menu is left, the PLAYERS page closes too.</summary>
    private void ResetPausePlayers() => _pausePlayers = false;
}
