using System.Numerics;

namespace CallOfSlop.VehicleKit;

/// <summary>
/// Body styles phase 5 (docs/vehiclekit/BODY-STYLES.md, section 11): new spaces on a lofted room. An <b>upper storey</b> stands on the level roofs of a run of the room's
/// middle bays (<see cref="BodyLook.UpperPlan"/>), lofted in the body's language over each (an enclosed saloon, or a bulwark round an open deck), reached by a stair
/// inside the room through a hatch in that floor. A <b>caged room</b> is the same room in bays of bars (HullLoft's cage flag), set by the plan. <b>Side pods</b> hang
/// on free middle bay sides, each the faction's shape, cut for its bay as a trim is.
/// </summary>
public sealed partial class Vehicle
{
    private readonly List<(VPart Pod, VPart Bay, char Kind)> _pods = new();

    private static readonly Orient TurnedRound = new(Vector3.UnitY, Vector3.UnitY, -Vector3.UnitZ, Vector3.UnitZ);

    /// <summary>
    /// The upper storey over <paramref name="room"/> as its look plans it: the stair on the room's floor under the hatch, then a lofted bay on each cut roof of the run,
    /// stood by whichever of its feet the hatch leaves a socket for (a left door bay's turned round with it). Its own <see cref="Room"/> for the check.
    /// </summary>
    public void UpperStorey(Room room)
    {
        var lk = Look!;
        var up = lk.Upper!;
        var rule = BodyStyleRule.Of(Style);
        var plan = room.Plan.ToArray();
        var lengths = lk.Lengths(plan);
        var names = lk.UpperNames(plan, rule.UpperNarrow);
        var st = up.Stair!;
        var stair = up.StairWay > 0
            ? Stand(st.Key(), new Vector3(up.StairX, room.FloorTop, room.R.Z0 + up.StairZ + 0.125f), Orient.Forward, null, "foot0", 0.04f)
            : Stand(st.Key(), new Vector3(up.StairX, room.FloorTop, room.R.Z0 + up.StairZ - 0.125f), TurnedRound, null, "foot0", 0.04f); // (climbing to the front: turned round)
        PanelList("stairs").Add(stair);
        var y = room.FloorTop + up.Cut + HullLoft.FloorRise;
        var z = room.R.Z0 + lengths.Take(up.From).Sum();
        var (zRun0, bays) = (z, new List<VPart>());
        var walls = PanelList(room.Tag + "walls");
        for (var k = up.From; k <= up.To; k++)
        {
            var name = names[k - up.From];
            var sh = HullLoft.ShapeOf(name)!.Value;
            var f = HullLoft.FootX(sh);
            var turned = plan[k] == "Door" && lk.DoorSide < 0 && !lk.RearDoor;
            (string By, float X, float Z)[] feet = turned
                ? [("footR1", -f, z + 0.125f), ("footL1", f, z + 0.125f), ("footR0", -f, z + lengths[k] - 0.125f), ("footL0", f, z + lengths[k] - 0.125f)]
                : [("footL0", -f, z + 0.125f), ("footR0", f, z + 0.125f), ("footL1", -f, z + lengths[k] - 0.125f), ("footR1", f, z + lengths[k] - 0.125f)];
            VPart? bay = null;
            var why = "";
            foreach (var (by, fx, fz) in feet)
            {
                try
                {
                    bay = Stand(name, new Vector3(fx, y, fz), turned ? TurnedRound : Orient.Forward, room.Bays[k], by, 0.06f);
                    break;
                }
                catch (VehicleKitException e)
                {
                    why = e.Message; // (the hatch there: the next foot)
                }
            }
            if (bay == null)
                throw new VehicleKitException($"the {room.Tag}'s upper storey over bay {k}: {why}");
            bays.Add(bay);
            walls.Add(bay);
            VehicleGenJob.Yield();
            z += lengths[k];
        }
        var top = bays.Min(b => b.Def.BoundsMax.Y);
        var half = MathF.Max(0.6f, bays.Min(b => HullLoft.ShapeOf(b.Def.Name) is { } s ? MathF.Min(s.A.XAt(1.0f), s.B.XAt(1.0f)) : 1f) - 0.08f);
        var upper = new Room
        {
            Tag = room.Tag + " upper",
            R = new Rect(-half, half, zRun0, z),
            FloorTop = room.FloorTop + up.Cut + HullLoft.FloorRise,
            Open = up.Open,
            Plan = names.ToList(),
            DoorFace = -Vector3.UnitY,
            FloorRest = ToRest(Vector3.Zero),
        };
        upper.Ceiling = up.Open ? upper.FloorTop + 3f : y + top - 0.1f;
        upper.DoorMid = new Vector3(up.StairX, upper.FloorTop, room.R.Z0 + up.StairZ + up.StairWay * st.Run);
        upper.Bays.AddRange(bays);
        upper.Walls.AddRange(bays);
        room.Upper = upper;
        room.Stair = stair;
        room.StairFoot = new Vector3(up.StairX, room.FloorTop, room.R.Z0 + up.StairZ - up.StairWay * 0.45f);
        room.StairWay = new Vector3(0, 0, up.StairWay);
        // Where the room's check stands a player: its middle, unless the stair (and the way onto it) is there, then as far along as the room allows from them.
        var (s0, s1) = (MathF.Min(room.StairFoot.Z, upper.DoorMid.Z), MathF.Max(room.StairFoot.Z, upper.DoorMid.Z));
        var best = room.Middle.Z;
        for (var t = room.R.Z0 + 0.8f; t <= room.R.Z1 - 0.8f; t += 0.25f)
            if (Gap(t) > Gap(best))
                best = t;
        room.WalkStart = room.Middle with { Z = best };
        float Gap(float t) => t < s0 ? s0 - t : t > s1 ? t - s1 : 0;
        Modules.Add($"space upper {up.Kind}");
    }

