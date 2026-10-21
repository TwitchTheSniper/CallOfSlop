using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The "Slopes" self-test section: the terrain slope rules (<see cref="SlopeRules"/>) in both movers. Walk-up speed against
/// slope angle (walk and sprint), no airborne ticks over crests and down 30 degree slopes, no stopping or sliding on noisy but
/// walkable slopes, steep slopes still slide and cliffs still block, slides are capped and steerable, terrain lips are
/// stepped over (taller banks need a mantle), determinism (replays are identical), a scripted walker along the bots' routes
/// from both team spawns to every hill and the enemy flag on each High Ground map, and a bot match on each of them.
/// </summary>
public static partial class SelfTest
{
    private static readonly IMover[] SlopeMovers = [ClassicMover.Instance, SweptMover.Instance];

    private static void CheckSlopes()
    {
        var previousMap = World.Map;
        var previousMover = World.Mover;
        try
        {
            SlopeSpeedCurves();
            SlopeCrests();
            SlopeNoise();
            SlopeSteep();
            SlopeLips();
            SlopeDeterminism();
            SlopeDiag();
            SlopeRoutes();
            SlopeBotMatches();
        }
        finally
        {
            SlopeNotices();
            World.Mover = previousMover;
            World.SetMap(previousMap);
        }
    }

    private static float SlopeFlat(Vector3 v) => MathF.Sqrt(v.X * v.X + v.Z * v.Z);

    /// <summary>Result lines of this section by group: logged now, and repeated at the end as one CI notice per group.</summary>
    private static readonly List<(string Group, string Text)> SlopeNotes = new();

    private static void SlopeNote(string group, string text)
    {
        Line(text);
        SlopeNotes.Add((group, text));
    }

    /// <summary>
    /// The section's results as GitHub Actions notices (the workflow prints selftest.log, and a line starting with "::notice"
    /// becomes an annotation on the run), so the numbers are visible without the log.
    /// </summary>
    private static void SlopeNotices()
    {
        foreach (var g in SlopeNotes.Select(n => n.Group).Distinct())
            Line($"::notice title=Slopes: {g}::" + string.Join("%0A", SlopeNotes.Where(n => n.Group == g).Select(n => n.Text.Replace("%", "%25").Replace("\r", "").Replace("\n", " "))));
        SlopeNotes.Clear();
    }

    /// <summary>
    /// A 60 x 60 m terrain map that is one plane rising to the north (-Z) at <paramref name="deg"/> degrees, with optional
    /// noise on top. <paramref name="extra"/> adds more features (JSON objects with a leading comma).
    /// </summary>
    private static Map SlopeMap(string name, float deg, float noise = 0, float scale = 1, string extra = "", string rows = "", float cell = 0.5f)
    {
        var t = MathF.Tan(deg * MathF.PI / 180f) * 34f;
        var grid = rows.Length > 0 ? rows
            : FormattableString.Invariant($"{{ \"type\": \"grid\", \"x0\": -34, \"z0\": -34, \"cell\": 68, \"cols\": 2, \"rows\": 2, \"heights\": [{t}, {t}, {-t}, {-t}], \"mode\": \"set\" }}");
        var noiseJson = noise > 0 ? FormattableString.Invariant($", {{ \"type\": \"noise\", \"amplitude\": {noise}, \"scale\": {scale}, \"octaves\": 2, \"seed\": 9 }}") : "";
        var json = "{ \"name\": \"" + name + "\", \"halfX\": 30, \"halfZ\": 30, \"terrain\": { \"cell\": " + cell.ToString(System.Globalization.CultureInfo.InvariantCulture)
                   + ", \"margin\": 4, \"features\": [ "
                   + grid + noiseJson + extra + " ] }, \"ops\": [ { \"op\": \"Spawn\", \"x\": 0, \"z\": 0 } ] }";
        return MapFile.Load(json, name);
    }

    private static MoveState SlopeStand(Map m, float x, float z) => new() { Position = new Vector3(x, m.GroundHeight(x, z), z), Grounded = true };

    /// <summary>Holds <paramref name="b"/> facing <paramref name="yaw"/> for <paramref name="ticks"/> ticks; <paramref name="each"/> sees every tick.</summary>
    private static MoveState SlopeHold(MoveState s, Buttons b, float yaw, int ticks, Action<int, MoveState>? each = null)
    {
        for (var i = 0; i < ticks; i++)
        {
            World.Simulate(ref s, new InputCmd { Buttons = b, Yaw = yaw, SpeedPct = 100 });
            each?.Invoke(i, s);
        }
        return s;
    }

