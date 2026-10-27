using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// The Zombies round flow (user requests, 2026-10-23; docs/zombies/ZOMBIES-MODE.md "Between rounds"):
/// <list type="bullet">
/// <item>Players who bled out come back <b>when the round ends</b> (its last zombie dies), with the start gear, so they
/// have the whole break at the workbench (<see cref="RespawnAtRoundEnd"/>).</item>
/// <item>The next round waits for <b>ready-up</b>: every living person presses the Ready key; bots count as ready and the
/// dead don't block. Then a short countdown (<see cref="ZombiesRules.ReadyGoSeconds"/>) and the round's banner and sound
/// as before. A long fallback (<see cref="ZombiesRules.ReadyFallbackSeconds"/>, 0 = off) stops an AFK player stalling the
/// run, and the host can force the start (pressing Ready again once they're ready).</item>
/// <item>Spectating: the pure pick and camera helpers for a player who bled out (<see cref="ZSpectate"/>; Program draws it).</item>
/// </list>
/// Crawling while downed is in ZombieBots.cs (<c>ZombieInput</c>, <c>ZombieBotCrawl</c>).
/// </summary>
public sealed partial class Game
{
    /// <summary>The break is waiting for ready-up (server-owned, in <see cref="Msg.ZWorld"/>'s last byte since protocol 47).</summary>
    public bool ZWaitingReady;

    /// <summary>Client view state: the living teammate the local player watches after bleeding out (byte.MaxValue: nobody; Program.Zombies.cs).</summary>
    public byte ZSpectateId = byte.MaxValue;

    private bool _zForceRound;
    private double _zReadyFallbackAt;
    private double _zReadyPressedAt = -10;

    /// <summary>Does this player count for ready-up: a living person in the run (bots are always ready; the dead and a sitting-out host don't count)?</summary>
    private bool ReadyVoter(Player p) => p.Bot == null && p.Alive && !(ZHostSitsOut && p.Id == LocalId);

    /// <summary>Server: seconds left of the ready-up fallback (0 when it's off).</summary>
    private double ReadyFallbackLeft(double now) => ZRules.ReadyFallbackSeconds > 0 ? Math.Max(0, _zReadyFallbackAt - now) : 0;

    /// <summary>
    /// Ready-up counts, both sides: people who pressed Ready, out of the people the break waits for (from the server's
    /// flags on clients). Bots and the dead aren't in either number.
    /// </summary>
    public (int Ready, int Needed) ZReadyCount
    {
        get
        {
            int ready = 0, needed = 0;
            foreach (var p in Players.Values)
            {
                if (!ZPlayers.TryGetValue(p.Id, out var zp) || !zp.ReadyVoter)
                    continue;
                needed++;
                if (zp.RoundReady)
                    ready++;
            }
            return (ready, needed);
        }
    }

    /// <summary>Server, when a round ends: everyone who bled out during it is back now, at the start, with the start gear.</summary>
    private void RespawnAtRoundEnd(double now)
    {
        foreach (var p in Players.Values)
            if (!p.Alive && !(ZHostSitsOut && p.Id == LocalId))
            {
                p.RespawnAt = now; // Spawn -> ZombiesOnSpawn: the start area and the start gear (dropped builds stay on the floor)
                p.WantsRespawn = true;
            }
    }

    /// <summary>Server, when a round ends: start waiting for ready-up, unless it's off or there's no person in the run. Returns whether it did.</summary>
    private bool BeginReadyUp(double now)
    {
        var people = false;
        foreach (var p in Players.Values)
        {
            var zp = ZP(p);
            zp.RoundReady = false;
            zp.ReadyVoter = false;
            people |= p.Bot == null && !(ZHostSitsOut && p.Id == LocalId);
        }
        if (!ZRules.ReadyUp || !people)
            return false;
        ZWaitingReady = true;
        _zForceRound = false;
        _zPhaseUntil = double.MaxValue;
        _zReadyFallbackAt = ZRules.ReadyFallbackSeconds > 0 ? now + ZRules.ReadyFallbackSeconds : double.MaxValue;
        var fallback = ZRules.ReadyFallbackSeconds > 0 ? $" (it starts anyway in {FormatClock(ZRules.ReadyFallbackSeconds)})" : "";
        ModeEvent($"Round {ZRound} survived. Rebuild, buy, then press Ready for round {ZRound + 1}{fallback}.", Sfx.ReconBeep);
        return true;
    }

