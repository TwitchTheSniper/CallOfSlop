using System.Diagnostics;
using System.IO.Compression;
using System.Numerics;
using System.Text;

namespace CallOfSlop;

/// <summary>What a selection is asked to do.</summary>
public enum SelAct : byte
{
    Delete, // the parts come off, back to the network, the hands or the ground
    Paint, // u8 mode (ConstructPaint.PaintMode), 3 x u8 colour
    Weld, // every free socket that meets another, and any construct they touch, welds
    Copy, // the server keeps a clip of them (paste with CActKind.Paste)
    Move, // a ClipPlace: they move there
}

/// <summary>
/// The server's half of the Selector Station and the Blueprint Desk (docs/sandbox/CONSTRUCTS.md, "Build stations"). A client
/// sends its selection in chunks (<see cref="MaxChunk"/> part indices each, by a stamp of where those parts are, so a selection
/// made on a construct that has since changed is refused) and then what to do with it. A stamp's blueprint comes up as
/// compressed text. Anything that could take too long for a tick is a job: one a player, worked on at
/// <see cref="BudgetMs"/> a tick, over all players, from <see cref="SelectTick"/>. Structure changes all at once, in one edit,
/// through the normal paths (<see cref="ConstructWorld.CutOut"/>, <see cref="ConstructWorld.CopyPiece"/>, RemoveParts, Merge).
/// </summary>
public sealed partial class ConstructEdits
{
    public const int MaxChunk = 256; // part indices a message
    public const int MaxUpload = 1 << 21; // compressed bytes of a blueprint
    public const int MaxBlueprintText = 1 << 24; // its text
    public const int UploadChunk = 1024;
    public const float AidReach = 64f; // metres from the eye a selection or a placed group may be
    public const int MaxDrops = 16; // parts a delete hands back one at a time; more with nowhere to go stay together as a body
    public const double BudgetMs = 2;

    /// <summary>What a player's beacons show them (the game sets it).</summary>
    public Func<byte, BeaconView>? BeaconOf;

    private sealed class SelectState
    {
        public ushort Body;
        public uint Hash;
        public ushort[] Items = [];
        public int Total, Got;
        public ConstructClip? Clip;
        public bool ClipStamped; // it came from a blueprint (the Desk's), not a copy (the Selector's)
        public MemoryStream? Upload;
        public int UploadLength;
        public Task<ConstructBuild.Blueprint>? Parsing;
        public ConstructWorld? BuildWorld;
        public IEnumerator<int>? Steps;
        public ConstructBuild.BuildRun? Run;
        public string BuildName = "";
        public bool Placing; // a paste asked for while the clip is still being built
        public ClipPlace Pending;
        public SelectJob? Job;
        public Queue<SelectJob>? Waiting; // (a selection over many constructs: one job after another)
    }

    private enum JobKind : byte { Paste, Move, Weld, Refund }

    private sealed class SelectJob
    {
        public JobKind Kind;
        public int Cursor, Phase;
        public ConstructClip? Clip;
        public ClipPlace Place;
        public ushort Body;
        public ushort TargetTag;
        public uint Hash;
        public ushort[] Items = [];
        public Vector3 BodyPos;
        public Quaternion Rot;
        public byte Turn;
        public Vector3 Offset;
        public bool[]? Skip;
        public List<(ConstructPartDef Def, int N)> Need = new();
        public List<(ushort Def, Vector3 At)> Paid = new();
        public List<Construct> Loose = new();
        public List<(ConstructPartDef Def, Vector3 At, bool Hand)> Refund = new();
        public int Took, Missing, Welds, Joined, Back, Dropped, Left;
    }

    private readonly SelectState?[] _select = new SelectState?[256];
    private int _selectBusy; // (players with anything to do, so an idle tick costs nothing)

    private SelectState Sel(byte player) => _select[player] ??= new SelectState();

    private void SelectResetAll()
    {
        for (var i = 0; i < _select.Length; i++)
            _select[i] = null;
        _selectBusy = 0;
    }

    private void SelectResetPlayer(byte player) => _select[player] = null;

