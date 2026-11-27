using System.Numerics;
using System.Text.Json.Nodes;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using CallOfSlop.ShipDesigner;

namespace CallOfSlop;

/// <summary>
/// The Ship Designer's quick wheel (Karl, 2026-11-27): hold Mouse 4 (the back side button, as the Sandbox tool wheel's
/// default) in the yard and a radial <see cref="RadialMenu"/> opens round the pointer (the Sandbox wheels' look); flick
/// towards a slice and let go to pick it, let go in the middle (or Esc) to cancel. The middle button only orbits, as before
/// the wheel (left or middle drag turns the view, right drag pans, the wheel zooms).
/// Slices: the four modes (Hull, Pieces, Interior, Connectors), then Door and Ladder (the connector tools) and the most used
/// pieces (Airlock, Window, Seat, Cargo hatch, Boarding ramp, Floodlight M). A piece is carried on the pointer: click the
/// hull to place it, click off the ship or Esc to drop it.
/// </summary>
public sealed partial class Ui
{
    private const ImGuiMouseButton SdWheelButton = (ImGuiMouseButton)3; // Mouse 4 (ImGui's first extra button)

    private static readonly (string Label, string Short, string Hint)[] SdWheelSlots =
    [
        ("Hull", "Hull", "hull bodies and stations"),
        ("Pieces", "Pieces", "drag pieces onto the hull"),
        ("Interior", "Interior", "rooms and decks"),
        ("Connectors", "Connec-\ntors", "doors, ladders and lifts"),
        ("Door", "Door", "click a room, then the room it opens into"),
        ("Ladder", "Ladder", "click a deck: it runs to the deck above"),
        ("Airlock", "Airlock", "carried: click the hull to place it"),
        ("Window", "Window", "carried: click the hull to place it"),
        (DesignSeats.Kind, "Seat", "carried: click the hull by a room"),
        ("Cargo hatch", "Cargo\nhatch", "carried: click the hull by a hold"),
        ("Boarding ramp", "Boarding\nramp", "carried: click the hull's belly"),
        ("Floodlight M", "Flood-\nlight", "carried: click the hull to place it"),
    ];

    private static readonly string[] SdWheelModes = ["hull", "pieces", "interior", "connectors"];

    private RadialMenu? _sdWheelMenu;
    private RadialIcon? _sdWheelIcons;
    private float2 _sdWheelAt;

    private RadialMenu SdWheel
    {
        get
        {
            if (_sdWheelMenu != null)
                return _sdWheelMenu;
            var m = new RadialMenu(SdWheelSlots.Length) { Title = "Designer", Help = "flick to a slice, let go" };
            for (var i = 0; i < SdWheelSlots.Length; i++)
            {
                var it = m.Items[i];
                it.Label = SdWheelSlots[i].Label;
                it.Hint = SdWheelSlots[i].Hint;
                it.Icon = i;
            }
            _sdWheelIcons = SdWheelIcon;
            return _sdWheelMenu = m;
        }
    }

    /// <summary>A slice's "icon": its short name in the slice, centred (the strings are made once).</summary>
    private static void SdWheelIcon(ImDrawListPtr dl, int icon, float2 c, float r, ImColor8 col, ImColor8 bg)
    {
        if ((uint)icon >= (uint)SdWheelSlots.Length)
            return;
        var s = SdWheelSlots[icon].Short;
        var sz = ImGui.CalcTextSize(s);
        var p = new float2(c.X - sz.X * 0.5f, c.Y - sz.Y * 0.5f);
        ImDrawListExtensions.AddText(dl, in p, col, s);
    }

    private bool SdWheelOpen => _sdWheelMenu is { IsOpen: true };

    /// <summary>Mouse 4 went down in the free yard: the wheel opens round the pointer.</summary>
    private void SdWheelDown(float2 mouse) => SdWheelOpenAt(mouse);