    // ---- Speed against slope angle ------------------------------------------------------------

    private static readonly float[] SlopeAngles = [0, 10, 20, 25, 30, 35, 40, 44, 46, 50];

    private static void SlopeSpeedCurves()
    {
        foreach (var mover in SlopeMovers)
        {
            World.Mover = mover;
            foreach (var sprint in new[] { false, true })
            {
                var nominal = sprint ? World.SprintSpeed : World.WalkSpeed;
                var speeds = new float[SlopeAngles.Length];
                var line = new List<string>();
                for (var k = 0; k < SlopeAngles.Length; k++)
                {
                    var deg = SlopeAngles[k];
                    var m = SlopeMap($"slope {deg}", deg);
                    World.SetMap(m);
                    // Straight up the slope (north) for 3.5 s from the south side; the speed over the last 2 s (uphill progress).
                    var s = SlopeStand(m, 0, 22);
                    var b = Buttons.Forward | (sprint ? Buttons.Sprint : Buttons.None);
                    s = SlopeHold(s, b, 0, 90);
                    var z0 = s.Position.Z;
                    s = SlopeHold(s, b, 0, 120);
                    speeds[k] = (z0 - s.Position.Z) / 2f;
                    line.Add(FormattableString.Invariant($"{deg:0}:{speeds[k]:0.0}"));
                }
                SlopeNote("speed", $"{mover.Mode,-7} {(sprint ? "sprint" : "walk  ")} uphill speed (m/s) by slope (deg): {string.Join("  ", line)}");
                for (var k = 0; k < SlopeAngles.Length; k++)
                {
                    var deg = SlopeAngles[k];
                    var v = speeds[k];
                    if (deg <= SlopeRules.FullSpeedDeg && v < nominal * 0.95f)
                        Fail($"{mover.Mode} {(sprint ? "sprint" : "walk")}: only {v:0.0} m/s up a {deg:0} degree slope (full speed {nominal} up to {SlopeRules.FullSpeedDeg})");
                    if (deg is > SlopeRules.FullSpeedDeg and < SlopeRules.WalkLimitDeg)
                    {
                        if (v < nominal * SlopeRules.SlowAtLimit * 0.9f || v > nominal * 0.99f)
                            Fail($"{mover.Mode} {(sprint ? "sprint" : "walk")}: {v:0.0} m/s up a {deg:0} degree slope (expected between {nominal * SlopeRules.SlowAtLimit:0.0} and {nominal:0.0})");
                        if (k > 0 && v > speeds[k - 1] + 0.1f)
                            Fail($"{mover.Mode}: faster up {deg:0} degrees ({v:0.0}) than up {SlopeAngles[k - 1]:0} ({speeds[k - 1]:0.0})");
                    }
                    if (deg > SlopeRules.WalkLimitDeg && v > 0.05f)
                        Fail($"{mover.Mode} {(sprint ? "sprint" : "walk")}: walked up a {deg:0} degree slope ({v:0.00} m/s; the limit is {SlopeRules.WalkLimitDeg})");
                }
            }
            // Across and down a 35 degree slope: full speed.
            var side = SlopeMap("slope 35 across", 35);
            World.SetMap(side);
            var a = SlopeHold(SlopeStand(side, -20, 0), Buttons.Forward, MathF.PI / 2, 90);
            var ax = a.Position.X;
            a = SlopeHold(a, Buttons.Forward, MathF.PI / 2, 60);
            var down = SlopeHold(SlopeStand(side, 0, -20), Buttons.Forward, MathF.PI, 60);
            var dz = down.Position.Z;
            down = SlopeHold(down, Buttons.Forward, MathF.PI, 60);
            var across = a.Position.X - ax;
            var downhill = down.Position.Z - dz;
            SlopeNote("speed", $"{mover.Mode,-7} 35 degrees: across {across:0.0} m/s, downhill {downhill:0.0} m/s (walking, full speed {World.WalkSpeed})");
            if (across < World.WalkSpeed * 0.95f || downhill < World.WalkSpeed * 0.95f)
                Fail($"{mover.Mode}: slowed walking across ({across:0.0}) or down ({downhill:0.0}) a 35 degree slope");
        }
    }

    // ---- Crests and downhill ----------------------------------------------------------------------

