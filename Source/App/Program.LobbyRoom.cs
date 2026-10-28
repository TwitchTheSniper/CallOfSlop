using System.Numerics;
using Brutal.GlfwApi;
using Brutal.OpenGlApi;

namespace CallOfSlop;

/// <summary>
/// The pre-match lobby room in the window (rules in Source/Game/PreMatchLobby.cs, screen in Source/UI/UiLobbyRoom.cs):
/// while the session is in <see cref="MatchPhase.Lobby"/> the mouse stays free, the session's map is drawn behind the
/// menu from a slow orbit (so its meshes, props, foliage and sky are built before the match: the preload), and the
/// lobby screen replaces the HUD. Plus the smoke-test step that screenshots it (smoke_lobby_room.bmp).
/// </summary>
public static unsafe partial class Program
{
    private static bool _wasLobbyRoom, _roomLoadoutsOpen;

    /// <summary>The session is in the lobby room.</summary>
    private static bool InLobbyRoom => _session is { InLobby: true };

    /// <summary>StartSession: the lobby room for hosting, and the host's team balance (single player too).</summary>
    private static void ConfigureLobbyRoom(Game game, LobbyAction kind)
    {
        game.LobbyEnabled = kind == LobbyAction.Host && _rangeLaunch == null; // single player: the menu page has the settings; the warmup starts it
        if (kind != LobbyAction.Join)
        {
            game.ReturnToLobby = kind == LobbyAction.Host && _settings.LobbyReturn;
            game.MatchMinutes = kind == LobbyAction.Host ? _settings.MatchMinutes : (int)(Game.MatchSeconds / 60);
            game.TeamBalance = _settings.TeamBalance; // (TeamBalance.cs; clients learn the host's from the mode state)
            game.HumansTeam = _settings.HumansTeam;
        }
        _wasLobbyRoom = false;
        _roomLoadoutsOpen = false;
        _sharingApplied = (_settings.ShareOnLists, _settings.AutoListServer);
        Ui.ResetLobbyRoom();
    }

    private static (bool Share, bool Auto) _sharingApplied;

    /// <summary>The host ticked "Share on our server lists" (or "Run one") in the lobby's Server section: list again now.</summary>
    private static void ApplyLobbySharing(Game game)
    {
        var want = (_settings.ShareOnLists, _settings.AutoListServer);
        if (!game.IsServer || game.Net.IsOffline || want == _sharingApplied)
            return;
        _sharingApplied = want;
        StartHostListing(game); // (stops the old listing first; not sharing: just stops it)
    }

    /// <summary>Every frame of a session: notices entering and leaving the lobby room. Returns true while in it.</summary>
    private static bool TrackLobbyRoom(double now)
    {
        var inLobby = InLobbyRoom;
        if (inLobby && !_wasLobbyRoom)
        {
            Ui.MenuOpen = false; // (a joiner's loadout screen: the lobby has its own LOADOUT button)
            Ui.PauseOpen = false;
            _deathCam.Stop();
            SetMouseCaptured(false);
        }
        else if (!inLobby && _wasLobbyRoom)
        {
            if (Ui.LobbyLoadouts)
            {
                Ui.LobbyLoadouts = false;
                SendRoomLoadout(now);
            }
            _roomLoadoutsOpen = false;
            if (_session is { IsServer: true } && !_smoke)
                _settings.Save(); // the host's lobby choices, for next time (the smoke test never writes settings.txt)
        }
        _wasLobbyRoom = inLobby;
        return inLobby;
    }

    private static void SendRoomLoadout(double now)
    {
        if (_session is { } g)
            g.ChooseLoadout(LoadoutStore.Active(g.Progress ?? _progress), now);
    }

    /// <summary>The lobby room's input: the Ready key, and the loadout screen closing (the server gets the new loadout).</summary>
    private static void UpdateLobbyRoom(double now)
    {
        PollActions(); // (blocked while typing in the chat)
        if (Pressed(GameAction.Ready))
            _game.ToggleReady();
        _game.TakeCountdownStarted(); // the lobby isn't a countdown: no loadout screen pops up
        _game.TakePlayStarted();
        _game.HeldButtons = Buttons.None;
        _game.AimHeld = false;
        if (_roomLoadoutsOpen && !Ui.LobbyLoadouts)
        {
            _roomLoadoutsOpen = false;
            SendRoomLoadout(now);
        }
    }

