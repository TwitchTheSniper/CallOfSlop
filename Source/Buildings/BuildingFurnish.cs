using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The furnishing half of <see cref="BuildingGenerator"/>: a room's explicit "props", then its type's rule
/// (Buildings/Furnishing/&lt;type&gt;.json) item by item on a 0.1 m occupancy grid of the room's floor. Doorways (1.25 m
/// deep), the walkways between a room's doors, and the stairs and their landings are reserved first, so solid furniture
/// never blocks a way through; tall things never cover a window; everything stays inside the room. Deterministic per
/// building seed and room.
/// </summary>
internal sealed partial class BuildingGenerator
{
    private const float Cell = 0.1f;

    /// <summary>A wall face of a room (the inside face of a wall segment next to it).</summary>
    private readonly record struct Face(bool AlongX, float At, int Dir, float A, float B, Seg S)
    {
        /// <summary>The yaw (degrees) of a prop standing with its back to this face, looking into the room (props face +Z).</summary>
        public float Yaw => AlongX ? (Dir > 0 ? 0f : 180f) : (Dir > 0 ? 90f : 270f);
    }

    /// <summary>One placed piece of furniture (building frame).</summary>
    private sealed record Piece(PropDef Def, Vector2 Pos, float Yaw, float Scale, float Y, BRect Fp);

    /// <summary>The room's floor as 0.1 m cells: 0 free, 1 outside or wall, 2 kept clear, 3 taken. A second layer for flat things (rugs).</summary>
    private sealed class Grid
    {
        public readonly float X0, Z0;
        public readonly int Nx, Nz;
        public readonly byte[] Solid, Flat;

        public Grid(BRect bounds)
        {
            X0 = bounds.X0;
            Z0 = bounds.Z0;
            Nx = Math.Max(1, (int)MathF.Ceiling(bounds.W / Cell));
            Nz = Math.Max(1, (int)MathF.Ceiling(bounds.D / Cell));
            Solid = new byte[Nx * Nz];
            Flat = new byte[Nx * Nz];
            Array.Fill(Solid, (byte)1);
            Array.Fill(Flat, (byte)1);
        }

        public float Cx(int i) => X0 + (i + 0.5f) * Cell;
        public float Cz(int k) => Z0 + (k + 0.5f) * Cell;

        /// <summary>The cells whose centres lie inside <paramref name="r"/>.</summary>
        public (int I0, int I1, int K0, int K1) Range(BRect r)
        {
            var i0 = Math.Max(0, (int)MathF.Ceiling((r.X0 - X0) / Cell - 0.5f));
            var i1 = Math.Min(Nx - 1, (int)MathF.Floor((r.X1 - X0) / Cell - 0.5f));
            var k0 = Math.Max(0, (int)MathF.Ceiling((r.Z0 - Z0) / Cell - 0.5f));
            var k1 = Math.Min(Nz - 1, (int)MathF.Floor((r.Z1 - Z0) / Cell - 0.5f));
            return (i0, i1, k0, k1);
        }

        /// <summary>
        /// Marks cells: those whose centres lie inside <paramref name="r"/>, or (<paramref name="touching"/>) every cell it touches,
        /// so nothing whose cells are free can overlap it by more than half a cell.
        /// </summary>
        public void Set(BRect r, byte v, bool onlyFree, byte[] layer, bool touching = false)
        {
            var (i0, i1, k0, k1) = touching ? Touching(r) : Range(r);
            for (var k = k0; k <= k1; k++)
                for (var i = i0; i <= i1; i++)
                    if (!onlyFree || layer[k * Nx + i] == 0)
                        layer[k * Nx + i] = v;
        }

        private (int I0, int I1, int K0, int K1) Touching(BRect r)
        {
            var i0 = Math.Max(0, (int)MathF.Floor((r.X0 - X0) / Cell + 0.001f));
            var i1 = Math.Min(Nx - 1, (int)MathF.Ceiling((r.X1 - X0) / Cell - 0.001f) - 1);
            var k0 = Math.Max(0, (int)MathF.Floor((r.Z0 - Z0) / Cell + 0.001f));
            var k1 = Math.Min(Nz - 1, (int)MathF.Ceiling((r.Z1 - Z0) / Cell - 0.001f) - 1);
            return (i0, i1, k0, k1);
        }

