using System.Numerics;
using System.Runtime.InteropServices;

namespace CallOfSlop;

/// <summary>
/// Self-test section "Crowd agents" (docs/zombies/MASS-HORDE.md, "Rendering: persistent crowd agents"; board items
/// dev-crowd-flicker-cf01 and dev-horde-render-opt-ho01). Headless and short: a scripted run of crowd messages (a
/// stream flowing east through a picture, counts moving downstream, kills, arrivals, heading wobbles, cells stopping
/// and starting) drawn through <see cref="CrowdAgents"/> frame by frame: no agent moves more than a walking step in a
/// frame, none appears except rising from the ground (or filled into a cell coming into view), none vanishes except
/// after sinking (or hidden by an individual, or with its cell out of view), the agents match the picture up to each
/// cell's cap once settled, and nothing allocates; the same run serial and in parallel gives the same agents; the
/// terrain horizon only hides what the ground really hides (Horde Stress); claims, hints and the ray test; the host's
/// particles keep their look when another is removed; and the preparation's CPU on Horde Stress at 25,000.
/// </summary>
public static partial class SelfTest
{
    private static void CheckCrowdAgents()
    {
        if (Marshal.SizeOf<CrowdInstance>() != 16)
            Fail($"a crowd instance should be 16 bytes, is {Marshal.SizeOf<CrowdInstance>()}");
        ZombieAnim.ClearFrustum();
        var serial = CrowdScript(serial: true, out var serialDigest);
        CrowdScript(serial: false, out var parallelDigest);
        if (serialDigest != parallelDigest)
            Fail($"the crowd agents differ run serial ({serialDigest:x16}) and in parallel ({parallelDigest:x16}) on the same script");
        Line(serial);
        CrowdViewChanges();
        CrowdTierChanges();
        CrowdHorizonCheck();
        CrowdParticleLooks();
        CrowdPrepBudget();
        CrowdAliveCheck();
    }

    /// <summary>A flat picture of 24 x 16 cells of 4 m from (-48, -32), a stream flowing east (heading byte for yaw +90 degrees).</summary>
    private static CrowdPicture CrowdTestPicture(out byte east)
    {
        var p = new CrowdPicture();
        p.Reset(1, 24, 16, 4f, -48f, -32f, 0f);
        east = HordeCrowd.HeadingByte(MathF.PI / 2);
        for (var r = 5; r < 11; r++)
            for (var c = 2; c < 12; c++)
                p.Set(r * 24 + c, (byte)(c < 6 ? 38 : 12 + (c * 7 + r * 3) % 20), east);
        return p;
    }

