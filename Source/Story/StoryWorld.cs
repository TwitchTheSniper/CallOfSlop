namespace CallOfSlop.Story;

/// <summary>A relationship as one entity holds it: the relation and target packed in one key (relation high, target low), and its strength.</summary>
public struct StoryPair
{
    public ulong Key;
    public int Value;

    public readonly ushort Rel => (ushort)(Key >> 32);
    public readonly int Target => (int)(uint)Key;

    public static ulong Pack(int rel, int target) => (ulong)(uint)rel << 32 | (uint)target;
}

/// <summary>One thing in the story world: a player, a person, a faction, a place, a construct.</summary>
public sealed class StoryEntity
{
    public int Id;
    public StoryKind Kind;
    public string Name = "";
    public ulong Key; // its game object's stable key (a player's world key, a place's name hash); 0: none
    public bool Gone, Scripted;
    public double Born;
    public int Keys; // game objects pointing at it now (a construct and its wreck pieces); at 0 it is gone

    // Its relationships, sorted by key (so one relation's targets sit together), and who holds one towards it.
    public StoryPair[] Pairs = [];
    public int PairCount;
    public ulong[] Held = []; // (relation << 32 | subject), sorted
    public int HeldCount;
    public (ushort Quality, int Value)[] Qualities = [];
    public int QualityCount;
}

/// <summary>
/// The relational world state (docs/story/STORY.md, box 1): entities, their qualities, and relationship pairs indexed both
/// ways, so "whom does Karl hold a grudge against" and "who holds a grudge against Karl" are both a binary search. Story
/// entities number in the hundreds or low thousands, so sorted arrays per entity are all it needs: no archetype tables.
/// Changes happen only on events (rare), never per tick. Not thread-safe: the host's tick owns it.
/// </summary>
public sealed class StoryWorld
{
    public const int MaxEntities = 100_000;

    public readonly List<StoryEntity> All = new(256);
    private readonly Dictionary<(StoryKind, ulong), int> _byKey = new();
    private readonly Dictionary<string, int> _byName = new(StringComparer.OrdinalIgnoreCase);
    public readonly StoryRules Rules;
    public int PairTotal { get; private set; }

    public StoryWorld(StoryRules rules) => Rules = rules;

    public int Count => All.Count;
    public StoryEntity this[int id] => All[id];
    public bool Valid(int id) => (uint)id < (uint)All.Count;

    public void Clear()
    {
        All.Clear();
        _byKey.Clear();
        _byName.Clear();
        PairTotal = 0;
    }

    /// <summary>A new entity (key 0: not found again by key). -1 when the world is full.</summary>
    public int Create(StoryKind kind, string name, ulong key, double now, bool scripted = false)
    {
        if (All.Count >= MaxEntities)
            return -1;
        var e = new StoryEntity { Id = All.Count, Kind = kind, Name = name, Key = key, Born = now, Scripted = scripted };
        All.Add(e);
        Bind(kind, key, e.Id);
        _byName.TryAdd(name, e.Id);
        return e.Id;
    }

    /// <summary>Every (kind, key) and the entity it points at (a save writes them).</summary>
    public Dictionary<(StoryKind Kind, ulong Key), int> KeyMap => _byKey; // (read it, never change it: Bind and Unbind do)

    public int KeyMapCount => _byKey.Count;

    /// <summary>The entity for a game object's key, or -1.</summary>
    public int Find(StoryKind kind, ulong key) => key != 0 && _byKey.TryGetValue((kind, key), out var id) ? id : -1;