        /// <summary>Does a footprint fit: inside the bounds and every cell under it free (flat: not outside, flat layer free)?</summary>
        public bool Fits(BRect r, bool flat)
        {
            if (r.X0 < X0 - 0.001f || r.Z0 < Z0 - 0.001f || r.X1 > X0 + Nx * Cell + 0.001f || r.Z1 > Z0 + Nz * Cell + 0.001f)
                return false;
            var (i0, i1, k0, k1) = Range(r);
            if (i1 < i0 || k1 < k0)
                return false;
            for (var k = k0; k <= k1; k++)
                for (var i = i0; i <= i1; i++)
                {
                    var j = k * Nx + i;
                    if (flat ? Solid[j] == 1 || Flat[j] != 0 : Solid[j] != 0)
                        return false;
                }
            return true;
        }

        /// <summary>Could a player stand there: every cell inside the room and not under furniture?</summary>
        public bool Walkable(BRect r)
        {
            var (i0, i1, k0, k1) = Range(r);
            if (i1 < i0 || k1 < k0)
                return false;
            for (var k = k0; k <= k1; k++)
                for (var i = i0; i <= i1; i++)
                    if (Solid[k * Nx + i] is 1 or 3)
                        return false;
            return true;
        }

        /// <summary>
        /// <see cref="Fits"/>, and no other furniture within <paramref name="gap"/> of it (walls and kept-clear cells may be
        /// that close: furniture stands against walls and beside doorways).
        /// </summary>
        public bool FitsApart(BRect r, float gap, bool flat)
        {
            if (!Fits(r, flat))
                return false;
            if (gap <= 0)
                return true;
            var layer = flat ? Flat : Solid;
            var (i0, i1, k0, k1) = Range(r.Inset(-gap));
            for (var k = k0; k <= k1; k++)
                for (var i = i0; i <= i1; i++)
                    if (layer[k * Nx + i] == 3)
                        return false;
            return true;
        }

        public byte At(float x, float z)
        {
            var i = (int)MathF.Floor((x - X0) / Cell);
            var k = (int)MathF.Floor((z - Z0) / Cell);
            return i < 0 || k < 0 || i >= Nx || k >= Nz ? (byte)1 : Solid[k * Nx + i];
        }
    }

    /// <summary>The room's record without furniture (variant "furnish": false).</summary>
    private void PlanRoomOnly(int f, int r) => RoomRecord(f, r, Pois(f, r), null);

    private PlannedRoom RoomRecord(int f, int r, List<Vector2> pois, Grid? grid)
    {
        var room = _floors[f].Rooms[r];
        var floorY = _level[f];
        var ceil = _level[f + 1] - Slab;
        var pr = new PlannedRoom { Id = room.Id, Type = room.Type, Floor = f, FloorY = floorY, CeilingY = ceil };
        foreach (var rect in room.Rects)
        {
            var i = rect.Inset(_t / 2);
            pr.Inside.Add(BoxW(i.X0, floorY, i.Z0, i.X1, ceil, i.Z1));
        }
        foreach (var z in DoorZones(f, r))
            pr.DoorZones.Add(BoxW(z.X0, floorY, z.Z0, z.X1, floorY + 2f, z.Z1));
        Vector2 wp;
        if (pois.Count > 0)
            wp = pois[0];
        else
        {
            // No doorway or stairs into it (an open-plan room reached through its neighbour): the free spot nearest its middle.
            var big = room.Rects.OrderByDescending(x => x.Area).First();
            wp = big.Center;
            if (grid != null)
            {
                var best = float.MaxValue;
                for (var k = 0; k < grid.Nz; k++)
                    for (var i = 0; i < grid.Nx; i++)
                    {
                        var v = grid.Solid[k * grid.Nx + i];
                        if (v is 1 or 3)
                            continue;
                        var p = new Vector2(grid.Cx(i), grid.Cz(k));
                        if (!grid.Walkable(new BRect(p.X - 0.45f, p.Y - 0.45f, p.X + 0.45f, p.Y + 0.45f)))
                            continue;
                        var d = Vector2.DistanceSquared(p, big.Center);
                        if (d < best)
                        {
                            best = d;
                            wp = p;
                        }
                    }
            }
            _plan.Waypoints.Add(P(wp.X, floorY, wp.Y));
        }
        pr.Waypoint = P(wp.X, floorY, wp.Y);
        _plan.Rooms.Add(pr);
        return pr;
    }

