using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Self-test section "Horde wake waves" (docs/zombies/MASS-HORDE.md, "Wake waves"; board item dev-horde-wake-waves-hw01):
/// the mass horde wakes without rows of zombies rising out of the ground. A scripted advance on Horde Stress's valley
/// floor (a blob of 6,000 in the mass 60-110 m ahead of the player, round 10's pace, the host drawing every tick from the
/// player's eye): the releases out of the mass come from the blob's edge facing the player first (the cell towards the
/// player empty) and spread over ticks (at most massPerCell from a cell a tick); every drawn zombie's place moves no more
/// than a small step from one frame to the next as it changes tier (each tracked by its drawn look, agent or particle);
/// no particle is drawn under the ground and nothing else is but agents genuinely rising or sinking; the counts add up;
/// no allocation. Then the client's side: an individual taking over a drawn agent (hidden while its cell still counts
/// it, swapped at the cell's back in a cell drawn at its cap: nothing rises) and one going back carrying its yaw and walk.
/// HORDE_WAKE_COMPARE=1 also runs the advance the old way (softWake off) and logs both.
/// </summary>
public static partial class SelfTest
{
    private static void CheckHordeWaves()
    {
        if (Maps.Find("Horde Stress") is not { Zombies: { Stress: { } st }, Terrain: { } terrain } map)
        {
            Fail("no Horde Stress map (with terrain and a zombies \"stress\" section)");
            return;
        }
        var compare = Environment.GetEnvironmentVariable("HORDE_WAKE_COMPARE") == "1";
        var old = compare ? WavesScene(map, st, terrain, soft: false) : null;
        var r = WavesScene(map, st, terrain, soft: true);
        if (old != null)
            Line($"horde wake waves, before (softWake off): {old.Text}");
        Line($"horde wake waves: {r.Text}");
        if (r.Released < 300)
            Fail($"wake waves: the advance should let a good part of the blob out of the mass ({r.Released} released)");
        if (r.FrontShare < 0.7f || r.DeepShare > 0.1f)
            Fail($"wake waves: the releases should come from the blob's edge facing the player ({r.FrontShare * 100:0}% with the cell towards the player empty, {r.DeepShare * 100:0}% more than {WavesDeep} m inside the blob's nearest mass)");
        if (r.PerCellTick > 2)
            Fail($"wake waves: a cell should drain over several ticks (up to {r.PerCellTick} let out of one cell in a tick)");
        if (r.TierChanges < 100 || r.MergeChanges < 20 || r.TierJump > WavesJump)
            Fail($"wake waves: no drawn zombie should jump as it changes tier ({r.TierChanges} changes tracked, {r.MergeChanges} of them merging back, the largest step {r.TierJump:0.000} m in a frame; at most {WavesJump} m)");
        if (r.HandOvers < r.Released / 2)
            Fail($"wake waves: the released zombies in view should take over their drawn agents ({r.HandOvers} of {r.Released})");
        if (r.BelowExtra > 0)
            Fail($"wake waves: no zombie should be drawn below the ground but agents genuinely rising or sinking ({r.BelowExtra} instances more than the agents fading)");
        if (r.FrontFading > Math.Max(60, r.HandOvers / 4))
            Fail($"wake waves: nothing should rise out of the ground or sink into it at the blob's front as it wakes ({r.FrontFading} agent-frames rising or sinking within {WavesFront} m of its front, over {r.HandOvers} hand-overs)");
        if (r.Bad > 0)
            Fail($"wake waves: the counts should add up on every tick ({r.Bad} ticks off: {r.BadText})");
        if (r.Alloc != 0)
            Fail($"wake waves: the crowd's ticks and the drawing should allocate nothing ({r.Alloc} B over 0.5 s)");
        WavesClientSide();
    }

    /// <summary>A release this far (m) past the nearest mass to the player is deep inside the blob.</summary>
    private const float WavesDeep = 8f;

    /// <summary>The blob's front: its nearest this many m to the player.</summary>
    private const float WavesFront = 12f;

    /// <summary>The most a drawn zombie may move in a frame (1/60 s) as it changes tier: a sprinter's step and the hand-over's ease.</summary>
    private const float WavesJump = 0.2f;

    private sealed class WavesResult
    {
        public int Released, HandOvers, PerCellTick, TierChanges, MergeChanges, BelowExtra, Bad;
        public float FrontShare, DeepShare, TierJump, AgentJump;
        public long Faded, FrontFading, Alloc = -1;
        public string Text = "", BadText = "";
    }