    /// <summary>A stamp of where some parts are, so a client's selection is known to be the same parts here (never their ids: extrusions differ between machines).</summary>
    public static uint SelectHash(Construct c, ReadOnlySpan<ushort> items)
    {
        var h = 2166136261u;
        foreach (var i in items)
        {
            if (i >= c.PartCount)
                return 0;
            ref var p = ref c.Parts[i];
            h = (h ^ i) * 16777619u;
            h = (h ^ (uint)(int)MathF.Round(p.Pos.X * 100)) * 16777619u;
            h = (h ^ (uint)(int)MathF.Round(p.Pos.Y * 100)) * 16777619u;
            h = (h ^ (uint)(int)MathF.Round(p.Pos.Z * 100)) * 16777619u;
            h = (h ^ p.Rot) * 16777619u;
        }
        return h;
    }

    // ---- A selection comes in ------------------------------------------------------------------------------------

    public string? SelectBegin(byte player, ushort body, int total, uint hash)
    {
        if (total <= 0 || total > Construct.MaxParts)
            return "too many parts selected";
        var s = Sel(player);
        if (s.Items.Length < total)
            s.Items = new ushort[total];
        s.Body = body;
        s.Hash = hash;
        s.Total = total;
        s.Got = 0;
        return null;
    }

    public string? SelectChunk(byte player, ref PacketReader r)
    {
        var s = Sel(player);
        int n = r.U16();
        if (n > MaxChunk || s.Got + n > s.Total)
            return "that selection doesn't add up";
        for (var k = 0; k < n; k++)
            s.Items[s.Got++] = r.U16();
        return null;
    }

    private string? SelectReady(ConstructWorld world, byte player, out Construct c, out SelectState s)
    {
        c = null!;
        s = Sel(player);
        ref var h = ref Hands[player];
        if (h.Seated)
            return "get out of the seat first";
        if (h.Tool != ConstructTool.Selector)
            return "take out the selector (the tool wheel)";
        if (s.Job is { Kind: JobKind.Paste or JobKind.Move })
            return "still working on the last one";
        if (s.Total == 0 || s.Got != s.Total)
            return "nothing is selected";
        if (world.Get(s.Body) is not { } found)
            return "that construct is gone";
        c = found;
        var items = s.Items.AsSpan(0, s.Total);
        foreach (var i in items)
            if (i >= c.PartCount)
                return "that construct has changed: select again";
        if (SelectHash(c, items) != s.Hash)
            return "that construct has changed: select again";
        return null;
    }

    private bool[] MarksOf(Construct c, SelectState s)
    {
        var marks = new bool[c.PartCount];
        for (var k = 0; k < s.Total; k++)
            marks[s.Items[k]] = true;
        return marks;
    }

    /// <summary>What a selection is asked to do. Null, or why not.</summary>
    public string? SelectDo(ConstructWorld world, byte player, Vector3 eye, SelAct act, PaintMode mode, uint colour, in ClipPlace place)
    {
        if (AidWhy(world, player, eye, BuildAid.Select) is { } off)
            return off;
        if (SelectReady(world, player, out var c, out var s) is { } why)
            return why;
        if (Vector3.Distance(eye, c.WorldCom) > AidReach + c.Radius)
            return "too far away";
        if (Contested(c, player) is { } defended)
            return defended;
        switch (act)
        {
            case SelAct.Paint:
                return SelectPaint(world, c, s, mode, colour);
            case SelAct.Copy:
                var clip = ConstructClip.Capture(c, s.Items.AsSpan(0, s.Total), out var bad, world);
                if (clip == null)
                    return bad;
                s.Clip = clip;
                s.ClipStamped = false;
                Done.Add($"player {player}: copied {clip.Parts} part(s)");
                return null;
        }
        if (RemoverRefusal(c, player, InfiniteParts, null) is { } refused)
            return refused;
        switch (act)
        {
            case SelAct.Delete:
                return SelectDelete(world, player, c, s, eye);
            case SelAct.Weld:
                Start(s, new SelectJob { Kind = JobKind.Weld, Body = c.Id, Items = s.Items[..s.Total] });
                return null;
            case SelAct.Move:
                return BeginPlace(world, player, eye, place, c, s);
        }
        return "no such action";
    }

    private void Start(SelectState s, SelectJob job)
    {
        _selectBusy++;
        if (s.Job == null)
            s.Job = job;
        else
            (s.Waiting ??= new Queue<SelectJob>()).Enqueue(job);
    }

    private static string? SelectPaint(ConstructWorld world, Construct c, SelectState s, PaintMode mode, uint colour)
    {
        if (mode == PaintMode.Reset)
            colour = 0;
        else if (colour == 0 || !ConstructPaint.Valid(colour))
            return "no such colour";
        for (var k = 0; k < s.Total; k++)
            SetPart(world, c, s.Items[k], colour);
        return null;
    }