    /// <summary>The first entity of that name (any case), or -1. Names needn't be unique; queries and scenarios use them.</summary>
    public int Find(string name)
    {
        if (!_byName.TryGetValue(name, out var id))
            return -1;
        if (!All[id].Gone)
            return id;
        foreach (var e in All)
            if (!e.Gone && e.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return e.Id;
        return id;
    }

    /// <summary>The entity for a key, made (with this name) if it isn't there yet; its name follows the game's.</summary>
    public int Get(StoryKind kind, ulong key, string name, double now)
    {
        var id = Find(kind, key);
        if (id >= 0)
        {
            if (All[id].Name != name && name.Length > 0)
                Rename(id, name);
            return id;
        }
        return Create(kind, name, key, now);
    }

    public void Rename(int id, string name)
    {
        var e = All[id];
        if (_byName.TryGetValue(e.Name, out var had) && had == id)
            _byName.Remove(e.Name);
        e.Name = name;
        _byName.TryAdd(name, id);
    }

    /// <summary>
    /// A game object went: its key is forgotten. The entity stays with its history, and is gone once no game object
    /// points at it (a construct whose wreck pieces are all salvaged).
    /// </summary>
    public void Unbind(StoryKind kind, ulong key)
    {
        if (key == 0 || !_byKey.Remove((kind, key), out var id) || !Valid(id))
            return;
        var e = All[id];
        if (--e.Keys <= 0)
        {
            e.Keys = 0;
            e.Gone = true;
        }
    }

    /// <summary>Points a key at an entity (a wreck piece is its construct's remains; a save puts its keys back).</summary>
    public void Bind(StoryKind kind, ulong key, int id)
    {
        if (key == 0 || !Valid(id))
            return;
        if (_byKey.TryGetValue((kind, key), out var had))
        {
            if (had == id)
                return;
            Unbind(kind, key);
        }
        _byKey[(kind, key)] = id;
        All[id].Keys++;
        All[id].Gone = false;
    }

    // ---- Relationships ----------------------------------------------------------------------------------------------

    private static int Search(StoryPair[] a, int n, ulong key)
    {
        int lo = 0, hi = n - 1;
        while (lo <= hi)
        {
            var mid = (lo + hi) >>> 1;
            var k = a[mid].Key;
            if (k == key)
                return mid;
            if (k < key)
                lo = mid + 1;
            else
                hi = mid - 1;
        }
        return ~lo;
    }

    private static int Search(ulong[] a, int n, ulong key)
    {
        int lo = 0, hi = n - 1;
        while (lo <= hi)
        {
            var mid = (lo + hi) >>> 1;
            var k = a[mid];
            if (k == key)
                return mid;
            if (k < key)
                lo = mid + 1;
            else
                hi = mid - 1;
        }
        return ~lo;
    }

    /// <summary>The strength of (subject, relation, target): 0 when it isn't there.</summary>
    public int Get(int subject, int rel, int target)
    {
        if (!Valid(subject))
            return 0;
        var e = All[subject];
        var i = Search(e.Pairs, e.PairCount, StoryPair.Pack(rel, target));
        return i >= 0 ? e.Pairs[i].Value : 0;
    }

    /// <summary>The range of a subject's pairs of one relation (sorted by target): [start, end).</summary>
    public (int Start, int End) Range(int subject, int rel)
    {
        var e = All[subject];
        var a = Search(e.Pairs, e.PairCount, StoryPair.Pack(rel, 0));
        a = a < 0 ? ~a : a;
        var b = Search(e.Pairs, e.PairCount, StoryPair.Pack(rel + 1, 0));
        b = b < 0 ? ~b : b;
        return (a, b);
    }

    /// <summary>The range of who holds one relation towards this entity, in its <see cref="StoryEntity.Held"/> list: [start, end).</summary>
    public (int Start, int End) HeldRange(int target, int rel)
    {
        var e = All[target];
        var a = Search(e.Held, e.HeldCount, StoryPair.Pack(rel, 0));
        a = a < 0 ? ~a : a;
        var b = Search(e.Held, e.HeldCount, StoryPair.Pack(rel + 1, 0));
        b = b < 0 ? ~b : b;
        return (a, b);
    }

    /// <summary>Sets (subject, relation, target) to a strength; 0 removes it. Exclusive relations drop the subject's other target; symmetric ones set both ways.</summary>
    public void Set(int subject, int rel, int target, int value)
    {
        if (!Valid(subject) || !Valid(target) || subject == target || (uint)rel >= (uint)Rules.Relations.Length)
            return;
        var def = Rules.Relations[rel];
        if (def.Exclusive && value != 0)
        {
            var (a, b) = Range(subject, rel);
            var e = All[subject];
            for (var i = b - 1; i >= a; i--)
                if (e.Pairs[i].Target != target)
                    SetOne(subject, rel, e.Pairs[i].Target, 0);
        }
        SetOne(subject, rel, target, value);
        if (def.Symmetric)
            SetOne(target, rel, subject, value);
    }

    /// <summary>Adds to a strength (0 and below removes it).</summary>
    public void Add(int subject, int rel, int target, int delta)
    {
        var now = Get(subject, rel, target);
        Set(subject, rel, target, (int)Math.Clamp((long)now + delta, 0, 1_000_000));
    }

    private void SetOne(int subject, int rel, int target, int value)
    {
        var e = All[subject];
        var key = StoryPair.Pack(rel, target);
        var i = Search(e.Pairs, e.PairCount, key);
        if (i >= 0)
        {
            if (value != 0)
            {
                e.Pairs[i].Value = value;
                return;
            }
            Array.Copy(e.Pairs, i + 1, e.Pairs, i, e.PairCount - i - 1);
            e.PairCount--;
            PairTotal--;
            var t = All[target];
            var j = Search(t.Held, t.HeldCount, StoryPair.Pack(rel, subject));
            if (j >= 0)
            {
                Array.Copy(t.Held, j + 1, t.Held, j, t.HeldCount - j - 1);
                t.HeldCount--;
            }
            return;
        }
        if (value == 0)
            return;
        i = ~i;
        if (e.PairCount == e.Pairs.Length)
            Array.Resize(ref e.Pairs, Math.Max(4, e.Pairs.Length * 2));
        Array.Copy(e.Pairs, i, e.Pairs, i + 1, e.PairCount - i);
        e.Pairs[i] = new StoryPair { Key = key, Value = value };
        e.PairCount++;
        PairTotal++;
        var to = All[target];
        var hk = StoryPair.Pack(rel, subject);
        var h = ~Search(to.Held, to.HeldCount, hk);
        if (to.HeldCount == to.Held.Length)
            Array.Resize(ref to.Held, Math.Max(4, to.Held.Length * 2));
        Array.Copy(to.Held, h, to.Held, h + 1, to.HeldCount - h);
        to.Held[h] = hk;
        to.HeldCount++;
    }

    // ---- Qualities --------------------------------------------------------------------------------------------------

    public int Quality(int id, int quality)
    {
        if (!Valid(id))
            return 0;
        var e = All[id];
        for (var i = 0; i < e.QualityCount; i++)
            if (e.Qualities[i].Quality == quality)
                return e.Qualities[i].Value;
        return 0;
    }

    public void SetQuality(int id, int quality, int value)
    {
        if (!Valid(id) || (uint)quality >= (uint)Rules.Qualities.Length)
            return;
        var e = All[id];
        for (var i = 0; i < e.QualityCount; i++)
            if (e.Qualities[i].Quality == quality)
            {
                if (value == 0)
                    e.Qualities[i] = e.Qualities[--e.QualityCount];
                else
                    e.Qualities[i].Value = value;
                return;
            }
        if (value == 0)
            return;
        if (e.QualityCount == e.Qualities.Length)
            Array.Resize(ref e.Qualities, Math.Max(2, e.Qualities.Length * 2));
        e.Qualities[e.QualityCount++] = ((ushort)quality, value);
    }

    // ---- Effects ----------------------------------------------------------------------------------------------------

    /// <summary>Applies effects whose slots are filled from <paramref name="slots"/> (-1: empty, the effect is skipped).</summary>
    public void Apply(ReadOnlySpan<StoryEffect> effects, ReadOnlySpan<int> slots)
    {
        foreach (var fx in effects)
        {
            var s = fx.Subject < slots.Length ? slots[fx.Subject] : -1;
            if (!Valid(s))
                continue;
            if (fx.Quality)
            {
                var v = fx.Op == StoryOp.Set ? fx.Amount : (int)Math.Clamp((long)Quality(s, fx.What) + fx.Amount, -1_000_000_000, 1_000_000_000);
                SetQuality(s, fx.What, v);
                continue;
            }
            var o = fx.Object >= 0 && fx.Object < slots.Length ? slots[fx.Object] : -1;
            if (!Valid(o) || o == s)
                continue;
            if (fx.Op == StoryOp.Set)
                Set(s, fx.What, o, Math.Max(0, fx.Amount));
            else
                Add(s, fx.What, o, fx.Amount);
        }
    }

    /// <summary>A name for the window and sentences ("nobody" for an empty slot).</summary>
    public string NameOf(int id) => Valid(id) ? All[id].Name : "nobody";
}
