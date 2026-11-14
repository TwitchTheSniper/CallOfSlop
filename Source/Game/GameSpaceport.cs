using System.Numerics;
using System.Text.Json;
using CallOfSlop.Story;

namespace CallOfSlop;

/// <summary>
/// The Ministry of Slop's spaceport (docs/story/SPACEPORT.md; Karl, 2026-11-13): a terminal, a control tower and three
/// landing pads about 2 km from the penal colony on Glacier, on ground of its own graded level, and the way off the
/// planet: the Ministry Courier, a small ship parked on the first pad, which a player may steal or be handed by its pilot,
/// Captain Fairweather, once they have run his errand. Where it stands is data (Story/Opening/Spaceport.json: which way
/// and how far from the colony, the pieces in the spaceport's own frame); the blueprints are the part kit's
/// (PartKit/Generators/BuildingParts.Spaceport.cs, the courier ShellParts.cs). Built with the colony on a fresh story
/// world, after everything round it; a world from before the spaceport gets it once, when next loaded, with its pilot. The
/// pieces are kept with the colony's buildings (Game.ColonyBuilding finds them), with a mark that it was built, so one
/// taken down stays down. Host only. Nothing runs per tick but a look at who sits down (the story's own).
/// </summary>
public sealed partial class Game
{
    public const string SpaceportMark = "(spaceport)"; // the colony buildings' list: the spaceport was built here once (no body)
    private const float SpaceportClear = 40; // m: nothing else built within this of the bench

    /// <summary>The spaceport's frame on this world, worked out from its terminal (none: no spaceport).</summary>
    private bool SpaceportFrame(out Vector3 origin, out Quaternion rot)
    {
        (origin, rot) = (default, Quaternion.Identity);
        var script = SpaceportScript.Current;
        if (script.Problem.Length > 0 || script.Pieces.Length == 0 || ColonyBuilding(script.Pieces[0].Blueprint) is not { } c)
            return false;
        var first = script.Pieces[0];
        rot = Quaternion.Normalize(c.Rot * Quaternion.Conjugate(SpaceportYaw(first.Yaw)));
        origin = c.ToWorld(new Vector3((c.LocalMin.X + c.LocalMax.X) * 0.5f, c.LocalMin.Y, (c.LocalMin.Z + c.LocalMax.Z) * 0.5f))
            - Vector3.Transform(new Vector3(first.At.X, -first.Sink, first.At.Y), rot);
        return true;
    }

    private static Quaternion SpaceportYaw(float degrees) => Quaternion.CreateFromAxisAngle(Vector3.UnitY, -degrees * MathF.PI / 180f);

    /// <summary>Where the courier's pilot keeps to (his body's home and work), or null with no spaceport.</summary>
    private Vector3? SpaceportKeeperSpot()
    {
        if (!SpaceportFrame(out var origin, out var rot))
            return null;
        var k = SpaceportScript.Current.KeeperAt;
        var at = origin + Vector3.Transform(new Vector3(k.X, 0, k.Y), rot);
        return StoryStand(at + GravityFrames.UpAt(at) * 1.5f, out var feet) ? feet : at;
    }

