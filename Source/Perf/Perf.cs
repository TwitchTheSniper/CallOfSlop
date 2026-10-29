using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace CallOfSlop;

/// <summary>
/// The areas the performance window (F9, Source/UI/UiPerf.cs) times on the CPU. Each has a fixed parent
/// (<see cref="Perf.Parent"/>): the tree the window shows. Order is display order only (nothing is saved or sent),
/// so new areas can go anywhere before <see cref="COUNT"/>.
/// </summary>
public enum PerfArea : byte
{
    Events, // the window's events (Glfw.PollEvents)
    Input, // the local player's input, or the lobby room
    Game, // Game.Update: the frame's game work and every fixed tick in it
    NetReceive, //   reading packets (Net.Poll and every handler)
    ServerTick, //   the host's 60 Hz ticks (the 8 ms budget applies to each one)
    Modes, //     match phases and the game mode (objectives, scores, the mode's own tick)
    Zombies, //       the Zombies run: rounds, spawns, the horde, boxes, drops
    Horde, //         the horde: thinking and moving every zombie
    Coop, //       the Co-op director and its soldiers
    Projectiles, //     grenades, rockets, grenade zones
    Destruction, //     props, mines, map logic, falling building pieces
    Players, //     people's moves (World.Simulate for the host and each client's inputs)
    Bots, //     bots: thinking, nav (A* and the graph build), moving
    NetSend, //     snapshots and the per-tick sends
    ClientTick, //   a client's prediction ticks (and their input sends)
    Visuals, //   per-frame state: interpolation, render states, feedback, sounds
    Audio, // voice chat, music state, the frenzy sound and visuals
    Render, // the 3D scene (the CPU side of every pass; GPU time is in PerfGpu)
    Buildings, //   buildings and the map's boxes, movers
    Terrain, //   terrain
    Props, //   props and debris
    Meshes, //   mesh and model ops
    Foliage, //   foliage
    Decals, //   decals and bullet impacts
    Sky, //   the sky
    Bodies, //   players (and your own legs)
    Entities, //   grenades, streaks, launchers, zombies, map logic, co-op soldiers
    Effects, //   tracers, blasts, smoke, objectives, frenzy sparks
    ViewModel, //   your gun
    Hud, //   the 2D HUD (crosshair, ammo, prompts)
    Ragdolls, //   ragdoll physics steps (drawn with the props' debris)
    Ui, // the ImGui menus and HUD windows, built and drawn
    Swap, // SwapBuffers: waiting for vsync or the GPU
    Loading, // map loads and switches, the collision world build (anywhere in the frame)
    Limiter, // the Max FPS frame limiter's wait after SwapBuffers (FrameLimiter.cs)
    COUNT,
}

/// <summary>
/// A zero-allocation scoped CPU profiler for the performance window. <c>using var _ = Perf.Measure(PerfArea.Bots);</c>
/// times a block: off (the window closed) that is one static bool test and an empty struct; on, two
/// <see cref="Stopwatch.GetTimestamp"/> calls and a push/pop on a fixed stack. Main thread only (the music thread is
/// read from its own counter). <see cref="EndFrame"/> closes the frame: every area's time this frame goes into a ring of
/// <see cref="History"/> frames (about 10 s at 60 fps) along with the frame time and the counters
/// (<see cref="PerfCounters"/>). All buffers are made once; nothing allocates per frame, open or closed.
/// </summary>
public static class Perf
{
    /// <summary>Frames kept (about 10 s at 60 fps).</summary>
    public const int History = 600;

    public const int Count = (int)PerfArea.COUNT;

    /// <summary>Deepest nesting tracked; deeper scopes still count, they just aren't checked.</summary>
    public const int MaxDepth = 32;

    /// <summary>Timing scopes (the window or overlay is up, or a self-test). Off: scopes cost a bool test.</summary>
    public static bool On;

    /// <summary>Freeze the history (the window's Pause): frames still end, nothing new is written.</summary>
    public static bool Paused;

