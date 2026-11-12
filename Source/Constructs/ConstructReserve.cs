namespace CallOfSlop;

/// <summary>One claim on stock: <see cref="Count"/> of a kind in a store, for an owner's order (stores and owners by their store handles).</summary>
public struct Reservation
{
    public ushort Store; // the store that holds the parts (its handle)
    public ushort Def; // the kind (a part id; a run-time piece's id on this machine: on the wire it goes by its key)
    public ushort Count;
    public ushort Owner; // who it is for: a Crafting Terminal's (later a building station's) store handle; 0 on a client
    public ushort OwnerId; // ...and which of its orders
}

/// <summary>
/// Reservations (docs/sandbox/constructs/production.md "Reservations"): a store's parts claimed for an order, so two orders never
/// count the same part. Available is held less reserved. Shared by the Crafting Terminal (ConstructCrafting.cs) and, next, the
/// blueprint building station. One bounded table a world; the server's is the truth, a client's holds what
/// <see cref="CBodyKind.Craft"/> said (owners unknown), so a list reads the same on either peer. Never saved: the orders are,
/// and they claim again after a load. A reservation dies with its owner, its store, or when its parts have left for the owner.
/// Taking a reserved part by hand is allowed: the claim is cut to what the store still holds and the order asks again.
/// </summary>
public sealed class ConstructReserve
{
    public const int Max = 1024;

    public readonly Reservation[] Items = new Reservation[Max];
    public int Count;

    /// <summary>How many of a kind in a store are claimed (every owner).</summary>
    public int Reserved(ushort store, ushort def)
    {
        var n = 0;
        for (var i = 0; i < Count; i++)
            if (Items[i].Store == store && Items[i].Def == def)
                n += Items[i].Count;
        return n;
    }

    /// <summary>How many parts in a store are claimed, every kind.</summary>
    public int Total(ushort store)
    {
        var n = 0;
        for (var i = 0; i < Count; i++)
            if (Items[i].Store == store)
                n += Items[i].Count;
        return n;
    }

    /// <summary>What an order holds claimed, every store.</summary>
    public int Held(ushort owner, ushort id)
    {
        var n = 0;
        for (var i = 0; i < Count; i++)
            if (Items[i].Owner == owner && Items[i].OwnerId == id)
                n += Items[i].Count;
        return n;
    }

    /// <summary>Claims <paramref name="n"/> more of a kind in a store for an order. False when the table is full.</summary>
    public bool Add(CStore st, ushort def, int n, ushort owner, ushort id)
    {
        if (n <= 0)
            return true;
        for (var i = 0; i < Count; i++)
        {
            ref var r = ref Items[i];
            if (r.Store == st.Handle && r.Def == def && r.Owner == owner && r.OwnerId == id)
            {
                r.Count = (ushort)Math.Min(ushort.MaxValue, r.Count + n);
                Mark(st);
                return true;
            }
        }
        if (Count >= Max)
            return false;
        Items[Count++] = new Reservation { Store = st.Handle, Def = def, Count = (ushort)Math.Min(ushort.MaxValue, n), Owner = owner, OwnerId = id };
        Mark(st);
        return true;
    }

    /// <summary>Cuts entry <paramref name="i"/> by <paramref name="n"/> (its parts left, or were taken); an empty one goes. True when it went.</summary>
    public bool Shrink(int i, int n, CStore? st)
    {
        ref var r = ref Items[i];
        r.Count = (ushort)Math.Max(0, r.Count - n);
        var gone = r.Count == 0;
        if (gone)
            Items[i] = Items[--Count];
        if (st != null)
            Mark(st);
        return gone;
    }

    /// <summary>Drops every claim of an owner's order (<paramref name="id"/> 0xFFFF: all its orders). The stores are marked by the caller's world.</summary>
    public void Release(ConstructWorld world, ushort owner, ushort id = ushort.MaxValue)
    {
        for (var i = Count - 1; i >= 0; i--)
            if (Items[i].Owner == owner && (id == ushort.MaxValue || Items[i].OwnerId == id))
            {
                var st = world.StoreByHandle(Items[i].Store);
                Items[i] = Items[--Count];
                if (st != null)
                    Mark(st);
            }
    }

    /// <summary>A store went (its part was removed): its claims go with it. A terminal that went: its orders' claims too.</summary>
    public void Freed(ConstructWorld world, ushort handle)
    {
        if (Count == 0)
            return;
        for (var i = Count - 1; i >= 0; i--)
            if (Items[i].Store == handle)
                Items[i] = Items[--Count];
        Release(world, handle);
    }

    /// <summary>A store's total, kept on it for its look line ("12 (4 reserved)").</summary>
    private void Mark(CStore st)
    {
        st.Reserved = (ushort)Math.Min(ushort.MaxValue, Total(st.Handle));
        st.Version++;
    }

    /// <summary>A client: one store's claims as the server said them (the whole set for that store).</summary>
    public void ClientSet(CStore st, ReadOnlySpan<(ushort Def, ushort Count)> kinds)
    {
        for (var i = Count - 1; i >= 0; i--)
            if (Items[i].Store == st.Handle)
                Items[i] = Items[--Count];
        foreach (var (def, n) in kinds)
            if (Count < Max && n > 0)
                Items[Count++] = new Reservation { Store = st.Handle, Def = def, Count = n };
        Mark(st);
    }

    public void Clear() => Count = 0;
}