    /// <summary>Is this the courier's pilot (by the story's name)?</summary>
    private static bool SpaceportKeeper(string name) => name.Equals(SpaceportScript.Current.Keeper, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Server: the spaceport, built once on this world: from the colony's build on a fresh world, or the load of one from
    /// before it (<paramref name="late"/>: the pilot is added to the story then too). Looks for level ground round the
    /// file's bearing and distance, grades the bench, lays the pieces and parks the courier.
    /// </summary>
    private void SpaceportBuild(bool late)
    {
        var script = SpaceportScript.Current;
        if (script.Problem.Length > 0)
        {
            Log($"[spaceport] {script.Problem}: no spaceport");
            return;
        }
        if (_opColony == 0 || Constructs.Get(_opColony) is not { } colony || !World.Map.Name.Equals(script.Map, StringComparison.OrdinalIgnoreCase))
            return;
        foreach (var (name, _) in _cbBuilt)
            if (name == SpaceportMark)
                return;
        _cbBuilt.Add((SpaceportMark, 0)); // (tried here: one that couldn't be built, or is taken down, isn't tried again)
        if (!SpaceportSite(colony, out var origin, out var rot, out var over, out var under))
        {
            Log($"[spaceport] no level, clear ground {script.Distance:0} m from the colony: no spaceport");
            return;
        }
        var lift = SpaceportGrade(origin, rot, over);
        var n = 0;
        foreach (var p in script.Pieces)
            if (SpaceportPlace(origin, rot, lift, p) is { } c)
            {
                _cbBuilt.Add((p.Blueprint, c.Id));
                n++;
            }
        SpaceportCourier(origin, rot, lift);
        if (late && Story is { } s && StoryData.People.Cast(OpeningScript.Current.Cast) is { } cast)
            s.Agents.AddCast(cast, StoryPlace(), scripted: false); // (the pilot: the rest of the cast are there already)
        var d = Vector3.Distance(origin, colony.Pos);
        Log($"[spaceport] {n} of {script.Pieces.Length} pieces, {d:0} m from the colony at {origin.X:0},{origin.Y:0},{origin.Z:0}; the land was {over:0.0} m over and {under:0.0} m under its bench");
    }

    /// <summary>The most level, clear spot round the file's bearing and distance: the spaceport's frame there (its front, -z, to the colony).</summary>
    private bool SpaceportSite(Construct colony, out Vector3 origin, out Quaternion rot, out float over, out float under)
    {
        var script = SpaceportScript.Current;
        (origin, rot, over, under) = (default, Quaternion.Identity, 0, 0);
        var best = float.MaxValue;
        var toward = Vector2.Normalize(script.Toward);
        var from = colony.ToWorld(new Vector3(5.875f, 0, 5.875f)); // (the yard's middle)
        for (var spread = 0f; spread <= script.Spread; spread += script.Step)
            for (var side = -1; side <= 1; side += 2)
            {
                if (spread == 0 && side > 0)
                    continue;
                var a = side * spread * MathF.PI / 180f;
                var dir = new Vector2(toward.X * MathF.Cos(a) - toward.Y * MathF.Sin(a), toward.X * MathF.Sin(a) + toward.Y * MathF.Cos(a));
                foreach (var dist in (ReadOnlySpan<float>)[script.Distance, script.Distance - script.Slack, script.Distance + script.Slack])
                {
                    var flat = colony.ToWorld(new Vector3(5.875f + dir.X * dist, 0, 5.875f + dir.Y * dist));
                    var g = ConstructWorld.Ground(World.Query, flat.X, flat.Z);
                    var up = GravityFrames.UpAt(g);
                    var f = from - g;
                    f -= up * Vector3.Dot(f, up);
                    if (f.LengthSquared() < 1)
                        continue;
                    var zAxis = -Vector3.Normalize(f);
                    var xAxis = Vector3.Normalize(Vector3.Cross(up, zAxis));
                    var r = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(new Matrix4x4(
                        xAxis.X, xAxis.Y, xAxis.Z, 0, up.X, up.Y, up.Z, 0, zAxis.X, zAxis.Y, zAxis.Z, 0, 0, 0, 0, 1)));
                    // The roughest land under the bench, and a little for each degree and metre off the file's line, so a
                    // spot as level as any nearer the line wins.
                    if (!SpaceportLand(g, r, out var o, out var u))
                        continue;
                    var score = MathF.Max(o, u) + 0.02f * spread + 0.004f * MathF.Abs(dist - script.Distance);
                    if (MathF.Max(o, u) > script.Most || score >= best || !SpaceportSiteClear(g, r))
                        continue;
                    (best, origin, rot, over, under) = (score, g, r, o, u);
                }
            }
        return best < float.MaxValue;
    }

