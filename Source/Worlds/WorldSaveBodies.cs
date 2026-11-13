using System.Numerics;

namespace CallOfSlop;

/// <summary>The names behind a save's numbers: parts, materials and setting keys, each listed once in the file.</summary>
public sealed class WorldNames
{
    private readonly Dictionary<ushort, ushort> _parts = new();
    private readonly Dictionary<string, ushort> _keys = new();
    public readonly List<string> Parts = new(), Keys = new();

    /// <summary>A part's number in this file (the extrusions' names hold their whole key).</summary>
    public ushort Part(ushort def)
    {
        if (def == ConstructParts.None)
            return ushort.MaxValue;
        if (!_parts.TryGetValue(def, out var k))
        {
            k = (ushort)Parts.Count;
            _parts[def] = k;
            Parts.Add(ConstructParts.Def(def).Name);
        }
        return k;
    }

    public ushort Key(string name)
    {
        if (!_keys.TryGetValue(name, out var k))
        {
            k = (ushort)Keys.Count;
            _keys[name] = k;
            Keys.Add(name);
        }
        return k;
    }

    public void Write(SaveWriter w)
    {
        w.I32(Parts.Count);
        foreach (var n in Parts)
            w.Str(n);
        w.I32(Keys.Count);
        foreach (var n in Keys)
            w.Str(n);
        var mats = RecyclingTable.Current.Materials;
        w.I32(mats.Length);
        foreach (var n in mats)
            w.Str(n);
    }
}

/// <summary>A save's names as this build has them: what a number in the file means here, and what no longer exists (each logged once).</summary>
public sealed class WorldNamesRead
{
    public ushort[] Parts = [];
    public string[] PartNames = [];
    public string[] Keys = [];
    public byte[] Materials = [];
    public string[] MaterialNames = [];
    public readonly Dictionary<string, int> Missing = new();
    public int Skipped;

    public void Read(SaveReader r)
    {
        var n = r.Count(4);
        Parts = new ushort[n];
        PartNames = new string[n];
        for (var k = 0; k < n; k++)
        {
            PartNames[k] = r.Str();
            Parts[k] = ConstructParts.Find(PartNames[k])?.Id ?? ConstructParts.None;
        }
        n = r.Count(4);
        Keys = new string[n];
        for (var k = 0; k < n; k++)
            Keys[k] = r.Str();
        n = r.Count(4);
        Materials = new byte[n + 1];
        MaterialNames = new string[n + 1];
        for (var k = 0; k < n; k++)
        {
            MaterialNames[k + 1] = r.Str();
            Materials[k + 1] = RecyclingTable.Current.IdOf(MaterialNames[k + 1]);
        }
    }

    /// <summary>A part number from the file as this build's id, or <see cref="ConstructParts.None"/> (noted as skipped).</summary>
    public ushort Part(ushort number, string where)
    {
        if (number == ushort.MaxValue)
            return ConstructParts.None;
        if (number >= Parts.Length)
            throw new WorldFileException($"{where}: part number {number}");
        var id = Parts[number];
        if (id == ConstructParts.None)
            Note(PartNames[number]);
        return id;
    }

    /// <summary>A material number from the file as this build's id, or 0 (noted as skipped).</summary>
    public byte Material(byte number)
    {
        if (number == 0)
            return 0;
        if (number >= Materials.Length)
            throw new WorldFileException($"material number {number}");
        if (Materials[number] == 0)
            Note("material " + MaterialNames[number]);
        return Materials[number];
    }

    public void Note(string name)
    {
        Missing[name] = Missing.GetValueOrDefault(name) + 1;
        Skipped++;
    }
}

/// <summary>A world's constructs and stores in a save (docs/sandbox/WORLD-SAVES.md): caught on the tick, written on the worker, read back at the start of play.</summary>
public sealed partial class ConstructWorld
{
    private const uint LogiTag = 0x49474F4C; // "LOGI": a body's smart logistics block (its riders' destinations)