    private static void SlopeCrests()
    {
        // Up 20 degrees to a crest at z = 0, then down 30 degrees (heights from south to north).
        var up = MathF.Tan(20 * MathF.PI / 180f) * 34f;
        var downH = up - MathF.Tan(30 * MathF.PI / 180f) * 34f;
        var rows = FormattableString.Invariant($"{{ \"type\": \"grid\", \"x0\": -34, \"z0\": -34, \"cell\": 34, \"cols\": 3, \"rows\": 3, \"heights\": [{downH}, {downH}, {downH}, {up}, {up}, {up}, 0, 0, 0], \"mode\": \"set\" }}");
        var crest = SlopeMap("crest", 0, rows: rows);
        var plane = SlopeMap("slope 30", 30);
        foreach (var mover in SlopeMovers)
        {
            World.Mover = mover;
            var parts = new List<string>();
            foreach (var sprint in new[] { false, true })
            {
                var b = Buttons.Forward | (sprint ? Buttons.Sprint : Buttons.None);
                World.SetMap(crest);
                var air = 0;
                var firstAir = float.NaN;
                var s = SlopeHold(SlopeStand(crest, 0, 26), b, 0, sprint ? 220 : 480, (_, st) =>
                {
                    if (st.Grounded)
                        return;
                    air++;
                    if (float.IsNaN(firstAir))
                        firstAir = st.Position.Z;
                });
                var crossed = s.Position.Z < -6;
                World.SetMap(plane);
                var airDown = 0;
                var d = SlopeHold(SlopeStand(plane, 0, -25), b, MathF.PI, sprint ? 200 : 420, (_, st) => airDown += st.Grounded ? 0 : 1);
                parts.Add($"{(sprint ? "sprint" : "walk")}: over the crest {air} airborne ticks (first at z {firstAir:0.0}; ended at z {s.Position.Z:0}), down 30 degrees {airDown} (ended at z {d.Position.Z:0})");
                if (!crossed || air > 0)
                    Fail($"{mover.Mode} {(sprint ? "sprint" : "walk")}: {air} airborne tick(s) going over a 20/30 degree crest (crossed it: {crossed})");
                if (airDown > 0 || d.Position.Z < 10)
                    Fail($"{mover.Mode} {(sprint ? "sprint" : "walk")}: {airDown} airborne tick(s) walking down a 30 degree slope (got to z {d.Position.Z:0.0})");
            }
            SlopeNote("feel", $"{mover.Mode,-7} " + string.Join("; ", parts));
        }
    }

    // ---- Noisy but walkable -------------------------------------------------------------------------

    private static void SlopeNoise()
    {
        // 36 degrees with sharp 15 cm noise: about 3% of the heightfield's patches are steeper than 42 degrees, the
        // footprint almost never is. The old single-patch rule stopped dead on every lane of this.
        var m = SlopeMap("noisy 36", 36, 0.15f, 1f);
        World.SetMap(m);
        var t = m.TerrainCollision!;
        int patchSteep = 0, footSteep = 0, samples = 0;
        for (var x = -10f; x < 10f; x += 0.37f)
            for (var z = -20f; z < 20f; z += 0.41f)
            {
                samples++;
                patchSteep += t.NormalAt(x, z).Y < World.MinWalkNormalY ? 1 : 0;
                footSteep += SlopeRules.Walkable(t, x, z) ? 0 : 1;
            }
        SlopeNote("feel", $"noisy 36 degree slope: {patchSteep * 100f / samples:0.0}% of points have a heightfield patch over 42 degrees, {footSteep * 100f / samples:0.00}% a footprint over {SlopeRules.WalkLimitDeg}");
        foreach (var mover in SlopeMovers)
        {
            World.Mover = mover;
            var parts = new List<string>();
            foreach (var lane in new[] { -6f, -2f, 2f, 6f })
            {
                var stops = 0;
                var slides = 0;
                var s = SlopeStand(m, lane, 22);
                var prev = s.Position;
                s = SlopeHold(s, Buttons.Forward, 0, 360, (i, st) =>
                {
                    if (i > 30 && SlopeFlat(st.Position - prev) < 1f * World.TickDt)
                        stops++;
                    if (!st.Grounded)
                        slides++;
                    prev = st.Position;
                });
                var climbed = 22 - s.Position.Z;
                // Then let go: after a second to stop, standing still for 2 s more mustn't move you.
                var stopped = SlopeHold(s, Buttons.None, 0, 60);
                var still = SlopeHold(stopped, Buttons.None, 0, 120);
                var drift = Vector3.Distance(still.Position, stopped.Position);
                parts.Add($"lane {lane:0}: {climbed:0} m, {stops} stopped, {slides} sliding/air, drift standing {drift:0.000} m");
                if (climbed < 20 || stops > 0 || slides > 0)
                    Fail($"{mover.Mode}: stuttered up a noisy 36 degree slope (lane x {lane}: {climbed:0.0} m in 6 s, {stops} stopped ticks, {slides} airborne/sliding)");
                if (drift > 0.05f)
                    Fail($"{mover.Mode}: slid {drift:0.00} m standing on a noisy 36 degree slope");
            }
            SlopeNote("feel", $"{mover.Mode,-7} noisy 36 degrees, 6 s walking up: " + string.Join("; ", parts));
        }
    }

