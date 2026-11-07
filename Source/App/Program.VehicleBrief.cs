using System.Numerics;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using CallOfSlop.VehicleKit;

namespace CallOfSlop;

/// <summary>
/// The behaviour brief's dev overlay (docs/vehiclekit/VEHICLEKIT.md, "Behaviour data"): in a dev build, on the host, with the look-at inspector open (F6), the faction
/// vehicle under the crosshair shows what its brief says: each gun's clear bearings as spokes round its pivot (green clear at level, amber only lifted), the engagement
/// range as a ring on the ground, its weak points (red hittable, grey covered), the side to keep away, and the brief's text. The brief lives on the server, so a client
/// sees nothing. Closed: a few checks a frame.
/// </summary>
public static unsafe partial class Program
{
    private const float BriefLookRange = 300f, SpokeLength = 4f;

    private static ushort _briefBody;
    private static int _briefStamp = int.MinValue;
    private static List<string> _briefLines = new();

    private static void DrawBriefOverlay()
    {
        if (BuildInfo.IsRelease || !Ui.LookAtOpen || _session == null || _game is not { IsServer: true, SandboxOn: true } g || _devFwd == Vector3.Zero)
            return;
        if (!g.Constructs.Raycast(_devEye, _devFwd, BriefLookRange, out var hit, look: true) || hit.Body is not { Brief: { } b } c)
            return;
        b.Refresh(c);
        if (c.Id != _briefBody || c.RebuildStamp != _briefStamp)
        {
            (_briefBody, _briefStamp) = (c.Id, c.RebuildStamp);
            _briefLines = b.Describe(c);
        }
        var dl = ImGui.GetForegroundDrawList();
        var size = ImGui.GetIO().DisplaySize;
        var vp = _devViewProj;
        bool Screen(Vector3 world, out float2 at)
        {
            var ok = Hud.Project(vp, world, size.X, size.Y, out var s, out _, 2000f);
            at = new float2(s.X, s.Y);
            return ok;
        }
        void Line(Vector3 a, Vector3 e, float4 colour, float width)
        {
            if (Screen(a, out var sa) && Screen(e, out var se))
                dl.AddLine(sa, se, ImGui.GetColorU32(colour), width);
        }
        Vector3 Dir(float bearing) => Vector3.Normalize(Vector3.Transform(b.Way(bearing), c.Rot));
        // Guns: a spoke for each bin it reaches clear.
        var green = new float4(0.35f, 0.95f, 0.45f, 0.9f);
        var amber = new float4(1f, 0.75f, 0.2f, 0.75f);
        for (var k = 0; k < b.Guns.Length; k++)
        {
            ref readonly var gun = ref b.Guns[k];
            var at = c.ToWorld(gun.Pivot);
            for (var bin = 0; bin < VehicleBrief.Bins; bin++)
            {
                var clear = (gun.Clear >> bin & 1) != 0;
                if (!clear)
                    continue;
                var level = (gun.Level >> bin & 1) != 0;
                Line(at, at + Dir(VehicleBrief.BinBearing(bin)) * SpokeLength, level ? green : amber, level ? 2f : 1.2f);
            }
            if (Screen(at, out var label))
                dl.AddText(label, ImGui.GetColorU32(new float4(1, 1, 1, 0.95f)), $"gun {k}{(VehicleBrief.Has(c, gun.Part) ? "" : " (out)")}");
        }
        // The engagement range: a ring on the ground round the vehicle.
        var centre = c.WorldCom;
        var down = Vector3.Transform(-Vector3.UnitY, c.Rot);
        var ground = centre + down * b.ComHeight;
        if (b.Engage > 0)
        {
            const int Segments = 48;
            var ring = new float4(0.45f, 0.75f, 1f, 0.7f);
            for (var s = 0; s < Segments; s++)
                Line(ground + Dir(s * 360f / Segments) * b.Engage, ground + Dir((s + 1) * 360f / Segments) * b.Engage, ring, 1.5f);
            Line(ground, ground + Dir(VehicleBrief.BinBearing(b.BestBin)) * b.Engage, ring, 2.5f); // (the best bearing to present)
        }
        // Weak points.
        for (var k = 0; k < b.Weak.Length; k++)
        {
            ref readonly var w = ref b.Weak[k];
            if (!VehicleBrief.Has(c, w.Part) || !Screen(c.ToWorld(w.Part.At), out var p))
                continue;
            var col = ImGui.GetColorU32(w.Bearings != 0 || w.Top ? new float4(1f, 0.3f, 0.25f, 0.95f) : new float4(0.6f, 0.6f, 0.62f, 0.8f));
            dl.AddLine(new float2(p.X - 6, p.Y - 6), new float2(p.X + 6, p.Y + 6), col, 2f);
            dl.AddLine(new float2(p.X - 6, p.Y + 6), new float2(p.X + 6, p.Y - 6), col, 2f);
            dl.AddText(new float2(p.X + 8, p.Y - 7), col, w.Kind);
        }
        // The side to keep away: an arrow out of that side.
        if (b.KeepAway < 4)
        {
            var way = Dir(b.KeepAway * 90f);
            var from = ground + way * (b.KeepAway % 2 == 0 ? b.Length : b.Width) * 0.6f;
            var red = new float4(1f, 0.35f, 0.3f, 0.9f);
            Line(from, from + way * 3, red, 3f);
            if (Screen(from + way * 3.3f, out var tip))
                dl.AddText(tip, ImGui.GetColorU32(red), "keep away");
        }
        // The brief in words, down the left.
        var y = size.Y * 0.25f;
        var text = ImGui.GetColorU32(new float4(1, 1, 1, 0.92f));
        var shade = ImGui.GetColorU32(new float4(0, 0, 0, 0.8f));
        foreach (var line in _briefLines)
        {
            dl.AddText(new float2(13, y + 1), shade, line);
            dl.AddText(new float2(12, y), text, line);
            y += 15;
            if (y > size.Y - 20)
                break;
        }
    }
}
