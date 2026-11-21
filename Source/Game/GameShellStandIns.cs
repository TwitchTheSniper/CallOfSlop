using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Robot shells nobody wears, drawn as the robot a wearer is (the user, 2026-11-20: "the unpossessed shells should look the same as the
/// possessed shells"; ConstructRenderShellRigs.cs lists them while it draws the bodies). Each is a stand-in <see cref="Player"/> in a shell:
/// its frame (and so its size), the modules fitted in its part's settings, its wear (a failed leg limps) and its hopper's load, standing
/// still in the part's frame, its hands out and empty, in its owner's colour as they would wear it. Stand-ins take ids past
/// <see cref="MaxPlayers"/> (no person has one), so their legs and arms keep their own state; one per rig slot, made once: nothing
/// allocates in a steady frame.
/// </summary>
public sealed partial class Game
{
    private readonly Player?[] _shStands = new Player?[ConstructRender.ShellRigMax];

    /// <summary>Every PC, after the bodies are drawn (Program.DrawSandbox): the shells <see cref="ConstructRender"/> left to be drawn as rigs.</summary>
    public void DrawStandingShells(Renderer gfx, double time)
    {
        Span<byte> kit = stackalloc byte[ShellFrames.SlotCount];
        for (var k = 0; k < ConstructRender.ShellRigCount; k++)
        {
            if (Constructs.Get(ConstructRender.ShellRigBody[k]) is not { } c || ConstructRender.ShellRigPart[k] >= c.PartCount)
                continue;
            var i = ConstructRender.ShellRigPart[k];
            ref var part = ref c.Parts[i];
            var d = part.D;
            var b = _shStands[k] ??= new Player();
            var owner = Players.GetValueOrDefault(c.Owner);
            // The owner's colour as they'd wear it (Player.Color: their team's, or their look's on their palette slot).
            b.Id = (byte)(MaxPlayers + (owner?.Id ?? c.Owner) % Player.Palette.Length + Player.Palette.Length * (k % ((256 - MaxPlayers) / Player.Palette.Length)));
            b.Team = owner?.Team ?? NoTeam;
            b.Look = owner?.Look ?? default;
            b.Name = owner?.Name ?? "";
            b.Alive = true;
            b.InShell = true;
            var frame = ShellFrames.FrameOf(d);
            ShellFrames.ReadKit(part, d, kit);
            if (b.ShellFrame != frame || !kit.SequenceEqual(b.ShellMods))
            {
                b.ShellFrame = frame;
                kit.CopyTo(b.ShellMods);
                ShellFrames.Refit(b); // (only when it changes: a Shell Gantry's swap)
            }
            ShellWear.Read(part, d, ref b.ShellWear);
            b.Health = ShellWear.HealthOf(part, d);
            if (ShellFrames.Worn(b).Find(ShellModuleKind.Hopper) != null && Constructs.StoreOf(c, i) is { Amount: > 0 } st)
                (b.HopperMaterial, b.HopperAmount) = (st.Material, (ushort)Math.Min(st.Amount, ushort.MaxValue));
            else
                (b.HopperMaterial, b.HopperAmount) = (0, 0);
            // Standing in the part's frame (its feet on its origin, -Z its front), as a rider is drawn in a construct's: no foot rays,
            // which would land its feet on its own collider.
            ref readonly var m = ref ConstructRender.ShellRigFrame[k];
            b.RenderPosition = m.Translation;
            b.RenderRide = true;
            b.RenderRideRot = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(m));
            b.RenderRideYaw = 0;
            b.RenderRideFeet = Vector3.Zero;
            b.RenderYaw = b.RenderPitch = b.RenderLookYaw = 0;
            b.RenderVelocity = Vector3.Zero;
            b.RenderGrounded = true;
            b.RenderMantling = false;
            b.RenderStance = Stance.Stand;
            b.RenderSeat = SeatKind.None;
            b.RenderLadder = b.RenderHandsUp = false;
            ScenePresentation.DrawShellStand(gfx, b, k, time);
        }
    }
}
