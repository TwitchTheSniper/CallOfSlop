using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace CallOfSlop;

/// <summary>
/// A field the map bake leaves out (MapBake.cs): state the game sets after a map is built (its id and pack, load times),
/// never data a map file builds. A baked map gets the value a freshly constructed object has.
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class NotBakedAttribute : Attribute;

/// <summary>Something in a built map the bake can't write (a delegate, a running task...): that map is built live instead.</summary>
public sealed class MapBakeException(string message) : Exception(message);

/// <summary>
/// The object graph of one built <see cref="Map"/>, written to bytes and read back (MapBake.cs). Generic over the map's
/// own types (every instance field, private ones included, of every object the map holds), so a new field is baked
/// without anyone having to remember it. What it does:
/// <list type="bullet">
/// <item>Value types without references, and arrays and lists of them, are copied as raw bytes (exact bits).</item>
/// <item>Objects keep their identity: an object held twice is one object after reading (shared lists, cycles).</item>
/// <item>Library objects (props, buildings, decals, flag designs and variants, models, the destruction table) are
/// written as a path from their library root and resolved to the very same instance on reading. Procedural meshes are
/// written as their shape and fetched from <see cref="MeshLibrary"/> (built there once if they aren't yet).</item>
/// <item>Dictionaries and sets are rebuilt from their entries in enumeration order, with the same comparer.</item>
/// <item>Anything it can't reproduce exactly (delegates, tasks, threads, reflection objects, unknown collections, other
/// maps) is refused with <see cref="MapBakeException"/>; that map is then simply built live.</item>
/// </list>
/// Bodies are written breadth first (an object's header where it is first met, its fields later), so a long chain of
/// objects never recurses deeply.
/// </summary>
internal static partial class MapGraph
{
    // ---- Tags -----------------------------------------------------------------------------------

    private const byte TNull = 0, TString = 1, TBack = 2, TLibrary = 3, TPlainObject = 4, TLock = 5, TNew = 6, TBoxed = 7, TPrimitive = 8;

    // ---- Codecs (one per type, built once, shared by every thread) --------------------------------

    private static readonly ConcurrentDictionary<Type, Codec> Codecs = new();

    internal static Codec CodecFor(Type t) => Codecs.GetOrAdd(t, Make);

    private static bool HasRefs(Type t) =>
        (bool)typeof(RuntimeHelpers).GetMethod(nameof(RuntimeHelpers.IsReferenceOrContainsReferences))!.MakeGenericMethod(t).Invoke(null, null)!;

    private static Codec Make(Type t)
    {
        if (t.IsPointer || t.IsByRef || t.IsByRefLike)
            return new Refused(t, "a pointer or ref struct");
        if (t.IsValueType)
        {
            if (Nullable.GetUnderlyingType(t) is { } under)
                return new NullableCodec(t, under);
            if (!HasRefs(t))
                return (Codec)Activator.CreateInstance(typeof(Unmanaged<>).MakeGenericType(t))!;
            return new StructCodec(t);
        }
        if (t == typeof(string))
            return new Refused(t, "(strings are written inline)");
        if (t.IsArray)
        {
            if (t.GetArrayRank() != 1 || t != t.GetElementType()!.MakeArrayType())
                return new Refused(t, "a multi-dimensional array");
            var e = t.GetElementType()!;
            return (Codec)Activator.CreateInstance((e.IsValueType && !HasRefs(e) ? typeof(UnmanagedArray<>) : typeof(ArrayCodec<>)).MakeGenericType(e))!;
        }
        if (t.IsGenericType)
        {
            var def = t.GetGenericTypeDefinition();
            var args = t.GetGenericArguments();
            if (def == typeof(List<>))
                return (Codec)Activator.CreateInstance((args[0].IsValueType && !HasRefs(args[0]) ? typeof(UnmanagedList<>) : typeof(ListCodec<>)).MakeGenericType(args))!;
            if (def == typeof(Dictionary<,>))
                return (Codec)Activator.CreateInstance(typeof(DictCodec<,>).MakeGenericType(args))!;
            if (def == typeof(HashSet<>))
                return (Codec)Activator.CreateInstance(typeof(SetCodec<>).MakeGenericType(args))!;
            if (def == typeof(SortedDictionary<,>))
                return (Codec)Activator.CreateInstance(typeof(SortedDictCodec<,>).MakeGenericType(args))!;
        }
        if (typeof(Delegate).IsAssignableFrom(t))
            return new Refused(t, "a delegate");
        if (t == typeof(MeshAsset))
            return new MeshAssetCodec();
        if (t == typeof(Map))
            return new ClassCodec(t); // the root only (another map is refused in WriteRef)
        var ns = t.Namespace ?? "";
        if (t.Assembly != typeof(Map).Assembly || ns.StartsWith("System", StringComparison.Ordinal))
            return new Refused(t, "a framework type the bake doesn't know");
        if (t == typeof(MapPack))
            return new Refused(t, "a map pack");
        return new ClassCodec(t);
    }

    internal abstract class Codec(Type type)
    {
        public readonly Type Type = type;

        /// <summary>Value types: the value itself (no tag).</summary>
        public virtual void WriteValue(Writer w, object? v) => throw new MapBakeException($"can't bake a {Type}");

        public virtual object? ReadValue(Reader r) => throw new FormatException(Type.Name);

        /// <summary>Reference types: what the reader needs to create the object (a length, a comparer), written where it is first met.</summary>
        public virtual void WriteHeader(Writer w, object v) => throw new MapBakeException($"can't bake a {Type}");

        public virtual object Create(Reader r) => throw new FormatException(Type.Name);

        /// <summary>Reference types: the contents, written later in queue order.</summary>
        public virtual void WriteBody(Writer w, object v)
        {
        }

        public virtual void ReadBody(Reader r, object o)
        {
        }

        /// <summary>The objects a library path step leads to (for the library index and resolving a path).</summary>
        public virtual object? Step(object o, int step) => null;

