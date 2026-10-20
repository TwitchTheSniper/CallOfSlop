using Brutal.JoltApi;
using Brutal.Numerics;
using static JoltSpike.Report;

namespace JoltSpike;

/// <summary>Sections c (determinism, reconciliation) and the movement half of d (performance).</summary>
public static class TestsDeterminism
{
    public const int Bots = 8, Ticks = 600;

    static readonly float3[] Spawns =
    [
        new(0, 0.05f, TestWorld.LaneStairs), new(0, 0.05f, TestWorld.LaneRamp30), new(-2, 0.05f, TestWorld.LaneCeiling),
        new(0, 1.2f, TestWorld.LaneMover), new(4, 1.2f, TestWorld.LaneSpinner), new(50, 10, 0), new(70, 10, 12), new(90, 10, -20),
    ];

    /// <summary>Recorded random inputs: each bot changes its mind every 20-40 ticks, like a twitchy player.</summary>
    public static Input[][] MakeInputs(int bots, int ticks, uint seed = 77)
    {
        var all = new Input[bots][];
        for (var b = 0; b < bots; b++)
        {
            var rng = new Rng(seed + (uint)b * 7919);
            var list = new Input[ticks];
            var t = 0;
            while (t < ticks)
            {
                var len = 20 + rng.Int(21);
                var yaw = rng.Range(0, 6.2831855f);
                var btn = Btn.None;
                if (rng.Float() < 0.75f) btn |= Btn.Forward;
                else if (rng.Float() < 0.3f) btn |= Btn.Back;
                if (rng.Float() < 0.2f) btn |= rng.Int(2) == 0 ? Btn.Left : Btn.Right;
                if (rng.Float() < 0.5f) btn |= Btn.Sprint;
                var stance = rng.Float();
                if (stance < 0.1f) btn |= Btn.Crouch;
                else if (stance < 0.15f) btn |= Btn.Prone;
                for (var i = 0; i < len && t < ticks; i++, t++)
                {
                    var c = btn;
                    if (rng.Float() < 0.06f) c |= Btn.Jump;
                    list[t] = new Input { Buttons = c, Yaw = yaw + (i - len / 2) * 0.01f }; // slow turn while moving
                }
            }
            all[b] = list;
        }
        return all;
    }

    public sealed class RunResult
    {
        public string Hash = "";
        public List<double> TickUs = new();
        public ulong[] Bot0Hash = []; // char 0's state after each tick
        public byte[][] Bot0Full = []; // char 0's full Jolt state before each tick
        public NetState[] Bot0Net = []; // char 0's snapshot state before each tick
        public Mover? Bot0;
        public string Final = "";
    }

    /// <summary>Simulates the bots for all ticks, hashing every state of every bot every tick.</summary>
    public static RunResult RunBots(TestWorld w, Input[][] inputs, bool record)
    {
        var r = new RunResult();
        var movers = new Mover[inputs.Length];
        for (var i = 0; i < movers.Length; i++)
            movers[i] = new Mover(w.P, Spawns[i % Spawns.Length]);
        var h = new Hasher();
        var ticks = inputs[0].Length;
        var rec = StateRecorderImpl.Create();
        if (record)
        {
            r.Bot0Hash = new ulong[ticks];
            r.Bot0Full = new byte[ticks][];
            r.Bot0Net = new NetState[ticks];
        }
        for (var t = 0; t < ticks; t++)
        {
            if (record)
            {
                r.Bot0Full[t] = movers[0].SaveFull(rec);
                r.Bot0Net[t] = movers[0].Net;
            }
            var t0 = Clock.Now;
            w.SetMovers(t);
            for (var i = 0; i < movers.Length; i++)
                movers[i].Step(inputs[i][t]);
            r.TickUs.Add(Clock.UsSince(t0));
            for (var i = 0; i < movers.Length; i++)
                movers[i].HashInto(ref h);
            if (record)
            {
                var h0 = new Hasher();
                movers[0].HashInto(ref h0);
                r.Bot0Hash[t] = h0.Value;
            }
        }
        r.Hash = h.ToString();
        var p = movers[0].Ch.GetPosition();
        r.Final = $"bot0 at ({F(p.X)}, {F(p.Y)}, {F(p.Z)}) {movers[0].Stance}";
        r.Bot0 = movers[0];
        rec.Dispose();
        return r;
    }

