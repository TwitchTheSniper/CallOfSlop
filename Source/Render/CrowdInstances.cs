using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CallOfSlop;

/// <summary>One crowd instance for the GPU: 16 bytes (Renderer.Crowd.cs reads them as three attributes).</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CrowdInstance
{
    /// <summary>Flat position (world metres).</summary>
    public float X, Z;

    /// <summary>
    /// Height over <see cref="CrowdInstances.BaseY"/> (the camera's: most precise near it), and the gait plus the walk
    /// cycle's phase: 0-1 walking, 1-2 running, 2-3 sprinting (<see cref="ZSpeed"/>; the rig's rows for that gait).
    /// </summary>
    public Half Y, Phase;

    /// <summary>Yaw (a turn in 255ths), brightness (in 1.5 / 255ths), the debug tier tint and held (0 or 255).</summary>
    public byte Yaw, Bright, Tint, Held;
}

/// <summary>
/// This frame's crowd instances for <see cref="Renderer.DrawCrowd"/> (docs/zombies/MASS-HORDE.md, "Rendering"): every
/// crowd zombie the camera can see, sorted into three levels of detail (<see cref="CrowdRig"/>: near, mid, far), packed
/// at 16 bytes (<see cref="CrowdInstance"/>). Written in parallel stripes (<see cref="CrowdJobs"/>): each stripe has
/// its own arrays per level, which the renderer uploads one after another. The host's particles are written here
/// (<see cref="FromParticles"/>); the far crowd, the host's mass and everything a client draws, by its persistent agents
/// (<see cref="CrowdAgents.Update"/>). CPU only, so the self-test can time it; no allocation once grown.
/// </summary>
public sealed class CrowdInstances
{
    public const int Lods = 3, Bytes = 16;

    /// <summary>Near rig up to <see cref="NearRange"/> m from the camera, mid up to <see cref="MidRange"/>, far beyond.</summary>
    public float NearRange = 35f, MidRange = 80f;

    /// <summary>Tier colours (1: crowd yellow, held orange; 0: zombie colours); <see cref="AgentShade"/> darkens the agents (the host's mass with the tier colours).</summary>
    public float DebugMix, AgentShade = 1f;

    /// <summary>Heights are stored over this (the camera's height this frame).</summary>
    public float BaseY;

    /// <summary>Metres walked per walk cycle (the rigs': <see cref="CrowdRig.CycleMetres"/>).</summary>
    public float Cycle = 4f * ZombieAnim.StrideHalf(ZSpeed.Walk);

    /// <summary>Metres per cycle of each gait (walk, run, sprint: the individuals' strides, ZombieAnim.StrideHalf).</summary>
    public static readonly float[] GaitCycle = [CrowdRig.CycleOf(ZSpeed.Walk), CrowdRig.CycleOf(ZSpeed.Run), CrowdRig.CycleOf(ZSpeed.Sprint)];

    /// <summary>A stripe's own instances (one thread writes it).</summary>
    public sealed class Stripe
    {
        public readonly CrowdInstance[][] Data = [new CrowdInstance[512], new CrowdInstance[1024], new CrowdInstance[4096]];
        public readonly int[] Count = new int[Lods];
        public int Culled;
        internal float BaseY, Cycle;
        internal byte Tint;

        public void Clear()
        {
            Array.Clear(Count);
            Culled = 0;
        }

        /// <summary>An instance at (x, y, z) facing <paramref name="yaw"/>, <paramref name="walked"/> m into its walk, in gait <paramref name="gait"/> (<see cref="ZSpeed"/>).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(int lod, float x, float y, float z, float yaw, float walked, float bright, bool held, int gait = 0)
        {
            ref var arr = ref Data[lod];
            var n = Count[lod];
            if (n >= arr.Length)
                Array.Resize(ref arr, arr.Length * 2);
            var f = yaw * (1f / MathF.Tau);
            f -= MathF.Floor(f);
            gait = Math.Clamp(gait, 0, 2);
            var p = walked / (gait == 0 ? Cycle : GaitCycle[gait]);
            p -= MathF.Floor(p);
            p = MathF.Min(p, 0.995f) + gait; // (a whole number of gaits, then the phase: Half keeps 1/512 at 2-3)
            ref var o = ref arr[n];
            o.X = x;
            o.Z = z;
            o.Y = (Half)(y - BaseY);
            o.Phase = (Half)p;
            o.Yaw = (byte)(f * 255f + 0.5f);
            o.Bright = (byte)Math.Clamp(bright * (255f / 1.5f) + 0.5f, 0f, 255f);
            o.Tint = Tint;
            o.Held = held ? (byte)255 : (byte)0;
            Count[lod] = n + 1;
        }
    }

    public readonly Stripe[] Stripes;
    private readonly CrowdJobs _jobs;

    /// <summary>Instances per level of detail this frame (all stripes; after <see cref="End"/>).</summary>
    public readonly int[] Count = new int[Lods];

    /// <summary>Zombies left out this frame (out of view, past the fog, behind the ground, over a cell's cap), and the agents' cell counts.</summary>
    public int Culled, CellsCulled, CellsDrawn, CellsHidden, CellsFogged;

    /// <summary>Of the instances, how many are particles (the host's) and how many agents.</summary>
    public int Particles, Agents;

    public int Total => Count[0] + Count[1] + Count[2];

    public CrowdInstances(CrowdJobs? jobs = null)
    {
        _jobs = jobs ?? CrowdJobs.Shared;
        Stripes = new Stripe[_jobs.Stripes];
        for (var s = 0; s < Stripes.Length; s++)
            Stripes[s] = new Stripe();
        _particlePass = ParticlePass;
    }