    /// <summary>
    /// The scripted run: 7 s at 60 frames a second, a crowd message every 6 frames. Returns a summary line; checks every
    /// frame. <paramref name="digest"/>: a hash of the agents at the end (serial and parallel runs must agree).
    /// </summary>
    private static string CrowdScript(bool serial, out ulong digest)
    {
        CrowdJobs.Serial = serial;
        try
        {
            var pic = CrowdTestPicture(out var east);
            var a = new CrowdAgents { DrawCap = 28, FarCap = 8, FarFrom = 60, FarTo = 180, Speed = 1.5f, JamCount = 40 };
            var inst = new CrowdInstances();
            var cam = new Vector3(-60, 6, 0);
            const float dt = 1 / 60f;
            var flux = new float[pic.Count.Length];
            var rng = 0xBEEFu;
            var prev = new Dictionary<uint, (Vector3 At, CrowdAgents.Mode State, float Fade)>();
            var now = new Dictionary<uint, (Vector3 At, CrowdAgents.Mode State, float Fade)>();
            int moves = 0, pops = 0, vanish = 0, outside = 0;
            var firstBad = "";
            float worstStep = 0;
            long alloc = 0;
            var frames = 7 * 60;
            for (var f = 0; f < frames; f++)
            {
                // The server's message: a share of each moving cell flows one cell east (if it has room), some die, some
                // arrive at the west edge; headings wobble a step; for a second in the middle, the front row stops.
                if (f % 6 == 0 && f > 0 && f < 5 * 60)
                {
                    var n = pic.Count.Length;
                    for (var c = n - 1; c >= 0; c--)
                    {
                        int m = pic.Count[c];
                        if (m == 0 || pic.Head[c] == 0 || c % 24 == 23)
                            continue;
                        flux[c] += m * 1.5f * 0.1f / 4f;
                        var move = Math.Min((int)flux[c], Math.Max(0, 40 - pic.Count[c + 1]));
                        if (move <= 0)
                            continue;
                        flux[c] -= move;
                        pic.Set(c, (byte)(m - move), pic.Head[c]);
                        pic.Set(c + 1, (byte)(pic.Count[c + 1] + move), east);
                    }
                    rng = HordeCrowd.Hash(rng + (uint)f);
                    var k = (int)(rng % (uint)n);
                    if (pic.Count[k] > 3)
                        pic.Set(k, (byte)(pic.Count[k] - 3), pic.Head[k]); // (shot)
                    var w = (5 + (int)(rng >> 8) % 6) * 24 + 2;
                    pic.Set(w, (byte)Math.Min(40, pic.Count[w] + 2), east); // (arrivals)
                    for (var c = 0; c < n; c++)
                        if (pic.Count[c] > 0 && pic.Head[c] != 0)
                            pic.Set(c, pic.Count[c], (byte)Math.Clamp(east + (int)((HordeCrowd.Hash((uint)(c * 31 + f)) % 5) - 2), 1, 255));
                    var stop = f is >= 120 and < 180;
                    for (var r = 0; r < 16; r++)
                        for (var c = 14; c < 24; c++)
                        {
                            var cell = r * 24 + c;
                            if (pic.Count[cell] > 0)
                                pic.Set(cell, pic.Count[cell], stop ? (byte)0 : east);
                        }
                }
                var a0 = GC.GetAllocatedBytesForCurrentThread();
                a.FromPicture(pic, null);
                inst.Begin(cam);
                a.Update(cam, dt, 0, inst);
                inst.End();
                if (f >= 60)
                    alloc += GC.GetAllocatedBytesForCurrentThread() - a0;
                now.Clear();
                for (var i = 0; i < a.Count; i++)
                    now[a.Id[i]] = (a.Position(i), a.State[i], a.Fade[i]);
                if (f > 0)
                {
                    var step = a.MaxStep(dt) + CrowdAgents.SinkDepth * dt / CrowdAgents.FadeSeconds; // (and its sinking or rising)
                    foreach (var (id, cur) in now)
                    {
                        if (prev.TryGetValue(id, out var was))
                        {
                            var d = Vector3.Distance(cur.At, was.At);
                            worstStep = MathF.Max(worstStep, d);
                            if (d > step)
                            {
                                moves++;
                                if (firstBad.Length == 0)
                                    firstBad = $"agent {id} moved {d:0.00} m in a frame at frame {f} ({was.State} to {cur.State})";
                            }
                        }
                        else if (!(cur.State == CrowdAgents.Mode.Rising && cur.Fade <= dt / CrowdAgents.FadeSeconds + 1e-3f))
                        {
                            pops++;
                            if (firstBad.Length == 0)
                                firstBad = $"agent {id} appeared at frame {f} {cur.State}, fade {cur.Fade:0.00}";
                        }
                    }
                    foreach (var (id, was) in prev)
                        if (!now.ContainsKey(id) && !(was.State == CrowdAgents.Mode.Sinking && was.Fade <= dt / CrowdAgents.FadeSeconds + 1e-3f) && was.State != CrowdAgents.Mode.Claimed)
                        {
                            vanish++;
                            if (firstBad.Length == 0)
                                firstBad = $"agent {id} vanished at frame {f} ({was.State}, fade {was.Fade:0.00})";
                        }
                }
                for (var i = 0; i < a.Count; i++)
                    if (a.State[i] is CrowdAgents.Mode.Live or CrowdAgents.Mode.Rising && a.CellAt(a.X[i], a.Z[i]) != a.CellOf[i])
                        outside++;
                (prev, now) = (now, prev);
            }
            // Settled (2 s with no messages): every visible cell holds its count up to its cap, and that is what is drawn.
            int want = 0, members = 0, wrong = 0;
            for (var c = 0; c < a.Cells; c++)
            {
                var d = new Vector2(a.MinX + (c % a.Cols + 0.5f) * a.Cell - cam.X, a.MinZ + (c / a.Cols + 0.5f) * a.Cell - cam.Z).Length();
                var t = a.TargetOf(c);
                want += t;
                members += a.MembersOf(c);
                // (the cap moves in steps, so it may be up to two off the distance's own)
                if (a.MembersOf(c) != t || t > pic.Count[c] || Math.Abs(t - Math.Min(pic.Count[c], a.CapAt(d))) > 2)
                    wrong++;
            }
            var drawn = inst.Total;
            if (moves > 0 || pops > 0 || vanish > 0)
                Fail($"crowd agents ({(serial ? "serial" : "parallel")}): {moves} jumps, {pops} pops in, {vanish} pops out over {frames} frames; first: {firstBad}");
            if (outside > 0)
                Fail($"crowd agents: {outside} agent-frames stood outside their own cell");
            if (wrong > 0 || members != want || drawn != want)
                Fail($"crowd agents: once settled the cells should hold the picture up to their caps ({want}): they hold {members} ({wrong} cells wrong), {drawn} drawn");
            if (alloc > 0)
                Fail($"crowd agents: {alloc} bytes allocated over the run after the first second");
            if (a.Passed == 0 || a.Crossed == 0 || a.Raised == 0 || a.Sunk == 0)
                Fail($"crowd agents: the script should pass agents between cells ({a.Passed}), cross ({a.Crossed}), raise ({a.Raised}) and sink ({a.Sunk}) some");
            ulong h = 1469598103934665603;
            for (var i = 0; i < a.Count; i++)
                h = (h ^ BitConverter.SingleToUInt32Bits(a.X[i]) ^ (ulong)BitConverter.SingleToUInt32Bits(a.Z[i]) << 32 ^ a.Id[i]) * 1099511628211;
            digest = h;
            return $"crowd agents: {frames} frames, {a.Count} agents at the end ({want} wanted), worst step {worstStep * 100:0.0} cm a frame (limit {a.MaxStep(dt) * 100:0.0}), "
                   + $"raised {a.Raised}, sunk {a.Sunk}, passed {a.Passed}, crossed {a.Crossed}, filled {a.Filled}; {alloc} bytes allocated";
        }
        finally
        {
            CrowdJobs.Serial = false;
        }
    }