    private List<Face> Faces(int f, int r)
    {
        var list = new List<Face>();
        foreach (var s in _segs[f])
        {
            if (s.Neg == r)
                list.Add(new Face(s.AlongX, s.C - _t / 2, -1, s.A, s.B, s));
            if (s.Pos == r)
                list.Add(new Face(s.AlongX, s.C + _t / 2, 1, s.A, s.B, s));
        }
        return list;
    }

    /// <summary>The 1.25 m deep strips inside room <paramref name="r"/> in front of its doors and arches.</summary>
    private List<BRect> DoorZones(int f, int r)
    {
        var list = new List<BRect>();
        foreach (var a in _openings)
        {
            if (a.Floor != f || a.O.Kind is not (OpeningKind.Door or OpeningKind.Arch) || a.S.Neg != r && a.S.Pos != r)
                continue;
            var dir = a.S.Pos == r ? 1f : -1f;
            var face = a.S.C + dir * _t / 2;
            var far = face + dir * BuildingGen.DoorClearDepth;
            var half = a.O.Width / 2 + 0.2f;
            var (c0, c1) = (MathF.Min(face, far), MathF.Max(face, far));
            list.Add(a.S.AlongX ? new BRect(a.Along - half, c0, a.Along + half, c1) : new BRect(c0, a.Along - half, c1, a.Along + half));
        }
        return list;
    }

    /// <summary>Points the room must stay connected through: 0.9 m inside each doorway, and the stairs' feet and tops in it.</summary>
    private List<Vector2> Pois(int f, int r)
    {
        var list = new List<Vector2>();
        foreach (var a in _openings)
        {
            if (a.Floor != f || a.O.Kind is not (OpeningKind.Door or OpeningKind.Arch) || a.S.Neg != r && a.S.Pos != r)
                continue;
            var dir = a.S.Pos == r ? 1f : -1f;
            var c = a.S.C + dir * 0.9f;
            list.Add(a.S.AlongX ? new Vector2(a.Along, c) : new Vector2(c, a.Along));
        }
        foreach (var (_, sf, _, bottom, top) in _stairPlans)
        {
            if (sf == f && RoomAt(f, bottom.X, bottom.Y) == r)
                list.Add(bottom);
            if (sf + 1 == f && RoomAt(f, top.X, top.Y) == r)
                list.Add(top);
        }
        foreach (var (pf, bottom) in _platformStairs)
            if (pf == f && RoomAt(f, bottom.X, bottom.Y) == r)
                list.Add(bottom);
        return list;
    }

    private void Furnish(int f, int r)
    {
        var room = _floors[f].Rooms[r];
        var pois = Pois(f, r);
        var bounds = room.Rects.Aggregate((a, b) => a.Union(b));
        var grid = new Grid(bounds);
        foreach (var rect in room.Rects)
            grid.Set(rect, 0, false, grid.Solid);
        // Walls (any wall on this floor, so a neighbour's wall poking in is respected too).
        foreach (var s in _segs[f])
        {
            var band = s.AlongX ? new BRect(s.A - _t / 2, s.C - _t / 2 - 0.03f, s.B + _t / 2, s.C + _t / 2 + 0.03f)
                : new BRect(s.C - _t / 2 - 0.03f, s.A - _t / 2, s.C + _t / 2 + 0.03f, s.B + _t / 2);
            grid.Set(band, 1, false, grid.Solid);
        }
        for (var j = 0; j < grid.Solid.Length; j++)
            grid.Flat[j] = grid.Solid[j] == 1 ? (byte)1 : (byte)0;
        foreach (var z in DoorZones(f, r))
            grid.Set(z, 2, true, grid.Solid, touching: true);
        foreach (var z in _reserved[f])
            grid.Set(z, 2, true, grid.Solid, touching: true);
        // Walkways between the room's doorways and stairs, a metre wide, so a room with two ways in never gets closed off.
        for (var i = 1; i < pois.Count; i++)
            Walkway(grid, pois[0], pois[i]);

        var (placedCount, skipped) = FurnishItems(f, r, grid, pois, bounds);
        var pr = RoomRecord(f, r, pois, grid);
        pr.Placed = placedCount;
        pr.Skipped = skipped;
        if (skipped > 0)
            _plan.Notes.Add($"{_def.Name}: room \"{room.Id}\" (floor {f}): {skipped} furnishing item(s) found no space");
    }

