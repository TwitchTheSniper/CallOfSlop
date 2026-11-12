namespace CallOfSlop;

/// <summary>
/// A store on a monitor (docs/sandbox/constructs/production.md "Production monitors"): a Materials Bin, a Storage Volume, a Parts Shelf
/// or any store with a Data out linked to a display shows its fill and what it holds, as a Piece Rack does. Read from the store on either
/// peer; it only reads (nothing here touches reservations or what a chute or a terminal takes); nothing allocates.
/// </summary>
public static class StoreMonitor
{
    /// <summary>How full a store is, 0 to 1, and its units or parts in and room (a bin: units; a shelf: slots).</summary>
    public static float FillOf(Construct c, int part, ConstructPartDef d, CStore st, out int have, out int room)
    {
        if (d.IsBin)
        {
            room = d.IsVolumeStore ? ConstructStoreVolume.Capacity(c, part) : d.BulkCapacity;
            have = st.Amount;
        }
        else
        {
            room = st.SlotCount;
            have = 0;
            for (var k = 0; k < st.SlotCount; k++)
                have += st.Def[k] == ConstructParts.None ? 0 : 1;
        }
        return room > 0 ? Math.Clamp(have / (float)room, 0, 1) : 0;
    }

    public static void Fill(ConstructWorld world, Construct c, int part, ConstructPartDef d, PanelCard card)
    {
        if (world.StoreOf(c, part) is not { } st)
        {
            card.State = PanelState.Off;
            card.Add(0, d.Name);
            return;
        }
        var fill = FillOf(c, part, d, st, out var have, out var room);
        card.State = fill >= 0.98f ? PanelState.Wants : have > 0 ? PanelState.Working : PanelState.Idle;
        var pct = (int)MathF.Round(fill * 100);
        if (card.Rows == 1)
        {
            // Tiny: "Materials Bin 62% metal" or "Parts Shelf 5/12".
            card.Add(0, d.Name).Add(0, ' ');
            if (d.IsBin)
            {
                card.Add(0, pct).Add(0, '%');
                if (st.Amount > 0)
                    card.Add(0, ' ').Add(0, RecyclingTable.Current.NameOf(st.Material));
            }
            else
                card.Add(0, have).Add(0, '/').Add(0, room);
            return;
        }
        var cols = card.Cols;
        card.Add(0, d.Name).Clip(0, cols);
        card.Bar = fill;
        if (d.IsBin)
        {
            // "120/400 metal, 30%"; a bin holds one material.
            card.Add(1, have).Add(1, '/').Add(1, room);
            card.Add(1, st.Amount > 0 ? " " : " empty");
            if (st.Amount > 0)
                card.Add(1, RecyclingTable.Current.NameOf(st.Material)).Add(1, ", ").Add(1, pct).Add(1, '%');
            card.Clip(1, cols);
            return;
        }
        // A shelf: its slots, then each kind once with its count (scrolling by itself when more than fit).
        card.Add(1, have).Add(1, '/').Add(1, room).Add(1, " slots, ").Add(1, pct).Add(1, "% full").Clip(1, cols);
        var kinds = 0;
        for (var k = 0; k < st.SlotCount; k++)
            if (First(st, k))
                kinds++;
        var fit = card.Rows - 2;
        var from = ProductionLines.AutoFirst(kinds, fit, ConstructRender.Now);
        var n = 0;
        for (var k = 0; k < st.SlotCount; k++)
        {
            if (!First(st, k))
                continue;
            if (n >= from && n - from < fit)
                card.AddRow(2 + n - from, ' ', ConstructParts.Def(st.Def[k]).Name, CountOf(st, st.Def[k]), cols);
            n++;
        }
        if (kinds == 0)
            card.Add(2, "empty");
    }

    /// <summary>Is slot k the first holding its kind (each kind listed once)?</summary>
    private static bool First(CStore st, int k)
    {
        var def = st.Def[k];
        if (def == ConstructParts.None)
            return false;
        for (var j = 0; j < k; j++)
            if (st.Def[j] == def)
                return false;
        return true;
    }

    private static int CountOf(CStore st, ushort def)
    {
        var n = 0;
        for (var k = 0; k < st.SlotCount; k++)
            if (st.Def[k] == def)
                n += Math.Max(1, (int)st.Count[k]);
        return n;
    }
}
