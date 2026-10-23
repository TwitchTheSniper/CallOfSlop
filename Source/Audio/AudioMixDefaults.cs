namespace CallOfSlop;

/// <summary>
/// Every audio source category the mixer (Settings > Audio > Open audio mixer, or F8) has a level slider for. Sounds are
/// sorted into these when they start (<see cref="Audio.BusFor"/>); the order is the order of the level rows in
/// <see cref="AudioMixDefaults.Entries"/>. New buses go before <see cref="COUNT"/>, anywhere: the JSON file uses the keys, not the numbers.
/// </summary>
public enum MixBus : byte
{
    Master,
    GunOwn,
    GunOwnSuppressed,
    GunOtherNear,
    GunOtherFar,
    GunOtherSuppressed,
    GunFrenzyLayer,
    GunMechanism,
    LauncherOwn,
    LauncherOther,
    LauncherLock,
    Explosion,
    BulletImpact,
    BulletWhizz,
    PropBreak,
    Gore,
    BodyHitWorld,
    HitMarker,
    Headshot,
    KillConfirm,
    FriendlyHit,
    Hurt,
    StepOwn,
    StepOther,
    FoleyOwn,
    FoleyOther,
    Melee,
    Tactical,
    Landmine,
    Streaks,
    Zombies,
    CoopEnemies,
    Ui,
    Notify,
    Voice,
    MusicMenu,
    MusicLobby,
    Frenzy1,
    Frenzy2,
    Frenzy3,
    Frenzy4,
    Frenzy5,
    Frenzy6,
    COUNT,

    /// <summary>"Work it out from the sound" (<see cref="Audio.BusFor"/>): the default for the Effect calls.</summary>
    Auto = 255,
}

/// <summary>The gun-mix shape values the mixer's Distance group exposes (rows after the levels in <see cref="AudioMixDefaults.Entries"/>).</summary>
public enum MixParam : byte
{
    ShotReference,
    ShotRolloff,
    ShotMax,
    BassCut,
    BassFloor,
    AirAbsorption,
    AirFloor,
    FarStart,
    FarEnd,
    LimiterCeiling,
    COUNT,
}

/// <summary>One mixer row: a level (dB, with mute) or a shape value, under a stable JSON key.</summary>
/// <param name="Key">The stable name in audio_mix.json (never rename one: old files would lose that value).</param>
/// <param name="Group">The heading it sits under in the window.</param>
/// <param name="Default">The built-in value: dB for a level, the value itself for a shape row.</param>
/// <param name="Format">printf format for the slider (levels: dB).</param>
public readonly record struct MixEntry(string Key, string Group, string Label, float Default, float Min, float Max, string Format, string Help, bool IsLevel);

/// <summary>
/// THE table of built-in mix values: every level (dB; 0 = the voice as tuned before the mixer) and every gun-mix shape
/// value, under the same keys as <c>audio_mix.json</c>. To bake a player's mix into the game, paste each <c>"db"</c> /
/// shape value from their file into <see cref="MixEntry.Default"/> here, set <see cref="BakedMix"/> and update the
/// self-test's expected table (see README, "Audio mixer"). Levels come first, in <see cref="MixBus"/> order, then the
/// shape rows in <see cref="MixParam"/> order (the self-test checks both).
/// <para>
/// Baked in: the user's "SeabMix2" (2026-10-23): guns, explosions and hit feedback up, footsteps down (14 levels; the
/// shape values are the factory ones). A saved audio_mix.json from before keeps the levels the player changed and moves
/// the ones left at their old default to the new one (<see cref="AudioMix.LoadJson"/>).
/// </para>
/// </summary>
public static class AudioMixDefaults
{
    /// <summary>The name of the player mix baked into <see cref="Entries"/> (null = the factory values).</summary>
    public const string? BakedMix = "SeabMix2";

    /// <summary>
    /// Slider range of every level row (dB). It was -40..+12 before 2026-10-23: a saved mix inside the old range loads
    /// the same, and a value outside the new range loads clamped (counted in the load report).
    /// </summary>
    public const float MinDb = -50f, MaxDb = 50f;

    /// <summary>
    /// The linear gain of <see cref="MaxDb"/> (+50 dB, about 316x): the highest level a bus can apply. Every place a bus
    /// gain goes through (the voices' AL_MAX_GAIN, the music trims) accepts at least this much.
    /// </summary>
    public static readonly float MaxLevelGain = MathF.Pow(10f, MaxDb / 20f);

    private static MixEntry L(string key, string group, string label, string help, float db = 0f) =>
        new(key, group, label, db, MinDb, MaxDb, "%+.1f dB", help, true);

