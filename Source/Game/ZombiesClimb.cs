using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// A siege up a climbable face (map logic "zclimb"; docs/zombies/ZOMBIES-MODE.md, "The bell: a siege up the tower"; the
/// Cathedral's bell tower). Started, the horde outside leaves the windows and walks to the face's foot; there the
/// zombies pile up against the wall in a living pyramid (each newcomer takes the next place: layer by layer, the
/// middle columns first, the bottom layers two deep), and once the pyramid stands the next ones climb over it and up
/// the bare wall above to the top, vault the parapet and come at whoever is up there. A shot zombie lets go and falls
/// (pushed off the wall, dazed where it lands, then back to the foot); a body that drops out of the pyramid lets
/// everyone above it slide down a place, so shooting the base brings it down. Stopped, the whole pile falls and the
/// horde goes back to the windows.
/// <para>
/// Server-authoritative and cheap: the climbing list is in arrival order, so a zombie's place is its index (one pass a
/// tick), and a zombie on the face is moved straight to its place (no physics, no route). Clients see a zombie state
/// (<see cref="ZState.Scaling"/>, protocol 68) and the positions as for any zombie, and draw a climbing pose
/// (ZAnimMode.Scaling: braced arms up in the pile, hand over hand on the wall). The numbers are the rules file's
/// "climb" section (<see cref="ClimbTuning"/>). Deterministic (no random numbers) and allocation-free per tick.
/// </para>
/// </summary>
public sealed partial class Game
{
    /// <summary>The zclimb entity climbing now (-1: no siege).</summary>
    private int _climbEnt = -1;

    /// <summary>The zombies on the face, in the order they reached it: index = place (the pyramid's slots first, then climbers).</summary>
    private readonly List<Zombie> _climbers = new(MaxZombies);

    private Vector3[] _climbSlots = [];
    private Vector3 _climbWall, _climbOut, _climbAlong, _climbTop;
    private float _climbHalfWidth, _climbTopY, _climbPileTop;
    private Box _climbFoot;
    private int _climbFootNode = -1;
    private NavGraph? _climbFootGraph;
    private bool _climbReached;
    private int _climbJoinSeq;

    /// <summary>Counters for the self-test and the log: zombies recruited from the windows, joined the face, shot off it, over the top, sent back to the windows.</summary>
    public int ClimbRecruited, ClimbJoined, ClimbFell, ClimbOver, ClimbReturned;

    /// <summary>Is a siege on (a zclimb started)?</summary>
    public bool ClimbActive => _climbEnt >= 0;

    /// <summary>Zombies on the face now (in the pyramid and climbing), and the pyramid's height above its foot (m).</summary>
    public int ClimbCount => _climbers.Count;

    public float ClimbPileHeight => _climbEnt >= 0 ? _climbPileTop - _climbWall.Y : 0;

    /// <summary>Places in the pyramid (bodies before the rest climb over it).</summary>
    public int ClimbSlots => _climbSlots.Length;

    /// <summary>Where climbers come over (the landing on top).</summary>
    public Vector3 ClimbTop => _climbTop;

    /// <summary>The face's foot box (the pile's ground).</summary>
    public Box ClimbFoot => _climbFoot;

    // ---- Logic ------------------------------------------------------------------------------------------------

    /// <summary>Server: a zclimb's action: start the siege (only during a round, one at a time), stop it, or pay its reward.</summary>
    private void ClimbAction(LogicState s, int i, LogicAction a, LogicSignal sig)
    {
        var e = s.Def.Entities[i];
        switch (a)
        {
            case LogicAction.Start when _climbEnt < 0 && ZPhase == ZRoundPhase.Round:
                ClimbSetup(e);
                _climbEnt = i;
                _climbReached = false;
                _zToSpawn += ZRules.Climb.ExtraFor(ZRound); // the bell calls more of them
                SetFlag(s, i, LogicState.FlagOn, true);
                Emit(s, i, LogicEvent.Started, sig with { Value = true, N = ZRound });
                break;
            case LogicAction.Stop when _climbEnt == i:
                ClimbStop(_logicNow);
                SetFlag(s, i, LogicState.FlagOn, false);
                Emit(s, i, LogicEvent.Stopped, sig with { Value = false });
                break;
            case LogicAction.GiveAll:
                ClimbReward(e, _logicNow);
                Emit(s, i, LogicEvent.Given, sig);
                break;
        }
    }

