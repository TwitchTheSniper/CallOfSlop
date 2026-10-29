using Brutal.GlfwApi;

namespace CallOfSlop;

/// <summary>
/// Chat keys and voice chat for the window: opens the HUD chat line on Chat (all) / Chat (team), reads the microphone
/// (only while the player has turned it on, or is testing it), decides push-to-talk / voice activation, sends frames to the
/// game, and plays everyone's voice (Audio.Voice.cs). Runs every frame, in and out of matches (the mic test works from the
/// main menu's settings too).
/// </summary>
public static unsafe partial class Program
{
    private static readonly VoiceSender _voiceSender = new();
    private static readonly short[] _micPcm = new short[VoiceCodec.FrameSamples];
    private static bool _chatLatch, _pttWasDown;
    private static Game? _voiceGame;
    private static double _voiceStatsAt;
    private static long _voiceFramesSent;

    /// <summary>Hosting / single player: the host's voice and chat settings onto the new session (joiners learn the host's).</summary>
    private static void ApplyVoiceHostSettings(Game g, bool joining)
    {
        if (joining)
            return;
        g.VoiceAllowed = _settings.AllowVoice;
        g.DeadChatPrivate = _settings.DeadChatPrivate;
    }

    /// <summary>The chat keys (from UpdateLocalPlayer, after the keybinds were polled). Keys held when the line closed wait for release.</summary>
    private static void ChatKeys()
    {
        if (_chatLatch && !Keybinds.IsDown(_window, _settings.Bind(GameAction.ChatAll)) && !Keybinds.IsDown(_window, _settings.Bind(GameAction.ChatTeam)))
            _chatLatch = false;
        if (Ui.ChatOpen || _chatLatch || Ui.PauseOpen || Ui.BenchRun != null || Ui.Rebinding != null || Ui.SandboxPartsOpen) // (Enter takes a part in the Sandbox parts menu)
            return;
        if (Pressed(GameAction.ChatAll))
            Ui.OpenChat(team: false);
        else if (Pressed(GameAction.ChatTeam))
            Ui.OpenChat(team: true);
    }

    /// <summary>The HUD chat and the mic indicator (ImGui pass, in a match).</summary>
    private static void DrawChatUi(Game g, double now)
    {
        Ui.DrawChat(g, now, _settings);
        if (Ui.TakeChatClosed())
            _chatLatch = true; // Enter is still down from sending: don't reopen
    }

    /// <summary>Microphone in, voices out. Once a frame, after the game update.</summary>
    private static void UpdateVoice(double now)
    {
        var s = _settings;
        var audio = AudioSystem;
        audio.VoiceVolume = s.VoiceVolume;
        audio.VoiceMuteAll = s.VoiceMuteAll;
        var game = _session;
        if (!ReferenceEquals(game, _voiceGame))
        {
            _voiceGame = game;
            game?.LoadVoiceMutes(s);
            _voiceSender.Reset();
            audio.Loopback.Jitter.Reset();
        }
        if (game == null || InLobbyRoom)
            Ui.CloseChat(); // the HUD chat line belongs to the match (the lobby room has its own box)
        game?.SetVoiceListen(s.VoiceMode != VoiceMode.Off && !s.VoiceMuteAll);

        // The mic is open only while it is in use: you talk in a networked game, you test it, or you look at its meter.
        var testing = Ui.MicTestOn && s.VoiceTransmit && Ui.MicPanelOpen(now);
        if (!testing)
            Ui.MicTestOn = false;
        var canTalk = game != null && !game.Net.IsOffline && s.VoiceTransmit && s.VoiceMode != VoiceMode.Off && game.VoiceAllowed;
        var wantMic = canTalk || testing || (s.VoiceTransmit && Ui.MicPanelOpen(now));
        if (wantMic)
            audio.Mic.Open(s.VoiceDevice, now);
        else if (audio.Mic.IsOpen)
            audio.Mic.Close();

        var ptt = !Ui.WantsTextInput && !Ui.ChatOpen && Ui.Rebinding == null && Keybinds.IsDown(_window, s.Bind(GameAction.PushToTalk));
        if (ptt && !_pttWasDown && game != null && !canTalk && !testing)
            Ui.VoiceHint(game.Net.IsOffline ? "Voice chat needs a multiplayer game"
                : !game.VoiceAllowed ? "The host has turned voice chat off"
                : s.VoiceMode == VoiceMode.Off ? "Voice chat is Off (Settings > Audio)"
                : "Turn on \"Use my microphone\" in Settings > Audio to talk", now);
        _pttWasDown = ptt;

        var reads = 0;
        while (audio.Mic.IsOpen && reads++ < 12 && audio.Mic.Read(_micPcm))
        {
            var send = _voiceSender.Process(_micPcm, ptt || testing, s.VoiceActivation, s.VoiceThresholdDb, now);
            audio.MicLevelDb = _voiceSender.LevelDb;
            audio.MicTransmitting = send;
            if (!send)
                continue;
            if (testing)
                audio.LoopbackFrame(_voiceSender.Frame, _voiceSender.Seq, now); // straight back to you, not to anyone else
            else if (canTalk && game!.SendVoice(_voiceSender, s.VoiceMode == VoiceMode.TeamRadio, now))
                _voiceFramesSent++;
        }
        if (!audio.Mic.IsOpen)
        {
            audio.MicLevelDb = -96f;
            audio.MicTransmitting = false;
        }

        game?.UpdateVoiceHeads();
        game?.UpdateVoiceVolumes(s);
        audio.UpdateVoice(game?.Voice, now);

        if (now - _voiceStatsAt > 60)
        {
            _voiceStatsAt = now;
            if (_voiceFramesSent > 0 || audio.VoiceFramesPlayed > 0)
                Console.WriteLine($"[voice] sent {_voiceFramesSent} frames; {audio.VoiceStats()}; mic {audio.Mic.Status}"
                    + (game is { IsServer: true } ? $"; relayed {game.VoiceFramesRelayed} ({game.VoiceBytesRelayed / 1024} KB), rate-dropped {game.VoiceRateDropped}" : ""));
        }
    }
}
