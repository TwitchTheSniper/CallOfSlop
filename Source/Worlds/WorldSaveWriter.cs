using System.Diagnostics;

namespace CallOfSlop;

/// <summary>
/// A world's file from a <see cref="WorldSnapshot"/>, on the worker thread: the body's sections, packed, written atomically.
/// It reads only the snapshot and what never changes after load (the part defs; an extrusion's def is added once and stays).
/// Sections carry a tag and a length, so a later format can add one and this build's reader skips what it doesn't know.
/// </summary>
public static class WorldSaveWriter
{
    public const uint Names = 0x53454D41, Players = 0x53594C50, Stores = 0x53524F54, Bodies = 0x53594442, Links = 0x534B4E4C, Ground = 0x53444E47, Resources = 0x53435352, Space = 0x53435053, Misc = 0x4353494D;

    /// <summary>Makes and writes the file. Null when it's on disk, else why not.</summary>
    public static string? Run(string folder, WorldSnapshot s)
    {
        try
        {
            var clock = Stopwatch.StartNew();
            var body = Body(s);
            var made = clock.Elapsed.TotalMilliseconds;
            var file = WorldFile.Pack(s.Meta, body);
            var bodyLength = body.Length;
            var packed = clock.Elapsed.TotalMilliseconds;
            var why = WorldStore.Write(folder, file);
            Console.WriteLine($"[worlds] {s.Meta.Name}: {s.Meta.Bodies} constructs, {s.Meta.Parts} parts: tick copy {s.CaptureMs:0.00} ms, bytes {made:0} ms, pack {packed - made:0} ms, write {clock.Elapsed.TotalMilliseconds - packed:0} ms, {bodyLength / 1024} KB -> {file.Length / 1024} KB{(why == null ? "" : $", FAILED: {why}")}");
            return why;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Console.WriteLine($"[worlds] {s.Meta.Name}: the save failed: {ex}");
            return ex.Message;
        }
    }

    /// <summary>The whole body: the names first (made while the rest is written), then the sections.</summary>
    public static ReadOnlySpan<byte> Body(WorldSnapshot s)
    {
        var names = new WorldNames();
        var w = s.Scratch ??= new SaveWriter(s.PartCount * 72 + (1 << 16)); // (kept between saves: a big world's bytes aren't garbage every 5 minutes)
        var head = s.Head ??= new SaveWriter(s.PartCount * 72 + (1 << 16));
        w.Clear();
        head.Clear();
        w.Section(Misc, () => w.U16(s.NextTag));
        w.Section(Players, () =>
        {
            w.I32(s.Players.Length);
            foreach (var p in s.Players)
                WritePlayer(w, p, names);
        });
        w.Section(Stores, () =>
        {
            w.I32(s.StoreCount);
            for (var k = 0; k < s.StoreCount; k++)
                ConstructWorld.WriteStore(w, s.Stores[k], names);
        });
        w.Section(Bodies, () =>
        {
            w.I32(s.BodyCount);
            for (var k = 0; k < s.BodyCount; k++)
            {
                var at = w.Length;
                w.I32(0);
                ConstructWorld.WriteBody(w, s, s.Bodies[k], names);
                w.PatchI32(at, w.Length - at - 4);
            }
        });
        w.Section(Links, () => WriteLinks(w, s));
        w.Section(Ground, () =>
        {
            w.U8((byte)(s.GroundOnPlanet ? 1 : 0)).I32(s.OpCount);
            foreach (ref var op in s.Ops.AsSpan(0, s.OpCount))
                w.U8((byte)op.Kind).Vec(op.At).F32(op.Radius);
        });
        if (s.Resources is { } field)
            w.Section(Resources, () =>
            {
                w.U32(field.Seed).I32(field.Deposits).I64(field.Clock);
                w.I32(field.Worked.Length);
                foreach (var (index, left) in field.Worked)
                    w.I32(index).I32(left);
                w.I32(field.Felled.Length);
                foreach (var key in field.Felled)
                    w.U32(key);
                w.I32(field.Regrow.Length);
                foreach (var (key, due) in field.Regrow)
                    w.U32(key).I64(due);
                w.I32(field.Cut.Length);
                foreach (var (key, done) in field.Cut)
                    w.U32(key).I32(done);
            });
        if (s.SpaceSystem.Length > 0)
            w.Section(Space, () =>
            {
                w.Str(s.SpaceSystem).I64(s.SpaceNs).I32(s.SpaceWarp).I32(s.ShipCount);
                foreach (ref var ship in s.Ships.AsSpan(0, s.ShipCount))
                    w.U16(ship.Id).I32(ship.Primary).F64(ship.R.X).F64(ship.R.Y).F64(ship.R.Z).F64(ship.V.X).F64(ship.V.Y).F64(ship.V.Z);
            });
        head.Section(Names, () => names.Write(head));
        head.Bytes(w.Span);
        return head.Span;
    }

    private static void WriteLinks(SaveWriter w, WorldSnapshot s) => ConstructWorld.WriteLinks(w, s);

    private static void WritePlayer(SaveWriter w, WorldPlayer p, WorldNames names)
    {
        w.U64(p.Key).U64(p.SteamId).Str(p.Name).U8((byte)(p.HasPlace ? 1 : 0)).Vec(p.Pos).F32(p.Yaw).F32(p.Pitch).F32(p.Health);
        w.Str(p.Tool.ToString()).U16(names.Part(p.Held));
        w.I32(p.Parts.Length);
        foreach (var (def, count) in p.Parts)
            w.U16(names.Part(def)).U16(count);
        w.I32(p.Materials.Length);
        foreach (var (material, count) in p.Materials)
            w.U8(material).U16(count);
        ConstructWorld.WriteStore(w, p.Pack, names);
        w.I32(p.Builds.Length);
        foreach (var b in p.Builds)
            w.Str(b);
    }

    /// <summary>A player's record from <see cref="WritePlayer"/>; what no longer exists is left out.</summary>
    public static WorldPlayer ReadPlayer(SaveReader r, WorldNamesRead names)
    {
        var p = new WorldPlayer { Key = r.U64(), SteamId = r.U64(), Name = r.Str(), HasPlace = r.U8() != 0, Pos = r.Vec(), Yaw = r.F32(), Pitch = r.F32(), Health = r.F32() };
        if (!float.IsFinite(p.Pos.X + p.Pos.Y + p.Pos.Z))
            p.HasPlace = false;
        p.Tool = Enum.TryParse<ConstructTool>(r.Str(), out var tool) ? tool : ConstructTool.None;
        p.Held = names.Part(r.U16(), "a player's hands");
        var n = r.Count(4);
        var parts = new List<(ushort, ushort)>();
        for (var k = 0; k < n; k++)
        {
            var def = names.Part(r.U16(), "an inventory");
            var count = r.U16();
            if (def != ConstructParts.None && def < SandboxInventory.MaxKinds)
                parts.Add((def, count)); // (an extrusion is a body, never counted: its id is past the inventory's)
        }
        p.Parts = parts.ToArray();
        n = r.Count(3);
        var mats = new List<(byte, ushort)>();
        for (var k = 0; k < n; k++)
        {
            var m = names.Material(r.U8());
            var count = r.U16();
            if (m != 0)
                mats.Add((m, count));
        }
        p.Materials = mats.ToArray();
        var handle = r.U16();
        ConstructWorld.ReadStore(r, p.Pack, names);
        p.Pack.Handle = handle;
        n = r.Count(4);
        p.Builds = new string[n];
        for (var k = 0; k < n; k++)
            p.Builds[k] = r.Str();
        return p;
    }
}
