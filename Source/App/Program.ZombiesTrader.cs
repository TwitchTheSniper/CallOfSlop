using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The trader in the world and his shelf over the match (Source/Game/ZombiesTrader.cs): a tall hatted figure in a long
/// coat behind a wooden counter with his wares on it, a lantern on a pole at its end, and a thin gold beam over him so
/// he's found from across the map (brighter in his first seconds). The interact key at the counter opens the shelf
/// (UiTrader.cs); Esc or walking away closes it. An original look: no brands, no borrowed faces.
/// </summary>
public static partial class Program
{
    private static readonly Vector3 TraderCoat = new(0.16f, 0.3f, 0.32f), TraderCoatDark = new(0.12f, 0.22f, 0.24f), TraderHat = new(0.2f, 0.14f, 0.1f),
        TraderSkin = new(0.8f, 0.68f, 0.55f), TraderScarf = new(0.6f, 0.15f, 0.12f), TraderWood = new(0.42f, 0.3f, 0.18f), TraderLantern = new(1f, 0.8f, 0.35f);

    /// <summary>The trader and his stall at his spot (nothing when he's away).</summary>
    private static void DrawTrader(double now)
    {
        var tr = _game.ZTrader;
        if (!tr.Here)
            return;
        var t = (float)now;
        var rot = Matrix4x4.CreateRotationY(-tr.Yaw);
        var at = tr.At;
        Matrix4x4 L(float sx, float sy, float sz, float x, float y, float z) => Matrix4x4.CreateScale(sx, sy, sz) * Matrix4x4.CreateTranslation(x, y, z) * rot * Matrix4x4.CreateTranslation(at);

        // The counter (in front of him: local -z is the way he faces) and the wares on it.
        Gfx.Draw(L(1.6f, 0.06f, 0.55f, 0, 0.9f, -0.55f), TraderWood);
        Gfx.Draw(L(1.5f, 0.84f, 0.45f, 0, 0.44f, -0.55f), TraderWood * 0.8f);
        Gfx.Draw(L(1.52f, 0.05f, 0.47f, 0, 0.62f, -0.55f), TraderCoatDark); // a strap of dark cloth round it
        for (var k = 0; k < tr.Count && k < 5; k++)
        {
            var item = tr.Stock[k];
            var c = item.Kind == ZTraderItemKind.Part && item.Part >= 0 && item.Part < Parts.All.Length ? PartDef.RarityColor(Parts.All[item.Part].Rarity)
                : item.Kind == ZTraderItemKind.Grenades ? new Vector3(0.35f, 0.4f, 0.3f) : new Vector3(0.75f, 0.65f, 0.3f);
            var sold = item.SoldOut;
            Gfx.Draw(L(0.18f, sold ? 0.04f : 0.16f, 0.16f, -0.6f + k * 0.3f, sold ? 0.95f : 1.01f, -0.5f), sold ? c * 0.35f : c, 1f, mode: sold ? 0 : 2);
        }

        // The lantern pole at the counter's end, its flame flickering.
        var flicker = 0.85f + 0.15f * MathF.Sin(t * 13f) * MathF.Sin(t * 7.3f);
        Gfx.Draw(L(0.05f, 1.7f, 0.05f, 0.9f, 0.85f, -0.55f), new Vector3(0.2f, 0.2f, 0.22f));
        Gfx.Draw(L(0.16f, 0.22f, 0.16f, 0.9f, 1.72f, -0.55f), new Vector3(0.25f, 0.25f, 0.27f));
        Gfx.Draw(L(0.1f, 0.14f, 0.1f, 0.9f, 1.72f, -0.55f), TraderLantern * flicker, 1f, mode: 2);

        // The man: coat, body, scarf, arms (one resting on the counter), head and the wide hat; a slow breath.
        var breath = 0.015f * MathF.Sin(t * 1.6f);
        Gfx.Draw(L(0.56f, 0.62f, 0.34f, 0, 0.31f, 0.15f), TraderCoatDark);
        Gfx.Draw(L(0.52f, 0.74f, 0.3f, 0, 0.98f + breath, 0.15f), TraderCoat);
        Gfx.Draw(L(0.3f, 0.12f, 0.2f, 0, 1.38f + breath, 0.1f), TraderScarf);
        Gfx.Draw(L(0.14f, 0.6f, 0.14f, 0.36f, 1.0f + breath, 0.15f), TraderCoat);
        Gfx.Draw(L(0.14f, 0.14f, 0.5f, -0.36f, 0.98f, -0.1f), TraderCoat); // the left arm out to the counter
        Gfx.Draw(L(0.12f, 0.08f, 0.12f, -0.36f, 1.0f, -0.4f), TraderSkin);
        Gfx.Draw(L(0.26f, 0.3f, 0.26f, 0, 1.6f + breath, 0.15f), TraderSkin);
        Gfx.Draw(L(0.28f, 0.1f, 0.06f, 0, 1.52f + breath, 0.02f), new Vector3(0.45f, 0.4f, 0.35f)); // a grey beard
        Gfx.Draw(L(0.66f, 0.05f, 0.66f, 0, 1.78f + breath, 0.15f), TraderHat);
        Gfx.Draw(L(0.3f, 0.22f, 0.3f, 0, 1.9f + breath, 0.15f), TraderHat);
        Gfx.Draw(L(0.32f, 0.05f, 0.32f, 0, 1.83f + breath, 0.15f), TraderScarf * 0.8f); // the hat band

        // The waypoint beam: a thin gold column over the stall, brighter in his first seconds, seen through the room.
        var fresh = (float)Math.Clamp(1 - (now - tr.ChangedAt) / 12.0, 0, 1);
        var glow = 0.18f + 0.5f * fresh * (0.6f + 0.4f * MathF.Abs(MathF.Sin(t * 4f)));
        Gfx.Draw(L(0.07f, 7f, 0.07f, 0, 4.5f, -0.2f), TraderLantern, glow, mode: 2);
    }

    // ---- The shelf ------------------------------------------------------------------------------------------

    /// <summary>The interact key at the trader opens his shelf (and frees the mouse for it).</summary>
    private static void TraderInput(bool interactPressed)
    {
        if (interactPressed && _game.Local is { } me && _game.FindZUse(me) is { Kind: ZUseKind.Trader, Blocked: false })
        {
            Ui.OpenTrader();
            SetMouseCaptured(false);
        }
    }

    /// <summary>The shelf over the match; it closes (and the mouse goes back to aiming) when you go down, die, walk away or he leaves.</summary>
    private static void DrawTraderInMatch(double now)
    {
        if (!Ui.TraderOpen)
            return;
        var gone = _session == null || _game.Mode != GameMode.Zombies || _game.Local is not { Alive: true } me || _game.IsDowned(me) || !_game.AtTrader(me);
        if (!gone)
            Ui.DrawTrader(_game, now);
        if (gone || !Ui.TraderOpen)
        {
            Ui.CloseTrader();
            if (!Ui.PauseOpen && !Ui.WorkbenchOpen)
                SetMouseCaptured(true);
            _swallowClick = true;
        }
    }
}