    private int _furnishFloor;

    private (int Placed, int Skipped) FurnishItems(int f, int r, Grid grid, List<Vector2> pois, BRect bounds)
    {
        _furnishFloor = f;
        var room = _floors[f].Rooms[r];
        var placedCount = 0;
        var skipped = 0;
        var faces = Faces(f, r);
        var windows = new List<(int Face, float A, float B, float Sill)>();
        foreach (var a in _openings)
        {
            if (a.Floor != f || a.O.Kind is not (OpeningKind.Window or OpeningKind.Hole))
                continue;
            for (var i = 0; i < faces.Count; i++)
                if (faces[i].S == a.S)
                    windows.Add((i, a.Along - a.O.Width / 2, a.Along + a.O.Width / 2, a.Bottom));
        }
        var floorY = _level[f];
        var ceil = _level[f + 1] - Slab;
        var rng = new Foliage.Rng(Foliage.Hash((uint)_seed * 2246822519u ^ Fnv(room.Id) ^ (uint)(f * 7919) ^ (uint)room.Seed * 3266489917u));
        var placed = new Dictionary<string, List<Piece>>(StringComparer.OrdinalIgnoreCase);

        // Explicit props first (the author's say), then the rule.
        foreach (var p in room.Props)
        {
            var def = _def.FindProp(p.Prop, _scope) ?? throw new BuildingFileException($"{Where(f)}, room \"{room.Id}\": unknown prop \"{p.Prop}\" ({_scope.Suggest(p.Prop)})");
            Vector2 pos;
            float yaw;
            if (p.Wall != 0)
            {
                var (alongX, at, dir) = p.Wall switch
                {
                    'N' => (true, bounds.Z0 + _t / 2, 1),
                    'S' => (true, bounds.Z1 - _t / 2, -1),
                    'W' => (false, bounds.X0 + _t / 2, 1),
                    _ => (false, bounds.X1 - _t / 2, -1),
                };
                var face = new Face(alongX, at, dir, 0, 0, null!);
                yaw = face.Yaw + p.Yaw;
                var back = -def.Bounds.Min.Z * p.Scale + 0.04f;
                var along = (alongX ? bounds.Center.X : bounds.Center.Y) + p.Along;
                var across = at + dir * back;
                pos = alongX ? new Vector2(along, across) : new Vector2(across, along);
            }
            else
            {
                pos = bounds.Center + new Vector2(p.X, p.Z);
                yaw = p.Yaw;
            }
            var fp = Footprint(def, pos, yaw, p.Scale);
            grid.Set(fp, 3, false, IsFlat(def, p.Scale) ? grid.Flat : grid.Solid, touching: true);
            Emit(new Piece(def, pos, yaw, p.Scale, p.Y, fp), room.Id, floorY);
            placedCount++;
        }
        if (room.Furnish != FurnishMode.Auto)
            return (placedCount, skipped);
        var rule = _def.Rule(room.Rules ?? room.Type); // the building's own rule first, then the shared one
        if (rule == null)
            return (placedCount, skipped); // a room type without a rule (a corridor, a stair hall) stays bare
        var area = room.Rects.Sum(x => x.Area);
        foreach (var item in rule.Items)
        {
            if (area < item.MinArea || area > item.MaxArea)
                continue;
            if (item.Chance < 1f && rng.Next() >= item.Chance)
                continue;
            var count = item.CountMin + (int)MathF.Floor(rng.Next() * (item.CountMax - item.CountMin + 1));
            count = Math.Min(count, item.CountMax);
            var list = item.Id.Length > 0 ? placed[item.Id] = new List<Piece>() : null;
            var parents = item.Of.Length > 0 && placed.TryGetValue(item.Of, out var pl) ? pl : null;
            for (var k = 0; k < count; k++)
            {
                var name = item.Props[Math.Min(item.Props.Length - 1, (int)(rng.Next() * item.Props.Length))];
                var def = _def.FindProp(name, _scope) ?? throw new BuildingFileException($"{rule.SourceFile}: unknown prop \"{name}\" ({_scope.Suggest(name)}) (furnishing building \"{_def.Name}\")");
                var piece = Place(item, def, k, grid, faces, windows, pois, parents, bounds, ceil - floorY, ref rng);
                if (piece == null)
                {
                    skipped++;
                    continue;
                }
                if (item.Place is not (FurnishPlace.On or FurnishPlace.Ceiling))
                    grid.Set(piece.Fp, 3, false, IsFlat(def, piece.Scale) ? grid.Flat : grid.Solid, touching: true);
                list?.Add(piece);
                Emit(piece, room.Id, floorY);
                placedCount++;
            }
        }
        return (placedCount, skipped);
    }

