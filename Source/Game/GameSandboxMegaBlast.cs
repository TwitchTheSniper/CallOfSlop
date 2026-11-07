using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Massive blasts on the game's side (ConstructMegaBlast.cs; docs/sandbox/CONSTRUCTS.md, "Massive blasts"): the players a shock front passes
/// each tick, and big craters dug a band at a time. A crater is one op per band (<see cref="DigOp.Slice"/>), each logged and sent as any op is,
/// so a joiner replays the bands and every peer digs them in order; one band a tick, sized so no tick digs more than
/// <see cref="MegaDigColumnsPerTick"/> columns of it. The host's cap on blast size is <see cref="SetBlastCap"/>.
/// </summary>
public sealed partial class Game
{
    /// <summary>Columns of ground a band of a big crater digs at most (a 120 m crater: about 52 bands, under a second).</summary>
    public const int MegaDigColumnsPerTick = 3500;

    /// <summary>Big craters waiting for their bands at once (more are dug as one op: rare, and still bounded by the crater cap).</summary>
    public const int MaxMegaDigs = 8;

    private struct MegaDig
    {
        public Vector3 At;
        public float Radius;
        public byte Next, Slices;
        public int Wait; // ticks before its first band: it is dug behind the shock front, once what stood there is gone
    }

    private readonly MegaDig[] _megaDigs = new MegaDig[MaxMegaDigs];
    private int _megaDigCount;

    /// <summary>Big craters still being dug.</summary>
    public int MegaDigsWaiting => _megaDigCount;

    /// <summary>Bands dug and their columns (the bench, the perf window).</summary>
    public int MegaDigBands, MegaDigColumns;

    /// <summary>The longest band dug so far (ms: the bench; reset by hand).</summary>
    public float MegaDigWorstMs;

    /// <summary>The host's cap on any blast's radius in metres (the lobby's "Biggest blast"; 0: only the engine's, <see cref="ConstructWorld.MegaMaxRadius"/>).</summary>
    public void SetBlastCap(int metres) => Constructs.BlastCap = Math.Max(0, metres);

    void IConstructTargets.MegaGround(Vector3 at, float crater)
    {
        if (!IsServer || !TerrainDigLive || !(crater > 0))
            return;
        var r = Math.Clamp(crater, CraterMin, ConstructWorld.MegaCraterMax);
        var size = World.Map.Terrain is { } t ? t.Edits.Size : TerrainEdits.TargetSize;
        var across = 2f * r / size;
        var slices = Math.Clamp((int)MathF.Ceiling(across * across * 0.785f / MegaDigColumnsPerTick), 1, 255);
        if (_megaDigCount >= MaxMegaDigs)
        {
            Dig(new DigOp(DigKind.Crater, at, MathF.Min(r, BigCraterMax))); // (a ninth waiting crater: a prop bomb's at most, at once)
            return;
        }
        var wait = (int)MathF.Ceiling(r * 1.2f / MegaBlasts.FrontSpeed(MathF.Max(r, ConstructWorld.MegaRadius)) * World.TickRate);
        _megaDigs[_megaDigCount++] = new MegaDig { At = at, Radius = r, Slices = (byte)slices, Wait = wait };
    }

    /// <summary>Server, each tick after the constructs' step: the first waiting crater digs its next band.</summary>
    private void MegaDigTick()
    {
        if (_megaDigCount == 0)
            return;
        ref var d = ref _megaDigs[0];
        if (d.Wait > 0)
        {
            for (var k = 0; k < _megaDigCount; k++)
                _megaDigs[k].Wait--;
            return;
        }
        var op = d.Slices <= 1 ? new DigOp(DigKind.Crater, d.At, d.Radius) : new DigOp(DigKind.Crater, d.At, d.Radius, d.Next, d.Slices);
        var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        MegaDigColumns += Dig(op);
        MegaDigWorstMs = MathF.Max(MegaDigWorstMs, (float)System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds);
        MegaDigBands++;
        if (++d.Next < Math.Max((byte)1, d.Slices))
            return;
        for (var k = 1; k < _megaDigCount; k++)
            _megaDigs[k - 1] = _megaDigs[k];
        _megaDigCount--;
    }

    void IConstructTargets.MegaFront(Vector3 at, float radius, float damage, float from, float to, Construct? by, int gunPart)
    {
        if (!IsServer)
            return;
        var now = SandboxNow;
        Player? credit = null;
        var credited = false;
        foreach (var victim in Players.Values)
        {
            if (!victim.Alive)
                continue;
            var chest = victim.Chest;
            var dist = Vector3.Distance(chest, at);
            if (dist <= from || dist > to || dist > radius)
                continue;
            if (!credited)
            {
                credit = ConstructCredit(by, gunPart);
                credited = true;
            }
            var k = 1f - dist / radius;
            // Near the middle nothing shields; further out a hill or a hull does, as for any blast.
            if (k < 0.5f && dist > 0.3f && (World.Query.Raycast(at, (chest - at) / dist, dist, QueryFilter.All) < dist - 0.05f || Throwables.ConstructCover(at, (chest - at) / dist, dist)))
                continue;
            var amount = (int)MathF.Min(int.MaxValue / 2f, MathF.Round(damage * k * k * 0.5f)); // (players take half what parts do, as from any blast)
            if (amount > 0)
                DamagePlayer(credit ?? victim, victim, amount, false, Throwables.KillIdBase, now, DamageKind.Explosive, at);
            if (victim.Alive)
            {
                var push = Knockback.Impulse(at, radius, MegaKnock, chest, victim.Sim.Stance == Stance.Prone, victim.Perks.Has(PerkId.FlakJacket));
                if (push != Vector3.Zero)
                    Knock(victim, push);
            }
        }
        if (from < 0 && Horde.Count > 0)
        {
            credit ??= ConstructCredit(by, gunPart);
            if (credit != null)
                BlastZombies(credit, at, radius, damage, now); // (zombies all at once, on its first tick)
        }
    }

    private const float MegaKnock = 60f; // (the Small Nuke's knockback)

    /// <summary>Each frame, every PC: the massive blasts being drawn (MegaBlastFx.cs), heard and felt at the local player's ear.</summary>
    public void MegaFxFrame(double now, float dt)
    {
        if (MegaBlastFx.Any)
            MegaBlastFx.Frame(now, dt, Local?.RenderEye ?? Vector3.Zero, Audio, ref Shake);
    }
}
