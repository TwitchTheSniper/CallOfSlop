namespace CallOfSlop.Story;

/// <summary>
/// A query over the story world (docs/story/STORY.md "Queries"): clauses joined by commas, each one of
/// <list type="bullet">
/// <item><c>?a Grudge ?b</c>, <c>?a Grudge ?b &gt;= 20</c>: a relationship (present, or its strength compared)</item>
/// <item><c>?a Kills &gt; 2</c>, <c>?a Kills</c>: a quality (compared, or not zero)</item>
/// <item><c>?a is Player</c>: an entity's kind</item>
/// <item><c>?a != ?b</c>, <c>?a == ?b</c></item>
/// <item><c>not</c> before any of the first three: there is no such thing (with a variable unbound: none at all)</item>
/// </list>
/// A term is a variable (<c>?name</c>) or an entity by name (<c>Karl</c>, <c>'Glacier Station'</c>). Solving backtracks
/// over the variables, generating from relationship indices both ways; it allocates nothing unless results are collected.
/// </summary>
public sealed class StoryQuery
{
    public enum ClauseKind : byte
    {
        Relation,
        Quality,
        Kind,
        Same,
        Different,
    }

    public enum Cmp : byte
    {
        NonZero,
        Eq,
        Ne,
        Lt,
        Le,
        Gt,
        Ge,
    }

    public readonly record struct Term(int Var, string? Name)
    {
        public override string ToString() => Var >= 0 ? $"?{Var}" : $"'{Name}'";
    }

    public readonly record struct Clause(ClauseKind Kind, bool Not, Term A, ushort What, Term B, Cmp Op, int N);

    public Clause[] Clauses = [];
    public string[] ClauseTexts = []; // each clause as written, in the order solved (why-not)
    public string Text = "";
    public int VarCount;

    /// <summary>
    /// Compiles <paramref name="text"/>. Variables are looked up in (and new ones added to) <paramref name="vars"/>, up to
    /// <paramref name="maxVars"/>; those already in it are taken as bound before the query runs.
    /// </summary>
    public static StoryQuery Compile(string text, StoryRules rules, List<string> vars, int maxVars = StoryRules.MaxVars)
    {
        var bound = vars.Count;
        var q = new StoryQuery { Text = text };
        var list = new List<Clause>();
        var texts = new List<string>();
        var generated = new HashSet<int>();
        foreach (var raw in SplitClauses(text))
        {
            var t = Tokens(raw);
            if (t.Count == 0)
                continue;
            texts.Add(raw.Trim());
            var not = t[0] == "not";
            if (not)
                t.RemoveAt(0);
            if (t.Count < 2)
                throw new StoryException($"\"{raw}\" is too short");
            Term Of(string w)
            {
                if (!w.StartsWith('?'))
                    return new Term(-1, w);
                var name = w[1..];
                if (!StoryRules.Word(name))
                    throw new StoryException($"\"{w}\" isn't a variable name");
                var i = vars.IndexOf(name);
                if (i < 0)
                {
                    if (vars.Count >= maxVars)
                        throw new StoryException($"?{name}: no new variables here (or more than {maxVars})");
                    vars.Add(name);
                    i = vars.Count - 1;
                }
                return new Term(i, null);
            }
            if (t[1] is "==" or "!=")
            {
                if (not || t.Count != 3)
                    throw new StoryException($"\"{raw}\": compare two terms, like \"?a != ?b\"");
                list.Add(new Clause(t[1] == "==" ? ClauseKind.Same : ClauseKind.Different, false, Of(t[0]), 0, Of(t[2]), Cmp.NonZero, 0));
                continue;
            }
            if (t[1] == "is")
            {
                if (t.Count != 3 || !Enum.TryParse<StoryKind>(t[2], true, out var kind))
                    throw new StoryException($"\"{raw}\": a kind is one of {string.Join(", ", Enum.GetNames<StoryKind>())}");
                var a = Of(t[0]);
                if (!not && a.Var >= 0)
                    generated.Add(a.Var);
                list.Add(new Clause(ClauseKind.Kind, not, a, (ushort)kind, default, Cmp.NonZero, 0));
                continue;
            }
            if (rules.Quality(t[1]) is { } qd)
            {
                var (op, n) = t.Count switch
                {
                    2 => (Cmp.NonZero, 0),
                    4 => (ParseCmp(t[2], raw), Number(t[3], raw)),
                    _ => throw new StoryException($"\"{raw}\": a quality is \"?a {qd.Name}\" or \"?a {qd.Name} > 2\""),
                };
                var a = Of(t[0]);
                if (!not && a.Var >= 0)
                    generated.Add(a.Var);
                list.Add(new Clause(ClauseKind.Quality, not, a, qd.Id, default, op, n));
                continue;
            }
            if (rules.Relation(t[1]) is not { } rd)
                throw new StoryException($"\"{raw}\": \"{t[1]}\" isn't a relation or quality, nor \"is\", \"==\" or \"!=\"");
            var (rop, rn) = t.Count switch
            {
                3 => (Cmp.NonZero, 0),
                5 => (ParseCmp(t[3], raw), Number(t[4], raw)),
                _ => throw new StoryException($"\"{raw}\": a relation is \"?a {rd.Name} ?b\" or \"?a {rd.Name} ?b >= 20\""),
            };
            var s = Of(t[0]);
            var o = Of(t[2]);
            if (!not)
            {
                if (s.Var >= 0)
                    generated.Add(s.Var);
                if (o.Var >= 0)
                    generated.Add(o.Var);
            }
            list.Add(new Clause(ClauseKind.Relation, not, s, rd.Id, o, rop, rn));
        }
        if (list.Count == 0)
            throw new StoryException("an empty query");
        foreach (var c in list)
            if (c.Kind is ClauseKind.Same or ClauseKind.Different)
                foreach (var term in (ReadOnlySpan<Term>)[c.A, c.B])
                    if (term.Var >= bound && !generated.Contains(term.Var))
                        throw new StoryException($"?{vars[term.Var]} is only compared: say what it is first (\"?{vars[term.Var]} is Player\")");
        // Generators first (in the order written), then the tests that need their variables.
        static bool Generates(Clause c) => !c.Not && c.Kind is ClauseKind.Relation or ClauseKind.Quality or ClauseKind.Kind;
        var order = Enumerable.Range(0, list.Count).Where(i => Generates(list[i])).Concat(Enumerable.Range(0, list.Count).Where(i => !Generates(list[i]))).ToArray();
        q.Clauses = order.Select(i => list[i]).ToArray();
        q.ClauseTexts = order.Select(i => texts[i]).ToArray();
        q.VarCount = vars.Count;
        return q;
    }

