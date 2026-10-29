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
    private readonly float[] _indX = new float[Game.MaxZombies], _indZ = new float[Game.MaxZombies];
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
        (agents.DrawCap, agents.FarCap, agents.FarFrom, agents.FarTo, agents.Speed, agents.JamCount) =
            (rules.DrawCap, rules.DrawCapFar, rules.DrawFarFrom, rules.DrawFarTo, rules.Speed, rules.MassCapacity);
        (agents.NearRange, agents.MidRange) = (inst.NearRange, inst.MidRange);
        if (host)
        {
            inst.AgentShade = Tiers ? 0.55f : 1f; // (with the tier colours the mass is a darker yellow)
            agents.FromMass(g.Crowd);
            Notes(g.Crowd, agents);
        }
        else
        {
            inst.AgentShade = 1f;
            agents.FromPicture(g.CrowdSeen, World.Map.Terrain);
            Individuals(g, agents);
        }
        agents.Update(cam, dt, fogEnd, inst);
        inst.CountCells(agents);
        inst.End();
        inst.Agents = inst.Total;
        if (host)
        {
            inst.FromParticles(g.Crowd, cam, fogEnd, agents, g.ServerTick);
            inst.End();
        }
        PrepMs = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
    }

    /// <summary>The host: the crowd's merges into the mass (an agent appears where the particle was) and releases from it (the nearest agent sinks).</summary>
    private void Notes(HordeCrowd c, CrowdAgents agents)
    {
        var from = Math.Max(_notes, c.Notes - HordeCrowd.NoteCap);
        for (var k = from; k < c.Notes; k++)
        {
            var j = (int)(k & (HordeCrowd.NoteCap - 1));
            if (c.NoteKind[j] == 1)
                agents.Hint(c.NoteX[j], c.NoteZ[j], c.NoteLook[j]);
            else
                agents.Release(c.NoteX[j], c.NoteZ[j]);
        }
        _notes = c.Notes;
    }

    /// <summary>
    /// A client: individuals that appeared since the last frame claim the nearest agent (it hides: the zombie came out of
    /// the crowd there); ones that vanished alive leave an agent where they were (back into the crowd).
    /// </summary>
    private void Individuals(Game g, CrowdAgents agents)
    {
        var frame = ++_frame;
        foreach (var z in g.Horde)
        {
            if (!z.Alive)
                continue;
            if (_seen[z.Id] != frame - 1)
                agents.Claim(z.RenderPos.X, z.RenderPos.Z);
            _seen[z.Id] = frame;
        }
        // Last frame's living individuals not seen alive this frame and not dead in the horde: gone back into the crowd.
        for (var k = 0; k < _indN; k++)
            if (_seen[_ind[k]] != frame && !InHorde(g, _ind[k]))
                agents.Hint(_indX[k], _indZ[k], 0);
        _indN = 0;
        foreach (var z in g.Horde)
            if (z.Alive && _indN < _ind.Length)
            {
                _ind[_indN] = z.Id;
                _indX[_indN] = z.RenderPos.X;
                _indZ[_indN] = z.RenderPos.Z;
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