    private static string FormatClock(double seconds) => $"{(int)seconds / 60}:{(int)seconds % 60:00}";

    /// <summary>Server, every break tick while waiting: everyone ready, the fallback ran out or the host forced it -> the countdown.</summary>
    private void TickReadyUp(double now)
    {
        int ready = 0, needed = 0;
        foreach (var p in Players.Values)
        {
            var zp = ZP(p);
            zp.ReadyVoter = ReadyVoter(p) || (p.Bot == null && !p.Alive && p.WantsRespawn && p.RespawnAt <= now && !(ZHostSitsOut && p.Id == LocalId)); // (back this tick: they count)
            if (!zp.ReadyVoter)
            {
                zp.RoundReady = false;
                continue;
            }
            needed++;
            if (zp.RoundReady)
                ready++;
        }
        var fallback = now >= _zReadyFallbackAt;
        if (!_zForceRound && ready < needed && !fallback)
            return;
        ZWaitingReady = false;
        _zPhaseUntil = now + ZRules.ReadyGoSeconds;
        var why = _zForceRound && ready < needed ? "The host started the next round" : fallback && ready < needed ? "Time's up" : "Everyone is ready";
        _zForceRound = false;
        ModeEvent($"{why}: round {ZRound + 1} in {ZRules.ReadyGoSeconds:0} seconds.", Sfx.StreakReady);
        Console.WriteLine($"[zombies] ready-up: {why.ToLowerInvariant()} ({ready}/{needed}), round {ZRound + 1} at +{ZRules.ReadyGoSeconds:0.0} s");
    }

    /// <summary>Server: a person said they're ready (or not) for the next round (the Ready key, <see cref="Msg.Ready"/> during a match).</summary>
    private void ServerSetRoundReady(Player p, bool ready)
    {
        if (!IsZombies || ZPhase != ZRoundPhase.Break || !ZWaitingReady || !p.Alive)
            return;
        var zp = ZP(p);
        if (zp.RoundReady == ready)
            return;
        zp.RoundReady = ready;
        zp.ReadyVoter = ReadyVoter(p);
        var (r, n) = ZReadyCount;
        ModeEvent($"{p.Name} is {(ready ? "ready" : "not ready")} ({r}/{n})", Sfx.HitTick);
    }

    /// <summary>
    /// The Ready key in a Zombies break, either side: ready / not ready for the next round. The host, already ready while
    /// someone isn't, forces the start instead (online only; solo the first press starts it). Returns false when it's
    /// not the time for it (the key then does what it does elsewhere).
    /// </summary>
    public bool ZToggleRoundReady()
    {
        if (!IsZombies || Phase != MatchPhase.Playing || ZPhase != ZRoundPhase.Break || !ZWaitingReady || Local is not { Alive: true } me)
            return false;
        var zp = ZP(me);
        var clock = Brutal.GlfwApi.Glfw.Time;
        if (IsServer && zp.RoundReady && ZReadyCount is var (r, n) && r < n)
        {
            if (clock - _zReadyPressedAt > 0.4) // (not a bounce of the press that readied)
                ZForceRound();
            return true;
        }
        _zReadyPressedAt = clock;
        var want = !zp.RoundReady;
        Audio.Effect(Sfx.HitTick, 0.5f);
        if (IsServer)
            ServerSetRoundReady(me, want);
        else
        {
            zp.RoundReady = want; // shown straight away; the server's copy follows in ZWorld
            _w.Begin(Msg.Ready).U8(want ? (byte)1 : (byte)0);
            Net.SendToServer(_w.Span, PacketReliability.ReliableOrdered);
        }
        return true;
    }

