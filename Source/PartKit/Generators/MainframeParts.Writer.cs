using System.Numerics;
using System.Text;

namespace CallOfSlop.PartKit.Generators;

public sealed partial class MainframeParts
{
    // The six ways, as the game numbers them (ConstructParts.DirVector).
    private const byte PX = 0, NX = 1, PY = 2, NY = 3, PZ = 4, NZ = 5;

    /// <summary>The turn whose local +x goes to <paramref name="x"/> and +y to <paramref name="y"/> (two ways at right angles).</summary>
    private static byte Turn(byte x, byte y)
    {
        for (byte r = 0; r < Rot24.Count; r++)
            if (Rot24.ApplyDir(r, 0) == x && Rot24.ApplyDir(r, 2) == y)
                return r;
        throw new InvalidOperationException($"no turn takes +x to {x} and +y to {y}");
    }

    /// <summary>The turn whose local +y goes to <paramref name="y"/> and +z to <paramref name="z"/>.</summary>
    private static byte TurnYZ(byte y, byte z)
    {
        for (byte r = 0; r < Rot24.Count; r++)
            if (Rot24.ApplyDir(r, 2) == y && Rot24.ApplyDir(r, 4) == z)
                return r;
        throw new InvalidOperationException($"no turn takes +y to {y} and +z to {z}");
    }

    /// <summary>A paint and a texture for a step (null: the part's own).</summary>
    private readonly record struct Look(string? Paint, string? Finish = null);

    /// <summary>One step of the blueprint, and where its part stands (for the links later steps make to it).</summary>
    private sealed class Step
    {
        public string Part = "";
        public int On = -1;
        public string Socket = "", By = "";
        public int Spin;
        public Vector3? At;
        public (Vector3 Pos, Quaternion Rot)? Weld;
        public List<(int On, string Their, string Own)>? Also;
        public PyDict? Config;
        public string? Paint, Finish;
        public bool OrdersOnly;
        public ConstructPartDef Def = null!;
        public int Seg; // its segment: 0 the body's, else a weld's
        public byte Rot; // its turn within its segment
        public Vector3 Pos; // where it stands in its segment
        public Vector3 Min, Max; // its box in the body's frame
    }

    /// <summary>
    /// The blueprint as it is written: steps in order, each part placed where the generator says (an "at" in its segment's frame, so
    /// nothing moves to a socket), linked exactly as the steps say ("exactLinks"). A part on the body's own frame links to the parts it
    /// touches, by their nearest sockets ("also"); a plate on a slanting facet hangs on its frame node by a socket; a node is welded to
    /// the nearest structure. Every step reaches step 0, so nothing falls off as a body of its own.
    /// </summary>
    private sealed class Writer(Dictionary<string, ConstructPartDef> own)
    {
        public readonly List<Step> Steps = [];
        public int Welds, Joints;

        /// <summary>The craft the ship is built carrying (its "carry" list): each one's blueprint, origin (body metres) and turn.</summary>
        public readonly List<(string Blueprint, Vector3 At, byte Turn)> Carried = [];

        /// <summary>Step <paramref name="k"/>'s turn in its segment.</summary>
        public byte Rot(int k) => Steps[k].Rot;

        /// <summary>The first turn that mates <paramref name="part"/>'s socket <paramref name="by"/> with socket <paramref name="socket"/> of
        /// step <paramref name="on"/> (a blueprint step's spin 0).</summary>
        public byte FirstTurn(int on, string socket, string part, string by)
        {
            var t = Steps[on];
            var tdir = Rot24.ApplyDir(t.Rot, SockOf(t.Def, socket).Dir);
            Span<byte> turns = stackalloc byte[4];
            var n = ConstructBuild.SpinTurns(SockOf(Def(part), by).Dir, tdir, turns);
            return n > 0 ? turns[0] : throw new InvalidOperationException($"mainframe: {part} by {by} can't go on {t.Part} {socket}");
        }
        public int Keys => _defs.Count;