    /// <summary>Paints an upper storey's bays as the bays they stand on (one body, two floors), after the room's runs.</summary>
    private void PaintUpper(Room room)
    {
        if (room.Upper is not { } upper || Look?.Upper is not { } up)
            return;
        for (var i = 0; i < upper.Bays.Count && up.From + i < room.Bays.Count; i++)
            if (ConstructPaint.ToHex(C.Parts[room.Bays[up.From + i].Index].Paint) is { Length: > 0 } colour)
                Paint(upper.Bays[i], colour);
    }

    /// <summary>
    /// The faction's side pods (<see cref="BodyStyleRule.Pods"/>) on free waist sockets of a skinned room's middle bays (not the door's), before the trim takes the rest:
    /// from the wheels' room up past the waist, each where it draws into nothing.
    /// </summary>
    private void FactionPods()
    {
        var rule = BodyStyleRule.Of(Style);
        if (NoTrim || BodyLook.NoSpaces || Look == null || Rooms.Count == 0 || rule.Pods.Length == 0)
            return;
        var d = Dice.Fork("side pods");
        var inside = Rooms.SelectMany(r => r.Bays.Concat(r.Furniture).Concat(r.Containers).Concat(r.Seats)).Concat(Panels.GetValueOrDefault("furniture") ?? []).Select(b => b.Index).ToHashSet();
        var placed = 0;
        foreach (var room in Rooms)
            for (var k = 1; k + 1 < room.Bays.Count && placed < rule.PodMax; k++)
            {
                var bay = room.Bays[k];
                if (room.Plan[k] == "Door" || HullLoft.ShapeOf(bay.Def.Name) is not { } sh || sh.Cage != '\0' || sh.Length < 0.7f)
                    continue;
                foreach (var (socket, side) in new[] { ("+x0", 1f), ("-x0", -1f) })
                {
                    var at = Array.FindIndex(bay.Def.Sockets, s => s.Name == socket);
                    if (at < 0 || C.SocketUsed(bay.Index, at) || placed >= rule.PodMax)
                        continue;
                    var bd = d.Fork($"bay {room.Tag} {k} {socket}");
                    var (kind, chance) = rule.Pods[bd.Int(0, rule.Pods.Length - 1)];
                    if (!bd.Chance(chance))
                        continue;
                    C.SocketAt(bay.Index, at, out _, out var face);
                    var outward = ConstructParts.DirVector(face).X > 0 ? 1f : -1f;
                    var (a, b) = side > 0 ? (sh.A, sh.B) : (sh.B, sh.A);
                    var low = MathF.Max(0.12f, TrimLow(bay, outward, room.FloorTop));
                    var waist = (a.WaistY + b.WaistY) / 2;
                    var high = MathF.Min(1.55f, waist + (float)bd.Between(0.3, 0.55));
                    if (high - low < 0.45f)
                        continue; // (all wheel arch)
                    var depth = kind switch { 'w' => bd.Between(0.34, 0.44), 'b' => bd.Between(0.3, 0.4), 'k' => bd.Between(0.34, 0.42), 'l' => bd.Between(0.3, 0.38), 'a' => bd.Between(0.3, 0.36), _ => 0.28 };
                    var shape = new HullLoft.PodShape(kind, sh.Length, MathF.Round(low * 100) / 100, MathF.Round(high * 100) / 100, MathF.Round((float)depth * 100) / 100, a, b);
                    var key = shape.Key();
                    if (key.Length > ConstructParts.MaxRuntimeName || ConstructParts.Find(key) == null)
                        continue;
                    VehicleGenJob.Yield();
                    try
                    {
                        var skip = new HashSet<int>(inside);
                        var pod = Turned(bay, socket, key, "base", side > 0 ? (byte)0 : Yaw180, true, (def, g) => DrawnClash(def, g, skip));
                        PanelList("trim").Add(pod);
                        _pods.Add((pod, bay, kind));
                        placed++;
                    }
                    catch (VehicleKitException)
                    {
                        // (a ladder, a door's leaf, a gun's arm or a neighbour's pod there: that side keeps its trim)
                    }
                }
            }
        if (placed > 0)
            Modules.Add($"pods {HullLoft.PodWord}{string.Concat(_pods.Select(x => x.Kind).Distinct())} on {placed} bay sides");
    }

    /// <summary>The pods' paint: a welded box off another wreck, a kiosk the war paint, a locker its varnish, the rest their bay's own colour.</summary>
    private void PaintPods(Dice d)
    {
        foreach (var (pod, bay, kind) in _pods)
        {
            var own = ConstructPaint.ToHex(C.Parts[bay.Index].Paint);
            var colour = kind switch
            {
                'w' => Style.Colour(d, d.Chance(0.5) ? "body" : "rust"),
                'k' => Style.Colour(d, Style.Palette.ContainsKey("war") ? "war" : "body"),
                'l' => "#6a4527",
                _ => own is { Length: > 0 } ? own : Style.Colour(d, "body"),
            };
            Paint(pod, colour);
        }
    }
}
