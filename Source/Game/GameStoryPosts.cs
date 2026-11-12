using System.Numerics;
using System.Text.Json;
using CallOfSlop.Story;

namespace CallOfSlop;

/// <summary>
/// The staff's posts (docs/story/STORY.md "Bodies", "Posts"; data Story/Posts/PenalColony.json): a person on a Work step
/// in the colony doesn't stand about at the work spot but goes to a post and keeps it for the shift: sat at the duty desk
/// in the guards' barracks doing the admin, at the fax, at the tea urn, on watch at the gate. Each post names who takes
/// it first; a post another body holds is passed over, and with nothing free the person works at the work spot as
/// before. Scripted and shown, never told: nothing is said about it. A sitting post is the real seat (the same Use a
/// player has, so every peer sees them sat there); anything else the body does (a reaction, a fight, a talk with
/// someone, a step's end) gets them up first. Posts are looked at with the bodies, four times a second; nothing
/// allocates.
/// </summary>
public sealed partial class Game
{
    public const float StoryPostNear = 1.2f, StoryPostSitReach = 1.6f; // m: at the post; close enough to sit
    public const double StoryPostStuck = 8;

    /// <summary>What a post stands for in this world now: where the body stands, what it faces, and the seat (its construct and part; 0: none).</summary>
    private bool StoryPostAt(StoryPost post, out Vector3 stand, out Vector3 face, out ushort seatBody, out int seatPart)
    {
        (stand, face, seatBody, seatPart) = (default, default, 0, -1);
        if (post.At == StoryPostPlace.Gate)
        {
            if (_opColony == 0)
                return false;
            stand = _opGate;
            face = _opGate + (_opGate - _opMiddle) with { Y = 0 }; // (looking out, away from the colony)
            return true;
        }
        var c = Constructs.Get(post.At == StoryPostPlace.Barracks ? _opBarracks : _opColony);
        if (c == null || post.At == StoryPostPlace.Barracks && _opBarracks == 0)
            return false;
        var part = -1;
        for (int i = 0, n = 0; i < c.PartCount; i++)
            if (!c.Parts[i].Broken && c.Parts[i].D.Name == post.Part && n++ == post.Nth)
            {
                part = i;
                break;
            }
        if (part < 0)
            return false; // (a world from before the posts: the barracks has no furniture)
        Freeform.PartWorld(c, part, out var at, out var rot);
        // A seat faces its -z and is come to from behind (the side its back is on: a desk is in front of it); anything else
        // is faced from "front" m along its own z.
        var front = Vector3.Transform(new Vector3(0, 0, post.Sit ? 0.9f : post.Front), rot);
        stand = at + front with { Y = 0 };
        face = post.Sit ? at - front * 3 : at;
        if (post.Sit)
            (seatBody, seatPart) = (c.Id, part);
        return true;
    }

    /// <summary>
    /// The goal of a body on a Work step (from <see cref="StoryStepSpot"/>): its post for this shift, picked at the step's
    /// start, or null for the work spot (no post free, none here).
    /// </summary>
    private Vector3? StoryPostGoal(StoryEngine s, StoryAgent a, ref StoryBody b)
    {
        var posts = StoryPostsData.Current.Posts;
        if (posts.Length == 0 || _opColony == 0)
            return null;
        if (b.PostFor != a.DoingFrom)
        {
            b.PostFor = a.DoingFrom;
            b.Post = StoryPostPick(s.World.NameOf(a.Entity), b.Entity, a.DoingFrom);
            b.PostStuck = -1;
            if (b.Post > 0)
                Console.WriteLine($"[story] {s.World.NameOf(a.Entity)}: the shift at the post \"{posts[b.Post - 1].Name}\"");
        }
        if (b.Post == 0 || !StoryPostAt(posts[b.Post - 1], out var stand, out _, out _, out _))
            return null;
        b.PostGoal = stand;
        return stand;
    }

