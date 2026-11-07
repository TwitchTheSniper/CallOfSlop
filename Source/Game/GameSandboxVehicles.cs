using System.Diagnostics;
using System.Numerics;
using Brutal.RakNetApi;
using CallOfSlop.VehicleKit;

namespace CallOfSlop;

/// <summary>Where the server's faction vehicles come from (a host setting): generated at spawn time, or the committed seeds only.</summary>
public enum VehicleSeeds : byte
{
    Generated, // a request's seed (or a fresh one) is generated on the server; a seed that fails rerolls, then falls back to a committed one
    Committed, // every request is given a committed seed's blueprint of its style and shape (what CI and the tests know)
}

/// <summary>
/// Dynamic spawn of faction vehicles (docs/vehiclekit/VEHICLEKIT.md, "Dynamic spawn"): the server runs the vehicle kit's
/// generator for (style, shape, seed) while the game plays, and builds the result like any blueprint. Requests queue
/// (<see cref="Request"/>), one generation runs at a time in slices of <see cref="SliceMs"/> a tick on its own thread while the
/// tick waits (<see cref="VehicleGenJob"/>: never at the same time as the game), and the vehicle is built on the tick it is
/// ready, a slice a tick as well (<see cref="ConstructBuild.GroundJob"/>: nothing half built is simulated, saved or sent), and
/// joins the world whole on a tick of its own. The parts menu's big blueprints queue here too, to be set down the same way.
/// Clients never generate: a built vehicle is an ordinary construct, sent as bodies, saved with the world and
/// salvaged part by part. A seed that throws or doesn't load rerolls <see cref="Rerolls"/> times (seeds derived from it), then
/// a committed seed of that style and shape stands in. Server only; the tick allocates nothing while nothing is asked for.
/// </summary>
public sealed class VehicleSpawner(Game game)
{
    /// <summary>A tick's share of a generation (ms): with the rest of a busy tick, under the 8 ms budget.</summary>
    public const double SliceMs = 2.5;

    /// <summary>The most requests waiting at once, and from one player.</summary>
    public const int QueueMax = 8, PerPlayer = 2;

    /// <summary>How many new seeds a failing seed gets before a committed one stands in.</summary>
    public const int Rerolls = 3;

    /// <summary>
    /// A blueprint of at most this many steps (with its tow) is set down at once, as it always was: under about 2 ms (every
    /// hand-made one but the RV). Past it, a slice a tick (<see cref="ConstructBuild.GroundJob"/>): a Double Decker's 220 steps
    /// cost 7 to 15 ms in all on this box.
    /// </summary>
    public const int ImmediateSteps = 48;

    /// <summary>Is this blueprint (and what it tows) set down at once?</summary>
    public static bool AtOnce(ConstructBuild.Blueprint bp, ConstructBuild.Blueprint? tow)
    {
        tow ??= bp.Tow.Length > 0 ? ConstructBuild.FindBlueprint(bp.Tow) : null;
        return bp.Steps.Count + (tow?.Steps.Count ?? 0) <= ImmediateSteps;
    }

    /// <summary>Host setting: generated or committed seeds.</summary>
    public VehicleSeeds Seeds = VehicleSeeds.Generated;

    /// <summary>What a finished request came to: the construct (null: none, with the reason), the blueprint's name.</summary>
    public delegate void Finished(Construct? built, string name, string why, byte owner);

    private struct Ask
    {
        public IVehicleGenerator Gen;
        public int Seed, Tries;
        public bool Picked; // the server chose the seed
        public Vector3 Ground;
        public float Yaw;
        public byte Owner, Front; // 255: nobody's; Front: set it down in front of this player when ready (255: at Ground)
        public Finished? Done;
        public ConstructBuild.Blueprint? Bp; // a blueprint to set down as it is (the parts menu's): nothing generated
    }

    private readonly Queue<Ask> _queue = new(QueueMax + 1);
    private Ask _now;
    private VehicleGenJob? _job;
    private ConstructBuild.GroundJob? _place; // the build being set down, for _now
    private string _placeName = "";
    private ulong _seedState = (ulong)Environment.TickCount64 * 0x2545F4914F6CDD1DUL;
    private int _failures;