    /// <summary>How far the land stands over and under the bench's plane at <paramref name="g"/> (a 4 m grid); false off the map.</summary>
    private bool SpaceportLand(Vector3 g, Quaternion r, out float over, out float under)
    {
        var script = SpaceportScript.Current;
        (over, under) = (0, 0);
        var map = World.Map;
        for (var x = script.BenchMin.X; x <= script.BenchMax.X; x += 4)
            for (var z = script.BenchMin.Y; z <= script.BenchMax.Y; z += 4)
            {
                var at = g + Vector3.Transform(new Vector3(x, 0, z), r);
                if (MathF.Abs(at.X) > map.HalfX - 4 || MathF.Abs(at.Z) > map.HalfZ - 4)
                    return false;
                var d = Vector3.Dot(ConstructWorld.Ground(World.Query, at.X, at.Z) - at, GravityFrames.UpAt(at));
                over = MathF.Max(over, d);
                under = MathF.Max(under, -d);
            }
        return true;
    }

    /// <summary>No other body within <see cref="SpaceportClear"/> m of the bench (the data centres, the quarry, a player's base).</summary>
    private bool SpaceportSiteClear(Vector3 g, Quaternion r)
    {
        var script = SpaceportScript.Current;
        var reach = Vector2.Distance(script.BenchMin, script.BenchMax) * 0.5f + SpaceportClear;
        var mid = g + Vector3.Transform(new Vector3((script.BenchMin.X + script.BenchMax.X) * 0.5f, 0, (script.BenchMin.Y + script.BenchMax.Y) * 0.5f), r);
        foreach (var c in Constructs.Bodies)
            if (Vector3.DistanceSquared(c.WorldCom, mid) < (reach + c.Radius) * (reach + c.Radius))
                return false;
        return true;
    }

    /// <summary>The bench graded level (Flatten strokes through Game.Dig, as the colony's: GameColonySite.cs) and its ramp to the
    /// land; returns how far its top stands over the frame's origin (m). Nothing graded where the ground can't be changed.</summary>
    private float SpaceportGrade(Vector3 origin, Quaternion rot, float over)
    {
        var script = SpaceportScript.Current;
        if (DigGround == null)
            return 0;
        var lift = over + 0.05f; // (just over the highest land under it: all made ground, one dark surface, a hardstand with a ramp up to it)
        Vector3 Spot(float x, float z) => origin + Vector3.Transform(new Vector3(x, lift, z), rot);
        const float R = GroundShape.MaxRadius, Step = R * 1.5f, Run = GroundShape.MaxLength - 2f;
        var kind = GroundShape.Grade(GroundKind.Stone);
        Vector2 lo = script.BenchMin, hi = script.BenchMax;
        for (var z = lo.Y + R; z < hi.Y - R + Step; z += Step)
        {
            var zz = MathF.Min(z, hi.Y - R);
            for (var x = lo.X + R; x < hi.X - R; x += Run)
                Dig(new DigOp(kind, Spot(x, zz), R, To: Spot(MathF.Min(x + Run, hi.X - R), zz)));
        }
        foreach (var (from, to) in script.Ramps)
        {
            var b = Spot(to.X, to.Y);
            Dig(new DigOp(kind, Spot(from.X, from.Y), 3f, To: ConstructWorld.Ground(World.Query, b.X, b.Z)));
        }
        return lift;
    }

    /// <summary>One piece laid in the spaceport's plane: its footprint's middle on its spot, its underside "sink" m into the bench.</summary>
    private Construct? SpaceportPlace(Vector3 origin, Quaternion rot, float lift, SpaceportScript.Piece p)
    {
        if (ConstructBuild.FindBlueprint(p.Blueprint) is not { } bp)
            return null;
        var spot = origin + Vector3.Transform(new Vector3(p.At.X, lift - p.Sink, p.At.Y), rot);
        var c = ConstructBuild.BuildOnGround(Constructs, bp, ConstructWorld.Ground(World.Query, spot.X, spot.Z), 0, out var why);
        if (c == null)
        {
            Log($"[spaceport] the {p.Blueprint} doesn't build: {why}");
            return null;
        }
        c.Rot = Quaternion.Normalize(rot * SpaceportYaw(p.Yaw));
        if (c.Dirty)
            c.Rebuild();
        c.Pos += spot - c.ToWorld(new Vector3((c.LocalMin.X + c.LocalMax.X) * 0.5f, c.LocalMin.Y, (c.LocalMin.Z + c.LocalMax.Z) * 0.5f));
        c.PrevPos = c.NetPos = c.NetFromPos = c.Pos;
        c.PrevRot = c.Rot;
        c.NetHistReset();
        if (!c.Anchored)
            c.Frozen = true;
        c.Rebuild();
        return c;
    }