    /// <summary>Turning the camera: cells coming into view are filled at once (nobody saw them empty), cells leaving it drop their agents.</summary>
    private static void CrowdViewChanges()
    {
        var pic = CrowdTestPicture(out _);
        var a = new CrowdAgents();
        var cam = new Vector3(-60, 1.7f, 0);
        var proj = Matrix4x4.CreatePerspectiveFieldOfView(1.1f, 16f / 9f, 0.05f, 500f);
        void Look(Vector3 at) => ZombieAnim.SetFrustum(Matrix4x4.CreateLookAt(cam, at, Vector3.UnitY) * proj);
        try
        {
            Look(cam + new Vector3(0, 0, -50)); // (north: the stream is to the east, out of view)
            for (var f = 0; f < 30; f++)
            {
                a.FromPicture(pic, null);
                a.Update(cam, 1 / 60f, 0, null);
            }
            var before = a.Count;
            var raisedBefore = a.Raised;
            var seen = new bool[a.Cells];
            for (var c = 0; c < a.Cells; c++)
                seen[c] = a.VisibleCell(c);
            Look(cam + new Vector3(50, 0, 0)); // (east, at the stream)
            a.FromPicture(pic, null);
            a.Update(cam, 1 / 60f, 0, null);
            int fresh = 0, short_ = 0, rising = 0;
            for (var c = 0; c < a.Cells; c++)
                if (a.VisibleCell(c) && !seen[c] && a.TargetOf(c) > 0)
                {
                    fresh++;
                    if (a.MembersOf(c) != a.TargetOf(c))
                        short_++;
                }
            for (var i = 0; i < a.Count; i++)
                if (a.State[i] == CrowdAgents.Mode.Rising && !seen[a.CellOf[i]])
                    rising++;
            if (fresh == 0 || short_ > 0 || rising > 0 || a.Raised != raisedBefore)
                Fail($"crowd agents: cells turned into view should be filled at once ({fresh} came into view, {short_} not full, {rising} agents rising in them)");
            Look(cam + new Vector3(-50, 0, 0)); // (west: the stream behind the camera)
            a.FromPicture(pic, null);
            a.Update(cam, 1 / 60f, 0, null);
            if (a.Count != 0)
                Fail($"crowd agents: with the stream out of view no agent should be left ({a.Count}; {before} before)");
        }
        finally
        {
            ZombieAnim.ClearFrustum();
        }
    }