    private static WavesResult WavesScene(Map map, ZombiesStress st, TerrainData terrain, bool soft)
    {
        var now = 0.0;
        using var g = ZGame(0, ref now, tweak: r =>
        {
            r.FirstRoundDelay = 9999;
            r.Crowd.IndividualCap = 120;
            r.Crowd.SoftWake = soft;
        }, onMap: map, setup: g => g.StressSize = 600);
        var me = g.Local!;
        me.ImmuneUntil = 1e9;
        var at = new Vector3(st.Rally.X, map.GroundHeight(st.Rally.X, st.Rally.Y) + 0.05f, st.Rally.Y);
        me.Teleport(at);
        me.Yaw = MathF.PI / 2; // (looking along +x, at the blob)
        me.Pitch = 0;
        g.SelfTestBeginRound(10, now); // (round 10's pace: the blob comes on at runners' and sprinters' speed)
        g.SelfTestCrowdScene(c =>
        {
            c.Reserve(12000);
            c.SeedMass(at.X + 85, at.Z, 6000, c.Tuning.MassCapacity, 26f);
        });
        var c = g.Crowd;
        var res = new WavesResult();
        var eye = at + new Vector3(0, World.EyeHeight, 0);
        ZombieAnim.SetFrustum(Matrix4x4.CreateLookAt(eye, eye + new Vector3(40, -3, 0), Vector3.UnitY) * Matrix4x4.CreatePerspectiveFieldOfView(1.1f, 16f / 9f, 0.05f, 900));
        CrowdJobs.Serial = true;
        var view = new CrowdView();
        var prev = new Dictionary<uint, (float X, float Z, bool Particle)>();
        var cur = new Dictionary<uint, (float X, float Z, bool Particle)>();
        var dup = new HashSet<uint>();
        var perCell = new Dictionary<int, int>();
        var notes = c.Notes;
        int front = 0, deep = 0;
        long faded0 = -1;
        try
        {
            // 9 s standing (the blob comes on and wakes), then 4 s walking away down the valley at 7 m/s (the far side of the
            // awake crowd falls behind and merges back into the mass).
            var stand = (int)(9 * World.TickRate);
            var ticks = stand + (int)(4 * World.TickRate);
            for (var t = 0; t < ticks; t++)
            {
                if (t >= stand)
                {
                    at.X -= 7f * World.TickDt;
                    at.Y = map.GroundHeight(at.X, at.Z) + 0.05f;
                    me.Teleport(at);
                    eye = at + new Vector3(0, World.EyeHeight, 0);
                    ZombieAnim.SetFrustum(Matrix4x4.CreateLookAt(eye, eye + new Vector3(40, -3, 0), Vector3.UnitY) * Matrix4x4.CreatePerspectiveFieldOfView(1.1f, 16f / 9f, 0.05f, 900));
                }
                // The nearest mass to the player before the tick (how deep into the blob a release is).
                var nearMass = float.MaxValue;
                for (var k = 0; k < c.MassCount.Length; k++)
                    if (c.MassCount[k] > 0)
                    {
                        var m = c.MassCentre(k);
                        nearMass = MathF.Min(nearMass, Vector2.Distance(new Vector2(m.X, m.Z), new Vector2(at.X, at.Z)));
                    }
                var measure = t >= stand - 30 && t < stand;
                var a0 = measure ? GC.GetAllocatedBytesForCurrentThread() : 0;
                now += World.TickDt;
                g.SelfTestZombiesTick(now);
                view.Prepare(g, eye, now, 0);
                if (measure)
                    res.Alloc = (res.Alloc < 0 ? 0 : res.Alloc) + GC.GetAllocatedBytesForCurrentThread() - a0;
                if (c.Total + g.AliveZombies + g.CrowdRoundKills != g.CrowdRoundTotal && res.Bad++ == 0)
                    res.BadText = $"crowd {c.Count} + mass {c.MassTotal} + alive {g.AliveZombies} + killed {g.CrowdRoundKills} != {g.CrowdRoundTotal}";
                // This tick's releases: where they came from.
                perCell.Clear();
                for (var k = Math.Max(notes, c.Notes - HordeCrowd.NoteCap); k < c.Notes; k++)
                {
                    var j = (int)(k & (HordeCrowd.NoteCap - 1));
                    if (c.NoteKind[j] != 2)
                        continue;
                    float x = c.NoteX[j], z = c.NoteZ[j];
                    res.Released++;
                    var col = Math.Clamp((int)((x - c.MinX) / c.MassCell), 0, c.MassCols - 1);
                    var row = Math.Clamp((int)((z - c.MinZ) / c.MassCell), 0, c.MassRows - 1);
                    if (soft)
                        (col, row) = ((int)(c.NoteLook[j] % (uint)c.MassCols), (int)(c.NoteLook[j] / (uint)c.MassCols)); // (the soft wake notes the cell)
                    var cell = row * c.MassCols + col;
                    perCell[cell] = perCell.GetValueOrDefault(cell) + 1;
                    var mc = c.MassCentre(cell);
                    float bx = at.X - mc.X, bz = at.Z - mc.Z;
                    var d = MathF.Sqrt(bx * bx + bz * bz);
                    int dc = MathF.Abs(bx) > 0.38f * d ? Math.Sign(bx) : 0, dr = MathF.Abs(bz) > 0.38f * d ? Math.Sign(bz) : 0;
                    int ux = col + dc, uz = row + dr;
                    var u = uz * c.MassCols + ux;
                    if (d <= c.MassCell || (uint)ux >= (uint)c.MassCols || (uint)uz >= (uint)c.MassRows || c.MassCount[u] == 0)
                        front++;
                    if (d - nearMass > WavesDeep)
                        deep++;
                }
                notes = c.Notes;
                foreach (var n in perCell.Values)
                    res.PerCellTick = Math.Max(res.PerCellTick, n);
                // Every drawn zombie, by its look: the agents (standing, as drawn) and the particles (where drawn).
                var a = view.Agents;
                if (faded0 < 0)
                    faded0 = a.Raised + a.Sunk;
                cur.Clear();
                dup.Clear();
                var fading = 0;
                for (var i = 0; i < a.Count; i++)
                {
                    if (a.State[i] is CrowdAgents.Mode.Claimed || a.Fade[i] <= 0 || !a.VisibleCell(a.CellOf[i]))
                        continue;
                    if (a.Fade[i] < 1f)
                    {
                        fading++;
                        // Rising or sinking at the blob's front (its nearest 12 m to the player): where the wake happens.
                        if (Vector2.Distance(new Vector2(a.X[i], a.Z[i]), new Vector2(at.X, at.Z)) < nearMass + WavesFront)
                            res.FrontFading++;
                        continue;
                    }
                    if (!cur.TryAdd(a.Look[i], (a.X[i], a.Z[i], false)))
                        dup.Add(a.Look[i]);
                }
                for (var i = 0; i < c.Count; i++)
                {
                    c.DrawnAt(i, out var x, out var z);
                    if (!cur.TryAdd(c.DrawLook[i], (x, z, true)))
                        dup.Add(c.DrawLook[i]);
                }
                foreach (var (look, p) in cur)
                {
                    if (dup.Contains(look) || !prev.TryGetValue(look, out var q))
                        continue;
                    var step = MathF.Sqrt((p.X - q.X) * (p.X - q.X) + (p.Z - q.Z) * (p.Z - q.Z));
                    if (p.Particle != q.Particle)
                    {
                        res.TierChanges++;
                        if (!p.Particle)
                            res.MergeChanges++;
                        res.TierJump = MathF.Max(res.TierJump, step);
                    }
                    else if (!p.Particle)
                        res.AgentJump = MathF.Max(res.AgentJump, step);
                }
                (prev, cur) = (cur, prev);
                // Below the ground: the instances drawn against the ground there (the particles' and the agents').
                var inst = view.Inst;
                var below = 0;
                foreach (var s in inst.Stripes)
                    for (var l = 0; l < CrowdInstances.Lods; l++)
                        for (var k = 0; k < s.Count[l]; k++)
                        {
                            ref var o = ref s.Data[l][k];
                            if ((float)o.Y + inst.BaseY < terrain.Sample(o.X, o.Z) - 0.1f)
                                below++;
                        }
                res.BelowExtra += Math.Max(0, below - fading);
            }
            res.Faded = view.Agents.Raised + view.Agents.Sunk - Math.Max(0, faded0);
            res.HandOvers = (int)view.HandOvers;
        }
        finally
        {
            CrowdJobs.Serial = false;
            ZombieAnim.ClearFrustum();
        }
        res.FrontShare = res.Released > 0 ? front / (float)res.Released : 0;
        res.DeepShare = res.Released > 0 ? deep / (float)res.Released : 0;
        res.Text = $"{res.Released} released over 13 s ({res.FrontShare * 100:0}% from the edge facing the player, {res.DeepShare * 100:0}% deep inside the blob, up to {res.PerCellTick} from a cell in a tick); "
                   + $"{res.HandOvers} hand-overs ({view.HandMisses} with no agent to take over), {res.TierChanges} tier changes tracked ({res.MergeChanges} merging back), the largest step at one {res.TierJump:0.000} m (agents walking: {res.AgentJump:0.000} m); "
                   + $"{res.Faded} agents raised or sunk ({res.FrontFading} agent-frames fading within {WavesFront} m of the blob's front), {res.BelowExtra} instances below the ground past those; {c.Merged} merged; {Math.Max(0, res.Alloc)} B allocated over 0.5 s; agents pulled forward {view.Agents.Pulled}, swapped at a cell's back {view.Agents.Backfilled + view.Agents.Swapped}";
        return res;
    }