    /// <summary>The Ministry Courier stood on its pad, the Ministry's (claimed in the story), nobody's in the game.</summary>
    private void SpaceportCourier(Vector3 origin, Quaternion rot, float lift)
    {
        var script = SpaceportScript.Current;
        if (ConstructBuild.FindBlueprint(script.Courier) is not { } bp)
            return;
        var spot = origin + Vector3.Transform(new Vector3(script.CourierAt.X, lift + script.CourierUp, script.CourierAt.Y), rot);
        if (ConstructBuild.BuildOnGround(Constructs, bp, spot, 0, out var why) is not { } c)
        {
            Log($"[spaceport] the {script.Courier} doesn't build: {why}");
            return;
        }
        c.Rot = Quaternion.Normalize(rot * SpaceportYaw(script.CourierYaw));
        if (c.Dirty)
            c.Rebuild();
        c.Pos += spot + GravityFrames.UpAt(spot) * 0.05f - c.ToWorld(new Vector3(c.Com.X, ConstructBuild.LowOf(Constructs, c), c.Com.Z));
        c.PrevPos = c.NetPos = c.NetFromPos = c.Pos;
        c.PrevRot = c.Rot;
        c.NetHistReset();
        _cbBuilt.Add((script.Courier, c.Id));
        if (Story is { } s && s.Factions.Find(script.Faction) is var f and >= 0 && StoryConstruct(c.Id, script.Courier) is var t and >= 0)
            s.Factions.ClaimFor(f, t);
        Constructs.Events.Add($"spaceport: the {script.Courier} as construct {c.Id}");
    }

    // ---- The courier: stolen, or handed over ----------------------------------------------------------------------------

    /// <summary>The courier while it is still the Ministry's (the map's in the game: nobody's), or null.</summary>
    private Construct? SpaceportCourierBody(ushort body)
    {
        var name = SpaceportScript.Current.Courier;
        foreach (var (n, b) in _cbBuilt)
            if (b == body && n.Equals(name, StringComparison.OrdinalIgnoreCase))
                return Constructs.Get(b) is { Owner: 255 } c ? c : null;
        return null;
    }

    /// <summary>
    /// Server, a player sits down (from <see cref="StoryBoard"/>): in the Ministry's courier without its pilot's say-so,
    /// that is theft: the story's Commandeer (the Ministry's deed "Stole a Ministry ship", the pilot's grudge), the
    /// pilot says so if he is about, and the ship is theirs now. True when it was the courier (the Board is not recorded too).
    /// </summary>
    private bool SpaceportBoard(Player p, ushort body)
    {
        if (SpaceportCourierBody(body) is not { } c || Story is not { } s || StoryWho(p) is not (var who and >= 0))
            return false;
        var pilot = s.World.Find(SpaceportScript.Current.Keeper);
        var ship = StoryConstruct(c.Id, SpaceportScript.Current.Courier);
        if (s.Rules.VerbId("Commandeer") is var v and >= 0)
            s.Record(v, who, ship, pilot, StoryPlace());
        if (pilot >= 0 && s.Rules.Quality("CourierStolen") is { } q)
            s.World.SetQuality(pilot, q.Id, 1);
        SpaceportUnclaim(s, ship);
        c.Owner = p.Id;
        if (pilot >= 0 && s.Agents.Of(pilot) != null)
            s.Talk.SayFor(pilot, who, SpaceportScript.Current.StolenLine, s.Time, -1, -1);
        Log($"[spaceport] {p.Name} took the {SpaceportScript.Current.Courier} without the paperwork");
        return true;
    }