    /// <summary>The stripe <paramref name="s"/> writes (CrowdAgents' pass).</summary>
    public Stripe StripeAt(int s) => Stripes[s];

    /// <summary>A new frame from <paramref name="cam"/>.</summary>
    public void Begin(Vector3 cam)
    {
        BaseY = cam.Y;
        var tint = (byte)Math.Clamp(DebugMix * 255f + 0.5f, 0f, 255f);
        foreach (var s in Stripes)
        {
            s.Clear();
            s.BaseY = BaseY;
            s.Cycle = Cycle;
            s.Tint = tint;
        }
        Array.Clear(Count);
        Culled = CellsCulled = CellsDrawn = CellsHidden = CellsFogged = Particles = Agents = 0;
    }

    /// <summary>Sums the stripes (call after the last writer).</summary>
    public void End()
    {
        Array.Clear(Count);
        var culled = 0;
        foreach (var s in Stripes)
        {
            for (var l = 0; l < Lods; l++)
                Count[l] += s.Count[l];
            culled += s.Culled;
        }
        Culled += culled;
    }

    /// <summary>A zombie's brightness from its look.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float BrightOf(uint h) => 0.82f + (h & 1023) / 1023f * 0.36f;

    /// <summary>An agent's brightness (with <see cref="AgentShade"/>).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float Bright(uint h) => BrightOf(h) * AgentShade;

    /// <summary>The agents' cells for the frame's counts (after their update).</summary>
    public void CountCells(CrowdAgents a)
    {
        CellsDrawn += a.CellsDrawn;
        CellsCulled += a.CellsCulled;
        CellsHidden += a.CellsHidden;
        CellsFogged += a.CellsFogged;
    }

    // ---- The host's particles ---------------------------------------------------------------------------

    private HordeCrowd? _pc;
    private CrowdAgents? _pcells;
    private Vector3 _pcam;
    private float _pfog2;
    private long _ptick;
    private readonly Action<int> _particlePass;

    /// <summary>Ticks a released particle takes to rise out of the ground (its mass agent sinks meanwhile).</summary>
    public const int RiseTicks = (int)(CrowdAgents.FadeSeconds * World.TickRate);

    /// <summary>
    /// The host: every crowd particle in view (a sphere test each; past the fog or behind the ground by its cell, from
    /// <paramref name="cells"/>' horizon), in parallel stripes. <paramref name="tick"/>: the server tick (a particle just
    /// let out of the mass rises).
    /// </summary>
    public void FromParticles(HordeCrowd c, Vector3 cam, float fogEnd, CrowdAgents? cells, long tick)
    {
        if (c.Count == 0)
            return;
        _pc = c;
        _pcells = cells != null && cells.Cells > 0 ? cells : null;
        _pcam = cam;
        _pfog2 = fogEnd > 0 ? (fogEnd + 1.5f) * (fogEnd + 1.5f) : float.MaxValue;
        _ptick = tick;
        var before = 0;
        foreach (var s in Stripes)
            before += s.Count[0] + s.Count[1] + s.Count[2];
        _jobs.Run(_particlePass);
        var after = 0;
        foreach (var s in Stripes)
            after += s.Count[0] + s.Count[1] + s.Count[2];
        Particles += after - before;
        _pc = null;
        _pcells = null;
    }

    private void ParticlePass(int s)
    {
        var c = _pc!;
        _jobs.Range(s, c.Count, out var from, out var to);
        var st = Stripes[s];
        var cam = _pcam;
        var terrain = c.Terrain;
        var cells = _pcells;
        var fog2 = _pfog2;
        float near2 = NearRange * NearRange, mid2 = MidRange * MidRange;
        var y = c.Y;
        for (var i = from; i < to; i++)
        {
            float x = c.X[i], z = c.Z[i];
            float dx = x - cam.X, dz = z - cam.Z;
            var d2 = dx * dx + dz * dz;
            if (d2 > fog2)
            {
                st.Culled++;
                continue;
            }
            if (cells != null)
            {
                var cell = cells.CellAt(x, z);
                if (cell >= 0 && cells.HiddenCell(cell))
                {
                    st.Culled++;
                    continue;
                }
            }
            if (terrain != null)
                y = terrain.Sample(x, z);
            if (!ZombieAnim.Visible(new Vector3(x, y + 0.9f, z), 1.1f))
            {
                st.Culled++;
                continue;
            }
            float vx = c.VX[i], vz = c.VZ[i];
            var moving = vx * vx + vz * vz > 0.04f;
            var yaw = moving ? MathF.Atan2(vx, -vz) : MathF.Atan2(c.DX[i], -c.DZ[i]);
            var h = c.Look[i];
            // Metres walked: along its heading (continuous while it walks); one waiting to be promoted and held back by the
            // front shuffles on the spot (CrowdAgents.JamPace).
            var f = World.FlatForward(yaw);
            var walked = x * f.X + z * f.Z + (h >> 10 & 255) / 64f;
            if (!moving && c.Held[i])
                walked += _ptick * World.TickDt * c.Spd[i] * CrowdAgents.JamPace;
            var age = _ptick - c.Born[i];
            var sink = age >= 0 && age < RiseTicks ? (1f - age / (float)RiseTicks) * CrowdAgents.SinkDepth : 0f;
            var lod = d2 < near2 ? 0 : d2 < mid2 ? 1 : 2;
            st.Add(lod, x, y - sink, z, yaw, walked, BrightOf(h), c.Held[i], c.Cls[i]); // (its gait: its speed class, CrowdPace.cs)
        }
    }
}
