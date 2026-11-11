using System.Numerics;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>
/// A link port a part file declares by function (board item dev-link-named-ports-np01): "Fuel in", "Drive out", "Input
/// shaft". A shaft port stands on one of the part's sockets (the port byte of a shaft link is that socket's index, as it
/// always was) and may move its point and facing to the middle of a face; a pipe or power port has its own point and
/// facing. The first port of a type on a part is port byte 255 (the part itself, as before), the next ones
/// <see cref="ConstructRoutes.DataPortBase"/> | k.
/// </summary>
public sealed class NamedPort
{
    public string Name = "";
    public RouteType Type;
    public bool In, Out; // may a link end here / start here
    public int Socket = -1; // shaft ports: the socket whose index is the port byte
    public Vector3 At; // the port's point (part frame, unscaled)
    public byte Face; // the direction it faces out of the part (a ConstructParts.DirVector index)
    public string Fluid = ""; // pipes: what it carries or takes (fuel, coolant, oil, water, exhaust)
    public bool NoEffect; // listed in the picker as "(no effect yet)": the simulation does nothing with it yet
    public byte Byte = 255; // the port byte in a link
    public bool Implicit; // made by the loader, not listed in the part file
    public Vector2 Opening; // conveyor ports: the clear opening a piece passes (metres across, up): a conveyor is set within both ends' (ConstructConveyor.cs)
}

public static partial class ConstructParts
{
    private static readonly string[] PortTypes = ["shaft", "pipe", "power", "chute", "feed", "conveyor"];

    private static Vector2 Vec2(JsonElement e, string file, string what)
    {
        if (e.ValueKind != JsonValueKind.Array || e.GetArrayLength() != 2)
            throw new ConstructPartException($"{file}: {what} is [across, up] in metres");
        return new Vector2(e[0].GetSingle(), e[1].GetSingle());
    }