    /// <summary>Claims (an individual came out of the crowd), hints (one went back) and the placement API's ray test.</summary>
    private static void CrowdTierChanges()
    {
        var pic = new CrowdPicture();
        pic.Reset(1, 8, 8, 4f, 0f, 0f, 0f);
        pic.Set(3 * 8 + 3, 10, 0); // (a still cell at (12-16, 12-16))
        var a = new CrowdAgents();
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
        if (a.Count != 10)
            Fail($"crowd agents: a cell of 10 should have 10 agents, has {a.Count}");
        var i0 = 0;
        var target = new Vector3(a.X[i0], 0, a.Z[i0]);
        // The ray test: from the camera at the agent's chest.
        var dir = Vector3.Normalize(target + new Vector3(0, 1.2f, 0) - cam);
        var hit = a.RayHit(cam, dir, 100f, out var t);
        if (hit < 0 || Vector3.Distance(a.Position(hit), target) > 3f)
            Fail($"crowd agents: a ray at an agent should hit it (hit {hit}, at {t:0.0} m)");
        // An individual appears there: the nearest agent hides; then the count drops and it goes without sinking.
        a.Claim(target.X, target.Z);
        Frames(1);
        var claimed = 0;
        for (var i = 0; i < a.Count; i++)
            if (a.State[i] == CrowdAgents.Mode.Claimed)
                claimed++;
        var sunk = a.Sunk;
        pic.Set(3 * 8 + 3, 9, 0);
        Frames(2);
        var stillClaimed = 0;
        for (var i = 0; i < a.Count; i++)
            if (a.State[i] == CrowdAgents.Mode.Claimed)
                stillClaimed++;
        if (claimed != 1 || stillClaimed != 0 || a.Sunk != sunk || a.Count != 9 + CountSinking(a))
            Fail($"crowd agents: a claim should hide one agent ({claimed}) and the count dropping should take it without sinking another ({stillClaimed} left hidden, {a.Sunk - sunk} sunk, {a.Count} agents)");
        // One goes back into the crowd at a spot: an agent appears right there before the count does, and stays when it comes.
        var spot = new Vector3(13.1f, 0, 14.9f);
        a.Hint(spot.X, spot.Z, 77u);
        Frames(1);
        var found = -1;
        for (var i = 0; i < a.Count; i++)
            if (a.Look[i] == 77u)
                found = i;
        pic.Set(3 * 8 + 3, 10, 0);
        var raised = a.Raised;
        Frames(60);
        var still = -1;
        for (var i = 0; i < a.Count; i++)
            if (a.Look[i] == 77u && a.State[i] != CrowdAgents.Mode.Sinking)
                still = i;
        if (found < 0 || Vector2.Distance(new Vector2(a.X[found], a.Z[found]), new Vector2(spot.X, spot.Z)) > 0.5f || still < 0 || a.Raised != raised)
            Fail("crowd agents: a hint should put an agent at its spot that stays when the count catches up (and raises no other)");
    }

    private static int CountSinking(CrowdAgents a)
    {
        var n = 0;
        for (var i = 0; i < a.Count; i++)
            if (a.State[i] == CrowdAgents.Mode.Sinking)
                n++;
        return n;
    }