    /// <summary>Host: start the next round now, ready or not (after the short countdown).</summary>
    public void ZForceRound()
    {
        if (IsServer && IsZombies && ZPhase == ZRoundPhase.Break && ZWaitingReady)
            _zForceRound = true;
    }

    // ---- Self-test hooks ------------------------------------------------------------------------------

    /// <summary>Self-test: <paramref name="p"/> presses Ready (or takes it back) for the next round, as the server hears it.</summary>
    public void SelfTestRoundReady(Player p, bool ready) => ServerSetRoundReady(p, ready);

    /// <summary>Self-test: the round in progress ends now (its zombies all dead).</summary>
    public void SelfTestEndRound(double now)
    {
        ClearHorde();
        _zToSpawn = 0;
        EndRound(now);
    }
}

/// <summary>
/// Spectating in Zombies after bleeding out (2026-10-23): who to watch and where the camera goes. Pure (players and a
/// raycast passed in), so the self-test runs it headless; Program.Zombies.cs keeps the choice and draws it.
/// Only living teammates are watched (downed ones too: they're alive), never yourself.
/// </summary>
public static class ZSpectate
{
    /// <summary>The camera's place behind the watched player's head: back, up and to the right (over the shoulder).</summary>
    public const float Back = 2.1f, Up = 0.35f, Right = 0.45f, WallGap = 0.2f;

    /// <summary>
    /// The player to watch: <paramref name="step"/> 0 keeps <paramref name="current"/> if they're still alive (else the
    /// next one), +1 the next living teammate by id, -1 the previous (both wrap round). byte.MaxValue when nobody else is alive.
    /// </summary>
    public static byte Pick(Game game, byte current, int step)
    {
        byte first = byte.MaxValue, last = byte.MaxValue, next = byte.MaxValue, prev = byte.MaxValue;
        var curAlive = false;
        foreach (var p in game.Players.Values)
        {
            if (p.Id == game.LocalId || !p.Alive)
                continue;
            var id = p.Id;
            if (first == byte.MaxValue || id < first)
                first = id;
            if (last == byte.MaxValue || id > last)
                last = id;
            if (id == current)
                curAlive = true;
            if (current != byte.MaxValue && id > current && (next == byte.MaxValue || id < next))
                next = id;
            if (current != byte.MaxValue && id < current && (prev == byte.MaxValue || id > prev))
                prev = id;
        }
        if (first == byte.MaxValue)
            return byte.MaxValue;
        if (current == byte.MaxValue)
            return step < 0 ? last : first;
        return step switch
        {
            0 when curAlive => current,
            < 0 => prev != byte.MaxValue ? prev : last,
            _ => next != byte.MaxValue ? next : first,
        };
    }

    /// <summary>
    /// The over-the-shoulder camera for a player whose eye is at <paramref name="eye"/> looking along
    /// <paramref name="yaw"/> / <paramref name="pitch"/>: pulled in short of any wall (<paramref name="ray"/>(origin, unit
    /// dir, max) gives the free distance). The camera looks the way they look.
    /// </summary>
    public static Vector3 ShoulderEye(Vector3 eye, float yaw, float pitch, Func<Vector3, Vector3, float, float> ray)
    {
        if (!float.IsFinite(eye.X + eye.Y + eye.Z))
            return Vector3.Zero;
        yaw = float.IsFinite(yaw) ? yaw : 0;
        pitch = float.IsFinite(pitch) ? Math.Clamp(pitch, -1.2f, 1.2f) : 0;
        var up = Math.Clamp(Free(ray(eye, Vector3.UnitY, Up + WallGap)) - WallGap, 0, Up);
        var p = eye + new Vector3(0, up, 0);
        var flat = World.FlatForward(yaw);
        var right = new Vector3(-flat.Z, 0, flat.X);
        var side = Math.Clamp(Free(ray(p, right, Right + WallGap)) - WallGap, 0, Right);
        p += right * side;
        var back = -World.Forward(yaw, pitch * 0.6f);
        var dist = Math.Clamp(Free(ray(p, back, Back + WallGap)) - WallGap, 0, Back);
        return p + back * dist;
    }

    private static float Free(float d) => float.IsFinite(d) ? d : 0;
}
