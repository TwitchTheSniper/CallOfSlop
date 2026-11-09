using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The Hands tool on this PC (ConstructEditsHands.cs has the rules; docs/sandbox/CONSTRUCTS.md, "Hands"): each press does one
/// thing, sent to the server, which checks it all again. LMB sets the held part into a lit storage slot, else takes the stored
/// or loose part under the crosshair; RMB drops what the hands hold where the crosshair is.
/// </summary>
public static partial class Program
{
    /// <summary>The look line on something loose, or a wrecked part lying alone: "Lamp - LMB: pick up". A refusal replaces it (the aim's reason).</summary>
    /// <summary>The look line on a loose tool upgrade: what it gives, and F to fit it, or why it can't be (ConstructEditsUpgrades.cs).</summary>
    private static string UpgradeLook(Game g, Player me, Construct c, ConstructPartDef d, ToolStat stat, byte level)
    {
        var why = g.SandboxHands.UpgradeRefusal(c, me.Id, g.SandboxInfiniteParts, out _, out _);
        return SandboxKeys.Line(d.Name, ToolUpgrades.Current.Gives(stat, level), why, why != null ? null : SandboxKeys.Do(SandboxKeys.Use, "fit to your tools"));
    }

    private static string HandsLook(Construct c, ConstructPartDef d, bool near, bool full, string carriedIn)
    {
        if (ConstructEdits.WreckedAlone(c))
            return SandboxKeys.Line(d.Name, SandboxKeys.Join($"wrecked, {d.Mass:0} kg", carriedIn), "physics gun only");
        if (d.PhysicsOnly)
            return SandboxKeys.Line(d.Name, carriedIn, "physics gun only");
        if (!near)
            return SandboxKeys.Line(d.Name, carriedIn);
        return SandboxKeys.Line(d.Name, carriedIn, full ? "hands full" : null, SandboxKeys.Do(SandboxKeys.Fire, full ? "into the backpack" : "pick up"));
    }

    private static void SandboxHandsSay(Game g, string why)
    {
        g.SandboxSay(why);
        BuildSounds.Play(BuildSound.Refuse, default);
    }

    /// <summary>One frame with the hands out (not in a seat, the mouse captured).</summary>
    private static void SandboxHandsInput(Game g, Player me, in SandboxHand h, Vector3 eye, Vector3 dir, float wall, bool aimed, in ConstructHit hit, bool slotted)
    {
        if (_scrTook)
            return; // (the buttons are the screen's this frame: Program.SandboxPointer.cs)
        if (Pressed(GameAction.Aim))
        {
            if (h.Held == ConstructParts.None && g.Constructs.HoseHeldBy(me.Id) < 0)
                SandboxHandsSay(g, "nothing in your hands");
            else
                g.SandboxSend(CActKind.HandDrop, at: eye + dir * MathF.Min(aimed ? hit.Distance : wall, ConstructBuild.UseReach));
        }
        if (!Pressed(GameAction.Fire) || _swallowClick)
            return;
        if (slotted && ConstructRender.SlotSelected >= 0)
        {
            g.SandboxSend(CActKind.StoreIn, body: hit.Body.Id, part: (ushort)hit.Part, socket: (byte)ConstructRender.SlotSelected);
            return;
        }
        if (!aimed || hit.Part < 0 || hit.Part >= hit.Body.PartCount || hit.Distance > ConstructBuild.UseReach)
        {
            SandboxHandsSay(g, "nothing in reach to pick up");
            return;
        }
        if (hit.SlotPlus1 > 0)
        {
            g.SandboxSend(CActKind.TakeStored, body: hit.Body.Id, part: (ushort)hit.Part, socket: (byte)(hit.SlotPlus1 - 1));
            return;
        }
        var d = hit.Body.DefOf(hit.Part);
        if (!hit.Body.Loose && !ConstructEdits.WreckedAlone(hit.Body))
        {
            SandboxHandsSay(g, g.Constructs.StoreOf(hit.Body, hit.Part) != null ? $"{SandboxKeys.Use}: use the {d.Name}" : $"the {d.Name} is fitted: use the {ConstructBuild.ToolFor(d).Label()}");
            return;
        }
        if (ConstructEdits.HandsRefusal(g.Constructs, hit.Body, AimContested(g, hit.Body, me.Id)) is { } why)
        {
            SandboxHandsSay(g, why);
            return;
        }
        g.SandboxSend(CActKind.HandTake, body: hit.Body.Id);
    }
}