    private static MixEntry P(string key, string label, float value, float min, float max, string format, string help) =>
        new(key, "Distance (gun mix shape)", label, value, min, max, format, help, false);

    public static readonly MixEntry[] Entries =
    [
        // ---- Levels (MixBus order) ----
        L("master", "Master", "Master", "Everything, music included (on top of Settings > Audio > Master volume)."),
        L("gun.own", "Guns", "Own gun", "Your own gunshots (not launchers). Your gun plays at OpenAL's per-voice cap; a boost (like the default +20 dB) lifts that cap with it.", db: 20f),
        L("gun.own_suppressed", "Guns", "Own gun, suppressed", "Your own gun with a suppressor."),
        L("gun.other_near", "Guns", "Other players' guns (near layer)", "Another player's shot up close; crossfades into the distant layer with distance.", db: 14.1f),
        L("gun.other_far", "Guns", "Distant gunfire layer / tail", "The distant layer of other players' shots (the far report and rolling echo).", db: 10f),
        L("gun.other_suppressed", "Guns", "Other players' suppressed guns", "Another player's suppressed shot (it has no distant layer)."),
        L("gun.frenzy_layer", "Guns", "Frenzy gun-sound layer", "The per-class frenzy layers on your own gun during a kill spree (Settings > Audio > Frenzy gun sound)."),
        L("gun.mechanism", "Guns", "Reload / equip / dry fire", "Reloads (yours and others'), weapon equip and the empty click."),
        L("launcher.own", "Launchers", "Own launcher", "Your own rocket and grenade launcher shots."),
        L("launcher.other", "Launchers", "Other players' launchers + rocket motor", "Other players' launches and a rocket's motor along its flight."),
        L("launcher.lock", "Launchers", "Lock-on tones / detonator", "The lock-on beeps, the locked tone and the detonator's click."),
        L("explosion", "Explosions & impacts", "Explosions", "Grenades, rockets, exploding props and kill streak blasts.", db: 20.1f),
        L("impact.bullet", "Explosions & impacts", "Bullet impacts + ricochets", "Shots hitting the world (by surface), ricochets, door knocks."),
        L("impact.whizz", "Explosions & impacts", "Bullet whizz / crack-by", "Bullets passing close to you.", db: 12.3f),
        L("props.break", "Explosions & impacts", "Breaking props, doors, glass", "Crates, bottles, bins, barrels, doors and windows breaking.", db: 5f),
        L("gore.gibs", "Explosions & impacts", "Gore / gibs squelch", "Bodies and animals bursting into gibs (the plain break sound when gore is off).", db: 7.3f),
        L("hit.world", "Explosions & impacts", "Body hits heard in the world", "Flesh hits and headshot crunches on other players, heard where they happen.", db: 3.9f),
        L("hit.marker", "Hit feedback", "Hit marker (body / metal)", "Your hit on a player (flesh thwack + tick) or a turret/drone (metal ping).", db: 8.4f),
        L("hit.headshot", "Hit feedback", "Headshot", "Your headshot crunch and the headshot-kill accent.", db: 15.7f),
        L("hit.kill", "Hit feedback", "Kill confirm", "The kill chime.", db: 13f),
        L("hit.friendly", "Hit feedback", "Friendly-fire marker", "Hitting a teammate (no damage)."),
        L("player.hurt", "Hit feedback", "Hurt / death / shield", "Being hit, dying, Last Stand's shield blocking.", db: 10f),
        L("step.own", "Movement", "Own footsteps", "Your own steps.", db: -8f),
        L("step.other", "Movement", "Other players' footsteps", "Everyone else's steps (positional).", db: -4.1f),
        L("foley.own", "Movement", "Own jump / land / prone", "Your jump push-off, landing thud and prone crawl."),
        L("foley.other", "Movement", "Others' jump / land / prone", "Other players' jumps, landings and crawling."),
        L("melee", "Movement", "Melee", "Melee swings and hits (yours and others')."),
        L("tactical", "Gear & streaks", "Tactical gear", "Grenade throws and bounces, smoke, recon, flash, stun, trophy, motion sensor, stim, devices, the flash ringing."),
        L("landmine", "Gear & streaks", "Landmine click", "A landmine's pressure-plate click (its blast is an Explosion)."),
        L("streaks", "Gear & streaks", "Kill streaks", "Jets, gunship, hunter drones, sentry turret, mortar whistle, EMP, napalm, supply drop, flares, radio calls."),
        L("zombies", "Gear & streaks", "Zombies", "Zombies hitting you, breaking windows and doors, and being hit."),
        L("coop.enemies", "Gear & streaks", "Co-op enemies (on top)", "Extra level for the enemy soldiers in Co-op missions: their gunfire (after the gun rows above), hits and grenade throws."),
        L("ui", "UI & notifications", "UI / menu clicks", "Menu clicks and small feed ticks (the hit tick too)."),
        L("notify", "UI & notifications", "Notifications", "Level up, respawn, streak ready, mode events (flags, hills, ready-up, zombie rounds and pick-ups). The game has no announcer voice."),
        L("voice", "Voice chat", "Voice chat", "Other players' voices (on top of Settings > Audio > Voice volume)."),
        L("music.menu", "Music", "Menu music", "The main menu's music."),
        L("music.lobby", "Music", "Lobby music", "Music in a session between matches (warmup, ready-up, results, map vote). A live match has no music of its own: in a match the only music is the kill frenzy (rows below)."),
        L("frenzy.1", "Music", "Kill frenzy level 1", "Drums and bass behind a closed filter."),
        L("frenzy.2", "Music", "Kill frenzy level 2", "The full beat."),
        L("frenzy.3", "Music", "Kill frenzy level 3", "Leads, pads and chants come in."),
        L("frenzy.4", "Music", "Kill frenzy level 4", "Full volume, the last drop."),
        L("frenzy.5", "Music", "Kill frenzy level 5 (FRENZY)", "As loud as the menu music."),
        L("frenzy.6", "Music", "Kill frenzy level 6 (MAX FRENZY)", "As loud as the gunfire (then the music limiter)."),

        // ---- Shape (MixParam order) ----
        P("dist.reference_m", "Gunshot reference distance", 5f, 1f, 20f, "%.1f m", "Other players' shots start fading from here (inverse distance)."),
        P("dist.rolloff", "Gunshot rolloff", 0.8f, 0.2f, 2.5f, "%.2f", "How fast other players' shots fade: 1 is the physical inverse-distance law, 0.8 about -5 dB per doubling."),
        P("dist.max_m", "Gunshot max distance", 250f, 50f, 500f, "%.0f m", "Shots stop getting quieter past this distance."),
        P("dist.bass_cut", "Distance bass cut", 1f, 0f, 3f, "%.2fx", "How fast far gunfire loses its boom (low shelf below 250 Hz; 1 = -5 dB at 30 m, 0 = never). Needs EFX."),
        P("dist.bass_floor", "Distance bass floor", 0.15f, 0f, 1f, "%.2f", "The lowest the low shelf goes (gain; 0.15 = -16 dB)."),
        P("dist.air", "Air absorption (treble)", 1f, 0f, 3f, "%.2fx", "How fast every positional sound loses its treble with distance (high shelf; 1 = about 0.4 at 60 m). Needs EFX."),
        P("dist.air_floor", "Air absorption floor", 0.12f, 0f, 1f, "%.2f", "The lowest the high shelf goes (gain)."),
        P("dist.far_start_m", "Near/distant crossfade start", 25f, 0f, 150f, "%.0f m", "Other players' shots start handing over to the distant layer here."),
        P("dist.far_end_m", "Near/distant crossfade end", 90f, 10f, 300f, "%.0f m", "Fully the distant layer from here (kept at least 5 m past the start)."),
        P("music.limiter_ceiling", "Music limiter ceiling (frenzy)", 0.96f, 0.5f, 0.985f, "%.3f", "The music bus limiter's ceiling while the kill frenzy plays (sample level; its safety clipper never passes 0.985)."),
    ];

