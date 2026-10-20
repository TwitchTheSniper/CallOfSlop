using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The game's presentation scene: one <see cref="Scene"/> holding the first-person viewmodel, a rig per player id, per
/// kill-streak entity id and for the current map's movers. Program and the render helpers call the Draw* methods at the
/// same points they used to draw immediately, so the draw order between systems is unchanged; each call poses one rig,
/// updates the dirty transforms, flattens the rig into a draw list and submits it.
///
/// Switch it off (fall back to the old immediate drawing) with the environment variable CALLOFSLOP_SCENEGRAPH=0.
/// Rigs are created on first use and reused; nothing allocates per frame once they exist.
/// </summary>
public static class ScenePresentation
{
    /// <summary>Draw through the scene graph (true) or the old immediate-mode code (false).</summary>
    public static bool Enabled = Environment.GetEnvironmentVariable("CALLOFSLOP_SCENEGRAPH") != "0";

    public static readonly Scene Scene = new();
    private static readonly DrawList List = new();
    private static ViewModelRig? _vm;
    private static readonly PlayerRig?[] Players = new PlayerRig?[256];
    private static readonly TurretRig?[] Turrets = new TurretRig?[256];
    private static readonly DroneRig?[] Drones = new DroneRig?[256];
    private static MoverRigSet? _movers;

    // ---- Stats (per frame, averaged between reads of Stats) ----
    private static long _ticks, _worstTicks, _frameTicks;
    private static int _frames, _nodes, _draws, _frameNodes, _frameDraws, _peakDraws;

    /// <summary>Call once at the start of each rendered 3D frame (closes the previous frame's stats).</summary>
    public static void BeginFrame()
    {
        if (_frameDraws > 0 || _frameTicks > 0)
        {
            _frames++;
            _ticks += _frameTicks;
            _nodes += _frameNodes;
            _draws += _frameDraws;
            _worstTicks = Math.Max(_worstTicks, _frameTicks);
            _peakDraws = Math.Max(_peakDraws, _frameDraws);
        }
        _frameTicks = 0;
        _frameNodes = _frameDraws = 0;
    }

    /// <summary>One line for the smoke log: average nodes updated, draws and CPU per frame since the last read.</summary>
    public static string Stats
    {
        get
        {
            var f = Math.Max(_frames, 1);
            var s = $"{(Enabled ? "on" : "OFF (immediate mode)")}, {Scene.CountNodes()} nodes in {Scene.Roots.Count} rigs, per frame: {_nodes / f} updated, "
                    + $"{_draws / f} draws (peak {_peakDraws}), pose+update+flatten {_ticks * 1e6 / Stopwatch.Frequency / f:0} us avg, {_worstTicks * 1e6 / Stopwatch.Frequency:0} us worst "
                    + $"over {_frames} frames";
            _frames = _nodes = _draws = _peakDraws = 0;
            _ticks = _worstTicks = 0;
            return s;
        }
    }

    private static void Draw(Renderer g, SceneNode root, long t0)
    {
        _frameNodes += Scene.UpdateTransforms();
        List.Clear();
        List.Flatten(root);
        _frameTicks += Stopwatch.GetTimestamp() - t0;
        _frameDraws += List.Count;
        List.Submit(g);
    }

    /// <summary>The first-person gun and arms, in camera space (the caller has set the viewmodel camera).</summary>
    public static void DrawViewModel(Renderer g, ViewModelAnimator anim, WeaponDef def, float cutaway, bool flash, float time, in ArmColors colors)
    {
        var t0 = Stopwatch.GetTimestamp();
        var vm = _vm;
        if (vm == null)
        {
            vm = _vm = new ViewModelRig();
            Scene.AddRoot(vm.Camera);
        }
        vm.Pose(anim, def, cutaway, flash, time, colors);
        Draw(g, vm.Camera, t0);
    }

    /// <summary>Another player's body, gun and arms (or corpse).</summary>
    public static void DrawPlayer(Renderer g, Player p, double time)
    {
        var t0 = Stopwatch.GetTimestamp();
        var rig = Players[p.Id] ??= NewPlayer(p.Id);
        rig.Pose(p, time);
        Draw(g, rig.Root, t0);
    }

    private static PlayerRig NewPlayer(byte id)
    {
        var r = new PlayerRig(id);
        Scene.AddRoot(r.Root);
        return r;
    }

    /// <summary>A sentry turret or attack drone.</summary>
    public static void DrawStreakEntity(Renderer g, StreakEntity e, Vector3 stripe, bool friendly, double now)
    {
        var t0 = Stopwatch.GetTimestamp();
        if (e.Kind == StreakId.SentryTurret)
        {
            var rig = Turrets[e.Id];
            if (rig == null)
            {
                rig = Turrets[e.Id] = new TurretRig();
                Scene.AddRoot(rig.Base);
            }
            rig.Pose(e, stripe, friendly, now);
            Draw(g, rig.Base, t0);
        }
        else
        {
            var rig = Drones[e.Id];
            if (rig == null)
            {
                rig = Drones[e.Id] = new DroneRig();
                Scene.AddRoot(rig.Root);
            }
            rig.Pose(e, stripe, friendly, now);
            Draw(g, rig.Root, t0);
        }
    }

    /// <summary>The map's movers at the (fractional) server tick. Returns the number of boxes drawn.</summary>
    public static int DrawMovers(Renderer g, Map map, double tick)
    {
        var t0 = Stopwatch.GetTimestamp();
        if (_movers == null || !ReferenceEquals(_movers.Map, map) || _movers.Rigs.Length != map.Movers.Count)
        {
            if (_movers != null)
                Scene.RemoveRoot(_movers.Root);
            _movers = new MoverRigSet(map);
            Scene.AddRoot(_movers.Root);
        }
        _movers.Pose(tick);
        Draw(g, _movers.Root, t0);
        return List.Count;
    }
}