        private readonly Dictionary<string, ConstructPartDef> _defs = new(StringComparer.Ordinal);
        private readonly List<Matrix4x4> _segs = [Matrix4x4.Identity];
        private readonly Dictionary<long, List<int>> _grid = [];
        private readonly HashSet<(int, string)> _used = [];
        private IReadOnlyCollection<string>? _keep; // (the new part's own sockets kept for what goes on them next)
        private readonly Dictionary<(int, char), int> _taken = [];
        private int[] _seen = new int[64];
        private int _stamp;
        private const float Cell = 16f;

        public ConstructPartDef Def(string name)
        {
            if (_defs.TryGetValue(name, out var d))
                return d;
            d = own.TryGetValue(name, out var mine) ? mine : ConstructParts.Find(name);
            if (d == null)
            {
                var why = HullLoft.IsCutName(name) ? HullLoft.CutRefusal(name) : name.StartsWith(Extrusion.Prefix, StringComparison.Ordinal) && Extrusion.ParseKey(name) is { } k ? Extrusion.KeyRefusal(k) : null;
                throw new InvalidOperationException($"mainframe: no part \"{name}\"{(why != null ? ": " + why : "")}");
            }
            _defs[name] = d;
            return d;
        }

        public static CSocket SockOf(ConstructPartDef d, string name) =>
            Array.Find(d.Sockets, s => s.Name == name) is { Name: not null } s ? s : throw new InvalidOperationException($"mainframe: {d.Name} has no socket \"{name}\"");

        public static bool HasSock(ConstructPartDef d, string name) => Array.Exists(d.Sockets, s => s.Name == name);

        public Matrix4x4 SegFrame(int seg) => _segs[seg];

        /// <summary>A point of step <paramref name="k"/>'s part (its own frame) in the body's frame.</summary>
        public Vector3 ToBody(int k, Vector3 local) => ToBody(Steps[k], local);

        private Vector3 ToBody(Step s, Vector3 local) => Vector3.Transform(s.Pos + Rot24.Apply(s.Rot, local), _segs[s.Seg]);

        /// <summary>The next free socket of a node, by its key (u up, e east, w west, n north, s south, d down).</summary>
        public string Take(int node, char key)
        {
            _taken.TryGetValue((node, key), out var i);
            while (_used.Contains((node, $"{key}{i}")))
                i++;
            var name = $"{key}{i}";
            if (!HasSock(Steps[node].Def, name))
                throw new InvalidOperationException($"mainframe: frame node at step {node} has no socket {name} left: lay a second node on that facet");
            _taken[(node, key)] = i + 1;
            return name;
        }

        public bool Free(int node, char key, int n = 1)
        {
            _taken.TryGetValue((node, key), out var i);
            return HasSock(Steps[node].Def, $"{key}{i + n - 1}");
        }

        // ---- Steps

        /// <summary>A part on the body's own frame at <paramref name="at"/> (body metres), turned by <paramref name="turn"/>, linked to
        /// what it touches (up to <paramref name="links"/> parts, the nearest sockets), or to <paramref name="to"/> when given.</summary>
        public int Root(string part, Vector3 at, byte turn, Look look = default, PyDict? config = null, int links = 2, int to = -1, bool linkable = true, IReadOnlyCollection<string>? keep = null)
        {
            var d = Def(part);
            var k = Steps.Count;
            var s = new Step { Part = part, Def = d, Seg = 0, Rot = turn, Pos = Q(at), At = k == 0 ? null : Q(at), Spin = turn, Paint = look.Paint, Finish = look.Finish, Config = config };
            Bounds(s);
            if (k > 0)
            {
                _keep = keep;
                if (to >= 0)
                    LinkTo(s, to);
                else
                    LinkNear(s, links);
                _keep = null;
                s.On = s.Also![0].On;
                foreach (var (on, their, mine) in s.Also)
                {
                    _used.Add((on, their)); // (a socket takes one link: none of these is offered again)
                    _used.Add((k, mine));
                }
            }
            Steps.Add(s);
            if (linkable)
                Index(k);
            return k;
        }