    /// <summary>The face from the entity: the wall line (the foot box's side nearest the landing), out from it, along it, and the pyramid's places.</summary>
    private void ClimbSetup(LogicEntity e)
    {
        var c = ZRules.Climb;
        var b = e.Box;
        var top = e.At;
        var ctr = b.Center;
        var gapX = MathF.Abs(top.X - ctr.X) - b.Half.X;
        var gapZ = MathF.Abs(top.Z - ctr.Z) - b.Half.Z;
        if (gapX > gapZ)
        {
            var sx = MathF.Sign(ctr.X - top.X);
            _climbOut = new Vector3(sx, 0, 0);
            _climbWall = new Vector3(ctr.X - sx * b.Half.X, b.Min.Y, ctr.Z);
            _climbHalfWidth = b.Half.Z;
        }
        else
        {
            var sz = MathF.Sign(ctr.Z - top.Z);
            _climbOut = new Vector3(0, 0, sz);
            _climbWall = new Vector3(ctr.X, b.Min.Y, ctr.Z - sz * b.Half.Z);
            _climbHalfWidth = b.Half.X;
        }
        _climbAlong = new Vector3(-_climbOut.Z, 0, _climbOut.X);
        _climbFoot = b;
        _climbTop = top;
        _climbTopY = top.Y + e.Range;
        var cols = Math.Max(1, (int)MathF.Floor(2 * (_climbHalfWidth - 0.3f) / c.ColumnSpacing));
        var slots = new List<Vector3>(256);
        for (var layer = 0; layer < c.Layers; layer++)
        {
            var y = _climbWall.Y + layer * c.LayerHeight;
            if (y + 1.9f > _climbTopY)
                break; // (a body this high already reaches the top)
            var n = Math.Max(1, cols - c.LayerShrink * layer);
            var rows = layer < c.BaseRows ? 2 : 1;
            for (var row = 0; row < rows; row++)
                for (var k = 0; k < n; k++)
                {
                    // The middle first, then out to either side.
                    var off = k == 0 ? 0f : (k & 1) == 1 ? (k + 1) / 2 : -(k / 2);
                    if ((n & 1) == 0)
                        off += 0.5f;
                    var along = MathF.Max(-(_climbHalfWidth - 0.3f), MathF.Min(_climbHalfWidth - 0.3f, off * c.ColumnSpacing));
                    slots.Add(_climbWall + _climbOut * (0.42f + row * 0.7f) + _climbAlong * along + new Vector3(0, y - _climbWall.Y, 0));
                }
        }
        _climbSlots = slots.ToArray();
        _climbers.Clear();
        _climbPileTop = _climbWall.Y;
        _climbFootGraph = null;
        _climbJoinSeq = 0;
    }

    /// <summary>The nav node at the face's foot (worked out once per graph).</summary>
    private int ClimbFootNode()
    {
        var nav = NavGraph.Current;
        if (!ReferenceEquals(_climbFootGraph, nav))
        {
            _climbFootGraph = nav;
            _climbFootNode = nav.Nodes.Length > 0 ? nav.Nearest(ClimbFootPoint, needSight: false) : -1;
        }
        return _climbFootNode;
    }

    /// <summary>Where the walkers head: the middle of the foot, a little out from the wall.</summary>
    private Vector3 ClimbFootPoint => _climbWall + _climbOut * MathF.Min(1.6f, _climbFoot.Half.X + _climbFoot.Half.Z);

    // ---- Every tick ---------------------------------------------------------------------------------------------