    /// <summary>The client's side: individuals taking over drawn agents and going back, on a small picture.</summary>
    private static void WavesClientSide()
    {
        var pic = new CrowdPicture();
        pic.Reset(1, 8, 8, 4f, 0f, 0f, 0f);
        pic.Set(3 * 8 + 3, 10, 0); // (a still cell at (12-16, 12-16), under its cap)
        pic.Set(3 * 8 + 5, 40, 0); // (a packed one at (20-24, 12-16), drawn at its cap of 28)
        var a = new CrowdAgents { DrawCap = 28, PullFromBehind = true };
        var cam = new Vector3(14, 1.7f, -20);
        void Frames(int n)
        {
            for (var f = 0; f < n; f++)
            {
                a.FromPicture(pic, null);
                a.Update(cam, 1 / 60f, 0, null);
            }
        }
        Frames(10);
        // Under the cap: the agent hides (its cell still counts it) and the pose is where it stood.
        var i0 = -1;
        for (var i = 0; i < a.Count && i0 < 0; i++)
            if (a.CellOf[i] == 3 * 8 + 3)
                i0 = i;
        var spot = new Vector2(a.X[i0] + 0.4f, a.Z[i0] - 0.3f);
        var took = a.TakeOver(spot.X, spot.Y, 3f, -1, counted: true, out var pose);
        var claimedOk = took >= 0 && a.State[took] == CrowdAgents.Mode.Claimed && Vector2.Distance(new Vector2(pose.X, pose.Z), new Vector2(a.X[took], a.Z[took])) < 1e-4f;
        var raised = a.Raised;
        var sunk = a.Sunk;
        pic.Set(3 * 8 + 3, 9, 0);
        Frames(30);
        if (!claimedOk || a.Raised != raised || a.Sunk != sunk)
            Fail($"wake waves: a client's individual should take over the agent it came out of, hidden until the count drops, raising and sinking none ({took}, raised {a.Raised - raised}, sunk {a.Sunk - sunk})");
        // At the cap: the agent goes at once and another takes its place at the cell's back at once (nothing rises).
        var members = a.MembersOf(3 * 8 + 5);
        var backfilled = a.Backfilled;
        took = a.TakeOver(22, 14, 3f, -1, counted: true, out _);
        Frames(1);
        var risingNow = 0;
        for (var i = 0; i < a.Count; i++)
            if (a.State[i] == CrowdAgents.Mode.Rising)
                risingNow++;
        if (took < 0 || members != 28 || a.MembersOf(3 * 8 + 5) != 28 || a.Backfilled != backfilled + 1 || risingNow > 0 || a.Raised != raised)
            Fail($"wake waves: in a cell drawn at its cap a take-over should swap the sample at the cell's back ({members} -> {a.MembersOf(3 * 8 + 5)} agents, backfilled {a.Backfilled - backfilled}, {risingNow} rising)");
        // Going back: the agent appears where it was drawn, facing its way, at its point in the walk.
        a.Hint(13.5f, 13.5f, 91u, 1.25f, 3.5f, 1);
        Frames(1);
        var back = -1;
        for (var i = 0; i < a.Count; i++)
            if (a.Look[i] == 91u)
                back = i;
        if (back < 0 || MathF.Abs(a.Yaw[back] - 1.25f) > 0.05f || a.Gait[back] != 1 || MathF.Abs(a.Walk[back] - 3.5f) > 0.05f || a.State[back] != CrowdAgents.Mode.Live)
            Fail("wake waves: an individual going back into the crowd should leave an agent facing its way at its point in the walk");
    }
}