        /// <summary>A part on socket <paramref name="socket"/> of step <paramref name="on"/> by its own <paramref name="by"/>, turned by
        /// <paramref name="turn"/> in that part's segment (it must be a turn that mates the two), standing at <paramref name="at"/> in
        /// that segment when given (else where the sockets put it).</summary>
        public int OnSocket(int on, string socket, string part, string by, byte turn, Vector3? at = null, Look look = default, PyDict? config = null)
        {
            var t = Steps[on];
            var d = Def(part);
            var ts = SockOf(t.Def, socket);
            var ds = SockOf(d, by);
            if (!_used.Add((on, socket)))
                throw new InvalidOperationException($"mainframe: socket {socket} of step {on} ({t.Part}) is used twice");
            var tdir = Rot24.ApplyDir(t.Rot, ts.Dir);
            Span<byte> turns = stackalloc byte[4];
            var n = ConstructBuild.SpinTurns(ds.Dir, tdir, turns);
            var spin = turns[..n].IndexOf(turn);
            if (spin < 0)
                throw new InvalidOperationException($"mainframe: {part} by {by} can't be turned {turn} on {t.Part} {socket}");
            var sockAt = t.Pos + Rot24.Apply(t.Rot, ts.At);
            var pos = at is { } a ? Q(a) : Q(sockAt - Rot24.Apply(turn, ds.At));
            var s = new Step { Part = part, Def = d, On = on, Socket = socket, By = by, Spin = spin, At = at is null ? null : pos, Seg = t.Seg, Rot = turn, Pos = pos, Paint = look.Paint, Finish = look.Finish, Config = config };
            Bounds(s);
            if (t.Def.HasJoint && Array.FindIndex(t.Def.Sockets, x => x.Name == socket) == t.Def.LeafIndex)
                Joints++;
            Steps.Add(s);
            _used.Add((Steps.Count - 1, by));
            if (s.Seg == 0)
                Index(Steps.Count - 1);
            return Steps.Count - 1;
        }

        /// <summary>A part welded freeform to step <paramref name="on"/>: its frame at <paramref name="pos"/>, <paramref name="rot"/> in that step's segment (a new segment of its own).</summary>
        public int Weld(int on, string part, Vector3 pos, Quaternion rot)
        {
            var t = Steps[on];
            var d = Def(part);
            rot = Quaternion.Normalize(rot);
            var frame = Matrix4x4.CreateFromQuaternion(rot) * Matrix4x4.CreateTranslation(pos) * _segs[t.Seg];
            _segs.Add(frame);
            var s = new Step { Part = part, Def = d, On = on, Weld = (pos, rot), Seg = _segs.Count - 1, Rot = 0, Pos = Vector3.Zero };
            Bounds(s);
            Welds++;
            Joints++;
            Steps.Add(s);
            return Steps.Count - 1;
        }

        /// <summary>The nearest part on the body's own frame to a point (body metres): a frame node's anchor.</summary>
        public int Nearest(Vector3 p)
        {
            var best = (D: float.MaxValue, K: 0);
            foreach (var reach in (ReadOnlySpan<float>)[4f, 16f, 64f, 256f, 1024f])
            {
                var (a, b) = (CellOf(p - new Vector3(reach)), CellOf(p + new Vector3(reach)));
                for (var x = a.X; x <= b.X; x++)
                    for (var y = a.Y; y <= b.Y; y++)
                        for (var z = a.Z; z <= b.Z; z++)
                            if (_grid.TryGetValue(Key(x, y, z), out var list))
                                foreach (var j in list)
                                {
                                    var t = Steps[j];
                                    var d = Vector3.DistanceSquared(p, Vector3.Clamp(p, t.Min, t.Max));
                                    if (d < best.D)
                                        best = (d, j);
                                }
                if (best.D <= reach * reach)
                    return best.K;
            }
            return best.K;
        }

        /// <summary>Another link from step <paramref name="k"/> to an earlier one.</summary>
        public void Also(int k, int on, string their, string mine)
        {
            if (on >= k)
                throw new InvalidOperationException("mainframe: an \"also\" names an earlier step");
            (Steps[k].Also ??= []).Add((on, their, mine));
        }

        // ---- Links by touch

        private void Index(int k)
        {
            var s = Steps[k];
            var (a, b) = (CellOf(s.Min - new Vector3(0.1f)), CellOf(s.Max + new Vector3(0.1f)));
            for (var x = a.X; x <= b.X; x++)
                for (var y = a.Y; y <= b.Y; y++)
                    for (var z = a.Z; z <= b.Z; z++)
                    {
                        var key = Key(x, y, z);
                        if (!_grid.TryGetValue(key, out var list))
                            _grid[key] = list = [];
                        list.Add(k);
                    }
        }

