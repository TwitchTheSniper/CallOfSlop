using System.Numerics;
using System.Text;

namespace CallOfSlop;

/// <summary>
/// The Ordnance Bench's server side (docs/sandbox/CONSTRUCTS.md, "Modular ordnance"): a design comes in as its key, and the bench in reach makes the
/// bomb or missile, a loose run-time part beside it. Creative: free. Survival: its section parts come out of the hands, the backpack and the bench's slots.
/// </summary>
public sealed partial class Game
{
    private readonly Dictionary<string, int> _ordHave = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Func<ConstructPartDef, string> OrdNameOf = d => d.Ord != null ? d.Name : "";

    /// <summary>Client: asks the bench in reach to build this design.</summary>
    public void SandboxSendOrdnance(string key)
    {
        var w = SbW;
        var bytes = Encoding.UTF8.GetBytes(key);
        w.Begin(Msg.CAct).U8((byte)CActKind.Ordnance).U32((uint)bytes.Length).Bytes(bytes);
        SandboxSendBuilt(w);
    }

    /// <summary>The nearest Ordnance Bench within reach, or null.</summary>
    private (Construct Body, int Part)? FindOrdnanceBench(Vector3 eye)
    {
        (Construct, int)? best = null;
        var most = ConstructEdits.BenchReach;
        foreach (var c in Constructs.Bodies)
            for (var i = 0; i < c.PartCount; i++)
                if (c.DefOf(i).IsOrdnanceBench && Vector3.Distance(eye, ConstructEdits.BenchCentre(c, i)) is var d && d <= most)
                {
                    most = d;
                    best = (c, i);
                }
        return best;
    }

    /// <summary>Server: one build. Returns why it was refused, or null.</summary>
    private string? SandboxOrdnanceBuild(Player p, string key)
    {
        if (FindOrdnanceBench(p.Eye) is not { } at)
            return "You're not at an Ordnance Bench.";
        var (bench, part) = at;
        if (OrdnanceDesign.Parse(key, out var why) is not { } design)
            return $"That design doesn't read: {why}.";
        var st = design.Stats();
        if (st.Problem.Length > 0)
            return $"That design can't be built: {st.Problem}.";
        if (Ordnance.Def(design) is not { } def)
            return "The game can't hold another new design this run.";
        var hands = SandboxHands;
        if (!SandboxPartsMenu)
        {
            var cube = design.Scale * design.Scale * design.Scale;
            var each = Math.Max(1, (int)MathF.Ceiling(cube));
            var want = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var piece in design.Pieces)
                want[piece.Section.Name] = want.GetValueOrDefault(piece.Section.Name) + piece.Items(each); // (one part for each own length begun)
            hands.BenchCollect(Constructs, p.Id, bench, part, _ordHave, OrdNameOf);
            foreach (var (name, n) in want)
                if (_ordHave.GetValueOrDefault(name) < n)
                    return $"You don't have enough {name} (have {_ordHave.GetValueOrDefault(name)}, this needs {n}).";
            foreach (var (name, n) in want)
                for (var i = 0; i < n; i++)
                    hands.BenchTake(Constructs, p.Id, bench, part, name, OrdNameOf);
        }
        var centre = ConstructEdits.BenchCentre(bench, part);
        var rise = 1.2f + (def.BoundsMax - def.BoundsMin).Length() * 0.5f;
        var c = Constructs.Create(centre + GravityFrames.UpAt(centre) * rise, Quaternion.Identity);
        if (c == null)
            return "The world is full.";
        Constructs.AddPart(c, def, Vector3.Zero, 0);
        c.Owner = p.Id;
        c.Rebuild();
        hands.Notices.Add((p.Id, $"Ordnance Bench: {def.Description}"));
        return null;
    }
}