    /// <summary>The tick's copy: every array the worker needs, in bulk.</summary>
    public void CaptureInto(WorldSnapshot s, Func<Construct, bool>? keep = null)
    {
        // keep: only some bodies and their stores (a planetoid left behind without the ship that leaves it: GameSpaceLanding.cs).
        WorldSnapshot.Grow(ref s.Bodies, Bodies.Count);
        s.BodyCount = s.PartCount = s.LinkCount = s.JointCount = s.RouteCount = s.NodeCount = s.TrackCount = s.TrackPartCount = 0;
        foreach (var c in Bodies)
        {
            if (c.PartCount == 0 || keep != null && !keep(c))
                continue; // (a body emptied this tick: it goes on the next)
            ref var b = ref s.Bodies[s.BodyCount++];
            b.Id = c.Id;
            b.Owner = c.Owner;
            b.FrozenBy = c.FrozenBy;
            b.Wreck = c.Wreck;
            b.Asleep = c.Asleep;
            b.Damaged = c.Damaged;
            b.Pinned = c.Pinned;
            b.Frozen = c.Frozen;
            b.HoverOn = c.HoverOn;
            b.HoverY = c.HoverY;
            b.Root = c.Root;
            b.Gear = c.Gear;
            b.Fuel = c.Fuel;
            b.Charge = c.Charge;
            b.Pos = c.Pos;
            b.Rot = c.Rot;
            b.Vel = c.Vel;
            b.AngVel = c.AngVel;
            b.CarriedBy = c.CarriedBy;
            b.CarryTag = c.CarryTag;
            b.CarryPart = c.CarryPart;
            b.CarrySeg = c.CarrySeg;
            b.CarryPos = c.CarryPos;
            b.CarryRot = c.CarryRot;
            b.SpaceShip = c.SpaceShip;
            b.Brief = c.Brief;
            b.Conveyed = c.ConveyedCount > 0 ? c.Conveyed.AsSpan(0, c.ConveyedCount).ToArray() : null; // (a save is rare: a copy is fine)
            if (b.Conveyed != null)
                foreach (ref var cr in b.Conveyed.AsSpan())
                    cr.Dest = (ushort)(ConstructLogistics.PartOfStore(c, cr.Dest) + 1); // (the copy's destination by part, plus one: handles are this run's own)
            b.Orders = WorkOrders.Snapshot(c);
            b.Craft = ConstructCrafting.Snapshot(c);
            b.Station = ConstructStation.Snapshot(c);
            b.Chains = ConstructLogisticsPlan.Snapshot(this, c);
            b.Targets = global::CallOfSlop.StockTargets.Snapshot(this, c);
            b.Parts = s.PartCount;
            b.PartCount = c.PartCount;
            b.Links = s.LinkCount;
            b.LinkCount = c.LinkCount;
            b.Joints = s.JointCount;
            b.JointCount = c.JointCount;
            b.Routes = s.RouteCount;
            b.RouteCount = c.RouteCount;
            b.Nodes = s.NodeCount;
            b.Tracks = s.TrackCount;
            b.TrackCount = c.TrackCount;
            b.TrackParts = s.TrackPartCount;
            if (c.TrackCount > 0)
            {
                if (s.TrackCount + c.TrackCount > s.Tracks.Length)
                    Array.Resize(ref s.Tracks, (s.TrackCount + c.TrackCount) * 3 / 2);
                if (s.TrackPartCount + c.TrackPartCount > s.TrackParts.Length)
                    Array.Resize(ref s.TrackParts, (s.TrackPartCount + c.TrackPartCount) * 3 / 2);
                Array.Copy(c.Tracks, 0, s.Tracks, s.TrackCount, c.TrackCount);
                Array.Copy(c.TrackParts, 0, s.TrackParts, s.TrackPartCount, c.TrackPartCount);
                s.TrackCount += c.TrackCount;
                s.TrackPartCount += c.TrackPartCount;
            }
            if (s.PartCount + c.PartCount > s.Parts.Length)
                Array.Resize(ref s.Parts, (s.PartCount + c.PartCount) * 3 / 2);
            if (s.LinkCount + c.LinkCount > s.Links.Length)
                Array.Resize(ref s.Links, (s.LinkCount + c.LinkCount) * 3 / 2);
            if (s.JointCount + c.JointCount > s.Joints.Length)
                Array.Resize(ref s.Joints, (s.JointCount + c.JointCount) * 3 / 2);
            if (s.RouteCount + c.RouteCount > s.Routes.Length)
                Array.Resize(ref s.Routes, (s.RouteCount + c.RouteCount) * 3 / 2);
            if (s.NodeCount + c.RouteNodeCount > s.Nodes.Length)
                Array.Resize(ref s.Nodes, (s.NodeCount + c.RouteNodeCount) * 3 / 2);
            Array.Copy(c.Parts, 0, s.Parts, s.PartCount, c.PartCount);
            Array.Copy(c.Links, 0, s.Links, s.LinkCount, c.LinkCount);
            Array.Copy(c.Joints, 0, s.Joints, s.JointCount, c.JointCount);
            Array.Copy(c.Routes, 0, s.Routes, s.RouteCount, c.RouteCount);
            Array.Copy(c.RouteNodes, 0, s.Nodes, s.NodeCount, c.RouteNodeCount);
            s.PartCount += c.PartCount;
            s.LinkCount += c.LinkCount;
            s.JointCount += c.JointCount;
            s.RouteCount += c.RouteCount;
            s.NodeCount += c.RouteNodeCount;
        }
        if (s.Stores.Length != MaxStores + 1)
        {
            s.Stores = new CStore[MaxStores + 1];
            for (var k = 0; k < s.Stores.Length; k++)
                s.Stores[k] = new CStore();
        }
        s.StoreCount = 0;
        HashSet<ushort>? kept = null;
        if (keep != null)
        {
            kept = [];
            foreach (var c in Bodies)
                if (keep(c))
                    for (var i = 0; i < c.PartCount; i++)
                        if (c.Parts[i].Store != 0)
                            kept.Add(c.Parts[i].Store);
        }
        for (var k = 1; k <= MaxStores; k++)
            if (Stores[k].Live && (kept == null || kept.Contains((ushort)k)))
                WorldStores.Copy(Stores[k], s.Stores[s.StoreCount++]);
        WorldSnapshot.Grow(ref s.Couplings, CouplingCount);
        Array.Copy(Couplings, s.Couplings, s.CouplingCount = CouplingCount);
        WorldSnapshot.Grow(ref s.XWires, XWireCount);
        Array.Copy(XWires, s.XWires, s.XWireCount = XWireCount);
        WorldSnapshot.Grow(ref s.WorldLinks, WorldLinkCount);
        Array.Copy(WorldLinks, s.WorldLinks, s.WorldLinkCount = WorldLinkCount);
        WorldSnapshot.Grow(ref s.WorldNodes, WorldNodeCount);
        Array.Copy(WorldNodes, s.WorldNodes, s.WorldNodeCount = WorldNodeCount);
        WorldSnapshot.Grow(ref s.Hoses, HoseCount);
        Array.Copy(Hoses, s.Hoses, s.HoseCount = HoseCount);
        WorldSnapshot.Grow(ref s.Connectors, ConnectorCount);
        s.ConnectorCount = 0;
        for (var k = 0; k < ConnectorCount; k++)
            if (Connectors[k].A.Player == 0 && Connectors[k].B.Player == 0) // (a line on a player doesn't outlive the session; one between constructs loads as a plain rope)
                s.Connectors[s.ConnectorCount++] = Connectors[k];
        s.NextConnector = _nextConnector;
        SnapshotNoCollides(s);
        s.NextTag = _nextTag;
        s.Designs = [.. Designs]; // (the world's shared panel designs: PanelDesigns.cs)
    }

    /// <summary>The setting key a part's texture (ConstructFinish.cs) is saved under, followed by the material's name.</summary>
    public const string FinishKey = "finish:";

    /// <summary>The setting key a part's fused unit (ConstructFuse.cs) is saved under: its number in the body (1, 2, ...), new tags when read.</summary>
    public const string FuseKey = "fuse:unit";

