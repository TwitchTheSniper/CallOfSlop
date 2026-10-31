using System.Numerics;

namespace CallOfSlop;

/// <summary>What the held tool can do with the part under the crosshair: nothing (no highlight), it can (outline), or it
/// would be refused (a red outline and the reason).</summary>
public enum AimVerdict : byte
{
    None,
    Can,
    Refused,
}

/// <summary>A verdict and, for a refusal, its reason (a string that already exists: building a call never allocates).</summary>
public readonly struct AimCall
{
    public readonly AimVerdict Verdict;
    public readonly string? Why;

    private AimCall(AimVerdict verdict, string? why)
    {
        Verdict = verdict;
        Why = why;
    }

    public static readonly AimCall No = default;
    public static readonly AimCall Ok = new(AimVerdict.Can, null);
    public static AimCall Refuse(string why) => new(AimVerdict.Refused, why);

    /// <summary>The aim highlight shows (it can act, or it is refused with a reason).</summary>
    public bool Show => Verdict != AimVerdict.None;

    public bool Can => Verdict == AimVerdict.Can;

    /// <summary>A refusal's reason, or null (for the actions' own messages: a refused call is what they return).</summary>
    public string? Refusal => Verdict == AimVerdict.Refused ? Why : null;

    public override string ToString() => Verdict == AimVerdict.Refused ? $"refused: {Why}" : Verdict.ToString();
}

/// <summary>
/// Board item dev-sandbox-highlight-relevant-hr01: one predicate per tool, <see cref="CanAct"/>, that says whether the held
/// tool can act on the part under the crosshair. The aim highlight (the outline, the ghost sockets, the look line's action
/// hint) shows only for a target the tool can act on (yellow) or would refuse (red, with the reason), and the input code
/// asks the same predicate before it sends the action, so the highlight and the action never disagree. The server's own
/// checks call the shared pieces (<see cref="Remove"/>, <see cref="Grab"/>, <see cref="Configure"/>, <see cref="Paint"/>,
/// <see cref="Use"/>), which "Sandbox aim highlight" compares with the actions on scripted targets. It runs once a frame on the
/// aimed part only, takes the reasons it is given (decision 19's contested rule and the others need the game's players) and
/// allocates nothing.
/// </summary>
public static class SandboxAim
{
    /// <summary>Everything the predicate reads about the aim, the hands and the tool's own mode.</summary>
    public struct Target
    {
        public ConstructWorld World;
        public Construct Body;
        public int Part;
        public float Distance;
        public int SlotPlus1; // the hit is on a stored part (taken with F, not a tool)
        public byte Player;
        public bool Seated;
        public string? Contested; // decision 19, survival: why this player may not edit it now (null: may)
        public string? GrabWhy; // the physics gun's own refusal (Game.SandboxGrabRefusal / ConstructEdits.GrabRefusal)
        public string? ConfigWhy; // the configure tool's refusal (Game.SandboxConfigRefusal)
        public RouteType LinkType; // the linking tool's selected type ...
        public LinkMode LinkMode; // ... its mode ...
        public bool Routing; // ... and a link is being routed
        public PaintMode PaintMode;
        public bool Creative; // the map has infinite parts (the remover deletes anything there)
        public bool Freeform; // the welder is in freeform mode
        public bool OnSeam; // the aim is on a freeform seam (the welder cuts it)
    }

    /// <summary>The one predicate: can <paramref name="tool"/> act on <paramref name="t"/>? A tool's own action first, then what any hands can use (F works with every tool).</summary>
    public static AimCall CanAct(ConstructTool tool, in Target t)
    {
        if (t.Part < 0 || t.Part >= t.Body.PartCount)
            return AimCall.No;
        var own = ToolCall(tool, in t);
        if (own.Can)
            return own;
        var use = Use(tool, in t);
        if (use.Can)
            return use;
        return own.Show ? own : use;
    }

