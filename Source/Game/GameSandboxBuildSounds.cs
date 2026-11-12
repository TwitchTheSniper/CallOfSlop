using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// What building sounds like on this peer (Audio/BuildSounds.cs; docs/sandbox/CONSTRUCTS.md "Building sounds"), worked out
/// from what every peer already has, so nothing goes over the wire: the structure the server confirmed (a construct's part
/// and link counts rising or falling, a body appearing or going) and the hands in the Sandbox state tail (edits in progress,
/// the physics gun's hold). A change only sounds with a player holding a tool near it, so damage breaking a part off, a joiner
/// being sent the world and machines making parts stay quiet. Runs on the host the same way, once a frame. Nothing allocates.
/// </summary>
public sealed partial class Game
{
    /// <summary>How far from a player's eye a new part counts as theirs (m): beyond any tool's reach, for paste.</summary>
    private const float BuildAddReach = 40f;

    /// <summary>...and a construct losing a part, past the construct's own radius.</summary>
    private const float BuildCutReach = 12f;

    /// <summary>More new bodies than this in a frame is a world arriving, not someone building.</summary>
    private const int BuildFreshMax = 8;

    /// <summary>The frames were away this long (s): what changed meanwhile is not news.</summary>
    private const double BuildAwayGap = 1.0;

    private readonly byte[] _bsEdit = new byte[256]; // the edit each player was last seen doing
    private readonly bool[] _bsWeld = new bool[256]; // ...with the welder
    private readonly bool[] _bsNearEnd = new bool[256]; // ...nearly finished
    private readonly Vector3[] _bsAt = new Vector3[256]; // ...where
    private readonly double[] _bsRemovedAt = new double[256]; // when each last had a Remove going
    private readonly ushort[] _bsGrab = new ushort[256]; // what each held with the physics gun
    private readonly ushort[] _bsHeld = new ushort[256]; // the part each had in their hands
    private readonly float[] _bsLoopDist = new float[BuildSounds.LoopVoices];
    private readonly Vector3[] _bsLoopAt = new Vector3[BuildSounds.LoopVoices];
    private readonly bool[] _bsLoopWeld = new bool[BuildSounds.LoopVoices];
    private int _bsGone, _bsWorldLinks, _bsCrossWires;
    private double _bsLast = double.NegativeInfinity;

    /// <summary>Every Sandbox frame (Program.DrawSandbox).</summary>
    internal void SandboxBuildSounds(double now)
    {
        if (!Audio.Enabled)
            return;
        BuildSounds.Bind(Audio);
        BuildSounds.Tick();
        var away = now - _bsLast > BuildAwayGap;
        _bsLast = now;
        BuildHands(now, away);
        BuildBodies(now, away);
        StationSounds();
        var worldLinks = Constructs.WorldLinkCount;
        if (!away && worldLinks > _bsWorldLinks && BuildNear(Audio.ListenerPosition, 100f) is { } layer)
            BuildSounds.Play(LinkSound(Constructs.WorldLinks[worldLinks - 1].Type), BuildAim(layer));
        _bsWorldLinks = worldLinks;
        var crossWires = Constructs.XWireCount;
        if (!away && crossWires > _bsCrossWires && BuildNear(Audio.ListenerPosition, 100f) is { } wirer)
            BuildSounds.Play(BuildSound.LinkCable, BuildAim(wirer));
        _bsCrossWires = crossWires;
    }

    private static BuildSound LinkSound(RouteType type) => type is RouteType.Data or RouteType.Power ? BuildSound.LinkCable : BuildSound.LinkPipe;

    /// <summary>Where a player works: a little in front of their eyes (the edits on the wire don't say which part).</summary>
    private static Vector3 BuildAim(Player p) => p.Eye + World.Forward(p.Yaw, p.Pitch) * 1.5f;