    /// <summary>The session's map from a slow orbit behind the lobby room (like the main menu's background, without changing maps).</summary>
    private static void RenderLobbyRoom(double now)
    {
        var fb = _window.FramebufferSize;
        if (fb.X <= 0 || fb.Y <= 0)
            return;
        var map = World.Map;
        Gfx.BeginFrame(fb.X, fb.Y, map.Sky);
        var a = (float)now * 0.06f;
        var r = map.ViewRadius;
        var eye = new Vector3(MathF.Sin(a) * r * map.HalfX / MathF.Max(map.HalfX, map.HalfZ), r * 0.4f + MathF.Sin((float)now * 0.2f) * 2f,
            MathF.Cos(a) * r * map.HalfZ / MathF.Max(map.HalfX, map.HalfZ));
        var fwd = Vector3.Normalize(new Vector3(0, 1.5f, 0) - eye);
        AudioSystem.SetListener(eye, fwd);
        Gfx.SetCamera(Matrix4x4.CreateLookAt(eye, eye + fwd, Vector3.UnitY), Matrix4x4.CreatePerspectiveFieldOfView(1.1f, fb.X / (float)fb.Y, 0.05f, map.FarPlane));
        Gfx.SetFog(map.Sky, map.FogStart * 1.5f, map.FogEnd * 1.5f);
        DrawArena();
        Gfx.SetFog(map.Sky, 0, 0);
        Gfx.SetCamera(Matrix4x4.Identity, Matrix4x4.CreateOrthographicOffCenter(0, fb.X, 0, fb.Y, -1, 1));
        Gfx.SetDepthTest(false);
        Gfx.SetBlend(true);
        Gfx.Rect(0, 0, fb.X, fb.Y, Vector3.Zero, 0.45f); // darker than the menu: the lobby panel is bigger
        Gfx.SetBlend(false);
        Gfx.SetDepthTest(true);
    }

    /// <summary>The lobby room's UI (or the loadout screen opened from it).</summary>
    private static void DrawLobbyRoomUi(Game game, double now)
    {
        if (Ui.LobbyLoadouts)
        {
            _roomLoadoutsOpen = true;
            Ui.DrawLobbyLoadouts(AudioSystem, now);
            return;
        }
        var roomAction = Ui.DrawLobbyRoom(game, _settings, game.IsServer ? _joinAddress : "", now);
        ApplyLobbySharing(game);
        switch (roomAction)
        {
            case LobbyRoomAction.Leave:
                if (game.IsServer && !_smoke)
                    _settings.Save(); // the host's lobby choices, for next time, even without a match
                EndSession("");
                break;
            case LobbyRoomAction.Loadout:
                Ui.OpenLobbyLoadouts();
                _roomLoadoutsOpen = true;
                break;
        }
    }

    // ---- Smoke step (map 0, after the TEAMS shot): open the lobby room over the single-player session, say something
    // in the chat, screenshot it (smoke_lobby_room.bmp) with fps, then back to the warmup so the rest of the run is unchanged.

    private static int _lobbySmoke;
    private static double _lobbySmokeAt;
    private static int _lobbySmokeFrames;

    /// <summary>One tick of the step. Returns true while it's busy.</summary>
    private static bool SmokeLobbyRoomTick(Game? game, double now)
    {
        if (game is not { IsServer: true })
            return false;
        switch (_lobbySmoke)
        {
            case 0:
                game.DebugLobby(true, now);
                game.SendChat("Smoke test: hello lobby", now);
                _lobbySmokeAt = now;
                _lobbySmokeFrames = _smokeFrames;
                _lobbySmoke = 1;
                return true;
            case 1 when now - _lobbySmokeAt > 1.0:
                SmokeShot("lobby_room");
                var fps = (_smokeFrames - _lobbySmokeFrames) / Math.Max(now - _lobbySmokeAt, 0.001);
                Console.WriteLine($"[smoke] lobby room: {game.Players.Count} players on {World.Map.Name} ({game.Phase}), {fps:0} fps, "
                    + $"text rebuilt {Ui.LobbyRoom.Rebuilds} times, chat {game.Chat.Count} line(s), GL error 0x{GlDebug.Error():X}");
                _lobbySmokeAt = now;
                _lobbySmoke = 2;
                return true;
            case 2 when now - _lobbySmokeAt > 0.2:
                game.DebugLobby(false, now); // back to the warmup
                _lobbySmoke = 3;
                return false;
            case 1 or 2:
                return true;
            default:
                return false;
        }
    }
}