    public static readonly double MsPerTick = 1000.0 / Stopwatch.Frequency;

    // ---- The area tree ----------------------------------------------------------------------------------------

    /// <summary>Each area's parent (-1: a root; <see cref="PerfArea.Loading"/> is a root that can open anywhere).</summary>
    public static readonly sbyte[] Parent = BuildParents();

    public static readonly string[] Names =
    [
        "Events", "Input", "Game update", "Net receive", "Server ticks", "Match and mode", "Zombies", "Horde", "Co-op",
        "Projectiles", "Props, logic, destruction", "Player moves", "Bots + nav", "Net send (snapshots)", "Client prediction",
        "Frame state, sounds", "Audio, voice, frenzy", "Render (CPU)", "Buildings", "Terrain", "Props", "Meshes", "Foliage",
        "Decals", "Sky", "Players", "Entities, zombies", "Effects", "View model", "HUD (2D)", "Ragdolls", "UI (ImGui)",
        "Swap (vsync/GPU wait)", "Loading", "Frame limiter (Max FPS wait)",
    ];

    /// <summary>Depth in the tree (0 for roots).</summary>
    public static readonly byte[] Depth = BuildDepths();

    private static sbyte[] BuildParents()
    {
        var p = new sbyte[Count];
        Array.Fill(p, (sbyte)-1);
        void Under(PerfArea parent, params PerfArea[] kids)
        {
            foreach (var k in kids)
                p[(int)k] = (sbyte)parent;
        }
        Under(PerfArea.Game, PerfArea.NetReceive, PerfArea.ServerTick, PerfArea.ClientTick, PerfArea.Visuals);
        Under(PerfArea.ServerTick, PerfArea.Modes, PerfArea.Projectiles, PerfArea.Destruction, PerfArea.Players, PerfArea.Bots, PerfArea.NetSend);
        Under(PerfArea.Modes, PerfArea.Zombies, PerfArea.Coop);
        Under(PerfArea.Zombies, PerfArea.Horde);
        Under(PerfArea.Render, PerfArea.Buildings, PerfArea.Terrain, PerfArea.Props, PerfArea.Meshes, PerfArea.Foliage, PerfArea.Decals, PerfArea.Sky,
            PerfArea.Bodies, PerfArea.Entities, PerfArea.Effects, PerfArea.ViewModel, PerfArea.Hud, PerfArea.Ragdolls);
        return p;
    }

    private static byte[] BuildDepths()
    {
        var d = new byte[Count];
        for (var i = 0; i < Count; i++)
            for (var a = Parent[i]; a >= 0; a = Parent[a])
                d[i]++;
        return d;
    }

    // ---- This frame ---------------------------------------------------------------------------------------------

    private static readonly long[] Acc = new long[Count]; // ticks this frame, inclusive of children
    private static readonly long[] Longest = new long[Count]; // the longest single scope this frame
    private static readonly int[] Calls = new int[Count];
    private static readonly PerfArea[] Stack = new PerfArea[MaxDepth];
    private static int _depth, _overflow;

    /// <summary>Scopes that ended out of order (a scope left open, or closed twice). Zero in a healthy build.</summary>
    public static int Misnested;

    /// <summary>The parent each area was last seen opening under (-1 a root, -2 never seen): the self-test checks it against <see cref="Parent"/>.</summary>
    public static readonly sbyte[] SeenParent = NewSeen();

    private static sbyte[] NewSeen()
    {
        var s = new sbyte[Count];
        Array.Fill(s, (sbyte)-2);
        return s;
    }

    /// <summary>Open scopes right now (0 between frames).</summary>
    public static int OpenScopes => _depth + _overflow;

    /// <summary>A timed block; dispose it (a <c>using</c>) to end it. Default (off) does nothing.</summary>
    public readonly ref struct Scope
    {
        private readonly long _t0;
        private readonly PerfArea _area;

        internal Scope(PerfArea area, long t0)
        {
            _area = area;
            _t0 = t0;
        }

        public void Dispose()
        {
            if (_t0 != 0)
                End(_area, _t0);
        }
    }