    /// <summary>
    /// A selection comes off in one edit. Creative: gone. Survival: back to the player: what a chute network takes goes there, the
    /// rest (physics-only parts, wrecked ones, parts with no network in reach) to the hands, the backpack or the ground up to
    /// <see cref="MaxDrops"/>; more than that stays together as a body where it was, to be taken apart by hand.
    /// </summary>
    private string? SelectDelete(ConstructWorld world, byte player, Construct c, SelectState s, Vector3 eye)
    {
        ReleaseGrabs(world, c);
        var marks = MarksOf(c, s);
        if (InfiniteParts)
        {
            var gone = world.RemoveParts(c, marks);
            Done.Add($"player {player}: deleted {gone} part(s)");
            return null;
        }
        var net = NetworkOf(world, player, eye);
        var job = new SelectJob { Kind = JobKind.Refund };
        var stay = new bool[s.Total];
        var stays = 0;
        for (var k = 0; k < s.Total; k++)
        {
            var d = c.DefOf(s.Items[k]);
            if (d.PhysicsOnly && !d.RuntimeMade || c.Parts[s.Items[k]].Broken || net == 0) // (a made part goes back onto a rack on the network: inventories phase 4)
            {
                stay[k] = true;
                stays++;
            }
        }
        var keepPiece = stays > MaxDrops;
        var doomed = new List<int>(s.Total);
        for (var k = 0; k < s.Total; k++)
        {
            var i = s.Items[k];
            if (keepPiece && stay[k])
                continue;
            ref var p = ref c.Parts[i];
            c.WorldFrame(p.Seg, out var q, out var t);
            c.PartBounds(i, out var mn, out var mx);
            if (!p.Broken)
                job.Refund.Add((p.D, t + Vector3.Transform((mn + mx) * 0.5f, q), stay[k]));
            doomed.Add(i);
        }
        // Everything selected leaves as one body (one edit, one split of what stays), then what is handed back leaves that.
        if (world.CutOut(c, marks, player) is not { } piece)
            return "the world is full";
        if (keepPiece)
        {
            var off = new bool[piece.PartCount];
            var kept = 0;
            for (var i = 0; i < piece.PartCount; i++)
            {
                var d = piece.DefOf(i);
                off[i] = !(d.PhysicsOnly || piece.Parts[i].Broken || net == 0);
                kept += off[i] ? 0 : 1;
            }
            world.RemoveParts(piece, off);
            job.Left = kept;
        }
        else
        {
            world.SpillAll(piece);
            world.Remove(piece);
        }
        if (job.Refund.Count > 0 || job.Left > 0)
            Start(s, job);
        Done.Add($"player {player}: took {doomed.Count} part(s) off");
        return null;
    }

    // ---- Paste and stamp -----------------------------------------------------------------------------------------

    /// <summary>The client's clip goes down: a copy or a stamp, as many times as it likes (they keep the clip). Null, or why not.</summary>
    public string? Paste(ConstructWorld world, byte player, Vector3 eye, in ClipPlace place)
    {
        var s = Sel(player);
        ref var h = ref Hands[player];
        if (h.Seated)
            return "get out of the seat first";
        if (h.Tool != ConstructTool.Selector)
            return "take out the selector (the tool wheel)";
        if (s.Job != null)
            return "still working on the last one";
        if (s.Parsing != null || s.Steps != null || s.Upload != null)
        {
            s.Placing = true; // (it goes when the clip is ready)
            s.Pending = place;
            return null;
        }
        return BeginPlace(world, player, eye, place, null, s);
    }