    /// <summary>One body as bytes: its fields, then its parts as columns (like values side by side, which packs far smaller).</summary>
    public static void WriteBody(SaveWriter w, WorldSnapshot s, in BodySnap b, WorldNames names)
    {
        w.U16(b.Id);
        w.U8((byte)((b.Wreck ? 1 : 0) | (b.Asleep ? 2 : 0) | (b.Damaged ? 4 : 0) | (b.Pinned ? 8 : 0) | (b.Frozen ? 16 : 0) | (b.HoverOn ? 32 : 0)));
        w.U16(s.OwnerRecord[b.Owner]).U16(s.OwnerRecord[b.FrozenBy]);
        w.I32(b.Root).I32(b.Gear).F32(b.Fuel).F32(b.Charge).F32(b.HoverY);
        w.Vec(b.Pos).Quat(b.Rot).Vec(b.Vel).Vec(b.AngVel);
        w.U16(b.SpaceShip).U16(b.CarriedBy);
        if (b.CarriedBy != 0)
            w.U16(b.CarryTag).U16(b.CarryPart).U8(b.CarrySeg).Vec(b.CarryPos).Quat(b.CarryRot);
        var parts = s.Parts.AsSpan(b.Parts, b.PartCount);
        w.I32(parts.Length);
        foreach (ref var p in parts)
            w.U16(names.Part(p.Def));
        foreach (ref var p in parts)
            w.Vec(p.Pos);
        foreach (ref var p in parts)
            w.U8(p.Rot);
        foreach (ref var p in parts)
            w.U8(p.Seg);
        foreach (ref var p in parts)
            w.F32(p.Health);
        foreach (ref var p in parts)
            w.U8((byte)(p.Broken ? 1 : 0));
        foreach (ref var p in parts)
            w.U8(p.Setting);
        foreach (ref var p in parts)
            w.U8(p.State);
        foreach (ref var p in parts)
            w.U16(p.Count);
        foreach (ref var p in parts)
            w.F32(p.Out);
        foreach (ref var p in parts)
            w.U16(p.Tag);
        foreach (ref var p in parts)
            w.U16(p.Rounds);
        foreach (ref var p in parts)
            w.U16(p.Cool);
        foreach (ref var p in parts)
            w.U16(p.Reload);
        foreach (ref var p in parts)
            w.U16(p.Burn);
        foreach (ref var p in parts)
            w.U16(p.Store);
        foreach (ref var p in parts)
            w.F32(p.SpawnTotal);
        foreach (ref var p in parts)
            w.F32(p.SpawnClock);
        foreach (ref var p in parts)
            w.U32(p.Paint);
        // Settings off their defaults, by key: a part file's schema may gain or reorder slots.
        var countAt = new SaveWriter(4096);
        var settings = 0;
        Dictionary<uint, int>? fused = null; // (the fused units, numbered 1, 2, ... in this body: ConstructFuse.cs)
        for (var i = 0; i < parts.Length; i++)
        {
            ref var p = ref parts[i];
            var d = p.D;
            // (the fuse tool's unit, numbered in this body, as a setting an older reader skips: ConstructFuse.cs)
            if (p.Fuse != 0)
            {
                fused ??= new Dictionary<uint, int>();
                if (!fused.TryGetValue(p.Fuse, out var unit))
                    fused[p.Fuse] = unit = fused.Count + 1;
                countAt.U16((ushort)i).U16(names.Key(FuseKey)).F32(unit);
                settings++;
            }
            for (var k = 0; k < d.Config.Length; k++)
                if (!d.Config[k].Bound && p.Cfg[k] != d.Config[k].Default)
                {
                    countAt.U16((ushort)i).U16(names.Key(d.Config[k].Key)).F32(p.Cfg[k]);
                    settings++;
                }
            // (a sign's messages, as settings an older reader skips: LogicText.cs)
            for (var slot = 0; slot < d.TextSlots; slot++)
            {
                var text = LogicText.Get(p, slot);
                for (var chunk = 0; chunk * 3 < text.Length; chunk++)
                {
                    countAt.U16((ushort)i).U16(names.Key(LogicText.SaveKey(slot, chunk))).F32(LogicText.Pack(text, chunk));
                    settings++;
                }
            }
            // (the texture paint tool's material, by name, as a setting an older reader skips: ConstructFinish.cs)
            if (p.Finish != 0 && ConstructFinish.Name(p.Finish) is { Length: > 0 } finish)
            {
                countAt.U16((ushort)i).U16(names.Key(FinishKey + finish)).F32(0f);
                settings++;
            }
            // (a media part's list, the same way: ConstructMedia.cs)
            if (d.IsMedia)
            {
                var text = ConstructMedia.SaveText(p);
                for (var chunk = 0; chunk * 3 < text.Length; chunk++)
                {
                    countAt.U16((ushort)i).U16(names.Key(ConstructMedia.SaveKey(chunk))).F32(LogicText.Pack(text, chunk));
                    settings++;
                }
            }
        }
        w.I32(settings).Bytes(countAt.Span);
        w.I32(b.LinkCount);
        foreach (ref var l in s.Links.AsSpan(b.Links, b.LinkCount))
            w.U16(l.A).U16(l.B).U8(l.SA).U8(l.SB);
        w.I32(b.JointCount);
        foreach (ref var j in s.Joints.AsSpan(b.Joints, b.JointCount))
        {
            w.U16(j.Hinge).U8(j.ParentSeg).U8((byte)j.Type).U16(j.Child);
            w.U8((byte)((j.Open ? 1 : 0) | (j.Locked ? 2 : 0) | (j.Wired ? 4 : 0) | (j.RefSet ? 8 : 0) | (j.Frozen ? 16 : 0)));
            w.Vec(j.Pivot).Vec(j.Axis).F32(j.Angle).F32(j.Vel).F32(j.Min).F32(j.Max).F32(j.Ref);
            w.Quat(j.FixRot).Vec(j.FixPos);
        }
        w.I32(b.RouteCount);
        foreach (ref var r in s.Routes.AsSpan(b.Routes, b.RouteCount))
        {
            w.U8((byte)r.Type).U16(r.From).U16(r.To).U8(r.FromPort).U8(r.ToPort).U8(r.Parent);
            if (r.Parent == 255 || r.ParentNode < LinkSave.NodeEscape)
                w.U8((byte)Math.Min((int)r.ParentNode, 255));
            else
                w.U8(LinkSave.NodeEscape).U16(r.ParentNode);
            LinkSave.WriteNodeCount(w, r.Count);
            w.F32(r.EndHealth).U32(r.Paint).F32(r.Timer).U8(r.Cursor);
            foreach (ref var n in s.Nodes.AsSpan(b.Nodes + r.Start, r.Count))
                w.U16(n.Host).Vec(n.Pos).F32(n.Health);
        }
        // The tracks, last: a body's bytes have a length, so an older save simply ends before them.
        w.I32(b.TrackCount);
        foreach (ref var t in s.Tracks.AsSpan(b.Tracks, b.TrackCount))
        {
            w.U8(t.Count).F32(t.Width).F32(t.LinkLength).F32(t.Tension).F32(t.Grip).F32(t.Health);
            foreach (var p in s.TrackParts.AsSpan(b.TrackParts + t.Start, t.Count))
                w.U16(p);
        }
        // A generated faction vehicle's behaviour brief (VehicleBrief.cs), after the tracks the same way: its length, then its bytes (0: none).
        if (b.Brief is { } brief)
        {
            var bw = new SaveWriter(1024);
            brief.Write(bw);
            w.I32(bw.Length).Bytes(bw.Span);
        }
        else
            w.I32(0);
        // Conveyors (ConstructConveyor.cs), after the brief the same way: their sizes, then the pieces riding them; a body without any writes nothing.
        var routes = s.Routes.AsSpan(b.Routes, b.RouteCount);
        var conveyors = b.Conveyed != null;
        foreach (ref readonly var r in routes)
            conveyors |= r.Type == RouteType.Conveyor;
        var logi = false; // (a rider with a destination: the LOGI block follows the stations' jobs)
        foreach (ref readonly var cr in (b.Conveyed ?? []).AsSpan())
            logi |= cr.Dest != 0 || cr.Flags != 0;
        logi |= b.Chains != null || b.Targets != null;
        if (conveyors || b.Orders != null || b.Craft != null || b.Station != null || logi)
            ConstructConveyor.WriteSave(w, routes, b.Conveyed ?? [], names); // (an empty one before the work orders: they follow it)
        // Work orders (WorkOrders.cs), after the conveyors: a body without any writes nothing.
        if (b.Orders != null || b.Craft != null || b.Station != null || logi)
            WorkOrders.WriteSave(w, b.Orders ?? [], names); // (an empty one before the crafting orders: they follow it)
        // Crafting Terminals' orders (ConstructCrafting.cs), after the work orders the same way.
        if (b.Craft != null || b.Station != null || logi)
            ConstructCrafting.WriteSave(w, b.Craft ?? [], names); // (an empty one before the stations' jobs: they follow it)
        // Building Stations' jobs (ConstructStation.cs), after the crafting orders the same way.
        if (b.Station != null || logi)
            ConstructStation.WriteSave(w, b.Station ?? []);
        // Smart logistics (ConstructLogistics.cs), after the stations' jobs: "LOGI", its length, then per rider in the riders' order its
        // destination part plus one (0: none), its order and its LogiFlags; then (phase 2) the chained orders' links: i32 n, each u16 id,
        // maker part (0xFFFF: none), kind (by the save's names), asked, made, destination part, root terminal part, root order. An older
        // build skips it; an older save ends before it; a phase 1 block ends after the riders.
        if (logi)
        {
            var riders = b.Conveyed ?? [];
            var chains = b.Chains ?? [];
            var targets = b.Targets ?? [];
            w.U32(LogiTag).I32(4 + riders.Length * 5 + 4 + chains.Length * 16 + 4 + chains.Length * 9 + 4 + targets.Length * 4);
            w.I32(riders.Length);
            foreach (ref readonly var cr in riders.AsSpan())
                w.U16(cr.Dest).U16(cr.Tag).U8(cr.Flags);
            w.I32(chains.Length);
            foreach (ref readonly var e in chains.AsSpan())
                w.U16(e.Id).U16(e.Maker).U16(names.Part(e.Kind)).U16(e.Asked).U16(e.Made).U16(e.Dest).U16(e.Root).U16(e.RootId);
            // (phase 2b, deeper chains: per link in the same order, u16 parent id, u8 depth, u8 class, u32 age stamp, u8 ChainFlags)
            w.I32(chains.Length);
            foreach (ref readonly var e in chains.AsSpan())
                w.U16(e.Parent).U8(e.Depth).U8(e.Class).U32(e.Stamp).U8(e.Flags);
            // (phase 3, stock targets: i32 n, each u16 rack part, u16 kind by the save's names)
            w.I32(targets.Length);
            foreach (var (tp, tk) in targets)
                w.U16(tp).U16(names.Part(tk));
        }
    }

