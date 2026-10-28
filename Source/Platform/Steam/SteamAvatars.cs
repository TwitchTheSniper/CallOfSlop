using Brutal.OpenGlApi;
using static Brutal.OpenGlApi.OpenGl;

namespace CallOfSlop.Platform.Steam;

/// <summary>
/// Steam avatars as GL textures for the menus (the lobby room's player list draws one before a Steam player's name).
/// One texture per Steam user, made on first use from <see cref="SteamPlatform.Friends"/>' RGBA and dropped when
/// Steam says the avatar changed. Only ever called from the render thread with a GL context; nothing here runs
/// headless (the self-test checks the RGBA side in Friends).
/// </summary>
public static unsafe class SteamAvatars
{
    private static readonly Dictionary<ulong, uint> _textures = new();
    private static bool _hooked;

    /// <summary>The GL texture of a Steam user's small avatar, or 0 while it is unknown or loading.</summary>
    public static uint Texture(ulong steamId)
    {
        if (steamId == 0 || !SteamPlatform.Available)
            return 0;
        if (!_hooked)
        {
            SteamPlatform.Friends.Changed += Drop;
            _hooked = true;
        }
        if (_textures.TryGetValue(steamId, out var tex))
            return tex;
        var avatar = SteamPlatform.Friends.AvatarOf(steamId);
        if (avatar == null)
            return 0;
        tex = Upload(avatar);
        if (tex != 0)
            _textures[steamId] = tex;
        return tex;
    }

    private static uint Upload(SteamAvatar avatar)
    {
        GlTexture tex = default;
        Native.glGenTextures(1, &tex);
        if (tex.Id == 0)
            return 0;
        Native.glBindTexture(GL_TEXTURE_2D, tex.Id);
        Native.glPixelStorei(GL_UNPACK_ALIGNMENT, 1);
        fixed (byte* p = avatar.Rgba)
            Native.glTexImage2D(GL_TEXTURE_2D, 0, (int)GL_RGBA8, avatar.Width, avatar.Height, 0, GL_RGBA, GL_UNSIGNED_BYTE, p);
        Native.glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, (int)GL_LINEAR);
        Native.glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, (int)GL_LINEAR);
        Native.glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, (int)GL_CLAMP_TO_EDGE);
        Native.glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, (int)GL_CLAMP_TO_EDGE);
        Native.glBindTexture(GL_TEXTURE_2D, 0);
        return tex.Id;
    }

    private static void Drop(ulong steamId)
    {
        if (!_textures.Remove(steamId, out var id))
            return;
        var tex = new GlTexture(id);
        Native.glDeleteTextures(1, &tex);
    }

    /// <summary>Frees every texture (the window is going away).</summary>
    public static void Dispose()
    {
        foreach (var id in _textures.Values)
        {
            var tex = new GlTexture(id);
            Native.glDeleteTextures(1, &tex);
        }
        _textures.Clear();
    }
}