    /// <summary>
    /// The terrain horizon on Horde Stress, from the valley floor and the Overlook: every cell it hides is really behind
    /// the ground (a line from the camera to every point of its top layer, a 5 x 5 grid at 0.2 and 1.9 m, passes under the
    /// terrain somewhere), and it hides something from the valley floor.
    /// </summary>
    private static void CrowdHorizonCheck()
    {
        if (Maps.Find("Horde Stress") is not { Terrain: { } terrain } map)
        {
            Fail("no Horde Stress map with terrain for the horizon check");
            return;
        }
        var cams = new[] { new Vector3(0, terrain.Sample(0, 0) + 1.7f, 60), new Vector3(-175, terrain.Sample(-175, 0) + 1.7f, 0), new Vector3(120, terrain.Sample(120, -60) + 1.7f, -60) };
        var a = new CrowdAgents();
        a.Layout((int)(MathF.Ceiling((terrain.MaxX - terrain.OriginX) / 4f)), (int)MathF.Ceiling((terrain.MaxZ - terrain.OriginZ) / 4f), 4f, terrain.OriginX, terrain.OriginZ, 0f, terrain);
        int hidden = 0, wrong = 0, cells = 0;
        var firstWrong = "";
        foreach (var cam in cams)
        {
            a.Horizon.Rebuild(terrain, cam, 1000f);
            for (var c = 0; c < a.Cells; c += 3)
            {
                cells++;
                float cx = a.MinX + (c % a.Cols + 0.5f) * 4f, cz = a.MinZ + (c / a.Cols + 0.5f) * 4f;
                var top = float.NegativeInfinity;
                for (var j = 0; j <= 4; j++)
                    for (var i = 0; i <= 4; i++)
                        top = MathF.Max(top, terrain.Sample(cx - 2 + i, cz - 2 + j));
                float dx = cx - cam.X, dz = cz - cam.Z;
                if (!a.Horizon.Hidden(dx, dz, MathF.Sqrt(dx * dx + dz * dz), 4f * 0.7072f, top + 2.2f))
                    continue;
                hidden++;
                for (var j = 0; j <= 4 && firstWrong.Length == 0; j++)
                    for (var i = 0; i <= 4; i++)
                    {
                        float px = cx - 2 + i, pz = cz - 2 + j;
                        foreach (var up in new[] { 0.2f, 1.9f })
                        {
                            var p = new Vector3(px, terrain.Sample(px, pz) + up, pz);
                            if (!BehindGround(terrain, cam, p))
                            {
                                wrong++;
                                firstWrong = $"from ({cam.X:0}, {cam.Y:0.0}, {cam.Z:0}) the point ({p.X:0.0}, {p.Y:0.0}, {p.Z:0.0}) is in sight";
                                break;
                            }
                        }
                        if (firstWrong.Length > 0)
                            break;
                    }
            }
        }
        Line($"crowd horizon: {hidden} of {cells} sampled cells hidden from 3 cameras on Horde Stress, {wrong} wrongly");
        if (wrong > 0)
            Fail($"crowd horizon: hid a cell that can be seen: {firstWrong}");
        if (hidden == 0)
            Fail("crowd horizon: nothing hidden behind the knolls and slopes of Horde Stress");
    }

    /// <summary>Is <paramref name="p"/> behind the terrain from <paramref name="cam"/> (the line passes under the ground somewhere between)?</summary>
    private static bool BehindGround(TerrainData terrain, Vector3 cam, Vector3 p)
    {
        var d = p - cam;
        var len = new Vector2(d.X, d.Z).Length();
        var steps = (int)(len / 0.5f);
        for (var k = 1; k < steps - 2; k++)
        {
            var q = cam + d * (k / (float)steps);
            if (q.X < terrain.OriginX || q.Z < terrain.OriginZ || q.X > terrain.MaxX || q.Z > terrain.MaxZ)
                continue;
            if (terrain.Sample(q.X, q.Z) > q.Y)
                return true;
        }
        return false;
    }

    /// <summary>The host's particles keep their look (brightness, stride) when another one is removed (it used to come from the index).</summary>
    private static void CrowdParticleLooks()
    {
        var c = new HordeCrowd();
        c.Reserve(8);
        for (var i = 0; i < 5; i++)
            c.Add(i, 0, 0, 0, (uint)(1000 + i), 0);
        var lastLook = c.Look[4];
        c.RemoveAt(1); // (particle 4 moves to index 1)
        if (c.Look[1] != lastLook || c.Look[0] == c.Look[1])
            Fail("crowd agents: a particle's look should move with it when another particle is removed");
    }

