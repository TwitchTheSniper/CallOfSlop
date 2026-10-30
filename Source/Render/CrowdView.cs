using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// A frame of the mass horde's crowd, ready to draw (docs/zombies/MASS-HORDE.md, "Rendering"): the host's particles as
/// they are, and the far crowd (the host's mass cells, or everything in a client's picture) as persistent agents
/// (<see cref="CrowdAgents"/>), into <see cref="Inst"/>. Also feeds the agents the tier changes they stand in for: the
/// host's merges and releases (<see cref="HordeCrowd.Notes"/>), a client's individuals appearing out of the crowd and
/// going back into it. The game draws through one of these (Program.DrawCrowd); the self-test and the crowd bench run the
/// same. Nothing allocates per frame.
/// </summary>
public sealed class CrowdView
{
    public readonly CrowdInstances Inst;
    public readonly CrowdAgents Host, Client;

    /// <summary>The host draws its own picture of the cells, as a client would.</summary>
    public bool AsClient;

    /// <summary>The debug tier colours.</summary>
    public bool Tiers;

    /// <summary>Wall-clock ms of the last <see cref="Prepare"/>.</summary>
    public double PrepMs;

    /// <summary>The agents the last frame used.</summary>
    public CrowdAgents Agents => _lastHost ? Host : Client;

    private bool _lastHost;
    private Vector3 _lastCam = new(float.MaxValue);

    /// <summary>The camera moving further than this (m) in a frame is a cut.</summary>
    public const float CutMetres = 12f;
    private double _last = double.NaN;
    private long _notes;
    private Game? _for;
    private int _indN;
    private readonly ushort[] _ind = new ushort[Game.MaxZombies];
    private readonly float[] _indX = new float[Game.MaxZombies], _indZ = new float[Game.MaxZombies], _indYaw = new float[Game.MaxZombies], _indWalk = new float[Game.MaxZombies];
    private readonly byte[] _indGait = new byte[Game.MaxZombies];
    private readonly long[] _seen = new long[65536];
    private long _frame;

    public CrowdView(CrowdJobs? jobs = null)
    {
        Inst = new CrowdInstances(jobs);
        Host = new CrowdAgents(jobs);
        Client = new CrowdAgents(jobs);
    }

    /// <summary>
    /// This frame's instances for <paramref name="g"/> from <paramref name="cam"/> at <paramref name="now"/> (the view
    /// frustum is <see cref="ZombieAnim.SetFrustum"/>'s); <paramref name="fogEnd"/>: past it nothing is drawn (0: no fog).
    /// </summary>
    public void Prepare(Game g, Vector3 cam, double now, float fogEnd)
    {
        var t0 = Stopwatch.GetTimestamp();
        var rules = g.ZRules.Crowd;
        if (!ReferenceEquals(_for, g))
        {
            // A new session: nothing carried over.
            Host.Clear();
            Client.Clear();
            _for = g;
            _notes = g.Crowd.Notes;
            _indN = 0;
            _last = double.NaN;
        }
        var dt = double.IsNaN(_last) ? 0f : (float)Math.Clamp(now - _last, 0, 0.1);
        _last = now;
        var inst = Inst;
        inst.DebugMix = Tiers ? 1f : 0f;
        inst.Begin(cam);
        var host = g.IsServer && g.Crowd.Total > 0 && !AsClient;
        if (host != _lastHost)
            (host ? Host : Client).Clear();
        _lastHost = host;
        var agents = host ? Host : Client;
        g.DrawnCrowd = agents; // (the game's crowd hit effects: ZombiesCrowdFx.cs)
        // A camera cut (a respawn, the spectator camera, a shot tool's next view): nobody saw the crowd in between, so the
        // agents start afresh, every cell filled at once, rather than catching up on caps and views.
        if (Vector3.DistanceSquared(cam, _lastCam) > CutMetres * CutMetres)
            agents.Clear();
        _lastCam = cam;
        (agents.DrawCap, agents.FarCap, agents.FarFrom, agents.FarTo, agents.JamCount) =
            (rules.DrawCap, rules.DrawCapFar, rules.DrawFarFrom, rules.DrawFarTo, rules.MassCapacity);
        // The round's pace: the host's own crowd's; a client works it out from the round it is sent (Msg.ZWorld), as the
        // host did (CrowdPace.For), so its drawn crowd walks, runs and sprints as the round's individuals do.
        agents.SetPace(host ? g.Crowd.Pace : CrowdPace.For(g.ZRules, rules, g.ZRound));
        (agents.NearRange, agents.MidRange) = (inst.NearRange, inst.MidRange);
        var soft = host ? g.Crowd.Tuning.SoftWake : rules.SoftWake;
        agents.PullFromBehind = soft;
        if (host)
        {
            inst.AgentShade = Tiers ? 0.55f : 1f; // (with the tier colours the mass is a darker yellow)
            agents.FromMass(g.Crowd);
            Notes(g.Crowd, agents, soft);
            if (soft)
                HandOver(g.Crowd, agents);
        }
        else
        {
            inst.AgentShade = 1f;
            agents.FromPicture(g.CrowdSeen, World.Map.Terrain);
            Individuals(g, agents, soft);
        }
        agents.Update(cam, dt, fogEnd, inst);
        inst.CountCells(agents);
        inst.End();
        inst.Agents = inst.Total;
        if (host)
        {
            inst.FromParticles(g.Crowd, cam, fogEnd, agents, g.ServerTick, dt);
            inst.End();
        }
        PrepMs = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
    }