        private static (int X, int Y, int Z) CellOf(Vector3 v) => ((int)MathF.Floor(v.X / Cell), (int)MathF.Floor(v.Y / Cell), (int)MathF.Floor(v.Z / Cell));

        private static long Key(int x, int y, int z) => ((long)(x + 4096) << 26) | ((long)(y + 4096) << 13) | (long)(z + 4096);

        /// <summary>Links to the nearest parts it touches; else to the nearest part within reach; else to step 0.</summary>
        private void LinkNear(Step s, int most)
        {
            var found = new List<(float D, int Step, string Their, string Own)>();
            foreach (var reach in (ReadOnlySpan<float>)[0.12f, 2f, 8f, 32f, 128f])
            {
                Near(s, reach, found);
                if (found.Count > 0)
                    break;
            }
            if (found.Count == 0)
                found.Add((0, 0, Take(0, 'u'), Array.Find(s.Def.Sockets, x => _keep?.Contains(x.Name) != true).Name ?? s.Def.Sockets[0].Name));
            found.Sort((a, b) => a.D.CompareTo(b.D));
            var done = new HashSet<int>();
            var mineUsed = new HashSet<string>(StringComparer.Ordinal);
            var theirUsed = new HashSet<(int, string)>();
            foreach (var f in found)
            {
                if (done.Contains(f.Step) || !mineUsed.Add(f.Own) || !theirUsed.Add((f.Step, f.Their)))
                    continue; // (one link a socket, either side)
                done.Add(f.Step);
                (s.Also ??= []).Add((f.Step, f.Their, f.Own));
                if (done.Count >= most)
                    break;
            }
        }

        private void LinkTo(Step s, int to)
        {
            var t = Steps[to];
            var best = (D: float.MaxValue, Their: "", Own: "");
            foreach (var ts in t.Def.Sockets)
            {
                if (_used.Contains((to, ts.Name)))
                    continue;
                var tp = ToBody(t, ts.At);
                foreach (var os in s.Def.Sockets)
                {
                    if (_keep?.Contains(os.Name) == true)
                        continue;
                    var d = Vector3.DistanceSquared(tp, ToBody(s, os.At));
                    if (d < best.D)
                        best = (d, ts.Name, os.Name);
                }
            }
            (s.Also ??= []).Add((to, best.Their, best.Own));
        }

        private void Near(Step s, float reach, List<(float D, int Step, string Their, string Own)> found)
        {
            var lo = s.Min - new Vector3(reach);
            var hi = s.Max + new Vector3(reach);
            var (a, b) = (CellOf(lo), CellOf(hi));
            _stamp++;
            if (_seen.Length < Steps.Count + 1)
                Array.Resize(ref _seen, Math.Max(Steps.Count + 1, _seen.Length * 2));
            var mine = new Vector3[s.Def.Sockets.Length];
            for (var i = 0; i < mine.Length; i++)
                mine[i] = ToBody(s, s.Def.Sockets[i].At);
            for (var x = a.X; x <= b.X; x++)
                for (var y = a.Y; y <= b.Y; y++)
                    for (var z = a.Z; z <= b.Z; z++)
                    {
                        if (!_grid.TryGetValue(Key(x, y, z), out var list))
                            continue;
                        foreach (var j in list)
                        {
                            if (_seen[j] == _stamp)
                                continue;
                            _seen[j] = _stamp;
                            var t = Steps[j];
                            if (t.Max.X < lo.X || t.Min.X > hi.X || t.Max.Y < lo.Y || t.Min.Y > hi.Y || t.Max.Z < lo.Z || t.Min.Z > hi.Z || t.Def.Name == NodeName)
                                continue; // (a frame node's sockets are dealt out by Take, never found by touch)
                            var best = (D: float.MaxValue, Their: "", Own: "");
                            var seenAt = new HashSet<Vector3>();
                            foreach (var ts in t.Def.Sockets)
                            {
                                if (_used.Contains((j, ts.Name)))
                                    continue;
                                var tp = ToBody(t, ts.At);
                                if (!seenAt.Add(tp))
                                    continue;
                                for (var i = 0; i < mine.Length; i++)
                                {
                                    if (_keep?.Contains(s.Def.Sockets[i].Name) == true)
                                        continue;
                                    var d = Vector3.DistanceSquared(tp, mine[i]);
                                    if (d < best.D)
                                        best = (d, ts.Name, s.Def.Sockets[i].Name);
                                }
                            }
                            if (best.D < float.MaxValue)
                                found.Add((MathF.Sqrt(best.D), j, best.Their, best.Own));
                        }
                    }
        }