    private void Emit(Piece p, string room, float floorY) =>
        _plan.Props.Add(new PlannedProp(p.Def, P(p.Pos.X, floorY + p.Y, p.Pos.Y), YawW(p.Yaw), p.Scale, room));

    private static bool IsFlat(PropDef def, float scale) => def.Bounds.Max.Y * scale < 0.06f && !def.IsSolid;

    private static uint Fnv(string s)
    {
        var h = 2166136261u;
        foreach (var ch in s.ToLowerInvariant())
        {
            h ^= ch;
            h *= 16777619u;
        }
        return h;
    }

    /// <summary>A prop's footprint on the plan at <paramref name="pos"/>, turned <paramref name="yawDeg"/> in the building frame.</summary>
    private static BRect Footprint(PropDef def, Vector2 pos, float yawDeg, float scale)
    {
        var b = Map.WorldBounds(def.Bounds, new Vector3(pos.X, 0, pos.Y), yawDeg * (MathF.PI / 180f), scale);
        return new BRect(b.Min.X, b.Min.Z, b.Max.X, b.Max.Z);
    }

    private static void Walkway(Grid g, Vector2 a, Vector2 b)
    {
        var len = Vector2.Distance(a, b);
        var steps = Math.Max(1, (int)MathF.Ceiling(len / 0.1f));
        for (var s = 0; s <= steps; s++)
        {
            var p = Vector2.Lerp(a, b, s / (float)steps);
            g.Set(new BRect(p.X - 0.5f, p.Y - 0.5f, p.X + 0.5f, p.Y + 0.5f), 2, true, g.Solid, touching: true);
        }
    }

    /// <summary>Facing vector of a yaw (degrees): props face +Z at yaw 0.</summary>
    private static Vector2 Facing(float yawDeg)
    {
        var a = yawDeg * (MathF.PI / 180f);
        return new Vector2(MathF.Sin(a), MathF.Cos(a));
    }

    /// <summary>The prop's own +X axis on the plan for a yaw.</summary>
    private static Vector2 SideAxis(float yawDeg)
    {
        var a = yawDeg * (MathF.PI / 180f);
        return new Vector2(MathF.Cos(a), -MathF.Sin(a));
    }