    public static void WriteStore(SaveWriter w, CStore s, WorldNames names)
    {
        w.U16(s.Handle).U8(s.SlotCount);
        for (var k = 0; k < s.SlotCount; k++)
            w.U16(names.Part(s.Def[k])).U8(s.Rot[k]).U8(s.Count[k]).F32(s.Health[k]);
        w.I32(s.Amount).U8(s.Material).I32(s.InAmount).U8(s.InMaterial).U8((byte)(s.Held ? 1 : 0)).U8(s.Blocked);
        w.I32(s.JobTicks).I32(s.JobAmount).U8(s.JobMaterial);
        w.U8((byte)s.MixN);
        for (var k = 0; k < s.MixN; k++)
            w.U8(s.MixMat![k]).I32(s.MixAmount![k]);
        w.U16((ushort)s.KindN);
        for (var k = 0; k < s.KindN; k++)
            w.U16(names.Part(s.KindDef![k])).I32(s.KindCount![k]);
    }

    /// <summary>A store as a save has it, what no longer exists left out. Reads into <paramref name="s"/> (the handle is the caller's).</summary>
    public static void ReadStore(SaveReader r, CStore s, WorldNamesRead names)
    {
        int n = r.U8();
        if (n > CStore.MaxSlots)
            throw new WorldFileException($"a store of {n} slots");
        s.Reset(n);
        for (var k = 0; k < n; k++)
        {
            var def = names.Part(r.U16(), "a store");
            var rot = r.U8();
            var count = r.U8();
            var hp = r.F32();
            if (def == ConstructParts.None || rot >= Rot24.Count || count == 0)
                continue;
            s.Def[k] = def;
            s.Rot[k] = rot;
            s.Count[k] = count;
            s.Health[k] = float.IsFinite(hp) ? Math.Clamp(hp, 0, 1) : 1;
        }
        var amount = r.I32();
        var material = names.Material(r.U8());
        s.Material = material;
        s.Amount = material == 0 ? 0 : Math.Clamp(amount, 0, ConstructStoreVolume.MaxBulk);
        var input = r.I32();
        var inMaterial = names.Material(r.U8());
        s.InMaterial = inMaterial;
        s.InAmount = inMaterial == 0 ? 0 : Math.Clamp(input, 0, ConstructStoreVolume.MaxBulk);
        s.Held = r.U8() != 0;
        s.Blocked = r.U8();
        s.JobTicks = Math.Max(0, r.I32());
        s.JobAmount = Math.Max(0, r.I32());
        s.JobMaterial = names.Material(r.U8());
        int mix = r.U8();
        if (mix > CStore.MaxMix)
            throw new WorldFileException($"a mix of {mix} materials");
        for (var k = 0; k < mix; k++)
        {
            var m = names.Material(r.U8());
            var a = r.I32();
            if (m == 0)
                continue;
            s.MixMat ??= new byte[CStore.MaxMix];
            s.MixAmount ??= new int[CStore.MaxMix];
            s.MixMat[s.MixN] = m;
            s.MixAmount[s.MixN++] = Math.Max(0, a);
        }
        int kinds = r.U16();
        for (var k = 0; k < kinds; k++)
        {
            var def = names.Part(r.U16(), "a store");
            var count = r.I32();
            if (def != ConstructParts.None && count > 0)
                s.AddKind(def, Math.Min(count, CStore.MaxPerKind));
        }
        s.Version++;
    }