    /// <summary>The host: the crowd's merges into the mass (an agent appears where the particle was) and releases from it (the nearest agent sinks).</summary>
    private void Notes(HordeCrowd c, CrowdAgents agents, bool soft)
    {
        var from = Math.Max(_notes, c.Notes - HordeCrowd.NoteCap);
        for (var k = from; k < c.Notes; k++)
        {
            var j = (int)(k & (HordeCrowd.NoteCap - 1));
            if (c.NoteKind[j] == 1)
                agents.Hint(c.NoteX[j], c.NoteZ[j], c.NoteLook[j], c.NoteYaw[j], c.NoteWalk[j], c.NoteGait[j]);
            else if (!soft)
                agents.Release(c.NoteX[j], c.NoteZ[j]); // (the old way: the nearest sinks while the particle rises)
        }
        _notes = c.Notes;
    }

    /// <summary>Since the start: particles let out of the mass that took over an agent, and those with none to take over (their cell wasn't drawn).</summary>
    public long HandOvers, HandMisses;

    /// <summary>The soft wake's hand-over is eased out at this speed (m/s), between these times (s).</summary>
    public const float HandSpeed = 1.2f, HandMin = 0.2f, HandMax = 2f;

    /// <summary>
    /// The host, the seamless tier change (docs/zombies/MASS-HORDE.md, "Wake waves"): each particle the mass let out since
    /// the last frame takes over the agent nearest it (<see cref="CrowdAgents.TakeOver"/>): it is drawn from that agent's
    /// place, facing its way, at its point in the walk and with its look, the offset to where the particle really is eased
    /// out over a moment (<see cref="HordeCrowd.HandWeight"/>), and the agent stops drawing that frame. Before the agents'
    /// update, so the agent isn't drawn this frame. A serial scan of the particles' hand-off flags, only when some are due.
    /// </summary>
    private void HandOver(HordeCrowd c, CrowdAgents agents)
    {
        if (!c.HandPending)
            return;
        c.HandPending = false;
        var within = c.MassCell * 3f; // (the drawn crowd can trail the counts by a cell or two at a moving front)
        for (var i = 0; i < c.Count; i++)
        {
            if (c.HandLeft[i] != -1f)
                continue;
            if (agents.TakeOver(c.X[i], c.Z[i], within, c.Cls[i], counted: false, out var pose) < 0)
            {
                c.HandLeft[i] = 0;
                HandMisses++;
                continue;
            }
            float ox = pose.X - c.X[i], oz = pose.Z - c.Z[i];
            c.HandX[i] = ox;
            c.HandZ[i] = oz;
            c.HandYaw[i] = pose.Yaw;
            c.Walk[i] = pose.Walk;
            c.DrawLook[i] = pose.Look;
            c.HandDur[i] = c.HandLeft[i] = Math.Clamp(MathF.Sqrt(ox * ox + oz * oz) / HandSpeed, HandMin, HandMax);
            HandOvers++;
        }
    }

    /// <summary>
    /// A client: individuals that appeared since the last frame claim the nearest agent (it hides: the zombie came out of
    /// the crowd there); ones that vanished alive leave an agent where they were (back into the crowd).
    /// </summary>
    private void Individuals(Game g, CrowdAgents agents, bool soft)
    {
        var frame = ++_frame;
        foreach (var z in g.Horde)
        {
            if (!z.Alive)
                continue;
            if (_seen[z.Id] != frame - 1 && !z.CrowdTook)
                agents.Claim(z.RenderPos.X, z.RenderPos.Z); // (the old way; the soft wake's took over its agent in the game's frame: Game.CrowdHandFrame)
            _seen[z.Id] = frame;
        }
        // Last frame's living individuals not seen alive this frame and not dead in the horde: gone back into the crowd.
        for (var k = 0; k < _indN; k++)
            if (_seen[_ind[k]] != frame && !InHorde(g, _ind[k]))
            {
                if (soft)
                    agents.Hint(_indX[k], _indZ[k], 0, _indYaw[k], _indWalk[k], _indGait[k]); // (it carries on as it was drawn)
                else
                    agents.Hint(_indX[k], _indZ[k], 0);
            }
        _indN = 0;
        foreach (var z in g.Horde)
            if (z.Alive && _indN < _ind.Length)
            {
                _ind[_indN] = z.Id;
                _indX[_indN] = z.RenderPos.X;
                _indZ[_indN] = z.RenderPos.Z;
                _indYaw[_indN] = z.RenderYaw;
                _indWalk[_indN] = z.Gait / MathF.Tau * 4f * ZombieAnim.StrideHalf(z.Speed, z.Limbs);
                _indGait[_indN] = (byte)Math.Min((int)z.Speed, 2);
                _indN++;
            }
    }

    private static bool InHorde(Game g, ushort id)
    {
        foreach (var z in g.Horde)
            if (z.Id == id)
                return true;
        return false;
    }
}