    /// <summary>Reconciliation: rewind bot 0 K ticks, replay its inputs, compare every replayed tick bit for bit.</summary>
    static (int FirstBad, float MaxErr, double Us) Replay(TestWorld w, RunResult r, Input[] inputs, int k, bool full)
    {
        var m = r.Bot0!;
        var ticks = inputs.Length;
        var s = ticks - k;
        var rec = StateRecorderImpl.Create();
        var t0 = Clock.Now;
        if (full)
            m.RestoreFull(rec, r.Bot0Full[s]);
        else
            m.RestoreNet(r.Bot0Net[s]);
        var firstBad = -1;
        var maxErr = 0f;
        for (var t = s; t < ticks; t++)
        {
            w.SetMovers(t);
            m.Step(inputs[t]);
            var h = new Hasher();
            m.HashInto(ref h);
            if (h.Value != r.Bot0Hash[t] && firstBad < 0)
                firstBad = t - s;
            if (t + 1 < ticks)
                maxErr = MathF.Max(maxErr, (m.Ch.GetPosition() - r.Bot0Net[t + 1].Position).Length());
        }
        var us = Clock.UsSince(t0);
        rec.Dispose();
        return (firstBad, maxErr, us);
    }

    public static void Determinism()
    {
        var inputs = MakeInputs(Bots, Ticks);
        var w = new TestWorld();

        var a = RunBots(w, inputs, record: true);
        var b = RunBots(w, inputs, record: false);
        Check("same process, same world: two runs identical", a.Hash == b.Hash, $"{a.Hash} vs {b.Hash}; {a.Final}");
        Result("c.bots8x600", a.Hash);
        Result("c.final", a.Final.Replace(' ', '_'));

        // Reconciliation from the full Jolt character state and from snapshot-only state (pos, vel, stance).
        foreach (var k in new[] { 8, 60, 240 })
        {
            var (badFull, errFull, _) = Replay(w, a, inputs[0], k, full: true);
            Check($"rewind {k} ticks from full Jolt state, replay", badFull < 0, badFull < 0 ? "bit-identical" : $"diverged after {badFull} ticks, max err {F(errFull * 1000, 3)} mm");
            var (badNet, errNet, _) = Replay(w, a, inputs[0], k, full: false);
            // Snapshot-only restore is the game's model; bit-exactness is the goal, sub-mm is acceptable.
            Check($"rewind {k} ticks from snapshot (pos/vel/stance), replay", badNet < 0 || errNet < 0.001f,
                badNet < 0 ? "bit-identical" : $"diverged after {badNet} ticks, max err {F(errNet * 1000, 3)} mm");
            Result($"c.recon{k}.full", badFull < 0 ? "exact" : $"diverged@{badFull}");
            Result($"c.recon{k}.net", badNet < 0 ? "exact" : $"diverged@{badNet}_err{F(errNet * 1000, 3)}mm");
        }

        // A second, separately built world (like client and server each loading the map) and one built in a
        // different order (different body IDs) must give the same answer.
        var w2 = new TestWorld();
        var c = RunBots(w2, inputs, record: false);
        Check("separately built world: identical", c.Hash == a.Hash, $"{c.Hash}");
        var w3 = new TestWorld(shuffled: true);
        var d = RunBots(w3, inputs, record: false);
        Check("world built in shuffled order: identical", d.Hash == a.Hash, d.Hash == a.Hash ? d.Hash : $"{d.Hash} (body add order matters: {d.Final})");
        Result("c.shuffled", d.Hash);

        // ---- d. performance of the movement side ----
        var warm = a.TickUs.Skip(30).ToList();
        Metric("8 chars ExtendedUpdate per tick us (mean/p50/p99/max)",
            $"{F(warm.Average(), 1)}/{F(Clock.Percentile(warm, 0.5), 1)}/{F(Clock.Percentile(warm, 0.99), 1)}/{F(warm.Max(), 1)}");
        Metric("per character per tick us (mean)", F(warm.Average() / Bots, 1));
        var reps = new List<double>();
        for (var i = 0; i < 10; i++)
            reps.Add(Replay(w, a, inputs[0], 240, full: false).Us);
        Metric("client reconcile: restore + replay 240 ticks, 1 char, us (median/max)", $"{F(Clock.Percentile(reps, 0.5), 0)}/{F(reps.Max(), 0)}");
        Metric("  = us per replayed tick", F(Clock.Percentile(reps, 0.5) / 240, 2));
        var replay8 = RunBots(w, inputs.Select(x => x.Take(240).ToArray()).ToArray(), record: false);
        Metric("8 chars x 240 ticks (server-side worst case) ms", F(replay8.TickUs.Sum() / 1000, 2));
        var tc = Clock.Now;
        for (var i = 0; i < 100; i++)
            _ = new Mover(w.P, new float3(-30, 0.05f, -40)).Ch;
        Metric("CharacterVirtual create us", F(Clock.UsSince(tc) / 100, 1));
    }
}