    /// <summary>
    /// Builds a body from a save. A part this build no longer has stands in as a plain block while the links are read, then
    /// goes the way the remover takes a part (<see cref="RemoveParts"/>): what it held on falls away as wrecks and the rest of
    /// the construct stands. Bad numbers throw <see cref="WorldFileException"/>, and the caller skips this body alone.
    /// </summary>
    public Construct? ReadBody(SaveReader r, WorldNamesRead names, Func<ushort, byte> owner, int bodyEnd = int.MaxValue,
        bool freshId = false, Func<ushort, ushort>? storeAs = null)
    {
        // freshId / storeAs: a body read into a world that already holds others (a ship landing on another planetoid,
        // Game/GameSpaceLanding.cs): it takes a free id, and its stores the handles they were copied to.
        var id = r.U16();
        var flags = r.U8();
        var ownerRec = r.U16();
        var frozenRec = r.U16();
        var root = r.I32();
        var gear = r.I32();
        var fuel = r.F32();
        var charge = r.F32();
        var hoverY = r.F32();
        var pos = r.At();
        var rot = r.Quat();
        var vel = r.Vec();
        var ang = r.Vec();
        if (!float.IsFinite(pos.X + pos.Y + pos.Z + rot.X + rot.Y + rot.Z + rot.W) || rot.LengthSquared() < 0.5f)
            throw new WorldFileException("a pose that isn't a number");
        var ship = r.U16();
        var carriedBy = r.U16();
        ushort carryTag = 0, carryPart = 0;
        byte carrySeg = 0;
        var carryPos = Vector3.Zero;
        var carryRot = Quaternion.Identity;
        if (carriedBy != 0)
        {
            carryTag = r.U16();
            carryPart = r.U16();
            carrySeg = r.U8();
            carryPos = r.Vec();
            carryRot = r.Quat();
        }
        var n = r.Count(2);
        if (n == 0 || n > Construct.MaxParts)
            throw new WorldFileException($"a construct of {n} parts");
        var stand = new bool[n];
        var parts = new CPart[n];
        for (var i = 0; i < n; i++)
        {
            var def = names.Part(r.U16(), "a construct");
            stand[i] = def == ConstructParts.None;
            parts[i].Def = stand[i] ? StandIn.Id : def;
        }
        for (var i = 0; i < n; i++)
            parts[i].Pos = r.Vec();
        for (var i = 0; i < n; i++)
        {
            parts[i].Rot = r.U8();
            if (parts[i].Rot >= Rot24.Count)
                throw new WorldFileException($"part {i}: turn {parts[i].Rot}");
        }
        for (var i = 0; i < n; i++)
            parts[i].Seg = r.U8();
        for (var i = 0; i < n; i++)
            parts[i].Health = r.F32();
        for (var i = 0; i < n; i++)
            parts[i].Broken = r.U8() != 0;
        for (var i = 0; i < n; i++)
        {
            var setting = r.U8();
            var sd = parts[i].D; // (a material bundle's Setting is its material id, not a row of its settings: it has none)
            parts[i].Setting = setting > 0 && setting >= sd.Settings.Length && sd.Yield == 0 ? sd.DefaultSetting : setting;
        }
        for (var i = 0; i < n; i++)
            parts[i].State = r.U8();
        for (var i = 0; i < n; i++)
            parts[i].Count = r.U16();
        for (var i = 0; i < n; i++)
            parts[i].Out = parts[i].OutNext = r.F32();
        for (var i = 0; i < n; i++)
            parts[i].Tag = r.U16();
        for (var i = 0; i < n; i++)
            parts[i].Rounds = r.U16();
        for (var i = 0; i < n; i++)
            parts[i].Cool = r.U16();
        for (var i = 0; i < n; i++)
            parts[i].Reload = r.U16();
        for (var i = 0; i < n; i++)
            parts[i].Burn = r.U16();
        for (var i = 0; i < n; i++)
            parts[i].Store = storeAs != null ? storeAs(r.U16()) : r.U16();
        for (var i = 0; i < n; i++)
            parts[i].SpawnTotal = r.F32();
        for (var i = 0; i < n; i++)
            parts[i].SpawnClock = r.F32();
        for (var i = 0; i < n; i++)
            parts[i].Paint = r.U32();
        for (var i = 0; i < n; i++)
        {
            // (the state is the save's: Init sets a switch or toggle to its Start on setting, which is read further down, so a loaded main
            // switch was always off and its floor had no power until someone worked it: 2026-11-12)
            var was = parts[i].State;
            ConstructConfig.Init(ref parts[i], parts[i].D);
            parts[i].State = was;
        }
        var settings = r.Count(8);
        ConstructFuse.Renumber? units = null;
        for (var k = 0; k < settings; k++)
        {
            var part = r.U16();
            var key = r.U16();
            var v = r.F32();
            if (part >= n || key >= names.Keys.Length)
                throw new WorldFileException("a setting of a part that isn't there");
            if (names.Keys[key] == FuseKey)
            {
                if (float.IsFinite(v) && v >= 1 && v < 1 << 24)
                    parts[part].Fuse = (units ??= new ConstructFuse.Renumber()).Tag((int)v); // (new tags: a unit is one by its number in this body)
                continue;
            }
            if (names.Keys[key].StartsWith(FinishKey, StringComparison.Ordinal))
            {
                parts[part].Finish = ConstructFinish.Of(names.Keys[key][FinishKey.Length..]); // (a material this build doesn't have: the part's own)
                continue;
            }
            var d = parts[part].D;
            var slot = Array.FindIndex(d.Config, e => !e.Bound && e.Key == names.Keys[key]);
            if (slot >= 0 && float.IsFinite(v))
                parts[part].Cfg[slot] = d.Config[slot].Clamp(v);
            else if (slot < 0 && d.TextSlots > 0 && float.IsFinite(v))
                LogicText.LoadChunk(ref parts[part], names.Keys[key], v); // (a sign's message: LogicText.cs)
            else if (slot < 0 && d.IsMedia && float.IsFinite(v))
                ConstructMedia.LoadChunk(ref parts[part], names.Keys[key], v);
        }
        for (var i = 0; i < n; i++)
            ConstructMedia.LoadDone(ref parts[i]);
        var links = r.Count(6);
        if (links > Construct.MaxLinks)
            throw new WorldFileException($"a construct of {links} links");
        var linkList = new CLink[links];
        var keptLinks = 0;
        for (var k = 0; k < links; k++)
        {
            var l = new CLink { A = r.U16(), B = r.U16(), SA = r.U8(), SB = r.U8() };
            if (l.A >= n || l.B >= n)
                throw new WorldFileException("a link to a part that isn't there");
            if (stand[l.A] || stand[l.B] || SocketGone(parts[l.A], l.SA) || SocketGone(parts[l.B], l.SB))
                continue; // (a socket a part file has since lost goes like the link's part would)
            linkList[keptLinks++] = l;
        }
        var joints = r.Count(64);
        if (joints > Construct.MaxJoints)
            throw new WorldFileException($"a construct of {joints} joints");
        var jointList = new CJoint[joints];
        for (var j = 0; j < joints; j++)
        {
            ref var jt = ref jointList[j];
            jt.Hinge = r.U16();
            jt.ParentSeg = r.U8();
            var type = r.U8();
            if (type > (byte)JointType.Swivel)
                throw new WorldFileException($"a joint of type {type}");
            jt.Type = (JointType)type;
            jt.Child = r.U16();
            var jf = r.U8();
            jt.Open = (jf & 1) != 0;
            jt.Locked = (jf & 2) != 0;
            jt.Wired = (jf & 4) != 0;
            jt.RefSet = (jf & 8) != 0;
            jt.Frozen = (jf & 16) != 0;
            jt.Pivot = r.Vec();
            jt.Axis = r.Vec();
            jt.Angle = jt.PrevAngle = r.F32();
            jt.Vel = r.F32();
            jt.Min = r.F32();
            jt.Max = r.F32();
            jt.Ref = r.F32();
            jt.FixRot = r.Quat();
            jt.FixPos = r.Vec();
            if (jt.Hinge >= n || jt.ParentSeg > j || jt.Child >= n && jt.Freeform || !float.IsFinite(jt.Angle + jt.Vel + jt.Ref))
                throw new WorldFileException("a joint that doesn't fit");
        }
        for (var i = 0; i < n; i++)
            if (parts[i].Seg > joints)
                throw new WorldFileException("a part on a joint that isn't there");
        var routes = r.Count(16);
        if (routes > Construct.MaxRoutes)
            throw new WorldFileException($"a construct of {routes} routed links");
        var routeList = new CRoute[routes];
        var nodeList = new List<CRouteNode>();
        for (var k = 0; k < routes; k++)
        {
            ref var rt = ref routeList[k];
            var type = r.U8();
            if (type >= (byte)RouteType.COUNT)
                throw new WorldFileException($"a routed link of type {type}");
            rt.Type = (RouteType)type;
            rt.From = r.U16();
            rt.To = r.U16();
            rt.FromPort = r.U8();
            rt.ToPort = r.U8();
            rt.Parent = r.U8();
            rt.ParentNode = r.U8();
            if (rt.ParentNode == LinkSave.NodeEscape)
                rt.ParentNode = r.U16();
            rt.Count = LinkSave.ReadNodeCount(r);
            rt.EndHealth = r.F32();
            rt.Paint = r.U32();
            rt.Timer = r.F32();
            rt.Cursor = r.U8();
            rt.Start = (ushort)nodeList.Count;
            if (rt.To >= n || (rt.Parent == 255 ? rt.From >= n : rt.Parent >= k || rt.ParentNode >= routeList[rt.Parent].Count) || rt.Count > Construct.MaxNodesPerRoute
                || nodeList.Count + rt.Count > Construct.MaxRouteNodes)
                throw new WorldFileException("a routed link that doesn't fit");
            for (var i = 0; i < rt.Count; i++)
            {
                var node = new CRouteNode { Host = r.U16(), Pos = r.Vec(), Health = r.F32() };
                if (node.Host >= n)
                    throw new WorldFileException("a routed link's node on a part that isn't there");
                nodeList.Add(node);
            }
        }
        var tracks = new List<(ushort[] Parts, float Width, float Link, float Tension, float Grip, float Health)>();
        var trackCount = r.Has(4) && r.Pos + 4 <= bodyEnd ? r.I32() : 0;
        if (trackCount < 0 || trackCount > Construct.MaxTracks)
            throw new WorldFileException($"a construct of {trackCount} tracks");
        for (var k = 0; k < trackCount; k++)
        {
            var count = r.U8();
            var (tw, tl, tt, tg, th) = (r.F32(), r.F32(), r.F32(), r.F32(), r.F32());
            if (count > Construct.MaxTrackWheels || !float.IsFinite(tw + tl + tt + tg + th))
                throw new WorldFileException("a track that doesn't fit");
            var wheels = new ushort[count];
            for (var i = 0; i < count; i++)
            {
                wheels[i] = r.U16();
                if (wheels[i] >= n)
                    throw new WorldFileException("a track on a part that isn't there");
            }
            tracks.Add((wheels, Math.Clamp(tw, ConstructTracks.MinWidth, ConstructTracks.MaxWidth), Math.Clamp(tl, ConstructTracks.MinLink, ConstructTracks.MaxLink), Math.Clamp(tt, 0, 1), Math.Clamp(tg, ConstructTracks.MinGrip, ConstructTracks.MaxGrip), th));
        }
        byte[]? briefBytes = null; // (a save before the behaviour brief ends at the tracks)
        if (r.Has(4) && r.Pos + 4 <= bodyEnd)
        {
            var len = r.I32();
            if (len < 0 || len > 1 << 20 || r.Pos + len > bodyEnd || !r.Has(len))
                throw new WorldFileException($"a behaviour brief of {len} bytes");
            if (len > 0)
                briefBytes = r.Bytes(len);
        }
        var conveyors = ConstructConveyor.ReadSave(r, bodyEnd, names); // (after the brief: an older save ends before it)
        var orders = conveyors != null ? WorkOrders.ReadSave(r, bodyEnd, names) : null; // (after the conveyors, the same way)
        var craft = orders != null ? ConstructCrafting.ReadSave(r, bodyEnd, names) : null; // (after the work orders, the same way)
        var stations = craft != null ? ConstructStation.ReadSave(r, bodyEnd) : null; // (after the crafting orders, the same way)
        (ushort Dest, ushort Tag, byte Flags)[]? logi = null; // (after the stations' jobs: the riders' destinations, ConstructLogistics.cs)
        ChainEntry[]? chains = null; // (and the chained orders' links: ConstructLogisticsPlan.cs)
        (ushort Part, ushort Kind)[]? targets = null; // (and its racks' stock targets: StockTargets.cs)
        if (stations != null && r.Has(8) && r.Pos + 8 <= bodyEnd)
        {
            var tag = r.U32();
            var len = r.I32();
            if (len < 0 || r.Pos + len > bodyEnd || !r.Has(len))
                throw new WorldFileException($"a logistics block of {len} bytes");
            var end = r.Pos + len;
            if (tag == LogiTag && len >= 4)
            {
                var m = r.I32();
                if (m < 0 || m > Construct.MaxConveyed || 4 + m * 5 > len)
                    throw new WorldFileException($"{m} rider destinations");
                logi = new (ushort, ushort, byte)[m];
                for (var i = 0; i < m; i++)
                    logi[i] = (r.U16(), r.U16(), r.U8());
                if (r.Pos + 4 <= end)
                {
                    var k = r.I32();
                    if (k < 0 || k > ConstructLogisticsPlan.MaxEntries || r.Pos + k * 16 > end)
                        throw new WorldFileException($"{k} chained orders");
                    chains = new ChainEntry[k];
                    for (var i = 0; i < k; i++)
                    {
                        chains[i] = new ChainEntry { Id = r.U16(), Maker = r.U16(), Asked = 0, Reason = "" };
                        var kind = r.U16();
                        chains[i].Kind = kind < names.Parts.Length ? names.Parts[kind] : ConstructParts.None;
                        (chains[i].Asked, chains[i].Made, chains[i].Dest, chains[i].Root, chains[i].RootId) = (r.U16(), r.U16(), r.U16(), r.U16(), r.U16());
                        (chains[i].Depth, chains[i].Class) = (1, ConstructLogisticsPlan.ClassNormal); // (a one-level save's links)
                    }
                    if (r.Pos + 4 <= end && r.I32() is var k2 && k2 == k && r.Pos + k * 9 <= end)
                        for (var i = 0; i < k; i++)
                            (chains[i].Parent, chains[i].Depth, chains[i].Class, chains[i].Stamp, chains[i].Flags) = (r.U16(), r.U8(), r.U8(), r.U32(), r.U8());
                    if (r.Pos + 4 <= end && r.I32() is var tn && tn >= 0 && tn <= global::CallOfSlop.StockTargets.MaxTargets && r.Pos + tn * 4 <= end)
                    {
                        targets = new (ushort, ushort)[tn];
                        for (var i = 0; i < tn; i++)
                        {
                            var tp = r.U16();
                            var tk = r.U16();
                            targets[i] = (tp, tk < names.Parts.Length ? names.Parts[tk] : ConstructParts.None);
                        }
                    }
                }
            }
            while (r.Pos < end)
                r.U8(); // (a newer block's rest)
        }
        var c = Create(pos, rot, freshId ? (ushort)0 : id) ?? throw new WorldFileException($"no room for construct {id}");
        id = c.Id;
        _partTally = -1;
        c.ReserveParts(n);
        parts.CopyTo(c.Parts, 0);
        c.PartCount = n;
        c.ReserveLinks(keptLinks);
        Array.Copy(linkList, c.Links, keptLinks);
        c.LinkCount = keptLinks;
        c.LinksChanged();
        c.ReserveJoints(joints);
        jointList.CopyTo(c.Joints, 0);
        c.JointCount = joints;
        c.ReserveRoutes(routes, nodeList.Count);
        routeList.CopyTo(c.Routes, 0);
        c.RouteCount = routes;
        nodeList.CopyTo(c.RouteNodes, 0);
        c.RouteNodeCount = nodeList.Count;
        if (conveyors != null)
            ConstructConveyor.Apply(c, conveyors);
        if (orders != null)
            WorkOrders.Apply(c, orders);
        if (craft != null)
            ConstructCrafting.Apply(this, c, craft);
        if (stations != null)
            ConstructStation.Apply(this, c, stations);
        c.Wreck = (flags & 1) != 0;
        c.Damaged = (flags & 4) != 0;
        c.Pinned = (flags & 8) != 0;
        c.Frozen = (flags & 16) != 0;
        c.Owner = owner(ownerRec);
        c.FrozenBy = owner(frozenRec);
        c.CarriedBy = carriedBy;
        c.CarryTag = carryTag;
        c.CarryPart = carryPart;
        c.CarrySeg = carrySeg;
        c.CarryPos = carryPos;
        c.CarryRot = carryRot;
        c.SpaceShip = ship;
        for (var i = 0; i < n; i++)
        {
            ref var p = ref c.Parts[i];
            if (p.D.Role != PartRole.Storage)
                p.Store = 0; // (a part that stopped being a container)
            else if (p.Store == 0 || p.Store > MaxStores || !Stores[p.Store].Live)
                AllocStore(ref p, p.D); // (a part that became one, or its store was unreadable)
            if (p.Burn > 0)
                c.Burning++;
        }
        if (logi != null && conveyors != null)
            for (var j = 0; j < conveyors.Index.Count && j < c.ConveyedCount; j++)
                if (conveyors.Index[j] < logi.Length && logi[conveyors.Index[j]] is var (lp, lt, lf))
                {
                    ref var cr = ref c.Conveyed[j];
                    cr.Dest = lp > 0 && lp <= n ? c.Parts[lp - 1].Store : (ushort)0; // (its destination's store as loaded)
                    (cr.Tag, cr.Flags) = (lt, cr.Dest != 0 || (lf & (byte)LogiFlags.Stuck) != 0 ? lf : (byte)0);
                }
        if (chains != null)
            ConstructLogisticsPlan.Apply(this, c, Array.FindAll(chains, e => e.Kind != ConstructParts.None));
        if (targets != null)
            global::CallOfSlop.StockTargets.Apply(this, c, targets);
        c.Root = root >= 0 && root < n ? root : 0;
        if (tracks.Count > 0)
            foreach (var t in tracks)
                ConstructTracks.Add(c, t.Parts, t.Width, t.Link, t.Tension, t.Grip, t.Health);
        c.Rebuild();
        c.Empty(); // (what follows is its fuel and charge, not a fresh part's)
        c.Fuel = fuel;
        c.Charge = float.IsFinite(charge) ? Math.Clamp(charge, 0, c.ChargeCapacity) : 0;
        c.Gear = Math.Max(0, gear);
        ConstructNet.SetPower(c, ConstructNet.PowerFlags(c), (byte)c.Gear);
        c.HoverOn = (flags & 32) != 0;
        c.HoverY = hoverY;
        c.Asleep = (flags & 2) != 0;
        c.Vel = vel;
        c.AngVel = ang;
        var doomed = 0;
        foreach (var gone in stand)
            doomed += gone ? 1 : 0;
        if (doomed > 0)
        {
            RemoveParts(c, stand);
            if (Get(id) is { Dirty: true } left)
                left.Rebuild();
        }
        if (briefBytes != null)
            LoadBrief(c, briefBytes);
        return c;
    }