    /// <summary>The preparation's CPU on Horde Stress at 25,000 (every zombie in the mass, the client's case), all on one thread.</summary>
    private static void CrowdPrepBudget()
    {
        if (Maps.Find("Horde Stress") is not { Zombies.Stress: not null } map)
            return;
        var now = 0.0;
        using var g = StressGame(map, ref now, 25000);
        var me = g.Local!;
        var eye = me.Position + new Vector3(0, 1.6f, 0);
        var view = Matrix4x4.CreateLookAt(eye, new Vector3(60, -4, 0), Vector3.UnitY);
        var proj = Matrix4x4.CreatePerspectiveFieldOfView(1.1f, 16f / 9f, 0.05f, 900);
        ZombieAnim.SetFrustum(view * proj);
        CrowdJobs.Serial = true;
        try
        {
            var v = new CrowdView();
            var t = now;
            for (var k = 0; k < 10; k++)
                v.Prepare(g, eye, t += 1 / 60.0, map.FogEnd);
            var a0 = GC.GetAllocatedBytesForCurrentThread();
            for (var k = 0; k < 10; k++)
                v.Prepare(g, eye, t += 1 / 60.0, map.FogEnd);
            var alloc = GC.GetAllocatedBytesForCurrentThread() - a0;
            var ms = ThreadCpu.TimeMs(() =>
            {
                for (var k = 0; k < 10; k++)
                    v.Prepare(g, eye, t += 1 / 60.0, map.FogEnd);
            }) / 10;
            var i = v.Inst;
            Line($"crowd prep on Horde Stress at 25,000 (host, mass only, one thread): {ms:0.00} ms CPU, {i.Total} instances ({i.Count[0]}/{i.Count[1]}/{i.Count[2]}), {v.Agents.CellsDrawn} cells drawn, {v.Agents.CellsHidden} behind the ground, {alloc} bytes allocated");
            if (alloc > 0)
                Fail($"crowd prep on Horde Stress allocated {alloc} bytes over 10 frames");
            if (TickCpu.Available && ms > 2.0)
                Fail($"crowd prep on Horde Stress at 25,000 took {ms:0.00} ms of CPU a frame (budget 2 ms)");
        }
        finally
        {
            CrowdJobs.Serial = false;
            ZombieAnim.ClearFrustum();
        }
    }

    /// <summary>
    /// Always alive (board item dev-horde-lod-motion-lm01): a packed crowd with its front blocked (a jam, nothing flowing)
    /// still plays its cycle in every level of detail (no agent shows one frame for over a second; the far rig's frames
    /// advance), jostles about its spot by a few cm, stays inside its cells, allocates nothing, and is the same serial
    /// and in parallel.
    /// </summary>
    private static void CrowdAliveCheck()
    {
        var one = CrowdAlive(serial: true, out var d1, out var line);
        CrowdAlive(serial: false, out var d2, out _);
        Line(line);
        if (d1 != d2)
            Fail($"crowd alive: serial ({d1:x16}) and parallel ({d2:x16}) runs differ");
        _ = one;
    }