    /// <summary>The nearest live player to <paramref name="at"/> with a building tool out, within <paramref name="reach"/>.</summary>
    private Player? BuildNear(Vector3 at, float reach)
    {
        Player? best = null;
        var bestD = reach * reach;
        foreach (var p in Players.Values)
        {
            if (!p.Alive)
                continue;
            ref var h = ref SandboxHands.Hand(p.Id);
            if (h.Tool is ConstructTool.None or ConstructTool.PhysicsGun or ConstructTool.Hands || h.Seated)
                continue;
            var d = Vector3.DistanceSquared(p.Eye, at);
            if (d <= bestD)
            {
                bestD = d;
                best = p;
            }
        }
        return best;
    }

    /// <summary>Everyone's edits in progress (the nearest few as loops), welds finishing and the physics gun's hold.</summary>
    private void BuildHands(double now, bool away)
    {
        var loops = 0;
        var listener = Audio.ListenerPosition;
        foreach (var p in Players.Values)
        {
            var id = p.Id;
            ref var h = ref SandboxHands.Hand(id);
            var edit = p.Alive ? h.Edit : EditKind.None;
            if (edit != EditKind.None)
            {
                var at = BuildAim(p);
                var weld = edit is EditKind.Weld or EditKind.SocketWeld || h.Tool == ConstructTool.Welder;
                _bsAt[id] = at;
                _bsWeld[id] = weld;
                _bsNearEnd[id] = h.Progress(SandboxNow) >= 0.8f || h.Start + h.Duration - SandboxNow < 0.2;
                if (edit == EditKind.Remove)
                    _bsRemovedAt[id] = now;
                var d = Vector3.DistanceSquared(at, listener);
                var range = BuildSounds.LoopRange(weld);
                if (h.Duration >= 0.15 && d <= range * range)
                    BuildLoopPick(ref loops, d, at, weld);
            }
            else if (_bsEdit[id] != (byte)EditKind.None && _bsWeld[id] && _bsNearEnd[id] && !away)
                BuildSounds.Play(BuildSound.WeldDone, _bsAt[id]); // (a cancelled weld just stops)
            _bsEdit[id] = (byte)edit;
            if (h.GrabBody != _bsGrab[id])
            {
                var body = Constructs.Get(h.GrabBody != 0 ? h.GrabBody : _bsGrab[id]);
                if (!away && p.Alive)
                    BuildSounds.Play(h.GrabBody != 0 ? BuildSound.Grab : BuildSound.Drop, body?.ToWorld(body.Com) ?? BuildAim(p));
                _bsGrab[id] = h.GrabBody;
            }
            if (h.Held != _bsHeld[id])
            {
                // Hands out: the part coming into them or leaving them (a tool's fit has its own sounds).
                var took = _bsHeld[id] == ConstructParts.None;
                if (!away && p.Alive && h.Tool == ConstructTool.Hands && (took || h.Held == ConstructParts.None))
                    BuildSounds.Play(took ? BuildSound.Grab : BuildSound.Drop, BuildAim(p));
                _bsHeld[id] = h.Held;
            }
        }
        for (var v = 0; v < loops; v++)
            BuildSounds.LoopAt(v, _bsLoopWeld[v], _bsLoopAt[v]);
        for (var v = loops; v < BuildSounds.LoopVoices; v++)
            BuildSounds.LoopStop(v);
    }

    /// <summary>Keeps the nearest loops.</summary>
    private void BuildLoopPick(ref int loops, float d, Vector3 at, bool weld)
    {
        var slot = loops < BuildSounds.LoopVoices ? loops++ : -1;
        if (slot < 0)
        {
            var far = 0;
            for (var k = 1; k < BuildSounds.LoopVoices; k++)
                if (_bsLoopDist[k] > _bsLoopDist[far])
                    far = k;
            if (d >= _bsLoopDist[far])
                return;
            slot = far;
        }
        _bsLoopDist[slot] = d;
        _bsLoopAt[slot] = at;
        _bsLoopWeld[slot] = weld;
    }