    // ---- Steep ground, cliffs, slides ---------------------------------------------------------------

    private static void SlopeSteep()
    {
        var steep = SlopeMap("slope 50", 50);
        var cliffy = SlopeMap("slope 60", 60);
        var lab = LabMap();
        foreach (var mover in SlopeMovers)
        {
            World.Mover = mover;
            // Standing on 50 degrees: slides down. Walking up it: no progress.
            World.SetMap(steep);
            var start = SlopeStand(steep, 0, 0);
            var stood = SlopeHold(start, Buttons.None, 0, 120);
            var climb = SlopeHold(start, Buttons.Forward | Buttons.Sprint, 0, 180);
            if (stood.Position.Z < start.Position.Z + 1f)
                Fail($"{mover.Mode}: didn't slide down a 50 degree slope (moved {stood.Position.Z - start.Position.Z:0.00} m in 2 s)");
            if (climb.Position.Y > start.Position.Y + 0.1f)
                Fail($"{mover.Mode}: walked {climb.Position.Y - start.Position.Y:0.00} m up a 50 degree slope");

            // Sliding down 60 degrees: capped, steerable sideways, and you can't climb back up.
            World.SetMap(cliffy);
            var top = SlopeStand(cliffy, 0, -24);
            var fastest = 0f;
            var slid = SlopeHold(top, Buttons.None, 0, 180, (_, st) => fastest = MathF.Max(fastest, st.Velocity.Z));
            var steer = SlopeHold(top, Buttons.Right, 0, 120);
            var upward = SlopeHold(top, Buttons.Forward | Buttons.Sprint, 0, 120);
            SlopeNote("feel", $"{mover.Mode,-7} steep: 50 degrees standing slid {stood.Position.Z - start.Position.Z:0.0} m in 2 s, sprinting up gained {climb.Position.Y - start.Position.Y:+0.00;-0.00} m; "
                 + $"60 degrees: fastest slide {fastest:0.0} m/s (cap {SlopeRules.MaxSlideSpeed}), steering right moved {steer.Position.X:0.0} m across in 2 s, holding forward ended {upward.Position.Z - top.Position.Z:+0.0;-0.0} m downhill");
            if (fastest > SlopeRules.MaxSlideSpeed + 0.05f)
                Fail($"{mover.Mode}: slid down 60 degrees at {fastest:0.0} m/s (cap {SlopeRules.MaxSlideSpeed})");
            if (slid.Position.Z < top.Position.Z + 5f)
                Fail($"{mover.Mode}: barely slid down a 60 degree slope ({slid.Position.Z - top.Position.Z:0.0} m in 3 s)");
            if (steer.Position.X < 3f)
                Fail($"{mover.Mode}: couldn't steer across a slide ({steer.Position.X:0.0} m in 2 s)");
            if (upward.Position.Y > top.Position.Y + 0.05f)
                Fail($"{mover.Mode}: climbed a 60 degree slope while sliding");

            // The lab's 6 m cliff still blocks walking and sprinting.
            World.SetMap(lab);
            var foot = SlopeStand(lab, -20, 16);
            var wall = SlopeHold(foot, Buttons.Forward | Buttons.Sprint, 0, 240);
            if (wall.Position.Z < 10.6f || wall.Position.Y > 1.5f)
                Fail($"{mover.Mode}: got up the terrain lab's cliff (z {wall.Position.Z:0.00}, y {wall.Position.Y:0.00})");
        }
    }

    // ---- Terrain lips and banks ---------------------------------------------------------------------