    /// <summary>One frame of the open wheel; true while it holds the mouse (the yard ignores it then).</summary>
    private bool SdWheelFrame(ImGuiIOPtr io, float2 mouse)
    {
        if (!SdWheelOpen)
            return false;
        var w = SdWheel;
        if (ImGui.IsMouseDown(SdWheelButton))
        {
            w.Move(new Vector2(io.MouseDelta.X, io.MouseDelta.Y), MathF.Max(io.DeltaTime, 1e-4f));
            return true;
        }
        var pick = w.Release();
        if (pick >= 0)
            SdWheelPick(pick);
        return true;
    }

    private void SdWheelOpenAt(float2 at)
    {
        var w = SdWheel;
        _sdWheelAt = at;
        w.Current = _roomView == null ? Array.IndexOf(SdWheelModes, _sdMode) : -1;
        for (var i = 0; i < SdWheelSlots.Length; i++)
        {
            var it = w.Items[i];
            it.Enabled = _sdLoaded && (i < 4 || Arr(S, "hulls").Any());
            it.Note = it.Enabled ? "" : "add a hull body first";
        }
        w.Open();
        _sdOrbiting = _sdPanning = false;
    }

    /// <summary>Draws the open wheel round where Mouse 4 went down (kept on screen).</summary>
    private void SdWheelDraw()
    {
        if (!SdWheelOpen)
            return;
        var w = _sdWheelMenu!;
        var size = ImGui.GetIO().DisplaySize;
        var sc = w.FitScale(_scale, size.Y);
        var edge = (RadialMenu.OuterEdge + 40f) * sc;
        var c = new float2(Math.Clamp(_sdWheelAt.X, edge, MathF.Max(edge, size.X - edge)), Math.Clamp(_sdWheelAt.Y, edge, MathF.Max(edge, size.Y - edge)));
        w.Draw(ImGui.GetForegroundDrawList(), c, sc, _sdWheelIcons);
    }

    /// <summary>Shuts the wheel and any piece it put on the pointer (Esc, the screen closing).</summary>
    private void SdWheelReset()
    {
        if (SdWheelOpen)
            _sdWheelMenu!.Close();
        SdCarryCancel();
    }

    private bool SdCarrying => _paletteDrag is { Carry: true };

    private void SdCarryCancel()
    {
        if (!SdCarrying)
            return;
        var p = _paletteDrag!;
        p.Off = true;
        if (_sd != null)
            PaletteDrop(); // (off the ship: a placed preview is taken off again)
        else
            _paletteDrag = null;
    }

    private void SdWheelPick(int i)
    {
        if (!_sdLoaded)
            return;
        SdCarryCancel();
        if (i < 4)
        {
            SetMode(SdWheelModes[i]);
            return;
        }
        var label = SdWheelSlots[i].Label;
        if (label == "Door")
        {
            if (_sdMode != "connectors" || _roomView != null)
                SetMode("connectors");
            _placingRiser = null;
            _doorKind = "Door";
            if (_selKind == "room" && _selId != 0)
            {
                _doorFrom = _selId;
                _sdTip = "Click the room the door opens into.";
            }
            else
            {
                _doorFrom = -1; // (the first click picks the room it starts from: UiShipDesignerInput's connectors pointer)
                _sdTip = "Click the room the door starts from, then the room it opens into.";
            }
            return;
        }
        if (label == "Ladder")
        {
            if (_sdMode != "connectors" || _roomView != null)
                SetMode("connectors");
            _doorFrom = 0;
            _placingRiser = ("Ladder", 1, 0);
            _sdTip = "Click this deck where the ladder goes: it runs to the deck above.";
            return;
        }
        if (_sdMode != "pieces" || _roomView != null)
            SetMode("pieces");
        PaletteStart(label);
        _paletteDrag!.Carry = true;
        _sdTip = $"Click the hull to place the {label}. A click off the ship or Esc drops it.";
    }
}