    private Piece? Place(FurnishItem item, PropDef def, int k, Grid grid, List<Face> faces, List<(int Face, float A, float B, float Sill)> windows,
        List<Vector2> pois, List<Piece>? parents, BRect bounds, float clear, ref Foliage.Rng rng)
    {
        var s = item.Scale;
        var flat = IsFlat(def, s);
        var gap = def.IsSolid ? item.Gap : 0.02f;
        var top = def.Bounds.Max.Y * s;
        var low = _lowCeil.TryGetValue(_furnishFloor, out var lc) ? lc : null;
        bool Fits(BRect fp)
        {
            if (!grid.FitsApart(fp, gap, flat))
                return false;
            if (low != null)
                foreach (var (r, clearUnder) in low)
                    if (top > clearUnder && fp.Overlaps(r))
                        return false; // too tall to stand under a platform
            return true;
        }
        switch (item.Place)
        {
            case FurnishPlace.Wall:
            case FurnishPlace.Corner:
            {
                if (top > clear - 0.05f)
                    return null;
                var cands = new List<(float Score, int Face, float U)>();
                for (var fi = 0; fi < faces.Count; fi++)
                {
                    var fc = faces[fi];
                    for (var u = fc.A; u <= fc.B + 0.001f; u += 0.1f)
                    {
                        var score = rng.Next() * 0.75f;
                        if (item.Prefer != FurnishPrefer.Any && pois.Count > 0)
                        {
                            var p = fc.AlongX ? new Vector2(u, fc.At) : new Vector2(fc.At, u);
                            var d = pois.Min(q => Vector2.Distance(p, q));
                            score += item.Prefer == FurnishPrefer.Far ? d : -d;
                        }
                        cands.Add((score, fi, u));
                    }
                }
                cands.Sort((x, y) => y.Score.CompareTo(x.Score));
                foreach (var (_, fi, u) in cands)
                {
                    var fc = faces[fi];
                    var yaw = fc.Yaw + item.Turn;
                    var back = -def.Bounds.Min.Z * s + 0.04f;
                    var across = fc.At + fc.Dir * back;
                    var pos = fc.AlongX ? new Vector2(u, across) : new Vector2(across, u);
                    var fp = Footprint(def, pos, yaw, s);
                    if (!Fits(fp))
                        continue;
                    var (fa, fb) = fc.AlongX ? (fp.X0, fp.X1) : (fp.Z0, fp.Z1);
                    var blocked = false;
                    foreach (var w in windows)
                        if (w.Face == fi && top > w.Sill + 0.2f && fa < w.B + 0.05f && fb > w.A - 0.05f)
                            blocked = true;
                    if (blocked)
                        continue;
                    if (item.Place == FurnishPlace.Corner)
                    {
                        // A corner: just past one end of the footprint (along the wall) there's a wall.
                        var depth = fc.At + fc.Dir * 0.3f;
                        var endA = fc.AlongX ? grid.At(fa - 0.25f, depth) : grid.At(depth, fa - 0.25f);
                        var endB = fc.AlongX ? grid.At(fb + 0.25f, depth) : grid.At(depth, fb + 0.25f);
                        if (endA != 1 && endB != 1)
                            continue;
                    }
                    return new Piece(def, pos, yaw, s, 0, fp);
                }
                return null;
            }
            case FurnishPlace.Centre:
            {
                var big = bounds;
                var c = big.Center;
                var roomLongX = big.W >= big.D;
                var propLongX = def.Bounds.Half.X >= def.Bounds.Half.Z;
                var yaw0 = (roomLongX == propLongX ? 0f : 90f) + item.Turn;
                for (var ring = 0; ring <= 5; ring++)
                {
                    var steps = ring == 0 ? 1 : 8;
                    for (var i = 0; i < steps; i++)
                    {
                        var ang = i * MathF.PI / 4;
                        var pos = c + new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * (ring * 0.2f);
                        foreach (var yaw in new[] { yaw0, yaw0 + 90f })
                        {
                            var fp = Footprint(def, pos, yaw, s);
                            if (Fits(fp))
                                return new Piece(def, pos, yaw, s, 0, fp);
                        }
                    }
                }
                return null;
            }
            case FurnishPlace.Floor:
            {
                for (var tries = 0; tries < 80; tries++)
                {
                    var pos = new Vector2(bounds.X0 + rng.Next() * bounds.W, bounds.Z0 + rng.Next() * bounds.D);
                    var yaw = def.IsSolid ? MathF.Floor(rng.Next() * 4) * 90f : rng.Next() * 360f;
                    var fp = Footprint(def, pos, yaw + item.Turn, s);
                    if (Fits(fp))
                        return new Piece(def, pos, yaw + item.Turn, s, 0, fp);
                }
                return null;
            }
            case FurnishPlace.On:
            {
                if (parents == null || parents.Count == 0)
                    return null;
                var parent = parents[k % parents.Count];
                var y = parent.Y + (item.Y ?? parent.Def.Bounds.Max.Y * parent.Scale);
                if (y + top > clear - 0.02f)
                    return null;
                var yaw = parent.Yaw + item.Turn;
                var child = Footprint(def, parent.Fp.Center, yaw, s);
                var mx = MathF.Max(0, (parent.Fp.W - child.W) / 2 * 0.8f);
                var mz = MathF.Max(0, (parent.Fp.D - child.D) / 2 * 0.8f);
                var pos = parent.Fp.Center + new Vector2((rng.Next() * 2 - 1) * mx, (rng.Next() * 2 - 1) * mz);
                return new Piece(def, pos, yaw, s, y, Footprint(def, pos, yaw, s));
            }
            case FurnishPlace.Beside:
            {
                if (parents == null || parents.Count == 0)
                    return null;
                var parent = parents[(k / 2) % parents.Count];
                var axis = SideAxis(parent.Yaw);
                var fwd = Facing(parent.Yaw);
                var pb = parent.Def.Bounds;
                var cb = def.Bounds;
                var backShift = pb.Min.Z * parent.Scale - cb.Min.Z * s; // line the backs up
                for (var t = 0; t < 2; t++)
                {
                    var side = (k + t) % 2 == 0 ? 1f : -1f;
                    var along = side > 0 ? pb.Max.X * parent.Scale + 0.05f - cb.Min.X * s : pb.Min.X * parent.Scale - 0.05f - cb.Max.X * s;
                    var pos = parent.Pos + axis * along + fwd * backShift;
                    var yaw = parent.Yaw + item.Turn;
                    var fp = Footprint(def, pos, yaw, s);
                    if (grid.FitsApart(fp, 0.02f, flat))
                        return new Piece(def, pos, yaw, s, 0, fp);
                }
                return null;
            }
            case FurnishPlace.Front:
            {
                if (parents == null || parents.Count == 0)
                    return null;
                var parent = parents[k % parents.Count];
                var fwd = Facing(parent.Yaw);
                var dist = parent.Def.Bounds.Max.Z * parent.Scale + item.Distance + def.Bounds.Max.Z * s;
                var pos = parent.Pos + fwd * dist;
                var yaw = parent.Yaw + 180f + item.Turn;
                var fp = Footprint(def, pos, yaw, s);
                return grid.FitsApart(fp, 0.02f, flat) ? new Piece(def, pos, yaw, s, 0, fp) : null;
            }
            case FurnishPlace.Around:
            {
                if (parents == null || parents.Count == 0)
                    return null;
                // Slots round each parent: two along a long side, one on a short side; long sides first.
                var slots = new List<(Piece Parent, Vector2 Pos, float Yaw)>();
                foreach (var parent in parents)
                {
                    var pb = parent.Def.Bounds;
                    var ps = parent.Scale;
                    var axis = SideAxis(parent.Yaw);
                    var fwd = Facing(parent.Yaw);
                    var w = (pb.Max.X - pb.Min.X) * ps;
                    var d = (pb.Max.Z - pb.Min.Z) * ps;
                    var sides = new List<(Vector2 N, Vector2 T, float Out, float Len)>
                    {
                        (fwd, axis, pb.Max.Z * ps, w), (-fwd, axis, -pb.Min.Z * ps, w), (axis, fwd, pb.Max.X * ps, d), (-axis, fwd, -pb.Min.X * ps, d),
                    };
                    sides.Sort((x, y) => y.Len.CompareTo(x.Len));
                    foreach (var (nrm, tan, outDist, len) in sides)
                    {
                        var offs = len >= 1.5f ? new[] { -len / 4, len / 4 } : new[] { 0f };
                        foreach (var o in offs)
                        {
                            var pos = parent.Pos + nrm * (outDist + item.Distance + def.Bounds.Max.Z * s) + tan * o;
                            var face = -nrm;
                            var yaw = MathF.Atan2(face.X, face.Y) * (180f / MathF.PI) + item.Turn;
                            slots.Add((parent, pos, yaw));
                        }
                    }
                }
                foreach (var (_, pos, yaw) in slots)
                {
                    var fp = Footprint(def, pos, yaw, s);
                    if (grid.FitsApart(fp, 0.02f, flat))
                        return new Piece(def, pos, yaw, s, 0, fp);
                }
                return null;
            }
            case FurnishPlace.Ceiling:
            {
                var gapUnder = item.Y ?? 0f;
                var y = clear - gapUnder - top;
                if (y < 2.0f)
                    return null;
                var longX = bounds.W >= bounds.D;
                var span = (longX ? bounds.W : bounds.D) - 1f;
                var n = Math.Max(1, item.CountMax);
                var t = n == 1 ? 0.5f : k / (float)(n - 1);
                var c = bounds.Center;
                var pos = longX ? new Vector2(bounds.X0 + 0.5f + span * (n == 1 ? 0.5f : 0.15f + 0.7f * t), c.Y)
                    : new Vector2(c.X, bounds.Z0 + 0.5f + span * (n == 1 ? 0.5f : 0.15f + 0.7f * t));
                if (grid.At(pos.X, pos.Y) == 1)
                    return null;
                return new Piece(def, pos, item.Turn, s, y, Footprint(def, pos, item.Turn, s));
            }
        }
        return null;
    }
}