    private static void SlopeLips()
    {
        // Flat ground with a 0.4 m lip (a 58 degree face on a 0.25 m grid) on the west and a 0.9 m bank on the east.
        const string extra = ", { \"type\": \"plateau\", \"x\": -10, \"z\": -12, \"w\": 10, \"d\": 10, \"height\": 0.4, \"edge\": 0.3 }"
                             + ", { \"type\": \"plateau\", \"x\": 10, \"z\": -12, \"w\": 10, \"d\": 10, \"height\": 0.9, \"edge\": 0.3 }";
        var m = SlopeMap("lips", 0, extra: extra, cell: 0.25f);
        World.SetMap(m);
        foreach (var mover in SlopeMovers)
        {
            World.Mover = mover;
            var lip = SlopeHold(SlopeStand(m, -10, 0), Buttons.Forward, 0, 150);
            var bank = SlopeHold(SlopeStand(m, 10, 0), Buttons.Forward, 0, 150);
            var mantled = false;
            var climb = SlopeHold(SlopeStand(m, 10, 0), Buttons.Forward, 0, 150, (i, st) => mantled |= st.MantleTicks > 0);
            climb = SlopeHold(climb, Buttons.Forward | Buttons.Jump, 0, 40, (i, st) => mantled |= st.MantleTicks > 0);
            climb = SlopeHold(climb, Buttons.Forward, 0, 60);
            SlopeNote("feel", $"{mover.Mode,-7} lips: 0.4 m lip walked to y {lip.Position.Y:0.00} z {lip.Position.Z:0.0}; 0.9 m bank stopped at y {bank.Position.Y:0.00} z {bank.Position.Z:0.0}, mantle {(mantled ? "yes" : "no")} to y {climb.Position.Y:0.00}");
            if (lip.Position.Y < 0.35f || lip.Position.Z > -8f)
                Fail($"{mover.Mode}: didn't step over a 0.4 m terrain lip (y {lip.Position.Y:0.00}, z {lip.Position.Z:0.0})");
            if (bank.Position.Y > 0.6f)
                Fail($"{mover.Mode}: walked up a 0.9 m terrain bank (it needs a mantle; got to y {bank.Position.Y:0.00})");
            if (!mantled || climb.Position.Y < 0.85f)
                Fail($"{mover.Mode}: couldn't mantle onto a 0.9 m terrain bank (y {climb.Position.Y:0.00})");
        }
    }

    // ---- Determinism ----------------------------------------------------------------------------------

    private static void SlopeDeterminism()
    {
        var m = SlopeMap("noisy 40", 40, 0.25f, 2f);
        World.SetMap(m);
        foreach (var mover in SlopeMovers)
        {
            World.Mover = mover;
            // A long input stream (turning, sprinting, jumping, crouching) across the slope; then the second half replayed
            // from the state in the middle, as client prediction does after a correction.
            var states = new MoveState[601];
            var cmds = new InputCmd[600];
            var rng = new Random(5);
            var yaw = 0f;
            for (var i = 0; i < cmds.Length; i++)
            {
                yaw += ((float)rng.NextDouble() - 0.5f) * 0.2f;
                var b = Buttons.Forward | (rng.Next(3) == 0 ? Buttons.Sprint : Buttons.None) | (rng.Next(40) == 0 ? Buttons.Jump : Buttons.None)
                        | (i / 60 % 4 == 3 ? Buttons.Left : Buttons.None) | (i / 100 % 5 == 4 ? Buttons.Crouch : Buttons.None);
                cmds[i] = new InputCmd { Seq = (uint)i, Buttons = b, Yaw = yaw, SpeedPct = 100 };
            }
            var s = SlopeStand(m, -15, 15);
            states[0] = s;
            for (var i = 0; i < cmds.Length; i++)
            {
                World.Simulate(ref s, cmds[i]);
                states[i + 1] = s;
            }
            var again = SlopeStand(m, -15, 15);
            for (var i = 0; i < cmds.Length; i++)
                World.Simulate(ref again, cmds[i]);
            var replay = states[300];
            for (var i = 300; i < cmds.Length; i++)
                World.Simulate(ref replay, cmds[i]);
            var same = again.Position == s.Position && again.Velocity == s.Velocity && again.Grounded == s.Grounded
                       && replay.Position == s.Position && replay.Velocity == s.Velocity && replay.Grounded == s.Grounded;
            SlopeNote("feel", $"{mover.Mode,-7} determinism: 600 ticks on a noisy 40 degree slope end at {s.Position} both times and when replayed from tick 300: {(same ? "identical" : "DIFFERENT")}");
            if (!same)
                Fail($"{mover.Mode}: slope movement isn't deterministic (run {s.Position}, again {again.Position}, replay {replay.Position})");
        }
    }