    /// <summary>
    /// Server, a topic answered (from <see cref="StoryTopicAct"/>): the file's handover topic, answered for real, gives the
    /// player the courier: the story's Cleared (the Ministry's deed "Ran the courier's errand"), and the ship is theirs.
    /// </summary>
    private void SpaceportTopic(StoryEngine s, Player p, int who, int person, ushort topic, TopicAnswer answer)
    {
        var script = SpaceportScript.Current;
        if (answer.Keep || script.Handover.Length == 0 || (uint)topic >= (uint)s.Rules.Topics.Topics.Length
            || s.Rules.Topics.Topics[topic].Id != script.Handover)
            return;
        Construct? c = null;
        foreach (var (n, b) in _cbBuilt)
            if (n.Equals(script.Courier, StringComparison.OrdinalIgnoreCase))
                c ??= SpaceportCourierBody(b);
        if (c == null)
            return;
        var ship = StoryConstruct(c.Id, script.Courier);
        if (s.Rules.VerbId("Cleared") is var v and >= 0)
            s.Record(v, who, ship, person, StoryPlace());
        if (s.Rules.Quality("CourierGiven") is { } q)
            s.World.SetQuality(person, q.Id, 1);
        SpaceportUnclaim(s, ship);
        c.Owner = p.Id;
        Log($"[spaceport] {p.Name} was handed the {script.Courier}");
    }

    private static void SpaceportUnclaim(StoryEngine s, int ship)
    {
        if (ship >= 0 && s.Factions.Find(SpaceportScript.Current.Faction) is var f and >= 0)
            s.Factions.Unclaim(f, ship);
    }
}

/// <summary>Story/Opening/Spaceport.json: where the Ministry's spaceport stands and what it is made of. A bad file is refused with a reason and no spaceport is built.</summary>
public sealed class SpaceportScript
{
    public const string Format = "call-of-slop-spaceport/1";
    public const int MaxPieces = 24;

    /// <summary>A piece: its blueprint, its footprint's middle in the spaceport's frame (x, z m; -z is its front, towards the colony), its turn (degrees) and how far it is sunk into the bench (m).</summary>
    public readonly record struct Piece(string Blueprint, Vector2 At, float Yaw, float Sink);

    public string Map = "Glacier", Faction = "Ministry of Slop";
    public Vector2 Toward = new(1, 0); // the way from the colony, in its frame (x along its front, z back)
    public float Distance = 2000, Slack = 150, Spread = 40, Step = 10, Most = 6;
    public Vector2 BenchMin, BenchMax;
    public (Vector2 From, Vector2 To)[] Ramps = [];
    public Piece[] Pieces = [];
    public string Courier = "Ministry Courier";
    public Vector2 CourierAt;
    public float CourierYaw, CourierUp;
    public string Keeper = "", Handover = "", StolenLine = "Oi! That's a Ministry vessel!";
    public Vector2 KeeperAt;
    public string Problem = "";

    public static string FilePath => Path.Combine(AppFolders.Data, "Story", "Opening", "Spaceport.json");

    private static SpaceportScript? _current;
    public static SpaceportScript Current => _current ??= Load(FilePath);