    private static AimCall ToolCall(ConstructTool tool, in Target t)
    {
        var c = t.Body;
        var d = c.DefOf(t.Part);
        switch (tool)
        {
            case ConstructTool.Wrench:
            case ConstructTool.Welder:
            {
                if (t.SlotPlus1 > 0)
                    return AimCall.No; // (a stored part is taken with F)
                if (tool == ConstructTool.Welder)
                {
                    if (t.OnSeam)
                        return AimCall.Ok;
                    if (t.Distance <= ConstructBuild.Reach && ConstructPlace.FindJoin(t.World, c, t.Part, out _))
                        return SalvageOrContested(t);
                    if (c.Loose && d.PhysicsOnly && !t.Freeform)
                        return SalvageOrContested(t); // (a physics-only piece: the socket weld's source)
                }
                return c.Loose ? AimCall.No : Remove(tool, c, t.Part, t.Player, t.Contested);
            }
            case ConstructTool.PhysicsGun:
                return Grab(t.GrabWhy);
            case ConstructTool.Linker:
                return Link(in t, d);
            case ConstructTool.Configure:
                return Configure(d, t.Contested, t.ConfigWhy);
            case ConstructTool.Paint:
                return t.Distance <= ConstructBuild.Reach ? Paint(d, t.PaintMode, t.Contested) : AimCall.No;
            case ConstructTool.Remover:
                return t.Distance <= ConstructBuild.Reach ? Remover(c, t.Player, t.Creative, t.Contested) : AimCall.No;
            default:
                return AimCall.No;
        }
    }

    /// <summary>The remover tool on any part (or link) of a construct: it can unless <see cref="ConstructEdits.RemoverRefusal"/> says no (driven, occupied, someone else's, defended).</summary>
    public static AimCall Remover(Construct c, byte player, bool creative, string? contested) =>
        ConstructEdits.RemoverRefusal(c, player, creative, contested) is { } why ? AimCall.Refuse(why) : AimCall.Ok;

    private static AimCall SalvageOrContested(in Target t) =>
        ConstructEdits.SalvageRefusal(t.Body, t.Player) is { } theirs ? AimCall.Refuse(theirs) : t.Contested is { } defended ? AimCall.Refuse(defended) : AimCall.Ok;

    /// <summary>
    /// The wrench or the welder takes a part off (decision 27: the tool is the part's: <see cref="ConstructBuild.ToolFor"/>).
    /// Not this tool's part or no tool: nothing. Else refused while someone drives it, when it is someone else's working
    /// construct, or when it is contested (the server's order). <paramref name="contested"/> is decision 19's reason.
    /// </summary>
    public static AimCall Remove(ConstructTool tool, Construct c, int part, byte player, string? contested)
    {
        if (part < 0 || part >= c.PartCount || !ConstructBuild.ToolFits(tool, c.DefOf(part)))
            return AimCall.No;
        if (c.Driver != 255)
            return AimCall.Refuse("someone is driving it");
        if (ConstructEdits.SalvageRefusal(c, player) is { } theirs)
            return AimCall.Refuse(theirs);
        if (contested != null)
            return AimCall.Refuse(contested);
        return AimCall.Ok;
    }

    /// <summary>The physics gun on any construct: it grabs (a frozen one too, which it unfreezes) unless <paramref name="grabWhy"/> (too heavy, contested, held by someone else, anchored, driven) says no.</summary>
    public static AimCall Grab(string? grabWhy) => grabWhy == null ? AimCall.Ok : AimCall.Refuse(grabWhy);

    /// <summary>The configure tool on a configurable part (contested first, then the tool's own refusal); a part with no settings: nothing.</summary>
    public static AimCall Configure(ConstructPartDef d, string? contested, string? configWhy)
    {
        if (!d.Configurable)
            return AimCall.No;
        var why = contested ?? configWhy;
        return why == null ? AimCall.Ok : AimCall.Refuse(why);
    }

    /// <summary>The paint tool on a paintable part (any part in the whole-construct mode), refused while contested.</summary>
    public static AimCall Paint(ConstructPartDef d, PaintMode mode, string? contested)
    {
        if (!ConstructPaint.Paintable(d) && mode != PaintMode.Whole)
            return AimCall.No;
        return contested == null ? AimCall.Ok : AimCall.Refuse(contested);
    }