    /// <summary>A render pass: its CPU time, and on dispose a GPU timestamp marking the pass's end (<see cref="PerfGpu"/>).</summary>
    public readonly ref struct PassScope
    {
        private readonly long _t0;
        private readonly PerfArea _area;
        private readonly PerfGpuPass _pass;

        internal PassScope(PerfArea area, PerfGpuPass pass, long t0)
        {
            _area = area;
            _pass = pass;
            _t0 = t0;
        }

        public void Dispose()
        {
            if (_t0 == 0)
                return;
            End(_area, _t0);
            Gpu?.Mark(_pass);
        }
    }

    /// <summary>Times the block until the returned scope is disposed (nothing when <see cref="On"/> is off).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Scope Measure(PerfArea area) => On ? new Scope(area, Begin(area)) : default;

    /// <summary>Turns timing on or off from the main thread (the one whose scopes count).</summary>
    public static void SetOn(bool on)
    {
        MainThread = Environment.CurrentManagedThreadId;
        On = on;
    }

    /// <summary>Times a render pass on the CPU and marks its end on the GPU timer (nothing when <see cref="On"/> is off).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static PassScope Pass(PerfArea area, PerfGpuPass pass) => On ? new PassScope(area, pass, Begin(area)) : default;

    /// <summary>The frame's GPU pass timer (made by the game once GL is up; null headless).</summary>
    public static PerfGpu? Gpu;

    /// <summary>The thread scopes count on (the main loop's; set when timing is turned on). Scopes on other threads do nothing.</summary>
    public static int MainThread = -1;

    private static long Begin(PerfArea area)
    {
        if (Environment.CurrentManagedThreadId != MainThread)
            return 0; // a worker (a nav bake, a map load task): not ours to time, and the stack is the main thread's
        var i = (int)area;
        if (_depth < MaxDepth)
        {
            SeenParent[i] = _depth == 0 ? (sbyte)-1 : (sbyte)Stack[_depth - 1];
            Stack[_depth++] = area;
        }
        else
            _overflow++;
        var t = Stopwatch.GetTimestamp();
        return t == 0 ? 1 : t;
    }

    private static void End(PerfArea area, long t0)
    {
        var d = Stopwatch.GetTimestamp() - t0;
        var i = (int)area;
        Acc[i] += d;
        Calls[i]++;
        if (d > Longest[i])
            Longest[i] = d;
        if (_overflow > 0)
        {
            _overflow--;
            return;
        }
        if (_depth > 0 && Stack[_depth - 1] == area)
        {
            _depth--;
            return;
        }
        // Out of order: unwind to this area if it's open (the ones above it were left open), else ignore the end.
        Misnested++;
        for (var k = _depth - 1; k >= 0; k--)
            if (Stack[k] == area)
            {
                _depth = k;
                return;
            }
    }

    // ---- History ------------------------------------------------------------------------------------------------

    private static readonly float[] FrameMsRing = new float[History];
    private static readonly float[] AreaMsRing = new float[History * Count];
    private static readonly float[] TickWorstRing = new float[History];
    private static readonly byte[] TicksRing = new byte[History];
    private static readonly float[] MusicMsRing = new float[History];
    private static int _head, _filled;
    private static long _lastFrame;
    private static long _lastMusicTicks = -1;

    /// <summary>The music thread's mixing ticks so far (<see cref="MusicPlayer.RenderTicks"/>), read once per frame while on.</summary>
    public static Func<long>? MusicTicks;

    /// <summary>Frames in the history (up to <see cref="History"/>).</summary>
    public static int Filled => _filled;

    /// <summary>Index of the frame <paramref name="back"/> frames ago in the rings (0: the newest).</summary>
    public static int Slot(int back) => ((_head - 1 - back) % History + History) % History;

    public static float FrameMs(int back) => FrameMsRing[Slot(back)];