    /// <summary>Generations finished, failed seeds, fallbacks to a committed seed (the log and the self-checks read these).</summary>
    public int Generated, Failed, FellBack;

    /// <summary>The last generation's cost: total ms on the ticks, the longest slice, how many ticks.</summary>
    public double LastCpuMs, LastWorstSliceMs;
    public int LastSlices;

    /// <summary>The last sliced set-down's cost: its steps' ms in all, the longest slice, how many, and the tick it joined the world on.</summary>
    public double LastPlaceCpuMs, LastPlaceWorstMs, LastJoinMs;
    public int LastPlaceSlices;

    /// <summary>Waiting or running.</summary>
    public int Pending => _queue.Count + (_job != null || _place != null ? 1 : 0);

    /// <summary>
    /// Asks for a faction vehicle: <paramref name="style"/> and <paramref name="shape"/> by name ("Bandit", "Box Truck"),
    /// <paramref name="seed"/> null for one the server picks. It stands on the ground at <paramref name="ground"/> facing
    /// <paramref name="yaw"/>, or in front of player <paramref name="frontOf"/> as they are when it is ready.
    /// <paramref name="done"/> hears how it went, on the tick. Returns why it was refused, or null: queued.
    /// </summary>
    public string? Request(string style, string shape, int? seed, Vector3 ground, float yaw, byte owner = 255, byte frontOf = 255, Finished? done = null)
    {
        if (VehicleSpawnKit.Find(style, shape) is not { } g)
            return $"no faction vehicle \"{style} {shape}\"";
        if (seed is < 1)
            return "a seed is 1 or more";
        if (Full(owner) is { } full)
            return full;
        _queue.Enqueue(new Ask
        {
            Gen = g,
            Seed = seed ?? VehicleSpawnKit.Next(ref _seedState),
            Picked = seed == null,
            Ground = ground,
            Yaw = yaw,
            Owner = owner,
            Front = frontOf,
            Done = done,
        });
        return null;
    }

    /// <summary>
    /// Asks for a blueprint to be set down as it is (the parts menu's, too big for one tick: <see cref="AtOnce"/>), in the same
    /// queue and the same way as a made vehicle, where <see cref="Request"/> says. Returns why it was refused, or null: queued.
    /// </summary>
    public string? RequestBlueprint(ConstructBuild.Blueprint bp, Vector3 ground, float yaw, byte owner = 255, byte frontOf = 255, Finished? done = null)
    {
        if (Full(owner) is { } full)
            return full;
        _queue.Enqueue(new Ask { Bp = bp, Ground = ground, Yaw = yaw, Owner = owner, Front = frontOf, Done = done });
        return null;
    }

    private string? Full(byte owner)
    {
        if (_queue.Count >= QueueMax)
            return "too many vehicles are being made: wait for them";
        if (owner == 255)
            return null;
        var mine = (_job != null || _place != null) && _now.Owner == owner ? 1 : 0;
        foreach (var a in _queue)
            if (a.Owner == owner)
                mine++;
        return mine >= PerPlayer ? "you have vehicles on the way already" : null;
    }