    private static bool CrowdAlive(bool serial, out ulong digest, out string line)
    {
        CrowdJobs.Serial = serial;
        try
        {
            var pic = new CrowdPicture();
            pic.Reset(1, 24, 16, 4f, -48f, -32f, 0f);
            var east = HordeCrowd.HeadingByte(MathF.PI / 2);
            for (var r = 5; r < 11; r++)
                for (var c = 2; c < 12; c++)
                    pic.Set(r * 24 + c, 40, c < 10 ? east : (byte)0); // (a packed block; its front two columns stand, the rest press on them)
            var a = new CrowdAgents { DrawCap = 40, FarCap = 40, FarFrom = 60, FarTo = 180, Speed = 1.5f, JamCount = 40 };
            var inst = new CrowdInstances();
            var cam = new Vector3(-60, 6, 0);
            const float dt = 1 / 60f;
            var seen = new Dictionary<uint, (int Frame, int Run, int Worst, int Changes, byte Lod, float MinX, float MaxX, float MinZ, float MaxZ)>();
            long alloc = 0;
            for (var f = 0; f < 420; f++)
            {
                var a0 = GC.GetAllocatedBytesForCurrentThread();
                a.FromPicture(pic, null);
                inst.Begin(cam);
                a.Update(cam, dt, 0, inst);
                inst.End();
                if (f >= 60)
                    alloc += GC.GetAllocatedBytesForCurrentThread() - a0;
                if (f < 120)
                    continue; // (settled: filled and eased into spots)
                for (var i = 0; i < a.Count; i++)
                {
                    if (a.State[i] != CrowdAgents.Mode.Live)
                        continue;
                    var lod = a.Lod[i];
                    var frames = lod >= 2 ? CrowdRig.FarFrames : 16;
                    var p = a.Walk[i] / CrowdInstances.GaitCycle[0];
                    p -= MathF.Floor(p);
                    var fr = (int)(MathF.Min(p, 0.995f) * frames);
                    if (!seen.TryGetValue(a.Id[i], out var t) || t.Lod != lod)
                    {
                        seen[a.Id[i]] = (fr, 0, t.Worst, t.Changes, lod, a.X[i], a.X[i], a.Z[i], a.Z[i]);
                        continue;
                    }
                    var run = fr == t.Frame ? t.Run + 1 : 0;
                    seen[a.Id[i]] = (fr, run, Math.Max(t.Worst, run), t.Changes + (fr != t.Frame ? 1 : 0), lod,
                        MathF.Min(t.MinX, a.X[i]), MathF.Max(t.MaxX, a.X[i]), MathF.Min(t.MinZ, a.Z[i]), MathF.Max(t.MaxZ, a.Z[i]));
                }
            }
            var perLod = new int[3];
            var stuck = new int[3];
            var advanced = new int[3];
            double spread = 0;
            float widest = 0;
            foreach (var t in seen.Values)
            {
                perLod[t.Lod]++;
                if (t.Worst > 60)
                    stuck[t.Lod]++;
                if (t.Changes >= 2)
                    advanced[t.Lod]++;
                var w = MathF.Max(t.MaxX - t.MinX, t.MaxZ - t.MinZ);
                spread += w;
                widest = MathF.Max(widest, w);
            }
            var total = seen.Count;
            var mean = total > 0 ? spread / total : 0;
            ulong h = 1469598103934665603;
            for (var i = 0; i < a.Count; i++)
                h = (h ^ BitConverter.SingleToUInt32Bits(a.X[i]) ^ (ulong)BitConverter.SingleToUInt32Bits(a.Walk[i]) << 32 ^ a.Id[i]) * 1099511628211;
            digest = h;
            line = $"crowd alive: {total} jammed agents (lods {perLod[0]}/{perLod[1]}/{perLod[2]}), frozen over 1 s {stuck[0]}/{stuck[1]}/{stuck[2]}, "
                   + $"cycling {advanced[0]}/{advanced[1]}/{advanced[2]}, jostle {mean * 100:0} cm mean, {widest * 100:0} cm widest; {alloc} bytes allocated";
            if (!serial)
                return true;
            if (total < 200 || perLod[2] < 20 || perLod[1] < 20)
                Fail($"crowd alive: the scene should hold jammed agents in the mid and far rigs ({line})");
            for (var l = 0; l < 3; l++)
            {
                if (perLod[l] > 0 && (stuck[l] > perLod[l] * 0.05 || advanced[l] < perLod[l] * 0.95))
                    Fail($"crowd alive: a jammed agent in lod {l} froze for over a second ({line})");
            }
            if (mean < 0.03 || widest > 1.2f)
                Fail($"crowd alive: a jam should jostle by a few cm and stay packed ({line})");
            if (alloc > 0)
                Fail($"crowd alive: {alloc} bytes allocated ({line})");
            return true;
        }
        finally
        {
            CrowdJobs.Serial = false;
        }
    }
}