    private string? BeginPlace(ConstructWorld world, byte player, Vector3 eye, in ClipPlace place, Construct? moving, SelectState s)
    {
        var bad = "";
        var clip = moving != null ? ConstructClip.Capture(moving, s.Items.AsSpan(0, s.Total), out bad) : s.Clip; // (a move keeps the parts themselves, and their ropes by tag: the clip needs none)
        if (clip == null)
            return moving != null ? bad : "nothing to place: copy something or pick a blueprint";
        if (AidWhy(world, player, eye, moving == null && s.ClipStamped ? BuildAid.Blueprint : BuildAid.Select) is { } off)
            return off;
        if (moving == null && (ConstructPlace.LimitRefusal(world, player) ?? world.CapRefusal()) is { } full)
            return full;
        if (moving is { MergeCount: > 0 } && moving.PartCount != clip.Parts) // (a whole construct moves as it is)
            for (var k = 0; k < s.Total; k++)
                if (moving.MergeOf[s.Items[k]] != 0)
                    return "take a merged receiver apart before moving part of it";
        var job = new SelectJob { Kind = moving != null ? JobKind.Move : JobKind.Paste, Clip = clip, Place = place, Rot = place.Rot };
        if (moving != null)
        {
            job.Body = moving.Id;
            job.Hash = s.Hash;
            job.Items = s.Items[..s.Total];
        }
        if (place.Attach)
        {
            if (world.Get(place.Body) is not { } target)
                return "that construct is gone";
            if (Contested(target, player) is { } defended)
                return defended;
            if (SalvageRefusal(target, player) is { } theirs)
                return theirs;
            if (target.Driver != 255)
                return "someone is driving it";
            if (target.Dirty)
                target.Rebuild();
            if (clip.AttachPose(target, place.Part, place.Socket, place.ClipPart, place.ClipSocket, place.Spin, out job.Turn, out job.Offset) is { } no)
                return no;
            if (target == moving)
                job.Skip = MarksOf(moving, s);
            if (Vector3.Distance(eye, ReachPoint(target, place.Part, eye)) > AidReach)
                return "too far away";
            job.TargetTag = target.Parts[place.Part].Tag;
        }
        else
        {
            if (!float.IsFinite(place.Pos.X + place.Pos.Y + place.Pos.Z + place.Rot.X + place.Rot.Y + place.Rot.Z + place.Rot.W) || place.Rot.LengthSquared() < 0.5f)
                return "bad place";
            if (Vector3.Distance(eye, place.Pos) > AidReach)
                return "too far away";
            job.Rot = Quaternion.Normalize(place.Rot);
            job.BodyPos = ConstructClip.BodyPos(clip.Origin, place.Pos, job.Rot);
        }
        if (moving == null && !InfiniteParts && SetNeed(world, player, eye, clip, job) is { } missing)
            return missing;
        Start(s, job);
        return null;
    }

    /// <summary>Survival: what a clip takes against what the player reaches (a beacon's view, what they carry, loose bodies near). Null when it is all there, else what is missing.</summary>
    private string? SetNeed(ConstructWorld world, byte player, Vector3 eye, ConstructClip clip, SelectJob job)
    {
        var view = NetworkOf(world, player, eye) != 0 ? BeaconOf?.Invoke(player) : null;
        var inv = Inventory?.Of(player);
        var loose = new Dictionary<ushort, int>();
        foreach (var b in world.Bodies)
            if (LooseFor(b, player, eye, out var d))
                loose[d.Id] = loose.GetValueOrDefault(d.Id) + 1;
        var kinds = 0;
        var missing = 0;
        var list = new StringBuilder();
        foreach (var (id, n) in clip.Cost())
        {
            if (ConstructParts.Get(id) is not { } d)
                continue;
            job.Need.Add((d, n));
            var have = ClipHave(d, view, inv, loose.GetValueOrDefault(id));
            if (have >= n)
                continue;
            missing += n - have;
            if (kinds++ < 4)
                list.Append(kinds == 1 ? "" : ", ").Append(d.Name).Append(" x").Append(n - have);
        }
        return missing == 0 ? null : $"{MissingLine(missing, clip.Parts)}: {list}{(kinds > 4 ? ", ..." : "")}";
    }

    /// <summary>How many of a part a player can pay with: on the network as they see it, carried, and for a physics-only part (never stored) loose near them.</summary>
    public static int ClipHave(ConstructPartDef d, BeaconView? view, SandboxInventory? inv, int loose) =>
        d.PhysicsOnly ? loose : (view != null && d.Id < view.Counts.Length ? view.Counts[d.Id] : 0) + (inv != null && d.Id < inv.Parts.Length ? inv.Parts[d.Id] : 0);

    public static string MissingLine(int missing, int parts) => $"{missing} of {parts} part{(parts == 1 ? "" : "s")} missing";

    /// <summary>Is this body a loose part a paste near <paramref name="eye"/> can use up?</summary>
    public static bool LooseFor(Construct b, byte player, Vector3 eye, out ConstructPartDef def)
    {
        def = null!;
        if (!b.Loose || b.Driver != 255 || b.GrabbedBy != 255 || b.CarriedBy != 0 || !Factions.Shares(b.Owner, player) && b.Owner != 255 || Vector3.DistanceSquared(b.Pos, eye) > AidReach * AidReach)
            return false;
        def = b.DefOf(0);
        return true;
    }