    /// <summary>Server, every Sandbox tick: the running generation's slice, or the next request started.</summary>
    public void Tick()
    {
        if (_place != null)
        {
            StepPlace();
            return;
        }
        if (_job == null)
        {
            if (_queue.Count == 0)
                return;
            Start(_queue.Dequeue());
            if (_job == null)
                return; // (committed, or a blueprint: being set down, or set down at once)
        }
        if (!_job.Step(SliceMs))
            return;
        var job = _job;
        _job = null;
        LastCpuMs = job.CpuMs;
        LastWorstSliceMs = job.WorstSliceMs;
        LastSlices = job.Slices;
        if (job.Blueprint is { } bp)
        {
            Generated++;
            Console.WriteLine($"[vehicles] generated {job.Name} (hash {job.Hash:x16}, kit {job.Kit}{(job.Modules.Length > 0 ? ": " + job.Modules : "")}) in {job.CpuMs:0} ms over {job.Slices} ticks, longest slice {job.WorstSliceMs:0.0} ms, longest span {job.WorstSpanMs:0.0} ms ({job.WorstSpanAt})");
            Place(_now, bp, job.Tow, job.Name);
            return;
        }
        Failed++;
        if (++_failures <= 10 || _failures % 100 == 0)
            Console.WriteLine($"[vehicles] {job.Name} failed ({_failures} so far): {job.Error}");
        var next = _now;
        if (++next.Tries <= Rerolls && !job.Error.StartsWith("no yield"))
        {
            next.Seed = VehicleSpawnKit.Reroll(next.Seed, next.Tries);
            Start(next);
            return;
        }
        FallBack(next, next.Tries > Rerolls ? $"{job.Name}, the last of {Rerolls} rerolls, failed" : $"{job.Name} failed");
    }

    private void Start(Ask a)
    {
        _now = a;
        if (a.Bp is { } bp)
        {
            Place(a, bp, null, bp.Name);
            return;
        }
        if (Seeds == VehicleSeeds.Committed)
        {
            FallBack(a, "");
            return;
        }
        _job = new VehicleGenJob(a.Gen, a.Seed);
    }

    private void FallBack(Ask a, string why)
    {
        var bp = VehicleSpawnKit.Committed(a.Gen, a.Seed);
        if (bp == null)
        {
            Console.WriteLine($"[vehicles] {a.Gen.Name}: no committed seed to fall back to{(why.Length > 0 ? $" ({why})" : "")}");
            Finish(a, null, a.Gen.Name, $"no {a.Gen.Name} could be made");
            return;
        }
        if (why.Length > 0)
        {
            FellBack++;
            Console.WriteLine($"[vehicles] {why}: the committed {bp.Name} stands in");
        }
        Place(a, bp, null, bp.Name);
    }

    /// <summary>Sets a blueprint down: at once when it is small (<see cref="AtOnce"/>), else its steps start, a slice a tick from the next.</summary>
    private void Place(in Ask a, ConstructBuild.Blueprint bp, ConstructBuild.Blueprint? tow, string name)
    {
        if (!AtOnce(bp, tow))
        {
            _now = a;
            _place = new ConstructBuild.GroundJob(game.Constructs, bp, tow);
            _placeName = name;
            return;
        }
        var c = game.SandboxVehicleBuild(a.Front, a.Ground, a.Yaw, bp, tow, null, out var why);
        if (c != null && a.Owner != 255)
            c.Owner = a.Owner;
        Finish(a, c, name, why);
    }

    /// <summary>The set-down's next slice; once its steps are done, on a tick of its own, it joins the world.</summary>
    private void StepPlace()
    {
        var job = _place!;
        if (!job.Ready)
        {
            job.Step(SliceMs);
            return; // (its join waits for the next tick: a slice and a join together could pass the budget)
        }
        _place = null;
        var start = Stopwatch.GetTimestamp();
        var c = game.SandboxVehicleBuild(_now.Front, _now.Ground, _now.Yaw, job.Bp, null, job, out var why);
        if (c != null && _now.Owner != 255)
            c.Owner = _now.Owner;
        LastJoinMs = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
        LastPlaceCpuMs = job.CpuMs;
        LastPlaceWorstMs = job.WorstSliceMs;
        LastPlaceSlices = job.Slices;
        Console.WriteLine($"[vehicles] set down {_placeName}{(c == null ? $" (refused: {why})" : $" as construct {c.Id}")}: steps {job.CpuMs:0.0} ms over {job.Slices} ticks, longest slice {job.WorstSliceMs:0.0} ms, joined in {LastJoinMs:0.0} ms");
        Finish(_now, c, _placeName, why);
    }

    private static void Finish(in Ask a, Construct? c, string name, string why) => a.Done?.Invoke(c, name, why, a.Owner);

    /// <summary>A new match or world: nothing waits, the running generation is dropped.</summary>
    public void Reset()
    {
        _job?.Abandon();
        _job = null;
        _place = null; // (its steps' world goes with it: nothing of it was in this one)
        _queue.Clear();
    }
}