    /// <summary>Server, each horde tick before the zombies think: drops whoever left the face, numbers the rest (their places) and the pyramid's height.</summary>
    private void ClimbTick()
    {
        if (_climbEnt < 0)
        {
            if (_climbers.Count > 0)
                _climbers.Clear();
            return;
        }
        var k = 0;
        var pile = 0;
        for (var i = 0; i < _climbers.Count; i++)
        {
            var z = _climbers[i];
            if (z.State != ZState.Scaling)
            {
                z.ScaleRank = -1;
                continue;
            }
            if (!z.ScaleClimber && pile >= _climbSlots.Length)
                z.ScaleClimber = true; // the pyramid is full: over it
            z.ScaleRank = z.ScaleClimber ? _climbSlots.Length : pile++;
            _climbers[k++] = z;
        }
        if (k < _climbers.Count)
            _climbers.RemoveRange(k, _climbers.Count - k);
        _climbPileTop = _climbWall.Y;
        for (var i = 0; i < pile; i++)
            _climbPileTop = MathF.Max(_climbPileTop, _climbSlots[i].Y + ZRules.Climb.LayerHeight);
    }

    /// <summary>Server: a zombie at a window during a siege leaves it for the face.</summary>
    private void ClimbRecruit(Zombie z, double now)
    {
        z.Window = -1;
        z.SiegeOut = true;
        z.Door = -1;
        z.Hm.Node = -1;
        z.Hm.Straight = false;
        z.StuckAt = now;
        z.StuckPos = z.Position;
        SetZState(z, ZState.Chase, now);
        ClimbRecruited++;
    }

    /// <summary>
    /// Server: a zombie outside (from a window, or fallen off the face): during a siege it walks to the face's foot and
    /// joins the climb there; otherwise it goes back to the nearest window whose area is open.
    /// </summary>
    private void SiegeWalk(ZombiesMap zm, Zombie z, double now)
    {
        if (_climbEnt < 0)
        {
            var best = -1;
            var bestD = float.MaxValue;
            for (var w = 0; w < zm.Windows.Count; w++)
            {
                if (!AreaOpen(zm.Windows[w].Area))
                    continue;
                var d = Vector3.DistanceSquared(zm.Windows[w].Outside, z.Position);
                if (d < bestD)
                {
                    bestD = d;
                    best = w;
                }
            }
            if (best < 0)
            {
                MoveZombie(z, z.Yaw, false, false);
                return;
            }
            z.SiegeOut = false;
            z.Window = best;
            z.Hm.Node = -1;
            z.StuckAt = now;
            z.StuckPos = z.Position;
            SetZState(z, ZState.ToWindow, now);
            ClimbReturned++;
            return;
        }
        var p = z.Position;
        var f = _climbFoot;
        if (p.X >= f.Min.X - 0.4f && p.X <= f.Max.X + 0.4f && p.Z >= f.Min.Z - 0.4f && p.Z <= f.Max.Z + 0.4f && p.Y >= f.Min.Y - 0.6f && p.Y <= f.Max.Y + 1f && z.Sim.Grounded)
        {
            // At the foot: onto the face (the next place in the pyramid, or over it).
            SetZState(z, ZState.Scaling, now);
            z.VaultAt = -1;
            // One in climbEvery goes straight up the wall (the rest build the pyramid), so the top is under attack from the first minute.
            var every = ZRules.Climb.ClimbEvery;
            z.ScaleClimber = every > 0 && _climbJoinSeq++ % every == every - 1;
            z.ScaleRank = z.ScaleClimber ? _climbSlots.Length : Math.Min(_climbSlots.Length, _climbers.Count);
            _climbers.Add(z);
            ClimbJoined++;
            ScaleMove(zm, z, now);
            return;
        }
        if (HordeAI.Fleeing(z, out var fy))
        {
            MoveZombie(z, HordeAI.Separate(z, fy), true, false); // scattering from a blast
            return;
        }
        var goal = ClimbFootPoint;
        var step = HordeAI.Steer(z, goal, ClimbFootNode(), out var jump);
        var to = step - p;
        var yaw = MathF.Atan2(to.X, -to.Z);
        if (now < z.UnstickUntil)
            yaw = z.UnstickYaw;
        ZStuckWatch(z, yaw, goal, now);
        MoveZombie(z, HordeAI.Separate(z, yaw), true, jump);
    }

