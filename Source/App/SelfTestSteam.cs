using System.Diagnostics;
using CallOfSlop.Platform.Steam;

namespace CallOfSlop;

/// <summary>
/// "Steam platform" (Source/Platform/Steam): the game runs with Steam absent (the layer stays off and never touches the
/// binding), the start decision covers every trigger, the native library ships, and the layer works end to end on a
/// recorded backend: init, callbacks, the overlay, names and avatars, Steam ids over the network, shutdown.
/// </summary>
public static partial class SelfTest
{
    private static void CheckSteamPlatform()
    {
        var real = SteamPlatform.Backend;
        try
        {
            SteamAbsent();
            SteamPlans();
            SteamNative();
            SteamRecorded();
            SteamIdsOverTheNetwork();
        }
        finally
        {
            SteamPlatform.UseBackend(real); // every later section runs without Steam, as the game does
        }
    }

    /// <summary>Nothing asked for Steam: off, unavailable cleanly, no restart, and every query answers "none".</summary>
    private static void SteamAbsent()
    {
        var folder = Path.Combine(Path.GetTempPath(), "cos-steam-" + Environment.ProcessId);
        Directory.CreateDirectory(folder);
        try
        {
            var started = SteamPlatform.Init(["--selftest"], folder, 0);
            Line($"absent: started {started}, state {SteamPlatform.State}, reason '{SteamPlatform.Reason}', restart {SteamPlatform.RestartRequested}, "
                 + $"id {SteamPlatform.LocalSteamId}, persona '{SteamPlatform.PersonaName}', Steam build {SteamPlatform.IsSteamBuild}");
            if (started || SteamPlatform.Available || SteamPlatform.State != SteamState.Off || SteamPlatform.RestartRequested)
                Fail("Steam absent: the layer should stay off");
            if (SteamPlatform.IsSteamBuild)
                Fail("this is a Steam build (the SteamAppId property is set): the self-test must run on a plain build");
            if (SteamPlatform.LocalSteamId != 0 || SteamPlatform.PersonaName.Length != 0 || SteamPlatform.Friends.NameOf(1) != "" || SteamPlatform.Friends.AvatarOf(1) != null)
                Fail("Steam absent: ids, names and avatars must be empty");
            SteamPlatform.RunCallbacks(); // no-ops
            SteamPlatform.Shutdown();
            if (SteamPlatform.State != SteamState.Off)
                Fail("Steam absent: shutdown left the layer in a state other than Off");
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    /// <summary>The start decision, case by case (pure: no Steam call).</summary>
    private static void SteamPlans()
    {
        var folder = Path.Combine(Path.GetTempPath(), "cos-steam-plan-" + Environment.ProcessId);
        Directory.CreateDirectory(folder);
        string? NoEnv(string _) => null;
        try
        {
            void Expect(string what, SteamStartPlan plan, SteamStartReason reason, uint appId, bool start, bool restart = false)
            {
                Line($"plan {what}: {plan.Reason} app {plan.AppId} start {plan.Start} restart {plan.RestartThroughSteam}: {plan.Detail}");
                if (plan.Reason != reason || plan.AppId != appId || plan.Start != start || plan.RestartThroughSteam != restart)
                    Fail($"plan {what}: expected {reason} app {appId} start {start} restart {restart}");
            }
            Expect("nothing", SteamPlatform.Plan(["host"], folder, NoEnv, 0), SteamStartReason.None, 0, false);
            Expect("Steam build", SteamPlatform.Plan([], folder, NoEnv, SteamPlatform.CallOfSlopAppId), SteamStartReason.SteamBuild, 1750920, true, restart: true);
            Expect("--no-steam beats a Steam build", SteamPlatform.Plan(["--no-steam"], folder, NoEnv, 123456), SteamStartReason.Disabled, 0, false);
            Expect("--steam placeholder", SteamPlatform.Plan(["--steam"], folder, NoEnv, 0), SteamStartReason.Flag, SteamPlatform.PlaceholderAppId, true);
            if (!SteamPlatform.Plan(["--steam"], folder, NoEnv, 0).Detail.Contains("PLACEHOLDER"))
                Fail("the Spacewar placeholder must be marked as such in the plan");
            Expect("--steam=777", SteamPlatform.Plan(["--steam=777"], folder, NoEnv, 0), SteamStartReason.Flag, 777, true);
            Expect("--steam=abc", SteamPlatform.Plan(["--steam=abc"], folder, NoEnv, 0), SteamStartReason.Disabled, 0, false);
            Expect("launched by Steam", SteamPlatform.Plan([], folder, k => k == "SteamAppId" ? "2468" : null, 0), SteamStartReason.Launcher, 2468, true);
            Expect("SteamAppId=0 in the environment is nothing", SteamPlatform.Plan([], folder, k => k == "SteamAppId" ? "0" : null, 0), SteamStartReason.None, 0, false);
            var file = Path.Combine(folder, SteamPlatform.AppIdFileName);
            File.WriteAllText(file, "480\n");
            Expect("steam_appid.txt", SteamPlatform.Plan([], folder, NoEnv, 0), SteamStartReason.AppIdFile, 480, true);
            Expect("steam_appid.txt loses to --steam=777", SteamPlatform.Plan(["--steam=777"], folder, NoEnv, 0), SteamStartReason.Flag, 777, true);
            Expect("steam_appid.txt loses to --no-steam", SteamPlatform.Plan(["--no-steam"], folder, NoEnv, 0), SteamStartReason.Disabled, 0, false);
            File.WriteAllText(file, "not an id");
            Expect("bad steam_appid.txt", SteamPlatform.Plan([], folder, NoEnv, 0), SteamStartReason.Disabled, 0, false);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    /// <summary>The native steam_api ships with the build on this OS (the NuGet runtimes layout, or flat in a package).</summary>
    private static void SteamNative()
    {
        var path = SteamPlatform.NativeLibraryPath();
        Line($"native library {SteamPlatform.NativeLibraryFileName()}: {path ?? "MISSING"}");
        if (path == null)
            Fail($"the native Steam library {SteamPlatform.NativeLibraryFileName()} isn't in the build output (the Steamworks.NET.AnyCPU package should put it in runtimes/<rid>/native)");
        if (!UpdateVerifyCli.PersonalFiles.Contains("steam_appid.txt"))
            Fail("steam_appid.txt must be in UpdateVerifyCli.PersonalFiles (a package with it would start Steam as the wrong app)");
    }

    /// <summary>The whole layer on a recorded backend.</summary>
    private static void SteamRecorded()
    {
        var folder = Path.Combine(Path.GetTempPath(), "cos-steam-rec-" + Environment.ProcessId);
        Directory.CreateDirectory(folder);
        try
        {
            // A Steam client that is not running: unavailable, with the reason, and the game carries on.
            var down = new RecordedSteamBackend { InitError = "NoSteamClient: Steam is not running" };
            SteamPlatform.UseBackend(down);
            var started = SteamPlatform.Init(["--steam"], folder, 0);
            Line($"recorded, Steam down: started {started}, state {SteamPlatform.State}, reason '{SteamPlatform.Reason}', calls {string.Join(" ", down.Calls)}");
            if (started || SteamPlatform.State != SteamState.Unavailable || !SteamPlatform.Reason.Contains("NoSteamClient") || down.Calls.Count != 1 || down.Calls[0] != $"Init({SteamPlatform.PlaceholderAppId})")
                Fail("recorded, Steam down: expected one Init call, Unavailable and the backend's reason");
            SteamPlatform.RunCallbacks();
            SteamPlatform.Shutdown();
            if (down.Calls.Count != 1)
                Fail("recorded, Steam down: RunCallbacks and Shutdown must not reach a backend that never started");

            // A Steam build started outside Steam: Steam relaunches it, and the game must quit without starting.
            var relaunch = new RecordedSteamBackend { WantsRestart = true };
            SteamPlatform.UseBackend(relaunch);
            started = SteamPlatform.Init([], folder, 123456);
            Line($"recorded, relaunch: started {started}, restart {SteamPlatform.RestartRequested}, calls {string.Join(" ", relaunch.Calls)}");
            if (started || !SteamPlatform.RestartRequested || relaunch.Calls.Count != 1 || relaunch.Calls[0] != "RestartAppIfNecessary(123456)")
                Fail("recorded, relaunch: expected RestartAppIfNecessary(123456) only, and a restart request");
            relaunch.WantsRestart = false;
            SteamPlatform.UseBackend(relaunch);
            started = SteamPlatform.Init(["--steam"], folder, 0);
            if (!started || relaunch.Calls.Count != 2 || relaunch.Calls[1] != "Init(480)")
                Fail("recorded: a developer's --steam run must never ask Steam to relaunch the game (it would launch Spacewar)");
            SteamPlatform.Shutdown();

            // Running: identity, the frame pump, the overlay, names and avatars.
            var rec = new RecordedSteamBackend { PersonaName = "Recorded Tester", LocalSteamId = 76561197960287930 };
            const ulong friend = 76561197960287931, stranger = 76561197960287932, nobody = 76561197960287933;
            rec.Names[friend] = "Known Friend";
            rec.Avatars[friend] = new SteamAvatar(32, 32, new byte[32 * 32 * 4]);
            rec.Names[stranger] = "Late Stranger";
            rec.Avatars[stranger] = new SteamAvatar(32, 32, new byte[32 * 32 * 4]);
            rec.Pending.Add(stranger);
            SteamPlatform.UseBackend(rec);
            File.WriteAllText(Path.Combine(folder, SteamPlatform.AppIdFileName), "480");
            started = SteamPlatform.Init(["host"], folder, 0);
            Line($"recorded, running: started {started}, state {SteamPlatform.State}, app {SteamPlatform.AppId}, id {SteamPlatform.LocalSteamId}, persona '{SteamPlatform.PersonaName}'");
            if (!started || !SteamPlatform.Available || SteamPlatform.AppId != 480 || SteamPlatform.LocalSteamId != rec.LocalSteamId || SteamPlatform.PersonaName != "Recorded Tester")
                Fail("recorded, running: the layer should be running with the backend's identity");
            var overlayEvents = new List<bool>();
            SteamPlatform.OverlayChanged += overlayEvents.Add;
            for (var i = 0; i < 3; i++)
                SteamPlatform.RunCallbacks();
            rec.RaiseOverlay(true);
            rec.RaiseOverlay(true); // a repeat changes nothing
            var overlayUp = SteamPlatform.OverlayActive;
            rec.RaiseOverlay(false);
            SteamPlatform.OverlayChanged -= overlayEvents.Add;
            Line($"overlay: up {overlayUp}, now {SteamPlatform.OverlayActive}, events {string.Join(",", overlayEvents)}; pumps {rec.Calls.Count(c => c == "RunCallbacks")}");
            if (!overlayUp || SteamPlatform.OverlayActive || overlayEvents.Count != 2 || !overlayEvents[0] || overlayEvents[1] || rec.Calls.Count(c => c == "RunCallbacks") != 3)
                Fail("overlay: expected exactly one open and one close event and three pumps");

            var changed = new List<ulong>();
            SteamPlatform.Friends.Changed += changed.Add;
            var knownName = SteamPlatform.Friends.NameOf(friend);
            var knownAvatar = SteamPlatform.Friends.AvatarOf(friend);
            var early = SteamPlatform.Friends.NameOf(stranger);
            var earlyAvatar = SteamPlatform.Friends.AvatarOf(stranger);
            SteamPlatform.Friends.NameOf(stranger); // asked again before it arrives: no second request
            var requests = rec.Calls.Count(c => c == $"RequestUserInformation({stranger})");
            SteamPlatform.RunCallbacks(); // the recorded Steam delivers the stranger
            var lateName = SteamPlatform.Friends.NameOf(stranger);
            var lateAvatar = SteamPlatform.Friends.AvatarOf(stranger);
            var noName = SteamPlatform.Friends.NameOf(nobody);
            var noAvatar = SteamPlatform.Friends.AvatarOf(nobody);
            SteamPlatform.Friends.NameOf(nobody);
            SteamPlatform.Friends.AvatarOf(nobody);
            var nobodyCalls = rec.Calls.Count(c => c.Contains(nobody.ToString()));
            SteamPlatform.Friends.Changed -= changed.Add;
            Line($"friends: known '{knownName}' avatar {knownAvatar?.Width}x{knownAvatar?.Height}; stranger early '{early}'/{(earlyAvatar == null ? "none" : "avatar")} requests {requests}, "
                 + $"late '{lateName}'/{(lateAvatar == null ? "none" : $"{lateAvatar.Width}x{lateAvatar.Height}")}, changed {string.Join(",", changed)}; nobody '{noName}'/{(noAvatar == null ? "none" : "avatar")} in {nobodyCalls} calls");
            if (knownName != "Known Friend" || knownAvatar is not { Width: 32, Height: 32 })
                Fail("friends: a known user's name and avatar should come straight from the backend");
            if (early != "" || earlyAvatar != null || requests != 1 || lateName != "Late Stranger" || lateAvatar == null || changed.Count == 0 || changed.Any(id => id != stranger))
                Fail("friends: an unknown user is requested once, and arrives with Changed events (name, then avatar) after the callbacks run");
            if (noName != "" || noAvatar != null || nobodyCalls > 4)
                Fail("friends: a user Steam has nothing on is asked about once, then cached as unknown (no per-frame calls)");

            SteamPlatform.Shutdown();
            Line($"shutdown: state {SteamPlatform.State}, backend running {rec.Running}, cached names {SteamPlatform.Friends.CachedNames}, last call {rec.Calls[^1]}");
            if (SteamPlatform.State != SteamState.Off || rec.Running || rec.Calls[^1] != "Shutdown" || SteamPlatform.Friends.CachedNames != 0 || SteamPlatform.LocalSteamId != 0)
                Fail("shutdown: the layer should be off, the backend shut down and the caches empty");
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    /// <summary>Host and client on (recorded) Steam: the client's id reaches the server, the table reaches the client (protocol 74).</summary>
    private static void SteamIdsOverTheNetwork()
    {
        var rec = new RecordedSteamBackend { LocalSteamId = 76561197960287940 };
        SteamPlatform.UseBackend(rec);
        var folder = Path.Combine(Path.GetTempPath(), "cos-steam-net-" + Environment.ProcessId);
        Directory.CreateDirectory(folder);
        try
        {
            SteamPlatform.Init(["--steam"], folder, 0);
            if (!SteamPlatform.Available)
            {
                Fail("Steam ids: the recorded backend didn't start");
                return;
            }
            var map = FindMap("Proto Box", m => m.MaxPlayers >= 4)!;
            const ushort port = 26323;
            var clock = Stopwatch.StartNew();
            var now = 0.0;
            World.SetMap(map);
            using var server = new Game(NetPeer.Host(port, Game.MaxPlayers - 1), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 1, mapId: map.Id);
            server.MapVoteEnabled = false;
            server.WarmupEnabled = true;
            using var client = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Guest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
            Pump(server, [client], 1.5, ref now, clock);
            if (client.LocalId == byte.MaxValue || server.Local == null)
            {
                Fail("Steam ids: the client didn't join");
                return;
            }
            var onServer = server.Players[client.LocalId].SteamId;
            var hostOnServer = server.Local.SteamId;
            var hostOnClient = client.SteamIdOf(Game.HostId);
            var selfOnClient = client.SteamIdOf(client.LocalId);
            var bot = server.Players.Values.First(p => p.Bot != null);
            var botOnClient = client.SteamIdOf(bot.Id);
            Line($"Steam ids: client on server {onServer}, host on server {hostOnServer}, host on client {hostOnClient}, self on client {selfOnClient}, bot on client {botOnClient}; protocol {ServerInfo.ProtocolVersion}, Msg.SteamIds {(byte)Msg.SteamIds}");
            if (onServer != rec.LocalSteamId || hostOnServer != rec.LocalSteamId || hostOnClient != rec.LocalSteamId || selfOnClient != rec.LocalSteamId || botOnClient != 0)
                Fail("Steam ids: every human's id should be known on both sides, and no bot's");
            int protocol = ServerInfo.ProtocolVersion;
            if (protocol < 74 || Enum.GetValues<Msg>().Max() < Msg.SteamIds)
                Fail("Steam ids: protocol 74 or later with Msg.SteamIds at the end of Msg");
        }
        finally
        {
            SteamPlatform.Shutdown();
            Directory.Delete(folder, true);
        }
    }
}