    public static SpaceportScript Load(string path)
    {
        var t = new SpaceportScript();
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("format", out var f) || f.GetString() != Format)
                throw new FormatException($"\"format\" must be \"{Format}\"");
            static Vector2 V(JsonElement a, float most = 400) => a.GetArrayLength() == 2 && float.IsFinite(a[0].GetSingle() + a[1].GetSingle())
                && MathF.Abs(a[0].GetSingle()) <= most && MathF.Abs(a[1].GetSingle()) <= most
                ? new Vector2(a[0].GetSingle(), a[1].GetSingle()) : throw new FormatException($"a spot is [x, z] within {most:0} m");
            static float N(JsonElement e, string key, float def, float lo, float hi) =>
                e.TryGetProperty(key, out var v) ? (v.GetSingle() is var x && float.IsFinite(x) && x >= lo && x <= hi ? x : throw new FormatException($"\"{key}\" is {lo:0.##} to {hi:0.##}")) : def;
            t.Map = root.TryGetProperty("map", out var m) ? m.GetString() ?? "" : t.Map;
            t.Faction = root.TryGetProperty("faction", out var fa) ? fa.GetString() ?? "" : t.Faction;
            var place = root.GetProperty("place");
            t.Toward = V(place.GetProperty("toward"), 1);
            if (t.Toward.LengthSquared() < 0.25f)
                throw new FormatException("\"toward\" is a direction, [x, z]");
            t.Distance = N(place, "distance", 2000, 100, 6000);
            t.Slack = N(place, "slack", 150, 0, 1000);
            t.Spread = N(place, "spread", 40, 0, 180);
            t.Step = N(place, "step", 10, 1, 90);
            t.Most = N(place, "most", 6, 0.5f, 20);
            var bench = root.GetProperty("bench");
            t.BenchMin = V(bench.GetProperty("min"), 200);
            t.BenchMax = V(bench.GetProperty("max"), 200);
            if (t.BenchMax.X - t.BenchMin.X is < 16 or > 160 || t.BenchMax.Y - t.BenchMin.Y is < 16 or > 160)
                throw new FormatException("the bench is 16 to 160 m each way");
            if (bench.TryGetProperty("ramps", out var ramps))
            {
                t.Ramps = [.. ramps.EnumerateArray().Select(r => (V(r.GetProperty("from"), 200), V(r.GetProperty("to"), 200)))];
                if (t.Ramps.Length > 8 || t.Ramps.Any(r => Vector2.Distance(r.From, r.To) > GroundShape.MaxLength))
                    throw new FormatException($"at most 8 ramps, each {GroundShape.MaxLength:0} m long at most");
            }
            var list = new List<Piece>();
            foreach (var e in root.GetProperty("pieces").EnumerateArray())
            {
                var name = e.GetProperty("blueprint").GetString() ?? "";
                if (ConstructBuild.FindBlueprint(name) == null)
                    throw new FormatException($"no blueprint named \"{name}\"");
                list.Add(new Piece(name, V(e.GetProperty("at"), 200), N(e, "yaw", 0, -360, 360), N(e, "sink", 0, -2, 2)));
            }
            if (list.Count is 0 or > MaxPieces)
                throw new FormatException($"{list.Count} pieces (1 to {MaxPieces})");
            t.Pieces = [.. list];
            var courier = root.GetProperty("courier");
            t.Courier = courier.GetProperty("blueprint").GetString() ?? "";
            if (ConstructBuild.FindBlueprint(t.Courier) == null)
                throw new FormatException($"no blueprint named \"{t.Courier}\"");
            t.CourierAt = V(courier.GetProperty("at"), 200);
            t.CourierYaw = N(courier, "yaw", 0, -360, 360);
            t.CourierUp = N(courier, "up", 0, -2, 40);
            if (root.TryGetProperty("keeper", out var k))
            {
                t.Keeper = k.GetProperty("name").GetString() ?? "";
                t.KeeperAt = V(k.GetProperty("at"), 200);
                t.Handover = k.TryGetProperty("handover", out var h) ? h.GetString() ?? "" : "";
                t.StolenLine = k.TryGetProperty("stolen", out var sl) ? sl.GetString() ?? t.StolenLine : t.StolenLine;
                if (t.StolenLine.Any(c => c < ' ' || c > '~'))
                    throw new FormatException("\"stolen\" is ASCII");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or FormatException or KeyNotFoundException or InvalidOperationException)
        {
            t.Problem = $"{Path.GetFileName(path)}: {ex.Message}";
        }
        return t;
    }
}