    /// <summary>A small per-zombie offset in -1..1 from its id (climbers spread along the face; the pyramid's bodies sway a little).</summary>
    private static float ClimbJitter(ushort id, int salt)
    {
        var h = (uint)(id * 2654435761u) ^ (uint)salt * 40503u;
        h ^= h >> 15;
        h *= 2246822519u;
        h ^= h >> 13;
        return (h & 1023) / 511.5f - 1f;
    }

    /// <summary>Server: one tick of a zombie on the face: to its place in the pyramid, or up over it and the wall, then over the top.</summary>
    private void ScaleMove(ZombiesMap zm, Zombie z, double now)
    {
        var c = ZRules.Climb;
        const float dt = World.TickDt;
        z.Sim.Velocity = Vector3.Zero;
        z.Sim.Grounded = true;
        z.Yaw = MathF.Atan2(-_climbOut.X, _climbOut.Z); // facing the wall
        if (_climbEnt < 0 && !(z.VaultAt >= 0 && now - z.VaultAt > c.VaultSeconds * 0.5))
        {
            ClimbFall(z, now);
            return;
        } // (over the top already when the siege stopped: it finishes the vault, or it would stay on the face for good)
        if (z.VaultAt >= 0)
        {
            // Over the parapet onto the landing.
            var t = (float)Math.Clamp((now - z.VaultAt) / c.VaultSeconds, 0, 1);
            var v = Vector3.Lerp(z.VaultFrom, _climbTop, t);
            v.Y = MathF.Max(v.Y, _climbTop.Y) + MathF.Sin(t * MathF.PI) * 0.5f;
            z.Sim.Position = t >= 1 ? _climbTop : v;
            if (t >= 1)
            {
                z.VaultAt = -1;
                z.SiegeOut = false;
                z.ScaleRank = -1;
                z.ScaleClimber = false;
                z.Area = zm.AreaAt(_climbTop);
                z.Hm.Node = -1;
                z.Hm.Straight = false;
                z.StuckAt = now;
                z.StuckPos = z.Position;
                z.NextSwing = now + 0.4;
                SetZState(z, ZState.Chase, now);
                ClimbOver++;
                if (!_climbReached && _climbEnt >= 0 && Logic is { } s)
                {
                    _climbReached = true;
                    Emit(s, _climbEnt, LogicEvent.Reached, LogicSignal.Pulse(n: ZRound));
                }
            }
            return;
        }
        var pos = z.Sim.Position;
        var rank = z.ScaleRank;
        if (rank >= 0 && rank < _climbSlots.Length)
        {
            // A body in the pyramid: to its place (it slides down as the ones under it are shot away).
            var target = _climbSlots[rank] + _climbAlong * (ClimbJitter(z.Id, 1) * 0.12f) + _climbOut * (ClimbJitter(z.Id, 2) * 0.06f);
            var d = target - pos;
            var len = d.Length();
            var stepLen = c.SettleSpeed * dt;
            z.Sim.Position = len <= stepLen ? target : pos + d * (stepLen / len);
            return;
        }
        // A climber: up the pyramid (over the bodies, quick), then hand over hand up the bare wall.
        var onPile = pos.Y < _climbPileTop - 0.2f;
        var h = pos.Y + (onPile ? c.PileClimbSpeed : c.WallClimbSpeed) * dt;
        var lat = ClimbJitter(z.Id, 3) * MathF.Max(0.1f, _climbHalfWidth - 1.2f) * 0.6f;
        var flat = _climbWall + _climbOut * (onPile ? 1.1f : 0.42f) + _climbAlong * lat;
        var fd = new Vector3(flat.X - pos.X, 0, flat.Z - pos.Z);
        var fl = fd.Length();
        var fs = c.SettleSpeed * dt;
        var np = fl <= fs ? new Vector3(flat.X, h, flat.Z) : new Vector3(pos.X + fd.X * fs / fl, h, pos.Z + fd.Z * fs / fl);
        z.Sim.Position = np;
        if (np.Y >= _climbTopY)
        {
            z.VaultAt = now;
            z.VaultFrom = np;
        }
    }