    /// <summary>A body's behaviour brief from its bytes: a brief that doesn't read is left off (logged), never the body.</summary>
    private static void LoadBrief(Construct c, byte[] bytes)
    {
        try
        {
            VehicleKit.VehicleBrief.Read(new SaveReader(bytes))?.Loaded(c);
        }
        catch (WorldFileException ex)
        {
            Console.WriteLine($"[world] construct {c.Id}'s behaviour brief didn't read ({ex.Message}): left off");
        }
    }

    private static ConstructPartDef? _standIn;

    /// <summary>What a part this build no longer has stands in as while its construct loads.</summary>
    private static ConstructPartDef StandIn => _standIn ??= ConstructParts.All.First(d => d.Role == PartRole.Frame && !d.HasJoint);

    /// <summary>A link on a socket the part doesn't have (a merged receiver's centre sockets sit past the end of its list).</summary>
    private static bool SocketGone(in CPart p, byte socket) => socket >= p.D.Sockets.Length && !p.D.IsReceiverGun;

    /// <summary>The ids and tags a loaded world must carry on from.</summary>
    public void AfterLoad(ushort nextTag)
    {
        _nextTag = nextTag;
        LoadChanged = true;
        CargoScanNow = true;
        Beacons.Dirty = true;
        LinksDirty = WorldLinksDirty = true;
    }

