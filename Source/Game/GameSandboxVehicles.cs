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

    /// <summary>
    /// Host setting: requests by role, by model and by convoy are served (docs/vehiclekit/VEHICLEKIT.md, "Families and convoys"). Off: only requests by shape are, as before
    /// kit 4 (the parts menu's tiles then spawn by shape whatever a player picked).
    /// </summary>
    public bool RolesAndConvoys = true;

    private const string ShapeOnly = "the host spawns faction vehicles by shape only";

    /// <summary>The most convoy members waiting at once (their own cap, beside <see cref="QueueMax"/>), and convoys at once on the server (and so from one player).</summary>
    public const int ConvoyQueueMax = VehicleConvoys.MaxCount, ConvoysAtOnce = 1;

    /// <summary>What a finished request came to: the construct (null: none, with the reason), the blueprint's name.</summary>
    public delegate void Finished(Construct? built, string name, string why, byte owner);

    private struct Ask
    {
        public IVehicleGenerator Gen;
        public int Seed, Tries;
        public VehicleLayers Layers; // kit 3's per-layer seeds (none: the seed's own); kept through rerolls of the seed
        public RoleAsk? Role; // kit 4's role and tier (null: asked for by shape); kept through rerolls, with the shape it picked
        public bool Picked; // the server chose the seed
        public Vector3 Ground;
        public float Yaw;
        public byte Owner, Front; // 255: nobody's; Front: set it down in front of this player when ready (255: at Ground)
        public Finished? Done;
        public ConstructBuild.Blueprint? Bp; // a blueprint to set down as it is (the parts menu's): nothing generated
        public VehicleModel? Model; // a family's model (VehicleFamilies.cs): its role ask is the model's; a reroll is the model on another seed
        public ConvoyRun? Convoy; // a convoy's member: where it stands is worked out when it is ready, behind the one before
        public int Lane, Index; // its lane (0 the middle) and place in the convoy (0 the leader)
        public SpawnContext? Context; // where it is made for (phase 7: VehicleContext.cs; null or neutral: as before); kept through rerolls
        public EngineKit.EngineRequest? Engine; // an engine on its own (GameSandboxEngines.cs): made by the Engine Kit, never rerolled or stood in for
    }

    /// <summary>A convoy being set down: its plan, where its leader stands and which way it heads, how far back each lane has reached.</summary>
    public sealed class ConvoyRun
    {
        public required ConvoyPlan Plan;
        public Vector3 Anchor, Fwd, Right;
        public float Heading;
        public byte Owner;
        public Finished? Done;
        public readonly float[] Back = new float[3]; // (each lane's last tail, m behind the leader's origin)
        public int Settled, Built;
        public readonly List<string> Names = new();
        public long Started;
        public ushort LeaderId; // the leader's construct once it is down (0 before): every member's behaviour brief names it
    }

    private readonly Queue<Ask> _queue = new(QueueMax + ConvoyQueueMax + 1);
    private Ask _now;
    private VehicleGenJob? _job;
    private ConstructBuild.GroundJob? _place; // the build being set down, for _now
    private string _placeName = "";
    private VehicleBrief? _placeBrief; // the generated vehicle's behaviour brief, put on its construct when it is down (VehicleBrief.cs)
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
    /// <paramref name="layers"/>: kit 3's per-layer seeds (docs/vehiclekit/VEHICLEKIT.md, "Layered variety"), so a caller can ask for "the same truck, different wear"
    /// (the seed and a <c>Wear</c> seed) or "the same body, another chassis"; a layer left null takes the seed. Only the shape grammar's shapes have layers.
    /// </summary>
    public string? Request(string style, string shape, int? seed, Vector3 ground, float yaw, byte owner = 255, byte frontOf = 255, Finished? done = null, VehicleLayers? layers = null, SpawnContext? context = null)
    {
        if (NoAirRefusal(context) is { } air)
            return air;
        if (VehicleSpawnKit.Find(style, shape) is not { } g)
            return $"no faction vehicle \"{style} {shape}\"";
        if (seed is < 1)
            return "a seed is 1 or more";
        if (layers is { Any: true } lay && (ShapeGrammar.RecipeOf(g) == null || VehicleLayers.All.Any(l => lay[l] is < 1)))
            return ShapeGrammar.RecipeOf(g) == null ? $"the {g.Name} has no layers" : "a layer's seed is 1 or more";
        if (Full(owner) is { } full)
            return full;
        _queue.Enqueue(new Ask
        {
            Gen = g,
            Seed = seed ?? VehicleSpawnKit.Next(ref _seedState),
            Picked = seed == null,
            Layers = layers ?? default,
            Ground = ground,
            Yaw = yaw,
            Owner = owner,
            Front = frontOf,
            Done = done,
            Context = context,
        });
        return null;
    }

    /// <summary>
    /// Asks for a faction vehicle by what it is for (docs/vehiclekit/VEHICLEKIT.md, "Roles and tiers"): <paramref name="role"/> by name ("scout", "raider", "gun platform",
    /// "carrier", "tanker", "recovery", "command", "boss"; null or empty: one the style fields, by its weights) at <paramref name="tier"/> (1 to 4, the threat ladder within the
    /// faction). The server picks the shape from the role and the seed (<see cref="VehicleRoles.ShapeFor"/>), the vehicle is made by kit 4 and measured against the role,
    /// and its role, tier and shape are in its kit line and the "generated" log line. Everything else is as <see cref="Request(string, string, int?, Vector3, float, byte, byte, Finished?, VehicleLayers?)"/>.
    /// </summary>
    public string? Request(string style, string? role, int tier, int? seed, Vector3 ground, float yaw, byte owner = 255, byte frontOf = 255, Finished? done = null, VehicleLayers? layers = null, SpawnContext? context = null)
    {
        if (!RolesAndConvoys)
            return ShapeOnly;
        if (NoAirRefusal(context) is { } air)
            return air;
        if (Styles.All.FirstOrDefault(s => s.Name.Equals(style, StringComparison.OrdinalIgnoreCase)) is not { } st)
            return $"no faction style \"{style}\"";
        if (seed is < 1)
            return "a seed is 1 or more";
        if (tier is < 1 or > VehicleRoles.MaxTier)
            return $"a tier is 1 to {VehicleRoles.MaxTier}";
        var s = seed ?? VehicleSpawnKit.Next(ref _seedState);
        var r = string.IsNullOrWhiteSpace(role) ? VehicleRoles.PickRole(st, s) : VehicleRoles.Find(role);
        if (r == null)
            return $"no role \"{role}\": {string.Join(", ", VehicleRoles.All.Select(x => x.Name))}";
        IVehicleGenerator g;
        try
        {
            g = VehicleRoles.ShapeFor(st, r, s);
        }
        catch (VehicleKitException ex)
        {
            return ex.Message;
        }
        if (layers is { Any: true } lay && (ShapeGrammar.RecipeOf(g) == null || VehicleLayers.All.Any(l => lay[l] is < 1)))
            return ShapeGrammar.RecipeOf(g) == null ? $"the {g.Name} has no layers" : "a layer's seed is 1 or more";
        if (Full(owner) is { } full)
            return full;
        _queue.Enqueue(new Ask
        {
            Gen = g,
            Seed = s,
            Picked = seed == null,
            Layers = layers ?? default,
            Role = new RoleAsk(r, tier, st) { Shape = VehicleSpawnKit.Names(g).Shape },
            Ground = ground,
            Yaw = yaw,
            Owner = owner,
            Front = frontOf,
            Done = done,
            Context = context,
        });
        return null;
    }

    /// <summary>
    /// Asks for a family's model (docs/vehiclekit/VEHICLEKIT.md, "Families and convoys"): the same <paramref name="model"/> always makes the same vehicle, so its key
    /// (<see cref="VehicleModel.Key"/>) is all a world or a log needs to spawn it again. Everything else is as the request by role.
    /// </summary>
    public string? RequestModel(VehicleModel model, Vector3 ground, float yaw, byte owner = 255, byte frontOf = 255, Finished? done = null, SpawnContext? context = null)
    {
        if (!RolesAndConvoys)
            return ShapeOnly;
        if (NoAirRefusal(context) is { } air)
            return air;
        if (model.Seed < 1)
            return "a seed is 1 or more";
        if (ModelAsk(model, out var ask) is { } no)
            return no;
        if (Full(owner) is { } full)
            return full;
        ask.Ground = ground;
        ask.Yaw = yaw;
        ask.Owner = owner;
        ask.Front = frontOf;
        ask.Done = done;
        ask.Context = context;
        _queue.Enqueue(ask);
        return null;
    }

    /// <summary>A model's ask: its generator and role ask (<see cref="VehicleFamilies.Resolve"/>), or why not.</summary>
    private static string? ModelAsk(VehicleModel model, out Ask ask)
    {
        ask = default;
        try
        {
            var (_, _, g, role, layers) = VehicleFamilies.Resolve(model);
            ask = new Ask { Gen = g, Seed = model.Seed, Layers = layers, Role = role, Model = model };
            return null;
        }
        catch (VehicleKitException ex)
        {
            return ex.Message;
        }
    }

    /// <summary>
    /// Asks for a convoy (docs/vehiclekit/VEHICLEKIT.md, "Families and convoys"): <paramref name="count"/> (2 to 6) of <paramref name="style"/>'s vehicles planned from
    /// <paramref name="seed"/> (null: the server picks) at <paramref name="tier"/> (<see cref="VehicleConvoys.Plan"/>: a leader a tier up, escorts and cargo, one paint
    /// scheme, one or two model families), <paramref name="mix"/> the roles behind the leader when given. The leader stands at <paramref name="at"/> heading
    /// <paramref name="heading"/>, the rest in a column or a wedge behind it, each placed when it is ready, spaced by the lengths of the ones before. Members queue
    /// as any request does but have their own cap (<see cref="ConvoyQueueMax"/>, one convoy at a time on the server); <paramref name="done"/> hears each member.
    /// One line is logged when the convoy is down. Returns why it was refused, or null: queued.
    /// </summary>
    public string? RequestConvoy(string style, int count, int tier, int? seed, Vector3 at, float heading, byte owner = 255, Finished? done = null, IReadOnlyList<string>? mix = null, SpawnContext? context = null)
    {
        if (!RolesAndConvoys)
            return ShapeOnly;
        if (NoAirRefusal(context) is { } air)
            return air;
        if (Styles.All.FirstOrDefault(s => s.Name.Equals(style, StringComparison.OrdinalIgnoreCase)) is not { } st)
            return $"no faction style \"{style}\"";
        if (seed is < 1)
            return "a seed is 1 or more";
        if (tier is < 1 or > VehicleRoles.MaxTier)
            return $"a tier is 1 to {VehicleRoles.MaxTier}";
        if (ConvoysPending() >= ConvoysAtOnce)
            return "a convoy is on its way already: wait for it";
        ConvoyPlan plan;
        try
        {
            plan = VehicleConvoys.Plan(st, count, tier, seed ?? VehicleSpawnKit.Next(ref _seedState), mix);
        }
        catch (VehicleKitException ex)
        {
            return ex.Message;
        }
        var asks = new List<Ask>(plan.Members.Count);
        foreach (var m in plan.Members)
        {
            if (ModelAsk(m.Model, out var ask) is { } no)
                return no;
            asks.Add(ask);
        }
        var up = GravityFrames.Curved ? GravityFrames.UpAt(at) : Vector3.UnitY;
        var fwd = GravityFrames.FlatForward(at, heading);
        var run = new ConvoyRun
        {
            Plan = plan,
            Anchor = at,
            Fwd = fwd,
            Right = Vector3.Normalize(Vector3.Cross(fwd, up)),
            Heading = heading,
            Owner = owner,
            Done = done,
            Started = Stopwatch.GetTimestamp(),
        };
        for (var k = 0; k < asks.Count; k++)
        {
            var a = asks[k];
            (a.Ground, a.Yaw, a.Owner, a.Front, a.Done, a.Convoy, a.Lane, a.Index, a.Context) = (at, heading, owner, 255, null, run, plan.Members[k].Lane, k, context); // (one context for the convoy: its anchor's)
            _queue.Enqueue(a);
        }
        return null;
    }

    /// <summary>Convoys waiting or being set down.</summary>
    private int ConvoysPending()
    {
        ConvoyRun? last = (_job != null || _place != null) ? _now.Convoy : null;
        var n = last != null ? 1 : 0;
        foreach (var a in _queue)
            if (a.Convoy != null && a.Convoy != last)
            {
                last = a.Convoy;
                n++;
            }
        return n;
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

    /// <summary>
    /// Asks for an engine on its own (the creative "Make an engine": GameSandboxEngines.cs), made by the Engine Kit in the generation's slices and set down
    /// as a vehicle is. A failed request is not rerolled: <paramref name="done"/> hears why. <see cref="EngineMade"/> holds the result while it is set down.
    /// </summary>
    public string? RequestEngine(EngineKit.EngineRequest r, Vector3 ground, float yaw, byte owner, byte frontOf, Finished done)
    {
        if (Full(owner) is { } full)
            return full;
        _queue.Enqueue(new Ask { Engine = r, Seed = r.Seed, Ground = ground, Yaw = yaw, Owner = owner, Front = frontOf, Done = done });
        return null;
    }

    /// <summary>The last engine made for <see cref="RequestEngine"/> (its numbers and note), kept until the next one: its finish callback reads it.</summary>
    public EngineKit.EngineResult? EngineMade;

    /// <summary>
    /// Off world (no air) a vehicle that burns fuel can't run: the kit gives every engine slot the Electric Motor there (VehicleContext.cs). With the host's committed
    /// seeds only, nothing can be made for it: refused with the reason, as a fallback to a committed file would be.
    /// </summary>
    private string? NoAirRefusal(SpawnContext? context) =>
        context is { NoAir: true } && Seeds == VehicleSeeds.Committed ? "no air here: the committed vehicles burn fuel, and the host makes no others" : null;

    private string? Full(byte owner)
    {
        var waiting = 0;
        foreach (var a in _queue)
            if (a.Convoy == null)
                waiting++; // (a convoy's members have their own cap: ConvoyQueueMax)
        if (waiting >= QueueMax)
            return "too many vehicles are being made: wait for them";
        if (owner == 255)
            return null;
        var mine = (_job != null || _place != null) && _now.Owner == owner && _now.Convoy == null ? 1 : 0;
        foreach (var a in _queue)
            if (a.Owner == owner && a.Convoy == null)
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
            Console.WriteLine($"[vehicles] generated {job.Name} (hash {job.Hash:x16}, kit {job.Kit}{(job.RoleNotes.Length + job.Modules.Length > 0 ? ": " + string.Join(", ", new[] { job.RoleNotes, job.Modules }.Where(x => x.Length > 0)) : "")}{(job.ContextNotes.Length > 0 ? $"; context {job.ContextNotes}{(_now.Context?.Read is { Length: > 0 } read ? $" (read: {read})" : "")}" : "")}) in {job.CpuMs:0} ms over {job.Slices} ticks, longest slice {job.WorstSliceMs:0.0} ms, longest span {job.WorstSpanMs:0.0} ms ({job.WorstSpanAt}){(job.Brief is { } brief ? "; brief: " + brief.Summary : "; no brief")}");
            _placeBrief = job.Brief;
            if (job.EngineMade != null)
                EngineMade = job.EngineMade;
            Place(_now.Convoy != null ? Slot(_now, job.Front, job.Rear) : _now, bp, job.Tow, job.Name);
            return;
        }
        Failed++;
        if (++_failures <= 10 || _failures % 100 == 0)
            Console.WriteLine($"[vehicles] {job.Name} failed ({_failures} so far): {job.Error}");
        if (_now.Engine != null)
        {
            Finish(_now, null, job.Name, job.Error); // (an engine is never rerolled: the same request makes the same refusal)
            return;
        }
        var next = _now;
        if (++next.Tries <= Rerolls && !job.Error.StartsWith("no yield"))
        {
            next.Seed = VehicleSpawnKit.Reroll(next.Seed, next.Tries);
            if (next.Model is { } model && ModelAsk(model with { Seed = next.Seed }, out var again) is null)
                (next.Gen, next.Role, next.Layers, next.Model) = (again.Gen, again.Role, again.Layers, again.Model); // (the model on another seed: its shape may change with it)
            Start(next);
            return;
        }
        FallBack(next, next.Tries > Rerolls ? $"{job.Name}, the last of {Rerolls} rerolls, failed" : $"{job.Name} failed");
    }

    private void Start(Ask a)
    {
        _now = a;
        _placeBrief = null;
        if (a.Bp is { } bp)
        {
            Place(a, bp, null, bp.Name);
            return;
        }
        if (a.Engine is { } engine)
        {
            _job = new VehicleGenJob(engine); // (engines have no committed files: always made)
            return;
        }
        if (Seeds == VehicleSeeds.Committed)
        {
            FallBack(a, "");
            return;
        }
        _job = new VehicleGenJob(a.Gen, a.Seed, a.Layers, a.Role, a.Context);
    }

    private void FallBack(Ask a, string why)
    {
        _placeBrief = null; // (a committed seed stands in: no brief; VehicleBriefMake.Rebuild makes one from the construct when wanted)
        if (a.Context is { NoAir: true })
        {
            Console.WriteLine($"[vehicles] {a.Gen.Name}: no air here and nothing electric could be made{(why.Length > 0 ? $" ({why})" : "")}: refused");
            Finish(a, null, a.Gen.Name, $"no air here: no {a.Gen.Name} could be made to run without it");
            return;
        }
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
        if (a.Convoy != null)
        {
            var (front, rear) = VehicleConvoys.Estimate(VehicleSpawnKit.Names(a.Gen).Shape); // (a committed file's length isn't known before it is built: the shape's usual)
            a = Slot(a, front, rear);
        }
        Place(a, bp, null, bp.Name);
    }

    /// <summary>
    /// A convoy member's place, now that its length is known: the leader at the anchor, each one after it <see cref="VehicleConvoys.Gap"/> behind the last tail in its
    /// lane (a wedge's side lanes start at the leader's tail, <see cref="VehicleConvoys.Lane"/> out), on the ground there, heading the convoy's way.
    /// </summary>
    private Ask Slot(Ask a, float front, float rear)
    {
        var run = a.Convoy!;
        float back;
        if (a.Index == 0)
        {
            back = 0;
            run.Back[0] = run.Back[1] = run.Back[2] = rear;
        }
        else
        {
            var lane = Math.Clamp(a.Lane + 1, 0, 2);
            back = run.Back[lane] + VehicleConvoys.Gap + front;
            run.Back[lane] = back + rear;
        }
        a.Ground = game.SandboxGroundAt(run.Anchor - run.Fwd * back + run.Right * (a.Lane * VehicleConvoys.Lane));
        a.Yaw = run.Heading;
        return a;
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
        Brief(a, c, name);
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
        Brief(_now, c, _placeName);
        LastJoinMs = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
        LastPlaceCpuMs = job.CpuMs;
        LastPlaceWorstMs = job.WorstSliceMs;
        LastPlaceSlices = job.Slices;
        Console.WriteLine($"[vehicles] set down {_placeName}{(c == null ? $" (refused: {why})" : $" as construct {c.Id}")}: steps {job.CpuMs:0.0} ms over {job.Slices} ticks, longest slice {job.WorstSliceMs:0.0} ms, joined in {LastJoinMs:0.0} ms");
        Finish(_now, c, _placeName, why);
    }

    /// <summary>
    /// The generated vehicle's behaviour brief goes on its construct (VehicleBrief.cs): its part indices take the built body's tags, with its convoy place and model,
    /// and a convoy's leader is remembered for the members after it. A brief that doesn't fit the body is left off (logged). A few hundred field writes, no allocation.
    /// </summary>
    private void Brief(in Ask a, Construct? c, string name)
    {
        var brief = _placeBrief;
        _placeBrief = null;
        if (c == null)
            return;
        if (a.Convoy is { } run && a.Index == 0)
            run.LeaderId = c.Id;
        if (brief == null)
            return;
        if (a.Convoy is { } r)
            (brief.Slot, brief.Formation, brief.Convoy, brief.ConvoyIndex, brief.Lane, brief.Leader) =
                (r.Plan.Members[a.Index].Slot, r.Plan.Formation, r.Plan.Seed, (byte)a.Index, (sbyte)a.Lane, r.LeaderId);
        if (a.Model is { } m && brief.Model.Length == 0)
            brief.Model = m.Key;
        if (brief.Attach(c) is { } why)
            Console.WriteLine($"[vehicles] {name}: its behaviour brief doesn't fit the body that was built ({why}): left off");
    }

    private static void Finish(in Ask a, Construct? c, string name, string why)
    {
        a.Done?.Invoke(c, name, why, a.Owner);
        if (a.Convoy is { } run)
            ConvoyMemberDone(run, c, name, why);
    }

    /// <summary>A convoy's member is down (or isn't): the convoy hears it, and once every member has, its one log line.</summary>
    private static void ConvoyMemberDone(ConvoyRun run, Construct? c, string name, string why)
    {
        run.Settled++;
        if (c != null)
            run.Built++;
        run.Names.Add(c != null ? $"{name} as {c.Id}" : $"{name} ({why})");
        run.Done?.Invoke(c, name, why, run.Owner);
        if (run.Settled < run.Plan.Members.Count)
            return;
        var s = (Stopwatch.GetTimestamp() - run.Started) / (double)Stopwatch.Frequency;
        Console.WriteLine($"[vehicles] {run.Plan.Label}: {run.Built} of {run.Plan.Members.Count} down in {s:0.0} s: {string.Join("; ", run.Plan.Members.Select((m, i) => $"{m.Slot} {(i < run.Names.Count ? run.Names[i] : "?")} ({m.Model.Key})"))}; scheme {VehicleConvoys.SchemeLabel(run.Plan.Style, run.Plan.Scheme)}");
    }

    /// <summary>A new match or world: nothing waits, the running generation is dropped.</summary>
    public void Reset()
    {
        _job?.Abandon();
        _job = null;
        _place = null; // (its steps' world goes with it: nothing of it was in this one)
        _placeBrief = null;
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
        if (VehicleSpawn.Request(style, shape, seed > 0 ? seed : null, ground, p.Yaw, id, id, _vehicleSpawned ??= VehicleSpawned, context: SpawnContexts.At(ground)) is { } why)
            return why;
        SandboxHands.Notices.Add((id, VehicleSpawn.Pending > 1 ? $"making a {style} {shape} ({VehicleSpawn.Pending - 1} ahead of it)" : $"making a {style} {shape}"));
        return null;
    }

    private VehicleSpawner.Finished? _vehicleSpawned;

    /// <summary>The ground under a point (the vehicle spawner's convoy places).</summary>
    internal Vector3 SandboxGroundAt(Vector3 p) => ConstructWorld.Ground(World.Query, p);

    /// <summary>
    /// Client (or the host): ask for a faction vehicle by role and tier (<paramref name="convoy"/> false; <paramref name="role"/> empty: one the style fields), or a
    /// convoy of <paramref name="count"/> in front of you (docs/vehiclekit/VEHICLEKIT.md, "Families and convoys"). <paramref name="seed"/> 0: the server picks.
    /// </summary>
    public void SandboxSpawnFaction(bool convoy, string style, string role, int tier, int count, int seed)
    {
        var w = SbW;
        w.Begin(Msg.CAct).U8((byte)CActKind.FactionVehicle).U8((byte)(convoy ? 1 : 0)).Str(style).Str(role).U8((byte)Math.Clamp(tier, 1, VehicleRoles.MaxTier))
            .U8((byte)Math.Clamp(count, 0, VehicleConvoys.MaxCount)).U32((uint)Math.Max(0, seed));
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

    /// <summary>
    /// Server: CActKind.FactionVehicle (after the kind byte): a faction vehicle by role and tier in front of the player, or a convoy set down across their view: its
    /// leader 22 m ahead and to one side, heading across, the rest in a line behind it, so the whole convoy is seen side on.
    /// </summary>
    private string? SandboxFactionVehicleAct(Player p, ref PacketReader r)
    {
        var convoy = r.U8() == 1;
        var style = r.Text(64);
        var role = r.Text(64);
        var tier = (int)r.U8();
        var count = (int)r.U8();
        var seed = (int)Math.Min(r.U32(), (uint)int.MaxValue);
        if (SandboxMenuSpawnRefusal(p) is { } no)
            return no;
        var id = p.Id;
        var fwd = GravityFrames.FlatForward(p.Position, p.Yaw);
        if (!convoy)
        {
            var ground = ConstructWorld.Ground(World.Query, p.Position + fwd * 6f);
            if (VehicleSpawn.Request(style, role.Length > 0 ? role : null, tier, seed > 0 ? seed : null, ground, p.Yaw, id, id, _vehicleSpawned ??= VehicleSpawned, context: SpawnContexts.At(ground)) is { } why)
                return why;
            SandboxHands.Notices.Add((id, $"making a {style} {(role.Length > 0 ? role : "vehicle")}, tier {tier}{(VehicleSpawn.Pending > 1 ? $" ({VehicleSpawn.Pending - 1} ahead of it)" : "")}"));
            return null;
        }
        var heading = p.Yaw + MathF.PI * 0.5f;
        var along = GravityFrames.FlatForward(p.Position, heading);
        var n = Math.Clamp(count == 0 ? 5 : count, VehicleConvoys.MinCount, VehicleConvoys.MaxCount);
        var at = p.Position + fwd * 22f + along * (n * 6f); // (about half the column's length ahead along the way it heads: the convoy passes in front)
        var anchor = ConstructWorld.Ground(World.Query, at);
        if (VehicleSpawn.RequestConvoy(style, n, tier, seed > 0 ? seed : null, anchor, heading, id, _vehicleSpawned ??= VehicleSpawned, context: SpawnContexts.At(anchor)) is { } busy)
            return busy;
        SandboxHands.Notices.Add((id, $"making a {style} convoy of {n}, tier {tier}"));
        SandboxConvoyAir(style, anchor, heading, seed); // (now and then an air escort over it: GameSandboxAircraft.cs)
        return null;
    }

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