    private static AimCall Link(in Target t, ConstructPartDef d)
    {
        if (t.Routing)
            return AimCall.Ok; // (a link in progress: the part is a surface for a node, or an input that ends it; the preview says which)
        if (t.LinkMode != LinkMode.Link)
            return AimCall.No; // (the edit modes act on links, which have their own outline)
        if (t.Distance > ConstructBuild.Reach + 1f)
            return AimCall.No;
        // LMB starts a link at an output of the selected type; RMB unwires an input or an output; R steps a setting.
        if (ConstructRoutes.IsOutput(d, t.LinkType) || ConstructRoutes.IsInput(d, t.LinkType) || d.Settings.Length > 0 || d.Configurable && d.IsSignal)
            return t.Contested == null ? AimCall.Ok : AimCall.Refuse(t.Contested);
        return AimCall.No;
    }

    /// <summary>
    /// What hands (or any tool: F works with all of them) can use without a tool: a seat, a door, hinge or joint, a button,
    /// switch, lever, selector or cargo volume, a hose reel or coupler, a hitch (with the wrench), a loose item to pick up,
    /// a stored part or a bin, the gun bench. Mirrors <see cref="ConstructEdits.Use"/> in the server's order.
    /// </summary>
    public static AimCall Use(ConstructTool tool, in Target t)
    {
        var c = t.Body;
        var d = c.DefOf(t.Part);
        ref var p = ref c.Parts[t.Part];
        if (t.SlotPlus1 > 0)
            return t.Distance <= ConstructBuild.UseReach ? AimCall.Ok : AimCall.No; // (a stored part: F takes it)
        if (d.IsBin && t.Distance <= ConstructBuild.UseReach)
            return AimCall.Ok;
        if (d.IsGunBench)
            return t.Distance <= ConstructBuild.UseReach + 1.4f ? AimCall.Ok : AimCall.No;
        if (d.Role == PartRole.Hose)
            return t.Distance <= ConstructBuild.UseReach + 0.5f && !t.Seated ? AimCall.Ok : AimCall.No;
        if (t.Distance > ConstructBuild.UseReach)
            return AimCall.No;
        if (c.Loose)
        {
            if (d.PhysicsOnly)
                return AimCall.Refuse($"physics only ({d.PhysicsWhy}): move it with the physics gun");
            if (t.World.StoreOf(c, 0) is { Empty: false })
                return AimCall.Refuse($"empty the {d.Name} first");
            return AimCall.Ok; // (F on a loose body picks it up, whatever the part is: the input sends PickUp first)
        }
        if (d.Role == PartRole.Seat)
        {
            if (p.Broken)
                return AimCall.Refuse("the seat is wrecked");
            if (p.SitterPlus1 != 0 || t.Part == c.DriveSeat && c.Driver != 255)
                return AimCall.Refuse("someone is already in it");
            return t.Seated ? AimCall.Refuse("you are already sitting") : AimCall.Ok;
        }
        if (d.Role == PartRole.Logic)
        {
            if (d.Logic is not (CLogic.Button or CLogic.Switch or CLogic.Cargo or CLogic.Lever or CLogic.Selector))
                return AimCall.No;
            return p.Broken ? AimCall.Refuse($"the {d.Name} is wrecked") : AimCall.Ok;
        }
        if (d.Role == PartRole.Hitch)
            return tool == ConstructTool.Wrench ? p.Broken ? AimCall.Refuse("that hitch is wrecked") : AimCall.Ok : AimCall.No;
        var seg = p.Seg;
        if (d.HasJoint && c.JointOf(t.Part) is var own and >= 0)
            seg = (byte)(own + 1);
        if (seg > 0)
        {
            ref var jt = ref c.Joints[seg - 1];
            if (jt.Locked)
                return AimCall.Refuse("it's locked");
            if (jt.Wired && jt.Motorised)
                return AimCall.Refuse("its wires run it");
            if (jt.Type is JointType.Turntable or JointType.Steering)
                return AimCall.Refuse("a turntable or steering pivot turns by its wires");
            return AimCall.Ok;
        }
        return AimCall.No;
    }
}