        private void Bounds(Step s)
        {
            var d = s.Def;
            Vector3 mn = d.BoundsMin, mx = d.BoundsMax;
            if (!(mn.X <= mx.X))
                (mn, mx) = (Vector3.Zero, Vector3.Zero); // (a part with no solid box)
            var lo = new Vector3(float.MaxValue);
            var hi = new Vector3(float.MinValue);
            for (var i = 0; i < 8; i++)
            {
                var c = new Vector3((i & 1) == 0 ? mn.X : mx.X, (i & 2) == 0 ? mn.Y : mx.Y, (i & 4) == 0 ? mn.Z : mx.Z);
                var w = ToBody(s, c);
                lo = Vector3.Min(lo, w);
                hi = Vector3.Max(hi, w);
            }
            (s.Min, s.Max) = (lo, hi);
        }

        /// <summary>To the centimetre, as the game stands a part (ConstructWorld.Quantise).</summary>
        public static Vector3 Q(Vector3 v) => ConstructWorld.Quantise(v);

        // ---- The file

        public string Text(string name, string description, List<PyDict> carry)
        {
            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append("  \"format\": ").Append(PyJson.Dumps(ConstructBuild.BlueprintFormat)).Append(",\n");
            sb.Append("  \"name\": ").Append(PyJson.Dumps(name)).Append(",\n");
            sb.Append("  \"description\": ").Append(PyJson.Dumps(description)).Append(",\n");
            sb.Append("  \"exactLinks\": true,\n");
            sb.Append("  \"steps\": [\n");
            for (var k = 0; k < Steps.Count; k++)
                sb.Append("    ").Append(PyJson.Dumps(StepDict(Steps[k]))).Append(k + 1 < Steps.Count ? ",\n" : "\n");
            sb.Append("  ]");
            if (carry.Count > 0)
            {
                sb.Append(",\n  \"carry\": [\n");
                for (var i = 0; i < carry.Count; i++)
                    sb.Append("    ").Append(PyJson.Dumps(carry[i])).Append(i + 1 < carry.Count ? ",\n" : "\n");
                sb.Append("  ]");
            }
            sb.Append("\n}\n");
            return sb.ToString();
        }

        private static PyDict StepDict(Step s)
        {
            var d = new PyDict { ["part"] = s.Part };
            if (s.Weld is { } w)
            {
                d["weld"] = new PyDict // (the weld names its own anchor)
                {
                    ["on"] = s.On,
                    ["pos"] = Py.L((double)w.Pos.X, (double)w.Pos.Y, (double)w.Pos.Z),
                    ["rot"] = Py.L((double)w.Rot.X, (double)w.Rot.Y, (double)w.Rot.Z, (double)w.Rot.W),
                };
            }
            else
            {
                if (s.On >= 0)
                    d["on"] = s.On;
                if (s.Socket.Length > 0)
                {
                    d["socket"] = s.Socket;
                    d["by"] = s.By;
                }
                if (s.Spin != 0)
                    d["spin"] = s.Spin;
                if (s.At is { } a)
                    d["at"] = Py.L(Cm(a.X), Cm(a.Y), Cm(a.Z));
            }
            if (s.Also is { Count: > 0 } also)
                d["also"] = also.Select(x => (object?)Py.L(x.On, x.Their, x.Own)).ToList();
            if (s.Config != null)
                d["config"] = s.Config;
            if (s.Paint != null)
                d["paint"] = s.Paint;
            if (s.Finish != null)
                d["finish"] = s.Finish;
            if (s.OrdersOnly)
                d["ordersOnly"] = true;
            return d;
        }

        private static double Cm(float v)
        {
            var r = Math.Round((double)v, 2);
            return r == 0 ? 0.0 : r;
        }
    }
}