    // ---- A blueprint comes up ------------------------------------------------------------------------------------

    public string? ClipBegin(byte player, int length)
    {
        if (length <= 0 || length > MaxUpload)
            return "that blueprint is too big";
        var s = Sel(player);
        s.Upload = new MemoryStream(length);
        s.UploadLength = length;
        s.Parsing = null;
        s.Steps = null;
        s.Run = null;
        s.BuildWorld = null;
        s.Clip = null;
        s.Placing = false;
        return null;
    }

    public string? ClipChunk(byte player, ReadOnlySpan<byte> bytes)
    {
        var s = Sel(player);
        if (s.Upload == null || s.Upload.Length + bytes.Length > s.UploadLength)
            return "that blueprint doesn't add up";
        s.Upload.Write(bytes);
        if (s.Upload.Length < s.UploadLength)
            return null;
        // Read away from the game thread; its steps are built at the next ticks, a few at a time.
        var data = s.Upload.ToArray();
        s.Upload = null;
        s.Parsing = Task.Run(() => ConstructBuild.ParseBlueprint(Inflate(data), "blueprint"));
        _selectBusy++;
        return null;
    }

    /// <summary>A blueprint's compressed text as text, within <see cref="MaxBlueprintText"/>.</summary>
    public static string Inflate(byte[] data)
    {
        using var input = new MemoryStream(data);
        using var z = new DeflateStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        var buffer = new byte[16384];
        for (int n; (n = z.Read(buffer, 0, buffer.Length)) > 0;)
        {
            output.Write(buffer, 0, n);
            if (output.Length > MaxBlueprintText)
                throw new ConstructPartException("that blueprint is too big");
        }
        return Encoding.UTF8.GetString(output.GetBuffer(), 0, (int)output.Length);
    }

