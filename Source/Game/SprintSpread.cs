using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Sprint inaccuracy: while you sprint, and for a short recovery window after you stop, your spread is multiplied by a
/// per-class factor (snipers, DMRs and LMGs suffer most, shotguns least). The crosshair grows with it because it draws
/// <see cref="Game.CurrentSpread"/>.
/// <para>
/// The state is ticked inside the shared 60 Hz simulation wherever a movement command is simulated (client prediction
/// for the local player, the server for everyone, bots included), so prediction and the server agree. The server also
/// raises the spread a client sends to a sprint floor (<see cref="ServerFloor"/>), so a client can't send a tight cone
/// while sprinting.
/// </para>
/// <para>
/// Composition: the multiplier only scales the base cone (hip/ADS blend after Steady Aim), so everything else that
/// changes spread stays multiplicative on top of it: Steady Aim (x0.65 hip) times sprint gives x0.65 x class factor;
/// attachments change HipSpreadDeg, which this scales; stance and movement terms are applied after it as before.
/// Lightweight / Stalker / Afterburner / Head Start only change speed: sprinting is detected from the Sprint button
/// plus real sprint speed, so a fast walk is never "sprinting". Another system (e.g. a class buff) can compose by
/// multiplying its own factor with <see cref="Mult"/>.
/// </para>
/// </summary>
public static class SprintSpread
{
    /// <summary>Seconds after you stop sprinting until your spread is back to normal (fades linearly).</summary>
    public const float RecoverySeconds = 0.3f;

    /// <summary><see cref="RecoverySeconds"/> in simulation ticks.</summary>
    public const int RecoveryTicks = (int)(RecoverySeconds * World.TickRate + 0.5f);

    /// <summary>Server: ticks of the recovery window it forgives when checking a shot (input and shot packets arrive at different times).</summary>
    public const int ServerToleranceTicks = 6;

    /// <summary>Server: fraction of the honest sprinting hip cone it insists on (slack for timing, aim blend and stance changes).</summary>
    public const float ServerSlack = 0.75f;

    /// <summary>Spread multiplier for a weapon class at full sprint (1 = no penalty).</summary>
    public static float ClassMult(WeaponClass c) => c switch
    {
        WeaponClass.Shotgun => 1.5f,
        WeaponClass.SMG => 2.0f,
        WeaponClass.Pistol => 2.0f,
        WeaponClass.AssaultRifle => 2.75f,
        WeaponClass.DMR => 3.0f,
        WeaponClass.Sniper => 3.0f,
        WeaponClass.LMG => 3.0f,
        _ => 2.5f, // Special
    };

    /// <summary>Spread multiplier for <paramref name="def"/> at full sprint.</summary>
    public static float ClassMult(WeaponDef def) => ClassMult(def.Class);

    /// <summary>Spread multiplier at sprint amount <paramref name="amount"/> (0 = not sprinting, 1 = sprinting).</summary>
    public static float Mult(WeaponDef def, float amount) => 1f + (ClassMult(def) - 1f) * Math.Clamp(amount, 0f, 1f);

    /// <summary>Is this movement command sprinting: Sprint held, standing, and actually moving faster than a walk.</summary>
    public static bool IsSprinting(in MoveState s, Buttons b)
    {
        if ((b & Buttons.Sprint) == 0 || s.Stance != Stance.Stand)
            return false;
        return new Vector2(s.Velocity.X, s.Velocity.Z).Length() > World.WalkSpeed * 1.02f;
    }

    /// <summary>Advances a player's sprint state by one simulated command (call after World.Simulate).</summary>
    public static void Tick(Player p, Buttons b)
    {
        if (!p.Alive)
        {
            p.Sprinting = false;
            p.SprintRecoverTicks = 0;
            return;
        }
        p.Sprinting = IsSprinting(p.Sim, b);
        if (p.Sprinting)
            p.SprintRecoverTicks = RecoveryTicks;
        else if (p.SprintRecoverTicks > 0)
            p.SprintRecoverTicks--;
    }

    /// <summary>0..1: 1 while sprinting, fading to 0 over the recovery window after.</summary>
    public static float Amount(Player p) => p.Sprinting ? 1f : (float)p.SprintRecoverTicks / RecoveryTicks;

    /// <summary>The server's lenient version of <see cref="Amount"/> (forgives <see cref="ServerToleranceTicks"/> of the window).</summary>
    public static float ServerAmount(Player p) =>
        p.Sprinting ? 1f : Math.Clamp((float)(p.SprintRecoverTicks - ServerToleranceTicks) / (RecoveryTicks - ServerToleranceTicks), 0f, 1f);

    /// <summary>
    /// The smallest spread the server accepts from <paramref name="p"/> with <paramref name="def"/> right now.
    /// Sprinting you can't aim down sights, so the floor is the (Steady Aim) hip cone times the class factor, with slack;
    /// in the recovery window it is the usual floor scaled by the lenient sprint amount; otherwise the usual floor.
    /// </summary>
    public static float ServerFloor(Player p, WeaponDef def)
    {
        var floor = def.MinSpreadRad;
        if (p.Sprinting)
            return MathF.Max(floor, def.HipSpreadRad * Perks.SpreadMult(p.Perks, 0f) * ClassMult(def) * ServerSlack);
        return floor * Mult(def, ServerAmount(p));
    }

    /// <summary>The largest spread the server accepts (wide enough for the full sprint penalty; wider only hurts the shooter).</summary>
    public static float ServerMax(WeaponDef def) => def.MaxSpreadRad * ClassMult(def);
}

public sealed partial class Game
{
    /// <summary>The spread the server resolves a shot with: what the shooter sent, held between the sprint-aware floor and cap.</summary>
    internal static float ServerClampSpread(Player shooter, WeaponDef def, float spread) =>
        Math.Clamp(spread, SprintSpread.ServerFloor(shooter, def), SprintSpread.ServerMax(def));

    /// <summary>A bot's shot spread: hip (Steady Aim) or aimed, Easy's extra, times the sprint penalty.</summary>
    internal static float BotShotSpread(Player p, WeaponDef def, bool ads, BotDifficulty skill) =>
        (def.HipSpreadRad * (ads ? def.AdsSpreadMult : Perks.SpreadMult(p.Perks, 0f)) + (skill == BotDifficulty.Easy ? 0.02f : 0f))
        * SprintSpread.Mult(def, SprintSpread.Amount(p)); // Steady Aim; sprinting bots shoot worse

    /// <summary>Self-test: simulates one movement command for <paramref name="p"/> through the server path.</summary>
    internal void SelfTestApplyCmd(Player p, InputCmd cmd) => ApplyCmd(p, cmd);
}