    // TEMP diagnostics: why the mesa top isn't connected.
    private static void SlopeDiag()
    {
        var m = SlopeHighGroundMaps().FirstOrDefault(x => x.Name == "Adobe Mesa");
        if (m == null)
            return;
        World.SetMap(m);
        for (var wait = 0; wait < 4000 && NavGraph.Current == NavGraph.Empty; wait++)
            NavGraph.Pump(50);
        var g = NavGraph.Current;
        var t = m.TerrainCollision!;
        var near = new List<int>();
        g.Within(new Vector3(-14, 0, -12.5f), 9f, near);
        var parts = new List<string>();
        foreach (var i in near.OrderBy(i => g.Nodes[i].X))
        {
            var q = g.Nodes[i];
            parts.Add(FormattableString.Invariant($"({q.X:0.0},{q.Y:0.0},{q.Z:0.0}) c{(g.Component[i] == g.MainComponent ? "M" : g.Component[i].ToString())} d{g.EdgeStart[i + 1] - g.EdgeStart[i]}"));
        }
        SlopeNote("diag", "ramp nodes: " + string.Join(" ", parts));
        ReadOnlySpan<float> wx = [-28f, -23.5f, -19f, -14f, -9.5f, -4f];
        ReadOnlySpan<float> wz = [-17f, -16f, -15f, -12.5f, -10.2f, -5f];
        var lw = new List<string>();
        for (var k = 0; k + 1 < wx.Length; k++)
        {
            var a0 = new Vector3(wx[k], m.GroundHeight(wx[k], wz[k]), wz[k]);
            var a1 = new Vector3(wx[k + 1], m.GroundHeight(wx[k + 1], wz[k + 1]), wz[k + 1]);
            lw.Add($"{k}: line {SlopeRules.LineWalkable(t, a0, a1, true)} straight {World.CanWalkStraight(a0, a1)} clear {NavGraph.Clear(a0, a1)} blocked {World.Blocked(a0 + new Vector3(0, 0.01f, 0))}/{World.Blocked(a1 + new Vector3(0, 0.01f, 0))}");
        }
        SlopeNote("diag", "ramp waypoints: " + string.Join("; ", lw));
        var top = new List<int>();
        g.Within(new Vector3(0, 0, 0), 6f, top);
        SlopeNote("diag", "top nodes: " + string.Join(" ", top.Select(i => FormattableString.Invariant($"({g.Nodes[i].X:0.0},{g.Nodes[i].Y:0.0},{g.Nodes[i].Z:0.0}) c{(g.Component[i] == g.MainComponent ? "M" : g.Component[i].ToString())}"))));
    }

    // ---- High Ground routes -----------------------------------------------------------------------------

    private struct SlopeRun
    {
        public bool Reached;
        public float Length, Time, Limit;
        public int Airborne, Stalls;
        public Vector3 End;
    }

    /// <summary>
    /// A scripted walker (a player holding forward, steering like a bot) from <paramref name="from"/> to <paramref name="to"/>
    /// along the nav graph's route, pressing jump at mantle edges and after a second without progress.
    /// </summary>
    private static SlopeRun SlopeFollow(NavGraph nav, NavSearch search, Vector3 from, Vector3 to, bool sprint)
    {
        var r = new SlopeRun { Length = -1 };
        var path = new int[1024];
        var a = nav.Nearest(from);
        var b = nav.Nearest(to, needSight: false);
        var n = a < 0 || b < 0 ? 0 : search.Find(nav, a, b, path, null, 100000);
        if (n == 0)
            return r;
        var len = SlopeFlat(nav.Nodes[path[0]] - from) + SlopeFlat(to - nav.Nodes[path[n - 1]]);
        for (var i = 1; i < n; i++)
            len += Vector3.Distance(nav.Nodes[path[i - 1]], nav.Nodes[path[i]]);
        r.Length = len;
        var speed = sprint ? World.SprintSpeed : World.WalkSpeed;
        r.Limit = len / (speed * 0.5f) + 8f;
        var maxTicks = (int)(r.Limit * World.TickRate);
        var s = new MoveState { Position = from, Grounded = true };
        var idx = 0;
        var check = s.Position;
        var jump = false;
        var stuckRun = 0;
        var ticks = 0;
        for (; ticks < maxTicks; ticks++)
        {
            var pos = s.Position;
            while (idx < n)
            {
                var q = nav.Nodes[path[idx]];
                var dy = q.Y - pos.Y;
                if (SlopeFlat(q - pos) < 0.8f && dy < 1.0f && dy > -2.5f)
                    idx++;
                else
                    break;
            }
            if (idx >= n && SlopeFlat(to - pos) < 1.5f)
            {
                r.Reached = true;
                break;
            }
            var aim = idx < n ? nav.Nodes[path[idx]] : to;
            var mantle = false;
            if (idx > 0 && idx < n)
                for (var e = nav.EdgeStart[path[idx - 1]]; e < nav.EdgeStart[path[idx - 1] + 1]; e++)
                    if (nav.EdgeTo[e] == path[idx] && (nav.EdgeFlags[e] & NavGraph.FlagMantle) != 0)
                    {
                        var ledge = nav.EdgeLedge[e];
                        mantle = SlopeFlat(ledge - pos) < 1.4f && ledge.Y > pos.Y + World.StepHeight;
                    }
            var yaw = MathF.Atan2(aim.X - pos.X, -(aim.Z - pos.Z));
            var keys = Buttons.Forward | (sprint ? Buttons.Sprint : Buttons.None) | (mantle || jump ? Buttons.Jump : Buttons.None);
            jump = false;
            World.Simulate(ref s, new InputCmd { Buttons = keys, Yaw = yaw, SpeedPct = 100 });
            if (!s.Grounded && s.MantleTicks == 0)
                r.Airborne++;
            if (ticks % World.TickRate == World.TickRate - 1)
            {
                if (SlopeFlat(s.Position - check) < 1f)
                {
                    r.Stalls++;
                    jump = true;
                    // Stuck twice in a row: re-plan from here, like a bot (around whatever stopped it).
                    var here = nav.Nearest(s.Position);
                    if (++stuckRun >= 2 && here >= 0)
                    {
                        int bf = idx > 0 && idx < n ? path[idx - 1] : -1, bt = idx < n ? path[idx] : -1;
                        var replanned = search.Find(nav, here, b, path, (f, t, _) => f == bf && t == bt ? float.PositiveInfinity : 0f, 100000);
                        if (replanned > 0)
                        {
                            n = replanned;
                            idx = 0;
                        }
                        stuckRun = 0;
                    }
                }
                else
                    stuckRun = 0;
                check = s.Position;
            }
        }
        r.Time = ticks / (float)World.TickRate;
        r.End = s.Position;
        return r;
    }