    /// <summary>The links between constructs from a save.</summary>
    public static void WriteLinks(SaveWriter w, WorldSnapshot s)
    {
        w.I32(s.CouplingCount);
        foreach (ref var c in s.Couplings.AsSpan(0, s.CouplingCount))
            w.U16(c.TagA).U16(c.TagB).U16(c.BodyA).U16(c.BodyB).U16(c.PartA).U16(c.PartB).F32(c.Length);
        w.I32(s.XWireCount);
        foreach (ref var x in s.XWires.AsSpan(0, s.XWireCount))
            w.U16(x.TagFrom).U16(x.TagTo).U16(x.BodyFrom).U16(x.BodyTo).U16(x.From).U16(x.To).F32(x.Value);
        w.I32(s.WorldLinkCount);
        foreach (ref var l in s.WorldLinks.AsSpan(0, s.WorldLinkCount))
        {
            w.U8((byte)l.Type).U16(l.TagFrom).U16(l.TagTo).U16(l.BodyFrom).U16(l.BodyTo).U16(l.From).U16(l.To).U8(l.FromPort).U8(l.ToPort);
            LinkSave.WriteNodeCount(w, l.Count);
            w.Vec(l.MadeFrom).Vec(l.MadeTo).Quat(l.RotFrom).Quat(l.RotTo).F32(l.Timer);
            foreach (ref var n in s.WorldNodes.AsSpan(l.Start, l.Count))
                w.Vec(n.Pos).U16(n.Tag).U16(n.Body).U16(n.Part).Vec(n.Made).Vec(n.Support);
        }
        w.I32(s.HoseCount);
        foreach (ref var h in s.Hoses.AsSpan(0, s.HoseCount))
            w.U16(h.TagOut).U16(h.TagIn).U16(h.BodyOut).U16(h.BodyIn).U16(h.PartOut).U16(h.PartIn).U8(h.Holder).F32(h.Value).F32(h.Timer).U8(h.Cursor);
    }