        public virtual IEnumerable<(int Step, object? Child)> Children(object o) => [];

        /// <summary>Compare two values or objects of this type (the self-test's baked = live check).</summary>
        public abstract void Compare(GraphComparer c, object a, object b, string path);
    }

    private sealed class Refused(Type t, string why) : Codec(t)
    {
        public override void WriteValue(Writer w, object? v) => throw new MapBakeException($"can't bake {Type} ({why})");

        public override void WriteHeader(Writer w, object v) => throw new MapBakeException($"can't bake {Type} ({why})");

        public override void Compare(GraphComparer c, object a, object b, string path) => c.Diff(path, $"{Type} can't be compared ({why})");
    }

    // Value types without references: raw bytes (padding included, so a baked value is bit for bit the one written; two
    // separate live builds can differ in padding bytes, which the comparer would see, but a bake is compared with its source).
    private sealed class Unmanaged<T>() : Codec(typeof(T)) where T : struct
    {
        public override void WriteValue(Writer w, object? v)
        {
            var x = (T)v!;
            w.Raw(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref x, 1)));
        }

        public override object? ReadValue(Reader r)
        {
            T x = default;
            r.Raw(MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref x, 1)));
            return x;
        }

        public override void Compare(GraphComparer c, object a, object b, string path)
        {
            var x = (T)a;
            var y = (T)b;
            if (!MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref x, 1)).SequenceEqual(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref y, 1))))
                c.Diff(path, $"{x} != {y}");
        }
    }

    private sealed class NullableCodec(Type t, Type under) : Codec(t)
    {
        public override void WriteValue(Writer w, object? v)
        {
            w.Byte(v == null ? (byte)0 : (byte)1);
            if (v != null)
                w.Slot(under, v);
        }

        public override object? ReadValue(Reader r) => r.Byte() == 0 ? null : r.Slot(under);

        public override object? Step(object o, int step) => o; // (a boxed nullable is its value)

        public override IEnumerable<(int Step, object? Child)> Children(object o) => [(0, o)];

        public override void Compare(GraphComparer c, object a, object b, string path) => c.Slot(under, a, b, path);
    }

    /// <summary>Every instance field of <paramref name="t"/> and its base classes that is baked, in a fixed order.</summary>
    private static FieldInfo[] BakedFields(Type t)
    {
        var list = new List<FieldInfo>();
        var chain = new List<Type>();
        for (var x = t; x != null && x != typeof(object) && x != typeof(ValueType); x = x.BaseType)
            chain.Insert(0, x);
        foreach (var x in chain)
            list.AddRange(x.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Where(f => f.GetCustomAttribute<NotBakedAttribute>() == null).OrderBy(f => f.MetadataToken));
        return list.ToArray();
    }

    private static bool HasNotBaked(Type t)
    {
        for (var x = t; x != null && x != typeof(object); x = x.BaseType)
            if (x.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly).Any(f => f.GetCustomAttribute<NotBakedAttribute>() != null))
                return true;
        return false;
    }

    private sealed class StructCodec(Type t) : Codec(t)
    {
        private readonly FieldInfo[] _fields = BakedFields(t);

        public override void WriteValue(Writer w, object? v)
        {
            foreach (var f in _fields)
                w.Field(f, f.GetValue(v));
        }

        public override object? ReadValue(Reader r)
        {
            var box = RuntimeHelpers.GetUninitializedObject(Type);
            foreach (var f in _fields)
                f.SetValue(box, r.Slot(f.FieldType));
            return box;
        }

        public override object? Step(object o, int step) => step < _fields.Length ? _fields[step].GetValue(o) : null;

        public override IEnumerable<(int Step, object? Child)> Children(object o)
        {
            for (var i = 0; i < _fields.Length; i++)
                yield return (i, _fields[i].GetValue(o));
        }

        public override void Compare(GraphComparer c, object a, object b, string path)
        {
            foreach (var f in _fields)
                c.Slot(f.FieldType, f.GetValue(a), f.GetValue(b), path + "." + f.Name);
        }
    }

    private sealed class ClassCodec(Type t) : Codec(t)
    {
        private readonly FieldInfo[] _fields = BakedFields(t);
        private readonly ConstructorInfo? _ctor = HasNotBaked(t) ? t.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, Type.EmptyTypes) : null;

        public override void WriteHeader(Writer w, object v)
        {
        }

        public override object Create(Reader r) => _ctor != null ? _ctor.Invoke(null) : RuntimeHelpers.GetUninitializedObject(Type);

        public override void WriteBody(Writer w, object v)
        {
            foreach (var f in _fields)
            {
                try
                {
                    w.Field(f, f.GetValue(v));
                }
                catch (MapBakeException ex)
                {
                    throw new MapBakeException($"{Type.Name}.{f.Name}: {ex.Message}");
                }
            }
        }

        private Action<Reader, object>? _read;

        public override void ReadBody(Reader r, object o) => (_read ??= EmitClass(Type))(r, o); // (MapBakeRead.cs)

        public override object? Step(object o, int step) => step < _fields.Length ? _fields[step].GetValue(o) : null;

        public override IEnumerable<(int Step, object? Child)> Children(object o)
        {
            for (var i = 0; i < _fields.Length; i++)
                yield return (i, _fields[i].GetValue(o));
        }

        public override void Compare(GraphComparer c, object a, object b, string path)
        {
            foreach (var f in _fields)
                c.Slot(f.FieldType, f.GetValue(a), f.GetValue(b), path + "." + f.Name);
        }
    }

    /// <summary>The bytes of an array of a type without references (checked by the caller).</summary>
    private static Span<byte> Bytes<T>(T[] a) =>
        a.Length == 0 ? [] : MemoryMarshal.CreateSpan(ref Unsafe.As<T, byte>(ref MemoryMarshal.GetArrayDataReference(a)), a.Length * Unsafe.SizeOf<T>());

    private static string FirstDifference<T>(ReadOnlySpan<T> x, ReadOnlySpan<T> y) where T : struct
    {
        for (var i = 0; i < x.Length; i++)
            if (!MemoryMarshal.AsBytes(x.Slice(i, 1)).SequenceEqual(MemoryMarshal.AsBytes(y.Slice(i, 1))))
                return $"element {i} of {x.Length} differs: {x[i]} != {y[i]}";
        return "elements differ";
    }

    private sealed class UnmanagedArray<T>() : Codec(typeof(T[])) where T : struct
    {
        public override void WriteHeader(Writer w, object v) => w.Count(((T[])v).Length);

        public override object Create(Reader r)
        {
            var n = r.Count(Unsafe.SizeOf<T>());
            return n == 0 ? Array.Empty<T>() : new T[n];
        }

        public override void WriteBody(Writer w, object v) => w.Raw(MemoryMarshal.AsBytes(((T[])v).AsSpan()));

        public override void ReadBody(Reader r, object o) => r.Raw(MemoryMarshal.AsBytes(((T[])o).AsSpan()));

        public override void Compare(GraphComparer c, object a, object b, string path)
        {
            var x = (T[])a;
            var y = (T[])b;
            if (x.Length != y.Length)
                c.Diff(path, $"{x.Length} elements != {y.Length}");
            else if (!MemoryMarshal.AsBytes(x.AsSpan()).SequenceEqual(MemoryMarshal.AsBytes(y.AsSpan())))
                c.Diff(path, FirstDifference<T>(x, y));
        }
    }

    private sealed class ArrayCodec<T>() : Codec(typeof(T[]))
    {
        public override void WriteHeader(Writer w, object v) => w.Count(((T[])v).Length);

        public override object Create(Reader r)
        {
            var n = r.Count(1);
            return n == 0 ? Array.Empty<T>() : new T[n];
        }

        public override void WriteBody(Writer w, object v)
        {
            var a = (T[])v;
            for (var i = 0; i < a.Length; i++)
                w.Slot(typeof(T), a[i]);
        }

        public override void ReadBody(Reader r, object o)
        {
            var a = (T[])o;
            for (var i = 0; i < a.Length; i++)
                a[i] = FieldReader<T>.Read(r);
        }

        public override object? Step(object o, int step) => step < ((T[])o).Length ? ((T[])o)[step] : null;

        public override IEnumerable<(int Step, object? Child)> Children(object o)
        {
            var a = (T[])o;
            for (var i = 0; i < a.Length; i++)
                yield return (i, a[i]);
        }

        public override void Compare(GraphComparer c, object a, object b, string path)
        {
            var x = (T[])a;
            var y = (T[])b;
            if (x.Length != y.Length)
            {
                c.Diff(path, $"{x.Length} elements != {y.Length}");
                return;
            }
            for (var i = 0; i < x.Length && c.Room; i++)
                c.Slot(typeof(T), x[i], y[i], $"{path}[{i}]");
        }
    }

    private sealed class UnmanagedList<T>() : Codec(typeof(List<T>)) where T : struct
    {
        public override void WriteHeader(Writer w, object v) => w.Count(((List<T>)v).Count);

        public override object Create(Reader r)
        {
            var n = r.Count(Unsafe.SizeOf<T>());
            var list = new List<T>(n);
            CollectionsMarshal.SetCount(list, n);
            return list;
        }

        public override void WriteBody(Writer w, object v) => w.Raw(MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan((List<T>)v)));

        public override void ReadBody(Reader r, object o) => r.Raw(MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan((List<T>)o)));

        public override void Compare(GraphComparer c, object a, object b, string path)
        {
            var x = CollectionsMarshal.AsSpan((List<T>)a);
            var y = CollectionsMarshal.AsSpan((List<T>)b);
            if (x.Length != y.Length)
                c.Diff(path, $"{x.Length} elements != {y.Length}");
            else if (!MemoryMarshal.AsBytes(x).SequenceEqual(MemoryMarshal.AsBytes(y)))
                c.Diff(path, FirstDifference<T>(x, y));
        }
    }

    private sealed class ListCodec<T>() : Codec(typeof(List<T>))
    {
        public override void WriteHeader(Writer w, object v) => w.Count(((List<T>)v).Count);

        public override object Create(Reader r)
        {
            var n = r.Count(1);
            var list = new List<T>(n);
            CollectionsMarshal.SetCount(list, n);
            return list;
        }

        public override void WriteBody(Writer w, object v)
        {
            foreach (var e in (List<T>)v)
                w.Slot(typeof(T), e);
        }

        public override void ReadBody(Reader r, object o)
        {
            var s = CollectionsMarshal.AsSpan((List<T>)o);
            for (var i = 0; i < s.Length; i++)
                s[i] = FieldReader<T>.Read(r);
        }

        public override object? Step(object o, int step) => step < ((List<T>)o).Count ? ((List<T>)o)[step] : null;

        public override IEnumerable<(int Step, object? Child)> Children(object o)
        {
            var l = (List<T>)o;
            for (var i = 0; i < l.Count; i++)
                yield return (i, l[i]);
        }

        public override void Compare(GraphComparer c, object a, object b, string path)
        {
            var x = (List<T>)a;
            var y = (List<T>)b;
            if (x.Count != y.Count)
            {
                c.Diff(path, $"{x.Count} elements != {y.Count}");
                return;
            }
            for (var i = 0; i < x.Count && c.Room; i++)
                c.Slot(typeof(T), x[i], y[i], $"{path}[{i}]");
        }
    }

    // The comparers a dictionary or set may use (anything else is refused).
    private static byte EqualityId<T>(IEqualityComparer<T> c)
    {
        if (ReferenceEquals(c, EqualityComparer<T>.Default))
            return 0;
        if (ReferenceEquals(c, StringComparer.Ordinal))
            return 1;
        if (ReferenceEquals(c, StringComparer.OrdinalIgnoreCase))
            return 2;
        if (ReferenceEquals(c, ReferenceEqualityComparer.Instance))
            return 3;
        if (ReferenceEquals(c, StringComparer.InvariantCultureIgnoreCase))
            return 4;
        throw new MapBakeException($"a comparer the bake doesn't know ({c.GetType().Name})");
    }

    private static IEqualityComparer<T> EqualityFor<T>(byte id) => id switch
    {
        0 => EqualityComparer<T>.Default,
        1 => (IEqualityComparer<T>)StringComparer.Ordinal,
        2 => (IEqualityComparer<T>)StringComparer.OrdinalIgnoreCase,
        3 => (IEqualityComparer<T>)(object)ReferenceEqualityComparer.Instance,
        4 => (IEqualityComparer<T>)StringComparer.InvariantCultureIgnoreCase,
        _ => throw new FormatException("comparer"),
    };

    private static byte OrderId<T>(IComparer<T> c)
    {
        if (ReferenceEquals(c, Comparer<T>.Default))
            return 0;
        if (ReferenceEquals(c, StringComparer.Ordinal))
            return 1;
        if (ReferenceEquals(c, StringComparer.OrdinalIgnoreCase))
            return 2;
        if (ReferenceEquals(c, StringComparer.InvariantCultureIgnoreCase))
            return 4;
        throw new MapBakeException($"a comparer the bake doesn't know ({c.GetType().Name})");
    }

    private static IComparer<T> OrderFor<T>(byte id) => id switch
    {
        0 => Comparer<T>.Default,
        1 => (IComparer<T>)StringComparer.Ordinal,
        2 => (IComparer<T>)StringComparer.OrdinalIgnoreCase,
        4 => (IComparer<T>)StringComparer.InvariantCultureIgnoreCase,
        _ => throw new FormatException("comparer"),
    };

    // Dictionaries and sets are filled after every object is read (their keys may hash on fields read later).
    private sealed class DictCodec<TK, TV>() : Codec(typeof(Dictionary<TK, TV>)) where TK : notnull
    {
        public override void WriteHeader(Writer w, object v)
        {
            var d = (Dictionary<TK, TV>)v;
            w.Byte(EqualityId(d.Comparer));
            w.Count(d.Count);
        }

        public override object Create(Reader r)
        {
            var c = EqualityFor<TK>(r.Byte());
            return new Dictionary<TK, TV>(r.Count(1), c);
        }

        private static readonly bool Raw = !RuntimeHelpers.IsReferenceOrContainsReferences<TK>() && !RuntimeHelpers.IsReferenceOrContainsReferences<TV>();

        public override void WriteBody(Writer w, object v)
        {
            var d = (Dictionary<TK, TV>)v;
            w.Count(d.Count);
            if (Raw) // (keys then values as raw bytes)
            {
                var keys = new TK[d.Count];
                var values = new TV[d.Count];
                d.Keys.CopyTo(keys, 0);
                d.Values.CopyTo(values, 0);
                w.Raw(Bytes(keys));
                w.Raw(Bytes(values));
                return;
            }
            foreach (var (k, x) in d)
            {
                w.Slot(typeof(TK), k);
                w.Slot(typeof(TV), x);
            }
        }

        public override void ReadBody(Reader r, object o)
        {
            var n = r.Count(1);
            var keys = new TK[n];
            var values = new TV[n];
            if (Raw)
            {
                r.Raw(Bytes(keys));
                r.Raw(Bytes(values));
            }
            else
                for (var i = 0; i < n; i++)
                {
                    keys[i] = FieldReader<TK>.Read(r);
                    values[i] = FieldReader<TV>.Read(r);
                }
            var d = (Dictionary<TK, TV>)o;
            r.Later(() =>
            {
                for (var i = 0; i < n; i++)
                    d.Add(keys[i], values[i]);
            });
        }

        public override object? Step(object o, int step)
        {
            var e = ((Dictionary<TK, TV>)o).ElementAtOrDefault(step / 2);
            return step / 2 >= ((Dictionary<TK, TV>)o).Count ? null : step % 2 == 0 ? e.Key : e.Value;
        }

        public override IEnumerable<(int Step, object? Child)> Children(object o)
        {
            var i = 0;
            foreach (var (k, v) in (Dictionary<TK, TV>)o)
            {
                yield return (i * 2, k);
                yield return (i * 2 + 1, v);
                i++;
            }
        }

        public override void Compare(GraphComparer c, object a, object b, string path)
        {
            var x = (Dictionary<TK, TV>)a;
            var y = (Dictionary<TK, TV>)b;
            if (!ReferenceEquals(x.Comparer, y.Comparer))
                c.Diff(path, "comparers differ");
            if (x.Count != y.Count)
            {
                c.Diff(path, $"{x.Count} entries != {y.Count}");
                return;
            }
            var i = 0;
            foreach (var ((k1, v1), (k2, v2)) in x.Zip(y))
            {
                if (!c.Room)
                    return;
                c.Slot(typeof(TK), k1, k2, $"{path}.key[{i}]");
                c.Slot(typeof(TV), v1, v2, $"{path}[{k1}]");
                i++;
            }
        }
    }

    private sealed class SetCodec<T>() : Codec(typeof(HashSet<T>))
    {
        public override void WriteHeader(Writer w, object v)
        {
            var s = (HashSet<T>)v;
            w.Byte(EqualityId(s.Comparer));
            w.Count(s.Count);
        }

        public override object Create(Reader r)
        {
            var c = EqualityFor<T>(r.Byte());
            return new HashSet<T>(r.Count(1), c);
        }

        private static readonly bool Raw = !RuntimeHelpers.IsReferenceOrContainsReferences<T>();

        public override void WriteBody(Writer w, object v)
        {
            var s = (HashSet<T>)v;
            w.Count(s.Count);
            if (Raw)
            {
                var items = new T[s.Count];
                s.CopyTo(items);
                w.Raw(Bytes(items));
                return;
            }
            foreach (var e in s)
                w.Slot(typeof(T), e);
        }

        public override void ReadBody(Reader r, object o)
        {
            var n = r.Count(1);
            var items = new T[n];
            if (Raw)
                r.Raw(Bytes(items));
            else
                for (var i = 0; i < n; i++)
                    items[i] = FieldReader<T>.Read(r);
            var s = (HashSet<T>)o;
            r.Later(() =>
            {
                foreach (var e in items)
                    s.Add(e);
            });
        }

        public override object? Step(object o, int step) => step < ((HashSet<T>)o).Count ? ((HashSet<T>)o).ElementAt(step) : null;

        public override IEnumerable<(int Step, object? Child)> Children(object o)
        {
            var i = 0;
            foreach (var e in (HashSet<T>)o)
                yield return (i++, e);
        }

        public override void Compare(GraphComparer c, object a, object b, string path)
        {
            var x = (HashSet<T>)a;
            var y = (HashSet<T>)b;
            if (!ReferenceEquals(x.Comparer, y.Comparer))
                c.Diff(path, "comparers differ");
            if (x.Count != y.Count)
            {
                c.Diff(path, $"{x.Count} elements != {y.Count}");
                return;
            }
            var i = 0;
            foreach (var (e1, e2) in x.Zip(y))
            {
                if (!c.Room)
                    return;
                c.Slot(typeof(T), e1, e2, $"{path}{{{i++}}}");
            }
        }
    }

    private sealed class SortedDictCodec<TK, TV>() : Codec(typeof(SortedDictionary<TK, TV>)) where TK : notnull
    {
        public override void WriteHeader(Writer w, object v) => w.Byte(OrderId(((SortedDictionary<TK, TV>)v).Comparer));

        public override object Create(Reader r) => new SortedDictionary<TK, TV>(OrderFor<TK>(r.Byte()));

        public override void WriteBody(Writer w, object v)
        {
            var d = (SortedDictionary<TK, TV>)v;
            w.Count(d.Count);
            foreach (var (k, x) in d)
            {
                w.Slot(typeof(TK), k);
                w.Slot(typeof(TV), x);
            }
        }

        public override void ReadBody(Reader r, object o)
        {
            var n = r.Count(1);
            var keys = new TK[n];
            var values = new TV[n];
            for (var i = 0; i < n; i++)
            {
                keys[i] = FieldReader<TK>.Read(r);
                values[i] = FieldReader<TV>.Read(r);
            }
            var d = (SortedDictionary<TK, TV>)o;
            r.Later(() =>
            {
                for (var i = 0; i < n; i++)
                    d.Add(keys[i], values[i]);
            });
        }

        public override object? Step(object o, int step)
        {
            var d = (SortedDictionary<TK, TV>)o;
            if (step / 2 >= d.Count)
                return null;
            var e = d.ElementAt(step / 2);
            return step % 2 == 0 ? e.Key : e.Value;
        }

        public override IEnumerable<(int Step, object? Child)> Children(object o)
        {
            var i = 0;
            foreach (var (k, v) in (SortedDictionary<TK, TV>)o)
            {
                yield return (i * 2, k);
                yield return (i * 2 + 1, v);
                i++;
            }
        }

        public override void Compare(GraphComparer c, object a, object b, string path)
        {
            var x = (SortedDictionary<TK, TV>)a;
            var y = (SortedDictionary<TK, TV>)b;
            if (!ReferenceEquals(x.Comparer, y.Comparer))
                c.Diff(path, "comparers differ");
            if (x.Count != y.Count)
            {
                c.Diff(path, $"{x.Count} entries != {y.Count}");
                return;
            }
            foreach (var ((k1, v1), (k2, v2)) in x.Zip(y))
            {
                if (!c.Room)
                    return;
                c.Slot(typeof(TK), k1, k2, $"{path}.key");
                c.Slot(typeof(TV), v1, v2, $"{path}[{k1}]");
            }
        }
    }

    /// <summary>
    /// A mesh: a procedural one (cylinder, sphere...) is written as its shape, and on reading <see cref="MeshLibrary"/>
    /// gives the asset for that shape (building it once if it hasn't yet), so every map shares one asset per shape, as
    /// when built live. A map's own mesh (its boundary floor) is written whole and read back as a new asset, made with
    /// the constructor so it gets its own id. Model meshes are library objects (their model's path) and never get here.
    /// </summary>
    private sealed class MeshAssetCodec() : Codec(typeof(MeshAsset))
    {
        public override void WriteHeader(Writer w, object v)
        {
            var m = (MeshAsset)v;
            if (MeshLibrary.IsPrimitive(m) && m.Spec is { } spec)
            {
                w.Byte(1); // a library primitive: its shape is enough
                CodecFor(typeof(PrimitiveSpec)).WriteValue(w, spec);
                return;
            }
            w.Byte(0);
            w.String(m.Key);
            w.Byte(m.DoubleSided ? (byte)1 : (byte)0);
            w.String(m.LodSource);
            w.Inline(m.Lods);
        }

        public override object Create(Reader r)
        {
            if (r.Byte() != 0)
                return MeshLibrary.Primitive((PrimitiveSpec)CodecFor(typeof(PrimitiveSpec)).ReadValue(r)!); // (the same asset every map gets; built once if it isn't yet)
            var key = r.String() ?? throw new FormatException("mesh key");
            var doubleSided = r.Byte() != 0;
            var lodSource = r.String() ?? throw new FormatException("mesh LODs");
            var lods = r.Inline() as MeshData[] ?? throw new FormatException("mesh LODs");
            return new MeshAsset(key, lods, doubleSided, lodSource);
        }

        public override void Compare(GraphComparer c, object a, object b, string path)
        {
            var x = (MeshAsset)a;
            var y = (MeshAsset)b;
            if (MeshLibrary.IsPrimitive(x) || MeshLibrary.IsPrimitive(y))
            {
                if (!ReferenceEquals(x, y))
                    c.Diff(path, "not the library's mesh");
                return;
            }
            if (x.Key != y.Key || x.DoubleSided != y.DoubleSided || x.LodSource != y.LodSource || x.Spec != y.Spec)
                c.Diff(path, $"mesh {x.Key} != {y.Key}");
            c.Slot(typeof(MeshData[]), x.Lods, y.Lods, path + ".Lods");
        }
    }

    // ---- Writing -------------------------------------------------------------------------------

    internal sealed class Writer(MapLibraryIndex library, Map root)
    {
        private static readonly string GameFolder = Path.TrimEndingDirectorySeparator(AppFolders.Data);
        private byte[] _buf = new byte[1 << 16];
        private int _pos;
        private readonly Dictionary<object, int> _ids = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<string, int> _strings = new(StringComparer.Ordinal);
        private readonly List<string> _stringList = new();
        private readonly Dictionary<Type, int> _types = new();
        private readonly List<Type> _typeList = new();
        private readonly Queue<(Codec Codec, object Obj, string Label)> _queue = new();
        private string _label = "";
        /// <summary>Bytes written per field of the map (what each part of a map costs on disk; the bake log).</summary>
        public readonly Dictionary<string, long> Sizes = new();
        private int _inline;

        public byte[] Run()
        {
            _ids[root] = 0;
            CodecFor(typeof(Map)).WriteBody(this, root);
            _inRoot = false;
            while (_queue.TryDequeue(out var job))
            {
                _label = job.Label;
                var p0 = _pos;
                job.Codec.WriteBody(this, job.Obj);
                Sizes[_label] = Sizes.GetValueOrDefault(_label) + _pos - p0;
            }
            // Tables first: types, strings, then the body.
            var body = _buf.AsSpan(0, _pos).ToArray();
            _pos = 0;
            Count(_typeList.Count);
            foreach (var t in _typeList)
                RawString(t.AssemblyQualifiedName!);
            Count(_stringList.Count);
            foreach (var s in _stringList)
                RawString(s);
            Count(_ids.Count);
            Raw(body);
            return _buf.AsSpan(0, _pos).ToArray();
        }

        private void Ensure(int n)
        {
            if (_pos + n > _buf.Length)
                Array.Resize(ref _buf, Math.Max(_buf.Length * 2, _pos + n));
        }

        public void Byte(byte b)
        {
            Ensure(1);
            _buf[_pos++] = b;
        }

        public void Raw(ReadOnlySpan<byte> s)
        {
            Ensure(s.Length);
            s.CopyTo(_buf.AsSpan(_pos));
            _pos += s.Length;
        }

        public void Count(int n)
        {
            var v = (uint)n;
            while (v >= 0x80)
            {
                Byte((byte)(v | 0x80));
                v >>= 7;
            }
            Byte((byte)v);
        }

        private void RawString(string s)
        {
            Count(s.Length);
            Raw(MemoryMarshal.AsBytes(s.AsSpan()));
        }

        public void String(string? s)
        {
            if (s == null)
            {
                Count(0);
                return;
            }
            if (!_strings.TryGetValue(s, out var id))
            {
                if (s.Length >= GameFolder.Length && s.Contains(GameFolder, StringComparison.OrdinalIgnoreCase))
                    throw new MapBakeException($"it holds a path on this computer (\"{s}\")"); // (the bake must be the same on every computer)
                id = _stringList.Count;
                _strings[s] = id;
                _stringList.Add(s);
            }
            Count(id + 1);
        }

        private int TypeId(Type t)
        {
            if (!_types.TryGetValue(t, out var id))
            {
                id = _typeList.Count;
                _types[t] = id;
                _typeList.Add(t);
            }
            return id;
        }

        public void Field(FieldInfo f, object? v)
        {
            if (!_inRoot || f.DeclaringType != typeof(Map))
            {
                Slot(f.FieldType, v);
                return;
            }
            _label = f.Name; // (a field of the map itself: what it holds is counted under its name)
            var p0 = _pos;
            Slot(f.FieldType, v);
            Sizes[_label] = Sizes.GetValueOrDefault(_label) + _pos - p0;
        }

        private bool _inRoot = true;

        /// <summary>A value of declared type <paramref name="declared"/>: value types as themselves, everything else by reference.</summary>
        public void Slot(Type declared, object? v)
        {
            if (declared.IsValueType)
                CodecFor(declared).WriteValue(this, v);
            else
                Ref(v);
        }

        /// <summary>A reference: null, a string, an object already written, a library object, or a new object (header now, body later).</summary>
        public void Ref(object? v)
        {
            switch (v)
            {
                case null:
                    Byte(TNull);
                    return;
                case string s:
                    Byte(TString);
                    String(s);
                    return;
            }
            if (_ids.TryGetValue(v, out var id))
            {
                Byte(TBack);
                Count(id);
                return;
            }
            var t = v.GetType();
            if (t.IsValueType) // a boxed value in an object slot (op arguments)
            {
                Byte(TBoxed);
                Count(TypeId(t));
                CodecFor(t).WriteValue(this, v);
                return;
            }
            if (library.Find(v) is { } at)
            {
                Byte(TLibrary);
                String(at.Root);
                Count(at.Path.Length);
                foreach (var step in at.Path)
                    Count(step);
                return;
            }
            if (t == typeof(object))
            {
                Byte(TPlainObject); // a lock object: a new one is as good
                _ids[v] = _ids.Count;
                return;
            }
            if (t == typeof(Lock))
            {
                Byte(TLock);
                _ids[v] = _ids.Count;
                return;
            }
            if (v is Map)
                throw new MapBakeException("another map");
            if (v is MeshAsset)
            {
                Byte(TPrimitive);
                _ids[v] = _ids.Count;
                _inline++;
                try
                {
                    CodecFor(t).WriteHeader(this, v);
                }
                finally
                {
                    _inline--;
                }
                return;
            }
            var codec = CodecFor(t);
            Byte(TNew);
            Count(TypeId(t));
            _ids[v] = _ids.Count;
            codec.WriteHeader(this, v);
            if (_inline > 0)
                codec.WriteBody(this, v);
            else
                _queue.Enqueue((codec, v, _label));
        }

        /// <summary>An object and everything it holds written here and now (a procedural mesh's LODs).</summary>
        public void Inline(object? v)
        {
            _inline++;
            try
            {
                Ref(v);
            }
            finally
            {
                _inline--;
            }
        }
    }

    // ---- Reading -------------------------------------------------------------------------------

    internal sealed partial class Reader
    {
        private readonly byte[] _buf;
        private int _pos;
        private readonly int _end;
        private readonly Type[] _types;
        private readonly string[] _strings;
        private readonly object?[] _objs;
        private int _objCount;
        private readonly Queue<(Codec Codec, object Obj)> _queue = new();
        private readonly List<Action> _later = new();
        private readonly MapLibraryResolver _library;
        private int _inline;

        private static readonly ConcurrentDictionary<string, Type?> TypeNames = new(StringComparer.Ordinal);

        public Reader(byte[] buf, int start, int length, MapLibraryResolver library)
        {
            _buf = buf;
            _pos = start;
            _end = start + length;
            _library = library;
            _types = new Type[Count(1)];
            for (var i = 0; i < _types.Length; i++)
            {
                var name = RawString();
                _types[i] = TypeNames.GetOrAdd(name, n => Type.GetType(n, throwOnError: false)) ?? throw new FormatException($"unknown type {name}");
            }
            _strings = new string[Count(1)];
            for (var i = 0; i < _strings.Length; i++)
                _strings[i] = RawString();
            _objs = new object?[Count(1)];
        }

        public Map Run()
        {
            var map = (Map)CodecFor(typeof(Map)).Create(this); // new Map(): its unbaked fields get their initial values
            _objs[_objCount++] = map;
            CodecFor(typeof(Map)).ReadBody(this, map);
            while (_queue.TryDequeue(out var job))
                job.Codec.ReadBody(this, job.Obj);
            foreach (var a in _later)
                a();
            if (_pos != _end)
                throw new FormatException("trailing bytes");
            return map;
        }

        public void Later(Action a) => _later.Add(a);

        public byte Byte()
        {
            if (_pos >= _end)
                throw new EndOfStreamException();
            return _buf[_pos++];
        }

        public void Raw(Span<byte> dst)
        {
            if (dst.Length > _end - _pos)
                throw new EndOfStreamException();
            _buf.AsSpan(_pos, dst.Length).CopyTo(dst);
            _pos += dst.Length;
        }

        /// <summary>A count, checked against what is left in the file (at least <paramref name="minBytes"/> bytes per item).</summary>
        public int Count(int minBytes)
        {
            uint v = 0;
            var shift = 0;
            byte b;
            do
            {
                if (shift > 28)
                    throw new FormatException("bad count");
                b = Byte();
                v |= (uint)(b & 0x7F) << shift;
                shift += 7;
            }
            while ((b & 0x80) != 0);
            if (v > int.MaxValue || (long)v * Math.Max(minBytes, 0) > _end - _pos)
                throw new FormatException("bad count");
            return (int)v;
        }

        private string RawString()
        {
            var n = Count(2);
            var s = new string('\0', n);
            Raw(MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref MemoryMarshal.GetReference(s.AsSpan()), n)));
            return s;
        }

        public string? String()
        {
            var id = Count(0);
            return id == 0 ? null : (uint)(id - 1) < (uint)_strings.Length ? _strings[id - 1] : throw new FormatException("bad string");
        }

        public object? Slot(Type declared) => declared.IsValueType ? CodecFor(declared).ReadValue(this) : Ref();

        private int Reserve()
        {
            if (_objCount >= _objs.Length)
                throw new FormatException("too many objects");
            return _objCount++;
        }

        public object? Ref()
        {
            switch (Byte())
            {
                case TNull:
                    return null;
                case TString:
                    return String() ?? throw new FormatException("null string");
                case TBack:
                {
                    var id = Count(0);
                    return id < _objCount && _objs[id] is { } o ? o : throw new FormatException("bad back reference");
                }
                case TBoxed:
                    return CodecFor(TypeAt(Count(0))).ReadValue(this);
                case TLibrary:
                {
                    var root = String() ?? throw new FormatException("library root");
                    var path = new int[Count(1)];
                    for (var i = 0; i < path.Length; i++)
                        path[i] = Count(0);
                    return _library.Resolve(root, path);
                }
                case TPlainObject:
                    return _objs[Reserve()] = new object();
                case TLock:
                    return _objs[Reserve()] = new Lock();
                case TPrimitive:
                {
                    var slot = Reserve();
                    _inline++;
                    try
                    {
                        return _objs[slot] = CodecFor(typeof(MeshAsset)).Create(this);
                    }
                    finally
                    {
                        _inline--;
                    }
                }
                case TNew:
                {
                    var t = TypeAt(Count(0));
                    var codec = CodecFor(t);
                    var slot = Reserve();
                    var o = codec.Create(this);
                    _objs[slot] = o;
                    if (_inline > 0)
                        codec.ReadBody(this, o);
                    else
                        _queue.Enqueue((codec, o));
                    return o;
                }
                default:
                    throw new FormatException("bad tag");
            }
        }

        public object? Inline()
        {
            _inline++;
            try
            {
                return Ref();
            }
            finally
            {
                _inline--;
            }
        }

        private Type TypeAt(int i) => (uint)i < (uint)_types.Length ? _types[i] : throw new FormatException("bad type");
    }

    // ---- Comparing (self-test: a baked map is the live one) -------------------------------------

    /// <summary>
    /// Walks two graphs side by side: same types, same bits, same shape of sharing (an object held twice in one is one
    /// object held twice in the other), library objects the very same instances. Unbaked fields are skipped.
    /// </summary>
    internal sealed class GraphComparer(MapLibraryIndex library)
    {
        private readonly Dictionary<object, object> _ab = new(ReferenceEqualityComparer.Instance), _ba = new(ReferenceEqualityComparer.Instance);
        private readonly Queue<(Codec Codec, object A, object B, string Path)> _queue = new();
        public readonly List<string> Diffs = new();
        public int Objects;

        public bool Room => Diffs.Count < 20;

        public void Diff(string path, string what)
        {
            if (Room)
                Diffs.Add($"{path}: {what}");
        }

        public void Run(Map a, Map b)
        {
            _ab[a] = b;
            _ba[b] = a;
            CodecFor(typeof(Map)).Compare(this, a, b, "Map");
            while (Room && _queue.TryDequeue(out var job))
                job.Codec.Compare(this, job.A, job.B, job.Path);
        }

        public void Slot(Type declared, object? a, object? b, string path)
        {
            if (declared.IsValueType && Nullable.GetUnderlyingType(declared) == null)
            {
                CodecFor(declared).Compare(this, a!, b!, path);
                return;
            }
            Ref(a, b, path);
        }

        private void Ref(object? a, object? b, string path)
        {
            if (a == null || b == null)
            {
                if (a != b)
                    Diff(path, a == null ? "null in the live map" : "null in the baked map");
                return;
            }
            if (a.GetType() != b.GetType())
            {
                Diff(path, $"{a.GetType().Name} != {b.GetType().Name}");
                return;
            }
            if (a is string sa)
            {
                if (sa != (string)b)
                    Diff(path, $"\"{sa}\" != \"{b}\"");
                return;
            }
            var t = a.GetType();
            if (t.IsValueType)
            {
                CodecFor(t).Compare(this, a, b, path);
                return;
            }
            if (library.Find(a) != null)
            {
                if (!ReferenceEquals(a, b))
                    Diff(path, "not the same library object");
                return;
            }
            if (a is Array { Length: 0 })
                return; // (empty arrays: identity doesn't matter)
            if (_ab.TryGetValue(a, out var seen))
            {
                if (!ReferenceEquals(seen, b))
                    Diff(path, "sharing differs (an object held twice in the live map is two objects in the baked one)");
                return;
            }
            if (_ba.ContainsKey(b))
            {
                Diff(path, "sharing differs (two objects in the live map are one in the baked one)");
                return;
            }
            _ab[a] = b;
            _ba[b] = a;
            Objects++;
            if (t == typeof(object) || t == typeof(Lock))
                return;
            if (a is MeshAsset)
            {
                CodecFor(t).Compare(this, a, b, path); // (a mesh's LODs are walked here and now, as they are written)
                return;
            }
            _queue.Enqueue((CodecFor(t), a, b, path));
        }
    }

    // ---- Library objects ---------------------------------------------------------------------------

    /// <summary>Library objects by path from a root (writer side; also the comparer's "is this a library object").</summary>
    internal sealed class MapLibraryIndex
    {
        private readonly Dictionary<object, (string Root, int[] Path)> _at = new(ReferenceEqualityComparer.Instance);

        public int Count => _at.Count;

        public (string Root, int[] Path)? Find(object o) => _at.TryGetValue(o, out var at) ? at : null;

        /// <summary>Indexes the roots and (with <paramref name="deep"/>) every object reachable from them, each under its first path.</summary>
        public MapLibraryIndex(IEnumerable<(string Key, object Root)> roots, bool deep) => Add(roots, deep);

        /// <summary>More roots (breadth first, in order); objects already indexed keep their path.</summary>
        public void Add(IEnumerable<(string Key, object Root)> roots, bool deep)
        {
            var list = roots.ToList();
            var fresh = new List<(string, object)>();
            foreach (var (key, root) in list)
                if (_at.TryAdd(root, (key, [])))
                    fresh.Add((key, root));
            if (!deep)
                return;
            var queue = new Queue<(object Obj, string Root, int[] Path)>();
            foreach (var (key, root) in fresh)
                queue.Enqueue((root, key, []));
            while (queue.TryDequeue(out var job))
            {
                var t = job.Obj.GetType();
                if (job.Obj is string || IsLeaf(t))
                    continue;
                foreach (var (step, child) in CodecFor(t).Children(job.Obj))
                {
                    if (child == null || child is string)
                        continue;
                    var ct = child.GetType();
                    var path = new int[job.Path.Length + 1];
                    job.Path.CopyTo(path, 0);
                    path[^1] = step;
                    if (ct.IsValueType)
                    {
                        if (!IsLeaf(ct))
                            queue.Enqueue((child, job.Root, path)); // (a struct is walked through, never indexed itself)
                        continue;
                    }
                    if (child is Array { Length: 0 } || !_at.TryAdd(child, (job.Root, path)))
                        continue;
                    queue.Enqueue((child, job.Root, path));
                }
            }
        }

        private static readonly ConcurrentDictionary<Type, bool> Leaves = new();

        /// <summary>Types with nothing to walk into: unmanaged values and arrays and lists of them, and types the bake refuses.</summary>
        private static bool IsLeaf(Type t) => Leaves.GetOrAdd(t, x => x.IsPrimitive || x.IsEnum || x == typeof(string) || CodecFor(x) is Refused or MeshAssetCodec
            || x.IsValueType && !HasRefs(x) || x.IsArray && x.GetElementType()!.IsValueType && !HasRefs(x.GetElementType()!)
            || x.IsGenericType && x.GetGenericTypeDefinition() == typeof(List<>) && x.GetGenericArguments()[0].IsValueType && !HasRefs(x.GetGenericArguments()[0]));
    }

    /// <summary>Library roots by key, then a path into them (reader side). Resolved paths are remembered per file.</summary>
    internal sealed class MapLibraryResolver(Func<string, object?> roots)
    {
        private readonly Dictionary<string, object?> _roots = new(StringComparer.Ordinal);

        public object Resolve(string root, int[] path)
        {
            if (!_roots.TryGetValue(root, out var o))
                _roots[root] = o = roots(root);
            if (o == null)
                throw new FormatException($"library object {root} not found");
            foreach (var step in path)
            {
                o = CodecFor(o.GetType()).Step(o, step);
                if (o == null)
                    throw new FormatException($"library path into {root} not found");
            }
            return o;
        }
    }
}