    /// <summary>The team's spawn nearest the middle of its spawn cluster.</summary>
    private static Vector3 SlopeTeamStart(List<Vector3> spawns)
    {
        var c = Vector3.Zero;
        foreach (var p in spawns)
            c += p;
        c /= spawns.Count;
        return spawns.MinBy(p => SlopeFlat(p - c));
    }

    private static List<Map> SlopeHighGroundMaps() => Maps.FindPack(HighGroundPack)?.Maps.ToList() ?? [];

    private static void SlopeRoutes()
    {
        var search = new NavSearch();
        var runs = new (IMover Mover, bool Sprint)[] { (ClassicMover.Instance, false), (ClassicMover.Instance, true), (SweptMover.Instance, true) };
        foreach (var m in SlopeHighGroundMaps())
        {
            World.SetMap(m);
            for (var wait = 0; wait < 4000 && NavGraph.Current == NavGraph.Empty; wait++)
                NavGraph.Pump(50);
            var nav = NavGraph.Current;
            var o = m.Objectives;
            var goals = new List<(string Name, Vector3 At)>();
            for (var i = 0; i < o.Hills.Count; i++)
                goals.Add(($"hill {i + 1}", o.Hills[i].Center));
            var parts = new List<string>();
            int total = 0, reached = 0;
            foreach (var (mover, sprint) in runs)
            {
                World.Mover = mover;
                var times = new List<string>();
                for (var team = 0; team < 2; team++)
                {
                    var from = SlopeTeamStart(o.TeamSpawns[team]);
                    foreach (var (name, at) in goals.Append(("enemy flag", o.Flags[1 - team])))
                    {
                        var r = SlopeFollow(nav, search, from, at, sprint);
                        total++;
                        var who = $"{(team == 0 ? "Blue" : "Red")} to {name}";
                        if (r.Length < 0)
                        {
                            Fail($"{m.Name}: no route for {who}");
                            continue;
                        }
                        if (r.Reached)
                            reached++;
                        times.Add(FormattableString.Invariant($"{(team == 0 ? "B" : "R")}>{name.Replace("hill ", "h").Replace("enemy flag", "flag")} {r.Length:0}m/{(r.Reached ? $"{r.Time:0.0}s" : $"STUCK@{r.End.X:0},{r.End.Y:0},{r.End.Z:0}")}{(r.Stalls > 0 ? $" {r.Stalls}st" : "")}{(r.Airborne > 30 ? $" {r.Airborne}air" : "")}"));
                        if (!r.Reached)
                            Fail($"{m.Name}: {mover.Mode} {(sprint ? "sprinting" : "walking")} {who} ({r.Length:0} m) didn't get there in {r.Limit:0} s: stopped at ({r.End.X:0.0}, {r.End.Y:0.0}, {r.End.Z:0.0}), {r.Stalls} stalled seconds");
                    }
                }
                parts.Add($"  {mover.Mode,-7} {(sprint ? "sprint" : "walk  ")}: {string.Join("  ", times)}");
            }
            SlopeNote("routes", $"{m.Name}: scripted walks along the bots' routes, {reached}/{total} arrived");
            foreach (var p in parts)
                SlopeNote("routes", p);
        }
        World.Mover = ClassicMover.Instance;
    }