    private static IEnumerable<string> SplitClauses(string text)
    {
        var start = 0;
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\'')
                quoted = !quoted;
            else if (text[i] == ',' && !quoted)
            {
                yield return text[start..i];
                start = i + 1;
            }
        }
        if (quoted)
            throw new StoryException("a quote that isn't closed");
        yield return text[start..];
    }

    private static List<string> Tokens(string clause)
    {
        var list = new List<string>();
        var i = 0;
        while (i < clause.Length)
        {
            if (char.IsWhiteSpace(clause[i]))
            {
                i++;
                continue;
            }
            if (clause[i] == '\'')
            {
                var end = clause.IndexOf('\'', i + 1);
                if (end < 0)
                    throw new StoryException("a quote that isn't closed");
                list.Add(clause[(i + 1)..end]);
                i = end + 1;
                continue;
            }
            var j = i;
            while (j < clause.Length && !char.IsWhiteSpace(clause[j]))
                j++;
            list.Add(clause[i..j]);
            i = j;
        }
        return list;
    }

    private static Cmp ParseCmp(string s, string raw) => s switch
    {
        "=" or "==" => Cmp.Eq,
        "!=" => Cmp.Ne,
        "<" => Cmp.Lt,
        "<=" => Cmp.Le,
        ">" => Cmp.Gt,
        ">=" => Cmp.Ge,
        _ => throw new StoryException($"\"{raw}\": \"{s}\" isn't a comparison (=, !=, <, <=, >, >=)"),
    };

    private static int Number(string s, string raw) =>
        int.TryParse(s, System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out var n)
            ? n : throw new StoryException($"\"{raw}\": \"{s}\" isn't a whole number");

    private static bool Test(Cmp op, int v, int n) => op switch
    {
        Cmp.NonZero => v != 0,
        Cmp.Eq => v == n,
        Cmp.Ne => v != n,
        Cmp.Lt => v < n,
        Cmp.Le => v <= n,
        Cmp.Gt => v > n,
        _ => v >= n,
    };

    // ---- Solving ----------------------------------------------------------------------------------------------------

    /// <summary>
    /// Is there a solution? On true the variables it bound keep the first solution's values (a pattern binds them); on
    /// false they are as they were. <paramref name="binds"/>: one per variable, -1 unbound.
    /// </summary>
    public bool Any(StoryWorld w, Span<int> binds) => Solve(0, w, binds, null, 1);

    private int _limit = -1; // solve only this many clauses (FirstFailing), -1 all

    /// <summary>
    /// Why not (the Story tab): the first clause, in the order solved, after which the clauses so far have no
    /// solution together; -1 when the whole query holds. <paramref name="binds"/> is left as it was.
    /// </summary>
    public int FirstFailing(StoryWorld w, ReadOnlySpan<int> binds)
    {
        Span<int> b = stackalloc int[Math.Max(VarCount, Math.Max(1, binds.Length))];
        for (var k = 1; k <= Clauses.Length; k++)
        {
            b.Fill(-1);
            binds.CopyTo(b);
            _limit = k;
            try
            {
                if (!Solve(0, w, b, null, 1))
                    return k - 1;
            }
            finally
            {
                _limit = -1;
            }
        }
        return -1;
    }

    private int[]? _into;
    private int _intoCount, _intoVar;

    /// <summary>
    /// The different values one variable takes over every solution, into <paramref name="into"/> (up to its length):
    /// whom a person could pick for a goal. Allocates nothing. <paramref name="binds"/> is left as it was.
    /// </summary>
    public int Collect(StoryWorld w, Span<int> binds, int var, int[] into)
    {
        Span<int> b = stackalloc int[Math.Max(VarCount, binds.Length)];
        b.Fill(-1);
        binds.CopyTo(b);
        _into = into;
        _intoCount = 0;
        _intoVar = var;
        try
        {
            Solve(0, w, b, null, 1);
        }
        finally
        {
            _into = null;
        }
        return _intoCount;
    }

    /// <summary>Every solution, up to <paramref name="max"/> (the window's query box).</summary>
    public List<int[]> All(StoryWorld w, int max)
    {
        var results = new List<int[]>();
        Span<int> b = stackalloc int[Math.Max(1, VarCount)];
        b.Fill(-1);
        Solve(0, w, b, results, max);
        return results;
    }

    /// <summary>The value a term has now: a bound variable, an entity by name, -1 unbound, -2 a name nobody has.</summary>
    private static int Value(in Term t, StoryWorld w, Span<int> b)
    {
        if (t.Var >= 0)
            return t.Var < b.Length ? b[t.Var] : -1;
        var id = w.Find(t.Name!);
        return id >= 0 ? id : -2;
    }

    /// <summary>True: stop (a solution found, or the results are full).</summary>
    private bool Solve(int ci, StoryWorld w, Span<int> b, List<int[]>? sink, int max)
    {
        if (ci == (_limit >= 0 ? _limit : Clauses.Length))
        {
            if (_into != null)
            {
                var v = (uint)_intoVar < (uint)b.Length ? b[_intoVar] : -1;
                if (v >= 0 && Array.IndexOf(_into, v, 0, _intoCount) < 0)
                    _into[_intoCount++] = v;
                return _intoCount >= _into.Length;
            }
            if (sink == null)
                return true;
            sink.Add(b.ToArray());
            return sink.Count >= max;
        }
        ref readonly var c = ref Clauses[ci];
        if (c.Not)
            return !Exists(c, w, b) && Solve(ci + 1, w, b, sink, max);
        switch (c.Kind)
        {
            case ClauseKind.Same:
            case ClauseKind.Different:
            {
                var x = Value(c.A, w, b);
                var y = Value(c.B, w, b);
                if (x == -1 || y == -1)
                    return false;
                return (x == y) == (c.Kind == ClauseKind.Same) && Solve(ci + 1, w, b, sink, max);
            }
            case ClauseKind.Kind:
            {
                var x = Value(c.A, w, b);
                if (x >= 0)
                    return w[x].Kind == (StoryKind)c.What && Solve(ci + 1, w, b, sink, max);
                if (x == -2)
                    return false;
                for (var id = 0; id < w.Count; id++)
                    if (w[id].Kind == (StoryKind)c.What && Bind(c.A.Var, id, ci, w, b, sink, max))
                        return true;
                return false;
            }
            case ClauseKind.Quality:
            {
                var x = Value(c.A, w, b);
                if (x >= 0)
                    return Test(c.Op, w.Quality(x, c.What), c.N) && Solve(ci + 1, w, b, sink, max);
                if (x == -2)
                    return false;
                for (var id = 0; id < w.Count; id++)
                    if (Test(c.Op, w.Quality(id, c.What), c.N) && Bind(c.A.Var, id, ci, w, b, sink, max))
                        return true;
                return false;
            }
            default:
                return SolveRelation(ci, c, w, b, sink, max);
        }
    }

    private bool Bind(int v, int id, int ci, StoryWorld w, Span<int> b, List<int[]>? sink, int max)
    {
        b[v] = id;
        if (Solve(ci + 1, w, b, sink, max))
        {
            if (sink != null)
                b[v] = -1;
            return true;
        }
        b[v] = -1;
        return false;
    }

    private bool SolveRelation(int ci, in Clause c, StoryWorld w, Span<int> b, List<int[]>? sink, int max)
    {
        var s = Value(c.A, w, b);
        var o = Value(c.B, w, b);
        if (s == -2 || o == -2)
            return false;
        if (s >= 0 && o >= 0)
            return Test(c.Op, w.Get(s, c.What, o), c.N) && Solve(ci + 1, w, b, sink, max);
        if (s >= 0)
        {
            var e = w[s];
            var (from, to) = w.Range(s, c.What);
            for (var i = from; i < to && i < e.PairCount; i++)
            {
                var p = e.Pairs[i];
                if (Test(c.Op, p.Value, c.N) && Bind(c.B.Var, p.Target, ci, w, b, sink, max))
                    return true;
            }
            return false;
        }
        if (o >= 0)
        {
            var e = w[o];
            var (from, to) = w.HeldRange(o, c.What);
            for (var i = from; i < to && i < e.HeldCount; i++)
            {
                var subject = (int)(uint)e.Held[i];
                if (Test(c.Op, w.Get(subject, c.What, o), c.N) && Bind(c.A.Var, subject, ci, w, b, sink, max))
                    return true;
            }
            return false;
        }
        // Both free: every holder of the relation.
        for (var id = 0; id < w.Count; id++)
        {
            var e = w[id];
            var (from, to) = w.Range(id, c.What);
            if (from >= to)
                continue;
            b[c.A.Var] = id;
            for (var i = from; i < to && i < e.PairCount; i++)
            {
                var p = e.Pairs[i];
                if (c.B.Var == c.A.Var || !Test(c.Op, p.Value, c.N))
                    continue;
                if (Bind(c.B.Var, p.Target, ci, w, b, sink, max))
                {
                    if (sink != null)
                        b[c.A.Var] = -1;
                    return true;
                }
            }
            b[c.A.Var] = -1;
        }
        return false;
    }

    /// <summary>A not-clause's test: is there anything that fits (its free variables taken as "any")?</summary>
    private static bool Exists(in Clause c, StoryWorld w, Span<int> b)
    {
        var x = Value(c.A, w, b);
        if (x == -2)
            return false;
        switch (c.Kind)
        {
            case ClauseKind.Kind:
                if (x >= 0)
                    return w[x].Kind == (StoryKind)c.What;
                for (var id = 0; id < w.Count; id++)
                    if (w[id].Kind == (StoryKind)c.What)
                        return true;
                return false;
            case ClauseKind.Quality:
                if (x >= 0)
                    return Test(c.Op, w.Quality(x, c.What), c.N);
                for (var id = 0; id < w.Count; id++)
                    if (Test(c.Op, w.Quality(id, c.What), c.N))
                        return true;
                return false;
        }
        var o = Value(c.B, w, b);
        if (o == -2)
            return false;
        if (x >= 0 && o >= 0)
            return Test(c.Op, w.Get(x, c.What, o), c.N);
        if (x >= 0)
        {
            var e = w[x];
            var (from, to) = w.Range(x, c.What);
            for (var i = from; i < to; i++)
                if (Test(c.Op, e.Pairs[i].Value, c.N))
                    return true;
            return false;
        }
        if (o >= 0)
        {
            var e = w[o];
            var (from, to) = w.HeldRange(o, c.What);
            for (var i = from; i < to; i++)
                if (Test(c.Op, w.Get((int)(uint)e.Held[i], c.What, o), c.N))
                    return true;
            return false;
        }
        for (var id = 0; id < w.Count; id++)
        {
            var e = w[id];
            var (from, to) = w.Range(id, c.What);
            for (var i = from; i < to; i++)
                if (Test(c.Op, e.Pairs[i].Value, c.N))
                    return true;
        }
        return false;
    }
}