    /// <summary>Server: a zombie on the face lets go (shot, or the siege is over): it drops off the wall and lies dazed where it lands.</summary>
    private void ClimbFall(Zombie z, double now)
    {
        if (z.State != ZState.Scaling)
            return;
        var c = ZRules.Climb;
        var p = z.Sim.Position;
        if (z.VaultAt >= 0 && now - z.VaultAt > c.VaultSeconds * 0.5)
            return; // (over the top already: it lands inside)
        var height = MathF.Max(0, p.Y - _climbWall.Y);
        z.VaultAt = -1;
        z.ScaleRank = -1;
        z.ScaleClimber = false;
        z.SiegeOut = true;
        z.Sim.Position = p + _climbOut * 0.35f;
        z.Sim.Velocity = _climbOut * c.FallPush + new Vector3(0, 1f, 0);
        z.Sim.Grounded = false;
        z.StunnedUntil = Math.Max(z.StunnedUntil, now + MathF.Sqrt(2 * height / 9.8f) + c.FallStun);
        z.Hm.Node = -1;
        z.StuckAt = now + 2;
        z.StuckPos = z.Position;
        SetZState(z, ZState.Chase, now);
        ClimbFell++;
    }

    /// <summary>Server: the siege is over: every zombie on the face falls; the walkers go back to the windows.</summary>
    private void ClimbStop(double now)
    {
        _climbEnt = -1;
        foreach (var z in _climbers)
            ClimbFall(z, now); // (the face's numbers stay until the next siege)
        _climbers.Clear();
    }

    /// <summary>
    /// Server: surviving the siege pays every player standing: points (by round), loose parts rolled as if the round
    /// were later (rarer), and a power-up dropped on the landing.
    /// </summary>
    private void ClimbReward(LogicEntity e, double now)
    {
        if (ZMap == null || Mode != GameMode.Zombies)
            return;
        var c = ZRules.Climb;
        var pts = c.RewardFor(ZRound);
        foreach (var p in Players.Values)
        {
            if (!IsStanding(p))
                continue;
            if (pts > 0)
                AddPoints(p, pts);
            var zp = ZP(p);
            var got = 0;
            for (var k = 0; k < c.RewardParts; k++)
            {
                var i = PickSlotPart(RunTables.Roll(RunTables.Current.PartDrops, ZRound + c.RewardRoundBonus, ZNext()), ZNext());
                if (i < 0)
                    continue;
                var part = Parts.All[i];
                zp.Parts[part.Name] = zp.Parts.GetValueOrDefault(part.Name) + 1;
                got++;
            }
            if (got > 0)
            {
                ZFeedTo(p, $"The siege is broken: {pts} points and {got} part{(got == 1 ? "" : "s")} for the bell. Fit them at a workbench.", Sfx.StreakReady);
                SendZRun(p);
            }
        }
        if (c.RewardPowerUp >= 0)
            SpawnPowerUp((ZPowerKind)c.RewardPowerUp, e.At, now);
    }

    /// <summary>A new run or map: no siege.</summary>
    private void ClimbReset()
    {
        foreach (var z in _climbers)
            z.ScaleRank = -1;
        _climbers.Clear();
        _climbEnt = -1;
        _climbReached = false;
        _climbFootGraph = null;
    }

    // ---- Self-test hooks ------------------------------------------------------------------------------------------

    /// <summary>Self-test: the zombies on the face in order (read only).</summary>
    public IReadOnlyList<Zombie> SelfTestClimbers => _climbers;

    /// <summary>Self-test: the pyramid's places.</summary>
    public ReadOnlySpan<Vector3> SelfTestClimbSlots => _climbSlots;

    /// <summary>Self-test: one server tick of the map logic and the Zombies run (the bell, the climb, the horde) at <paramref name="now"/>.</summary>
    public void SelfTestSiegeTick(double now)
    {
        ServerTick++;
        ServerTickLogic(now);
        if (Phase == MatchPhase.Playing)
            ServerTickZombies(now);
    }

    /// <summary>Self-test: a map logic entity's index by name (-1: none).</summary>
    public int SelfTestLogicIndex(string name) => Logic?.Def.Find(name) ?? -1;
}
