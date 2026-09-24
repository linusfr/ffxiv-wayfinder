using System.Numerics;

using Dalamud.Game.NativeWrapper;

namespace Wayfinder.UI;

/// <summary>Where a native window is on screen this frame, and how big.</summary>
public readonly record struct AddonGeometry(Vector2 Position, Vector2 Size, float Scale)
{
    public float Bottom => Position.Y + Size.Y;
}

/// <summary>
/// Reads a native window's on-screen rectangle.
///
/// <para>Everything here goes through <see cref="AtkUnitBasePtr"/>, Dalamud's safe
/// wrapper over AtkUnitBase. It exposes exactly the four things an overlay needs —
/// position, scaled size, scale and visibility — without the plugin dereferencing
/// anything itself, so a struct layout change on patch day is Dalamud's problem
/// rather than a crash here.</para>
///
/// <para>This is read during the draw callback, for the frame being drawn. It is not
/// polling: nothing here watches for state changes, and the caller only draws at
/// all when the addon lifecycle has said the window exists.</para>
/// </summary>
internal static class AddonAnchor
{
    /// <summary>
    /// Narrower than this and the window is mid-open, mid-close, or collapsed into a
    /// transition. Drawing against those numbers makes the overlay jump.
    /// </summary>
    private const float MinimumUsableWidth = 80f;

    public static AddonGeometry? Measure(AtkUnitBasePtr addon)
    {
        if (addon.IsNull || !addon.IsReady || !addon.IsVisible)
            return null;

        var size = addon.ScaledSize;
        if (size.X < MinimumUsableWidth || size.Y <= 0f)
            return null;

        var scale = addon.Scale;
        if (scale <= 0f)
            scale = 1f;

        return new AddonGeometry(addon.Position, size, scale);
    }
}