public sealed partial class Game
{
    /// <summary>The server's faction vehicles made at spawn time (docs/vehiclekit/VEHICLEKIT.md, "Dynamic spawn").</summary>
    public VehicleSpawner VehicleSpawn => _vehicleSpawn ??= new VehicleSpawner(this);

    private VehicleSpawner? _vehicleSpawn;

    /// <summary>Client (or the host): ask for a faction vehicle by style and shape, made for a seed (0: the server picks one), in front of you.</summary>
    public void SandboxSpawnVehicle(string style, string shape, int seed)
    {
        var w = SbW;
        w.Begin(Msg.CAct).U8((byte)CActKind.Vehicle).Str(style).Str(shape).U32((uint)Math.Max(0, seed));
        if (IsServer)
        {
            if (Local is { } me)
            {
                var r = new PacketReader(w.Span[1..]);
                if (SandboxActGuarded(me, ref r) is { } why)
                    SandboxSay(why);
            }
            return;
        }
        Net.SendToServer(w.Span, PacketReliability.ReliableOrdered);
    }

    /// <summary>Server: CActKind.Vehicle (after the kind byte): the parts menu's faction vehicle, queued for the server to make.</summary>
    private string? SandboxVehicleAct(Player p, ref PacketReader r)
    {
        var style = r.Text(64);
        var shape = r.Text(64);
        var seed = (int)Math.Min(r.U32(), (uint)int.MaxValue);
        if (SandboxMenuSpawnRefusal(p) is { } no)
            return no;
        var fwd = GravityFrames.FlatForward(p.Position, p.Yaw);
        var ground = ConstructWorld.Ground(World.Query, p.Position + fwd * 6f);
        var id = p.Id;
        if (VehicleSpawn.Request(style, shape, seed > 0 ? seed : null, ground, p.Yaw, id, id, _vehicleSpawned ??= VehicleSpawned) is { } why)
            return why;
        SandboxHands.Notices.Add((id, VehicleSpawn.Pending > 1 ? $"making a {style} {shape} ({VehicleSpawn.Pending - 1} ahead of it)" : $"making a {style} {shape}"));
        return null;
    }

    private VehicleSpawner.Finished? _vehicleSpawned;

    /// <summary>A parts-menu vehicle is out (or isn't): the player who asked is told, and the story hears of it.</summary>
    private void VehicleSpawned(Construct? c, string name, string why, byte who)
    {
        if (c == null)
        {
            Console.WriteLine($"[vehicles] {name} wasn't built: {why}");
            SandboxHands.Notices.Add((who, why));
            return;
        }
        Constructs.Events.Add($"player {who} spawned {name} as construct {c.Id} (faction vehicle)");
        if (Players.TryGetValue(who, out var p))
        {
            SandboxHands.Notices.Add((who, $"{name} is ready"));
            StoryFound(p, c, name); // (GameStory.cs)
        }
    }

    /// <summary>
    /// Server: a finished faction vehicle stands on the ground: in front of player <paramref name="front"/> when they are still
    /// here, alive and on foot (as the parts menu's blueprints do), else at <paramref name="ground"/> facing <paramref name="yaw"/>.
    /// </summary>
    internal Construct? SandboxVehicleBuild(byte front, Vector3 ground, float yaw, ConstructBuild.Blueprint bp, ConstructBuild.Blueprint? tow, ConstructBuild.GroundJob? job, out string why)
    {
        if (!SandboxOn)
        {
            why = "not in Sandbox";
            return null;
        }
        if (front != 255 && Players.TryGetValue(front, out var p) && p.Alive && !SandboxHands.Hand(p.Id).Seated)
            return SandboxBuildInFront(p, bp, tow, out why, job);
        var c = job != null ? job.Join(ground, yaw, out why) : ConstructBuild.BuildOnGround(Constructs, bp, ground, yaw, out why, tow);
        if (c == null && why.Length == 0)
            why = "the world is full of constructs";
        return c;
    }
}
