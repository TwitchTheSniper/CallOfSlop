using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace CallOfSlop;

/// <summary>
/// Fast reading for the map bake (MapBakeGraph.cs): a typed reader per field type, and for every class and struct a small
/// method emitted once per run that reads its fields straight into place, in the order the writer wrote them. No
/// reflection and no boxing per field, so a baked map reads in a few milliseconds.
/// </summary>
internal static partial class MapGraph
{
    /// <summary>Reads a value of type <typeparamref name="T"/> in the writer's format for a slot of that declared type.</summary>
    internal static class FieldReader<T>
    {
        public static readonly Func<Reader, T> Read = MakeReader<T>();
    }

    private static Func<Reader, T> MakeReader<T>()
    {
        var t = typeof(T);
        if (Nullable.GetUnderlyingType(t) is { } under)
            return (Func<Reader, T>)typeof(MapGraph).GetMethod(nameof(NullableReader), BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(under).Invoke(null, null)!;
        if (t.IsValueType && !HasRefs(t))
            return r => r.Value<T>();
        if (t.IsValueType)
            return EmitStruct<T>();
        return r => (T)r.Ref()!;
    }

    private static Func<Reader, TU?> NullableReader<TU>() where TU : struct => r => r.Byte() == 0 ? null : FieldReader<TU>.Read(r);

    /// <summary>A struct with references: each field read with its own <see cref="FieldReader{T}"/>, in the writer's order.</summary>
    private static Func<Reader, T> EmitStruct<T>()
    {
        var t = typeof(T);
        var dm = new DynamicMethod($"ReadStruct_{t.Name}", t, [typeof(Reader)], typeof(MapGraph).Module, skipVisibility: true);
        var il = dm.GetILGenerator();
        var v = il.DeclareLocal(t);
        il.Emit(OpCodes.Ldloca, v);
        il.Emit(OpCodes.Initobj, t);
        foreach (var f in BakedFields(t))
        {
            il.Emit(OpCodes.Ldloca, v);
            EmitRead(il, f.FieldType);
            il.Emit(OpCodes.Stfld, f);
        }
        il.Emit(OpCodes.Ldloc, v);
        il.Emit(OpCodes.Ret);
        return (Func<Reader, T>)dm.CreateDelegate(typeof(Func<Reader, T>));
    }

    /// <summary>A class's fields read into an object already created (<see cref="ClassCodec"/>).</summary>
    internal static Action<Reader, object> EmitClass(Type t)
    {
        var dm = new DynamicMethod($"ReadClass_{t.Name}", null, [typeof(Reader), typeof(object)], typeof(MapGraph).Module, skipVisibility: true);
        var il = dm.GetILGenerator();
        var o = il.DeclareLocal(t);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, t);
        il.Emit(OpCodes.Stloc, o);
        foreach (var f in BakedFields(t))
        {
            il.Emit(OpCodes.Ldloc, o);
            EmitRead(il, f.FieldType);
            il.Emit(OpCodes.Stfld, f);
        }
        il.Emit(OpCodes.Ret);
        return (Action<Reader, object>)dm.CreateDelegate(typeof(Action<Reader, object>));
    }

    // Pushes FieldReader<type>.Read(reader) (the reader is argument 0).
    private static void EmitRead(ILGenerator il, Type type)
    {
        var holder = typeof(FieldReader<>).MakeGenericType(type);
        var func = typeof(Func<,>).MakeGenericType(typeof(Reader), type);
        il.Emit(OpCodes.Ldsfld, holder.GetField(nameof(FieldReader<int>.Read))!);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, func.GetMethod("Invoke")!);
    }

    internal sealed partial class Reader
    {
        /// <summary>A value without references, as raw bytes.</summary>
        public T Value<T>()
        {
            var n = Unsafe.SizeOf<T>();
            if (n > _end - _pos)
                throw new EndOfStreamException();
            var v = Unsafe.ReadUnaligned<T>(ref _buf[_pos]);
            _pos += n;
            return v;
        }
    }
}

/// <summary>
/// XXH64 (seed 0): the map bake's check that a file is whole (MapBake.cs). Not a key: keys are SHA-256. Several times
/// faster than SHA-256 on processors without SHA instructions, which matters for 100 MB of bakes at start-up.
/// </summary>
internal static class XxHash64
{
    private const ulong P1 = 11400714785074694791UL, P2 = 14029467366897019727UL, P3 = 1609587929392839161UL, P4 = 9650029242287828579UL, P5 = 2870177450012600261UL;

    private static ulong Round(ulong acc, ulong input) => System.Numerics.BitOperations.RotateLeft(acc + input * P2, 31) * P1;

    private static ulong Merge(ulong acc, ulong val) => (acc ^ Round(0, val)) * P1 + P4;

    public static ulong Hash(ReadOnlySpan<byte> data)
    {
        var len = data.Length;
        var i = 0;
        ulong h;
        if (len >= 32)
        {
            ulong v1 = unchecked(P1 + P2), v2 = P2, v3 = 0, v4 = unchecked(0 - P1);
            for (; i <= len - 32; i += 32)
            {
                v1 = Round(v1, System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(data[i..]));
                v2 = Round(v2, System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(data[(i + 8)..]));
                v3 = Round(v3, System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(data[(i + 16)..]));
                v4 = Round(v4, System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(data[(i + 24)..]));
            }
            h = System.Numerics.BitOperations.RotateLeft(v1, 1) + System.Numerics.BitOperations.RotateLeft(v2, 7)
                + System.Numerics.BitOperations.RotateLeft(v3, 12) + System.Numerics.BitOperations.RotateLeft(v4, 18);
            h = Merge(h, v1);
            h = Merge(h, v2);
            h = Merge(h, v3);
            h = Merge(h, v4);
        }
        else
            h = P5;
        h += (ulong)len;
        for (; i <= len - 8; i += 8)
            h = System.Numerics.BitOperations.RotateLeft(h ^ Round(0, System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(data[i..])), 27) * P1 + P4;
        if (i <= len - 4)
        {
            h = System.Numerics.BitOperations.RotateLeft(h ^ System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(data[i..]) * P1, 23) * P2 + P3;
            i += 4;
        }
        for (; i < len; i++)
            h = System.Numerics.BitOperations.RotateLeft(h ^ data[i] * P5, 11) * P1;
        h ^= h >> 33;
        h *= P2;
        h ^= h >> 29;
        h *= P3;
        h ^= h >> 32;
        return h;
    }
}