    public static float AreaMs(int back, PerfArea area) => AreaMsRing[Slot(back) * Count + (int)area];

    public static float AreaMs(int back, int area) => AreaMsRing[Slot(back) * Count + area];

    /// <summary>The longest single server tick in that frame (the 8 ms budget is per tick).</summary>
    public static float TickWorstMs(int back) => TickWorstRing[Slot(back)];

    /// <summary>Server ticks run in that frame.</summary>
    public static int Ticks(int back) => TicksRing[Slot(back)];

    /// <summary>Music thread mixing time that landed in that frame.</summary>
    public static float MusicMs(int back) => MusicMsRing[Slot(back)];

    private static bool _wasOn;

    /// <summary>
    /// Ends a frame (once per main-loop pass, outside every scope): the frame time always (one timestamp), and while
    /// <see cref="On"/> every area's time, the server ticks, the music thread and the counters.
    /// </summary>
    /// <param name="testFrameMs">Self-test: record this frame time instead of the measured one.</param>
    public static void EndFrame(float testFrameMs = -1)
    {
        var now = Stopwatch.GetTimestamp();
        var frameMs = testFrameMs >= 0 ? testFrameMs : _lastFrame == 0 ? 0f : (float)((now - _lastFrame) * MsPerTick);
        _lastFrame = now;
        if (_depth != 0 || _overflow != 0)
        {
            Misnested++; // a scope was left open over the frame's end
            _depth = _overflow = 0;
        }
        if (On && !_wasOn)
        {
            Array.Clear(AreaMsRing); // the area history starts when the window opens (the frame times are kept)
            Array.Clear(TickWorstRing);
            Array.Clear(TicksRing);
            Array.Clear(MusicMsRing);
            _lastMusicTicks = -1;
            PerfCounters.Reset();
        }
        _wasOn = On;
        if (Paused)
        {
            ClearFrame();
            PerfCounters.EndFrame(-1);
            return;
        }
        FrameMsRing[_head] = frameMs;
        if (On)
        {
            var b = _head * Count;
            for (var i = 0; i < Count; i++)
                AreaMsRing[b + i] = (float)(Acc[i] * MsPerTick);
            TickWorstRing[_head] = (float)(Longest[(int)PerfArea.ServerTick] * MsPerTick);
            TicksRing[_head] = (byte)Math.Min(Calls[(int)PerfArea.ServerTick], 255);
            var music = MusicTicks?.Invoke() ?? 0;
            MusicMsRing[_head] = _lastMusicTicks < 0 ? 0f : (float)((music - _lastMusicTicks) * MsPerTick);
            _lastMusicTicks = music;
            PerfCounters.EndFrame(_head);
        }
        else
            PerfCounters.EndFrame(-1);
        ClearFrame();
        _head = (_head + 1) % History;
        if (_filled < History)
            _filled++;
    }

    private static void ClearFrame()
    {
        Array.Clear(Acc);
        Array.Clear(Longest);
        Array.Clear(Calls);
    }

    /// <summary>Self-test: forget everything (history, nesting state, counters).</summary>
    public static void ResetForTest()
    {
        ClearFrame();
        Array.Clear(FrameMsRing);
        Array.Clear(AreaMsRing);
        Array.Clear(TickWorstRing);
        Array.Clear(TicksRing);
        Array.Clear(MusicMsRing);
        Array.Fill(SeenParent, (sbyte)-2);
        _head = _filled = _depth = _overflow = Misnested = 0;
        _lastFrame = 0;
        _lastMusicTicks = -1;
        _wasOn = On;
        Paused = false;
        PerfCounters.Reset();
    }

    /// <summary>This frame so far (before <see cref="EndFrame"/>): an area's inclusive milliseconds (self-test).</summary>
    public static double OpenFrameMs(PerfArea area) => Acc[(int)area] * MsPerTick;

    /// <summary>This frame so far: how many scopes of an area ended (self-test).</summary>
    public static int OpenFrameCalls(PerfArea area) => Calls[(int)area];
}