    /// <summary>A post for this person's shift (its index + 1; 0: none free): those naming them first, then anyone's.</summary>
    private short StoryPostPick(string name, int entity, double from)
    {
        var posts = StoryPostsData.Current.Posts;
        var turn = (int)(StoryMix(StoryNameKey(name) ^ (ulong)(long)(from * 10)) % 997);
        for (var pass = 0; pass < 2; pass++)
        {
            var n = 0;
            for (var i = 0; i < posts.Length; i++)
                if (StoryPostFor(posts[i], name, pass))
                    n++;
            for (var k = 0; k < n; k++)
            {
                // The k-th (from the turn) of this pass's posts that is free.
                var want = (turn + k) % n;
                for (int i = 0, m = 0; i < posts.Length; i++)
                {
                    if (!StoryPostFor(posts[i], name, pass) || m++ != want)
                        continue;
                    if (!StoryPostHeld(i + 1, entity) && StoryPostAt(posts[i], out _, out _, out _, out _))
                        return (short)(i + 1);
                    break;
                }
            }
        }
        return 0;
    }

    private static bool StoryPostFor(StoryPost post, string name, int pass)
    {
        if (pass == 1)
            return post.Who.Length == 0;
        foreach (var w in post.Who)
            if (w.Equals(name, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private bool StoryPostHeld(int post, int entity)
    {
        for (var i = 0; i < _bodyCount; i++)
            if (_bodies[i].Post == post && _bodies[i].Entity != entity)
                return true;
        return false;
    }

    // The barracks' doorway (the Colony Barracks blueprint, its frame in metres): the room is x 0 to 4 by z 0 to 8 with the
    // doorway in the east wall (x 4, the door corner's leg) from z 0.1 to 1.6. Bots don't find doorways, so a body going in or out walks to the
    // spot outside it, then the spot inside, or the other way round.
    private static readonly Vector3 BarracksDoorOut = new(5.4f, 0, 0.9f), BarracksDoorIn = new(2.7f, 0, 0.9f);

    /// <summary>A point in a building's blueprint frame (its first part's: the first pad, at the blueprint's origin) and back. The construct's own frame is its centre of mass's.</summary>
    private static Vector3 BpLocal(Construct c, Vector3 world)
    {
        Freeform.PartWorld(c, 0, out var at, out var rot);
        return Vector3.Transform(world - at, Quaternion.Conjugate(rot));
    }

    private static Vector3 BpWorld(Construct c, Vector3 local)
    {
        Freeform.PartWorld(c, 0, out var at, out var rot);
        return at + Vector3.Transform(local, rot);
    }

    private static bool InBarracks(Vector3 local) => local.X is > 0.25f and < 3.75f && local.Z is > 0.25f and < 7.75f;

    /// <summary>
    /// The barracks' doorway step (from <see cref="StoryBodyBot"/>): the floor is a pad's height above the ground, which a
    /// bot doesn't climb, so a body held at the threshold on its way in for a moment steps in (put just inside).
    /// </summary>
    private void StoryDoorStep(Player p, ref StoryBody b, Vector3 goal, double now)
    {
        if (_opBarracks == 0 || Constructs.Get(_opBarracks) is not { } c)
            return;
        var me = BpLocal(c, p.Position);
        var at = !InBarracks(me) && InBarracks(BpLocal(c, goal)) && me.Z is > 0.1f and < 1.7f && me.X is > 3.3f and < 6f;
        if (!at)
        {
            b.DoorSince = -1;
            return;
        }
        if (b.DoorSince < 0)
            b.DoorSince = now;
        else if (now - b.DoorSince > 1.5 && SandboxSpawnSpot(BpWorld(c, BarracksDoorIn with { Y = 1f }), out var feet) == null)
        {
            p.Teleport(feet);
            b.DoorSince = -1;
        }
    }

    /// <summary>Where a body walks for <paramref name="goal"/>: by way of the barracks' doorway when one of them is inside it.</summary>
    private Vector3 StoryDoorRoute(Vector3 at, Vector3 goal)
    {
        if (_opBarracks == 0 || Constructs.Get(_opBarracks) is not { } c)
            return goal;
        var me = BpLocal(c, at);
        var inside = InBarracks(me);
        if (inside == InBarracks(BpLocal(c, goal)))
            return goal;
        var flatMe = me with { Y = 0 };
        var inDoor = me.Z is > 0.3f and < 1.5f && me.X is > 2.4f and < 5.6f;
        var near = inside ? Vector3.DistanceSquared(flatMe, BarracksDoorIn) < 1.5f * 1.5f : Vector3.DistanceSquared(flatMe, BarracksDoorOut) < 1.5f * 1.5f;
        var next = inDoor || near ? (inside ? BarracksDoorOut : BarracksDoorIn) : inside ? BarracksDoorIn : BarracksDoorOut;
        if (!inside && !inDoor && !near && me.X < 4.2f)
            next = me.Z < -0.3f ? new Vector3(5.4f, 0, -1.2f) // (round the front corner)
                : me.Z > 8.3f ? new Vector3(5.4f, 0, 9.2f) // (round the back corner)
                : me.X < 0 ? new Vector3(-1.4f, 0, -1.4f) : BarracksDoorOut; // (beside the west wall: to the front first)
        var w = BpWorld(c, next);
        return w with { Y = at.Y };
    }

    /// <summary>
    /// A body's post, in its brain (from <see cref="StoryBodyBot"/>): sat down at a sitting post once beside it, turned
    /// to what it works at, and up again the moment it is doing anything else.
    /// </summary>
    private void StoryPostBot(Player p, BotBrain bot, ref StoryBody b, double now)
    {
        ref var h = ref SandboxHands.Hand(p.Id);
        var posts = StoryPostsData.Current.Posts;
        var on = b.Post > 0 && b.Post <= posts.Length && b.Fight == 0 && !b.Leaving && b.Goal == b.PostGoal
            && Story is { } s && s.Agents.Of(b.Entity) is { Busy: true } a && a.DoingFrom == b.PostFor && s.Rules.People.Actions[a.Doing.Action].Verb == _vWork;
        if (!on)
        {
            if (b.PostSat)
            {
                b.PostSat = false;
                if (h.Seated)
                    SandboxStand(p, teleport: true);
            }
            return;
        }
        var post = posts[b.Post - 1];
        if (!StoryPostAt(post, out var stand, out var face, out var seatBody, out var seatPart))
            return;
        if (b.PostSat && !h.Seated)
            b.PostSat = false; // (the seat went, or the seat stood them up)
        if (!b.PostSat && post.Sit)
        {
            var d2 = Flat2(stand - p.Position);
            if (d2 > 6f * 6f)
                b.PostStuck = -1;
            else if (b.PostStuck < 0)
                b.PostStuck = now;
            var stuck = b.PostStuck >= 0 && now - b.PostStuck > StoryPostStuck;
            if ((d2 <= StoryPostSitReach * StoryPostSitReach || stuck) && Constructs.Get(seatBody) is { } chair && !h.Seated)
            {
                if (stuck)
                    p.Teleport(stand); // (held up by a corner of the doorway: in they go)
                if (SandboxHands.Use(Constructs, p.Id, p.Eye, chair.Id, seatPart) == null)
                    b.PostSat = true;
                else if (stuck)
                    b.PostStuck = now; // (somebody's in it: wait, then try again)
            }
        }
        if (b.PostSat)
        {
            bot.ForcedGoal = p.Position;
            bot.HoldAnchor = p.Position;
        }
        else
        {
            var d2 = Flat2(stand - p.Position);
            if (d2 is > 0.3f * 0.3f and < 1.6f * 1.6f && !post.Sit)
            {
                bot.ForcedGoal = stand; // (right up to it: a hold stops a bot 1.5 m short)
                bot.HoldUntil = 0;
            }
            if (d2 > StoryPostNear * StoryPostNear)
                return; // (still on the way)
        }
        var dir = face - p.Position;
        if (Flat2(dir) < 0.04f)
            return;
        bot.GlanceYaw = GravityFrames.YawOf(p.Position, dir);
        bot.NextGlance = bot.GlanceUntil = now + 0.5; // (turned to the work; a talk or a reaction turns them after this)
    }
}

/// <summary>Where a post is: a part in the guards' barracks or in the colony, or the colony's gate.</summary>
public enum StoryPostPlace : byte { Barracks, Colony, Gate }

/// <summary>One post (Story/Posts/PenalColony.json).</summary>
public sealed class StoryPost
{
    public string Name = "", About = "", Part = "";
    public StoryPostPlace At;
    public int Nth;
    public bool Sit;
    public float Front;
    public string[] Who = [];
}

/// <summary>
/// Story/Posts/PenalColony.json (format call-of-slop-posts/1): the colony staff's posts for a work shift. A bad file is
/// refused with a reason (Problem) and there are no posts (the staff stand at the work spot, as before).
/// </summary>
public sealed class StoryPostsData
{
    public const string Format = "call-of-slop-posts/1";
    public const int MaxPosts = 16;

    public string Problem = "";
    public StoryPost[] Posts = [];

    private static StoryPostsData? _current;
    public static StoryPostsData Current => _current ??= Load(Path.Combine(AppFolders.Data, "Story", "Posts", "PenalColony.json"));

    public static StoryPostsData Load(string path)
    {
        try
        {
            var d = Parse(File.ReadAllText(path));
            Console.WriteLine($"[story] posts: {d.Posts.Length}");
            return d;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or FormatException or InvalidOperationException)
        {
            Console.WriteLine($"[story] posts refused: {ex.Message}");
            return new StoryPostsData { Problem = $"PenalColony.json (posts): {ex.Message}" };
        }
    }

    public static StoryPostsData Parse(string json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var root = doc.RootElement;
        if (!root.TryGetProperty("format", out var f) || f.GetString() != Format)
            throw new FormatException($"format must be \"{Format}\"");
        if (!root.TryGetProperty("posts", out var list) || list.ValueKind != JsonValueKind.Array)
            throw new FormatException("\"posts\" is a list");
        if (list.GetArrayLength() > MaxPosts)
            throw new FormatException($"at most {MaxPosts} posts");
        var o = new List<StoryPost>();
        var n = 0;
        foreach (var e in list.EnumerateArray())
        {
            var where = $"posts[{n++}]";
            string Str(string key) => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
            var post = new StoryPost { Name = Str("name"), About = Str("about"), Part = Str("part") };
            if (post.Name.Length == 0)
                throw new FormatException($"{where}: a name");
            var at = Str("at");
            post.At = at.ToLowerInvariant() switch
            {
                "barracks" => StoryPostPlace.Barracks,
                "colony" => StoryPostPlace.Colony,
                "gate" => StoryPostPlace.Gate,
                _ => throw new FormatException($"{where} ({post.Name}): \"at\" is barracks, colony or gate"),
            };
            if (post.At != StoryPostPlace.Gate && post.Part.Length == 0)
                throw new FormatException($"{where} ({post.Name}): a part to work at (\"part\")");
            if (post.At != StoryPostPlace.Gate && ConstructParts.Find(post.Part) == null)
                throw new FormatException($"{where} ({post.Name}): no such part \"{post.Part}\"");
            post.Nth = e.TryGetProperty("nth", out var nth) && nth.TryGetInt32(out var k) ? k : 0;
            if (post.Nth is < 0 or > 15)
                throw new FormatException($"{where} ({post.Name}): \"nth\" is 0 to 15");
            var does = Str("do");
            post.Sit = does.ToLowerInvariant() switch
            {
                "sit" => true,
                "" or "stand" => false,
                _ => throw new FormatException($"{where} ({post.Name}): \"do\" is sit or stand"),
            };
            if (post.Sit && post.At == StoryPostPlace.Gate)
                throw new FormatException($"{where} ({post.Name}): nothing to sit on at the gate");
            post.Front = e.TryGetProperty("front", out var fr) && fr.TryGetSingle(out var fv) ? fv : 0.7f;
            if (post.Front is < -3 or > 3)
                throw new FormatException($"{where} ({post.Name}): \"front\" is -3 to 3 m");
            if (e.TryGetProperty("who", out var who))
            {
                if (who.ValueKind != JsonValueKind.Array)
                    throw new FormatException($"{where} ({post.Name}): \"who\" is a list of names");
                post.Who = [.. who.EnumerateArray().Select(w => w.GetString() ?? "")];
            }
            foreach (var p in o)
                if (p.Name.Equals(post.Name, StringComparison.OrdinalIgnoreCase))
                    throw new FormatException($"{where}: two posts named \"{post.Name}\"");
            o.Add(post);
        }
        return new StoryPostsData { Posts = [.. o] };
    }
}