    public static byte[] Deflate(string text)
    {
        using var output = new MemoryStream();
        using (var z = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true))
            z.Write(Encoding.UTF8.GetBytes(text));
        return output.ToArray();
    }

    // ---- The ticks -----------------------------------------------------------------------------------------------

    /// <summary>Every tick: the jobs, within <see cref="BudgetMs"/> over all players. An idle tick costs one comparison.</summary>
    public void SelectTick(ConstructWorld world, Func<byte, Vector3?> eyeOf)
    {
        if (_selectBusy <= 0)
            return;
        var until = Stopwatch.GetTimestamp() + (long)(BudgetMs * Stopwatch.Frequency / 1000);
        var busy = 0;
        for (var pid = 0; pid < _select.Length; pid++)
        {
            if (_select[pid] is not { } s)
                continue;
            if (Stopwatch.GetTimestamp() >= until)
            {
                busy += s.Job != null || s.Parsing != null || s.Steps != null || s.Waiting is { Count: > 0 } ? 1 : 0;
                continue;
            }
            var player = (byte)pid;
            try
            {
                if (s.Job == null && s.Waiting is { Count: > 0 } waiting)
                    s.Job = waiting.Dequeue();
                if (s.Parsing != null || s.Steps != null)
                    ClipBuildTick(world, player, s, until, eyeOf);
                if (s.Job != null && eyeOf(player) is { } eye)
                    JobTick(world, player, eye, s, s.Job, until);
                else if (s.Job != null)
                    s.Job = null; // (left, or dead: what was asked goes with them)
            }
            catch (Exception ex)
            {
                // Never a crash: the job goes and is logged (a bug to fix, not a game to end).
                Console.WriteLine($"[sandbox] error: player {pid}'s selection job threw: {ex}");
                s.Job = null;
                s.Parsing = null;
                s.Steps = null;
            }
            busy += s.Job != null || s.Parsing != null || s.Steps != null || s.Waiting is { Count: > 0 } ? 1 : 0;
        }
        _selectBusy = busy;
    }

    private void ClipBuildTick(ConstructWorld world, byte player, SelectState s, long until, Func<byte, Vector3?> eyeOf)
    {
        if (s.Parsing is { } parsing)
        {
            if (!parsing.IsCompleted)
                return;
            s.Parsing = null;
            if (parsing.Exception is { } ex)
            {
                Notices.Add((player, $"that blueprint can't be read: {ex.InnerException?.Message}"));
                s.Placing = false;
                return;
            }
            var bp = parsing.Result;
            if (bp.Steps.Count > ConstructClip.MaxParts)
            {
                Notices.Add((player, "that blueprint is too big"));
                s.Placing = false;
                return;
            }
            s.BuildName = bp.Name;
            s.BuildWorld = new ConstructWorld { CreativeRecycling = InfiniteParts }; // (creative-only settings keep or go as the real world would)
            s.Run = new ConstructBuild.BuildRun();
            s.Steps = ConstructBuild.BuildSteps(s.BuildWorld, bp, Vector3.Zero, Quaternion.Identity, s.Run).GetEnumerator();
        }
        if (s.Steps is not { } steps)
            return;
        while (Stopwatch.GetTimestamp() < until)
            if (!steps.MoveNext())
            {
                s.Steps = null;
                if (s.Run!.Built is { } built)
                {
                    s.Clip = ConstructClip.Of(built, s.BuildName, s.BuildWorld);
                    s.ClipStamped = true;
                }
                else
                {
                    Notices.Add((player, $"that blueprint doesn't build here: {s.Run.Why}"));
                    s.Placing = false;
                }
                s.BuildWorld = null;
                s.Run = null;
                if (s.Placing && eyeOf(player) is { } eye)
                {
                    s.Placing = false;
                    if (BeginPlace(world, player, eye, s.Pending, null, s) is { } why)
                        Notices.Add((player, why));
                }
                return;
            }
    }

    private void JobTick(ConstructWorld world, byte player, Vector3 eye, SelectState s, SelectJob job, long until)
    {
        switch (job.Kind)
        {
            case JobKind.Weld:
                WeldTick(world, player, s, job, until);
                break;
            case JobKind.Refund:
                RefundTick(world, player, eye, s, job, until);
                break;
            default:
                PlaceTick(world, player, eye, s, job, until);
                break;
        }
    }

    private void Finish(SelectState s, byte player, string line)
    {
        s.Job = null;
        Notices.Add((player, line));
    }

    private void PlaceTick(ConstructWorld world, byte player, Vector3 eye, SelectState s, SelectJob job, long until)
    {
        var clip = job.Clip!;
        var source = job.Kind == JobKind.Move ? world.Get(job.Body) : null;
        if (job.Kind == JobKind.Move && source == null)
        {
            Finish(s, player, "that construct is gone");
            return;
        }
        var target = job.Place.Attach ? world.Get(job.Place.Body) : null;
        if (job.Place.Attach && target == null)
        {
            Finish(s, player, "that construct is gone");
            return;
        }
        // Phase 0: does it fit?
        while (job.Phase == 0)
        {
            if (job.Cursor >= clip.Parts)
            {
                job.Phase = 1;
                job.Cursor = 0;
                break;
            }
            if (Stopwatch.GetTimestamp() >= until)
                return;
            if (job.Place.Attach)
            {
                if (target!.Dirty)
                    target.Rebuild();
                if (clip.Overlaps(target, job.Turn, job.Offset, job.Skip, job.Cursor, 8))
                {
                    Finish(s, player, "something is in the way");
                    return;
                }
                job.Cursor += 8;
                continue;
            }
            var fp = clip.PartPlace(job.Cursor, job.BodyPos, job.Rot);
            if (ConstructPlace.Fits(world, Query, clip.Body.DefOf(job.Cursor), fp, Feet(world), source) is { } no)
            {
                Finish(s, player, no);
                return;
            }
            job.Cursor++;
        }
        // Phase 1: paying (survival, a paste): the network, then what they carry, loose bodies for physics-only parts. Short: all goes back, and says why.
        while (job.Phase == 1)
        {
            if (job.Kind == JobKind.Move || InfiniteParts)
            {
                job.Phase = 2;
                break;
            }
            if (job.Cursor >= job.Need.Count)
            {
                if (job.Missing > 0)
                {
                    Unpay(world, player, eye, job);
                    Finish(s, player, MissingLine(job.Missing, clip.Parts));
                    return;
                }
                job.Phase = 2;
                break;
            }
            if (Stopwatch.GetTimestamp() >= until)
                return;
            var (def, n) = job.Need[job.Cursor];
            if (def.PhysicsOnly && !def.RuntimeMade && !InfiniteParts) // (a made part: TakeForAid, the count, a rack, a loose piece)
            {
                job.Took = 0;
                foreach (var b in world.Bodies)
                    if (job.Took < n && LooseFor(b, player, eye, out var d) && d.Id == def.Id)
                    {
                        job.Loose.Add(b);
                        job.Took++;
                    }
                job.Missing += n - job.Took;
                foreach (var b in job.Loose)
                {
                    job.Paid.Add((def.Id, b.Pos));
                    world.Remove(b);
                }
                job.Loose.Clear();
                job.Took = 0;
                job.Cursor++;
                continue;
            }
            if (job.Took < n && TakeForAid(world, player, def, eye))
            {
                job.Took++;
                job.Paid.Add((def.Id, eye));
                continue;
            }
            job.Missing += n - job.Took;
            job.Took = 0;
            job.Cursor++;
        }
        // Phase 2: the one edit.
        if (job.Kind == JobKind.Move)
            CommitMove(world, player, s, job, source!, target);
        else
            CommitPaste(world, player, eye, s, job, target);
    }

    /// <summary>A paste that came up short: what it took goes back.</summary>
    private void Unpay(ConstructWorld world, byte player, Vector3 eye, SelectJob job)
    {
        var net = NetworkOf(world, player, eye);
        foreach (var (id, at) in job.Paid)
            if (ConstructParts.Get(id) is { RuntimeMade: true } m)
                ReturnMade(world, player, m, eye, at); // (a rack on the network, the count, else a body: inventories phase 4)
            else if (ConstructParts.Get(id) is { } d && (net == 0 || d.PhysicsOnly || world.Beacons.PutPart(world, this, player, net, d) == null))
                Give(world, player, d, at);
        job.Paid.Clear();
    }

    private static void SetPose(Construct c, Vector3 pos, Quaternion rot)
    {
        c.Pos = c.PrevPos = c.NetPos = c.NetFromPos = pos;
        c.Rot = c.PrevRot = c.NetRot = c.NetFromRot = rot;
        c.Vel = c.AngVel = Vector3.Zero;
        c.NetHistReset();
    }

    private static void Hold(Construct c, byte player)
    {
        c.Frozen = true;
        c.FrozenBy = player;
    }

    private void CommitPaste(ConstructWorld world, byte player, Vector3 eye, SelectState s, SelectJob job, Construct? target)
    {
        var clip = job.Clip!;
        var order = new int[clip.Parts];
        for (var i = 0; i < order.Length; i++)
            order[i] = i;
        var body = world.CopyPiece(clip.Body, order, job.BodyPos, job.Rot, out var why, clip.World);
        if (body != null && job.Kind != JobKind.Move)
        {
            if (InfiniteParts)
                ConstructStoreVolume.FillAll(world, body); // (a creative paste: what the settings say)
            else
            {
                ConstructFluid.EmptyAll(body); // (a survival paste is new parts, paid for: its volumes arrive empty)
                ConstructStoreVolume.EmptyAll(body);
            }
        }
        if (body == null)
        {
            Unpay(world, player, eye, job);
            Finish(s, player, why);
            return;
        }
        body.Owner = player;
        Hold(body, player);
        var joined = false;
        if (target != null)
        {
            SetPose(body, target.Pos + Vector3.Transform(job.Offset, target.Rot), Quaternion.Normalize(target.Rot * Rot24.Quat(job.Turn)));
            joined = JoinTo(world, target, job.Place.Part, body);
        }
        if (!joined)
            world.SplitPieces(body);
        Done.Add($"player {player}: placed {clip.Parts} part(s) as construct {(joined ? target!.Id : body.Id)}");
        Finish(s, player, $"{clip.Parts} part{(clip.Parts == 1 ? "" : "s")} placed{(joined ? " and attached" : target != null ? " (not attached: they don't meet)" : ", frozen where they are")}");
    }

    private void CommitMove(ConstructWorld world, byte player, SelectState s, SelectJob job, Construct source, Construct? target)
    {
        var clip = job.Clip!;
        if (job.Items.Any(i => i >= source.PartCount) || SelectHash(source, job.Items) != job.Hash)
        {
            Finish(s, player, "that construct has changed: select again");
            return;
        }
        ReleaseGrabs(world, source);
        var marks = new bool[source.PartCount];
        foreach (var i in job.Items)
            marks[i] = true;
        if (target == source && marks[job.Place.Part])
        {
            Finish(s, player, "a group can't be put on itself");
            return;
        }
        if (target != null && target != source && target.Parts[job.Place.Part].Tag != job.TargetTag)
        {
            Finish(s, player, "that construct has changed");
            return;
        }
        var from = source.Id;
        if (world.CutOut(source, marks, player) is not { } piece)
        {
            Finish(s, player, "the world is full");
            return;
        }
        Hold(piece, player);
        var joined = false;
        if (target != null)
        {
            // (after the cut the part may have a new index, or gone with a piece that fell away: found by its tag)
            var part = -1;
            for (var i = 0; world.IsLive(target) && i < target.PartCount && part < 0; i++)
                if (target.Parts[i].Tag == job.TargetTag)
                    part = i;
            SetPose(piece, target.Pos + Vector3.Transform(job.Offset, target.Rot), Quaternion.Normalize(target.Rot * Rot24.Quat(job.Turn)));
            joined = part >= 0 && JoinTo(world, target, part, piece);
        }
        else
            SetPose(piece, job.BodyPos, job.Rot);
        piece.Dirty = true;
        piece.Rebuild();
        world.StructureDirty.Add(piece.Id);
        world.StructureDirty.Add(from);
        if (!joined)
            world.SplitPieces(piece);
        Done.Add($"player {player}: moved {job.Items.Length} part(s) of construct {from}");
        Finish(s, player, $"{job.Items.Length} part{(job.Items.Length == 1 ? "" : "s")} moved{(joined ? " and attached" : target != null ? " (not attached: they don't meet)" : ", frozen where they are")}");
    }

    /// <summary>Welds <paramref name="piece"/> into <paramref name="target"/> at its part: the normal join. False when they don't meet.</summary>
    private static bool JoinTo(ConstructWorld world, Construct target, int part, Construct piece)
    {
        if (target.Dirty)
            target.Rebuild();
        if (piece.Dirty)
            piece.Rebuild();
        return ConstructPlace.FindJoin(world, target, part, out var plan, only: piece) && world.Merge(target, plan);
    }

    private void WeldTick(ConstructWorld world, byte player, SelectState s, SelectJob job, long until)
    {
        if (world.Get(job.Body) is not { } c)
        {
            s.Job = null;
            return;
        }
        while (Stopwatch.GetTimestamp() < until)
        {
            if (job.Cursor >= job.Items.Length)
            {
                Finish(s, player, $"attached: {job.Welds} new join{(job.Welds == 1 ? "" : "s")}, {job.Joined} construct{(job.Joined == 1 ? "" : "s")} joined on");
                return;
            }
            int i = job.Items[job.Cursor++];
            if (i >= c.PartCount)
                continue;
            job.Welds += world.AutoLinkMany(c, i, i + 1);
            if (ConstructPlace.FindJoin(world, c, i, out var plan) && Contested(plan.Other, player) == null && SalvageRefusal(plan.Other, player) == null && world.CanMerge(c, plan.Other) == null && world.Merge(c, plan))
                job.Joined++; // (what joins is added after c's parts: the selection's indices stand)
        }
    }

    private void RefundTick(ConstructWorld world, byte player, Vector3 eye, SelectState s, SelectJob job, long until)
    {
        var net = NetworkOf(world, player, eye);
        while (Stopwatch.GetTimestamp() < until)
        {
            if (job.Cursor >= job.Refund.Count)
            {
                Finish(s, player, $"{job.Back} part{(job.Back == 1 ? "" : "s")} back on the network{(job.Dropped > 0 ? $", {job.Dropped} handed back or dropped" : "")}{(job.Left > 0 ? $", {job.Left} left as a body" : "")}");
                return;
            }
            var (def, at, hand) = job.Refund[job.Cursor++];
            if (!hand && (!def.PhysicsOnly || def.RuntimeMade) && net != 0 && world.Beacons.PutPart(world, this, player, net, def) != null)
            {
                job.Back++; // (a made part back onto a rack on the network: inventories phase 4)
                continue;
            }
            job.Dropped++;
            if (job.Dropped > MaxDrops && Inventory != null && Inventory.Of(player).Add(def.Id))
                continue; // (a full network: counted on the player, not a heap of bodies)
            if (def.RuntimeMade)
            {
                if (DropLoose(world, def, at, Quaternion.Identity) is { } freed)
                    freed.Owner = player; // (a made part is a body, never handed: the user's answer 1)
                else
                    Done.Add($"player {player}: the world is full: a refunded {PartRef.Shown(def)} is lost");
                continue;
            }
            Give(world, player, def, at);
        }
    }
}