    private static void ParseNamedPorts(ConstructPartDef d, JsonElement root, string file)
    {
        ParseHydraulics(d, root, file); // (before the ports: a pump has a Drive in)
        var list = new List<NamedPort>();
        if (Opt(root, "ports") is { } ps)
        {
            if (ps.ValueKind != JsonValueKind.Array || ps.GetArrayLength() is < 1 or > 16)
                throw new ConstructPartException($"{file}: \"ports\" lists 1 to 16 link ports ({{name, type, dir, socket or at + face}})");
            var pi = 0;
            foreach (var p in ps.EnumerateArray())
            {
                var where = $"ports[{pi++}]";
                Known(p, ["name", "type", "dir", "socket", "at", "face", "fluid", "noEffect", "opening"], $"{file}, {where}");
                var np = new NamedPort { Name = Str(Need(p, "name", $"{file}, {where}"), file, where + ".name") };
                if (np.Name.Length == 0 || list.Any(x => string.Equals(x.Name, np.Name, StringComparison.OrdinalIgnoreCase)))
                    throw new ConstructPartException($"{file}: {where}: a port needs its own name (\"{np.Name}\")");
                var ts = Str(Need(p, "type", $"{file}, {where}"), file, where + ".type").ToLowerInvariant();
                if (Array.IndexOf(PortTypes, ts) < 0)
                    throw new ConstructPartException($"{file}: {where}.type is one of {string.Join(", ", PortTypes)} (data ports are the logic part's own inputs and outputs), not \"{ts}\"");
                np.Type = ConstructRoutes.TypeOf(ts)!.Value;
                var dir = Opt(p, "dir") is { } dj ? Str(dj, file, where + ".dir").ToLowerInvariant() : "both";
                (np.In, np.Out) = dir switch
                {
                    "in" => (true, false),
                    "out" => (false, true),
                    "both" => (true, true),
                    _ => throw new ConstructPartException($"{file}: {where}.dir is in, out or both"),
                };
                np.Fluid = Opt(p, "fluid") is { } fl ? Str(fl, file, where + ".fluid").ToLowerInvariant() : "";
                if (np.Fluid.Length > 0 && np.Type != RouteType.Pipe)
                    throw new ConstructPartException($"{file}: {where}: only a pipe port has a \"fluid\"");
                np.NoEffect = Opt(p, "noEffect") is { ValueKind: JsonValueKind.True };
                if (np.Type == RouteType.Conveyor)
                {
                    var op = Vec2(Need(p, "opening", $"{file}, {where}"), file, where + ".opening");
                    if (op.X < 0.1f || op.Y < 0.1f || op.X > 32 || op.Y > 32)
                        throw new ConstructPartException($"{file}: {where}.opening is 0.1 to 32 m each way");
                    np.Opening = op;
                }
                else if (Opt(p, "opening") != null)
                    throw new ConstructPartException($"{file}: {where}: only a conveyor port has an \"opening\"");
                if (np.Type == RouteType.Shaft)
                {
                    var sn = Str(Need(p, "socket", $"{file}, {where}"), file, where + ".socket");
                    np.Socket = Array.FindIndex(d.Sockets, s => s.Name == sn);
                    if (np.Socket < 0)
                        throw new ConstructPartException($"{file}: {where}: a shaft port stands on a socket, and \"{sn}\" is not one of them");
                    if (d.Sockets[np.Socket].Kind is SocketKind.Axle or SocketKind.Leaf)
                        throw new ConstructPartException($"{file}: {where}: socket \"{sn}\" is an axle or leaf socket, not a drive port");
                    if (list.Any(x => x.Socket == np.Socket))
                        throw new ConstructPartException($"{file}: {where}: socket \"{sn}\" already carries another port");
                    np.At = Opt(p, "at") is { } sa ? Vec(sa, file, where + ".at") : d.Sockets[np.Socket].At;
                    np.Face = Opt(p, "face") is { } sf ? DirOf(Str(sf, file, where + ".face"), file, where) : d.Sockets[np.Socket].Dir;
                }
                else
                {
                    np.At = Vec(Need(p, "at", $"{file}, {where}"), file, where + ".at");
                    np.Face = DirOf(Str(Need(p, "face", $"{file}, {where}"), file, where + ".face"), file, where);
                }
                var axis = np.Face >> 1;
                var plane = (np.Face & 1) == 0 ? d.BoundsMax[axis] : d.BoundsMin[axis];
                if (MathF.Abs(np.At[axis] - plane) > 0.005f && !OnBoxFace(d, np.At, np.Face))
                    throw new ConstructPartException($"{file}: {where} (\"{np.Name}\"): its point is not on the {DirName(np.Face)} face of the part (at {np.At[axis]:0.###}, the face is at {plane:0.###}): a link must leave a port straight out of the part's surface");
                for (var a = 0; a < 3; a++)
                    if (a != axis && (np.At[a] < d.BoundsMin[a] - 0.005f || np.At[a] > d.BoundsMax[a] + 0.005f))
                        throw new ConstructPartException($"{file}: {where} (\"{np.Name}\"): its point is outside the part");
                list.Add(np);
            }
        }
        // Parts that draw power have an implicit "Power in" unless the file names its own.
        if (d.Electric && !list.Any(x => x.Type == RouteType.Power && x.In))
        {
            var c = (d.BoundsMin + d.BoundsMax) * 0.5f;
            list.Add(new NamedPort { Name = "Power in", Type = RouteType.Power, In = true, At = new Vector3(c.X, c.Y, d.BoundsMax.Z), Face = 4, Implicit = true });
        }
        // Every gun takes an ammo feed and every box and crate gives one, unless the file names its own.
        if (d.Role == PartRole.Gun && !list.Any(x => x.Type == RouteType.Feed && x.In))
        {
            var c = (d.BoundsMin + d.BoundsMax) * 0.5f;
            var mag = Array.Find(d.Sockets, s => s.Name == "left"); // (where a magazine goes: a feed takes its place)
            list.Add(mag != null
                ? new NamedPort { Name = "Ammo in", Type = RouteType.Feed, In = true, At = mag.At, Face = mag.Dir, Implicit = true }
                : new NamedPort { Name = "Ammo in", Type = RouteType.Feed, In = true, At = new Vector3(c.X, d.BoundsMax.Y, c.Z), Face = 2, Implicit = true });
        }
        if (d.Role == PartRole.AmmoBox && !list.Any(x => x.Type == RouteType.Feed && x.Out))
        {
            var c = (d.BoundsMin + d.BoundsMax) * 0.5f;
            list.Add(new NamedPort { Name = "Ammo out", Type = RouteType.Feed, Out = true, At = new Vector3(c.X, d.BoundsMax.Y, c.Z), Face = 2, Implicit = true });
        }
        // An Extruder and a Wheel Former put pieces out onto a conveyor: below the die, at its own die's bottom edge, facing out of the output face.
        if (d.IsExtruder && !d.IsFanFormer && !d.IsAirframeFormer && !list.Any(x => x.Type == RouteType.Conveyor && x.Out))
        {
            var die = d.IsWheelFormer ? new Vector2(d.ExtWheelMax.X, d.ExtWheelMax.Y) : d.ExtMaxSection;
            var y = d.IsWheelFormer ? d.BoundsMin.Y + 0.1f : MathF.Max(d.BoundsMin.Y + 0.05f, d.ExtDieAt.Y - d.ExtDie.Y * 0.5f);
            list.Add(new NamedPort { Name = "Piece out", Type = RouteType.Conveyor, Out = true, At = new Vector3(d.ExtDieAt.X, y, d.BoundsMax.Z), Face = 4, Opening = die, Implicit = true });
        }
        // A Press or Die Lathe takes pieces off a conveyor into its bays: a Piece in at each bay's socket (by its name: "Piece in", "Piece in 2"...),
        // the line 11 cm under the socket, facing the way a piece leaves the bay (ConstructPress.TakeFromConveyor).
        if (d.IsPress && d.PressBays.Length > 0 && !list.Any(x => x.Type == RouteType.Conveyor))
            for (var b = 0; b < d.PressBays.Length; b++)
            {
                var s = d.Sockets[d.PressBays[b].Socket];
                var open = Math.Max(0.1f, d.PressMaxCm / 100f);
                list.Add(new NamedPort { Name = b == 0 ? "Piece in" : $"Piece in {b + 1}", Type = RouteType.Conveyor, In = true, At = s.At - new Vector3(0, 0.11f, 0), Face = s.Dir, Opening = new Vector2(open, open), Implicit = true });
            }
        // And a Piece out after them (so the bays' port bytes stay as they were), where a stroke's body leaves (outAt, facing outDir) at the
        // floor's line: what it stamps that no tray holds rides out on a conveyor to a rack (ConstructPress.Stroke), the machine's width across.
        // An Assembler has one too, at its output point: what it builds rides out to a rack (ConstructAssembling.Finish).
        if ((d.IsPress && d.PressBays.Length > 0 || d.IsAssembler) && !list.Any(x => x.Type == RouteType.Conveyor && x.Out))
        {
            byte face = 4;
            for (byte f = 0; f < 6; f++)
                if (Vector3.Dot(ConstructParts.DirVector(f), d.AsmOutDir) > 0.9f)
                    face = f;
            var across = MathF.Max(0.1f, MathF.Min(d.BoundsMax.X - d.BoundsMin.X, 4f));
            list.Add(new NamedPort { Name = "Piece out", Type = RouteType.Conveyor, Out = true, At = d.AsmOutAt with { Y = d.BoundsMin.Y + 0.1f }, Face = face, Opening = new Vector2(across, across), Implicit = true });
        }
        // A Piece Rack sends pieces on too (a Crafting Terminal fetches from it: ConstructCrafting.cs): a Piece out at its far end, facing on.
        // After its Piece in, so that port's byte stays as it was. A Crafting Terminal's tray has none: it is the end of the line.
        if (d.IsPieceRack && !d.IsCraftTerminal && !list.Any(x => x.Type == RouteType.Conveyor && x.Out))
            list.Add(new NamedPort { Name = "Piece out", Type = RouteType.Conveyor, Out = true, At = new Vector3((d.BoundsMin.X + d.BoundsMax.X) * 0.5f, d.BoundsMin.Y + 0.05f, d.BoundsMax.Z), Face = 4, Opening = d.RackSection, Implicit = true });
        // A part with conveyor ports both ways passes pieces on: a splitter or a join (ConstructConveyor.cs, "Junctions").
        d.IsConveyorJunction = !d.IsExtruder && !d.IsPress && !d.IsPieceRack && list.Any(x => x.Type == RouteType.Conveyor && x.In) && list.Any(x => x.Type == RouteType.Conveyor && x.Out);
        // Port bytes: shaft ports are their socket; the others count within their type (the first is the part itself, 255).
        Span<int> seen = stackalloc int[(int)RouteType.COUNT];
        foreach (var np in list)
        {
            if (np.Type == RouteType.Shaft)
                np.Byte = (byte)np.Socket;
            else
            {
                var k = seen[(int)np.Type]++;
                if (k >= 16)
                    throw new ConstructPartException($"{file}: at most 16 {ConstructRoutes.TypeKey(np.Type)} ports a part");
                np.Byte = ConstructRoutes.DataPortByte(k);
            }
        }
        d.NamedPorts = list.ToArray();
        if (d.ShaftIn)
        {
            d.DriveInSocket = list.Find(x => x.Type == RouteType.Shaft && x.In)?.Socket ?? -1;
            if (d.DriveInSocket < 0)
                throw new ConstructPartException($"{file}: {(d.IsRail ? "a shaft-driven rail" : d.Rotor != null ? "a rotor on a shaft" : "storage.gather's ratedRpm")} needs a shaft input port (\"Drive in\")");
        }
    }
}