    /// <summary>The row of a level.</summary>
    public static ref readonly MixEntry Of(MixBus bus) => ref Entries[(int)bus];

    /// <summary>The row of a shape value.</summary>
    public static ref readonly MixEntry Of(MixParam p) => ref Entries[(int)MixBus.COUNT + (int)p];

    /// <summary>
    /// The shape values the game had before the mixer existed (every level was 0 dB then): the identity self-test checks
    /// the gun-mix shape against the pre-mixer formulas while the defaults hold these.
    /// </summary>
    public static readonly float[] FactoryShape = [5f, 0.8f, 250f, 1f, 0.15f, 1f, 0.12f, 25f, 90f, 0.96f];

    /// <summary>True while the table holds the factory values (every level 0 dB and the factory shape: no mix baked in).</summary>
    public static bool IsFactory
    {
        get
        {
            for (var i = 0; i < (int)MixBus.COUNT; i++)
                if (Entries[i].Default != 0f)
                    return false;
            return ShapeIsFactory;
        }
    }

    /// <summary>True while every shape value is the factory one (a baked mix may still change levels).</summary>
    public static bool ShapeIsFactory
    {
        get
        {
            for (var i = 0; i < (int)MixParam.COUNT; i++)
                if (Of((MixParam)i).Default != FactoryShape[i])
                    return false;
            return true;
        }
    }
}
