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
}

public static partial class ConstructParts
{
    private static readonly string[] PortTypes = ["shaft", "pipe", "power", "chute"];

    private static void ParseNamedPorts(ConstructPartDef d, JsonElement root, string file)
    {
        var list = new List<NamedPort>();
        if (Opt(root, "ports") is { } ps)
        {
            if (ps.ValueKind != JsonValueKind.Array || ps.GetArrayLength() is < 1 or > 16)
                throw new ConstructPartException($"{file}: \"ports\" lists 1 to 16 link ports ({{name, type, dir, socket or at + face}})");
            var pi = 0;
            foreach (var p in ps.EnumerateArray())
            {
                var where = $"ports[{pi++}]";
                Known(p, ["name", "type", "dir", "socket", "at", "face", "fluid", "noEffect"], $"{file}, {where}");
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
    }
}
