using Brutal.OpenGlApi;
using static Brutal.OpenGlApi.OpenGl;

namespace CallOfSlop;

/// <summary>
/// The still half of a depth map kept between frames (2026-11-23, the Zero Day works: the sun's map and the eight dark-interior
/// maps were drawn again from every caster cell in reach every frame, standing still or not). The bodies that haven't moved for
/// two frames are drawn into a map of their own, <see cref="Still"/>, again only when what it was drawn from changes (the map's
/// place, the sun, which still bodies are near, their boxes or their poses: <see cref="Key"/>); each frame the live map the
/// shaders read is a copy of it (a depth blit, cheap on the GPU) with the moving bodies drawn over, or is left as it was when
/// nothing moves. <see cref="SunShadows"/> and <see cref="RoomShade"/> each keep one. Render only.
/// </summary>
internal sealed unsafe class ShadowMapCache
{
    /// <summary>The still bodies' map (a 2D texture or an array, as the live one), and the framebuffers the blit reads and writes through.</summary>
    public uint Still;
    private GlFramebuffer _read, _draw;
    private int _size, _layers;
    private bool _array;

    /// <summary>What the still map was drawn from (0: nothing yet); and whether the live map holds more than the still one (moving bodies drawn over it, or it was never copied).</summary>
    public long Key;
    public bool LiveDiffers = true;

    /// <summary>Makes (or makes again at a new size) the still map like the live one: a depth texture of <paramref name="size"/>, <paramref name="layers"/> layers when an array.</summary>
    public bool Ensure(int size, int layers, bool array)
    {
        if (Still != 0 && size == _size && layers == _layers && array == _array)
            return true;
        Free();
        (_size, _layers, _array) = (size, layers, array);
        GlTexture tex = default;
        Native.glGenTextures(1, &tex);
        Still = tex.Id;
        var target = array ? GL_TEXTURE_2D_ARRAY : GL_TEXTURE_2D;
        Native.glBindTexture(target, Still);
        if (array)
            Native.glTexImage3D(GL_TEXTURE_2D_ARRAY, 0, (int)GL_DEPTH_COMPONENT32F, size, size, layers, 0, GL_DEPTH_COMPONENT, GL_FLOAT, null);
        else
            Native.glTexImage2D(GL_TEXTURE_2D, 0, (int)GL_DEPTH_COMPONENT32F, size, size, 0, GL_DEPTH_COMPONENT, GL_FLOAT, null);
        Native.glTexParameteri(target, GL_TEXTURE_MIN_FILTER, (int)GL_NEAREST);
        Native.glTexParameteri(target, GL_TEXTURE_MAG_FILTER, (int)GL_NEAREST);
        Native.glBindTexture(target, 0);
        GlFramebuffer a, b;
        Native.glGenFramebuffers(1, &a);
        Native.glGenFramebuffers(1, &b);
        (_read, _draw) = (a, b);
        Key = 0;
        LiveDiffers = true;
        return true;
    }

    /// <summary>Binds <paramref name="fbo"/> to draw into layer <paramref name="layer"/> of the still map (a 2D map: layer 0).</summary>
    public void TargetStill(GlFramebuffer fbo, int layer)
    {
        Native.glBindFramebuffer(GL_FRAMEBUFFER, fbo);
        if (_array)
            Native.glFramebufferTextureLayer(GL_FRAMEBUFFER, GL_DEPTH_ATTACHMENT, Still, 0, layer);
        else
            Native.glFramebufferTexture2D(GL_FRAMEBUFFER, GL_DEPTH_ATTACHMENT, GL_TEXTURE_2D, Still, 0);
    }

    /// <summary>The still map's layer <paramref name="layer"/> copied over the live map's (<paramref name="live"/>). Leaves the read and draw framebuffers unbound.</summary>
    public void CopyToLive(uint live, int layer)
    {
        Native.glBindFramebuffer(GL_READ_FRAMEBUFFER, _read);
        Native.glBindFramebuffer(GL_DRAW_FRAMEBUFFER, _draw);
        if (_array)
        {
            Native.glFramebufferTextureLayer(GL_READ_FRAMEBUFFER, GL_DEPTH_ATTACHMENT, Still, 0, layer);
            Native.glFramebufferTextureLayer(GL_DRAW_FRAMEBUFFER, GL_DEPTH_ATTACHMENT, live, 0, layer);
        }
        else
        {
            Native.glFramebufferTexture2D(GL_READ_FRAMEBUFFER, GL_DEPTH_ATTACHMENT, GL_TEXTURE_2D, Still, 0);
            Native.glFramebufferTexture2D(GL_DRAW_FRAMEBUFFER, GL_DEPTH_ATTACHMENT, GL_TEXTURE_2D, live, 0);
        }
        Native.glBlitFramebuffer(0, 0, _size, _size, 0, 0, _size, _size, GL_DEPTH_BUFFER_BIT, GL_NEAREST);
        Native.glBindFramebuffer(GL_READ_FRAMEBUFFER, default);
        Native.glBindFramebuffer(GL_DRAW_FRAMEBUFFER, default);
    }

    public void Free()
    {
        if (Still != 0)
        {
            var t = new GlTexture(Still);
            Native.glDeleteTextures(1, &t);
            Still = 0;
        }
        if (_read.Id != 0)
        {
            var a = _read;
            var b = _draw;
            Native.glDeleteFramebuffers(1, &a);
            Native.glDeleteFramebuffers(1, &b);
            _read = _draw = default;
        }
        Key = 0;
        LiveDiffers = true;
    }
}