    // ---- High Ground bot matches ------------------------------------------------------------------------

    private static void SlopeBotMatches()
    {
        World.Mover = ClassicMover.Instance;
        foreach (var m in SlopeHighGroundMaps())
        {
            using var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id,
                bots: Game.ClassicPlayers - 1, botSkill: BotDifficulty.Normal, mapId: m.Id);
            game.MapVoteEnabled = false;
            var t = m.TerrainCollision!;
            const double dt = 1.0 / World.TickRate;
            var now = 0.0;
            var travel = new Dictionary<byte, float>();
            var last = new Dictionary<byte, Vector3>();
            var unstick = new Dictionary<byte, int>();
            var prevStuck = new Dictionary<byte, int>();
            var cells = new HashSet<(int, int)>();
            int slideTicks = 0, slopeSlow = 0, botSeconds = 0, bunched = 0, samples = 0;
            for (var i = 0; i < 60 * World.TickRate; i++)
            {
                now += dt;
                game.Update(now, dt);
                foreach (var p in game.Players.Values)
                {
                    if (p.Bot is not { } brain || !p.Alive)
                        continue;
                    var st = brain.StuckCount;
                    if (st > prevStuck.GetValueOrDefault(p.Id))
                        unstick[p.Id] = unstick.GetValueOrDefault(p.Id) + 1;
                    prevStuck[p.Id] = st;
                    var gap = p.Position.Y - t.HeightAt(p.Position.X, p.Position.Z);
                    if (!p.Sim.Grounded && p.Sim.MantleTicks == 0 && gap < 0.3f && !SlopeRules.Walkable(t, p.Position.X, p.Position.Z))
                        slideTicks++;
                }
                if (i % 60 != 0)
                    continue;
                var alive = game.Players.Values.Where(p => p.Bot != null && p.Alive).ToList();
                foreach (var p in alive)
                {
                    var moved = 0f;
                    if (last.TryGetValue(p.Id, out var lp))
                    {
                        moved = Vector3.Distance(lp, p.Position);
                        travel[p.Id] = travel.GetValueOrDefault(p.Id) + moved;
                        botSeconds++;
                        // A second on a slope (over 20 degrees) covering under 1 m.
                        if (moved < 1f && t.NormalAt(p.Position.X, p.Position.Z).Y < 0.94f && p.Position.Y - t.HeightAt(p.Position.X, p.Position.Z) < 0.3f)
                            slopeSlow++;
                    }
                    last[p.Id] = p.Position;
                    cells.Add(((int)MathF.Floor(p.Position.X / 6f), (int)MathF.Floor(p.Position.Z / 6f)));
                }
                // Bunching: pairs of bots within 2.5 m of each other.
                for (var a = 0; a < alive.Count; a++)
                    for (var b = a + 1; b < alive.Count; b++)
                        if (SlopeFlat(alive[a].Position - alive[b].Position) < 2.5f)
                            bunched++;
                samples++;
            }
            var lowTravel = travel.Count(kv => kv.Value < 15f);
            var stuck = travel.Count(kv => kv.Value < 15f && unstick.GetValueOrDefault(kv.Key) >= 3);
            var cover = cells.Count * 36f / (4 * m.HalfX * m.HalfZ) * 100f;
            var stalls = game.Players.Values.Where(p => p.Bot != null).Sum(p => p.Bot!.SlopeStalls);
            var kills = game.Players.Values.Sum(p => (int)p.Kills);
            SlopeNote("bots", $"{m.Name,-12} bots 60 s: kills {kills}, avg travel {(travel.Count > 0 ? travel.Values.Average() : 0):0} m, stuck {stuck} (under 15 m: {lowTravel}), "
                 + $"unstick tries {unstick.Values.Sum()}, slope re-plans {stalls}, sliding {slideTicks} bot-ticks, slow on slopes {slopeSlow}/{botSeconds} bot-seconds, "
                 + $"map covered {cover:0}% (6 m cells), bunched pairs {bunched / (float)Math.Max(1, samples):0.00} per second");
            if (stuck > 0)
                Fail($"{m.Name}: {stuck} bot(s) stuck (under 15 m in 60 s and still trying to move)");
        }
        World.SetMap(Maps.All[0]);
    }
}
