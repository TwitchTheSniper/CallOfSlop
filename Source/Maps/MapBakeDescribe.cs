using System.Reflection;
using System.Runtime.CompilerServices;

namespace CallOfSlop;

// TEMPORARY diagnostic: what object graph does a built map hold? (removed before finishing)
public static class MapBakeDescribe
{
    public static int Run()
    {
        var counts = new Dictionary<Type, int>();
        var firstPath = new Dictionary<Type, string>();
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var q = new Queue<(object o, string path)>();
        foreach (var m in Maps.All)
            q.Enqueue((m, "Map"));
        static IEnumerable<FieldInfo> Fields(Type t)
        {
            for (var x = t; x != null && x != typeof(object); x = x.BaseType)
                foreach (var f in x.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    yield return f;
        }
        void Visit(object? v, string path)
        {
            if (v == null) return;
            var t = v.GetType();
            if (t.IsPrimitive || t.IsEnum || v is string) return;
            if (t.IsValueType)
            {
                if (!RuntimeHelpers.IsReferenceOrContainsReferences<byte>() && false) return;
                if (!(bool)typeof(RuntimeHelpers).GetMethod("IsReferenceOrContainsReferences")!.MakeGenericMethod(t).Invoke(null, null)!)
                {
                    counts[t] = counts.GetValueOrDefault(t) + 1;
                    firstPath.TryAdd(t, path);
                    return;
                }
                counts[t] = counts.GetValueOrDefault(t) + 1;
                firstPath.TryAdd(t, path);
                foreach (var f in Fields(t))
                    Visit(f.GetValue(v), path + "." + f.Name);
                return;
            }
            if (!seen.Add(v)) return;
            q.Enqueue((v, path));
        }
        while (q.Count > 0)
        {
            var (o, path) = q.Dequeue();
            var t = o.GetType();
            counts[t] = counts.GetValueOrDefault(t) + 1;
            firstPath.TryAdd(t, path);
            if (o is Array arr)
            {
                var et = t.GetElementType()!;
                if (et.IsPrimitive || et.IsEnum) continue;
                var i = 0;
                foreach (var e in arr)
                {
                    Visit(e, path + "[]");
                    if (++i > 5000 && et.IsValueType && !(bool)typeof(RuntimeHelpers).GetMethod("IsReferenceOrContainsReferences")!.MakeGenericMethod(et).Invoke(null, null)!) break;
                }
                continue;
            }
            if (o is Delegate)
                continue;
            foreach (var f in Fields(t))
                Visit(f.GetValue(o), path + "." + f.Name);
        }
        foreach (var (t, n) in counts.OrderBy(k => k.Key.FullName))
            Console.WriteLine($"[describe] {n,8} {t.FullName}  via {firstPath[t]}");
        Console.WriteLine("[describe] Map fields:");
        foreach (var f in Fields(typeof(Map)))
            Console.WriteLine($"[describe]   {f.FieldType.Name} {f.Name}");
        return 0;
    }
}