    /// <summary>Bodies that gained or lost parts or links, appeared or went since the last frame.</summary>
    private void BuildBodies(double now, bool away)
    {
        var fresh = 0;
        foreach (var c in Constructs.Bodies)
        {
            if (c.Dirty)
                continue; // (next frame, rebuilt)
            if (c.HeardParts < 0)
            {
                fresh++;
                continue;
            }
            var links = c.WireCount + c.RouteCount;
            if (c.PartCount == c.HeardParts && links == c.HeardLinks)
                continue;
            if (!away)
            {
                if (c.PartCount > c.HeardParts)
                    BuildAdded(c, c.HeardParts);
                else if (c.PartCount < c.HeardParts)
                    BuildCut(now, c.ToWorld(c.Com), c.Radius, c.HeardParts - c.PartCount);
                else if (links > c.HeardLinks)
                    BuildLinked(c);
            }
            c.HeardParts = c.PartCount;
            c.HeardLinks = links;
        }
        if (fresh > 0)
            foreach (var c in Constructs.Bodies)
            {
                if (c.HeardParts >= 0 || c.Dirty)
                    continue;
                if (!away && fresh <= BuildFreshMax)
                    BuildAdded(c, 0);
                c.HeardParts = c.PartCount;
                c.HeardLinks = c.WireCount + c.RouteCount;
            }
        if (away || Constructs.GoneTotal - _bsGone > Constructs.GoneRing.Length)
            _bsGone = Constructs.GoneTotal;
        for (; _bsGone < Constructs.GoneTotal; _bsGone++)
        {
            ref readonly var g = ref Constructs.GoneRing[_bsGone % Constructs.GoneRing.Length];
            BuildCut(now, g.At, g.Radius, g.Parts);
        }
    }

    private static Vector3 BuildPartAt(Construct c, int part)
    {
        c.WorldFrame(c.Parts[part].Seg, out var q, out var t);
        return t + Vector3.Transform(c.Parts[part].Pos, q);
    }

    /// <summary>Parts <paramref name="from"/> onward are new on <paramref name="c"/>: a clunk for each, batched; a welder joining two constructs is one thud of what it joined.</summary>
    private void BuildAdded(Construct c, int from)
    {
        var last = c.PartCount - 1;
        if (last < from)
            return;
        var at = BuildPartAt(c, last);
        if (BuildNear(at, BuildAddReach) is not { } who)
        {
            at = BuildPartAt(c, from); // (a long line's far end: its near one)
            if (BuildNear(at, BuildAddReach) is not { } nearer)
                return;
            who = nearer;
        }
        float heaviest = 0, total = 0;
        for (var i = from; i <= last; i++)
        {
            var m = c.DefOf(i).Mass;
            heaviest = MathF.Max(heaviest, m);
            total += m;
        }
        var material = BuildSounds.MaterialOf(c.DefOf(last).Material);
        if (SandboxHands.Hand(who.Id).Tool == ConstructTool.Welder)
            BuildSounds.Play(BuildSound.Place, at, total, material);
        else
            BuildSounds.Play(BuildSound.Place, at, heaviest, material, last - from + 1);
    }

    /// <summary><paramref name="parts"/> parts came off, or a body of them went: an unbolt, or the remover's pop. The welder cuts only with a Remove seen going, as its welds take bodies away too.</summary>
    private void BuildCut(double now, Vector3 at, float radius, int parts)
    {
        if (BuildNear(at, radius + BuildCutReach) is not { } who)
            return;
        var tool = SandboxHands.Hand(who.Id).Tool;
        if (tool == ConstructTool.Welder && now - _bsRemovedAt[who.Id] > 0.5)
            return;
        BuildSounds.Play(tool == ConstructTool.Remover ? BuildSound.Pop : BuildSound.Unbolt, BuildAim(who), count: parts);
    }

    /// <summary>A wire or routed link was finished on <paramref name="c"/> (they only come from players).</summary>
    private void BuildLinked(Construct c)
    {
        var sound = BuildSound.LinkCable;
        var at = c.ToWorld(c.Com);
        if (c.RouteCount > 0)
        {
            ref readonly var r = ref c.Routes[c.RouteCount - 1];
            sound = LinkSound(r.Type);
            if (r.To < c.PartCount)
                at = BuildPartAt(c, r.To);
        }
        BuildSounds.Play(sound, at);
    }
}