    public void ReadLinks(SaveReader r)
    {
        var n = r.Count(16);
        CouplingCount = 0;
        for (var k = 0; k < n; k++)
        {
            var cp = new CCoupling { TagA = r.U16(), TagB = r.U16(), BodyA = r.U16(), BodyB = r.U16(), PartA = r.U16(), PartB = r.U16(), Length = r.F32() };
            cp.SentLength = cp.Length;
            if (CouplingCount < MaxCouplings)
                Couplings[CouplingCount++] = cp;
        }
        n = r.Count(16);
        XWireCount = 0;
        for (var k = 0; k < n; k++)
        {
            var x = new CXWire { TagFrom = r.U16(), TagTo = r.U16(), BodyFrom = r.U16(), BodyTo = r.U16(), From = r.U16(), To = r.U16(), Value = r.F32() };
            if (XWireCount < MaxXWires)
                XWires[XWireCount++] = x;
        }
        n = r.Count(16);
        WorldLinkCount = WorldNodeCount = 0;
        for (var k = 0; k < n; k++)
        {
            var type = r.U8();
            if (type >= (byte)RouteType.COUNT)
                throw new WorldFileException($"a world link of type {type}");
            var l = new WLink
            {
                Type = (RouteType)type, TagFrom = r.U16(), TagTo = r.U16(), BodyFrom = r.U16(), BodyTo = r.U16(), From = r.U16(), To = r.U16(),
                FromPort = r.U8(), ToPort = r.U8(), Count = LinkSave.ReadNodeCount(r), MadeFrom = r.At(), MadeTo = r.At(), RotFrom = r.Quat(), RotTo = r.Quat(), Timer = r.F32(),
                Start = (ushort)WorldNodeCount,
            };
            var fits = WorldLinkCount < MaxWorldLinks && WorldNodeCount + l.Count <= MaxWorldNodes && l.Count <= Construct.MaxNodesPerRoute;
            if (fits)
                EnsureWorldNodes(WorldNodeCount + l.Count);
            for (var i = 0; i < l.Count; i++)
            {
                var node = new WNode { Pos = r.Vec(), Tag = r.U16(), Body = r.U16(), Part = r.U16(), Made = r.At(), Support = r.Vec() };
                if (node.Body == 0)
                    node.Pos += r.Shift; // (a world node; a body's is in its rest frame)
                if (fits)
                    WorldNodes[WorldNodeCount++] = node;
            }
            if (fits)
                WorldLinks[WorldLinkCount++] = l;
        }
        n = r.Count(16);
        HoseCount = 0;
        for (var k = 0; k < n; k++)
        {
            var h = new CHose { TagOut = r.U16(), TagIn = r.U16(), BodyOut = r.U16(), BodyIn = r.U16(), PartOut = r.U16(), PartIn = r.U16(), Holder = r.U8(), Value = r.F32(), Timer = r.F32(), Cursor = r.U8() };
            var held = h.Holder != 255;
            h.Holder = 255; // (nobody holds anything as a world loads)
            if (held && h.BodyIn == 0 || HoseCount >= MaxHoses)
                continue; // (a receiver in someone's hands, plugged into nothing: back on its reel)
            Hoses[HoseCount++] = h;
        }
    }

    /// <summary>Ropes and springs in a save.</summary>
    public static void WriteConnectors(SaveWriter w, WorldSnapshot s)
    {
        w.U16(s.NextConnector).I32(s.ConnectorCount);
        foreach (ref var c in s.Connectors.AsSpan(0, s.ConnectorCount))
        {
            w.U16(c.Id).U8((byte)c.Kind).U8(c.Size).F32(c.Length).F32(c.Rating).F32(c.Stiffness).F32(c.Damping);
            WriteEnd(w, c.A);
            WriteEnd(w, c.B);
        }

        static void WriteEnd(SaveWriter w, in ConnectorEnd e) => w.U16(e.Tag).U16(e.Body).U16(e.Part).U8((byte)(e.OnWreck ? 1 : 0)).Vec(e.Local).Vec(e.Support);
    }

    public void ReadConnectors(SaveReader r)
    {
        _nextConnector = r.U16();
        var n = r.Count(16);
        ConnectorCount = 0;
        for (var k = 0; k < n; k++)
        {
            var c = new CConnector { Id = r.U16(), Kind = (ConnectorKind)r.U8(), Size = r.U8(), Length = r.F32(), Rating = r.F32(), Stiffness = r.F32(), Damping = r.F32() };
            c.A = ReadEnd(r);
            c.B = ReadEnd(r);
            if (c.Kind >= ConnectorKind.COUNT || c.Size >= ConnectorSizes.Length)
                throw new WorldFileException($"a connector of kind {c.Kind}, size {c.Size}");
            if (ConnectorCount >= MaxConnectors)
                continue;
            if (ConnectorCount == Connectors.Length)
                Array.Resize(ref Connectors, Math.Min(Connectors.Length * 2, MaxConnectors));
            EndPoint(c.A, out c.LastA);
            EndPoint(c.B, out c.LastB);
            Connectors[ConnectorCount++] = c;
        }
        ConnectorsAllDirty = true;

        static ConnectorEnd ReadEnd(SaveReader r)
        {
            var e = new ConnectorEnd { Tag = r.U16(), Body = r.U16(), Part = r.U16(), OnWreck = r.U8() != 0, Local = r.Vec(), Support = r.Vec() };
            if (e.Body == 0)
                e.Local += r.Shift; // (an end on the world is a point in it)
            return e;
        }
    }

    /// <summary>After a load that left parts out: the links find their parts again, and those whose part went go now, not on the first tick.</summary>
    public void RelinkAfterSkips()
    {
        for (var k = CouplingCount - 1; k >= 0; k--)
        {
            ref var cp = ref Couplings[k];
            if (Resolve(cp.TagA, ref cp.BodyA, ref cp.PartA) == null || Resolve(cp.TagB, ref cp.BodyB, ref cp.PartB) == null)
                Couplings[k] = Couplings[--CouplingCount];
        }
        for (var k = ConnectorCount - 1; k >= 0; k--)
        {
            ref var cn = ref Connectors[k];
            if (cn.A.Tag != 0 && Resolve(cn.A.Tag, ref cn.A.Body, ref cn.A.Part) == null || cn.B.Tag != 0 && Resolve(cn.B.Tag, ref cn.B.Body, ref cn.B.Part) == null)
                Connectors[k] = Connectors[--ConnectorCount];
        }
        RelinkNoCollides();
        for (var k = XWireCount - 1; k >= 0; k--)
        {
            ref var x = ref XWires[k];
            if (Resolve(x.TagFrom, ref x.BodyFrom, ref x.From) == null || Resolve(x.TagTo, ref x.BodyTo, ref x.To) == null)
                XWires[k] = XWires[--XWireCount];
        }
        for (var k = HoseCount - 1; k >= 0; k--)
        {
            ref var h = ref Hoses[k];
            if (Resolve(h.TagOut, ref h.BodyOut, ref h.PartOut) == null)
                Hoses[k] = Hoses[--HoseCount];
            else if (h.TagIn != 0 && Resolve(h.TagIn, ref h.BodyIn, ref h.PartIn) == null)
                (h.TagIn, h.BodyIn, h.PartIn) = (0, 0, 0);
        }
        for (var k = 0; k < WorldLinkCount; k++)
        {
            ref var l = ref WorldLinks[k];
            if (Resolve(l.TagFrom, ref l.BodyFrom, ref l.From) == null || Resolve(l.TagTo, ref l.BodyTo, ref l.To) == null)
            {
                continue; // (the step's own check snaps a link with a lost end)
            }
            for (var i = 0; i < l.Count; i++)
            {
                ref var node = ref WorldNodes[l.Start + i];
                if (node.Tag != 0)
                    Resolve(node.Tag, ref node.Body, ref node.Part);
            }
        }
        LinksDirty = WorldLinksDirty = true;
    }
}

/// <summary>
/// A link's node counts in a world save: a byte, as saves always had them (links once had at most 32 nodes, so every old file
/// reads the same), or an escape byte and a u16 for a long link. A root link's parent node is 255, so the escape there is 254.
/// </summary>
internal static class LinkSave
{
    public const byte NodeEscape = 254;
    private const byte CountEscape = 255;

    public static void WriteNodeCount(SaveWriter w, int count)
    {
        if (count < CountEscape)
            w.U8((byte)count);
        else
            w.U8(CountEscape).U16((ushort)count);
    }

    public static ushort ReadNodeCount(SaveReader r)
    {
        var b = r.U8();
        return b == CountEscape ? r.U16() : b;
    }
}
