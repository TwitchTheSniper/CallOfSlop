using System.Numerics;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// Sandbox: the felt-g readout (Karl, 2026-11-24): how many g the player feels and which way, from the server's figure
/// (<see cref="ShipGravity.PersonPull"/>, the owner's snapshot on a client). A small dial left of centre: the arrow is the pull
/// as seen facing forward (right is right, down is down), filled while it presses them backwards, hollow forwards; under it the
/// figure and the way in words. White, amber from <see cref="ShipGravity.SlowG"/> (moving slows), red from
/// <see cref="ShipGravity.PinG"/> (pinned). Shown aboard a vehicle (riding or seated), or anywhere the pull is off 1 g.
/// </summary>
public sealed partial class Ui
{
    private void DrawGForce(Game game, Player local, float2 pos, float2 size, float pad)
    {
        if (!local.Alive || !game.SandboxOn)
            return;
        var cam = Hud.Camera;
        if (!cam.Valid)
            return;
        var seatBody = game.SandboxHands.Hand(local.Id).SeatBody;
        var aboard = seatBody != 0 || local.Sim.RideBody != 0;
        var felt = local.FeltPull;
        var g = felt.Length() / ShipGravity.G;
        if (!aboard && MathF.Abs(g - 1f) < 0.15f)
            return; // (standing on the ground: the world's 1 g, nothing to say)

        // The player's frame: up from the seat's vehicle, the deck they stand on, or the ground; forward their look, levelled.
        var up = seatBody != 0 && game.Constructs.Get(seatBody) is { } seat ? Vector3.Transform(Vector3.UnitY, seat.Rot)
            : local.Sim.RideBody != 0 && local.Sim.RideUpW != Vector3.Zero ? Vector3.Normalize(local.Sim.RideUpW)
            : GravityFrames.UpAt(local.Position);
        var fwd = cam.Forward - up * Vector3.Dot(cam.Forward, up);
        fwd = fwd.LengthSquared() > 1e-6f ? Vector3.Normalize(fwd) : Vector3.Normalize(Vector3.Cross(up, Vector3.UnitX));
        var right = Vector3.Cross(fwd, up);
        float down = -Vector3.Dot(felt, up), ahead = Vector3.Dot(felt, fwd), side = Vector3.Dot(felt, right);

        var col = g >= ShipGravity.PinG ? Red : g >= ShipGravity.SlowG ? Orange : g < ShipGravity.FloatG ? Dim : new float4(1, 1, 1, 1);
        ImGui.SetNextWindowPos(new float2(pos.X + pad, pos.Y + size.Y * 0.62f), ImGuiCond.Always, new float2(0, 0.5f));
        if (ImGui.Begin("##gforce"u8, Overlay))
        {
            var r = 22f * _scale;
            var at = ImGui.GetCursorScreenPos();
            var c = new float2(at.X + r + 2 * _scale, at.Y + r + 2 * _scale);
            var dl = ImGui.GetWindowDrawList();
            var ring = new ImColor8(200, 205, 210, 110);
            ImDrawListExtensions.AddCircleFilled(dl, in c, r, new ImColor8(10, 12, 16, 120), 32);
            ImDrawListExtensions.AddCircle(dl, in c, r, ring, 32, 1.2f * _scale);
            dl.AddLine(new float2(c.X - r, c.Y), new float2(c.X + r, c.Y), new ImColor8(200, 205, 210, 45), 1f);
            dl.AddLine(new float2(c.X, c.Y - r), new float2(c.X, c.Y + r), new ImColor8(200, 205, 210, 45), 1f);
            var ink = new ImColor8((byte)(col.X * 255), (byte)(col.Y * 255), (byte)(col.Z * 255), 235);
            if (g >= ShipGravity.FloatG)
            {
                // The arrow's length grows with the g, full at the pin.
                var flat = new Vector2(side, down);
                var len = flat.Length();
                var reach = r * 0.85f * MathF.Min(1f, g / ShipGravity.PinG);
                var tip = len > 1e-3f ? new float2(c.X + side / len * reach * (len / (g * ShipGravity.G)), c.Y + down / len * reach * (len / (g * ShipGravity.G))) : c;
                dl.AddLine(c, tip, ink, 2.5f * _scale);
                var dot = 4.5f * _scale;
                if (ahead < 0)
                    ImDrawListExtensions.AddCircleFilled(dl, in tip, dot, ink, 12); // (pressed back: into the seat or the aft bulkhead)
                else
                    ImDrawListExtensions.AddCircle(dl, in tip, dot, ink, 12, 1.8f * _scale);
            }
            ImGui.Dummy(new float2(2 * r + 4 * _scale, 2 * r + 4 * _scale));
            ImGui.SameLine(0, 8 * _scale);
            ImGui.BeginGroup();
            ImGui.PushFont(default, 22);
            ImGuiSafe.TextColored(col, $"{g:0.0} g");
            ImGui.PopFont();
            if (g < ShipGravity.FloatG)
                ImGui.TextColored(Dim, "weightless"u8);
            else
            {
                var way = Way(down, ahead, side, out var also);
                var tag = g >= ShipGravity.PinG ? "  PINNED" : "";
                if (also.Length > 0)
                    ImGuiSafe.TextColored(Dim, $"{way}, {also}{tag}");
                else
                    ImGuiSafe.TextColored(Dim, $"{way}{tag}");
            }
            ImGui.EndGroup();
        }
        ImGui.End();
    }

    /// <summary>The pull's way in words, the strongest of down/up, forward/back and left/right, and a second one when it is a third of the first or more.</summary>
    private static string Way(float down, float ahead, float side, out string also)
    {
        float ad = MathF.Abs(down), aa = MathF.Abs(ahead), asd = MathF.Abs(side);
        var first = ad >= aa && ad >= asd ? 0 : aa >= asd ? 1 : 2;
        var firstMag = first == 0 ? ad : first == 1 ? aa : asd;
        var second = -1;
        var secondMag = 0f;
        for (var k = 0; k < 3; k++)
        {
            var m = k == 0 ? ad : k == 1 ? aa : asd;
            if (k != first && m > secondMag)
            {
                second = k;
                secondMag = m;
            }
        }
        also = second >= 0 && secondMag >= firstMag * 0.35f ? Word(second, down, ahead, side) : "";
        return Word(first, down, ahead, side);
    }

    private static string Word(int k, float down, float ahead, float side) => k switch
    {
        0 => down >= 0 ? "down" : "up",
        1 => ahead >= 0 ? "forward" : "back",
        _ => side >= 0 ? "right" : "left",
    };
}
